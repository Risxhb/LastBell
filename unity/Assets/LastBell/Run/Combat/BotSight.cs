using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // What a bot sees beyond the live hazard list, read the way a player reads the screen (no encounter internals):
    // - orbs: the glowing spheres a boss leaves flying (Pendulum Sentinel's counterweight orbs, the Oracle's well orbs).
    //   Weights lying on the floor are kept out of the same way, as still circles.
    //   Each one's velocity is how far its sphere moved since the last tick; a sphere just taken from the pool has none yet.
    // - after-strikes: a hazard that hits after its warning goes (HitOnResolve false: a Toll Lane rolling down its runes)
    //   still counts for 0.5 s once its warning is gone.
    // Shared by every bot in a world and refreshed once per combat tick.
    public static class BotSight
    {
        public sealed class Orb { public Transform T; public Vector3 Pos, Vel; public float R, Fixed; public int Tick; public bool Moving; }
        public static readonly List<Orb> Orbs = new List<Orb>();
        static readonly Dictionary<Transform, Orb> seen = new Dictionary<Transform, Orb>();
        static readonly List<Transform> pool = new List<Transform>();
        static readonly List<Hazard> prevLive = new List<Hazard>(), ghosts = new List<Hazard>();
        static readonly List<int> ghostUntil = new List<int>();
        static CombatWorld world; static int tick = -1, scanTick = -999;
        const int GhostTicks = 30, ScanEvery = 5;

        public static void Update(CombatWorld w, BossEncounter boss)
        {
            if (w != world) { world = w; tick = -1; scanTick = -999; seen.Clear(); pool.Clear(); Orbs.Clear(); prevLive.Clear(); ghosts.Clear(); ghostUntil.Clear(); }
            if (w.Tick == tick) return;
            tick = w.Tick;
            // orbs: the boss's pooled spheres, found by name under the arena root (rescanned now and then: the pool grows)
            var root = boss ? boss.transform.parent : null;
            if (root && tick - scanTick >= ScanEvery)
            {
                scanTick = tick; pool.Clear();
                for (int i = 0; i < root.childCount; i++) { var c = root.GetChild(i); if (c.name.EndsWith(" orb") || c.name.EndsWith("(lying)")) pool.Add(c); }
            }
            Orbs.Clear();
            for (int i = 0; i < pool.Count; i++)
            {
                var t = pool[i]; if (!t) continue;
                if (!seen.TryGetValue(t, out var o))
                {
                    // a weight lying on the floor (Pendulum Sentinel): whoever steps inside its circle takes all its damage
                    o = new Orb { T = t, Tick = -999, Fixed = t.name.EndsWith("(lying)") ? PendulumSentinelRules.WeightRadius + Hazard.Margin - PlayerController.Radius : 0 };
                    seen[t] = o;
                }
                if (!t.gameObject.activeInHierarchy) { o.Tick = -999; continue; }
                var p = Sim.Flat(t.position); int dt = tick - o.Tick;
                o.Moving = o.Fixed <= 0 && dt >= 1 && dt <= 3 && Sim.Flat(p - o.Pos).magnitude < 1.5f;   // a pooled sphere reused elsewhere jumps
                o.Vel = o.Moving ? (p - o.Pos) * (Sim.Hz / (float)dt) : Vector3.zero;
                o.Pos = p; o.Tick = tick; o.R = o.Fixed > 0 ? o.Fixed : t.lossyScale.x / 2.2f;   // orb views are drawn 2.2 x the orb radius
                Orbs.Add(o);
            }
            // after-strikes: warnings that left the live list this tick without being withdrawn
            var live = w.Hazards.Live;
            for (int i = 0; i < prevLive.Count; i++)
            {
                var h = prevLive[i];
                if (h.HitOnResolve || h.Cancelled || h.BurnPerSecond > 0 || h.Soak || h.Damage <= 0) continue;   // a soak or a marker is no strike
                bool still = false; for (int j = 0; j < live.Count && !still; j++) still = live[j] == h;
                if (!still) { ghosts.Add(h); ghostUntil.Add(tick + GhostTicks); }
            }
            for (int i = ghosts.Count - 1; i >= 0; i--) if (ghostUntil[i] < tick) { ghosts.RemoveAt(i); ghostUntil.RemoveAt(i); }
            prevLive.Clear(); for (int i = 0; i < live.Count; i++) prevLive.Add(live[i]);
        }

        // an orb's path over the next `ahead` seconds passes within its radius + a body + pad of q
        public static bool OrbHits(Vector3 q, float ahead, float pad)
        {
            for (int i = 0; i < Orbs.Count; i++)
            {
                var o = Orbs[i]; float reach = o.R + PlayerController.Radius + pad;
                var a = o.Pos; var ab = o.Moving ? o.Vel * ahead : Vector3.zero;
                float len2 = ab.sqrMagnitude, u = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(Sim.Flat(q) - a, ab) / len2) : 0f;
                if (Sim.Flat(q - (a + ab * u)).sqrMagnitude <= reach * reach) return true;
            }
            return false;
        }

        public static bool GhostHits(PlayerController me, Vector3 q)
        {
            for (int i = 0; i < ghosts.Count; i++) { var h = ghosts[i]; if (h.Contains(q) && (h.Affects == null || h.Affects(me))) return true; }
            return false;
        }
    }
}
