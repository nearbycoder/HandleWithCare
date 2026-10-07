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

        public static string Note(LevelDef lv, int stage, PieceKind focus, bool budget = false)
        {
            var src = Source(lv);
            switch (stage)
            {
                case 1: return budget ? Budget(lv, src) : Describe(src, focus);
                case 2:
                    if (!budget) return $"Here's exactly where I'd put {Article(focus)}.";
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
            string rest;
            if (def.Has(Quirk.Floats))
                rest = p.Y + p.H >= src.H ? "up against the lid" : $"tucked under {Row(src, p, p.Y + p.H, idx, "the lid")}";
            else if (p.Y == 0) rest = "on the floor";
            else if (src.ShelfCovers(p.Y, p.X)) rest = "on a shelf";
            else rest = "on top of " + Row(src, p, p.Y - 1, idx, "the floor");
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
