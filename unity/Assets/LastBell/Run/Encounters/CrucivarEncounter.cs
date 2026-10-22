using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Crucivar, The Furnace Heart: the Brass Foundry's area boss (bosses/2-brass-foundry-crucivar/BOSS.md, from Codex's
    // four-boss handoff, collab PR #46). Steady Ticking numbers from the packet: W/R/CD in seconds, geometry in metres
    // before the player-radius margin, damage against 100 player HP.
    //   Diebreaker        r 3 circle ahead of the hammer           1.8 / 2.4 / 6    18
    //   Slag Cast         r 1.4 circles, pools 3 s                 2.0 / 1.6 / 10   10 + 4/s standing
    //   Rail Ram          2.6 wide lane, up to 8 m of travel       1.8 / 2.5 / 12   16
    //   Pressure Release  two opposite 70-degree sectors, reach 5  1.7 / 2.0 / 9    12
    //   Casting Cycle     three sequential strips, width 2         1.8 each / 3.5 / 22   14 per strip
    // Phases 100-65 Sealed Furnace (isolated attacks), 65-30 Casting Heat (insulation sheds, Slag before Casting),
    // 30-0 Open Pressure (flywheel guard and shoulder vents open, Ram repositions before Casting). Transitions queue to
    // the end of the current attack and deal no damage. HP is shortened for the test build like the Bellkeeper's
    // (packet example 6600 at 40 DPS, x0.375).
    // Pressure Release vents: front = the three belly shutters, back = the exhaust under the flywheel, left/right = the
    // flank grilles. The packet's front/back OR left/right pairs need vents on all four sides, which the v1 sheet lacked.
    public sealed class CrucivarEncounter : BossEncounter
    {
        public override string Id => "boss.crucivar";
        public override string DisplayName => "Crucivar, The Furnace Heart";
        public override string Lesson => "Cross behind the cooled strikes; punish the lodged hammer";
        public override bool Placeholder => !presenter || !presenter.HasModel;
        public override float NextPhaseFraction() => Hp > MaxHp * P2At ? P2At - 0.01f : P3At - 0.01f;

        const float P2At = 0.65f, P3At = 0.30f;
        const float DieRadius = 3f, DieForward = 2.9f, DieRight = 0.7f, SlagRadius = 1.4f, RamWidth = 2.6f, RamTravelMax = 8f,
            ReleaseReach = 5f, ReleaseHalf = 35f, StripWidth = 2f, StripLength = 13f, StripSpacing = 3.6f, Walk = 1.4f,
            Bound = 8f;   // charge endpoints and repositioning: keeps a 1.8 m wall corridor behind the body (no wall pin)
        const int DieW = 108, DieRec = 144, SlagFollow = 36, SlagW = 120, SlagRec = 96, PoolLife = 180, RamW = 108, RamTravel = 36,
            RamRec = 150, RelW = 102, RelRec = 120, StripW = 108, StripGap = 36, CastRec = 210, ShiftTicks = 90;
        const int DieDmg = 18, SlagDmg = 10, RamDmg = 16, RelDmg = 12, StripDmg = 14; const float PoolDps = 4f;
        const int MaxPools = 4;

        enum Atk { Diebreaker, Slag, Ram, Release, Casting }
        static readonly int[] Cooldown = { 360, 600, 720, 540, 1320 };
        static readonly Atk[][] Rotation =
        {
            new[] { Atk.Diebreaker, Atk.Slag, Atk.Release, Atk.Ram, Atk.Diebreaker, Atk.Release },
            new[] { Atk.Slag, Atk.Casting, Atk.Ram, Atk.Diebreaker, Atk.Release },
            new[] { Atk.Ram, Atk.Casting, Atk.Release, Atk.Diebreaker, Atk.Slag, Atk.Diebreaker },
        };
        readonly int[] readyAt = new int[5];
        readonly List<Hazard> pools = new List<Hazard>();
        int phase = 1, step, slagTurn;

        CharacterPresenter presenter;
        CrucivarRig rig;
        public CrucivarRig Rig => rig;
        public CharacterPresenter Presenter => presenter;
        public int PhaseIndex => phase;
        public int Attacks { get; private set; }

        void Awake() { MaxHp = 2500; BodyRadius = 1.7f; HitHeight = 2.0f; MarkHeight = 4.6f; }

        protected override void Build()
        {
            presenter = CharacterPresenter.Create(transform, "Crucivar", 3f, 0.8f, 0, CrucivarRig.BuildPlaceholder);
            rig = presenter.Visual.gameObject.AddComponent<CrucivarRig>();
            rig.Bind(presenter.Visual);
            PhaseName = "Sealed Furnace";
            BindAbilities();
        }

        int PhaseFor() => Hp > MaxHp * P2At ? 1 : Hp > MaxHp * P3At ? 2 : 3;

        protected override IEnumerable<int> Script() => CrucivarArgs.V1 ? V1Script() : V2Script();

        IEnumerable<int> V1Script()
        {
            while (true)
            {
                int want = PhaseFor();
                if (want != phase)
                {
                    // queued: only between attacks; skip straight to the latest phase (never stack phase attacks)
                    phase = want;
                    foreach (var w in PhaseShift()) yield return w;
                }
                var rot = Rotation[phase - 1];
                var atk = rot[step++ % rot.Length];
                if (Age < readyAt[(int)atk]) { yield return 1; continue; }   // cooldown: try the next entry on the next tick
                readyAt[(int)atk] = Age + Cooldown[(int)atk];
                Attacks++;
                IEnumerable<int> body = atk switch
                {
                    Atk.Diebreaker => Diebreaker(),
                    Atk.Slag => SlagCast(),
                    Atk.Ram => RailRam(),
                    Atk.Release => PressureRelease(),
                    _ => CastingCycle(),
                };
                foreach (var w in body) yield return w;
            }
        }

        IEnumerable<int> PhaseShift()
        {
            Current = "phase shift";
            SetPhase(phase == 2 ? "Casting Heat" : "Open Pressure");
            presenter.PlayOnce("PhaseShift", ShiftTicks / (float)Sim.Hz);
            rig.BeginPhase(phase);
            glowTarget = 2.4f;
            for (int i = 0; i < ShiftTicks; i++) { if (i == ShiftTicks / 2) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.2f); } yield return 1; }
            glowTarget = 1;
        }

        IEnumerable<int> Recover(string name, string clip, int ticks)
        {
            Current = name; presenter.PlayOnce(clip, ticks / (float)Sim.Hz); glowTarget = 0.5f;
            yield return ticks; glowTarget = 1;
        }

        // ---------------- Diebreaker ----------------
        IEnumerable<int> Diebreaker()
        {
            foreach (var w in Approach(DieForward + 0.6f, Walk, 150)) yield return w;
            Current = "Diebreaker";
            // face the target once, then the toothed circle locks immediately: no tracking after the warning appears
            for (int i = 0; i < 12; i++) { TurnTowards(YawToTarget(), 360); yield return 1; }
            var centre = ClampToBound(transform.position, transform.position + transform.forward * DieForward + transform.right * DieRight);
            var h = W.Hazards.Spawn(new Hazard { Source = "Diebreaker", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = centre,
                Radius = DieRadius, LockAt = 0, ResolveAt = DieW, Damage = DieDmg, Knockback = 2f });
            h.OnResolve = hz => { W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius); Release(1.2f); rig.HammerLand(); };
            presenter.PlayOnce("Diebreaker", DieW / (float)Sim.Hz);
            for (int i = 0; i < DieW; i++) { Windup(i / (float)DieW); rig.Ratchet(i / (float)DieW); yield return 1; }
            foreach (var w in Recover("Diebreaker (lodged)", "HammerLodged", DieRec)) yield return w;
        }

        // ---------------- Slag Cast ----------------
        IEnumerable<int> SlagCast()
        {
            Current = "Slag Cast";
            TurnTowards(YawToTarget(), 9999);
            presenter.PlayOnce("SlagCast", SlagW / (float)Sim.Hz);
            rig.Pour(true);
            // targeted circles: one player solo, two for 2-4, three for 5-8, rotating through eligible recipients
            var standing = W.Players.FindAll(p => !p.Down);
            int n = standing.Count <= 1 ? 1 : standing.Count <= 4 ? 2 : 3;
            for (int k = 0; k < n && standing.Count > 0; k++)
            {
                var who = standing[(slagTurn + k) % standing.Count];
                var h = new Hazard { Source = "Slag Cast", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = SlagRadius,
                    LockAt = SlagFollow, ResolveAt = SlagW, Damage = SlagDmg, Knockback = 1f, Origin = Bounded(who.transform.position) };
                h.Track = hz => { if (!who.Down) hz.Origin = Bounded(Vector3.MoveTowards(hz.Origin, Sim.Flat(who.transform.position), 6f * Sim.Dt)); };
                h.OnResolve = hz => { W.BossFx.SlagSplash(hz.Origin, hz.Radius); SpawnPool(hz.Origin); };
                W.Hazards.Spawn(h);
            }
            slagTurn += n;
            for (int i = 0; i < SlagW; i++) { Windup(0.5f * i / SlagW); yield return 1; }
            rig.Pour(false);
            foreach (var w in Recover("Slag Cast", "Idle", SlagRec)) yield return w;
        }

        // a pool never sits inside the boss and never outnumbers MaxPools: the oldest one cools early instead
        void SpawnPool(Vector3 at)
        {
            pools.RemoveAll(p => !W.Hazards.Live.Contains(p));
            if (pools.Count >= MaxPools) { pools[0].ResolveAt = pools[0].Age + 1; pools.RemoveAt(0); }
            var pool = W.Hazards.Spawn(new Hazard { Source = "Slag Pool", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at,
                Radius = SlagRadius, LockAt = 0, ResolveAt = PoolLife, BurnPerSecond = PoolDps });
            pools.Add(pool);
        }
        static Vector3 Bounded(Vector3 p)
        {
            p.y = 0; var f = new Vector2(p.x, p.z); float max = CombatWorld.PlayerBound - SlagRadius;
            if (f.magnitude > max) { f = f.normalized * max; p.x = f.x; p.z = f.y; }
            return p;
        }

        // ---------------- Rail Ram ----------------
        IEnumerable<int> RailRam(System.Action onBrake = null)
        {
            Current = "Rail Ram";
            TurnTowards(YawToTarget(), 9999);
            var start = transform.position; var dir = Sim.Dir(transform.eulerAngles.y);
            float travel = Mathf.Min(RamTravelMax, Mathf.Max(3f, DistToTarget() + 2f));
            var end = start + dir * travel; end.y = 0;
            if (new Vector2(end.x, end.z).magnitude > Bound) end = ClampInside(start, end, Bound);
            travel = Vector3.Distance(start, end);
            var yaw = transform.eulerAngles.y;
            // the lane covers the travel plus the body ahead of the endpoint; it locks immediately
            var h = Lane("Rail Ram", start, yaw, travel + BodyRadius, RamWidth, RamW, RamDmg);
            h.Knockback = 2f;
            h.OnResolve = hz => { W.BossFx.ChargeTrail(transform, true); Release(); };
            presenter.PlayOnce("RailRam", (RamW + RamTravel) / (float)Sim.Hz);
            rig.Hiss(true);
            for (int i = 0; i < RamW; i++) { Windup(i / (float)RamW); yield return 1; }
            rig.Hiss(false);
            for (int i = 1; i <= RamTravel; i++) { transform.position = Vector3.Lerp(start, end, Mathf.SmoothStep(0, 1, i / (float)RamTravel)); yield return 1; }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.RamBrake(transform.position);
            onBrake?.Invoke();
            foreach (var w in Recover("Rail Ram (braking)", "Vulnerable", RamRec)) yield return w;
        }
        static Vector3 ClampInside(Vector3 from, Vector3 to, float r)
        {
            var d = to - from; float lo = 0, hi = 1;
            for (int i = 0; i < 20; i++) { float m = (lo + hi) / 2; var p = from + d * m; if (new Vector2(p.x, p.z).magnitude <= r) lo = m; else hi = m; }
            return from + d * lo;
        }

        // ---------------- Pressure Release ----------------
        IEnumerable<int> PressureRelease()
        {
            if (DistToTarget() > ReleaseReach + BodyRadius + 1.5f)
                foreach (var w in Approach(ReleaseReach + 0.5f, Walk, 120)) yield return w;
            Current = "Pressure Release";
            // the pair that holds the target: front/back when it stands ahead or behind, otherwise left/right
            float rel = Mathf.DeltaAngle(transform.eulerAngles.y, Sim.YawTo(transform.position, Target(), transform.eulerAngles.y));
            bool frontBack = Mathf.Abs(rel) < 45 || Mathf.Abs(rel) > 135;
            float y0 = transform.eulerAngles.y + (frontBack ? 0 : 90);
            for (int k = 0; k < 2; k++)
            {
                var h = W.Hazards.Spawn(new Hazard { Source = "Pressure Release", Shape = HazardShape.Cone, Kind = HazardKind.Strike,
                    Origin = transform.position, Yaw = y0 + 180 * k, Radius = ReleaseReach + BodyRadius, HalfAngle = ReleaseHalf,
                    LockAt = 0, ResolveAt = RelW, Damage = RelDmg, Knockback = 1.8f });
                h.OnResolve = hz => { W.BossFx.SteamRelease(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(0.8f); };
            }
            rig.Vents(frontBack, true);
            presenter.PlayOnce(frontBack ? "ReleaseFrontBack" : "ReleaseSides", RelW / (float)Sim.Hz);
            for (int i = 0; i < RelW; i++) { Windup(i / (float)RelW); rig.Gauge(i / (float)RelW); yield return 1; }
            rig.Vents(frontBack, false); rig.Gauge(0);
            foreach (var w in Recover("Pressure Release", "Idle", RelRec)) yield return w;
        }

        // ---------------- Casting Cycle (signature) ----------------
        public readonly List<Hazard> Strips = new List<Hazard>();
        IEnumerable<int> CastingCycle()
        {
            // Open Pressure: ram to a new spot first, which changes the strips' angle
            if (phase == 3) foreach (var w in RailRam()) yield return w;
            Current = "Casting Cycle";
            TurnTowards(YawToTarget(), 9999);
            float yaw = transform.eulerAngles.y; var fwd = Sim.Dir(yaw); var right = Vector3.Cross(Vector3.up, fwd);
            // three parallel strips running away from the crucible, numbered across the floor; the first one is on the
            // target's side so the safe move is to cross behind it once it has fired, never to outrun the sequence
            float side = Vector3.Dot(Sim.Flat(Target() - transform.position), right) >= 0 ? 1 : -1;
            var origin = transform.position + fwd * (BodyRadius + 0.2f);
            presenter.PlayOnce("CastingCycle", (StripW + 2 * StripGap) / (float)Sim.Hz);
            rig.Pour(true);
            Strips.Clear();
            for (int k = 0; k < 3; k++)
            {
                var o = origin + right * (side * StripSpacing * (1 - k));
                var h = Lane("Casting Cycle " + (k + 1), o, yaw, StripLength, StripWidth, StripW, StripDmg);
                h.Kind = HazardKind.Ground; h.Knockback = 1f;
                h.OnResolve = hz => { W.BossFx.StripStamp(hz.Origin, hz.Yaw, hz.Length, hz.Width); Release(0.7f); };
                Strips.Add(h);
                RunHud.Float(o + fwd * 1.2f + Vector3.up * 0.4f, (k + 1).ToString(), new Color(1f, 0.75f, 0.35f));   // strike order
                for (int i = 0; i < (k < 2 ? StripGap : StripW); i++) { Windup((k * StripGap + i) / (float)(StripW + 2 * StripGap)); yield return 1; }
            }
            rig.Pour(false);
            foreach (var w in Recover("Casting Cycle (cooling)", "Vulnerable", CastRec)) yield return w;
        }

        // ================ ability set v2 (abilities-v2/ABILITIES.md, proposal) ================
        // Phase plan (Medium; Easy stretches the warnings inside each ability, never the recoveries):
        //   1 Sealed Furnace  Mould Press > 2.0 · Bellows Draw > 3.5 major · Great Casting (pour only) > 2.0
        //   65 %: second form "Casting Heat" (1.5 s, harmless, queued after the current attack, same HP bar)
        //   2 Casting Heat    Gear Chain > 1.5 · Slag Rim + Mould Press > 2.0 · Slag Rim + Gear Chain > 1.5 ·
        //                     Bellows Draw > 3.5 major · Great Casting (pour + 3 spokes) > 2.0
        //   30 %: "Open Pressure" (1.5 s, harmless)
        //   3 Open Pressure   Great Casting (full) > 3.5 major · Slag Rim + Gear Chain > 1.5 · Mould Press > 2.0 ·
        //                     Bellows Draw > 2.0
        // Hard adds Second Press after every Mould Press. The boss walks to the anvil (within 3 m of the centre, 1.4 m/s,
        // at most 1.5 s) before Mould Press, Bellows Draw and the Great Casting.
        public MouldPress Mould { get; private set; }
        public GearChain Chain { get; private set; }
        public BellowsDraw Bellows { get; private set; }
        public GreatCasting Casting { get; private set; }
        public SecondPress Press { get; private set; }
        public CombatWorld World => W;
        public bool SecondForm { get; private set; }
        public int Transitions { get; private set; }
        public void SetCurrent(string name) => Current = name;
        public void Glow(float k) => Windup(k);
        public void Flare(float f = 1) => Release(f);

        // tests: hold the schedule and run only what TestRun hands over
        public bool TestHold;
        IEnumerable<int> testBody;
        public void TestRun(IEnumerable<int> body) => testBody = body;
        public bool TestBusy => testBody != null || testRunning;
        bool testRunning;

        const float AnvilRange = 3f, AnvilMaxWalk = 1.5f;

        void BindAbilities()
        {
            if (Mould != null) return;
            Mould = new MouldPress(); Chain = new GearChain(); Bellows = new BellowsDraw(); Casting = new GreatCasting(); Press = new SecondPress();
            foreach (var a in new CrucivarAbility[] { Mould, Chain, Bellows, Casting, Press }) a.Bind(this, W);
        }

        public IEnumerable<int> WalkToAnvil()
        {
            var to = Sim.Flat(transform.position); if (to.magnitude <= AnvilRange) yield break;
            var dest = to.normalized * (AnvilRange - 0.2f);
            foreach (var w in WalkTo(dest, Walk, Sim.Ticks(AnvilMaxWalk))) yield return w;
        }

        public IEnumerable<int> MouldWithPress(bool forcePress = false)
        {
            foreach (var w in Mould.Run()) yield return w;
            if ((forcePress || W.Difficulty == Difficulty.Hard) && Mould.Last != null)
                foreach (var w in Press.Run(Mould.Last)) yield return w;
        }

        IEnumerable<int> Step(bool walk, Func<IEnumerable<int>> body, string recoverName, string clip, float recover, bool slagRim = false)
        {
            if (walk) foreach (var w in WalkToAnvil()) yield return w;
            if (slagRim) Bellows.SlagRim();
            Attacks++;
            foreach (var w in body()) yield return w;
            foreach (var w in Recover(recoverName, clip, Sim.Ticks(recover))) yield return w;
        }

        IEnumerable<IEnumerable<int>> Plan(int ph)
        {
            switch (ph)
            {
                case 1:
                    yield return Step(true, () => MouldWithPress(), "Mould Press (hammer lifts)", "Idle", 2.0f);
                    yield return Step(true, Bellows.Run, "Bellows Draw (vents cooling)", "Vulnerable", 3.5f);
                    yield return Step(true, () => Casting.Run(0), "Great Casting (cooling)", "Idle", 2.0f);
                    break;
                case 2:
                    yield return Step(false, Chain.Run, "Gear Chain", "Idle", 1.5f);
                    yield return Step(true, () => MouldWithPress(), "Mould Press (hammer lifts)", "Idle", 2.0f, slagRim: true);
                    yield return Step(false, Chain.Run, "Gear Chain", "Idle", 1.5f, slagRim: true);
                    yield return Step(true, Bellows.Run, "Bellows Draw (vents cooling)", "Vulnerable", 3.5f);
                    yield return Step(true, () => Casting.Run(3), "Great Casting (cooling)", "Idle", 2.0f);
                    break;
                default:
                    yield return Step(true, () => Casting.Run(6), "Great Casting (hammer lodged)", "HammerLodged", 3.5f);
                    yield return Step(false, Chain.Run, "Gear Chain", "Idle", 1.5f, slagRim: true);
                    yield return Step(true, () => MouldWithPress(), "Mould Press (hammer lifts)", "Idle", 2.0f);
                    yield return Step(true, Bellows.Run, "Bellows Draw (vents cooling)", "Idle", 2.0f);
                    break;
            }
        }

        // one v2 ability on loop (-lastbellAbility <id>); its phase variant follows the HP phase
        IEnumerable<int> Solo(string id) => id switch
        {
            "crucivar.mould_press" => Step(true, () => MouldWithPress(), "Mould Press (hammer lifts)", "Idle", 2.0f),
            "crucivar.second_press" => Step(true, () => MouldWithPress(forcePress: true), "Second Press (hammer lifts)", "Idle", 2.0f),
            "crucivar.gear_chain" => Step(false, Chain.Run, "Gear Chain", "Idle", 1.5f),
            "crucivar.bellows_draw" => Step(true, Bellows.Run, "Bellows Draw (vents cooling)", "Vulnerable", 3.5f),
            _ => Step(true, () => Casting.Run(phase == 1 ? 0 : phase == 2 ? 3 : 6), "Great Casting (cooling)", "Idle", 3.5f),
        };

        IEnumerable<int> V2Script()
        {
            BindAbilities();
            string solo = CrucivarArgs.Known(CrucivarArgs.Solo) ? CrucivarArgs.Solo : null;
            while (true)
            {
                if (TestHold)
                {
                    if (testBody == null) { Current = "test hold"; yield return 1; continue; }
                    var body = testBody; testRunning = true;
                    foreach (var w in body) yield return w;
                    testBody = null; testRunning = false;
                    continue;
                }
                bool restart = false;
                foreach (var step in solo != null ? new[] { Solo(solo) } : Plan(phase))
                {
                    foreach (var w in step) yield return w;
                    // a transition waits for the current attack to finish; two thresholds in one burst queue one change
                    int want = PhaseFor();
                    if (want != phase) { phase = want; foreach (var w in V2Shift()) yield return w; restart = true; break; }
                    if (TestHold) { restart = true; break; }
                }
                if (!restart) yield return 1;
            }
        }

        // second form at 65 % (insulation falls, glow deepens), Open Pressure at 30 %: 1.5 s, harmless, no heal
        IEnumerable<int> V2Shift()
        {
            Transitions++;
            if (phase >= 2) SecondForm = true;
            foreach (var w in PhaseShift()) yield return w;
        }

        // ---------------- presentation ----------------
        protected override void StepAlways()
        {
            if (!presenter) return;
            bool walking = Current == "approach" || Current == "reposition";
            presenter.TickBoss(walking ? "Walk" : "Idle");
            rig.Walking = walking;
        }

        protected override void Update()
        {
            base.Update();
            if (presenter) presenter.GlowLevel = glowLevel;
            if (rig) rig.Heat = glowLevel;
        }

        protected override void OnDefeat()
        {
            // heat fades white/orange to red, kneels, a harmless final pour into its own crucible, locks solid
            glowTarget = 0.05f; glowLevel = 2.5f;
            presenter.PlayOnce("Defeat", 3f);
            rig.Shutdown();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
