using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using R = LastBell.Run.CrucibleSkaterRules;

namespace LastBell.Run
{
    // Acceptance run for the Crucible Skater inside the real game:  -crucibleSkaterTest -lastbellOut <folder>
    // Pure rules first (every escape at 4.2 and 6 m/s after a 0.3 s reaction on every level, leg orders, pour counts,
    // the rotation), then the fight on Brass Foundry with a bot party of 2 and then 4 (the player is a Perfect ally bot
    // too; bots send ordinary PlayerInput and see only the telegraphs). Each mechanic is forced once per phase and must
    // fire and resolve; one natural Phase 1 loop runs unforced; a standing-still pass (party of 2) proves melee and range
    // are both hit. Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player DebugSetHp (hits are kept from
    // downing anyone), direct player placement. Writes crucible-skater-report.txt plus screenshots and exits 0 / 1.
    public sealed class CrucibleSkaterAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-crucibleSkaterTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "crucible-skater-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<CrucibleSkaterAutoTest>()) return;
            var go = new GameObject("Crucible Skater autotest"); DontDestroyOnLoad(go); go.AddComponent<CrucibleSkaterAutoTest>();
        }

        sealed class Still : IInputSource { public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward }; }

        RunDirector d;
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<(int tick, Hazard h)> spawned = new List<(int, Hazard)>();
        string dir; bool keepAlive = true; int party;
        CrucibleSkaterEncounter boss;

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
            var line = $"{(ok ? "PASS" : "FAIL")}  {(party > 0 ? $"[{party}p] " : "")}{name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[crucible-skater-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[crucible-skater-test] " + s); }
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
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{party}p_{name}.png"), tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 15), new Vector3(0, 0, -0.5f), 52);
        IEnumerator Game(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{party}p_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }

        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s (boss: {(boss ? boss.Current : "-")})");
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 100) p.DebugSetHp(100);
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void OnSpawn(Hazard h) => spawned.Add((d.World.Tick, h));
        int HitsSince(int tick, string source, PlayerController who = null) => hits.Count(x => x.tick >= tick && x.source == source && (who == null || x.who == who));
        void Place(PlayerController p, Vector3 at) => p.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius);

        IEnumerator Block(string atk, float timeout = 60f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)) || boss.Dead, timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead || d.State != RunState.Combat, timeout, atk + " ends");
        }
        IEnumerator StartOf(string atk, float timeout = 40f)
        {
            int before = boss.BlockLog.Count; boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
        }
        IEnumerator BetweenBlocks(float timeout = 60f) { yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, "between blocks"); }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            int map = RunMaps.IndexOf(RunDirector.CrucivarMap);
            R.Bound = map >= 0 ? RunMaps.All[map].Bound : 13.7f;
            Note($"Brass Foundry player bound {R.Bound} m, boss bound {R.BossBound} m");
            Pure();

            Section("registration");
            int enc = RunDirector.CrucibleSkaterPractice;
            Check("practice entry registered on Brass Foundry, practice only, 2 phases", enc >= 0 && RunDirector.Bosses[enc].Map == RunDirector.CrucivarMap && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Phases == 2);
            Check("run order unchanged (4 run bosses, Crucible Skater excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("listed in the Minibosses row of the practice menu", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc) && LastBell.Menu.TestRushSetup.CardName(enc) == "Crucible Skater");
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null && map >= 0) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);

            foreach (int n in new[] { 2, 4 })
            {
                party = n;
                yield return PartyFight(n, enc, map);
                if (fail > 40) break;
            }
            party = 0;
            Finish();
        }

        void Pure()
        {
            foreach (var lvl in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                foreach (float sp in R.Speeds)
                {
                    string tag = $"{lvl} at {sp} m/s";
                    float have, need;
                    Check($"Figure Carve: out of a locked lane before the body arrives ({tag})", R.CarveEscape(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Body Check: the quarry steps out of the charge lane ({tag})", R.CheckEscape(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Body Check: a ranged blocker reaches the ring ({tag})", R.BlockReach(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Crucible Toss: from anywhere into the farther pour ({tag})", R.PourReach(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Crucible Toss solo: from the first pour into the second ({tag})", R.PourRelay(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Rink Lap: from the wall inside the band ({tag})", R.LapEscape(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    Check($"Pirouette: from the centre (or a late pour) out of the circle ({tag})", R.PirouetteEscape(lvl, sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                    if (lvl == Difficulty.Hard) Check($"Retrace: out of a reversed segment ({sp} m/s)", R.RetraceEscape(sp, out have, out need), $"{have:0.00} of {need:0.00} m");
                }
            Check("Toss + Lap: the pours sit wholly inside the band's safe disc", R.PoursInsideBand());
            Check("Figure Carve leg orders: solo 3 (4), 2p I-II-I (I-II-I-II), 3p I-II-III (+I), 4p I-IV (+I)",
                R.CarveOrder(1, false).SequenceEqual(new[] { 0, 0, 0 }) && R.CarveOrder(1, true).Length == 4 &&
                R.CarveOrder(2, false).SequenceEqual(new[] { 0, 1, 0 }) && R.CarveOrder(2, true).SequenceEqual(new[] { 0, 1, 0, 1 }) &&
                R.CarveOrder(3, false).SequenceEqual(new[] { 0, 1, 2 }) && R.CarveOrder(3, true).SequenceEqual(new[] { 0, 1, 2, 0 }) &&
                R.CarveOrder(4, false).SequenceEqual(new[] { 0, 1, 2, 3 }) && R.CarveOrder(4, true).SequenceEqual(new[] { 0, 1, 2, 3, 0 }));
            Check("Crucible Toss counts: solo 1 then 1, 2p 1+1, 3p 2+1, 4p 2+2; one soaker survives the whole pour from full",
                R.PourCounts(1).SequenceEqual(new[] { 1, 1 }) && R.PoursInSequence(1) && R.PourCounts(2).SequenceEqual(new[] { 1, 1 }) && !R.PoursInSequence(2) &&
                R.PourCounts(3).SequenceEqual(new[] { 2, 1 }) && R.PourCounts(4).SequenceEqual(new[] { 2, 2 }) && R.PourTotal * 1.3f < 100 && R.BlockTotal * 1.3f < 100);
            Check("Body Check blockers: 1 (2 at four players)", R.BlockersNeeded(1) == 1 && R.BlockersNeeded(2) == 1 && R.BlockersNeeded(3) == 1 && R.BlockersNeeded(4) == 2);
            Check("no single hit downs a full-HP player on Hard", new[] { R.CarveDmg, R.ChargeDmg, R.LapDmg, R.PirouetteDmg }.All(x => x * 1.3f < 100));
            bool stops = true;
            for (float a = 0; a < 360; a += 15) foreach (var from in new[] { Vector3.zero, new Vector3(5, 0, 2), Sim.Dir(a + 90) * 9.4f })
                {
                    var s = R.LegStop(from, Sim.Dir(a) * 13f, Vector3.forward);
                    if (Sim.Flat(s).magnitude > R.BossBound + 0.01f) stops = false;
                }
            Check("every leg ends inside the boss bound", stops);
            Check("rotation: Carve, Check, Toss, Lap above 50%; Carve, Turns, Toss+Lap below",
                CrucibleSkaterEncounter.Rotation(false).SequenceEqual(new[] { "Carve", "Check", "Toss", "Lap" }) && CrucibleSkaterEncounter.Rotation(true).SequenceEqual(new[] { "Carve", "Turns", "TossLap" }));
        }

        // ---------------- one party ----------------
        IEnumerator PartyFight(int n, int enc, int map)
        {
            Section($"party of {n} (the player and {n - 1} ally bot{(n > 2 ? "s" : "")}, all Perfect bots)");
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 40) { waitMenu += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            if (d == null) { Check("run scene loads", false, "no RunDirector in Menu after 40 s"); yield break; }
            Check("allies selected", d.SelectAllies(n - 1) && RunParty.Selected == n - 1);
            Check("practice request accepted", d.StartPractice("Tollbreaker", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 40) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Brass Foundry", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc && d.World.Players.Count == n,
                d ? $"{d.World?.Players.Count} players" : "");
            if (!d || d.State != RunState.Ready) yield break;
            d.Player.Input = new AllyBot(n - 1, n, BotMode.Perfect);
            hits.Clear(); spawned.Clear();
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            d.World.Hazards.Spawned -= OnSpawn; d.World.Hazards.Spawned += OnSpawn;
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is CrucibleSkaterEncounter, 4, "boss spawned");
            boss = d.Boss as CrucibleSkaterEncounter;
            if (!boss) yield break;
            boss.Seed = 20261103 + n;
            d.World.Difficulty = Difficulty.Medium;
            yield return Until(() => d.State == RunState.Combat, 8, "combat");
            Check("fight opens in phase 1 at full HP, placeholder stand-in, no ability text", !boss.Phase2 && boss.Hp == boss.MaxHp && boss.MaxHp == CrucibleSkaterEncounter.MaxHpMedium && boss.Placeholder && boss.Lesson == "" && boss.PhaseName == "" && boss.transform.Find("Crucible Skater (STAND-IN placeholder)"));
            if (n == 2) RenderShot("stand_in", boss.transform.position + new Vector3(0, 2.2f, 5.5f), boss.transform.position + Vector3.up * 1.1f, 40);

            var steps = new List<Func<IEnumerator>> { () => CarveChecks(n, false), () => CheckChecks(n, 1), () => TossChecks(n, false), () => LapChecks(n, false), () => NaturalLoop(n) };
            if (n == 2) steps.Add(StillChecks);
            steps.Add(() => PhaseTwo(n)); steps.Add(() => HardChecks(n)); steps.Add(LifetimeAndStyle); steps.Add(DefeatChecks);
            foreach (var step in steps)
            {
                if (d.State != RunState.Combat)
                {
                    Check("the party is still fighting (no wipe)", false, $"state {d.State}, cause {d.DefeatCause}");
                    yield return Until(() => d.State == RunState.Results, 15, "results after the wipe");
                    break;
                }
                yield return step();
            }
            Note($"bot hits taken this party: {string.Join(", ", hits.GroupBy(x => x.source).Select(g => $"{g.Key} {g.Count()}"))}");
            if (d.State == RunState.Results) d.ReturnToMenu();
        }

        // ---------------- 1. Figure Carve ----------------
        IEnumerator CarveChecks(int n, bool p2)
        {
            int legs0 = boss.LegLog.Count, trails0 = boss.TrailsSpawned, tick0 = d.World.Tick;
            yield return StartOf("Carve");
            yield return Until(() => boss.Current.StartsWith("Figure Carve"), 5, "Figure Carve begins");
            yield return null; yield return null;
            int markedNow = d.World.Players.Count(p => p.Marks.Any(m => m.StartsWith("order_")));
            var lanes = d.World.Hazards.Live.Where(h => h.Source == "Figure Carve").ToList();
            int want = R.CarveOrder(n, p2).Length;
            Check($"Figure Carve{(p2 ? " (phase 2)" : "")}: every player numbered, {want} lanes shown at once", markedNow == n && lanes.Count == want, $"{markedNow} numbered, {lanes.Count} lanes");
            yield return Until(() => lanes.All(h => h.Locked), 4, "lanes lock");
            Overhead(p2 ? "carve_p2" : "carve");
            yield return BetweenBlocks(30);
            var legs = boss.LegLog.Skip(legs0).ToList();
            int crouch = R.Warn(R.CarveCrouch, Difficulty.Medium);
            Check($"Figure Carve{(p2 ? " (phase 2)" : "")}: {want} legs skated, each at least a crouch (0.9 s) after its lane locked", legs.Count == want && legs.All(l => l.afterLock >= crouch), string.Join(",", legs.Select(l => l.afterLock)));
            var order = R.CarveOrder(n, p2);
            Check("Figure Carve: legs follow the numerals (farthest first)", legs.Count == want && legs.Select(l => l.who).Distinct().Count() == Mathf.Min(n, want));
            Check("Figure Carve: a hot trail behind every leg it skated", boss.TrailsSpawned - trails0 == legs.Count(l => Sim.Flat(l.to - l.from).magnitude >= 0.5f), $"{boss.TrailsSpawned - trails0} trails");
            Check("Figure Carve: lanes resolved, numerals cleared", !d.World.Hazards.Live.Any(h => h.Source == "Figure Carve") && d.World.Players.All(p => !p.Marks.Any(m => m.StartsWith("order_"))));
            Note($"Figure Carve hits on bots: {HitsSince(tick0, "Figure Carve")}, trail ticks: {HitsSince(tick0, "Carve trail")}");
        }

        // ---------------- 2. Body Check ----------------
        IEnumerator CheckChecks(int n, int turns)
        {
            int c0 = boss.CheckLog.Count, tick0 = d.World.Tick;
            yield return StartOf(turns > 1 ? "Turns" : "Check");
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == "Body Check (block)"), 5, "block ring shown");
            var ring = d.World.Hazards.Live.FirstOrDefault(h => h.Source == "Body Check (block)");
            var far = d.World.Players.Where(p => !p.Down).OrderByDescending(p => Sim.Flat(p.transform.position - boss.transform.position).sqrMagnitude).FirstOrDefault();
            var quarry = d.World.Players.FirstOrDefault(p => p.Marks.Contains(MechanicIcons.CarriedMark));
            Check($"Body Check: the farthest player is the quarry, the ring wants {R.BlockersNeeded(n)}", ring != null && quarry != null && ring.ShareCount == R.BlockersNeeded(n) && ring.Soak, quarry == far ? "" : "quarry was not the farthest at the first frame");
            yield return Wait(0.9f);
            Overhead(turns > 1 ? "turns" : "check");
            yield return BetweenBlocks(40);
            var log = boss.CheckLog.Skip(c0).ToList();
            Check($"Body Check: {turns} check{(turns > 1 ? "s" : "")} resolved (blocked or charged)", log.Count == turns, string.Join("; ", log.Select(x => x.blocked ? $"blocked by {x.blockers} ({x.share} each)" : "charged")));
            if (turns > 1) Check("Turns: the second quarry is someone else", log.Count == 2 && log[0].quarry != log[1].quarry);
            Check("Body Check: a blocked check splits 60 and trips it for 3.5 s; a charge leaves a trail", log.All(x => !x.blocked || (x.blockers >= 1 && x.share == R.Share(R.BlockTotal, x.blockers))) &&
                boss.PunishLog.Where(p => p.why == "tripped" && p.start >= tick0).All(p => p.end - p.start == R.Ticks(R.StuckSeconds)) &&
                boss.PunishLog.Count(p => p.why == "tripped" && p.start >= tick0) == log.Count(x => x.blocked));
            Check("Body Check: ring, lane and marker cleared", !d.World.Hazards.Live.Any(h => h.Source.StartsWith("Body Check")) && d.World.Players.All(p => !p.Marks.Contains(MechanicIcons.CarriedMark)));
        }

        // ---------------- 3. Crucible Toss ----------------
        IEnumerator TossChecks(int n, bool withLap)
        {
            int p0 = boss.PourLog.Count, l0 = boss.LapLog.Count;
            yield return StartOf(withLap ? "TossLap" : "Toss");
            yield return Until(() => d.World.Hazards.Live.Count(h => h.Source == "Crucible Toss") == 2, 5, "two pours shown");
            var pours = d.World.Hazards.Live.Where(h => h.Source == "Crucible Toss").ToList();
            var counts = R.PourCounts(n);
            Check($"Crucible Toss: two pours wanting {counts[0]} + {counts[1]}, {R.PourRing} m out on opposite sides, empty = wipe",
                pours.Count == 2 && pours.Select(h => h.ShareCount).OrderBy(x => x).SequenceEqual(counts.OrderBy(x => x)) && pours.All(h => h.WipeIfEmpty && h.Soak && Mathf.Abs(Sim.Flat(h.Origin).magnitude - R.PourRing) < 0.05f) &&
                Vector3.Angle(pours[0].Origin, pours[1].Origin) > 179f);
            yield return Wait(3.6f);
            Overhead(withLap ? "toss_lap" : "toss");
            yield return BetweenBlocks(40);
            var log = boss.PourLog.Skip(p0).ToList();
            Check($"Crucible Toss: both pours caught by the bots, nobody wiped", log.Count == 2 && log.All(x => !x.wiped && x.inside >= 1) && d.State == RunState.Combat && d.World.Players.All(p => !p.Down),
                string.Join("; ", log.Select(x => $"need {x.need}, inside {x.inside}, {x.share} each")));
            Check("Crucible Toss: shares follow the soakers (70 split)", log.All(x => x.inside == 0 || x.share == R.Share(R.PourTotal, x.inside)));
            if (withLap)
            {
                var lap = boss.LapLog.Skip(l0).ToList();
                Check("Toss + Lap: the pours land before the Pirouette resolves, and the lap and Pirouette both ran", lap.Count == 1 && log.Count == 2 && log.All(x => x.tick <= lap[0].tick));
            }
            Check("Crucible Toss: pours, numerals and ladles cleared", !d.World.Hazards.Live.Any(h => h.Source == "Crucible Toss") && boss.LadleCount == 0 && !FindObjectsByType<CrucibleSkaterEncounter.Numeral>(FindObjectsSortMode.None).Any());
        }

        // ---------------- 4. Rink Lap -> Pirouette ----------------
        IEnumerator LapChecks(int n, bool p2)
        {
            int l0 = boss.LapLog.Count, pun0 = boss.PunishLog.Count;
            yield return StartOf("Lap");
            yield return Until(() => boss.Current.Contains("lapping"), 6, "lapping");
            Overhead("lap");
            yield return Until(() => boss.Current.Contains("Pirouette"), 6, "Pirouette");
            yield return Wait(1.2f);
            Overhead("pirouette");
            yield return BetweenBlocks(30);
            var lap = boss.LapLog.Skip(l0).ToList();
            var pun = boss.PunishLog.Skip(pun0).Where(x => x.why == "dizzy").ToList();
            Check("Rink Lap -> Pirouette resolved, then dizzy 3.5 s with nothing of its own live (the major punish window)", lap.Count == 1 && pun.Count == 1 && pun[0].end - pun[0].start == R.Ticks(R.DizzySeconds),
                lap.Count == 1 ? $"band hits {lap[0].bandHits}, pirouette hits {lap[0].pirouetteHits}" : "");
            Check("Rink Lap: band and circle cleared", !d.World.Hazards.Live.Any(h => h.Source == "Rink Lap" || h.Source == "Pirouette"));
        }

        // ---------------- an unforced phase 1 loop ----------------
        IEnumerator NaturalLoop(int n)
        {
            yield return BetweenBlocks(20);
            int b0 = boss.BlockLog.Count;
            yield return Until(() => boss.BlockLog.Count - b0 >= 4 && boss.Current == "between blocks", 90, "one natural rotation");
            var seen = boss.BlockLog.Skip(b0).Take(4).Select(s => s.Split(' ').Last()).ToList();
            var want = CrucibleSkaterEncounter.Rotation(false);
            // the rotation continues where it was; any 4 in a row are a rotation of it
            bool ok = seen.Count == 4 && Enumerable.Range(0, 4).Any(k => Enumerable.Range(0, 4).All(i => seen[i] == want[(i + k) % 4]));
            Check("an unforced Phase 1 loop runs all four mechanics in order with the party standing", ok && d.State == RunState.Combat && d.World.Players.All(p => !p.Down), string.Join(",", seen));
        }

        // ---------------- standing still: melee and range both get hit ----------------
        IEnumerator StillChecks()
        {
            Section("standing still (content rules §5: melee and range are both in danger)");
            yield return BetweenBlocks(20);
            var me = d.Player; var ally = d.Allies[0];
            var inputs = d.World.Players.ToDictionary(p => p, p => p.Input);
            foreach (var p in d.World.Players) p.Input = new Still();
            // Rink Lap: the ranged player at 10 m is in the band, the melee player beside the boss is under the Pirouette
            boss.ForcedNext = "Lap";
            yield return Until(() => boss.Current.StartsWith("Rink Lap"), 10, "Rink Lap");
            Place(ally, new Vector3(0, 0, 10.5f)); Place(me, new Vector3(1.5f, 0, 0.5f));
            int t0 = d.World.Tick;
            yield return BetweenBlocks(30);
            Check("Rink Lap: a ranged player standing still at the edge is hit by the lap", HitsSince(t0, "Rink Lap", ally) == 1);
            Check("Pirouette: a melee player standing still at the centre is hit", HitsSince(t0, "Pirouette", me) == 1);
            // Figure Carve: ranged (farther, I) and melee (between, on leg I) both hit
            var b = Sim.Flat(boss.transform.position);
            Place(ally, b + new Vector3(0, 0, 9f)); Place(me, b + new Vector3(0, 0, 2.2f));
            t0 = d.World.Tick;
            yield return Block("Carve", 30);
            Check("Figure Carve: the ranged waypoint and a melee player on leg I, both standing still, are hit", HitsSince(t0, "Figure Carve", ally) >= 1 && HitsSince(t0, "Figure Carve", me) >= 1,
                $"ranged {HitsSince(t0, "Figure Carve", ally)}, melee {HitsSince(t0, "Figure Carve", me)}");
            // the trails cool with the bots back in charge (a natural Toss needs its soakers), then freeze again in a gap
            foreach (var kv in inputs) kv.Key.Input = kv.Value;
            yield return Wait(R.TrailLife + 0.5f);
            yield return BetweenBlocks(40);
            foreach (var p in d.World.Players) p.Input = new Still();
            // Body Check, nobody in the ring: the far quarry standing still takes the charge
            b = Sim.Flat(boss.transform.position);
            var away = (b.magnitude > 1 ? -b.normalized : Vector3.forward);
            Place(ally, b + away * 10f); Place(me, b + Quaternion.Euler(0, 90, 0) * away * 2.5f);
            t0 = d.World.Tick; int c0 = boss.CheckLog.Count;
            yield return Block("Check", 30);
            Check("Body Check: unblocked, the far quarry standing still is charged", boss.CheckLog.Count > c0 && !boss.CheckLog.Last().blocked && HitsSince(t0, "Body Check", ally) == 1,
                boss.CheckLog.Count > c0 ? $"blocked {boss.CheckLog.Last().blocked}" : "no check");
            foreach (var kv in inputs) kv.Key.Input = kv.Value;
            yield return Wait(R.TrailLife + 0.5f);
            yield return BetweenBlocks(40);
            foreach (var p in d.World.Players) p.Input = new Still();
            // Body Check, a blocker standing in the ring: it trips
            b = Sim.Flat(boss.transform.position); away = (b.magnitude > 1 ? -b.normalized : Vector3.forward);
            Place(ally, b + away * 10f); Place(me, b + away * (R.BodyRadius + 0.1f + R.RingRadius));
            t0 = d.World.Tick; c0 = boss.CheckLog.Count; int trips = boss.Trips;
            yield return Block("Check", 30);
            Check($"Body Check: a player standing in the ring blocks it (takes the whole {R.BlockTotal}), it trips, the quarry is spared", boss.CheckLog.Count > c0 && boss.CheckLog.Last().blocked && boss.Trips == trips + 1 &&
                HitsSince(t0, "Body Check (block)", me) == 1 && HitsSince(t0, "Body Check", ally) == 0 && hits.Where(x => x.tick >= t0 && x.who == me && x.source == "Body Check (block)").Sum(x => x.dmg) == R.BlockTotal);
            Overhead("check_blocked");
            foreach (var kv in inputs) kv.Key.Input = kv.Value;
            yield return Wait(R.TrailLife * 0.5f);
        }

        // ---------------- the 50% reheat and phase 2 ----------------
        IEnumerator PhaseTwo(int n)
        {
            Section($"the 50% reheat and phase 2 ({n} players)");
            yield return StartOf("Toss");
            yield return Until(() => boss.Current.StartsWith("Crucible Toss"), 5, "Toss");
            boss.DebugSetHpFraction(0.505f); boss.TakeHit(100, boss.transform.position + Vector3.up);
            Check("crossing 50% mid-attack: the attack carries on", !boss.Phase2 && boss.ShiftStart < 0 && boss.Current.StartsWith("Crucible Toss"));
            yield return Until(() => boss.ShiftStart >= 0, 30, "reheat starts");
            Check("the reheat waits for the attack to finish", boss.BlockLog.Last().EndsWith("Reheat"));
            int hp = boss.Hp; boss.TakeHit(50, boss.transform.position + Vector3.up);
            Check("it stays damageable during the reheat (no hold, no heal)", boss.Hp == hp - 50 && boss.Hp < boss.HpAtShiftStart);
            int s0 = d.World.Tick;
            yield return Until(() => boss.Phase2, 5, "phase 2");
            Check("the reheat is harmless (nothing spawned) and lasts 2 s", !spawned.Any(x => x.tick >= s0 && x.tick < boss.ShiftEnd) && boss.ShiftEnd - boss.ShiftStart == R.Ticks(R.ShiftSeconds));
            yield return BetweenBlocks(20);
            yield return CarveChecks(n, true);
            yield return CheckChecks(n, 2);
            yield return TossChecks(n, true);
        }

        IEnumerator HardChecks(int n)
        {
            d.World.Difficulty = Difficulty.Hard;
            int r0 = boss.RetraceLegs;
            yield return Block("Carve", 40);
            Check("Hard, Retrace: the route is skated back after the last leg", boss.RetraceLegs > r0, $"{boss.RetraceLegs - r0} segments");
            d.World.Difficulty = Difficulty.Medium;
        }

        // every hazard keeps its time and wears the raid look
        IEnumerator LifetimeAndStyle()
        {
            yield return BetweenBlocks(20);
            int now = d.World.Tick;
            var own = spawned.Where(x => x.h.Source != null).ToList();
            var outlived = own.Where(x => x.h.ResolveAt < (1 << 27) && x.tick + x.h.ResolveAt + Mathf.Max(0, x.h.ActiveTicks) + 3 < now && d.World.Hazards.Live.Contains(x.h)).ToList();
            Check("nothing outlives its time (every warning, soak, trail resolves or cools on schedule)", outlived.Count == 0, string.Join(", ", outlived.Select(x => x.h.Source).Distinct()));
            var plain = own.Where(x => x.h.Tele != null && !x.h.Tele.LockOnTop && x.h.Tele.Order == 0).ToList();
            Check($"every warning is in the raid style (HazardSystem.RaidStyle): {own.Count} spawned", own.Count > 0 && plain.Count == 0, string.Join(", ", plain.Select(x => x.h.Source).Distinct()));
            Check("every trail is a standing burn that counts down to its lock (no fake countdown)", own.Where(x => x.h.Source.EndsWith("trail")).All(x => x.h.BurnPerSecond > 0 && x.h.FillToLock));
            var sources = own.Select(x => x.h.Source).Distinct().OrderBy(s => s).ToList();
            Note("hazards seen: " + string.Join(", ", sources));
            Check("every mechanic fired", new[] { "Figure Carve", "Carve trail", "Body Check (block)", "Body Check", "Crucible Toss", "Rink Lap", "Pirouette", "Retrace" }.All(sources.Contains));
        }

        IEnumerator DefeatChecks()
        {
            yield return StartOf("Carve");
            yield return Until(() => boss.TrailsSpawned > 0 && d.World.Hazards.Live.Any(h => h.Source == "Carve trail") && d.World.Hazards.Live.Any(h => h.Source == "Figure Carve"), 15, "a trail and a lane live");
            boss.DebugDefeat();
            yield return null;
            Check("defeat mid-attack: every lane, trail, soak and ladle cleared at once, numerals gone", d.World.Hazards.Count == 0 && boss.LadleCount == 0 && d.World.Players.All(p => p.Marks.Count == 0));
            int t = d.World.Tick;
            yield return Until(() => d.State == RunState.Results, 12, "results");
            Check("practice victory after defeat", d.State == RunState.Results && d.Victory);
        }

        void Finish()
        {
            Check("no runtime errors or exceptions", errors == 0, errors + " logged");
            lines.Insert(0, $"Crucible Skater autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "crucible-skater-report.txt"), lines);
            Debug.Log($"[crucible-skater-test] done: {pass} passed, {fail} failed");
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
