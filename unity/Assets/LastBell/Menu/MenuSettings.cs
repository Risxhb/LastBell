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
            ApplyFrameRate();   // a quality level carries its own vSyncCount; keep the player's choice
            PlayerPrefs.SetInt(Prefix + "Width", width); PlayerPrefs.SetInt(Prefix + "Height", height);
            PlayerPrefs.SetInt(Prefix + "Mode", mode); PlayerPrefs.SetInt(Prefix + "Quality", quality); PlayerPrefs.Save();
        }
        // Frame pacing (Risxhb, October 26, 2026): VSync off by default; an FPS cap that defaults to 120 (the old fixed
        // value). With VSync on the cap is ignored and frames follow the monitor. Saved separately from the display row so
        // it applies even before a resolution was ever saved. The world still steps at 60 Hz; SimSmoother draws between.
        public static readonly int[] FrameCaps = { 60, 120, 144, 240, -1 };   // -1 = unlimited
        public const int DefaultFrameCap = 120;
        public static bool VSync => PlayerPrefs.GetInt(Prefix + "VSync", 0) == 1;
        public static int FrameCap { get { int c = PlayerPrefs.GetInt(Prefix + "FrameCap", DefaultFrameCap); return Array.IndexOf(FrameCaps, c) >= 0 ? c : DefaultFrameCap; } }
        public static string CapName(int cap) => cap < 0 ? "Unlimited" : cap + " FPS";
        public static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : FrameCap;
        }
        public static void SetFrameRate(bool vsync, int cap)
        {
            PlayerPrefs.SetInt(Prefix + "VSync", vsync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "FrameCap", Array.IndexOf(FrameCaps, cap) >= 0 ? cap : DefaultFrameCap);
            PlayerPrefs.Save(); ApplyFrameRate();
        }
        public static void Restore()
        {
            ApplyFrameRate();
            if (!PlayerPrefs.HasKey(Prefix + "Width")) return;
            int w = PlayerPrefs.GetInt(Prefix + "Width"), h = PlayerPrefs.GetInt(Prefix + "Height");
            // A saved resolution from a disconnected monitor should not create an inaccessible window.
            w = Mathf.Clamp(w, 640, Mathf.Max(640, Display.main.systemWidth)); h = Mathf.Clamp(h, 360, Mathf.Max(360, Display.main.systemHeight));
#if !UNITY_WEBGL || UNITY_EDITOR
            Screen.SetResolution(w, h, Mode(PlayerPrefs.GetInt(Prefix + "Mode")));
#endif
            QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality"), 0, QualitySettings.names.Length - 1), true);
            ApplyFrameRate();
        }
    }
}
