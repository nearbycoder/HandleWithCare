using System;
using System.Collections.Generic;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HWC.Gameplay
{
    /// <summary>
    /// Plays a Recording: drives the box through each leg's set piece and the pieces inside it,
    /// with a director that knows the future (slow motion before incidents, shake on impacts).
    /// Also serves replays with scrubbing and speed control.
    /// </summary>
    public sealed class JourneyPlayer : MonoBehaviour
    {
        public Recording Rec;
        public BoxView Box;
        public float T;               // playback time (s)
        public float Speed = 1f;      // user speed (replay)
        public bool Playing;
        public bool UserPaused;
        public bool IsReplay;
        public float SlowMo = 1f;     // director time scale
        public int CurrentLeg = -1;
        public event Action<int> LegChanged;
        public event Action<string> Caption;
        public event Action<Incident> IncidentHappened;

        Action onDone;
        readonly List<StageSet> stages = new List<StageSet>();
        Transform stageRoot;
        PieceView[] bodyViews;
        int nextBump, nextIncident, lastSpan = -1;
        float prevT;
        readonly List<(float start, float end, float scale, Vector3 focus)> slowWindows = new List<(float, float, float, Vector3)>();
        Vector3 focusPoint;
        float focusWeight;
        bool ended;

        Game G => Game.I;

        public float Duration => Rec == null ? 0 : Rec.Duration;

        public void Play(Recording rec, BoxView box, Action done, bool replay = false)
        {
            Stop();
            Rec = rec;
            Box = box;
            onDone = done;
            IsReplay = replay;
            T = 0; prevT = -1;
            Speed = 1f;
            UserPaused = false;
            Playing = true;
            ended = false;
            CurrentLeg = -1;
            nextBump = 0; nextIncident = 0; lastSpan = -1;

            if (stageRoot == null)
            {
                stageRoot = new GameObject("Stages").transform;
                stageRoot.SetParent(transform, false);
            }
            var kin = rec.Kin;
            for (int li = 0; li < kin.Route.Legs.Count; li++)
            {
                var spans = kin.Spans.FindAll(s => s.Leg == li);
                var st = StageSet.Build(stageRoot, kin.Route.Legs[li].Kind, spans, box.InteriorWidth * 0.5f + BoxView.Wall, box.OuterHalfHeight, new Vector3(0, 0, 200f + li * 60f));
                st.gameObject.SetActive(false);
                stages.Add(st);
            }

            // map recorded bodies to the box's piece views
            bodyViews = new PieceView[rec.Bodies.Length];
            for (int b = 0; b < rec.Bodies.Length; b++)
            {
                var info = rec.Bodies[b];
                if (info.Type != BodyType.Piece) continue;
                if (info.PieceIndex >= 0 && info.PieceIndex < box.Pieces.Count)
                {
                    bodyViews[b] = box.Pieces[info.PieceIndex];
                    bodyViews[b].ResetLook();
                }
            }

            BuildDirector();
            G.Station.gameObject.SetActive(false);
            box.transform.SetParent(stageRoot, true);
            box.gameObject.SetActive(true);
            Apply(true);
        }

        public void Stop()
        {
            Playing = false;
            foreach (var s in stages) if (s != null) Destroy(s.gameObject);
            stages.Clear();
            Time.timeScale = 1f;
            if (Box != null && G != null && G.Station != null)
            {
                Box.transform.SetParent(G.Station.transform, false);
                Box.transform.localPosition = new Vector3(0, Box.OuterHalfHeight, 0);
                Box.transform.localRotation = Quaternion.identity;
                G.Station.gameObject.SetActive(true);
            }
            Rec = null;
        }

        void BuildDirector()
        {
            slowWindows.Clear();
            if (G.Save.ReducedMotion) return;
            foreach (var inc in Rec.Incidents)
            {
                bool big = inc.IsFailure || inc.Kind == IncidentKind.Sneezed || inc.Kind == IncidentKind.Toppled || inc.Kind == IncidentKind.StrapSnapped;
                if (!big) continue;
                var p = new Vector3(inc.Where.x, inc.Where.y, 0);
                slowWindows.Add((inc.Time - 0.45f, inc.Time + 0.35f, 0.22f, p));
            }
        }

        float DirectorScale(out bool focusing)
        {
            focusing = false;
            float s = 1f;
            foreach (var w in slowWindows)
            {
                if (T < w.start - 0.25f || T > w.end + 0.3f) continue;
                float k;
                if (T < w.start) k = 1f - (w.start - T) / 0.25f;
                else if (T > w.end) k = 1f - (T - w.end) / 0.3f;
                else k = 1f;
                k = Mathf.Clamp01(k);
                s = Mathf.Min(s, Mathf.Lerp(1f, w.scale, k));
                if (k > 0.2f) { focusing = true; focusPoint = w.focus; }
            }
            return s;
        }

        void Update()
        {
            if (!Playing || Rec == null) return;
            if (G.Hud.Paused) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) UserPaused = !UserPaused;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Skip();
                if (IsReplay || UserPaused)
                {
                    if (kb.leftArrowKey.isPressed) T = Mathf.Max(0, T - Time.unscaledDeltaTime * 2f);
                    if (kb.rightArrowKey.isPressed) T = Mathf.Min(Duration, T + Time.unscaledDeltaTime * 2f);
                }
                if (kb.digit1Key.wasPressedThisFrame) Speed = 0.25f;
                if (kb.digit2Key.wasPressedThisFrame) Speed = 0.5f;
                if (kb.digit3Key.wasPressedThisFrame) Speed = 1f;
                if (kb.digit4Key.wasPressedThisFrame) Speed = 2f;
            }

            SlowMo = DirectorScale(out bool focusing);
            focusWeight = Mathf.MoveTowards(focusWeight, focusing ? 1f : 0f, Time.unscaledDeltaTime * 3f);
            if (!UserPaused) T += Time.unscaledDeltaTime * Speed * SlowMo;
            Time.timeScale = UserPaused ? 0.0001f : Mathf.Clamp(Speed * SlowMo, 0.05f, 4f);
            if (T >= Duration)
            {
                T = Duration;
                Apply(false);
                if (!ended) { ended = true; Finish(); }
                return;
            }
            Apply(false);
        }

        public void Skip()
        {
            if (ended) return;
            T = Duration;
            nextBump = Rec.Bumps.Count;
            Apply(true);
            ended = true;
            Finish();
        }

        void Finish()
        {
            Playing = false;
            Time.timeScale = 1f;
            onDone?.Invoke();
        }

        public void Seek(float t)
        {
            T = Mathf.Clamp(t, 0, Duration);
            if (T < prevT) { nextBump = 0; nextIncident = 0; lastSpan = -1; foreach (var v in bodyViews) if (v != null) v.ResetLook(); }
            Apply(true);
        }

        void Apply(bool snap)
        {
            var kin = Rec.Kin;
            float tickF = T * SimConst.TickRate;
            int k = Mathf.Clamp((int)tickF, 0, kin.TickCount - 1);
            int k2 = Mathf.Min(k + 1, kin.TickCount - 1);
            float a = tickF - k;
            if (kin.LegOf[k2] != kin.LegOf[k]) k2 = k;
            int leg = kin.LegOf[k];
            if (leg != CurrentLeg)
            {
                for (int i = 0; i < stages.Count; i++) stages[i].gameObject.SetActive(i == leg);
                CurrentLeg = leg;
                snap = true;
                LegChanged?.Invoke(leg);
            }
            var st = stages[leg];
            double x = kin.X[k] + (kin.X[k2] - kin.X[k]) * a;
            double y = kin.Y[k] + (kin.Y[k2] - kin.Y[k]) * a;
            double ang = kin.A[k] + (kin.A[k2] - kin.A[k]) * a;
            var boxPos = st.BoxWorld(x, y);
            float angDeg = (float)(ang * Mathf.Rad2Deg);
            Box.transform.position = boxPos;
            Box.transform.rotation = Quaternion.Euler(0, 0, angDeg);
            kin.Velocity(k, out double vx, out double vy);
            st.FollowBox(boxPos, angDeg, (float)vx);

            // pieces
            float fF = T / (SimConst.FrameEvery * SimConst.Dt);
            int f0 = Mathf.Clamp((int)fF, 0, Rec.Frames.Count - 1);
            int f1 = Mathf.Min(f0 + 1, Rec.Frames.Count - 1);
            float fa = fF - f0;
            var A = Rec.Frames[f0];
            var B = Rec.Frames[f1];
            for (int b = 0; b < bodyViews.Length; b++)
            {
                var v = bodyViews[b];
                if (v == null) continue;
                var fr = A[b];
                var pb = B[b];
                // interpolate unless it teleported (topple)
                if ((fr.Half.x == pb.Half.x) && (fr.State & BodyState.Removed) == 0)
                    fr.Pos = new V2(fr.Pos.x + (pb.Pos.x - fr.Pos.x) * fa, fr.Pos.y + (pb.Pos.y - fr.Pos.y) * fa);
                v.ApplyFrame(fr, snap);
            }

            // captions for route events
            int span = kin.EventOf[k];
            if (span != lastSpan)
            {
                lastSpan = span;
                foreach (var sp in kin.Spans)
                    if (sp.Leg == leg && sp.Index == span && sp.Def.Label != null && !snap) Caption?.Invoke(sp.Def.Label);
            }

            // bumps → sound, shake, dust
            int tick = k;
            if (!snap)
            {
                while (nextBump < Rec.Bumps.Count && Rec.Bumps[nextBump].Tick <= tick)
                {
                    var bump = Rec.Bumps[nextBump++];
                    float strength = Mathf.Clamp01((bump.Speed - 2f) / 14f);
                    G.Hud.Sfx(BumpSound(bump), 0.3f + strength);
                    G.Rig.AddTrauma(strength * 0.35f);
                    if (strength > 0.25f) Fx.Dust(Box.CellToWorld(bump.Point.x, bump.Point.y), 0.6f);
                }
                while (nextIncident < Rec.Incidents.Count && Rec.Incidents[nextIncident].Tick <= tick)
                {
                    var inc = Rec.Incidents[nextIncident++];
                    OnIncident(inc);
                }
            }
            else
            {
                while (nextBump < Rec.Bumps.Count && Rec.Bumps[nextBump].Tick <= tick) nextBump++;
                while (nextIncident < Rec.Incidents.Count && Rec.Incidents[nextIncident].Tick <= tick) nextIncident++;
            }
            prevT = T;

            Camera(st, boxPos, angDeg, snap);
        }

        string BumpSound(BumpFx b)
        {
            var ia = Rec.Bodies[b.A];
            var ib = Rec.Bodies[b.B];
            PieceKind? k = ia.Type == BodyType.Piece ? ia.Kind : (ib.Type == BodyType.Piece ? ib.Kind : (PieceKind?)null);
            if (k == null) return "thud";
            var def = Catalog.Get(k.Value);
            if (def.Kind == PieceKind.Bubble || def.Kind == PieceKind.Paper || def.Kind == PieceKind.Foam) return "soft";
            if (def.Has(Quirk.Metal)) return "clank";
            if (def.Has(Quirk.Fragile)) return "clink";
            return "thud";
        }

        void OnIncident(Incident inc)
        {
            var v = bodyViews[inc.Body];
            var info = Rec.Bodies[inc.Body];
            var pos = Box.CellToWorld(inc.Where.x, inc.Where.y);
            switch (inc.Kind)
            {
                case IncidentKind.Broke:
                    Fx.Shards(pos, Palette.ItemColor(info.Kind));
                    G.Hud.Sfx(info.Kind == PieceKind.Teacup || info.Kind == PieceKind.Vase ? "smash" : "crack");
                    G.Rig.AddTrauma(0.6f);
                    G.Post.Kick(1f);
                    break;
                case IncidentKind.Squished: Fx.Poof(pos, Palette.ItemColor(info.Kind), 1f); G.Hud.Sfx("squish"); break;
                case IncidentKind.Popped:
                    if (info.Kind == PieceKind.Balloon) { Fx.Confetti(pos, 0.5f); G.Hud.Sfx("bang"); G.Rig.AddTrauma(0.5f); }
                    else G.Hud.Sfx("pop");
                    break;
                case IncidentKind.Burned: Fx.Poof(pos, new Color(0.3f, 0.28f, 0.26f), 0.8f); break;
                case IncidentKind.Melted: Fx.Poof(pos, Palette.ItemColor(PieceKind.IceSwan), 1f); G.Hud.Sfx("melt"); break;
                case IncidentKind.Sneezed:
                    var facing = Rec.Frames[Mathf.Min(Rec.Frames.Count - 1, Recording.FrameOfTick(inc.Tick))][inc.Body].Facing;
                    var dir = Box.transform.rotation * new Vector3(facing, 0, 0);
                    var nose = Box.CellToWorld(inc.Where.x + facing * 1.0f, inc.Where.y);
                    Fx.Fire(nose, dir, SimConst.FlameLength * BoxView.Cell);
                    G.Hud.Sfx("sneeze");
                    G.Rig.AddTrauma(0.4f);
                    break;
                case IncidentKind.SneezeWindup:
                case IncidentKind.Tickled: G.Hud.Sfx("ahh"); break;
                case IncidentKind.Woke: G.Hud.Sfx("yelp"); break;
                case IncidentKind.Spilled: Fx.Poof(pos, Palette.ItemColor(info.Kind), 0.7f); G.Hud.Sfx("spill"); break;
                case IncidentKind.Toppled: G.Hud.Sfx("topple"); break;
                case IncidentKind.StrapSnapped: G.Hud.Sfx("snap"); G.Rig.AddTrauma(0.3f); break;
            }
            IncidentHappened?.Invoke(inc);
        }

        float anchorY;

        void Camera(StageSet st, Vector3 boxPos, float angDeg, bool snap)
        {
            var rig = G.Rig;
            // follow the box exactly along the road; smooth vertically so bumps read as motion
            if (snap) anchorY = boxPos.y;
            anchorY = Mathf.Lerp(anchorY, boxPos.y, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.5f));
            rig.Anchor = new Vector3(boxPos.x, anchorY, boxPos.z);
            float size = Mathf.Max(Box.InteriorWidth, Box.InteriorHeight * 1.4f);
            float dist = 1.6f + size * 1.9f;
            var off = st.CameraOffset;
            var look = boxPos + new Vector3(0.15f, 0.05f, 0);
            var pos = look + new Vector3(off.x * dist * 0.35f, off.y * dist * 0.45f, -dist);
            if (focusWeight > 0.01f)
            {
                var fp = Box.CellToWorld(focusPoint.x, focusPoint.y);
                var cpos = fp + new Vector3(0.25f, 0.18f, -dist * 0.55f);
                look = Vector3.Lerp(look, fp, focusWeight * 0.85f);
                pos = Vector3.Lerp(pos, cpos, focusWeight);
            }
            rig.LookAt(pos, look, Mathf.Lerp(30f, 26f, focusWeight));
            rig.PosSharpness = 4.5f;
            rig.RotSharpness = 6f;
            if (snap) rig.Snap();
        }
    }
}
