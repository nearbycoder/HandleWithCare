#!/usr/bin/env bash
# Save self-test with the built player: seven launches on one save with saving switched on, all inside
# Logs/selftest/savepilot (the player's config is sandboxed there; the test mode refuses any other folder).
#   1  fresh save: settings clicked, delivery 1 delivered, an unsealed box kept through Main Menu and quit
#   2  restart: progress, settings (VSync, frame-rate limit...) and the unsealed box are back
#   3  save.json cut in half: the backup loads, the damaged file is kept, the title screen says so
#   4  garbage and no backup: a fresh save without an exception, both damaged files kept; then
#      delivery 1 with three stars
#   5  restart: a failed trip, then MY BEST brings the three-star packing back (undo, redo, ship it);
#      then a failed trip on delivery 5, and back to it through the main menu
#   6  restart: delivery 5's last trip comes back (report, trails, hash, Ask Mabel's focus) while the
#      bench is already open, and the next trip's review compares with it; then Settings > START OVER:
#      cancel changes nothing; confirm clears
#      progress, keeps the settings and a copy of the old save
#   7  a save in v0.1.0's format: the last box shipped becomes MY BEST if it still delivers, checked
#      on a worker thread when each bench opens
#   Tools/savepilot.sh [OUTDIR]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$ROOT/Tools/selftest.inc.sh"
OUT="$(selftest_out savepilot "${1:-}")"
sandbox_config "$OUT"
DATA="$XDG_CONFIG_HOME/unity3d/Mossbury Parcel Post/Handle With Care"
launch() {
  timeout 300 "$ROOT/Tools/play.sh" -logFile "$OUT/player$1.log" -hwcSave "$OUT" -hwcSaveStep "$1" > /dev/null 2>&1 || true
  cap_log "$OUT/player$1.log"
  grep -E "\[AutoPilot\] (PASS|FAIL|done)|\[Save\]|Exception" "$OUT/player$1.log" || true
}
status=0
launch 1
launch 2
if [ -f "$DATA/save.json" ]; then
  # cut the save off halfway, like a crash in the middle of an old-style in-place write
  size=$(stat -c %s "$DATA/save.json")
  head -c $((size / 2)) "$DATA/save.json" > "$DATA/save.json.cut" && mv "$DATA/save.json.cut" "$DATA/save.json"
else
  echo "FAIL no save.json after launch 2"; status=1
fi
launch 3
printf 'not a save {{{' > "$DATA/save.json"
rm -f "$DATA/save.json.bak"
launch 4
launch 5
launch 6
if [ -f "$OUT/v010-save.json" ]; then
  cp "$OUT/v010-save.json" "$DATA/save.json" && rm -f "$DATA/save.json.bak"
else
  echo "FAIL launch 6 wrote no v0.1.0 save"; status=1
fi
launch 7
for i in 1 2 3 4 5 6 7; do
  grep -q "\[AutoPilot\] done" "$OUT/player$i.log" || { echo "FAIL launch $i did not finish"; status=1; }
done
if grep -qE "\[AutoPilot\] FAIL|Exception" "$OUT"/player?.log; then status=1; fi
ls "$DATA"
check_config || status=1
exit $status
