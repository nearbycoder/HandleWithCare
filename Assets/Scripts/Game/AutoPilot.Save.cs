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
    ///   4  (garbage, no backup) a fresh save, both damaged files kept, and it saves again
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
            foreach (var key in new[] { "shake", "vsync", "bgpause" })
            {
                var t = m.SettingToggle(key);
                yield return ClickAt(RectScreen((RectTransform)t.transform));
                Debug.Log($"[AutoPilot] save 1: clicked {key} at {RectScreen((RectTransform)t.transform)}: now {t.isOn}");
            }
            Shot("S1_settings");
            yield return AfterShot();
            for (int i = 0; i < 4 && g.Save.FrameCap != 60; i++) yield return ClickButton(m.FrameRateButton);
            SaveCheck(!g.Save.ScreenShake && !g.Save.VSync && !g.Save.PauseInBackground && g.Save.FrameCap == 60, "clicked: screen shake, VSync and background pause off, 60 fps limit");
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
            SaveCheck(!g.Save.ScreenShake && !g.Save.VSync && !g.Save.PauseInBackground && g.Save.FrameCap == 60, "the settings survive a restart");
            SaveCheck(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 60 && !g.Rig.ShakeEnabled,
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
        }
    }
}
