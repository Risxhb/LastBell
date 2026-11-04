using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;
using R = LastBell.Run.CrucibleSkaterRules;

namespace LastBell.Run
{
    // The Crucible Skater: a practice-only Brass Foundry miniboss (docs/bosses/crucible-skater.md, a proposal). A small,
    // fast skater on the molten floor; the party decides where its route goes, whether it finishes a run and who it
    // chases next. Four mechanics:
    //
    //   Figure Carve    numerals I.. on the players (farthest first); lanes skater -> I -> II ... follow them 1.5 s, lock,
    //                   then it skates the legs (40 Strike); every leg it skated burns 20/s for 7 s. Solo: three legs, each
    //                   re-aimed. 2p I-II-I. Phase 2: one more leg, 9 s trails. Hard: Retrace (the route skated back).
    //   Body Check      the farthest player is the quarry; a block ring right in front of it and a charge lane through the
    //                   quarry. Someone (not the quarry) in the ring: they split 60 and it trips, stuck 3.5 s. Nobody: it
    //                   charges (55) and leaves a trail. Phase 2: Turns, two quarries in a row (the second is someone else).
    //   Crucible Toss   two pours 5.4 m out, opposite sides, numeral = players needed (1+1, 2+1, 2+2; solo one then the
    //                   other); 70 split among those inside, an empty pour wipes the party. Phase 2: with the Rink Lap.
    //   Rink Lap        the band from 8.5 m to the wall, then a full lap (45 to anyone it passes there), then Pirouette: a 6 m
    //                   circle at the centre (50). Then dizzy 3.5 s: the major punish window, every cast.
    //
    // 50%: it finishes the current attack, then a harmless 2 s reheat (damageable, no heal). No enrage. Placeholder look only:
    // code-built primitives, no model, clips or audio. Every warning wears the raid style (HazardSystem.RaidStyle, Brass
    // Foundry motif). PhaseName and Lesson stay empty; names live in Current / BlockLog for tests and diagnostics.
    public sealed class CrucibleSkaterEncounter : BossEncounter
    {
        public override string Id => "boss.crucible-skater";
        public override string DisplayName => "The Crucible Skater";
        public override string Lesson => "";
        public override bool Placeholder => true;
        public override float NextPhaseFraction() => 0.49f;
        public const int Motif = 3;   // Telegraph-Style v2 area motif: Brass Foundry
        public const int MaxHpMedium = 8400;   // doubled after review (3 Nov); solo base, scaled for the party below
        // co-op HP: 1 + 0.7 x (fighters - 1), the shared formula (BossEncounter.Spawn); passive bots don't count (review, 3 Nov)
        protected override bool PartyScaled => true;

        static readonly Color Brass = new Color(0.78f, 0.6f, 0.3f), Ivory = new Color(0.94f, 0.9f, 0.8f), Cobalt = new Color(0.16f, 0.24f, 0.55f),
            Molten = new Color(1.0f, 0.55f, 0.15f), SoakRim = new Color(1.6f, 1.35f, 0.7f), BlockRim = new Color(1.5f, 1.5f, 1.3f);

        // ---------------- state (tests and diagnostics) ----------------
        public bool Phase2 { get; private set; }
        public bool GatePending { get; private set; }
        public int Blocks { get; private set; }
        public readonly List<string> BlockLog = new List<string>();
        public string ForcedNext;
        public int Seed = Environment.TickCount;
        public Difficulty Level => W != null ? W.Difficulty : Difficulty.Medium;
        public int Tick => W != null ? W.Tick : 0;
        public int ShiftStart { get; private set; } = -1;
        public int ShiftEnd { get; private set; } = -1;
        public int HpAtShiftStart { get; private set; }
        public bool Stuck { get; private set; }
        public bool Dizzy { get; private set; }

        public int CarveCasts, CheckCasts, TossCasts, LapCasts, Trips, Charges, TrailsSpawned, RetraceLegs;
        // a leg: when it started, where from/to, who it was aimed at, how long after its lock it started
        public readonly List<(int tick, Vector3 from, Vector3 to, PlayerController who, int afterLock)> LegLog = new List<(int, Vector3, Vector3, PlayerController, int)>();
        public readonly List<(int tick, PlayerController quarry, bool blocked, int blockers, int share)> CheckLog = new List<(int, PlayerController, bool, int, int)>();
        public readonly List<(int tick, int need, int inside, int share, bool wiped)> PourLog = new List<(int, int, int, int, bool)>();
        public readonly List<(int tick, int bandHits, int pirouetteHits)> LapLog = new List<(int, int, int)>();
        public readonly List<(int start, int end, string why)> PunishLog = new List<(int, int, string)>();
        public int LastCarveLegs { get; private set; }

        enum Atk { Carve, Check, Toss, Lap, Turns, TossLap }
        static readonly Atk[] RotationHigh = { Atk.Carve, Atk.Check, Atk.Toss, Atk.Lap };
        static readonly Atk[] RotationLow = { Atk.Carve, Atk.Turns, Atk.TossLap };
        public static string[] Rotation(bool phase2) => Array.ConvertAll(phase2 ? RotationLow : RotationHigh, a => a.ToString());
        int rotHigh, rotLow, lapSign = 1;
        System.Random rng;
        readonly List<PlayerController> marked = new List<PlayerController>();

        // look (placeholder)
        Transform body, spinner, bowl; Renderer core;
        float spin, spinRate, wobble;

        void Awake() { MaxHp = MaxHpMedium; BodyRadius = R.BodyRadius; HitHeight = 1.2f; MarkHeight = 2.6f; }

        bool Hard => Level == Difficulty.Hard;
        int Warn(float s) => R.Warn(s, Level);
        void Log(string s) => BlockLog.Add($"{Tick} {(Phase2 ? "P2" : "P1")} {s}");
        List<PlayerController> Standing() { var l = new List<PlayerController>(); foreach (var p in W.Players) if (p && !p.Down) l.Add(p); return l; }
        Vector3 Pos => Sim.Flat(transform.position);

        // every warning: the shared raid look with the Brass Foundry motif
        Hazard Spawn(Hazard h) { W.Hazards.Spawn(h); HazardSystem.RaidStyle(h, Motif); return h; }
        bool IsLive(Hazard h) { if (h == null) return false; var live = W.Hazards.Live; for (int i = 0; i < live.Count; i++) if (live[i] == h) return true; return false; }

        void MoveTo(Vector3 p, bool face = true)
        {
            p = Sim.Flat(p); var d = p - Pos;
            if (face && d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Euler(0, Sim.YawTo(Pos, p, transform.eulerAngles.y), 0);
            transform.position = p;
        }

        // ---------------- placeholder body ----------------
        protected override void Build()
        {
            PhaseName = "";
            R.Bound = CombatWorld.PlayerBound;
            body = new GameObject("Crucible Skater (STAND-IN placeholder)").transform; body.SetParent(transform, false);
            spinner = new GameObject("Spinner").transform; spinner.SetParent(body, false);
            Prim.Make(PrimitiveKind.Capsule, spinner, new Vector3(0, 0.95f, 0), new Vector3(0.62f, 0.6f, 0.5f), Brass).name = "Body (stand-in)";
            Prim.Make(PrimitiveKind.Sphere, spinner, new Vector3(0, 1.55f, 0.08f), new Vector3(0.42f, 0.42f, 0.36f), Ivory).name = "Mask (stand-in)";
            bowl = Prim.Make(PrimitiveKind.Cylinder, spinner, new Vector3(0, 1.88f, 0), new Vector3(0.62f, 0.12f, 0.62f), Brass);
            bowl.name = "Crucible (stand-in)";
            core = Prim.Make(PrimitiveKind.Cylinder, spinner, new Vector3(0, 1.99f, 0), new Vector3(0.5f, 0.02f, 0.5f), Molten, 2f).GetComponent<Renderer>();
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                Prim.Make(PrimitiveKind.Cube, spinner, new Vector3(x, 0.2f, 0), new Vector3(0.16f, 0.32f, 0.3f), Cobalt).name = "Boot (stand-in)";
                Prim.Make(PrimitiveKind.Cube, spinner, new Vector3(x, 0.03f, 0.05f), new Vector3(0.05f, 0.05f, 1.1f), Ivory, 0.6f).name = "Blade (stand-in)";
            }
            Prim.Make(PrimitiveKind.Cube, spinner, new Vector3(0, 1.2f, 0.27f), new Vector3(0.28f, 0.2f, 0.06f), Molten, 1.2f).name = "Core (stand-in)";
        }

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            rng ??= new System.Random(Seed);
            if (Pos.magnitude > 4f) { var to = Pos.normalized * 2f; foreach (var w in WalkTo(to, 8f, 200)) yield return w; }
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
                IEnumerable<int> steps = atk switch
                {
                    Atk.Carve => Carve(),
                    Atk.Check => Check(1),
                    Atk.Turns => Check(2),
                    Atk.Toss => Toss(false),
                    Atk.TossLap => Toss(true),
                    _ => Lap(),
                };
                foreach (var w in steps) yield return w;
                ClearMarks();
                Current = "between blocks"; Stuck = false; Dizzy = false; spinRate = 0;
                yield return R.Ticks(R.GapSeconds);
            }
        }

        protected override void StepAlways()
        {
            StepLadles();
            if (!Phase2 && !GatePending && Hp * 2 <= MaxHp) GatePending = true;
        }

        public override float[] PhaseStarts => new[] { 1f, 0.5f };
        public override int PhaseNumber => Phase2 ? 2 : 1;
        protected override void EnterPhase(int n) { if (!Phase2) { Phase2 = true; GatePending = false; } }

        // ================= 1. Figure Carve =================
        IEnumerable<int> Carve()
        {
            var pl = Standing(); int n = pl.Count; if (n == 0) yield break;
            pl.Sort((a, b) => Sim.Flat(b.transform.position - transform.position).sqrMagnitude.CompareTo(Sim.Flat(a.transform.position - transform.position).sqrMagnitude));
            Current = "Figure Carve"; CarveCasts++;
            var order = R.CarveOrder(n, Phase2); int L = order.Length; LastCarveLegs = L;
            bool solo = n == 1;
            if (!solo) for (int i = 0; i < n; i++) { var id = MechanicIcons.Order(i + 1); if (id != null) { pl[i].Marks.Add(id); marked.Add(pl[i]); } }
            int track = Warn(R.CarveTrack), crouch = Warn(R.CarveCrouch);
            int trailLife = R.Ticks(Phase2 ? R.TrailLifeP2 : R.TrailLife);
            const int Never = 1 << 28;
            var lanes = new Hazard[L]; var spawnT = new int[L]; var lockT = new int[L]; var startT = new int[L]; var arriveT = new int[L];
            var from = new Vector3[L]; var stop = new Vector3[L]; var dir = new Vector3[L];
            for (int k = 0; k < L; k++) { lockT[k] = Never; startT[k] = Never; arriveT[k] = Never; }
            var origin = Pos; var fallback = transform.forward;
            int cur = 0;
            for (int t = 0; cur < L && t < 60 * 30; t++)
            {
                // show: co-op every lane at once; solo leg k when leg k-1 locks
                for (int k = 0; k < L; k++)
                {
                    if (lanes[k] != null) continue;
                    if (!(k == 0 || !solo || t >= lockT[k - 1])) continue;
                    spawnT[k] = t; lockT[k] = t + track;
                    lanes[k] = Spawn(new Hazard
                    {
                        Source = "Figure Carve", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = origin,
                        Yaw = 0, Length = R.LaneLength, Width = R.LaneWidth, LockAt = track, ResolveAt = Never, Damage = R.CarveDmg,
                        Knockback = 0, Stun = 6, HitOnResolve = false, ClipRadius = R.Bound + 0.6f,
                    });
                    lanes[k].FillToLock = true;   // the fill counts down to its lock, then holds until the body has passed
                }
                // geometry: unlocked lanes follow their player, from wherever the previous leg ends
                for (int k = 0; k < L; k++)
                {
                    var h = lanes[k]; if (h == null) break;
                    if (t < lockT[k])
                    {
                        from[k] = k == 0 ? origin : stop[k - 1];
                        var who = pl[order[k]]; var wp = who && !who.Down ? Sim.Flat(who.transform.position) : from[k] + fallback * 4f;
                        dir[k] = R.LegDir(from[k], wp, fallback); stop[k] = R.LegStop(from[k], wp, fallback);
                        h.Origin = from[k]; h.Yaw = Sim.YawTo(from[k], from[k] + dir[k], 0);
                    }
                    // schedule: a leg starts when the previous one arrives, and never sooner than a crouch after its own lock
                    int prev = k == 0 ? 0 : arriveT[k - 1];
                    startT[k] = lockT[k] >= Never ? Never : Mathf.Max(prev, lockT[k] + crouch);
                    arriveT[k] = startT[k] >= Never ? Never : startT[k] + Mathf.Max(4, Mathf.CeilToInt(Sim.Flat(stop[k] - from[k]).magnitude / R.SkateSpeed * Sim.Hz));
                    if (arriveT[k] < Never) h.ResolveAt = Mathf.Max(h.Age + 1, arriveT[k] + 1 - spawnT[k]);
                }
                if (t == 0) { Windup(0.2f); }
                // skating
                if (t >= startT[cur])
                {
                    if (t == startT[cur])
                    {
                        Current = $"Figure Carve (leg {cur + 1}/{L})";
                        LegLog.Add((Tick, from[cur], stop[cur], pl[order[cur]], startT[cur] - lockT[cur]));
                        W.BossFx.ChargeTrail(transform, true);
                    }
                    int dur = arriveT[cur] - startT[cur];
                    float u = Mathf.Clamp01((t - startT[cur] + 1) / (float)dur), len = Sim.Flat(stop[cur] - from[cur]).magnitude;
                    MoveTo(Vector3.Lerp(from[cur], stop[cur], u));
                    BodyHits(lanes[cur], from[cur], len * u);
                    if (t + 1 >= arriveT[cur])
                    {
                        // the blow carries to the edge as it arrives
                        Wake(lanes[cur]);
                        Trail("Carve trail", from[cur], stop[cur], trailLife);
                        W.BossFx.ChargeLand(transform.position); CameraShake.Shake(0.08f, 0.12f); Release(0.6f);
                        cur++;
                    }
                }
                else Windup(Mathf.Clamp01(t / (float)Mathf.Max(1, lockT[0] + crouch)));
                yield return 1;
            }
            W.BossFx.ChargeTrail(transform, false);
            ClearMarks();
            if (Hard) foreach (var w in Retrace(from, stop, trailLife)) yield return w;
            Current = "Figure Carve (recovery)";
            yield return R.Ticks(0.4f);
        }

        // Hard: the whole route skated back, each segment shown at least 1.0 s before the body passes
        IEnumerable<int> Retrace(Vector3[] from, Vector3[] stop, int trailLife)
        {
            Current = "Retrace";
            var segs = new List<(Vector3 a, Vector3 b)>();
            for (int k = from.Length - 1; k >= 0; k--) if (Sim.Flat(stop[k] - from[k]).magnitude > 0.5f) segs.Add((stop[k], from[k]));
            if (segs.Count == 0) yield break;
            int warn = R.Ticks(R.RetraceWarn), at = warn;
            var lanes = new List<(Hazard h, int start, int end)>();
            foreach (var (a, b) in segs)
            {
                float len = Sim.Flat(b - a).magnitude; int dur = Mathf.Max(4, Mathf.CeilToInt(len / R.SkateSpeed * Sim.Hz));
                var h = Spawn(new Hazard
                {
                    Source = "Retrace", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = a, Yaw = Sim.YawTo(a, b, 0),
                    Length = len + R.BodyRadius, Width = R.LaneWidth, LockAt = 0, ResolveAt = at + dur + 1, Damage = R.CarveDmg, Knockback = 0, Stun = 6,
                    HitOnResolve = false, ClipRadius = R.Bound + 0.6f,
                });
                lanes.Add((h, at, at + dur)); at += dur;
            }
            W.BossFx.ChargeTrail(transform, true);
            for (int t = 0; t < at; t++)
            {
                foreach (var (h, s, e) in lanes)
                {
                    if (t < s || t >= e) continue;
                    if (t == s) RetraceLegs++;
                    float u = Mathf.Clamp01((t - s + 1) / (float)(e - s)), len = h.Length - R.BodyRadius;
                    MoveTo(h.Origin + Sim.Dir(h.Yaw) * (len * u));
                    BodyHits(h, h.Origin, len * u);
                }
                yield return 1;
            }
            W.BossFx.ChargeTrail(transform, false);
            W.BossFx.ChargeLand(transform.position);
        }

        // whoever the body passes on a lane (it reaches its front edge plus its body), once each
        void BodyHits(Hazard lane, Vector3 from, float travelled)
        {
            foreach (var p in W.Players)
            {
                if (p.Down || lane.Hit.Contains(p)) continue;
                var l = Quaternion.Euler(0, -lane.Yaw, 0) * Sim.Flat(p.transform.position - from);
                if (Mathf.Abs(l.x) > lane.Width / 2 + Hazard.Margin || l.z < -Hazard.Margin || l.z > travelled + R.BodyRadius) continue;
                lane.Hit.Add(p); p.ReceiveHit(lane);
            }
        }
        void Wake(Hazard lane)
        {
            foreach (var p in W.Players)
            {
                if (p.Down || lane.Hit.Contains(p) || !lane.Contains(p.transform.position)) continue;
                lane.Hit.Add(p); p.ReceiveHit(lane);
            }
        }
        void Trail(string source, Vector3 a, Vector3 b, int life)
        {
            float len = Sim.Flat(b - a).magnitude; if (len < 0.5f) return;
            Spawn(new Hazard
            {
                Source = source, Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = a, Yaw = Sim.YawTo(a, b, 0), Length = len, Width = R.TrailWidth,
                LockAt = 0, ResolveAt = life, BurnPerSecond = R.TrailBurn, Knockback = 0, Stun = 0, ClipRadius = R.Bound + 0.6f,
            });
            TrailsSpawned++;
        }

        // ================= 2. Body Check =================
        IEnumerable<int> Check(int turns)
        {
            CheckCasts++;
            PlayerController last = null;
            for (int i = 0; i < turns; i++)
            {
                var pl = Standing(); if (pl.Count == 0) yield break;
                bool solo = pl.Count == 1;
                // the quarry: the farthest player, and on a second turn the farthest who wasn't the last quarry
                PlayerController quarry = null; float best = -1;
                foreach (var p in pl) { if (p == last && pl.Count > 1) continue; float d = Sim.Flat(p.transform.position - transform.position).sqrMagnitude; if (d > best) { best = d; quarry = p; } }
                last = quarry;
                Current = turns > 1 ? $"Body Check (turn {i + 1})" : "Body Check";
                quarry.Marks.Add(MechanicIcons.CarriedMark); marked.Add(quarry);
                int lockAt = Warn(R.CheckLock), res = Warn(R.CheckResolve), need = R.BlockersNeeded(pl.Count);
                Hazard ring = null, lane = null; bool blocked = false; int blockers = 0, share = 0;
                ring = new Hazard
                {
                    Source = "Body Check (block)", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Radius = R.RingRadius,
                    LockAt = lockAt, ResolveAt = res, ShareTotal = R.BlockTotal, ShareCount = need, Soak = true, Knockback = 0, Stun = 6,
                };
                var q = quarry;
                if (!solo) ring.Includes = p => p != q && ring.Contains(p.transform.position);
                ring.OnResolve = hz =>
                {
                    blockers = hz.Hit.Count; blocked = blockers > 0; share = R.Share(R.BlockTotal, blockers);
                    if (blocked && lane != null) W.Hazards.Cancel(lane);
                };
                lane = new Hazard
                {
                    Source = "Body Check", MechanicId = MechanicIcons.Lane, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Length = R.LaneLength, Width = R.CheckWidth,
                    LockAt = lockAt, ResolveAt = res + 1, Damage = R.ChargeDmg, Knockback = 0, Stun = 6, ClipRadius = R.Bound + 0.6f,
                };
                Vector3 dir = transform.forward;
                void Aim()
                {
                    if (ring.Locked) return;
                    dir = R.LegDir(Pos, q && !q.Down ? Sim.Flat(q.transform.position) : Pos + dir * 4f, transform.forward);
                    var c = Pos + dir * (R.BodyRadius + 0.1f + R.RingRadius);
                    ring.Origin = c; lane.Origin = c + dir * R.RingRadius; lane.Yaw = Sim.YawTo(Pos, Pos + dir, 0);
                    transform.rotation = Quaternion.Euler(0, lane.Yaw, 0);
                }
                Aim();
                Spawn(ring); if (ring.Tele != null) { ring.Tele.Friendly(BlockRim); ring.Tele.Order = 2; }
                Spawn(lane);
                Numeral.Show(W, ring, need.ToString(), 2.4f);
                for (int t = 0; t <= res + 1; t++) { Aim(); Windup(t / (float)res); yield return 1; }
                quarry.Marks.Remove(MechanicIcons.CarriedMark); marked.Remove(quarry);
                CheckLog.Add((Tick, quarry, blocked, blockers, share));
                if (blocked)
                {
                    // tripped: stuck in front of the blockers, the bonus punish window
                    Trips++; Current = "Body Check (tripped)"; Stuck = true; wobble = 1;
                    W.BossFx.ChargeLand(ring.Origin); CameraShake.Shake(0.14f, 0.2f); Release(1.2f);
                    int s0 = Tick; yield return R.Ticks(R.StuckSeconds);
                    PunishLog.Add((s0, Tick, "tripped")); Stuck = false; wobble = 0;
                }
                else
                {
                    // the charge already hit the lane; it carries on to the bound and leaves a trail
                    Charges++; Current = "Body Check (charge)";
                    var a = Pos; var b = R.ClampToBound(a, a + dir * R.LaneLength);
                    Trail("Check trail", a, b, R.Ticks(Phase2 ? R.TrailLifeP2 : R.TrailLife));
                    W.BossFx.ChargeTrail(transform, true);
                    int dur = Mathf.Max(4, Mathf.CeilToInt(Sim.Flat(b - a).magnitude / R.ChargeSpeed * Sim.Hz));
                    for (int t = 1; t <= dur; t++) { MoveTo(Vector3.Lerp(a, b, t / (float)dur)); yield return 1; }
                    W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position); CameraShake.Shake(0.18f, 0.25f); Release(1f);
                    yield return R.Ticks(0.5f);
                }
            }
        }

        // ================= 3. Crucible Toss =================
        IEnumerable<int> Toss(bool withLap)
        {
            var pl = Standing(); int n = pl.Count; if (n == 0) yield break;
            TossCasts++; Current = withLap ? "Crucible Toss + Rink Lap" : "Crucible Toss";
            var counts = R.PourCounts(n);
            var spots = R.PourSpots(R.PourLayouts[rng.Next(R.PourLayouts.Length)] + (rng.Next(2) == 0 ? 0 : 180));
            int land = Warn(R.PourLand), gap = R.PoursInSequence(n) ? Warn(R.PourGapSolo) : 0;
            var pours = new List<Hazard>();
            for (int i = 0; i < 2; i++)
            {
                int need = counts[i];
                var h = new Hazard
                {
                    Source = "Crucible Toss", MechanicId = MechanicIcons.SoakTower, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = spots[i],
                    Radius = R.PourRadius, LockAt = 0, ResolveAt = land + gap * i, ShareTotal = R.PourTotal, ShareCount = need, WipeIfEmpty = true, Soak = true,
                    Knockback = 0, Stun = 6,
                };
                h.OnResolve = hz =>
                {
                    int inside = hz.Hit.Count;
                    PourLog.Add((Tick, need, inside, R.Share(R.PourTotal, inside), inside == 0));
                    W.BossFx.TollResolve(hz.Origin, 0, R.PourRadius); CameraShake.Shake(inside == 0 ? 0.3f : 0.1f, 0.2f);
                };
                Spawn(h); if (h.Tele != null) { h.Tele.Friendly(SoakRim); h.Tele.Order = 2; }
                Numeral.Show(W, h, need.ToString(), 2.6f);
                Ladle(transform.position + Vector3.up * 2f, spots[i], Age + Mathf.Max(1, h.ResolveAt - R.Ticks(1f)), Age + h.ResolveAt);
                pours.Add(h);
            }
            Release(0.8f);
            if (withLap) foreach (var w in Lap()) yield return w;
            for (int guard = 0; guard < 900 && pours.Exists(IsLive); guard++) yield return 1;
        }

        // ================= 4. Rink Lap -> Pirouette =================
        IEnumerable<int> Lap()
        {
            LapCasts++; bool alone = !Current.StartsWith("Crucible Toss"); string pre = alone ? "" : "Crucible Toss + ";
            Current = pre + "Rink Lap";
            float a0 = Pos.magnitude > 0.5f ? Sim.YawTo(Vector3.zero, Pos, 0) : (float)(rng.NextDouble() * 360.0);
            int warn = Warn(R.LapWarn), lap = R.Ticks(R.LapTime);
            var band = Spawn(new Hazard
            {
                Source = "Rink Lap", MechanicId = MechanicIcons.Ring, Shape = HazardShape.Ring, Kind = HazardKind.Strike, Origin = Vector3.zero, Inner = R.BandInner,
                Radius = R.Bound + 1f, LockAt = 0, ResolveAt = warn + lap, Damage = R.LapDmg, Knockback = 0, Stun = 6, HitOnResolve = false, ClipRadius = R.Bound + 0.6f,
            });
            // to the rim during the warning
            var rim = Sim.Dir(a0) * R.BossBound; var start = Pos;
            int go = Mathf.Max(4, Mathf.Min(warn - 2, Mathf.CeilToInt(Sim.Flat(rim - start).magnitude / R.SkateSpeed * Sim.Hz)));
            for (int t = 0; t < warn; t++) { if (t < go) MoveTo(Vector3.Lerp(start, rim, (t + 1) / (float)go)); Windup(t / (float)warn); yield return 1; }
            Current = pre + "Rink Lap (lapping)";
            W.BossFx.ChargeTrail(transform, true);
            float sign = lapSign; lapSign = -lapSign; int bandHits = 0;
            for (int t = 1; t <= lap; t++)
            {
                float ang = a0 + sign * 360f * t / lap;
                var p = Sim.Dir(ang) * R.BossBound;
                transform.position = p; transform.rotation = Quaternion.Euler(0, ang + sign * 90f, 0);
                foreach (var pc in W.Players)
                {
                    if (pc.Down || band.Hit.Contains(pc) || !band.Contains(pc.transform.position)) continue;
                    var pp = Sim.Flat(pc.transform.position); if (pp.sqrMagnitude < 0.01f) continue;
                    if (Mathf.Abs(Mathf.DeltaAngle(Sim.YawTo(Vector3.zero, pp, 0), ang)) > R.LapHalfArc) continue;
                    band.Hit.Add(pc); pc.ReceiveHit(band); bandHits++;
                }
                yield return 1;
            }
            W.BossFx.ChargeTrail(transform, false);
            // Pirouette: to the centre, then a spin over the 6 m circle
            Current = pre + "Pirouette";
            int pw = Warn(R.PirouetteWarn), pirouetteHits = 0;
            var circle = Spawn(new Hazard
            {
                Source = "Pirouette", MechanicId = null, Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = Vector3.zero, Radius = R.PirouetteRadius,
                LockAt = 0, ResolveAt = pw, Damage = R.PirouetteDmg, Knockback = 0, Stun = 6,
            });
            circle.OnResolve = hz => { pirouetteHits = hz.Hit.Count; W.BossFx.TollResolve(Vector3.zero, 0, R.PirouetteRadius); CameraShake.Shake(0.16f, 0.22f); Release(1.2f); };
            var from = Pos; int toC = R.Ticks(R.ToCentreTime);
            for (int t = 0; t <= pw; t++)
            {
                if (t < toC) MoveTo(Vector3.Lerp(from, Vector3.zero, (t + 1) / (float)toC));
                else spinRate = Mathf.Lerp(200f, 1400f, (t - toC) / (float)Mathf.Max(1, pw - toC));
                Windup(t / (float)pw);
                yield return 1;
            }
            spinRate = 0;
            LapLog.Add((Tick, bandHits, pirouetteHits));
            // dizzy at the centre: the major punish window (nothing of its own is live)
            Current = "Rink Lap (dizzy)"; Dizzy = true; wobble = 1;
            int s0 = Tick; yield return R.Ticks(R.DizzySeconds);
            PunishLog.Add((s0, Tick, "dizzy")); Dizzy = false; wobble = 0;
        }

        // ================= the 50% reheat =================
        IEnumerable<int> Transition()
        {
            GatePending = false; ClearMarks();
            Current = "Reheat"; Log("Reheat"); ShiftStart = Tick; HpAtShiftStart = Hp;
            CameraShake.Shake(0.12f, 0.3f);
            int n = R.Ticks(R.ShiftSeconds);
            for (int t = 0; t < n; t++) { spinRate = 900f * Mathf.Sin(Mathf.PI * t / n); Windup(t / (float)n); yield return 1; }   // harmless: nothing spawned
            spinRate = 0; Phase2 = true; Release(1.5f); ShiftEnd = Tick;
            Log("phase 2");
        }

        void ClearMarks()
        {
            foreach (var p in marked)
                if (p) p.Marks.RemoveAll(m => m == MechanicIcons.CarriedMark || (m != null && m.StartsWith("order_")));
            marked.Clear();
        }

        // ---------------- thrown ladles (presentation) ----------------
        sealed class Flying { public Transform View; public Vector3 From, To; public int Start, End; }
        readonly List<Flying> ladles = new List<Flying>();
        public int LadleCount => ladles.Count;
        void Ladle(Vector3 from, Vector3 to, int start, int end)
        {
            var t = Prim.Make(PrimitiveKind.Sphere, transform.parent, from, Vector3.one * 0.7f, Molten, 2f);
            t.name = "Ladle of brass (placeholder)"; t.gameObject.SetActive(false);
            ladles.Add(new Flying { View = t, From = from, To = to, Start = start, End = end });
        }
        void StepLadles()
        {
            for (int i = ladles.Count - 1; i >= 0; i--)
            {
                var f = ladles[i];
                if (Age < f.Start) continue;
                float u = Mathf.Clamp01((Age - f.Start) / (float)Mathf.Max(1, f.End - f.Start));
                if (f.View) { f.View.gameObject.SetActive(true); f.View.position = Vector3.Lerp(f.From, f.To, u) + Vector3.up * (9f * u * (1 - u) + 0.3f); }
                if (Age >= f.End) { if (f.View) Destroy(f.View.gameObject); ladles.RemoveAt(i); }
            }
        }
        void ClearLadles() { foreach (var f in ladles) if (f.View) Destroy(f.View.gameObject); ladles.Clear(); }

        // ---------------- lifecycle ----------------
        protected override void Update()
        {
            base.Update();
            if (!spinner) return;
            spin += spinRate * Time.deltaTime;
            float wob = wobble > 0 ? Mathf.Sin(Time.time * 9f) * 9f * wobble : 0;
            spinner.localRotation = Quaternion.Euler(wob, spin, Dead ? 70 : wob * 0.6f);
            if (core) Prim.SetGlow(core, Molten * (1.2f + glowLevel * 1.4f));
        }

        protected override void OnDefeat()
        {
            ClearLadles(); ClearMarks();
            Stuck = false; Dizzy = false; spinRate = 0; wobble = 0;
            W.Fx.Crescendo(transform.position, true, 2, true);
        }

        void OnDestroy() { ClearLadles(); if (W != null) ClearMarks(); }

        // Ivory numeral over a soak (friendly identity, never a hazard colour) until its hazard leaves the floor.
        public sealed class Numeral : MonoBehaviour
        {
            CombatWorld w; Hazard h; float height;
            public static void Show(CombatWorld world, Hazard hazard, string text, float height)
            {
                var go = new GameObject("Soak numeral " + text); go.transform.SetParent(world.Root, false);
                var n = go.AddComponent<Numeral>(); n.w = world; n.h = hazard; n.height = height;
                var tm = go.AddComponent<TextMesh>();
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
                tm.anchor = TextAnchor.MiddleCenter; tm.characterSize = 0.22f; tm.fontSize = 72; tm.fontStyle = FontStyle.Bold;
                tm.color = new Color(1f, 0.95f, 0.82f); tm.text = text;
                n.Update();
            }
            void Update()
            {
                bool live = false; if (w != null) { var l = w.Hazards.Live; for (int i = 0; i < l.Count; i++) if (l[i] == h) { live = true; break; } }
                if (!live) { Destroy(gameObject); return; }
                transform.position = h.Origin + Vector3.up * height;
                var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }
        }
    }
}
