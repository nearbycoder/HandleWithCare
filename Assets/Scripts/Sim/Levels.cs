using System.Collections.Generic;
using static HWC.Sim.Ev;

namespace HWC.Sim
{
    /// <summary>The handcrafted deliveries: twenty in the story, five more in Overtime.</summary>
    public static partial class Levels
    {
        static List<LevelDef> all;

        public static IReadOnlyList<LevelDef> All
        {
            get
            {
                if (all == null)
                {
                    all = new List<LevelDef>();
                    AddChapter1(all);
                    AddChapter2(all);
                    AddChapter3(all);
                    AddChapter4(all);
                    // CH5
                    for (int i = 0; i < all.Count; i++) { all[i].Number = i + 1; all[i].Seed = (uint)(1000 + i * 7919); }
                }
                return all;
            }
        }

        public static LevelDef Get(int number) => All[number - 1];

        /// <summary>Shifts 1-4 tell the story; shift 5 (Overtime) opens after the finale.</summary>
        public const int StoryChapters = 4;
        public static int Chapters
        {
            get { int c = 0; foreach (var l in All) if (l.Chapter > c) c = l.Chapter; return c; }
        }

        static MaterialCounts M(int paper = 0, int bubble = 0, int foam = 0, int divider = 0, int shelf = 0, int strap = 0) =>
            new MaterialCounts { Paper = paper, Bubble = bubble, Foam = foam, Divider = divider, Shelf = shelf, Strap = strap };

        static Route Route(params Leg[] legs)
        {
            var r = new Route();
            r.Legs.AddRange(legs);
            return r;
        }

        static Leg Van(params RouteEvent[] e) { var l = new Leg(LegKind.Van); l.Events.AddRange(e); return l; }
        static Leg Depot(params RouteEvent[] e) { var l = new Leg(LegKind.Depot); l.Events.AddRange(e); return l; }
        static Leg Doorstep(params RouteEvent[] e) { var l = new Leg(LegKind.Doorstep); l.Events.AddRange(e); return l; }
        static Leg Ship(params RouteEvent[] e) { var l = new Leg(LegKind.Ship); l.Events.AddRange(e); return l; }
        static Leg Plane(params RouteEvent[] e) { var l = new Leg(LegKind.Plane); l.Events.AddRange(e); return l; }
        static Leg Catapult(params RouteEvent[] e) { var l = new Leg(LegKind.Catapult); l.Events.AddRange(e); return l; }

        // ---------------------------------------------------------------------------------------
        // Chapter 1: First Shift (Van)
        // ---------------------------------------------------------------------------------------
        static void AddChapter1(List<LevelDef> L)
        {
            L.Add(new LevelDef
            {
                Chapter = 1, Id = "cup", Title = "A Cup for Edna", Customer = "Grandma Edna",
                Order = "My best teacup, for my granddaughter. It has survived three wars and one cat.",
                Mabel = "Paper is cheap. Teacups aren't. Fill the gaps.",
                NewThing = "basics",
                ReviewGood = "Not a chip on it! I've put the kettle on.",
                ReviewBad = "It arrived in more pieces than it left in.",
                W = 3, H = 2, Items = new[] { PieceKind.Teacup },
                Materials = M(paper: 5), Par = 5,
                Route = Route(Van(Depart(1.0f, 7f), Cruise(0.8f), Bump(0.05f), Cruise(0.9f), Brake(0.6f), Rest(0.4f))),
                Ref = new[] {
                    "ppp",
                    "pcp" },
                ExpertRef = new[] {      // solver: the cup only needs company on the floor
                    "...",
                    "pcp" },
            });

            L.Add(new LevelDef
            {
                Chapter = 1, Id = "bookends", Title = "Bookends", Customer = "Mossbury Library",
                Order = "Two volumes of the encyclopaedia and the librarian's favourite mug.",
                Mabel = "Heavy things slide too. Make them part of the wall.",
                ReviewGood = "Volumes A to M, perfect. The mug too. Shh!",
                ReviewBad = "Books: fine. Mug: overdue for a funeral.",
                W = 5, H = 2, Items = new[] { PieceKind.Teacup, PieceKind.Books, PieceKind.Books },
                Materials = M(paper: 8), Par = 4,
                Route = Route(Van(Depart(0.7f, 8f), Cruise(0.6f), Pothole(0.045f), Cruise(0.8f), Bump(0.045f), Cruise(0.6f), Brake(0.55f), Rest(0.5f))),
                Ref = new[] {
                    "kkkk.",
                    ".p.cp" },
            });

            L.Add(new LevelDef
            {
                Chapter = 1, Id = "vase", Title = "The Tall Vase", Customer = "Mrs. Pemberton",
                Order = "Grandmother's vase, and Mr. Snuggles the bear. Both irreplaceable.",
                Mabel = "Tall things tip over. Give the vase a shoulder to lean on.",
                NewThing = "bubble",
                ReviewGood = "The vase stood proud. Mr. Snuggles waved.",
                ReviewBad = "The vase is now a mosaic. Mr. Snuggles is traumatised.",
                W = 4, H = 3, Items = new[] { PieceKind.Vase, PieceKind.Teddy },
                Materials = M(paper: 4, bubble: 4), Par = 6,
                Route = Route(Van(Depart(0.9f, 8f), Cruise(0.6f), Bump(0.07f), Cruise(0.5f), Pothole(0.07f), Cruise(0.6f), Brake(0.5f), Rest(0.5f))),
                Ref = new[] {
                    "t...",
                    "vp..",
                    "vppp" },
            });

            L.Add(new LevelDef
            {
                Chapter = 1, Id = "strike", Title = "Strike!", Customer = "Bowl-a-Rama",
                Order = "The championship ball, plus the trophy teacups. Rush job.",
                Mabel = "Wall the ball off. Dividers don't move.",
                NewThing = "divider",
                ReviewGood = "A perfect game! Ball and cups both pristine.",
                ReviewBad = "The ball arrived. It brought the cups along as gravel.",
                W = 5, H = 2, Items = new[] { PieceKind.BowlingBall, PieceKind.Teacup, PieceKind.Teacup },
                Materials = M(paper: 6, bubble: 2, divider: 2), Par = 4,
                Route = Route(Van(Depart(0.8f, 6f), Cruise(0.5f), Brake(0.5f, 1f, "STOP AND GO"), Speed(0.6f, 6f), Cruise(0.4f), Brake(0.45f, 0.5f, "STOP AND GO"), Speed(0.6f, 7f), Cruise(0.5f), Bump(0.06f), Cruise(0.4f), Brake(0.5f), Rest(0.5f))),
                Ref = new[] {
                    "c....",
                    "c...o" },
                RefDividers = new[] { 1 },
            });

            L.Add(new LevelDef
            {
                Chapter = 1, Id = "snoozles", Title = "Snoozles", Customer = "Dr. Fernsby, Zoologist",
                Order = "Snoozles is a very sleepy armadillo. Please do not wake him. Also: my vase.",
                Mabel = "Sleepers roll over. Give him a cozy nook with soft walls.",
                NewThing = "foam",
                ReviewGood = "Still snoring when we opened the box. Bless him.",
                ReviewBad = "Snoozles is awake and furious. He has eaten the packing slip.",
                W = 5, H = 3, Items = new[] { PieceKind.Armadillo, PieceKind.Vase },
                Materials = M(paper: 4, bubble: 3, foam: 3, divider: 1), Par = 5,
                Route = Route(Van(Depart(0.9f, 6f), Cruise(0.4f), Cobbles(1.6f, 0.0012f), Cruise(0.4f), SpeedBump(0.055f), Cruise(0.5f), SpeedBump(0.055f), Cruise(0.5f), Brake(0.55f), Rest(0.6f))),
                Ref = new[] {
                    ".....",
                    "...av",
                    "...pv" },
                RefDividers = new[] { 3 },
            });
        }

        static void AddChapter2(List<LevelDef> L) { L.AddRange(Chapter2()); }
        static void AddChapter3(List<LevelDef> L) { L.AddRange(Chapter3()); }
        static void AddChapter4(List<LevelDef> L) { L.AddRange(Chapter4()); }
    }
}
