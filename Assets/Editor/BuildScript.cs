using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HWC.EditorTools
{
    /// <summary>Menu items and batch-mode entry points for building the player.</summary>
    public static class BuildScript
    {
        [MenuItem("Handle With Care/Build Linux Player")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/HandleWithCare.x86_64");

        static void Build(BuildTarget target, string path)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = path,
                target = target,
                // the baked texture maps are BC7 already; LZ4HC packs the data files to roughly half
                options = BuildOptions.CompressWithLz4HC,
            });
            var s = report.summary;
            Debug.Log($"[HWC] {target} build {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors -> {path}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
