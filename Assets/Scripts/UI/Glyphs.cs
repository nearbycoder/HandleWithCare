using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HWC.UI
{
    /// <summary>
    /// Button shapes and marks that no UI font here has (PlayStation's cross, circle, square and
    /// triangle, a check and a cross), drawn in code at start-up into one TextMeshPro sprite sheet.
    /// Every text built by <see cref="Ui.Text"/> can show them inline: <c>&lt;sprite name="ps_cross"&gt;</c>.
    /// </summary>
    public static class Glyphs
    {
        public const string PsCross = "ps_cross", PsCircle = "ps_circle", PsSquare = "ps_square", PsTriangle = "ps_triangle";
        public const string MarkCheck = "mark_check", MarkCross = "mark_cross";
        static readonly string[] Names = { PsCross, PsCircle, PsSquare, PsTriangle, MarkCheck, MarkCross };
        const int Cell = 128, Pad = 4, Stride = Cell + Pad * 2;

        static TMP_SpriteAsset asset;
        static bool tried;

        /// <summary>The sprite sheet (null if the sprite shader is missing from the build).</summary>
        public static TMP_SpriteAsset Asset
        {
            get
            {
                if (!tried) { tried = true; asset = Build(); }
                return asset;
            }
        }

        public static bool Ok => Asset != null;

        /// <summary>The names of the sprites a text actually draws, in order (for the self-tests).</summary>
        public static string Drawn(TMP_Text t)
        {
            t.ForceMeshUpdate();
            var names = new List<string>();
            var info = t.textInfo;
            for (int i = 0; i < info.characterCount; i++)
                if (info.characterInfo[i].elementType == TMP_TextElementType.Sprite)
                {
                    var glyph = info.characterInfo[i].textElement?.glyph;   // the shape whose rectangle is drawn
                    int k = glyph != null ? (int)glyph.index : -1;
                    names.Add(k >= 0 && k < Names.Length ? Names[k] : "?" + k);
                }
            return string.Join(",", names);
        }

        /// <summary>Inline sprite tag for one of the glyphs.</summary>
        public static string Tag(string name) => $"<sprite name=\"{name}\">";

        /// <summary>A drawn check mark (or a dot, if the sheet couldn't be made).</summary>
        public static string Check => Ok ? Tag(MarkCheck) : "<color=#2E8B57>●</color>";
        /// <summary>A drawn cross mark (or a multiplication sign, if the sheet couldn't be made).</summary>
        public static string Cross => Ok ? Tag(MarkCross) : "<color=#A8322A><size=150%><b>×</b></size></color>";

        static TMP_SpriteAsset Build()
        {
            var shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null) { Debug.LogError("[Glyphs] TextMeshPro/Sprite shader missing: button shapes fall back to text"); return null; }
            int w = Stride * Names.Length, h = Stride;
            var px = new Color32[w * h];
            for (int i = 0; i < Names.Length; i++) Draw(px, w, i * Stride + Pad, Pad, Names[i]);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "HWC Glyphs", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var sa = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            sa.name = "HWC Glyphs";
            sa.hideFlags = HideFlags.DontSave;
            sa.spriteSheet = tex;
            // TextMeshPro builds its glyph and character tables from this list once a material is set
            sa.spriteInfoList = new List<TMP_Sprite>();
            for (int i = 0; i < Names.Length; i++)
            {
                sa.spriteInfoList.Add(new TMP_Sprite
                {
                    id = i, name = Names[i], hashCode = TMP_TextUtilities.GetSimpleHashCode(Names[i]), unicode = 0,
                    x = i * Stride + Pad, y = Pad, width = Cell, height = Cell,
                    xOffset = 4, yOffset = Cell * 0.86f, xAdvance = Cell + 10, scale = 1f,
                });
            }
            var mat = new Material(shader) { name = "HWC Glyphs" };
            mat.SetTexture(ShaderUtilities.ID_MainTex, tex);
            sa.material = mat;
            sa.UpdateLookupTables();
            // the upgrade from the list above links each character to its glyph but leaves its glyph index
            // at 0, so the next lookup would point every name at the first shape: set the indices and redo it
            var chars = sa.spriteCharacterTable;
            for (int i = 0; i < chars.Count; i++) chars[i].glyphIndex = (uint)i;
            sa.UpdateLookupTables();
            return sa;
        }

        // ---- drawing: signed distances in a [-1, 1] cell, y up, anti-aliased over about one pixel ----------

        static readonly Color Disc = new Color(0.14f, 0.12f, 0.11f);
        static readonly Color Blue = new Color(0.49f, 0.70f, 0.93f), Red = new Color(0.98f, 0.42f, 0.44f);
        static readonly Color Pink = new Color(0.92f, 0.60f, 0.85f), Green = new Color(0.27f, 0.85f, 0.67f);
        static readonly Color CheckGreen = new Color(0.18f, 0.55f, 0.34f), CrossRed = new Color(0.66f, 0.20f, 0.16f);

        static void Draw(Color32[] px, int w, int x0, int y0, string name)
        {
            const float px2u = 2f / Cell;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    var p = new Vector2((x + 0.5f) * px2u - 1f, (y + 0.5f) * px2u - 1f);
                    Color c;
                    if (name.StartsWith("ps_"))
                    {
                        float disc = Cover(p.magnitude - 0.94f);
                        float sym; Color sc;
                        switch (name)
                        {
                            case PsCross: sym = Cover(Mathf.Min(Seg(p, new Vector2(-0.4f, -0.4f), new Vector2(0.4f, 0.4f)), Seg(p, new Vector2(-0.4f, 0.4f), new Vector2(0.4f, -0.4f))) - 0.1f); sc = Blue; break;
                            case PsCircle: sym = Cover(Mathf.Abs(p.magnitude - 0.44f) - 0.1f); sc = Red; break;
                            case PsSquare: sym = Cover(Mathf.Abs(Box(p, 0.38f)) - 0.1f); sc = Pink; break;
                            default: sym = Cover(Mathf.Abs(Tri(p - new Vector2(0, -0.06f))) - 0.1f); sc = Green; break;
                        }
                        c = Color.Lerp(Disc, sc, sym);
                        c.a = disc;
                    }
                    else
                    {
                        float d = name == MarkCheck
                            ? Mathf.Min(Seg(p, new Vector2(-0.74f, 0.04f), new Vector2(-0.22f, -0.56f)), Seg(p, new Vector2(-0.22f, -0.56f), new Vector2(0.76f, 0.68f))) - 0.17f
                            : Mathf.Min(Seg(p, new Vector2(-0.6f, -0.6f), new Vector2(0.6f, 0.6f)), Seg(p, new Vector2(-0.6f, 0.6f), new Vector2(0.6f, -0.6f))) - 0.17f;
                        c = name == MarkCheck ? CheckGreen : CrossRed;
                        c.a = Cover(d);
                    }
                    px[(y0 + y) * w + x0 + x] = c;
                }
        }

        /// <summary>Coverage from a signed distance in cell units (negative inside).</summary>
        static float Cover(float d) => Mathf.Clamp01(0.5f - d * Cell * 0.5f);

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        static float Box(Vector2 p, float half)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half, Mathf.Abs(p.y) - half);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        /// <summary>Signed distance to an upward triangle (negative inside).</summary>
        static float Tri(Vector2 p)
        {
            Vector2 a = new Vector2(0f, 0.46f), b = new Vector2(-0.48f, -0.36f), c = new Vector2(0.48f, -0.36f);
            float d = Mathf.Min(Seg(p, a, b), Mathf.Min(Seg(p, b, c), Seg(p, c, a)));
            bool inside = Side(p, a, b) <= 0 && Side(p, b, c) <= 0 && Side(p, c, a) <= 0
                       || Side(p, a, b) >= 0 && Side(p, b, c) >= 0 && Side(p, c, a) >= 0;
            return inside ? -d : d;
        }

        static float Side(Vector2 p, Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }
}
