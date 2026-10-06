#!/usr/bin/env bash
# Gamepad-only self-test with the built player at Steam Deck resolution (1280x800): a virtual
# gamepad, no mouse or keyboard events. Title -> delivery 1 (pick, place, paint, seal, skip, next),
# then dividers with undo/redo, turning Ember, Ask Mabel, pause and settings. PASS/FAIL per step.
#   Tools/padpilot.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out padpilot "${1:-}")"
sandbox_config "$OUT"
GAME="$ROOT/Builds/Linux/HandleWithCare.x86_64"
args=(-screen-fullscreen 0 -screen-width 1280 -screen-height 800)
[ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
timeout 400 "$GAME" "${args[@]}" -logFile "$OUT/player.log" -hwcPad "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\] (PASS|FAIL|done)|Exception" "$OUT/player.log" || true
status=0
! grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT/player.log" && grep -q "\[AutoPilot\] done" "$OUT/player.log" || status=1
check_config || status=1
exit $status
