using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// A journey set piece for one leg. Built from the leg's event spans so that every bump,
    /// pothole, drop and stair is physically there where the sim felt it.
    /// World frame: leg-local kinematics X/Y map to (X, Y) with the box centre at rest at
    /// (0, BoxHalfH + Floor) where Floor is the surface height the box starts on.
    /// </summary>
    public sealed class StageSet : MonoBehaviour
    {
        public LegKind Kind;
        public float BoxHalfH, BoxHalfW;
        public Transform Vehicle;          // follows the box during the leg (van, plane hold)
        public Vector3 VehicleOffset;
        public float CameraDistance = 3f;
        public Vector3 CameraOffset = new Vector3(0.9f, 0.5f, -1f);
        public float VehicleWheelSpin;
        readonly List<Transform> wheels = new List<Transform>();

        public Vector3 BoxWorld(double x, double y) => transform.TransformPoint(new Vector3((float)x, (float)y + BoxHalfH, 0f));

        public void FollowBox(Vector3 boxPos, float angleDeg, float speed)
        {
            if (Vehicle == null) return;
            Vehicle.position = boxPos + Quaternion.Euler(0, 0, angleDeg) * VehicleOffset;
            Vehicle.rotation = Quaternion.Euler(0, 0, angleDeg);
            foreach (var w in wheels) w.Rotate(0, 0, -speed * Time.deltaTime * 360f / (Mathf.PI * 0.6f), Space.Self);
        }

        public static StageSet Build(Transform parent, LegKind kind, List<EventSpan> spans, float boxHalfW, float boxHalfH, Vector3 origin)
        {
            var go = new GameObject("Stage_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            var s = go.AddComponent<StageSet>();
            s.Kind = kind;
            s.BoxHalfH = boxHalfH;
            s.BoxHalfW = boxHalfW;
            float minX = -6, maxX = 6;
            foreach (var sp in spans) { minX = Mathf.Min(minX, (float)sp.X0 - 4); maxX = Mathf.Max(maxX, (float)sp.X1 + 8); }
            switch (kind)
            {
                case LegKind.Van: s.BuildVan(spans, minX, maxX); break;
                case LegKind.Depot: s.BuildDepot(spans, minX, maxX); break;
                case LegKind.Doorstep: s.BuildDoorstep(spans, minX, maxX); break;
                default: s.BuildGeneric(spans, minX, maxX); break;
            }
            return s;
        }

        GameObject Box(string name, Vector3 size, Vector3 pos, Color c, Transform parent = null, float r = 0.02f)
        {
            return MeshGen.Make(name, MeshGen.RoundedBox(size, r), Mat.Lit(c, 0.25f), parent ?? transform, pos);
        }

        void Ground(float minX, float maxX, float y, Color c)
        {
            Box("ground", new Vector3(maxX - minX + 40, 0.2f, 40), new Vector3((minX + maxX) * 0.5f, y - 0.1f, 15), c, null, 0.01f);
        }

        void Trees(float minX, float maxX, float y, float z0, int seed)
        {
            var rng = new System.Random(seed);
            for (float x = minX - 10; x < maxX + 10; x += 2.2f + (float)rng.NextDouble() * 2.5f)
            {
                float z = z0 + (float)rng.NextDouble() * 6f;
                float h = 1.6f + (float)rng.NextDouble() * 1.6f;
                var tree = ModelLibrary.Spawn(rng.NextDouble() < 0.5 ? "tree_round" : "tree_tall", transform);
                if (tree != null)
                {
                    tree.transform.localPosition = new Vector3(x, y, z);
                    tree.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f);
                    tree.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    continue;
                }
                Box("trunk", new Vector3(0.18f, h * 0.5f, 0.18f), new Vector3(x, y + h * 0.25f, z), Palette.WoodDark);
                var crown = MeshGen.Make("crown", MeshGen.Sphere(), Mat.Lit(Palette.Hex(rng.NextDouble() < 0.5 ? "6FA35A" : "86B860"), 0.15f), transform, new Vector3(x, y + h * 0.6f, z));
                crown.transform.localScale = new Vector3(1.2f, 1.1f, 1.2f) * (0.7f + h * 0.25f);
            }
        }

        void BuildVan(List<EventSpan> spans, float minX, float maxX)
        {
            // box rests on the van's cargo floor; road is 0.55 m below the cargo floor
            float floorY = 0f;
            float roadY = floorY - 0.62f;
            Ground(minX, maxX, roadY - 0.02f, Palette.Hex("8DB36B"));
            Box("road", new Vector3(maxX - minX + 30, 0.06f, 3.2f), new Vector3((minX + maxX) * 0.5f, roadY, 0.9f), Palette.Hex("5E5A5C"), null, 0.01f);
            for (float x = minX - 10; x < maxX + 15; x += 2.4f)
                Box("dash", new Vector3(1.1f, 0.01f, 0.12f), new Vector3(x, roadY + 0.035f, 1.5f), Palette.Hex("F2E6C4"), null, 0.004f);
            foreach (var sp in spans)
            {
                float x = (float)sp.X0;
                switch (sp.Kind)
                {
                    case EventKind.Bump:
                    case EventKind.SpeedBump:
                        var b = MeshGen.Make("bump", MeshGen.Sphere(), Mat.Lit(sp.Kind == EventKind.SpeedBump ? Palette.Sticky : Palette.Hex("6B6466"), 0.3f), transform, new Vector3((float)(sp.X0 + sp.X1) * 0.5f, roadY, 0.9f));
                        b.transform.localScale = new Vector3(0.6f, (float)sp.Def.A * 2.5f, 3.0f);
                        break;
                    case EventKind.Pothole:
                        Box("pothole", new Vector3(0.7f, 0.01f, 1.2f), new Vector3((float)(sp.X0 + sp.X1) * 0.5f, roadY + 0.032f, 0.4f), Palette.Hex("2E2A2C"), null, 0.005f);
                        break;
                    case EventKind.Cobbles:
                        for (float cx = (float)sp.X0; cx < sp.X1; cx += 0.32f)
                            for (int k = 0; k < 6; k++)
                                Box("cobble", new Vector3(0.28f, 0.03f, 0.45f), new Vector3(cx + (k % 2) * 0.16f, roadY + 0.03f, -0.4f + k * 0.5f), Palette.Hex(k % 2 == 0 ? "8A8079" : "9C918A"), null, 0.012f);
                        break;
                }
            }
            Trees(minX, maxX, roadY, 4.5f, 11);

            // the van: built around the box, cutaway on the camera side
            var van = new GameObject("van").transform;
            van.SetParent(transform, false);
            Vehicle = van;
            VehicleOffset = new Vector3(0, -BoxHalfH, 0);
            float len = Mathf.Max(2.6f, BoxHalfW * 2 + 1.6f);
            var white = Palette.Hex("F4F1EA");
            var red = Palette.PostalRed;
            Box("cargoFloor", new Vector3(len, 0.08f, 1.5f), new Vector3(0.2f, -0.04f, 0.15f), Palette.Hex("7C6A5A"), van);
            Box("backWall", new Vector3(len, 1.7f, 0.06f), new Vector3(0.2f, 0.85f, 0.9f), white, van);
            Box("roof", new Vector3(len, 0.08f, 1.5f), new Vector3(0.2f, 1.74f, 0.15f), white, van);
            Box("stripe", new Vector3(len, 0.18f, 0.065f), new Vector3(0.2f, 0.5f, 0.88f), red, van);
            Box("rearDoor", new Vector3(0.08f, 1.7f, 1.5f), new Vector3(0.2f - len * 0.5f, 0.85f, 0.15f), white, van);
            Box("cab", new Vector3(1.1f, 1.4f, 1.5f), new Vector3(0.2f + len * 0.5f + 0.5f, 0.6f, 0.15f), white, van);
            Box("hood", new Vector3(0.7f, 0.6f, 1.5f), new Vector3(0.2f + len * 0.5f + 1.3f, 0.2f, 0.15f), white, van);
            Box("chassis", new Vector3(len + 2f, 0.2f, 1.4f), new Vector3(0.6f, -0.2f, 0.15f), Palette.Hex("3A3638"), van);
            foreach (float wx in new[] { 0.2f - len * 0.35f, 0.2f + len * 0.5f + 1.0f })
            {
                var w = new GameObject("wheel").transform;
                w.SetParent(van, false);
                w.localPosition = new Vector3(wx, -0.33f, -0.55f);
                var tire = MeshGen.Make("tire", MeshGen.RoundedBox(new Vector3(0.6f, 0.6f, 0.25f), 0.12f), Mat.Lit(Palette.Hex("2A2628"), 0.4f), w);
                MeshGen.Make("hub", MeshGen.RoundedBox(new Vector3(0.28f, 0.28f, 0.27f), 0.06f), Mat.Lit(Palette.Hex("C9C4BE"), 0.6f, 0.6f), w);
                wheels.Add(w);
            }
            CameraOffset = new Vector3(0.7f, 0.55f, -1f);
        }

        void BuildDepot(List<EventSpan> spans, float minX, float maxX)
        {
            Ground(minX, maxX, -3f, Palette.Hex("8C8682"));
            Box("backwall", new Vector3(maxX - minX + 30, 8f, 0.2f), new Vector3((minX + maxX) * 0.5f, 1f, 3f), Palette.Hex("C9BFAE"), null, 0.01f);
            foreach (var sp in spans)
            {
                float x0 = (float)sp.X0, x1 = (float)sp.X1;
                float y0 = (float)sp.Y0;
                switch (sp.Kind)
                {
                    case EventKind.Conveyor:
                    case EventKind.Rest:
                        Box("belt", new Vector3(x1 - x0 + 1.2f, 0.12f, 0.9f), new Vector3((x0 + x1) * 0.5f, y0 - 0.06f, 0.1f), Palette.Hex("3C3A3D"), null, 0.03f);
                        Box("beltFrame", new Vector3(x1 - x0 + 1.3f, 0.3f, 0.95f), new Vector3((x0 + x1) * 0.5f, y0 - 0.27f, 0.1f), Palette.Hex("E0A23A"), null, 0.02f);
                        break;
                    case EventKind.Drop:
                        float landY = (float)sp.Y1;
                        Box("bin", new Vector3(1.6f, 0.5f, 1.2f), new Vector3(x1, landY - 0.25f, 0.1f), Palette.Hex("4E7FA8"), null, 0.04f);
                        break;
                    case EventKind.Chute:
                        float ang = sp.Def.A;
                        var ch = Box("chute", new Vector3((x1 - x0) / Mathf.Cos(ang * Mathf.Deg2Rad) + 0.8f, 0.08f, 0.95f), new Vector3((x0 + x1) * 0.5f, ((float)sp.Y0 + (float)sp.Y1) * 0.5f - 0.04f, 0.1f), Palette.Hex("B8BCC2"), null, 0.02f);
                        ch.transform.localRotation = Quaternion.Euler(0, 0, -ang);
                        Box("bumper", new Vector3(0.12f, 0.4f, 0.95f), new Vector3(x1 + BoxHalfW + 0.1f, (float)sp.Y1 + 0.1f, 0.1f), Palette.PostalRed, null, 0.03f);
                        Box("chuteFloor", new Vector3(2.5f, 0.1f, 1.2f), new Vector3(x1 + 0.4f, (float)sp.Y1 - 0.05f, 0.1f), Palette.Hex("6E6A66"), null, 0.02f);
                        break;
                    case EventKind.ArmTip:
                        Box("armBase", new Vector3(0.5f, 1.4f, 0.5f), new Vector3(x0, y0 + 0.7f + 0.6f, 0.9f), Palette.Hex("E0A23A"), null, 0.06f);
                        Box("table", new Vector3(1.4f, 0.1f, 1f), new Vector3(x0, y0 - 0.05f, 0.1f), Palette.Hex("6E6A66"), null, 0.02f);
                        break;
                }
            }
            CameraOffset = new Vector3(0.6f, 0.7f, -1f);
        }

        void BuildDoorstep(List<EventSpan> spans, float minX, float maxX)
        {
            Ground(minX, maxX, -1.0f, Palette.Hex("8DB36B"));
            Box("street", new Vector3(maxX - minX + 30, 0.1f, 3f), new Vector3((minX + maxX) * 0.5f, -1.0f, -1f), Palette.Hex("6B6466"), null, 0.01f);
            foreach (var sp in spans)
            {
                if (sp.Kind == EventKind.Stairs)
                {
                    int n = (int)sp.Def.A;
                    float sh = sp.Def.B > 0 ? sp.Def.B : 0.18f;
                    for (int i = 0; i < n; i++)
                        Box("step", new Vector3(0.6f, sh * (i + 1) + 1.0f, 1.6f), new Vector3((float)sp.X0 + 0.28f * (i + 0.5f) + 0.6f, (float)sp.Y0 - 1.0f * 0.5f + sh * (i + 1) * 0.5f - BoxHalfH - 0.6f, 1.2f), Palette.Hex("C9B9A5"), null, 0.02f);
                }
                if (sp.Kind == EventKind.Toss)
                {
                    float px = (float)sp.X1, py = (float)sp.Y1 - BoxHalfW;
                    Box("porch", new Vector3(4f, 0.2f, 2.4f), new Vector3(px, py - 0.1f, 0.6f), Palette.Hex("A9805A"), null, 0.02f);
                    Box("door", new Vector3(1.0f, 2.1f, 0.12f), new Vector3(px + 0.8f, py + 1.05f, 1.6f), Palette.Teal, null, 0.03f);
                    Box("house", new Vector3(6f, 4f, 0.3f), new Vector3(px, py + 2f, 1.8f), Palette.Hex("F0D9B5"), null, 0.02f);
                }
            }
            Trees(minX, maxX, -1.0f, 5f, 23);
            CameraOffset = new Vector3(0.7f, 0.6f, -1f);
        }

        void BuildGeneric(List<EventSpan> spans, float minX, float maxX)
        {
            Ground(minX, maxX, -0.8f, Kind == LegKind.Ship ? Palette.Hex("3F7FA6") : Palette.Hex("A7B8C9"));
            Box("deck", new Vector3(maxX - minX + 4, 0.1f, 2f), new Vector3((minX + maxX) * 0.5f, -0.05f, 0.2f), Palette.Wood, null, 0.01f);
            CameraOffset = new Vector3(0.7f, 0.6f, -1f);
        }
    }
}
