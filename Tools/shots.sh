#!/usr/bin/env bash
# Screenshot tour of one delivery with the built player: Tools/shots.sh LEVEL [ref|ref3|expert|naive] [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LEVEL="${1:-1}"; WHICH="${2:-ref}"; OUT="${3:-/tmp/hwc-shots}"
rm -rf "$OUT"; mkdir -p "$OUT"
timeout 300 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcShots "$OUT" -hwcLevel "$LEVEL" -hwcWhich "$WHICH" > /dev/null 2>&1 || true
grep -E "\[AutoPilot\]|Exception|Error" "$OUT/player.log" | grep -v "shot " | head -20
ls "$OUT"/*.png 2>/dev/null | wc -l
