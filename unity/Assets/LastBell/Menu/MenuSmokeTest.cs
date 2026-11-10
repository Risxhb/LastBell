using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
        // A player window without focus disables non-background devices when they are added (the default ResetAndDisable
        // behaviour applies before a test switches to IgnoreFocus), so the arrow keys never reached the UI (Ultimate lane, 4 Nov:
        // 4 keyboard checks failed with another window in front). Call after switching InputSystem.settings.
        public static void EnableTestDevices()
        {
            foreach (var dv in TestDevices) if (dv != null && !dv.enabled) InputSystem.EnableDevice(dv);
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
            EnableTestDevices();
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            menu = FindAnyObjectByType<MainMenu>();
            yield return new WaitForSeconds(2);
            // The Showcase scene is now only the startup router and Boss Rush's live backdrop (its menu UI was retired on
            // November 10, 2026). This test keeps the live showcase honest: real models, battle, arena rotation, music.
            // The main menu itself is covered by ConfrontationMenuTest and MenuFlowTest.
            Check("showcase scene stays for the menu test", menu && SceneManager.GetActiveScene().path == MainMenu.ScenePath && SceneManager.sceneCount == 1);
            // The only canvas left is the showcase's own arena fade: no buttons, labels or menu.
            Check("no menu UI on the showcase scene", !FindAnyObjectByType<UnityEngine.UI.Selectable>() && !FindAnyObjectByType<UnityEngine.UI.Text>() && !FindAnyObjectByType<ConfrontationMenu>());
            Check("four real models", menu.Battle.ActorCount == 4 && menu.Battle.AllModelsPresent);
            Check("no combat authority in menu", !RunDirector.Instance && !FindAnyObjectByType<BossEncounter>() &&
                FindObjectsByType<PlayerController>().All(p => !p.enabled && p.World == null && p.Kit == null && p.Input == null));
            Check("menu music plays", menu.Music.Source && menu.Music.Source.clip && menu.Music.Source.isPlaying && menu.Music.Source.timeSamples > 0);
            Check("one persistent audio owner", FindObjectsByType<MenuMusic>().Length == 1);
            Quaternion openingCamera = Camera.main.transform.rotation;
            float before = menu.Battle.Elapsed; yield return new WaitForSeconds(1);
            Check("fight runs live", menu.Battle.Elapsed > before + 0.5f);
            Check("camera slowly orbits", Quaternion.Angle(openingCamera, Camera.main.transform.rotation) > 1);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(2560, 1080) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed); yield return new WaitForSeconds(1);
                yield return Shot("showcase-" + size.x + "x" + size.y + ".png");
            }
            var showcase = menu.GetComponent<MenuShowcase>();
            while (showcase.Elapsed < 13.05f) yield return null;
            yield return Shot("party-spell-release.png");
            // Compare after the first full cycle, once the existing spell pools/trails have warmed.
            float loop = showcase.Stages.Length * MenuShowcase.StageSeconds;   // one full rotation through every showcased arena
            while (showcase.Elapsed < loop + 6) yield return null;
            yield return Shot("first-arena-rotation.png");
            int objects = menu.Battle.GetComponentsInChildren<Transform>().Length + FindAnyObjectByType<MenuFxLifetime>().GetComponentsInChildren<Transform>(true).Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            while (showcase.Elapsed < 2 * loop + 6) yield return null;
            Check("two complete arena rotations", showcase.Changes == 2 * showcase.Stages.Length && showcase.CurrentStage == 0);
            Check("one visible environment", showcase.Stages.Count(s => s.Environment.activeInHierarchy) == 1);
            Check("all actors travel independently", menu.Battle.ActorTravel.Take(menu.Battle.ActorCount).All(distance => distance > .1f) && menu.Battle.IndependentMotionFrames > 60);
            Check("casters plant without sliding during attacks", menu.Battle.PlantedCastFrames > 120 && menu.Battle.CastSlideFrames == 0);
            Check("all choreography clips available", menu.Battle.Presenters.All(p => p.Placeholders.Count == 0));
            Check("actual class and boss spell effects play", menu.Battle.SpellsPresented > 100);
            Check("fixed actor/effect population", menu.Battle.ActorCount == 4 && menu.Battle.EffectCount == 18 &&
                menu.Battle.GetComponentsInChildren<Transform>().Length + FindAnyObjectByType<MenuFxLifetime>().GetComponentsInChildren<Transform>(true).Length == objects);
            Check("bounded materials over two loops", Resources.FindObjectsOfTypeAll<Material>().Length <= materials + 2);
            yield return Shot("after-two-loops.png");
            Check("zero runtime errors", errors.Count == 0);
            report.AddRange(errors); report.Add($"RESULT: {report.Count(l => l.StartsWith("PASS"))} passed, {failures} failed");
            File.WriteAllLines(Path.Combine(output, "menu-test-report.txt"), report);
            Application.logMessageReceived -= Log;
            RestoreInputSettings();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
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
