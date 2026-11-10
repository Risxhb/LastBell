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
        // The LAN lobby scene (LanLobbyScreen adds itself at runtime, so the scene holds only a camera and a light).
        // Made once, then kept in Git.
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
        // One build path (10 Nov 2026): LAN ships in the main player, so the old LAN-test build is now the normal
        // Windows build. LASTBELL_LAN_OUTPUT still names the output, for older scripts.
        [MenuItem("Last Bell/Current Player/Build LAN Test Player")]
        public static void BuildWindows()
        {
            string output = Environment.GetEnvironmentVariable("LASTBELL_LAN_OUTPUT");
            if (!string.IsNullOrEmpty(output)) Environment.SetEnvironmentVariable("LASTBELL_WINDOWS_OUTPUT", output);
            ConfrontationMenuBuild.BuildWindows();
        }
    }
}
