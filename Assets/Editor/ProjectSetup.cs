using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HWC.EditorTools
{
    /// <summary>
    /// One-time (idempotent) project wiring: template materials in Resources (so the shader
    /// variants ship), URP quality tuning, player settings and the bootstrap scene.
    /// Batch: -executeMethod HWC.EditorTools.ProjectSetup.Apply
    /// </summary>
    public static class ProjectSetup
    {
        const string MatDir = "Assets/Resources/Materials";
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Handle With Care/Apply Project Setup")]
        public static void Apply()
        {
            Directory.CreateDirectory(MatDir);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (lit == null || unlit == null || particles == null)
            {
                Debug.LogError("[ProjectSetup] URP shaders not found");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Opaque lit
            var m = Ensure("HWC_Lit", lit);
            m.SetFloat("_Smoothness", 0.35f);
            m.EnableKeyword("_NORMALMAP");
            // Lit with baked PBR maps from Blender (albedo, normal, metallic/smoothness, occlusion)
            m = Ensure("HWC_LitBaked", lit);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_OCCLUSIONMAP");
            // Foliage cards: alpha-tested leaf clusters, double sided, cast cut-out shadows
            m = Ensure("HWC_LitCutout", lit);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Smoothness", 0.3f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_NORMALMAP");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            // Glass: premultiplied so reflections stay bright while the body stays see-through
            m = Ensure("HWC_Glass", lit);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 1f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetFloat("_Smoothness", 0.96f);
            // Lit with emission (fire, lava lamp, glows)
            m = Ensure("HWC_LitEmissive", lit);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", Color.white);
            // Transparent lit (glass, ghosts, bubble wrap)
            m = Ensure("HWC_LitTransparent", lit);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetFloat("_Smoothness", 0.85f);
            // Unlit (ghost overlays, grid)
            m = Ensure("HWC_Unlit", unlit);
            m = Ensure("HWC_UnlitTransparent", unlit);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            // Particles: alpha blended and additive
            m = Ensure("HWC_ParticleAlpha", particles);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m = Ensure("HWC_ParticleAdd", particles);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 10;

            ConfigureUrp();
            ConfigurePlayer();
            EnsureScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectSetup] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static Material Ensure(string name, Shader shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureUrp()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (rp != null)
            {
                rp.shadowDistance = 40f;
                rp.shadowCascadeCount = 2;
                rp.msaaSampleCount = 4;
                rp.supportsHDR = true;
                rp.renderScale = 1f;
                var so = new SerializedObject(rp);
                Set(so, "m_MainLightShadowmapResolution", 4096);
                Set(so, "m_SoftShadowsSupported", 1);
                Set(so, "m_SoftShadowQuality", 3);
                Set(so, "m_AdditionalLightsRenderingMode", 1);
                Set(so, "m_AdditionalLightShadowsSupported", 1);
                Set(so, "m_SupportsCameraDepthTexture", 1);
                Set(so, "m_SupportsCameraOpaqueTexture", 1);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(rp);
            }

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (data != null)
            {
                foreach (var ssao in data.rendererFeatures.Where(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion"))
                {
                    var so = new SerializedObject(ssao);
                    SetF(so, "m_Settings.Intensity", 1.4f);
                    SetF(so, "m_Settings.Radius", 0.12f);
                    SetF(so, "m_Settings.DirectLightingStrength", 0.3f);
                    SetF(so, "m_Settings.Falloff", 40f);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(ssao);
                }
                EditorUtility.SetDirty(data);
            }

            // Use the PC quality level everywhere on desktop, and in the browser (GRAPHICS FIDELITY scales it down there).
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            int pc = -1;
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "PC") pc = i;
            if (pc >= 0)
            {
                qs.FindProperty("m_CurrentQuality").intValue = pc;
                var per = qs.FindProperty("m_PerPlatformDefaultQuality");
                for (int i = 0; i < per.arraySize; i++)
                {
                    var e = per.GetArrayElementAtIndex(i);
                    var key = e.FindPropertyRelative("first").stringValue;
                    if (key == "Standalone" || key == "Server" || key == "WebGL") e.FindPropertyRelative("second").intValue = pc;
                }
                var lvl = levels.GetArrayElementAtIndex(pc);
                lvl.FindPropertyRelative("vSyncCount").intValue = 1;
                lvl.FindPropertyRelative("antiAliasing").intValue = 4;
                qs.ApplyModifiedProperties();
            }
        }

        static void Set(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[ProjectSetup] missing " + prop); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0;
            else if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = v;
            else p.intValue = v;
        }

        static void SetF(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning("[ProjectSetup] missing " + prop); return; }
            p.floatValue = v;
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Mossbury Parcel Post";
            PlayerSettings.productName = "Handle With Care";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
        }

        static void EnsureScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var old = "Assets/Scenes/SampleScene.unity";
            if (File.Exists(old)) AssetDatabase.DeleteAsset(old);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
