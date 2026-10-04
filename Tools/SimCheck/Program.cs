using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HWC.Sim;

/// <summary>
/// Validates every delivery with the exact simulation code the game ships:
///   check            all levels: reference valid + delivered + under par, 3-star ref, naive fails, deterministic
///   run N [which]    timeline + per-item results for level N (which = ref | ref3 | naive | empty)
///   trace N [which]  positions every 0.25 s
///   map N "row" ...  run an ad-hoc map against level N (dividers via --div 2,3, shelves via --shelf 1@0, mods via --mods "x,y:S")
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0] : "check";
        try
        {
            switch (cmd)
            {
                case "check": return Check(args.Skip(1).ToArray());
                case "run": return RunOne(int.Parse(args[1]), args.Length > 2 ? args[2] : "ref", false);
                case "trace": return RunOne(int.Parse(args[1]), args.Length > 2 ? args[2] : "ref", true);
                case "map": return RunMap(args);
                case "route": return PrintRoute(int.Parse(args[1]));
                case "debug":
                {
                    var lv = Levels.Get(int.Parse(args[1]));
                    var rows = args[2].Split('/');
                    return DebugRun.Run(lv, LevelParse.Parse(lv, rows, null, null, null), int.Parse(args[3]), float.Parse(args[4]), float.Parse(args[5]));
                }
                case "explore": return Search.Explore(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 400);
                case "solve": return Search.Solve(int.Parse(args[1]), args.Length > 2 ? int.Parse(args[2]) : 600, args.Length > 3 ? int.Parse(args[3]) : 16);
                default: Console.Error.WriteLine("unknown command"); return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    static Packing Which(LevelDef lv, string which)
    {
        switch (which)
        {
            case "ref": return lv.ReferencePacking();
            case "ref3": return lv.Reference3Packing() ?? lv.ReferencePacking();
            case "naive": return Naive(lv);
            case "empty": return Naive(lv);
        }
        throw new ArgumentException(which);
    }

    /// <summary>Items only, dropped left to right, no materials: should fail on every level but the first.</summary>
    public static Packing Naive(LevelDef lv)
    {
        var pk = new Packing(lv.W, lv.H);
        var items = lv.Items.OrderByDescending(k => Catalog.Get(k).Mass).ToList();
        foreach (var k in items)
        {
            bool done = false;
            for (int y = 0; y < lv.H && !done; y++)
                for (int x = 0; x < lv.W && !done; x++)
                {
                    var p = new Placement(k, x, y);
                    if (pk.CanPlace(p)) { pk.Pieces.Add(p); done = true; }
                }
            if (!done) throw new Exception($"naive packer could not place {k} in level {lv.Number}");
        }
        return pk;
    }

    static int Check(string[] args)
    {
        int failures = 0;
        var sw = Stopwatch.StartNew();
        var levels = Levels.All;
        foreach (var lv in levels)
        {
            if (args.Length > 0 && !args.Contains(lv.Number.ToString())) continue;
            var problems = new List<string>();
            Packing pk;
            try { pk = lv.ReferencePacking(); }
            catch (Exception e) { Console.WriteLine($"#{lv.Number,2} {lv.Title,-28} ref: PARSE ERROR {e.Message}"); failures++; continue; }
            string err = pk.Validate(lv);
            if (err != null) problems.Add("reference invalid: " + err);
            Recording rec = null;
            if (err == null)
            {
                var t0 = sw.Elapsed;
                rec = Simulator.Run(lv, pk);
                var simMs = (sw.Elapsed - t0).TotalMilliseconds;
                if (!rec.Outcome.Delivered) problems.Add("reference NOT delivered: " + Describe(rec));
                if (!rec.Outcome.UnderBudget) problems.Add($"reference cost {rec.Outcome.Cost} > par {lv.Par}");
                var rec2 = Simulator.Run(lv, pk);
                if (rec2.Hash != rec.Hash) problems.Add("NOT deterministic");
                Console.Write($"#{lv.Number,2} {lv.Title,-28} ref: {Stars(rec.Outcome)} cost {rec.Outcome.Cost,2}/{lv.Par,-2} care {rec.Outcome.WorstCare,4:0.00} ({simMs,4:0}ms, {lv.Kinematics.Duration,4:0.0}s)");
            }
            else Console.Write($"#{lv.Number,2} {lv.Title,-28} ref: INVALID");

            var pk3 = lv.Reference3Packing();
            if (pk3 != null)
            {
                string e3 = pk3.Validate(lv);
                if (e3 != null) problems.Add("ref3 invalid: " + e3);
                else
                {
                    var r3 = Simulator.Run(lv, pk3);
                    Console.Write($"  ref3: {Stars(r3.Outcome)} cost {r3.Outcome.Cost}, care {r3.Outcome.WorstCare:0.00}");
                    if (r3.Outcome.Stars < 3) problems.Add("ref3 is not three stars: " + Describe(r3));
                }
            }
            else if (rec != null) Console.Write(rec.Outcome.Stars == 3 ? "  (ref is 3★)" : "  (no 3★ ref)");

            if (lv.Number > 1)
            {
                var naive = Naive(lv);
                var rn = Simulator.Run(lv, naive);
                Console.Write($"  naive: {(rn.Outcome.Delivered ? "DELIVERED" : "fails")}");
                if (rn.Outcome.Delivered) problems.Add("naive packing (no materials) is delivered");
            }
            Console.WriteLine();
            foreach (var p in problems) Console.WriteLine("     !! " + p);
            failures += problems.Count;
        }
        Console.WriteLine(failures == 0 ? $"ALL OK ({sw.Elapsed.TotalSeconds:0.0}s)" : $"{failures} PROBLEM(S)");
        return failures == 0 ? 0 : 1;
    }

    public static string Stars(Outcome o) => new string('★', o.Stars) + new string('☆', 3 - o.Stars);

    static string Describe(Recording rec)
    {
        var sb = new StringBuilder();
        foreach (var i in rec.Outcome.Items)
            sb.Append($"{i.Kind}={i.Status}({i.Care:0.00}) ");
        foreach (var inc in rec.Incidents.Where(x => x.IsFailure).Take(4))
            sb.Append($"| {inc.Time:0.00}s {rec.Bodies[inc.Body].Kind} {inc.Kind} {inc.Value:0.0}/{inc.Limit:0.0} ev {EventName(rec, inc)} ");
        return sb.ToString();
    }

    static string EventName(Recording rec, Incident inc)
    {
        if (inc.Leg < 0) return "-";
        var leg = rec.Kin.Route.Legs[inc.Leg];
        if (inc.Event < 0 || inc.Event >= leg.Events.Count) return leg.Kind + "/end";
        return leg.Kind + "/" + leg.Events[inc.Event].Kind;
    }

    static int RunOne(int n, string which, bool trace)
    {
        var lv = Levels.Get(n);
        var pk = Which(lv, which);
        return Report(lv, pk, trace);
    }

    static int Report(LevelDef lv, Packing pk, bool trace)
    {
        Console.WriteLine($"Level {lv.Number}: {lv.Title}  box {lv.W}x{lv.H}  route {lv.Kinematics.Duration:0.0}s");
        Console.Write(pk.ToAscii());
        string err = pk.Validate(lv);
        if (err != null) Console.WriteLine("INVALID: " + err);
        var rec = Simulator.Run(lv, pk);
        Console.WriteLine($"cost {rec.Outcome.Cost} par {lv.Par}  stars {Stars(rec.Outcome)}  delivered {rec.Outcome.Delivered}  care {rec.Outcome.WorstCare:0.00}");
        foreach (var i in rec.Outcome.Items)
        {
            string at = i.PeakLeg >= 0 ? $"{rec.Kin.Route.Legs[i.PeakLeg].Kind}/{(i.PeakEvent >= 0 ? rec.Kin.Route.Legs[i.PeakLeg].Events[i.PeakEvent].Kind.ToString() : "end")} @{i.PeakTick * SimConst.Dt:0.00}s" : "";
            Console.WriteLine($"  {i.Kind,-12} {i.Status,-9} peak jolt {i.PeakJolt,5:0.0} / {i.Limit,4:0.0}  care {i.Care:0.00}  {at}");
        }
        Console.WriteLine("incidents:");
        foreach (var inc in rec.Incidents)
            Console.WriteLine($"  {inc.Time,5:0.00}s {rec.Bodies[inc.Body].Kind,-12} {inc.Kind,-13} {inc.Value,5:0.0}/{inc.Limit,4:0.0} {(inc.Other >= 0 ? "by " + Name(rec, inc.Other) : ""),-16} [{EventName(rec, inc)}]{(inc.IsFailure ? " FAIL" : "")}");
        // peak jolt per event for the items
        if (trace)
        {
            for (int f = 0; f < rec.Frames.Count; f += 6)
            {
                var sb = new StringBuilder($"{Recording.TimeOfFrame(f),5:0.00}s ");
                var fr = rec.Frames[f];
                for (int b = 0; b < fr.Length; b++)
                {
                    if (rec.Bodies[b].Type != BodyType.Piece) continue;
                    if (Catalog.Get(rec.Bodies[b].Kind).IsPadding) continue;
                    sb.Append($"{rec.Bodies[b].Kind}({fr[b].Pos.x:0.00},{fr[b].Pos.y:0.00}) ");
                }
                Console.WriteLine(sb);
            }
        }
        Console.WriteLine($"bumps: {rec.Bumps.Count}, hash {rec.Hash:X}");
        return 0;
    }

    static string Name(Recording rec, int body)
    {
        var b = rec.Bodies[body];
        return b.Type == BodyType.Piece ? b.Kind.ToString() : b.Type.ToString();
    }

    static int RunMap(string[] args)
    {
        var lv = Levels.Get(int.Parse(args[1]));
        var rows = new List<string>();
        int[] div = null; string shelf = null, mods = null;
        bool trace = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--trace") { trace = true; continue; }
            if (args[i] == "--div") div = args[++i].Split(',').Select(int.Parse).ToArray();
            else if (args[i] == "--shelf") shelf = args[++i];
            else if (args[i] == "--mods") mods = args[++i];
            else rows.Add(args[i]);
        }
        var pk = LevelParse.Parse(lv, rows.ToArray(), div, shelf, mods);
        return Report(lv, pk, trace);
    }

    static int PrintRoute(int n)
    {
        var lv = Levels.Get(n);
        var k = lv.Kinematics;
        foreach (var s in k.Spans)
            Console.WriteLine($"leg {s.Leg} {s.Kind,-10} ticks {s.StartTick,5}-{s.EndTick,5}  ({s.StartTick * SimConst.Dt:0.00}s)  y {s.Y0:0.000}->{s.Y1:0.000} a {s.A0 * 57.3:0}->{s.A1 * 57.3:0}");
        // peak box acceleration
        double peak = 0; int at = 0;
        for (int i = 1; i < k.TickCount - 1; i++)
        {
            k.Velocity(i, out double vx, out double vy);
            k.Velocity(i - 1, out double px, out double py);
            double a = Math.Sqrt((vx - px) * (vx - px) + (vy - py) * (vy - py)) / SimConst.Dt;
            if (a > peak) { peak = a; at = i; }
        }
        Console.WriteLine($"peak accel {peak / 9.81:0.0} g at {at * SimConst.Dt:0.00}s");
        return 0;
    }
}
