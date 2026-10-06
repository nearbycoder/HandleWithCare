using System;
using System.Collections.Generic;
using HWC.Audio;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using TMPro;
using UnityEngine;
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
        public bool Open => (title != null && title.gameObject.activeSelf) || select.gameObject.activeSelf || settings.gameObject.activeSelf || credits.gameObject.activeSelf;
        Action settingsBack;

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
            HideAll();
        }

        public void HideAll()
        {
            title.gameObject.SetActive(false);
            select.gameObject.SetActive(false);
            settings.gameObject.SetActive(false);
            credits.gameObject.SetActive(false);
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
            for (int i = 0; i < labels.Length; i++)
            {
                var b = Ui.Button(title, labels[i], labels[i], acts[i], i == 0 ? Palette.PostalRed : Palette.Cream, i == 0 ? Palette.Cream : Palette.Ink, i == 0 ? 46 : 34);
                b.Image.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(84, -480 - i * 88 - (i > 0 ? 22 : 0)), new Vector2(i == 0 ? 400 : 340, i == 0 ? 96 : 74));
                Ui.Shadow(b.Image, 6, 0.3f);
                titleButtons.Add(b.Image.rectTransform);
                if (i == 0) { continueBtn = b; continueLabel = b.Label; }
            }
            totalStars = Ui.Text(title, "stars", "", 28, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            totalStars.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(88, 40), new Vector2(500, 40));
        }

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
            var h = Ui.Text(board.transform, "h", "DELIVERY LOG", 64, Palette.Cream, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(900, 80));
            h.outlineWidth = 0.15f;
            h.outlineColor = Palette.Ink;
            selectGrid = Ui.Rect("grid", board.transform);
            selectGrid.Stretch(30, 30, 110, 110);
            var back = Ui.Button(board.transform, "back", "◀  BACK", () => { if (G.Phase == Phase.Title) ShowTitle(); else { HideAll(); G.Hud.SetPaused(true); } }, Palette.Cream, Palette.Ink, 32);
            back.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 24), new Vector2(220, 68));
        }

        static readonly string[] ShiftNames = { "SHIFT 1 · FIRST DAY", "SHIFT 2 · THE DEPOT", "SHIFT 3 · LAST MILE", "SHIFT 4 · EXPRESS", "SHIFT 5 · OVERTIME" };

        public void ShowSelect()
        {
            HideAll();
            select.gameObject.SetActive(true);
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
            int stars = G.Save.StarsFor(lv.Number);
            var card = Ui.Button(col, "card" + lv.Number, null, () => { if (unlocked) { HideAll(); G.Hud.SetPaused(false); G.StartLevel(lv.Number); } }, unlocked ? Palette.Cream : Palette.Hex("D8CBB4"), Palette.Ink);
            var rt = card.Image.rectTransform;
            rt.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(((k % 2) * 2 - 1) * 6, -70 - k * 136), new Vector2(cardW, 124));
            float inner = cardW - 104f;   // title and customer width
            rt.localRotation = Quaternion.Euler(0, 0, ((lv.Number * 37) % 7 - 3) * 0.6f);
            Ui.Shadow(card.Image, 5, 0.3f);
            card.HoverScale = unlocked ? 1.04f : 1f;
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
            for (int s = 0; s < 3; s++)
            {
                var st = Ui.Icon(card.transform, "star" + s, Ui.Star, s < stars ? Palette.Gold : new Color(0, 0, 0, 0.13f));
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
            var dim = Ui.Panel(settings, "dim", new Color(0.08f, 0.05f, 0.04f, 0.55f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(settings, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 970));
            Ui.Shadow(p, 10);
            var h = Ui.Text(p.transform, "h", "SETTINGS", 64, Palette.Ink, Ui.Display);
            h.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(800, 80));
            master = SliderRow(p.transform, "MASTER VOLUME", -130, v => { G.Save.MasterVolume = v; ApplyAudio(); });
            music = SliderRow(p.transform, "MUSIC", -205, v => { G.Save.MusicVolume = v; ApplyAudio(); });
            sfx = SliderRow(p.transform, "SOUND EFFECTS", -280, v => { G.Save.SfxVolume = v; ApplyAudio(); G.Hud.Sfx("click", 0.8f); });
            ToggleRow(p.transform, "SCREEN SHAKE", -370, "shake");
            ToggleRow(p.transform, "REDUCED MOTION (no slow-mo, fewer particles)", -435, "motion");
            ToggleRow(p.transform, "SHOW PACKING GRID", -500, "grid");
            ToggleRow(p.transform, "FULLSCREEN", -565, "full");
            ToggleRow(p.transform, "HIGH QUALITY GRAPHICS (turn off on slower computers)", -630, "gfx");
            var tl = Ui.Text(p.transform, "tapeLabel", "TAPE DESIGN", 30, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            tl.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -690), new Vector2(400, 40));
            tapeRow = Ui.Rect("tapes", p.transform);
            tapeRow.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -740), new Vector2(780, 110));
            var back = Ui.Button(p.transform, "back", "DONE", () => { G.Save.Write(); settingsBack?.Invoke(); }, Palette.PostalRed, Palette.Cream, 38);
            back.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 76));
        }

        Slider SliderRow(Transform parent, string label, float y, Action<float> onChange)
        {
            var l = Ui.Text(parent, label, label, 28, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(360, 40));
            var go = Ui.Rect("slider", parent);
            go.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(420, y - 6), new Vector2(420, 30));
            var bg = Ui.Panel(go, "bg", new Color(0, 0, 0, 0.15f), Ui.Rounded(12));
            bg.rectTransform.Stretch(0, 0, 8, 8);
            var fillArea = Ui.Rect("fillArea", go).Stretch(6, 6, 8, 8);
            var fill = Ui.Panel(fillArea, "fill", Palette.Teal, Ui.Rounded(10));
            fill.rectTransform.Stretch();
            var handleArea = Ui.Rect("handleArea", go).Stretch(12, 12, 0, 0);
            var handle = Ui.Panel(handleArea, "handle", Palette.PostalRed, Ui.Circle);
            handle.rectTransform.sizeDelta = new Vector2(34, 34);
            var s = go.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1;
            s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        void ToggleRow(Transform parent, string label, float y, string key)
        {
            var l = Ui.Text(parent, label, label, 26, Palette.Ink, Ui.Display, TextAlignmentOptions.Left);
            l.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(660, 40));
            var go = Ui.Rect("toggle", parent);
            go.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y + 2), new Vector2(84, 44));
            var bg = Ui.Panel(go, "bg", new Color(0, 0, 0, 0.18f), Ui.Rounded(22));
            bg.rectTransform.Stretch();
            var knob = Ui.Panel(go, "knob", Palette.Cream, Ui.Circle);
            knob.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22, 0), new Vector2(36, 36));
            var t = go.gameObject.AddComponent<Toggle>();
            t.targetGraphic = bg;
            t.onValueChanged.AddListener(on =>
            {
                bg.color = on ? Palette.Teal : new Color(0, 0, 0, 0.18f);
                knob.rectTransform.anchoredPosition = new Vector2(on ? 62 : 22, 0);
                switch (key)
                {
                    case "shake": G.Save.ScreenShake = on; G.Rig.ShakeEnabled = on; break;
                    case "motion": G.Save.ReducedMotion = on; Fx.Reduced = on; break;
                    case "grid": G.Save.ShowGrid = on; if (G.Station.Box != null) G.Station.Box.ShowGrid(on); break;
                    case "full": G.Save.Fullscreen = on; Screen.fullScreenMode = on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed; break;
                    case "gfx": G.Save.HighQuality = on; GraphicsQuality.Apply(on); break;
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
                    : key == "gfx" ? G.Save.HighQuality : G.Save.Fullscreen;
                t.isOn = !v;
                t.isOn = v;
            }
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
            var back = Ui.Button(p.transform, "back", "BACK", ShowTitle, Palette.PostalRed, Palette.Cream, 38);
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
