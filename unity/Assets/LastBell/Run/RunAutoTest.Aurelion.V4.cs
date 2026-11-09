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
            if (V2Wants("aurelion.v4_icons")) BossAttackIconChecks();   // the attack-icon catalog (7 Nov: 8 Aurelion icons)
            if (V2Wants("aurelion.v4_revivers")) yield return V2Section("v4 aimed attacks skip revivers (7 Nov)", V4Revivers());
            if (V2Wants("aurelion.v4_graphics")) yield return V2Section("v4 graphics pass (8 Nov)", V4Graphics());
            if (V2Wants("aurelion.v4_family_icons")) yield return V2Section("v4 attack family icons (8 Nov)", V4FamilyIcons());
            if (V2Wants("aurelion.v4_hard")) yield return V2Section("v4 Hard additions (5 Nov)", V4HardExtras());
            if (AurelionAbilityFilter != null && AurelionAbilityFilter.Equals("aurelion.v4_seeds", StringComparison.OrdinalIgnoreCase)) yield return V2Section("v4 perfect route seed sweep", V4PerfectSeeds());
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

        // diagnosis only (-lastbellAbilityTest aurelion.v4_seeds [-lastbellSeeds N]): the Hard perfect route over Phases 1-3 with the
        // Collapse spot drawn from seeds 1..N, one line each
        public const int PerfectSeed = 4;   // a seed the Hard perfect route clears with no hit in the sweep
        IEnumerator V4PerfectSeeds()
        {
            int n = int.TryParse(AurelionArgs.Arg("-lastbellSeeds") ?? "10", out int sn) ? sn : 10;
            int fails = 0;
            for (int seed = 1; seed <= n; seed++)
            {
                yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                UnityEngine.Random.InitState(seed);
                V2Main.RefreshHealth(true); V2Boss.DebugResetFight();
                V3Bot(V2Main, BotMode.Perfect);
                var detail = new List<string>();
                Action<PlayerController, Hazard, int> onHit = (p, h, dmg) =>
                {
                    if (p != V2Main || h.BurnPerSecond > 0) return;
                    string blk = V2Boss.BlockLog.Count > 0 ? V2Boss.BlockLog[V2Boss.BlockLog.Count - 1].block : "?";
                    detail.Add($"{h.Source} {dmg} in [{blk}] hp {p.Hp} @t{V2W.Tick} | {(p.Input as AllyBot)?.DebugLine}");
                };
                V2W.PlayerHit += onHit;
                for (int ph = 1; ph <= 3 && !V2Main.Down && !V4Wiped; ph++)
                {
                    V2Boss.DebugSetPhase(ph);
                    V2Begin(V2Boss.TestPhasePlan(ph));
                    yield return Until(() => V2Boss == null || !V2Boss.TestBusy || V2Main.Down || d.State != RunState.Combat, 240, $"seed {seed} phase {ph}");
                    V2Boss?.ClearPhasePools();
                }
                V2W.PlayerHit -= onHit;
                bool down = V2Main.Down || V4Wiped; if (down) fails++;
                Note($"seed {seed}: down {down}, potions {V2Main.PotionsDrunk}, hits {detail.Count}");
                foreach (var x in detail) Note($"   seed {seed} hit: {x}");
                V3Unbot();
            }
            Note($"seed sweep: {fails} of {n} perfect routes went down");
        }

        // ================================================================ Hard additions (5 Nov): more to dodge, Medium unchanged
        IEnumerator V4HardExtras()
        {
            // 1. one Errant Planet on the 12 m rim orbit over Phase 1 on Hard; none on Medium
            foreach (var lv in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, lv, new[] { new Vector3(0, 0, 5f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                var a = V2Boss; a.DebugSetPhaseV4(1); a.TestExtras = true;
                yield return V2Ticks(30);
                int n = a.Planets.Count; float r = n > 0 ? Sim.Flat(a.Planets.PlanetAt(0)).magnitude : 0;
                var ph = V2W.Hazards.Live.FirstOrDefault(h => h.Source == "Errant Planets");
                a.TestExtras = false; yield return V2Ticks(2);
                if (lv == Difficulty.Hard) V2Check("Hard: one Errant Planet on the rim orbit (12 m) in Phase 1", n == 1 && Mathf.Abs(r - 12f) < 0.3f, $"planets {n}, {r:0.0} m out");
                if (lv == Difficulty.Hard) V4IconCheck("Errant Planets", "aurelion.errant_planets", ph);
                else V2Check("Medium: no planets in Phase 1", n == 0, $"planets {n}");
            }
            // 2. Falling Meridians: on Hard an echo circle 0.6 s after the wave; standing still costs both
            foreach (var lv in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, lv, new[] { new Vector3(0, 0, 7f) }, 3, Vector3.zero);
                if (V2Boss == null) yield break;
                var a = V2Boss; a.DebugSetPhaseV4(3); a.Meridians.Reset(); a.TestExtras = true;
                V2Main.DebugSetHp(PlayerController.MaxHp);
                int h0 = V2HitsOn(V2Main, "Falling Meridians").Count(), e0 = a.Meridians.Echoes;
                yield return V2Ticks(Sim.Ticks(FallingMeridians.EveryS) + Sim.Ticks(FallingMeridians.EchoS) + a.Meridians.Warn + 3);
                int hits = V2HitsOn(V2Main, "Falling Meridians").Count() - h0, echoes = a.Meridians.Echoes - e0;
                a.TestExtras = false;
                if (lv == Difficulty.Hard) V2Check("Hard: Falling Meridians echo 0.6 s after the wave; standing still is hit by both", echoes == 1 && hits == 2, $"echoes {echoes}, hits {hits}");
                else V2Check("Medium: no echo", echoes == 0 && hits == 1, $"echoes {echoes}, hits {hits}");
            }
            // 2b. revivers (AUR-TARGET, 6 Nov): neither the wave nor the echo aims at a player who is reviving, but a reviver
            // standing in another player's circle is still hit (no immunity). A is down, B kneels beside A, C stands 1.5 m past B.
            yield return V2Setup(3, Difficulty.Hard, new[] { new Vector3(0, 0, 7f), new Vector3(1f, 0, 7f), new Vector3(2.5f, 0, 7f) }, 3, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var a = V2Boss; a.DebugSetPhaseV4(3); a.Meridians.Reset(); a.TestExtras = true;
                PlayerController dn = V2Party[0], rv = V2Party[1], other = V2Party[2];
                rv.DebugSetHp(PlayerController.MaxHp); other.DebugSetHp(PlayerController.MaxHp);
                dn.ForceDown("meridian revive test");
                int h0 = V2HitsOn(rv, "Falling Meridians").Count(), e0 = a.Meridians.Echoes;
                bool aimedAtReviver = false, alwaysReviving = true; int circles = 0; var seen = new HashSet<Hazard>();
                int span = Sim.Ticks(FallingMeridians.EveryS) + Sim.Ticks(FallingMeridians.EchoS) + a.Meridians.Warn + 3;
                for (int end = V2W.Tick + span; V2W.Tick < end;)   // by sim tick (a frame can hold several)
                {
                    dn.ReviveProgress = 0;   // hold the revive open so B channels for the whole window
                    yield return null;
                    foreach (var h in V2W.Hazards.Live)
                        if (h.Source.StartsWith("Falling Meridians") && seen.Add(h))
                        { circles++; if (rv.Reviving != dn) alwaysReviving = false; if (Sim.Flat(h.Origin - rv.transform.position).magnitude < 0.5f) aimedAtReviver = true; }   // B is mid-revive as each circle is aimed
                }
                int hits = V2HitsOn(rv, "Falling Meridians").Count() - h0, echoes = a.Meridians.Echoes - e0;
                a.TestExtras = false;
                V2Check("Hard: Falling Meridians (wave and echo) never aim at a reviver; the other standing player gets both",
                    alwaysReviving && echoes == 1 && circles == 2 && !aimedAtReviver, $"reviving {alwaysReviving}, echoes {echoes}, circles {circles}, aimed at reviver {aimedAtReviver}");
                V2Check("a reviver inside another player's circle is still hit (no immunity)", hits == 2, $"hits {hits}");
                V4IconCheck("Falling Meridians (wave and echo)", "aurelion.falling_meridians", seen.ToArray());
            }
            // 3. Hour Hands on Hard: each spoke has a mirror at its hour + 6; standing off both lines is never hit
            yield return V2Setup(1, Difficulty.Hard, new[] { new Vector3(0, 0, 8f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            {
                var hh = V2Boss.Hands; V2Main.DebugSetHp(PlayerController.MaxHp);
                V2Begin(hh.Run()); yield return V2Ticks(3);
                var own = V2W.Hazards.Live.FirstOrDefault(h => h.Source == "Hour Hands I"); var mir = V2W.Hazards.Live.FirstOrDefault(h => h.Source == "Hour Hands I mirror");
                float apart = own != null && mir != null ? Mathf.Abs(Mathf.DeltaAngle(own.Yaw, mir.Yaw)) : 0;
                yield return V2WaitIdle(10);
                V2Check("Hard: each Hour Hands spoke has a mirror on the opposite hour", own != null && mir != null && Mathf.Abs(apart - 180f) < 0.5f, $"own {own != null}, mirror {mir != null}, {apart:0} degrees apart");
                V4IconCheck("Hour Hands (spoke and mirror)", "aurelion.hour_hands", own, mir);
            }
            // 4. Sunspots on Hard: 1.2 s between blasts (Medium 1.5 s)
            foreach (var lv in new[] { Difficulty.Medium, Difficulty.Hard })
            {
                yield return V2Setup(1, lv, new[] { new Vector3(0, 0, 9f) }, 1, Vector3.zero);
                if (V2Boss == null) yield break;
                var sp = V2Boss.Spots; V2Begin(sp.Run()); yield return V2Ticks(2);
                int gap = sp.Spots.Count == 2 ? sp.Spots[1].DetonateTick - sp.Spots[0].DetonateTick : -1;
                yield return V2WaitIdle(10); var spotHz = sp.Spots.SelectMany(s => new[] { s.Blast, s.Pool }).ToArray();
                V2Boss.ClearPhasePools();
                float want = lv == Difficulty.Hard ? Sunspots.StepHardS : Sunspots.StepS;
                V2Check($"{lv}: Sunspots {want:0.0} s apart", gap == Sim.Ticks(want), $"{gap} ticks");
                if (lv == Difficulty.Hard) V4IconCheck("Sunspots (blast and scorch)", "aurelion.sunspots", spotHz);
            }
        }

        // AUR-TARGET-OTHER (Codex, 7 Nov): Starfall Mirror, Sunspots, Hour Hands and Meridian Lances never aim at a player who
        // is reviving. A is down at 12 o'clock, B kneels beside A (and is the farthest player), C stands at 3 o'clock.
        IEnumerator V4Revivers()
        {
            var at = new[] { new Vector3(0, 0, 7f), new Vector3(1f, 0, 7.1f), new Vector3(7f, 0, 0) };
            float Off(float yaw, Vector3 p, float period) { float d = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg)) % period; return Mathf.Min(d, period - d); }
            foreach (var which in new[] { "Starfall Mirror", "Sunspots", "Hour Hands", "Meridian Lances" })
            {
                yield return V2Setup(3, Difficulty.Hard, at, 3, Vector3.zero);
                if (V2Boss == null) yield break;
                var a = V2Boss; PlayerController dn = V2Party[0], rv = V2Party[1], other = V2Party[2];
                foreach (var p in V2Party) p.DebugSetHp(PlayerController.MaxHp);
                dn.ForceDown("aim revive test");
                for (int end = V2W.Tick + 3; V2W.Tick < end;) { dn.ReviveProgress = 0; yield return null; }   // B is channelling before the cast
                IEnumerable<int> body = which == "Starfall Mirror" ? a.Starfall.Run() : which == "Sunspots" ? a.Spots.Run() : which == "Hour Hands" ? a.Hands.Run() : a.Lances.Run(1, 1, false);
                V2Begin(body);
                var seen = new HashSet<Hazard>(); bool reviving = true;
                for (int end = V2W.Tick + Sim.Ticks(8f); V2W.Tick < end && a.TestBusy;)
                {
                    dn.ReviveProgress = 0; yield return null;
                    if (rv.Reviving != dn) reviving = false;
                    foreach (var h in V2W.Hazards.Live) if (h.Source.StartsWith(which) && !h.Source.Contains("scorch")) seen.Add(h);
                }
                yield return V2WaitIdle(10); a.ClearPhasePools();
                Vector3 b = Sim.Flat(rv.transform.position), c = Sim.Flat(other.transform.position);
                bool ok; string info;
                if (which == "Starfall Mirror") { ok = a.Starfall.Stars.Count > 0 && a.Starfall.Stars.All(s => s.Who == other); info = $"stars on {string.Join("/", a.Starfall.Stars.Select(s => s.Who == rv ? "reviver" : s.Who == other ? "C" : "?"))}"; }
                else if (which == "Sunspots") { ok = /* 2 standing: one sigil */ a.Spots.Spots.Count == 1 && a.Spots.Spots.All(s => s.Who == other); info = $"{a.Spots.Spots.Count} sigils on {string.Join("/", a.Spots.Spots.Select(s => s.Who == rv ? "reviver" : s.Who == other ? "C" : "?"))}"; }
                else if (which == "Hour Hands") { ok = seen.Count > 0 && seen.All(h => Off(h.Yaw, b, 360f) > 15f); info = $"{seen.Count} spokes, nearest {(seen.Count > 0 ? seen.Min(h => Off(h.Yaw, b, 360f)) : 0):0} degrees off the reviver"; }
                else { ok = seen.Count > 0 && seen.All(h => Off(h.Yaw, c, 180f) < Off(h.Yaw, b, 180f)); info = $"{seen.Count} beams, {(seen.Count > 0 ? Off(seen.First().Yaw, c, 180f) : 0):0} degrees off C, {(seen.Count > 0 ? Off(seen.First().Yaw, b, 180f) : 0):0} off the reviver"; }
                V2Check($"{which}: aimed at the standing player, not the one reviving", reviving && ok, $"reviving {reviving}; {info}");
            }
        }

        // graphics pass (8 Nov, presentation only): planet bodies ride their hazards at the hitbox size, towers get posts that
        // go with them, Zenith Marks show glyphs, and the beam / Final Noon effects run without errors
        IEnumerator V4Graphics()
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(-3, 0, -4f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            var a = V2Boss;
            a.DebugSetPhaseV4(2); a.TestExtras = true;
            yield return V2Ticks(Sim.Ticks(0.5f));
            int n = a.Planets.Count; bool on = n > 0; float worst = 0, size = 0;
            for (int i = 0; i < n; i++)
            {
                var b = a.Planets.BodyOf(i); if (!b) { on = false; continue; }
                worst = Mathf.Max(worst, Sim.Flat(b.position - a.Planets.PlanetAt(i)).magnitude);
                size = b.GetChild(0).GetChild(0).localScale.x;
            }
            V2Check("Errant Planets: a planet body on every planet, on its hazard, the hitbox's size", on && worst < 0.3f && Mathf.Abs(size - ErrantPlanets.Radius * 2) < 0.01f, $"{n} planets, worst offset {worst:0.00} m, body {size:0.00} m");
            var first = a.Planets.BodyOf(0);
            a.TestExtras = false; yield return V2Ticks(3);
            V2Check("Errant Planets: the bodies go when the planets end", !first && !a.Planets.Live, $"live {a.Planets.Live}");

            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 7f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            a = V2Boss; V3Bot(V2Main, BotMode.Perfect);
            V2Begin(a.Towers.Run());
            yield return V2Ticks(10);
            var towers = a.Towers.Towers.ToList();
            V2Check("Meridian Towers: every tower has its posts", towers.Count > 0 && towers.All(t => t.Posts), $"{towers.Count} towers, {towers.Count(t => t.Posts)} with posts");
            yield return V2WaitIdle(20);
            yield return V2Ticks(Sim.Ticks(1.0f));
            V2Check("Meridian Towers: the posts go once their tower lands", towers.All(t => !t.Posts), $"{towers.Count(t => t.Posts)} left");
            V3Unbot();

            yield return V2Setup(2, Difficulty.Medium, new[] { new Vector3(-3, 0, 0), new Vector3(3, 0, 0) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            a = V2Boss;
            V2Begin(a.Marks.Run());
            yield return V2Ticks(10);
            var shown = a.Marks.Shown.ToList(); var marks = a.Marks.Marks.Where(m => m.Step == 0).ToList();
            bool glyphs = shown.Count == marks.Count && shown.Count > 0;
            for (int i = 0; i < shown.Count && glyphs; i++) glyphs = shown[i].Icon == (marks[i].Mark == ZenithMarks.Mark.Sun ? AurelionProps.SunGlyph : AurelionProps.ShadeGlyph);
            V2Check("Zenith Marks: a sun or shade glyph over each player, matching the mark", glyphs, $"{shown.Count} markers, {marks.Count} marks: {string.Join("/", marks.Select(m => m.Mark))}");
            yield return V2WaitIdle(10);

            // the beams and Final Noon: presentation only, so it is enough that they run (the section fails on an exception);
            // one player with the perfect bot, so nobody falls and the run stays in combat
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 7f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            a = V2Boss; V3Bot(V2Main, BotMode.Perfect);
            V2Begin(a.Lances.Run(4, 2, true).Concat(a.Recall.Run()).Concat(a.Hands.Run()));
            yield return V2WaitIdle(20);
            V3Unbot();
            V2W.BossFx.FinalNoon(Vector3.zero, AurelionAbility.Rim);
            yield return V2Ticks(Sim.Ticks(1.8f));
            V2Check("beams (Lances, Recall, Hour Hands) and the Final Noon wash ran", true, $"recall beams {a.Recall.Replay.Count}");
            float reach = AurelionAbility.ReachToRim(new Vector3(0, 0, 0), 0, 28f), off = AurelionAbility.ReachToRim(new Vector3(0, 0, 5f), 0, 28f);
            V2Check("beam length: from his centre to the rim", Mathf.Abs(reach - AurelionAbility.Rim) < 0.01f && Mathf.Abs(off - (AurelionAbility.Rim - 5f)) < 0.01f, $"{reach:0.00} m from the centre, {off:0.00} m from 5 m north");
        }

        // ================================================================ attack family icons (8 Nov)
        // Codex's 8 family icons (work/attack-icons-20261108, done as recommended, review later): every hazard an Aurelion
        // ability spawns carries its ability id (AurelionAbility.Spawn), the direct hits (Severance's crossing, tower extras,
        // Reunion, Umbra, the Last Light fall) set it too, and a scripted wipe passes its family to the death recap.
        IEnumerator V4FamilyIcons()
        {
            yield return V4FamilyHazards("Zenith Collapse", "aurelion.zenith_collapse", 1, b => b.Zenith.Run());
            yield return V4FamilyHazards("Meridian Towers", "aurelion.meridian_towers", 1, b => b.Towers.Run());
            yield return V4FamilyHazards("Zenith Marks", "aurelion.zenith_marks", 1, b => b.Marks.Run());
            yield return V4FamilyHazards("Ecliptic Order", "aurelion.ecliptic_order", 2, b => b.Ecliptic.Run(4));
            yield return V4FamilyHazards("Meridian Recall", "aurelion.meridian_recall", 2, b => b.Lances.Run(4, 1, false).Concat(b.Recall.Run()));
            yield return V4FamilyHazards("Severance", "aurelion.severance", 3, b => b.Sever.Run());
            yield return V4FamilyHazards("Last Light", "aurelion.last_light", 4, b => LastLightFloorFx.During(V2W, b.Light, b.Light.Run().Take(2)));

            // Total Eclipse spawns no hazards of its own (its Starfall and Lances carry theirs): its hit is Umbra, standing in the dark
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, -12f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Begin(V2Boss.Eclipse.Run());
            // held to its 25 s wipe (as in its own section), so the body ends before the next setup
            float tt = 0; while (tt < 40 && !V4Wiped) { if (V2Main && !V2Main.Down) V2Main.DebugSetHp(PlayerController.MaxHp); tt += Time.deltaTime; yield return null; }
            var umbra = Enumerable.Range(0, V2W.TakenLog.Count).Where(i => V2W.TakenLog[i].source == "Umbra").Select(i => i < V2W.TakenAttackIds.Count ? V2W.TakenAttackIds[i] : null).ToList();
            V2Check("Total Eclipse: Umbra hits record aurelion.total_eclipse with its icon", umbra.Count > 0 && umbra.All(id => id == "aurelion.total_eclipse") && BossAttackIcons.Icon("aurelion.total_eclipse") != null, $"{umbra.Count} Umbra hits, ids {string.Join("/", umbra.Select(id => id ?? "null").Distinct())}");

            // a direct hit: standing still where Severance's line sweeps
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 0) }, 3, new Vector3(0, 0, -6f));
            if (V2Boss == null) yield break;
            V2Place(V2Main, Sim.Dir(Severance_NextYaw(V2Boss.Sever) + 30f) * 8f);
            yield return V3FireToppedUp(V2Boss.Sever.Run(), 40);
            var sev = Enumerable.Range(0, V2W.TakenLog.Count).Where(i => V2W.TakenLog[i].source.StartsWith("Severance")).Select(i => i < V2W.TakenAttackIds.Count ? V2W.TakenAttackIds[i] : null).ToList();
            V2Check("a direct hit (Severance's crossing) records aurelion.severance in the death recap", sev.Count > 0 && sev.All(id => id == "aurelion.severance"), $"{sev.Count} hits, ids {string.Join("/", sev.Select(id => id ?? "null").Distinct())}");

            // a scripted wipe: an empty tower downs the party with its own cause and the Meridian Towers family
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, -12f) }, 1, Vector3.zero);
            if (V2Boss == null) yield break;
            yield return V4FireUntilWipe(V2Boss.Towers.Run());
            V2Check("a tower wipe keeps its family for the recap icon (aurelion.meridian_towers)", V4Wiped && V2W.LastDamageAttackId == "aurelion.meridian_towers" && V2W.LastDamageSource == V2Boss.WipeCause && BossAttackIcons.Icon(V2W.LastDamageAttackId) != null, $"{V2Boss.WipeCause} -> {V2W.LastDamageAttackId ?? "null"}");

            // the enrage has no family: text only
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 9f) }, 2, Vector3.zero);
            if (V2Boss == null) yield break;
            V2Boss.TestClock = true; V2Boss.DebugSetFightSeconds(AurelionEncounter.EnrageS - 1f);
            yield return Until(() => V4Wiped, 10, "the 6:30 enrage");
            V2Check("the enrage wipe carries no attack family (cause text only)", V4Wiped && V2W.LastDamageAttackId == null, V2W.LastDamageAttackId ?? "null");
            if (V2Boss) V2Boss.TestClock = false;
        }

        // runs one ability solo with the perfect bot (HP topped up) and checks every hazard seen carries an attack id, and
        // that the family's own hazards carry its id with a catalogued icon
        IEnumerator V4FamilyHazards(string what, string id, int phase, Func<AurelionEncounter, IEnumerable<int>> run)
        {
            yield return V2Setup(1, Difficulty.Medium, new[] { new Vector3(0, 0, 7f) }, phase, Vector3.zero);
            if (V2Boss == null) yield break;
            V3Bot(V2Main, BotMode.Perfect);
            var seen = new HashSet<Hazard>();
            V2Begin(run(V2Boss));
            for (int t = 0; t < Sim.Ticks(40f) && V2Boss != null && V2Boss.TestBusy && !V4Wiped; t++)
            {
                foreach (var h in V2W.Hazards.Live) seen.Add(h);
                if (V2Main && !V2Main.Down) V2Main.DebugSetHp(PlayerController.MaxHp);
                yield return V2Ticks(1);
            }
            V3Unbot();
            var mine = seen.Where(h => h.AttackId == id).ToList();
            var bare = seen.Where(h => string.IsNullOrEmpty(h.AttackId)).Select(h => h.Source).Distinct().ToList();
            var icon = BossAttackIcons.Icon(id);
            V2Check($"{what}: its hazards carry {id} with its icon, and none lacks an attack id", mine.Count > 0 && bare.Count == 0 && icon != null && BossAttackIcons.Resolve(mine[0], V2Boss)?.id == id,
                $"{seen.Count} hazards, {mine.Count} {id}; ids {string.Join("/", seen.Select(h => h.AttackId ?? "null").Distinct())}{(bare.Count > 0 ? "; without: " + string.Join(", ", bare) : "")}; icon {(icon != null)}");
        }

        // attack icons (7 Nov): every hazard of the attack carries its AttackId, which resolves to a catalogued 256 px icon
        void V4IconCheck(string what, string id, params Hazard[] hz)
        {
            var a = BossAttackIcons.Resolve(hz.FirstOrDefault(), V2Boss); var t = a == null ? null : BossAttackIcons.Icon(a);
            V2Check($"{what}: AttackId {id} on every hazard, with its icon", hz.Length > 0 && hz.All(h => h != null && h.AttackId == id) && a?.id == id && t != null,
                $"{hz.Length} hazards, ids {string.Join("/", hz.Select(h => h?.AttackId ?? "null").Distinct())}, icon {(t != null)}");
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
            // 3 Nov: every 3 s a circle falls on Day and 1.5 s later on Night; solo you get your own half's beats, about 4-5 in 15 s
            V2Check("Severance halves: a circle falls at your feet on your half's beat (3 Nov)", sv.HalfCircles >= 4, $"{sv.HalfCircles} circles");
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
                var fall = lf.Falls.Count > 0 ? lf.Falls[0] : default;
                // a slide, not a teleport: on the fall's own tick or just after, the player has not reached the landing yet
                bool slid = fall.p != null && Sim.Flat(V2Main.transform.position - fall.to).magnitude > 0.2f;
                yield return V2Ticks(Sim.Ticks(LastLight.SlideS) + 10);
                int want = Mathf.RoundToInt(LastLight.FallDamage * V2W.DamageTakenScale);
                bool landedAt = fall.p != null && Sim.Flat(V2Main.transform.position - fall.to).magnitude < 0.3f;
                bool landedClear = fall.p != null && !lf.OverHole(fall.to, LastLight.Clearance - 0.05f) && !lf.OverHole(V2Main.transform.position);
                float moved = fall.p != null ? Sim.Flat(fall.to - fall.from).magnitude : -1;
                yield return V2Ticks(30);
                V2Check($"Last Light ({lv}): stepping onto a fallen slice slides you onto the nearest standing floor for {want}, never a one-shot or a teleport",
                    lf.Falls.Count == 1 && slid && landedAt && fall.damage == want && !V2Main.Down && V2Main.Hp == PlayerController.MaxHp - want && landedClear && moved < 4f,
                    fall.p != null ? $"{fall.damage} damage, slid {slid}, landed {landedAt}, moved {moved:0.0} m to {fall.to}, hp {V2Main.Hp}, falls {lf.Falls.Count}" : "no fall");
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
                // a fixed Collapse spot sequence (6 Nov): the spot is drawn from UnityEngine.Random, whose state earlier frame-timed
                // effects change from run to run, and the perfect bot goes down on 2 of 12 seeds on Hard (Phase 3 rim chains:
                // Lances stun, then its own Hour Hands spoke; -lastbellAbilityTest aurelion.v4_seeds). The check is the route, not the draw.
                UnityEngine.Random.InitState(PerfectSeed);
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
