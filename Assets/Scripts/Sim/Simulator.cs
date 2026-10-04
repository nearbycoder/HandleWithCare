using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    public static class Simulator
    {
        public const float PreSettleSeconds = 0.5f;
        const float CellsPerMeter = 1f / SimConst.MetersPerCell;

        /// <summary>Simulates a packing through the level's route. Deterministic.</summary>
        public static Recording Run(LevelDef level, Packing packing, bool recordFrames = true)
        {
            var kin = level.Kinematics;
            var world = World.FromPacking(packing, level.Seed);
            var rec = new Recording { Level = level, Packing = packing.Clone(), Kin = kin };

            var bodies = new BodyInfo[world.Bodies.Count];
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = world.Bodies[i];
                bodies[i] = new BodyInfo
                {
                    Type = b.Type, Kind = b.Def != null ? b.Def.Kind : PieceKind.Paper, PieceIndex = b.PieceIndex,
                    Half0 = b.Half, Strapped = b.Is(BodyState.Strapped),
                };
            }
            rec.Bodies = bodies;

            // let everything settle into place before the box is picked up
            var gDown = new V2(0, -SimConst.Gravity);
            int pre = (int)(PreSettleSeconds * SimConst.TickRate);
            for (int i = 0; i < pre; i++) world.Step(gDown, V2.Zero);
            foreach (var b in world.Bodies)
            {
                if (!b.IsPiece) continue;
                b.Vel = V2.Zero;
                b.PeakJolt = 0; b.PeakCrush = 0; b.PeakJoltRatio = 0;
                b.Timer = 0; b.Timer2 = 0;
            }
            world.RouteStartTick = world.Tick;
            world.DamageEnabled = true;
            rec.StartTick = world.Tick;

            var frameJolt = new float[world.Bodies.Count];
            ulong hash = SimMathUtil.HashSeed;
            double pvx = 0, pvy = 0;
            for (int k = 0; k < kin.TickCount; k++)
            {
                double ang = kin.A[k];
                kin.Velocity(k, out double vx, out double vy);
                double dvx = (vx - pvx) * CellsPerMeter, dvy = (vy - pvy) * CellsPerMeter;
                pvx = vx; pvy = vy;
                var gLocal = gDown.Rotated(-ang);
                var dvLocal = new V2((float)dvx, (float)dvy).Rotated(-ang);
                world.CurrentLeg = kin.LegOf[k];
                world.CurrentEvent = kin.EventOf[k];
                world.Step(gLocal, dvLocal);

                for (int i = 0; i < frameJolt.Length; i++)
                    frameJolt[i] = Math.Max(frameJolt[i], world.Bodies[i].LastJolt);

                if (k % SimConst.FrameEvery == 0)
                {
                    if (recordFrames) rec.Frames.Add(Capture(world, frameJolt));
                    Array.Clear(frameJolt, 0, frameJolt.Length);
                }
                if (k % 60 == 0) hash ^= world.StateHash() + (ulong)k;
            }

            // keep-warm items are judged over the whole trip
            float duration = kin.Duration;
            foreach (var b in world.Bodies)
            {
                if (!b.IsPiece || !b.Has(Quirk.KeepWarm) || !b.Active) continue;
                if (b.WarmTime < SimConst.WarmFraction * duration && !b.Is(BodyState.Broken))
                {
                    b.State |= BodyState.Chilled;
                    world.AddIncident(b, IncidentKind.Chilled, b.WarmTime / Math.Max(0.01f, duration), SimConst.WarmFraction, -1, true);
                }
            }
            if (recordFrames) rec.Frames.Add(Capture(world, frameJolt));

            rec.Incidents.AddRange(world.Incidents);
            rec.Bumps.AddRange(world.Bumps);
            rec.Hash = hash ^ world.StateHash();
            rec.Outcome = Judge(level, packing, world);
            return rec;
        }

        static BodyFrame[] Capture(World w, float[] jolt)
        {
            var f = new BodyFrame[w.Bodies.Count];
            for (int i = 0; i < f.Length; i++)
            {
                var b = w.Bodies[i];
                f[i] = new BodyFrame
                {
                    Pos = b.Pos, Half = b.Half, State = b.State, Facing = (sbyte)b.Facing,
                    Roll = b.RollAngle, Jolt = jolt[i],
                };
            }
            return f;
        }

        static Outcome Judge(LevelDef level, Packing packing, World world)
        {
            var o = new Outcome { Cost = packing.Cost, Par = level.Par };
            o.UnderBudget = o.Cost <= o.Par;
            bool all = true;
            float worst = 0;
            foreach (var b in world.Bodies)
            {
                if (!b.IsPiece || b.Def.IsPadding) continue;
                var r = new ItemResult
                {
                    Body = b.Index, PieceIndex = b.PieceIndex, Kind = b.Def.Kind,
                    Care = b.PeakJoltRatio, PeakJolt = b.PeakJolt,
                    Limit = b.Def.JoltLimit > 0 ? b.Def.JoltLimit : b.Def.WakeLimit,
                };
                r.Status = StatusOf(b);
                if (r.Failed) all = false;
                worst = Math.Max(worst, r.Care);
                o.Items.Add(r);
            }
            o.Delivered = all;
            o.WorstCare = worst;
            o.Careful = all && worst < SimConst.CareFraction;
            return o;
        }

        static ItemStatus StatusOf(Body b)
        {
            var s = b.State;
            if ((s & BodyState.Squished) != 0) return ItemStatus.Squished;
            if ((s & BodyState.Broken) != 0) return ItemStatus.Broken;
            if ((s & BodyState.Spilled) != 0) return ItemStatus.Spilled;
            if ((s & BodyState.Melted) != 0) return ItemStatus.Melted;
            if ((s & BodyState.Popped) != 0) return ItemStatus.Popped;
            if ((s & BodyState.Awake) != 0) return ItemStatus.Awake;
            if ((s & BodyState.Scorched) != 0) return ItemStatus.Scorched;
            if ((s & BodyState.Chilled) != 0) return ItemStatus.Chilled;
            if ((s & BodyState.Burned) != 0) return ItemStatus.Burned;
            return b.PeakJoltRatio < SimConst.CareFraction ? ItemStatus.Perfect : ItemStatus.Fine;
        }
    }
}
