using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HWC.Sim;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>Progress, best results, last packing per delivery and settings (JSON in persistentDataPath).</summary>
    [Serializable]
    public sealed class SaveData
    {
        [Serializable]
        public sealed class LevelRecord
        {
            public int Number;
            public int Stars;
            public bool Delivered;
            public bool UnderBudget;
            public bool Careful;
            public int BestCost = -1;
            public float BestCare = -1;
            public int Attempts;
            public string Packing;
            public int HintStage;          // Ask Mabel: how many hints have been shown (0 = none)
            public int HintFocus = -1;     // the item the hints are about (PieceKind), fixed when first asked
            public bool HintsHidden;
            public bool Expert;            // three stars at or under Mabel's best
            // the best packing shipped (more stars, then cheaper, then gentler), apart from the box on the bench
            public string BestPacking;
            public int BestPackingStars;
            public int BestPackingCost = -1;
            public float BestPackingCare = -1;
            public bool CheckLastBox;      // from a v0.1.0 save: LastShipped is not yet checked as a best packing
            // the last box shipped, simulated again when the bench opens so its trip report and trails come back
            public string LastShipped;
            public string LastShippedHash; // the trip's hash when it was shipped (hex)
        }

        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;  // 1: v0.1.0 (and builds before round 5), where Packing is the last box shipped
        public int LastLevel = 1;
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.7f;
        public float SfxVolume = 0.9f;
        public bool ScreenShake = true;
        public bool ReducedMotion;
        public bool Fullscreen = true;
        public bool ShowGrid = true;
        public bool HighQuality = true;       // before round 12; still written (HIGH or ULTRA) for older builds
        public int Fidelity = WebPlatform.IsWeb ? (WebPlatform.Mobile ? 0 : 1) : -1;   // GRAPHICS FIDELITY, LOW 0 to ULTRA 3; -1: from HighQuality (on HIGH, off MEDIUM). MEDIUM in a browser, LOW on a phone or tablet
        public bool VSync = true;
        public int FrameCap = 120;            // frames per second when VSync is off; 0 = unlimited
        public int WindowW, WindowH;          // windowed size; 0 = leave the window as launched
        public bool PauseInBackground = true; // pause (and muffle) when the window loses focus
        public bool LargerText;               // small text grows where it has room
        public int ButtonIcons;               // gamepad prompts: 0 auto (from the pad), 1 Xbox, 2 PlayStation
        public string Tape = "kraft";
        public List<string> SeenTips = new List<string>();
        public List<LevelRecord> Records = new List<LevelRecord>();

        public int FidelityLevel => Fidelity >= 0 ? Mathf.Clamp(Fidelity, 0, 3) : (HighQuality ? 2 : 1);

        public void SetFidelity(int level)
        {
            Fidelity = Mathf.Clamp(level, 0, 3);
            HighQuality = Fidelity >= 2;
        }

        static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, "save.json");
        static string BackupPath => PathOnDisk + ".bak";
        public static bool Disabled;
        public static bool LogWrites;   // the save test logs who wrote

        /// <summary>What the last Load had to do about a damaged save (for a line on the title screen).</summary>
        public enum LoadResult { Fresh, Loaded, RecoveredFromBackup, Lost }
        public static LoadResult LastLoad { get; private set; } = LoadResult.Fresh;

        /// <summary>
        /// Reads save.json. A file that can't be read is moved aside (never deleted) and the backup from
        /// the write before it is used instead.
        /// </summary>
        public static SaveData Load()
        {
            LastLoad = LoadResult.Fresh;
            if (Disabled) return new SaveData();
            var s = TryRead(PathOnDisk, out bool present);
            if (s != null) { LastLoad = LoadResult.Loaded; s.AdoptOldBests(); s.MarkLastShipped(); return s; }
            if (!present && !File.Exists(BackupPath)) return new SaveData();
            if (present)
            {
                try
                {
                    string aside = Path.Combine(Application.persistentDataPath, $"save.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Move(PathOnDisk, aside);
                    Debug.LogWarning("[Save] save.json could not be read; kept it as " + Path.GetFileName(aside));
                }
                catch (Exception e) { Debug.LogWarning("[Save] could not move the damaged save aside: " + e.Message); }
            }
            var b = TryRead(BackupPath, out _);
            if (b != null) { LastLoad = LoadResult.RecoveredFromBackup; Debug.LogWarning("[Save] recovered from the backup"); b.AdoptOldBests(); b.MarkLastShipped(); return b; }
            LastLoad = LoadResult.Lost;
            Debug.LogWarning("[Save] no usable backup: starting a new save");
            return new SaveData();
        }

        static SaveData TryRead(string path, out bool present)
        {
            present = File.Exists(path);
            if (!present) return null;
            try
            {
                string text = File.ReadAllText(path);
                if (!Complete(text)) { Debug.LogWarning($"[Save] {Path.GetFileName(path)} is cut short"); return null; }
                var s = JsonUtility.FromJson<SaveData>(text);
                if (s == null || s.Records == null || s.SeenTips == null) return null;
                return s;
            }
            catch (Exception e) { Debug.LogWarning($"[Save] could not read {Path.GetFileName(path)}: {e.Message}"); return null; }
        }

        /// <summary>One whole JSON object: braces and brackets balance (outside strings) and nothing follows.
        /// Don't rely on JsonUtility to reject a file that was cut off mid-write.</summary>
        static bool Complete(string text)
        {
            int depth = 0; bool inString = false, esc = false, opened = false;
            foreach (char c in text)
            {
                if (inString)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (depth == 0 && opened && !char.IsWhiteSpace(c)) return false;
                if (c == '"') inString = true;
                else if (c == '{' || c == '[') { depth++; opened = true; }
                else if (c == '}' || c == ']') { if (--depth < 0) return false; }
                else if (depth == 0 && !char.IsWhiteSpace(c)) return false;
            }
            return opened && depth == 0 && !inString;
        }

        /// <summary>
        /// Writes to a temporary file, then swaps it in with a rename, so a crash or power cut mid-write
        /// leaves either the old save or the new one. The previous save stays as save.json.bak.
        /// </summary>
        public void Write()
        {
            if (Disabled) return;
            string tmp = PathOnDisk + ".tmp";
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(this, true));
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    f.Write(bytes, 0, bytes.Length);
                    f.Flush(true);
                }
                if (File.Exists(PathOnDisk) && WebPlatform.IsWeb)
                {
                    // the browser's file system has no File.Replace; the page persists the frame's changes together
                    File.Copy(PathOnDisk, BackupPath, true);
                    File.Delete(PathOnDisk);
                    File.Move(tmp, PathOnDisk);
                }
                else if (File.Exists(PathOnDisk)) File.Replace(tmp, PathOnDisk, BackupPath);
                else File.Move(tmp, PathOnDisk);
                if (LogWrites)
                {
                    var st = new System.Diagnostics.StackTrace(1, false);
                    var who = new List<string>();
                    for (int i = 0; i < Math.Min(4, st.FrameCount); i++) who.Add(st.GetFrame(i).GetMethod()?.Name);
                    Debug.Log($"[Save] wrote {bytes.Length} bytes from {string.Join(" < ", who)}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] could not write: " + e.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public LevelRecord Get(int number, bool create = false)
        {
            foreach (var r in Records) if (r.Number == number) return r;
            if (!create) return null;
            var nr = new LevelRecord { Number = number };
            Records.Add(nr);
            return nr;
        }

        public int StarsFor(int number) => Get(number)?.Stars ?? 0;
        public bool IsDelivered(int number) => Get(number)?.Delivered ?? false;
        public int HintStage(int number) => Get(number)?.HintStage ?? 0;
        public bool IsExpert(int number) => Get(number)?.Expert ?? false;

        /// <summary>Hints unlock after a trip that missed a star (and stay once used).</summary>
        public bool HintsAvailable(int number)
        {
            var r = Get(number);
            return r != null && (r.HintStage > 0 || (r.Attempts > 0 && r.Stars < 3));
        }

        public int TotalStars
        {
            get { int t = 0; foreach (var r in Records) t += r.Stars; return t; }
        }

        public bool IsUnlocked(int number)
        {
            if (number <= 1) return true;
            return IsDelivered(number - 1);
        }

        public void Record(LevelDef lv, Outcome o, Packing shipped = null, ulong hash = 0)
        {
            var r = Get(lv.Number, true);
            r.Attempts++;
            if (shipped != null)
            {
                r.LastShipped = Serialize(shipped);
                r.LastShippedHash = hash.ToString("x16");
                r.CheckLastBox = false;   // a new trip: the old box from v0.1.0 is no longer the last one
            }
            if (o.Delivered)
            {
                r.Delivered = true;
                r.UnderBudget |= o.UnderBudget;
                r.Careful |= o.Careful;
                r.Stars = Math.Max(r.Stars, o.Stars);
                // stars can be earned across attempts
                int s = 1 + (r.UnderBudget ? 1 : 0) + (r.Careful ? 1 : 0);
                r.Stars = Math.Max(r.Stars, Math.Min(s, 3));
                if (r.BestCost < 0 || o.Cost < r.BestCost) r.BestCost = o.Cost;
                if (o.Stars == 3 && o.Cost <= lv.Expert) r.Expert = true;
                if (r.BestCare < 0 || o.WorstCare < r.BestCare) r.BestCare = o.WorstCare;
                if (shipped != null && BetterThanBest(r, o))
                {
                    r.BestPacking = Serialize(shipped);
                    r.BestPackingStars = o.Stars;
                    r.BestPackingCost = o.Cost;
                    r.BestPackingCare = o.WorstCare;
                }
            }
            Write();
        }

        /// <summary>
        /// Saves from v0.1.0 keep one packing per delivery: the last one shipped. Each delivered delivery
        /// without a best packing is marked, and its box is checked the first time its bench opens
        /// (<see cref="AdoptLastBox"/>). Runs once: the save version goes to 2.
        /// </summary>
        public int AdoptOldBests()
        {
            if (Version >= CurrentVersion) return 0;
            int marked = 0;
            foreach (var r in Records)
            {
                if (string.IsNullOrEmpty(r.Packing)) continue;
                if (r.Attempts > 0) r.LastShipped = r.Packing;   // v0.1.0 stored a box only when it was sealed
                if (r.Delivered && string.IsNullOrEmpty(r.BestPacking)) { r.CheckLastBox = true; marked++; }
            }
            Debug.Log($"[Save] upgraded a version {Version} save: {marked} last box(es) to check as best packings");
            Version = CurrentVersion;
            return marked;
        }

        /// <summary>
        /// Saves from before round 6 don't name the last box shipped. In a v0.1.0 save (and the boxes still
        /// marked for checking) the stored box is the last one shipped, so it becomes LastShipped.
        /// </summary>
        void MarkLastShipped()
        {
            foreach (var r in Records)
                if (string.IsNullOrEmpty(r.LastShipped) && !string.IsNullOrEmpty(r.Packing) && r.CheckLastBox) r.LastShipped = r.Packing;
        }

        /// <summary>
        /// The last box shipped for this delivery, to simulate again when its bench opens (the trip report,
        /// the trails and Ask Mabel's focus come from it). Null if there is none or it isn't legal any more.
        /// </summary>
        public Packing LastShippedPacking(LevelDef lv)
        {
            var r = Get(lv.Number);
            if (r == null || string.IsNullOrEmpty(r.LastShipped)) return null;
            try
            {
                var pk = Deserialize(lv, r.LastShipped);
                string bad = pk.Validate(lv);
                if (bad == null && !pk.AllItemsPlaced(lv)) bad = "items missing";
                if (bad == null) return pk;
                Debug.Log($"[Save] delivery {r.Number}: the last box shipped isn't legal now ({bad})");
            }
            catch (Exception e) { Debug.LogWarning($"[Save] delivery {r.Number}: could not read the last box shipped: {e.Message}"); }
            return null;
        }

        /// <summary>
        /// A delivery from a v0.1.0 save, opened for the first time: its last shipped box has been simulated
        /// again (on a worker thread, see Game.StartLevel) and is kept as the best packing, with the stars,
        /// cost and care it really gets, if it is still legal and still delivers. o is null when it isn't legal.
        /// </summary>
        public bool AdoptLastBox(LevelDef lv, Outcome o, double ms = 0)
        {
            var r = Get(lv.Number);
            if (r == null || !r.CheckLastBox) return false;
            r.CheckLastBox = false;
            if (!string.IsNullOrEmpty(r.BestPacking) || string.IsNullOrEmpty(r.LastShipped)) return false;
            if (o == null) { Debug.Log($"[Save] delivery {r.Number}: the last box from v0.1.0 isn't legal now; no best packing"); return false; }
            if (!o.Delivered) { Debug.Log($"[Save] delivery {r.Number}: the last box from v0.1.0 doesn't deliver; no best packing"); return false; }
            r.BestPacking = r.LastShipped;
            r.BestPackingStars = o.Stars;
            r.BestPackingCost = o.Cost;
            r.BestPackingCare = o.WorstCare;
            Debug.Log($"[Save] delivery {r.Number}: the last box from v0.1.0 is the best packing ({o.Stars} stars, cost {o.Cost}; simulated in {ms:0} ms off the main thread)");
            return true;
        }

        /// <summary>More stars, then a lower cost, then a lower peak jolt.</summary>
        static bool BetterThanBest(LevelRecord r, Outcome o)
        {
            if (string.IsNullOrEmpty(r.BestPacking)) return true;
            if (o.Stars != r.BestPackingStars) return o.Stars > r.BestPackingStars;
            if (o.Cost != r.BestPackingCost) return o.Cost < r.BestPackingCost;
            return o.WorstCare < r.BestPackingCare;
        }

        /// <summary>The best packing shipped for this delivery, or null.</summary>
        public Packing GetBestPacking(LevelDef lv)
        {
            var r = Get(lv.Number);
            if (r == null || string.IsNullOrEmpty(r.BestPacking)) return null;
            try { return Deserialize(lv, r.BestPacking); }
            catch { return null; }
        }

        /// <summary>
        /// Clears progress (records, boxes, seen notes) and keeps the settings. The save from before is
        /// copied to save.erased-[time].json first; if that copy can't be made, nothing is cleared.
        /// </summary>
        public bool StartOver()
        {
            if (!Disabled && File.Exists(PathOnDisk))
            {
                try
                {
                    string copy = Path.Combine(Application.persistentDataPath, $"save.erased-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Copy(PathOnDisk, copy, false);
                    Debug.Log("[Save] starting over; the old save is kept as " + Path.GetFileName(copy));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Save] could not copy the save before starting over, so nothing was cleared: " + e.Message);
                    return false;
                }
            }
            Records.Clear();
            SeenTips.Clear();
            LastLevel = 1;
            Tape = "kraft";   // the other tapes are earned with stars
            Write();
            return true;
        }

        public Packing GetPacking(LevelDef lv)
        {
            var r = Get(lv.Number);
            if (r == null || string.IsNullOrEmpty(r.Packing)) return null;
            try
            {
                var pk = Deserialize(lv, r.Packing);
                return pk.Validate(lv) == null || !pk.AllItemsPlaced(lv) ? pk : pk;
            }
            catch { return null; }
        }

        public void SetPacking(LevelDef lv, Packing pk)
        {
            Get(lv.Number, true).Packing = Serialize(pk);
            Write();
        }

        // compact text: "P kind x y rot facing strap;...|D 1,2|S row@col,..."
        public static string Serialize(Packing pk)
        {
            var sb = new StringBuilder();
            foreach (var p in pk.Pieces)
                sb.Append($"{(int)p.Kind},{p.X},{p.Y},{(p.Rotated ? 1 : 0)},{p.Facing},{(p.Strapped ? 1 : 0)};");
            sb.Append('|');
            sb.Append(string.Join(",", pk.Dividers));
            sb.Append('|');
            for (int i = 0; i < pk.Shelves.Count; i++)
                sb.Append((i > 0 ? "," : "") + pk.Shelves[i].Row + "@" + pk.Shelves[i].Col);
            return sb.ToString();
        }

        public static Packing Deserialize(LevelDef lv, string s)
        {
            var pk = new Packing(lv.W, lv.H);
            var parts = s.Split('|');
            foreach (var t in parts[0].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var v = t.Split(',');
                pk.Pieces.Add(new Placement((PieceKind)int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]), v[3] == "1", int.Parse(v[4]), v[5] == "1"));
            }
            if (parts.Length > 1)
                foreach (var d in parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) pk.Dividers.Add(int.Parse(d));
            if (parts.Length > 2)
                foreach (var sh in parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var b = sh.Split('@');
                    pk.Shelves.Add(new ShelfSpec(int.Parse(b[0]), int.Parse(b[1])));
                }
            return pk;
        }
    }
}
