using System;
using UnityEngine;
using LastBell.Run.Networking;

namespace LastBell.Run.Presentation
{
    // Hit feel (Presentation lane, 8 Nov 2026; proposal, done as recommended, review later). Three pieces of feedback, all
    // presentation only:
    //   * Damage numbers on the boss. Your hits are ivory (gold and larger when big), allies' hits are smaller and dimmer in
    //     their own column, so four players stay readable. Hits from the same player within MergeWindow add into one number
    //     (beams, spins). A fixed pool of PoolSize numbers, drawn digit by digit from ten cached one-character texts: no string
    //     is built per hit or per frame, and no new IMGUI text handle appears mid-fight.
    //   * Hit-stop. Big hits on the boss and a player going down freeze character animation for a few frames
    //     (CharacterPresenter.GlobalAnimScale = 0). It is capped at MaxStop and the next one can start only Gap after the last
    //     ends, so stops never stack or chain. The sim tick, positions, hazards and timers keep running exactly as before.
    //   * Hit vignette. When your own character loses HP the screen edge flashes Ember Coral (a short white contact first),
    //     stronger for bigger hits; while you are down the screen greys a little (gpt/06 "Outcome feedback").
    //     Telegraph-safe by construction (10 Nov, review note): both overlays are drawn through a grid of screen cells
    //     (TintMask) and never touch a cell showing a live telegraph, a boss bullet or a hostile projectile (two rings
    //     of cells around them fade in as a feather), and the flash never goes above MaxFlashAlpha anywhere.
    // No invincibility of any kind: nothing here reads or writes gameplay state other than reading HP, Down and the boss.
    // LAN: BossDamage is recorded for clients (LanFx Hud "Damage") and replayed there; downs and the local player's hits are
    // read from the replica's own HP each frame, so a client gets the same feedback for its own character.
    // Sound lane hooks: BossDamaged, HitStopStarted, LocalPlayerHurt, PlayerDowned (C# events, presentation thread).
    public sealed class HitFeel : MonoBehaviour
    {
        public static event Action<Vector3, int, PlayerController, bool> BossDamaged;   // contact, amount, dealer (may be null), big
        public static event Action<float> HitStopStarted;                                // seconds
        public static event Action<PlayerController, int> LocalPlayerHurt;              // the local player, HP lost
        public static event Action<PlayerController> PlayerDowned;

        // ---------------- tuning (provisional) ----------------
        public const int BigHitMine = 45, BigHitAlly = 80;          // a big hit: Bellstrike / Sigil / Splitting Throw tier, Comet tier
        public const float StopMine = 0.05f, StopAlly = 0.035f, StopDown = 0.085f, MaxStop = 0.1f, Gap = 0.25f;
        const int PoolSize = 48;
        const float Life = 0.85f, Rise = 38f, MergeWindow = 0.3f, FlashLife = 0.42f;

        RunDirector d;
        void Awake() { d = GetComponent<RunDirector>(); }
        void Start() { Vignette(); }   // built before the fight, not on the first hit
        void OnDisable() { CharacterPresenter.GlobalAnimScale = 1; }

        // ---------------- damage numbers ----------------
        enum Kind : byte { Mine, Ally, Neutral }
        struct Num { public bool Live, Big; public Kind Kind; public Vector3 Pos; public int Value, Column; public float Age, Pop; public PlayerController By; }
        static readonly Num[] nums = new Num[PoolSize];
        public static int LiveNumbers { get { int n = 0; for (int i = 0; i < PoolSize; i++) if (nums[i].Live) n++; return n; } }
        public static int NumbersShown, Merged, Stops, Downs, Flashes;   // counters for tests and captures
        // autotest only: the old behaviour (a plain text float per hit, no hit-stop or vignette), for before/after checks
        public static bool LegacyLook;

        // Every hit the boss takes (BossEncounter.TakeHit). `by` is the dealing player (CombatWorld.Actor), null if unknown.
        public static void BossDamage(Vector3 at, int amount, PlayerController by)
        {
            if (LegacyLook) { RunHud.Float(at, amount.ToString(), new Color(1f, 0.86f, 0.45f)); return; }   // the pre-8 Nov float, for before/after
            long a0 = Measuring ? HitFeel.AllocNow() : 0;   // measured across the whole call, the LAN recording included
            LanFx.RecordDamage?.Invoke(at, amount, by);       // typed and pooled (LanFxCapture), so a hit allocates nothing
            if (amount <= 0) { if (Measuring) AllocBytes += Math.Max(0, HitFeel.AllocNow() - a0); return; }
            var dir = RunDirector.Instance; var local = dir ? dir.Player : null;
            var world = dir ? dir.World : null;
            Kind kind = by == null ? (world != null && world.Players.Count <= 1 ? Kind.Mine : Kind.Neutral) : by == local ? Kind.Mine : Kind.Ally;
            bool big = amount >= (kind == Kind.Ally ? BigHitAlly : BigHitMine);
            if (PresentationSettings.DamageNumbers) AddNumber(at, amount, by, kind, big, world);
            if (big && PresentationSettings.HitStop) Stop(kind == Kind.Ally ? StopAlly : StopMine);
            BossDamaged?.Invoke(at, amount, by, big);
            if (Measuring) AllocBytes += Math.Max(0, HitFeel.AllocNow() - a0);
        }

        static void AddNumber(Vector3 at, int amount, PlayerController by, Kind kind, bool big, CombatWorld world)
        {
            // the same dealer's fresh number takes the hit (a beam's ticks, a spin's hits read as one growing number)
            for (int i = 0; i < PoolSize; i++)
            {
                ref var n = ref nums[i];
                if (!n.Live || n.By != by || n.Kind != kind || n.Age > MergeWindow) continue;
                n.Value = Mathf.Min(n.Value + amount, 999999); n.Big |= big; n.Pop = 1; n.Age = Mathf.Min(n.Age, 0.12f); Merged++;
                return;
            }
            int slot = -1; float oldest = -1;
            for (int i = 0; i < PoolSize; i++)
            {
                if (!nums[i].Live) { slot = i; break; }
                // full: replace the oldest, allies' numbers first
                float score = nums[i].Age + (nums[i].Kind == Kind.Mine ? 0 : 10);
                if (score > oldest) { oldest = score; slot = i; }
            }
            int column = 0;
            if (kind == Kind.Ally && world != null) { int idx = world.Players.IndexOf(by); column = idx <= 0 ? 1 : (idx % 2 == 1 ? -(idx + 1) / 2 : idx / 2); }
            nums[slot] = new Num { Live = true, Big = big, Kind = kind, Pos = at, Value = Mathf.Min(amount, 999999), Column = column, Age = 0, Pop = big ? 1 : 0, By = by };
            NumbersShown++;
        }

        public static void ClearTransient()
        {
            for (int i = 0; i < PoolSize; i++) nums[i].Live = false;
            CharacterPresenter.GlobalAnimScale = 1; stopUntil = 0; nextStop = 0; flash = 0;
        }

        // ---------------- hit-stop ----------------
        static float stopUntil, nextStop;
        public static bool Stopped => CharacterPresenter.GlobalAnimScale == 0;
        public static float LastStopLength { get; private set; }
        public static void Stop(float seconds)
        {
            float now = Time.unscaledTime;
            if (now < nextStop) return;   // never stacks, never chains: one stop, then a gap
            seconds = Mathf.Clamp(seconds, 0, MaxStop);
            stopUntil = now + seconds; nextStop = stopUntil + Gap; LastStopLength = seconds;
            CharacterPresenter.GlobalAnimScale = 0; Stops++;
            HitStopStarted?.Invoke(seconds);
        }

        // ---------------- player feedback ----------------
        static float flash, flashAge; static bool flashWhite;
        public static float FlashLevel => flash;
        CombatWorld seenWorld; PlayerController seenLocal; int localHp = -1;
        readonly PlayerController[] seenPlayers = new PlayerController[8]; readonly bool[] seenDown = new bool[8];

        // allocation check (RunAutoTest.Presentation): bytes allocated inside this feature's per-frame and per-hit code
        public static bool Measuring; public static long AllocBytes; public static int MeasuredFrames;
        public static long AllocStyles, AllocFeedback, AllocNumbers; public static int LabelsDrawn;   // OnGUI split (windowed runs)
        // bytes allocated so far: the per-thread counter where the runtime reports it, else the managed heap in use (this Mono
        // player reports 0 per thread). A collection in between reads negative and is ignored by the callers.
        static int threadCounter = -1;
        public static long AllocNow()
        {
            if (threadCounter < 0) { long t0 = GC.GetAllocatedBytesForCurrentThread(); var probe = new byte[8192]; threadCounter = GC.GetAllocatedBytesForCurrentThread() - t0 >= 8192 && probe.Length > 0 ? 1 : 0; }
            return threadCounter == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalMemory(false);
        }

        void Update()
        {
            long a0 = Measuring ? HitFeel.AllocNow() : 0;
            float dt = Time.unscaledDeltaTime;
            if (Stopped && Time.unscaledTime >= stopUntil) CharacterPresenter.GlobalAnimScale = 1;
            for (int i = 0; i < PoolSize; i++)
            {
                ref var n = ref nums[i]; if (!n.Live) continue;
                n.Age += Stopped ? dt * 0.25f : dt;   // the number hangs during a stop, too
                n.Pop = Mathf.MoveTowards(n.Pop, 0, dt / 0.22f);
                if (n.Age >= Life) n.Live = false;
            }
            if (flash > 0) { flashAge += dt; flash = Mathf.MoveTowards(flash, 0, dt / FlashLife); }
            WatchPlayers();
            if (Measuring) { AllocBytes += Math.Max(0, HitFeel.AllocNow() - a0); MeasuredFrames++; }
        }

        void WatchPlayers()
        {
            var w = d ? d.World : null;
            if (w != seenWorld) { seenWorld = w; Array.Clear(seenPlayers, 0, seenPlayers.Length); localHp = -1; seenLocal = null; }
            if (w == null) return;
            for (int i = 0; i < w.Players.Count && i < seenPlayers.Length; i++)
            {
                var p = w.Players[i]; if (!p) continue;
                if (seenPlayers[i] != p) { seenPlayers[i] = p; seenDown[i] = p.Down; continue; }
                if (p.Down && !seenDown[i] && (d.State == RunState.Combat || d.State == RunState.Downed))
                {
                    Downs++;
                    if (PresentationSettings.HitStop) Stop(StopDown);
                    PlayerDowned?.Invoke(p);
                }
                seenDown[i] = p.Down;
            }
            var me = d.Player;
            if (me != seenLocal) { seenLocal = me; localHp = me ? me.Hp : -1; return; }
            if (!me) return;
            int lost = localHp - me.Hp; localHp = me.Hp;
            if (lost > 0 && (d.State == RunState.Combat || d.State == RunState.Downed))
            {
                Flashes++;
                if (PresentationSettings.HitFlash)
                {
                    float k = Mathf.Clamp01(0.45f + 2.2f * lost / Mathf.Max(1f, me.MaxHealth));
                    if (k >= flash) { flash = k; flashAge = 0; flashWhite = true; }
                }
                LocalPlayerHurt?.Invoke(me, lost);
            }
        }

        // ---------------- drawing ----------------
        static readonly string[] Digits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        // one GUIContent per digit, text set once: GUI.Label(Rect, string, style) refills a shared GUIContent each call, and in
        // Unity 6 setting its text builds a new string (about 100 bytes per label); passing our own content builds nothing
        static readonly GUIContent[] DigitContent = System.Array.ConvertAll(Digits, s => new GUIContent(s));
        GUIStyle stMine, stBig, stAlly; int styleH = -1;
        readonly float[] wMine = new float[10], wBig = new float[10], wAlly = new float[10];
        readonly int[] digitBuf = new int[8];
        static Texture2D vignette;
        static readonly Color Coral = new Color(0.92f, 0.41f, 0.36f), Contact = new Color(1f, 0.95f, 0.88f),
            NumIvory = new Color(0.96f, 0.92f, 0.82f), NumGold = new Color(0.965f, 0.79f, 0.41f), NumAlly = new Color(0.86f, 0.84f, 0.8f, 0.78f),
            DownInk = new Color(0.06f, 0.07f, 0.09f, 0.3f);

        void Styles()
        {
            if (styleH == Screen.height && stMine != null) return;
            styleH = Screen.height;
            stMine = UiTheme.Style(30, true, TextAnchor.MiddleLeft, Color.white);
            stBig = UiTheme.Style(40, true, TextAnchor.MiddleLeft, Color.white);
            stAlly = UiTheme.Style(21, true, TextAnchor.MiddleLeft, Color.white);
            for (int i = 0; i < 10; i++)
            {
                var gc = new GUIContent(Digits[i]);
                wMine[i] = stMine.CalcSize(gc).x; wBig[i] = stBig.CalcSize(gc).x; wAlly[i] = stAlly.CalcSize(gc).x;
            }
        }

        static Texture2D Vignette()
        {
            if (vignette) return vignette;
            const int N = 128;
            vignette = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                    float r = Mathf.Sqrt(u * u * 0.85f + v * v);   // a little wider than tall
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 1.15f, r));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            vignette.SetPixels32(px); vignette.Apply(false, true);
            return vignette;
        }

        void OnGUI()
        {
            if (d == null || d.World == null || Event.current.type != EventType.Repaint) return;
            long a0 = Measuring ? HitFeel.AllocNow() : 0;
            GUI.depth = 5;   // under RunHud's panels and cards (depth 0)
            Styles();
            long a1 = Measuring ? AllocNow() : 0;
            DrawPlayerFeedback();
            long a2 = Measuring ? AllocNow() : 0;
            DrawNumbers();
            if (Measuring) { long a3 = AllocNow(); AllocBytes += Math.Max(0, a3 - a0); AllocStyles += Math.Max(0, a1 - a0); AllocFeedback += Math.Max(0, a2 - a1); AllocNumbers += Math.Max(0, a3 - a2); }
        }

        void DrawPlayerFeedback()
        {
            var me = d.Player;
            bool down = me && me.Down && PresentationSettings.HitFlash, flashing = flash > 0.01f;
            if (!down && !flashing) return;
            var old = GUI.color;
            TintMask.Build(UnmaskedLook ? null : d.World, Camera.main, Screen.width, Screen.height);
            if (down) TintMask.Draw(Texture2D.whiteTexture, DownInk, false);
            if (flashing)
            {
                var c = flashWhite && flashAge < 0.06f ? Color.Lerp(Contact, Coral, flashAge / 0.06f) : Coral;
                if (flashAge >= 0.06f) flashWhite = false;
                c.a = (UnmaskedLook ? 0.75f : MaxFlashAlpha) * flash;
                TintMask.Draw(Vignette(), c, true);
            }
            GUI.color = old;
        }
        // autotest only: the 8 Nov look (no mask, peak 0.75) for before/after pictures; and a flash on demand for galleries
        public static bool UnmaskedLook;
        public static void TestFlash(float k) { flash = Mathf.Clamp01(k); flashAge = 0.1f; flashWhite = false; }
        public const float MaxFlashAlpha = 0.55f;   // the flash's peak opacity at the very edge (0.75 before 10 Nov)

        // The screen as a grid of cells. Build marks every cell that a live telegraph, a boss bullet or a hostile projectile
        // covers (a conservative screen box per shape); Draw paints a full-screen overlay row by row, one quad per run of
        // equal cells, and leaves every marked cell out. Nothing allocates.
        public static class TintMask
        {
            public const int Cols = 48, Rows = 27;
            static readonly byte[] cells = new byte[Cols * Rows];   // 0 clear, 1 outer feather (0.6), 2 inner feather (0.25), 3 blocked (nothing drawn)
            static float sw = 1, sh = 1;
            public static int Blocked { get; private set; }
            public static float MaxAlphaDrawn { get; set; }          // tests: the most any drawn cell was given (<= MaxFlashAlpha)
            public static int BlockedDraws { get; set; }             // tests: quads drawn over a blocked cell (must stay 0)
            public static byte Cell(int c, int r) => cells[r * Cols + c];
            public static bool BlockedAt(Vector2 gui)
            {
                int c = (int)(gui.x / sw * Cols), r = (int)(gui.y / sh * Rows);
                return c >= 0 && c < Cols && r >= 0 && r < Rows && cells[r * Cols + c] == Block;
            }

            public static void Build(CombatWorld w, Camera cam, float width, float height)
            {
                Array.Clear(cells, 0, cells.Length); Blocked = 0; sw = Mathf.Max(1, width); sh = Mathf.Max(1, height);
                if (w == null || !cam) return;
                var hz = w.Hazards.Live;
                for (int i = 0; i < hz.Count; i++)
                {
                    var h = hz[i];
                    // a tether is a chain between players, drawn by its ability, not a floor shape
                    if (h == null || h.Cancelled || h.Shape == HazardShape.Tether) continue;
                    // a lane: its length in every direction (conservative)
                    float r = h.Shape == HazardShape.Lane ? Mathf.Max(h.Length, h.Width) : h.Radius;
                    Box(cam, h.Origin, Mathf.Max(0.5f, r), 0);
                }
                var bl = w.Bullets.Live;
                for (int i = 0; i < bl.Count; i++) Box(cam, bl[i].Pos, bl[i].Radius + 0.35f, 1);
                var pr = w.Projectiles.Live;
                for (int i = 0; i < pr.Count; i++) if (pr[i].Owner == null) Box(cam, pr[i].Pos, pr[i].Radius + 0.35f, 1);
                // feather: two rings of cells around the blocked ones fade in (0.25, then 0.6)
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        if (cells[r * Cols + c] == 0 && NextTo(c, r, Block)) cells[r * Cols + c] = 2;
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        if (cells[r * Cols + c] == 0 && NextTo(c, r, 2)) cells[r * Cols + c] = 1;
            }
            const byte Block = 3;
            static bool NextTo(int c, int r, byte level)
            {
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr >= 0 && rr < Rows && cc >= 0 && cc < Cols && cells[rr * Cols + cc] == level) return true;
                    }
                return false;
            }

            // a floor shape of radius r around p, with height for bodies and bullets: the screen box of its eight corners
            static void Box(Camera cam, Vector3 p, float r, float lift)
            {
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    var q = cam.WorldToScreenPoint(new Vector3(p.x + ((k & 1) == 0 ? -r : r), p.y + ((k & 4) == 0 ? -0.2f : 1.2f + lift), p.z + ((k & 2) == 0 ? -r : r)));
                    // a shape reaching behind the camera (an arena-wide ring): it can cover any part of the screen, so all of it
                    if (q.z <= 0.01f) { x0 = y0 = -1; x1 = sw + 1; y1 = sh + 1; break; }
                    float gy = sh - q.y;
                    x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); y0 = Mathf.Min(y0, gy); y1 = Mathf.Max(y1, gy);
                }
                if (x1 < 0 || y1 < 0 || x0 > sw || y0 > sh) return;
                int c0 = Mathf.Max(0, (int)(x0 / sw * Cols)), c1 = Mathf.Min(Cols - 1, (int)(x1 / sw * Cols));
                int r0 = Mathf.Max(0, (int)(y0 / sh * Rows)), r1 = Mathf.Min(Rows - 1, (int)(y1 / sh * Rows));
                for (int r2 = r0; r2 <= r1; r2++)
                    for (int c = c0; c <= c1; c++)
                        if (cells[r2 * Cols + c] != Block) { cells[r2 * Cols + c] = Block; Blocked++; }
            }

            // one quad per run of equal cells in a row: the texture's matching part (vignette) or the plain tint
            public static void Draw(Texture tex, Color tint, bool mapTexture)
            {
                float cw = sw / Cols, ch = sh / Rows, full = tint.a;
                for (int r = 0; r < Rows; r++)
                {
                    int c = 0;
                    while (c < Cols)
                    {
                        byte v = cells[r * Cols + c]; int e = c + 1;
                        while (e < Cols && cells[r * Cols + e] == v) e++;
                        if (v == Block) { c = e; continue; }
                        float a = v == 0 ? full : v == 1 ? full * 0.6f : full * 0.25f;
                        if (!UnmaskedLook && a > MaxAlphaDrawn) MaxAlphaDrawn = a;
                        for (int k = c; k < e; k++) if (cells[r * Cols + k] == Block) BlockedDraws++;
                        tint.a = a; GUI.color = tint;
                        // whole-pixel edges, so neighbouring quads meet exactly (no hairline seams)
                        float x0 = Mathf.Round(c * cw), y0 = Mathf.Round(r * ch);
                        var rect = new Rect(x0, y0, Mathf.Round(e * cw) - x0, Mathf.Round((r + 1) * ch) - y0);
                        if (mapTexture) GUI.DrawTextureWithTexCoords(rect, tex, new Rect(rect.x / sw, 1f - rect.yMax / sh, rect.width / sw, rect.height / sh), true);
                        else GUI.DrawTexture(rect, tex);
                        c = e;
                    }
                }
            }
        }

        void DrawNumbers()
        {
            var cam = Camera.main; if (!cam) return;
            float h = Screen.height;
            // allies' numbers first, so yours draw on top
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < PoolSize; i++)
                {
                    ref var n = ref nums[i];
                    if (!n.Live || (n.Kind == Kind.Mine) != (pass == 1)) continue;
                    var s = cam.WorldToScreenPoint(n.Pos); if (s.z < 0) continue;
                    var st = n.Kind == Kind.Ally ? stAlly : n.Big ? stBig : stMine;
                    var widths = n.Kind == Kind.Ally ? wAlly : n.Big ? wBig : wMine;
                    var col = n.Kind == Kind.Ally ? NumAlly : n.Big ? NumGold : n.Kind == Kind.Neutral ? new Color(NumIvory.r, NumIvory.g, NumIvory.b, 0.8f) : NumIvory;
                    float t = Mathf.Clamp01(n.Age / Life), ease = 1 - (1 - t) * (1 - t);
                    col.a *= 1 - Mathf.Clamp01((n.Age - (Life - 0.3f)) / 0.3f);
                    float x = s.x + UiTheme.V(n.Column * 44f), y = h - s.y - UiTheme.V(Rise * ease + 10f * n.Pop + 18f);
                    DrawValue(x, y, n.Value, st, widths, col);
                }
        }

        // UiTheme.Label for a cached GUIContent: a 1 px shadow, white text tinted through GUI.color, alpha in 8 steps (the same
        // text-cache rules as the rest of the HUD)
        static void Digit(Rect r, GUIContent content, GUIStyle style, Color c)
        {
            var keep = style.normal.textColor; float o = Mathf.Max(1, Mathf.Round(UiTheme.Scale));
            var oldGui = GUI.color; style.normal.textColor = Color.white;
            float a = Mathf.Ceil(c.a * oldGui.a * 8f) / 8f;
            GUI.color = new Color(0, 0, 0, 0.85f * a); GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), content, style);
            var tint = oldGui * c; tint.a = a; GUI.color = tint; GUI.Label(r, content, style);
            style.normal.textColor = keep; GUI.color = oldGui;
        }

        // the value centred on x, one cached digit text at a time
        // The digits as quads from the font's own glyph atlas (the font material's text shader), with the same 1 px shadow and
        // 8-step alpha as UiTheme.Label. In Unity 6 every IMGUI text draw allocates (about 100 bytes a label, even with a cached
        // GUIContent), and a fight shows thousands of digits, so the numbers skip the text system. False (then the labels draw
        // instead) if the font isn't dynamic or a glyph is packed rotated.
        const string DigitChars = "0123456789";
        readonly CharacterInfo[] glyph = new CharacterInfo[10];
        public static int GlyphDraws;   // tests: the glyph path is the one in use
        bool Glyphs(float cx, float cy, int count, GUIStyle st, Color c)
        {
            var font = st.font; if (!font || !font.dynamic || !font.material) return false;
            int size = st.fontSize;
            font.RequestCharactersInTexture(DigitChars, size, FontStyle.Normal);   // keeps them in the atlas; a rebuild moves UVs, so read them each draw
            var tex = font.material.mainTexture; if (!tex) return false;
            float total = 0;
            for (int i = 0; i < 10; i++)
            {
                if (!font.GetCharacterInfo(DigitChars[i], out glyph[i], size, FontStyle.Normal)) return false;
                if (glyph[i].uvTopLeft.x != glyph[i].uvBottomLeft.x) return false;   // rotated in the atlas
            }
            for (int i = 0; i < count; i++) total += glyph[digitBuf[i]].advance;
            float o = Mathf.Max(1, Mathf.Round(UiTheme.Scale)), a = Mathf.Ceil(c.a * GUI.color.a * 8f) / 8f;
            float baseline = cy + size * 0.36f;   // the digits' optical centre on cy
            var tint = c; tint.a = a; var shadow = new Color(0.04f, 0.05f, 0.09f, 0.9f * a);   // a Deep Ink outline: readable on bright floors
            for (int pass = 0; pass < 5; pass++)
            {
                float x = cx - total / 2, ox = pass == 4 ? 0 : (pass & 1) == 0 ? -o : o, oy = pass == 4 ? 0 : pass < 2 ? -o : o;
                for (int i = count - 1; i >= 0; i--)
                {
                    var g = glyph[digitBuf[i]];
                    var r = new Rect(x + g.minX + ox, baseline - g.maxY + oy, g.glyphWidth, g.glyphHeight);
                    var uv = new Rect(g.uvBottomLeft.x, g.uvBottomLeft.y, g.uvTopRight.x - g.uvBottomLeft.x, g.uvTopRight.y - g.uvBottomLeft.y);
                    Graphics.DrawTexture(r, tex, uv, 0, 0, 0, 0, pass == 4 ? tint : shadow, font.material);
                    x += g.advance;
                }
            }
            GlyphDraws++; LabelsDrawn += count;
            return true;
        }

        void DrawValue(float cx, float cy, int value, GUIStyle st, float[] widths, Color c)
        {
            int count = 0;
            do { digitBuf[count++] = value % 10; value /= 10; } while (value > 0 && count < digitBuf.Length);
            if (Glyphs(cx, cy, count, st, c)) return;
            float total = 0; for (int i = 0; i < count; i++) total += widths[digitBuf[i]];
            float x = cx - total / 2, hh = st.fontSize * 1.4f;
            for (int i = count - 1; i >= 0; i--)
            {
                int dg = digitBuf[i];
                Digit(new Rect(x, cy - hh / 2, widths[dg] + 2, hh), DigitContent[dg], st, c); LabelsDrawn++;
                x += widths[dg];
            }
        }
    }
}
