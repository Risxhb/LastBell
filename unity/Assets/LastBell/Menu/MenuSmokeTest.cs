using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastBell.Menu
{
    // Opt-in checks inside the rendered Unity runtime, never active during ordinary menu use.
    public sealed class MenuSmokeTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Contains("-lastbellMenuTest");
        public static Keyboard TestKeyboard { get; private set; }
        public static Gamepad TestGamepad { get; private set; }
        public static Mouse TestMouse { get; private set; }
        public static InputDevice[] TestDevices => new InputDevice[] { TestKeyboard, TestGamepad, TestMouse };
        public static void PrepareInput()
        {
            if (TestKeyboard != null) return;
            TestKeyboard = InputSystem.AddDevice<Keyboard>("MenuTestKeyboard");
            TestGamepad = InputSystem.AddDevice<Gamepad>("MenuTestGamepad");
            TestMouse = InputSystem.AddDevice<Mouse>("MenuTestMouse");
        }
        static bool started;
        readonly List<string> report = new List<string>();
        readonly List<string> errors = new List<string>();
        int failures;
        string output;
        MainMenu menu;
        InputSettings originalInputSettings, testInputSettings;
        HideFlags originalInputFlags;
        void Start()
        {
            if (started) { Destroy(this); return; }
            started = true;
            var go = new GameObject("Menu test lifetime"); DontDestroyOnLoad(go);
            // Move the component's owner out of the scene without keeping the menu alive.
            StartCoroutine(Test(go));
        }
        IEnumerator Test(GameObject lifetime)
        {
            var runner = lifetime.AddComponent<MenuSmokeRunner>(); runner.Begin(this);
            yield return null;
        }
        public IEnumerator Run()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "menu-test");
            Directory.CreateDirectory(output); Application.logMessageReceived += Log;
            Application.runInBackground = true;
            originalInputSettings = InputSystem.settings;
            originalInputFlags=originalInputSettings.hideFlags;originalInputSettings.hideFlags=HideFlags.DontUnloadUnusedAsset;
            testInputSettings = Instantiate(originalInputSettings);
            testInputSettings.hideFlags=HideFlags.DontUnloadUnusedAsset;
            testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = testInputSettings;
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            menu = FindAnyObjectByType<MainMenu>();
            yield return new WaitForSeconds(2);
            Check("four real models", menu.Battle.ActorCount == 4 && menu.Battle.AllModelsPresent);
            Check("no combat authority in menu", !RunDirector.Instance && !FindAnyObjectByType<BossEncounter>() &&
                FindObjectsByType<PlayerController>().All(p => !p.enabled && p.World == null && p.Kit == null && p.Input == null));
            Check("supplied logo loaded", menu.Logo && menu.Logo.sprite);
            Check("menu music plays", menu.Music.Source && menu.Music.Source.clip && menu.Music.Source.isPlaying && menu.Music.Source.timeSamples > 0);
            Quaternion openingCamera = Camera.main.transform.rotation;
            Check("four main actions", menu.StartButton.GetComponentInChildren<Text>().text == "Start Run" && menu.TestRushButton.GetComponentInChildren<Text>().text == "Boss-Rush Test Rush" &&
                menu.SettingsButton.GetComponentInChildren<Text>().text == "Settings" && menu.ExitButton.GetComponentInChildren<Text>().text == "Exit");
            Check("initial focus", EventSystem.current.currentSelectedGameObject == menu.StartButton.gameObject);
            // Real InputSystem keyboard/controller events drive the installed UI module.
            var keyboard = TestKeyboard;
            var pad = TestGamepad;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Check("keyboard right selects Test Rush", EventSystem.current.currentSelectedGameObject == menu.TestRushButton.gameObject);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Check("keyboard right selects Settings", EventSystem.current.currentSelectedGameObject == menu.SettingsButton.gameObject);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Check("keyboard submit opens settings", menu.SettingsOpen);
            if (!menu.SettingsOpen)
            {
                var module = EventSystem.current.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                report.Add("Focus=" + EventSystem.current.currentSelectedGameObject?.name + "; keyboard enabled=" + keyboard.enabled + "; navigation enabled=" + module.move.action.enabled + "; navigation=" + module.move.action.ReadValue<Vector2>());
                FinishEarly("Settings did not open; dependent checks skipped."); yield break;
            }
            Check("settings traps primary input", !menu.StartButton.IsInteractable());
            float before = menu.Battle.Elapsed; yield return new WaitForSeconds(0.3f);
            Check("fight continues behind settings", menu.Battle.Elapsed > before + 0.2f);
            bool hadMusicPreference = PlayerPrefs.HasKey("LastBell.Menu.MusicVolume"); float originalVolume = menu.Music.Volume;
            menu.MusicVolumeControl.value = 0.25f; yield return null;
            Check("music slider changes live volume and persists", Mathf.Abs(menu.Music.Source.volume - 0.25f) < 0.01f && Mathf.Approximately(PlayerPrefs.GetFloat("LastBell.Menu.MusicVolume"), 0.25f));
            menu.MusicVolumeControl.value = 0; yield return null;
            Check("music slider can mute", menu.Music.Source.volume == 0);
            menu.MusicVolumeControl.value = originalVolume;
            if (!hadMusicPreference) PlayerPrefs.DeleteKey("LastBell.Menu.MusicVolume");
            string[] keys = { "Width", "Height", "Mode", "Quality" };
            var oldPrefs = keys.Select(k => PlayerPrefs.HasKey("LastBell.Menu." + k) ? (int?)PlayerPrefs.GetInt("LastBell.Menu." + k) : null).ToArray();
            menu.ApplyButton.onClick.Invoke();
            Check("Apply saves actual display and quality settings", PlayerPrefs.GetInt("LastBell.Menu.Width") == Screen.width &&
                PlayerPrefs.GetInt("LastBell.Menu.Quality") == QualitySettings.GetQualityLevel());
            for (int i = 0; i < keys.Length; i++)
                if (oldPrefs[i].HasValue) PlayerPrefs.SetInt("LastBell.Menu." + keys[i], oldPrefs[i].Value);
                else PlayerPrefs.DeleteKey("LastBell.Menu." + keys[i]);
            PlayerPrefs.Save();
            menu.ResolutionControl.Show(); yield return null;
            Check("resolution list opens", MainMenu.DropdownOpen(menu.ResolutionControl));
            menu.ResolutionControl.Hide(); yield return new WaitForSeconds(0.2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null; yield return null;
            Check("Escape closes settings and restores focus", !menu.SettingsOpen && EventSystem.current.currentSelectedGameObject == menu.SettingsButton.gameObject);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South)); yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
            Check("gamepad submit opens settings", menu.SettingsOpen);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East)); yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
            Check("gamepad Back closes settings", !menu.SettingsOpen);
            Vector2 settingsPoint = menu.SettingsButton.transform.position;
            InputSystem.QueueStateEvent(TestMouse, new MouseState { position = settingsPoint }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(TestMouse, new MouseState { position = settingsPoint }); yield return null; yield return null;
            Check("mouse opens Settings", menu.SettingsOpen);
            menu.CloseSettings();
            yield return new WaitForSeconds(1);
            Check("camera slowly orbits", Quaternion.Angle(openingCamera, Camera.main.transform.rotation) > 1);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(2560, 1080) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed); yield return new WaitForSeconds(1);
                Check("bottom row fits " + size, Fits(menu.StartButton) && Fits(menu.TestRushButton) && Fits(menu.SettingsButton) && Fits(menu.ExitButton));
                yield return Shot("menu-" + size.x + "x" + size.y + ".png");
            }
            menu.OpenSettings(); yield return null; yield return Shot("settings.png"); menu.CloseSettings();
            var showcase = menu.GetComponent<MenuShowcase>();
            while (showcase.Elapsed < 13.05f) yield return null;
            yield return Shot("party-spell-release.png");
            // Compare after the first full cycle, once the existing spell pools/trails have warmed.
            while (showcase.Elapsed < 81) yield return null;
            yield return Shot("first-arena-rotation.png");
            int objects = menu.Battle.GetComponentsInChildren<Transform>().Length + FindAnyObjectByType<MenuFxLifetime>().GetComponentsInChildren<Transform>(true).Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            while (showcase.Elapsed < 156) yield return null;
            Check("two complete arena rotations", showcase.Changes == 10 && showcase.CurrentStage == 0);
            Check("all actors travel independently", menu.Battle.ActorTravel.Take(menu.Battle.ActorCount).All(distance => distance > .1f) && menu.Battle.IndependentMotionFrames > 60);
            Check("casters plant without sliding during attacks", menu.Battle.PlantedCastFrames > 120 && menu.Battle.CastSlideFrames == 0);
            Check("all choreography clips available", menu.Battle.Presenters.All(p => p.Placeholders.Count == 0));
            Check("actual class and boss spell effects play", menu.Battle.SpellsPresented > 100);
            Check("fixed actor/effect population", menu.Battle.ActorCount == 4 && menu.Battle.EffectCount == 18 &&
                menu.Battle.GetComponentsInChildren<Transform>().Length + FindAnyObjectByType<MenuFxLifetime>().GetComponentsInChildren<Transform>(true).Length == objects);
            Check("bounded materials over five loops", Resources.FindObjectsOfTypeAll<Material>().Length <= materials + 2);
            yield return Shot("after-five-loops.png");
            menu.StartButton.onClick.Invoke(); menu.StartRun();
            Check("duplicate start guarded", menu.Loading);
            float deadline = Time.realtimeSinceStartup + 45;
            while (!FindAnyObjectByType<HeroSelection>() && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSeconds(.5f);
            var selection=FindAnyObjectByType<HeroSelection>();
            Check("Start opens cinematic hero selection",selection&&!RunDirector.Instance&&!FindAnyObjectByType<MenuBattle>());
            if(selection){selection.SelectHero(2);selection.ConfirmClass();selection.ConfirmClass();}
            deadline=Time.realtimeSinceStartup+45;
            while (!RunDirector.Instance && Time.realtimeSinceStartup < deadline) yield return null;
            var director = RunDirector.Instance;
            Check("Select Class enters fresh run Ready", director && director.State == RunState.Ready && director.Encounter==director.EncounterOrder[0]);
            Check("menu music continues through run preparation", FindAnyObjectByType<MenuMusic>() && FindAnyObjectByType<MenuMusic>().Source.isPlaying && FindAnyObjectByType<MenuMusic>().Source == FindAnyObjectByType<MenuMusic>().MenuSource);
            if (director)
            {
                yield return new WaitForSeconds(0.2f);
                Check("fresh session preserves class", director.ClassName=="Arcanist" && director.RunsStarted == 1 && director.State == RunState.Ready);
                director.SetPaused(true); director.ReturnToMenu();
                deadline = Time.realtimeSinceStartup + 30;
                while (!FindAnyObjectByType<MainMenu>() && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSeconds(1);
                Check("return creates one clean choreography", FindObjectsByType<MenuBattle>().Length == 1 && !RunDirector.Instance);
                Check("return creates one menu music source", FindObjectsByType<MenuMusic>().Length == 1 && FindAnyObjectByType<MenuMusic>().Source.isPlaying);
            }
            Check("zero runtime errors", errors.Count == 0);
            report.AddRange(errors); report.Add($"RESULT: {report.Count(l => l.StartsWith("PASS"))} passed, {failures} failed");
            File.WriteAllLines(Path.Combine(output, "menu-test-report.txt"), report);
            Application.logMessageReceived -= Log;
            RestoreInputSettings();
#if UNITY_EDITOR
            FindAnyObjectByType<MainMenu>().Exit();
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            if (failures == 0) FindAnyObjectByType<MainMenu>().Exit(); else Application.Quit(1);
#endif
        }
        bool Fits(Button button)
        {
            var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
            return corners.All(v => v.x >= 0 && v.x <= Screen.width && v.y >= 0 && v.y <= Screen.height);
        }
        IEnumerator Shot(string name)
        {
            for (int i = 0; i < 4; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
            Color middle = texture.GetPixel(texture.width / 2, texture.height / 2);
            Check("rendered capture " + name, middle.maxColorComponent > 0.01f);
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG()); Destroy(texture);
            yield return null;
        }
        void Check(string name, bool passed) { if (!passed) failures++; string line = (passed ? "PASS " : "FAIL ") + name; report.Add(line); Debug.Log("[menu-test] " + line); }
        void FinishEarly(string reason)
        {
            report.Add(reason); report.AddRange(errors);
            File.WriteAllLines(Path.Combine(output, "menu-test-report.txt"), report);
            RestoreInputSettings();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false; UnityEditor.EditorApplication.Exit(1);
#else
            Application.Quit(1);
#endif
        }
        void RestoreInputSettings()
        {
            if (originalInputSettings) { InputSystem.settings = originalInputSettings; originalInputSettings.hideFlags=originalInputFlags; }
            if (testInputSettings) Destroy(testInputSettings);
            if (TestKeyboard != null) InputSystem.RemoveDevice(TestKeyboard);
            if (TestGamepad != null) InputSystem.RemoveDevice(TestGamepad);
            if (TestMouse != null) InputSystem.RemoveDevice(TestMouse);
            TestKeyboard = null; TestGamepad = null; TestMouse = null;
        }
        void Log(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    }
    public sealed class MenuSmokeRunner : MonoBehaviour
    {
        public void Begin(MenuSmokeTest owner) { StartCoroutine(owner.Run()); }
    }
}
