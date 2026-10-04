using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// The cardboard box: cutaway shell, grid, flaps, tape, and the content space holding piece
    /// views, dividers and shelves. The transform origin is the box centre (matches the route
    /// kinematics pose).
    /// </summary>
    public sealed class BoxView : MonoBehaviour
    {
        public const float Cell = ModelLibrary.Cell;
        public const float Depth = 0.32f;
        public const float Wall = 0.022f;

        public int W, H;
        public Transform Contents;        // origin: interior bottom-left, z = 0 at mid depth
        public Transform Shell;
        public readonly List<PieceView> Pieces = new List<PieceView>();
        readonly List<GameObject> statics = new List<GameObject>();
        GameObject grid;
        Transform flapL, flapR, flapB;
        GameObject tape, label;
        float flapClose;                  // 0 open .. 1 closed
        float flapTarget;
        float tapeT, tapeTarget;

        public float InteriorWidth => W * Cell;
        public float InteriorHeight => H * Cell;
        public float OuterHalfHeight => InteriorHeight * 0.5f + Wall;

        public static BoxView Create(Transform parent, int w, int h)
        {
            var go = new GameObject($"Box_{w}x{h}");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BoxView>();
            b.Build(w, h);
            return b;
        }

        void Build(int w, int h)
        {
            W = w; H = h;
            float iw = w * Cell, ih = h * Cell;
            Shell = new GameObject("shell").transform;
            Shell.SetParent(transform, false);
            Contents = new GameObject("contents").transform;
            Contents.SetParent(transform, false);
            Contents.localPosition = new Vector3(-iw * 0.5f, -ih * 0.5f, 0f);

            var shellModel = ModelLibrary.Spawn($"box_{w}x{h}", Shell);
            if (shellModel != null)
            {
                var r = shellModel.transform;
                flapL = ModelLibrary.FindDeep(r, "FlapL");
                flapR = ModelLibrary.FindDeep(r, "FlapR");
                flapB = ModelLibrary.FindDeep(r, "FlapB");
                var t = ModelLibrary.FindDeep(r, "Tape");
                if (t != null) { tape = t.gameObject; tapeFromModel = true; }
                var f = ModelLibrary.FindDeep(r, "Front");
                if (f != null) { front = f.gameObject; front.SetActive(false); }
            }
            else BuildPlaceholderShell(iw, ih);

            // grid on the inside of the back wall
            grid = MeshGen.Make("grid", MeshGen.Quad(), GridMaterial(), Contents, new Vector3(iw * 0.5f, ih * 0.5f, Depth * 0.5f - 0.002f), false);
            grid.transform.localScale = new Vector3(iw, ih, 1f);
            grid.transform.localRotation = Quaternion.identity;
            var gr = grid.GetComponent<MeshRenderer>();
            gr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_BaseMap_ST", new Vector4(w, h, 0, 0));
            gr.SetPropertyBlock(mpb);
            if (tape != null) tape.SetActive(false);
            SetFlaps(0f, true);
        }

        static Material gridMat;
        static Material GridMaterial()
        {
            if (gridMat == null)
            {
                gridMat = Mat.UnlitInstance(new Color(0.35f, 0.2f, 0.08f, 0.55f), TextureLibrary.GridCell);
                gridMat.name = "grid";
            }
            return gridMat;
        }

        void BuildPlaceholderShell(float iw, float ih)
        {
            var kraft = Mat.Lit(Palette.Kraft, 0.2f);
            var inner = Mat.Lit(Palette.KraftLight, 0.15f);
            float t = Wall, d = Depth;
            // floor, back, left, right
            MeshGen.Make("floor", MeshGen.RoundedBox(new Vector3(iw + 2 * t, t, d + t), 0.004f), kraft, Shell, new Vector3(0, -ih * 0.5f - t * 0.5f, t * 0.5f));
            MeshGen.Make("back", MeshGen.RoundedBox(new Vector3(iw + 2 * t, ih + 2 * t, t), 0.004f), inner, Shell, new Vector3(0, 0, d * 0.5f + t * 0.5f));
            MeshGen.Make("left", MeshGen.RoundedBox(new Vector3(t, ih + 2 * t, d + t), 0.004f), kraft, Shell, new Vector3(-iw * 0.5f - t * 0.5f, 0, t * 0.5f));
            MeshGen.Make("right", MeshGen.RoundedBox(new Vector3(t, ih + 2 * t, d + t), 0.004f), kraft, Shell, new Vector3(iw * 0.5f + t * 0.5f, 0, t * 0.5f));
            // flaps hinge on the top edges
            flapL = Hinge("FlapL", new Vector3(-iw * 0.5f - t, ih * 0.5f + t, 0), new Vector3(iw * 0.5f, t, d + t), new Vector3(iw * 0.25f, t * 0.5f, t * 0.5f), kraft);
            flapR = Hinge("FlapR", new Vector3(iw * 0.5f + t, ih * 0.5f + t, 0), new Vector3(iw * 0.5f, t, d + t), new Vector3(-iw * 0.25f, t * 0.5f, t * 0.5f), kraft);
            flapB = Hinge("FlapB", new Vector3(0, ih * 0.5f + t, d * 0.5f + t), new Vector3(iw + 2 * t, t, d * 0.5f), new Vector3(0, t * 0.5f, -d * 0.25f), kraft);
            tape = MeshGen.Make("Tape", MeshGen.RoundedBox(new Vector3(iw + 2 * t + 0.02f, 0.004f, 0.07f), 0.001f), Mat.Lit(Palette.Hex("D9B26F"), 0.6f), Shell, new Vector3(0, ih * 0.5f + 2 * t + 0.002f, 0.02f));
        }

        Transform Hinge(string name, Vector3 hingePos, Vector3 size, Vector3 offset, Material m)
        {
            var h = new GameObject(name).transform;
            h.SetParent(Shell, false);
            h.localPosition = hingePos;
            MeshGen.Make("panel", MeshGen.RoundedBox(size, 0.004f), m, h, offset);
            return h;
        }

        // ---- Flaps & tape -------------------------------------------------------------------

        public void SetFlaps(float closed, bool instant)
        {
            flapTarget = closed;
            if (instant) { flapClose = closed; ApplyFlaps(); }
        }

        public void SetTape(float t, bool instant)
        {
            tapeTarget = t;
            if (instant) { tapeT = t; ApplyTape(); }
        }

        public bool FlapsSettled => Mathf.Abs(flapClose - flapTarget) < 0.01f;

        void ApplyFlaps()
        {
            // open: flaps fold outward and down; closed: flat across the top
            float c = flapClose;
            float open = 1f - c;
            if (flapL != null) flapL.localRotation = Quaternion.Euler(0, 0, 160f * open);
            if (flapR != null) flapR.localRotation = Quaternion.Euler(0, 0, -160f * open);
            if (flapB != null) flapB.localRotation = Quaternion.Euler(-150f * Mathf.Clamp01(open * 1.4f), 0, 0);
        }

        bool tapeFromModel;

        void ApplyTape()
        {
            if (tape == null) return;
            tape.SetActive(tapeT > 0.001f);
            tape.transform.localScale = new Vector3(Mathf.Max(0.001f, tapeT), 1, 1);
            if (!tapeFromModel)
            {
                var p = tape.transform.localPosition;
                tape.transform.localPosition = new Vector3(-(InteriorWidth + 2 * Wall) * 0.5f * (1f - tapeT), p.y, p.z);
            }
        }

        /// <summary>Swaps the tape texture (cosmetic unlocks).</summary>
        public void SetTapeStyle(string style)
        {
            if (tape == null) return;
            var mat = TextureLibrary.MaterialFor("tape_" + style);
            foreach (var r in tape.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        public Vector3 TapeStartWorld => tape != null ? tape.transform.position : transform.position;
        public Vector3 TapeEndWorld => tape != null ? tape.transform.TransformPoint(new Vector3(InteriorWidth + 2 * Wall + 0.008f, 0, 0)) : transform.position;
        public float TapeProgress => tapeT;

        void Update()
        {
            float dt = Time.deltaTime;
            if (Mathf.Abs(flapClose - flapTarget) > 1e-4f)
            {
                flapClose = Mathf.MoveTowards(flapClose, flapTarget, dt * 2.6f);
                ApplyFlaps();
            }
            if (Mathf.Abs(tapeT - tapeTarget) > 1e-4f)
            {
                tapeT = Mathf.MoveTowards(tapeT, tapeTarget, dt * 1.6f);
                ApplyTape();
            }
        }

        GameObject front;

        public void ShowGrid(bool on) { if (grid != null) grid.SetActive(on); }

        /// <summary>Closes the cutaway front (title screen parcel).</summary>
        public void ShowFront(bool on) { if (front != null) front.SetActive(on); }

        // ---- Content helpers ------------------------------------------------------------------

        public Vector3 CellToLocal(float cx, float cy) => new Vector3(cx * Cell, cy * Cell, 0f);

        public Vector3 CellToWorld(float cx, float cy) => Contents.TransformPoint(CellToLocal(cx, cy));

        /// <summary>Mouse ray to content-space cell coordinates (continuous). False if parallel.</summary>
        public bool RayToCell(Ray ray, out Vector2 cell)
        {
            var plane = new Plane(Contents.forward * -1f, Contents.position);
            cell = default;
            if (!plane.Raycast(ray, out float d)) return false;
            var local = Contents.InverseTransformPoint(ray.GetPoint(d));
            cell = new Vector2(local.x / Cell, local.y / Cell);
            return true;
        }

        public void ClearStatics()
        {
            foreach (var g in statics) if (g != null) Destroy(g);
            statics.Clear();
        }

        public void BuildStatics(Packing pk)
        {
            ClearStatics();
            foreach (int d in pk.Dividers) statics.Add(MakeDivider(d, Contents, false));
            foreach (var s in pk.Shelves)
            {
                pk.ShelfSpan(s, out int x0, out int x1);
                statics.Add(MakeShelf(s.Row, x0, x1, Contents, false));
            }
        }

        public GameObject MakeDivider(int line, Transform parent, bool ghost)
        {
            var go = ModelLibrary.Spawn("divider", parent);
            if (go == null)
            {
                go = MeshGen.Make("divider", MeshGen.RoundedBox(new Vector3(0.12f * Cell, 1f, Depth * 0.98f), 0.003f), Mat.Lit(Palette.KraftDark, 0.2f), parent);
            }
            go.transform.localPosition = new Vector3(line * Cell, InteriorHeight * 0.5f, 0);
            go.transform.localScale = new Vector3(1, InteriorHeight, 1);
            if (ghost) SetGhost(go);
            return go;
        }

        public GameObject MakeShelf(int row, int x0, int x1, Transform parent, bool ghost)
        {
            var go = ModelLibrary.Spawn("shelf", parent);
            float a = x0 * Cell + (x0 == 0 ? 0 : 0.06f * Cell), b = x1 * Cell - (x1 == W ? 0 : 0.06f * Cell);
            if (go == null)
            {
                go = MeshGen.Make("shelf", MeshGen.RoundedBox(new Vector3(1f, 0.06f * Cell * 1.2f, Depth * 0.98f), 0.003f), Mat.Lit(Palette.Kraft, 0.2f), parent);
            }
            go.transform.localPosition = new Vector3((a + b) * 0.5f, row * Cell, 0);
            go.transform.localScale = new Vector3(b - a, 1, 1);
            if (ghost) SetGhost(go);
            return go;
        }

        static void SetGhost(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Mat.Glass(new Color(1f, 1f, 1f, 0.4f), 0.2f);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public static void TintGhost(GameObject go, Color c)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Mat.Glass(c, 0.2f);
                r.sharedMaterials = mats;
            }
        }
    }
}
