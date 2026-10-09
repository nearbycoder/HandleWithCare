using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HWC.EditorTools
{
    /// <summary>Menu items and batch-mode entry points for building the player.</summary>
    public static class BuildScript
    {
        public const string Version = "0.2.0";
        public const string BundleId = "com.nearbycoder.handlewithcare";
        const string IconPath = "Assets/Icons/AppIcon.png";   // ArtSource/make_textures.py -- icon

        [MenuItem("Handle With Care/Build Linux Player")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/HandleWithCare.x86_64");

        /// <summary>Universal (Intel + Apple silicon) .app, Mono. Unsigned: needs the Mac Build Support module.</summary>
        [MenuItem("Handle With Care/Build macOS Player")]
        public static void BuildMac()
        {
            SetMacArchitecture("x64ARM64");
            Build(BuildTarget.StandaloneOSX, "Builds/Mac/HandleWithCare.app");
        }

        /// <summary>Windows x64, Mono. Needs the Windows Build Support (Mono) module, which this machine lacks.</summary>
        [MenuItem("Handle With Care/Build Windows Player")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/HandleWithCare.exe");

        /// <summary>
        /// Browser build (WebGL 2) for GitHub Pages: Builds/WebGL, page from Assets/WebGLTemplates/HandleWithCare.
        /// Brotli with the JavaScript decompression fallback, so any static host works without Content-Encoding
        /// headers; no threads, so no SharedArrayBuffer or COOP/COEP. Tools/build-pages.sh copies it to the site.
        /// Then the same build again with ETC2 textures (Builds/WebGL-etc2), whose files go beside the first as
        /// WebGL-etc2.* (Unity names them after the folder): phones and tablets have no DXT, and a DXT texture there is unpacked to four to eight times
        /// its size on the CPU and the GPU. The page picks the ETC2 set where the browser lacks DXT.
        /// </summary>
        [MenuItem("Handle With Care/Build Web Player")]
        public static void BuildWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.template = "PROJECT:HandleWithCare";
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            // the heap a session reaches (about 500 MB), allocated once: growing it later can mean the browser holds the
            // old and the new copy at once, which a phone's tab may not survive
            PlayerSettings.WebGL.initialMemorySize = 512;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            bool ok = Run(BuildTarget.WebGL, "Builds/WebGL", BuildOptions.None);
            if (ok)
            {
                string was = WebTextureFormat();
                WebTextureFormat("ETC2");
                try { ok = Run(BuildTarget.WebGL, "Builds/WebGL-etc2", BuildOptions.None); }
                finally { WebTextureFormat(was ?? "DXTC"); }
                // the code is the same in both (checked here); phones load the ETC2 data with it
                foreach (var part in new[] { "wasm", "framework.js" })
                    if (ok && !Same($"Builds/WebGL/Build/WebGL.{part}.unityweb", $"Builds/WebGL-etc2/Build/WebGL-etc2.{part}.unityweb"))
                    { Debug.LogError($"[HWC] the ETC2 build's {part} differs from the DXT build's"); ok = false; }
                if (ok)
                {
                    System.IO.File.Copy("Builds/WebGL-etc2/Build/WebGL-etc2.data.unityweb", "Builds/WebGL/Build/WebGL-etc2.data.unityweb", true);   // named after its folder
                    Debug.Log($"[HWC] WebGL ETC2 textures: Builds/WebGL/Build/WebGL-etc2.data.unityweb, {new System.IO.FileInfo("Builds/WebGL/Build/WebGL-etc2.data.unityweb").Length / (1024 * 1024)} MB");
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Same(string a, string b) =>
            System.Linq.Enumerable.SequenceEqual(System.IO.File.ReadAllBytes(a), System.IO.File.ReadAllBytes(b));

        /// <summary>
        /// The Web platform's default texture compression (DXTC, ETC2, ASTC...), which the textures' "Automatic"
        /// format follows: read, or set when a name is given. Unity 6 keeps it behind
        /// PlayerSettings.Get/SetDefaultTextureCompressionFormat, reached by reflection (its signature isn't public).
        /// </summary>
        static string WebTextureFormat(string set = null)
        {
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object Target(Type t) => t == typeof(BuildTargetGroup) ? BuildTargetGroup.WebGL : t == typeof(BuildTarget) ? BuildTarget.WebGL : (object)NamedBuildTarget.WebGL;
            var get = typeof(PlayerSettings).GetMethod("GetDefaultTextureCompressionFormat", any);
            var put = typeof(PlayerSettings).GetMethod("SetDefaultTextureCompressionFormat", any);
            if (get == null || put == null) throw new Exception("PlayerSettings.Get/SetDefaultTextureCompressionFormat not found");
            string now = get.Invoke(null, new[] { Target(get.GetParameters()[0].ParameterType) })?.ToString();
            if (set == null) return now;
            var ps = put.GetParameters();
            put.Invoke(null, new[] { Target(ps[0].ParameterType), Enum.Parse(ps[1].ParameterType, set) });
            Debug.Log($"[HWC] WebGL texture compression {now} -> {get.Invoke(null, new[] { Target(get.GetParameters()[0].ParameterType) })}");
            return now;
        }

        /// <summary>Version, bundle id and icon for every platform.</summary>
        public static void ApplyIdentity()
        {
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            else Debug.LogWarning("[HWC] no app icon at " + IconPath);
        }

        /// <summary>The macOS build settings live in the Mac module's assembly; reach them by reflection so the
        /// project still compiles on editors without that module.</summary>
        static void SetMacArchitecture(string arch)
        {
            var t = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            var p = t?.GetProperty("architecture");
            if (p == null) { Debug.LogWarning("[HWC] macOS build settings not found (is Mac Build Support installed?)"); return; }
            p.SetValue(null, Enum.Parse(p.PropertyType, arch));
            Debug.Log("[HWC] macOS architecture: " + p.GetValue(null));
        }

        // the baked texture maps are BC7 already; LZ4HC packs the data files to roughly half
        static void Build(BuildTarget target, string path, BuildOptions options = BuildOptions.CompressWithLz4HC)
        {
            bool ok = Run(target, path, options);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Run(BuildTarget target, string path, BuildOptions options)
        {
            ApplyIdentity();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = path,
                target = target,
                options = options,
            });
            var s = report.summary;
            Debug.Log($"[HWC] {target} build {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors -> {path}");
            return s.result == BuildResult.Succeeded;
        }
    }
}
