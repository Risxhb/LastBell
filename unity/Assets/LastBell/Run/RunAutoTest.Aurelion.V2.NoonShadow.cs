using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Noon Shadow (aurelion.noon_shadow) checks, owned by the Noon Shadow session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/03-noon_shadow.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    // Yaw 90 = east (+x), 270 = west. Runners cross along z = ±1 with the boss parked at (0, 0, -8), clear of the route.
    public sealed partial class RunAutoTest
    {
        const string NoonSrc = "Noon Shadow";
        static readonly Vector3 NoonBossAside = new Vector3(0, 0, -8);
        static bool NoonYaw(float got, float want) => Mathf.Abs(Mathf.DeltaAngle(got, want)) < 1f;

        IEnumerator V2NoonShadow()
        {
            int win = Sim.Ticks(NoonShadow.WindowS);

            // 1 · solo timing: Noon on the player's half, locked at once, resolving 228 ticks after it appears (the spawn
            // tick counts as tick 1); Afternoon on the other half warns on Noon's resolve tick; a player standing still is hit
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(6, 0, 0) }, 1, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon; var p = V2Party[0];
                yield return V2Fire(a.Run(false));
                V2Check("solo: Noon and Afternoon (two halves)", a.Halves.Count == 2, $"{a.Halves.Count} halves");
                if (a.Halves.Count == 2)
                {
                    var noon = a.Halves[0]; var aft = a.Halves[1];
                    V2Check("Noon lights the player's half (east)", NoonYaw(a.LitYaws[0], 90), $"yaw {a.LitYaws[0]:0.0}");
                    V2Check("Noon is a Ground half-arena cone from the centre", noon.Shape == HazardShape.Cone && noon.Kind == HazardKind.Ground && noon.HalfAngle == 90 && noon.Origin == Vector3.zero && noon.Radius >= CombatWorld.PlayerBound,
                        $"{noon.Shape} {noon.Kind} half {noon.HalfAngle} r {noon.Radius} at {noon.Origin}");
                    V2Check("the side is locked at the warning start (no tracking)", noon.LockAt == 0 && noon.Track == null && aft.LockAt == 0 && aft.Track == null, $"lock {noon.LockAt}/{aft.LockAt}");
                    V2Check("Noon resolves 3.8 s (228 ticks) after it appears", noon.ResolveAt == win && a.ResolveTicks.Count == 2 && a.ResolveTicks[0] - a.SpawnTicks[0] == win - 1,
                        $"ResolveAt {noon.ResolveAt}, spawn {a.SpawnTicks[0]}, resolve {(a.ResolveTicks.Count > 0 ? a.ResolveTicks[0] : -1)}");
                    V2Check("Afternoon warns on Noon's resolve tick", a.ResolveTicks.Count == 2 && a.SpawnTicks[1] == a.ResolveTicks[0], $"afternoon spawn {a.SpawnTicks[1]}, noon resolve {(a.ResolveTicks.Count > 0 ? a.ResolveTicks[0] : -1)}");
                    V2Check("Afternoon lights the other half", NoonYaw(a.LitYaws[1], a.LitYaws[0] + 180), $"{a.LitYaws[0]:0} then {a.LitYaws[1]:0}");
                    V2Check("Afternoon resolves 3.8 s after it appears", aft.ResolveAt == win && a.ResolveTicks.Count == 2 && a.ResolveTicks[1] - a.SpawnTicks[1] == win - 1, $"ResolveAt {aft.ResolveAt}");
                    var hits = V2HitsOn(p, NoonSrc).ToList();
                    int want = Mathf.RoundToInt(NoonShadow.Damage * V2W.DamageTakenScale);
                    V2Check("a player standing still on the lit half is hit by Noon (45), not by Afternoon", hits.Count == 1 && hits[0].H == noon && hits[0].Damage == want,
                        string.Join(", ", hits.Select(h => $"{(h.H == noon ? "noon" : "afternoon")} {h.Damage}")));
                    V2Check("no knockback, 0.25 s stun", noon.Knockback == 0 && noon.Stun == Sim.Ticks(0.25f), $"knock {noon.Knockback}, stun {noon.Stun}");
                }
                V2Check("one hit per instance", V2OneHitPerInstance());
            }

            // 2 · N = 2, same side: one next to the boss and one at long range on the rim; both hit if they don't move.
            // Two players means no Afternoon outside Phase 3.
            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(4.8f, 0, 0.6f), new Vector3(13.5f, 0, 0) }, 1, new Vector3(3, 0, 0));
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(false));
                V2Check("N = 2: Noon only (no Afternoon outside Phase 3)", a.Halves.Count == 1, $"{a.Halves.Count} halves");
                V2Check("N = 2 same side: that half is lit", a.LitYaws.Count > 0 && NoonYaw(a.LitYaws[0], 90), a.LitYaws.Count > 0 ? $"yaw {a.LitYaws[0]:0.0}" : "none");
                V2Check("melee next to the boss who doesn't move is hit", V2WasHit(V2Party[0], NoonSrc));
                V2Check("ranged on the rim (13.5 m) who doesn't move is hit", V2WasHit(V2Party[1], NoonSrc));
                V2Check("one hit per instance", V2OneHitPerInstance());
            }

            // 3 · the escape: both on the far lit edge (13.5 m), reacting after 0.25 s. At 4.2 m/s one walks only (0.22 s
            // to spare) and one walks plus one dash; at 6 m/s both walk. Neither is hit; the dash costs one dash of stamina.
            foreach (var speed in new[] { 4.2f, 6f })
            {
                yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(13.5f, 0, 1), new Vector3(13.5f, 0, -1) }, 1, NoonBossAside);
                if (V2Boss == null) yield break;
                var a = V2Boss.Noon; var walker = V2Party[0]; var dasher = V2Party[1];
                V2Begin(a.Run(false));
                yield return V2Ticks(Sim.Ticks(0.25f));
                V2In(walker).Go(new Vector3(-2, 0, 1), speed);
                V2In(dasher).Go(new Vector3(-2, 0, -1), speed);
                bool dash = speed < 5;
                if (dash) { yield return V2Ticks(Sim.Ticks(0.5f)); V2In(dasher).Dash(); }
                float minStamina = PlayerController.MaxStamina;
                while (V2Boss != null && V2Boss.TestBusy) { minStamina = Mathf.Min(minStamina, dasher.Stamina); yield return null; }
                V2Check($"{speed} m/s: Noon lit the start edge (east)", a.LitYaws.Count > 0 && NoonYaw(a.LitYaws[0], 90));
                V2Check($"{speed} m/s: the walker crosses in time", !V2WasHit(walker, NoonSrc), $"x {walker.transform.position.x:0.00}");
                V2Check($"{speed} m/s: the {(dash ? "walk + one dash" : "second walker")} crosses in time", !V2WasHit(dasher, NoonSrc), $"x {dasher.transform.position.x:0.00}");
                if (dash) V2Check("the dash costs one dash of stamina, no more", minStamina <= PlayerController.MaxStamina - PlayerController.DashCost + 0.5f && minStamina >= PlayerController.MaxStamina - PlayerController.DashCost - 0.5f,
                    $"lowest stamina {minStamina:0.0}");
            }

            // 4 · solo escape: the edge runner crosses with one dash, then walks back across for Afternoon
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(13.5f, 0, 1) }, 1, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon; var p = V2Party[0];
                V2Begin(a.Run(false));
                yield return V2Ticks(Sim.Ticks(0.25f));
                V2In(p).Go(new Vector3(-3, 0, 1));
                yield return V2Ticks(Sim.Ticks(0.5f)); V2In(p).Dash();
                yield return Until(() => a.Halves.Count >= 2 || V2Boss == null || !V2Boss.TestBusy, 10, "Noon Shadow: Afternoon");
                bool aftWest = a.LitYaws.Count == 2 && NoonYaw(a.LitYaws[1], 270);
                yield return V2Ticks(Sim.Ticks(0.25f));
                V2In(p).Go(new Vector3(3, 0, 1));
                yield return V2WaitIdle();
                V2Check("solo: Afternoon lights the far (west) half", aftWest, string.Join(", ", a.LitYaws.Select(y => y.ToString("0"))));
                V2Check("solo: the edge runner clears Noon and Afternoon at 4.2 m/s with one dash", !V2WasHit(p, NoonSrc), $"hits {V2HitsOn(p, NoonSrc).Count()}");
            }

            // 5 · the N rules: the 2-player tie, 3 of 4 on one side, 8 players (Phase 2), Afternoon for everyone in Phase 3
            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(6, 0, 0), new Vector3(-6.5f, 0, 0) }, 1, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(false));
                V2Check("N = 2 tie (mean within 1 m): the half holding the farther player is lit", a.LitYaws.Count == 1 && NoonYaw(a.LitYaws[0], 270) && V2WasHit(V2Party[1], NoonSrc) && !V2WasHit(V2Party[0], NoonSrc),
                    a.LitYaws.Count > 0 ? $"yaw {a.LitYaws[0]:0.0}" : "none");
            }
            yield return V2Setup(4, Difficulty.Medium, new[] { new Vector3(5, 0, 2), new Vector3(6, 0, -2), new Vector3(4, 0, 0.5f), new Vector3(-10, 0, 0) }, 1, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(false));
                int hit = V2Party.Take(3).Count(p => V2WasHit(p, NoonSrc));
                V2Check("N = 4, 3 on one side: that side is lit", a.LitYaws.Count == 1 && NoonYaw(a.LitYaws[0], 90) && hit == 3 && !V2WasHit(V2Party[3], NoonSrc),
                    $"{a.Halves.Count} halves, yaw {(a.LitYaws.Count > 0 ? a.LitYaws[0] : -1):0.0}, {hit}/3 majority hit");
            }
            {
                var spots = V2Ring(6, 3, new Vector3(0, 0, 7)).Concat(new[] { new Vector3(-3, 0, -11), new Vector3(3, 0, -11) }).ToArray();
                yield return V2Setup(8, Difficulty.Medium, spots, 2, new Vector3(6, 0, 0));
                if (V2Boss == null) yield break;
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(false));
                int hit = V2Party.Take(6).Count(p => V2WasHit(p, NoonSrc));
                V2Check("N = 8 (Phase 2), 6 south and 2 north: the south half is lit, Noon only", a.Halves.Count == 1 && a.LitYaws.Count == 1 && NoonYaw(a.LitYaws[0], 0) && hit == 6 && !V2WasHit(V2Party[6], NoonSrc) && !V2WasHit(V2Party[7], NoonSrc),
                    $"{a.Halves.Count} halves, yaw {(a.LitYaws.Count > 0 ? a.LitYaws[0] : -1):0.0}, {hit}/6 hit");
                V2Check("one hit per instance (8 players)", V2OneHitPerInstance());
            }
            yield return V2Setup(4, Difficulty.Medium, V2Ring(4, 1.5f, new Vector3(0, 0, 6)), 3, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(true));
                int both = V2Party.Count(p => V2HitsOn(p, NoonSrc).Count() == 1);
                V2Check("Phase 3 (N = 4): Noon and Afternoon for everyone", a.Halves.Count == 2 && a.SpawnTicks[1] == a.ResolveTicks[0] && NoonYaw(a.LitYaws[1], a.LitYaws[0] + 180), $"{a.Halves.Count} halves");
                V2Check("Phase 3: players standing still are hit once (Noon), never by both", both == 4 && V2OneHitPerInstance(), $"{both}/4 hit exactly once");
            }

            // 6 · boss death mid-windup clears the half in the killing tick; nothing resolves or hits afterwards
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(6, 0, 0) }, 1, NoonBossAside);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss.Noon; var p = V2Party[0];
                V2Begin(a.Run(false));
                yield return V2Ticks(Sim.Ticks(1.5f));
                bool liveBefore = a.Halves.Count == 1 && V2W.Hazards.Live.Contains(a.Halves[0]);
                var w = V2W;
                V2KillBoss();
                bool clearedNow = a.Halves.Count == 1 && !w.Hazards.Live.Contains(a.Halves[0]);
                float t = 0; while (t < 4.5f) { t += Time.deltaTime; yield return null; }
                V2Check("boss death mid-windup cancels the half on the same tick", liveBefore && clearedNow, $"live before {liveBefore}, cleared {clearedNow}");
                V2Check("the cancelled half never resolves or hits", a.ResolveTicks.Count == 0 && !V2WasHit(p, NoonSrc), $"resolves {a.ResolveTicks.Count}, hits {V2HitsOn(p, NoonSrc).Count()}");
            }

            // 7 · Easy stretches both windows ×1.25 (285 ticks); Hard keeps Medium's 228 and the same 45
            foreach (var level in new[] { Difficulty.Easy, Difficulty.Hard })
            {
                yield return V2Setup(1, level, new[] { new Vector3(6, 0, 0) }, 1, NoonBossAside);
                if (V2Boss == null) yield break;
                var a = V2Boss.Noon;
                yield return V2Fire(a.Run(false));
                int want = Sim.Ticks(NoonShadow.WindowS * (level == Difficulty.Easy ? 1.25f : 1f));
                V2Check($"{level}: Noon and Afternoon each warn {want} ticks", a.Halves.Count == 2 && a.Halves.All(h => h.ResolveAt == want && h.Damage == NoonShadow.Damage) && a.SpawnTicks[1] == a.ResolveTicks[0],
                    string.Join(", ", a.Halves.Select(h => $"{h.ResolveAt} ticks / {h.Damage}")));
            }
        }
    }
}
