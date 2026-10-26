using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Striking Order (Bellkeeper ability set v2) acceptance checks. Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/
    // 02-striking-order.md ("Autotest" under Build notes). Harness: BkStart, BkAdd, BkStep... in RunAutoTest.BellkeeperV2.cs.
    // Run alone with -lastbellBellkeeperV2 -lastbellTestAbility striking_order.
    public sealed partial class RunAutoTest
    {
        StrikingOrderAbility so;
        bool soOneHitEach = true; string soOneHitDetail = "";
        int SoTick => d.World.Tick - so.LastCastTick;                 // the current cast tick (the last one stepped)
        void SoStepTo(int castTick) { if (castTick > SoTick) BkStep(castTick - SoTick); }
        int SoHits(PlayerController p, Hazard h) => h == null ? 0 : bkHits.Count(x => x.Player == p && x.HazardId == h.Id);
        int SoTotal(PlayerController p) => bkHits.Count(x => x.Player == p && x.Source == so.Name);
        string SoTicks(PlayerController p) => string.Join("/", bkHits.Where(x => x.Player == p && x.Source == so.Name).Select(x => x.Tick - so.LastCastTick));

        // A Striking Order fight with the run's player plus scripted allies at `at` (at[0] is the run's player), the cast
        // launched and stepped to its tick 0.
        IEnumerator SoStart(Vector3[] at, Difficulty level = Difficulty.Medium)
        {
            yield return BkStart("striking_order", level);
            so = null;
            if (bk == null) yield break;
            ScriptedParty.RemoveExtras(d.World, d.Player);
            BkPlace(d.Player, at[0]);
            for (int i = 1; i < at.Length; i++) BkAdd(at[i]);
            so = bk.Ability<StrikingOrderAbility>();
            bk.BeginCombat();
            int t = BkStepUntil(() => so.Casts > 0, 300);
            if (t < 0) { Check("Striking Order: the practice loop launches it", false); so = null; }
        }
        // every (player, ring) pair hit at most once (R16), over every scenario
        void SoAudit(string scenario)
        {
            foreach (var g in bkHits.Where(x => x.Source == so.Name).GroupBy(x => (x.Player, x.HazardId)))
                if (g.Count() > 1) { soOneHitEach = false; soOneHitDetail += $"{scenario}: ring {g.Key.HazardId} hit a player {g.Count()}×; "; }
        }
        static Vector3[] SoCircle(int n, float r) => Enumerable.Range(0, n).Select(i => Quaternion.Euler(0, i * 360f / n, 0) * new Vector3(0, 0, r)).ToArray();

        IEnumerator StrikingOrderChecks()
        {
            Note("---------- Striking Order ----------");
            soOneHitEach = true; soOneHitDetail = "";

            // ---- practice hook ----
            yield return SoStart(new[] { new Vector3(0, 0, 6) });
            if (so == null) yield break;
            Check("Striking Order: built, and -lastbellAbility striking_order loops it alone", so.Built && bk.PracticeAbility == so
                  && bk.Abilities.All(x => x == so || x.Casts == 0), $"casts {so.Casts}");

            // ---- solo, still: I–IV all on you, timings, a still player is hit ----
            var me = d.Player;
            Check("Striking Order solo: I–IV all on the one player", so.Owners.Count == 4 && so.Owners.All(o => o == me) && so.NumeralsOn(me) == 4,
                  $"{so.Owners.Count} rings, {so.NumeralsOn(me)} numerals over the head");
            int maxPatterns = 0, ringILive = -1;
            for (int t = SoTick; t < 365; t++) { BkStep(1); maxPatterns = Mathf.Max(maxPatterns, BkPatternsLive()); if (ringILive < 0 && so.Rings[0] != null && so.Rings[0].Locked) ringILive = SoTick; }
            Check("Striking Order: rings spawn on cast ticks 0 / 60 / 120 / 180 (each starts tracking a step apart)", so.SpawnedAt.SequenceEqual(new[] { 0, 60, 120, 180 }), string.Join(",", so.SpawnedAt));
            Check("Striking Order: ring I rings at 3.0 s, then one every 1.0 s (ticks 180 / 240 / 300 / 360)", so.RangAt.SequenceEqual(new[] { 180, 240, 300, 360 }), string.Join(",", so.RangAt));
            Check("Striking Order: a ring locks 2.0 s before it rings", ringILive == 60 && so.Rings.All(r => r.ResolveAt - r.LockAt == 120), $"ring I locked from cast tick {ringILive}");
            Check("Striking Order: a still player is hit by ring I at 3.0 s (25)", SoHits(me, so.Rings[0]) == 1 && bkHits.Any(x => x.Player == me && x.HazardId == so.Rings[0].Id && x.Damage == 25 && x.Tick - so.LastCastTick == 180),
                  $"hits at cast ticks {SoTicks(me)}");
            Note($"Striking Order solo still: {SoTotal(me)} of 4 rings hit (the 1.5 m knockback can carry a still player out of a stacked locked ring); HP {me.Hp}");
            Check("Striking Order: one damaging pattern live (≤ 2 rule)", maxPatterns <= 1, $"max {maxPatterns}");
            bool runningAtEnd = so.Running; BkStep(422 - SoTick);
            Check("Striking Order: 1.0 s recovery after the last ring, then it ends", runningAtEnd && !so.Running && so.NumeralsShown == 0, $"running at 365: {runningAtEnd}, at 422: {so.Running}");
            SoAudit("solo still");

            // ---- solo chain: walking a curve at 4.2 m/s escapes all four, no stamina spent ----
            yield return SoStart(new[] { new Vector3(7, 0, 0) });
            if (so == null) yield break;
            var walker = BkScript(d.Player);
            walker.Steer = p =>
            {
                var r = Sim.Flat(p.transform.position); float m = Mathf.Max(0.1f, r.magnitude);
                var tangent = new Vector3(-r.z, 0, r.x) / m; var fix = r / m * Mathf.Clamp(7f - m, -1, 1) * 0.5f;
                return (tangent + fix).normalized * (4.2f / PlayerController.WalkSpeed);
            };
            float st0 = d.Player.Stamina, travelled = 0; var last = d.Player.transform.position;
            for (int t = SoTick; t < 365; t++) { BkStep(1); travelled += Sim.Flat(d.Player.transform.position - last).magnitude; last = d.Player.transform.position; }
            Check("Striking Order solo chain: walking a 7 m curve at 4.2 m/s escapes all four rings", SoTotal(d.Player) == 0 && so.RangAt.Count == 4,
                  $"{SoTotal(d.Player)} hits, {travelled / (365 / 60f):0.00} m/s, stamina {st0:0} -> {d.Player.Stamina:0}");
            SoAudit("solo chain");

            // ---- N = 4, all still: melee at the boss, ranged at 9 m and at the wall; one each ----
            var spots = new[] { new Vector3(1.5f, 0, 0), new Vector3(0, 0, 9), BkEdge(Vector3.left), new Vector3(0, 0, -9) };
            yield return SoStart(spots);
            if (so == null) yield break;
            var party = d.World.Players.ToList();
            Check("Striking Order N = 4: I–IV, one each, r 2.6 m", so.Owners.Count == 4 && so.Owners.Distinct().Count() == 4 && Mathf.Approximately(so.RingRadius, 2.6f)
                  && party.All(p => so.NumeralsOn(p) == 1), $"{so.Owners.Count} rings, r {so.RingRadius}");
            SoStepTo(130);
            yield return Shot("bk_striking_order_warning");
            SoStepTo(365);
            string names(int i) => i == 0 ? "melee (1.5 m from the boss)" : i == 1 ? "ranged 9 m" : i == 2 ? "at the wall" : "ranged 9 m (south)";
            for (int i = 0; i < 4; i++)
            {
                var p = party[i]; int k = so.Owners.ToList().IndexOf(p);
                Check($"Striking Order: a still player {names(i)} is hit by their own ring ({StrikingOrderAbility.Numerals[k]})",
                      k >= 0 && SoTotal(p) == 1 && SoHits(p, so.Rings[k]) == 1 && bkHits.Any(x => x.Player == p && x.Source == so.Name && x.Tick - so.LastCastTick == 180 + 60 * k),
                      $"hits at cast ticks {SoTicks(p)}");
            }
            SoAudit("N = 4 still");

            // ---- N = 2 at the walls: A, B, A, B; escape a locked ring at 4.2 m/s (sideways along the wall), and with a late dash ----
            yield return SoStart(new[] { BkEdge(Vector3.right), BkEdge(Vector3.left) });
            if (so == null) yield break;
            var A = so.Owners[0]; var B = so.Owners[1];
            Check("Striking Order N = 2: I–IV alternate A, B, A, B, r 2.6 m", so.Owners.Count == 4 && A != B && so.Owners[2] == A && so.Owners[3] == B
                  && so.NumeralsOn(A) == 2 && so.NumeralsOn(B) == 2 && Mathf.Approximately(so.RingRadius, 2.6f));
            Vector3 Along(PlayerController p) { var r = Sim.Flat(p.transform.position).normalized; return p.transform.position + new Vector3(-r.z, 0, r.x) * 10; }
            SoStepTo(60 + 36);                                   // ring I locked at 60, plus the 0.6 s reaction margin
            var sa = BkScript(A); sa.Goal = Along(A); sa.Speed = 4.2f;
            SoStepTo(240 - 30);                                  // B waits until 0.5 s before ring II, then dashes out
            var sb = BkScript(B); float stB = B.Stamina; sb.Goal = Along(B); sb.Speed = 4.2f; sb.DashNext = true;
            BkStep(1); float stB1 = B.Stamina;
            SoStepTo(245);
            Check("Striking Order: walking out of a locked ring at the wall at 4.2 m/s (from lock + 0.6 s) escapes it", SoHits(A, so.Rings[0]) == 0,
                  $"A: ring I hits {SoHits(A, so.Rings[0])}");
            Check("Striking Order: a late dash (0.5 s before the ring) escapes, and costs stamina (no iframes)", SoHits(B, so.Rings[1]) == 0 && stB - stB1 >= PlayerController.DashCost - 1,
                  $"B: ring II hits {SoHits(B, so.Rings[1])}, stamina {stB:0} -> {stB1:0}");
            SoAudit("N = 2 escape");

            // ---- every ring hits anyone inside: a bystander next to the owner is hit too ----
            yield return SoStart(new[] { new Vector3(0, 0, 6), new Vector3(1, 0, 6) });
            if (so == null) yield break;
            SoStepTo(185);
            var others = d.World.Players.Where(p => p != so.Owners[0]).ToList();
            Check("Striking Order: ring I hits its owner and a bystander standing inside it (one hit each)",
                  SoHits(so.Owners[0], so.Rings[0]) == 1 && others.Count == 1 && SoHits(others[0], so.Rings[0]) == 1);
            SoAudit("bystander");

            // ---- N = 8: I–VIII, one each, r 2.2 m, spread ≥ 5 m apart, all still and all hit once ----
            yield return SoStart(SoCircle(8, 9f));
            if (so == null) yield break;
            party = d.World.Players.ToList();
            Check("Striking Order N = 8: I–VIII, one each, r 2.2 m", so.Owners.Count == 8 && so.Owners.Distinct().Count() == 8 && Mathf.Approximately(so.RingRadius, 2.2f)
                  && so.NumeralsShown == 8, $"{so.Owners.Count} rings, r {so.RingRadius}");
            maxPatterns = 0;
            for (int t = SoTick; t < 605; t++) { BkStep(1); maxPatterns = Mathf.Max(maxPatterns, BkPatternsLive()); }
            Check("Striking Order N = 8: rings at 180 … 600, one every 1.0 s", so.RangAt.SequenceEqual(Enumerable.Range(0, 8).Select(k => 180 + 60 * k)), string.Join(",", so.RangAt));
            Check("Striking Order N = 8: every still player hit exactly once, by their own ring", party.All(p => SoTotal(p) == 1 && SoHits(p, so.Rings[so.Owners.ToList().IndexOf(p)]) == 1),
                  string.Join(" ", party.Select(p => SoTotal(p))));
            Check("Striking Order N = 8: still one damaging pattern", maxPatterns <= 1, $"max {maxPatterns}");
            SoAudit("N = 8");

            // ---- a downed owner cancels their ring (and numeral) if it hasn't locked ----
            yield return SoStart(new[] { new Vector3(0, 0, 6), new Vector3(0, 0, -6) });
            if (so == null) yield break;
            A = so.Owners[0]; B = so.Owners[1];
            SoStepTo(90);                                        // ring II spawned at 60, locks at 120
            B.ReceiveHit(new Hazard { Source = "test: down", Damage = 999 });
            BkStep(1);
            bool cancelled = so.Rings[1] != null && so.Rings[1].Cancelled && so.NumeralsOn(B) == 0;
            SoStepTo(365);
            Check("Striking Order: an owner who goes down before lock cancels their rings and numerals (II and IV)", B.Down && cancelled && so.Rings[3] == null
                  && so.RangAt.SequenceEqual(new[] { 180, 300 }), $"B down {B.Down}, rang at {string.Join(",", so.RangAt)}");
            Check("Striking Order: the other player's rings still ring (I and III)", SoHits(A, so.Rings[0]) == 1 && SoHits(A, so.Rings[2]) == 1, $"A hits at {SoTicks(A)}");
            SoAudit("downed owner");

            // ---- boss death mid-windup cancels everything ----
            yield return SoStart(new[] { new Vector3(0, 0, 6) });
            if (so == null) yield break;
            SoStepTo(100);
            int spawned = so.Spawned.Count;
            bk.DebugDefeat();
            BkStep(400);
            Check("Striking Order: boss death mid-windup cancels every ring and numeral", spawned == 2 && SoTotal(d.Player) == 0
                  && !d.World.Hazards.Live.Any(h => h.Source == so.Name) && so.NumeralsShown == 0 && !so.Running, $"{spawned} rings were up; hits {SoTotal(d.Player)}");

            // ---- Easy / Hard ----
            yield return SoStart(new[] { new Vector3(0, 0, 6) }, Difficulty.Easy);
            if (so == null) yield break;
            SoStepTo(482);
            Check("Striking Order Easy: step 1.4 s, ring I at 3.75 s (225 / 309 / 393 / 477)", so.SpawnedAt.SequenceEqual(new[] { 0, 84, 168, 252 }) && so.RangAt.SequenceEqual(new[] { 225, 309, 393, 477 }),
                  $"spawned {string.Join(",", so.SpawnedAt)}; rang {string.Join(",", so.RangAt)}");
            Check("Striking Order Easy: locks 2.5 s before, radius 2.2 m (−0.4), 15 damage", so.Rings.All(r => r.ResolveAt - r.LockAt == 150) && Mathf.Approximately(so.RingRadius, 2.2f)
                  && bkHits.Where(x => x.Source == so.Name).All(x => x.Damage == 15) && SoTotal(d.Player) > 0, $"r {so.RingRadius}, hits {SoTicks(d.Player)}");
            SoAudit("Easy");
            yield return SoStart(new[] { new Vector3(0, 0, 6) }, Difficulty.Hard);
            if (so == null) yield break;
            SoStepTo(365);
            Check("Striking Order Hard: Medium timings and radius, damage ×1.3", so.RangAt.SequenceEqual(new[] { 180, 240, 300, 360 }) && Mathf.Approximately(so.RingRadius, 2.6f)
                  && so.Rings.All(r => r.ResolveAt - r.LockAt == 120) && bkHits.Where(x => x.Source == so.Name).All(x => x.Damage > 25) && SoTotal(d.Player) > 0,
                  $"rang {string.Join(",", so.RangAt)}, damage {string.Join("/", bkHits.Where(x => x.Source == so.Name).Select(x => x.Damage))}");
            SoAudit("Hard");

            Check("Striking Order: one hit per ring instance per player, in every scenario", soOneHitEach, soOneHitDetail);

            // ---- clip: practice with three stand-ins (they step out of rings about to strike) ----
            yield return BkStart("striking_order");
            if (bk == null) yield break;
            ScriptedParty.RemoveExtras(d.World, d.Player);
            BkPlace(d.Player, new Vector3(0, 0, -7));
            ScriptedParty.AddStandIns(d.World, 3);
            yield return BkClip("bk_striking_order", 6.5f);
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }
    }
}
