using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    /// <summary>A handcrafted delivery: box, items, materials, route, text and reference solutions.</summary>
    public sealed class LevelDef
    {
        public int Number;
        public int Chapter;
        public string Id;
        public string Title;
        public string Customer;
        public string Order;          // the customer's note on the order card
        public string Mabel;          // head packer's sticky-note hint
        public string NewThing;       // what this delivery introduces (tutorial key), or null
        public string ReviewGood;     // customer's review when it arrives perfectly
        public string ReviewBad;      // generic disappointed review (item-specific lines added by UI)
        public int W, H;
        public PieceKind[] Items;
        public MaterialCounts Materials;
        public int Par;
        public Route Route;
        public uint Seed = 1;

        // reference solutions (validated by Tools/SimCheck)
        public string[] Ref;          // rows, top first
        public int[] RefDividers;
        public string RefShelves;     // "row@col,row@col"
        public string RefMods;        // "x,y:LS;x,y:S"
        public string[] Ref3;         // optional separate three-star solution
        public int[] Ref3Dividers;
        public string Ref3Shelves;
        public string Ref3Mods;

        Kinematics kin;
        public Kinematics Kinematics => kin ?? (kin = Kinematics.Build(Route));

        public Packing ReferencePacking() => LevelParse.Parse(this, Ref, RefDividers, RefShelves, RefMods);
        public Packing Reference3Packing() => Ref3 == null ? null : LevelParse.Parse(this, Ref3, Ref3Dividers, Ref3Shelves, Ref3Mods);

        public HashSet<LegKind> LegKinds
        {
            get
            {
                var s = new HashSet<LegKind>();
                foreach (var l in Route.Legs) s.Add(l.Kind);
                return s;
            }
        }
    }

    public static class LevelParse
    {
        public static PieceKind FromGlyph(char c)
        {
            char l = char.ToLowerInvariant(c);
            foreach (PieceKind k in Enum.GetValues(typeof(PieceKind)))
                if (Packing.Glyph(k) == l) return k;
            throw new ArgumentException($"unknown glyph '{c}'");
        }

        /// <summary>
        /// Parses an ASCII map (rows top first, '.' empty). Uppercase = rotated piece. Multi-cell
        /// pieces repeat their glyph over every cell. Mods: "x,y:L" faces left, "x,y:S" strapped,
        /// keyed by the piece's bottom-left cell.
        /// </summary>
        public static Packing Parse(LevelDef lv, string[] rows, int[] dividers, string shelves, string mods)
        {
            var pk = new Packing(lv.W, lv.H);
            if (rows == null) return pk;
            if (rows.Length != lv.H) throw new ArgumentException($"level {lv.Number}: map has {rows.Length} rows, box is {lv.H}");
            var grid = new char[lv.W, lv.H];
            for (int r = 0; r < rows.Length; r++)
            {
                if (rows[r].Length != lv.W) throw new ArgumentException($"level {lv.Number}: row '{rows[r]}' is not {lv.W} wide");
                for (int x = 0; x < lv.W; x++) grid[x, lv.H - 1 - r] = rows[r][x];
            }
            var used = new bool[lv.W, lv.H];
            var modMap = ParseMods(mods);
            for (int y = 0; y < lv.H; y++)
            {
                for (int x = 0; x < lv.W; x++)
                {
                    char c = grid[x, y];
                    if (c == '.' || used[x, y]) continue;
                    var kind = FromGlyph(c);
                    bool rot = char.IsUpper(c);
                    var def = Catalog.Get(kind);
                    int w = rot ? def.H : def.W, h = rot ? def.W : def.H;
                    for (int yy = y; yy < y + h; yy++)
                        for (int xx = x; xx < x + w; xx++)
                        {
                            if (xx >= lv.W || yy >= lv.H || grid[xx, yy] != c || used[xx, yy])
                                throw new ArgumentException($"level {lv.Number}: piece '{c}' at {x},{y} does not fit its {w}x{h} shape");
                            used[xx, yy] = true;
                        }
                    var p = new Placement(kind, x, y, rot);
                    if (modMap.TryGetValue((x, y), out string m))
                    {
                        if (m.Contains("L")) p.Facing = -1;
                        if (m.Contains("S")) p.Strapped = true;
                    }
                    pk.Pieces.Add(p);
                }
            }
            if (dividers != null) pk.Dividers.AddRange(dividers);
            if (!string.IsNullOrEmpty(shelves))
            {
                foreach (var part in shelves.Split(','))
                {
                    var bits = part.Trim().Split('@');
                    pk.Shelves.Add(new ShelfSpec(int.Parse(bits[0]), int.Parse(bits[1])));
                }
            }
            return pk;
        }

        static Dictionary<(int, int), string> ParseMods(string mods)
        {
            var d = new Dictionary<(int, int), string>();
            if (string.IsNullOrEmpty(mods)) return d;
            foreach (var part in mods.Split(';'))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                var kv = t.Split(':');
                var xy = kv[0].Split(',');
                d[(int.Parse(xy[0]), int.Parse(xy[1]))] = kv[1];
            }
            return d;
        }
    }

    /// <summary>Shorthand for authoring route events.</summary>
    public static class Ev
    {
        public static RouteEvent Rest(float d) => new RouteEvent(EventKind.Rest, d);
        public static RouteEvent Depart(float d, float speed) => new RouteEvent(EventKind.Depart, d, speed, label: "OFF WE GO");
        public static RouteEvent Speed(float d, float speed, string label = null) => new RouteEvent(EventKind.Depart, d, speed, label: label);
        public static RouteEvent Cruise(float d, float rough = 0.002f) => new RouteEvent(EventKind.Cruise, d, rough);
        public static RouteEvent Bump(float h, float d = 0.32f) => new RouteEvent(EventKind.Bump, d, h, label: "BUMP");
        public static RouteEvent SpeedBump(float h = 0.1f, float d = 0.22f) => new RouteEvent(EventKind.SpeedBump, d, h, label: "SPEED BUMP!");
        public static RouteEvent Pothole(float depth = 0.06f) => new RouteEvent(EventKind.Pothole, 0.35f, depth, label: "POTHOLE!");
        public static RouteEvent Cobbles(float d, float amp = 0.0015f, float freq = 13f) => new RouteEvent(EventKind.Cobbles, d, amp, freq, label: "COBBLESTONES");
        public static RouteEvent Hill(float d, float deg) => new RouteEvent(EventKind.Hill, d, deg, label: "STEEP HILL");
        public static RouteEvent Brake(float d, float to = 0f, string label = "HARD BRAKE!") => new RouteEvent(EventKind.Brake, d, to, label: label);
        public static RouteEvent Conveyor(float d, float speed = 0.8f) => new RouteEvent(EventKind.Conveyor, d, speed, label: "CONVEYOR");
        public static RouteEvent Drop(float h, string label = "DROP!") => new RouteEvent(EventKind.Drop, 0.25f, h, label: label);
        public static RouteEvent ArmTip(float deg, float hold = 0.8f) => new RouteEvent(EventKind.ArmTip, 0, deg, hold, label: "ROBOT ARM");
        public static RouteEvent Chute(float deg = 28f, float slide = 0.9f) => new RouteEvent(EventKind.Chute, 0, deg, slide, label: "CHUTE!");
        public static RouteEvent Stairs(int steps, float stepH = 0.18f, float stepT = 0.5f) => new RouteEvent(EventKind.Stairs, stepT, steps, stepH, label: "STAIRS");
        public static RouteEvent Toss(float dist, float dh, float spinDeg, float flight = 0.7f) => new RouteEvent(EventKind.Toss, flight, dist, dh, spinDeg, label: "TOSS!");
        public static RouteEvent Righting(float d = 1.0f) => new RouteEvent(EventKind.Righting, d, label: null);
        public static RouteEvent Rock(float d, float deg, float period = 3f) => new RouteEvent(EventKind.Rock, d, deg, period, label: "ROUGH SEAS");
        public static RouteEvent WaveSlam(float h, float rise = 0.6f) => new RouteEvent(EventKind.WaveSlam, 0.2f, h, rise, label: "BIG WAVE!");
        public static RouteEvent Turbulence(float d, float amp = 0.05f) => new RouteEvent(EventKind.Turbulence, d, amp, label: "TURBULENCE");
        public static RouteEvent AirPocket(float h) => new RouteEvent(EventKind.AirPocket, 0.2f, h, label: "AIR POCKET!");
        public static RouteEvent Launch(float d, float speed, float deg) => new RouteEvent(EventKind.Launch, d, speed, deg, label: "LAUNCH!");
        public static RouteEvent Flight(float d, float spinDeg) => new RouteEvent(EventKind.Flight, d, 0, 0, spinDeg, label: "WHEEE");
        public static RouteEvent HayLand(float d = 0.18f) => new RouteEvent(EventKind.HayLand, d, label: "HAYSTACK!");
    }
}
