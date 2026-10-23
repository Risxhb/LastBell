using System;
using LastBell.Run;
using UnityEngine;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // All environments belong to one menu scene. Rotation creates no gameplay or music owners.
    public sealed class MenuShowcase : MonoBehaviour
    {
        public const float StageSeconds = 15, WipeHalfSeconds = .8f;
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
        public bool Wiping => building && building.gameObject.activeSelf;
        public bool CoversScreen => Wiping && Mathf.Abs(building.anchoredPosition.x) <= (building.sizeDelta.x - canvasRect.rect.width) * .5f;
        MenuBattle battle;
        RectTransform building, canvasRect;
        void Start()
        {
            battle = GetComponent<MenuBattle>();
            if (Stages == null || Stages.Length != RunMaps.All.Length)
            { Debug.LogError("Menu showcase environments are missing. Run Prepare Showcase Scene."); enabled = false; return; }
            BuildWipe();
            battle.CameraPosition = new Vector3(0, 29, 24); battle.CameraTarget = new Vector3(0, 1, 0);
            battle.FieldOfView = 44; battle.OrbitDegreesPerSecond = .35f;
            Activate(0); MenuShowcaseTest.Install();
        }
        void Update()
        {
            Elapsed += Time.unscaledDeltaTime;
            float next = (Changes + 1) * StageSeconds;
            float distance = Elapsed - next;
            bool leaving = Elapsed - Changes * StageSeconds < WipeHalfSeconds && Changes > 0;
            bool entering = distance >= -WipeHalfSeconds;
            building.gameObject.SetActive(entering || leaving);
            float width = canvasRect.rect.width, height = canvasRect.rect.height;
            building.sizeDelta = new Vector2(width * 1.5f, height * 1.18f);
            float travel = width * 1.3f;
            float x = entering ? Mathf.Lerp(-travel, 0, Mathf.Clamp01(1 + distance / WipeHalfSeconds))
                : Mathf.Lerp(0, travel, Mathf.Clamp01((Elapsed - Changes * StageSeconds) / WipeHalfSeconds));
            building.anchoredPosition = new Vector2(x, 0);
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
        void BuildWipe()
        {
            var root = new GameObject("Foreground architecture wipe", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -10;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            canvasRect = (RectTransform)root.transform;
            building = Panel("Passing clocktower facade", root.transform, new Color(.035f,.048f,.075f), new Vector2(.5f,.5f), Vector2.zero, Vector2.zero);
            // Full-height masonry occludes the arena at every aspect ratio; the menu renders above it.
            for (int i = 0; i < 12; i++)
            {
                float x = (i + .5f) / 12;
                Panel("Stone pier", building, new Color(.065f,.075f,.095f), new Vector2(x,.5f), Vector2.zero, new Vector2(34,1800));
                Panel("Brass edge", building, new Color(.26f,.19f,.105f), new Vector2(x,.5f), new Vector2(-18,0), new Vector2(4,1800));
                for (int row = 0; row < 4; row++)
                {
                    var window = Panel("Recessed lancet window", building, new Color(.012f,.02f,.04f), new Vector2(x,.18f + row * .21f), new Vector2(66,0), new Vector2(58,130));
                    Panel("Warm glass", window, new Color(.43f,.29f,.12f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(35,104));
                    Panel("Window mullion", window, new Color(.055f,.06f,.08f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(6,110));
                }
            }
            for (int row = 0; row < 5; row++)
                Panel("Projecting cornice", building, new Color(.12f,.125f,.14f), new Vector2(.5f,row * .235f + .035f), Vector2.zero, new Vector2(9000,18));
            building.gameObject.SetActive(false);
        }
        static RectTransform Panel(string name, Transform parent, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return rect;
        }
        public void Stop()
        {
            enabled = false; if (building) building.gameObject.SetActive(false);
            if (battle) battle.enabled = false;
        }
    }
}
