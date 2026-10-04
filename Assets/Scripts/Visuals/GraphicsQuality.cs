using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HWC.Visuals
{
    /// <summary>
    /// "High quality graphics" setting. Off trades MSAA, resolution, shadow range and SSAO for speed
    /// on weaker GPUs. Changes the active pipeline asset at runtime only (never in the editor, where
    /// that would rewrite the asset on disk).
    /// </summary>
    public static class GraphicsQuality
    {
        static bool captured;
        static int msaa;
        static float scale, shadowDistance;

        public static void Apply(bool high)
        {
            if (Application.isEditor) return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-hwcLowGfx") >= 0) high = false;   // test hook
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return;
            if (!captured)
            {
                msaa = rp.msaaSampleCount;
                scale = rp.renderScale;
                shadowDistance = rp.shadowDistance;
                captured = true;
            }
            rp.msaaSampleCount = high ? msaa : 1;
            rp.renderScale = high ? scale : 0.8f;
            rp.shadowDistance = high ? shadowDistance : 22f;
            SetSsao(rp, high);
        }

        static void SetSsao(UniversalRenderPipelineAsset rp, bool on)
        {
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(field?.GetValue(rp) is ScriptableRendererData[] list)) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                    if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") f.SetActive(on);
            }
        }
    }
}
