using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using R = LastBell.Run.BriarMetronomeRules;

namespace LastBell.Run
{
    // The Briar Metronome: a practice-only Verdant Chimes miniboss (docs/bosses/briar-metronome.md, a proposal). It fights
    // from the arena centre on a steady beat (108 BPM, 132 below 50%). Four abilities:
    //
    //   Pruning Tempo   a chain of cuts on the beat: Out (6 m circle), In (3.5 m to the edge), Across (90 degree cone aimed
    //                   at the farthest player when drawn; phase 2, Hard always). The current and the next cut are always shown.
    //   Hedgerow        Root Collar (3.5 m circle), then four hedges grow from 4 m to the wall, 3 m per beat (the first at
    //                   the farthest player), and stay as bramble (a burn) until the grafts are done: four pens.
    //   Season Grafts   one graft per pen (boss parts), each carrying a season numeral. Only its season's player can cut it
    //                   (by standing next to it). Uncut, it blooms: a 6 m burst plus 15 pollen to everyone. Then Rooted
    //                   (3.5 s, nothing live): the punish window.
    //   Ripening        a 4 m seed follows every player and bursts on everyone else (spread), leaving a bramble patch; the
    //                   metronome weight follows the farthest player and lands (two weights below 50%).
    //
    // Visuals: the rigged model (Characters/BriarMetronome, 9 Nov clips) when present, else a labelled primitive stand-in; no
    // audio. Each cut's strike frame lands on its own resolve (BossClipCues). Every warning wears the raid look (HazardSystem.RaidStyle).
    // No ability text reaches players (PhaseName and Lesson stay empty); names live in the F7 diagnostics.
    public sealed class BriarMetronomeEncounter : BossEncounter, IAllyBotJob
    {
        public override string Id => "boss.briar-metronome";
        public override string DisplayName => "The Briar Metronome";
        public override string Lesson => "";
        // the rigged model when Resources/Characters/BriarMetronome(.glb) is in the project, else the labelled stand-in
        public override bool Placeholder => !presenter || !presenter.HasModel;
        CharacterPresenter presenter; BossClipCues cues; public CharacterPresenter Presenter => presenter; public BossClipCues Cues => cues;
        public static readonly string[] ClipNames = { "Idle", "Walk", "TempoLoop", "CutIn", "CutOut", "CutAcross", "BladePlant", "PlantLoop",
            "MantleShake", "RootedLoop", "OrbWhirl", "WhirlLoop", "OrbThrow", "PhaseShift", "Defeat" };
        // clip lengths and strike frames (seconds), as keyed in briar_metronome_rig.py
        const float CutLen = 1.2f, CutStrike = 0.75f, PlantLen = 1.0f, PlantStrike = 0.6f, ShakeLen = 1.2f, WhirlLen = 0.8f, ThrowLen = 1.0f, ThrowRelease = 0.35f;
        float? faceYaw;   // the Across cut turns it toward its cone
        public override float NextPhaseFraction() => 0.49f;

        // ---------------- tunables (Medium) ----------------
        public const float BpmHigh = 108f, BpmLow = 132f;
        public const int CutDmg = 45, CollarDmg = 40, HedgeDmg = 40, BloomDmg = 50, PollenDmg = 15, SeedDmg = 30, WeightDmg = 60;
        public const float HedgeBurn = 25f, BrambleBurn = 20f;
        public const int CutsHigh = 4, CutsLow = 6;
        public const float FirstCutBeats = 7f, CutBeatsMedium = 3f, CutBeatsLow = 3.5f, CutBeatsEasy = 4f;   // Low: Medium/Hard below 50%
        public const float CollarBeatsMedium = 2.5f, CollarBeatsEasy = 3.5f, HedgeDelayBeats = 4f;
        public const int GraftHpMedium = 90, GraftHpEasy = 70;
        public const float BloomSeconds = 12f, BloomSecondsEasy = 15f, BloomSecondsShared = 3f;   // +3 s when someone owns two grafts
        public const float BloomWarnBeatsHigh = 3.5f, BloomWarnBeatsLow = 4.5f;
        public const float SeedFollowBeats = 4f, SeedLockBeatsMedium = 1.5f, SeedLockBeatsEasy = 2f, BrambleBeats = 8f;
        public const float WeightFollowBeatsHigh = 3f, WeightLockBeatsHigh = 3f, WeightFollowBeatsLow = 3.5f, WeightLockBeatsLow = 4f;
        public const int RootedTicks = 210, ShiftTicks = 120;
        public const float GapBeats = 1.5f;
        public const int MotifUsed = HazardSystem.RaidMotif;   // no Verdant Chimes motif yet (open question in the doc)

        // placeholder palette
        static readonly Color Bark = new Color(0.36f, 0.25f, 0.16f), Moss = new Color(0.30f, 0.46f, 0.22f), Ivory = new Color(0.93f, 0.89f, 0.78f),
            Brass = new Color(0.85f, 0.66f, 0.32f), Violet = new Color(0.55f, 0.36f, 0.75f), Leaf = new Color(0.22f, 0.5f, 0.2f), Bramble = new Color(0.24f, 0.32f, 0.14f);

        // ---------------- state (tests and the F7 overlay read it) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public string ForcedNext;                       // dev/test hook: replaces the next rotation entry once
        public int Seed = Environment.TickCount;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        bool Easy => Level == Difficulty.Easy; bool Hard => Level == Difficulty.Hard;
        public float Bpm => Phase2 ? BpmLow : BpmHigh;
        public int BeatTicks => Mathf.RoundToInt(60f * Sim.Hz / Bpm);
        public int Tick => W != null ? W.Tick : 0;
        public int BeatIndex { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public CombatWorld World => W;

        // per-ability counters and logs
        public int PruneCasts, HedgeCasts, GraftCasts, RipenCasts;
        public int CutsLanded, CutHits, CollarHits, HedgeHits, BloomHits, PollenHits, SeedHits, WeightHits, WeightsLanded, SeedsBurst;
        public int GraftsCut, GraftsBloomed, GraftHeld;
        public readonly List<(int tick, R.Cut cut, float yaw, int spacing)> CutLog = new List<(int, R.Cut, float, int)>();
        public readonly List<(int tick, float spoke0, Vector3 farthest)> HedgeLog = new List<(int, float, Vector3)>();
        public readonly List<(int tick, int season, int owner, bool cut, int ticksAlive)> GraftLog = new List<(int, int, int, bool, int)>();
        public readonly List<(int tick, Vector3 at, PlayerController target)> WeightLog = new List<(int, Vector3, PlayerController)>();
        public readonly List<(int start, int end, int liveAtStart)> RootedLog = new List<(int, int, int)>();
        public bool Rooted { get; private set; }
        public bool HedgesUp => hedgeBurns.Count > 0 || hedgeGrowing > 0;
        public float Spoke0 { get; private set; }
        public int GraftsAlive { get { int n = 0; foreach (var g in grafts) if (g.Alive) n++; return n; } }
        public List<Vector3> GraftSpots() { var l = new List<Vector3>(); foreach (var g in grafts) if (g.Alive) l.Add(g.Pos); return l; }

        enum Atk { Prune, Hedge, Ripen }
        static readonly Atk[] RotationHigh = { Atk.Prune, Atk.Hedge, Atk.Ripen };
        static readonly Atk[] RotationLow = { Atk.Prune, Atk.Ripen, Atk.Hedge };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());
        int rotHigh, rotLow, beatAge;
        System.Random rng;
        Transform body, pendulum, shears;
        readonly List<Transform> props = new List<Transform>();
        readonly Dictionary<PlayerController, Transform> numerals = new Dictionary<PlayerController, Transform>();

        void Awake() { MaxHp = 3200; BodyRadius = R.BodyRadius; HitHeight = 2.2f; MarkHeight = 4.8f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "BriarMetronome", 1.5f, 1f, 0, h => { BuildStandIn(h); return h; });
            cues = new BossClipCues(presenter);
            gameObject.AddComponent<BriarMetronomeHud>().Bind(this);
            PhaseName = "";
            R.Bound = CombatWorld.PlayerBound;
        }

        // primitive stand-in (placeholder): bark trunk, moss mantle, ivory mask, a brass pendulum in the chest, shears
        void BuildStandIn(Transform parent)
        {
            body = new GameObject("Briar Metronome (STAND-IN, labelled placeholder)").transform; body.SetParent(parent, false);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 1.4f, 0), new Vector3(1.6f, 1.4f, 1.6f), Bark);
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 3.0f, 0), new Vector3(2.3f, 1.2f, 2.3f), Moss);
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 3.5f, 0.55f), new Vector3(0.7f, 0.85f, 0.4f), Ivory);
            foreach (float a in new[] { -50f, 10f, 70f, 140f, 220f })
                Prim.Make(PrimitiveKind.Sphere, body, Sim.Dir(a) * 1.0f + Vector3.up * 3.2f, Vector3.one * 0.32f, Violet, 0.6f);
            pendulum = new GameObject("Pendulum").transform; pendulum.SetParent(body, false); pendulum.localPosition = new Vector3(0, 2.3f, 0.82f);
            Prim.Make(PrimitiveKind.Cylinder, pendulum, new Vector3(0, -0.55f, 0), new Vector3(0.06f, 0.55f, 0.06f), Brass, 1.5f);
            Prim.Make(PrimitiveKind.Sphere, pendulum, new Vector3(0, -1.1f, 0), Vector3.one * 0.32f, Brass, 2f);
            shears = new GameObject("Shears").transform; shears.SetParent(body, false); shears.localPosition = new Vector3(1.05f, 1.6f, 0.4f);
            Prim.Make(PrimitiveKind.Cube, shears, new Vector3(0, 0, 0.9f), new Vector3(0.12f, 0.08f, 1.9f), Ivory, 0.4f, Quaternion.Euler(0, 8, 0));
            Prim.Make(PrimitiveKind.Cube, shears, new Vector3(0, 0, 0.9f), new Vector3(0.12f, 0.08f, 1.9f), Ivory, 0.4f, Quaternion.Euler(0, -8, 0));
        }

        int Beats(float b) => Mathf.Max(1, Mathf.RoundToInt(b * BeatTicks));
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        int StandingCount { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
        static Vector3 Centre => Vector3.zero;

        Hazard Spawn(Hazard h)
        {
            if (h.Knockback == 1.5f) h.Knockback = 0;   // no knockback anywhere in this fight
            if (h.Stun == 15) h.Stun = 6;
            W.Hazards.Spawn(h);
            HazardSystem.RaidStyle(h, MotifUsed);
            return h;
        }
        bool IsLive(Hazard h) { if (h == null) return false; var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        PlayerController Farthest(Vector3 from, PlayerController not = null)
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players) { if (p.Down || p == not) continue; float d = Sim.Flat(p.transform.position - from).magnitude; if (d > bd) { bd = d; best = p; } }
            return best;
        }

        // ---------------- seasons ----------------
        // the player who owns season s now: the assigned one, or the next standing player in party order if they are down
        public PlayerController OwnerOf(int season)
        {
            int n = W.Players.Count; if (n == 0) return null;
            int i0 = R.OwnerIndex(season, n);
            for (int k = 0; k < n; k++) { var p = W.Players[(i0 + k) % n]; if (p && !p.Down) return p; }
            return null;
        }
        public List<int> SeasonsOf(PlayerController p)
        {
            var l = new List<int>(); int n = W.Players.Count; int i = W.Players.IndexOf(p);
            for (int s = 0; s < 4; s++) if (R.OwnerIndex(s, n) == i) l.Add(s);
            return l;
        }
        void LabelSeasons()
        {
            foreach (var p in W.Players)
            {
                if (!p || numerals.ContainsKey(p)) continue;
                var s = SeasonsOf(p); if (s.Count == 0) continue;
                numerals[p] = Label(p.transform, string.Join("+", s.ConvertAll(R.Numeral)), 2.6f, Ivory, 0.12f);
            }
        }
        static Transform Label(Transform parent, string text, float height, Color c, float size)
        {
            var go = new GameObject("Season numeral " + text); go.transform.SetParent(parent, false); go.transform.localPosition = Vector3.up * height;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            tm.anchor = TextAnchor.MiddleCenter; tm.characterSize = size; tm.fontSize = 64; tm.fontStyle = FontStyle.Bold; tm.color = c; tm.text = text;
            go.AddComponent<FaceCam>();
            return go.transform;
        }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            LabelSeasons();
            if (Sim.Flat(transform.position).magnitude > 0.3f) foreach (var w in WalkTo(Centre, 4.5f, 300)) yield return w;
            while (true)
            {
                if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
                if (GatePending) foreach (var w in Transition()) yield return w;
                Atk atk;
                if (ForcedNext != null && Enum.TryParse(ForcedNext, out Atk forced)) atk = forced;
                else atk = Phase2 ? RotationLow[rotLow++ % RotationLow.Length] : RotationHigh[rotHigh++ % RotationHigh.Length];
                ForcedNext = null;
                Log(atk.ToString());
                IEnumerable<int> body = atk switch { Atk.Prune => Prune(), Atk.Hedge => Hedgerow(), _ => Ripen() };
                foreach (var w in body) yield return w;
                Current = "between blocks";
                yield return Beats(GapBeats);
            }
        }

        protected override void StepAlways()
        {
            if (++beatAge >= BeatTicks) { beatAge = 0; BeatIndex++; }
            StepGrafts();
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
            if (faceYaw.HasValue) TurnTowards(faceYaw.Value, 240);
            cues?.Step(Current == "reposition");
        }
        protected override void OnHalt() { cues?.Clear(); if (cues != null) cues.Loop = "Idle"; faceYaw = null; }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (Phase2) return; Phase2 = true; GatePending = false; beatAge = 0; }

        // ================= 1. Pruning Tempo =================
        public List<R.Cut> LastChain { get; private set; }
        IEnumerable<int> Prune()
        {
            PruneCasts++;
            bool across = Phase2 || Hard;
            var chain = R.Chain(rng, Phase2 ? CutsLow : CutsHigh, across);
            LastChain = chain;
            Current = "Pruning Tempo (" + string.Join(" ", chain) + ")";
            int spacing = Beats(Easy ? CutBeatsEasy : Phase2 ? CutBeatsLow : CutBeatsMedium), lead = Beats(FirstCutBeats);
            int start = Age;
            var at = new int[chain.Count]; for (int k = 0; k < chain.Count; k++) at[k] = lead + k * spacing;
            // cut k is drawn when cut k-2 lands (the first two at once), so the current and the next are always on the floor;
            // it locks (and draws on top) when the cut before it lands
            int spawned = 0, total = at[chain.Count - 1];
            cues.LoopAt(0, "TempoLoop", Bpm / BpmHigh);   // swaying on the beat between cuts (keyed at 108 BPM)
            for (int t = 0; t <= total; t++)
            {
                while (spawned < chain.Count && (spawned < 2 || t >= at[spawned - 2]))
                {
                    int k = spawned++;
                    int lockAt = k == 0 ? 0 : at[k - 1] - t;
                    SpawnCut(chain[k], lockAt, at[k] - t, spacing);
                    cues.Strike(CutClip(chain[k]), at[k] - t, CutStrike, CutLen);   // each cut keyed from its own resolve
                }
                // the shears open on the beat; the pendulum is drawn by Update
                Windup((t % spacing) / (float)spacing);
                yield return 1;
            }
            Current = "Pruning Tempo (recovery)"; cues.Loop = "Idle"; faceYaw = null;
            yield return Beats(1);
        }
        static string CutClip(R.Cut c) => c == R.Cut.In ? "CutIn" : c == R.Cut.Out ? "CutOut" : "CutAcross";

        void SpawnCut(R.Cut cut, int lockAt, int resolveAt, int spacing)
        {
            var h = R.CutShape(cut, transform.position, transform.eulerAngles.y);
            h.Source = "Pruning Tempo (" + cut + ")"; h.Damage = CutDmg; h.LockAt = lockAt; h.ResolveAt = resolveAt;
            h.ClipRadius = CombatWorld.PlayerBound + 0.6f;
            if (cut == R.Cut.Across)
            {
                // aimed at the farthest player when it is drawn (two cuts ahead), then fixed: you see where it will cut
                var f = Farthest(transform.position); if (f) h.Yaw = Sim.YawTo(transform.position, f.transform.position, h.Yaw);
                faceYaw = h.Yaw;   // presentation: it turns to face the cone it will chop along
            }
            h.OnResolve = hz =>
            {
                CutsLanded++; CutHits += hz.Hit.Count; CutLog.Add((Tick, cut, hz.Yaw, spacing));
                Release(0.8f); CameraShake.Shake(0.07f, 0.1f);
                if (shears) shears.localRotation = Quaternion.Euler(0, cut == R.Cut.Out ? 60 : -30, 0);
            };
            Spawn(h);
        }

        // ================= 2. Hedgerow + 3. Season Grafts =================
        readonly List<Hazard> hedgeBurns = new List<Hazard>();
        readonly List<Transform> hedgeProps = new List<Transform>();
        int hedgeGrowing;

        IEnumerable<int> Hedgerow()
        {
            HedgeCasts++;
            Current = "Hedgerow";
            var f = Farthest(transform.position);
            Spoke0 = f ? Sim.YawTo(transform.position, f.transform.position, 0) : transform.eulerAngles.y;
            HedgeLog.Add((Tick, Spoke0, f ? f.transform.position : Vector3.zero));
            if (shears) shears.localRotation = Quaternion.Euler(70, 0, 0);
            // Root Collar
            int collar = Beats(Easy ? CollarBeatsEasy : CollarBeatsMedium);
            var c = new Hazard { Source = "Root Collar", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = transform.position, Radius = R.CollarRadius,
                LockAt = 0, ResolveAt = collar, Damage = CollarDmg };
            c.OnResolve = hz => { CollarHits += hz.Hit.Count; CameraShake.Shake(0.1f, 0.15f); };
            Spawn(c);
            cues.Strike("BladePlant", collar, PlantStrike, PlantLen); cues.LoopAt(collar, "PlantLoop");   // the blade lands with the Root Collar
            // four hedges, every section shown now, growing 3 m per beat from HedgeDelay beats
            var sections = R.Sections(CombatWorld.PlayerBound);
            int grow0 = Beats(HedgeDelayBeats);
            for (int k = 0; k < 4; k++)
            {
                float yaw = R.SpokeYaw(Spoke0, k);
                for (int s = 0; s < sections.Count; s++)
                {
                    var (from, len) = sections[s];
                    var h = new Hazard { Source = "Hedgerow", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = Sim.Flat(transform.position) + Sim.Dir(yaw) * from, Yaw = yaw,
                        Length = len, Width = R.HedgeWidth, LockAt = 0, ResolveAt = grow0 + s * BeatTicks, Damage = HedgeDmg, ClipRadius = CombatWorld.PlayerBound + 0.6f };
                    hedgeGrowing++;
                    h.OnResolve = hz => { HedgeHits += hz.Hit.Count; hedgeGrowing--; GrowBramble(hz); };
                    Spawn(h);
                }
            }
            for (int t = 0; t < grow0 + (sections.Count - 1) * BeatTicks + 1; t++) { Windup(t / (float)(grow0 + sections.Count * BeatTicks)); yield return 1; }
            while (hedgeGrowing > 0) yield return 1;
            // the grafts, one per pen
            foreach (var w in Grafts()) yield return w;
            // the hedges wither, it pulls its shears out: Rooted (nothing live)
            foreach (var h in hedgeBurns) W.Hazards.Cancel(h);
            hedgeBurns.Clear(); ClearHedgeProps();
            yield return 1;
            for (int guard = 0; guard < 600 && W.Hazards.Count > 0; guard++) yield return 1;
            Current = "Rooted"; Rooted = true; Release(1.2f);
            cues.Clear(); cues.Loop = "RootedLoop";
            int from0 = Tick, live0 = W.Hazards.Count;
            yield return RootedTicks;
            RootedLog.Add((from0, Tick, live0)); Rooted = false; cues.Loop = "Idle";
            if (shears) shears.localRotation = Quaternion.identity;
            Current = "Hedgerow (recovery)";
        }

        void GrowBramble(Hazard section)
        {
            var b = new Hazard { Source = "Bramble hedge", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = section.Origin, Yaw = section.Yaw, Length = section.Length,
                Width = section.Width, LockAt = 0, ResolveAt = Sim.Ticks(60), BurnPerSecond = HedgeBurn, ClipRadius = section.ClipRadius };
            Spawn(b); hedgeBurns.Add(b);
            var mid = section.Origin + Sim.Dir(section.Yaw) * (section.Length / 2) + Vector3.up * 0.45f;
            var view = Prim.Make(PrimitiveKind.Cube, transform.parent, mid, new Vector3(section.Width * 0.8f, 0.9f, section.Length), Bramble, 0f, Quaternion.Euler(0, section.Yaw, 0));
            view.name = "Hedge (placeholder)"; hedgeProps.Add(view);
        }
        void ClearHedgeProps() { foreach (var t in hedgeProps) if (t) Destroy(t.gameObject); hedgeProps.Clear(); }

        sealed class Graft
        {
            public int Season, Pen, Born, BloomAt; public Vector3 Pos; public float Hp; public bool Alive = true, Blooming;
            public readonly int Net = NextNetPropId--;   // LAN prop id (stable while the graft's view lives)
            public BossPart Part; public Hazard Timer, Bloom; public Transform View;
        }
        readonly List<Graft> grafts = new List<Graft>();
        public int GraftHp => Easy ? GraftHpEasy : GraftHpMedium;
        public int BloomTicks
        {
            get
            {
                float s = Easy ? BloomSecondsEasy : BloomSeconds;
                if (W.Players.Count <= 3) s += BloomSecondsShared;   // someone owns two grafts (solo: both)
                return Sim.Ticks(s);
            }
        }

        IEnumerable<int> Grafts()
        {
            GraftCasts++;
            Current = "Season Grafts";
            cues.Play("MantleShake", Mathf.RoundToInt(ShakeLen * Sim.Hz));   // it shakes its mantle and the grafts sprout
            int n = W.Players.Count;
            foreach (int season in R.GraftSeasons(n)) Sprout(season);
            while (true)
            {
                bool busy = false;
                foreach (var g in grafts) if (g.Alive || IsLive(g.Bloom)) busy = true;
                if (!busy) break;
                yield return 1;
            }
            grafts.Clear();
        }

        void Sprout(int season)
        {
            var g = new Graft { Season = season, Pen = season, Pos = R.GraftSpot(transform.position, Spoke0, season), Born = Age, Hp = GraftHp };
            g.BloomAt = Age + BloomTicks;
            g.View = GraftView(season, g.Pos + Vector3.up * 0.7f);
            // the bloom timer: a filling ring at its base (nobody is hit by it; Affects nobody)
            g.Timer = Spawn(new Hazard { Source = "Season Graft", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = g.Pos, Radius = R.GraftBody,
                LockAt = 0, ResolveAt = BloomTicks, Damage = 0, HitOnResolve = false, Affects = _ => false });
            g.Part = new BossPart { Name = "Season Graft " + R.Numeral(season), Pos = g.Pos, BodyRadius = R.GraftBody, HitHeight = 0.8f };
            g.Part.TakeHit = (dmg, contact) =>
            {
                if (!g.Alive || g.Blooming) return 0;
                var owner = OwnerOf(g.Season);
                if (owner == null || Sim.Flat(owner.transform.position - g.Pos).magnitude > R.OwnerReach)
                { GraftHeld++; RunHud.Float(contact + Vector3.up * 0.5f, "held", new Color(0.7f, 0.85f, 1f)); return 0; }
                int dealt = Mathf.Min(dmg, Mathf.CeilToInt(g.Hp)); g.Hp -= dealt;
                RunHud.Float(contact + Vector3.up * 0.5f, dealt.ToString(), new Color(0.6f, 1f, 0.6f));
                if (Solid && dealt > 0) TakeHit(dealt, transform.position + Vector3.up * HitHeight, false);
                return dealt;
            };
            g.Part.OnCleared = _ => { if (g.View) Destroy(g.View.gameObject); };
            W.Parts.Add(g.Part);
            grafts.Add(g);
        }

        Transform GraftView(int season, Vector3 at)
        {
            var v = Prim.Make(PrimitiveKind.Cone, transform.parent, at, new Vector3(0.9f, 1.4f, 0.9f), Leaf, 0.5f);
            v.name = $"Season graft {R.Numeral(season)} (placeholder)";
            Prim.Make(PrimitiveKind.Sphere, v, new Vector3(0, -0.45f, 0), new Vector3(1.8f, 0.4f, 1.8f), Moss);
            Label(v, R.Numeral(season), 1.2f, Ivory, 0.14f);
            return v;
        }

        void StepGrafts()
        {
            if (W == null) return;
            foreach (var g in grafts)
            {
                if (!g.Alive) continue;
                if (g.Hp <= 0)
                {
                    g.Alive = false; g.Part.Alive = false; W.Parts.Remove(g.Part); W.Hazards.Cancel(g.Timer); GraftsCut++;
                    var o = OwnerOf(g.Season);
                    GraftLog.Add((Tick, g.Season, o ? W.Players.IndexOf(o) : -1, true, Age - g.Born));
                    RunHud.Float(g.Pos + Vector3.up * 1.8f, "CUT", new Color(0.6f, 1f, 0.6f));
                    if (g.View) Destroy(g.View.gameObject);
                    continue;
                }
                if (!g.Blooming && Age >= g.BloomAt)
                {
                    // out of time: it stops taking cuts and bursts after a short warning
                    g.Blooming = true; g.Part.Alive = false; W.Parts.Remove(g.Part);
                    var b = new Hazard { Source = "Graft Bloom", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = g.Pos, Radius = R.BloomRadius,
                        LockAt = 0, ResolveAt = Beats(Phase2 ? BloomWarnBeatsLow : BloomWarnBeatsHigh), Damage = BloomDmg };
                    b.OnResolve = hz =>
                    {
                        BloomHits += hz.Hit.Count; GraftsBloomed++; g.Alive = false;
                        var o = OwnerOf(g.Season);
                        GraftLog.Add((Tick, g.Season, o ? W.Players.IndexOf(o) : -1, false, Age - g.Born));
                        // pollen: every standing player
                        var pollen = new Hazard { Source = "Pollen", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = g.Pos, Radius = 999, Damage = PollenDmg, Knockback = 0, Stun = 0, Flinch = false };
                        foreach (var p in W.Players) { if (p.Down) continue; pollen.Hit.Add(p); p.ReceiveHit(pollen); PollenHits++; }
                        if (g.View) Destroy(g.View.gameObject);
                        CameraShake.Shake(0.12f, 0.2f);
                    };
                    g.Bloom = Spawn(b);
                }
            }
        }
        void ClearGrafts()
        {
            foreach (var g in grafts) { if (g.View) Destroy(g.View.gameObject); if (g.Part != null) { g.Part.Alive = false; W.Parts.Remove(g.Part); } }
            grafts.Clear();
        }

        // bots (AllyBot): walk to your own nearest uncut graft by the inner path, then cut it
        public bool BotJob(PlayerController me, out Vector3 stand, out Vector3 aim)
        {
            stand = aim = Vector3.zero;
            if (Dead || grafts.Count == 0) return false;
            Graft best = null; float bd = float.MaxValue;
            foreach (var g in grafts)
            {
                if (!g.Alive || g.Blooming || OwnerOf(g.Season) != me) continue;
                float d = Sim.Flat(g.Pos - me.transform.position).magnitude + (R.PenOf(transform.position, Spoke0, me.transform.position) == g.Pen ? 0 : 20);
                if (d < bd) { bd = d; best = g; }
            }
            if (best == null) return false;
            var outward = Sim.Flat(best.Pos - transform.position).normalized;
            var spot = best.Pos + outward * 1.6f;
            stand = HedgesUp ? R.Waypoint(transform.position, Spoke0, me.transform.position, spot, best.Pen) : spot;
            aim = best.Pos + Vector3.up * 0.8f;
            return true;
        }

        // ================= 4. Ripening =================
        IEnumerable<int> Ripen()
        {
            RipenCasts++;
            Current = Phase2 ? "Ripening (two weights)" : "Ripening";
            int follow = Beats(SeedFollowBeats), burst = follow + Beats(Easy ? SeedLockBeatsEasy : SeedLockBeatsMedium);
            var seeds = new List<Hazard>();
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                var carrier = p;
                var s = new Hazard { Source = "Ripening seed", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position), Radius = R.SeedRadius,
                    LockAt = follow, ResolveAt = burst, Damage = SeedDmg, Affects = q => q != carrier };
                s.Track = hz => { if (carrier && !carrier.Down) hz.Origin = Sim.Flat(carrier.transform.position); };
                s.OnResolve = hz =>
                {
                    SeedHits += hz.Hit.Count; SeedsBurst++;
                    var b = new Hazard { Source = "Bramble patch", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = hz.Origin, Radius = R.BrambleRadius,
                        LockAt = 0, ResolveAt = Beats(BrambleBeats), BurnPerSecond = BrambleBurn };
                    Spawn(b);
                };
                seeds.Add(Spawn(s));
            }
            var target = Farthest(transform.position);
            var first = Weight(target, Phase2);
            int guard = 0;
            while ((IsLive(first) || seeds.Exists(IsLive)) && guard++ < 1200) { Windup(0.5f); yield return 1; }
            if (Phase2)
            {
                var at = WeightLog.Count > 0 ? WeightLog[WeightLog.Count - 1].at : transform.position;
                var second = Weight(Farthest(at), true);
                for (guard = 0; IsLive(second) && guard < 1200; guard++) yield return 1;
            }
            Current = "Ripening (recovery)";
            yield return Beats(1);
        }

        Hazard Weight(PlayerController target, bool low)
        {
            if (target == null) return null;
            int follow = Beats(low ? WeightFollowBeatsLow : WeightFollowBeatsHigh), land = follow + Beats(low ? WeightLockBeatsLow : WeightLockBeatsHigh);
            // the orb whirled overhead while the weight follows, hurled on the lock
            cues.Play("OrbWhirl", Mathf.RoundToInt(WhirlLen * Sim.Hz)); cues.LoopAt(0, "WhirlLoop");
            cues.Strike("OrbThrow", follow, ThrowRelease, ThrowLen); cues.LoopAt(follow, "Idle");
            var h = new Hazard { Source = "Metronome Weight", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(target.transform.position), Radius = R.WeightRadius,
                LockAt = follow, ResolveAt = land, Damage = WeightDmg };
            h.Track = hz => { if (target && !target.Down) hz.Origin = Vector3.MoveTowards(hz.Origin, Sim.Flat(target.transform.position), R.WeightFollow * Sim.Dt); };
            h.OnResolve = hz => { WeightHits += hz.Hit.Count; WeightsLanded++; WeightLog.Add((Tick, hz.Origin, target)); CameraShake.Shake(0.15f, 0.2f); Release(1f); };
            return Spawn(h);
        }

        // ================= the 50% shift =================
        IEnumerable<int> Transition()
        {
            GatePending = false;
            Current = "Shift"; Log("Shift"); ShiftStart = Tick; HpAtShiftStart = Hp;
            CameraShake.Shake(0.1f, 0.3f);
            cues.Clear(); faceYaw = null; cues.Play("PhaseShift", ShiftTicks); cues.Loop = "Idle";
            yield return ShiftTicks;   // harmless: nothing is spawned, it stays damageable
            Phase2 = true; beatAge = 0; Release(1.5f); ShiftEnd = Tick;
            Log("tempo -> " + Bpm);
        }

        // ---------------- presentation ----------------
        protected override void Update()
        {
            base.Update();
            if (pendulum)
            {
                // one full swing per two beats, reaching the side on each beat
                float beats = (BeatIndex + beatAge / (float)Mathf.Max(1, BeatTicks)) * Mathf.PI;
                pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(beats + Mathf.PI / 2) * 28f);
            }
            if (body) body.localScale = Vector3.one * (1 + 0.04f * (glowLevel - 1));
            if (presenter) presenter.GlowLevel = glowLevel;
            foreach (var kv in numerals) if (kv.Value) kv.Value.gameObject.SetActive(kv.Key && !kv.Key.Down);
        }

        protected override void OnDefeat()
        {
            ClearGrafts(); ClearHedgeProps(); hedgeBurns.Clear(); hedgeGrowing = 0; Rooted = false;
            cues?.Clear(); faceYaw = null;
            if (presenter) presenter.Action("Defeat", 150);
            W.Fx.Crescendo(transform.position, true, 2, true);
            if (body) body.localRotation = Quaternion.Euler(0, 0, 12);
        }

        // ---------------- LAN ----------------
        // Host: grafts travel as props (kind = season), the beat, tempo, shears pose and each player's seasons (by lobby id:
        // the host's and a client's player order differ) as Look numbers. Client replica (never stepped): the pendulum
        // swings on the host's beat, the shears, body pulse and defeat tilt follow, the season numerals hang over the
        // replica players, and every "Bramble hedge" stand-in gets its hedge wall.
        // Look: [0] beat (index + fraction)  [1] BPM  [2..4] shears local euler  [5] numerals shown  [6] n  then n x (id, season bits)
        public override void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into)
        {
            foreach (var g in grafts) if (g.View) into.Add(NetProp(g.Net, (byte)Mathf.Clamp(g.Season, 0, 3), g.View.position));
        }
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            var l = into.Look;
            l.Add(BeatIndex + beatAge / (float)Mathf.Max(1, BeatTicks)); l.Add(Bpm);
            var e = shears ? shears.localEulerAngles : Vector3.zero; l.Add(e.x); l.Add(e.y); l.Add(e.z);
            l.Add(numerals.Count > 0 ? 1 : 0);
            int at = l.Count; l.Add(0); int n = 0;
            if (numerals.Count > 0 && NetIdOf != null && W != null)
                foreach (var p in W.Players)
                {
                    if (!p || n >= 8) continue;
                    int id = NetIdOf(p); if (id < 0) continue;
                    int bits = 0; foreach (int s in SeasonsOf(p)) bits |= 1 << s;
                    if (bits == 0) continue;
                    l.Add(id); l.Add(bits); n++;
                }
            l[at] = n;
        }
        public override Transform MakeNetProp(byte kind)
        {
            if (kind > 3) return null;
            NetGraftsMade++;
            return GraftView(kind, Vector3.zero);
        }

        // client-side state and counters (two-process smoke tests read these)
        public int NetFrames { get; private set; }
        public int NetGraftsMade { get; private set; }
        public int NetHedgesMade { get; private set; }
        public int NetNumeralViews => replicaNumerals.Count;
        double replicaBeat; bool replicaBeatSet;
        readonly Dictionary<PlayerController, (Transform view, int bits)> replicaNumerals = new Dictionary<PlayerController, (Transform, int)>();
        readonly Dictionary<Hazard, Transform> replicaHedges = new Dictionary<Hazard, Transform>();
        static readonly List<PlayerController> scratchPlayers = new List<PlayerController>();
        static readonly List<Hazard> scratchHazards = new List<Hazard>();
        static readonly HashSet<PlayerController> scratchSeen = new HashSet<PlayerController>();

        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            NetFrames++;
            var l = state.Look;
            // the pendulum runs on at the host's tempo and eases toward the host's beat (snapshots come at 30 Hz)
            if (l.Count > 1)
            {
                double target = l[0], bpm = Mathf.Max(1f, l[1]);
                if (!replicaBeatSet || System.Math.Abs(target - replicaBeat) > 0.5) { replicaBeat = target; replicaBeatSet = true; }
                else { replicaBeat += dt * bpm / 60.0; replicaBeat += (target - replicaBeat) * (1 - System.Math.Exp(-dt / 0.5)); }
            }
            if (pendulum && replicaBeatSet)
                pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin((float)(replicaBeat * System.Math.PI) + Mathf.PI / 2) * 28f);
            if (shears && l.Count > 4) shears.localRotation = Quaternion.Euler(l[2], l[3], l[4]);
            if (body)
            {
                body.localScale = Vector3.one * (1 + 0.04f * (state.Glow - 1));
                body.localRotation = Dead ? Quaternion.Euler(0, 0, 12) : Quaternion.identity;
            }
            ReplicaNumerals(l);
            ReplicaHedges();
        }

        void ReplicaNumerals(List<float> l)
        {
            scratchSeen.Clear();
            bool shown = l.Count > 6 && l[5] > 0.5f;
            if (shown && NetPlayerOf != null)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(l[6]), 0, 8);
                for (int k = 0; k < n && 8 + 2 * k < l.Count; k++)
                {
                    var p = NetPlayerOf(Mathf.RoundToInt(l[7 + 2 * k])); int bits = Mathf.RoundToInt(l[8 + 2 * k]);
                    if (!p || bits == 0) continue;
                    scratchSeen.Add(p);
                    replicaNumerals.TryGetValue(p, out var e);
                    if (!e.view || e.bits != bits)
                    {
                        if (e.view) Destroy(e.view.gameObject);
                        var seasons = new List<string>(); for (int s = 0; s < 4; s++) if ((bits & (1 << s)) != 0) seasons.Add(R.Numeral(s));
                        e.view = Label(p.transform, string.Join("+", seasons), 2.6f, Ivory, 0.12f); e.bits = bits;
                        replicaNumerals[p] = e;
                    }
                    e.view.gameObject.SetActive(!p.Down);
                }
            }
            scratchPlayers.Clear();
            foreach (var kv in replicaNumerals) if (!kv.Key || !scratchSeen.Contains(kv.Key)) scratchPlayers.Add(kv.Key);
            foreach (var p in scratchPlayers) { if (replicaNumerals[p].view) Destroy(replicaNumerals[p].view.gameObject); replicaNumerals.Remove(p); }
        }

        // one hedge wall per live "Bramble hedge" stand-in (origin, yaw, length and width come with it)
        void ReplicaHedges()
        {
            if (W == null || W.Hazards == null) return;
            var live = W.Hazards.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h == null || !h.Replica || h.Cancelled || h.Source != "Bramble hedge" || replicaHedges.ContainsKey(h)) continue;
                var mid = h.Origin + Sim.Dir(h.Yaw) * (h.Length / 2) + Vector3.up * 0.45f;
                var view = Prim.Make(PrimitiveKind.Cube, transform.parent, mid, new Vector3(h.Width * 0.8f, 0.9f, h.Length), Bramble, 0f, Quaternion.Euler(0, h.Yaw, 0));
                view.name = "Hedge (placeholder, LAN)"; replicaHedges[h] = view; NetHedgesMade++;
            }
            scratchHazards.Clear();
            foreach (var kv in replicaHedges)
            {
                bool on = !kv.Key.Cancelled;
                if (on) { on = false; for (int i = 0; i < live.Count; i++) if (live[i] == kv.Key) { on = true; break; } }
                if (!on) scratchHazards.Add(kv.Key);
            }
            foreach (var h in scratchHazards) { if (replicaHedges[h]) Destroy(replicaHedges[h].gameObject); replicaHedges.Remove(h); }
        }

        void ClearReplica()
        {
            foreach (var kv in replicaNumerals) if (kv.Value.view) Destroy(kv.Value.view.gameObject);
            replicaNumerals.Clear();
            foreach (var kv in replicaHedges) if (kv.Value) Destroy(kv.Value.gameObject);
            replicaHedges.Clear();
        }

        sealed class FaceCam : MonoBehaviour
        {
            void LateUpdate() { var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position); }
        }

        void OnDestroy()
        {
            ClearHedgeProps();
            foreach (var g in grafts) if (g.View) Destroy(g.View.gameObject);
            foreach (var kv in numerals) if (kv.Value) Destroy(kv.Value.gameObject);
            numerals.Clear();
            ClearReplica();
        }
    }
}
