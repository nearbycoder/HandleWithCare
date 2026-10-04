using System;
using System.Collections.Generic;
using System.Text;

namespace HWC.Sim
{
    /// <summary>One piece placed in the box grid. (X, Y) is the bottom-left cell.</summary>
    public struct Placement
    {
        public PieceKind Kind;
        public int X, Y;
        public bool Rotated;   // swaps W/H for rotatable pieces
        public int Facing;     // +1 right, -1 left (creatures, dragon, robot)
        public bool Strapped;

        public Placement(PieceKind kind, int x, int y, bool rotated = false, int facing = 1, bool strapped = false)
        {
            Kind = kind; X = x; Y = y; Rotated = rotated; Facing = facing == 0 ? 1 : facing; Strapped = strapped;
        }

        public int W => Rotated ? Catalog.Get(Kind).H : Catalog.Get(Kind).W;
        public int H => Rotated ? Catalog.Get(Kind).W : Catalog.Get(Kind).H;
        public PieceDef Def => Catalog.Get(Kind);
    }

    /// <summary>A horizontal shelf on grid line Row, spanning the compartment that contains Col.</summary>
    public struct ShelfSpec
    {
        public int Row, Col;
        public ShelfSpec(int row, int col) { Row = row; Col = col; }
    }

    public struct MaterialCounts
    {
        public int Paper, Bubble, Foam, Divider, Shelf, Strap;

        public int Get(MaterialSlot s)
        {
            switch (s)
            {
                case MaterialSlot.Paper: return Paper;
                case MaterialSlot.Bubble: return Bubble;
                case MaterialSlot.Foam: return Foam;
                case MaterialSlot.Divider: return Divider;
                case MaterialSlot.Shelf: return Shelf;
                default: return Strap;
            }
        }

        public void Set(MaterialSlot s, int v)
        {
            switch (s)
            {
                case MaterialSlot.Paper: Paper = v; break;
                case MaterialSlot.Bubble: Bubble = v; break;
                case MaterialSlot.Foam: Foam = v; break;
                case MaterialSlot.Divider: Divider = v; break;
                case MaterialSlot.Shelf: Shelf = v; break;
                default: Strap = v; break;
            }
        }

        public int Cost =>
            Paper * Catalog.Get(PieceKind.Paper).Cost + Bubble * Catalog.Get(PieceKind.Bubble).Cost +
            Foam * Catalog.Get(PieceKind.Foam).Cost + Divider * SimConst.DividerCost +
            Shelf * SimConst.ShelfCost + Strap * SimConst.StrapCost;

        public override string ToString() =>
            $"paper {Paper}, bubble {Bubble}, foam {Foam}, divider {Divider}, shelf {Shelf}, strap {Strap}";
    }

    public enum MaterialSlot { Paper, Bubble, Foam, Divider, Shelf, Strap }

    /// <summary>
    /// The player's packing: pieces on the grid, dividers on column lines, shelves on row lines,
    /// and straps (as flags on pieces). Contains all placement rules.
    /// </summary>
    public sealed class Packing
    {
        public readonly int W, H;
        public readonly List<Placement> Pieces = new List<Placement>();
        public readonly List<int> Dividers = new List<int>();
        public readonly List<ShelfSpec> Shelves = new List<ShelfSpec>();

        public Packing(int w, int h) { W = w; H = h; }

        public Packing Clone()
        {
            var p = new Packing(W, H);
            p.Pieces.AddRange(Pieces);
            p.Dividers.AddRange(Dividers);
            p.Shelves.AddRange(Shelves);
            return p;
        }

        public static bool IsFloater(PieceKind k) => Catalog.Get(k).Has(Quirk.Floats);

        // ---- Geometry queries ---------------------------------------------------------------

        public int PieceAt(int cx, int cy, int ignore = -1)
        {
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (i == ignore) continue;
                var p = Pieces[i];
                if (cx >= p.X && cx < p.X + p.W && cy >= p.Y && cy < p.Y + p.H) return i;
            }
            return -1;
        }

        public bool AreaFree(int x, int y, int w, int h, int ignore = -1)
        {
            if (x < 0 || y < 0 || x + w > W || y + h > H) return false;
            for (int i = 0; i < Pieces.Count; i++)
            {
                if (i == ignore) continue;
                var p = Pieces[i];
                if (x < p.X + p.W && x + w > p.X && y < p.Y + p.H && y + h > p.Y) return false;
            }
            return true;
        }

        /// <summary>Columns [x0, x1) of the compartment containing column col.</summary>
        public void CompartmentOf(int col, out int x0, out int x1)
        {
            x0 = 0; x1 = W;
            foreach (int d in Dividers)
            {
                if (d <= col && d > x0) x0 = d;
                if (d > col && d < x1) x1 = d;
            }
        }

        public void ShelfSpan(ShelfSpec s, out int x0, out int x1) => CompartmentOf(s.Col, out x0, out x1);

        public bool ShelfCovers(int rowLine, int col)
        {
            foreach (var s in Shelves)
            {
                if (s.Row != rowLine) continue;
                ShelfSpan(s, out int x0, out int x1);
                if (col >= x0 && col < x1) return true;
            }
            return false;
        }

        public bool CrossesStatics(int x, int y, int w, int h)
        {
            foreach (int d in Dividers)
                if (x < d && d < x + w) return true;
            foreach (var s in Shelves)
            {
                if (!(y < s.Row && s.Row < y + h)) continue;
                ShelfSpan(s, out int x0, out int x1);
                if (x < x1 && x + w > x0) return true;
            }
            return false;
        }

        public bool IsSupported(int x, int y, int w, int h, bool floater, int ignore = -1)
        {
            if (!floater)
            {
                if (y == 0) return true;
                for (int c = x; c < x + w; c++)
                {
                    if (ShelfCovers(y, c)) return true;
                    int below = PieceAt(c, y - 1, ignore);
                    if (below >= 0 && !IsFloater(Pieces[below].Kind)) return true;
                }
                return false;
            }
            if (y + h == H) return true;
            for (int c = x; c < x + w; c++)
            {
                if (ShelfCovers(y + h, c)) return true;
                if (PieceAt(c, y + h, ignore) >= 0) return true;
            }
            return false;
        }

        public bool CanPlace(Placement p, int ignore = -1)
        {
            if (!AreaFree(p.X, p.Y, p.W, p.H, ignore)) return false;
            if (CrossesStatics(p.X, p.Y, p.W, p.H)) return false;
            return IsSupported(p.X, p.Y, p.W, p.H, IsFloater(p.Kind), ignore);
        }

        /// <summary>
        /// The ghost position for a piece hovered at (x, cursorY): move up out of anything it
        /// overlaps, then fall (or rise, for floaters) to the first supported spot. Returns false
        /// when there is no valid spot in that column.
        /// </summary>
        public bool FindDropPosition(ref Placement p, int ignore = -1)
        {
            int w = p.W, h = p.H;
            if (p.X < 0) p.X = 0;
            if (p.X + w > W) p.X = W - w;
            if (p.X < 0) return false;
            bool floater = IsFloater(p.Kind);
            int y = SimMathUtil.Clamp(p.Y, 0, H - h);
            if (y < 0) return false;

            // Find the nearest free (non-crossing) spot to the cursor row, preferring upward for
            // normal pieces and downward for floaters.
            int found = -1;
            for (int dist = 0; dist <= H; dist++)
            {
                int a = floater ? y - dist : y + dist;
                int b = floater ? y + dist : y - dist;
                if (Fits(p.X, a, w, h, ignore)) { found = a; break; }
                if (dist > 0 && Fits(p.X, b, w, h, ignore)) { found = b; break; }
            }
            if (found < 0) return false;
            y = found;
            if (!floater)
            {
                while (!IsSupported(p.X, y, w, h, false, ignore) && y > 0 && Fits(p.X, y - 1, w, h, ignore)) y--;
            }
            else
            {
                while (!IsSupported(p.X, y, w, h, true, ignore) && y + h < H && Fits(p.X, y + 1, w, h, ignore)) y++;
            }
            p.Y = y;
            return IsSupported(p.X, y, w, h, floater, ignore);
        }

        bool Fits(int x, int y, int w, int h, int ignore)
        {
            if (y < 0 || y + h > H) return false;
            return AreaFree(x, y, w, h, ignore) && !CrossesStatics(x, y, w, h);
        }

        /// <summary>Drops unsupported pieces (and raises unsupported floaters) until stable.
        /// Returns indices of pieces that moved.</summary>
        public List<int> Settle()
        {
            var moved = new List<int>();
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 200)
            {
                changed = false;
                for (int i = 0; i < Pieces.Count; i++)
                {
                    var p = Pieces[i];
                    bool floater = IsFloater(p.Kind);
                    if (IsSupported(p.X, p.Y, p.W, p.H, floater, i)) continue;
                    int ny = p.Y + (floater ? 1 : -1);
                    if (ny < 0 || ny + p.H > H) continue;
                    if (!Fits(p.X, ny, p.W, p.H, i)) continue;
                    p.Y = ny;
                    Pieces[i] = p;
                    if (!moved.Contains(i)) moved.Add(i);
                    changed = true;
                }
            }
            return moved;
        }

        public bool CanPlaceDivider(int line)
        {
            if (line <= 0 || line >= W || Dividers.Contains(line)) return false;
            foreach (var p in Pieces)
                if (p.X < line && line < p.X + p.W) return false;
            foreach (var s in Shelves)
            {
                ShelfSpan(s, out int x0, out int x1);
                if (x0 < line && line < x1) return false;
            }
            return true;
        }

        public bool CanPlaceShelf(int row, int col)
        {
            if (row <= 0 || row >= H || col < 0 || col >= W) return false;
            CompartmentOf(col, out int x0, out int x1);
            foreach (var s in Shelves)
            {
                if (s.Row != row) continue;
                ShelfSpan(s, out int sx0, out int sx1);
                if (sx0 == x0 && sx1 == x1) return false;
            }
            foreach (var p in Pieces)
                if (p.Y < row && row < p.Y + p.H && p.X < x1 && p.X + p.W > x0) return false;
            return true;
        }

        // ---- Accounting ------------------------------------------------------------------------

        public MaterialCounts UsedMaterials()
        {
            var m = new MaterialCounts();
            foreach (var p in Pieces)
            {
                if (p.Kind == PieceKind.Paper) m.Paper++;
                else if (p.Kind == PieceKind.Bubble) m.Bubble++;
                else if (p.Kind == PieceKind.Foam) m.Foam++;
                if (p.Strapped) m.Strap++;
            }
            m.Divider = Dividers.Count;
            m.Shelf = Shelves.Count;
            return m;
        }

        public int Cost => UsedMaterials().Cost;

        /// <summary>Checks that this packing is legal for the level. Returns null when valid.</summary>
        public string Validate(LevelDef level)
        {
            if (W != level.W || H != level.H) return "box size mismatch";
            var need = new Dictionary<PieceKind, int>();
            foreach (var k in level.Items) need[k] = need.TryGetValue(k, out int n) ? n + 1 : 1;
            var have = new Dictionary<PieceKind, int>();
            for (int i = 0; i < Pieces.Count; i++)
            {
                var p = Pieces[i];
                if (!p.Def.IsPadding) have[p.Kind] = have.TryGetValue(p.Kind, out int n) ? n + 1 : 1;
                if (p.Rotated && !p.Def.Rotatable) return $"{p.Kind} cannot be rotated";
                if (p.Strapped && p.Def.IsPadding) return "straps only go on items";
                if (!AreaFree(p.X, p.Y, p.W, p.H, i)) return $"{p.Kind} at {p.X},{p.Y} overlaps or is out of bounds";
                if (CrossesStatics(p.X, p.Y, p.W, p.H)) return $"{p.Kind} at {p.X},{p.Y} crosses a divider or shelf";
                if (!IsSupported(p.X, p.Y, p.W, p.H, IsFloater(p.Kind), i)) return $"{p.Kind} at {p.X},{p.Y} is not supported";
            }
            foreach (var kv in need)
            {
                have.TryGetValue(kv.Key, out int n);
                if (n != kv.Value) return $"needs {kv.Value} x {kv.Key}, has {n}";
            }
            foreach (var kv in have)
                if (!need.ContainsKey(kv.Key)) return $"{kv.Key} is not part of this order";
            var used = UsedMaterials();
            foreach (MaterialSlot s in Enum.GetValues(typeof(MaterialSlot)))
                if (used.Get(s) > level.Materials.Get(s)) return $"uses {used.Get(s)} {s}, only {level.Materials.Get(s)} available";
            for (int i = 0; i < Dividers.Count; i++)
            {
                int d = Dividers[i];
                if (d <= 0 || d >= W) return "divider out of range";
                for (int j = i + 1; j < Dividers.Count; j++) if (Dividers[j] == d) return "duplicate divider";
            }
            return null;
        }

        public bool AllItemsPlaced(LevelDef level)
        {
            int count = 0;
            foreach (var p in Pieces) if (!p.Def.IsPadding) count++;
            return count == level.Items.Length;
        }

        /// <summary>ASCII picture (top row first) for debugging and tests.</summary>
        public string ToAscii()
        {
            var sb = new StringBuilder();
            for (int y = H - 1; y >= 0; y--)
            {
                if (y < H - 1)
                {
                    // shelf line between y+1 and y
                    var line = new StringBuilder("  ");
                    bool any = false;
                    for (int x = 0; x < W; x++) { bool s = ShelfCovers(y + 1, x); any |= s; line.Append(s ? "==" : "  "); }
                    if (any) sb.AppendLine(line.ToString());
                }
                sb.Append("| ");
                for (int x = 0; x < W; x++)
                {
                    int i = PieceAt(x, y);
                    char c = i < 0 ? '.' : Glyph(Pieces[i].Kind);
                    if (i >= 0 && Pieces[i].Strapped) c = char.ToUpperInvariant(c);
                    sb.Append(c);
                    sb.Append(Dividers.Contains(x + 1) ? '|' : ' ');
                }
                sb.AppendLine("|");
            }
            return sb.ToString();
        }

        public static char Glyph(PieceKind k)
        {
            switch (k)
            {
                case PieceKind.Paper: return 'p';
                case PieceKind.Bubble: return 'b';
                case PieceKind.Foam: return 'f';
                case PieceKind.Teacup: return 'c';
                case PieceKind.Books: return 'k';
                case PieceKind.Teddy: return 't';
                case PieceKind.Vase: return 'v';
                case PieceKind.BowlingBall: return 'o';
                case PieceKind.Armadillo: return 'a';
                case PieceKind.Magnet: return 'm';
                case PieceKind.Potion: return 'q';
                case PieceKind.Cake: return 'e';
                case PieceKind.Balloon: return 'l';
                case PieceKind.Cactus: return 'x';
                case PieceKind.Robot: return 'r';
                case PieceKind.IceSwan: return 'i';
                case PieceKind.LavaLamp: return 'h';
                case PieceKind.BouncyBall: return 'n';
                case PieceKind.SnowGlobe: return 'g';
                case PieceKind.Frog: return 'j';
                case PieceKind.Dragon: return 'd';
                case PieceKind.DragonEgg: return 'z';
            }
            return '?';
        }
    }
}
