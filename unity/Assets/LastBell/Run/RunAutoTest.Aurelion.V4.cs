using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Aurelion ability set v4, mythic end-boss difficulty (abilities-v4/ABILITIES-v4.md, proposal):
    //   Meridian Towers, Sunspots, Total Eclipse, Severance, Last Light, Solar Hunger, the wipes, the enrage, the
    //   intermission flow, the perfect route through Phases 1-3 and the memory-blind bot.
    //   -lastbellAurelionV4Only    with -lastbellAurelionOnly: only this section
    // The long runs play at 4x game speed.
    public sealed partial class RunAutoTest
    {
        static bool AurelionV4Only => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionV4Only", StringComparison.OrdinalIgnoreCase));

        IEnumerator AurelionV4Checks()
        {
            Note("---------- Aurelion ability set v4, mythic (proposal) ----------");
            AurelionArgs.SoloOverride = null; AurelionArgs.WindowsMediumOverride = false;
            AurelionArgs.SetOverride = "v4";
            Time.timeScale = 4;
            if (V2Wants("aurelion.meridian_towers")) yield return V2Section("v4 Meridian Towers", V4Towers());
            if (V2Wants("aurelion.sunspots")) yield return V2Section("v4 Sunspots", V4Sunspots());
            if (V2Wants("aurelion.total_eclipse")) yield return V2Section("v4 Total Eclipse", V4Eclipse());
            if (V2Wants("aurelion.severance")) yield return V2Section("v4 Severance", V4Severance());
            if (V2Wants("aurelion.last_light")) yield return V2Section("v4 Last Light and the enrage", V4LastLight());
            if (V2Wants("aurelion.v4_fight")) yield return V2Section("v4 fight", V4Fight());
            if (V2Wants("aurelion.v4_dps")) yield return V2Section("v4 damage per second (boss HP tuning)", V4Dps());
            Time.timeScale = 1;
            AurelionArgs.SetOverride = null;
        }

        // a body that may end in a wipe (the practice then ends): wait for the body or the wipe, whichever comes first
        IEnumerator V4FireUntilWipe(IEnumerable<int> body) { V2Begin(body); yield return Until(() => V2Boss == null || !V2Boss.TestBusy || V4Wiped, 20, "v4 body or its wipe"); yield return V2Ticks(5); }
        bool V4Wiped => V2Boss != null && V2Boss.WipeCause != null;
        IEnumerator V4Bots(BotMode mode) { foreach (var p in V2Party) V3BotAt(p, mode); yield break; }
        void V3BotAt(PlayerController p, BotMode mode) => p.Input = new AllyBot(V2Party.IndexOf(p), V2Party.Count, mode) { HoldRange = V3Ranged };

        // ================================================================ Meridian Towers
        IEnumerator V4Towers()
        {
            // solo, the perfect route: towers I, II, III filled in turn, no wipe, no damage
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var tw = V2Boss.Towers;
            V3Bot(V2Main, BotMode.Perfect);
            yield return V2Fire(tw.Run(), 20);
            V2Check("solo: 3 numbered towers, 6.5 m out, at least 120 degrees apart, 2.5 s apart",
                tw.Towers.Count == 3 && tw.Towers.All(t => Mathf.Abs(Sim.Flat(t.At).magnitude - 6.5f) < 0.01f) && Vector3.Distance(tw.Towers[0].At, tw.Towers[1].At) >= 11.2f
                && tw.Towers[1].ResolveTick - tw.Towers[0].ResolveTick == Sim.Ticks(2.5f), $"{tw.Towers.Count} towers");
            V2Check("solo: the perfect route is in each tower as it lands, in order; no wipe, no damage", !tw.Failed && !V4Wiped && !V2WasHit(V2Main, "Meridian Towers") && tw.Towers.All(t => t.Resolved && t.In.Count >= 1),
                string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source + " " + h.Damage)));
            V3Unbot();

            // N = 4: 2 towers landing together, 2 + 2 (or 1 + 3), filled by their assigned players
            yield return V2Setup(4, Difficulty.Medium, V2Ring(4, 9f), 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V4Bots(BotMode.Perfect);
            yield return V2Fire(V2Boss.Towers.Run(), 20);
            V2Check("N = 4: 2 towers whose counts add up to 4, both full as they land together, no wipe, no extras hit", V2Boss.Towers.Towers.Count == 2 && V2Boss.Towers.Towers.Sum(t => t.Need) == 4 && V2Boss.Towers.Towers.All(t => t.In.Count == t.Need && t.ResolveTick == V2Boss.Towers.Towers[0].ResolveTick) && !V2Boss.Towers.Failed && !V4Wiped && !V2Party.Any(p => V2WasHit(p, "Meridian Towers")), $"in: {string.Join("/", V2Boss.Towers.Towers.Select(t => t.In.Count))}");
            V3Unbot();

            // the wipe: nobody goes to tower I
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, -12f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V4FireUntilWipe(V2Boss.Towers.Run());
            V2Check("a tower short of players wipes the party (Total Eclipse), even solo", V2Boss.Towers.Failed && V4Wiped && V2Main.Down && V2Boss.WipeCause.StartsWith("Total Eclipse"), V2Boss.WipeCause ?? "no wipe");
        }

        // ================================================================ Sunspots
        IEnumerator V4Sunspots()
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var sp = V2Boss.Spots;
            V3Bot(V2Main, BotMode.Perfect);
            yield return V2Fire(sp.Run(), 20);
            yield return V2Ticks(30);
            V2Check("solo: two sigils on you, 1.5 s apart; the perfect route drops them apart and is never hit by a blast",
                sp.Spots.Count == 2 && sp.Spots.All(s => s.Who == V2Main && s.Blast != null) && !V2WasHit(V2Main, "Sunspots") && !sp.Chained,
                string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)));
            V2Check("each detonation leaves a scorch pool (3.0 m, burning) that lasts 75 s", sp.Spots.All(s => s.Pool != null && V2W.Hazards.Live.Contains(s.Pool) && s.Pool.BurnPerSecond > 0));
            V2Boss.ClearPhasePools(); yield return V2Ticks(3);
            V2Check("the pools survive a phase change (the floor fills from the start)", sp.Spots.All(s => V2W.Hazards.Live.Contains(s.Pool)));
            foreach (var s in sp.Spots) V2W.Hazards.Cancel(s.Pool);
            V3Unbot();

            // the chain reaction: N = 3 has 2 carriers; all three stand together, still
            yield return V2Setup(3, Difficulty.Medium, new[] { new Vector3(0, 0, 8f), new Vector3(1, 0, 8f), new Vector3(-1, 0, 8f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V4FireUntilWipe(V2Boss.Spots.Run());
            V2Check("a blast that catches another carrier sets off a Solar Flare: the party wipes", V2Boss.Spots.Chained && V4Wiped && V2Boss.WipeCause.StartsWith("Solar Flare"), V2Boss.WipeCause ?? "no wipe");
        }

        // ================================================================ Total Eclipse
        IEnumerator V4Eclipse()
        {
            // broken: the party breaks the shield (test damage) and the intermission ends early; the perfect route stays lit
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            var ec = V2Boss.Eclipse;
            V3Bot(V2Main, BotMode.Perfect);
            int start = V2W.Tick;
            V2Begin(ec.Run());
            yield return V2Ticks(10);
            int shield = V2Boss.ShieldHp;
            V2Check("Corona Shield raised: 700 per fighting player", shield == V2Boss.Eclipse.ShieldFor() && shield >= TotalEclipse.ShieldPerFighter, $"{shield}");
            yield return V2Ticks(Sim.Ticks(8f));
            for (int i = 0; i < 400 && V2Boss.ShieldHp > 0; i++) V2Boss.TakeHit(50, V2Boss.transform.position + Vector3.up * 2);
            yield return V2WaitIdle(10);
            V2Check("breaking the shield ends the intermission early, no wipe", ec.Broken && !V4Wiped && (V2W.Tick - start) < Sim.Ticks(TotalEclipse.LimitS), $"{(V2W.Tick - start) / (float)Sim.Hz:0.0} s");
            V2Check("the perfect route keeps to the moving light wells: at most one Umbra tick (a star can push you out)", V2HitsOn(V2Main, "Umbra").Count() <= 1, string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source)));
            V3Unbot();

            // held: nobody breaks it, Total Eclipse wipes at 25 s; standing still in the dark takes Umbra
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, -12f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Begin(V2Boss.Eclipse.Run());
            float t = 0; while (t < 40 && !V4Wiped) { if (V2Main && !V2Main.Down) V2Main.DebugSetHp(PlayerController.MaxHp); t += Time.deltaTime; yield return null; }
            V2Check("an unbroken Corona Shield wipes the party after 25 s", V4Wiped && V2Boss.WipeCause.StartsWith("Total Eclipse"), V2Boss.WipeCause ?? "no wipe");
            V2Check("standing in the dark takes Umbra every second after 1 s", V2HitsOn(V2Main, "Umbra").Count() >= 20, V2HitsOn(V2Main, "Umbra").Count() + " Umbra hits");
        }

        // ================================================================ Severance
        IEnumerator V4Severance()
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-6f, 0, 2f) }, 3, Vector3.zero);
            if (V2Boss == null) yield break;
            var sv = V2Boss.Sever;
            V3Bot(V2Main, BotMode.Perfect);
            yield return V3FireToppedUp(sv.Run(), 40);
            V2Check("solo: the perfect route stays ahead of the turning line and never crosses it", sv.Crossings == 0 && !V4Wiped, $"{sv.Crossings} crossings; hits {string.Join(", ", V2HitsOn(V2Main).Select(h => h.Source))}");
            V3Unbot();

            // standing still just ahead of the line's sweep: it passes over you
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 0) }, 3, new Vector3(0, 0, -6f));
            if (V2Boss == null) yield break;
            sv = V2Boss.Sever;
            float yaw = Severance_NextYaw(sv);
            V2Place(V2Main, Sim.Dir(yaw + 30f) * 8f);
            yield return V3FireToppedUp(sv.Run(), 40);
            V2Check("standing still where the line will sweep: crossed and hit (50)", sv.Crossings >= 1 && V2HitsOn(V2Main, "Severance").Any(), $"{sv.Crossings} crossings");

            // co-op: both players on one side, the other half falls
            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 0) }, 3, new Vector3(0, 0, -6f));
            if (V2Boss == null) yield break;
            sv = V2Boss.Sever;
            yaw = Severance_NextYaw(sv);
            V2Place(V2Party[0], Sim.Dir(yaw + 90f) * 7f); V2Place(V2Party[1], Sim.Dir(yaw + 100f) * 7f);
            yield return V3FireToppedUp(sv.Run(), 10);
            V2Check("co-op: everyone on one side for 0.5 s, the other half falls: the party wipes", V4Wiped && V2Boss.WipeCause.StartsWith("Severance"), V2Boss.WipeCause ?? "no wipe");
        }
        // expected damage: each class alone, the bot rotation (AllyBot.Attacks) for 30 s against a held boss with no
        // attacks, at its range; reports DPS and the boss HP a 6:00 solo fight would want (about 4.6 min of uptime)
        IEnumerator V4Dps()
        {
            foreach (var cls in new[] { "Arcanist", "Chimesinger", "Vanguard" })
            {
                yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, -12f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                var p = PlayerController.Spawn(V2W, cls, new Vector3(0, 0, 8f), 180);
                p.AbilitiesEnabled = true; p.Input = new AllyBot(0, 1, BotMode.Passive);
                bool was = AllyBot.Attacks; AllyBot.Attacks = true;
                V2Boss.DebugSetHpFraction(1f);
                int hp0 = V2Boss.Hp; float t0 = V2W.Tick;
                yield return V2Ticks(Sim.Ticks(30f));
                float secs = (V2W.Tick - t0) / Sim.Hz; int dealt = hp0 - V2Boss.Hp;
                AllyBot.Attacks = was;
                V2W.Players.Remove(p); Destroy(p.gameObject);
                float dps = dealt / Mathf.Max(1, secs);
                Note($"DPS {cls}: {dps:0.0} ({dealt} in {secs:0} s) => a 6:00 solo fight (4.6 min uptime) wants boss HP about {dps * 276:0}");
                V2Check($"{cls} bot deals damage", dealt > 0, dealt.ToString());
            }
            AllyBot.Attacks = false;
        }

        static float Severance_NextYaw(Severance sv) => new[] { 0f, 60f, 120f }[sv.Casts % 3];

        // ================================================================ Last Light and the enrage
        IEnumerator V4LastLight()
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 4, Vector3.zero);
            if (V2Boss == null) yield break;
            var ll = V2Boss.Light;
            V3Bot(V2Main, BotMode.Perfect);
            V2Begin(ll.Run());
            yield return Until(() => ll.Fallen >= 7 || V2Main.Down, 140, "seven shadow sectors");
            bool aliveAt7 = !V2Main.Down;
            yield return Until(() => V4Wiped, 30, "Final Noon after the eighth sector");
            V2Check("the floor falls one 45-degree sector every 12 s, then every 10 s; the perfect route lives through seven", aliveAt7 && ll.Fallen == 8, $"{ll.Fallen} fallen, alive at 7 {aliveAt7}");
            V2Check("the eighth sector ends the fight: Final Noon wipes the party", V4Wiped && V2Boss.WipeCause.StartsWith("Final Noon"), V2Boss.WipeCause ?? "no wipe");
            V3Unbot();

            // the hard enrage at 7:00
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.TestClock = true; V2Boss.DebugSetFightSeconds(AurelionEncounter.EnrageS - 1f);
            yield return Until(() => V4Wiped, 10, "the 7:00 enrage");
            V2Check("Final Noon wipes the party at 6:30 (the hard enrage)", V4Wiped && V2Boss.WipeCause.Contains("enrage"), V2Boss.WipeCause ?? "no wipe");
            if (V2Boss) V2Boss.TestClock = false;
        }

        // ================================================================ the fight
        IEnumerator V4Fight()
        {
            // Solar Hunger: each Collapse is hungrier
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 2.5f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var radii = new List<float>(); var dmg = new List<int>();
            for (int i = 0; i < 4; i++) { yield return V3FireToppedUp(V2Boss.HungryCollapse(), 10); radii.Add(V2Boss.Zenith.Last.Inner); dmg.Add(V2Boss.Zenith.Last.Damage); }
            V2Check("Solar Hunger: every Collapse shrinks its disc 0.5 m (to 3.0 m) and hits 5 harder (50 > 60)",
                radii.SequenceEqual(new[] { 4.5f, 4.0f, 3.5f, 3.0f }) && dmg.SequenceEqual(new[] { 50, 55, 60, 60 }), $"radii {string.Join("/", radii)}, damage {string.Join("/", dmg)}");

            // the 70 % flow: the intermission plays once, then Misalignment
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; V3Bot(V2Main, BotMode.Perfect);
                a.DebugSetHpFraction(0.69f); a.TestHold = false;
                float t = 0; bool sawEclipse = false;
                while (t < 120 && d.State == RunState.Combat && !(a.Phase == 2 && a.Current != "phase shift" && sawEclipse))
                {
                    if (a.Eclipse.Active) { sawEclipse = true; if (a.ShieldHp > 0 && a.Eclipse.Wells.Count > 0) a.TakeHit(40, a.transform.position + Vector3.up * 2); }
                    if (V2Main && !V2Main.Down) V2Main.DebugSetHp(PlayerController.MaxHp);
                    t += Time.deltaTime; yield return null;
                }
                V2Check("at 70 %: Total Eclipse plays, then Misalignment (one transition)", sawEclipse && a.Phase == 2 && a.IntermissionDone && !V4Wiped, $"phase {a.Phase}, eclipse {sawEclipse}, wipe {a.WipeCause}");
                a.TestHold = true; V3Unbot();
            }

            // the perfect route through Phases 1-3 on Hard: survives every block (potions allowed), never wipes
            foreach (var level in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, level, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                V2Main.RefreshHealth(true);   // V2Setup restores HP, not potions
                V2Boss.DebugResetFight();
                V3Bot(V2Main, BotMode.Perfect);
                var detail = new List<string>();
                Action<PlayerController, Hazard, int> onHit = (p, h, dmg) =>
                {
                    if (p != V2Main || h.BurnPerSecond > 0) return;
                    string blk = V2Boss.BlockLog.Count > 0 ? V2Boss.BlockLog[V2Boss.BlockLog.Count - 1].block : "?";
                    detail.Add($"{h.Source} {dmg} in [{blk}] hp {p.Hp} | {(p.Input as AllyBot)?.DebugLine}");
                };
                V2W.PlayerHit += onHit;
                for (int ph = 1; ph <= 3 && !V2Main.Down && !V4Wiped; ph++)
                {
                    V2Boss.DebugSetPhase(ph);
                    V2Begin(V2Boss.TestPhasePlan(ph));
                    yield return Until(() => V2Boss == null || !V2Boss.TestBusy || V2Main.Down || d.State != RunState.Combat, 240, $"v4 phase {ph} plan");
                    V2Boss?.ClearPhasePools();
                }
                V2W.PlayerHit -= onHit;
                foreach (var s in detail) Note($"v4 {level} perfect hit: {s}");
                Note($"v4 {level} perfect route, Phases 1-3: down {V2Main.Down}, wipe {V2Boss?.WipeCause}, potions {V2Main.PotionsDrunk}; hits: {V3HitList()}");
                V2Check($"{level}: the perfect route survives Phases 1-3 (every block) without a wipe", !V2Main.Down && !V4Wiped, V2Boss?.WipeCause ?? V3HitList());
                V3Unbot();
            }

            // the memory-blind reflex bot (which does every VISIBLE job: towers, wells, sides) does not survive Phases 1-2 on Hard
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Main.RefreshHealth(true);
            V3Bot(V2Main, BotMode.Reflex);
            V2Boss.DebugSetPhase(1);
            for (int ph = 1; ph <= 2 && !V2Main.Down && d.State == RunState.Combat; ph++)
            {
                V2Boss.DebugSetPhase(ph);
                V2Begin(V2Boss.TestPhasePlan(ph));
                yield return Until(() => V2Boss == null || !V2Boss.TestBusy || V2Main.Down || d.State != RunState.Combat, 240, $"v4 phase {ph}, blind bot");
                V2Boss?.ClearPhasePools();
            }
            Note($"v4 Hard blind bot, Phases 1-2: down {V2Main.Down}, wipe {V2Boss?.WipeCause}; hits: {V3HitList()}");
            V2Check("Hard: the memory-blind reflex bot dies in Phases 1-2 (memory mechanics)", V2Main.Down, V2Boss?.WipeCause ?? V3HitList());
            V3Unbot();
        }
    }
}
