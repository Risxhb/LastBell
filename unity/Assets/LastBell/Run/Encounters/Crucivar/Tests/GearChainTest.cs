using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Part C §3 checks for Gear Chain (brief 02-crucivar.gear_chain.md) with scripted party members. The boss stands at
    // (0, 0, -1.5), the rim is r 13.7. "Solve" walks every chain to 8 m at 4.2 m/s (no sprint, so no stamina is spent).
    public sealed class GearChainTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.gear_chain";
        const string Src = "Gear Chain";

        CrucivarV2AutoTest t;
        GearChain Chain => t.Boss.Chain;
        IEnumerable<CrucivarV2AutoTest.HitRec> ChainHits => t.Hits.Where(h => h.Source == Src);
        int Expected(Difficulty d) => Mathf.RoundToInt(GearChain.Damage * RunDifficulty.DamageTakenScale(d));

        public override IEnumerator Run(CrucivarV2AutoTest test)
        {
            t = test;

            // ---- melee + ranged standing still: hugging the boss, chained to a partner at the edge ----
            yield return Cast(2, Difficulty.Medium, new[] { V(0, 0.8f), V(0, 13f) }, solve: false, shotAt: 1.5f, shot: "gear_chain_out_of_band");
            if (t.Boss == null) yield break;
            var sp = t.Spawned(Src).ToList();
            t.Check("N=2: one chain, one Tether hazard", sp.Count == 1 && sp[0].H.Shape == HazardShape.Tether && Chain.Links.Count == 1, $"spawns {sp.Count}");
            int R = sp.Count > 0 ? sp[0].H.ResolveAt : -1;
            t.Check("telegraph 3.0 s on Medium", R == Sim.Ticks(3.0f), $"{R} ticks");
            var hits = ChainHits.ToList();
            t.Check("melee (2.3 m from the boss) standing still is hit", t.WasHit(t.Party[0], Src));
            t.Check("ranged at the edge (13 m) standing still is hit", t.WasHit(t.Party[1], Src));
            t.Check("both hit on the resolve tick", sp.Count > 0 && hits.Count == 2 && hits.All(h => h.Tick == sp[0].Tick + R - 1),
                string.Join(", ", hits.Select(h => h.Tick - (sp.Count > 0 ? sp[0].Tick : 0))));
            t.Check("snap deals 28 Ground on Medium", hits.All(h => h.Damage == Expected(Difficulty.Medium) && h.H.Kind == HazardKind.Ground && h.H.Knockback == 0 && h.H.Stun == Sim.Ticks(0.25f)),
                string.Join(", ", hits.Select(h => h.Damage)));
            t.Check("no knockback: the snapped players didn't move", Moved(t.Party[0], V(0, 0.8f)) < 0.05f && Moved(t.Party[1], V(0, 13f)) < 0.05f);

            // ---- partners on the same spot ----
            yield return Cast(2, Difficulty.Medium, new[] { V(2, 5), V(3, 5) }, solve: false);
            t.Check("partners standing together (1 m) are both hit (too slack)", ChainHits.Count() == 2);
            yield return Cast(2, Difficulty.Medium, new[] { V(2, 5), V(3, 5) }, solve: true);
            t.Check("escape: partners on the same spot walk apart at 4.2 m/s, no hit", !ChainHits.Any(), Lengths());

            // ---- worst case: partners at opposite edges ----
            yield return Cast(2, Difficulty.Medium, new[] { V(-13, 0), V(13, 0) }, solve: false);
            t.Check("partners at opposite edges standing still are both hit (too taut)", ChainHits.Count() == 2);
            yield return Cast(2, Difficulty.Medium, new[] { V(-13, 0), V(13, 0) }, solve: true, shotAt: 2.6f, shot: "gear_chain_in_band");
            t.Check("escape (worst case): opposite edges walk in at 4.2 m/s, no hit", !ChainHits.Any(), Lengths() + $" in band after {inBandAt:0.00} s");
            t.Check("worst case reaches the band with margin (≥ 0.5 s before resolve)", inBandAt >= 0 && inBandAt <= GearChain.Warn - 0.5f, $"{inBandAt:0.00} s");
            t.Check("walking spends no stamina (finite stamina never runs out)", t.Party.All(p => p.Stamina >= PlayerController.MaxStamina - 0.01f));

            // ---- solo: chained to the rim post nearest the player ----
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 13f) }, solve: false, shotAt: 1.2f, shot: "gear_chain_solo_post");
            var l0 = Chain.Links.FirstOrDefault();
            t.Check("solo: one chain to a rim post at the nearest rim point (r 13.7)", Chain.Links.Count == 1 && l0.ToPost && Vector3.Distance(l0.Post, V(0, CrucivarAbility.Rim)) < 0.05f,
                l0 != null ? l0.Post.ToString() : "none");
            t.Check("solo at the edge standing still is hit (0.7 m from the post)", ChainHits.Count() == 1);
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 0.8f) }, solve: false);
            t.Check("solo melee hugging the boss standing still is hit (12.9 m from the post)", ChainHits.Count() == 1);
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 9f) }, solve: false);
            t.Check("solo ranged at 9 m standing still is hit (4.7 m from the post)", ChainHits.Count() == 1);
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 13f) }, solve: true);
            t.Check("solo escape: walk 6 m in from the edge at 4.2 m/s, no hit", !ChainHits.Any(), Lengths());
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 0.8f) }, solve: true);
            t.Check("solo escape: walk out from the boss toward the post, no hit", !ChainHits.Any(), Lengths());

            // ---- N = 3: nearest with farthest, the middle player to a post ----
            var a3 = new[] { V(0, 1f), V(6, 0), V(0, 11f) };
            yield return Cast(3, Difficulty.Medium, a3, solve: false);
            t.Check("N=3: 1 pair (nearest + farthest) + the middle player on a post", Chain.Links.Count == 2 && Paired(0, 2) && PostFor(1, V(CrucivarAbility.Rim, 0)), Describe());
            yield return Cast(3, Difficulty.Medium, a3, solve: true);
            t.Check("N=3 escape, no hit", !ChainHits.Any(), Lengths());

            // ---- N = 4 ----
            var a4 = new[] { V(8, -1.5f), V(-2.5f, -1.5f), V(0, 9.5f), V(-5, 2.5f) };   // 8, 2.5, 11, ~6.4 m from the boss
            yield return Cast(4, Difficulty.Medium, a4, solve: false);
            t.Check("N=4: 2 pairs, nearest with farthest, second with third", Chain.Links.Count == 2 && Paired(1, 2) && Paired(3, 0) && Chain.Links.All(l => !l.ToPost), Describe());
            yield return Cast(4, Difficulty.Medium, a4, solve: true);
            t.Check("N=4 escape, no hit", !ChainHits.Any(), Lengths());
            yield return Cast(4, Difficulty.Medium, Cluster(4), solve: false);
            t.Check("N=4 clustered standing still: all 4 hit once", ChainHits.Count() == 4 && ChainHits.Select(h => h.P).Distinct().Count() == 4);

            // ---- N = 8 ----
            yield return Cast(8, Difficulty.Medium, Cluster(8), solve: false, shotAt: 1.5f, shot: "gear_chain_n8");
            t.Check("N=8: 4 pairs, no post", Chain.Links.Count == 4 && Chain.Links.All(l => !l.ToPost), Describe());
            t.Check("N=8 clustered standing still: all 8 hit exactly once", ChainHits.Count() == 8 && ChainHits.Select(h => h.P).Distinct().Count() == 8);
            yield return Cast(8, Difficulty.Medium, Spread(8, 7f), solve: true);
            t.Check("N=8 spread, every pair walks to 8 m: no hit", !ChainHits.Any(), Lengths());

            // ---- with Slag Rim (rim floods r > 9.5 m): stay inside the 9.2 m disc ----
            yield return Cast(2, Difficulty.Medium, new[] { V(-8.5f, 0), V(8.5f, 0) }, solve: true, slagRim: true);
            t.Check("with Slag Rim: partners on opposite sides of the disc walk in, no chain hit", !ChainHits.Any(), Lengths());
            t.Check("with Slag Rim: both partners end inside the 9.2 m disc", t.Party.All(p => Sim.Flat(p.transform.position).magnitude <= 9.2f));
            yield return Cast(1, Difficulty.Medium, new[] { V(0, 9f) }, solve: true, slagRim: true);
            t.Check("with Slag Rim: solo walks in from the disc edge, no chain hit", !ChainHits.Any(), Lengths());
            t.Note("Slag Rim on this branch is the Bellows Draw stub (no hazard yet); the integration branch runs it for real");

            // ---- a downed partner: no snap; the other end is re-chained to a post with the remaining time ----
            // (the scripted member goes down: downing the director's own player alone would end the practice)
            yield return Cast(2, Difficulty.Medium, new[] { V(3, 12.8f), V(2, 5) }, solve: false, during: DownAt(1, 1.0f));
            var ld = Chain.Links.FirstOrDefault();
            t.Check("downed partner takes no snap", !t.WasHit(t.Party[1], Src));
            t.Check("partner re-chained to the rim post nearest them", ld != null && ld.ToPost && ld.A == t.Party[0] && Vector3.Distance(ld.Post, Sim.Flat(t.Party[0].transform.position).normalized * CrucivarAbility.Rim) < 0.1f, Describe());
            t.Check("re-chained partner at the edge still snaps on the original resolve tick", ChainHits.Count() == 1 && ChainHits.First().P == t.Party[0] && ChainHits.First().Tick == t.Spawned(Src).First().Tick + Sim.Ticks(3f) - 1);
            yield return Cast(3, Difficulty.Medium, a3, solve: false, during: DownAt(1, 1.0f));
            t.Check("post-chained player downed mid-telegraph: the chain ends, no snap", !t.WasHit(t.Party[1], Src) && Chain.Links.Any(l => l.ToPost && l.Ended == "down"), Describe());

            // ---- a dash breaks the chain with no snap (provisional rule), costs 30 stamina ----
            yield return Cast(2, Difficulty.Medium, new[] { V(2, 5), V(3, 5) }, solve: false, during: DashAt(1, 1.0f));
            t.Check("a dash breaks the chain: nobody snaps", !ChainHits.Any() && Chain.Links.All(l => l.Ended == "dash"), Describe());
            t.Check("the dash cost 30 stamina", Mathf.Abs(dashStamina - (PlayerController.MaxStamina - PlayerController.DashCost)) < 1f, $"{dashStamina:0}");
            yield return Cast(2, Difficulty.Medium, new[] { V(2, 5), V(3, 5) }, solve: false, during: DashAt(1, 1.0f, enabled: false));
            t.Check("no dash (stamina/abilities unavailable): the chain still snaps", ChainHits.Count() == 2);

            // ---- Easy: ×1.25 warning, band 5-11 m ----
            yield return Cast(2, Difficulty.Easy, new[] { V(-2.75f, 5), V(2.75f, 5) }, solve: false);
            int rEasy = t.Spawned(Src).Select(s => s.H.ResolveAt).FirstOrDefault();
            t.Check("Easy: telegraph 3.75 s (×1.25)", rEasy == Sim.Ticks(3.75f), $"{rEasy} ticks");
            t.Check("Easy: 5.5 m apart is inside the 5-11 m band, no hit", !ChainHits.Any());
            yield return Cast(2, Difficulty.Easy, new[] { V(-5.25f, 5), V(5.25f, 5) }, solve: false);
            t.Check("Easy: 10.5 m apart is inside the band, no hit", !ChainHits.Any());
            yield return Cast(2, Difficulty.Easy, new[] { V(2, 5), V(3, 5) }, solve: false);
            t.Check("Easy: 1 m apart still snaps, 28 × 0.6", ChainHits.Count() == 2 && ChainHits.All(h => h.Damage == Expected(Difficulty.Easy)), string.Join(", ", ChainHits.Select(h => h.Damage)));
            yield return Cast(2, Difficulty.Medium, new[] { V(-2.75f, 5), V(2.75f, 5) }, solve: false);
            t.Check("Medium: the same 5.5 m snaps (band 6-10 m)", ChainHits.Count() == 2);
            yield return Cast(2, Difficulty.Medium, new[] { V(-5.25f, 5), V(5.25f, 5) }, solve: false);
            t.Check("Medium: the same 10.5 m snaps", ChainHits.Count() == 2);
            yield return Cast(2, Difficulty.Medium, new[] { V(-4, 5), V(4, 5) }, solve: false);
            t.Check("Medium: 8 m apart standing still is safe", !ChainHits.Any());

            // ---- Hard: same band and timing, global ×1.3 damage ----
            yield return Cast(2, Difficulty.Hard, new[] { V(2, 5), V(3, 5) }, solve: false);
            int rHard = t.Spawned(Src).Select(s => s.H.ResolveAt).FirstOrDefault();
            t.Check("Hard: telegraph 3.0 s, band 6-10 m (no change)", rHard == Sim.Ticks(3f) && Chain.Min == 6 && Chain.Max == 10, $"{rHard} ticks");
            t.Check("Hard: snap deals 28 × 1.3", ChainHits.Count() == 2 && ChainHits.All(h => h.Damage == Expected(Difficulty.Hard)), string.Join(", ", ChainHits.Select(h => h.Damage)));

            t.Check("one hit per instance (every Gear Chain case)", t.OneHitPerInstance());

            // ---- boss death mid-windup cancels the chains (last: the practice ends) ----
            yield return t.Setup(2, Difficulty.Medium, new[] { V(2, 5), V(3, 5) });
            if (t.Boss == null) yield break;
            t.Begin(t.Boss.Chain.Run());
            yield return t.Until(() => t.Spawned(Src).Any(), 5, "chain clamped");
            yield return t.Seconds(1.5f);
            t.Check("chains drawn during the windup", GearChainFx.Count == 1, $"{GearChainFx.Count}");
            t.KillBoss();
            yield return t.Seconds(GearChain.Warn + 0.5f);
            t.Check("boss death mid-windup: no snap afterwards", !ChainHits.Any());
            t.Check("boss death mid-windup: no hazard or chain left", t.W.Hazards.Count == 0 && GearChainFx.Count == 0, $"hazards {t.W.Hazards.Count}, chains {GearChainFx.Count}");
        }

        // ---------------- helpers ----------------
        float inBandAt;

        // Sets up n players, casts Gear Chain, optionally walks every chain to 8 m, waits for the resolve.
        IEnumerator Cast(int n, Difficulty d, Vector3[] at, bool solve, float shotAt = -1, string shot = null, bool slagRim = false, System.Func<IEnumerator> during = null)
        {
            yield return t.Setup(n, d, at);
            if (t.Boss == null) yield break;
            if (slagRim) t.Boss.Bellows.SlagRim();
            inBandAt = -1;
            t.Begin(t.Boss.Chain.Run());
            yield return t.Until(() => t.Spawned(Src).Any(), 5, "chain clamped");
            if (t.Boss == null || !t.Spawned(Src).Any()) yield break;
            int start = t.Spawned(Src).First().Tick;
            if (solve) Solve();
            if (during != null) t.StartCoroutine(during());
            bool shotTaken = shot == null;
            float guard = 0;
            while (t.Boss != null && t.Boss.TestBusy && guard < 15)
            {
                guard += Time.deltaTime;
                float s = (t.W.Tick - start) * Sim.Dt;
                if (inBandAt < 0 && Chain.Links.Count > 0 && Chain.Links.All(l => l.Ended != null || Chain.InBand(l))) inBandAt = s;
                if (!shotTaken && s >= shotAt) { shotTaken = true; yield return t.Shot(shot); }
                yield return null;
            }
            foreach (var p in t.Party) t.In(p).Stop();
        }

        // every chain to 8 m along its own line, at 4.2 m/s
        void Solve()
        {
            foreach (var l in Chain.Links)
            {
                var a = Sim.Flat(l.A.transform.position);
                if (l.ToPost)
                {
                    var dir = a - l.Post; if (dir.magnitude < 0.5f) dir = -l.Post;
                    t.In(l.A).Go(l.Post + dir.normalized * 8f, 4.2f);
                }
                else
                {
                    var b = Sim.Flat(l.B.transform.position); var mid = (a + b) * 0.5f;
                    var dir = b - a; if (dir.magnitude < 0.1f) dir = Vector3.right;
                    dir = dir.normalized;
                    t.In(l.A).Go(mid - dir * 4f, 4.2f); t.In(l.B).Go(mid + dir * 4f, 4.2f);
                }
            }
        }

        System.Func<IEnumerator> DownAt(int who, float s) => () => Do(s, () => t.Party[who].ForceDown(new Hazard { Source = "test: downed" }));
        float dashStamina;
        // a dash pressed after s seconds (enabled: false = the press is swallowed, the scripted member can't dash)
        System.Func<IEnumerator> DashAt(int who, float s, bool enabled = true) => () => Dash(t.Party[who], s, enabled);
        IEnumerator Dash(PlayerController p, float s, bool enabled)
        {
            yield return t.Seconds(s);
            p.AbilitiesEnabled = enabled; t.In(p).Dash();
            yield return t.Ticks(2);
            dashStamina = p.Stamina;
        }
        IEnumerator Do(float s, System.Action act) { yield return t.Seconds(s); act(); }

        bool Paired(int i, int j) => Chain.Links.Any(l => !l.ToPost && ((l.A == t.Party[i] && l.B == t.Party[j]) || (l.A == t.Party[j] && l.B == t.Party[i])));
        bool PostFor(int i, Vector3 post) => Chain.Links.Any(l => l.ToPost && l.A == t.Party[i] && Vector3.Distance(l.Post, post) < 0.1f);
        string Describe() => string.Join("; ", Chain.Links.Select(l => $"P{t.Party.IndexOf(l.A)}-{(l.ToPost ? "post" + l.Post.ToString("0.0") : "P" + t.Party.IndexOf(l.B))}{(l.Ended != null ? " ended " + l.Ended : "")}"));
        string Lengths() => "lengths " + string.Join(", ", Chain.Links.Select(l => l.A ? l.Length.ToString("0.0") : "-"));
        static float Moved(PlayerController p, Vector3 from) => Sim.Flat(p.transform.position - from).magnitude;
        static Vector3 V(float x, float z) => new Vector3(x, 0, z);
        static Vector3[] Cluster(int n) => Enumerable.Range(0, n).Select(i => V(0, 5) + Quaternion.Euler(0, 360f * i / n, 0) * Vector3.right * 1.5f).ToArray();
        static Vector3[] Spread(int n, float r) => Enumerable.Range(0, n).Select(i => Quaternion.Euler(0, 360f * i / n + 10f, 0) * Vector3.forward * r).ToArray();
    }
}
