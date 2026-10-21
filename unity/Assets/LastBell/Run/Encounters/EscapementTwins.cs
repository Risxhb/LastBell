using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Escapement Twins, The Two Who Agree: Brass Foundry miniboss, Unity block-out of docs/design/escapement-twins.md
    // (PR #55, Steady Ticking). One boss on one stadium body (5.2 x 2.2 m) with two sender towers, Sun (left) and Moon
    // (right). It never leaves the arena centre and only turns in the 1 s gap between attacks (§4). Its trick: a Verdict
    // lane whose centreline passes over the OPPOSITE tower's pawl jams it, stretching that cast's recovery to 4 s (§3.2).
    // Code-built stand-in geometry; timings, shapes and damage are the spec's, HP is shortened like the other test bosses.
    public sealed class EscapementTwins : BossEncounter
    {
        public override string Id => "miniboss.escapement_twins";
        public override string DisplayName => "The Escapement Twins";
        public override string Lesson => "Bait the Verdict lane across the far pawl, sidestep it, punish the jam";
        public const string Phase1 = "Agreement", Phase2 = "Disagreement";

        // ---- spec §2 / §3 numbers (ticks at 60 Hz, metres) ----
        public const float TowerX = 1.6f, Seg = 1.5f, Radius = 1.1f;
        public const float VerdictWidth = 1.5f, VerdictReach = 8f;
        public const int VerdictTrack = 54, VerdictResolve = 114, VerdictActive = 15, VerdictRecovery1 = 156, VerdictRecovery2 = 168, JamRecovery = 240, VerdictDmg = 18;
        public const float RebuttalRadius = 5.1f, RebuttalHalf = 50f, PairHalf = 40f;
        public const int RebuttalResolve = 96, RebuttalActive = 18, RebuttalRecovery = 162, RebuttalDmg = 18;
        public const int PairSecond = 90, PairRecovery = 204;
        public const float RingInner = 2.5f, RingOuter = 5.5f;
        public const int RingResolve = 120, RingActive = 18, RingRecovery = 180, RingDmg = 26;
        public const float DeadbeatInner = 1.3f, DeadbeatReach = 8f;
        public const int DeadbeatResolve = 120, DeadbeatActive = 18, DeadbeatRecovery = 216, DeadbeatDmg = 26;
        public const int Gap = 60, TransitionTicks = 120; public const float TurnRate = 40f;
        public const int HitStun = 9;   // 0.15 s, no knockback (spec §2)
        static readonly Vector3 P1Pawl = new Vector3(0.7f, 0, 1.3f), P2Pawl = new Vector3(0.9f, 0, 1.5f);
        const float P1PawlR = 0.5f, P2PawlR = 0.6f;

        public int Phase { get; private set; } = 1;
        public int SelfStrikes { get; private set; }
        public bool LastVerdictJammed { get; private set; }
        public bool LastSenderSun { get; private set; } = true;
        public int VerdictsCast { get; private set; }
        public int RecoveryTicks { get; private set; }   // the recovery just entered, for tests
        public override float NextPhaseFraction => 0.49f;

        System.Random rng; int targetTurn, sameSender;
        // presentation
        Transform body, sunTower, moonTower, bridge, ratchet, sunPawl, moonPawl;
        Renderer sunFace, moonFace, core;
        float sunLift, moonLift, sunPawlUp, moonPawlUp, tilt, spin = 60, jam;

        void Awake() { MaxHp = 1800; BodyRadius = Radius; BodySegment = Seg; HitHeight = 1.4f; MarkHeight = 4.4f; }

        // ---------------- geometry helpers ----------------
        Vector3 Tower(bool sun) => transform.TransformPoint(new Vector3(sun ? -TowerX : TowerX, 0, 0));
        public Vector3 Pawl(bool sunSide)
        {
            var p = Phase == 1 ? P1Pawl : P2Pawl;
            return transform.TransformPoint(new Vector3(sunSide ? -p.x : p.x, 0, p.z));
        }
        public float PawlRadius => Phase == 1 ? P1PawlR : P2PawlR;
        public float BodyDistance(Vector3 p) => Sim.Flat(p - Core(p)).magnitude - BodyRadius;
        // centreline from the sender passes within the pawl's radius, with the pawl ahead (spec §3.2)
        public bool CrossesPawl(Vector3 from, float yaw, Vector3 pawl, float r)
        {
            var u = Sim.Dir(yaw); var w = Sim.Flat(pawl - from);
            float along = Vector3.Dot(w, u); return along > 0 && (w - u * along).magnitude <= r;
        }
        float ExitDistance(Vector3 from, Vector3 u)
        {
            float last = 0; for (float t = 0; t < 6; t += 0.05f) if (BodyDistance(from + u * t) <= 0) last = t; return last;
        }

        PlayerController RotatingTarget()
        {
            var standing = W.Players.FindAll(p => !p.Down);
            if (standing.Count == 0) return null;
            return standing[targetTurn++ % standing.Count];
        }
        PlayerController Nearest()
        {
            PlayerController best = null; float bd = float.MaxValue;
            foreach (var p in W.Players) { if (p.Down) continue; float d = BodyDistance(p.transform.position); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        // ---------------- build (stand-in) ----------------
        protected override void Build()
        {
            rng = new System.Random(20261021);
            var brass = new Color(0.66f, 0.48f, 0.22f); var navy = new Color(0.12f, 0.16f, 0.30f); var ivory = new Color(0.95f, 0.92f, 0.82f);
            var iron = new Color(0.22f, 0.22f, 0.25f);
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            // tracked plinth: the stadium collider's footprint (5.2 x 2.2 m)
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 0.45f, 0), new Vector3(2 * Seg, 0.9f, 2 * Radius), navy);
            for (int s = -1; s <= 1; s += 2)
                Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(s * Seg, 0.45f, 0), new Vector3(2 * Radius, 0.45f, 2 * Radius), iron);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 0.95f, 0), new Vector3(2 * Seg + 0.8f, 0.12f, 2 * Radius - 0.2f), brass);
            sunTower = MakeTower(true, brass, navy, ivory, out sunFace);
            moonTower = MakeTower(false, brass, navy, ivory, out moonFace);
            // rocker bridge and the gold ratchet between the towers
            bridge = Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 3.0f, 0), new Vector3(2 * TowerX - 1.2f, 0.25f, 0.5f), brass);
            ratchet = new GameObject("Ratchet").transform; ratchet.SetParent(body, false); ratchet.localPosition = new Vector3(0, 1.9f, 0.55f);
            Prim.Make(PrimitiveKind.Cylinder, ratchet, Vector3.zero, new Vector3(1.5f, 0.08f, 1.5f), brass, 0.3f, Quaternion.Euler(90, 0, 0));
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36 * Mathf.Deg2Rad;
                Prim.Make(PrimitiveKind.Cube, ratchet, new Vector3(Mathf.Cos(a) * 0.8f, Mathf.Sin(a) * 0.8f, 0), new Vector3(0.18f, 0.18f, 0.14f), brass, 0.3f, Quaternion.Euler(0, 0, i * 36));
            }
            core = Prim.Make(PrimitiveKind.Sphere, ratchet, new Vector3(0, 0, 0.08f), Vector3.one * 0.45f, new Color(1f, 0.8f, 0.35f), 4f).GetComponent<Renderer>();
            // pawls at either side of the ratchet, just past the front edge (spec §2.2, model correction M1)
            sunPawl = MakePawl(true, brass); moonPawl = MakePawl(false, brass);
        }

        Transform MakeTower(bool sun, Color brass, Color navy, Color ivory, out Renderer face)
        {
            var t = new GameObject(sun ? "SunTower" : "MoonTower").transform; t.SetParent(body, false);
            t.localPosition = new Vector3(sun ? -TowerX : TowerX, 0.9f, 0);
            Prim.Make(PrimitiveKind.Cube, t, new Vector3(0, 1.45f, 0), new Vector3(1.3f, 2.9f, 1.3f), navy);
            Prim.Make(PrimitiveKind.Cube, t, new Vector3(0, 2.95f, 0), new Vector3(1.4f, 0.15f, 1.4f), brass);
            Prim.Make(PrimitiveKind.Cone, t, new Vector3(0, 3.25f, 0), new Vector3(0.7f, 0.45f, 0.7f), brass);
            var plate = Prim.Make(PrimitiveKind.Cube, t, new Vector3(0, 1.9f, 0.68f), new Vector3(0.75f, 1.1f, 0.06f), ivory, 0.2f);
            face = plate.GetComponent<Renderer>();
            // face symbol: a gold sun disc, or a pale crescent (a disc with a navy bite)
            if (sun) Prim.Make(PrimitiveKind.Sphere, t, new Vector3(0, 1.95f, 0.72f), new Vector3(0.36f, 0.36f, 0.06f), new Color(1f, 0.75f, 0.3f), 2f);
            else
            {
                Prim.Make(PrimitiveKind.Sphere, t, new Vector3(0, 1.95f, 0.72f), new Vector3(0.36f, 0.36f, 0.06f), new Color(0.85f, 0.85f, 0.95f), 1.2f);
                Prim.Make(PrimitiveKind.Sphere, t, new Vector3(0.1f, 2.0f, 0.735f), new Vector3(0.3f, 0.3f, 0.06f), ivory, 0.2f);
            }
            return t;
        }

        Transform MakePawl(bool sunSide, Color brass)
        {
            var t = new GameObject(sunSide ? "SunPawl" : "MoonPawl").transform; t.SetParent(body, false);
            t.localPosition = new Vector3(sunSide ? -P1Pawl.x : P1Pawl.x, 0.9f, P1Pawl.z);
            Prim.Make(PrimitiveKind.Cube, t, new Vector3(0, 0.25f, 0), new Vector3(0.22f, 0.5f, 0.22f), brass, 0.4f);
            Prim.Make(PrimitiveKind.Cone, t, new Vector3(0, 0.6f, 0), new Vector3(0.3f, 0.25f, 0.3f), brass, 0.4f);
            return t;
        }

        // ---------------- schedule (spec §4) ----------------
        protected override IEnumerable<int> Script()
        {
            if (Phase == 1)
            {
                PhaseName = Phase1;
                foreach (var w in Verdict(true, VerdictRecovery1)) yield return w;
                foreach (var w in GapThenMaybeShift()) yield return w; if (Phase != 1) yield break;
                foreach (var w in Rebuttal()) yield return w;
                foreach (var w in GapThenMaybeShift()) yield return w; if (Phase != 1) yield break;
                foreach (var w in CommonMeasure()) yield return w;
                foreach (var w in GapThenMaybeShift()) yield return w;
                yield break;
            }
            foreach (var w in Verdict(PickSender(), VerdictRecovery2)) yield return w;
            foreach (var w in GapThenMaybeShift()) yield return w;
            foreach (var w in Deadbeat()) yield return w;
            foreach (var w in GapThenMaybeShift()) yield return w;
            foreach (var w in CommonMeasure()) yield return w;
            foreach (var w in GapThenMaybeShift()) yield return w;
            foreach (var w in Verdict(PickSender(), VerdictRecovery2)) yield return w;
            foreach (var w in GapThenMaybeShift()) yield return w;
            foreach (var w in RebuttalPair()) yield return w;
            foreach (var w in GapThenMaybeShift()) yield return w;
        }

        // Alternating Verdict's sender: seeded, never the same tower three times running (§4.3)
        bool PickSender()
        {
            bool sun = rng.Next(2) == 0;
            if (sameSender >= 2 && sun == LastSenderSun) sun = !sun;
            return sun;
        }

        // 1 s gap: no warning, turn toward the standing players' centroid at 40 deg/s; then the queued 50 % transition
        IEnumerable<int> GapThenMaybeShift()
        {
            Current = "gap";
            for (int i = 0; i < Gap; i++)
            {
                var c = Vector3.zero; int n = 0;
                foreach (var p in W.Players) if (!p.Down) { c += p.transform.position; n++; }
                if (n > 0) TurnTowards(Sim.YawTo(transform.position, c / n, transform.eulerAngles.y), TurnRate);
                yield return 1;
            }
            if (Phase == 1 && Hp <= MaxHp / 2)
            {
                Current = "transition"; Phase = 2; SetPhase(Phase2);
                glowTarget = 2.2f;
                yield return TransitionTicks;   // towers tilt apart, bridge extends, pawls unfold (Update); harmless
                glowTarget = 1;
            }
        }

        IEnumerable<int> Recover(int ticks, string what = "recovery")
        {
            Current = what; RecoveryTicks = ticks; glowTarget = 0.5f;
            yield return ticks;
            glowTarget = 1;
        }

        Hazard NoKnock(Hazard h) { h.Knockback = 0; h.Stun = HitStun; return h; }

        // resolve flash along the floor part of a lane (presentation only)
        void LaneBurst(Hazard hz)
        {
            var u = Sim.Dir(hz.Yaw);
            for (float t = 0.5f; t < hz.Length; t += 1.2f)
            {
                var p = hz.Origin + u * t;
                if (BodyDistance(p) > 0) W.BossFx.ChimeResolve(p, hz.Width / 2);
            }
        }

        // Sun Verdict (Phase 1) / Alternating Verdict (Phase 2)
        IEnumerable<int> Verdict(bool sun, int recovery)
        {
            string name = Phase == 1 ? "Sun Verdict" : sun ? "Alternating Verdict (Sun)" : "Alternating Verdict (Moon)";
            Current = name;
            sameSender = VerdictsCast > 0 && sun == LastSenderSun ? sameSender + 1 : 1;
            LastSenderSun = sun; VerdictsCast++;
            var target = RotatingTarget();
            var from = Tower(sun);
            var h = NoKnock(new Hazard
            {
                Source = name, Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Width = VerdictWidth,
                Length = 4.4f + VerdictReach, LockAt = VerdictTrack, ResolveAt = VerdictResolve, Damage = VerdictDmg,
            });
            void Aim(Hazard hz)
            {
                var at = target && !target.Down ? target.transform.position : transform.position + transform.forward * 5;
                hz.Yaw = Sim.YawTo(hz.Origin, at, hz.Yaw);
                var u = Sim.Dir(hz.Yaw);
                hz.Length = Mathf.Min(4.4f + VerdictReach, Mathf.Min(ExitDistance(hz.Origin, u) + VerdictReach, ToEdge(hz.Origin, u, CombatWorld.PlayerBound + 0.5f)));
            }
            Aim(h); h.Track = Aim;
            bool jammed = false;
            h.OnResolve = hz =>
            {
                LaneBurst(hz);
                jammed = CrossesPawl(hz.Origin, hz.Yaw, Pawl(!sun), PawlRadius);
                if (jammed)
                {
                    SelfStrikes++;
                    var at = Pawl(!sun) + Vector3.up * 1.1f;
                    W.BossFx.BossHit(at); W.BossFx.ChimeResolve(Pawl(!sun), 0.6f);
                    RunHud.Float(at + Vector3.up * 0.8f, "JAMMED", new Color(1f, 0.9f, 0.5f));
                }
                Release();
            };
            W.Hazards.Spawn(h);
            for (int i = 0; i < VerdictResolve; i++)
            {
                if (sun) { sunLift = 1; sunPawlUp = 1; } else { moonLift = 1; moonPawlUp = 1; }
                Windup(i / (float)VerdictResolve); yield return 1;
            }
            sunLift = moonLift = 0; sunPawlUp = moonPawlUp = 0;
            LastVerdictJammed = jammed;
            yield return VerdictActive;
            if (jammed) jam = 1;
            foreach (var w in Recover(jammed ? JamRecovery : recovery, jammed ? "jammed" : "recovery")) yield return w;
            jam = 0;
        }

        Hazard RebuttalCone(bool sun, float half, int resolve, string name, Vector3 at)
        {
            var from = Tower(sun);
            return W.Hazards.Spawn(NoKnock(new Hazard
            {
                Source = name, Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = from, Yaw = Sim.YawTo(from, at, transform.eulerAngles.y),
                Radius = RebuttalRadius, HalfAngle = half, LockAt = 0, ResolveAt = resolve, Damage = RebuttalDmg,
                OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); },
            }));
        }

        // Moon Rebuttal: 100 deg cone from the Moon tower at the nearest player, fixed at warning start
        IEnumerable<int> Rebuttal()
        {
            Current = "Moon Rebuttal";
            var t = Nearest(); var at = t ? t.transform.position : transform.position + transform.forward * 4;
            RebuttalCone(false, RebuttalHalf, RebuttalResolve, "Moon Rebuttal", at);
            for (int i = 0; i < RebuttalResolve; i++) { moonLift = 1; Windup(i / (float)RebuttalResolve); yield return 1; }
            moonLift = 0;
            yield return RebuttalActive;
            foreach (var w in Recover(RebuttalRecovery)) yield return w;
        }

        // Rebuttal Pair: cone 1 from the tower that didn't send the last Verdict; cone 2 (80 deg) from the other tower is
        // an outline-only preview until cone 1 is spent, then a full 90-tick warning. Both aim where the nearest player
        // stood at the start (spec §3.1).
        IEnumerable<int> RebuttalPair()
        {
            Current = "Rebuttal Pair";
            bool first = !LastSenderSun;   // true = Sun first
            var t = Nearest(); var at = t ? t.transform.position : transform.position + transform.forward * 4;
            RebuttalCone(first, RebuttalHalf, RebuttalResolve, "Rebuttal Pair (1)", at);
            var from2 = Tower(!first); float yaw2 = Sim.YawTo(from2, at, transform.eulerAngles.y);
            var preview = W.BossFx.Cone(RebuttalRadius, PairHalf); preview.Memory(); preview.Place(from2, yaw2);
            for (int i = 0; i < RebuttalResolve; i++) { if (first) sunLift = 1; else moonLift = 1; Windup(i / (float)RebuttalResolve); yield return 1; }
            sunLift = moonLift = 0;
            yield return RebuttalActive;
            preview.Dismiss();
            if (Dead) yield break;
            W.Hazards.Spawn(NoKnock(new Hazard
            {
                Source = "Rebuttal Pair (2)", Shape = HazardShape.Cone, Kind = HazardKind.Strike, Origin = from2, Yaw = yaw2,
                Radius = RebuttalRadius, HalfAngle = PairHalf, LockAt = 0, ResolveAt = PairSecond, Damage = RebuttalDmg,
                OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); },
            }));
            for (int i = 0; i < PairSecond; i++) { if (first) moonLift = 1; else sunLift = 1; Windup(i / (float)PairSecond); yield return 1; }
            sunLift = moonLift = 0;
            yield return RebuttalActive;
            foreach (var w in Recover(PairRecovery)) yield return w;
        }

        // Common Measure: stadium band 2.5-5.5 m out from the body edge; both pawls close, chord rings
        IEnumerable<int> CommonMeasure()
        {
            Current = "Common Measure";
            W.Hazards.Spawn(NoKnock(new Hazard
            {
                Source = "Common Measure", Shape = HazardShape.Stadium, Kind = HazardKind.Ground, Origin = transform.position,
                Yaw = transform.eulerAngles.y, Segment = Seg, Inner = Radius + RingInner, Radius = Radius + RingOuter,
                LockAt = 0, ResolveAt = RingResolve, Damage = RingDmg,
                OnResolve = hz =>
                {
                    for (int s = -1; s <= 1; s += 2) W.BossFx.TollResolve(transform.TransformPoint(new Vector3(s * Seg, 0, 0)), hz.Inner, hz.Radius);
                    Release();
                },
            }));
            for (int i = 0; i < RingResolve; i++) { sunPawlUp = moonPawlUp = -1; Windup(i / (float)RingResolve); yield return 1; }
            sunPawlUp = moonPawlUp = 0;
            yield return RingActive;
            foreach (var w in Recover(RingRecovery)) yield return w;
        }

        // Deadbeat: the ratchet stops, two parallel lanes straight ahead from the towers, 2.6 m corridor between
        IEnumerable<int> Deadbeat()
        {
            Current = "Deadbeat";
            float yaw = transform.eulerAngles.y;
            for (int s = -1; s <= 1; s += 2)
            {
                var from = transform.TransformPoint(new Vector3(s * (DeadbeatInner + VerdictWidth / 2), 0, Radius));
                W.Hazards.Spawn(NoKnock(new Hazard
                {
                    Source = "Deadbeat", Shape = HazardShape.Lane, Kind = HazardKind.Strike, Origin = from, Yaw = yaw,
                    Width = VerdictWidth, Length = Mathf.Min(DeadbeatReach, ToEdge(from, Sim.Dir(yaw), CombatWorld.PlayerBound + 0.5f)),
                    LockAt = 0, ResolveAt = DeadbeatResolve, Damage = DeadbeatDmg,
                    OnResolve = hz => { LaneBurst(hz); Release(); },
                }));
            }
            for (int i = 0; i < DeadbeatResolve; i++) { spin = 0; Windup(i / (float)DeadbeatResolve); glowTarget = 0.3f; yield return 1; }
            yield return DeadbeatActive;
            spin = 60;
            foreach (var w in Recover(DeadbeatRecovery)) yield return w;
        }

        // ---------------- presentation ----------------
        protected override void Update()
        {
            base.Update();
            if (!body) return;
            float k = Time.deltaTime * 8;
            tilt = Mathf.MoveTowards(tilt, Phase == 2 && !Dead ? 1 : 0, Time.deltaTime / 2f);
            if (sunTower) sunTower.localRotation = Quaternion.Slerp(sunTower.localRotation, Quaternion.Euler(0, 0, 9 * tilt), k);
            if (moonTower) moonTower.localRotation = Quaternion.Slerp(moonTower.localRotation, Quaternion.Euler(0, 0, -9 * tilt), k);
            if (bridge) bridge.localScale = new Vector3(2 * TowerX - 1.2f + 0.8f * tilt, 0.25f, 0.5f);
            Pose(sunPawl, true, sunPawlUp); Pose(moonPawl, false, moonPawlUp);
            if (sunFace) Prim.SetGlow(sunFace, new Color(1f, 0.8f, 0.45f) * (0.2f + 2.2f * sunLift * (0.6f + 0.4f * Mathf.Sin(Time.time * 18))));
            if (moonFace) Prim.SetGlow(moonFace, new Color(0.85f, 0.85f, 1f) * (0.2f + 2.2f * moonLift * (0.6f + 0.4f * Mathf.Sin(Time.time * 18))));
            if (ratchet)
            {
                float s = Dead ? 0 : jam > 0 ? 0 : spin;
                ratchet.localRotation *= Quaternion.Euler(0, 0, -s * Time.deltaTime);
                if (jam > 0) ratchet.localPosition = new Vector3(0.04f * Mathf.Sin(Time.time * 60), 1.9f, 0.55f);
                else ratchet.localPosition = new Vector3(0, 1.9f, 0.55f);
            }
            if (core) Prim.SetGlow(core, new Color(1f, 0.8f, 0.35f) * 1.4f * glowLevel);
            if (Dead) { tilt = 0; body.localPosition = Vector3.MoveTowards(body.localPosition, new Vector3(0, -0.3f, 0), Time.deltaTime * 0.4f); }
        }
        // pawls: Phase 1 short folded catches, Phase 2 larger and further out; raised on a Verdict, closed on Common Measure
        void Pose(Transform pawl, bool sunSide, float up)
        {
            if (!pawl) return;
            var p = Vector3.Lerp(P1Pawl, P2Pawl, tilt);
            pawl.localPosition = new Vector3(sunSide ? -p.x : p.x, 0.9f + 0.25f * Mathf.Max(0, up), p.z);
            pawl.localScale = Vector3.one * Mathf.Lerp(1f, 1.35f, tilt);
            pawl.localRotation = Quaternion.Slerp(pawl.localRotation, Quaternion.Euler(up < 0 ? -40 : 0, 0, (sunSide ? 1 : -1) * 20 * Mathf.Max(0, up)), Time.deltaTime * 10);
        }
        protected override void OnDefeat() { glowTarget = 0.2f; spin = 0; jam = 0; }
    }
}
