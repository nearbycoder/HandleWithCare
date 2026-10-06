#!/usr/bin/env bash
# Plays the trailer shot list with the built player and records every clip at a fixed 30 fps game
# clock, with the game's own audio mix (see Assets/Scripts/Game/TrailerDirector.cs).
#   Tools/trailer/capture.sh [clip,clip,...]   -> Recordings/clips/<clip>/{00000.jpg..., audio.wav, events.txt}
# About 6 minutes and 5 GB of JPEG frames for the whole list. Then:
#   python3 Tools/trailer/make_trailer.py      -> docs/media/trailer.mp4
#   python3 Tools/trailer/make_media.py        -> docs/media/teaser.webp, docs/media/trailer-poster.jpg
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/Recordings/clips"
mkdir -p "$OUT"
. "$ROOT/Tools/selftest.inc.sh"
sandbox_config "$OUT"
args=(-logFile "$OUT/player.log" -hwcTrailer "$OUT" -hwcShotList "$ROOT/Tools/trailer/shots.txt")
[ -n "${1:-}" ] && args+=(-hwcClips "$1")
timeout 3600 nice "$ROOT/Tools/play.sh" "${args[@]}" > /dev/null 2>&1 || true
grep -E "\[Trailer\]|Exception" "$OUT/player.log" | grep -v "=== " || true
grep -q "\[Trailer\] done" "$OUT/player.log" && check_config
