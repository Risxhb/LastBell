using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Falling Hours (Risxhb, October 28, 2026): below 10% HP, orbs drop from the sky onto the floor, more and more often
    // the longer the fight goes on. Each landing orb spews 3 orbs that roll out along the floor, bounce off the arena wall
    // 3 times and dissipate when they reach it a fourth time. It runs alongside every attack, in both tempos, until the
    // boss dies. This is a deliberate soft enrage, allowed for the Tempo Tyrant only (docs/design/content-rules.md).
    //   drop      a 1.5 m circle where it will land (Ground), the orb falling into it; 5/4 beats; 18 damage
    //   bouncer   a 0.6 m orb rolling at 6 m/s (walking speed) (Strike, not jumpable); 10 damage; each hits a player once
    //   density   0.4 drops/s at the start, +0.02/s every second (+1 drop/s every 50 s), capped at 3/s;
    //             every 3rd drop lands on a standing player (in turn), the rest anywhere on the floor
    public sealed partial class TempoTyrantEncounter
    {
        public const float OrbsAt = 0.1f;
        public static readonly (float H, float N) OrbFallWarn = (5, 4);
        public const float OrbImpactRadius = 1.5f, OrbFallHeight = 16f;
        public const int OrbImpactDmg = 18, OrbSplit = 3, OrbBounces = 3, OrbTargetEvery = 3;
        public const float BouncerRadius = 0.6f, BouncerSpeed = 6f;
        public const int BouncerDmg = 10;
        public const float OrbRateStart = 0.4f, OrbRateRamp = 0.02f, OrbRateMax = 3f;
        public const int OrbTellTicks = 60;

        sealed class Drop { public Hazard H; public Transform Orb; public int Warn; }
        sealed class Bouncer { public Vector3 P, V; public int Bounces; public Hazard H; public Transform Orb; }

        public bool OrbsOn { get; private set; }
        public int OrbsStartTick { get; private set; } = -1;
        public int Drops { get; private set; }
        public int DropsLanded { get; private set; }
        public int BouncersSpawned { get; private set; }
        public float MaxBouncerRadius { get; private set; }
        public float OrbRate { get; private set; }
        public readonly List<int> DropTicks = new List<int>();          // tick each drop appeared
        public readonly List<int> BouncerBounceLog = new List<int>();   // bounces of each orb when it dissipated
        public int LiveOrbCount => drops.Count + bouncers.Count;

        readonly List<Drop> drops = new List<Drop>();
        readonly List<Bouncer> bouncers = new List<Bouncer>();
        readonly Stack<Transform> orbPool = new Stack<Transform>();
        Transform orbRoot; System.Random orbRng; float orbAcc; int orbTarget;
        static readonly Color OrbGlow = new Color(1.0f, 0.5f, 0.12f);   // amber, below bloom white-out: the floor circle stays readable

        // at a block boundary, once, strictly below 10%: a short non-damaging tell, then the orbs start
        IEnumerable<int> OrbTell()
        {
            Current = "Falling Hours (tell)"; Announcement = "THE HOURS FALL";
            Fx.Rim(transform.position, BodyRadius + 1.8f, RimAmber, 0.45f);
            Log("falling hours");
            for (int i = 0; i < OrbTellTicks; i++) { Windup(0.5f * i / OrbTellTicks); yield return 1; }
            Release(0.6f); Announcement = "";
            OrbsOn = true; OrbsStartTick = W.Tick; orbRng = new System.Random(1028); orbAcc = 0.999f; orbTarget = 0;
        }

        // every combat tick while the orbs are on (from StepAlways, so pause freezes them)
        void StepOrbs()
        {
            if (!OrbsOn || Dead) return;
            float t = (W.Tick - OrbsStartTick) / (float)Sim.Hz;
            OrbRate = Mathf.Min(OrbRateMax, OrbRateStart + OrbRateRamp * t);
            orbAcc += OrbRate * Sim.Dt;
            while (orbAcc >= 1) { orbAcc -= 1; SpawnDrop(); }
            foreach (var d in drops)
            {
                float k = Mathf.Clamp01(d.H.Age / (float)d.Warn);
                d.Orb.position = d.H.Origin + Vector3.up * (OrbFallHeight * (1 - k * k) + BouncerRadius);   // accelerating fall
            }
            float bound = CombatWorld.PlayerBound;
            for (int i = bouncers.Count - 1; i >= 0; i--)
            {
                var b = bouncers[i];
                b.P += b.V * Sim.Dt;
                var flat = new Vector2(b.P.x, b.P.z);
                if (flat.magnitude >= bound)
                {
                    if (b.Bounces >= OrbBounces) { Dissipate(i); continue; }   // the fourth wall contact: gone
                    var n = new Vector3(flat.x, 0, flat.y) / flat.magnitude;
                    b.V -= 2 * Vector3.Dot(b.V, n) * n;
                    b.P = n * (bound - 0.01f);
                    b.Bounces++;
                    Fx.Rim(b.P, BouncerRadius + 0.2f, RimAmber * 0.5f, 0.15f);
                }
                MaxBouncerRadius = Mathf.Max(MaxBouncerRadius, new Vector2(b.P.x, b.P.z).magnitude);
                b.H.Origin = b.P; b.Orb.position = b.P + Vector3.up * BouncerRadius * 0.75f;
                foreach (var p in W.Players)
                {
                    if (p.Down || b.H.Hit.Contains(p) || !b.H.Contains(p.transform.position)) continue;
                    b.H.Hit.Add(p); p.ReceiveHit(b.H);
                }
            }
        }

        void SpawnDrop()
        {
            Vector3 at;
            var standing = W.Players.FindAll(p => !p.Down);
            if (++Drops % OrbTargetEvery == 0 && standing.Count > 0) at = Sim.Flat(standing[orbTarget++ % standing.Count].transform.position);
            else
            {
                float r = Mathf.Sqrt((float)orbRng.NextDouble()) * (CombatWorld.PlayerBound - 1f), a = (float)orbRng.NextDouble() * 360f;
                at = Sim.Dir(a) * r;
            }
            DropTicks.Add(W.Tick);
            int warn = Beats(OrbFallWarn);
            var h = new Hazard { Source = "Falling Hours (orb)", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = OrbImpactRadius,
                LockAt = 0, ResolveAt = warn, Damage = OrbImpactDmg, Knockback = 1f, ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius };
            var d = new Drop { H = h, Warn = warn, Orb = TakeOrb(at + Vector3.up * (OrbFallHeight + BouncerRadius), 1.0f) };
            h.OnResolve = hz => Land(d);
            Live(h, true, 0.35f);
            drops.Add(d);
        }

        void Land(Drop d)
        {
            drops.Remove(d); ReleaseOrb(d.Orb); DropsLanded++;
            Fx.Rim(d.H.Origin, OrbImpactRadius, RimAmber, 0.25f);
            float yaw0 = (float)orbRng.NextDouble() * 360f;
            for (int k = 0; k < OrbSplit; k++)
            {
                var v = Sim.Dir(yaw0 + k * 360f / OrbSplit) * BouncerSpeed;
                var h = new Hazard { Source = "Falling Hours (bouncing orb)", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = d.H.Origin, Radius = BouncerRadius,
                    LockAt = 0, ResolveAt = 60 * Sim.Hz, Damage = BouncerDmg, Knockback = 0.5f, HitOnResolve = false,   // not jumpable (Risxhb): touching one hits
                    ClipRadius = CombatWorld.PlayerBound + PlayerController.Radius };
                Live(h, true, 0.2f);
                bouncers.Add(new Bouncer { P = d.H.Origin, V = v, H = h, Orb = TakeOrb(d.H.Origin + Vector3.up * BouncerRadius, BouncerRadius * 1.5f) });
                BouncersSpawned++;
            }
        }

        void Dissipate(int i)
        {
            var b = bouncers[i]; bouncers.RemoveAt(i);
            BouncerBounceLog.Add(b.Bounces);
            W.Hazards.Cancel(b.H); ReleaseOrb(b.Orb);
            Fx.Rim(b.P, BouncerRadius + 0.3f, RimIvory * 0.5f, 0.2f);
        }

        void ClearOrbs()
        {
            foreach (var d in drops) ReleaseOrb(d.Orb);
            foreach (var b in bouncers) ReleaseOrb(b.Orb);
            drops.Clear(); bouncers.Clear(); OrbsOn = false;
        }

        // pooled glowing spheres (shared cached material and mesh: nothing accumulates across attempts)
        Transform TakeOrb(Vector3 at, float size)
        {
            if (!orbRoot) { orbRoot = new GameObject("Falling Hours orbs").transform; orbRoot.SetParent(transform.parent, false); }
            Transform t;
            if (orbPool.Count > 0) t = orbPool.Pop();
            else
            {
                var go = new GameObject("Falling orb"); go.transform.SetParent(orbRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(PrimitiveKind.Sphere);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = TempoTyrantRig.Mat(OrbGlow, true);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                SimSmoother.Attach(go);
                t = go.transform;
            }
            t.localScale = Vector3.one * size; t.position = at; t.gameObject.SetActive(true);
            return t;
        }
        void ReleaseOrb(Transform t) { if (!t) return; t.gameObject.SetActive(false); orbPool.Push(t); }

        void OnDestroy() { if (orbRoot) Destroy(orbRoot.gameObject); }
    }
}
