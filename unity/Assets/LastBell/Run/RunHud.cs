using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Functional IMGUI HUD and menus for the run loop (stand-in for final UI art). Everything shown is read from the
    // live gameplay state each frame; nothing here changes rules. Hazard names are spelled out while they warn, so the
    // fight reads with audio off. Layout is in virtual 1080p units (UiTheme.R / V); styles are rebuilt per screen height.
    public sealed class RunHud : MonoBehaviour
    {
        struct FloatText { public Vector3 pos; public string text; public Color color; public float age, jx; }
        static readonly List<FloatText> floats = new List<FloatText>();
        static int floatSeq;
        const float FloatLife = 0.9f, FloatRise = 40;
        // x jitter (virtual px, -18..18) picked deterministically per float so simultaneous numbers stack in a column
        public static void Float(Vector3 world, string text, Color c)
        { if (LastBell.Run.Networking.LanFx.Record != null) LastBell.Run.Networking.LanFx.Record(LastBell.Run.Networking.LanFxTarget.Hud, "Float", new object[] { world, text, c });
            if (floats.Count >= 80) return;
            int h = (floatSeq++ + (text != null ? text.Length : 0)) & int.MaxValue;
            floats.Add(new FloatText { pos = world, text = text, color = c, jx = (h % 5 - 2) * 9f });
        }
        public static void ClearTransient() { floats.Clear(); labels.Clear(); banner = null; Presentation.HitFeel.ClearTransient(); }
        // a big centre-screen call (Aurelion v4: wipes, the enrage warning) for `seconds` of real time
        static string banner; static Color bannerColor; static float bannerUntil;
        public static void Banner(string text, Color c, float seconds) { banner = text; bannerColor = c; bannerUntil = Time.unscaledTime + seconds; }
        public static string CurrentBanner => banner != null && Time.unscaledTime < bannerUntil ? banner : null;
        // a narrated caption box (demo videos): a title and a line of explanation, until changed or cleared with null
        public static string CaptionTitle, CaptionBody;
        // a world-space label that stays until its owner hides it (Aurelion v3: Ecliptic numerals, SUN / SHADE marks);
        // with `follow`, Pos is an offset above that transform (a moving player)
        // Icon (optional): drawn as a glyph of 2 x Size px instead of the text, with Next (smaller, after a chevron) for a
        // two-step mark (Aurelion Zenith Marks, 8 Nov graphics pass); Text stays set for logs and tests
        public sealed class WorldLabel { public Vector3 Pos; public Transform Follow; public string Text; public Color Color; public int Size; public bool Hidden; public Texture2D Icon, Next, Then; public void Hide() => Hidden = true; }
        static readonly List<WorldLabel> labels = new List<WorldLabel>();
        public static WorldLabel Label(Vector3 world, string text, Color c, int size = 34, Transform follow = null)
        {
            labels.RemoveAll(l => l.Hidden);
            var l = new WorldLabel { Pos = world, Text = text, Color = c, Size = size, Follow = follow };
            if (labels.Count < 40) labels.Add(l);
            return l;
        }

        RunDirector d;
        GUIStyle revealName, revealEpithet, resultPrimary, resultAction;
        GUIStyle display30, display36, display40, heading, eyebrow, eyebrowC, labelGold, bodyC, bodyL, row15, numR, numC,
                 small13, small13R, micro, microR, controls, chipSt, keySt, keyStL, cornerSt, stateSt, pill, tab, floatSt, slotName, tipSt, descSt,
                 microC, subC, rowLabel, microHead, btn, btnP, mapBtn, microWrap, meterHead, bannerSt, captionBody;
        int styleHeight = -1;
        static int menuClass;   // static: survives the scene load when the menu switches map
        public static bool ShowMeter;   // Tab

        // boss bar damage chip: the HUD-only trailing value (holds, then lerps down to the live HP)
        BossEncounter chipBoss; float chip = 1, chipLast = 1, chipHold;
        // boss block fades to 0.4 over 0.5 s once the boss is down
        BossEncounter fadeBoss; float bossAlpha = 1;
        Color BA(Color c) => new Color(c.r, c.g, c.b, c.a * bossAlpha);

        static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            ["Meridian Cut"] = "it follows you, then locks: DASH out (or stay beyond the tip)", ["Zenith Pins"] = "it follows you, then locks: DASH off your circle",
            ["Processional Thrust"] = "step sideways before the blade reaches you", ["Crown Reversal"] = "leave his back: go to the flank",
            ["Meridian Alignment"] = "ring I: stand close inside it or far outside", ["Meridian Alignment II"] = "ring II: hug him or get outside it",
            [ChimeDebts.Source] = "a paid debt detonates: be outside the ring", [ChimeDebts.Source + " line"] = "and outside its line to the boss",
        };
        // Phase ticks only for bosses that change phase, at their real thresholds (fraction of max HP).
        static float[] PhaseTicks(BossEncounter b) =>
            b.HudMarks != null ? b.HudMarks :   // an encounter's own thresholds (the Bellkeeper Ultimate: its five phases)
            b is AurelionEncounter ae ? ae.PhaseMarks :   // 70 / 40 / 20 %
            b is SylvaraEncounter ? new[] { 0.75f, 0.50f, 0.25f } :
            b is CrucivarEncounter ? new[] { 0.65f, 0.30f } :
            b is TempoTyrantEncounter ? new[] { TempoTyrantEncounter.FrenzyAt } :
            b is ChimeboundOracleEncounter || b is DialWardenEncounter || b is PendulumSentinelEncounter || b is EscapementTwinsEncounter || b is RivetCantorEncounter || b is CrucibleSkaterEncounter || b is BriarMetronomeEncounter || b is SiltCarillonEncounter ? new[] { 0.5f } : null;
        float hitFlash;

        void Awake() { d = GetComponent<RunDirector>(); }
        // load the player panel's art while the scene loads: done on its first draw it cost 26 ms (perf report
        // aurelion-4p-2026-11-03, frame 763)
        void Start()
        {
            BossBarArt.Warm();
            foreach (var names in SlotIconNames.Values) foreach (var n in names) Icon(n);
            Icon("potion");
        }

        // LAN client (LanClientWorld): the director holds a replica of the host's fight and never runs it. The HUD draws the
        // replica; the menu, Retry and Main Menu shortcuts are the host's, so a client gets a waiting card and Leave instead.
        public static bool LanClientView;
        public static System.Action LanLeave;

        void Update()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i]; f.age += Time.deltaTime; floats[i] = f;   // the rise is applied on screen (FloatRise)
                if (f.age > FloatLife) floats.RemoveAt(i);
            }
            TickChip();
            TickPlayerInput();
            TickFade();
            var kb = Keyboard.current; if (kb == null || d == null) return;
            if (kb.tabKey.wasPressedThisFrame) ShowMeter = !ShowMeter;
            if (LanClientView) return;
            if (d.State == RunState.Results)
            {
                if (kb.rKey.wasPressedThisFrame) d.RequestRetry();
                else if (kb.mKey.wasPressedThisFrame) d.ReturnToMenu();
            }
            if (d.State == RunState.Menu && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) d.StartRun(RunDirector.Classes[menuClass]);
            if (d.State == RunState.Menu && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
            {
                int n = RunMaps.All.Length, step = kb.rightBracketKey.wasPressedThisFrame ? 1 : n - 1;
                d.SelectMap((Mathf.Max(0, RunMaps.Current) + step) % n);
            }
            if (d.State == RunState.Menu && kb.dKey.wasPressedThisFrame)
                d.SelectDifficulty((Difficulty)(((int)RunDifficulty.Selected + 1) % RunDifficulty.Names.Length));
            if (d.State == RunState.Menu && kb.aKey.wasPressedThisFrame)
                d.SelectAllies((RunParty.Selected + 1) % (RunParty.MaxAllies + 1));
        }

        void TickChip()
        {
            var b = d != null ? d.Boss : null;
            if (b == null || b.MaxHp <= 0) { chipBoss = null; return; }
            float f = Mathf.Clamp01((float)b.Hp / b.MaxHp);
            if (b != chipBoss) { chipBoss = b; chip = chipLast = f; chipHold = 0; return; }
            hitFlash = Mathf.MoveTowards(hitFlash, 0, Time.unscaledDeltaTime / 0.18f);
            if (f < chipLast - 1e-5f) { chipHold = 0.3f; hitFlash = 1; }   // a fresh hit restarts the hold and flashes the pool
            chipLast = f;
            if (f >= chip) { chip = f; return; }
            float dt = Time.unscaledDeltaTime;
            if (chipHold > 0) { chipHold -= dt; return; }
            chip = Mathf.Lerp(chip, f, 1 - Mathf.Exp(-dt * 7f));   // ~0.6 s to close the gap
            if (chip - f < 0.001f) chip = f;
        }

        void TickFade()
        {
            var b = d != null ? d.Boss : null;
            if (b != fadeBoss) { fadeBoss = b; bossAlpha = 1; }
            if (b == null) return;
            bool down = d.State == RunState.Cleared || b.Hp <= 0 || (d.State == RunState.Results && d.Victory);
            bossAlpha = Mathf.MoveTowards(bossAlpha, down ? 0.4f : 1f, Time.unscaledDeltaTime * (0.6f / 0.5f));
        }

        // ---------------------------------------------------------------------------------------------------------
        // styles + virtual-unit helpers
        // ---------------------------------------------------------------------------------------------------------
        void Styles()
        {
            if (styleHeight == Screen.height && display30 != null) return;
            styleHeight = Screen.height; fontsWarm = false;
            var ivoryControls = new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, 0.6f);
            display30 = UiTheme.Style(30, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            display36 = UiTheme.Style(UiTheme.SizeDisplay, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            display40 = UiTheme.Style(40, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            revealName = UiTheme.Style(64, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            resultPrimary = UiTheme.Style(26, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            resultAction = UiTheme.Style(22, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            revealEpithet = UiTheme.Style(26, false, TextAnchor.MiddleCenter, UiTheme.GoldBright);
            heading = UiTheme.Style(UiTheme.SizeHeading, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            eyebrow = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleLeft, UiTheme.GoldLabel);
            eyebrowC = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleCenter, UiTheme.GoldLabel);
            labelGold = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleLeft, UiTheme.GoldLabel, true);
            bodyC = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.UpperCenter, UiTheme.IvoryBody, true);
            bodyL = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            row15 = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            numR = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleRight, UiTheme.Ivory);
            numC = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            small13 = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            small13R = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleRight, UiTheme.IvoryBody);
            micro = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.MiddleLeft, UiTheme.IvoryMuted);
            microR = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.MiddleRight, UiTheme.IvoryMuted);
            controls = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.UpperLeft, ivoryControls, true);
            descSt = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            chipSt = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            keySt = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.UpperLeft, UiTheme.Ivory);
            keyStL = UiTheme.Style(UiTheme.SizeMicro + 1, true, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            cornerSt = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.UpperRight, UiTheme.GoldBright);
            stateSt = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleCenter, UiTheme.IvoryBody);
            pill = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleCenter, UiTheme.Gold);
            tab = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            floatSt = UiTheme.Style(20, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            slotName = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleCenter, UiTheme.Ivory, true);
            tipSt = UiTheme.Style(UiTheme.SizeNumeric, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            microC = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.MiddleCenter, UiTheme.IvoryMuted);
            subC = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.MiddleCenter, UiTheme.IvoryMuted);
            rowLabel = UiTheme.Style(UiTheme.SizeNumeric, false, TextAnchor.MiddleLeft, UiTheme.IvoryMuted);
            microHead = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.MiddleRight, UiTheme.IvoryMicro);
            btn = UiTheme.ButtonStyle(UiTheme.SizeNumeric);
            btnP = UiTheme.ButtonStyle(UiTheme.SizeNumeric, true);
            mapBtn = UiTheme.ButtonStyle(UiTheme.SizeLabel);
            microWrap = UiTheme.Style(UiTheme.SizeMicro, false, TextAnchor.UpperLeft, UiTheme.IvoryMuted, true);
            bannerSt = UiTheme.Style(44, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            captionBody = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true);
            meterHead = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleLeft, new Color(UiTheme.Gold.r, UiTheme.Gold.g, UiTheme.Gold.b, 0.6f));
        }

        static Rect S(float x, float y, float w, float h) => UiTheme.R(x, y, w, h);
        static Rect S(Rect v) => UiTheme.R(v.x, v.y, v.width, v.height);
        static float Px => Mathf.Max(1, Mathf.Round(UiTheme.Scale));            // a crisp 1 px line at any scale
        static void F(float x, float y, float w, float h, Color c) => UiTheme.Fill(S(x, y, w, h), c);
        static void T(float x, float y, float w, float h, string t, GUIStyle st, Color? c = null) => UiTheme.Label(S(x, y, w, h), t, st, c);
        // A ticking line ("beat in 7.4s · you (I): TICK · then TOCK"), left-aligned: one label per " · " part, and times over
        // 3 s in whole seconds (rounded up), so the changing part repeats a few cached texts. Drawn whole, each new combination
        // was a new IMGUI text handle, and Unity's ClearUnusedTextHandles cost 5-10 ms when it swept them (perf report
        // bellkeeper-ultimate-2026-11-03, Update 12:25: the Ultimate's slam 6 frame).
        static readonly string[] DotSep = { " · " };
        static readonly System.Text.RegularExpressions.Regex Tenths = new System.Text.RegularExpressions.Regex(@"(\d+\.\d)s");
        static string WholeSeconds(string s) => s.IndexOf('.') < 0 ? s : Tenths.Replace(s, m =>
        {
            float v = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return v > 3 ? Mathf.CeilToInt(v) + "s" : m.Value;
        });
        static void TickLine(float x, float y, float h, string line, GUIStyle st, Color? c = null)
        {
            var parts = line.Split(DotSep, System.StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
                x = DrawSegs(x, y, h, TimeSegs((i > 0 ? " · " : "") + parts[i]), st, c);
        }
        // Each time ("2.4s") is its own label, apart from the text around it ("slam 6 in "), so countdowns share about 30
        // number texts instead of one per name and tenth (perf report bellkeeper-ultimate-2026-11-03, Update 19:30). Spaces
        // at a segment's ends are drawn as gaps, so the widths don't depend on how trailing spaces are measured.
        // cached per text (a kit builds its line fresh each frame, but only a few hundred distinct lines occur), so the
        // regex and substrings don't allocate every frame
        static readonly Dictionary<string, List<string>> segCache = new Dictionary<string, List<string>>();
        static List<string> TimeSegs(string s)
        {
            if (segCache.TryGetValue(s, out var hit)) return hit;
            if (segCache.Count > 1024) segCache.Clear();
            var segs = new List<string>(); segCache[s] = segs; int at = 0;
            foreach (System.Text.RegularExpressions.Match m in Tenths.Matches(s))
            {
                if (m.Index > at) segs.Add(s.Substring(at, m.Index - at));
                segs.Add(WholeSeconds(m.Value)); at = m.Index + m.Length;
            }
            if (at < s.Length) segs.Add(s.Substring(at));
            return segs;
        }
        static readonly Dictionary<(GUIStyle, int), float> spaceW = new Dictionary<(GUIStyle, int), float>();
        static float SpaceW(GUIStyle st)
        {
            if (!spaceW.TryGetValue((st, st.fontSize), out float w)) spaceW[(st, st.fontSize)] = w = Wv(st, "a a") - Wv(st, "aa");
            return w;
        }
        static float SegsWidth(GUIStyle st, List<string> sg)
        {
            float w = 0, sp = SpaceW(st);
            foreach (var g in sg) { string tr = g.Trim(' '); w += (g.Length - tr.Length) * sp + (tr.Length > 0 ? Wv(st, tr) : 0); }
            return w;
        }
        static float DrawSegs(float x, float y, float h, List<string> sg, GUIStyle st, Color? c = null)
        {
            float sp = SpaceW(st);
            foreach (var g in sg)
            {
                string tr = g.TrimStart(' '); x += (g.Length - tr.Length) * sp;
                string core = tr.TrimEnd(' ');
                if (core.Length > 0) { float pw = Wv(st, core); T(x, y, pw + 4, h, core, st, c); x += pw; }
                x += (tr.Length - core.Length) * sp;
            }
            return x;
        }
        static float Wv(GUIStyle st, string t)
        {
            long t0 = UiTheme.FontLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            float w = st.CalcSize(new GUIContent(t)).x / UiTheme.Scale;
            if (UiTheme.FontLog) UiTheme.LogSlowText(t0, "width (" + Event.current.type + ")", t, st);
            return w;
        }
        static float Hv(GUIStyle st, string t, float wv)
        {
            long t0 = UiTheme.FontLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            float h = st.CalcHeight(new GUIContent(t), wv * UiTheme.Scale) / UiTheme.Scale;
            if (UiTheme.FontLog) UiTheme.LogSlowText(t0, "height (" + Event.current.type + ")", t, st);
            return h;
        }
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static void Dim() => UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), UiTheme.ModalDim);
        // HUD content is centre-clamped to 1920 virtual px on ultrawide; 48 px screen margin on 21:9, 32 otherwise
        static float Margin => Screen.width / (float)Mathf.Max(1, Screen.height) > 2.2f ? UiTheme.G6 : UiTheme.G4;
        static float Left => Mathf.Max(0, (UiTheme.VirtualWidth - 1920) / 2) + Margin;
        static float Right => UiTheme.VirtualWidth - Mathf.Max(0, (UiTheme.VirtualWidth - 1920) / 2) - Margin;

        // house card centred horizontally (virtual units); y < 0 centres vertically. rule = 3 px header rule colour.
        Rect Card(float w, float h, float y, bool modal, Color? rule = null)
        {
            var v = new Rect(UiTheme.VirtualWidth / 2 - w / 2, y < 0 ? UiTheme.VirtualHeight / 2 - h / 2 : y, w, h);
            UiTheme.Frame(S(v), modal ? UiTheme.InkDeep : UiTheme.Ink, true, true);
            if (rule.HasValue) F(v.x, v.y, v.width, 3, rule.Value);
            return v;
        }

        // ---------------------------------------------------------------------------------------------------------
        // A hovered ability tooltip is queued and drawn after everything else, so nothing in battle can cover it.
        string tipText; float tipX, tipTop;
        void OnGUI()
        {
            if (d == null) return;
            GUI.depth = 0;   // above encounter HUDs (TempoTyrantHud draws at depth 10)
            long t = Stamp();
            Styles();
            if (!fontsWarm) WarmFonts();
            DrainWarmUp(); t = Sec("styles + warm-up", t);
            tipText = null;
            DrawHud(); t = Stamp();
            if (tipText != null) DrawTooltip(tipX, tipTop, tipText);
            Sec("tooltip draw", t);
        }

        // -lastbellFontLog: time each HUD section in every event (layout and repaint), so a slow text layout is found even when
        // it happens in a CalcSize or a draw the per-label timing doesn't wrap
        static long Stamp() => UiTheme.FontLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        static long Sec(string what, long t0)
        {
            if (!UiTheme.FontLog) return 0;
            long now = System.Diagnostics.Stopwatch.GetTimestamp(); double ms = (now - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 3) Debug.Log($"[fontlog] {ms:0.0} ms section {what} ({Event.current.type}) frame {Time.frameCount} t {Time.timeSinceLevelLoad:0.00} gc {System.GC.CollectionCount(0)}");
            return now;
        }
        void DrawHud()
        {
            // The legacy run menu is a standalone diagnostic only: when the main menu owns the session (a launch is pending or
            // the return bridge exists) the Menu state is a one-frame hand-off, so draw a plain ink frame instead of flashing it.
            if (LanClientView && d.State == RunState.Menu) return;   // before the host's first snapshot
            if (d.State == RunState.Menu) { if (LastBell.Menu.MenuReturnBridge.Active || LastBell.Menu.MenuLaunch.Pending) UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), UiTheme.InkDeep); else Menu(); return; }
            bool world = d.World != null;
            if (world)
            {
                // boss-order tracker (Track) removed from the combat HUD at Risxhb's request
                long t = Stamp();
                Boss(); t = Sec("boss block", t);
                PlayerPanel(); t = Sec("player panel", t);
                if (d.State != RunState.Results) RunPartyCards.Draw(d, Left, UiTheme.VirtualHeight - UiTheme.G4); t = Sec("party cards", t);   // bots and LAN allies (UI/RunPartyCards.cs)
                BannerText(); CaptionText(); t = Sec("banner + caption", t);
                Floats(); t = Sec("floats", t);
                Labels(); t = Sec("world labels", t);
                PlayerMarks(); t = Sec("player marks", t);
                DownedMarks(); t = Sec("downed marks", t);
                if (ShowMechanicGallery) MechanicGallery();
                switch (d.State)
                {
                    case RunState.Ready: WarmReviveRings(); if (!LanClientView) ReadyCard(); break;
                    case RunState.Intro: if (d.Boss != null) IntroCard(); break;
                    case RunState.Cleared: ClearedCard(); break;
                    case RunState.Downed: DownedCard(); break;
                }
                Sec("state card", t);   // Ready / Intro / Cleared / Downed, plus the mechanic gallery
            }
            if (d.State == RunState.Results) { if (LanClientView) LanResults(); else Results(); }
            if (!world) return;
            if (d.Paused) { long tp = Stamp(); Pause(); Sec("pause", tp); }
            // dev line: dimmed Micro, lifted clear of the "Development Build" watermark
            // dev key hint removed from the HUD (keys still work: F9 defeat boss, F8 next phase)
            // map / difficulty chip removed from the combat HUD (Risxhb, October 24, 2026)
        }

        // top of the player panel (virtual y); overlays and tooltips stack above it
        // player HUD height (virtual): health row, stamina row, ability slots and their exception captions
        float PanelH => 22 + 18 + TileH + CapH + 4;
        float PanelTop => UiTheme.VirtualHeight - UiTheme.G4 - PanelH;

        // Campaign preparation card (Campaign.cs): which arena and fight is next, and what this stretch of the run holds.
        string CampaignHead()
        {
            int pos = d.ProgressSlot;
            return pos == 0 && d.Encounter == RunDirector.TutorialPractice ? "First, the tutorial" : Campaign.AreaName(d.Encounter);
        }
        string CampaignBody()
        {
            int enc = d.Encounter, area = Campaign.AreaOf(enc);
            string next = Campaign.StageLabel(d.EncounterOrder, d.ProgressSlot) + ": " + RunDirector.Bosses[enc].Name.Split('(')[0].Trim() + ".";
            if (area == Campaign.TutorialArea) return "A guided Tempo Tyrant fight on Metronome Court. Then three arenas of minibosses and their bosses, and the Bellkeeper.";
            if (area == Campaign.FinalAreaIndex) return next + " The last bell. Win on Hard to face the Overclocked Tempo Tyrant.";
            if (area == Campaign.BonusArea) return next + " The Tempo Tyrant, pushed to the limit. The run is already won.";
            if (Campaign.IsAreaBoss(enc)) return next + " Health and potions refill before him.";
            return next + " Health refills before every fight; potions carry over until the area boss.";
        }

        void ReadyCard()
        {
            bool practice = d.Mode == RunMode.Practice;
            // practice shows no preparation card (Risxhb, October 25, 2026): the fight starts by itself (RunDirector.AutoReady)
            if (practice) return;
            if (Shop.RunShopFlow.Showing) return;   // the intermission shop replaces the preparation card
            string eb = UiTheme.Spaced(RunDifficulty.Name(d.Difficulty));
            string head = (practice ? "PRACTICE — " + RunDirector.Bosses[d.Encounter].Name : CampaignHead()).ToUpperInvariant();
            string body = practice ? "Practice never advances the run." : d.EncounterOrder.Length == 1 ? "One fight: the Bellkeeper." : CampaignBody();
            const float pad = UiTheme.G3;
            // body clamped to 2 lines: widen the card (not the text) until it fits
            float w = 640, twoLines = 2 * bodyC.lineHeight / UiTheme.Scale + 2;
            while (w < 960 && Hv(bodyC, body, w - 2 * pad) > twoLines) w += 40;
            float bh = Mathf.Min(Hv(bodyC, body, w - 2 * pad), twoLines);
            float h = pad + 20 + 6 + 40 + 10 + bh + 20 + 44 + pad;
            var r = Card(w, h, 168, false);   // same band as Intro / Cleared, clear of the hero
            float y = r.y + pad;
            T(r.x, y, w, 20, eb, eyebrowC); y += 26;
            T(r.x, y, w, 40, head, display30); y += 50;
            T(r.x + pad, y, w - 2 * pad, bh + 4, body, bodyC); y += bh + 20;
            if (GUI.Button(S(r.x + w / 2 - 120, y, 240, 44), "READY  (Enter)", btnP)) d.RequestReady();
        }

        // ---- Souls-style boss reveal / defeat: no panel, the name large over the arena on a soft dark band ----
        GUIStyle fitSt;
        // A copy of `st` whose font shrinks (never grows) until `text` fits `maxW` virtual px. The shrunk size snaps down to
        // one of FitSteps, so a new long name never asks the font for a size it hasn't laid out yet (perf report
        // bellkeeper-ultimate-2026-11-03: a fresh size cost 10.6 ms in GetMeshInfo plus a font texture upload mid-fight).
        static readonly float[] FitSteps = { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f, 0.3f };
        GUIStyle Fit(GUIStyle st, string text, float maxW)
        {
            if (fitSt == null) fitSt = new GUIStyle(st);
            fitSt.font = st.font; fitSt.alignment = st.alignment; fitSt.fontStyle = st.fontStyle; fitSt.wordWrap = false; fitSt.clipping = TextClipping.Overflow;
            fitSt.normal.textColor = st.normal.textColor; fitSt.fontSize = st.fontSize;
            long t0 = UiTheme.FontLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            float w = fitSt.CalcSize(new GUIContent(text)).x / UiTheme.Scale;
            if (UiTheme.FontLog) UiTheme.LogSlowText(t0, "fit", text, fitSt);
            if (w > maxW)
            {
                float want = maxW / w; int size = FitSize(st, FitSteps[FitSteps.Length - 1]);
                foreach (var f in FitSteps) if (f <= want) { size = FitSize(st, f); break; }
                fitSt.fontSize = size;
            }
            return fitSt;
        }
        static int FitSize(GUIStyle st, float f) => Mathf.Max(8, Mathf.FloorToInt(st.fontSize * f));

        // Font warm-up: lay out and draw (invisibly) every glyph the HUD uses at every size it uses (each style, the Fit steps
        // of the fitted styles, the overlay sizes), so no glyph or size is laid out or put on the atlas for the first time in
        // combat. IMGUI text goes through TextCore in Unity 6, so this draws through GUIStyle rather than only asking the legacy
        // Font for characters. Runs on the first HUD repaint after the styles are built and again on each Ready screen
        // (RunDirector.ReadyWarmUp), since a full atlas drops glyphs that haven't been drawn lately.
        const string WarmChars = " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,:;'\"!?-+*/%()[]<>#&=_|—·…●○☾☉•×→–›◆◇▮▯°✓✕";   // every non-ASCII glyph in our UI strings: one missing here costs 13-15 ms the first time (font fallback)
        bool fontsWarm;
        void OnEnable() => RunDirector.ReadyWarmUp += OnReadyWarmUp;
        void OnDisable() => RunDirector.ReadyWarmUp -= OnReadyWarmUp;
        void OnReadyWarmUp(RunDirector dir, int encounter) { if (dir == d) fontsWarm = false; }
        // The warm-up is a queue drained a few ms per repaint (perf report aurelion-4p-2026-11-03: done in one go it froze the
        // Ready card for 344 ms).
        const double WarmBudgetMs = 4;
        readonly Queue<System.Action> warmQueue = new Queue<System.Action>();
        long warmStarted; int warmFrames;
        void WarmFonts()
        {
            if (Event.current.type != EventType.Repaint) return;
            fontsWarm = true;
            warmQueue.Clear(); warmStarted = System.Diagnostics.Stopwatch.GetTimestamp(); warmFrames = 0;
            foreach (var st in new[] { revealName, revealEpithet, resultPrimary, resultAction, display30, display36, display40, heading, eyebrow, eyebrowC,
                     labelGold, bodyC, bodyL, row15, numR, numC, small13, small13R, micro, microR, controls, chipSt, keySt, keyStL, cornerSt, stateSt, pill,
                     tab, floatSt, slotName, tipSt, descSt, microC, subC, rowLabel, microHead, btn, btnP, mapBtn, microWrap, meterHead })
                if (st != null) EnqueueFont(st.font, st.fontSize, st.fontStyle);
            foreach (var st in new[] { revealName, revealEpithet, display30 })
                foreach (var f in FitSteps) EnqueueFont(st.font, FitSize(st, f), st.fontStyle);
            // the overlays (caption 28/20, banner 46, world labels 30-54) use the skin font at fixed unscaled sizes
            foreach (int size in new[] { 20, 28, 30, 34, 44, 46, 54 }) { EnqueueFont(null, size, FontStyle.Bold); EnqueueFont(null, size, FontStyle.Normal); }
            // the boss block's mechanic rows (BossBlock): each attack and mechanic line is a long string laid out the first time
            // its hazard appears (perf report aurelion-eclipse-2026-11-03: 7.4 ms at Starfall Mirror), so lay them all out here
            if (row15 != null)
            {
                var st = row15;
                foreach (var at in BossAttackIcons.All) if (at != null) { string t = $"{at.name}  ({at.bossName})".ToUpper(); warmQueue.Enqueue(() => WarmText(st, t)); }
                foreach (var m in MechanicIcons.All)
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    string n = m.name.ToUpper(); warmQueue.Enqueue(() => WarmText(st, n));
                    foreach (var hint in Hints.Values) { string t = n + " — " + hint; warmQueue.Enqueue(() => WarmText(st, t)); }
                }
            }
        }
        void DrainWarmUp()
        {
            if (warmQueue.Count == 0 || Event.current.type != EventType.Repaint) return;
            var old = GUI.color; GUI.color = Color.clear; long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = WarmBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0;
            while (warmQueue.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - t0 < budget) warmQueue.Dequeue()();
            GUI.color = old; warmFrames++;
            if (warmQueue.Count == 0 && UiTheme.FontLog)
                Debug.Log($"[fontlog] HUD font warm-up done over {warmFrames} frames, {(System.Diagnostics.Stopwatch.GetTimestamp() - warmStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms wall; atlases: {UiTheme.AtlasSummary()}");
        }
        // one step for the ASCII set, then one per non-ASCII glyph: each fallback-font glyph costs 13-15 ms the first time,
        // so batched into one step they froze single Ready frames for 50-170 ms (perf report aurelion-4p-2026-11-03, 10:20)
        static readonly string[] WarmParts = SplitWarmChars();
        static string[] SplitWarmChars()
        {
            var parts = new List<string>(); var ascii = new System.Text.StringBuilder();
            foreach (char ch in WarmChars) { if (ch < 128) ascii.Append(ch); else parts.Add(ch.ToString()); }
            parts.Insert(0, ascii.ToString()); return parts.ToArray();
        }
        void EnqueueFont(Font font, int size, FontStyle style)
        {
            foreach (var part in WarmParts) { var c = new GUIContent(part); warmQueue.Enqueue(() => WarmFont(font, size, style, c)); }
        }
        GUIStyle warmSt;
        static void WarmText(GUIStyle st, string text)
        {
            var c = new GUIContent(text); var sz = st.CalcSize(c);
            st.Draw(new Rect(0, 0, sz.x, sz.y), c, false, false, false, false);   // GUI.color is clear: nothing shows
        }
        void WarmFont(Font font, int size, FontStyle style, GUIContent warmText)
        {
            if (font == null) font = GUI.skin.font;
            // no Font.RequestCharactersInTexture: IMGUI lays text out through TextCore, and that legacy call only made the legacy
            // font textures rebuild about 7 times per warm-up (smoke 24, -lastbellFontLog)
            if (warmSt == null) warmSt = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false, clipping = TextClipping.Overflow };
            warmSt.font = font; warmSt.fontSize = size; warmSt.fontStyle = style;
            var sz = warmSt.CalcSize(warmText);
            warmSt.Draw(new Rect(0, 0, sz.x, sz.y), warmText, false, false, false, false);   // GUI.color is clear: nothing shows
        }
        // soft full-width band behind the reveal text, faded by `a`
        void DrawBand(float y, float h, float a) => UiImgui.Band(new Rect(0, y, UiTheme.VirtualWidth, h), a);
        // thin gold rules flowing out from the title, each ending in a small diamond; `grow` animates them outward
        void Rules(float cx, float y, float halfText, float grow, Color c)
        {
            float len = Mathf.Lerp(0, 220, grow), gap = 28;
            if (len < 2) return;
            F(cx - halfText - gap - len, y, len, 1.5f, c); F(cx + halfText + gap, y, len, 1.5f, c);
            UiTheme.Diamond(new Vector2(cx - halfText - gap - len, y + 0.75f) * UiTheme.Scale, UiTheme.V(7), c);
            UiTheme.Diamond(new Vector2(cx + halfText + gap + len, y + 0.75f) * UiTheme.Scale, UiTheme.V(7), c);
        }
        static float Ease(float t) => t * t * (3 - 2 * t);

        void IntroCard()
        {
            // timeline over the intro: name fades and settles in, rules grow, everything fades before combat starts
            float p = d.IntroProgress, inA = Ease(Mathf.Clamp01(p / 0.3f)), outA = 1 - Ease(Mathf.Clamp01((p - 0.82f) / 0.18f)), a = inA * outA;
            // boss moments (Presentation lane, 8 Nov): cinematic bars, a role and area line, and every boss's title
            bool moments = Presentation.PresentationSettings.BossMoments;
            if (moments) Letterbox(p);
            string full = d.Boss.DisplayName; int comma = full.IndexOf(',');
            string name = (comma > 0 ? full.Substring(0, comma) : full).Trim(), epithet = moments ? Presentation.BossMoments.IntroEpithet : comma > 0 ? full.Substring(comma + 1).Trim() : null;
            float vw = UiTheme.VirtualWidth, cx = vw / 2, maxW = Mathf.Min(vw, 1920) - 2 * 160, y0 = 300 + (1 - inA) * 14;
            DrawBand(y0 - 70, 300, a);
            string head = moments && Presentation.BossMoments.IntroRoleSpaced.Length > 0 ? Presentation.BossMoments.IntroRoleSpaced
                : UiTheme.Spaced(d.Mode == RunMode.Practice ? "Practice" : Campaign.StageLabel(d.EncounterOrder, d.ProgressSlot));
            T(0, y0 - 46, vw, 22, head, eyebrowC, A(UiTheme.GoldLabel, UiTheme.GoldLabel.a * a));
            string big = UiTheme.Spaced(name);
            var nameSt = Fit(revealName, big, maxW);
            T(0, y0, vw, 84, big, nameSt, A(UiTheme.Ivory, a));
            float half = Mathf.Min(maxW, nameSt.CalcSize(new GUIContent(big)).x / UiTheme.Scale) / 2;
            Rules(cx, y0 + 42, half, Ease(Mathf.Clamp01((p - 0.12f) / 0.4f)), A(UiTheme.Gold, 0.85f * a));
            float y = y0 + 92;
            if (epithet != null) { var eSt = Fit(revealEpithet, epithet, maxW); T(0, y, vw, 34, epithet, eSt, A(UiTheme.GoldBright, a)); y += 40; }
            T(0, y + 4, vw, 24, d.Boss.Lesson, bodyC, A(UiTheme.IvoryMuted, UiTheme.IvoryMuted.a * a));
            T(0, y + 34, vw, 18, $"Fight begins in {Mathf.Max(0, (120 - d.StateTicks) / 60f):0.0}s", microC, A(UiTheme.IvoryMicro, UiTheme.IvoryMicro.a * a));
        }

        // the cleared card's next line: a new arena is named, and a Hard Bellkeeper win announces the bonus
        string NextLine(int next)
        {
            string name = RunDirector.Bosses[next].Name.Split('(')[0].Trim();
            if (next == Campaign.BonusIndex) return "The last bell has rung... and the Tempo Tyrant answers. Bonus: " + name;
            bool newArea = Campaign.AreaOf(next) != Campaign.AreaOf(d.Encounter);
            return newArea ? $"Onward to {Campaign.AreaName(next)}. Next: {name}" : "Next: " + name;
        }

        // ink bars top and bottom during the intro: in by 0.15, out from 0.82 (the boss moments)
        void Letterbox(float p)
        {
            float k = Ease(Mathf.Clamp01(p / 0.15f)) * (1 - Ease(Mathf.Clamp01((p - 0.82f) / 0.18f)));
            if (k <= 0.001f) return;
            float vw = UiTheme.VirtualWidth, vh = UiTheme.VirtualHeight, bh = 72 * k;
            var ink = A(UiTheme.InkDeep, 0.94f); var rule = A(UiTheme.Gold, 0.55f * k);
            F(0, 0, vw, bh, ink); F(0, vh - bh, vw, bh, ink);
            F(0, bh, vw, 1.5f, rule); F(0, vh - bh - 1.5f, vw, 1.5f, rule);
        }

        void ClearedCard()
        {
            if (Presentation.PresentationSettings.BossMoments) { DefeatedCard(); return; }
            float a = Ease(Mathf.Clamp01(d.StateTicks / 40f));
            string next = d.Mode == RunMode.Practice ? "Practice complete" : d.ProgressSlot == d.EncounterOrder.Length - 1 ? (d.BonusFight ? "Overclocked, and outplayed" : "The last bell has rung") : NextLine(d.EncounterOrder[d.ProgressSlot + 1]);
            string full = d.Boss ? d.Boss.DisplayName : "Boss"; int comma = full.IndexOf(',');
            string name = (comma > 0 ? full.Substring(0, comma) : full).Trim();
            float vw = UiTheme.VirtualWidth, cx = vw / 2, maxW = Mathf.Min(vw, 1920) - 2 * 160, y0 = 300;
            DrawBand(y0 - 60, 260, a);
            string big = UiTheme.Spaced(name);
            var nameSt = Fit(revealName, big, maxW);
            T(0, y0, vw, 84, big, nameSt, A(UiTheme.Gold, a));
            float half = Mathf.Min(maxW, nameSt.CalcSize(new GUIContent(big)).x / UiTheme.Scale) / 2;
            Rules(cx, y0 + 42, half, Ease(Mathf.Clamp01(d.StateTicks / 60f)), A(UiTheme.Gold, 0.85f * a));
            T(0, y0 + 90, vw, 36, UiTheme.Spaced("Falls silent"), revealEpithet, A(UiTheme.Ivory, a));
            T(0, y0 + 136, vw, 24, next, bodyC, A(UiTheme.IvoryMuted, UiTheme.IvoryMuted.a * a));
        }

        // The boss moments' card (Presentation lane, 8 Nov): it waits for the kill's freeze, flash and slow motion (0.65 s), then
        // the role line, the name in gold, "Defeated", the fight time and the next fight
        BossEncounter defeatFor; string defeatNext, defeatName; static readonly GUIContent measureText = new GUIContent();
        void DefeatedCard()
        {
            float a = Ease(Mathf.Clamp01((d.StateTicks - 40) / 28f));
            if (a <= 0.001f) return;
            // the card's text is built once per boss (no text built per frame)
            if (defeatFor != d.Boss || defeatNext == null)
            {
                defeatFor = d.Boss;
                defeatNext = d.Mode == RunMode.Practice ? "Practice complete" : d.ProgressSlot == d.EncounterOrder.Length - 1 ? (d.BonusFight ? "Overclocked, and outplayed" : "The last bell has rung") : NextLine(d.EncounterOrder[d.ProgressSlot + 1]);
                string full = d.Boss ? d.Boss.DisplayName : "Boss"; int comma = full.IndexOf(',');
                defeatName = UiTheme.Spaced((comma > 0 ? full.Substring(0, comma) : full).Trim());
            }
            string next = defeatNext, big = defeatName;
            float vw = UiTheme.VirtualWidth, cx = vw / 2, maxW = Mathf.Min(vw, 1920) - 2 * 160, y0 = 300 - (1 - a) * 10;
            DrawBand(y0 - 70, 300, a);
            var role = Presentation.BossMoments.IntroRoleSpaced;
            if (role.Length > 0) T(0, y0 - 46, vw, 22, role, eyebrowC, A(UiTheme.GoldLabel, UiTheme.GoldLabel.a * a));
            var nameSt = Fit(revealName, big, maxW);
            T(0, y0, vw, 84, big, nameSt, A(UiTheme.Gold, a));
            measureText.text = big;
            float half = Mathf.Min(maxW, nameSt.CalcSize(measureText).x / UiTheme.Scale) / 2;
            Rules(cx, y0 + 42, half, Ease(Mathf.Clamp01((d.StateTicks - 40) / 50f)), A(UiTheme.Gold, 0.85f * a));
            T(0, y0 + 90, vw, 40, Presentation.BossMoments.DefeatedSpaced, display36, A(UiTheme.Ivory, a));
            T(0, y0 + 138, vw, 24, next, bodyC, A(UiTheme.IvoryMuted, UiTheme.IvoryMuted.a * a));
            var time = Presentation.BossMoments.FightTimeLine;
            if (time.Length > 0) T(0, y0 + 168, vw, 18, time, microC, A(UiTheme.IvoryMicro, UiTheme.IvoryMicro.a * a));
        }

        void DownedCard()
        {
            const float w = 560, pad = UiTheme.G3;
            // a catalogued boss attack shows its 64 px icon, with the attack name and boss beside it (BossAttackIcons)
            // the last hit only if it took you to 0 and is the defeat cause; a scripted wipe (an empty tower, Severance, Last
            // Light) downs you with its own cause, and shows its family's icon with the attack name and boss on a second line
            // (8 Nov icons). The enrage has no family: text only.
            var wd = d.World; BossAttackIcons.Attack ka = null; bool scripted = false;
            if (wd != null)
            {
                int n = wd.TakenLog.Count;
                if (n > 0 && wd.TakenLog[n - 1].hpAfter <= 0 && wd.TakenLog[n - 1].source == wd.LastDamageSource)
                {
                    ka = TakenAttack(wd, n - 1);
                    scripted = NamesOwnCause(wd.TakenLog[n - 1].source, ka);
                }
                else { ka = BossAttackIcons.Get(wd.LastDamageAttackId); scripted = ka != null; }
            }
            var r = Card(w, pad + 44 + 8 + 26 + (scripted ? 26 : 0) + pad, 270, false, UiTheme.Danger);
            float y = r.y + pad;
            T(r.x, y, w, 44, UiTheme.Spaced("Down"), display36); y += 52;
            var ki = BossAttackIcons.Icon(ka);
            if (ki != null) GUI.DrawTexture(S(r.x + pad, r.y + pad + 8, 64, 64), ki, ScaleMode.ScaleToFit, true);
            string a = "Struck by ", cause = ka != null && !scripted ? ka.Label : d.DefeatCause ?? "?";
            float wa = Wv(bodyL, a), wc = Wv(bodyL, cause), x = r.x + w / 2 - (wa + wc) / 2;
            T(x, y, wa + 2, 26, a, bodyL, UiTheme.Ivory);
            T(x + wa, y, wc + 2, 26, cause, bodyL, UiTheme.Gold);
            if (scripted) T(r.x, y + 26, w, 26, ka.Label, bodyC, UiTheme.IvoryMuted);
        }

        // ---------------------------------------------------------------------------------------------------------
        // legacy run menu
        // ---------------------------------------------------------------------------------------------------------
        void Menu()
        {
            Dim();
            const float W = 720, pad = UiTheme.G3, inner = W - 2 * pad, colGap = 12, rowGap = 8;
            const string controlText =
                "Controls: WASD move · mouse aims · LMB basic · 2 3 4 class actions · 5 Mending Chorus / Temporal Anchor · RMB dash · F potion · Shift sprint · Esc pause\n" +
                "Movement stamina: sprint and dash share one bar. No invincibility frames (dodging does not make you immune). F drinks a potion (+40 HP).\nHealth refills before each boss; potions carry over between bosses and refill before the final boss. Retry restarts at the first boss.\n" +
                "Only real boss encounters are offered (the stand-in minibosses were removed); Sylvara is practice only, in Verdant Chimes. Arcanist kit numbers are prototype tuning. Dev keys in combat: F9 defeat boss, F8 next phase.";
            const int perRow = 3;
            int mapRows = (RunMaps.All.Length + perRow - 1) / perRow, bossRows = (RunDirector.Bosses.Length + perRow - 1) / perRow;
            // controls: one block per paragraph with a 12 px gap
            string[] paras = controlText.Split('\n');
            const float paraGap = 12;
            float ctrlH = 0;
            foreach (var p in paras) ctrlH += Hv(controls, p, inner) + paraGap;
            ctrlH -= paraGap;
            // Map / Difficulty: tracked-caps eyebrow, note on a second Micro line
            int cur = RunMaps.Current;
            string mapNote = (cur >= 0 ? RunMaps.All[cur].Name + " — " + RunMaps.All[cur].Note : RunMaps.CurrentName) + "   ([ ] to switch)";
            var level = RunDifficulty.Selected;
            string diffNote = RunDifficulty.Notes[(int)level] + "   (D to switch)";
            float mapNoteH = Mathf.Max(18, Hv(microWrap, mapNote, inner)), diffNoteH = Mathf.Max(18, Hv(microWrap, diffNote, inner));
            float total = pad + 48 + 4 + 26 + 16
                        + 26 + 40 + 8 + 40 + 12
                        + 28 + mapNoteH + mapRows * 40 + (mapRows - 1) * rowGap + 12
                        + 28 + diffNoteH + 40 + 20
                        + 26 + 40 + 20
                        + 44 + 20
                        + 26 + bossRows * 40 + (bossRows - 1) * rowGap + 16
                        + ctrlH + 16 + 40 + pad;
            var r = Card(W, total, -1, true, UiTheme.Gold);
            float x = r.x + pad, y = r.y + pad, cw = (inner - (perRow - 1) * colGap) / perRow;

            T(r.x, y, W, 48, UiTheme.Spaced("Last Bell"), display36); y += 52;
            T(r.x, y, W, 26, "Boss-rush test build · pick a class, a map and a difficulty", subC); y += 42;

            T(x, y, inner, 20, UiTheme.Spaced("Class"), eyebrow); y += 26;
            for (int i = 0; i < RunDirector.Classes.Length; i++)
            {
                var br = S(x + i * (cw + colGap), y, cw, 40);
                bool on = menuClass == i;
                if (GUI.Toggle(br, on, (on ? "> " : "") + RunDirector.Classes[i], btn)) menuClass = i;
                if (on) UiTheme.Border(br, UiTheme.Gold, Px);
            }
            y += 48;
            string desc = menuClass == 0
                ? "Two throwing axes. LMB Twin Hurl (tap, or hold to lodge) · 2 Grinding Toll · 3 Splitting Throw (his hits +40%) · 4 Call of Iron (recall through the boss) · 5 Bell Frenzy."
                : menuClass == 1
                ? "Bell staff. 1 Resonant Note (+Harmony) · 2 Bellstrike (2 charges, rings a bell) · 3 Harmonic Refrain (adds echoes) · 4 Crescendo (detonate) · 6 Mending Chorus (instant heal)."
                : "Time magic. 1 Clockbolt (+Tick, max 3) · 2 Time Seal (2 charges, 6 s, ripe last 2 s) · 3 Arc Lance (3 Ticks: inscribes a seal) · 4 Convergence (detonate seals) · 6 Temporal Anchor (recast to return + shield).";
            T(x, y, inner, 40, desc, descSt); y += 52;

            T(x, y, inner, 20, UiTheme.Spaced("Map"), eyebrow); y += 22;
            T(x, y, inner, mapNoteH, mapNote, microWrap); y += mapNoteH + 6;
            for (int i = 0; i < RunMaps.All.Length; i++)
            {
                var mr = S(x + (i % perRow) * (cw + colGap), y + (i / perRow) * (40 + rowGap), cw, 40);
                bool on = cur == i;
                if (GUI.Toggle(mr, on, (on ? "> " : "") + RunMaps.All[i].Name, mapBtn) && !on) d.SelectMap(i);
                if (on) UiTheme.Border(mr, UiTheme.Gold, Px);
            }
            y += mapRows * 40 + (mapRows - 1) * rowGap + 12;

            T(x, y, inner, 20, UiTheme.Spaced("Difficulty"), eyebrow); y += 22;
            T(x, y, inner, diffNoteH, diffNote, microWrap); y += diffNoteH + 6;
            int nd = RunDifficulty.Names.Length; float dw = (inner - (nd - 1) * colGap) / nd;
            for (int i = 0; i < nd; i++)
            {
                var dr = S(x + i * (dw + colGap), y, dw, 40);
                bool on = (int)level == i;
                if (GUI.Toggle(dr, on, (on ? "> " : "") + RunDifficulty.Names[i], btn) && !on) d.SelectDifficulty((Difficulty)i);
                if (on) UiTheme.Border(dr, UiTheme.Gold, Px);
            }
            y += 60;

            // bot allies (RunParty): Chzy18 wants the count in the menu for every test build
            T(x, y, inner, 20, UiTheme.Spaced("Allies (bots)") + "   (A to switch)", eyebrow); y += 26;
            int na = RunParty.MaxAllies + 1; float aw = (inner - (na - 1) * colGap) / na;
            for (int i = 0; i < na; i++)
            {
                var ar = S(x + i * (aw + colGap), y, aw, 40);
                bool on = RunParty.Selected == i;
                if (GUI.Toggle(ar, on, (on ? "> " : "") + i, btn) && !on) d.SelectAllies(i);
                if (on) UiTheme.Border(ar, UiTheme.Gold, Px);
            }
            y += 60;
            if (GUI.Button(S(r.x + W / 2 - 150, y, 300, 44), "START RUN  (Enter)", btnP)) d.StartRun(RunDirector.Classes[menuClass]);
            y += 64;

            T(x, y, inner, 20, UiTheme.Spaced("Practice one boss"), eyebrow); y += 26;
            for (int i = 0; i < RunDirector.Bosses.Length; i++)
                if (GUI.Button(S(x + (i % perRow) * (cw + colGap), y + (i / perRow) * (40 + rowGap), cw, 40), RunDirector.Bosses[i].Name, mapBtn)) d.StartPractice(RunDirector.Classes[menuClass], i);   // a boss with its own arena loads it first
            y += bossRows * 40 + (bossRows - 1) * rowGap + 16;

            F(x, y - 8, inner, 1, A(UiTheme.Gold, 0.18f));
            foreach (var p in paras)
            {
                float ph = Hv(controls, p, inner);
                T(x, y, inner, ph + 4, p, controls); y += ph + paraGap;
            }
            y += 16 - paraGap;
            if (GUI.Button(S(r.xMax - pad - 120, y, 120, 40), "Quit", btn)) Application.Quit();
        }

        // ---------------------------------------------------------------------------------------------------------
        // results + pause (modals)
        // ---------------------------------------------------------------------------------------------------------
        // Results in the menus' language: no boxes. A soft dark band, an eyebrow + rule per column, faint row dividers and
        // text actions with the gold highlight strip (keys R / M still work).
        // text action with the main menu's highlight strip on hover (UiImgui.Row; the Ivory .78 rest colour is this screen's own)
        static readonly Color resultRest = new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, .78f);
        bool StripButton(float x, float y, float w, float h, string label, GUIStyle st) => UiImgui.Row(new Rect(x, y, w, h), label, st, resultRest);
        string Ellipsize(GUIStyle st, string t, float maxW)
        {
            if (Wv(st, t) <= maxW) return t;
            while (t.Length > 1 && Wv(st, t + "…") > maxW) t = t.Substring(0, t.Length - 1);
            return t.TrimEnd() + "…";
        }
        void ColumnHead(float x, float y, float w, string text, bool centred = false)
        {
            T(x, y, w, 20, UiTheme.Spaced(text), centred ? eyebrowC : eyebrow);
            F(centred ? x + w / 2 - 48 : x, y + 26, 96, 2, UiTheme.Gold);
        }

        // LAN client results: the host decides Retry or Main Menu; the client can only wait or leave.
        void LanResults()
        {
            Dim();
            float vw = UiTheme.VirtualWidth, CW = 560, cx = vw / 2 - CW / 2, y = 220;
            DrawBand(y - 120, 520, 1);
            string head = d.Victory ? "VICTORY" : "DEFEAT";
            T(cx, y, CW, 56, UiTheme.Spaced(head), display40, d.Victory ? UiTheme.Gold : UiTheme.Danger); y += 64;
            F(cx + CW / 2 - 60, y, 120, 1.5f, UiTheme.Gold); y += 22;
            const string body = "Waiting for the host to retry or leave.";
            T(cx, y, CW, Hv(bodyC, body, CW) + 4, body, bodyC); y += 60;
            if (StripButton(cx, y, CW, 44, "Leave session", resultAction)) LanLeave?.Invoke();
        }

        void Results()
        {
            Dim();
            bool test = LastBell.Menu.MenuReturnBridge.TestSession, practice = d.Mode == RunMode.Practice;
            var w = d.World;
            string head = d.Victory ? (practice ? "PRACTICE CLEARED" : "VICTORY") : "DEFEAT";
            string shown = head.Length <= 8 ? UiTheme.Spaced(head) : head;
            string summary = d.Victory ? (practice ? "Practice result only: the run is unchanged." : d.BonusLost ? "The Bellkeeper is silenced and the run is won. The Overclocked Tempo Tyrant kept time: his card waits in Boss Rush."
                                       : d.BonusFight ? "The Overclocked Tempo Tyrant is broken. Nothing is left to ring." : "The Bellkeeper is silenced. The run is cleared."
                                         + (d.Difficulty == Difficulty.Hard ? "" : " Win on Hard to face the Overclocked Tempo Tyrant."))
                                       : $"Downed by {d.DefeatCause} during {(d.Boss ? d.Boss.DisplayName : RunDirector.Bosses[d.Encounter].Name)}.";
            float secs = d.RunTicks / 60f;
            var rows = new List<(string, string)>
            {
                ("Time in combat", $"{Mathf.FloorToInt(secs / 60f)}:{Mathf.FloorToInt(secs % 60f):00}"),
                ("Encounters cleared", d.EncountersCleared.ToString()),
            };
            if (w != null) { rows.Add(("Damage dealt", w.DamageDealt.ToString())); rows.Add(("Damage taken", w.DamageTaken.ToString())); }

            bool recap = !d.Victory && w != null && w.TakenLog.Count > 0, meter = w != null && w.RunMeter.Count > 0;
            const float CW = 560, SW = 400, gap = 72, rowH = 30, y0 = 200;
            float vw = UiTheme.VirtualWidth, cx = vw / 2 - CW / 2;
            DrawBand(y0 - 120, 860, 1);

            // centre: title, rule, summary, stats, actions
            float y = y0;
            T(cx, y, CW, 56, shown, display40, d.Victory ? UiTheme.Gold : UiTheme.Danger); y += 64;
            F(cx + CW / 2 - 60, y, 120, 1.5f, UiTheme.Gold); y += 22;
            float sh = Hv(bodyC, summary, CW);
            T(cx, y, CW, sh + 4, summary, bodyC); y += sh + 22;
            foreach (var (k, v) in rows)
            {
                T(cx, y, 300, rowH, k, rowLabel);
                T(cx, y, CW, rowH, v, numR);
                F(cx, y + rowH - 1, CW, 1, A(UiTheme.Ivory, 0.10f));
                y += rowH;
            }
            y += 30;
            if (StripButton(cx, y, CW, 48, InputHints.Label(practice ? "Retry Practice" : "Retry Run", HintAction.Retry), resultPrimary)) d.RequestRetry();
            y += 52;
            if (StripButton(cx, y, CW, 44, InputHints.Label("Main Menu", HintAction.MainMenu), resultAction)) d.ReturnToMenu();
            y += 48;
            if (test && StripButton(cx, y, CW, 44, "Test Setup", resultAction)) LastBell.Menu.MenuReturnBridge.Return(true);

            // right: damage meter, unframed, tops aligned with the title
            if (meter) Meter(cx + CW + gap, y0 + 14, w.RunMeter, "DAMAGE · whole " + (practice ? "practice" : "run"), d.RunTicks / 60f, 0, false);

            // left: death recap — the last hits in order, the killing blow marked with a Danger accent and its own label
            if (recap)
            {
                int from = Mathf.Max(0, w.TakenLog.Count - 6);
                float rx = cx - gap - SW, yy = y0 + 14;
                ColumnHead(rx, yy, SW, "Death recap"); yy += 42;
                T(rx, yy, SW, 18, "last hits taken, oldest first", micro, UiTheme.IvoryMicro); yy += 26;
                const float numW = 150;   // damage, hp, time
                int last = w.TakenLog[w.TakenLog.Count - 1].tick, hover = -1;
                var mouse = Event.current.mousePosition;
                for (int i = from; i < w.TakenLog.Count; i++)
                {
                    var e = w.TakenLog[i]; bool fatal = i == w.TakenLog.Count - 1 && e.hpAfter <= 0;
                    // a catalogued boss attack shows its icon (40 px on the killing blow, 22 px otherwise) and "Attack · Boss";
                    // the name always sits beside the icon, because several icons look alike at small sizes (BossAttackIcons)
                    var at = TakenAttack(w, i); var icon = BossAttackIcons.Icon(at);
                    float rh = fatal ? (icon != null ? 62 : 46) : 28, ty = yy + (fatal ? 18 : 0), ic = fatal ? 40 : 22, lx = rx;
                    if (fatal) { F(rx - 12, yy + 2, SW + 12, rh - 4, A(UiTheme.Danger, 0.10f)); F(rx - 12, yy + 2, 4, rh - 4, UiTheme.Danger); T(rx, yy + 2, SW, 16, "KILLING BLOW", micro, UiTheme.Danger); }
                    if (icon != null) { GUI.DrawTexture(S(rx, ty + (fatal ? 0 : 2), ic, ic), icon, ScaleMode.ScaleToFit, true); lx = rx + ic + 8; }
                    string what = at != null ? at.Label : e.source;
                    T(lx, ty, SW - numW - 8 - (lx - rx), 26, Ellipsize(small13, what, SW - numW - 8 - (lx - rx)), small13, fatal ? UiTheme.Ivory : UiTheme.IvoryBody);
                    T(rx + SW - numW, ty, 44, 26, "-" + e.damage, small13R, fatal ? UiTheme.Danger : UiTheme.IvoryBody);
                    T(rx + SW - numW + 46, ty, 54, 26, "hp " + e.hpAfter, small13R, UiTheme.IvoryMuted);
                    T(rx + SW - numW + 100, ty, 50, 26, $"{(e.tick - last) / 60f:0.0}s", small13R, UiTheme.IvoryMuted);
                    if (!fatal) F(rx, yy + rh - 1, SW, 1, A(UiTheme.Ivory, 0.08f));
                    if (at != null && S(rx - 12, yy, SW + 12, rh).Contains(mouse)) hover = i;
                    yy += rh;
                }
                if (hover < 0 && PinRecapTooltip) hover = w.TakenLog.Count - 1;
                var ha = hover >= 0 ? TakenAttack(w, hover) : null;
                if (ha != null) AttackTooltip(rx, yy + 12, SW, ha, $"Hit you for {w.TakenLog[hover].damage} (hp {w.TakenLog[hover].hpAfter} after){(hover == w.TakenLog.Count - 1 ? ": the killing blow." : ".")}");
            }
        }

        // Boss attack icons in the recap (BossAttackIcons): the attack ID recorded per hit, parallel to TakenLog.
        // PinRecapTooltip shows the killing blow's details without a mouse (autotest screenshots).
        public static bool PinRecapTooltip;
        // A lethal hit that names its own cause, not just its attack (a Bellkeeper Ultimate wipe: "The escapement skips (empty
        // plate)"), keeps that text on the downed card, with its family's icon and name on the second line like a ForceDown wipe.
        internal static bool NamesOwnCause(string source, BossAttackIcons.Attack a)
        {
            if (a == null || string.IsNullOrEmpty(source) || string.Equals(source, a.name, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (a.aliases != null) foreach (var al in a.aliases) if (string.Equals(source, al, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static BossAttackIcons.Attack TakenAttack(CombatWorld w, int i) => i < w.TakenAttackIds.Count ? BossAttackIcons.Get(w.TakenAttackIds[i]) : null;

        // Boss attack details (virtual units): the 64 px icon with the attack name, boss and tag beside it, then its shape.
        void AttackTooltip(float x, float y, float width, BossAttackIcons.Attack a, string line)
        {
            const float pad = UiTheme.G2, ic = 64;
            string body = a.shape + (string.IsNullOrEmpty(line) ? "" : "\n" + line);
            float tw = width - 2 * pad - ic - 12, bh = Hv(descSt, body, tw), h = Mathf.Max(ic + 2 * pad, pad + 22 + 20 + bh + pad);
            UiTheme.Frame(S(x, y, width, h), UiTheme.InkDeep, true, true);
            F(x, y, 3, h, UiTheme.Gold);
            var icon = BossAttackIcons.Icon(a);
            if (icon != null) GUI.DrawTexture(S(x + pad, y + pad, ic, ic), icon, ScaleMode.ScaleToFit, true);
            float tx = x + pad + ic + 12;
            T(tx, y + pad, tw, 22, a.name.ToUpper(), heading);
            T(tx, y + pad + 22, tw, 20, $"{a.bossName}   ·   {a.tag}", eyebrow);
            T(tx, y + pad + 44, tw, bh + 4, body, descSt);
        }

        void Pause()
        {
            Dim();
            bool test = LastBell.Menu.MenuReturnBridge.TestSession;
            int nb = test ? 4 : 3;
            const float W = 360, pad = UiTheme.G3, bh = 44, gap = 12;
            var r = Card(W, pad + 44 + 16 + nb * bh + (nb - 1) * gap + pad, -1, true, UiTheme.Gold);
            float y = r.y + pad, bx = r.x + pad, bw = W - 2 * pad;
            T(r.x, y, W, 44, UiTheme.Spaced("Paused"), display30); y += 60;
            if (GUI.Button(S(bx, y, bw, bh), "RESUME  (Esc)", btnP)) d.SetPaused(false); y += bh + gap;
            if (GUI.Button(S(bx, y, bw, bh), d.Mode == RunMode.Practice ? "RESTART PRACTICE" : "RESTART RUN", btn)) d.RequestRetry(); y += bh + gap;
            if (GUI.Button(S(bx, y, bw, bh), "MAIN MENU", btn)) d.ReturnToMenu(); y += bh + gap;
            if (test && GUI.Button(S(bx, y, bw, bh), "TEST SETUP", btn)) LastBell.Menu.MenuReturnBridge.Return(true);
        }

        // ---------------------------------------------------------------------------------------------------------
        // combat HUD
        // ---------------------------------------------------------------------------------------------------------
        void Track()
        {
            float x = Left, y = 32, h = 28;
            if (d.Mode == RunMode.Practice)
            {
                const string t = "PRACTICE — does not advance the run";
                float tw = Wv(tab, t) + 24;
                F(x, y, tw, h, A(UiTheme.Ink, 0.7f)); F(x, y + h - 2, tw, 2, UiTheme.Gold);
                T(x, y, tw, h - 2, t, tab);
                return;
            }
            // campaign: one arena's stretch at a time, after its name
            var (first, last) = Campaign.Cluster(d.EncounterOrder, d.ProgressSlot);
            {
                string area = UiTheme.Spaced(Campaign.AreaName(d.Encounter));
                float aw = Wv(tab, area) + 24;
                F(x, y, aw, h, A(UiTheme.InkDeep, 0.8f)); T(x, y, aw, h - 2, area, tab, UiTheme.GoldLabel);
                x += aw + 4;
            }
            for (int i = first; i <= last; i++)
            {
                int cur = d.ProgressSlot; bool done = i < cur || (i == cur && d.State == RunState.Cleared) || (d.State == RunState.Results && d.Victory);
                bool now = !done && i == cur;
                string t = (done ? "+ " : now ? "> " : "") + RunDirector.Bosses[d.EncounterOrder[i]].Name;
                float tw = Wv(tab, t) + 24;
                F(x, y, tw, h, A(UiTheme.Ink, 0.7f));
                if (now) F(x, y + h - 2, tw, 2, UiTheme.Gold);
                else if (done) F(x, y + h - 2, tw, 2, UiTheme.GoldDim);
                T(x, y, tw, h - 2, t, tab, now ? UiTheme.Ivory : done ? UiTheme.IvoryMuted : UiTheme.IvoryMicro);
                x += tw + 4;
            }
        }

        void Boss()
        {
            var b = d.Boss; if (b == null) return;
            float vw = UiTheme.VirtualWidth, bw = Mathf.Min(720, Mathf.Min(vw, 1920) * 0.55f), x = vw / 2 - bw / 2;
            float frac = b.MaxHp > 0 ? Mathf.Clamp01((float)b.Hp / b.MaxHp) : 0, trail = chipBoss == b ? Mathf.Max(chip, frac) : frac;

            const float by = 78, bh = 18, rowH = 24;
            float rowsY = by + bh + 6;
            // no backing plate (Risxhb): the title and rows read on drop shadows alone

            // title: Display 30, 10 px above the bar frame
            { string bt = b.DisplayName.ToUpper(); T(x - 120, 30, bw + 240, 38, bt, Fit(display30, bt, bw + 200), BA(UiTheme.Ivory)); }

            // bar: gold frame + ink gap, trough, damage chip, red fill with a highlight line.
            // The frame reads at high resolutions: 2 px Gold from Scale 1.3 up, otherwise 1 px GoldBright 0.9.
            var outer = S(x - 3, by - 3, bw + 6, bh + 6);
            // ornamental end pieces under the bar (painted PNGs from Resources/UI/BossBar when present, procedural filigree otherwise)
            BossBarArt.DrawOrnaments(outer, bossAlpha);
            UiTheme.Fill(outer, BA(UiTheme.InkDeep));
            if (UiTheme.Scale >= 1.3f) UiTheme.Border(outer, BA(UiTheme.Gold), Mathf.Max(2, Mathf.Round(UiTheme.V(1.5f))));
            else UiTheme.Border(outer, BA(A(UiTheme.GoldBright, 0.9f)), Px);
            F(x, by, bw, bh, BA(UiTheme.BarBack));
            if (trail > frac) F(x, by, bw * trail, bh, BA(UiTheme.Chip));
            // animated HP pool: flowing liquid, sheen sweep, glowing leading edge, hit flash, low-HP pulse
            BossBarArt.Draw(S(x, by, bw, bh), frac, trail, bossAlpha, hitFlash, frac < 0.2f);
            var ticks = PhaseTicks(b);
            if (ticks != null)
                foreach (var t in ticks)
                {
                    float tx = x + bw * t;
                    F(tx - 1, by - 3, 2, 24, BA(UiTheme.Ivory));
                    UiTheme.Diamond(new Vector2(tx, by - 8) * UiTheme.Scale, UiTheme.V(6), BA(UiTheme.Ivory));
                }

            // phase line (no HP numbers; the Tempo Tyrant shows its tempo in its own HUD)
            float yy = rowsY;
            if (!(b is TempoTyrantEncounter)) T(x + 10, yy, bw * 0.65f, rowH, b.PhaseName, row15, BA(UiTheme.Ivory));
            // Aurelion v4: the enrage clock and the Corona Shield (BossEncounter.HudExtra), right of the phase line
            // (numR is the same style; building a new GUIStyle here every frame made every layout of the ticking shield text a cold one)
            if (b.HudExtraShown != null) T(x + bw * 0.35f, yy, bw * 0.65f - 10, rowH, b.HudExtraShown, numR, BA(UiTheme.Ivory));
            // an encounter's own lines under it (the Bellkeeper Ultimate: segment, push timer and the player's job this beat)
            var extra = b.HudLine;
            if (!string.IsNullOrEmpty(extra)) foreach (var line in extra.Split('\n')) { yy += rowH; TickLine(x + 10, yy, rowH, line, row15, BA(UiTheme.Ivory)); }

            // attack-name warning rows removed (Risxhb, October 24, 2026): telegraphs on the floor carry the warning.
            // Exception (Chzy18, October 24, 2026): a hazard tagged with a hostile mechanic (Hazard.MechanicId) gets one row with
            // the mechanic's 22 px icon and name, after the attack's own icon and name when it has one. Untagged hazards stay
            // floor-only, so this panel is unchanged until an encounter tags a mechanic. docs/run-loop/mechanic-icons.md
            var seen = new HashSet<string>(); yy += rowH + 4;
            foreach (var hzd in d.World.Hazards.Live)
            {
                var m = MechanicIcons.Get(hzd.MechanicId); if (m == null) continue;
                var at = BossAttackIcons.Resolve(hzd, b);
                if (!seen.Add((at?.id ?? hzd.Source) + "|" + m.id)) continue;
                float lx = x + 10;
                var ai = BossAttackIcons.Icon(at);
                if (ai != null) { GUI.DrawTexture(S(lx, yy + 1, 22, 22), ai, ScaleMode.ScaleToFit, true); lx += 28; }
                string what = (at != null ? $"{at.name}  ({at.bossName})" : hzd.Source).ToUpper();
                T(lx, yy, Wv(row15, what) + 4, rowH, what, row15, BA(UiTheme.Ivory)); lx += Wv(row15, what) + 12;
                var mi = MechanicIcons.Icon(m);
                if (mi != null) { GUI.DrawTexture(S(lx, yy + 1, 22, 22), mi, ScaleMode.ScaleToFit, true); lx += 28; }
                Hints.TryGetValue(hzd.Source, out var hint);
                string mname = m.name.ToUpper() + (string.IsNullOrEmpty(hint) ? "" : " — " + hint);
                T(lx, yy, Wv(row15, mname) + 4, rowH, mname, row15, BA(UiTheme.GoldBright));
                yy += rowH;
            }
        }

        // Skill bar icons by Codex (#43 / PR #45): Resources/Run/Icons/<file>.png, 256 px. Slot order = the kit's slots,
        // then the class's dash, then the potion. A missing icon falls back to the ability name.
        static readonly Dictionary<string, string[]> SlotIconNames = new Dictionary<string, string[]>
        {
            // Tollbreaker: Grinding Toll and Bell Frenzy show his own shop items' art (Long Grind: the spinning axe; Frenzied Hour:
            // the bell and axes) until their ability icons are drawn; they were the retired Arcanist and Chimes art (PR review ICON-01)
            ["Tollbreaker"] = new[] { "tollbreaker_twin_hurl", "items/long-grind-128", "tollbreaker_splitting_throw", "tollbreaker_call_of_iron", "items/frenzied-hour-128", "tollbreaker_dash" },
            ["Chimesinger"] = new[] { "chimesinger_resonant_note", "chimesinger_bellstrike", "chimesinger_harmonic_refrain", "chimesinger_crescendo", "chimesinger_mending_chorus", "chimesinger_harmonic_step" },
            ["Arcanist"] = new[] { "arcanist_magic_missile", "arcanist_disintegrate", "arcanist_rune_sigil", "arcanist_comet", "arcanist_arcane_surge", "arcanist_blink" },
        };
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        public static Texture2D Icon(string file)
        {
            if (!icons.TryGetValue(file, out var t)) icons[file] = t = Resources.Load<Texture2D>("Run/Icons/" + file);
            return t;
        }
        // slot 0..n-1 = kit slots, n = dash; returns null when the class or icon is unknown
        public static Texture2D SlotIcon(string className, int slot) =>
            SlotIconNames.TryGetValue(className, out var names) && slot < names.Length ? Icon(names[slot]) : null;

        // ---- player panel: no backing panel, liquid HP, quiet ability slots ----
        // A skill being available is the normal, silent state. Only exceptions are marked: cooldown (dimmed icon, sweep and
        // seconds), charges (corner count), empowered / combo / active states (caption, gold edge when empowered) and
        // "can't use" (dimmed icon + caption). Keybinds show only the binding for the player's current input.
        const float TileW = 64, TileH = 64, CapH = 18;
        static bool preferMouseBasic = true;   // KeyFor: the mouse half of an "x / LMB" binding (keys now come from InputHints)
        int lastPlayerHp = -1; float playerFlash;

        void TickPlayerInput()
        {
            var p = d != null ? d.Player : null;
            if (p != null)
            {
                if (lastPlayerHp >= 0 && p.Hp < lastPlayerHp) playerFlash = 1;
                lastPlayerHp = p.Hp;
            }
            else lastPlayerHp = -1;
            playerFlash = Mathf.MoveTowards(playerFlash, 0, Time.unscaledDeltaTime / 0.18f);
            if (d == null || d.State != RunState.Combat) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) preferMouseBasic = true;
        }

        // "1 / LMB" -> the binding for the current input (current bindings have one key each); "4 hold" -> "4" plus the hold glyph
        static string KeyFor(string binding, bool preferMouse, out bool hold)
        {
            hold = binding.EndsWith(" hold"); if (hold) binding = binding.Substring(0, binding.Length - 5);
            int slash = binding.IndexOf(" / ");
            if (slash < 0) return binding;
            return preferMouse ? binding.Substring(slash + 3) : binding.Substring(0, slash);
        }

        static Texture2D holdGlyph;
        // small "press and hold" glyph: a ring with a filled lower half
        static Texture2D HoldGlyph()
        {
            if (holdGlyph) return holdGlyph;
            const int S = 32; holdGlyph = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1.5f - Mathf.Abs(r - 12f));
                float fill = r < 10.5f && dy < 1.5f ? Mathf.Clamp01(10.5f - r) : 0;   // lower half (texture rows run bottom-up)
                holdGlyph.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(ring, fill)));
            }
            holdGlyph.Apply(); return holdGlyph;
        }

        enum Mark { None, Cooling, Charges, Empowered, Active, Blocked }
        static Mark Classify(string state, float cooldown01, out string caption, out string corner)
        {
            caption = null; corner = null; state = state ?? "";
            if (state == "" || state == "ready") return cooldown01 > 0 ? Mark.Cooling : Mark.None;
            // "1/2" or "1/2 +3.4s": charges in the corner, recharge seconds as the caption while not full
            var m = System.Text.RegularExpressions.Regex.Match(state, @"^(\d+)/(\d+)(?:\s*\+(.+))?$");
            if (m.Success)
            {
                corner = m.Groups[1].Value;
                if (m.Groups[3].Success) caption = m.Groups[3].Value;
                return m.Groups[1].Value == "0" ? Mark.Cooling : Mark.Charges;
            }
            if (cooldown01 > 0 || System.Text.RegularExpressions.Regex.IsMatch(state, @"^\d+(\.\d+)?s$")) { caption = state; return Mark.Cooling; }
            caption = state.ToUpperInvariant();
            if (state == "EMPOWERED" || state == "TICKS FULL") return Mark.Empowered;
            if (state.StartsWith("needs") || state.StartsWith("no ") || state == "stamina") return Mark.Blocked;
            return Mark.Active;
        }

        // x, y virtual top-left of the icon; returns the icon's screen rect (tooltip hit area)
        Rect Tile(float x, float y, string key, bool hold, Texture2D icon, string name, string state, float cooldown01, bool off, string pips = null)
        {
            var r = S(x, y, TileW, TileH);
            string caption = null, corner = null;
            var mark = off ? Mark.Blocked : Classify(state, cooldown01, out caption, out corner);
            bool cooling = mark == Mark.Cooling, blocked = mark == Mark.Blocked;
            // quiet well: soft ink, hairline edge; a gold edge only when empowered
            UiTheme.Fill(r, A(UiTheme.InkDeep, 0.55f));
            UiTheme.Border(r, mark == Mark.Empowered ? UiTheme.GoldBright : A(UiTheme.GoldDim, 0.6f), mark == Mark.Empowered ? Mathf.Max(2, Px) : Px);
            var ir = S(x + 4, y + 4, TileW - 8, TileH - 8);
            var old = GUI.color;
            if (icon != null) { GUI.color = cooling ? new Color(1, 1, 1, 0.35f) : blocked ? new Color(0.62f, 0.62f, 0.66f, 0.55f) : Color.white; GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit, true); GUI.color = old; }
            else T(x + 2, y + 12, TileW - 4, 40, name, slotName);
            if (cooling)
            {
                F(x + 1, y + 1, TileW - 2, (TileH - 2) * Mathf.Clamp01(cooldown01), new Color(0, 0, 0, 0.5f));
                if (!string.IsNullOrEmpty(caption)) { T(x, y + TileH / 2 - 12, TileW, 24, caption, numC); caption = null; }
            }
            // keybind: small shadowed text in the top-left corner, no box
            T(x + 5, y + 2, TileW - 10, 16, key, keySt, blocked ? UiTheme.IvoryMuted : UiTheme.Ivory);
            if (hold)
            {
                var hr = S(x + TileW - 17, y + TileH - 17, 13, 13);
                GUI.color = new Color(0, 0, 0, 0.8f); GUI.DrawTexture(new Rect(hr.x + Px, hr.y + Px, hr.width, hr.height), HoldGlyph());
                GUI.color = UiTheme.Ivory; GUI.DrawTexture(hr, HoldGlyph()); GUI.color = old;
            }
            if (corner != null && !off) T(x + 5, y + 2, TileW - 10, 16, corner, cornerSt, UiTheme.GoldBright);
            // exceptions only: caption under the slot
            if (!off && !string.IsNullOrEmpty(caption))
                T(x - 10, y + TileH + 2, TileW + 20, CapH, caption, stateSt, mark == Mark.Empowered ? UiTheme.GoldBright : blocked ? UiTheme.IvoryMuted : UiTheme.IvoryBody);
            if (pips != null) T(x - 10, y + TileH + 2, TileW + 20, CapH, pips, stateSt, off ? UiTheme.IvoryMuted : UiTheme.Ivory);
            return r;
        }

        // bot allies (RunParty): class and health, left edge
        void PlayerPanel()
        {
            var p = d.Player; if (p == null) return;
            long t = Stamp();
            var k = p.Kit; int n = k.SlotNames.Length;
            const float gap = 10;
            float barW = (n + 2) * TileW + (n + 1) * gap;
            bool off = d.State != RunState.Combat;
            float vw = UiTheme.VirtualWidth, pw = Mathf.Max(480, barW), x = vw / 2 - pw / 2, top = PanelTop;
            float y = top;
            // health: liquid pool (same animation as the boss bar), class name left and number right on the bar
            float hf = Mathf.Clamp01((float)p.Hp / p.MaxHealth);
            var hb = S(x - 2, y - 2, pw + 4, 20);
            UiTheme.Fill(hb, A(UiTheme.InkDeep, 0.85f)); UiTheme.Border(hb, A(UiTheme.GoldDim, 0.8f), Px);
            F(x, y, pw, 16, UiTheme.BarBack);
            BossBarArt.Draw(S(x, y, pw, 16), hf, hf, 1, playerFlash, hf < 0.3f);
            T(x + 8, y - 4, pw * 0.5f, 24, p.ClassName, keyStL, UiTheme.IvoryBody);
            y += 22;
            // stamina: thin bar; its number only appears while it is spent
            float st = Mathf.Clamp01(p.Stamina / p.MaxStaminaNow);
            F(x, y, pw, 5, A(UiTheme.BarBack, 0.9f));
            F(x, y, pw * st, 5, UiTheme.Stamina);
            if (st < 0.999f) T(x, y + 4, pw, 16, Mathf.RoundToInt(p.Stamina).ToString(), microR, UiTheme.IvoryMuted);
            y += 18;
            t = Sec("player panel: bars", t);

            float bx = vw / 2 - barW / 2, by = y; int hover = -1;
            for (int i = 0; i < n; i++)
            {
                KeyFor(k.SlotKeys[i], preferMouseBasic, out bool hold);   // the kit says which slots are held
                string key = InputHints.Slot(i);   // the gameplay binding table (KeyboardMouseInput.SlotKeys)
                long tk = Stamp(); string state = k.ShownSlotState(i); float cd = k.ShownCooldown(i); Sec("player panel: kit state", tk);
                var r = Tile(bx + i * (TileW + gap), by, key, hold, SlotIcon(p.ClassName, i), k.SlotNames[i], state, cd, off);
                if (r.Contains(Event.current.mousePosition)) hover = i;
            }
            bool dashReady = p.Stamina >= PlayerController.DashCost;
            var dr = Tile(bx + n * (TileW + gap), by, InputHints.Key(HintAction.Dash), false, SlotIcon(p.ClassName, n),
                          p.ClassName == "Chimesinger" ? "Harmonic Step" : p.ClassName == "Arcanist" ? "Blink" : "Dash",
                          dashReady ? "" : "stamina", 0, off);
            var pr = Tile(bx + (n + 1) * (TileW + gap), by, InputHints.Key(HintAction.Potion), false, Icon("potion"), "Potion",
                          p.Potions == 0 ? "no potions" : "", 0, off, p.Potions == 0 ? null : new string('●', p.Potions) + new string('○', Mathf.Max(0, PlayerController.MaxPotions - p.Potions)));
            // the controls hint line was removed from the player HUD at Risxhb's request
            t = Sec("player panel: tiles", t);

            // class state (HARMONY / SEALS / TICKS ...) as quiet tags above the health bar
            string cs = k.ShownClassState();
            if (!string.IsNullOrEmpty(cs))
            {
                // drawn in segments with each time its own label (TimeSegs): the Tollbreaker's "SPLIT 4.1s  FRENZY 2.0s  AXES ..."
                // was a new text every tenth and axe state, filling Unity's text cache (perf report, Update 19:30)
                var parts = cs.Split(new[] { "   " }, System.StringSplitOptions.RemoveEmptyEntries);
                float total = 0; var widths = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++) { parts[i] = parts[i].Trim(); widths[i] = SegsWidth(pill, TimeSegs(parts[i])) + 23; total += widths[i] + (i > 0 ? 8 : 0); }
                float px = vw / 2 - total / 2, py = top - 10 - 24;
                for (int i = 0; i < parts.Length; i++)
                {
                    // the main menu's small info card: ink strip, 3 px Gold left bar (UiImgui.InfoStrip), Gold label
                    UiImgui.InfoStrip(new Rect(px, py, widths[i], 24), UiTheme.InkStrip);
                    DrawSegs(px + 11, py, 24, TimeSegs(parts[i]), pill);   // a segment is centred in its width + 4, so +11 puts it 10 past the bar
                    px += widths[i] + 8;
                }
            }
            t = Sec("player panel: class state", t);
            int over = pr.Contains(Event.current.mousePosition) ? 100 : dr.Contains(Event.current.mousePosition) ? 101 : hover;
            if (Event.current.type == EventType.Repaint && over != tipSlot) { tipSlot = over; tipSince = Time.unscaledTime; }
            var mouse = Mouse.current; bool attacking = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
            bool showTip = over >= 0 && !attacking && Time.unscaledTime - tipSince >= TipDelay;
            if (!showTip) { }
            else if (pr.Contains(Event.current.mousePosition)) Tooltip(x, top, $"POTION  [{InputHints.Key(HintAction.Potion)}]\n+{PlayerController.PotionHeal} HP, 1 s between drinks. {p.Potions}/{PlayerController.MaxPotions} left.\nPotions carry over between minibosses; health and potions refill before each area boss.");
            else if (dr.Contains(Event.current.mousePosition)) Tooltip(x, top, "DASH  [" + InputHints.Key(HintAction.Dash) + "]\nShort burst (3 m in 0.2 s) in the move direction. Costs 30 stamina; sprint shares the bar.\nNo invincibility: get out of the shape before it resolves. Cancels basic attacks and unreleased windups.");
            else if (hover >= 0) Tooltip(x, top, k.Tooltip(hover));
            Sec("player panel: tooltip", t);
            if (ShowMeter && d.World != null) Meter(Right - MeterW, 260, d.World.EncounterMeter, "DAMAGE · this encounter", (d.World.Tick - d.World.SegmentStartTick) / 60f);
        }

        // x = panel content left, top = panel top (virtual); sits above the class-state pills
        const float TipDelay = 0.4f; int tipSlot = -1; float tipSince;
        void Tooltip(float x, float top, string text) { tipX = x; tipTop = top; tipText = text; }
        void DrawTooltip(float x, float top, string text)
        {
            const float W = 520, pad = UiTheme.G2;
            float th = Hv(tipSt, text, W - 2 * pad), h = th + 2 * pad;
            float tx = x - UiTheme.G3, ty = top - 46 - 8 - h;
            UiTheme.Frame(S(tx, ty, W, h), UiTheme.InkDeep, true, true);
            F(tx, ty, 3, h, UiTheme.Gold);
            T(tx + pad, ty + pad, W - 2 * pad, th + 4, text, tipSt);
        }

        // PF-07 first pass: per-ability damage, share, hits, largest hit and DPS over the segment. Not Details! parity.
        // x, y virtual; every value sits in a fixed column rect so headers align.
        // minH: match a neighbouring panel's height (Results) so the bottoms align
        const float MeterW = 380;
        void Meter(float x, float y, Dictionary<string, CombatWorld.MeterRow> rows, string title, float seconds, float minH = 0, bool framed = true)
        {
            var list = new List<CombatWorld.MeterRow>(rows.Values); list.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            int total = 0; foreach (var r in list) total += r.Damage;
            const float W = MeterW, rh = 22;
            // header: 12 px tracked caps GoldLabel 0.6; the total / DPS sits right-aligned beside it (or below if it won't fit)
            string head = UiTheme.Spaced(title), sum = $"{total} · {(seconds > 0.5f ? total / seconds : 0):0} DPS";
            bool split = Wv(meterHead, head) + 12 + Wv(microR, sum) > W - 32;
            float headH = split ? 20 + 18 : 20;
            float h = Mathf.Max(minH, 16 + headH + 8 + 18 + 4 + rh * Mathf.Max(1, list.Count) + 16);
            if (framed) UiTheme.Frame(S(x, y, W, h)); else { x -= 16; y -= 16; }   // unframed: content starts at (x, y)
            T(x + 16, y + 16, W - 32, 20, head, meterHead);
            T(x + 16, y + (split ? 36 : 16), W - 32, split ? 18 : 20, sum, microR);
            float yy = y + 16 + headH + 8;
            T(x + 16, yy, 134, 18, "ability", micro, UiTheme.IvoryMicro);
            T(x + 150, yy, 58, 18, "dmg", microHead); T(x + 208, yy, 48, 18, "%", microHead); T(x + 256, yy, 44, 18, "hits", microHead); T(x + 300, yy, 44, 18, "max", microHead);
            yy += 22;
            foreach (var r in list)
            {
                float f = total > 0 ? (float)r.Damage / total : 0;
                F(x + 16, yy + 3, (W - 32) * f, rh - 6, A(UiTheme.Gold, 0.22f));
                T(x + 16, yy, 134, rh, r.Ability, small13);
                T(x + 150, yy, 58, rh, r.Damage.ToString(), small13R);
                T(x + 208, yy, 48, rh, $"{f * 100:0}%", small13R);
                T(x + 256, yy, 44, rh, r.Hits.ToString(), small13R);
                T(x + 300, yy, 44, rh, r.Largest.ToString(), small13R);
                yy += rh;
            }
        }

        // world-anchored labels: positions in screen px, Inter SemiBold with a drop shadow
        void Floats()
        {
            var cam = Camera.main; if (!cam) return; float h = Screen.height;
            foreach (var f in floats)
            {
                var s = cam.WorldToScreenPoint(f.pos); if (s.z < 0) continue;
                var c = f.color; c.a = 1 - Mathf.Clamp01((f.age - 0.5f) / 0.4f);
                // per-float x jitter + a ~40 px screen rise over the lifetime
                float cx = s.x + UiTheme.V(f.jx), cy = h - s.y - UiTheme.V(FloatRise * Mathf.Clamp01(f.age / FloatLife));
                UiTheme.Label(new Rect(cx - UiTheme.V(60), cy - UiTheme.V(15), UiTheme.V(120), UiTheme.V(30)), f.text, floatSt, c);
            }
        }

        // Aurelion v4 overlays (wipe / enrage banner, demo captions, world labels): their own plain styles
        GUIStyle lab, numStyle;
        GUIStyle num => numStyle ?? (numStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
        // Demo caption (CaptionTitle / CaptionBody): the main menu's info card (ink strip, 3 px Gold left bar), centred above
        // the player panel's class-state chips; Ivory heading and IvoryBody body on the theme type ramp.
        const float CaptionH = 96;
        float CaptionTop => PanelTop - 34 - 16 - CaptionH;   // virtual; Labels keep world labels above it
        void CaptionText()
        {
            if (string.IsNullOrEmpty(CaptionTitle)) return;
            float w = Mathf.Min(1100, Mathf.Min(UiTheme.VirtualWidth, 1920) - 80), x = UiTheme.VirtualWidth / 2 - w / 2, y = CaptionTop;
            UiImgui.InfoStrip(new Rect(x, y, w, CaptionH));
            T(x + 24, y + 12, w - 48, 28, CaptionTitle, heading);
            T(x + 24, y + 44, w - 48, 44, CaptionBody ?? "", captionBody);
        }

        // The big centre call (Aurelion wipes, the enrage warning): theme Display type in the caller's colour with the theme
        // shadow, and the 96x2 Gold rule under it as on the main menu's titles. No band: the floor under it stays readable.
        void BannerText()
        {
            var t = CurrentBanner; if (t == null) return;
            float vw = UiTheme.VirtualWidth, y = UiTheme.VirtualHeight * 0.3f;
            T(0, y, vw, 64, t, bannerSt, bannerColor);
            UiImgui.Rule(vw / 2 - UiTheme.RuleWidth / 2, y + 66);
        }
        void Labels()
        {
            var cam = Camera.main; if (!cam) return; float h = Screen.height;
            if (lab == null) lab = new GUIStyle(num) { wordWrap = false, clipping = TextClipping.Overflow };   // "SUNSPOT I  II" stays on one line
            // keep labels out of the boss block, the demo caption and the screen edges (Aurelion readability A1/A3/A4): a
            // label pushed inward keeps a thin leader back to the spot it names
            float top = d.Boss != null ? UiTheme.V(165) : 16, bottom = string.IsNullOrEmpty(CaptionTitle) ? h - UiTheme.V(150) : UiTheme.V(CaptionTop - 10);
            foreach (var l in labels)
            {
                if (l.Hidden || (l.Follow == null && !ReferenceEquals(l.Follow, null))) continue;
                var s = cam.WorldToScreenPoint(l.Follow ? l.Follow.position + l.Pos : l.Pos); if (s.z < 0) continue;
                if (l.Icon)
                {
                    float g = UiTheme.V(l.Size * 2), gn = g * 0.6f, ga = g * 0.35f;
                    float total = l.Next ? g + ga + gn : g;
                    float x0 = Mathf.Clamp(s.x - total / 2, 10, Screen.width - total - 10), y0 = Mathf.Clamp(h - s.y - g / 2, top, Mathf.Max(top, bottom - g)) ;
                    GUI.DrawTexture(new Rect(x0, y0, g, g), l.Icon, ScaleMode.ScaleToFit, true);
                    if (l.Next)
                    {
                        if (l.Then) GUI.DrawTexture(new Rect(x0 + g, y0 + (g - ga) / 2, ga, ga), l.Then, ScaleMode.ScaleToFit, true);
                        GUI.DrawTexture(new Rect(x0 + g + ga, y0 + (g - gn) / 2, gn, gn), l.Next, ScaleMode.ScaleToFit, true);
                    }
                    continue;
                }
                lab.fontSize = l.Size; long t0 = UiTheme.FontLog ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                float wide = Mathf.Max(200, lab.CalcSize(new GUIContent(l.Text)).x + 8);
                if (UiTheme.FontLog) UiTheme.LogSlowText(t0, "label size", l.Text, lab);
                float half = l.Size * 0.6f;
                var at = new Vector2(s.x, h - s.y);
                var c = new Vector2(Mathf.Clamp(at.x, wide / 2 + 10, Screen.width - wide / 2 - 10), Mathf.Clamp(at.y, top + half, Mathf.Max(top + half, bottom - half)));
                if ((c - at).sqrMagnitude > 36)
                {
                    var dir = (at - c).normalized;
                    Leader(c + dir * half, at, new Color(l.Color.r, l.Color.g, l.Color.b, 0.8f));
                    UiTheme.Fill(new Rect(at.x - 3, at.y - 3, 6, 6), l.Color);
                }
                lab.normal.textColor = new Color(0, 0, 0, 0.75f);
                UiTheme.RawLabel(new Rect(c.x - wide / 2 + 2, c.y - 28, wide, 60), l.Text, lab);
                lab.normal.textColor = l.Color;
                UiTheme.RawLabel(new Rect(c.x - wide / 2, c.y - 30, wide, 60), l.Text, lab);
            }
        }

        static void Leader(Vector2 a, Vector2 b, Color c)
        {
            var v = b - a; float len = v.magnitude; if (len < 4) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg, a);
            UiTheme.Fill(new Rect(a.x, a.y - 1, len, 2), c);
            GUI.matrix = m;
        }

        // Mechanic markers over players' heads (PlayerController.Marks, MechanicIcons): 40 px icons side by side, each with its
        // name on a strip under it, because several look alike at this size. Placement approved by Chzy18 on October 24, 2026;
        // see docs/run-loop/mechanic-icons.md. Nothing is drawn until an encounter assigns a mark.
        // Revives: over each downed player, their class's downed bell (Codex's revive HUD art, work/revive-hud-20261104, ivory
        // not gold: gold is the bosses' danger colour) inside the segmented ring, which fills clockwise ivory over slate as the
        // revive runs, and the seconds left to revive them on a strip under it; DEAD (bell dimmed, no ring) once bled out.
        // Beside a downed ally the local player sees the revive prompt (the hand) and what it takes: stand still.
        void DownedMarks()
        {
            var cam = Camera.main; if (!cam || d.World.Players.Count < 2) return; float h = Screen.height, w = UiTheme.V(96), ring = UiTheme.V(64), bell = UiTheme.V(44);
            var me = d.Player;
            foreach (var p in d.World.Players)
            {
                if (p == null || !p.Down) continue;
                var s = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 1.4f); if (s.z < 0) continue;
                float cx = s.x, cy = h - s.y - ring / 2 - UiTheme.V(6);
                if (!p.Dead) { var rt = ReviveRing(p.ReviveProgress); if (rt) GUI.DrawTexture(new Rect(cx - ring / 2, cy - ring / 2, ring, ring), rt, ScaleMode.ScaleToFit, true); }
                var mark = Icon("revive/downed-" + p.ClassName.ToLowerInvariant());
                if (mark) { var c = GUI.color; if (p.Dead) GUI.color = new Color(1, 1, 1, 0.45f); GUI.DrawTexture(new Rect(cx - bell / 2, cy - bell / 2, bell, bell), mark, ScaleMode.ScaleToFit, true); GUI.color = c; }
                var r = new Rect(cx - w / 2, cy + ring / 2 + UiTheme.V(2), w, UiTheme.V(20));
                UiTheme.Fill(r, UiTheme.InkStrip);
                string t = p.Dead ? "DEAD" : $"DOWN  {Mathf.CeilToInt(PlayerController.BleedOutSeconds - p.DownedTicks / (float)Sim.Hz)} s";
                UiTheme.Label(r, t, slotName);
                // the prompt: the local player beside them and up (the revive itself needs them still: PlayerController.CanChannelRevive)
                if (p.Dead || me == null || me == p || me.Down || Sim.Flat(me.transform.position - p.transform.position).magnitude > PlayerController.ReviveRange) continue;
                var hand = Icon("revive/revive-prompt"); float ps = UiTheme.V(36);
                if (hand) GUI.DrawTexture(new Rect(cx + ring / 2 + UiTheme.V(4), cy - ps / 2, ps, ps), hand, ScaleMode.ScaleToFit, true);
                var pr = new Rect(cx + ring / 2 + UiTheme.V(4) + ps, cy - UiTheme.V(10), UiTheme.V(110), UiTheme.V(20));
                UiTheme.Fill(pr, UiTheme.InkStrip); UiTheme.Label(pr, "STAND STILL", slotName);
            }
        }

        // the ring with its first k of ReviveSteps segments ivory, the rest slate, built once per step from the art's alpha
        // (clockwise from the top; the art itself is a half-filled still, so it is never stretched as a timer)
        const int ReviveSteps = 30;
        static Texture2D[] reviveRings;
        // all the steps built on the Ready screen (party fights), so the first revive in combat builds nothing (review PERF-01)
        void WarmReviveRings()
        {
            if (reviveRings != null && reviveRings[ReviveSteps] || d.World == null || d.World.Players.Count < 2) return;
            for (int k = 0; k <= ReviveSteps; k++) ReviveRing(k / (float)ReviveSteps);
        }
        static Texture2D ReviveRing(float progress)
        {
            reviveRings ??= new Texture2D[ReviveSteps + 1];
            int k = Mathf.Clamp(Mathf.FloorToInt(progress * ReviveSteps + 0.001f), 0, ReviveSteps);
            if (reviveRings[k]) return reviveRings[k];
            var src = Icon("revive/revive-ring"); if (!src || !src.isReadable) return src;
            var px = src.GetPixels32(); int n = src.width; var o = new Color32[px.Length];
            var lit = new Color(0.96f, 0.92f, 0.82f); var dim = new Color(0.20f, 0.27f, 0.38f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; var c = px[i]; if (c.a == 0) { o[i] = c; continue; }
                    float a = Mathf.Repeat(Mathf.Atan2(x - n / 2f + 0.5f, y - n / 2f + 0.5f) * Mathf.Rad2Deg, 360f);   // 0 at the top, clockwise
                    float lum = (c.r + c.g + c.b) / (3f * 255f);
                    var tint = a < 360f * k / ReviveSteps ? lit : dim;
                    o[i] = (Color32)new Color(tint.r * (0.55f + 0.45f * lum), tint.g * (0.55f + 0.45f * lum), tint.b * (0.55f + 0.45f * lum), c.a / 255f);
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(o); t.Apply(false, true);
            return reviveRings[k] = t;
        }

        // white anti-aliased disc, tinted at draw time (backing for over-head markers)
        static Texture2D markDisc;
        static Texture2D MarkDisc
        {
            get
            {
                if (markDisc) return markDisc;
                const int n = 64; markDisc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f, a = Mathf.Clamp01(n / 2f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
                markDisc.SetPixels(px); markDisc.Apply(); return markDisc;
            }
        }

        void PlayerMarks()
        {
            var cam = Camera.main; if (!cam) return; float h = Screen.height, sz = UiTheme.V(40), gap = UiTheme.V(48);
            foreach (var p in d.World.Players)
            {
                if (p == null || p.Down || p.Marks.Count == 0) continue;
                var s = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.6f); if (s.z < 0) continue;
                float x0 = s.x - (p.Marks.Count * (sz + gap) - gap) / 2, y0 = h - s.y - sz - UiTheme.V(20);
                for (int i = 0; i < p.Marks.Count; i++)
                {
                    var m = MechanicIcons.Get(p.Marks[i]); if (m == null) continue;
                    var r = new Rect(x0 + i * (sz + gap), y0, sz, sz);
                    // dark ink disc behind the marker so its thin ivory rim reads on light floors (Chzy18, 31 Oct)
                    var gc = GUI.color; GUI.color = UiTheme.InkStrip;
                    GUI.DrawTexture(new Rect(r.center.x - sz * 0.58f, r.center.y - sz * 0.58f, sz * 1.16f, sz * 1.16f), MarkDisc, ScaleMode.StretchToFill, true);
                    GUI.color = gc;
                    var t = MechanicIcons.Icon(m); if (t != null) GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
                    float nw = slotName.CalcSize(new GUIContent(m.name)).x;
                    UiTheme.Fill(new Rect(r.center.x - nw / 2 - UiTheme.V(4), r.yMax + UiTheme.V(1), nw + UiTheme.V(8), UiTheme.V(17)), UiTheme.InkStrip);
                    UiTheme.Label(new Rect(r.center.x - UiTheme.V(60), r.yMax + UiTheme.V(1), UiTheme.V(120), UiTheme.V(17)), m.name, slotName);
                }
            }
        }

        // Test and review aid (autotest screenshot, no key binding): every catalogued mechanic icon at HUD size (64 px) with
        // its name, friendly markers first, then hostile mechanics.
        public static bool ShowMechanicGallery;
        void MechanicGallery()
        {
            const float ic = 64, CW = 128, CH = 96; const int perRow = 8; var all = MechanicIcons.All;
            int friendly = 0; foreach (var m in all) if (m.Friendly) friendly++;
            int rows = (friendly + perRow - 1) / perRow + (all.Count - friendly + perRow - 1) / perRow;   // each set starts a new row
            float pw = perRow * CW + 32, ph = rows * CH + 56, px = UiTheme.VirtualWidth / 2 - pw / 2, py = 150;
            UiTheme.Frame(S(px, py, pw, ph), UiTheme.InkDeep, true, true);
            F(px, py, pw, 3, UiTheme.Gold);
            T(px + 16, py + 12, pw - 32, 22, UiTheme.Spaced("Mechanic icons") + "   ·   friendly markers, then hostile mechanics   ·   64 px", eyebrow);
            int col = 0; float y = py + 44; string set = null;
            foreach (var m in all)
            {
                if (set != null && m.set != set || col == perRow) { col = 0; y += CH; }
                set = m.set;
                float cx = px + 16 + col * CW;
                var t = MechanicIcons.Icon(m); if (t != null) GUI.DrawTexture(S(cx + (CW - ic) / 2, y, ic, ic), t, ScaleMode.ScaleToFit, true);
                T(cx, y + ic + 4, CW, 22, m.name, slotName);
                col++;
            }
        }
    }
}
