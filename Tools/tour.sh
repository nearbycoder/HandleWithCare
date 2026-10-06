#!/usr/bin/env bash
# Screenshot tour of the menus and a hand-played first delivery (real input events).
#   Tools/tour.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out tour "${1:-}")"
sandbox_config "$OUT"
timeout 300 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcMenus "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\]|Exception" "$OUT/player.log" | grep -v "shot " || true
status=0
ls "$OUT"/*.png || status=1
check_config || status=1
exit $status
