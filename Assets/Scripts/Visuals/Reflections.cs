using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HWC.Visuals
{
    /// <summary>
    /// One realtime reflection probe that re-captures the surroundings whenever the scene changes
    /// (packing bench, each journey stage, the unboxing room), so glazed, metal and glass surfaces
    /// reflect the room they are in.
    /// </summary>
    public sealed class Reflections : MonoBehaviour
    {
        static Reflections inst;
        ReflectionProbe probe;
        Coroutine pending;

        public static void Capture(Vector3 center, Vector3 size)
        {
            if (inst == null)
            {
                var go = new GameObject("ReflectionProbe");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<Reflections>();
                var p = inst.probe = go.AddComponent<ReflectionProbe>();
                p.mode = ReflectionProbeMode.Realtime;
                p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                p.resolution = GraphicsQuality.ProbeSize;
                GraphicsQuality.Changed += inst.Resize;
                p.hdr = true;
                p.boxProjection = true;
                p.blendDistance = 1f;
                p.importance = 1;
                p.nearClipPlane = 0.05f;
                p.farClipPlane = 60f;
                p.clearFlags = ReflectionProbeClearFlags.Skybox;
            }
            inst.transform.position = center;
            inst.probe.size = size;
            if (inst.pending != null) inst.StopCoroutine(inst.pending);
            inst.pending = inst.StartCoroutine(inst.RenderSoon());
        }

        void Resize()
        {
            if (probe == null || probe.resolution == GraphicsQuality.ProbeSize) return;
            probe.resolution = GraphicsQuality.ProbeSize;
            if (pending != null) StopCoroutine(pending);
            pending = StartCoroutine(RenderSoon());
        }

        void OnDestroy() => GraphicsQuality.Changed -= Resize;

        IEnumerator RenderSoon()
        {
            // wait for freshly spawned models and lighting to settle
            yield return null;
            yield return null;
            probe.RenderProbe();
            pending = null;
        }
    }
}
