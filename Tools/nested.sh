#!/usr/bin/env bash
# Runs a command (usually a self-test) with its game windows inside a private, headless KWin, so nothing
# opens on the desktop. The compositor gets its own D-Bus session (it never talks to the desktop's), no
# global shortcuts, and exits with the command: nothing is left running and nothing is killed by name.
#   Tools/nested.sh Tools/tour.sh
#   Tools/nested.sh Tools/padpilot.sh
# Size of the virtual screen: NESTED_SIZE=2560x1440 (default 2560x1600, larger than every test window).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[ $# -gt 0 ] || { echo "usage: $0 COMMAND [ARGS...]" >&2; exit 2; }
command -v kwin_wayland > /dev/null || { echo "kwin_wayland not found" >&2; exit 1; }
command -v dbus-run-session > /dev/null || { echo "dbus-run-session not found" >&2; exit 1; }
SIZE="${NESTED_SIZE:-2560x1600}"
SOCK="hwc-nested-$$"
LOGDIR="$ROOT/Logs/selftest"
mkdir -p "$LOGDIR"
SESSION="$LOGDIR/nested-$$.sh" STATUS="$LOGDIR/nested-$$.status"
trap 'rm -f "$SESSION" "$STATUS" "$LOGDIR/nested-kwin-$$.log"' EXIT
# the session script: the command, word for word, then its exit status for us
{
  echo '#!/usr/bin/env bash'
  echo "echo \"nested KWin on \$WAYLAND_DISPLAY ($SIZE)\""
  printf '%q ' "$@"; echo
  echo "echo \$? > $(printf '%q' "$STATUS")"
} > "$SESSION"
chmod +x "$SESSION"
# Wayland only: without DISPLAY the player can't fall back to the desktop's X server
env -u DISPLAY dbus-run-session -- kwin_wayland --virtual --no-lockscreen --no-global-shortcuts \
  --socket "$SOCK" --width "${SIZE%x*}" --height "${SIZE#*x}" --exit-with-session "$SESSION" \
  2> "$LOGDIR/nested-kwin-$$.log" || true
[ -s "$STATUS" ] || { tail -20 "$LOGDIR/nested-kwin-$$.log" >&2; echo "the command didn't run in the nested KWin" >&2; exit 1; }
exit "$(cat "$STATUS")"
