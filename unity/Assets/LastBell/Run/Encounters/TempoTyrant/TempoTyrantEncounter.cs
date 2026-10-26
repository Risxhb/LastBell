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
    //   Threefold Severance  1 left cleave (x < +2.5 m in the boss frame), 2 right cleave (x > -2.5), 3 overhead: his whole
    //                        front half (everything ahead of his centre line). Opening 9 beats (6 and 8 failed far-start route checks), 4 beats between impacts,
    //                        recovery 5/6 beats (Haste/Normal). Position and facing locked before the first preview; he never swivels.
    //   Sundering Wake       3 m lane to a distant player (+2.6 m thrust reach), 5/6-beat warning, travel 0.45 s (hit when the blade arrives),
    //                        then a separate 2.2 m eruption lane on the same path, 4/5-beat warning, resolves once.
    //   Hollow Crown         ring outside a 4 m pocket (7/8 beats), then a 5.5 m circle under him (4 beats more).
    //   Closing Rush         announced, non-damaging: after 1.5 s with the pursuit target beyond 9 m.
    //   Mirrored Wake        Haste only: a second eruption lane, the fissure reflected across the arena's north-south line
    //                        (left/right on the fixed camera). Dashed preview from the start of the charge warning; it
    //                        erupts on the same tick as the real fissure. Skipped when the two lanes would nearly coincide.
    //   Spiral Slices        the floor's 12 wedge slabs (between the brass hour spokes) are the attack. He conducts from the
    //                        centre: the whole floor is indicated (dashed), then the first slice "goes" (its outline
    //                        clears) and the other 11 pulse. The safe slice then spirals one slice clockwise (right) or
    //                        anticlockwise (left) per step while every other slice pulses, so players chase it round.
    //                        Then every other slice from the centre: one set of 6, then the other 6.
    //                        (Replaced Stop-Time on October 24, 2026, at Risxhb's request.)
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
        // Windows in beats as (Haste, Normal). Normal is the former Haste pace (165 BPM, the song as written). Haste is a
        // savage tier: every beat is 25% shorter (220 BPM) AND indicators, windups and combos get fewer beats from
        // placement to activation. Normal stays walkable; Haste needs sprint and pre-positioning (route checks prove both
        // remain survivable, without dashes or immunity).
        public static readonly (float H, float N) SevOpen = (9, 9), SevGap = (4, 4), SevRecover = (3, 5),
            WakeWarn = (4, 5), FissureWarn = (3, 4), WakeRecover = (2, 3),
            CrownRing = (6, 7), CrownInnerWarn = (3, 4), CrownRecover = (4, 6), RushTell = (2, 2);
        public static float Of((float H, float N) w, Tempo t) => t == Tempo.Haste ? w.H : w.N;
        public const float WakeWidth = 3f, FissureWidth = 2.2f,
            WakeOvershoot = 2f, WakeMaxTravel = 15f, WakeMinTravel = 4f, WakeReach = 2.6f;   // reach: greatsword thrust beyond the body
        public const int WakeTravelTicks = 27;
        public const float CrownPocket = 4f, CrownInner = 5.5f;
        public const float FightDist = 4.5f, RushDist = 9f, RushHysteresis = 1.5f, RushSpeed = 16f;
        public const int RushSustainTicks = 90, RushCooldownTicks = 8 * Sim.Hz, PursuitMaxTicks = 75;
        public const float HasteWalk = 4.2f, NormalWalk = 3.4f;
        public const float FrenzyAt = 0.2f;
        public const int FrenzyTellTicks = 90;
        public const int SevDmg1 = 26, SevDmg2 = 26, SevDmg3 = 30, WakeDmg = 24, FissureDmg = 16, RingDmg = 22, InnerDmg = 22;
        public const int FissureLeadBeats = 8;                  // chain: eruption at least this many beats before strike 1
        // Mirrored Wake (Haste): skipped when the reflected lane points within this angle of the real one (they'd overlap)
        public const float MirrorMinAngle = 20f;
        // Spiral Slices: 12 slices of 30 degrees on the floor's hour spokes (spoke i at i*30 deg from +z, clockwise from
        // above and on the fixed camera), slice i centred on i*30 + 15 deg. Windows (Haste, Normal) in beats.
        public const int SliceCount = 12, SpiralSteps = 10;
        public const float SliceHalf = 15f, SliceReach = 14f;
        public static readonly (float H, float N) SliceIndicate = (4, 5), SliceFirst = (4, 5), SliceStep = (2, 2),
            AltWarn = (4, 5), AltGap = (3, 4), SliceRecover = (2, 3);
        public const int SpiralDmg = 12, AltDmg = 18;

        // this boss's live-warning palette (HDR): saturated red-orange strikes, hot amber eruption, ivory stop markers
        public static readonly Color LiveFill = new Color(1.25f, 0.30f, 0.12f), LiveBorder = new Color(2.2f, 0.75f, 0.25f),
            EruptFill = new Color(1.5f, 0.62f, 0.12f), EruptBorder = new Color(2.6f, 1.3f, 0.3f),
            MarkerFill = new Color(0.57f, 0.53f, 0.43f), MarkerBorder = new Color(0.86f, 0.80f, 0.64f), RushMarkerBorder = new Color(1.3f, 1.2f, 0.95f);   // ivory at ~60%: a target, not a danger
        // flash: resolve-flash strength for this telegraph (floor-sized strikes stay low so the next warning stays readable)
        Hazard Live(Hazard h, bool eruption = false, float flash = 0.5f)
        {
            W.Hazards.Spawn(h);
            float boost = Frenzy ? 1.15f : 1f;   // frenzy: live warnings push harder through the busier screen
            h.Tele.Tint((eruption ? EruptFill : LiveFill) * boost, eruption ? EruptBorder : LiveBorder);
            h.Tele.FlashScale = flash; h.Tele.SpentFast = true;   // a spent strike never outshines the next live one
            return h;
        }
        static readonly Color RimIvory = new Color(1.6f, 1.5f, 1.25f), RimBrass = new Color(1.5f, 1.05f, 0.45f), RimAmber = new Color(2.0f, 1.1f, 0.3f);
        // solid ivory ring + centre pip: "he stops here" (presentation only, never a hazard)
        (BellkeeperFx.Telegraph ring, BellkeeperFx.Telegraph pip) StopMarker(Vector3 at, bool dot = true)
        {
            var ring = W.BossFx.Ring(BodyRadius - 0.08f, BodyRadius); ring.Tint(MarkerFill, dot ? RushMarkerBorder : MarkerBorder); ring.Lock(); ring.Place(at, 0);   // the rush endpoint stays brighter than the Wake landing
            var pip = W.BossFx.Circle(0.28f); pip.Tint(MarkerFill, dot ? RushMarkerBorder : MarkerBorder); pip.Lock(); pip.Place(dot ? at : new Vector3(9999f, 0, 9999f), 0);   // no dot: parked far off screen (a clear tint renders black)
            return (ring, pip);
        }
        public TempoTyrantFx Fx { get; private set; }

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
        [System.NonSerialized] public Hazard LastWake, LastFissure, LastMirror, LastRing, LastInner;
        public int LastFissureResolveAge = -1, LastStrike1ResolveAge = -1;
        public int MirrorPreviews { get; private set; }
        public int MirrorEruptions { get; private set; }
        public int MirrorSkips { get; private set; }

        // Spiral Slices state (HUD / tests)
        public bool SlicesActive { get; private set; }
        public int SafeSlice { get; private set; } = -1;     // the current safe slice during the spiral (-1 = none)
        public int AltSafeParity { get; private set; } = -1;   // every-other phase: the parity of the slices that stay safe
        public int SpiralDir { get; private set; }           // +1 clockwise (right on screen), -1 anticlockwise (left)
        public int SliceBlocks { get; private set; }
        public int SlicePulses { get; private set; }
        // one entry per pulse: resolve tick, safe slice (-1 for every-other), every-other parity (-1 for the spiral)
        public readonly List<(int tick, int safe, int parity)> SliceLog = new List<(int, int, int)>();
        int sliceUses;

        int farTicks, rushReadyAt, rotation;
        public string ForcedNext;   // dev/test hook only
        enum Atk { Severance, Wake, Crown, Slices }
        static readonly Atk[] Rotation = { Atk.Severance, Atk.Wake, Atk.Crown, Atk.Slices, Atk.Severance, Atk.Crown, Atk.Wake, Atk.Slices };

        void Awake() { MaxHp = 2600; BodyRadius = 1.4f; HitHeight = 2.0f; MarkHeight = 4.4f; }

        protected override void Build()
        {
            Rig = TempoTyrantRig.Create(transform);
            Audio = gameObject.AddComponent<TempoTyrantAudio>(); Audio.Bind(this);
            gameObject.AddComponent<TempoTyrantHud>().Bind(this);
            Fx = gameObject.AddComponent<TempoTyrantFx>();
            Clock.Reset();
            PhaseName = "Haste";
        }

        public static bool ArenaCentred(Vector3 p) => new Vector2(p.x, p.z).magnitude <= CentreGate + 0.05f;
        float Walk => Clock.State == Tempo.Haste ? HasteWalk : NormalWalk;
        int Beats(float b) => Clock.TicksFor(b);
        int Beats((float H, float N) w) => Clock.TicksFor(Of(w, Clock.State));

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
                    // test hook: ForcedNext ("Severance" / "Wake" / "Crown" / "Slices") replaces the next rotation entry once
                    var atk = ForcedNext != null && System.Enum.TryParse<Atk>(ForcedNext, out var forced) ? forced : Rotation[rotation++ % Rotation.Length];
                    ForcedNext = null;
                    Log(atk.ToString());
                    IEnumerable<int> body = atk == Atk.Severance ? Severance(-1, true) : atk == Atk.Wake ? Wake(false) : atk == Atk.Crown ? Crown() : Slices();
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
            Fx.Rim(transform.position, BodyRadius + 1.2f, RimAmber, 0.45f); Fx.Rim(transform.position, BodyRadius + 2.4f, RimAmber * 0.7f, 0.45f);
            Log("frenzy");
            for (int i = 0; i < FrenzyTellTicks; i++)
            {
                if (i > 0 && (i == FrenzyTellTicks / 3 || i == 2 * FrenzyTellTicks / 3)) { Fx.Rim(transform.position, BodyRadius + 1.2f, RimAmber, 0.45f); Fx.Rim(transform.position, BodyRadius + 2.4f, RimAmber * 0.7f, 0.45f); }   // tick, tick, tick
                Windup(0.45f * i / FrenzyTellTicks); yield return 1;
            }
            Release(0.5f); Announcement = "";
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
            int tell = Beats(RushTell);
            var endMarker = StopMarker(RushEndpoint(t));
            for (int i = 0; i < tell; i++)
            {
                if (!t || t.Down || !W.Players.Contains(t)) { RushCancels++; endMarker.ring.Dismiss(); endMarker.pip.Dismiss(); Announcement = ""; Current = "rush cancelled"; rushReadyAt = Age + Sim.Hz; yield break; }
                TurnTowards(Sim.YawTo(transform.position, t.transform.position, transform.eulerAngles.y), 360);
                var e = RushEndpoint(t); endMarker.ring.Place(e, 0); endMarker.pip.Place(e, 0);
                yield return 1;
            }
            var start = transform.position; var end = RushEndpoint(t);
            endMarker.ring.Place(end, 0); endMarker.pip.Place(end, 0);
            int travel = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / RushSpeed * Sim.Hz));
            Current = "Closing Rush"; Rushes++;
            for (int i = 1; i <= travel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)travel); yield return 1; }
            endMarker.ring.Dismiss(); endMarker.pip.Dismiss(); Announcement = ""; rushReadyAt = Age + RushCooldownTicks; farTicks = 0;
            Current = "Closing Rush (stopped)";
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
            // 3: the overhead takes the entire half of the arena he faces (everything ahead of his centre line); behind him is safe
            else { h.Source = "Threefold Severance 3 (overhead)"; h.Width = 2 * SevReach; h.Length = SevReach; h.Origin = boss; h.Damage = SevDmg3; }
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
            int open = openingTicks > 0 ? openingTicks : Beats(SevOpen), gap = Beats(SevGap);
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
                    if (n == 3) Fx.Streak(LockedPos, yaw, 1.0f, 9f); else Fx.Arc(LockedPos, yaw, n == 1 ? -1 : 1);
                    Release(1.2f); CameraShake.Shake(0.12f, 0.2f);
                    if (n == 1) LastStrike1ResolveAge = Age;
                };
                if (previews[k] != null) { previews[k].Dismiss(); previews[k] = null; }
                Live(h, false, 0.25f); SeveranceStrikes.Add(h);
                for (int i = 0; i < Numbers.Count; i++) Numbers[i] = (Numbers[i].at, Numbers[i].number, Numbers[i].number == k + 1);
                Rig.Pose(k == 0 ? TyrantPose.SlashLeft : k == 1 ? TyrantPose.SlashRight : TyrantPose.Overhead, h.ResolveAt);
                for (; age < resolveAt[k]; age++) { Windup((age - start) / (float)h.ResolveAt); yield return 1; }
                Numbers.RemoveAll(x => x.number == n);
            }
            Numbers.Clear();
            Current = "Threefold Severance (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(SevRecover); glowTarget = 1;
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
            int warn = Beats(WakeWarn);
            var h = new Hazard { Source = "Sundering Wake (charge)", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = start, Yaw = yaw,
                Length = travelLen + BodyRadius + WakeReach, Width = WakeWidth, LockAt = 0, ResolveAt = warn + WakeTravelTicks, Damage = WakeDmg, Knockback = 2f,
                HitOnResolve = false };
            LastWake = Live(h);
            var endMarker = StopMarker(end, false);   // where he stops (the lane runs on by his body and 2.6 m thrust reach)
            // Haste: the fissure's mirror image, previewed (dashed) from now so players can read it before it goes live
            LastMirror = null;
            BellkeeperFx.Telegraph mirrorPreview = null;
            var (mOrigin, mYaw) = MirrorLane(start, yaw);
            bool mirror = Clock.State == Tempo.Haste && MirrorUseful(yaw);
            if (Clock.State == Tempo.Haste && !mirror) MirrorSkips++;
            if (mirror)
            {
                mirrorPreview = W.BossFx.Lane(travelLen, FissureWidth); mirrorPreview.Memory(); mirrorPreview.Place(mOrigin, mYaw);
                MirrorPreviews++; Announcement = "MIRRORED WAKE";
            }
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
            W.BossFx.ChargeTrail(transform, false); Fx.Rim(transform.position, BodyRadius + 0.2f, RimBrass * 0.6f); CameraShake.Shake(0.1f, 0.15f);
            endMarker.ring.Dismiss(); endMarker.pip.Dismiss();
            // the delayed wake: a separate hazard instance with its own warning; it erupts exactly once
            int fissureWarn = Beats(FissureWarn);
            var f = new Hazard { Source = "Sundering Wake (eruption)", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = start, Yaw = yaw,
                Length = travelLen, Width = FissureWidth, LockAt = 0, ResolveAt = fissureWarn, Damage = FissureDmg, Knockback = 1f };
            f.OnResolve = hz => { FissureEruptions++; Fx.Streak(hz.Origin, hz.Yaw, 0, hz.Length, RimAmber, hz.Width * 0.5f); Rig.Fissure(false); };
            LastFissure = Live(f, true);
            if (mirror)
            {
                mirrorPreview.Dismiss();
                var m = new Hazard { Source = "Sundering Wake (mirrored eruption)", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = mOrigin, Yaw = mYaw,
                    Length = travelLen, Width = FissureWidth, LockAt = 0, ResolveAt = fissureWarn, Damage = FissureDmg, Knockback = 1f };
                m.OnResolve = hz => { MirrorEruptions++; Fx.Streak(hz.Origin, hz.Yaw, 0, hz.Length, RimAmber, hz.Width * 0.5f); };
                LastMirror = Live(m, true);
                Announcement = "";
            }
            LastFissureResolveAge = Age + fissureWarn;
            Rig.Fissure(true, start, end);
            Rig.Pose(TyrantPose.Recover);
            if (inChain) yield break;   // the chain schedules the Severance around the pending eruption
            Current = "Sundering Wake (recovery)";
            int rec = Mathf.Max(Beats(WakeRecover), fissureWarn + 1);   // the block ends after its eruption: no overlap
            glowTarget = 0.5f; yield return rec; glowTarget = 1;
        }

        // ---------------- Hollow Crown ----------------
        IEnumerable<int> Crown()
        {
            foreach (var w in CentreUp()) yield return w;
            Current = "Hollow Crown"; Rig.Pose(TyrantPose.RingCast);
            var c = Sim.Flat(transform.position);
            int ringT = Beats(CrownRing), innerT = Beats(CrownInnerWarn);
            var ring = new Hazard { Source = "Hollow Crown (outer ring)", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = c, Inner = CrownPocket,
                Radius = SevReach, LockAt = 0, ResolveAt = ringT, Damage = RingDmg, Knockback = 1f, ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius };
            ring.OnResolve = hz => { Fx.Rim(hz.Origin, hz.Inner, RimAmber * 0.6f); Release(1); };   // amber, not white: the inner warning is live here
            LastRing = Live(ring, false, 0.25f);
            ring.Tele.Tint(LiveFill * 0.7f * (Frenzy ? 1.15f : 1f), LiveBorder);   // wide annulus: calmer fill, full-strength border carries the read
            var innerPreview = W.BossFx.Circle(CrownInner); innerPreview.Memory(); innerPreview.Place(c, 0);
            // "1" east or west of him (whichever side is farther from the nearest player), never north/south where it
            // would stack on "(2)"; "(2)" sits just south of his body
            var near = W.NearestStanding(c); float sideX = near && near.transform.position.x > c.x ? -1 : 1;
            var label1 = c + Vector3.right * sideX * Mathf.Min(CrownPocket + 3.5f, CombatWorld.PlayerBound - 1.5f - Mathf.Abs(c.x));
            var label2 = c + new Vector3(0, 0, 1.2f);
            Numbers.Clear(); Numbers.Add((label1, 1, true)); Numbers.Add((label2, 2, false));
            for (int i = 0; i < ringT; i++) { Windup(i / (float)ringT); yield return 1; }
            innerPreview.Dismiss(); Numbers.Clear(); Numbers.Add((label2, 2, true));
            Rig.Pose(TyrantPose.GroundStrike, innerT);
            var inner = new Hazard { Source = "Hollow Crown (inner circle)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = c, Radius = CrownInner,
                LockAt = 0, ResolveAt = innerT, Damage = InnerDmg, Knockback = 1.5f };
            inner.OnResolve = hz => { Fx.Rim(hz.Origin, hz.Radius, RimIvory); Release(1.0f); CameraShake.Shake(0.15f, 0.25f); };
            LastInner = Live(inner, false, 0.5f);
            for (int i = 0; i < innerT; i++) { Windup(i / (float)innerT); yield return 1; }
            Numbers.Clear();
            Current = "Hollow Crown (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(CrownRecover); glowTarget = 1;
        }

        // ---------------- Mirrored Wake ----------------
        // Reflection across the arena's north-south line (x -> -x). Yaw is measured from +z, so it reflects to -yaw.
        public static (Vector3 origin, float yaw) MirrorLane(Vector3 origin, float yaw) => (new Vector3(-origin.x, 0, origin.z), -yaw);
        public static bool MirrorUseful(float yaw) => Mathf.Abs(Mathf.DeltaAngle(yaw, -yaw)) >= MirrorMinAngle;

        // ---------------- Spiral Slices ----------------
        public static float SliceYaw(int i) => Mathf.Repeat(i, SliceCount) * 360f / SliceCount + SliceHalf;
        public static int SliceOf(Vector3 p) => Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg, 360f) / (360f / SliceCount)) % SliceCount;
        public static int Wrap(int i) => ((i % SliceCount) + SliceCount) % SliceCount;
        public static Hazard SliceHazard(int i, string source, int dmg) => new Hazard
        {
            Source = source, Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Vector3.zero, Yaw = SliceYaw(i),
            Radius = SliceReach, HalfAngle = SliceHalf, LockAt = 0, Damage = dmg, Knockback = 0.6f,
            ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius, HitOnResolve = false,
        };
        BellkeeperFx.Telegraph SliceOutline(int i)
        {
            var t = W.BossFx.Cone(SliceReach, SliceHalf); t.Memory();
            t.Clip(Vector3.zero, CombatWorld.PlayerBound + PlayerController.Radius); t.Place(Vector3.zero, SliceYaw(i));
            return t;
        }

        // One pulse over a set of slices. Neighbouring slices overlap by the player margin on their shared spoke, so the
        // group hits each player at most once: the first slice listed runs the hit test for all of them.
        List<Hazard> SliceVolley(List<int> slices, string source, int dmg, int resolveAt, float flash)
        {
            var group = new List<Hazard>();
            foreach (var i in slices) { var h = SliceHazard(i, source, dmg); h.ResolveAt = resolveAt; group.Add(h); }
            if (group.Count == 0) return group;
            group[0].OnResolve = lead =>
            {
                SlicePulses++;
                foreach (var p in W.Players)
                {
                    if (p.Down) continue;
                    foreach (var h in group)
                        if (h.Contains(p.transform.position)) { h.Hit.Add(p); p.ReceiveHit(h); break; }
                }
                Release(0.4f);
            };
            foreach (var h in group) Live(h, false, flash);
            return group;
        }

        IEnumerable<int> Slices()
        {
            foreach (var w in CentreUp()) yield return w;   // he conducts the floor from the centre
            Current = "Spiral Slices"; Rig.Pose(TyrantPose.RingCast);
            SliceBlocks++; SlicesActive = true;
            SpiralDir = sliceUses++ % 2 == 0 ? 1 : -1;
            // the first safe slice is the one under the nearest player, so the opening is always reachable
            var near = W.NearestStanding(Vector3.zero);
            int safe = near ? SliceOf(near.transform.position) : 0;
            Announcement = SpiralDir > 0 ? "SPIRAL  RIGHT  >>>" : "<<<  SPIRAL  LEFT";

            // 1) indicate the whole floor: every slice dashed
            var outline = new BellkeeperFx.Telegraph[SliceCount];
            for (int i = 0; i < SliceCount; i++) outline[i] = SliceOutline(i);
            int indicate = Beats(SliceIndicate);
            for (int i = 0; i < indicate; i++) { Windup(0.3f * i / indicate); yield return 1; }
            // 2) the first slice goes (its outline clears) and the other 11 go live; then the safe slice spirals on
            for (int i = 0; i < SliceCount; i++) outline[i].Dismiss();
            SafeSlice = safe;
            int step = Beats(SliceStep), first = Beats(SliceFirst);
            for (int k = 0; k < SpiralSteps; k++)
            {
                int warn = k == 0 ? first : step;
                var others = new List<int>();
                for (int i = 1; i < SliceCount; i++) others.Add(Wrap(SafeSlice + i));
                foreach (var h in SliceVolley(others, "Spiral Slices", SpiralDmg, warn, 0.2f)) { h.Knockback = 0; h.Stun = 0; }   // a hit never stalls the chase
                SliceLog.Add((W.Tick + warn, SafeSlice, -1));
                for (int i = 0; i < warn; i++) { Windup(0.4f + 0.4f * i / warn); yield return 1; }
                if (k == 0) Announcement = "";
                if (k < SpiralSteps - 1) SafeSlice = Wrap(SafeSlice + SpiralDir);   // the safe slice spirals on
            }
            int last = SafeSlice; SafeSlice = -1;

            // 3) every other slice from the centre: first the set holding the slice the spiral ended on, then the other
            int parity = last % 2;
            Current = "Spiral Slices (every other)"; Rig.Pose(TyrantPose.GroundStrike);
            int altWarn = Beats(AltWarn), altGap = Beats(AltGap);
            var setA = new List<int>(); var setB = new List<int>();
            for (int i = 0; i < SliceCount; i++) (i % 2 == parity ? setA : setB).Add(i);
            var previewB = new List<BellkeeperFx.Telegraph>();
            foreach (var i in setB) previewB.Add(SliceOutline(i));
            Announcement = "EVERY  OTHER  SLICE"; AltSafeParity = 1 - parity;
            SliceVolley(setA, "Every Other Slice (1)", AltDmg, altWarn, 0.4f);
            SliceLog.Add((W.Tick + altWarn, -1, parity));
            for (int i = 0; i < altWarn; i++) { Windup(i / (float)altWarn); yield return 1; }
            Announcement = "";
            foreach (var t in previewB) t.Dismiss();
            AltSafeParity = parity;
            Rig.Pose(TyrantPose.GroundStrike, altGap);
            SliceVolley(setB, "Every Other Slice (2)", AltDmg, altGap, 0.4f);
            SliceLog.Add((W.Tick + altGap, -1, 1 - parity));
            for (int i = 0; i < altGap; i++) { Windup(i / (float)altGap); yield return 1; }
            Release(1f); CameraShake.Shake(0.12f, 0.2f);
            SlicesActive = false; AltSafeParity = -1;
            Current = "Spiral Slices (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(SliceRecover); glowTarget = 1;
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
            int open = Mathf.Max(Beats(SevOpen), eruptAge - Age + Beats(FissureLeadBeats));
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
            Numbers.Clear(); Announcement = ""; PhaseName = "Defeated";
            SlicesActive = false; SafeSlice = -1; AltSafeParity = -1;
            Rig.Fissure(false); Rig.Pose(TyrantPose.Defeat);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
