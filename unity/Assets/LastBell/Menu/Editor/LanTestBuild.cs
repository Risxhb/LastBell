using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    public static class LanTestBuild
    {
        // The LAN lobby scene (LanLobbyScreen adds itself at runtime, so the scene holds only a camera and a light and
        // the editor, which doesn't compile LASTBELL_LAN_TEST code, can create it). Made once, then kept in Git.
        public const string LobbyScene = "Assets/LastBell/Menu/LanLobby.unity";
        public static void EnsureLobbyScene()
        {
            if (File.Exists(LobbyScene)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, LobbyScene);
            AssetDatabase.Refresh();
            Debug.Log("LAN test build: created " + LobbyScene);
        }
        public static string[] Scenes => MenuAppearance.BuildScenes.Concat(new[] { LobbyScene }).ToArray();
        // A distinct player compile symbol keeps this edition's navigation/restrictions out of the ordinary player.
        [MenuItem("Last Bell/Current Player/Build LAN Test Player")]
        public static void BuildWindows()
        {
            string output = Environment.GetEnvironmentVariable("LASTBELL_LAN_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set LASTBELL_LAN_OUTPUT to an isolated LastBell.exe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            EnsureLobbyScene();
            bool background = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.runInBackground = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes, locationPathName = output, target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None, extraScriptingDefines = new[] { "LASTBELL_LAN_TEST" }
                });
                Debug.Log($"LAN test build: {report.summary.result}, {report.summary.totalErrors} errors/{report.summary.totalWarnings} warnings, {report.summary.totalSize} bytes");
                foreach (var step in report.steps) foreach (var msg in step.messages) if (msg.type == LogType.Error || msg.type == LogType.Exception) Debug.LogError("LAN BUILD [" + step.name + "]: " + msg.content);
                if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            finally { PlayerSettings.runInBackground = background; }
        }
    }
}
