using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Last Toll Tide (Bellkeeper ability set v2) acceptance checks, owned by the Last Toll Tide ability session.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/04-toll-tide.md ("Autotest" under Build notes). Harness: BkStart,
    // BkAdd, BkStep... in RunAutoTest.BellkeeperV2.cs. Run alone with -lastbellBellkeeperV2 -lastbellTestAbility toll_tide.
    // "Tick k" below is the ability's tick (0 = the tide's first warning tick).
    public sealed partial class RunAutoTest
    {
        TollTideAbility tt;
        int TtAt => d.World.Tick - tt.LastCastTick;
        void TtStepTo(int abilityTick) { BkStep(Mathf.Max(0, abilityTick - TtAt)); }
        // restarts the boss and steps through the Toll Tide's tick 0 (practice loop: it launches on the first script tick)
        bool TtLaunch()
        {
            tt = bk.Ability<TollTideAbility>();
            bk.BeginCombat();
            return BkStepUntil(() => tt.Spawned.Count > 0, 300) >= 0;
        }
        // hits since the last BkStart, one entry per (tick, player, hazard) even if a handler was attached twice
        List<BkHit> TtHits(PlayerController p = null, string src = null) =>
            bkHits.Where(h => (p == null || h.Player == p) && (src == null || h.Source == src))
                  .GroupBy(h => (h.Tick, h.Player, h.HazardId)).Select(g => g.First()).ToList();
        bool TtOnePerInstance() => TtHits().GroupBy(h => (h.Player, h.HazardId)).All(g => g.Count() == 1);
        static float TtR(PlayerController p, Vector3 from) => Sim.Flat(p.transform.position - from).magnitude;
        Vector3 TtHug(float yaw) => Sim.Dir(yaw) * (bk.BodyRadius + 0.3f);
        static Vector3 TtBand(float yaw, float r = 5.5f) => Sim.Dir(yaw) * r;
        const int TtReact = 36;   // 0.6 s reaction margin before an escaping player moves

        IEnumerator TollTideChecks()
        {
            Note("---- The Last Toll Tide (bellkeeper.toll_tide) ----");
            yield return TtCore(Difficulty.Medium);
            yield return TtCore(Difficulty.Easy);
            yield return TtCore(Difficulty.Hard);
            yield return TtEscapeThrone();
            yield return TtEscapeTestMap();
            foreach (int n in new[] { 2, 4, 8 }) yield return TtParty(n);
            yield return TtDeath(90, "during the tide's warning");
            yield return TtDeath(240, "during the Heart Toll's warning");
            yield return TtPracticeLoop();
            yield return TtPunishInPlan();
            yield return TtEvidence();
        }

        // Shapes, ticks, damage and knockback with still players: at the wall, at 9 m, hugging the boss, and in the band.
        IEnumerator TtCore(Difficulty level)
        {
            yield return BkStart("toll_tide", level);
            if (bk == null) yield break;
            string L = "Toll Tide [" + level + "]: ";
            var wall = d.Player; BkPlace(wall, BkEdge(Vector3.right));
            var far = BkAdd(new Vector3(0, 0, 9f));
            var hug = BkAdd(TtHug(225));
            var band = BkAdd(TtBand(135));
            float wallR0 = TtR(wall, Vector3.zero), farR0 = TtR(far, Vector3.zero);
            if (!TtLaunch()) { Check(L + "launches", false, "no hazard within 300 ticks"); ScriptedParty.RemoveExtras(d.World, d.Player); yield break; }

            int tideT = tt.TideTicks, tollT = tt.TollTicks, tideHit = tideT - 1, tollHit = tideT - 1 + tollT;
            var tide = tt.Tide;
            Check(L + "tide warns at tick 0: Ground ring on the centre, 7.0 m to the wall + 2 m",
                  tide != null && tide.Source == TollTideAbility.TideSource && tide.Shape == HazardShape.Ring && tide.Kind == HazardKind.Ground
                  && Sim.Flat(tide.Origin).magnitude < 0.01f && tide.Inner == 7f && Mathf.Abs(tide.Radius - (CombatWorld.PlayerBound + 2f)) < 0.01f
                  && tide.Knockback == -1.5f && tide.Damage == 35 && TtAt == 0,
                  tide == null ? "none" : $"inner {tide.Inner}, outer {tide.Radius:0.00} (bound {CombatWorld.PlayerBound}), knock {tide.Knockback}, tick {TtAt}");
            int maxLive = 0;
            while (TtAt < tideHit - 1) { BkStep(1); maxLive = Mathf.Max(maxLive, BkPatternsLive()); }
            bool quietBefore = TtHits().Count == 0 && tt.Toll == null;
            BkStep(1);   // tick tideHit
            var wallTide = TtHits(wall, TollTideAbility.TideSource); var toll = tt.Toll;
            Check(L + $"tide resolves at tick {tideHit} ({tideT / 60f:0.00} s), nothing earlier",
                  quietBefore && wallTide.Count == 1 && wallTide[0].Tick - tt.LastCastTick == tideHit, wallTide.Count > 0 ? $"hit at tick {wallTide[0].Tick - tt.LastCastTick}" : "no hit");
            Check(L + "Heart Toll warns on the tick the tide breaks: Ground circle r 4.0 on the boss",
                  toll != null && toll.Shape == HazardShape.Circle && toll.Kind == HazardKind.Ground && toll.Radius == 4f && toll.Damage == 35
                  && Sim.Flat(toll.Origin - bk.transform.position).magnitude < 0.01f && tt.Spawned.Count == 2,
                  toll == null ? "none" : $"r {toll.Radius}, at {toll.Origin}, {tt.Spawned.Count} spawned");
            Check(L + "a still player at the wall is hit by the tide", wallTide.Count == 1 && wallTide[0].Damage == Mathf.RoundToInt(35 * d.World.DamageTakenScale),
                  wallTide.Count > 0 ? $"{wallTide[0].Damage} dmg (scale {d.World.DamageTakenScale})" : "missed");
            Check(L + "a still player at 9 m is hit by the tide", TtHits(far, TollTideAbility.TideSource).Count == 1);
            Check(L + "the hugging and the band players are not hit by the tide",
                  TtHits(hug, TollTideAbility.TideSource).Count == 0 && TtHits(band, TollTideAbility.TideSource).Count == 0);
            BkStep(12);
            float wallR1 = TtR(wall, Vector3.zero), farR1 = TtR(far, Vector3.zero);
            Check(L + "the tide knocks 1.5 m inward (toward the centre, never into the wall)",
                  Mathf.Abs(wallR0 - wallR1 - 1.5f) < 0.2f && Mathf.Abs(farR0 - farR1 - 1.5f) < 0.2f, $"wall {wallR0:0.00} -> {wallR1:0.00}, 9 m {farR0:0.00} -> {farR1:0.00}");
            float hugR0 = TtR(hug, bk.transform.position);
            while (TtAt < tollHit - 1) { BkStep(1); maxLive = Mathf.Max(maxLive, BkPatternsLive()); }
            bool tollQuiet = TtHits(null, TollTideAbility.TollSource).Count == 0;
            BkStep(1);   // tick tollHit
            var hugToll = TtHits(hug, TollTideAbility.TollSource);
            Check(L + $"Heart Toll resolves at tick {tollHit} ({(tollT + 0) / 60f:0.00} s after the tide), nothing earlier",
                  tollQuiet && hugToll.Count == 1 && hugToll[0].Tick - tt.LastCastTick == tollHit, hugToll.Count > 0 ? $"hit at tick {hugToll[0].Tick - tt.LastCastTick}" : "no hit");
            Check(L + "a still player hugging the boss is hit by the Heart Toll", hugToll.Count == 1);
            Check(L + "a still player in the band (5.5 m) is never hit", TtHits(band).Count == 0, $"{TtHits(band).Count} hits");
            Check(L + "the knocked-in players are clear of the Heart Toll", TtHits(wall, TollTideAbility.TollSource).Count == 0 && TtHits(far, TollTideAbility.TollSource).Count == 0);
            Check(L + "one hit per instance per player", TtOnePerInstance() && TtHits().Count == 3, $"{TtHits().Count} hits");
            Check(L + "at most two damaging patterns live (one at a time here)", maxLive == 1, $"max {maxLive}");
            BkStep(1);
            Check(L + $"Cast ends at the Heart Toll (tick {tollHit + 1}); the punish window is the encounter's", !tt.Running, $"running {tt.Running} at tick {TtAt}");
            BkStep(11);
            float hugR1 = TtR(hug, bk.transform.position);
            Check(L + "the Heart Toll knocks 1.5 m outward from the boss", hugR1 - hugR0 > 1.2f, $"{hugR0:0.00} -> {hugR1:0.00}");
            if (level == Difficulty.Easy) Check(L + "Easy warnings: tide 3.75 s, Heart Toll 2.5 s", tideT == 225 && tollT == 150);
            else Check(L + "tide 3.0 s, Heart Toll 2.0 s (Hard unchanged: its extra mechanic is The Unfinished Hour)", tideT == 180 && tollT == 120);
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }

        // Escape at 4.2 m/s with finite stamina and no dash on the Throne: from the wall into the band, from the boss's side out
        // to the band. Also with the boss 6 m off-centre (the band shrinks against the wall), and at 6 m/s.
        IEnumerator TtEscapeThrone()
        {
            foreach (var bossAt in new[] { Vector3.zero, new Vector3(0, 0, -6f) })
            {
                yield return BkStart("toll_tide");
                if (bk == null) yield break;
                bk.transform.position = bossAt;
                string L = $"Toll Tide escape (Throne, boss {bossAt.magnitude:0} m off centre): ";
                BkPlace(d.Player, TtBand(90));                              // the run's player waits in the band (8 m from the off-centre boss)
                var walk = BkAdd(BkEdge(Vector3.forward));                 // south wall, 12.95 m from the centre
                var fast = BkAdd(BkEdge(new Vector3(-1, 0, 0.3f)));
                // out to the band: away from the boss, but inside 6.7 m of the centre
                var away = bossAt == Vector3.zero ? Vector3.left : Sim.Flat(-bossAt).normalized;
                var hug = BkAdd(bossAt + away * (bk.BodyRadius + 0.3f));
                var sw = BkScript(walk); var sf = BkScript(fast); var sh = BkScript(hug);
                sw.Speed = 4.2f; sf.Speed = 6f; sh.Speed = 4.2f;
                float st0 = walk.Stamina;
                if (!TtLaunch()) { Check(L + "launches", false); ScriptedParty.RemoveExtras(d.World, d.Player); yield break; }
                TtStepTo(TtReact - 1);
                sw.Goal = Sim.Flat(walk.transform.position).normalized * 5.6f;
                sf.Goal = Sim.Flat(fast.transform.position).normalized * 5.6f;
                int tideHit = tt.TideTicks - 1, arrive = -1;
                while (TtAt < tideHit) { BkStep(1); if (arrive < 0 && TtR(walk, Vector3.zero) <= 6.7f) arrive = TtAt; }
                Check(L + "walking 4.2 m/s from the wall reaches the band before the tide (no dash)",
                      TtHits(walk).Count == 0 && arrive >= 0, $"inside 6.7 m at {arrive / 60f:0.00} s of {(tideHit + 1) / 60f:0.00} s");
                Check(L + "walking 6 m/s from the wall reaches the band", TtHits(fast).Count == 0);
                TtStepTo(tideHit + TtReact);
                sh.Goal = bossAt + away * 4.9f;
                if (Sim.Flat(sh.Goal.Value).magnitude > 6.2f) sh.Goal = Sim.Flat(sh.Goal.Value).normalized * 6.2f;
                int tollHit = tt.TideTicks - 1 + tt.TollTicks;
                TtStepTo(tollHit);
                BkStep(1);
                Check(L + "walking 4.2 m/s from the boss's side clears the Heart Toll (and stays inside the tide)",
                      TtHits(hug).Count == 0, $"{TtR(hug, bossAt):0.00} m from the boss, {TtR(hug, Vector3.zero):0.00} m from the centre");
                Check(L + "nobody escaping was hit; stamina untouched (no dash needed)", TtHits().Count == 0 && walk.Stamina >= st0 - 0.01f,
                      $"{TtHits().Count} hits, stamina {st0:0} -> {walk.Stamina:0}");
                ScriptedParty.RemoveExtras(d.World, d.Player);
            }
        }

        // The key-art test map (12.5 m square): the corner is 17.7 m out, so 4.2 m/s needs one dash (brief: 3.23 s walking).
        IEnumerator TtEscapeTestMap()
        {
            yield return BkStart("toll_tide", testMap: true);
            if (bk == null) yield break;
            const string L = "Toll Tide escape (test map): ";
            Check(L + "the tide covers the corners of the square floor", CombatWorld.SquareBound && TollTideAbility.TideOuter >= CombatWorld.PlayerBound * 1.414f + 1.9f,
                  $"square {CombatWorld.SquareBound}, bound {CombatWorld.PlayerBound}, outer {TollTideAbility.TideOuter:0.00}");
            BkPlace(d.Player, BkEdge(new Vector3(-1, 0, -1)));   // still, in a corner: hit
            var dash = BkAdd(BkEdge(new Vector3(1, 0, 1)));
            var noDash = BkAdd(BkEdge(new Vector3(1, 0, -1)));
            var six = BkAdd(BkEdge(new Vector3(-1, 0, 1)));
            var sd = BkScript(dash); var sn = BkScript(noDash); var s6 = BkScript(six);
            sd.Speed = 4.2f; sn.Speed = 4.2f; s6.Speed = 6f;
            float st0 = dash.Stamina;
            if (!TtLaunch()) { Check(L + "launches", false); ScriptedParty.RemoveExtras(d.World, d.Player); yield break; }
            TtStepTo(TtReact - 1);
            foreach (var (p, s) in new[] { (dash, sd), (noDash, sn), (six, s6) }) s.Goal = Sim.Flat(p.transform.position).normalized * 5.6f;
            sd.DashNext = true;
            TtStepTo(tt.TideTicks - 1);
            Check(L + "4.2 m/s plus one dash from the corner beats the tide (finite stamina)", TtHits(dash).Count == 0 && dash.Stamina < st0,
                  $"{TtR(dash, Vector3.zero):0.00} m from the centre, stamina {st0:0} -> {dash.Stamina:0}");
            Check(L + "6 m/s from the corner beats the tide without a dash", TtHits(six).Count == 0, $"{TtR(six, Vector3.zero):0.00} m");
            Check(L + "4.2 m/s without a dash is caught from the corner (as the escape table says)", TtHits(noDash).Count == 1, $"{TtR(noDash, Vector3.zero):0.00} m");
            Check(L + "a still player in the corner is hit", TtHits(d.Player, TollTideAbility.TideSource).Count == 1);
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }

        // N = 2/4/8 scripted players (not a co-op mechanic: same shapes, full damage each, no split). Still: half far, half
        // hugging, everyone hit once. Then everyone escapes into the band at 4.2 m/s: nobody hit.
        IEnumerator TtParty(int n)
        {
            string L = $"Toll Tide N = {n}: ";
            yield return BkStart("toll_tide");
            if (bk == null) yield break;
            var all = new List<PlayerController> { d.Player };
            BkPlace(d.Player, BkEdge(Sim.Dir(0)));
            for (int i = 1; i < n; i++)
                all.Add(BkAdd(i % 2 == 0 ? BkEdge(Sim.Dir(i * 360f / n)) * (i % 4 == 0 ? 1f : 0.75f) : TtHug(i * 360f / n)));
            if (!TtLaunch()) { Check(L + "launches", false); ScriptedParty.RemoveExtras(d.World, d.Player); yield break; }
            TtStepTo(tt.TideTicks - 1 + tt.TollTicks + 1);
            int dmg = Mathf.RoundToInt(35 * d.World.DamageTakenScale);
            bool eachOnce = all.All(p => TtHits(p).Count == 1 && TtHits(p)[0].Damage == dmg);
            Check(L + "every still player (far and hugging) is hit exactly once, full damage", eachOnce && TtOnePerInstance() && d.World.Players.Count == n,
                  string.Join(", ", all.Select(p => $"{TtHits(p).Count}×{(TtHits(p).Count > 0 ? TtHits(p)[0].Damage : 0)}")));
            Check(L + "the same two hazards whatever the party size", tt.Spawned.Count == 2, $"{tt.Spawned.Count}");
            ScriptedParty.RemoveExtras(d.World, d.Player);

            yield return BkStart("toll_tide");
            if (bk == null) yield break;
            BkPlace(d.Player, TtBand(0));
            var runners = new List<(PlayerController p, ScriptedPlayer s, Vector3 home, bool fromWall)>();
            for (int i = 1; i < n; i++)
            {
                float yaw = i * 360f / n; bool fromWall = i % 2 == 0;
                var p = BkAdd(fromWall ? BkEdge(Sim.Dir(yaw)) : TtHug(yaw));
                runners.Add((p, BkScript(p), TtBand(yaw), fromWall));
            }
            if (!TtLaunch()) yield break;
            TtStepTo(TtReact - 1);
            foreach (var r in runners) if (r.fromWall) r.s.Goal = r.home;
            TtStepTo(tt.TideTicks - 1 + TtReact);
            foreach (var r in runners) if (!r.fromWall) r.s.Goal = r.home;
            TtStepTo(tt.TideTicks - 1 + tt.TollTicks + 1);
            Check(L + "everyone walks into the band at 4.2 m/s (from the wall and from the boss) and nobody is hit",
                  TtHits().Count == 0, $"{TtHits().Count} hits; band spots 5.5 m, {n} players");
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }

        // Boss death mid-windup cancels the pending hazard on the killing tick, and the Heart Toll never follows.
        IEnumerator TtDeath(int atTick, string when)
        {
            yield return BkStart("toll_tide");
            if (bk == null) yield break;
            string L = "Toll Tide: boss death " + when + ": ";
            BkPlace(d.Player, BkEdge(Vector3.left));
            var hug = BkAdd(TtHug(90));
            if (!TtLaunch()) { Check(L + "launches", false); ScriptedParty.RemoveExtras(d.World, d.Player); yield break; }
            TtStepTo(atTick);
            int liveBefore = d.World.Hazards.Count, deathTick = d.World.Tick; var pending = tt.Spawned.ToList();
            bk.DebugDefeat();
            bool clearedSameTick = d.World.Hazards.Count == 0;
            BkStep(420);
            int after = TtHits().Count(h => h.Tick > deathTick);
            Check(L + "every pending hazard is cancelled on the killing tick", bk.Dead && liveBefore == 1 && clearedSameTick,
                  $"live {liveBefore} -> {(clearedSameTick ? 0 : d.World.Hazards.Count)}, spawned {string.Join("+", pending.Select(h => h.Source))}");
            Check(L + "no hit afterwards (hugging player safe), and no Heart Toll after a death in the tide's warning",
                  after == 0 && d.World.Hazards.Count == 0 && !tt.Running && TtHits(hug).Count == 0
                  && (atTick >= 179 || pending.All(h => h.Source == TollTideAbility.TideSource)), $"{after} hits after death");
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }

        // -lastbellAbility toll_tide: the practice hook loops the move alone (Cast, then the default 2 s gap).
        IEnumerator TtPracticeLoop()
        {
            yield return BkStart("toll-tide");
            if (bk == null) yield break;
            BkPlace(d.Player, TtBand(0));
            bool launched = TtLaunch(); int first = tt.LastCastTick;
            int t = BkStepUntil(() => tt.Casts >= 2, 600);
            bool alone = bk.Abilities.All(a => a == tt || a.Casts == 0);
            Check("Toll Tide practice hook (-lastbellAbility toll_tide / toll-tide): loops the move alone",
                  launched && bk.PracticeAbility == tt && t >= 0 && alone && tt.LastCastTick - first >= 300 + 120 && tt.LastCastTick - first <= 300 + 123,
                  $"second cast {tt.LastCastTick - first} ticks after the first");
        }

        // The phase plan: the 3.5 s punish window starts when the Toll Tide's Cast ends (right after the Heart Toll).
        IEnumerator TtPunishInPlan()
        {
            yield return BkStart(null);
            if (bk == null) yield break;
            BkPlace(d.Player, TtBand(0));
            tt = bk.Ability<TollTideAbility>();
            int t0 = d.World.Tick; bk.BeginCombat();
            int launch = -1, punish = -1, recovery = 0;
            for (int i = 0; i < 60 * 60 && punish < 0; i++)
            {
                d.World.Step(); foreach (var p in d.World.Players) if (!p.Down) p.DebugSetHp(PlayerController.MaxHp);
                if (launch < 0) launch = bk.PlanLog.Where(e => e.tick >= t0 && e.what == "launch toll_tide").Select(e => e.tick).DefaultIfEmpty(-1).First();
                else punish = bk.PlanLog.Where(e => e.tick >= launch && e.what == "punish").Select(e => e.tick).DefaultIfEmpty(-1).First();
                if (bk.Dead || d.Player.Down) break;
            }
            bool tollRang = tt.Toll != null && tt.Toll.Age >= tt.Toll.ResolveAt;
            while (bk.Current == "recovery" && recovery < 300) { recovery++; d.World.Step(); foreach (var p in d.World.Players) if (!p.Down) p.DebugSetHp(PlayerController.MaxHp); }
            Check("Toll Tide in the phase plan: the 3.5 s punish window follows the Heart Toll",
                  launch >= 0 && punish - launch >= 300 && punish - launch <= 301 && tollRang && Mathf.Abs(recovery - BellkeeperEncounter.PunishTicks) <= 2,
                  launch < 0 ? "Toll Tide never launched (party down or plan stalled)" : $"launch {launch - t0}, punish {punish - launch} ticks later, recovery {recovery} ticks");
        }

        // Evidence: screenshots of the rolling front, the break and the Heart Toll, then a short real-time clip with scripted
        // players walking in from the wall.
        IEnumerator TtEvidence()
        {
            yield return BkStart("toll_tide");
            if (bk == null) yield break;
            BkPlace(d.Player, TtBand(180 + 40));
            var a = BkAdd(BkEdge(Sim.Dir(200))); var b = BkAdd(BkEdge(Sim.Dir(150)));
            var hug = BkAdd(TtHug(180));
            if (!TtLaunch()) yield break;
            TtStepTo(90); yield return Shot("bk_toll_tide_front_rolling");
            TtStepTo(tt.TideTicks - 1); yield return Shot("bk_toll_tide_break_heart_toll_warns");
            TtStepTo(tt.TideTicks - 1 + 80); yield return Shot("bk_toll_tide_heart_toll");
            bk.Halt(); d.World.ClearTransient();
            BkPlace(a, BkEdge(Sim.Dir(200))); BkPlace(b, BkEdge(Sim.Dir(150))); BkPlace(hug, TtHug(180));
            var sa = BkScript(a); var sb = BkScript(b); var sh = BkScript(hug);
            sa.Goal = TtBand(200); sb.Goal = TtBand(150); sh.Goal = TtBand(180);
            yield return BkClip("bk_toll_tide", 6f);
            ScriptedParty.RemoveExtras(d.World, d.Player);
        }
    }
}
