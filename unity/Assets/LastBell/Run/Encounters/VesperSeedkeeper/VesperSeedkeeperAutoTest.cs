using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using R = LastBell.Run.VesperSeedkeeperRules;

namespace LastBell.Run
{
    // Acceptance run for the Vesper Seedkeeper inside the real game:  -vesperSeedkeeperTest -lastbellOut <folder>
    // Pure rules first (stakes, wave gaps and walks at 4.2 and 6 m/s, seed shares, planting, Dusk Tide escapes), then the
    // fight on the Verdant Chimes with the standard ally bots doing every job, at party sizes 2 and 4 (the player is driven
    // by an ally bot too), through both phases; then the melee/ranged danger check with players standing still, Easy and
    // Hard, and defeat. Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player DebugSetHp (kept at full so the
    // bots stay up and every mechanic can be counted), direct placement of the standing-still players.
    // Writes vesper-seedkeeper-report.txt plus screenshots and exits 0 / 1.
    public sealed class VesperSeedkeeperAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-vesperSeedkeeperTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "vesper-seedkeeper-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<VesperSeedkeeperAutoTest>()) return;
            var go = new GameObject("Vesper Seedkeeper autotest"); DontDestroyOnLoad(go); go.AddComponent<VesperSeedkeeperAutoTest>();
        }

        sealed class Idle : IInputSource { public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward }; }

        RunDirector d;
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<(int tick, Hazard h)> spawned = new List<(int, Hazard)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        VesperSeedkeeperEncounter boss;
        int enc, map;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; lines.Add("      LOG " + t + ": " + msg); } }

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[vesper-seedkeeper-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[vesper-seedkeeper-test] " + s); }
        void Section(string s) { lines.Add(""); lines.Add("== " + s); }

        void RenderShot(string name, Vector3 pos, Vector3 look, float fov, int w = 1280, int h = 720)
        {
            var go = new GameObject("capture camera"); var cam = go.AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.fieldOfView = fov; cam.aspect = w / (float)h; cam.orthographic = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 15), new Vector3(0, 0, -0.5f), 52);

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }
        void Keep()
        {
            if (d == null || d.World == null || !keepAlive) return;
            foreach (var p in d.World.Players) if (!p.Down && p.Hp < 100) p.DebugSetHp(100);
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void OnSpawn(Hazard h) => spawned.Add((d.World.Tick, h));
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));
        int Standing => d.World.Players.Count(p => !p.Down);

        IEnumerator Block(string atk, float timeout = 60f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, atk + " ends");
        }
        IEnumerator BetweenBlocks(float timeout = 60f) { yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, "between blocks"); }
        void CheckClean(string tag) =>
            Check($"{tag}: nothing left between blocks (hazards, blooms, carried marks, pending cords/seeds)",
                d.World.Hazards.Count == 0 && boss.Blooms.Count == 0 && boss.PendingCords == 0 && boss.PendingSeeds == 0 && d.World.Players.All(p => !p.Marks.Contains(MechanicIcons.CarriedMark)),
                $"hazards {d.World.Hazards.Count}, blooms {boss.Blooms.Count}, cords {boss.PendingCords}, seeds {boss.PendingSeeds}");

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            map = RunMaps.IndexOf(RunDirector.SylvaraMap); enc = RunDirector.VesperSeedkeeperPractice;
            R.Bound = map >= 0 ? RunMaps.All[map].Bound : 13.5f;
            Note($"Verdant Chimes player bound {R.Bound} m, boss bound {CombatWorld.BossBound} m");
            PureRules();

            Section("registration and practice start");
            Check("practice entry registered on the Verdant Chimes, practice only, 2 phases", enc >= 0 && RunDirector.Bosses[enc].Map == RunDirector.SylvaraMap && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Phases == 2);
            Check("Sylvara's practice lookup still finds Sylvara (first Verdant Chimes entry)", RunDirector.Bosses[RunDirector.SylvaraPractice].Name.StartsWith("Sylvara"));
            Check("run order unchanged (Vesper Seedkeeper excluded)", !RunDirector.RunOrder.Contains(enc));
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            // entries were added after the tutorial since (Rivet Cantor, the bonus fight), so only its presence is checked
            Check("tutorial entry still registered, practice only", RunDirector.TutorialPractice >= 0 && !RunDirector.Bosses[RunDirector.TutorialPractice].InRun);
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }

            foreach (int party in new[] { 2, 3, 4 }) yield return PartyFight(party);
            yield return DangerChecks();
            yield return DifficultyChecks();
            Finish();
        }

        IEnumerator StartFight(int party, Difficulty level, int seed)
        {
            d = RunDirector.Instance;
            if (d.State != RunState.Menu) { if (d.State != RunState.Results) d.SetPaused(true); d.ReturnToMenu(); yield return Until(() => d.State == RunState.Menu, 3, "menu"); }
            RunParty.Selected = party - 1;
            Check($"{party} players: practice request accepted", d.StartPractice("Tollbreaker", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check($"{party} players: Ready on the Verdant Chimes with {party - 1} ally bots", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc && d.Allies.Count == party - 1,
                d ? $"state {d.State}, map {RunMaps.Current}, allies {d.Allies.Count}" : "");
            // the player is a bot as well (the same Perfect ally bot the menu allies use)
            d.Player.Input = new AllyBot(party - 1, party, BotMode.Perfect);
            hits.Clear(); spawned.Clear();
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            d.World.Hazards.Spawned -= OnSpawn; d.World.Hazards.Spawned += OnSpawn;
            Check($"{party} players: Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is VesperSeedkeeperEncounter, 4, "boss spawned");
            boss = d.Boss as VesperSeedkeeperEncounter;
            boss.Seed = seed;
            d.World.Difficulty = level;
            VesperSeedkeeperHud.ShowDiagnostics = true;
            yield return Until(() => d.State == RunState.Combat, 8, "combat");
        }

        // ---------------- pure rules ----------------
        void PureRules()
        {
            bool spacing = true;
            for (int n = 1; n <= 4; n++)
                foreach (bool second in new[] { false, true })
                {
                    var a = R.StakeAngles(n, 17f, second);
                    if (a.Count != n) spacing = false;
                    for (int i = 0; i < n; i++) foreach (var s in R.StakeAngles(n, 17f, !second))
                        if (n > 1 && Mathf.Abs(Mathf.DeltaAngle(a[i], s)) < 180f / n - 0.1f) spacing = false;
                }
            Check("Vesper Cords: one stake per player (1-4), evenly spaced; wave 2 halfway between wave 1", spacing &&
                R.Cords(1) == 1 && R.Cords(2) == 2 && R.Cords(3) == 3 && R.Cords(4) == 4);
            foreach (float speed in new[] { R.SlowWalk, R.DesignWalk })
                for (int n = 1; n <= 4; n++)
                {
                    float worst = 0; for (float off = 0; off < 360; off += 15) worst = Mathf.Max(worst, R.WorstStakeWalk(n, off, speed));
                    Check($"Vesper Cords at {speed} m/s, {n} stakes: from anywhere on the floor any stake is reached in time (worst {worst:0.00} s of {VesperSeedkeeperEncounter.CordSec} s)", worst <= VesperSeedkeeperEncounter.CordSec);
                    float chord = 2f * R.StakeRing * Mathf.Sin(Mathf.Deg2Rad * 90f / n), walk = R.Reaction + Mathf.Max(0, chord - 2 * (R.StakeRadius - 0.45f)) / speed;
                    Check($"Vesper Cords wave gap at {speed} m/s, {n} stakes: the walk to the next stake ({walk:0.00} s) fits the gap ({R.WaveGapSeconds(n):0.00} s)", walk <= R.WaveGapSeconds(n));
                }
            Check("Vesper Cords: the lanes stop short of the stakes (a severer is never on a lane)",
                R.CordLane(Vector3.zero, R.StakeAt(0)).length + Hazard.Margin < R.StakeRing - (R.StakeRadius - 0.45f));
            Check("Seedgift: seeds 1/1/2/2 for 1-4 players; shares [1], [2], [2,1] (a pair and a lone carrier), [2,2]; 40 on a lone carrier, 80 split by a pair",
                new[] { 1, 2, 3, 4 }.Select(R.Seeds).SequenceEqual(new[] { 1, 1, 2, 2 }) &&
                R.Shares(1).SequenceEqual(new[] { 1 }) && R.Shares(2).SequenceEqual(new[] { 2 }) && R.Shares(3).SequenceEqual(new[] { 2, 1 }) && R.Shares(4).SequenceEqual(new[] { 2, 2 }) &&
                new[] { 1, 2, 3, 4 }.All(n => R.Shares(n).Length == R.Seeds(n) && R.Shares(n).Sum() == n) && R.SeedTotal(1) == 40 && R.SeedTotal(2) == 80);
            Check("Seedgift: one player taking every share survives from full (80 < 100)", R.SeedTotal(2) < PlayerController.MaxHp && VesperSeedkeeperEncounter.SeedWither < PlayerController.MaxHp);
            bool plant = true; string why = "";
            foreach (var b in new[] { Vector3.zero, new Vector3(3, 0, 0), new Vector3(-2, 0, 2), new Vector3(0, 0, -3) })
                for (float x = -14; x <= 14; x += 0.5f)
                    for (float z = -14; z <= 14; z += 0.5f)
                    {
                        var p = new Vector3(x, 0, z); if (p.magnitude > R.Bound) continue;
                        var s = R.PlantSpot(p, b); float fromBoss = Sim.Flat(s - b).magnitude;
                        if (fromBoss < R.BloomMinFromBoss - 0.01f || s.magnitude > R.Bound - R.BloomRadius - 0.29f || fromBoss - R.BloomRadius - Hazard.Margin <= R.UmbraRadius + Hazard.Margin)
                        { plant = false; if (why == "") why = $"boss {b}, landed {p} -> {s} ({fromBoss:0.00} m)"; }
                    }
            Check("Seedgift: a bloom is always planted inside the floor, 7.2 m+ from the vessel, never touching the umbra", plant, why);
            foreach (float speed in new[] { R.SlowWalk, R.DesignWalk })
                foreach (var lvl in new[] { 1f, 1.25f })
                    foreach (bool rev in new[] { false, true })
                    {
                        bool ok = true; string w = "";
                        foreach (var b in new[] { Vector3.zero, new Vector3(3, 0, 0), new Vector3(0, 0, -3), new Vector3(-2.1f, 0, 2.1f) })
                            if (!R.TideSurvivable(b, VesperSeedkeeperEncounter.TideFirstSec * lvl, VesperSeedkeeperEncounter.TideSecondSec * lvl, rev, speed, out var ww)) { ok = false; w = $"boss {b}: {ww}"; }
                        Check($"Dusk Tide{(rev ? " (reversed)" : "")} at {speed} m/s{(lvl > 1 ? ", Easy" : "")}: both hits walkable from every start", ok, w);
                    }
            float lateWalk = R.Reaction + (R.LateBloomRadius + Hazard.Margin + 0.05f) / R.SlowWalk;
            Check($"Hard Late Bloom: walking out from a bloom's centre at 4.2 m/s ({lateWalk:0.00} s) fits the warning ({VesperSeedkeeperEncounter.LateBloomSec} s)", lateWalk <= VesperSeedkeeperEncounter.LateBloomSec);
            Check("rotation: Tide (the punish window) every other block in both phases",
                VesperSeedkeeperEncounter.Rotation(false).SequenceEqual(new[] { "Cords", "Tide", "Seeds", "Tide" }) &&
                VesperSeedkeeperEncounter.Rotation(true).SequenceEqual(new[] { "Seeds", "Cords", "Tide", "Tide" }));
        }

        // ---------------- the fight with bots ----------------
        IEnumerator PartyFight(int party)
        {
            Section($"fight with bots: {party} players");
            yield return StartFight(party, Difficulty.Medium, 20261103 + party);
            if (!boss) yield break;
            string tag = $"{party}p";
            Check($"{tag}: opens in phase 1, no ability text", !boss.Phase2 && boss.PhaseName == "" && boss.Lesson == "" && boss.MaxHp == 3000);
            if (party == 2)
            {
                if (boss.Presenter && boss.Presenter.HasModel)
                {
                    var missing = VesperSeedkeeperEncounter.ClipNames.Where(c => !boss.Presenter.Has(c)).ToList();
                    Check("the rigged model is loaded with all 11 clips", !boss.Placeholder && missing.Count == 0, missing.Count > 0 ? "missing: " + string.Join(", ", missing) : "");
                }
                else Check("no rigged model in this build: the labelled stand-in is used (Placeholder)", boss.Placeholder && GameObject.Find("Vesper Seedkeeper (STAND-IN, labelled placeholder)"));
            }
            Check($"{tag}: party of {party} standing", Standing == party, $"{Standing}");
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "drifts to the centre");
            if (party == 4) RenderShot("model_front_placeholder", new Vector3(0, 3.6f, 9.5f), new Vector3(0, 2.2f, 0), 40);
            yield return BetweenBlocks();

            // ---- Vesper Cords ----
            int t0 = d.World.Tick, c0 = boss.CordLog.Count, sev0 = boss.CordsSevered;
            boss.ForcedNext = "Cords";
            yield return Until(() => boss.PendingCords > 0, 20, "cords out");
            yield return Wait(4.5f); Overhead($"{tag}_cords");
            yield return BetweenBlocks();
            var cords = boss.CordLog.Skip(c0).ToList();
            Check($"{tag} Vesper Cords: {party} stakes and cords, all resolved", cords.Count == 1 && cords[0].stakes == party, string.Join(",", cords.Select(c => $"w{c.wave}:{c.severed}/{c.stakes}")));
            Check($"{tag} Vesper Cords: the bots severed every stake (no surge)", boss.CordsSevered - sev0 == party && HitsSince(t0, "Vesper Cords (surge)") == 0,
                $"severed {boss.CordsSevered - sev0}/{party}, surge hits {HitsSince(t0, "Vesper Cords (surge)")}, lane hits {HitsSince(t0, "Vesper Cords (snap)")}");
            Check($"{tag} Vesper Cords: every warning has the raid look and a stake numeral 1", spawned.Where(s => s.tick >= t0 && s.h.Source.StartsWith("Vesper Cords")).All(s => s.h.Tele != null) &&
                spawned.Count(s => s.tick >= t0 && s.h.Source == "Vesper Cords (stake)" && s.h.Soak && s.h.ShareCount == 1) == party);
            CheckClean($"{tag} Vesper Cords");
            if (party == 2) yield return StakeBreakCheck(tag);

            // ---- Dusk Tide ----
            yield return TideBlock(tag, false);

            // ---- Seedgift + Nightfall ----
            yield return SeedBlock(tag, party);

            // ---- the 50% shift ----
            int shifts0 = boss.BlockLog.Count(s => s.EndsWith(" Shift"));
            boss.DebugSetHpFraction(0.45f);
            yield return Until(() => boss.Phase2, 40, "phase 2 shift");
            int hpShift = boss.HpAtShiftStart;
            Check($"{tag} 50% shift: waits for the block, 2 s harmless, no heal", boss.ShiftEnd - boss.ShiftStart == Sim.Ticks(VesperSeedkeeperEncounter.ShiftSec) && boss.Hp <= hpShift &&
                boss.BlockLog.Count(s => s.EndsWith(" Shift")) == shifts0 + 1 && boss.PhaseName == "",
                $"{boss.ShiftEnd - boss.ShiftStart} ticks, hp {hpShift} -> {boss.Hp}");
            yield return BetweenBlocks();

            // ---- phase 2 ----
            yield return SeedBlock(tag + " P2", party);
            t0 = d.World.Tick; c0 = boss.CordLog.Count; sev0 = boss.CordsSevered;
            boss.ForcedNext = "Cords";
            yield return Until(() => boss.PendingCords > 0, 20, "cords out (P2)");
            Check($"{tag} P2 Vesper Cords: two waves ({party * 2} stakes)", boss.PendingCords == party * 2, $"{boss.PendingCords}");
            yield return Wait(4.0f); if (party == 4) Overhead($"{tag}_cords_two_waves");
            yield return BetweenBlocks();
            cords = boss.CordLog.Skip(c0).ToList();
            var gap = boss.CordWaveGaps.LastOrDefault();
            Check($"{tag} P2 Vesper Cords: wave 2 snaps a walk after wave 1, bots sever both", cords.Count == 2 && cords.All(c => c.stakes == party) && boss.CordsSevered - sev0 == party * 2 &&
                gap.end - gap.start == Mathf.Max(1, Sim.Ticks(R.WaveGapSeconds(party))),
                string.Join(",", cords.Select(c => $"w{c.wave}:{c.severed}/{c.stakes}")) + $", gap {gap.end - gap.start} ticks");
            CheckClean($"{tag} P2 Vesper Cords");
            yield return TideBlock(tag + " P2", false);
            yield return TideBlock(tag + " P2", true);

            Check($"{tag}: no player went down (bots did every job)", d.World.Players.All(p => !p.Down));
            Note($"{tag} hits by source: " + string.Join(", ", hits.GroupBy(h => h.source).Select(g => $"{g.Key} {g.Count()}")));
            yield return DefeatChecks(tag);
        }

        IEnumerator TideBlock(string tag, bool expectReversed)
        {
            int t0 = d.World.Tick, l0 = boss.TideLog.Count, p0 = boss.PunishLog.Count;
            boss.ForcedNext = "Tide";
            yield return Until(() => boss.TideLog.Count > l0, 20, "tide");
            if (boss.TideLog.Count > l0)
            {
                var tide = boss.TideLog[l0];
                Check($"{tag} Dusk Tide: {(expectReversed ? "reversed (flow first)" : "ebb first")}, vessel within {R.TideDrift} m of the centre",
                    tide.reversed == expectReversed && tide.at.magnitude <= R.TideDrift + 0.05f, $"reversed {tide.reversed}, at {tide.at}");
                if (tag.StartsWith("4p") && !expectReversed && !tag.Contains("P2")) { yield return Wait(1.2f); Overhead($"{tag}_tide"); }
            }
            yield return Until(() => boss.Closed, 15, "punish window");
            bool quiet = true;
            while (boss.Closed) { if (d.World.Hazards.Count > 0) quiet = false; Keep(); yield return null; }
            (int start, int end) w = boss.PunishLog.Count > p0 ? boss.PunishLog[p0] : (0, 0);
            Check($"{tag} Dusk Tide: the 3.5 s punish window follows with nothing live", quiet && w.end - w.start == Sim.Ticks(VesperSeedkeeperEncounter.PunishSec), $"{w.end - w.start} ticks, quiet {quiet}");
            yield return BetweenBlocks();
            Check($"{tag} Dusk Tide: the bots walked in, then out (no tide hits)", HitsSince(t0, "Dusk Tide") == 0, $"{HitsSince(t0, "Dusk Tide")} hits");
            CheckClean($"{tag} Dusk Tide");
        }

        IEnumerator SeedBlock(string tag, int party)
        {
            int t0 = d.World.Tick, s0 = boss.SeedLog.Count, n0 = boss.NightLog.Count;
            boss.ForcedNext = "Seeds";
            yield return Until(() => boss.PendingSeeds > 0, 20, "seeds given");
            int carriers = boss.Carriers.Count;
            Check($"{tag} Seedgift: {R.Seeds(party)} seed(s), carriers marked", carriers == R.Seeds(party) && boss.Carriers.All(c => c.Marks.Contains(MechanicIcons.CarriedMark)), $"{carriers} carriers");
            yield return Wait(2.5f);
            foreach (var p in d.World.Players) if (p.Input is AllyBot ab) Note($"{tag} Seedgift +2.5 s {p.ClassName}{(boss.Carriers.Contains(p) ? " (carrier)" : "")}: {ab.DebugLine}");
            if (party == 4 && !tag.Contains("P2")) { yield return Wait(1.7f); Overhead($"{tag}_seedgift_stacks"); }
            yield return Until(() => boss.NightActive, 15, "nightfall");
            if (party == 4 && !tag.Contains("P2")) { yield return Wait(3.0f); Overhead($"{tag}_nightfall"); yield return Shot($"{tag}_nightfall_game_camera"); }
            yield return BetweenBlocks();
            var seeds = boss.SeedLog.Skip(s0).ToList();
            var want = R.Shares(party).OrderBy(x => x).ToArray();
            Check($"{tag} Seedgift: every seed planted by its full share ({string.Join("+", R.Shares(party))}), {(boss.Phase2 ? "Nightfall at once" : "Nightfall after")}",
                seeds.Count == R.Seeds(party) && seeds.Select(s => s.inside).OrderBy(x => x).SequenceEqual(want) && boss.SeedsWithered == 0,
                string.Join(", ", seeds.Select(s => $"{s.inside} inside, {s.share} each")));
            Check($"{tag} Seedgift: blooms planted 7.2 m+ from the vessel", seeds.All(s => float.IsInfinity(s.planted.x) || Sim.Flat(s.planted - boss.transform.position).magnitude >= R.BloomMinFromBoss - 0.6f));
            var night = boss.NightLog.Skip(n0).ToList();
            Check($"{tag} Nightfall: all {party} in light, none in the dark, no bloom guttered", night.Count == 1 && night[0].lit == party && night[0].dark == 0 && night[0].guttered == 0,
                night.Count > 0 ? $"blooms {night[0].blooms}, lit {night[0].lit}, dark {night[0].dark}, guttered {night[0].guttered}" : "no nightfall");
            Check($"{tag} Nightfall: the bots kept out of the umbra", HitsSince(t0, "Nightfall (umbra)") == 0);
            Check($"{tag} Nightfall: bloom soaks with their numerals spawned ({string.Join("+", R.Shares(party))})",
                spawned.Where(s => s.tick >= t0 && s.h.Source == "Nightfall (bloom)" && s.h.Soak).Select(s => s.h.ShareCount).OrderBy(x => x).SequenceEqual(want));
            CheckClean($"{tag} Seedgift + Nightfall");
        }

        // a stake can also be broken early by attacking it (a boss part; its hits count against the boss too)
        IEnumerator StakeBreakCheck(string tag)
        {
            int t0 = d.World.Tick, c0 = boss.CordLog.Count, br0 = boss.CordsBroken, sev0 = boss.CordsSevered;
            boss.ForcedNext = "Cords";
            yield return Until(() => boss.PendingCords > 0, 20, "cords out (break)");
            yield return Wait(0.5f);
            var parts = boss.LiveStakeParts();
            var part = parts.FirstOrDefault();
            int hp0 = boss.Hp, dealt = 0, lanes0 = d.World.Hazards.Live.Count(h => h.Source == "Vesper Cords (snap)");
            if (part != null)
            {
                // a real strike (the shared melee test every kit uses): from 1.2 m outside the stake, facing it
                var dir = Sim.Flat(part.Pos).normalized; var from = part.Pos + dir * 1.2f;
                Check($"{tag} stake break: the stake is a live boss part in the world", d.World.Parts.Contains(part) && part.Alive);
                for (int i = 0; i < 6 && boss.LiveStakeParts().Contains(part); i++) dealt += d.World.StrikeBoss(d.Player, from, -dir, 2f, 90f, 25, "test strike");
            }
            yield return Wait(0.15f);
            int lanes1 = d.World.Hazards.Live.Count(h => h.Source == "Vesper Cords (snap)");
            Check($"{tag} stake break: {VesperSeedkeeperEncounter.StakeHp} damage breaks it; its lane and stake are withdrawn; the hits count against the boss",
                part != null && boss.CordsBroken == br0 + 1 && !part.Alive && !d.World.Parts.Contains(part) && dealt == VesperSeedkeeperEncounter.StakeHp &&
                lanes1 == lanes0 - 1 && hp0 - boss.Hp == VesperSeedkeeperEncounter.StakeHp,
                $"dealt {dealt}, broken {boss.CordsBroken - br0}, lanes {lanes0}->{lanes1}, boss hp {hp0}->{boss.Hp}");
            yield return BetweenBlocks();
            var cords = boss.CordLog.Skip(c0).ToList();
            Check($"{tag} stake break: the other stake is still severed by the bots, no surge", cords.Count == 1 && cords[0].severed == cords[0].stakes && boss.CordsSevered - sev0 == cords[0].stakes &&
                HitsSince(t0, "Vesper Cords (surge)") == 0, string.Join(",", cords.Select(c => $"{c.severed}/{c.stakes}")));
            CheckClean($"{tag} stake break");
        }

        IEnumerator DefeatChecks(string tag)
        {
            // defeat mid-mechanic: everything is gone in the killing tick
            boss.ForcedNext = "Seeds";
            yield return Until(() => boss.PendingSeeds > 0, 20, "seeds for defeat");
            boss.DebugDefeat();
            yield return null;
            Check($"{tag} defeat mid-Seedgift: hazards, props and carried marks gone in the killing tick", boss.Dead && d.World.Hazards.Count == 0 && boss.PendingSeeds == 0 &&
                d.World.Players.All(p => !p.Marks.Contains(MechanicIcons.CarriedMark)) && !GameObject.Find("Seed of light (stand-in)"));
            yield return Until(() => d.State == RunState.Results, 8, tag + " results");
            Check($"{tag}: practice ends in victory", d.State == RunState.Results && d.Victory);
        }

        // ---------------- melee and ranged both in danger (standing still) ----------------
        IEnumerator DangerChecks()
        {
            Section("melee and ranged danger (two players standing still)");
            yield return StartFight(1, Difficulty.Medium, 7);
            if (!boss) yield break;
            d.Player.Input = new Idle();
            var far = PlayerController.Spawn(d.World, "Arcanist", new Vector3(-12.9f, 0, 0), 90); far.Input = new Idle(); far.AbilitiesEnabled = false; extras.Add(far);
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "centre");
            yield return BetweenBlocks();
            void PlaceBoth() { d.Player.transform.position = d.World.Constrain(Sim.Flat(boss.transform.position) + new Vector3(2.6f, 0, 0.6f), PlayerController.Radius); far.transform.position = d.World.Constrain(new Vector3(-12.9f, 0, 0.3f), PlayerController.Radius); }
            var near = d.Player;
            foreach (var (atk, prefix, running) in new[] { ("Cords", "Vesper Cords", "Vesper Cords"), ("Tide", "Dusk Tide", "Dusk Tide"), ("Seeds", "", "Seedgift") })
            {
                PlaceBoth();
                int t0 = d.World.Tick; int b0 = boss.BlockLog.Count;
                boss.ForcedNext = atk;
                yield return Until(() => boss.BlockLog.Skip(b0).Any(s => s.EndsWith(" " + atk)), 20, atk + " (standing still)");
                yield return Until(() => boss.Current.StartsWith(running), 10, atk + " cast"); PlaceBoth();   // after the vessel's drift
                yield return BetweenBlocks();
                if (atk == "Seeds")
                {
                    Check("standing still: the far player carries the seed and takes it alone", HitsSince(t0, "Seedgift", far) == 1);
                    Check("standing still: the player next to the vessel is caught by the umbra", HitsSince(t0, "Nightfall (umbra)", near) == 1);
                }
                else
                    Check($"standing still: {prefix} hits both the player next to the vessel and the one at the edge", HitsSince(t0, prefix, near) >= 1 && HitsSince(t0, prefix, far) >= 1,
                        $"near {HitsSince(t0, prefix, near)}, far {HitsSince(t0, prefix, far)}");
            }
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
            boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "danger results");
        }

        // ---------------- Easy and Hard ----------------
        IEnumerator DifficultyChecks()
        {
            Section("difficulty");
            yield return StartFight(2, Difficulty.Easy, 11);
            if (boss)
            {
                yield return BetweenBlocks();
                int t0 = d.World.Tick; boss.ForcedNext = "Cords";
                yield return Until(() => boss.PendingCords > 0, 20, "easy cords");
                var stake = spawned.LastOrDefault(s => s.tick >= t0 && s.h.Source == "Vesper Cords (stake)").h;
                Check("Easy: warnings ×1.25 (cords snap after 8.75 s)", stake != null && stake.ResolveAt == Sim.Ticks(VesperSeedkeeperEncounter.CordSec * 1.25f), stake != null ? $"{stake.ResolveAt} ticks" : "none");
                yield return BetweenBlocks();
                boss.DebugDefeat(); yield return Until(() => d.State == RunState.Results, 8, "easy results");
            }
            yield return StartFight(2, Difficulty.Hard, 12);
            if (boss)
            {
                yield return BetweenBlocks();
                int t0 = d.World.Tick; boss.ForcedNext = "Seeds";
                yield return Until(() => boss.NightLog.Count > 0, 30, "hard nightfall");
                yield return Until(() => spawned.Any(s => s.tick >= t0 && s.h.Source == "Nightfall (Late Bloom)"), 4, "late bloom");
                Overhead("hard_late_bloom");
                yield return BetweenBlocks();
                Check("Hard: Late Bloom bursts every bloom after Nightfall; the bots step out", spawned.Count(s => s.tick >= t0 && s.h.Source == "Nightfall (Late Bloom)") == R.Seeds(2) && HitsSince(t0, "Nightfall (Late Bloom)") == 0,
                    $"{spawned.Count(s => s.tick >= t0 && s.h.Source == "Nightfall (Late Bloom)")} bursts, {HitsSince(t0, "Nightfall (Late Bloom)")} hits");
                CheckClean("Hard Seedgift + Nightfall");
                boss.DebugDefeat(); yield return Until(() => d.State == RunState.Results, 8, "hard results");
            }
            if (d.State != RunState.Menu) { d.ReturnToMenu(); yield return Until(() => d.State == RunState.Menu, 3, "menu at the end"); }
            RunParty.Selected = 0;
        }

        void Finish()
        {
            Check("no runtime errors", errors == 0, errors + " logged");
            lines.Insert(0, $"Vesper Seedkeeper autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "vesper-seedkeeper-report.txt"), lines);
            Debug.Log($"[vesper-seedkeeper-test] done: {pass} passed, {fail} failed");
            StartCoroutine(Quit());
        }
        IEnumerator Quit()
        {
            yield return new WaitForSeconds(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }
}
