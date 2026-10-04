using System.Collections.Generic;
using static HWC.Sim.Ev;

namespace HWC.Sim
{
    public static partial class Levels
    {
        // Standard route pieces -------------------------------------------------------------------

        static Leg VanShort() => Van(Depart(0.8f, 7f), Cruise(0.6f), Bump(0.06f), Cruise(0.5f), Brake(0.55f), Rest(0.3f));
        static Leg VanRough() => Van(Depart(0.8f, 7f), Cruise(0.4f), Pothole(0.07f), Cruise(0.5f), SpeedBump(0.09f), Cruise(0.4f), Brake(0.55f), Rest(0.3f));
        static Leg DepotBasic() => Depot(Rest(0.3f), Conveyor(1.4f), Drop(0.45f), Rest(0.4f), Conveyor(0.8f), Rest(0.3f));
        static Leg DepotFull() => Depot(Rest(0.3f), Conveyor(1.2f), Drop(0.45f), Rest(0.3f), ArmTip(90f, 0.8f), Rest(0.3f), Chute(26f, 0.8f), Rest(0.3f));
        static Leg DepotArm() => Depot(Rest(0.3f), Conveyor(1.0f), ArmTip(90f, 0.9f), Rest(0.3f), Chute(26f, 0.8f), Rest(0.3f));
        static Leg DoorSteps(int steps = 4) => Doorstep(Rest(0.3f), Stairs(steps), Rest(0.2f), Toss(1.0f, 0.18f, 0f), Rest(0.4f));
        static Leg DoorToss() => Doorstep(Rest(0.3f), Stairs(3), Toss(1.1f, 0.2f, 90f), Rest(0.5f), Righting(1.0f), Rest(0.3f));

        // Chapter 2: The Sorting Depot -------------------------------------------------------------

        static IEnumerable<LevelDef> Chapter2()
        {
            yield return new LevelDef
            {
                Chapter = 2, Id = "magnets", Title = "Opposites Attract", Customer = "Professor Quill",
                Order = "Two horseshoe magnets for my lab, and my lucky teacup. Do NOT let them meet.",
                Mabel = "Magnets pull from 4 cells away. Strap one down, or keep them apart.",
                NewThing = "strap",
                ReviewGood = "Magnets apart, teacup intact. Science prevails!",
                ReviewBad = "The magnets found each other. The teacup was in the way.",
                W = 5, H = 2, Items = new[] { PieceKind.Magnet, PieceKind.Magnet, PieceKind.Teacup },
                Materials = M(paper: 5, bubble: 2, divider: 1, strap: 2), Par = 6,
                Route = Route(VanShort(), DepotBasic()),
                Ref = new[] {
                    "mpcpm",
                    "ppbpp" },
                RefMods = "0,1:S;4,1:S",
            };
            yield return new LevelDef
            {
                Chapter = 2, Id = "potion", Title = "Potion Commotion", Customer = "Pip, Wizard's Apprentice",
                Order = "Potion of Mild Levitation. Keep it UPRIGHT or it levitates the postman.",
                Mabel = "The robot arm tips boxes on their side. Snug fits only.",
                NewThing = "upright",
                ReviewGood = "Still bubbling, still upright. My master is impressed!",
                ReviewBad = "It spilled. Everything in the box is now slightly floating.",
                W = 5, H = 3, Items = new[] { PieceKind.Potion, PieceKind.Books, PieceKind.Teacup },
                Materials = M(paper: 6, bubble: 4, foam: 2, divider: 1), Par = 9,
                Route = Route(VanShort(), DepotArm()),
                Ref = new[] {
                    "pqbpp",
                    "pqcbp",
                    "kkbpp" },
            };
            yield return new LevelDef
            {
                Chapter = 2, Id = "birthday", Title = "Happy Birthday, Timmy", Customer = "Timmy (age 7)",
                Order = "A cake and a balloon for my party! And Dad's heavy books, he says.",
                Mabel = "Balloons float. Cakes squish. A shelf keeps weight off.",
                NewThing = "shelf",
                ReviewGood = "BEST. PARTY. EVER. The balloon is now my best friend.",
                ReviewBad = "The cake looks like it sat on itself. Timmy is inconsolable.",
                W = 4, H = 3, Items = new[] { PieceKind.Cake, PieceKind.Balloon, PieceKind.Books },
                Materials = M(paper: 6, bubble: 3, shelf: 1), Par = 8,
                Route = Route(VanShort(), DepotBasic()),
                Ref = new[] {
                    "kkbl",
                    "ppbp",
                    "eepp" },
                RefShelves = "2@0",
            };
            yield return new LevelDef
            {
                Chapter = 2, Id = "cactus", Title = "A Prickly Situation", Customer = "Spike's Succulents",
                Order = "One prize cactus. One balloon (it's a gift). One teacup (it's a long story).",
                Mabel = "Spikes pop balloons and bubble wrap. Paper and foam are spike-proof.",
                NewThing = "sharp",
                ReviewGood = "Prickly as ever, and the balloon survived. A miracle.",
                ReviewBad = "BANG. The balloon met the cactus. They did not get along.",
                W = 5, H = 3, Items = new[] { PieceKind.Cactus, PieceKind.Balloon, PieceKind.Teacup },
                Materials = M(paper: 6, bubble: 2, foam: 3, divider: 1), Par = 10,
                Route = Route(VanShort(), DepotArm()),
                Ref = new[] {
                    "pp|lp",
                    "fx|pp",
                    "ff|cp" },
                RefDividers = new[] { 3 },
            };
            yield return new LevelDef
            {
                Chapter = 2, Id = "magnetic", Title = "Magnetic Personality", Customer = "Dr. Fernsby & Prof. Quill",
                Order = "Snoozles is visiting the lab. Please send him with the magnets and my vase.",
                Mabel = "Everything you've learned, in one box. Breathe.",
                ReviewGood = "All present, all asleep, all in one piece. Remarkable.",
                ReviewBad = "Snoozles woke up stuck to a magnet. He has opinions.",
                W = 6, H = 3, Items = new[] { PieceKind.Magnet, PieceKind.Magnet, PieceKind.Armadillo, PieceKind.Vase },
                Materials = M(paper: 6, bubble: 4, foam: 3, divider: 2, strap: 2), Par = 16,
                Route = Route(VanRough(), DepotFull()),
                Ref = new[] {
                    "mpvbpm",
                    "fbvafp",
                    "ffbffp" },
                RefMods = "0,2:S;5,2:S",
            };
        }

        // Chapter 3: Last Mile ------------------------------------------------------------------------

        static IEnumerable<LevelDef> Chapter3()
        {
            yield return new LevelDef
            {
                Chapter = 3, Id = "robot", Title = "Clockwork Clank", Customer = "Tinker Tess",
                Order = "Clank is fully wound and VERY eager. Also two teacups and his magnet.",
                Mabel = "Walkers march until something stops them. Magnets pull metal, too.",
                NewThing = "walker",
                ReviewGood = "Clank marched in, saluted, and the cups were fine. Wonderful.",
                ReviewBad = "Clank arrived. The teacups arrived as a jigsaw.",
                W = 6, H = 2, Items = new[] { PieceKind.Robot, PieceKind.Teacup, PieceKind.Teacup, PieceKind.Magnet },
                Materials = M(paper: 6, bubble: 3, divider: 2, strap: 1), Par = 6,
                Route = Route(DepotBasic(), DoorSteps(4)),
                Ref = new[] {
                    "pcp|r.",
                    "pcp|.." },
                RefDividers = new[] { 3 },
            };
            yield return new LevelDef
            {
                Chapter = 3, Id = "swan", Title = "Swan Song", Customer = "The Mossbury Ice Gala",
                Order = "Our ice swan centrepiece, the lava lamp for mood lighting, and Mr. Snuggles.",
                Mabel = "Heat melts ice within a cell. Distance, or a divider, keeps it cold.",
                NewThing = "heat",
                ReviewGood = "The swan is magnificent and not even a little damp.",
                ReviewBad = "We received a lava lamp, a bear, and a very wet box.",
                W = 6, H = 3, Items = new[] { PieceKind.IceSwan, PieceKind.LavaLamp, PieceKind.Teddy },
                Materials = M(paper: 6, bubble: 4, foam: 2, divider: 1), Par = 9,
                Route = Route(VanShort(), DoorSteps(3)),
                Ref = new[] {
                    "pip|hp",
                    "pip|ht",
                    "bbb|bb" },
                RefDividers = new[] { 3 },
            };
            yield return new LevelDef
            {
                Chapter = 3, Id = "boing", Title = "Boing", Customer = "Bounce Brothers Circus",
                Order = "The Super Bouncy Ball (it never stops), a snow globe, and Grandma's vase.",
                Mabel = "A bouncer keeps every bit of energy. Give it nowhere to go.",
                NewThing = "bouncy",
                ReviewGood = "The ball bounced in, the globe and vase stayed put. Ta-da!",
                ReviewBad = "The ball bounced. And bounced. And bounced. Through everything.",
                W = 5, H = 3, Items = new[] { PieceKind.BouncyBall, PieceKind.SnowGlobe, PieceKind.Vase },
                Materials = M(paper: 6, bubble: 4, foam: 3, divider: 1), Par = 12,
                Route = Route(DoorToss()),
                Ref = new[] {
                    "pvpfp",
                    "fvpgf",
                    "fbnbf" },
            };
            yield return new LevelDef
            {
                Chapter = 3, Id = "frog", Title = "Hoppy Delivery", Customer = "Mossbury Pond Society",
                Order = "Our champion frog, a celebration cake, and two teacups. He hops when bored.",
                Mabel = "Hoppers jump every few seconds. Give them a low ceiling.",
                NewThing = "hopper",
                ReviewGood = "Frog: proud. Cake: pristine. Teacups: unhopped-on.",
                ReviewBad = "The frog landed on the cake. Several times. Ribbit.",
                W = 6, H = 3, Items = new[] { PieceKind.Frog, PieceKind.Cake, PieceKind.Teacup, PieceKind.Teacup },
                Materials = M(paper: 7, bubble: 3, shelf: 2, divider: 1), Par = 9,
                Route = Route(VanShort(), DoorSteps(3)),
                Ref = new[] {
                    "pcpcpp",
                    "jp|eep",
                    "pp|ppp" },
                RefDividers = new[] { 2 },
                RefShelves = "2@0",
            };
            yield return new LevelDef
            {
                Chapter = 3, Id = "ember", Title = "Ember", Customer = "Mabel (personal)",
                Order = "Found a tiny dragon in the depot. Sneezes when bumped. Sending him to my sister.",
                Mabel = "Dragons sneeze fire 3 cells ahead. Paper burns. Foam doesn't.",
                NewThing = "fire",
                ReviewGood = "Ember arrived toasty and happy. Nothing else did, oh wait, everything did!",
                ReviewBad = "Ember sneezed. The box is now 40% smaller and smells of toast.",
                W = 5, H = 3, Items = new[] { PieceKind.Dragon, PieceKind.Balloon, PieceKind.Books, PieceKind.Teddy },
                Materials = M(paper: 6, bubble: 3, foam: 4, divider: 1), Par = 12,
                Route = Route(VanShort(), DepotBasic(), DoorSteps(3)),
                Ref = new[] {
                    "pp|lpp",
                    "fdd|pp",
                    "kk|fff" },
                RefMods = "1,1:L",
                RefDividers = new[] { 3 },
            };
        }

        // Chapter 4: Express Service ------------------------------------------------------------------

        static IEnumerable<LevelDef> Chapter4()
        {
            yield return new LevelDef
            {
                Chapter = 4, Id = "seas", Title = "High Seas", Customer = "Captain Barnacle",
                Order = "Ship-to-island: a potion, my bowling ball, and Snoozles, who loves the sea air.",
                Mabel = "The ferry rocks both ways for a long time. Everything leans.",
                NewThing = "ship",
                ReviewGood = "Not a drop spilled, not a snore missed. Yo ho!",
                ReviewBad = "Rough crossing. The bowling ball won.",
                W = 6, H = 3, Items = new[] { PieceKind.Potion, PieceKind.BowlingBall, PieceKind.Armadillo },
                Materials = M(paper: 6, bubble: 4, foam: 3, divider: 2, strap: 1), Par = 14,
                Route = Route(VanShort(), Ship(Rest(0.4f), Rock(5.0f, 20f, 2.6f), WaveSlam(0.35f), Rest(0.6f)), DoorSteps(3)),
                Ref = new[] {
                    "pqp|bp",
                    "pqp|af",
                    "o|pp|ff" },
            };
            yield return new LevelDef
            {
                Chapter = 4, Id = "airpocket", Title = "Air Pocket", Customer = "Sky Post Express",
                Order = "Air mail: a balloon, a snow globe, and a pair of magnets for the pilot.",
                Mabel = "In an air pocket everything floats. Then everything lands.",
                NewThing = "plane",
                ReviewGood = "Smooth flying, perfect landing. Ten out of ten.",
                ReviewBad = "The air pocket rearranged the box. Rudely.",
                W = 6, H = 3, Items = new[] { PieceKind.Balloon, PieceKind.SnowGlobe, PieceKind.Magnet, PieceKind.Magnet },
                Materials = M(paper: 7, bubble: 4, foam: 3, divider: 1, strap: 2), Par = 14,
                Route = Route(Plane(Rest(0.3f), Turbulence(2.5f, 0.024f), AirPocket(0.5f), Rest(0.4f), Turbulence(1.5f, 0.02f), Rest(0.3f)), VanShort()),
                Ref = new[] {
                    "lpp|pm",
                    "fgf|pp",
                    "mff|pp" },
                RefMods = "0,0:S;5,2:S",
            };
            yield return new LevelDef
            {
                Chapter = 4, Id = "vasedragon", Title = "The Vase and the Dragon", Customer = "Lady Ashworth",
                Order = "My priceless vase, and Ember to keep me company. He's family now.",
                Mabel = "Point the sneeze at something fireproof. Keep the vase snug.",
                ReviewGood = "The vase is perfect. Ember sneezed on my eyebrows. Five stars.",
                ReviewBad = "Ember is fine. The vase is a puzzle now.",
                W = 6, H = 4, Items = new[] { PieceKind.Vase, PieceKind.Dragon, PieceKind.Cactus, PieceKind.Teacup },
                Materials = M(paper: 6, bubble: 4, foam: 4, divider: 2, shelf: 1), Par = 16,
                Route = Route(VanRough(), DepotArm(), DoorSteps(3)),
                Ref = new[] {
                    "pvp|pp",
                    "fvf|cp",
                    "ddf|bp",
                    "xff|bb" },
            };
            yield return new LevelDef
            {
                Chapter = 4, Id = "museum", Title = "Museum Piece", Customer = "Mossbury Museum of Fragile Things",
                Order = "Our touring exhibition: vase, snow globe, ice swan, lava lamp, teacup. Gulp.",
                Mabel = "Everything is fragile and half of it hates the other half.",
                ReviewGood = "The exhibition opened on time, every piece perfect. Bravo!",
                ReviewBad = "We are now the Museum of Fragments.",
                W = 7, H = 4, Items = new[] { PieceKind.Vase, PieceKind.SnowGlobe, PieceKind.IceSwan, PieceKind.LavaLamp, PieceKind.Teacup },
                Materials = M(paper: 8, bubble: 5, foam: 4, divider: 2, shelf: 1), Par = 20,
                Route = Route(Plane(Rest(0.3f), Turbulence(2.0f, 0.022f), Rest(0.3f)), VanShort(), DoorSteps(3)),
                Ref = new[] {
                    "pvpip|p",
                    "bvbip|h",
                    "pgpcpbh",
                    "fffbbff" },
            };
            yield return new LevelDef
            {
                Chapter = 4, Id = "egg", Title = "The Dragon Egg", Customer = "The Royal Hatchery",
                Order = "One dragon egg. It must arrive whole and WARM. Ember volunteered to help.",
                Mabel = "Keep the egg next to Ember. Ember sneezes. The egg is fireproof. Good luck.",
                ReviewGood = "It hatched in the box! It sneezed! Everyone cried!",
                ReviewBad = "The egg did not make it. We are not talking about it.",
                W = 6, H = 4, Items = new[] { PieceKind.DragonEgg, PieceKind.Dragon, PieceKind.Magnet, PieceKind.Magnet, PieceKind.Balloon },
                Materials = M(paper: 8, bubble: 4, foam: 5, divider: 2, shelf: 1, strap: 2), Par = 22,
                Route = Route(Catapult(Rest(0.4f), Launch(0.35f, 11f, 50f), Flight(1.4f, 360f), HayLand(0.2f), Rest(0.5f)), DoorSteps(3)),
                Ref = new[] {
                    "lpp|ppm",
                    "fzdd|ff",
                    "ffff|pp",
                    "mfff|pp" },
                RefMods = "0,0:S;6,3:S",
            };
        }
    }
}
