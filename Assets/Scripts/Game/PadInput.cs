using System;
using System.Collections.Generic;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace HWC.Gameplay
{
    /// <summary>
    /// Gamepad (and Steam Deck) support. The pad drives a virtual mouse: the left stick moves a
    /// software cursor, the d-pad snaps it to the next target in that direction (box cells, items on
    /// the shelf, buttons), A is the left button (hold to paint), B the right one. Because the
    /// packing code and the UI read the mouse, every mouse action works unchanged; the face buttons
    /// add shortcuts on top. The OS cursor hides while the pad is in use.
    /// </summary>
    public sealed class PadInput : MonoBehaviour
    {
        public static PadInput I;
        /// <summary>True while the gamepad is the device in use (prompts show pad buttons).</summary>
        public bool Active { get; private set; }
        public event Action<bool> ActiveChanged;
        public Vector2 CursorPosition => pos;
        /// <summary>The gamepad-only self-test: the desk's real mouse and keyboard never take over.</summary>
        public bool PadOnly;

        Mouse padMouse;
        Vector2 pos, lastPos;
        RectTransform canvasRt, cursor;
        Image cursorRing;
        string lastScreen;
        float pressPulse;
        bool holdA;   // the press that woke the cursor isn't a click
        float snapAt = -1;
        Game G => Game.I;

        const float Deadzone = 0.18f;

        public static PadInput Create(Transform parent)
        {
            var go = new GameObject("PadInput");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<PadInput>();
            I.BuildCursor(go.transform);
            return I;
        }

        void BuildCursor(Transform parent)
        {
            var canvas = Ui.CreateCanvas("PadCursor", 200, parent);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;   // never blocks the pointer it draws
            canvasRt = (RectTransform)canvas.transform;
            var ring = Ui.Panel(canvasRt, "cursor", Palette.Cream, Ui.Circle);
            ring.raycastTarget = false;
            cursor = ring.rectTransform;
            cursor.sizeDelta = new Vector2(46, 46);
            cursor.anchorMin = cursor.anchorMax = new Vector2(0.5f, 0.5f);
            Ui.Shadow(ring, 4, 0.35f);
            var inner = Ui.Panel(cursor, "inner", Palette.PostalRed, Ui.Circle);
            inner.raycastTarget = false;
            inner.rectTransform.Stretch(9, 9, 9, 9);
            var dot = Ui.Panel(cursor, "dot", Palette.Cream, Ui.Circle);
            dot.raycastTarget = false;
            dot.rectTransform.Stretch(18, 18, 18, 18);
            cursorRing = ring;
            cursor.gameObject.SetActive(false);
        }

        // ---- device switching ----------------------------------------------------------------------

        static bool PadUsed(Gamepad pad)
        {
            if (pad.leftStick.ReadValue().magnitude > 0.35f || pad.rightStick.ReadValue().magnitude > 0.35f) return true;
            foreach (var c in pad.allControls)
                if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame) return true;
            return false;
        }

        bool MouseOrKeyboardUsed()
        {
            bool used = false;
            foreach (var d in InputSystem.devices)
            {
                if (d is Mouse m && m != padMouse)
                {
                    if (m.delta.ReadValue().sqrMagnitude > 9f || m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame) used = true;
                }
            }
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) used = true;
            if (used && PadOnly)
            {
                if (Time.unscaledTime > ignoredLogAt) { ignoredLogAt = Time.unscaledTime + 1f; Debug.Log("[Pad] ignored the real mouse or keyboard (gamepad-only test)"); }
                return false;
            }
            return used;
        }

        /// <summary>How long after a screen changes the cursor moves to its main button.</summary>
        public const float SnapDelay = 0.35f;

        float ignoredLogAt;

        void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on;
            if (on)
            {
                if (padMouse == null) padMouse = InputSystem.AddDevice<Mouse>("PadMouse");
                var real = RealMouse();
                pos = real != null ? real.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                lastScreen = null;   // snap to the screen's default target
                holdA = true;
            }
            else if (padMouse != null)
            {
                InputSystem.QueueStateEvent(padMouse, new MouseState { position = pos });   // let go of any held button
            }
            Debug.Log(on ? "[Pad] the gamepad took over" : "[Pad] the mouse or keyboard took over");
            Cursor.visible = !on;
            cursor.gameObject.SetActive(on);
            ActiveChanged?.Invoke(on);
        }

        Mouse RealMouse()
        {
            foreach (var d in InputSystem.devices) if (d is Mouse m && m != padMouse) return m;
            return null;
        }

        // ---- per frame -------------------------------------------------------------------------------

        void Update()
        {
            var pad = Gamepad.current;
            if (pad != null && PadUsed(pad)) SetActive(true);
            else if (Active && MouseOrKeyboardUsed()) SetActive(false);
            if (!Active || pad == null || G == null) return;
            float dt = Time.unscaledDeltaTime;

            // the screen changed (results, pause, a menu): start on its main button
            // (after the screen's own pop-in animation, so the button is where it will stay)
            string screen = ScreenKey();
            if (screen != lastScreen) { lastScreen = screen; snapAt = Time.unscaledTime + SnapDelay; }
            if (snapAt > 0 && Time.unscaledTime >= snapAt) { snapAt = -1; SnapToDefault(); }

            // left stick: free cursor, faster at full tilt
            var stick = pad.leftStick.ReadValue();
            float mag = stick.magnitude;
            if (mag > Deadzone)
            {
                float k = Mathf.Pow((mag - Deadzone) / (1f - Deadzone), 1.6f);
                pos += stick / mag * k * 1150f * (Screen.height / 1080f) * dt;
            }
            // d-pad: the next target in that direction
            var dpad = pad.dpad;
            if (dpad.up.wasPressedThisFrame) Snap(Vector2.up);
            if (dpad.down.wasPressedThisFrame) Snap(Vector2.down);
            if (dpad.left.wasPressedThisFrame) Snap(Vector2.left);
            if (dpad.right.wasPressedThisFrame) Snap(Vector2.right);
            pos.x = Mathf.Clamp(pos.x, 0, Screen.width - 1);
            pos.y = Mathf.Clamp(pos.y, 0, Screen.height - 1);

            if (holdA && !pad.buttonSouth.isPressed) holdA = false;
            bool left = pad.buttonSouth.isPressed && !holdA, right = false;
            Shortcuts(pad, ref left, ref right);

            var st = new MouseState { position = pos, delta = pos - lastPos };
            if (left) st = st.WithButton(MouseButton.Left, true);
            if (right) st = st.WithButton(MouseButton.Right, true);
            InputSystem.QueueStateEvent(padMouse, st);
            lastPos = pos;

            // the cursor
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, pos, null, out var lp);
            cursor.anchoredPosition = lp;
            if (pad.buttonSouth.wasPressedThisFrame) pressPulse = 1f;
            pressPulse = Mathf.Max(0, pressPulse - dt * 6f);
            cursor.localScale = Vector3.one * (left ? 0.82f : 1f + pressPulse * 0.1f);
            cursor.SetAsLastSibling();
        }

        /// <summary>Face buttons and shoulders. A is the left mouse button unless the context uses it.</summary>
        void Shortcuts(Gamepad pad, ref bool left, ref bool right)
        {
            var hud = G.Hud;
            bool a = pad.buttonSouth.wasPressedThisFrame, b = pad.buttonEast.wasPressedThisFrame;
            bool x = pad.buttonWest.wasPressedThisFrame, y = pad.buttonNorth.wasPressedThisFrame;
            bool start = pad.startButton.wasPressedThisFrame;
            bool menu = G.Menus.Open;

            if (menu)                       // a menu can sit on top of the pause menu (Settings, Delivery Log)
            {
                if (b) G.Menus.Back();
                return;
            }
            if (hud.Paused)
            {
                if (b || start) hud.SetPaused(false);
                return;
            }
            switch (G.Phase)
            {
                case Phase.Packing:
                {
                    var pc = G.Packing;
                    if (start)
                    {
                        if (pc.Tool != Tool.None) pc.DropTool();
                        else hud.SetPaused(true);
                    }
                    // B is the right mouse button (erase padding, put a piece back, put a divider tool down),
                    // except with an item in hand: X turns it, so B puts it back on the shelf
                    if (pc.Tool == Tool.Item) { if (b) pc.DropTool(); }
                    else right = pad.buttonEast.isPressed;
                    if (x) pc.RotateHeld();
                    if (y && hud.HintButton.gameObject.activeInHierarchy) hud.HintButton.Press();
                    if (pad.leftShoulder.wasPressedThisFrame) CycleMaterial(-1);
                    if (pad.rightShoulder.wasPressedThisFrame) CycleMaterial(+1);
                    if (pad.leftTrigger.wasPressedThisFrame) pc.Undo();
                    if (pad.rightTrigger.wasPressedThisFrame) pc.Redo();
                    if (pad.selectButton.wasPressedThisFrame && pc.ReadyToSeal && pc.Tool == Tool.None) G.SealAndShip();
                    break;
                }
                case Phase.Journey:
                {
                    var j = G.Journey;
                    if (start) { hud.SetPaused(true); break; }
                    if (b) j.Skip();
                    if (a && !OverUi()) { j.UserPaused = !j.UserPaused; left = false; }
                    if (x) j.Speed = j.Speed >= 2f ? 0.25f : j.Speed * 2f;
                    if (y) j.CameraMode = (j.CameraMode + 1) % 3;
                    if (j.IsReplay && pad.rightShoulder.wasPressedThisFrame) j.NextTrouble();
                    break;
                }
                case Phase.Reveal:
                    if ((a || b) && hud.RevealSkipButton.gameObject.activeInHierarchy) { hud.RevealSkipButton.Press(); left = false; }
                    break;
                case Phase.Results:
                    if (x) hud.RepackButton.Press();
                    else if (y) hud.ReplayButton.Press();
                    break;
            }
        }

        void CycleMaterial(int dir)
        {
            var pc = G.Packing;
            var lv = pc.Level;
            if (lv == null) return;
            var avail = new List<MaterialSlot>();
            for (int i = 0; i < 6; i++) if (lv.Materials.Get((MaterialSlot)i) > 0) avail.Add((MaterialSlot)i);
            if (avail.Count == 0) return;
            int cur = -1;
            for (int i = 0; i < avail.Count; i++) if (IsSelected(pc, avail[i])) cur = i;
            int next = cur < 0 ? (dir > 0 ? 0 : avail.Count - 1) : (cur + dir + avail.Count) % avail.Count;
            pc.SelectMaterial(avail[next]);
        }

        static bool IsSelected(PackingController pc, MaterialSlot s)
        {
            switch (s)
            {
                case MaterialSlot.Paper: return pc.Tool == Tool.Padding && pc.HeldKind == PieceKind.Paper;
                case MaterialSlot.Bubble: return pc.Tool == Tool.Padding && pc.HeldKind == PieceKind.Bubble;
                case MaterialSlot.Foam: return pc.Tool == Tool.Padding && pc.HeldKind == PieceKind.Foam;
                case MaterialSlot.Divider: return pc.Tool == Tool.Divider;
                case MaterialSlot.Shelf: return pc.Tool == Tool.Shelf;
                default: return pc.Tool == Tool.Strap;
            }
        }

        bool OverUi()
        {
            if (EventSystem.current == null) return false;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pos }, hits);
            return hits.Count > 0;
        }

        // ---- snapping --------------------------------------------------------------------------------

        string ScreenKey()
        {
            var ms = G.Menus.ActiveScreen;
            return $"{G.Phase}|{G.Hud.Paused}|{(ms != null ? ms.name : "-")}|{G.Hud.ResultsShowing}|{(G.Level != null ? G.Level.Number : 0)}";
        }

        void SnapToDefault()
        {
            UiButton target = null;
            // Settings and the Delivery Log open on top of the pause menu (which stays paused underneath)
            if (G.Menus.Open) target = G.Menus.DefaultButton;
            else if (G.Hud.Paused) target = G.Hud.ResumeButton;
            else if (G.Phase == Phase.Results && G.Hud.ResultsShowing) target = G.Hud.NextButton.Interactable ? G.Hud.NextButton : G.Hud.RepackButton;
            else if (G.Phase == Phase.Packing && G.Station.Box != null)
            {
                var b = G.Station.Box;
                pos = G.Rig.Cam.WorldToScreenPoint(b.CellToWorld(b.W * 0.5f, b.H * 0.5f));
                return;
            }
            if (target != null && target.isActiveAndEnabled) pos = ScreenOf(target.Image.rectTransform);
        }

        static Vector2 ScreenOf(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        /// <summary>Everything the d-pad can land on, in screen pixels.</summary>
        List<Vector2> Targets()
        {
            var list = new List<Vector2>();
            Transform uiRoot = G.Menus.Open ? G.Menus.ActiveScreen : (G.Hud.Paused ? G.Hud.PauseRoot : G.Hud.Root);
            if (uiRoot != null)
            {
                foreach (var b in uiRoot.GetComponentsInChildren<UiButton>(false))
                    if (b.Interactable && b.isActiveAndEnabled) list.Add(ScreenOf(b.Image.rectTransform));
                foreach (var s in uiRoot.GetComponentsInChildren<Selectable>(false))
                    if (s.IsInteractable() && s.isActiveAndEnabled) list.Add(ScreenOf((RectTransform)s.transform));
            }
            if (!G.Hud.Paused && !G.Menus.Open && G.Phase == Phase.Packing && G.Station.Box != null)
            {
                var box = G.Station.Box;
                var cam = G.Rig.Cam;
                var pc = G.Packing;
                if (pc.Tool == Tool.Divider)
                    for (int line = 1; line < box.W; line++) list.Add(cam.WorldToScreenPoint(box.CellToWorld(line, box.H * 0.5f)));
                else if (pc.Tool == Tool.Shelf)
                    for (int row = 1; row < box.H; row++)
                        for (int col = 0; col < box.W; col++) list.Add(cam.WorldToScreenPoint(box.CellToWorld(col + 0.5f, row)));
                else
                    for (int yy = 0; yy < box.H; yy++)
                        for (int xx = 0; xx < box.W; xx++) list.Add(cam.WorldToScreenPoint(box.CellToWorld(xx + 0.5f, yy + 0.5f)));
                foreach (var t in pc.TrayPositions) list.Add(cam.WorldToScreenPoint(t + Vector3.up * 0.06f));
            }
            return list;
        }

        void Snap(Vector2 dir)
        {
            float best = float.MaxValue;
            Vector2? to = null;
            foreach (var c in Targets())
            {
                var v = c - pos;
                float d = v.magnitude;
                if (d < 6f) continue;
                float cos = Vector2.Dot(v / d, dir);
                if (cos < 0.5f) continue;                       // within 60 degrees of the press
                float score = d * (1f + 3f * (1f - cos));       // prefer straight ahead
                if (score < best) { best = score; to = c; }
            }
            if (to.HasValue) pos = to.Value;
        }

        void OnDestroy()
        {
            if (padMouse != null && padMouse.added) InputSystem.RemoveDevice(padMouse);
            Cursor.visible = true;
        }
    }
}
