using System.Collections.Generic;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>Small procedural meshes for UI decoration, grids and fallbacks.</summary>
    public static class MeshGen
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        /// <summary>Box of the given size with rounded edges (radius r), centred on the origin.</summary>
        public static Mesh RoundedBox(Vector3 size, float r, int seg = 6)
        {
            string key = $"rb{size.x:0.###}_{size.y:0.###}_{size.z:0.###}_{r:0.###}_{seg}";
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var half = size * 0.5f;
            r = Mathf.Min(r, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.999f);
            var inner = half - Vector3.one * r;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int n = seg * 2 + 2; // grid resolution per face
            // six faces: build a grid on each face of a unit cube then round it
            Vector3[] faceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] faceU = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            for (int f = 0; f < 6; f++)
            {
                var nn = faceN[f];
                var uu = faceU[f];
                var vv = Vector3.Cross(nn, uu);
                int start = verts.Count;
                for (int j = 0; j <= n; j++)
                {
                    for (int i = 0; i <= n; i++)
                    {
                        // distribute samples so the rounded band gets most of them
                        float a = Dist(i, n), b = Dist(j, n);
                        var p = nn + uu * a + vv * b; // on cube [-1,1]
                        var q = new Vector3(p.x * half.x, p.y * half.y, p.z * half.z);
                        var c = new Vector3(Mathf.Clamp(q.x, -inner.x, inner.x), Mathf.Clamp(q.y, -inner.y, inner.y), Mathf.Clamp(q.z, -inner.z, inner.z));
                        var d = q - c;
                        Vector3 nrm = d.sqrMagnitude > 1e-12f ? d.normalized : nn;
                        verts.Add(c + nrm * r);
                        norms.Add(nrm);
                        uvs.Add(new Vector2(a * 0.5f + 0.5f, b * 0.5f + 0.5f));
                    }
                }
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int i0 = start + j * (n + 1) + i;
                        int i1 = i0 + 1, i2 = i0 + (n + 1), i3 = i2 + 1;
                        tris.Add(i0); tris.Add(i2); tris.Add(i1);
                        tris.Add(i1); tris.Add(i2); tris.Add(i3);
                    }
                }
            }
            m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            cache[key] = m;
            return m;
        }

        // maps grid index to [-1,1] with extra density near the edges
        static float Dist(int i, int n)
        {
            float t = i / (float)n * 2f - 1f;
            return Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 0.6f);
        }

        /// <summary>Surface of revolution around Y from a (radius, height) profile.</summary>
        public static Mesh Lathe(string key, Vector2[] profile, int sides = 32)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int rows = profile.Length;
            for (int s = 0; s <= sides; s++)
            {
                float ang = s / (float)sides * Mathf.PI * 2f;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                for (int r = 0; r < rows; r++)
                {
                    var p = profile[r];
                    verts.Add(new Vector3(p.x * cs, p.y, p.x * sn));
                    var prev = profile[Mathf.Max(0, r - 1)];
                    var next = profile[Mathf.Min(rows - 1, r + 1)];
                    var tan = (next - prev).normalized;
                    var n2 = new Vector2(tan.y, -tan.x);
                    norms.Add(new Vector3(n2.x * cs, n2.y, n2.x * sn).normalized);
                    uvs.Add(new Vector2(s / (float)sides, r / (float)(rows - 1)));
                }
            }
            for (int s = 0; s < sides; s++)
            {
                for (int r = 0; r < rows - 1; r++)
                {
                    int a = s * rows + r, b = (s + 1) * rows + r;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            }
            m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            cache[key] = m;
            return m;
        }

        public static Mesh Sphere(int seg = 24)
        {
            var prof = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = -Mathf.PI / 2 + Mathf.PI * i / seg;
                prof[i] = new Vector2(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f);
            }
            prof[0].x = 0; prof[seg].x = 0;
            return Lathe("sphere" + seg, prof, seg * 2);
        }

        public static Mesh Quad()
        {
            if (cache.TryGetValue("quad", out var m) && m != null) return m;
            m = new Mesh { name = "quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            m.RecalculateTangents();
            cache["quad"] = m;
            return m;
        }

        public static GameObject Make(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos = default, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }
    }
}
