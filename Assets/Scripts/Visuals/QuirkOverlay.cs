using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// Packing-view hints that make quirks legible before sealing: where a dragon's sneeze will
    /// reach, which magnets will pull on each other, how far heat spreads, who is asleep.
    /// </summary>
    public sealed class QuirkOverlay : MonoBehaviour
    {
        readonly List<GameObject> items = new List<GameObject>();
        readonly List<(Transform t, float phase, Vector3 basePos)> zzz = new List<(Transform, float, Vector3)>();
        static Material flameMat, heatMat, lineMat;
        static Texture2D zTex;

        public void Clear()
        {
            foreach (var g in items) if (g != null) Destroy(g);
            items.Clear();
            zzz.Clear();
        }

        public void Rebuild(BoxView box, Packing pk)
        {
            Clear();
            if (box == null || pk == null) return;
            transform.SetParent(box.Contents, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            float C = BoxView.Cell;
            float zf = -BoxView.Depth * 0.5f + 0.012f;
            for (int i = 0; i < pk.Pieces.Count; i++)
            {
                var p = pk.Pieces[i];
                var d = p.Def;
                if (d.Has(Quirk.Sneezer))
                {
                    // flame strip from the nose, as far as the first thing that would stop it
                    float nose = p.Facing > 0 ? p.X + p.W : p.X;
                    float reach = SimConst.FlameLength;
                    for (float s = 0.5f; s <= SimConst.FlameLength; s += 1f)
                    {
                        int cx = Mathf.FloorToInt(nose + p.Facing * s);
                        if (cx < 0 || cx >= pk.W) { reach = s - 0.5f; break; }
                        bool divider = pk.Dividers.Contains(p.Facing > 0 ? cx : cx + 1);
                        if (divider && s > 0.5f) { reach = s - 0.5f; break; }
                        int hit = pk.PieceAt(cx, p.Y);
                        if (hit >= 0)
                        {
                            var k = pk.Pieces[hit].Kind;
                            if (k != PieceKind.Paper && k != PieceKind.Bubble && k != PieceKind.Balloon) { reach = s - 0.5f; break; }
                        }
                    }
                    reach = Mathf.Max(0.15f, reach);
                    var q = Quad("flame", FlameMat());
                    q.transform.localPosition = new Vector3((nose + p.Facing * reach * 0.5f) * C, (p.Y + 0.5f) * C, zf);
                    q.transform.localScale = new Vector3(reach * C, 0.62f * C, 1);
                }
                if (d.Has(Quirk.Hot) && LevelHasHeatSensitive(pk))
                {
                    var q = Quad("heat", HeatMat());
                    q.transform.localPosition = new Vector3((p.X + p.W * 0.5f) * C, (p.Y + p.H * 0.5f) * C, zf + 0.002f);
                    q.transform.localScale = new Vector3((p.W + 2f * SimConst.HeatRange) * C, (p.H + 2f * SimConst.HeatRange) * C, 1);
                }
                if (d.Has(Quirk.Magnet))
                {
                    for (int j = i + 1; j < pk.Pieces.Count; j++)
                    {
                        var o = pk.Pieces[j];
                        if (!o.Def.Has(Quirk.Metal)) continue;
                        var a = new Vector2(p.X + p.W * 0.5f, p.Y + p.H * 0.5f);
                        var b = new Vector2(o.X + o.W * 0.5f, o.Y + o.H * 0.5f);
                        if ((a - b).magnitude > SimConst.MagnetRange) continue;
                        var go = new GameObject("pull");
                        go.transform.SetParent(transform, false);
                        var lr = go.AddComponent<LineRenderer>();
                        lr.useWorldSpace = false;
                        lr.positionCount = 2;
                        lr.SetPosition(0, new Vector3(a.x * C, a.y * C, zf));
                        lr.SetPosition(1, new Vector3(b.x * C, b.y * C, zf));
                        lr.widthMultiplier = 0.022f;
                        lr.sharedMaterial = Mat.Unlit(new Color(0.9f, 0.2f, 0.2f, 0.75f), true);
                        lr.numCapVertices = 3;
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        items.Add(go);
                    }
                }
            }
        }

        static bool LevelHasHeatSensitive(Packing pk)
        {
            foreach (var p in pk.Pieces) if (p.Def.Has(Quirk.Melts) || p.Def.Has(Quirk.KeepWarm)) return true;
            return false;
        }

        GameObject Quad(string name, Material m)
        {
            var go = MeshGen.Make(name, MeshGen.Quad(), m, transform, Vector3.zero, false);
            go.GetComponent<MeshRenderer>().receiveShadows = false;
            items.Add(go);
            return go;
        }

        static Material FlameMat()
        {
            if (flameMat != null) return flameMat;
            const int W = 64, H = 16;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = 1f - Mathf.Abs((y + 0.5f) / H * 2 - 1);
                    float stripe = ((x / 4 + y / 4) % 2 == 0) ? 1f : 0.65f;
                    t.SetPixel(x, y, new Color(1f, 0.42f, 0.12f, Mathf.Clamp01(v * 1.8f) * 0.8f * stripe));
                }
            t.Apply();
            flameMat = Mat.UnlitInstance(Color.white, t);
            return flameMat;
        }

        static Material HeatMat()
        {
            if (heatMat != null) return heatMat;
            heatMat = Mat.UnlitInstance(new Color(1f, 0.55f, 0.2f, 0.18f), TextureLibrary.SoftDot);
            return heatMat;
        }

        static Material LineMat()
        {
            if (lineMat != null) return lineMat;
            var t = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            for (int x = 0; x < 8; x++) t.SetPixel(x, 0, x < 4 ? new Color(0.85f, 0.2f, 0.2f, 0.8f) : new Color(0, 0, 0, 0));
            t.Apply();
            lineMat = Mat.UnlitInstance(Color.white, t);
            return lineMat;
        }

        static Material ZMat()
        {
            if (zTex == null)
            {
                const int N = 32;
                zTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        bool top = y > 24 && y < 29 && x > 5 && x < 27;
                        bool bot = y > 3 && y < 8 && x > 5 && x < 27;
                        bool diag = Mathf.Abs((x - 6) - (y - 6) * 21f / 20f) < 3.2f && y >= 6 && y <= 27;
                        bool on = top || bot || diag;
                        zTex.SetPixel(x, y, on ? new Color(0.2f, 0.25f, 0.45f, 0.9f) : new Color(0, 0, 0, 0));
                    }
                zTex.Apply();
            }
            return Mat.UnlitInstance(Color.white, zTex);
        }

        /// <summary>Animates the sleepy z's (driven by the packing controller).</summary>
        public void Tick()
        {
            float t = Time.time;
            foreach (var (tr, ph, bp) in zzz)
            {
                if (tr == null) continue;
                float u = (t * 0.4f + ph) % 1f;
                tr.localScale = Vector3.one * (0.06f + u * 0.07f);
                tr.localPosition = bp + new Vector3(Mathf.Sin(u * 6f) * 0.02f + u * 0.05f, u * 0.16f, 0);
                tr.GetComponent<MeshRenderer>().material.color = new Color(1, 1, 1, Mathf.Sin(u * Mathf.PI));
            }
        }
    }
}
