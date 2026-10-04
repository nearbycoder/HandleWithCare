using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    public enum PieceKind
    {
        // Packing materials (bodies)
        Paper, Bubble, Foam,
        // Deliverable items
        Teacup, Books, Teddy, Vase, BowlingBall, Armadillo, Magnet, Potion, Cake, Balloon,
        Cactus, Robot, IceSwan, LavaLamp, BouncyBall, SnowGlobe, Frog, Dragon, DragonEgg,
    }

    [Flags]
    public enum Quirk
    {
        None = 0,
        Fragile = 1 << 0,      // breaks when a jolt exceeds its limit
        Topples = 1 << 1,      // tall: tips over without shoulder support
        Upright = 1 << 2,      // must not topple (spills)
        Rolls = 1 << 3,        // visual rolling, low friction
        Sleeper = 1 << 4,      // rolls over in its sleep, wakes on a hard jolt
        Magnet = 1 << 5,       // attracts magnets and metal
        Metal = 1 << 6,        // attracted by magnets
        Squishable = 1 << 7,   // fails under load
        Floats = 1 << 8,       // buoyant
        Sharp = 1 << 9,        // pops balloons and bubble wrap, wakes sleepers
        Walker = 1 << 10,      // walks forward, turns when blocked
        Hot = 1 << 11,         // heat source
        Melts = 1 << 12,       // melts near heat
        Bouncy = 1 << 13,      // high restitution
        Hopper = 1 << 14,      // hops periodically
        Sneezer = 1 << 15,     // sneezes fire when jolted
        Flammable = 1 << 16,   // burns (padding) or gets scorched (items)
        Fireproof = 1 << 17,   // blocks fire, unharmed
        KeepWarm = 1 << 18,    // must stay near heat for part of the trip
        Padding = 1 << 19,     // packing material
        Soft = 1 << 20,        // soft surface (for icons)
        Poppable = 1 << 21,    // bubble wrap / balloon
        Facing = 1 << 22,      // can face left or right
    }

    public enum StaticKind { Divider, Shelf }

    /// <summary>Immutable definition of a piece (item or padding).</summary>
    public sealed class PieceDef
    {
        public PieceKind Kind;
        public string Id;
        public string Name;
        public string Blurb;          // one-line quirk description for the item card
        public int W, H;
        public float Mass;
        public float Friction;
        public float Restitution;
        public float Hardness;        // surface hardness, 1 = rigid, lower = absorbs jolts
        public float JoltLimit;       // 0 = can't break from jolts
        public float CrushLimit;      // load (kg equivalent) it can bear, 0 = unlimited
        public float GravityScale = 1f;
        public float WakeLimit;       // sleepers
        public float SneezeLimit;     // sneezers
        public Quirk Quirks;
        public bool Rotatable;
        public int Cost;              // padding only
        public bool IsPadding => (Quirks & Quirk.Padding) != 0;
        public bool Has(Quirk q) => (Quirks & q) != 0;
    }

    /// <summary>Global tuning constants of the simulation.</summary>
    public static class SimConst
    {
        public const int TickRate = 240;
        public const float Dt = 1f / TickRate;
        public const int FrameEvery = 4;                 // record at 60 Hz
        public const float Gravity = 40f;                // cells/s^2 (cell = 0.25 m)
        public const float MetersPerCell = 0.25f;
        public const float MaxSpeed = 60f;
        public const int VelocityIterations = 14;
        public const int PositionIterations = 4;
        public const float Slop = 0.004f;
        public const float AxisTolerance = 0.08f;
        public const float RestitutionThreshold = 1.5f;
        public const float CrushWindow = 0.1f;           // seconds averaged for crush loads
        public const float StrapStrength = 34f;          // average load (kg eq) a strap holds
        public const float StrapHardness = 0.45f;        // how much of the box's own jolt a strapped item feels
        public const float MagnetRange = 4.5f;
        public const float MagnetStrength = 150f;
        public const float HeatRange = 1.0f;             // edge gap in cells
        public const float MeltTime = 2.0f;
        public const float WarmFraction = 0.6f;
        public const float FlameLength = 3f;
        public const float CareFraction = 0.5f;          // "handled with care" threshold
        public const int DividerCost = 2;
        public const int ShelfCost = 2;
        public const int StrapCost = 3;
        public const float WallHardness = 1f;
        public const float DividerHardness = 0.85f;
    }

    public static class Catalog
    {
        static readonly Dictionary<PieceKind, PieceDef> defs = new Dictionary<PieceKind, PieceDef>();

        public static PieceDef Get(PieceKind k) => defs[k];
        public static IEnumerable<PieceDef> All => defs.Values;

        static void Add(PieceDef d) => defs[d.Kind] = d;

        static Catalog()
        {
            // ---- Packing materials -------------------------------------------------------
            Add(new PieceDef
            {
                Kind = PieceKind.Paper, Id = "paper", Name = "Crumpled Paper",
                Blurb = "Cheap filler. Softens bumps a little. Burns.",
                W = 1, H = 1, Mass = 0.12f, Friction = 0.6f, Restitution = 0f, Hardness = 0.55f,
                Quirks = Quirk.Padding | Quirk.Flammable | Quirk.Soft, Cost = 1,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Bubble, Id = "bubble", Name = "Bubble Wrap",
                Blurb = "Soaks up jolts. Pops on big hits and sharp things.",
                W = 1, H = 1, Mass = 0.15f, Friction = 0.5f, Restitution = 0.05f, Hardness = 0.3f,
                JoltLimit = 15f, CrushLimit = 14f,
                Quirks = Quirk.Padding | Quirk.Poppable | Quirk.Soft, Cost = 2,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Foam, Id = "foam", Name = "Foam Block",
                Blurb = "The softest, grippiest padding. Fireproof.",
                W = 1, H = 1, Mass = 0.2f, Friction = 0.85f, Restitution = 0f, Hardness = 0.18f,
                Quirks = Quirk.Padding | Quirk.Fireproof | Quirk.Soft, Cost = 3,
            });

            // ---- Items ---------------------------------------------------------------------
            Add(new PieceDef
            {
                Kind = PieceKind.Teacup, Id = "teacup", Name = "Teacup",
                Blurb = "Fragile.",
                W = 1, H = 1, Mass = 0.35f, Friction = 0.45f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 9f, CrushLimit = 6f, Quirks = Quirk.Fragile,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Books, Id = "books", Name = "Encyclopedias",
                Blurb = "Heavy and sturdy. Burns.",
                W = 2, H = 1, Mass = 2.4f, Friction = 0.65f, Restitution = 0.05f, Hardness = 0.9f,
                Quirks = Quirk.Flammable, Rotatable = true,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Teddy, Id = "teddy", Name = "Teddy Bear",
                Blurb = "Soft. Pads whatever it touches. Burns.",
                W = 1, H = 1, Mass = 0.4f, Friction = 0.7f, Restitution = 0.05f, Hardness = 0.4f,
                Quirks = Quirk.Flammable | Quirk.Soft,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Vase, Id = "vase", Name = "Tall Vase",
                Blurb = "Fragile. Tall: tips over unless something is beside it.",
                W = 1, H = 2, Mass = 0.9f, Friction = 0.55f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 8f, CrushLimit = 8f, Quirks = Quirk.Fragile | Quirk.Topples,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.BowlingBall, Id = "bowling", Name = "Bowling Ball",
                Blurb = "Very heavy. Rolls. Hits like a truck.",
                W = 1, H = 1, Mass = 5f, Friction = 0.06f, Restitution = 0.1f, Hardness = 1f,
                Quirks = Quirk.Rolls,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Armadillo, Id = "armadillo", Name = "Snoozles the Armadillo",
                Blurb = "Asleep. Rolls over in its sleep. Must arrive asleep.",
                W = 1, H = 1, Mass = 1.2f, Friction = 0.12f, Restitution = 0.15f, Hardness = 0.8f,
                WakeLimit = 7f, Quirks = Quirk.Rolls | Quirk.Sleeper | Quirk.Facing,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Magnet, Id = "magnet", Name = "Horseshoe Magnet",
                Blurb = "Pulls other magnets and metal from 4 cells away.",
                W = 1, H = 1, Mass = 1f, Friction = 0.45f, Restitution = 0.1f, Hardness = 1f,
                Quirks = Quirk.Magnet | Quirk.Metal,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Potion, Id = "potion", Name = "Bubbling Potion",
                Blurb = "Must stay upright. Tips over unless something is beside it.",
                W = 1, H = 2, Mass = 0.8f, Friction = 0.6f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 13f, CrushLimit = 10f, Quirks = Quirk.Fragile | Quirk.Topples | Quirk.Upright,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Cake, Id = "cake", Name = "Birthday Cake",
                Blurb = "Squishable: only padding may rest on top. Toasts.",
                W = 2, H = 1, Mass = 1f, Friction = 0.6f, Restitution = 0.02f, Hardness = 0.6f,
                JoltLimit = 11f, CrushLimit = 0.9f, Quirks = Quirk.Squishable | Quirk.Flammable,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Balloon, Id = "balloon", Name = "Party Balloon",
                Blurb = "Floats up. Pops on sharp things, fire or a big squeeze.",
                W = 1, H = 1, Mass = 0.08f, Friction = 0.3f, Restitution = 0.35f, Hardness = 0.5f,
                JoltLimit = 18f, CrushLimit = 4f, GravityScale = -1.3f,
                Quirks = Quirk.Floats | Quirk.Poppable | Quirk.Soft,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Cactus, Id = "cactus", Name = "Prickly Cactus",
                Blurb = "Sharp: pops balloons and bubble wrap. Pot is breakable.",
                W = 1, H = 1, Mass = 1f, Friction = 0.6f, Restitution = 0.05f, Hardness = 1f,
                JoltLimit = 12f, Quirks = Quirk.Sharp | Quirk.Fragile,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Robot, Id = "robot", Name = "Clank the Wind-up Robot",
                Blurb = "Walks forward and pushes things. Metal.",
                W = 1, H = 1, Mass = 1.3f, Friction = 0.5f, Restitution = 0.1f, Hardness = 1f,
                Quirks = Quirk.Walker | Quirk.Metal | Quirk.Facing,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.IceSwan, Id = "iceswan", Name = "Ice Swan",
                Blurb = "Fragile and slippery. Melts within 1 cell of heat.",
                W = 1, H = 2, Mass = 1f, Friction = 0.05f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 9f, Quirks = Quirk.Fragile | Quirk.Melts | Quirk.Topples,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.LavaLamp, Id = "lavalamp", Name = "Lava Lamp",
                Blurb = "Hot. Glass. Must stay upright.",
                W = 1, H = 2, Mass = 1.1f, Friction = 0.6f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 11f, Quirks = Quirk.Hot | Quirk.Fragile | Quirk.Topples | Quirk.Upright,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.BouncyBall, Id = "bouncy", Name = "Super Bouncy Ball",
                Blurb = "Bounces forever. Fill the space around it.",
                W = 1, H = 1, Mass = 0.3f, Friction = 0.4f, Restitution = 0.92f, Hardness = 0.75f,
                Quirks = Quirk.Bouncy | Quirk.Rolls,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.SnowGlobe, Id = "snowglobe", Name = "Snow Globe",
                Blurb = "Very fragile.",
                W = 1, H = 1, Mass = 0.6f, Friction = 0.5f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 6.5f, CrushLimit = 6f, Quirks = Quirk.Fragile,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Frog, Id = "frog", Name = "Bouncy Frog",
                Blurb = "Hops every few seconds and lands on whatever is there.",
                W = 1, H = 1, Mass = 0.5f, Friction = 0.7f, Restitution = 0.1f, Hardness = 0.55f,
                Quirks = Quirk.Hopper | Quirk.Facing | Quirk.Soft,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.Dragon, Id = "dragon", Name = "Ember the Tiny Dragon",
                Blurb = "Sneezes fire when jolted, 3 cells ahead. Warm.",
                W = 2, H = 1, Mass = 1.5f, Friction = 0.55f, Restitution = 0.1f, Hardness = 0.8f,
                SneezeLimit = 5.5f, Quirks = Quirk.Sneezer | Quirk.Hot | Quirk.Facing | Quirk.Fireproof,
            });
            Add(new PieceDef
            {
                Kind = PieceKind.DragonEgg, Id = "dragonegg", Name = "Dragon Egg",
                Blurb = "Very fragile. Keep it next to something warm.",
                W = 1, H = 1, Mass = 0.8f, Friction = 0.4f, Restitution = 0.1f, Hardness = 1f,
                JoltLimit = 6.5f, CrushLimit = 6f,
                Quirks = Quirk.Fragile | Quirk.KeepWarm | Quirk.Fireproof,
            });
        }
    }
}
