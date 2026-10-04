using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HWC.Visuals
{
    /// <summary>Smoothed camera with trauma-based shake, FOV easing and simple shot targets.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public Vector3 TargetPos;
        public Quaternion TargetRot = Quaternion.identity;
        public float TargetFov = 30f;
        public float PosSharpness = 6f, RotSharpness = 7f, FovSharpness = 5f;
        public float Trauma;
        public bool ShakeEnabled = true;
        /// <summary>Moving reference point: the camera smooths its offset from it, not its world position.</summary>
        public Vector3 Anchor;
        Vector3 baseRel;
        Vector3 basePos;
        Quaternion baseRot;
        float seed;

        public static CameraRig Create()
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Hex("2B2230");
            cam.cullingMask &= ~(1 << 31); // icon studio layer
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            camGo.AddComponent<AudioListener>();
            rig.Cam = cam;
            rig.seed = Random.value * 100f;
            return rig;
        }

        public void Snap()
        {
            baseRel = TargetPos - Anchor;
            basePos = TargetPos;
            baseRot = TargetRot;
            Cam.fieldOfView = TargetFov;
            Apply(0f);
        }

        public void LookAt(Vector3 pos, Vector3 target, float fov)
        {
            TargetPos = pos;
            TargetRot = Quaternion.LookRotation(target - pos, Vector3.up);
            TargetFov = fov;
        }

        public void AddTrauma(float t) { Trauma = Mathf.Clamp01(Trauma + t); }

        void LateUpdate()
        {
            float dt = Mathf.Min(Clock.UnscaledDelta, 0.05f);
            baseRel = Vector3.Lerp(baseRel, TargetPos - Anchor, 1f - Mathf.Exp(-dt * PosSharpness));
            basePos = Anchor + baseRel;
            baseRot = Quaternion.Slerp(baseRot, TargetRot, 1f - Mathf.Exp(-dt * RotSharpness));
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, TargetFov, 1f - Mathf.Exp(-dt * FovSharpness));
            Trauma = Mathf.Max(0f, Trauma - dt * 1.4f);
            Apply(Clock.UnscaledTime);
        }

        void Apply(float t)
        {
            var p = basePos;
            var r = baseRot;
            if (ShakeEnabled && Trauma > 0f)
            {
                float s = Trauma * Trauma;
                float f = 22f;
                p += r * new Vector3((Mathf.PerlinNoise(seed, t * f) - 0.5f) * 0.06f, (Mathf.PerlinNoise(seed + 3, t * f) - 0.5f) * 0.06f, 0) * s;
                r *= Quaternion.Euler((Mathf.PerlinNoise(seed + 7, t * f) - 0.5f) * 3f * s, (Mathf.PerlinNoise(seed + 9, t * f) - 0.5f) * 3f * s, (Mathf.PerlinNoise(seed + 11, t * f) - 0.5f) * 5f * s);
            }
            transform.SetPositionAndRotation(p, r);
        }
    }
}
