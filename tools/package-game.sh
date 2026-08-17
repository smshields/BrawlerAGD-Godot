#!/bin/zsh
# Package one built game as discrete, shippable apps (Packaged Games, 2026-08-15 —
# docs/features/packaged-games.md).
#
#   tools/package-game.sh <built-game.json> [--platforms mac,win,linux]
#
# Pipeline: `BrawlerRunner prep-game` gates completeness and applies the namegen
# naming pass → the repo (BrawlerSim, namegen, godot) is rsynced to a temp build
# dir → the embedded game + per-game branding (window title / bundle id from the
# game's name) are written into the copy → the copy is dotnet-built (the headless
# editor needs a loadable project assembly BEFORE export, or its C# export glue
# dies) → headless Godot exports per platform (presets: "macOS packaged" /
# "Windows" / "Linux x86_64") → the mac app is THINNED to arm64 (official mac
# templates ship universal binaries only; MAC_UNIVERSAL=1 skips thinning) and
# deep re-signed — MANDATORY (ad-hoc, or set SIGN_IDENTITY to a Developer ID;
# NOTARIZE=1 additionally submits via notarytool once credentials exist) →
# archives land in dist/packages/<slug>/.
#
# macOS recipients of ad-hoc builds must clear quarantine once after download:
#   xattr -dr com.apple.quarantine <app>   (or right-click → Open)
set -euo pipefail

REPO=$(cd "$(dirname "$0")/.." && pwd)
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"

# TWO dotnet toolchains, deliberately scoped (2026-08-15): the homebrew .NET 8 SDK
# runs BrawlerRunner (prep step only). Godot's export glue spawns the SYSTEM dotnet
# muxer AND passes the child a DOTNET_ROOT it discovered via hostfxr — if this
# shell's DOTNET_ROOT points at the homebrew v8 root (the login profile sets it),
# the child becomes a v10 muxer forced onto the v8 SDK, the NuGet MSBuild SDK
# resolver can't resolve Godot.NET.Sdk (MSB4276), and every export dies with a
# blank "Failed to build project". So: UNSET it globally here and give the prep
# step its homebrew env explicitly.
unset DOTNET_ROOT DOTNET_HOST_PATH 2>/dev/null || true
BREW_DOTNET="/opt/homebrew/opt/dotnet@8/bin/dotnet"
SYS_DOTNET="/usr/local/share/dotnet/dotnet"

GAME_JSON=${1:?"usage: package-game.sh <built-game.json> [--platforms mac,win,linux]"}
PLATFORMS="mac,win,linux"
if [[ "${2:-}" == "--platforms" ]]; then
    PLATFORMS=${3:?"--platforms needs a value like mac,win,linux"}
fi

if pgrep -f "Godot_mono.*--editor" > /dev/null; then
    echo "Close the Godot editor first (exports clash with its build caches)." >&2
    exit 1
fi

BUILD=$(mktemp -d -t brawler-package)
if [[ "${KEEP_BUILD:-0}" == "1" ]]; then
    echo "build dir kept: $BUILD"
else
    trap "rm -rf '$BUILD'" EXIT
fi

# 1. Prep: completeness gate + naming pass; grab name + slug from the output.
PREP=$(DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" "$BREW_DOTNET" run \
    --project "$REPO/BrawlerRunner" -c Release -- \
    prep-game --game "$GAME_JSON" --out "$BUILD/standalone_game.json")
echo "$PREP" | head -1
NAME=$(echo "$PREP" | grep '^name=' | cut -d= -f2-)
SLUG=$(echo "$PREP" | grep '^slug=' | cut -d= -f2-)
OUT="$REPO/dist/packages/$SLUG"
mkdir -p "$OUT"

# 2. A clean project copy: the csproj references ../BrawlerSim and ../namegen,
#    so the copy carries all three (never the repo's runs/, dist/, or caches).
for dir in BrawlerSim namegen godot; do
    rsync -a --exclude .godot --exclude bin --exclude obj "$REPO/$dir" "$BUILD/src/"
done
PROJECT="$BUILD/src/godot"
cp "$BUILD/standalone_game.json" "$PROJECT/standalone_game.json"

# 3. Brand the copy: window title + bundle id from the game name.
python3 - "$PROJECT" "$NAME" "$SLUG" <<'PYEOF'
import sys
project, name, slug = sys.argv[1], sys.argv[2], sys.argv[3]
p = f"{project}/project.godot"
s = open(p).read().replace('config/name="BrawlerAGD-Godot"', f'config/name="{name}"')
open(p, "w").write(s)
p = f"{project}/export_presets.cfg"
s = open(p).read().replace(
    'application/bundle_identifier="com.shieldsgames.brawleragd.packaged"',
    f'application/bundle_identifier="com.shieldsgames.{slug}"')
open(p, "w").write(s)
PYEOF

# 4. Build the copy's project assembly, then import. The headless editor loads
#    C# scripts from .godot/mono/temp/bin/Debug at startup — without this build
#    every script is "not compiling" and export's dotnet publish glue fails.
#    Uses the SAME system dotnet as Godot's export so caches stay consistent.
"$SYS_DOTNET" build "$PROJECT/BrawlerGodot.csproj" -v quiet > "$BUILD/build.log" 2>&1 \
    || { echo "project build failed:" >&2; tail -20 "$BUILD/build.log" >&2; exit 1; }
"$GODOT" --path "$PROJECT" --headless --import > /dev/null 2>&1 || true

# Exports print engine noise; treat "ERROR:" lines or a missing artifact as
# failure — EXCEPT the headless "autoload ... is not compiling" line, which is
# benign editor-context noise (the C# assembly loads late headless; the export
# and the produced app are fine — launch-verified 2026-08-15).
run_export() { # <preset> <output path>
    local log="$BUILD/export.log"
    "$GODOT" --path "$PROJECT" --headless --export-release "$1" "$2" > "$log" 2>&1 || true
    if grep "^ERROR:" "$log" | grep -qv "Failed to create an autoload" || [[ ! -e "$2" ]]; then
        echo "export '$1' FAILED:" >&2
        grep -iE "error" "$log" | head -10 >&2
        exit 1
    fi
}

if [[ "$PLATFORMS" == *mac* ]]; then
    APP="$OUT/$NAME.app"
    rm -rf "$APP"
    run_export "macOS packaged" "$APP"
    # Lean build: official mac templates are universal-only, so thin the engine
    # binary to arm64 and drop the x86_64 .NET payload after the fact.
    if [[ "${MAC_UNIVERSAL:-0}" != "1" ]]; then
        BIN="$APP/Contents/MacOS/$NAME"
        lipo -info "$BIN" | grep -q arm64 || { echo "no arm64 slice in $BIN" >&2; exit 1; }
        lipo -thin arm64 "$BIN" -output "$BIN"
        find "$APP/Contents/Resources" -maxdepth 1 -type d -name "data_*_x86_64" \
            -exec rm -rf {} +
    fi
    codesign --force --deep --sign "${SIGN_IDENTITY:--}" "$APP"
    codesign --verify --deep --strict "$APP"
    if [[ "${NOTARIZE:-0}" == "1" ]]; then
        ditto -c -k --keepParent "$APP" "$OUT/$SLUG-notarize.zip"
        xcrun notarytool submit "$OUT/$SLUG-notarize.zip" --keychain-profile brawler-notary --wait
        xcrun stapler staple "$APP"
        rm "$OUT/$SLUG-notarize.zip"
    fi
    ditto -c -k --keepParent "$APP" "$OUT/$SLUG-macos-arm64.zip"
    echo "packaged: $OUT/$SLUG-macos-arm64.zip"
fi

if [[ "$PLATFORMS" == *win* ]]; then
    WIN="$BUILD/win64"
    mkdir -p "$WIN"
    run_export "Windows" "$WIN/$NAME.exe"
    # --norsrc: no macOS AppleDouble (._*) junk in an archive bound for Windows.
    (cd "$BUILD" && ditto -c -k --norsrc win64 "$OUT/$SLUG-windows-x64.zip")
    echo "packaged: $OUT/$SLUG-windows-x64.zip"
fi

if [[ "$PLATFORMS" == *linux* ]]; then
    LIN="$BUILD/linux-x86_64"
    mkdir -p "$LIN"
    run_export "Linux x86_64" "$LIN/$SLUG.x86_64"
    # tar.gz preserves the executable bit for Linux users.
    tar -czf "$OUT/$SLUG-linux-x86_64.tar.gz" -C "$BUILD" linux-x86_64
    echo "packaged: $OUT/$SLUG-linux-x86_64.tar.gz"
fi

echo "done: $OUT"
