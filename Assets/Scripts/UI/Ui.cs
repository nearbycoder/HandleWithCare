using System;
using System.Collections.Generic;
using HWC.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HWC.UI
{
    /// <summary>Runtime uGUI builders with the game's shipping-label styling.</summary>
    public static class Ui
    {
        static TMP_FontAsset body, bold, display, italic;

        public static TMP_FontAsset Body => body ?? (body = Font("FiraSans-Medium"));
        public static TMP_FontAsset Bold => bold ?? (bold = Font("FiraSans-Bold"));
        public static TMP_FontAsset Display => display ?? (display = Font("FiraSansCompressed-Heavy"));
        public static TMP_FontAsset Italic => italic ?? (italic = Font("FiraSans-MediumItalic"));

        static TMP_FontAsset Font(string name)
        {
            var f = Resources.Load<Font>("Fonts/" + name);
            if (f == null) { Debug.LogError("[Ui] missing font " + name); return TMP_Settings.defaultFontAsset; }
            var fa = TMP_FontAsset.CreateFontAsset(f, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            fa.name = name;
            return fa;
        }

        // ---- Canvas & rects ---------------------------------------------------------------------

        public static Canvas CreateCanvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TextScale>();
            return c;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        // ---- Sprites ------------------------------------------------------------------------------

        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// <summary>Rounded rectangle sprite (9-sliced), optionally with a soft inner border.</summary>
        public static Sprite Rounded(int radius = 18, int border = 0)
        {
            string key = $"r{radius}_{border}";
            if (sprites.TryGetValue(key, out var sp)) return sp;
            int n = radius * 2 + 8;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float c = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - (c - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - (c - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    float v = 1f;
                    if (border > 0)
                    {
                        float inner = Mathf.Clamp01((radius - border) - d + 0.5f);
                        // edge darkening ring
                        float ex = Mathf.Min(x + 0.5f, n - x - 0.5f), ey = Mathf.Min(y + 0.5f, n - y - 0.5f);
                        float edge = Mathf.Min(ex, ey);
                        v = edge < border ? 0.82f : 1f;
                        v = Mathf.Lerp(0.82f, v, inner);
                    }
                    byte g = (byte)(v * 255);
                    px[y * n + x] = new Color32(g, g, g, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
            sprites[key] = sp;
            return sp;
        }

        public static Sprite Circle => Rounded(32);

        public static Sprite FromTexture(Texture2D tex)
        {
            if (tex == null) return null;
            string key = "tex_" + tex.name + "_" + tex.width + "x" + tex.height;
            if (sprites.TryGetValue(key, out var sp)) return sp;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprites[key] = sp;
            return sp;
        }

        // ---- Widgets --------------------------------------------------------------------------------

        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? Rounded(18);
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        /// <summary>Soft drop shadow (UI effect on the image itself, so it hides with it).</summary>
        public static UnityEngine.UI.Shadow Shadow(Graphic target, float offset = 6f, float alpha = 0.28f)
        {
            var sh = target.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            sh.effectColor = new Color(0.12f, 0.08f, 0.05f, alpha);
            sh.effectDistance = new Vector2(0, -offset);
            return sh;
        }

        static Sprite star;

        /// <summary>Five-point star sprite.</summary>
        public static Sprite Star
        {
            get
            {
                if (star != null) return star;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "star", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                var pts = new Vector2[10];
                for (int k = 0; k < 10; k++)
                {
                    float a = Mathf.PI / 2 + k * Mathf.PI / 5;
                    float r = k % 2 == 0 ? 0.48f : 0.21f;
                    pts[k] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.47f + Mathf.Sin(a) * r);
                }
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                            {
                                var p = new Vector2((x + (sx + 0.5f) / 4f) / N, (y + (sy + 0.5f) / 4f) / N);
                                bool inside = false;
                                for (int i2 = 0, j2 = 9; i2 < 10; j2 = i2++)
                                    if (((pts[i2].y > p.y) != (pts[j2].y > p.y)) && (p.x < (pts[j2].x - pts[i2].x) * (p.y - pts[i2].y) / (pts[j2].y - pts[i2].y) + pts[i2].x)) inside = !inside;
                                if (inside) hits++;
                            }
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                star = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
                return star;
            }
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font = null, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font ?? Body;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.spriteAsset = Glyphs.Asset;   // button shapes and check / cross marks, inline
            TextScale.Created++;
            return t;
        }

        public static Image Icon(Transform parent, string name, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static UiButton Button(Transform parent, string name, string label, Action onClick, Color color, Color textColor, float fontSize = 30f, TMP_FontAsset font = null)
        {
            var img = Panel(parent, name, color, Rounded(16, 3));
            var b = img.gameObject.AddComponent<UiButton>();
            b.Init(img, onClick);
            if (!string.IsNullOrEmpty(label))
            {
                var t = Text(img.transform, "label", label, fontSize, textColor, font ?? Display);
                t.rectTransform.Stretch(8, 8, 2, 4);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.enableAutoSizing = true;
                t.fontSizeMax = fontSize;
                t.fontSizeMin = fontSize * 0.5f;
                b.Label = t;
            }
            return b;
        }
    }

    /// <summary>
    /// LARGER TEXT: small UI text (24 units or less) grows by up to 30% where its box has room, and never
    /// goes below its normal size. TextMeshPro's auto-size picks the largest size that fits, so a text
    /// only grows as far as its own rectangle allows.
    /// </summary>
    public sealed class TextScale : MonoBehaviour
    {
        public const float SmallText = 24f, Grow = 1.3f;
        public static bool Larger { get; private set; }
        public static int Created;   // texts made so far (Ui.Text): new ones get the setting too
        static int version;

        struct Normal { public bool Auto; public float Size, Min, Max; }
        static readonly Dictionary<TextMeshProUGUI, Normal> normal = new Dictionary<TextMeshProUGUI, Normal>();
        int seenCreated = -1, seenVersion = -1;

        public static void Set(bool on)
        {
            if (on == Larger) return;
            Larger = on;
            version++;
        }

        void LateUpdate()
        {
            if (seenCreated == Created && seenVersion == version) return;
            seenCreated = Created; seenVersion = version;
            foreach (var t in GetComponentsInChildren<TextMeshProUGUI>(true)) Apply(t);
            if (normal.Count > 4000)   // forget texts that were destroyed (screens rebuilt over a long session)
            {
                var dead = new List<TextMeshProUGUI>();
                foreach (var k in normal.Keys) if (k == null) dead.Add(k);
                foreach (var k in dead) normal.Remove(k);
            }
        }

        static void Apply(TextMeshProUGUI t)
        {
            if (!normal.TryGetValue(t, out var n))
            {
                n = new Normal { Auto = t.enableAutoSizing, Size = t.fontSize, Min = t.fontSizeMin, Max = t.fontSizeMax };
                normal[t] = n;
            }
            float top = n.Auto ? n.Max : n.Size;
            if (top > SmallText) return;
            if (Larger)
            {
                t.enableAutoSizing = true;
                t.fontSizeMin = n.Auto ? n.Min : n.Size;
                t.fontSizeMax = top * Grow;
            }
            else
            {
                t.enableAutoSizing = n.Auto;
                t.fontSizeMin = n.Min;
                t.fontSizeMax = n.Max;
                if (!n.Auto) t.fontSize = n.Size;
            }
        }

        /// <summary>The small texts that are on screen (for the self-test).</summary>
        public static IEnumerable<TextMeshProUGUI> SmallTexts()
        {
            foreach (var kv in normal)
                if (kv.Key != null && kv.Key.isActiveAndEnabled && (kv.Value.Auto ? kv.Value.Max : kv.Value.Size) <= SmallText) yield return kv.Key;
        }
    }

    /// <summary>A tactile button: grows on hover, presses down like a rubber stamp, clicks.</summary>
    public sealed class UiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Image Image;
        public TextMeshProUGUI Label;
        public Action OnClick;
        public Action OnHover;
        public bool Interactable = true;
        public Color BaseColor;
        public float HoverScale = 1.05f;
        bool hover, down;
        float scale = 1f;
        float flash;
        public static Action ClickSound, HoverSound;

        public void Init(Image img, Action onClick)
        {
            Image = img;
            OnClick = onClick;
            BaseColor = img.color;
        }

        public void SetInteractable(bool on)
        {
            Interactable = on;
            Image.color = on ? BaseColor : Color.Lerp(BaseColor, new Color(0.6f, 0.58f, 0.55f, BaseColor.a), 0.7f);
            if (Label != null) Label.alpha = on ? 1f : 0.55f;
        }

        public void SetColor(Color c)
        {
            BaseColor = c;
            SetInteractable(Interactable);
        }

        public void Pulse() { flash = 1f; }

        public void OnPointerEnter(PointerEventData e) { hover = true; if (Interactable) { HoverSound?.Invoke(); OnHover?.Invoke(); } }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) down = true; }
        public void OnPointerUp(PointerEventData e) { down = false; }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            Press();
        }

        /// <summary>Clicks the button (mouse or keyboard shortcut), with the same sound and squash.</summary>
        public void Press()
        {
            if (!Interactable || !isActiveAndEnabled) return;
            ClickSound?.Invoke();
            scale = 0.86f;
            OnClick?.Invoke();
        }

        void Update()
        {
            float target = !Interactable ? 1f : (down ? 0.92f : (hover ? HoverScale : 1f));
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-Clock.UnscaledDelta * 18f));
            if (flash > 0) flash = Mathf.Max(0, flash - Clock.UnscaledDelta * 1.5f);
            float pulse = flash > 0 ? 1f + Mathf.Sin(flash * Mathf.PI * 3f) * 0.06f * flash : 1f;
            transform.localScale = Vector3.one * scale * pulse;
        }
    }
}
