using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The "Tutorial complete" panel beside the practice results. It lives on the run director, because the boss (and the
    // TutorialDirector on it) is destroyed when the fight clears. It removes itself when the next fight or the menu starts.
    public sealed class TutorialCompletePanel : MonoBehaviour
    {
        public static TutorialCompletePanel Current { get; private set; }
        RunDirector d; string body = "";
        GUIStyle sTitle, sBody, sSection; float styledFor = -1; static Texture2D panelTex;

        public static void Show(RunDirector director, IReadOnlyList<TutorialDirector.Record> records)
        {
            if (!director) return;
            var p = director.GetComponent<TutorialCompletePanel>() ?? director.gameObject.AddComponent<TutorialCompletePanel>();
            p.d = director; Current = p;
            int drills = records.Count(r => r.Tries > 1 || !r.Passed);
            var missed = records.Where(r => !r.Passed).Select(r => r.Title).ToList();
            p.body = "The Tempo Tyrant falls. You've seen every mechanic in the game so far: read the shape, step out, punish.\n\n"
                + (missed.Count == 0 ? "Every mechanic handled." : "To practise: " + string.Join(", ", missed) + ".")
                + (director.Mode == RunMode.Run ? "\n\nNext: Skyward Spire. Three minibosses guard Aurelion."
                    : "\n\nNext: Boss Rush to fight any boss for real, or Retry (R) to run the tutorial again.");
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void OnGUI()
        {
            if (!d || (d.State != RunState.Cleared && d.State != RunState.Results)) { if (d && d.State != RunState.Combat) Destroy(this); return; }
            GUI.depth = -20;
            float k = Screen.height / 1080f;
            if (!Mathf.Approximately(styledFor, k))
            {
                styledFor = k;
                sSection = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * k), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.93f, 0.74f, 0.38f) } };
                sTitle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * k), fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.86f, 0.5f) } };
                sBody = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19 * k), wordWrap = true, normal = { textColor = new Color(0.92f, 0.9f, 0.86f) } };
            }
            if (!panelTex) { panelTex = new Texture2D(1, 1); panelTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.85f)); panelTex.Apply(); }
            float w = 560 * k, x = 24 * k, y = 150 * k, pad = 18 * k, inner = w - 2 * pad;
            float bh = sBody.CalcHeight(new GUIContent(body), inner), h = pad + 26 * k + 44 * k + bh + pad;
            GUI.DrawTexture(new Rect(x, y, w, h), panelTex);
            GUI.DrawTexture(new Rect(x, y, 4 * k, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.93f, 0.74f, 0.38f), 0, 0);
            GUI.Label(new Rect(x + pad, y + pad, inner, 26 * k), "TUTORIAL", sSection);
            GUI.Label(new Rect(x + pad, y + pad + 26 * k, inner, 44 * k), "Tutorial complete", sTitle);
            GUI.Label(new Rect(x + pad, y + pad + 70 * k, inner, bh), body, sBody);
        }
    }
}
