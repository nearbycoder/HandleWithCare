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
        }

        public int Version = 1;
        public int LastLevel = 1;
        public float MasterVolume = 0.9f;
        public float MusicVolume = 0.7f;
        public float SfxVolume = 0.9f;
        public bool ScreenShake = true;
        public bool ReducedMotion;
        public bool Fullscreen = true;
        public bool ShowGrid = true;
        public bool HighQuality = true;
        public string Tape = "kraft";
        public List<string> SeenTips = new List<string>();
        public List<LevelRecord> Records = new List<LevelRecord>();

        static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, "save.json");
        public static bool Disabled;

        public static SaveData Load()
        {
            try
            {
                if (!Disabled && File.Exists(PathOnDisk))
                {
                    var s = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathOnDisk));
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Save] could not load: " + e.Message); }
            return new SaveData();
        }

        public void Write()
        {
            if (Disabled) return;
            try { File.WriteAllText(PathOnDisk, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning("[Save] could not write: " + e.Message); }
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

        public void Record(LevelDef lv, Outcome o)
        {
            var r = Get(lv.Number, true);
            r.Attempts++;
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
                if (r.BestCare < 0 || o.WorstCare < r.BestCare) r.BestCare = o.WorstCare;
            }
            Write();
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
