#!/bin/zsh
# Open the project in the CORRECT Godot build (Godot_mono.app = 4.7 .NET).
#
# Why this exists: this machine also has /Applications/Godot.app (4.6, GDScript-only).
# Opening the project with that build shows "does not have the Mono module" warnings
# and would downgrade project.godot. Always use this script (or Godot_mono.app).
#
# DOTNET_ROOT must be UNSET here (2026-08-17): the login profile points it at the
# homebrew .NET 8 root, but the editor's Build/Play spawns the SYSTEM dotnet
# (/usr/local/share/dotnet, v10) — a v10 muxer forced onto the v8 SDK cannot
# resolve Godot.NET.Sdk (MSB4276) and every in-editor build fails with a blank
# "Failed to build project". Surfaced when the godot csproj gained the NameGen
# project reference (2026-08-14); full diagnosis in docs/features/packaged-games.md.
set -euo pipefail

REPO=$(cd "$(dirname "$0")/.." && pwd)
unset DOTNET_ROOT DOTNET_HOST_PATH 2>/dev/null || true

exec "/Applications/Godot_mono.app/Contents/MacOS/Godot" --path "$REPO/godot" --editor
