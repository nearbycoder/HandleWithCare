using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using HWC.Audio;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace HWC.Gameplay
{
    /// <summary>
    /// Scripted capture for the trailer and screenshots:
    ///   -hwcTrailer DIR -hwcShotList FILE [-hwcClips a,b,c]
    /// Plays each clip of the shot list at a fixed 30 fps game clock (Time.captureDeltaTime), so every
    /// frame is rendered no matter how slow the machine is, and writes DIR/clip/00000.jpg..., the
    /// game's own audio mix (AudioRenderer) as DIR/clip/audio.wav, and DIR/clip/events.txt
    /// (phases, incidents, legs, stamps, sounds with their clip times) for the edit.
    /// Shot list lines: "name kind key=value ...", '#' comments. Kinds: title, log, shift, level.
    /// Saves are disabled; music is muted so the clips carry sound effects only.
    /// </summary>
    public sealed class TrailerDirector : MonoBehaviour
    {
        public const int Fps = 30;

        string dir;
        readonly List<Dictionary<string, string>> clips = new List<Dictionary<string, string>>();
        Game G => Game.I;

        public static bool TryStart(Game g)
        {
            var args = Environment.GetCommandLineArgs();
            string outDir = Arg(args, "-hwcTrailer");
            if (outDir == null) return false;
            SaveData.Disabled = true;
            var td = g.gameObject.AddComponent<TrailerDirector>();
            td.dir = outDir;
            Directory.CreateDirectory(outDir);
            string list = Arg(args, "-hwcShotList");
            string only = Arg(args, "-hwcClips");
            var pick = only == null ? null : new HashSet<string>(only.Split(','));
            foreach (var raw in File.ReadAllLines(list))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var c = new Dictionary<string, string> { ["name"] = parts[0], ["kind"] = parts[1] };
                for (int i = 2; i < parts.Length; i++)
                {
                    int eq = parts[i].IndexOf('=');
                    if (eq > 0) c[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
                }
                if (pick == null || pick.Contains(c["name"])) td.clips.Add(c);
            }
            g.Save = NewSave();
            g.Autopilot = true;   // no shift cards unless a clip asks for one
            g.ApplySettings();
            return true;
        }

        static SaveData NewSave()
        {
            var s = new SaveData { Fullscreen = false, MusicVolume = 0f, HighQuality = true, ScreenShake = true };
            s.SeenTips.Add("basics");
            for (int c = 1; c <= 4; c++) s.SeenTips.Add("shift_" + c);
            return s;
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static string Get(Dictionary<string, string> c, string k, string def = null) => c.TryGetValue(k, out var v) ? v : def;
        static float GetF(Dictionary<string, string> c, string k, float def) => c.TryGetValue(k, out var v) ? float.Parse(v, CultureInfo.InvariantCulture) : def;
        static int GetI(Dictionary<string, string> c, string k, int def) => c.TryGetValue(k, out var v) ? int.Parse(v) : def;
        static bool GetB(Dictionary<string, string> c, string k, bool def) => c.TryGetValue(k, out var v) ? v == "1" || v == "true" : def;

        // ---- capture --------------------------------------------------------------------------

        string clipDir;
        int frame;
        bool recording;
        FileStream wav;
        int wavSamples;
        StreamWriter events;
        bool audioOk;
        int channels, rate;
        BlockingCollection<(byte[] data, int w, int h, GraphicsFormat fmt, string path)> queue;
        readonly ConcurrentBag<byte[]> pool = new ConcurrentBag<byte[]>();
        Thread[] workers;

        float ClipTime => frame / (float)Fps;

        void Log(string what)
        {
            if (events != null) events.WriteLine(ClipTime.ToString("0.000", CultureInfo.InvariantCulture) + "\t" + what);
        }

        void BeginClip(string name)
        {
            clipDir = Path.Combine(dir, name);
            if (Directory.Exists(clipDir)) Directory.Delete(clipDir, true);
            Directory.CreateDirectory(clipDir);
            frame = 0;
            events = new StreamWriter(Path.Combine(clipDir, "events.txt"));
            if (audioOk)
            {
                wav = new FileStream(Path.Combine(clipDir, "audio.wav"), FileMode.Create);
                wav.Write(new byte[44], 0, 44);
                wavSamples = 0;
            }
            recording = true;
            Debug.Log($"[Trailer] clip {name} start ({Screen.width}x{Screen.height})");
        }

        void EndClip()
        {
            recording = false;
            Debug.Log($"[Trailer] clip end, {frame} frames ({ClipTime:0.00}s)");
            events?.Dispose();
            events = null;
            if (wav != null)
            {
                WriteWavHeader(wav, wavSamples, channels, rate);
                wav.Dispose();
                wav = null;
            }
        }

        static void WriteWavHeader(FileStream fs, int samples, int ch, int rate)
        {
            int dataBytes = samples * 2;
            fs.Seek(0, SeekOrigin.Begin);
            var w = new BinaryWriter(fs, Encoding.ASCII, true);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate);
            w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
            w.Flush();
        }

        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (audioOk)
                {
                    int n = AudioRenderer.GetSampleCountForCaptureFrame();
                    if (n > 0)
                    {
                        var buf = new NativeArray<float>(n * channels, Allocator.Temp);
                        AudioRenderer.Render(buf);
                        if (recording && wav != null)
                        {
                            var bytes = new byte[buf.Length * 2];
                            for (int i = 0; i < buf.Length; i++)
                            {
                                short s = (short)Mathf.Clamp(Mathf.RoundToInt(buf[i] * 32767f), -32768, 32767);
                                bytes[i * 2] = (byte)(s & 0xff);
                                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xff);
                            }
                            wav.Write(bytes, 0, bytes.Length);
                            wavSamples += buf.Length;
                        }
                        buf.Dispose();
                    }
                }
                if (!recording) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var raw = tex.GetRawTextureData<byte>();
                if (!pool.TryTake(out var arr) || arr.Length != raw.Length) arr = new byte[raw.Length];
                raw.CopyTo(arr);
                queue.Add((arr, tex.width, tex.height, tex.graphicsFormat, Path.Combine(clipDir, frame.ToString("00000") + ".jpg")));
                Destroy(tex);
                frame++;
            }
        }

        void StartWorkers()
        {
            queue = new BlockingCollection<(byte[], int, int, GraphicsFormat, string)>(12);
            workers = new Thread[4];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(() =>
                {
                    foreach (var item in queue.GetConsumingEnumerable())
                    {
                        var jpg = ImageConversion.EncodeArrayToJPG(item.data, item.fmt, (uint)item.w, (uint)item.h, 0, 93);
                        File.WriteAllBytes(item.path, jpg);
                        pool.Add(item.data);
                    }
                }) { IsBackground = true };
                workers[i].Start();
            }
        }

        // ---- fake cursor (captures don't include the OS pointer) -------------------------------

        RectTransform cursor;

        void BuildCursor()
        {
            var canvas = Ui.CreateCanvas("TrailerCursor", 200, transform);
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "trailer_cursor" };
            // classic arrow pointer, unit space 0..32 with y down
            var poly = new[] { new Vector2(2, 2), new Vector2(2, 26), new Vector2(8, 20.5f), new Vector2(12.5f, 30), new Vector2(16.5f, 28.2f), new Vector2(12.2f, 19), new Vector2(20, 19) };
            var ink = Palette.Ink;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float cov = 0, edge = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / 4f) * 32f / S, (S - 1 - y + (sy + 0.5f) / 4f) * 32f / S);
                            bool inside = Inside(poly, p);
                            float d = EdgeDistance(poly, p);
                            if (inside && d > 1.3f) cov += 1;
                            else if (inside || d < 0.9f) edge += 1;
                        }
                    cov /= 16f; edge /= 16f;
                    float a = Mathf.Clamp01(cov + edge);
                    var c = a > 0 ? Color.Lerp(ink, Color.white, cov / a) : Color.clear;
                    c.a = a;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            var img = new GameObject("cursor").AddComponent<Image>();
            img.transform.SetParent(canvas.transform, false);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0, 1), 100f);
            img.raycastTarget = false;
            cursor = img.rectTransform;
            cursor.anchorMin = cursor.anchorMax = Vector2.zero;
            cursor.pivot = new Vector2(0, 1);
            cursor.sizeDelta = new Vector2(52, 52);
            img.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
            cursor.gameObject.SetActive(false);
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        static float EdgeDistance(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j]; var b = poly[i];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        void Update()
        {
            if (cursor != null && cursor.gameObject.activeSelf)
            {
                float k = Screen.height > 0 ? 1080f / Screen.height : 1f;
                cursor.anchoredPosition = mousePos * k;
            }
        }

        // ---- real input (same path as a player's mouse and keyboard) ---------------------------

        Vector2 mousePos;
        bool buttonDown;

        IEnumerator MoveMouse(Vector2 to, int frames = 14)
        {
            var from = mousePos;
            for (int i = 1; i <= frames; i++)
            {
                float u = i / (float)frames;
                u = u * u * (3 - 2 * u);
                mousePos = Vector2.Lerp(from, to, u);
                var st = new MouseState { position = mousePos };
                if (buttonDown) st = st.WithButton(MouseButton.Left, true);
                InputSystem.QueueStateEvent(Mouse.current, st);
                yield return null;
            }
        }

        IEnumerator Press(bool down)
        {
            buttonDown = down;
            var st = new MouseState { position = mousePos };
            if (down) st = st.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(Mouse.current, st);
            yield return null;
            yield return null;
        }

        IEnumerator Key(Key key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
        }

        IEnumerator Wait(float seconds)
        {
            float t = 0;
            while (t < seconds) { t += Clock.UnscaledDelta; yield return null; }
        }

        Vector2 ToScreen(Vector3 world) => G.Rig.Cam.WorldToScreenPoint(world);

        // ---- main -------------------------------------------------------------------------------

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureDeltaTime = 1f / Fps;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            rate = AudioSettings.outputSampleRate;
            audioOk = AudioRenderer.Start();
            Debug.Log($"[Trailer] AudioRenderer {(audioOk ? "on" : "UNAVAILABLE")}, {channels} ch @ {rate} Hz, {clips.Count} clips");
            BuildCursor();
            StartWorkers();
            StartCoroutine(CaptureLoop());
            AudioDirector.Played += (name, vol) => { if (recording) Log($"sfx {name} {vol:0.00}"); };
            G.Journey.IncidentHappened += inc => { if (recording) Log($"incident {inc.Kind} {G.Journey.Rec.Bodies[inc.Body].Kind}"); };
            G.Journey.LegChanged += leg => { if (recording && G.Journey.Rec != null) Log($"leg {leg} {G.Journey.Rec.Kin.Route.Legs[leg].Kind}"); };
            G.Journey.Caption += text => { if (recording) Log($"caption {text}"); };
            G.Reveal.ItemRevealed += (it, _) => { if (recording) Log($"stamp {it.Kind} {it.Status}"); };
            G.Hud.Cinematic = true;
            mousePos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            yield return Wait(1.0f);
            Debug.Log($"[Trailer] screen {Screen.width}x{Screen.height}, dt {Time.deltaTime:0.0000} unscaled {Time.unscaledDeltaTime:0.0000} clock {Clock.UnscaledDelta:0.0000}");

            foreach (var c in clips)
            {
                Debug.Log("[Trailer] === " + c["name"]);
                G.Save = NewSave();
                G.Autopilot = true;
                G.ApplySettings();
                var phaseLogger = StartCoroutine(LogPhases());
                switch (c["kind"])
                {
                    case "title": yield return TitleClip(c); break;
                    case "log": yield return LogClip(c); break;
                    case "shift": yield return ShiftClip(c); break;
                    case "level": yield return LevelClip(c); break;
                    default: Debug.LogError("[Trailer] unknown clip kind " + c["kind"]); break;
                }
                StopCoroutine(phaseLogger);
                if (recording) EndClip();
                cursor.gameObject.SetActive(false);
                Time.timeScale = 1f;
                yield return null;
            }
            queue.CompleteAdding();
            foreach (var w in workers) w.Join();
            if (audioOk) AudioRenderer.Stop();
            Debug.Log("[Trailer] done");
            Application.Quit();
        }

        IEnumerator LogPhases()
        {
            var last = (Phase)(-1);
            while (true)
            {
                if (G.Phase != last) { last = G.Phase; if (recording) Log("phase " + last); }
                yield return null;
            }
        }

        IEnumerator TitleClip(Dictionary<string, string> c)
        {
            G.Journey.Stop();
            G.ShowTitle();
            if (!GetB(c, "ui", true)) G.Menus.HideAll();
            yield return Wait(GetF(c, "settle", 1.5f));
            BeginClip(c["name"]);
            yield return Wait(GetF(c, "dur", 5f));
        }

        IEnumerator LogClip(Dictionary<string, string> c)
        {
            G.Journey.Stop();
            G.ShowTitle();
            // a player partway through: three shifts done with mixed stars, the fourth just started
            int[] stars = { 3, 3, 2, 3, 3, 3, 2, 3, 3, 1, 3, 3, 2, 3, 3, 3, 2 };
            for (int i = 0; i < stars.Length && i < GetI(c, "done", 17); i++)
                G.Save.Records.Add(new SaveData.LevelRecord { Number = i + 1, Stars = stars[i], Delivered = true, UnderBudget = stars[i] >= 2, Careful = stars[i] == 3 });
            G.Menus.ShowSelect();
            yield return Wait(0.8f);
            BeginClip(c["name"]);
            yield return Wait(GetF(c, "dur", 4f));
        }

        IEnumerator ShiftClip(Dictionary<string, string> c)
        {
            int n = GetI(c, "level", 16);
            G.Autopilot = false;
            G.Save.SeenTips.Remove("shift_" + Levels.Get(n).Chapter);
            BeginClip(c["name"]);
            G.StartLevel(n);
            G.Packing.ClearAll();
            yield return Wait(GetF(c, "dur", 4.2f));
        }

        Packing TargetPacking(LevelDef lv, Dictionary<string, string> c)
        {
            string pack = Get(c, "pack", "ref");
            if (pack == "ref") return lv.ReferencePacking();
            if (pack == "naive") return Naive(lv);
            int[] div = null;
            if (c.TryGetValue("div", out var d)) div = Array.ConvertAll(d.Split(','), int.Parse);
            return LevelParse.Parse(lv, pack.Split('/'), div, Get(c, "shelf"), Get(c, "mods"));
        }

        /// <summary>Items only, heaviest first, dropped left to right (same as the validator's naive packer).</summary>
        static Packing Naive(LevelDef lv)
        {
            var pk = new Packing(lv.W, lv.H);
            var items = new List<PieceKind>(lv.Items);
            items.Sort((a, b) => Catalog.Get(b).Mass.CompareTo(Catalog.Get(a).Mass));
            foreach (var k in items)
            {
                bool done = false;
                for (int y = 0; y < lv.H && !done; y++)
                    for (int x = 0; x < lv.W && !done; x++)
                    {
                        var p = new Placement(k, x, y);
                        if (pk.CanPlace(p)) { pk.Pieces.Add(p); done = true; }
                    }
            }
            return pk;
        }

        IEnumerator LevelClip(Dictionary<string, string> c)
        {
            int n = GetI(c, "level", 1);
            var lv = Levels.Get(n);
            var target = TargetPacking(lv, c);
            G.SkipReveal = !GetB(c, "reveal", true);
            G.Journey.Stop();
            G.StartLevel(n);
            G.Packing.ClearAll();
            yield return Wait(GetF(c, "settle", 0.6f));
            string mode = Get(c, "packmode", "instant");
            bool record = Get(c, "from", "start") == "start";
            if (record) BeginClip(c["name"]);
            if (mode == "hand") yield return HandPack(lv, target);
            else yield return QuickPack(target, mode == "instant" ? 0f : GetF(c, "delay", 0.18f));
            if (Canon(G.Packing.Pk) != Canon(target))
            {
                Debug.Log("[Trailer] packing differs from target, placing directly");
                G.Packing.ClearAll();
                yield return QuickPack(target, 0f);
            }
            if (mode == "hand")
            {
                // park the pointer on the seal button before pressing space
                var seal = G.Hud.SealScreen();
                if (seal.HasValue) yield return MoveMouse(CanvasToScreen(seal.Value), 16);
            }
            yield return Wait(GetF(c, "hold", 1.0f));
            if (!GetB(c, "seal", true)) yield break;
            if (!record && Get(c, "from") == "seal") BeginClip(c["name"]);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            if (G.Phase == Phase.Packing) { Debug.Log("[Trailer] space did not seal: " + G.CurrentPacking.Validate(lv)); G.SealAndShip(); }
            yield return Wait(0.3f);
            cursor.gameObject.SetActive(false);
            while (G.Phase == Phase.Sealing) yield return null;
            if (!recording && Get(c, "from") == "journey") BeginClip(c["name"]);
            G.Journey.CameraMode = GetI(c, "cam", 0);
            float speed = GetF(c, "speed", 1f);
            G.Journey.Speed = speed;
            while (G.Phase == Phase.Journey) yield return null;
            if (!GetB(c, "afterjourney", true)) yield break;
            while (G.Phase == Phase.Reveal) yield return null;
            if (!GetB(c, "results", true)) yield break;
            yield return Wait(GetF(c, "resultshold", 2.5f));
            foreach (var step in Get(c, "after", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (step == "replay")
                {
                    Log("replay");
                    G.Replay();
                    G.Journey.CameraMode = GetI(c, "replaycam", 1);
                    G.Journey.Speed = GetF(c, "replayspeed", 1f);
                    while (G.Phase == Phase.Journey) yield return null;
                    yield return Wait(0.8f);
                }
                else if (step == "repack")
                {
                    Log("repack");
                    G.Repack();
                    yield return Wait(GetF(c, "repackhold", 4f));
                }
            }
        }

        static string Canon(Packing pk)
        {
            var parts = new List<string>();
            foreach (var p in pk.Pieces) parts.Add($"{p.Kind},{p.X},{p.Y},{p.Rotated},{p.Facing},{p.Strapped}");
            parts.Sort(StringComparer.Ordinal);
            var div = new List<int>(pk.Dividers); div.Sort();
            var sh = new List<string>(); foreach (var s in pk.Shelves) sh.Add(s.Row + "@" + s.Col); sh.Sort(StringComparer.Ordinal);
            return string.Join(";", parts) + "|" + string.Join(",", div) + "|" + string.Join(",", sh);
        }

        Vector2 CanvasToScreen(Vector2 canvasLocal)
        {
            float k = Screen.height / 1080f;
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + canvasLocal * k;
        }

        IEnumerator QuickPack(Packing target, float delay)
        {
            var order = target.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) { G.Packing.DebugAddDivider(d); if (delay > 0) yield return Wait(delay); }
            foreach (var s in order.Shelves) { G.Packing.DebugAddShelf(s); if (delay > 0) yield return Wait(delay); }
            foreach (var p in order.Pieces)
            {
                if (!G.Packing.DebugPlace(p)) Debug.Log($"[Trailer] could not place {p.Kind} at {p.X},{p.Y}");
                if (delay > 0) yield return Wait(delay);
            }
            G.Packing.DropTool();
        }

        /// <summary>
        /// Packs like a person: hovers each item on the shelf (item card), carries it to its cell,
        /// paints padding in drags along each row, flips creatures with R, straps with the strap tool.
        /// </summary>
        IEnumerator HandPack(LevelDef lv, Packing target)
        {
            var pc = G.Packing;
            var box = G.Station.Box;
            cursor.gameObject.SetActive(true);
            var usedSlots = new HashSet<int>();
            foreach (int d in target.Dividers)
            {
                yield return Key(UnityEngine.InputSystem.Key.Digit4);
                yield return MoveMouse(ToScreen(box.CellToWorld(d, lv.H * 0.5f)), 12);
                pc.DebugAddDivider(d);
                yield return Wait(0.25f);
                yield return Key(UnityEngine.InputSystem.Key.Escape);
            }
            var pieces = new List<Placement>(target.Pieces);
            pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            int i = 0;
            while (i < pieces.Count)
            {
                var p = pieces[i];
                foreach (var s in target.Shelves)
                    if (s.Row <= p.Y && !pc.Pk.Shelves.Contains(s))
                    {
                        yield return Key(UnityEngine.InputSystem.Key.Digit5);
                        yield return MoveMouse(ToScreen(box.CellToWorld(s.Col + 0.5f, s.Row)), 12);
                        pc.DebugAddShelf(s);
                        yield return Wait(0.25f);
                        yield return Key(UnityEngine.InputSystem.Key.Escape);
                    }
                if (p.Def.IsPadding)
                {
                    // a run of the same padding along this row
                    int j = i;
                    while (j + 1 < pieces.Count && pieces[j + 1].Kind == p.Kind && pieces[j + 1].Y == p.Y && pieces[j + 1].X == pieces[j].X + 1) j++;
                    var key = p.Kind == PieceKind.Paper ? UnityEngine.InputSystem.Key.Digit1 : (p.Kind == PieceKind.Bubble ? UnityEngine.InputSystem.Key.Digit2 : UnityEngine.InputSystem.Key.Digit3);
                    if (pc.Tool != Tool.Padding || pc.HeldKind != p.Kind)
                    {
                        if (pc.Tool != Tool.None) yield return Key(UnityEngine.InputSystem.Key.Escape);
                        yield return Key(key);
                    }
                    yield return MoveMouse(ToScreen(box.CellToWorld(p.X + 0.5f, p.Y + 0.5f)), 12);
                    yield return Press(true);
                    for (int k = i + 1; k <= j; k++) yield return MoveMouse(ToScreen(box.CellToWorld(pieces[k].X + 0.5f, pieces[k].Y + 0.5f)), 7);
                    yield return Press(false);
                    yield return Wait(0.1f);
                    i = j + 1;
                    continue;
                }
                if (pc.Tool != Tool.None) yield return Key(UnityEngine.InputSystem.Key.Escape);
                int slot = -1;
                for (int s = 0; s < lv.Items.Length; s++) if (lv.Items[s] == p.Kind && !usedSlots.Contains(s)) { slot = s; break; }
                usedSlots.Add(slot);
                // hover the item on the shelf so its card shows, then pick it up
                yield return MoveMouse(ToScreen(G.Station.SlotPosition(slot) + Vector3.up * (Catalog.Get(p.Kind).H * BoxView.Cell * 0.5f)), 16);
                yield return Wait(0.5f);
                yield return Press(true);
                yield return Press(false);
                if (pc.Tool != Tool.Item) { Debug.Log($"[Trailer] hand: could not pick {p.Kind}"); pc.DebugPlace(p); i++; continue; }
                if (p.Rotated) { yield return Key(UnityEngine.InputSystem.Key.R); yield return Wait(0.15f); }
                if (p.Def.Has(Quirk.Facing) && p.Facing < 0 && !p.Rotated) { yield return Key(UnityEngine.InputSystem.Key.R); yield return Wait(0.15f); }
                yield return MoveMouse(ToScreen(box.CellToWorld(p.X + p.W * 0.5f, p.Y + p.H * 0.5f)), 18);
                yield return Wait(0.12f);
                yield return Press(true);
                yield return Press(false);
                yield return Wait(0.2f);
                if (p.Strapped)
                {
                    yield return Key(UnityEngine.InputSystem.Key.Digit6);
                    yield return Press(true);
                    yield return Press(false);
                    yield return Wait(0.2f);
                    yield return Key(UnityEngine.InputSystem.Key.Escape);
                }
                i++;
            }
            if (pc.Tool != Tool.None) yield return Key(UnityEngine.InputSystem.Key.Escape);
        }
    }
}
