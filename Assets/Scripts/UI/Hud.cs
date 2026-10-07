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
    /// <summary>All in-game screens: packing HUD, journey HUD, results, pause.</summary>
    public sealed class Hud : MonoBehaviour
    {
        public bool Paused;
        public bool Cinematic;      // trailer capture: no skip buttons or timeline
        RectTransform root, packRoot, journeyRoot, resultsRoot, pauseRoot;
        Game G => Game.I;

        // packing widgets
        TextMeshProUGUI orderNum, orderTitle, orderCustomer, orderText, mabelText, budgetText, sealHint, feedbackText;
        Image budgetFill, sticky;
        UiButton sealBtn, undoBtn, redoBtn, clearBtn, bestBtn;
        readonly Image[] bestStars = new Image[3];
        readonly List<(MaterialSlot slot, UiButton btn, TextMeshProUGUI count, Image icon)> slots = new List<(MaterialSlot, UiButton, TextMeshProUGUI, Image)>();
        RectTransform itemCard;
        TextMeshProUGUI cardName, cardBlurb, cardStats;
        Image cardIcon;
        float feedbackT;
        bool escConsumed;

        // journey widgets
        TextMeshProUGUI legBanner, caption, timeText;
        RectTransform timeline, timelineFill, captionRt, legRt;
        float captionT, legT;
        readonly List<GameObject> markers = new List<GameObject>();
        readonly List<(RectTransform rt, Vector3 world, float t)> popups = new List<(RectTransform, Vector3, float)>();

        // results widgets
        TextMeshProUGUI resTitle, resReview, resCustomer, resCost;
        RectTransform resItems, resStars;
        AudioDirector audioDir;

        public void Build(Transform canvas)
        {
            audioDir = AudioDirector.Create(transform);
            UiButton.ClickSound = () => Sfx("click", 0.7f);
            UiButton.HoverSound = () => Sfx("hover", 0.35f);
            root = Ui.Rect("Hud", canvas).Stretch();
            BuildPacking();
            BuildJourney();
            BuildResults();
            BuildPause();
            BuildReveal();
            BuildShiftCard();
            HideAll();
        }

        public void Sfx(string name, float intensity = 1f) => audioDir?.Play(name, intensity);

        void HideAll()
        {
            packRoot.gameObject.SetActive(false);
            journeyRoot.gameObject.SetActive(false);
            resultsRoot.gameObject.SetActive(false);
            pauseRoot.gameObject.SetActive(false);
            revealRoot.gameObject.SetActive(false);
        }

        // ---- shift (chapter) title cards ----------------------------------------------------
        RectTransform shiftCard;
        CanvasGroup shiftGroup;
        TextMeshProUGUI shiftTitle, shiftLine;
        float shiftT = -1f;
        public bool ShiftCardShowing => shiftT >= 0f;
        static readonly string[] ShiftTitles = { "SHIFT 1  \u00B7  FIRST DAY", "SHIFT 2  \u00B7  THE SORTING DEPOT", "SHIFT 3  \u00B7  LAST MILE", "SHIFT 4  \u00B7  EXPRESS SERVICE", "SHIFT 5  \u00B7  OVERTIME" };
        static readonly string[] ShiftLines =
        {
            "Welcome to Mossbury Parcel Post. We ship anything. Carefully.",
            "The depot has a new robot arm. It has no feelings. Pack accordingly.",
            "Meet Dash, our fastest courier. He says stairs are \u201Cbasically a ramp\u201D.",
            "By sea, by air, and by catapult. Yes, catapult. Don't ask.",
            "The rush is over. These are the orders nobody else would take.",
        };

        void BuildShiftCard()
        {
            var dim = Ui.Panel(root, "shiftCard", new Color(0.1f, 0.07f, 0.06f, 0.7f), Ui.Rounded(2));
            shiftCard = dim.rectTransform;
            shiftCard.Stretch();
            var b = dim.gameObject.AddComponent<UiButton>();
            b.Init(dim, () => shiftT = Mathf.Max(shiftT, 2.6f));
            b.HoverScale = 1f;
            var band = Ui.Panel(shiftCard, "band", Palette.PostalRed, Ui.Rounded(8));
            band.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1400, 170));
            shiftTitle = Ui.Text(band.transform, "t", "", 92, Palette.Cream, Ui.Display);
            shiftTitle.rectTransform.Stretch(20, 20, 10, 10);
            var note = Ui.Panel(shiftCard, "note", Palette.Sticky, Ui.Rounded(4));
            note.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(160, -120), new Vector2(760, 120));
            note.rectTransform.localRotation = Quaternion.Euler(0, 0, -2f);
            shiftLine = Ui.Text(note.transform, "l", "", 30, Palette.Ink, Ui.Italic);
            shiftLine.rectTransform.Stretch(20, 20, 12, 30);
            var sig = Ui.Text(note.transform, "s", "\u2014 Mabel", 22, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            sig.rectTransform.Stretch(16, 16, 8, 8);
            shiftGroup = shiftCard.gameObject.AddComponent<CanvasGroup>();
            shiftCard.gameObject.SetActive(false);
        }

        void MaybeShowShift(LevelDef lv)
        {
            bool first = true;
            foreach (var l in Levels.All) if (l.Chapter == lv.Chapter && l.Number < lv.Number) first = false;
            string key = "shift_" + lv.Chapter;
            if (!first || G.Save.SeenTips.Contains(key) || G.Autopilot) return;
            G.Save.SeenTips.Add(key);
            shiftTitle.text = ShiftTitles[Mathf.Clamp(lv.Chapter - 1, 0, ShiftTitles.Length - 1)];
            shiftLine.text = ShiftLines[Mathf.Clamp(lv.Chapter - 1, 0, ShiftLines.Length - 1)];
            shiftCard.gameObject.SetActive(true);
            shiftCard.SetAsLastSibling();
            shiftT = 0f;
            Sfx("stamp_good", 0.8f);
        }

        void UpdateShiftCard(float dt)
        {
            if (shiftT < 0f) return;
            shiftT += dt;
            float a = shiftT < 3.4f ? 1f : Mathf.Clamp01(1f - (shiftT - 3.4f) / 0.4f);
            shiftGroup.alpha = a;
            float s = shiftT < 0.2f ? Mathf.Lerp(1.6f, 1f, shiftT / 0.2f) : 1f;
            shiftTitle.transform.parent.localScale = Vector3.one * s;
            if (a <= 0f) { shiftCard.gameObject.SetActive(false); shiftT = -1f; }
        }

        public void HideAllScreens()
        {
            HideAll();
            Paused = false;
            Time.timeScale = 1f;
        }

        // =================================================================================
        // Reveal stamps
        // =================================================================================

        RectTransform revealRoot;
        readonly List<(RectTransform rt, Vector3 world, float t)> stamps = new List<(RectTransform, Vector3, float)>();

        void BuildReveal()
        {
            revealRoot = Ui.Rect("Reveal", root).Stretch();
            var skip = Ui.Button(revealRoot, "skip", "SKIP  ▶▶", () => G.Reveal.Skip(), Palette.Cream, Palette.Ink, 28);
            skip.Image.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 22), new Vector2(170, 56));
            var hint = Prompt(Ui.Text(revealRoot, "skipKeys", "ENTER / SPACE", 18, Palette.Cream, Ui.Bold, TextAlignmentOptions.Right), "ENTER / SPACE", "A / B");
            hint.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-34, 80), new Vector2(240, 26));
            hint.outlineWidth = 0.25f;
            hint.outlineColor = Palette.Ink;
            hint.raycastTarget = false;
            revealSkipHint = hint;
            revealSkip = skip;
        }

        UiButton revealSkip;
        TextMeshProUGUI revealSkipHint;

        public void HookReveal(RevealController r)
        {
            r.ItemRevealed += (it, world) =>
            {
                var box = Ui.Panel(revealRoot, "stamp", new Color(0, 0, 0, 0), Ui.Rounded(10, 0));
                string word = it.Body < 0 && it.Kind == PieceKind.DragonEgg ? "IT HATCHED!" : (it.Kind == PieceKind.Dragon && it.Status == ItemStatus.Scorched ? "BOX ON FIRE" : StatusWord(it.Status));
                var txt = Ui.Text(box.transform, "t", word, 58, it.Failed ? Palette.Bad : (it.Status == ItemStatus.Perfect ? Palette.Good : Palette.Teal), Ui.Display);
                txt.rectTransform.Stretch();
                txt.outlineWidth = 0.18f;
                txt.outlineColor = Palette.Cream;
                box.rectTransform.sizeDelta = new Vector2(420, 90);
                stamps.Add((box.rectTransform, world, 0f));
            };
        }

        public void ShowReveal()
        {
            HideAll();
            foreach (var st in stamps) if (st.rt != null) Destroy(st.rt.gameObject);
            stamps.Clear();
            revealRoot.gameObject.SetActive(true);
            revealSkip.gameObject.SetActive(!Cinematic);
            revealSkipHint.gameObject.SetActive(!Cinematic);
            AudioDirector.I?.Loop(null, 0);
            AudioDirector.I?.PlayMusic("reveal", 0.8f);
        }

        void UpdateStamps(float dt)
        {
            for (int i = stamps.Count - 1; i >= 0; i--)
            {
                var (rt, world, t) = stamps[i];
                t += dt;
                if (rt == null || t > 1.5f) { if (rt != null) Destroy(rt.gameObject); stamps.RemoveAt(i); continue; }
                stamps[i] = (rt, world, t);
                var sp = G.Rig.Cam.WorldToScreenPoint(world);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var lp);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = lp + new Vector2(260, 30);
                float sc = t < 0.12f ? Mathf.Lerp(2.4f, 1f, t / 0.12f) : 1f;
                rt.localScale = Vector3.one * sc;
                rt.localRotation = Quaternion.Euler(0, 0, -8f);
                var txt = rt.GetComponentInChildren<TextMeshProUGUI>();
                txt.alpha = Mathf.Clamp01((1.5f - t) * 3f);
            }
        }

        // tutorial anchors (canvas-local, centred)
        public Vector2? SlotScreen(MaterialSlot slot)
        {
            foreach (var sl in slots) if (sl.slot == slot && sl.btn.gameObject.activeInHierarchy) return CanvasLocal(sl.btn.Image.rectTransform);
            return null;
        }

        public Vector2? SealScreen() => CanvasLocal(sealBtn.Image.rectTransform);

        Vector2 CanvasLocal(RectTransform rt)
        {
            var wp = rt.TransformPoint(rt.rect.center);
            var sp = RectTransformUtility.WorldToScreenPoint(null, wp);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var lp);
            return lp;
        }

        // =================================================================================
        // Packing
        // =================================================================================

        void BuildPacking()
        {
            packRoot = Ui.Rect("Packing", root).Stretch();

            // order card (top-left)
            var card = Ui.Panel(packRoot, "order", Palette.Cream, Ui.Rounded(14, 3));
            card.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(520, 220));
            Ui.Shadow(card);
            var stripe = Ui.Panel(card.transform, "stripe", Palette.PostalRed, Ui.Rounded(6));
            stripe.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -18), new Vector2(150, 34));
            orderNum = Ui.Text(stripe.transform, "num", "DELIVERY 1", 24, Palette.Cream, Ui.Display);
            orderNum.rectTransform.Stretch();
            orderTitle = Ui.Text(card.transform, "title", "", 50, Palette.Ink, Ui.Display, TextAlignmentOptions.TopLeft);
            orderTitle.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -56), new Vector2(480, 56));
            orderTitle.textWrappingMode = TextWrappingModes.NoWrap;            // long titles shrink to one line
            orderTitle.enableAutoSizing = true; orderTitle.fontSizeMin = 28; orderTitle.fontSizeMax = 50;
            orderCustomer = Ui.Text(card.transform, "cust", "", 22, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.TopLeft);
            orderCustomer.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -112), new Vector2(480, 30));
            orderText = Ui.Text(card.transform, "text", "", 21, Palette.InkSoft, Ui.Italic, TextAlignmentOptions.TopLeft);
            orderText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -144), new Vector2(480, 70));
            routeText = Ui.Text(card.transform, "route", "", 18, Palette.Ink, Ui.Body, TextAlignmentOptions.TopLeft);
            routeText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -212), new Vector2(480, 60));
            orderCard = card.rectTransform;

            // budget (top-right)
            var bud = Ui.Panel(packRoot, "budget", Palette.Cream, Ui.Rounded(14, 3));
            bud.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -24), new Vector2(340, 124));
            Ui.Shadow(bud);
            var bl = Ui.Text(bud.transform, "label", "MATERIALS USED", 22, Palette.InkSoft, Ui.Display, TextAlignmentOptions.TopLeft);
            bl.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(300, 30));
            budgetText = Ui.Text(bud.transform, "value", "0 / par 5", 30, Palette.Ink, Ui.Display, TextAlignmentOptions.TopRight);
            budgetText.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -8), new Vector2(200, 36));
            var track = Ui.Panel(bud.transform, "track", new Color(0, 0, 0, 0.12f), Ui.Rounded(8));
            track.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 44), new Vector2(304, 18));
            expertText = Ui.Text(bud.transform, "expert", "", 19, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomLeft);
            expertText.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 10), new Vector2(304, 26));
            budgetFill = Ui.Panel(track.transform, "fill", Palette.Good, Ui.Rounded(8));
            budgetFill.rectTransform.anchorMin = Vector2.zero;
            budgetFill.rectTransform.anchorMax = new Vector2(0.5f, 1);
            budgetFill.rectTransform.offsetMin = budgetFill.rectTransform.offsetMax = Vector2.zero;

            // Mabel's sticky note
            sticky = Ui.Panel(packRoot, "sticky", Palette.Sticky, Ui.Rounded(4));
            sticky.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -166), new Vector2(330, 150));
            sticky.rectTransform.localRotation = Quaternion.Euler(0, 0, 2.5f);
            Ui.Shadow(sticky, 5, 0.22f);
            mabelText = Ui.Text(sticky.transform, "text", "", 23, Palette.Ink, Ui.Italic, TextAlignmentOptions.TopLeft);
            mabelText.rectTransform.Stretch(18, 16, 16, 34);
            mabelText.enableAutoSizing = true; mabelText.fontSizeMin = 15; mabelText.fontSizeMax = 23;   // hint notes run longer
            var sign = Ui.Text(sticky.transform, "sign", "— Mabel", 20, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            sign.rectTransform.Stretch(16, 16, 10, 10);
            sticky.raycastTarget = false;   // the note grows (FitSticky); it must never hide a shelf item from the mouse

            // Ask Mabel (under the sticky note): escalating hints from her own packing
            hintBtn = Ui.Button(packRoot, "askMabel", "ASK MABEL", AskMabel, Palette.Sticky, Palette.Ink, 26);
            hintBtn.Image.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -332), new Vector2(250, 54));
            hintBtn.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, 2.5f);
            Ui.Shadow(hintBtn.Image, 4, 0.22f);
            KeyHint(hintBtn, "", "Y");
            hintBadge = Ui.Panel(sticky.transform, "hint", Palette.Teal, Ui.Rounded(10));
            hintBadge.rectTransform.Place(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(40, -4), new Vector2(120, 34));
            hintBadgeText = Ui.Text(hintBadge.transform, "t", "HINT 1/4", 22, Palette.Cream, Ui.Display);
            hintBadgeText.rectTransform.Stretch();
            foreach (var gr in sticky.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;

            // toolbar
            var bar = Ui.Panel(packRoot, "toolbar", new Color(0.16f, 0.12f, 0.1f, 0.82f), Ui.Rounded(20));
            bar.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-90, 22), new Vector2(6 * 136 + 24, 150));
            var names = new[] { "PAPER", "BUBBLES", "FOAM", "DIVIDER", "SHELF", "STRAP" };
            for (int i = 0; i < 6; i++)
            {
                var slot = (MaterialSlot)i;
                var b = Ui.Button(bar.transform, names[i], null, () => G.Packing.SelectMaterial(slot), Palette.Cream, Palette.Ink);
                b.Image.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12 + i * 136, 0), new Vector2(128, 128));
                var icon = Ui.Icon(b.transform, "icon", null, Color.white);
                icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(84, 84));
                var nm = Ui.Text(b.transform, "name", names[i], 21, Palette.Ink, Ui.Display);
                nm.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(124, 28));
                var cnt = Ui.Text(b.transform, "count", "x0", 22, Palette.Cream, Ui.Display);
                var cbg = Ui.Panel(b.transform, "countbg", Palette.Ink, Ui.Rounded(12));
                cbg.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(4, 6), new Vector2(46, 30));
                cnt.transform.SetParent(cbg.transform, false);
                cnt.rectTransform.Stretch();
                var key = Prompt(Ui.Text(b.transform, "key", (i + 1).ToString(), 18, new Color(0.3f, 0.25f, 0.2f, 0.6f), Ui.Bold, TextAlignmentOptions.TopLeft), (i + 1).ToString(), "");
                key.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -4), new Vector2(30, 24));
                int idx = i;
                b.OnHover = () => ShowMaterialCard((MaterialSlot)idx);
                slots.Add((slot, b, cnt, icon));
            }

            // gamepad: the shoulders step through the toolbar
            foreach (var (side, label) in new[] { (0f, "LB"), (1f, "RB") })
            {
                var sh = Prompt(Ui.Text(bar.transform, "pad" + label, "", 24, Palette.Cream, Ui.Display), "", label);
                sh.rectTransform.Place(new Vector2(side, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(side == 0f ? -34f : 34f, 0), new Vector2(60, 40));
                sh.outlineWidth = 0.2f; sh.outlineColor = Palette.Ink;
            }

            // seal button (bottom-right)
            sealBtn = Ui.Button(packRoot, "seal", "SEAL & SHIP", () => G.SealAndShip(), Palette.PostalRed, Palette.Cream, 46);
            sealBtn.Image.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 40), new Vector2(330, 110));
            sealBtn.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, -2f);
            KeyHint(sealBtn, "SPACE", "VIEW");
            sealHint = Ui.Text(packRoot, "sealHint", "", 22, Palette.Cream, Ui.Bold, TextAlignmentOptions.Center);
            sealHint.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 154), new Vector2(330, 32));
            var outline = sealHint.gameObject.AddComponent<Shadow>();

            // undo / redo / clear (bottom-left)
            undoBtn = Ui.Button(packRoot, "undo", "UNDO", () => G.Packing.Undo(), Palette.Cream, Palette.Ink, 26);
            undoBtn.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 40), new Vector2(120, 56));
            redoBtn = Ui.Button(packRoot, "redo", "REDO", () => G.Packing.Redo(), Palette.Cream, Palette.Ink, 26);
            KeyHint(undoBtn, "Z", "LT", 14f);
            KeyHint(redoBtn, "Y", "RT", 14f);
            redoBtn.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(156, 40), new Vector2(120, 56));
            clearBtn = Ui.Button(packRoot, "clear", "EMPTY BOX", () => G.Packing.ClearAll(), Palette.Cream, Palette.Ink, 26);
            clearBtn.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 104), new Vector2(248, 56));
            // MY BEST: the best packing shipped for this delivery, back in the box (undoable)
            bestBtn = Ui.Button(packRoot, "myBest", "MY BEST", LoadBest, Palette.Cream, Palette.Ink, 26);
            bestBtn.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 168), new Vector2(248, 56));
            var bestLabel = bestBtn.Label;
            bestLabel.rectTransform.Stretch(14, 100, 2, 4);
            bestLabel.alignment = TextAlignmentOptions.Left;
            for (int i = 0; i < 3; i++)
            {
                bestStars[i] = Ui.Icon(bestBtn.transform, "star" + i, Ui.Star, Palette.Gold);
                bestStars[i].rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-82 + i * 30, 0), new Vector2(28, 28));
                bestStars[i].raycastTarget = false;
            }
            bestBtn.gameObject.SetActive(false);

            // item card (left)
            var ic = Ui.Panel(packRoot, "itemCard", Palette.Paper, Ui.Rounded(14, 3));
            itemCard = ic.rectTransform;
            itemCard.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 10), new Vector2(380, 250));
            Ui.Shadow(ic);
            cardIcon = Ui.Icon(itemCard, "icon", null, Color.white);
            cardIcon.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -6), new Vector2(110, 110));
            cardName = Ui.Text(itemCard, "name", "", 32, Palette.Ink, Ui.Display, TextAlignmentOptions.TopLeft);
            cardName.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(124, -16), new Vector2(240, 96));
            cardBlurb = Ui.Text(itemCard, "blurb", "", 22, Palette.InkSoft, Ui.Body, TextAlignmentOptions.TopLeft);
            cardBlurb.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -122), new Vector2(344, 80));
            cardStats = Ui.Text(itemCard, "stats", "", 20, Palette.PostalRedDark, Ui.Bold, TextAlignmentOptions.TopLeft);
            cardStats.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -204), new Vector2(344, 40));
            // the card overlaps the left column of wide boxes: let clicks through to the box
            foreach (var gr in itemCard.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
            itemCard.gameObject.SetActive(false);

            // last trip report (under the order card)
            var lt = Ui.Panel(packRoot, "lastTrip", Palette.Paper, Ui.Rounded(12, 3));
            lastTrip = lt.rectTransform;
            lastTrip.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -262), new Vector2(520, 150));
            Ui.Shadow(lt);
            var lth = Ui.Text(lastTrip, "h", "LAST TRIP", 22, Palette.PostalRedDark, Ui.Display, TextAlignmentOptions.TopLeft);
            lth.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -10), new Vector2(480, 28));
            lastTripText = Ui.Text(lastTrip, "t", "", 20, Palette.Ink, Ui.Body, TextAlignmentOptions.TopLeft);
            lastTripText.rectTransform.Stretch(18, 14, 40, 10);
            newBadge = Ui.Panel(sticky.transform, "new", Palette.PostalRed, Ui.Rounded(10));
            newBadge.rectTransform.Place(new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(18, -4), new Vector2(84, 34));
            var nb = Ui.Text(newBadge.transform, "t", "NEW!", 24, Palette.Cream, Ui.Display);
            nb.rectTransform.Stretch();

            // feedback toast
            feedbackText = Ui.Text(packRoot, "feedback", "", 30, Palette.Cream, Ui.Display);
            feedbackText.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-90, 190), new Vector2(800, 44));
            feedbackText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
        }

        RectTransform lastTrip, orderCard;
        UiButton hintBtn;
        TextMeshProUGUI expertText, resExpert;
        Image hintBadge;
        TextMeshProUGUI hintBadgeText;
        public UiButton HintButton => hintBtn;

        void AskMabel()
        {
            var lv = G.Level;
            if (lv == null || G.Phase != Phase.Packing) return;
            var rec = G.Save.Get(lv.Number, true);
            if (rec.HintStage < Hints.MaxStage)
            {
                if (rec.HintStage == 0 || rec.HintFocus < 0) rec.HintFocus = (int)Hints.Focus(lv, G.LastRun);
                rec.HintStage++;
                rec.HintsHidden = false;
                Sfx("note", 0.8f);
                packT = 0;   // the sticky note pops again
            }
            else rec.HintsHidden = !rec.HintsHidden;
            G.Save.Write();
            RefreshHints(lv);
        }

        void RefreshHints(LevelDef lv)
        {
            var rec = G.Save.Get(lv.Number);
            int stage = rec?.HintStage ?? 0;
            hintBtn.gameObject.SetActive(G.Save.HintsAvailable(lv.Number) && !Cinematic);
            hintBadge.gameObject.SetActive(stage > 0);
            mabelText.rectTransform.Stretch(18, 16, stage > 0 ? 34 : 16, 34);   // clear of the HINT badge
            if (stage == 0)
            {
                mabelText.text = lv.Mabel;
                hintBtn.Label.text = "ASK MABEL";
                G.Packing.ShowHints(0, lv.Items[0], false);
                return;
            }
            var focus = rec.HintFocus >= 0 ? (PieceKind)rec.HintFocus : lv.Items[0];
            newBadge.gameObject.SetActive(false);
            hintBadgeText.text = $"HINT {stage}/{Hints.MaxStage}";
            mabelText.text = Hints.Note(lv, stage, focus);
            hintBtn.Label.text = stage < Hints.MaxStage ? "ANOTHER HINT" : (rec.HintsHidden ? "SHOW HINTS" : "HIDE HINTS");
            G.Packing.ShowHints(stage, focus, !rec.HintsHidden);
        }
        // ---- Mabel's note grows to fit -------------------------------------------------------------------
        const float NoteW = 330f, NoteH = 150f, NoteTop = -166f, NoteToButton = 16f;
        /// <summary>The smallest size a note may shrink to before the note grows instead (LARGER TEXT: 30% more).</summary>
        public static float NoteReadable => TextScale.Larger ? 19f * TextScale.Grow : 19f;
        string fittedText;
        bool fittedLarger;
        float fittedCanvasH;

        /// <summary>
        /// Long notes (Mabel's hints) used to shrink to fit the sticky note. Now the note gets taller
        /// until its text fits at a readable size, and ASK MABEL moves down with it. Short notes keep
        /// the normal note.
        /// </summary>
        void FitSticky()
        {
            float canvasH = ((RectTransform)root).rect.height;
            // "— Mabel" grows with LARGER TEXT too: keep the note's text clear of it
            var mo = mabelText.rectTransform.offsetMin;
            float bottom = TextScale.Larger ? 42f : 34f;
            if (!Mathf.Approximately(mo.y, bottom)) { mabelText.rectTransform.offsetMin = new Vector2(mo.x, bottom); fittedText = null; }
            if (fittedText == mabelText.text && fittedLarger == TextScale.Larger && Mathf.Approximately(fittedCanvasH, canvasH)) return;
            fittedText = mabelText.text; fittedLarger = TextScale.Larger; fittedCanvasH = canvasH;
            var m = mabelText.rectTransform;
            float insetV = -m.offsetMax.y + m.offsetMin.y, insetH = m.offsetMin.x - m.offsetMax.x;
            // measure at the readable size, with auto-size off for the measurement
            bool auto = mabelText.enableAutoSizing; float size = mabelText.fontSize;
            mabelText.enableAutoSizing = false;
            mabelText.fontSize = NoteReadable;
            float need = mabelText.GetPreferredValues(mabelText.text, NoteW - insetH, 0).y + insetV + 4f;
            mabelText.enableAutoSizing = auto; mabelText.fontSize = size;
            // keep clear of the seal button and its hint at the bottom right (they reach about 200 up)
            float room = canvasH + NoteTop - NoteToButton - 54f - 230f;
            float h = Mathf.Clamp(Mathf.Ceil(need), NoteH, Mathf.Max(NoteH, room));
            sticky.rectTransform.sizeDelta = new Vector2(NoteW, h);
            hintBtn.Image.rectTransform.anchoredPosition = new Vector2(-40, NoteTop - h - NoteToButton);
            mabelText.ForceMeshUpdate();
        }

        CanvasGroup stickyFade;
        /// <summary>The note fades while the pointer is over it, so a shelf item behind a tall note stays in view.</summary>
        void PeekUnderSticky(float dt)
        {
            if (stickyFade == null) stickyFade = sticky.gameObject.AddComponent<CanvasGroup>();
            Vector2 p = PadPrompts ? PadInput.I.CursorPosition : (Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(-1, -1));
            bool over = RectTransformUtility.RectangleContainsScreenPoint(sticky.rectTransform, p, null);
            stickyFade.alpha = Mathf.MoveTowards(stickyFade.alpha, over ? 0.3f : 1f, dt * 6f);
        }
        public float StickyAlpha => stickyFade != null ? stickyFade.alpha : 1f;

        /// <summary>For the self-test: show a note, fit it, and report its font size and the note's height.</summary>
        public (float size, float height) FitNoteForTest(string text)
        {
            mabelText.text = text;
            FitSticky();
            mabelText.ForceMeshUpdate();
            return (mabelText.fontSize, sticky.rectTransform.rect.height);
        }
        public RectTransform StickyRect => sticky.rectTransform;
        public RectTransform BudgetRect => budgetText.transform.parent as RectTransform;
        public float NoteFontSize => mabelText.fontSize;
        public string NoteText => mabelText.text;

        bool wasReady;
        TextMeshProUGUI routeText;

        static string RouteSummary(LevelDef lv)
        {
            var parts = new List<string>();
            foreach (var leg in lv.Route.Legs)
            {
                var evs = new List<string>();
                foreach (var e in leg.Events)
                {
                    string n = null;
                    switch (e.Kind)
                    {
                        case EventKind.Brake: n = "hard brake"; break;
                        case EventKind.Pothole: n = "pothole"; break;
                        case EventKind.SpeedBump: n = "speed bump"; break;
                        case EventKind.Bump: n = "bumps"; break;
                        case EventKind.Cobbles: n = "cobbles"; break;
                        case EventKind.Drop: n = e.Label == "SET DOWN" ? null : "belt drop"; break;
                        case EventKind.ArmTip: n = "robot arm"; break;
                        case EventKind.Chute: n = "chute"; break;
                        case EventKind.Stairs: n = "stairs"; break;
                        case EventKind.Toss: n = "toss"; break;
                        case EventKind.Rock: n = "rocking"; break;
                        case EventKind.WaveSlam: n = "big wave"; break;
                        case EventKind.Turbulence: n = "turbulence"; break;
                        case EventKind.AirPocket: n = "air pocket"; break;
                        case EventKind.Launch: n = "launch"; break;
                        case EventKind.HayLand: n = "landing"; break;
                    }
                    if (n != null && !evs.Contains(n)) evs.Add(n);
                }
                string legName = leg.Kind == LegKind.Van ? "Van" : leg.Kind == LegKind.Depot ? "Depot" : leg.Kind == LegKind.Doorstep ? "Doorstep" :
                                 leg.Kind == LegKind.Ship ? "Ferry" : leg.Kind == LegKind.Plane ? "Air mail" : "Catapult";
                parts.Add($"<b>{legName}</b> <color=#6A5A4A>({string.Join(", ", evs)})</color>");
            }
            return "<b>ROUTE</b>  " + string.Join("  \u2192  ", parts);
        }
        TextMeshProUGUI lastTripText;
        Image newBadge;
        float packT;

        public static string EventName(Recording rec, int leg, int ev)
        {
            if (rec == null || leg < 0 || leg >= rec.Kin.Route.Legs.Count) return "the trip";
            var l = rec.Kin.Route.Legs[leg];
            if (ev < 0 || ev >= l.Events.Count) return "the end of the trip";
            switch (l.Events[ev].Kind)
            {
                case EventKind.Depart: return "pulling away";
                case EventKind.Brake: return "the hard brake";
                case EventKind.Bump: return "a bump";
                case EventKind.SpeedBump: return "the speed bump";
                case EventKind.Pothole: return "the pothole";
                case EventKind.Cobbles: return "the cobblestones";
                case EventKind.Hill: return "the hill";
                case EventKind.Conveyor: return "the conveyor";
                case EventKind.Drop: return "the belt drop";
                case EventKind.ArmTip: return "the robot arm";
                case EventKind.Chute: return "the chute";
                case EventKind.Stairs: return "the stairs";
                case EventKind.Toss: return "the porch toss";
                case EventKind.Righting: return "the pick-up";
                case EventKind.Rock: return "the rough seas";
                case EventKind.WaveSlam: return "the big wave";
                case EventKind.Turbulence: return "the turbulence";
                case EventKind.AirPocket: return "the air pocket";
                case EventKind.Launch: return "the launch";
                case EventKind.Flight: return "the flight";
                case EventKind.HayLand: return "the landing";
            }
            return "the trip";
        }

        void FillLastTrip(Recording rec)
        {
            if (rec == null) { lastTrip.gameObject.SetActive(false); return; }
            var lines = new List<string>();
            foreach (var inc in rec.Incidents)
            {
                if (!inc.IsFailure || lines.Count >= 3) continue;
                var kind = rec.Bodies[inc.Body].Kind;
                string what = Catalog.Get(kind).Name;
                string how = inc.Kind == IncidentKind.Stuck ? "stuck to the other magnet" : StatusWordForIncident(inc.Kind).ToLowerInvariant();
                string detail = inc.Limit > 0 && (inc.Kind == IncidentKind.Broke || inc.Kind == IncidentKind.Woke || inc.Kind == IncidentKind.Squished) ? $" (jolt {inc.Value:0.#}/{inc.Limit:0.#})" : "";
                lines.Add($"{Glyphs.Cross} {what} {how} at {EventName(rec, inc.Leg, inc.Event)}{detail}");
            }
            if (lines.Count == 0)
            {
                foreach (var it in rec.Outcome.Items)
                {
                    if (it.Care < SimConst.CareFraction || lines.Count >= 3) continue;
                    lines.Add($"<color=#B07A1A>!</color> {Catalog.Get(it.Kind).Name} rattled ({it.Care * 100:0}%) at {EventName(rec, it.PeakLeg, it.PeakEvent)}");
                }
                if (lines.Count == 0) lines.Add($"{Glyphs.Check} Everything arrived calm and happy.");
            }
            lastTrip.gameObject.SetActive(true);
            lastTripText.text = string.Join("\n", lines);
            lastTrip.sizeDelta = new Vector2(520, 52 + lines.Count * 30);
        }

        static string StatusWordForIncident(IncidentKind k)
        {
            switch (k)
            {
                case IncidentKind.Broke: return "SHATTERED";
                case IncidentKind.Woke: return "WOKE UP";
                case IncidentKind.Spilled: return "SPILLED";
                case IncidentKind.Popped: return "POPPED";
                case IncidentKind.Melted: return "MELTED";
                case IncidentKind.Squished: return "GOT SQUISHED";
                case IncidentKind.Scorched: return "GOT SCORCHED";
                case IncidentKind.Chilled: return "GOT COLD";
                case IncidentKind.Stuck: return "STUCK";
                case IncidentKind.BoxScorched: return "SET THE BOX ON FIRE";
            }
            return k.ToString().ToUpperInvariant();
        }

        public void ShowPacking(LevelDef lv)
        {
            HideAll();
            packRoot.gameObject.SetActive(true);
            FillLastTrip(G.LastRun);
            bool isNew = lv.NewThing != null && !G.Save.SeenTips.Contains("new_" + lv.NewThing);
            newBadge.gameObject.SetActive(isNew);
            if (isNew) { G.Save.SeenTips.Add("new_" + lv.NewThing); }
            packT = 0;
            MaybeShowShift(lv);
            orderNum.text = $"DELIVERY {lv.Number}";
            orderTitle.text = lv.Title.ToUpperInvariant();
            orderCustomer.text = "To: " + lv.Customer;
            orderText.text = "\u201C" + lv.Order + "\u201D";
            routeText.text = RouteSummary(lv);
            orderCard.sizeDelta = new Vector2(520, 282);
            lastTrip.anchoredPosition = new Vector2(28, -322);
            mabelText.text = lv.Mabel;
            RefreshHints(lv);
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                s.icon.sprite = MaterialIcon(s.slot);
                bool any = lv.Materials.Get(s.slot) > 0;
                s.btn.gameObject.SetActive(any);
            }
            // reflow visible slots
            int vis = 0;
            foreach (var s in slots) if (s.btn.gameObject.activeSelf) s.btn.Image.rectTransform.anchoredPosition = new Vector2(12 + vis++ * 136, 0);
            var bar = slots[0].btn.transform.parent as RectTransform;
            bar.sizeDelta = new Vector2(Mathf.Max(1, vis) * 136 + 16, 150);
            wasReady = true;
            G.Packing.Changed -= RefreshPacking;
            G.Packing.Changed += RefreshPacking;
            G.Packing.Hovered -= ShowItemCard;
            G.Packing.Hovered += ShowItemCard;
            G.Packing.Feedback -= ShowFeedback;
            G.Packing.Feedback += ShowFeedback;
            RefreshPacking();
            AudioDirector.I?.PlayMusic("packing");
            AudioDirector.I?.Loop(null, 0);
        }

        static Sprite MaterialIcon(MaterialSlot s)
        {
            switch (s)
            {
                case MaterialSlot.Paper: return IconStudio.Piece(PieceKind.Paper);
                case MaterialSlot.Bubble: return IconStudio.Piece(PieceKind.Bubble);
                case MaterialSlot.Foam: return IconStudio.Piece(PieceKind.Foam);
            }
            string id = s == MaterialSlot.Divider ? "divider" : (s == MaterialSlot.Shelf ? "shelf" : "strap");
            if (IconStudio.Has("mat_" + id)) return IconStudio.Cached("mat_" + id);
            var go = ModelLibrary.Spawn(id + "_icon", null) ?? ModelLibrary.Spawn(id, null);
            if (go == null)
            {
                go = new GameObject(id);
                var c = s == MaterialSlot.Strap ? Palette.Hex("2E4A7A") : Palette.KraftDark;
                var size = s == MaterialSlot.Divider ? new Vector3(0.04f, 0.5f, 0.3f) : (s == MaterialSlot.Shelf ? new Vector3(0.5f, 0.04f, 0.3f) : new Vector3(0.4f, 0.08f, 0.3f));
                MeshGen.Make("m", MeshGen.RoundedBox(size, 0.01f), Mat.Lit(c, 0.3f), go.transform);
            }
            return IconStudio.Object3D("mat_" + id, go);
        }

        void RefreshPacking()
        {
            var pc = G.Packing;
            if (pc.Level == null) return;
            var lv = pc.Level;
            int cost = pc.Pk.Cost;
            budgetText.text = $"{cost} <size=70%>/ par {lv.Par}</size>";
            expertText.text = G.Save.IsExpert(lv.Number) ? $"MABEL'S BEST  {lv.Expert}   {Glyphs.Check} <color=#2E8B57>MATCHED</color>" : $"MABEL'S BEST  {lv.Expert}";
            float frac = Mathf.Clamp01(cost / (float)Mathf.Max(1, lv.Par * 1.5f));
            budgetFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, frac), 1);
            budgetFill.color = cost <= lv.Par ? Palette.Good : Palette.Bad;
            foreach (var s in slots)
            {
                int left = pc.Remaining(s.slot);
                s.count.text = left.ToString();
                bool active = (pc.Tool == Tool.Padding && SlotFor(pc.HeldKind) == s.slot) ||
                              (pc.Tool == Tool.Divider && s.slot == MaterialSlot.Divider) ||
                              (pc.Tool == Tool.Shelf && s.slot == MaterialSlot.Shelf) ||
                              (pc.Tool == Tool.Strap && s.slot == MaterialSlot.Strap);
                s.btn.SetColor(active ? Palette.Sticky : Palette.Cream);
                s.btn.HoverScale = 1.06f;
                var ap = s.btn.Image.rectTransform.anchoredPosition;
                s.btn.Image.rectTransform.anchoredPosition = new Vector2(ap.x, active ? 10 : 0);
            }
            int remaining = pc.RemainingItems().Count;
            bool ready = pc.ReadyToSeal && pc.Tool == Tool.None;
            sealBtn.SetInteractable(pc.ReadyToSeal);
            if (pc.ReadyToSeal && !wasReady) { sealBtn.Pulse(); Sfx("stamp_ok", 0.5f); }
            wasReady = pc.ReadyToSeal;
            sealHint.text = remaining > 0 ? $"Pack {remaining} more item{(remaining > 1 ? "s" : "")}" : (ready ? (PadPrompts ? PadGlyphs.Format("{View} to seal") : "Space to seal") : "");
            RefreshBest(lv);
        }

        void LoadBest() => G.Packing.LoadPacking(G.Save.GetBestPacking(G.Packing.Level));

        /// <summary>MY BEST shows when a best packing exists and the box holds something else.</summary>
        void RefreshBest(LevelDef lv)
        {
            var r = G.Save.Get(lv.Number);
            bool show = r != null && !string.IsNullOrEmpty(r.BestPacking) && r.BestPacking != SaveData.Serialize(G.Packing.Pk);
            bestBtn.gameObject.SetActive(show);
            if (!show) return;
            for (int i = 0; i < 3; i++) bestStars[i].color = i < r.BestPackingStars ? Palette.Gold : new Color(0, 0, 0, 0.13f);
        }

        public UiButton BestButton => bestBtn;
        public UiButton ClearButton => clearBtn;
        /// <summary>For the self-tests: what the first prompt written with this pad button shows now.</summary>
        public string PromptFor(string pad)
        {
            foreach (var (t, kb, pd) in prompts) if (pd == pad && t != null) return t.text;
            return null;
        }
        /// <summary>For the self-tests: the sprites the first prompt for this pad button draws.</summary>
        public string PromptDrawn(string pad)
        {
            foreach (var (t, kb, pd) in prompts) if (pd == pad && t != null) return Glyphs.Drawn(t);
            return null;
        }
        public string LastTripDrawn => Glyphs.Drawn(lastTripText);
        public string LastTripText => lastTrip.gameObject.activeSelf ? lastTripText.text : "";
        public string SealHintText => sealHint.text;

        public string UndoHint => undoBtn.transform.Find("key")?.GetComponent<TextMeshProUGUI>()?.text;
        public string RedoHint => redoBtn.transform.Find("key")?.GetComponent<TextMeshProUGUI>()?.text;

        static MaterialSlot SlotFor(PieceKind k) => k == PieceKind.Paper ? MaterialSlot.Paper : (k == PieceKind.Bubble ? MaterialSlot.Bubble : MaterialSlot.Foam);

        void ShowItemCard(PieceKind? kind)
        {
            if (kind == null) { itemCard.gameObject.SetActive(false); return; }
            var def = Catalog.Get(kind.Value);
            itemCard.gameObject.SetActive(true);
            cardIcon.sprite = IconStudio.Piece(kind.Value);
            cardName.text = def.Name.ToUpperInvariant();
            cardBlurb.text = def.Blurb;
            var stats = new List<string>();
            if (def.JoltLimit > 0 && !def.IsPadding) stats.Add($"JOLT LIMIT {def.JoltLimit:0.#}");
            if (def.WakeLimit > 0) stats.Add($"WAKES AT {def.WakeLimit:0.#}");
            if (def.CrushLimit > 0 && def.CrushLimit < 2) stats.Add("NOTHING HEAVY ON TOP");
            if (def.IsPadding) stats.Add($"COST {def.Cost}  ·  SOFTNESS {(1 - def.Hardness) * 100:0}%");
            if (def.Mass >= 2) stats.Add($"HEAVY ({def.Mass:0.#} kg)");
            cardStats.text = string.Join("   ", stats);
        }

        void ShowMaterialCard(MaterialSlot s)
        {
            if (s <= MaterialSlot.Foam) { ShowItemCard(s == MaterialSlot.Paper ? PieceKind.Paper : (s == MaterialSlot.Bubble ? PieceKind.Bubble : PieceKind.Foam)); return; }
            itemCard.gameObject.SetActive(true);
            cardIcon.sprite = MaterialIcon(s);
            switch (s)
            {
                case MaterialSlot.Divider: cardName.text = "DIVIDER"; cardBlurb.text = "A cardboard wall on a grid line. Splits the box into compartments. Blocks heat."; cardStats.text = $"COST {SimConst.DividerCost}"; break;
                case MaterialSlot.Shelf: cardName.text = "SHELF"; cardBlurb.text = "A board across one compartment. Keeps weight off whatever is below."; cardStats.text = $"COST {SimConst.ShelfCost}"; break;
                default: cardName.text = "STRAP"; cardBlurb.text = "Holds an item fixed to the box. Snaps if the load is too much."; cardStats.text = $"COST {SimConst.StrapCost}"; break;
            }
        }

        void ShowFeedback(string msg)
        {
            feedbackText.text = msg;
            feedbackT = 1.6f;
        }

        public void ShowSealing()
        {
            HideAll();
            itemCard.gameObject.SetActive(false);
            Sfx("flaps");
            Invoke(nameof(TapeSound), 0.75f);
            AudioDirector.I?.Duck(0.5f, 1.5f);
        }

        void TapeSound() => Sfx("tape");

        // =================================================================================
        // Journey
        // =================================================================================

        void BuildJourney()
        {
            journeyRoot = Ui.Rect("Journey", root).Stretch();
            // dip to black around each leg change (behind the HUD, in front of the scene)
            legDip = Ui.Panel(journeyRoot, "legDip", new Color(0.05f, 0.04f, 0.04f, 1f), Ui.Rounded(2));
            legDip.rectTransform.Stretch();
            legDip.raycastTarget = false;
            legDip.color = new Color(0.05f, 0.04f, 0.04f, 0f);
            var lb = Ui.Panel(journeyRoot, "legBanner", Palette.Ink, Ui.Rounded(12));
            legRt = lb.rectTransform;
            legRt.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(560, 70));
            legBanner = Ui.Text(lb.transform, "text", "", 40, Palette.Cream, Ui.Display);
            legBanner.rectTransform.Stretch();

            var cp = Ui.Rect("caption", journeyRoot);
            captionRt = cp;
            cp.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(900, 120));
            caption = Ui.Text(cp, "text", "", 84, Palette.Sticky, Ui.Display);
            caption.rectTransform.Stretch();
            caption.outlineWidth = 0.22f;
            caption.outlineColor = Palette.Ink;

            var tl = Ui.Panel(journeyRoot, "timeline", new Color(0.1f, 0.08f, 0.07f, 0.65f), Ui.Rounded(10));
            timeline = tl.rectTransform;
            timeline.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(1100, 22));
            var fill = Ui.Panel(tl.transform, "fill", Palette.Sticky, Ui.Rounded(10));
            timelineFill = fill.rectTransform;
            timelineFill.anchorMin = Vector2.zero;
            timelineFill.anchorMax = new Vector2(0, 1);
            timelineFill.offsetMin = timelineFill.offsetMax = Vector2.zero;
            timeText = Ui.Text(journeyRoot, "time", "", 22, Palette.Cream, Ui.Bold, TextAlignmentOptions.Left);
            timeText.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(560, 28), new Vector2(200, 30));

            var skip = Ui.Button(journeyRoot, "skip", "SKIP  ▶▶", () => G.Journey.Skip(), Palette.Cream, Palette.Ink, 28);
            skip.Image.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 22), new Vector2(170, 56));
            KeyHint(skip, "ENTER", "B", 13f);
            skipBtn = skip;

            // replay controls (scrub by clicking the timeline, speed, done)
            replayBar = Ui.Rect("replay", journeyRoot);
            replayBar.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(1100, 64));
            var click = timeline.gameObject.AddComponent<UiButton>();
            click.Init(timeline.GetComponent<Image>(), null);
            click.HoverScale = 1.0f;
            click.OnClick = () =>
            {
                var mp = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                RectTransformUtility.ScreenPointToLocalPointInRectangle(timeline, mp, null, out var lp);
                float frac = Mathf.Clamp01((lp.x + timeline.rect.width * 0.5f) / timeline.rect.width);
                G.Journey.Seek(frac * G.Journey.Duration);
            };
            string[] spd = { "II", "¼×", "½×", "1×", "2×" };
            float[] speeds = { -1, 0.25f, 0.5f, 1f, 2f };
            for (int i = 0; i < spd.Length; i++)
            {
                float v = speeds[i];
                var b = Ui.Button(replayBar, "sp" + i, spd[i], () => { if (v < 0) G.Journey.UserPaused = !G.Journey.UserPaused; else { G.Journey.Speed = v; G.Journey.UserPaused = false; } }, Palette.Cream, Palette.Ink, 26, Ui.Bold);
                b.Image.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 96, 0), new Vector2(88, 52));
            }
            var done = Ui.Button(replayBar, "done", "DONE", () => G.Journey.Skip(), Palette.PostalRed, Palette.Cream, 28);
            done.Image.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(150, 52));
            camBtn = Ui.Button(replayBar, "cam", "CAM: DIRECTOR", null, Palette.Teal, Palette.Cream, 22);
            camBtn.OnClick = () => { G.Journey.CameraMode = (G.Journey.CameraMode + 1) % 3; camBtn.Label.text = "CAM: " + JourneyPlayer.CameraModeNames[G.Journey.CameraMode]; };
            camBtn.Image.rectTransform.Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-160, 0), new Vector2(190, 52));
            var hint = Ui.Text(replayBar, "hint", "Click the timeline to jump  ·  red marks = trouble", 20, Palette.Cream, Ui.Bold, TextAlignmentOptions.Center);
            hint.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, 0), new Vector2(420, 40));
            hint.enableAutoSizing = true; hint.fontSizeMax = 20; hint.fontSizeMin = 14;
            hint.gameObject.AddComponent<Shadow>();
        }

        RectTransform replayBar;
        UiButton skipBtn, camBtn;

        public void ShowJourney(LevelDef lv, Recording rec, bool replay = false)
        {
            HideAll();
            journeyRoot.gameObject.SetActive(true);
            replayBar.gameObject.SetActive(replay);
            skipBtn.gameObject.SetActive(!replay && !Cinematic);
            timeline.gameObject.SetActive(replay || !Cinematic);
            timeText.gameObject.SetActive(replay || !Cinematic);
            foreach (var m in markers) Destroy(m);
            markers.Clear();
            foreach (var inc in rec.Incidents)
            {
                if (!inc.IsFailure) continue;
                var mk = Ui.Panel(timeline, "mark", Palette.Bad, Ui.Rounded(8));
                float x = inc.Time / rec.Duration;
                mk.rectTransform.anchorMin = mk.rectTransform.anchorMax = new Vector2(x, 0.5f);
                mk.rectTransform.sizeDelta = new Vector2(14, 34);
                markers.Add(mk.gameObject);
            }
            // leg separators
            var kin = rec.Kin;
            legCuts.Clear();
            for (int i = 1; i < kin.LegStartTick.Count; i++) legCuts.Add(kin.LegStartTick[i] * SimConst.Dt);
            for (int i = 1; i < kin.LegStartTick.Count; i++)
            {
                var sep = Ui.Panel(timeline, "sep", Palette.Cream, Ui.Rounded(2));
                float x = kin.LegStartTick[i] * SimConst.Dt / rec.Duration;
                sep.rectTransform.anchorMin = sep.rectTransform.anchorMax = new Vector2(x, 0.5f);
                sep.rectTransform.sizeDelta = new Vector2(4, 30);
                markers.Add(sep.gameObject);
            }
            G.Journey.LegChanged -= OnLeg;
            G.Journey.LegChanged += OnLeg;
            G.Journey.Caption -= OnCaption;
            G.Journey.Caption += OnCaption;
            G.Journey.IncidentHappened -= OnIncident;
            G.Journey.IncidentHappened += OnIncident;
            AudioDirector.I?.PlayMusic("journey");
        }

        static readonly Dictionary<LegKind, string> legNames = new Dictionary<LegKind, string>
        {
            { LegKind.Van, "THE VAN" }, { LegKind.Depot, "SORTING DEPOT" }, { LegKind.Doorstep, "LAST MILE" },
            { LegKind.Ship, "THE FERRY" }, { LegKind.Plane, "AIR MAIL" }, { LegKind.Catapult, "EXPRESS CATAPULT" },
        };

        Image legDip;
        readonly List<float> legCuts = new List<float>();
        const float LegDipHalf = 0.22f;   // seconds of playback either side of a leg change

        void OnLeg(int leg)
        {
            if (G.Journey.Rec == null) return;
            var kind = G.Journey.Rec.Kin.Route.Legs[leg].Kind;
            legBanner.text = $"LEG {leg + 1}  ·  {legNames[kind]}";
            legT = 0f;
            Sfx("whoosh", 0.6f);
            AudioDirector.I?.Loop(kind == LegKind.Van ? "engine" : (kind == LegKind.Depot ? "conveyor" : null), 0.35f);
        }

        void OnCaption(string text)
        {
            caption.text = text;
            captionT = 0f;
        }

        void OnIncident(Incident inc)
        {
            string word = null;
            switch (inc.Kind)
            {
                case IncidentKind.Broke: word = "SMASH!"; break;
                case IncidentKind.Squished: word = "SPLAT!"; break;
                case IncidentKind.Popped: word = G.Journey.Rec.Bodies[inc.Body].Kind == PieceKind.Balloon ? "BANG!" : null; break;
                case IncidentKind.Woke: word = "!!"; break;
                case IncidentKind.Spilled: word = "SPILLED!"; break;
                case IncidentKind.Sneezed: word = "ACHOO!"; break;
                case IncidentKind.SneezeWindup: case IncidentKind.Tickled: word = "ah... ah..."; break;
                case IncidentKind.Melted: word = "drip..."; break;
                case IncidentKind.StrapSnapped: word = "SNAP!"; break;
                case IncidentKind.Stuck: word = "CLANK!"; break;
                case IncidentKind.BoxScorched: word = "BOX ON FIRE!"; break;
                case IncidentKind.Scorched: word = "TOASTED"; break;
            }
            if (word == null) return;
            var t = Ui.Text(journeyRoot, "pop", word, 54, inc.IsFailure ? Palette.Bad : Palette.Sticky, Ui.Display);
            t.outlineWidth = 0.25f;
            t.outlineColor = Palette.Ink;
            t.rectTransform.sizeDelta = new Vector2(400, 80);
            popups.Add((t.rectTransform, G.Journey.Box.CellToWorld(inc.Where.x, inc.Where.y + 0.6f), 0f));
        }

        // =================================================================================
        // Results
        // =================================================================================

        void BuildResults()
        {
            resultsRoot = Ui.Rect("Results", root).Stretch();
            var dim = Ui.Panel(resultsRoot, "dim", new Color(0.08f, 0.05f, 0.04f, 0.45f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(resultsRoot, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(980, 760));
            Ui.Shadow(p, 10);
            resTitle = Ui.Text(p.transform, "title", "", 84, Palette.Ink, Ui.Display);
            resTitle.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(900, 100));
            resItems = Ui.Rect("items", p.transform);
            resItems.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(900, 150));
            resStars = Ui.Rect("stars", p.transform);
            resStars.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -310), new Vector2(900, 170));
            var rev = Ui.Panel(p.transform, "review", Palette.Paper, Ui.Rounded(12, 2));
            rev.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -500), new Vector2(860, 130));
            resReview = Ui.Text(rev.transform, "text", "", 28, Palette.Ink, Ui.Italic, TextAlignmentOptions.Left);
            resReview.rectTransform.Stretch(24, 24, 14, 40);
            resCustomer = Ui.Text(rev.transform, "cust", "", 22, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            resCustomer.rectTransform.Stretch(24, 24, 10, 12);
            resExpert = Ui.Text(rev.transform, "expert", "", 20, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomLeft);
            resExpert.rectTransform.Stretch(24, 24, 10, 12);
            resCost = Ui.Text(p.transform, "cost", "", 22, Palette.InkSoft, Ui.Bold);
            resCost.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -478), new Vector2(860, 30));

            repackBtn = Ui.Button(p.transform, "repack", "REPACK", () => G.Repack(), Palette.Teal, Palette.Cream, 40);
            repackBtn.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-300, 34), new Vector2(260, 84));
            KeyHint(repackBtn, "R", "X");
            replayBtn = Ui.Button(p.transform, "replay", "REPLAY", () => G.Replay(), Palette.Ink, Palette.Cream, 40);
            replayBtn.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(260, 84));
            KeyHint(replayBtn, "P", "Y");
            nextBtn = Ui.Button(p.transform, "next", "NEXT ▶", () => G.NextLevel(), Palette.PostalRed, Palette.Cream, 40);
            nextBtn.Image.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(300, 34), new Vector2(260, 84));
            KeyHint(nextBtn, "ENTER", "A");
            var log = Ui.Button(resultsRoot, "log", "DELIVERY LOG", () => G.ShowDeliveryLog(), Palette.Cream, Palette.Ink, 26);
            log.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 28), new Vector2(240, 60));
            var menu = Ui.Button(resultsRoot, "menu", "MAIN MENU", () => G.ShowTitle(), Palette.Cream, Palette.Ink, 26);
            menu.Image.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(280, 28), new Vector2(220, 60));
            resultsPanel = p.rectTransform;
        }

        RectTransform resultsPanel;
        readonly List<(Image img, bool got, float delay)> starAnims = new List<(Image, bool, float)>();
        float resultsT;
        bool confettiPending;

        UiButton nextBtn, repackBtn, replayBtn;

        /// <summary>A small keyboard shortcut in the button's top-left corner, like the toolbar numbers.</summary>
        void KeyHint(UiButton b, string key, string pad, float size = 16f)
        {
            var c = b.Label != null ? b.Label.color : Palette.Cream;
            var t = Ui.Text(b.transform, "key", key, size, new Color(c.r, c.g, c.b, 0.6f), Ui.Bold, TextAlignmentOptions.TopLeft);
            t.rectTransform.Stretch(9, 9, 5, 5);
            t.raycastTarget = false;
            Prompt(t, key, pad);
        }

        // ---- prompts that follow the input device (keyboard and mouse, or gamepad) ----------------------
        readonly List<(TextMeshProUGUI t, string kb, string pad)> prompts = new List<(TextMeshProUGUI, string, string)>();
        bool padPrompts;
        public static bool PadPrompts => PadInput.I != null && PadInput.I.Active;

        TextMeshProUGUI Prompt(TextMeshProUGUI t, string kb, string pad)
        {
            prompts.Add((t, kb, pad));
            t.text = PadPrompts ? PadGlyphs.Label(pad) : KeyLabel(kb);
            return t;
        }

        /// <summary>A one-letter key hint shows the label of the key that does it on this keyboard layout.</summary>
        static string KeyLabel(string kb) => kb != null && kb.Length == 1 && kb[0] >= 'A' && kb[0] <= 'Z' ? Shortcuts.Label(kb[0]) : kb;

        int keysVersion = -1, glyphsVersion = -1;

        void RefreshPrompts()
        {
            padPrompts = PadPrompts;
            keysVersion = Shortcuts.Version;
            glyphsVersion = PadGlyphs.Version;
            foreach (var (t, kb, pad) in prompts) if (t != null) t.text = padPrompts ? PadGlyphs.Label(pad) : KeyLabel(kb);
            if (packRoot.gameObject.activeSelf && G.Packing.Level != null) RefreshPacking();
        }

        /// <summary>Keyboard shortcuts for the unboxing and the results, so a retry never needs the mouse.</summary>
        void UpdateShortcuts(Keyboard kb)
        {
            if (kb == null || Paused || G.Menus.Open || G.FreshPhase) return;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            if (G.Phase == Phase.Reveal && revealRoot.gameObject.activeSelf && revealSkip.gameObject.activeSelf)
            {
                if (enter || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) revealSkip.Press();
            }
            else if (G.Phase == Phase.Results && resultsRoot.gameObject.activeSelf)
            {
                if (Shortcuts.Pressed(kb, 'r')) repackBtn.Press();
                else if (Shortcuts.Pressed(kb, 'p')) replayBtn.Press();
                else if (enter) nextBtn.Press();
            }
        }

        public void ShowResults(LevelDef lv, Recording rec)
        {
            HideAll();
            resultsRoot.gameObject.SetActive(true);
            AudioDirector.I?.Loop(null, 0);
            var o = rec.Outcome;
            resTitle.text = o.Delivered ? (o.Stars == 3 ? "PERFECT DELIVERY!" : "DELIVERED!") : "DAMAGED IN TRANSIT";
            resTitle.color = o.Delivered ? Palette.TealDark : Palette.PostalRedDark;
            foreach (Transform c in resItems) Destroy(c.gameObject);
            foreach (Transform c in resStars) Destroy(c.gameObject);
            int n = o.Items.Count;
            for (int i = 0; i < n; i++)
            {
                var it = o.Items[i];
                var cell = Ui.Rect("item", resItems);
                float w = Mathf.Min(170, 900f / n);
                cell.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - (n - 1) * 0.5f) * w, 0), new Vector2(w - 10, 150));
                var icon = Ui.Icon(cell, "icon", IconStudio.Piece(it.Kind), Color.white);
                icon.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 4), new Vector2(122, 122));
                var st = Ui.Text(cell, "status", it.Kind == PieceKind.Dragon && it.Status == ItemStatus.Scorched ? "BOX ON FIRE" : StatusWord(it.Status), 24, it.Failed ? Palette.Bad : Palette.Good, Ui.Display);
                st.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(w, 34));
                st.rectTransform.localRotation = Quaternion.Euler(0, 0, -6);
            }
            starAnims.Clear();
            resultsT = 0;
            string[] labels = { "DELIVERED", $"UNDER BUDGET  {o.Cost} / PAR {o.Par}", o.WorstCare >= 1f ? "HANDLED WITH CARE  (OVER THE LIMIT)" : $"HANDLED WITH CARE  {o.WorstCare * 100:0}% / {SimConst.CareFraction * 100:0}%" };
            bool[] got = { o.Delivered, o.Delivered && o.UnderBudget, o.Delivered && o.Careful };
            for (int i = 0; i < 3; i++)
            {
                var cell = Ui.Rect("star", resStars);
                cell.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 290, 0), new Vector2(280, 170));
                var back = Ui.Icon(cell, "sb", Ui.Star, new Color(0, 0, 0, 0.12f));
                back.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(110, 110));
                var star = Ui.Icon(cell, "s", Ui.Star, Palette.Gold);
                star.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(110, 110));
                star.transform.localScale = Vector3.zero;
                starAnims.Add((star, got[i], 0.5f + i * 0.35f));
                var lab = Ui.Text(cell, "l", labels[i], 20, got[i] ? Palette.Ink : new Color(0.3f, 0.25f, 0.2f, 0.5f), Ui.Display);
                lab.rectTransform.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(280, 50));
            }
            resReview.text = "“" + Review(lv, rec) + "”";
            resCustomer.text = "— " + lv.Customer;
            resCost.text = "";
            bool expert = o.Stars == 3 && o.Cost <= lv.Expert;
            resExpert.text = expert ? $"<color=#1F7A6F>EXPERT!</color>  Matched Mabel's best ({lv.Expert})" : (o.Delivered ? $"Mabel's best: {lv.Expert}" : "");
            nextBtn.SetInteractable(o.Delivered || G.Save.IsDelivered(lv.Number));
            Sfx(o.Delivered ? (o.Stars == 3 ? "fanfare" : "success") : "fail");
            AudioDirector.I?.Duck(0.6f, 1.6f);
            confettiPending = o.Stars == 3;
            G.Post.SetDof(0.85f, 1.2f);
        }

        public static string StatusWord(ItemStatus s)
        {
            switch (s)
            {
                case ItemStatus.Perfect: return "PERFECT";
                case ItemStatus.Fine: return "OK";
                case ItemStatus.Broken: return "SHATTERED";
                case ItemStatus.Spilled: return "SPILLED";
                case ItemStatus.Awake: return "WIDE AWAKE";
                case ItemStatus.Melted: return "MELTED";
                case ItemStatus.Popped: return "POPPED";
                case ItemStatus.Scorched: return "SCORCHED";
                case ItemStatus.Squished: return "SQUISHED";
                case ItemStatus.Chilled: return "TOO COLD";
                case ItemStatus.Burned: return "BURNED";
                case ItemStatus.Stuck: return "STUCK TOGETHER";
            }
            return s.ToString();
        }

        static string Review(LevelDef lv, Recording rec)
        {
            var o = rec.Outcome;
            if (o.Delivered) return o.Careful ? lv.ReviewGood : lv.ReviewGood + " A little rattled, mind.";
            return lv.ReviewBad;
        }

        // =================================================================================
        // Pause
        // =================================================================================

        void BuildPause()
        {
            pauseRoot = Ui.Rect("Pause", root).Stretch();
            var dim = Ui.Panel(pauseRoot, "dim", new Color(0.05f, 0.03f, 0.02f, 0.6f), Ui.Rounded(2));
            dim.rectTransform.Stretch();
            var p = Ui.Panel(pauseRoot, "panel", Palette.Cream, Ui.Rounded(18, 4));
            p.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 520));
            var t = Ui.Text(p.transform, "t", "PAUSED", 72, Palette.Ink, Ui.Display);
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(480, 90));
            p.rectTransform.sizeDelta = new Vector2(520, 680);
            string[] labels = { "RESUME", "RESTART DELIVERY", "SETTINGS", "DELIVERY LOG", "MAIN MENU" };
            System.Action[] acts = {
                () => SetPaused(false),
                () => { SetPaused(false); G.Repack(); },
                () => { pauseRoot.gameObject.SetActive(false); G.Menus.ShowSettings(() => { pauseRoot.gameObject.SetActive(true); }); },
                () => { pauseRoot.gameObject.SetActive(false); G.ShowDeliveryLog(); },
                () => { SetPaused(false); G.ShowTitle(); },
            };
            for (int i = 0; i < labels.Length; i++)
            {
                var b = Ui.Button(p.transform, labels[i], labels[i], acts[i], i == 0 ? Palette.PostalRed : Palette.Ink, Palette.Cream, 34);
                if (i == 0) resumeBtn = b;
                b.Image.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -140 - i * 98), new Vector2(400, 78));
            }
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            pauseRoot.gameObject.SetActive(p);
            pauseRoot.SetAsLastSibling();
            Time.timeScale = p ? 0f : 1f;
            AudioDirector.I?.Muffle(p);
        }

        public void ConsumeEscape() { escConsumed = true; }

        // ---- for gamepad navigation ----------------------------------------------------------------
        UiButton resumeBtn;
        public RectTransform Root => root;
        public RectTransform PauseRoot => pauseRoot;
        public bool ResultsShowing => resultsRoot.gameObject.activeSelf;
        public bool RevealShowing => revealRoot.gameObject.activeSelf;
        public UiButton RevealSkipButton => revealSkip;
        public UiButton NextButton => nextBtn;
        public UiButton RepackButton => repackBtn;
        public UiButton ReplayButton => replayBtn;
        public UiButton ResumeButton => resumeBtn;
        public UiButton SealButton => sealBtn;

        // =================================================================================

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (!escConsumed && !G.Menus.Open && (G.Phase == Phase.Packing || G.Phase == Phase.Journey)) SetPaused(!Paused);
            }
            escConsumed = false;
            UpdateShortcuts(kb);
            Shortcuts.Refresh(Keyboard.current);
            PadGlyphs.Refresh(G.Save.ButtonIcons);
            if (PadPrompts != padPrompts || Shortcuts.Version != keysVersion || PadGlyphs.Version != glyphsVersion) RefreshPrompts();
            if (camBtn != null && camBtn.gameObject.activeInHierarchy) camBtn.Label.text = "CAM: " + JourneyPlayer.CameraModeNames[G.Journey.CameraMode];

            float dt = Clock.UnscaledDelta;
            UpdateStamps(dt);
            UpdateShiftCard(dt);
            if (packRoot.gameObject.activeSelf)
            {
                FitSticky();
                PeekUnderSticky(dt);
                packT += dt;
                float s = packT < 0.25f ? Mathf.Lerp(0.85f, 1f, 1f - Mathf.Pow(1f - packT / 0.25f, 3f)) : 1f;
                sticky.rectTransform.localScale = Vector3.one * s;
                if (newBadge.gameObject.activeSelf) newBadge.rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(packT * 6f) * 0.08f);
            }
            if (resultsRoot.gameObject.activeSelf)
            {
                resultsT += dt;
                float pk = Mathf.Clamp01(resultsT / 0.3f);
                resultsPanel.localScale = Vector3.one * (pk < 1f ? Mathf.Lerp(0.8f, 1f, 1f - Mathf.Pow(1f - pk, 3f)) : 1f);
                for (int i = 0; i < starAnims.Count; i++)
                {
                    var (img, got, delay) = starAnims[i];
                    if (!got) continue;
                    float k = (resultsT - delay) / 0.25f;
                    if (k < 0) continue;
                    if (img.transform.localScale.x == 0f) Sfx("star_" + (i + 1), 0.9f);
                    float sc = k < 1f ? Mathf.Lerp(2.2f, 1f, 1f - Mathf.Pow(1f - k, 2f)) : 1f;
                    img.transform.localScale = Vector3.one * Mathf.Max(0.001f, sc);
                    img.transform.localRotation = Quaternion.Euler(0, 0, k < 1f ? (1f - k) * 30f : 0);
                }
                if (confettiPending && resultsT > 1.3f)
                {
                    confettiPending = false;
                    Fx.Confetti(G.Rig.Cam.transform.position + G.Rig.Cam.transform.forward * 2f + Vector3.down * 0.8f, 1f);
                    Sfx("fanfare_tail", 0.8f);
                }
            }
            if (feedbackT > 0)
            {
                feedbackT -= dt;
                feedbackText.alpha = Mathf.Clamp01(feedbackT * 2f);
            }
            else feedbackText.alpha = 0;

            if (journeyRoot.gameObject.activeSelf && G.Journey.Rec != null)
            {
                float frac = G.Journey.T / Mathf.Max(0.01f, G.Journey.Duration);
                float near = float.MaxValue;
                foreach (var cut in legCuts) near = Mathf.Min(near, Mathf.Abs(G.Journey.T - cut));
                float dip = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - near / LegDipHalf));
                legDip.color = new Color(0.05f, 0.04f, 0.04f, dip);
                timelineFill.anchorMax = new Vector2(frac, 1);
                timeText.text = $"{G.Journey.T:0.0}s";
                captionT += dt;
                float cs = captionT < 0.12f ? Mathf.Lerp(1.6f, 1f, captionT / 0.12f) : 1f;
                captionRt.localScale = Vector3.one * cs;
                caption.alpha = Mathf.Clamp01(1.4f - captionT);
                captionRt.localRotation = Quaternion.Euler(0, 0, -4f + Mathf.Sin(captionT * 6f) * 1.5f);
                legT += dt;
                float ly = legT < 0.3f ? Mathf.Lerp(120, -26, legT / 0.3f) : (legT > 2.6f ? Mathf.Lerp(-26, 120, (legT - 2.6f) / 0.3f) : -26);
                legRt.anchoredPosition = new Vector2(0, ly);
            }

            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (rt, world, t) = popups[i];
                t += dt;
                if (rt == null || t > 1.6f) { if (rt != null) Destroy(rt.gameObject); popups.RemoveAt(i); continue; }
                popups[i] = (rt, world, t);
                var sp = G.Rig.Cam.WorldToScreenPoint(world);
                var canvasRt = (RectTransform)root;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = lp + new Vector2(0, t * 60f);
                float s = t < 0.1f ? Mathf.Lerp(1.8f, 1f, t / 0.1f) : 1f;
                rt.localScale = Vector3.one * s;
                var txt = rt.GetComponent<TextMeshProUGUI>();
                txt.alpha = Mathf.Clamp01(2f - t * 1.4f);
            }
        }
    }
}
