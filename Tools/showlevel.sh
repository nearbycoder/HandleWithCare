#!/usr/bin/env bash
# Look at a delivery's reference packing in the resident editor (enters play mode if needed).
#   Tools/showlevel.sh N NAME [W H]   -> Screenshots/NAME.png
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
n="$1"; name="$2"; w="${3:-1920}"; h="${4:-1080}"
if ! "$ROOT/Tools/uc" eval -- --code "return UnityEngine.Application.isPlaying;" 2>/dev/null | grep -q "\"result\":true"; then
  "$ROOT/Tools/uc" editor_play > /dev/null 2>&1 || true
  for i in $(seq 1 30); do
    sleep 2
    "$ROOT/Tools/uc" eval -- --code "return HWC.Gameplay.Game.I != null;" 2>/dev/null | grep -q "\"result\":true" && break
  done
  sleep 2
fi
"$ROOT/Tools/uc" eval -- --code "
var g = HWC.Gameplay.Game.I; var lv = HWC.Sim.Levels.Get($n);
g.Save.SeenTips.Add(\"basics\"); for (int c = 1; c <= 4; c++) g.Save.SeenTips.Add(\"shift_\" + c);
g.StartLevel($n); g.Packing.ClearAll();
var pk = lv.ReferencePacking().Clone();
pk.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
foreach (int d in pk.Dividers) g.Packing.DebugAddDivider(d);
foreach (var s in pk.Shelves) g.Packing.DebugAddShelf(s);
foreach (var p in pk.Pieces) g.Packing.DebugPlace(p);
return \"ok\";" > /dev/null
sleep 2.5
"$ROOT/Tools/cap" "$name" "$w" "$h"
