using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Screenshots of a fight for UI reviews (UI plan sub-thread D: encounter callouts), not a test:
    //   LastBell.exe -lastbellPractice <boss> -lastbellUiShots 4,8,12 [-lastbellShotName ult-p2] -lastbellOut <folder>
    // Saves <name>-<w>x<h>-t<s>.png that many seconds into combat (from the first Combat state), then quits.
    public sealed class UiShotCapture : MonoBehaviour
    {
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if ((Arg("-lastbellUiShots") == null && Arg("-lastbellCaptureRes") == null) || FindAnyObjectByType<UiShotCapture>()) return;
            var go = new GameObject("UI shot capture"); DontDestroyOnLoad(go); go.AddComponent<UiShotCapture>();
        }

        // -lastbellCaptureRes 1280x720: hold a windowed size for captures. The menu re-applies the player's saved resolution
        // (MenuSettings.Restore) over -screen-width; this only reads it back, never writes the saved settings.
        int resW, resH;
        void Awake()
        {
            var r = Arg("-lastbellCaptureRes"); if (r == null) return;
            var p = r.ToLowerInvariant().Split('x');
            if (p.Length == 2) { int.TryParse(p[0], out resW); int.TryParse(p[1], out resH); }
        }
        void LateUpdate()
        {
            if (resW > 0 && resH > 0 && (Screen.width != resW || Screen.height != resH || Screen.fullScreenMode != FullScreenMode.Windowed))
                Screen.SetResolution(resW, resH, FullScreenMode.Windowed);
        }

        IEnumerator Start()
        {
            if (Arg("-lastbellUiShots") == null) yield break;
            var times = Arg("-lastbellUiShots").Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).OrderBy(t => t).ToArray();
            string dir = Arg("-lastbellOut") ?? Path.Combine(Application.persistentDataPath, "ui-shots"), name = Arg("-lastbellShotName") ?? "shot";
            Directory.CreateDirectory(dir);
            float wait = 0;
            while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Combat) && wait < 120)
            {
                // a practice launch waits on its Ready screen: press Ready, as the tutorial autotest does
                if (RunDirector.Instance != null && RunDirector.Instance.State == RunState.Ready) RunDirector.Instance.RequestReady();
                wait += Time.unscaledDeltaTime; yield return null;
            }
            float t0 = Time.unscaledTime;
            foreach (var t in times)
            {
                while (Time.unscaledTime - t0 < t) yield return null;
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{name}-{Screen.width}x{Screen.height}-t{t:00}.png"), tex.EncodeToPNG()); Destroy(tex);
                Debug.Log($"[ui-shots] {name} t{t:0}");
            }
            Application.Quit(0);
        }
    }
}
