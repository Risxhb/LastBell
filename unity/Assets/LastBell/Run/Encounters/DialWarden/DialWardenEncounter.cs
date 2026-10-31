using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Dial Warden: a practice-only Skyward Spire miniboss (docs/bosses/dial-warden.md). Four mechanics, no filler:
    //
    //   Constellation    stars appear, lines draw between them one per beat, then fade; after a gap the path fires in
    //                    drawn order, one beam per beat, each erupting its two stars. Below 50%: gold + silver paths,
    //                    silver firing in reverse, on alternating beats.
    //   Tip the Scales   the counterweight is thrown at a marked point; the group's average position must sit in the
    //                    target circle opposite it 4 beats after impact. 3 throws. Below 50%: two weights at once.
    //   Arrest the Hand  the one QTE. The hand sweeps a lap of the dial; whoever stands on a lit hour is braced and must
    //                    enter the shown directions (WASD / arrows), then parry with E as the hand arrives. A miss lets
    //                    the hand break through for a double-speed lap over the whole floor: 55 x misses to everyone.
    //   Mainspring       he slams; a ring wall with gaps runs out to the edge, rebounds with turned gaps, then the
    //                    centre implodes. Below 50%: two slams 2 beats apart.
    // 50%: HP holds while he shifts (120 -> 150 BPM) and casts the reversed Arrest the Hand. Midnight (5:00): gapless
    // waves every 6 beats. Every number is a first-pass, heroic-raid target for Medium, not approved balance.
    //
    // No ability text reaches the player: PhaseName and Lesson stay empty, names live only in the F7 diagnostics.
    public sealed class DialWardenEncounter : BossEncounter
    {
        public override string Id => "boss.dial-warden";
        public override string DisplayName => "The Dial Warden";
        public override string Lesson => "";
        public override bool Placeholder => !presenter || !presenter.HasModel;
        public override float NextPhaseFraction() => 0.49f;

        // ---------------- tunables ----------------
        public const float BpmHigh = 120f, BpmLow = 150f;
        public const int BeamDmg = 45, WeightDmg = 60, OffBalanceDmg = 35, OffBalanceStep = 15, HandDmg = 35, BreakDmg = 55, WaveDmg = 45, ImplosionDmg = 45;
        public const int EnrageTicks = 300 * Sim.Hz;
        public const float WalkSpeed = 3.4f;
        public const int StarsSingle = 6, StarsDouble = 5, Throws = 3, LeadBeatsMid = 4;
        public const float VOut = 7f, VIn = 9f, WaveSecondBeats = 2, MidnightEveryBeats = 6;
        public const int MaxLit = 4;
        public const int TellSingle = 6, TellDouble = 8, ComboGapBeats = 6;   // Mainspring warnings and the Constellation + Mainspring gap (beats)

        // palette (HDR)
        public static readonly Color StarFill = new Color(0.35f, 0.42f, 0.7f), StarBorder = new Color(1.4f, 1.6f, 2.6f),
            GoldLine = new Color(2.4f, 1.8f, 0.7f), SilverLine = new Color(1.5f, 1.8f, 2.4f),
            BeamFill = new Color(1.3f, 1.0f, 0.35f), BeamBorder = new Color(2.6f, 2.0f, 0.9f),
            WeightFill = new Color(1.3f, 0.55f, 0.12f), WeightBorder = new Color(2.6f, 1.3f, 0.35f),
            TargetFill = new Color(0.25f, 1.35f, 1.0f), TargetBorder = new Color(0.8f, 3.2f, 2.4f),
            PadFill = new Color(0.25f, 1.35f, 1.0f), PadBorder = new Color(0.8f, 3.2f, 2.4f),
            BracedFill = new Color(1.6f, 1.2f, 0.4f), BracedBorder = new Color(3.0f, 2.3f, 0.9f),
            HandFill = new Color(1.25f, 0.3f, 0.15f), HandBorder = new Color(2.4f, 0.8f, 0.3f),
            GapOut = new Color(0.3f, 1.4f, 1.1f), GapOutBorder = new Color(0.7f, 2.6f, 2.0f),
            ImplodeFill = new Color(1.3f, 0.3f, 0.2f), ImplodeBorder = new Color(2.4f, 0.7f, 0.4f);

        public DialWardenAudio Audio { get; private set; }
        public DialWardenFx Fx { get; private set; }
        CharacterPresenter presenter; public CharacterPresenter Presenter => presenter;
        Transform chainBone;

        // ---------------- state (read by tests and the F7 overlay) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public bool HoldHp { get; private set; }
        public bool Midnight { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;                 // dev/test hook: replaces the next rotation entry once
        public bool ForceEnrage;                  // dev/test hook: Midnight at the next block boundary
        public int? ForceLitCount;                // dev/test hook: lit hours for the next Arrest the Hand
        public int Seed = Environment.TickCount;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public float Bpm => Phase2 ? BpmLow : BpmHigh;
        public int BeatTicks => Mathf.RoundToInt(60f * Sim.Hz / Bpm);
        public int Tick => W != null ? W.Tick : 0;
        public int CombatTicks => Age;
        public int BeatIndex { get; private set; }
        public readonly Dictionary<PlayerController, IQteSource> QteSources = new Dictionary<PlayerController, IQteSource>();
        readonly KeyboardQte keyboard = new KeyboardQte();

        // hit counters
        public int BeamHits, WeightHits, OffBalanceHits, HandHits, BreakHits, WaveHits, ImplosionHits;

        enum Atk { Constellation, Scales, Arrest, Mainspring, ConstellationMainspring, ScalesConstellation, DoubleMainspring }
        static readonly Atk[] RotationHigh = { Atk.Constellation, Atk.Scales, Atk.Arrest, Atk.Mainspring };
        static readonly Atk[] RotationLow = { Atk.ConstellationMainspring, Atk.ScalesConstellation, Atk.Arrest, Atk.DoubleMainspring };
        int rotHigh, rotLow, beatAge;
        System.Random rng;
        string loopClip = "Idle";
        readonly List<(int at, Action act)> scheduled = new List<(int, Action)>();

        void Awake() { MaxHp = 3600; BodyRadius = 1.3f; HitHeight = 1.5f; MarkHeight = 3.3f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "DialWarden", 1.5f, 1f, 0, host => { BuildStandIn(host); return host; });
            if (presenter.HasModel) chainBone = FindDeep(presenter.transform, "chain.L");
            Audio = gameObject.AddComponent<DialWardenAudio>();
            Fx = gameObject.AddComponent<DialWardenFx>();
            gameObject.AddComponent<DialWardenQteHud>().Bind(this);
            PhaseName = "";
            DialWardenRoutes.Bound = CombatWorld.PlayerBound;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name || t.name.Replace("_", ".") == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
            return null;
        }

        // a labelled code-built stand-in, used only when Resources/Characters/DialWarden is missing
        static void BuildStandIn(Transform host)
        {
            var body = Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0, 1.5f, 0), Vector3.one * 1.8f, new Color(0.18f, 0.24f, 0.55f));
            body.name = "Dial Warden (STAND-IN, labelled placeholder)";
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 1.5f, 0.88f), new Vector3(1.1f, 0.04f, 1.1f), new Color(0.92f, 0.88f, 0.76f), 0, Quaternion.Euler(90, 0, 0));
            Prim.Make(PrimitiveKind.Cube, host, new Vector3(-1.1f, 1.1f, 0.9f), new Vector3(0.18f, 0.08f, 2.2f), new Color(0.85f, 0.66f, 0.32f), 0, Quaternion.Euler(-30, 0, 0));
            Prim.Make(PrimitiveKind.Sphere, host, new Vector3(1.4f, 0.6f, 0), Vector3.one * 0.5f, new Color(0.85f, 0.66f, 0.32f));
        }

        int Beats(float b) => Mathf.RoundToInt(b * BeatTicks);
        void Schedule(int ticks, Action a) => scheduled.Add((Age + Mathf.Max(1, ticks), a));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        void Clip(string name, int ticks) { if (presenter) presenter.Action(name, Mathf.Max(1, ticks)); }
        bool Hard => Level == Difficulty.Hard; bool Easy => Level == Difficulty.Easy;
        static Vector3 Centre => Vector3.zero;

        Hazard Live(Hazard h, Color fill, Color border, float flash = 0.35f)
        {
            W.Hazards.Spawn(h);
            h.Tele.Tint(fill, border); h.Tele.FlashScale = flash; h.Tele.SpentFast = true;
            return h;
        }
        Hazard Blow(string source, int dmg) => new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = transform.position, Radius = 999, Damage = dmg, Knockback = 0, Stun = 0 };

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Centre, WalkSpeed, 240)) yield return w;
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in Transition()) yield return w;
                if (!Midnight && (Age >= EnrageTicks || ForceEnrage)) foreach (var w in MidnightToll()) yield return w;

                Blocks++;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> body = atk switch
                {
                    Atk.Constellation => Constellation(false, false),
                    Atk.ConstellationMainspring => Constellation(true, true),
                    Atk.Scales => Scales(false, false),
                    Atk.ScalesConstellation => Scales(true, true),
                    Atk.Arrest => Arrest(!Phase2),
                    Atk.DoubleMainspring => Mainspring(2),
                    _ => Mainspring(1),
                };
                foreach (var w in body) yield return w;
                Current = "between blocks"; loopClip = "Idle";
                yield return Beats(2);
            }
        }

        protected override void StepAlways()
        {
            if (++beatAge >= BeatTicks) { beatAge = 0; BeatIndex++; if (!LapActive) Audio.Tick(false); }
            for (int i = scheduled.Count - 1; i >= 0; i--)
                if (Age >= scheduled[i].at) { var a = scheduled[i].act; scheduled.RemoveAt(i); a(); }
            StepFires();
            StepWaves();
            if (presenter) presenter.TickBoss(Current == "approach" || Current == "reposition" ? "Walk" : loopClip);
        }

        // practice phase row: the end of the 50% shift (slower tempo, HP released at half), without the shift and Arrest
        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n)
        {
            if (Phase2) return;
            Phase2 = true; GatePending = false; HoldHp = false; beatAge = 0;
        }

        protected override void OnHurt()
        {
            // the 50% shift: HP holds at half from the moment it is reached until the reversed Arrest the Hand ends
            if ((!Phase2 || HoldHp) && Hp * 2 < MaxHp) Hp = (MaxHp + 1) / 2;
        }

        IEnumerable<int> OnBeat() { int t = (BeatTicks - beatAge) % BeatTicks; if (t > 0) yield return t; }
        IEnumerable<int> ToCentre() { if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Centre, WalkSpeed * 1.4f, 240)) yield return w; }

        // ================= 1. Constellation =================
        public List<Vector3> Gold { get; private set; }
        public List<Vector3> Silver { get; private set; }
        public readonly List<(int tick, DialWardenRoutes.Fire fire)> FireLog = new List<(int, DialWardenRoutes.Fire)>();
        public int FireStart { get; private set; } = -1;
        public int ConstellationTries { get; private set; }
        public DialWardenRoutes.RouteResult LastRoute { get; private set; }
        public int ConstellationCasts { get; private set; }
        readonly List<(DialWardenRoutes.Fire f, int at)> pendingFires = new List<(DialWardenRoutes.Fire, int)>();
        readonly List<BellkeeperFx.Telegraph> starViews = new List<BellkeeperFx.Telegraph>();
        int GapBeats => Easy ? 6 : Hard ? 3 : 4;
        float PreFlashBeats => Easy ? 1f : Hard ? 0f : 0.5f;

        // Draws a path (or two), then fires it. withWave launches a Mainspring as the path starts firing.
        IEnumerable<int> Constellation(bool doubled, bool withWave)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = doubled ? "Constellation (gold + silver)" : "Constellation"; ConstellationCasts++;
            int beat = BeatTicks, gap = Beats(withWave ? Mathf.Max(GapBeats, ComboGapBeats) : GapBeats);
            List<DialWardenRoutes.Wave> waves = null;
            // roll until a walking route exists (the wave, if any, starts with the first segment)
            DialWardenRoutes.RouteResult res = default; int tries = 0;
            do
            {
                tries++;
                Gold = DialWardenRoutes.Stars(rng, doubled ? StarsDouble : StarsSingle, null);
                Silver = doubled ? DialWardenRoutes.Stars(rng, StarsDouble, Gold, 3.5f) : null;
                if (Gold == null || (doubled && Silver == null)) continue;
                var tl = new DialWardenRoutes.Timeline();
                tl.Fires.AddRange(DialWardenRoutes.Fires(Gold, Silver, beat));
                if (withWave) { waves = RollWaves(1, 0); tl.Waves.AddRange(waves); }
                res = DialWardenRoutes.Check(tl, gap);
            } while (!res.Ok && tries < 40);
            ConstellationTries = tries; LastRoute = res;
            if (Gold == null || (doubled && Silver == null)) yield break;

            Clip("SkyRaise", Beats(3)); loopClip = "SkyLoop";
            foreach (var w in OnBeat()) yield return w;
            // stars, dim, all at once
            foreach (var v in starViews) v.Dismiss(); starViews.Clear();
            foreach (var p in Gold) starViews.Add(Star(p));
            if (Silver != null) foreach (var p in Silver) starViews.Add(Star(p));
            // lines drawn one per beat (alternating gold/silver when doubled)
            var order = new List<(Vector3 a, Vector3 b, bool silver)>();
            int segs = Mathf.Max(Gold.Count, Silver?.Count ?? 0) - 1;
            for (int k = 0; k < segs; k++)
            {
                if (k + 1 < Gold.Count) order.Add((Gold[k], Gold[k + 1], false));
                if (Silver != null && k + 1 < Silver.Count) order.Add((Silver[k], Silver[k + 1], true));
            }
            var lines = new List<BellkeeperFx.Telegraph>();
            for (int i = 0; i < order.Count; i++)
            {
                var (a, b, silver) = order[i];
                var t = Line(a, b, silver ? SilverLine : GoldLine);
                lines.Add(t); Audio.Star(i);
                Windup(i / (float)order.Count);
                yield return beat;
            }
            // the lines fade (Easy keeps them as faint outlines), the stars stay; then the gap
            foreach (var t in lines) { if (Easy) t.Memory(); else t.Dismiss(); }
            loopClip = "Idle"; Release(0.4f);
            FireStart = Tick + gap;
            foreach (var f in DialWardenRoutes.Fires(Gold, Silver, beat)) pendingFires.Add((f, FireStart + f.Tick));
            if (withWave && waves != null) foreach (var wv in waves) LaunchWave(wv, FireStart, false);
            int last = FireStart; foreach (var pf in pendingFires) last = Mathf.Max(last, pf.at);
            while (Tick <= last + 1 || ActiveWaveCount > 0) { Current = withWave ? "Constellation + Mainspring" : Current; yield return 1; }
            foreach (var t in lines) t.Dismiss();
            foreach (var v in starViews) v.Dismiss(); starViews.Clear();
            Current = "Constellation (recovery)";
        }

        // a drawn line runs from star edge to star edge, so it never overlaps the star circles
        BellkeeperFx.Telegraph Line(Vector3 a, Vector3 b, Color c)
        {
            var d = Sim.Flat(b - a); float r = DialWardenRoutes.StarRadius + 0.15f;
            var t = W.BossFx.Lane(Mathf.Max(0.2f, d.magnitude - 2 * r), DialWardenRoutes.BeamWidth * 0.7f);
            t.Tint(c * 0.4f, c); t.Lock(); t.Place(a + d.normalized * r, Sim.YawTo(a, b, 0));
            return t;
        }

        BellkeeperFx.Telegraph Star(Vector3 p)
        {
            var t = W.BossFx.Circle(DialWardenRoutes.StarRadius); t.Tint(StarFill, StarBorder); t.Lock(); t.Place(p, 0);
            return t;
        }

        // segments re-flash PreFlashBeats before they fire, then the beam and both stars hit on the fire tick
        void StepFires()
        {
            for (int i = pendingFires.Count - 1; i >= 0; i--)
            {
                var (f, at) = pendingFires[i];
                int pre = Mathf.Max(1, Beats(PreFlashBeats));
                if (Tick < at - pre + 1 && !(Tick >= at)) continue;
                pendingFires.RemoveAt(i);
                int resolve = at - Tick + 1;
                var beamShape = DialWardenRoutes.BeamShape(f);
                var beam = new Hazard { Source = "Constellation beam", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = f.A, Yaw = beamShape.Yaw,
                    Length = beamShape.Length, Width = DialWardenRoutes.BeamWidth, LockAt = 0, ResolveAt = resolve, Damage = BeamDmg, Knockback = 0, Stun = 6 };
                beam.OnResolve = hz => { FireLog.Add((Tick, f)); BeamHits += hz.Hit.Count; Audio.Zap(); Release(0.3f); };
                Live(beam, BeamFill, f.Silver ? SilverLine : BeamBorder, 0.5f);
                foreach (var s in new[] { f.A, f.B })
                {
                    var star = new Hazard { Source = "Constellation star", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = s, Radius = DialWardenRoutes.StarRadius,
                        LockAt = 0, ResolveAt = resolve, Damage = BeamDmg, Knockback = 0, Stun = 6 };
                    star.OnResolve = hz => BeamHits += hz.Hit.Count;
                    Live(star, BeamFill, BeamBorder, 0.5f);
                }
            }
        }

        // ================= 2. Tip the Scales =================
        public bool ScalesActive { get; private set; }
        public int Throw { get; private set; }
        public int Balanced { get; private set; }
        public int Unbalanced { get; private set; }
        public readonly List<(int tick, Vector3 a, Vector3? b, Vector3 mirror, float zone)> ThrowLog = new List<(int, Vector3, Vector3?, Vector3, float)>();
        public readonly List<(int tick, Vector3 centroid, int standing, bool balanced, float dist)> WeighLog = new List<(int, Vector3, int, bool, float)>();
        public float Zone => Easy ? 3.5f : Hard ? 2.0f : 2.5f;
        int WeightWarnBeats => Easy ? 4 : 3;
        public const int WeighBeats = 4;

        IEnumerable<int> Scales(bool pair, bool withStars)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = pair ? "Tip the Scales (two weights)" : "Tip the Scales"; ScalesActive = true; Throw = 0;
            int missesThisCast = 0;
            Clip("Hoist", Beats(2)); loopClip = "HoistLoop"; Audio.Whirl();
            yield return Beats(2);
            // below 50% a single constellation is drawn first and fires as the first weight lands
            List<DialWardenRoutes.Fire> overlap = null; int overlapStart = -1;
            if (withStars)
            {
                Gold = DialWardenRoutes.Stars(rng, StarsSingle, null); Silver = null;
                var tl = new DialWardenRoutes.Timeline(); tl.Fires.AddRange(DialWardenRoutes.Fires(Gold, null, BeatTicks));
                LastRoute = DialWardenRoutes.Check(tl, Beats(WeightWarnBeats));
                foreach (var v in starViews) v.Dismiss(); starViews.Clear();
                foreach (var p in Gold) starViews.Add(Star(p));
                var lines = new List<BellkeeperFx.Telegraph>();
                for (int i = 0; i + 1 < Gold.Count; i++)
                {
                    var t = Line(Gold[i], Gold[i + 1], GoldLine);
                    lines.Add(t); Audio.Star(i); yield return BeatTicks;
                }
                foreach (var t in lines) { if (Easy) t.Memory(); else t.Dismiss(); }
                overlap = DialWardenRoutes.Fires(Gold, null, BeatTicks);
            }
            Vector3? prev = null;
            for (int i = 0; i < Throws; i++)
            {
                Throw = i + 1;
                int warn = Beats(WeightWarnBeats) + 1, weighIn = Beats(WeighBeats);
                Vector3 a = DialWardenRoutes.WeightSpot(rng, prev); Vector3? b = pair ? DialWardenRoutes.PairedSpot(rng, a) : (Vector3?)null;
                Vector3 m = DialWardenRoutes.Mirror(a, b);
                // with stars firing: keep the target's centre clear of anything firing around the weigh
                if (overlap != null && i == 0)
                {
                    overlapStart = Tick + warn - 1;
                    for (int guard = 0; guard < 60 && TargetCrossed(m, overlap, overlapStart, Tick + warn - 1 + weighIn); guard++)
                    { a = DialWardenRoutes.WeightSpot(rng, prev); b = pair ? DialWardenRoutes.PairedSpot(rng, a) : (Vector3?)null; m = DialWardenRoutes.Mirror(a, b); }
                }
                prev = a;
                ThrowLog.Add((Tick, a, b, m, Zone));
                var ha = WeightHazard(a, warn); Hazard hb = b.HasValue ? WeightHazard(b.Value, warn) : null;
                // the balance needle (centre to the target) and the target itself
                var needle = W.BossFx.Lane(Mathf.Max(0.5f, m.magnitude - Zone), 0.22f); needle.Memory(); needle.Lock(); needle.Place(Centre, Sim.YawTo(Centre, m, 0));
                var target = W.BossFx.Circle(Zone); target.Tint(TargetFill, TargetBorder); target.Lock(); target.Place(m, 0);
                Clip("WeightSlam", Mathf.RoundToInt(warn / 0.6f));
                var hand = transform.position + Vector3.up * 1.8f;
                int throwFrom = warn - Beats(1);
                for (int t = 0; t < warn - 1; t++)
                {
                    TurnTowards(Sim.YawTo(transform.position, a, transform.eulerAngles.y), 360);
                    float u = (t - throwFrom) / (float)Mathf.Max(1, warn - 1 - throwFrom);
                    if (u > 0) { Fx.Weight(0, hand, a, u); if (b.HasValue) Fx.Falling(1, b.Value, u); }
                    Windup(t / (float)warn);
                    yield return 1;
                }
                if (overlap != null && i == 0) foreach (var f in overlap) pendingFires.Add((f, overlapStart + 1 + f.Tick));
                yield return 1;   // impact tick (the hazards resolve here)
                Fx.Weight(0, hand, a, 1); if (b.HasValue) Fx.Falling(1, b.Value, 1);
                Audio.Thud(); CameraShake.Shake(0.14f, 0.2f); Release(1f);
                loopClip = "BalanceLoop";
                for (int t = 0; t < weighIn; t++) { target.Progress(t / (float)weighIn); yield return 1; }
                // the weigh: the average position of every standing player
                var c = DialWardenRoutes.Centroid(W.Players, out int n);
                float dist = Sim.Flat(c - m).magnitude; bool ok = n == 0 || dist <= Zone;
                WeighLog.Add((Tick, c, n, ok, dist));
                if (ok) { Balanced++; Audio.Balanced(); }
                else
                {
                    Unbalanced++; Audio.Unbalanced(); CameraShake.Shake(0.18f, 0.25f);
                    var blow = Blow("Tip the Scales (off balance)", OffBalanceDmg + OffBalanceStep * missesThisCast);
                    foreach (var p in W.Players) if (!p.Down) { blow.Hit.Add(p); p.ReceiveHit(blow); OffBalanceHits++; }
                    missesThisCast++;
                }
                needle.Dismiss(); target.Dismiss(); Fx.HideWeights();
                loopClip = "HoistLoop";
                yield return Beats(1);
            }
            while (pendingFires.Count > 0) yield return 1;
            yield return 2;
            foreach (var v in starViews) v.Dismiss(); starViews.Clear();
            ScalesActive = false; loopClip = "Idle"; Current = "Tip the Scales (recovery)";
        }

        Hazard WeightHazard(Vector3 at, int warn)
        {
            var h = new Hazard { Source = "Counterweight", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = DialWardenRoutes.WeightRadius,
                LockAt = 0, ResolveAt = warn, Damage = WeightDmg, Knockback = 0, Stun = 8 };
            h.OnResolve = hz => { foreach (var p in hz.Hit) WeightHits++; };
            return Live(h, WeightFill, WeightBorder, 0.4f);
        }

        // is the target's centre within 1.5 m of anything firing within half a beat of the weigh?
        bool TargetCrossed(Vector3 m, List<DialWardenRoutes.Fire> fires, int start, int weigh)
        {
            foreach (var f in fires)
            {
                int at = start + 1 + f.Tick; if (Mathf.Abs(at - weigh) > BeatTicks / 2) continue;
                for (float a = 0; a < 360; a += 45) if (DialWardenRoutes.FireHits(f, m + Sim.Dir(a) * 1.5f) || DialWardenRoutes.FireHits(f, m)) return true;
            }
            return false;
        }

        // ================= 3. Arrest the Hand =================
        public sealed class Lit
        {
            public int Step, Hour; public Vector3 Pos;
            public PlayerController Player; public ParryQte Qte; public bool Opened, Resolved, Caught, Missed, Unmanned;
            internal BellkeeperFx.Telegraph Pad;
        }
        public readonly List<Lit> LitList = new List<Lit>();
        public IEnumerable<int> LitHours { get { foreach (var l in LitList) yield return l.Hour; } }
        public bool LapActive { get; private set; }
        public bool Clockwise { get; private set; } = true;
        public float HandSteps => Mathf.Max(0, lapTicks) / (float)BeatTicks;
        public float HandYaw { get; private set; } = 180f;
        public bool Breaking { get; private set; }
        public int Catches { get; private set; }
        public int LapMisses { get; private set; }
        public int Laps { get; private set; }
        public readonly List<(int tick, int hour, string result, int keys, string reason)> ParryLog = new List<(int, int, string, int, string)>();
        int lapTicks, pauseLeft;
        readonly Dictionary<PlayerController, (ParryQte q, Lit lit)> braced = new Dictionary<PlayerController, (ParryQte, Lit)>();
        readonly Dictionary<PlayerController, ParryQte> lastPrompt = new Dictionary<PlayerController, ParryQte>();
        readonly List<QteKey> drained = new List<QteKey>();
        public int Keys => (Easy ? 2 : Hard ? 4 : 3) + (Phase2 && StandingCount <= 1 ? 1 : 0);
        public int LeadBeats => Easy ? 5 : Hard ? 3 : 4;
        public int Window => Easy ? 12 : Hard ? 5 : 8;
        int StandingCount { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
        public int LitCountFor(int players) => Mathf.Clamp(Mathf.CeilToInt(players / 2f) + (Phase2 && players > 1 ? 1 : 0), 1, MaxLit);

        // the QTE box asks this for the local player: the open prompt, or the last one for 0.6 s after it ended
        public ParryQte PromptFor(PlayerController p, out int sinceResult)
        {
            sinceResult = 0;
            if (p && braced.TryGetValue(p, out var b)) return b.q;
            if (p && lastPrompt.TryGetValue(p, out var q) && q.ResultTick >= 0) { sinceResult = Tick - q.ResultTick; return sinceResult <= 36 ? q : null; }
            return null;
        }

        IQteSource SourceFor(PlayerController p)
        {
            if (QteSources.TryGetValue(p, out var s)) return s;
            return p.Input is KeyboardMouseInput ? keyboard : null;
        }

        IEnumerable<int> Arrest(bool clockwise)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = clockwise ? "Arrest the Hand" : "Arrest the Hand (reversed)";
            Clockwise = clockwise; Laps++; LapMisses = 0;
            int beat = BeatTicks;
            int count = ForceLitCount ?? LitCountFor(StandingCount); ForceLitCount = null;
            var steps = DialWardenRoutes.LitSteps(rng, count);
            LitList.Clear();
            foreach (var s in steps) { int hr = DialWardenRoutes.HourAtStep(s, clockwise); LitList.Add(new Lit { Step = s, Hour = hr, Pos = DialWardenRoutes.HourPos(hr) }); }
            // the dial: 12 dim hour marks, the lit pads, the safe circle round him and the hand itself
            var views = new List<BellkeeperFx.Telegraph>();
            for (int h = 0; h < 12; h++) { if (LitList.Exists(l => l.Hour == h)) continue; var m = W.BossFx.Circle(0.45f); m.Memory(); m.Lock(); m.Place(DialWardenRoutes.HourPos(h), 0); views.Add(m); }
            foreach (var l in LitList) { l.Pad = W.BossFx.Circle(DialWardenRoutes.PadRadius); l.Pad.Tint(PadFill, PadBorder); l.Pad.Lock(); l.Pad.Place(l.Pos, 0); views.Add(l.Pad); }
            var safe = W.BossFx.Circle(DialWardenRoutes.SafeRadius - 0.3f); safe.Memory(); safe.Lock(); safe.Place(Centre, 0); views.Add(safe);
            float edge = CombatWorld.PlayerBound + 0.6f;
            var hand = W.BossFx.Lane(edge - DialWardenRoutes.SafeRadius, DialWardenRoutes.HandHalfWidth * 2); hand.Tint(HandFill, HandBorder); hand.Lock();
            HandYaw = DialWardenRoutes.HandYaw(0, clockwise);
            hand.Place(Sim.Dir(HandYaw) * DialWardenRoutes.SafeRadius, HandYaw); views.Add(hand);
            transform.rotation = Quaternion.Euler(0, HandYaw, 0);
            Clip("HandPlant", Beats(2)); loopClip = "HandLoop"; Audio.Toll();
            LapActive = true; Breaking = false;
            var hit = new HashSet<PlayerController>();
            lapTicks = -Beats(LeadBeatsMid); pauseLeft = 0;
            int lapEnd = 12 * beat;
            while (lapTicks < lapEnd)
            {
                // 1) the hand moves (unless held by a catch)
                float prevYaw = HandYaw; bool moved = false;
                if (pauseLeft > 0) { pauseLeft--; foreach (var kv in braced) kv.Value.q.Delay(1); }
                else
                {
                    lapTicks++; moved = lapTicks > 0;
                    if (lapTicks > 0 && lapTicks % beat == 0) Audio.Tick(true);
                    foreach (var l in LitList)
                        if (lapTicks == l.Step * beat)
                        {
                            if (l.Caught) { pauseLeft = beat; Clip("Caught", beat); Audio.Clang(); CameraShake.Shake(0.1f, 0.15f); }
                            else if (!l.Opened || l.Unmanned) MissAt(l, "unmanned");
                        }
                }
                HandYaw = DialWardenRoutes.HandYaw(HandSteps, clockwise);
                // 2) prompts open for whoever stands on a lit hour LeadBeats before the hand gets there
                foreach (var l in LitList)
                {
                    if (l.Opened || l.Resolved) continue;
                    int arriveIn = l.Step * beat - lapTicks;
                    if (arriveIn > Beats(LeadBeats)) continue;
                    var who = OnPad(l);
                    if (who != null) Open(l, who, Tick + arriveIn);
                    else if (arriveIn <= Beats(1)) { l.Opened = true; l.Unmanned = true; }   // too late to brace: it will be missed
                }
                // 3) inputs and windows
                foreach (var l in LitList)
                {
                    if (l.Qte == null || l.Resolved) continue;
                    var src = SourceFor(l.Player); drained.Clear(); src?.Drain(drained);
                    foreach (var k in drained) { int before = l.Qte.Index; l.Qte.Press(k, Tick); if (l.Qte.Open || l.Qte.Result == ParryQte.Outcome.Success) { if (l.Qte.Index > before) Audio.Key(); } }
                    l.Qte.Step(Tick);
                    if (l.Qte.Open) continue;
                    l.Resolved = true; Unbrace(l.Player);
                    ParryLog.Add((Tick, l.Hour, l.Qte.Result.ToString(), l.Qte.Sequence.Length, l.Qte.FailReason));
                    if (l.Qte.Result == ParryQte.Outcome.Success)
                    {
                        l.Caught = true; Catches++; hit.Add(l.Player);
                        l.Pad.Tint(BracedFill * 0.5f, BracedBorder);
                        if (lapTicks >= l.Step * beat) { lapTicks = l.Step * beat; pauseLeft = beat; Clip("Caught", beat); Audio.Clang(); CameraShake.Shake(0.1f, 0.15f); }
                    }
                    else
                    {
                        Audio.Bad(); MissAt(l, l.Qte.FailReason);
                        if (!hit.Contains(l.Player) && !l.Player.Down) { hit.Add(l.Player); var blow = HandBlow(HandDmg); l.Player.ReceiveHit(blow); HandHits++; }
                    }
                }
                HandYaw = DialWardenRoutes.HandYaw(HandSteps, clockwise);
                // 4) the sweep hits anyone between the safe circle and the edge it passes (not a braced parrier)
                if (moved)
                    foreach (var p in W.Players)
                    {
                        if (p.Down || hit.Contains(p) || braced.ContainsKey(p)) continue;
                        if (!DialWardenRoutes.HandCrosses(p.transform.position, prevYaw, HandYaw, DialWardenRoutes.SafeRadius)) continue;
                        hit.Add(p); p.ReceiveHit(HandBlow(HandDmg)); HandHits++;
                    }
                // presentation: the hand, his facing, pads filling as it nears
                hand.Place(Sim.Dir(HandYaw) * DialWardenRoutes.SafeRadius, HandYaw);
                transform.rotation = Quaternion.Euler(0, HandYaw, 0);
                foreach (var l in LitList) if (!l.Resolved && !l.Caught) l.Pad.Progress(Mathf.Clamp01(1 - (l.Step * beat - lapTicks) / (float)Beats(LeadBeats)));
                yield return 1;
            }
            foreach (var l in LitList) if (l.Player != null) Unbrace(l.Player);
            // a miss: the hand breaks through and runs one more lap at double speed over the whole floor
            if (LapMisses > 0)
            {
                Breaking = true; Current = "Arrest the Hand (broken through)";
                Clip("BreakThrough", Beats(2)); Audio.Crack(); CameraShake.Shake(0.2f, 0.3f);
                var brk = W.BossFx.Lane(edge, DialWardenRoutes.HandHalfWidth * 2.4f); brk.Tint(HandFill * 1.4f, HandBorder * 1.3f); brk.Lock(); views.Add(brk);
                var hitAll = new HashSet<PlayerController>();
                int dmg = BreakDmg * LapMisses, ticks = Beats(6);
                for (int t = 1; t <= ticks; t++)
                {
                    float prevYaw = HandYaw; HandYaw = DialWardenRoutes.HandYaw(12f * t / ticks, clockwise);
                    brk.Place(Centre, HandYaw); hand.Place(Sim.Dir(HandYaw) * DialWardenRoutes.SafeRadius, HandYaw);
                    transform.rotation = Quaternion.Euler(0, HandYaw, 0);
                    foreach (var p in W.Players)
                    {
                        if (p.Down || hitAll.Contains(p) || !DialWardenRoutes.HandCrosses(p.transform.position, prevYaw, HandYaw, 0)) continue;
                        hitAll.Add(p); p.ReceiveHit(HandBlow(dmg, "Arrest the Hand (broken through)")); BreakHits++;
                    }
                    if (t % Mathf.Max(1, beat / 2) == 0) Audio.Tick(true);
                    yield return 1;
                }
            }
            foreach (var v in views) v.Dismiss();
            LapActive = false; Breaking = false; loopClip = "Idle"; Current = "Arrest the Hand (recovery)";
        }

        Hazard HandBlow(int dmg, string src = "Arrest the Hand") =>
            new Hazard { Source = src, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = transform.position, Yaw = HandYaw, Length = 20, Width = 1.2f, Damage = dmg, Knockback = 0, Stun = 0 };

        PlayerController OnPad(Lit l)
        {
            PlayerController best = null; float bd = float.MaxValue;
            foreach (var p in W.Players)
            {
                if (p.Down || braced.ContainsKey(p)) continue;
                float d = Sim.Flat(p.transform.position - l.Pos).magnitude;
                if (d <= DialWardenRoutes.PadRadius + Hazard.Margin && d < bd) { bd = d; best = p; }
            }
            return best;
        }

        void Open(Lit l, PlayerController p, int arriveTick)
        {
            l.Opened = true; l.Player = p;
            l.Qte = new ParryQte(ParryQte.Roll(rng, Keys), Tick, arriveTick, Window);
            braced[p] = (l.Qte, l); lastPrompt[p] = l.Qte;
            SourceFor(p)?.Begin();
            p.MovementEnabled = false;   // braced: rooted on the hour until it resolves
            p.FaceNow(Centre);
            l.Pad.Tint(BracedFill, BracedBorder);
        }

        void Unbrace(PlayerController p)
        {
            if (p == null) return;
            braced.Remove(p);
            if (p) p.MovementEnabled = true;
        }
        void ReleaseAllBraces() { foreach (var p in new List<PlayerController>(braced.Keys)) Unbrace(p); }

        void MissAt(Lit l, string why)
        {
            if (l.Missed || l.Caught) return;
            l.Missed = true; l.Resolved = true; LapMisses++;
            if (l.Qte == null) ParryLog.Add((Tick, l.Hour, "Missed", 0, why));
            l.Pad.Tint(HandFill, HandBorder);
            Audio.Crack(); Clip("BreakThrough", Beats(2)); CameraShake.Shake(0.12f, 0.2f);
        }

        // ================= 4. Mainspring =================
        sealed class LiveWave { public DialWardenRoutes.Wave W; public int Origin, Slot; public HashSet<PlayerController> Hit = new HashSet<PlayerController>(); public bool Rebounded, Midnight; public List<BellkeeperFx.Telegraph> Views = new List<BellkeeperFx.Telegraph>(); }
        readonly List<LiveWave> liveWaves = new List<LiveWave>();
        public int ActiveWaveCount => liveWaves.Count;
        public int WavesLaunched { get; private set; }
        public readonly List<(int tick, DialWardenRoutes.Wave wave, int origin)> WaveLog = new List<(int, DialWardenRoutes.Wave, int)>();
        public int MainspringTries { get; private set; }
        int GapCount => Easy ? 4 : 3;
        float GapHalf => Easy ? 18f : Hard ? 13f : 15f;
        float GapTurn => Hard ? 90f : 60f;
        float SpeedScale => Bpm / BpmHigh;

        List<DialWardenRoutes.Wave> RollWaves(int count, int firstStart)
        {
            var list = new List<DialWardenRoutes.Wave>();
            for (int i = 0; i < count; i++)
            {
                float[] outG = i == 0 ? DialWardenRoutes.EvenGaps(rng, GapCount) : DialWardenRoutes.Turned(list[0].OutGaps, (rng.Next(2) == 0 ? -1 : 1) * (20f + 20f * (float)rng.NextDouble()));
                var w = new DialWardenRoutes.Wave
                {
                    Start = firstStart + i * Beats(WaveSecondBeats), OutGaps = outG, InGaps = DialWardenRoutes.Turned(outG, (rng.Next(2) == 0 ? -1 : 1) * GapTurn),
                    GapHalf = GapHalf, VOut = VOut * SpeedScale, VIn = VIn * SpeedScale, ImplosionDelay = Beats(Phase2 ? 1.5f : 1f),
                };
                list.Add(w);
            }
            return list;
        }

        IEnumerable<int> Mainspring(int count)
        {
            foreach (var w in ToCentre()) yield return w;
            Current = count > 1 ? "Mainspring (double)" : "Mainspring";
            int tell = Beats(count > 1 ? TellDouble : TellSingle);
            List<DialWardenRoutes.Wave> waves = null; DialWardenRoutes.RouteResult res = default; int tries = 0;
            do { tries++; waves = RollWaves(count, 0); var tl = new DialWardenRoutes.Timeline(); tl.Waves.AddRange(waves); res = DialWardenRoutes.Check(tl, tell); }
            while (!res.Ok && tries < 40);
            MainspringTries = tries; LastRoute = res;
            int impact = Tick + tell, slam = Beats(4);
            foreach (var wv in waves) LaunchWave(wv, impact, false);   // the gaps show for the whole warning
            for (int t = 0; t < tell; t++)
            {
                if (t == tell - slam) Clip("GroundSlam", Mathf.RoundToInt(slam / 0.59f));   // the hop and slam fill the last 4 beats
                Windup(t / (float)tell); yield return 1;
            }
            while (ActiveWaveCount > 0) yield return 1;
            Current = "Mainspring (recovery)";
        }

        // registers a wave: its gap sectors show now (outward bright, return dim), the wall leaves his body at origin + Start
        void LaunchWave(DialWardenRoutes.Wave w, int origin, bool midnight)
        {
            var lw = new LiveWave { W = w, Origin = origin, Midnight = midnight, Slot = WavesLaunched % 2 };
            WavesLaunched++; WaveLog.Add((Tick, w, origin));
            float edge = w.Edge;
            if (w.GapHalf > 0)
            {
                foreach (var g in w.OutGaps) { var c = W.BossFx.Cone(edge, w.GapHalf); c.Tint(GapOut * 0.5f, GapOutBorder); c.Lock(); c.Place(Centre, g); lw.Views.Add(c); }
                foreach (var g in w.InGaps) { var c = W.BossFx.Cone(edge, w.GapHalf); c.Memory(); c.Lock(); c.Place(Centre, g); lw.Views.Add(c); }
            }
            liveWaves.Add(lw);
        }

        void StepWaves()
        {
            for (int i = liveWaves.Count - 1; i >= 0; i--)
            {
                var lw = liveWaves[i]; var w = lw.W; int t = Tick - lw.Origin;   // timeline tick for this wave
                if (t == w.Start) { Audio.Slam(); Audio.Rumble(); CameraShake.Shake(0.2f, 0.3f); Release(1.2f); }
                if (w.At(t, out float r, out bool outward))
                {
                    if (!outward && !lw.Rebounded)
                    {
                        lw.Rebounded = true; Audio.Rumble();
                        if (!lw.Midnight) Clip("Collapse", Mathf.RoundToInt(w.InTicks / 0.69f));
                        // the implosion: its warning appears as the wave turns back
                        int resolve = (lw.Origin + w.Implosion) - Tick + 1;
                        var imp = new Hazard { Source = "Mainspring (implosion)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Centre,
                            Radius = DialWardenRoutes.ImplosionRadius, LockAt = 0, ResolveAt = resolve, Damage = ImplosionDmg, Knockback = 0, Stun = 6 };
                        imp.OnResolve = hz => { ImplosionHits += hz.Hit.Count; Audio.Implode(); CameraShake.Shake(0.16f, 0.25f); };
                        Live(imp, ImplodeFill, ImplodeBorder, 0.5f);
                    }
                    Fx.Wave(lw.Slot, Centre, r, outward ? w.OutGaps : w.InGaps, w.GapHalf, lw.Midnight ? DialWardenFx.WaveMidnight : outward ? DialWardenFx.WaveOut : DialWardenFx.WaveIn);
                    foreach (var p in W.Players)
                    {
                        if (p.Down || lw.Hit.Contains(p) || !DialWardenRoutes.WaveHits(w, t, p.transform.position)) continue;
                        lw.Hit.Add(p);
                        p.ReceiveHit(new Hazard { Source = lw.Midnight ? "Midnight" : "Mainspring", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Centre, Damage = WaveDmg, Knockback = 0, Stun = 6 });
                        WaveHits++;
                    }
                }
                else if (t > w.Start) Fx.HideWave(lw.Slot);
                if (t > w.Implosion)
                {
                    foreach (var v in lw.Views) v.Dismiss();
                    Fx.HideWave(lw.Slot); liveWaves.RemoveAt(i);
                }
            }
        }

        // ================= the 50% shift and Midnight =================
        IEnumerable<int> Transition()
        {
            GatePending = false; HoldHp = true;
            Current = "Shift"; Log("Shift");
            foreach (var w in ToCentre()) yield return w;
            Clip("PhaseShift", Beats(4)); Audio.Toll(); CameraShake.Shake(0.12f, 0.3f);
            yield return Beats(4);
            Phase2 = true; beatAge = 0; Release(1.5f);
            Log("tempo -> " + Bpm);
            yield return Beats(1);
            foreach (var w in Arrest(false)) yield return w;
            HoldHp = false;
        }

        IEnumerable<int> MidnightToll()
        {
            Midnight = true; Log("Midnight");
            foreach (var w in ToCentre()) yield return w;
            Audio.Toll();
            while (true)
            {
                Current = "Midnight";
                Clip("GroundSlam", Mathf.RoundToInt(Beats(2) / 0.59f));
                var w = new DialWardenRoutes.Wave { Start = Beats(2), GapHalf = 0, VOut = VOut * SpeedScale, VIn = VIn * SpeedScale, ImplosionDelay = Beats(1) };
                LaunchWave(w, Tick, true);
                yield return Beats(MidnightEveryBeats);
            }
        }

        // ---------------- lifecycle ----------------
        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel;
            if (W != null && (Dead || !W.CombatActive)) { if (Fx) Fx.HideWave(0); if (Fx) Fx.HideWave(1); }
            keyboard.Poll();
        }

        void LateUpdate()
        {
            // the counterweight leaves his hand while it is thrown (the clips keep animating the chain; this hides it)
            if (chainBone && Fx && Fx.WeightShown) chainBone.localScale = Vector3.one * 0.001f;
        }

        protected override void OnDefeat()
        {
            ReleaseAllBraces();
            liveWaves.Clear(); pendingFires.Clear(); scheduled.Clear();
            if (Fx) Fx.HideAll();
            LapActive = false; ScalesActive = false;
            if (presenter) presenter.Action("Defeat", 150);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy() { ReleaseAllBraces(); }
    }
}
