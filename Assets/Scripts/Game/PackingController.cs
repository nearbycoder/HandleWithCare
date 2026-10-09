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
        /// <summary>What the item card is for, so it can say what you can do with it.</summary>
        public enum CardUse { None, Shelf, Placed, HoldItem, HoldPadding, Strap }
        public CardUse CardFor { get; private set; }
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
        CardUse lastCardFor;
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

        public void Begin(LevelDef lv, Packing pk, Recording last, bool keepHistory = false)
        {
            // back from watching the last trip: the same box, so undo and redo still apply
            bool same = keepHistory && Level == lv && Pk == pk;
            Level = lv;
            Pk = pk;
            Box = G.Station.Box;
            lastRun = last;
            Active = true;
            Tool = Tool.None;
            if (!same) { undo.Clear(); redo.Clear(); }
            RebuildViews(true);
            RebuildTray();
            if (trails == null) trails = new GameObject("Trails").AddComponent<TrailOverlay>();
            trails.Show(Box, last);
            if (quirks == null) quirks = new GameObject("Quirks").AddComponent<QuirkOverlay>();
            Changed -= RefreshQuirks;
            Changed += RefreshQuirks;
            Changed -= UpdateHintMatches;
            Changed += UpdateHintMatches;
            Changed?.Invoke();
        }

        /// <summary>The last trip arrived after the bench opened (simulated again): show its trails.</summary>
        public Recording LastRunShown => lastRun;
        public int UndoDepth => undo.Count;
        public int RedoDepth => redo.Count;
        public void ShowLastRun(Recording last)
        {
            lastRun = last;
            if (Active && trails != null) trails.Show(Box, last);
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
            lastHovered = null; CardFor = lastCardFor = CardUse.None;
            Hovered?.Invoke(null);
        }

        // ---- Ask Mabel: ghosts of the reference packing --------------------------------------------

        public readonly List<Placement> HintPieces = new List<Placement>();
        public readonly List<int> HintDividers = new List<int>();
        public readonly List<ShelfSpec> HintShelves = new List<ShelfSpec>();
        readonly List<GameObject> hintObjects = new List<GameObject>();
        static readonly Color HintTint = new Color(1f, 0.86f, 0.3f, 0.5f);
        static readonly Color HintStaticTint = new Color(1f, 0.8f, 0.2f, 0.85f);   // dividers and shelves are thin: stronger
        static readonly Color HintMatchedTint = new Color(0.45f, 0.95f, 0.55f, 0.16f);   // already in the box: a faint green
        static readonly Color HintMatchedStaticTint = new Color(0.45f, 0.95f, 0.55f, 0.3f);
        static readonly Color HintBlockedTint = new Color(1f, 0.33f, 0.28f, 0.55f);      // something else is in its spot
        public static readonly Color ExtraTint = new Color(0.95f, 0.25f, 0.2f);           // yours, not in her packing

        // the ghosts drawn for the hints, and how each one stands against the box now
        readonly List<(PieceView view, Placement p)> hintGhosts = new List<(PieceView, Placement)>();
        readonly List<(GameObject go, int line)> hintDividerGhosts = new List<(GameObject, int)>();
        readonly List<(GameObject go, ShelfSpec s)> hintShelfGhosts = new List<(GameObject, ShelfSpec)>();
        readonly Dictionary<object, int> hintShown = new Dictionary<object, int>();
        int hintStage; PieceKind hintFocus; bool hintBudget, hintVisible;
        /// <summary>Hint ghosts (pieces, dividers, shelves) the box already matches, of how many; ghosts whose
        /// spot is taken; and your pieces that aren't in her packing (indices into Pk.Pieces).</summary>
        public int HintsInPlace { get; private set; }
        public int HintsTotal { get; private set; }
        public int HintsBlocked { get; private set; }
        public readonly List<int> HintExtras = new List<int>();
        public event Action HintsMatched;
        /// <summary>The badges drawn now: checks on ghosts in place, crosses on ghosts in the way, crosses on your
        /// pieces that aren't in her packing (so the states read without their colours).</summary>
        public int BadgeChecks { get; private set; }
        public int BadgeBlocked { get; private set; }
        public int BadgeExtras { get; private set; }
        readonly List<GameObject> badges = new List<GameObject>();

        void ClearBadges()
        {
            foreach (var b in badges) if (b != null) Destroy(b);
            badges.Clear();
            BadgeChecks = BadgeBlocked = BadgeExtras = 0;
        }

        // in front of the box, inset from a corner of the piece's cells
        Vector3 BadgeAt(float x, float y) => new Vector3(x * BoxView.Cell, y * BoxView.Cell, -BoxView.Depth * 0.5f - 0.03f);
        const float BadgeInset = 0.19f;   // cells

        void Badge(bool check, Vector3 at)
        {
            badges.Add(MarkBadge.Make(Box.Contents, check, at));
        }

        void ClearHints()
        {
            foreach (var o in hintObjects) if (o != null) Destroy(o);
            hintObjects.Clear();
            HintPieces.Clear(); HintDividers.Clear(); HintShelves.Clear();
            hintGhosts.Clear(); hintDividerGhosts.Clear(); hintShelfGhosts.Clear(); hintShown.Clear();
            hintStage = 0;
            ClearBadges();
            HintsInPlace = HintsTotal = HintsBlocked = 0;
            HintExtras.Clear();
            foreach (var v in views) if (v != null) v.SetExtra(false, ExtraTint);
        }

        /// <summary>
        /// Each hint ghost against the box as it is now: matched ones fade to a faint green, ones whose spot is
        /// taken turn red, and (when the stage shows everything of its kind) your pieces that aren't in her
        /// packing are tinted as extra. Runs on every change, undo and redo included.
        /// </summary>
        void UpdateHintMatches()
        {
            HintsInPlace = HintsBlocked = 0;
            HintExtras.Clear();
            HintsTotal = HintPieces.Count + HintDividers.Count + HintShelves.Count;
            if (!Active || hintStage <= 0 || Pk == null) { HintsMatched?.Invoke(); return; }
            foreach (var p in HintPieces)
            {
                var m = Hints.MatchOf(Pk, p);
                if (m == Hints.Match.InPlace) HintsInPlace++;
                else if (m == Hints.Match.Blocked) HintsBlocked++;
            }
            var src = Hints.Source(Level);
            foreach (int d in HintDividers) if (Hints.HasDivider(Pk, d)) HintsInPlace++;
            foreach (var sh in HintShelves) if (Hints.HasShelf(Pk, src, sh)) HintsInPlace++;
            HintExtras.AddRange(Hints.Extras(Pk, Level, hintStage, hintFocus, hintBudget));
            if (hintVisible)
            {
                foreach (var (view, p) in hintGhosts)
                {
                    var m = Hints.MatchOf(Pk, p);
                    if (Shown(view, (int)m)) continue;
                    view.SetGhost(true, m == Hints.Match.InPlace ? HintMatchedTint : m == Hints.Match.Blocked ? HintBlockedTint : HintTint);
                }
                foreach (var (go, line) in hintDividerGhosts)
                {
                    bool on = Hints.HasDivider(Pk, line);
                    if (!Shown(go, on ? 1 : 0)) BoxView.TintGhost(go, on ? HintMatchedStaticTint : HintStaticTint);
                }
                foreach (var (go, sh) in hintShelfGhosts)
                {
                    bool on = Hints.HasShelf(Pk, src, sh);
                    if (!Shown(go, on ? 1 : 0)) BoxView.TintGhost(go, on ? HintMatchedStaticTint : HintStaticTint);
                }
            }
            for (int i = 0; i < views.Count; i++)
                if (views[i] != null) views[i].SetExtra(hintVisible && HintExtras.Contains(i), ExtraTint);
            // the same states as marks: a ghost's in its top-right corner, your piece's in its top-left
            ClearBadges();
            if (hintVisible)
            {
                foreach (var (view, p) in hintGhosts)
                {
                    var m = Hints.MatchOf(Pk, p);
                    if (m == Hints.Match.Open) continue;
                    Badge(m == Hints.Match.InPlace, BadgeAt(p.X + p.W - BadgeInset, p.Y + p.H - BadgeInset));
                    if (m == Hints.Match.InPlace) BadgeChecks++; else BadgeBlocked++;
                }
                foreach (var (go, line) in hintDividerGhosts)
                    if (Hints.HasDivider(Pk, line)) { Badge(true, BadgeAt(line, Pk.H - BadgeInset)); BadgeChecks++; }
                foreach (var (go, sh) in hintShelfGhosts)
                    if (Hints.HasShelf(Pk, src, sh)) { src.ShelfSpan(sh, out int x0, out int x1); Badge(true, BadgeAt((x0 + x1) * 0.5f, sh.Row)); BadgeChecks++; }
                foreach (int i in HintExtras)
                {
                    if (i < 0 || i >= Pk.Pieces.Count) continue;
                    var p = Pk.Pieces[i];
                    Badge(false, BadgeAt(p.X + BadgeInset, p.Y + p.H - BadgeInset));
                    BadgeExtras++;
                }
            }
            HintsMatched?.Invoke();
        }

        /// <summary>True when a ghost already shows this state (so it isn't re-tinted every change).</summary>
        bool Shown(object ghost, int state)
        {
            if (hintShown.TryGetValue(ghost, out int was) && was == state) return true;
            hintShown[ghost] = state;
            return false;
        }

        /// <summary>For the self-tests: how the ghost of a piece looks now (Open, InPlace or Blocked), or -1.</summary>
        public int HintGhostState(Placement p)
        {
            foreach (var (view, gp) in hintGhosts)
                if (Hints.Same(gp, p) && hintShown.TryGetValue(view, out int st)) return st;
            return -1;
        }
        /// <summary>For the self-tests: the box's pieces drawn as extra now.</summary>
        public List<int> PiecesShownExtra()
        {
            var list = new List<int>();
            for (int i = 0; i < views.Count; i++) if (views[i] != null && views[i].Extra) list.Add(i);
            return list;
        }

        /// <summary>Shows the hint ghosts for a stage (0 or hidden = none).</summary>
        public void ShowHints(int stage, PieceKind focus, bool visible, bool budget = false)
        {
            ClearHints();
            if (!Active || stage <= 0) { UpdateHintMatches(); return; }
            hintStage = stage; hintFocus = focus; hintBudget = budget; hintVisible = visible;
            HintPieces.AddRange(Hints.Pieces(Level, stage, focus, budget));
            var src = Hints.Source(Level);
            if (Hints.ShowsStatics(stage)) { HintDividers.AddRange(src.Dividers); HintShelves.AddRange(src.Shelves); }
            if (!visible) { UpdateHintMatches(); return; }
            foreach (var p in HintPieces)
            {
                var v = PieceView.Create(p.Kind, Box.Contents, p.Rotated, p.Facing);
                v.SetCellRect(p.X, p.Y, p.W, p.H);
                v.SetStrapped(p.Strapped);
                v.SetGhost(true, HintTint);
                v.name = "hint_" + p.Kind;
                hintObjects.Add(v.gameObject);
                hintGhosts.Add((v, p));
                hintShown[v] = (int)Hints.Match.Open;
            }
            foreach (int d in HintDividers)
            {
                var go = Box.MakeDivider(d, Box.Contents, true);
                BoxView.TintGhost(go, HintStaticTint);
                hintObjects.Add(go);
                hintDividerGhosts.Add((go, d));
                hintShown[go] = 0;
            }
            foreach (var sh in HintShelves)
            {
                src.ShelfSpan(sh, out int x0, out int x1);
                var go = Box.MakeShelf(sh.Row, x0, x1, Box.Contents, true);
                BoxView.TintGhost(go, HintStaticTint);
                hintObjects.Add(go);
                hintShelfGhosts.Add((go, sh));
                hintShown[go] = 0;
            }
            UpdateHintMatches();
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

        /// <summary>Puts a whole packing in the box (MY BEST). Undoable.</summary>
        public void LoadPacking(Packing pk)
        {
            if (pk == null) return;
            string s = SaveData.Serialize(pk);
            if (s == SaveData.Serialize(Pk)) return;
            Snapshot();
            Restore(s);
            G.Hud.Sfx("clear");
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
                // letters by their label (Z is undo on QWERTZ and AZERTY too); Ctrl+Z and Shift+Z work as well
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (Shortcuts.Pressed(kb, 'z')) { if (shift) Redo(); else Undo(); }
                if (Shortcuts.Pressed(kb, 'y')) Redo();
                if (Shortcuts.Pressed(kb, 'r') || (mouse.scroll.ReadValue().y != 0 && Tool != Tool.None)) RotateHeld();
                if (kb.digit1Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Paper);
                if (kb.digit2Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Bubble);
                if (kb.digit3Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Foam);
                if (kb.digit4Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Divider);
                if (kb.digit5Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Shelf);
                if (kb.digit6Key.wasPressedThisFrame) SelectMaterial(MaterialSlot.Strap);
                if ((kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) && ReadyToSeal && Tool == Tool.None) G.SealAndShip();
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
            // a red cross from the last trip under the pointer: its item's card, for that very item
            int trouble = !overUi && inBox && Tool == Tool.None && hoverTray == null ? TroubleAt(cell) : -1;
            // with something in hand, the card is about it (and says how to use it), wherever it points
            bool holding = Tool == Tool.Item || Tool == Tool.Padding;
            PieceKind? hk = trouble >= 0 ? lastRun.Bodies[trouble].Kind
                          : holding ? HeldKind
                          : hoverTray != null ? hoverTray.Kind : (hoverPiece >= 0 ? Pk.Pieces[hoverPiece].Kind : (PieceKind?)null);
            var use = trouble >= 0 ? CardUse.None
                    : Tool == Tool.Item ? CardUse.HoldItem : Tool == Tool.Padding ? CardUse.HoldPadding
                    : hoverTray != null ? CardUse.Shelf
                    : hoverPiece >= 0 ? (Tool == Tool.None ? CardUse.Placed : Tool == Tool.Strap && !Pk.Pieces[hoverPiece].Def.IsPadding ? CardUse.Strap : CardUse.None)
                    : CardUse.None;
            if (hk != lastHovered || trouble != HoverTroubleBody || use != lastCardFor)
            {
                lastHovered = hk; HoverTroubleBody = trouble; lastCardFor = CardFor = use;
                Hovered?.Invoke(hk);
            }
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

        /// <summary>The recorded body whose mark from the last trip (a red cross, or an amber near miss) is under
        /// this cell point, or -1.</summary>
        public int HoverTroubleBody { get; private set; } = -1;
        const float TroubleReach = 0.4f;   // cells from the cross's centre

        int TroubleAt(Vector2 cell)
        {
            if (lastRun == null || lastRun.Level != Level) return -1;
            int best = -1; float bestD = TroubleReach * TroubleReach;
            foreach (var tr in lastRun.Troubles)
            {
                if (tr.Body < 0) continue;
                float dx = cell.x - tr.Where.x, dy = cell.y - tr.Where.y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = tr.Body; }
            }
            return best;
        }

        /// <summary>The last trip's trails and crosses of an item (or of one recorded body) stand out.</summary>
        public void HighlightTrip(PieceKind? kind, int body)
        {
            if (trails != null) trails.Highlight(lastRun, kind, body);
        }
        public int TripMarksHighlighted => trails != null ? trails.HighlightedMarks() : 0;
        public int NearMissMarksShown => trails != null ? trails.NearMarks : 0;

        /// <summary>
        /// For the browser check (Tools/check-pages.mjs, through unityInstance.SendMessage("Packing", "WebReportBench")):
        /// the items of Mabel's packing, lowest first, each as where it lies in the tray and where it goes in the box,
        /// in 1920x1080 UI units, so the check can pack the box with real clicks.
        /// </summary>
        void WebReportBench()
        {
            if (!Active || Level == null) { WebPlatform.Log("bench: not packing"); return; }
            var cam = G.Rig.Cam;
            Vector2 ToUi(Vector3 world)
            {
                var s = cam.WorldToScreenPoint(world);
                return new Vector2(s.x * 1920f / Screen.width, (Screen.height - s.y) * 1080f / Screen.height);
            }
            var pieces = new List<Placement>();
            foreach (var p in Hints.Source(Level).Pieces) if (!p.Def.IsPadding) pieces.Add(p);
            pieces.Sort((a, b) => a.Y.CompareTo(b.Y));
            var used = new List<TrayItem>();
            var sb = new System.Text.StringBuilder("bench: delivery " + Level.Number + " |");
            foreach (var p in pieces)
            {
                TrayItem t = null;
                foreach (var c in tray) if (c.Kind == p.Kind && !used.Contains(c)) { t = c; break; }
                if (t == null) continue;
                used.Add(t);
                int w = p.Rotated ? p.Def.H : p.Def.W, h = p.Rotated ? p.Def.W : p.Def.H;
                var from = ToUi(t.View.WorldBounds().center);
                var to = ToUi(Box.CellToWorld(p.X + w * 0.5f, p.Y + h * 0.5f));
                sb.Append($" {p.Kind} {from.x:0},{from.y:0} > {to.x:0},{to.y:0}{(p.Rotated ? " rotated" : "")};");
            }
            WebPlatform.Log(sb.ToString());
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
