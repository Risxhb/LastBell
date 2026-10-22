using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using LastBell.Run;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LastBell.Menu.EditorTools
{
    // Builds the committed menu and run maps without regenerating any scenes or source assets.
    public static class WebDemoBuild
    {
        [MenuItem("Last Bell/Main Menu/Build Browser Demo", priority = 33)]
        public static void Build()
        {
            var scenes = new[] { MainMenu.ScenePath }.Concat(RunMaps.All.Select(m => m.RunScene)).ToArray();
            foreach (var scene in scenes)
                if (!File.Exists(scene)) throw new FileNotFoundException("Required demo scene is missing: " + scene + ". Prepare generated maps first; see docs/main-menu/web-demo.md.", scene);
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("Install Web Build Support for this Unity editor first.");

            PlayerSettings.WebGL.template = "PROJECT:LastBell";
            // Pages cannot configure Content-Encoding or cross-origin isolation headers.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.companyName = "Last Bell";
            PlayerSettings.productName = "Last Bell";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            var output = Environment.GetEnvironmentVariable("LASTBELL_WEB_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = "Builds/Web/lastbell";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes, locationPathName = output, target = BuildTarget.WebGL, options = BuildOptions.None
            });
            Debug.Log($"Last Bell browser build: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Last Bell browser build failed; see the build log.");
            PackageData(output);
            File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
        }

        [Serializable] sealed class DataManifest { public long totalBytes; public DataPart[] parts; }
        [Serializable] sealed class DataPart { public string file, sha256; public int bytes; }

        // Regular Git rejects files over 100 MiB. Keep each generated download at 64 MiB or less.
        public static void PackageData(string output)
        {
            var build = Path.Combine(output, "Build");
            var data = Directory.GetFiles(build, "*.data.unityweb").Single();
            var parts = new List<DataPart>();
            long totalBytes = new FileInfo(data).Length;
            using (var input = File.OpenRead(data))
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[64 * 1024 * 1024];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var digest = BitConverter.ToString(hash.ComputeHash(buffer, 0, count)).Replace("-", "").ToLowerInvariant();
                    var file = "data-" + digest + ".bin";
                    using (var part = File.Create(Path.Combine(build, file))) part.Write(buffer, 0, count);
                    parts.Add(new DataPart { file = file, sha256 = digest, bytes = count });
                }
            }
            File.WriteAllText(Path.Combine(build, "data-manifest.json"), JsonUtility.ToJson(new DataManifest { totalBytes = totalBytes, parts = parts.ToArray() }, true));
            File.Delete(data); // Only the just-built oversized file; its bytes are preserved in the verified parts.
            Debug.Log($"Last Bell: packaged {totalBytes} data bytes in {parts.Count} parts.");
        }
    }
}
