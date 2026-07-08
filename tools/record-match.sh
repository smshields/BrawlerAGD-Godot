#!/bin/zsh
# Record a match as an H.264 MP4 (QuickTime-friendly).
# Godot's movie writer only emits MJPEG AVI, so we record then transcode.
#
# Usage:
#   tools/record-match.sh <game.json> [seed] [seconds] [out.mp4]
# Example:
#   tools/record-match.sh runs/gamec.json 11 30 runs/media/gamec_seed11.mp4
set -euo pipefail

GAME=${1:?usage: record-match.sh <game.json> [seed] [seconds] [out.mp4]}
SEED=${2:-7}
SECONDS_CAP=${3:-30}
OUT=${4:-runs/media/match_seed${SEED}.mp4}

REPO=$(cd "$(dirname "$0")/.." && pwd)
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
FFMPEG=${FFMPEG:-/opt/homebrew/bin/ffmpeg}
TMP_AVI=$(mktemp -t brawler_rec).avi

mkdir -p "$(dirname "$REPO/$OUT")"

# The QUIT_AFTER failsafe exits Godot with code 2 when it cuts a long match — expected.
BRAWLER_AUTOPLAY="ai:${SEED}" \
BRAWLER_GAME="$(cd "$(dirname "$GAME")" && pwd)/$(basename "$GAME")" \
BRAWLER_QUIT_AFTER="$SECONDS_CAP" \
"$GODOT" --path "$REPO/godot" --resolution 1280x720 \
    --write-movie "$TMP_AVI" --fixed-fps 60 || true

"$FFMPEG" -y -v error -i "$TMP_AVI" \
    -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart \
    "$REPO/$OUT"
rm -f "$TMP_AVI"
echo "recorded: $REPO/$OUT"
