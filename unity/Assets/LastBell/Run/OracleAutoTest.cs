using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for the Chimebound Oracle inside the real game, started with  -oracleTest -lastbellOut <folder>
    // (editor Play Mode via OracleTestTools.PlayModeTest, or a player). Same approach as RunAutoTest: the director's public
    // requests, a bot that sends ordinary PlayerInput, and the labelled dev hooks (boss HP fraction / defeat, player
    // DebugSetHp to keep the bot standing). It checks spec v0.1 (collab PR #57) against what the hazards actually do,
    // captures every telegraph, writes oracle-report.txt and exits 0 (all passed) or 1.
    public sealed class OracleAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-oracleTest", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "oracle-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var d = FindAnyObjectByType<RunDirector>();
            if (d && !d.GetComponent<OracleAutoTest>()) d.gameObject.AddComponent<OracleAutoTest>();
        }

        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld; public Vector3 Aim;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = Aim };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                return r;
            }
        }

        // every hazard instance seen, with the combat tick it appeared on
        sealed class Seen { public Hazard H; public int Spawn; public int Phase; }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly List<(string source, int dmg, int tick)> hits = new List<(string, int, int)>();
        readonly HashSet<string> shotTaken = new HashSet<string>();
        float maxLeash; int maxLive, maxLiveP2; bool watching; string dir;
        ChimeboundOracle boss;

        void Start()
        {
            d = GetComponent<RunDirector>(); dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t)
        {
            if (t != LogType.Error && t != LogType.Exception && t != LogType.Assert) return;
            // the editor's own startup jobs (e.g. UnityEditor.Search indexing) can throw while Play Mode runs: not game code
            if (stack != null && stack.Contains("UnityEditor.") && !stack.Contains("LastBell.")) { lines.Add($"      ignored editor {t}: {msg}"); return; }
            errors++; lines.Add($"      {t}: {msg}");
        }

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[oracle-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[oracle-test] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            var path = Path.Combine(dir, $"{++shots:00}_{name}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
            Note("screenshot " + Path.GetFileName(path));
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!Safe(cond) && t < timeout) { t += Time.deltaTime; yield return null; }
            if (!Safe(cond)) Check("reached: " + what, false, $"timed out after {timeout}s");
        }
        static bool Safe(Func<bool> c) { try { return c(); } catch (Exception) { return false; } }

        // ---------------------------------------------------------------- bot and watcher

        void Update()
        {
            if (!watching || d.World == null || !boss || boss.Dead) return;
            var w = d.World;
            foreach (var h in w.Hazards.Live)
                if (!seen.ContainsKey(h.Id)) seen[h.Id] = new Seen { H = h, Spawn = w.Tick - h.Age, Phase = boss.PhaseName == ChimeboundOracle.Phase2 ? 2 : 1 };
            // damaging patterns live at once: a marker volley (or its repeat) counts as one
            int patterns = w.Hazards.Live.Select(h => h.Source == ChimeboundOracle.Shattered || h.Source == ChimeboundOracle.After ? "volley" : h.Id.ToString()).Distinct().Count();
            if (boss.PhaseName == ChimeboundOracle.Phase2) maxLiveP2 = Mathf.Max(maxLiveP2, patterns); else maxLive = Mathf.Max(maxLive, patterns);
            maxLeash = Mathf.Max(maxLeash, Sim.Flat(boss.transform.position).magnitude);
            var p = d.Player; if (!p) return;
            if (p.Hp < 45) p.DebugSetHp(PlayerController.MaxHp);   // keep the bot standing; hits are still logged
            bot.Aim = boss.transform.position;
            bot.MoveWorld = Steer(p.transform.position);
        }

        // keep 5 m from the boss and leave any hazard that resolves soon; in a sector attack, go to the safe sector
        Vector3 Steer(Vector3 pos)
        {
            var b = Sim.Flat(boss.transform.position); var to = b - Sim.Flat(pos);
            foreach (var h in d.World.Hazards.Live)
            {
                if (!h.Contains(pos)) continue;
                if (h.HalfAngle > 90)
                {
                    var goal = h.Origin + Sim.Dir(h.Yaw + 180) * 4.2f;     // the silent sector's numeral
                    var v = Sim.Flat(goal - pos);
                    // go around the body, not through it
                    if (Vector3.Dot(v.normalized, to.normalized) > 0.7f && to.magnitude < 3f) v = Vector3.Cross(Vector3.up, to.normalized);
                    return v.normalized;
                }
                if (h.ResolveAt - h.Age > 50) continue;
                var away = Sim.Flat(pos - h.Origin); if (away.sqrMagnitude < 0.01f) away = Vector3.right;
                if (h.Shape == HazardShape.Lane) return h.PushDir(pos);
                if (h.Shape == HazardShape.Cone) { var side = Vector3.Cross(Vector3.up, Sim.Dir(h.Yaw)); return ((Vector3.Dot(away, side) >= 0 ? side : -side) * 0.8f + away.normalized * 0.6f).normalized; }
                return away.normalized;
            }
            float dist = to.magnitude;
            if (Sim.Flat(pos).magnitude > 10.5f) return -Sim.Flat(pos).normalized;
            return dist > 5.5f ? to.normalized : dist < 4.5f ? -to.normalized : Vector3.Cross(Vector3.up, to.normalized) * 0.4f;
        }

        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((h.Source, dmg, d.World.Tick));

        IEnumerable<Seen> Of(string src, int phase = 0) => seen.Values.Where(s => s.H.Source == src && (phase == 0 || s.Phase == phase)).OrderBy(s => s.Spawn).ThenBy(s => s.H.Id);

        IEnumerator ShotWhen(string src, string name, int minAge)
        {
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Source == src && h.Age >= minAge), 40, name + " telegraph");
            if (!shotTaken.Add(name)) yield break;
            yield return Shot(name);
        }

        // ---------------------------------------------------------------- scenario

        IEnumerator Main()
        {
            Note($"Chimebound Oracle test · {Application.version} · Unity {Application.unityVersion} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            d.InputOverride = bot;
            yield return Wait(1.0f);
            Check("practice starts the Oracle", d.StartPractice("Vanguard", 2));
            yield return Wait(0.4f); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
            boss = d.Boss as ChimeboundOracle;
            Check("encounter is the Chimebound Oracle in phase 1", boss != null && boss.PhaseName == ChimeboundOracle.Phase1, boss ? boss.PhaseName : "none");
            if (boss == null) { yield return Finish(); yield break; }
            d.World.PlayerHit += OnHit; watching = true;

            // ---------- phase 1: teaching loop, Silent Fifth, combined loop ----------
            yield return ShotWhen(ChimeboundOracle.Shattered, "p1_shattered_chime", 20);
            yield return ShotWhen(ChimeboundOracle.Dividing, "p1_dividing_chime_alone", 30);
            yield return ShotWhen(ChimeboundOracle.Silent, "p1_silent_fifth", 60);
            var fifth = boss.LastFifth;
            Check("Silent Fifth: one 288° cone, 6.5 m, 144-tick warning, 26 damage, Ground",
                fifth != null && fifth.Shape == HazardShape.Cone && Mathf.Approximately(fifth.HalfAngle, 144) && Mathf.Approximately(fifth.Radius, 6.5f)
                && fifth.LockAt == 0 && fifth.ResolveAt == 144 && fifth.Damage == 26 && fifth.Kind == HazardKind.Ground);
            if (fifth != null) SectorGeometry("Silent Fifth", fifth);
            Check("Silent Fifth is cast from inside the 3 m leash", Sim.Flat(boss.transform.position).magnitude <= ChimeboundOracle.Leash + 0.05f,
                $"{Sim.Flat(boss.transform.position).magnitude:0.00} m");
            yield return Until(() => Of(ChimeboundOracle.Dividing).Count() >= 2, 40, "second loop's Dividing Chime");
            yield return Shot("p1_markers_then_cone");

            // markers and the combined cone, measured from the hazards themselves
            var marks = Of(ChimeboundOracle.Shattered, 1).ToList();
            Check("Shattered Chime: 1.4 m markers, follow 30 + 48 after lock, 12 damage",
                marks.Count >= 6 && marks.All(s => Mathf.Approximately(s.H.Radius, 1.4f) && s.H.LockAt == 30 && s.H.ResolveAt == 78 && s.H.Damage == 12), $"{marks.Count} markers");
            Check("markers come in threes, 24 ticks apart", marks.Count >= 3 && marks[1].Spawn - marks[0].Spawn == 24 && marks[2].Spawn - marks[1].Spawn == 24,
                marks.Count >= 3 ? $"{marks[1].Spawn - marks[0].Spawn}, {marks[2].Spawn - marks[1].Spawn}" : "");
            var cones = Of(ChimeboundOracle.Dividing, 1).ToList();
            Check("Dividing Chime: 6.5 m, 70°, 18 tracking + 51 after lock, 20 damage",
                cones.Count >= 2 && cones.All(s => Mathf.Approximately(s.H.Radius, 6.5f) && Mathf.Approximately(s.H.HalfAngle, 35) && s.H.LockAt == 18 && s.H.ResolveAt == 69 && s.H.Damage == 20));
            if (cones.Count >= 2 && marks.Count >= 6)
            {
                var first = marks[3]; var last = marks[5]; var cone = cones[1];
                int coneStart = cone.Spawn - first.Spawn, gap = cone.Spawn + 69 - (last.Spawn + 78);
                Check("combined loop: the cone warns at tick 110 of the volley", coneStart == 110, coneStart.ToString());
                Check("anti-chain: the cone resolves ≥ 53 ticks after the last marker (15 stun + 38 escape)", gap >= 53, gap.ToString());
            }

            // ---------- phase transition ----------
            yield return Until(() => boss.Current == "recovery" || boss.Current == ChimeboundOracle.Shattered, 20, "a point to cross 50 %");
            boss.DebugSetHpFraction(0.49f);
            yield return Until(() => boss.PhaseName == ChimeboundOracle.Phase2, 25, "phase 2");
            yield return null; yield return null;
            Check("phase 2 is queued: no hazard is live during the iris transition", boss.Current == "phase shift" && d.World.Hazards.Count == 0,
                $"{boss.Current}, {d.World.Hazards.Count} live");
            yield return Wait(0.8f); yield return Shot("p2_iris_open");

            // ---------- phase 2: Afterchime, Dividing, Needle, Passing Silence ----------
            yield return Until(() => boss.LastRepeats.Count > 0, 25, "Afterchime repeat");
            yield return Shot("p2_afterchime_repeat");
            var rep = boss.LastRepeats.ToList(); var org = boss.LastOriginals.ToList();
            Check("Afterchime repeats every original at its locked position", rep.Count == org.Count && org.Count == 3
                && rep.Zip(org, (r, o) => Sim.Flat(r.Origin - o.Origin).magnitude < 0.01f).All(x => x), $"{rep.Count} repeats / {org.Count} originals");
            Check("the repeat warns 90 ticks, as one attack event (shared hit set)",
                rep.Count > 0 && rep.All(r => r.ResolveAt == 90 && r.LockAt == 0) && rep.Skip(1).All(r => r.Group == rep[0]));
            yield return ShotWhen(ChimeboundOracle.Needle, "p2_unison_needle", 30);
            var needle = boss.LastNeedle;
            Check("Unison Needle: 1.5 m beam, 48 tracking + 60 locked, 26 damage", needle != null && Mathf.Approximately(needle.Width, 1.5f)
                && needle.LockAt == 48 && needle.ResolveAt == 108 && needle.Damage == 26);
            yield return Until(() => needle.Locked, 3, "needle lock");
            if (needle != null)
            {
                float reach = Sim.Flat(needle.Origin + Sim.Dir(needle.Yaw) * needle.Length).magnitude;
                Check("the Needle reaches the floor edge (no out-of-reach spot)", reach >= CombatWorld.PlayerBound, $"{reach:0.00} m from centre");
            }
            yield return Until(() => boss.LastPassingBeat2 != null, 30, "Passing Silence beat 2");
            yield return Wait(0.3f); yield return Shot("p2_passing_silence_beat2");
            var b1 = boss.LastFifth; var b2 = boss.LastPassingBeat2;
            if (b1 != null && b2 != null)
            {
                float turn = Mathf.Abs(Mathf.DeltaAngle(b1.Yaw, b2.Yaw));
                Check("Passing Silence: the safe sector moves exactly one sector", Mathf.Abs(turn - 72) < 0.5f, $"{turn:0.0}°");
                var s1 = seen.Values.FirstOrDefault(s => s.H == b1); var s2 = seen.Values.FirstOrDefault(s => s.H == b2);
                Check("beat 2 warns 120 ticks from beat 1's resolve tick", b2.ResolveAt == 120 && s1 != null && s2 != null && s2.Spawn == s1.Spawn + 144,
                    s1 != null && s2 != null ? $"beat 2 at +{s2.Spawn - s1.Spawn}" : "");
                SectorGeometry("Passing Silence beat 2", b2);
            }
            yield return Until(() => b2 == null || b2.Age >= b2.ResolveAt, 4, "beat 2 resolve");
            yield return Wait(0.3f);

            // ---------- invariants over everything seen ----------
            watching = false;
            Check("the boss never left the 3 m leash", maxLeash <= ChimeboundOracle.Leash + 0.05f, $"max {maxLeash:0.00} m");
            Check("never more than two damaging patterns at once (a marker volley counts as one)", maxLive <= 2, maxLive.ToString());
            Check("phase 2: one damaging pattern at a time", maxLiveP2 == 1, maxLiveP2.ToString());
            var byEvent = hits.GroupBy(x => (x.source, x.tick)).Where(g => g.Count() > 1).ToList();
            Check("one hit per attack event (no double hit on a sector border or repeat overlap)", byEvent.Count == 0,
                $"{hits.Count} hits: " + string.Join(", ", hits.GroupBy(x => x.source).Select(g => $"{g.Key} ×{g.Count()}")));

            // ---------- shared hit set, directly ----------
            boss.Halt();
            yield return Until(() => d.World.Hazards.Count == 0, 6, "quiet");
            var p = d.Player; p.DebugSetHp(PlayerController.MaxHp); int hp0 = p.Hp;
            var lead = d.World.Hazards.Spawn(new Hazard { Source = "group test", Shape = HazardShape.Circle, Origin = Sim.Flat(p.transform.position), Radius = 1f, ResolveAt = 3, Damage = 5 });
            d.World.Hazards.Spawn(new Hazard { Source = "group test", Shape = HazardShape.Circle, Origin = Sim.Flat(p.transform.position), Radius = 1f, ResolveAt = 3, Damage = 5, Group = lead });
            yield return Wait(0.3f);
            Check("two overlapping hazards in one group hit once", hp0 - p.Hp == 5, $"took {hp0 - p.Hp}");

            // ---------- defeat ----------
            boss.BeginCombat();
            yield return Until(() => d.World.Hazards.Count > 0, 10, "a warning to kill through");
            int resolved0 = d.World.Hazards.Resolved;
            boss.DebugDefeat();
            Check("defeat cancels pending hazards and outlines", d.World.Hazards.Count == 0 && boss.Outlines == 0);
            yield return Wait(1.0f);
            Check("nothing resolves after defeat", d.World.Hazards.Resolved == resolved0);
            yield return Finish();
        }

        // a point in each sector at 3 m and on each border; the safe sector and beyond the radius are safe
        void SectorGeometry(string label, Hazard h)
        {
            float safe = h.Yaw + 180; bool ok = true; var bad = new List<string>();
            for (int n = 0; n < 5; n++)
            {
                var c = h.Origin + Sim.Dir(safe + n * 72) * 3.5f;
                bool want = n != 0;
                if (h.Contains(c) != want) { ok = false; bad.Add($"sector {n}"); }
                var border = h.Origin + Sim.Dir(safe + 36 + n * 72) * 3.5f;   // between sector n and n+1
                if (n != 0 && n != 4 && !h.Contains(border)) { ok = false; bad.Add($"border {n}"); }
            }
            if (h.Contains(h.Origin + Sim.Dir(safe + 72) * 7.2f)) { ok = false; bad.Add("outside the radius"); }
            Check($"{label}: four sectors hit, the silent one and beyond 6.5 m are safe", ok, string.Join(", ", bad));
        }

        IEnumerator Finish()
        {
            Check("no errors or exceptions in the log", errors == 0, errors.ToString());
            lines.Insert(0, $"Chimebound Oracle test: {pass} passed, {fail} failed");
            File.WriteAllLines(Path.Combine(dir, "oracle-report.txt"), lines);
            Debug.Log($"[oracle-test] done: {pass} passed, {fail} failed");
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
