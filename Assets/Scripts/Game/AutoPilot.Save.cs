using System.Collections;
using System.Collections.Generic;
using System.IO;
using HWC.Sim;
using HWC.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HWC.Gameplay
{
    /// <summary>
    /// The save test (Tools/savepilot.sh): several launches of the player on one save, with saving
    /// switched on. It only runs when the save folder is inside Logs/selftest (the script points
    /// XDG_CONFIG_HOME there), so it can never touch a player's real progress.
    ///   1  fresh save: settings clicked, delivery 1 delivered, an unsealed box kept through Main Menu and quit
    ///   2  restart: progress, settings and the unsealed box are back
    ///   3  (save.json cut in half) the backup loads, the damaged file is kept, the title says so
    ///   4  (garbage, no backup) a fresh save, both damaged files kept, and it saves again;
    ///      then delivery 1 with three stars (its best packing)
    ///   5  restart: a failed trip, then MY BEST brings the three-star packing back (undo, redo, ship it)
    ///   6  Settings > START OVER: cancel changes nothing; confirm clears progress, keeps the settings
    ///      and a copy of the old save
    /// </summary>
    public sealed partial class AutoPilot
    {
        public static bool SaveTest { get; private set; }
        int saveStep;
        bool saveRefused;

        static void StartSaveTest(Game g, string dir, string step)
        {
            SaveTest = true;
            SaveData.LogWrites = true;
            var ap = g.gameObject.AddComponent<AutoPilot>();
            ap.dir = dir;
            int.TryParse(step ?? "1", out ap.saveStep);
            if (!Application.persistentDataPath.Replace('\\', '/').Contains("/Logs/selftest/"))
            {
                SaveData.Disabled = true;   // not a test folder: write nothing at all
                ap.saveRefused = true;
            }
            Directory.CreateDirectory(dir);
        }

        string DataDir => Application.persistentDataPath;
        string SavePath => Path.Combine(DataDir, "save.json");
        string ExpectedFile(string name) => Path.Combine(dir, "expected-" + name + ".txt");
        void SaveCheck(bool ok, string what) => Debug.Log($"[AutoPilot] {(ok ? "PASS" : "FAIL")} save {saveStep}: {what}");

        IEnumerator SaveTestRun()
        {
            var g = Game.I;
            IgnoreFocus();
            Debug.Log($"[AutoPilot] save {saveStep}: window focused {Application.isFocused}, mouse enabled {Mouse.current?.enabled}");
            mousePos = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            if (saveRefused)
            {
                Debug.Log("[AutoPilot] FAIL save: refusing to run outside a Logs/selftest folder: " + DataDir);
            }
            else
            {
                Debug.Log($"[AutoPilot] save {saveStep}: folder {DataDir}, load {SaveData.LastLoad}");
                yield return new WaitForSecondsRealtime(1.0f);   // the title's entrance
                switch (saveStep)
                {
                    case 1: yield return SaveStep1(g); break;
                    case 2: yield return SaveStep2(g); break;
                    case 3: yield return SaveStep3(g); break;
                    case 4: yield return SaveStep4(g); break;
                    case 5: yield return SaveStep5(g); break;
                    case 6: yield return SaveStep6(g); break;
                    case 7: yield return SaveStep7(g); break;
                }
                SaveCheck(Directory.GetFiles(DataDir, "*.tmp").Length == 0, "no temporary file left behind");
                yield return ShotsWritten();
            }
            Debug.Log("[AutoPilot] done");
            yield return new WaitForSecondsRealtime(0.3f);
            if (lastChange > 0f) SaveCheck(Time.unscaledTime - lastChange < 1.5f, $"quitting {Time.unscaledTime - lastChange:0.00}s after the last change, before the timed save");
            Application.Quit();   // OnApplicationQuit keeps the box on the bench
        }

        IEnumerator ClickButton(UiButton b) => ClickAt(RectScreen(b.Image.rectTransform));

        /// <summary>The reference packing's pieces, bottom-up (the order a player would place them).</summary>
        static List<Placement> BottomUp(LevelDef lv)
        {
            var list = new List<Placement>(lv.ReferencePacking().Pieces);
            list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            return list;
        }

        /// <summary>Places the next reference piece that fits, so the box changes.</summary>
        static bool PlaceOne(Game g, List<Placement> pending)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                if (!g.Packing.DebugPlace(pending[i])) continue;
                pending.RemoveAt(i);
                return true;
            }
            return false;
        }

        IEnumerator ContinueFromTitle(Game g)
        {
            yield return new WaitForSecondsRealtime(0.9f);
            yield return ClickButton(g.Menus.DefaultButton);   // CONTINUE
            yield return new WaitForSecondsRealtime(0.6f);
        }

        IEnumerator SaveStep1(Game g)
        {
            SaveCheck(SaveData.LastLoad == SaveData.LoadResult.Fresh && g.Save.Records.Count == 0, "a new player starts with a fresh save");
            g.Save.SeenTips.Add("basics");    // no tutorial or shift card in the way
            g.Save.SeenTips.Add("shift_1");
            // settings, clicked with real mouse events, then DONE (which saves)
            var m = g.Menus;
            m.ShowSettings(g.ShowTitle);
            yield return new WaitForSecondsRealtime(0.6f);
            foreach (var key in new[] { "shake", "vsync", "bgpause", "text" })
            {
                var t = m.SettingToggle(key);
                yield return ClickAt(RectScreen((RectTransform)t.transform));
                Debug.Log($"[AutoPilot] save 1: clicked {key} at {RectScreen((RectTransform)t.transform)}: now {t.isOn}");
            }
            Shot("S1_settings");
            yield return AfterShot();
            for (int i = 0; i < 4 && g.Save.FrameCap != 60; i++) yield return ClickButton(m.FrameRateButton);
            for (int i = 0; i < 3 && g.Save.ButtonIcons != PadGlyphs.PlayStation; i++) yield return ClickButton(m.ButtonIconsButton);
            SaveCheck(!g.Save.ScreenShake && !g.Save.VSync && !g.Save.PauseInBackground && g.Save.FrameCap == 60 && g.Save.LargerText && TextScale.Larger
                      && g.Save.ButtonIcons == PadGlyphs.PlayStation && m.ButtonIconsButton.Label.text == "PLAYSTATION",
                      "clicked: screen shake, VSync and background pause off, 60 fps limit, larger text on, PlayStation button icons");
            yield return ClickButton(m.DefaultButton);   // DONE
            yield return new WaitForSecondsRealtime(0.3f);
            SaveCheck(File.Exists(SavePath) && File.Exists(SavePath + ".bak") == false, "DONE writes save.json");

            // delivery 1, delivered with its reference packing
            yield return RunLevel(1, "ref", false);
            SaveCheck(g.Save.IsDelivered(1) && g.Save.StarsFor(1) == 3 && File.Exists(SavePath + ".bak"), "delivery 1 recorded with three stars; the previous save kept as save.json.bak");

            // an unsealed box survives Main Menu and CONTINUE
            g.StartLevel(2);
            yield return new WaitForSecondsRealtime(0.5f);
            var pending = BottomUp(g.Level);
            bool placed = PlaceOne(g, pending);
            string one = SaveData.Serialize(g.CurrentPacking);
            g.ShowTitle();                                   // what Pause > MAIN MENU does
            yield return ContinueFromTitle(g);
            SaveCheck(placed && g.Phase == Phase.Packing && g.Level.Number == 2 && SaveData.Serialize(g.CurrentPacking) == one,
                      "Main Menu and CONTINUE bring back the unsealed box");

            // written to disk a couple of seconds after the last change
            placed = PlaceOne(g, pending);
            string two = SaveData.Serialize(g.CurrentPacking);
            yield return new WaitForSecondsRealtime(3f);
            var onDisk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            SaveCheck(placed && onDisk.Get(2)?.Packing == two, "the box is on disk a few seconds after the last change");

            Shot("S1_unsealed_box");
            yield return AfterShot();
            yield return ShotsWritten();

            // one more piece, then quit straight away (before the timed save): quitting keeps it
            placed = PlaceOne(g, pending);
            lastChange = Time.unscaledTime;
            string three = SaveData.Serialize(g.CurrentPacking);
            onDisk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            SaveCheck(placed && three != two && onDisk.Get(2)?.Packing == two, "a third piece placed just before quitting (not on disk yet)");
            File.WriteAllText(ExpectedFile("quit"), three);
            File.WriteAllText(ExpectedFile("backup"), two);
        }

        float lastChange = -1f;

        IEnumerator SaveStep2(Game g)
        {
            SaveCheck(SaveData.LastLoad == SaveData.LoadResult.Loaded && !g.Menus.SaveNoteShowing, "the save loads after a restart (no damaged-save note)");
            SaveCheck(g.Save.IsDelivered(1) && g.Save.StarsFor(1) == 3 && g.Save.IsUnlocked(2), "delivery 1 is still delivered with three stars");
            SaveCheck(!g.Save.ScreenShake && !g.Save.VSync && !g.Save.PauseInBackground && g.Save.FrameCap == 60 && g.Save.LargerText && g.Save.ButtonIcons == PadGlyphs.PlayStation, "the settings survive a restart");
            SaveCheck(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 60 && !g.Rig.ShakeEnabled && TextScale.Larger && PadGlyphs.Ps,
                      $"and are applied at launch (vSyncCount {QualitySettings.vSyncCount}, targetFrameRate {Application.targetFrameRate})");
            yield return ContinueFromTitle(g);
            string expected = File.Exists(ExpectedFile("quit")) ? File.ReadAllText(ExpectedFile("quit")) : null;
            SaveCheck(g.Level != null && g.Level.Number == 2 && SaveData.Serialize(g.CurrentPacking) == expected, "CONTINUE opens delivery 2 with the box as it was when the game quit");
            Shot("S2_box_after_restart");
            yield return AfterShot();
        }

        IEnumerator SaveStep3(Game g)
        {
            var corrupt = Directory.GetFiles(DataDir, "save.corrupt-*.json");
            SaveCheck(SaveData.LastLoad == SaveData.LoadResult.RecoveredFromBackup, "a save cut in half loads the backup");
            SaveCheck(corrupt.Length == 1 && new FileInfo(corrupt[0]).Length > 0, "the damaged save is kept aside, not deleted");
            SaveCheck(g.Save.IsDelivered(1) && g.Save.StarsFor(1) == 3 && g.Save.FrameCap == 60, "progress and settings come back from the backup");
            SaveCheck(g.Menus.SaveNoteShowing && g.Menus.SaveNoteText.Contains("backup"), "the title screen says the backup was used");
            Shot("S3_recovered_note");
            yield return AfterShot();
            yield return ContinueFromTitle(g);
            string expected = File.Exists(ExpectedFile("backup")) ? File.ReadAllText(ExpectedFile("backup")) : null;
            SaveCheck(g.Level != null && g.Level.Number == 2 && SaveData.Serialize(g.CurrentPacking) == expected, "the backup is the save from one write earlier (the box before the last piece)");
            g.ShowTitle();
            yield return new WaitForSecondsRealtime(0.8f);
            SaveCheck(!g.Menus.SaveNoteShowing, "the note shows once, not every time the title comes back");
        }

        IEnumerator SaveStep4(Game g)
        {
            var corrupt = Directory.GetFiles(DataDir, "save.corrupt-*.json");
            SaveCheck(SaveData.LastLoad == SaveData.LoadResult.Lost && g.Save.Records.Count == 0, "garbage and no backup: a fresh save, no exception");
            SaveCheck(corrupt.Length == 2, $"both damaged files are kept ({corrupt.Length})");
            SaveCheck(g.Menus.SaveNoteShowing && g.Menus.SaveNoteText.Contains("fresh start"), "the title screen says it is a fresh start");
            Shot("S4_lost_note");
            yield return AfterShot();
            g.Menus.ShowSettings(g.ShowTitle);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return ClickButton(g.Menus.DefaultButton);   // DONE
            yield return new WaitForSecondsRealtime(0.3f);
            SaveData back = null;
            try { back = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); } catch { }
            SaveCheck(back != null && back.Records.Count == 0, "the fresh save is written normally");

            // three stars on delivery 1: that packing becomes its best
            g.Save.SeenTips.Add("basics");
            g.Save.SeenTips.Add("shift_1");
            yield return RunLevel(1, "ref", false);
            string best = SaveData.Serialize(g.LastRun.Packing);
            SaveCheck(g.LastRun.Outcome.Stars == 3 && g.Save.Get(1)?.BestPacking == best && g.Save.Get(1)?.BestPackingStars == 3,
                      "a three-star trip is kept as the best packing");
            File.WriteAllText(ExpectedFile("best"), best);
        }

        IEnumerator SaveStep6(Game g)
        {
            var m = g.Menus;
            SaveCheck(g.Save.IsDelivered(1) && g.Save.Records.Count > 0, "progress to clear: delivery 1 is delivered");
            m.ShowSettings(g.ShowTitle);
            yield return new WaitForSecondsRealtime(0.6f);
            var grid = m.SettingToggle("grid");
            yield return ClickAt(RectScreen((RectTransform)grid.transform));   // a setting to keep: the packing grid off
            int records = g.Save.Records.Count;

            yield return ClickButton(m.StartOverButton);
            yield return new WaitForSecondsRealtime(0.4f);
            SaveCheck(m.StartOverAsking && m.ActiveScreen != null && m.ActiveScreen.name == "StartOver" && m.DefaultButton == m.StartOverKeepButton,
                      "START OVER asks first (the pad cursor would start on KEEP MY PROGRESS)");
            Shot("S6_start_over_confirm");
            yield return AfterShot();
            yield return ClickButton(m.StartOverKeepButton);
            yield return new WaitForSecondsRealtime(0.3f);
            SaveCheck(!m.StartOverAsking && g.Save.Records.Count == records && Directory.GetFiles(DataDir, "save.erased-*.json").Length == 0,
                      "KEEP MY PROGRESS changes nothing");

            yield return ClickButton(m.StartOverButton);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return ClickButton(m.StartOverConfirmButton);
            yield return new WaitForSecondsRealtime(0.8f);
            var erased = Directory.GetFiles(DataDir, "save.erased-*.json");
            SaveData old = null, now = null;
            try { old = JsonUtility.FromJson<SaveData>(File.ReadAllText(erased[0])); } catch { }
            try { now = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); } catch { }
            SaveCheck(g.Phase == Phase.Title && g.Save.Records.Count == 0 && g.Save.SeenTips.Count == 0 && g.Menus.ContinueLabel == "START SHIFT",
                      $"START OVER: a fresh game on the title screen ('{g.Menus.ContinueLabel}')");
            SaveCheck(now != null && now.Records.Count == 0 && !now.ShowGrid && !g.Save.ShowGrid, "on disk: no progress, and the settings are kept (the grid stays off)");
            SaveCheck(erased.Length == 1 && old != null && old.IsDelivered(1) && old.Get(1)?.BestPacking != null,
                      "the old save is kept as " + (erased.Length > 0 ? Path.GetFileName(erased[0]) : "(missing)"));
            Shot("S6_after_start_over");
            yield return AfterShot();
            File.WriteAllText(Path.Combine(dir, "v010-save.json"), V010Save());   // the script puts it in place for launch 7
        }

        /// <summary>
        /// A save as v0.1.0 wrote it (exactly its fields; Packing is the last box shipped): delivery 1
        /// shipped with its three-star reference, delivery 2 delivered once but last shipped items-only,
        /// delivery 3 tried and never delivered.
        /// </summary>
        static string V010Save()
        {
            string Rec(int num, int stars, bool delivered, int attempts, string packing) =>
                $"{{\"Number\":{num},\"Stars\":{stars},\"Delivered\":{(delivered ? "true" : "false")},\"UnderBudget\":{(stars >= 2 ? "true" : "false")},\"Careful\":{(stars >= 3 ? "true" : "false")}," +
                $"\"BestCost\":{(delivered ? 5 : -1)},\"BestCare\":{(delivered ? "0.33" : "-1.0")},\"Attempts\":{attempts},\"Packing\":\"{packing}\"}}";
            // and the rest of the story (4-20) shipped with their references: a whole v0.1.0 playthrough
            string more = "";
            for (int n = 4; n <= 20; n++) more += "," + Rec(n, 3, true, 1, SaveData.Serialize(Levels.Get(n).ReferencePacking()));
            return "{\"Version\":1,\"LastLevel\":3,\"MasterVolume\":0.9,\"MusicVolume\":0.7,\"SfxVolume\":0.9,\"ScreenShake\":true,\"ReducedMotion\":false," +
                   "\"Fullscreen\":false,\"ShowGrid\":true,\"HighQuality\":true,\"Tape\":\"kraft\",\"SeenTips\":[\"basics\",\"shift_1\"],\"Records\":[" +
                   Rec(1, 3, true, 2, SaveData.Serialize(Levels.Get(1).ReferencePacking())) + "," +
                   Rec(2, 1, true, 3, SaveData.Serialize(NaivePacking(Levels.Get(2)))) + "," +
                   Rec(3, 0, false, 1, SaveData.Serialize(Levels.Get(3).ReferencePacking())) + more + "]}";
        }

        IEnumerator SaveStep7(Game g)
        {
            var r1 = g.Save.Get(1); var r2 = g.Save.Get(2); var r3 = g.Save.Get(3);
            var lv1 = Levels.Get(1);
            string ref1 = SaveData.Serialize(lv1.ReferencePacking());
            var sim1 = Simulator.Run(lv1, lv1.ReferencePacking(), false);
            var sim2 = Simulator.Run(Levels.Get(2), NaivePacking(Levels.Get(2)), false);
            SaveCheck(SaveData.LastLoad == SaveData.LoadResult.Loaded && g.Save.Version == SaveData.CurrentVersion && r1 != null && r1.Stars == 3 && r1.Attempts == 2,
                      "a v0.1.0 save loads, keeps its progress, and is upgraded to version 2");
            SaveCheck(r1.CheckLastBox && r2.CheckLastBox && !r3.CheckLastBox && string.IsNullOrEmpty(r1.BestPacking),
                      "the delivered deliveries are marked to check when their bench opens (nothing simulated at launch)");
            yield return new WaitForSecondsRealtime(0.9f);
            // each bench, opened the way the delivery log does it
            float t0 = Time.realtimeSinceStartup;
            g.StartLevel(2);
            float ms2 = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return new WaitForSecondsRealtime(0.4f);
            SaveCheck(!sim2.Outcome.Delivered && string.IsNullOrEmpty(r2.BestPacking) && !r2.CheckLastBox && r2.Stars == 1,
                      "delivery 2: a last box that wouldn't deliver is not kept (the star stays)");
            g.StartLevel(3);
            yield return new WaitForSecondsRealtime(0.4f);
            SaveCheck(string.IsNullOrEmpty(r3.BestPacking), "delivery 3: never delivered, so no best packing");
            int rest = 0; float worst = 0f;
            for (int n = 4; n <= 20; n++)
            {
                t0 = Time.realtimeSinceStartup;
                g.StartLevel(n);
                worst = Mathf.Max(worst, (Time.realtimeSinceStartup - t0) * 1000f);
                yield return null;
                var r = g.Save.Get(n);
                if (r != null && r.BestPacking == r.Packing && r.BestPackingStars == 3 && !r.CheckLastBox) rest++;
            }
            t0 = Time.realtimeSinceStartup;
            g.StartLevel(18);                                 // already checked: the bench alone, for comparison
            float plain = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return null;
            Debug.Log($"[AutoPilot] save 7: opening a bench that checks its last box took {ms2:0} ms (delivery 2), at most {worst:0} ms (4-20); without the check {plain:0} ms (18)");
            SaveCheck(rest == 17, $"deliveries 4-20: {rest} of 17 three-star boxes kept as best packings when their bench opens");
            t0 = Time.realtimeSinceStartup;
            g.StartLevel(1);
            float ms1 = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return new WaitForSecondsRealtime(0.6f);
            SaveCheck(r1.BestPacking == ref1 && r1.BestPackingStars == sim1.Outcome.Stars && r1.BestPackingCost == sim1.Outcome.Cost && sim1.Outcome.Stars == 3,
                      $"delivery 1: its last shipped box becomes the best packing with the simulation's result ({r1.BestPackingStars} stars, cost {r1.BestPackingCost}; bench opened in {ms1:0} ms)");
            var bestBtn = g.Hud.BestButton;
            SaveCheck(SaveData.Serialize(g.CurrentPacking) == ref1 && !bestBtn.gameObject.activeInHierarchy, "the bench shows the last box; MY BEST waits until the box changes");
            yield return ClickButton(g.Hud.ClearButton);
            yield return new WaitForSecondsRealtime(0.4f);
            int gold = 0;
            foreach (var img in bestBtn.GetComponentsInChildren<UnityEngine.UI.Image>()) if (img.name.StartsWith("star") && img.color == HWC.Visuals.Palette.Gold) gold++;
            SaveCheck(g.CurrentPacking.Pieces.Count == 0 && bestBtn.gameObject.activeInHierarchy && gold == 3, $"after EMPTY BOX, MY BEST shows with {gold} gold stars");
            Shot("S7_v010_my_best");
            yield return AfterShot();
            yield return ClickButton(bestBtn);
            yield return new WaitForSecondsRealtime(0.4f);
            SaveCheck(SaveData.Serialize(g.CurrentPacking) == ref1, "MY BEST puts the v0.1.0 box back");
            yield return Key(UnityEngine.InputSystem.Key.Space);
            yield return WaitPhase(Phase.Journey, 8f);
            g.Journey.Skip();
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 5f);
            SaveCheck(g.Phase == Phase.Results && g.LastRun.Outcome.Stars == 3 && g.LastRun.Hash == sim1.Hash,
                      $"shipped: {g.LastRun.Outcome.Stars} stars, hash {(g.LastRun.Hash == sim1.Hash ? "matches" : "DIFFERS")} the simulation's");
            var onDisk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            SaveCheck(onDisk.Version == SaveData.CurrentVersion && onDisk.Get(1)?.BestPacking == ref1, "on disk: version 2, with the best packing");
        }

        IEnumerator SaveStep5(Game g)
        {
            string best = File.Exists(ExpectedFile("best")) ? File.ReadAllText(ExpectedFile("best")) : null;
            SaveCheck(best != null && g.Save.Get(1)?.BestPacking == best, "the best packing survives a restart");
            yield return new WaitForSecondsRealtime(0.9f);
            g.StartLevel(1);                                  // what picking it in the delivery log does
            yield return new WaitForSecondsRealtime(0.6f);
            var bestBtn = g.Hud.BestButton;
            SaveCheck(g.Level?.Number == 1 && !bestBtn.gameObject.activeInHierarchy, "MY BEST is hidden while the box already holds the best packing");

            // a failed trip: items only
            yield return RunLevel(1, "naive", false);
            string failed = SaveData.Serialize(g.LastRun.Packing);
            SaveCheck(!g.LastRun.Outcome.Delivered && g.Save.Get(1)?.BestPacking == best, "a failed trip doesn't replace the best packing");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.R);
            yield return new WaitForSecondsRealtime(0.6f);
            SaveCheck(g.Phase == Phase.Packing && SaveData.Serialize(g.CurrentPacking) == failed, "R repacks: the failed box is on the bench");
            int gold = 0;
            foreach (var img in bestBtn.GetComponentsInChildren<UnityEngine.UI.Image>()) if (img.name.StartsWith("star") && img.color == HWC.Visuals.Palette.Gold) gold++;
            SaveCheck(bestBtn.gameObject.activeInHierarchy && gold == 3, $"MY BEST shows, with {gold} gold stars");
            Shot("S5_my_best_button");
            yield return AfterShot();

            yield return ClickButton(bestBtn);
            yield return new WaitForSecondsRealtime(0.4f);
            SaveCheck(SaveData.Serialize(g.CurrentPacking) == best && !bestBtn.gameObject.activeInHierarchy, "clicking MY BEST puts the three-star packing in the box");
            Shot("S5_my_best_loaded");
            yield return AfterShot();
            yield return Key(UnityEngine.InputSystem.Key.Z);
            SaveCheck(SaveData.Serialize(g.CurrentPacking) == failed, "Z undoes it");
            yield return Key(UnityEngine.InputSystem.Key.Y);
            SaveCheck(SaveData.Serialize(g.CurrentPacking) == best, "Y redoes it");

            // ship it again: three stars, and the same trip as the validator's
            var expected = Simulator.Run(g.Level, SaveData.Deserialize(g.Level, best), false);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            SaveCheck(g.Phase == Phase.Sealing, "Space seals it");
            yield return WaitPhase(Phase.Journey, 8f);
            g.Journey.Skip();
            yield return WaitPhase(Phase.Reveal, 3f);
            yield return Key(UnityEngine.InputSystem.Key.Enter);
            yield return WaitPhase(Phase.Results, 5f);
            SaveCheck(g.Phase == Phase.Results && g.LastRun.Outcome.Stars == 3 && g.LastRun.Hash == expected.Hash,
                      $"shipped from MY BEST: {g.LastRun.Outcome.Stars} stars, hash {(g.LastRun.Hash == expected.Hash ? "matches" : "DIFFERS")}");
        }
    }
}
