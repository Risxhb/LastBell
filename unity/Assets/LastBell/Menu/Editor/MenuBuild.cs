using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    public static class MenuBuild
    {
        public const string PlayerPath = "Builds/Menu/LastBell.exe";
        [MenuItem("Last Bell/Main Menu/Create or Refresh Scene", priority = 30)]
        public static void BuildScene()
        {
            ConfigureMedia();
            // Save a copy before editing: never regenerate the source arena, maps, materials or pending assets.
            var scene = EditorSceneManager.OpenScene(RunMaps.All[0].SourceScene);
            if (!EditorSceneManager.SaveScene(scene, MainMenu.ScenePath, true)) throw new IOException("Could not create main menu scene");
            scene = EditorSceneManager.OpenScene(MainMenu.ScenePath);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour && (behaviour.GetType().Name == "KeyArtArena" || behaviour is RunDirector || behaviour is RunHud))
                        Object.DestroyImmediate(behaviour);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "ScaleReference_Vanguard" || t.name == "TelegraphCheck" || t.name == "TelegraphTest") t.gameObject.SetActive(false);
            }
            var menu = new GameObject("Last Bell Main Menu");
            menu.AddComponent<MainMenu>(); menu.AddComponent<MenuBattle>(); menu.AddComponent<MenuMusic>();
            EditorSceneManager.SaveScene(scene);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != MainMenu.ScenePath).ToArray();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainMenu.ScenePath, true) }.Concat(existing).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Last Bell: main menu created; existing run scenes and settings preserved.");
        }
        static void ConfigureMedia()
        {
            AssetDatabase.Refresh();
            const string logo = "Assets/LastBell/Menu/Resources/Menu/LastBellLogo.png";
            var texture = AssetImporter.GetAtPath(logo) as TextureImporter;
            if (texture && (texture.textureType != TextureImporterType.Sprite || !texture.alphaIsTransparency))
            {
                texture.textureType = TextureImporterType.Sprite; texture.spriteImportMode = SpriteImportMode.Single;
                texture.alphaIsTransparency = true; texture.mipmapEnabled = false; texture.maxTextureSize = 4096;
                texture.textureCompression = TextureImporterCompression.Uncompressed; texture.SaveAndReimport();
            }
            const string music = "Assets/LastBell/Menu/Resources/Menu/MainMenuMusic.wav";
            var audio = AssetImporter.GetAtPath(music) as AudioImporter;
            if (audio && audio.defaultSampleSettings.loadType != AudioClipLoadType.Streaming)
            {
                var sample = audio.defaultSampleSettings; sample.loadType = AudioClipLoadType.Streaming;
                sample.compressionFormat = AudioCompressionFormat.Vorbis; sample.quality = 0.8f;
                sample.preloadAudioData = false; audio.defaultSampleSettings = sample; audio.loadInBackground = true;
                audio.SaveAndReimport();
            }
        }
        [MenuItem("Last Bell/Main Menu/Open Scene", priority = 31)]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(MainMenu.ScenePath)) BuildScene(); else EditorSceneManager.OpenScene(MainMenu.ScenePath);
        }
        [MenuItem("Last Bell/Main Menu/Build Windows Player", priority = 32)]
        public static void BuildWindows()
        {
            var scenes = new[] { MainMenu.ScenePath,HeroSelection.ScenePath }.Concat(RunMaps.All.Select(m => m.RunScene)).ToArray();
            foreach (var path in scenes) if (!File.Exists(path)) throw new FileNotFoundException("Build the existing run scenes first", path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            Debug.Log($"Last Bell menu build: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        public static void BuildLocalDevelopment()
        {
            var scenes=new[]{MainMenu.ScenePath,HeroSelection.ScenePath}.Concat(RunMaps.All.Select(m=>m.RunScene)).ToArray();
            foreach(var path in scenes)if(!File.Exists(path))throw new FileNotFoundException("Required current scene is missing; do not regenerate over local edits",path);
            const string pathOut="E:/Collab/builds/menu-flow-20261022/LastBell.exe";Directory.CreateDirectory(Path.GetDirectoryName(pathOut));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=pathOut,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            var result=$"{report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings, {report.summary.totalSize} bytes, {report.summary.totalTime}";
            File.WriteAllText("E:/Collab/build-evidence/menu-flow-20261022/build-result.txt",result);Debug.Log("Local menu flow build: "+result);
            if(Application.isBatchMode)EditorApplication.Exit(report.summary.result==BuildResult.Succeeded?0:1);
        }
        public static void PrepareSelection()
        {
            // Copy the existing environment, never regenerate the source arena or the existing menu.
            if (!File.Exists(HeroSelection.ScenePath))
            {
                AssetDatabase.CopyAsset(MainMenu.ScenePath, HeroSelection.ScenePath);
                var scene = EditorSceneManager.OpenScene(HeroSelection.ScenePath);
                foreach (var root in scene.GetRootGameObjects())
                    if (root.GetComponent<MainMenu>()) Object.DestroyImmediate(root);
                new GameObject("Hero selection").AddComponent<HeroSelection>();
                EditorSceneManager.SaveScene(scene);
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != MainMenu.ScenePath && s.path != HeroSelection.ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainMenu.ScenePath,true), new EditorBuildSettingsScene(HeroSelection.ScenePath,true) }.Concat(scenes).ToArray();
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        // Preview scenes need their runtime initialization; batch single-camera rendering cannot initialize every URP volume stack.
        public static void CaptureMapPreviews()
        {
            MapPreviewCapture.AuthoringRequested=true;
            PlayFlowTest();
        }
        public static void PlayModeSmokeTest()
        {
            EditorSceneManager.OpenScene(MainMenu.ScenePath);
            EditorApplication.playModeStateChanged += FocusGame;
            EditorApplication.isPlaying = true;
        }
        public static void PlayFlowTest()
        {
            EditorSceneManager.OpenScene(MainMenu.ScenePath);
            EditorApplication.playModeStateChanged += FocusGame;
            EditorApplication.isPlaying = true;
        }
        static void FocusGame(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= FocusGame;
            var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            game.Show(); game.Focus();
        }
    }
}
