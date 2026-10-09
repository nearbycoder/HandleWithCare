using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// What the browser build does differently. The page (Assets/WebGLTemplates/HandleWithCare) persists
    /// Application.persistentDataPath to IndexedDB on every write, so the save code barely changes; there are no
    /// worker threads, so the journey simulation runs on the main thread (tens of milliseconds); and pictures and
    /// GIFs go to the browser's downloads instead of the Pictures folder. Save.Write swaps files without
    /// File.Replace, which the browser's file system lacks. On phones and tablets the page passes -hwcMobile (and
    /// -hwcLite after a tab that closed while the game ran), shows the touch controls (TouchInput) and gets the
    /// game's state for them through HWC_TouchState.
    /// </summary>
    public static class WebPlatform
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsWeb => true;
        [DllImport("__Internal")] static extern void HWC_Download(string name, string mime, byte[] data, int length);
        [DllImport("__Internal")] static extern void HWC_Ready();
        [DllImport("__Internal")] static extern void HWC_TouchState(string json);
        [DllImport("__Internal")] static extern void HWC_InputMode(string mode);
#else
        public static bool IsWeb => false;
        static void HWC_Download(string name, string mime, byte[] data, int length) { }
        static void HWC_Ready() { }
        static void HWC_TouchState(string json) { }
        static void HWC_InputMode(string mode) { }
#endif

        static bool HasArg(string a) => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0;
        /// <summary>A phone or tablet (the page saw a touch-first screen): GRAPHICS FIDELITY starts at LOW.</summary>
        public static bool Mobile => IsWeb && HasArg("-hwcMobile");
        /// <summary>The last visit's tab closed while the game ran (out of memory, most likely): start on LOW.</summary>
        public static bool Lite => IsWeb && HasArg("-hwcLite");

        /// <summary>The touch controls' view of the game (a JSON object), when it changes.</summary>
        public static void TouchState(string json) { if (IsWeb) HWC_TouchState(json); }
        /// <summary>The game took another input device (the gamepad): the page hides the touch controls.</summary>
        public static void InputMode(string mode) { if (IsWeb) HWC_InputMode(mode); }

        // the engine's memory counters, for "[Web] memory" lines (they work in release players)
        static readonly string[] counterNames = { "Total Used Memory", "Total Reserved Memory", "GC Used Memory", "GC Reserved Memory",
                                                  "Gfx Used Memory", "Texture Memory", "Mesh Memory", "Audio Used Memory" };
        static readonly List<(string name, ProfilerRecorder rec)> counters = new List<(string, ProfilerRecorder)>();

        public static void StartMemoryCounters()
        {
            if (!IsWeb || counters.Count > 0) return;
            foreach (var n in counterNames) counters.Add((n, ProfilerRecorder.StartNew(ProfilerCategory.Memory, n)));
        }

        /// <summary>One line for the logs: what the engine has in its heap and on the GPU.</summary>
        public static void LogMemory(string when)
        {
            if (!IsWeb) return;
            var sb = new System.Text.StringBuilder("memory " + when + ":");
            foreach (var (n, r) in counters) if (r.Valid) sb.Append($" {n} {r.LastValue / 1048576f:0.0} MB,");
            sb.Append($" allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576f:0.0} MB, " +
                      $"managed heap {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576f:0.0} MB ({UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576f:0.0} used)");
            Log(sb.ToString());
        }

        /// <summary>Runs work on a worker thread, or right here in the browser (which has none).</summary>
        public static Task<T> Run<T>(Func<T> work)
        {
            if (!IsWeb) return Task.Run(work);
            try { return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }

        /// <summary>Hands a file to the browser as a download.</summary>
        public static void Download(string name, string mime, byte[] data) => HWC_Download(name, mime, data, data.Length);

        /// <summary>The title screen is up: the page drops its loading cover and the check script sees "[Web] ready".</summary>
        public static void Ready(string settings)
        {
            if (!IsWeb) return;
            Debug.Log("[Web] ready: " + settings);
            HWC_Ready();
        }

        /// <summary>A line for Tools/check-pages.mjs in the browser's console (nothing on desktop).</summary>
        public static void Log(string line)
        {
            if (IsWeb) Debug.Log("[Web] " + line);
        }
    }
}
