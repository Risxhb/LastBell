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
        RunDirector d; string lead = "", result = "", next = "";
        GUIStyle sTitle, sBody, sResult; int styledFor = -1; readonly GUIContent measure = new GUIContent();

        public static void Show(RunDirector director, IReadOnlyList<TutorialDirector.Record> records)
        {
            if (!director) return;
            var p = director.GetComponent<TutorialCompletePanel>() ?? director.gameObject.AddComponent<TutorialCompletePanel>();
            p.d = director; Current = p;
            int drills = records.Count(r => r.Tries > 1 || !r.Passed);
            var missed = records.Where(r => !r.Passed).Select(r => r.Title).ToList();
            p.lead = "The Tempo Tyrant falls. You've seen every mechanic in the game so far: read the shape, step out, punish.";
            p.result = missed.Count == 0 ? "Every mechanic handled." : "To practise: " + string.Join(", ", missed) + ".";
            // Retry and Main Menu are rows on the results band beside this card, so the card only points onward
            p.next = director.Mode == RunMode.Run ? "Next: Skyward Spire. Three minibosses guard Aurelion." : "Next: Boss Rush, to fight any boss for real.";
        }

        void OnDestroy() { if (Current == this) Current = null; }

        // Same card as the tutorial's instruction panel (TutorialDirector.CardBack): an ink strip with a Gold left bar fading out
        // on the right, spaced Gold eyebrow, Ivory title, 96x2 Gold rule; the lead in body text, the result as a GoldBright
        // line, then where to go next. Left side, beside the centred Results band.
        void OnGUI()
        {
            if (!d || (d.State != RunState.Cleared && d.State != RunState.Results)) { if (d && d.State != RunState.Combat) Destroy(this); return; }
            GUI.depth = -20;
            if (styledFor != Screen.height)
            {
                styledFor = Screen.height;
                sTitle = UiTheme.Style(TutorialDirector.TitleSize, true, TextAnchor.UpperLeft, UiTheme.Ivory, true);
                sBody = UiTheme.Style(TutorialDirector.BodySize, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
                sResult = UiTheme.Style(TutorialDirector.BodySize, true, TextAnchor.UpperLeft, UiTheme.GoldBright, true);
            }
            float pad = UiTheme.G3, padY = UiTheme.G2;
            float x = UiTheme.V(TutorialDirector.CardX + 3 + pad), inner = UiTheme.V(TutorialDirector.CardW - 3 - pad - UiTheme.G6), cy = UiTheme.V(TutorialDirector.CardY + padY);
            float th = H(sTitle, "Tutorial complete", inner), lh = H(sBody, lead, inner), rh = H(sResult, result, inner), nh = H(sBody, next, inner);
            float h = UiTheme.V(padY + 20 + 8) + th + UiTheme.V(8 + UiTheme.RuleHeight + 16) + lh + UiTheme.V(14) + rh + UiTheme.V(14) + nh + UiTheme.V(padY);
            TutorialDirector.CardBack(h / UiTheme.Scale);
            UiTheme.Label(new Rect(x, cy, inner, UiTheme.V(20)), Eyebrow, UiImgui.Styles.Eyebrow); cy += UiTheme.V(20 + 8);
            UiTheme.Label(new Rect(x, cy, inner, th), "Tutorial complete", sTitle); cy += th + UiTheme.V(8);
            UiTheme.Fill(new Rect(x, cy, UiTheme.V(UiTheme.RuleWidth), Mathf.Max(1, UiTheme.V(UiTheme.RuleHeight))), UiTheme.Gold); cy += UiTheme.V(UiTheme.RuleHeight + 16);
            UiTheme.Label(new Rect(x, cy, inner, lh), lead, sBody); cy += lh + UiTheme.V(14);
            UiTheme.Label(new Rect(x, cy, inner, rh), result, sResult); cy += rh + UiTheme.V(14);
            UiTheme.Label(new Rect(x, cy, inner, nh), next, sBody);
        }
        float H(GUIStyle st, string text, float width) { measure.text = text; return st.CalcHeight(measure, width); }
        static readonly string Eyebrow = UiTheme.Spaced("Tutorial · Complete");
    }
}
