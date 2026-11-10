using UnityEngine;

namespace LastBell
{
    // One visual language for the IMGUI combat HUD and the code-built uGUI menus: Inter (SIL OFL, see
    // Resources/UI/Fonts/Inter-LICENSE.txt), named palette tokens, an 8 px spacing grid and flat-rect brass frames.
    // Reference resolution is 1920x1080 (height-matched), the same as every menu CanvasScaler.
    public static class UiTheme
    {
        // ---- palette ----
        public static readonly Color Ink = new Color(0.025f, 0.035f, 0.065f, 0.92f);      // panel fill
        public static readonly Color InkDeep = new Color(0.012f, 0.016f, 0.032f, 0.97f);  // modals, troughs
        public static readonly Color InkStrip = new Color(0.025f, 0.035f, 0.065f, 0.62f); // backing for text over the world
        public static readonly Color Ivory = new Color(0.96f, 0.92f, 0.82f, 1f);
        public static readonly Color IvoryBody = new Color(0.96f, 0.92f, 0.82f, 0.90f);
        public static readonly Color IvoryMuted = new Color(0.96f, 0.92f, 0.82f, 0.70f);
        public static readonly Color IvoryMicro = new Color(0.96f, 0.92f, 0.82f, 0.45f);
        public static readonly Color Gold = new Color(0.88f, 0.72f, 0.43f, 1f);
        public static readonly Color GoldLabel = new Color(0.88f, 0.72f, 0.43f, 0.80f);
        public static readonly Color GoldBright = new Color(1.00f, 0.86f, 0.56f, 1f);
        public static readonly Color GoldDim = new Color(0.88f, 0.72f, 0.43f, 0.35f);
        public static readonly Color Bronze = new Color(0.42f, 0.30f, 0.15f, 1f);
        public static readonly Color BarBack = new Color(0.12f, 0.06f, 0.05f, 1f);
        public static readonly Color BossRed = new Color(0.78f, 0.20f, 0.18f, 1f);
        public static readonly Color BossRedHi = new Color(0.95f, 0.42f, 0.35f, 1f);
        public static readonly Color Chip = new Color(1.00f, 0.85f, 0.60f, 0.80f);       // recent-damage trail
        public static readonly Color HealthPlayer = new Color(0.80f, 0.24f, 0.22f, 1f);
        public static readonly Color HealthPlayerHi = new Color(0.96f, 0.45f, 0.38f, 1f);
        public static readonly Color Stamina = new Color(0.45f, 0.85f, 0.75f, 1f);
        public static readonly Color Danger = new Color(1.00f, 0.45f, 0.35f, 1f);         // text on Ink, never on red
        public static readonly Color Haste = new Color(1.00f, 0.72f, 0.30f, 1f);
        public static readonly Color Calm = new Color(0.45f, 0.95f, 0.85f, 1f);
        public static readonly Color FocusFill = new Color(0.10f, 0.08f, 0.05f, 0.95f);
        public static readonly Color PressedFill = new Color(0.05f, 0.04f, 0.02f, 0.95f);
        public static readonly Color ModalDim = new Color(0, 0, 0, 0.55f);
        // ---- main-menu language (ConfrontationMenu; docs/ui/ui-kit.md) ----
        public static readonly Color RestLabel = new Color(0.93f, 0.88f, 0.76f, 0.74f);      // text row at rest
        public static readonly Color RestLabelMuted = new Color(0.93f, 0.88f, 0.76f, 0.62f); // card / tab label at rest
        public static readonly Color RuleFaint = new Color(0.93f, 0.88f, 0.76f, 0.16f);      // footer rules, card and tab edges
        public static readonly Color PrimaryInk = new Color(0.07f, 0.05f, 0.02f, 1f);        // dark text on a gold fill
        public static readonly Color DisabledFill = new Color(0.22f, 0.20f, 0.17f, 0.90f);   // a gold fill when disabled
        public static readonly Color DisabledLabel = new Color(0.60f, 0.57f, 0.50f, 1f);     // its label
        public static readonly Color Backdrop = new Color(0.012f, 0.014f, 0.026f, 1f);       // opaque navy behind full-screen menus
        public static readonly Color GradientInk = new Color(0.008f, 0.008f, 0.022f, 1f);    // the side contrast gradient's ink
        public static readonly Color BandInk = new Color(0.01f, 0.012f, 0.025f, 1f);         // the centre band's ink
        public static readonly Color StripFill = new Color(0.88f, 0.72f, 0.43f, 0.26f);      // focus highlight strip (Gold .26)
        public static readonly Color StripEdge = new Color(0.88f, 0.72f, 0.43f, 0.55f);      // its 1 px top/bottom edges (Gold .55)
        public const float RuleWidth = 96, RuleHeight = 2;                                      // the gold rule under a title

        // ---- type ramp at 1080p (Inter; SemiBold replaces fake bold) ----
        public const int SizeDisplay = 36, SizeHeading = 22, SizeLabel = 13, SizeBody = 17, SizeNumeric = 15, SizeMicro = 12;
        // ---- spacing grid ----
        public const float G1 = 8, G2 = 16, G3 = 24, G4 = 32, G6 = 48;

        // ---- fonts ----
        static Font regular, semibold;
        public static Font Regular => regular ? regular : regular = Load("Inter-Regular");
        public static Font SemiBold => semibold ? semibold : semibold = Load("Inter-SemiBold");
        static Font Load(string name)
        {
            var f = Resources.Load<Font>("UI/Fonts/" + name);
            return f ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // Spaced capitals for eyebrows and display titles ("B O S S   R U S H"); the legacy Text/IMGUI have no tracking.
        public static string Spaced(string s) => string.Join("   ", System.Array.ConvertAll(s.ToUpperInvariant().Split(' '), w => string.Join(" ", w.ToCharArray())));

        // ---- shared gradient textures (built once, never destroyed; tint with GUI.color / Graphic.color) ----
        static Texture2D strip, side, centre;
        // Highlight strip, 128x1 white: alpha rises over the first 22 % and falls over the last 32 % (main menu rows).
        public static Texture2D StripTexture => strip ? strip : strip = Horizontal(u => new Color(1, 1, 1,
            Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / .22f)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - u) / .32f))));
        // Side contrast gradient, 128x1 GradientInk: clear at the left edge, ~0.985 from 43 % across (main menu right side).
        // Draw it mirrored (uv x 1..0) for a left-hand column.
        public static Texture2D SideGradient => side ? side : side = Horizontal(u => new Color(GradientInk.r, GradientInk.g, GradientInk.b,
            Mathf.SmoothStep(0, .985f, Mathf.Clamp01(u * 2.3f))));
        // Centre band, 1x64 BandInk: 0.78 * sin^2 from top to bottom (Results, boss reveals); dark in the middle, clear at both edges.
        public static Texture2D CentreBand
        {
            get
            {
                if (centre) return centre;
                centre = new Texture2D(1, 64, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) { float a = Mathf.Sin(y / 63f * Mathf.PI); centre.SetPixel(0, y, new Color(BandInk.r, BandInk.g, BandInk.b, 0.78f * a * a)); }
                centre.Apply(); return centre;
            }
        }
        static Texture2D Horizontal(System.Func<float, Color> at)
        {
            var t = new Texture2D(128, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 128; i++) t.SetPixel(i, 0, at(i / 127f));
            t.Apply(); return t;
        }

        // =====================================================================================================
        // IMGUI (combat HUD). Lay out in virtual 1080p units and convert with V()/R(); styles from Style() are
        // already sized for the current screen, so text renders crisp (no GUI.matrix scaling).
        // =====================================================================================================
        // no high floor: the virtual canvas is always 1080 tall, so a short window (a browser page) must scale below
        // 0.7 or bottom-anchored HUD falls off the screen; text sizes keep their own 9 px minimum
        public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.35f, 2f);
        public static float V(float v) => v * Scale;
        public static Rect R(float x, float y, float w, float h) { float s = Scale; return new Rect(x * s, y * s, w * s, h * s); }
        public static float VirtualWidth => Screen.width / Scale;      // the virtual canvas is VirtualWidth x 1080
        public const float VirtualHeight = 1080;

        static Texture2D white;
        public static Texture2D White
        {
            get
            {
                if (!white) { white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; white.SetPixel(0, 0, Color.white); white.Apply(); }
                return white;
            }
        }
        public static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, White); GUI.color = o; }
        public static void Border(Rect r, Color c, float t = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y + t, t, r.height - 2 * t), c); Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2 * t), c);
        }
        // 45-degree diamond centred on c, side = size (screen px).
        public static void Diamond(Vector2 c, float size, Color col)
        {
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(45, c);
            Fill(new Rect(c.x - size / 2, c.y - size / 2, size, size), col); GUI.matrix = m;
        }
        // House frame (screen px): Ink fill, 1 px GoldDim border, inset 1 px inner shadow, gold L-brackets + diamonds.
        public static void Frame(Rect r, Color? fill = null, bool ornaments = true, bool shadow = true)
        {
            float s = Scale, t = Mathf.Max(1, Mathf.Round(s));
            if (shadow) Fill(new Rect(r.x + 3 * s, r.y + 4 * s, r.width, r.height), new Color(0, 0, 0, 0.30f));
            Fill(r, fill ?? Ink);
            Border(r, GoldDim, t);
            Border(new Rect(r.x + 3 * s, r.y + 3 * s, r.width - 6 * s, r.height - 6 * s), new Color(0, 0, 0, 0.5f), t);
            if (!ornaments) return;
            float len = 10 * s, th = Mathf.Max(1, 2 * s), o = 2 * s;
            var b = new Rect(r.x - o, r.y - o, r.width + 2 * o, r.height + 2 * o);
            Fill(new Rect(b.x, b.y, len, th), Gold); Fill(new Rect(b.x, b.y, th, len), Gold);
            Fill(new Rect(b.xMax - len, b.y, len, th), Gold); Fill(new Rect(b.xMax - th, b.y, th, len), Gold);
            Fill(new Rect(b.x, b.yMax - th, len, th), Gold); Fill(new Rect(b.x, b.yMax - len, th, len), Gold);
            Fill(new Rect(b.xMax - len, b.yMax - th, len, th), Gold); Fill(new Rect(b.xMax - th, b.yMax - len, th, len), Gold);
            float d = 4 * s;
            Diamond(new Vector2(b.x, b.y), d, Gold); Diamond(new Vector2(b.xMax, b.y), d, Gold);
            Diamond(new Vector2(b.x, b.yMax), d, Gold); Diamond(new Vector2(b.xMax, b.yMax), d, Gold);
        }
        // Text with a 1 px (scaled) drop shadow at black 0.85.
        // The text is laid out in white and coloured through GUI.color: the text colour is part of the IMGUI text cache key,
        // so a fading float laid its text out again every frame, and a label switching colour (a key going from muted to
        // ivory) was laid out again the first time (perf report aurelion-4p-2026-11-03: "absorbed" 9.4 ms, "LMB" 6.8 ms).
        public static void Label(Rect r, string text, GUIStyle style, Color? color = null)
        {
            var keep = style.normal.textColor; var c = color ?? keep; float o = Mathf.Max(1, Mathf.Round(Scale));
            var oldGui = GUI.color; style.normal.textColor = Color.white;
            // GUI.color is multiplied into the IMGUI text colour too, so the alpha goes in 8 steps: a fading damage float made a
            // new text handle every frame, and Unity's cleanup freed ~6,700 at once (perf report bellkeeper-ultimate, Update 20:00)
            float a = Mathf.Ceil(c.a * oldGui.a * 8f) / 8f;
            GUI.color = new Color(0, 0, 0, 0.85f * a); RawLabel(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
            var tint = oldGui * c; tint.a = a; GUI.color = tint; RawLabel(r, text, style);
            style.normal.textColor = keep; GUI.color = oldGui;
        }
        // GUI.Label, timed under -lastbellFontLog: any text layout over FontLogMs is logged with its text, size and font, to
        // find a text that is laid out for the first time mid-fight (perf report aurelion-eclipse-2026-11-03).
        public static readonly bool FontLog = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.Equals("-lastbellFontLog", System.StringComparison.OrdinalIgnoreCase));
        const double FontLogMs = 2;
        public static void RawLabel(Rect r, string text, GUIStyle style)
        {
            if (!FontLog) { GUI.Label(r, text, style); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            GUI.Label(r, text, style);
            LogSlowText(t0, "label (" + Event.current.type + ")", text, style);
        }
        public static void LogSlowText(long t0, string what, string text, GUIStyle style)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms < FontLogMs) return;
            string atlas = AtlasSummary(), change = atlas == lastAtlas ? "" : $" ATLAS CHANGED [{lastAtlas}] -> [{atlas}]";
            lastAtlas = atlas;
            Debug.Log($"[fontlog] {ms:0.0} ms {what} frame {Time.frameCount} t {Time.timeSinceLevelLoad:0.00} gc {System.GC.CollectionCount(0)}: \"{text}\" size {style.fontSize} {style.fontStyle} font {(style.font ? style.font.name : "skin")}{change}");
        }
        // TextCore font atlases (pages and size per font asset), to see whether a slow layout grew or added an atlas page
        static string lastAtlas = "";
        public static string AtlasSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var fa in Resources.FindObjectsOfTypeAll<UnityEngine.TextCore.Text.FontAsset>())
            {
                var tex = fa.atlasTextures; int n = tex == null ? 0 : tex.Length;
                sb.Append(fa.name).Append(' ').Append(n).Append('x').Append(fa.atlasWidth).Append('/').Append(fa.atlasHeight).Append(' ').Append(fa.characterTable.Count).Append("ch; ");
            }
            return sb.ToString();
        }
        // the legacy font path (TextGenerator, RequestCharactersInTexture) rebuilds its own texture when it fills up
        [RuntimeInitializeOnLoadMethod]
        static void HookFontRebuilds()
        {
            if (FontLog) Font.textureRebuilt += f => Debug.Log($"[fontlog] legacy font texture rebuilt: {(f ? f.name : "?")} frame {Time.frameCount} t {Time.timeSinceLevelLoad:0.00}");
        }
        // Label style sized for the current screen: px is the 1080p size.
        public static GUIStyle Style(int px, bool strong, TextAnchor anchor, Color color, bool wrap = false)
        {
            var st = new GUIStyle(GUI.skin.label) { font = strong ? SemiBold : Regular, fontSize = Mathf.Max(9, Mathf.RoundToInt(px * Scale)), fontStyle = FontStyle.Normal, alignment = anchor, wordWrap = wrap, richText = false, clipping = TextClipping.Overflow };
            st.normal.textColor = color; st.hover.textColor = color; st.active.textColor = color;
            st.padding = new RectOffset(0, 0, 0, 0); st.margin = new RectOffset(0, 0, 0, 0);
            return st;
        }
        // House button: rest Ink + GoldDim edge, hover/focus warm fill + GoldBright edge, pressed dark. primary = Gold fill.
        public static GUIStyle ButtonStyle(int px, bool primary = false)
        {
            var st = new GUIStyle(GUI.skin.button) { font = SemiBold, fontSize = Mathf.Max(9, Mathf.RoundToInt(px * Scale)), fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter, richText = false };
            st.border = new RectOffset(2, 2, 2, 2); st.padding = new RectOffset(8, 8, 2, 2); st.margin = new RectOffset(0, 0, 0, 0);
            Color restFill = primary ? Gold : new Color(Ink.r, Ink.g, Ink.b, 0.85f), restEdge = primary ? GoldBright : GoldDim;
            Color hoverFill = primary ? GoldBright : FocusFill, hoverEdge = GoldBright;
            Color text = primary ? PrimaryInk : new Color(Ivory.r, Ivory.g, Ivory.b, 0.85f), textHot = primary ? text : Ivory;
            st.normal.background = Tile(restFill, restEdge); st.normal.textColor = text;
            st.hover.background = Tile(hoverFill, hoverEdge); st.hover.textColor = textHot;
            st.focused.background = st.hover.background; st.focused.textColor = textHot;
            st.active.background = Tile(primary ? Bronze : PressedFill, GoldBright); st.active.textColor = primary ? Ivory : textHot;
            st.onNormal.background = Tile(FocusFill, Gold); st.onNormal.textColor = GoldBright;   // toggled-on
            st.onHover.background = st.hover.background; st.onHover.textColor = GoldBright;
            st.onActive.background = st.active.background; st.onActive.textColor = GoldBright;
            return st;
        }
        static readonly System.Collections.Generic.Dictionary<long, Texture2D> tiles = new System.Collections.Generic.Dictionary<long, Texture2D>();
        // 4x4 nine-slice tile: 1 px edge, solid centre.
        public static Texture2D Tile(Color fill, Color edge)
        {
            long key = ((long)ColorUtility.ToHtmlStringRGBA(fill).GetHashCode() << 32) ^ (uint)ColorUtility.ToHtmlStringRGBA(edge).GetHashCode();
            if (tiles.TryGetValue(key, out var t) && t) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, x == 0 || y == 0 || x == 3 || y == 3 ? edge : fill);
            t.Apply(); tiles[key] = t; return t;
        }
    }
}
