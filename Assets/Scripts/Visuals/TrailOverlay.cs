using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// After a journey, the packing view shows each item's path from the last run as a faint
    /// trail, plus a marker where something went wrong. Makes failures legible and fixable.
    /// </summary>
    public sealed class TrailOverlay : MonoBehaviour
    {
        readonly List<GameObject> objects = new List<GameObject>();

        public void Hide()
        {
            foreach (var o in objects) if (o != null) Destroy(o);
            objects.Clear();
        }

        public void Show(BoxView box, Recording rec)
        {
            Hide();
            if (rec == null || box == null) return;
            transform.SetParent(box.Contents, false);
            transform.localPosition = new Vector3(0, 0, -BoxView.Depth * 0.5f - 0.01f);
            transform.localRotation = Quaternion.identity;
            var failedBodies = new HashSet<int>();
            foreach (var inc in rec.Incidents) if (inc.IsFailure) failedBodies.Add(inc.Body);

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
                bool failed = failedBodies.Contains(b);
                var col = failed ? new Color(0.95f, 0.3f, 0.25f, 0.85f) : Palette.ItemColor(info.Kind);
                col.a = failed ? 0.85f : 0.5f;
                var go = new GameObject("trail_" + info.Kind);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                lr.widthMultiplier = 0.012f;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 2;
                lr.sharedMaterial = Mat.Unlit(col, true);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0.15f, 0), new GradientAlphaKey(1f, 1) });
                lr.colorGradient = grad;
                objects.Add(go);
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
            }
        }
    }
}
