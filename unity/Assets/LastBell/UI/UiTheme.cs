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

        // =====================================================================================================
        // IMGUI (combat HUD). Lay out in virtual 1080p units and convert with V()/R(); styles from Style() are
        // already sized for the current screen, so text renders crisp (no GUI.matrix scaling).
        // =====================================================================================================
        public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.70f, 2f);
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
        public static void Label(Rect r, string text, GUIStyle style, Color? color = null)
        {
            var keep = style.normal.textColor; var c = color ?? keep; float o = Mathf.Max(1, Mathf.Round(Scale));
            style.normal.textColor = new Color(0, 0, 0, 0.85f * c.a); GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
            style.normal.textColor = c; GUI.Label(r, text, style);
            style.normal.textColor = keep;
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
            Color text = primary ? new Color(0.07f, 0.05f, 0.02f) : new Color(Ivory.r, Ivory.g, Ivory.b, 0.85f), textHot = primary ? text : Ivory;
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
