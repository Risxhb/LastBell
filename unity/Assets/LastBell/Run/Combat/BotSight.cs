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
    // - bullet paths: a harmless preview of where bullets will fly (Damage 0, a lane or a cone: Crucivar's turrets). Once
    //   it goes the bullets leave its origin, so its area is dangerous where the bullets in it have not passed yet and
    //   would reach within SoonS s (bullets fly at most ShotSpeed). The far end of a long fan is free until the wave
    //   nears: a casting across the arena from its turret can be hit between volleys. The preview itself counts the same
    //   way: only where its bullets would arrive within SoonS s of it ending.
    // Shared by every bot in a world and refreshed once per combat tick.
    public static class BotSight
    {
        public sealed class Orb { public Transform T; public Vector3 Pos, Vel; public float R, Fixed; public int Tick; public bool Moving; }
        public static readonly List<Orb> Orbs = new List<Orb>();
        static readonly Dictionary<Transform, Orb> seen = new Dictionary<Transform, Orb>();
        static readonly List<Transform> pool = new List<Transform>();
        static readonly List<Hazard> prevLive = new List<Hazard>(), ghosts = new List<Hazard>();
        static readonly List<int> ghostUntil = new List<int>();
        static readonly List<Hazard> paths = new List<Hazard>(), bullets = new List<Hazard>();
        static readonly List<int> pathFrom = new List<int>();
        static readonly List<List<Hazard>> pathShots = new List<List<Hazard>>();   // the bullets that left each path
        static readonly HashSet<Hazard> seenShots = new HashSet<Hazard>();
        // each flying bullet's velocity, from how far it moved since the last tick (a new one has none yet)
        static readonly Dictionary<Hazard, (Vector3 at, int tick, Vector3 vel)> shotSeen = new Dictionary<Hazard, (Vector3, int, Vector3)>();
        static readonly List<Hazard> shotGone = new List<Hazard>();
        // where the bullets come from (Crucivar's rim turrets), traced back from each bullet's speed and age: two brand carriers
        // walked out to the wall beside one and its spiral arms, which sweep along the wall, pinned them there (11-04)
        public static readonly List<Vector3> Turrets = new List<Vector3>();
        static readonly List<int> turretTick = new List<int>();
        static readonly HashSet<Hazard> traced = new HashSet<Hazard>();
        // s before its bullets show; m past a spot before the wave has gone by; s a path keeps taking new bullets (a spiral fires 2.4 s)
        const float PathLead = 0.8f, PathPass = 1.2f, PathOwn = 2.6f, SoonS = 1.6f, ShotSpeed = 6f;
        static CombatWorld world; static int tick = -1, scanTick = -999;
        const int GhostTicks = 30, ScanEvery = 5;

        public static void Update(CombatWorld w, BossEncounter boss)
        {
            if (w != world) { world = w; tick = -1; scanTick = -999; seen.Clear(); pool.Clear(); Orbs.Clear(); prevLive.Clear(); ghosts.Clear(); ghostUntil.Clear(); paths.Clear(); pathFrom.Clear(); pathShots.Clear(); seenShots.Clear(); shotSeen.Clear(); Turrets.Clear(); turretTick.Clear(); traced.Clear(); }
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
            // bullet paths that left the live list without being withdrawn, and the bullets now flying (small moving circles)
            for (int i = 0; i < prevLive.Count; i++)
            {
                var h = prevLive[i];
                if (h.Damage > 0 || h.Cancelled || h.Soak || h.BurnPerSecond > 0 || (h.Shape != HazardShape.Lane && h.Shape != HazardShape.Cone)) continue;
                bool still = false; for (int j = 0; j < live.Count && !still; j++) still = live[j] == h;
                if (!still) { paths.Add(h); pathFrom.Add(tick); pathShots.Add(new List<Hazard>()); }
            }
            bullets.Clear();
            for (int i = 0; i < live.Count; i++) { var h = live[i]; if (h.Shape == HazardShape.Circle && h.Track != null && h.Damage > 0 && h.Radius <= 0.7f && !h.Cancelled) bullets.Add(h); }
            // a new bullet belongs to the paths it appears in while they are fresh (a later volley has its own path)
            for (int j = 0; j < bullets.Count; j++)
            {
                if (!seenShots.Add(bullets[j])) continue;
                for (int i = 0; i < paths.Count; i++) if (tick - pathFrom[i] <= Sim.Ticks(PathOwn) && InPath(paths[i], bullets[j].Origin)) pathShots[i].Add(bullets[j]);
            }
            seenShots.RemoveWhere(b => !bullets.Contains(b));
            for (int j = 0; j < bullets.Count; j++)
            {
                var b = bullets[j]; var p = Sim.Flat(b.Origin);
                if (shotSeen.TryGetValue(b, out var o) && tick > o.tick) shotSeen[b] = (p, tick, (p - o.at) * (Sim.Hz / (float)(tick - o.tick)));
                else if (!shotSeen.ContainsKey(b)) shotSeen[b] = (p, tick, Vector3.zero);
            }
            shotGone.Clear(); foreach (var b in shotSeen.Keys) if (!bullets.Contains(b)) shotGone.Add(b);
            foreach (var b in shotGone) { shotSeen.Remove(b); traced.Remove(b); }
            for (int j = 0; j < bullets.Count; j++)
            {
                var b = bullets[j]; if (traced.Contains(b) || !shotSeen.TryGetValue(b, out var o) || o.vel.sqrMagnitude < 0.01f) continue;
                traced.Add(b);
                var from = o.at - o.vel * (b.Age / (float)Sim.Hz); int near = -1;
                for (int i = 0; i < Turrets.Count && near < 0; i++) if (Sim.Flat(Turrets[i] - from).magnitude < 1.5f) near = i;
                if (near < 0) { Turrets.Add(from); turretTick.Add(tick); } else turretTick[near] = tick;
            }
            for (int i = Turrets.Count - 1; i >= 0; i--) if (tick - turretTick[i] > 12 * Sim.Hz) { Turrets.RemoveAt(i); turretTick.RemoveAt(i); }
            for (int i = paths.Count - 1; i >= 0; i--)
            {
                pathShots[i].RemoveAll(b => !bullets.Contains(b));
                if (pathShots[i].Count == 0 && tick - pathFrom[i] > Sim.Ticks(PathLead)) { paths.RemoveAt(i); pathFrom.RemoveAt(i); pathShots.RemoveAt(i); }
            }
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

        // metres to the nearest turret seen firing lately (infinity if none)
        public static float TurretDist(Vector3 q)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < Turrets.Count; i++) best = Mathf.Min(best, Sim.Flat(Turrets[i] - q).magnitude);
            return best;
        }

        public static IReadOnlyList<Hazard> Bullets => bullets;   // the small moving circles flying now (Crucivar's turret bullets)

        // a boss part's HP and kill order (Crucivar PR 19 adds BossPart.Hp, MaxHp and Order); read by name so this
        // branch builds before that lands: -1 / 0 when the field is not there
        static readonly System.Reflection.FieldInfo partHp = typeof(BossPart).GetField("Hp"), partMax = typeof(BossPart).GetField("MaxHp"), partOrder = typeof(BossPart).GetField("Order");
        public static float PartHp(BossPart p) => partHp != null ? (float)partHp.GetValue(p) : 0;
        public static float PartMaxHp(BossPart p) => partMax != null ? (float)partMax.GetValue(p) : 0;
        public static int PartOrder(BossPart p) => partOrder != null ? (int)partOrder.GetValue(p) : -1;
        public static Vector3 ShotVel(Hazard b) => shotSeen.TryGetValue(b, out var o) ? o.vel : Vector3.zero;

        public static bool GhostHits(PlayerController me, Vector3 q)
        {
            for (int i = 0; i < ghosts.Count; i++) { var h = ghosts[i]; if (h.Contains(q) && (h.Affects == null || h.Affects(me))) return true; }
            for (int i = 0; i < paths.Count; i++)
            {
                var h = paths[i]; if (!h.Contains(q)) continue;
                float at = Along(h, q);
                var shots = pathShots[i];
                for (int j = 0; j < shots.Count; j++) { float b = Along(h, shots[j].Origin); if (b <= at + PathPass && at - b <= ShotSpeed * SoonS) return true; }
            }
            return false;
        }

        // seconds until the first wave of a remembered path still to pass q gets there (infinity if none): a ring's wall needs
        // longer to leave than a lane, so a bot steering through bullets weighs how soon it comes
        public static float PathArrive(Vector3 q)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < paths.Count; i++)
            {
                var h = paths[i]; if (!h.Contains(q)) continue;
                float at = Along(h, q);
                var shots = pathShots[i];
                for (int j = 0; j < shots.Count; j++)
                {
                    float b = Along(h, shots[j].Origin); if (b > at + PathPass) continue;
                    best = Mathf.Min(best, Mathf.Max(0, at - b) / Mathf.Max(1f, ShotVel(shots[j]).magnitude));
                }
            }
            return best;
        }

        // a bullet preview (harmless itself) and whether its bullets would reach q within SoonS s of it ending
        public static bool IsPath(Hazard h) => h.Damage <= 0 && !h.Soak && h.BurnPerSecond <= 0 && (h.Shape == HazardShape.Lane || h.Shape == HazardShape.Cone);
        public static bool PathSoon(Hazard h, Vector3 q) => (h.ResolveAt - h.Age) / (float)Sim.Hz + Mathf.Max(0, Along(h, q)) / ShotSpeed <= SoonS;

        // how far along a bullet path a point is (a lane: along its line; a cone: out from its tip), and whether a bullet is
        // in its band (a lane's bullets start up to a column behind its origin)
        static float Along(Hazard h, Vector3 p) => h.Shape == HazardShape.Lane ? Vector3.Dot(Sim.Flat(p - h.Origin), Sim.Dir(h.Yaw)) : Sim.Flat(p - h.Origin).magnitude;
        static bool InPath(Hazard h, Vector3 p)
        {
            var v = Sim.Flat(p - h.Origin);
            if (h.Shape == HazardShape.Lane)
            {
                var d = Sim.Dir(h.Yaw); float a = Vector3.Dot(v, d);
                return a >= -12f && a <= h.Length + 1f && (v - d * a).magnitude <= h.Width / 2f + 0.6f;
            }
            return v.magnitude <= h.Radius + 1f && (v.magnitude < 0.5f || Mathf.Abs(Mathf.DeltaAngle(h.Yaw, Sim.YawTo(h.Origin, p, h.Yaw))) <= h.HalfAngle + 3f);
        }
    }
}
