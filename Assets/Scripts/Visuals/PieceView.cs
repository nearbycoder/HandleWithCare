using System.Collections.Generic;
using HWC.Sim;
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
                    mats[j] = baseC.a < 0.99f ? Mat.Glass(mixed) : Mat.Lit(mixed, 0.25f);
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
            if (snap) toppleAngle = toppleTarget;

            var changed = f.State ^ State;
            State = f.State;
            if (changed != 0) ApplyStateLook();
            if (f.Jolt > 1.5f && !snap)
            {
                float lim = Def.JoltLimit > 0 ? Def.JoltLimit : 12f;
                Kick(f.Jolt / lim, Vector2.zero);
            }
        }

        void ApplyStateLook()
        {
            bool removed = (State & BodyState.Removed) != 0;
            if (removed) hidden = 1f;
            if ((State & (BodyState.Broken | BodyState.Squished)) != 0)
            {
                SetTint(Palette.Hex("6B5E57"), 0.35f);
                baseScale = (State & BodyState.Squished) != 0 ? new Vector3(1.15f, 0.45f, 1.1f) : new Vector3(1.05f, 0.55f, 1.0f);
            }
            else if ((State & (BodyState.Scorched | BodyState.Burned)) != 0) SetTint(Palette.Hex("2A2220"), 0.6f);
            else if ((State & BodyState.Popped) != 0 && Kind == PieceKind.Bubble) SetTint(Palette.Hex("A0A8AC"), 0.4f);
            else if ((State & BodyState.Spilled) != 0) SetTint(Palette.Hex("5A3B7A"), 0.3f);
        }

        public void ResetLook()
        {
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

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * Mathf.Max(0.15f, Time.timeScale);
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
            var sc = new Vector3(baseScale.x * (1f - s * 0.6f), baseScale.y * (1f + s), baseScale.z * (1f - s * 0.6f)) * popScale;
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
