#!/usr/bin/env bash
# GRAPHICS FIDELITY probe with the built player: the same three frames (the bench, the trip, the unboxing) at
# LOW, MEDIUM, HIGH and ULTRA, a screenshot and 300 timed frames each (VSync off, no frame cap).
#   Tools/fidelity.sh [OUTDIR]        (run it through Tools/nested.sh to keep the window off the desktop)
# The load average is part of every line: on a shared GPU the times are only comparable within one run.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out fidelity "${1:-}")"
sandbox_config "$OUT"
timeout 400 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcFidelityProbe "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[Fidelity\]|\[AutoPilot\] (FAIL|done)|Exception" "$OUT/player.log" | head -60
echo "$(ls "$OUT"/F_*.png 2>/dev/null | wc -l) screenshots in ${OUT#$ROOT/}"
check_config
