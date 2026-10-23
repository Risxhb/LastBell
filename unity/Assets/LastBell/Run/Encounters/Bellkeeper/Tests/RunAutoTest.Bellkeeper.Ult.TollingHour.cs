using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: Tolling Hour, The Thirteenth Hour and the T2 area quotes checks, owned by their mechanic session.
    // Run alone: -lastbellBellkeeperUltimate -lastbellTestAbility ult_tolling_hour (or ult_thirteenth_hour / ult_area_quotes).
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs. Everything runs through the real plan from a practice phase start (T2,
    // P1, P3, P5), so each launch happens inside the boss's script step exactly as in an attempt.
    public sealed partial class RunAutoTest
    {
        TollingHour uTh; ThirteenthHour uXiii; AreaQuotes uQ;
        static readonly string[] UqSources = { AreaQuotes.SpireSource, AreaQuotes.CoreSource, AreaQuotes.BellowsSource, AreaQuotes.TideSource, AreaQuotes.BloomSource };
        // the quotes' damaging hits (the Bellows itself is the push: a 0-damage shove, the intended route)
        static readonly string[] UqDamaging = { AreaQuotes.SpireSource, AreaQuotes.CoreSource, AreaQuotes.TideSource, AreaQuotes.BloomSource };

        // practice from a phase, the plan running, one tick stepped (the segment has begun)
        IEnumerator UqStart(string segment, int extras = 0)
        {
            yield return UltStart(null, segment);
            if (bk == null) yield break;
            uTh = bk.Mechanic<TollingHour>(); uXiii = bk.Mechanic<ThirteenthHour>(); uQ = bk.Mechanic<AreaQuotes>();
            for (int i = 0; i < extras; i++) BkAdd(Sim.Dir(360f * (i + 1) / (extras + 1)) * 6f);
            bk.BeginCombat(); BkStep(1);
        }
        void UqTo(int tick, bool heal) { while (d.World.Tick < tick) { if (heal) UltStepAlive(1); else BkStep(1); } }
        List<BkHit> UqHits(PlayerController p, params string[] src) =>
            bkHits.Where(h => (p == null || h.Player == p) && (src.Length == 0 || src.Contains(h.Source))).GroupBy(h => (h.Tick, h.Player, h.HazardId)).Select(g => g.First()).ToList();
        static float UqR(PlayerController p, Vector3 from) => Sim.Flat(p.transform.position - from).magnitude;
        Vector3 UqHug(Vector3 dir) => dir.normalized * (bk.BodyRadius + PlayerController.Radius + 0.1f);
        // ticks are stepped synchronously, so let a fresh telegraph fade in (a few rendered frames) before the screenshot
        IEnumerator UqShot(string name) { for (int i = 0; i < 12; i++) yield return null; yield return Shot(name); }

        IEnumerator UltTollingHourChecks()
        {
            Note("---- Ultimate: Tolling Hour · The Thirteenth Hour · T2 area quotes ----");
            // the harness calls this for any of the three keys (or the whole section): run all three groups
            yield return UqTolling(); yield return UqTollLethality(); yield return UqTollPotionBot();
            yield return UqThirteenth(); yield return UqThirteenthShort(); yield return UqThirteenthDeath();
            UqGeometry();
            yield return UqQuotesStill();
            yield return UqVerdantChain();
            foreach (int n in new[] { 1, 2, 4, 8 }) yield return UqRoute(n);
            yield return UqQuotesDeath();
            yield return UqPracticeHooks();
        }

        // ---------------- Tolling Hour ----------------
        IEnumerator UqTolling()
        {
            // T2: six tolls at 3..8 s, 12 each, every player (N = 4), exactly 1.0 s apart
            yield return UqStart("T2", 3);
            if (bk == null) yield break;
            int t0 = UltTick0();
            UqTo(t0 + Sim.Ticks(9), true);
            var ps = d.World.Players.ToList();
            bool each = ps.All(p => UqHits(p, TollingHour.Source).Count == 6 && UqHits(p, TollingHour.Source).All(h => h.Damage == 12));
            var mine = UqHits(d.Player, TollingHour.Source).Select(h => h.Tick - t0).ToList();
            bool ticks = mine.SequenceEqual(Enumerable.Range(0, 6).Select(k => Sim.Ticks(3 + k)));
            Check("Tolling Hour T2: six unavoidable 12-damage raidwides on every player (N = 4)", each, string.Join(" ", ps.Select(p => UqHits(p, TollingHour.Source).Count)));
            Check("Tolling Hour T2: the tolls land on 3.0, 4.0 .. 8.0 s exactly (launch at 3 + At)", ticks && UltLaunchTick("ult_tolling_hour", t0) - t0 == Sim.Ticks(3), string.Join(",", mine.Select(t => (t / 60f).ToString("0.00"))));
            Check("Tolling Hour: the great bell is behind the north rim (the boss turned north to call it)", Mathf.Abs(Mathf.DeltaAngle(bk.transform.eulerAngles.y, 180)) < 5, $"yaw {bk.transform.eulerAngles.y:0}");

            // P1: two tolls at loop start (24); P3: three (36)
            foreach (var (seg, n) in new[] { ("1", 2), ("P3", 3) })
            {
                yield return UqStart(seg);
                if (bk == null) yield break;
                t0 = UltTick0();
                UqTo(t0 + Sim.Ticks(n + 0.5f), true);
                var at = UqHits(d.Player, TollingHour.Source).Select(h => h.Tick - t0).ToList();
                Check($"Tolling Hour {seg}: {n} tolls at the loop start, 1.0 s apart ({12 * n})", at.SequenceEqual(Enumerable.Range(0, n).Select(k => Sim.Ticks(k))), string.Join(",", at));
            }
        }

        // T2 kills a player below 73 HP and not one at 73+ (no potions drunk)
        IEnumerator UqTollLethality()
        {
            yield return UqStart("T2", 1);
            if (bk == null) yield break;
            int t0 = UltTick0();
            var low = d.Player; var ok = d.World.Players[1];
            UqTo(t0 + Sim.Ticks(3) - 1, true);
            low.DebugSetHp(72); ok.DebugSetHp(73);
            UqTo(t0 + Sim.Ticks(7), false);
            bool aliveAfterFive = !low.Down && low.Hp == 12;
            UqTo(t0 + Sim.Ticks(8), false);
            Check("Tolling Hour T2: a player at 72 HP survives five tolls and dies on the sixth", aliveAfterFive && low.Down, $"hp after 5: {(aliveAfterFive ? 12 : -1)}, down {low.Down}");
            Check("Tolling Hour T2: a player at 73 HP survives the chain with 1 HP", !ok.Down && ok.Hp == 1, $"hp {ok.Hp}");
        }

        // the strat bot reads "potion to 73+" from the T2 start and enters the chain topped up
        IEnumerator UqTollPotionBot()
        {
            yield return UqStart("T2");
            if (bk == null) yield break;
            int t0 = UltTick0();
            var sb = UltimateParty.Drive(d.Player, 0, 1); sb.Speed = 4.2f; sb.Attacks = false;
            d.Player.DebugSetHp(40);
            UqTo(t0 + Sim.Ticks(3) - 1, false);
            int before = d.Player.Hp;
            UqTo(t0 + Sim.Ticks(8) + 1, false);
            Check("Tolling Hour T2: the strat bot drinks to 73+ before the first toll and survives the six", before >= 73 && !d.Player.Down, $"hp {before} at the first toll, {d.Player.Hp} after, potions left {d.Player.Potions}");
            d.Player.Input = bot;
        }

        // ---------------- The Thirteenth Hour ----------------
        IEnumerator UqThirteenth()
        {
            yield return UqStart("P5", 3);
            if (bk == null) yield break;
            int t0 = UltTick0(), enrage = t0 + Sim.Ticks(78);
            // the tolls are raidwides that resolve on their spawn tick: record the spawns (robust to other P5 mechanics
            // downing a still test player once they are merged)
            var tolls = new List<int>();
            System.Action<BellkeeperAbility, Hazard> rec = (a, h) => { if (a == uXiii && h.Source == ThirteenthHour.Source) tolls.Add(d.World.Tick - t0); };
            bk.HazardSpawned += rec;
            Check("Thirteenth Hour: starts with P5", uXiii.Running && uXiii.Built && uXiii.LastCastTick == t0, $"launch {uXiii.LastCastTick - t0}");
            UqTo(t0 + Sim.Ticks(36) + 1, true);
            yield return UqShot("ult_thirteenth_count_VI");
            UqTo(enrage - 1, true);
            bool noneYet = !bk.PlanLog.Any(e => e.what == "wipe " + ThirteenthHour.Source);
            Check("Thirteenth Hour: I..XII toll every 6 s, 6 each, and nothing wipes before XIII", noneYet && uXiii.Count == 12 && uXiii.Numeral == "XII"
                  && tolls.SequenceEqual(Enumerable.Range(1, 12).Select(k => Sim.Ticks(6 * k))) && UqHits(null, ThirteenthHour.Source).All(h => h.Damage == 6),
                  $"count {uXiii.Count}, tolls at {string.Join(",", tolls.Select(t => t / 60f))}");
            BkStep(1);
            bk.HazardSpawned -= rec;
            int wipeTick = bk.PlanLog.FirstOrDefault(e => e.what == "wipe " + ThirteenthHour.Source).tick;
            Check("Thirteenth Hour: XIII wipes the whole party (N = 4) exactly at 78 s, before the plan's 78.5 s fallback",
                  wipeTick == enrage && uXiii.Count == 13 && d.World.Players.All(p => p.Down) && !bk.PlanLog.Any(e => e.what.StartsWith("wipe") && e.tick < enrage),
                  $"wipe at {(wipeTick - t0) / 60f:0.000}s, count {uXiii.Count}");
        }

        // a shortened enrage (tests and the plan check): the count keeps 13 steps and XIII lands on At[0]
        IEnumerator UqThirteenthShort()
        {
            BellkeeperEncounter.PushSeconds = new float[] { 130, 0, 150, 130, 0, 130, 2.6f };
            yield return UqStart("P5");
            if (bk != null)
            {
                int t0 = UltTick0();
                UqTo(t0 + Sim.Ticks(2.6f) - 1, true);
                bool none = bk.Ult.Wipes == 0 && uXiii.Count == 12;
                BkStep(1);
                Check("Thirteenth Hour: a shortened enrage (2.6 s) still counts XII then wipes on XIII exactly", none && bk.Ult.Wipes >= 1 && d.Player.Down && bk.PlanLog.First(e => e.what.StartsWith("wipe")).tick == t0 + Sim.Ticks(2.6f), $"count {uXiii.Count}");
            }
            BellkeeperEncounter.PushSeconds = new float[] { 130, 0, 150, 130, 0, 130, 78 };
        }

        // boss death stops the count: no more tolls, no XIII
        IEnumerator UqThirteenthDeath()
        {
            yield return UqStart("P5");
            if (bk == null) yield break;
            int t0 = UltTick0();
            UqTo(t0 + Sim.Ticks(20), true);
            int count = uXiii.Count, dead = d.World.Tick;
            bk.DebugDefeat();
            BkStep(Sim.Ticks(62));
            Check("Thirteenth Hour: boss death stops the count (no toll, no XIII after it)", count == 3 && uXiii.Count == 3 && !uXiii.Running && UqHits(null, ThirteenthHour.Source).All(h => h.Tick <= dead)
                  && !bk.PlanLog.Any(e => e.what.StartsWith("wipe") && e.tick > dead), $"count {count} -> {uXiii.Count}");
        }

        // ---------------- T2 area quotes ----------------
        // Verdant geometry (pure): the three circles cover the whole floor; the seam step is short enough at 4.2 m/s
        void UqGeometry()
        {
            const float B = 13f;   // the Throne's player bound (RunMaps "shattered-hour")
            float R = AreaQuotes.BloomRadius + Hazard.Margin;
            var cs = new[] { 1, 5, 9 }.Select(AreaQuotes.HourCentre).ToArray();
            int uncovered = 0; var outA = new List<Vector3>(); var aOnly = new List<Vector3>();
            for (float x = -B; x <= B; x += 0.2f)
                for (float z = -B; z <= B; z += 0.2f)
                {
                    var q = new Vector3(x, 0, z); if (q.magnitude > B) continue;
                    bool a = (q - cs[0]).magnitude <= R, b = (q - cs[1]).magnitude <= R, c = (q - cs[2]).magnitude <= R;
                    if (!a && !b && !c) uncovered++;
                    if (q.magnitude < 11.5f) continue;
                    if (!a) outA.Add(q); else if (!b && !c) aOnly.Add(q);
                }
            float gap = float.MaxValue;
            foreach (var p in outA) foreach (var q in aOnly) gap = Mathf.Min(gap, (p - q).magnitude);
            Check("Root hours: the three circles (r 11.4 on hour marks 7 m out) leave no safe spot on the floor", uncovered == 0, $"{uncovered} uncovered samples");
            Check("Root hours: the seam step (outside the first circle -> inside it, outside the other two) fits 1.0 s at 4.2 m/s", gap < 4.2f * 0.95f && aOnly.Count > 50,
                  $"step {gap:0.00} m, inner region {aOnly.Count} samples");
        }

        // still melee and still ranged players are hit by each quote; ticks; the bellows push is 3 m; roots
        IEnumerator UqQuotesStill()
        {
            yield return UqStart("T2", 2);
            if (bk == null) yield break;
            int t0 = UltTick0();
            var me = d.Player; var mel = d.World.Players[1]; var off = d.World.Players[2];
            UqTo(t0 + Sim.Ticks(10) - 2, true);
            // Spire: the sun (farthest) at 11 m north-south line, a melee between, one off the line
            BkPlace(me, new Vector3(0, 0, 11)); BkPlace(mel, UqHug(Vector3.forward)); BkPlace(off, new Vector3(-8, 0, -3));
            UqTo(t0 + Sim.Ticks(10), true);
            int qt = UltLaunchTick("ult_area_quotes", t0);
            Check("Quotes: launched at T2 + 10 s; the sun is the farthest player", qt - t0 == Sim.Ticks(10) && uQ.Sun == me && uQ.Built, $"launch {(qt - t0) / 60f:0.00}s, sun {(uQ.Sun ? uQ.Sun.name : "-")}");
            UqTo(qt + Sim.Ticks(2.5f), true);
            yield return UqShot("ult_quotes_spire_sundial");
            UqTo(qt + Sim.Ticks(4) + 1, true);
            var sp = UqHits(null, AreaQuotes.SpireSource);
            Check("Spire (Sundial): resolves at quote + 4.0 s; still sun (ranged) and still melee on the line are hit (60), the off-line player isn't",
                  sp.Count == 2 && sp.All(h => h.Tick == qt + Sim.Ticks(4) && h.Damage == 60) && sp.Any(h => h.Player == me) && sp.Any(h => h.Player == mel), string.Join(", ", sp.Select(h => h.Player.name + "@" + (h.Tick - qt))));
            Check("Spire (Sundial): the shadow locked 1.0 s before it fell", uQ.Shadow != null && uQ.Shadow.LockAt == uQ.Shadow.ResolveAt - 60);
            // Foundry: band, hugging melee, ranged (placed after the Sundial's 1.5 m knockback has finished sliding)
            UqTo(qt + Sim.Ticks(4) + 20, true);
            BkPlace(me, new Vector3(3.3f, 0, 0)); BkPlace(mel, UqHug(Vector3.back)); BkPlace(off, new Vector3(-10, 0, 0));
            UqTo(qt + Sim.Ticks(6), true);
            yield return UqShot("ult_quotes_foundry_bellows");
            UqTo(qt + Sim.Ticks(9) + 1, true);
            var core = UqHits(null, AreaQuotes.CoreSource); var tide = UqHits(null, AreaQuotes.TideSource); var blow = UqHits(me, AreaQuotes.BellowsSource);
            Check("Foundry (Bellows): core and push 0.2 s before quote + 9.0 s, the tide line on it", core.All(h => h.Tick == qt + Sim.Ticks(9) - 12) && tide.All(h => h.Tick == qt + Sim.Ticks(9)) && blow.All(h => h.Tick == qt + Sim.Ticks(9) - 12),
                  $"core {string.Join(",", core.Select(h => h.Tick - qt))} tide {string.Join(",", tide.Select(h => h.Tick - qt))}");
            Check("Foundry (Bellows): a still hugging melee is burned by the core (60), a still ranged player by the tide line (60)",
                  core.Count(h => h.Player == mel) == 1 && core.First(h => h.Player == mel).Damage == 60 && tide.Count(h => h.Player == off) == 1 && tide.First(h => h.Player == off).Damage == 60);
            Check("Foundry (Bellows): from the band (3.3 m) the player is blown exactly 3 m out (0 damage: a pure shove) and lands inside the tide line",
                  blow.Count == 1 && blow[0].Damage == 0 && Mathf.Abs(UqR(me, uQ.ForgeOrigin) - 6.3f) < 0.12f && !core.Any(h => h.Player == me) && !tide.Any(h => h.Player == me), $"r {UqR(me, uQ.ForgeOrigin):0.00} m, push damage {(blow.Count > 0 ? blow[0].Damage : -1)}");
            // Verdant: hugging melee (in all three), ranged near the wall, a player at 9 m
            BkPlace(me, new Vector3(0, 0, 12.5f)); BkPlace(mel, UqHug(Vector3.left)); BkPlace(off, new Vector3(9, 0, 0));
            UqTo(qt + Sim.Ticks(12) - 30, true);
            yield return UqShot("ult_quotes_verdant_roots");
            UqTo(qt + Sim.Ticks(12) + 1, true);
            bool rooted = d.World.Players.Where(p => UqHits(p, AreaQuotes.BloomSource).Count > 0).All(p => bk.Ult.IsStilled(p));
            UqTo(qt + Sim.Ticks(14) + 1, true);
            var bl = UqHits(null, AreaQuotes.BloomSource);
            Check("Verdant (Root hours): three blooms at quote + 12, 13, 14 s, four hours apart", uQ.BloomTicks.SequenceEqual(new[] { 12, 13, 14 }.Select(s => qt + Sim.Ticks(s)))
                  && (uQ.BloomHours[1] - uQ.BloomHours[0] + 12) % 4 == 0 && uQ.BloomHours.Distinct().Count() == 3, $"hours {string.Join(",", uQ.BloomHours)}");
            Check("Verdant (Root hours): every still player is hit (40) and rooted; the hugging melee by all three",
                  d.World.Players.All(p => bl.Any(h => h.Player == p)) && bl.Count(h => h.Player == mel) == 3 && bl.All(h => h.Damage == 40) && rooted && uQ.Rooted >= 3,
                  string.Join(" ", d.World.Players.Select(p => bl.Count(h => h.Player == p))));
        }

        // the chain is the one-shot: a still player at full HP in all three circles survives two blooms and dies on the third
        IEnumerator UqVerdantChain()
        {
            yield return UqStart("T2");
            if (bk == null) yield break;
            int t0 = UltTick0();
            UqTo(t0 + Sim.Ticks(22) - 2, true);
            BkPlace(d.Player, UqHug(Vector3.right)); d.Player.DebugSetHp(PlayerController.MaxHp);
            UqTo(t0 + Sim.Ticks(23) + 1, false);
            bool two = !d.Player.Down && d.Player.Hp == 20;
            UqTo(t0 + Sim.Ticks(24) + 1, false);
            Check("Verdant (Root hours): rooted in the blooms' path, a full-HP player survives two and dies on the third", two && d.Player.Down, $"hp after two {(two ? 20 : d.Player.Hp)}");
        }

        // the guide's route at 4.2 m/s with finite stamina: perfect strat bots clear all of T2 untouched by the quotes
        IEnumerator UqRoute(int n)
        {
            yield return UltStart(null, "T2");
            if (bk == null) yield break;
            uQ = bk.Mechanic<AreaQuotes>();
            var bots = UltimateParty.Fill(d.World, n);
            foreach (var b in bots) { b.Speed = 4.2f; b.Attacks = false; }
            bk.BeginCombat(); BkStep(1);
            int t0 = UltTick0();
            float minSt = 999; int potions = 0;
            while (d.World.Tick < t0 + Sim.Ticks(25.4f)) { BkStep(1); foreach (var p in d.World.Players) minSt = Mathf.Min(minSt, p.Stamina); }
            foreach (var p in d.World.Players) potions += PlayerController.MaxPotions - p.Potions;
            var hits = UqHits(null, UqDamaging); int blown = UqHits(null, AreaQuotes.BellowsSource).Count;
            Check($"Quotes route N = {n}: perfect bots at 4.2 m/s (finite stamina) take no damaging quote hit, all ride the bellows from the band, nobody falls in T2",
                  hits.Count == 0 && blown == n && d.World.Players.All(p => !p.Down), $"blown {blown}, hits [{string.Join(", ", hits.Select(h => h.Player.name + ":" + h.Source + "@" + (h.Tick - t0) / 60f))}], potions {potions}, min stamina {minSt:0}, hours {string.Join(",", uQ.BloomHours)}");
            foreach (var p in d.World.Players) p.Input = null;
            d.Player.Input = bot;
        }

        // boss death mid-quote cancels everything in that tick
        IEnumerator UqQuotesDeath()
        {
            yield return UqStart("T2");
            if (bk == null) yield break;
            int t0 = UltTick0();
            UqTo(t0 + Sim.Ticks(16), true);
            int pending = d.World.Hazards.Live.Count(h => uQ.Spawned.Contains(h)), dead = d.World.Tick;
            bk.DebugDefeat();
            BkStep(Sim.Ticks(10));
            Check("Quotes: boss death during the bellows' warning cancels the quote (no hit, no root, no SUN glyph)",
                  pending == 3 && d.World.Hazards.Count == 0 && !uQ.Running && UqHits(null, UqSources).All(h => h.Tick <= dead) && !bk.Ult.All.Any(), $"{pending} pending");
        }

        // -lastbellAbility hooks
        IEnumerator UqPracticeHooks()
        {
            yield return UltStart("ult_tolling_hour");
            if (bk == null) yield break;
            var th = bk.Mechanic<TollingHour>();
            bk.BeginCombat(); UltStepAlive(Sim.Ticks(6));
            var at = UqHits(d.Player, TollingHour.Source).Select(h => h.Tick - th.LastCastTick).ToList();
            Check("practice -lastbellAbility ult_tolling_hour: the six-toll chain, 1.0 s apart", at.SequenceEqual(Enumerable.Range(0, 6).Select(k => Sim.Ticks(k))), string.Join(",", at));
            yield return UltStart("ult_thirteenth_hour");
            if (bk == null) yield break;
            var xiii = bk.Mechanic<ThirteenthHour>();
            bk.BeginCombat(); UltStepAlive(Sim.Ticks(6) + 2);
            Check("practice -lastbellAbility ult_thirteenth_hour: the count starts (I at 6 s)", xiii.Running && xiii.Count == 1 && UqHits(d.Player, ThirteenthHour.Source).Count == 1, $"count {xiii.Count}");
            yield return UltStart("ult_area_quotes");
            if (bk == null) yield break;
            var q = bk.Mechanic<AreaQuotes>();
            bk.BeginCombat(); UltStepAlive(3);
            Check("practice -lastbellAbility ult_area_quotes: the Sundial starts on you (solo sun)", q.Running && q.Shadow != null && q.Sun == d.Player);
        }
    }
}
