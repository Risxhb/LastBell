using System.Collections.Generic;
using UnityEngine;

namespace LastBell
{
    // IMGUI side of the main-menu language, for in-run screens (OnGUI). Every rect is in virtual 1080p units (UiTheme.R
    // converts); styles come from UiImgui.Styles, cached per screen height. See docs/ui/ui-kit.md.
    public static class UiImgui
    {
        static float Px => Mathf.Max(1, Mathf.Round(UiTheme.Scale));   // a crisp 1 px line at any scale
        static readonly GUIStyle hit = new GUIStyle();

        // ---------------------------------------------------------------- styles
        public sealed class StyleSet
        {
            public GUIStyle Eyebrow, EyebrowCentre, Title, TitleCentre, Row, RowLeft, Primary, PrimaryLeft, Body, BodyCentre, Hint;
        }
        static StyleSet styles; static int stylesHeight = -1;
        // Built once per screen height (call inside OnGUI: GUIStyle copies GUI.skin). Never mutate these; copy one instead.
        public static StyleSet Styles
        {
            get
            {
                if (styles != null && stylesHeight == Screen.height) return styles;
                stylesHeight = Screen.height;
                return styles = new StyleSet
                {
                    Eyebrow = UiTheme.Style(14, false, TextAnchor.MiddleLeft, UiTheme.Gold),
                    EyebrowCentre = UiTheme.Style(14, false, TextAnchor.MiddleCenter, UiTheme.Gold),
                    Title = UiTheme.Style(52, true, TextAnchor.MiddleLeft, UiTheme.Ivory),
                    TitleCentre = UiTheme.Style(52, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    Row = UiTheme.Style(24, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    RowLeft = UiTheme.Style(24, true, TextAnchor.MiddleLeft, UiTheme.Ivory),
                    Primary = UiTheme.Style(28, true, TextAnchor.MiddleCenter, UiTheme.Ivory),
                    PrimaryLeft = UiTheme.Style(28, true, TextAnchor.MiddleLeft, UiTheme.Ivory),
                    Body = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true),
                    BodyCentre = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.UpperCenter, UiTheme.IvoryBody, true),
                    Hint = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.MiddleCenter, UiTheme.IvoryMuted),
                };
            }
        }

        // ---------------------------------------------------------------- drawing
        // The focus highlight strip over a screen rect: StripFill band fading at both ends, 1 px StripEdge top and bottom.
        public static void Strip(Rect screen)
        {
            var o = GUI.color; var tex = UiTheme.StripTexture;
            GUI.color = UiTheme.StripFill; GUI.DrawTexture(screen, tex, ScaleMode.StretchToFill, true);
            GUI.color = UiTheme.StripEdge; GUI.DrawTexture(new Rect(screen.x, screen.y, screen.width, Px), tex); GUI.DrawTexture(new Rect(screen.x, screen.yMax - Px, screen.width, Px), tex);
            GUI.color = o;
        }
        // Text row with the strip on mouse hover (no keyboard focus): the button RunHud's Results used. Returns true on click.
        // rest: the label colour at rest (default RestLabel, as on the main menu).
        public static bool Row(Rect v, string label, GUIStyle style, Color? rest = null, bool enabled = true)
        {
            var r = UiTheme.R(v.x, v.y, v.width, v.height); bool over = enabled && r.Contains(Event.current.mousePosition);
            if (over) Strip(r);
            var c = rest ?? UiTheme.RestLabel;
            UiTheme.Label(r, label, style, !enabled ? new Color(c.r, c.g, c.b, .35f) : over ? UiTheme.GoldBright : c);
            return GUI.Button(r, GUIContent.none, hit) && enabled;
        }
        // Text row in a focus list: the strip shows on the focused row (keyboard, controller or mouse hover). Returns true when
        // clicked, or when the row is focused and Confirm (Enter / A) was pressed this frame. style: default Styles.Row.
        public static bool Row(ImguiFocus focus, Rect v, string label, GUIStyle style = null, bool enabled = true, Color? rest = null)
        {
            int id = focus.Register(enabled);
            var r = UiTheme.R(v.x, v.y, v.width, v.height);
            if (enabled && focus.MouseMoved && r.Contains(Event.current.mousePosition)) focus.Index = id;
            bool hot = enabled && focus.IsFocused(id);
            if (hot) Strip(r);
            var c = rest ?? UiTheme.RestLabel;
            UiTheme.Label(r, label, style ?? Styles.Row, !enabled ? new Color(c.r, c.g, c.b, .35f) : hot ? UiTheme.GoldBright : c);
            bool clicked = GUI.Button(r, GUIContent.none, hit) && enabled;
            if (clicked) focus.Index = id;
            return clicked || focus.Submitted(id);
        }
        // Header: spaced Gold eyebrow, Ivory title, 96x2 Gold rule. v.width is the column; v.y the eyebrow's top. Left-aligned
        // on full screens, centred on centre-band overlays (Results, Pause). Returns the virtual y just below the rule.
        public static float Header(Rect v, string eyebrow, string title, bool centred = false, GUIStyle eyebrowStyle = null, GUIStyle titleStyle = null)
        {
            var s = Styles; float y = v.y;
            if (!string.IsNullOrEmpty(eyebrow)) { UiTheme.Label(UiTheme.R(v.x, y, v.width, 20), UiTheme.Spaced(eyebrow), eyebrowStyle ?? (centred ? s.EyebrowCentre : s.Eyebrow)); y += 26; }
            var ts = titleStyle ?? (centred ? s.TitleCentre : s.Title); float th = ts.fontSize / UiTheme.Scale * 1.25f;
            UiTheme.Label(UiTheme.R(v.x, y, v.width, th), title, ts); y += th + 4;
            Rule(centred ? v.x + v.width / 2 - UiTheme.RuleWidth / 2 : v.x, y);
            return y + UiTheme.RuleHeight;
        }
        // The 96x2 Gold rule (virtual top-left).
        public static void Rule(float x, float y) => UiTheme.Fill(UiTheme.R(x, y, UiTheme.RuleWidth, UiTheme.RuleHeight), UiTheme.Gold);
        // Soft horizontal band (CentreBand: dark middle, clear top and bottom edges) behind centred overlay text.
        public static void Band(Rect v, float alpha = 1)
        {
            var o = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTexture(UiTheme.R(v.x, v.y, v.width, v.height), UiTheme.CentreBand, ScaleMode.StretchToFill, true); GUI.color = o;
        }
        // The main menu's side contrast gradient: clear at the inner edge, dark towards the screen side. right = a right-hand
        // column (dark on the right); false mirrors it for a left-hand column.
        public static void SideGradient(Rect v, bool right = true, float alpha = 1)
        {
            var o = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTextureWithTexCoords(UiTheme.R(v.x, v.y, v.width, v.height), UiTheme.SideGradient, right ? new Rect(0, 0, 1, 1) : new Rect(1, 0, -1, 1), true);
            GUI.color = o;
        }
        // Small info card (Ready, tutorial prompts, tooltips): an ink strip with a 3 px Gold left bar; no box or ornaments.
        public static void InfoStrip(Rect v, Color? fill = null)
        {
            UiTheme.Fill(UiTheme.R(v.x, v.y, v.width, v.height), fill ?? UiTheme.InkDeep);
            UiTheme.Fill(UiTheme.R(v.x, v.y, 3, v.height), UiTheme.Gold);
        }
    }

    // Keyboard / controller focus for an IMGUI list of UiImgui.Row calls, driven by MenuInput.Poll (arrows, dpad, left stick,
    // Enter / A to confirm, Esc / B for Cancelled). Usage, inside OnGUI:
    //     focus.Begin();
    //     if (UiImgui.Row(focus, rect0, "Resume")) ...;   // rows register in draw order: ids 0, 1, 2 ...
    //     if (UiImgui.Row(focus, rect1, "Main Menu")) ...;
    //     if (focus.Cancelled) ...;
    // Navigation wraps; disabled rows are skipped. Rows must be drawn in the same order every pass.
    public sealed class ImguiFocus
    {
        public int Index;               // the focused row's id
        public bool Wrap = true;
        public int Columns = 1;         // > 1: a grid; left/right step one, up/down step a row
        public bool Active = true;      // false: ignore input (another layer owns it); rows still draw
        public bool Cancelled { get; private set; }   // Back (Esc / B) pressed: true in the first OnGUI pass of that frame only
        public bool MouseMoved { get; private set; }  // the mouse moved this frame (hover then takes focus)
        public int Count => count;
        int count, next, frame = -1; bool submit; Vector2 lastMouse = new Vector2(float.NaN, float.NaN);
        readonly List<bool> enabled = new List<bool>();

        // Call once at the top of each OnGUI pass, before the rows.
        public void Begin()
        {
            if (next > 0) count = next;
            next = 0;
            if (frame == Time.frameCount) { Cancelled = false; return; }   // later passes this frame: Back already reported once
            frame = Time.frameCount;
            var m = Event.current != null ? Event.current.mousePosition : lastMouse;
            MouseMoved = !float.IsNaN(lastMouse.x) && (m - lastMouse).sqrMagnitude > 1; lastMouse = m;
            var input = Active ? LastBell.Menu.MenuInput.Poll() : default;
            Cancelled = input.Cancel; submit = input.Submit;
            int step = input.Move.y != 0 ? -input.Move.y * Mathf.Max(1, Columns) : input.Move.x;
            if (step != 0) Move(step);
            if (count > 0) Index = Mathf.Clamp(Index, 0, count - 1);
        }
        public bool IsFocused(int id) => id == Index;
        // Move focus by step rows, skipping disabled rows (wrapping if Wrap).
        public void Move(int step)
        {
            int n = count; if (n == 0) return;
            int i = Mathf.Clamp(Index, 0, n - 1);
            for (int tries = 0; tries < n; tries++)
            {
                int j = i + step;
                if (Wrap) j = ((j % n) + n) % n; else if (j < 0 || j >= n) return;
                i = j; if (Enabled(i)) { Index = i; return; }
            }
        }
        public bool Enabled(int id) => id >= 0 && id < enabled.Count && enabled[id];
        // Rows call these (UiImgui.Row). Register returns the row's id for this pass.
        public int Register(bool isEnabled)
        {
            int id = next++;
            while (enabled.Count <= id) enabled.Add(true);
            enabled[id] = isEnabled;
            if (id == Index && !isEnabled && count > 0 && Event.current != null && Event.current.type == EventType.Layout) Move(1);
            return id;
        }
        // True once per Confirm press, for the focused enabled row.
        public bool Submitted(int id)
        {
            if (!submit || id != Index || !Enabled(id)) return false;
            submit = false; return true;
        }
        // Put focus on a row (e.g. the screen's main action when it opens).
        public void Reset(int index = 0) { Index = index; submit = false; Cancelled = false; }
    }
}
