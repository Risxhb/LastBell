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
        public bool SettingsOpen => (settings && settings.activeSelf) || (UISettings && UISettings.IsOpen);
        public Button UISettingsButton { get; private set; }
        public MenuAppearancePanel UISettings { get; private set; }
        public bool Loading { get; private set; }
        public Button StartButton { get; private set; }
        public Button TestRushButton { get; private set; }
        public Button SettingsButton { get; private set; }
        public Button ExitButton { get; private set; }
        public Button MusicPlayButton { get; private set; }
        public Button MusicPauseButton { get; private set; }
        public Button MusicNextButton { get; private set; }
        Text musicTrack;
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
        Text status, tagline, titleText;
        float titleAlpha = 1;
        Vector2Int[] resolutions;
        Texture2D gradient;
        Sprite gradientSprite;
        readonly List<Button> primary = new List<Button>();
        Rect lastSafe;
        static readonly Color Ink = new Color(0.025f, 0.035f, 0.065f), Gold = new Color(0.88f, 0.72f, 0.43f), Ivory = new Color(0.96f, 0.92f, 0.82f);

        // Handle of the one additive copy Boss Rush select loads as its backdrop: arenas, orbit camera and battle only.
        // Bound to that scene instance, so a real menu load can never be mistaken for the backdrop.
        public static Scene BackdropScene;
        void Awake()
        {
            if(BackdropScene.IsValid()&&gameObject.scene==BackdropScene)
            {
                BackdropScene=default;
                if(!GetComponent<MenuBattle>())gameObject.AddComponent<MenuBattle>();
                var live=GetComponent<MenuShowcase>();if(!live)live=gameObject.AddComponent<MenuShowcase>();live.Manual=true;
                enabled=false;return;
            }
            if(!MenuAppearance.Enter(MenuStyle.Showcase,gameObject))return;
            MenuLaunch.Clear();
            MenuReturnBridge.TestSession=false;
            if(MapPreviewCapture.Requested){MapPreviewCapture.Launch();return;}
            // Existing run diagnostics retain their own entry point and never run underneath a menu overlay.
            if (RunAutoTest.Requested) { SceneManager.LoadScene(RunMaps.All[0].RunScene); return; }
            if (MenuSmokeTest.Requested || MenuFlowTest.Requested) MenuSmokeTest.PrepareInput();
            MenuSettings.Restore();
            Music = MenuMusic.Ensure();
            Music.PlayMenu();
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
            else titleText = Text("L A S T   B E L L", safeRoot, 86, Ivory, new Vector2(0.5f, 0.849f), new Vector2(1050, 116));
            // Tagline: Label role (13 px tracked caps), Ivory 0.85 with shadow, 12 px under the logo's lower edge (y 787 at 1080p).
            tagline = Text(UiTheme.Spaced("THE HOUR IS YOURS TO BREAK"), safeRoot, UiTheme.SizeLabel, new Color(Ivory.r, Ivory.g, Ivory.b, 0.85f), new Vector2(0.5f, 0.7065f), new Vector2(900, 24), true);
            bar = Rect("Bottom navigation", safeRoot).gameObject;
            Bounds((RectTransform)bar.transform, new Vector2(0.5f, 0.065f), new Vector2(0.5f, 0.065f));
            ((RectTransform)bar.transform).sizeDelta = new Vector2(BarFull, 80);
            // Framed row: a quiet Ink well with a 1 px GoldDim edge holds the four actions together.
            var barFill = bar.AddComponent<Image>(); barFill.color = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.55f); barFill.raycastTarget = false;
            var barEdge = bar.AddComponent<Outline>(); barEdge.effectColor = UiTheme.GoldDim; barEdge.effectDistance = Vector2.one;
            barGroup = bar.AddComponent<CanvasGroup>();
            StartButton = Button("Start Run", bar.transform, Vector2.zero, new Vector2(BarWidths[0], 68), StartRun, true);
            TestRushButton = Button("Boss-Rush Test Rush", bar.transform, Vector2.zero, new Vector2(BarWidths[1], 68), () => OpenSelection(true));
            SettingsButton = Button("Settings", bar.transform, Vector2.zero, new Vector2(BarWidths[2], 68), OpenSettings);
            ExitButton = Button("Exit", bar.transform, Vector2.zero, new Vector2(BarWidths[3], 68), Exit);
#if UNITY_WEBGL && !UNITY_EDITOR
            ExitButton.GetComponentInChildren<Text>().text = "Restart Menu";
#endif
            primary.AddRange(new[] { StartButton, TestRushButton, SettingsButton, ExitButton });
            LayoutBar(1);
            LinkHorizontal(primary);
            // Class strip sits on an Ink strip between hairlines so it reads as one quiet line, not floor-level clutter.
            var strip = Image("Class strip", safeRoot, UiTheme.InkStrip); Place(strip.rectTransform, new Vector2(0.5f, 0.168f), new Vector2(560, 30));
            Rule(strip.transform, new Vector2(0.5f, 1), new Vector2(560, 1), UiTheme.GoldDim);
            Rule(strip.transform, new Vector2(0.5f, 0), new Vector2(560, 1), UiTheme.GoldDim);
            Text("VANGUARD  /  ARCANIST  /  CHIMESINGER", strip.transform, UiTheme.SizeLabel, UiTheme.GoldLabel, new Vector2(0.5f, 0.5f), new Vector2(540, 26), true);
            Text("LAST BELL  /  WORK IN PROGRESS", safeRoot, UiTheme.SizeMicro, UiTheme.IvoryMicro, new Vector2(0.5f, 0.016f), new Vector2(900, 22));
            BuildMusicControls();
            BuildSettings(safeRoot);
            fade = Image("Scene transition", root, new Color(Ink.r, Ink.g, Ink.b, 0));
            Bounds(fade.rectTransform, Vector2.zero, Vector2.one); fade.raycastTarget = false;
        }
        // Bottom row geometry at 1080p; scaled down uniformly when the safe area is narrower (1024x768 is 1440 virtual px wide).
        static readonly float[] BarWidths = { 330, 350, 330, 330 };
        const float BarGap = 24, BarPad = 16, BarFull = 330 + 350 + 330 + 330 + 3 * BarGap + 2 * BarPad, ScreenMargin = 32;
        float barScale = -1;
        void LayoutBar(float k)
        {
            if (Mathf.Abs(k - barScale) < 0.001f) return;
            barScale = k;
            ((RectTransform)bar.transform).sizeDelta = new Vector2(BarFull * k, 80);
            float x = -BarFull * k / 2 + BarPad * k;
            for (int i = 0; i < primary.Count; i++)
            {
                var rect = (RectTransform)primary[i].transform; float w = BarWidths[i] * k;
                rect.sizeDelta = new Vector2(w, 68); rect.anchoredPosition = new Vector2(x + w / 2, 0); x += w + BarGap * k;
            }
        }
        void BuildMusicControls()
        {
            var panel = Image("Music player", safeRoot, UiTheme.Ink);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-ScreenMargin, 128); rect.sizeDelta = new Vector2(300, 92);
            Frame(panel); MenuUI.Ornaments(rect, 8, 2, 2);
            musicTrack = Text(Music.TrackTitle, rect, UiTheme.SizeNumeric, Gold, new Vector2(.5f, .77f), new Vector2(276, 26), true);
            musicTrack.resizeTextForBestFit = true; musicTrack.resizeTextMinSize = UiTheme.SizeMicro; musicTrack.resizeTextMaxSize = UiTheme.SizeNumeric;
            MusicPlayButton = Button("Play", rect, new Vector2(-94, -18), new Vector2(84, 36), () => Music.ResumeTrack(), false, UiTheme.SizeNumeric);
            MusicPauseButton = Button("Pause", rect, new Vector2(0, -18), new Vector2(84, 36), () => Music.PauseTrack(), false, UiTheme.SizeNumeric);
            MusicNextButton = Button("Next", rect, new Vector2(94, -18), new Vector2(84, 36), () => Music.NextTrack(), false, UiTheme.SizeNumeric);
            LinkHorizontal(new[] { MusicPlayButton, MusicPauseButton, MusicNextButton });
            foreach (var button in primary)
            {
                var navigation = button.navigation; navigation.selectOnUp = MusicPlayButton; button.navigation = navigation;
            }
            foreach (var button in new[] { MusicPlayButton, MusicPauseButton, MusicNextButton })
            {
                var navigation = button.navigation; navigation.selectOnDown = ExitButton; button.navigation = navigation;
            }
        }
        void BuildSettings(Transform root)
        {
            var blocker = Image("Settings overlay", root, new Color(0.01f, 0.015f, 0.035f, 0.68f));
            Bounds(blocker.rectTransform, Vector2.zero, Vector2.one); blocker.raycastTarget = true; settings = blocker.gameObject;
            var panel = Image("Settings panel", blocker.transform, UiTheme.InkDeep);
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(720, 730));
            Frame(panel); MenuUI.Ornaments(panel.transform);
            // 3 px gold rule on the modal header only.
            Rule(panel.transform, new Vector2(0.5f, 1), new Vector2(720, 3), Gold).rectTransform.pivot = new Vector2(0.5f, 1);
            Text("S E T T I N G S", panel.transform, 34, Ivory, new Vector2(0.5f, 0.90f), new Vector2(620, 60));
            Text(UiTheme.Spaced("AUDIO & DISPLAY"), panel.transform, UiTheme.SizeLabel, UiTheme.GoldLabel, new Vector2(0.5f, 0.82f), new Vector2(600, 25), true);
            Rule(panel.transform, new Vector2(0.5f, 0.797f), new Vector2(120, 1), UiTheme.GoldDim);
            MusicVolumeControl = VolumeSlider(panel.transform, 0.70f);
            ModeControl = Dropdown("Display mode", panel.transform, 0.58f, new[] { "Windowed", "Borderless" });
            ResolutionControl = Dropdown("Resolution", panel.transform, 0.46f, new string[0]);
            QualityControl = Dropdown("Quality", panel.transform, 0.34f, QualitySettings.names);
            status = Text("Music volume updates immediately.", panel.transform, 15, UiTheme.IvoryMuted, new Vector2(0.5f, 0.26f), new Vector2(640, 30));
            UISettingsButton = Button("UI",panel.transform,new Vector2(0,-222),new Vector2(592,44),OpenUISettings);
            UISettings=MenuAppearancePanel.Create(root,()=>{settings.SetActive(true);EventSystem.current.SetSelectedGameObject(UISettingsButton.gameObject);});
            ApplyButton = Button("Apply", panel.transform, new Vector2(-158, -285), new Vector2(276, 54), ApplySettings, true);
            CloseButton = Button("Back", panel.transform, new Vector2(158, -285), new Vector2(276, 54), CloseSettings);
            // Explicit circular focus order traps keyboard/gamepad navigation inside the modal.
            Selectable[] order = { MusicVolumeControl, ModeControl, ResolutionControl, QualityControl, UISettingsButton, ApplyButton, CloseButton };
            for (int i = 0; i < order.Length; i++)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = order[(i + order.Length - 1) % order.Length], selectOnDown = order[(i + 1) % order.Length],
                    selectOnLeft = order[i] == ApplyButton ? CloseButton : null, selectOnRight = order[i] == CloseButton ? ApplyButton : null };
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
            if(UISettings)UISettings.Hide();
            // Legacy Dropdown owns its temporary list; close it before hiding the parent.
            ModeControl.Hide(); ResolutionControl.Hide(); QualityControl.Hide();
            settings.SetActive(false); barGroup.interactable = true; barGroup.blocksRaycasts = true;
            PlayerPrefs.Save();
            EventSystem.current.SetSelectedGameObject(SettingsButton.gameObject);
        }
        public void OpenUISettings(){if(Loading||!SettingsOpen)return;settings.SetActive(false);UISettings.Open();}
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
            SceneManager.LoadScene(MenuAppearance.ScenePath);
#else
            Application.Quit();
#endif
        }
        void Update()
        {
            if (!safeRoot) return;
            musicTrack.text = Music.TrackTitle + (Music.UserPaused ? "  /  PAUSED" : "  /  PLAYING");
            MusicPlayButton.interactable = MusicPauseButton.interactable = MusicNextButton.interactable = !Loading && !SettingsOpen;
            if (Screen.safeArea != lastSafe)
            {
                lastSafe = Screen.safeArea;
                safeRoot.anchorMin = lastSafe.position / new Vector2(Screen.width, Screen.height);
                safeRoot.anchorMax = (lastSafe.position + lastSafe.size) / new Vector2(Screen.width, Screen.height);
            }
            // Narrow aspects (4:3) shrink the bottom row to the safe width minus the 32 px screen margins.
            float available = safeRoot.rect.width;
            if (available > 0) LayoutBar(Mathf.Min(1, (available - 2 * ScreenMargin) / BarFull));
            // The settings modal overlaps the logo on wide/short screens: fade logo and tagline back while it is open.
            titleAlpha = Mathf.MoveTowards(titleAlpha, SettingsOpen ? 0.25f : 1, Time.unscaledDeltaTime * 5);
            if (Logo) Logo.color = new Color(1, 1, 1, titleAlpha);
            if (titleText) titleText.color = new Color(Ivory.r, Ivory.g, Ivory.b, titleAlpha);
            if (tagline) tagline.color = new Color(Ivory.r, Ivory.g, Ivory.b, 0.85f * titleAlpha);
            var keyboard = MenuSmokeTest.Requested || MenuFlowTest.Requested ? MenuSmokeTest.TestKeyboard : Keyboard.current;
            var gamepad = MenuSmokeTest.Requested || MenuFlowTest.Requested ? MenuSmokeTest.TestGamepad : Gamepad.current;
#if UNITY_WEBGL && !UNITY_EDITOR
            gamepad = null;
#endif
            bool cancel = keyboard?.escapeKey.wasPressedThisFrame == true || gamepad?.buttonEast.wasPressedThisFrame == true;
            if (cancel && SettingsOpen)
            {
                if(UISettings&&UISettings.IsOpen){UISettings.Close();return;}
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
        // House button (see MenuUI.Button): ColorBlock tints stay <= 1 and MenuFocus drives fill/edge/label explicitly.
        // Rest Ink 0.85 + GoldDim edge; focus FocusFill + GoldBright edge + drawn diamond + underline. accent = the Gold-fill primary CTA.
        // The label stays the first Text child (tests read it); the marker is a drawn FocusDiamond, not a glyph.
        Button Button(string label, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action, bool accent = false, int textSize = UiTheme.SizeHeading)
        {
            var image = Image(label, parent, accent ? UiTheme.Gold : new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.85f));
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), size); image.rectTransform.anchoredPosition = position; image.raycastTarget = true;
            var border = image.gameObject.AddComponent<Outline>(); border.effectColor = accent ? UiTheme.GoldBright : UiTheme.GoldDim; border.effectDistance = Vector2.one;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; MenuUI.Tint(button);
            bool small = size.x < 150;
            var text = Text(label, image.transform, textSize, accent ? new Color(0.07f, 0.05f, 0.02f) : Ivory, new Vector2(0.5f, 0.5f), size - new Vector2(12, 4), true);
            // Stretch the label with the button so the responsive bottom row never clips it.
            var labelRect = text.rectTransform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(small ? 4 : 30, 2); labelRect.offsetMax = new Vector2(small ? -4 : -30, -2);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Min(textSize, 13); text.resizeTextMaxSize = textSize;
            Color cue = accent ? new Color(0.07f, 0.05f, 0.02f) : UiTheme.GoldBright;
            var marker = Rect("Focus diamond", image.transform).gameObject.AddComponent<FocusDiamond>();
            marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = new Vector2(0, 0.5f);
            marker.rectTransform.sizeDelta = small ? new Vector2(6, 10) : new Vector2(10, 16); marker.rectTransform.anchoredPosition = new Vector2(small ? 8 : 18, 0);
            marker.color = cue; marker.raycastTarget = false;
            var line = Image("Focus underline", image.transform, accent ? cue : UiTheme.Gold);
            line.rectTransform.anchorMin = new Vector2(0.3f, 0); line.rectTransform.anchorMax = new Vector2(0.7f, 0); line.rectTransform.pivot = new Vector2(0.5f, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 2); line.rectTransform.anchoredPosition = new Vector2(0, small ? 3 : 6);
            var focus = image.gameObject.AddComponent<MenuFocus>();
            focus.Fill = image; focus.Edge = border; focus.Label = text; focus.Marker = marker.gameObject; focus.Underline = line.gameObject; focus.Primary = accent;
            button.onClick.AddListener(action); return button;
        }
        // Settings rows: Body caption left, Ink well right with a 1 px GoldDim edge; focus adds the diamond beside the caption and a gold underline.
        void RowFocus(Selectable control, Image well, Text caption, Transform parent, float y)
        {
            var edge = well.gameObject.AddComponent<Outline>(); edge.effectColor = UiTheme.GoldDim; edge.effectDistance = Vector2.one;
            var marker = Rect("Focus diamond", parent).gameObject.AddComponent<FocusDiamond>();
            Place(marker.rectTransform, new Vector2(0.25f, y), new Vector2(10, 16)); marker.rectTransform.anchoredPosition = new Vector2(-126, 0);
            marker.color = UiTheme.GoldBright; marker.raycastTarget = false;
            var line = Image("Focus underline", well.transform, UiTheme.Gold);
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0); line.rectTransform.pivot = new Vector2(0.5f, 0);
            line.rectTransform.sizeDelta = new Vector2(0, 2); line.rectTransform.anchoredPosition = Vector2.zero;
            var focus = control.gameObject.AddComponent<MenuFocus>();
            focus.Fill = well; focus.Edge = edge; focus.Label = caption; focus.Marker = marker.gameObject; focus.Underline = line.gameObject;
        }
        Slider VolumeSlider(Transform parent, float y)
        {
            var label = Text("Music Volume", parent, UiTheme.SizeBody, UiTheme.IvoryBody, new Vector2(0.25f, y), new Vector2(220, 44)); label.alignment = TextAnchor.MiddleLeft;
            var background = Image("Main menu music volume", parent, new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.85f));
            Place(background.rectTransform, new Vector2(0.66f, y), new Vector2(320, 48)); background.raycastTarget = true;
            var track = Rect("Volume track", background.transform); Bounds(track, new Vector2(0.05f, 0.42f), new Vector2(0.78f, 0.58f));
            var trough = track.gameObject.AddComponent<Image>(); trough.color = new Color(0, 0, 0, 0.55f); trough.raycastTarget = false;
            var fill = Image("Volume fill", track, Gold); Bounds(fill.rectTransform, Vector2.zero, Vector2.one);
            var handleArea = Rect("Volume handle area", background.transform); Bounds(handleArea, new Vector2(0.05f, 0), new Vector2(0.78f, 1));
            var handle = Image("Volume handle", handleArea, Ivory); Place(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(10, 26)); handle.raycastTarget = true;
            var slider = background.gameObject.AddComponent<Slider>(); slider.targetGraphic = handle; slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            MenuUI.Tint(slider);
            slider.minValue = 0; slider.maxValue = 1; slider.value = Music.Volume;
            var percent = Text(Mathf.RoundToInt(Music.Volume * 100) + "%", background.transform, UiTheme.SizeNumeric, Gold, new Vector2(0.90f, 0.5f), new Vector2(60, 40), true);
            slider.onValueChanged.AddListener(value => { Music.SetVolume(value); percent.text = Mathf.RoundToInt(value * 100) + "%"; });
            RowFocus(slider, background, label, parent, y);
            return slider;
        }
        Dropdown Dropdown(string label, Transform parent, float y, string[] options)
        {
            var caption = Text(label, parent, UiTheme.SizeBody, UiTheme.IvoryBody, new Vector2(0.25f, y), new Vector2(220, 44)); caption.alignment = TextAnchor.MiddleLeft;
            var image = Image(label + " control", parent, new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.85f)); Place(image.rectTransform, new Vector2(0.66f, y), new Vector2(320, 48)); image.raycastTarget = true;
            var drop = image.gameObject.AddComponent<Dropdown>(); drop.targetGraphic = image; MenuUI.Tint(drop);
            var selected = Text("", image.transform, UiTheme.SizeBody, Ivory, new Vector2(0.45f, 0.5f), new Vector2(256, 44)); selected.alignment = TextAnchor.MiddleLeft;
            // Drawn chevron (Inter has no ▾): a right-pointing ClassSelectionArrow turned to point down.
            var chevron = Rect("Dropdown chevron", image.transform).gameObject.AddComponent<ClassSelectionArrow>();
            Place(chevron.rectTransform, new Vector2(0.93f, 0.5f), new Vector2(18, 18)); chevron.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
            chevron.Next = true; chevron.color = Gold; chevron.raycastTarget = false;
            drop.captionText = selected;
            var template = Image("Template", image.transform, UiTheme.InkDeep);
            template.rectTransform.anchorMin = new Vector2(0, 0); template.rectTransform.anchorMax = new Vector2(1, 0);
            template.rectTransform.pivot = new Vector2(0.5f, 1); template.rectTransform.sizeDelta = new Vector2(0, 220);
            template.raycastTarget = true;
            var templateEdge = template.gameObject.AddComponent<Outline>(); templateEdge.effectColor = UiTheme.GoldDim; templateEdge.effectDistance = Vector2.one;
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Image("Viewport", template.transform, Color.white); Bounds(viewport.rectTransform, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport.transform); Bounds(content, new Vector2(0, 1), Vector2.one); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = new Vector2(0, 44);
            // Item: warm focus fill at tint 1, dimmed to Ink-like at rest (tints never exceed 1).
            var item = Image("Item", content, new Color(0.16f, 0.13f, 0.08f, 0.95f));
            Bounds(item.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f)); item.rectTransform.sizeDelta = new Vector2(0, 44); item.raycastTarget = true;
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item;
            var colors = toggle.colors; colors.normalColor = new Color(0.35f, 0.35f, 0.38f, 1); colors.highlightedColor = Color.white; colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.25f, 0.25f, 0.25f, 1); colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f); colors.colorMultiplier = 1; colors.fadeDuration = 0.08f; toggle.colors = colors;
            var check = Rect("Item checkmark", item.transform).gameObject.AddComponent<FocusDiamond>();
            Place(check.rectTransform, new Vector2(0.06f, 0.5f), new Vector2(8, 12)); check.color = Gold; check.raycastTarget = false; toggle.graphic = check;
            var itemText = Text("Option", item.transform, UiTheme.SizeBody, Ivory, new Vector2(0.54f, 0.5f), new Vector2(265, 40)); itemText.alignment = TextAnchor.MiddleLeft;
            drop.template = template.rectTransform; drop.itemText = itemText;
            scroll.viewport = viewport.rectTransform; scroll.content = content;
            drop.AddOptions(options.ToList()); template.gameObject.SetActive(false);
            RowFocus(drop, image, caption, parent, y);
            return drop;
        }
        static RectTransform Rect(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
        static Image Image(string name, Transform parent, Color color)
        { var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        // Inter via UiTheme (SemiBold from 20 px or when strong, Regular otherwise) with the house drop shadow.
        Text Text(string text, Transform parent, int size, Color color, Vector2 anchor, Vector2 bounds, bool strong = false)
        {
            var rect = Rect(text.Length > 32 ? text.Substring(0, 32) : text, parent); Place(rect, anchor, bounds);
            var label = rect.gameObject.AddComponent<Text>(); label.font = strong || size >= 20 ? UiTheme.SemiBold : UiTheme.Regular; label.fontStyle = FontStyle.Normal;
            label.text = text; label.fontSize = size; label.lineSpacing = 1.08f;
            label.color = color; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            MenuUI.Shadow(label, 0.8f); return label;
        }
        // House frame on an existing panel image: 1 px GoldDim border plus an inset dark line.
        static void Frame(Image panel)
        {
            var edge = panel.gameObject.AddComponent<Outline>(); edge.effectColor = UiTheme.GoldDim; edge.effectDistance = Vector2.one;
            // Inset dark line drawn as four 1 px rects (an Outline on a transparent image would inherit its zero alpha).
            var inner = Rect("Inner shadow", panel.transform); Bounds(inner, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(3, 3); inner.offsetMax = new Vector2(-3, -3);
            var shade = new Color(0, 0, 0, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                var side = Image("Inset line", inner, shade).rectTransform; bool horizontal = i < 2; float sideAt = i % 2;
                side.anchorMin = horizontal ? new Vector2(0, sideAt) : new Vector2(sideAt, 0); side.anchorMax = horizontal ? new Vector2(1, sideAt) : new Vector2(sideAt, 1);
                side.pivot = new Vector2(horizontal ? 0.5f : sideAt, horizontal ? sideAt : 0.5f); side.anchoredPosition = Vector2.zero;
                side.sizeDelta = horizontal ? new Vector2(0, 1) : new Vector2(1, 0);
            }
        }
        static void Bounds(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size; }
        static Image Rule(Transform parent, Vector2 anchor, Vector2 size, Color color)
        { var image = Image("Brass rule", parent, color); Place(image.rectTransform, anchor, size); return image; }
    }

    // Only installed after entering the run from this menu. Direct run-scene diagnostics stay untouched.
    public sealed class MenuReturnBridge : MonoBehaviour
    {
        public static bool TestSession;
        static bool returnToSetup;
        static MenuReturnBridge active;
        public static bool Active => active;
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
            // "Test Setup" reopens Boss Rush on the main menu (the old hero-select scene is retired).
            if(setup)ConfrontationMenu.ResumeBossRush=true;
            SceneManager.LoadSceneAsync(MenuAppearance.ScenePath);
        }
        void OnDestroy() { if (observed) observed.StateChanged -= OnState; if (active == this) active = null; }
    }
}
