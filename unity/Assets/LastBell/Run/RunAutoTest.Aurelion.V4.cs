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
            Time.timeScale = 4;
            if (V2Wants("aurelion.meridian_towers")) yield return V2Section("v4 Meridian Towers", V4Towers());
            if (V2Wants("aurelion.sunspots")) yield return V2Section("v4 Sunspots", V4Sunspots());
            if (V2Wants("aurelion.total_eclipse")) yield return V2Section("v4 Total Eclipse", V4Eclipse());
            if (V2Wants("aurelion.severance")) yield return V2Section("v4 Severance", V4Severance());
            if (V2Wants("aurelion.meridian_recall")) yield return V2Section("v4 Meridian Recall (Hard)", V4Recall());
            if (V2Wants("aurelion.last_light")) yield return V2Section("v4 Last Light and the enrage", V4LastLight());
            if (V2Wants("aurelion.v4_fight")) yield return V2Section("v4 fight", V4Fight());
            if (V2Wants("aurelion.v4_dps")) yield return V2Section("v4 damage per second (boss HP tuning)", V4Dps());
            Time.timeScale = 1;
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

        // ================================================================ Meridian Recall (Hard only)
        IEnumerator V4Recall()
        {
            // a 3-beam volley through the player at 9 m, then the boss steps 6 m away: the replay keeps the old lines
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            var rc = V2Boss.Recall; var ln = V2Boss.Lances;
            yield return V2Fire(ln.Run(3, 1, false), 10);
            var volley = ln.Volleys.Count > 0 ? ln.Volleys[ln.Volleys.Count - 1] : new List<Hazard>();
            yield return V2Ticks(30);                                  // let the volley's own knockback and stun play out
            float aim = ln.AimOf(0);
            V2Boss.transform.position = new Vector3(0, 0, -6f);
            V2Main.DebugSetHp(PlayerController.MaxHp);
            V2Place(V2Main, Sim.Dir(aim) * 8f);                      // on the remembered beam, 8 m from the old centre
            int hits0 = V2HitsOn(V2Main, "Meridian Recall").Count(), start = V2W.Tick;
            V2Begin(rc.Run()); yield return V2Ticks(2);
            var rep = rc.Replay.ToList();
            bool copied = rep.Count == volley.Count && rep.Count == 6 && rep.Select((h, i) => Sim.Flat(h.Origin - volley[i].Origin).sqrMagnitude < 1e-4f && Mathf.Abs(Mathf.DeltaAngle(h.Yaw, volley[i].Yaw)) < 0.01f).All(x => x);
            V2Check("the replay copies the last volley's 6 half-lanes: its old centre and locked bearings, not the boss's new spot", copied,
                $"{rep.Count} lanes; replay {string.Join(" ", rep.Select(h => $"{h.Yaw:0.#}@{h.Origin.z:0.#}"))} | volley {string.Join(" ", volley.Select(h => $"{h.Yaw:0.#}@{h.Origin.z:0.#}"))}");
            V2Check("locked from its first tick, 3.5 s to the hit (no Hard x0.7)", rep.Count > 0 && rep.All(h => h.Locked && h.ResolveAt == Sim.Ticks(MeridianRecall.WarnS)), rep.Count > 0 ? $"lock {rep[0].LockAt}, resolve {rep[0].ResolveAt}" : "none");
            yield return V2WaitIdle(10); yield return V2Ticks(3);
            var got = V2HitsOn(V2Main, "Meridian Recall").Skip(hits0).ToList();
            V2Check("standing on the remembered line at 8 m: hit once for 65 on Hard (heavy, not a one-shot)", got.Count == 1 && got[0].Damage == 65 && !V2Main.Down, $"{got.Count} hits, {string.Join("/", got.Select(h => h.Damage))}, down {V2Main.Down}");

            // the same volley, but standing in the gap between two remembered beams: untouched
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            rc = V2Boss.Recall; ln = V2Boss.Lances;
            yield return V2Fire(ln.Run(3, 1, false), 10);
            yield return V2Ticks(30);
            aim = ln.AimOf(0);
            V2Main.DebugSetHp(PlayerController.MaxHp);
            V2Place(V2Main, Sim.Dir(aim + 30f) * 8f);                // 3 beams are 60 degrees apart: 30 degrees is mid-gap
            hits0 = V2HitsOn(V2Main, "Meridian Recall").Count();
            yield return V2Fire(rc.Run(), 10); yield return V2Ticks(3);
            V2Check("in the gap between remembered beams: no hit", V2HitsOn(V2Main, "Meridian Recall").Count() == hits0);

            // the solo loop: a volley, then its replay
            V2Check("the F6 / -lastbellAbility loop knows aurelion.meridian_recall", AurelionArgs.Known("aurelion.meridian_recall"));
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
            // 3 Nov: the halves' circles fall every 1.5 s on one half or the other, so about 9 in 15 s solo (one per beat)
            V2Check("Severance halves: a circle falls at your feet on each beat (3 Nov)", sv.HalfCircles >= 6, $"{sv.HalfCircles} circles");
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
            foreach (var cls in new[] { "Arcanist", "Chimesinger", "Tollbreaker" })
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

            // readability A2: while Last Light runs the camera keeps the boss in view with you; afterwards it follows you alone
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 4, new Vector3(0, 0, -8f));
            if (V2Boss == null) yield break;
            var framed = RunDirector.FrameAlso == null;
            V2Begin(LastLightFloorFx.During(V2W, V2Boss.Light, V2Boss.Light.Run().Take(2))); yield return V2Ticks(5);
            var f = RunDirector.FrameAlso?.Invoke();
            framed &= f.HasValue && Sim.Flat(f.Value - V2Boss.transform.position).sqrMagnitude < 0.01f;
            yield return V2WaitIdle(20);
            V2Check("Last Light: the camera frames the boss with the player for the phase, then lets go", framed && RunDirector.FrameAlso == null, f.HasValue ? $"framed {f.Value}" : "not framed");

            // the fall (proposal, 1 Nov, Chzy18 picked "B: Fall"): standing where the first slice falls drops you onto the
            // nearest standing floor for a heavy hit that is never a one-shot, Medium and Hard
            foreach (var lv in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, lv, new[] { new Vector3(0.5f, 0, 6f) }, 4, new Vector3(0, 0, -8f));
                if (V2Boss == null) yield break;
                var lf = V2Boss.Light;
                // the previous check leaves its locked slice live: clear it and stand the player back on the mark
                V2Boss.ClearPhasePools(); V2Main.Relocate(new Vector3(0.5f, 0, 6f)); V2Main.DebugSetHp(PlayerController.MaxHp);
                V2Begin(LastLightFloorFx.During(V2W, lf, lf.Run().Take(2)));   // ends on the first slice's lock tick
                yield return V2WaitIdle(20);
                yield return V2Ticks(10);
                var fall = lf.Falls.Count > 0 ? lf.Falls[0] : default;
                int want = Mathf.RoundToInt(LastLight.FallDamage * V2W.DamageTakenScale);
                bool landedClear = fall.p != null && !lf.OverHole(fall.to, LastLight.Clearance - 0.05f) && !lf.OverHole(V2Main.transform.position);
                float moved = fall.p != null ? Sim.Flat(fall.to - fall.from).magnitude : -1;
                yield return V2Ticks(30);
                V2Check($"Last Light ({lv}): stepping onto a fallen slice drops you onto the nearest standing floor for {want}, never a one-shot",
                    lf.Falls.Count == 1 && fall.damage == want && !V2Main.Down && V2Main.Hp == PlayerController.MaxHp - want && landedClear && moved < 4f,
                    fall.p != null ? $"{fall.damage} damage, moved {moved:0.0} m to {fall.to}, hp {V2Main.Hp}, falls {lf.Falls.Count}" : "no fall");
                V2Boss.ClearPhasePools();
                yield return V2WaitIdle(20);
            }

            // the hard enrage at 6:30
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.TestClock = true; V2Boss.DebugSetFightSeconds(AurelionEncounter.EnrageS - 1f);
            yield return Until(() => V4Wiped, 10, "the 6:30 enrage");
            V2Check("Final Noon wipes the party at 6:30 (the hard enrage)", V4Wiped && V2Boss.WipeCause.Contains("enrage"), V2Boss.WipeCause ?? "no wipe");
            if (V2Boss) V2Boss.TestClock = false;
        }

        // ================================================================ the fight
        IEnumerator V4Fight()
        {
            // Solar Hunger: each Collapse is hungrier
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 2.5f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            var radii = new List<float>(); var dmg = new List<int>(); var pulls = new List<float>();
            for (int i = 0; i < 4; i++) { yield return V3FireToppedUp(V2Boss.HungryCollapse(), 10); radii.Add(V2Boss.Zenith.Last.Inner); dmg.Add(V2Boss.Zenith.Last.Damage); pulls.Add(V2Boss.DevourPull); }
            V2Check("Solar Hunger: every Collapse shrinks its disc 0.5 m (to 3.0 m) and hits 5 harder (50 > 60)",
                radii.SequenceEqual(new[] { 4.5f, 4.0f, 3.5f, 3.0f }) && dmg.SequenceEqual(new[] { 50, 55, 60, 60 }), $"radii {string.Join("/", radii)}, damage {string.Join("/", dmg)}");
            // A17 Devour: after the 4th Collapse the 7.5 m/s pull runs; a player 9 m out loses 0.5 s x 7.5 m ~ 3.75 m standing still
            {
                var a = V2Boss; int left0 = a.DevourLeft;
                V2Place(V2Main, new Vector3(0, 0, 9f)); float d0 = Sim.Flat(V2Main.transform.position - a.transform.position).magnitude;
                yield return Until(() => a.DevourLeft <= left0 - 30 || a.DevourLeft == 0, 5, "Devour 0.5 s");
                float d1 = Sim.Flat(V2Main.transform.position - a.transform.position).magnitude, hp = V2Main.Hp;
                V2Check("Devour: every Collapse pulls everyone in for 2.0 s at 6.5 > 7.0 > 7.5 m/s (Easy fixed 6.5), faster than a walk, no damage",
                    pulls.SequenceEqual(new[] { 6.5f, 7.0f, 7.5f, 7.5f }) && AurelionEncounter.DevourSpeed(3, true) == 6.5f && left0 >= Sim.Ticks(2.0f) - 2
                    && Mathf.Abs((d0 - d1) - 3.75f) < 0.5f && AurelionEncounter.DevourCap < PlayerController.SprintSpeed && AurelionEncounter.DevourFirst > PlayerController.WalkSpeed,
                    $"pulls {string.Join("/", pulls)}, ticks left {left0}, pulled {d0 - d1:0.00} m in 0.5 s, hp {hp}");
            }

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

            // 20 %: Last Light starts at once (Chzy18, 2 Nov), cutting off the running block with no hit
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 3, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; V3Bot(V2Main, BotMode.Perfect);
                a.DebugSetHpFraction(0.3f); a.TestHold = false;
                yield return Until(() => V2W.Hazards.Live.Count > 0 && a.Current != "phase shift", 15, "a Phase 3 block with shapes up");
                int hp0 = V2Main.Hp, cuts0 = a.Cuts, t0 = V2W.Tick;
                a.DebugSetHpFraction(0.19f);
                yield return Until(() => a.Phase == 4, 5, "Last Light");
                int took = V2W.Tick - t0;
                V2Check("at 20 %: Last Light starts within 2 ticks, the running block cut off with no hit",
                    a.Phase == 4 && took <= 2 && a.Cuts == cuts0 + 1 && !a.Sever.Active && V2Main.Hp >= hp0 && !V4Wiped,
                    $"phase {a.Phase} after {took} ticks, cuts {a.Cuts - cuts0}, severance {a.Sever.Active}, hp {hp0}>{V2Main.Hp}");
                a.TestHold = true; V3Unbot();
            }

            // Errant Planets (Phase 2, 3 Nov): three planets circle at 5.5 m/s; standing on a path costs one 30 hit as one passes
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; a.DebugSetPhaseV4(2); a.TestExtras = true;
                yield return Until(() => a.Planets.Live, 3, $"planets up (phase {a.Phase}, current {a.Current}, combat {V2W.CombatActive}, eclipse {a.Eclipse.Active})");
                if (!a.Planets.Live) { V2Check("Errant Planets: planets appear in Phase 2", false, $"phase {a.Phase}, current {a.Current}"); a.TestExtras = false; yield break; }
                int n = a.Planets.Count; var p0 = n > 0 ? a.Planets.PlanetAt(0) : Vector3.zero;
                yield return V2Ticks(60);
                float moved = n > 0 ? Sim.Flat(a.Planets.PlanetAt(0) - p0).magnitude : 0;
                // stand 3 m ahead of planet 1 on its orbit (it turns +): it reaches us in about 0.7 s
                var c = new Vector3(1.5f, 0, 1.0f); var ahead = c + Quaternion.Euler(0, 25f, 0) * Sim.Flat(a.Planets.PlanetAt(0) - c);
                V2Main.DebugSetHp(PlayerController.MaxHp); V2Place(V2Main, ahead); int hp0 = V2Main.Hp;
                yield return V2Ticks(Sim.Ticks(1.6f));
                int lost = hp0 - V2Main.Hp;
                a.TestExtras = false; yield return V2Ticks(2);
                V2Check("Errant Planets: 3 planets in Phase 2 at 5.5 m/s; one passing deals 30 once; gone when they stop",
                    n == 3 && Mathf.Abs(moved - ErrantPlanets.Speed) < 0.3f && lost == ErrantPlanets.Damage && !a.Planets.Live, $"planets {n}, moved {moved:0.00} m in 1 s, hp lost {lost}, live after {a.Planets.Live}");
            }
            // Falling Meridians (Phase 3, 3 Nov): every 3.5 s a 2.5 m circle at each player's feet; standing still costs 30
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 7f) }, 3, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; a.DebugSetPhaseV4(3); a.Meridians.Reset(); a.TestExtras = true;
                V2Main.DebugSetHp(PlayerController.MaxHp); int hp0 = V2Main.Hp;
                yield return V2Ticks(Sim.Ticks(FallingMeridians.EveryS) + a.Meridians.Warn + 3);
                int lost = hp0 - V2Main.Hp, casts = a.Meridians.Casts, n = a.Meridians.Last.Count;
                float off = n > 0 ? Sim.Flat(a.Meridians.Last[0].Origin - V2Main.transform.position).magnitude : 99;
                a.TestExtras = false;
                V2Check("Falling Meridians: after 3.5 s, one 2.5 m circle at each player's feet, 1.2 s warning on Medium, 30 if you stand still",
                    casts == 1 && n == 1 && off < 0.3f && lost == FallingMeridians.Damage && a.Meridians.Warn == Sim.Ticks(1.2f), $"casts {casts}, circles {n}, {off:0.00} m from the player, hp lost {lost}");
            }

            // Phases 2 and 3 shorter windows (2 Nov, proposal): Zenith Collapse 2.55 > 2.17 > 1.91 s on Medium (3 Nov); damage unchanged
            {
                var dm = new List<int>(); var zw = new List<int>();
                for (int ph = 1; ph <= 3; ph++)
                {
                    yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, ph, Vector3.zero);
                    if (V2Boss == null) yield break;
                    V2Boss.DebugSetPhaseV4(ph); zw.Add(V2Boss.Zenith.WindowTicks);
                    yield return V2Fire(V2Boss.Lances.Run(3, 1, false), 10);
                    dm.Add(V2Boss.Lances.Volleys.Count > 0 && V2Boss.Lances.Volleys[V2Boss.Lances.Volleys.Count - 1].Count > 0 ? V2Boss.Lances.Volleys[V2Boss.Lances.Volleys.Count - 1][0].Damage : -1);
                }
                V2Check("Phases 2 and 3: windows x0.85 / x0.75, damage unchanged (Lances 30 in every phase)",
                    dm.SequenceEqual(new[] { 30, 30, 30 }) && zw[1] == Sim.Ticks(2.55f * AurelionAbility.P2Window) && zw[2] == Sim.Ticks(2.55f * AurelionAbility.P3Window), $"Lances {string.Join("/", dm)}, Collapse window ticks {string.Join("/", zw)}");
            }

            // the perfect route through Phases 1-3 on Hard: survives every block (potions allowed), never wipes
            foreach (var level in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, level, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                V2Main.RefreshHealth(true);   // V2Setup restores HP, not potions
                V2Boss.DebugResetFight();
                int recallLog0 = V2Boss.BlockLog.Count;
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
                var recalls = V2Boss == null ? new List<int>() : V2Boss.BlockLog.Skip(recallLog0).Where(b => b.block == "Meridian Recall").Select(b => b.phase).ToList();
                if (level == Difficulty.Hard) V2Check("Hard: Meridian Recall plays once in Phase 2 and once in Phase 3, each after its volley", recalls.SequenceEqual(new[] { 2, 3 }), $"recall phases {string.Join("/", recalls)}");
                else V2Check($"{level}: no Meridian Recall (Hard only)", recalls.Count == 0, $"recall phases {string.Join("/", recalls)}");
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
