#!/usr/bin/env bash
# Runs a command (usually a self-test) with its game windows inside a private, headless KWin, so nothing
# opens on the desktop. The compositor gets its own D-Bus session (it never talks to the desktop's), no
# global shortcuts, and exits with the command. Helpers the session's bus started (ksecretd and the like)
# would outlive it, so when KWin is gone every process still on that private bus is stopped: found by the
# bus address in its environment, so never another session's, and never by name.
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
SESSION="$LOGDIR/nested-$$.sh" STATUS="$LOGDIR/nested-$$.status" BUS="$LOGDIR/nested-$$.bus"
trap 'rm -f "$SESSION" "$STATUS" "$BUS" "$LOGDIR/nested-kwin-$$.log"' EXIT
# the session script: the command, word for word, then its exit status for us
{
  echo '#!/usr/bin/env bash'
  echo "printf '%s' \"\$DBUS_SESSION_BUS_ADDRESS\" > $(printf '%q' "$BUS")"
  echo "echo \"nested KWin on \$WAYLAND_DISPLAY ($SIZE)\""
  printf '%q ' "$@"; echo
  echo "echo \$? > $(printf '%q' "$STATUS")"
} > "$SESSION"
chmod +x "$SESSION"
# Wayland only: without DISPLAY the player can't fall back to the desktop's X server
env -u DISPLAY dbus-run-session -- kwin_wayland --virtual --no-lockscreen --no-global-shortcuts \
  --socket "$SOCK" --width "${SIZE%x*}" --height "${SIZE#*x}" --exit-with-session "$SESSION" \
  2> "$LOGDIR/nested-kwin-$$.log" || true
# the processes still on our private bus: helpers it activated (ksecretd, ...) that outlived it
on_our_bus() {
  local addr p
  addr="$(cat "$BUS" 2> /dev/null)"
  [ -n "$addr" ] || return 0
  for p in /proc/[0-9]*; do
    [ -O "$p" ] || continue
    if { tr '\0' '\n' < "$p/environ"; } 2> /dev/null | grep -qxF -e "DBUS_SESSION_BUS_ADDRESS=$addr" -e "DBUS_STARTER_ADDRESS=$addr"; then
      echo "${p#/proc/}"
    fi
  done
}
left="$(on_our_bus | grep -vx "$$" || true)"
if [ -n "$left" ]; then
  echo "nested: stopping $(echo $left | wc -w) helper(s) left on the session's bus: $(for p in $left; do printf '%s(%s) ' "$p" "$(cat /proc/$p/comm 2> /dev/null)"; done)" >&2
  kill $left 2> /dev/null || true
  for _ in 1 2 3 4 5 6 7 8 9 10; do left="$(on_our_bus | grep -vx "$$" || true)"; [ -z "$left" ] && break; sleep 0.3; done
  [ -z "$left" ] || { kill -9 $left 2> /dev/null || true; }
fi
[ -s "$STATUS" ] || { tail -20 "$LOGDIR/nested-kwin-$$.log" >&2; echo "the command didn't run in the nested KWin" >&2; exit 1; }
exit "$(cat "$STATUS")"
