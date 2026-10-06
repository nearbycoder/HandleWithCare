#!/usr/bin/env bash
# Screenshot tour of the menus and a hand-played first delivery (real input events).
#   Tools/tour.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out tour "${1:-}")"
timeout 300 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcMenus "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\]|Exception" "$OUT/player.log" | grep -v "shot " || true
ls "$OUT"/*.png
