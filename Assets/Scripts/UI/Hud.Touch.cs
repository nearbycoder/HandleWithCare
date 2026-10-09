using System.Collections.Generic;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// The HUD with the browser's touch controls showing (TouchInput): the page draws thumb-sized buttons for
    /// UNDO, REDO, EMPTY BOX, MY BEST, ASK MABEL, SEAL &amp; SHIP, the journey's SKIP and the replay's controls,
    /// so the HUD's own (mouse-sized) ones step aside; the prompts say "tap"; and everything keeps clear of the
    /// notch and the home indicator (the page's safe area).
    /// </summary>
    public sealed partial class Hud
    {
        public static bool TouchPrompts => TouchInput.I != null && TouchInput.I.Active;
        bool touchPrompts, touchReplay;
        readonly List<RectTransform> touchHidden = new List<RectTransform>();

        void BuildTouch()
        {
            foreach (var b in new[] { undoBtn, redoBtn, clearBtn, bestBtn, hintBtn, sealBtn, skipBtn, revealSkip })
                touchHidden.Add(b.Image.rectTransform);
            touchHidden.Add(sealHint.rectTransform);
            touchHidden.Add(replayBar);
            touchHidden.Add(revealSkipHint.rectTransform);
            if (TouchInput.I != null) TouchInput.I.SafeAreaChanged += ApplySafeArea;
        }

        void UpdateTouch()
        {
            bool on = TouchPrompts;
            bool replay = on && journeyRoot.gameObject.activeSelf && G.Journey.IsReplay;
            if (on == touchPrompts && replay == touchReplay) return;
            if (on != touchPrompts) foreach (var rt in touchHidden) SetTouchHidden(rt, on);
            touchPrompts = on;
            touchReplay = replay;
            // the replay's scrub bar is the page's, with the time beside it
            SetTouchHidden(timeline, replay);
            SetTouchHidden(timeText.rectTransform, replay);
            RefreshPrompts();
            ScalePause();
        }

        /// <summary>On a phone the pause menu's buttons would be under 30 points tall: it grows (it has the room)
        /// until they are about 40, by at most 40%.</summary>
        void ScalePause()
        {
            float boost = 1f;
            var canvas = root.GetComponentInParent<Canvas>();
            if (touchPrompts && canvas != null && TouchInput.I.PixelsPerCss > 0)
            {
                float cssPerUnit = canvas.scaleFactor / TouchInput.I.PixelsPerCss;
                boost = Mathf.Clamp(40f / (78f * cssPerUnit), 1f, 1.4f);
            }
            pauseRoot.localScale = Vector3.one * boost;
        }

        static void SetTouchHidden(RectTransform rt, bool hidden)
        {
            var g = rt.GetComponent<CanvasGroup>();
            if (g == null) g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = hidden ? 0f : 1f;
            g.blocksRaycasts = !hidden;
            g.interactable = !hidden;
        }

        /// <summary>The screens (not their full-screen dimming) inside the page's safe area.</summary>
        void ApplySafeArea()
        {
            ScalePause();
            var sa = TouchInput.I.SafeArea;
            var canvas = root.GetComponentInParent<Canvas>();
            float k = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            foreach (var rt in new[] { packRoot, journeyRoot, resultsRoot, revealRoot })
            {
                rt.offsetMin = new Vector2(sa.x, sa.y) / k;
                rt.offsetMax = -new Vector2(sa.z, sa.w) / k;
            }
            G.Menus.ApplySafeArea(sa);
        }
    }
}
