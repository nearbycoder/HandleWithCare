using System.Collections.Generic;
using HWC.Sim;
using TMPro;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>
    /// The 3D presentation of one piece. Positioned in box-content space (1 cell = 0.25 m),
    /// with secondary motion (squash, wobble, topple, roll) layered on top of exact sim poses.
    /// </summary>
    public sealed class PieceView : MonoBehaviour
    {
        public const float Cell = ModelLibrary.Cell;

        public PieceKind Kind;
        public PieceDef Def;
        public int Facing = 1;
        public bool Rotated;
        public bool Strapped;
        public BodyState State;

        Transform pivot;     // centre of the piece: topple/rotation/squash
        Transform model;     // the mesh (facing mirror)
        Transform rollT;     // roll rotation
        GameObject strapGo;
        Renderer[] renderers;
        Material[][] originalMats;
        bool ghost;

        // secondary motion
        float squash, squashVel;
        Vector2 wobble, wobbleVel;
        float toppleAngle, toppleTarget;
        float dropT = 1f;
        float hidden;
        float walkT;
        float popScale = 1f;
        Vector3 baseScale = Vector3.one;

        public Vector3 CenterLocal => transform.localPosition;
        public Transform Pivot => pivot;
        public Transform Model => model;

        public static PieceView Create(PieceKind kind, Transform parent, bool rotated = false, int facing = 1)
        {
            var go = new GameObject("piece_" + kind);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PieceView>();
            v.Build(kind, rotated, facing);
            return v;
        }

        void Build(PieceKind kind, bool rotated, int facing)
        {
            Kind = kind;
            Def = Catalog.Get(kind);
            pivot = new GameObject("pivot").transform;
            pivot.SetParent(transform, false);
            rollT = new GameObject("roll").transform;
            rollT.SetParent(pivot, false);
            var m = ModelLibrary.SpawnPiece(kind, rollT);
            model = m.transform;
            renderers = GetComponentsInChildren<Renderer>(true);
            originalMats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) originalMats[i] = renderers[i].sharedMaterials;
            SetRotated(rotated);
            SetFacing(facing);
        }

        public void SetRotated(bool r)
        {
            Rotated = r;
            pivot.localRotation = Quaternion.Euler(0, 0, r ? 90f : 0f);
        }

        public void SetFacing(int f)
        {
            Facing = f >= 0 ? 1 : -1;
            model.localScale = new Vector3(Facing, 1, 1);
        }

        public void SetStrapped(bool s)
        {
            Strapped = s;
            if (s && strapGo == null)
            {
                strapGo = ModelLibrary.Spawn("strap", pivot);
                if (strapGo == null)
                {
                    strapGo = new GameObject("strap");
                    strapGo.transform.SetParent(pivot, false);
                    var band = MeshGen.Make("band", MeshGen.RoundedBox(new Vector3(Def.W * Cell * 1.02f, Cell * 0.12f, Cell * 0.9f), 0.01f), Mat.Lit(Palette.Hex("2E4A7A"), 0.3f), strapGo.transform);
                    band.transform.localPosition = Vector3.zero;
                }
                float sx = Def.W, sy = Def.H;
                strapGo.transform.localScale = new Vector3(sx, 1, 1);
            }
            if (strapGo != null) strapGo.SetActive(s);
        }

        Vector3 moveTarget;
        bool moving;

        /// <summary>Places the piece at a sim pose (cells, centre) in content space.</summary>
        public void SetPose(Vector2 centerCells)
        {
            moving = false;
            transform.localPosition = new Vector3(centerCells.x * Cell, centerCells.y * Cell, 0f);
        }

        /// <summary>Glides to a pose (packing mode: settling, undo).</summary>
        public void MoveTo(Vector2 centerCells)
        {
            moveTarget = new Vector3(centerCells.x * Cell, centerCells.y * Cell, 0f);
            moving = (moveTarget - transform.localPosition).sqrMagnitude > 1e-8f;
        }

        public void SetCellRect(int x, int y, int w, int h)
        {
            SetPose(new Vector2(x + w * 0.5f, y + h * 0.5f));
        }

        bool hovered;
        float hoverAmt;

        /// <summary>Gentle lift and glow when the cursor is over a placed piece.</summary>
        public void SetHover(bool on)
        {
            if (on && !hovered) Kick(0.25f, Vector2.up);
            hovered = on;
        }

        public void PlayDrop()
        {
            dropT = 0f;
            squash = -0.22f;
            squashVel = 0f;
        }

        public void Kick(float amount, Vector2 dir)
        {
            squash += -Mathf.Clamp(amount, 0, 1.2f) * 0.18f;
            wobbleVel += dir * amount * 6f;
        }

        public void SetGhost(bool on, Color tint)
        {
            ghost = on;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!on) { r.sharedMaterials = originalMats[i]; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; continue; }
                var mats = new Material[originalMats[i].Length];
                for (int j = 0; j < mats.Length; j++) mats[j] = Mat.Glass(tint, 0.2f);
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public void SetTint(Color c, float amount)
        {
            if (ghost) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = new Material[originalMats[i].Length];
                for (int j = 0; j < mats.Length; j++)
                {
                    var src = originalMats[i][j];
                    if (amount <= 0f) { mats[j] = src; continue; }
                    Color baseC = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
                    var mixed = Color.Lerp(baseC, c, amount);
                    mixed.a = baseC.a;
                    bool textured = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null;
                    if (textured && baseC.a >= 0.99f) mats[j] = Mat.Tinted(src, Color.Lerp(Color.white, c, amount));
                    else mats[j] = baseC.a < 0.99f ? Mat.Glass(mixed) : Mat.Lit(mixed, 0.25f);
                }
                renderers[i].sharedMaterials = mats;
            }
        }

        /// <summary>Applies a recorded sim frame (journey/replay).</summary>
        public void ApplyFrame(in BodyFrame f, bool snap)
        {
            SetPose(new Vector2(f.Pos.x, f.Pos.y));
            if (f.Facing != 0 && f.Facing != Facing) SetFacing(f.Facing);
            if (Def.Has(Quirk.Rolls)) rollT.localRotation = Quaternion.Euler(0, 0, f.Roll * Mathf.Rad2Deg);

            // toppled: lying AABB (half swapped relative to the def's upright shape)
            bool toppled = (f.State & BodyState.Toppled) != 0;
            toppleTarget = toppled && Def.H > Def.W ? -90f * (f.ToppleDir == 0 ? 1 : f.ToppleDir) : 0f;
            if ((f.State & BodyState.Broken) != 0 && Kind != PieceKind.Cake) toppleTarget = toppleAngle = 0f;   // shards lie flat
            if (snap) toppleAngle = toppleTarget;

            var changed = f.State ^ State;
            if ((changed & f.State & BodyState.Hopping) != 0) { squash = 0.3f; squashVel = 0; }
            State = f.State;
            if (changed != 0) ApplyStateLook();
            if (f.Jolt > 1.5f && !snap)
            {
                float lim = Def.JoltLimit > 0 ? Def.JoltLimit : 12f;
                Kick(f.Jolt / lim, Vector2.zero);
            }
        }

        string currentModel;

        /// <summary>Replaces the mesh with another model (broken shards, awake armadillo, puddle).</summary>
        void SwapModel(string id, Color? tint)
        {
            if (currentModel == id) return;
            var go = ModelLibrary.Spawn(id, rollT);
            if (go == null) return;
            currentModel = id;
            model.SetParent(null, false);   // out of the renderers collected below (Destroy waits for the frame's end)
            Destroy(model.gameObject);
            model = go.transform;
            model.localScale = new Vector3(Facing, 1, 1);
            renderers = GetComponentsInChildren<Renderer>(true);
            originalMats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (tint.HasValue)
                {
                    var mats = renderers[i].sharedMaterials;
                    for (int j = 0; j < mats.Length; j++) mats[j] = Mat.Lit(tint.Value, 0.6f);
                    renderers[i].sharedMaterials = mats;
                }
                originalMats[i] = renderers[i].sharedMaterials;
            }
        }

        void ApplyStateLook()
        {
            bool removed = (State & BodyState.Removed) != 0;
            if (removed) hidden = 1f;
            if ((State & BodyState.Broken) != 0 && Kind != PieceKind.Cake)
            {
                SwapModel("piece_shards", Palette.ItemColor(Kind));
                rollT.localRotation = Quaternion.identity;
                // the sim collapses a shattered item into a low pile (SimConst.ShardHalfH); sit the
                // one-cell shard model's base (modelled at -Cell/2) on the pile's bottom
                model.localPosition = new Vector3(0, Cell * 0.5f - SimConst.ShardHalfH * Cell - 0.0075f, 0);
            }
            else if ((State & BodyState.Squished) != 0)
            {
                SetTint(Palette.Hex("B8867A"), 0.25f);
                baseScale = new Vector3(1.12f, 0.5f, 1.08f);
            }
            else if ((State & (BodyState.Scorched | BodyState.Burned)) != 0) SetTint(Palette.Hex("2A2220"), 0.6f);
            else if ((State & BodyState.Popped) != 0 && Kind == PieceKind.Bubble) SetTint(Palette.Hex("A0A8AC"), 0.4f);
            if ((State & BodyState.StrapSnapped) != 0 && strapGo != null) strapGo.SetActive(false);
            if ((State & BodyState.Awake) != 0 && Kind == PieceKind.Armadillo) SwapModel("piece_armadillo_awake", null);
            if ((State & BodyState.Spilled) != 0 && spill == null)
            {
                spill = ModelLibrary.Spawn("piece_puddle", transform);
                if (spill != null)
                {
                    foreach (var r in spill.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Mat.Glass(new Color(0.56f, 0.31f, 0.77f, 0.7f));
                    spill.transform.localPosition = new Vector3(0, -Mathf.Min(Def.W, Def.H) * Cell * 0.5f + 0.01f, 0);
                    spill.transform.localScale = new Vector3(1.6f, 1, 1.2f);
                }
            }
        }

        GameObject spill;

        /// <summary>For pieces that no longer exist (popped, melted, burned): show what's left.</summary>
        public void ShowRemnant(ItemStatus status)
        {
            switch (status)
            {
                case ItemStatus.Melted:
                    SwapModel("piece_puddle", new Color(0.75f, 0.9f, 0.97f));
                    foreach (var r in renderers) r.sharedMaterial = Mat.Glass(new Color(0.75f, 0.9f, 0.97f, 0.6f));
                    break;
                case ItemStatus.Popped:
                    SwapModel("piece_shards", Palette.ItemColor(Kind));
                    baseScale = new Vector3(0.8f, 0.4f, 0.8f);
                    break;
                default:
                    SwapModel("piece_shards", Palette.Hex("2A2220"));
                    break;
            }
        }

        public void ResetLook()
        {
            if (currentModel != null)
            {
                // detach the swapped model first: Destroy waits for the end of the frame, and its renderers
                // must not be collected below (a later reset would touch them once they are gone)
                model.SetParent(null, false);
                Destroy(model.gameObject);
                model = ModelLibrary.SpawnPiece(Kind, rollT).transform;
                model.localScale = new Vector3(Facing, 1, 1);
                renderers = GetComponentsInChildren<Renderer>(true);
                originalMats = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++) originalMats[i] = renderers[i].sharedMaterials;
                currentModel = null;
            }
            if (spill != null) { Destroy(spill); spill = null; }
            State = BodyState.None;
            hidden = 0f;
            popScale = 1f;
            baseScale = Vector3.one;
            toppleAngle = toppleTarget = 0f;
            squash = squashVel = 0;
            wobble = wobbleVel = Vector2.zero;
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = originalMats[i];
            gameObject.SetActive(true);
        }

        // sleepy z's for sleepers (packing and journey)
        readonly List<TextMeshPro> zs = new List<TextMeshPro>();
        float zT;

        void UpdateZs(float dt)
        {
            bool asleep = Def.Has(Quirk.Sleeper) && (State & BodyState.Awake) == 0 && !ghost && gameObject.activeInHierarchy;
            if (asleep && zs.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var go = new GameObject("z");
                    go.transform.SetParent(transform, false);
                    var t = go.AddComponent<TextMeshPro>();
                    t.text = "z";
                    t.font = HWC.UI.Ui.Display;
                    t.fontSize = 1.2f;
                    t.color = Palette.Hex("3B4766");
                    t.alignment = TextAlignmentOptions.Center;
                    t.rectTransform.sizeDelta = new Vector2(0.2f, 0.2f);
                    zs.Add(t);
                }
            }
            if (!asleep)
            {
                foreach (var z in zs) if (z != null) z.gameObject.SetActive(false);
                return;
            }
            zT += dt;
            for (int i = 0; i < zs.Count; i++)
            {
                var z = zs[i];
                z.gameObject.SetActive(true);
                float u = (zT * 0.45f + i / 3f) % 1f;
                z.transform.localPosition = new Vector3(Cell * 0.25f + u * 0.06f + Mathf.Sin(u * 7f) * 0.012f, Cell * 0.45f + u * 0.16f, -0.12f);
                z.transform.localScale = Vector3.one * (0.5f + u * 0.9f);
                z.alpha = Mathf.Sin(u * Mathf.PI) * 0.9f;
                z.transform.rotation = Quaternion.LookRotation(z.transform.position - (Camera.main != null ? Camera.main.transform.position : z.transform.position - Vector3.forward));
            }
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Clock.UnscaledDelta, 0.05f) * Mathf.Max(0.15f, Time.timeScale);
            UpdateZs(dt);
            if ((State & BodyState.Walking) != 0 && (State & BodyState.Removed) == 0)
            {
                walkT += dt * 9f;
                model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(walkT)) * 0.012f, 0);
                model.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(walkT) * 6f);
            }
            // spring for squash and wobble
            const float k = 260f, damp = 14f;
            squashVel += (-k * squash - damp * squashVel) * dt;
            squash += squashVel * dt;
            wobbleVel += (-k * wobble - damp * wobbleVel) * dt;
            wobble += wobbleVel * dt;
            toppleAngle = Mathf.MoveTowards(toppleAngle, toppleTarget, 520f * dt);
            if (dropT < 1f) dropT = Mathf.Min(1f, dropT + dt * 6f);
            if (hidden > 0f) popScale = Mathf.MoveTowards(popScale, 0f, dt * 6f);

            if (moving)
            {
                var p = transform.localPosition;
                var np = Vector3.MoveTowards(p, moveTarget, dt * 3.5f);
                np = Vector3.Lerp(np, moveTarget, 1f - Mathf.Exp(-dt * 18f));
                transform.localPosition = np;
                if ((np - moveTarget).sqrMagnitude < 1e-7f) { transform.localPosition = moveTarget; moving = false; squash -= 0.12f; }
            }

            float s = Mathf.Clamp(squash, -0.4f, 0.4f);
            hoverAmt = Mathf.MoveTowards(hoverAmt, hovered ? 1f : 0f, dt * 8f);
            var sc = new Vector3(baseScale.x * (1f - s * 0.6f), baseScale.y * (1f + s), baseScale.z * (1f - s * 0.6f)) * popScale * (1f + hoverAmt * 0.05f);
            pivot.localScale = sc;
            float rotBase = Rotated ? 90f : 0f;
            pivot.localRotation = Quaternion.Euler(wobble.y * 8f, 0, rotBase + toppleAngle + wobble.x * 8f);
            if (popScale <= 0.001f && hidden > 0f) pivot.gameObject.SetActive(false);
            else if (!pivot.gameObject.activeSelf) pivot.gameObject.SetActive(true);
        }

        public Bounds WorldBounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers) if (r.enabled) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
