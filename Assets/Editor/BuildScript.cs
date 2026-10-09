using System;
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
            Build(BuildTarget.WebGL, "Builds/WebGL", BuildOptions.None);
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
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
