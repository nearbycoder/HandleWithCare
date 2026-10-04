using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HWC.Visuals
{
    /// <summary>Global post-processing volume built at runtime, with a few animated controls.</summary>
    public sealed class Post : MonoBehaviour
    {
        public Volume Volume;
        Bloom bloom;
        Tonemapping tone;
        ColorAdjustments color;
        Vignette vignette;
        DepthOfField dof;
        WhiteBalance white;
        ChromaticAberration chroma;
        LiftGammaGain lgg;
        float chromaKick, vignetteKick, saturationTarget = 8f, exposureTarget;
        float dofTarget, dofFocus = 2f;

        public static Post Create()
        {
            var go = new GameObject("PostVolume");
            var p = go.AddComponent<Post>();
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 10;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            v.sharedProfile = prof;
            p.Volume = v;

            p.bloom = prof.Add<Bloom>(true);
            p.bloom.threshold.Override(0.95f);
            p.bloom.intensity.Override(0.55f);
            p.bloom.scatter.Override(0.65f);
            p.bloom.tint.Override(new Color(1f, 0.93f, 0.85f));

            p.tone = prof.Add<Tonemapping>(true);
            p.tone.mode.Override(TonemappingMode.Neutral);

            p.color = prof.Add<ColorAdjustments>(true);
            p.color.postExposure.Override(0.15f);
            p.color.contrast.Override(10f);
            p.color.saturation.Override(8f);

            p.white = prof.Add<WhiteBalance>(true);
            p.white.temperature.Override(6f);
            p.white.tint.Override(2f);

            p.lgg = prof.Add<LiftGammaGain>(true);
            p.lgg.lift.Override(new Vector4(1.0f, 0.98f, 0.97f, 0.0f));
            p.lgg.gain.Override(new Vector4(1.0f, 0.99f, 0.96f, 0.0f));

            p.vignette = prof.Add<Vignette>(true);
            p.vignette.intensity.Override(0.26f);
            p.vignette.smoothness.Override(0.45f);
            p.vignette.color.Override(new Color(0.12f, 0.06f, 0.04f));

            p.dof = prof.Add<DepthOfField>(true);
            p.dof.mode.Override(DepthOfFieldMode.Off);
            p.dof.gaussianStart.Override(3f);
            p.dof.gaussianEnd.Override(12f);

            p.chroma = prof.Add<ChromaticAberration>(true);
            p.chroma.intensity.Override(0f);
            return p;
        }

        public void Kick(float amount)
        {
            chromaKick = Mathf.Max(chromaKick, amount);
            vignetteKick = Mathf.Max(vignetteKick, amount * 0.6f);
        }

        public void SetSaturation(float s) => saturationTarget = s;
        public void SetExposure(float e) => exposureTarget = e;

        /// <summary>Background blur from focusDistance (metres); 0 disables.</summary>
        public void SetDof(float amount, float focusDistance)
        {
            dofTarget = amount;
            dofFocus = focusDistance;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            chromaKick = Mathf.MoveTowards(chromaKick, 0f, dt * 2.5f);
            vignetteKick = Mathf.MoveTowards(vignetteKick, 0f, dt * 2f);
            chroma.intensity.Override(Mathf.Clamp01(chromaKick * 0.8f));
            vignette.intensity.Override(0.26f + vignetteKick * 0.25f);
            color.saturation.Override(Mathf.Lerp(color.saturation.value, saturationTarget, 1f - Mathf.Exp(-dt * 4f)));
            color.postExposure.Override(Mathf.Lerp(color.postExposure.value, 0.15f + exposureTarget, 1f - Mathf.Exp(-dt * 4f)));
            if (dofTarget > 0.01f)
            {
                dof.mode.Override(DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(dofFocus);
                dof.gaussianEnd.Override(dofFocus + Mathf.Lerp(30f, 4f, dofTarget));
                dof.gaussianMaxRadius.Override(Mathf.Lerp(0.5f, 1.5f, dofTarget));
            }
            else dof.mode.Override(DepthOfFieldMode.Off);
        }
    }
}
