using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HWC.Gameplay
{
    /// <summary>
    /// SAVE GIF and photos. A GIF replays a few seconds of the trip without the HUD on a fixed clock
    /// (Time.captureDeltaTime), so every frame is kept however slow the machine is, reads each frame back
    /// small, and encodes the clip on a worker thread (<see cref="GifWriter"/>). F12 saves a PNG of the screen.
    /// Files go to the Pictures folder ("Handle With Care"); a self-test run writes only inside its sandbox.
    /// </summary>
    public sealed class ClipRecorder : MonoBehaviour
    {
        public static ClipRecorder I;
        public const int Width = 480;
        public const float FrameSeconds = 0.07f;          // 14.3 frames a second (a GIF counts in hundredths)
        public const int MaxFrames = 120;
        public const long MaxBytes = 9L * 1024 * 1024;
        public const float Before = 2.2f, After = 1.8f;   // trip seconds around the moment
        const float Warm = 0.5f;                          // played but not kept: the camera settles after the jump

        public bool Busy { get; private set; }
        public string LastSaved { get; private set; }
        public string LastError { get; private set; }
        public int LastFrames { get; private set; }
        public float LastFrom { get; private set; }
        public float LastTo { get; private set; }
        /// <summary>Goes up each time a GIF or a photo has been written (or failed), for the self-tests.</summary>
        public int Finished { get; private set; }

        Game G => Game.I;
        TextMeshProUGUI toast;
        CanvasGroup toastGroup;
        float toastT;

        public static void Create(Transform parent)
        {
            var go = new GameObject("Clips");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<ClipRecorder>();
            var canvas = Ui.CreateCanvas("ClipToast", 60, go.transform);
            var panel = Ui.Panel(canvas.transform, "toast", new Color(0.12f, 0.1f, 0.09f, 0.88f), Ui.Rounded(12));
            panel.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(980, 64));
            panel.raycastTarget = false;
            I.toast = Ui.Text(panel.transform, "t", "", 22, Palette.Cream, Ui.Bold);
            I.toast.rectTransform.Stretch(18, 18, 6, 6);
            I.toast.enableAutoSizing = true; I.toast.fontSizeMin = 14; I.toast.fontSizeMax = 22;
            I.toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
            I.toastGroup.alpha = 0;
            I.toastGroup.blocksRaycasts = false;
        }

        public string ToastText => toastGroup != null && toastGroup.alpha > 0.01f ? toast.text : "";

        void Toast(string text, float seconds = 4.5f)
        {
            toast.text = text;
            toastT = seconds;
        }

        void Update()
        {
            toastT -= Clock.UnscaledDelta;
            toastGroup.alpha = Mathf.Clamp01(toastT * 3f);
            var kb = Keyboard.current;
            if (kb != null && kb.f12Key.wasPressedThisFrame && !Busy) StartCoroutine(Photo());
        }

        // ---- where the files go ----------------------------------------------------------------------

        /// <summary>
        /// The Pictures folder's "Handle With Care". A self-test (any -hwc flag) writes only under its own
        /// sandboxed config folder ($XDG_CONFIG_HOME), and refuses when there is none.
        /// </summary>
        public static string Folder(out string refusal)
        {
            refusal = null;
            bool selfTest = false;
            foreach (var a in Environment.GetCommandLineArgs()) if (a.StartsWith("-hwc", StringComparison.Ordinal)) selfTest = true;
            if (selfTest)
            {
                string sandbox = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                string data = Application.persistentDataPath;
                if (string.IsNullOrEmpty(sandbox) || !Path.GetFullPath(data).StartsWith(Path.GetFullPath(sandbox), StringComparison.Ordinal))
                {
                    refusal = "a test run without its own config folder doesn't save pictures";
                    return null;
                }
                return Path.Combine(data, "Pictures");
            }
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pics))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                pics = string.IsNullOrEmpty(home) ? Application.persistentDataPath : Path.Combine(home, "Pictures");
            }
            return Path.Combine(pics, "Handle With Care");
        }

        static string Pretty(string path)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrEmpty(home) && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + path.Substring(home.Length) : path;
        }

        static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            string r = sb.ToString();
            while (r.Contains("--")) r = r.Replace("--", "-");
            return r.Trim('-');
        }

        string NewPath(string ext, out string refusal)
        {
            if (WebPlatform.IsWeb) { refusal = null; return FileName(ext); }   // a download: the browser picks the folder
            string dir = Folder(out refusal);
            if (dir == null) return null;
            Directory.CreateDirectory(dir);
            string name = FileName(ext);
            string path = Path.Combine(dir, name);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, name.Replace(ext, $"-{i}{ext}"));
            return path;
        }

        string FileName(string ext)
        {
            string what = G.Level != null ? $"{G.Level.Number:00}-{Slug(G.Level.Title)}" : "handle-with-care";
            return $"{what}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{ext}";
        }

        // ---- photos ----------------------------------------------------------------------------------

        IEnumerator Photo()
        {
            string path;
            try { path = NewPath(".png", out var refusal); if (path == null) { Fail(refusal); yield break; } }
            catch (Exception e) { Fail(e.Message); yield break; }
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (WebPlatform.IsWeb) WebPlatform.Download(path, "image/png", shot.EncodeToPNG());
                else File.WriteAllBytes(path, shot.EncodeToPNG());
                LastSaved = path; LastError = null;
                Toast(WebPlatform.IsWeb ? $"Photo downloaded: {path}" : $"Photo saved: {Pretty(path)}");
                Debug.Log("[Clips] photo " + path);
            }
            catch (Exception e) { Fail(e.Message); }
            finally { if (shot != null) Destroy(shot); }
            Finished++;
            G.Hud.Sfx("pick");
        }

        void Fail(string why)
        {
            LastError = why;
            Toast("Couldn't save: " + why);
            Debug.LogWarning("[Clips] couldn't save: " + why);
        }

        // ---- GIFs ------------------------------------------------------------------------------------

        /// <summary>The trip's big moment: its first failure, else the near miss closest to the limit, else the
        /// hardest knock.</summary>
        public static float Moment(Recording rec)
        {
            float fail = -1f, nearT = -1f, nearCare = -1f;
            foreach (var tr in rec.Troubles)
            {
                if (tr.Failure) { if (fail < 0f || tr.Time < fail) fail = tr.Time; }
                else if (tr.Care > nearCare) { nearCare = tr.Care; nearT = tr.Time; }
            }
            if (fail >= 0f) return fail;
            if (nearT >= 0f) return nearT;
            float best = -1f, at = rec.Duration * 0.5f;
            foreach (var b in rec.Bumps) if (b.Speed > best) { best = b.Speed; at = b.Tick * SimConst.Dt; }
            return at;
        }

        /// <summary>The stretch of the trip a clip around this moment keeps.</summary>
        public static void Window(Recording rec, float moment, out float from, out float to)
        {
            float end = Mathf.Max(0f, rec.Duration - 0.1f);
            from = Mathf.Clamp(moment - Before, 0f, end);
            to = Mathf.Clamp(moment + After, from, end);
            if (to - from < Before + After) to = Mathf.Min(end, from + Before + After);       // near the start: go on longer
            if (to - from < Before + After) from = Mathf.Max(0f, to - (Before + After));   // near the end: start earlier
        }

        /// <summary>From the review: the trip's big moment, then back to the review.</summary>
        public void SaveFromReview()
        {
            if (Busy || G.LastRun == null || G.Phase != Phase.Results) return;
            var rec = G.LastRun;
            Window(rec, Moment(rec), out float from, out float to);
            G.Replay();
            StartCoroutine(Record(rec, from, to, () => G.Journey.Skip()));
        }

        /// <summary>In a replay: around the moment on screen, then back to that moment, paused.</summary>
        public void SaveFromReplay()
        {
            var j = G.Journey;
            if (Busy || !j.Playing || !j.IsReplay || j.Rec == null) return;
            var rec = j.Rec;
            float at = j.T;
            Window(rec, at, out float from, out float to);
            StartCoroutine(Record(rec, from, to, () => { j.Seek(at); j.UserPaused = true; }));
        }

        IEnumerator Record(Recording rec, float from, float to, Action back)
        {
            Busy = true;
            LastFrom = from; LastTo = to;
            string path = null;
            try { path = NewPath(".gif", out var refusal); if (path == null) { Fail(refusal); Busy = false; Finished++; back(); yield break; } }
            catch (Exception e) { Fail(e.Message); Busy = false; Finished++; back(); yield break; }

            var j = G.Journey;
            float speed = j.Speed;
            j.Speed = 1f; j.UserPaused = false;
            // no HUD in the clip (the toast's own canvas is hidden too)
            var hidden = new List<Canvas>();
            foreach (var c in G.GetComponentsInChildren<Canvas>()) if (c.enabled && c.isRootCanvas) { c.enabled = false; hidden.Add(c); }
            int h = Mathf.RoundToInt(Width * (float)Screen.height / Mathf.Max(1, Screen.width) / 2f) * 2;
            var small = new RenderTexture(Width, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(Width, h, TextureFormat.RGB24, false);
            var frames = new List<byte[]>();
            Time.captureDeltaTime = FrameSeconds;
            float volume = AudioListener.volume;
            AudioListener.volume = 0f;   // the clip plays faster than real time: no sound, it would only clatter
            j.Seek(Mathf.Max(0f, from - Warm));
            float started = Time.realtimeSinceStartup;
            try
            {
                while (frames.Count < MaxFrames && Time.realtimeSinceStartup - started < 90f)
                {
                    yield return new WaitForEndOfFrame();
                    if (!j.Playing || j.Rec != rec) break;
                    if (j.T < from) continue;
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    // halve until close, then the last step: a box filter more or less, not a 5x point sample
                    Texture src = shot;
                    var temps = new List<RenderTexture>();
                    int sw = shot.width, sh = shot.height;
                    while (sw / 2 >= Width)
                    {
                        sw /= 2; sh /= 2;
                        var t = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        t.filterMode = FilterMode.Bilinear;
                        Graphics.Blit(src, t);
                        temps.Add(t); src = t;
                    }
                    Graphics.Blit(src, small);
                    foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
                    Destroy(shot);
                    var was = RenderTexture.active;
                    RenderTexture.active = small;
                    read.ReadPixels(new Rect(0, 0, Width, h), 0, 0, false);
                    RenderTexture.active = was;
                    frames.Add(read.GetRawTextureData<byte>().ToArray());
                    if (j.T >= to) break;
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                AudioListener.volume = volume;
                foreach (var c in hidden) if (c != null) c.enabled = true;
                j.Speed = speed;
                small.Release(); Destroy(small); Destroy(read);
            }
            LastFrames = frames.Count;
            back();
            Debug.Log($"[Clips] recorded {frames.Count} frames of {rec.Level?.Title} from {from:0.00}s to {to:0.00}s ({Width}x{h})");
            Toast("Saving the GIF…", 30f);
            int width = Width;
            if (WebPlatform.IsWeb)
            {
                yield return null;   // the toast shows before the encoding holds up the page for a moment
                yield return null;
                string failed = null;
                try
                {
                    int delay = Mathf.RoundToInt(FrameSeconds * 100f);
                    var ms = new MemoryStream();
                    GifWriter.Write(ms, frames, width, h, delay);
                    if (ms.Length > MaxBytes) { ms = new MemoryStream(); GifWriter.Write(ms, frames, width, h, delay, false); }
                    WebPlatform.Download(path, "image/gif", ms.ToArray());
                    Debug.Log($"[Clips] gif {path} ({ms.Length / 1024} KB, {frames.Count} frames)");
                }
                catch (Exception e) { failed = e.Message; }
                if (failed != null) Fail(failed);
                else { LastSaved = path; LastError = null; Toast($"GIF downloaded: {path}"); G.Hud.Sfx("pick"); }
                Busy = false;
                Finished++;
                yield break;
            }
            var task = Task.Run(() =>
            {
                string tmp = path + ".part";
                int delay = Mathf.RoundToInt(FrameSeconds * 100f);
                using (var fs = File.Create(tmp)) GifWriter.Write(fs, frames, width, h, delay);
                // a busy clip (a shaking camera all the way): drop the dither to stay small enough to share
                if (new FileInfo(tmp).Length > MaxBytes)
                    using (var fs = File.Create(tmp)) GifWriter.Write(fs, frames, width, h, delay, false);
                File.Move(tmp, path);
            });
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Fail(task.Exception?.GetBaseException().Message ?? "unknown error");
            else
            {
                LastSaved = path; LastError = null;
                Toast($"GIF saved: {Pretty(path)}");
                Debug.Log($"[Clips] gif {path} ({new FileInfo(path).Length / 1024} KB, {frames.Count} frames)");
                G.Hud.Sfx("pick");
            }
            Busy = false;
            Finished++;
        }
    }
}
