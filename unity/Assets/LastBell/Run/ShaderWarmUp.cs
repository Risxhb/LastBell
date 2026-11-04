using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastBell.Run
{
    // Shader warm-up at load (Chzy18, 2 Nov; perf report bellkeeper-ultimate-2026-11-02: the 24 ms frame at the Ultimate's
    // slam 6 waits on the GPU for a shader compiled on first use). On DX12 a plain ShaderVariantCollection only half-warms
    // (it doesn't know the render state), so this uses Unity 6's GraphicsStateCollection: a recorded list of every shader
    // variant plus render state the game drew, compiled up front while the menu shows.
    //   Record:  a development build (Unity.exe ... -executeMethod ...RunBuild.BuildWindows -lastbellDevelopment), then
    //            LastBell.exe -lastbellAutotest -lastbellRecordShaders <file> -lastbellOut <dir>   (traces the run, saves on quit;
    //            an existing <file> is added to, so several suites can record into one collection)
    //   Ship:    copy the file to Assets/StreamingAssets/LastBell.graphicsstate; every boot then warms it.
    //   Off:     -lastbellNoShaderWarmUp
    // A collection recorded on another graphics API or quality level is skipped (it would compile the wrong programs).
    public static class ShaderWarmUp
    {
        public const string FileName = "LastBell.graphicsstate";
        // pipeline states compiled per frame while warming: small enough that the menu stays smooth, large enough to finish in a few seconds
        const int PerFrame = 24;

        public static GraphicsStateCollection Collection { get; private set; }
        public static bool Done => Collection == null || Collection.isWarmedUp;
        public static string Status { get; private set; } = "not started";
        static GraphicsStateCollection trace; static string tracePath;

        static bool Has(string flag) => Environment.GetCommandLineArgs().Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        static string Arg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            tracePath = Arg("-lastbellRecordShaders");
            if (!string.IsNullOrEmpty(tracePath))
            {
                // record instead of warming: a warmed state would never be traced. Tracing starts on the first frame:
                // before the first scene loads, BeginTrace refuses.
                var rec = new GameObject("Shader recorder") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(rec);
                rec.AddComponent<Recorder>();
                Application.quitting += SaveTrace;
                Status = "recording to " + tracePath;
                return;
            }
            if (Has("-lastbellNoShaderWarmUp")) { Status = "off (-lastbellNoShaderWarmUp)"; return; }
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (!File.Exists(path)) { Status = "no recorded collection"; Debug.Log("[shaders] " + Status); return; }
            var c = new GraphicsStateCollection();
            if (!c.LoadFromFile(path)) { Status = "could not load " + FileName; Debug.LogWarning("[shaders] " + Status); return; }
            if (c.graphicsDeviceType != SystemInfo.graphicsDeviceType || c.runtimePlatform != Application.platform)
            {
                Status = $"skipped: recorded on {c.graphicsDeviceType}/{c.runtimePlatform}, running {SystemInfo.graphicsDeviceType}/{Application.platform}";
                Debug.Log("[shaders] " + Status);
                return;
            }
            Collection = c;
            float t0 = Time.realtimeSinceStartup;
            Status = $"warming {c.variantCount} variants, {c.totalGraphicsStateCount} states";
            Debug.Log("[shaders] " + Status);
            var go = new GameObject("Shader warm-up") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().t0 = t0;
        }

        static void SaveTrace()
        {
            if (trace == null) return;
            trace.EndTrace();
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(tracePath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                bool ok = trace.SaveToFile(tracePath);
                Debug.Log($"[shaders] recorded {trace.variantCount} variants, {trace.totalGraphicsStateCount} states -> {tracePath} ({(ok ? "saved" : "save failed")})");
            }
            catch (Exception e) { Debug.LogWarning("[shaders] saving the recording failed: " + e.Message); }
            trace = null;
        }

        sealed class Recorder : MonoBehaviour
        {
            int frames;
            void Update()
            {
                if (trace == null && frames++ < 600)
                {
                    var t = new GraphicsStateCollection();
                    if (File.Exists(tracePath) && !t.LoadFromFile(tracePath)) Debug.LogWarning("[shaders] could not load the existing recording; starting a new one");
                    if (!Debug.isDebugBuild) { Debug.LogWarning("[shaders] recording needs a development build (-lastbellDevelopment); nothing is traced"); frames = 600; return; }
                    if (t.BeginTrace()) { trace = t; Debug.Log($"[shaders] tracing from frame {Time.frameCount} ({SystemInfo.graphicsDeviceType}) -> {tracePath}"); }
                    else if (frames == 1 || frames == 600) Debug.LogWarning($"[shaders] BeginTrace refused on frame {Time.frameCount}");
                }
                else if (trace != null && Time.frameCount % 1200 == 0)
                    Debug.Log($"[shaders] frame {Time.frameCount}: tracing {trace.isTracing}, {trace.variantCount} variants, {trace.totalGraphicsStateCount} states so far");
            }
        }

        sealed class Runner : MonoBehaviour
        {
            public float t0; bool started;
            void Update()
            {
                if (!started) { started = true; Collection.WarmUpProgressively(PerFrame, default); return; }
                if (!Collection.isWarmedUp) { Collection.WarmUpProgressively(PerFrame, default); return; }
                Status = $"warmed {Collection.totalGraphicsStateCount} states in {Time.realtimeSinceStartup - t0:0.0} s";
                Debug.Log("[shaders] " + Status);
                Destroy(gameObject);
            }
        }
    }
}
