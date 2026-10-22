using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Hour Hands (aurelion.hour_hands) checks, owned by the Hour Hands session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/05-hour_hands.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    // Every case puts the boss on the arena centre, so a player's hour is simply their bearing from the origin
    // (0° = +z). Spawn / lock / resolve ticks come from delegates hooked onto each spoke as it spawns: the last Track
    // call is the lock tick (HazardSystem.Step locks after it), OnResolve is the resolve tick.
    public sealed partial class RunAutoTest
    {
        readonly Dictionary<Hazard, int> hhSpawn = new Dictionary<Hazard, int>(), hhLock = new Dictionary<Hazard, int>(), hhResolve = new Dictionary<Hazard, int>();
        readonly List<Hazard> hhSpokes = new List<Hazard>();
        HazardSystem hhHooked; Action<Hazard> hhHandler;
        bool hhOneHit = true;

        static bool HHIs(Hazard h) => h.Source.StartsWith("Hour Hands");
        List<Hazard> HHLive => hhSpokes.Where(h => !h.Cancelled).ToList();   // spokes that were not withdrawn as duplicates
        static int HHHour(Hazard h) => Mathf.RoundToInt(Mathf.Repeat(h.Yaw, 360) / HourHands.HourDeg) % 12;
        string HHDesc() => string.Join("; ", hhSpokes.Select(h => $"{h.Source} #{h.Id} yaw {h.Yaw:0}{(h.Cancelled ? " (shared, withdrawn)" : "")} spawn {Get(hhSpawn, h)} lock {Get(hhLock, h)} resolve {Get(hhResolve, h)}"));
        static int Get(Dictionary<Hazard, int> m, Hazard h) => m.TryGetValue(h, out int t) ? t : -1;

        // fresh combat (boss at the centre) and a clean spoke log hooked onto this world's hazard system
        IEnumerator HHSetup(int n, Difficulty level, Vector3[] at)
        {
            if (V2Boss != null && hhSpokes.Count > 0) hhOneHit &= V2OneHitPerInstance();   // the previous Hour Hands case
            yield return V2Setup(n, level, at, 2, Vector3.zero);
            hhSpawn.Clear(); hhLock.Clear(); hhResolve.Clear(); hhSpokes.Clear();
            if (V2Boss == null) yield break;
            if (hhHooked != null && hhHandler != null) hhHooked.Spawned -= hhHandler;
            hhHooked = V2W.Hazards; var w = V2W;
            hhHandler = h =>
            {
                if (!HHIs(h)) return;
                hhSpokes.Add(h); hhSpawn[h] = w.Tick;
                h.Track += x => hhLock[x] = w.Tick;
                h.OnResolve += x => hhResolve[x] = w.Tick;
            };
            hhHooked.Spawned += hhHandler;
        }

        // the point 1.6 m off the spoke's centre line (clear of its 0.9 m half-width plus the 0.3 m margin), level with p
        static Vector3 HHSide(Hazard h, Vector3 pos, float lateral = 1.6f)
        {
            var l = Quaternion.Euler(0, -h.Yaw, 0) * Sim.Flat(pos - h.Origin);
            float side = l.x >= 0 ? 1 : -1;
            return h.Origin + Quaternion.Euler(0, h.Yaw, 0) * new Vector3(side * lateral, 0, l.z);
        }

        // Stands still until a locked spoke it is inside is `delay` ticks past its lock, then walks sideways out of it at
        // `speed` (no dash: stamina is never spent). Runs until the ability ends.
        IEnumerator HHDodger(PlayerController p, int delay, float speed)
        {
            var seen = new HashSet<Hazard>();
            while (V2Boss != null && V2Boss.TestBusy && p)
            {
                foreach (var h in hhSpokes)
                {
                    if (h.Cancelled || seen.Contains(h) || !h.Locked || hhResolve.ContainsKey(h) || V2W.Tick - Get(hhLock, h) < delay) continue;
                    seen.Add(h);
                    if (h.Contains(p.transform.position)) V2In(p).Go(HHSide(h, p.transform.position), speed);
                }
                yield return null;
            }
        }

        IEnumerator HHRunWithDodgers(IEnumerable<PlayerController> who, int delay, float speed)
        {
            V2Begin(V2Boss.Hands.Run());
            var cs = who.Select(p => StartCoroutine(HHDodger(p, delay, speed))).ToList();
            yield return V2WaitIdle();
            foreach (var c in cs) StopCoroutine(c);
        }

        IEnumerator V2HourHands()
        {
            hhOneHit = true; hhSpokes.Clear();
            int lockT = Sim.Ticks(HourHands.TrackS), resolveT = Sim.Ticks(HourHands.TrackS) + Sim.Ticks(HourHands.StrikeS);

            // ---- 1 + 6 (solo): ticks, shape, tracking, spoke II on spoke I's lock tick ----
            yield return HHSetup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 5) });
            if (V2Boss == null) yield break;
            {
                var p = V2Party[0];
                V2Begin(V2Boss.Hands.Run());
                V2In(p).Go(new Vector3(4, 0, 3));                 // hour 0 → bearing 53°, hour 2 (60°) while spoke I tracks
                yield return Until(() => hhSpokes.Count > 0 && hhSpokes[0].Locked, 5, "Hour Hands: spoke I locks");
                V2In(p).Go(new Vector3(0, 0, 5));                 // out of spoke I, back to hour 0 while spoke II tracks
                yield return V2WaitIdle();
                int end = V2W.Tick;
                var s1 = hhSpokes.FirstOrDefault(); var s2 = hhSpokes.Skip(1).FirstOrDefault();
                V2Check("solo: exactly two spokes (I, then II)", hhSpokes.Count == 2 && s1.Source.EndsWith(" I") && s2.Source.EndsWith(" II"), HHDesc());
                if (s1 != null)
                {
                    V2Check("spoke is a 1.8 m × 28 m Strike lane from his feet, 30 damage, 0.25 s stun",
                        s1.Shape == HazardShape.Lane && s1.Kind == HazardKind.Strike && Mathf.Approximately(s1.Width, 1.8f) && Mathf.Approximately(s1.Length, 28f)
                        && s1.Damage == 30 && s1.Stun == 15 && Mathf.Approximately(s1.Knockback, 1.5f) && Sim.Flat(s1.Origin).magnitude < 0.01f,
                        $"{s1.Shape} {s1.Kind} {s1.Width}×{s1.Length} dmg {s1.Damage} stun {s1.Stun} kb {s1.Knockback} origin {s1.Origin}");
                    int sp = Get(hhSpawn, s1);
                    V2Check("Medium: spoke I tracks 1.5 s (locks on its 90th tick) and resolves 1.0 s later (150th tick)",
                        s1.LockAt == 90 && s1.ResolveAt == 150 && Get(hhLock, s1) - sp == lockT - 1 && Get(hhResolve, s1) - sp == resolveT - 1, HHDesc());
                    V2Check("spoke I locked on the hour the player had reached (60°)", HHHour(s1) == 2, $"yaw {s1.Yaw}");
                }
                if (s1 != null && s2 != null)
                {
                    V2Check("solo: spoke II spawns on spoke I's lock tick", Get(hhSpawn, s2) == Get(hhLock, s1), HHDesc());
                    V2Check("solo: spoke II tracks the new hour (0°) and resolves 2.5 s after it spawns",
                        HHHour(s2) == 0 && Get(hhResolve, s2) - Get(hhSpawn, s2) == resolveT - 1, HHDesc());
                    V2Check("solo: the hands strike twice in 4.0 s (spoke II resolves on tick 240 of the sequence)",
                        Get(hhResolve, s2) - Get(hhSpawn, s1) == lockT + resolveT - 2, $"{Get(hhResolve, s2) - Get(hhSpawn, s1)} ticks after spoke I spawned");
                    V2Check("Run returns on the tick the last spoke resolves", end - Get(hhResolve, s2) >= 0 && end - Get(hhResolve, s2) <= 2, $"returned {end - Get(hhResolve, s2)} ticks after");
                    V2Check("player who left spoke I's hour and stood on spoke II is hit by II only, once",
                        !V2HitsOn(p).Any(h => h.H == s1) && V2HitsOn(p).Count(h => h.H == s2) == 1, string.Join(", ", V2HitsOn(p).Select(h => h.Source)));
                }
            }

            // ---- 3: standing still, solo, is hit by both hands ----
            // At 3 m: spoke I's 1.5 m sideways knockback lands mid-tracking of spoke II, and beyond ~4.6 m that can leave the
            // player between two hours (snapping to 30° is up to r·sin 15° off the centre line), outside spoke II's lane.
            yield return HHSetup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 3) });
            if (V2Boss == null) yield break;
            {
                var p = V2Party[0];
                yield return V2Fire(V2Boss.Hands.Run());
                var hits = V2HitsOn(p, "Hour Hands").ToList();
                V2Check("solo standing still: hit by spoke I and spoke II, once each", hits.Count == 2 && hits.Select(h => h.H.Id).Distinct().Count() == 2,
                    string.Join(", ", hits.Select(h => $"{h.Source} {h.Damage} @{h.Tick}")));
                V2Check("Hard: a spoke hit deals 30 × 1.3", hits.Count > 0 && hits.All(h => h.Damage == Mathf.RoundToInt(30 * RunDifficulty.DamageTakenScale(Difficulty.Hard))),
                    string.Join(", ", hits.Select(h => h.Damage)));
            }

            // ---- 4: melee at his feet, 8.5 m out and at the rim, standing still: all hit ----
            float rimPos = AurelionAbility.Rim - 0.5f;
            yield return HHSetup(3, Difficulty.Hard, new[] { new Vector3(0, 0, 1.2f), new Vector3(8.5f, 0, 0), new Vector3(0, 0, -rimPos) });
            if (V2Boss == null) yield break;
            {
                yield return V2Fire(V2Boss.Hands.Run());
                var names = new[] { "melee at 1.2 m", "ranged at 8.5 m", $"rim at {rimPos:0.0} m" };
                for (int i = 0; i < 3; i++)
                {
                    var p = V2Party[i];
                    V2Check($"standing still, {names[i]}: hit once", V2HitsOn(p, "Hour Hands").Count() == 1, $"at {Sim.Flat(p.transform.position)}; {HHDesc()}");
                }
                V2Check("N = 3 on three hours: three spokes", HHLive.Count == 3 && HHLive.Select(HHHour).Distinct().Count() == 3, HHDesc());
            }

            // ---- 2: the escape route, 0.25 s after the lock, walking only ----
            foreach (var speed in new[] { 4.2f, 6f })
            {
                yield return HHSetup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 5) });
                if (V2Boss == null) yield break;
                var p = V2Party[0];
                float stamina = p.Stamina;
                yield return HHRunWithDodgers(new[] { p }, Sim.Ticks(0.25f), speed);
                V2Check($"solo at {speed} m/s: a 1.2 m sidestep 0.25 s after each lock dodges both hands (walk only)",
                    !V2WasHit(p, "Hour Hands") && hhSpokes.Count == 2 && p.Stamina >= stamina - 0.01f, $"hits {V2HitsOn(p, "Hour Hands").Count()}, stamina {stamina:0}>{p.Stamina:0}; {HHDesc()}");
            }
            foreach (var n in new[] { 2, 4 })
            {
                yield return HHSetup(n, Difficulty.Hard, V2Ring(n, 6f));
                if (V2Boss == null) yield break;
                yield return HHRunWithDodgers(V2Party.ToList(), Sim.Ticks(0.25f), 4.2f);
                V2Check($"N = {n} spread round him at 6 m: everyone sidesteps their own spoke at 4.2 m/s",
                    V2Party.All(p => !V2WasHit(p, "Hour Hands")), $"hit: {V2Hits.Count(h => HHIs(h.H))}; {HHDesc()}");
            }

            // ---- 6 (co-op): sharing an hour, N = 2 / 4 / 8 ----
            yield return HHSetup(2, Difficulty.Hard, new[] { new Vector3(-0.3f, 0, 5), new Vector3(0.3f, 0, 7) });
            if (V2Boss == null) yield break;
            {
                yield return V2Fire(V2Boss.Hands.Run());
                var a = V2HitsOn(V2Party[0], "Hour Hands").ToList(); var b = V2HitsOn(V2Party[1], "Hour Hands").ToList();
                V2Check("N = 2 on the same hour: one shared spoke hits each of them once",
                    HHLive.Count == 1 && a.Count == 1 && b.Count == 1 && a[0].H == b[0].H, HHDesc());
            }
            yield return HHSetup(2, Difficulty.Hard, new[] { new Vector3(0, 0, 5), new Vector3(5, 0, 0) });
            if (V2Boss == null) yield break;
            {
                yield return V2Fire(V2Boss.Hands.Run());
                V2Check("N = 2 on two hours: two spokes, each player hit once by their own",
                    HHLive.Count == 2 && V2Party.All(p => V2HitsOn(p, "Hour Hands").Count() == 1) && V2HitsOn(V2Party[0], "Hour Hands").First().H != V2HitsOn(V2Party[1], "Hour Hands").First().H, HHDesc());
            }
            foreach (var n in new[] { 4, 8 })
            {
                yield return HHSetup(n, Difficulty.Hard, V2Ring(n, 6f, null, 7));
                if (V2Boss == null) yield break;
                yield return V2Fire(V2Boss.Hands.Run());
                V2Check($"N = {n} spread round him, standing still: {n} spokes on {n} hours, everyone hit once",
                    HHLive.Count == n && HHLive.Select(HHHour).Distinct().Count() == n && V2Party.All(p => V2HitsOn(p, "Hour Hands").Count() == 1),
                    $"hits {V2Hits.Count(h => HHIs(h.H))}; {HHDesc()}");
            }
            // 8 crowded onto 3 hours: the spokes collapse to one per hour
            yield return HHSetup(8, Difficulty.Hard, Enumerable.Range(0, 8).Select(i => Sim.Dir((i % 3) * 120f + (i / 3 - 1) * 4f) * (4f + i * 0.4f)).ToArray());
            if (V2Boss == null) yield break;
            {
                yield return V2Fire(V2Boss.Hands.Run());
                V2Check("N = 8 crowded onto 3 hours: 3 spokes, everyone hit once",
                    HHLive.Count == 3 && V2Party.All(p => V2HitsOn(p, "Hour Hands").Count() == 1), $"hits {V2Hits.Count(h => HHIs(h.H))}; {HHDesc()}");
            }

            // ---- adjacent hours at 3 m: the sidestep toward the neighbour lands in their spoke unless you step out first ----
            var nb = Sim.Dir(30) * 3f;
            yield return HHSetup(2, Difficulty.Hard, new[] { new Vector3(0, 0, 3), nb });
            if (V2Boss == null) yield break;
            {
                var a = V2Party[0];
                V2Begin(V2Boss.Hands.Run());
                yield return Until(() => HHLive.Count == 2 && HHLive.All(h => h.Locked), 5, "Hour Hands: adjacent spokes lock");
                yield return V2Ticks(Sim.Ticks(0.25f));
                V2In(a).Go(new Vector3(1.6f, 0, 3));
                yield return V2WaitIdle();
                var hits = V2HitsOn(a, "Hour Hands").ToList();
                V2Check("adjacent hours at 3 m: a 1.2 m sidestep toward the neighbour is caught by the neighbour's spoke",
                    hits.Count == 1 && HHHour(hits[0].H) == 1, string.Join(", ", hits.Select(h => $"yaw {h.H.Yaw:0}")) + "; " + HHDesc());
            }
            yield return HHSetup(2, Difficulty.Hard, new[] { new Vector3(0, 0, 3), nb });
            if (V2Boss == null) yield break;
            {
                var a = V2Party[0];
                V2Begin(V2Boss.Hands.Run());
                V2In(a).Go(new Vector3(0, 0, 6));                 // outward along the own hour while the spokes track
                yield return Until(() => HHLive.Count == 2 && HHLive.All(h => h.Locked), 5, "Hour Hands: adjacent spokes lock");
                yield return V2Ticks(Sim.Ticks(0.25f));
                V2In(a).Go(new Vector3(1.6f, 0, 6));
                yield return V2WaitIdle();
                V2Check("adjacent hours: stepping out to 6 m first opens the gap, the same sidestep is clean",
                    !V2WasHit(a, "Hour Hands") && V2HitsOn(V2Party[1], "Hour Hands").Count() == 1, HHDesc());
            }

            // ---- 8: Easy and Hard ----
            foreach (var level in new[] { Difficulty.Easy, Difficulty.Hard })
            {
                yield return HHSetup(1, level, new[] { new Vector3(0, 0, 5) });
                if (V2Boss == null) yield break;
                yield return V2Fire(V2Boss.Hands.Run());
                int wantLock = Sim.Ticks(HourHands.TrackS * (level == Difficulty.Easy ? 1.25f : 1)), wantRes = wantLock + Sim.Ticks(HourHands.StrikeS * (level == Difficulty.Easy ? 1.25f : 1));
                var s = hhSpokes.FirstOrDefault();
                V2Check($"{level}: track {wantLock} ticks, resolve at {wantRes} (Easy × 1.25 on track and window)",
                    s != null && s.LockAt == wantLock && s.ResolveAt == wantRes && Get(hhResolve, s) - Get(hhSpawn, s) == wantRes - 1, HHDesc());
            }
            Note("Hour Hands: Hard only in the fight itself — the phase plan check covers 'never runs' on Medium and 'runs inside the Horizon' on Hard");

            // ---- 5: one hit per instance, across every case above ----
            hhOneHit &= V2OneHitPerInstance();
            V2Check("one hit per spoke instance in every case", hhOneHit);

            // ---- 7: boss death mid-windup cancels the spokes in the same tick (last: it ends the practice) ----
            yield return HHSetup(2, Difficulty.Hard, new[] { new Vector3(0, 0, 5), new Vector3(5, 0, 0) });
            if (V2Boss == null) yield break;
            {
                var w = V2W;
                V2Begin(V2Boss.Hands.Run());
                yield return Until(() => hhSpokes.Count == 2, 5, "Hour Hands: spokes up");
                yield return V2Ticks(40);
                int killTick = w.Tick;
                V2KillBoss();
                bool cleared = !w.Hazards.Live.Any(HHIs);
                float t = 0; while (t < 3f) { t += Time.deltaTime; yield return null; }
                V2Check("boss death mid-windup cancels every spoke on the killing tick, and none ever hits",
                    cleared && !hhResolve.Any() && !V2Hits.Any(h => HHIs(h.H)), $"killed on {killTick}; live after kill: {!cleared}; {HHDesc()}");
            }
            if (hhHooked != null && hhHandler != null) hhHooked.Spawned -= hhHandler;
            hhHooked = null; hhHandler = null;
        }
    }
}
