using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Errant Planets (aurelion.errant_planets), v4 Phase 2 (Misalignment), accepted 2 Nov (Chzy18: "make it harder by giving
    // the players something to do/dodge"). Three planets (Easy one; two until 3 Nov), 1.6 m across, circle the arena on
    // off-centre orbits at 5.5 m/s (4.5 until 3 Nov: Chzy18 "I want them harder") in alternating directions over every Phase 2 block (not Total Eclipse). Their paths are drawn as faint rings,
    // so a crossing can be timed. Touching one: 30 damage and a 1.5 m knockback. Each lap is its own hazard instance, so a planet hits
    // a player at most once per lap (gpt/08 §2: once per hazard instance; it was once a second until 4 Nov, Codex review HIT-01).
    // The encounter calls Step every tick; the planet is a live Circle hazard whose origin moves, so bots dodge it.
    // Hard (5 Nov, done as recommended, review later): one planet on the wide rim orbit over Phase 1 as well (Begin(true)).
    public sealed class ErrantPlanets : AurelionAbility
    {
        public override string Id => "aurelion.errant_planets";
        public override string Name => "Errant Planets";

        public const float Radius = 0.8f, Speed = 5.5f;
        public const int Damage = 30;
        static readonly (Vector3 centre, float r, float dir, float start)[] Orbits =
        {
            (new Vector3(1.5f, 0, 1.0f), 7.0f, 1f, 0f),
            (new Vector3(-1.0f, 0, -1.5f), 9.5f, -1f, 180f),
            (Vector3.zero, 12.0f, 1f, 90f),  // 3 Nov: a wide orbit near the rim, clear of the Zenith Collapse disc (an inner one forced bots out of it)
        };
        // coral, not gold: the Skyward floor already has gold rings, and a gold path vanished among them (2 Nov shots)
        static readonly Color PathFill = new Color(0.45f, 0.16f, 0.13f, 0.0f), PathEdge = new Color(0.45f, 0.16f, 0.13f, 0.3f);   // dim: the path never hurts, only the planet

        sealed class Planet { public Hazard H; public BellkeeperFx.Telegraph Path; public int Orbit; public float Angle, Travelled; }
        readonly List<Planet> planets = new List<Planet>();
        public bool Live => planets.Count > 0;
        public int Count => planets.Count;
        public Vector3 PlanetAt(int i) => planets[i].H.Origin;   // test evidence

        public void Begin(bool rimOnly = false)
        {
            int want = rimOnly || Easy ? 1 : Orbits.Length;
            if (Live && Count == want && (planets[0].Orbit == 2) == rimOnly) return;
            if (Live) End();
            AurelionTimeline.Note("errant planets begin");
            Boss.PlayClipIfIdle("PlanetsRelease", Sim.Ticks(1.5f));
            for (int k = 0; k < want; k++)
            {
                int i = rimOnly ? 2 : k;   // the rim orbit stays clear of the Phase 1 towers, rings and Collapse disc
                var o = Orbits[i];
                var p = new Planet { Orbit = i, Angle = o.start };
                p.H = Lap(Pos(o, o.start));
                p.Path = W.BossFx.Ring(o.r - 0.05f, o.r + 0.05f);
                p.Path.Tint(PathFill, PathEdge); p.Path.Progress(1); p.Path.Order = -2; p.Path.Place(o.centre, 0);
                planets.Add(p);
            }
        }

        // one planet's hazard for one lap of its orbit
        Hazard Lap(Vector3 at) => Spawn(new Hazard
        {
            Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = Radius,
            LockAt = 0, ResolveAt = 1, ActiveTicks = 1 << 28, Damage = Damage, Knockback = 1.5f, Stun = 0,
        });
        public int Laps { get; private set; }   // test evidence

        public void Step()
        {
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i]; var o = Orbits[p.Orbit];
                if (p.H.Cancelled) { End(); return; }
                float step = Speed / o.r * Mathf.Rad2Deg / Sim.Hz;
                p.Angle += o.dir * step; p.Travelled += step;
                p.H.Origin = Pos(o, p.Angle);
                // a full lap: the planet comes round as a new hazard instance, so it may hit the same players once more
                if (p.Travelled >= 360f) { p.Travelled -= 360f; var old = p.H; p.H = Lap(p.H.Origin); W.Hazards.Cancel(old); Laps++; }
            }
        }

        public void End()
        {
            foreach (var p in planets) { W.Hazards.Cancel(p.H); p.Path.Expire(); }
            planets.Clear();
        }
        public override void Abort() => End();

        // bots: is q in a planet's way now or within `seconds` (sampled every 0.2 s along its orbit)?
        public bool Near(Vector3 q, float seconds, float pad)
        {
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i]; var o = Orbits[p.Orbit];
                for (float t = 0; t <= seconds + 0.01f; t += 0.2f)
                    if (Sim.Flat(q - Pos(o, p.Angle + o.dir * Speed / o.r * Mathf.Rad2Deg * t)).magnitude < Radius + pad) return true;
            }
            return false;
        }

        static Vector3 Pos((Vector3 centre, float r, float dir, float start) o, float deg) => o.centre + Sim.Dir(deg) * o.r;
    }
}
