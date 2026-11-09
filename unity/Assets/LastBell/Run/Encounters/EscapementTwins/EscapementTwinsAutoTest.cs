using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using R = LastBell.Run.EscapementTwinsRules;

namespace LastBell.Run
{
    // Acceptance run for the Escapement Twins inside the real game:  -escapementTwinsTest -lastbellOut <folder>
    // Pure rules first (every warning against the content-rules escape check on the Brass Foundry floor), then
    // registration, then the fight with Perfect bots (every player, the test player included, is an AllyBot) at party
    // sizes 2 and 4, plus a short solo pass and a Hard pass. Each ability is forced once per phase (ForcedNext); each must
    // fire, resolve and leave nothing on the floor. Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player
    // DebugSetHp (the test keeps everyone topped up so one bot's mistake can't end the run; hits are still counted).
    // Writes escapement-twins-report.txt plus screenshots and exits 0 / 1.
    public sealed class EscapementTwinsAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-escapementTwinsTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "escapement-twins-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<EscapementTwinsAutoTest>()) return;
            var go = new GameObject("Escapement Twins autotest"); DontDestroyOnLoad(go); go.AddComponent<EscapementTwinsAutoTest>();
        }

        RunDirector d; EscapementTwinsEncounter boss;
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<string> spawned = new List<string>(); readonly List<int> spawnTick = new List<int>();
        string dir; bool keepAlive = true; int downs;

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
            lines.Add(line); Debug.Log("[escapement-twins-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[escapement-twins-test] " + s); }
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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 15), new Vector3(0, 0, -0.5f), 50);
        IEnumerator Snap(string name) { yield return new WaitForEndOfFrame(); Overhead(name); }

        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 100) p.DebugSetHp(100);
        }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void OnSpawn(Hazard h) { spawned.Add(h.Source); spawnTick.Add(d.World.Tick); }
        int HitsSince(int tick, string prefix) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix));

        // ======================================================================================================
        IEnumerator Main()
        {
            yield return new WaitForSeconds(1f);
            Section("pure rules (escape check: 4.2 m/s after 0.3 s, Brass Foundry floor)");
            int map = RunMaps.IndexOf(RunDirector.CrucivarMap);
            float bound = RunMaps.All[map].Bound;
            Note($"Brass Foundry player bound {bound} m, boss bound {CombatWorld.BossBound} m");
            PureRules(bound);

            Section("registration");
            int enc = RunDirector.EscapementTwinsPractice;
            Check("practice entry registered on the Brass Foundry, practice only", enc >= 0 && RunDirector.Bosses[enc].Map == RunDirector.CrucivarMap && !RunDirector.Bosses[enc].InRun);
            Check("not in the run order", !RunDirector.RunOrder.Contains(enc));
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            Check("card name is 'Escapement Twins'", LastBell.Menu.TestRushSetup.CardName(enc) == "Escapement Twins");
            // entries are looked up by name, never by index; the integration added Rivet Cantor and the campaign bonus after the
            // tutorial (9 Nov), so only check the tutorial is still there and practice only
            Check("the tutorial entry is still registered, practice only", RunDirector.TutorialPractice >= 0 && !RunDirector.Bosses[RunDirector.TutorialPractice].InRun);
            Check("rotation: four abilities in each phase",
                EscapementTwinsEncounter.Rotation(false).Distinct().Count() == 4 && EscapementTwinsEncounter.Rotation(true).Distinct().Count() == 4);

            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }
            d = RunDirector.Instance;

            foreach (int n in new[] { 2, 4 }) yield return PartyRun(enc, map, n, Difficulty.Medium, full: true);
            yield return PartyRun(enc, map, 1, Difficulty.Medium, full: false);
            yield return PartyRun(enc, map, 2, Difficulty.Hard, full: false);
            Finish();
        }

        void PureRules(float bound)
        {
            foreach (bool p2 in new[] { false, true })
                foreach (var lv in new[] { Difficulty.Medium, Difficulty.Easy })
                {
                    string tag = $"{(p2 ? "below 50%" : "above 50%")} {lv}";
                    float beat = R.BeatTicks(p2) / (float)Sim.Hz;
                    void Esc(string what, float metres, float seconds) =>
                        Check($"escape {tag}: {what}", R.NeedSeconds(metres) <= seconds, $"{metres:0.0} m needs {R.NeedSeconds(metres):0.00} s, warning {seconds:0.00} s");
                    Esc("first Pallet Lock from the far edge", R.FirstLockWalk(bound), R.FirstLockBeats(p2, lv) * beat);
                    Esc("Pallet Lock swap, disc to disc", R.SwapLockWalk(), R.SwapLockBeats(p2, lv) * beat);
                    Esc("Escape Wheel spoke at the far edge", R.SpokeWalk(bound), R.VolleySeconds(1, p2, lv));
                    Esc("Counterpoise from the floor's edge", R.SoakWalk(bound), R.SoakBeats(p2, lv) * beat);
                    if (p2) Esc("Counterpoise second set", R.SecondSetWalk(), (R.SecondSetBeats + (lv == Difficulty.Easy ? 2 : 0)) * beat);
                    Esc("Lock and Release lane", R.LaneWalk(), R.LaneBeats(lv) * beat);
                    Esc("Lock and Release shock ring (after the lane)", R.ShockWalk(bound), (R.LaneBeats(lv) + R.ShockAfterBeats) * beat);
                    Esc("Lock and Release landing", R.LandWalk(), R.LeapBeats * beat);
                }
            Check("marks split evenly (1:1/0, 2:1/1, 3:2/1, 4:2/2)", R.BrassCount(1) == 1 && R.BrassCount(2) == 1 && R.BrassCount(3) == 2 && R.BrassCount(4) == 2);
            var s2 = R.SoakSpots(90, 0, 2); var s3 = R.SoakSpots(90, 0, 3); var s4 = R.SoakSpots(90, 0, 4); var s1 = R.SoakSpots(90, 0, 1);
            Check("Counterpoise numerals: I+I, II+I, II+II, solo one circle", s2.Select(x => x.need).SequenceEqual(new[] { 1, 1 }) && s3.Select(x => x.need).SequenceEqual(new[] { 2, 1 })
                && s4.Select(x => x.need).SequenceEqual(new[] { 2, 2 }) && s1.Count == 1 && s1[0].at == Vector3.zero);
            Check("one player alone in a circle survives from full HP, Hard x1.3 included", Mathf.RoundToInt(R.SoakDmg * 1.3f) < 100 && Mathf.RoundToInt(R.SoloSoakDmg * 1.3f) < 100,
                $"{R.SoakDmg} Medium / {Mathf.RoundToInt(R.SoakDmg * 1.3f)} Hard, solo {R.SoloSoakDmg} / {Mathf.RoundToInt(R.SoloSoakDmg * 1.3f)}");
            Check("Counterpoise circles inside the floor", R.SoakOut + R.SoakRadius <= bound && R.SoakSideOut + R.SoakRadius <= bound);
            Check("twins inside the boss bound", R.Spacing <= CombatWorld.BossBound);
        }

        // one fight: n players, all Perfect bots (the test player too)
        IEnumerator PartyRun(int enc, int map, int n, Difficulty level, bool full)
        {
            Section($"{n} player{(n > 1 ? "s" : "")}, {level}{(full ? "" : " (short)")}");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d = RunDirector.Instance;
            Check($"{n}p: allies selected", d.SelectAllies(n - 1));
            Check($"{n}p: practice request accepted", d.StartPractice("Tollbreaker", enc));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "practice Ready");
            d = RunDirector.Instance;
            Check($"{n}p: Ready on the Brass Foundry with {n} players", d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.World.Players.Count == n,
                $"{d.World.Players.Count} players, map {RunMaps.Current}");
            d.Player.Input = new AllyBot(n - 1, n, BotMode.Perfect);
            hits.Clear(); spawned.Clear(); spawnTick.Clear(); downs = 0;
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            d.World.Hazards.Spawned -= OnSpawn; d.World.Hazards.Spawned += OnSpawn;
            Check($"{n}p: Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is EscapementTwinsEncounter, 3, "boss spawned");
            boss = d.Boss as EscapementTwinsEncounter;
            if (boss == null) yield break;
            d.World.Difficulty = level;
            EscapementTwinsHud.ShowDiagnostics = true;
            yield return Until(() => d.State == RunState.Combat, 8, "combat");
            Check($"{n}p: two bodies, one bar: Tock is a live boss part", d.World.Parts.Contains(boss.TockPart) && boss.TockPart.Alive);
            if (n == 2 && full)
            {
                RenderShot("twins_placeholder", new Vector3(0, 6f, 13f), new Vector3(0, 1.8f, 0), 45);
                int hp0 = boss.Hp; int dealt = d.World.DamagePart(boss.TockPart, 25, boss.TockPos + Vector3.up, "test");
                Check("hitting Tock damages the shared bar", dealt == 25 && boss.Hp == hp0 - 25, $"dealt {dealt}, {hp0} -> {boss.Hp}");
                Check("fight opens in phase 1 at 100 BPM, no ability text", !boss.Phase2 && Mathf.Approximately(boss.Bpm, 100) && boss.PhaseName == "" && boss.Lesson == "");
            }

            if (level == Difficulty.Hard)
            {
                yield return Block(EscapementTwinsEncounter.LockRelease);
                Check("Hard: Recoil lanes spawn with the landing circles (2)", SpawnedInBlock().Count(s => s == "Lock and Release (recoil)") == 2);
                Check("Hard: Lock and Release resolves and leaves nothing", d.World.Hazards.Count == 0);
            }
            else
            {
                foreach (var atk in EscapementTwinsEncounter.Rotation(false)) yield return AbilityChecks(atk, n, full);
                if (full)
                {
                    // the 50% shift
                    boss.DebugSetHpFraction(0.49f);
                    yield return Until(() => boss.Phase2, 60, "the 50% shift");
                    Check($"{n}p: the shift is harmless and keeps the bar (no heal)", boss.ShiftEnd - boss.ShiftStart >= R.ShiftTicks - 1 && boss.Hp <= boss.HpAtShiftStart && Mathf.Approximately(boss.Bpm, 120),
                        $"{boss.ShiftStart}->{boss.ShiftEnd}, HP {boss.HpAtShiftStart}->{boss.Hp}, {boss.Bpm} BPM");
                    foreach (var atk in EscapementTwinsEncounter.Rotation(true)) yield return AbilityChecks(atk, n, full);
                }
            }

            Note($"{n}p hits taken by source: " + string.Join(", ", hits.GroupBy(h => h.source).Select(g => g.Key + " " + g.Count())));
            Check($"{n}p: nobody downed (bots did every mechanic)", downs == 0 && d.World.Players.All(p => !p.Down), $"{downs} downs");
            // defeat clears everything in the killing tick
            yield return Until(() => boss.Current.StartsWith("Pallet Lock") || boss.Current.StartsWith("Escape") || boss.Current.StartsWith("Counter") || boss.Current.StartsWith("Lock") || boss.Current == "between blocks", 30, "boss busy for the defeat check");
            boss.DebugDefeat();
            yield return null;
            Check($"{n}p: defeat clears hazards, the Tock part and the head markers at once", d.World.Hazards.Count == 0 && d.World.Parts.Count == 0 && boss.MarkedCount == 0
                && d.World.Players.All(p => !p.Marks.Contains(MechanicIcons.TeamBrass) && !p.Marks.Contains(MechanicIcons.TeamCobalt)));
            yield return Until(() => d.State == RunState.Results, 10, "results");
            EscapementTwinsHud.ShowDiagnostics = false;
            d.World.PlayerHit -= OnHit; d.World.Hazards.Spawned -= OnSpawn;
        }

        // forces atk as the next block; onStart runs on the first frame it is under way (counters snapshot there: the block
        // before it has ended, and nothing of this one resolves inside a second); returns once it ends
        int blockStart;
        IEnumerator Block(string atk, Action onStart = null, float timeout = 90f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            var entry = boss.BlockLog.Skip(before).FirstOrDefault(s => s.EndsWith(" " + atk));
            blockStart = entry != null && int.TryParse(entry.Split(' ')[0], out int bt) ? bt : d.World.Tick;
            onStart?.Invoke();
            bool shot = false; var name = atk;
            float t = 0;
            while (!(boss.Current == "between blocks" || boss.Current == "turning") && !boss.Dead && t < timeout)
            {
                t += Time.deltaTime; Keep(); CountDowns();
                if (!shot && d.World.Hazards.Count > 0 && d.World.Hazards.Live.Any(h => h.Age > h.ResolveAt / 2)) { shot = true; yield return Snap($"{(boss.Phase2 ? "p2" : "p1")}_{name}_{d.World.Players.Count}p"); }
                yield return null;
            }
            if (t >= timeout) Check(atk + " ends", false, $"timed out after {timeout}s, at '{boss.Current}'");
        }
        List<string> SpawnedInBlock() { var l = new List<string>(); for (int i = 0; i < spawned.Count; i++) if (spawnTick[i] >= blockStart) l.Add(spawned[i]); return l; }
        readonly HashSet<PlayerController> wasDown = new HashSet<PlayerController>();
        void CountDowns() { foreach (var p in d.World.Players) { if (p.Down && wasDown.Add(p)) downs++; else if (!p.Down) wasDown.Remove(p); } }

        IEnumerator AbilityChecks(string atk, int n, bool full)
        {
            bool p2 = boss.Phase2; string ph = p2 ? "P2" : "P1";
            int t0 = 0, locks = 0, swaps = 0, vol = 0, soaks = 0, empty = 0, punish = 0, lockLog = 0, volLog = 0, soakLog = 0;
            yield return Block(atk, () =>
            {
                t0 = blockStart; locks = boss.LocksResolved; swaps = boss.Swaps; vol = boss.VolleysResolved; soaks = boss.SoaksResolved; empty = boss.SoakEmpty;
                punish = boss.PunishLog.Count; lockLog = boss.LockLog.Count; volLog = boss.VolleyLog.Count; soakLog = boss.SoakLog.Count;
            });
            Check($"{n}p {ph} {atk}: nothing left on the floor after it", d.World.Hazards.Count == 0, $"{d.World.Hazards.Count} live");
            var mine = SpawnedInBlock();
            switch (atk)
            {
                case EscapementTwinsEncounter.Pallet:
                {
                    int want = R.Locks(p2);
                    var log = boss.LockLog.Where(l => l.tick >= t0).ToList();   // logged on the block's first tick
                    Check($"{n}p {ph} Pallet Lock: {want} locks resolve, {want - 1} swaps", boss.LocksResolved - locks == want && boss.Swaps - swaps == want - 1 && mine.Count == 2 * want,
                        $"{boss.LocksResolved - locks} locks, {boss.Swaps - swaps} swaps, {mine.Count} rings");
                    Check($"{n}p {ph} Pallet Lock: marks split {R.BrassCount(n)} brass / {n - R.BrassCount(n)} cobalt, then flip",
                        log.Count == want && log[0].brass == R.BrassCount(n) && log[0].cobalt == n - R.BrassCount(n) && (want < 2 || log[1].brass == log[0].cobalt),
                        string.Join("; ", log.Select(l => $"lock {l.lockNo}: {l.brass}/{l.cobalt}")));
                    Check($"{n}p {ph} Pallet Lock: markers removed afterwards", boss.MarkedCount == 0);
                    Note($"Pallet Lock hits on the bots: {HitsSince(t0, "Pallet Lock")}");
                    break;
                }
                case EscapementTwinsEncounter.Wheel:
                {
                    int want = R.Volleys(p2);
                    var log = boss.VolleyLog.Where(l => l.tick >= t0).ToList();
                    Check($"{n}p {ph} Escape Wheel: {want} volleys of {R.Spokes} spokes resolve, alternating Tick/Tock", boss.VolleysResolved - vol == want && mine.Count == want * R.Spokes
                        && log.Count == want && log.Select((l, i) => l.tick_ == (i % 2 == 0)).All(x => x), $"{boss.VolleysResolved - vol} volleys, {mine.Count} spokes");
                    Note($"Escape Wheel hits on the bots: {HitsSince(t0, "Escape Wheel")}");
                    break;
                }
                case EscapementTwinsEncounter.Counter:
                {
                    int sets = R.Sets(p2), circles = n <= 1 ? sets : 2 * sets;
                    var log = boss.SoakLog.Where(l => l.tick >= t0).ToList();
                    Check($"{n}p {ph} Counterpoise: {circles} circle(s) resolve, none empty (no wipe)", boss.SoaksResolved - soaks == circles && boss.SoakEmpty == empty,
                        string.Join("; ", log.Select(l => $"set {l.set} need {l.need} got {l.inside}")));
                    Check($"{n}p {ph} Counterpoise: every circle soaked by at least its numeral", log.All(l => l.inside >= l.need));
                    break;
                }
                default:
                {
                    Check($"{n}p {ph} Lock and Release: lane, shock ring, punish window and two landings", boss.PunishLog.Count - punish == 1
                        && mine.Count(s => s == "Lock and Release (lane)") == 1 && mine.Count(s => s == "Lock and Release (shock)") == 1 && mine.Count(s => s == "Lock and Release (landing)") == 2,
                        string.Join(", ", mine.GroupBy(s => s).Select(g => g.Key + " " + g.Count())));
                    var w = boss.PunishLog[boss.PunishLog.Count - 1];
                    Check($"{n}p {ph} Lock and Release: punish window 3.5 s with nothing live", w.end - w.start >= Sim.Ticks(R.PunishSeconds) - 1, $"{(w.end - w.start) / (float)Sim.Hz:0.00} s");
                    Note($"Lock and Release hits on the bots: lane {boss.LaneHits}, shock {boss.ShockHits}, landing {boss.LandHits} (fight totals)");
                    break;
                }
            }
        }

        void Finish()
        {
            Check("no runtime errors", errors == 0, errors + " logged");
            lines.Insert(0, $"Escapement Twins autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "escapement-twins-report.txt"), lines);
            Debug.Log($"[escapement-twins-test] done: {pass} passed, {fail} failed");
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
