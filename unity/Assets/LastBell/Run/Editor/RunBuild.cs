using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Run.EditorTools
{
    // Last Bell > Run Loop: builds one run scene per map (a copy of that arena's scene with a RunDirector; see RunMaps),
    // the Resources materials the runtime needs in a player build, and the Windows player. The test map's run scene
    // (RunArena.unity) is the first build scene, so the player boots there; the menu loads the others.
    //   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -quit
    public static class RunBuild
    {
        public static string RunScene => RunMaps.All[0].RunScene;
        const string ResDir = "Assets/LastBell/Run/Resources/Run";
        public const string BuildPath = "Builds/Windows/LastBell.exe";
        // -lastbellBuildOut <path>: build somewhere else (e.g. while the default build is open for a playtest)
        static string OutPath()
        {
            var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "-lastbellBuildOut");
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : BuildPath;
        }

        // Look-check props the area builders leave in their scenes; they don't belong in a fight.
        static readonly string[] StripNames = { "ScaleReference_Vanguard", "TelegraphCheck", "TelegraphTest" };

        // Arena scenes that are generated rather than committed (Area 2 ignores its ~47 MB scene and meshes) are built first.
        static readonly System.Collections.Generic.Dictionary<string, System.Action> SourceBuilders = new System.Collections.Generic.Dictionary<string, System.Action>
        {
            ["skyward-spire"] = LastBell.Areas.Area1.EditorTools.SkywardSpireBuilder.Build,
            ["brass-foundry"] = LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.Build,
            ["verdant-chimes"] = LastBell.Areas.Area3.EditorTools.VerdantChimesBuilder.Build,
            ["shattered-hour"] = LastBell.Areas.Area4.EditorTools.ShatteredHourArenaBuilder.Build,
            ["metronome-court"] = LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtBuilder.Build,
        };

        [MenuItem("Last Bell/Run Loop/Rebuild Run Scenes (all maps)", priority = 20)]
        public static void BuildScene()
        {
            EnsureMaterials();
            foreach (var map in RunMaps.All) BuildMapScene(map);
            var runScenes = RunMaps.All.Select(m => m.RunScene).ToArray();
            var others = EditorBuildSettings.scenes.Where(s => !runScenes.Contains(s.path));
            EditorBuildSettings.scenes = runScenes.Select(p => new EditorBuildSettingsScene(p, true)).Concat(others).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(RunScene, OpenSceneMode.Single);
            Debug.Log("Last Bell: run scenes rebuilt: " + string.Join(", ", runScenes));
        }

        static void BuildMapScene(RunMaps.Map map)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(map.SourceScene) == null && SourceBuilders.TryGetValue(map.Id, out var build))
            {
                Debug.Log($"Last Bell: generating {map.SourceScene} with its area builder");
                build();
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(map.SourceScene) == null) { Debug.LogError("Last Bell: arena scene missing: " + map.SourceScene); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(map.RunScene));
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(map.RunScene) != null) AssetDatabase.DeleteAsset(map.RunScene);
            AssetDatabase.CopyAsset(map.SourceScene, map.RunScene);
            var scene = EditorSceneManager.OpenScene(map.RunScene, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => StripNames.Contains(t.name)))
                    t.gameObject.SetActive(false);   // not destroyed: some sit inside prefab instances
            new GameObject("Run Director").AddComponent<RunDirector>();
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Last Bell/Run Loop/Open Run Scene", priority = 21)]
        static void Open() { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RunScene) == null) BuildScene(); EditorSceneManager.OpenScene(RunScene); } }

        static void EnsureMaterials()
        {
            Directory.CreateDirectory(ResDir);
            Mat("PrimLit", Shader.Find("Universal Render Pipeline/Lit"), m => { m.SetFloat("_Cull", 0); });
            Mat("PrimLitGlow", Shader.Find("Universal Render Pipeline/Lit"), m => { m.SetFloat("_Cull", 0); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; });
            Mat("FX_ColorDissolve", Shader.Find("LastBell/FX/ColorDissolve"), null);
            Mat("FX_Telegraph", Shader.Find("LastBell/FX/Telegraph"), null);
            AssetDatabase.SaveAssets();
        }
        static void Mat(string name, Shader sh, System.Action<Material> setup)
        {
            if (sh == null) { Debug.LogError("Last Bell: shader missing for " + name); return; }
            var path = $"{ResDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; setup?.Invoke(m); EditorUtility.SetDirty(m);
        }

        [MenuItem("Last Bell/Run Loop/Build Windows Player", priority = 22)]
        public static void BuildWindows()
        {
            BuildScene();
            var opts = new BuildPlayerOptions
            {
                scenes = RunMaps.All.Select(m => m.RunScene).ToArray(),
                locationPathName = OutPath(),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"Last Bell build: {s.result}, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024 * 1024)} MB, {s.totalTime} -> {Path.GetFullPath(OutPath())}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        // Editor Play Mode run of the same autotest (not batch mode: Play Mode needs the editor loop). Launch with
        //   Unity.exe -projectPath unity -executeMethod LastBell.Run.EditorTools.RunBuild.PlayModeAutotest -lastbellAutotest -lastbellOut <dir>
        // RunAutoTest stops Play Mode and exits the editor with 0 (all passed) or 1 when it finishes.
        public static void PlayModeAutotest()
        {
            BuildScene();
            EditorSceneManager.OpenScene(RunScene);
            EditorApplication.isPlaying = true;
        }

        // Batch compile check only (no build): -executeMethod LastBell.Run.EditorTools.RunBuild.CompileCheck
        public static void CompileCheck()
        {
            Debug.Log("Last Bell: scripts compiled; run scene " + (AssetDatabase.LoadAssetAtPath<SceneAsset>(RunScene) != null ? "present" : "missing"));
            if (Application.isBatchMode) EditorApplication.Exit(EditorUtility.scriptCompilationFailed ? 1 : 0);
        }
    }
}
