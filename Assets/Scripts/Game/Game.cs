using System.Collections;
using HWC.Sim;
using HWC.UI;
using HWC.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace HWC.Gameplay
{
    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Game.I != null) return;
            var go = new GameObject("Game");
            go.AddComponent<Game>();
        }
    }

    public enum Phase { Boot, Title, Select, Packing, Sealing, Journey, Reveal, Results }

    /// <summary>Root of the whole game: builds the world and UI, owns the flow between phases.</summary>
    public sealed class Game : MonoBehaviour
    {
        public static Game I;

        public CameraRig Rig;
        public Post Post;
        public Light Sun;
        public Canvas Canvas;
        public Canvas OverlayCanvas;
        public Station Station;
        public PackingController Packing;
        public JourneyPlayer Journey;
        public Hud Hud;
        public Menus Menus;
        public Tutorial Tutorial;
        public RevealController Reveal;
        Phase phase;
        /// <summary>The frame the current phase began on. A key press that changed phase is not
        /// read again by the next screen in that same frame.</summary>
        public int PhaseFrame { get; private set; } = -1;
        public Phase Phase { get => phase; set { phase = value; PhaseFrame = Time.frameCount; } }
        public bool FreshPhase => Time.frameCount <= PhaseFrame;
        public bool Autopilot;
        public bool SkipReveal;     // the 20-delivery self-test skips the unboxing for speed

        public LevelDef Level;
        public Packing CurrentPacking;
        public Recording LastRun;
        public SaveData Save;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 120;
            Save = SaveData.Load();
            BuildCore();
        }

        void BuildCore()
        {
            Rig = CameraRig.Create();
            Rig.transform.SetParent(transform, false);
            Post = Post.Create();
            Post.transform.SetParent(transform, false);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = Palette.Hex("FFE6C7");
            Sun.intensity = 1.9f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.82f;
            Sun.shadowBias = 0.02f;
            Sun.shadowNormalBias = 0.25f;
            sunGo.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.Hex("9AA7C4") * 0.85f;
            RenderSettings.ambientEquatorColor = Palette.Hex("C9A98A") * 0.75f;
            RenderSettings.ambientGroundColor = Palette.Hex("5C4434") * 0.6f;
            RenderSettings.fog = false;

            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            Canvas = Ui.CreateCanvas("UI", 10, transform);
            OverlayCanvas = Ui.CreateCanvas("Overlay", 50, transform);

            Station = Station.Create(transform);
            Packing = new GameObject("Packing").AddComponent<PackingController>();
            Packing.transform.SetParent(transform, false);
            Journey = new GameObject("Journey").AddComponent<JourneyPlayer>();
            Journey.transform.SetParent(transform, false);
            Hud = new GameObject("Hud").AddComponent<Hud>();
            Hud.transform.SetParent(transform, false);
            Hud.Build(Canvas.transform);
            Reveal = new GameObject("Reveal").AddComponent<RevealController>();
            Reveal.transform.SetParent(transform, false);
            Menus = new GameObject("Menus").AddComponent<Menus>();
            Menus.transform.SetParent(transform, false);
            Menus.Build(OverlayCanvas.transform);
            Tutorial = new GameObject("Tutorial").AddComponent<Tutorial>();
            Tutorial.transform.SetParent(transform, false);
            Tutorial.Build(OverlayCanvas.transform);
            Hud.HookReveal(Reveal);
            PadInput.Create(transform);
            ApplySettings();
        }

        public void ApplySettings()
        {
            Rig.ShakeEnabled = Save.ScreenShake;
            Fx.Reduced = Save.ReducedMotion;
            GraphicsQuality.Apply(Save.HighQuality);
            Menus.ApplyAudio();
            if (!Application.isEditor) Screen.fullScreenMode = Save.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        void Start()
        {
            if (TrailerDirector.TryStart(this)) return;
            if (AutoPilot.TryStart(this))
            {
                Save = new SaveData { SeenTips = new System.Collections.Generic.List<string> { "basics" } };
                Autopilot = !System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-hwcMenus" || a == "-hwcPad");
                ApplySettings();
                if (Autopilot) StartLevel(1);
                return;
            }
            ShowTitle();
        }

        public void ShowTitle()
        {
            Phase = Phase.Title;
            Journey.Stop();
            Reveal.Hide();
            Packing.End();
            Tutorial.Stop();
            Hud.HideAllScreens();
            Post.SetDof(0f, 2f);
            Station.gameObject.SetActive(true);
            Station.ShowTitle();
            Menus.ShowTitle();
        }

        public void ShowDeliveryLog()
        {
            Menus.ShowSelect();
        }

        // ---- Flow ---------------------------------------------------------------------------------

        public void StartLevel(int number)
        {
            Menus.HideAll();
            Reveal.Hide();
            Level = Levels.Get(number);
            Save.LastLevel = number;
            CurrentPacking = Save.GetPacking(Level) ?? new Packing(Level.W, Level.H);
            LastRun = null;
            EnterPacking();
        }

        public void EnterPacking()
        {
            Phase = Phase.Packing;
            Journey.Stop();
            Reveal.Hide();
            Post.SetDof(0f, 2f);
            Station.gameObject.SetActive(true);
            Station.SetupFor(Level);
            Packing.Begin(Level, CurrentPacking, LastRun);
            Hud.ShowPacking(Level);
            Tutorial.MaybeStart(Level);
        }

        public void SealAndShip()
        {
            if (Phase != Phase.Packing) return;
            if (!CurrentPacking.AllItemsPlaced(Level)) return;
            StartCoroutine(SealRoutine());
        }

        IEnumerator SealRoutine()
        {
            Phase = Phase.Sealing;
            Packing.End();
            Hud.ShowSealing();
            Save.SetPacking(Level, CurrentPacking);
            // simulate on a worker thread while the flaps close and the tape goes on
            var lvl = Level;
            var pk = CurrentPacking.Clone();
            _ = lvl.Kinematics;
            var task = System.Threading.Tasks.Task.Run(() => Simulator.Run(lvl, pk));
            var box = Station.Box;
            box.ShowGrid(false);
            box.SetTapeStyle(Save.Tape);
            box.SetFlaps(1f, false);
            yield return new WaitForSeconds(0.8f);
            // the tape gun sweeps across the top as the tape goes down
            var gun = ModelLibrary.Spawn("tapegun", box.transform);
            box.SetTape(1f, false);
            float t = 0;
            while (t < 0.95f)
            {
                t += Time.deltaTime;
                if (gun != null)
                {
                    var pos = box.TapeEndWorld + Vector3.up * 0.004f;   // rides the end of the tape
                    if (t > 0.75f) pos += Vector3.up * (t - 0.75f) * 1.6f;
                    gun.transform.position = pos;
                    gun.transform.rotation = Quaternion.Euler(0, 0, -18f + Mathf.Sin(t * 30f) * 2f);
                    gun.transform.localScale = Vector3.one * (1.7f * Mathf.Clamp01(t * 6f));   // hand-sized next to the box
                }
                yield return null;
            }
            if (gun != null) Destroy(gun);
            Rig.AddTrauma(0.15f);
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) { Debug.LogException(task.Exception); EnterPacking(); yield break; }
            var rec = task.Result;
            LastRun = rec;
            Phase = Phase.Journey;
            Hud.ShowJourney(Level, rec);
            Journey.Play(rec, Station.Box, OnJourneyDone);
        }

        void OnJourneyDone()
        {
            Save.Record(Level, LastRun.Outcome);
            if (SkipReveal) { ShowResultsNow(); return; }
            Phase = Phase.Reveal;
            Hud.ShowReveal();
            // seen this unboxing before? play it brisker (the dragon egg finale always plays in full)
            Reveal.Quick = (Save.Get(Level.Number)?.Attempts ?? 0) > 1 && System.Array.IndexOf(Level.Items, PieceKind.DragonEgg) < 0;
            StartCoroutine(Reveal.Play(LastRun, Station.Box, ShowResultsNow));
        }

        void ShowResultsNow()
        {
            Phase = Phase.Results;
            Hud.ShowResults(Level, LastRun);
        }

        void OnReplayDone()
        {
            Phase = Phase.Results;
            Hud.ShowResults(Level, LastRun);
        }

        public void Repack()
        {
            Journey.Stop();
            EnterPacking();
        }

        public void NextLevel()
        {
            int n = Level.Number + 1;
            // the story ends at The Dragon Egg (credits, then Overtime opens); Overtime ends with credits too
            if (n > Levels.All.Count || Levels.Get(n).Chapter > Levels.StoryChapters && Level.Chapter <= Levels.StoryChapters)
            {
                ShowTitle();
                Menus.ShowCreditsFinale(n <= Levels.All.Count);
                return;
            }
            StartLevel(n);
        }

        public void Replay()
        {
            if (LastRun == null) return;
            Reveal.Hide();
            // the unboxing left the flaps open and the tape cut: the trip itself was sealed
            Station.Box.SetFlaps(1f, true);
            Station.Box.SetTape(1f, true);
            Post.SetDof(0f, 2f);
            Phase = Phase.Journey;
            Hud.ShowJourney(Level, LastRun, true);
            Journey.Play(LastRun, Station.Box, OnReplayDone, true);
        }
    }
}
