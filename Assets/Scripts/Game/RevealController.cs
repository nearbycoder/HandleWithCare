using System;
using System.Collections;
using System.Collections.Generic;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// The unboxing at the customer's table: tape sliced, flaps burst open, each item rises into
    /// a spotlight and gets its rubber stamp, then joins a lineup on the table.
    /// </summary>
    public sealed class RevealController : MonoBehaviour
    {
        static readonly Vector3 RoomOrigin = new Vector3(0, 0, -400f);
        GameObject room;
        Light spot, flash;
        readonly List<GameObject> spawned = new List<GameObject>();
        // the box's own piece views are lifted into the room; put them back afterwards so a replay can use them
        readonly List<(Transform t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, bool active)> moved = new List<(Transform, Transform, Vector3, Quaternion, Vector3, bool)>();
        public bool Running;
        /// <summary>Repeat unboxings of a delivery run faster; the first one (and the egg finale) plays in full.</summary>
        public bool Quick;
        /// <summary>Unscaled time the egg started to hatch (-1 = not yet), for the screenshot tours.</summary>
        public float HatchStartedAt = -1f;
        bool skip;
        float Pace => Quick ? 0.5f : 1f;
        Game G => Game.I;

        public event Action<ItemResult, Vector3> ItemRevealed;   // UI stamps

        void EnsureRoom()
        {
            if (room != null) return;
            room = new GameObject("RevealRoom");
            room.transform.SetParent(transform, false);
            room.transform.position = RoomOrigin;
            var m = ModelLibrary.Spawn("reveal_room", room.transform);
            if (m == null)
                MeshGen.Make("table", MeshGen.RoundedBox(new Vector3(2.6f, 0.06f, 1.6f), 0.02f), Mat.Lit(Palette.Wood, 0.3f), room.transform, new Vector3(0, -0.03f, 0));
            var sgo = new GameObject("spot");
            sgo.transform.SetParent(room.transform, false);
            sgo.transform.localPosition = new Vector3(-0.4f, 2.6f, -1.2f);
            sgo.transform.LookAt(room.transform.position + Vector3.up * 0.4f);
            spot = sgo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 55f;
            spot.range = 6f;
            spot.intensity = 0f;
            spot.color = Palette.Hex("FFE8C8");
            GraphicsQuality.Track(spot, LightShadows.None, LightShadows.Soft, LightShadows.Soft, LightShadows.Soft);
            var fgo = new GameObject("flash");
            fgo.transform.SetParent(room.transform, false);
            fgo.transform.localPosition = new Vector3(0, 0.9f, -0.2f);
            flash = fgo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.range = 4f;
            flash.intensity = 0f;
            flash.color = Palette.Hex("FFE2A8");
        }

        public void Hide()
        {
            Running = false;
            foreach (var g in spawned) if (g != null) Destroy(g);
            spawned.Clear();
            foreach (var m in moved)
            {
                if (m.t == null) continue;
                if (m.parent == null) { Destroy(m.t.gameObject); continue; }
                m.t.SetParent(m.parent, false);
                m.t.localPosition = m.pos;
                m.t.localRotation = m.rot;
                m.t.localScale = m.scale;
                m.t.gameObject.SetActive(m.active);
            }
            moved.Clear();
            if (room != null) room.SetActive(false);
        }

        public void Skip() { skip = true; }

        public IEnumerator Play(Recording rec, BoxView box, Action done)
        {
            Running = true;
            skip = false;
            HatchStartedAt = -1f;
            EnsureRoom();
            room.SetActive(true);
            var preset = LightingPreset.For(null);
            preset.SunEuler = new Vector3(35, -40, 0);
            preset.Sun = Palette.Hex("FFE4C0");
            preset.SunIntensity = 1.4f;
            preset.AmbientSky = Palette.Hex("B8A8D0");
            preset.SkyBottom = Palette.Hex("3A2C3A");
            preset.Apply(G.Sun, G.Rig.Cam);

            // the box arrives on the table, closed and taped
            box.transform.SetParent(room.transform, false);
            box.transform.localRotation = Quaternion.identity;
            box.transform.localPosition = new Vector3(0, box.OuterHalfHeight + 0.6f, 0);
            box.SetFlaps(1f, true);
            box.SetTape(1f, true);
            Reflections.Capture(box.transform.position + Vector3.up * 0.4f, new Vector3(6f, 4f, 6f));
            float boxH = box.OuterHalfHeight * 2f;
            float size = Mathf.Max(box.InteriorWidth, boxH * 1.3f);
            var rig = G.Rig;
            rig.Anchor = Vector3.zero;
            var center = room.transform.position + new Vector3(0, boxH * 0.5f, 0);
            float dist = 1.4f + size * 1.5f;
            rig.LookAt(center + new Vector3(0.5f, dist * 0.45f, -dist * 1.2f), center, 34f);
            rig.Snap();
            rig.LookAt(center + new Vector3(0.25f, dist * 0.32f, -dist), center + Vector3.up * 0.05f, 30f);
            rig.PosSharpness = 2.5f;
            // keep the box and the lineup sharp; only the room behind them softens
            G.Post.SetDof(0.7f, dist * 1.05f + size * 0.7f);

            // drop onto the table
            float t = 0;
            var from = box.transform.localPosition;
            var to = new Vector3(0, box.OuterHalfHeight, 0);
            while (t < 0.35f)
            {
                t += Time.deltaTime / Pace;
                float u = Mathf.Clamp01(t / 0.35f);
                box.transform.localPosition = Vector3.Lerp(from, to, u * u);
                yield return null;
            }
            box.transform.localPosition = to;
            G.Hud.Sfx("thud", 0.8f);
            rig.AddTrauma(0.25f);
            Fx.Dust(box.transform.position - Vector3.up * box.OuterHalfHeight, box.W);
            yield return Wait(0.55f * Pace);

            // slice the tape
            G.Hud.Sfx("slice");
            box.SetTape(0f, false);
            yield return Wait(0.45f * Pace);

            // flaps burst open with a flash of light
            G.Hud.Sfx("flaps_open");
            box.SetFlaps(0f, false);
            StartCoroutine(Flash());
            Fx.Dust(box.transform.position + Vector3.up * box.OuterHalfHeight, box.W * 1.5f);
            Fx.Sparkle(box.transform.position + Vector3.up * box.OuterHalfHeight, Color.white, box.InteriorWidth * 0.5f);
            spot.intensity = 0f;
            yield return Wait(0.6f * Pace);

            // items rise one at a time
            var items = new List<ItemResult>(rec.Outcome.Items);
            int n = items.Count;
            float lineup = Mathf.Max(box.InteriorWidth + 0.5f, n * 0.42f);
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                var view = it.PieceIndex >= 0 && it.PieceIndex < box.Pieces.Count ? box.Pieces[it.PieceIndex] : null;
                GameObject show;
                if (view != null && view.gameObject.activeInHierarchy && (rec.Frames[rec.Frames.Count - 1][it.Body].State & BodyState.Removed) == 0)
                {
                    show = view.gameObject;
                    var vt = show.transform;
                    moved.Add((vt, vt.parent, vt.localPosition, vt.localRotation, vt.localScale, show.activeSelf));
                }
                else
                {
                    // popped / melted / burned: present what is left
                    show = new GameObject("remnant_" + it.Kind);
                    show.transform.SetParent(box.Contents, false);
                    var rv = PieceView.Create(it.Kind, show.transform);
                    rv.ShowRemnant(it.Status);
                    var last = rec.Frames[rec.Frames.Count - 1][it.Body];
                    show.transform.localPosition = new Vector3(last.Pos.x * PieceView.Cell, last.Pos.y * PieceView.Cell, 0);
                    spawned.Add(show);
                }
                var pv = show.GetComponent<PieceView>() ?? show.GetComponentInChildren<PieceView>();
                if (pv != null && it.Status == ItemStatus.Perfect || it.Status == ItemStatus.Fine) { }
                var startPos = show.transform.position;
                show.transform.SetParent(room.transform, true);
                var top = room.transform.position + new Vector3(0, boxH + 0.35f, -0.1f);
                // lineup just in front of the box, close enough to stay in frame while the rest rise
                var slot = room.transform.position + new Vector3(-lineup * 0.5f + (i + 0.5f) * lineup / n, Catalog.Get(it.Kind).H * 0.125f + 0.01f, -0.4f);
                float riseScale = it.Status == ItemStatus.Broken ? 2.1f : 1.35f;   // a shard pile needs to be bigger to read
                float restScale = it.Status == ItemStatus.Broken ? 1.4f : 1f;
                spot.intensity = 0f;
                G.Hud.Sfx("lift", 0.8f);
                // rise
                t = 0;
                var startRot = show.transform.rotation;
                while (t < 0.55f && !skip)
                {
                    t += Time.deltaTime / Pace;
                    float u = Mathf.Clamp01(t / 0.55f);
                    float e = 1f - Mathf.Pow(1f - u, 3f);
                    show.transform.position = Vector3.Lerp(startPos, top, e) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.08f;
                    show.transform.rotation = Quaternion.Slerp(startRot, Quaternion.Euler(0, Mathf.Sin(u * Mathf.PI) * 25f, 0), e);
                    show.transform.localScale = Vector3.one * Mathf.Lerp(1f, riseScale, e);
                    spot.intensity = Mathf.Lerp(0, 6f, e);
                    rig.LookAt(rig.TargetPos, Vector3.Lerp(center, top, 0.32f * e), 30f);
                    yield return null;
                }
                show.transform.position = top;
                ItemRevealed?.Invoke(it, top);
                StampFx(it, top);
                if (it.Kind == PieceKind.Dragon && !it.Failed) StartCoroutine(RevealSneeze(show.transform));
                if (it.Kind == PieceKind.DragonEgg && !it.Failed && rec.Outcome.Delivered) yield return Hatch(show, top);
                yield return Wait((it.Failed ? 1.1f : 0.85f) * Pace);
                // settle into the lineup
                t = 0;
                while (t < 0.4f && !skip)
                {
                    t += Time.deltaTime / Pace;
                    float u = Mathf.Clamp01(t / 0.4f);
                    float e = u * u * (3 - 2 * u);
                    show.transform.position = Vector3.Lerp(top, slot, e) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.12f;
                    show.transform.localScale = Vector3.one * Mathf.Lerp(riseScale, restScale, e);
                    show.transform.rotation = Quaternion.Slerp(show.transform.rotation, Quaternion.Euler(0, -12f, 0), e);
                    yield return null;
                }
                show.transform.position = slot;
                show.transform.localScale = Vector3.one * restScale;
                G.Hud.Sfx("place", 0.6f);
                Fx.Dust(slot - Vector3.up * Catalog.Get(it.Kind).H * 0.125f, 1);
                spot.intensity = 0f;
            }
            rig.LookAt(center + new Vector3(0.1f, dist * 0.5f, -dist * 1.15f), center + new Vector3(0, -0.05f, -0.3f), 32f);
            yield return Wait(0.4f);
            Running = false;
            done?.Invoke();
        }

        IEnumerator Wait(float s)
        {
            float t = 0;
            while (t < s && !skip) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator Flash()
        {
            float t = 0;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                flash.intensity = Mathf.Lerp(4.5f, 0f, t / 0.6f);
                yield return null;
            }
            flash.intensity = 0;
        }

        void StampFx(ItemResult it, Vector3 pos)
        {
            if (it.Failed)
            {
                G.Hud.Sfx("stamp_bad");
                G.Rig.AddTrauma(0.2f);
            }
            else
            {
                G.Hud.Sfx(it.Status == ItemStatus.Perfect ? "stamp_good" : "stamp_ok");
                Fx.Sparkle(pos, it.Status == ItemStatus.Perfect ? Palette.Gold : Color.white, 0.18f);
            }
        }

        /// <summary>The finale: the egg wobbles, cracks and a hatchling pops out (and sneezes).</summary>
        IEnumerator Hatch(GameObject egg, Vector3 at)
        {
            HatchStartedAt = Time.unscaledTime;
            float t = 0;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                egg.transform.rotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 30f) * 12f * t);
                yield return null;
            }
            G.Hud.Sfx("crack", 1f);
            Fx.Shards(at, Palette.ItemColor(PieceKind.DragonEgg), 0.6f);
            Fx.Sparkle(at, Palette.Gold, 0.3f);
            egg.SetActive(false);
            var baby = new GameObject("hatchling");
            baby.transform.SetParent(room.transform, true);
            baby.transform.position = at;
            var bv = PieceView.Create(PieceKind.Dragon, baby.transform);
            bv.transform.localScale = Vector3.one * 0.6f;
            spawned.Add(baby);
            t = 0;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / 0.35f);
                baby.transform.localScale = Vector3.one * (u < 0.7f ? Mathf.Lerp(0.2f, 1.2f, u / 0.7f) : Mathf.Lerp(1.2f, 1f, (u - 0.7f) / 0.3f));
                yield return null;
            }
            G.Hud.Sfx("fanfare", 0.8f);
            Fx.Confetti(at + Vector3.down * 0.2f, 0.6f);
            // just above the hatchling: higher and the stamp leaves the top of the frame on the 6x4 box
            ItemRevealed?.Invoke(new ItemResult { Kind = PieceKind.DragonEgg, Status = ItemStatus.Perfect, Body = -1 }, at + Vector3.up * 0.08f);
            yield return RevealSneeze(baby.transform);
            yield return Wait(0.6f);
            egg.SetActive(true);
            egg.transform.localScale = Vector3.zero;
            baby.transform.SetParent(egg.transform.parent, true);
        }

        IEnumerator RevealSneeze(Transform dragon)
        {
            yield return new WaitForSeconds(0.35f);
            G.Hud.Sfx("ahh");
            yield return new WaitForSeconds(0.45f);
            G.Hud.Sfx("sneeze");
            Fx.Fire(dragon.position + dragon.right * 0.3f, (G.Rig.Cam.transform.position - dragon.position).normalized * 0.4f + dragon.right * 0.6f, 0.6f);
            G.Rig.AddTrauma(0.3f);
        }
    }
}
