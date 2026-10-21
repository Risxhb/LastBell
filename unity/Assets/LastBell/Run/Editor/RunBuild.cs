using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Run.EditorTools
{
    // Last Bell > Run Loop: builds the run scene (a copy of the key-art arena with a RunDirector), the Resources
    // materials the runtime needs in a player build, and the Windows player.
    //   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Run.EditorTools.RunBuild.BuildWindows -quit
    public static class RunBuild
    {
        public const string ArenaScene = "Assets/LastBell/Arenas/KeyArt/KeyArtTestArena.unity";
        public const string RunScene = "Assets/LastBell/Run/RunArena.unity";
        const string ResDir = "Assets/LastBell/Run/Resources/Run";
        public const string BuildPath = "Builds/Windows/LastBell.exe";

        [MenuItem("Last Bell/Run Loop/Rebuild Run Scene", priority = 20)]
        public static void BuildScene()
        {
            EnsureMaterials();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RunScene) != null) AssetDatabase.DeleteAsset(RunScene);
            AssetDatabase.CopyAsset(ArenaScene, RunScene);
            var scene = EditorSceneManager.OpenScene(RunScene, OpenSceneMode.Single);
            new GameObject("Run Director").AddComponent<RunDirector>();
            EditorSceneManager.SaveScene(scene);
            var others = EditorBuildSettings.scenes.Where(s => s.path != RunScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RunScene, true) }.Concat(others).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Last Bell: run scene rebuilt at " + RunScene);
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
                scenes = new[] { RunScene },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"Last Bell build: {s.result}, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024 * 1024)} MB, {s.totalTime} -> {Path.GetFullPath(BuildPath)}");
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
