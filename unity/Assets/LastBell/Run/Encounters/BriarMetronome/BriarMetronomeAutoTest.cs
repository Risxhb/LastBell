using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using R = LastBell.Run.BriarMetronomeRules;

namespace LastBell.Run
{
    // Acceptance run for the Briar Metronome inside the real game:  -briarMetronomeTest -lastbellOut <folder>
    // 1. Pure rules: seasons, the cut chain, every escape window at 4.2 m/s after a 0.3 s reaction, the inner-path route.
    // 2. Registration (practice only, Verdant Chimes, Minibosses row, -lastbellPractice briar).
    // 3. Solo, standing still next to it and at long range: who each ability hits (content rules §5 melee/ranged danger).
    // 4. Bot parties of 2 and 4 (AllyBot, Perfect: the menu allies' mode): every ability in both phases fires and resolves,
    //    grafts are cut by their own season, the Rooted window opens with nothing live, nothing is left over, defeat
    //    clears everything. Labelled dev hooks: ForcedNext, boss HP fraction / defeat, player HP top-up (damage is still
    //    counted per source), direct player placement. Writes briar-metronome-report.txt plus screenshots, exits 0 / 1.
    public sealed class BriarMetronomeAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-briarMetronomeTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "briar-metronome-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<BriarMetronomeAutoTest>()) return;
            var go = new GameObject("Briar Metronome autotest"); DontDestroyOnLoad(go); go.AddComponent<BriarMetronomeAutoTest>();
        }

        static Vector2 ToMove(Vector3 world)
        {
            var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, fwd)), 1);
        }
        sealed class Still : IInputSource
        {
            public Vector3 MoveWorld;
            public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + who.transform.forward, Move = ToMove(MoveWorld) };
        }

        RunDirector d; readonly Still still = new Still();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly List<(int tick, string source, PlayerController who, int dmg)> hits = new List<(int, string, PlayerController, int)>();
        string dir; bool keepAlive = true; int enc;
        BriarMetronomeEncounter boss;

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
            lines.Add(line); Debug.Log("[briar-metronome-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[briar-metronome-test] " + s); }
        void Section(string s) { lines.Add(""); lines.Add("== " + s); }

        // in-game frames for the animation critic (9 Nov): each clip once, at its strike frame, from the gameplay and a close camera
        static readonly Dictionary<string, float> StrikeAt = new Dictionary<string, float>
        {
            ["CutIn"] = 0.75f, ["CutOut"] = 0.75f, ["CutAcross"] = 0.75f, ["BladePlant"] = 0.6f, ["MantleShake"] = 0.5f,
            ["OrbThrow"] = 0.35f, ["PhaseShift"] = 1.0f, ["Defeat"] = 2.3f,
        };
        IEnumerator ClipFrames(CharacterPresenter pr, Transform t)
        {
            var seen = new HashSet<string>();
            while (pr && t && seen.Count < 17)
            {
                string c = pr.CurrentClip;
                if (c != "-" && seen.Add(c))
                {
                    yield return new WaitForSeconds(StrikeAt.TryGetValue(c, out var s) ? s : 0.4f);
                    if (!pr || !t) yield break;
                    var b = t.position;
                    RenderShot($"clip_{c}_game", b + new Vector3(0, 11f, -14f), b + Vector3.up * 1.4f, 40);
                    RenderShot($"clip_{c}_close", b + t.forward * 7f + Vector3.up * 2.6f, b + Vector3.up * 1.8f, 40);
                }
                yield return null;
            }
        }

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
        void Overhead(string name) => RenderShot(name, new Vector3(0, 30, 13), new Vector3(0, 0, -0.5f), 52);
        IEnumerator GameShot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }
        Vector3? goal;
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 100) p.DebugSetHp(100);
            var me = d.Player;
            if (me && me.Input == still && goal.HasValue) { var to = Sim.Flat(goal.Value - me.transform.position); still.MoveWorld = to.magnitude > 0.15f ? to.normalized * Mathf.Min(1, to.magnitude / 0.4f) : Vector3.zero; }
            else still.MoveWorld = Vector3.zero;
        }
        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, p, dmg));
        void PlaceMe(Vector3 at) { d.Player.transform.position = d.World.Constrain(new Vector3(at.x, 0, at.z), PlayerController.Radius); goal = d.Player.transform.position; d.Player.Input = still; }
        int HitsSince(int tick, string prefix, PlayerController who = null) => hits.Count(h => h.tick >= tick && h.source.StartsWith(prefix) && (who == null || h.who == who));

        IEnumerator Block(string atk, float timeout = 90f)
        {
            int before = boss.BlockLog.Count;
            boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, timeout, atk + " ends");
        }
        IEnumerator StartOf(string atk, float timeout = 30f)
        {
            int before = boss.BlockLog.Count; boss.ForcedNext = atk;
            yield return Until(() => boss.BlockLog.Skip(before).Any(s => s.EndsWith(" " + atk)), timeout, atk + " starts");
        }

        // ======================================================================================================
        IEnumerator Main()
        {
            Section("pure rules");
            yield return new WaitForSeconds(1f);
            R.Bound = RunMaps.All[RunMaps.IndexOf(RunDirector.SylvaraMap)].Bound;
            Note($"Verdant Chimes player bound {R.Bound} m; beats {Mathf.RoundToInt(3600 / BriarMetronomeEncounter.BpmHigh)} / {Mathf.RoundToInt(3600 / BriarMetronomeEncounter.BpmLow)} ticks");
            PureSeasons(); PureChain(); PureEscapes(); PureRoute();

            Section("registration and practice start");
            enc = RunDirector.BriarMetronomePractice;
            int map = RunMaps.IndexOf(RunDirector.SylvaraMap);
            Check("practice entry on Verdant Chimes, practice only, 2 phases", enc >= 0 && RunDirector.Bosses[enc].Map == RunDirector.SylvaraMap && !RunDirector.Bosses[enc].InRun && RunDirector.Bosses[enc].Phases == 2);
            Check("Sylvara keeps her practice entry (the map lookup still finds her)", RunDirector.Bosses[RunDirector.SylvaraPractice].Name.StartsWith("Sylvara"));
            Check("run order unchanged (Briar Metronome excluded)", !RunDirector.RunOrder.Contains(enc) && RunDirector.RunOrder.Length == 4);
            Check("listed in the Minibosses row", LastBell.Menu.TestRushSetup.MinibossRow.Contains(enc) && !LastBell.Menu.TestRushSetup.BossRow.Contains(enc));
            Check("-lastbellPractice briar matches exactly one boss", RunDirector.Bosses.Count(b => b.Name.ToLowerInvariant().Replace(" ", "").Contains("briar")) == 1);
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            float waitMenu = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) && waitMenu < 30) { waitMenu += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) { Check("run scene loads", false, "no RunDirector after 30 s"); Finish(); yield break; }
            d = RunDirector.Instance;
            d.SelectAllies(0);
            Check("practice request accepted", d.StartPractice("Tollbreaker", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 40) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Verdant Chimes", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc);
            yield return BeginFight(20261103, false);
            Check("fight opens in phase 1 at 108 BPM (33-tick beat), no ability text",
                !boss.Phase2 && boss.BeatTicks == 33 && boss.PhaseName == "" && boss.Lesson == "" && !BriarMetronomeHud.ShowDiagnostics);
            if (boss.Presenter && boss.Presenter.HasModel)
            {
                var missing = BriarMetronomeEncounter.ClipNames.Where(c => !boss.Presenter.Has(c)).ToList();
                Check($"the rigged model is loaded with all {BriarMetronomeEncounter.ClipNames.Length} clips", !boss.Placeholder && missing.Count == 0, missing.Count > 0 ? "missing: " + string.Join(", ", missing) : "");
                StartCoroutine(ClipFrames(boss.Presenter, boss.transform));
            }
            else Check("no rigged model in this build: the labelled stand-in is used (Placeholder)", boss.Placeholder && GameObject.Find("Briar Metronome (STAND-IN, labelled placeholder)"));
            yield return Until(() => Sim.Flat(boss.transform.position).magnitude < 0.35f, 8, "walks to the centre");
            RenderShot("body_front", new Vector3(0, 3.4f, 8.5f), new Vector3(0, 2f, 0), 42);

            yield return SoloDanger();
            yield return Party(2);
            yield return Party(4);
            Finish();
        }

        IEnumerator BeginFight(int seed, bool bots)
        {
            goal = null; hits.Clear();
            d.World.PlayerHit -= OnHit; d.World.PlayerHit += OnHit;
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is BriarMetronomeEncounter, 3, "boss spawned");
            boss = d.Boss as BriarMetronomeEncounter;
            boss.Seed = seed;
            d.World.Difficulty = Difficulty.Medium;
            BriarMetronomeHud.ShowDiagnostics = false;
            int n = d.World.Players.Count;
            for (int i = 0; i < n; i++) d.World.Players[i].Input = bots ? new AllyBot(i, n, BotMode.Perfect) : (IInputSource)still;
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
        }

        // ---------------- pure rules ----------------
        void PureSeasons()
        {
            string Row(int n) => string.Join(" ", Enumerable.Range(0, 4).Where(s => R.GraftSeasons(n).Contains(s)).Select(s => R.Numeral(s) + ">p" + (R.OwnerIndex(s, n) + 1)));
            Check("seasons: 2 players own I+III / II+IV (opposite pens)", R.OwnerIndex(0, 2) == 0 && R.OwnerIndex(2, 2) == 0 && R.OwnerIndex(1, 2) == 1 && R.OwnerIndex(3, 2) == 1, Row(2));
            Check("seasons: 3 players own I+IV / II / III (adjacent pens)", R.OwnerIndex(0, 3) == 0 && R.OwnerIndex(3, 3) == 0 && R.OwnerIndex(1, 3) == 1 && R.OwnerIndex(2, 3) == 2, Row(3));
            Check("seasons: 4 players one each; solo grows only grafts I and III", Enumerable.Range(0, 4).All(s => R.OwnerIndex(s, 4) == s) && R.GraftSeasons(1).SequenceEqual(new[] { 0, 2 }), Row(4) + " | solo " + Row(1));
            bool pens = true;
            for (float s0 = 0; s0 < 360; s0 += 17)
                for (int k = 0; k < 4; k++) if (R.PenOf(Vector3.zero, s0, R.GraftSpot(Vector3.zero, s0, k)) != k) pens = false;
            Check("each graft stands in the middle of its own pen, 8.5 m out", pens && Mathf.Approximately(R.GraftSpot(Vector3.zero, 0, 0).magnitude, 8.5f));
        }

        void PureChain()
        {
            var rng = new System.Random(5); bool ok = true; string why = "";
            for (int i = 0; i < 2000; i++)
            {
                bool across = i % 2 == 1; int count = across ? BriarMetronomeEncounter.CutsLow : BriarMetronomeEncounter.CutsHigh;
                var c = R.Chain(rng, count, across);
                if (c.Count != count || !c.Contains(R.Cut.In) || !c.Contains(R.Cut.Out) || across != c.Contains(R.Cut.Across)) { ok = false; why = string.Join(" ", c); }
                if (c.Count > 0 && c[0] == R.Cut.Across) { ok = false; why = "starts with Across: " + string.Join(" ", c); }
                for (int k = 1; k < c.Count; k++) if (c[k] == R.Cut.Across && c[k - 1] == R.Cut.Across) { ok = false; why = "Across twice: " + string.Join(" ", c); }
            }
            Check("cut chain: 4 cuts (6 with Across below 50%), always an In and an Out, never Across twice in a row (2000 rolls)", ok, why);
        }

        void PureEscapes()
        {
            float bh = BriarMetronomeEncounter.BpmHigh, bl = BriarMetronomeEncounter.BpmLow;
            // the cut chain: a walker with the fight's lookahead survives every chain from every start (4.2 m/s, 0.3 s reaction)
            var starts = new List<Vector3>(); foreach (var p in R.Floor(3f)) starts.Add(p);
            var p1 = new List<List<R.Cut>>();
            for (int m = 0; m < 16; m++) { var c = new List<R.Cut>(); for (int k = 0; k < 4; k++) c.Add((m >> k & 1) == 0 ? R.Cut.In : R.Cut.Out); p1.Add(c); }
            var crng = new System.Random(11); var p1Hard = new List<List<R.Cut>>(); var p2 = new List<List<R.Cut>>();
            for (int i = 0; i < 40; i++) { p1Hard.Add(R.Chain(crng, BriarMetronomeEncounter.CutsHigh, true)); p2.Add(R.Chain(crng, BriarMetronomeEncounter.CutsLow, true)); }
            foreach (var (label, chains, bpm, beats) in new[] {
                ("phase 1 Medium, every In/Out chain", p1, bh, BriarMetronomeEncounter.CutBeatsMedium),
                ("phase 1 Hard (Across), 40 chains", p1Hard, bh, BriarMetronomeEncounter.CutBeatsMedium),
                ("phase 2 Medium (6 cuts, Across, 3.5 beats), 40 chains", p2, bl, BriarMetronomeEncounter.CutBeatsLow),
                ("phase 2 Easy (4 beats), 40 chains", p2, bl, BriarMetronomeEncounter.CutBeatsEasy) })
            {
                int n = 0, ok = 0; string why = "";
                foreach (var c in chains)
                    foreach (var st in starts)
                    {
                        n++;
                        if (R.ChainSurvivable(c, st, bpm, beats, BriarMetronomeEncounter.FirstCutBeats, R.SlowWalk, out string w)) ok++;
                        else if (why == "") why = $"{string.Join(" ", c)} from ({st.x:0},{st.z:0}): {w}";
                    }
                Check($"Pruning Tempo {label}: a 4.2 m/s walker reading the shown cuts survives from every start ({n} runs)", ok == n, ok == n ? "" : $"{ok}/{n}; {why}");
            }
            // Root Collar from his body, hedge sidestep, weight lock, graft bloom, all at the faster tempo
            float collar = R.CollarRadius + Hazard.Margin - (R.BodyRadius + PlayerController.Radius);
            Check($"Root Collar: from his body edge to outside in 2.5 beats at 132 BPM ({collar:0.00} m of {R.Budget(BriarMetronomeEncounter.CollarBeatsMedium, bl):0.00})", collar <= R.Budget(BriarMetronomeEncounter.CollarBeatsMedium, bl));
            float side = R.HedgeWidth / 2 + Hazard.Margin;
            Check($"Hedgerow: off the hedge line before the first section grows (4 beats; {side:0.00} m)", side <= R.Budget(BriarMetronomeEncounter.HedgeDelayBeats, bl));
            float wpull = R.WeightRadius + Hazard.Margin;
            Check($"Metronome Weight: out of the locked circle in 3 beats at 108 BPM and 4 at 132 ({wpull:0.00} m of {R.Budget(BriarMetronomeEncounter.WeightLockBeatsHigh, bh):0.00} / {R.Budget(BriarMetronomeEncounter.WeightLockBeatsLow, bl):0.00})",
                wpull <= R.Budget(BriarMetronomeEncounter.WeightLockBeatsHigh, bh) && wpull <= R.Budget(BriarMetronomeEncounter.WeightLockBeatsLow, bl));
            float bloom = R.BloomRadius + Hazard.Margin;
            Check($"Graft Bloom: out of the 6 m burst from the graft (3.5 beats at 108, 4.5 at 132; {bloom:0.00} m of {R.Budget(BriarMetronomeEncounter.BloomWarnBeatsHigh, bh):0.00} / {R.Budget(BriarMetronomeEncounter.BloomWarnBeatsLow, bl):0.00})",
                bloom <= R.Budget(BriarMetronomeEncounter.BloomWarnBeatsHigh, bh) && bloom <= R.Budget(BriarMetronomeEncounter.BloomWarnBeatsLow, bl));
            Check("damage: no single hit of this boss reaches 100 even on Hard (x1.3)", Mathf.CeilToInt(BriarMetronomeEncounter.WeightDmg * 1.3f) < 100);
        }

        // a walker following Waypoint from every pen to every graft never steps on a hedge line
        void PureRoute()
        {
            bool ok = true; string why = ""; int routes = 0;
            foreach (float s0 in new[] { 0f, 37f, 210f })
            {
                var hedges = new List<Hazard>();
                for (int k = 0; k < 4; k++)
                    foreach (var (from, len) in R.Sections(R.Bound))
                        hedges.Add(new Hazard { Shape = HazardShape.Lane, Origin = Sim.Dir(R.SpokeYaw(s0, k)) * from, Yaw = R.SpokeYaw(s0, k), Length = len, Width = R.HedgeWidth });
                for (int fromPen = 0; fromPen < 4; fromPen++)
                    foreach (float r in new[] { 5f, 9f, 12.5f })
                    foreach (float side in new[] { 0f, 1f, -1f })   // the middle of the pen, or 1.3 m beside either hedge
                        for (int toPen = 0; toPen < 4; toPen++)
                        {
                            routes++;
                            float a = side == 0 ? R.PenYaw(s0, fromPen) + 20f : R.SpokeYaw(s0, fromPen + (side > 0 ? 1 : 0)) - side * Mathf.Asin(1.3f / r) * Mathf.Rad2Deg;
                            var p = Sim.Dir(a) * r;
                            var stand = R.GraftSpot(Vector3.zero, s0, toPen) + Sim.Dir(R.PenYaw(s0, toPen)) * 1.6f;
                            int steps = 0;
                            while (Sim.Flat(p - stand).magnitude > 0.2f && steps++ < 4000)
                            {
                                var w = R.Waypoint(Vector3.zero, s0, p, stand, toPen);
                                var to = Sim.Flat(w - p); if (to.magnitude < 0.05f) break;
                                p += to.normalized * Mathf.Min(0.1f, to.magnitude);
                                if (Sim.Flat(p).magnitude < R.BodyRadius + PlayerController.Radius - 0.01f) { ok = false; why = $"through his body ({s0}, pen {fromPen}->{toPen})"; break; }
                                if (hedges.Any(h => h.Contains(p))) { ok = false; why = $"on a hedge at ({p.x:0.0},{p.z:0.0}) ({s0}, pen {fromPen}->{toPen})"; break; }
                            }
                            if (Sim.Flat(p - stand).magnitude > 0.2f && why == "") { ok = false; why = $"never arrives ({s0}, pen {fromPen}->{toPen})"; }
                        }
            }
            Check($"inner path: from every pen to every graft without touching a hedge or his body ({routes} routes)", ok, why);
        }

        // ---------------- solo: standing still next to it / at long range ----------------
        IEnumerator SoloDanger()
        {
            Section("solo: melee and ranged danger (standing still)");
            var near = new Vector3(0, 0, 2.4f); var far = new Vector3(0, 0, 12.4f);

            PlaceMe(near); int start = d.World.Tick;
            yield return Block("Prune");
            Check("Pruning Tempo: standing still next to it is hit (Out)", HitsSince(start, "Pruning Tempo (Out)", d.Player) >= 1, string.Join(" ", boss.LastChain));
            PlaceMe(far); start = d.World.Tick;
            yield return Block("Prune");
            Check("Pruning Tempo: standing still at 12.4 m is hit (In)", HitsSince(start, "Pruning Tempo (In)", d.Player) >= 1, string.Join(" ", boss.LastChain));

            PlaceMe(near); start = d.World.Tick;
            yield return StartOf("Hedge");
            yield return Until(() => boss.HedgesUp && boss.GraftsAlive > 0, 15, "grafts sprout (solo)");
            Check("Root Collar: standing still next to it is hit", HitsSince(start, "Root Collar", d.Player) == 1);
            Check("solo: two grafts (I and III), the solo bloom time is 15 s", boss.GraftsAlive == 2 && boss.BloomTicks == 900, $"{boss.GraftsAlive} grafts, {boss.BloomTicks} ticks");
            // hit a graft from afar: its owner (me) isn't next to it, so it holds
            var spot = boss.GraftSpots()[0]; int held = boss.GraftHeld;
            d.World.DamagePart(d.World.Parts[0], 10, spot, "test");
            Check("a graft takes no damage unless its owner stands next to it ('held')", boss.GraftHeld == held + 1);
            PlaceMe(spot + Sim.Flat(spot).normalized * 1.5f);
            int bossHp = boss.Hp; int dealt = d.World.DamagePart(d.World.Parts[0], 10, spot, "test");
            Check("next to its own graft, the owner's hit cuts it and also hits the boss", dealt == 10 && boss.Hp == bossHp - 10);
            Overhead("solo_hedges_grafts");
            PlaceMe(far);
            yield return Until(() => boss.Rooted, 40, "Rooted (solo)");
            Check("uncut grafts bloom (6 m burst and pollen)", boss.GraftsBloomed >= 2 && HitsSince(start, "Pollen", d.Player) >= 2);
            Check("Rooted opens with nothing live and no boss parts", d.World.Hazards.Count == 0 && d.World.Parts.Count == 0, $"{d.World.Hazards.Count} live, {d.World.Parts.Count} parts");
            yield return Until(() => boss.Current == "between blocks", 10, "after Rooted");

            // far player: the first hedge grows under the farthest player
            PlaceMe(far); start = d.World.Tick;
            yield return StartOf("Hedge");
            yield return Until(() => boss.HedgesUp && boss.GraftsAlive > 0, 15, "hedges grown (far)");
            Check("Hedgerow: standing still at 12.4 m (the farthest) the first hedge grows over you", HitsSince(start, "Hedgerow", d.Player) == 1 && Mathf.Abs(Mathf.DeltaAngle(boss.Spoke0, 0)) < 1f);
            yield return Until(() => boss.Current == "between blocks", 40, "hedge block ends (far)");

            PlaceMe(far); start = d.World.Tick;
            yield return Block("Ripen");
            Check("Ripening: standing still at long range the weight lands on you", HitsSince(start, "Metronome Weight", d.Player) == 1);
            Check("Ripening: your own seed never hurts you", HitsSince(start, "Ripening seed", d.Player) == 0);
            yield return Leftovers("Ripening");
            d.SetPaused(true); d.ReturnToMenu(); yield return null;
        }

        // ---------------- bot parties ----------------
        IEnumerator Party(int n)
        {
            Section($"bot party of {n} (AllyBot Perfect, every ability in both phases)");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            d.SelectAllies(n - 1);
            Check($"party of {n}: practice starts", d.StartPractice("Tollbreaker", enc) && d.World.Players.Count == n, $"{d.World?.Players.Count} players");
            yield return BeginFight(20261103 + n, true);
            var classes = string.Join(", ", d.World.Players.Select((p, i) => $"p{i + 1} {p.ClassName} [{string.Join("+", boss.SeasonsOf(p).Select(R.Numeral))}]"));
            Note("party: " + classes);
            int t0 = d.World.Tick;

            // phase 1
            int c0 = boss.CutsLanded; yield return Block("Prune");
            Check($"[{n}p] P1 Pruning Tempo: 4 cuts land, no Across", boss.CutsLanded - c0 == 4 && !boss.LastChain.Contains(R.Cut.Across), string.Join(" ", boss.LastChain));
            yield return BlockWithShots("Hedge", n, "p1");
            yield return Block("Ripen");
            Check($"[{n}p] P1 Ripening: {n} seeds burst, one weight lands", boss.SeedsBurst >= n && boss.WeightsLanded >= 1, $"{boss.SeedsBurst} seeds, {boss.WeightsLanded} weights");

            // the 50% shift
            yield return StartOf("Prune");
            boss.DebugSetHpFraction(0.505f); boss.TakeHit(100, boss.transform.position + Vector3.up * 2);
            Check($"[{n}p] crossing 50% mid-attack: the attack carries on", !boss.Phase2 && boss.ShiftStart < 0);
            yield return Until(() => boss.ShiftStart >= 0, 30, "shift starts");
            int haz = d.World.Hazards.Count, hp = boss.Hp; boss.TakeHit(40, boss.transform.position + Vector3.up * 2);
            Check($"[{n}p] the shift waits for the attack, is harmless and it stays damageable", boss.BlockLog.Last().EndsWith("Shift") && haz == 0 && boss.Hp == hp - 40, $"{haz} live");
            yield return Until(() => boss.Phase2, 5, "phase 2");
            Check($"[{n}p] 2 s shift, then 132 BPM (27-tick beat)", boss.ShiftEnd - boss.ShiftStart == BriarMetronomeEncounter.ShiftTicks && boss.BeatTicks == 27);

            // phase 2
            yield return Until(() => boss.Current == "between blocks", 30, "P2 ready");
            c0 = boss.CutsLanded; int x0 = boss.CutLog.Count;
            yield return Block("Prune");
            var across = boss.CutLog.Skip(x0).Where(c => c.cut == R.Cut.Across).ToList();
            Check($"[{n}p] P2 Pruning Tempo: 6 cuts with Across, 3.5 beats apart (94 ticks)", boss.CutsLanded - c0 == 6 && across.Count >= 1 && boss.CutLog.Skip(x0).All(c => c.spacing == 94), string.Join(" ", boss.LastChain));
            int w0 = boss.WeightsLanded, wl0 = boss.WeightLog.Count;
            yield return Block("Ripen");
            Check($"[{n}p] P2 Ripening: two weights", boss.WeightsLanded - w0 == 2);
            if (boss.WeightLog.Count - wl0 >= 1) Note($"weight 1 on p{d.World.Players.IndexOf(boss.WeightLog[wl0].target) + 1}" + (boss.WeightLog.Count - wl0 >= 2 ? $", weight 2 on p{d.World.Players.IndexOf(boss.WeightLog[wl0 + 1].target) + 1}" : ""));
            yield return BlockWithShots("Hedge", n, "p2");

            // leftovers, then defeat with grafts and hedges up
            Check($"[{n}p] after Hedgerow nothing is left (no hazards, no parts)", d.World.Hazards.Count == 0 && d.World.Parts.Count == 0, $"{d.World.Hazards.Count} live, {d.World.Parts.Count} parts");
            yield return Block("Ripen");
            yield return Leftovers($"[{n}p] Ripening");
            yield return StartOf("Hedge");
            yield return Until(() => boss.GraftsAlive > 0, 15, "grafts for the defeat check");
            boss.DebugDefeat();
            yield return null;
            Check($"[{n}p] defeat: hazards, grafts and hedges are cleared at once", d.World.Hazards.Count == 0 && d.World.Parts.Count == 0 && boss.GraftsAlive == 0 && !boss.HedgesUp);
            yield return Until(() => d.State == RunState.Results, 10, "results");

            // what the bots took (HP topped up during the run, so this is the measure)
            var taken = hits.Where(h => h.tick >= t0).GroupBy(h => h.source).Select(g => $"{g.Key} {g.Count()}x/{g.Sum(h => h.dmg)}").ToList();
            Note($"[{n}p] damage the bots took by source: " + (taken.Count == 0 ? "none" : string.Join(", ", taken)));
            var cutLog = boss.GraftLog.Select(g => $"{R.Numeral(g.season)}:p{g.owner + 1} {(g.cut ? "cut" : "BLOOM")} {g.ticksAlive / 60f:0.0}s");
            Note($"[{n}p] grafts: " + string.Join(", ", cutLog));
            d.SetPaused(true); d.ReturnToMenu(); yield return null;
            d.SelectAllies(0);
        }

        // whatever an attack leaves on the floor when it ends (bramble patches) must expire within 5 s; the boss's next
        // attack may already be drawing its own warnings by then, so only those hazards are tracked
        IEnumerator Leftovers(string what)
        {
            var left = d.World.Hazards.Live.ToList(); int parts = d.World.Parts.Count;
            yield return Wait(5f);
            var still = left.Where(h => d.World.Hazards.Live.Contains(h)).ToList();
            Check($"{what}: everything it leaves ({left.Count}: {string.Join(", ", left.Select(h => h.Source).Distinct())}) is gone within 5 s, no boss parts", still.Count == 0 && parts == 0,
                string.Join(", ", still.Select(h => h.Source)));
        }

        IEnumerator BlockWithShots(string atk, int n, string tag)
        {
            int g0 = boss.GraftLog.Count, r0 = boss.RootedLog.Count, hc0 = boss.HedgeCasts, cut0 = boss.GraftsCut;
            yield return StartOf(atk);
            yield return Until(() => boss.HedgesUp && boss.HedgeCasts > hc0, 15, "hedges");
            yield return Wait(0.6f); Overhead($"{n}p_{tag}_hedges_growing");
            yield return Until(() => boss.GraftsAlive > 0, 15, "grafts");
            yield return Wait(2.5f); Overhead($"{n}p_{tag}_grafts_cutting");
            foreach (var p in d.World.Players)
                Note($"[{n}p] {tag} +2.5 s p{d.World.Players.IndexOf(p) + 1} at ({p.transform.position.x:0.0},{p.transform.position.z:0.0}) pen {R.PenOf(Vector3.zero, boss.Spoke0, p.transform.position)} | {(p.Input as AllyBot)?.DebugLine}");
            yield return GameShot($"{n}p_{tag}_game_view");
            yield return Until(() => boss.Current == "between blocks" || boss.Dead, 60, "hedge block ends");
            var gl = boss.GraftLog.Skip(g0).ToList();
            int expect = R.GraftSeasons(n).Length;
            Check($"[{n}p] {tag.ToUpper()} Hedgerow + Season Grafts: {expect} grafts sprout and every one resolves", gl.Count == expect, $"{gl.Count} resolved");
            Check($"[{n}p] {tag.ToUpper()} the bots cut grafts by their own season (owner next to it)", gl.Count(g => g.cut) >= 1 && gl.Where(g => g.cut).All(g => g.owner == R.OwnerIndex(g.season, n)),
                string.Join(", ", gl.Select(g => $"{R.Numeral(g.season)} p{g.owner + 1} {(g.cut ? "cut" : "bloom")} {g.ticksAlive / 60f:0.0}s")));
            if (gl.Any(g => !g.cut)) Note($"[{n}p] {tag} bloomed grafts: {gl.Count(g => !g.cut)} (bots were too slow; see damage notes)");
            var rl = boss.RootedLog.Skip(r0).ToList();
            Check($"[{n}p] {tag.ToUpper()} Rooted: one 3.5 s window with nothing live", rl.Count == 1 && rl[0].end - rl[0].start == BriarMetronomeEncounter.RootedTicks && rl[0].liveAtStart == 0);
        }

        void Finish()
        {
            Check("no runtime errors", errors == 0, errors + " logged");
            lines.Insert(0, $"Briar Metronome autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "briar-metronome-report.txt"), lines);
            Debug.Log($"[briar-metronome-test] done: {pass} passed, {fail} failed");
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
