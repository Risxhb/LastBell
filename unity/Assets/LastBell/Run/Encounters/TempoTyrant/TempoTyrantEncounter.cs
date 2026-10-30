using System.Collections.Generic;
using System.Linq;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // The Tempo Tyrant: the post-game superboss (docs/bosses/tempo-tyrant.md, ultimate plan
    // docs/bosses/tempo-tyrant-ultimate-plan.md). Greatsword knight, labelled stand-in body until the final model/rig
    // exists. All numbers are tunable first-pass proposals, not approved balance. Geometry is in metres before the
    // 0.3 m player margin; damage against 100 HP. Overlaps beyond two patterns are allowed by the Tempo Tyrant
    // exception in docs/design/content-rules.md (Risxhb, 2026-10-28) as long as a route exists (TempoTyrantRoutes).
    //
    // Tempo: one TempoClock (starts in Haste). Attack timings are authored in beats and converted with the tempo that
    // is current when the block starts; a block never changes tempo mid-way. A duration expiry only requests a change;
    // at the next complete block boundary the boss stands still for three accented metronome ticks and the new tempo
    // commits on the third. Damage is identical in both tempos.
    //
    //   Threefold Severance  1 left cleave (x < +2.5 m in the boss frame), 2 right cleave (x > -2.5), 3 overhead: his whole
    //                        front half (everything ahead of his centre line). Opening 9 beats (6 and 8 failed far-start route checks), 4 beats between impacts,
    //                        recovery 3/5 beats (Haste/Normal). Position and facing locked before the first preview; he never swivels.
    //   Sundering Wake       a charge down a 3 m lane at the farthest player (+2.6 m thrust reach), 4/5-beat warning, travel
    //                        0.45 s (hit when the blade arrives). On arrival the Backswing: a 6 m half-disc behind his landing
    //                        point (facing back up the lane), live 1/2 beats, previewed dashed from the start of the warning.
    //                        Each charge leaves its own 2.2 m eruption lane (3/4-beat warning, resolves once). Haste: he
    //                        re-targets the farthest player and charges again straight after the backswing (2 charges,
    //                        the second with a 3-beat warning); Normal: 1 charge. (Oct 28 rework, Risxhb.)
    //   Hollow Crown         ring outside a 4 m pocket (6/7 beats), then a 5.5 m circle under him (3/4 beats more).
    //   Closing Rush         announced, non-damaging: after 0.5 s with the pursuit target beyond 7.5 m; 1-beat tell,
    //                        24 m/s, 3 s cooldown. Also between Haste charges and during the Spiral Slices walk-in.
    //   Mirrored Wake        Haste only: every eruption lane gets a twin reflected across the arena's north-south line
    //                        (left/right on the fixed camera). Dashed preview from the start of the charge warning; it
    //                        erupts on the same tick as the real fissure. Skipped when the two lanes would nearly coincide.
    //   Spiral Slices        the floor's 12 wedge slabs (between the brass hour spokes) are the attack. He conducts from the
    //                        centre: the whole floor is indicated (dashed), then the first slice "goes" (its outline
    //                        clears) and the other 11 pulse. The safe slice then spirals one slice clockwise (right) or
    //                        anticlockwise (left) per step while every other slice pulses, so players chase it round.
    //                        Then every other slice from the centre: one set of 6, then the other 6.
    //   Broken Cadence       below 20% HP (once, queued to a block boundary), in both tempos: every block is a chain of
    //                        overlapping attacks. Each next attack starts while the previous one's last hit is still on the
    //                        floor (Cadence.Pairs), never two slice attacks at once, warnings about 35% shorter but never
    //                        below the route-check floor of the pairing (Cadence windows).
    //   Falling Hours        below 10% HP: orbs rain onto the floor, denser the longer it lasts; each spews 3 orbs that
    //                        bounce off the wall 3 times (TempoTyrantEncounter.Orbs.cs). A deliberate soft enrage.
    public sealed partial class TempoTyrantEncounter : BossEncounter
    {
        public override string Id => "boss.tempo-tyrant";
        public override string DisplayName => "The Tempo Tyrant";
        public override string Lesson => "Follow the numbered 1-2-3 into cleared ground; the slow tempo is your opening";
        public override bool Placeholder => Rig == null || !Rig.HasModel;   // the Meshy model, unless Resources/Characters/TempoTyrant is missing
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
            WakeWarn = (4, 5), WakeWarn2 = (3, 4), Backswing = (1, 2), FissureWarn = (3, 4), WakeRecover = (2, 3),
            CrownRing = (6, 7), CrownInnerWarn = (3, 4), CrownRecover = (4, 6), RushTell = (1, 1);
        public static float Of((float H, float N) w, Tempo t) => t == Tempo.Haste ? w.H : w.N;
        public const float WakeWidth = 3f, FissureWidth = 2.2f,
            WakeOvershoot = 2f, WakeMaxTravel = 15f, WakeMinTravel = 4f, WakeReach = 2.6f;   // reach: greatsword thrust beyond the body
        public const int WakeTravelTicks = 27;
        public const int HasteCharges = 2, NormalCharges = 1;   // charges per Sundering Wake
        public const float BackswingRadius = 6f, BackswingHalf = 90f;   // half-disc behind the landing point
        public const float CrownPocket = 4f, CrownInner = 5.5f;
        public const float FightDist = 4.5f, RushDist = 7.5f, RushHysteresis = 1.5f, RushSpeed = 24f;
        public const int RushSustainTicks = 30, RushCooldownTicks = 3 * Sim.Hz, PursuitMaxTicks = 75;
        public const float HasteWalk = 4.2f, NormalWalk = 3.4f;
        public const float FrenzyAt = 0.2f;
        public const int FrenzyTellTicks = 90;
        public const int SevDmg1 = 26, SevDmg2 = 26, SevDmg3 = 30, WakeDmg = 24, BackswingDmg = 26, FissureDmg = 16, RingDmg = 22, InnerDmg = 22;
        public const int FissureLeadBeats = 8;                  // ordinary blocks: an eruption is never this close before strike 1
        // Mirrored Wake (Haste): skipped when the reflected lane points within this angle of the real one (they'd overlap)
        public const float MirrorMinAngle = 20f;
        // Spiral Slices: 12 slices of 30 degrees on the floor's hour spokes (spoke i at i*30 deg from +z, clockwise from
        // above and on the fixed camera), slice i centred on i*30 + 15 deg. Windows (Haste, Normal) in beats.
        public const int SliceCount = 12, SpiralSteps = 10;
        public const float SliceHalf = 15f, SliceReach = 14f;
        public static readonly (float H, float N) SliceIndicate = (4, 5), SliceFirst = (4, 5), SliceStep = (2, 2),
            AltWarn = (4, 5), AltGap = (3, 4), SliceRecover = (2, 3);
        public const int SpiralDmg = 12, AltDmg = 18;

        public enum Atk { Severance, Wake, Crown, Slices }

        // Broken Cadence (below 20% HP, both tempos). Every warning starts at about 0.65x its ordinary value and is raised
        // back to the route-check floor (tools/tempo-tyrant/routes_check.py floors; TempoTyrantRoutes in the autotest).
        // Join = the overlapping attack's first warning for that pairing (Wake: charge 1, Severance: opening, Crown: ring,
        // Slices: indicate). A pairing with no route is not listed.
        public static class Cadence
        {
            // floors (Oct 28 search): a lone Severance keeps its 9-beat opening and 4-beat gap (6 and 3 fail far starts);
            // the spiral's opening (indicate + first) stays near its ordinary length because a co-op safe slice can be
            // across the floor; spiral steps 1.5 beats (1 passes only hugging the boss, so 1.5 keeps the 5 m chase).
            public static readonly (float H, float N) SevOpen = (9, 9), SevGap = (4, 4),
                WakeWarn = (3, 3), WakeWarn2 = (2, 3), Backswing = (1, 1.5f), FissureWarn = (2, 3), FissureLead = (5, 5),
                CrownRing = (5, 6), CrownInner = (3, 3),
                SliceIndicate = (5, 5), SliceFirst = (3, 4), SliceStep = (1.5f, 1.5f), AltWarn = (3, 3), AltGap = (2, 3);
            public static readonly (Atk from, Atk to, (float H, float N) join)[] Pairs =
            {
                (Atk.Crown, Atk.Wake, (3, 3)), (Atk.Wake, Atk.Severance, (6, 6)), (Atk.Crown, Atk.Slices, (4, 3)),
                (Atk.Slices, Atk.Crown, (4, 5)), (Atk.Severance, Atk.Slices, (3, 3)), (Atk.Slices, Atk.Severance, (6, 6)),
            };
            public const int ChainLength = 3;   // attacks per Broken Cadence block
            public static (float H, float N) Join(Atk from, Atk to) => Pairs.First(p => p.from == from && p.to == to).join;
            public static bool Allowed(Atk from, Atk to) => Pairs.Any(p => p.from == from && p.to == to);
        }

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
        public int ChainsRun { get; private set; }   // Broken Cadence blocks run
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
        [System.NonSerialized] public Hazard LastWake, LastCharge, LastBackswing, LastFissure, LastMirror, LastRing, LastInner;
        public int LastFissureResolveAge = -1, LastStrike1ResolveAge = -1;
        public int MirrorPreviews { get; private set; }
        public int MirrorEruptions { get; private set; }
        public int MirrorSkips { get; private set; }
        public int BackswingPreviews { get; private set; }
        public int BackswingResolves { get; private set; }
        // every charge: tick its warning started, charge index in its Wake (0 = first), path, and the player it targeted
        public readonly List<(int tick, int index, Vector3 start, Vector3 end, float yaw, PlayerController target)> ChargeLog =
            new List<(int, int, Vector3, Vector3, float, PlayerController)>();
        // every closing rush: tell start tick, far-ticks when it fired, tell and travel ticks, distance, where it fired
        public sealed class RushRecord { public int TellTick, FarTicks, TellTicks, TravelTicks = -1, StopTick = -1; public float Distance; public string Where; }
        public readonly List<RushRecord> RushLog = new List<RushRecord>();
        // Broken Cadence: each overlap (tick, first, second, hazards of the first still pending when the second started)
        public readonly List<(int tick, Atk from, Atk to, int pending)> CadenceLog = new List<(int, Atk, Atk, int)>();
        public int MaxSliceAttacksAtOnce { get; private set; }   // distinct Spiral Slices attacks with hazards live together
        public int SevLockViolations { get; private set; }       // ticks he stood off his locked spot while a strike was pending
        public readonly List<int> FissureLeads = new List<int>();   // Wake -> Severance overlaps: eruption to strike 1 (ticks)

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
        readonly List<(Hazard h, int attack)> sliceHazards = new List<(Hazard, int)>();

        int farTicks, rushReadyAt, rotation, cadencePick, sevLockUntil = -1, fissureSerial;
        public string ForcedNext;   // dev/test hook only
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
        // ordinary window, or its Broken Cadence value once the frenzy is on
        int Bt((float H, float N) ordinary, (float H, float N) cadence) => Beats(Frenzy ? cadence : ordinary);

        // ---------------- script ----------------
        protected override IEnumerable<int> Script()
        {
            while (true)
            {
                // ---- complete block boundary: queued state changes happen only here ----
                if (!Frenzy && !FrenzyPending && Hp * 5 < MaxHp) FrenzyPending = true;   // strictly below 20%
                if (FrenzyPending)
                    foreach (var w in FrenzyTell()) yield return w;
                if (!OrbsOn && Hp * 10 < MaxHp)   // strictly below 10%: Falling Hours (TempoTyrantEncounter.Orbs.cs)
                    foreach (var w in OrbTell()) yield return w;
                if (Clock.Pending)
                    foreach (var w in TempoTransition()) yield return w;

                // ---- pursuit between attacks (never damaging) ----
                foreach (var w in Pursue()) yield return w;

                // ---- one attack block ----
                Blocks++;
                if (Frenzy)
                {
                    ChainsRun++;
                    foreach (var w in CadenceBlock()) yield return w;
                }
                else
                {
                    var atk = NextAttack();
                    Log(atk.ToString());
                    IEnumerable<int> body = atk == Atk.Severance ? Severance(-1, true, false) : atk == Atk.Wake ? Wake(false) : atk == Atk.Crown ? Crown(false) : Slices(false);
                    foreach (var w in body) yield return w;
                }
                Current = "between blocks"; Rig.Pose(TyrantPose.Idle);
                yield return Beats(2);
            }
        }

        // test hook: ForcedNext ("Severance" / "Wake" / "Crown" / "Slices") replaces the next rotation entry once
        Atk NextAttack()
        {
            var atk = ForcedNext != null && System.Enum.TryParse<Atk>(ForcedNext, out var forced) ? forced : Rotation[rotation++ % Rotation.Length];
            ForcedNext = null;
            return atk;
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
            StepOrbs();
            // he never leaves his planted spot while a Severance strike is pending (also across a Broken Cadence overlap)
            if (Age < sevLockUntil && Sim.Flat(transform.position - LockedPos).magnitude > 0.01f) SevLockViolations++;
            // never two slice attacks at once: count distinct Spiral Slices attacks with hazards live together
            if (sliceHazards.Count > 0)
            {
                sliceHazards.RemoveAll(x => x.h.Cancelled || x.h.Age > x.h.ResolveAt || !W.Hazards.Live.Contains(x.h));
                int n = sliceHazards.Select(x => x.attack).Distinct().Count();
                if (n > MaxSliceAttacksAtOnce) MaxSliceAttacksAtOnce = n;
            }
        }

        bool RushReady(PlayerController t) => t && !t.Down && farTicks >= RushSustainTicks && Age >= rushReadyAt && Age >= sevLockUntil;

        IEnumerable<int> Pursue()
        {
            for (int i = 0; i < PursuitMaxTicks; i++)
            {
                var t = PursuitTarget; if (!t) yield break;
                var to = Sim.Flat(t.transform.position - transform.position);
                if (to.magnitude <= FightDist) yield break;
                if (RushReady(t))
                {
                    foreach (var w in ClosingRush(t, "pursuit")) yield return w;
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

        IEnumerable<int> ClosingRush(PlayerController t, string where)
        {
            Current = "Closing Rush (announced)"; Announcement = "CLOSING RUSH"; Rig.Pose(TyrantPose.Rush);
            int tell = Beats(RushTell);
            var rec = new RushRecord { TellTick = W.Tick, FarTicks = farTicks, TellTicks = tell, Where = where }; RushLog.Add(rec);
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
            rec.TravelTicks = travel; rec.Distance = Vector3.Distance(start, end);
            Current = "Closing Rush"; Rushes++;
            for (int i = 1; i <= travel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)travel); yield return 1; }
            endMarker.ring.Dismiss(); endMarker.pip.Dismiss(); Announcement = ""; rushReadyAt = Age + RushCooldownTicks; farTicks = 0;
            rec.StopTick = W.Tick;
            Current = "Closing Rush (stopped)";
            Rig.Pose(TyrantPose.Idle);
            yield return Beats(1);
        }

        // ---------------- positioning gate ----------------
        // allowRush: he may break off into a closing rush while walking in (ordinary Spiral Slices)
        IEnumerable<int> CentreUp(bool allowRush = false)
        {
            if (ArenaCentred(transform.position)) yield break;
            while (Age < sevLockUntil) yield return 1;   // never off his planted spot while a Severance strike is pending
            var dest = Sim.Flat(transform.position).normalized * (CentreGate * 0.4f);
            for (int i = 0; i < 180 && Sim.Flat(dest - transform.position).magnitude > 0.3f; i++)
            {
                if (allowRush && RushReady(PursuitTarget))
                {
                    foreach (var w in ClosingRush(PursuitTarget, "Slices walk-in")) yield return w;
                    if (ArenaCentred(transform.position)) yield break;
                    dest = Sim.Flat(transform.position).normalized * (CentreGate * 0.4f);
                }
                Current = "reposition";
                TurnTowards(Sim.YawTo(transform.position, dest, transform.eulerAngles.y), 200);
                transform.position = Vector3.MoveTowards(transform.position, new Vector3(dest.x, 0, dest.z), Walk * 1.6f * Sim.Dt);
                yield return 1;
            }
            if (!ArenaCentred(transform.position)) transform.position = Sim.Flat(transform.position).normalized * CentreGate;   // after a stalled walk
        }

        // ---------------- Broken Cadence: overlapping chains ----------------
        // The first attack comes from the ordinary rotation; each next one is picked (in turn) from the pairings allowed
        // after the previous attack and starts as soon as the previous one has its last hit on the floor (handoff), so
        // their hits overlap. A Slices attack never starts while another one's slices are live.
        IEnumerable<int> CadenceBlock()
        {
            var chain = new List<Atk> { NextAttack() };
            for (int i = 1; i < Cadence.ChainLength; i++)
            {
                var opts = Cadence.Pairs.Where(p => p.from == chain[i - 1]).ToList();
                if (opts.Count == 0) break;
                chain.Add(opts[cadencePick++ % opts.Count].to);
            }
            Log("Broken Cadence: " + string.Join(" > ", chain));
            for (int i = 0; i < chain.Count; i++)
            {
                bool handoff = i < chain.Count - 1;
                var atk = chain[i]; Atk? prev = i > 0 ? chain[i - 1] : (Atk?)null;
                float join = -1;
                if (prev.HasValue)
                {
                    join = Of(Cadence.Join(prev.Value, atk), Clock.State);
                    CadenceLog.Add((W.Tick, prev.Value, atk, PendingOf(prev.Value)));
                }
                IEnumerable<int> body;
                switch (atk)
                {
                    case Atk.Severance: body = Severance(join, true, handoff, prev == Atk.Wake ? LastFissureResolveAge : -1); break;
                    case Atk.Wake: body = Wake(handoff, join); break;
                    case Atk.Crown: body = Crown(handoff, join); break;
                    default: body = Slices(handoff, join, prev == Atk.Severance ? BehindSlice(LockedPos, LockedYaw) : -1); break;
                }
                foreach (var w in body) yield return w;
            }
        }

        static readonly string[][] SourcesOf = { new[] { "Threefold Severance" }, new[] { "Sundering Wake" }, new[] { "Hollow Crown" }, new[] { "Spiral Slices", "Every Other Slice" } };
        public int PendingOf(Atk a) => W.Hazards.Live.Count(h => !h.Cancelled && SourcesOf[(int)a].Any(s => h.Source.StartsWith(s)));
        // the slice directly behind him (5 m back from his planted spot): safe from Severance strike 3
        public static int BehindSlice(Vector3 boss, float yaw) => SliceOf(boss - Sim.Dir(yaw) * 5f);

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

        // joinBeats > 0: the opening when it overlaps (Broken Cadence pairing); otherwise the ordinary / cadence opening.
        // eruptAge >= 0: a pending fissure erupts then; the opening is lengthened so it lands >= the fissure lead first.
        // handoff: return once strike 3 is live (he stays planted until it lands; CentreUp and rushes wait for it).
        IEnumerable<int> Severance(float joinBeats, bool gate, bool handoff, int eruptAge = -1)
        {
            if (gate) foreach (var w in CentreUp()) yield return w;
            Current = "Threefold Severance";
            // plant and lock: face the target once, then position and facing never change during the combo
            float yaw = YawToTarget();
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            LockedYaw = yaw; LockedPos = Sim.Flat(transform.position);
            int open = joinBeats > 0 ? Beats(joinBeats) : Bt(SevOpen, Cadence.SevOpen), gap = Bt(SevGap, Cadence.SevGap);
            if (eruptAge >= 0)
            {
                open = Mathf.Max(open, eruptAge - Age + Bt((FissureLeadBeats, FissureLeadBeats), Cadence.FissureLead));
                FissureLeads.Add(Age + open - eruptAge);   // ticks from the pending eruption to strike 1
            }
            int[] resolveAt = { open, open + gap, open + 2 * gap };
            sevLockUntil = Age + resolveAt[2] + 1;
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
                    if (n == 3 && handoff) Numbers.RemoveAll(x => x.number == 3);
                };
                if (previews[k] != null) { previews[k].Dismiss(); previews[k] = null; }
                Live(h, false, 0.25f); SeveranceStrikes.Add(h);
                for (int i = 0; i < Numbers.Count; i++) Numbers[i] = (Numbers[i].at, Numbers[i].number, Numbers[i].number == k + 1);
                Rig.Pose(k == 0 ? TyrantPose.SlashLeft : k == 1 ? TyrantPose.SlashRight : TyrantPose.Overhead, h.ResolveAt);
                if (handoff && k == 2) yield break;   // strike 3 is on the floor; the next attack overlaps it
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

        public static float BackYaw(float chargeYaw) => Mathf.Repeat(chargeYaw + 180f, 360f);
        // the Backswing: a half-disc behind the landing point, facing back up the charge lane
        public static Hazard BackswingHazard(Vector3 landing, float chargeYaw) => new Hazard
        {
            Source = "Sundering Wake (backswing)", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = Sim.Flat(landing),
            Yaw = BackYaw(chargeYaw), Radius = BackswingRadius, HalfAngle = BackswingHalf, LockAt = 0, Damage = BackswingDmg, Knockback = 1.5f,
            ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius,
        };

        // handoff: return once the last backswing has landed (its fissures still warning) for a Broken Cadence pairing.
        // joinBeats > 0: the first charge's warning when it overlaps.
        IEnumerable<int> Wake(bool handoff, float joinBeats = -1)
        {
            foreach (var w in CentreUp()) yield return w;   // room to charge: the lane always has real travel
            int charges = Clock.State == Tempo.Haste ? HasteCharges : NormalCharges;
            for (int c = 0; c < charges; c++)
            {
                if (c > 0 && !Frenzy && RushReady(PursuitTarget))   // ordinary blocks: he may close in between charges
                    foreach (var w in ClosingRush(PursuitTarget, "between charges")) yield return w;
                int warn = c == 0 ? (joinBeats > 0 ? Beats(joinBeats) : Bt(WakeWarn, Cadence.WakeWarn)) : Bt(WakeWarn2, Cadence.WakeWarn2);
                foreach (var w in Charge(c, warn)) yield return w;
            }
            if (handoff) yield break;
            Current = "Sundering Wake (recovery)";
            int rec = Mathf.Max(Beats(WakeRecover), LastFissureResolveAge - Age + 1);   // the block ends after its last eruption
            glowTarget = 0.5f; yield return rec; glowTarget = 1;
        }

        // One charge: warning (lane, landing marker, dashed backswing and mirror previews), travel, then the backswing
        // and this charge's own eruption lane (+ mirror in Haste). Returns when the backswing lands.
        IEnumerable<int> Charge(int c, int warn)
        {
            Current = "Sundering Wake";
            var target = WakeTarget();   // re-targets the farthest player from where he stands now
            var (start, end, yaw) = WakePath(target ? target.transform.position : transform.position + transform.forward * 6);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            float travelLen = Vector3.Distance(start, end);
            ChargeLog.Add((W.Tick, c, start, end, yaw, target));
            string tag = c == 0 ? "" : " " + (c + 1);
            var h = new Hazard { Source = "Sundering Wake (charge" + tag + ")", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = start, Yaw = yaw,
                Length = travelLen + BodyRadius + WakeReach, Width = WakeWidth, LockAt = 0, ResolveAt = warn + WakeTravelTicks, Damage = WakeDmg, Knockback = 2f,
                HitOnResolve = false };
            LastCharge = Live(h); if (c == 0) LastWake = LastCharge;
            var endMarker = StopMarker(end, false);   // where he stops (the lane runs on by his body and 2.6 m thrust reach)
            // the backswing, previewed (dashed) at the landing point from the start of the warning
            var bsPreview = W.BossFx.Cone(BackswingRadius, BackswingHalf); bsPreview.Memory();
            bsPreview.Clip(Vector3.zero, CombatWorld.PlayerBound + PlayerController.Radius); bsPreview.Place(end, BackYaw(yaw));
            BackswingPreviews++;
            // Haste: the fissure's mirror image, previewed (dashed) from now so players can read it before it goes live
            if (c == 0) LastMirror = null;
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
            // the backswing goes live on arrival: almost immediate, but it has been on the floor (dashed) all along
            bsPreview.Dismiss();
            int bs = Bt(Backswing, Cadence.Backswing);
            var b = BackswingHazard(end, yaw); b.ResolveAt = bs;
            b.OnResolve = hz => { BackswingResolves++; Fx.Arc(hz.Origin, hz.Yaw, c % 2 == 0 ? 1 : -1); Release(1f); CameraShake.Shake(0.1f, 0.18f); };
            LastBackswing = Live(b, false, 0.35f);
            // the delayed wake: a separate hazard instance with its own warning; it erupts exactly once
            int fissureWarn = Bt(FissureWarn, Cadence.FissureWarn);
            int serial = ++fissureSerial;
            var f = new Hazard { Source = "Sundering Wake (eruption" + tag + ")", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = start, Yaw = yaw,
                Length = travelLen, Width = FissureWidth, LockAt = 0, ResolveAt = fissureWarn, Damage = FissureDmg, Knockback = 1f };
            f.OnResolve = hz => { FissureEruptions++; Fx.Streak(hz.Origin, hz.Yaw, 0, hz.Length, RimAmber, hz.Width * 0.5f); if (serial == fissureSerial) Rig.Fissure(false); };
            LastFissure = Live(f, true);
            if (mirror)
            {
                mirrorPreview.Dismiss();
                var m = new Hazard { Source = "Sundering Wake (mirrored eruption" + tag + ")", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = mOrigin, Yaw = mYaw,
                    Length = travelLen, Width = FissureWidth, LockAt = 0, ResolveAt = fissureWarn, Damage = FissureDmg, Knockback = 1f };
                m.OnResolve = hz => { MirrorEruptions++; Fx.Streak(hz.Origin, hz.Yaw, 0, hz.Length, RimAmber, hz.Width * 0.5f); };
                LastMirror = Live(m, true);
                Announcement = "";
            }
            LastFissureResolveAge = Age + fissureWarn;
            Rig.Fissure(true, start, end);
            Current = "Sundering Wake (backswing)";
            Rig.Pose(TyrantPose.Backswing, bs);
            for (int i = 0; i < bs; i++) { Windup(i / (float)bs); yield return 1; }
            Rig.Pose(TyrantPose.Recover);
        }

        // ---------------- Hollow Crown ----------------
        // handoff: return once the ring has landed (the inner circle still warning). joinBeats > 0: ring warning.
        IEnumerable<int> Crown(bool handoff, float joinBeats = -1)
        {
            foreach (var w in CentreUp()) yield return w;
            Current = "Hollow Crown"; Rig.Pose(TyrantPose.RingCast);
            var c = Sim.Flat(transform.position);
            int ringT = joinBeats > 0 ? Beats(joinBeats) : Bt(CrownRing, Cadence.CrownRing), innerT = Bt(CrownInnerWarn, Cadence.CrownInner);
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
            inner.OnResolve = hz => { Fx.Rim(hz.Origin, hz.Radius, RimIvory); Release(1.0f); CameraShake.Shake(0.15f, 0.25f); if (handoff) Numbers.RemoveAll(x => x.number == 2 && x.at == label2); };
            LastInner = Live(inner, false, 0.5f);
            if (handoff) yield break;   // the inner circle is on the floor; the next attack overlaps it
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
            foreach (var h in group) { Live(h, false, flash); sliceHazards.Add((h, sliceUses)); }
            return group;
        }

        bool SliceAttackLive => sliceHazards.Any(x => !x.h.Cancelled && W.Hazards.Live.Contains(x.h));

        // handoff: return once the second every-other set is on the floor. joinBeats > 0: indicate when it overlaps.
        // forcedSafe >= 0: the first safe slice (Broken Cadence after a Severance: the slice behind him).
        // Broken Cadence reveals the first safe slice while the floor is indicated, so an overlap is readable from the start.
        IEnumerable<int> Slices(bool handoff, float joinBeats = -1, int forcedSafe = -1)
        {
            while (SliceAttackLive) yield return 1;   // never two slice attacks at once
            foreach (var w in CentreUp(allowRush: !Frenzy)) yield return w;   // he conducts the floor from the centre
            Current = "Spiral Slices"; Rig.Pose(TyrantPose.RingCast);
            SliceBlocks++; SlicesActive = true;
            SpiralDir = sliceUses++ % 2 == 0 ? 1 : -1;
            // the first safe slice is the one under the nearest player, so the opening is always reachable
            var near = W.NearestStanding(Vector3.zero);
            int safe = forcedSafe >= 0 ? forcedSafe : near ? SliceOf(near.transform.position) : 0;
            Announcement = SpiralDir > 0 ? "SPIRAL  RIGHT  >>>" : "<<<  SPIRAL  LEFT";

            // 1) indicate the whole floor: every slice dashed (Broken Cadence: all but the safe one, revealed now)
            var outline = new BellkeeperFx.Telegraph[SliceCount];
            for (int i = 0; i < SliceCount; i++) if (!Frenzy || i != safe) outline[i] = SliceOutline(i);
            if (Frenzy) SafeSlice = safe;
            int indicate = joinBeats > 0 ? Beats(joinBeats) : Bt(SliceIndicate, Cadence.SliceIndicate);
            for (int i = 0; i < indicate; i++) { Windup(0.3f * i / indicate); yield return 1; }
            // 2) the first slice goes (its outline clears) and the other 11 go live; then the safe slice spirals on
            for (int i = 0; i < SliceCount; i++) if (outline[i] != null) outline[i].Dismiss();
            SafeSlice = safe;
            int step = Bt(SliceStep, Cadence.SliceStep), first = Bt(SliceFirst, Cadence.SliceFirst);
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
            int altWarn = Bt(AltWarn, Cadence.AltWarn), altGap = Bt(AltGap, Cadence.AltGap);
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
            var groupB = SliceVolley(setB, "Every Other Slice (2)", AltDmg, altGap, 0.4f);
            SliceLog.Add((W.Tick + altGap, -1, 1 - parity));
            if (handoff && groupB.Count > 0)
            {
                // set B is on the floor; the next attack overlaps it. The attack ends when B lands.
                var lead = groupB[0]; var hit = lead.OnResolve;
                lead.OnResolve = hz => { hit?.Invoke(hz); Release(1f); CameraShake.Shake(0.12f, 0.2f); SlicesActive = false; AltSafeParity = -1; };
                yield break;
            }
            for (int i = 0; i < altGap; i++) { Windup(i / (float)altGap); yield return 1; }
            Release(1f); CameraShake.Shake(0.12f, 0.2f);
            SlicesActive = false; AltSafeParity = -1;
            Current = "Spiral Slices (recovery)"; Rig.Pose(TyrantPose.Recover);
            glowTarget = 0.5f; yield return Beats(SliceRecover); glowTarget = 1;
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
            ClearOrbs();
            Rig.Fissure(false); Rig.Pose(TyrantPose.Defeat);
            if (Audio) Audio.StopAll();
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
