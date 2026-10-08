using System.Collections;
using System.IO;
using HWC.Sim;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace HWC.Gameplay
{
    /// <summary>SAVE GIF and F12 photos (round 11): written where they should be, and the game comes back as it was.</summary>
    public sealed partial class AutoPilot
    {
        /// <summary>Waits for the clip recorder to finish what was just started (or 120 s).</summary>
        IEnumerator WaitClip(int before)
        {
            float t = 0f;
            while (ClipRecorder.I.Finished == before && t < 120f) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// <summary>The file was written inside this run's sandbox (its config folder), not the real Pictures.</summary>
        static bool InSandbox(string path)
        {
            string sandbox = System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return path != null && !string.IsNullOrEmpty(sandbox) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(sandbox));
        }

        string ClipCheck(string what, int minFrames)
        {
            var c = ClipRecorder.I;
            string path = c.LastSaved;
            long bytes = path != null && File.Exists(path) ? new FileInfo(path).Length : -1;
            bool ok = c.LastError == null && bytes > 0 && bytes < 10 * 1024 * 1024 && InSandbox(path) && c.LastFrames >= minFrames && c.LastFrames <= ClipRecorder.MaxFrames
                   && !path.EndsWith(".part") && c.ToastText.StartsWith("GIF saved:");
            Check2(ok, "gif", $"{what}: {Path.GetFileName(path)}, {c.LastFrames} frames from {c.LastFrom:0.00}s to {c.LastTo:0.00}s, {bytes / 1024} KB, in the sandbox {InSandbox(path)}; toast \"{c.ToastText}\"{(c.LastError != null ? ", error " + c.LastError : "")}");
            return path;
        }

        /// <summary>
        /// tour.sh: after a failed trip (delivery 5, items only), G on the review saves a GIF around the first
        /// failure and comes back to the same review; in the replay, G saves around the moment on screen and
        /// leaves the replay paused there; F12 saves a photo. Real key events.
        /// </summary>
        IEnumerator GifAndPhoto()
        {
            var g = Game.I;
            yield return RunLevel(5, "naive", false);
            yield return new WaitForSecondsRealtime(1.0f);
            var trip = g.LastRun;
            float moment = ClipRecorder.Moment(trip);
            float firstFailure = float.MaxValue;
            foreach (var tr in trip.Troubles) if (tr.Failure && tr.Time < firstFailure) firstFailure = tr.Time;
            Check2(g.Phase == Phase.Results && Mathf.Abs(moment - firstFailure) < 0.001f, "gif", $"the review's moment is the trip's first failure ({moment:0.00}s)");
            string title = g.Hud.ResultTitle;
            int before = ClipRecorder.I.Finished;
            yield return Key(UnityEngine.InputSystem.Key.G);
            yield return null;
            Check2(ClipRecorder.I.Busy && g.Phase == Phase.Journey && !g.Canvas.enabled, "gif", "G on the review starts recording the trip, without the HUD");
            yield return WaitClip(before);
            yield return new WaitForSecondsRealtime(0.3f);
            string gif = ClipCheck("G on the review", 40);
            Check2(ClipRecorder.I.LastFrom <= moment && moment <= ClipRecorder.I.LastTo, "gif", $"the clip holds the moment ({ClipRecorder.I.LastFrom:0.00}s <= {moment:0.00}s <= {ClipRecorder.I.LastTo:0.00}s)");
            Check2(g.Phase == Phase.Results && g.Hud.ResultTitle == title && g.LastRun == trip && g.Canvas.enabled && Time.captureDeltaTime == 0f && AudioListener.volume > 0f,
                   "gif", $"back on the same review (\"{g.Hud.ResultTitle}\"), the HUD, the clock and the sound as they were");
            Shot("G1_review_gif_saved");
            yield return AfterShot();

            // in a replay: the moment on screen
            yield return Key(UnityEngine.InputSystem.Key.P);
            yield return new WaitForSecondsRealtime(0.4f);
            g.Journey.Seek(Mathf.Min(2.0f, trip.Duration * 0.3f));
            g.Journey.UserPaused = true;   // so the moment G sees is the one read here
            yield return new WaitForSecondsRealtime(0.2f);
            float at = g.Journey.T;
            before = ClipRecorder.I.Finished;
            yield return Key(UnityEngine.InputSystem.Key.G);
            yield return WaitClip(before);
            yield return new WaitForSecondsRealtime(0.3f);
            ClipCheck("G in a replay", 30);
            Check2(ClipRecorder.I.LastFrom <= at && at <= ClipRecorder.I.LastTo && g.Phase == Phase.Journey && g.Journey.IsReplay && g.Journey.UserPaused && Mathf.Abs(g.Journey.T - at) < 0.05f,
                   "gif", $"around the replay's moment ({at:0.00}s), then back there, paused (at {g.Journey.T:0.00}s)");
            Shot("G2_replay_gif_saved");
            yield return AfterShot();
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 3f);

            // F12: a photo of the screen
            before = ClipRecorder.I.Finished;
            yield return Key(UnityEngine.InputSystem.Key.F12);
            yield return WaitClip(before);
            string png = ClipRecorder.I.LastSaved;
            bool pngOk = png != null && png.EndsWith(".png") && File.Exists(png) && new FileInfo(png).Length > 10000 && InSandbox(png) && ClipRecorder.I.LastError == null;
            Check2(pngOk, "photo", $"F12 saves a PNG: {Path.GetFileName(png)}, {(png != null && File.Exists(png) ? new FileInfo(png).Length / 1024 : -1)} KB, toast \"{ClipRecorder.I.ToastText}\"");
            Debug.Log($"[AutoPilot] gif files: {gif}");
        }

        /// <summary>padpilot: on a review, the d-pad onto SAVE GIF and A save one too.</summary>
        IEnumerator PadGif()
        {
            var g = Game.I;
            yield return PadOnto(g.Hud.GifButton.Image.rectTransform);
            int before = ClipRecorder.I.Finished;
            yield return PadButton(GamepadButton.South);
            yield return WaitClip(before);
            yield return new WaitForSecondsRealtime(0.4f);
            var c = ClipRecorder.I;
            bool ok = c.LastError == null && c.LastSaved != null && c.LastSaved.EndsWith(".gif") && File.Exists(c.LastSaved) && InSandbox(c.LastSaved) && g.Phase == Phase.Results;
            PadCheck(ok, $"the d-pad onto SAVE GIF and A save the trip's GIF ({Path.GetFileName(c.LastSaved)}, {c.LastFrames} frames), back on the review");
        }
    }
}
