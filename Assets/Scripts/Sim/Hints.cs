using System.Collections.Generic;

namespace HWC.Sim
{
    /// <summary>
    /// "Ask Mabel": escalating hints drawn from a delivery's reference packing.
    ///   1  a note about the item at risk: where it sits in Mabel's packing and what is beside it
    ///   2  that item's exact spot (a ghost in the box)
    ///   3  Mabel's dividers and shelves
    ///   4  the whole packing
    /// After a trip that only missed the budget star, the hints are about money instead:
    ///   1  what her packing costs and what it uses (and leaves out)
    ///   2  where her padding goes
    ///   3  her dividers and shelves too
    ///   4  the whole packing
    /// Pure C# so SimCheck can prove that the final stage is a three-star packing for every delivery.
    /// </summary>
    public static class Hints
    {
        public const int MaxStage = 4;
        /// <summary>The saved hint focus for budget hints (item hints store a PieceKind).</summary>
        public const int BudgetFocus = -2;

        /// <summary>The trip arrived safely and gently, but cost more than par: only the budget star is missing.</summary>
        public static bool OnlyOverBudget(LevelDef lv, Recording last) =>
            last != null && last.Outcome != null && last.Level == lv && last.Outcome.Delivered && last.Outcome.Careful && !last.Outcome.UnderBudget;

        /// <summary>The hint focus to save when Mabel is first asked: the budget, or an item.</summary>
        public static int FocusId(LevelDef lv, Recording last) => OnlyOverBudget(lv, last) ? BudgetFocus : (int)Focus(lv, last);

        /// <summary>The packing hints are drawn from (the separate three-star one when there is one).</summary>
        public static Packing Source(LevelDef lv) => lv.Reference3Packing() ?? lv.ReferencePacking();

        /// <summary>The item the hint is about: the first one that failed last trip, else the one that came
        /// closest to its limit, else the order's first item.</summary>
        public static PieceKind Focus(LevelDef lv, Recording last)
        {
            if (last != null && last.Outcome != null && last.Level == lv)
            {
                // the first item that failed...
                float first = float.MaxValue;
                PieceKind? failed = null;
                foreach (var inc in last.Incidents)
                {
                    if (!inc.IsFailure || inc.Body < 0 || inc.Body >= last.Bodies.Length) continue;
                    var k = last.Bodies[inc.Body].Kind;
                    if (!Catalog.Get(k).IsPadding && inc.Time < first) { first = inc.Time; failed = k; }
                }
                if (failed.HasValue) return failed.Value;
                // ...or the one that came closest to its limit
                ItemResult worst = null;
                foreach (var it in last.Outcome.Items) if (worst == null || it.Care > worst.Care) worst = it;
                if (worst != null) return worst.Kind;
            }
            return lv.Items[0];
        }

        /// <summary>The ghost pieces shown at a stage (dividers and shelves come from <see cref="Statics"/>).</summary>
        public static List<Placement> Pieces(LevelDef lv, int stage, PieceKind focus, bool budget = false)
        {
            var list = new List<Placement>();
            if (stage < 2) return list;
            foreach (var p in Source(lv).Pieces)
                if (stage >= 4 || (budget ? p.Def.IsPadding : p.Kind == focus)) list.Add(p);
            return list;
        }

        public static bool ShowsStatics(int stage) => stage >= 3;

        // ---- what the box already matches -------------------------------------------------------------

        public enum Match { Open, InPlace, Blocked }

        /// <summary>The same piece in the same spot, the same way round, strapped the same, and facing the same
        /// way when it faces at all.</summary>
        public static bool Same(Placement a, Placement b) =>
            a.Kind == b.Kind && a.X == b.X && a.Y == b.Y && a.Rotated == b.Rotated && a.Strapped == b.Strapped &&
            (!a.Def.Has(Quirk.Facing) || a.Facing == b.Facing);

        /// <summary>A hint ghost against the box: matched, its spot taken by something else (a piece, a
        /// divider or a shelf in the way), or still open.</summary>
        public static Match MatchOf(Packing mine, Placement ghost)
        {
            foreach (var p in mine.Pieces) if (Same(ghost, p)) return Match.InPlace;
            return mine.AreaFree(ghost.X, ghost.Y, ghost.W, ghost.H) && !mine.CrossesStatics(ghost.X, ghost.Y, ghost.W, ghost.H) ? Match.Open : Match.Blocked;
        }

        public static bool HasDivider(Packing mine, int line) => mine.Dividers.Contains(line);

        /// <summary>Her shelf is in the box when one sits on the same line across the same columns.</summary>
        public static bool HasShelf(Packing mine, Packing src, ShelfSpec s)
        {
            src.ShelfSpan(s, out int x0, out int x1);
            foreach (var m in mine.Shelves)
            {
                if (m.Row != s.Row) continue;
                mine.ShelfSpan(m, out int a0, out int a1);
                if (a0 == x0 && a1 == x1) return true;
            }
            return false;
        }

        /// <summary>Whether a stage shows everything of its kind, so a piece of yours that isn't among the
        /// ghosts is one too many: her padding (budget hints from stage 2), her whole packing (stage 4).</summary>
        public static bool ShowsAll(int stage, bool budget) => stage >= MaxStage || (budget && stage >= 2);

        /// <summary>Your pieces that her packing doesn't have at this stage (indices into mine.Pieces): your
        /// extra padding for budget hints, anything not in hers at the last stage. Empty for the other stages.</summary>
        public static List<int> Extras(Packing mine, LevelDef lv, int stage, PieceKind focus, bool budget)
        {
            var list = new List<int>();
            if (!ShowsAll(stage, budget)) return list;
            var ghosts = Pieces(lv, stage, focus, budget);
            for (int i = 0; i < mine.Pieces.Count; i++)
            {
                var p = mine.Pieces[i];
                if (stage < MaxStage && !p.Def.IsPadding) continue;   // budget hints are about padding only
                bool found = false;
                foreach (var g in ghosts) if (Same(g, p)) { found = true; break; }
                if (!found) list.Add(i);
            }
            return list;
        }

        public static string Note(LevelDef lv, int stage, PieceKind focus, bool budget = false)
        {
            var src = Source(lv);
            switch (stage)
            {
                case 1: return budget ? Budget(lv, src) : Describe(src, focus);
                case 2:
                    if (!budget)
                    {
                        // keep what stage 1 said about where it sits: the ghost alone can float in mid-air
                        string where = Where(src, focus);
                        return where == null ? $"Here's exactly where I'd put {Article(focus)}." : $"Here's exactly where I'd put {Article(focus)}: {where}.";
                    }
                    return src.UsedMaterials().Paper + src.UsedMaterials().Bubble + src.UsedMaterials().Foam == 0
                        ? "No padding at all in mine. The box does the work." : "Here's where my padding goes. Nothing more.";
                case 3:
                    return src.Dividers.Count + src.Shelves.Count == 0
                        ? "No dividers or shelves in mine. Padding does the work."
                        : $"And here {Count(src.Dividers.Count, "divider")}{(src.Dividers.Count > 0 && src.Shelves.Count > 0 ? " and " : "")}{Count(src.Shelves.Count, "shelf", "shelves")} go{(src.Dividers.Count + src.Shelves.Count == 1 ? "es" : "")}.";
                default: return "That's my whole packing. Copy it and it'll arrive perfect.";
            }
        }

        /// <summary>Budget stage 1: what her packing costs, what it uses and what it leaves out.</summary>
        static string Budget(LevelDef lv, Packing src)
        {
            var m = src.UsedMaterials();
            var used = new List<string>();
            var unused = new List<string>();
            foreach (MaterialSlot s in System.Enum.GetValues(typeof(MaterialSlot)))
            {
                int n = m.Get(s);
                if (n > 0) used.Add(MaterialCount(s, n));
                else if (lv.Materials.Get(s) > 0) unused.Add(MaterialName(s, 2));
            }
            string uses = used.Count == 0 ? "nothing at all" : Join(used);
            string none = unused.Count == 0 ? "" : " No " + Join(unused, "or") + ".";
            return $"Mine costs {m.Cost} (par {lv.Par}): {uses}.{none}";
        }

        static string MaterialCount(MaterialSlot s, int n)
        {
            if (s == MaterialSlot.Paper || s == MaterialSlot.Bubble || s == MaterialSlot.Foam) return $"{n} {MaterialName(s, n)}";
            return n == 1 ? "a " + MaterialName(s, 1) : $"{n} {MaterialName(s, n)}";
        }

        static string MaterialName(MaterialSlot s, int n)
        {
            switch (s)
            {
                case MaterialSlot.Paper: return "paper";
                case MaterialSlot.Bubble: return "bubble wrap";
                case MaterialSlot.Foam: return "foam";
                case MaterialSlot.Divider: return n == 1 ? "divider" : "dividers";
                case MaterialSlot.Shelf: return n == 1 ? "shelf" : "shelves";
                default: return n == 1 ? "strap" : "straps";
            }
        }

        static string Join(List<string> parts, string and = "and") =>
            parts.Count == 1 ? parts[0] : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + $" {and} " + parts[parts.Count - 1];

        /// <summary>
        /// For the self-tests: Mabel's packing made dearer (cheap padding swapped for dearer padding, then
        /// extra padding in empty spots) until it costs more than par, keeping the first one that still
        /// arrives safely and gently. Null when none turns up: that delivery can't miss only the budget star
        /// this way.
        /// </summary>
        public static Packing OverBudgetSample(LevelDef lv, int maxTries = 60, int randomTries = 400)
        {
            int tries = 0;
            foreach (var pk in Dearer(lv, Source(lv)))
            {
                if (tries++ >= maxTries) break;
                var o = Simulator.Run(lv, pk, false).Outcome;
                if (o.Delivered && o.Careful && !o.UnderBudget) return pk;
            }
            // nothing in order: seeded random changes (the same ones every time)
            tries = 0;
            foreach (var pk in DearerRandom(lv, Source(lv), lv.Seed))
            {
                if (tries++ >= randomTries) break;
                var o = Simulator.Run(lv, pk, false).Outcome;
                if (o.Delivered && o.Careful && !o.UnderBudget) return pk;
            }
            return null;
        }

        /// <summary>
        /// For the self-tests: Mabel's packing made careless until it still arrives, under par, but rattles
        /// something past the care line: a trip that misses only the care star. First padding taken away, or
        /// swapped for a cheaper kind (one piece, then two, then seeded random changes); then her items
        /// rearranged (an item swapped with a piece of padding, or moved elsewhere in the box, then seeded
        /// random mixes of all of these). Null when none turns up.
        /// </summary>
        public static Packing CarelessSample(LevelDef lv, int maxTries = 120, int moreTries = 300)
        {
            int tries = 0;
            foreach (var pk in Careless(lv, Source(lv)))
            {
                if (tries++ >= maxTries) break;
                if (IsCareless(lv, pk)) return pk;
            }
            tries = 0;
            foreach (var pk in Rearranged(lv, Source(lv)))
            {
                if (tries++ >= moreTries) break;
                if (IsCareless(lv, pk)) return pk;
            }
            return null;
        }

        static bool IsCareless(LevelDef lv, Packing pk)
        {
            var o = Simulator.Run(lv, pk, false).Outcome;
            return o.Delivered && !o.Careful && o.UnderBudget;
        }

        /// <summary>Her items in other places: each swapped with a piece of padding, each moved to every spot it
        /// fits, then seeded random mixes of moves, swaps, padding taken away or made cheaper, and a divider or a
        /// shelf gone.</summary>
        static IEnumerable<Packing> Rearranged(LevelDef lv, Packing src)
        {
            for (int i = 0; i < src.Pieces.Count; i++)
            {
                if (src.Pieces[i].Def.IsPadding) continue;
                for (int j = 0; j < src.Pieces.Count; j++)
                {
                    if (!src.Pieces[j].Def.IsPadding) continue;
                    var pk = src.Clone();
                    Swap(pk, i, j);
                    if (pk.Validate(lv) == null) yield return pk;
                }
            }
            for (int i = 0; i < src.Pieces.Count; i++)
            {
                if (src.Pieces[i].Def.IsPadding) continue;
                for (int y = 0; y < src.H; y++)
                    for (int x = 0; x < src.W; x++)
                    {
                        var pk = src.Clone();
                        var p = pk.Pieces[i];
                        if (p.X == x && p.Y == y) continue;
                        p.X = x; p.Y = y;
                        if (!pk.CanPlace(p, i)) continue;
                        pk.Pieces[i] = p;
                        if (pk.Validate(lv) == null) yield return pk;
                    }
            }
            uint state = lv.Seed * 2246822519u + 991u;
            int Next(int n) { state = state * 1664525u + 1013904223u; return (int)((state >> 8) % (uint)n); }
            var items = new List<int>();
            var pads = new List<int>();
            for (int attempt = 0; attempt < 4000; attempt++)
            {
                var pk = src.Clone();
                int steps = 1 + Next(4);
                for (int s = 0; s < steps; s++)
                {
                    items.Clear(); pads.Clear();
                    for (int i = 0; i < pk.Pieces.Count; i++) (pk.Pieces[i].Def.IsPadding ? pads : items).Add(i);
                    int op = Next(5);
                    if (op == 0 && pads.Count > 0) pk.Pieces.RemoveAt(pads[Next(pads.Count)]);
                    else if (op == 1 && pads.Count > 0) { int k = pads[Next(pads.Count)]; var p = pk.Pieces[k]; p.Kind = PieceKind.Paper; pk.Pieces[k] = p; }
                    else if (op == 2 && pk.Dividers.Count + pk.Shelves.Count > 0)
                    {
                        int j = Next(pk.Dividers.Count + pk.Shelves.Count);
                        if (j < pk.Dividers.Count) pk.Dividers.RemoveAt(j); else pk.Shelves.RemoveAt(j - pk.Dividers.Count);
                    }
                    else if (op == 3 && items.Count > 0)
                    {
                        int i = items[Next(items.Count)];
                        var p = pk.Pieces[i];
                        p.X = Next(pk.W); p.Y = Next(pk.H);
                        if (Next(3) == 0) p.Strapped = false;
                        if (pk.CanPlace(p, i)) pk.Pieces[i] = p;
                    }
                    else if (op == 4 && items.Count > 0 && pads.Count > 0) Swap(pk, items[Next(items.Count)], pads[Next(pads.Count)]);
                }
                if (pk.Validate(lv) == null) yield return pk;
            }
        }

        static void Swap(Packing pk, int i, int j)
        {
            var a = pk.Pieces[i]; var b = pk.Pieces[j];
            (a.X, a.Y, b.X, b.Y) = (b.X, b.Y, a.X, a.Y);
            pk.Pieces[i] = a; pk.Pieces[j] = b;
        }

        static IEnumerable<Packing> Careless(LevelDef lv, Packing src)
        {
            var pads = new List<int>();
            for (int i = 0; i < src.Pieces.Count; i++) if (src.Pieces[i].Def.IsPadding) pads.Add(i);
            // one piece of padding cheaper, then one taken away
            foreach (var (from, to) in new[] { (PieceKind.Foam, PieceKind.Paper), (PieceKind.Bubble, PieceKind.Paper), (PieceKind.Foam, PieceKind.Bubble) })
                foreach (int i in pads)
                {
                    if (src.Pieces[i].Kind != from) continue;
                    var pk = src.Clone();
                    var p = pk.Pieces[i]; p.Kind = to; pk.Pieces[i] = p;
                    if (pk.Validate(lv) == null) yield return pk;
                }
            foreach (int i in pads)
            {
                var pk = src.Clone();
                pk.Pieces.RemoveAt(i);
                if (pk.Validate(lv) == null) yield return pk;
            }
            // two taken away
            for (int a = 0; a < pads.Count; a++)
                for (int b = a + 1; b < pads.Count; b++)
                {
                    var pk = src.Clone();
                    pk.Pieces.RemoveAt(pads[b]);
                    pk.Pieces.RemoveAt(pads[a]);
                    if (pk.Validate(lv) == null) yield return pk;
                }
            // seeded random: a few pieces of padding cheaper or gone, a divider or a shelf gone
            uint state = lv.Seed * 2246822519u + 777u;
            int Next(int n) { state = state * 1664525u + 1013904223u; return (int)((state >> 8) % (uint)n); }
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                var pk = src.Clone();
                int steps = 1 + Next(4);
                for (int s = 0; s < steps; s++)
                {
                    int op = Next(4);
                    if (op == 3 && pk.Dividers.Count + pk.Shelves.Count > 0)
                    {
                        int j = Next(pk.Dividers.Count + pk.Shelves.Count);
                        if (j < pk.Dividers.Count) pk.Dividers.RemoveAt(j); else pk.Shelves.RemoveAt(j - pk.Dividers.Count);
                        continue;
                    }
                    var idx = new List<int>();
                    for (int i = 0; i < pk.Pieces.Count; i++) if (pk.Pieces[i].Def.IsPadding) idx.Add(i);
                    if (idx.Count == 0) continue;
                    int k = idx[Next(idx.Count)];
                    if (op == 0) pk.Pieces.RemoveAt(k);
                    else { var p = pk.Pieces[k]; p.Kind = p.Kind == PieceKind.Foam && op == 1 ? PieceKind.Bubble : PieceKind.Paper; pk.Pieces[k] = p; }
                }
                if (pk.Validate(lv) == null) yield return pk;
            }
        }

        /// <summary>
        /// Her packing with a few random changes until it costs more than par: padding swapped for a dearer
        /// kind, extra padding wherever it can rest, or a strap on a piece. Deterministic (seeded).
        /// </summary>
        static IEnumerable<Packing> DearerRandom(LevelDef lv, Packing src, uint seed)
        {
            uint state = seed * 2654435761u + 12345u;
            int Next(int n) { state = state * 1664525u + 1013904223u; return (int)((state >> 8) % (uint)n); }
            var kinds = new[] { PieceKind.Paper, PieceKind.Bubble, PieceKind.Foam };
            for (int attempt = 0; attempt < 4000; attempt++)
            {
                var pk = src.Clone();
                for (int step = 0; step < 12 && pk.Cost <= lv.Par; step++)
                {
                    int op = Next(3);
                    if (op == 0 && pk.Pieces.Count > 0)
                    {
                        int i = Next(pk.Pieces.Count);
                        var p = pk.Pieces[i];
                        if (p.Kind != PieceKind.Paper && p.Kind != PieceKind.Bubble) continue;
                        var was = p.Kind;
                        p.Kind = p.Kind == PieceKind.Paper && Next(2) == 0 ? PieceKind.Bubble : PieceKind.Foam;
                        pk.Pieces[i] = p;
                        if (pk.Validate(lv) != null) { p.Kind = was; pk.Pieces[i] = p; }
                    }
                    else if (op == 1)
                    {
                        var p = new Placement(kinds[Next(3)], Next(pk.W), Next(pk.H));
                        if (!pk.CanPlace(p)) continue;
                        pk.Pieces.Add(p);
                        if (pk.Validate(lv) != null) pk.Pieces.RemoveAt(pk.Pieces.Count - 1);
                    }
                    else if (pk.Pieces.Count > 0)
                    {
                        int i = Next(pk.Pieces.Count);
                        var p = pk.Pieces[i];
                        if (p.Strapped) continue;
                        p.Strapped = true; pk.Pieces[i] = p;
                        if (pk.Validate(lv) != null) { p.Strapped = false; pk.Pieces[i] = p; }
                    }
                }
                if (pk.Cost > lv.Par && pk.Validate(lv) == null) yield return pk;
            }
        }

        static IEnumerable<Packing> Dearer(LevelDef lv, Packing src)
        {
            // swaps, one piece at a time: paper or bubble wrap -> foam, then paper -> bubble wrap
            foreach (var (from, to) in new[] { (PieceKind.Paper, PieceKind.Foam), (PieceKind.Bubble, PieceKind.Foam), (PieceKind.Paper, PieceKind.Bubble) })
            {
                var pk = src.Clone();
                for (int i = 0; i < pk.Pieces.Count; i++)
                {
                    if (pk.Pieces[i].Kind != from) continue;
                    var p = pk.Pieces[i]; p.Kind = to; pk.Pieces[i] = p;
                    if (pk.Validate(lv) != null) break;   // out of that material
                    if (pk.Cost > lv.Par) { yield return pk.Clone(); }
                }
            }
            // extra padding, bottom-up, in spots where it can rest
            foreach (var kind in new[] { PieceKind.Paper, PieceKind.Bubble, PieceKind.Foam })
            {
                var pk = src.Clone();
                for (int y = 0; y < pk.H; y++)
                    for (int x = 0; x < pk.W; x++)
                    {
                        var p = new Placement(kind, x, y);
                        if (!pk.CanPlace(p)) continue;
                        pk.Pieces.Add(p);
                        if (pk.Validate(lv) != null) { pk.Pieces.RemoveAt(pk.Pieces.Count - 1); continue; }
                        if (pk.Cost > lv.Par) yield return pk.Clone();
                    }
            }
        }

        static string Count(int n, string one, string many = null)
        {
            if (n == 0) return "";
            string w = n == 1 ? one : (many ?? one + "s");
            return n == 1 ? (one == "shelf" ? "the shelf" : "the " + one) : $"the {n} {w}";
        }

        /// <summary>Stage 1: where the focus item sits in the reference and what's on either side.</summary>
        static string Describe(Packing src, PieceKind focus)
        {
            int idx = -1;
            for (int i = 0; i < src.Pieces.Count; i++) if (src.Pieces[i].Kind == focus) { idx = i; break; }
            if (idx < 0) return "Watch what failed last time, and give it some company.";
            var p = src.Pieces[idx];
            var def = p.Def;
            string rest = Rest(src, p, idx);
            string left = Side(src, p.X, p.Y, p.H, -1, idx, focus);
            string right = Side(src, p.X + p.W, p.Y, p.H, +1, idx, focus);
            string extra = "";
            if (def.Has(Quirk.Facing)) extra += p.Facing < 0 ? ", facing left" : ", facing right";
            if (p.Rotated) extra += ", lying on its side";
            if (p.Strapped) extra += ", strapped down";
            // the game's named characters (Snoozles, Clank, Ember) are "him" in the order notes
            bool named = focus == PieceKind.Armadillo || focus == PieceKind.Robot || focus == PieceKind.Dragon;
            string he = named ? "he" : "it", his = named ? "his" : "its", him = named ? "him" : "it";
            string sides = left == right ? (left == "nothing" ? $"nothing beside {him}" : $"{left} on both sides") : $"{left} on {his} left and {right} on {his} right";
            return $"Mind {Article(focus)}. In my packing {he} sits {rest}{extra}, with {sides}.";
        }

        /// <summary>What a piece sits on (or, floating, is tucked under).</summary>
        static string Rest(Packing src, Placement p, int idx)
        {
            if (p.Def.Has(Quirk.Floats))
                return p.Y + p.H >= src.H ? "up against the lid" : $"tucked under {Row(src, p, p.Y + p.H, idx, "the lid")}";
            if (p.Y == 0) return "on the floor";
            if (src.ShelfCovers(p.Y, p.X)) return "on a shelf";
            return "on top of " + Row(src, p, p.Y - 1, idx, "the floor");
        }

        /// <summary>Stage 2's reminder of where the item sits in her packing (and which way round), or null.</summary>
        static string Where(Packing src, PieceKind focus)
        {
            int idx = -1;
            for (int i = 0; i < src.Pieces.Count; i++) if (src.Pieces[i].Kind == focus) { idx = i; break; }
            if (idx < 0) return null;
            var p = src.Pieces[idx];
            string extra = "";
            if (p.Def.Has(Quirk.Facing)) extra += p.Facing < 0 ? ", facing left" : ", facing right";
            if (p.Rotated) extra += ", lying on its side";
            if (p.Strapped) extra += ", strapped down";
            return Rest(src, p, idx) + extra;
        }

        /// <summary>What lies along the row just under (or over) a piece.</summary>
        static string Row(Packing src, Placement p, int y, int self, string none)
        {
            if (y < 0 || y >= src.H) return none;
            var seen = new List<PieceKind>();
            for (int x = p.X; x < p.X + p.W; x++)
            {
                int j = src.PieceAt(x, y, self);
                if (j >= 0 && !seen.Contains(src.Pieces[j].Kind)) seen.Add(src.Pieces[j].Kind);
            }
            if (seen.Count == 0) return none;
            var parts = new List<string>();
            foreach (var k in seen) parts.Add(Article(k));
            return string.Join(" and ", parts);
        }

        /// <summary>What touches the piece's side at grid line <paramref name="col"/> (a wall, a divider, a neighbour).</summary>
        static string Side(Packing src, int col, int y, int h, int dir, int self, PieceKind focus)
        {
            // col is the grid line between the piece and its neighbour
            if (col <= 0 || col >= src.W) return "the box wall";
            if (src.Dividers.Contains(col)) return "a divider";
            int cx = dir < 0 ? col - 1 : col;
            var seen = new List<PieceKind>();
            for (int yy = y; yy < y + h; yy++)
            {
                int j = src.PieceAt(cx, yy, self);
                if (j >= 0 && !seen.Contains(src.Pieces[j].Kind)) seen.Add(src.Pieces[j].Kind);
            }
            if (seen.Count == 0) return "nothing";
            var parts = new List<string>();
            foreach (var k in seen) parts.Add(k == focus && !Catalog.Get(k).IsPadding ? "the other " + Short(k) : Article(k));
            return string.Join(" and ", parts);
        }

        static string Article(PieceKind k)
        {
            if (Catalog.Get(k).IsPadding) return Short(k);
            string s = Short(k);
            if (k == PieceKind.Armadillo || k == PieceKind.Dragon || k == PieceKind.Robot) return s;
            return "the " + s;
        }

        /// <summary>Everyday names for Mabel's notes.</summary>
        public static string Short(PieceKind k)
        {
            switch (k)
            {
                case PieceKind.Paper: return "paper";
                case PieceKind.Bubble: return "bubble wrap";
                case PieceKind.Foam: return "foam";
                case PieceKind.Teacup: return "teacup";
                case PieceKind.Books: return "book stack";
                case PieceKind.Teddy: return "teddy";
                case PieceKind.Vase: return "vase";
                case PieceKind.BowlingBall: return "bowling ball";
                case PieceKind.Armadillo: return "Snoozles";
                case PieceKind.Magnet: return "magnet";
                case PieceKind.Potion: return "potion";
                case PieceKind.Cake: return "cake";
                case PieceKind.Balloon: return "balloon";
                case PieceKind.Cactus: return "cactus";
                case PieceKind.Robot: return "Clank";
                case PieceKind.IceSwan: return "ice swan";
                case PieceKind.LavaLamp: return "lava lamp";
                case PieceKind.BouncyBall: return "bouncy ball";
                case PieceKind.SnowGlobe: return "snow globe";
                case PieceKind.Frog: return "frog";
                case PieceKind.Dragon: return "Ember";
                case PieceKind.DragonEgg: return "dragon egg";
            }
            return Catalog.Get(k).Name.ToLowerInvariant();
        }
    }
}
