using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// Loads the Blender-made models from Resources/Models and swaps their placeholder materials
    /// (named col_RRGGBB, glass_RRGGBB, metal_RRGGBB, glow_RRGGBB, tex_name) for shared URP
    /// materials. Falls back to simple procedural shapes when a model is missing.
    /// </summary>
    public static class ModelLibrary
    {
        public const float Cell = 0.25f;
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly HashSet<string> missing = new HashSet<string>();

        public static GameObject Load(string id)
        {
            if (prefabs.TryGetValue(id, out var p) && p != null) return p;
            if (missing.Contains(id)) return null;
            p = Resources.Load<GameObject>("Models/" + id);
            if (p == null) { missing.Add(id); return null; }
            prefabs[id] = p;
            return p;
        }

        /// <summary>Instantiates a model by id with materials resolved. Null when missing.</summary>
        public static GameObject Spawn(string id, Transform parent)
        {
            var prefab = Load(id);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = id;
            ResolveMaterials(go);
            return go;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        public static void ResolveMaterials(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Resolve(mats[i] != null ? mats[i].name : "col_FF00FF");
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        static readonly Dictionary<string, Material> resolved = new Dictionary<string, Material>();

        public static Material Resolve(string name)
        {
            // Blender may append ".001" etc.
            int dot = name.IndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            name = name.Replace(" (Instance)", "");
            if (resolved.TryGetValue(name, out var m) && m != null) return m;
            int us = name.IndexOf('_');
            string kind = us > 0 ? name.Substring(0, us) : name;
            string arg = us > 0 ? name.Substring(us + 1) : "";
            if (kind == "leaf")
            {
                m = Mat.Cutout("leaf_" + arg, TextureLibrary.Get(arg), TextureLibrary.Get(arg + "_n"));
                resolved[name] = m;
                return m;
            }
            if (kind == "bake")
            {
                m = TextureLibrary.BakedMaterial(arg);
                resolved[name] = m;
                return m;
            }
            if (kind != "tex" && kind != "decal" && arg.Length > 6) arg = arg.Substring(0, 6);
            Color c = Color.magenta;
            if (arg.Length >= 6) c = Palette.Hex(arg.Substring(0, 6));
            switch (kind)
            {
                case "glass": c.a = 0.3f; m = Mat.ClearGlass(c); break;
                case "metal": m = Mat.Lit(c, 0.75f, 0.9f); break;
                case "shiny": m = Mat.Lit(c, 0.8f, 0f); break;
                case "matte": m = Mat.Lit(c, 0.1f, 0f); break;
                case "glow": m = Mat.Emissive(c, c * 2.2f); break;
                case "tex": m = TextureLibrary.MaterialFor(arg); break;
                case "decal": m = Mat.Decal(arg, TextureLibrary.Get("decal_" + arg)); break;
                default: m = Mat.Lit(c, 0.3f, 0f); break;
            }
            resolved[name] = m;
            return m;
        }

        /// <summary>Model for a piece, sized to its nominal cell footprint and centred on the origin.</summary>
        public static GameObject SpawnPiece(PieceKind kind, Transform parent)
        {
            var def = Catalog.Get(kind);
            var go = Spawn("piece_" + def.Id, parent);
            if (go != null) return go;
            return Placeholder(kind, parent);
        }

        static GameObject Placeholder(PieceKind kind, Transform parent)
        {
            var def = Catalog.Get(kind);
            var root = new GameObject("placeholder_" + def.Id);
            root.transform.SetParent(parent, false);
            var col = Palette.ItemColor(kind);
            float w = def.W * Cell, h = def.H * Cell, d = Cell * 0.82f;
            Material mat = kind == PieceKind.Bubble || kind == PieceKind.IceSwan || kind == PieceKind.SnowGlobe
                ? Mat.Glass(new Color(col.r, col.g, col.b, 0.55f))
                : (kind == PieceKind.LavaLamp ? Mat.Emissive(col, col * 1.5f) : Mat.Lit(col, kind == PieceKind.BowlingBall ? 0.85f : 0.35f));
            bool round = kind == PieceKind.BowlingBall || kind == PieceKind.Armadillo || kind == PieceKind.Balloon ||
                         kind == PieceKind.BouncyBall || kind == PieceKind.SnowGlobe || kind == PieceKind.Paper || kind == PieceKind.DragonEgg;
            if (round)
            {
                var s = MeshGen.Make("body", MeshGen.Sphere(), mat, root.transform);
                float sz = Mathf.Min(w, h) * 0.9f;
                s.transform.localScale = new Vector3(sz, kind == PieceKind.DragonEgg ? sz * 1.15f : sz, Mathf.Min(sz, d));
            }
            else
            {
                MeshGen.Make("body", MeshGen.RoundedBox(new Vector3(w * 0.9f, h * 0.94f, d), Cell * 0.12f), mat, root.transform);
            }
            // a darker "face" stripe so facing reads in the prototype
            if (def.Has(Quirk.Facing))
            {
                var eye = MeshGen.Make("eye", MeshGen.Sphere(), Mat.Lit(Palette.Ink, 0.8f), root.transform,
                    new Vector3(w * 0.32f, h * 0.15f, -d * 0.42f));
                eye.transform.localScale = Vector3.one * Cell * 0.12f;
            }
            return root;
        }
    }
}
