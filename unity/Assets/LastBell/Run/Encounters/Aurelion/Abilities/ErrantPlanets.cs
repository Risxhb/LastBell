using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Errant Planets (aurelion.errant_planets), v4 Phase 2 (Misalignment), accepted 2 Nov (Chzy18: "make it harder by giving
    // the players something to do/dodge"). Three planets (Easy one; two until 3 Nov), 1.6 m across, circle the arena on
    // off-centre orbits at 5.5 m/s (4.5 until 3 Nov: Chzy18 "I want them harder") in alternating directions over every Phase 2 block (not Total Eclipse). Their paths are drawn as faint rings,
    // so a crossing can be timed. Touching one: 30 damage and a 1.5 m knockback, at most once a second per player.
    // The encounter calls Step every tick; the planet is a live Circle hazard whose origin moves, so bots dodge it.
    public sealed class ErrantPlanets : AurelionAbility
    {
        public override string Id => "aurelion.errant_planets";
        public override string Name => "Errant Planets";

        public const float Radius = 0.8f, Speed = 5.5f, RehitS = 1.0f;
        public const int Damage = 30;
        static readonly (Vector3 centre, float r, float dir, float start)[] Orbits =
        {
            (new Vector3(1.5f, 0, 1.0f), 7.0f, 1f, 0f),
            (new Vector3(-1.0f, 0, -1.5f), 9.5f, -1f, 180f),
            (Vector3.zero, 3.5f, 1f, 90f),   // 3 Nov: a tight inner orbit, through the middle of the dais
        };
        // coral, not gold: the Skyward floor already has gold rings, and a gold path vanished among them (2 Nov shots)
        static readonly Color PathFill = new Color(0.45f, 0.16f, 0.13f, 0.0f), PathEdge = new Color(0.45f, 0.16f, 0.13f, 0.3f);   // dim: the path never hurts, only the planet

        sealed class Planet { public Hazard H; public BellkeeperFx.Telegraph Path; public int Orbit; public float Angle; public readonly Dictionary<PlayerController, int> At = new Dictionary<PlayerController, int>(); }
        readonly List<Planet> planets = new List<Planet>();
        public bool Live => planets.Count > 0;
        public int Count => planets.Count;
        public Vector3 PlanetAt(int i) => planets[i].H.Origin;   // test evidence

        public void Begin()
        {
            if (Live) return;
            AurelionTimeline.Note("errant planets begin");
            int n = Easy ? 1 : Orbits.Length;
            for (int i = 0; i < n; i++)
            {
                var o = Orbits[i];
                var p = new Planet { Orbit = i, Angle = o.start };
                p.H = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Pos(o, o.start), Radius = Radius,
                    LockAt = 0, ResolveAt = 1, ActiveTicks = 1 << 28, Damage = Damage, Knockback = 1.5f, Stun = 0,
                });
                p.Path = W.BossFx.Ring(o.r - 0.05f, o.r + 0.05f);
                p.Path.Tint(PathFill, PathEdge); p.Path.Progress(1); p.Path.Order = -2; p.Path.Place(o.centre, 0);
                planets.Add(p);
            }
        }

        public void Step()
        {
            int rehit = Sim.Ticks(RehitS);
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i]; var o = Orbits[p.Orbit];
                if (p.H.Cancelled) { End(); return; }
                p.Angle += o.dir * Speed / o.r * Mathf.Rad2Deg / Sim.Hz;
                p.H.Origin = Pos(o, p.Angle);
                // once a second per player: a hit enters Hit (the hazard's once-per-instance rule), so let it go again later
                foreach (var who in p.H.Hit) if (!p.At.ContainsKey(who)) p.At[who] = W.Tick;
                foreach (var kv in p.At) if (W.Tick - kv.Value >= rehit) { p.H.Hit.Remove(kv.Key); stale.Add(kv.Key); }
                foreach (var who in stale) p.At.Remove(who);
                stale.Clear();
            }
        }
        readonly List<PlayerController> stale = new List<PlayerController>();

        public void End()
        {
            foreach (var p in planets) { W.Hazards.Cancel(p.H); p.Path.Expire(); }
            planets.Clear();
        }
        public override void Abort() => End();

        static Vector3 Pos((Vector3 centre, float r, float dir, float start) o, float deg) => o.centre + Sim.Dir(deg) * o.r;
    }
}
