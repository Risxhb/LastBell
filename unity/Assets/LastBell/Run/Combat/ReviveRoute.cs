using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Sylvara revives (Chzy18, 5 Nov: bots wouldn't cross her bullets to a downed ally): a short plan in space and time.
    // The floor is a grid of 0.6 m cells; every 0.2 s for the next 3 s, a cell is blocked where a bullet that can hit this
    // bot passes (seeds as their petal ring after the burst) or a telegraph lands or burns. A walk-speed search over that
    // grid, allowed to wait in place, finds the earliest way into a cell within reach of the downed ally that then stays
    // clear the longest: it waits for a gap, takes the emptiest lane, and stands at the safest point beside them. The steer
    // still makes every move (it dodges what the plan can't see coming), heading for the plan's next waypoint. No immunity.
    static class ReviveRoute
    {
        const float Cell = 0.6f, Dt = 0.2f, Pad = 0.35f;   // pad: about a cell's half-diagonal on top of bullet + body radius
        const int Layers = 15;                              // 3 s
        static int n; static float half; static bool[] blocked;   // [layer * n * n + z * n + x]
        static readonly List<(int dx, int dz)> moves = new List<(int, int)>();
        static int[] seen, from; static int stamp;
        static readonly List<int> frontier = new List<int>(), next = new List<int>();

        // the route for `me` to a spot within reach of `downed`: the waypoint to head for now, and the spot it ends on;
        // false when no clear way exists within 3 s (then `waypoint` is the clear cell nearest the downed ally it can reach)
        public static bool Plan(PlayerController me, Vector3 downed, out Vector3 waypoint, out Vector3 stand)
        {
            var w = me.World; var pos = Sim.Flat(me.transform.position); downed = Sim.Flat(downed);
            Build(me, w);
            int start = Index(pos);
            waypoint = pos; stand = downed;
            if (start < 0) return false;
            float reach = PlayerController.ReviveRange - 0.35f;
            // breadth-first over (cell, layer): one layer per step, each step a walk of at most WalkSpeed * Dt, or a wait
            stamp++; if (seen == null || seen.Length != Layers * n * n) { seen = new int[Layers * n * n]; from = new int[Layers * n * n]; }
            frontier.Clear(); frontier.Add(start); seen[start] = stamp; from[start] = -1;
            int bestGoal = -1, bestClear = -1, nearest = start; float nearestD = Sim.Flat(CellPos(start) - downed).magnitude;
            for (int l = 0; l < Layers - 1 && frontier.Count > 0; l++)
            {
                next.Clear();
                foreach (int node in frontier)
                {
                    int c = node % (n * n), cx = c % n, cz = c / n;
                    foreach (var (dx, dz) in moves)
                    {
                        int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int nc = z * n + x, nn = (l + 1) * n * n + nc;
                        if (seen[nn] == stamp || blocked[nn] || blocked[l * n * n + nc]) continue;   // clear when it gets there and on the way
                        var at = CellPos(nc); if (!CombatWorld.InPlayerBound(at, -0.3f)) continue;
                        seen[nn] = stamp; from[nn] = node; next.Add(nn);
                        float dd = Sim.Flat(at - downed).magnitude;
                        if (dd < nearestD) { nearestD = dd; nearest = nn; }
                        if (dd <= reach)
                        {
                            int clear = 0; for (int k = l + 1; k < Layers && !blocked[k * n * n + nc]; k++) clear++;
                            // the earliest arrival wins; among those of this step, the one that stays clear longest
                            if (bestGoal < 0 || (bestGoal / (n * n) == l + 1 && clear > bestClear)) { bestGoal = nn; bestClear = clear; }
                        }
                    }
                }
                if (bestGoal >= 0) break;
                frontier.Clear(); frontier.AddRange(next);
            }
            int end = bestGoal >= 0 ? bestGoal : nearest;
            stand = CellPos(end % (n * n));
            // the waypoint: the path's cell about 0.6 s ahead (far enough to give the steer a direction, near enough to keep it)
            var path = new List<int>(); for (int k = end; k >= 0; k = from[k]) path.Add(k);
            path.Reverse();
            waypoint = CellPos(path[Mathf.Min(3, path.Count - 1)] % (n * n));
            return bestGoal >= 0;
        }

        static void Build(PlayerController me, CombatWorld w)
        {
            float bound = CombatWorld.PlayerBound;
            int want = Mathf.CeilToInt(2 * bound / Cell) + 1;
            if (want != n || blocked == null) { n = want; blocked = new bool[Layers * n * n]; }
            else System.Array.Clear(blocked, 0, blocked.Length);
            half = (n - 1) * Cell * 0.5f;
            if (moves.Count == 0)
            {
                float step = PlayerController.WalkSpeed * Dt;
                int m = Mathf.FloorToInt(step / Cell);
                for (int dz = -m; dz <= m; dz++) for (int dx = -m; dx <= m; dx++) if (Mathf.Sqrt(dx * dx + dz * dz) * Cell <= step) moves.Add((dx, dz));
            }
            int mine = w.Bullets.ColourOf != null ? w.Bullets.ColourOf(me) : 0;
            bool both = w.Boss is SylvaraEncounter sy && sy.SwapWarned(me);
            foreach (var b in w.Bullets.Live)
            {
                if (b.Dead || (b.Colour != 0 && b.Colour != mine && !both)) continue;
                float r = b.Radius + Hazard.Margin + Pad, fuse = b.Split != null ? (b.Life - b.Age) : float.MaxValue;
                for (int l = 0; l < Layers; l++)
                    for (int h = 0; h < 2; h++)   // the layer's moment and halfway to the next: a bullet doesn't jump a cell
                    {
                        float t = l * Dt + h * Dt * 0.5f;
                        if (t <= fuse) Disc(l, b.Pos + b.Vel * t, r);
                        else if (t - fuse < 1f) Ring(l, b.Pos + b.Vel * fuse, 4.5f * (t - fuse), BulletArt.PetalHitRadius + Hazard.Margin + Pad);   // the petal ring (as SylvaraSteer.Cost)
                    }
            }
            foreach (var hz in w.Hazards.Live)
            {
                bool hurts = (hz.HitOnResolve && hz.Damage > 0) || hz.BurnPerSecond > 0;
                if (!hurts) continue;
                float left = (hz.ResolveAt - hz.Age) * Sim.Dt;
                int from0 = hz.BurnPerSecond > 0 ? 0 : Mathf.Max(0, Mathf.FloorToInt((left - 0.3f) / Dt)), to0 = hz.BurnPerSecond > 0 ? Layers - 1 : Mathf.Min(Layers - 1, Mathf.CeilToInt((left + 0.2f) / Dt));
                if (from0 > Layers - 1) continue;
                for (int c = 0; c < n * n; c++)
                    if (hz.Contains(CellPos(c)))
                        for (int l = from0; l <= to0; l++) blocked[l * n * n + c] = true;
            }
        }

        static void Disc(int l, Vector3 at, float r)
        {
            int x0 = Mathf.FloorToInt((at.x - r + half) / Cell), x1 = Mathf.CeilToInt((at.x + r + half) / Cell);
            int z0 = Mathf.FloorToInt((at.z - r + half) / Cell), z1 = Mathf.CeilToInt((at.z + r + half) / Cell);
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(n - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(n - 1, x1); x++)
                {
                    float cx = x * Cell - half - at.x, cz = z * Cell - half - at.z;
                    if (cx * cx + cz * cz <= r * r) blocked[l * n * n + z * n + x] = true;
                }
        }

        static void Ring(int l, Vector3 at, float radius, float width)
        {
            int k = Mathf.Max(8, Mathf.CeilToInt(2 * Mathf.PI * radius / (Cell * 0.8f)));
            for (int i = 0; i < k; i++) Disc(l, at + Sim.Dir(360f * i / k) * radius, width);
        }

        static int Index(Vector3 p)
        {
            int x = Mathf.RoundToInt((p.x + half) / Cell), z = Mathf.RoundToInt((p.z + half) / Cell);
            return x < 0 || z < 0 || x >= n || z >= n ? -1 : z * n + x;
        }
        static Vector3 CellPos(int c) => new Vector3((c % n) * Cell - half, 0, (c / n) * Cell - half);
    }
}
