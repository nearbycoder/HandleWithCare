using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HWC.Sim;

/// <summary>Randomised exploration and local-search solving of deliveries (design tools).</summary>
static class Search
{
    static readonly PieceKind[] Pads = { PieceKind.Paper, PieceKind.Bubble, PieceKind.Foam };

    /// <summary>Random item-only packings: does anything get delivered without materials?</summary>
    public static int Explore(int n, int samples)
    {
        var lv = Levels.Get(n);
        _ = lv.Kinematics;
        int delivered = 0;
        var found = new List<string>();
        object gate = new object();
        Parallel.For(0, samples, new ParallelOptions { MaxDegreeOfParallelism = 24 }, i =>
        {
            var rnd = new Random(i * 7919 + 13);
            var pk = new Packing(lv.W, lv.H);
            foreach (var k in lv.Items.OrderBy(_ => rnd.Next()))
                if (!PlaceRandom(pk, lv, new Placement(k, 0, 0), rnd)) return;
            if (pk.Validate(lv) != null) return;
            var rec = Simulator.Run(lv, pk, false);
            if (rec.Outcome.Delivered)
                lock (gate) { delivered++; if (found.Count < 4) found.Add(pk.ToAscii() + $"   care {rec.Outcome.WorstCare:0.00}"); }
        });
        Console.WriteLine($"#{n} {lv.Title}: {delivered}/{samples} item-only packings delivered");
        foreach (var f in found) Console.WriteLine(f);
        return 0;
    }

    static bool PlaceRandom(Packing pk, LevelDef lv, Placement p, Random rnd)
    {
        var def = p.Def;
        var options = new List<Placement>();
        bool[] rots = def.Rotatable ? new[] { false, true } : new[] { false };
        int[] faces = def.Has(Quirk.Facing) ? new[] { 1, -1 } : new[] { p.Facing == 0 ? 1 : p.Facing };
        foreach (var r in rots)
            foreach (var f in faces)
                for (int y = 0; y < lv.H; y++)
                    for (int x = 0; x < lv.W; x++)
                    {
                        var q = new Placement(p.Kind, x, y, r, f, p.Strapped);
                        if (pk.CanPlace(q)) options.Add(q);
                    }
        if (options.Count == 0) return false;
        pk.Pieces.Add(options[rnd.Next(options.Count)]);
        return true;
    }

    static double Score(Recording rec)
    {
        var o = rec.Outcome;
        double s = 0;
        if (o.Delivered) s += 1000 + (o.Careful ? 600 : 0);
        else s -= 100 * o.Items.Count(i => i.Failed);
        s -= o.Cost * 10;
        s -= Math.Min(3, o.WorstCare) * 120;
        return s;
    }

    /// <summary>Local search from the reference (and from scratch) for cheap / three-star packings.</summary>
    public static int Solve(int n, int iters, int restarts)
    {
        var lv = Levels.Get(n);
        _ = lv.Kinematics;
        var results = new (double score, Packing pk, Recording rec)[restarts];
        Parallel.For(0, restarts, new ParallelOptions { MaxDegreeOfParallelism = 24 }, r =>
        {
            var rnd = new Random(r * 104729 + n);
            Packing refPk = null;
            try { refPk = lv.ReferencePacking(); if (refPk.Validate(lv) != null) refPk = null; } catch { refPk = null; }
            Packing cur;
            if (r % 4 == 0 && refPk != null) cur = refPk;
            else if (r % 4 == 1) cur = FloorFirst(lv, rnd);
            else cur = RandomFull(lv, rnd);
            if (cur != null && r % 4 == 2) cur = FillAll(cur, lv, rnd);
            for (int tries = 0; cur == null && tries < 50; tries++) cur = RandomFull(lv, rnd);
            if (cur == null) return;
            var rec = Simulator.Run(lv, cur, false);
            double sc = Score(rec);
            var best = (sc, cur.Clone(), rec);
            double temp = 60;
            for (int it = 0; it < iters; it++)
            {
                var cand = Mutate(cur, lv, rnd);
                if (cand == null || cand.Validate(lv) != null || !cand.AllItemsPlaced(lv)) continue;
                var cr = Simulator.Run(lv, cand, false);
                double cs = Score(cr);
                if (cs >= sc || rnd.NextDouble() < Math.Exp((cs - sc) / temp))
                {
                    cur = cand; sc = cs; rec = cr;
                    if (cs > best.sc) best = (cs, cand.Clone(), cr);
                }
                temp = Math.Max(2, temp * 0.995);
            }
            results[r] = best;
        });
        foreach (var res in results.Where(x => x.pk != null).OrderByDescending(x => x.score).Take(4))
        {
            var o = res.rec.Outcome;
            Console.WriteLine($"score {res.score:0} {Program.Stars(o)} cost {o.Cost}/{lv.Par} care {o.WorstCare:0.00} delivered {o.Delivered}");
            Console.Write(res.pk.ToAscii());
            Console.WriteLine("   map: " + string.Join(" ", MapRows(res.pk)) + (res.pk.Dividers.Count > 0 ? " --div " + string.Join(",", res.pk.Dividers) : "") +
                (res.pk.Shelves.Count > 0 ? " --shelf " + string.Join(",", res.pk.Shelves.Select(s => s.Row + "@" + s.Col)) : "") + Mods(res.pk));
        }
        return 0;
    }

    static string Mods(Packing pk)
    {
        var m = new List<string>();
        foreach (var p in pk.Pieces)
        {
            string f = (p.Facing < 0 ? "L" : "") + (p.Strapped ? "S" : "");
            if (f.Length > 0) m.Add($"{p.X},{p.Y}:{f}");
        }
        return m.Count > 0 ? " --mods \"" + string.Join(";", m) + "\"" : "";
    }

    public static IEnumerable<string> MapRows(Packing pk)
    {
        for (int y = pk.H - 1; y >= 0; y--)
        {
            var row = new char[pk.W];
            for (int x = 0; x < pk.W; x++)
            {
                int i = pk.PieceAt(x, y);
                row[x] = i < 0 ? '.' : (pk.Pieces[i].Rotated ? char.ToUpperInvariant(Packing.Glyph(pk.Pieces[i].Kind)) : Packing.Glyph(pk.Pieces[i].Kind));
            }
            yield return new string(row);
        }
    }

    /// <summary>Fills every reachable gap with the softest padding still available.</summary>
    static Packing FillAll(Packing src, LevelDef lv, Random rnd)
    {
        var pk = src.Clone();
        for (int y = 0; y < lv.H; y++)
            for (int x = 0; x < lv.W; x++)
            {
                var used = pk.UsedMaterials();
                PieceKind? k = null;
                foreach (var cand in new[] { PieceKind.Foam, PieceKind.Bubble, PieceKind.Paper })
                    if (used.Get(Slot(cand)) < lv.Materials.Get(Slot(cand)) && rnd.NextDouble() < 0.85) { k = cand; break; }
                if (k == null) continue;
                var p = new Placement(k.Value, x, y);
                if (pk.CanPlace(p)) pk.Pieces.Add(p);
            }
        return pk;
    }

    /// <summary>Soft floor first (foam, then bubble), items on top, then the gaps filled.</summary>
    static Packing FloorFirst(LevelDef lv, Random rnd)
    {
        var pk = new Packing(lv.W, lv.H);
        for (int x = 0; x < lv.W; x++)
        {
            var used = pk.UsedMaterials();
            PieceKind? k = null;
            foreach (var cand in new[] { PieceKind.Foam, PieceKind.Bubble })
                if (used.Get(Slot(cand)) < lv.Materials.Get(Slot(cand))) { k = cand; break; }
            if (k == null) break;
            pk.Pieces.Add(new Placement(k.Value, x, 0));
        }
        foreach (var k in lv.Items.OrderBy(_ => rnd.Next()))
            if (!PlaceRandom(pk, lv, new Placement(k, 0, 0), rnd)) return RandomFull(lv, rnd);
        return FillAll(pk, lv, rnd);
    }

    static Packing RandomFull(LevelDef lv, Random rnd)
    {
        var pk = new Packing(lv.W, lv.H);
        foreach (var k in lv.Items.OrderBy(_ => rnd.Next()))
            if (!PlaceRandom(pk, lv, new Placement(k, 0, 0), rnd)) return null;
        return pk;
    }

    static Packing Mutate(Packing src, LevelDef lv, Random rnd)
    {
        var pk = src.Clone();
        var used = pk.UsedMaterials();
        int op = rnd.Next(13);
        switch (op)
        {
            case 10:
            case 11:
            {
                // lift a piece onto new padding placed where it stood
                if (pk.Pieces.Count == 0) return null;
                var avail = Pads.Where(k => used.Get(Slot(k)) < lv.Materials.Get(Slot(k))).ToList();
                if (avail.Count == 0) return null;
                int i = rnd.Next(pk.Pieces.Count);
                var p = pk.Pieces[i];
                if (p.Y + p.H >= lv.H) return null;
                pk.Pieces.RemoveAt(i);
                var pad = new Placement(avail[rnd.Next(avail.Count)], p.X + rnd.Next(p.W), p.Y);
                if (!pk.CanPlace(pad)) return null;
                pk.Pieces.Add(pad);
                var lifted = p; lifted.Y = p.Y + 1;
                if (!pk.CanPlace(lifted)) return null;
                pk.Pieces.Add(lifted);
                break;
            }
            case 12:
            {
                // swap a padding piece for another kind
                var idx = Enumerable.Range(0, pk.Pieces.Count).Where(i => pk.Pieces[i].Def.IsPadding).ToList();
                if (idx.Count == 0) return null;
                int i = idx[rnd.Next(idx.Count)];
                var p = pk.Pieces[i];
                var other = Pads.Where(k => k != p.Kind && used.Get(Slot(k)) < lv.Materials.Get(Slot(k))).ToList();
                if (other.Count == 0) return null;
                p.Kind = other[rnd.Next(other.Count)];
                pk.Pieces[i] = p;
                break;
            }
            case 0:
            case 1:
            case 2:
            {
                // add padding
                var avail = Pads.Where(k => used.Get(Slot(k)) < lv.Materials.Get(Slot(k))).ToList();
                if (avail.Count == 0) return null;
                if (!PlaceRandom(pk, lv, new Placement(avail[rnd.Next(avail.Count)], 0, 0), rnd)) return null;
                break;
            }
            case 3:
            case 4:
            {
                // remove padding
                var idx = Enumerable.Range(0, pk.Pieces.Count).Where(i => pk.Pieces[i].Def.IsPadding).ToList();
                if (idx.Count == 0) return null;
                pk.Pieces.RemoveAt(idx[rnd.Next(idx.Count)]);
                pk.Settle();
                break;
            }
            case 5:
            case 6:
            {
                // move any piece
                if (pk.Pieces.Count == 0) return null;
                int i = rnd.Next(pk.Pieces.Count);
                var p = pk.Pieces[i];
                pk.Pieces.RemoveAt(i);
                pk.Settle();
                if (!PlaceRandom(pk, lv, p, rnd)) return null;
                pk.Settle();
                break;
            }
            case 7:
            {
                // strap toggle
                var items = Enumerable.Range(0, pk.Pieces.Count).Where(i => !pk.Pieces[i].Def.IsPadding).ToList();
                if (items.Count == 0) return null;
                int i = items[rnd.Next(items.Count)];
                var p = pk.Pieces[i];
                if (!p.Strapped && used.Strap >= lv.Materials.Strap) return null;
                p.Strapped = !p.Strapped;
                pk.Pieces[i] = p;
                break;
            }
            case 8:
            {
                // divider toggle
                if (lv.Materials.Divider == 0) return null;
                int line = 1 + rnd.Next(lv.W - 1);
                if (pk.Dividers.Contains(line)) pk.Dividers.Remove(line);
                else if (used.Divider < lv.Materials.Divider && pk.CanPlaceDivider(line)) pk.Dividers.Add(line);
                else return null;
                break;
            }
            default:
            {
                // shelf toggle or facing flip
                if (lv.Materials.Shelf > 0 && rnd.Next(2) == 0)
                {
                    int row = 1 + rnd.Next(lv.H - 1), col = rnd.Next(lv.W);
                    int existing = pk.Shelves.FindIndex(s => s.Row == row && s.Col == col);
                    if (existing >= 0) { pk.Shelves.RemoveAt(existing); pk.Settle(); }
                    else if (used.Shelf < lv.Materials.Shelf && pk.CanPlaceShelf(row, col)) pk.Shelves.Add(new ShelfSpec(row, col));
                    else return null;
                }
                else
                {
                    var faceIdx = Enumerable.Range(0, pk.Pieces.Count).Where(i => pk.Pieces[i].Def.Has(Quirk.Facing)).ToList();
                    if (faceIdx.Count == 0) return null;
                    int i = faceIdx[rnd.Next(faceIdx.Count)];
                    var p = pk.Pieces[i];
                    p.Facing = -p.Facing;
                    pk.Pieces[i] = p;
                }
                break;
            }
        }
        return pk;
    }

    static MaterialSlot Slot(PieceKind k) => k == PieceKind.Paper ? MaterialSlot.Paper : (k == PieceKind.Bubble ? MaterialSlot.Bubble : MaterialSlot.Foam);
}
