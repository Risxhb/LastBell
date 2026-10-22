using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Zenith Collapse (aurelion.zenith_collapse, v2.1) checks, owned by the Abilities session; called once from
    // AurelionV2Checks. Spec: abilities-v2/ABILITIES-v2.1.md §1 (and §3 for its Lances pairing).
    public sealed partial class RunAutoTest
    {
        IEnumerator V2ZenithCollapse()
        {
            // 1 + 8: ticks per level (locked at once; resolves 3.0 / 2.4 / 3.75 s after the spawn); Run returns on the resolve tick
            foreach (var (level, secs) in new[] { (Difficulty.Easy, 3.75f), (Difficulty.Medium, 3.0f), (Difficulty.Hard, 2.4f) })
            {
                yield return V2Setup(1, level, new[] { new Vector3(0, 0, 2.8f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                int dur = V2Boss.Zenith.Duration();
                V2Begin(V2Boss.Zenith.Run());
                yield return V2WaitIdle(10);
                var z = V2Spawned("Zenith Collapse").FirstOrDefault();

                V2Check($"{level}: one annulus on the boss, 4.5 m solo to 30 m, Ground, 45, locked at once, resolves {secs} s after it appears",
                    z != null && V2Spawned("Zenith Collapse").Count() == 1 && z.H.Shape == HazardShape.Ring && z.H.Kind == HazardKind.Ground && Mathf.Approximately(z.H.Inner, 4.5f)
                    && z.H.Radius >= 28f && z.H.Damage == ZenithCollapse.Damage && z.H.LockAt == 0 && z.H.ResolveAt == Sim.Ticks(secs) && z.H.Knockback == 0,
                    z == null ? "no spawn" : $"inner {z.H.Inner}, outer {z.H.Radius}, lock {z.H.LockAt}, resolve {z.H.ResolveAt} (want {Sim.Ticks(secs)})");
                V2Check($"{level}: Duration() equals the real resolve offset (spawn on Run's first tick)", z != null && z.H.ResolveAt - 1 == dur, z == null ? "" : $"resolve offset {z.H.ResolveAt - 1}, duration {dur}");
                V2Check($"{level}: the player inside the safe disc is not hit", !V2WasHit(V2Main));
            }

            // 3 + 4: standing still at 8 m (ranged), at the rim and just outside the disc is hit once, for 45; next to his body is safe
            yield return V2Setup(4, Difficulty.Medium, new[] { new Vector3(0, 0, 8f), new Vector3(13.4f, 0, 0), new Vector3(0, 0, -5.0f), new Vector3(-1.8f, 0, 0) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V2Fire(V2Boss.Zenith.Run(), 10);
            V2Check("N = 4 uses the 5.5 m disc", V2Spawned("Zenith Collapse").FirstOrDefault()?.H.Inner == 5.5f, V2Spawned("Zenith Collapse").FirstOrDefault()?.H.Inner.ToString());
            V2Check("standing still at 8 m (ranged) is hit for 45", V2HitsOn(V2Party[0], "Zenith").Sum(h => h.Damage) == 45, string.Join(", ", V2HitsOn(V2Party[0]).Select(h => h.Source + " " + h.Damage)));
            V2Check("standing still at the rim (13.4 m) is hit", V2WasHit(V2Party[1], "Zenith"));
            V2Check("standing still 5.0 m out (inside the 5.5 m disc, N = 4) is not hit", !V2WasHit(V2Party[2], "Zenith"));
            V2Check("standing still next to his body is safe from the Collapse itself", !V2WasHit(V2Party[3], "Zenith"));
            V2Check("one hit per instance", V2OneHitPerInstance());

            // 6: the disc grows with the party: 4.5 m (N 1-2), 5.5 m (N 3-4), 6.5 m (N >= 5), 5.0 m at N <= 2 in Phase 3
            foreach (var (n, ph, want) in new[] { (2, 1, 4.5f), (2, 3, 5.0f), (8, 1, 6.5f) })
            {
                var ring = V2Ring(n, n >= 5 ? 6.0f : 3.0f, Vector3.zero);
                yield return V2Setup(n, Difficulty.Medium, ring, ph, Vector3.zero);
                if (V2Boss == null) yield break;
                yield return V2Fire(V2Boss.Zenith.Run(), 10);
                var z = V2Spawned("Zenith Collapse").FirstOrDefault();
                V2Check($"N = {n}, Phase {ph}: safe disc {want} m and the party standing inside it ({(n >= 5 ? 6.0f : 3.0f)} m out) is not hit",
                    z != null && Mathf.Approximately(z.H.Inner, want) && V2Hits.Count == 0, $"inner {z?.H.Inner}, hits {V2Hits.Count}");
            }

            // 2: escape from the rim with finite stamina: Medium walking at 4.2 m/s; Hard sprinting, and walking + one dash
            foreach (var (level, mode) in new[] { (Difficulty.Medium, "walk"), (Difficulty.Hard, "sprint"), (Difficulty.Hard, "walk + dash"), (Difficulty.Medium, "6 m/s walk") })
            {
                yield return V2Setup(1, level, new[] { new Vector3(0, 0, 13.4f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                V2Begin(V2Boss.Zenith.Run());
                yield return V2Ticks(ReactTicks);
                var inp = V2In(V2Main); float lowest = V2Main.Stamina;
                if (mode == "sprint") { inp.Sprint = true; inp.Go(new Vector3(0, 0, 2.8f), PlayerController.SprintSpeed); }
                else { inp.Go(new Vector3(0, 0, 2.8f), mode == "6 m/s walk" ? DesignWalk : ProtoWalk); if (mode == "walk + dash") inp.Dash(); }
                while (V2Boss != null && V2Boss.TestBusy) { lowest = Mathf.Min(lowest, V2Main.Stamina); if (V2Main.transform.position.z < 3.2f) inp.Sprint = false; yield return null; }
                inp.Stop();
                V2Check($"{level}: from the rim (13.4 m), {mode}{(mode.Contains("6") ? "" : " at 4.2 m/s")} after a 0.25 s reaction reaches the disc in time", !V2WasHit(V2Main),
                    $"ended {Sim.Flat(V2Main.transform.position).magnitude:0.0} m out, lowest stamina {lowest:0}");
            }

            // the pairing (§3): Lances resolve 0.3 s after the Collapse; solo they aim at you, so the disc is no refuge for
            // anyone standing still; a rim runner sprints in, then steps into the beam gap after the beams lock
            foreach (var level in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(2, level, new[] { new Vector3(0, 0, 13.4f), new Vector3(0, 0, -1.8f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                V2Begin(V2Boss.ZenithBlock(3));
                yield return V2Ticks(ReactTicks);
                var inp = V2In(V2Main); inp.Sprint = true; inp.Go(new Vector3(0, 0, 3.5f), PlayerController.SprintSpeed);
                // the beams track the farthest player (the runner) until they lock; then step 30° round onto the gap's midline
                yield return Until(() => V2W.Hazards.Live.Any(h => h.Source.StartsWith("Meridian Lances") && h.Locked) || !V2Boss.TestBusy, 10, "Aurelion v2: pair lances locked");
                yield return V2Ticks(ReactTicks);
                inp.Sprint = false; inp.Go(Sim.Dir(30f) * 3.5f, ProtoWalk);
                yield return V2WaitIdle(10);
                inp.Stop();
                var z = V2Spawned("Zenith Collapse").FirstOrDefault(); var l = V2Spawned("Meridian Lances").FirstOrDefault();
                int off = z == null || l == null ? int.MinValue : (l.Tick + l.H.ResolveAt) - (z.Tick + z.H.ResolveAt);
                V2Check($"{level} pair: Lances resolve 0.3 s after Zenith Collapse", off == Sim.Ticks(AurelionEncounter.LancesAfterZenith), off + " ticks");
                V2Check($"{level} pair: the rim runner (sprint in, then a 4.2 m/s step into the beam gap) is never hit", !V2WasHit(V2Main),
                    string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)) + $"; ended at ({V2Main.transform.position.x:0.0},{V2Main.transform.position.z:0.0})");
                V2Check($"{level} pair: a melee player standing still next to him is hit by the Lances", V2WasHit(V2Party[1], "Meridian Lances"));
            }

            // the block walks him to the arena centre before the telegraph appears
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 3f) }, 1, new Vector3(0, 0, -6f));
            if (V2Boss == null) yield break;
            int t0 = V2W.Tick;
            yield return V2Fire(V2Boss.ZenithBlock(3), 20);
            {
                var z = V2Spawned("Zenith Collapse").FirstOrDefault();
                V2Check("from 6 m out he walks to the centre first, and the disc is centred there", z != null && Sim.Flat(z.H.Origin).magnitude <= 0.35f && z.Tick - t0 > 60,
                    z == null ? "no spawn" : $"origin ({z.H.Origin.x:0.00},{z.H.Origin.z:0.00}) at tick +{z.Tick - t0}");
            }

            // 7 (last: it ends the practice): boss death mid-windup cancels the Collapse on the same tick
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 10f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Begin(V2Boss.Zenith.Run());
            yield return V2Ticks(60);
            int live = V2W.Hazards.Count, resolved0 = V2W.Hazards.Resolved;
            V2KillBoss();
            V2Check("boss death mid-windup cancels the Collapse on the same tick", live > 0 && V2W.Hazards.Count == 0, $"live {live} > {V2W.Hazards.Count}");
            yield return Wait(3.5f);
            V2Check("nothing resolves or hits after his death", (V2W == null || V2W.Hazards.Resolved == resolved0) && !V2WasHit(V2Main));
            yield return Until(() => d.State == RunState.Results, 10, "Aurelion v2: results after the Zenith death check");
        }
    }
}
