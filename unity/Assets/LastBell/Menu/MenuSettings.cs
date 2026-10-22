using System;
using System.Linq;
using UnityEngine;

namespace LastBell.Menu
{
    // Display controls are independent of MenuMusic's scene-scoped volume preference.
    public static class MenuSettings
    {
        const string Prefix = "LastBell.Menu.";
        public static Vector2Int[] Resolutions => Screen.resolutions.Select(r => new Vector2Int(r.width, r.height))
            .Append(new Vector2Int(Screen.width, Screen.height)).Distinct().OrderBy(r => r.x).ThenBy(r => r.y).ToArray();
        public static FullScreenMode Mode(int value) => value == 0 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        public static int DisplayMode => Screen.fullScreenMode == FullScreenMode.Windowed ? 0 : 1;
        public static void Apply(int width, int height, int mode, int quality)
        {
            if (width < 640 || height < 360 || quality < 0 || quality >= QualitySettings.names.Length)
                throw new ArgumentOutOfRangeException(nameof(width), "Unsupported display settings");
#if !UNITY_WEBGL || UNITY_EDITOR
            Screen.SetResolution(width, height, Mode(mode));
#endif
            QualitySettings.SetQualityLevel(quality, true);
            PlayerPrefs.SetInt(Prefix + "Width", width); PlayerPrefs.SetInt(Prefix + "Height", height);
            PlayerPrefs.SetInt(Prefix + "Mode", mode); PlayerPrefs.SetInt(Prefix + "Quality", quality); PlayerPrefs.Save();
        }
        public static void Restore()
        {
            if (!PlayerPrefs.HasKey(Prefix + "Width")) return;
            int w = PlayerPrefs.GetInt(Prefix + "Width"), h = PlayerPrefs.GetInt(Prefix + "Height");
            // A saved resolution from a disconnected monitor should not create an inaccessible window.
            w = Mathf.Clamp(w, 640, Mathf.Max(640, Display.main.systemWidth)); h = Mathf.Clamp(h, 360, Mathf.Max(360, Display.main.systemHeight));
#if !UNITY_WEBGL || UNITY_EDITOR
            Screen.SetResolution(w, h, Mode(PlayerPrefs.GetInt(Prefix + "Mode")));
#endif
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality"), 0, QualitySettings.names.Length - 1), true);
        }
    }
}
