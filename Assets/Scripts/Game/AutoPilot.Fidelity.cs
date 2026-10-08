using System.Collections;
using System.Collections.Generic;
using System.IO;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// -hwcFidelityProbe DIR (Tools/fidelity.sh): holds three frames still (the bench of The Vase and the Dragon
    /// with its reference packing, the depot sneeze on its trip, the unboxing) and at each GRAPHICS FIDELITY step
    /// takes a screenshot of that same frame and times 300 frames with VSync off and no frame cap.
    /// </summary>
    public sealed partial class AutoPilot
    {
        bool fidelityProbe;
        const int ProbeLevel = 18, ProbeFrames = 300;

        IEnumerator FidelityProbe()
        {
            var g = Game.I;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var lv = Levels.Get(ProbeLevel);
            g.StartLevel(ProbeLevel);
            g.Packing.ClearAll();
            yield return new WaitForSecondsRealtime(0.5f);
            if (!PlaceAll(lv.ReferencePacking())) { Debug.Log("[AutoPilot] FAIL fidelity: the reference packing could not be placed"); yield break; }
            yield return new WaitForSecondsRealtime(1.5f);   // the bench camera settles
            yield return ProbeSteps("bench");

            // the trip, held on the sneeze in the depot
            g.SealAndShip();
            while (g.Phase != Phase.Journey) yield return null;
            float at = g.LastRun.Duration * 0.44f;
            while (g.Phase == Phase.Journey && g.Journey.T < at) yield return null;
            g.Journey.UserPaused = true;
            yield return new WaitForSecondsRealtime(1.5f);   // the director camera settles on the held frame
            yield return ProbeSteps("journey");

            // the unboxing, held with the vase in the light (its coroutine runs on scaled time)
            g.Journey.UserPaused = false;
            g.Journey.Skip();
            while (g.Phase != Phase.Reveal) yield return null;
            float r0 = Time.unscaledTime;
            while (g.Phase == Phase.Reveal && Time.unscaledTime - r0 < 3.0f) yield return null;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(1.0f);
            yield return ProbeSteps("unboxing");
            Time.timeScale = 1f;
            GraphicsQuality.Apply(GraphicsQuality.High);
            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        }

        bool PlaceAll(Packing pk)
        {
            var g = Game.I;
            var order = pk.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) g.Packing.DebugAddDivider(d);
            foreach (var s in order.Shelves) g.Packing.DebugAddShelf(s);
            var pending = new List<Placement>(order.Pieces);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                for (int i = 0; i < pending.Count; i++) if (g.Packing.DebugPlace(pending[i])) pending.RemoveAt(i--);
                if (pending.Count == before) return false;
            }
            return g.Packing.ReadyToSeal;
        }

        IEnumerator ProbeSteps(string scene)
        {
            for (int s = GraphicsQuality.Low; s <= GraphicsQuality.Ultra; s++)
            {
                GraphicsQuality.Apply(s);
                yield return new WaitForSecondsRealtime(0.8f);   // the reflection probe re-renders, shaders warm up
                for (int i = 0; i < 30; i++) yield return null;
                var dts = new List<float>(ProbeFrames);
                double main = 0, gpu = 0;
                int timed = 0;
                var ft = new FrameTiming[1];
                for (int i = 0; i < ProbeFrames; i++)
                {
                    yield return null;
                    dts.Add(Time.unscaledDeltaTime * 1000f);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { main += ft[0].cpuMainThreadFrameTime; gpu += ft[0].gpuFrameTime; timed++; }
                }
                dts.Sort();
                float sum = 0;
                foreach (var d in dts) sum += d;
                float avg = sum / dts.Count, med = dts[dts.Count / 2], p95 = dts[(int)(dts.Count * 0.95f)];
                string load = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg").Trim() : "?";
                string line = $"{scene} {GraphicsQuality.Names[s]}: {avg:0.00} ms avg ({1000f / avg:0} fps), median {med:0.00}, p95 {p95:0.00} over {ProbeFrames} frames"
                              + (timed > 0 ? $"; main thread {main / timed:0.00} ms, GPU {gpu / timed:0.00} ms" : "")
                              + $"; {UnityEngine.Screen.width}x{UnityEngine.Screen.height}; load {load}";
                Debug.Log("[Fidelity] " + line);
                Debug.Log("[Fidelity] " + GraphicsQuality.Describe());
                report.AppendLine(line);
                report.AppendLine("  " + GraphicsQuality.Describe());
                Shot($"F_{scene}_{s}_{GraphicsQuality.Names[s].ToLowerInvariant()}");
                yield return AfterShot();
            }
        }
    }
}
