using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    public enum RunState { Menu, Ready, Intro, Combat, Cleared, Downed, Results }
    public enum RunMode { Run, Practice }

    // Run flow in the single arena:
    //   Menu -> Ready (preparation) -> [Intro -> Combat -> Cleared] for each boss in Bosses -> Results (victory)
    // The stand-in minibosses (Dial Warden, Pendulum Sentinel, Chimebound Oracle) and the intermission were removed on
    // 2026-10-22: the run and practice only offer real boss encounters, currently the Bellkeeper.
    //   any Combat -> Downed -> Results (defeat).  Results -> Retry (fresh run, encounter 1) or Menu.
    // Practice runs one chosen encounter with the same rules and never advances or unlocks the run.
    // Every transition goes through Go(), which bumps a generation number: timers and requests from an older state are
    // ignored, and requests are only honoured in the state they belong to, so repeated Ready/Continue/Retry presses can't
    // double-start an encounter or skip a state. Gameplay advances on a 60 Hz tick; the director checks run outcomes after
    // each tick (defeat wins a same-tick tie with boss death, spec §4).
    public sealed class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        // Every real boss encounter, in run order. The run fights the entries with InRun set; the practice menu offers them
        // all. Map: the run map an entry's practice loads first (null = fight on the current map). Add a boss with one line.
        public sealed class BossEntry
        {
            public readonly string Name; public readonly Func<CombatWorld, BossEncounter> Spawn; public readonly bool InRun; public readonly string Map;
            public BossEntry(string name, Func<CombatWorld, BossEncounter> spawn, bool inRun = true, string map = null) { Name = name; Spawn = spawn; InRun = inRun; Map = map; }
        }
        public static readonly BossEntry[] Bosses =
        {
            new BossEntry("Bellkeeper", w => BossEncounter.Spawn<BellkeeperEncounter>(w, Vector3.zero, 0)),
            // Verdant Chimes area boss (graybox): practice only, fought in her own arena
            new BossEntry("Sylvara (area 3)", w => BossEncounter.Spawn<SylvaraEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: SylvaraMap),
            // Brass Foundry area boss: practice only, fought in its own arena
            new BossEntry("Crucivar (area 2)", w => BossEncounter.Spawn<CrucivarEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: CrucivarMap),
        };
        public static readonly int[] RunOrder = Enumerable.Range(0, Bosses.Length).Where(i => Bosses[i].InRun).ToArray();
        public static int SylvaraPractice => Array.FindIndex(Bosses, b => b.Map == SylvaraMap);
        public const string SylvaraMap = "verdant-chimes";
        public static int CrucivarPractice => Array.FindIndex(Bosses, b => b.Map == CrucivarMap);
        public const string CrucivarMap = "brass-foundry";
        public static readonly string[] Classes = { "Vanguard", "Chimesinger", "Arcanist" };
        const int IntroTicks = 120, ClearTicks = 180, DownedTicks = 100;
        static readonly Vector3 PlayerSocket = new Vector3(0, 0, 6.5f);

        public RunState State { get; private set; } = RunState.Menu;
        public RunMode Mode { get; private set; }
        public string ClassName { get; private set; } = "Vanguard";
        public Difficulty Difficulty { get; private set; } = Difficulty.Medium;   // copied from the menu at run start
        public int Encounter { get; private set; }          // index into Bosses (the last is the final boss)
        public int Generation { get; private set; }
        public int StateTicks { get; private set; }
        public bool Victory { get; private set; }
        public string DefeatCause { get; private set; }
        public int RunTicks { get; private set; }
        public int EncountersCleared { get; private set; }
        public bool Paused { get; private set; }
        public CombatWorld World { get; private set; }
        public PlayerController Player { get; private set; }
        public BossEncounter Boss { get; private set; }
        public int RunsStarted { get; private set; }

        // instrumentation for tests / handoff evidence
        public int EncounterStarts, EncounterEnds, Transitions;
        public readonly List<string> Log = new List<string>();
        public event Action<RunState> StateChanged;
        public IInputSource InputOverride;                  // autotest bot

        Transform scopeParent; float acc;
        LastBell.VFX.BellkeeperFx bossFx; PlayerFx playerFx;
        static Transform sharedFxRoot; static LastBell.VFX.BellkeeperFx sharedBossFx; static PlayerFx sharedPlayerFx;
        Camera cam; Vector3 camVel;
        static readonly Vector3 FollowOffset = new Vector3(0, 13f, 9.7f), MenuCamPos = new Vector3(0, 29, 22.5f);

        static bool mapArgHandled;

        void Awake()
        {
            Instance = this;
            if (!mapArgHandled)
            {
                mapArgHandled = true;
                int want = RunMaps.IndexOf(RunMaps.RequestedId ?? "");
                if (want >= 0 && want != RunMaps.Current && RunMaps.InBuild(want)) { RunMaps.Load(want); return; }
            }
            cam = Camera.main;
            HazardSystem.FindArena();
            int map = RunMaps.Current;
            if (map >= 0) CombatWorld.SetPlayerBound(RunMaps.All[map].Bound, RunMaps.All[map].Square);
            if (!GetComponent<RunHud>()) gameObject.AddComponent<RunHud>();
            Application.targetFrameRate = 120;
            if (RunAutoTest.Requested) RunAutoTest.Launch();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Go(RunState s)
        {
            Generation++; Transitions++; StateTicks = 0;
            Log.Add($"{Time.frameCount}: {State} -> {s} (enc {Encounter}, gen {Generation})");
            Debug.Log($"[run] {State} -> {s} · encounter {Encounter} · gen {Generation}");
            State = s;
            KeyboardMouseInput.Blocked = s == RunState.Menu || s == RunState.Results;
            StateChanged?.Invoke(s);
        }

        // ---------------- requests (UI / keyboard / tests) ----------------
        public bool StartRun(string className) => Begin(RunMode.Run, className, RunOrder[0]);
        // A boss with its own arena loads that map first; the new scene's director starts the practice (Start below).
        public bool StartPractice(string className, int encounter)
        {
            encounter = Mathf.Clamp(encounter, 0, Bosses.Length - 1);
            var map = Bosses[encounter].Map;
            if (map != null && State == RunState.Menu)
            {
                int v = RunMaps.IndexOf(map);
                if (v >= 0 && v != RunMaps.Current && RunMaps.InBuild(v)) { pendingPractice = (className, encounter); return SelectMap(v); }
            }
            return Begin(RunMode.Practice, className, encounter);
        }
        static (string cls, int enc)? pendingPractice;   // survives the scene load
        void Start()
        {
            if (pendingPractice == null || Instance != this) return;
            var (cls, enc) = pendingPractice.Value; pendingPractice = null;
            Begin(RunMode.Practice, cls, enc);
        }

        bool Begin(RunMode mode, string className, int encounter)
        {
            if (State != RunState.Menu) return false;
            Mode = mode; ClassName = Array.IndexOf(Classes, className) >= 0 ? className : "Vanguard";
            Difficulty = RunDifficulty.Selected;
            BuildRun(encounter);
            Go(RunState.Ready);
            return true;
        }

        // Menu only: loads the chosen map's run scene (this director is replaced by the one in that scene).
        public bool SelectMap(int index)
        {
            if (State != RunState.Menu || index < 0 || index >= RunMaps.All.Length) return false;
            if (index == RunMaps.Current) return true;
            if (!RunMaps.InBuild(index)) { Debug.LogError("Last Bell: map not in the build: " + RunMaps.All[index].RunScene + " (Last Bell > Run Loop > Rebuild Run Scenes)"); return false; }
            Log.Add($"{Time.frameCount}: load map {RunMaps.All[index].Id}");
            RunMaps.Load(index);
            return true;
        }

        // Menu only: the level the next run or practice starts at (remembered between sessions).
        public bool SelectDifficulty(Difficulty level)
        {
            if (State != RunState.Menu) return false;
            if (RunDifficulty.Selected != level) Log.Add($"{Time.frameCount}: difficulty {RunDifficulty.Name(level)}");
            RunDifficulty.Selected = level;
            return true;
        }

        public bool RequestReady()
        {
            if (State != RunState.Ready || Paused) return false;
            BeginIntro(Encounter);
            return true;
        }

        public bool RequestRetry()
        {
            if (State != RunState.Results && !Paused) return false;
            var mode = Mode; var cls = ClassName; int enc = Mode == RunMode.Practice ? practiceEncounter : RunOrder[0];
            Teardown();
            Mode = mode; ClassName = cls; BuildRun(enc); Go(RunState.Ready);
            return true;
        }

        public bool ReturnToMenu()
        {
            if (State == RunState.Menu) return false;
            if (State != RunState.Results && !Paused) return false;
            Teardown(); Go(RunState.Menu);
            return true;
        }

        public void SetPaused(bool p)
        {
            if (State == RunState.Menu || State == RunState.Results) p = false;
            Paused = p; KeyboardMouseInput.Blocked = p || State == RunState.Menu || State == RunState.Results;
        }

        int practiceEncounter;

        // ---------------- run construction / teardown ----------------
        void BuildRun(int startEncounter)
        {
            Teardown();
            RunsStarted++;
            scopeParent = new GameObject(Mode == RunMode.Run ? $"Run #{RunsStarted}" : $"Practice #{RunsStarted}").transform;
            // FX layers are created once per process and survive map loads: re-creating them crashed the player natively
            // (Mesh.set_colors), so a new map's director adopts the existing ones.
            if (sharedFxRoot == null)
            {
                sharedFxRoot = new GameObject("Run FX (persistent)").transform;
                DontDestroyOnLoad(sharedFxRoot.gameObject);
                sharedBossFx = LastBell.VFX.BellkeeperFx.Create(); sharedBossFx.transform.SetParent(sharedFxRoot, false);
                sharedPlayerFx = PlayerFx.Create(sharedFxRoot);
            }
            bossFx = sharedBossFx; playerFx = sharedPlayerFx;
            World = new CombatWorld(scopeParent, bossFx, playerFx) { Difficulty = Difficulty };   // a retry keeps the run's level
            Player = PlayerController.Spawn(World, ClassName, PlayerSocket, 180);
            Player.Input = InputOverride ?? new KeyboardMouseInput();
            Player.AbilitiesEnabled = false;
            Player.Presenter.GetUp();
            Encounter = startEncounter; practiceEncounter = startEncounter;
            RunTicks = 0; EncountersCleared = 0; Victory = false; DefeatCause = null;
            acc = 0;
        }

        void Teardown()
        {
            if (World != null) World.ClearTransient();
            if (bossFx) bossFx.ResetAll(); if (playerFx) playerFx.ResetAll();
            if (scopeParent) Destroy(scopeParent.gameObject);
            World = null; Player = null; Boss = null; scopeParent = null;
            RunHud.ClearTransient();
            Paused = false;
        }

        void BeginIntro(int index)
        {
            Encounter = index;
            World.ClearTransient();
            if (Boss) Destroy(Boss.gameObject);
            Boss = Bosses[index].Spawn(World);
            World.Boss = Boss;
            World.CombatActive = false;
            // health policy (see PlayerController.RefreshHealth): every run boss after the first refills health; the last
            // also refills potions. A practice starts with a fresh player.
            int pos = Array.IndexOf(RunOrder, index);
            if (Mode == RunMode.Run && pos > 0) Player.RefreshHealth(refillPotions: pos == RunOrder.Length - 1);
            Player.PrepareForEncounter(PlayerSocket, 180);
            Player.AbilitiesEnabled = false;
            Go(RunState.Intro);
        }

        void BeginCombat()
        {
            EncounterStarts++;
            Log.Add($"encounter start {Boss.Id}");
            World.CombatActive = true;
            World.BeginSegment();
            Player.AbilitiesEnabled = true;
            Boss.BeginCombat();
            Go(RunState.Combat);
        }

        void EndCombat(bool bossDefeated)
        {
            EncounterEnds++;
            Log.Add($"encounter end {Boss?.Id} defeated={bossDefeated}");
            World.CombatActive = false;
            Player.AbilitiesEnabled = false;
            World.ClearTransient();
            if (!bossDefeated && Boss) Boss.Halt();
        }

        // ---------------- tick ----------------
        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame && State != RunState.Menu && State != RunState.Results) SetPaused(!Paused);
                if (!Paused)
                {
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    {
                        if (State == RunState.Ready) RequestReady();
                    }
                    // dev shortcuts (labelled in the HUD): finish the current boss / jump it to its next phase
                    if (State == RunState.Combat && Boss && !Boss.Dead)
                    {
                        if (kb.f9Key.wasPressedThisFrame) Boss.DebugDefeat();
                        if (kb.f8Key.wasPressedThisFrame) Boss.DebugSetHpFraction(Boss is SylvaraEncounter ? (Boss.Hp > Boss.MaxHp * 0.65f ? 0.64f : 0.29f) : Boss.NextPhaseFraction());
                    }
                }
            }
            if (World == null || Paused) return;
            acc += Mathf.Min(Time.deltaTime, 0.1f);
            int guard = 0;
            while (acc >= Sim.Dt && guard++ < 8) { acc -= Sim.Dt; Tick(); }
        }

        void Tick()
        {
            int gen = Generation;
            World.Step();
            StateTicks++;
            if (State == RunState.Combat || State == RunState.Cleared || State == RunState.Downed) RunTicks++;
            if (gen != Generation) return;
            switch (State)
            {
                case RunState.Intro:
                    if (StateTicks >= IntroTicks) BeginCombat();
                    break;
                case RunState.Combat:
                    // defeat first: a same-tick boss kill does not save a downed party
                    if (!World.AnyStanding)
                    {
                        DefeatCause = World.LastDamageSource ?? "unknown";
                        EndCombat(false);
                        Go(RunState.Downed);
                    }
                    else if (Boss.Dead)
                    {
                        EndCombat(true);
                        EncountersCleared++;
                        Go(RunState.Cleared);
                    }
                    break;
                case RunState.Cleared:
                    if (StateTicks >= ClearTicks)
                    {
                        if (Boss) { Destroy(Boss.gameObject); Boss = null; World.Boss = null; }
                        if (Mode == RunMode.Practice) { Victory = true; Go(RunState.Results); }
                        else if (Array.IndexOf(RunOrder, Encounter) == RunOrder.Length - 1) { Victory = true; Go(RunState.Results); }
                        else BeginIntro(RunOrder[Array.IndexOf(RunOrder, Encounter) + 1]);
                    }
                    break;
                case RunState.Downed:
                    if (StateTicks >= DownedTicks) { Victory = false; Go(RunState.Results); }
                    break;
            }
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main; if (!cam) return;
            cam.orthographic = false;   // some arenas' own top-down view key (C) switches the camera to orthographic
            Vector3 target, look;
            if (Player) { target = Player.transform.position + FollowOffset; look = Player.transform.position + Vector3.up; }
            else { target = MenuCamPos; look = new Vector3(0, 0, 1); }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, target, ref camVel, 0.12f);
            if (Player) cam.transform.rotation = Quaternion.LookRotation(-FollowOffset + Vector3.up);
            else cam.transform.rotation = Quaternion.LookRotation(look - MenuCamPos);
            cam.fieldOfView = 44;
        }

        // slot on the progress track: one per boss
        public int ProgressSlot => Math.Max(0, Array.IndexOf(RunOrder, Encounter));
    }
}
