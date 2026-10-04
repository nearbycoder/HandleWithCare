using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>Lighting and sky for one environment.</summary>
    public struct LightingPreset
    {
        public Vector3 SunEuler;
        public Color Sun;
        public float SunIntensity;
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public Color SkyTop, SkyBottom;

        public static LightingPreset For(LegKind? kind)
        {
            switch (kind)
            {
                case LegKind.Van: return Make(new Vector3(38, -35, 0), "FFE9CC", 2.0f, "A9C6E8", "D8C9AE", "6E7A5A", "78AEE0", "F4E6CF");
                case LegKind.Depot: return Make(new Vector3(55, -20, 0), "FFF1DC", 1.6f, "B9C3CC", "B7A994", "5C5650", "8C8682", "8C8682");
                case LegKind.Doorstep: return Make(new Vector3(28, -50, 0), "FFD9A8", 2.1f, "A8B8E0", "E0C1A0", "6E6A50", "8FB2E6", "FCD8B0");
                case LegKind.Ship: return Make(new Vector3(45, -30, 0), "FFF4E0", 2.0f, "A6CCEC", "D0D8DC", "3F6F8C", "5FA6E0", "DCEFF8");
                case LegKind.Plane: return Make(new Vector3(30, 20, 0), "FFF6E8", 1.8f, "B4CFEA", "D9D4CC", "7A7E86", "6FA8E6", "F2F6FA");
                case LegKind.Catapult: return Make(new Vector3(18, -60, 0), "FFC48A", 2.2f, "B7A6D9", "F0B98C", "6A5A50", "7E8FD6", "FFC69A");
                default: return Make(new Vector3(42, -28, 0), "FFE6C7", 1.9f, "9AA7C4", "C9A98A", "5C4434", "2B2230", "2B2230");
            }
        }

        static LightingPreset Make(Vector3 e, string sun, float i, string sky, string eq, string gr, string top, string bottom) => new LightingPreset
        {
            SunEuler = e, Sun = Palette.Hex(sun), SunIntensity = i,
            AmbientSky = Palette.Hex(sky), AmbientEquator = Palette.Hex(eq), AmbientGround = Palette.Hex(gr),
            SkyTop = Palette.Hex(top), SkyBottom = Palette.Hex(bottom),
        };

        public void Apply(Light sun, Camera cam)
        {
            sun.transform.rotation = Quaternion.Euler(SunEuler);
            sun.color = Sun;
            sun.intensity = SunIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky * 0.9f;
            RenderSettings.ambientEquatorColor = AmbientEquator * 0.8f;
            RenderSettings.ambientGroundColor = AmbientGround * 0.6f;
            cam.backgroundColor = SkyBottom;
        }
    }

    /// <summary>
    /// A journey set piece for one leg, built from the leg's event spans so that every bump,
    /// pothole, drop, stair and chute is physically there where the sim felt it.
    /// Box pose (X, Y) from the kinematics is the box's bottom-centre at rest; BoxWorld adds the
    /// half height to get the centre.
    /// </summary>
    public sealed class StageSet : MonoBehaviour
    {
        public LegKind Kind;
        public float BoxHalfH, BoxHalfW;
        public Transform Vehicle;
        public Vector3 VehicleOffset;
        public Vector3 CameraOffset = new Vector3(0.9f, 0.5f, -1f);
        public LightingPreset Lighting;
        readonly List<Transform> wheels = new List<Transform>();
        readonly List<EventSpan> spans = new List<EventSpan>();
        Transform courier, courierArmL, courierArmR, courierLegL, courierLegR, courierHead;
        Transform armBase, armUpper, armFore, armGrip;
        Transform catapultArm;
        readonly List<(Transform t, float speed, float baseY)> bobbers = new List<(Transform, float, float)>();
        GameObject sky;
        float walkPhase;
        Vector3 lastBoxPos;
        bool tossed;

        public Vector3 BoxWorld(double x, double y) => transform.TransformPoint(new Vector3((float)x, (float)y + BoxHalfH, 0f));

        GameObject Spawn(string id, Vector3 pos, float yaw = 0, Vector3? scale = null, Transform parent = null)
        {
            var go = ModelLibrary.Spawn(id, parent ?? transform);
            if (go == null) return null;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return go;
        }

        public static StageSet Build(Transform parent, LegKind kind, List<EventSpan> legSpans, float boxHalfW, float boxHalfH, Vector3 origin)
        {
            var go = new GameObject("Stage_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            var s = go.AddComponent<StageSet>();
            s.Kind = kind;
            s.BoxHalfH = boxHalfH;
            s.BoxHalfW = boxHalfW;
            s.spans.AddRange(legSpans);
            s.Lighting = LightingPreset.For(kind);
            float minX = 0, maxX = 0;
            foreach (var sp in legSpans) { minX = Mathf.Min(minX, (float)Mathf.Min((float)sp.X0, (float)sp.X1)); maxX = Mathf.Max(maxX, (float)Mathf.Max((float)sp.X0, (float)sp.X1)); }
            minX -= 8; maxX += 10;
            switch (kind)
            {
                case LegKind.Van: s.BuildVan(minX, maxX); break;
                case LegKind.Depot: s.BuildDepot(minX, maxX); break;
                case LegKind.Doorstep: s.BuildDoorstep(minX, maxX); break;
                case LegKind.Ship: s.BuildShip(minX, maxX); break;
                case LegKind.Plane: s.BuildPlane(minX, maxX); break;
                case LegKind.Catapult: s.BuildCatapult(minX, maxX); break;
            }
            s.BuildSky(minX, maxX);
            return s;
        }

        void BuildSky(float minX, float maxX)
        {
            if (Kind == LegKind.Depot) return;
            sky = new GameObject("sky");
            sky.transform.SetParent(transform, false);
            var tex = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float t = y / 63f;
                var c = Color.Lerp(Lighting.SkyBottom, Lighting.SkyTop, Mathf.SmoothStep(0, 1, t));
                tex.SetPixel(0, y, c); tex.SetPixel(1, y, c);
            }
            tex.Apply();
            var quad = MeshGen.Make("skyQuad", MeshGen.Quad(), Mat.UnlitInstance(Color.white, tex), sky.transform, Vector3.zero, false);
            quad.transform.localPosition = new Vector3((minX + maxX) * 0.5f, 6f, 60f);
            quad.transform.localScale = new Vector3(maxX - minX + 300f, 80f, 1f);
            quad.transform.localRotation = Quaternion.Euler(0, 180, 0);
            quad.GetComponent<MeshRenderer>().receiveShadows = false;
            var rnd = new System.Random(31);
            for (float x = minX - 20; x < maxX + 20; x += 9f + (float)rnd.NextDouble() * 8f)
            {
                var c = Spawn("prop_cloud", new Vector3(x, 5f + (float)rnd.NextDouble() * 4f, 25f + (float)rnd.NextDouble() * 15f), (float)rnd.NextDouble() * 360f, Vector3.one * (1.2f + (float)rnd.NextDouble()));
                if (c != null)
                {
                    foreach (var r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bobbers.Add((c.transform, 0.3f + (float)rnd.NextDouble() * 0.3f, c.transform.localPosition.y));
                }
            }
        }

        void Scenery(float minX, float maxX, float groundY, int seed, bool houses = true)
        {
            var rnd = new System.Random(seed);
            string[] trees = { "tree_round", "tree_pine", "tree_round" };
            for (float x = minX; x < maxX; x += 1.6f + (float)rnd.NextDouble() * 2.2f)
            {
                float z = 5.5f + (float)rnd.NextDouble() * 6f;
                float roll = (float)rnd.NextDouble();
                if (houses && roll < 0.18f)
                {
                    Spawn("house_" + rnd.Next(3), new Vector3(x, groundY, z + 3f), 180f + (float)(rnd.NextDouble() * 20 - 10));
                    x += 2.5f;
                }
                else if (roll < 0.65f) Spawn(trees[rnd.Next(trees.Length)], new Vector3(x, groundY, z), (float)rnd.NextDouble() * 360f, Vector3.one * (0.8f + (float)rnd.NextDouble() * 0.6f));
                else Spawn("prop_bush", new Vector3(x, groundY, z - 2f), (float)rnd.NextDouble() * 360f, Vector3.one * (0.8f + (float)rnd.NextDouble() * 0.5f));
            }
            for (float x = minX; x < maxX; x += 2f) Spawn("prop_fence", new Vector3(x, groundY, 3.4f), 0);
            for (float x = minX - 10; x < maxX + 10; x += 16f + (float)rnd.NextDouble() * 8f)
                Spawn("prop_hill", new Vector3(x, groundY - 1.5f, 28f + (float)rnd.NextDouble() * 10f), (float)rnd.NextDouble() * 360f, Vector3.one * (1.2f + (float)rnd.NextDouble()));
            var ground = MeshGen.Make("ground", MeshGen.RoundedBox(new Vector3(maxX - minX + 120, 0.2f, 80), 0.01f), Mat.Lit(Palette.Hex("8DB36B"), 0.1f), transform,
                new Vector3((minX + maxX) * 0.5f, groundY - 0.11f, 30f));
            ground.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---- Van -------------------------------------------------------------------------------

        float HillY(float x)
        {
            float y = 0;
            foreach (var sp in spans)
            {
                if (sp.Kind != EventKind.Hill) continue;
                if (x < sp.X0 || x > sp.X1) continue;
                float u = (float)((x - sp.X0) / Mathf.Max(0.01f, (float)(sp.X1 - sp.X0)));
                float ang = sp.Def.A * Mathf.Deg2Rad;
                float d = sp.Def.Duration;
                y += (float)(sp.Vx0 * Mathf.Sin(ang) * d / (2 * Mathf.PI) * (1 - Mathf.Cos(2 * Mathf.PI * u)));
            }
            return y;
        }

        void BuildVan(float minX, float maxX)
        {
            const float roadDrop = 0.82f;
            for (float x = minX; x < maxX; x += 2f)
            {
                var t = Spawn("road_tile", new Vector3(x, -roadDrop + HillY(x), 0.2f));
                if (t != null)
                {
                    float slope = (HillY(x + 1f) - HillY(x - 1f)) / 2f;
                    t.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan(slope) * Mathf.Rad2Deg);
                }
            }
            foreach (var sp in spans)
            {
                float x = (float)(sp.X0 + sp.X1) * 0.5f;
                switch (sp.Kind)
                {
                    case EventKind.Bump: case EventKind.SpeedBump: Spawn("prop_speedbump", new Vector3(x, -roadDrop + 0.02f, 0.2f), 0, new Vector3(1, sp.Def.A / 0.1f * 0.8f, 1)); break;
                    case EventKind.Pothole: Spawn("prop_pothole", new Vector3(x, -roadDrop + 0.01f, -0.3f)); break;
                    case EventKind.Cobbles:
                        for (float cx = (float)sp.X0; cx < sp.X1; cx += 1f) Spawn("prop_cobbles", new Vector3(cx, -roadDrop + 0.005f, 0.2f));
                        break;
                }
            }
            Scenery(minX, maxX, -roadDrop - 0.02f, 11);
            for (float x = minX; x < maxX; x += 9f) Spawn("prop_lamppost", new Vector3(x + 3f, -roadDrop, 2.3f), 180f);

            var truck = new GameObject("truck").transform;
            truck.SetParent(transform, false);
            Vehicle = truck;
            float boxW = BoxHalfW * 2f;
            float boxCenterOnBed = -1.6f;
            VehicleOffset = new Vector3(-boxCenterOnBed, -BoxHalfH, -0.02f);
            Spawn("truck_bed", Vector3.zero, 0, null, truck);
            Spawn("truck_cab", Vector3.zero, 0, null, truck);
            if (boxW < 1.5f) Spawn("parcel_stack", new Vector3(-2.75f, 0f, 0.25f), 15f, Vector3.one * 0.8f, truck);
            foreach (var wx in new[] { -2.45f, 1.35f })
            {
                var w = Spawn("truck_wheel", new Vector3(wx, -0.5f, -0.72f), 0, null, truck);
                if (w != null) wheels.Add(w.transform);
                var w2 = Spawn("truck_wheel", new Vector3(wx, -0.5f, 0.72f), 180f, null, truck);
                if (w2 != null) wheels.Add(w2.transform);
            }
            CameraOffset = new Vector3(0.9f, 0.55f, -1f);
        }

        // ---- Depot -----------------------------------------------------------------------------

        void BuildDepot(float minX, float maxX)
        {
            float floorY = -1.0f;
            foreach (var sp in spans) floorY = Mathf.Min(floorY, (float)Mathf.Min((float)sp.Y0, (float)sp.Y1) - 1.0f);
            for (float x = minX; x < maxX; x += 10f) Spawn("depot_floor", new Vector3(x, floorY, 1f));
            for (float x = minX; x < maxX; x += 10f) Spawn("depot_wall", new Vector3(x, floorY, 5f));
            for (float x = minX + 2; x < maxX; x += 3.4f) Spawn("prop_rack", new Vector3(x, floorY, 3.6f));
            for (float x = minX + 1; x < maxX; x += 4f) Spawn("prop_hanglamp", new Vector3(x, 3.2f, 1.5f));

            // belts wherever the box rests or rides level; merged per height
            var surfaces = new List<(float a, float b, float y)>();
            float margin = BoxHalfW + 0.35f;
            for (int si = 0; si < spans.Count; si++)
            {
                var sp = spans[si];
                bool level = sp.Kind == EventKind.Rest || sp.Kind == EventKind.Conveyor || sp.Kind == EventKind.ArmTip;
                if (sp.Kind == EventKind.Drop) surfaces.Add(((float)sp.X1 - margin, (float)sp.X1 + margin, (float)sp.Y1));
                if (!level) continue;
                bool dropNext = si + 1 < spans.Count && spans[si + 1].Kind == EventKind.Drop;
                float a = (float)Mathf.Min((float)sp.X0, (float)sp.X1) - margin;
                float b = dropNext ? (float)sp.X1 + 0.02f : (float)Mathf.Max((float)sp.X0, (float)sp.X1) + margin;
                surfaces.Add((a, b, (float)sp.Y0));
            }
            surfaces.Sort((p, q) => p.y != q.y ? p.y.CompareTo(q.y) : p.a.CompareTo(q.a));
            var merged = new List<(float a, float b, float y)>();
            foreach (var sf in surfaces)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (Mathf.Abs(last.y - sf.y) < 0.01f && sf.a <= last.b + 0.05f) { merged[merged.Count - 1] = (last.a, Mathf.Max(last.b, sf.b), last.y); continue; }
                }
                merged.Add(sf);
            }
            foreach (var sf in merged)
            {
                Spawn("prop_conveyor", new Vector3((sf.a + sf.b) * 0.5f, sf.y, 0), 0, new Vector3(sf.b - sf.a, 1, 1));
                // legs down to the floor
                float h = sf.y - 0.16f - floorY;
                for (float x = sf.a + 0.3f; x < sf.b; x += 1.6f)
                {
                    foreach (float z in new[] { -0.4f, 0.4f })
                        MeshGen.Make("leg", MeshGen.RoundedBox(new Vector3(0.08f, h, 0.06f), 0.01f), Mat.Lit(Palette.Hex("6B6466"), 0.4f), transform, new Vector3(x, floorY + h * 0.5f, z));
                }
            }
            foreach (var sp in spans)
            {
                float x0 = (float)sp.X0, x1 = (float)sp.X1, y0 = (float)sp.Y0, y1 = (float)sp.Y1;
                if (sp.Kind == EventKind.ArmTip)
                {
                    var arm = Spawn("arm_base", new Vector3(x0 - 1.1f, floorY, 1.7f));
                    if (arm != null)
                    {
                        armBase = arm.transform;
                        armUpper = ModelLibrary.FindDeep(armBase, "Upper");
                        armFore = ModelLibrary.FindDeep(armBase, "Fore");
                        armGrip = ModelLibrary.FindDeep(armBase, "Grip");
                        float lift = y0 - floorY;
                        armBase.localScale = Vector3.one * Mathf.Clamp(lift / 1.0f, 0.9f, 1.6f);
                    }
                }
                if (sp.Kind == EventKind.Chute)
                {
                    float ang = sp.Def.A;
                    float dx = x1 - x0, dy = y0 - y1;
                    float len = Mathf.Sqrt(dx * dx + dy * dy) + BoxHalfW * 2 + 0.4f;
                    var ch = Spawn("prop_chute", new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f - 0.01f, 0), 0, new Vector3(len / 2f, 1, 1));
                    if (ch != null) ch.transform.localRotation = Quaternion.Euler(0, 0, -ang);
                    Spawn("prop_bumper", new Vector3(x1 + BoxHalfW + 0.1f, y1, 0f));
                    Spawn("prop_conveyor", new Vector3(x1 - 0.2f, y1, 0), 0, new Vector3(BoxHalfW * 2f + 0.8f, 1, 1));
                }
            }
            CameraOffset = new Vector3(0.7f, 0.6f, -1f);
        }

        static void StretchLegs(GameObject conveyor, float height)
        {
            // the conveyor's legs are 0.84 m tall in the model; stretching the whole mesh in y would
            // squash the belt, so hide legs that would float and accept short ones otherwise
            if (height < 0.3f) return;
            conveyor.transform.localScale = new Vector3(conveyor.transform.localScale.x, 1f, 1f);
        }

        // ---- Doorstep --------------------------------------------------------------------------

        void BuildDoorstep(float minX, float maxX)
        {
            const float carry = 0.95f;   // box bottom height above the ground when carried
            float groundY = -carry;
            for (float x = minX; x < maxX; x += 2f) Spawn("street_tile", new Vector3(x, groundY - 0.12f, 0.6f));
            foreach (var sp in spans)
            {
                if (sp.Kind == EventKind.Stairs)
                {
                    int n = Mathf.Max(1, (int)sp.Def.A);
                    float sh = sp.Def.B > 0 ? sp.Def.B : 0.18f;
                    for (int i = 0; i < n; i++)
                    {
                        var stp = Spawn("prop_step", new Vector3((float)sp.X0 + 0.28f * i + 0.05f, groundY + sh * (i + 1), 0.4f), 0, new Vector3(1, 1 + sh * (i + 1) / 0.2f, 1));
                    }
                }
                if (sp.Kind == EventKind.Toss)
                {
                    float spin = Mathf.Abs(sp.Def.C) % 180f;
                    float halfY = spin > 45f && spin < 135f ? BoxHalfW : BoxHalfH;
                    float centerY = (float)sp.Y1 + BoxHalfH;
                    float porchY = centerY - halfY;
                    Spawn("porch", new Vector3((float)sp.X1 - 1.6f, porchY, -0.2f));
                    // stairs up to the porch from the street
                    float h = porchY - groundY;
                    int n = Mathf.Max(1, Mathf.RoundToInt(h / 0.18f));
                    for (int i = 0; i < n; i++)
                        Spawn("prop_step", new Vector3((float)sp.X1 - 1.6f - 0.3f * (n - i), groundY + h * (i + 1) / n, -0.2f), 0, new Vector3(1, 1 + h * (i + 1) / n / 0.2f, 1));
                }
            }
            Scenery(minX, maxX, groundY, 23);
            Spawn("prop_mailbox", new Vector3(minX + 9f, groundY, -0.6f), 0);
            var c = Spawn("courier", Vector3.zero);
            if (c != null)
            {
                courier = c.transform;
                courierArmL = ModelLibrary.FindDeep(courier, "ArmL");
                courierArmR = ModelLibrary.FindDeep(courier, "ArmR");
                courierLegL = ModelLibrary.FindDeep(courier, "LegL");
                courierLegR = ModelLibrary.FindDeep(courier, "LegR");
                courierHead = ModelLibrary.FindDeep(courier, "Head");
            }
            CameraOffset = new Vector3(0.8f, 0.6f, -1f);
        }

        // ---- Ship / Plane / Catapult -----------------------------------------------------------

        void BuildShip(float minX, float maxX)
        {
            var deck = new GameObject("ship").transform;
            deck.SetParent(transform, false);
            Vehicle = deck;
            VehicleOffset = new Vector3(0, -BoxHalfH, 0);
            Spawn("ship_deck", Vector3.zero, 0, null, deck);
            for (float x = minX - 6; x < maxX + 12; x += 12f)
            {
                for (int k = 0; k < 4; k++)
                {
                    var w = Spawn("prop_waves", new Vector3(x, -1.7f - k * 0.05f, -2.5f + k * 6f), 0, new Vector3(1, 1, 0.4f));
                    if (w != null) bobbers.Add((w.transform, 0.8f + k * 0.2f, w.transform.localPosition.y));
                }
            }
            var sea = MeshGen.Make("sea", MeshGen.RoundedBox(new Vector3(maxX - minX + 200, 0.2f, 120), 0.01f), Mat.Lit(Palette.Hex("3F7FA6"), 0.6f), transform, new Vector3((minX + maxX) * 0.5f, -2.0f, 50f));
            sea.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Spawn("prop_hill", new Vector3(maxX + 10, -3f, 45f), 0, new Vector3(2, 1.2f, 1.5f));
            Spawn("tree_round", new Vector3(maxX + 8, -0.2f, 42f), 0, Vector3.one * 2f);
            CameraOffset = new Vector3(0.8f, 0.6f, -1f);
        }

        void BuildPlane(float minX, float maxX)
        {
            var hold = new GameObject("plane").transform;
            hold.SetParent(transform, false);
            Vehicle = hold;
            VehicleOffset = new Vector3(0, -BoxHalfH, 0);
            Spawn("plane_hold", Vector3.zero, 0, null, hold);
            Spawn("parcel_stack", new Vector3(-2.3f, 0f, 0.6f), 10f, Vector3.one * 0.8f, hold);
            Spawn("parcel_stack", new Vector3(2.4f, 0f, 0.7f), -20f, Vector3.one * 0.7f, hold);
            CameraOffset = new Vector3(0.7f, 0.5f, -1f);
        }

        Transform catapultBucket;
        Vector3 catapultPivot;
        const float ArmLen = 2.5f;
        const float RestAngle = 205f;

        void BuildCatapult(float minX, float maxX)
        {
            float landX = 0, landY = 0;
            foreach (var sp in spans) if (sp.Kind == EventKind.HayLand) { landX = (float)sp.X1; landY = (float)sp.Y1; }
            // bucket under the box at rest: pivot sits up and forward of it along the rest angle
            var restDir = new Vector3(Mathf.Cos(RestAngle * Mathf.Deg2Rad), Mathf.Sin(RestAngle * Mathf.Deg2Rad), 0);
            catapultPivot = new Vector3(0, -0.02f, 0) - restDir * ArmLen;
            float groundY = catapultPivot.y - 1.3f;
            Scenery(minX, maxX + 20f, Mathf.Min(groundY, landY - 1.6f), 37, houses: false);
            var cat = Spawn("catapult", new Vector3(catapultPivot.x, groundY, 0.3f));
            if (cat != null)
            {
                catapultArm = ModelLibrary.FindDeep(cat.transform, "Arm");
                catapultBucket = ModelLibrary.FindDeep(cat.transform, "Bucket");
            }
            var hay = Spawn("prop_haystack", new Vector3(landX, landY - 1.45f, 0.2f), 0, Vector3.one * 1.3f);
            Spawn("prop_tower", new Vector3(landX + 5f, landY - 1.9f, 6f));
            CameraOffset = new Vector3(0.8f, 0.6f, -1f);
        }

        // ---- Per-frame animation -----------------------------------------------------------------

        /// <summary>Called every frame with the box pose. span = the running event (or -1).</summary>
        public void Animate(Vector3 boxPos, float angleDeg, float speed, int eventIndex, float eventT)
        {
            if (Vehicle != null)
            {
                Vehicle.position = boxPos + Quaternion.Euler(0, 0, angleDeg) * VehicleOffset;
                Vehicle.rotation = Quaternion.Euler(0, 0, angleDeg);
                float dt = Time.deltaTime;
                foreach (var w in wheels) w.Rotate(0, 0, -speed * dt / 0.32f * Mathf.Rad2Deg, Space.Self);
            }
            float t = Time.time;
            foreach (var b in bobbers)
            {
                var p = b.t.localPosition;
                p.y = b.baseY + Mathf.Sin(t * b.speed + p.x) * 0.08f;
                b.t.localPosition = p;
            }
            EventSpan? cur = null;
            foreach (var sp in spans) if (sp.Index == eventIndex) cur = sp;

            if (courier != null) AnimateCourier(boxPos, cur, eventT);
            if (armBase != null) AnimateArm(boxPos, angleDeg, cur, eventT);
            if (catapultArm != null)
            {
                // the arm points at the box while it is being thrown, then follows through
                float angle = RestAngle;
                var local = transform.InverseTransformPoint(boxPos) - new Vector3(0, BoxHalfH, 0);
                var toBox = local - catapultPivot;
                if (cur.HasValue && cur.Value.Kind == EventKind.Launch)
                    angle = Mathf.Atan2(toBox.y, toBox.x) * Mathf.Rad2Deg;
                else if (cur.HasValue && cur.Value.Kind != EventKind.Rest)
                    angle = 90f;
                else if (cur.HasValue && cur.Value.Kind == EventKind.Rest && cur.Value.Index > 0)
                    angle = 90f;
                if (angle < 0) angle += 360f;
                float curA = catapultArm.localEulerAngles.z;
                bool follow = angle == 90f;
                float a2 = follow ? Mathf.MoveTowardsAngle(curA, angle, Time.unscaledDeltaTime * 600f) : angle;
                catapultArm.localRotation = Quaternion.Euler(0, 0, a2);
                if (catapultBucket != null) catapultBucket.localRotation = Quaternion.Euler(0, 0, -a2);
            }
            lastBoxPos = boxPos;
        }

        void AnimateCourier(Vector3 boxPos, EventSpan? cur, float eventT)
        {
            var kind = cur.HasValue ? cur.Value.Kind : EventKind.Rest;
            if (kind == EventKind.Toss || tossed)
            {
                if (!tossed && cur.HasValue)
                {
                    tossed = true;
                }
                // stays where the throw started, arms up
                if (courierArmL != null) courierArmL.localRotation = Quaternion.Slerp(courierArmL.localRotation, Quaternion.Euler(-160, 0, 0), Time.deltaTime * 10f);
                if (courierArmR != null) courierArmR.localRotation = Quaternion.Slerp(courierArmR.localRotation, Quaternion.Euler(-160, 0, 0), Time.deltaTime * 10f);
                return;
            }
            float moved = (boxPos - lastBoxPos).magnitude;
            walkPhase += moved * 9f;
            var feet = boxPos + new Vector3(0, -BoxHalfH - 0.95f, 0.42f);
            courier.position = feet;
            courier.rotation = Quaternion.Euler(0, 0, 0);
            float swing = Mathf.Sin(walkPhase) * (moved > 0.0005f ? 28f : 0f);
            if (courierLegL != null) courierLegL.localRotation = Quaternion.Euler(swing, 0, 0);
            if (courierLegR != null) courierLegR.localRotation = Quaternion.Euler(-swing, 0, 0);
            // arms reach forward around the box
            if (courierArmL != null) courierArmL.localRotation = Quaternion.Euler(-70, 0, -12);
            if (courierArmR != null) courierArmR.localRotation = Quaternion.Euler(-70, 0, 12);
            if (courierHead != null) courierHead.localRotation = Quaternion.Euler(Mathf.Sin(walkPhase * 0.5f) * 3f, 0, 0);
        }

        void AnimateArm(Vector3 boxPos, float angleDeg, EventSpan? cur, float eventT)
        {
            // simple 2-link IK in the arm's vertical plane toward a target above the box
            bool holding = cur.HasValue && cur.Value.Kind == EventKind.ArmTip;
            var target = holding ? boxPos + Quaternion.Euler(0, 0, angleDeg) * new Vector3(0, BoxHalfH + 0.28f, 0)
                                 : armBase.position + new Vector3(0.9f, 2.2f, -0.6f);
            var shoulder = armUpper != null ? armUpper.position : armBase.position + Vector3.up * 0.8f;
            var d = target - shoulder;
            float yawDeg = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            armBase.localRotation = Quaternion.Euler(0, Mathf.LerpAngle(armBase.localEulerAngles.y, yawDeg, Time.deltaTime * 8f), 0);
            float horiz = new Vector2(d.x, d.z).magnitude;
            float dist = Mathf.Min(new Vector2(horiz, d.y).magnitude, 2.0f - 0.01f);
            const float l1 = 1.1f, l2 = 0.9f;
            float a2 = Mathf.Acos(Mathf.Clamp((dist * dist - l1 * l1 - l2 * l2) / (2 * l1 * l2), -1, 1));
            float a1 = Mathf.Atan2(d.y, horiz) - Mathf.Atan2(l2 * Mathf.Sin(a2), l1 + l2 * Mathf.Cos(a2));
            if (armUpper != null) armUpper.localRotation = Quaternion.Euler(0, 0, a1 * Mathf.Rad2Deg - 90f);
            if (armFore != null) armFore.localRotation = Quaternion.Euler(0, 0, a2 * Mathf.Rad2Deg);
            if (armGrip != null) armGrip.rotation = Quaternion.Euler(0, 0, angleDeg);
        }

        public void ApplyLighting(Light sun, Camera cam) => Lighting.Apply(sun, cam);
    }
}
