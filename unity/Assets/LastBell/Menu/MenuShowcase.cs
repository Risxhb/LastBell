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
        }
        public Stage[] Stages;
        public int CurrentStage { get; private set; }
        public int Changes { get; private set; }
        public float Elapsed { get; private set; }
        public float LastChangeTime { get; private set; }
        public bool LastChangeCovered { get; private set; }
        public bool Wiping => veil && veil.color.a > 0;
        public bool CoversScreen => veil && veil.color.a >= .99f;
        MenuBattle battle;
        float startedAt = -1;
        Image veil;
        void Start()
        {
            battle = GetComponent<MenuBattle>();
            if (Stages == null || Stages.Length != RunMaps.Showcased.Length)
            { Debug.LogError("Menu showcase environments are missing. Run Prepare Showcase Scene."); enabled = false; return; }
            BuildFade();
            battle.CameraPosition = new Vector3(0, 29, 24); battle.CameraTarget = new Vector3(0, 1, 0);
            battle.FieldOfView = 44; battle.OrbitDegreesPerSecond = 1.2f;
            Activate(0); MenuShowcaseTest.Install();
        }
        void Update()
        {
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
