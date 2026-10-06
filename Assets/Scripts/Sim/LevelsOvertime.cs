using System.Collections.Generic;
using static HWC.Sim.Ev;

namespace HWC.Sim
{
    public static partial class Levels
    {
        // Chapter 5: Overtime (after the finale) ----------------------------------------------------
        // Pairs of quirks the first twenty deliveries never put in one box.

        static IEnumerable<LevelDef> Chapter5()
        {
            yield return new LevelDef
            {
                Chapter = 5, Id = "fireice", Title = "Fire and Ice", Customer = "The Frost Fair",
                Order = "Our prize ice swan, and Ember to light the bonfire. Please don't let them meet.",
                Mabel = "Ember is hot AND sneezes. Ice melts from either.",
                ReviewGood = "The swan is crisp, Ember is cosy, and the bonfire is lit.",
                ReviewBad = "We ordered a swan. We received a puddle and a smug dragon.",
                W = 6, H = 3, Items = new[] { PieceKind.Dragon, PieceKind.IceSwan, PieceKind.Teddy },
                Materials = M(paper: 6, bubble: 4, foam: 4, divider: 2), Par = 11,
                Route = Route(VanRough(), DepotBasic(), DoorSteps(3)),
                Ref = new[] {
                    "....bi",
                    "....pi",
                    ".fddpt" },
                RefDividers = new[] { 4 },
                RefMods = "2,0:L",
            };
            yield return new LevelDef
            {
                Chapter = 5, Id = "party", Title = "Party Animal", Customer = "Timmy (age 8)",
                Order = "Birthday again! A cake, a balloon, Mum's best teacup, and my new frog, Sir Hops-a-Lot.",
                Mabel = "Frogs land on whatever is under them. Balloons don't like being landed on.",
                ReviewGood = "Cake perfect, balloon bobbing, teacup fine, frog delighted. Best birthday ever!",
                ReviewBad = "Sir Hops-a-Lot ruined my birthday. I still love him.",
                W = 5, H = 3, Items = new[] { PieceKind.Frog, PieceKind.Balloon, PieceKind.Cake, PieceKind.Teacup },
                Materials = M(paper: 6, bubble: 4, foam: 3, divider: 1, shelf: 2), Par = 7,
                Route = Route(VanShort(), DoorSteps(4)),
                Ref = new[] {          // the balloon tucks under the cake; the frog gets its own corner
                    ".....",
                    "ee.c.",
                    "lp.bj" },
                RefDividers = new[] { 3 },
            };
            yield return new LevelDef
            {
                Chapter = 5, Id = "clankcake", Title = "Let Them Eat Cake", Customer = "Tinker Tess",
                Order = "Clank wants to deliver our anniversary cake and the wedding snow globe himself. He insists.",
                Mabel = "Clank pushes whatever is in front of him. Cakes don't like being pushed.",
                ReviewGood = "Clank presented the cake with a bow. Not a crumb out of place, and it's still snowing in the globe.",
                ReviewBad = "Clank marched straight through the cake. He looks very pleased.",
                W = 6, H = 3, Items = new[] { PieceKind.Robot, PieceKind.Cake, PieceKind.SnowGlobe },
                Materials = M(paper: 6, bubble: 3, foam: 3, divider: 2, shelf: 1, strap: 1), Par = 8,
                Route = Route(DepotFull(), DoorSteps(3)),
                Ref = new[] {          // Clank gets a shelf to himself: nothing up there to push
                    ".....r",
                    "g.ee..",
                    "f..p.." },
                RefShelves = "2@2",
            };
            yield return new LevelDef
            {
                Chapter = 5, Id = "warm", Title = "A Warm Welcome", Customer = "The Royal Hatchery",
                Order = "Another egg! Ember is busy, so the lava lamp will keep it warm. Plus a snow globe.",
                Mabel = "The lamp is hot and tall. The egg wants it close. The ferry wants it over.",
                ReviewGood = "Warm egg, upright lamp, snow still falling in the globe. Lovely.",
                ReviewBad = "The lamp tipped over and the egg went cold. Very sad scenes.",
                W = 5, H = 3, Items = new[] { PieceKind.DragonEgg, PieceKind.LavaLamp, PieceKind.SnowGlobe },
                Materials = M(paper: 6, bubble: 4, foam: 4, divider: 1, strap: 1), Par = 7,
                Route = Route(Ship(Rest(0.4f), Rock(4.0f, 16f, 2.6f), WaveSlam(0.3f), Rest(0.5f)), DoorCareful()),
                Ref = new[] {
                    "...h.",
                    ".gzh.",
                    ".bbp." },
            };
            yield return new LevelDef
            {
                Chapter = 5, Id = "movingday", Title = "Moving Day", Customer = "Mabel (personal, again)",
                Order = "I'm moving house. My cactus, my snow globe, a balloon from the leaving do, and Ember.",
                Mabel = "Everything I own, by catapult. Pack it like you mean it.",
                ReviewGood = "Every single thing arrived. I may cry. Ember already sneezed on the new curtains.",
                ReviewBad = "The new house smells of smoke and there is glitter everywhere. Thanks.",
                W = 6, H = 4, Items = new[] { PieceKind.Dragon, PieceKind.Cactus, PieceKind.Balloon, PieceKind.SnowGlobe },
                Materials = M(paper: 8, bubble: 4, foam: 5, divider: 2, shelf: 1, strap: 2), Par = 7,
                Route = Route(VanShort(), Catapult(Rest(0.4f), Launch(0.35f, 10f, 45f), Flight(1.2f, 360f), HayLand(0.2f), Rest(0.5f)), DoorCareful()),
                Ref = new[] {
                    "l.....",
                    "......",
                    "g.....",
                    "p..ddx" },
                RefDividers = new[] { 1, 3 },
            };
        }
    }
}
