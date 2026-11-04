using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Silt Carillon inside the real game:  -siltCarillonTest -lastbellOut <folder>
    // 1. Pure rules: timings, bell counts and spots, and the escape checks (4.2 m/s after a 0.3 s reaction) for every
    //    warning at every level and tempo.
    // 2. Solo practice on Verdant Chimes with a scripted walker (ordinary PlayerInput): each ability hits a player who
    //    stands still next to the boss and one who stands still at long range, and a walker who reads it takes nothing;
    //    bells, Drained, Backwash (Hard), Easy timings, the 50% transformation, phase 2, and an empty bell hitting the party.
    // 3. The whole rotation of both phases with Perfect bot parties of 2 and 4 (the director's allies plus a bot player):
    //    every mechanic fires and resolves, every bell set is rung with the right numerals, nothing is left live between
    //    attacks, no bell is left empty, and defeat clears everything.
    // Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player DebugSetHp, direct placement, extra roster players.
    // Writes silt-carillon-report.txt plus screenshots and exits 0 / 1.
    public sealed class SiltCarillonAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-siltCarillonTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "silt-carillon-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<SiltCarillonAutoTest>()) return;
            var go = new GameObject("Silt Carillon autotest"); DontDestroyOnLoad(go); go.AddComponent<SiltCarillonAutoTest>();
        }

        static Vector2 ToMove(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld;
            public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward, Move = ToMove(MoveWorld) };
        }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true;
        SiltCarillonEncounter boss;

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
            lines.Add(line); Debug.Log("[silt-carillon-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[silt-carillon-test] " + s); }
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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 32, 14), new Vector3(0, 0, -0.5f), 50);
        // the game camera's own frame (HUD and bell numerals included)
        IEnumerator Grab(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(UnityEngine.Screen.width, UnityEngine.Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        Vector3? goal; Func<Vector3?> goalFn;
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 100) p.DebugSetHp(100);
            var me = d.Player;
            var g = goalFn != null ? goalFn() : goal;
            if (me && g.HasValue && me.Input == bot) { var to = Sim.Flat(g.Value - me.transform.position); bot.MoveWorld = to.magnitude > 0.15f ? to.normalized * Mathf.Min(1, to.magnitude / 0.4f) : Vector3.zero; }
            else bot.MoveWorld = Vector3.zero;
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void Place(PlayerController p, Vector3 at) { p.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); }
        void PlaceMe(Vector3 at) { Place(d.Player, at); goal = d.Player.transform.position; goalFn = null; d.Player.Input = bot; }
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));

        IEnumerator Block(string atk, float timeout = 60f)
        {
            yield return StartOf(atk, timeout);
            yield return BetweenBlocks(timeout);
        }
        IEnumerator StartOf(string atk, float timeout = 30f)
        {
            int before = boss.BlockLog.Count; boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
        }
        IEnumerator BetweenBlocks(float timeout = 60f) { yield return Until(() => boss.Current == "between blocks" || boss.Dead || d.State != RunState.Combat, timeout, "between blocks"); }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            int vmap = RunMaps.IndexOf(RunDirector.SylvaraMap);
            SiltCarillonRules.Bound = RunMaps.All[vmap].Bound;
            Note($"Verdant Chimes player bound {SiltCarillonRules.Bound} m");
            PureChecks();

            Section("registration and practice start");
            int enc = RunDirector.SiltCarillonPractice;
            Check("practice entry registered on Verdant Chimes, practice only, two phases", enc >= 0 && RunDirector.Bosses[enc].Map == "verdant-chimes" && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Phases == 2);
            Check("run order unchanged (4 run bosses, Silt Carillon excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            Check("Sylvara's practice entry still opens Verdant Chimes", RunDirector.SylvaraPractice >= 0 && RunDirector.Bosses[RunDirector.SylvaraPractice].Name.StartsWith("Sylvara"));
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[vmap].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }
            d = RunDirector.Instance;
            d.SelectAllies(0); d.SelectDifficulty(Difficulty.Medium);
            Check("practice request accepted", d.StartPractice("Tollbreaker", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 40) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Verdant Chimes", d && d.State == RunState.Ready && RunMaps.Current == vmap && d.Mode == RunMode.Practice && d.Encounter == enc);
            if (!d || d.State != RunState.Ready) { Finish(); yield break; }

            yield return SoloFight();
            yield return PartyFight(1);
            yield return PartyFight(3);
            Finish();
        }

        IEnumerator BeginFight(int seed)
        {
            goal = null; goalFn = null; hits.Clear();
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is SiltCarillonEncounter, 3, "boss spawned");
            boss = d.Boss as SiltCarillonEncounter;
            if (boss == null) yield break;
            boss.Seed = seed;
            d.World.Difficulty = Difficulty.Medium;   // a saved menu choice must not change the measured numbers
            SiltCarillonHud.ShowDiagnostics = false;
            yield return Until(() => d.State == RunState.Combat, 8, "combat");
        }

        // ---------------- pure rules ----------------
        void PureChecks()
        {
            var levels = new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard };
            Check("bells: solo I; 2 = I+I; 3 = II+I; 4 = II+II",
                SiltCarillonRules.BellNeeds(1).SequenceEqual(new[] { 1 }) && SiltCarillonRules.BellNeeds(2).SequenceEqual(new[] { 1, 1 }) &&
                SiltCarillonRules.BellNeeds(3).SequenceEqual(new[] { 2, 1 }) && SiltCarillonRules.BellNeeds(4).SequenceEqual(new[] { 2, 2 }));
            Check("bells: 30 per player wanted (I = 30, II = 60); a pair in II takes 30 each, one player alone in II takes 60 and survives from full on Hard",
                SiltCarillonRules.Share(1, 1) == 30 && SiltCarillonRules.Share(2, 2) == 30 && SiltCarillonRules.Share(2, 1) == 60 && SiltCarillonRules.Share(2, 3) == 20 &&
                Mathf.RoundToInt(SiltCarillonRules.BellTotal(2) * RunDifficulty.DamageTakenScale(Difficulty.Hard)) < 100);
            Check("an empty bell is a party-wide hit of 35; two empty bells together still leave a full-HP player standing on Hard",
                SiltCarillonRules.EmptyBellDmg == 35 && Mathf.RoundToInt(2 * SiltCarillonRules.EmptyBellDmg * RunDifficulty.DamageTakenScale(Difficulty.Hard)) < 100);
            var hi = SiltCarillonRules.BellSpots(false, 30, 2); var lo = SiltCarillonRules.BellSpots(true, 30, 2);
            Check("bells: High Tide pair opposite at 4.5 m (inside the dry disc); Crosscurrent pair in dry sectors at 6 m",
                hi.All(p => Mathf.Abs(p.magnitude - 4.5f) < 0.01f && p.magnitude + SiltCarillonRules.BellRadius < SiltCarillonRules.TideInner - Hazard.Margin) && Mathf.Abs(Vector3.Angle(hi[0], hi[1]) - 180) < 0.5f &&
                lo.All(p => Mathf.Abs(p.magnitude - 6f) < 0.01f && !SiltCarillonRules.InSet(p, 30, 0) && !SiltCarillonRules.InSet(p + Sim.Dir(Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg + 90) * SiltCarillonRules.BellRadius, 30, 0)));
            // every escape, both tempos, every level
            bool tide = true, cross = true, drop = true, peal = true, bell = true; string why = "";
            foreach (var lvl in levels)
                foreach (var (p2, bpm) in new[] { (false, 96f), (true, 116f) })
                {
                    if (!p2 && SiltCarillonRules.Reach(SiltCarillonRules.TideWarnBeats(lvl, false), bpm) < SiltCarillonRules.TideEscape) { tide = false; why += $" tide {lvl}"; }
                    if (p2)
                    {
                        float worst = 0;
                        for (float x = -13.5f; x <= 13.5f; x += 0.5f) for (float z = -13.5f; z <= 13.5f; z += 0.5f)
                        {
                            var q = new Vector3(x, 0, z); if (q.magnitude > SiltCarillonRules.Bound) continue;
                            foreach (int set in new[] { 0, 1 }) worst = Mathf.Max(worst, SiltCarillonRules.CrossEscape(q, 17f, set));
                        }
                        if (SiltCarillonRules.Reach(SiltCarillonRules.TideWarnBeats(lvl, true), bpm) < worst) { cross = false; why += $" cross {lvl} needs {worst:0.0}"; }
                    }
                    if (SiltCarillonRules.Reach(SiltCarillonRules.DropLeadBeats(lvl, p2), bpm) < SiltCarillonRules.DropEscape) { drop = false; why += $" drop {lvl} {bpm}"; }
                    if (SiltCarillonRules.Reach(SiltCarillonRules.PealGapBeats(lvl), bpm) < SiltCarillonRules.PealCross) { peal = false; why += $" peal {lvl} {bpm}"; }
                    // any player from anywhere to the farther bell (the pair's worst assignment)
                    var spots = SiltCarillonRules.BellSpots(p2, 0, 2); float far = 0;
                    for (float a = 0; a < 360; a += 5) for (float r = 0; r <= SiltCarillonRules.Bound; r += 0.5f) far = Mathf.Max(far, spots.Max(s => Sim.Flat(Sim.Dir(a) * r - s).magnitude) - SiltCarillonRules.BellRadius + 0.3f);
                    if (SiltCarillonRules.Reach(SiltCarillonRules.BellTollBeats(lvl, p2, false), bpm) < far) { bell = false; why += $" bell {lvl} {bpm} needs {far:0.0}"; }
                }
            Check($"Silt Tide: from the edge into the dry disc in time at 4.2 m/s ({SiltCarillonRules.TideEscape:0.0} m in 5 beats at 96 BPM)", tide, why);
            Check("Crosscurrent: from anywhere on the floor out of a flooding set in time at 4.2 m/s (4 beats at 116 BPM)", cross, why);
            Check($"Silt Fall: out of a locked drop in time at 4.2 m/s ({SiltCarillonRules.DropEscape:0.0} m; 1.5 beats at 96, 1.75 at 116)", drop, why);
            Check("Drowned Peal: across a band border into the band that just landed in time (1.5 beats, 116 BPM)", peal, why);
            Check("Sunken Bells: any player reaches the farther bell of a pair from anywhere in time (8 beats at 96, 10 at 116)", bell, why);
            Check("rotation: tide + bells, fall, peal, tide + peal, fall in both phases",
                SiltCarillonEncounter.Rotation(false).SequenceEqual(new[] { "TideBells", "Fall", "Peal", "TidePeal", "Fall" }) && SiltCarillonEncounter.Rotation(true).SequenceEqual(SiltCarillonEncounter.Rotation(false)));
            var ys = SiltCarillonRules.SectorYaws(0, 0); var ys1 = SiltCarillonRules.SectorYaws(0, 1);
            Check("Crosscurrent: three alternate 60-degree sectors, the other set fills the gaps",
                ys.SequenceEqual(new[] { 0f, 120f, 240f }) && ys1.SequenceEqual(new[] { 60f, 180f, 300f }) && SiltCarillonRules.InSet(Sim.Dir(10) * 8, 0, 0) && !SiltCarillonRules.InSet(Sim.Dir(10) * 8, 0, 1) && SiltCarillonRules.InSet(Sim.Dir(70) * 8, 0, 1));
        }

        // ---------------- solo ----------------
        IEnumerator SoloFight()
        {
            Section("solo fight (scripted walker)");
            d.Player.Input = bot;
            yield return BeginFight(20261103);
            if (boss == null) yield break;
            Check("fight opens in phase 1 at 96 BPM", !boss.Phase2 && Mathf.Approximately(boss.Bpm, 96) && boss.BeatTicks == 38);
            Check("placeholder stand-in body (no model) and no ability text", boss.Placeholder && boss.PhaseName == "" && boss.Lesson == "" && !SiltCarillonHud.ShowDiagnostics && boss.transform.Find("Silt Carillon (STAND-IN, labelled placeholder)"));
            Check("practice phase row matches the encounter (2 phases)", boss.PhaseStarts.Length == RunDirector.Bosses[RunDirector.SiltCarillonPractice].Phases);
            RenderShot("standin_front", new Vector3(0, 3.4f, 9.5f), new Vector3(0, 1.8f, 0), 40);

            // 1. the opening High Tide + Sunken Bells: the walker rings the lone bell
            Section("High Tide + Sunken Bells (solo)");
            int start = d.World.Tick;
            goalFn = () => boss.LiveBells.Count > 0 ? boss.LiveBells[0].h.Origin : (Vector3?)null;
            yield return Until(() => boss.LiveBells.Count > 0, 15, "bells surface");
            var first = boss.LiveBells.Count > 0 ? boss.LiveBells[0].h : null;
            Check("solo: one bell with numeral I, a shared soak of 30 that does not wipe", boss.LiveBells.Count == 1 && boss.LiveBells[0].need == 1 && first.ShareTotal == 30 && !first.WipeIfEmpty && first.Soak && first.ShareCount == 1);
            Check("the opening bell tolls 11 beats after it surfaces (8 + 3 for the first set)", first != null && first.ResolveAt == 11 * 38, first != null ? first.ResolveAt + " ticks" : "");
            Check("the High Tide floods the ring from 7 m to the edge (burn 25/s after 5 beats), drawn in the raid look", d.World.Hazards.Live.Any(h => h.Source == "Silt Tide" && h.Shape == HazardShape.Ring && Mathf.Approximately(h.Inner, 7) && h.BurnPerSecond == 25 && h.LockAt == 190 && h.FillToLock));
            Check("no ability names in the boss panel: no live hazard carries a mechanic tag", d.World.Hazards.Live.All(h => string.IsNullOrEmpty(h.MechanicId)));
            yield return Wait(1.5f);
            Overhead("solo_high_tide_bell");
            yield return Grab("solo_high_tide_bell_camera");
            yield return Until(() => boss.Drained, 15, "Drained");
            Check("the bell rung: the tide drains at once and it is Drained with nothing live", boss.BellSetLog.Count == 1 && boss.BellSetLog[0].allRung && boss.DrainLog.Any(x => x.early) && d.World.Hazards.Count == 0, $"{d.World.Hazards.Count} live");
            Check("the lone ringer takes the bell's 30", HitsSince(start, "Sunken Bell", d.Player) == 1 && hits.Last(h => h.source == "Sunken Bell").dmg == 30);
            int hp = boss.Hp; boss.TakeHit(50, boss.transform.position + Vector3.up * 2);
            Check("Drained: it can be damaged (the punish window)", boss.Hp == hp - 50);
            RenderShot("solo_drained", new Vector3(0, 5, 9), new Vector3(0, 1.5f, 0), 45);
            goalFn = null;
            yield return BetweenBlocks(20);
            Check("Drained lasts 3.5 s", boss.DrainedLog.Count == 1 && boss.DrainedLog[0].end - boss.DrainedLog[0].start == SiltCarillonEncounter.DrainedTicks);
            Check("the walker who stayed inside 7 m and rang the bell took no mud", HitsSince(start, "Silt Tide", d.Player) == 0);

            // 2. Silt Fall: a still hugger and a still far player are hit; a walker circling at 9 m is not
            Section("Silt Fall (solo + two still dummies)");
            var near = AddExtra(new Vector3(0, 0, 2.6f)); var far = AddExtra(new Vector3(-8.5f, 0, -9.5f));
            PlaceMe(new Vector3(9, 0, 0)); start = d.World.Tick; int ds = boss.DropsSpawned;
            goalFn = () => { var p = Sim.Flat(d.Player.transform.position); float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; return Sim.Dir(a + 35) * 9f; };
            yield return StartOf("Fall");
            yield return Wait(2.5f);
            Overhead("solo_silt_fall");
            yield return BetweenBlocks(30);
            goalFn = null;
            Check("four drops for each of the 3 standing players", boss.DropsSpawned - ds == 12, $"{boss.DropsSpawned - ds}");
            Check("standing still next to it: hit (melee)", HitsSince(start, "Silt Fall", near) >= 1);
            Check("standing still at long range: hit (ranged)", HitsSince(start, "Silt Fall", far) >= 1);
            Check("a player who keeps walking takes no drop and no mound", HitsSince(start, "Silt Fall", d.Player) == 0 && HitsSince(start, "Silt Mound", d.Player) == 0,
                $"{HitsSince(start, "Silt Fall", d.Player)} drops, {HitsSince(start, "Silt Mound", d.Player)} mound ticks");
            Check("every drop leaves a mound and every mound sets; nothing is left live", boss.MoundsSpawned == boss.DropsLanded && boss.MoundsSet == boss.MoundsSpawned && boss.FallLive == 0 && d.World.Hazards.Count == 0);

            // 3. Drowned Peal (outgoing)
            Section("Drowned Peal (solo + dummies)");
            Place(near, new Vector3(0, 0, 2.6f)); Place(far, new Vector3(-8.5f, 0, -9.5f));
            PlaceMe(new Vector3(5.6f, 0, 0)); start = d.World.Tick; int pb = boss.PealBands;
            goalFn = () => boss.PealBands - pb >= 1 ? new Vector3(2.6f, 0, 0) : new Vector3(5.6f, 0, 0);
            yield return StartOf("Peal");
            yield return Wait(1.6f);
            Overhead("solo_peal");
            yield return BetweenBlocks(20);
            goalFn = null;
            var pl = boss.PealLog.Last();
            Check("three bands, outgoing (A, B, C), first after 4 beats then every 1.5 beats", boss.PealBands - pb == 3 && !pl.ebb && pl.first == 152 && pl.gap == 57);
            Check("standing still next to it: band A hits (melee)", HitsSince(start, "Drowned Peal A", near) == 1);
            Check("standing still at long range: band C hits (ranged)", HitsSince(start, "Drowned Peal C", far) == 1);
            Check("the walker who steps into the band that just landed takes nothing", HitsSince(start, "Drowned Peal", d.Player) == 0);

            // 4. High Tide + Drowned Peal: the far dummy stands in the mud
            Section("High Tide + Drowned Peal (solo + dummies)");
            Place(near, new Vector3(0, 0, 2.6f)); Place(far, new Vector3(-8.5f, 0, -9.5f));
            PlaceMe(new Vector3(0, 0, -12)); start = d.World.Tick; pb = boss.PealBands;
            goalFn = () => boss.PealBands - pb >= 1 ? new Vector3(2.6f, 0, 0) : new Vector3(5.6f, 0, 0);
            yield return StartOf("TidePeal");
            yield return Wait(4.5f);
            Overhead("solo_tide_peal");
            yield return BetweenBlocks(30);
            goalFn = null;
            Check("standing still at long range: the mud burns (the edge is punished)", HitsSince(start, "Silt Tide", far) >= 5);
            Check("standing still next to it: band A hits", HitsSince(start, "Drowned Peal A", near) == 1);
            Check("the walker who comes in from the edge and dances the peal takes nothing", HitsSince(start, "Silt Tide", d.Player) == 0 && HitsSince(start, "Drowned Peal", d.Player) == 0);
            Check("the tide ebbs by itself; nothing is left live", boss.TideLive == 0 && d.World.Hazards.Count == 0 && boss.DrainLog.Last().early == false);
            RemoveExtras();

            // 5. difficulty
            Section("difficulty");
            d.World.Difficulty = Difficulty.Hard;
            int bw = boss.BackwashCount, dw = boss.DrainedLog.Count;
            goalFn = () => boss.LiveBells.Count > 0 ? boss.LiveBells[0].h.Origin : (Vector3?)null;
            yield return Block("TideBells", 40);
            goalFn = null;
            Check("Hard adds Backwash: the drained mud strikes once 1.5 beats later", boss.BackwashCount == bw + 1, $"{boss.BackwashCount - bw}");
            Check("Hard: the Drained window still opens with nothing live and lasts 3.5 s", boss.DrainedLog.Count == dw + 1 && boss.DrainedLog.Last().end - boss.DrainedLog.Last().start == SiltCarillonEncounter.DrainedTicks);
            d.World.Difficulty = Difficulty.Easy;
            PlaceMe(new Vector3(9, 0, 0));
            goalFn = () => { var p = Sim.Flat(d.Player.transform.position); float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; return Sim.Dir(a + 35) * 9f; };
            yield return Block("Fall");
            goalFn = null;
            Check("Easy: drops lock 2 beats before landing (half a beat more)", boss.FallLog.Last().lead == 76, $"{boss.FallLog.Last().lead} ticks");
            d.World.Difficulty = Difficulty.Medium;

            // 6. the 50% transformation and phase 2
            Section("the 50% transformation and phase 2");
            PlaceMe(new Vector3(9, 0, 0));
            goalFn = () => { var p = Sim.Flat(d.Player.transform.position); float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; return Sim.Dir(a + 35) * 9f; };
            yield return StartOf("Fall");
            boss.DebugSetHpFraction(0.505f);
            boss.TakeHit(100, boss.transform.position + Vector3.up * 2);
            Check("crossing 50% mid-attack: the attack carries on", !boss.Phase2 && boss.ShiftStart < 0 && boss.Current == "Silt Fall");
            yield return Until(() => boss.ShiftStart >= 0, 30, "transformation starts");
            goalFn = null;
            Check("the transformation waits for the attack to finish", boss.BlockLog.Last().EndsWith("Shift"));
            hp = boss.Hp; boss.TakeHit(60, boss.transform.position + Vector3.up * 2);
            Check("it stays damageable during the transformation (no heal)", boss.Hp == hp - 60 && boss.Hp < boss.HpAtShiftStart);
            Check("the transformation is harmless (no hazards)", d.World.Hazards.Count == 0, $"{d.World.Hazards.Count} live");
            // the first phase 2 block is chosen the tick the shift ends: Crosscurrent + bells, the walker heads for the bell
            boss.ForcedNext = "TideBells"; int before = boss.BlockLog.Count;
            PlaceMe(new Vector3(0, 0, 2.5f));
            goalFn = () => boss.LiveBells.Count > 0 ? boss.LiveBells[0].h.Origin : (Vector3?)null;
            start = d.World.Tick; int tl = boss.TideLog.Count, dr = boss.DrainedLog.Count;
            yield return Until(() => boss.Phase2, 5, "phase 2");
            Check("2 s transformation, then 116 BPM", boss.ShiftEnd - boss.ShiftStart == SiltCarillonEncounter.ShiftTicks && Mathf.Approximately(boss.Bpm, 116) && boss.BeatTicks == 31);
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" TideBells")), 10, "phase 2 opens with the tide and bells");
            yield return Wait(1.5f);
            Overhead("solo_crosscurrent_bell");
            yield return BetweenBlocks(30);
            goalFn = null;
            var tides = boss.TideLog.Skip(tl).ToList();
            Check("below 50%: Crosscurrent (three sectors) with the bell in a dry sector; rung, drained, Drained", tides.Count == 1 && tides[0].cross && boss.BellSetLog.Last().allRung && boss.DrainedLog.Count == dr + 1);
            Check("the bell walker takes no mud", HitsSince(start, "Silt Tide", d.Player) == 0);

            // Ebb peal: C first
            PlaceMe(new Vector3(0, 0, 7.5f)); start = d.World.Tick; pb = boss.PealBands;
            goalFn = () => boss.PealBands - pb >= 1 ? new Vector3(0, 0, 11.5f) : new Vector3(0, 0, 7.5f);
            yield return Block("Peal");
            goalFn = null;
            Check("below 50% the Peal ebbs (C, B, A)", boss.PealLog.Last().ebb && boss.PealBands - pb == 3);
            Check("the walker who steps out into C after it lands takes nothing", HitsSince(start, "Drowned Peal", d.Player) == 0);

            // Crosscurrent + Peal: two sets
            tl = boss.TideLog.Count;
            PlaceMe(new Vector3(0, 0, 3)); start = d.World.Tick;
            yield return Block("TidePeal", 40);
            var cp = boss.TideLog.Skip(tl).ToList();
            Check("Crosscurrent + Peal: one set, then the other three sectors", cp.Count == 2 && cp[0].set == 0 && cp[1].set == 1 && cp.All(x => x.cross));
            Check("nothing is left live after the tide", d.World.Hazards.Count == 0 && boss.TideLive == 0);

            ds = boss.DropsSpawned; PlaceMe(new Vector3(9, 0, 0));
            goalFn = () => { var p = Sim.Flat(d.Player.transform.position); float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; return Sim.Dir(a + 35) * 9f; };
            yield return Block("Fall");
            goalFn = null;
            Check("below 50%: five drops, locking 1.75 beats before they land", boss.DropsSpawned - ds == 5 && boss.FallLog.Last().lead == Mathf.RoundToInt(1.75f * 31));

            // 7. an empty bell: a party-wide hit, the tide stays, no Drained window (review, 3 Nov 2026)
            Section("an empty bell");
            PlaceMe(new Vector3(0, 0, 3.0f)); start = d.World.Tick; int empty0 = boss.BellsEmpty, dr2 = boss.DrainedLog.Count, sets0 = boss.BellSetLog.Count;
            // stay inside the dry ground but clear of every bell
            goalFn = () => { foreach (var b in boss.LiveBells) { var away = -Sim.Flat(b.h.Origin).normalized * 3f; return away; } return null; };
            yield return Block("TideBells", 40);
            goalFn = null;
            Check("nobody in the bell: everyone standing takes the 35 Drowned Toll, nobody is downed", boss.BellsEmpty == empty0 + 1 && HitsSince(start, "Drowned Toll", d.Player) == 1 && hits.Last(h => h.source == "Drowned Toll").dmg == 35 && !d.Player.Down && d.State == RunState.Combat);
            Check("an unrung set does not drain the tide: no Drained window", boss.BellSetLog.Count == sets0 + 1 && !boss.BellSetLog.Last().allRung && boss.DrainedLog.Count == dr2);
        }

        // ---------------- bot parties ----------------
        IEnumerator PartyFight(int allies)
        {
            int size = allies + 1;
            Section($"party of {size}: Perfect bots through both phases");
            if (d.State != RunState.Menu) { if (d.State != RunState.Results) d.SetPaused(true); d.ReturnToMenu(); }
            yield return Until(() => d.State == RunState.Menu, 5, "menu");
            d.SelectAllies(allies); d.SelectDifficulty(Difficulty.Medium);
            var me = new AllyBot(allies, size, BotMode.Perfect);
            d.InputOverride = me;
            Check($"party of {size}: practice accepted", d.StartPractice("Arcanist", RunDirector.SiltCarillonPractice));
            yield return Until(() => d.State == RunState.Ready, 10, "ready");
            if (d.Player) d.Player.Input = me;
            yield return BeginFight(7000 + size);
            if (boss == null) yield break;
            Check($"party of {size}: {size} players (the player plus {allies} bot allies)", d.World.Players.Count == size, $"{d.World.Players.Count}");
            yield return BetweenBlocks(45);   // the opening tide and bells (the first set, 3 beats longer)
            Check($"party of {size}: the opening bells are rung", boss.BellSetLog.Count == 1 && boss.BellSetLog[0].allRung && d.World.Players.All(p => !p.Down));
            int errs0 = errors, startTick = d.World.Tick;
            var expect = SiltCarillonRules.BellNeeds(size);
            foreach (bool p2 in new[] { false, true })
            {
                if (p2) boss.DebugSetHpFraction(0.49f);   // the shift comes first, then the forced block
                int k = 0;
                foreach (var atk in SiltCarillonEncounter.Rotation(p2))
                {
                    k++;
                    int bl = boss.BellSetLog.Count, dr = boss.DrainedLog.Count, ds = boss.DropsSpawned, pb = boss.PealBands, tl = boss.TideLog.Count, ms = boss.MoundsSet;
                    yield return StartOf(atk, 40);
                    if (p2 && k == 1) Check($"party of {size}: the 50% shift, then phase 2 at 116 BPM", boss.Phase2 && boss.ShiftStart >= 0 && Mathf.Approximately(boss.Bpm, 116));
                    yield return Wait(atk == "Fall" ? 2.2f : 3.2f);
                    if (k <= 4) Overhead($"party{size}_{(p2 ? "p2" : "p1")}_{atk}");
                    yield return BetweenBlocks(45);
                    string tag = $"party of {size}, {(p2 ? "phase 2" : "phase 1")}, {atk}";
                    bool down = d.World.Players.Any(p => p.Down);
                    switch (atk)
                    {
                        case "TideBells":
                            var set = boss.BellSetLog.Count > bl ? boss.BellSetLog[bl] : default;
                            Check($"{tag}: the bells surface with numerals {string.Join("+", expect)}, all are rung, the tide drains and it is Drained",
                                boss.BellSetLog.Count == bl + 1 && set.needs.SequenceEqual(expect) && set.allRung && boss.DrainedLog.Count == dr + 1 && boss.TideLog.Count == tl + 1 && boss.TideLog[tl].cross == p2,
                                set.inside != null ? "inside " + string.Join("+", set.inside) : "no set");
                            break;
                        case "Fall":
                            int per = p2 ? SiltCarillonRules.DropsLow : SiltCarillonRules.DropsHigh;
                            Check($"{tag}: {per} drops for each of the {size} players land and every mound sets", boss.DropsSpawned - ds == per * size && boss.MoundsSet - ms == per * size && boss.FallLive == 0,
                                $"{boss.DropsSpawned - ds} drops, {boss.MoundsSet - ms} mounds");
                            break;
                        case "Peal":
                            Check($"{tag}: three bands land ({(p2 ? "ebbing" : "outgoing")})", boss.PealBands - pb == 3 && boss.PealLog.Last().ebb == p2);
                            break;
                        default:
                            Check($"{tag}: the tide floods and ebbs ({(p2 ? "two Crosscurrent sets" : "High Tide")}) and the three bands land",
                                boss.TideLog.Count - tl == (p2 ? 2 : 1) && boss.PealBands - pb == 3 && boss.TideLive == 0);
                            break;
                    }
                    Check($"{tag}: nothing left live, nobody down", d.World.Hazards.Count == 0 && !down && d.State == RunState.Combat, $"{d.World.Hazards.Count} live, state {d.State}");
                    if (d.State != RunState.Combat) yield break;
                }
            }
            var mine = hits.Where(h => h.tick >= startTick).GroupBy(h => h.source).Select(g => $"{g.Key} {g.Count()}").ToList();
            Note($"party of {size}: hits taken by source (bots, HP topped up): {(mine.Count == 0 ? "none" : string.Join(", ", mine))}");
            Check($"party of {size}: no runtime errors during the fight", errors == errs0, $"{errors - errs0}");
            // defeat mid Silt Fall: drops, mounds and telegraphs cleared at once
            yield return StartOf("Fall");
            yield return Until(() => boss.FallLive > 0 && d.World.Hazards.Count > 0, 8, "drops live");
            boss.DebugDefeat();
            yield return null;
            Check($"party of {size}: defeat clears every drop, mound and telegraph at once", d.World.Hazards.Count == 0 && boss.FallLive == 0 && boss.LiveBells.Count == 0);
            yield return Until(() => d.State == RunState.Results, 10, "results");
            d.InputOverride = null; d.SelectAllies(0);
        }

        // ---------------- helpers ----------------
        PlayerController AddExtra(Vector3 at)
        {
            var p = PlayerController.Spawn(d.World, RunDirector.Classes[(extras.Count + 1) % 3], d.World.Constrain(at, PlayerController.Radius), 180);
            p.Input = new Bot(); p.AbilitiesEnabled = false; extras.Add(p); return p;
        }
        void RemoveExtras()
        {
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
        }

        void Finish()
        {
            Check("no runtime errors", errors == 0, errors + " logged");
            lines.Insert(0, $"Silt Carillon autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "silt-carillon-report.txt"), lines);
            Debug.Log($"[silt-carillon-test] done: {pass} passed, {fail} failed");
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
