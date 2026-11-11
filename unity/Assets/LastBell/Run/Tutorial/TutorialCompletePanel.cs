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
        RunDirector d; string lead = "", tail = "", body = ""; bool practice; InputHints.Device bodyFor; bool built;
        GUIStyle sTitle, sBody; int styledFor = -1; readonly GUIContent measure = new GUIContent();

        public static void Show(RunDirector director, IReadOnlyList<TutorialDirector.Record> records)
        {
            if (!director) return;
            var p = director.GetComponent<TutorialCompletePanel>() ?? director.gameObject.AddComponent<TutorialCompletePanel>();
            p.d = director; Current = p;
            int drills = records.Count(r => r.Tries > 1 || !r.Passed);
            var missed = records.Where(r => !r.Passed).Select(r => r.Title).ToList();
            p.lead = "The Tempo Tyrant falls. You've seen every mechanic in the game so far: read the shape, step out, punish.\n\n"
                + (missed.Count == 0 ? "Every mechanic handled." : "To practise: " + string.Join(", ", missed) + ".");
            p.practice = director.Mode != RunMode.Run; p.built = false;
            p.tail = p.practice ? "" : "\n\nNext: Skyward Spire. Three minibosses guard Aurelion.";
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // The retry hint comes from InputHints ("Retry (R)"; no key on a gamepad, where Retry is a focusable row), so the
        // body is rebuilt when the last device used changes.
        string Body()
        {
            if (built && bodyFor == InputHints.Last) return body;
            built = true; bodyFor = InputHints.Last;
            var retry = InputHints.Key(HintAction.Retry);
            return body = lead + (practice
                ? "\n\nNext: Boss Rush to fight any boss for real, or " + (retry.Length > 0 ? "Retry (" + retry + ")" : "Retry") + " to run the tutorial again."
                : tail);
        }

        // Same card as the tutorial's instruction panel (TutorialDirector): UiImgui.InfoStrip, spaced Gold eyebrow, Ivory
        // title, 96x2 Gold rule, theme body text. Left side, beside the centred Results band.
        void OnGUI()
        {
            if (!d || (d.State != RunState.Cleared && d.State != RunState.Results)) { if (d && d.State != RunState.Combat) Destroy(this); return; }
            GUI.depth = -20;
            if (styledFor != Screen.height)
            {
                styledFor = Screen.height;
                sTitle = UiTheme.Style(TutorialDirector.TitleSize, true, TextAnchor.UpperLeft, UiTheme.Ivory, true);
                sBody = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            }
            const float X = 24, Y = 150, Wd = 560;
            float pad = UiTheme.G3, padY = UiTheme.G2;
            string text = Body();
            float x = UiTheme.V(X + 3 + pad), inner = UiTheme.V(Wd - 3 - 2 * pad), cy = UiTheme.V(Y + padY);
            measure.text = "Tutorial complete"; float th = sTitle.CalcHeight(measure, inner);
            measure.text = text; float bh = sBody.CalcHeight(measure, inner);
            float h = UiTheme.V(padY + 20 + 8) + th + UiTheme.V(8 + UiTheme.RuleHeight + 16) + bh + UiTheme.V(padY);
            UiImgui.InfoStrip(new Rect(X, Y, Wd, h / UiTheme.Scale));
            UiTheme.Label(new Rect(x, cy, inner, UiTheme.V(20)), Eyebrow, UiImgui.Styles.Eyebrow); cy += UiTheme.V(20 + 8);
            UiTheme.Label(new Rect(x, cy, inner, th), "Tutorial complete", sTitle); cy += th + UiTheme.V(8);
            UiTheme.Fill(new Rect(x, cy, UiTheme.V(UiTheme.RuleWidth), Mathf.Max(1, UiTheme.V(UiTheme.RuleHeight))), UiTheme.Gold); cy += UiTheme.V(UiTheme.RuleHeight + 16);
            UiTheme.Label(new Rect(x, cy, inner, bh), text, sBody);
        }
        static readonly string Eyebrow = UiTheme.Spaced("Tutorial");
    }
}
