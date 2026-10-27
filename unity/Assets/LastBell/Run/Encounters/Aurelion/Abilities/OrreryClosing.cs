using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 04 Orrery Closing (aurelion.orrery_closing), the signature, brief abilities-v2/04-orrery_closing.md (proposal).
    // Run(horizonTicks) closes the rim ring (8.5 m out from the arena centre, out past the bound) and the crown (3.6 m on
    // the boss), both locked on the warning's first tick and resolving together 2.3 s later (Easy 2.9 s, Hard 2.0 s). Two separate
    // Ground instances, 40 damage each, no knockback, 0.25 s stun. It returns on the tick after they resolve.
    // horizonTicks > 0 (Phase 2+: 240, Hard 360) keeps the rim hitting for that long after the resolve (the Horizon,
    // Hazard.ActiveTicks): anyone who steps out is hit once (one hit per instance, so whoever the rim already hit is not
    // hit again) and it always expires. HorizonLive is true while that rim is still live; boss death clears it in the
    // killing tick (BossEncounter.Die > ClearTransient), which ends HorizonLive with it.
    public sealed class OrreryClosing : AurelionAbility
    {
        public override string Id => "aurelion.orrery_closing";
        public override string Name => "Orrery Closing";

        public const float WarnS = 2.3f, RimInner = 8.5f, RimOuter = 30f, CrownRadius = 3.6f, StunS = 0.25f;
        public const int Damage = 40;

        // test evidence: the last closing's two instances and the Horizon it was given
        public Hazard LastRim { get; private set; }
        public Hazard LastCrown { get; private set; }
        public int LastHorizon { get; private set; }

        public bool HorizonLive => W != null && LastRim != null && LastHorizon > 0 && !LastRim.Cancelled && W.Hazards.Live.Contains(LastRim);

        // ticks from Run's first tick to the rim/crown resolve tick (Run itself returns one tick later); the v2.1 plan
        // lines pairs up with it. CONTRACT: keep it equal to the real timing when the windows change.
        public int Duration() => Warn() - 1;
        // the warning: 2.3 s, Easy 2.9 s, Hard x0.7 held at the 2.0 s floor (ABILITIES-v2.1.md §2)
        public const float HardFloorS = 2.0f;
        public int Warn() => Win(WarnS, HardFloorS);

        public IEnumerable<int> Run(int horizonTicks)
        {
            Say(Name);
            int warn = Warn();
            LastHorizon = Mathf.Max(0, horizonTicks);
            Clip("MeridianAlignment", warn);
            var rim = Spawn(new Hazard
            {
                Source = Name + " rim", Shape = HazardShape.Ring, Kind = HazardKind.Ground, Origin = Centre,
                Inner = RimInner, Radius = RimOuter, LockAt = 0, ResolveAt = warn, Damage = Damage, Knockback = 0, Stun = Sim.Ticks(StunS),
                // the resolve tick plus horizonTicks after it
                ActiveTicks = LastHorizon > 0 ? LastHorizon + 1 : 0,
            });
            var crown = Spawn(new Hazard
            {
                Source = Name + " crown", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = BossPos,
                Radius = CrownRadius, LockAt = 0, ResolveAt = warn, Damage = Damage, Knockback = 0, Stun = Sim.Ticks(StunS),
            });
            LastRim = rim; LastCrown = crown;
            rim.OnResolve = hz => { W.BossFx.TollResolve(hz.Origin, hz.Inner, Rim); Flare(1.2f); };
            crown.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
            OrreryClosingFx.Create(W, rim, crown, LastHorizon);
            for (int i = 0; i < warn; i++) { Glow(i / (float)warn); yield return 1; }
            if (LastHorizon > 0) Say("Horizon");
        }
    }

    // Presentation only. The orrery rings: dashed gold circles laid exactly on the rim's inner edge and the crown's edge,
    // with clock ticks and a riding bead. The rim ring descends from above the dais and the crown drops from his halo,
    // both spinning up toward the hit; they land on the resolve tick. The crown then flashes and sinks away. With a
    // Horizon the rim ring stays down on its edge, the band turns a calmer coral with a pulsing border ("don't cross"),
    // and in its last second the ring blinks faster and dims before lifting away as the hazard expires. A hazard cleared
    // early (boss death, retry) just fades its ring.
    sealed class OrreryClosingFx : MonoBehaviour
    {
        static readonly Color Gold = new Color(0.82f, 0.64f, 0.3f);
        const float EdgeInset = 0.3f;   // telegraph style: decor rings sit inside the hazard, continuous, never on the outline
        static readonly Color CalmFill = new Color(0.78f, 0.36f, 0.30f), CalmBorder = new Color(1.6f, 0.75f, 0.45f);
        const float RimStartY = 3.5f, CrownStartY = 3.4f;

        sealed class Ring { public Hazard H; public Transform Root, Bead; public Material Mat; public float Spin, Turn, R, StartY, Fade = -1; }
        readonly List<Ring> rings = new List<Ring>();
        CombatWorld w; int horizon;

        public static void Create(CombatWorld world, Hazard rim, Hazard crown, int horizonTicks)
        {
            var go = new GameObject("Orrery Closing fx"); go.transform.SetParent(world.Root, false);
            var fx = go.AddComponent<OrreryClosingFx>(); fx.w = world; fx.horizon = horizonTicks;
            fx.Add(rim, rim.Inner + EdgeInset, 1, RimStartY);       // just inside each hazard, clear of its plain outline
            fx.Add(crown, crown.Radius - EdgeInset, -1.6f, CrownStartY);
        }

        void Add(Hazard h, float r, float spin, float startY)
        {
            var o = new Ring { H = h, Spin = spin, R = r, StartY = startY, Mat = Prim.Material(Gold, 1f) };
            o.Root = new GameObject("Orrery ring").transform; o.Root.SetParent(transform, false);
            o.Root.position = new Vector3(h.Origin.x, startY, h.Origin.z);
            int n = Mathf.Max(12, Mathf.CeilToInt(2 * Mathf.PI * r / 0.6f));
            for (int k = 0; k < n; k++)
            {
                float ang = k * 360f / n;
                Share(Prim.Make(PrimitiveKind.Cube, o.Root, Quaternion.Euler(0, ang, 0) * new Vector3(0, 0, r), new Vector3(2 * Mathf.PI * r / n * 1.02f, 0.05f, 0.07f), Gold, 0, Quaternion.Euler(0, ang + 90, 0)), o.Mat);
            }
            // clock ticks just inside the edge (the rim's point out into the danger, the crown's point in)
            float side = h.Shape == HazardShape.Ring ? 0.25f : -0.25f;
            for (int k = 0; k < 12; k++)
                Share(Prim.Make(PrimitiveKind.Cube, o.Root, Quaternion.Euler(0, k * 30, 0) * new Vector3(0, 0.04f, r + side), new Vector3(k % 3 == 0 ? 0.14f : 0.08f, 0.08f, k % 3 == 0 ? 0.6f : 0.35f), Gold, 0, Quaternion.Euler(0, k * 30, 0)), o.Mat);
            o.Bead = Prim.Make(PrimitiveKind.Sphere, o.Root, new Vector3(0, 0.18f, r), Vector3.one * 0.3f, Gold);
            Share(o.Bead, o.Mat);
            rings.Add(o);
        }
        static void Share(Transform t, Material m) { var r = t.GetComponent<Renderer>(); Destroy(r.sharedMaterial); r.sharedMaterial = m; }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var o = rings[i];
                var h = o.H;
                bool live = w != null && w.Hazards.Live.Contains(h) && !h.Cancelled;
                bool resolved = h.Age >= h.ResolveAt;
                if (!live && o.Fade < 0) o.Fade = resolved ? 1f : 0.4f;   // resolved / expired: flash; cleared early: dim
                if (o.Fade >= 0)
                {
                    o.Fade -= dt * 1.6f;
                    o.Mat.SetColor("_EmissionColor", Gold * Mathf.Max(0, o.Fade) * 6f);
                    // the crown sinks into the floor; an expired Horizon lifts away
                    o.Root.position += (h.Shape == HazardShape.Ring && horizon > 0 && resolved ? Vector3.up : Vector3.down) * dt * 0.6f;
                    if (o.Fade <= 0) { Destroy(o.Root.gameObject); Destroy(o.Mat); rings.RemoveAt(i); }
                    continue;
                }
                var p = o.Root.position;
                if (!resolved)
                {
                    float k = Mathf.Clamp01(h.Age / (float)Mathf.Max(1, h.ResolveAt));
                    o.Turn += o.Spin * Mathf.Lerp(12f, 140f, k * k) * dt;   // the clockwork winds up toward the close
                    o.Root.position = new Vector3(p.x, Mathf.Lerp(o.StartY, 0.05f, k * k * k), p.z);   // descends, slamming at the end
                    o.Mat.SetColor("_EmissionColor", Gold * (0.4f + 2.4f * k * k));
                }
                else
                {
                    // the Horizon: the ring sits on its edge, turning slowly; the band is calmer coral with a pulsing border
                    float left = (h.ResolveAt + Mathf.Max(0, h.ActiveTicks - 1) - h.Age) * Sim.Dt;
                    float rate = left < 1f ? 9f : 3f;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate * Mathf.PI);
                    float dim = Mathf.Clamp01(left / 1f);
                    o.Turn += o.Spin * 8f * dt;
                    o.Root.position = new Vector3(p.x, 0.05f, p.z);
                    o.Mat.SetColor("_EmissionColor", Gold * (0.6f + 1.8f * pulse) * Mathf.Lerp(0.35f, 1f, dim));
                    if (h.Tele != null) h.Tele.Tint(CalmFill * Mathf.Lerp(0.55f, 1f, dim), CalmBorder * (0.6f + 0.8f * pulse));
                }
                o.Root.rotation = Quaternion.Euler(0, o.Turn, 0);
                o.Bead.localPosition = Quaternion.Euler(0, o.Turn * 2.5f, 0) * new Vector3(0, 0.18f, o.R);
            }
            if (rings.Count == 0) Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var o in rings) if (o.Mat) Destroy(o.Mat);
        }
    }
}
