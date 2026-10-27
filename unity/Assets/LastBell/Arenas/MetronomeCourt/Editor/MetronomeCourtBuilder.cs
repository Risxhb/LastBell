using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LastBell.Arenas.MetronomeCourt.EditorTools
{
    // Last Bell > Metronome Court: writes the small source scene (camera, lights, atmosphere and the code-built
    // MetronomeCourtArena) and its run scene (a copy plus a RunDirector, exactly like RunBuild does for the other maps),
    // and adds only that run scene to the build settings. It never regenerates or touches any other map's scenes.
    //   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtBuilder.BuildAll -quit
    public static class MetronomeCourtBuilder
    {
        public const string MapId = "metronome-court";
        static RunMaps.Map Map => RunMaps.All[RunMaps.IndexOf(MapId)];

        [MenuItem("Last Bell/Metronome Court/Build Source + Run Scene", priority = 40)]
        public static void BuildAll()
        {
            Build();
            BuildRunScene();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Editor Play Mode acceptance run (needs graphics, not -batchmode):
        //   Unity.exe -projectPath unity -executeMethod LastBell.Arenas.MetronomeCourt.EditorTools.MetronomeCourtBuilder.PlayModeTest -tempoTyrantTest -lastbellOut <dir>
        [MenuItem("Last Bell/Metronome Court/Open Run Scene", priority = 41)]
        public static void OpenRunScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Map.RunScene) == null) BuildRunScene();
            EditorSceneManager.OpenScene(Map.RunScene, OpenSceneMode.Single);
        }
        public static void PlayModeTest() { OpenRunScene(); EditorApplication.isPlaying = true; }

        public static void Build()
        {
            var path = Map.SourceScene;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.09f, 0.14f);
            cam.fieldOfView = 44; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400;
            camGo.transform.SetPositionAndRotation(new Vector3(0, 29, 22.5f), Quaternion.LookRotation(new Vector3(0, 1, 1) - new Vector3(0, 29, 22.5f)));
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true; camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // post-processing, as the area builders do: ACES, bloom, navy vignette, slight desaturation
            var volume = new GameObject("Post").AddComponent<Volume>(); volume.isGlobal = true;
            string profilePath = Path.GetDirectoryName(path).Replace("\\", "/") + "/MetronomeCourtPost.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.5f); bloom.scatter.Override(0.65f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.28f); vig.color.Override(new Color(0.04f, 0.06f, 0.12f));
            var ca = profile.Add<ColorAdjustments>(true); ca.contrast.Override(10f); ca.saturation.Override(-8f); ca.postExposure.Override(0.15f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, profile); }
            AssetDatabase.SaveAssets(); volume.sharedProfile = profile;

            var sun = new GameObject("Key light (dusk)").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.86f, 0.70f); sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52, 152, 0);
            var fill = new GameObject("Fill light (teal sky)").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.45f, 0.62f, 0.75f); fill.intensity = 0.35f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(35, -30, 0);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.42f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.30f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.10f, 0.13f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.12f, 0.16f, 0.22f); RenderSettings.fogStartDistance = 15; RenderSettings.fogEndDistance = 105;
            RenderSettings.skybox = null;

            new GameObject("Metronome Court").AddComponent<MetronomeCourtArena>();
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("Metronome Court: source scene written " + path);
        }

        // The current combined player (same scene list and options as Current Player > Build Windows Player), built to a
        // separate output without running any menu-asset preparation:
        //   -executeMethod ...MetronomeCourtBuilder.BuildWindowsPlayer   (LASTBELL_WINDOWS_OUTPUT selects the exe path)
        public static void BuildWindowsPlayer()
        {
            var output = System.Environment.GetEnvironmentVariable("LASTBELL_WINDOWS_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = "Builds/TempoTyrant/LastBell.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = LastBell.Menu.MenuAppearance.BuildScenes, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            Debug.Log($"Tempo Tyrant player build: {report.summary.result}, {report.summary.totalErrors} errors/{report.summary.totalWarnings} warnings -> {output}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        public static void BuildRunScene()
        {
            var map = Map;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(map.SourceScene) == null) Build();
            Directory.CreateDirectory(Path.GetDirectoryName(map.RunScene));
            AssetDatabase.Refresh();
            // overwrite in place (never delete + copy) so the run scene keeps its GUID and the build settings stay valid
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(map.RunScene) != null) { File.Copy(map.SourceScene, map.RunScene, true); AssetDatabase.ImportAsset(map.RunScene, ImportAssetOptions.ForceUpdate); }
            else AssetDatabase.CopyAsset(map.SourceScene, map.RunScene);
            var scene = EditorSceneManager.OpenScene(map.RunScene, OpenSceneMode.Single);
            new GameObject("Run Director").AddComponent<RunDirector>();
            EditorSceneManager.SaveScene(scene);
            if (!EditorBuildSettings.scenes.Any(s => s.path == map.RunScene))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(map.RunScene, true) }).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Metronome Court: run scene written and registered " + map.RunScene);
        }
    }
}
