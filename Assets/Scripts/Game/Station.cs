using System.Collections.Generic;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>The packing bench: workbench, back wall, the box, and the shelf of items to pack.</summary>
    public sealed class Station : MonoBehaviour
    {
        public BoxView Box;
        public Transform ItemShelf;
        public readonly List<Vector3> ShelfSlots = new List<Vector3>();
        GameObject env;
        Light lamp;

        public static Station Create(Transform parent)
        {
            var go = new GameObject("Station");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Station>();
            s.BuildEnvironment();
            return s;
        }

        void BuildEnvironment()
        {
            env = ModelLibrary.Spawn("station", transform);
            if (env == null)
            {
                env = new GameObject("station_placeholder");
                env.transform.SetParent(transform, false);
                // bench top at y = 0
                MeshGen.Make("bench", MeshGen.RoundedBox(new Vector3(7f, 0.12f, 1.6f), 0.02f), Mat.Lit(Palette.Wood, 0.35f), env.transform, new Vector3(0, -0.06f, 0.2f));
                MeshGen.Make("benchFront", MeshGen.RoundedBox(new Vector3(7f, 0.9f, 0.1f), 0.02f), Mat.Lit(Palette.WoodDark, 0.3f), env.transform, new Vector3(0, -0.55f, -0.58f));
                MeshGen.Make("wall", MeshGen.RoundedBox(new Vector3(9f, 5f, 0.1f), 0.01f), Mat.Lit(Palette.Wall, 0.1f), env.transform, new Vector3(0, 2.2f, 1.0f));
                MeshGen.Make("floor", MeshGen.RoundedBox(new Vector3(12f, 0.1f, 8f), 0.01f), Mat.Lit(Palette.Hex("5B4636"), 0.2f), env.transform, new Vector3(0, -1.05f, 0f));
            }
            var lampGo = new GameObject("lamp");
            lampGo.transform.SetParent(transform, false);
            lampGo.transform.localPosition = new Vector3(-1.5f, 0.65f, 0.25f);
            lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = Palette.Hex("FFD3A0");
            lamp.intensity = 1.6f;
            lamp.range = 3.5f;
            lamp.shadowBias = 0.03f;
            lamp.shadowNormalBias = 0.3f;
            GraphicsQuality.Track(lamp, LightShadows.None, LightShadows.None, LightShadows.None, LightShadows.Soft);   // ULTRA only

            ItemShelf = new GameObject("itemShelf").transform;
            ItemShelf.SetParent(transform, false);
        }

        public void SetupFor(LevelDef lv)
        {
            LightingPreset.For(null).Apply(Game.I.Sun, Game.I.Rig.Cam);
            if (Box == null || Box.W != lv.W || Box.H != lv.H)
            {
                if (Box != null) Destroy(Box.gameObject);
                Box = BoxView.Create(transform, lv.W, lv.H);
            }
            Box.transform.SetParent(transform, false);
            Box.transform.localPosition = new Vector3(0, Box.OuterHalfHeight, 0);
            Box.transform.localRotation = Quaternion.identity;
            Box.gameObject.SetActive(true);
            Box.ShowFront(false);
            Box.SetFlaps(0f, true);
            Box.SetTape(0f, true);
            Box.ShowGrid(Game.I.Save.ShowGrid);
            BuildShelf(lv);
            Frame(lv, true);
            Reflections.Capture(transform.position + new Vector3(0, 0.5f, 0), new Vector3(8f, 4f, 4f));
        }

        const float Slot = 0.62f;

        void BuildShelf(LevelDef lv)
        {
            foreach (Transform c in ItemShelf) Destroy(c.gameObject);
            ShelfSlots.Clear();
            float boxRight = lv.W * BoxView.Cell * 0.5f + BoxView.Wall;
            int n = lv.Items.Length;
            int cols = n > 3 ? 2 : 1;
            int rows = (n + cols - 1) / cols;
            ItemShelf.localPosition = new Vector3(boxRight + 0.3f, 0.035f, 0.05f);
            var model = ModelLibrary.Spawn($"itemshelf_{cols}x{rows}", ItemShelf);
            if (model == null)
            {
                var wood = Mat.Lit(Palette.Wood, 0.3f);
                for (int r = 0; r <= rows; r++)
                    MeshGen.Make("board", MeshGen.RoundedBox(new Vector3(cols * Slot + 0.06f, 0.035f, 0.42f), 0.01f), wood, ItemShelf, new Vector3(cols * Slot * 0.5f, r * Slot - 0.0175f, 0));
            }
            for (int i = 0; i < n; i++)
            {
                int r = rows - 1 - i / cols, c = i % cols;
                ShelfSlots.Add(ItemShelf.TransformPoint(new Vector3(c * Slot + Slot * 0.5f, r * Slot, -0.03f)));
            }
        }

        /// <summary>Screen rectangles (pixels) of the HUD panels drawn over the bench, from the HUD.</summary>
        public System.Func<List<Rect>> KeepOut;
        /// <summary>Self-test: frame the bench as before round 7 (box and shelf fitted to the whole screen).</summary>
        public bool LegacyFrame;
        /// <summary>The last framing: how much it had to pull back (1 = the plain fit) and the shift in pixels.</summary>
        public float FrameScale { get; private set; } = 1f;
        public Vector2 FrameShift { get; private set; }
        public bool FrameClear { get; private set; } = true;

        public void Frame(LevelDef lv, bool snap)
        {
            var rig = Game.I.Rig;
            rig.Anchor = Vector3.zero;
            float boxW = lv.W * BoxView.Cell, boxH = lv.H * BoxView.Cell;
            int n = lv.Items.Length;
            int cols = n > 3 ? 2 : 1;
            int rows = (n + cols - 1) / cols;
            float shelfW = cols * Slot + 0.36f;
            float left = -boxW * 0.5f - 0.15f, right = boxW * 0.5f + shelfW + 0.12f;
            float top = Mathf.Max(boxH + 0.3f, rows * Slot + 0.12f), bottom = -0.42f;
            float cx = (left + right) * 0.5f, cy = (top + bottom) * 0.5f;
            float width = right - left, height = top - bottom;
            float fov = 24f;
            float aspect = Mathf.Max(1.2f, rig.Cam.aspect);
            float distH = height * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float distW = width * 0.5f / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect);
            float dist = Mathf.Max(distH, distW) * 1.18f + 0.3f;
            var target = new Vector3(cx, cy + 0.05f, 0);
            var pos = target + new Vector3(0, dist * 0.2f, -dist);
            FrameScale = 1f; FrameShift = Vector2.zero; FrameClear = true;
            var keep = LegacyFrame ? null : KeepOut?.Invoke();
            if (keep != null && keep.Count > 0) FitAroundHud(lv, keep, target, dist, fov, ref pos, ref target);
            rig.LookAt(pos, target, fov);
            rig.PosSharpness = 5f;
            if (snap) rig.Snap();
        }

        // ---- keeping the box and the shelf clear of the HUD -----------------------------------------------

        const float HudMargin = 10f;   // pixels between a panel and the box or the shelf

        /// <summary>The box's cells (with its walls) and the shelf's cubbies, as world-space quads (4 corners each).</summary>
        public List<Vector3[]> FramedQuads(LevelDef lv)
        {
            var quads = new List<Vector3[]>();
            float wc = BoxView.Wall / BoxView.Cell;
            quads.Add(new[] { Box.CellToWorld(-wc, -wc), Box.CellToWorld(lv.W + wc, -wc), Box.CellToWorld(lv.W + wc, lv.H + wc), Box.CellToWorld(-wc, lv.H + wc) });
            int n = lv.Items.Length, cols = n > 3 ? 2 : 1, rows = (n + cols - 1) / cols;
            quads.Add(new[]
            {
                ItemShelf.TransformPoint(new Vector3(0, 0, -0.03f)), ItemShelf.TransformPoint(new Vector3(cols * Slot, 0, -0.03f)),
                ItemShelf.TransformPoint(new Vector3(cols * Slot, rows * Slot, -0.03f)), ItemShelf.TransformPoint(new Vector3(0, rows * Slot, -0.03f)),
            });
            return quads;
        }

        /// <summary>Screen pixels of a world point for a camera that isn't there yet.</summary>
        static Vector2 Project(Vector3 p, Vector3 camPos, Quaternion camRot, float tanHalf, float aspect, float sw, float sh)
        {
            var v = Quaternion.Inverse(camRot) * (p - camPos);
            float z = Mathf.Max(0.01f, v.z);
            return new Vector2((v.x / (z * tanHalf * aspect) * 0.5f + 0.5f) * sw, (v.y / (z * tanHalf) * 0.5f + 0.5f) * sh);
        }

        static Rect Bounds(Vector3[] quad, Vector3 camPos, Quaternion camRot, float tanHalf, float aspect, float sw, float sh)
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in quad)
            {
                var s = Project(p, camPos, camRot, tanHalf, aspect, sw, sh);
                mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
            }
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        static bool Clear(List<Rect> rects, List<Rect> keep, Vector2 shift, float margin, float sw, float sh)
        {
            foreach (var r0 in rects)
            {
                var r = new Rect(r0.position + shift, r0.size);
                if (r.xMin < margin || r.yMin < margin || r.xMax > sw - margin || r.yMax > sh - margin) return false;
                foreach (var k in keep)
                    if (r.xMin < k.xMax + margin && r.xMax > k.xMin - margin && r.yMin < k.yMax + margin && r.yMax > k.yMin - margin) return false;
            }
            return true;
        }

        /// <summary>
        /// Pulls the camera back as little as possible, and slides the shot as little as possible, until the
        /// box and the shelf are clear of every HUD panel (and on screen). Falls back to the plain fit if
        /// nothing works (a window too small for the HUD).
        /// </summary>
        void FitAroundHud(LevelDef lv, List<Rect> keep, Vector3 target0, float dist0, float fov, ref Vector3 pos, ref Vector3 target)
        {
            var cam = Game.I.Rig.Cam;
            float sw = cam.pixelWidth, sh = cam.pixelHeight, aspect = sw / Mathf.Max(1f, sh);
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var quads = FramedQuads(lv);
            var rects = new List<Rect>(quads.Count);
            for (float k = 1f; k <= 2.0001f; k += 0.01f)
            {
                float d = dist0 * k;
                var camPos = target0 + new Vector3(0, d * 0.2f, -d);
                var camRot = Quaternion.LookRotation(target0 - camPos, Vector3.up);
                float depth = (Quaternion.Inverse(camRot) * (target0 - camPos)).z;
                float perPx = 2f * depth * tanHalf / sh;
                var p = camPos;
                Vector2 total = Vector2.zero;
                // a slide moves points at other depths a little differently: slide, measure again, twice more
                for (int pass = 0; pass < 3; pass++)
                {
                    rects.Clear();
                    foreach (var q in quads) rects.Add(Bounds(q, p, camRot, tanHalf, aspect, sw, sh));
                    if (Clear(rects, keep, Vector2.zero, HudMargin * 0.5f, sw, sh) && (pass > 0 || total == Vector2.zero))
                    {
                        pos = p; target = p + (target0 - camPos);
                        FrameScale = k; FrameShift = total;
                        return;
                    }
                    if (!NearestClearSlide(rects, keep, sw, sh, out var slide)) break;
                    // aim the camera the other way by the same amount at the box's depth
                    p += -(camRot * Vector3.right) * slide.x * perPx - (camRot * Vector3.up) * slide.y * perPx;
                    total += slide;
                }
            }
            FrameClear = false;
            Debug.LogWarning($"[Station] delivery {lv.Number}: no framing keeps the bench clear of the HUD at {sw}x{sh}");
        }

        readonly List<float> slideXs = new List<float>(), slideYs = new List<float>();

        /// <summary>
        /// The smallest slide of the image (pixels) that takes every rect clear of the panels and keeps it on
        /// screen. Each panel rules out a rectangle of slides, so the nearest allowed one has its x and y on
        /// one of those rectangles' edges (or at 0): every pair is tried.
        /// </summary>
        bool NearestClearSlide(List<Rect> rects, List<Rect> keep, float sw, float sh, out Vector2 slide)
        {
            var xs = slideXs; var ys = slideYs;
            xs.Clear(); ys.Clear(); xs.Add(0f); ys.Add(0f);
            foreach (var r in rects)
            {
                xs.Add(HudMargin - r.xMin); xs.Add(sw - HudMargin - r.xMax);
                ys.Add(HudMargin - r.yMin); ys.Add(sh - HudMargin - r.yMax);
                foreach (var kp in keep)
                {
                    xs.Add(kp.xMin - HudMargin - r.xMax - 0.01f); xs.Add(kp.xMax + HudMargin - r.xMin + 0.01f);
                    ys.Add(kp.yMin - HudMargin - r.yMax - 0.01f); ys.Add(kp.yMax + HudMargin - r.yMin + 0.01f);
                }
            }
            float best = float.MaxValue; slide = default;
            foreach (float sy in ys)
            {
                if (sy * sy >= best) continue;
                foreach (float sx in xs)
                {
                    float c = sx * sx + sy * sy;
                    if (c >= best || !Clear(rects, keep, new Vector2(sx, sy), HudMargin - 0.005f, sw, sh)) continue;
                    best = c; slide = new Vector2(sx, sy);
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>Title backdrop: a sealed parcel on the bench, the shelf hidden.</summary>
        public void ShowTitle()
        {
            LightingPreset.For(null).Apply(Game.I.Sun, Game.I.Rig.Cam);
            if (Box == null || Box.W != 4 || Box.H != 3)
            {
                if (Box != null) Destroy(Box.gameObject);
                Box = BoxView.Create(transform, 4, 3);
            }
            foreach (var p in Box.Pieces) if (p != null) Destroy(p.gameObject);
            Box.Pieces.Clear();
            Box.ClearStatics();
            Box.transform.SetParent(transform, false);
            Box.transform.localPosition = new Vector3(0.55f, Box.OuterHalfHeight, -0.05f);
            Box.transform.localRotation = Quaternion.Euler(0, -24f, 0);
            Box.ShowGrid(false);
            Box.ShowFront(true);
            Box.SetFlaps(1f, true);
            Box.SetTapeStyle(Game.I.Save.Tape);
            Box.SetTape(1f, true);
            foreach (Transform c in ItemShelf) Destroy(c.gameObject);
            var rig = Game.I.Rig;
            rig.Anchor = Vector3.zero;
            rig.LookAt(new Vector3(-0.5f, 1.05f, -2.95f), new Vector3(-0.12f, 0.45f, 0.1f), 30f);   // parcel on the right, menu column on the left
            rig.Snap();
            rig.PosSharpness = 1.5f;
        }

        public Vector3 SlotPosition(int i) => i < ShelfSlots.Count ? ShelfSlots[i] : ItemShelf.position;
    }
}
