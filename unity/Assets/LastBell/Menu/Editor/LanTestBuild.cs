using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    public static class LanTestBuild
    {
        // A distinct player compile symbol keeps this edition's navigation/restrictions out of the ordinary player.
        [MenuItem("Last Bell/Current Player/Build LAN Test Player")]
        public static void BuildWindows()
        {
            string output = Environment.GetEnvironmentVariable("LASTBELL_LAN_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Set LASTBELL_LAN_OUTPUT to an isolated LastBell.exe path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            bool background = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.runInBackground = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = MenuAppearance.BuildScenes, locationPathName = output, target = BuildTarget.StandaloneWindows64,
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
