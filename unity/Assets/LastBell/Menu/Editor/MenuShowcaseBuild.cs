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
            var stages = RunMaps.Showcased;
            showcase.Stages = new MenuShowcase.Stage[stages.Length];
            string[] bosses = { "Bellkeeper2", "Aurelion", "Crucivar", "Sylvara", "Bellkeeper2" };
            int[] sizes = { 3, 4, 5, 6, 8 };
            for (int i = 0; i < stages.Length; i++)
            {
                var source = EditorSceneManager.OpenScene(stages[i].SourceScene, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(source);
                var sourceCamera = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
                var stage = new MenuShowcase.Stage { Name = stages[i].Name, Boss = bosses[i], PartySize = sizes[i],
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
        // Batch entry:  Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Menu.EditorTools.MenuShowcaseBuild.AddMetronomeCourtStage -quit
        [MenuItem("Last Bell/Main Menu/Add or Refresh Metronome Court Showcase Stage", priority = 35)]
        public static void AddMetronomeCourtStage()
        {
            AddStage("metronome-court");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Adds one arena to the existing showcase scene without regenerating it: existing stages, GUIDs and order stay as
        // they are. Run again to refresh that arena's copy in place (e.g. after its art changes). The map must already be
        // InShowcase, and its position in RunMaps.Showcased must be the next stage index (TestRushSetup.ShowcaseStage
        // relies on the two orders matching).
        // Re-copies the four area arenas after their art changes (new Blender models, regenerated Area 4 assets), keeping
        // each stage's cast, party size and camera. Batch:
        // Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Menu.EditorTools.MenuShowcaseBuild.RefreshAreaStages
        [MenuItem("Last Bell/Main Menu/Refresh Area Showcase Stages", priority = 37)]
        public static void RefreshAreaStages()
        {
            foreach (var id in new[] { "skyward-spire", "brass-foundry", "verdant-chimes", "shattered-hour" }) AddStage(id, keepCasting: true);
            IsolateCopiedScenery();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void AddStage(string mapId) => AddStage(mapId, keepCasting: false);

        public static void AddStage(string mapId, bool keepCasting)
        {
            AssetDatabase.Refresh();
            int mapIndex = RunMaps.IndexOf(mapId);
            if (mapIndex < 0) throw new ArgumentException("Unknown map " + mapId);
            var map = RunMaps.All[mapIndex];
            int index = Array.IndexOf(RunMaps.Showcased, map);
            if (index < 0) throw new InvalidOperationException(mapId + " is not InShowcase in RunMaps.");
            // Opening the scene in the editor lets Verdant Chimes' LookSettingsApplier.OnValidate rewrite the scene fog
            // (runtime overrides it per stage). Keep the saved values so the rest of the scene file is untouched.
            var savedFog = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(MainMenu.ScenePath),
                @"RenderSettings:[\s\S]*?m_LinearFogStart: ([-\d.eE]+)\s+m_LinearFogEnd: ([-\d.eE]+)");
            var scene = EditorSceneManager.OpenScene(MainMenu.ScenePath);
            var menu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
            var showcase = UnityEngine.Object.FindAnyObjectByType<MenuShowcase>();
            if (!menu || !showcase || showcase.Stages == null) throw new InvalidOperationException("Expected the prepared showcase scene.");
            var existing = Array.FindIndex(showcase.Stages, s => s.Name == map.Name);
            if (existing >= 0 && existing != index) throw new InvalidOperationException($"{map.Name} is stage {existing}, expected {index}.");
            if (existing < 0 && index != showcase.Stages.Length) throw new InvalidOperationException($"{map.Name} would be stage {showcase.Stages.Length}, but RunMaps.Showcased puts it at {index}.");
            for (int i = 0; i < showcase.Stages.Length; i++)
                if (i != index && showcase.Stages[i].Name != RunMaps.Showcased[i].Name) throw new InvalidOperationException($"Stage {i} is {showcase.Stages[i].Name}, RunMaps has {RunMaps.Showcased[i].Name}.");

            var source = EditorSceneManager.OpenScene(map.SourceScene, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(source);
            BakeCodeBuiltArena(source);
            var sourceCamera = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            var stage = new MenuShowcase.Stage { Name = map.Name,
                Ambient = RenderSettings.ambientLight, AmbientIntensity = RenderSettings.ambientIntensity,
                Fog = RenderSettings.fogColor, FogEnabled = RenderSettings.fog, FogMode = RenderSettings.fogMode,
                FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance, Skybox = RenderSettings.skybox,
                Background = sourceCamera ? sourceCamera.backgroundColor : Color.black };
            ConfigureStage(mapId, stage);
            if (keepCasting && existing >= 0)
            {
                // Only the arena copy and its lighting change; who is on stage and how the camera moves stay as authored.
                var old = showcase.Stages[existing];
                stage.Boss = old.Boss; stage.PartySize = old.PartySize; stage.CustomCamera = old.CustomCamera;
                stage.CameraTarget = old.CameraTarget; stage.CameraPosition = old.CameraPosition; stage.FieldOfView = old.FieldOfView;
                stage.OrbitDegreesPerSecond = old.OrbitDegreesPerSecond; stage.ScreenOffsetDegrees = old.ScreenOffsetDegrees;
            }
            var group = new GameObject("Showcase environment - " + stage.Name);
            SceneManager.MoveGameObjectToScene(group, scene); group.SetActive(false); group.transform.SetParent(menu.transform, false);
            foreach (var root in source.GetRootGameObjects())
            {
                Sanitise(root);
                SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetParent(group.transform, true);
            }
            foreach (var t in group.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                    PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (mapId == "metronome-court") group.AddComponent<MenuMetronomeSwing>();
            EditorSceneManager.CloseScene(source, true);   // the source scene file is never saved
            SceneManager.SetActiveScene(scene);

            stage.Environment = group;
            if (existing >= 0)
            {
                // Refresh in place: same index, new copy of the arena.
                var old = showcase.Stages[existing].Environment;
                group.transform.SetSiblingIndex(old ? old.transform.GetSiblingIndex() : group.transform.GetSiblingIndex());
                if (old) UnityEngine.Object.DestroyImmediate(old);
                showcase.Stages[existing] = stage;
            }
            else showcase.Stages = showcase.Stages.Concat(new[] { stage }).ToArray();
            for (int i = 0; i < showcase.Stages.Length; i++) showcase.Stages[i].Environment.SetActive(i == 0);
            if (savedFog.Success)
            {
                RenderSettings.fogStartDistance = float.Parse(savedFog.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                RenderSettings.fogEndDistance = float.Parse(savedFog.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            EditorUtility.SetDirty(showcase);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log($"Showcase stage {index} '{map.Name}' {(existing >= 0 ? "refreshed" : "added")}: {group.GetComponentsInChildren<Renderer>(true).Length} renderers; {showcase.Stages.Length} stages.");
        }

        // Re-applies only the cast/orbit framing (and the camera-distance fog offset) to an already added stage, keeping its
        // baked arena copy as it is. Batch: -executeMethod LastBell.Menu.EditorTools.MenuShowcaseBuild.UpdateMetronomeCourtFraming
        [MenuItem("Last Bell/Main Menu/Update Metronome Court Showcase Framing", priority = 36)]
        public static void UpdateMetronomeCourtFraming()
        {
            UpdateStageFraming("metronome-court");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        public static void UpdateStageFraming(string mapId)
        {
            var map = RunMaps.All[RunMaps.IndexOf(mapId)];
            var savedFog = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(MainMenu.ScenePath),
                @"RenderSettings:[\s\S]*?m_LinearFogStart: ([-\d.eE]+)\s+m_LinearFogEnd: ([-\d.eE]+)");
            var scene = EditorSceneManager.OpenScene(MainMenu.ScenePath);
            var showcase = UnityEngine.Object.FindAnyObjectByType<MenuShowcase>();
            var stage = showcase ? showcase.Stages.FirstOrDefault(s => s.Name == map.Name) : null;
            if (stage == null) throw new InvalidOperationException(map.Name + " is not a showcase stage yet; run AddStage.");
            // Undo the previous camera-distance fog offset, then let ConfigureStage apply the one for the new camera.
            float previous = stage.CustomCamera ? Mathf.Max(0, Mathf.Round(stage.CameraPosition.magnitude - 36)) : 0;
            stage.FogStart -= previous; stage.FogEnd -= previous;
            ConfigureStage(mapId, stage);
            if (savedFog.Success)
            {
                RenderSettings.fogStartDistance = float.Parse(savedFog.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                RenderSettings.fogEndDistance = float.Parse(savedFog.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            EditorUtility.SetDirty(showcase); EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log($"Showcase framing for '{map.Name}': target {stage.CameraTarget}, position {stage.CameraPosition}, FOV {stage.FieldOfView}, offset {stage.ScreenOffsetDegrees}, fog {stage.FogStart}-{stage.FogEnd}.");
        }

        // Per-arena cast and orbit framing for stages added by AddStage. The original five stages keep the shared defaults.
        static void ConfigureStage(string mapId, MenuShowcase.Stage stage)
        {
            stage.Boss = "Bellkeeper2"; stage.PartySize = 4;
            if (mapId != "metronome-court") return;
            // The Tempo Tyrant has no final model yet (labelled stand-in), so the staged duel uses the Aurelion presenter.
            // Seven players completes the 3-8 party sizes across the stages.
            stage.Boss = "Aurelion"; stage.PartySize = 7;
            // Orbit centre sits between the 13.1 m floor disc and the metronome (z = -19.5, ~12 m tall), so both stay in
            // frame for the full 360 degrees at 16:9 and 21:9. The screen offset lifts the court up and right, clear of
            // the arena select's dark card band and title column.
            stage.CustomCamera = true;
            // Fitted offline over the full orbit (16:9 and 21:9): 46 m out at 62 degrees elevation is as close as the camera
            // can get while the metronome's frame stays on screen (x 0.31-0.86, y 0.09-0.98). The floor fills about the
            // same share of the screen as the other stages; like them, its near edge can pass under the card band.
            // Risxhb, October 25, 2026: orbit the court's centre like the other stages (default target, no screen offset,
            // default orbit speed). The camera sits a little farther out than the default because this floor is larger.
            stage.CameraTarget = new Vector3(0, 1, 0);
            stage.CameraPosition = stage.CameraTarget + new Vector3(0, 32f, 26.5f);
            stage.FieldOfView = 44; stage.OrbitDegreesPerSecond = 1.2f;
            stage.ScreenOffsetDegrees = Vector2.zero;
            // The arena's fog starts just beyond the far floor edge as seen from the gameplay camera (~36 m from the
            // centre). Push it back by however much farther out this camera sits, so the court stays clear.
            float extra = Mathf.Max(0, Mathf.Round(stage.CameraPosition.magnitude - 36));
            stage.FogStart += extra; stage.FogEnd += extra;
        }

        // Code-built arenas create their geometry at runtime. Build it now so the copied environment carries it,
        // then remove the builder so nothing in the menu ticks the arena's clocks, lighting or gameplay hooks.
        static void BakeCodeBuiltArena(Scene source)
        {
            var volumes = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include)
                .Select(v => (v, v.sharedProfile)).ToArray();
            foreach (var arena in source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LastBell.Arenas.MetronomeCourt.MetronomeCourtArena>(true)))
            {
                var build = typeof(LastBell.Arenas.MetronomeCourt.MetronomeCourtArena).GetMethod("Build",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (build == null) throw new MissingMethodException("MetronomeCourtArena.Build");
                build.Invoke(arena, null);
                if (!arena.transform.Find("Generated")) throw new InvalidOperationException("Metronome Court did not generate its geometry.");
                // Frozen at the arena's readiness look (non-damaging Haste preview), as the player sees it before a fight.
                if (arena.TempoSpotLight) { arena.TempoSpotLight.color = LastBell.Arenas.MetronomeCourt.MetronomeCourtArena.HasteLightTint; arena.TempoSpotLight.intensity = 110; }
                if (arena.TempoRimLight) { arena.TempoRimLight.color = LastBell.Arenas.MetronomeCourt.MetronomeCourtArena.HasteLightTint; arena.TempoRimLight.intensity = .5f; }
                UnityEngine.Object.DestroyImmediate(arena);
            }
            foreach (var (v, profile) in volumes) if (v) v.sharedProfile = profile;   // the builder's grade binding must not leak a profile copy
        }

        // Same ownership rules as Prepare(), plus any Tempo Tyrant / Metronome runtime behaviour left in the copy.
        static void Sanitise(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) continue;
                var type = behaviour.GetType();
                if (type.Name == "KeyArtArena" || behaviour is RunDirector || behaviour is RunHud || behaviour is UnityEngine.EventSystems.EventSystem
                    || type.Name.Contains("Tempo") || type.Name.Contains("Metronome")) behaviour.enabled = false;
            }
            foreach (var c in root.GetComponentsInChildren<Camera>(true)) { c.enabled = false; c.tag = "Untagged"; }
            foreach (var a in root.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
            foreach (var a in root.GetComponentsInChildren<AudioSource>(true)) { a.enabled = false; a.playOnAwake = false; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("ScaleReference", StringComparison.Ordinal) || t.name.StartsWith("Telegraph", StringComparison.Ordinal)) t.gameObject.SetActive(false);
        }

        public static void BuildWindows()
        {
            var output = Environment.GetEnvironmentVariable("LASTBELL_SHOWCASE_OUTPUT") ?? "E:/Collab/builds/menu-showcase-current/LastBell.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = MenuAppearance.BuildScenes,
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
