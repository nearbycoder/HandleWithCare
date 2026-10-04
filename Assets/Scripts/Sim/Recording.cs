using System.Collections.Generic;

namespace HWC.Sim
{
    public enum IncidentKind
    {
        Broke, Woke, Toppled, Spilled, Popped, Burned, Melted, Squished,
        SneezeWindup, Sneezed, StrapSnapped, Scorched, Chilled, Tickled,
    }

    /// <summary>Something noteworthy that happened during the journey.</summary>
    public struct Incident
    {
        public int Tick;
        public float Time;
        public int Body;
        public int Other;           // body that caused it, or -1
        public IncidentKind Kind;
        public float Value, Limit;  // e.g. jolt 11.2 vs limit 8
        public int Leg, Event;      // route context
        public V2 Where;
        public bool IsFailure;      // costs the delivery

        public override string ToString() => $"{Time:0.00}s body {Body} {Kind} {Value:0.0}/{Limit:0.0} (other {Other}, leg {Leg}, event {Event})";
    }

    /// <summary>A notable collision, for sound and particles.</summary>
    public struct BumpFx
    {
        public int Tick;
        public int A, B;
        public float Speed;
        public V2 Point;
        public V2 Normal;
    }

    public struct BodyFrame
    {
        public V2 Pos;
        public V2 Half;
        public BodyState State;
        public sbyte Facing;
        public float Roll;
        public float Jolt;          // jolt this frame (max over the frame's ticks), for juice
    }

    public struct BodyInfo
    {
        public BodyType Type;
        public PieceKind Kind;      // valid when Type == Piece
        public int PieceIndex;
        public V2 Half0;
        public bool Strapped;
    }

    public enum ItemStatus { Perfect, Fine, Broken, Spilled, Awake, Melted, Popped, Scorched, Squished, Chilled, Burned }

    public sealed class ItemResult
    {
        public int Body;
        public int PieceIndex;
        public PieceKind Kind;
        public ItemStatus Status;
        public float Care;          // worst fraction of any limit reached (0..1+)
        public float PeakJolt;
        public float Limit;
        public bool Failed => Status != ItemStatus.Perfect && Status != ItemStatus.Fine;
    }

    public sealed class Outcome
    {
        public bool Delivered;
        public bool UnderBudget;
        public bool Careful;
        public int Cost, Par;
        public float WorstCare;
        public List<ItemResult> Items = new List<ItemResult>();

        public int Stars => !Delivered ? 0 : 1 + (UnderBudget ? 1 : 0) + (Careful ? 1 : 0);
    }

    /// <summary>The full result of simulating one packing on one route.</summary>
    public sealed class Recording
    {
        public LevelDef Level;
        public Packing Packing;
        public Kinematics Kin;
        public int StartTick;       // first route tick (after the pre-settle)
        public BodyInfo[] Bodies;
        public List<BodyFrame[]> Frames = new List<BodyFrame[]>();
        public List<Incident> Incidents = new List<Incident>();
        public List<BumpFx> Bumps = new List<BumpFx>();
        public Outcome Outcome;
        public ulong Hash;
        public float Duration => Frames.Count * SimConst.FrameEvery * SimConst.Dt;

        /// <summary>Frame index for a route tick.</summary>
        public static int FrameOfTick(int tick) => tick / SimConst.FrameEvery;
        public static float TimeOfFrame(int frame) => frame * SimConst.FrameEvery * SimConst.Dt;
    }
}
