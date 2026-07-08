#!/bin/zsh
# Export the playable macOS app to dist/BrawlerAGD.app.
#
# Steps: headless Godot export (runs dotnet publish for both architectures via
# godot/BrawlerGodot.sln), then a forced deep ad-hoc re-sign — REQUIRED: Godot's own
# export-time signature does not cover the embedded .NET data directories, and macOS
# SIGKILLs the app on launch without this step.
#
# Prereqs: Godot_mono.app 4.7, export templates installed
# (~/Library/Application Support/Godot/export_templates/4.7.stable.mono).
set -euo pipefail

REPO=$(cd "$(dirname "$0")/.." && pwd)
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

if pgrep -f "Godot_mono.*--editor" > /dev/null; then
    echo "Close the Godot editor first (exports clash with its build caches)." >&2
    exit 1
fi

mkdir -p "$REPO/dist"
"$GODOT" --path "$REPO/godot" --headless --export-release "macOS" 2>&1 \
    | grep -iE "publish|DONE|error" | grep -v backtrace || true

codesign --force --deep --sign - "$REPO/dist/BrawlerAGD.app"
codesign --verify --deep --strict "$REPO/dist/BrawlerAGD.app"
echo "exported + re-signed: $REPO/dist/BrawlerAGD.app"
