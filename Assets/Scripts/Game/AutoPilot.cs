using System;
using System.Collections;
using System.IO;
using System.Text;
using HWC.Sim;
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
            string shots = Arg(args, "-hwcShots"), auto = Arg(args, "-hwcAutopilot"), menus = Arg(args, "-hwcMenus");
            if (shots == null && auto == null && menus == null) return false;
            SaveData.Disabled = true;
            var ap = g.gameObject.AddComponent<AutoPilot>();
            ap.dir = shots ?? auto ?? menus;
            ap.all = auto != null;
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

        bool menus;

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
            yield return new WaitForSecondsRealtime(2.9f);
            Shot("M6_reveal");
            while (g.Phase != Phase.Results) yield return null;
            yield return new WaitForSecondsRealtime(2.2f);
            Shot("M7_results");
            yield return AfterShot();
            g.Hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.4f);
            Shot("M8_pause");
            Debug.Log("[AutoPilot] done");
            Application.Quit();
        }

        IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            if (menus) { yield return MenuTour(); yield break; }
            if (all)
            {
                for (int n = 1; n <= Levels.All.Count; n++) yield return RunLevel(n, "ref", false);
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
                Debug.Log("[AutoPilot] done");
            }
            else
            {
                yield return RunLevel(level, which, true);
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

            var pk = whichPacking == "ref3" ? (lv.Reference3Packing() ?? lv.ReferencePacking()) : (whichPacking == "naive" ? NaivePacking(lv) : lv.ReferencePacking());
            // place pieces through the controller, bottom-up, like a player would
            var order = pk.Clone();
            order.Pieces.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            foreach (int d in order.Dividers) { g.Packing.DebugAddDivider(d); if (tour) yield return new WaitForSecondsRealtime(0.08f); }
            foreach (var s in order.Shelves) { g.Packing.DebugAddShelf(s); if (tour) yield return new WaitForSecondsRealtime(0.08f); }
            foreach (var p in order.Pieces)
            {
                if (!g.Packing.DebugPlace(p)) { Fail(n, $"could not place {p.Kind} at {p.X},{p.Y}"); yield break; }
                if (tour) yield return new WaitForSecondsRealtime(0.12f);
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
                foreach (float f in new[] { 0.2f, 0.45f, 0.7f, 0.9f })
                {
                    while (g.Phase == Phase.Journey && g.Journey.T < dur * f) yield return null;
                    Shot($"L{n:00}_4_journey_{(int)(f * 100):00}");
                }
            }
            else
            {
                g.Journey.Skip();
            }
            while (g.Phase != Phase.Results) yield return null;
            yield return new WaitForSecondsRealtime(tour ? 1.2f : 0.2f);
            if (tour) Shot($"L{n:00}_5_results");

            var got = g.LastRun;
            bool same = got.Hash == expected.Hash && got.Outcome.Stars == expected.Outcome.Stars;
            string line = $"#{n:00} {lv.Title}: stars {got.Outcome.Stars} delivered {got.Outcome.Delivered} cost {got.Outcome.Cost}/{lv.Par} care {got.Outcome.WorstCare:0.00} hash {(same ? "match" : "MISMATCH")}";
            bool pass = same && (whichPacking != "ref" || got.Outcome.Delivered);
            Debug.Log($"[AutoPilot] {(pass ? "PASS" : "FAIL")} {line}");
            report.AppendLine((pass ? "PASS " : "FAIL ") + line);
            if (!tour && n % 4 == 1) Shot($"auto_L{n:00}_results");
        }

        void Fail(int n, string why)
        {
            Debug.Log($"[AutoPilot] FAIL #{n:00} {why}");
            report.AppendLine($"FAIL #{n:00} {why}");
        }

        void Shot(string name)
        {
            var path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AutoPilot] shot " + path);
            shotFrame = Time.frameCount;
        }

        int shotFrame = -10;

        /// <summary>Waits until the screenshot taken this frame has been written.</summary>
        IEnumerator AfterShot()
        {
            while (Time.frameCount <= shotFrame + 1) yield return null;
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
}
