using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Pendulum Sentinel: a practice-only Skyward Spire miniboss (docs/bosses/pendulum-sentinel.md, a proposal).
    // Four mechanics, no filler:
    //
    //   Shortening Swing      he lunges at his target, then four swings, each faster and narrower (180, 150, 120, 90
    //                         degrees), each re-aimed at the target. Swing 1 gets only as long as the worst-placed
    //                         player needs to walk out. Below 50% swings 2 and 4 turn to the farthest player. Hard:
    //                         Backswing (swings 3 and 4 also hit the arc behind him a beat later).
    //   Counterweight Balance three weights thrown along his arms. Each is a soak: anyone under it when it lands, or
    //                         who steps into it while it lies on the floor (3 beats), absorbs it: 75 damage shared by
    //                         everyone inside. An unabsorbed weight bursts into 9 orbs that bounce off the floor's
    //                         edge 3-4 times before they vanish. Solo: one by one.
    //   Plumb Line            the pendulum stops; everything outside four tiny level marks is hit after 4 beats
    //                         (5 below 50%). A mark holds 2; a third cracks it. Below 50% the marks drift first.
    //   Return Stroke         every other attack. A lane from him to the floor's edge. He charges and slams the edge, then cleaves the
    //                         half-circle behind him. Below 50%: three strokes back to back, each lane appearing as he
    //                         lands. Then stuck for 3.5 s (the major punish window).
    //
    // Harder pass (Risxhb, 27 Oct 2026): every warning is as short as the content-rules escape check (4.2 and 6 m/s after
    // a 0.3 s reaction) allows from every start position; the acceptance test proves it.
    //
    // 50%: he finishes the current attack, then a 2 s harmless transformation (he stays damageable, no heal); 90 -> 112
    // BPM. No enrage (content rules §5). No ability text reaches the player: PhaseName and Lesson stay empty, names live
    // only in the F7 diagnostics.
    public sealed class PendulumSentinelEncounter : BossEncounter
    {
        public override string Id => "boss.pendulum-sentinel";
        public override string DisplayName => "The Pendulum Sentinel";
        public override string Lesson => "";
        public override bool Placeholder => !presenter || !presenter.HasModel;
        public override float NextPhaseFraction() => 0.49f;

        // ---------------- tunables (Medium; harder pass of 27 Oct) ----------------
        public const float BpmHigh = 90f, BpmLow = 112f;
        public const int SwingDmg = 55, WeightDmg = 75, OrbDmg = 25, PlumbDmg = 90, StrokeDmg = 75, CleaveDmg = 65;
        public const int Orbs = 9, Capacity = 2;
        public const float OrbSpeed = 7f, OrbRadius = 0.5f, WeightTurn = 40f, DriftPerBeat = 15f, ConeReach = 30f;
        public const int PlumbBeatsHigh = 4, PlumbBeatsLow = 5, DriftBeats = 3, StuckTicks = 210, ShiftTicks = 120;
        public const int ThrowsHigh = 3, ThrowsLow = 3, StrokesLow = 3;
        public const float WalkSpeed = 3.0f, LungeSpeed = 22f, GapBeats = 1f;
        public const float MarkRadiusMedium = 1.2f, MarkRadiusEasy = 1.8f;
        float WeightWarnBeats => PendulumSentinelRules.WeightWarnBeats(Level, Phase2);
        float SoakBeats => PendulumSentinelRules.SoakBeats(Level);
        float MarkRadius => Easy ? MarkRadiusEasy : MarkRadiusMedium;

        // palette (HDR): strikes coral, ground eruptions amber, the level marks teal (safe), weights brass-amber
        public static readonly Color StrikeFill = new Color(1.3f, 0.32f, 0.16f), StrikeBorder = new Color(2.4f, 0.8f, 0.3f),
            GroundFill = new Color(1.3f, 0.6f, 0.12f), GroundBorder = new Color(2.6f, 1.35f, 0.35f),
            HatchFill = new Color(0.55f, 0.16f, 0.1f), HatchBorder = new Color(1.6f, 0.5f, 0.25f),
            MarkFill = new Color(0.25f, 1.35f, 1.0f), MarkBorder = new Color(0.8f, 3.2f, 2.4f),
            WeightFill = new Color(1.25f, 0.75f, 0.2f), WeightBorder = new Color(2.6f, 1.7f, 0.5f),
            ArrowFill = new Color(1.6f, 0.95f, 0.25f), ArrowBorder = new Color(2.6f, 2.0f, 1.2f),
            OrbCore = new Color(1.0f, 0.8f, 0.45f), PlumbGold = new Color(2.6f, 2.0f, 0.8f);

        public PendulumSentinelAudio Audio { get; private set; }
        CharacterPresenter presenter; public CharacterPresenter Presenter => presenter;

        // ---------------- state (read by tests and the F7 overlay) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;                 // dev/test hook: replaces the next rotation entry once
        public int Seed = Environment.TickCount;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public float Bpm => Phase2 ? BpmLow : BpmHigh;
        public int BeatTicks => Mathf.RoundToInt(60f * Sim.Hz / Bpm);
        public int Tick => W != null ? W.Tick : 0;
        public int BeatIndex { get; private set; }
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }

        // hit counters and logs
        public int SwingHits, BackswingHits, WeightCatches, WeightMisses, OrbHits, PlumbHits, PlumbCracks, StrokeHits, CleaveHits;
        public int OrbsFired { get; private set; }
        public int OrbCount => orbs.Count;
        public readonly List<(int tick, int swing, float yaw, float arc, Vector3 aimedAt, int warn)> SwingLog = new List<(int, int, float, float, Vector3, int)>();
        public readonly List<(int tick, Vector3 at, int catchers, int share, bool late)> WeightLog = new List<(int, Vector3, int, int, bool)>();
        public readonly List<(int tick, int delay)> WeightSpawnLog = new List<(int, int)>();
        public readonly List<(int tick, List<Vector3> marks, float radius)> PlumbLog = new List<(int, List<Vector3>, float)>();
        public readonly List<(int tick, Vector3 from, Vector3 end, float yaw, float length)> StrokeLog = new List<(int, Vector3, Vector3, float, float)>();
        public readonly List<(int start, int end)> StuckLog = new List<(int, int)>();
        public List<Vector3> LiveMarks { get; private set; }
        public bool Stuck { get; private set; }
        public int PendingWeights { get; private set; }   // thrown and not yet absorbed or burst
        public int PendingLandings { get; private set; }  // thrown and still in the air
        public int LyingCount => soaks.Count;
        public List<Vector3> LyingSpots => soaks.ConvertAll(x => x.At);
        public readonly List<(int land, int end, bool absorbed)> SoakLog = new List<(int, int, bool)>();
        public int OrbBounces { get; private set; }
        public float OrbFarthest { get; private set; }    // the farthest any orb got from the centre
        public readonly List<(int bounces, int max, bool popped)> OrbEnds = new List<(int, int, bool)>();

        enum Atk { Swing, Stroke, Weights, Plumb, SwingWeights }
        // Return Stroke every other attack (27 Oct: doubled from once per rotation)
        static readonly Atk[] RotationHigh = { Atk.Swing, Atk.Stroke, Atk.Weights, Atk.Stroke, Atk.Plumb, Atk.Stroke };
        static readonly Atk[] RotationLow = { Atk.Plumb, Atk.Stroke, Atk.SwingWeights, Atk.Stroke, Atk.Swing, Atk.Stroke };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());
        int rotHigh, rotLow, beatAge;
        System.Random rng;
        string loopClip = "Idle";
        float? faceYaw;
        readonly List<(int at, Action act)> scheduled = new List<(int, Action)>();

        void Awake() { MaxHp = 3000; BodyRadius = 1.3f; HitHeight = 2.4f; MarkHeight = 5.0f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "PendulumSentinel", 1.5f, 1f, 0, host => { BuildStandIn(host); return host; });
            Audio = gameObject.AddComponent<PendulumSentinelAudio>();
            gameObject.AddComponent<PendulumSentinelHud>().Bind(this);
            PhaseName = "";
            PendulumSentinelRules.Bound = CombatWorld.PlayerBound;
        }

        // a labelled code-built stand-in, used only when Resources/Characters/PendulumSentinel is missing
        static void BuildStandIn(Transform host)
        {
            var tower = Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 2.8f, 0), new Vector3(1.2f, 2.2f, 1.2f), new Color(0.92f, 0.88f, 0.78f));
            tower.name = "Pendulum Sentinel (STAND-IN, labelled placeholder)";
            Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 2.2f, 0.62f), Vector3.one * 0.5f, new Color(0.85f, 0.66f, 0.32f));
            foreach (float a in new[] { -60f, 60f, 180f })
                Prim.Make(PrimitiveKind.Cylinder, host, Sim.Dir(a) * 0.8f + Vector3.up * 0.8f, new Vector3(0.18f, 0.8f, 0.18f), new Color(0.2f, 0.3f, 0.65f));
        }

        int Beats(float b) => Mathf.Max(1, Mathf.RoundToInt(b * BeatTicks));
        void Schedule(int ticks, Action a) => scheduled.Add((Age + Mathf.Max(1, ticks), a));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        void Clip(string name, int ticks) { if (presenter) presenter.Action(name, Mathf.Max(1, ticks)); }
        bool Hard => Level == Difficulty.Hard; bool Easy => Level == Difficulty.Easy;
        int Standing { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
        static Vector3 Centre => Vector3.zero;

        Hazard Live(Hazard h, Color fill, Color border, float flash = 0.35f)
        {
            W.Hazards.Spawn(h);
            h.Tele.Tint(fill, border); h.Tele.FlashScale = flash; h.Tele.SpentFast = true;
            return h;
        }
        bool IsLive(Hazard h) { if (h == null) return false; var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }
        static Hazard Blow(string source, Vector3 at, int dmg, HazardKind kind = HazardKind.Ground) =>
            new Hazard { Source = source, Shape = HazardShape.Circle, Kind = kind, Origin = at, Radius = 999, Damage = dmg, Knockback = 0, Stun = 6 };

        PlayerController Farthest()
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players) { if (p.Down) continue; float d = Sim.Flat(p.transform.position - transform.position).magnitude; if (d > bd) { bd = d; best = p; } }
            return best;
        }
        Vector3 AimAt(bool farthest)
        {
            var p = farthest ? Farthest() : W.NearestStanding(transform.position);
            return p ? p.transform.position : transform.position + transform.forward;
        }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            foreach (var w in ToCentre()) yield return w;
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in Transition()) yield return w;

                Blocks++;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> body = atk switch
                {
                    Atk.Swing => Swing(false),
                    Atk.SwingWeights => Swing(true),
                    Atk.Stroke => Stroke(Phase2 ? StrokesLow : 1),
                    Atk.Weights => Weights(Phase2 ? ThrowsLow : ThrowsHigh),
                    _ => Plumb(Phase2),
                };
                foreach (var w in body) yield return w;
                Current = "between blocks"; loopClip = "Idle"; faceYaw = null;
                yield return Beats(GapBeats);
            }
        }

        protected override void StepAlways()
        {
            if (++beatAge >= BeatTicks) { beatAge = 0; BeatIndex++; Audio.Tick(); }
            for (int i = scheduled.Count - 1; i >= 0; i--)
                if (Age >= scheduled[i].at) { var a = scheduled[i].act; scheduled.RemoveAt(i); a(); }
            StepOrbs();
            StepSoaks();
            StepFalling();
            if (faceYaw.HasValue) TurnTowards(faceYaw.Value, 540);
            if (presenter) presenter.TickBoss(Current == "approach" || Current == "reposition" ? "Walk" : loopClip);
            // the 50% gate is noticed during an attack too, but the shift waits for the attack to finish
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
        }

        IEnumerable<int> ToCentre() { if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Centre, WalkSpeed * 1.4f, 300)) yield return w; }

        // ================= 1. Shortening Swing =================
        public int SwingCasts { get; private set; }
        public int[] LastSwingWarns { get; private set; }
        public int[] LastSwingClips { get; private set; }

        public int LungeCount { get; private set; }
        public float LastLungeGap { get; private set; }     // distance to the target when the lunge ended
        public float LastFirstBeats { get; private set; }

        IEnumerable<int> Swing(bool withWeights)
        {
            Current = withWeights ? "Shortening Swing + Counterweight (lunge)" : "Shortening Swing (lunge)"; SwingCasts++;
            // the lunge: he lurches at his target and stops 3 m short (inside the boss bound); it deals no damage
            var target = AimAt(false);
            var dest = PendulumSentinelRules.LungeEnd(transform.position, target);
            float dist = Sim.Flat(dest - transform.position).magnitude;
            if (dist > 0.5f)
            {
                int ticks = Mathf.Max(6, Mathf.CeilToInt(dist / LungeSpeed * Sim.Hz)); var from = Sim.Flat(transform.position);
                faceYaw = Sim.YawTo(from, dest, transform.eulerAngles.y); Clip("Charge", ticks); Audio.Rush(); LungeCount++;
                for (int t = 1; t <= ticks; t++) { transform.position = Vector3.Lerp(from, dest, t / (float)ticks); yield return 1; }
            }
            LastLungeGap = Sim.Flat(AimAt(false) - transform.position).magnitude;
            Current = withWeights ? "Shortening Swing + Counterweight" : "Shortening Swing";
            var players = new List<Vector3>(); foreach (var p in W.Players) if (!p.Down) players.Add(p.transform.position);
            float yaw1 = Sim.YawTo(transform.position, AimAt(false), transform.eulerAngles.y);
            LastFirstBeats = PendulumSentinelRules.FirstSwingBeats(transform.position, yaw1, players, Bpm, Level);
            var warns = PendulumSentinelRules.ToTicks(PendulumSentinelRules.SwingWarnBeats(Level, Phase2, LastFirstBeats), BeatTicks);
            var clips = PendulumSentinelRules.SwingClipTicks(warns);
            LastSwingWarns = warns; LastSwingClips = clips;
            // the pendulum is pulled to one side, then each swing clip passes the middle exactly on its arc's hit
            Audio.Ratchet();
            int firstClip = warns[0] - clips[0] / 2;
            Clip("SwingWind", firstClip);
            int at = 0;
            for (int k = 0; k < warns.Length; k++)
            {
                int clipStart = at + warns[k] - clips[k] / 2, idx = k;
                Schedule(clipStart, () => Clip(idx % 2 == 0 ? "SwingLeft" : "SwingRight", clips[idx]));
                at += warns[k];
            }
            for (int k = 0; k < warns.Length; k++)
            {
                bool far = Phase2 && (k == 1 || k == 3);
                var aim = AimAt(far);
                float yaw = Sim.YawTo(transform.position, aim, transform.eulerAngles.y), arc = PendulumSentinelRules.Arcs[k];
                faceYaw = yaw;
                SwingCone($"Shortening Swing {k + 1}", yaw, arc, warns[k], false);
                if (Hard && k >= 2) SwingCone($"Backswing {k + 1}", yaw + 180f, arc, warns[k] + Beats(1), true);
                SwingLog.Add((Tick, k + 1, yaw, arc, aim, warns[k]));
                // below 50%: one set of weights is thrown with swing 2's warning, landing during swing 3 and lying as a soak through swing 4
                if (withWeights && k == 1) ThrowSet(0f, Beats(WeightWarnBeats) + 1);
                for (int t = 0; t < warns[k]; t++) { Windup((t + 1) / (float)warns[k]); yield return 1; }
            }
            if (Hard) yield return Beats(1);
            Current = withWeights ? "Shortening Swing + Counterweight (weights)" : Current;
            while (PendingWeights > 0 || orbs.Count > 0) yield return 1;
            faceYaw = null; Current = "Shortening Swing (recovery)";
            yield return Beats(0.5f);
        }

        void SwingCone(string source, float yaw, float arc, int resolve, bool back)
        {
            var h = new Hazard
            {
                Source = source, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = transform.position, Yaw = yaw, Radius = ConeReach,
                HalfAngle = arc / 2, LockAt = 0, ResolveAt = resolve, Damage = SwingDmg, Knockback = 0, Stun = 6, ClipRadius = CombatWorld.PlayerBound + 0.6f,
            };
            h.OnResolve = hz => { if (back) BackswingHits += hz.Hit.Count; else SwingHits += hz.Hit.Count; Audio.Swing(); CameraShake.Shake(0.08f, 0.12f); Release(0.6f); };
            Live(h, StrikeFill, StrikeBorder, 0.4f);
        }

        // ================= 2. Counterweight Balance =================
        public int WeightCasts { get; private set; }

        IEnumerable<int> Weights(int throws)
        {
            if (Sim.Flat(transform.position).magnitude > 4f) foreach (var w in WalkTo(Sim.Flat(transform.position).normalized * 3f, WalkSpeed * 1.4f, 240)) yield return w;
            Current = "Counterweight Balance"; WeightCasts++;
            faceYaw = Sim.YawTo(transform.position, AimAt(false), transform.eulerAngles.y);
            Clip("WeightSpin", Beats(1)); Audio.Whirl();
            yield return Beats(1);
            for (int i = 0; i < throws; i++)
            {
                float turn = Phase2 ? i * WeightTurn : 0f;
                ThrowSet(turn, Beats(WeightWarnBeats) + 1);
                for (int t = 0; t < Beats(WeightWarnBeats); t++) { Windup(t / (float)Beats(WeightWarnBeats)); yield return 1; }
                while (PendingLandings > 0) yield return 1;   // lying soaks from this throw stay while the next one flies
                yield return Beats(0.5f);
            }
            while (PendingWeights > 0 || orbs.Count > 0) yield return 1;
            faceYaw = null; Current = "Counterweight Balance (recovery)";
        }

        // one set of three weights along his arms (front right, front left, back); landing times follow the party size
        void ThrowSet(float turn, int warn)
        {
            int standing = Standing;
            var spots = PendulumSentinelRules.WeightSpots(transform.position, transform.eulerAngles.y + turn, standing <= 1);
            var delays = PendulumSentinelRules.WeightDelays(standing, warn, BeatTicks);
            Clip("WeightThrow", Beats(1.5f));
            var hand = transform.position + Vector3.up * 2.4f;
            for (int i = 0; i < spots.Count; i++) Weight(spots[i], delays[i], hand);
        }

        void Weight(Vector3 at, int delay, Vector3 hand)
        {
            PendingWeights++; PendingLandings++; WeightSpawnLog.Add((Tick, delay));
            float angle0 = (float)(rng.NextDouble() * 360.0);
            int bounces = rng.Next(PendulumSentinelRules.BouncesMin, PendulumSentinelRules.BouncesMax + 1);
            var h = new Hazard { Source = "Counterweight", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = PendulumSentinelRules.WeightRadius,
                LockAt = 0, ResolveAt = delay, Damage = 0, HitOnResolve = false, Knockback = 0, Stun = 0 };
            var arrows = Arrows(at, PendulumSentinelRules.WeightRadius, Orbs, angle0);
            int fallTicks = Mathf.Min(delay - 1, BeatTicks);
            Fall(hand, at, Age + delay - fallTicks, Age + delay);
            h.OnResolve = hz =>
            {
                PendingLandings--;
                Audio.Thud(); CameraShake.Shake(0.12f, 0.18f);
                var inside = Inside(hz.Origin);
                if (inside.Count > 0) { Absorb(hz.Origin, inside, false); foreach (var a in arrows) a.Dismiss(); PendingWeights--; return; }
                // nobody under it: it lies on the floor as a soak, counting down, until someone steps in or it bursts
                var ring = W.BossFx.Circle(PendulumSentinelRules.WeightRadius); ring.Tint(WeightFill, WeightBorder); ring.Lock(); ring.Place(hz.Origin, 0);
                var view = Prim.Make(PrimitiveKind.Sphere, transform.parent, hz.Origin + Vector3.up * 0.45f, Vector3.one * 0.9f, new Color(0.85f, 0.66f, 0.32f), 0.8f);
                view.name = "Counterweight (lying)";
                soaks.Add(new Soak { At = Sim.Flat(hz.Origin), Start = Age, Expire = Age + Beats(SoakBeats), Angle0 = angle0, Bounces = bounces, Ring = ring, Arrows = arrows, View = view });
            };
            Live(h, WeightFill, WeightBorder, 0.4f);
        }

        List<PlayerController> Inside(Vector3 at)
        {
            var list = new List<PlayerController>();
            foreach (var p in W.Players) if (!p.Down && Sim.Flat(p.transform.position - at).magnitude <= PendulumSentinelRules.WeightRadius + Hazard.Margin) list.Add(p);
            return list;
        }

        // absorbed: the weight's damage is shared equally by everyone inside
        void Absorb(Vector3 at, List<PlayerController> who, bool late)
        {
            int share = PendulumSentinelRules.Share(WeightDmg, who.Count);
            var blow = Blow("Counterweight (caught)", at, share);
            foreach (var p in who) { blow.Hit.Add(p); p.ReceiveHit(blow); }
            WeightCatches++; WeightLog.Add((Tick, at, who.Count, share, late)); Audio.Catch();
            if (late) CameraShake.Shake(0.1f, 0.14f);
        }

        // ---------------- lying soaks ----------------
        sealed class Soak { public Vector3 At; public int Start, Expire, Bounces; public float Angle0; public BellkeeperFx.Telegraph Ring; public List<BellkeeperFx.Telegraph> Arrows; public Transform View; }
        readonly List<Soak> soaks = new List<Soak>();

        void StepSoaks()
        {
            if (W == null) return;
            for (int i = soaks.Count - 1; i >= 0; i--)
            {
                var s = soaks[i];
                var inside = Inside(s.At);
                if (inside.Count > 0) Absorb(s.At, inside, true);
                else if (Age >= s.Expire)
                {
                    // not absorbed in time: it bursts into orbs that bounce round the floor
                    WeightMisses++; WeightLog.Add((Tick, s.At, 0, 0, false)); Audio.Shatter(); CameraShake.Shake(0.1f, 0.15f);
                    FireRing(s.At, 0.8f, Orbs, OrbSpeed, s.Angle0, "Counterweight orb", s.Bounces);
                }
                else { s.Ring.Progress((Age - s.Start) / (float)Mathf.Max(1, s.Expire - s.Start)); continue; }
                SoakLog.Add((s.Start, Age, inside.Count > 0)); EndSoak(s); soaks.RemoveAt(i); PendingWeights--;
            }
        }
        void EndSoak(Soak s)
        {
            if (s.Ring != null) s.Ring.Dismiss();
            if (s.Arrows != null) foreach (var a in s.Arrows) a.Dismiss();
            if (s.View) Destroy(s.View.gameObject);
        }
        void ClearSoaks() { foreach (var s in soaks) EndSoak(s); soaks.Clear(); }

        // one short chevron lane per orb inside a weight's circle: the floor shows where each orb would fly if missed
        List<BellkeeperFx.Telegraph> Arrows(Vector3 at, float radius, int count, float angle0)
        {
            var list = new List<BellkeeperFx.Telegraph>();
            float len = Mathf.Max(0.3f, radius - 1.0f);
            for (int i = 0; i < count; i++)
            {
                float yaw = angle0 + i * 360f / count;
                var t = W.BossFx.Lane(len, 0.3f); t.Tint(ArrowFill * 0.6f, ArrowBorder * 0.7f); t.Lock();
                t.Place(at + Sim.Dir(yaw) * (radius - 0.1f), yaw + 180f);
                list.Add(t);
            }
            return list;
        }

        // ---------------- orbs ----------------
        sealed class Orb { public Vector3 Pos, Vel; public Hazard Src; public Transform View; public int Bounces, MaxBounces; }
        readonly List<Orb> orbs = new List<Orb>();
        readonly Stack<Transform> orbPool = new Stack<Transform>();

        void FireRing(Vector3 c, float rim, int n, float speed, float angle0, string source, int bounces)
        {
            if (Dead) return;
            for (int i = 0; i < n; i++)
            {
                var dir = Sim.Dir(angle0 + i * 360f / n);
                var o = new Orb { Pos = Sim.Flat(c) + dir * rim, Vel = dir * speed, MaxBounces = bounces };
                o.Src = new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Radius = OrbRadius, Damage = OrbDmg, Knockback = 0, Stun = 6 };
                o.View = TakeOrbView(); orbs.Add(o); OrbsFired++;
            }
        }
        Transform TakeOrbView()
        {
            Transform t = orbPool.Count > 0 ? orbPool.Pop() : null;
            if (!t)
            {
                t = Prim.Make(PrimitiveKind.Sphere, transform.parent, Vector3.zero, Vector3.one * (OrbRadius * 2.2f), OrbCore, 4f);
                t.name = "Counterweight orb";
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            t.gameObject.SetActive(true);
            return t;
        }
        void ReturnOrb(Orb o) { if (o.View) { o.View.gameObject.SetActive(false); orbPool.Push(o.View); } }
        void StepOrbs()
        {
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i];
                o.Pos += o.Vel * Sim.Dt;
                bool gone = false, popped = false;
                // the floor's edge: it bounces off (angle in = angle out) until its bounces are spent, then it vanishes
                if (Sim.Flat(o.Pos).magnitude > CombatWorld.PlayerBound)
                {
                    if (o.Bounces >= o.MaxBounces) gone = true;
                    else { o.Pos = Sim.Flat(o.Pos).normalized * CombatWorld.PlayerBound; o.Vel = PendulumSentinelRules.Reflect(o.Vel, o.Pos); o.Bounces++; OrbBounces++; }
                }
                if (!gone) OrbFarthest = Mathf.Max(OrbFarthest, Sim.Flat(o.Pos).magnitude);
                if (!gone)
                    foreach (var p in W.Players)
                    {
                        if (p.Down || o.Src.Hit.Contains(p)) continue;
                        if (Sim.Flat(p.transform.position - o.Pos).magnitude > OrbRadius + PlayerController.Radius) continue;
                        o.Src.Origin = o.Pos; o.Src.Hit.Add(p); p.ReceiveHit(o.Src); OrbHits++;
                        gone = popped = true; break;   // an orb pops on the first player it hits
                    }
                if (gone) { OrbEnds.Add((o.Bounces, o.MaxBounces, popped)); ReturnOrb(o); orbs.RemoveAt(i); continue; }
                if (o.View) o.View.position = new Vector3(o.Pos.x, 0.9f, o.Pos.z);
            }
        }
        void ClearOrbs() { foreach (var o in orbs) ReturnOrb(o); orbs.Clear(); }

        // ---------------- falling weights (presentation) ----------------
        sealed class Falling { public Transform View; public Vector3 From, To; public int Start, End; }
        readonly List<Falling> falling = new List<Falling>();
        void Fall(Vector3 from, Vector3 to, int start, int end)
        {
            var t = Prim.Make(PrimitiveKind.Sphere, transform.parent, from, Vector3.one * 0.9f, new Color(0.85f, 0.66f, 0.32f), 0.5f);
            t.name = "Counterweight (thrown)"; t.gameObject.SetActive(false);
            falling.Add(new Falling { View = t, From = from, To = to, Start = start, End = end });
        }
        void StepFalling()
        {
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i];
                if (Age < f.Start) continue;
                float u = Mathf.Clamp01((Age - f.Start) / (float)Mathf.Max(1, f.End - f.Start));
                if (f.View) { f.View.gameObject.SetActive(true); f.View.position = Vector3.Lerp(f.From, f.To, u) + Vector3.up * (12f * u * (1 - u) + 0.45f); }
                if (Age > f.End + 20) { if (f.View) Destroy(f.View.gameObject); falling.RemoveAt(i); }
            }
        }
        void ClearFalling() { foreach (var f in falling) if (f.View) Destroy(f.View.gameObject); falling.Clear(); }

        // ================= 3. Plumb Line =================
        public int PlumbCasts { get; private set; }
        Transform plumbLine;
        public bool PlumbActive { get; private set; }

        IEnumerable<int> Plumb(bool drift)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = drift ? "Plumb Line (drifting)" : "Plumb Line"; PlumbCasts++; PlumbActive = true;
            Clip("PlumbDrop", Beats(1.5f)); loopClip = "PlumbLoop"; Audio.Stop(); Audio.Level(true);
            var centre = Sim.Flat(transform.position);
            var marks = PendulumSentinelRules.PlumbMarks(rng, PendulumSentinelRules.MarkCount(Standing), centre);
            LiveMarks = marks; float r = MarkRadius;
            var views = new List<BellkeeperFx.Telegraph>();
            var hatch = W.BossFx.Circle(CombatWorld.PlayerBound + 0.6f); hatch.Tint(HatchFill, HatchBorder); hatch.Lock(); hatch.Place(Centre, 0); views.Add(hatch);
            var markViews = new List<BellkeeperFx.Telegraph>();
            foreach (var m in marks) { var t = W.BossFx.Circle(r); t.Tint(MarkFill, MarkBorder); t.Lock(); t.Place(m, 0); markViews.Add(t); views.Add(t); }
            // the plumb line: from the pendulum's pivot (3.4 m, in front of the tower) straight down to the floor
            plumbLine = Prim.Make(PrimitiveKind.Cylinder, transform, new Vector3(0, 1.7f, 0.45f), new Vector3(0.035f, 1.7f, 0.035f), PlumbGold, 3f);
            plumbLine.name = "Plumb line";
            int total = Beats(Phase2 ? PlumbBeatsLow : PlumbBeatsHigh), driftT = drift ? Beats(DriftBeats) : 0; float sgn = rng.Next(2) == 0 ? -1 : 1;
            float perTick = DriftPerBeat / BeatTicks * sgn;
            for (int t = 0; t < total; t++)
            {
                if (t < driftT)
                    for (int i = 0; i < marks.Count; i++) { marks[i] = PendulumSentinelRules.Drift(marks[i], centre, perTick); markViews[i].Place(marks[i], 0); }
                hatch.Progress((t + 1) / (float)total);
                Windup((t + 1) / (float)total);
                yield return 1;
            }
            // the floor's hit: everyone outside a mark, and everyone in a mark holding more than two
            var counts = new int[marks.Count];
            foreach (var p in W.Players) { if (p.Down) continue; int i = PendulumSentinelRules.MarkIndex(marks, r, p.transform.position); if (i >= 0) counts[i]++; }
            for (int i = 0; i < counts.Length; i++) if (counts[i] > Capacity) { PlumbCracks++; markViews[i].Tint(StrikeFill, StrikeBorder); }
            var blow = Blow("Plumb Line", Centre, PlumbDmg);
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                int i = PendulumSentinelRules.MarkIndex(marks, r, p.transform.position);
                if (i >= 0 && counts[i] <= Capacity) continue;
                blow.Hit.Add(p); p.ReceiveHit(blow); PlumbHits++;
            }
            PlumbLog.Add((Tick, new List<Vector3>(marks), r));
            Audio.Level(false); Audio.Boom(); CameraShake.Shake(0.2f, 0.3f); Release(1.2f);
            W.BossFx.TollResolve(Centre, 0, CombatWorld.PlayerBound);
            if (plumbLine) Destroy(plumbLine.gameObject);
            foreach (var v in views) v.Dismiss();
            yield return 6;
            PlumbActive = false; LiveMarks = null; loopClip = "Idle"; Current = "Plumb Line (recovery)";
            yield return Beats(0.5f);
        }

        // ================= 4. Return Stroke =================
        public int StrokeCasts { get; private set; }

        public readonly List<(int tick, Vector3 at, float yaw)> CleaveLog = new List<(int, Vector3, float)>();

        IEnumerable<int> Stroke(int count)
        {
            Current = count > 1 ? "Return Stroke (triple)" : "Return Stroke"; StrokeCasts++;
            var (showB, dashB, cleaveB) = PendulumSentinelRules.StrokeBeats(Level, Phase2);
            Hazard lastCleave = null;
            for (int s = 0; s < count; s++)
            {
                var from = Sim.Flat(transform.position);
                var (dir, yaw, len, end) = PendulumSentinelRules.StrokeLane(from, AimAt(false), transform.forward);
                faceYaw = yaw;
                int warn = Beats(showB), dash = Beats(dashB), cleaveDelay = Beats(cleaveB);
                var lane = new Hazard { Source = "Return Stroke", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Yaw = yaw, Length = len,
                    Width = PendulumSentinelRules.StrokeWidth, LockAt = 0, ResolveAt = warn + dash, HitOnResolve = false, Damage = StrokeDmg, Knockback = 0, Stun = 6,
                    ClipRadius = CombatWorld.PlayerBound + 0.6f };
                Live(lane, StrikeFill, StrikeBorder, 0.5f);
                // the cleave: shown with the lane round where he will stop, it lands a moment after he slams the edge,
                // over the half-circle behind him (the side he charged from)
                var cleave = new Hazard { Source = "Return Stroke (cleave)", Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = end, Yaw = yaw + 180f,
                    Radius = PendulumSentinelRules.RearRadius, HalfAngle = 90f, LockAt = 0, ResolveAt = warn + dash + cleaveDelay, Damage = CleaveDmg, Knockback = 0, Stun = 6,
                    ClipRadius = CombatWorld.PlayerBound + 0.6f };
                cleave.OnResolve = hz => { CleaveHits += hz.Hit.Count; CleaveLog.Add((Tick, hz.Origin, yaw)); Audio.Swing(); CameraShake.Shake(0.14f, 0.2f); };
                Live(cleave, GroundFill, GroundBorder, 0.45f);
                lastCleave = cleave;
                Clip("ChargeLean", warn); Audio.Creak();
                for (int t = 0; t < warn; t++) { Windup((t + 1) / (float)warn); yield return 1; }
                // the charge: his body sweeps the lane; whoever it passes is hit
                Current = "Return Stroke (charge)"; Clip("Charge", dash); Audio.Rush();
                float bodyLen = Sim.Flat(end - from).magnitude;
                for (int t = 1; t <= dash; t++)
                {
                    float travelled = bodyLen * t / dash;
                    transform.position = from + dir * travelled;
                    foreach (var p in W.Players)
                    {
                        if (p.Down || lane.Hit.Contains(p)) continue;
                        var l = Quaternion.Euler(0, -yaw, 0) * Sim.Flat(p.transform.position - from);
                        if (Mathf.Abs(l.x) > lane.Width / 2 + Hazard.Margin || l.z < -Hazard.Margin || l.z > travelled + BodyRadius) continue;
                        lane.Hit.Add(p); p.ReceiveHit(lane); StrokeHits++;
                    }
                    yield return 1;
                }
                // the blow carries to the edge: the rest of the lane past his body is hit as he slams in
                foreach (var p in W.Players)
                {
                    if (p.Down || lane.Hit.Contains(p) || !lane.Contains(p.transform.position)) continue;
                    lane.Hit.Add(p); p.ReceiveHit(lane); StrokeHits++;
                }
                StrokeLog.Add((Tick, from, Sim.Flat(transform.position), yaw, len));
                Clip("WallSlam", Sim.Ticks(0.8f)); Audio.Crash(); CameraShake.Shake(0.22f, 0.3f); Release(1.4f);
                W.BossFx.ChargeLand(transform.position);
                // below 50% the next lane appears the moment he lands, while this stroke's cleave is still coming
                if (s < count - 1) Current = $"Return Stroke ({(s == 0 ? "second" : "third")})";
            }
            // the last cleave lands, then he is stuck against the edge: the fight's major punish window (nothing live)
            Current = "Return Stroke (cleave)"; loopClip = "StuckLoop"; faceYaw = null;
            for (int guard = 0; guard < 600 && IsLive(lastCleave); guard++) yield return 1;
            Current = "Return Stroke (stuck)"; Stuck = true;
            int stuckFrom = Tick;
            yield return StuckTicks;
            StuckLog.Add((stuckFrom, Tick)); Stuck = false;
            loopClip = "Idle";
            var back = Sim.Flat(transform.position);
            if (back.magnitude > 4f) foreach (var w in WalkTo(back.normalized * 3.5f, WalkSpeed, 300)) yield return w;
            Current = "Return Stroke (recovery)";
        }

        // ================= the 50% transformation =================
        IEnumerable<int> Transition()
        {
            GatePending = false;
            Current = "Shift"; Log("Shift"); ShiftStart = Tick; HpAtShiftStart = Hp; faceYaw = null;
            Clip("PhaseShift", ShiftTicks); Audio.Shift(); CameraShake.Shake(0.12f, 0.3f);
            yield return ShiftTicks;   // harmless: nothing is spawned, and he stays damageable throughout
            Phase2 = true; beatAge = 0; Release(1.5f); ShiftEnd = Tick;
            Log("tempo -> " + Bpm);
        }

        // ---------------- lifecycle ----------------
        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel;
        }

        protected override void OnDefeat()
        {
            ClearOrbs(); ClearSoaks(); ClearFalling(); scheduled.Clear();
            if (plumbLine) Destroy(plumbLine.gameObject);   // floor telegraphs are dismissed by the world's ClearTransient
            PendingWeights = 0; PlumbActive = false; Stuck = false;
            if (presenter) presenter.Action("Defeat", 150);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy()
        {
            foreach (var o in orbs) if (o.View) Destroy(o.View.gameObject);
            foreach (var s in soaks) if (s.View) Destroy(s.View.gameObject);
            while (orbPool.Count > 0) { var t = orbPool.Pop(); if (t) Destroy(t.gameObject); }
            ClearFalling();
        }
    }
}
