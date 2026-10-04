using System;
using HWC.Sim;

static class DebugRun
{
    /// <summary>Steps a packing and prints one body's state every few ticks within [t0, t1].</summary>
    public static int Run(LevelDef lv, Packing pk, int body, float t0, float t1)
    {
        var kin = lv.Kinematics;
        var w = World.FromPacking(pk, lv.Seed);
        var gDown = new V2(0, -SimConst.Gravity);
        int pre = (int)(Simulator.PreSettleSeconds * SimConst.TickRate);
        for (int i = 0; i < pre; i++) w.Step(gDown, V2.Zero);
        foreach (var b in w.Bodies) if (b.IsPiece) b.Vel = V2.Zero;
        w.RouteStartTick = w.Tick; w.DamageEnabled = true;
        double pvx = 0, pvy = 0;
        for (int k = 0; k < kin.TickCount; k++)
        {
            double ang = kin.A[k];
            kin.Velocity(k, out double vx, out double vy);
            var dv = new V2((float)((vx - pvx) * 4), (float)((vy - pvy) * 4)).Rotated(-ang);
            pvx = vx; pvy = vy;
            w.Step(gDown.Rotated(-ang), dv);
            float t = k * SimConst.Dt;
            if (t >= t0 && t <= t1 && k % 3 == 0)
            {
                var b = w.Bodies[body];
                Console.WriteLine($"{t:0.000} pos {b.Pos} vel {b.Vel} sup {b.Supported} trip {b.TripDir} aeff {w.AEff} state {b.State} jolt {b.LastJolt:0.0}");
            }
        }
        return 0;
    }
}
