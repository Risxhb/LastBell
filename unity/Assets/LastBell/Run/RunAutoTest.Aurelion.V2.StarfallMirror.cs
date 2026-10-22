using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Starfall Mirror (aurelion.starfall_mirror) checks, owned by the Starfall Mirror session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/02-starfall_mirror.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    // Ages follow Hazard.Age: a hazard spawned on world tick s has age a on tick s + a - 1, locks at age LockAt and
    // resolves at age ResolveAt; "cast age" is the same count from the cast's first spawn.
    public sealed partial class RunAutoTest
    {
        const string SfName = "Starfall Mirror";

        sealed class SfTimes { public int Spawn = -1, LastTrack = -1, Resolve = -1; public int LockAge => LastTrack - Spawn + 1; public int ResolveAge => Resolve - Spawn + 1; }
        sealed class SfLog
        {
            public int Cast = -1, End = -1;
            public readonly Dictionary<Hazard, SfTimes> T = new Dictionary<Hazard, SfTimes>();
            public int CastAge(int tick) => tick - Cast + 1;
        }
        // a scripted move: from cast age `age`, player `p` walks to `to` at `speed` (and dashes once if `dash`)
        sealed class SfMove { public int Age; public PlayerController P; public Vector3 To; public float Speed = 4.2f; public bool Dash; }

        // Casts Starfall Mirror once on the boss's script and plays the scripted moves; records spawn, lock and resolve
        // ticks of every circle (by wrapping its Track and OnResolve) and the tick Run returned.
        IEnumerator SfRun(SfLog log, params SfMove[] moves)
        {
            Action<Hazard> hook = h =>
            {
                if (!h.Source.StartsWith(SfName)) return;
                var t = new SfTimes { Spawn = V2W.Tick }; log.T[h] = t;
                if (log.Cast < 0) log.Cast = V2W.Tick;
                var tr = h.Track; h.Track = hz => { t.LastTrack = V2W.Tick; tr?.Invoke(hz); };
                var rs = h.OnResolve; h.OnResolve = hz => { t.Resolve = V2W.Tick; rs?.Invoke(hz); };
            };
            var w = V2W; w.Hazards.Spawned += hook;
            V2Begin(SfTimed(V2Boss.Starfall.Run(), log));
            var pending = moves.ToList(); float guard = 0;
            while (V2Boss != null && V2Boss.TestBusy && guard < 20)
            {
                if (log.Cast >= 0)
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        var m = pending[i];
                        if (log.CastAge(w.Tick) < m.Age) continue;
                        V2In(m.P).Go(m.To, m.Speed); if (m.Dash) V2In(m.P).Dash();
                        pending.RemoveAt(i);
                    }
                guard += Time.deltaTime; yield return null;
            }
            w.Hazards.Spawned -= hook;
            foreach (var p in V2Party) V2In(p).Stop();
            yield return V2Ticks(2);
        }
        IEnumerable<int> SfTimed(IEnumerable<int> body, SfLog log) { foreach (var x in body) yield return x; log.End = V2W.Tick; }

        StarfallMirror Sf => V2Boss.Starfall;
        int SfHitCount(PlayerController p) => V2HitsOn(p, SfName).Count();
        bool SfHitBy(PlayerController p, Hazard h) => V2Hits.Any(x => x.P == p && x.H == h);
        StarfallMirror.Star SfOf(PlayerController p, int index = 1) => Sf.Stars.FirstOrDefault(s => s.Who == p && s.Index == index);
        static string SfPos(Vector3 v) => $"({v.x:0.0}, {v.z:0.0})";
        string SfHitText() => string.Join("; ", V2Hits.Where(h => h.Source.StartsWith(SfName)).Select(h => $"P{V2Party.IndexOf(h.P)} by {h.Source} {SfPos(h.H.Origin)} for {h.Damage}"));

        IEnumerator V2StarfallMirror()
        {
            int lockAt = Sim.Ticks(StarfallMirror.FollowS), resolveAt = lockAt + Sim.Ticks(StarfallMirror.WindowS);
            int stun = Sim.Ticks(StarfallMirror.StunS);

            // ---- 1. timing and shape: N = 2 standing still on their spots, each hit by their own star only ----
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, 4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                var hz = log.T.Keys.ToList();
                var stars = hz.Where(h => !h.Source.EndsWith(StarfallMirror.MirrorTag)).ToList();
                var mirrors = hz.Where(h => h.Source.EndsWith(StarfallMirror.MirrorTag)).ToList();
                V2Check("N = 2: 2 stars + 2 mirrors, all spawned on the cast tick", stars.Count == 2 && mirrors.Count == 2 && log.T.Values.All(t => t.Spawn == log.Cast),
                    $"{stars.Count} stars, {mirrors.Count} mirrors, spawn ticks {string.Join(",", log.T.Values.Select(t => t.Spawn - log.Cast))}");
                V2Check("circles: Ground, radius 2.2 m, 30 damage, no knockback, 0.25 s stun", hz.All(h => h.Shape == HazardShape.Circle && h.Kind == HazardKind.Ground
                    && Mathf.Approximately(h.Radius, 2.2f) && h.Damage == 30 && h.Knockback == 0 && h.Stun == stun),
                    string.Join("; ", hz.Select(h => $"{h.Shape} {h.Kind} r{h.Radius} d{h.Damage} kb{h.Knockback} st{h.Stun}")));
                V2Check($"stars and mirrors lock at age {lockAt} and resolve at {resolveAt} (2.0 s follow + 1.1 s window)",
                    log.T.Values.All(t => t.LockAge == lockAt && t.ResolveAge == resolveAt),
                    string.Join("; ", log.T.Select(kv => $"{kv.Key.Source} lock {kv.Value.LockAge} resolve {kv.Value.ResolveAge}")));
                V2Check($"Run returns {resolveAt} ticks after the cast, after the last resolve", log.End - log.Cast == resolveAt && log.T.Values.All(t => t.Resolve < log.End),
                    $"end {log.End - log.Cast}");
                V2Check("each star sits on its player; each mirror on the reflection through the centre",
                    Sf.Stars.Count == 2 && Sf.Stars.All(s => Sim.Flat(s.Main.Origin - s.Who.transform.position).magnitude < 0.05f
                        && (s.Mirror.Origin - StarfallMirror.Reflect(s.Main.Origin)).magnitude < 0.01f),
                    string.Join("; ", Sf.Stars.Select(s => $"star {SfPos(s.Main.Origin)} on {SfPos(s.Who.transform.position)}, mirror {SfPos(s.Mirror.Origin)}")));
                bool ownOnly = V2Party.All(p => SfHitCount(p) == 1 && SfHitBy(p, SfOf(p).Main) && V2HitsOn(p, SfName).All(h => h.Damage == 30));
                V2Check("standing still at the lock point: hit by your own star, once, for 30", ownOnly, SfHitText());
                V2Check("one hit per instance (timing run)", V2OneHitPerInstance());
            }

            // ---- 2. escape at 4.2 m/s: 0.25 s after the lock, walk 2.9 m away (clear of 2.2 + 0.3 m) ----
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, 4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log, new SfMove { Age = lockAt + stun, P = V2Party[0], To = new Vector3(-8.9f, 0, 4) });
                V2Check("4.2 m/s: walking 2.9 m away after a 0.25 s reaction escapes; the one standing still is hit",
                    SfHitCount(V2Party[0]) == 0 && SfHitCount(V2Party[1]) == 1, SfHitText() + $" | escaper at {SfPos(V2Party[0].transform.position)}");
            }
            // ---- 2b. same at 6 m/s, and walk + one dash at 4.2 m/s ----
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, 4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log, new SfMove { Age = lockAt + stun, P = V2Party[0], To = new Vector3(-8.9f, 0, 4), Speed = 6f },
                                        new SfMove { Age = lockAt + stun, P = V2Party[1], To = new Vector3(8.9f, 0, 4), Dash = true });
                V2Check("6 m/s walk and 4.2 m/s walk + dash both escape", V2Party.All(p => SfHitCount(p) == 0), SfHitText());
            }

            // ---- 3. N = 5: radius 1.8 m, each walks 2.4 m outward from a 7 m ring and all escape ----
            {
                var ring = V2Ring(5, 7f, null, 18f);
                yield return V2Setup(5, Difficulty.Medium, ring);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log, Enumerable.Range(0, 5).Select(i => new SfMove { Age = lockAt + stun, P = V2Party[i], To = ring[i].normalized * 9.4f }).ToArray());
                V2Check("N = 5: 5 stars + 5 mirrors at radius 1.8 m", log.T.Count == 10 && log.T.Keys.All(h => Mathf.Approximately(h.Radius, 1.8f)), $"{log.T.Count} circles, radii {string.Join(",", log.T.Keys.Select(h => h.Radius).Distinct())}");
                V2Check("N = 5: walking 2.4 m outward at 4.2 m/s after the lock escapes (worst 2.1 m)", V2Party.All(p => SfHitCount(p) == 0), SfHitText());
            }

            // ---- 4. melee next to the boss and ranged at the arena edge both get hit if they don't move ----
            {
                var at = new[] { new Vector3(2.0f, 0, -1.5f), new Vector3(0, 0, 13.2f) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                float far = Sim.Flat(V2Party[1].transform.position - V2Boss.transform.position).magnitude;
                V2Check($"melee beside the boss and ranged at the edge ({far:0.0} m) are both hit standing still",
                    far >= 8 && V2Party.All(p => SfHitCount(p) == 1 && SfHitBy(p, SfOf(p).Main)), SfHitText());
            }

            // ---- 5. two players 1 m apart: each is also hit by the other's star ----
            {
                var at = new[] { new Vector3(-5, 0, 4), new Vector3(-4, 0, 4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                var a = V2Party[0]; var b = V2Party[1];
                V2Check("1 m apart: each is hit by their own star and the partner's star (2 instances, 60)",
                    SfHitBy(a, SfOf(a).Main) && SfHitBy(a, SfOf(b).Main) && SfHitBy(b, SfOf(b).Main) && SfHitBy(b, SfOf(a).Main)
                    && V2HitsOn(a, SfName).Sum(h => h.Damage) == 60 && V2HitsOn(b, SfName).Sum(h => h.Damage) == 60, SfHitText());
                V2Check("one hit per instance (stacked stars)", V2OneHitPerInstance());
            }

            // ---- 6. a player on another player's mirror point is hit by that reflection ----
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, -4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                var a = V2Party[0]; var b = V2Party[1];
                V2Check("standing on a partner's mirror point: hit by that reflection", SfHitBy(b, SfOf(a).Mirror) && SfHitBy(a, SfOf(b).Mirror), SfHitText());
            }

            // ---- 7. N = 4 and N = 8 (ring spots; N = 8 all escape inward) ----
            {
                var ring = V2Ring(4, 6f, null, 45f);
                yield return V2Setup(4, Difficulty.Medium, ring);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                V2Check("N = 4: 4 stars + 4 mirrors at radius 2.2 m", log.T.Count == 8 && log.T.Keys.All(h => Mathf.Approximately(h.Radius, 2.2f)) && Sf.Stars.Count == 4,
                    $"{log.T.Count} circles");
                V2Check("N = 4 on a 6 m ring standing still: own star plus the opposite partner's mirror (2 each)", V2Party.All(p => SfHitCount(p) == 2), SfHitText());
            }
            {
                var ring = V2Ring(8, 6f, null, 22.5f);
                yield return V2Setup(8, Difficulty.Medium, ring, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log, Enumerable.Range(0, 8).Select(i => new SfMove { Age = lockAt + stun, P = V2Party[i], To = ring[i].normalized * 3.7f }).ToArray());
                V2Check("N = 8: 8 stars + 8 mirrors at radius 1.8 m", log.T.Count == 16 && log.T.Keys.All(h => Mathf.Approximately(h.Radius, 1.8f)), $"{log.T.Count} circles");
                V2Check("N = 8 on a 6 m ring: all eight escape by stepping 2.3 m inward at 4.2 m/s", V2Party.All(p => SfHitCount(p) == 0),
                    SfHitText() + " | " + string.Join(" ", V2Party.Select(p => SfPos(p.transform.position))));
                V2Check("one hit per instance (N = 8)", V2OneHitPerInstance());
            }

            // ---- 8. solo: star II spawns on star I's lock; standing still takes both, leaving twice takes none ----
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-6, 0, 4) });
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                var p = V2Party[0];
                var I = SfOf(p, 1); var II = SfOf(p, 2);
                bool shape = I != null && II != null && log.T.Count == 4;
                V2Check("solo: star I + mirror, then star II + mirror", shape, string.Join(", ", log.T.Keys.Select(h => h.Source)));
                if (shape)
                {
                    V2Check($"solo: star II spawns on star I's lock (cast age {lockAt}), locks at {2 * lockAt}, resolves at {lockAt + resolveAt}",
                        // the first boss step on which star I reports Locked (its age is 120 there)
                        log.T[II.Main].Spawn - log.Cast == lockAt && log.T[I.Main].LockAge == lockAt && log.T[II.Mirror].Spawn == log.T[II.Main].Spawn
                        && log.CastAge(log.T[II.Main].LastTrack) == 2 * lockAt && log.CastAge(log.T[II.Main].Resolve) == lockAt + resolveAt,
                        $"II spawn age {log.CastAge(log.T[II.Main].Spawn)}, I lock age {log.T[I.Main].LockAge}, II lock age {log.CastAge(log.T[II.Main].LastTrack)}, II resolve age {log.CastAge(log.T[II.Main].Resolve)}");
                    V2Check($"solo: Run returns {lockAt + resolveAt} ticks after the cast", log.End - log.Cast == lockAt + resolveAt, $"end {log.End - log.Cast}");
                    V2Check("solo standing still: hit by star I and star II (60)", SfHitBy(p, I.Main) && SfHitBy(p, II.Main) && V2HitsOn(p, SfName).Sum(h => h.Damage) == 60, SfHitText());
                }
            }
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-6, 0, 4) });
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log, new SfMove { Age = lockAt + stun, P = V2Party[0], To = new Vector3(-8.9f, 0, 4) },
                                        new SfMove { Age = 2 * lockAt + stun, P = V2Party[0], To = new Vector3(-8.9f, 0, 6.9f) });
                V2Check("solo at 4.2 m/s: leave star I, then leave star II (each 0.25 s after its lock) takes no hit", SfHitCount(V2Party[0]) == 0, SfHitText());
            }

            // ---- 9. Easy stretches follow and window by 1.25 (damage ×0.6); Hard keeps the timings (damage ×1.3) ----
            foreach (var (level, lk, rs, dmg) in new[] {
                (Difficulty.Easy, Sim.Ticks(StarfallMirror.FollowS * 1.25f), Sim.Ticks(StarfallMirror.FollowS * 1.25f) + Sim.Ticks(StarfallMirror.WindowS * 1.25f), 18),
                (Difficulty.Hard, lockAt, resolveAt, 39) })
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, 4) };
                yield return V2Setup(2, level, at);
                if (V2Boss == null) yield break;
                var log = new SfLog();
                yield return SfRun(log);
                V2Check($"{level}: lock at {lk}, resolve at {rs}, radius 2.2 m", log.T.Count == 4 && log.T.Values.All(t => t.LockAge == lk && t.ResolveAge == rs) && log.T.Keys.All(h => Mathf.Approximately(h.Radius, 2.2f)),
                    string.Join("; ", log.T.Values.Select(t => $"lock {t.LockAge} resolve {t.ResolveAge}")));
                V2Check($"{level}: a star hit deals {dmg}", V2Party.All(p => V2HitsOn(p, SfName).Count() == 1 && V2HitsOn(p, SfName).All(h => h.Damage == dmg)), SfHitText());
            }

            // ---- 10. boss death mid-follow cancels every star and mirror in the killing tick (last: it ends the practice) ----
            {
                var at = new[] { new Vector3(-6, 0, 4), new Vector3(6, 0, 4) };
                yield return V2Setup(2, Difficulty.Medium, at);
                if (V2Boss == null) yield break;
                var w = V2W; int spawned = 0; Action<Hazard> hook = h => { if (h.Source.StartsWith(SfName)) spawned++; };
                w.Hazards.Spawned += hook;
                V2Begin(V2Boss.Starfall.Run());
                yield return Until(() => spawned > 0, 5, "Starfall Mirror cast");
                yield return V2Ticks(60);
                int liveBefore = w.Hazards.Live.Count(h => h.Source.StartsWith(SfName));
                V2KillBoss();
                int liveAfter = w.Hazards.Live.Count(h => h.Source.StartsWith(SfName));
                yield return V2Ticks(lockAt + resolveAt);
                w.Hazards.Spawned -= hook;
                V2Check("boss death mid-follow cancels every circle in the killing tick, and none hits later", liveBefore == 4 && liveAfter == 0 && !V2Hits.Any(h => h.Source.StartsWith(SfName)),
                    $"live before {liveBefore}, after {liveAfter}, hits {V2Hits.Count(h => h.Source.StartsWith(SfName))}");
                yield return Until(() => d.State == RunState.Results, 10, "Aurelion v2: results after the Starfall death check");
            }
        }
    }
}
