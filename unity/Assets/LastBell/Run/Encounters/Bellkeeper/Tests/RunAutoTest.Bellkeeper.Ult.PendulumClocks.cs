using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: Pendulum Clocks checks (ULTIMATE.md §3.1, §5, §6; GUIDE.md). Owned by the Pendulum Clocks session.
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot). A halted boss
    // doesn't step its moves, so the mechanic is cast through its practice hook with a preset cue (ClkStart).
    public sealed partial class RunAutoTest
    {
        PendulumClocks clk;
        readonly List<PlayerController> clkParty = new List<PlayerController>();
        static readonly float[] ClkSingles = { 6f, 11f, 16f, 21f }, ClkPairs = { 6f, 12.5f };
        static int ClkMod4(int i) => ((i % 4) + 4) % 4;
        static UltCue ClkCue(string variant, int seed, float[] at) => new UltCue { Variant = variant, Seed = seed, At = at };
        int ClkHits(PlayerController p) => BkHitsOn(p, PendulumClocks.Source);
        int ClkAllHits() => bkHits.Count(h => h.Source == PendulumClocks.Source);
        static bool ClkNear(Vector3 a, Vector3 b, float tol = 0.02f) => Sim.Flat(a - b).magnitude < tol;
        static Vector3 ClkSide(int station) => Vector3.Cross(Vector3.up, UltimateState.StationDir(station));

        // practice fight with N players, the clocks launched with `cue` on the first tick; extras alternate melee (6 m) and ranged (11 m)
        IEnumerator ClkStart(int n, UltCue cue, Vector3? mePos = null)
        {
            yield return UltStart(UltimateMechanics.PendulumClocks);
            if (bk == null) yield break;
            clk = bk.Mechanic<PendulumClocks>();
            clkParty.Clear(); clkParty.Add(d.Player);
            if (mePos.HasValue) BkPlace(d.Player, mePos.Value);
            for (int i = 1; i < n; i++) clkParty.Add(BkAdd(Sim.Dir(360f * i / n + 20) * (i % 2 == 0 ? 11f : 6f)));
            PendulumClocks.PracticeCue = cue;
            bk.BeginCombat();
            UltStepAlive(1);
            PendulumClocks.PracticeCue = null;
            if (clk.Casts != 1 || !clk.Running) Check("clocks: the practice hook launched the clocks with the test cue", false, $"casts {clk.Casts}");
        }
        // steps until the cast is over, calling `before` with the tick about to run and `after` after it
        void ClkRun(System.Action<int> before = null, System.Action after = null, int max = 3000)
        {
            for (int i = 0; i < max && clk.Running; i++) { before?.Invoke(d.World.Tick + 1); UltStepAlive(1); after?.Invoke(); }
        }
        List<UltimateBot> ClkDrive(float speed = 4.2f, bool attacks = false)
        {
            var bots = new List<UltimateBot>();
            for (int i = 0; i < clkParty.Count; i++) { var b = UltimateParty.Drive(clkParty[i], i, clkParty.Count); b.Speed = speed; b.Attacks = attacks; bots.Add(b); }
            return bots;
        }

        IEnumerator UltPendulumClocksChecks()
        {
            Note("Ultimate: Pendulum Clocks (proposal)");

            // ---------------- 1. solo: assignment, glyph, draw / lock / resolve ticks, the clock spot strikes the boss ----------------
            yield return ClkStart(1, ClkCue("singles", 5, ClkSingles));
            if (bk == null) yield break;
            var me = d.Player; var ss = clk.Strikes.ToList(); int lt = clk.LastCastTick;
            Check("clocks solo: hours I-IV all on you, hands circling the boss (seeded start and turn)",
                  ss.Count == 4 && ss.All(s => s.Owner == me) && ss.Select(s => s.Hour).SequenceEqual(new[] { 1, 2, 3, 4 })
                  && Enumerable.Range(1, 3).All(k => ClkMod4(ss[k].Station - ss[k - 1].Station) == ClkMod4(clk.Turn)),
                  string.Join(" ", ss.Select(s => UltimateState.StationNames[s.Station])));
            Check("clocks singles: hour k strikes exactly At[k] after launch; numerals appear >= 6 s before hour I",
                  ss.Select(s => s.Tick - lt).SequenceEqual(ClkSingles.Select(Sim.Ticks)) && ss[0].Tick - lt >= 360, string.Join(", ", ss.Select(s => (s.Tick - lt) / 60f)));
            var st0 = bk.Ult.Of(me); string glyph = bk.Ult.GlyphText(me, d.World.Tick);
            Check("clocks: the glyph shows your numeral and your hand's station", st0.Hour == 1 && st0.Station == ss[0].Station && st0.StrikeTick == ss[0].Tick
                  && glyph != null && glyph.StartsWith("I " + UltimateState.StationNames[ss[0].Station]), glyph);
            Check("Leaning Bell pairing: LeanDirAt = the striking hour's station, null on any other tick",
                  ss.All(s => clk.LeanDirAt(s.Tick).HasValue && ClkNear(clk.LeanDirAt(s.Tick).Value, UltimateState.StationDir(s.Station)) && !clk.LeanDirAt(s.Tick + 1).HasValue && !clk.LeanDirAt(s.Tick - 1).HasValue));
            {
                var drawn = new Dictionary<PendulumClocks.Strike, int>(); var resolved = new Dictionary<PendulumClocks.Strike, int>();
                bool tracked = false, held = false, shape = true; string trackInfo = "";
                var two = ss[1];
                ClkRun(now =>
                {
                    var next = ss.FirstOrDefault(s => !s.Resolved); if (next == null) return;
                    var spot = clk.Spot(next.Station);
                    if (next == two && now > two.LockTick) BkPlace(me, spot + ClkSide(two.Station) * 3f);        // leaves after the lock
                    else if (next == two && now >= two.LockTick - 30 && now <= two.LockTick - 21) BkPlace(me, spot + ClkSide(two.Station));   // tracking probe
                    else if (now <= next.LockTick) BkPlace(me, spot);
                }, () =>
                {
                    int t = d.World.Tick;
                    foreach (var s in ss)
                    {
                        if (s.Cone != null && !drawn.ContainsKey(s)) { drawn[s] = t; shape &= s.Cone.Shape == HazardShape.Cone && s.Cone.Kind == HazardKind.Strike && s.Cone.Radius == 6f && s.Cone.HalfAngle == 25f && !s.Cone.HitOnResolve; }
                        if (s.Resolved && !resolved.ContainsKey(s)) resolved[s] = t;
                    }
                    if (t == two.LockTick - 25 && two.Cone != null) { tracked = ClkNear(two.Cone.Origin, me.transform.position) && !two.Cone.Locked; trackInfo = $"{two.Cone.Origin} vs {me.transform.position}"; }
                    if (t == two.LockTick + 10 && two.Cone != null) held = two.Cone.Locked && ClkNear(two.Cone.Origin, clk.Spot(two.Station)) && !ClkNear(me.transform.position, clk.Spot(two.Station), 1f);
                });
                Check("clocks: each cone draws exactly 3.0 s before its hour (a 6 m, 25° Strike cone)", ss.All(s => drawn.TryGetValue(s, out int t) && t == s.Tick - 180) && shape,
                      string.Join(", ", ss.Select(s => drawn.TryGetValue(s, out int t) ? ((s.Tick - t) / 60f).ToString("0.00") : "-")));
                Check("clocks: the cone tracks its owner until it locks 1.5 s before, then stays put", tracked && held, trackInfo);
                Check("clocks: each hour resolves exactly on its tick", ss.All(s => resolved.TryGetValue(s, out int t) && t == s.Tick));
                Check("clocks: from the clock spot (far side, 5.2 m) every hour strikes the boss: no Cracked Hour, nobody hurt",
                      ss.All(s => s.StruckBoss && !s.Cracked) && clk.Cracks == 0 && bkHits.Count == 0 && bk.Ult.CrackedCount == 0, string.Join(", ", bkHits.Select(h => h.Source)));
                Check("clocks: the pendulum beats once per hour (rig clip HourTick, stand-in flare)", clk.Beats == 4);
                Check("clocks: the numerals clear when the last hour has struck", bk.Ult.Of(me).Hour == 0 && bk.Ult.GlyphText(me, d.World.Tick) == null && bk.Ult.StationOf(me) == ss[3].Station);
            }

            // ---------------- 2. solo failures: station side, too far (ranged), right line (5.2 m), hugging (melee) ----------------
            yield return ClkStart(1, ClkCue("singles", 5, ClkSingles));
            if (bk == null) yield break;
            me = d.Player; ss = clk.Strikes.ToList();
            {
                bool crackedAfterI = false, zeroDealt = false;
                ClkRun(now =>
                {
                    var next = ss.FirstOrDefault(s => !s.Resolved); if (next == null || now > next.LockTick) return;
                    int k = next.Hour - 1;
                    BkPlace(me, k == 0 ? UltimateState.StationDir(next.Station) * 5f : k == 1 ? clk.Spot(next.Station, 8.6f) : k == 2 ? clk.Spot(next.Station) : clk.Spot(next.Station, 2.0f));
                }, () =>
                {
                    if (d.World.Tick == ss[0].Tick + 1) { crackedAfterI = bk.Ult.IsCracked(me); zeroDealt = d.World.DealtScale != null && d.World.DealtScale(me) == 0; }
                });
                var cracks = bkHits.Where(h => h.Source == PendulumClocks.CrackSource && h.Player == me).ToList();
                Check("clocks: standing on the station side cracks the hour (30 to everyone, you deal 0 for 20 s)", ss[0].Cracked && crackedAfterI && zeroDealt
                      && cracks.Any(h => h.Tick == ss[0].Tick && h.Damage == UltimateState.CrackedPartyDamage));
                Check("clocks: a still ranged player (8.6 m out on the right line) cracks the hour: the cone reaches 6 m", ss[1].Cracked && cracks.Any(h => h.Tick == ss[1].Tick));
                Check("clocks: the right line at 5.2 m and hugging the boss on the far side (2.0 m) both strike him", !ss[2].Cracked && !ss[3].Cracked && cracks.Count == 2 && clk.Cracks == 2 && bk.Ult.CrackedCount == 2,
                      $"cracks {string.Join(",", ss.Select(s => s.Cracked ? 1 : 0))}");
            }

            // ---------------- 3. N = 2: your cone strikes an ally (60); outside it is safe; a downed striker's hour still cracks ----------------
            yield return ClkStart(2, ClkCue("singles", 9, ClkSingles));
            if (bk == null) yield break;
            me = d.Player; var ally = clkParty[1]; ss = clk.Strikes.ToList();
            Check("clocks N = 2: I and III to one player, II and IV to the other", ss.Count == 4 && ss[0].Owner == ss[2].Owner && ss[1].Owner == ss[3].Owner && ss[0].Owner != ss[1].Owner);
            {
                var mine = ss.First(s => s.Owner == me); var theirs = ss.First(s => s.Owner == ally); var allyLast = ss.Last(s => s.Owner == ally);
                bool downed = false;
                ClkRun(now =>
                {
                    var next = ss.FirstOrDefault(s => !s.Resolved); if (next == null || now > next.LockTick) return;
                    if (next == mine) { BkPlace(me, clk.Spot(mine.Station)); BkPlace(ally, clk.Spot(mine.Station, 3.0f)); }   // still, between the striker and the boss
                    else if (next == theirs) { BkPlace(ally, clk.Spot(theirs.Station)); BkPlace(me, clk.Spot(theirs.Station, 5.2f, 90)); }
                    else if (next.Owner == me) BkPlace(me, clk.Spot(next.Station));
                }, () =>
                {
                    if (!downed && mine.Resolved && theirs.Resolved && !allyLast.Resolved)
                    {
                        downed = true;
                        ally.ReceiveHit(new Hazard { Source = "Down probe", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = 99, Damage = UltimateState.Lethal * 10, Knockback = 0, Stun = 0 });
                    }
                });
                var allyHits = bkHits.Where(h => h.Player == ally && h.Source == PendulumClocks.Source).ToList();
                Check("clocks: a still player between a striker and the boss takes 60 from the clock (once)", allyHits.Count == 1 && allyHits[0].Damage == PendulumClocks.AllyDamage && allyHits[0].Tick == mine.Tick && mine.Struck.Contains(ally),
                      string.Join(", ", allyHits.Select(h => h.Damage + "@" + (h.Tick - mine.Tick))));
                Check("clocks: the striker is never hit by their own clock, and a player 90° round the boss is outside the ally's cone", ClkHits(me) == 0 && !mine.Cracked && !theirs.Cracked);
                Check("clocks (hardened): a striker who is down when their hour comes still cracks it (30 to the others)",
                      ally.Down && allyLast.Cracked && bkHits.Any(h => h.Player == me && h.Source == PendulumClocks.CrackSource && h.Tick == allyLast.Tick) && clk.Cracks == 1,
                      $"cracks {clk.Cracks}");
            }

            // ---------------- 4. escape: an ally caught in a locked cone walks out at 4.2 m/s ----------------
            yield return ClkStart(2, ClkCue("singles", 9, ClkSingles));
            if (bk == null) yield break;
            me = d.Player; ally = clkParty[1]; ss = clk.Strikes.ToList();
            {
                var mine = ss.First(s => s.Owner == me);
                var inside = clk.Spot(mine.Station, 3.2f); bool wasInside = false;
                var sp = BkScript(ally); sp.Speed = 4.2f;
                ClkRun(now =>
                {
                    if (mine.Resolved) return;
                    if (now <= mine.LockTick) { BkPlace(me, clk.Spot(mine.Station)); BkPlace(ally, inside); }
                    else if (now == mine.LockTick + 1) sp.Goal = inside + ClkSide(mine.Station) * 3.1f;   // reacts only after the lock
                }, () => { if (d.World.Tick == mine.LockTick && mine.Cone != null) wasInside = mine.Cone.Contains(ally.transform.position); });
                Check("escape: an ally inside a locked cone leaves it in 1.5 s at 4.2 m/s (walk, no dash)", wasInside && ClkHits(ally) == 0, $"ally at {ally.transform.position}");
            }

            // ---------------- 5. assignment per party size, pairs timing, Leaning Bell pairing ----------------
            yield return ClkStart(1, ClkCue("pairs", 2, ClkPairs));
            if (bk == null) yield break;
            ss = clk.Strikes.ToList(); lt = clk.LastCastTick;
            Check("clocks pairs solo: I at At[0], II 2.0 s later; III at At[1], IV 2.0 s later; LeanDirAt(III) = III's station",
                  ss.Select(s => s.Tick - lt).SequenceEqual(new[] { 360, 480, 750, 870 }) && ClkNear(clk.LeanDirAt(ss[2].Tick) ?? Vector3.zero, UltimateState.StationDir(ss[2].Station)),
                  string.Join(", ", ss.Select(s => (s.Tick - lt) / 60f)));
            yield return ClkStart(2, ClkCue("pairs", 2, ClkPairs));
            if (bk == null) yield break;
            ss = clk.Strikes.ToList(); lt = clk.LastCastTick;
            Check("clocks pairs N = 2: I+II strike together at At[0] (one each), III+IV at At[1]",
                  ss[0].Tick == ss[1].Tick && ss[0].Tick - lt == 360 && ss[2].Tick == ss[3].Tick && ss[2].Tick - lt == 750 && ss[0].Owner != ss[1].Owner && ss[2].Owner != ss[3].Owner);
            yield return ClkStart(4, ClkCue("singles", 4, ClkSingles));
            if (bk == null) yield break;
            ss = clk.Strikes.ToList();
            Check("clocks N = 4: one hour each, four different hands", ss.Count == 4 && ss.Select(s => s.Owner).Distinct().Count() == 4 && ss.Select(s => s.Station).Distinct().Count() == 4);
            yield return ClkStart(4, ClkCue("pairs", 4, ClkPairs));
            if (bk == null) yield break;
            ss = clk.Strikes.ToList();
            {
                bool ok = true; var info = new List<string>();
                foreach (var tick in ss.Select(s => s.Tick).Distinct())
                {
                    var at = ss.Where(s => s.Tick == tick).ToList(); var dir = clk.LeanDirAt(tick);
                    bool adjacent = at.Count == 2 && ClkMod4(at[0].Station - at[1].Station) % 2 == 1;
                    bool safe = dir.HasValue && at.All(s => Vector3.Dot(clk.Spot(s.Station), dir.Value) < -1f);
                    ok &= adjacent && safe; info.Add($"{string.Join("+", at.Select(s => UltimateState.StationNames[s.Station]))} lean {(dir.HasValue ? dir.Value.ToString() : "null")}");
                }
                Check("clocks pairs N = 4: paired hands are adjacent, the paired Leaning Bell leans toward both and both clock spots are on the safe half", ok, string.Join("; ", info));
            }
            yield return ClkStart(8, ClkCue("singles", 8, ClkSingles));
            if (bk == null) yield break;
            ss = clk.Strikes.ToList();
            {
                bool rightAngles = Enumerable.Range(1, 4).All(h => { var at = ss.Where(s => s.Hour == h).ToList(); return at.Count == 2 && ClkMod4(at[0].Station - at[1].Station) % 2 == 1; });
                bool safe = ss.All(s => clk.LeanDirAt(s.Tick) is Vector3 dir && Vector3.Dot(clk.Spot(s.Station), dir) < -1f);
                Check("clocks N = 8: two strikers per hour, every player one hour, hands at right angles (never the same hand); a lean on any hour spares both",
                      ss.Count == 8 && ss.Select(s => s.Owner).Distinct().Count() == 8 && rightAngles && safe);
            }
            foreach (int n in new[] { 5, 6, 8 })
            {
                yield return ClkStart(n, ClkCue("pairs", n, ClkPairs));
                if (bk == null) yield break;
                ss = clk.Strikes.ToList();
                bool ok = ss.Count == n && ss.Select(s => s.Owner).Distinct().Count() == n; var info = new List<string>();
                foreach (var g in ss.GroupBy(s => s.Tick))
                {
                    var hands = g.Select(s => s.Station).Distinct().ToList(); var dir = clk.LeanDirAt(g.Key);
                    bool twoAdjacent = hands.Count == 2 && ClkMod4(hands[0] - hands[1]) % 2 == 1;
                    bool withinHour = g.GroupBy(s => s.Hour).All(h => h.Count() == 1 || ClkMod4(h.First().Station - h.Last().Station) % 2 == 1);
                    bool sides = g.GroupBy(s => s.Station).All(h => h.Count() == 1 ? h.First().Side == 0 : h.Select(s => s.Side).OrderBy(x => x).SequenceEqual(new[] { -1, 1 }));
                    bool safe = dir.HasValue && g.All(s => Vector3.Dot(clk.Hint(s), dir.Value) < -1f);
                    ok &= twoAdjacent && withinHour && sides && safe;
                    info.Add($"{string.Join("+", g.Select(s => UltimateState.StationNames[s.Station] + (s.Side > 0 ? "+" : s.Side < 0 ? "-" : "")))} lean {(dir.HasValue ? dir.Value.ToString() : "null")}");
                }
                Check($"clocks pairs N = {n}: a pair tick holds only two adjacent hands (shared hands side by side, right angles within an hour); the bisector lean spares every striker",
                      ok, string.Join("; ", info));
            }

            // ---------------- 6. the intended route: strat bots at 4.2 m/s, finite stamina, N = 1 / 2 / 4 / 8 ----------------
            foreach (var (n, variant, start) in new[] { (1, "singles", new Vector3(0, 0, 12.2f)), (1, "pairs", new Vector3(0, 0, 2.2f)), (2, "singles", new Vector3(0, 0, 6)), (2, "pairs", new Vector3(-11, 0, 0)),
                                                        (4, "singles", new Vector3(0, 0, 6)), (4, "pairs", new Vector3(0, 0, 12)), (5, "pairs", new Vector3(0, 0, 6)), (6, "pairs", new Vector3(0, 0, 2.2f)),
                                                        (8, "singles", new Vector3(0, 0, 2.2f)), (8, "pairs", new Vector3(0, 0, 6)) })
            {
                yield return ClkStart(n, ClkCue(variant, 11 + n, variant == "pairs" ? ClkPairs : ClkSingles), start);
                if (bk == null) yield break;
                var bots = ClkDrive();
                ss = clk.Strikes.ToList();
                int low = 100; ClkRun(after: () => { foreach (var p in clkParty) low = Mathf.Min(low, Mathf.RoundToInt(p.Stamina)); });
                var bad = ss.Where(s => s.Cracked || s.Struck.Count > 0).Select(s => $"{UltimateState.Roman[s.Hour]} {UltimateState.StationNames[s.Station]}{(s.Cracked ? " cracked" : "")}{(s.Struck.Count > 0 ? " struck " + s.Struck.Count : "")}").ToList();
                Check($"route N = {n} {variant}: strat bots at 4.2 m/s (finite stamina) strike the boss every hour and never strike each other",
                      ss.All(s => s.Resolved) && bad.Count == 0 && ClkAllHits() == 0 && clkParty.All(p => !p.Down),
                      bad.Count > 0 ? string.Join(", ", bad) + " · " + bots[0].DebugLine : $"min stamina {low}");
            }

            // the pairing rule with shared hands: a Leaning Bell (Ultimate tuning, 3.5 s) on the first pair tick, leaning by LeanDirAt
            foreach (int n in new[] { 5, 6, 8 })
            {
                yield return ClkStart(n, ClkCue("pairs", 30 + n, ClkPairs));
                if (bk == null) yield break;
                var bots = ClkDrive();
                ss = clk.Strikes.ToList();
                int pairTick = ss[0].Tick; var dir = clk.LeanDirAt(pairTick); bool leaned = false;
                ClkRun(after: () =>
                {
                    if (d.World.Tick == pairTick - UltShapes.LeanTicks && dir.HasValue)
                        leaned = bk.UltMove(bk.UltLean, BellkeeperEncounter.LeanTuning(dir.Value));
                });
                UltStepAliveUntil(() => !bk.UltLean.Running, 300);
                var bad = ss.Where(s => s.Cracked || s.Struck.Count > 0).Select(s => $"{UltimateState.Roman[s.Hour]} {UltimateState.StationNames[s.Station]}{(s.Side != 0 ? (s.Side > 0 ? "+" : "-") : "")}{(s.Cracked ? " cracked" : "")}{(s.Struck.Count > 0 ? " struck" : "")}").ToList();
                int leanHits = bkHits.Count(h => h.Source == UltShapes.LeanSource);
                Check($"pairing N = {n} pairs: a Leaning Bell on the pair tick leans along the bisector; all four strikers and everyone else are spared, every hour strikes the boss",
                      leaned && leanHits == 0 && bad.Count == 0 && ClkAllHits() == 0 && clkParty.All(p => !p.Down),
                      $"lean {(dir.HasValue ? dir.Value.ToString() : "null")} launched {leaned}, lean hits {leanHits}" + (bad.Count > 0 ? " · " + string.Join(", ", bad) : "") + " · " + bots[0].DebugLine);
            }

            // ---------------- 7. still players are threatened (N = 4: two strat bots, a still melee hugging the boss, a still ranged at 10 m) ----------------
            yield return ClkStart(4, ClkCue("singles", 21, ClkSingles));
            if (bk == null) yield break;
            {
                var melee = clkParty[2]; var ranged = clkParty[3];
                ss = clk.Strikes.ToList();
                var ms = ss.First(s => s.Owner == melee);
                // the melee hugs the boss (1.95 m) on its own station's side: its cone points away from him
                BkPlace(melee, Vector3.zero + UltimateState.StationDir(ms.Station) * 1.95f); BkPlace(ranged, new Vector3(10, 0, 0));
                for (int i = 0; i < 2; i++) { var b = UltimateParty.Drive(clkParty[i], i, 4); b.Speed = 4.2f; b.Attacks = false; }
                ClkRun();
                var rs = ss.First(s => s.Owner == ranged);
                Check("still ranged (10 m): its own hour cracks (the cone reaches 6 m)", rs.Cracked);
                Check("still melee hugging the boss on its station's side: its own hour cracks", ms.Cracked);
                Note($"still players: the strat bots kept them out of their cones (clock hits on the still melee {ClkHits(melee)}, ranged {ClkHits(ranged)}); bot hours {string.Join(", ", ss.Where(s => s.Owner == clkParty[0] || s.Owner == clkParty[1]).Select(s => s.Cracked ? "cracked" : "struck"))}");
            }

            // ---------------- 8. boss death cancels everything ----------------
            yield return ClkStart(2, ClkCue("singles", 3, ClkSingles));
            if (bk == null) yield break;
            {
                ss = clk.Strikes.ToList();
                UltStepAliveUntil(() => ss[0].Cone != null, 600); UltStepAlive(10);
                int cracked = bk.Ult.CrackedCount, hits = bkHits.Count; bool live = d.World.Hazards.Count > 0;
                bk.DebugDefeat();
                BkStep(1500);
                Check("clocks: boss death cancels the cones and the numerals; no hour strikes or cracks afterwards",
                      live && d.World.Hazards.Count == 0 && !clk.Running && clk.Strikes.Count == 0 && bk.Ult.GlyphText(d.Player, d.World.Tick) == null
                      && bk.Ult.CrackedCount == cracked && bkHits.Count == hits && !clk.LeanDirAt(ss[1].Tick).HasValue);
            }

            // ---------------- 9. the plan: rows, pairing, phase-start practice ----------------
            yield return UltStart();
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = false;
            clk = bk.Mechanic<PendulumClocks>();
            bk.BeginCombat(); UltStepAlive(2);
            {
                int t0 = UltTick0();
                UltStepAlive(t0 + Sim.Ticks(18.5f) + 2 - d.World.Tick);
                ss = clk.Strikes.ToList();
                var dir = bk.UltLean.Tuning?.Dir;
                Check("plan P1: clocks singles launched at 3 s strike at 12, 17, 22, 27 s",
                      UltLaunchTick("ult_pendulum_clocks", t0) - t0 == Sim.Ticks(3) && ss.Select(s => s.Tick - t0).SequenceEqual(new[] { 12f, 17f, 22f, 27f }.Select(Sim.Ticks)),
                      string.Join(", ", ss.Select(s => (s.Tick - t0) / 60f)));
                Check("plan P1: the Leaning Bell resolving with hour III (22 s) leans toward III's station", bk.UltLean.Running && dir.HasValue && ClkNear(dir.Value, UltimateState.StationDir(ss[2].Station)),
                      dir.HasValue ? dir.Value.ToString() : "no dir");
            }
            // the full P1 loop with everything live: a strat bot keeps every hour
            foreach (int n in new[] { 1, 4 })
            {
                yield return UltStart();
                if (bk == null) yield break;
                BellkeeperEncounter.PushTimersOn = false;
                clk = bk.Mechanic<PendulumClocks>();
                clkParty.Clear(); clkParty.Add(d.Player);
                for (int i = 1; i < n; i++) clkParty.Add(BkAdd(Sim.Dir(90f * i + 20) * 6f));
                var bots = ClkDrive();
                bk.BeginCombat(); UltStepAlive(2);
                int t0 = UltTick0();
                UltStepAliveUntil(() => clk.Casts > 0 && !clk.Running && d.World.Tick > t0 + Sim.Ticks(27), Sim.Ticks(30));
                ss = clk.Strikes.ToList();
                Check($"plan P1 N = {n}: with the Leaning Bells, Toll Tide, Heart Tolls and Reflection live, strat bots at 4.2 m/s keep every hour",
                      ss.Count == 4 && ss.All(s => s.Resolved && !s.Cracked && s.Struck.Count == 0) && ClkAllHits() == 0,
                      string.Join(", ", ss.Select(s => $"{UltimateState.Roman[s.Hour]}{(s.Cracked ? " cracked" : "")}{(s.Struck.Count > 0 ? " struck" : "")}")) + " · " + bots[0].DebugLine);
            }
            foreach (var (seg, launch, strikes) in new[] { ("P2", 6f, new[] { 12f, 14f, 18.5f, 20.5f }), ("P3", 12f, new[] { 18f, 21f, 24f, 27f }), ("P4", 0f, new[] { 7.75f, 9.75f, 10.75f, 12.75f }), ("P5", 10f, new[] { 17.2f, 19.2f, 27f, 29f }) })
            {
                yield return UltStart(null, seg);
                if (bk == null) yield break;
                BellkeeperEncounter.PushTimersOn = false;
                clk = bk.Mechanic<PendulumClocks>();
                bk.BeginCombat(); UltStepAlive(2);
                int t0 = UltTick0();
                UltStepAliveUntil(() => clk.Casts > 0, Sim.Ticks(14));
                ss = clk.Strikes.ToList();
                Check($"practice from {seg}: clocks launch at {launch} s, solo hours at {string.Join(", ", strikes)} s",
                      UltLaunchTick("ult_pendulum_clocks", t0) - t0 == Sim.Ticks(launch) && ss.Select(s => s.Tick - t0).SequenceEqual(strikes.Select(Sim.Ticks)),
                      $"launch {(UltLaunchTick("ult_pendulum_clocks", t0) - t0) / 60f}, hours {string.Join(", ", ss.Select(s => (s.Tick - t0) / 60f))}");
            }
            BellkeeperEncounter.PushTimersOn = true;

            // practice hook: -lastbellAbility ult_pendulum_clocks loops the default cue
            yield return UltStart(UltimateMechanics.PendulumClocks);
            if (bk == null) yield break;
            clk = bk.Mechanic<PendulumClocks>();
            bk.BeginCombat(); UltStepAlive(2);
            Check("practice hook -lastbellAbility ult_pendulum_clocks: singles, hours 6/11/16/21 s after launch", bk.PracticeAbility == clk && clk.Casts == 1 && clk.Cue.Variant == "singles"
                  && clk.Strikes.Select(s => s.Tick - clk.LastCastTick).SequenceEqual(ClkSingles.Select(Sim.Ticks)));

            // evidence clip: four strat bots in practice
            yield return UltStart(UltimateMechanics.PendulumClocks);
            if (bk == null) yield break;
            clk = bk.Mechanic<PendulumClocks>();
            clkParty.Clear(); clkParty.Add(d.Player);
            for (int i = 1; i < 4; i++) clkParty.Add(BkAdd(Sim.Dir(90f * i + 20) * 6f, RunDirector.Classes[i % RunDirector.Classes.Length]));
            ClkDrive(PlayerController.WalkSpeed, true);
            yield return BkClip("ult_pendulum_clocks_n4", 23f);
            d.Player.Input = bot;
        }
    }
}
