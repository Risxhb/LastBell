using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Meridian Lances (aurelion.meridian_lances) checks, owned by the Meridian Lances session; called once from AurelionV2Checks
    // (RunAutoTest.Aurelion.V2.cs). Brief: abilities-v2/01-meridian_lances.md, "Escape-check plan" and the eight autotest checks.
    // Harness: V2Setup(n, level, positions, phase, bossAt), V2In(p).Go/Walk/Dash/Stop, V2Fire / V2Begin + V2WaitIdle on
    // V2Boss.<Ability>.Run(...), V2Hits / V2Spawns, V2OneHitPerInstance(), V2KillBoss(), V2Check(name, ok, detail).
    // A spy wraps every lance's Track and OnResolve as it spawns, so spawn, lock and resolve ticks are exact (the coroutine
    // itself only sees frames), and snapshots the party on each tracked tick (the last one is the lock tick).
    public sealed partial class RunAutoTest
    {
        sealed class LanceRec
        {
            public Hazard H; public int Spawn, Tracks, LastTrack = -1, Resolve = -1;
            public readonly List<float> Yaws = new List<float>();
            public Dictionary<PlayerController, Vector3> AtLock;
        }
        readonly Dictionary<Hazard, LanceRec> lanceRecs = new Dictionary<Hazard, LanceRec>();
        readonly Dictionary<int, string> lanceBeam = new Dictionary<int, string>();   // hazard id > "run/volley/beam"
        CombatWorld lanceHooked; int lanceStart, lanceEnd, lanceRuns, lanceBeamDoubles, lanceSplitSets;

        static readonly Vector3 LanceBoss = new Vector3(0, 0, -1.5f);
        static Vector3 LPolar(float yaw, float r) => LanceBoss + Sim.Dir(yaw) * r;
        static float LLineDist(Vector3 p, Vector3 o, float yaw) => Mathf.Abs(Vector3.Cross(Sim.Dir(yaw), Sim.Flat(p - o)).y);
        static int LTk(Difficulty level, float s) => Sim.Ticks(s * (level == Difficulty.Easy ? 1.25f : 1f));
        float LBody => V2Boss.BodyRadius + PlayerController.Radius + 0.05f;
        MeridianLances L => V2Boss.Lances;

        void LanceSpyOn()
        {
            if (lanceHooked == V2W) return;
            lanceHooked = V2W;
            V2W.Hazards.Spawned += h =>
            {
                if (!h.Source.StartsWith("Meridian Lances")) return;
                var r = new LanceRec { H = h, Spawn = V2W.Tick, AtLock = LSnap() };
                lanceRecs[h] = r;
                var track = h.Track; var resolve = h.OnResolve;
                h.Track = hz => { track?.Invoke(hz); r.Tracks++; r.LastTrack = V2W.Tick; r.Yaws.Add(hz.Yaw); r.AtLock = LSnap(); };
                h.OnResolve = hz => { r.Resolve = V2W.Tick; resolve?.Invoke(hz); };
            };
        }
        Dictionary<PlayerController, Vector3> LSnap() => V2Party.Where(p => p && !p.Down).ToDictionary(p => p, p => Sim.Flat(p.transform.position));
        LanceRec LRec(int volley, int i = 0) => lanceRecs[L.Volleys[volley][i]];
        // smallest distance from p to any beam line of a volley
        float LVolleyDist(int volley, Vector3 p) => Enumerable.Range(0, L.Volleys[volley].Count / 2).Min(b => LLineDist(p, L.Volleys[volley][2 * b].Origin, L.Volleys[volley][2 * b].Yaw));

        IEnumerable<int> LanceTimed(IEnumerable<int> body) { lanceStart = V2W.Tick; foreach (var w in body) yield return w; lanceEnd = V2W.Tick; }

        // Runs one Lances cast (its phase variant) and waits for it, keeping everyone's HP topped up so nobody goes down
        // and changes who is farthest / nearest. Then records the per-beam hit evidence.
        IEnumerator LanceRun(int beams, int volleys, bool cascade, params IEnumerator[] alongside)
        {
            LanceSpyOn(); lanceRecs.Clear(); lanceEnd = -1;
            V2Begin(LanceTimed(L.Run(beams, volleys, cascade)));
            var co = alongside.Select(StartCoroutine).ToList();
            float t = 0;
            while ((V2Boss.TestBusy || lanceEnd < 0) && t < 15 && d.State == RunState.Combat)
            {
                foreach (var p in V2Party) if (p && !p.Down && p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp);
                t += Time.deltaTime; yield return null;
            }
            foreach (var c in co) if (c != null) StopCoroutine(c);
            if (lanceEnd < 0) V2Check("cast finished", false, $"timed out (boss '{V2Boss?.Current}')");
            // one hit per beam: no player hit twice by the two halves of one beam, and the halves end with the same hit set
            int run = ++lanceRuns;
            for (int v = 0; v < L.Volleys.Count; v++)
                for (int i = 0; i < L.Volleys[v].Count; i++) lanceBeam[L.Volleys[v][i].Id] = $"{run}/{v}/{i / 2}";
            lanceBeamDoubles += V2Hits.Where(h => lanceBeam.ContainsKey(h.H.Id) && h.Tick >= lanceStart)
                .GroupBy(h => (h.P, lanceBeam[h.H.Id])).Count(g => g.Count() > 1);
            foreach (var vol in L.Volleys) for (int i = 0; i + 1 < vol.Count; i += 2) if (!vol[i].Hit.SetEquals(vol[i + 1].Hit)) lanceSplitSets++;
        }

        // the nearest safe spot for a scripted dodger: same radius, turned off the nearest beam line until `clear` m from
        // it (the lane edge is 1.1 + 0.3 m), or, near the boss where no point at that radius is clear, out to the wedge middle
        static Vector3 LSafeSpot(Vector3 pos, Vector3 boss, float aim, float spacing, float clear)
        {
            float r = Sim.Flat(pos - boss).magnitude, ang = Sim.YawTo(boss, pos, aim);
            float err = Mathf.Repeat(ang - aim + spacing / 2, spacing) - spacing / 2;
            float line = ang - err, side = err >= 0 ? 1 : -1, half = spacing / 2, need;
            if (r * Mathf.Sin(half * Mathf.Deg2Rad) < clear + 0.2f) { r = clear / Mathf.Sin(half * Mathf.Deg2Rad) + 0.2f; need = half; }
            else need = Mathf.Asin(clear / r) * Mathf.Rad2Deg;
            return boss + Sim.Dir(line + side * Mathf.Max(Mathf.Abs(err), need)) * r;
        }

        // Scripted dodger: reacts `react` ticks after a volley locks (at once for a cascade, which is shown locked) and
        // walks to the nearest safe spot at `speed`.
        IEnumerator LanceDodge(PlayerController p, int volley, int react = 15, float speed = 4.2f, float clear = 1.6f)
        {
            float t = 0;
            while (L.Volleys.Count <= volley && t < 10) { t += Time.deltaTime; yield return null; }
            if (L.Volleys.Count <= volley) yield break;
            // the lock tick is spawn + LockAt - 1 (the spawn tick for a cascade, LockAt 0)
            var h = L.Volleys[volley][0];
            int go = L.SpawnTicks[volley] + Mathf.Max(0, h.LockAt - 1) + react, beams = L.Volleys[volley].Count / 2;
            while (V2W.Tick < go) yield return null;
            V2In(p).Go(LSafeSpot(p.transform.position, h.Origin, h.Yaw, 180f / beams, clear), speed);
        }
        IEnumerator LanceGoAt(PlayerController p, Func<int> tick, Vector3 to, float speed = 4.2f)
        {
            float t = 0;
            while ((tick() < 0 || V2W.Tick < tick()) && t < 10) { t += Time.deltaTime; yield return null; }
            V2In(p).Go(to, speed);
        }

        IEnumerable<V2HitRec> LHits(PlayerController p) => V2HitsOn(p, "Meridian Lances").Where(h => h.Tick >= lanceStart);

        IEnumerator V2MeridianLances()
        {
            lanceRuns = 0; lanceBeamDoubles = 0; lanceSplitSets = 0; lanceBeam.Clear();
            var variants = new[] { (ph: 1, beams: 3, volleys: 1, cascade: false), (ph: 2, beams: 4, volleys: 2, cascade: false), (ph: 3, beams: 4, volleys: 2, cascade: true) };

            // ---- 1 + 8: shape, spawn / lock / resolve ticks, at every level (Easy ×1.25 on every track and window, Hard unchanged)
            foreach (var level in new[] { Difficulty.Medium, Difficulty.Easy, Difficulty.Hard })
                foreach (var v in variants)
                {
                    string tag = $"{level} {(v.cascade ? "cascade" : v.beams + " beams × " + v.volleys)}";
                    yield return V2Setup(1, level, new[] { LPolar(0, 6) }, v.ph);
                    if (V2Boss == null) yield break;
                    yield return LanceRun(v.beams, v.volleys, v.cascade);
                    if (L.Volleys.Count != v.volleys) { V2Check($"{tag}: {v.volleys} volley(s)", false, $"{L.Volleys.Count}"); continue; }
                    float spacing = 180f / v.beams;
                    bool shape = L.Volleys.All(vol => vol.Count == 2 * v.beams && vol.All(h => h.Shape == HazardShape.Lane && h.Kind == HazardKind.Strike
                        && Mathf.Approximately(h.Width, 2.2f) && h.Length >= 28 && h.Damage == 35 && Sim.Flat(h.Origin - LanceBoss).magnitude < 0.05f));
                    bool angles = L.Volleys.All(vol => Enumerable.Range(0, v.beams).All(b =>
                        Mathf.Abs(Mathf.DeltaAngle(vol[2 * b].Yaw, vol[0].Yaw + b * spacing)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(vol[2 * b + 1].Yaw, vol[2 * b].Yaw + 180)) < 0.01f));
                    V2Check($"{tag}: {v.beams} beams {spacing}° apart, each two 28 m × 2.2 m Strike lanes through his centre, 35 damage", shape && angles,
                        string.Join("; ", L.Volleys.Select(vol => vol.Count + " lanes: " + string.Join(",", vol.Select(h => h.Yaw.ToString("0.0"))))));
                    var r1 = LRec(0);
                    V2Check($"{tag}: volley 1 tracks {LTk(level, 1.2f)} ticks, resolves {LTk(level, 1.2f)} after the lock ({LTk(level, 2.4f)} warned)",
                        r1.Tracks == LTk(level, 1.2f) && r1.Resolve - r1.LastTrack == LTk(level, 1.2f) && r1.Resolve - r1.Spawn + 1 == LTk(level, 2.4f) && L.Volleys[0].All(h => lanceRecs[h].Resolve == r1.Resolve),
                        $"spawn {r1.Spawn}, tracked {r1.Tracks} (lock {r1.LastTrack}), resolve {r1.Resolve}");
                    if (v.volleys > 1)
                    {
                        var r2 = LRec(1);
                        V2Check($"{tag}: volley 2 warns on volley 1's resolve tick", r2.Spawn == r1.Resolve, $"v1 resolve {r1.Resolve}, v2 spawn {r2.Spawn}");
                        if (v.cascade)
                            V2Check($"{tag}: cascade shows locked at once, rotated 22.5°, resolves {LTk(level, 2.2f)} ticks after it appears",
                                r2.Tracks == 0 && r2.H.LockAt == 0 && r2.Resolve - r2.Spawn + 1 == LTk(level, 2.2f) && Mathf.Abs(Mathf.DeltaAngle(L.AimOf(1), L.AimOf(0) + 22.5f)) < 0.01f,
                                $"tracked {r2.Tracks}, spawn {r2.Spawn}, resolve {r2.Resolve}, aims {L.AimOf(0):0.0} > {L.AimOf(1):0.0}");
                        else
                            V2Check($"{tag}: volley 2 tracks {LTk(level, 1.0f)} ticks, resolves {LTk(level, 1.2f)} after the lock ({LTk(level, 2.2f)} warned)",
                                r2.Tracks == LTk(level, 1.0f) && r2.Resolve - r2.LastTrack == LTk(level, 1.2f) && r2.Resolve - r2.Spawn + 1 == LTk(level, 2.2f),
                                $"spawn {r2.Spawn}, tracked {r2.Tracks} (lock {r2.LastTrack}), resolve {r2.Resolve}");
                    }
                    int lastResolve = L.Volleys.SelectMany(vol => vol).Max(h => lanceRecs[h].Resolve);
                    V2Check($"{tag}: Run returns on the last resolve tick", lanceEnd == lastResolve, $"end {lanceEnd}, last resolve {lastResolve}");
                    // the still player (6 m, on volley 1's line) is hit, scaled once by the level's damage-taken scale
                    var hit = LHits(V2Party[0]).FirstOrDefault();
                    int want = Mathf.RoundToInt(35 * RunDifficulty.DamageTakenScale(level));
                    V2Check($"{tag}: a player standing still on volley 1's line is hit on its resolve tick for {want}", hit != null && hit.Tick == r1.Resolve && hit.Damage == want,
                        hit == null ? "no hit" : $"tick {hit.Tick} (resolve {r1.Resolve}), damage {hit.Damage}");
                }

            // ---- aim: tracks the farthest player at up to 120°/s, then holds
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(30, 9) }, 1);
                if (V2Boss == null) yield break;
                var p = V2Party[0];
                V2In(p).Walk(Sim.Dir(30 + 90), 4.2f);   // tangential, about 27°/s round him
                yield return LanceRun(3, 1, false);
                var r = LRec(0);
                float dist = LVolleyDist(0, r.AtLock[p]);
                V2Check("tracking: a player walking round him at 4.2 m/s is still on a beam line at the lock", dist < 0.3f, $"{dist:0.00} m off the nearest line");
                float step = r.Yaws.Zip(r.Yaws.Skip(1), (a, b) => Mathf.Abs(Mathf.DeltaAngle(a, b))).DefaultIfEmpty(0).Max();
                V2Check("tracking: the aim turns at most 2° a tick (120°/s)", step <= 2.001f, $"max step {step:0.000}°");
            }
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(0, 9) }, 2);
                if (V2Boss == null) yield break;
                var p = V2Party[0];
                yield return LanceRun(4, 1, false, LanceJump(p, 20, 20));
                var r = LRec(0);
                float step = r.Yaws.Zip(r.Yaws.Skip(1), (a, b) => Mathf.Abs(Mathf.DeltaAngle(a, b))).DefaultIfEmpty(0).Max();
                float dist = LVolleyDist(0, r.AtLock[p]);
                V2Check("tracking: after a sudden 20° jump the aim turns at exactly the 2° cap and still catches up by the lock",
                    step > 1.99f && step <= 2.001f && dist < 0.3f, $"max step {step:0.000}°, {dist:0.00} m off at the lock");
            }

            // ---- 3 + 4: standing still is punished at every range: at 12 m, at the rim, and hugging his body
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(0, 12) }, 1);
                if (V2Boss == null) yield break;
                var p = V2Party[0];
                yield return LanceRun(3, 1, false);
                var hits = LHits(p).ToList();
                yield return V2Ticks(30);
                float off = LVolleyDist(0, p.transform.position);
                V2Check("a player standing still at 12 m (volley 1 through him) is hit once and knocked sideways out of the lane",
                    hits.Count == 1 && off > 1.0f, $"{hits.Count} hits, {off:0.00} m off the line afterwards");
            }
            {
                var rim = new Vector3(0, 0, 0) + Sim.Dir(200) * (AurelionAbility.Rim - 0.4f);
                yield return V2Setup(1, Difficulty.Medium, new[] { rim }, 1);
                if (V2Boss == null) yield break;
                yield return LanceRun(3, 1, false);
                V2Check("a player standing still at the arena edge is hit (the beams reach past the rim)", LHits(V2Party[0]).Count() == 1,
                    $"{LHits(V2Party[0]).Count()} hits at {Sim.Flat(V2Party[0].transform.position - LanceBoss).magnitude:0.0} m");
            }
            foreach (var v in variants.Take(2))
            {
                // N = 2: the ranged player draws volley 1; the melee player hugs him halfway between two beam lines
                float half = 90f / v.beams;
                yield return V2Setup(2, Difficulty.Medium, new[] { LPolar(0, 10), LPolar(half, 2) }, v.ph);
                if (V2Boss == null) yield break;
                V2Place(V2Party[1], LPolar(half, LBody)); yield return V2Ticks(2);
                yield return LanceRun(v.beams, 1, false);
                var r = LRec(0);
                bool melee = LHits(V2Party[1]).Any(h => h.Tick == r.Resolve), ranged = LHits(V2Party[0]).Any(h => h.Tick == r.Resolve);
                V2Check($"{v.beams} beams: hugging his body midway between two beams is still hit, and so is the still ranged player",
                    melee && ranged, $"melee hit {melee}, ranged hit {ranged}");
            }

            // ---- 2: escape routes at 4.2 m/s, walking only, 0.25 s reaction after the lock
            foreach (var v in variants.Take(2))
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(0, 2) }, v.ph);
                if (V2Boss == null) yield break;
                var p = V2Party[0];
                V2Place(p, LPolar(0, LBody)); yield return V2Ticks(2);
                yield return LanceRun(v.beams, v.volleys, false, LanceDodge(p, 0), LanceDodge(p, 1));
                V2Check($"escape, {v.beams} beams × {v.volleys}: against his body on the beam line, walking to the wedge at 4.2 m/s is never hit",
                    !LHits(p).Any() && p.Stamina >= PlayerController.MaxStamina - 0.01f, $"{LHits(p).Count()} hits, ended {Sim.Flat(p.transform.position - LanceBoss).magnitude:0.0} m out");
            }
            foreach (var v in variants.Take(2))
                foreach (var speed in new[] { 4.2f, 6f })
                {
                    yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(0, 12) }, v.ph);
                    if (V2Boss == null) yield break;
                    var p = V2Party[0];
                    yield return LanceRun(v.beams, v.volleys, false, LanceDodge(p, 0, 15, speed), LanceDodge(p, 1, 15, speed));
                    V2Check($"escape, {v.beams} beams × {v.volleys}: at 12 m on volley 1's line, a sidestep at {speed} m/s is never hit", !LHits(p).Any(), $"{LHits(p).Count()} hits");
                }
            {
                // the cascade: the far player in a volley-1 wedge walks onto the just-resolved line (about 4.7 m at 12 m)
                yield return V2Setup(2, Difficulty.Medium, new[] { LPolar(0, 12.5f), LPolar(22.5f, 12) }, 3);
                if (V2Boss == null) yield break;
                PlayerController far = V2Party[0], wedge = V2Party[1];
                yield return LanceRun(4, 2, true, LanceDodge(far, 0), LanceGoAt(wedge, () => L.SpawnTicks.Count > 1 ? L.SpawnTicks[1] + 15 : -1, LPolar(0, 12)));
                V2Check("escape, cascade: a player at 12 m in a volley-1 wedge walks onto the old beam line at 4.2 m/s and is never hit; volley 1's target sidesteps",
                    !LHits(wedge).Any() && !LHits(far).Any(), $"wedge player {LHits(wedge).Count()} hits, volley-1 target {LHits(far).Count()} hits");
                yield return V2Setup(2, Difficulty.Medium, new[] { LPolar(0, 12.5f), LPolar(22.5f, 12) }, 3);
                if (V2Boss == null) yield break;
                far = V2Party[0]; wedge = V2Party[1];
                yield return LanceRun(4, 2, true, LanceDodge(far, 0));
                var hits = LHits(wedge).ToList();
                V2Check("cascade: the same wedge player standing still is hit once, by the rotated volley", hits.Count == 1 && hits[0].Tick == LRec(1).Resolve,
                    string.Join(", ", hits.Select(h => "tick " + h.Tick)) + $" (v2 resolve {LRec(1).Resolve})");
            }

            // ---- 6: solo and N = 2 / 4 / 8: volley 1 through the farthest, volley 2 through the nearest
            var parties = new Dictionary<int, Vector3[]>
            {
                [1] = new[] { LPolar(40, 7) },
                [2] = new[] { LPolar(100, 4), LPolar(10, 10) },
                [4] = new[] { LPolar(0, 3), LPolar(95, 6), LPolar(190, 9), LPolar(280, 12) },
                [8] = Enumerable.Range(0, 8).Select(i => LPolar(10 + 45 * i, new[] { 2.6f, 4, 5.5f, 7, 8.5f, 10, 11, 12 }[i])).ToArray(),
            };
            foreach (var kv in parties)
            {
                yield return V2Setup(kv.Key, Difficulty.Medium, kv.Value, 2);
                if (V2Boss == null) yield break;
                yield return LanceRun(4, 2, false);
                if (L.Volleys.Count < 2) { V2Check($"N = {kv.Key}: two volleys", false, $"{L.Volleys.Count}"); continue; }
                var s1 = LRec(0).AtLock; var s2 = LRec(1).AtLock;
                var far = s1.OrderByDescending(e => Sim.Flat(e.Value - LanceBoss).sqrMagnitude).First();
                var near = s2.OrderBy(e => Sim.Flat(e.Value - LanceBoss).sqrMagnitude).First();
                float d1 = LVolleyDist(0, far.Value), d2 = LVolleyDist(1, near.Value);
                int fi = V2Party.IndexOf(far.Key), ni = V2Party.IndexOf(near.Key);
                int wantFar = kv.Key - 1, wantNear = 0;   // each party is listed nearest first, farthest last
                V2Check($"N = {kv.Key}: volley 1 runs through the farthest player, volley 2 through the nearest{(kv.Key == 1 ? " (both you)" : "")}",
                    fi == wantFar && ni == wantNear && d1 < 0.3f && d2 < 0.3f, $"farthest #{fi} {d1:0.00} m off, nearest #{ni} {d2:0.00} m off");
            }

            // ---- 5: one hit per instance and per beam (across every cast above)
            V2Check("one hit per beam: no player was hit twice by one beam's two halves, and the halves share their hits",
                lanceBeamDoubles == 0 && lanceSplitSets == 0, $"{lanceRuns} casts, {lanceBeamDoubles} double hits, {lanceSplitSets} beams with split hit sets");
            V2Check("one hit per instance (last cast)", V2OneHitPerInstance());

            // ---- 7: boss death mid-windup cancels every beam in the killing tick
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { LPolar(0, 8) }, 2);
                if (V2Boss == null) yield break;
                LanceSpyOn(); lanceRecs.Clear();
                V2Begin(LanceTimed(L.Run(4, 2, false)));
                yield return Until(() => L.Volleys.Count > 0 && V2W.Tick >= L.SpawnTicks[0] + 60, 10, "Lances: 1 s into volley 1");
                var w = V2W; int liveBefore = w.Hazards.Live.Count(h => h.Source.StartsWith("Meridian Lances")), resolvedBefore = w.Hazards.Resolved, killTick = w.Tick;
                V2KillBoss();
                int liveAfter = w.Hazards.Live.Count(h => h.Source.StartsWith("Meridian Lances"));
                yield return Wait(3);
                bool hits = V2Hits.Any(h => h.Source.StartsWith("Meridian Lances") && h.Tick >= killTick);
                V2Check("boss death mid-windup cancels every beam on the same tick (none resolve or hit later)",
                    liveBefore == 8 && liveAfter == 0 && w.Hazards.Resolved == resolvedBefore && !hits,
                    $"live {liveBefore} > {liveAfter}, resolved +{w.Hazards.Resolved - resolvedBefore}, later hits {hits}");
            }
        }

        // teleports a player `deg` degrees round the boss `after` ticks into volley 1 (to test the turn-rate cap)
        IEnumerator LanceJump(PlayerController p, int after, float deg)
        {
            float t = 0;
            while (L.Volleys.Count == 0 && t < 10) { t += Time.deltaTime; yield return null; }
            int at = L.SpawnTicks[0] + after;
            while (V2W.Tick < at) yield return null;
            var rel = Sim.Flat(p.transform.position - LanceBoss);
            V2Place(p, LanceBoss + Quaternion.Euler(0, deg, 0) * rel);
        }
    }
}
