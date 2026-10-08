using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HWC.Sim;

/// <summary>
/// Validates every delivery with the exact simulation code the game ships:
///   check            all levels: reference valid + delivered + under par, 3-star ref, naive fails, deterministic
///   run N [which]    timeline + per-item results for level N (which = ref | ref3 | naive | empty | careless)
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
                case "knocks": { var pr = KnockCheck.Run(true, out var sum); Console.WriteLine(sum); foreach (var x in pr) Console.WriteLine("     !! " + x); return pr.Count == 0 ? 0 : 1; }
                case "hints": return PrintHints();
                case "hashes": return PrintHashes();
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

    /// <summary>The trip hash of every delivery's reference, three-star, expert and items-only packing
    /// (to show that a change leaves every trip exactly as it was).</summary>
    static int PrintHashes()
    {
        foreach (var lv in Levels.All)
        {
            var parts = new List<string> { $"ref {Simulator.Run(lv, lv.ReferencePacking(), false).Hash:x16}" };
            var pk3 = lv.Reference3Packing();
            if (pk3 != null) parts.Add($"ref3 {Simulator.Run(lv, pk3, false).Hash:x16}");
            parts.Add($"expert {Simulator.Run(lv, lv.ExpertPacking(), false).Hash:x16}");
            parts.Add($"naive {Simulator.Run(lv, Naive(lv), false).Hash:x16}");
            Console.WriteLine($"#{lv.Number,2} " + string.Join("  ", parts));
        }
        return 0;
    }

    static Packing Which(LevelDef lv, string which)
    {
        switch (which)
        {
            case "ref": return lv.ReferencePacking();
            case "ref3": return lv.Reference3Packing() ?? lv.ReferencePacking();
            case "naive": return Naive(lv);
            case "empty": return Naive(lv);
            case "careless": return Hints.CarelessSample(lv) ?? throw new ArgumentException("no careless sample for this delivery");
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
                Meters(rec, "reference", problems);
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
                    Meters(r3, "ref3", problems);
                    Console.Write($"  ref3: {Stars(r3.Outcome)} cost {r3.Outcome.Cost}, care {r3.Outcome.WorstCare:0.00}");
                    if (r3.Outcome.Stars < 3) problems.Add("ref3 is not three stars: " + Describe(r3));
                }
            }
            else if (rec != null) Console.Write(rec.Outcome.Stars == 3 ? "  (ref is 3★)" : "  (no 3★ ref)");

            // Mabel's best: a stored packing that validates, earns three stars and costs Expert <= par
            var ex = lv.ExpertPacking();
            string ee = ex.Validate(lv);
            if (ee != null) problems.Add("expert packing invalid: " + ee);
            else
            {
                var rx = Simulator.Run(lv, ex, false);
                if (rx.Outcome.Stars < 3) problems.Add("expert packing is not three stars: " + Describe(rx));
                if (lv.Expert > lv.Par) problems.Add($"expert cost {lv.Expert} > par {lv.Par}");
                if (rec != null && lv.Expert > rec.Outcome.Cost) problems.Add($"expert cost {lv.Expert} > reference cost {rec.Outcome.Cost}");
                Console.Write($"  expert {lv.Expert}");
            }

            // Ask Mabel: the last hint stage must be a packing that validates and earns three stars
            var hinted = HintedPacking(lv);
            string he = hinted.Validate(lv);
            if (he != null) problems.Add("hint packing invalid: " + he);
            else if (Simulator.Run(lv, hinted, false).Outcome.Stars < 3) problems.Add("hint packing is not three stars");
            else Console.Write("  hints: 3★");

            // budget hints: her costs as the note says, under par, and her padding at stage 2
            var src = Hints.Source(lv);
            string bnote = Hints.Note(lv, 1, lv.Items[0], true);
            if (!bnote.StartsWith($"Mine costs {src.Cost} (par {lv.Par})") || src.Cost > lv.Par) problems.Add("budget note wrong: " + bnote);
            var b2 = Hints.Pieces(lv, 2, lv.Items[0], true);
            if (b2.Count != src.Pieces.Count(p => p.Def.IsPadding) || b2.Any(p => !p.Def.IsPadding)) problems.Add("budget stage 2 isn't exactly her padding");
            if (Hints.Pieces(lv, Hints.MaxStage, lv.Items[0], true).Count != src.Pieces.Count) problems.Add("budget stage 4 isn't her whole packing");
            // the ghosts against the box: her own packing matches all of them with nothing extra; an
            // empty box matches none; one item moved is no longer in place
            var all = Hints.Pieces(lv, Hints.MaxStage, lv.Items[0]);
            if (all.Any(gp => Hints.MatchOf(src, gp) != Hints.Match.InPlace) || Hints.Extras(src, lv, Hints.MaxStage, lv.Items[0], false).Count > 0
                || src.Dividers.Any(d => !Hints.HasDivider(src, d)) || src.Shelves.Any(sh => !Hints.HasShelf(src, src, sh)))
                problems.Add("her packing doesn't match all of her own ghosts");
            var emptyBox = new Packing(lv.W, lv.H);
            if (all.Any(gp => Hints.MatchOf(emptyBox, gp) != Hints.Match.Open)) problems.Add("an empty box matches or blocks a ghost");
            ghostChecks += all.Count * 2;

            // a packing that misses only the budget star (the self-test ships it), when one exists
            var over = Hints.OverBudgetSample(lv);
            if (over == null)
            {
                // only acceptable when no packing can cost more than par: every material on offer is within it
                bool impossible = lv.Materials.Cost <= lv.Par;
                Console.Write(impossible ? $"  over budget: can't happen (everything on offer costs {lv.Materials.Cost}, par {lv.Par})" : "  over budget: none found");
                if (!impossible) problems.Add("no over-budget packing found, though one may exist");
            }
            else
            {
                var rov = Simulator.Run(lv, over);
                Meters(rov, "over-budget", problems);
                var ro = rov.Outcome;
                Console.Write($"  over budget: {Stars(ro)} {ro.Cost}/{lv.Par}");
                if (over.Validate(lv) != null || !ro.Delivered || !ro.Careful || ro.UnderBudget) problems.Add("over-budget sample isn't one");
                // budget hints mark exactly the padding of yours that isn't in hers, and count the rest in place
                var srcPad = src.Pieces.Where(q => q.Def.IsPadding).ToList();
                var want = Enumerable.Range(0, over.Pieces.Count).Where(i => over.Pieces[i].Def.IsPadding &&
                    !srcPad.Any(q => q.Kind == over.Pieces[i].Kind && q.X == over.Pieces[i].X && q.Y == over.Pieces[i].Y)).ToList();
                var extras = Hints.Extras(over, lv, 2, lv.Items[0], true);
                int inPlace = Hints.Pieces(lv, 2, lv.Items[0], true).Count(gp => Hints.MatchOf(over, gp) == Hints.Match.InPlace);
                int wantIn = srcPad.Count(q => over.Pieces.Any(m => m.Kind == q.Kind && m.X == q.X && m.Y == q.Y));
                if (!extras.SequenceEqual(want) || inPlace != wantIn) problems.Add($"budget ghosts: {extras.Count} extra and {inPlace} in place, expected {want.Count} and {wantIn}");
                Console.Write($" (extra {extras.Count}, in place {inPlace}/{srcPad.Count})");
            }

            // a packing that misses only the care star (the self-tests ship it): its near misses
            var careless = Hints.CarelessSample(lv);
            if (careless == null) Console.Write($"  careless: none found ({CarelessScan.Explain(lv)})");
            else
            {
                var rc = Simulator.Run(lv, careless);
                Meters(rc, "careless", problems);
                var oc = rc.Outcome;
                int near = Troubles.NearMisses(rc).Count;
                Console.Write($"  careless: {Stars(oc)} care {oc.WorstCare:0.00}, {near} near miss{(near == 1 ? "" : "es")}");
                if (careless.Validate(lv) != null || !oc.Delivered || oc.Careful || !oc.UnderBudget) problems.Add("careless sample isn't one");
                if (near == 0) problems.Add("careless sample has no near miss");
                carelessFound++;
            }

            if (lv.Number > 1)
            {
                var naive = Naive(lv);
                var rn = Simulator.Run(lv, naive);
                Meters(rn, "naive", problems);
                Console.Write($"  naive: {(rn.Outcome.Delivered ? "DELIVERED" : "fails")}");
                if (rn.Outcome.Delivered) problems.Add("naive packing (no materials) is delivered");
            }
            Console.WriteLine();
            foreach (var p in problems) Console.WriteLine("     !! " + p);
            failures += problems.Count;
        }
        Console.WriteLine($"care meters: {metersChecked} item trips end where their review does");
        Console.WriteLine($"hint ghosts: {ghostChecks} ghosts matched against her packing and an empty box");
        Console.WriteLine($"troubles: {troubleTrips} trips checked, {nearMisses} near misses; a careless sample (only the care star missed) on {carelessFound} deliveries");
        if (args.Length == 0)
        {
            var kp = KnockCheck.Run(false, out var ksum);
            Console.WriteLine(ksum);
            foreach (var x in kp) Console.WriteLine("     !! " + x);
            failures += kp.Count;
        }
        Console.WriteLine(failures == 0 ? $"ALL OK ({sw.Elapsed.TotalSeconds:0.0}s)" : $"{failures} PROBLEM(S)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The trip's care meters end where the review does: each item's last recorded care is its
    /// outcome's, and its last recorded state gives the same status.</summary>
    static int metersChecked, ghostChecks, troubleTrips, nearMisses, carelessFound;
    static void Meters(Recording rec, string what, List<string> problems)
    {
        // and its near misses (the replay's amber marks) follow their rules
        troubleTrips++;
        nearMisses += Troubles.NearMisses(rec).Count;
        string tc = Troubles.Check(rec);
        if (tc != null) problems.Add($"{what}: troubles: {tc}");
        var last = rec.Frames[rec.Frames.Count - 1];
        foreach (var it in rec.Outcome.Items)
        {
            metersChecked++;
            var fr = last[it.Body];
            if (fr.Care != it.Care) problems.Add($"{what}: {it.Kind}'s meter ends at {fr.Care:0.000}, the review says {it.Care:0.000}");
            var st = Simulator.StatusOf(fr.State, fr.Care);
            if (st != it.Status) problems.Add($"{what}: {it.Kind}'s meter ends {st}, the review says {it.Status}");
            // the meter never goes down during the trip
            float prev = 0f;
            foreach (var f in rec.Frames)
            {
                if (f[it.Body].Care < prev) { problems.Add($"{what}: {it.Kind}'s meter goes down"); break; }
                prev = f[it.Body].Care;
            }
        }
    }

    /// <summary>Everything the hints show at the final stage, assembled into a packing.</summary>
    public static Packing HintedPacking(LevelDef lv)
    {
        var src = Hints.Source(lv);
        var pk = new Packing(lv.W, lv.H);
        pk.Pieces.AddRange(Hints.Pieces(lv, Hints.MaxStage, lv.Items[0]));
        if (Hints.ShowsStatics(Hints.MaxStage)) { pk.Dividers.AddRange(src.Dividers); pk.Shelves.AddRange(src.Shelves); }
        return pk;
    }

    static int PrintHints()
    {
        foreach (var lv in Levels.All)
        {
            Console.WriteLine($"#{lv.Number,2} {lv.Title}");
            foreach (var focus in lv.Items.Distinct())
                Console.WriteLine($"     [{focus}] {Hints.Note(lv, 1, focus)}");
            for (int st = 2; st <= Hints.MaxStage; st++) Console.WriteLine($"     {st}: {Hints.Note(lv, st, lv.Items[0])}");
            Console.WriteLine($"     [budget] {Hints.Note(lv, 1, lv.Items[0], true)}");
            Console.WriteLine($"     2: {Hints.Note(lv, 2, lv.Items[0], true)}");
        }
        return 0;
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
