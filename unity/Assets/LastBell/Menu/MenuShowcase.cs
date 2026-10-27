using System;
using LastBell.Run;
using UnityEngine;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // All environments belong to one menu scene. Rotation creates no gameplay or music owners.
    public sealed class MenuShowcase : MonoBehaviour
    {
        public const float StageSeconds = 15, WipeHalfSeconds = .55f;
        [Serializable] public sealed class Stage
        {
            public string Name, Boss;
            public GameObject Environment;
            public int PartySize;
            public Color Ambient, Fog, Background;
            public bool FogEnabled;
            public float FogStart, FogEnd, AmbientIntensity;
            public FogMode FogMode;
            public Material Skybox;
            // Optional orbit framing for this stage. Off: the shared default framing below (the original five stages).
            public bool CustomCamera;
            public Vector3 CameraTarget, CameraPosition;   // orbit centre and the camera's position at orbit angle 0
            public float FieldOfView, OrbitDegreesPerSecond;
            public Vector2 ScreenOffsetDegrees;            // yaw, pitch after looking at the target: moves the arena off-centre
        }
        static readonly Vector3 DefaultCameraPosition = new Vector3(0, 29, 24), DefaultCameraTarget = new Vector3(0, 1, 0);
        const float DefaultFieldOfView = 44, DefaultOrbitDegreesPerSecond = 1.2f;
        public Stage[] Stages;
        public int CurrentStage { get; private set; }
        public int Changes { get; private set; }
        public float Elapsed { get; private set; }
        public float LastChangeTime { get; private set; }
        public bool LastChangeCovered { get; private set; }
        public bool Wiping => veil && veil.color.a > 0;
        public bool CoversScreen => veil && veil.color.a >= .99f;
        // Manual: Boss Rush select drives the stage instead of the 15 s rotation.
        public bool Manual;
        public bool Started { get; private set; }
        MenuBattle battle;
        float startedAt = -1, manualWipe;
        int requested = -1;
        Image veil;
        void Start()
        {
            battle = GetComponent<MenuBattle>();
            if (Stages == null || Stages.Length != RunMaps.Showcased.Length)
            { Debug.LogError("Menu showcase environments are missing. Run Prepare Showcase Scene."); enabled = false; return; }
            // Boss Rush is hosted by the main menu (ConfrontationMenu.Rush); the old hero-select host is retired.
            var host = Manual ? FindAnyObjectByType<ConfrontationMenu>() : null;
            if (Manual && !(host && host.Rush)) { UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(gameObject.scene); enabled = false; return; } // Boss Rush select already gone
            if (Manual) UnityEngine.SceneManagement.SceneManager.SetActiveScene(gameObject.scene); // stage lighting stays in this scene
            BuildFade();
            if (Manual) { Activate(requested >= 0 ? requested : 0); requested = -1; Started = true; return; }
            Activate(0); MenuShowcaseTest.Install(); Started = true;
        }
        public void Show(int stage) { if (Stages != null && stage >= 0 && stage < Stages.Length) requested = stage; }
        void ManualUpdate()
        {
            // Fade through midnight, swap the arena while covered, then fade back in.
            bool changing = requested >= 0 && requested != CurrentStage;
            if (!changing) requested = -1;
            manualWipe = Mathf.MoveTowards(manualWipe, changing ? 1 : 0, Time.unscaledDeltaTime / WipeHalfSeconds);
            if (changing && manualWipe >= 1) { LastChangeCovered = true; Changes++; Activate(requested); requested = -1; }
            veil.color = new Color(.008f, .012f, .025f, Mathf.SmoothStep(0, 1, manualWipe));
        }
        void Update()
        {
            if (Manual) { ManualUpdate(); return; }
            // Start after all scene Start callbacks (including cast preparation), so
            // cold asset loading is not counted as time already spent on the menu.
            if (startedAt < 0) { startedAt = Time.realtimeSinceStartup; return; }
            Elapsed = Time.realtimeSinceStartup - startedAt;
            float next = (Changes + 1) * StageSeconds;
            float distance = Elapsed - next;
            bool leaving = Elapsed - Changes * StageSeconds < WipeHalfSeconds && Changes > 0;
            bool entering = distance >= -WipeHalfSeconds;
            float alpha = entering ? Mathf.Clamp01(1 + distance / WipeHalfSeconds)
                : leaving ? 1 - Mathf.Clamp01((Elapsed - Changes * StageSeconds) / WipeHalfSeconds) : 0;
            veil.color = new Color(.008f, .012f, .025f, Mathf.SmoothStep(0, 1, alpha));
            if (Elapsed >= next)
            {
                LastChangeCovered = CoversScreen; LastChangeTime = Elapsed;
                Changes++; Activate(Changes % Stages.Length);
            }
        }
        void Activate(int index)
        {
            CurrentStage = index;
            for (int i = 0; i < Stages.Length; i++) Stages[i].Environment.SetActive(i == index);
            var stage = Stages[index];
            RenderSettings.ambientLight = stage.Ambient; RenderSettings.ambientIntensity = stage.AmbientIntensity;
            RenderSettings.fog = stage.FogEnabled; RenderSettings.fogColor = stage.Fog;
            RenderSettings.fogMode = stage.FogMode; RenderSettings.fogStartDistance = stage.FogStart; RenderSettings.fogEndDistance = stage.FogEnd;
            RenderSettings.skybox = stage.Skybox;
            if (Camera.main) Camera.main.backgroundColor = stage.Background;
            bool custom = stage.CustomCamera;
            battle.CameraPosition = custom ? stage.CameraPosition : DefaultCameraPosition;
            battle.CameraTarget = custom ? stage.CameraTarget : DefaultCameraTarget;
            battle.FieldOfView = custom ? stage.FieldOfView : DefaultFieldOfView;
            battle.OrbitDegreesPerSecond = custom ? stage.OrbitDegreesPerSecond : DefaultOrbitDegreesPerSecond;
            battle.ScreenOffsetDegrees = custom ? stage.ScreenOffsetDegrees : Vector2.zero;
            battle.SetStage(index, stage.Boss, stage.PartySize);
        }
        void BuildFade()
        {
            var root = new GameObject("Arena dissolve canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -10;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            var go = new GameObject("Arena fade through midnight", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root.transform, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            veil = go.GetComponent<Image>(); veil.color = Color.clear; veil.raycastTarget = false;
        }
        public void Stop()
        {
            enabled = false; if (veil) veil.color = Color.clear;
            if (battle) battle.enabled = false;
        }
    }
}
