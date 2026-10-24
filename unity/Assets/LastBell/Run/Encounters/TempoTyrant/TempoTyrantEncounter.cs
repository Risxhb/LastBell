using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Tempo Tyrant: a practice-only miniboss for the Metronome Court (docs/metronome-arena/tempo-tyrant.md).
    // Greatsword knight, labelled stand-in body until the final model/rig exists. All numbers are tunable first-pass
    // proposals, not approved balance. Geometry is in metres before the 0.3 m player margin; damage against 100 HP.
    //
    // Tempo: one TempoClock (starts in Haste). Attack timings are authored in beats and converted with the tempo that
    // is current when the block starts; a block never changes tempo mid-way. A duration expiry only requests a change;
    // at the next complete block boundary the boss stands still for three accented metronome ticks and the new tempo
    // commits on the third. Damage is identical in both tempos.
    //
    //   Threefold Severance  1 left cleave (x < +2.5 m in the boss frame), 2 right cleave (x > -2.5), 3 overhead front
    //                        band (|x| < 3.5, ahead of his back plate). Opening 9 beats (6 and 8 failed far-start route checks), 4 beats between impacts,
    //                        recovery 6 beats. Position and facing locked before the first preview; he never swivels.
    //   Sundering Wake       3 m lane to a distant player (+2.6 m thrust reach), 6-beat warning, travel 0.45 s (hit when the blade arrives),
    //                        then a separate 2.2 m eruption lane on the same path, 5-beat warning, resolves once.
    //   Hollow Crown         ring outside a 4 m pocket (8 beats), then a 5.5 m circle under him (4 beats more).
    //   Closing Rush         announced, non-damaging: after 1.5 s with the pursuit target beyond 9 m.
    //   Broken Cadence       below 20% HP (once, queued to a block boundary): every Haste passage opens with
    //                        Sundering Wake -> Threefold Severance, fissure always erupting >= 8 beats before strike 1.
    public sealed class TempoTyrantEncounter : BossEncounter
    {
        public override string Id => "boss.tempo-tyrant";
        public override string DisplayName => "The Tempo Tyrant";
        public override string Lesson => "Follow the numbered 1-2-3 into cleared ground; the slow tempo is your opening";
        public override bool Placeholder => true;   // stand-in body; the final model/rig is a remaining art gate
        public override float NextPhaseFraction() => 0.19f;

        // ---------------- tunables (see the tunables table in the encounter doc) ----------------
        public const float CentreGate = 1.5f;                     // combo/crown start only this close to the arena centre
        public const float OwnBound = 8.5f;                     // charge / rush endpoints: >= 2 m wall corridor behind the body
        public const float SevSide = 2.5f, SevBand = 3.5f, SevReach = 30f;
        public const float SevOpenBeats = 9, SevGapBeats = 4, SevRecoverBeats = 6;
        public const float WakeWidth = 3f, FissureWidth = 2.2f, WakeWarnBeats = 6, FissureWarnBeats = 5, WakeRecoverBeats = 4,
            WakeOvershoot = 2f, WakeMaxTravel = 15f, WakeMinTravel = 4f, WakeReach = 2.6f;   // reach: greatsword thrust beyond the body
        public const int WakeTravelTicks = 27;
        public const float CrownPocket = 4f, CrownInner = 5.5f, CrownRingBeats = 8, CrownInnerBeats = 4, CrownRecoverBeats = 8;
        public const float FightDist = 4.5f, RushDist = 9f, RushHysteresis = 1.5f, RushSpeed = 16f, RushTellBeats = 3;
        public const int RushSustainTicks = 90, RushCooldownTicks = 8 * Sim.Hz, PursuitMaxTicks = 75;
        public const float HasteWalk = 3.4f, NormalWalk = 2.6f;
        public const float FrenzyAt = 0.2f;
        public const int FrenzyTellTicks = 90;
        public const int SevDmg1 = 26, SevDmg2 = 26, SevDmg3 = 30, WakeDmg = 24, FissureDmg = 16, RingDmg = 22, InnerDmg = 22;
        public const int FissureLeadBeats = 8;                  // chain: eruption at least this many beats before strike 1

        public readonly TempoClock Clock = new TempoClock();
        public TempoTyrantRig Rig { get; private set; }
        public TempoTyrantAudio Audio { get; private set; }
        public bool Frenzy { get; private set; }
        public bool FrenzyPending { get; private set; }
        public int FrenzyTriggers { get; private set; }
        public bool ChainDue { get; private set; }
        public int ChainsRun { get; private set; }
        public int Blocks { get; private set; }
        public int Rushes { get; private set; }
        public int RushCancels { get; private set; }
        public PlayerController PursuitTarget { get; private set; }
        public string Announcement { get; private set; } = "";   // HUD: big non-damaging tells (rush, frenzy, tempo shift)
        public int WakeTravelHits { get; private set; }
        public int FissureEruptions { get; private set; }
        public readonly List<string> BlockLog = new List<string>();   // diagnostics: "tick tempo block"

        // live geometry for the HUD numbers / tests
        public readonly List<(Vector3 at, int number, bool imminent)> Numbers = new List<(Vector3, int, bool)>();
        public readonly List<Hazard> SeveranceStrikes = new List<Hazard>();
        public float LockedYaw { get; private set; }
        public Vector3 LockedPos { get; private set; }
        [System.NonSerialized] public Hazard LastWake, LastFissure, LastRing, LastInner;
        public int LastFissureResolveAge = -1, LastStrike1ResolveAge = -1;

        int farTicks, rushReadyAt, rotation;
        public string ForcedNext;   // dev/test hook only
        enum Atk { Severance, Wake, Crown }
        static readonly Atk[] Rotation = { Atk.Severance, Atk.Wake, Atk.Crown, Atk.Severance, Atk.Crown, Atk.Wake };

        void Awake() { MaxHp = 2600; BodyRadius = 1.4f; HitHeight = 2.0f; MarkHeight = 4.4f; }

        protected override void Build()
        {
            Rig = TempoTyrantRig.Create(transform);
            Audio = gameObject.AddComponent<TempoTyrantAudio>(); Audio.Bind(this);
            gameObject.AddComponent<TempoTyrantHud>().Bind(this);
            Clock.Reset();
            PhaseName = "Haste";
        }

        public static bool ArenaCentred(Vector3 p) => new Vector2(p.x, p.z).magnitude <= CentreGate + 0.05f;
        float Walk => Clock.State == Tempo.Haste ? HasteWalk : NormalWalk;
        int Beats(float b) => Clock.TicksFor(b);

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                // ---- complete block boundary: queued state changes happen only here ----
                if (!Frenzy && !FrenzyPending && Hp * 5 < MaxHp) FrenzyPending = true;   // strictly below 20%
                if (FrenzyPending)
                    foreach (var w in FrenzyTell()) yield return w;
                if (Clock.Pending)
                    foreach (var w in TempoTransition()) yield return w;

                // ---- pursuit between attacks (never damaging) ----
                foreach (var w in Pursue()) yield return w;

                // ---- one attack block ----
                Blocks++;
                if (Frenzy && Clock.State == Tempo.Haste && ChainDue)
                {
                    ChainDue = false; ChainsRun++;
                    Log("Broken Cadence chain");
                    foreach (var w in FrenzyChain()) yield return w;
                }
                else
                {
                    // test hook: ForcedNext ("Severance" / "Wake" / "Crown") replaces the next rotation entry once
                    var atk = ForcedNext != null && System.Enum.TryParse<Atk>(ForcedNext, out var forced) ? forced : Rotation[rotation++ % Rotation.Length];
                    ForcedNext = null;
                    Log(atk.ToString());
                    IEnumerable<int> body = atk == Atk.Severance ? Severance(-1, true) : atk == Atk.Wake ? Wake(false) : Crown();
                    foreach (var w in body) yield return w;
                }
                Current = "between blocks"; Rig.Pose(TyrantPose.Idle);
                yield return Beats(2);
            }
        }

        void Log(string block) => BlockLog.Add($"{W.Tick} {Clock.State} {block}");

        // Three accented ticks at a safe boundary; the new tempo commits on the third. The boss holds still and no
        // attack runs across the commit.
        IEnumerable<int> TempoTransition()
        {
            Current = "tempo shift"; Rig.Pose(TyrantPose.Idle);
            Announcement = Clock.Next == Tempo.Haste ? "HASTE  1 · 2 · 3" : "TEMPO SLOWS  1 · 2 · 3";
            Clock.BeginTransition();
            int guard = 0;
            while (Clock.Transitioning && guard++ < 6 * Sim.Hz) yield return 1;
            Announcement = "";
            PhaseName = Clock.State == Tempo.Haste ? (Frenzy ? "Broken Cadence · Haste" : "Haste") : (Frenzy ? "Broken Cadence · Normal" : "Normal");
            SetPhase(PhaseName);
            if (Frenzy && Clock.State == Tempo.Haste) ChainDue = true;   // each Haste passage opens with the chain
            Log("tempo -> " + Clock.State);
        }

        IEnumerable<int> FrenzyTell()
        {
            FrenzyPending = false; Frenzy = true; FrenzyTriggers++;
            Current = "Broken Cadence (tell)"; Announcement = "BROKEN CADENCE";
            Rig.Pose(TyrantPose.Frenzy); Rig.Frenzy = true;
            W.Fx.Crescendo(transform.position, true, 1, false);
            Log("frenzy");
            for (int i = 0; i < FrenzyTellTicks; i++) { Windup(i / (float)FrenzyTellTicks); yield return 1; }
            Release(1.2f); Announcement = "";
            if (Clock.State == Tempo.Haste) ChainDue = true;   // queued once; never double-enqueued by the next commit
            PhaseName = "Broken Cadence · " + Clock.State; SetPhase(PhaseName);
        }

        // ---------------- pursuit and closing rush ----------------
        // Deterministic: the farthest standing player (ties: lowest roster index); keep the current target while it
        // stays valid and beyond RushDist - hysteresis, so the boss does not flip between players every tick.
        public PlayerController SelectPursuitTarget()
        {
            if (PursuitTarget && !PursuitTarget.Down && W.Players.Contains(PursuitTarget) &&
                Sim.Flat(PursuitTarget.transform.position - transform.position).magnitude > RushDist - RushHysteresis) return PursuitTarget;
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                float d = Sim.Flat(p.transform.position - transform.position).magnitude;
                if (d > bd + 1e-3f) { bd = d; best = p; }
            }
            return best;
        }

        protected override void StepAlways()
        {
            Rig.Step();
            if (Clock.Step(W.Tick)) Rig.Tick(Clock.LastBeatAccented);
            Rig.Haste = Clock.State == Tempo.Haste;
            Rig.RodPhase = Clock.RodPhase;
            var t = SelectPursuitTarget();
            if (t != PursuitTarget) { PursuitTarget = t; farTicks = 0; }
            if (t && Sim.Flat(t.transform.position - transform.position).magnitude > RushDist) farTicks++; else farTicks = 0;
            if (Current == "approach" || Current == "reposition" || Current == "pursuit") Rig.Pose(TyrantPose.Walk);
        }

        IEnumerable<int> Pursue()
        {
            for (int i = 0; i < PursuitMaxTicks; i++)
            {
                var t = PursuitTarget; if (!t) yield break;
                var to = Sim.Flat(t.transform.position - transform.position);
                if (to.magnitude <= FightDist) yield break;
                if (farTicks >= RushSustainTicks && Age >= rushReadyAt)
                {
                    foreach (var w in ClosingRush(t)) yield return w;
                    yield break;
                }
                Current = "pursuit";
                TurnTowards(Sim.YawTo(transform.position, t.transform.position, transform.eulerAngles.y), 240);
                var p = transform.position + transform.forward * Walk * Sim.Dt;
                if (new Vector2(p.x, p.z).magnitude <= OwnBound) transform.position = p;
                yield return 1;
            }
        }

        public Vector3 RushEndpoint(PlayerController t)
        {
            var start = transform.position; var tp = Sim.Flat(t.transform.position);
            var dir = Sim.Flat(tp - start); float dist = dir.magnitude; if (dist < 1e-3f) return start;
            dir /= dist;
            return ClampInside(start, start + dir * Mathf.Max(0, dist - FightDist), OwnBound);
        }

        IEnumerable<int> ClosingRush(PlayerController t)
        {
            Current = "Closing Rush (announced)"; Announcement = "CLOSING RUSH"; Rig.Pose(TyrantPose.Rush);
            int tell = Beats(RushTellBeats);
            var endMarker = W.BossFx.Circle(BodyRadius); endMarker.Memory();
            for (int i = 0; i < tell; i++)
            {
                if (!t || t.Down || !W.Players.Contains(t)) { RushCancels++; endMarker.Dismiss(); Announcement = ""; Current = "rush cancelled"; rushReadyAt = Age + Sim.Hz; yield break; }
                TurnTowards(Sim.YawTo(transform.position, t.transform.position, transform.eulerAngles.y), 360);
                endMarker.Place(RushEndpoint(t), 0);
                yield return 1;
            }
            var start = transform.position; var end = RushEndpoint(t);
            endMarker.Place(end, 0);
            int travel = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / RushSpeed * Sim.Hz));
            Current = "Closing Rush"; Rushes++;
            for (int i = 1; i <= travel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)travel); yield return 1; }
            endMarker.Dismiss(); Announcement = ""; rushReadyAt = Age + RushCooldownTicks; farTicks = 0;
            Rig.Pose(TyrantPose.Idle);
            yield return Beats(1);
        }

        // ---------------- positioning gate ----------------
        IEnumerable<int> CentreUp()
        {
            if (ArenaCentred(transform.position)) yield break;
            var p = Sim.Flat(transform.position); var dest = p.normalized * (CentreGate * 0.4f);
            foreach (var w in WalkTo(dest, Walk * 1.6f, 240)) yield return w;
            if (!ArenaCentred(transform.position)) transform.position = Sim.Flat(transform.position).normalized * CentreGate;   // after a stalled walk
        }

        // ---------------- Threefold Severance ----------------
        // Boss frame: right = +x. Each strike is a lane rectangle covering a slab of the arena (telegraph clipped to the
        // floor). Builders are public so route checks and tests use exactly the same numbers.
        public static Hazard SeveranceStrike(int n, Vector3 boss, float yaw, float bodyRadius)
        {
            var fwd = Sim.Dir(yaw); var right = Vector3.Cross(Vector3.up, fwd);
            var h = new Hazard { Shape = HazardShape.Lane, Kind = HazardKind.Strike, Yaw = yaw, LockAt = 0, ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius };
            if (n == 1) { h.Source = "Threefold Severance 1 (left cleave)"; h.Width = SevReach; h.Length = 2 * SevReach; h.Origin = boss + right * (SevSide - SevReach / 2) - fwd * SevReach; h.Damage = SevDmg1; }
            else if (n == 2) { h.Source = "Threefold Severance 2 (right cleave)"; h.Width = SevReach; h.Length = 2 * SevReach; h.Origin = boss + right * (-SevSide + SevReach / 2) - fwd * SevReach; h.Damage = SevDmg2; }
            else { h.Source = "Threefold Severance 3 (overhead)"; h.Width = 2 * SevBand; h.Length = SevReach; h.Origin = boss - fwd * bodyRadius; h.Damage = SevDmg3; }
            h.Origin.y = 0;
            return h;
        }
        // a representative point inside a strike's area on the floor (for the number label)
        static Vector3 LabelPoint(int n, Vector3 boss, float yaw)
        {
            var fwd = Sim.Dir(yaw); var right = Vector3.Cross(Vector3.up, fwd);
            var p = n == 1 ? boss - right * 5f : n == 2 ? boss + right * 5f : boss + fwd * 5f;
            var f = new Vector2(p.x, p.z); float max = CombatWorld.PlayerBound - 1.5f;
            if (f.magnitude > max) { f = f.normalized * max; p.x = f.x; p.z = f.y; }
            return p;
        }

        // openingTicks < 0: the authored 6-beat opening at the current tempo
        IEnumerable<int> Severance(int openingTicks, bool gate)
        {
            if (gate) foreach (var w in CentreUp()) yield return w;
            Current = "Threefold Severance";
            // plant and lock: face the target once, then position and facing never change during the combo
            float yaw = YawToTarget();
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            LockedYaw = yaw; LockedPos = Sim.Flat(transform.position);
            int open = openingTicks > 0 ? openingTicks : Beats(SevOpenBeats), gap = Beats(SevGapBeats);
            int[] resolveAt = { open, open + gap, open + 2 * gap };
            // previews: the full 1-2-3 sequence from the start; only the imminent strike is a live (filled) warning
            var previews = new BellkeeperFx.Telegraph[3];
            SeveranceStrikes.Clear(); Numbers.Clear();
            for (int k = 0; k < 3; k++)
            {
                var g = SeveranceStrike(k + 1, LockedPos, yaw, BodyRadius);
                Numbers.Add((LabelPoint(k + 1, LockedPos, yaw), k + 1, k == 0));
                if (k == 0) continue;
                previews[k] = W.BossFx.Lane(g.Length, g.Width); previews[k].Memory(); previews[k].Clip(Vector3.zero, g.ClipRadius); previews[k].Place(g.Origin, g.Yaw);
            }
            Rig.Pose(TyrantPose.Windup);
            int age = 0;
            for (int k = 0; k < 3; k++)
            {
                var h = SeveranceStrike(k + 1, LockedPos, yaw, BodyRadius);
                int start = k == 0 ? 0 : resolveAt[k - 1];
                h.ResolveAt = resolveAt[k] - start; h.Knockback = 1.2f;
                int n = k + 1;
                h.OnResolve = hz =>
                {
                    W.BossFx.SweepResolve(LockedPos, yaw + (n == 1 ? -90 : n == 2 ? 90 : 0), 6.5f, n == 3 ? 20 : 80);
                    Release(1.2f); CameraShake.Shake(0.12f, 0.2f);
                    if (n == 1) LastStrike1ResolveAge = Age;
                };
                if (previews[k] != null) { previews[k].Dismiss(); previews[k] = null; }
                W.Hazards.Spawn(h); SeveranceStrikes.Add(h);
                for (int i = 0; i < Numbers.Count; i++) Numbers[i] = (Numbers[i].at, Numbers[i].number, i == k);
                Rig.Pose(k == 0 ? TyrantPose.SlashLeft : k == 1 ? TyrantPose.SlashRight : TyrantPose.Overhead, h.ResolveAt);
                for (; age < resolveAt[k]; age++) { Windup((age - start) / (float)h.ResolveAt); yield return 1; }
                Numbers.RemoveAll(x => x.number == n);
            }
            Numbers.Clear();
            Current = "Threefold Severance (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(SevRecoverBeats); glowTarget = 1;
        }

        // ---------------- Sundering Wake ----------------
        public PlayerController WakeTarget()
        {
            PlayerController best = null; float bd = -1;
            foreach (var p in W.Players)
            {
                if (p.Down) continue;
                float d = Sim.Flat(p.transform.position - transform.position).magnitude;
                if (d > bd + 1e-3f) { bd = d; best = p; }
            }
            return best;
        }

        public (Vector3 start, Vector3 end, float yaw) WakePath(Vector3 targetPos)
        {
            var start = Sim.Flat(transform.position);
            var dir = Sim.Flat(targetPos - start); float dist = dir.magnitude;
            if (dist < 1e-3f) { dir = transform.forward; dist = 0; }
            dir.Normalize();
            float travel = Mathf.Clamp(dist + WakeOvershoot, WakeMinTravel, WakeMaxTravel);
            var end = ClampInside(start, start + dir * travel, OwnBound);
            return (start, end, Sim.YawTo(start, end, transform.eulerAngles.y));
        }

        // inChain: the fissure eruption tick is reported so the following Severance can be scheduled after it
        IEnumerable<int> Wake(bool inChain)
        {
            foreach (var w in CentreUp()) yield return w;   // room to charge: the lane always has real travel
            Current = "Sundering Wake";
            var target = WakeTarget();
            var (start, end, yaw) = WakePath(target ? target.transform.position : transform.position + transform.forward * 6);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            float travelLen = Vector3.Distance(start, end);
            int warn = Beats(WakeWarnBeats);
            var h = new Hazard { Source = "Sundering Wake (charge)", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = start, Yaw = yaw,
                Length = travelLen + BodyRadius + WakeReach, Width = WakeWidth, LockAt = 0, ResolveAt = warn + WakeTravelTicks, Damage = WakeDmg, Knockback = 2f,
                HitOnResolve = false };
            LastWake = W.Hazards.Spawn(h);
            var endMarker = W.BossFx.Circle(BodyRadius); endMarker.Memory(); endMarker.Place(end, 0);   // the endpoint he stops on
            Rig.Pose(TyrantPose.ThrustWindup, warn);
            for (int i = 0; i < warn; i++) { Windup(i / (float)warn); yield return 1; }
            Rig.Pose(TyrantPose.Thrust); Release(); W.BossFx.ChargeTrail(transform, true);
            var fwd = Sim.Dir(yaw);
            for (int i = 1; i <= WakeTravelTicks; i++)
            {
                transform.position = Vector3.Lerp(start, end, i / (float)WakeTravelTicks);
                float front = Vector3.Distance(start, transform.position) + BodyRadius + WakeReach;
                foreach (var p in W.Players)
                {
                    if (p.Down || h.Hit.Contains(p) || !h.Contains(p.transform.position)) continue;
                    if (Vector3.Dot(Sim.Flat(p.transform.position - start), fwd) > front + Hazard.Margin) continue;   // not reached yet
                    h.Hit.Add(p); p.ReceiveHit(h); WakeTravelHits++;
                }
                yield return 1;
            }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position); endMarker.Dismiss();
            // the delayed wake: a separate hazard instance with its own warning; it erupts exactly once
            int fissureWarn = Beats(FissureWarnBeats);
            var f = new Hazard { Source = "Sundering Wake (eruption)", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = start, Yaw = yaw,
                Length = travelLen, Width = FissureWidth, LockAt = 0, ResolveAt = fissureWarn, Damage = FissureDmg, Knockback = 1f };
            f.OnResolve = hz => { FissureEruptions++; W.BossFx.StripStamp(hz.Origin, hz.Yaw, hz.Length, hz.Width); Rig.Fissure(false); };
            LastFissure = W.Hazards.Spawn(f);
            LastFissureResolveAge = Age + fissureWarn;
            Rig.Fissure(true, start, end);
            Rig.Pose(TyrantPose.Recover);
            if (inChain) yield break;   // the chain schedules the Severance around the pending eruption
            Current = "Sundering Wake (recovery)";
            int rec = Mathf.Max(Beats(WakeRecoverBeats), fissureWarn + 1);   // the block ends after its eruption: no overlap
            glowTarget = 0.5f; yield return rec; glowTarget = 1;
        }

        // ---------------- Hollow Crown ----------------
        IEnumerable<int> Crown()
        {
            foreach (var w in CentreUp()) yield return w;
            Current = "Hollow Crown"; Rig.Pose(TyrantPose.RingCast);
            var c = Sim.Flat(transform.position);
            int ringT = Beats(CrownRingBeats), innerT = Beats(CrownInnerBeats);
            var ring = new Hazard { Source = "Hollow Crown (outer ring)", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = c, Inner = CrownPocket,
                Radius = SevReach, LockAt = 0, ResolveAt = ringT, Damage = RingDmg, Knockback = 1f, ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius };
            ring.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, Mathf.Min(hz.Radius, CombatWorld.PlayerBound + 2)); Release(1); };
            LastRing = W.Hazards.Spawn(ring);
            var innerPreview = W.BossFx.Circle(CrownInner); innerPreview.Memory(); innerPreview.Place(c, 0);
            Numbers.Clear(); Numbers.Add((c + new Vector3(0, 0, CrownPocket + 3.5f), 1, true)); Numbers.Add((c + new Vector3(0, 0, -2.6f), 2, false));
            for (int i = 0; i < ringT; i++) { Windup(i / (float)ringT); yield return 1; }
            innerPreview.Dismiss(); Numbers.Clear(); Numbers.Add((c + new Vector3(0, 0, -2.6f), 2, true));
            Rig.Pose(TyrantPose.GroundStrike, innerT);
            var inner = new Hazard { Source = "Hollow Crown (inner circle)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = c, Radius = CrownInner,
                LockAt = 0, ResolveAt = innerT, Damage = InnerDmg, Knockback = 1.5f };
            inner.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(1.4f); CameraShake.Shake(0.15f, 0.25f); };
            LastInner = W.Hazards.Spawn(inner);
            for (int i = 0; i < innerT; i++) { Windup(i / (float)innerT); yield return 1; }
            Numbers.Clear();
            Current = "Hollow Crown (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(CrownRecoverBeats); glowTarget = 1;
        }

        // ---------------- Broken Cadence chain ----------------
        IEnumerable<int> FrenzyChain()
        {
            foreach (var w in Wake(true)) yield return w;
            int eruptAge = LastFissureResolveAge;
            // reposition after the charge has actually ended (the fissure is already warning on the floor)
            foreach (var w in CentreUp()) yield return w;
            // full opening, lengthened when needed so the eruption lands >= FissureLeadBeats before strike 1;
            // nothing is shortened. The fissure is spent before any route between slash safe regions is needed.
            int open = Mathf.Max(Beats(SevOpenBeats), eruptAge - Age + Beats(FissureLeadBeats));
            foreach (var w in Severance(open, false)) yield return w;
        }

        // ---------------- helpers ----------------
        public static Vector3 ClampInside(Vector3 from, Vector3 to, float r)
        {
            to.y = 0;
            if (new Vector2(to.x, to.z).magnitude <= r) return to;
            var d = to - from; float lo = 0, hi = 1;
            for (int i = 0; i < 24; i++) { float m = (lo + hi) / 2; var p = from + d * m; if (new Vector2(p.x, p.z).magnitude <= r) lo = m; else hi = m; }
            return from + d * lo;
        }

        protected override void Update()
        {
            base.Update();
            if (Rig) Rig.Glow = glowLevel;
        }

        protected override void OnDefeat()
        {
            Numbers.Clear(); Announcement = "";
            Rig.Fissure(false); Rig.Pose(TyrantPose.Defeat);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
