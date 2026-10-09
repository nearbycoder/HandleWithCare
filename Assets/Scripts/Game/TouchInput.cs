using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HWC.Sim;
using HWC.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace HWC.Gameplay
{
    /// <summary>
    /// Touch screens in the browser build. The page (Assets/WebGLTemplates/HandleWithCare) decides when the touch
    /// controls show (a touch-first screen, or a real touch; a mouse, key or gamepad hides them) and draws their
    /// buttons; it tells this object through unityInstance.SendMessage("TouchBridge", ...) and gets the game's state
    /// back as JSON (WebPlatform.TouchState). Touches on the game's own buttons go to the UI as they are. Touches on
    /// the bench drive a virtual mouse, like the gamepad's (PadInput), so the packing code works unchanged:
    /// a tap clicks; dragging an item from the shelf or the box carries it above the finger (so the finger never
    /// hides it) and lets go where it is drawn; dragging with padding paints, or rubs out in ERASE mode; holding a
    /// finger still on something shows its card without picking it up.
    /// </summary>
    public sealed class TouchInput : MonoBehaviour
    {
        public static TouchInput I;
        /// <summary>True while the touch controls are showing (prompts say "tap", the HUD makes room).</summary>
        public bool Active { get; private set; }
        public event Action<bool> ActiveChanged;
        /// <summary>ERASE: a finger in the box rubs out padding (the right mouse button).</summary>
        public bool Erase;
        /// <summary>The page's safe area, in screen pixels: left, bottom, right, top.</summary>
        public Vector4 SafeArea { get; private set; }
        public event Action SafeAreaChanged;

        Mouse touchMouse;
        Game G => Game.I;

        public static TouchInput Create(Transform parent)
        {
            var go = new GameObject("TouchBridge");   // the name the page sends to
            go.transform.SetParent(parent, false);
            I = go.AddComponent<TouchInput>();
            return I;
        }

        public static bool IsVirtual(Mouse m) => I != null && m != null && m == I.touchMouse;

        // ---- from the page ---------------------------------------------------------------------------

        /// <summary>"touch" shows the touch controls, anything else ("mouse", "keyboard", "pad") hides them.</summary>
        public void SetMode(string mode)
        {
            bool on = mode == "touch";
            if (on == Active) return;
            Active = on;
            if (!on) Release();
            Erase = false;
            Debug.Log(on ? "[Touch] the touch controls took over" : $"[Touch] {mode} took over");
            ActiveChanged?.Invoke(on);
            sentState = null;
        }

        float pxPerCss = 1f;
        /// <summary>Screen pixels per CSS pixel, from the page (1 until it says).</summary>
        public float PixelsPerCss => pxPerCss;

        /// <summary>"cssWidth,cssHeight,top,right,bottom,left": the page's size and safe-area insets in CSS pixels.</summary>
        public void SetView(string v)
        {
            var p = v.Split(',');
            if (p.Length < 6) return;
            float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
            float cssW = Mathf.Max(1f, F(0));
            pxPerCss = Screen.width / cssW;
            SafeArea = new Vector4(F(5), F(4), F(3), F(2)) * pxPerCss;
            Debug.Log($"[Touch] view {v}: {pxPerCss:0.00} screen pixels per CSS pixel");
            SafeAreaChanged?.Invoke();
        }

        /// <summary>A touch control was tapped.</summary>
        public void Command(string c)
        {
            if (G == null) return;
            var pc = G.Packing;
            var hud = G.Hud;
            var j = G.Journey;
            string arg = null;
            int colon = c.IndexOf(':');
            if (colon > 0) { arg = c.Substring(colon + 1); c = c.Substring(0, colon); }
            WebPlatform.Log("touch " + c + (arg != null ? " " + arg : ""));
            bool packing = G.Phase == Phase.Packing && pc.Active && !hud.Paused && !G.Menus.Open;
            switch (c)
            {
                case "rotate": if (packing) pc.RotateHeld(); break;
                case "back": if (packing) pc.DropTool(); Erase = false; break;
                case "erase": Erase = !Erase; break;
                case "undo": if (packing) pc.Undo(); break;
                case "redo": if (packing) pc.Redo(); break;
                case "clear": if (packing) pc.ClearAll(); break;
                case "best": if (packing && hud.BestButton.gameObject.activeInHierarchy) hud.BestButton.Press(); break;
                case "hint": if (packing && hud.HintButton.gameObject.activeInHierarchy) hud.HintButton.Press(); break;
                case "seal": if (packing && pc.ReadyToSeal) { if (pc.Tool != Tool.None) pc.DropTool(); G.SealAndShip(); } break;
                case "pause":
                    if (!G.Menus.Open && !ClipRecorder.I.Busy && (G.Phase == Phase.Packing || G.Phase == Phase.Journey)) hud.SetPaused(!hud.Paused);
                    break;
                case "play": if (G.Phase == Phase.Journey) j.UserPaused = !j.UserPaused; break;
                case "speed":
                    if (G.Phase == Phase.Journey && float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float sp)) { j.Speed = sp; j.UserPaused = false; }
                    break;
                case "cam": if (G.Phase == Phase.Journey) j.CameraMode = (j.CameraMode + 1) % 3; break;
                case "skip":
                    if (G.Phase == Phase.Journey) j.Skip();
                    else if (G.Phase == Phase.Reveal && hud.RevealSkipButton.gameObject.activeInHierarchy) hud.RevealSkipButton.Press();
                    break;
                case "trouble": if (G.Phase == Phase.Journey && j.IsReplay) j.NextTrouble(); break;
                case "seek":
                    if (G.Phase == Phase.Journey && j.IsReplay && float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                        j.Seek(Mathf.Clamp01(f) * j.Duration);
                    break;
                case "gif": if (G.Phase == Phase.Journey && j.IsReplay && hud.ReplayGifButton.gameObject.activeInHierarchy) hud.ReplayGifButton.Press(); break;
                case "memory": WebPlatform.LogMemory(arg ?? "now"); break;
                case "report": ReportButtons(); break;
            }
            sentState = null;   // answer with the new state at once
        }

        /// <summary>For the touch check (Tools/check-mobile.mjs): where the game's own buttons and the bench's items
        /// are, in CSS pixels from the top left, so it taps real places rather than guessed ones.</summary>
        void ReportButtons()
        {
            var sb = new StringBuilder("buttons:");
            void Add(string name, RectTransform rt)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) return;
                var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                sb.Append($" {name} {c.x / pxPerCss:0},{(Screen.height - c.y) / pxPerCss:0};");
            }
            var ms = G.Menus.ActiveScreen;
            if (ms != null)
                foreach (var b in ms.GetComponentsInChildren<UiButton>(false))
                    if (b.Label != null && !string.IsNullOrEmpty(b.Label.text)) Add(b.Label.text.Replace(' ', '_'), b.Image.rectTransform);
            var hud = G.Hud;
            if (hud.Paused) Add("RESUME", hud.ResumeButton.Image.rectTransform);
            if (hud.ResultsShowing) { Add("NEXT", hud.NextButton.Image.rectTransform); Add("REPACK", hud.RepackButton.Image.rectTransform); Add("REPLAY", hud.ReplayButton.Image.rectTransform); }
            if (G.Phase == Phase.Packing && G.Packing.Active)
            {
                foreach (var (slot, _, _, rt) in hud.ToolbarTags()) Add(slot.ToString().ToUpperInvariant(), rt);
                var cam = G.Rig.Cam;
                var pc = G.Packing;
                int i = 0;
                foreach (var t in pc.TrayPositions)
                {
                    var s = cam.WorldToScreenPoint(t + Vector3.up * 0.06f);
                    sb.Append($" shelf{i++} {s.x / pxPerCss:0},{(Screen.height - s.y) / pxPerCss:0};");
                }
                var box = G.Station.Box;
                for (int y = 0; y < box.H; y++)
                    for (int x = 0; x < box.W; x++)
                    {
                        var s = cam.WorldToScreenPoint(box.CellToWorld(x + 0.5f, y + 0.5f));
                        sb.Append($" cell{x}_{y} {s.x / pxPerCss:0},{(Screen.height - s.y) / pxPerCss:0};");
                    }
            }
            WebPlatform.Log(sb.ToString());
        }

        // ---- per frame -------------------------------------------------------------------------------

        // one finger drives the bench at a time; others are left to the UI (and the page's buttons)
        int finger = -1;
        bool fingerOnUi, classified, dragging, still, painting, carrying;
        Vector2 start, cur, pos;
        float startTime, offsetK;
        bool left, right;
        // clicks wait their turn (a drag can pick up and drop within a few frames): each is a press, then a release
        struct PendingClick { public Vector2 At; public bool Right; }
        readonly Queue<PendingClick> clicks = new Queue<PendingClick>();
        PendingClick click;
        int clickPhase;          // 2: press this frame, 1: release, 0: none
        bool dropToolPending;    // let go over a button: back to the shelf once the pick-up has happened
        int beginFrame;
        const float Slop = 9f, LongPress = 0.45f, Lift = 70f;   // CSS pixels and seconds

        void Update()
        {
            if (G == null) return;
            if (Active && PadInput.I != null && PadInput.I.Active) { SetMode("pad"); WebPlatform.InputMode("pad"); }
            if (Active) Track();
            SendState();
        }

        void Track()
        {
            var ts = Touchscreen.current;
            if (ts == null) return;
            TouchControl t = null;
            if (finger >= 0)
            {
                foreach (var tc in ts.touches) if (tc.touchId.ReadValue() == finger) { t = tc; break; }
                if (t == null || !t.press.isPressed) { End(); t = null; }
            }
            if (finger < 0)
                foreach (var tc in ts.touches)
                    if (tc.press.wasPressedThisFrame) { Begin(tc); t = tc; break; }

            if (t != null && !fingerOnUi)
            {
                cur = t.position.ReadValue();
                Hold();
            }
            Emit();
        }

        bool OnBench => G.Phase == Phase.Packing && G.Packing.Active && !G.Hud.Paused && !G.Menus.Open;

        void Begin(TouchControl t)
        {
            finger = t.touchId.ReadValue();
            start = cur = t.position.ReadValue();
            startTime = Time.unscaledTime;
            beginFrame = Time.frameCount;
            classified = dragging = still = painting = carrying = false;
            offsetK = 0f;
            fingerOnUi = OverUi(start);
            if (fingerOnUi) return;   // the game's own buttons take touches themselves
            pos = start;
            if (OnBench && G.Packing.Tool == Tool.Padding)
            {
                // paint (or rub out) from the first cell, under the finger
                painting = classified = true;
                if (Erase) right = true; else left = true;
            }
        }

        void Hold()
        {
            var pc = G.Packing;
            bool moved = (cur - start).magnitude > Slop * pxPerCss;
            if (!classified)
            {
                if (moved && Time.frameCount >= beginFrame + 2)   // (the bench has seen where the finger is)
                {
                    classified = dragging = true;
                    var use = pc.CardFor;
                    if (OnBench && pc.Tool == Tool.None && !Erase && (use == PackingController.CardUse.Shelf || use == PackingController.CardUse.Placed))
                    {
                        Click(start, false);   // pick up what the drag started on (from the shelf or the box)
                        carrying = true;       // it rides above the finger once it is in hand
                    }
                    else if (OnBench && pc.Tool == Tool.Item) carrying = true;
                }
                else if (Time.unscaledTime - startTime > LongPress) { classified = still = true; }   // its card, no click
            }
            if (painting) { pos = cur; return; }
            if (dragging)
            {
                bool lift = carrying && OnBench && pc.Tool == Tool.Item;
                offsetK = Mathf.MoveTowards(offsetK, lift ? 1f : 0f, Time.unscaledDeltaTime / 0.12f);
                pos = cur + new Vector2(0, Lift * pxPerCss * offsetK);
            }
            else pos = start;
        }

        void End()
        {
            if (!fingerOnUi)
            {
                if (painting) { left = right = false; }
                else if (!classified) Click(start);                         // a tap
                else if (dragging && carrying && OnBench)
                {
                    // let go of what it carries where it is drawn; over a button (the toolbar), it goes back instead
                    if (!OverUi(pos)) Click(pos, false);
                    else dropToolPending = true;
                }
                else if (dragging && OnBench && G.Packing.Tool != Tool.None && G.Packing.Tool != Tool.Item && !OverUi(pos)) Click(pos);   // a divider, shelf or strap
            }
            finger = -1;
            fingerOnUi = painting = dragging = carrying = false;
        }

        void Click(Vector2 at, bool? right = null)
        {
            // ERASE with nothing in hand: a tap takes a piece out of the box (the right button)
            clicks.Enqueue(new PendingClick { At = at, Right = right ?? (Erase && OnBench && G.Packing.Tool == Tool.None) });
            pos = at;
        }

        void Release()
        {
            finger = -1;
            fingerOnUi = painting = dragging = carrying = false;
            left = right = false;
            clicks.Clear();
            clickPhase = 0;
            dropToolPending = false;
            if (touchMouse != null) InputSystem.QueueStateEvent(touchMouse, new MouseState { position = pos });
        }

        Vector2 lastSent;
        void Emit()
        {
            bool l = left, r = right;
            Vector2 p = pos;
            if (clickPhase == 0 && clicks.Count > 0) { click = clicks.Dequeue(); clickPhase = 2; }
            if (clickPhase > 0)
            {
                p = click.At;
                if (click.Right) r = clickPhase == 2; else l = clickPhase == 2;
                clickPhase--;
            }
            else if (dropToolPending && clicks.Count == 0)
            {
                dropToolPending = false;
                if (OnBench && G.Packing.Tool == Tool.Item) G.Packing.DropTool();
            }
            if (touchMouse == null)
            {
                if (finger < 0 && !l && !r) return;   // nothing touched the bench yet
                touchMouse = InputSystem.AddDevice<Mouse>("TouchMouse");
            }
            if (p == lastSent && !l && !r && !touchMouse.leftButton.isPressed && !touchMouse.rightButton.isPressed) return;
            var st = new MouseState { position = p, delta = p - lastSent };
            if (l) st = st.WithButton(MouseButton.Left, true);
            if (r) st = st.WithButton(MouseButton.Right, true);
            InputSystem.QueueStateEvent(touchMouse, st);
            lastSent = p;
        }

        static readonly List<RaycastResult> hits = new List<RaycastResult>();
        static bool OverUi(Vector2 p)
        {
            if (EventSystem.current == null) return false;
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = p }, hits);
            return hits.Count > 0;
        }

        // ---- to the page -------------------------------------------------------------------------------

        string sentState;
        float stateAt;
        int stateLevel = -1;
        string troubles = "";

        void SendState()
        {
            if (!WebPlatform.IsWeb || Time.unscaledTime < stateAt && sentState != null) return;
            stateAt = Time.unscaledTime + 0.1f;
            string s = State();
            if (s == sentState) return;
            sentState = s;
            WebPlatform.TouchState(s);
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string B(bool b) => b ? "1" : "0";

        string State()
        {
            var pc = G.Packing;
            var hud = G.Hud;
            var j = G.Journey;
            string phase = G.Phase.ToString().ToLowerInvariant();
            var sb = new StringBuilder(256);
            sb.Append("{\"phase\":").Append(Q(phase));
            sb.Append(",\"paused\":").Append(B(hud.Paused));
            sb.Append(",\"menu\":").Append(B(G.Menus.Open));
            sb.Append(",\"busy\":").Append(B(ClipRecorder.I != null && ClipRecorder.I.Busy));
            sb.Append(",\"card\":").Append(B(hud.ShiftCardShowing));
            if (G.Phase == Phase.Packing && pc.Active && pc.Level != null)
            {
                var def = pc.Tool == Tool.Item || pc.Tool == Tool.Padding ? Catalog.Get(pc.HeldKind) : null;
                sb.Append(",\"tool\":").Append(Q(pc.Tool.ToString().ToLowerInvariant()));
                sb.Append(",\"turn\":").Append(B(def != null && (def.Rotatable || def.Has(Quirk.Facing))));
                sb.Append(",\"erase\":").Append(B(Erase));
                sb.Append(",\"undo\":").Append(B(pc.UndoDepth > 0));
                sb.Append(",\"redo\":").Append(B(pc.RedoDepth > 0));
                sb.Append(",\"clear\":").Append(B(pc.Pk.Pieces.Count + pc.Pk.Dividers.Count + pc.Pk.Shelves.Count > 0));
                sb.Append(",\"best\":").Append(B(hud.BestButton.gameObject.activeSelf));
                sb.Append(",\"hint\":").Append(B(hud.HintButton.gameObject.activeSelf));
                sb.Append(",\"hintLabel\":").Append(Q(hud.HintButton.Label != null ? hud.HintButton.Label.text : "ASK MABEL"));
                sb.Append(",\"seal\":").Append(B(pc.ReadyToSeal));
                sb.Append(",\"sealHint\":").Append(Q(hud.SealHintText));
            }
            if (G.Phase == Phase.Journey && j.Rec != null)
            {
                if (stateLevel != j.Rec.GetHashCode())
                {
                    stateLevel = j.Rec.GetHashCode();
                    var tb = new StringBuilder();
                    foreach (var tr in j.Rec.Troubles)
                        tb.Append(tb.Length > 0 ? "," : "").Append((tr.Time / Mathf.Max(0.01f, j.Duration)).ToString("0.000", CultureInfo.InvariantCulture)).Append(tr.Failure ? "" : "n");
                    troubles = tb.ToString();
                }
                sb.Append(",\"replay\":").Append(B(j.IsReplay));
                sb.Append(",\"stopped\":").Append(B(j.UserPaused));
                sb.Append(",\"speed\":").Append(j.Speed.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(",\"cam\":").Append(Q(JourneyPlayer.CameraModeNames[j.CameraMode]));
                sb.Append(",\"t\":").Append((j.T / Mathf.Max(0.01f, j.Duration)).ToString("0.000", CultureInfo.InvariantCulture));
                sb.Append(",\"troubles\":").Append(Q(troubles));
                sb.Append(",\"skip\":").Append(B(!j.IsReplay && !hud.Cinematic));
                sb.Append(",\"gif\":").Append(B(j.IsReplay && hud.ReplayGifButton.gameObject.activeInHierarchy));
            }
            if (G.Phase == Phase.Reveal) sb.Append(",\"skip\":").Append(B(hud.RevealSkipButton.gameObject.activeInHierarchy));
            sb.Append('}');
            return sb.ToString();
        }

        void OnDestroy()
        {
            if (touchMouse != null && touchMouse.added) InputSystem.RemoveDevice(touchMouse);
        }
    }
}
