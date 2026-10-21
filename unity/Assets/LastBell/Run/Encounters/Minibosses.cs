using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // The three miniboss slots from the single-arena plan (§3), as labelled STAND-INS: code-built silhouettes and
    // first-pass schedules that teach each lesson. Names, timings, damage and HP are provisional; the real encounters are
    // encounter-lane content (#8) still to be authored. Each uses only the shared hazard shapes.

    // 1 — Dial Warden: Hand Sweep (locked cone) and Hollow Dial (ring with inner/outer safety). Slow pivot, central.
    public sealed class DialWarden : BossEncounter
    {
        public override string Id => "miniboss.dial_warden";
        public override string DisplayName => "The Dial Warden";
        public override string Lesson => "Read its facing, leave the cone, choose the ring's safe side";
        const int SweepLock = 18, SweepResolve = 72, DialResolve = 78, SweepDmg = 20, DialDmg = 18;
        Transform hand, dial, body; Renderer dialR; float handAngle, handTarget; int loops;

        void Awake() { MaxHp = 1500; BodyRadius = 1.4f; HitHeight = 1.2f; MarkHeight = 3.2f; }

        protected override void Build()
        {
            var brass = new Color(0.62f, 0.45f, 0.22f); var navy = new Color(0.12f, 0.16f, 0.3f);
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 0.8f, 0), new Vector3(2.6f, 0.8f, 2.6f), navy);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 1.75f, 0), new Vector3(2.2f, 0.15f, 2.2f), brass);
            dial = Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 1.92f, 0), new Vector3(1.9f, 0.04f, 1.9f), new Color(0.95f, 0.9f, 0.75f), 0.6f);
            dialR = dial.GetComponent<Renderer>();
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30 * Mathf.Deg2Rad;
                Prim.Make(PrimitiveKind.Cube, body, new Vector3(Mathf.Sin(a) * 0.82f, 1.97f, Mathf.Cos(a) * 0.82f), new Vector3(0.08f, 0.04f, 0.22f), navy, 0, Quaternion.Euler(0, i * 30, 0));
            }
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 2.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), brass);
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 2.35f, 0.25f), new Vector3(0.18f, 0.18f, 0.18f), new Color(1f, 0.8f, 0.35f), 4f);   // eye = facing
            // the clock hand: the weapon, pointing where the sweep goes
            hand = new GameObject("Hand").transform; hand.SetParent(body, false); hand.localPosition = new Vector3(0, 2.0f, 0);
            Prim.Make(PrimitiveKind.Cube, hand, new Vector3(0, 0, 1.9f), new Vector3(0.28f, 0.12f, 3.4f), brass, 0.4f);
            Prim.Make(PrimitiveKind.Cone, hand, new Vector3(0, 0, 3.75f), new Vector3(0.55f, 0.12f, 0.55f), brass, 0.6f, Quaternion.Euler(90, 0, 0));
        }

        protected override IEnumerable<int> Script()
        {
            if (loops == 0) PhaseName = "Teaching";
            foreach (var w in Approach(4.5f, 1.6f)) yield return w;
            foreach (var w in HandSweep()) yield return w;
            yield return loops == 0 ? 50 : 20;
            foreach (var w in Approach(4f, 1.6f, 90)) yield return w;
            foreach (var w in HollowDial()) yield return w;
            yield return loops == 0 ? 50 : 20;
            if (loops > 0)
            {
                // alternating sequence once both have been shown
                foreach (var w in HandSweep()) yield return w;
                foreach (var w in HollowDial()) yield return w;
            }
            Current = "recovery"; handTarget = 0; glowTarget = 0.4f;
            yield return 75;
            glowTarget = 1; loops++; PhaseName = "Alternating";
        }

        IEnumerable<int> HandSweep()
        {
            Current = "Hand Sweep";
            var h = Cone("Hand Sweep", 6f, 40f, SweepLock, SweepResolve, SweepDmg);
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); handTarget = -60; Release(); };
            for (int i = 0; i < SweepResolve; i++) { handTarget = Mathf.Lerp(0, 55, i / (float)SweepResolve); Windup(i / (float)SweepResolve); yield return 1; }
            yield return 12; handTarget = 0;
        }

        IEnumerable<int> HollowDial()
        {
            Current = "Hollow Dial";
            var h = Ring("Hollow Dial", transform.position, 2.4f, 6f, DialResolve, DialDmg);
            h.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, hz.Radius); Release(); };
            for (int i = 0; i < DialResolve; i++) { Windup(i / (float)DialResolve); handAngle += 6; yield return 1; }
            yield return 6;
        }

        protected override void Update()
        {
            base.Update();
            if (hand) hand.localRotation = Quaternion.Euler(0, Mathf.LerpAngle(hand.localEulerAngles.y, handTarget + handAngle * 0, Time.deltaTime * 12), 0);
            if (dialR) Prim.SetGlow(dialR, new Color(1f, 0.85f, 0.5f) * 0.6f * glowLevel);
            if (Dead && body) { body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(35, 0, 0), Time.deltaTime * 2); body.localPosition = Vector3.MoveTowards(body.localPosition, new Vector3(0, -1.2f, 0), Time.deltaTime * 0.8f); }
        }
        protected override void OnDefeat() { glowTarget = 0.1f; Current = "defeated"; }
    }

    // 2 — Pendulum Sentinel: Pendulum Charge (locked lane, real repositioning) and Low Pendulum (a low bar you can JUMP
    // or step around). Tall swinging silhouette, recovery after a charge.
    public sealed class PendulumSentinel : BossEncounter
    {
        public override string Id => "miniboss.pendulum_sentinel";
        public override string DisplayName => "The Pendulum Sentinel";
        public override string Lesson => "Step sideways out of the charge; jump the low pendulum or go around it";
        const int ChargeResolve = 48, ChargeTravel = 18, BarResolve = 72, ChargeDmg = 22, BarDmg = 18;
        Transform arm, bob, body; float swing, swingTarget; int loops;
        Vector3 barFrom, barDir; float barLen; int barSweep = -1;

        void Awake() { MaxHp = 1700; BodyRadius = 1.1f; HitHeight = 1.8f; MarkHeight = 4.6f; }

        protected override void Build()
        {
            var steel = new Color(0.3f, 0.34f, 0.45f); var brass = new Color(0.66f, 0.5f, 0.24f);
            body = new GameObject("Body").transform; body.SetParent(transform, false);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 0.25f, 0), new Vector3(2.0f, 0.25f, 2.0f), steel);
            Prim.Make(PrimitiveKind.Cylinder, body, new Vector3(0, 2.0f, 0), new Vector3(1.0f, 1.6f, 1.0f), steel);
            Prim.Make(PrimitiveKind.Cube, body, new Vector3(0, 3.7f, 0), new Vector3(2.4f, 0.3f, 0.5f), brass);
            Prim.Make(PrimitiveKind.Sphere, body, new Vector3(0, 3.0f, 0.45f), new Vector3(0.3f, 0.3f, 0.3f), new Color(0.6f, 1f, 1.1f), 4f);   // eye = facing
            arm = new GameObject("Pendulum").transform; arm.SetParent(body, false); arm.localPosition = new Vector3(0, 3.7f, 0.4f);
            Prim.Make(PrimitiveKind.Cube, arm, new Vector3(0, -1.5f, 0), new Vector3(0.1f, 3f, 0.1f), brass);
            bob = Prim.Make(PrimitiveKind.Sphere, arm, new Vector3(0, -3.1f, 0), new Vector3(0.8f, 0.8f, 0.3f), brass, 0.5f);
        }

        protected override IEnumerable<int> Script()
        {
            if (loops == 0) PhaseName = "Teaching";
            foreach (var w in Charge()) yield return w;
            Current = "recovery"; glowTarget = 0.4f; swingTarget = 0;
            yield return loops == 0 ? 80 : 50;
            glowTarget = 1;
            foreach (var w in LowPendulum()) yield return w;
            yield return loops == 0 ? 60 : 30;
            if (loops > 0) { foreach (var w in Charge()) yield return w; foreach (var w in LowPendulum()) yield return w; }
            foreach (var w in WalkTo(Vector3.zero, 2.5f, 120)) yield return w;
            Current = "recovery"; glowTarget = 0.4f; yield return 60; glowTarget = 1;
            loops++; PhaseName = "Alternating";
        }

        IEnumerable<int> Charge()
        {
            Current = "Pendulum Charge";
            var (start, end, yaw, len) = ChargeLane(2.75f);
            var h = Lane("Pendulum Charge", start, yaw, len, 2.2f, ChargeResolve, ChargeDmg);
            h.OnResolve = hz => { W.BossFx.ChargeTrail(transform, true); Release(); };
            for (int i = 0; i < ChargeResolve; i++) { TurnTowards(yaw, 480); swingTarget = -50 * i / ChargeResolve; Windup(i / (float)ChargeResolve); yield return 1; }
            if (Dead) yield break;
            for (int i = 1; i <= ChargeTravel; i++) { transform.position = Vector3.Lerp(start, end, i / (float)ChargeTravel); swingTarget = 40; yield return 1; }
            W.BossFx.ChargeTrail(transform, false); W.BossFx.ChargeLand(transform.position);
        }

        IEnumerable<int> LowPendulum()
        {
            Current = "Low Pendulum";
            barDir = Sim.Flat(Target() - transform.position); if (barDir.sqrMagnitude < 0.1f) barDir = transform.forward; barDir.Normalize();
            barFrom = transform.position + barDir * (BodyRadius + 0.2f);
            barLen = Mathf.Max(3f, ToEdge(barFrom, barDir, CombatWorld.PlayerBound + 0.5f));
            var h = Lane("Low Pendulum", barFrom, Sim.YawTo(barFrom, barFrom + barDir, 0), barLen, 1.4f, BarResolve, BarDmg, jumpClear: true);
            h.Stun = 12; h.Knockback = 1.0f;
            h.OnResolve = hz => { barSweep = 0; Release(0.7f); };
            for (int i = 0; i < BarResolve; i++) { TurnTowards(Sim.YawTo(transform.position, barFrom + barDir, 0), 360); swingTarget = 70 * i / BarResolve; Windup(i / (float)BarResolve); yield return 1; }
            yield return 14;
            swingTarget = 0;
        }

        protected override void Update()
        {
            base.Update();
            swing = Mathf.Lerp(swing, swingTarget + 6 * Mathf.Sin(Time.time * 2.2f), Time.deltaTime * 8);
            if (arm) arm.localRotation = Quaternion.Euler(swing, 0, 0);
            if (bob) Prim.SetGlow(bob.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.4f) * 0.3f * glowLevel);
            if (barSweep >= 0)
            {
                // the bob skims along the lane at ankle height when the bar resolves (presentation only)
                barSweep++;
                float u = barSweep / 12f;
                if (u > 1) barSweep = -1;
                else W.BossFx.ChimeResolve(barFrom + barDir * barLen * u, 0.7f);
            }
            if (Dead && body) { body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(80, 0, 0), Time.deltaTime * 1.5f); body.localPosition = Vector3.MoveTowards(body.localPosition, new Vector3(0, -0.6f, 0), Time.deltaTime * 0.5f); }
        }
        protected override void OnDefeat() { glowTarget = 0.1f; W.BossFx.ChargeTrail(transform, false); }
    }

    // 3 — Chimebound Oracle: Shattered Chime (markers that follow you, then lock) and Dividing Chime (a cone after the
    // markers). Small floating bell-cluster silhouette; drifts but never charges.
    public sealed class ChimeboundOracle : BossEncounter
    {
        public override string Id => "miniboss.chimebound_oracle";
        public override string DisplayName => "The Chimebound Oracle";
        public override string Lesson => "Drag the markers somewhere harmless, then leave the cone";
        const int MarkFollow = 30, MarkResolve = 78, MarkGap = 24, MarkDmg = 12, ConeLock = 18, ConeResolve = 69, ConeDmg = 20;
        Transform cluster; readonly List<Transform> bells = new List<Transform>(); int loops; float spin = 1;

        void Awake() { MaxHp = 1700; BodyRadius = 1.2f; HitHeight = 2.0f; MarkHeight = 4.2f; }

        protected override void Build()
        {
            cluster = new GameObject("Cluster").transform; cluster.SetParent(transform, false); cluster.localPosition = new Vector3(0, 2.0f, 0);
            Prim.Make(PrimitiveKind.Sphere, cluster, Vector3.zero, Vector3.one * 1.0f, new Color(0.25f, 0.2f, 0.45f), 0);
            Prim.Make(PrimitiveKind.Sphere, cluster, new Vector3(0, 0.05f, 0.42f), Vector3.one * 0.3f, new Color(1f, 0.85f, 1.2f), 4f);   // facing
            for (int i = 0; i < 5; i++)
            {
                var b = Prim.Make(PrimitiveKind.Cone, cluster, Vector3.zero, new Vector3(0.6f, 0.7f, 0.6f), new Color(0.9f, 0.72f, 0.32f), 0.5f, Quaternion.Euler(180, 0, 0));
                bells.Add(b);
            }
        }

        protected override IEnumerable<int> Script()
        {
            if (loops == 0) PhaseName = "Teaching";
            foreach (var w in Chimes(3)) yield return w;
            yield return loops == 0 ? 60 : 24;
            if (loops == 0)
            {
                foreach (var w in DividingCone()) yield return w;
                yield return 60;
            }
            else
            {
                // the cone warns when the last marker locks
                var c = Chimes(3).GetEnumerator(); int t = 0; bool coneStarted = false; IEnumerator<int> cone = null; int coneWait = 0;
                while (true)
                {
                    bool more = c.MoveNext(); int wv = more ? Mathf.Max(1, c.Current) : 1;
                    for (int k = 0; k < wv; k++)
                    {
                        t++;
                        if (!coneStarted && t >= 2 * MarkGap + MarkFollow) { coneStarted = true; cone = DividingCone().GetEnumerator(); }
                        if (cone != null) { if (coneWait > 0) coneWait--; else if (cone.MoveNext()) coneWait = Mathf.Max(1, cone.Current) - 1; else cone = null; }
                        yield return 1;
                    }
                    if (!more && cone == null) break;
                }
            }
            foreach (var w in WalkTo(Vector3.zero, 2f, 90)) yield return w;
            Current = "recovery"; glowTarget = 0.35f; spin = 0.2f; yield return 70; glowTarget = 1; spin = 1;
            loops++; PhaseName = "Combined";
        }

        IEnumerable<int> Chimes(int count)
        {
            Current = "Shattered Chime";
            for (int i = 0; i < count; i++)
            {
                var h = Marker("Shattered Chime", 1.4f, MarkFollow, MarkResolve, MarkDmg);
                h.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                for (int k = 0; k < (i < count - 1 ? MarkGap : MarkResolve); k++) { Windup(k / (float)MarkResolve); yield return 1; }
            }
            Release(0.6f);
        }

        IEnumerable<int> DividingCone()
        {
            Current = "Dividing Chime";
            var h = Cone("Dividing Chime", 6.5f, 35f, ConeLock, ConeResolve, ConeDmg, true, 300);
            h.OnResolve = hz => { W.BossFx.SweepResolve(hz.Origin, hz.Yaw, hz.Radius, hz.HalfAngle); Release(); };
            for (int i = 0; i < ConeResolve; i++) { Windup(i / (float)ConeResolve); spin = 3; yield return 1; }
            spin = 1; yield return 8;
        }

        protected override void StepAlways()
        {
            // slow drift around the dial centre, keeping some distance from the target
            if (Current == "Shattered Chime" && DistToTarget() < 3.5f)
            {
                var away = Sim.Flat(transform.position - Target()).normalized;
                var p = transform.position + away * 1.2f * Sim.Dt;
                if (new Vector2(p.x, p.z).magnitude <= CombatWorld.BossBound - 1) transform.position = p;
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!cluster) return;
            float t = Time.time;
            cluster.localPosition = new Vector3(0, Dead ? Mathf.MoveTowards(cluster.localPosition.y, 0.4f, Time.deltaTime) : 2.0f + 0.2f * Mathf.Sin(t * 1.4f), 0);
            for (int i = 0; i < bells.Count; i++)
            {
                float a = t * spin * (Dead ? 0 : 1) + i * Mathf.PI * 2 / bells.Count;
                bells[i].localPosition = new Vector3(Mathf.Cos(a) * 1.25f, 0.3f * Mathf.Sin(a * 2 + i), Mathf.Sin(a) * 1.25f);
                Prim.SetGlow(bells[i].GetComponent<Renderer>(), new Color(1f, 0.75f, 0.35f) * 0.5f * glowLevel);
            }
        }
        protected override void OnDefeat() { glowTarget = 0.05f; spin = 0; }
    }
}
