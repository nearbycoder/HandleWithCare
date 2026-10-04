using System;

namespace HWC.Sim
{
    public enum BodyType { Wall, Divider, Shelf, Piece }

    [Flags]
    public enum BodyState
    {
        None = 0,
        Broken = 1 << 0,
        Awake = 1 << 1,
        Toppled = 1 << 2,
        Spilled = 1 << 3,
        Popped = 1 << 4,
        Burned = 1 << 5,      // destroyed by fire (padding, balloon)
        Melted = 1 << 6,
        Squished = 1 << 7,
        Strapped = 1 << 8,
        StrapSnapped = 1 << 9,
        Removed = 1 << 10,    // no longer collides (burned paper, popped balloon)
        Scorched = 1 << 11,   // item touched by fire
        Chilled = 1 << 12,    // keep-warm item got cold (decided at the end)
        Windup = 1 << 13,     // dragon about to sneeze
        Sneezing = 1 << 14,   // flame active (visual)
        Walking = 1 << 15,
        Hopping = 1 << 16,
        Stuck = 1 << 17,      // magnets that touched each other
    }

    /// <summary>A rigid axis-aligned rectangle in box-local cell coordinates.</summary>
    public sealed class Body
    {
        public int Index;
        public BodyType Type;
        public PieceDef Def;          // null for walls, dividers, shelves
        public int PieceIndex = -1;   // index into Packing.Pieces

        public V2 Pos;                // centre
        public V2 Half;               // half extents
        public V2 Vel;
        public float Mass, InvMass;
        public bool Fixed;            // static or strapped
        public float Friction, Restitution, Hardness, GravityScale;
        public int Facing = 1;
        public BodyState State;

        // quirk runtime
        public float Timer, Timer2, MeltTime, WarmTime, StuckTime;
        public int Dir = 1;
        public float RollAngle;
        public int ToppleTick = -1, ToppleDir;
        public float SneezeAt = -1;
        public int SneezeTick = -1;
        public float Cooldown;

        // per-tick contact summary
        public bool Supported;        // has a contact holding it against gravity
        public V2 VelBefore;
        public V2 ContactImpulse;     // sum of P*n into this body
        public float ContactImpulseMag, WeightedImpulseMag;
        public float CompPosX, CompNegX, CompPosY, CompNegY;
        public float SupportHardness = 1f;
        public int TripDir;
        public V2 ExtraStrapForce;
        public float FlameReach;

        // damage tracking
        public float LastJolt, PeakJolt, PeakCrush, PeakJoltRatio;
        public int PeakTick = -1, PeakLeg = -1, PeakEvent = -1;
        public V2 Jw0, Jw1;          // contact velocity changes of the last two ticks (jolt window)
        public float[] CrushRing;
        public int CrushHead;
        public float CrushSum;
        public float[] StrapRing;
        public float StrapSum;

        public bool IsPiece => Type == BodyType.Piece;
        public bool Active => (State & BodyState.Removed) == 0;
        public bool Has(Quirk q) => Def != null && (Def.Quirks & q) != 0;
        public bool Is(BodyState s) => (State & s) != 0;

        public float MinX => Pos.x - Half.x;
        public float MaxX => Pos.x + Half.x;
        public float MinY => Pos.y - Half.y;
        public float MaxY => Pos.y + Half.y;

        public bool Overlaps(float minX, float minY, float maxX, float maxY, float eps = 0f) =>
            MinX < maxX - eps && MaxX > minX + eps && MinY < maxY - eps && MaxY > minY + eps;
    }
}
