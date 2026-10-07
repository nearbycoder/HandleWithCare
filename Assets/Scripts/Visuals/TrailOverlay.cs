using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// After a journey, the packing view shows each item's path from the last run as a faint
    /// trail, plus a red cross where something went wrong, and an amber "!" where an item that still
    /// arrived went past the care line (a near miss). Makes failures legible and fixable.
    /// </summary>
    public sealed class TrailOverlay : MonoBehaviour
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<(int body, LineRenderer line)> lines = new List<(int, LineRenderer)>();
        readonly List<(int body, Transform mark)> marks = new List<(int, Transform)>();
        const float LineWidth = 0.012f;

        public void Hide()
        {
            foreach (var o in objects) if (o != null) Destroy(o);
            objects.Clear();
            lines.Clear();
            marks.Clear();
            NearMarks = 0;
        }

        /// <summary>An item's trails and crosses stand out while its card shows (every item of that kind, or
        /// one recorded body); null lets them all go back.</summary>
        public void Highlight(Recording rec, PieceKind? kind, int body)
        {
            bool On(int b) => body >= 0 ? b == body : kind.HasValue && rec != null && b < rec.Bodies.Length && rec.Bodies[b].Kind == kind.Value;
            foreach (var (b, lr) in lines) if (lr != null) lr.widthMultiplier = On(b) ? LineWidth * 2.4f : LineWidth;
            foreach (var (b, m) in marks) if (m != null) m.localScale = Vector3.one * (On(b) ? 1.6f : 1f);
        }
        public int HighlightedMarks()
        {
            int n = 0;
            foreach (var (_, m) in marks) if (m != null && m.localScale.x > 1.01f) n++;
            return n;
        }
        /// <summary>For the self-tests: the amber near-miss marks shown.</summary>
        public int NearMarks { get; private set; }
        public static readonly Color NearColor = new Color(0.96f, 0.62f, 0.12f, 0.95f);

        public void Show(BoxView box, Recording rec)
        {
            Hide();
            if (rec == null || box == null) return;
            transform.SetParent(box.Contents, false);
            transform.localPosition = new Vector3(0, 0, -BoxView.Depth * 0.5f - 0.01f);
            transform.localRotation = Quaternion.identity;
            var failedBodies = new HashSet<int>();
            var nearBodies = new HashSet<int>();
            foreach (var tr in rec.Troubles) (tr.Failure ? failedBodies : nearBodies).Add(tr.Body);

            for (int b = 0; b < rec.Bodies.Length; b++)
            {
                var info = rec.Bodies[b];
                if (info.Type != BodyType.Piece) continue;
                var def = Catalog.Get(info.Kind);
                if (def.IsPadding) continue;
                var pts = new List<Vector3>();
                Vector2 last = new Vector2(-99, -99);
                for (int f = 0; f < rec.Frames.Count; f += 2)
                {
                    var fr = rec.Frames[f][b];
                    if ((fr.State & BodyState.Removed) != 0) break;
                    var p = new Vector2(fr.Pos.x, fr.Pos.y);
                    if ((p - last).sqrMagnitude < 0.0025f) continue;
                    last = p;
                    pts.Add(new Vector3(p.x * BoxView.Cell, p.y * BoxView.Cell, 0));
                }
                if (pts.Count < 2) continue;
                bool failed = failedBodies.Contains(b), near = !failed && nearBodies.Contains(b);
                var col = failed ? new Color(0.95f, 0.3f, 0.25f, 0.85f) : near ? NearColor : Palette.ItemColor(info.Kind);
                col.a = failed ? 0.85f : near ? 0.75f : 0.5f;
                var go = new GameObject("trail_" + info.Kind);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr.widthMultiplier = LineWidth;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 2;
                lr.sharedMaterial = Mat.Unlit(col, true);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0.15f, 0), new GradientAlphaKey(1f, 1) });
                lr.colorGradient = grad;
                objects.Add(go);
                lines.Add((b, lr));
            }

            // failure markers
            foreach (var inc in rec.Incidents)
            {
                if (!inc.IsFailure) continue;
                var go = new GameObject("marker");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(inc.Where.x * BoxView.Cell, inc.Where.y * BoxView.Cell, -0.005f);
                for (int k = 0; k < 2; k++)
                {
                    var bar = MeshGen.Make("x", MeshGen.Quad(), Mat.Unlit(new Color(0.9f, 0.18f, 0.15f, 0.9f), true), go.transform, Vector3.zero, false);
                    bar.transform.localScale = new Vector3(0.11f, 0.022f, 1f);
                    bar.transform.localRotation = Quaternion.Euler(0, 0, k == 0 ? 45 : -45);
                }
                objects.Add(go);
                marks.Add((inc.Body, go.transform));
            }

            // near-miss markers: a little amber warning sign (a diamond with "!") where the knock happened,
            // outlined in ink so it reads over any item
            NearMarks = 0;
            var ink = Mat.Unlit(new Color(0.16f, 0.12f, 0.1f, 0.92f), true);
            var amber = Mat.Unlit(NearColor, true);
            foreach (var tr in rec.Troubles)
            {
                if (tr.Failure) continue;
                var go = new GameObject("nearMarker");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(tr.Where.x * BoxView.Cell, tr.Where.y * BoxView.Cell, -0.006f);
                var edge = MeshGen.Make("edge", MeshGen.Quad(), ink, go.transform, Vector3.zero, false);
                edge.transform.localScale = new Vector3(0.1f, 0.1f, 1f);
                edge.transform.localRotation = Quaternion.Euler(0, 0, 45);
                var face = MeshGen.Make("face", MeshGen.Quad(), amber, go.transform, new Vector3(0, 0, -0.004f), false);
                face.transform.localScale = new Vector3(0.08f, 0.08f, 1f);
                face.transform.localRotation = Quaternion.Euler(0, 0, 45);
                // (well in front: the bench camera looks down, and transparent quads sort by distance)
                var bar = MeshGen.Make("bar", MeshGen.Quad(), ink, go.transform, new Vector3(0, 0.009f, -0.012f), false);
                bar.transform.localScale = new Vector3(0.016f, 0.046f, 1f);
                var dot = MeshGen.Make("dot", MeshGen.Quad(), ink, go.transform, new Vector3(0, -0.028f, -0.012f), false);
                dot.transform.localScale = new Vector3(0.016f, 0.015f, 1f);
                // and drawn in order whatever the distance
                face.GetComponent<Renderer>().sortingOrder = 1;
                bar.GetComponent<Renderer>().sortingOrder = dot.GetComponent<Renderer>().sortingOrder = 2;
                objects.Add(go);
                marks.Add((tr.Body, go.transform));
                NearMarks++;
            }
        }
    }
}
