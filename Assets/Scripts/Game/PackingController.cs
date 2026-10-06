using System;
using System.Collections.Generic;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HWC.Gameplay
{
    public enum Tool { None, Item, Padding, Divider, Shelf, Strap }

    /// <summary>
    /// The packing puzzle interaction: pick items from the shelf, drop them into the box grid,
    /// paint padding, place dividers/shelves, strap items, undo/redo. Owns the piece views.
    /// </summary>
    public sealed class PackingController : MonoBehaviour
    {
        public LevelDef Level;
        public Packing Pk;
        public BoxView Box;
        public Tool Tool;
        public PieceKind HeldKind;
        public bool HeldRotated;
        public int HeldFacing = 1;
        public bool HeldStrapped;
        public bool Active;

        public event Action Changed;
        public event Action<PieceKind?> Hovered;   // item card
        public event Action<string> Feedback;      // short messages ("Needs support")

        readonly List<PieceView> views = new List<PieceView>();
        readonly List<TrayItem> tray = new List<TrayItem>();
        readonly Stack<string> undo = new Stack<string>();
        readonly Stack<string> redo = new Stack<string>();
        PieceView ghost;
        PieceKind ghostKind;
        bool ghostRot;
        GameObject staticGhost;
        Tool staticGhostTool;
        Placement ghostPlacement;
        bool ghostValid;
        Vector3 ghostSmooth;
        bool painting, erasing;
        Vector2Int lastPaintCell = new Vector2Int(-99, -99);
        int hoverPiece = -1;
        int lastHover = -1;
        PieceKind? lastHovered;
        Recording lastRun;
        TrailOverlay trails;

        sealed class TrayItem
        {
            public PieceKind Kind;
            public PieceView View;
            public int Slot;
        }

        Game G => Game.I;

        // ---- Lifecycle -------------------------------------------------------------------------

        public void Begin(LevelDef lv, Packing pk, Recording last)
        {
            Level = lv;
            Pk = pk;
            Box = G.Station.Box;
            lastRun = last;
            Active = true;
            Tool = Tool.None;
            undo.Clear();
            redo.Clear();
            RebuildViews(true);
            RebuildTray();
            if (trails == null) trails = new GameObject("Trails").AddComponent<TrailOverlay>();
            trails.Show(Box, last);
            if (quirks == null) quirks = new GameObject("Quirks").AddComponent<QuirkOverlay>();
            Changed -= RefreshQuirks;
            Changed += RefreshQuirks;
            Changed?.Invoke();
        }

        public void End()
        {
            Active = false;
            DropTool();
            foreach (var t in tray) if (t.View != null) Destroy(t.View.gameObject);
            tray.Clear();
            if (trails != null) trails.Hide();
            if (quirks != null) quirks.Clear();
            ClearHints();
            Hovered?.Invoke(null);
        }

        // ---- Ask Mabel: ghosts of the reference packing --------------------------------------------

        public readonly List<Placement> HintPieces = new List<Placement>();
        public readonly List<int> HintDividers = new List<int>();
        public readonly List<ShelfSpec> HintShelves = new List<ShelfSpec>();
        readonly List<GameObject> hintObjects = new List<GameObject>();
        static readonly Color HintTint = new Color(1f, 0.86f, 0.3f, 0.5f);
        static readonly Color HintStaticTint = new Color(1f, 0.8f, 0.2f, 0.85f);   // dividers and shelves are thin: stronger

        void ClearHints()
        {
            foreach (var o in hintObjects) if (o != null) Destroy(o);
            hintObjects.Clear();
            HintPieces.Clear(); HintDividers.Clear(); HintShelves.Clear();
        }

        /// <summary>Shows the hint ghosts for a stage (0 or hidden = none).</summary>
        public void ShowHints(int stage, PieceKind focus, bool visible)
        {
            ClearHints();
            if (!Active || stage <= 0) return;
            HintPieces.AddRange(Hints.Pieces(Level, stage, focus));
            var src = Hints.Source(Level);
            if (Hints.ShowsStatics(stage)) { HintDividers.AddRange(src.Dividers); HintShelves.AddRange(src.Shelves); }
            if (!visible) return;
            foreach (var p in HintPieces)
            {
                var v = PieceView.Create(p.Kind, Box.Contents, p.Rotated, p.Facing);
                v.SetCellRect(p.X, p.Y, p.W, p.H);
                v.SetStrapped(p.Strapped);
                v.SetGhost(true, HintTint);
                v.name = "hint_" + p.Kind;
                hintObjects.Add(v.gameObject);
            }
            foreach (int d in HintDividers)
            {
                var go = Box.MakeDivider(d, Box.Contents, true);
                BoxView.TintGhost(go, HintStaticTint);
                hintObjects.Add(go);
            }
            foreach (var sh in HintShelves)
            {
                src.ShelfSpan(sh, out int x0, out int x1);
                var go = Box.MakeShelf(sh.Row, x0, x1, Box.Contents, true);
                BoxView.TintGhost(go, HintStaticTint);
                hintObjects.Add(go);
            }
        }

        public IReadOnlyList<PieceView> Views => views;
        QuirkOverlay quirks;

        void RefreshQuirks()
        {
            if (quirks != null && Active) quirks.Rebuild(Box, Pk);
        }

        void RebuildViews(bool instant)
        {
            foreach (var v in views) if (v != null) Destroy(v.gameObject);
            views.Clear();
            foreach (var p in Pk.Pieces)
            {
                var v = PieceView.Create(p.Kind, Box.Contents, p.Rotated, p.Facing);
                v.SetCellRect(p.X, p.Y, p.W, p.H);
                v.SetStrapped(p.Strapped);
                views.Add(v);
            }
            Box.Pieces.Clear();
            Box.Pieces.AddRange(views);
            Box.BuildStatics(Pk);
        }

        void SyncViewPositions()
        {
            for (int i = 0; i < Pk.Pieces.Count; i++)
            {
                var p = Pk.Pieces[i];
                views[i].MoveTo(new Vector2(p.X + p.W * 0.5f, p.Y + p.H * 0.5f));
                views[i].SetStrapped(p.Strapped);
            }
        }

        void RebuildTray()
        {
            foreach (var t in tray) if (t.View != null) Destroy(t.View.gameObject);
            tray.Clear();
            var remaining = RemainingItems();
            if (Tool == Tool.Item) remaining.Remove(HeldKind);
            for (int i = 0; i < Level.Items.Length; i++)
            {
                // slots are fixed per level item index; show only the unplaced ones
                var k = Level.Items[i];
                if (!remaining.Remove(k)) continue;
                var v = PieceView.Create(k, G.Station.ItemShelf, false, 1);
                v.transform.position = G.Station.SlotPosition(i) + Vector3.up * (Catalog.Get(k).H * BoxView.Cell * 0.5f + 0.005f);
                v.PlayDrop();
                tray.Add(new TrayItem { Kind = k, View = v, Slot = i });
            }
        }

        /// <summary>Items of the order not yet in the box (multiset).</summary>
        public List<PieceKind> RemainingItems()
        {
            var list = new List<PieceKind>(Level.Items);
            foreach (var p in Pk.Pieces) if (!p.Def.IsPadding) list.Remove(p.Kind);
            return list;
        }

        public int Remaining(MaterialSlot s) => Level.Materials.Get(s) - Pk.UsedMaterials().Get(s);

        public bool ReadyToSeal => Pk.AllItemsPlaced(Level) && Pk.Validate(Level) == null;

        // ---- Undo ----------------------------------------------------------------------------

        void Snapshot()
        {
            undo.Push(SaveData.Serialize(Pk));
            redo.Clear();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Push(SaveData.Serialize(Pk));
            Restore(undo.Pop());
            G.Hud.Sfx("undo");
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(SaveData.Serialize(Pk));
            Restore(redo.Pop());
            G.Hud.Sfx("undo");
        }

        void Restore(string s)
        {
            DropTool();
            var restored = SaveData.Deserialize(Level, s);
            Pk.Pieces.Clear(); Pk.Pieces.AddRange(restored.Pieces);
            Pk.Dividers.Clear(); Pk.Dividers.AddRange(restored.Dividers);
            Pk.Shelves.Clear(); Pk.Shelves.AddRange(restored.Shelves);
            RebuildViews(true);
            RebuildTray();
            Changed?.Invoke();
        }

        public void ClearAll()
        {
            if (Pk.Pieces.Count == 0 && Pk.Dividers.Count == 0 && Pk.Shelves.Count == 0) return;
            Snapshot();
            DropTool();
            Pk.Pieces.Clear(); Pk.Dividers.Clear(); Pk.Shelves.Clear();
            RebuildViews(true);
            RebuildTray();
            G.Hud.Sfx("clear");
            Changed?.Invoke();
        }

        // ---- Tools ------------------------------------------------------------------------------

        public void SelectMaterial(MaterialSlot s)
        {
            if (!Active) return;
            if (Remaining(s) <= 0 && s != MaterialSlot.Strap && s != MaterialSlot.Divider && s != MaterialSlot.Shelf)
            {
                Feedback?.Invoke("None left");
                G.Hud.Sfx("nope");
                return;
            }
            DropTool();
            switch (s)
            {
                case MaterialSlot.Paper: HoldPiece(PieceKind.Paper, false, 1, false, Tool.Padding); break;
                case MaterialSlot.Bubble: HoldPiece(PieceKind.Bubble, false, 1, false, Tool.Padding); break;
                case MaterialSlot.Foam: HoldPiece(PieceKind.Foam, false, 1, false, Tool.Padding); break;
                case MaterialSlot.Divider: Tool = Tool.Divider; break;
                case MaterialSlot.Shelf: Tool = Tool.Shelf; break;
                case MaterialSlot.Strap: Tool = Tool.Strap; break;
            }
            G.Hud.Sfx("pick");
            Changed?.Invoke();
        }

        void HoldPiece(PieceKind k, bool rot, int facing, bool strapped, Tool tool)
        {
            Tool = tool;
            HeldKind = k;
            HeldRotated = rot && Catalog.Get(k).Rotatable;
            HeldFacing = facing;
            HeldStrapped = strapped;
            EnsureGhost();
        }

        public void DropTool()
        {
            // an item held in hand goes back to the shelf
            bool wasItem = Tool == Tool.Item;
            Tool = Tool.None;
            painting = erasing = false;
            if (ghost != null) { Destroy(ghost.gameObject); ghost = null; }
            if (staticGhost != null) { Destroy(staticGhost); staticGhost = null; }
            if (Active && wasItem) RebuildTray();
            Changed?.Invoke();
        }

        void EnsureGhost()
        {
            if (ghost != null && ghostKind == HeldKind && ghostRot == HeldRotated) { ghost.SetFacing(HeldFacing); return; }
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = PieceView.Create(HeldKind, Box.Contents, HeldRotated, HeldFacing);
            ghostKind = HeldKind;
            ghostRot = HeldRotated;
            ghost.SetGhost(true, new Color(1f, 1f, 1f, 0.5f));
            ghost.name = "ghost";
            ghostSmooth = Vector3.zero;
        }

        public void RotateHeld()
        {
            if (Tool != Tool.Item && Tool != Tool.Padding) return;
            var def = Catalog.Get(HeldKind);
            if (def.Has(Quirk.Facing)) HeldFacing = -HeldFacing;
            else if (def.Rotatable) HeldRotated = !HeldRotated;
            else { Feedback?.Invoke(def.Has(Quirk.Upright) ? "This side up!" : "Can't turn that"); G.Hud.Sfx("nope"); return; }
            EnsureGhost();
            ghost.SetFacing(HeldFacing);
            ghost.Kick(0.6f, new Vector2(1, 0));
            G.Hud.Sfx("rotate");
        }

        // ---- Update / input ---------------------------------------------------------------------

        void Update()
        {
            if (quirks != null && Active) quirks.Tick();
            if (!Active || G.Phase != Phase.Packing || G.Hud.Paused) return;
            if (G.FreshPhase) return;   // the key or click that opened the packing screen isn't a packing action
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null) return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var ray = G.Rig.Cam.ScreenPointToRay(mouse.position.ReadValue());
            Box.RayToCell(ray, out var cell);
            bool inBox = cell.x >= -0.3f && cell.x <= Pk.W + 0.3f && cell.y >= -0.3f && cell.y <= Pk.H + 0.6f;

            // keyboard
            if (kb != null)
            {
                bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                if (kb.zKey.wasPressedThisFrame) { if (kb.leftShiftKey.isPressed) Redo(); else Undo(); }
                if (kb.yKey.wasPressedThisFrame) Redo();
                if (kb.rKey.wasPressedThisFrame || (mouse.scroll.ReadValue().y != 0 && Tool != Tool.None)) RotateHeld();
                if (kb.digit1Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Paper);
                if (kb.digit2Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Bubble);
                if (kb.digit3Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Foam);
                if (kb.digit4Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Divider);
                if (kb.digit5Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Shelf);
                if (kb.digit6Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Strap);
                if ((kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) && ReadyToSeal && Tool == Tool.None) G.SealAndShip();
                _ = ctrl;
            }

            // hover over the shelf items
            TrayItem hoverTray = null;
            if (!overUi && Tool == Tool.None)
            {
                hoverTray = TrayUnder(ray);
            }

            hoverPiece = -1;
            if (!overUi && inBox) hoverPiece = Pk.PieceAt(Mathf.FloorToInt(cell.x), Mathf.FloorToInt(cell.y));
            int hl = Tool == Tool.None || Tool == Tool.Strap ? hoverPiece : -1;
            if (hl != lastHover)
            {
                if (lastHover >= 0 && lastHover < views.Count) views[lastHover].SetHover(false);
                if (hl >= 0 && hl < views.Count) { views[hl].SetHover(true); G.Hud.Sfx("hover", 0.25f); }
                lastHover = hl;
            }
            PieceKind? hk = hoverTray != null ? hoverTray.Kind : (hoverPiece >= 0 ? Pk.Pieces[hoverPiece].Kind : (Tool == Tool.Item || Tool == Tool.Padding ? HeldKind : (PieceKind?)null));
            if (hk != lastHovered) { lastHovered = hk; Hovered?.Invoke(hk); }
            for (int i = 0; i < tray.Count; i++)
            {
                var t = tray[i];
                float s = t == hoverTray ? 1.12f : 1f;
                t.View.transform.localScale = Vector3.Lerp(t.View.transform.localScale, Vector3.one * s, 1f - Mathf.Exp(-Time.deltaTime * 14f));
            }

            switch (Tool)
            {
                case Tool.None: UpdateNone(mouse, overUi, cell, inBox, hoverTray); break;
                case Tool.Item:
                case Tool.Padding: UpdateHolding(mouse, overUi, cell, inBox, kb); break;
                case Tool.Divider: UpdateDivider(mouse, overUi, cell, inBox); break;
                case Tool.Shelf: UpdateShelf(mouse, overUi, cell, inBox); break;
                case Tool.Strap: UpdateStrap(mouse, overUi, cell, inBox); break;
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame && Tool != Tool.None)
            {
                DropTool();
                G.Hud.ConsumeEscape();
            }
        }

        TrayItem TrayUnder(Ray ray)
        {
            TrayItem best = null;
            float bestD = float.MaxValue;
            foreach (var t in tray)
            {
                var b = t.View.WorldBounds();
                b.Expand(0.06f);
                if (b.IntersectRay(ray, out float d) && d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        void UpdateNone(Mouse mouse, bool overUi, Vector2 cell, bool inBox, TrayItem hoverTray)
        {
            if (overUi) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (hoverTray != null)
                {
                    HoldPiece(hoverTray.Kind, false, 1, false, Tool.Item);
                    tray.Remove(hoverTray);
                    // ghost starts where the item was
                    ghost.transform.position = hoverTray.View.transform.position;
                    ghostSmooth = ghost.transform.localPosition;
                    Destroy(hoverTray.View.gameObject);
                    G.Hud.Sfx("pick");
                    Changed?.Invoke();
                    return;
                }
                if (hoverPiece >= 0)
                {
                    PickUp(hoverPiece);
                    return;
                }
                // clicking a divider or shelf removes it
                if (inBox && TryRemoveStaticAt(cell)) return;
            }
            if (mouse.rightButton.wasPressedThisFrame && !overUi)
            {
                if (hoverPiece >= 0) RemovePiece(hoverPiece);
                else if (inBox) TryRemoveStaticAt(cell);
            }
        }

        bool TryRemoveStaticAt(Vector2 cell)
        {
            int line = Mathf.RoundToInt(cell.x);
            if (Mathf.Abs(cell.x - line) < 0.18f && Pk.Dividers.Contains(line))
            {
                Snapshot();
                Pk.Dividers.Remove(line);
                Box.BuildStatics(Pk);
                G.Hud.Sfx("remove");
                Changed?.Invoke();
                return true;
            }
            int row = Mathf.RoundToInt(cell.y);
            if (Mathf.Abs(cell.y - row) < 0.18f)
            {
                int col = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, Pk.W - 1);
                for (int i = 0; i < Pk.Shelves.Count; i++)
                {
                    var s = Pk.Shelves[i];
                    if (s.Row != row) continue;
                    Pk.ShelfSpan(s, out int x0, out int x1);
                    if (col < x0 || col >= x1) continue;
                    Snapshot();
                    Pk.Shelves.RemoveAt(i);
                    Pk.Settle();
                    SyncViewPositions();
                    Box.BuildStatics(Pk);
                    G.Hud.Sfx("remove");
                    Changed?.Invoke();
                    return true;
                }
            }
            return false;
        }

        void PickUp(int index)
        {
            Snapshot();
            var p = Pk.Pieces[index];
            var v = views[index];
            Pk.Pieces.RemoveAt(index);
            views.RemoveAt(index);
            var moved = Pk.Settle();
            SyncViewPositions();
            HoldPiece(p.Kind, p.Rotated, p.Facing, p.Strapped, p.Def.IsPadding ? Tool.Padding : Tool.Item);
            ghost.transform.position = v.transform.position;
            ghostSmooth = ghost.transform.localPosition;
            Destroy(v.gameObject);
            Box.Pieces.Remove(v);
            G.Hud.Sfx("pick");
            Changed?.Invoke();
        }

        void RemovePiece(int index)
        {
            Snapshot();
            var p = Pk.Pieces[index];
            var v = views[index];
            Pk.Pieces.RemoveAt(index);
            views.RemoveAt(index);
            Box.Pieces.Remove(v);
            Fx.Poof(v.transform.position, Palette.ItemColor(p.Kind), 0.6f);
            Destroy(v.gameObject);
            Pk.Settle();
            SyncViewPositions();
            if (!p.Def.IsPadding) RebuildTray();
            G.Hud.Sfx("remove");
            Changed?.Invoke();
        }

        void UpdateHolding(Mouse mouse, bool overUi, Vector2 cell, bool inBox, Keyboard kb)
        {
            EnsureGhost();
            var def = Catalog.Get(HeldKind);
            int w = HeldRotated ? def.H : def.W, h = HeldRotated ? def.W : def.H;
            // cursor targets the piece's centre
            var p = new Placement(HeldKind, Mathf.FloorToInt(cell.x - w * 0.5f + 0.5f), Mathf.FloorToInt(cell.y - h * 0.5f + 0.5f), HeldRotated, HeldFacing, HeldStrapped);
            bool valid = inBox && !overUi && Pk.FindDropPosition(ref p);
            if (valid && !CanAfford(p)) valid = false;
            ghostPlacement = p;
            ghostValid = valid;

            Vector3 target;
            if (inBox && !overUi && p.X >= 0)
            {
                target = Box.CellToLocal(p.X + w * 0.5f, p.Y + h * 0.5f) + new Vector3(0, 0, -0.03f);
            }
            else
            {
                // float under the cursor in front of the box
                var cl = Box.CellToLocal(cell.x, cell.y);
                target = new Vector3(cl.x, cl.y, -0.12f);
            }
            if (ghostSmooth == Vector3.zero) ghostSmooth = target;
            ghostSmooth = Vector3.Lerp(ghostSmooth, target, 1f - Mathf.Exp(-Time.deltaTime * 22f));
            ghost.transform.localPosition = ghostSmooth + new Vector3(0, Mathf.Sin(Time.time * 5f) * 0.004f, 0);
            ghost.SetGhost(true, valid ? new Color(0.85f, 1f, 0.9f, 0.55f) : new Color(1f, 0.45f, 0.4f, 0.45f));

            if (overUi) return;

            if (Tool == Tool.Padding)
            {
                // paint: place on press and while dragging over new cells; right-drag erases padding
                if (mouse.leftButton.wasPressedThisFrame) { painting = true; lastPaintCell = new Vector2Int(-99, -99); }
                if (!mouse.leftButton.isPressed) painting = false;
                if (mouse.rightButton.wasPressedThisFrame) { erasing = true; lastPaintCell = new Vector2Int(-99, -99); }   // erase the cell just painted, too
                if (!mouse.rightButton.isPressed) erasing = false;
                var c = new Vector2Int(Mathf.FloorToInt(cell.x), Mathf.FloorToInt(cell.y));
                if (painting && valid && c != lastPaintCell)
                {
                    lastPaintCell = c;
                    if (Remaining(SlotOf(HeldKind)) > 0) Place(p, false);
                    else { painting = false; Feedback?.Invoke("Out of " + def.Name); G.Hud.Sfx("nope"); DropTool(); }
                }
                else if (painting && !valid && c != lastPaintCell)
                {
                    lastPaintCell = c;
                }
                if (erasing && c != lastPaintCell)
                {
                    lastPaintCell = c;
                    int i = Pk.PieceAt(c.x, c.y);
                    if (i >= 0 && Pk.Pieces[i].Def.IsPadding) RemovePiece(i);
                }
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (valid) { Place(p, true); }
                else if (inBox)
                {
                    ghost.Kick(0.8f, new Vector2(1, 0));
                    Feedback?.Invoke(WhyInvalid(p));
                    G.Hud.Sfx("nope");
                }
                else
                {
                    DropTool();
                }
            }
            if (mouse.rightButton.wasPressedThisFrame) RotateHeld();
        }

        string WhyInvalid(Placement p)
        {
            if (p.X < 0 || p.X + p.W > Pk.W) return "Doesn't fit there";
            if (!Pk.AreaFree(p.X, Mathf.Clamp(p.Y, 0, Pk.H - p.H), p.W, p.H)) return "No room";
            return "Needs something to rest on";
        }

        bool CanAfford(Placement p)
        {
            if (!p.Def.IsPadding) return true;
            return Remaining(SlotOf(p.Kind)) > 0;
        }

        static MaterialSlot SlotOf(PieceKind k) => k == PieceKind.Paper ? MaterialSlot.Paper : (k == PieceKind.Bubble ? MaterialSlot.Bubble : MaterialSlot.Foam);

        void Place(Placement p, bool endTool)
        {
            Snapshot();
            Pk.Pieces.Add(p);
            var v = PieceView.Create(p.Kind, Box.Contents, p.Rotated, p.Facing);
            v.transform.localPosition = ghost != null ? ghost.transform.localPosition : Box.CellToLocal(p.X + p.W * 0.5f, p.Y + p.H * 0.5f);
            v.MoveTo(new Vector2(p.X + p.W * 0.5f, p.Y + p.H * 0.5f));
            v.SetStrapped(p.Strapped);
            v.PlayDrop();
            views.Add(v);
            Box.Pieces.Add(v);
            Fx.Dust(Box.CellToWorld(p.X + p.W * 0.5f, p.Y), p.W);
            G.Hud.Sfx(p.Def.IsPadding ? "pad_" + p.Def.Id : "place", Mathf.Clamp(p.Def.Mass, 0.2f, 3f));
            G.Rig.AddTrauma(Mathf.Clamp01(p.Def.Mass * 0.05f));
            if (endTool)
            {
                Tool = Tool.None;
                if (ghost != null) { Destroy(ghost.gameObject); ghost = null; }
            }
            Changed?.Invoke();
        }

        void UpdateDivider(Mouse mouse, bool overUi, Vector2 cell, bool inBox)
        {
            int line = Mathf.Clamp(Mathf.RoundToInt(cell.x), 1, Pk.W - 1);
            bool existing = Pk.Dividers.Contains(line);
            bool valid = inBox && !overUi && (existing || (Pk.CanPlaceDivider(line) && Remaining(MaterialSlot.Divider) > 0));
            ShowStaticGhost(Tool.Divider, () => Box.MakeDivider(line, Box.Contents, true), new Vector3(line * BoxView.Cell, Box.InteriorHeight * 0.5f, -0.01f), valid, existing, inBox && !overUi);
            if (overUi || !inBox) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (existing)
                {
                    Snapshot();
                    Pk.Dividers.Remove(line);
                    Box.BuildStatics(Pk);
                    G.Hud.Sfx("remove");
                    Changed?.Invoke();
                }
                else if (valid)
                {
                    Snapshot();
                    Pk.Dividers.Add(line);
                    Box.BuildStatics(Pk);
                    G.Hud.Sfx("divider");
                    Fx.Dust(Box.CellToWorld(line, 0), 1);
                    if (Remaining(MaterialSlot.Divider) <= 0) DropTool();
                    Changed?.Invoke();
                }
                else
                {
                    Feedback?.Invoke(Remaining(MaterialSlot.Divider) <= 0 ? "No dividers left" : "Something is in the way");
                    G.Hud.Sfx("nope");
                }
            }
            if (mouse.rightButton.wasPressedThisFrame) DropTool();
        }

        void UpdateShelf(Mouse mouse, bool overUi, Vector2 cell, bool inBox)
        {
            int row = Mathf.Clamp(Mathf.RoundToInt(cell.y), 1, Pk.H - 1);
            int col = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, Pk.W - 1);
            Pk.CompartmentOf(col, out int x0, out int x1);
            int existingIdx = -1;
            for (int i = 0; i < Pk.Shelves.Count; i++)
            {
                Pk.ShelfSpan(Pk.Shelves[i], out int a, out int b);
                if (Pk.Shelves[i].Row == row && a == x0 && b == x1) existingIdx = i;
            }
            bool existing = existingIdx >= 0;
            bool valid = inBox && !overUi && (existing || (Pk.CanPlaceShelf(row, col) && Remaining(MaterialSlot.Shelf) > 0));
            float a0 = x0 * BoxView.Cell, b0 = x1 * BoxView.Cell;
            ShowStaticGhost(Tool.Shelf, () => Box.MakeShelf(row, x0, x1, Box.Contents, true), new Vector3((a0 + b0) * 0.5f, row * BoxView.Cell, -0.01f), valid, existing, inBox && !overUi, b0 - a0);
            if (overUi || !inBox) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (existing)
                {
                    Snapshot();
                    Pk.Shelves.RemoveAt(existingIdx);
                    Pk.Settle();
                    SyncViewPositions();
                    Box.BuildStatics(Pk);
                    G.Hud.Sfx("remove");
                    Changed?.Invoke();
                }
                else if (valid)
                {
                    Snapshot();
                    Pk.Shelves.Add(new ShelfSpec(row, col));
                    Box.BuildStatics(Pk);
                    G.Hud.Sfx("shelf");
                    if (Remaining(MaterialSlot.Shelf) <= 0) DropTool();
                    Changed?.Invoke();
                }
                else
                {
                    Feedback?.Invoke(Remaining(MaterialSlot.Shelf) <= 0 ? "No shelves left" : "Something is in the way");
                    G.Hud.Sfx("nope");
                }
            }
            if (mouse.rightButton.wasPressedThisFrame) DropTool();
        }

        void ShowStaticGhost(Tool t, Func<GameObject> make, Vector3 localPos, bool valid, bool existing, bool visible, float width = 0f)
        {
            if (staticGhost == null || staticGhostTool != t)
            {
                if (staticGhost != null) Destroy(staticGhost);
                staticGhost = make();
                staticGhostTool = t;
            }
            staticGhost.SetActive(visible);
            staticGhost.transform.localPosition = localPos;
            if (t == Tool.Shelf && width > 0) staticGhost.transform.localScale = new Vector3(width, 1, 1);
            BoxView.TintGhost(staticGhost, existing ? new Color(1f, 0.55f, 0.4f, 0.55f) : (valid ? new Color(0.85f, 1f, 0.9f, 0.6f) : new Color(1f, 0.4f, 0.35f, 0.4f)));
        }

        void UpdateStrap(Mouse mouse, bool overUi, Vector2 cell, bool inBox)
        {
            if (overUi) return;
            if (mouse.leftButton.wasPressedThisFrame && hoverPiece >= 0)
            {
                var p = Pk.Pieces[hoverPiece];
                if (p.Def.IsPadding) { Feedback?.Invoke("Straps go on items"); G.Hud.Sfx("nope"); return; }
                if (!p.Strapped && Remaining(MaterialSlot.Strap) <= 0) { Feedback?.Invoke("No straps left"); G.Hud.Sfx("nope"); return; }
                Snapshot();
                p.Strapped = !p.Strapped;
                Pk.Pieces[hoverPiece] = p;
                views[hoverPiece].SetStrapped(p.Strapped);
                views[hoverPiece].Kick(0.7f, Vector2.up);
                G.Hud.Sfx(p.Strapped ? "strap" : "remove");
                if (Remaining(MaterialSlot.Strap) <= 0 && p.Strapped) DropTool();
                Changed?.Invoke();
            }
            if (mouse.rightButton.wasPressedThisFrame) DropTool();
        }

        public int HoverPiece => hoverPiece;

        /// <summary>World positions of the items still on the shelf (gamepad cursor targets).</summary>
        public IEnumerable<Vector3> TrayPositions
        {
            get { foreach (var t in tray) if (t.View != null) yield return t.View.transform.position; }
        }

        // ---- Scripted placement (autopilot, tutorials) -------------------------------------------

        /// <summary>Places a piece exactly as given, through the normal placement path. False if illegal.</summary>
        public bool DebugPlace(Placement p)
        {
            if (!Active) return false;
            if (!p.Def.IsPadding)
            {
                var remaining = RemainingItems();
                if (!remaining.Contains(p.Kind)) return false;
            }
            var probe = p;
            probe.Strapped = false;
            if (!Pk.CanPlace(probe)) return false;
            if (p.Def.IsPadding && Remaining(SlotOf(p.Kind)) <= 0) return false;
            DropTool();
            HoldPiece(p.Kind, p.Rotated, p.Facing, p.Strapped, p.Def.IsPadding ? Tool.Padding : Tool.Item);
            ghost.transform.localPosition = Box.CellToLocal(p.X + p.W * 0.5f, p.Y + p.H * 0.5f + 1.5f);
            Place(p, true);
            if (!p.Def.IsPadding) RebuildTray();
            return true;
        }

        public void DebugAddDivider(int line)
        {
            if (!Pk.CanPlaceDivider(line)) return;
            Snapshot();
            Pk.Dividers.Add(line);
            Box.BuildStatics(Pk);
            G.Hud.Sfx("divider");
            Changed?.Invoke();
        }

        public void DebugAddShelf(ShelfSpec s)
        {
            if (!Pk.CanPlaceShelf(s.Row, s.Col)) return;
            Snapshot();
            Pk.Shelves.Add(s);
            Box.BuildStatics(Pk);
            G.Hud.Sfx("shelf");
            Changed?.Invoke();
        }
    }
}
