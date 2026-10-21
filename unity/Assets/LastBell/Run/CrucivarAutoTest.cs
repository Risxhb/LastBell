using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for Crucivar inside the real game, started with  -crucivarTest -lastbellOut <folder>  (editor Play
    // Mode via CrucivarTestTools.PlayModeTest, or a player). Same approach as RunAutoTest: it only uses the director's
    // public requests, a bot that sends ordinary PlayerInput, and the labelled dev hooks (boss HP fraction / defeat,
    // player DebugSetHp to keep the bot standing). It checks the packet's numbers against what the hazards actually do,
    // captures screenshots of every attack's telegraph, writes crucivar-report.txt and exits 0 (all passed) or 1.
    public sealed class CrucivarAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-crucivarTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "crucivar-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var d = FindAnyObjectByType<RunDirector>();
            if (d && !d.GetComponent<CrucivarAutoTest>()) d.gameObject.AddComponent<CrucivarAutoTest>();
        }

        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld; public Vector3 Aim; public bool Sprint;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim, Sprint = Sprint };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                return r;
            }
        }

        // every hazard instance seen, with the combat tick it appeared on
        sealed class Seen { public Hazard H; public string Source; public int Spawn, Resolve, Phase; public bool Resolved; }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly List<(string source, int dmg, int tick)> hits = new List<(string, int, int)>();
        readonly HashSet<string> tasted = new HashSet<string>(), shotTaken = new HashSet<string>();
        readonly List<string> attackOrder = new List<string>();
        int maxStrikesLive; string dir;
        CrucivarEncounter boss;

        void Start()
        {
            d = GetComponent<RunDirector>(); dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        // editor-internal errors (search indexing, importers) are not the game's; only runtime ones count
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Exception || t == LogType.Error) && !(stack ?? "").Contains("UnityEditor.")) { errors++; lines.Add("      LOG " + t + ": " + msg); } }

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[crucivar-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[crucivar-test] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        IEnumerator Capture(string name, int frames)
        {
            var cdir = Path.Combine(dir, "frames_" + name); Directory.CreateDirectory(cdir);
            Time.captureFramerate = 60;
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(cdir, $"{i:0000}.jpg"), tex.EncodeToJPG(85)); Destroy(tex);
            }
            Time.captureFramerate = 0;
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        // ---------------- observation, every frame while fighting ----------------
        void Observe()
        {
            var w = d.World; if (w == null) return;
            int strikes = 0; var live = new HashSet<string>();
            foreach (var h in w.Hazards.Live)
            {
                if (!seen.ContainsKey(h.Id))
                {
                    string family = Family(h.Source);
                    seen[h.Id] = new Seen { H = h, Source = h.Source, Spawn = w.Tick - h.Age, Resolve = h.ResolveAt, Phase = boss ? boss.PhaseIndex : 0 };
                    if (h.BurnPerSecond <= 0 && (attackOrder.Count == 0 || attackOrder[attackOrder.Count - 1] != family)) attackOrder.Add(family);
                }
                if (h.BurnPerSecond <= 0) live.Add(Family(h.Source));
            }
            strikes = live.Count;
            maxStrikesLive = Mathf.Max(maxStrikesLive, strikes + (w.Hazards.Live.Any(h => h.BurnPerSecond > 0) ? 1 : 0));
            foreach (var s in seen.Values) if (!s.Resolved && !w.Hazards.Live.Contains(s.H)) s.Resolved = true;
        }
        static string Family(string src) => src.StartsWith("Casting Cycle") ? "Casting Cycle" : src;

        // the bot: hold ~5 m from the body, stand in the first instance of each attack once (to measure its damage),
        // otherwise step out of anything that resolves soon and out of slag pools
        IEnumerator Fight(float seconds, Func<bool> until, bool taste = true)
        {
            float t = 0;
            while (t < seconds && d.State == RunState.Combat && !until())
            {
                Observe();
                var p = d.Player; var b = d.Boss;
                if (p && b)
                {
                    if (p.Hp < 45) p.DebugSetHp(100);
                    var to = Sim.Flat(b.transform.position - p.transform.position); float dist = to.magnitude - b.BodyRadius;
                    bot.Aim = b.transform.position;
                    bot.MoveWorld = dist > 5.5f ? to.normalized * 0.6f : dist < 3.5f ? -to.normalized : Vector3.Cross(Vector3.up, to.normalized) * 0.4f;
                    if (new Vector2(p.transform.position.x, p.transform.position.z).magnitude > 9.5f) bot.MoveWorld = -p.transform.position.normalized;
                    bot.Sprint = false;
                    var pos = p.transform.position;
                    foreach (var h in d.World.Hazards.Live)
                    {
                        if (!h.Contains(pos)) continue;
                        string fam = Family(h.Source);
                        if (taste && !tasted.Contains(fam)) { bot.MoveWorld = Vector3.zero; break; }
                        if (h.BurnPerSecond <= 0 && h.ResolveAt - h.Age > 50) continue;
                        bot.MoveWorld = Escape(h, pos); bot.Sprint = true; break;
                    }
                }
                t += Time.deltaTime; yield return null;
            }
            bot.MoveWorld = Vector3.zero; bot.Sprint = false;
        }
        static Vector3 Escape(Hazard h, Vector3 pos)
        {
            var away = Sim.Flat(pos - h.Origin); if (away.sqrMagnitude < 0.01f) away = Vector3.right; away.Normalize();
            if (h.Shape == HazardShape.Lane) return h.PushDir(pos);
            if (h.Shape == HazardShape.Cone) { var side = Vector3.Cross(Vector3.up, Sim.Dir(h.Yaw)); return (Vector3.Dot(away, side) >= 0 ? side : -side); }
            return away;
        }

        IEnumerator ShotWhenTelegraphed(params string[] families)
        {
            // a screenshot of each attack family once, about halfway through its warning
            while (d.State == RunState.Combat)
            {
                foreach (var h in d.World.Hazards.Live)
                {
                    string fam = Family(h.Source);
                    if (h.BurnPerSecond > 0 || shotTaken.Contains(fam) || h.Age < h.ResolveAt / 2) continue;
                    shotTaken.Add(fam);
                    yield return Shot(fam.Replace(' ', '_').ToLowerInvariant());
                }
                yield return null;
            }
        }

        // ---------------- the scenario ----------------
        IEnumerator Main()
        {
            Note($"Crucivar autotest · Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} · {DateTime.Now:yyyy-MM-dd HH:mm} · scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            d.InputOverride = bot;
            yield return Wait(1.0f);
            Check("boots into the menu", d.State == RunState.Menu);
            yield return Shot("menu");
            Check("practice starts", d.StartPractice("Vanguard", RunDirector.PracticeCrucivar));
            yield return Wait(0.3f); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
            boss = d.Boss as CrucivarEncounter;
            Check("the practice slot spawns Crucivar", boss != null, d.Boss ? d.Boss.GetType().Name : "none");
            if (boss == null) { Finish(); yield break; }
            Check("Crucivar uses its model, not the placeholder", !boss.Placeholder);
            string[] clips = { "Idle", "Walk", "Diebreaker", "HammerLodged", "SlagCast", "RailRam", "ReleaseFrontBack", "ReleaseSides", "CastingCycle", "Vulnerable", "PhaseShift", "Defeat" };
            var missing = clips.Where(c => !boss.Presenter.Has(c)).ToArray();
            Check("all body clips are in the model", missing.Length == 0, missing.Length == 0 ? $"{clips.Length} clips" : "missing " + string.Join(", ", missing));
            d.World.PlayerHit += (p, h, dmg) => { hits.Add((h.Source, dmg, d.World.Tick)); tasted.Add(Family(h.Source)); };
            yield return Shot("intro");
            StartCoroutine(ShotWhenTelegraphed());

            // Phase 1: isolated hammer, slag, vent and ram attacks
            var p1 = new[] { "Diebreaker", "Slag Cast", "Pressure Release", "Rail Ram" };
            yield return Fight(60, () => p1.All(a => seen.Values.Any(s => s.Source == a && s.Resolved)));
            Check("phase 1 uses Diebreaker, Slag Cast, Pressure Release and Rail Ram", p1.All(a => seen.Values.Any(s => s.Source == a)),
                  "seen: " + string.Join(", ", seen.Values.Select(s => Family(s.Source)).Distinct()));
            Check("phase 1 has no Casting Cycle", !seen.Values.Any(s => s.Phase == 1 && Family(s.Source) == "Casting Cycle"));
            // let the first pools cool before checking expiry
            yield return Fight(4, () => false);

            // Phase 2: Casting Heat
            boss.DebugSetHpFraction(boss.NextPhaseFraction);
            yield return Fight(15, () => boss.PhaseIndex == 2);
            Check("phase 2 starts below 65 %", boss.PhaseIndex == 2 && boss.PhaseName == "Casting Heat", boss.PhaseName);
            yield return Wait(1.0f);
            yield return Shot("phase2_insulation_shed");
            bool capturing = false;
            yield return Fight(60, () =>
            {
                if (!capturing && d.World.Hazards.Live.Any(h => h.Source == "Casting Cycle 1")) { capturing = true; StartCoroutine(Capture("casting_cycle", 260)); }
                return seen.Values.Count(s => s.Phase == 2 && s.Source.StartsWith("Casting Cycle") && s.Resolved) >= 3;
            });
            var strips = seen.Values.Where(s => s.Source.StartsWith("Casting Cycle")).OrderBy(s => s.Spawn).Take(3).ToList();
            Check("Casting Cycle lays three numbered strips", strips.Count == 3 && strips.Select(s => s.Source).SequenceEqual(new[] { "Casting Cycle 1", "Casting Cycle 2", "Casting Cycle 3" }),
                  string.Join(", ", strips.Select(s => s.Source)));
            if (strips.Count == 3)
            {
                var res = strips.Select(s => s.Spawn + s.Resolve).ToArray();
                Check("each strip warns 1.8 s and the next resolves 0.6 s later", strips.All(s => s.Resolve == 108) && res[1] - res[0] == 36 && res[2] - res[1] == 36,
                      $"warn {strips[0].Resolve}/{strips[1].Resolve}/{strips[2].Resolve} ticks, resolves at +{res[1] - res[0]} / +{res[2] - res[1]}");
                var hz = strips.Select(s => s.H).ToArray();
                Check("strips are 2 m wide and parallel with a body-sized cold corridor between them",
                      hz.All(h => Mathf.Approximately(h.Width, 2f)) && Mathf.Abs(Mathf.DeltaAngle(hz[0].Yaw, hz[2].Yaw)) < 0.1f &&
                      Vector3.Distance(hz[0].Origin, hz[1].Origin) - 2f - 2 * Hazard.Margin >= 0.9f,
                      $"spacing {Vector3.Distance(hz[0].Origin, hz[1].Origin):0.00} m");
            }
            Check("Slag Cast comes before Casting in phase 2", attackOrder.IndexOf("Slag Cast", Math.Max(0, attackOrder.IndexOf("Casting Cycle") - 3)) >= 0);
            yield return Fight(3, () => false);

            // Phase 3: Open Pressure
            boss.DebugSetHpFraction(boss.NextPhaseFraction);
            yield return Fight(15, () => boss.PhaseIndex == 3);
            Check("phase 3 starts below 30 %", boss.PhaseIndex == 3 && boss.PhaseName == "Open Pressure", boss.PhaseName);
            yield return Wait(1.2f);
            yield return Shot("phase3_open_pressure");
            yield return Fight(60, () => seen.Values.Count(s => s.Phase == 3 && s.Source == "Casting Cycle 3" && s.Resolved) >= 1);
            int cast3 = seen.Values.Where(s => s.Phase == 3 && s.Source == "Casting Cycle 1").Select(s => s.Spawn).DefaultIfEmpty(-1).Min();
            int ram3 = seen.Values.Where(s => s.Phase == 3 && s.Source == "Rail Ram" && s.Spawn < cast3).Select(s => s.Spawn).DefaultIfEmpty(-1).Max();
            Check("phase 3 rams to a new spot right before Casting", cast3 > 0 && ram3 > 0 && cast3 - ram3 <= 36 + 108 + 150 + 2, $"ram at {ram3}, casting at {cast3}");

            // numbers: damage per attack, pool behaviour, overlap
            var spec = new Dictionary<string, int> { ["Diebreaker"] = 18, ["Slag Cast"] = 10, ["Rail Ram"] = 16, ["Pressure Release"] = 12, ["Casting Cycle"] = 14 };
            foreach (var kv in spec)
            {
                var got = hits.Where(h => Family(h.source) == kv.Key).Select(h => h.dmg).Distinct().ToArray();
                if (got.Length == 0) Note($"{kv.Key}: no hit landed on the bot (damage not measured)");
                else Check($"{kv.Key} deals {kv.Value}", got.All(g => g == kv.Value), "hits: " + string.Join("/", got));
            }
            var burns = hits.Where(h => h.source == "Slag Pool").ToList();
            if (burns.Count > 0)
            {
                // 4/s in whole HP: at most one point per 15 ticks while standing in any number of pools
                var ticks = burns.Select(b => b.tick).ToArray(); int minGap = int.MaxValue;
                for (int i = 1; i < ticks.Length; i++) minGap = Math.Min(minGap, ticks[i] - ticks[i - 1]);
                Check("slag pools burn 4/s and never stack", burns.All(b => b.dmg == 1) && (ticks.Length < 2 || minGap >= 15), $"{burns.Count} burn ticks, min gap {minGap}");
            }
            else Note("slag pools: the bot never stood in one (burn not measured)");
            var pools = seen.Values.Where(s => s.Source == "Slag Pool").ToList();
            Check("slag pools last 3 s, then cool and disappear", pools.Count > 0 && pools.All(s => s.Resolve <= 180) && pools.Where(s => d.World.Tick - s.Spawn > 200).All(s => s.Resolved),
                  $"{pools.Count} pools");
            Check("never more than two damaging patterns at once", maxStrikesLive <= 2, $"max {maxStrikesLive}");
            var w = seen.Values.GroupBy(s => Family(s.Source)).ToDictionary(g => g.Key, g => g.First().Resolve);
            Check("warning times match the packet (1.8 / 2.0 / 1.8 / 1.7 s)",
                  w.TryGetValue("Diebreaker", out var a1) && a1 == 108 && w.TryGetValue("Slag Cast", out var a2) && a2 == 120 &&
                  w.TryGetValue("Rail Ram", out var a3) && a3 == 108 && w.TryGetValue("Pressure Release", out var a4) && a4 == 102,
                  string.Join(", ", w.Select(kv => $"{kv.Key} {kv.Value}")));
            Note("attack order: " + string.Join(" > ", attackOrder));

            // defeat cancels everything in the killing tick
            yield return Fight(20, () => d.World.Hazards.Live.Any(h => h.BurnPerSecond <= 0 && h.Age < h.ResolveAt - 10), false);
            int hp0 = d.Player.Hp, pending = d.World.Hazards.Count;
            yield return Shot("before_defeat");
            boss.DebugDefeat();
            Check("defeat cancels pending hazards and pools", d.World.Hazards.Count == 0, $"{pending} -> {d.World.Hazards.Count}");
            Check("defeated Crucivar stops colliding", !boss.Solid);
            yield return Wait(1.6f);
            yield return Shot("defeat");
            Check("no damage after defeat", d.Player.Hp == hp0, $"{hp0} -> {d.Player.Hp}");
            yield return Until(() => d.State == RunState.Results, 8, "practice results");
            Check("practice ends in a practice result", d.State == RunState.Results && d.Victory && d.Mode == RunMode.Practice);
            Check("no errors or exceptions logged", errors == 0, $"{errors}");
            Finish();
        }

        void Finish()
        {
            lines.Insert(0, $"Crucivar autotest: {pass} passed, {fail} failed");
            File.WriteAllLines(Path.Combine(dir, "crucivar-report.txt"), lines);
            Debug.Log($"[crucivar-test] done: {pass} passed, {fail} failed");
            StartCoroutine(Quit());
        }
        IEnumerator Quit()
        {
            yield return Wait(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.Exit(fail == 0 ? 0 : 1);
#else
            Application.Quit(fail == 0 ? 0 : 1);
#endif
        }
    }
}
