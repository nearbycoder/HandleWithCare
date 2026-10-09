using System;
using System.Collections.Generic;
using HWC.Audio;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HWC.Gameplay
{
    /// <summary>Title, delivery log (level select), settings and credits screens.</summary>
    public sealed class Menus : MonoBehaviour
    {
        RectTransform root, title, select, settings, credits;
        RectTransform selectGrid;
        TextMeshProUGUI totalStars, continueLabel;
        UiButton continueBtn;
        float titleT;
        RectTransform titleLogo, titleSub;
        const float LogoScale = 0.82f;
        readonly List<RectTransform> titleButtons = new List<RectTransform>();
        Game G => Game.I;
        public bool Open => (title != null && title.gameObject.activeSelf) || select.gameObject.activeSelf || settings.gameObject.activeSelf || credits.gameObject.activeSelf || confirm.gameObject.activeSelf;
        Action settingsBack;
        UiButton selectBack, settingsDone, creditsBack;

        /// <summary>The menu screen on top, if any (gamepad navigation stays inside it).</summary>
        public RectTransform ActiveScreen =>
            confirm.gameObject.activeSelf ? confirm :
            settings.gameObject.activeSelf ? settings : select.gameObject.activeSelf ? select :
            credits.gameObject.activeSelf ? credits : title.gameObject.activeSelf ? title : null;

        /// <summary>Which screen is on top, for the self-tests: confirm, settings, log, credits, title or none.</summary>
        public string ScreenName =>
            confirm.gameObject.activeSelf ? "confirm" : settings.gameObject.activeSelf ? "settings" : select.gameObject.activeSelf ? "log" :
            credits.gameObject.activeSelf ? "credits" : title.gameObject.activeSelf ? "title" : "none";

        /// <summary>Where the gamepad cursor starts on the screen on top.</summary>
        public UiButton DefaultButton =>
            confirm.gameObject.activeSelf ? keepBtn :
            settings.gameObject.activeSelf ? settingsDone : select.gameObject.activeSelf ? selectBack :
            credits.gameObject.activeSelf ? creditsBack : title.gameObject.activeSelf ? continueBtn : null;

        /// <summary>Gamepad B or Esc: leave the screen on top. False if there was nothing to leave.</summary>
        public bool Back()
        {
            if (confirm.gameObject.activeSelf) { keepBtn.Press(); return true; }
            if (settings.gameObject.activeSelf) { settingsDone.Press(); return true; }
            if (select.gameObject.activeSelf) { selectBack.Press(); return true; }
            if (credits.gameObject.activeSelf) { creditsBack.Press(); return true; }
            return false;
        }

        public static readonly (string id, string name, int stars)[] Tapes =
        {
            ("kraft", "Kraft", 0), ("stripe", "Candy Stripe", 8), ("polka", "Teal Polka", 16),
            ("fragile", "FRAGILE", 24), ("scales", "Dragon Scale", 34), ("gold", "Gold", 48),
        };

        public void Build(Transform canvas)
        {
            root = Ui.Rect("Menus", canvas).Stretch();
            BuildTitle();
            BuildSelect();
            BuildSettings();
            BuildCredits();
            BuildStartOver();
            HideAll();
        }

        /// <summary>The screens inside the page's safe area (left, bottom, right, top in screen pixels; touch
        /// screens with a notch), their full-screen dimming still edge to edge.</summary>
        public void ApplySafeArea(Vector4 sa)
        {
            var canvas = root.GetComponentInParent<Canvas>();
            float k = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            Vector2 min = new Vector2(sa.x, sa.y) / k, max = new Vector2(sa.z, sa.w) / k;
            foreach (var screen in new[] { title, select, settings, credits, confirm })
            {
                screen.offsetMin = min;
                screen.offsetMax = -max;
                if (screen.Find("dim") is RectTransform dim) { dim.offsetMin = -min; dim.offsetMax = max; }
            }
        }

        public void HideAll()
        {
            title.gameObject.SetActive(false);
            select.gameObject.SetActive(false);
            settings.gameObject.SetActive(false);
            credits.gameObject.SetActive(false);
            confirm.gameObject.SetActive(false);
        }

        // =================================================================================== title

        void BuildTitle()
        {
            title = Ui.Rect("Title", root).Stretch();
            var logo = Ui.Rect("logo", title);
            titleLogo = logo;
            // left column: logo, strapline, menu; the parcel sits on the right of the frame
            logo.Place(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(70 + 980 * LogoScale * 0.5f, -(48 + 330 * LogoScale * 0.5f)), new Vector2(980, 330));
            var stamp = Ui.Panel(logo, "stampBg", new Color(0.85f, 0.28f, 0.23f, 0.95f), Ui.Rounded(26, 6));
            stamp.rectTransform.Stretch();
            Ui.Shadow(stamp, 10, 0.35f);
            var inner = Ui.Panel(stamp.transform, "inner", new Color(0, 0, 0, 0), Ui.Rounded(20, 4));
            inner.rectTransform.Stretch(14, 14, 14, 14);
            var outline = Ui.Panel(stamp.transform, "outline", Palette.Cream, Ui.Rounded(20, 0));
            outline.rectTransform.Stretch(14, 14, 14, 14);
            outline.color = new Color(1, 1, 1, 0.0f);
            var t1 = Ui.Text(stamp.transform, "t1", "HANDLE", 150, Palette.Cream, Ui.Display);
            t1.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(940, 170));
            t1.characterSpacing = 6;
            var t2 = Ui.Text(stamp.transform, "t2", "WITH CARE", 118, Palette.Cream, Ui.Display);
            t2.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(940, 140));
            t2.characterSpacing = 8;
            var sub = Ui.Text(title, "sub", "MOSSBURY PARCEL POST  ·  WE SHIP ANYTHING. CAREFULLY.", 30, Palette.Ink, Ui.Display);
            titleSub = sub.rectTransform;
            sub.fontSize = 26;
            sub.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(84, -360), new Vector2(790, 40));
            var subBg = Ui.Panel(title, "subBg", new Color(0.97f, 0.93f, 0.85f, 0.92f), Ui.Rounded(10));
            subBg.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(70, -358), new Vector2(818, 54));
            subBg.transform.SetSiblingIndex(sub.transform.GetSiblingIndex());

            string[] labels = { "START SHIFT", "DELIVERY LOG", "SETTINGS", "CREDITS", "QUIT" };
            Action[] acts = { ContinueGame, ShowSelect, () => ShowSettings(ShowTitle), ShowCredits, () => Application.Quit() };
            int shown = WebPlatform.IsWeb ? labels.Length - 1 : labels.Length;   // a browser tab is closed, not quit
            for (int i = 0; i < shown; i++)
            {
                var b = Ui.Button(title, labels[i], labels[i], acts[i], i == 0 ? Palette.PostalRed : Palette.Cream, i == 0 ? Palette.Cream : Palette.Ink, i == 0 ? 46 : 34);
                b.Image.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(84, -480 - i * 88 - (i > 0 ? 22 : 0)), new Vector2(i == 0 ? 400 : 340, i == 0 ? 96 : 74));
                Ui.Shadow(b.Image, 6, 0.3f);
                titleButtons.Add(b.Image.rectTransform);
                if (i == 0) { continueBtn = b; continueLabel = b.Label; }
            }
            totalStars = Ui.Text(title, "stars", "", 28, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            totalStars.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(88, 40), new Vector2(500, 40));
            // a damaged save: say what happened (once, on the title screen after launch)
            var note = Ui.Panel(title, "saveNote", Palette.Sticky, Ui.Rounded(4));
            note.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(470, 30), new Vector2(560, 112));
            note.rectTransform.localRotation = Quaternion.Euler(0, 0, 1.5f);
            Ui.Shadow(note, 5, 0.25f);
            saveNoteText = Ui.Text(note.transform, "t", "", 21, Palette.Ink, Ui.Italic, TextAlignmentOptions.TopLeft);
            saveNoteText.rectTransform.Stretch(16, 16, 12, 30);
            var sig = Ui.Text(note.transform, "sig", "\u2014 Mabel", 18, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            sig.rectTransform.Stretch(14, 14, 8, 8);
            saveNoteText.enableAutoSizing = true; saveNoteText.fontSizeMin = 15; saveNoteText.fontSizeMax = 21;
            saveNote = note.rectTransform;
            note.gameObject.SetActive(false);
        }

        RectTransform saveNote;
        TextMeshProUGUI saveNoteText;
        bool saveNoteDone;
        public bool SaveNoteShowing => saveNote != null && saveNote.gameObject.activeInHierarchy;
        public string SaveNoteText => saveNoteText != null ? saveNoteText.text : "";

        public void ShowTitle()
        {
            HideAll();
            title.gameObject.SetActive(true);
            titleT = 0;
            int total = G.Save.TotalStars;
            totalStars.text = total > 0 ? $"<sprite=0> {total} / {Levels.All.Count * 3} STARS EARNED" : "";
            totalStars.text = total > 0 ? $"{total} / {Levels.All.Count * 3} STARS EARNED" : "";
            bool started = G.Save.Records.Count > 0;
            continueLabel.text = started ? "CONTINUE" : "START SHIFT";
            var load = SaveData.LastLoad;
            bool damaged = !saveNoteDone && (load == SaveData.LoadResult.RecoveredFromBackup || load == SaveData.LoadResult.Lost);
            saveNoteDone = true;
            saveNote.gameObject.SetActive(damaged);
            if (damaged)
                saveNoteText.text = load == SaveData.LoadResult.RecoveredFromBackup
                    ? "Your save file was damaged, so I opened the backup from just before it. You may have lost the last thing you did."
                    : "Your save file was damaged and there was no backup, so this is a fresh start. The damaged file is still in the save folder.";
            AudioDirector.I?.PlayMusic("title");
        }

        void ContinueGame()
        {
            HideAll();
            int n = 1;
            for (int i = 1; i <= Levels.All.Count; i++) if (G.Save.IsUnlocked(i)) n = i;
            if (G.Save.LastLevel >= 1 && G.Save.IsUnlocked(G.Save.LastLevel) && !G.Save.IsDelivered(G.Save.LastLevel)) n = G.Save.LastLevel;
            G.StartLevel(n);
        }

        // ================================================================================== select

        void BuildSelect()
        {
            select = Ui.Rect("Select", root).Stretch();
            var dim = Ui.Panel(select, "dim", new Color(0.12f, 0.08f, 0.06f, 0.55f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var board = Ui.Panel(select, "board", Palette.Hex("B98A55"), Ui.Rounded(20, 6));
            board.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1760, 960));
            Ui.Shadow(board, 12, 0.4f);
            UiIntro.Add(select, board.rectTransform);
            var h = Ui.Text(board.transform, "h", "DELIVERY LOG", 64, Palette.Cream, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(900, 80));
            h.outlineWidth = 0.15f;
            h.outlineColor = Palette.Ink;
            selectGrid = Ui.Rect("grid", board.transform);
            selectGrid.Stretch(30, 30, 110, 110);
            var back = selectBack = Ui.Button(board.transform, "back", "◀  BACK", () => { if (G.Phase == Phase.Title) ShowTitle(); else { HideAll(); G.Hud.SetPaused(true); } }, Palette.Cream, Palette.Ink, 32);
            back.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 24), new Vector2(220, 68));
            // the card under the pointer: which of its three stars are earned, and the best trip so far
            var det = Ui.Panel(board.transform, "detail", Palette.Paper, Ui.Rounded(10, 2));
            det.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(276, 14), new Vector2(1454, 88));
            det.raycastTarget = false;
            selectDetail = Ui.Text(det.transform, "t", "", 22, Palette.Ink, Ui.Body, TextAlignmentOptions.Left);
            selectDetail.rectTransform.Stretch(20, 20, 6, 6);
            selectDetail.enableAutoSizing = true; selectDetail.fontSizeMin = 15; selectDetail.fontSizeMax = 22;
            selectDetail.raycastTarget = false;
        }

        TextMeshProUGUI selectDetail;
        const string DetailPrompt = "Point at a delivery to see which stars it has and your best trip.";
        public string SelectDetailText => selectDetail != null ? selectDetail.text : "";

        /// <summary>A card in the delivery log (for the self-tests).</summary>
        public RectTransform CardRect(int number)
        {
            foreach (Transform col in selectGrid)
            {
                var c = col.Find("card" + number);
                if (c != null) return (RectTransform)c;
            }
            return null;
        }

        /// <summary>The three stars of a delivery by goal (delivered, under budget, handled with care).</summary>
        bool[] StarGoals(int number)
        {
            var r = G.Save.Get(number);
            var got = new[] { r != null && r.Delivered, r != null && r.Delivered && r.UnderBudget, r != null && r.Delivered && r.Careful };
            int stars = r?.Stars ?? 0, n = 0;
            foreach (bool b in got) if (b) n++;
            if (n < stars) for (int i = 0; i < 3; i++) got[i] = i < stars;   // an older record without the goals
            return got;
        }

        // Fira Sans has no check or cross marks (they draw as empty boxes): a dot and a multiplication sign
        static string Mark(bool ok) => ok ? Glyphs.Check : Glyphs.Cross;

        string Detail(LevelDef lv)
        {
            string head = $"<b>{lv.Number:00}  {lv.Title.ToUpperInvariant()}</b>";
            if (!G.Save.IsUnlocked(lv.Number)) return head + $"\nLocked: deliver {lv.Number - 1:00} first.";
            var r = G.Save.Get(lv.Number);
            string mabel = $"Mabel's best {lv.Expert}" + (r != null && r.Expert ? "  <color=#1F7A6F>EXPERT</color>" : "");
            if (r == null || r.Attempts == 0) return head + $"   \u00B7   not shipped yet\nPar {lv.Par}   \u00B7   {mabel}";
            string about = $"   \u00B7   {(r.Attempts == 1 ? "1 trip" : r.Attempts + " trips")}{(r.HintStage > 0 ? "   \u00B7   hinted" : "")}   \u00B7   {mabel}";
            if (!r.Delivered) return head + about + $"\n{Mark(false)} Not delivered yet   \u00B7   par {lv.Par}";
            var got = StarGoals(lv.Number);
            string budget = r.BestCost >= 0 ? $"  best {r.BestCost} / par {lv.Par}" : "";
            string care = r.BestCare >= 0 ? $"  best {r.BestCare * 100:0}% / {SimConst.CareFraction * 100:0}%" : "";
            return head + about + $"\n{Mark(got[0])} DELIVERED        {Mark(got[1])} UNDER BUDGET{budget}        {Mark(got[2])} HANDLED WITH CARE{care}";
        }

        static readonly string[] ShiftNames = { "SHIFT 1 · FIRST DAY", "SHIFT 2 · THE DEPOT", "SHIFT 3 · LAST MILE", "SHIFT 4 · EXPRESS", "SHIFT 5 · OVERTIME" };

        public void ShowSelect()
        {
            HideAll();
            select.gameObject.SetActive(true);
            selectDetail.text = DetailPrompt;
            foreach (Transform c in selectGrid) Destroy(c.gameObject);
            int shifts = Levels.Chapters;
            float cardW = (1760f - 60f) / shifts - 20f;     // 390 with four shifts, 320 with five
            for (int ch = 1; ch <= shifts; ch++)
            {
                var col = Ui.Rect("shift" + ch, selectGrid);
                col.anchorMin = new Vector2((ch - 1) / (float)shifts, 0);
                col.anchorMax = new Vector2(ch / (float)shifts, 1);
                col.offsetMin = new Vector2(10, 0);
                col.offsetMax = new Vector2(-10, 0);
                var head = Ui.Panel(col, "head", ch > Levels.StoryChapters ? Palette.TealDark : Palette.Ink, Ui.Rounded(10));
                head.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(cardW + 10, 52));
                var ht = Ui.Text(head.transform, "t", ShiftNames[Mathf.Min(ch, ShiftNames.Length) - 1], 26, Palette.Cream, Ui.Display);
                ht.rectTransform.Stretch(6, 6, 2, 2);
                int k = 0;
                foreach (var lv in Levels.All)
                {
                    if (lv.Chapter != ch) continue;
                    MakeCard(col, lv, k++, cardW);
                }
            }
        }

        void MakeCard(RectTransform col, LevelDef lv, int k, float cardW)
        {
            bool unlocked = G.Save.IsUnlocked(lv.Number);
            var card = Ui.Button(col, "card" + lv.Number, null, () => { if (unlocked) { HideAll(); G.Hud.SetPaused(false); G.StartLevel(lv.Number); } }, unlocked ? Palette.Cream : Palette.Hex("D8CBB4"), Palette.Ink);
            var rt = card.Image.rectTransform;
            rt.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(((k % 2) * 2 - 1) * 6, -70 - k * 136), new Vector2(cardW, 124));
            float inner = cardW - 104f;   // title and customer width
            rt.localRotation = Quaternion.Euler(0, 0, ((lv.Number * 37) % 7 - 3) * 0.6f);
            Ui.Shadow(card.Image, 5, 0.3f);
            card.HoverScale = unlocked ? 1.04f : 1f;
            card.OnHover = () => selectDetail.text = Detail(lv);
            var pin = Ui.Panel(card.transform, "pin", Palette.PostalRed, Ui.Circle);
            pin.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(22, 22));
            var num = Ui.Text(card.transform, "num", lv.Number.ToString("00"), 44, unlocked ? Palette.PostalRed : new Color(0.4f, 0.35f, 0.3f), Ui.Display, TextAlignmentOptions.Left);
            num.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 8), new Vector2(80, 60));
            var t = Ui.Text(card.transform, "title", unlocked ? lv.Title.ToUpperInvariant() : "???", 28, Palette.Ink, Ui.Display, TextAlignmentOptions.TopLeft);
            t.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(92, -16), new Vector2(inner, 36));
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = true; t.fontSizeMin = 16; t.fontSizeMax = 28;
            var cu = Ui.Text(card.transform, "cust", unlocked ? lv.Customer : "Locked", 19, Palette.InkSoft, Ui.Italic, TextAlignmentOptions.TopLeft);
            cu.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(92, -52), new Vector2(inner, 28));
            cu.enableAutoSizing = true; cu.fontSizeMin = 14; cu.fontSizeMax = 19;
            cu.textWrappingMode = TextWrappingModes.NoWrap;
            var goals = StarGoals(lv.Number);   // a missing star shows as a gap in its place
            for (int s = 0; s < 3; s++)
            {
                var st = Ui.Icon(card.transform, "star" + s, Ui.Star, goals[s] ? Palette.Gold : new Color(0, 0, 0, 0.13f));
                st.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(92 + s * 34, 10), new Vector2(30, 30));
            }
            if (unlocked && G.Save.HintStage(lv.Number) > 0)
            {
                // a pencil note: Mabel helped with this one
                var hn = Ui.Text(card.transform, "hinted", "hinted", 17, new Color(0.35f, 0.32f, 0.3f, 0.8f), Ui.Italic, TextAlignmentOptions.Left);
                hn.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 8), new Vector2(70, 24));
                hn.rectTransform.localRotation = Quaternion.Euler(0, 0, -4);
            }
            if (!unlocked)
            {
                var lk = Ui.Text(card.transform, "lock", "LOCKED", 22, new Color(0.45f, 0.38f, 0.32f), Ui.Display, TextAlignmentOptions.Right);
                lk.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 10), new Vector2(160, 30));
            }
            else if (G.Save.IsDelivered(lv.Number))
            {
                bool ex = G.Save.IsExpert(lv.Number);
                var d = Ui.Text(card.transform, "done", ex ? "EXPERT" : "DELIVERED", 22, ex ? Palette.TealDark : Palette.Good, Ui.Display, TextAlignmentOptions.Right);
                d.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 10), new Vector2(160, 30));
                d.rectTransform.localRotation = Quaternion.Euler(0, 0, 6);
            }
        }

        // ================================================================================ settings

        Slider master, music, sfx;
        readonly List<(Toggle t, string key)> toggles = new List<(Toggle, string)>();
        RectTransform tapeRow;

        void BuildSettings()
        {
            settings = Ui.Rect("Settings", root).Stretch();
            var dim = Ui.Panel(settings, "dim", new Color(0.08f, 0.05f, 0.04f, 0.66f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(settings, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1540, 940));
            UiIntro.Add(settings, p.rectTransform);
            Ui.Shadow(p, 10);
            var h = Ui.Text(p.transform, "h", "SETTINGS", 64, Palette.Ink, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(800, 80));
            // two columns: sound and play on the left, display on the right
            var left = Ui.Rect("left", p.transform);
            left.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(770, 600));
            var right = Ui.Rect("right", p.transform);
            right.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(770, 0), new Vector2(770, 600));
            ColumnHead(left, "SOUND & PLAY");
            ColumnHead(right, "DISPLAY");
            master = SliderRow(left, "MASTER VOLUME", -170, v => { G.Save.MasterVolume = v; ApplyAudio(); });
            music = SliderRow(left, "MUSIC", -240, v => { G.Save.MusicVolume = v; ApplyAudio(); });
            sfx = SliderRow(left, "SOUND EFFECTS", -310, v => { G.Save.SfxVolume = v; ApplyAudio(); G.Hud.Sfx("click", 0.8f); });
            ToggleRow(left, "SCREEN SHAKE", -390, "shake");
            ToggleRow(left, "REDUCED MOTION (no slow-mo, fewer particles)", -455, "motion");
            ToggleRow(left, "SHOW PACKING GRID", -520, "grid");
            iconsBtn = CycleRow(left, "GAMEPAD BUTTON ICONS", -585, CycleIcons);
            ToggleRow(right, "FULLSCREEN", -170, "full");
            if (WebPlatform.IsWeb)
            {
                // the browser sizes the page and paces the frames: no window size, frame rate limit or VSync
                ToggleRow(right, "PAUSE WHEN IN THE BACKGROUND", -235, "bgpause");
                ToggleRow(right, "LARGER TEXT (cards, notes, hints)", -300, "text");
                FidelityRow(right, -380);
            }
            else
            {
                windowBtn = CycleRow(right, "WINDOW SIZE", -235, CycleWindow);
                frameBtn = CycleRow(right, "FRAME RATE LIMIT (VSync off)", -300, CycleFrameCap);
                ToggleRow(right, "VSYNC", -365, "vsync");
                ToggleRow(right, "PAUSE WHEN IN THE BACKGROUND", -430, "bgpause");
                ToggleRow(right, "LARGER TEXT (cards, notes, hints)", -495, "text");
                FidelityRow(right, -575);
            }
            var tl = Ui.Text(p.transform, "tapeLabel", "TAPE DESIGN", 30, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            tl.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -652), new Vector2(400, 40));
            tapeRow = Ui.Rect("tapes", p.transform);
            tapeRow.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -700), new Vector2(780, 110));
            var back = settingsDone = Ui.Button(p.transform, "back", "DONE", () => { G.Save.Write(); settingsBack?.Invoke(); }, Palette.PostalRed, Palette.Cream, 38);
            back.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 76));
            startOverBtn = Ui.Button(p.transform, "startOver", "START OVER", () => { confirm.gameObject.SetActive(true); confirm.SetAsLastSibling(); }, Palette.Cream, Palette.PostalRedDark, 26);
            startOverBtn.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 38), new Vector2(230, 60));
        }

        // ---- start over: progress cleared, settings kept, the old save kept as a copy ---------------------
        RectTransform confirm;
        UiButton keepBtn, eraseBtn, startOverBtn;
        public UiButton StartOverButton => startOverBtn;
        public string ContinueLabel => continueLabel.text;
        public UiButton StartOverKeepButton => keepBtn;
        public UiButton StartOverConfirmButton => eraseBtn;
        public bool StartOverAsking => confirm.gameObject.activeSelf;

        void BuildStartOver()
        {
            confirm = Ui.Rect("StartOver", root).Stretch();
            var dim = Ui.Panel(confirm, "dim", new Color(0.08f, 0.05f, 0.04f, 0.6f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(confirm, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 420));
            UiIntro.Add(confirm, p.rectTransform);
            Ui.Shadow(p, 10);
            var h = Ui.Text(p.transform, "h", "START OVER?", 58, Palette.PostalRedDark, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(760, 72));
            var t = Ui.Text(p.transform, "t", "Every star, delivery and box on the bench is cleared, and Shift 1 starts again. " +
                "Your settings stay. A copy of this save is kept next to it, just in case.", 24, Palette.Ink, Ui.Body);
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(740, 150));
            keepBtn = Ui.Button(p.transform, "keep", "KEEP MY PROGRESS", () => confirm.gameObject.SetActive(false), Palette.Teal, Palette.Cream, 30);
            keepBtn.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-200, 36), new Vector2(360, 76));
            eraseBtn = Ui.Button(p.transform, "erase", "START OVER", StartOver, Palette.PostalRed, Palette.Cream, 30);
            eraseBtn.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(200, 36), new Vector2(360, 76));
            confirm.gameObject.SetActive(false);
        }

        void StartOver()
        {
            G.Packing.End();               // the box on the bench is part of the progress: don't keep it
            if (!G.Save.StartOver()) { confirm.gameObject.SetActive(false); return; }
            G.Level = null;
            G.LastRun = null;              // nor the last trip
            G.Hud.Sfx("clear");
            G.ShowTitle();
        }

        static void ColumnHead(RectTransform col, string text)
        {
            var t = Ui.Text(col, "head", text, 24, Palette.PostalRedDark, Ui.Display, TextAlignmentOptions.Left);
            t.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -112), new Vector2(600, 32));
            t.characterSpacing = 4;
        }

        // ---- display settings ---------------------------------------------------------------------------
        UiButton windowBtn, frameBtn, iconsBtn;
        bool syncing;
        public UiButton ButtonIconsButton => iconsBtn;

        /// <summary>Gamepad prompts: AUTO (from the pad), XBOX or PLAYSTATION.</summary>
        void CycleIcons()
        {
            G.Save.ButtonIcons = (G.Save.ButtonIcons + 1) % PadGlyphs.SettingNames.Length;
            PadGlyphs.Refresh(G.Save.ButtonIcons);
            RefreshDisplayButtons();
        }
        public UiButton WindowSizeButton => windowBtn;
        public UiButton FrameRateButton => frameBtn;
        public Toggle SettingToggle(string key) { foreach (var (t, k) in toggles) if (k == key) return t; return null; }
        static readonly int[] FrameCaps = { 30, 60, 120, 0 };
        static readonly Vector2Int[] WindowSizes =
        {
            new Vector2Int(1280, 720), new Vector2Int(1280, 800), new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080), new Vector2Int(2560, 1440),
        };

        /// <summary>The window sizes that fit on this display.</summary>
        static List<Vector2Int> FittingSizes()
        {
            var list = new List<Vector2Int>();
            int dw = Display.main.systemWidth, dh = Display.main.systemHeight;
            foreach (var s in WindowSizes) if (s.x <= dw && s.y <= dh) list.Add(s);
            if (list.Count == 0) list.Add(WindowSizes[0]);
            return list;
        }

        void CycleWindow()
        {
            var sizes = FittingSizes();
            var cur = new Vector2Int(G.Save.WindowW > 0 ? G.Save.WindowW : Screen.width, G.Save.WindowH > 0 ? G.Save.WindowH : Screen.height);
            int i = sizes.IndexOf(cur);
            var next = sizes[(i + 1) % sizes.Count];
            G.Save.WindowW = next.x; G.Save.WindowH = next.y;
            G.ApplyDisplay();
            RefreshDisplayButtons();
        }

        void CycleFrameCap()
        {
            int i = Array.IndexOf(FrameCaps, G.Save.FrameCap);
            G.Save.FrameCap = FrameCaps[(i + 1) % FrameCaps.Length];
            G.ApplyDisplay();
            RefreshDisplayButtons();
        }

        void RefreshDisplayButtons()
        {
            iconsBtn.Label.text = PadGlyphs.SettingNames[Mathf.Clamp(G.Save.ButtonIcons, 0, PadGlyphs.SettingNames.Length - 1)];
            if (windowBtn == null) return;   // the browser build has no window or frame-rate rows
            bool full = G.Save.Fullscreen;
            windowBtn.Label.text = full ? "FULLSCREEN" : $"{(G.Save.WindowW > 0 ? G.Save.WindowW : Screen.width)} \u00D7 {(G.Save.WindowH > 0 ? G.Save.WindowH : Screen.height)}";
            windowBtn.SetInteractable(!full);
            frameBtn.Label.text = G.Save.FrameCap > 0 ? $"{G.Save.FrameCap} FPS" : "UNLIMITED";
            frameBtn.SetInteractable(!G.Save.VSync);
        }

        UiButton CycleRow(Transform parent, string label, float y, Action onClick)
        {
            var l = Ui.Text(parent, label, label, 26, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(430, 40));
            l.enableAutoSizing = true; l.fontSizeMin = 18; l.fontSizeMax = 26;
            var b = Ui.Button(parent, label + "_value", "-", () => { onClick(); G.Hud.Sfx("click", 0.6f); }, Palette.Ink, Palette.Cream, 24);   // filled in by RefreshDisplayButtons
            b.Image.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y + 4), new Vector2(200, 48));
            b.HoverScale = 1.04f;
            return b;
        }

        Slider SliderRow(Transform parent, string label, float y, Action<float> onChange)
        {
            var l = Ui.Text(parent, label, label, 28, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(280, 40));
            var go = Ui.Rect("slider", parent);
            go.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, y - 6), new Vector2(360, 30));
            var bg = Ui.Panel(go, "bg", new Color(0, 0, 0, 0.15f), Ui.Rounded(12));
            bg.rectTransform.Stretch(0, 0, 8, 8);
            var fillArea = Ui.Rect("fillArea", go).Stretch(6, 6, 8, 8);
            var fill = Ui.Panel(fillArea, "fill", Palette.Teal, Ui.Rounded(10));
            fill.rectTransform.Stretch();
            var handleArea = Ui.Rect("handleArea", go).Stretch(12, 12, 0, 0);
            var handle = Ui.Panel(handleArea, "handle", Palette.PostalRed, Ui.Circle);
            handle.rectTransform.sizeDelta = new Vector2(34, 34);
            go.gameObject.AddComponent<HoverSfx>();
            var s = go.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1;
            s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        // ---- graphics fidelity: four steps on one track, filled up to the one chosen ----------------------
        readonly List<UiButton> fidelitySteps = new List<UiButton>();
        TextMeshProUGUI fidelityBlurb, fidelityKeys;
        public IReadOnlyList<UiButton> FidelityButtons => fidelitySteps;
        public string FidelityBlurb => fidelityBlurb.text;

        void FidelityRow(Transform parent, float y)
        {
            var l = Ui.Text(parent, "GRAPHICS FIDELITY", "GRAPHICS FIDELITY", 26, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(400, 40));
            fidelityKeys = Ui.Text(parent, "keys", "\u2190 \u2192 keys", 20, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.Right);
            fidelityKeys.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y - 4), new Vector2(220, 34));
            var track = Ui.Rect("fidelity", parent);
            track.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y - 48), new Vector2(650, 54));
            var bg = Ui.Panel(track, "bg", new Color(0, 0, 0, 0.1f), Ui.Rounded(18));
            bg.rectTransform.Stretch(-4, -4, -4, -4);
            const float gap = 6f, w = (650f - 3 * gap) / 4f;
            for (int i = 0; i < GraphicsQuality.Names.Length; i++)
            {
                int step = i;
                var b = Ui.Button(track, "fidelity_" + i, GraphicsQuality.Names[i], () => SetFidelity(step), Palette.Cream, Palette.Ink, 24);
                b.Image.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * (w + gap), 0), new Vector2(w, 54));
                b.HoverScale = 1.04f;
                fidelitySteps.Add(b);
            }
            fidelityBlurb = Ui.Text(parent, "blurb", "", 20, Palette.InkSoft, Ui.Body, TextAlignmentOptions.TopLeft);
            fidelityBlurb.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, y - 112), new Vector2(646, 58));
            fidelityBlurb.enableAutoSizing = true; fidelityBlurb.fontSizeMin = 15; fidelityBlurb.fontSizeMax = 20;
        }

        void SetFidelity(int level)
        {
            level = Mathf.Clamp(level, 0, GraphicsQuality.Names.Length - 1);
            if (level == G.Save.FidelityLevel && level == GraphicsQuality.Level) return;
            G.Save.SetFidelity(level);
            GraphicsQuality.Apply(level);
            RefreshFidelity();
        }

        void RefreshFidelity()
        {
            int cur = G.Save.FidelityLevel;
            for (int i = 0; i < fidelitySteps.Count; i++)
            {
                var b = fidelitySteps[i];
                // the chosen step solid teal, the ones below it a lighter teal: the track reads as filled up to it
                b.SetColor(i == cur ? Palette.Teal : i < cur ? Color.Lerp(Palette.Cream, Palette.Teal, 0.32f) : Palette.Cream);
                b.Label.color = i == cur ? Palette.Cream : Palette.Ink;
            }
            fidelityBlurb.text = GraphicsQuality.Blurbs[cur];
        }

        void ToggleRow(Transform parent, string label, float y, string key)
        {
            var l = Ui.Text(parent, label, label, 26, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(540, 40));
            l.enableAutoSizing = true; l.fontSizeMin = 18; l.fontSizeMax = 26;
            var go = Ui.Rect("toggle", parent);
            go.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y + 2), new Vector2(84, 44));
            var bg = Ui.Panel(go, "bg", new Color(0, 0, 0, 0.18f), Ui.Rounded(22));
            bg.rectTransform.Stretch();
            var knob = Ui.Panel(go, "knob", Palette.Cream, Ui.Circle);
            knob.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(36, 36));
            go.gameObject.AddComponent<HoverSfx>();
            var t = go.gameObject.AddComponent<Toggle>();
            t.targetGraphic = bg;
            t.onValueChanged.AddListener(on =>
            {
                bg.color = on ? Palette.Teal : new Color(0, 0, 0, 0.18f);
                knob.rectTransform.anchoredPosition = new Vector2(on ? 62 : 22, 0);
                if (syncing) return;   // ShowSettings is only showing the saved values
                switch (key)
                {
                    case "shake": G.Save.ScreenShake = on; G.Rig.ShakeEnabled = on; break;
                    case "motion": G.Save.ReducedMotion = on; Fx.Reduced = on; break;
                    case "grid": G.Save.ShowGrid = on; if (G.Station.Box != null) G.Station.Box.ShowGrid(on); break;
                    case "full":
                        // in a browser the page goes fullscreen (on this click, or the next if the browser wants one) and Esc leaves it
                        if (WebPlatform.IsWeb) { Screen.fullScreen = on; break; }
                        G.Save.Fullscreen = on; G.ApplyDisplay(); if (windowBtn != null) RefreshDisplayButtons(); break;
                    case "vsync": G.Save.VSync = on; G.ApplyDisplay(); if (frameBtn != null) RefreshDisplayButtons(); break;
                    case "bgpause": G.Save.PauseInBackground = on; break;
                    case "text": G.Save.LargerText = on; TextScale.Set(on); break;
                }
                G.Hud.Sfx("click", 0.6f);
            });
            toggles.Add((t, key));
        }

        public void ShowSettings(Action back)
        {
            HideAll();
            settingsBack = () => { HideAll(); back?.Invoke(); };
            settings.gameObject.SetActive(true);
            master.SetValueWithoutNotify(G.Save.MasterVolume);
            music.SetValueWithoutNotify(G.Save.MusicVolume);
            sfx.SetValueWithoutNotify(G.Save.SfxVolume);
            foreach (var (t, key) in toggles)
            {
                bool v = key == "shake" ? G.Save.ScreenShake : key == "motion" ? G.Save.ReducedMotion : key == "grid" ? G.Save.ShowGrid
                    : key == "vsync" ? G.Save.VSync : key == "bgpause" ? G.Save.PauseInBackground
                    : key == "text" ? G.Save.LargerText : WebPlatform.IsWeb ? Screen.fullScreen : G.Save.Fullscreen;
                syncing = true;
                t.isOn = !v;
                t.isOn = v;
                syncing = false;
            }
            RefreshDisplayButtons();
            RefreshFidelity();
            BuildTapes();
        }

        void BuildTapes()
        {
            foreach (Transform c in tapeRow) Destroy(c.gameObject);
            int total = G.Save.TotalStars;
            for (int i = 0; i < Tapes.Length; i++)
            {
                var (id, name, req) = Tapes[i];
                bool open = total >= req;
                bool sel = G.Save.Tape == id;
                var b = Ui.Button(tapeRow, id, null, () => { if (open) { G.Save.Tape = id; G.Save.Write(); BuildTapes(); } }, sel ? Palette.Sticky : Color.white, Palette.Ink);
                b.Image.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 130, 0), new Vector2(122, 104));
                var sw = Ui.Icon(b.transform, "swatch", Ui.FromTexture(TextureLibrary.Get("tape_" + id)), open ? Color.white : new Color(1, 1, 1, 0.25f));
                sw.preserveAspect = false;
                sw.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(104, 34));
                var lab = Ui.Text(b.transform, "n", open ? name.ToUpperInvariant() : $"{req} STARS", 17, Palette.Ink, Ui.Display);
                lab.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(118, 50));
            }
        }

        public void ApplyAudio()
        {
            var a = AudioDirector.I;
            if (a == null) return;
            a.Master = G.Save.MasterVolume;
            a.Music = G.Save.MusicVolume;
            a.Sfx = G.Save.SfxVolume;
        }

        // ================================================================================= credits

        void BuildCredits()
        {
            credits = Ui.Rect("Credits", root).Stretch();
            var dim = Ui.Panel(credits, "dim", new Color(0.08f, 0.05f, 0.04f, 0.6f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(credits, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 760));
            UiIntro.Add(credits, p.rectTransform);
            var h = Ui.Text(p.transform, "h", "CREDITS", 64, Palette.Ink, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(800, 80));
            var body = Ui.Text(p.transform, "body",
                "<b>HANDLE WITH CARE</b>\nA game about packing bizarre deliveries.\n\n" +
                "Design, code, models, textures, music and sound: made for this project\n" +
                "Models: Blender 4.5 (scripted)   ·   Engine: Unity 6 URP\n" +
                "Music & SFX: synthesized from scratch with numpy\n" +
                "Fonts: Fira Sans (SIL Open Font License)\n\n" +
                "Thanks to every customer who tipped generously.\nAnd to Ember, who sneezed on everything.",
                28, Palette.Ink, Ui.Body);
            body.rectTransform.Stretch(60, 60, 120, 140);
            var back = creditsBack = Ui.Button(p.transform, "back", "BACK", ShowTitle, Palette.PostalRed, Palette.Cream, 38);
            back.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 76));
            // beside the credits panel, not over its text
            var note = Ui.Panel(credits, "overtime", Palette.Sticky, Ui.Rounded(4));
            note.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(700, 200), new Vector2(330, 170));
            note.rectTransform.localRotation = Quaternion.Euler(0, 0, 4f);
            Ui.Shadow(note, 5, 0.25f);
            var nt = Ui.Text(note.transform, "t", "Don't go home yet. Five odd orders came in. They're in the Delivery Log under OVERTIME.", 22, Palette.Ink, Ui.Italic, TextAlignmentOptions.TopLeft);
            nt.rectTransform.Stretch(16, 14, 14, 30);
            var ns = Ui.Text(note.transform, "sig", "— Mabel", 18, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            ns.rectTransform.Stretch(14, 14, 8, 8);
            overtimeNote = note.rectTransform;
            note.gameObject.SetActive(false);
        }

        RectTransform overtimeNote;
        public bool OvertimeNoteShowing => overtimeNote != null && overtimeNote.gameObject.activeInHierarchy;

        /// <summary>After The Dragon Egg: the credits, plus a note that Overtime is open (when it is).</summary>
        public void ShowCreditsFinale(bool overtimeOpens = false)
        {
            ShowCredits();
            overtimeNote.gameObject.SetActive(overtimeOpens);
        }

        void ShowCredits()
        {
            HideAll();
            credits.gameObject.SetActive(true);
            overtimeNote.gameObject.SetActive(false);
        }

        void Update()
        {
            // Esc backs out of the screen on top, like the pad's B; the pause menu underneath doesn't see it
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && Open && Back()) G.Hud.ConsumeEscape();
            if (settings.gameObject.activeSelf && !confirm.gameObject.activeSelf)
            {
                if (kb != null && kb.leftArrowKey.wasPressedThisFrame) { SetFidelity(G.Save.FidelityLevel - 1); G.Hud.Sfx("click", 0.6f); }
                if (kb != null && kb.rightArrowKey.wasPressedThisFrame) { SetFidelity(G.Save.FidelityLevel + 1); G.Hud.Sfx("click", 0.6f); }
                fidelityKeys.enabled = PadInput.I == null || !PadInput.I.Active;
            }
            if (title == null || !title.gameObject.activeSelf) return;
            titleT += Clock.UnscaledDelta;
            // stamp-slam entrance for the logo, then a gentle breathe
            float s = titleT < 0.25f ? Mathf.Lerp(2.2f, 0.94f, titleT / 0.25f) : (titleT < 0.4f ? Mathf.Lerp(0.94f, 1f, (titleT - 0.25f) / 0.15f) : 1f + Mathf.Sin(titleT * 1.6f) * 0.006f);
            titleLogo.localScale = Vector3.one * (s * LogoScale);
            titleLogo.localRotation = Quaternion.Euler(0, 0, -3f);
            if (titleT > 0.2f && titleT - Clock.UnscaledDelta <= 0.2f) { G.Hud.Sfx("stamp_good"); G.Rig.AddTrauma(0.35f); }
            for (int i = 0; i < titleButtons.Count; i++)
            {
                float k = Mathf.Clamp01((titleT - 0.45f - i * 0.07f) / 0.25f);
                k = 1f - Mathf.Pow(1f - k, 3f);
                titleButtons[i].localScale = Vector3.one * k;
            }
        }
    }
}
