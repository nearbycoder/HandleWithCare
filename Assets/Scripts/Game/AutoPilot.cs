using System;
using System.Collections;
using System.IO;
using System.Text;
using HWC.Sim;
using HWC.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace HWC.Gameplay
{
    /// <summary>
    /// Command-line driven self-test and screenshot tours for the built player.
    ///   -hwcShots DIR [-hwcLevel N] [-hwcWhich ref|ref3|naive]  one delivery: packing, sealing, journey, results
    ///   -hwcAutopilot DIR                                      every delivery with its reference packing:
    ///                                                           PASS/FAIL against the expected outcome + screenshots
    /// Saves are disabled in these modes so a player's progress is never touched.
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
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
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 1, Stars = 3, Delivered = true, UnderBudget = true, Careful = true });
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 2, Stars = 2, Delivered = true });
            g.Save.Records.Add(new SaveData.LevelRecord { Number = 3, Stars = 1, Delivered = true });
            g.ShowTitle();
            yield return new WaitForSecondsRealtime(2.0f);
            Shot("M1_title");
            yield return AfterShot();
            g.Menus.ShowSelect();
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("M2_select");
            yield return AfterShot();
            g.Menus.ShowSettings(g.ShowTitle);
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("M3_settings");
            yield return AfterShot();
            g.Menus.HideAll();
            g.StartLevel(1);
            yield return new WaitForSecondsRealtime(1.2f);
            Shot("M4_tutorial");
            yield return AfterShot();
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
            yield return ShotsWritten();
                Debug.Log("[AutoPilot] done");
            Application.Quit();
        }

        void Check(bool ok, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} keys: {what}");

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

        IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            if (menus) { yield return MenuTour(); yield break; }
            if (pad)
            {
                yield return PadTour();
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

        void PadCheck(bool ok, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} pad: {what}");

        static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9f;

        /// <summary>Presses a d-pad direction until the cursor sits on the target (or gives up).</summary>
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
            Shot("P7_settings");
            yield return AfterShot();
            yield return PadButton(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            PadCheck(!g.Menus.Open && g.Hud.Paused, "B leaves Settings");
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
