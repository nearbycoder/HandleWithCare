#!/usr/bin/env bash
# Screenshot tour of the menus and a hand-played first delivery (real input events).
#   Tools/tour.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out tour "${1:-}")"
sandbox_config "$OUT"
timeout 420 "$ROOT/Tools/play.sh" -logFile "$OUT/player.log" -hwcMenus "$OUT" > /dev/null 2>&1 || true
cap_log "$OUT/player.log"
grep -E "\[AutoPilot\]|Exception" "$OUT/player.log" | grep -v "shot " || true
status=0
ls "$OUT"/*.png || status=1
# the GIFs and photos it saved (SAVE GIF, F12), read back by ImageMagick
while IFS= read -r -d '' f; do
  if info="$(magick identify -format '%n %W %H\n' "$f" 2> /dev/null)" && info="${info%%$'\n'*}" && [ -n "$info" ]; then
    set -- $info; echo "picture: ${f##*/}: $1 frame(s), ${2}x${3}, $(( $(stat -c %s "$f") / 1024 )) KB"
  else echo "FAIL picture: ImageMagick can't read ${f##*/}"; status=1; fi
done < <(find "$OUT/config" \( -name '*.gif' -o -name '*.png' \) -path '*/Pictures/*' -print0 2> /dev/null)
if grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT/player.log"; then echo "FAIL: a step failed or an exception was logged"; status=1; fi
check_config || status=1
exit $status
