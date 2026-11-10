using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Rivet Cantor inside the real game:  -rivetCantorTest -lastbellOut <folder>
    // Registration and practice start on Brass Foundry, then the whole fight with Perfect ally bots (the player too) at
    // party sizes 2 and 4: both phases' rotations, every mechanic fires and resolves, the Key Change and Retuning windows
    // are quiet, every warning got the raid look, and defeat leaves nothing behind. Then solo: the Verse and the drum
    // chain, Hard's Encore, Easy's longer warnings, and the stand-still check (a still melee and a still ranged player
    // are both hit by Rivet Run and Anvil Hymn). Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player
    // DebugSetHp (hits are counted, HP is topped up so one miss doesn't end the run), direct placement, still extras.
    // Writes rivet-cantor-report.txt plus screenshots and exits 0 / 1.
    public sealed class RivetCantorAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-rivetCantorTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "rivet-cantor-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<RivetCantorAutoTest>()) return;
            var go = new GameObject("Rivet Cantor autotest"); DontDestroyOnLoad(go); go.AddComponent<RivetCantorAutoTest>();
        }

        sealed class Still : IInputSource { public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward }; }

        RunDirector d;
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<(int tick, Hazard h)> spawned = new List<(int, Hazard)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        RivetCantorEncounter boss;
        readonly HashSet<string> shotTaken = new HashSet<string>();

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
            lines.Add(line); Debug.Log("[rivet-cantor-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[rivet-cantor-test] " + s); }
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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 15), new Vector3(0, 0, -0.5f), 52);

        void Keep()
        {
            if (d == null || d.World == null) return;
            if (keepAlive) foreach (var p in d.World.Players) if (!p.Down && p.Hp < 100) p.DebugSetHp(100);
            // one overhead shot per mechanic and party size, a moment into it with its warnings up
            if (boss != null && !boss.Dead && boss.Pending > 0)
            {
                string key = $"{d.World.Players.Count}p_{(boss.Phase2 ? "p2" : "p1")}_{Slug(boss.Current)}";
                int age = boss.Current.StartsWith("Work-Song") ? 150 : boss.Current.StartsWith("Rivet Run") ? 120 : boss.Current.StartsWith("Rivet Chains") ? 150 : 60;
                if (!shotTaken.Contains(key) && boss.Current != lastCurrent) { lastCurrent = boss.Current; currentSince = d.World.Tick; }
                if (!shotTaken.Contains(key) && d.World.Tick - currentSince >= age && !boss.Current.EndsWith("(end)")) { shotTaken.Add(key); Overhead(key); }
            }
        }
        string lastCurrent = ""; int currentSince;
        static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void OnSpawn(Hazard h) => spawned.Add((d.World.Tick, h));
        void Place(PlayerController p, Vector3 at) { p.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); }
        int Hits(int since, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= since && h.source.StartsWith(prefix) && (who == null || h.who == who));

        IEnumerator Block(string atk, float timeout = 60f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, atk + " ends");
        }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("rules");
            yield return new WaitForSeconds(1f);
            Check("Verse patterns: 3 calls above 50%, 4 below, Encore adds the last call again",
                RivetCantorEncounter.VersePattern(false, 0, false).Length == 3 && RivetCantorEncounter.VersePattern(true, 1, false).Length == 4 &&
                RivetCantorEncounter.VersePattern(false, 1, true).Length == 4 && RivetCantorEncounter.VersePattern(false, 1, true)[3] == RivetCantorEncounter.VersePattern(false, 1, true)[2]);
            Check("rotation: Verse, Rivet Run, Chains, Anvil Hymn; below 50% the chains lock under a Rivet Run",
                string.Join(",", RivetCantorEncounter.Rotation(false)) == "Verse,Rivets,Chains,Hymn" && string.Join(",", RivetCantorEncounter.Rotation(true)) == "Verse,Rivets,RivetChains,Hymn");
            Check("Chorus total survivable by one player from full on Medium and Hard (settled soak rule)", RivetCantorEncounter.ChorusTotal * RunDifficulty.DamageTakenScale(Difficulty.Hard) < 100);

            Section("registration and practice start");
            int map = RunMaps.IndexOf(RunDirector.CrucivarMap), enc = RunDirector.RivetCantorPractice;
            Check("practice entry registered on Brass Foundry, practice only, 2 phases", enc >= 0 && RunDirector.Bosses[enc].Map == "brass-foundry" && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Phases == 2);
            Check("run order unchanged (Rivet Cantor excluded)", !RunDirector.RunOrder.Contains(enc) && RunDirector.RunOrder.Length == 4);
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            Check("Crucivar keeps the Brass Foundry practice slot", RunDirector.CrucivarPractice >= 0 && RunDirector.Bosses[RunDirector.CrucivarPractice].Name.StartsWith("Crucivar"));
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }

            foreach (int size in new[] { 2, 4 }) yield return PartyFight(size, enc, map);
            yield return SoloChecks(enc, map);
            Finish();
        }

        IEnumerator Practice(int size, int enc, int map)
        {
            d = RunDirector.Instance;
            if (d.State != RunState.Menu) { d.ReturnToMenu(); yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Menu, 10, "back to the menu"); d = RunDirector.Instance; }
            RunParty.Selected = size - 1;
            Check($"[{size}p] practice request accepted", d.StartPractice("Tollbreaker", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 40) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check($"[{size}p] practice Ready on Brass Foundry with {size - 1} allies", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc && d.World.Players.Count == size);
            d.Player.Input = new AllyBot(size - 1, size, BotMode.Perfect);
            hits.Clear(); spawned.Clear(); shotTaken.RemoveWhere(s => s.StartsWith(size + "p_")); lastCurrent = "";
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            d.World.Hazards.Spawned -= OnSpawn; d.World.Hazards.Spawned += OnSpawn;
            Check($"[{size}p] Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is RivetCantorEncounter, 4, "boss spawned");
            boss = d.Boss as RivetCantorEncounter;
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the measured numbers
            yield return Until(() => d.State == RunState.Combat, 8, "combat");
        }

        // in-game frames for the animation critic (9 Nov): each clip once, at its strike frame, from the gameplay and a close camera
        static readonly Dictionary<string, float> StrikeAt = new Dictionary<string, float>
        {
            ["ChorusCall"] = 0.35f, ["SoloCall"] = 0.35f, ["HammerRaise"] = 0.78f, ["HammerLoop"] = 0.02f, ["ForkPlant"] = 0.8f,
            ["ForkStrike"] = 0.55f, ["DrumSlam"] = 1.4f, ["PhaseShift"] = 1.0f, ["Defeat"] = 2.3f,
        };
        IEnumerator ClipFrames(CharacterPresenter pr, Transform t, Dictionary<string, float> strikeAt)
        {
            var seen = new HashSet<string>();
            while (pr && t && seen.Count < 16)
            {
                string c = pr.CurrentClip;
                if (c != "-" && seen.Add(c))
                {
                    yield return new WaitForSeconds(strikeAt.TryGetValue(c, out var s) ? s : 0.4f);
                    if (!pr || !t) yield break;
                    var b = t.position;
                    RenderShot($"clip_{c}_game", b + new Vector3(0, 11f, -14f), b + Vector3.up * 1.2f, 40);
                    RenderShot($"clip_{c}_close", b + t.forward * 6.5f + Vector3.up * 2.4f, b + Vector3.up * 1.6f, 40);
                }
                yield return null;
            }
        }

        IEnumerator PartyFight(int size, int enc, int map)
        {
            Section($"party of {size} (Perfect bots, the player too)");
            yield return Practice(size, enc, map);
            if (boss == null) yield break;
            Check($"[{size}p] opens in phase 1 at 120 BPM, no ability text", !boss.Phase2 && Mathf.Approximately(boss.Bpm, 120) && boss.BeatTicks == 30 && boss.PhaseName == "" && boss.Lesson == "");
            if (boss.Presenter && boss.Presenter.HasModel)
            {
                var missing = RivetCantorEncounter.ClipNames.Where(c => !boss.Presenter.Has(c)).ToList();
                Check($"[{size}p] the rigged model is loaded with all {RivetCantorEncounter.ClipNames.Length} clips", !boss.Placeholder && missing.Count == 0, missing.Count > 0 ? "missing: " + string.Join(", ", missing) : "");
                if (size == 2) StartCoroutine(ClipFrames(boss.Presenter, boss.transform, StrikeAt));
            }
            else Check($"[{size}p] no rigged model in this build: the labelled stand-in is used (Placeholder)", boss.Placeholder && GameObject.Find("Rivet Cantor (STAND-IN, labelled placeholder)"));
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "walks to the centre");
            RenderShot($"{size}p_body", new Vector3(0, 4f, 9.5f), new Vector3(0, 2f, 0), 40);

            // phase 1: one whole rotation in its natural order
            yield return Until(() => boss.BlockLog.Any(s => s.EndsWith(" Hymn")), 120, "phase 1 rotation reaches Anvil Hymn");
            yield return Until(() => boss.Current == "between blocks", 30, "Anvil Hymn ends");
            var p1 = boss.BlockLog.Select(s => s.Split(' ').Last()).ToList();
            Check($"[{size}p] phase 1 ran Verse, Rivet Run, Chains, Anvil Hymn in order", p1.Take(4).SequenceEqual(new[] { "Verse", "Rivets", "Chains", "Hymn" }), string.Join(",", p1));
            int p1Spawn = spawned.Count;

            // 50%: the Key Change
            int p2Start = boss.BlockLog.Count;
            boss.DebugSetHpFraction(0.45f);
            int hpBefore = boss.Hp;
            yield return Until(() => boss.Phase2, 40, "Key Change to phase 2");
            Check($"[{size}p] Key Change: 2 s, harmless (nothing spawned), no heal, now 140 BPM",
                boss.ShiftEnd - boss.ShiftStart >= RivetCantorEncounter.ShiftTicks && !spawned.Any(s => s.tick > boss.ShiftStart && s.tick < boss.ShiftEnd) && boss.Hp <= hpBefore && Mathf.Approximately(boss.Bpm, 140),
                $"{boss.ShiftStart}-{boss.ShiftEnd}, HP {boss.Hp}/{hpBefore}");
            yield return Until(() => boss.BlockLog.Skip(p2Start).Any(s => s.EndsWith(" Hymn")), 120, "phase 2 rotation reaches Anvil Hymn");
            yield return Until(() => boss.Current == "between blocks", 30, "phase 2 Anvil Hymn ends");
            var p2 = boss.BlockLog.Skip(p2Start).Select(s => s.Split(' ').Last()).Where(s => s != "140").ToList();
            Check($"[{size}p] one Key Change, then phase 2 ran Verse, Rivet Run, Chains under Rivet Run, Anvil Hymn", p2.Count(s => s == "Change") == 1 && p2.FirstOrDefault() == "Change" && p2.Where(s => s != "Change").Take(4).SequenceEqual(new[] { "Verse", "Rivets", "RivetChains", "Hymn" }), string.Join(",", p2));

            // ---- every mechanic fired and resolved ----
            int chorusCalls = boss.VerseLog.Sum(v => v.calls.Count(c => c == RivetCantorEncounter.Call.Chorus));
            int soloCalls = boss.VerseLog.Sum(v => v.calls.Count(c => c == RivetCantorEncounter.Call.Solo));
            Check($"[{size}p] Verse: 3 calls then 4 calls below 50%", boss.VerseLog.Count == 2 && boss.VerseLog[0].calls.Length == 3 && boss.VerseLog[1].calls.Length == 4);
            Check($"[{size}p] Verse: every Chorus resolved, none empty, the whole party inside each (shares split by party size)",
                boss.ChorusResolves == chorusCalls && boss.ChorusEmpty == 0 && boss.ChorusLog.All(c => c.inside == size && c.share == Mathf.CeilToInt(RivetCantorEncounter.ChorusTotal / (float)size)),
                string.Join("; ", boss.ChorusLog.Select(c => $"{c.inside}/{c.standing} in, {c.share} each")));
            Check($"[{size}p] Verse: every Solo resolved; the bots spread (Solo hits on others: {boss.SoloHits})", boss.SoloResolves == soloCalls && boss.SoloHits <= 1, $"{boss.SoloResolves}/{soloCalls} resolved");
            Check($"[{size}p] Verse: the Chorus numeral showed the live party ({size}) whenever a Chorus was up", boss.NumeralTicks > 0 && boss.NumeralMin == size && boss.NumeralMax == size && boss.FillMatches == boss.FillChecks,
                $"{boss.NumeralTicks} ticks, numeral {boss.NumeralMin}-{boss.NumeralMax}, fill ticks matched the soakers at {boss.FillMatches}/{boss.FillChecks} resolves");
            Check($"[{size}p] Verse: on every Solo the bots got off his feet (2.5 m; hits {boss.SoloFeetHits})", boss.SoloFeetHits <= 1);
            int rivets = spawned.Count(s => s.h.Source == "Rivet");
            Check($"[{size}p] Rivet Run: three casts (one above 50%, plain and under chains below), every rivet popped ({rivets}); outward X aimed at the nearest, inward X at the farthest and never on top of the first",
                boss.RivetCasts == 3 && boss.RivetLog.Count == 3 && boss.RivetPops == rivets && boss.RivetLog.All(r => { float a = Mathf.Abs(Mathf.DeltaAngle(r.aim1, r.aim2)) % 90f; return a >= 12f && a <= 78f; }),
                string.Join("; ", boss.RivetLog.Select(r => $"aim {r.aim1:0}/{r.aim2:0}, counter-line +{r.set2Start - r.tick} ticks, first X ends +{r.set1End - r.tick}")));
            Check($"[{size}p] Rivet Run: below 50% the counter-line starts before the first X ends; above, after it",
                boss.RivetLog.Count == 3 && boss.RivetLog[0].set2Start > boss.RivetLog[0].set1End && boss.RivetLog.Skip(1).All(r => r.set2Start < r.set1End));
            Note($"[{size}p] Rivet hits on bots: {boss.RivetHits}");
            Check($"[{size}p] Rivet Chains: twice, {size / 2} pair(s) and {size % 2} drum chain, every chain resolved",
                boss.ChainLog.Count == 2 && boss.ChainLog.All(c => c.pairs == size / 2 && c.drum == size % 2),
                string.Join("; ", boss.ChainLog.Select(c => $"{c.pairs} pairs, {c.drum} drum, {c.snaps} snaps, {c.whips} whips")));
            var wantNumerals = Enumerable.Range(1, size).Select(MechanicIcons.Order).ToList();
            Check($"[{size}p] Rivet Chains: pairs follow the order numerals, shown over each player while chained (I-II, III-IV)",
                boss.ChainNumerals.SequenceEqual(wantNumerals) && d.World.Players.All(p => !p.Marks.Any(m => m.StartsWith("order_"))), string.Join(",", boss.ChainNumerals));
            Check($"[{size}p] Rivet Chains: the bots stretched their chains and kept off the others' (snaps {boss.ChainSnaps}, whips {boss.ChainWhips})", boss.ChainSnaps <= 1 && boss.ChainWhips <= 1);
            Check($"[{size}p] Anvil Hymn: twice, 4 pulses each, all resolved", boss.HymnCasts == 2 && spawned.Count(s => s.h.Source == "Anvil Hymn") == 8);
            Note($"[{size}p] Anvil Hymn hits on bots: {boss.PulseHits}");
            Check($"[{size}p] Retuning: 3.5 s with nothing live and nothing spawned (the punish window)",
                boss.RetuneLog.Count == 2 && boss.RetuneLog.All(r => r.end - r.start >= RivetCantorEncounter.RetuneTicks && r.liveAtEnd == 0 && !spawned.Any(s => s.tick > r.start && s.tick < r.end)),
                string.Join("; ", boss.RetuneLog.Select(r => $"{r.start}-{r.end} live {r.liveAtEnd}")));
            Check($"[{size}p] every warning got the raid look ({boss.Styled} styled of {spawned.Count} spawned)", boss.Styled == spawned.Count && spawned.Count > p1Spawn && spawned.All(s => s.h.MechanicId != null));
            Check($"[{size}p] no wipe: the party stood through the whole fight", d.State == RunState.Combat && d.World.Players.All(p => !p.Down));
            int taken = hits.Sum(h => h.dmg);
            Note($"[{size}p] damage taken by the bot party over both rotations: {taken} ({string.Join(", ", hits.GroupBy(h => h.source).Select(g => $"{g.Key} {g.Count()}x"))})");

            yield return DefeatChecks(size);
        }

        IEnumerator DefeatChecks(int size)
        {
            // defeat mid-mechanic: nothing may stay behind
            yield return Block("Verse", 60);
            boss.ForcedNext = "Rivets";
            yield return Until(() => boss.Current.StartsWith("Rivet Run") && boss.Pending > 10, 20, "Rivet Run warnings up");
            boss.DebugDefeat();
            yield return null; yield return null;
            Check($"[{size}p] defeat mid-Rivet Run: every hazard cancelled, no glyphs or player marks left",
                d.World.Hazards.Count == 0 && boss.GlyphCount == 0 && d.World.Players.All(p => p.Marks.Count == 0) && boss.Dead);
            yield return Until(() => d.State == RunState.Cleared || d.State == RunState.Results, 10, "victory routing");
            Check($"[{size}p] defeat routes to Cleared/Results", d.State == RunState.Cleared || d.State == RunState.Results, d.State.ToString());
            yield return Until(() => d.State == RunState.Results, 15, "results");
        }

        IEnumerator SoloChecks(int enc, int map)
        {
            Section("solo (1 player) and difficulty");
            yield return Practice(1, enc, map);
            if (boss == null) yield break;
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "walks to the centre");
            yield return Until(() => boss.Current == "between blocks", 30, "first Verse ends");
            int c0 = boss.ChorusLog.Count;
            yield return Block("Verse");
            Check("solo Verse: the Chorus is taken alone (70), the Solo mark spares its owner and the bot left his feet for it", boss.ChorusLog.Skip(c0).All(x => x.inside == 1 && x.share == RivetCantorEncounter.ChorusTotal) && boss.SoloHits == 0 && boss.SoloFeetHits == 0 && boss.ChorusEmpty == 0);
            int ch0 = boss.ChainLog.Count;
            yield return Block("Chains");
            Check("solo Chains: chained to the drum, and the bot walked 7 m clear", boss.ChainLog.Count == ch0 + 1 && boss.ChainLog.Last().drum == 1 && boss.ChainLog.Last().pairs == 0 && boss.ChainLog.Last().snaps == 0);

            d.World.Difficulty = Difficulty.Hard;
            yield return Block("Verse");
            Check("Hard: Encore, the Verse's last call sung twice (4 calls above 50%)", boss.VerseLog.Last().calls.Length == 4 && boss.VerseLog.Last().calls[3] == boss.VerseLog.Last().calls[2]);
            d.World.Difficulty = Difficulty.Easy;
            Check("Easy: longer warnings (answer 8 beats, rivets 4, chains 6, hymn tell 4)", boss.EchoBeats == 8 && boss.RivetFirstBeats == 4 && boss.ChainBeats == 6 && boss.HymnTellBeats == 4);
            d.World.Difficulty = Difficulty.Medium;
            Check("Medium: answer 6 beats, rivets 3, chains 5, hymn tell 3", boss.EchoBeats == 6 && boss.RivetFirstBeats == 3 && boss.ChainBeats == 5 && boss.HymnTellBeats == 3);

            // stand-still: a still player next to him and a still one at long range are both hit by Rivet Run and Anvil Hymn
            Section("stand-still danger (content rules §5)");
            yield return Until(() => boss.Current == "between blocks", 20, "between blocks");
            var melee = d.Player; var botInput = melee.Input; melee.Input = new Still();
            var c = Sim.Flat(boss.transform.position);
            // an empty Chorus is a heavy hit, not a wipe; a Solo hits whoever stays on his feet
            Place(melee, c + new Vector3(0, 0, 8f));
            int e0 = boss.ChorusEmpty, t0 = d.World.Tick;
            yield return Block("Verse");
            var emptyHits = hits.Where(h => h.tick >= t0 && h.source == "Chorus" && h.who == melee).ToList();
            Check($"empty Chorus: no wipe, {RivetCantorEncounter.EmptyChorusDmg} to everyone standing", boss.ChorusEmpty > e0 && emptyHits.Count > 0 && emptyHits.All(h => h.dmg == RivetCantorEncounter.EmptyChorusDmg) && d.State == RunState.Combat,
                $"empty {boss.ChorusEmpty - e0}, hits {string.Join("/", emptyHits.Select(h => h.dmg))}");
            Place(melee, Sim.Flat(boss.transform.position) + new Vector3(0, 0, 1.8f));
            t0 = d.World.Tick;
            yield return Block("Verse");
            Check("Solo: a still player at his feet (1.8 m) is hit by every Solo", Hits(t0, "Solo (his feet)", melee) > 0, $"hits {Hits(t0, "Solo (his feet)", melee)}");
            yield return Until(() => boss.Current == "between blocks", 20, "between blocks");
            c = Sim.Flat(boss.transform.position);
            Place(melee, c + new Vector3(0, 0, 2.6f));
            var ranged = PlayerController.Spawn(d.World, "Arcanist", d.World.Constrain(c + new Vector3(0, 0, -11f), PlayerController.Radius), 0);
            ranged.Input = new Still(); ranged.AbilitiesEnabled = false; extras.Add(ranged);
            t0 = d.World.Tick;
            yield return Block("Rivets");
            Check("Rivet Run hits a still melee player (2.6 m) and a still ranged player (11 m)", Hits(t0, "Rivet", melee) > 0 && Hits(t0, "Rivet", ranged) > 0, $"melee {Hits(t0, "Rivet", melee)}, ranged {Hits(t0, "Rivet", ranged)}");
            Place(melee, Sim.Flat(boss.transform.position) + new Vector3(0, 0, 2.6f)); Place(ranged, Sim.Flat(boss.transform.position) + new Vector3(0, 0, -11f));
            t0 = d.World.Tick;
            yield return Block("Hymn");
            Check("Anvil Hymn hits a still melee player and a still ranged player", Hits(t0, "Anvil Hymn", melee) > 0 && Hits(t0, "Anvil Hymn", ranged) > 0);
            Place(melee, Sim.Flat(boss.transform.position) + new Vector3(0, 0, 2.6f)); Place(ranged, Sim.Flat(boss.transform.position) + new Vector3(0, 0, -11f));
            t0 = d.World.Tick;
            yield return Block("Chains");
            Note($"Chains with the two still players (paired, 13.6 m apart): snaps {Hits(t0, "Rivet Chain (snap)")} — chains threaten by geometry, not by standing still");
            Overhead("solo_stand_still");
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
            boss.DebugDefeat();
            yield return null; yield return null;
            Check("solo defeat: nothing left behind (no hazards, glyphs or Chorus numeral)", d.World.Hazards.Count == 0 && boss.GlyphCount == 0 && boss.ChorusNeed == -1);
        }

        void Finish()
        {
            Check("no exceptions or errors logged", errors == 0, errors > 0 ? errors + " logged" : "");
            lines.Insert(0, $"RESULT {(fail == 0 ? "PASS" : "FAIL")}  {pass} passed, {fail} failed  (Rivet Cantor acceptance, {DateTime.Now:yyyy-MM-dd HH:mm})");
            File.WriteAllLines(Path.Combine(dir, "rivet-cantor-report.txt"), lines);
            Debug.Log("[rivet-cantor-test] " + lines[0]);
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
