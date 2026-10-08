using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HWC.Visuals
{
    /// <summary>
    /// The GRAPHICS FIDELITY setting: LOW, MEDIUM, HIGH (the pipeline asset as shipped, the default) and ULTRA.
    /// MEDIUM is what the old HIGH QUALITY GRAPHICS switch did when off. Changes the active pipeline asset at
    /// runtime only (never in the editor, where that would rewrite the asset on disk); lights, particles, the
    /// reflection probe and the post-processing read their part from here.
    /// </summary>
    public static class GraphicsQuality
    {
        public const int Low = 0, Medium = 1, High = 2, Ultra = 3;
        public static readonly string[] Names = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
        public static readonly string[] Blurbs =
        {
            "For weak GPUs: lower resolution, hard shadows, half-size textures, no ambient occlusion, bloom or blur, fewer particles.",
            "Slightly lower resolution, no MSAA or ambient occlusion, shorter shadows (what HIGH QUALITY GRAPHICS off used to be).",
            "The standard look: full resolution, 4× MSAA, soft shadows, ambient occlusion, bloom.",
            "Supersampled, sharper shadows further out, the bench lamp casts shadows, finer bloom and blur, more particles. Needs a strong GPU.",
        };

        public static int Level { get; private set; } = High;
        public static event Action Changed;

        // per step, LOW to ULTRA (HIGH's values are read from the asset when first applied)
        static readonly float[] Scale = { 0.67f, 0.8f, -1f, 1.25f };
        static readonly int[] Msaa = { 1, 1, -1, -1 };
        static readonly float[] ShadowRange = { 18f, 22f, -1f, 55f };
        static readonly int[] ShadowMap = { 1024, -1, -1, 4096 };
        static readonly int[] Cascades = { 1, -1, -1, 4 };
        static readonly int[] Lut = { 16, -1, -1, 64 };
        static readonly int[] Probe = { 64, 256, 256, 512 };
        static readonly float[] ParticleScale = { 0.5f, 1f, 1f, 1.6f };

        public static float Particles => ParticleScale[Level];
        public static int ProbeSize => Probe[Level];
        public static bool Bloom => Level >= Medium;
        public static bool FineBloom => Level == Ultra;
        public static bool Blur => Level >= Medium;
        public static bool FineBlur => Level == Ultra;

        static bool captured;
        static int baseMsaa, baseShadowMap, baseCascades, baseLut, baseMipLimit;
        static float baseScale, baseShadowRange;
        static AnisotropicFiltering baseAniso;
        static readonly List<(Light light, LightShadows[] perStep)> lights = new List<(Light, LightShadows[])>();

        /// <summary>A light whose shadows follow the setting: one LightShadows per step, LOW to ULTRA.</summary>
        public static void Track(Light light, params LightShadows[] perStep)
        {
            lights.Add((light, perStep));
            light.shadows = perStep[Level];
        }

        public static void Apply(int level)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hwcLowGfx") >= 0) level = Low;   // test hook
            Level = Mathf.Clamp(level, Low, Ultra);
            lights.RemoveAll(l => l.light == null);
            foreach (var (light, perStep) in lights) light.shadows = perStep[Level];
            if (!Application.isEditor) ApplyPipeline();
            Changed?.Invoke();
        }

        static void ApplyPipeline()
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return;
            if (!captured)
            {
                baseMsaa = rp.msaaSampleCount;
                baseScale = rp.renderScale;
                baseShadowRange = rp.shadowDistance;
                baseShadowMap = rp.mainLightShadowmapResolution;
                baseCascades = rp.shadowCascadeCount;
                baseLut = rp.colorGradingLutSize;
                baseAniso = QualitySettings.anisotropicFiltering;
                baseMipLimit = QualitySettings.globalTextureMipmapLimit;
                captured = true;
            }
            int i = Level;
            rp.msaaSampleCount = Msaa[i] > 0 ? Msaa[i] : baseMsaa;
            rp.renderScale = Scale[i] > 0 ? Scale[i] : baseScale;
            rp.shadowDistance = ShadowRange[i] > 0 ? ShadowRange[i] : baseShadowRange;
            rp.mainLightShadowmapResolution = ShadowMap[i] > 0 ? ShadowMap[i] : baseShadowMap;
            rp.shadowCascadeCount = Cascades[i] > 0 ? Cascades[i] : baseCascades;
            rp.colorGradingLutSize = Lut[i] > 0 ? Lut[i] : baseLut;
            SetSsao(rp, i >= High);
            // textures: half size on LOW; ULTRA filters at 16x at every angle
            QualitySettings.globalTextureMipmapLimit = i == Low ? 1 : baseMipLimit;
            QualitySettings.anisotropicFiltering = i == Low ? AnisotropicFiltering.Disable : baseAniso;
            if (i == Ultra) Texture.SetGlobalAnisotropicFilteringLimits(16, 16);
            else Texture.SetGlobalAnisotropicFilteringLimits(-1, -1);   // the engine's defaults
        }

        /// <summary>For the self-tests and the fidelity probe: what the pipeline is doing now.</summary>
        public static string Describe()
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return Names[Level] + " (no URP asset)";
            return $"{Names[Level]}: scale {rp.renderScale:0.00}, MSAA {rp.msaaSampleCount}x, shadows {rp.mainLightShadowmapResolution} x{rp.shadowCascadeCount} to {rp.shadowDistance:0} m, " +
                   $"SSAO {(SsaoOn(rp) ? "on" : "off")}, LUT {rp.colorGradingLutSize}, mip limit {QualitySettings.globalTextureMipmapLimit}, aniso {QualitySettings.anisotropicFiltering}, " +
                   $"probe {ProbeSize}, particles x{Particles:0.00}, bloom {(Bloom ? (FineBloom ? "HQ" : "on") : "off")}, blur {(Blur ? (FineBlur ? "HQ" : "on") : "off")}";
        }

        static IEnumerable<ScriptableRendererFeature> Features(UniversalRenderPipelineAsset rp)
        {
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(field?.GetValue(rp) is ScriptableRendererData[] list)) yield break;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures) if (f != null) yield return f;
            }
        }

        static void SetSsao(UniversalRenderPipelineAsset rp, bool on)
        {
            foreach (var f in Features(rp)) if (f.GetType().Name == "ScreenSpaceAmbientOcclusion") f.SetActive(on);
        }

        static bool SsaoOn(UniversalRenderPipelineAsset rp)
        {
            foreach (var f in Features(rp)) if (f.GetType().Name == "ScreenSpaceAmbientOcclusion") return f.isActive;
            return false;
        }
    }
}
