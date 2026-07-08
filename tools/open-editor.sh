#!/bin/zsh
# Open the project in the CORRECT Godot build (Godot_mono.app = 4.7 .NET) with the
# Homebrew dotnet SDK on PATH so the in-editor Build button works.
#
# Why this exists: this machine also has /Applications/Godot.app (4.6, GDScript-only).
# Opening the project with that build shows "does not have the Mono module" warnings
# and would downgrade project.godot. Always use this script (or Godot_mono.app).
set -euo pipefail

REPO=$(cd "$(dirname "$0")/.." && pwd)
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

exec "/Applications/Godot_mono.app/Contents/MacOS/Godot" --path "$REPO/godot" --editor
