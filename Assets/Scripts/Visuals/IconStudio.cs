using System.Collections.Generic;
using HWC.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HWC.Visuals
{
    /// <summary>Renders thumbnails of the 3D models (items, materials) for UI cards and the toolbar.</summary>
    public static class IconStudio
    {
        const int Layer = 31;
        static Camera cam;
        static Light key;
        static RenderTexture rt;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly Vector3 Origin = new Vector3(0, -500, 0);

        static void Setup()
        {
            if (cam != null) return;
            var go = new GameObject("IconStudioCam");
            Object.DontDestroyOnLoad(go);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = false;
            rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "icons" };
            cam.targetTexture = rt;
            var lgo = new GameObject("IconStudioKey");
            Object.DontDestroyOnLoad(lgo);
            key = lgo.AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 6f;
            key.intensity = 3f;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.cullingMask = 1 << Layer;
            lgo.transform.position = Origin + new Vector3(-0.8f, 1.2f, -1.4f);
        }

        public static Sprite Piece(PieceKind kind)
        {
            string id = "piece_" + kind;
            if (cache.TryGetValue(id, out var s) && s != null) return s;
            Setup();
            var root = new GameObject("icon_" + kind);
            root.transform.position = Origin;
            var model = ModelLibrary.SpawnPiece(kind, root.transform);
            s = Capture(root, id, kind == PieceKind.Dragon ? -25f : -20f);
            return s;
        }

        public static bool Has(string id) => cache.ContainsKey(id);
        public static Sprite Cached(string id) => cache.TryGetValue(id, out var s) ? s : null;

        public static Sprite Object3D(string id, GameObject root)
        {
            if (cache.TryGetValue(id, out var s) && s != null) { if (root != null) { root.SetActive(false); Object.Destroy(root); } return s; }
            Setup();
            root.transform.position = Origin;
            return Capture(root, id, -20f);
        }

        static Sprite Capture(GameObject root, string id, float yaw)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var b = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            // frame the silhouette the camera actually sees (width and height), not a bounding sphere
            var e = b.extents;
            float radius = Mathf.Max(e.x, e.y * 1.05f) * 1.12f;
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + e.z;
            var dir = Quaternion.Euler(14f, 0, 0) * Vector3.back;
            cam.transform.position = b.center + dir * dist;
            cam.transform.LookAt(b.center);

            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { name = id };
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            // Destroy() is deferred to the end of the frame; hide it now so the next icon captured
            // this frame doesn't render on top of this model
            root.SetActive(false);
            Object.Destroy(root);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            cache[id] = sprite;
            return sprite;
        }
    }
}
