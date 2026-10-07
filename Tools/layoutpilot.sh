#!/usr/bin/env bash
# Bench layout self-test with the built player: at 1920x1080, 1280x800, 2560x1080 and 1600x1200, every
# bench on a first visit and on a retry (LAST TRIP report and Mabel's tallest hint note), LARGER TEXT off
# and on. Logs the box cells and shelf cubbies under a HUD panel and the cells' size on screen, framed as
# before round 7 and as now. PASS/FAIL per screen size, text size and visit.
#   Tools/layoutpilot.sh [OUTDIR] [WxH ...]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out layoutpilot "${1:-}")"
shift || true
sizes=("$@")
[ ${#sizes[@]} -eq 0 ] && sizes=(1920x1080 1280x800 2560x1080 1600x1200)
sandbox_config "$OUT"
GAME="$ROOT/Builds/Linux/HandleWithCare.x86_64"
status=0
for s in "${sizes[@]}"; do
  w="${s%x*}"; h="${s#*x}"
  d="$OUT/$s"; mkdir -p "$d"
  args=(-screen-fullscreen 0 -screen-width "$w" -screen-height "$h")
  [ -n "${WAYLAND_DISPLAY:-}" ] && args+=(-force-wayland)
  timeout 400 "$GAME" "${args[@]}" -logFile "$d/player.log" -hwcLayout "$d" > /dev/null 2>&1 || true
  cap_log "$d/player.log"
  grep -E "\[AutoPilot\] (PASS|FAIL|done|layout summary)|Exception|NO CLEAR" "$d/player.log" || true
  ! grep -qE "\[AutoPilot\] FAIL|Exception" "$d/player.log" && grep -q "\[AutoPilot\] done" "$d/player.log" || status=1
done
check_config || status=1
exit $status
