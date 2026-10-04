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
        public Phase Phase;

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
        }

        void Start()
        {
            if (AutoPilot.TryStart(this)) Save = new SaveData();
            StartLevel(Mathf.Clamp(Save.LastLevel, 1, Levels.All.Count));
        }

        // ---- Flow ---------------------------------------------------------------------------------

        public void StartLevel(int number)
        {
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
            Station.gameObject.SetActive(true);
            Station.SetupFor(Level);
            Packing.Begin(Level, CurrentPacking, LastRun);
            Hud.ShowPacking(Level);
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
            var rec = Simulator.Run(Level, CurrentPacking);
            var box = Station.Box;
            box.ShowGrid(false);
            box.SetFlaps(1f, false);
            yield return new WaitForSeconds(0.8f);
            box.SetTape(1f, false);
            yield return new WaitForSeconds(0.9f);
            LastRun = rec;
            Phase = Phase.Journey;
            Hud.ShowJourney(Level, rec);
            Journey.Play(rec, Station.Box, OnJourneyDone);
        }

        void OnJourneyDone()
        {
            Phase = Phase.Results;
            Save.Record(Level, LastRun.Outcome);
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
            if (n > Levels.All.Count) n = 1;
            StartLevel(n);
        }

        public void Replay()
        {
            if (LastRun == null) return;
            Phase = Phase.Journey;
            Hud.ShowJourney(Level, LastRun);
            Journey.Play(LastRun, Station.Box, OnJourneyDone);
        }
    }
}
