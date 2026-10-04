using System.Collections.Generic;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>Material cache built from the template materials in Resources/Materials.</summary>
    public static class Mat
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Material lit, litEmissive, litTransparent, unlit, unlitTransparent, particleAlpha, particleAdd, litBaked, glassClear;

        /// <summary>URP Lit driven by maps baked in Blender (mask: R metallic, G occlusion, A smoothness).</summary>
        public static Material Baked(string key, Texture2D albedo, Texture2D normal, Texture2D mask)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref litBaked, "HWC_LitBaked")) { name = key };
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_Metallic", 1f);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            else m.DisableKeyword("_NORMALMAP");
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.SetTexture("_OcclusionMap", mask);
                m.SetFloat("_OcclusionStrength", 1f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.EnableKeyword("_OCCLUSIONMAP");
            }
            cache[key] = m;
            return m;
        }

        /// <summary>A copy of a textured material with its base colour multiplied by `tint`.</summary>
        public static Material Tinted(Material src, Color tint)
        {
            string key = $"{src.name}_tint{ColorUtility.ToHtmlStringRGBA(tint)}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(src) { name = key };
            m.SetColor("_BaseColor", tint);
            cache[key] = m;
            return m;
        }

        /// <summary>Clear glass: premultiplied transparency so highlights and reflections stay bright.</summary>
        public static Material ClearGlass(Color c, float smooth = 0.96f)
        {
            string key = $"cglass{ColorUtility.ToHtmlStringRGBA(c)}_{smooth:0.00}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref glassClear, "HWC_Glass")) { name = key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        static Material Template(ref Material field, string name)
        {
            if (field == null)
            {
                field = Resources.Load<Material>("Materials/" + name);
                if (field == null) Debug.LogError("[Mat] missing template material " + name);
            }
            return field;
        }

        public static Material Lit(Color c, float smooth = 0.35f, float metal = 0f)
        {
            string key = $"lit{ColorUtility.ToHtmlStringRGBA(c)}_{smooth:0.00}_{metal:0.00}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref lit, "HWC_Lit")) { name = key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.DisableKeyword("_NORMALMAP");
            cache[key] = m;
            return m;
        }

        public static Material Textured(string key, Texture2D tex, Color tint, float smooth = 0.3f, Texture2D normal = null, float normalScale = 1f)
        {
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref lit, "HWC_Lit")) { name = key };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            else m.DisableKeyword("_NORMALMAP");
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color c, Color emission, float smooth = 0.4f)
        {
            string key = $"em{ColorUtility.ToHtmlStringRGBA(c)}_{ColorUtility.ToHtmlStringRGBA(emission)}_{emission.maxColorComponent:0.00}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref litEmissive, "HWC_LitEmissive")) { name = key };
            m.SetColor("_BaseColor", c);
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        public static Material Glass(Color c, float smooth = 0.9f)
        {
            string key = $"glass{ColorUtility.ToHtmlStringRGBA(c)}_{smooth:0.00}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(Template(ref litTransparent, "HWC_LitTransparent")) { name = key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            cache[key] = m;
            return m;
        }

        /// <summary>Transparent textured lit material (printed stamps, decals).</summary>
        public static Material Decal(string key, Texture2D tex)
        {
            if (cache.TryGetValue("decal_" + key, out var m)) return m;
            m = new Material(Template(ref litTransparent, "HWC_LitTransparent")) { name = "decal_" + key };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.15f);
            cache["decal_" + key] = m;
            return m;
        }

        public static Material Unlit(Color c, bool transparent = false)
        {
            string key = $"unlit{ColorUtility.ToHtmlStringRGBA(c)}_{transparent}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(transparent ? Template(ref unlitTransparent, "HWC_UnlitTransparent") : Template(ref unlit, "HWC_Unlit")) { name = key };
            m.SetColor("_BaseColor", c);
            cache[key] = m;
            return m;
        }

        /// <summary>A fresh (uncached) transparent unlit material, for things that fade.</summary>
        public static Material UnlitInstance(Color c, Texture tex = null)
        {
            var m = new Material(Template(ref unlitTransparent, "HWC_UnlitTransparent"));
            m.SetColor("_BaseColor", c);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        public static Material Particle(bool additive, Texture tex = null)
        {
            string key = $"part{additive}_{(tex != null ? tex.name : "none")}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(additive ? Template(ref particleAdd, "HWC_ParticleAdd") : Template(ref particleAlpha, "HWC_ParticleAlpha")) { name = key };
            if (tex != null) m.SetTexture("_BaseMap", tex);
            cache[key] = m;
            return m;
        }
    }
}
