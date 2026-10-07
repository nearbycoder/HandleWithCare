using System;
using System.Collections;
using System.IO;
using System.Text;
using HWC.Sim;
using HWC.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TMPro;
using System.Collections.Generic;

namespace HWC.Gameplay
{
    /// <summary>
    /// Command-line driven self-test and screenshot tours for the built player.
    ///   -hwcShots DIR [-hwcLevel N] [-hwcWhich ref|ref3|naive]  one delivery: packing, sealing, journey, results
    ///   -hwcAutopilot DIR                                      every delivery with its reference packing:
    ///                                                           PASS/FAIL against the expected outcome + screenshots
    ///   -hwcSave DIR -hwcSaveStep N                            one launch of the save test (Tools/savepilot.sh)
    /// Saves are disabled in these modes so a player's progress is never touched, except in the save
    /// test, which refuses to run outside a Logs/selftest folder.
    /// </summary>
    public sealed partial class AutoPilot : MonoBehaviour
    {
        string dir;
        int level = 1;
        string which = "ref";
        bool all;
        readonly StringBuilder report = new StringBuilder();

        public static bool TryStart(Game g)
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-hwcFps") >= 0) g.gameObject.AddComponent<FrameProbe>();
            string shots = Arg(args, "-hwcShots"), auto = Arg(args, "-hwcAutopilot"), menus = Arg(args, "-hwcMenus"), hints = Arg(args, "-hwcHints"), pad = Arg(args, "-hwcPad");
            string save = Arg(args, "-hwcSave");
            if (save != null) { StartSaveTest(g, save, Arg(args, "-hwcSaveStep")); return true; }
            if (shots == null && auto == null && menus == null && hints == null && pad == null) return false;
            SaveData.Disabled = true;
            var ap = g.gameObject.AddComponent<AutoPilot>();
            ap.dir = shots ?? auto ?? menus ?? hints ?? pad;
            ap.pad = pad != null;
            ap.all = auto != null;
            ap.hints = hints != null;
            g.SkipReveal = ap.all || ap.hints;
            ap.menus = menus != null;
            int.TryParse(Arg(args, "-hwcLevel") ?? "1", out ap.level);
            ap.which = Arg(args, "-hwcWhich") ?? "ref";
            Directory.CreateDirectory(ap.dir);
            return true;
        }

        static readonly string[] TestArgs = { "-hwcShots", "-hwcAutopilot", "-hwcMenus", "-hwcHints", "-hwcPad", "-hwcSave", "-hwcTrailer" };
        public static bool Requested => Array.Exists(Environment.GetCommandLineArgs(), a => Array.IndexOf(TestArgs, a) >= 0);

        /// <summary>
        /// Self-tests send their own input events, often to a window that doesn't have focus (other
        /// programs open windows too). From the first frame, keep every device listening without focus:
        /// by default a focus loss disables the mouse, and later clicks are dropped.
        /// </summary>
        public static void IgnoreFocus()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var d in InputSystem.devices) if (!d.enabled) InputSystem.EnableDevice(d);
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        bool menus, hints, pad;

        // ---- real input events (exercise the same path as a player's mouse) -------------------

        Vector2 mousePos;

        IEnumerator MoveMouse(Vector2 to, int frames = 12)
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

        bool buttonDown;

        IEnumerator Press(bool down)
        {
            buttonDown = down;
            var st = new MouseState { position = mousePos };
            if (down) st = st.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(Mouse.current, st);
            yield return null;
            yield return null;
        }

        IEnumerator ClickAt(Vector2 at)
        {
            yield return MoveMouse(at);
            yield return Press(true);
            yield return Press(false);
        }

        IEnumerator Key(Key key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
        }

        static Vector2 Screen(Vector3 world) => Game.I.Rig.Cam.WorldToScreenPoint(world);

        /// <summary>Plays delivery 1 the way a person would: click, drop, pick paper, paint, seal.</summary>
        IEnumerator PlayFirstDeliveryByHand(bool shots)
        {
            var g = Game.I;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mousePos = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            var box = g.Station.Box;
            if (g.Hud.ShiftCardShowing)
            {
                yield return ClickAt(mousePos);
                float wait = 0;
                while (g.Hud.ShiftCardShowing && wait < 3f) { wait += Time.unscaledDeltaTime; yield return null; }
                if (g.Hud.ShiftCardShowing) Debug.Log("[AutoPilot] FAIL input: clicking did not dismiss the shift card");
            }
            yield return ClickAt(Screen(g.Station.SlotPosition(0) + Vector3.up * 0.1f));
            if (g.Packing.Tool != Tool.Item) Debug.Log("[AutoPilot] FAIL input: clicking the teacup did not pick it up");
            yield return MoveMouse(Screen(box.CellToWorld(1.5f, 0.6f)), 20);
            if (shots) { Shot("H1_holding"); yield return AfterShot(); }
            yield return Press(true);
            yield return Press(false);
            if (g.Packing.RemainingItems().Count != 0) Debug.Log("[AutoPilot] FAIL input: the teacup was not placed");
            yield return Key(UnityEngine.InputSystem.Key.Digit1);
            if (g.Packing.Tool != Tool.Padding) Debug.Log("[AutoPilot] FAIL input: key 1 did not select paper");
            // paint: drag across the bottom row and back along the top
            yield return MoveMouse(Screen(box.CellToWorld(0.5f, 0.5f)), 10);
            yield return Press(true);
            yield return MoveMouse(Screen(box.CellToWorld(0.5f, 1.5f)), 10);
            yield return MoveMouse(Screen(box.CellToWorld(1.5f, 1.5f)), 10);
            yield return MoveMouse(Screen(box.CellToWorld(2.5f, 1.5f)), 10);
            yield return MoveMouse(Screen(box.CellToWorld(2.5f, 0.5f)), 10);
            yield return Press(false);
            if (shots) { Shot("H2_painted"); yield return AfterShot(); }
            int paper = g.Packing.Pk.UsedMaterials().Paper;
            Debug.Log($"[AutoPilot] {(paper >= 4 ? "PASS" : "FAIL")} input: painted {paper} paper by dragging");
            yield return Key(UnityEngine.InputSystem.Key.Escape);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            if (g.Phase == Phase.Packing) Debug.Log("[AutoPilot] FAIL input: space did not seal");
            else Debug.Log("[AutoPilot] PASS input: sealed with the space bar");
        }

        IEnumerator MenuTour()
        {
            var g = Game.I;
            g.Save.SeenTips.Clear();
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 1, Stars = 3, Delivered = true, UnderBudget = true, Careful = true, Attempts = 1, BestCost = 5, BestCare = 0.33f });
            // two stars, the budget one missing; one star, the care one missing too
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 2, Stars = 2, Delivered = true, Careful = true, Attempts = 3, BestCost = Levels.Get(2).Par + 2, BestCare = 0.41f, HintStage = 1 });
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 3, Stars = 1, Delivered = true, Attempts = 2, BestCost = Levels.Get(3).Par + 1, BestCare = 0.8f });
            g.ShowTitle();
            yield return new WaitForSecondsRealtime(2.0f);
            Shot("M1_title");
            yield return AfterShot();
            g.Menus.ShowSelect();
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("M2_select");
            yield return AfterShot();
            yield return DeliveryLogDetail();
            g.Menus.ShowSettings(g.ShowTitle);
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("M3_settings");
            yield return AfterShot();
            yield return DisplaySettings();
            g.Menus.HideAll();
            g.StartLevel(1);
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("M4_tutorial");
            yield return AfterShot();
            // focus loss mid-delivery pauses (the tour runs unfocused, so the handler is called directly)
            g.Save.PauseInBackground = true;
            g.SendMessage("OnApplicationFocus", false);
            Check2(g.Hud.Paused, "display", "losing focus while packing pauses the game");
            g.SendMessage("OnApplicationFocus", true);
            g.Hud.SetPaused(false);
            g.Save.PauseInBackground = false;
            yield return PlayFirstDeliveryByHand(true);
            while (g.Phase != Phase.Reveal) yield return null;
            float reveal0 = Time.unscaledTime;
            yield return new WaitForSecondsRealtime(2.9f);
            Shot("M6_reveal");
            while (g.Phase != Phase.Results) yield return null;
            Debug.Log($"[AutoPilot] first unboxing took {Time.unscaledTime - reveal0:0.0}s (quick {g.Reveal.Quick})");
            yield return new WaitForSecondsRealtime(2.2f);
            Shot("M7_results");
            yield return AfterShot();
            g.Hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("M8_pause");
            yield return AfterShot();
            g.Hud.SetPaused(false);
            yield return KeyboardRetryLoop();
            yield return KeyboardLayouts();
            yield return EscapeMenus();
            yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
            Application.Quit();
        }

        void Check(bool ok, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} keys: {what}");
        void Check2(bool ok, string area, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} {area}: {what}");

        static Vector2 RectScreen(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        /// <summary>The delivery log: the stars sit in their goal's place, and hovering a card explains them.</summary>
        IEnumerator DeliveryLogDetail()
        {
            var g = Game.I;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mousePos = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            var card2 = g.Menus.CardRect(2);
            var stars = new bool[3];
            for (int i = 0; i < 3; i++) stars[i] = card2.Find("star" + i).GetComponent<UnityEngine.UI.Image>().color.a > 0.5f;
            Check2(stars[0] && !stars[1] && stars[2], "log", "a two-star card missing the budget star shows gold, gap, gold");
            yield return MoveMouse(RectScreen(card2), 16);
            yield return new WaitForSecondsRealtime(0.3f);
            string d = g.Menus.SelectDetailText;
            Debug.Log("[AutoPilot] log: detail for card 2: " + d.Replace("\n", " | "));
            Check2(d.Contains("02") && d.Contains($"best {Levels.Get(2).Par + 2} / par {Levels.Get(2).Par}") && d.Contains("3 trips") && d.Contains("hinted"),
                   "log", "hovering card 2 shows its best cost against par, trips and the hint mark");
            Shot("M2b_select_detail");
            yield return AfterShot();
            yield return MoveMouse(RectScreen(g.Menus.CardRect(3)), 10);
            yield return new WaitForSecondsRealtime(0.3f);
            Check2(g.Menus.SelectDetailText.Contains("03") && g.Menus.SelectDetailText.Contains("best 80% / 65%"), "log", "hovering card 3 shows its best care against the 65% line");
        }

        /// <summary>The display settings, clicked with real mouse events.</summary>
        IEnumerator DisplaySettings()
        {
            var g = Game.I;
            var m = g.Menus;
            int launchW = UnityEngine.Screen.width, launchH = UnityEngine.Screen.height;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mousePos = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            // VSync off, then the frame-rate limit down to 30
            if (g.Save.VSync) yield return ClickAt(RectScreen((RectTransform)m.SettingToggle("vsync").transform));
            Check2(!g.Save.VSync && QualitySettings.vSyncCount == 0, "display", "clicking VSYNC turns it off");
            for (int i = 0; i < 4 && g.Save.FrameCap != 30; i++) yield return ClickAt(RectScreen(m.FrameRateButton.Image.rectTransform));
            Check2(g.Save.FrameCap == 30 && Application.targetFrameRate == 30, "display", $"clicking the frame-rate limit reaches 30 (targetFrameRate {Application.targetFrameRate})");
            yield return new WaitForSecondsRealtime(0.5f);
            int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(2f);
            float fps = (Time.frameCount - f0) / (Time.realtimeSinceStartup - t0);
            Check2(fps <= 31f, "display", $"measured {fps:0.0} fps with the 30 cap");
            // windowed, then the next window size
            if (g.Save.Fullscreen) yield return ClickAt(RectScreen((RectTransform)m.SettingToggle("full").transform));
            Check2(!g.Save.Fullscreen && m.WindowSizeButton.Interactable, "display", "fullscreen off enables WINDOW SIZE");
            yield return ClickAt(RectScreen(m.WindowSizeButton.Image.rectTransform));
            yield return new WaitForSecondsRealtime(1.0f);
            Debug.Log($"[AutoPilot] display: window size {g.Save.WindowW}x{g.Save.WindowH} chosen, window is {UnityEngine.Screen.width}x{UnityEngine.Screen.height} {UnityEngine.Screen.fullScreenMode}");
            Check2(g.Save.WindowW > 0 && g.Save.WindowH > 0, "display", "clicking WINDOW SIZE picks a size");
            yield return ClickAt(RectScreen((RectTransform)m.SettingToggle("bgpause").transform));
            Check2(g.Save.PauseInBackground, "display", "clicking PAUSE WHEN IN THE BACKGROUND turns it on");
            Shot("M3b_settings_display");
            yield return AfterShot();
            // the choices survive a save round trip
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(g.Save));
            Check2(back.VSync == g.Save.VSync && back.FrameCap == 30 && back.WindowW == g.Save.WindowW && back.PauseInBackground && !back.Fullscreen, "display", "the display settings survive the save file");
            // back to the defaults for the rest of the tour (the size the tour was launched at)
            g.Save.VSync = true; g.Save.FrameCap = 120; g.Save.PauseInBackground = false;
            g.Save.WindowW = launchW; g.Save.WindowH = launchH;
            g.ApplyDisplay();
            yield return new WaitForSecondsRealtime(0.8f);
            g.Save.WindowW = g.Save.WindowH = 0;
        }

        IEnumerator WaitPhase(Phase p, float timeout)
        {
            float t0 = Time.unscaledTime;
            while (Game.I.Phase != p && Time.unscaledTime - t0 < timeout) yield return null;
        }

        /// <summary>From Results back round the loop with the keyboard only: R, Space, Enter, Enter, Enter.</summary>
        IEnumerator KeyboardRetryLoop()
        {
            var g = Game.I;
            yield return Key(UnityEngine.InputSystem.Key.R);
            Check(g.Phase == Phase.Packing, "R on Results repacks");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Check(g.Phase == Phase.Sealing, "Space seals the kept packing");
            yield return WaitPhase(Phase.Journey, 5f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Reveal, 2f);
            Check(g.Phase == Phase.Reveal, "Enter skips the journey");
            float r0 = Time.unscaledTime;
            yield return WaitPhase(Phase.Results, 20f);
            Debug.Log($"[AutoPilot] repeat unboxing took {Time.unscaledTime - r0:0.0}s (quick {g.Reveal.Quick})");
            Check(g.Reveal.Quick, "a repeat unboxing plays quick");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Key(UnityEngine.InputSystem.Key.P);
            Check(g.Phase == Phase.Journey && g.Journey.IsReplay, "P on Results replays");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 2f);
            Check(g.Phase == Phase.Results, "Enter ends the replay");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.R);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Journey, 5f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Reveal, 2f);
            yield return new WaitForSecondsRealtime(0.3f);
            Shot("M9_reveal_skip_hint");
            yield return AfterShot();
            r0 = Time.unscaledTime;
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 3f);
            Check(g.Phase == Phase.Results && Time.unscaledTime - r0 < 1.5f, $"Enter skips the unboxing ({Time.unscaledTime - r0:0.00}s)");
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(g.Phase == Phase.Packing && g.Level.Number == 2, "Enter on Results goes to the next delivery (and doesn't seal it)");
        }

        static UiButton ButtonNamed(Transform root, string name)
        {
            foreach (var b in root.GetComponentsInChildren<UiButton>(true)) if (b.name == name) return b;
            return null;
        }

        /// <summary>Esc backs out of every menu to the right place, with real key events, and a press that
        /// closes a menu never also pauses or resumes the game underneath.</summary>
        IEnumerator EscapeMenus()
        {
            var g = Game.I;
            var m = g.Menus;
            var esc = UnityEngine.InputSystem.Key.Escape;
            g.Packing.DropTool();
            yield return null;
            yield return Key(esc);
            Check2(g.Hud.Paused && m.ScreenName == "none", "esc", "Esc while packing pauses");
            ButtonNamed(g.Hud.PauseRoot, "SETTINGS").Press();
            yield return null;
            Check2(m.ScreenName == "settings", "esc", "SETTINGS from the pause menu opens Settings");
            yield return Key(esc);
            yield return null;
            Check2(m.ScreenName == "none" && g.Hud.Paused && g.Hud.PauseRoot.gameObject.activeSelf, "esc", "Esc in Settings (from pause) goes back to the pause menu, still paused");
            ButtonNamed(g.Hud.PauseRoot, "DELIVERY LOG").Press();
            yield return null;
            Check2(m.ScreenName == "log", "esc", "DELIVERY LOG from the pause menu opens the log");
            yield return Key(esc);
            yield return null;
            Check2(m.ScreenName == "none" && g.Hud.Paused && g.Hud.PauseRoot.gameObject.activeSelf, "esc", "Esc in the log (from pause) goes back to the pause menu, still paused");
            yield return Key(esc);
            Check2(!g.Hud.Paused && g.Phase == Phase.Packing, "esc", "Esc in the pause menu resumes");
            // from the title screen
            g.ShowTitle();
            yield return new WaitForSecondsRealtime(0.8f);
            ButtonNamed(m.ActiveScreen, "SETTINGS").Press();
            yield return null;
            m.StartOverButton.Press();
            yield return null;
            Check2(m.ScreenName == "confirm", "esc", "START OVER asks first");
            yield return Key(esc);
            Check2(m.ScreenName == "settings" && g.Save.TotalStars > 0, "esc", "Esc on the start-over question keeps the progress and stays in Settings");
            yield return Key(esc);
            Check2(m.ScreenName == "title", "esc", "Esc in Settings (from the title) goes back to the title");
            yield return Key(esc);
            Check2(m.ScreenName == "title", "esc", "Esc on the title screen does nothing");
            ButtonNamed(m.ActiveScreen, "DELIVERY LOG").Press();
            yield return null;
            yield return Key(esc);
            Check2(m.ScreenName == "title", "esc", "Esc in the log (from the title) goes back to the title");
            ButtonNamed(m.ActiveScreen, "CREDITS").Press();
            yield return null;
            Check2(m.ScreenName == "credits", "esc", "CREDITS opens the credits");
            yield return Key(esc);
            Check2(m.ScreenName == "title", "esc", "Esc in the credits goes back to the title");
        }

        /// <summary>
        /// Undo and redo follow the key labels. The platform can't be given another layout here without
        /// changing the desktop's, so the labels are swapped in through Shortcuts.TestLabels and the
        /// physical keys are pressed with real key events.
        /// </summary>
        IEnumerator KeyboardLayouts()
        {
            var g = Game.I;
            Shortcuts.LogLayout();   // what this platform reports for the real keyboard
            var qwertz = new System.Collections.Generic.Dictionary<Key, string> { { UnityEngine.InputSystem.Key.Y, "z" }, { UnityEngine.InputSystem.Key.Z, "y" } };
            var azerty = new System.Collections.Generic.Dictionary<Key, string> {
                { UnityEngine.InputSystem.Key.Q, "a" }, { UnityEngine.InputSystem.Key.A, "q" }, { UnityEngine.InputSystem.Key.W, "z" },
                { UnityEngine.InputSystem.Key.Z, "w" }, { UnityEngine.InputSystem.Key.Semicolon, "m" }, { UnityEngine.InputSystem.Key.M, "," } };
            // Russian ЙЦУКЕН: no key is labelled Z or Y, so the US positions are kept
            var russian = new System.Collections.Generic.Dictionary<Key, string>();
            string ru = "фисвуапршолдьтщзйкыегмцчня";   // a..z
            for (int i = 0; i < 26; i++) russian[UnityEngine.InputSystem.Key.A + i] = ru[i].ToString();
            yield return LayoutCase(g, "US (the platform's names)", null, UnityEngine.InputSystem.Key.Z, UnityEngine.InputSystem.Key.Y, UnityEngine.InputSystem.Key.None, "Z", "Y");
            yield return LayoutCase(g, "German QWERTZ", qwertz, UnityEngine.InputSystem.Key.Y, UnityEngine.InputSystem.Key.Z, UnityEngine.InputSystem.Key.None, "Z", "Y");
            yield return LayoutCase(g, "French AZERTY", azerty, UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.Y, UnityEngine.InputSystem.Key.Z, "Z", "Y");
            yield return LayoutCase(g, "Russian", russian, UnityEngine.InputSystem.Key.Z, UnityEngine.InputSystem.Key.Y, UnityEngine.InputSystem.Key.None, "Я", "Н");
            Shot("K1_undo_hint_russian");
            yield return AfterShot();
            Shortcuts.TestLabels = null;
            yield return null;
        }

        /// <summary>One layout: the undo key empties a one-piece box, the redo key puts the piece back,
        /// a key that is neither does nothing, and the hints show the labels.</summary>
        IEnumerator LayoutCase(Game g, string name, System.Collections.Generic.Dictionary<Key, string> labels, Key undoKey, Key redoKey, Key idleKey, string undoHint, string redoHint)
        {
            Shortcuts.TestLabels = labels;
            yield return null;
            yield return null;
            g.Packing.ClearAll();
            yield return null;
            string empty = SaveData.Serialize(g.CurrentPacking);
            bool placed = false;
            foreach (var p in BottomUp(g.Level)) if (g.Packing.DebugPlace(p)) { placed = true; break; }
            string one = SaveData.Serialize(g.CurrentPacking);
            if (idleKey != UnityEngine.InputSystem.Key.None) yield return Key(idleKey);
            bool idleOk = SaveData.Serialize(g.CurrentPacking) == one;
            yield return Key(undoKey);
            bool undone = SaveData.Serialize(g.CurrentPacking) == empty;
            yield return Key(redoKey);
            bool redone = SaveData.Serialize(g.CurrentPacking) == one;
            string uh = g.Hud.UndoHint, rh = g.Hud.RedoHint;
            Check2(placed && undone && redone && idleOk && uh == undoHint && rh == redoHint, "layout",
                   $"{name}: {undoKey} undoes {(undone ? "yes" : "NO")}, {redoKey} redoes {(redone ? "yes" : "NO")}" +
                   (idleKey != UnityEngine.InputSystem.Key.None ? $", {idleKey} does nothing {(idleOk ? "yes" : "NO")}" : "") + $"; hints '{uh}' / '{rh}'");
        }

        IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            if (SaveTest) { yield return SaveTestRun(); yield break; }
            if (menus) { yield return MenuTour(); yield break; }
            if (pad)
            {
                yield return PadTour();
                yield return LargerTextTour();
                yield return NoteFitCheck();
                yield return PlayStationPrompts();
                yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
                yield return new WaitForSecondsRealtime(0.3f);
                Application.Quit();
                yield break;
            }
            if (hints)
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                for (int n = 1; n <= Levels.All.Count; n++) yield return RunHinted(n, n == 18);
                int budgetRuns = 0;
                for (int n = 1; n <= Levels.All.Count; n++) yield return RunBudgetHinted(n, n == 18, () => budgetRuns++);
                Debug.Log($"[AutoPilot] {(budgetRuns >= 21 ? "PASS" : "FAIL")} budget hints: {budgetRuns} deliveries shipped over budget and hinted about money (SimCheck finds a sample on 21)");
                // the story finale: Next after The Dragon Egg rolls credits and opens Overtime
                var g = Game.I;
                g.StartLevel(20);
                yield return new WaitForSecondsRealtime(0.3f);
                g.NextLevel();
                yield return new WaitForSecondsRealtime(0.8f);
                bool finale = g.Phase == Phase.Title && g.Menus.OvertimeNoteShowing && g.Save.IsUnlocked(21);
                Debug.Log($"[AutoPilot] {(finale ? "PASS" : "FAIL")} finale: credits with the Overtime note, delivery 21 unlocked");
                Shot("finale_overtime");
                yield return AfterShot();
                Game.I.Menus.ShowSelect();
                yield return new WaitForSecondsRealtime(0.8f);
                Shot("hints_log");
                yield return AfterShot();
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
                yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
                yield return new WaitForSecondsRealtime(0.3f);
                Application.Quit();
                yield break;
            }
            if (all)
            {
                for (int n = 1; n <= Levels.All.Count; n++) yield return RunLevel(n, "ref", false);
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
                yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
            }
            else
            {
                yield return RunLevel(level, which, true);
                yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
            }
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        IEnumerator RunLevel(int n, string whichPacking, bool tour)
        {
            var g = Game.I;
            var lv = Levels.Get(n);
            g.StartLevel(n);
            g.Packing.ClearAll();
            yield return new WaitForSecondsRealtime(tour ? 1.0f : 0.3f);
            if (tour) Shot($"L{n:00}_1_empty");

            var pk = whichPacking == "ref3" ? (lv.Reference3Packing() ?? lv.ReferencePacking()) : whichPacking == "expert" ? lv.ExpertPacking() : (whichPacking == "naive" ? NaivePacking(lv) : lv.ReferencePacking());
            // place pieces through the controller, bottom-up, like a player would
            var order = pk.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) { g.Packing.DebugAddDivider(d); if (tour) yield return new WaitForSecondsRealtime(0.08f); }
            foreach (var s in order.Shelves) { g.Packing.DebugAddShelf(s); if (tour) yield return new WaitForSecondsRealtime(0.08f); }
            // bottom-up, like a player; anything not placeable yet (a balloon tucked under a cake) waits a round
            var pending = new System.Collections.Generic.List<Placement>(order.Pieces);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                for (int i = 0; i < pending.Count; i++)
                {
                    if (!g.Packing.DebugPlace(pending[i])) continue;
                    pending.RemoveAt(i--);
                    if (tour) yield return new WaitForSecondsRealtime(0.12f);
                }
                if (pending.Count == before) { Fail(n, $"could not place {pending[0].Kind} at {pending[0].X},{pending[0].Y}"); yield break; }
            }
            yield return new WaitForSecondsRealtime(tour ? 0.8f : 0.1f);
            if (tour) Shot($"L{n:00}_2_packed");
            if (!g.Packing.ReadyToSeal) { Fail(n, "not ready to seal: " + g.CurrentPacking.Validate(lv)); yield break; }

            var expected = Simulator.Run(lv, pk, false);
            g.SealAndShip();
            yield return new WaitForSecondsRealtime(0.6f);
            if (tour) Shot($"L{n:00}_3_sealing");
            while (g.Phase == Phase.Sealing) yield return null;

            if (tour)
            {
                float dur = g.LastRun.Duration;
                // fixed points plus a frame just before each leg change (mid dip-to-black)
                var shots = new System.Collections.Generic.List<(float t, string name)>();
                foreach (float f in new[] { 0.2f, 0.45f, 0.7f, 0.9f }) shots.Add((dur * f, $"journey_{(int)(f * 100):00}"));
                var starts = g.LastRun.Kin.LegStartTick;
                for (int i = 1; i < starts.Count; i++) shots.Add((starts[i] * SimConst.Dt - 0.08f, $"journey_cut{i}"));
                shots.Sort((a, b) => a.t.CompareTo(b.t));
                foreach (var (t, name) in shots)
                {
                    while (g.Phase == Phase.Journey && g.Journey.T < t) yield return null;
                    Shot($"L{n:00}_4_{name}");
                }
            }
            else
            {
                g.Journey.Skip();
            }
            if (tour)
            {
                // the unboxing: arrival, flaps, items rising with their stamps
                while (g.Phase == Phase.Journey) yield return null;
                float r0 = Time.unscaledTime;
                foreach (float at in new[] { 0.6f, 1.6f, 3.0f, 4.6f })
                {
                    while (g.Phase == Phase.Reveal && Time.unscaledTime - r0 < at) yield return null;
                    if (g.Phase != Phase.Reveal) break;
                    Shot($"L{n:00}_5_reveal_{at:0.0}");
                }
                // the egg finale: the hatchling has popped out and been stamped
                if (System.Array.IndexOf(lv.Items, PieceKind.DragonEgg) >= 0)
                {
                    while (g.Phase == Phase.Reveal && (g.Reveal.HatchStartedAt < 0 || Time.unscaledTime - g.Reveal.HatchStartedAt < 1.45f)) yield return null;
                    if (g.Phase == Phase.Reveal) Shot($"L{n:00}_5_hatch");
                }
            }
            while (g.Phase != Phase.Results) yield return null;
            yield return new WaitForSecondsRealtime(tour ? 1.2f : 0.2f);
            if (tour) Shot($"L{n:00}_6_results");
            if (tour)
            {
                // back at the bench: the last trip's trails and report
                yield return AfterShot();
                g.Repack();
                yield return new WaitForSecondsRealtime(1.2f);
                Shot($"L{n:00}_7_bench_after");
                yield return AfterShot();
            }

            var got = g.LastRun;
            bool same = got.Hash == expected.Hash && got.Outcome.Stars == expected.Outcome.Stars;
            string line = $"#{n:00} {lv.Title}: stars {got.Outcome.Stars} delivered {got.Outcome.Delivered} cost {got.Outcome.Cost}/{lv.Par} care {got.Outcome.WorstCare:0.00} hash {(same ? "match" : "MISMATCH")}";
            bool pass = same && (whichPacking != "ref" || got.Outcome.Delivered);
            Debug.Log($"[AutoPilot] {(pass ? "PASS" : "FAIL")} {line}");
            report.AppendLine((pass ? "PASS " : "FAIL ") + line);
            if (!tour && n % 4 == 1) Shot($"auto_L{n:00}_results");
        }

        // ---- gamepad only: a virtual pad device, no mouse or keyboard events -------------------------

        Gamepad gp;
        GamepadState gs;

        void PadSend() => InputSystem.QueueStateEvent(gp, gs);

        /// <summary>The triggers are axes: GamepadState.WithButton can't set them (their enum values wrap into the d-pad bits).</summary>
        void PadSet(GamepadButton b, bool down)
        {
            if (b == GamepadButton.LeftTrigger) gs.leftTrigger = down ? 1f : 0f;
            else if (b == GamepadButton.RightTrigger) gs.rightTrigger = down ? 1f : 0f;
            else gs = gs.WithButton(b, down);
        }

        IEnumerator PadButton(GamepadButton b, int holdFrames = 2)
        {
            PadSet(b, true); PadSend();
            for (int i = 0; i < holdFrames; i++) yield return null;
            PadSet(b, false); PadSend();
            yield return null;
            yield return null;
        }

        IEnumerator PadHold(GamepadButton b, bool down)
        {
            PadSet(b, down); PadSend();
            yield return null;
            yield return null;
        }

        /// <summary>The left stick until the cursor is inside this rectangle (the d-pad only jumps between targets).</summary>
        IEnumerator PadStickInto(RectTransform rt, float timeout = 3f)
        {
            var target = RectScreen(rt);
            float t0 = Time.unscaledTime;
            while (!RectTransformUtility.RectangleContainsScreenPoint(rt, PadInput.I.CursorPosition, null) && Time.unscaledTime - t0 < timeout)
            {
                var d = target - PadInput.I.CursorPosition;
                gs.leftStick = d.normalized * Mathf.Clamp(d.magnitude / 200f, 0.5f, 1f);
                PadSend();
                yield return null;
            }
            gs.leftStick = Vector2.zero; PadSend();
            yield return null;
        }

        void PadCheck(bool ok, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} pad: {what}");

        static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9f;

        /// <summary>Presses a d-pad direction until the cursor sits on the target (or gives up).</summary>
        /// <summary>D-pad until the cursor is on this control.</summary>
        IEnumerator PadOnto(RectTransform rt, int maxSteps = 16)
        {
            var target = RectScreen(rt);
            for (int i = 0; i < maxSteps && !RectTransformUtility.RectangleContainsScreenPoint(rt, PadInput.I.CursorPosition, null); i++)
            {
                var d = target - PadInput.I.CursorPosition;
                GamepadButton b = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? GamepadButton.DpadRight : GamepadButton.DpadLeft) : (d.y > 0 ? GamepadButton.DpadUp : GamepadButton.DpadDown);
                yield return PadButton(b);
            }
        }

        // ---- LARGER TEXT, at the Steam Deck's 1280x800, with the pad only ------------------------------

        /// <summary>Text drawn outside its own rectangle (TextMeshPro's bounds against the rect, less margins).</summary>
        static bool Overflows(TextMeshProUGUI t)
        {
            var b = t.textBounds;
            if (b.size.x <= 0.01f || string.IsNullOrWhiteSpace(t.text)) return false;
            var r = t.rectTransform.rect;
            var m = t.margin;
            const float slack = 1.5f;
            return b.min.x < r.xMin + m.x - slack || b.max.x > r.xMax - m.z + slack || b.min.y < r.yMin + m.w - slack || b.max.y > r.yMax - m.y + slack;
        }

        /// <summary>On the screen showing now: with the setting on, the small texts are larger (never smaller)
        /// and none overflows its rectangle that didn't with the setting off.</summary>
        IEnumerator TextCheck(string screen)
        {
            var off = new Dictionary<TextMeshProUGUI, (float size, bool over)>();
            TextScale.Set(false);
            yield return null; yield return null;
            foreach (var t in TextScale.SmallTexts()) { t.ForceMeshUpdate(); off[t] = (t.fontSize, Overflows(t)); }
            TextScale.Set(true);
            yield return null; yield return null;
            int n = 0, grew = 0; float sum = 0, most = 1f;
            var bad = new List<string>();
            foreach (var t in TextScale.SmallTexts())
            {
                if (!off.TryGetValue(t, out var o) || string.IsNullOrWhiteSpace(t.text)) continue;
                t.ForceMeshUpdate();
                float r = t.fontSize / Mathf.Max(0.01f, o.size);
                n++; sum += r; most = Mathf.Max(most, r);
                if (r > 1.02f) grew++;
                if (t.fontSize < o.size - 0.05f) bad.Add($"{t.name} shrank {o.size:0.#} -> {t.fontSize:0.#}");
                if (Overflows(t) && !o.over) bad.Add($"{t.name} overflows: '{t.text.Replace("\n", " ")}'");
            }
            Check2(grew > 0 && bad.Count == 0, "text", $"{screen}: {grew} of {n} small texts larger (mean x{(n > 0 ? sum / n : 1f):0.00}, most x{most:0.00}), new overflows {bad.Count}{(bad.Count > 0 ? ": " + string.Join("; ", bad) : "")}");
        }

        IEnumerator LargerTextTour()
        {
            var g = Game.I;
            // switch it on in Settings (opened from pause) with the d-pad and A
            yield return PadButton(GamepadButton.Start);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.South);          // SETTINGS
            yield return new WaitForSecondsRealtime(0.6f);
            var toggle = (RectTransform)g.Menus.SettingToggle("text").transform;
            yield return PadOnto(toggle);
            yield return PadButton(GamepadButton.South);
            yield return new WaitForSecondsRealtime(0.3f);
            Check2(TextScale.Larger && g.Save.LargerText, "text", "the d-pad reaches LARGER TEXT and A switches it on");
            yield return TextCheck("settings");
            Shot("T1_settings_larger");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.4f);

            // the bench: order card, Mabel's hint and an item card
            var cam = g.Rig.Cam;
            Vector2 item = Vector2.zero;
            foreach (var t in g.Packing.TrayPositions) item = cam.WorldToScreenPoint(t + Vector3.up * 0.06f);
            yield return PadTo(item);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return TextCheck("packing (order card, hint note, item card)");
            Shot("T2_packing_larger");
            yield return AfterShot();

            // the delivery log with a card's detail line
            yield return PadButton(GamepadButton.Start);
            yield return new WaitForSecondsRealtime(0.3f);
            for (int i = 0; i < 3; i++) yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.South);          // DELIVERY LOG
            yield return new WaitForSecondsRealtime(0.6f);
            for (int i = 0; i < 8 && !g.Menus.SelectDetailText.Contains("01  "); i++) yield return PadButton(GamepadButton.DpadUp);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return TextCheck("delivery log");
            Shot("T3_log_larger");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);

            // a review: delivery 1 with its reference packing, shipped with View
            if (!g.Save.SeenTips.Contains("basics")) g.Save.SeenTips.Add("basics");
            g.StartLevel(1);
            yield return new WaitForSecondsRealtime(0.5f);
            g.Packing.ClearAll();
            foreach (var p in BottomUp(g.Level)) g.Packing.DebugPlace(p);
            yield return PadButton(GamepadButton.Select);
            yield return WaitPhase(Phase.Journey, 6f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return TextCheck("results");
            Shot("T4_results_larger");
            yield return AfterShot();
        }

        static Rect ScreenRectOf(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);   // overlay canvas: world corners are screen pixels
            float x0 = Mathf.Min(c[0].x, c[1].x, c[2].x, c[3].x), x1 = Mathf.Max(c[0].x, c[1].x, c[2].x, c[3].x);
            float y0 = Mathf.Min(c[0].y, c[1].y, c[2].y, c[3].y), y1 = Mathf.Max(c[0].y, c[1].y, c[2].y, c[3].y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>
        /// Mabel's notes grow to fit: every hint note of every delivery (each stage, each item it can be
        /// about) is shown at a readable size with LARGER TEXT off and on, intro notes keep the normal
        /// note, and the tallest note stays clear of the materials meter and the seal button.
        /// </summary>
        IEnumerator NoteFitCheck()
        {
            var g = Game.I;
            if (!g.Save.SeenTips.Contains("basics")) g.Save.SeenTips.Add("basics");
            string longest = null; int longN = 1, longStage = 1; PieceKind longFocus = PieceKind.Teacup;
            foreach (bool larger in new[] { false, true })
            {
                TextScale.Set(larger);
                g.StartLevel(15);
                yield return new WaitForSecondsRealtime(0.4f);
                float minSize = 99f, maxH = 0f; int notes = 0, grown = 0, introGrown = 0; string smallest = "";
                foreach (var lv in Levels.All)
                {
                    var (isz, ih) = g.Hud.FitNoteForTest(lv.Mabel);
                    if (ih > 150.5f) introGrown++;
                    var kinds = new List<PieceKind>();
                    foreach (var k in lv.Items) if (!kinds.Contains(k)) kinds.Add(k);
                    for (int st = 1; st <= Hints.MaxStage; st++)
                        foreach (var k in kinds)
                        {
                            string text = Hints.Note(lv, st, k);
                            var (sz, h) = g.Hud.FitNoteForTest(text);
                            notes++;
                            if (h > 150.5f) grown++;
                            if (sz < minSize) { minSize = sz; smallest = $"#{lv.Number} stage {st} {k}"; }
                            if (h > maxH) { maxH = h; longest = text; longN = lv.Number; longStage = st; longFocus = k; }
                        }
                }
                Debug.Log($"[AutoPilot] note: larger text {(larger ? "on" : "off")}: {notes} hint notes, smallest {minSize:0.0} ({smallest}), {grown} needed a taller note, tallest {maxH:0}, intro notes that grew {introGrown}");
                Check2(minSize >= Hud.NoteReadable - 0.1f, "note", $"larger text {(larger ? "on" : "off")}: every hint note at {Hud.NoteReadable:0.#} or more (smallest {minSize:0.0}, {smallest})");
                if (!larger) Check2(introGrown == 0, "note", $"intro notes keep the normal note ({introGrown} grew)");

                // the tallest one, shown the real way (hint stage and focus in the record) on its delivery
                var rec = g.Save.Get(longN, true);
                rec.Attempts = Mathf.Max(1, rec.Attempts);
                rec.HintStage = longStage; rec.HintFocus = (int)longFocus; rec.HintsHidden = false;
                g.StartLevel(longN);
                yield return new WaitForSecondsRealtime(0.6f);
                var sticky = ScreenRectOf(g.Hud.StickyRect);
                var btn = ScreenRectOf(g.Hud.HintButton.Image.rectTransform);
                var seal = ScreenRectOf(g.Hud.SealButton.Image.rectTransform);
                var budget = ScreenRectOf(g.Hud.BudgetRect);
                bool clear = !sticky.Overlaps(budget) && btn.yMin > seal.yMax + 40f && g.Hud.HintButton.isActiveAndEnabled && btn.yMin > 0;
                Debug.Log($"[AutoPilot] note: #{longN} stage {longStage}: font {g.Hud.NoteFontSize:0.0}, note {sticky.height:0}px of {UnityEngine.Screen.height}, button bottom {btn.yMin:0} vs seal top {seal.yMax:0}");
                Check2(clear && g.Hud.NoteText == longest, "note", $"larger text {(larger ? "on" : "off")}: the tallest note on its bench clears the meter and the seal button, ASK MABEL below it");
                // the button still works where it moved to (A with the pad cursor on it)
                yield return PadOnto(g.Hud.HintButton.Image.rectTransform);
                int before = rec.HintStage; bool hidden = rec.HintsHidden;
                yield return PadButton(GamepadButton.South);
                yield return new WaitForSecondsRealtime(0.2f);
                Check2(rec.HintStage != before || rec.HintsHidden != hidden, "note", "A on ASK MABEL (moved down) still asks");
                if (rec.HintStage != before) { rec.HintStage = before; } else rec.HintsHidden = hidden;
                g.StartLevel(longN);
                yield return new WaitForSecondsRealtime(0.6f);
                Shot(larger ? "N2_note_larger" : "N1_note");
                yield return AfterShot();
                if (!larger)
                {
                    // the cursor over the note: it fades so a shelf item behind it shows; away again: back
                    yield return PadStickInto(g.Hud.StickyRect);
                    yield return new WaitForSecondsRealtime(0.4f);
                    float faded = g.Hud.StickyAlpha;
                    Shot("N3_note_peek");
                    yield return AfterShot();
                    yield return PadStickInto(g.Hud.SealButton.Image.rectTransform);
                    yield return new WaitForSecondsRealtime(0.4f);
                    Check2(faded < 0.5f && g.Hud.StickyAlpha > 0.99f, "note", $"the note fades under the cursor ({faded:0.00}) and comes back ({g.Hud.StickyAlpha:0.00})");
                }
            }
            TextScale.Set(g.Save.LargerText);
        }

        /// <summary>
        /// PlayStation prompts: a second virtual pad with the Input System's own DualShock 4 layout makes
        /// the prompts read L1 / R1 / L2 / R2 / SHARE and the drawn shapes; the Xbox-style pad brings the
        /// letters back; the BUTTON ICONS setting (reached with the d-pad) forces PlayStation. Then a
        /// failed trip: the Last trip report shows the drawn cross.
        /// </summary>
        IEnumerator PlayStationPrompts()
        {
            var g = Game.I;
            string Tri = Glyphs.Tag(Glyphs.PsTriangle), Crs = Glyphs.Tag(Glyphs.PsCross), Sq = Glyphs.Tag(Glyphs.PsSquare);
            Check2(Glyphs.Ok, "ps", "the glyph sheet was drawn (the sprite shader is in the build)");
            g.Save.SeenTips.Remove("basics");
            g.Save.Records.RemoveAll(r => r.Number == 1);
            g.StartLevel(1);                                   // the tutorial's first note mentions A
            yield return new WaitForSecondsRealtime(0.6f);
            string xboxTut = g.Tutorial.ShownText, xboxSeal = g.Hud.PromptFor("VIEW");
            var ds4 = InputSystem.AddDevice<UnityEngine.InputSystem.DualShock.DualShock4GamepadHID>("PadPilotDS4");
            ds4.MakeCurrent();
            yield return null; yield return null;
            Debug.Log($"[AutoPilot] ps: current pad {Gamepad.current?.layout}, PlayStation {PadGlyphs.Ps}; undo '{g.Hud.PromptFor("LT")}', redo '{g.Hud.PromptFor("RT")}', seal '{g.Hud.PromptFor("VIEW")}', hint '{g.Hud.PromptFor("Y")}', shoulder '{g.Hud.PromptFor("LB")}'");
            Check2(PadGlyphs.Ps && g.Hud.PromptFor("LT") == "L2" && g.Hud.PromptFor("RT") == "R2" && g.Hud.PromptFor("VIEW") == "SHARE"
                   && g.Hud.PromptFor("LB") == "L1" && g.Hud.PromptFor("RB") == "R1" && g.Hud.PromptFor("Y") == Tri,
                   "ps", "a DualShock 4 turns the bench prompts into L2 / R2 / SHARE / L1 / R1 and the triangle");
            Check2(xboxTut.Contains("press A.") && g.Tutorial.ShownText.Contains("press " + Crs), "ps", $"the tutorial note follows: '{g.Tutorial.ShownText}'");
            Shot("C1_ps_tutorial");
            yield return AfterShot();
            gp.MakeCurrent();
            yield return null; yield return null;
            Check2(!PadGlyphs.Ps && g.Hud.PromptFor("VIEW") == xboxSeal && g.Hud.PromptFor("LT") == "LT" && g.Tutorial.ShownText == xboxTut,
                   "ps", "back on the Xbox-style pad, the letters return (AUTO)");
            InputSystem.RemoveDevice(ds4);
            g.Save.SeenTips.Add("basics");

            // BUTTON ICONS in Settings, from the pause menu, with the d-pad and A: AUTO -> XBOX -> PLAYSTATION
            g.Tutorial.Stop();
            yield return PadButton(GamepadButton.Start);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.South);          // SETTINGS
            yield return new WaitForSecondsRealtime(0.6f);
            yield return PadOnto(g.Menus.ButtonIconsButton.Image.rectTransform);
            yield return PadButton(GamepadButton.South);
            bool xbox = g.Save.ButtonIcons == PadGlyphs.Xbox && !PadGlyphs.Ps;
            yield return PadButton(GamepadButton.South);
            yield return null;
            Check2(xbox && g.Save.ButtonIcons == PadGlyphs.PlayStation && PadGlyphs.Ps && g.Menus.ButtonIconsButton.Label.text == "PLAYSTATION",
                   "ps", "the d-pad reaches BUTTON ICONS; A steps it to XBOX, then PLAYSTATION, on the Xbox-style pad");
            Shot("C2_ps_settings");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.4f);
            Check2(g.Hud.PromptFor("VIEW") == "SHARE" && g.Hud.PromptFor("LT") == "L2", "ps", "the bench follows the setting");

            // ship the teacup with no padding: it breaks; the review reads square / triangle / cross
            g.Packing.ClearAll();
            foreach (var p in BottomUp(g.Level)) if (!Catalog.Get(p.Kind).IsPadding) g.Packing.DebugPlace(p);
            yield return null;
            Check2(g.Hud.SealHintText == "Share to seal", "ps", $"the seal hint: '{g.Hud.SealHintText}'");
            yield return PadButton(GamepadButton.Select);
            yield return WaitPhase(Phase.Journey, 6f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(1.2f);
            string KeyOf(UiButton b) => Glyphs.Drawn(b.transform.Find("key").GetComponent<TextMeshProUGUI>());
            string drawn = $"{KeyOf(g.Hud.RepackButton)} | {KeyOf(g.Hud.ReplayButton)} | {KeyOf(g.Hud.NextButton)}";
            Check2(drawn == $"{Glyphs.PsSquare} | {Glyphs.PsTriangle} | {Glyphs.PsCross}", "ps", $"the review: square repacks, triangle replays, cross goes on (drawn: {drawn})");
            Shot("C3_ps_results");
            yield return AfterShot();
            yield return PadButton(GamepadButton.West);           // repack
            yield return WaitPhase(Phase.Packing, 3f);
            yield return new WaitForSecondsRealtime(0.5f);
            Check2(g.Hud.LastTripText.Contains(Glyphs.Tag(Glyphs.MarkCross)) && g.Hud.LastTripDrawn == Glyphs.MarkCross, "ps", $"the Last trip report draws the cross: '{g.Hud.LastTripText}' (drawn: {g.Hud.LastTripDrawn})");
            Shot("C4_last_trip_cross");
            yield return AfterShot();
            g.Save.ButtonIcons = PadGlyphs.Auto;
        }

        IEnumerator PadTo(Vector2 target, int maxSteps = 12)
        {
            for (int i = 0; i < maxSteps && !Near(PadInput.I.CursorPosition, target); i++)
            {
                var d = target - PadInput.I.CursorPosition;
                GamepadButton b = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? GamepadButton.DpadRight : GamepadButton.DpadLeft) : (d.y > 0 ? GamepadButton.DpadUp : GamepadButton.DpadDown);
                yield return PadButton(b);
            }
        }

        Vector2 CellScreen(float x, float y) => Game.I.Rig.Cam.WorldToScreenPoint(Game.I.Station.Box.CellToWorld(x, y));

        /// <summary>Title to delivery 2 with a gamepad only, then rotate, dividers, undo, Ask Mabel and pause.</summary>
        IEnumerator PadTour()
        {
            var g = Game.I;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            gp = InputSystem.AddDevice<Gamepad>("PadPilot");
            PadInput.I.PadOnly = true;   // whoever is at the desk may move the real mouse over this window
            g.Save.SeenTips.Clear();
            g.ShowTitle();
            yield return new WaitForSecondsRealtime(1.5f);

            yield return PadButton(GamepadButton.South);          // wakes the cursor (not a click)
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(PadInput.I.Active && g.Phase == Phase.Title, "the first press shows the cursor on the title screen");
            Shot("P1_title_cursor");
            yield return AfterShot();
            yield return PadButton(GamepadButton.South);          // START SHIFT
            yield return new WaitForSecondsRealtime(0.6f);
            PadCheck(g.Phase == Phase.Packing && g.Level.Number == 1, "A on START SHIFT opens delivery 1");
            if (g.Hud.ShiftCardShowing)
            {
                yield return PadButton(GamepadButton.South);
                float w = 0; while (g.Hud.ShiftCardShowing && w < 3f) { w += Time.unscaledDeltaTime; yield return null; }
                PadCheck(!g.Hud.ShiftCardShowing, "A dismisses the shift card");
            }
            yield return new WaitForSecondsRealtime(0.4f);

            // pick the teacup off the shelf
            var cam = g.Rig.Cam;
            Vector2 cup = Vector2.zero;
            foreach (var t in g.Packing.TrayPositions) cup = cam.WorldToScreenPoint(t + Vector3.up * 0.06f);
            yield return PadTo(cup);
            yield return PadButton(GamepadButton.South);
            PadCheck(g.Packing.Tool == Tool.Item, "d-pad to the teacup on the shelf, A picks it up");
            Shot("P2_holding_cup");
            yield return AfterShot();
            yield return PadTo(CellScreen(1.5f, 0.5f));
            yield return PadButton(GamepadButton.South);
            PadCheck(g.Packing.RemainingItems().Count == 0, "d-pad into the box, A drops the teacup");

            // paper: RB, then hold A and sweep with the d-pad
            yield return PadButton(GamepadButton.RightShoulder);
            PadCheck(g.Packing.Tool == Tool.Padding && g.Packing.HeldKind == PieceKind.Paper, "RB picks the first material (paper)");
            yield return PadTo(CellScreen(0.5f, 0.5f));
            yield return PadHold(GamepadButton.South, true);
            foreach (var c in new[] { new Vector2(0.5f, 1.5f), new Vector2(1.5f, 1.5f), new Vector2(2.5f, 1.5f), new Vector2(2.5f, 0.5f) })
            {
                var to = CellScreen(c.x, c.y);
                for (int i = 0; i < 4 && !Near(PadInput.I.CursorPosition, to); i++)
                {
                    var d = to - PadInput.I.CursorPosition;
                    var b = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? GamepadButton.DpadRight : GamepadButton.DpadLeft) : (d.y > 0 ? GamepadButton.DpadUp : GamepadButton.DpadDown);
                    gs = gs.WithButton(b, true); PadSend(); yield return null; yield return null;
                    gs = gs.WithButton(b, false); PadSend(); yield return null; yield return null;
                }
            }
            yield return PadHold(GamepadButton.South, false);
            int paper = g.Packing.Pk.UsedMaterials().Paper;
            PadCheck(paper >= 4, $"hold A and sweep the d-pad to paint ({paper} paper)");
            Shot("P3_painted");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);           // right click: erase the paper under the cursor
            PadCheck(g.Packing.Pk.UsedMaterials().Paper == paper - 1, "B erases the paper under the cursor");
            yield return PadButton(GamepadButton.Start);
            PadCheck(g.Packing.Tool == Tool.None && !g.Hud.Paused, "Start puts the paper down (and doesn't pause)");

            // seal, skip the trip and the unboxing, then the review
            yield return PadButton(GamepadButton.Select);
            PadCheck(g.Phase == Phase.Sealing, "View seals and ships");
            yield return WaitPhase(Phase.Journey, 6f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Reveal, 3f);
            PadCheck(g.Phase == Phase.Reveal, "B skips the trip");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return PadButton(GamepadButton.South);
            yield return WaitPhase(Phase.Results, 3f);
            PadCheck(g.Phase == Phase.Results, "A skips the unboxing");
            yield return new WaitForSecondsRealtime(1.2f);
            PadCheck(Near(PadInput.I.CursorPosition, ButtonScreen(g.Hud.NextButton)), "the cursor starts on NEXT");
            Shot("P4_results_cursor");
            yield return AfterShot();
            yield return PadButton(GamepadButton.West);
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(g.Phase == Phase.Packing && g.Level.Number == 1, "X repacks");
            yield return PadButton(GamepadButton.Select);
            yield return WaitPhase(Phase.Journey, 6f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return PadButton(GamepadButton.East);
            yield return WaitPhase(Phase.Results, 3f);
            yield return new WaitForSecondsRealtime(1.0f);
            yield return PadButton(GamepadButton.South);          // NEXT
            yield return new WaitForSecondsRealtime(0.6f);
            PadCheck(g.Phase == Phase.Packing && g.Level.Number == 2, "A on NEXT opens delivery 2");

            // a later delivery: dividers with undo / redo
            g.StartLevel(4);
            yield return new WaitForSecondsRealtime(0.8f);
            if (g.Hud.ShiftCardShowing) { yield return PadButton(GamepadButton.South); yield return new WaitForSecondsRealtime(0.6f); }
            for (int i = 0; i < 6 && g.Packing.Tool != Tool.Divider; i++) yield return PadButton(GamepadButton.RightShoulder);
            PadCheck(g.Packing.Tool == Tool.Divider, "RB cycles to the divider");
            yield return PadButton(GamepadButton.DpadLeft);
            yield return PadButton(GamepadButton.South);
            PadCheck(g.Packing.Pk.Dividers.Count == 1, "A places a divider on the line under the cursor");
            Shot("P5_divider");
            yield return AfterShot();
            yield return PadButton(GamepadButton.LeftTrigger);
            PadCheck(g.Packing.Pk.Dividers.Count == 0, "LT undoes it");
            yield return PadButton(GamepadButton.RightTrigger);
            PadCheck(g.Packing.Pk.Dividers.Count == 1, "RT redoes it");
            yield return PadButton(GamepadButton.RightShoulder);
            yield return PadButton(GamepadButton.Start);
            PadCheck(g.Packing.Tool == Tool.None && !g.Hud.Paused, "Start puts the tool down first");

            // Ember turns round with X; Ask Mabel with Y after a missed star
            var rec = g.Save.Get(15, true); rec.Attempts = 1; rec.Stars = 0;
            g.StartLevel(15);
            yield return new WaitForSecondsRealtime(0.8f);
            if (g.Hud.ShiftCardShowing) { yield return PadButton(GamepadButton.South); yield return new WaitForSecondsRealtime(0.6f); }
            Vector2 ember = Vector2.zero;
            int k = 0;
            foreach (var t in g.Packing.TrayPositions) { if (k++ == 0) ember = cam.WorldToScreenPoint(t + Vector3.up * 0.06f); }
            yield return PadTo(ember);
            yield return PadButton(GamepadButton.South);
            int facing = g.Packing.HeldFacing; bool rot = g.Packing.HeldRotated;
            yield return PadButton(GamepadButton.West);
            PadCheck(g.Packing.Tool == Tool.Item && (g.Packing.HeldFacing != facing || g.Packing.HeldRotated != rot), $"X turns the held {g.Packing.HeldKind}");
            yield return PadButton(GamepadButton.East);
            PadCheck(g.Packing.Tool == Tool.None && g.Packing.RemainingItems().Count == g.Level.Items.Length, "B puts the held item back on the shelf");
            yield return PadButton(GamepadButton.North);
            PadCheck(g.Save.HintStage(15) == 1, "Y asks Mabel");
            Shot("P6_hint");
            yield return AfterShot();

            // pause, settings and back with B
            yield return PadButton(GamepadButton.Start);
            PadCheck(g.Hud.Paused, "Start pauses");
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(Near(PadInput.I.CursorPosition, ButtonScreen(g.Hud.ResumeButton)), "the cursor starts on RESUME");
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.South);          // SETTINGS
            yield return new WaitForSecondsRealtime(0.4f);
            PadCheck(g.Menus.Open, "d-pad down twice and A opens Settings");
            yield return new WaitForSecondsRealtime(0.5f);
            PadCheck(Near(PadInput.I.CursorPosition, ButtonScreen(g.Menus.DefaultButton)), "the cursor starts on DONE in Settings opened from pause");
            var atDone = PadInput.I.CursorPosition;
            yield return PadButton(GamepadButton.DpadUp);
            PadCheck(PadInput.I.CursorPosition.y > atDone.y + 20f, "the d-pad moves between the settings opened from pause");
            Shot("P7_settings");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(!g.Menus.Open && g.Hud.Paused, "B leaves Settings");
            // the delivery log from the pause menu: the d-pad walks the cards and the detail follows
            yield return new WaitForSecondsRealtime(0.5f);
            for (int i = 0; i < 3; i++) yield return PadButton(GamepadButton.DpadDown);
            yield return PadButton(GamepadButton.South);          // DELIVERY LOG
            yield return new WaitForSecondsRealtime(0.6f);
            PadCheck(g.Menus.ActiveScreen != null && RectTransformUtility.RectangleContainsScreenPoint(g.Menus.DefaultButton.Image.rectTransform, PadInput.I.CursorPosition, null),
                     "d-pad down three times and A opens the delivery log, the cursor on BACK");
            for (int i = 0; i < 8 && !g.Menus.SelectDetailText.Contains("01  "); i++) yield return PadButton(GamepadButton.DpadUp);
            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("[AutoPilot] pad: log detail: " + g.Menus.SelectDetailText.Replace("\n", " | "));
            PadCheck(g.Menus.SelectDetailText.Contains("01  ") && g.Menus.SelectDetailText.Contains("DELIVERED"), "the d-pad onto card 1 shows its stars by goal");
            Shot("P8_log_detail");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.4f);
            PadCheck(!g.Menus.Open && g.Hud.Paused, "B leaves the delivery log, back to the pause menu");
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.2f);
            PadCheck(!g.Hud.Paused, "B resumes");
        }

        static Vector2 ButtonScreen(UiButton b)
        {
            var rt = b.Image.rectTransform;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        /// <summary>Ask Mabel on a delivery after a missed star: click the hint button through every stage
        /// with real mouse events, build exactly what the ghosts show, ship it and expect three stars.</summary>
        IEnumerator RunHinted(int n, bool shots)
        {
            var g = Game.I;
            var lv = Levels.Get(n);
            var rec = g.Save.Get(n, true);
            rec.Attempts = 1; rec.Stars = 0; rec.HintStage = 0; rec.HintFocus = -1; rec.HintsHidden = false;
            g.StartLevel(n);
            g.Packing.ClearAll();
            g.Menus.HideAll();
            yield return new WaitForSecondsRealtime(0.4f);
            var btn = g.Hud.HintButton;
            if (!btn.gameObject.activeInHierarchy) { Fail(n, "hint button not shown after a missed star"); yield break; }
            mousePos = ButtonScreen(btn) + new Vector2(0, -200);
            for (int st = 1; st <= Hints.MaxStage; st++)
            {
                yield return ClickAt(ButtonScreen(btn));
                yield return new WaitForSecondsRealtime(shots ? 0.5f : 0.05f);
                if (rec.HintStage != st) { Fail(n, $"click {st} left the hint stage at {rec.HintStage}"); yield break; }
                if (shots) { Shot($"hint_L{n:00}_stage{st}"); yield return AfterShot(); }
            }
            // build what the ghosts show, through the packing controller
            var pk = new Packing(lv.W, lv.H);
            pk.Pieces.AddRange(g.Packing.HintPieces);
            pk.Dividers.AddRange(g.Packing.HintDividers);
            pk.Shelves.AddRange(g.Packing.HintShelves);
            var order = pk.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) g.Packing.DebugAddDivider(d);
            foreach (var sh in order.Shelves) g.Packing.DebugAddShelf(sh);
            var pending = new System.Collections.Generic.List<Placement>(order.Pieces);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                for (int i = 0; i < pending.Count; i++) if (g.Packing.DebugPlace(pending[i])) pending.RemoveAt(i--);
                if (pending.Count == before) { Fail(n, $"could not place hinted {pending[0].Kind} at {pending[0].X},{pending[0].Y}"); yield break; }
            }
            yield return new WaitForSecondsRealtime(shots ? 0.6f : 0.1f);
            if (shots) { Shot($"hint_L{n:00}_built"); yield return AfterShot(); }
            if (!g.Packing.ReadyToSeal) { Fail(n, "hinted packing not ready to seal: " + g.CurrentPacking.Validate(lv)); yield break; }
            var expected = Simulator.Run(lv, pk, false);
            g.SealAndShip();
            while (g.Phase == Phase.Sealing) yield return null;
            g.Journey.Skip();
            while (g.Phase != Phase.Results) yield return null;
            yield return new WaitForSecondsRealtime(0.1f);
            var got = g.LastRun;
            bool pass = got.Hash == expected.Hash && got.Outcome.Stars == 3;
            string line = $"hints #{n:00} {lv.Title}: stars {got.Outcome.Stars} hash {(got.Hash == expected.Hash ? "match" : "MISMATCH")} note \"{Hints.Note(lv, 1, (PieceKind)Math.Max(0, rec.HintFocus))}\"";
            Debug.Log($"[AutoPilot] {(pass ? "PASS" : "FAIL")} {line}");
            report.AppendLine((pass ? "PASS " : "FAIL ") + line);
        }

        /// <summary>
        /// Ship a packing that only misses the budget star (Hints.OverBudgetSample), then: the LAST TRIP report
        /// names the cost, and Ask Mabel's hints, clicked with real mouse events, are about money: her costs,
        /// then her padding, then her dividers and shelves, then her whole packing.
        /// </summary>
        IEnumerator RunBudgetHinted(int n, bool shots, Action ran)
        {
            var g = Game.I;
            var lv = Levels.Get(n);
            var sample = Hints.OverBudgetSample(lv);
            if (sample == null) { Debug.Log($"[AutoPilot] budget #{n:00}: no over-budget sample (SimCheck finds none either)"); yield break; }
            var old = g.Save.Get(n);
            if (old != null) g.Save.Records.Remove(old);   // a fresh record: no stars yet
            g.StartLevel(n);
            g.Packing.ClearAll();
            var order = sample.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) g.Packing.DebugAddDivider(d);
            foreach (var sh in order.Shelves) g.Packing.DebugAddShelf(sh);
            var pending = new System.Collections.Generic.List<Placement>(order.Pieces);
            while (pending.Count > 0)
            {
                int before = pending.Count;
                for (int i = 0; i < pending.Count; i++) if (g.Packing.DebugPlace(pending[i])) pending.RemoveAt(i--);
                if (pending.Count == before) { Fail(n, $"budget: could not place {pending[0].Kind} at {pending[0].X},{pending[0].Y}"); yield break; }
            }
            yield return new WaitForSecondsRealtime(0.1f);
            var expected = Simulator.Run(lv, sample, false);
            g.SealAndShip();
            while (g.Phase == Phase.Sealing) yield return null;
            g.Journey.Skip();
            while (g.Phase != Phase.Results) yield return null;
            var got = g.LastRun.Outcome;
            if (g.LastRun.Hash != expected.Hash || !got.Delivered || !got.Careful || got.UnderBudget) { Fail(n, $"budget: the over-budget trip came out {got.Stars} stars, cost {got.Cost}/{lv.Par}, hash {(g.LastRun.Hash == expected.Hash ? "match" : "MISMATCH")}"); yield break; }
            yield return new WaitForSecondsRealtime(0.1f);
            g.Repack();
            yield return new WaitForSecondsRealtime(shots ? 0.6f : 0.2f);
            string report = g.Hud.LastTripText;
            if (!report.Contains($"Over budget: materials cost {got.Cost}, par {lv.Par}")) { Fail(n, "budget: the LAST TRIP report doesn't name the cost: " + report); yield break; }
            var rec = g.Save.Get(n);
            var btn = g.Hud.HintButton;
            if (!btn.gameObject.activeInHierarchy) { Fail(n, "budget: no ASK MABEL after a two-star trip"); yield break; }
            var src = Hints.Source(lv);
            int padding = 0; foreach (var p in src.Pieces) if (p.Def.IsPadding) padding++;
            mousePos = ButtonScreen(btn) + new Vector2(0, -200);
            for (int st = 1; st <= Hints.MaxStage; st++)
            {
                yield return ClickAt(ButtonScreen(btn));
                yield return new WaitForSecondsRealtime(shots ? 0.5f : 0.05f);
                if (rec.HintStage != st || rec.HintFocus != Hints.BudgetFocus) { Fail(n, $"budget: click {st}: stage {rec.HintStage}, focus {rec.HintFocus}"); yield break; }
                bool padOnly = true; foreach (var p in g.Packing.HintPieces) padOnly &= p.Def.IsPadding;
                bool ok = st == 1 ? g.Hud.NoteText.StartsWith($"Mine costs {src.Cost} (par {lv.Par})") && g.Packing.HintPieces.Count == 0
                        : st == 2 ? padOnly && g.Packing.HintPieces.Count == padding && g.Packing.HintDividers.Count + g.Packing.HintShelves.Count == 0
                        : st == 3 ? padOnly && g.Packing.HintPieces.Count == padding && g.Packing.HintDividers.Count == src.Dividers.Count && g.Packing.HintShelves.Count == src.Shelves.Count
                        : g.Packing.HintPieces.Count == src.Pieces.Count;
                if (!ok) { Fail(n, $"budget: stage {st} shows {g.Packing.HintPieces.Count} pieces, note \"{g.Hud.NoteText}\""); yield break; }
                if (shots) { Shot($"budget_L{n:00}_stage{st}"); yield return AfterShot(); }
            }
            ran();
            Debug.Log($"[AutoPilot] PASS budget #{n:00} {lv.Title}: shipped at {got.Cost}/{lv.Par} (2 stars, hash match); report and hints about the budget: \"{Hints.Note(lv, 1, lv.Items[0], true)}\"");
        }

        void Fail(int n, string why)
        {
            Debug.Log($"[AutoPilot] FAIL #{n:00} {why}");
            report.AppendLine($"FAIL #{n:00} {why}");
        }

        void Shot(string name)
        {
            var path = Path.Combine(dir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AutoPilot] shot " + path);
            shotFrame = Time.frameCount;
            pendingShots.Add((path, Time.realtimeSinceStartup));
        }

        int shotFrame = -10;
        readonly System.Collections.Generic.List<(string path, float t)> pendingShots = new System.Collections.Generic.List<(string, float)>();

        static bool Written(string path)
        {
            try { return File.Exists(path) && new FileInfo(path).Length > 0; } catch (IOException) { return false; }
        }

        /// <summary>Screenshots are written at the end of a frame, and under load sometimes later (or
        /// not at all): drop the ones on disk, FAIL the ones still missing after 5 s.</summary>
        void CheckShots()
        {
            for (int i = pendingShots.Count - 1; i >= 0; i--)
            {
                var (path, t) = pendingShots[i];
                if (Written(path)) pendingShots.RemoveAt(i);
                else if (Time.realtimeSinceStartup - t > 5f)
                {
                    Debug.Log("[AutoPilot] FAIL screenshot never written: " + path);
                    pendingShots.RemoveAt(i);
                }
            }
        }

        void Update() => CheckShots();

        /// <summary>Waits until the screenshot taken last has been written (or given up on).</summary>
        IEnumerator AfterShot()
        {
            while (Time.frameCount <= shotFrame + 1) yield return null;
            string last = pendingShots.Count > 0 ? pendingShots[pendingShots.Count - 1].path : null;
            while (last != null && pendingShots.Exists(p => p.path == last)) { CheckShots(); yield return null; }
        }

        /// <summary>Before quitting: every screenshot on disk (or reported missing).</summary>
        IEnumerator ShotsWritten()
        {
            while (pendingShots.Count > 0) { CheckShots(); yield return null; }
        }

        static Packing NaivePacking(LevelDef lv)
        {
            var pk = new Packing(lv.W, lv.H);
            foreach (var k in lv.Items)
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
    }

    /// <summary>-hwcFps: logs average and worst frame time for each game phase ("[Perf] ..." lines).</summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        Phase phase = Phase.Boot;
        int frames, timed;
        float total, worst;
        double cpuMain, gpu;
        readonly FrameTiming[] timing = new FrameTiming[1];

        void Update()
        {
            var g = Game.I;
            if (g == null) return;
            if (g.Phase != phase) { Flush(); phase = g.Phase; }
            float dt = Time.unscaledDeltaTime;
            if (frames > 3) { total += dt; worst = Mathf.Max(worst, dt); }   // skip the hitch of a phase change
            frames++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0) { cpuMain += timing[0].cpuMainThreadFrameTime; gpu += timing[0].gpuFrameTime; timed++; }
        }

        void OnDestroy() => Flush();

        void Flush()
        {
            int n = frames - 4;
            if (n > 10) Debug.Log($"[Perf] {phase}: {n / total:0} fps avg, worst frame {worst * 1000:0.0} ms over {n} frames"
                                  + (timed > 0 ? $"; main thread {cpuMain / timed:0.0} ms, GPU {gpu / timed:0.0} ms" : ""));
            frames = 0; total = 0; worst = 0; timed = 0; cpuMain = 0; gpu = 0;
        }
    }
}
