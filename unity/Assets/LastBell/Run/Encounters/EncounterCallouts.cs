using UnityEngine;

namespace LastBell.Run
{
    // Theme styles for the in-fight encounter callouts (UI plan sub-thread D, docs/ui/ui-kit.md): world tags over players and
    // floor spots, the banner line under the boss bar, soak numerals, and the F7 developer readouts. Inter from UiTheme instead
    // of Unity's default font, sizes from the UiTheme ramp scaled with the screen, colours from UiTheme tokens.
    // Text is drawn through UiTheme.Label (laid out in white, tinted through GUI.color, alpha in 8 steps), so a colour that
    // blinks or fades doesn't lay the text out again each frame. Not for boss intro / cleared / defeat banners (by design).
    public static class EncounterCallouts
    {
        // ---- sizes at 1080p ----
        public const int TagSize = UiTheme.SizeBody + 2;      // names and jobs over players, words on floor spots (read mid-fight)
        public const int TagLargeSize = UiTheme.SizeHeading;  // numerals on rings and gaps, bigger floor words
        public const int BannerSize = UiTheme.SizeDisplay;    // the fight's one-line callout under the boss bar
        // Soak numerals must be impossible to miss (content rules §5): well above the ramp's display size.
        public const int NumeralSize = UiTheme.SizeDisplay * 16 / 9;      // 64 at 1080p
        public const int NumeralSmallSize = UiTheme.SizeDisplay * 11 / 9; // 44: fights that show many soaks at once (Vesper)
        public const int DiagnosticsSize = UiTheme.SizeNumeric;

        public sealed class StyleSet { public GUIStyle Tag, TagLarge, Banner, Numeral, NumeralSmall, Diagnostics; }
        static StyleSet styles; static int stylesHeight = -1;
        // Built once per screen height (call inside OnGUI). Never mutate them.
        public static StyleSet Styles
        {
            get
            {
                if (styles != null && stylesHeight == Screen.height) return styles;
                stylesHeight = Screen.height;
                return styles = new StyleSet
                {
                    Tag = UiTheme.Style(TagSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    TagLarge = UiTheme.Style(TagLargeSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    Banner = UiTheme.Style(BannerSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    Numeral = UiTheme.Style(NumeralSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    NumeralSmall = UiTheme.Style(NumeralSmallSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    Diagnostics = UiTheme.Style(DiagnosticsSize, false, TextAnchor.UpperLeft, UiTheme.IvoryBody),
                };
            }
        }

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        // Ink behind world tags (as the tutorial's tags and info strips), so a word stays legible over a lit floor or a beam.
        public static readonly Color TagInk = new Color(UiTheme.InkDeep.r, UiTheme.InkDeep.g, UiTheme.InkDeep.b, 0.7f);

        // One height per tag style, so tags of different words line up (screen px).
        public static float TagHeight(GUIStyle style) => Mathf.Round(style.fontSize * 1.55f);

        // A world tag's backing: a small ink strip with a 2 px left tick in the tag's own colour (screen px).
        public static void TagBack(Rect box, Color tick)
        {
            UiTheme.Fill(box, TagInk);
            UiTheme.Fill(new Rect(box.x, box.y, Mathf.Max(1, UiTheme.V(2)), box.height), new Color(tick.r, tick.g, tick.b, Mathf.Min(1, tick.a)));
        }

        // The fight's one-line callout across the screen, yFraction down from the top, over a soft centre band (UiImgui.Band,
        // as the Results overlay) so it holds over the boss and the floor.
        public static void Banner(float yFraction, string text, Color color, GUIStyle style = null)
        {
            var s = style ?? Styles.Banner; float y = Screen.height * yFraction, h = s.fontSize * 1.6f;
            UiImgui.Band(new Rect(0, (y - h * 0.6f) / UiTheme.Scale, UiTheme.VirtualWidth, h * 2.2f / UiTheme.Scale), 0.85f * Mathf.Min(1, color.a));
            UiTheme.Label(new Rect(0, y, Screen.width, h), text, s, color);
        }

        // A big soak numeral centred on a screen point, with a soft ink drop shadow (3 px at 1080p).
        public static void Numeral(Vector2 centre, string text, float alpha = 1, GUIStyle style = null)
        {
            if (alpha <= 0.01f) return;
            var s = style ?? Styles.Numeral; float w = s.fontSize * 2.2f, h = s.fontSize * 1.3f, o = Mathf.Max(1, Mathf.Round(UiTheme.V(3)));
            var r = new Rect(centre.x - w / 2, centre.y - h / 2, w, h);
            var keep = s.normal.textColor; var old = GUI.color; s.normal.textColor = Color.white;
            float a = Mathf.Ceil(alpha * old.a * 8f) / 8f;
            GUI.color = Alpha(UiTheme.InkDeep, 0.9f * a); UiTheme.RawLabel(new Rect(r.x + o, r.y + o, r.width, r.height), text, s);
            // a thin ink outline as well: a numeral sits on lit tower rims and beams
            float t = Mathf.Max(1, Mathf.Round(UiTheme.V(1.5f)));
            GUI.color = Alpha(UiTheme.InkDeep, 0.8f * a);
            UiTheme.RawLabel(new Rect(r.x - t, r.y, r.width, r.height), text, s); UiTheme.RawLabel(new Rect(r.x + t, r.y, r.width, r.height), text, s);
            UiTheme.RawLabel(new Rect(r.x, r.y - t, r.width, r.height), text, s); UiTheme.RawLabel(new Rect(r.x, r.y + t, r.width, r.height), text, s);
            GUI.color = Alpha(UiTheme.Ivory, a); UiTheme.RawLabel(r, text, s);
            s.normal.textColor = keep; GUI.color = old;
        }

        // The F7 developer readout (off by default; players never see it): an ink strip with a gold left bar, top left.
        public static void Diagnostics(string text, float widthPx = 760)
        {
            var s = Styles.Diagnostics; float pad = UiTheme.V(UiTheme.G1);
            var content = new GUIContent(text); float w = UiTheme.V(widthPx), h = s.CalcHeight(content, w) + 2 * pad;
            float x = UiTheme.V(16), y = Screen.height * 0.30f;
            UiTheme.Fill(new Rect(x, y, w + 2 * pad, h), UiTheme.InkStrip);
            UiTheme.Fill(new Rect(x, y, Mathf.Max(1, UiTheme.V(3)), h), UiTheme.Gold);
            UiTheme.Label(new Rect(x + 2 * pad, y + pad, w, h - 2 * pad), text, s);
        }
    }
}
