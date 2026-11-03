using UnityEngine;

namespace LastBell.Run
{
    // Warms Aurelion's world-label glyphs before the fight (perf capture 3 Nov: the first "NIGHT ☾ / DAY ☉" at the 40 % step
    // loaded the font's fallback glyphs inside one frame, 59 ms). Added by AurelionEncounter.Build; one size per OnGUI
    // frame, the same way the run HUD warms its own fonts, then it removes itself. The labels use the plain bold skin label
    // (RunHud.Labels) at these sizes.
    public sealed class AurelionWarmUp : MonoBehaviour
    {
        static readonly int[] Sizes = { 26, 30, 44, 46, 54 };
        static string chars;
        int step;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (chars == null)
            {
                var sb = new System.Text.StringBuilder("☾ ☉ • > ");
                for (char ch = ' '; ch <= '~'; ch++) sb.Append(ch);
                chars = sb.ToString();
            }
            var s = new GUIStyle(GUI.skin.label) { fontSize = Sizes[step], fontStyle = FontStyle.Bold };
            s.CalcSize(new GUIContent(chars));
            var font = s.font != null ? s.font : GUI.skin.font;
            if (font != null && font.dynamic) font.RequestCharactersInTexture(chars, s.fontSize, s.fontStyle);
            if (++step >= Sizes.Length) Destroy(this);
        }
    }
}
