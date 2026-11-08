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
            if (LanFx.Record != null) LanFx.Record(LanFxTarget.Hud, "Damage", new object[] { at, amount, by });
            if (amount <= 0) return;
            long a0 = Measuring ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var dir = RunDirector.Instance; var local = dir ? dir.Player : null;
            var world = dir ? dir.World : null;
            Kind kind = by == null ? (world != null && world.Players.Count <= 1 ? Kind.Mine : Kind.Neutral) : by == local ? Kind.Mine : Kind.Ally;
            bool big = amount >= (kind == Kind.Ally ? BigHitAlly : BigHitMine);
            if (PresentationSettings.DamageNumbers) AddNumber(at, amount, by, kind, big, world);
            if (big && PresentationSettings.HitStop) Stop(kind == Kind.Ally ? StopAlly : StopMine);
            BossDamaged?.Invoke(at, amount, by, big);
            if (Measuring) AllocBytes += GC.GetAllocatedBytesForCurrentThread() - a0;
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
        CombatWorld seenWorld; PlayerController seenLocal; int localHp = -1;
        readonly PlayerController[] seenPlayers = new PlayerController[8]; readonly bool[] seenDown = new bool[8];

        // allocation check (RunAutoTest.Presentation): bytes allocated inside this feature's per-frame and per-hit code
        public static bool Measuring; public static long AllocBytes; public static int MeasuredFrames;

        void Update()
        {
            long a0 = Measuring ? GC.GetAllocatedBytesForCurrentThread() : 0;
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
            if (Measuring) { AllocBytes += GC.GetAllocatedBytesForCurrentThread() - a0; MeasuredFrames++; }
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
        GUIStyle stMine, stBig, stAlly; int styleH = -1;
        readonly float[] wMine = new float[10], wBig = new float[10], wAlly = new float[10];
        readonly int[] digitBuf = new int[8];
        static Texture2D vignette;
        static readonly Color Coral = new Color(0.92f, 0.41f, 0.36f), Contact = new Color(1f, 0.95f, 0.88f),
            NumIvory = new Color(0.96f, 0.92f, 0.82f), NumGold = new Color(0.965f, 0.79f, 0.41f), NumAlly = new Color(0.86f, 0.84f, 0.8f, 0.62f),
            DownInk = new Color(0.06f, 0.07f, 0.09f, 0.3f);

        void Styles()
        {
            if (styleH == Screen.height && stMine != null) return;
            styleH = Screen.height;
            stMine = UiTheme.Style(24, true, TextAnchor.MiddleLeft, Color.white);
            stBig = UiTheme.Style(32, true, TextAnchor.MiddleLeft, Color.white);
            stAlly = UiTheme.Style(17, true, TextAnchor.MiddleLeft, Color.white);
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
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.62f, 1.25f, r));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            vignette.SetPixels32(px); vignette.Apply(false, true);
            return vignette;
        }

        void OnGUI()
        {
            if (d == null || d.World == null || Event.current.type != EventType.Repaint) return;
            long a0 = Measuring ? GC.GetAllocatedBytesForCurrentThread() : 0;
            GUI.depth = 5;   // under RunHud's panels and cards (depth 0)
            Styles();
            DrawPlayerFeedback();
            DrawNumbers();
            if (Measuring) AllocBytes += GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        void DrawPlayerFeedback()
        {
            var me = d.Player; var full = new Rect(0, 0, Screen.width, Screen.height);
            var old = GUI.color;
            if (me && me.Down && PresentationSettings.HitFlash) { GUI.color = DownInk; GUI.DrawTexture(full, Texture2D.whiteTexture); }
            if (flash > 0.01f)
            {
                var c = flashWhite && flashAge < 0.06f ? Color.Lerp(Contact, Coral, flashAge / 0.06f) : Coral;
                if (flashAge >= 0.06f) flashWhite = false;
                c.a = 0.6f * flash; GUI.color = c;
                GUI.DrawTexture(full, Vignette(), ScaleMode.StretchToFill, true);
            }
            GUI.color = old;
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
                    float x = s.x + UiTheme.V(n.Column * 34f), y = h - s.y - UiTheme.V(Rise * ease + 10f * n.Pop + 18f);
                    DrawValue(x, y, n.Value, st, widths, col);
                }
        }

        // the value centred on x, one cached digit text at a time
        void DrawValue(float cx, float cy, int value, GUIStyle st, float[] widths, Color c)
        {
            int count = 0;
            do { digitBuf[count++] = value % 10; value /= 10; } while (value > 0 && count < digitBuf.Length);
            float total = 0; for (int i = 0; i < count; i++) total += widths[digitBuf[i]];
            float x = cx - total / 2, hh = st.fontSize * 1.4f;
            for (int i = count - 1; i >= 0; i--)
            {
                int dg = digitBuf[i];
                UiTheme.Label(new Rect(x, cy - hh / 2, widths[dg] + 2, hh), Digits[dg], st, c);
                x += widths[dg];
            }
        }
    }
}
