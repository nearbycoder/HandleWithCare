using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    /// <summary>The wall a knock throws the box's contents against, as the box is seen on the bench.</summary>
    public enum KnockWall { Floor, Left, Right, Sides }

    /// <summary>One knock named on the order card's ROUTE line, and the wall it throws things against.</summary>
    public struct Knock
    {
        public int Leg, Event;
        public EventKind Kind;
        public string Name;
        public KnockWall Wall;
    }

    /// <summary>
    /// Which way each knock of a route throws the items. Pulling away, braking, the robot arm, the chute and
    /// the catapult's launch push sideways; which side comes from the route's own motion (the push the
    /// contents feel in the box's frame, summed over the event). The ferry's rocking goes both ways, and every
    /// other knock slams them into the floor (a speed bump or an air pocket lifts things first, but they come
    /// down harder than they go up). SimCheck checks each of these against the walls the items really hit.
    /// </summary>
    public static class Knocks
    {
        /// <summary>The route's knocks as the order card names them: one per name and leg, in order.</summary>
        public static List<Knock> Of(LevelDef lv)
        {
            var list = new List<Knock>();
            var kin = lv.Kinematics;
            for (int li = 0; li < lv.Route.Legs.Count; li++)
            {
                var leg = lv.Route.Legs[li];
                for (int ei = 0; ei < leg.Events.Count; ei++)
                {
                    string n = Name(leg.Events[ei]);
                    if (n == null) continue;
                    bool seen = false;
                    foreach (var k in list) if (k.Leg == li && k.Name == n) { seen = true; break; }
                    if (seen) continue;
                    list.Add(new Knock { Leg = li, Event = ei, Kind = leg.Events[ei].Kind, Name = n, Wall = WallOf(kin, li, ei) });
                }
            }
            return list;
        }

        /// <summary>The short name on the ROUTE line, or null for events it doesn't list.</summary>
        public static string Name(RouteEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.Brake: return "hard brake";
                case EventKind.Pothole: return "pothole";
                case EventKind.SpeedBump: return "speed bump";
                case EventKind.Bump: return "bumps";
                case EventKind.Cobbles: return "cobbles";
                case EventKind.Drop: return e.Label == "SET DOWN" ? null : "belt drop";
                case EventKind.ArmTip: return "robot arm";
                case EventKind.Chute: return "chute";
                case EventKind.Stairs: return "stairs";
                case EventKind.Toss: return "toss";
                case EventKind.Rock: return "rocking";
                case EventKind.WaveSlam: return "big wave";
                case EventKind.Turbulence: return "turbulence";
                case EventKind.AirPocket: return "air pocket";
                case EventKind.Launch: return "launch";
                case EventKind.HayLand: return "landing";
                default: return null;
            }
        }

        /// <summary>Whether a kind of knock pushes sideways (its side then comes from the motion).</summary>
        public static bool Sideways(EventKind k) =>
            k == EventKind.Depart || k == EventKind.Brake || k == EventKind.ArmTip || k == EventKind.Chute || k == EventKind.Launch;

        public static KnockWall WallOf(Kinematics kin, int leg, int ev)
        {
            foreach (var s in kin.Spans)
            {
                if (s.Leg != leg || s.Index != ev) continue;
                if (s.Kind == EventKind.Rock) return KnockWall.Sides;
                if (!Sideways(s.Kind)) return KnockWall.Floor;
                SidewaysPush(kin, s, out double right, out double left);
                return right >= left ? KnockWall.Right : KnockWall.Left;
            }
            return KnockWall.Floor;
        }

        /// <summary>
        /// The largest change in sideways speed the contents feel during an event, towards the right wall and
        /// towards the left (m/s): the push in the box's frame (gravity less the box's own acceleration, turned
        /// with the box), summed over the stretch of the event where it builds up the most.
        /// </summary>
        public static void SidewaysPush(Kinematics kin, EventSpan s, out double right, out double left)
        {
            double run = 0, lo = 0, hi = 0;
            right = 0; left = 0;
            for (int t = Math.Max(1, s.StartTick); t < s.EndTick; t++)
            {
                kin.Velocity(t, out double vx, out double vy);
                kin.Velocity(t - 1, out double px, out double py);
                double ax = (vx - px) / SimConst.Dt, ay = (vy - py) / SimConst.Dt;
                double fx = -ax, fy = -SimConst.Gravity * SimConst.MetersPerCell - ay;
                double c = Math.Cos(-kin.A[t]), sn = Math.Sin(-kin.A[t]);
                run += (fx * c - fy * sn) * SimConst.Dt;
                right = Math.Max(right, run - lo);
                left = Math.Max(left, hi - run);
                lo = Math.Min(lo, run);
                hi = Math.Max(hi, run);
            }
        }
    }
}
