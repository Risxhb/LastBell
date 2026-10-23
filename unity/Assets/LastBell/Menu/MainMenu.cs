using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    public sealed class MainMenu : MonoBehaviour
    {
        public const string ScenePath = "Assets/LastBell/Menu/ShowcaseMainMenu.unity";
        public bool SettingsOpen => settings && settings.activeSelf;
        public bool Loading { get; private set; }
        public Button StartButton { get; private set; }
        public Button TestRushButton { get; private set; }
        public Button SettingsButton { get; private set; }
        public Button ExitButton { get; private set; }
        public Button CloseButton { get; private set; }
        public Button ApplyButton { get; private set; }
        public Dropdown ResolutionControl { get; private set; }
        public Dropdown ModeControl { get; private set; }
        public Dropdown QualityControl { get; private set; }
        public Slider MusicVolumeControl { get; private set; }
        public MenuMusic Music { get; private set; }
        public Image Logo { get; private set; }
        public MenuBattle Battle { get; private set; }
        RectTransform safeRoot;
        GameObject settings, bar;
        CanvasGroup barGroup;
        Image fade;
        Text status;
        Font font;
        Vector2Int[] resolutions;
        Texture2D gradient;
        Sprite gradientSprite;
        readonly List<Button> primary = new List<Button>();
        Rect lastSafe;
        static readonly Color Ink = new Color(0.025f, 0.035f, 0.065f), Gold = new Color(0.88f, 0.72f, 0.43f), Ivory = new Color(0.96f, 0.92f, 0.82f);

        void Awake()
        {
            MenuLaunch.Clear();
            MenuReturnBridge.TestSession=false;
            if(MapPreviewCapture.Requested){MapPreviewCapture.Launch();return;}
            // Existing run diagnostics retain their own entry point and never run underneath a menu overlay.
            if (RunAutoTest.Requested) { SceneManager.LoadScene(RunMaps.All[0].RunScene); return; }
            if (MenuSmokeTest.Requested || MenuFlowTest.Requested) MenuSmokeTest.PrepareInput();
            MenuSettings.Restore();
            Music = MenuMusic.Ensure();
            Music.PlayMenu();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Battle = GetComponent<MenuBattle>();
            if (!Battle) Battle = gameObject.AddComponent<MenuBattle>();
            if (!GetComponent<MenuShowcase>()) gameObject.AddComponent<MenuShowcase>();
            BuildUI();
            if (MenuSmokeTest.Requested) gameObject.AddComponent<MenuSmokeTest>();
            MenuFlowTest.Install();
        }
        void Start() { if (StartButton) EventSystem.current.SetSelectedGameObject(StartButton.gameObject); }
        void BuildUI()
        {
            var canvas = new GameObject("Main menu canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            if (!EventSystem.current)
            {
                var events = new GameObject("Menu navigation", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.AddComponent<MenuInput>().Bind(events.GetComponent<InputSystemUIInputModule>());
            }
            var root = canvas.transform;
            gradient = new Texture2D(1, 128, TextureFormat.RGBA32, false) { name = "Menu contrast gradient", wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 128; i++) gradient.SetPixel(0, i, new Color(Ink.r, Ink.g, Ink.b, Mathf.Pow(1 - i / 127f, 2) * 0.98f));
            gradient.Apply(); gradientSprite = Sprite.Create(gradient, new Rect(0, 0, 1, 128), new Vector2(0.5f, 0.5f));
            var bottom = Image("Lower contrast", root, Color.white); bottom.sprite = gradientSprite;
            Bounds(bottom.rectTransform, new Vector2(0, 0), new Vector2(1, 0.44f));
            var top = Image("Title contrast", root, Color.white); top.sprite = gradientSprite;
            Bounds(top.rectTransform, new Vector2(0, 0.71f), Vector2.one); top.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            safeRoot = Rect("Safe area", root); Bounds(safeRoot, Vector2.zero, Vector2.one);
            var logoSprite = Resources.Load<Sprite>("Menu/LastBellLogo");
            if (logoSprite)
            {
                Logo = Image("Last Bell logo", safeRoot, Color.white); Logo.sprite = logoSprite; Logo.preserveAspect = true;
                Place(Logo.rectTransform, new Vector2(0.5f, 0.855f), new Vector2(820, 273));
            }
            else Text("L A S T   B E L L", safeRoot, 86, Ivory, new Vector2(0.5f, 0.849f), new Vector2(1050, 116));
            Text("THE HOUR IS YOURS TO BREAK", safeRoot, 14, Gold, new Vector2(0.5f, 0.705f), new Vector2(850, 30));
            bar = Rect("Bottom navigation", safeRoot).gameObject;
            Bounds((RectTransform)bar.transform, new Vector2(0.5f, 0.065f), new Vector2(0.5f, 0.065f));
            ((RectTransform)bar.transform).sizeDelta = new Vector2(1520, 80);
            barGroup = bar.AddComponent<CanvasGroup>();
            StartButton = Button("Start Run", bar.transform, new Vector2(-555, 0), new Vector2(330, 68), StartRun, true);
            TestRushButton = Button("Boss-Rush Test Rush", bar.transform, new Vector2(-185, 0), new Vector2(350, 68), () => OpenSelection(true));
            SettingsButton = Button("Settings", bar.transform, new Vector2(185, 0), new Vector2(330, 68), OpenSettings);
            ExitButton = Button("Exit", bar.transform, new Vector2(555, 0), new Vector2(330, 68), Exit);
#if UNITY_WEBGL && !UNITY_EDITOR
            ExitButton.GetComponentInChildren<Text>().text = "Restart Menu";
#endif
            primary.AddRange(new[] { StartButton, TestRushButton, SettingsButton, ExitButton });
            LinkHorizontal(primary);
            Text("VANGUARD  /  ARCANIST  /  CHIMESINGER", safeRoot, 13, new Color(0.73f, 0.73f, 0.78f), new Vector2(0.5f, 0.168f), new Vector2(900, 28));
            Text("LAST BELL  /  WORK IN PROGRESS", safeRoot, 11, new Color(0.6f, 0.61f, 0.69f), new Vector2(0.5f, 0.016f), new Vector2(900, 24));
            BuildSettings(safeRoot);
            fade = Image("Scene transition", root, new Color(Ink.r, Ink.g, Ink.b, 0));
            Bounds(fade.rectTransform, Vector2.zero, Vector2.one); fade.raycastTarget = false;
        }
        void BuildSettings(Transform root)
        {
            var blocker = Image("Settings overlay", root, new Color(0.01f, 0.015f, 0.035f, 0.68f));
            Bounds(blocker.rectTransform, Vector2.zero, Vector2.one); blocker.raycastTarget = true; settings = blocker.gameObject;
            var panel = Image("Settings panel", blocker.transform, new Color(0.035f, 0.045f, 0.075f, 0.98f));
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(720, 730));
            Rule(panel.transform, new Vector2(0.5f, 1), new Vector2(720, 2), Gold);
            Text("S E T T I N G S", panel.transform, 30, Ivory, new Vector2(0.5f, 0.90f), new Vector2(620, 60));
            Text("AUDIO & DISPLAY", panel.transform, 13, Gold, new Vector2(0.5f, 0.82f), new Vector2(600, 25));
            MusicVolumeControl = VolumeSlider(panel.transform, 0.70f);
            ModeControl = Dropdown("Display mode", panel.transform, 0.58f, new[] { "Windowed", "Borderless" });
            ResolutionControl = Dropdown("Resolution", panel.transform, 0.46f, new string[0]);
            QualityControl = Dropdown("Quality", panel.transform, 0.34f, QualitySettings.names);
            status = Text("Music volume updates immediately.", panel.transform, 13, Gold, new Vector2(0.5f, 0.23f), new Vector2(640, 35));
            ApplyButton = Button("Apply", panel.transform, new Vector2(-158, -285), new Vector2(276, 54), ApplySettings, true);
            CloseButton = Button("Back", panel.transform, new Vector2(158, -285), new Vector2(276, 54), CloseSettings);
            // Explicit circular focus order traps keyboard/gamepad navigation inside the modal.
            Selectable[] order = { MusicVolumeControl, ModeControl, ResolutionControl, QualityControl, ApplyButton, CloseButton };
            for (int i = 0; i < order.Length; i++)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = order[(i + order.Length - 1) % order.Length], selectOnDown = order[(i + 1) % order.Length],
                    selectOnLeft = i == 4 ? CloseButton : null, selectOnRight = i == 5 ? ApplyButton : null };
                order[i].navigation = navigation;
            }
            settings.SetActive(false);
        }
        public void OpenSettings()
        {
            if (Loading) return;
            resolutions = MenuSettings.Resolutions;
            ResolutionControl.ClearOptions(); ResolutionControl.AddOptions(resolutions.Select(r => r.x + " × " + r.y).ToList());
            ResolutionControl.value = Mathf.Max(0, Array.FindIndex(resolutions, r => r.x == Screen.width && r.y == Screen.height));
            ModeControl.value = MenuSettings.DisplayMode; QualityControl.value = QualitySettings.GetQualityLevel();
            MusicVolumeControl.SetValueWithoutNotify(Music.Volume);
            status.text = "Music volume updates immediately.";
#if UNITY_WEBGL && !UNITY_EDITOR
            ModeControl.interactable = false; ResolutionControl.interactable = false;
            status.text = "Music is live. Use the page's Fullscreen button for display size.";
#endif
            settings.SetActive(true); barGroup.interactable = false; barGroup.blocksRaycasts = false;
            EventSystem.current.SetSelectedGameObject(MusicVolumeControl.gameObject);
        }
        public void CloseSettings()
        {
            // Legacy Dropdown owns its temporary list; close it before hiding the parent.
            ModeControl.Hide(); ResolutionControl.Hide(); QualityControl.Hide();
            settings.SetActive(false); barGroup.interactable = true; barGroup.blocksRaycasts = true;
            PlayerPrefs.Save();
            EventSystem.current.SetSelectedGameObject(SettingsButton.gameObject);
        }
        public void ApplySettings()
        {
            if (!SettingsOpen || resolutions == null || resolutions.Length == 0) return;
            var r = resolutions[ResolutionControl.value];
            MenuSettings.Apply(r.x, r.y, ModeControl.value, QualityControl.value);
            status.text = "Display settings saved.";
        }
        public void StartRun()
        { OpenSelection(false); }
        public void OpenSelection(bool testRush)
        {
            if (Loading || SettingsOpen) return;
            Loading = true; barGroup.interactable = false; fade.raycastTarget = true;
            var showcase = GetComponent<MenuShowcase>(); if (showcase) showcase.Stop();
            HeroSelection.TestRush = testRush;
            HeroSelection.ResumeSetup = false;
            StartButton.GetComponentInChildren<Text>().text = "Preparing…";
            StartCoroutine(EnterRun());
        }
        IEnumerator EnterRun()
        {
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime)
            { fade.color = new Color(Ink.r, Ink.g, Ink.b, Mathf.Clamp01(t / 0.4f)); yield return null; }
            var load = SceneManager.LoadSceneAsync(HeroSelection.ScenePath);
            while (!load.isDone) yield return null;
        }
        public void Exit()
        {
            if (Loading || SettingsOpen) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            SceneManager.LoadScene(ScenePath);
#else
            Application.Quit();
#endif
        }
        void Update()
        {
            if (!safeRoot) return;
            if (Screen.safeArea != lastSafe)
            {
                lastSafe = Screen.safeArea;
                safeRoot.anchorMin = lastSafe.position / new Vector2(Screen.width, Screen.height);
                safeRoot.anchorMax = (lastSafe.position + lastSafe.size) / new Vector2(Screen.width, Screen.height);
            }
            var keyboard = MenuSmokeTest.Requested || MenuFlowTest.Requested ? MenuSmokeTest.TestKeyboard : Keyboard.current;
            var gamepad = MenuSmokeTest.Requested || MenuFlowTest.Requested ? MenuSmokeTest.TestGamepad : Gamepad.current;
#if UNITY_WEBGL && !UNITY_EDITOR
            gamepad = null;
#endif
            bool cancel = keyboard?.escapeKey.wasPressedThisFrame == true || gamepad?.buttonEast.wasPressedThisFrame == true;
            if (cancel && SettingsOpen)
            {
                // First Back closes an open dropdown; the next closes the settings modal.
                if (!DropdownOpen(ModeControl) && !DropdownOpen(ResolutionControl) && !DropdownOpen(QualityControl)) CloseSettings();
            }
            bool nav = keyboard?.tabKey.wasPressedThisFrame == true || gamepad?.dpad.ReadValue().sqrMagnitude > 0.1f;
            if (nav && !EventSystem.current.currentSelectedGameObject)
                EventSystem.current.SetSelectedGameObject(SettingsOpen ? MusicVolumeControl.gameObject : StartButton.gameObject);
        }
        void OnDestroy() { if (gradientSprite) Destroy(gradientSprite); if (gradient) Destroy(gradient); }
        public static bool DropdownOpen(Dropdown dropdown) => dropdown.transform.Find("Dropdown List") != null;
        static void LinkHorizontal(IList<Button> buttons)
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnLeft = buttons[(i + buttons.Count - 1) % buttons.Count], selectOnRight = buttons[(i + 1) % buttons.Count],
                selectOnUp = buttons[i], selectOnDown = buttons[i] };
        }
        Button Button(string label, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action, bool accent = false)
        {
            var image = Image(label, parent, accent ? new Color(0.35f, 0.27f, 0.15f, 0.94f) : new Color(0.045f, 0.06f, 0.095f, 0.94f));
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), size); image.rectTransform.anchoredPosition = position; image.raycastTarget = true;
            var border = image.gameObject.AddComponent<Outline>(); border.effectColor = new Color(Gold.r, Gold.g, Gold.b, accent ? 0.9f : 0.4f); border.effectDistance = Vector2.one;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.5f, 1.4f, 1.15f);
            colors.selectedColor = new Color(1.65f, 1.5f, 1.2f); colors.pressedColor = new Color(0.7f, 0.64f, 0.5f); colors.disabledColor = new Color(0.4f, 0.4f, 0.4f);
            colors.fadeDuration = 0.1f; colors.colorMultiplier = 1; button.colors = colors;
            Text(label, image.transform, 22, Ivory, new Vector2(0.5f, 0.5f), size - new Vector2(12, 4));
            // A shape cue supplements selected colour for keyboard/controller users.
            var marker = Text("◆", image.transform, 13, Gold, new Vector2(0.08f, 0.5f), new Vector2(20, 20));
            var focus = image.gameObject.AddComponent<MenuFocus>(); focus.Marker = marker.gameObject;
            button.onClick.AddListener(action); return button;
        }
        Slider VolumeSlider(Transform parent, float y)
        {
            var label = Text("Music Volume", parent, 18, Ivory, new Vector2(0.25f, y), new Vector2(220, 44)); label.alignment = TextAnchor.MiddleLeft;
            var background = Image("Main menu music volume", parent, new Color(0.1f, 0.12f, 0.18f));
            Place(background.rectTransform, new Vector2(0.66f, y), new Vector2(320, 48)); background.raycastTarget = true;
            var track = Rect("Volume track", background.transform); Bounds(track, new Vector2(0.05f, 0.43f), new Vector2(0.78f, 0.57f));
            var fill = Image("Volume fill", track, Gold); Bounds(fill.rectTransform, Vector2.zero, Vector2.one);
            var handleArea = Rect("Volume handle area", background.transform); Bounds(handleArea, new Vector2(0.05f, 0), new Vector2(0.78f, 1));
            var handle = Image("Volume handle", handleArea, Ivory); Place(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(12, 28)); handle.raycastTarget = true;
            var slider = background.gameObject.AddComponent<Slider>(); slider.targetGraphic = handle; slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.minValue = 0; slider.maxValue = 1; slider.value = Music.Volume;
            var percent = Text(Mathf.RoundToInt(Music.Volume * 100) + "%", background.transform, 16, Gold, new Vector2(0.90f, 0.5f), new Vector2(60, 40));
            slider.onValueChanged.AddListener(value => { Music.SetVolume(value); percent.text = Mathf.RoundToInt(value * 100) + "%"; });
            return slider;
        }
        Dropdown Dropdown(string label, Transform parent, float y, string[] options)
        {
            var caption = Text(label, parent, 18, Ivory, new Vector2(0.25f, y), new Vector2(220, 44)); caption.alignment = TextAnchor.MiddleLeft;
            var image = Image(label + " control", parent, new Color(0.1f, 0.12f, 0.18f)); Place(image.rectTransform, new Vector2(0.66f, y), new Vector2(320, 48)); image.raycastTarget = true;
            var drop = image.gameObject.AddComponent<Dropdown>(); drop.targetGraphic = image;
            var selected = Text("", image.transform, 18, Ivory, new Vector2(0.47f, 0.5f), new Vector2(260, 44));
            Text("▾", image.transform, 18, Gold, new Vector2(0.93f, 0.5f), new Vector2(24, 40)); drop.captionText = selected;
            var template = Image("Template", image.transform, new Color(0.07f, 0.09f, 0.14f));
            template.rectTransform.anchorMin = new Vector2(0, 0); template.rectTransform.anchorMax = new Vector2(1, 0);
            template.rectTransform.pivot = new Vector2(0.5f, 1); template.rectTransform.sizeDelta = new Vector2(0, 220);
            template.raycastTarget = true;
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Image("Viewport", template.transform, Color.white); Bounds(viewport.rectTransform, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport.transform); Bounds(content, new Vector2(0, 1), Vector2.one); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = new Vector2(0, 44);
            var item = Image("Item", content, new Color(0.12f, 0.15f, 0.22f));
            Bounds(item.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f)); item.rectTransform.sizeDelta = new Vector2(0, 44); item.raycastTarget = true;
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item;
            var check = Text("◆", item.transform, 12, Gold, new Vector2(0.06f, 0.5f), new Vector2(24, 40)); toggle.graphic = check;
            var itemText = Text("Option", item.transform, 17, Ivory, new Vector2(0.54f, 0.5f), new Vector2(265, 40)); itemText.alignment = TextAnchor.MiddleLeft;
            drop.template = template.rectTransform; drop.itemText = itemText;
            scroll.viewport = viewport.rectTransform; scroll.content = content;
            drop.AddOptions(options.ToList()); template.gameObject.SetActive(false); return drop;
        }
        static RectTransform Rect(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
        static Image Image(string name, Transform parent, Color color)
        { var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        Text Text(string text, Transform parent, int size, Color color, Vector2 anchor, Vector2 bounds)
        {
            var rect = Rect(text.Length > 32 ? text.Substring(0, 32) : text, parent); Place(rect, anchor, bounds);
            var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.text = text; label.fontSize = size;
            label.color = color; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
        }
        static void Bounds(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size; }
        static void Rule(Transform parent, Vector2 anchor, Vector2 size, Color color)
        { var image = Image("Brass rule", parent, color); Place(image.rectTransform, anchor, size); }
    }

    public sealed class MenuFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject Marker;
        bool selected, hovered;
        void Start() { Refresh(); }
        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        void Refresh() { if (Marker) Marker.SetActive(selected || hovered); }
    }

    // Only installed after entering the run from this menu. Direct run-scene diagnostics stay untouched.
    public sealed class MenuReturnBridge : MonoBehaviour
    {
        public static bool TestSession;
        static bool returnToSetup;
        static MenuReturnBridge active;
        RunDirector observed;
        public static void Ensure()
        {
            if (active) return;
            var go = new GameObject("Main menu return routing"); DontDestroyOnLoad(go); active = go.AddComponent<MenuReturnBridge>();
        }
        public static bool Return(bool setup)
        {
            if(!RunDirector.Instance||RunDirector.Instance.State==RunState.Menu)return false;returnToSetup=setup&&TestSession;
            if(RunDirector.Instance.ReturnToMenu())return true;returnToSetup=false;return false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession(){TestSession=false;returnToSetup=false;active=null;}
        void Update()
        {
            if (observed == RunDirector.Instance) return;
            if (observed) observed.StateChanged -= OnState;
            observed = RunDirector.Instance;
            if (observed) observed.StateChanged += OnState;
        }
        void OnState(RunState state)
        { if (state == RunState.Menu) StartCoroutine(Return()); }
        IEnumerator Return()
        {
            yield return null;bool setup=returnToSetup;returnToSetup=false;MenuLaunch.Clear();
            if(setup){HeroSelection.TestRush=true;HeroSelection.ResumeSetup=true;}
            SceneManager.LoadSceneAsync(setup?HeroSelection.ScenePath:MainMenu.ScenePath);
        }
        void OnDestroy() { if (observed) observed.StateChanged -= OnState; if (active == this) active = null; }
    }
}
