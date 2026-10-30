using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 06 Zenith Collapse (aurelion.zenith_collapse), all difficulties, spec abilities-v2/ABILITIES-v2.1.md §1 (proposal).
    // The Regent stands at the meridian (the phase plan walks him to the arena centre first) and the whole sky falls on
    // the dais except a disc of shade at his feet: a Ground annulus on the boss, inner radius 4.5 m (N <= 2), 5.5 m
    // (N 3-4) or 6.5 m (N >= 5), 5.0 m at N <= 2 in Phase 3, outer 30 m (past the bound). Locked at once, resolves 3.0 s
    // later (Hard 2.4 s, Easy 3.75 s). 45 damage, no knockback, 0.25 s stun. The "come close" move: ranged must run in;
    // its layer partner, Meridian Lances (resolving 0.3 s after it), makes the disc deadly for anyone standing still.
    // Run returns on the resolve tick. Look: the annulus is amber-coral with a bright amber inner edge, and three
    // orrery rings slide in from the rim to that edge during the charge (ZenithCollapseFx); the disc itself stays
    // bare floor, with nothing coral or gold inside it.
    public sealed class ZenithCollapse : AurelionAbility
    {
        public override string Id => "aurelion.zenith_collapse";
        public override string Name => "Zenith Collapse";

        public const float Outer = 30f, WindowS = 3.0f, HardWindowS = 2.4f, StunS = 0.25f;
        public const int Damage = 45;

        public static float SafeRadius(int n, int phase) => n <= 2 ? (phase >= 3 ? 5.0f : 4.5f) : n <= 4 ? 5.5f : 6.5f;
        public int WindowTicks => Sim.Ticks(Hard ? HardWindowS : WindowS * WarnScale);
        // ticks from Run's first tick to its resolve tick (= when Run returns)
        public int Duration() => WindowTicks - 1;

        public Hazard Last { get; private set; }   // test evidence
        // v4 Solar Hunger (abilities-v4 §3.3): the encounter sets these before each cast. Every Collapse in the fight is
        // hungrier: the safe disc shrinks (never below HungerFloor) and the hit grows. 0 = v2.1 / v3 behaviour.
        public float HungerShrink; public int HungerBonus; public float HungerFloor = 3.0f;
        public float CurrentSafe => HungerShrink > 0 ? Mathf.Max(HungerFloor, SafeRadius(N, Phase) - HungerShrink) : SafeRadius(N, Phase);

        public IEnumerable<int> Run()
        {
            Say(Name);
            int win = WindowTicks;
            Clip("MeridianAlignment", win);   // stand-in: glaive lifted straight up
            Last = Spawn(new Hazard
            {
                Source = Name, Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = BossPos,
                Inner = CurrentSafe, Radius = Outer, LockAt = 0, ResolveAt = win,
                Damage = Damage + HungerBonus, Knockback = 0, Stun = Sim.Ticks(StunS),
            });
            Last.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, Rim); Flare(1.4f); };
            ZenithCollapseFx.Create(W, Last);
            for (int i = 0; i < win - 1; i++) { Glow(i / (float)win); yield return 1; }
        }
    }

    // Presentation only. Three dashed gold orrery rings (the SpawnOrrery look: dashes, a riding bead) start spread over
    // the outer band, the outermost on the rim, and slide inward, speeding up, so all three land on the safe disc's edge
    // on the resolve tick and stack there as one bright line. They never cross the edge, so the disc stays bare floor.
    // They counter-rotate and brighten as the sky falls, flash and sink on the resolve; a hazard cleared early (boss
    // death, retry) just dims them.
    public sealed class ZenithCollapseFx : MonoBehaviour
    {
        static readonly Color Gold = new Color(0.82f, 0.64f, 0.3f);
        const int Rings = 3;
        const float Spread = 2.6f;   // start radii: rim, rim - 2.6, rim - 5.2 m

        sealed class Ring { public Transform Root, Bead; public Transform[] Segs; public float Start, Spin, Turn, R; }
        readonly List<Ring> rings = new List<Ring>();
        Material mat; Hazard h; CombatWorld w; float fade = -1;

        // test evidence: the latest instance, its innermost ring radius now and the smallest any ring has reached
        public static ZenithCollapseFx Latest { get; private set; }
        public float Inner => h.Inner;
        public float LeadRadius { get; private set; }
        public float MinRadiusSeen { get; private set; } = float.MaxValue;

        public static ZenithCollapseFx Create(CombatWorld world, Hazard hazard)
        {
            var go = new GameObject("Zenith Collapse fx"); go.transform.SetParent(world.Root, false);
            go.transform.position = new Vector3(hazard.Origin.x, 0.05f, hazard.Origin.z);
            var fx = go.AddComponent<ZenithCollapseFx>(); fx.w = world; fx.h = hazard; fx.mat = Prim.Material(Gold, 1f);
            float rim = AurelionAbility.Rim;
            int n = Mathf.CeilToInt(2 * Mathf.PI * rim / 0.9f);
            for (int j = 0; j < Rings; j++)
            {
                var o = new Ring { Start = Mathf.Max(hazard.Inner + 0.5f, rim - j * Spread), Spin = j % 2 == 0 ? 1 : -1, Segs = new Transform[n] };
                o.Root = new GameObject("Zenith orrery ring").transform; o.Root.SetParent(go.transform, false);
                for (int k = 0; k < n; k++)
                    fx.Share(o.Segs[k] = Prim.Make(PrimitiveKind.Cube, o.Root, Vector3.zero, Vector3.one, Gold, 0, Quaternion.Euler(0, k * 360f / n + 90, 0)));
                o.Bead = Prim.Make(PrimitiveKind.Sphere, o.Root, Vector3.zero, Vector3.one * 0.3f, Gold);
                fx.Share(o.Bead);
                fx.rings.Add(o);
                fx.Place(o, o.Start);
            }
            Latest = fx;
            return fx;
        }

        void Share(Transform t) { var r = t.GetComponent<Renderer>(); Destroy(r.sharedMaterial); r.sharedMaterial = mat; }

        void Place(Ring o, float r)
        {
            o.R = r;
            int n = o.Segs.Length;
            float len = 2 * Mathf.PI * r / n * 1.02f;   // continuous: dashed gold would read as a friendly dotted circle
            for (int k = 0; k < n; k++)
            {
                o.Segs[k].localPosition = Quaternion.Euler(0, k * 360f / n, 0) * new Vector3(0, 0, r);
                o.Segs[k].localScale = new Vector3(len, 0.05f, 0.08f);
            }
            o.Bead.localPosition = Quaternion.Euler(0, o.Turn * 1.5f, 0) * new Vector3(0, 0.16f, r);
            LeadRadius = Mathf.Min(LeadRadius == 0 ? float.MaxValue : LeadRadius, r);
            MinRadiusSeen = Mathf.Min(MinRadiusSeen, r);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool live = w != null && w.Hazards.Live.Contains(h) && !h.Cancelled;
            if (!live && fade < 0) fade = h.Age >= h.ResolveAt ? 1f : 0.4f;   // resolved: flash; cleared early: dim
            if (fade >= 0)
            {
                fade -= dt * 1.6f;
                mat.SetColor("_EmissionColor", Gold * Mathf.Max(0, fade) * 6f);
                transform.position += Vector3.down * dt * 0.5f;
                if (fade <= 0) Destroy(gameObject);
                return;
            }
            float k = Mathf.Clamp01(h.Age / (float)Mathf.Max(1, h.ResolveAt));
            float e = k * k;   // slow at first, then falling fast onto the edge
            float edge = h.Inner + 0.3f;    // inside the annulus, clear of its plain inner edge: the decor never enters the disc or covers the outline
            LeadRadius = 0;
            foreach (var o in rings)
            {
                o.Turn += o.Spin * Mathf.Lerp(10f, 110f, e) * dt;
                o.Root.localRotation = Quaternion.Euler(0, o.Turn, 0);
                Place(o, Mathf.Lerp(o.Start, edge, e));
            }
            mat.SetColor("_EmissionColor", Gold * (0.4f + 2.6f * e));
        }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
            if (Latest == this) Latest = null;
        }
    }
}
