using System;
using System.Collections.Generic;
using System.Linq;
using HWC.Sim;

/// <summary>
/// Checks the order card's knock icons (<see cref="Knocks"/>) against the trips themselves: during each
/// knock, which walls of the box the items really hit, and how hard (the bumps' speeds, summed), in every
/// delivery's items-only, reference, three-star and expert trips.
/// </summary>
static class KnockCheck
{
    const int Tail = 30;   // ticks after an event whose hits still count for it (an eighth of a second)

    sealed class Tally
    {
        public readonly double[] Hits = new double[4];
        public int Events, SideAgree, SideSeen;
    }

    /// <summary>The hits on each wall (floor, lid, left, right) during one event of a trip.</summary>
    static double[] HitsDuring(Recording rec, EventSpan s)
    {
        var sum = new double[4];
        foreach (var b in rec.Bumps)
        {
            // bump ticks count from the start of the route, one ahead of the kinematics tick
            int k = b.Tick - 1;
            if (k < s.StartTick || k >= s.EndTick + Tail) continue;
            int w = b.A < 4 ? b.A : b.B < 4 ? b.B : -1;
            if (w >= 0) sum[w] += b.Speed;
        }
        return sum;
    }

    static List<Recording> Trips(LevelDef lv)
    {
        var list = new List<Recording> { Simulator.Run(lv, Program.Naive(lv), false), Simulator.Run(lv, lv.ReferencePacking(), false) };
        var pk3 = lv.Reference3Packing();
        if (pk3 != null) list.Add(Simulator.Run(lv, pk3, false));
        list.Add(Simulator.Run(lv, lv.ExpertPacking(), false));
        return list;
    }

    static string Icon(KnockWall w) => w switch
    {
        KnockWall.Floor => "v floor", KnockWall.Left => "< left", KnockWall.Right => "> right", _ => "<> sides",
    };

    /// <summary>Runs the check; prints one line per delivery when <paramref name="verbose"/>. Returns the problems.</summary>
    public static List<string> Run(bool verbose, out string summary)
    {
        var problems = new List<string>();
        var byName = new SortedDictionary<string, (KnockWall wall, Tally t)>();
        int icons = 0;
        foreach (var lv in Levels.All)
        {
            var knocks = Knocks.Of(lv);
            icons += knocks.Count;
            var trips = Trips(lv);
            var kin = lv.Kinematics;
            var parts = new List<string>();
            foreach (var kn in knocks)
            {
                // every event with this name on this leg must throw the same way as the icon says
                var t = new Tally();
                foreach (var s in kin.Spans)
                {
                    if (s.Leg != kn.Leg || Knocks.Name(s.Def) != kn.Name) continue;
                    var w = Knocks.WallOf(kin, s.Leg, s.Index);
                    if (w != kn.Wall) problems.Add($"#{lv.Number} {kn.Name} on leg {kn.Leg}: one event throws {w}, the icon says {kn.Wall}");
                    t.Events++;
                    foreach (var rec in trips)
                    {
                        var h = HitsDuring(rec, s);
                        for (int i = 0; i < 4; i++) t.Hits[i] += h[i];
                        if (kn.Wall == KnockWall.Left || kn.Wall == KnockWall.Right)
                        {
                            int mine = kn.Wall == KnockWall.Left ? 2 : 3, other = 5 - mine;
                            if (h[mine] + h[other] > 0) { t.SideSeen++; if (h[mine] > h[other]) t.SideAgree++; }
                        }
                    }
                }
                if (!byName.TryGetValue(kn.Name, out var agg)) byName[kn.Name] = agg = (kn.Wall, new Tally());
                else if (agg.wall != kn.Wall && !(Knocks.Sideways(kn.Kind)))
                    problems.Add($"#{lv.Number} {kn.Name}: {kn.Wall}, elsewhere {agg.wall}");
                for (int i = 0; i < 4; i++) agg.t.Hits[i] += t.Hits[i];
                agg.t.Events += t.Events; agg.t.SideAgree += t.SideAgree; agg.t.SideSeen += t.SideSeen;
                parts.Add($"{kn.Name} {Icon(kn.Wall)} [F{t.Hits[0]:0} L{t.Hits[1]:0} <{t.Hits[2]:0} >{t.Hits[3]:0}]");
                // a side arrow per delivery: the arrowed wall is hit at least as hard as the opposite one, when either is hit
                if ((kn.Wall == KnockWall.Left || kn.Wall == KnockWall.Right))
                {
                    int mine = kn.Wall == KnockWall.Left ? 2 : 3;
                    if (t.Hits[mine] < t.Hits[5 - mine]) parts[parts.Count - 1] += " (the other side was hit harder here)";
                }
            }
            if (verbose) Console.WriteLine($"#{lv.Number,2} {lv.Title,-28} " + string.Join(", ", parts));
        }

        // per kind of knock, over every delivery: the icon's wall is the one hit hardest
        var lines = new List<string>();
        int sideSeen = 0, sideAgree = 0;
        foreach (var (name, (wall, t)) in byName)
        {
            double F = t.Hits[0], C = t.Hits[1], L = t.Hits[2], R = t.Hits[3];
            bool ok;
            string why = "";
            switch (wall)
            {
                case KnockWall.Floor: ok = F + C + L + R == 0 || (F > C && F > L && F > R); if (F + C + L + R == 0) why = " (no hits: shaking only)"; break;
                // a side knock: that side is hit harder than the other side and the lid (the floor is reported,
                // not compared: the robot arm also sets the box down at the end)
                case KnockWall.Left: ok = L > R && L > C; why = F > L ? " (the floor too: the arm sets the box down)" : ""; break;
                case KnockWall.Right: ok = R > L && R > C; why = F > R ? " (the floor too)" : ""; break;
                default: ok = L > 0 && R > 0 && L + R > C; why = " (floor not compared: the box also heaves)"; break;
            }
            sideSeen += t.SideSeen; sideAgree += t.SideAgree;
            lines.Add($"  {name,-11} {Icon(wall),-8} {t.Events,3} events  hits: floor {F,5:0}  lid {C,4:0}  left {L,4:0}  right {R,4:0}  {(ok ? "ok" : "WRONG")}{why}");
            if (!ok) problems.Add($"{name}: the icon says {wall}, but the trips hit floor {F:0}, lid {C:0}, left {L:0}, right {R:0}");
        }
        if (verbose) foreach (var l in lines) Console.WriteLine(l);
        summary = $"knocks: {icons} icons on 25 route lines, {byName.Count} kinds each hitting the wall its icon shows; side arrows agree with the harder-hit side in {sideAgree} of {sideSeen} event trips with side hits";
        return problems;
    }
}
