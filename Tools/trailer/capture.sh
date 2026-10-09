#!/usr/bin/env bash
# Plays the trailer shot list with the built player and records every clip at a fixed 30 fps game
# clock, with the game's own audio mix (see Assets/Scripts/Game/TrailerDirector.cs).
#   Tools/nested.sh Tools/trailer/capture.sh [clip,clip,...]   -> $CLIPS/<clip>/{00000.jpg..., audio.wav, events.txt}
# Run it inside Tools/nested.sh so the window opens in a private KWin, not on the desktop.
#   CLIPS=DIR      where the clips go (default Recordings/clips)
#   FIDELITY=0..3  GRAPHICS FIDELITY of the capture, LOW to ULTRA (default 3, ULTRA: the fixed clock renders
#                  every frame however long it takes, so every step holds 30 fps in the clips)
# About 8 minutes and 6 GB of JPEG frames for the whole list. Then:
#   python3 Tools/trailer/make_trailer.py      -> docs/media/trailer.mp4
#   python3 Tools/trailer/make_media.py        -> docs/media/teaser.webp, docs/media/trailer-poster.jpg
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${CLIPS:-$ROOT/Recordings/clips}"
mkdir -p "$OUT"
. "$ROOT/Tools/selftest.inc.sh"
sandbox_config "$OUT"
args=(-logFile "$OUT/player.log" -hwcTrailer "$OUT" -hwcShotList "$ROOT/Tools/trailer/shots.txt" -hwcFidelity "${FIDELITY:-3}")
[ -n "${1:-}" ] && args+=(-hwcClips "$1")
timeout 3600 nice "$ROOT/Tools/play.sh" "${args[@]}" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[Trailer\]|Exception" "$OUT/player.log" | grep -v "=== " || true
grep -q "\[Trailer\] done" "$OUT/player.log" && check_config
