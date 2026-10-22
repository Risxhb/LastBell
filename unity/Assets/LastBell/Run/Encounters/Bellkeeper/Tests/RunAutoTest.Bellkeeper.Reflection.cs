using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Reflection of the Hour (Bellkeeper ability set v2) acceptance checks, owned by the Reflection of the Hour ability session.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/03-reflection.md ("Autotest" under Build notes). Harness: BkStart,
    // BkAdd, BkStep... in RunAutoTest.BellkeeperV2.cs. Run alone with -lastbellBellkeeperV2 -lastbellTestAbility reflection.
    //
    // Tick convention: the cast's tick 0 is the launch tick (rf.LastCastTick); a hazard spawned then has Age 1 after that
    // tick's hazard step, so LockAt / ResolveAt (Age) land on relative ticks LockAt − 1 / ResolveAt − 1, as for every hazard.
    public sealed partial class RunAutoTest
    {
        ReflectionAbility rf;
        const string RfName = "Reflection of the Hour";

        IEnumerator ReflectionChecks()
        {
            Note("---- Reflection of the Hour ----");
            yield return RfSolo();
            yield return RfTracking();
            yield return RfEscapeSolo(4.2f);
            yield return RfEscapeSolo(6f);
            yield return RfEscapePartners();
            yield return RfPairs();
            yield return RfMeleeRanged();
            yield return RfParty(4);
            yield return RfParty(8);
            yield return RfClamp();
            yield return RfDeath();
            yield return RfLevels();
            yield return RfPractice();
            yield return RfEvidence();
        }

        // ---------------- helpers ----------------
        IEnumerator RfStart(Difficulty level = Difficulty.Medium)
        {
            rf = null;
            yield return BkStart("reflection", level);
            if (bk != null) rf = bk.Ability<ReflectionAbility>();
        }
        // restarts the boss (the practice loop launches Reflection on the first tick); returns the images it spawned
        List<Hazard> RfCast()
        {
            bk.BeginCombat();
            int t = BkStepUntil(() => rf.Spawned.Count > 0, 300);
            if (t < 0) { Check("Reflection: cast launched", false, "no images after 300 ticks"); return new List<Hazard>(); }
            return rf.Spawned.ToList();
        }
        int RfRel => d.World.Tick - rf.LastCastTick;
        bool RfDone(List<Hazard> hz) => hz.All(h => !d.World.Hazards.Live.Contains(h));
        // steps until every image has resolved; returns the relative tick of the resolve (or -1)
        int RfResolve(List<Hazard> hz) => BkStepUntil(() => RfDone(hz), 400) < 0 ? -1 : RfRel;
        bool RfOneHitPerInstance() => bkHits.Where(h => h.Source == RfName).GroupBy(h => (h.HazardId, h.Player)).All(g => g.Count() == 1);
        static bool Near(Vector3 a, Vector3 b, float tol = 0.05f) => Sim.Flat(a - b).magnitude <= tol;
        static string V(Vector3 v) => $"({v.x:0.0#}, {v.z:0.0#})";
        string Hits(IEnumerable<PlayerController> ps) => string.Join(" ", ps.Select((p, i) => $"p{i}:{BkHitsOn(p, RfName)}"));

        // ---------------- solo: shape, timing, still player ----------------
        IEnumerator RfSolo()
        {
            yield return RfStart(); if (rf == null) yield break;
            Check("Reflection: the ability is built (not the stub)", rf.Built);
            BkPlace(d.Player, new Vector3(5, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            Check("Reflection solo: two images spawn on the cast's tick 0 (mirror + own position)", hz.Count == 2 && RfRel == 0, $"{hz.Count} images, rel tick {RfRel}");
            Check("Reflection solo: images at the mirror (−5, 0) and at the player (5, 0)",
                  hz.Any(h => Near(h.Origin, new Vector3(-5, 0, 0))) && hz.Any(h => Near(h.Origin, new Vector3(5, 0, 0))), string.Join(" ", hz.Select(h => V(h.Origin))));
            Check("Reflection: Ground circles, r 3.0 m, 35 damage, lock at 2.0 s, resolve at 4.0 s (Medium)",
                  hz.All(h => h.Shape == HazardShape.Circle && h.Kind == HazardKind.Ground && Mathf.Approximately(h.Radius, 3.0f)
                              && h.Damage == 35 && h.LockAt == 120 && h.ResolveAt == 240 && h.Source == RfName),
                  $"r {hz[0].Radius} dmg {hz[0].Damage} lock {hz[0].LockAt} resolve {hz[0].ResolveAt} {hz[0].Kind}");
            Check("Reflection: one damaging pattern live, porcelain after-images shown", BkPatternsLive() == 1 && rf.ImagesShown == 2, $"patterns {BkPatternsLive()}, images {rf.ImagesShown}");
            BkStepUntil(() => hz.All(h => h.Locked), 300); int lockRel = RfRel;
            Check("Reflection: images lock at the cast's tick 119 (Age 120, 2.0 s before resolve)", lockRel == 119, $"locked at rel {lockRel}");
            int res = RfResolve(hz);
            Check("Reflection: images resolve at the cast's tick 239 (Age 240 = 4.0 s)", res == 239, $"resolved at rel {res}");
            var hits = bkHits.Where(h => h.Player == d.Player && h.Source == RfName).ToList();
            var own = hz.First(h => Near(h.Origin, new Vector3(5, 0, 0)));
            Check("Reflection solo: a still player is hit once, by their own image, for 35",
                  hits.Count == 1 && hits[0].HazardId == own.Id && hits[0].Damage == 35 && hits[0].Tick - rf.LastCastTick == 239,
                  string.Join("; ", hits.Select(h => $"hz {h.HazardId} dmg {h.Damage} rel {h.Tick - rf.LastCastTick}")));
            Check("Reflection: after-images shatter and are removed on resolve", rf.ImagesShown == 0, $"{rf.ImagesShown} left");
            BkStepUntil(() => !rf.Running, 200);
            Check("Reflection: cast ends after a 1.0 s recovery", RfRel >= 299 && RfRel <= 301, $"ended at rel {RfRel}");
            Check("Reflection: one hit per instance (solo)", RfOneHitPerInstance());
        }

        // ---------------- images track the player and the boss until they lock ----------------
        IEnumerator RfTracking()
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 0));
            var hz = RfCast(); if (hz.Count < 2) yield break;
            var mirror = hz.First(h => Near(h.Origin, new Vector3(-4, 0, 0))); var own = hz.First(h => h != mirror);
            BkStep(60 - RfRel);
            BkPlace(d.Player, new Vector3(0, 0, 5)); bk.transform.position = new Vector3(1, 0, 0);
            BkStep(1);
            Check("Reflection: before lock the images follow the player and the boss (2·boss − player)",
                  Near(mirror.Origin, new Vector3(2, 0, -5)) && Near(own.Origin, new Vector3(0, 0, 5)), $"mirror {V(mirror.Origin)} own {V(own.Origin)}");
            BkStep(125 - RfRel);
            var m0 = mirror.Origin; var o0 = own.Origin;
            BkPlace(d.Player, new Vector3(-6, 0, 0));
            BkStep(3);
            Check("Reflection: after lock the images stay put", Near(mirror.Origin, m0, 0.001f) && Near(own.Origin, o0, 0.001f), $"mirror {V(mirror.Origin)} own {V(own.Origin)}");
            RfResolve(hz);
            Check("Reflection: a player clear of both locked images is not hit", BkHitsOn(d.Player, RfName) == 0, $"{BkHitsOn(d.Player, RfName)} hits");
            bk.transform.position = Vector3.zero;
        }

        // ---------------- escape routes (finite stamina, no iframes) ----------------
        // solo: stand still until lock + the 0.6 s reaction margin, then walk 3.3 m+ sideways (not to the far side)
        IEnumerator RfEscapeSolo(float speed)
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            BkStep(rf.LockAtTicks - 1 + 36 - RfRel);
            var s = BkScript(d.Player); s.Speed = speed; s.Goal = new Vector3(5, 0, 7);
            float st0 = d.Player.Stamina; var from = d.Player.transform.position;
            RfResolve(hz);
            float moved = Sim.Flat(d.Player.transform.position - from).magnitude;
            Check($"Reflection escape solo at {speed:0.0} m/s: walk out after lock + 0.6 s reaction, not hit",
                  BkHitsOn(d.Player, RfName) == 0, $"{BkHitsOn(d.Player, RfName)} hits, walked {moved:0.00} m in {(rf.WarnTicks - rf.LockAtTicks - 36) / 60f:0.00} s, stamina {st0:0} -> {d.Player.Stamina:0}");
        }
        // N = 2 mirror partners each standing on the other's image at lock, then both walk out at 4.2 m/s
        IEnumerator RfEscapePartners()
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 0)); var ally = BkAdd(new Vector3(-4, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            BkStep(rf.LockAtTicks - 1 + 36 - RfRel);
            var a = BkScript(d.Player); a.Speed = 4.2f; a.Goal = new Vector3(4, 0, 6);
            var b = BkScript(ally); b.Speed = 4.2f; b.Goal = new Vector3(-4, 0, -6);
            RfResolve(hz);
            Check("Reflection escape N = 2: partners on each other's images leave 3.3 m at 4.2 m/s after lock, neither hit",
                  BkHitsOn(d.Player, RfName) == 0 && BkHitsOn(ally, RfName) == 0, Hits(new[] { d.Player, ally }));
        }

        // ---------------- N = 2: mirror partners vs one side ----------------
        IEnumerator RfPairs()
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 0)); var ally = BkAdd(new Vector3(-4, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            Check("Reflection N = 2: one image per player, no own-position image in co-op", hz.Count == 2 && hz.All(h => Mathf.Approximately(h.Radius, 3f)), $"{hz.Count} images");
            RfResolve(hz);
            Check("Reflection N = 2: still mirror partners are both hit once", BkHitsOn(d.Player, RfName) == 1 && BkHitsOn(ally, RfName) == 1, Hits(new[] { d.Player, ally }));
            Check("Reflection: one hit per instance (N = 2)", RfOneHitPerInstance());

            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 2)); ally = BkAdd(new Vector3(4, 0, -2));
            hz = RfCast(); if (hz.Count == 0) yield break;
            RfResolve(hz);
            Check("Reflection N = 2: both on one side of the boss, neither is hit", BkHitsOn(d.Player, RfName) == 0 && BkHitsOn(ally, RfName) == 0, Hits(new[] { d.Player, ally }));
        }

        // ---------------- melee and ranged both in danger ----------------
        IEnumerator RfMeleeRanged()
        {
            // N = 4: two melee hugging the boss (2 m east / west) and two ranged at 9 m (north / south), all standing still
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(2, 0, 0));
            var ps = new List<PlayerController> { d.Player, BkAdd(new Vector3(-2, 0, 0)), BkAdd(new Vector3(0, 0, 9)), BkAdd(new Vector3(0, 0, -9)) };
            var hz = RfCast(); if (hz.Count == 0) yield break;
            RfResolve(hz);
            Check("Reflection melee: still players hugging the boss (2 m) are hit by the partner opposite", BkHitsOn(ps[0], RfName) == 1 && BkHitsOn(ps[1], RfName) == 1, Hits(ps));
            Check("Reflection ranged: still players at 9 m are hit by the partner opposite", BkHitsOn(ps[2], RfName) == 1 && BkHitsOn(ps[3], RfName) == 1, Hits(ps));

            // N = 2 at the wall, east and west
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, BkEdge(Vector3.right)); var ally = BkAdd(BkEdge(Vector3.left));
            hz = RfCast(); if (hz.Count == 0) yield break;
            RfResolve(hz);
            Check("Reflection ranged: still players at the wall are hit by the partner opposite", BkHitsOn(d.Player, RfName) == 1 && BkHitsOn(ally, RfName) == 1,
                  $"{Hits(new[] { d.Player, ally })} at {CombatWorld.PlayerBound:0.0} m");

            // solo: the own-position image hits a still player anywhere
            foreach (var spot in new[] { new Vector3(0, 0, 2), new Vector3(0, 0, -9), BkEdge(Vector3.forward) })
            {
                yield return RfStart(); if (rf == null) yield break;
                BkPlace(d.Player, spot);
                hz = RfCast(); if (hz.Count == 0) yield break;
                RfResolve(hz);
                Check($"Reflection solo: a still player at {Sim.Flat(spot).magnitude:0.0} m from the boss is hit once", BkHitsOn(d.Player, RfName) == 1, $"{BkHitsOn(d.Player, RfName)} hits");
            }
        }

        // ---------------- N = 4 / 8: radius, gather on one side vs spread round the boss ----------------
        IEnumerator RfParty(int n)
        {
            float r = n >= 5 ? 2.5f : 3.0f;
            var oneSide = n == 4
                ? new[] { new Vector3(3, 0, -3), new Vector3(3, 0, 3), new Vector3(6, 0, -1.5f), new Vector3(6, 0, 1.5f) }
                : new[] { 3f, 6f }.SelectMany(x => new[] { -4.5f, -1.5f, 1.5f, 4.5f }.Select(z => new Vector3(x, 0, z))).ToArray();
            yield return RfStart(); if (rf == null) yield break;
            var ps = RfPlaceParty(oneSide);
            var hz = RfCast(); if (hz.Count == 0) yield break;
            Check($"Reflection N = {n}: {n} images of r {r:0.0} m", hz.Count == n && hz.All(h => Mathf.Approximately(h.Radius, r)), $"{hz.Count} images, r {hz[0].Radius}");
            RfResolve(hz);
            Check($"Reflection N = {n}: a party gathered on one side of the boss takes no hits", ps.All(p => BkHitsOn(p, RfName) == 0), Hits(ps));

            yield return RfStart(); if (rf == null) yield break;
            ps = RfPlaceParty(Enumerable.Range(0, n).Select(i => Quaternion.Euler(0, i * 360f / n, 0) * new Vector3(0, 0, 6)).ToArray());
            hz = RfCast(); if (hz.Count == 0) yield break;
            RfResolve(hz);
            Check($"Reflection N = {n}: still players spread round the boss at 6 m are each hit once (by the player opposite)", ps.All(p => BkHitsOn(p, RfName) == 1), Hits(ps));
            Check($"Reflection: one hit per instance (N = {n})", RfOneHitPerInstance());
        }
        List<PlayerController> RfPlaceParty(Vector3[] spots)
        {
            BkPlace(d.Player, spots[0]);
            var ps = new List<PlayerController> { d.Player };
            for (int i = 1; i < spots.Length; i++) ps.Add(BkAdd(spots[i]));
            return ps;
        }

        // ---------------- an off-centre boss: images clamp to the bound ----------------
        IEnumerator RfClamp()
        {
            yield return RfStart(); if (rf == null) yield break;
            bk.transform.position = new Vector3(6, 0, 0);
            BkPlace(d.Player, new Vector3(-6, 0, 0)); var ally = BkAdd(BkEdge(Vector3.right));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            var far = hz.OrderByDescending(h => h.Origin.x).First();
            Check("Reflection: an image past the wall is clamped to the bound (boss at 6 m, player at −6 m)",
                  CombatWorld.InPlayerBound(far.Origin, 0.01f) && Near(far.Origin, ReflectionAbility.ClampToBound(new Vector3(18, 0, 0))), $"image at {V(far.Origin)}, bound {CombatWorld.PlayerBound}");
            RfResolve(hz);
            Check("Reflection: the clamped image still hits a still player at the wall", BkHitsOn(ally, RfName) == 1 && BkHitsOn(d.Player, RfName) == 0, Hits(new[] { d.Player, ally }));
            bk.transform.position = Vector3.zero;
        }

        // ---------------- boss death mid-windup ----------------
        IEnumerator RfDeath()
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 0)); var ally = BkAdd(new Vector3(-4, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            BkStep(150 - RfRel);
            bk.DebugDefeat();
            Check("Reflection: boss death mid-windup cancels every image that tick", bk.Dead && hz.All(h => !d.World.Hazards.Live.Contains(h)) && rf.ImagesShown == 0,
                  $"dead {bk.Dead}, live {hz.Count(h => d.World.Hazards.Live.Contains(h))}, after-images {rf.ImagesShown}");
            BkStep(150);
            Check("Reflection: no hits after the boss died mid-windup", BkHitsOn(d.Player, RfName) == 0 && BkHitsOn(ally, RfName) == 0, Hits(new[] { d.Player, ally }));
        }

        // ---------------- Easy / Hard ----------------
        IEnumerator RfLevels()
        {
            yield return RfStart(Difficulty.Easy); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            var hz = RfCast(); if (hz.Count == 0) yield break;
            Check("Reflection Easy: 5.0 s warning, lock 2.5 s before, r 2.6 m (N ≤ 4) / 2.1 m (N ≥ 5)",
                  hz.All(h => h.ResolveAt == 300 && h.LockAt == 150 && Mathf.Approximately(h.Radius, 2.6f)) && Mathf.Approximately(rf.RadiusFor(8), 2.1f),
                  $"resolve {hz[0].ResolveAt} lock {hz[0].LockAt} r {hz[0].Radius} r(N=8) {rf.RadiusFor(8)}");
            int res = RfResolve(hz);
            int easyDmg = Mathf.RoundToInt(35 * d.World.DamageTakenScale);
            Check("Reflection Easy: resolves at the cast's tick 299; a still player takes 35 × 0.6",
                  res == 299 && bkHits.Count(h => h.Player == d.Player && h.Source == RfName && h.Damage == easyDmg) == 1, $"rel {res}, expected {easyDmg} dmg");
            // Easy escape: lock + 0.6 s reaction, then 2.9 m+ at 4.2 m/s
            yield return RfStart(Difficulty.Easy); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            hz = RfCast(); if (hz.Count == 0) yield break;
            BkStep(rf.LockAtTicks - 1 + 36 - RfRel);
            var s = BkScript(d.Player); s.Speed = 4.2f; s.Goal = new Vector3(5, 0, 7);
            RfResolve(hz);
            Check("Reflection Easy escape at 4.2 m/s after lock + reaction, not hit", BkHitsOn(d.Player, RfName) == 0);

            yield return RfStart(Difficulty.Hard); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(5, 0, 0));
            hz = RfCast(); if (hz.Count == 0) yield break;
            Check("Reflection Hard: shape and timing unchanged from Medium (4.0 s, lock 2.0 s, r 3.0 m)",
                  hz.All(h => h.ResolveAt == 240 && h.LockAt == 120 && Mathf.Approximately(h.Radius, 3f)), $"resolve {hz[0].ResolveAt} lock {hz[0].LockAt} r {hz[0].Radius}");
            RfResolve(hz);
            int hardDmg = Mathf.RoundToInt(35 * d.World.DamageTakenScale);
            Check("Reflection Hard: a still player takes 35 × 1.3", bkHits.Count(h => h.Player == d.Player && h.Source == RfName && h.Damage == hardDmg) == 1, $"expected {hardDmg}");
        }

        // ---------------- practice hook ----------------
        IEnumerator RfPractice()
        {
            yield return RfStart(); if (rf == null) yield break;
            Check("Reflection practice hook: -lastbellAbility reflection selects it", bk.PracticeAbility == rf && BellkeeperAbilities.Normalize("reflection") == rf.Key, bk.PracticeAbility?.Key ?? "none");
            BkPlace(d.Player, new Vector3(5, 0, 0));
            bk.BeginCombat();
            int first = -1, second = -1;
            for (int i = 0; i < 600 && second < 0; i++)
            {
                d.World.Step(); BkTopUp();
                if (rf.Casts == 1 && first < 0) first = d.World.Tick;
                if (rf.Casts == 2) second = d.World.Tick;
            }
            bool alone = bk.Abilities.All(a => a == rf || a.Casts == 0);
            Check("Reflection practice: loops alone (cast, 1.0 s recovery, 2.0 s gap, cast)", second > 0 && alone && second - first >= 400 && second - first <= 430,
                  $"casts {rf.Casts}, interval {second - first} ticks, alone {alone}");
        }

        // ---------------- evidence: a screenshot and a short clip ----------------
        IEnumerator RfEvidence()
        {
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(4, 0, 2));
            BkAdd(new Vector3(-4.5f, 0, -1)); BkAdd(new Vector3(1, 0, 7)); BkAdd(new Vector3(2.5f, 0, -6));
            RfCast();
            BkStep(80);
            yield return null; yield return null; yield return null;
            yield return Shot("bk_reflection_warning");
            BkStep(rf.LockAtTicks + 10 - RfRel);
            yield return null; yield return null;
            yield return Shot("bk_reflection_locked");
            // clip: the run's player and one ally gather on one side; two allies stay where they are and take the partner's image
            yield return RfStart(); if (rf == null) yield break;
            BkPlace(d.Player, new Vector3(5, 0, 1));
            BkAdd(new Vector3(5, 0, -2)); BkAdd(new Vector3(-3, 0, 6)); BkAdd(new Vector3(3, 0, -6));
            yield return BkClip("bk_reflection", 5.5f);
        }
    }
}
