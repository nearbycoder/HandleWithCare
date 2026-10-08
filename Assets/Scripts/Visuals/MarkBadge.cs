using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// A small badge on the bench, drawn in front of the box: a check or a cross in a square outlined in ink.
    /// Mabel's hint ghosts carry one, so their state reads by its shape as well as its colour.
    /// </summary>
    public static class MarkBadge
    {
        public const float Size = 0.075f;
        static readonly Color Ink = new Color(0.16f, 0.12f, 0.1f, 0.95f);
        static readonly Color CheckFace = new Color(0.24f, 0.62f, 0.38f, 0.95f);
        static readonly Color CrossFace = new Color(0.86f, 0.24f, 0.2f, 0.95f);
        static readonly Color Symbol = new Color(0.98f, 0.96f, 0.9f, 1f);

        public static GameObject Make(Transform parent, bool check, Vector3 localPos)
        {
            var go = new GameObject(check ? "badgeCheck" : "badgeCross");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var edge = MeshGen.Make("edge", MeshGen.Quad(), Mat.Unlit(Ink, true), go.transform, Vector3.zero, false);
            edge.transform.localScale = new Vector3(Size, Size, 1f);
            var face = MeshGen.Make("face", MeshGen.Quad(), Mat.Unlit(check ? CheckFace : CrossFace, true), go.transform, new Vector3(0, 0, -0.004f), false);
            face.transform.localScale = new Vector3(Size * 0.8f, Size * 0.8f, 1f);
            face.GetComponent<Renderer>().sortingOrder = 3;
            var sym = Mat.Unlit(Symbol, true);
            void Bar(Vector2 from, Vector2 to)
            {
                var d = to - from;
                var bar = MeshGen.Make("bar", MeshGen.Quad(), sym, go.transform, new Vector3((from.x + to.x) * 0.5f, (from.y + to.y) * 0.5f, -0.012f), false);
                bar.transform.localScale = new Vector3(d.magnitude + Size * 0.11f, Size * 0.13f, 1f);
                bar.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                bar.GetComponent<Renderer>().sortingOrder = 4;
            }
            float s = Size;
            if (check)
            {
                var v = new Vector2(-0.08f * s, -0.2f * s);
                Bar(v, new Vector2(-0.27f * s, 0.0f));
                Bar(v, new Vector2(0.27f * s, 0.24f * s));
            }
            else
            {
                Bar(new Vector2(-0.22f * s, -0.22f * s), new Vector2(0.22f * s, 0.22f * s));
                Bar(new Vector2(-0.22f * s, 0.22f * s), new Vector2(0.22f * s, -0.22f * s));
            }
            edge.GetComponent<Renderer>().sortingOrder = 2;
            return go;
        }
    }
}
