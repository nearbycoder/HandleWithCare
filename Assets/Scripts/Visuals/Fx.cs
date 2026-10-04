using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>One-shot particle effects built in code.</summary>
    public static class Fx
    {
        public static bool Reduced;   // reduced-motion setting trims effects

        static ParticleSystem Make(string name, Vector3 pos, Material mat, out ParticleSystemRenderer psr)
        {
            var go = new GameObject("fx_" + name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.rateOverTime = 0;
            psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = mat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ps.Play();
        }

        static ParticleSystem.MinMaxGradient Fade(Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(c.a, 0.12f), new GradientAlphaKey(c.a * 0.6f, 0.6f), new GradientAlphaKey(0, 1) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>Cardboard dust puff where something lands. width in cells.</summary>
        public static void Dust(Vector3 pos, float width)
        {
            var ps = Make("dust", pos, Mat.Particle(false, TextureLibrary.SoftDot), out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new Color(0.86f, 0.74f, 0.58f, 0.55f);
            main.gravityModifier = -0.02f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(Mathf.Max(0.1f, width * 0.25f), 0.01f, 0.25f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(new Color(0.86f, 0.74f, 0.58f, 0.55f));
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.6f));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.15f;
            limit.limit = 0.05f;
            Burst(ps, Reduced ? 4 : Mathf.RoundToInt(8 + width * 4));
        }

        public static void Poof(Vector3 pos, Color c, float size)
        {
            var ps = Make("poof", pos, Mat.Particle(false, TextureLibrary.SoftDot), out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f * size, 0.14f * size);
            c.a = 0.8f;
            main.startColor = c;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.08f * size;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(c);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.2f;
            limit.limit = 0.1f;
            Burst(ps, Reduced ? 5 : 14);
        }

        /// <summary>Broken pieces flying out (little chunky cubes).</summary>
        public static void Shards(Vector3 pos, Color c, float size = 1f)
        {
            var ps = Make("shards", pos, Mat.Lit(c, 0.6f), out var r);
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = MeshGen.RoundedBox(new Vector3(1, 0.6f, 0.8f), 0.1f, 2);
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * size, 2.2f * size);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 1.2f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = 0.05f;
            sh.rotation = new Vector3(-90, 0, 0);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0.7f, 1f, 1f, 0f));
            Burst(ps, Reduced ? 8 : 22);
        }

        /// <summary>A cone of fire shooting along dir (world), length in metres.</summary>
        public static void Fire(Vector3 pos, Vector3 dir, float length)
        {
            var ps = Make("fire", pos, Mat.Particle(true, TextureLibrary.SoftDot), out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(length * 2.2f, length * 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f, 1f), new Color(1f, 0.35f, 0.08f, 1f));
            main.duration = 0.35f;
            main.gravityModifier = -0.3f;
            ps.transform.rotation = Quaternion.LookRotation(dir);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 14f;
            sh.radius = 0.02f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 2.2f));
            var em = ps.emission;
            em.rateOverTime = Reduced ? 60 : 160;
            ps.Play();
        }

        public static void Confetti(Vector3 pos, float spread = 1f)
        {
            var ps = Make("confetti", pos, Mat.Lit(Color.white, 0.3f), out var r);
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = MeshGen.Quad();
            r.alignment = ParticleSystemRenderSpace.World;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * spread, 3.5f * spread);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0.35f;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Palette.PostalRed, 0f), new GradientColorKey(Palette.Sticky, 0.33f), new GradientColorKey(Palette.Teal, 0.66f), new GradientColorKey(Palette.Cream, 1f) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 35f;
            sh.rotation = new Vector3(-90, 0, 0);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.08f;
            limit.limit = 0.6f;
            Burst(ps, Reduced ? 30 : 110);
        }

        /// <summary>Little white sparkles (pristine items, stars).</summary>
        public static void Sparkle(Vector3 pos, Color c, float radius = 0.15f)
        {
            var ps = Make("sparkle", pos, Mat.Particle(true, TextureLibrary.SoftDot), out var r);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = c;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(c);
            Burst(ps, Reduced ? 6 : 16);
        }
    }
}
