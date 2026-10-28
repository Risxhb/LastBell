using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Chimebound Oracle: a practice-only Skyward Spire miniboss (docs/bosses/chimebound-oracle.md). Labelled
    // stand-in body until a final model exists. Every number is a first-pass, heroic-raid target for Medium, not approved
    // balance. Damage is against 100 HP; geometry in metres before the 0.3 m player margin.
    //
    // No ability text reaches the player: PhaseName and Lesson stay empty, and names live only in the F7 diagnostics.
    // Each mechanic is taught by its tell (pose, floor, sound), and every strike lands on a metronome beat.
    //
    // Tempo: this boss's TempoClock sets the beat (on the Metronome Court, the court's metronome follows it). Above 50% it
    // runs at Normal (140 BPM). At 50% the Oracle walks to the centre, tolls, and it commits to Haste (185 BPM) for good.
    //
    //   Toll Lanes      8 north-south lanes; each strike cascades from the far (north) end toward the camera in 0.15 s
    //                   and hits a player once per lane. Above 50%: Rounds (0..7, one every 2 beats, 4-beat warning) or
    //                   Odds & Evens. Below 50%: Called Changes (new order each cast, all runes shown at once, the
    //                   fastest-filling rune fires first, one slot per beat, OracleLanes rules + walking route check).
    //   Chime Wells     floor circles crack (4-beat warning), erupt, then fire orbs in a ring. Above 50%: one well at a
    //                   time, 6 orbs. Below 50%: three at once, 9 orbs, a second ring 2 beats later turned 20 degrees.
    //   Carillon Chain  soak circles lit one at a time; stand in the lit one for 0.5 s. A circle that runs out goes off
    //                   and hits everyone. Above 50%: 3 circles, 3 s each. Below 50%: 5 circles, 2 s each, while single
    //                   lanes strike every 3 beats (never a lane holding the lit circle or the next two).
    //   Resonant Mark   a ring on up to 3 players for about 6 s; then a small well drops at their feet and fires 3 orbs.
    //   Clapper Strike  a cone at the nearest player after a ~1.3 s windup.
    //   Grand Toll      the 50% transition: HP holds at half, no attacks except one Called Changes recital at Haste.
    //   Last Bell       soft enrage at 4:00: every lane strikes together, again and again.
    // Difficulty: Easy uses 6-beat lane/well warnings and 1.4x soak timers. Hard adds Echo: below 50%, each lane strikes
    // again 2 beats after its first hit.
    public sealed class ChimeboundOracleEncounter : BossEncounter
    {
        public override string Id => "boss.chimebound-oracle";
        public override string DisplayName => "The Chimebound Oracle";
        public override string Lesson => "";        // no ability text: players learn from the tells
        public override bool Placeholder => Rig == null || !Rig.HasModel;   // the Meshy model, unless Resources/Characters/ChimeboundOracle is missing
        public override float NextPhaseFraction() => 0.49f;

        // ---------------- tunables ----------------
        public const float NormalBpm = 140f, HasteBpm = 185f;
        public const int LaneDmg = 45, WellDmg = 25, OrbDmg = 15, MissDmg = 30, MarkWellDmg = 20, ClapperDmg = 60;
        public const float LaneWarnBeats = 4, EasyWarnBeats = 6, EchoBeats = 2;
        public const float LaneDrawInset = 0.14f;
        public const int CascadeStepTicks = 3, LaneSegments = 4;      // 4 rune segments, 3 ticks apart (= OracleLanes.CascadeTicks)
        public const float WellRadius = 1.8f, WellWarnBeats = 4, MarkWellRadius = 1.4f, MarkWellWarnBeats = 2;
        public const int OrbsHigh = 6, OrbsLow = 9, MarkOrbs = 3;
        public const int WellGroupsHigh = 2, WellsPerGroupHigh = 3, WellGroupsLow = 2, WellsPerGroupLow = 4, WellsPerGroupLastBell = 5;
        public const float OrbSpeedHigh = 5f, OrbSpeedLow = 6.5f, OrbRadius = 0.35f, WaveGapBeats = 2, WaveTwist = 20f;
        public const float SoakRadius = 1.6f; public const int SoakDwellTicks = 30;
        public const int ChainHigh = 3, ChainLow = 5;
        public const float ChainLiveHigh = 3f, ChainLiveLow = 2f, EasyTimeScale = 1.4f;
        public const float ChainStepMinHigh = 4f, ChainStepMaxHigh = 8.5f, ChainStepMinLow = 3.5f, ChainStepMaxLow = 6f, ChainArea = 9.5f;
        public const float PressureEveryBeats = 3;
        public const float MarkBeatsHigh = 14, MarkBeatsLow = 18, MarkRingRadius = 2.0f; public const int MaxMarks = 3;
        public const float ClapperRadius = 7f, ClapperHalf = 50f, ClapperBeatsHigh = 3, ClapperBeatsLow = 4, ClapperReach = 3.5f;
        public const int EnrageTicks = 240 * Sim.Hz;
        public const float WalkSpeed = 4.5f;

        // palette (HDR): lanes are cold rune light over a red fill, wells amber, soak circles teal, the mark violet
        public static readonly Color LaneFill = new Color(0.95f, 0.18f, 0.22f), LaneBorder = new Color(0.9f, 1.6f, 2.4f),
            WellFill = new Color(1.4f, 0.6f, 0.12f), WellBorder = new Color(2.6f, 1.4f, 0.35f),
            SoakFill = new Color(0.25f, 1.35f, 1.0f), SoakBorder = new Color(0.8f, 3.2f, 2.4f),   // "go here": the brightest teal on the floor
            ThreadBorder = new Color(1.6f, 1.15f, 0.5f),
            MarkFill = new Color(0.45f, 0.18f, 0.7f), MarkBorder = new Color(1.5f, 0.8f, 2.4f),
            ConeFill = new Color(1.25f, 0.30f, 0.12f), ConeBorder = new Color(2.2f, 0.75f, 0.25f),
            OrbCore = new Color(0.9f, 1.0f, 1.0f), OrbHaste = new Color(1.0f, 0.85f, 0.6f),
            ArrowFill = new Color(1.6f, 0.95f, 0.25f), ArrowBorder = new Color(2.6f, 2.0f, 1.2f);

        public readonly TempoClock Clock = new TempoClock();
        public ChimeboundOracleRig Rig { get; private set; }
        public ChimeboundOracleAudio Audio { get; private set; }
        public TempoTyrantFx Fx { get; private set; }

        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public bool HoldHp { get; private set; }
        public bool Enraged { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;                               // dev/test hook: replaces the next rotation entry once
        public bool ForceEnrage;                                 // dev/test hook: start the Last Bell at the next block boundary
        public int CombatTicks => Age;
        public int Seed = Environment.TickCount;                 // Called Changes / placement randomness (tests may set it)
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;

        // lanes
        public List<OracleLanes.Slot> LastPattern { get; private set; }
        public int LastPatternTries { get; private set; }
        public string LastPatternName { get; private set; } = "";
        public int LaneCasts { get; private set; }
        public int LaneStrikes { get; private set; }
        public int LaneHits { get; private set; }
        public readonly List<(int tick, int lane, bool echo, string pattern)> LaneLog = new List<(int, int, bool, string)>();
        public readonly List<(int tick, int lane)> ChimeLog = new List<(int, int)>();
        public int OffBeatStrikes { get; private set; }         // strikes that landed more than 2 ticks from a beat
        public int ConcurrentWithWellsP1 { get; private set; }  // above 50%, lane strikes while a well was warning (must stay 0)

        // wells and orbs
        public int WellEruptions { get; private set; }
        public int OrbsFired { get; private set; }
        public int OrbHits { get; private set; }
        public readonly List<(int tick, Vector3 at, int count, float angle0, int wave)> RingLog = new List<(int, Vector3, int, float, int)>();
        public int LiveWells { get; private set; }
        public int MaxConcurrentWells { get; private set; }
        public int OrbCount => orbs.Count;
        public int ArrowsShown { get; private set; }
        public readonly List<(int tick, Vector3 at, int count, float angle0)> ArrowLog = new List<(int, Vector3, int, float)>();

        // soak chain
        public bool ChainActive { get; private set; }
        public int ChainIndex { get; private set; } = -1;
        public int ChainLength { get; private set; }
        public readonly List<Vector3> ChainPositions = new List<Vector3>();
        public int ChainLiveTicks { get; private set; }
        public int Soaks { get; private set; }
        public int Misses { get; private set; }
        public int OutOfOrderStands { get; private set; }       // ticks someone stood in an unlit future circle (never counts)
        public readonly List<(int tick, int lane, int circle)> PressureLog = new List<(int, int, int)>();
        public int PressureOverlaps { get; private set; }       // pressure lanes that held the lit circle or the next two (must stay 0)

        // mark / clapper
        public int MarksCast { get; private set; }
        public int MarksDropped { get; private set; }
        public int ActiveMarks => marks.Count;
        public int ClapperCasts { get; private set; }
        [NonSerialized] public Hazard LastClapper;

        enum Atk { Rounds, OddsEvens, Wells, Clapper, Mark, Carillon, Called, CalledWells, WellsBurst }
        static readonly Atk[] RotationHigh = { Atk.Rounds, Atk.Wells, Atk.Clapper, Atk.Mark, Atk.Carillon, Atk.OddsEvens, Atk.Wells, Atk.Clapper, Atk.Carillon };
        static readonly Atk[] RotationLow = { Atk.Called, Atk.WellsBurst, Atk.Mark, Atk.CalledWells, Atk.Clapper, Atk.Carillon, Atk.Called, Atk.Mark, Atk.WellsBurst, Atk.Carillon };
        int rotHigh, rotLow, castSeed;
        System.Random rng;

        sealed class Orb { public Vector3 Pos, Vel; public Hazard Src; public Transform View; }
        readonly List<Orb> orbs = new List<Orb>();
        readonly Stack<Transform> orbPool = new Stack<Transform>();
        sealed class Mark { public PlayerController P; public int Start, End; public BellkeeperFx.Telegraph Ring; }
        readonly List<Mark> marks = new List<Mark>();
        readonly List<(int at, Action act)> scheduled = new List<(int, Action)>();
        int lastHalfBeat = -1;

        void Awake() { MaxHp = 3000; BodyRadius = 1.2f; HitHeight = 2.2f; MarkHeight = 4.6f; }

        protected override void Build()
        {
            Rig = ChimeboundOracleRig.Create(transform);
            Audio = gameObject.AddComponent<ChimeboundOracleAudio>();
            Fx = gameObject.AddComponent<TempoTyrantFx>();   // shared ribbon effects (streaks along lanes, floor rims)
            gameObject.AddComponent<ChimeboundOracleHud>().Bind(this);
            Clock.NormalBpm = NormalBpm; Clock.HasteBpm = HasteBpm;
            Clock.NormalTicks = Clock.HasteTicks = int.MaxValue;   // tempo changes only at the 50% Grand Toll
            Clock.Reset(Tempo.Normal);
            PhaseName = "";
            OracleLanes.Bound = CombatWorld.PlayerBound;
        }

        int Beats(float b) => Clock.TicksFor(b);
        float LaneWarn => Level == Difficulty.Easy ? EasyWarnBeats : LaneWarnBeats;
        float WellWarn => Level == Difficulty.Easy ? EasyWarnBeats : WellWarnBeats;
        float TimeScale => Level == Difficulty.Easy ? EasyTimeScale : 1f;
        bool EchoOn => Level == Difficulty.Hard && Phase2;
        static float Bound => CombatWorld.PlayerBound;
        static float Clip => CombatWorld.PlayerBound + PlayerController.Radius;

        Hazard Live(Hazard h, Color fill, Color border, float flash = 0.35f)
        {
            W.Hazards.Spawn(h);
            h.Tele.Tint(fill, border); h.Tele.FlashScale = flash; h.Tele.SpentFast = true;
            return h;
        }
        void Schedule(int ticks, Action a) => scheduled.Add((Age + Mathf.Max(1, ticks), a));
        void Log(string s) => BlockLog.Add($"{W.Tick} {Clock.State} {s}");

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in GrandToll()) yield return w;
                if (!Enraged && (Age >= EnrageTicks || ForceEnrage)) foreach (var w in LastBell()) yield return w;

                Blocks++;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> body;
                switch (atk)
                {
                    case Atk.Rounds: body = LaneCast(OracleLanes.Rounds(), "Rounds", false); break;
                    case Atk.OddsEvens: body = LaneCast(OracleLanes.OddsEvens(), "Odds & Evens", false); break;
                    case Atk.Called: body = CalledChanges(false); break;
                    case Atk.CalledWells: body = CalledChanges(true); break;
                    case Atk.Wells: body = WellsSequence(); break;
                    case Atk.WellsBurst: body = WellsBurst(); break;
                    case Atk.Clapper: body = Clapper(); break;
                    case Atk.Mark: body = CastMark(); break;
                    default: body = Carillon(); break;
                }
                foreach (var w in body) yield return w;
                Current = "between blocks"; Rig.Pose(OraclePose.Idle);
                yield return Beats(2);
            }
        }

        protected override void StepAlways()
        {
            Rig.Step();
            Clock.Step(W.Tick);
            Rig.Haste = Clock.State == Tempo.Haste;
            for (int i = scheduled.Count - 1; i >= 0; i--)
                if (Age >= scheduled[i].at) { var a = scheduled[i].act; scheduled.RemoveAt(i); a(); }
            StepOrbs();
            StepMarks();
            if (Current == "approach" || Current == "reposition") Rig.Pose(OraclePose.Walk);
        }

        protected override void OnHurt()
        {
            // the Grand Toll: HP holds at half from the moment it is reached until the recital ends
            if ((!Phase2 || HoldHp) && Hp * 2 < MaxHp) Hp = (MaxHp + 1) / 2;
        }

        IEnumerable<int> AlignToBeat()
        {
            int t = Clock.TicksToNextBeat();
            if (t > 0) yield return t;
        }

        // ---------------- Toll Lanes ----------------
        // One lane strike: 4 rune segments far-to-near, CascadeStepTicks apart; the lane hits each player at most once.
        public static Vector3 SegmentOrigin(int lane, int s) => new Vector3(OracleLanes.CentreX(lane), 0, -Clip + s * (2 * Clip / LaneSegments));
        // One floor warning per lane (a single telegraph: no stacked pieces, so no seams or overlap flicker). When it
        // resolves, the strike rolls far-to-near: the 4 segments are hit-tested CascadeStepTicks apart, once per player.
        void FireLane(int lane, int resolveAt, bool echo, string pattern)
        {
            var hit = new HashSet<PlayerController>();
            float segLen = 2 * Clip / LaneSegments;
            var h = new Hazard
            {
                Source = echo ? "Toll Lane (echo)" : "Toll Lane", Shape = HazardShape.Lane, Kind = HazardKind.Ground,
                // drawn 14 cm narrower than the lane so neighbouring warnings never share a border (two coincident translucent
                // borders swap draw order as the camera moves: flicker). The hit test below still uses the full lane.
                Origin = SegmentOrigin(lane, 0), Yaw = 0, Length = 2 * Clip, Width = OracleLanes.Width - LaneDrawInset,
                LockAt = 0, ResolveAt = resolveAt, Damage = LaneDmg, Knockback = 0.8f, Stun = 8,
                ClipRadius = Clip, HitOnResolve = false,
            };
            void Segment(Hazard hz, int s)
            {
                var seg = new Hazard { Source = hz.Source, Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = SegmentOrigin(lane, s), Yaw = 0,
                    Length = segLen, Width = OracleLanes.Width, Damage = LaneDmg, Knockback = 0.8f, Stun = 8 };
                foreach (var p in W.Players)
                {
                    if (p.Down || hit.Contains(p) || !seg.Contains(p.transform.position)) continue;
                    hit.Add(p); seg.Hit.Add(p); p.ReceiveHit(seg); LaneHits++;
                }
                Fx.Streak(seg.Origin, 0, 0, segLen, new Color(1.0f, 1.5f, 2.2f), OracleLanes.Width * 0.3f);
                if (s == LaneSegments - 1) CameraShake.Shake(0.05f, 0.08f);
            }
            h.OnResolve = hz =>
            {
                LaneStrikes++; LaneLog.Add((W.Tick, lane, echo, pattern));
                if (!Clock.Transitioning && !NearBeat()) OffBeatStrikes++;
                if (!Phase2 && LiveWells > 0) ConcurrentWithWellsP1++;
                Release(0.5f);
                Segment(hz, 0);
                for (int s = 1; s < LaneSegments; s++) { int k = s; Schedule(k * CascadeStepTicks, () => { if (!Dead) Segment(hz, k); }); }
                if (EchoOn && !echo && !Enraged) FireLane(lane, Beats(EchoBeats), true, pattern + " echo");
            };
            Live(h, LaneFill * (echo ? 0.75f : 1f), LaneBorder, 0.2f);
            // the lane's own pitch, one beat before it strikes
            int chimeIn = resolveAt - Beats(1);
            if (chimeIn >= 1) Schedule(chimeIn, () => { Audio.Lane(lane); ChimeLog.Add((W.Tick, lane)); });
            else { Audio.Lane(lane); ChimeLog.Add((W.Tick, lane)); }
        }

        bool NearBeat()
        {
            double frac = Clock.Beat - Math.Floor(Clock.Beat);
            double ticksFromBeat = Math.Min(frac, 1 - frac) * 60.0 / Clock.Bpm * Sim.Hz;
            return ticksFromBeat <= 2.01;
        }

        // A whole pattern from the cast start (on a beat). Above 50% each lane appears 4 beats before it strikes
        // (allAtOnce false: at most two lanes are ever lit); below 50% every rune appears at once.
        IEnumerable<int> LaneCast(List<OracleLanes.Slot> slots, string name, bool allAtOnce)
        {
            Current = "Toll Lanes: " + name; LastPatternName = name; LastPattern = slots; LaneCasts++;
            Rig.Pose(OraclePose.StaffRaise, Beats(2));
            foreach (var w in AlignToBeat()) yield return w;
            float warn = LaneWarn;
            int last = 0;
            var spawns = new List<(int at, int lane, int resolve)>();
            foreach (var s in slots)
                foreach (var lane in s.B >= 0 ? new[] { s.A, s.B } : new[] { s.A })
                {
                    int strike = Beats(warn + s.Beat) + 1;   // +1: a hazard spawned this tick resolves at age ResolveAt - 1 ticks later
                    int spawn = allAtOnce ? 0 : Beats(s.Beat);
                    spawns.Add((spawn, lane, strike - spawn));
                    last = Mathf.Max(last, strike);
                }
            spawns.Sort((a, b) => a.at.CompareTo(b.at));
            int age = 0, k = 0;
            while (age <= last + CascadeStepTicks * LaneSegments)
            {
                while (k < spawns.Count && spawns[k].at <= age) { FireLane(spawns[k].lane, spawns[k].resolve, false, name); k++; }
                Windup(Mathf.Clamp01(age / (float)Mathf.Max(1, last)));
                age++; yield return 1;
            }
            if (EchoOn) yield return Beats(EchoBeats) + CascadeStepTicks * LaneSegments;
            Current = "Toll Lanes (recovery)"; Rig.Pose(OraclePose.Recover);
            glowTarget = 0.5f; yield return Beats(2); glowTarget = 1;
        }

        IEnumerable<int> CalledChanges(bool withWells)
        {
            var r = new System.Random(Seed * 31 + castSeed++);
            var slots = OracleLanes.CalledChanges(r, Clock.Bpm, LaneWarn, EchoOn ? EchoBeats : 0, out int tries);
            LastPatternTries = tries;
            if (withWells)
            {
                // two wells crack two beats into the cast and erupt mid-pattern: the orbs cross the lanes
                var spots = WellSpots(2, 5f);
                Schedule(Beats(2) + Clock.TicksToNextBeat(), () => { foreach (var at in spots) Well(at, WellRadius, Beats(WellWarn), OrbsLow, OrbSpeedLow, 2, WellDmg, "Chime Well"); });
            }
            foreach (var w in LaneCast(slots, withWells ? "Called Changes + Wells" : "Called Changes", true)) yield return w;
        }

        // ---------------- Chime Wells and orbs ----------------
        // A well's arrows: one short chevron lane per orb, from near the centre to the rim, so the floor shows exactly
        // where each orb will fly. The lane telegraph's chevrons point back toward its origin, so each arrow starts at the
        // rim and runs inward: its chevrons point outward, the way the orb goes. A second wave's arrows are dimmer.
        List<BellkeeperFx.Telegraph> WellArrows(Vector3 at, float radius, int count, float angle0, int waves)
        {
            var list = new List<BellkeeperFx.Telegraph>();
            float len = Mathf.Max(0.3f, radius - 0.45f), width = radius < 1.6f ? 0.26f : 0.32f;
            for (int wv = 0; wv < waves; wv++)
                for (int i = 0; i < count; i++)
                {
                    float yaw = angle0 + WaveTwist * wv + i * 360f / count;
                    var t = W.BossFx.Lane(len, width);
                    if (wv == 0) t.Tint(ArrowFill, ArrowBorder); else t.Tint(ArrowFill * 0.45f, ArrowBorder * 0.5f);
                    t.Lock(); t.Place(at + Sim.Dir(yaw) * (radius - 0.05f), yaw + 180f);
                    list.Add(t);
                }
            ArrowsShown += list.Count;
            return list;
        }

        void Well(Vector3 at, float radius, int warn, int count, float speed, int waves, int dmg, string source)
        {
            LiveWells++; MaxConcurrentWells = Mathf.Max(MaxConcurrentWells, LiveWells);
            float angle0 = (float)(rng.NextDouble() * 360.0);
            var h = new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = radius, LockAt = 0, ResolveAt = warn, Damage = dmg, Knockback = 1.2f };
            var arrows = WellArrows(at, radius, count, angle0, waves);
            ArrowLog.Add((W.Tick, at, count, angle0));
            h.OnResolve = hz =>
            {
                foreach (var a in arrows) a.Dismiss();
                LiveWells--; WellEruptions++;
                Audio.Burst(); Fx.Rim(hz.Origin, hz.Radius, new Color(2.4f, 1.3f, 0.3f));
                FireRing(hz.Origin, hz.Radius, count, speed, angle0, source + " orb", 0);
                for (int wv = 1; wv < waves; wv++) { int wave = wv; Schedule(Beats(WaveGapBeats * wv), () => FireRing(hz.Origin, hz.Radius, count, speed, angle0 + WaveTwist * wave, source + " orb", wave)); }
            };
            Live(h, WellFill, WellBorder, 0.4f);
            if (warn > Beats(1)) Schedule(Mathf.Max(1, warn - Mathf.Min(warn - 1, Sim.Ticks(1.35f))), () => Audio.Hum());
        }

        // orbs leave from the well's rim, so a player standing inside takes the eruption, not the whole ring at once
        void FireRing(Vector3 c, float rim, int n, float speed, float angle0, string source, int wave)
        {
            if (Dead) return;
            RingLog.Add((W.Tick, c, n, angle0, wave));
            for (int i = 0; i < n; i++)
            {
                var dir = Sim.Dir(angle0 + i * 360f / n);
                var o = new Orb { Pos = c + dir * rim, Vel = dir * speed };
                o.Src = new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Radius = OrbRadius, Damage = OrbDmg, Knockback = 0.5f, Stun = 6 };
                o.View = TakeOrbView();
                orbs.Add(o); OrbsFired++;
            }
        }

        Transform TakeOrbView()
        {
            Transform t = orbPool.Count > 0 ? orbPool.Pop() : null;
            if (!t)
            {
                t = Prim.Make(PrimitiveKind.Sphere, transform.parent, Vector3.zero, Vector3.one * (OrbRadius * 2.2f), OrbCore, 4f);
                t.name = "Oracle orb";
                t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            t.gameObject.SetActive(true);
            Prim.SetGlow(t.GetComponent<MeshRenderer>(), (Phase2 ? OrbHaste : OrbCore) * 4f);
            return t;
        }
        void ReturnOrb(Orb o) { if (o.View) { o.View.gameObject.SetActive(false); orbPool.Push(o.View); } }

        void StepOrbs()
        {
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var o = orbs[i];
                o.Pos += o.Vel * Sim.Dt;
                bool gone = new Vector2(o.Pos.x, o.Pos.z).magnitude > Bound + 1.5f;
                if (!gone)
                    foreach (var p in W.Players)
                    {
                        if (p.Down || o.Src.Hit.Contains(p)) continue;
                        if (Sim.Flat(p.transform.position - o.Pos).magnitude > OrbRadius + PlayerController.Radius) continue;
                        o.Src.Origin = o.Pos; o.Src.Hit.Add(p); p.ReceiveHit(o.Src); OrbHits++;
                        gone = true; break;   // an orb pops on the first player it hits
                    }
                if (gone) { ReturnOrb(o); orbs.RemoveAt(i); continue; }
                if (o.View) o.View.position = new Vector3(o.Pos.x, 0.9f, o.Pos.z);
            }
        }
        void ClearOrbs() { foreach (var o in orbs) ReturnOrb(o); orbs.Clear(); }

        // spots on the floor at least `spacing` apart, the first within reach of the nearest player
        List<Vector3> WellSpots(int n, float spacing)
        {
            var list = new List<Vector3>();
            var near = W.NearestStanding(transform.position);
            for (int guard = 0; list.Count < n && guard < 400; guard++)
            {
                Vector3 p;
                if (list.Count == 0 && near && guard < 200)
                {
                    float a = (float)(rng.NextDouble() * 360), d = 2.5f + (float)rng.NextDouble() * 3f;
                    p = Sim.Flat(near.transform.position) + Sim.Dir(a) * d;
                }
                else { float a = (float)(rng.NextDouble() * 360), d = (float)Math.Sqrt(rng.NextDouble()) * 9f; p = Sim.Dir(a) * d; }
                if (new Vector2(p.x, p.z).magnitude > 9.5f) continue;
                bool ok = true; foreach (var q in list) if ((q - p).magnitude < spacing) ok = false;
                if (ok) list.Add(p);
            }
            return list;
        }

        // Wells drop in groups: a whole group lands together, warns, and detonates together. Above 50%: two groups of 3
        // (6 orbs each). Below 50%: two groups of 4 (9 orbs each). In the Last Bell (berserk), groups of 5 fall between
        // the lane volleys.
        public int GroupsDropped { get; private set; }
        public readonly List<(int tick, int wells)> GroupLog = new List<(int, int)>();
        void DropGroup(int wells, float spacing, int warn, int orbsEach, float speed, int waves)
        {
            var spots = WellSpots(wells, spacing);
            foreach (var at in spots) Well(at, WellRadius, warn, orbsEach, speed, waves, WellDmg, "Chime Well");
            GroupsDropped++; GroupLog.Add((W.Tick, spots.Count));
            Fx.Rim(transform.position, BodyRadius + 0.8f, new Color(2.4f, 1.3f, 0.3f), 0.25f);
        }

        IEnumerable<int> WellGroups(int groups, int wellsEach, float spacing, int orbsEach, float speed)
        {
            for (int g = 0; g < groups; g++)
            {
                Rig.Pose(OraclePose.FloorStrike, Beats(1));
                foreach (var w in AlignToBeat()) yield return w;
                int warn = Beats(WellWarn) + 1;
                DropGroup(wellsEach, spacing, warn, orbsEach, speed, 1);
                for (int i = 0; i < warn; i++) { Windup(i / (float)warn); yield return 1; }
                Release(1f); CameraShake.Shake(0.1f, 0.15f); Rig.Pose(OraclePose.Recover);
                yield return Beats(2);
            }
        }

        IEnumerable<int> WellsSequence()
        {
            Current = "Chime Wells";
            foreach (var w in WellGroups(WellGroupsHigh, WellsPerGroupHigh, 4.5f, OrbsHigh, OrbSpeedHigh)) yield return w;
            Current = "Chime Wells (recovery)";
            while (orbs.Count > 0) yield return 1;   // the block ends when its orbs have left the floor
            yield return Beats(1);
        }

        IEnumerable<int> WellsBurst()
        {
            Current = "Chime Wells (burst)";
            foreach (var w in WellGroups(WellGroupsLow, WellsPerGroupLow, 4.5f, OrbsLow, OrbSpeedLow)) yield return w;
            Current = "Chime Wells (recovery)";
            while (orbs.Count > 0) yield return 1;
            yield return Beats(1);
        }

        // ---------------- Carillon Chain ----------------
        public List<Vector3> PlaceChain(int n, float minStep, float maxStep)
        {
            var list = new List<Vector3>();
            var near = W.NearestStanding(Vector3.zero);
            var from = near ? Sim.Flat(near.transform.position) : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 best = Vector3.zero; bool found = false;
                for (int g = 0; g < 300 && !found; g++)
                {
                    float a = (float)(rng.NextDouble() * 360), d = Mathf.Lerp(minStep, maxStep, (float)rng.NextDouble());
                    var p = from + Sim.Dir(a) * d;
                    if (new Vector2(p.x, p.z).magnitude > ChainArea) continue;
                    if (Sim.Flat(p - transform.position).magnitude < BodyRadius + SoakRadius + 0.6f) continue;   // never under the body
                    bool ok = true; foreach (var q in list) if ((q - p).magnitude < 2 * SoakRadius + 0.8f) ok = false;
                    if (ok) { best = p; found = true; }
                }
                if (!found) best = Sim.Flat(from).normalized * Mathf.Min(ChainArea, Sim.Flat(from).magnitude);
                list.Add(best); from = best;
            }
            return list;
        }

        public static bool LaneTouchesCircle(int lane, Vector3 c, float r)
        {
            float lo = OracleLanes.CentreX(lane) - OracleLanes.Width / 2 - Hazard.Margin, hi = OracleLanes.CentreX(lane) + OracleLanes.Width / 2 + Hazard.Margin;
            return c.x + r >= lo && c.x - r <= hi;
        }

        IEnumerable<int> Carillon()
        {
            Current = "Carillon Chain";
            int n = Phase2 ? ChainLow : ChainHigh;
            ChainLiveTicks = Sim.Ticks((Phase2 ? ChainLiveLow : ChainLiveHigh) * TimeScale);
            ChainPositions.Clear(); ChainPositions.AddRange(PlaceChain(n, Phase2 ? ChainStepMinLow : ChainStepMinHigh, Phase2 ? ChainStepMaxLow : ChainStepMaxHigh));
            ChainLength = n; ChainIndex = -1; ChainActive = true;
            Rig.Pose(OraclePose.ArmsSpread, Beats(2));
            // the whole route appears dim first, then the first circle lights
            var dim = new BellkeeperFx.Telegraph[n];
            for (int i = 0; i < n; i++) { dim[i] = W.BossFx.Circle(SoakRadius); dim[i].Memory(); dim[i].Lock(); dim[i].Place(ChainPositions[i], 0); }
            foreach (var w in AlignToBeat()) yield return w;
            yield return Beats(2);
            BellkeeperFx.Telegraph lit = null, thread = null;
            int pressureEvery = Beats(PressureEveryBeats), sincePressure = pressureEvery - Beats(1);
            for (int i = 0; i < n; i++)
            {
                ChainIndex = i;
                dim[i].Dismiss();
                lit = W.BossFx.Circle(SoakRadius); lit.Tint(SoakFill, SoakBorder); lit.Lock(); lit.Place(ChainPositions[i], 0);
                if (thread != null) thread.Dismiss();
                thread = null;
                if (i + 1 < n)
                {
                    var a = ChainPositions[i]; var b = ChainPositions[i + 1]; var d = Sim.Flat(b - a);
                    thread = W.BossFx.Lane(Mathf.Max(0.1f, d.magnitude - 2 * SoakRadius), 0.16f); thread.Memory(); thread.Tint(new Color(0.4f, 0.32f, 0.15f), ThreadBorder); thread.Lock();
                    thread.Place(a + d.normalized * SoakRadius, Sim.YawTo(a, b, 0));
                }
                Audio.Chain(i);
                Fx.Rim(ChainPositions[i], SoakRadius + 0.4f, new Color(0.6f, 2.4f, 1.8f), 0.3f);   // a flash as it lights
                int dwell = 0, live = 0; bool soaked = false;
                while (live < ChainLiveTicks)
                {
                    live++;
                    lit.Progress(live / (float)ChainLiveTicks);
                    if (Clock.LastBeatIndex >= 0) Fx.Rim(ChainPositions[i], SoakRadius, new Color(0.5f, 2.0f, 1.5f), 0.16f);   // the lit circle pulses on the beat
                    bool inside = false;
                    foreach (var p in W.Players)
                    {
                        if (p.Down) continue;
                        if (Sim.Flat(p.transform.position - ChainPositions[i]).magnitude <= SoakRadius + Hazard.Margin) inside = true;
                        for (int j = i + 1; j < n; j++) if (Sim.Flat(p.transform.position - ChainPositions[j]).magnitude <= SoakRadius + Hazard.Margin) OutOfOrderStands++;
                    }
                    if (inside) dwell++;
                    if (dwell >= SoakDwellTicks) { soaked = true; break; }
                    if (Phase2 && ++sincePressure >= pressureEvery) { sincePressure = 0; PressureLane(i); }
                    Windup(0.5f * live / ChainLiveTicks);
                    yield return 1;
                }
                lit.Dismiss();
                if (soaked)
                {
                    Soaks++; Audio.Soaked(); Fx.Rim(ChainPositions[i], SoakRadius, new Color(0.6f, 2.2f, 1.7f), 0.3f);
                }
                else
                {
                    Misses++; Audio.Missed(); Release(1f); CameraShake.Shake(0.18f, 0.25f);
                    Fx.Rim(ChainPositions[i], SoakRadius, new Color(2.4f, 0.5f, 0.3f), 0.3f); Fx.Rim(ChainPositions[i], SoakRadius + 2.5f, new Color(2.0f, 0.4f, 0.25f), 0.35f);
                    var boom = new Hazard { Source = "Carillon (unsoaked)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = ChainPositions[i], Radius = 999, Damage = MissDmg, Knockback = 0, Stun = 0 };
                    foreach (var p in W.Players) if (!p.Down) { boom.Hit.Add(p); p.ReceiveHit(boom); }
                }
            }
            if (thread != null) thread.Dismiss();
            ChainActive = false; ChainIndex = -1;
            Current = "Carillon Chain (recovery)"; Rig.Pose(OraclePose.Recover);
            yield return Beats(LaneWarn) + CascadeStepTicks * LaneSegments + 1;   // let any pressure lane finish
        }

        // below 50%: a single lane every few beats, never one holding the lit circle or the next two
        void PressureLane(int circle)
        {
            var allowed = new List<int>();
            for (int lane = 0; lane < OracleLanes.Count; lane++)
            {
                bool blocked = false;
                for (int j = circle; j < Mathf.Min(ChainPositions.Count, circle + 3); j++) if (LaneTouchesCircle(lane, ChainPositions[j], SoakRadius)) blocked = true;
                if (!blocked) allowed.Add(lane);
            }
            if (allowed.Count == 0) return;
            int pick = allowed[rng.Next(allowed.Count)];
            for (int j = circle; j < Mathf.Min(ChainPositions.Count, circle + 3); j++) if (LaneTouchesCircle(pick, ChainPositions[j], SoakRadius)) PressureOverlaps++;
            PressureLog.Add((W.Tick, pick, circle));
            FireLane(pick, Beats(LaneWarn) + 1, false, "Carillon pressure");
        }

        // ---------------- Resonant Mark ----------------
        IEnumerable<int> CastMark()
        {
            Current = "Resonant Mark"; Rig.Pose(OraclePose.Point, Beats(1));
            var targets = new List<PlayerController>();
            foreach (var p in W.Players) if (!p.Down) targets.Add(p);
            targets.Sort((a, b) => Sim.Flat(b.transform.position - transform.position).magnitude.CompareTo(Sim.Flat(a.transform.position - transform.position).magnitude));
            int dur = Beats(Phase2 ? MarkBeatsLow : MarkBeatsHigh);
            for (int i = 0; i < Mathf.Min(MaxMarks, targets.Count); i++)
            {
                var ring = W.BossFx.Circle(MarkRingRadius); ring.Tint(MarkFill, MarkBorder); ring.Lock(); ring.Place(targets[i].transform.position, 0);
                marks.Add(new Mark { P = targets[i], Start = Age, End = Age + dur, Ring = ring });
                MarksCast++;
            }
            TurnTowards(YawToTarget(), 720);
            yield return Beats(2);
        }

        void StepMarks()
        {
            bool onBeat = Clock.LastBeatIndex >= 0;
            int half = (int)Math.Floor(Clock.Beat * 2); bool onHalf = half != lastHalfBeat; lastHalfBeat = half;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                if (!m.P || m.P.Down) { m.Ring.Dismiss(); marks.RemoveAt(i); continue; }
                var at = Sim.Flat(m.P.transform.position);
                m.Ring.Place(at, 0);
                float k = (Age - m.Start) / (float)Mathf.Max(1, m.End - m.Start);
                m.Ring.Progress(k);
                int left = m.End - Age;
                bool lastBeats = left <= Beats(4);
                // the ring tightens on every beat (and every half beat at the end): tick, tick, tick
                if (onBeat || (lastBeats && onHalf))
                {
                    Audio.Tick(); Fx.Rim(at, Mathf.Lerp(MarkRingRadius, 0.6f, k), new Color(1.6f, 0.9f, 2.4f), 0.18f);
                }
                if (Age >= m.End)
                {
                    m.Ring.Dismiss(); marks.RemoveAt(i); MarksDropped++;
                    Well(at, MarkWellRadius, Beats(MarkWellWarnBeats) + 1, MarkOrbs, Phase2 ? OrbSpeedLow : OrbSpeedHigh, 1, MarkWellDmg, "Resonant Mark");
                }
            }
        }
        void ClearMarks() { foreach (var m in marks) m.Ring?.Dismiss(); marks.Clear(); }

        // ---------------- Clapper Strike ----------------
        IEnumerable<int> Clapper()
        {
            foreach (var w in Approach(ClapperReach, WalkSpeed, 120)) yield return w;
            Current = "Clapper Strike"; ClapperCasts++;
            int windup = Beats(Phase2 ? ClapperBeatsLow : ClapperBeatsHigh) + 1;
            Rig.Pose(OraclePose.ClapperWindup, windup); Audio.Whoosh();
            var h = Cone("Clapper Strike", ClapperRadius, ClapperHalf, windup - Beats(1), windup, ClapperDmg, true, 200);
            h.Knockback = 2f; h.Tele.Tint(ConeFill, ConeBorder); h.Tele.FlashScale = 0.4f; h.Tele.SpentFast = true;
            h.OnResolve = hz => { Rig.Pose(OraclePose.ClapperSwing, 4); Release(1.2f); CameraShake.Shake(0.12f, 0.18f); Fx.Streak(hz.Origin, hz.Yaw, BodyRadius, ClapperRadius - BodyRadius); };
            LastClapper = h;
            for (int i = 0; i < windup; i++) { Windup(i / (float)windup); yield return 1; }
            Current = "Clapper Strike (recovery)";
            yield return Beats(2);
            Rig.Pose(OraclePose.Recover);
        }

        // ---------------- Grand Toll (50%) ----------------
        IEnumerable<int> GrandToll()
        {
            GatePending = false; HoldHp = true;
            Current = "Grand Toll"; Log("Grand Toll");
            foreach (var w in WalkTo(Vector3.zero, WalkSpeed * 1.4f, 240)) yield return w;
            transform.rotation = Quaternion.Euler(0, 180, 0);   // face north (-z), where the Metronome Court's metronome stands
            Current = "Grand Toll"; Rig.Pose(OraclePose.RingMetronome, Beats(1));
            Audio.Toll(); Fx.Rim(transform.position, 3f, new Color(1.0f, 1.5f, 2.2f), 0.4f);
            Clock.BeginTransition();
            int guard = 0;
            while (Clock.Transitioning && guard++ < 6 * Sim.Hz) { Windup(0.3f + 0.7f * guard / (3f * Beats(1))); yield return 1; }
            Phase2 = true; Release(1.5f); CameraShake.Shake(0.15f, 0.3f);
            Fx.Rim(transform.position, 6f, new Color(2.0f, 1.4f, 0.5f), 0.45f); Fx.Rim(transform.position, 10f, new Color(1.6f, 1.1f, 0.4f), 0.5f);
            Log("tempo -> " + Clock.State);
            yield return Beats(2);
            // the recital: one Called Changes cast at the new speed while his HP still holds
            foreach (var w in CalledChanges(false)) yield return w;
            HoldHp = false;
        }

        // ---------------- Last Bell (soft enrage) ----------------
        IEnumerable<int> LastBell()
        {
            Enraged = true; Log("Last Bell");
            foreach (var w in WalkTo(Vector3.zero, WalkSpeed * 1.4f, 240)) yield return w;
            Audio.Toll();
            while (true)
            {
                Current = "Last Bell"; Rig.Pose(OraclePose.StaffRaise, Beats(2));
                foreach (var w in AlignToBeat()) yield return w;
                int strike = Beats(LaneWarnBeats) + 1;
                for (int lane = 0; lane < OracleLanes.Count; lane++) FireLane(lane, strike, false, "Last Bell");
                yield return strike + Beats(1);
                // the berserk: a bigger group of wells falls between every volley
                DropGroup(WellsPerGroupLastBell, 4f, Beats(WellWarnBeats) + 1, OrbsLow, OrbSpeedLow, 1);
                yield return Beats(2);
            }
        }

        // ---------------- lifecycle ----------------
        protected override void Update()
        {
            base.Update();
            if (Rig) Rig.Glow = glowLevel;
        }

        protected override void OnDefeat()
        {
            ChainActive = false; ChainIndex = -1;
            ClearOrbs(); ClearMarks(); scheduled.Clear();
            Rig.Pose(OraclePose.Defeat);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy()
        {
            foreach (var o in orbs) if (o.View) Destroy(o.View.gameObject);
            while (orbPool.Count > 0) { var t = orbPool.Pop(); if (t) Destroy(t.gameObject); }
        }
    }
}
