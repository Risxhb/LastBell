using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastBell.Arenas.MetronomeCourt;
using UnityEngine;

namespace LastBell.Run
{
    // Acceptance run for The Tempo Tyrant inside the real game:  -tempoTyrantTest -lastbellOut <folder>  (editor Play
    // Mode via TempoTyrantTestTools.PlayModeTest, or the Windows player). It uses the director's public requests, a bot
    // that sends ordinary PlayerInput, extra simulated roster players for party counts (not co-op), and the labelled dev
    // hooks (ForcedNext attack, boss HP fraction / defeat, player DebugSetHp). It measures what the hazards actually do,
    // runs the whole-route feasibility checks, captures screenshots, writes tempo-tyrant-report.txt and exits 0 / 1.
    public sealed class TempoTyrantAutoTest : MonoBehaviour
    {
        public static bool Requested => Environment.GetCommandLineArgs().Any(a => a.Equals("-tempoTyrantTest", StringComparison.OrdinalIgnoreCase));
        static bool Quick => Environment.GetCommandLineArgs().Any(a => a.Equals("-tempoTyrantQuick", StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "tempo-tyrant-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || FindAnyObjectByType<TempoTyrantAutoTest>()) return;
            var go = new GameObject("Tempo Tyrant autotest"); DontDestroyOnLoad(go); go.AddComponent<TempoTyrantAutoTest>();
        }

        sealed class Bot : IInputSource
        {
            public Vector3 MoveWorld; public bool Sprint;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = who.transform.position + who.transform.forward, Sprint = Sprint };
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(MoveWorld, right), Vector3.Dot(MoveWorld, fwd)), 1);
                return r;
            }
        }

        sealed class Seen { public Hazard H; public string Source; public int Spawn, Resolve; public Tempo Tempo; public float Bpm; }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly List<(int tick, string source, int hazard, PlayerController who, int dmg)> hits = new List<(int, string, int, PlayerController, int)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true; Dictionary<string, int> matNames;
        TempoTyrantEncounter boss;
        int transitionHazardFrames, transitionFrames, comboDrift; float comboDriftMax;

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
            lines.Add(line); Debug.Log("[tempo-tyrant-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[tempo-tyrant-test] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        // a separate camera rendered into a texture (does not fight the director's follow camera)
        void Render(string file, Vector3 pos, Vector3 look, float fov, int w = 1280, int h = 720)
        {
            var go = new GameObject("capture camera"); var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main) { cam.CopyFrom(main); }
            cam.fieldOfView = fov; cam.aspect = w / (float)h; cam.orthographic = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
        void RenderShot(string name, Vector3 pos, Vector3 look, float fov) => Render(Path.Combine(dir, $"{++shots:00}_{name}.png"), pos, look, fov);

        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.deltaTime; Observe(); Keep(); yield return null; } }
        IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            float t = 0; while (!cond() && t < timeout) { t += Time.deltaTime; Observe(); Keep(); yield return null; }
            if (!cond()) Check("reached: " + what, false, $"timed out after {timeout}s");
        }

        // keep the roster standing unless a check needs damage to land; move the bot toward its goal
        Vector3? goal; bool goalSprint;
        void Keep()
        {
            if (d == null || d.World == null) return;
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < 60) p.DebugSetHp(100);
            var me = d.Player;
            if (me && goal.HasValue)
            {
                var to = Sim.Flat(goal.Value - me.transform.position);
                bot.MoveWorld = to.magnitude > 0.25f ? to.normalized : Vector3.zero; bot.Sprint = goalSprint && to.magnitude > 0.5f;
            }
            else bot.MoveWorld = Vector3.zero;
        }

        int perfFrames; float perfTime;
        void Observe()
        {
            if (d != null && d.State == RunState.Combat && !d.Paused) { perfFrames++; perfTime += Time.unscaledDeltaTime; }
            var w = d?.World; if (w == null) return;
            foreach (var h in w.Hazards.Live)
                if (!seen.ContainsKey(h.Id))
                    seen[h.Id] = new Seen { H = h, Source = h.Source, Spawn = w.Tick - h.Age, Resolve = w.Tick - h.Age + h.ResolveAt - 1,
                        Tempo = boss ? boss.Clock.State : Tempo.Haste, Bpm = boss ? boss.Clock.Bpm : 0 };
            if (boss && boss.Clock.Transitioning) { transitionFrames++; if (w.Hazards.Count > 0) transitionHazardFrames++; }
            if (boss && boss.Current == "Threefold Severance" && boss.SeveranceStrikes.Count > 0)
            {
                float drift = Sim.Flat(boss.transform.position - boss.LockedPos).magnitude + Mathf.Abs(Mathf.DeltaAngle(boss.transform.eulerAngles.y, boss.LockedYaw)) * 0.01f;
                comboDriftMax = Mathf.Max(comboDriftMax, drift); if (drift > 0.01f) comboDrift++;
            }
        }

        void OnHit(PlayerController p, Hazard h, int dmg) => hits.Add((d.World.Tick, h.Source, h.Id, p, dmg));

        IEnumerator Main()
        {
            // ---------------- registration and arena ----------------
            yield return new WaitForSeconds(1f);
            int map = RunMaps.IndexOf(RunDirector.TempoTyrantMap), enc = RunDirector.TempoTyrantPractice;
            Check("practice entry registered", enc >= 0 && RunDirector.Bosses[enc].Name.StartsWith("Tempo Tyrant") && !RunDirector.Bosses[enc].InRun);
            Check("run order unchanged (4 run bosses, Tyrant excluded)", RunDirector.RunOrder.Length == 4 && !RunDirector.RunOrder.Contains(enc));
            Check("Metronome Court map registered and in build", map >= 0 && RunMaps.InBuild(map) && !RunMaps.All[map].InShowcase);
            Check("showcase stages unchanged (5 original arenas)", RunMaps.Showcased.Length == 5);
            // a player build boots into the menu router: load the arena's run scene directly
            float boot = 0; while (RunDirector.Instance == null && boot < 4) { boot += Time.deltaTime; yield return null; }
            if (RunDirector.Instance == null) UnityEngine.SceneManagement.SceneManager.LoadScene(RunMaps.All[map].RunScene);
            while (RunDirector.Instance == null || RunDirector.Instance.State != RunState.Menu) yield return null;
            d = RunDirector.Instance;
            if (RunMaps.Current != map) { Check("practice loads its arena", d.StartPractice("Vanguard", enc)); }
            else Check("practice starts on its arena", d.StartPractice("Vanguard", enc));
            float t0 = 0; while ((RunDirector.Instance == null || RunDirector.Instance.State != RunState.Ready) && t0 < 30) { t0 += Time.deltaTime; yield return null; }
            d = RunDirector.Instance;
            Check("practice Ready on Metronome Court", d && d.State == RunState.Ready && RunMaps.Current == map && d.Mode == RunMode.Practice && d.Encounter == enc);
            var arena = FindAnyObjectByType<MetronomeCourtArena>();
            Check("arena present with separate frame/rod/weight/escapement", arena && arena.Frame && arena.Rod && arena.Weight && arena.Escapement && arena.Weight.parent == arena.Rod);
            Check("exactly one physical rod", arena && arena.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Rod") == 1);
            Check("arena bound is the 25 m floor", Mathf.Approximately(CombatWorld.PlayerBound, 12.5f) && !CombatWorld.SquareBound);
            float minA = 999, maxA = -999;
            for (float t = 0; t < 1.0f; t += Time.deltaTime) { if (arena) { minA = Mathf.Min(minA, arena.RodAngle); maxA = Mathf.Max(maxA, arena.RodAngle); } yield return null; }
            Check("readiness previews the metronome (non-damaging)", arena && arena.Preview && maxA - minA > 5f && d.World.Hazards.Count == 0, $"swing range {maxA - minA:0.0} deg");
            // menu preview image for the Test Rush map tile (960x540, the same view MapPreviewCapture uses)
            if (Application.isEditor)
            {
                var previewDir = Path.Combine(Application.dataPath, "LastBell/Menu/Resources/Menu/Maps"); Directory.CreateDirectory(previewDir);
                Render(Path.Combine(previewDir, RunMaps.All[map].Id + ".png"), new Vector3(0, 26, 20), Vector3.zero, 40, 960, 540);
                Note("wrote menu preview Resources/Menu/Maps/" + RunMaps.All[map].Id + ".png");
            }
            RenderShot("arena_overview", new Vector3(0, 26, 20), Vector3.zero, 40);
            RenderShot("arena_metronome_elevation", new Vector3(0, 6, 4), new Vector3(0, 5, MetronomeCourtArena.MetronomeZ), 50);
            RenderShot("arena_iso_south_approach", new Vector3(14, 20, 34), new Vector3(0, 2, -4), 45);
            d.Player.Input = bot;
            d.World.PlayerHit += OnHit;

            // ---------------- combat start ----------------
            Check("Ready accepted", d.RequestReady());
            yield return Until(() => d.Boss is TempoTyrantEncounter, 3, "boss spawned");
            boss = d.Boss as TempoTyrantEncounter;
            boss.ForcedNext = "Severance";
            yield return Until(() => d.State == RunState.Combat, 6, "combat");
            yield return Shot("gameplay_start_haste");
            Check("fight starts in Haste at beat 0", boss.Clock.State == Tempo.Haste && boss.Clock.Beat < 1 && boss.Clock.Commits == 0);
            Check("stand-in is labelled", boss.Placeholder && boss.Rig && boss.Rig.name.Contains("STAND-IN"));
            TempoTyrantHud.ShowDiagnostics = false;   // clean captures; the report carries the numbers
            Check("music: By My Measure paired with its 0.75x version", boss.Audio && boss.Audio.Music == TempoTyrantAudio.MusicStatus.Paired, boss.Audio ? boss.Audio.StatusText : "");
            var music = FindAnyObjectByType<LastBell.Menu.MenuMusic>();
            yield return Wait(1.0f);
            Check("music: Haste plays the 165 BPM version, beat-locked to the clock", music && music.Source && music.Source.clip && music.Source.clip.name == TempoTyrantAudio.HasteTrack && music.Source.isPlaying &&
                boss.Audio.MusicLocked && Mathf.Abs(boss.Audio.LastMusicError) < TempoTyrantAudio.ResyncTolerance, boss.Audio.StatusText);
            Check("Haste indicators: fewer beats AND shorter seconds than Normal", HasteShorter(), HasteTable());
            Note(HasteTable());
            var arenaNow = FindAnyObjectByType<MetronomeCourtArena>();
            Check("metronome reaches the gameplay frame: rod-shadow spot light with shadows + rim lantern chase", arenaNow && arenaNow.RodShadowLight && arenaNow.RodShadowLight.shadows != LightShadows.None && arenaNow.LanternCount >= 8,
                arenaNow ? $"lanterns {arenaNow.LanternCount}" : "");
            int shadowFrames = 0, sampled = 0;
            for (float t = 0; t < 1.5f; t += Time.deltaTime, sampled++)
            {
                if (arenaNow && arenaNow.RodShadowOnFloor && new Vector2(arenaNow.RodShadowTip.x, arenaNow.RodShadowTip.z).magnitude <= MetronomeCourtArena.FloorRadius + 0.01f) shadowFrames++;
                yield return null;
            }
            // the projected shadow sweeps across the dial through the middle of each swing and runs past the floor edge at the
            // extremes (a sweeping beam), so it must be on the floor for part of every swing, not all of it
            Check("rod shadow sweeps across the dial on every swing", sampled > 0 && shadowFrames >= Mathf.Max(1, sampled * 0.25f), $"{shadowFrames}/{sampled} frames on the floor");
            Check("world lighting in its Haste (warm) state", arenaNow && arenaNow.TempoBlend > 0.9f, arenaNow ? $"blend {arenaNow.TempoBlend:0.00}" : "");
            yield return BossCloseups("haste");

            // ---------------- Threefold Severance (Haste) ----------------
            yield return Severance("haste", 165f);

            // ---------------- tempo change (Haste -> Normal) ----------------
            goal = new Vector3(0, 0, 7);
            yield return Until(() => boss.Clock.Commits >= 1, 40, "first tempo commit");
            if (boss.Clock.Commits >= 1)
            {
                var c = boss.Clock.History[0];
                Check("Haste lasted at least its nominal 12 s and changed at a block boundary", c.to == Tempo.Normal && c.stateTicks >= boss.Clock.HasteTicks, $"actual {c.stateTicks / 60f:0.00} s");
                Note($"actual Haste passage {c.stateTicks / 60f:0.00} s (nominal 12 s; extra = rest of the block + 3 transition ticks)");
            }
            Check("no hazard live during the three accented transition ticks", transitionFrames > 0 && transitionHazardFrames == 0, $"{transitionHazardFrames}/{transitionFrames} frames");
            Check("music rate derived from tempo (0.75 outside Haste)", Mathf.Approximately(boss.Clock.MusicRate, 0.75f));
            yield return Wait(0.6f);
            var arenaN = FindAnyObjectByType<MetronomeCourtArena>();
            Check("world lighting follows tempo into its Normal (cool) state", arenaN && arenaN.TempoBlend < 0.1f, arenaN ? $"blend {arenaN.TempoBlend:0.00}" : "");
            Check("music: Normal swaps to the 0.75x version at the same beat, still locked", music && music.Source.clip && music.Source.clip.name == TempoTyrantAudio.SlowTrack && music.Source.isPlaying &&
                boss.Audio.MusicSwaps >= 1 && Mathf.Abs(boss.Audio.LastMusicError) < TempoTyrantAudio.ResyncTolerance, boss.Audio.StatusText);
            yield return Wait(0.4f);
            yield return Shot("gameplay_normal_tempo");
            yield return BossCloseups("normal");
            boss.ForcedNext = "Severance";
            yield return Severance("normal", 123.75f);

            // ---------------- Sundering Wake ----------------
            yield return Wake();

            // ---------------- Hollow Crown ----------------
            yield return Crown();

            // ---------------- pursuit, closing rush, target invalidation, party counts ----------------
            yield return Pursuit();

            // ---------------- frenzy ----------------
            yield return Frenzy();

            // ---------------- pause / resume ----------------
            yield return PauseResume();

            // ---------------- defeat, retry cleanup, menu return ----------------
            yield return DefeatAndRetry();

            // ---------------- whole-route feasibility (authoritative geometry) ----------------
            Routes();

            Note($"performance: {perfFrames} combat frames, average {(perfTime > 0 ? perfFrames / perfTime : 0):0.0} fps at {Screen.width}x{Screen.height}");
            Check("no errors or exceptions logged", errors == 0, $"{errors}");
            Finish();
        }

        IEnumerator BossCloseups(string tag)
        {
            if (!boss) yield break;
            var p = boss.transform.position; var f = boss.transform.forward;
            RenderShot("boss_front_" + tag, p + f * 7.5f + Vector3.up * 2.4f, p + Vector3.up * 1.9f, 40);
            RenderShot("boss_back_" + tag, p - f * 7.5f + Vector3.up * 2.4f, p + Vector3.up * 1.9f, 40);
            if (boss.Rig && boss.Rig.Sword) RenderShot("boss_weapon_" + tag, boss.Rig.Sword.position + boss.transform.right * 3.5f + Vector3.up * 0.6f, boss.Rig.Sword.position + boss.Rig.Sword.up * 1.4f, 45);
            yield return null;
        }

        // place the bot inside all three strikes (front-left of the boss) to prove one hit per strike, three distinct
        IEnumerator Severance(string tag, float bpm)
        {
            int start = hits.Count; keepAlive = true;
            yield return Until(() => boss.Current == "Threefold Severance" && boss.SeveranceStrikes.Count == 1, 30, "Severance " + tag);
            if (boss.SeveranceStrikes.Count == 0) yield break;
            var bp = boss.LockedPos; float yaw = boss.LockedYaw; var fwd = Sim.Dir(yaw); var right = Vector3.Cross(Vector3.up, fwd);
            var spot = bp + fwd * 3.2f - right * 1.2f;
            d.Player.transform.position = d.World.Constrain(spot, PlayerController.Radius); goal = spot;
            yield return Wait(0.15f);
            yield return Shot($"severance_{tag}_preview_123");
            // geometry from the live strikes
            var s1 = TempoTyrantEncounter.SeveranceStrike(1, bp, yaw, boss.BodyRadius); var s2 = TempoTyrantEncounter.SeveranceStrike(2, bp, yaw, boss.BodyRadius); var s3 = TempoTyrantEncounter.SeveranceStrike(3, bp, yaw, boss.BodyRadius);
            Check($"Severance {tag}: 1 left cleave leaves the boss's right safe", s1.Contains(bp - right * 5) && !s1.Contains(bp + right * 5) && s1.Contains(bp - fwd * 5 - right * 2));
            Check($"Severance {tag}: 2 right cleave mirrors it", s2.Contains(bp + right * 5) && !s2.Contains(bp - right * 5));
            Check($"Severance {tag}: 3 overhead hits the centre, flanks safe", s3.Contains(bp + fwd * 6) && !s3.Contains(bp - right * 4.5f + fwd * 4) && !s3.Contains(bp + right * 4.5f + fwd * 4));
            Check($"Severance {tag}: telegraphs are clipped to the floor", boss.SeveranceStrikes[0].ClipRadius > 0 && boss.SeveranceStrikes[0].ClipRadius <= CombatWorld.PlayerBound + 0.5f);
            int beforeStrikes = boss.SeveranceStrikes.Count;
            yield return Until(() => boss.SeveranceStrikes.Count >= 2, 10, "strike 2 live"); yield return Wait(0.05f);
            Check($"Severance {tag}: strike 2 is the boxed number once strike 1 is spent", boss.Numbers.Count(x => x.imminent) == 1 && boss.Numbers.First(x => x.imminent).number == 2,
                string.Join(",", boss.Numbers.Select(x => x.number + (x.imminent ? "*" : ""))));
            yield return Shot($"severance_{tag}_strike2_imminent");
            yield return Until(() => boss.SeveranceStrikes.Count >= 3, 10, "strike 3 live"); yield return Wait(0.05f);
            yield return Shot($"severance_{tag}_strike3_imminent");
            yield return Until(() => boss.Current != "Threefold Severance", 10, "combo end");
            yield return Wait(0.3f);
            var mine = hits.Skip(start).Where(h => h.who == d.Player && h.source.StartsWith("Threefold Severance")).ToList();
            Check($"Severance {tag}: three distinct strikes hit once each", mine.Count == 3 && mine.Select(h => h.hazard).Distinct().Count() == 3 && mine.Select(h => h.source).Distinct().Count() == 3,
                string.Join(", ", mine.Select(h => $"{h.source}@{h.tick}")));
            var sev = seen.Values.Where(s => s.Source.StartsWith("Threefold Severance")).OrderBy(s => s.Spawn).ToList();
            var last3 = sev.Skip(Mathf.Max(0, sev.Count - 3)).ToList();
            if (last3.Count == 3)
            {
                var tp = bpm >= 140 ? Tempo.Haste : Tempo.Normal; float gapB = TempoTyrantEncounter.Of(TempoTyrantEncounter.SevGap, tp), openB = TempoTyrantEncounter.Of(TempoTyrantEncounter.SevOpen, tp);
                int gap = TempoClock.TicksFor(gapB, bpm), open = TempoClock.TicksFor(openB, bpm);
                int g1 = last3[1].Resolve - last3[0].Resolve, g2 = last3[2].Resolve - last3[1].Resolve;
                Check($"Severance {tag}: impacts {gapB} beats apart at {bpm} BPM", g1 == gap && g2 == gap, $"{g1}, {g2} ticks (expected {gap})");
                Check($"Severance {tag}: opening warning {openB} beats", last3[0].Resolve - last3[0].Spawn + 1 == open, $"{last3[0].Resolve - last3[0].Spawn + 1} ticks (expected {open})");
                Check($"Severance {tag}: tempo fixed for the whole block", last3.All(s => Mathf.Approximately(s.Bpm, bpm)));
            }
            Check($"Severance {tag}: position and facing locked during the combo", comboDrift == 0, $"max drift {comboDriftMax:0.000}");
        }

        IEnumerator Wake()
        {
            keepAlive = true;
            yield return Until(() => boss.Current == "between blocks", 30, "boundary before Wake");
            // a second roster player stands beside the lane to step into the fissure after the charge
            PlayerController buddy = null;
            var far = d.World.Constrain(new Vector3(0, 0, 11.5f), PlayerController.Radius);
            d.Player.transform.position = far; goal = far;
            boss.ForcedNext = "Wake";
            int startHits = hits.Count, eruptions0 = boss.FissureEruptions;
            yield return Until(() => boss.Current == "Sundering Wake", 20, "Wake");
            int wakeStart = d.World.Tick; var lane = boss.LastWake;
            yield return Wait(0.2f);
            yield return Shot("wake_warning_path_endpoint");
            int warnTicks = boss.Clock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.WakeWarn, boss.Clock.State));
            yield return Until(() => boss.Rig.Current == TyrantPose.Thrust, 6, "Wake travel");
            yield return Shot("wake_travel");
            yield return Until(() => boss.LastFissure != null && boss.LastFissure != lane && d.World.Hazards.Live.Contains(boss.LastFissure), 4, "fissure warning");
            var fissure = boss.LastFissure;
            if (fissure != null)
            {
                // step the buddy into the fissure lane, midway along it
                var mid = fissure.Origin + Sim.Dir(fissure.Yaw) * (fissure.Length * 0.5f);
                buddy = AddExtra(mid);
                yield return Wait(0.1f);
                yield return Shot("wake_fissure_warning");
            }
            yield return Until(() => boss.FissureEruptions > eruptions0, 6, "eruption");
            yield return Wait(0.6f);
            var mine = hits.Skip(startHits).ToList();
            var charge = mine.Where(h => h.source == "Sundering Wake (charge)" && h.who == d.Player).ToList();
            Check("Wake: traveling strike hits the player in the lane once", charge.Count == 1, string.Join(",", charge.Select(h => h.tick)));
            Check("Wake: no damage before the travel reaches the player", charge.All(h => h.tick - wakeStart >= warnTicks), charge.Count > 0 ? $"hit {charge[0].tick - wakeStart} ticks after start, warning {warnTicks}" : "");
            Check("Wake: fissure is a separate hazard instance with its own warning", fissure != null && lane != null && fissure.Id != lane.Id && fissure.Source.Contains("eruption") && fissure.ResolveAt > 0,
                fissure != null ? $"charge #{lane?.Id}, eruption #{fissure.Id}, eruption warning {fissure.ResolveAt} ticks" : "");
            Check("Wake: fissure erupts exactly once", boss.FissureEruptions == eruptions0 + 1);
            var erupt = mine.Where(h => h.source == "Sundering Wake (eruption)").ToList();
            Check("Wake: eruption hits a player standing in the wake once", buddy != null && erupt.Count(h => h.who == buddy) == 1, $"{erupt.Count} eruption hits");
            Check("Wake: endpoint inside the boss bound", new Vector2(boss.transform.position.x, boss.transform.position.z).magnitude <= TempoTyrantEncounter.OwnBound + 0.05f);
            RemoveExtras();
            yield return Until(() => boss.Current == "between blocks" || boss.Current.StartsWith("pursuit") || boss.Current.StartsWith("Closing"), 10, "Wake block ends");
            Check("Wake: no lingering hazards after the block", !d.World.Hazards.Live.Any(h => h.Source.StartsWith("Sundering Wake")));
        }

        IEnumerator Crown()
        {
            keepAlive = true;
            yield return Until(() => boss.Current == "between blocks", 30, "boundary before Crown");
            boss.ForcedNext = "Crown";
            int startHits = hits.Count;
            yield return Until(() => boss.Current == "Hollow Crown", 20, "Crown");
            var c = Sim.Flat(boss.transform.position);
            var outside = d.World.Constrain(c + new Vector3(0, 0, 8.5f), PlayerController.Radius);
            d.Player.transform.position = outside; goal = outside;
            Check("Crown starts near the arena centre", TempoTyrantEncounter.ArenaCentred(c));
            yield return Wait(0.2f);
            yield return Shot("crown_ring_warning_pocket");
            yield return Until(() => boss.LastInner != null && d.World.Hazards.Live.Contains(boss.LastInner), 8, "inner circle warning");
            yield return Wait(0.1f);
            yield return Shot("crown_inner_warning");
            yield return Until(() => boss.Current != "Hollow Crown", 8, "Crown resolves");
            var mine = hits.Skip(startHits).Where(h => h.who == d.Player).ToList();
            Check("Crown: outer ring hits a player outside the pocket once; inner circle does not", mine.Count(h => h.source.Contains("outer ring")) == 1 && mine.Count(h => h.source.Contains("inner circle")) == 0,
                string.Join(", ", mine.Select(h => h.source)));
            var ring = seen.Values.LastOrDefault(s => s.Source.Contains("outer ring")); var inner = seen.Values.LastOrDefault(s => s.Source.Contains("inner circle"));
            if (ring != null && inner != null)
                Check("Crown: separate warnings, ring then circle", inner.Spawn >= ring.Resolve && inner.Resolve - ring.Resolve == TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.CrownInnerWarn, inner.Tempo), inner.Bpm),
                    $"ring {ring.Resolve}, inner {inner.Spawn}->{inner.Resolve}");
            Check("Crown: pocket fits a player outside the boss body", TempoTyrantEncounter.CrownPocket - Hazard.Margin > 1.4f + PlayerController.Radius + 1f);
        }

        PlayerController AddExtra(Vector3 at)
        {
            var p = PlayerController.Spawn(d.World, RunDirector.Classes[extras.Count % 3], d.World.Constrain(at, PlayerController.Radius), 180);
            p.Input = new Bot(); p.AbilitiesEnabled = false; extras.Add(p); return p;
        }
        void RemoveExtras()
        {
            foreach (var p in extras) if (p) { d.World.Players.Remove(p); Destroy(p.gameObject); }
            extras.Clear();
        }

        IEnumerator Pursuit()
        {
            keepAlive = true;
            yield return Until(() => boss.Current == "between blocks", 30, "boundary before pursuit");
            // party counts through eight (simulated roster, not co-op): deterministic farthest-target selection
            foreach (int n in new[] { 2, 4, 8 })
            {
                RemoveExtras();
                for (int i = 1; i < n; i++) AddExtra(Quaternion.Euler(0, i * 360f / n, 0) * Vector3.forward * (3 + i));
                yield return Wait(Quick ? 2f : 5f);
                var t = boss.SelectPursuitTarget();
                Check($"party {n}: pursuit target is a standing roster player", t && !t.Down && d.World.Players.Contains(t));
                yield return Shot($"party_{n}");
            }
            RemoveExtras();
            // a distant player draws an announced, non-damaging closing rush
            var corner = d.World.Constrain(new Vector3(-9, 0, 9), PlayerController.Radius);
            d.Player.transform.position = corner; goal = corner;
            int hp = d.Player.Hp, rushes = boss.Rushes, rushHits = hits.Count;
            yield return Until(() => boss.Current == "Closing Rush (announced)", 40, "closing rush tell");
            yield return Shot("closing_rush_tell");
            yield return Until(() => boss.Rushes > rushes && boss.Current != "Closing Rush", 6, "rush travel");
            Check("closing rush announced and non-damaging", boss.Rushes > rushes && hits.Skip(rushHits).All(h => !h.source.Contains("Rush")));
            Check("rush stops at fighting distance inside the bound", new Vector2(boss.transform.position.x, boss.transform.position.z).magnitude <= TempoTyrantEncounter.OwnBound + 0.05f &&
                Sim.Flat(d.Player.transform.position - boss.transform.position).magnitude <= TempoTyrantEncounter.FightDist + 1.5f,
                $"distance {Sim.Flat(d.Player.transform.position - boss.transform.position).magnitude:0.0} m");
            // target invalidation: a far roster player goes down during the tell
            goal = Sim.Flat(boss.transform.position) + Vector3.forward * 3; d.Player.transform.position = d.World.Constrain(goal.Value, PlayerController.Radius);
            yield return Until(() => boss.Current == "between blocks", 30, "boundary");
            var farP = AddExtra(-Sim.Flat(boss.transform.position).normalized * 12f);
            int cancels = boss.RushCancels;
            yield return Until(() => boss.Current == "Closing Rush (announced)" && boss.PursuitTarget == farP, 45, "rush toward far roster player");
            if (boss.Current == "Closing Rush (announced)")
            {
                keepAlive = false;
                farP.ReceiveHit(new Hazard { Source = "test: down", Damage = 999 });
                keepAlive = true;
                yield return Wait(0.3f);
                Check("target down during the rush tell cancels it safely", boss.RushCancels == cancels + 1 && boss.PursuitTarget != farP, $"current {boss.Current}");
            }
            RemoveExtras();
        }

        IEnumerator Frenzy()
        {
            keepAlive = true;
            goal = null;
            boss.DebugSetHpFraction(0.2f);
            int blocks = boss.Blocks;
            yield return Until(() => boss.Blocks >= blocks + 1, 30, "block at exactly 20%");
            Check("exactly 20% does not trigger frenzy (strictly below)", !boss.Frenzy && boss.FrenzyTriggers == 0, $"hp {boss.Hp}/{boss.MaxHp}");
            boss.DebugSetHpFraction(0.19f);
            yield return Until(() => boss.Frenzy, 30, "frenzy");
            yield return Shot("frenzy_tell");
            var arena = FindAnyObjectByType<MetronomeCourtArena>();
            yield return Wait(0.2f);
            Check("frenzy: cosmetic metronome cracks shown", arena && arena.ShowingCracks);
            RenderShot("frenzy_metronome_cracks", new Vector3(0, 6, 4), new Vector3(0, 5, MetronomeCourtArena.MetronomeZ), 50);
            int chains = boss.ChainsRun;
            // stand still near the centre: the chain may charge past; the bot is kept standing
            yield return Until(() => boss.ChainsRun > chains, 50, "first Broken Cadence chain");
            yield return Until(() => boss.Current == "Threefold Severance", 20, "chain Severance");
            yield return Shot("frenzy_chain_severance");
            yield return Until(() => boss.Current.StartsWith("Threefold Severance (recovery)") || boss.Current == "between blocks", 20, "chain end");
            Check("chain: fissure erupts >= 8 beats before strike 1", boss.LastStrike1ResolveAge - boss.LastFissureResolveAge >= boss.Clock.TicksFor(TempoTyrantEncounter.FissureLeadBeats) - 1,
                $"eruption {boss.LastFissureResolveAge}, strike 1 {boss.LastStrike1ResolveAge}");
            if (!Quick)
            {
                yield return Until(() => boss.ChainsRun > chains + 1, 150, "chain on the next Haste passage");
                Check("frenzy triggered exactly once", boss.FrenzyTriggers == 1);
                var log = boss.BlockLog.Where(l => l.EndsWith("Broken Cadence chain")).ToList();
                Check("chains only in Haste, once per passage", log.Count == boss.ChainsRun && log.All(l => l.Contains(" Haste ")), string.Join(" | ", log));
            }
            Note("block log: " + string.Join(" | ", boss.BlockLog));
            Note("tempo history: " + string.Join(" | ", boss.Clock.History.Select(h => $"tick {h.tick} -> {h.to} after {h.stateTicks / 60f:0.00}s")));
        }

        IEnumerator PauseResume()
        {
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Age < h.ResolveAt - 30), 30, "a pending hazard");
            double beat = boss.Clock.Beat; int tick = d.World.Tick; var ages = d.World.Hazards.Live.Select(h => h.Age).ToArray(); int hp = d.Player.Hp;
            d.SetPaused(true);
            yield return new WaitForSeconds(1.5f);
            Check("pause freezes tempo, tick and hazards", boss.Clock.Beat == beat && d.World.Tick == tick && d.World.Hazards.Live.Select(h => h.Age).SequenceEqual(ages));
            var srcs = boss.Audio.GetComponents<AudioSource>();
            Check("pause silences scheduled metronome ticks", srcs.All(s => !s.isPlaying));
            var mus = FindAnyObjectByType<LastBell.Menu.MenuMusic>();
            Check("pause pauses this fight's music", mus && mus.Source && !mus.Source.isPlaying);
            yield return Shot("paused");
            int scheduled = boss.Audio.TicksScheduled;
            d.SetPaused(false);
            yield return null; yield return null;
            Check("resume does not burst ticks or damage", d.World.Tick - tick <= 13 &&   // two resume frames at the director's 0.1 s/frame cap (6 ticks) + 1
                 boss.Audio.TicksScheduled - scheduled <= 1 && d.Player.Hp >= hp - 40, $"ticks +{d.World.Tick - tick}, scheduled +{boss.Audio.TicksScheduled - scheduled}");
            yield return Wait(0.3f);
            Check("resume resumes the music beat-locked", mus && mus.Source.isPlaying && Mathf.Abs(boss.Audio.LastMusicError) < TempoTyrantAudio.ResyncTolerance, boss.Audio.StatusText);
        }

        IEnumerator DefeatAndRetry()
        {
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Age < h.ResolveAt - 10), 30, "a pending hazard before defeat");
            int pending = d.World.Hazards.Count; int hp = d.Player.Hp; int hitsBefore = hits.Count;
            yield return Shot("before_defeat");
            boss.DebugDefeat();
            Check("defeat cancels outstanding damage in the killing tick", d.World.Hazards.Count == 0, $"{pending} -> {d.World.Hazards.Count}");
            Check("defeated boss stops colliding", !boss.Solid);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("defeat");
            Check("no damage after defeat", hits.Count == hitsBefore);
            Check("defeat stops metronome tick audio", boss.Audio.GetComponents<AudioSource>().All(s => !s.isPlaying));
            yield return Until(() => d.State == RunState.Results, 8, "practice results");
            Check("practice ends in a practice result", d.State == RunState.Results && d.Victory && d.Mode == RunMode.Practice);

            // fresh attempts: everything resets, nothing accumulates
            int mats0 = 0, meshes0 = 0, gos0 = 0;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Check($"retry {attempt} accepted", d.RequestRetry());
                yield return null;
                d.Player.Input = bot; d.World.PlayerHit += OnHit;
                Check($"retry {attempt}: no stale hazards or bosses", d.State == RunState.Ready && d.World.Hazards.Count == 0 && FindObjectsByType<TempoTyrantEncounter>().Length == 0);
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 6, "retry combat");
                boss = d.Boss as TempoTyrantEncounter;
                Check($"retry {attempt}: fresh tempo/frenzy/phase", boss && boss.Clock.State == Tempo.Haste && boss.Clock.Beat < 1 && !boss.Frenzy && boss.FrenzyTriggers == 0 && boss.ChainsRun == 0 && boss.Hp == boss.MaxHp && boss.Numbers.Count == 0);
                var arena = FindAnyObjectByType<MetronomeCourtArena>();
                Check($"retry {attempt}: metronome cracks reset", arena && !arena.ShowingCracks);
                yield return Wait(4f);
                boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Results, 8, "retry results");
                yield return new WaitForSeconds(0.5f);
                yield return Resources.UnloadUnusedAssets(); yield return null;
                var names = Resources.FindObjectsOfTypeAll<Material>().GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.Count());
                if (attempt == 1) matNames = names;
                else Note("material growth: " + string.Join(", ", names.Where(kv => !matNames.ContainsKey(kv.Key) || matNames[kv.Key] < kv.Value).Select(kv => $"{kv.Key} x{kv.Value}")));
                int mats = Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.StartsWith("TempoTyrant") || m.name.StartsWith("Telegraph") || m.name.Contains("Tyrant")),
                    allMats = Resources.FindObjectsOfTypeAll<Material>().Length, meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length, gos = FindObjectsByType<GameObject>().Length;
                Note($"attempt {attempt}: encounter materials {mats} (all materials {allMats}), meshes {meshes}, scene objects {gos}");
                if (attempt == 1) { mats0 = mats; meshes0 = meshes; gos0 = gos; }
                else Check($"retry {attempt}: no accumulating encounter materials/meshes/objects", mats <= mats0 && meshes <= meshes0 + 2 && gos <= gos0, $"{mats0}->{mats}, {meshes0}->{meshes}, {gos0}->{gos}");
            }
            Check("return to menu accepted", d.ReturnToMenu());
            yield return null;
            Check("menu return leaves no boss, hazards or fissure", d.State == RunState.Menu && FindObjectsByType<TempoTyrantEncounter>().Length == 0 && GameObject.Find("Wake fissure (cosmetic)") == null);
            var ar = FindAnyObjectByType<MetronomeCourtArena>();
            yield return new WaitForSeconds(0.3f);
            Check("metronome returns to preview after menu return", ar && ar.Preview);
        }

        void Routes()
        {
            float bound = CombatWorld.PlayerBound;
            var bosses = new[] { Vector3.zero, new Vector3(1.5f, 0, 0), new Vector3(0, 0, 1.5f), new Vector3(-1.06f, 0, -1.06f), new Vector3(1.06f, 0, 1.06f) };
            foreach (float bpm in new[] { 165f, 123.75f })
            {
                int starts = 0, ok = 0; float worst = float.MaxValue; string worstAt = "";
                foreach (var b in bosses)
                    for (int y = 0; y < 360; y += 45)
                    {
                        var r = TempoTyrantRoutes.Severance(b, y, bpm, PlayerController.WalkSpeed, bound);
                        starts += r.Starts; ok += r.Feasible; if (r.WorstSlack < worst) { worst = r.WorstSlack; worstAt = r.ToString(); }
                        foreach (var f in r.Failures.Take(2)) Note("route failure: " + f);
                    }
                Check($"routes: Threefold Severance at {bpm} BPM, walking, from every sampled legal start", ok == starts, $"{ok}/{starts}, worst slack {worst:0.00} s");
                Note("worst: " + worstAt);
                starts = 0; ok = 0; worst = float.MaxValue;
                foreach (var b in bosses)
                {
                    var r = TempoTyrantRoutes.Crown(b, bpm, PlayerController.WalkSpeed, bound);
                    starts += r.Starts; ok += r.Feasible; worst = Mathf.Min(worst, r.WorstSlack);
                    foreach (var f in r.Failures.Take(2)) Note("route failure: " + f);
                }
                Check($"routes: Hollow Crown in then out at {bpm} BPM, walking", ok == starts, $"{ok}/{starts}, worst slack {worst:0.00} s");
            }
            // chain: charges ending near the centre from several directions; fissure lane up during the opening.
            // The fissure always shows its full 5-beat warning from the moment the charge ends (players react from then),
            // and strike 1 resolves >= 8 beats after the eruption (the opening is lengthened to guarantee it).
            int cs = 0, cok = 0; float cw = float.MaxValue;
            foreach (var split in new[] { (TempoTyrantEncounter.FissureWarn.H, (float)TempoTyrantEncounter.FissureLeadBeats) })
                foreach (var from in new[] { new Vector3(0, 0, 8), new Vector3(8, 0, 0), new Vector3(-6, 0, -6), new Vector3(5, 0, -7) })
                    foreach (int y in new[] { 0, 90, 200, 315 })
                    {
                        var end = Sim.Flat(-from).normalized * 1.5f;
                        var r = TempoTyrantRoutes.Chain(from, end, y, 165f, PlayerController.WalkSpeed, bound, split.Item1, split.Item2);
                        cs += r.Starts; cok += r.Feasible; cw = Mathf.Min(cw, r.WorstSlack);
                        foreach (var f in r.Failures.Take(2)) Note("route failure: " + f);
                    }
            Check("routes: Broken Cadence chain (fissure then 1-2-3) at 165 BPM, walking", cok == cs, $"{cok}/{cs}, worst slack {cw:0.00} s");
        }

        static readonly (string name, (float H, float N) w)[] Windows =
        {
            ("Severance opening", TempoTyrantEncounter.SevOpen), ("Severance gap", TempoTyrantEncounter.SevGap), ("Wake warning", TempoTyrantEncounter.WakeWarn),
            ("fissure warning", TempoTyrantEncounter.FissureWarn), ("Crown ring", TempoTyrantEncounter.CrownRing), ("Crown inner", TempoTyrantEncounter.CrownInnerWarn),
            ("rush tell", TempoTyrantEncounter.RushTell),
        };
        static bool HasteShorter()
        {
            var c = new TempoClock();
            return Windows.All(x => x.w.H <= x.w.N && TempoClock.TicksFor(x.w.H, c.HasteBpm) < TempoClock.TicksFor(x.w.N, c.NormalBpm)) &&
                   Windows.Count(x => x.w.H < x.w.N) >= 4;
        }
        static string HasteTable()
        {
            var c = new TempoClock();
            return "windows Haste/Normal: " + string.Join("; ", Windows.Select(x => $"{x.name} {x.w.H}/{x.w.N} beats = {TempoClock.TicksFor(x.w.H, c.HasteBpm) / 60f:0.00}/{TempoClock.TicksFor(x.w.N, c.NormalBpm) / 60f:0.00} s"));
        }

        void Finish()
        {
            lines.Insert(0, $"Tempo Tyrant autotest: {pass} passed, {fail} failed  ({(Application.isEditor ? "Editor Play Mode" : "Windows player")}, Unity {Application.unityVersion})");
            File.WriteAllLines(Path.Combine(dir, "tempo-tyrant-report.txt"), lines);
            Debug.Log($"[tempo-tyrant-test] done: {pass} passed, {fail} failed");
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
