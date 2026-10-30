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
        // frame-pacing runs (docs/frame-pacing.md): -tempoTyrantFps <n> or -tempoTyrantVsync for this run only (never saved)
        static void ApplyFrameOverride()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("-tempoTyrantFps", StringComparison.OrdinalIgnoreCase));
            if (args.Any(a => a.Equals("-tempoTyrantVsync", StringComparison.OrdinalIgnoreCase))) { QualitySettings.vSyncCount = 1; Application.targetFrameRate = -1; }
            else if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int fps)) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
        }
        static string FrameMode => QualitySettings.vSyncCount > 0 ? "VSync" : Application.targetFrameRate > 0 ? Application.targetFrameRate + " FPS cap" : "unlimited";
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

        // Spiral Slices bot: walks to the centre of the current safe slice at ChaseRadius (during the spiral), or of the
        // nearest slice in the safe parity (every-other phase). Idle bots never move.
        sealed class ChaseBot : IInputSource
        {
            public const float ChaseRadius = 5f;
            public Func<TempoTyrantEncounter> Boss;
            public PlayerInput Read(PlayerController who)
            {
                var r = new PlayerInput { Aim = who.transform.position + who.transform.forward };
                var b = Boss(); if (!b) return r;
                int target = -1, mine = TempoTyrantEncounter.SliceOf(who.transform.position);
                if (b.SafeSlice >= 0) target = b.SafeSlice;
                else if (b.AltSafeParity >= 0) target = mine % 2 == b.AltSafeParity ? mine : TempoTyrantEncounter.Wrap(mine + 1);
                if (target < 0) return r;
                var goal = Sim.Dir(TempoTyrantEncounter.SliceYaw(target)) * ChaseRadius;
                var to = Sim.Flat(goal - who.transform.position); if (to.magnitude < 0.2f) return r;
                var cam = Camera.main; var fwd = cam ? Sim.Flat(cam.transform.forward).normalized : Vector3.forward;
                var right = Vector3.Cross(Vector3.up, fwd);
                r.Move = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(to.normalized, right), Vector3.Dot(to.normalized, fwd)), 1);
                return r;
            }
        }

        sealed class Seen { public Hazard H; public string Source; public int Spawn, Resolve; public Tempo Tempo; public float Bpm; }

        RunDirector d; readonly Bot bot = new Bot();
        readonly List<string> lines = new List<string>(); int pass, fail, errors, shots;
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly List<(int tick, string source, int hazard, PlayerController who, int dmg)> hits = new List<(int, string, int, PlayerController, int)>();
        readonly List<PlayerController> extras = new List<PlayerController>();
        string dir; bool keepAlive = true; int keepAbove = 60; Dictionary<string, int> matNames;
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
            foreach (var p in d.World.Players) if (keepAlive && !p.Down && p.Hp < keepAbove) p.DebugSetHp(100);
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
                // the sim pose: the drawn pose trails it by up to one tick (SimSmoother), e.g. right after the dash in
                var simPos = SimSmoother.SimPosition(boss.transform); var simYaw = SimSmoother.SimRotation(boss.transform).eulerAngles.y;
                float drift = Sim.Flat(simPos - boss.LockedPos).magnitude + Mathf.Abs(Mathf.DeltaAngle(simYaw, boss.LockedYaw)) * 0.01f;
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
            Check("Metronome Court map registered and in build", map >= 0 && RunMaps.InBuild(map) && RunMaps.All[map].InShowcase);
            Check("showcase stages: 5 original arenas + Metronome Court last", RunMaps.Showcased.Length == 6 && RunMaps.Showcased[5].Id == "metronome-court");
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
            ApplyFrameOverride(); Note("frame mode: " + FrameMode);
            yield return Shot("gameplay_start_haste");
            Check("fight starts in Haste at beat 0", boss.Clock.State == Tempo.Haste && boss.Clock.Beat < 1 && boss.Clock.Commits == 0);
            Check("Meshy model and greatsword loaded (stand-in hidden)", boss.Rig && boss.Rig.HasModel && !boss.Placeholder && boss.Rig.transform.Find("Greatsword (Meshy)") && boss.Rig.Presenter.Has("Walk") && boss.Rig.Presenter.Has("Run"),
                boss.Rig ? boss.Rig.name : "no rig");
            TempoTyrantHud.ShowDiagnostics = false;   // clean captures; the report carries the numbers
            Check("music: By My Measure paired with its 1.33x Haste version", boss.Audio && boss.Audio.Music == TempoTyrantAudio.MusicStatus.Paired, boss.Audio ? boss.Audio.StatusText : "");
            var music = FindAnyObjectByType<LastBell.Menu.MenuMusic>();
            yield return Wait(1.0f);
            Check("music: Haste plays the 1.33x (220 BPM) version, beat-locked to the clock", music && music.Source && music.Source.clip && music.Source.clip.name == TempoTyrantAudio.HasteTrack && music.Source.isPlaying &&
                boss.Audio.MusicLocked && Mathf.Abs(boss.Audio.LastMusicError) < TempoTyrantAudio.ResyncTolerance, boss.Audio.StatusText);
            Check("Haste indicators: fewer beats AND shorter seconds than Normal", HasteShorter(), HasteTable());
            Note(HasteTable());
            var arenaNow = FindAnyObjectByType<MetronomeCourtArena>();
            Check("metronome reaches the gameplay frame: tempo-tinted spot light + rim lantern chase", arenaNow && arenaNow.TempoSpotLight && Near(arenaNow.TempoSpotLight.color, MetronomeCourtArena.HasteLightTint) && arenaNow.LanternCount >= 8,
                arenaNow ? $"lanterns {arenaNow.LanternCount}" : "");
            // Risxhb: no metronome shadow on the arena floor; the lighting alone signals Haste/Normal
            Check("metronome casts no shadow on the arena floor", arenaNow && arenaNow.TempoSpotLight.shadows == LightShadows.None && !arenaNow.transform.Find("Rod shadow (projected)"),
                arenaNow ? $"spot shadows {arenaNow.TempoSpotLight.shadows}" : "");
            Check("world lighting in its Haste (warm) state", arenaNow && arenaNow.TempoBlend > 0.9f, arenaNow ? $"blend {arenaNow.TempoBlend:0.00}" : "");
            // asset F: lighting carries the tempo read anywhere on the court (rim light + beat pulse), dusk sky, fog off the court
            Check("tempo rim light: shadowless, tinted pale gold in Haste", arenaNow && arenaNow.TempoRimLight && arenaNow.TempoRimLight.shadows == LightShadows.None && Near(arenaNow.TempoRimLight.color, MetronomeCourtArena.HasteLightTint),
                arenaNow && arenaNow.TempoRimLight ? $"rim {arenaNow.TempoRimLight.intensity:0.00}" : "");
            float gearA0 = arenaNow ? arenaNow.GearAngle : 0;   // sampled before the 1.5 s window below
            float pulseMin = 1, pulseMax = 0;
            for (float t = 0; t < 1.5f; t += Time.deltaTime) { if (arenaNow) { pulseMin = Mathf.Min(pulseMin, arenaNow.BeatPulse); pulseMax = Mathf.Max(pulseMax, arenaNow.BeatPulse); } yield return null; }
            Check("metronome weight sits low in Haste (asset A)", arenaNow && arenaNow.WeightHeight < MetronomeCourtArena.WeightHaste + 0.4f, arenaNow ? $"weight {arenaNow.WeightHeight:0.00}" : "");
            Check("lights pulse on the beat", pulseMax - pulseMin > 0.5f && arenaNow.LightsPulse, $"pulse {pulseMin:0.00}..{pulseMax:0.00}");
            Check("court floor: stone grain detail + reflection probe rendered (asset B)", MetronomeCourtArena.FloorDetailReady && arenaNow && arenaNow.ProbeRendered, "");
            Check("ambience: sky gears turn, broken masonry drifts in the void (asset E)", arenaNow && arenaNow.GearCount >= 3 && arenaNow.DrifterCount >= 10 && arenaNow.GearAngle - gearA0 > 0.5f,
                arenaNow ? $"gears {arenaNow.GearCount}, drifters {arenaNow.DrifterCount}, turned {arenaNow.GearAngle - gearA0:0.0} deg" : "");
            Check("dusk sky shown and fog starts beyond the court", arenaNow && arenaNow.SkyActive && Camera.main && Camera.main.clearFlags == CameraClearFlags.Skybox && RenderSettings.fogStartDistance >= MetronomeCourtArena.FogStart && arenaNow.LanternPoolCount >= 4,
                $"fog {RenderSettings.fogStartDistance:0}-{RenderSettings.fogEndDistance:0}, pools {(arenaNow ? arenaNow.LanternPoolCount : 0)}");
            yield return BossCloseups("haste");

            // ---------------- Threefold Severance (Haste) ----------------
            yield return Severance("haste", 220f);

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
            Check("music rate derived from tempo (1.0 outside Haste, 1.33 in Haste)", Mathf.Approximately(boss.Clock.MusicRate, 1f) && Mathf.Approximately(boss.Clock.HasteBpm / boss.Clock.NormalBpm, 4f / 3f));
            yield return Wait(0.6f);
            var arenaN = FindAnyObjectByType<MetronomeCourtArena>();
            Check("world lighting follows tempo into its Normal (cool) state", arenaN && arenaN.TempoBlend < 0.1f, arenaN ? $"blend {arenaN.TempoBlend:0.00}" : "");
            Check("metronome weight sits high in Normal (asset A: the weight is the tempo)", arenaN && arenaN.WeightHeight > MetronomeCourtArena.WeightNormal - 0.4f, arenaN ? $"weight {arenaN.WeightHeight:0.00}" : "");
            Check("tempo lights turn cool blue in Normal", arenaN && arenaN.TempoRimLight && Near(arenaN.TempoRimLight.color, MetronomeCourtArena.NormalLightTint, 0.2f) && Near(arenaN.TempoSpotLight.color, MetronomeCourtArena.NormalLightTint, 0.2f), "");
            Check("music: Normal swaps to the song as written (1.0x) at the same beat, still locked", music && music.Source.clip && music.Source.clip.name == TempoTyrantAudio.SlowTrack && music.Source.isPlaying &&
                boss.Audio.MusicSwaps >= 1 && Mathf.Abs(boss.Audio.LastMusicError) < TempoTyrantAudio.ResyncTolerance, boss.Audio.StatusText);
            yield return Wait(0.4f);
            yield return Shot("gameplay_normal_tempo");
            yield return BossCloseups("normal");
            boss.ForcedNext = "Severance";
            yield return Severance("normal", 165f);

            // ---------------- Sundering Wake ----------------
            yield return Wake();

            // ---------------- Hollow Crown ----------------
            yield return Crown();

            // ---------------- Spiral Slices (replaced Stop-Time) ----------------
            yield return SpiralSlices("first", null);

            // ---------------- Mirrored Wake (Haste only) ----------------
            yield return MirroredWake();
            yield return SpiralSlices("after mirrored wake", Tempo.Haste);

            // ---------------- pursuit, closing rush, target invalidation, party counts ----------------
            yield return Pursuit();

            // ---------------- frenzy ----------------
            yield return Frenzy();

            // ---------------- below 10%: Falling Hours ----------------
            yield return FallingHours();

            // ---------------- pause / resume ----------------
            yield return PauseResume();

            // ---------------- defeat, retry cleanup, menu return ----------------
            yield return DefeatAndRetry();

            // ---------------- whole-route feasibility (authoritative geometry) ----------------
            Routes();

            Note($"performance: {perfFrames} combat frames, average {(perfTime > 0 ? perfFrames / perfTime : 0):0.0} fps at {Screen.width}x{Screen.height} ({FrameMode})");
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
            Check($"Severance {tag}: 3 overhead takes his whole front half, behind him safe", s3.Contains(bp + fwd * 6) && s3.Contains(bp - right * 9f + fwd * 1) && s3.Contains(bp + right * 9f + fwd * 1) &&
                !s3.Contains(bp - fwd * 1.5f) && !s3.Contains(bp - fwd * 4 - right * 6) && !s3.Contains(bp - fwd * 4 + right * 6));
            Check($"Severance {tag}: telegraphs are clipped to the floor", boss.SeveranceStrikes[0].ClipRadius > 0 && boss.SeveranceStrikes[0].ClipRadius <= CombatWorld.PlayerBound + 0.5f);
            int beforeStrikes = boss.SeveranceStrikes.Count;
            yield return Until(() => boss.SeveranceStrikes.Count >= 2, 10, "strike 2 live"); yield return Wait(0.05f);
            Check($"Severance {tag}: strike 2 is the large (imminent) number once strike 1 is spent", boss.Numbers.Count(x => x.imminent) == 1 && boss.Numbers.First(x => x.imminent).number == 2,
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
                var tp = bpm >= 190 ? Tempo.Haste : Tempo.Normal; float gapB = TempoTyrantEncounter.Of(TempoTyrantEncounter.SevGap, tp), openB = TempoTyrantEncounter.Of(TempoTyrantEncounter.SevOpen, tp);
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
            int startHits = hits.Count, eruptions0 = boss.FissureEruptions, mirrors0 = boss.MirrorPreviews + boss.MirrorEruptions;
            yield return Until(() => boss.Current == "Sundering Wake", 20, "Wake");
            int wakeStart = d.World.Tick; var lane = boss.LastWake; var wakeTempo = boss.Clock.State;
            // Backswing: one roster player just behind his landing point (off the charge lane), one just in front of it
            var cl = boss.ChargeLog[boss.ChargeLog.Count - 1];
            var fwdW = Sim.Dir(cl.yaw); var rightW = Vector3.Cross(Vector3.up, fwdW);
            var behindP = AddExtra(cl.end - fwdW * 3f + rightW * 2.5f); var frontP = AddExtra(cl.end + fwdW * 2f + rightW * 2.5f);
            int bsPreviews = boss.BackswingPreviews;
            yield return Wait(0.2f);
            Check("Backswing: dashed preview at the landing point from the start of the charge warning (not live yet)", bsPreviews >= 1 && boss.BackswingPreviews == bsPreviews &&
                (boss.LastBackswing == null || !d.World.Hazards.Live.Contains(boss.LastBackswing)), $"previews {boss.BackswingPreviews}");
            yield return Shot("wake_warning_path_endpoint");
            int warnTicks = boss.Clock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.WakeWarn, boss.Clock.State));
            yield return Until(() => boss.Rig.Current == TyrantPose.Thrust, 6, "Wake travel");
            yield return Shot("wake_travel");
            yield return Until(() => boss.LastBackswing != null && d.World.Hazards.Live.Contains(boss.LastBackswing), 3, "backswing live");
            var bsH = boss.LastBackswing;
            yield return Shot("wake_backswing");
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
            if (bsH != null)
            {
                var bsHits = mine.Where(h => h.hazard == bsH.Id).ToList();
                Check("Backswing: hits the player behind his landing point once, misses the one in front", bsHits.Count(h => h.who == behindP) == 1 && bsHits.Count(h => h.who == frontP) == 0,
                    $"behind {bsHits.Count(h => h.who == behindP)}, front {bsHits.Count(h => h.who == frontP)}");
                Check("Backswing: a half-disc behind the landing point facing back up the lane", bsH.Shape == HazardShape.Cone && Mathf.Approximately(bsH.HalfAngle, 90) &&
                    Mathf.Abs(Mathf.DeltaAngle(bsH.Yaw, cl.yaw + 180)) < 0.5f && Sim.Flat(bsH.Origin - cl.end).magnitude < 0.05f && bsH.Kind == HazardKind.Strike);
                int bsWant = boss.Clock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.Backswing, wakeTempo));
                if (seen.TryGetValue(bsH.Id, out var sb))
                    Check($"Backswing: live on arrival, lands {TempoTyrantEncounter.Of(TempoTyrantEncounter.Backswing, wakeTempo)} beat(s) later ({wakeTempo})",
                        Mathf.Abs(sb.Spawn - (cl.tick + warnTicks + TempoTyrantEncounter.WakeTravelTicks)) <= 1 && sb.Resolve - sb.Spawn + 1 == bsWant,
                        $"spawn {sb.Spawn - cl.tick} ticks after the warning, live {sb.Resolve - sb.Spawn + 1} ticks (want {bsWant})");
            }
            else Check("Backswing: went live", false);
            Check("Wake: endpoint inside the boss bound", new Vector2(boss.transform.position.x, boss.transform.position.z).magnitude <= TempoTyrantEncounter.OwnBound + 0.05f);
            if (wakeTempo == Tempo.Normal)
                Check("Wake (Normal): no mirrored fissure outside Haste", boss.LastMirror == null && boss.MirrorPreviews + boss.MirrorEruptions == mirrors0);
            else Note("Wake ran in Haste here; the Normal no-mirror rule is covered only if a later Normal Wake runs");
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

        // Spiral Slices with a chaser and an idle player. The chaser (a roster bot, nearest the centre) follows the safe
        // slice and takes no damage; the bot player stands still in one slice and is hit by every spiral pulse whose safe
        // slice isn't its own, plus exactly one of the two every-other sets. want: wait for a boundary in this tempo first.
        IEnumerator SpiralSlices(string tag, Tempo? want)
        {
            keepAlive = true; RemoveExtras(); goal = null;
            yield return Until(() => boss.Current == "between blocks" && (want == null || (boss.Clock.State == want && !boss.Clock.Pending)), want == null ? 30 : 60, "boundary before Spiral Slices " + tag);
            // the idle bot player stands mid-slice 3 (105 deg) at 9 m; the chaser starts mid-slice 8 at 4 m (nearest)
            var idleAt = Sim.Dir(TempoTyrantEncounter.SliceYaw(3)) * 9f;
            d.Player.transform.position = d.World.Constrain(idleAt, PlayerController.Radius);
            var chaser = AddExtra(Sim.Dir(TempoTyrantEncounter.SliceYaw(8)) * 4f); chaser.Input = new ChaseBot { Boss = () => boss };
            boss.ForcedNext = "Slices";
            int startHits = hits.Count, logStart = boss.SliceLog.Count, blocks = boss.SliceBlocks;
            yield return Until(() => boss.SlicesActive, 25, "Spiral Slices " + tag);
            if (!boss.SlicesActive) { RemoveExtras(); yield break; }
            var tempo = boss.Clock.State; int dir = boss.SpiralDir;
            {
                // route: one slice to the next at the chase radius (5 m) inside one step, walking; and across the floor
                // (12.5 m rim to the far side's 5 m) inside indicate + first warning, sprinting
                float chord = 2 * ChaseBot.ChaseRadius * Mathf.Sin(TempoTyrantEncounter.SliceHalf * Mathf.Deg2Rad);   // slice centre to slice centre
                float stepS = TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.SliceStep, tempo), boss.Clock.Bpm) / (float)Sim.Hz;
                float openS = TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.SliceIndicate, tempo) + TempoTyrantEncounter.Of(TempoTyrantEncounter.SliceFirst, tempo), boss.Clock.Bpm) / (float)Sim.Hz;
                Check($"Spiral Slices {tag}: routes: chase walkable, opening reachable sprinting", chord / stepS <= PlayerController.WalkSpeed * 0.9f && (12.5f + 5f) / openS <= PlayerController.SprintSpeed * 0.95f,
                    $"chase {chord / stepS:0.0} m/s (walk {PlayerController.WalkSpeed}), opening {(12.5f + 5f) / openS:0.0} m/s (sprint {PlayerController.SprintSpeed})");
            }
            Check($"Spiral Slices {tag}: boss conducts from the arena centre", TempoTyrantEncounter.ArenaCentred(boss.transform.position));
            Check($"Spiral Slices {tag}: whole floor indicated before any slice is live", !d.World.Hazards.Live.Any(h => h.Source.StartsWith("Spiral")));
            yield return Wait(0.15f);
            yield return Shot($"slices_{tag.Replace(' ', '_')}_indicate");
            yield return Until(() => boss.SafeSlice >= 0, 6, "first slice goes");
            yield return Wait(0.1f);
            var live = d.World.Hazards.Live.Where(h => h.Source == "Spiral Slices").ToList();
            Check($"Spiral Slices {tag}: 11 slices live, the first safe slice is the nearest player's", live.Count == 11 && boss.SafeSlice == 8 &&
                live.All(h => Mathf.Abs(Mathf.DeltaAngle(h.Yaw, TempoTyrantEncounter.SliceYaw(8))) > 1f), $"{live.Count} live, safe {boss.SafeSlice}");
            yield return Shot($"slices_{tag.Replace(' ', '_')}_spiral_start");
            yield return Until(() => boss.SliceLog.Count - logStart >= 5, 10, "mid spiral");
            yield return Shot($"slices_{tag.Replace(' ', '_')}_spiral_mid");
            yield return Until(() => boss.AltSafeParity >= 0, 15, "every-other phase");
            yield return Wait(0.2f);
            yield return Shot($"slices_{tag.Replace(' ', '_')}_every_other");
            yield return Until(() => !boss.SlicesActive, 10, "Spiral Slices end");
            yield return Wait(0.2f);
            var log = boss.SliceLog.Skip(logStart).ToList();
            var spiral = log.Where(l => l.parity < 0).ToList(); var alt = log.Where(l => l.parity >= 0).ToList();
            bool steps = spiral.Count == TempoTyrantEncounter.SpiralSteps &&
                spiral.Zip(spiral.Skip(1), (a, b) => TempoTyrantEncounter.Wrap(a.safe + dir) == b.safe).All(x => x);
            Check($"Spiral Slices {tag} ({tempo}, {(dir > 0 ? "right" : "left")}): the safe slice moves one slice per step", steps, string.Join(" ", spiral.Select(l => l.safe)));
            int stepTicks = TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.SliceStep, tempo), tempo == Tempo.Haste ? boss.Clock.HasteBpm : boss.Clock.NormalBpm);
            Check($"Spiral Slices {tag}: steps are {TempoTyrantEncounter.Of(TempoTyrantEncounter.SliceStep, tempo)} beats apart",
                spiral.Zip(spiral.Skip(1), (a, b) => b.tick - a.tick).All(t => Mathf.Abs(t - stepTicks) <= 1), string.Join(",", spiral.Zip(spiral.Skip(1), (a, b) => b.tick - a.tick)));
            int lastSafe = spiral.Count > 0 ? spiral[spiral.Count - 1].safe : -1;
            Check($"Spiral Slices {tag}: every other slice, the set holding the spiral's last slice first, then the other",
                alt.Count == 2 && alt[0].parity == lastSafe % 2 && alt[1].parity == 1 - alt[0].parity, string.Join(" ", alt.Select(a => a.parity)));
            var all = hits.Skip(startHits).ToList();
            var chased = all.Where(h => h.who == chaser).ToList();
            Check($"Spiral Slices {tag}: a player chasing the safe slice takes no damage", chased.Count == 0, string.Join(", ", chased.Select(h => h.source)));
            int idleSlice = TempoTyrantEncounter.SliceOf(d.Player.transform.position);
            int wantSpiral = spiral.Count(l => l.safe != idleSlice);
            var idle = all.Where(h => h.who == d.Player).ToList();
            int gotSpiral = idle.Count(h => h.source == "Spiral Slices"), gotAlt = idle.Count(h => h.source.StartsWith("Every Other"));
            Check($"Spiral Slices {tag}: standing still is hit by every pulse that isn't safe for it, once each", gotSpiral == wantSpiral && gotAlt == 1,
                $"spiral {gotSpiral}/{wantSpiral}, every-other {gotAlt}/1");
            Check($"Spiral Slices {tag}: a spiral hit never stuns", idle.Where(h => h.source == "Spiral Slices").Count() == 0 || d.Player.Stunned == 0);
            Check($"Spiral Slices {tag}: no lingering slice hazards", !d.World.Hazards.Live.Any(h => h.Source.StartsWith("Spiral") || h.Source.StartsWith("Every Other")));
            RemoveExtras();
        }

        // Haste: the fissure gets a mirror image (x -> -x). A roster player standing only in the mirror lane is hit once by
        // the mirrored eruption and never by the real one; both erupt on the same tick.
        IEnumerator MirroredWake()
        {
            keepAlive = true; RemoveExtras();
            yield return Until(() => boss.Current == "between blocks" && boss.Clock.State == Tempo.Haste && !boss.Clock.Pending, 60, "Haste boundary before Mirrored Wake");
            var spot = d.World.Constrain(new Vector3(7.5f, 0, 7.5f), PlayerController.Radius);
            d.Player.transform.position = spot; goal = spot;
            boss.ForcedNext = "Wake";
            int previews0 = boss.MirrorPreviews, mErupt0 = boss.MirrorEruptions, fErupt0 = boss.FissureEruptions, startHits = hits.Count;
            yield return Until(() => boss.Current == "Sundering Wake", 20, "Mirrored Wake");
            var tempo = boss.Clock.State;
            yield return Wait(0.15f);
            Check("Mirrored Wake: dashed mirror preview shown during the charge warning", tempo == Tempo.Haste && boss.MirrorPreviews == previews0 + 1,
                $"tempo {tempo}, previews +{boss.MirrorPreviews - previews0}, skips {boss.MirrorSkips}");
            yield return Shot("mirrored_wake_preview");
            yield return Until(() => boss.LastMirror != null && d.World.Hazards.Live.Contains(boss.LastMirror), 4, "mirrored eruption warning");
            var f = boss.LastFissure; var m = boss.LastMirror;
            if (f == null || m == null) { Check("Mirrored Wake: mirror lane spawned", false); RemoveExtras(); yield break; }
            Check("Mirrored Wake: mirror is the fissure reflected across the north-south line", Mathf.Abs(m.Origin.x + f.Origin.x) < 1e-3f && Mathf.Abs(m.Origin.z - f.Origin.z) < 1e-3f &&
                Mathf.Abs(Mathf.DeltaAngle(m.Yaw, -f.Yaw)) < 0.01f && Mathf.Approximately(m.Length, f.Length) && Mathf.Approximately(m.Width, f.Width) && m.ResolveAt == f.ResolveAt,
                $"fissure {f.Origin} yaw {f.Yaw:0.0}, mirror {m.Origin} yaw {m.Yaw:0.0}");
            var mid = m.Origin + Sim.Dir(m.Yaw) * (m.Length * 0.6f);
            var buddy = AddExtra(mid);
            // Haste: after the backswing he re-targets the farthest player and charges again at once
            int charges0 = boss.ChargeLog.Count; var c1 = boss.ChargeLog[charges0 - 1];
            var farAway = AddExtra(-Sim.Flat(c1.end).normalized * 11.8f);
            Note($"mirror-lane player at {buddy.transform.position}: in mirror {m.Contains(buddy.transform.position)}, in fissure {f.Contains(buddy.transform.position)}");
            yield return Wait(0.1f);
            yield return Shot("mirrored_wake_live");
            yield return Until(() => boss.MirrorEruptions > mErupt0 && boss.FissureEruptions > fErupt0, 6, "both eruptions");
            yield return Wait(0.4f);
            var bh = hits.Skip(startHits).Where(h => h.who == buddy).ToList();
            Check("Mirrored Wake: a player in the mirror lane is hit once by it, never by the real fissure",
                bh.Count(h => h.source == "Sundering Wake (mirrored eruption)") == 1 && bh.Count(h => h.source == "Sundering Wake (eruption)") == 0, string.Join(", ", bh.Select(h => h.source)));
            if (seen.TryGetValue(f.Id, out var sf) && seen.TryGetValue(m.Id, out var sm))
                Check("Mirrored Wake: both lanes go live and erupt on the same ticks", sf.Spawn == sm.Spawn && sf.Resolve == sm.Resolve, $"fissure {sf.Spawn}->{sf.Resolve}, mirror {sm.Spawn}->{sm.Resolve}");
            Check("Mirrored Wake: each lane erupts exactly once", boss.MirrorEruptions == mErupt0 + 1 && boss.FissureEruptions == fErupt0 + 1);
            yield return Until(() => boss.ChargeLog.Count > charges0, 4, "second charge");
            if (boss.ChargeLog.Count > charges0)
            {
                var c2 = boss.ChargeLog[charges0];
                int bsT = TempoClock.TicksFor(TempoTyrantEncounter.Backswing.H, boss.Clock.HasteBpm), w1 = TempoClock.TicksFor(TempoTyrantEncounter.WakeWarn.H, boss.Clock.HasteBpm);
                float wantYaw = Sim.YawTo(c2.start, farAway.transform.position, 0);
                Check("Haste: the second charge re-targets the farthest player, straight after the backswing", c2.index == 1 && c2.target == farAway && Sim.Flat(c2.start - c1.end).magnitude < 0.05f &&
                    Mathf.Abs(Mathf.DeltaAngle(c2.yaw, wantYaw)) < 2f && Mathf.Abs(c2.tick - (c1.tick + w1 + TempoTyrantEncounter.WakeTravelTicks + bsT)) <= 1,
                    $"target {(c2.target ? c2.target.name : "none")}, yaw {c2.yaw:0} vs {wantYaw:0}, starts {c2.tick - c1.tick} ticks after charge 1 (want {w1 + TempoTyrantEncounter.WakeTravelTicks + bsT})");
                yield return Shot("haste_second_charge");
                int f2 = boss.FissureEruptions;
                yield return Until(() => boss.FissureEruptions > f2, 5, "second fissure");
                Check("Haste: each charge leaves its own fissure", boss.FissureEruptions == fErupt0 + 2 && boss.LastFissure != f && boss.LastFissure.Source == "Sundering Wake (eruption 2)", boss.LastFissure != null ? boss.LastFissure.Source : "");
            }
            else Check("Haste: the second charge happened", false);
            RemoveExtras();
            yield return Until(() => boss.Current == "between blocks" || boss.Current.StartsWith("pursuit") || boss.Current.StartsWith("Closing"), 10, "Mirrored Wake block ends");
            Check("Mirrored Wake: no lingering hazards after the block", !d.World.Hazards.Live.Any(h => h.Source.StartsWith("Sundering Wake")));
            goal = null;
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
            yield return Until(() => boss.Rushes > rushes && boss.Current != "Closing Rush", 6, "rush travel");   // "Closing Rush (stopped)" once he lands
            Check("closing rush announced and non-damaging", boss.Rushes > rushes && hits.Skip(rushHits).All(h => !h.source.Contains("Rush")));
            var rr = boss.RushLog.LastOrDefault(x => x.StopTick >= 0);
            if (rr != null)
            {
                int tellWant = TempoClock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.RushTell, boss.Clock.State), boss.Clock.Bpm);
                float speed = rr.Distance / (rr.TravelTicks / (float)Sim.Hz);
                Check("rush timing: 0.5 s far, 1-beat tell, 24 m/s, 7.5 m", rr.FarTicks >= TempoTyrantEncounter.RushSustainTicks && TempoTyrantEncounter.RushSustainTicks == 30 &&
                    rr.TellTicks == tellWant && speed <= TempoTyrantEncounter.RushSpeed + 0.01f && speed >= TempoTyrantEncounter.RushSpeed - 2.5f && Mathf.Approximately(TempoTyrantEncounter.RushDist, 7.5f) && Mathf.Approximately(TempoTyrantEncounter.RushSpeed, 24f),
                    $"far {rr.FarTicks} ticks, tell {rr.TellTicks} (want {tellWant}), {rr.Distance:0.0} m in {rr.TravelTicks} ticks = {speed:0.0} m/s, from {rr.Where}");
            }
            else Check("rush timing recorded", false);
            var done = boss.RushLog.Where(x => x.StopTick >= 0).ToList();
            var gaps = done.Zip(done.Skip(1), (a, b) => b.TellTick - a.StopTick).ToList();
            Check("rush cooldown 3 s between rushes", TempoTyrantEncounter.RushCooldownTicks == 3 * Sim.Hz && gaps.All(g => g >= TempoTyrantEncounter.RushCooldownTicks - 1),
                $"{done.Count} rushes, gaps {string.Join(",", gaps)} ticks; fired from: {string.Join(",", boss.RushLog.Select(x => x.Where).Distinct())}");
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
            int chains = boss.ChainsRun, cad0 = boss.CadenceLog.Count;
            keepAbove = 99;   // overlapping hits: refill after every hit so the bot never goes down
            yield return Until(() => boss.ChainsRun > chains, 50, "first Broken Cadence block");
            yield return Until(() => boss.CadenceLog.Count > cad0, 20, "first overlap");
            yield return Wait(0.3f);
            yield return Shot("broken_cadence_overlap");
            // observe until chains have run in both tempos and every pairing has shown up (bounded)
            float waited = 0;
            while (waited < (Quick ? 45f : 120f))
            {
                int pairsSeen = boss.CadenceLog.Skip(cad0).Select(x => (x.from, x.to)).Distinct().Count();
                int temposSeen = boss.BlockLog.Where(l => l.Contains("Broken Cadence:")).Select(l => l.Split(' ')[1]).Distinct().Count();
                if (pairsSeen >= TempoTyrantEncounter.Cadence.Pairs.Length && temposSeen >= 2) break;
                yield return Wait(1f); waited += 1f;
            }
            keepAbove = 60;
            var ov = boss.CadenceLog.Skip(cad0).ToList();
            Check("Broken Cadence: every block is an overlapping chain of allowed pairings", ov.Count > 0 && ov.All(x => TempoTyrantEncounter.Cadence.Allowed(x.from, x.to) && x.pending > 0),
                string.Join(" | ", ov.Select(x => $"{x.from}>{x.to} ({x.pending} pending)")));
            int seenPairs = ov.Select(x => (x.from, x.to)).Distinct().Count();
            if (!Quick) Check("Broken Cadence: every pairing seen", seenPairs == TempoTyrantEncounter.Cadence.Pairs.Length, $"{seenPairs}/{TempoTyrantEncounter.Cadence.Pairs.Length} in {waited:0} s");
            else Note($"Broken Cadence pairings seen: {seenPairs}/{TempoTyrantEncounter.Cadence.Pairs.Length} in {waited:0} s (quick run)");
            var chainLog = boss.BlockLog.Where(l => l.Contains("Broken Cadence:")).ToList();
            Check("Broken Cadence: runs in both tempos", chainLog.Any(l => l.Contains(" Haste ")) && chainLog.Any(l => l.Contains(" Normal ")), string.Join(" | ", chainLog));
            Check("Broken Cadence: never two slice attacks at once", boss.MaxSliceAttacksAtOnce <= 1 && ov.Any(x => x.to == TempoTyrantEncounter.Atk.Slices || x.from == TempoTyrantEncounter.Atk.Slices),
                $"max {boss.MaxSliceAttacksAtOnce} slice attacks live together");
            Check("Broken Cadence: he never moves while a Severance strike is pending", boss.SevLockViolations == 0, $"{boss.SevLockViolations} ticks");
            int leadWant = Mathf.Min(TempoClock.TicksFor(TempoTyrantEncounter.Cadence.FissureLead.H, boss.Clock.HasteBpm), TempoClock.TicksFor(TempoTyrantEncounter.Cadence.FissureLead.N, boss.Clock.NormalBpm));
            Check("Broken Cadence: a pending fissure erupts >= its lead before strike 1", boss.FissureLeads.All(l => l >= leadWant - 1), $"leads {string.Join(",", boss.FissureLeads)} ticks (min {leadWant})");
            Check("Broken Cadence windows: never longer than ordinary, most shorter", CadenceShorter(), CadenceTable());
            Note(CadenceTable());
            Check("frenzy triggered exactly once", boss.FrenzyTriggers == 1);
            Note("block log: " + string.Join(" | ", boss.BlockLog));
            Note("tempo history: " + string.Join(" | ", boss.Clock.History.Select(h => $"tick {h.tick} -> {h.to} after {h.stateTicks / 60f:0.00}s")));
        }

        // Below 10%: orbs fall onto the floor, more often the longer it lasts; each spews 3 orbs that bounce off the
        // wall 3 times. The bot stands still (kept standing) so orbs land on it and around it.
        IEnumerator FallingHours()
        {
            keepAlive = true; keepAbove = 99;
            yield return Until(() => !boss.Clock.Transitioning, 10, "no tempo shift");
            boss.DebugSetHpFraction(0.10f);
            int blocks = boss.Blocks;
            yield return Until(() => boss.Blocks >= blocks + 1, 40, "block at exactly 10%");
            Check("Falling Hours: exactly 10% does not start it (strictly below)", !boss.OrbsOn);
            boss.DebugSetHpFraction(0.09f);
            yield return Until(() => boss.OrbsOn, 60, "Falling Hours");
            if (!boss.OrbsOn) yield break;
            int hits0 = hits.Count; float span = Quick ? 12f : 20f;
            yield return Wait(4f);
            yield return Shot("falling_hours_early");
            yield return Wait(span - 4f);
            yield return Shot("falling_hours_dense");
            int start = boss.OrbsStartTick, half = (int)(span / 2 * Sim.Hz);
            int first = boss.DropTicks.Count(t => t - start < half), second = boss.DropTicks.Count(t => t - start >= half && t - start < 2 * half);
            Check("Falling Hours: orbs fall more often the longer it lasts", first > 0 && second > first && boss.OrbRate > TempoTyrantEncounter.OrbRateStart,
                $"{first} drops in the first {span / 2:0} s, {second} in the next; rate now {boss.OrbRate:0.00}/s");
            Check("Falling Hours: every landed orb spews 3 orbs", boss.DropsLanded > 0 && boss.BouncersSpawned == TempoTyrantEncounter.OrbSplit * boss.DropsLanded,
                $"{boss.DropsLanded} landed, {boss.BouncersSpawned} bouncing orbs");
            Check("Falling Hours: each bouncing orb bounces off the wall 3 times, then dissipates", boss.BouncerBounceLog.Count > 0 && boss.BouncerBounceLog.All(b => b == TempoTyrantEncounter.OrbBounces),
                $"{boss.BouncerBounceLog.Count} dissipated, bounces {string.Join(",", boss.BouncerBounceLog.Distinct())}");
            Check("Falling Hours: orbs can't be jumped", seen.Values.Where(x => x.Source.StartsWith("Falling Hours")).All(x => !x.H.JumpClearable) && seen.Values.Any(x => x.Source.Contains("bouncing")));
            Check("Falling Hours: bouncing orbs stay on the floor", boss.MaxBouncerRadius <= CombatWorld.PlayerBound + 0.01f, $"max {boss.MaxBouncerRadius:0.00} m");
            var orbHits = hits.Skip(hits0).Where(h => h.source.StartsWith("Falling Hours")).ToList();
            Check("Falling Hours: orbs hit, each at most once per player", orbHits.Count > 0 && orbHits.GroupBy(h => (h.hazard, h.who)).All(g => g.Count() == 1),
                $"{orbHits.Count} hits ({orbHits.Count(h => h.source.Contains("bouncing"))} from bouncing orbs)");
            float warnS = Mathf.Min(TempoClock.TicksFor(TempoTyrantEncounter.OrbFallWarn.H, boss.Clock.HasteBpm), TempoClock.TicksFor(TempoTyrantEncounter.OrbFallWarn.N, boss.Clock.NormalBpm)) / (float)Sim.Hz;
            Check("Falling Hours routes: a walker leaves a landing circle in time; bouncing orbs no faster than walking",
                (TempoTyrantEncounter.OrbImpactRadius + Hazard.Margin) / PlayerController.WalkSpeed + TempoTyrantRoutes.Reaction <= warnS && TempoTyrantEncounter.BouncerSpeed <= PlayerController.WalkSpeed,
                $"needs {(TempoTyrantEncounter.OrbImpactRadius + Hazard.Margin) / PlayerController.WalkSpeed + TempoTyrantRoutes.Reaction:0.00} s of {warnS:0.00} s");
            Note($"Falling Hours: {boss.Drops} drops, {boss.LiveOrbCount} orbs live at the end of {span:0} s");
        }

        IEnumerator PauseResume()
        {
            yield return Until(() => d.World.Hazards.Live.Any(h => h.Age < h.ResolveAt - 30), 30, "a pending hazard");
            double beat = boss.Clock.Beat; int tick = d.World.Tick; var ages = d.World.Hazards.Live.Select(h => h.Age).ToArray(); int hp = d.Player.Hp;
            d.SetPaused(true);
            yield return new WaitForSeconds(1.5f);
            Check("pause freezes tempo, tick and hazards", boss.Clock.Beat == beat && d.World.Tick == tick && d.World.Hazards.Live.Select(h => h.Age).SequenceEqual(ages));
            Check("no metronome tick or tempo-change sound sources on the boss", boss.GetComponentsInChildren<AudioSource>().Length == 0);
            var mus = FindAnyObjectByType<LastBell.Menu.MenuMusic>();
            Check("pause pauses this fight's music", mus && mus.Source && !mus.Source.isPlaying);
            yield return Shot("paused");
            d.SetPaused(false);
            yield return null; yield return null;
            Check("resume does not burst ticks or damage", d.World.Tick - tick <= 13 &&   // two resume frames at the director's 0.1 s/frame cap (6 ticks) + 1
                 d.Player.Hp >= hp - 40, $"ticks +{d.World.Tick - tick}");
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
            Check("defeat clears the Falling Hours orbs", boss.LiveOrbCount == 0 && !boss.OrbsOn, $"{boss.LiveOrbCount} orbs");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("defeat");
            Check("no damage after defeat", hits.Count == hitsBefore);
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
            foreach (float bpm in new[] { 220f, 165f })
            {
                bool haste = bpm >= 190; float speed = haste ? PlayerController.SprintSpeed : PlayerController.WalkSpeed; string how = haste ? "sprinting" : "walking";
                int starts = 0, ok = 0; float worst = float.MaxValue; string worstAt = "";
                foreach (var b in bosses)
                    for (int y = 0; y < 360; y += 45)
                    {
                        var r = TempoTyrantRoutes.Severance(b, y, bpm, speed, bound);
                        starts += r.Starts; ok += r.Feasible; if (r.WorstSlack < worst) { worst = r.WorstSlack; worstAt = r.ToString(); }
                        foreach (var f in r.Failures.Take(2)) Note("route failure: " + f);
                    }
                Check($"routes: Threefold Severance at {bpm} BPM, {how}, from every sampled legal start", ok == starts, $"{ok}/{starts}, worst slack {worst:0.00} s");
                Note("worst: " + worstAt);
                starts = 0; ok = 0; worst = float.MaxValue;
                foreach (var b in bosses)
                {
                    var r = TempoTyrantRoutes.Crown(b, bpm, speed, bound);
                    starts += r.Starts; ok += r.Feasible; worst = Mathf.Min(worst, r.WorstSlack);
                    foreach (var f in r.Failures.Take(2)) Note("route failure: " + f);
                }
                Check($"routes: Hollow Crown in then out at {bpm} BPM, {how}", ok == starts, $"{ok}/{starts}, worst slack {worst:0.00} s");
            }
            // Oct 28 ultimate rework (TempoTyrantRoutes timelines; the full sweep is tools/tempo-tyrant/routes_check.py ultimate).
            // Haste: sprint with stamina simulated along the route; Normal: walking.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var tp in new[] { Tempo.Haste, Tempo.Normal })
            {
                string how = tp == Tempo.Haste ? "sprint + finite stamina" : "walking";
                var r = TempoTyrantRoutes.WakeRework(tp, bound);
                Check($"routes: Sundering Wake rework ({tp}: charge, backswing, fissure{(tp == Tempo.Haste ? " + mirror, charge again" : "")}), {how}", r.Feasible == r.Starts, $"{r.Feasible}/{r.Starts}, worst slack {r.WorstSlack:0.00} s");
                foreach (var f in r.Failures.Take(3)) Note("route failure: " + f);
                foreach (TempoTyrantEncounter.Atk a in System.Enum.GetValues(typeof(TempoTyrantEncounter.Atk)))
                {
                    r = TempoTyrantRoutes.CadenceAlone(a, tp, bound);
                    Check($"routes: Broken Cadence {a} alone ({tp}), {how}", r.Feasible == r.Starts, $"{r.Feasible}/{r.Starts}, worst slack {r.WorstSlack:0.00} s");
                    foreach (var f in r.Failures.Take(3)) Note("route failure: " + f);
                }
                foreach (var pr in TempoTyrantEncounter.Cadence.Pairs)
                {
                    r = TempoTyrantRoutes.CadencePair(pr.from, pr.to, tp, bound);
                    Check($"routes: Broken Cadence pairing {pr.from} -> {pr.to} ({tp}, join {TempoTyrantEncounter.Of(pr.join, tp)} beats), {how}", r.Feasible == r.Starts, $"{r.Feasible}/{r.Starts}, worst slack {r.WorstSlack:0.00} s");
                    foreach (var f in r.Failures.Take(3)) Note("route failure: " + f);
                }
            }
            TempoTyrantRoutes.ReleaseCaches();
            Note($"Oct 28 route checks took {sw.ElapsedMilliseconds / 1000f:0.0} s");
            // Mirrored Wake alone: charges in every 15-degree direction (short and long), off both lanes within the
            // 3-beat fissure warning, walking, counting none of the earlier preview time
            int ms = 0, mok = 0, mskip = 0; float mw = float.MaxValue;
            for (int yaw = 0; yaw < 360; yaw += 15)
            {
                if (!TempoTyrantEncounter.MirrorUseful(yaw)) { mskip++; continue; }
                foreach (float travel in new[] { TempoTyrantEncounter.WakeMinTravel, 10f })
                {
                    var start = Sim.Dir(yaw + 180) * 1.0f;
                    var end = TempoTyrantEncounter.ClampInside(start, start + Sim.Dir(yaw) * travel, TempoTyrantEncounter.OwnBound);
                    var r = TempoTyrantRoutes.MirroredWake(start, end, 220f, PlayerController.WalkSpeed, bound);
                    ms += r.Starts; mok += r.Feasible; mw = Mathf.Min(mw, r.WorstSlack);
                    foreach (var fl in r.Failures.Take(2)) Note("route failure: " + fl);
                }
            }
            Check("routes: Mirrored Wake, off both lanes within the fissure warning alone, walking", mok == ms, $"{mok}/{ms}, worst slack {mw:0.00} s, {mskip} near-axis directions skip the mirror");
        }

        static readonly (string name, (float H, float N) w)[] Windows =
        {
            ("Severance opening", TempoTyrantEncounter.SevOpen), ("Severance gap", TempoTyrantEncounter.SevGap), ("Wake warning", TempoTyrantEncounter.WakeWarn),
            ("second charge", TempoTyrantEncounter.WakeWarn2), ("backswing", TempoTyrantEncounter.Backswing), ("fissure warning", TempoTyrantEncounter.FissureWarn), ("Crown ring", TempoTyrantEncounter.CrownRing), ("Crown inner", TempoTyrantEncounter.CrownInnerWarn),
            ("rush tell", TempoTyrantEncounter.RushTell), ("slice indicate", TempoTyrantEncounter.SliceIndicate), ("first slice", TempoTyrantEncounter.SliceFirst),
            ("spiral step", TempoTyrantEncounter.SliceStep), ("every-other 1", TempoTyrantEncounter.AltWarn), ("every-other 2", TempoTyrantEncounter.AltGap),
        };
        static bool HasteShorter()
        {
            var c = new TempoClock();
            return Windows.All(x => x.w.H <= x.w.N && TempoClock.TicksFor(x.w.H, c.HasteBpm) < TempoClock.TicksFor(x.w.N, c.NormalBpm)) &&
                   Windows.Count(x => x.w.H < x.w.N) >= 4;
        }
        static readonly (string name, (float H, float N) ord, (float H, float N) cad)[] CadenceWindows =
        {
            ("Severance opening", TempoTyrantEncounter.SevOpen, TempoTyrantEncounter.Cadence.SevOpen), ("Severance gap", TempoTyrantEncounter.SevGap, TempoTyrantEncounter.Cadence.SevGap),
            ("Wake warning", TempoTyrantEncounter.WakeWarn, TempoTyrantEncounter.Cadence.WakeWarn), ("second charge", TempoTyrantEncounter.WakeWarn2, TempoTyrantEncounter.Cadence.WakeWarn2),
            ("backswing", TempoTyrantEncounter.Backswing, TempoTyrantEncounter.Cadence.Backswing), ("fissure warning", TempoTyrantEncounter.FissureWarn, TempoTyrantEncounter.Cadence.FissureWarn),
            ("Crown ring", TempoTyrantEncounter.CrownRing, TempoTyrantEncounter.Cadence.CrownRing), ("Crown inner", TempoTyrantEncounter.CrownInnerWarn, TempoTyrantEncounter.Cadence.CrownInner),
            ("spiral opening", (TempoTyrantEncounter.SliceIndicate.H + TempoTyrantEncounter.SliceFirst.H, TempoTyrantEncounter.SliceIndicate.N + TempoTyrantEncounter.SliceFirst.N),
                (TempoTyrantEncounter.Cadence.SliceIndicate.H + TempoTyrantEncounter.Cadence.SliceFirst.H, TempoTyrantEncounter.Cadence.SliceIndicate.N + TempoTyrantEncounter.Cadence.SliceFirst.N)),
            ("spiral step", TempoTyrantEncounter.SliceStep, TempoTyrantEncounter.Cadence.SliceStep), ("every-other 1", TempoTyrantEncounter.AltWarn, TempoTyrantEncounter.Cadence.AltWarn),
            ("every-other 2", TempoTyrantEncounter.AltGap, TempoTyrantEncounter.Cadence.AltGap),
        };
        static bool CadenceShorter() => CadenceWindows.All(x => x.cad.H <= x.ord.H && x.cad.N <= x.ord.N) && CadenceWindows.Count(x => x.cad.H < x.ord.H) >= 6;
        static string CadenceTable() => "Broken Cadence windows (beats, ordinary -> cadence, Haste/Normal): " +
            string.Join("; ", CadenceWindows.Select(x => $"{x.name} {x.ord.H}/{x.ord.N} -> {x.cad.H}/{x.cad.N}")) + "; joins: " +
            string.Join("; ", TempoTyrantEncounter.Cadence.Pairs.Select(pr => $"{pr.from}>{pr.to} {pr.join.H}/{pr.join.N}"));
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
        static bool Near(Color a, Color b, float tol = 0.03f) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < tol;
    }
}
