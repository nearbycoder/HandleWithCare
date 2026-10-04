using System;
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
    /// Teaches without walls of text: one sticky note at a time, pointing (with a bouncing arrow)
    /// at the exact thing to do next. Steps advance when the player does the thing.
    /// </summary>
    public sealed class Tutorial : MonoBehaviour
    {
        struct Step
        {
            public string Text;
            public Func<Vector2?> Target;      // screen position to point at (canvas local), null = no arrow
            public Func<bool> Done;
        }

        RectTransform root, note, arrow;
        TextMeshProUGUI text;
        readonly List<Step> steps = new List<Step>();
        int index = -1;
        float t;
        Game G => Game.I;
        public bool Active => index >= 0 && index < steps.Count;

        public void Build(Transform canvas)
        {
            root = Ui.Rect("Tutorial", canvas).Stretch();
            var img = Ui.Panel(root, "note", Palette.Sticky, Ui.Rounded(4));
            note = img.rectTransform;
            note.sizeDelta = new Vector2(400, 130);
            Ui.Shadow(img, 6, 0.3f);
            text = Ui.Text(note, "t", "", 28, Palette.Ink, Ui.Italic, TextAlignmentOptions.Center);
            text.rectTransform.Stretch(18, 18, 12, 30);
            var sig = Ui.Text(note, "sig", "— Mabel", 18, Palette.InkSoft, Ui.Bold, TextAlignmentOptions.BottomRight);
            sig.rectTransform.Stretch(14, 14, 8, 8);
            var a = Ui.Text(root, "arrow", "▼", 72, Palette.PostalRed, Ui.Body);
            arrow = a.rectTransform;
            arrow.sizeDelta = new Vector2(80, 80);
            a.outlineWidth = 0.2f;
            a.outlineColor = Palette.Cream;
            root.gameObject.SetActive(false);
        }

        /// <summary>Starts the first-delivery tutorial if the player hasn't seen it.</summary>
        public void MaybeStart(LevelDef lv)
        {
            Stop();
            if (lv.Number != 1 || G.Save.SeenTips.Contains("basics")) return;
            var pc = G.Packing;
            steps.Clear();
            steps.Add(new Step
            {
                Text = "Here's Edna's teacup. Click it to pick it up.",
                Target = () => WorldToCanvas(G.Station.SlotPosition(0) + Vector3.up * 0.25f),
                Done = () => pc.Tool == Tool.Item || pc.RemainingItems().Count == 0,
            });
            steps.Add(new Step
            {
                Text = "Now drop it into the box. Click a spot on the floor.",
                Target = () => WorldToCanvas(G.Station.Box.CellToWorld(1.5f, 1.4f)),
                Done = () => pc.RemainingItems().Count == 0,
            });
            steps.Add(new Step
            {
                Text = "Teacups hate rattling about. Grab some crumpled paper.",
                Target = () => G.Hud.SlotScreen(MaterialSlot.Paper),
                Done = () => pc.Tool == Tool.Padding || pc.Pk.UsedMaterials().Paper > 0,
            });
            steps.Add(new Step
            {
                Text = "Click and drag to stuff the gaps. Fill every empty spot!",
                Target = () => WorldToCanvas(G.Station.Box.CellToWorld(0.5f, 1.4f)),
                Done = () => pc.Pk.UsedMaterials().Paper >= 4,
            });
            steps.Add(new Step
            {
                Text = "Snug as a bug. Seal it and ship it!",
                Target = () => G.Hud.SealScreen(),
                Done = () => G.Phase != Phase.Packing,
            });
            index = 0;
            t = 0;
            root.gameObject.SetActive(true);
            Show();
        }

        public void Stop()
        {
            index = -1;
            if (root != null) root.gameObject.SetActive(false);
        }

        void Show()
        {
            text.text = steps[index].Text;
            t = 0;
            G.Hud.Sfx("note", 0.7f);
        }

        Vector2? WorldToCanvas(Vector3 world)
        {
            var sp = G.Rig.Cam.WorldToScreenPoint(world);
            if (sp.z < 0) return null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var lp);
            return lp;
        }

        void Update()
        {
            if (!Active) return;
            if (G.Phase != Phase.Packing && index < steps.Count - 1) { Stop(); return; }
            bool waiting = G.Hud.ShiftCardShowing;
            note.gameObject.SetActive(!waiting);
            if (waiting) { arrow.gameObject.SetActive(false); t = 0; return; }
            var st = steps[index];
            if (st.Done())
            {
                index++;
                if (index >= steps.Count)
                {
                    G.Save.SeenTips.Add("basics");
                    G.Save.Write();
                    Stop();
                    return;
                }
                Show();
                st = steps[index];
            }
            t += Time.unscaledDeltaTime;
            var target = st.Target();
            float pop = t < 0.2f ? Mathf.Lerp(0.6f, 1f, t / 0.2f) : 1f;
            note.localScale = Vector3.one * pop;
            note.localRotation = Quaternion.Euler(0, 0, -2f + Mathf.Sin(t * 1.3f) * 0.6f);
            if (target.HasValue)
            {
                arrow.gameObject.SetActive(true);
                var p = target.Value;
                float bob = Mathf.Abs(Mathf.Sin(t * 5f)) * 18f;
                arrow.anchoredPosition = p + new Vector2(0, 70 + bob);
                arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f);
                note.anchorMin = note.anchorMax = new Vector2(0.5f, 0.5f);
                var np = p + new Vector2(0, 230);
                np.x = Mathf.Clamp(np.x, -760, 760);
                np.y = Mathf.Clamp(np.y, -380, 440);
                note.anchoredPosition = Vector2.Lerp(note.anchoredPosition, np, t < 0.05f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
            }
            else arrow.gameObject.SetActive(false);
        }
    }
}
