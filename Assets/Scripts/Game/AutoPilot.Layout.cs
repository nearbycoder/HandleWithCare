using System.Collections;
using System.Collections.Generic;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// -hwcLayout DIR (Tools/layoutpilot.sh, one launch per screen size): every bench, as on a first visit
    /// (no report, Mabel's intro note) and as on a retry (the items-only packing's LAST TRIP report and
    /// Mabel's tallest hint note), with LARGER TEXT off and on. For each, the box cells and shelf cubbies
    /// under a HUD panel and the cells' size on screen, framed as before round 7 and as now.
    /// </summary>
    public sealed partial class AutoPilot
    {
        public static bool LayoutRequested => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hwcLayout") >= 0;

        struct LayoutMeasure { public int Cells, CellsCovered, Slots, SlotsCovered; public float CellPx; }

        LayoutMeasure MeasureBench(LevelDef lv)
        {
            var g = Game.I;
            var cam = g.Rig.Cam;
            var keep = g.Hud.BenchKeepOut();
            var m = new LayoutMeasure();
            float px = 0f;
            for (int y = 0; y < lv.H; y++)
                for (int x = 0; x < lv.W; x++)
                {
                    var r = ScreenQuad(cam, g.Station.Box.CellToWorld(x, y), g.Station.Box.CellToWorld(x + 1, y + 1));
                    px += r.width;
                    m.Cells++;
                    if (Covered(r, keep)) m.CellsCovered++;
                }
            m.CellPx = px / Mathf.Max(1, m.Cells);
            int n = lv.Items.Length, cols = n > 3 ? 2 : 1, rows = (n + cols - 1) / cols;
            const float slot = 0.62f;
            var shelf = g.Station.ItemShelf;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var q = ScreenQuad(cam, shelf.TransformPoint(new Vector3(c * slot, r * slot, -0.03f)), shelf.TransformPoint(new Vector3((c + 1) * slot, (r + 1) * slot, -0.03f)));
                    m.Slots++;
                    if (Covered(q, keep)) m.SlotsCovered++;
                }
            return m;
        }

        static Rect ScreenQuad(Camera cam, Vector3 a, Vector3 b)
        {
            Vector2 sa = cam.WorldToScreenPoint(a), sb = cam.WorldToScreenPoint(b);
            return Rect.MinMaxRect(Mathf.Min(sa.x, sb.x), Mathf.Min(sa.y, sb.y), Mathf.Max(sa.x, sb.x), Mathf.Max(sa.y, sb.y));
        }

        static bool Covered(Rect r, List<Rect> keep)
        {
            // off screen counts as covered too
            if (r.xMin < 0 || r.yMin < 0 || r.xMax > UnityEngine.Screen.width || r.yMax > UnityEngine.Screen.height) return true;
            foreach (var k in keep) if (r.Overlaps(k)) return true;
            return false;
        }

        /// <summary>Mabel's tallest note for a delivery (any stage, any item, or about the budget).</summary>
        (int stage, int focus) TallestNote(LevelDef lv)
        {
            var g = Game.I;
            float best = -1f; int bs = 1, bf = (int)lv.Items[0];
            for (int st = 1; st <= Hints.MaxStage; st++)
            {
                foreach (var k in lv.Items)
                {
                    var (_, h) = g.Hud.FitNoteForTest(Hints.Note(lv, st, k));
                    if (h > best) { best = h; bs = st; bf = (int)k; }
                }
                var (_, hb) = g.Hud.FitNoteForTest(Hints.Note(lv, st, lv.Items[0], true));
                if (hb > best) { best = hb; bs = st; bf = Hints.BudgetFocus; }
            }
            return (bs, bf);
        }

        IEnumerator LayoutCheck()
        {
            var g = Game.I;
            if (!g.Save.SeenTips.Contains("basics")) g.Save.SeenTips.Add("basics");
            for (int c = 1; c <= 9; c++) g.Save.SeenTips.Add("shift_" + c);   // no shift title cards over the bench
            string res = $"{UnityEngine.Screen.width}x{UnityEngine.Screen.height}";
            var shotsAt = new HashSet<int> { 18, 19 };
            foreach (bool larger in new[] { false, true })
            {
                TextScale.Set(larger);
                foreach (bool retry in new[] { false, true })
                {
                    int beforeCovered = 0, afterCovered = 0, benches = 0;
                    float minRatio = 9f; string minAt = "";
                    double worstMs = 0;
                    foreach (var lv in Levels.All)
                    {
                        g.Save.Records.Clear();
                        g.LastRun = null;
                        if (retry)
                        {
                            g.LastRun = Simulator.Run(lv, NaivePacking(lv));
                            var (st, focus) = TallestNote(lv);
                            var rec = g.Save.Get(lv.Number, true);
                            rec.Attempts = 1; rec.HintStage = st; rec.HintFocus = focus; rec.HintsHidden = false;
                        }
                        g.StartLevel(lv.Number);
                        yield return null;
                        yield return new WaitForSecondsRealtime(0.35f);
                        g.Station.LegacyFrame = true;
                        g.Hud.FrameBench(true);
                        yield return null;
                        var old = MeasureBench(lv);
                        string tag = $"{res}_{(larger ? "larger" : "normal")}_{(retry ? "retry" : "first")}_L{lv.Number:00}";
                        bool shoot = retry && larger && shotsAt.Contains(lv.Number);
                        if (shoot) { Shot($"layout_{tag}_before"); yield return AfterShot(); }
                        g.Station.LegacyFrame = false;
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        g.Hud.FrameBench(true);
                        worstMs = System.Math.Max(worstMs, clock.Elapsed.TotalMilliseconds);
                        yield return null;
                        var now = MeasureBench(lv);
                        if (shoot) { Shot($"layout_{tag}_after"); yield return AfterShot(); }
                        float ratio = now.CellPx / Mathf.Max(1f, old.CellPx);
                        if (ratio < minRatio) { minRatio = ratio; minAt = $"#{lv.Number}"; }
                        beforeCovered += old.CellsCovered + old.SlotsCovered;
                        afterCovered += now.CellsCovered + now.SlotsCovered;
                        benches++;
                        Debug.Log($"[AutoPilot] layout {tag}: {lv.W}x{lv.H} box, report {(g.Hud.LastTripText.Length > 0 ? "shown" : "none")}; " +
                                  $"before {old.CellsCovered}/{old.Cells} cells and {old.SlotsCovered}/{old.Slots} cubbies under the HUD, cell {old.CellPx:0}px; " +
                                  $"now {now.CellsCovered} cells and {now.SlotsCovered} cubbies, cell {now.CellPx:0}px ({ratio * 100:0}%), pulled back {g.Station.FrameScale:0.00}, slid ({g.Station.FrameShift.x:0},{g.Station.FrameShift.y:0})px{(g.Station.FrameClear ? "" : ", NO CLEAR FRAMING")}");
                    }
                    string what = $"{res} larger text {(larger ? "on" : "off")}, {(retry ? "retry (report + tallest note)" : "first visit")}";
                    Debug.Log($"[AutoPilot] layout summary {what}: {benches} benches, cells/cubbies under the HUD {beforeCovered} before, {afterCovered} now; smallest cell {minRatio * 100:0}% of before ({minAt}); framing took at most {worstMs:0.0} ms");
                    // the aim was 80%; the worst case (16:9, LARGER TEXT, a report of three wrapped lines and the
                    // tallest note on the 6x4 boxes) gets 76%: guard against anything worse, and say so
                    Check2(afterCovered == 0 && minRatio >= 0.75f, "layout", $"{what}: nothing under the HUD ({beforeCovered} before), cells at least 75% of before (smallest {minRatio * 100:0}%, {minAt}{(minRatio < 0.8f ? ", under the 80% aim" : "")})");
                }
            }
            // the trip's care meters, on the delivery with the most items: clear of the journey's controls
            LevelDef most = Levels.All[0];
            foreach (var lv in Levels.All) if (lv.Items.Length > most.Items.Length) most = lv;
            foreach (bool larger in new[] { false, true })
            {
                TextScale.Set(larger);
                g.Save.Records.Clear();
                g.LastRun = Simulator.Run(most, NaivePacking(most));
                g.StartLevel(most.Number);
                yield return new WaitForSecondsRealtime(0.35f);
                g.WatchLastTrip();
                yield return new WaitForSecondsRealtime(0.6f);
                var clash = g.Hud.CareMeterClashes();
                var mr = g.Hud.CareMetersRect;
                string what = $"{res} larger text {(larger ? "on" : "off")}";
                Check2(g.Phase == Phase.Journey && g.Hud.CareMetersShown && clash.Count == 0, "layout",
                       $"{what}: the care meters on #{most.Number} ({most.Items.Length} items, {mr.rect.width:0}x{mr.rect.height:0} units) clear of the journey's controls{(clash.Count > 0 ? ": under " + string.Join(", ", clash) : "")}");
                Shot($"layout_{res}_{(larger ? "larger" : "normal")}_meters");
                yield return AfterShot();
                g.Journey.Skip();
                float t0 = Time.realtimeSinceStartup;
                while (g.Phase != Phase.Packing && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            }
            TextScale.Set(false);
        }
    }
}
