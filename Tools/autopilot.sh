#!/usr/bin/env bash
# Self-test with the built player: plays every delivery with their reference packings through
# the real packing code, seals, simulates and checks every outcome against the .NET validator
# (same hash = deterministic across runtimes). Prints PASS/FAIL lines and writes report.txt.
#   Tools/autopilot.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out autopilot "${1:-}")"
timeout 900 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcAutopilot "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\] (PASS|FAIL|done)|Exception" "$OUT/player.log" || true
! grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT/player.log" && grep -q "\[AutoPilot\] done" "$OUT/player.log"
