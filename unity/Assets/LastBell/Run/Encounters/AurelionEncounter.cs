using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion, the Meridian Regent: the Skyward Spire area boss. Meshy model rigged in Blender (4.2 m) and the
    // Steady Ticking schedule from the boss packet (bosses/1-skyward-spire-aurelion/BOSS.md, handoff PR #46). Every
    // number below starts from that packet and is a proposal, not tested balance. Times are converted at 60 Hz
    // ("track" = ticks the warning follows before it locks):
    //   attack               shape                       warn  track  recover  cooldown  damage
    //   Meridian Cut         80° cone, 6 m               96    66     90       300       14  DASH CHECK
    //   Zenith Pins          circles r 1.8 on players    105   78     84       540       12  DASH CHECK
    //   Processional Thrust  2 m lane, up to 9 m travel  102   0      132      660       16  hits as he travels
    //   Crown Reversal       rear 120° sector, 4 m       90    0      108      480       12  (phase 2+, rear camping)
    //   Meridian Alignment   ring 4–8, then ring 2.6–4.6 120 + 108    180      1200      14 per ring
    // Dash checks (user decision 2026-10-21): the warning still shows for ~1.6 s but keeps tracking, then locks with
    // 0.5 s (Cut) / 0.45 s (Pins) left. From the worst spot neither walking nor sprinting gets out after the lock; a
    // dash (3 m in 0.2 s, no immunity) does. Ring II keeps a real safe pocket hugging his body (1.55–2.3 m from his
    // centre) as well as the outside, so melee can stay on him through the second pulse.
    // Phases: 100–65 % Perfect Order (isolated attacks), 65–30 % Misalignment (Pins lead into Cut, Reversal on),
    // 30–0 % Exposed Meridian (Alignment, then after full recovery a Thrust). Transitions queue to attack boundaries and
    // a burst through two thresholds plays one transition. At most two damaging patterns are ever live (Pins + Cut).
    // Not in this graybox: the Midnight Toll tier's Counterclock, boss stagger, the authored model and audio.
    public sealed partial class AurelionEncounter : BossEncounter
    {
        public override string Id => "boss.aurelion";
        public override string DisplayName => "Aurelion, the Meridian Regent";
        public override string Lesson => "Watch the lock, take the flank, punish the lowered blade";

        public const float CutReach = 6f, CutHalf = 40f, PinRadius = 1.8f, ThrustWidth = 2f, ThrustMax = 9f;
        public const float RevReach = 4f, RevHalf = 60f, AlignOuterIn = 4f, AlignOuterOut = 8f, AlignInnerIn = 2.6f, AlignInnerOut = 4.6f;
        public const int CutWarn = 96, CutTrack = 66, CutRec = 90, CutCd = 300, CutDmg = 14;
        public const int PinWarn = 105, PinTrack = 78, PinRec = 84, PinCd = 540, PinDmg = 12;
        public const int ThrustWarn = 102, ThrustTravel = 30, ThrustRec = 132, ThrustCd = 660, ThrustDmg = 16;
        public const int RevWarn = 90, RevRec = 108, RevCd = 480, RevDmg = 12, RevCamp = 180;
        public const int AlignWarn1 = 120, AlignWarn2 = 108, AlignRec = 180, AlignCd = 1200, AlignDmg = 14;
        public const int HitStun = 9;                     // ~0.15 s (packet), shorter than the shared default
        const float WalkSpeed = 2.2f, CampRange = RevReach + 2.5f;
        const int TeachGap = 30;                          // Perfect Order: extra pause after every attack

        public int Phase => phase;
        public int AlignmentsStarted, ReversalsStarted, PinVolleys, ThrustsStarted, CutsStarted, PinsCutCombos;
        public int MaxLiveHazards;                        // test evidence: never above two
        public int LastReversalBehindTicks, AlignThenThrust, AlignThenThrustGap = int.MaxValue;
        public int ThrustTravelHits;                      // hits dealt by the moving blade (test evidence)
        int alignEndAge;
        int phase = 1;
        int cutReady, pinReady = 60, thrustReady = 240, revReady, alignReady = 600;
        readonly Dictionary<PlayerController, int> behind = new Dictionary<PlayerController, int>();
        int pinRotation;

        // HP = measured sustained DPS x 150 s (packet). First autotest measurement: Vanguard ~90 DPS while dodging -> 13 500.
        void Awake() { MaxHp = 13500;BodyRadius = 1.1f; HitHeight = 2.6f; MarkHeight = 5.2f; }

        int PhaseFor() => Hp > MaxHp * 0.65f ? 1 : Hp > MaxHp * 0.30f ? 2 : 3;
        public void DebugReadyThrust() { thrustReady = 0; }   // test hook: the next pass may choose the Thrust
        public override float NextPhaseFraction() => Hp > MaxHp * 0.65f ? 0.64f : 0.29f;
        bool Ready(int at) => Age >= at;

        protected override IEnumerable<int> Script() => AurelionArgs.V1 ? V1Script() : V2Script();

        // ability set v1 (the packet's five attacks), kept for comparison behind -lastbellAbilitySet v1; v2 is in Aurelion/AurelionEncounter.V2.cs
        IEnumerable<int> V1Script()
        {
            int want = PhaseFor();
            if (want > phase)
            {
                phase = want;   // one transition even if a burst crossed both thresholds
                foreach (var w in PhaseShift()) yield return w;
            }

            if (phase >= 2 && Ready(revReady) && RearCamper())
                foreach (var w in Reversal()) yield return w;
            else if (phase == 3 && Ready(alignReady))
            {
                foreach (var w in Alignment()) yield return w;
                if (PhaseFor() == phase && Ready(thrustReady) && W.AnyStanding)
                {
                    AlignThenThrust++; AlignThenThrustGap = Mathf.Min(AlignThenThrustGap, Age - alignEndAge);
                    foreach (var w in Thrust()) yield return w;
                }
            }
            else if (phase < 3 && Ready(alignReady) && DistToTarget() < AlignOuterOut + 0.5f)
                foreach (var w in Alignment()) yield return w;
            else if (Ready(thrustReady) && DistToTarget() > CutReach + 1)
                foreach (var w in Thrust()) yield return w;
            else if (Ready(pinReady))
            {
                if (phase >= 2 && Ready(cutReady)) foreach (var w in PinsThenCut()) yield return w;
                else foreach (var w in Pins()) yield return w;
            }
            else if (Ready(cutReady))
            {
                foreach (var w in Approach(CutReach - 1.5f, WalkSpeed, 90)) yield return w;
                if (DistToTarget() <= CutReach + 0.5f) foreach (var w in Cut()) yield return w;
            }
            else
            {
                foreach (var w in Approach(4.5f, WalkSpeed, 30)) yield return w;
                Current = "idle"; yield return 12;
            }
            yield return 1;   // every pass takes at least one tick, so the script never ends empty
        }

        // ---------------- attacks ----------------
        Hazard Tune(Hazard h) { h.Stun = HitStun; MaxLiveHazards = Mathf.Max(MaxLiveHazards, W.Hazards.Count); return h; }
        IEnumerable<int> Recover(int ticks)
        {
            Current = "recovery"; glowTarget = 0.4f;
            yield return ticks + (phase == 1 ? TeachGap : 0);
            glowTarget = 1;
        }

        Hazard CutHazard()
        {
            CutsStarted++; cutReady = Age + CutCd; pose = Pose.CutWindup;
            var h = Tune(Cone("Meridian Cut", CutReach, CutHalf, CutTrack, CutWarn, CutDmg));
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); pose = Pose.CutDone; };
            return h;
        }
        IEnumerable<int> Cut()
        {
            Current = "Meridian Cut"; CutHazard(); Clip("MeridianCut", CutWarn + CutRec);
            for (int i = 0; i < CutWarn; i++) { Windup(i / (float)CutWarn); yield return 1; }
            foreach (var w in Recover(CutRec)) yield return w;
        }

        // one circle per recipient: 1 solo, 2 for 2–4 players, 3 for 5–8, rotating through the standing players
        List<Hazard> PinVolley()
        {
            PinVolleys++; pinReady = Age + PinCd; pose = Pose.Halo;
            var standing = W.Players.FindAll(p => !p.Down);
            int n = standing.Count <= 1 ? 1 : standing.Count <= 4 ? 2 : 3;
            var list = new List<Hazard>();
            for (int i = 0; i < Mathf.Min(n, standing.Count); i++)
            {
                var who = standing[(pinRotation + i) % standing.Count];
                var h = Tune(W.Hazards.Spawn(new Hazard
                {
                    Source = "Zenith Pins", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = PinRadius,
                    LockAt = PinTrack, ResolveAt = PinWarn, Damage = PinDmg, Origin = Bounded(who.transform.position),
                }));
                h.Track = hz =>
                {
                    // a downed or removed recipient's pin is withdrawn before it locks (no impossible assignment)
                    if (!who || who.Down) { W.Hazards.Cancel(hz); return; }
                    hz.Origin = Bounded(who.transform.position);
                };
                h.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); Release(0.6f); };
                list.Add(h);
            }
            pinRotation += n;
            return list;
        }
        static Vector3 Bounded(Vector3 p)
        {
            var v = new Vector2(p.x, p.z); float max = CombatWorld.PlayerBound - PinRadius * 0.5f;
            if (v.magnitude > max) v = v.normalized * max;
            return new Vector3(v.x, 0, v.y);
        }
        IEnumerable<int> Pins()
        {
            Current = "Zenith Pins"; TurnTowards(YawToTarget(), 9999); Clip("ZenithPins", PinWarn + PinRec);
            PinVolley();
            for (int i = 0; i < PinWarn; i++) { Windup(i / (float)PinWarn); yield return 1; }
            pose = Pose.Neutral;
            foreach (var w in Recover(PinRec)) yield return w;
        }

        // Misalignment: the Cut's warning starts when the pins lock, so the pins resolve (105) well before the cut (174):
        // two separate dash checks, about 1.2 s apart
        IEnumerable<int> PinsThenCut()
        {
            PinsCutCombos++;
            Current = "Zenith Pins"; TurnTowards(YawToTarget(), 9999); Clip("ZenithPins", PinWarn + PinRec);
            PinVolley();
            yield return PinTrack;
            Current = "Meridian Cut"; CutHazard(); Clip("MeridianCut", CutWarn + CutRec);
            for (int i = 0; i < CutWarn; i++) { Windup(i / (float)CutWarn); yield return 1; }
            foreach (var w in Recover(CutRec)) yield return w;
        }

        IEnumerable<int> Thrust()
        {
            Current = "Processional Thrust"; ThrustsStarted++; thrustReady = Age + ThrustCd; pose = Pose.Level;
            Clip("ProcessionalThrust", ThrustWarn + ThrustTravel + ThrustRec);
            var start = transform.position; var target = Target();
            var dir = Sim.Flat(target - start); if (dir.sqrMagnitude < 1) dir = transform.forward; dir.Normalize();
            float travel = Mathf.Clamp(Sim.Flat(target - start).magnitude + 2f, 3f, ThrustMax);
            var end = ClampToBound(start, start + dir * travel);   // the boss stays inside its bound: a wall corridor remains
            float yaw = Sim.YawTo(start, end, transform.eulerAngles.y);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            // the lane stays up through the travel; it deals no damage when it expires: the blade hits whoever is
            // still inside the lane when his front (body + glaive tip) reaches them, so a late sidestep still works
            var h = Tune(Lane("Processional Thrust", start, yaw, Vector3.Distance(start, end) + BodyRadius, ThrustWidth, ThrustWarn + ThrustTravel, ThrustDmg));
            h.HitOnResolve = false;
            for (int i = 0; i < ThrustWarn; i++) { Windup(i / (float)ThrustWarn); yield return 1; }
            pose = Pose.Thrust; Release(); W.BossFx.ChargeTrail(transform, true);
            var fwd = Sim.Dir(yaw);
            for (int i = 1; i <= ThrustTravel; i++)
            {
                transform.position = Vector3.Lerp(start, end, i / (float)ThrustTravel);
                float front = Vector3.Distance(start, transform.position) + BodyRadius;
                foreach (var p in W.Players)
                {
                    if (p.Down || h.Hit.Contains(p) || !h.Contains(p.transform.position)) continue;
                    if (Vector3.Dot(Sim.Flat(p.transform.position - start), fwd) > front + Hazard.Margin) continue;   // not reached yet
                    h.Hit.Add(p); p.ReceiveHit(h);
                    ThrustTravelHits++;
                }
                yield return 1;
            }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position);
            foreach (var w in Recover(ThrustRec)) yield return w;
            pose = Pose.Neutral;
        }

        // eligible only after someone has stayed in the rear sector for more than 3 s
        bool RearCamper()
        {
            foreach (var kv in behind) if (kv.Value > RevCamp && kv.Key && !kv.Key.Down) return true;
            return false;
        }
        public bool InRear(Vector3 p)
        {
            var v = Sim.Flat(p - transform.position);
            return v.magnitude <= CampRange && Vector3.Angle(transform.forward, v) >= 180 - RevHalf;
        }
        IEnumerable<int> Reversal()
        {
            Current = "Crown Reversal"; ReversalsStarted++; revReady = Age + RevCd; pose = Pose.LookBack;
            Clip("CrownReversal", RevWarn + RevRec);
            LastReversalBehindTicks = 0; foreach (var kv in behind) LastReversalBehindTicks = Mathf.Max(LastReversalBehindTicks, kv.Value);
            behind.Clear();
            var h = Tune(W.Hazards.Spawn(new Hazard
            {
                Source = "Crown Reversal", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Radius = RevReach, HalfAngle = RevHalf,
                LockAt = 0, ResolveAt = RevWarn, Damage = RevDmg, Origin = transform.position, Yaw = transform.eulerAngles.y + 180,
            }));
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            for (int i = 0; i < RevWarn; i++) { Windup(i / (float)RevWarn); yield return 1; }
            pose = Pose.Neutral;
            foreach (var w in Recover(RevRec)) yield return w;
        }

        // the signature: plant, outer ring, then (only after it resolves) the inner ring, then the open-chest window
        public bool SecondRingSeenBeforeFirstResolved;
        IEnumerable<int> Alignment()
        {
            Current = "Meridian Alignment"; AlignmentsStarted++; alignReady = Age + AlignCd; pose = Pose.Planted;
            Clip("MeridianAlignment", AlignWarn1 + AlignWarn2);
            var at = transform.position;
            bool firstDone = false;
            var a = Tune(Ring("Meridian Alignment", at, AlignOuterIn, AlignOuterOut, AlignWarn1, AlignDmg));
            a.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); Release(); firstDone = true; };
            SpawnOrrery(a, 1);
            for (int i = 0; i < AlignWarn1; i++) { Windup(i / (float)AlignWarn1); yield return 1; }
            if (!firstDone) SecondRingSeenBeforeFirstResolved = true;   // test evidence: must never happen
            var b = Tune(Ring("Meridian Alignment II", at, AlignInnerIn, AlignInnerOut, AlignWarn2, AlignDmg));
            b.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); Release(1.2f); };
            SpawnOrrery(b, -1);
            for (int i = 0; i < AlignWarn2; i++) { Windup(i / (float)AlignWarn2); yield return 1; }
            Current = "open chest"; pose = Pose.Exposed; glowTarget = 0.3f; Clip("OpenChest", AlignRec);
            alignEndAge = Age;   // the last ring has just resolved
            yield return AlignRec;
            glowTarget = 1; pose = Pose.Neutral;
        }

        // Orrery rings (presentation only): two dashed gold circles laid exactly on a ring warning's inner and outer
        // edges, with clock ticks on the outer one and a planet bead riding it. They spin up and brighten as the pulse
        // nears, flash on resolve and sink away. Because they trace the warning's own borders they add spectacle
        // without drawing any light where there is no danger. A hazard cleared early (defeat) just fades them.
        sealed class Orrery
        {
            public Hazard Hazard; public Transform Root, Bead; public Material Mat; public float Spin, Turn, Fade = -1, BeadR;
        }
        readonly List<Orrery> orreries = new List<Orrery>();
        void SpawnOrrery(Hazard h, float spin)
        {
            var o = new Orrery { Hazard = h, Spin = spin, Mat = Prim.Material(Gold, 1f), BeadR = h.Radius };
            o.Root = new GameObject("Orrery").transform; o.Root.SetParent(W.Root, false);
            o.Root.position = new Vector3(h.Origin.x, 0.03f, h.Origin.z);
            foreach (float r in new[] { h.Inner, h.Radius })
            {
                int n = Mathf.Max(12, Mathf.CeilToInt(2 * Mathf.PI * r / 0.6f));
                for (int k = 0; k < n; k++)
                {
                    float ang = k * 360f / n;
                    var seg = Prim.Make(PrimitiveKind.Cube, o.Root, Quaternion.Euler(0, ang, 0) * new Vector3(0, 0, r), new Vector3(2 * Mathf.PI * r / n * 0.62f, 0.05f, 0.06f), Gold, 0, Quaternion.Euler(0, ang + 90, 0));
                    Share(seg, o.Mat);
                }
            }
            for (int k = 0; k < 12; k++)
            {
                var tick = Prim.Make(PrimitiveKind.Cube, o.Root, Quaternion.Euler(0, k * 30, 0) * new Vector3(0, 0.04f, h.Radius - 0.25f), new Vector3(k % 3 == 0 ? 0.14f : 0.08f, 0.08f, k % 3 == 0 ? 0.6f : 0.35f), Gold, 0, Quaternion.Euler(0, k * 30, 0));
                Share(tick, o.Mat);
            }
            o.Bead = Prim.Make(PrimitiveKind.Sphere, o.Root, new Vector3(0, 0.18f, h.Radius), Vector3.one * 0.3f, Gold);
            Share(o.Bead, o.Mat);
            orreries.Add(o);
        }
        static void Share(Transform t, Material m) { var r = t.GetComponent<Renderer>(); Destroy(r.sharedMaterial); r.sharedMaterial = m; }
        void UpdateOrreries(float dt)
        {
            for (int i = orreries.Count - 1; i >= 0; i--)
            {
                var o = orreries[i];
                if (!o.Root) { orreries.RemoveAt(i); continue; }
                bool live = W != null && W.Hazards.Live.Contains(o.Hazard);
                float k = Mathf.Clamp01(o.Hazard.Age / (float)Mathf.Max(1, o.Hazard.ResolveAt));
                if (!live && o.Fade < 0) o.Fade = o.Hazard.Age >= o.Hazard.ResolveAt ? 1f : 0.4f;   // resolved: flash; cleared: dim
                if (o.Fade >= 0)
                {
                    o.Fade -= dt * 1.6f;
                    o.Mat.SetColor("_EmissionColor", Gold * Mathf.Max(0, o.Fade) * 6f);
                    o.Root.position += Vector3.down * dt * 0.5f;
                    if (o.Fade <= 0) { Destroy(o.Root.gameObject); Destroy(o.Mat); orreries.RemoveAt(i); }
                    continue;
                }
                o.Turn += o.Spin * Mathf.Lerp(12f, 120f, k * k) * dt;   // the clockwork winds up toward the pulse
                o.Root.rotation = Quaternion.Euler(0, o.Turn, 0);
                o.Root.position = new Vector3(o.Root.position.x, Mathf.Lerp(0.03f, 0.12f, k), o.Root.position.z);
                o.Bead.localPosition = Quaternion.Euler(0, o.Turn * 2.5f, 0) * new Vector3(0, 0.18f, o.BeadR);
                o.Mat.SetColor("_EmissionColor", Gold * (0.3f + 2.4f * k * k));
            }
        }

        // non-damaging, readable transformation; the boss stays targetable throughout
        IEnumerable<int> PhaseShift()
        {
            Current = "phase shift"; SetPhase(phase == 2 ? "Misalignment" : "Exposed Meridian");
            pose = Pose.Neutral; glowTarget = 2.5f; Clip("PhaseShift", 90);
            for (int i = 0; i < 90; i++) { if (i == 45) { W.Fx.Crescendo(transform.position, true, 0, false); Release(1.2f); } yield return 1; }
            glowTarget = 1;
        }

        protected override void StepAlways()
        {
            foreach (var p in W.Players)
            {
                if (p.Down) { behind.Remove(p); continue; }
                behind.TryGetValue(p, out int t);
                behind[p] = InRear(p.transform.position) ? t + 1 : 0;
            }
            MaxLiveHazards = Mathf.Max(MaxLiveHazards, W.Hazards.Count);
            if (presenter) presenter.TickBoss(Current == "approach" || Current == "reposition" ? "Walk" : "Idle");
        }
        public int BehindTicks(PlayerController p) => behind.TryGetValue(p, out int t) ? t : 0;

        // ---------------- model: the Meshy Aurelion rigged in Blender (prototype/arena-3d/models/build-aurelion.ps1) ----------------
        // Clips are authored so their strike frame sits at the attack's resolve time; PlayOnce stretches a clip to the
        // given ticks, which keep the same proportions. Without the model the code-built stand-in below is used.
        CharacterPresenter presenter;
        public CharacterPresenter Presenter => presenter;
        public override bool Placeholder => !presenter || !presenter.HasModel;
        void Clip(string name, int ticks) { if (presenter) presenter.Action(name, ticks); }

        // ---------------- stand-in presentation (used only when Resources/Characters/Aurelion is missing) ----------------
        enum Pose { Neutral, CutWindup, CutDone, Halo, Level, Thrust, LookBack, Planted, Exposed }
        Pose pose;
        Transform body, glaive, head, pendulum, shutterL, shutterR, shoulderL, banners;
        readonly Transform[] arcs = new Transform[3];
        Renderer pendulumR; float swing, kneel;
        static readonly Color Ivory = new Color(0.93f, 0.9f, 0.82f), Gold = new Color(0.82f, 0.64f, 0.3f),
            Cobalt = new Color(0.2f, 0.32f, 0.85f), Navy = new Color(0.12f, 0.15f, 0.32f), Dark = new Color(0.08f, 0.08f, 0.12f);

        protected override void Build()
        {
            PhaseName = "Perfect Order";
            presenter = CharacterPresenter.Create(transform, "Aurelion", 3f, 0.8f, 0, host => { BuildStandIn(host); return host; });
            BindAbilities();
        }

        void BuildStandIn(Transform host)
        {
            body = new GameObject("Body").transform; body.SetParent(host, false);
            // legs: long, brass knee and hip joints
            foreach (float s in new[] { -1f, 1f })
            {
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.32f * s, 0.55f, 0), new Vector3(0.28f, 0.55f, 0.32f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0.32f * s, 1.12f, 0.02f), Vector3.one * 0.26f, Gold);
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.3f * s, 1.65f, 0), new Vector3(0.32f, 0.5f, 0.36f), Ivory);
                Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0.3f * s, 2.15f, 0), Vector3.one * 0.3f, Gold);
                Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.3f * s, 0.06f, 0.12f), new Vector3(0.22f, 0.12f, 0.5f), Ivory);
            }
            // narrow waist, belfry chest
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 2.3f, 0), new Vector3(0.45f, 0.18f, 0.4f), Navy);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.95f, 0.02f), new Vector3(1.05f, 1.0f, 0.75f), Ivory);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 2.95f, 0.39f), new Vector3(0.42f, 0.7f, 0.04f), Dark);   // lancet opening
            pendulum = new GameObject("Pendulum").transform; pendulum.SetParent(body, false); pendulum.localPosition = new Vector3(0, 3.25f, 0.33f);
            Prim.Make(PrimitiveKind.Cube, pendulum, new Vector3(0, -0.25f, 0), new Vector3(0.04f, 0.5f, 0.04f), Gold);
            pendulumR = Prim.Make(PrimitiveKind.Sphere, pendulum, new Vector3(0, -0.52f, 0), Vector3.one * 0.2f, Gold, 1.5f).GetComponent<Renderer>();
            shutterL = Prim.Make(PrimitiveKind.Cube, body, new Vector3(-0.11f, 2.95f, 0.43f), new Vector3(0.22f, 0.72f, 0.05f), Ivory);
            shutterR = Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.11f, 2.95f, 0.43f), new Vector3(0.22f, 0.72f, 0.05f), Ivory);
            // broad architectural shoulders
            shoulderL = Prim.Make(PrimitiveKind.Cube, body, new Vector3(-0.78f, 3.35f, 0), new Vector3(0.6f, 0.36f, 0.7f), Ivory);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0.78f, 3.35f, 0), new Vector3(0.6f, 0.36f, 0.7f), Ivory);
            foreach (float s in new[] { -1f, 1f })
                Prim.Make(PrimitiveKind.Capsule, body, new Vector3(0.85f * s, 2.75f, 0), new Vector3(0.22f, 0.5f, 0.22f), Ivory);
            // elongated mouthless mask, cobalt eye slits, brass brow
            head = new GameObject("Head").transform; head.SetParent(body, false); head.localPosition = new Vector3(0, 3.75f, 0.05f);
            Prim.Make(PrimitiveKind.Capsule, head, Vector3.zero, new Vector3(0.34f, 0.32f, 0.3f), Ivory);
            Prim.Make(PrimitiveKind.Cube, head, new Vector3(0, 0.12f, 0.12f), new Vector3(0.3f, 0.05f, 0.08f), Gold);
            foreach (float s in new[] { -1f, 1f })
                Prim.Make(PrimitiveKind.Cube, head, new Vector3(0.07f * s, 0.05f, 0.15f), new Vector3(0.08f, 0.025f, 0.02f), Cobalt, 4f);
            Prim.Make(PrimitiveKind.Cone, head, new Vector3(0, 0.42f, 0), new Vector3(0.12f, 0.25f, 0.12f), Gold);
            // three orrery arcs on independent pivots behind the shoulders
            for (int a = 0; a < 3; a++)
            {
                arcs[a] = new GameObject("Arc" + a).transform; arcs[a].SetParent(body, false);
                arcs[a].localPosition = new Vector3(0, 3.5f, -0.5f);
                float r = 1.0f + a * 0.28f;
                for (int k = 0; k <= 8; k++)
                {
                    float ang = Mathf.Lerp(-110, 110, k / 8f) * Mathf.Deg2Rad;
                    Prim.Make(PrimitiveKind.Cube, arcs[a], new Vector3(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r, 0), new Vector3(0.09f, 0.26f, 0.05f), Gold, 0.4f, Quaternion.Euler(0, 0, -ang * Mathf.Rad2Deg));
                }
                Prim.Make(PrimitiveKind.Sphere, arcs[a], new Vector3(Mathf.Sin(1.92f) * r, Mathf.Cos(1.92f) * r, 0), Vector3.one * 0.14f, Gold);
            }
            // two navy waist banners, front and back, ending above the ankles
            banners = new GameObject("Banners").transform; banners.SetParent(body, false);
            Prim.Make(PrimitiveKind.Cube, banners, new Vector3(0, 1.3f, 0.3f), new Vector3(0.36f, 1.9f, 0.03f), Navy);
            Prim.Make(PrimitiveKind.Cube, banners, new Vector3(0, 1.3f, -0.3f), new Vector3(0.36f, 1.9f, 0.03f), Navy);
            // the clock-hand glaive: ivory blade, brass counterweight, held diagonally in the right hand
            glaive = new GameObject("Glaive").transform; glaive.SetParent(body, false); glaive.localPosition = new Vector3(0.95f, 2.4f, 0.25f);
            Prim.Make(PrimitiveKind.Cylinder, glaive, Vector3.zero, new Vector3(0.08f, 2.1f, 0.08f), Navy);
            Prim.Make(PrimitiveKind.Cube, glaive, new Vector3(0, 2.6f, 0), new Vector3(0.06f, 1.1f, 0.3f), Ivory, 0.5f);
            Prim.Make(PrimitiveKind.Sphere, glaive, new Vector3(0, 2.05f, 0), Vector3.one * 0.26f, Gold);
            Prim.Make(PrimitiveKind.Sphere, glaive, new Vector3(0, -2.1f, 0), Vector3.one * 0.3f, Gold);
        }

        protected override void Update()
        {
            base.Update();
            UpdateOrreries(Time.deltaTime);
            V2DevKeys();
            if (presenter) presenter.GlowLevel = glowLevel;
            if (!body) return;
            float dt = Time.deltaTime;
            // one idle pendulum beat; it stops on defeat
            if (!Dead) swing += dt * 2.4f;
            pendulum.localRotation = Quaternion.Euler(0, 0, Dead ? 0 : Mathf.Sin(swing) * 18f);
            Prim.SetGlow(pendulumR, Gold * (0.6f + glowLevel * (phase == 3 ? 1.4f : 0.6f)));

            // glaive pose follows the attack (presentation only: the hazards decide hits)
            Vector3 gEuler = pose switch
            {
                Pose.CutWindup => new Vector3(0, 0, 70), Pose.CutDone => new Vector3(80, 0, -40),
                Pose.Level or Pose.Thrust => new Vector3(90, 0, 0), Pose.Halo => new Vector3(0, 0, -10),
                Pose.Planted or Pose.Exposed => new Vector3(0, 0, 0), Pose.LookBack => new Vector3(-30, 0, 20),
                _ => new Vector3(0, 0, -18),
            };
            if (Dead) gEuler = Vector3.zero;
            glaive.localRotation = Quaternion.Slerp(glaive.localRotation, Quaternion.Euler(gEuler), dt * 8f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(0, pose == Pose.LookBack ? 150 : 0, 0), dt * 6f);

            // phases: cracked, tilted shoulder and independent arcs (2); chest shutters open (3)
            float t = Time.time;
            for (int a = 0; a < 3; a++)
            {
                Quaternion target;
                if (Dead) target = Quaternion.Euler(0, 0, a * 25f - 25f) * Quaternion.Euler(70, 0, 0);
                else if (phase == 1) target = Quaternion.Euler(0, 0, pose == Pose.Halo ? Mathf.Sin(t * 6 + a) * 6 : 0);
                else if (phase == 2) target = Quaternion.Euler(Mathf.Sin(t * 0.7f + a * 2.1f) * 25f, Mathf.Sin(t * 0.5f + a) * 20f, Mathf.Sin(t * 0.9f + a) * 35f);
                else target = Quaternion.Euler(0, (a - 1) * 35f, Mathf.Sin(t * 0.6f + a) * 15f);
                arcs[a].localRotation = Quaternion.Slerp(arcs[a].localRotation, target, dt * 3f);
                arcs[a].localScale = Vector3.one * (phase == 3 && !Dead ? 1.25f : 1f);
            }
            shoulderL.localRotation = Quaternion.Slerp(shoulderL.localRotation, Quaternion.Euler(0, 0, phase >= 2 ? 16 : 0), dt * 4f);
            float open = phase == 3 || pose == Pose.Exposed ? 1 : 0;
            shutterL.localPosition = Vector3.Lerp(shutterL.localPosition, new Vector3(-0.11f - 0.26f * open, 2.95f, 0.43f), dt * 4f);
            shutterR.localPosition = Vector3.Lerp(shutterR.localPosition, new Vector3(0.11f + 0.26f * open, 2.95f, 0.43f), dt * 4f);

            // defeat: plants the glaive and kneels
            kneel = Mathf.MoveTowards(kneel, Dead ? 1 : 0, dt * 1.2f);
            body.localPosition = new Vector3(0, -0.9f * kneel, 0);
            body.localRotation = Quaternion.Euler(12f * kneel, 0, 0);
            banners.localRotation = Quaternion.Euler(Current == "Processional Thrust" ? 20 : 0, 0, 0);
        }

        protected override void OnDefeat()
        {
            glowTarget = 0.1f; glowLevel = 2.5f; pose = Pose.Planted; Clip("Defeat", 150);
            W.Fx.Crescendo(transform.position, true, 2, true);
        }
    }
}
