using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// What the browser build does differently. The page (Assets/WebGLTemplates/HandleWithCare) persists
    /// Application.persistentDataPath to IndexedDB on every write, so the save code barely changes; there are no
    /// worker threads, so the journey simulation runs on the main thread (tens of milliseconds); and pictures and
    /// GIFs go to the browser's downloads instead of the Pictures folder. Save.Write swaps files without
    /// File.Replace, which the browser's file system lacks.
    /// </summary>
    public static class WebPlatform
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool IsWeb => true;
        [DllImport("__Internal")] static extern void HWC_Download(string name, string mime, byte[] data, int length);
        [DllImport("__Internal")] static extern void HWC_Ready();
#else
        public static bool IsWeb => false;
        static void HWC_Download(string name, string mime, byte[] data, int length) { }
        static void HWC_Ready() { }
#endif

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
