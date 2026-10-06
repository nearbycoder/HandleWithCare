#!/usr/bin/env bash
# Self-test of Ask Mabel with the built player: on every delivery, clicks the hint button through
# every stage with real mouse events, builds exactly what the hint ghosts show, ships it and expects
# three stars with the validator's hash. Screenshots of each stage on delivery 18, then checks
# that finishing delivery 20 rolls the credits and opens Overtime.
#   Tools/hintpilot.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/tmp/hwc-hintpilot}"
rm -rf "$OUT"; mkdir -p "$OUT"
timeout 900 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcHints "$OUT" > /dev/null 2>&1 || true
grep -E "\[AutoPilot\] (PASS|FAIL|done)|Exception" "$OUT/player.log" || true
! grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT/player.log" && grep -q "\[AutoPilot\] done" "$OUT/player.log"
