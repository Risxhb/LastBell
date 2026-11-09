using UnityEngine;

namespace LastBell.Run
{
    // Warms Aurelion's world-label and banner text before the fight (perf captures 3 Nov: the first "NIGHT ☾ / DAY ☉" at the
    // 40 % step loaded the font's fallback glyphs inside one frame, 59 ms; the first Phase 2 labels laid out new text, 23-30 ms).
    // Added by AurelionEncounter.Build. IMGUI lays text out through TextCore, which loads glyphs and tables only on a real
    // draw (CalcSize and Font.RequestCharactersInTexture only churn the legacy atlas, perf 3 Nov), so each OnGUI repaint
    // draws the whole set once with GUI.Label, invisible, at one size and style (the run HUD's labels, banners and
    // captions use the plain skin label), then it removes itself.
    public sealed class AurelionWarmUp : MonoBehaviour
    {
        static readonly (int size, FontStyle style)[] Steps =
        {
            (20, FontStyle.Bold), (26, FontStyle.Bold), (28, FontStyle.Bold), (30, FontStyle.Bold),
            (44, FontStyle.Bold), (46, FontStyle.Bold), (54, FontStyle.Bold), (20, FontStyle.Normal),
            (0, FontStyle.Normal),   // 0: the boss block's HudExtra line ("CORONA SHIELD n  ·  clock"), the run HUD's own numeric style
        };
        static string chars;
        int step;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (chars == null)
            {
                var sb = new System.Text.StringBuilder("☾ ☉ • — · > ");   // "—" first shows in the Total Eclipse banner (perf 3 Nov, 25 ms)
                for (char ch = ' '; ch <= '~'; ch++) sb.Append(ch);
                chars = sb.ToString();
            }
            var (size, style) = Steps[step];
            var s = size > 0 ? new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow }
                             : UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleRight, UiTheme.Ivory);
            s.normal.textColor = new Color(1, 1, 1, 0);
            GUI.Label(new Rect(0, 0, Screen.width, s.fontSize * 1.5f), chars, s);
            if (++step >= Steps.Length) Destroy(this);
        }
    }
}

namespace LastBell.Run
{
    // -lastbellAurelionTimeline (performance captures, 3 Nov): logs block starts, phase changes, the planets' arrival and
    // every dynamic font texture rebuild with the frame number, so a frame spike can be matched to what the fight did.
    public static class AurelionTimeline
    {
        public static readonly bool On = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellAurelionTimeline", System.StringComparison.OrdinalIgnoreCase));
        static bool hooked;
        public static void Note(string what)
        {
            if (!On) return;
            if (!hooked) { hooked = true; UnityEngine.Font.textureRebuilt += f => Note("font texture rebuilt: " + (f ? f.name : "?")); }
            UnityEngine.Debug.Log($"[aurelion timeline] frame {UnityEngine.Time.frameCount} t {UnityEngine.Time.realtimeSinceStartup:F3} {what}");
        }
    }
}
