using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using R = LastBell.Run.VesperSeedkeeperRules;

namespace LastBell.Run
{
    // The Vesper Seedkeeper: a practice-only Verdant Chimes miniboss (docs/bosses/vesper-seedkeeper.md, a proposal).
    // A floating dusk lantern-vessel; light and dark at the vesper hour. Four abilities:
    //
    //   Vesper Cords   N cords (one per standing player) tie it to stakes near the edge. When they snap, each cord whips
    //                  along its length (lane) and every stake with nobody on it surges through the whole party.
    //                  Below 50%: a second set between the first, snapping a walk later.
    //   Seedgift       the farthest player (two at 4 players) carries a seed: a shared soak that follows them, locks and
    //                  lands; where it lands a Dusk Bloom is planted (never in the vessel's shadow).
    //   Nightfall      always after Seedgift: everyone outside a lit bloom is hit; a bloom holds as many as planted it
    //                  (one more gutters it); the umbra under the vessel lands with it. Hard: Late Bloom (the blooms burst).
    //   Dusk Tide      ring out to the edge, then the circle under it (reversed every other time below 50%); then it
    //                  closes its petals for the 3.5 s punish window.
    //
    // Every job is a generic soak (Hazard.Soak with ShareCount) or a plain shape to leave, so ally bots play it as is.
    // Visuals: the rigged model (Characters/VesperSeedkeeper) when present, else a labelled primitive stand-in; no final FX
    // or audio yet. Every warning gets the raid look (RaidStyle).
    // 50%: the shift waits for the block to finish, then 2 s harmless; no heal, no enrage. No ability text: PhaseName
    // and Lesson stay empty; names live in the F7 overlay (VesperSeedkeeperHud).
    public sealed class VesperSeedkeeperEncounter : BossEncounter
    {
        public override string Id => "boss.vesper-seedkeeper";
        public override string DisplayName => "The Vesper Seedkeeper";
        public override string Lesson => "";
        public override float NextPhaseFraction() => 0.49f;
        // the rigged model when Resources/Characters/VesperSeedkeeper(.glb) is in the project, else the labelled stand-in
        public override bool Placeholder => !presenter || !presenter.HasModel;
        CharacterPresenter presenter; public CharacterPresenter Presenter => presenter;
        string loopClip = "Idle";
        public static readonly string[] ClipNames = { "Idle", "Walk", "LanternCast", "TetherLoop", "BloomOpen", "BloomLoop", "SeedTip", "HaloTilt", "SweepLoop", "PhaseShift", "Defeat" };
        void Clip(string name, int ticks) { if (presenter) presenter.Action(name, Mathf.Max(1, ticks)); }

        // ---------------- tunables (Medium) ----------------
        public const int StakeHp = 60, CordDmg = 40, SurgeDmg = 30, SeedWither = 60, DarkDmg = 70, UmbraDmg = 70, TideDmg = 55, LateBloomDmg = 40;
        public const float CordSec = 7.0f, SeedFollowSec = 5.0f, SeedFallSec = 1.0f, NightGapSec = 1.5f, NightSec = 6.0f, NightSecLow = 5.0f;
        public const float TideFirstSec = 2.8f, TideSecondSec = 1.6f, PunishSec = 3.5f, LateBloomSec = 1.2f, ShiftSec = 2.0f, GapSec = 1.0f;
        public const float DriftSpeed = 3.0f;
        float Slow => Level == Difficulty.Easy ? 1.25f : 1f;
        int T(float sec) => Mathf.Max(1, Sim.Ticks(sec * Slow));

        // palette (HDR): cords and tide coral, ground jobs amber, the dark a deep plum-coral, lights friendly ivory-gold
        public static readonly Color StrikeFill = new Color(1.3f, 0.32f, 0.16f), StrikeBorder = new Color(2.4f, 0.8f, 0.3f),
            GroundFill = new Color(1.3f, 0.6f, 0.12f), GroundBorder = new Color(2.6f, 1.35f, 0.35f),
            DarkFill = new Color(0.5f, 0.16f, 0.2f), DarkBorder = new Color(1.4f, 0.42f, 0.36f),
            LightRim = new Color(2.2f, 2.0f, 1.3f), Ivory = new Color(0.93f, 0.9f, 0.82f), Gold = new Color(1.0f, 0.78f, 0.36f),
            Cobalt = new Color(0.2f, 0.28f, 0.62f), CordGlow = new Color(1.0f, 0.62f, 0.9f);

        // ---------------- state (tests, F7 overlay) ----------------
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;            // dev/test hook: replaces the next rotation entry once ("Cords", "Tide", "Seeds")
        public int Seed = Environment.TickCount;
        public int Tick => W != null ? W.Tick : 0;
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public bool Closed { get; private set; }     // the punish window

        public int CordCasts, CordsBroken, CordsSevered, CordsMissed, CordLaneHits, SurgeHits;
        public int SeedCasts, SeedsPlanted, SeedsWithered, SeedsWithdrawn;
        public int NightCasts, NightLit, NightDark, NightGuttered, UmbraHits, LateBloomHits;
        public int TideCasts, EbbHits, FlowHits;
        public readonly List<(int tick, int wave, int stakes, int severed)> CordLog = new List<(int, int, int, int)>();
        public readonly List<(int tick, int wave, float angle, Vector3 stake, bool severed)> StakeLog = new List<(int, int, float, Vector3, bool)>();
        public readonly List<(int tick, PlayerController carrier, int inside, int share, Vector3 landed, Vector3 planted)> SeedLog = new List<(int, PlayerController, int, int, Vector3, Vector3)>();
        public readonly List<(int tick, int blooms, int lit, int dark, int guttered)> NightLog = new List<(int, int, int, int, int)>();
        public readonly List<(int tick, bool reversed, Vector3 at, int firstAt, int secondAt)> TideLog = new List<(int, bool, Vector3, int, int)>();
        public readonly List<(int start, int end)> PunishLog = new List<(int, int)>();
        public readonly List<(int start, int end)> CordWaveGaps = new List<(int, int)>();
        public int PendingCords { get; private set; }
        public int PendingSeeds { get; private set; }
        public sealed class Bloom { public Vector3 At; public int Cap; public BellkeeperFx.Telegraph View; }
        public readonly List<Bloom> Blooms = new List<Bloom>();
        public bool NightActive { get; private set; }
        public IReadOnlyList<PlayerController> Carriers => carriers;

        enum Atk { Cords, Tide, Seeds }
        static readonly Atk[] RotationHigh = { Atk.Cords, Atk.Tide, Atk.Seeds, Atk.Tide };
        static readonly Atk[] RotationLow = { Atk.Seeds, Atk.Cords, Atk.Tide, Atk.Tide };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());
        int rotHigh, rotLow, tidesLow;
        System.Random rng;
        readonly List<PlayerController> carriers = new List<PlayerController>();
        readonly List<Transform> props = new List<Transform>();       // placeholder cords, stake posts, seed orbs
        readonly List<(Transform orb, PlayerController who, int net)> seedOrbs = new List<(Transform, PlayerController, int)>();   // net: LAN prop id
        BellkeeperFx.Telegraph dark;
        Transform visual, core, halo; Renderer coreR; float bob;

        void Awake() { MaxHp = 3000; BodyRadius = 1.3f; HitHeight = 2.2f; MarkHeight = 4.4f; }

        // ---------------- placeholder body ----------------
        protected override void Build()
        {
            PhaseName = "";
            R.Bound = CombatWorld.PlayerBound;
            presenter = CharacterPresenter.Create(transform, "VesperSeedkeeper", 1.5f, 1f, 0, host => { BuildStandIn(host); return host; });
            gameObject.AddComponent<VesperSeedkeeperHud>().Bind(this);
        }

        // a labelled code-built stand-in, used only when the rigged model is missing
        void BuildStandIn(Transform host)
        {
            visual = new GameObject("Vesper Seedkeeper (STAND-IN, labelled placeholder)").transform;
            visual.SetParent(host, false);
            Prim.Make(PrimitiveKind.Sphere, visual, new Vector3(0, 2.0f, 0), new Vector3(2.3f, 1.3f, 2.3f), Ivory).name = "Vessel bowl (stand-in)";
            core = Prim.Make(PrimitiveKind.Sphere, visual, new Vector3(0, 2.25f, 0), Vector3.one * 1.0f, Gold, 3f); core.name = "Core of light (stand-in)";
            coreR = core.GetComponent<Renderer>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f; var dir = Sim.Dir(a);
                Prim.Make(PrimitiveKind.Cube, visual, dir * 1.15f + Vector3.up * 2.7f, new Vector3(0.7f, 0.08f, 1.1f), Ivory, 0,
                    Quaternion.Euler(-35f, a, 0)).name = "Petal (stand-in)";
            }
            halo = Prim.Make(PrimitiveKind.Cylinder, visual, new Vector3(0, 3.7f, 0), new Vector3(2.4f, 0.03f, 2.4f), Gold, 2f); halo.name = "Halo (stand-in)";
            Prim.Make(PrimitiveKind.Cylinder, visual, new Vector3(0, 1.1f, 0), new Vector3(0.06f, 0.5f, 0.06f), Cobalt).name = "Lantern cord (stand-in)";
            Prim.Make(PrimitiveKind.Sphere, visual, new Vector3(0, 0.6f, 0), Vector3.one * 0.45f, Gold, 2.5f).name = "Hanging lantern (stand-in)";
        }

        bool Hard => Level == Difficulty.Hard;
        int Standing { get { int n = 0; foreach (var p in W.Players) if (!p.Down) n++; return n; } }
        List<PlayerController> StandingPlayers() { var l = new List<PlayerController>(); foreach (var p in W.Players) if (!p.Down) l.Add(p); return l; }
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        bool IsLive(Hazard h) { if (h == null) return false; var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        Hazard Live(Hazard h, Color fill, Color border)
        {
            W.Hazards.Spawn(h);
            HazardSystem.RaidStyle(h);   // Telegraph-Style v2: area motif, ink under-stroke, locked footprint on top
            h.Tele.Tint(fill, border); h.Tele.SpentFast = true;
            return h;
        }
        // a hit that is not a spawned shape (the surge, the dark, a withered seed): no knockback, a short flinch
        static Hazard Blow(string source, Vector3 at, int dmg) =>
            new Hazard { Source = source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = 999, Damage = dmg, Knockback = 0, Stun = 6 };

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            foreach (var w in DriftTo(Vector3.zero)) yield return w;
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
                    Atk.Cords => Cords(),
                    Atk.Seeds => Seedgift(),
                    _ => Tide(Phase2 && (tidesLow++ % 2 == 1)),
                };
                foreach (var w in body) yield return w;
                Current = "between blocks";
                yield return Sim.Ticks(GapSec);
            }
        }

        protected override void StepAlways()
        {
            // seed orbs ride over their carriers (presentation, host side)
            foreach (var (orb, who, _) in seedOrbs) if (orb && who) orb.position = who.transform.position + Vector3.up * 2.9f;
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
            if (presenter) presenter.TickBoss(Current == "reposition" ? "Walk" : loopClip);
        }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (Phase2) return; Phase2 = true; GatePending = false; OpenPetals(); }

        IEnumerable<int> DriftTo(Vector3 dest)
        {
            if (Sim.Flat(dest - transform.position).magnitude > 0.3f) foreach (var w in WalkTo(dest, DriftSpeed, 360)) yield return w;
        }

        // ================= 1. Vesper Cords =================
        IEnumerable<int> Cords()
        {
            foreach (var w in DriftTo(Vector3.zero)) yield return w;
            Current = Phase2 ? "Vesper Cords (two waves)" : "Vesper Cords"; CordCasts++;
            Clip("LanternCast", Sim.Ticks(1f)); loopClip = "TetherLoop";
            int n = R.Cords(Standing); float offset = (float)(rng.NextDouble() * 360.0);
            int first = T(CordSec);
            CordSet(n, offset, 1, first);
            if (Phase2)
            {
                int second = first + T(R.WaveGapSeconds(n));
                CordSet(n, offset, 2, second);
                CordWaveGaps.Add((first, second));
            }
            for (int t = 0; PendingCords > 0 && t < 1200; t++) { Windup(Mathf.Clamp01(t / (float)first)); yield return 1; }
            Release(1.2f);
            Current = "Vesper Cords (recovery)"; loopClip = "Idle";
            yield return Sim.Ticks(0.5f);
        }

        // One cord: its lane, its stake (a soak to stand on) and the stake as a boss part (break it early by attacking it;
        // its hits count against the boss too, like every part). Severed = someone on the stake at the snap, or broken.
        sealed class Cord { public Hazard Lane, Stake; public BossPart Part; public Transform View, Post; public bool Done; public int Hp, Wave; public float Angle; public CordWave Set; }
        sealed class CordWave { public int Count, Severed, Resolved, Wave; }
        readonly List<Cord> cords = new List<Cord>();
        public List<BossPart> LiveStakeParts() { var l = new List<BossPart>(); foreach (var c in cords) if (!c.Done) l.Add(c.Part); return l; }

        void CordSet(int n, float offset, int wave, int resolve)
        {
            var boss = Sim.Flat(transform.position);
            var set = new CordWave { Count = n, Wave = wave };
            foreach (float a in R.StakeAngles(n, offset, wave == 2))
            {
                var at = R.StakeAt(a);
                var (yaw, len) = R.CordLane(boss, at);
                PendingCords++;
                var c = new Cord { Hp = StakeHp, Wave = wave, Angle = a, Set = set };
                c.Lane = new Hazard { Source = "Vesper Cords (snap)", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = boss, Yaw = yaw,
                    Length = len, Width = R.CordWidth, LockAt = 0, ResolveAt = resolve, Damage = CordDmg, Knockback = 0, Stun = 6, ClipRadius = R.Bound + 0.6f };
                c.Lane.OnResolve = hz => CordLaneHits += hz.Hit.Count;
                Live(c.Lane, StrikeFill, StrikeBorder);
                c.Stake = new Hazard { Source = "Vesper Cords (stake)", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at,
                    Radius = R.StakeRadius, LockAt = 0, ResolveAt = resolve, Damage = 0, HitOnResolve = false, Soak = true, ShareCount = 1, Knockback = 0, Stun = 0 };
                c.View = CordProp(boss, at); c.Post = Prop(PrimitiveKind.Cylinder, at + Vector3.up * 0.5f, new Vector3(0.35f, 0.5f, 0.35f), Cobalt, 0, "Cord stake (stand-in)");
                c.Stake.OnResolve = hz =>
                {
                    bool on = false;
                    foreach (var p in W.Players) if (!p.Down && Sim.Flat(p.transform.position - hz.Origin).magnitude <= hz.Radius + Hazard.Margin) on = true;
                    FinishCord(c, on, false);
                };
                Live(c.Stake, GroundFill, GroundBorder);
                c.Part = new BossPart { Name = "Vesper stake", Pos = at, BodyRadius = R.StakePartRadius, HitHeight = 0.6f };
                c.Part.TakeHit = (dmg, contact) =>
                {
                    if (c.Done || Dead) return 0;
                    int dealt = Mathf.Min(dmg, c.Hp); c.Hp -= dealt;
                    RunHud.Float(contact + Vector3.up * 0.5f, dealt.ToString(), Gold);
                    if (Solid && dealt > 0) TakeHit(dealt, transform.position + Vector3.up * HitHeight, false);   // parts are the boss
                    if (Dead) return dealt;   // that hit was the killing blow: everything is already cleared
                    if (c.Hp <= 0) { W.Hazards.Cancel(c.Lane); W.Hazards.Cancel(c.Stake); CordsBroken++; FinishCord(c, true, true); }
                    return dealt;
                };
                W.Parts.Add(c.Part);
                cords.Add(c);
            }
        }

        void FinishCord(Cord c, bool severed, bool broken)
        {
            if (c.Done) return;
            c.Done = true;
            StakeLog.Add((Tick, c.Wave, c.Angle, c.Stake.Origin, severed));
            if (severed) { CordsSevered++; c.Set.Severed++; }
            else
            {
                // unsevered: the dusk surges through the whole party
                CordsMissed++;
                var surge = Blow("Vesper Cords (surge)", c.Stake.Origin, SurgeDmg);
                foreach (var p in W.Players) if (!p.Down) { surge.Hit.Add(p); p.ReceiveHit(surge); SurgeHits++; }
                W.BossFx.ChimeResolve(c.Stake.Origin, 2.2f);
            }
            if (broken) W.BossFx.ChimeResolve(c.Stake.Origin, 1.2f);
            KillProp(c.View); KillProp(c.Post);
            c.Part.Alive = false; W.Parts.Remove(c.Part); cords.Remove(c);
            PendingCords--;
            if (++c.Set.Resolved == c.Set.Count) CordLog.Add((Tick, c.Set.Wave, c.Set.Count, c.Set.Severed));
        }

        // ================= 2. Seedgift + 3. Nightfall =================
        IEnumerable<int> Seedgift()
        {
            Current = "Seedgift"; SeedCasts++;
            var shares = R.Shares(Standing);
            PickCarriers(shares.Length);
            int lockAt = T(SeedFollowSec), landAt = lockAt + T(SeedFallSec);
            Clip("SeedTip", Sim.Ticks(1f));
            for (int i = 0; i < carriers.Count; i++) GiveSeed(carriers[i], shares[i], R.SeedTotal(shares[i]), lockAt, landAt);
            for (int t = 0; PendingSeeds > 0 && t < 1200; t++) { Windup(Mathf.Clamp01(t / (float)landAt)); yield return 1; }
            Release(0.8f);
            carriers.Clear();
            if (!Phase2) { Current = "Seedgift (planted)"; yield return T(NightGapSec); }
            foreach (var w in Nightfall()) yield return w;
        }

        // the farthest player from the vessel, then the player farthest from the first carrier
        void PickCarriers(int count)
        {
            carriers.Clear();
            var all = StandingPlayers(); if (all.Count == 0) return;
            PlayerController first = null; float bd = -1;
            foreach (var p in all) { float d = Sim.Flat(p.transform.position - transform.position).magnitude; if (d > bd) { bd = d; first = p; } }
            carriers.Add(first);
            if (count >= 2)
            {
                PlayerController second = null; bd = -1;
                foreach (var p in all) { if (p == first) continue; float d = Sim.Flat(p.transform.position - first.transform.position).magnitude; if (d > bd) { bd = d; second = p; } }
                if (second) carriers.Add(second);
            }
        }

        void GiveSeed(PlayerController who, int share, int total, int lockAt, int landAt)
        {
            PendingSeeds++;
            who.Marks.Add(MechanicIcons.CarriedMark);
            var orb = Prop(PrimitiveKind.Sphere, who.transform.position + Vector3.up * 2.9f, Vector3.one * 0.45f, Gold, 4f, "Seed of light (stand-in)");
            seedOrbs.Add((orb, who, NextNetPropId--));
            var h = new Hazard { Source = "Seedgift", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(who.transform.position),
                Radius = R.SeedRadius, LockAt = lockAt, ResolveAt = landAt, ShareTotal = total, ShareCount = share, Soak = true, Knockback = 0, Stun = 6 };
            bool done = false;
            void End() { if (done) return; done = true; who.Marks.Remove(MechanicIcons.CarriedMark); KillProp(orb); seedOrbs.RemoveAll(s => s.orb == orb); PendingSeeds--; }
            h.Track = hz =>
            {
                if (who.Down) { W.Hazards.Cancel(hz); SeedsWithdrawn++; End(); return; }   // a downed carrier's seed is withdrawn
                hz.Origin = Sim.Flat(who.transform.position);
            };
            h.OnResolve = hz =>
            {
                if (hz.Hit.Count == 0)
                {
                    // nobody under the locked seed: it withers on its carrier, no bloom
                    SeedsWithered++;
                    if (!who.Down) { var b = Blow("Seedgift (withered)", who.transform.position, SeedWither); b.Hit.Add(who); who.ReceiveHit(b); }
                    SeedLog.Add((Tick, who, 0, 0, hz.Origin, Vector3.positiveInfinity));
                }
                else
                {
                    var at = R.PlantSpot(hz.Origin, transform.position);
                    var view = W.BossFx.Circle(R.BloomRadius); view.Friendly(LightRim); view.Lock(); view.Place(at, 0);
                    Blooms.Add(new Bloom { At = at, Cap = share, View = view });
                    SeedsPlanted++;
                    SeedLog.Add((Tick, who, hz.Hit.Count, HazardSystem.ShareOf(hz, hz.Hit.Count), hz.Origin, at));
                }
                End();
            };
            Live(h, GroundFill, GroundBorder);
        }

        IEnumerable<int> Nightfall()
        {
            Current = "Nightfall"; NightCasts++; NightActive = true;
            Clip("BloomOpen", Sim.Ticks(1.2f)); loopClip = "BloomLoop";
            int dur = T(Phase2 ? NightSecLow : NightSec);
            // each bloom is a soak to fill with its numeral (bots read it); the dark itself is dealt here
            var soaks = new List<Hazard>();
            foreach (var b in Blooms)
            {
                var s = new Hazard { Source = "Nightfall (bloom)", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = b.At,
                    Radius = R.BloomRadius, LockAt = 0, ResolveAt = dur, Damage = 0, HitOnResolve = false, Soak = true, ShareCount = b.Cap, Knockback = 0, Stun = 0 };
                W.Hazards.Spawn(s); HazardSystem.RaidStyle(s); s.Tele.Friendly(LightRim); s.Tele.SpentFast = true;
                soaks.Add(s);
            }
            var umbra = new Hazard { Source = "Nightfall (umbra)", MechanicId = MechanicIcons.DelayedCircle, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(transform.position),
                Radius = R.UmbraRadius, LockAt = 0, ResolveAt = dur, Damage = UmbraDmg, Knockback = 0, Stun = 6 };
            umbra.OnResolve = hz => { UmbraHits += hz.Hit.Count; ResolveDark(hz.Hit); };
            Live(umbra, DarkFill * 1.6f, DarkBorder * 1.3f);
            // the dark: the whole floor with the blooms cut out (hole radius = the lit test), under every timed warning
            dark = W.BossFx.Circle(R.Bound + 0.6f);
            dark.Tint(DarkFill, DarkBorder); dark.Motif(HazardSystem.RaidMotif); dark.Order = -1; dark.Progress(0); dark.Place(Vector3.zero, 0);
            var holes = new Vector4[6]; int nh = Mathf.Min(6, Blooms.Count);
            for (int i = 0; i < nh; i++) holes[i] = new Vector4(Blooms[i].At.x, Blooms[i].At.z, R.BloomRadius + Hazard.Margin, 0);
            dark.Holes(holes, nh);
            for (int t = 0; IsLive(umbra) && t < dur + 30; t++) { dark.Progress((t + 1) / (float)dur); Windup((t + 1) / (float)dur); yield return 1; }
            dark.Resolve(); dark = null;
            W.BossFx.TollResolve(Vector3.zero, 0, R.Bound);
            CameraShake.Shake(0.18f, 0.3f); Release(1.4f);
            var late = new List<Hazard>();
            if (Hard)
                foreach (var b in Blooms)
                {
                    var h = new Hazard { Source = "Nightfall (Late Bloom)", MechanicId = MechanicIcons.DelayedCircle, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = b.At,
                        Radius = R.LateBloomRadius, LockAt = 0, ResolveAt = Sim.Ticks(LateBloomSec), Damage = LateBloomDmg, Knockback = 0, Stun = 6 };
                    h.OnResolve = hz => LateBloomHits += hz.Hit.Count;
                    Live(h, GroundFill, GroundBorder); late.Add(h);
                }
            ClearBlooms();
            NightActive = false;
            Current = "Nightfall (dawn)"; loopClip = "Idle";
            for (int t = 0; late.Exists(IsLive) && t < 300; t++) yield return 1;
            yield return Sim.Ticks(0.5f);
        }

        // at the Nightfall tick: everyone not in a lit bloom (and not already hit by the umbra) takes the dark
        void ResolveDark(HashSet<PlayerController> umbraHit)
        {
            var counts = new int[Blooms.Count];
            foreach (var p in W.Players) { if (p.Down) continue; for (int i = 0; i < Blooms.Count; i++) if (InBloom(i, p.transform.position)) counts[i]++; }
            int lit = 0, darkHits = 0, guttered = 0;
            for (int i = 0; i < counts.Length; i++) if (counts[i] > Blooms[i].Cap) { guttered++; NightGuttered++; Blooms[i].View?.Tint(StrikeFill, StrikeBorder); }
            var blow = Blow("Nightfall", Vector3.zero, DarkDmg);
            foreach (var p in W.Players)
            {
                if (p.Down || umbraHit.Contains(p)) continue;
                bool isLit = false;
                for (int i = 0; i < Blooms.Count; i++) if (InBloom(i, p.transform.position) && counts[i] <= Blooms[i].Cap) isLit = true;
                if (isLit) { lit++; NightLit++; continue; }
                blow.Hit.Add(p); p.ReceiveHit(blow); darkHits++; NightDark++;
            }
            NightLog.Add((Tick, Blooms.Count, lit, darkHits, guttered));
        }
        public bool InBloom(int i, Vector3 p) => Sim.Flat(p - Blooms[i].At).magnitude <= R.BloomRadius + Hazard.Margin;

        void ClearBlooms() { foreach (var b in Blooms) b.View?.Dismiss(); Blooms.Clear(); }

        // ================= 4. Dusk Tide =================
        IEnumerable<int> Tide(bool reversed)
        {
            var spots = new List<Vector3>(); foreach (var p in StandingPlayers()) spots.Add(p.transform.position);
            foreach (var w in DriftTo(R.TideSpot(spots))) yield return w;
            Current = reversed ? "Dusk Tide (reversed)" : "Dusk Tide"; TideCasts++;
            Clip("HaloTilt", Sim.Ticks(0.8f)); loopClip = "SweepLoop";
            var at = Sim.Flat(transform.position);
            int first = T(TideFirstSec), second = first + T(TideSecondSec);
            if (halo) halo.localPosition = new Vector3(0, reversed ? 1.5f : 3.7f, 0);   // stand-in cue: halo dropped low = flood first (LAN: ReplicaPresent, from the attack name)
            var ebb = new Hazard { Source = "Dusk Tide (ebb)", MechanicId = MechanicIcons.Ring, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = at, Inner = R.TideRadius,
                Radius = R.Bound + 1.5f, LockAt = 0, ResolveAt = reversed ? second : first, Damage = TideDmg, Knockback = 0, Stun = 6, ClipRadius = R.Bound + 0.6f };
            ebb.OnResolve = hz => EbbHits += hz.Hit.Count;
            var flow = new Hazard { Source = "Dusk Tide (flow)", MechanicId = MechanicIcons.DelayedCircle, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at,
                Radius = R.TideRadius, LockAt = 0, ResolveAt = reversed ? first : second, Damage = TideDmg, Knockback = 0, Stun = 6 };
            flow.OnResolve = hz => FlowHits += hz.Hit.Count;
            Live(ebb, GroundFill, GroundBorder); Live(flow, StrikeFill, StrikeBorder);
            TideLog.Add((Tick, reversed, at, Tick + first, Tick + second));
            for (int t = 0; (IsLive(ebb) || IsLive(flow)) && t < second + 60; t++) { Windup((t + 1) / (float)second); yield return 1; }
            Release(1f); if (halo) halo.localPosition = new Vector3(0, 3.7f, 0);
            loopClip = "Idle";
            // the punish window: petals closed, the core dimmed, nothing live
            Current = "Dusk Tide (closed)"; Closed = true; glowTarget = 0.4f;
            int from = Tick;
            yield return Sim.Ticks(PunishSec);
            PunishLog.Add((from, Tick)); Closed = false; glowTarget = 1;
            Current = "Dusk Tide (recovery)";
        }

        // ================= the 50% shift =================
        IEnumerable<int> Transition()
        {
            GatePending = false;
            Current = "Shift"; Log("Shift"); ShiftStart = Tick; HpAtShiftStart = Hp;
            Clip("PhaseShift", Sim.Ticks(ShiftSec)); loopClip = "Idle";
            CameraShake.Shake(0.12f, 0.3f);
            yield return Sim.Ticks(ShiftSec);   // harmless: nothing is spawned, it stays damageable
            Phase2 = true; OpenPetals(); Release(1.5f); ShiftEnd = Tick;
        }
        void OpenPetals() { if (visual) visual.localScale = new Vector3(1.15f, 1.05f, 1.15f); }

        // ---------------- placeholder props ----------------
        Transform Prop(PrimitiveKind kind, Vector3 at, Vector3 scale, Color c, float glow, string name)
        {
            var t = Prim.Make(kind, transform.parent, at, scale, c, glow); t.name = name;
            t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            props.Add(t); return t;
        }
        Transform CordProp(Vector3 from, Vector3 to)
        {
            var mid = (from + to) / 2 + Vector3.up * 1.0f; var d = Sim.Flat(to - from);
            var t = Prop(PrimitiveKind.Cube, mid, new Vector3(0.08f, 0.08f, d.magnitude), CordGlow, 3f, "Vesper cord (stand-in)");
            t.rotation = Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d : Vector3.forward);
            return t;
        }
        void KillProp(Transform t) { if (t) { props.Remove(t); Destroy(t.gameObject); } }
        void ClearProps() { foreach (var t in props) if (t) Destroy(t.gameObject); props.Clear(); seedOrbs.Clear(); }

        // ---------------- lifecycle ----------------
        protected override void Update()
        {
            base.Update();
            PresentLook(glowLevel, Time.deltaTime);
        }
        // bob, halo spin and core glow: the host from its own state, a LAN client's replica from the host's (ReplicaPresent)
        void PresentLook(float glow, float dt)
        {
            bob += dt;
            if (visual) visual.localPosition = new Vector3(0, 0.6f + Mathf.Sin(bob * 1.3f) * 0.12f, 0);
            if (halo) halo.Rotate(0, 25f * dt, 0, Space.Self);
            if (coreR) Prim.SetGlow(coreR, Gold * (3f * glow));
            if (presenter) presenter.GlowLevel = glow;
        }

        // ---------------- LAN ----------------
        // props: 0 = a cord stake post, 1 = a cord (sent at its stake; the view stretches from the vessel), 2 = a seed orb
        public const byte NetStake = 0, NetCord = 1, NetSeed = 2;
        public override void CaptureNetProps(List<Networking.LanWorldFrame.Prop> into)
        {
            foreach (var c in cords)
            {
                if (c.Done || c.Stake == null) continue;
                if (c.Post) into.Add(NetProp(c.Stake.Id, NetStake, c.Post.position));
                if (c.View && c.Lane != null) into.Add(NetProp(c.Lane.Id, NetCord, c.Stake.Origin));
            }
            foreach (var (orb, _, net) in seedOrbs) if (orb) into.Add(NetProp(net, NetSeed, orb.position));
        }
        public override Transform MakeNetProp(byte kind)
        {
            switch (kind)
            {
                case NetStake: { var t = Prim.Make(PrimitiveKind.Cylinder, transform.parent, Vector3.zero, new Vector3(0.35f, 0.5f, 0.35f), Cobalt, 0); t.name = "Cord stake (LAN)"; NoShadow(t); return t; }
                case NetCord:
                {
                    var root = new GameObject("Vesper cord (LAN)").transform; root.SetParent(transform.parent, false);
                    var bar = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(0.08f, 0.08f, 1f), CordGlow, 3f); NoShadow(bar);
                    root.gameObject.AddComponent<VesperNetCord>().Bind(transform, bar);
                    return root;
                }
                case NetSeed: { var t = Prim.Make(PrimitiveKind.Sphere, transform.parent, Vector3.zero, Vector3.one * 0.45f, Gold, 4f); t.name = "Seed of light (LAN)"; NoShadow(t); return t; }
                default: return null;
            }
        }
        static void NoShadow(Transform t) { var r = t ? t.GetComponent<MeshRenderer>() : null; if (r) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // looks: Glow = core glow; Look[0] = phase 2 (petals open, 1/0). The halo drop follows the attack name, the tip-over Dead.
        public override void CaptureNetLook(Networking.LanWorldFrame.BossState into)
        {
            into.Glow = glowLevel;
            into.Look.Add(Phase2 ? 1 : 0);
        }
        public override void ReplicaPresent(Networking.LanWorldFrame.BossState state, float dt)
        {
            bool phase2 = state.Look.Count > 0 && state.Look[0] > 0.5f;
            if (visual) visual.localScale = phase2 ? new Vector3(1.15f, 1.05f, 1.15f) : Vector3.one;
            if (halo) halo.localPosition = new Vector3(0, state.Attack == "Dusk Tide (reversed)" ? 1.5f : 3.7f, 0);
            PresentLook(state.Glow, dt);
            if (visual) visual.localRotation = state.Dead ? Quaternion.Euler(70, 0, 0) : Quaternion.identity;
        }

        protected override void OnDefeat()
        {
            // hazards and telegraphs were cancelled by the world's ClearTransient in this same tick
            foreach (var who in carriers) if (who) who.Marks.Remove(MechanicIcons.CarriedMark);
            foreach (var p in W.Players) p.Marks.Remove(MechanicIcons.CarriedMark);
            carriers.Clear(); Blooms.Clear(); cords.Clear(); dark = null;
            ClearProps();
            PendingCords = 0; PendingSeeds = 0; NightActive = false; Closed = false;
            if (visual) visual.localRotation = Quaternion.Euler(70, 0, 0);   // tipped over (stand-in defeat pose)
            if (presenter) presenter.Action("Defeat", 150);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy() { foreach (var t in props) if (t) Destroy(t.gameObject); }
    }
}
