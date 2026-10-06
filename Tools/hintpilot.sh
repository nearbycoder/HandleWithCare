#!/usr/bin/env bash
# Self-test of Ask Mabel with the built player: on every delivery, clicks the hint button through
# every stage with real mouse events, builds exactly what the hint ghosts show, ships it and expects
# three stars with the validator's hash. Screenshots of each stage on delivery 18, then checks
# that finishing delivery 20 rolls the credits and opens Overtime.
#   Tools/hintpilot.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out hintpilot "${1:-}")"
sandbox_config "$OUT"
timeout 900 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcHints "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\] (PASS|FAIL|done)|Exception" "$OUT/player.log" || true
status=0
! grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT/player.log" && grep -q "\[AutoPilot\] done" "$OUT/player.log" || status=1
check_config || status=1
exit $status
