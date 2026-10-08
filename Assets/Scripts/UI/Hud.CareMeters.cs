using System.Collections.Generic;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HWC.Gameplay
{
    /// <summary>
    /// The care meters on the trip: one row per item, with a bar for the worst knock so far as a share of
    /// its limit and the 65% line of the "handled with care" star. They read the recorded frames, so they
    /// end exactly where the review does, and rewind with the replay. In a replay each row is a button that
    /// jumps to just before its item's moment.
    /// </summary>
    public sealed partial class Hud
    {
        const float MeterW = 360f, MeterRowH = 46f, MeterTop = 44f, BarW = 120f;
        static readonly Color Amber = new Color(0.96f, 0.66f, 0.2f);
        static readonly Color BadBright = new Color(1f, 0.45f, 0.38f);   // Palette.Bad is too dark on the meters' dark panel

        sealed class MeterRow
        {
            public int Body;
            public PieceKind Kind;
            public bool HasLimit;
            public RectTransform Fill;
            public Image FillImage, Track;
            public TextMeshProUGUI Value;
            public float Shown = -1f;
            public ItemStatus Status = (ItemStatus)(-1);
            public UiButton Jump;
        }

        RectTransform careRoot;
        Recording meterRec;
        int meterFrameForTest = -1;   // the trip being shown (the journey player gets it just after the HUD)
        readonly List<MeterRow> meterRows = new List<MeterRow>();

        /// <summary>Items that can be judged on care: anything with a jolt, crush or wake limit, or that can topple.</summary>
        public static bool HasCareLimit(PieceDef d) =>
            (d.JoltLimit > 0 && d.Kind != PieceKind.Bubble) || d.CrushLimit > 0 || d.WakeLimit > 0 || d.Has(Quirk.Topples);

        void BuildCareMeters(LevelDef lv, Recording rec, bool replay)
        {
            if (careRoot != null) Destroy(careRoot.gameObject);
            meterRows.Clear();
            meterRec = rec;
            var panel = Ui.Panel(journeyRoot, "careMeters", new Color(0.1f, 0.08f, 0.07f, 0.72f), Ui.Rounded(14));
            careRoot = panel.rectTransform;
            panel.raycastTarget = false;
            var head = Ui.Text(careRoot, "head", "HANDLE WITH CARE", 20, Palette.Sticky, Ui.Display, TextAlignmentOptions.Left);
            head.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -8), new Vector2(200, 28));
            var line = Ui.Text(careRoot, "line", "keep under 65%", 16, new Color(1f, 0.95f, 0.85f, 0.75f), Ui.Bold, TextAlignmentOptions.Right);
            line.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -10), new Vector2(140, 26));
            int n = 0;
            for (int b = 0; b < rec.Bodies.Length; b++)
            {
                var info = rec.Bodies[b];
                if (info.Type != BodyType.Piece) continue;
                var def = Catalog.Get(info.Kind);
                if (def.IsPadding) continue;
                var row = new MeterRow { Body = b, Kind = info.Kind, HasLimit = HasCareLimit(def) };
                var r = Ui.Rect("row_" + info.Kind, careRoot);
                r.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -MeterTop - n * MeterRowH), new Vector2(MeterW - 20, MeterRowH));
                if (replay && row.HasLimit)
                {
                    // the whole row is the button (an item with no limit has no moment to show): a faint plate that shows under the pointer
                    var plate = r.gameObject.AddComponent<Image>();
                    plate.sprite = Ui.Rounded(10);
                    plate.type = Image.Type.Sliced;
                    plate.color = new Color(1f, 1f, 1f, 0.07f);
                    row.Jump = r.gameObject.AddComponent<UiButton>();
                    row.Jump.Init(plate, () => JumpToItem(row.Body));
                    row.Jump.HoverScale = 1.04f;
                }
                var icon = Ui.Icon(r, "icon", IconStudio.Piece(info.Kind), Color.white);
                icon.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(42, 42));
                icon.raycastTarget = false;
                var name = Ui.Text(r, "name", Cap(Hints.Short(info.Kind)), 19, Palette.Cream, Ui.Bold, TextAlignmentOptions.Left);
                name.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(110, 30));
                name.enableAutoSizing = true; name.fontSizeMin = 13; name.fontSizeMax = 19;
                name.textWrappingMode = TextWrappingModes.NoWrap;
                var track = Ui.Panel(r, "track", new Color(1f, 1f, 1f, 0.16f), Ui.Rounded(6));
                track.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(162, 0), new Vector2(BarW, 14));
                track.raycastTarget = false;
                row.Track = track;
                var fill = Ui.Panel(track.transform, "fill", Palette.Good, Ui.Rounded(6));
                fill.raycastTarget = false;
                row.FillImage = fill;
                row.Fill = fill.rectTransform;
                row.Fill.anchorMin = Vector2.zero;
                row.Fill.anchorMax = new Vector2(0, 1);
                row.Fill.offsetMin = row.Fill.offsetMax = Vector2.zero;
                var tick = Ui.Panel(track.transform, "line65", Palette.Cream, null);
                tick.raycastTarget = false;
                tick.rectTransform.anchorMin = new Vector2(SimConst.CareFraction, -0.35f);
                tick.rectTransform.anchorMax = new Vector2(SimConst.CareFraction, 1.35f);
                tick.rectTransform.sizeDelta = new Vector2(3, 0);
                track.gameObject.SetActive(row.HasLimit);
                row.Value = Ui.Text(r, "value", "", 22, Palette.Cream, Ui.Display, TextAlignmentOptions.Right);
                row.Value.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(row.HasLimit ? 52 : 170, 30));
                row.Value.enableAutoSizing = true; row.Value.fontSizeMin = 14; row.Value.fontSizeMax = 22;
                row.Value.textWrappingMode = TextWrappingModes.NoWrap;
                meterRows.Add(row);
                n++;
            }
            float foot = 0f;
            if (replay && n > 0)
            {
                var tip = Ui.Text(careRoot, "tip", "click an item to see its moment", 16, new Color(1f, 0.95f, 0.85f, 0.75f), Ui.Bold, TextAlignmentOptions.Center);
                tip.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(MeterW - 20, 24));
                tip.enableAutoSizing = true; tip.fontSizeMin = 12; tip.fontSizeMax = 16;
                foot = 26f;
            }
            careRoot.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(MeterW, MeterTop + n * MeterRowH + 8 + foot));
            careRoot.gameObject.SetActive(n > 0 && !Cinematic);
            UpdateCareMeters();
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Where a meter row's button jumps: its item's first trouble (failure or near miss), else its
        /// worst knock, else the start.</summary>
        public static float ItemMomentTime(Recording rec, int body)
        {
            var first = Troubles.FirstOf(rec, body);
            if (first.HasValue) return first.Value.Time;
            var it = rec.Outcome.Items.Find(i => i.Body == body);
            return it != null && it.PeakTick >= 0 && it.Care > 0f ? it.PeakTick * SimConst.Dt : 0f;
        }

        void JumpToItem(int body)
        {
            var j = G.Journey;
            if (!j.IsReplay || j.Rec != meterRec) return;
            float at = ItemMomentTime(meterRec, body);
            j.Seek(Mathf.Max(0f, at - JourneyPlayer.TroubleLead));
            j.UserPaused = false;
        }

        /// <summary>The recorded frame the journey is showing now.</summary>
        int MeterFrame()
        {
            if (meterFrameForTest >= 0) return Mathf.Min(meterFrameForTest, meterRec.Frames.Count - 1);
            if (G.Journey.Rec != meterRec) return 0;
            return Mathf.Clamp((int)(G.Journey.T / (SimConst.FrameEvery * SimConst.Dt)), 0, meterRec.Frames.Count - 1);
        }

        void UpdateCareMeters()
        {
            var rec = meterRec;
            if (careRoot == null || rec == null || meterRows.Count == 0) return;
            var frame = rec.Frames[MeterFrame()];
            foreach (var row in meterRows)
            {
                var fr = frame[row.Body];
                float care = fr.Care;
                var status = Simulator.StatusOf(fr.State, care);
                bool failed = status != ItemStatus.Perfect && status != ItemStatus.Fine;
                if (Mathf.Approximately(care, row.Shown) && status == row.Status) continue;
                bool jumped = row.Shown >= 0f && care > row.Shown + 0.02f;
                row.Shown = care; row.Status = status;
                row.Fill.anchorMax = new Vector2(Mathf.Clamp(care, 0.02f, 1f), 1);
                row.FillImage.color = failed ? BadBright : (care >= SimConst.CareFraction ? Amber : Palette.Good);
                if (failed)
                {
                    row.Value.text = row.Kind == PieceKind.Dragon && status == ItemStatus.Scorched ? "BOX ON FIRE" : StatusWord(status);
                    row.Value.color = BadBright;
                    // the word needs more room than a percentage: it takes the bar's place
                    row.Track.gameObject.SetActive(false);
                    row.Value.rectTransform.sizeDelta = new Vector2(170, 30);
                }
                else
                {
                    row.Track.gameObject.SetActive(row.HasLimit);
                    row.Value.rectTransform.sizeDelta = new Vector2(row.HasLimit ? 52 : 170, 30);
                    row.Value.text = row.HasLimit ? $"{care * 100f:0}%" : "no limit";
                    row.Value.color = !row.HasLimit ? new Color(1f, 0.95f, 0.85f, 0.55f) : (care >= SimConst.CareFraction ? Amber : Palette.Cream);
                }
                if (jumped) row.Value.transform.localScale = Vector3.one * 1.35f;
            }
            foreach (var row in meterRows)
                row.Value.transform.localScale = Vector3.Lerp(row.Value.transform.localScale, Vector3.one, 1f - Mathf.Exp(-Clock.UnscaledDelta * 10f));
        }

        // ---- for the self-tests ----------------------------------------------------------------------------
        public bool CareMetersShown => careRoot != null && careRoot.gameObject.activeInHierarchy;
        public RectTransform CareMetersRect => careRoot;
        /// <summary>What each meter shows now: (kind, care, status word or percentage).</summary>
        public List<(PieceKind kind, float care, string text)> CareMeterReadings()
        {
            var list = new List<(PieceKind, float, string)>();
            foreach (var r in meterRows) list.Add((r.Kind, r.Shown, r.Value.text));
            return list;
        }
        /// <summary>
        /// For the self-tests: the meters as they stand at the trip's last frame, against the review. Null when
        /// every item's meter ends at its review's care value and says the same (its percentage, or its status
        /// word when it failed); otherwise what differs.
        /// </summary>
        public string CheckCareMeters(Recording rec, out int rows)
        {
            rows = meterRows.Count;
            if (rec == null || rec != meterRec) return "the meters show another trip";
            meterFrameForTest = rec.Frames.Count - 1;
            foreach (var r in meterRows) r.Shown = -1f;   // redraw every row
            UpdateCareMeters();
            meterFrameForTest = -1;
            var problems = new List<string>();
            int items = 0;
            foreach (var it in rec.Outcome.Items)
            {
                items++;
                var row = meterRows.Find(r => r.Body == it.Body);
                if (row == null) { problems.Add($"{it.Kind} has no meter"); continue; }
                if (row.Shown != it.Care) problems.Add($"{it.Kind} {row.Shown:0.000} vs {it.Care:0.000}");
                string want = it.Failed ? (it.Kind == PieceKind.Dragon && it.Status == ItemStatus.Scorched ? "BOX ON FIRE" : StatusWord(it.Status))
                                        : (row.HasLimit ? $"{it.Care * 100f:0}%" : "no limit");
                if (row.Value.text != want) problems.Add($"{it.Kind} says \"{row.Value.text}\", not \"{want}\"");
            }
            if (items != meterRows.Count) problems.Add($"{meterRows.Count} meters for {items} items");
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>For the self-tests: the journey controls the meters overlap on screen (the leg banner
        /// slides up and down, so only its width counts).</summary>
        public List<string> CareMeterClashes()
        {
            var list = new List<string>();
            if (!CareMetersShown) return list;
            Canvas.ForceUpdateCanvases();
            var m = PanelRect(careRoot);
            var others = new List<(string, RectTransform)> { ("the timeline", timeline), ("SKIP", skipBtn.Image.rectTransform) };
            foreach (var b in replayBar.GetComponentsInChildren<UiButton>()) others.Add((b.name, b.Image.rectTransform));
            foreach (var (name, rt) in others)
                if (rt.gameObject.activeInHierarchy && PanelRect(rt).Overlaps(m)) list.Add(name);
            var lb = PanelRect(legRt);
            if (lb.xMin < m.xMax && lb.xMax > m.xMin) list.Add("the leg banner");
            float sw = Screen.width, sh = Screen.height;
            if (m.xMin < 0 || m.yMin < 0 || m.xMax > sw || m.yMax > sh) list.Add("the screen's edge");
            return list;
        }

        /// <summary>For the self-tests: the button of each meter row, in order (null outside replays and for an
        /// item with no limit).</summary>
        public List<UiButton> CareMeterButtons()
        {
            var list = new List<UiButton>();
            foreach (var r in meterRows) list.Add(r.Jump);
            return list;
        }

        /// <summary>Recorded body index of each meter row, in order.</summary>
        public List<int> CareMeterBodies()
        {
            var list = new List<int>();
            foreach (var r in meterRows) list.Add(r.Body);
            return list;
        }
    }
}
