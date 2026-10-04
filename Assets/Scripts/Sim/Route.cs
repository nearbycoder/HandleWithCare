using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    public enum LegKind { Van, Depot, Doorstep, Ship, Plane, Catapult }

    public enum EventKind
    {
        Rest, Depart, Cruise, Bump, Pothole, SpeedBump, Cobbles, Hill, Brake,
        Conveyor, Drop, ArmTip, Chute,
        Stairs, Toss, Righting,
        Rock, WaveSlam,
        Turbulence, AirPocket,
        Launch, Flight, HayLand,
    }

    /// <summary>One authored event. Parameters are in metres, seconds and degrees.</summary>
    public struct RouteEvent
    {
        public EventKind Kind;
        public float Duration;
        public float A, B, C;
        public string Label;   // caption shown in the journey ("HARD BRAKE!")

        public RouteEvent(EventKind kind, float duration, float a = 0, float b = 0, float c = 0, string label = null)
        {
            Kind = kind; Duration = duration; A = a; B = b; C = c; Label = label;
        }
    }

    public sealed class Leg
    {
        public LegKind Kind;
        public List<RouteEvent> Events = new List<RouteEvent>();
        public Leg(LegKind kind) { Kind = kind; }
        public Leg Add(RouteEvent e) { Events.Add(e); return this; }
    }

    public sealed class Route
    {
        public List<Leg> Legs = new List<Leg>();
    }

    /// <summary>Where one event landed in the sampled trajectory (used by set pieces and the HUD).</summary>
    public struct EventSpan
    {
        public int Leg, Index;
        public EventKind Kind;
        public RouteEvent Def;
        public int StartTick, EndTick;
        public double X0, Y0, A0;   // pose at start (metres, radians), leg-local
        public double X1, Y1, A1;   // pose at end
        public double Vx0;          // forward speed at start (m/s)
    }

    /// <summary>
    /// The box's exact world motion, sampled at the simulation tick rate. Each leg has its own
    /// local frame starting at the origin; legs always start and end at rest and upright.
    /// </summary>
    public sealed class Kinematics
    {
        public int TickCount;
        public double[] X, Y, A;      // metres, metres, radians (counter-clockwise)
        public int[] LegOf, EventOf;
        public List<EventSpan> Spans = new List<EventSpan>();
        public List<int> LegStartTick = new List<int>();
        public Route Route;

        public float Duration => TickCount * SimConst.Dt;

        /// <summary>Box velocity in m/s for tick k (forward difference, zero at leg ends).</summary>
        public void Velocity(int k, out double vx, out double vy)
        {
            if (k < 0 || k + 1 >= TickCount || LegOf[k + 1] != LegOf[k]) { vx = 0; vy = 0; return; }
            vx = (X[k + 1] - X[k]) / SimConst.Dt;
            vy = (Y[k + 1] - Y[k]) / SimConst.Dt;
        }

        public static Kinematics Build(Route route)
        {
            var b = new Builder();
            for (int li = 0; li < route.Legs.Count; li++)
            {
                var leg = route.Legs[li];
                b.BeginLeg(li);
                for (int ei = 0; ei < leg.Events.Count; ei++) b.Run(li, ei, leg.Events[ei]);
                b.EndLeg(li);
            }
            var k = b.Finish();
            k.Route = route;
            return k;
        }

        sealed class Builder
        {
            readonly List<double> xs = new List<double>(), ys = new List<double>(), angs = new List<double>();
            readonly List<int> legs = new List<int>(), evs = new List<int>();
            public readonly List<EventSpan> spans = new List<EventSpan>();
            public readonly List<int> legStarts = new List<int>();
            double x, y, a, vx;   // current pose and forward speed
            const double G = 9.81;
            const double Dt = 1.0 / SimConst.TickRate;

            public void BeginLeg(int li)
            {
                x = 0; y = 0; a = 0; vx = 0;
                legStarts.Add(xs.Count);
            }

            public void EndLeg(int li)
            {
                // settle: half a second of rest so every leg ends still and upright
                if (Math.Abs(vx) > 1e-6)
                    Run(li, -1, new RouteEvent(EventKind.Brake, 0.8f));
                if (Math.Abs(a - Math.Round(a / (2 * Math.PI)) * 2 * Math.PI) > 1e-6)
                    Run(li, -1, new RouteEvent(EventKind.Righting, 1.0f));
                Emit(li, -1, 0.25, t => (x, y, a));
            }

            public Kinematics Finish()
            {
                var k = new Kinematics
                {
                    TickCount = xs.Count,
                    X = xs.ToArray(), Y = ys.ToArray(), A = angs.ToArray(),
                    LegOf = legs.ToArray(), EventOf = evs.ToArray(),
                };
                k.Spans.AddRange(spans);
                k.LegStartTick.AddRange(legStarts);
                return k;
            }

            // Emits ticks for duration d using pose function f(t) (absolute pose).
            void Emit(int li, int ei, double d, Func<double, (double, double, double)> f)
            {
                int n = Math.Max(1, (int)Math.Round(d * SimConst.TickRate));
                for (int i = 0; i < n; i++)
                {
                    var (px, py, pa) = f(i * Dt);
                    xs.Add(px); ys.Add(py); angs.Add(pa); legs.Add(li); evs.Add(ei);
                }
                var (ex, ey, ea) = f(n * Dt);
                x = ex; y = ey; a = ea;
            }

            static double Smooth(double t) => SimMathUtil.Smooth(t);
            static double Smoother(double t) => SimMathUtil.Smoother(t);

            // Integral of smoothstep from 0..t (normalised by duration) for velocity ramps.
            static double SmoothIntegral(double u) => u <= 0 ? 0 : (u >= 1 ? 0.5 + (u - 1) : u * u * u - 0.5 * u * u * u * u);

            /// <summary>Small deterministic road roughness (sum of sines), metres.</summary>
            static double Rough(double t, double amp) =>
                amp * (0.5 * Math.Sin(t * 2 * Math.PI * 7.1) + 0.3 * Math.Sin(t * 2 * Math.PI * 11.3 + 1.3) + 0.2 * Math.Sin(t * 2 * Math.PI * 17.9 + 2.1));

            public void Run(int li, int ei, RouteEvent e)
            {
                int startTick = xs.Count;
                double x0 = x, y0 = y, a0 = a, v0 = vx;
                double d = e.Duration;
                switch (e.Kind)
                {
                    case EventKind.Rest:
                        Emit(li, ei, d, t => (x0, y0, a0));
                        break;

                    case EventKind.Depart:
                    case EventKind.Brake:
                    {
                        // smooth speed change v0 -> target over d (Depart: A = target speed; Brake: A = target speed, default 0)
                        double v1 = e.A;
                        Emit(li, ei, d, t =>
                        {
                            double u = t / d;
                            double dist = v0 * t + (v1 - v0) * d * SmoothIntegral(u);
                            return (x0 + dist, y0, a0);
                        });
                        vx = v1;
                        break;
                    }

                    case EventKind.Cruise:
                    {
                        double rough = e.A;
                        Emit(li, ei, d, t => (x0 + v0 * t, y0 + Rough(t, rough) * Math.Sin(Math.PI * Math.Min(1, t / Math.Max(1e-3, d))), a0));
                        break;
                    }

                    case EventKind.Bump:
                    case EventKind.SpeedBump:
                    {
                        // y = h * sin^2(pi t / d): smooth rise, airborne-feeling crest, landing
                        double h = e.A;
                        Emit(li, ei, d, t =>
                        {
                            double s = Math.Sin(Math.PI * t / d);
                            return (x0 + v0 * t, y0 + h * s * s, a0);
                        });
                        break;
                    }

                    case EventKind.Pothole:
                    {
                        // wheel drops (0.9 g) into the hole, slams at the bottom, rebounds smoothly
                        double depth = e.A;
                        double acc = 0.9 * G;
                        double t1 = Math.Sqrt(2 * depth / acc);
                        double v1 = -acc * t1;
                        double vr = -0.35 * v1;
                        double t2 = Math.Max(d - t1, 0.15);
                        Emit(li, ei, t1 + t2, t =>
                        {
                            double px = x0 + v0 * t;
                            if (t < t1) return (px, y0 - 0.5 * acc * t * t, a0);
                            double u = (t - t1) / t2;
                            if (u > 1) u = 1;
                            // cubic hermite from (-depth, vr) to (0, 0)
                            double h00 = 2 * u * u * u - 3 * u * u + 1, h10 = u * u * u - 2 * u * u + u;
                            double h01 = -2 * u * u * u + 3 * u * u;
                            double py = h00 * (-depth) + h10 * (vr * t2) + h01 * 0;
                            return (px, y0 + py, a0);
                        });
                        break;
                    }

                    case EventKind.Cobbles:
                    {
                        double amp = e.A;
                        double f = e.B > 0 ? e.B : 13;
                        Emit(li, ei, d, t =>
                        {
                            double env = Math.Min(1, Math.Min(t, d - t) / 0.15);
                            double wob = Math.Sin(2 * Math.PI * f * t) * 0.7 + Math.Sin(2 * Math.PI * f * 1.37 * t + 0.6) * 0.3;
                            return (x0 + v0 * t, y0 + amp * env * (0.5 + 0.5 * wob), a0);
                        });
                        break;
                    }

                    case EventKind.Hill:
                    {
                        // pitch up to A degrees then down by the same (a hill crest), travelling at v0
                        double ang = e.A * Math.PI / 180;
                        Emit(li, ei, d, t =>
                        {
                            double ph = 2 * Math.PI * t / d;
                            double pa = a0 + ang * Math.Sin(ph);
                            double py = y0 + v0 * Math.Sin(ang) * d / (2 * Math.PI) * (1 - Math.Cos(ph));
                            return (x0 + v0 * t, py, pa);
                        });
                        break;
                    }

                    case EventKind.Conveyor:
                    {
                        double speed = e.A > 0 ? e.A : 0.8;
                        double amp = e.B > 0 ? e.B : 0.0006;
                        // ease onto belt speed in the first 0.4 s
                        Emit(li, ei, d, t =>
                        {
                            double ramp = Math.Min(t, 0.4);
                            double dist = speed * (t - ramp) + speed * 0.4 * SmoothIntegral(ramp / 0.4);
                            double vib = amp * Math.Sin(2 * Math.PI * 23 * t) * Math.Min(1, t / 0.3);
                            return (x0 + dist, y0 + vib, a0);
                        });
                        vx = speed;
                        break;
                    }

                    case EventKind.Drop:
                    case EventKind.WaveSlam:
                    case EventKind.AirPocket:
                    {
                        // A = height. WaveSlam first rises by A over B seconds. AirPocket keeps forward speed.
                        double h = e.A;
                        double rise = e.Kind == EventKind.WaveSlam ? Math.Max(0.4, e.B) : 0;
                        double tf = Math.Sqrt(2 * h / G);
                        double fwd = e.Kind == EventKind.Drop ? v0 * 0.6 : v0;
                        double baseY = e.Kind == EventKind.Drop ? y0 - h : y0;
                        double topY = e.Kind == EventKind.Drop ? y0 : y0 + h;
                        double bounce = 0.12;
                        double vLand = G * tf;
                        double tb = 2 * bounce * vLand / G;
                        double total = rise + tf + tb + Math.Max(0.05, d);
                        Emit(li, ei, total, t =>
                        {
                            double px = x0 + fwd * Math.Min(t, rise + tf);
                            if (t < rise) return (px, y0 + h * Smoother(t / rise), a0);
                            double tt = t - rise;
                            if (tt < tf) return (px, topY - 0.5 * G * tt * tt, a0);
                            double tb2 = tt - tf;
                            if (tb2 < tb) return (px, baseY + bounce * vLand * tb2 - 0.5 * G * tb2 * tb2, a0);
                            return (px, baseY, a0);
                        });
                        vx = 0;
                        break;
                    }

                    case EventKind.ArmTip:
                    {
                        // Robot arm: lift 0.25 m, rotate A degrees, hold B s, rotate back, set down.
                        double ang = e.A * Math.PI / 180;
                        double hold = e.B > 0 ? e.B : 0.8;
                        const double lift = 0.25, tl = 0.45, tr = 0.7, td = 0.35;
                        double total = tl + tr + hold + tr + td;
                        Emit(li, ei, total, t =>
                        {
                            if (t < tl) return (x0, y0 + lift * Smooth(t / tl), a0);
                            t -= tl;
                            if (t < tr) return (x0, y0 + lift, a0 + ang * Smoother(t / tr));
                            t -= tr;
                            if (t < hold) return (x0, y0 + lift, a0 + ang);
                            t -= hold;
                            if (t < tr) return (x0, y0 + lift, a0 + ang * (1 - Smoother(t / tr)));
                            t -= tr;
                            double u = Math.Min(1, t / td);
                            // set down quickly with a small clunk at the end
                            return (x0, y0 + lift * (1 - u * u), a0);
                        });
                        vx = 0;
                        break;
                    }

                    case EventKind.Chute:
                    {
                        // tilt down A degrees, slide B seconds with friction, hit the bumper at the bottom
                        double ang = e.A * Math.PI / 180;
                        double slide = e.B > 0 ? e.B : 0.9;
                        const double tt = 0.3, mu = 0.22;
                        double acc = G * (Math.Sin(ang) - mu * Math.Cos(ang));
                        double vEnd = acc * slide;
                        double total = tt + slide;
                        Emit(li, ei, total, t =>
                        {
                            if (t < tt) return (x0, y0, a0 - ang * Smooth(t / tt));
                            double s = t - tt;
                            double dist = 0.5 * acc * s * s;
                            return (x0 + dist * Math.Cos(ang), y0 - dist * Math.Sin(ang), a0 - ang);
                        });
                        // bumper: instant stop and snap flat
                        double sx = x, sy = y;
                        Emit(li, ei, 0.25, t => (sx + Math.Min(t, 0.02) * vEnd * 0.2, sy, a0 - ang * (1 - Smooth(t / 0.25))));
                        vx = 0;
                        break;
                    }

                    case EventKind.Stairs:
                    {
                        // carried up A steps of height B; each step has a footfall jolt
                        int steps = Math.Max(1, (int)e.A);
                        double sh = e.B > 0 ? e.B : 0.18;
                        double st = d > 0 ? d : 0.5;
                        double run = 0.28;
                        Emit(li, ei, steps * st, t =>
                        {
                            int k = Math.Min(steps - 1, (int)(t / st));
                            double u = (t - k * st) / st;
                            double rise = sh * (k + Smooth(Math.Min(1, u / 0.7)));
                            double fall = u > 0.7 ? -0.03 * Math.Sin(Math.PI * (u - 0.7) / 0.3) : 0;
                            return (x0 + run * (k + u), y0 + rise + fall, a0 + 0.06 * Math.Sin(Math.PI * u));
                        });
                        vx = 0;
                        break;
                    }

                    case EventKind.Toss:
                    {
                        // ballistic arc: A = horizontal distance, B = height difference (landing - start),
                        // C = spin in degrees. Lands instantly (thud).
                        double dist = e.A, dh = e.B, spin = e.C * Math.PI / 180;
                        double tf = d > 0 ? d : 0.7;
                        double vxT = dist / tf;
                        double vy0 = (dh + 0.5 * G * tf * tf) / tf;
                        const double windup = 0.35;
                        Emit(li, ei, windup, t => (x0 - 0.15 * Smooth(t / windup), y0 - 0.08 * Smooth(t / windup), a0 + 0.12 * Smooth(t / windup)));
                        double wx = x, wy = y, wa = a;
                        Emit(li, ei, tf, t => (wx + vxT * t, wy + vy0 * t - 0.5 * G * t * t, wa + (spin - (wa - a0)) * Smooth(t / tf)));
                        double lx = x, ly = y;
                        double la = a0 + spin;
                        Emit(li, ei, 0.6, t => (lx, ly, la));
                        vx = 0;
                        break;
                    }

                    case EventKind.Righting:
                    {
                        // someone picks the box up and turns it upright (to the nearest full turn)
                        double target = Math.Round(a0 / (2 * Math.PI)) * 2 * Math.PI;
                        double lift = e.A > 0 ? e.A : 0.15;
                        Emit(li, ei, d, t =>
                        {
                            double u = t / d;
                            double up = lift * Math.Sin(Math.PI * Math.Min(1, u));
                            return (x0, y0 + up, a0 + (target - a0) * Smoother(u));
                        });
                        a = target;
                        vx = 0;
                        break;
                    }

                    case EventKind.Rock:
                    {
                        // ship roll: A degrees amplitude, B seconds period, with heave
                        double amp = e.A * Math.PI / 180;
                        double period = e.B > 0 ? e.B : 3;
                        Emit(li, ei, d, t =>
                        {
                            double env = Math.Min(1, Math.Min(t, d - t) / 0.8);
                            double ph = 2 * Math.PI * t / period;
                            return (x0 + 0.02 * Math.Sin(ph * 0.5), y0 + 0.12 * env * Math.Sin(ph * 2 + 0.4), a0 + amp * env * Math.Sin(ph));
                        });
                        break;
                    }

                    case EventKind.Turbulence:
                    {
                        double amp = e.A > 0 ? e.A : 0.05;
                        Emit(li, ei, d, t =>
                        {
                            double env = Math.Min(1, Math.Min(t, d - t) / 0.4);
                            double s = 0.55 * Math.Sin(2 * Math.PI * 2.3 * t) + 0.3 * Math.Sin(2 * Math.PI * 4.1 * t + 1.1) + 0.15 * Math.Sin(2 * Math.PI * 6.7 * t + 2.3);
                            double r = 0.03 * Math.Sin(2 * Math.PI * 1.3 * t + 0.5);
                            return (x0 + v0 * t, y0 + amp * env * s, a0 + r * env);
                        });
                        break;
                    }

                    case EventKind.Launch:
                    {
                        // catapult arm: accelerate to A m/s along B degrees over d seconds, box pitched with the arm
                        double speed = e.A, ang = e.B * Math.PI / 180;
                        Emit(li, ei, d, t =>
                        {
                            double u = t / d;
                            double dist = speed * d * SmoothIntegral(u);
                            return (x0 + dist * Math.Cos(ang), y0 + dist * Math.Sin(ang), a0 + ang * 0.6 * Smooth(u));
                        });
                        launchVx = speed * Math.Cos(ang);
                        launchVy = speed * Math.Sin(ang);
                        vx = launchVx;
                        break;
                    }

                    case EventKind.Flight:
                    {
                        // ballistic flight with C degrees of spin; ends at A metres below/above launch height (B)
                        double tf = d;
                        double spin = e.C * Math.PI / 180;
                        double fx = x0, fy = y0, fa = a0, vxx = launchVx, vyy = launchVy;
                        Emit(li, ei, tf, t => (fx + vxx * t, fy + vyy * t - 0.5 * G * t * t, fa + spin * Smooth(t / tf)));
                        launchVx = vxx;
                        launchVy = vyy - G * tf;
                        vx = vxx;
                        break;
                    }

                    case EventKind.HayLand:
                    {
                        // soft landing: decelerate from the flight velocity to rest over d seconds while
                        // rotating back to the nearest upright angle
                        double vx0 = launchVx, vy0 = launchVy;
                        double target = Math.Round(a0 / (2 * Math.PI)) * 2 * Math.PI;
                        Emit(li, ei, d, t =>
                        {
                            double u = t / d;
                            double k = d * (u - u * u / 2); // integral of linear decay
                            return (x0 + vx0 * k, y0 + vy0 * k, a0 + (target - a0) * Smooth(u));
                        });
                        a = target;
                        vx = 0;
                        launchVx = 0; launchVy = 0;
                        Emit(li, ei, 0.5, t => (x, y, a));
                        break;
                    }
                }

                if (ei >= 0)
                {
                    spans.Add(new EventSpan
                    {
                        Leg = li, Index = ei, Kind = e.Kind, Def = e,
                        StartTick = startTick, EndTick = xs.Count,
                        X0 = x0, Y0 = y0, A0 = a0, X1 = x, Y1 = y, A1 = a, Vx0 = v0,
                    });
                }
            }

            double launchVx, launchVy;
        }
    }
}
