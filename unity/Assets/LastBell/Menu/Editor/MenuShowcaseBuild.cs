using System;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu.EditorTools
{
    public static class MenuShowcaseBuild
    {
        [MenuItem("Last Bell/Main Menu/Prepare Showcase Scene", priority = 34)]
        public static void Prepare()
        {
            AssetDatabase.Refresh();
            // Additive authoring: preserve the original menu and all arena source scenes.
            // Never overwrite a previously authored showcase scene on a repeated invocation.
            if (File.Exists(MainMenu.ScenePath)) throw new IOException("Showcase scene already exists; open it to edit it.");
            const string original = "Assets/LastBell/Menu/MainMenu.unity";
            if (!AssetDatabase.CopyAsset(original, MainMenu.ScenePath)) throw new IOException("Could not copy the existing menu.");
            var scene = EditorSceneManager.OpenScene(MainMenu.ScenePath);
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            var camera = Camera.main;
            if (!menu || !camera) throw new InvalidOperationException("Existing main menu/camera is required.");
            camera.transform.SetParent(null, true);
            var legacy = new GameObject("Original menu environment (preserved, inactive)");
            foreach (var root in scene.GetRootGameObjects())
                if (root != menu.gameObject && root != camera.gameObject && root != legacy) root.transform.SetParent(legacy.transform, true);
            legacy.SetActive(false);
            var showcase = menu.gameObject.AddComponent<MenuShowcase>();
            showcase.Stages = new MenuShowcase.Stage[RunMaps.All.Length];
            string[] bosses = { "Bellkeeper2", "Aurelion", "Crucivar", "Sylvara", "Bellkeeper2" };
            int[] sizes = { 3, 4, 5, 6, 8 };
            for (int i = 0; i < RunMaps.All.Length; i++)
            {
                var source = EditorSceneManager.OpenScene(RunMaps.All[i].SourceScene, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(source);
                var sourceCamera = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
                var stage = new MenuShowcase.Stage { Name = RunMaps.All[i].Name, Boss = bosses[i], PartySize = sizes[i],
                    Ambient = RenderSettings.ambientLight, AmbientIntensity = RenderSettings.ambientIntensity,
                    Fog = RenderSettings.fogColor, FogEnabled = RenderSettings.fog, FogMode = RenderSettings.fogMode,
                    FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance, Skybox = RenderSettings.skybox,
                    Background = sourceCamera ? sourceCamera.backgroundColor : Color.black };
                var group = new GameObject("Showcase environment - " + stage.Name);
                SceneManager.MoveGameObjectToScene(group, scene); group.SetActive(false); group.transform.SetParent(menu.transform, false);
                foreach (var root in source.GetRootGameObjects())
                {
                    // Disable duplicate ownership only in the copied environment. Source files are never saved.
                    foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (behaviour && (behaviour.GetType().Name == "KeyArtArena" || behaviour is RunDirector || behaviour is RunHud || behaviour is UnityEngine.EventSystems.EventSystem)) behaviour.enabled = false;
                    foreach (var c in root.GetComponentsInChildren<Camera>(true)) { c.enabled = false; c.tag = "Untagged"; }
                    foreach (var a in root.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
                    foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) { a.enabled = false; a.playOnAwake = false; }
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith("ScaleReference", StringComparison.Ordinal) || t.name.StartsWith("Telegraph", StringComparison.Ordinal)) t.gameObject.SetActive(false);
                    SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetParent(group.transform, true);
                }
                stage.Environment = group; showcase.Stages[i] = stage;
                EditorSceneManager.CloseScene(source, true);
            }
            SceneManager.SetActiveScene(scene);
            showcase.Stages[0].Environment.SetActive(true);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainMenu.ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != MainMenu.ScenePath && s.path != original)).ToArray();
            AssetDatabase.SaveAssets();
            IsolateCopiedScenery();
            Debug.Log("Prepared one showcase menu scene, preserving all source scenes. Casts: 3/4/5/6/8.");
        }
        public static void BuildWindows()
        {
            var output = Environment.GetEnvironmentVariable("LASTBELL_SHOWCASE_OUTPUT") ?? "E:/Collab/builds/menu-showcase-20261022/LastBell.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { MainMenu.ScenePath, HeroSelection.ScenePath }.Concat(RunMaps.All.Select(m => m.RunScene)).ToArray(),
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            Debug.Log($"Showcase build: {report.summary.result}, {report.summary.totalErrors} errors/{report.summary.totalWarnings} warnings");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        public static void IsolateCopiedScenery()
        {
            // Only the derived showcase is changed. Original menu/arena/prefab assets stay intact.
            var scene = EditorSceneManager.OpenScene(MainMenu.ScenePath);
            var showcase = UnityEngine.Object.FindAnyObjectByType<MenuShowcase>();
            if (!showcase || scene.path != MainMenu.ScenePath) throw new InvalidOperationException("Expected the derived showcase scene.");
            foreach (var stage in showcase.Stages)
            {
                foreach (var t in stage.Environment.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                        PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // This editor viewer's OnValidate accesses prefab Renderer.material while playing.
                // It is unnecessary in a menu with its own camera; remove it from this copy only.
                foreach (var viewer in stage.Environment.GetComponentsInChildren<LastBell.Areas.Area1.SkywardSpireArena>(true))
                    UnityEngine.Object.DestroyImmediate(viewer);
            }
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }
}
