using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    public enum RunState { Menu, Ready, Intro, Combat, Cleared, Downed, Results }
    public enum RunMode { Run, Practice }

    // Run flow (the campaign, Campaign.cs):
    //   Menu -> Ready (preparation) -> [Intro -> Combat -> Cleared] for each fight in EncounterOrder -> Results (victory)
    // A run is the tutorial (first run only), then each area's three minibosses (shuffled) and its area boss, one arena at
    // a time, then the Bellkeeper; a Hard Bellkeeper win appends the Overclocked Tempo Tyrant. When the next fight is in
    // another arena the run travels (TravelTo): the state is carried across the scene load. The shop opens before area
    // bosses and the Bellkeeper. Practice fights only its selected boss.
    //   any Combat -> Downed -> Results (defeat).  Results -> Retry (fresh run, encounter 1) or Menu.
    // Practice runs one chosen encounter with the same rules and never advances or unlocks the run.
    // Every transition goes through Go(), which bumps a generation number: timers and requests from an older state are
    // ignored, and requests are only honoured in the state they belong to, so repeated Ready/Continue/Retry presses can't
    // double-start an encounter or skip a state. Gameplay advances on a 60 Hz tick; the director checks run outcomes after
    // each tick (defeat wins a same-tick tie with boss death, spec §4).
    public sealed class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        // Every real boss encounter. Each run shuffles eligible entries and appends Final; practice offers them
        // all. Map: the run map an entry's practice loads first (null = fight on the current map). Add a boss with one line.
        public sealed class BossEntry
        {
            public readonly string Name; public readonly Func<CombatWorld, BossEncounter> Spawn; public readonly bool InRun, Final; public readonly string Map;
            // Phases: how many HP phases the practice phase row offers (1 = Phase 1 only). It must match the encounter's
            // PhaseStarts; the autotest checks that for every entry.
            public readonly int Phases;
            // WarmUp (optional): runs on the Ready screen before this boss's intro, while nobody is fighting. Use it for
            // first-use costs that would otherwise land in combat: draw the fight's labels once in each font and size, build
            // pooled objects hidden, and so on. The boss doesn't exist yet; the world, the player and the allies do.
            public readonly Action<CombatWorld> WarmUp;
            public BossEntry(string name, Func<CombatWorld, BossEncounter> spawn, bool inRun = true, string map = null, bool final = false, int phases = 1, Action<CombatWorld> warmUp = null) { Name = name; Spawn = spawn; InRun = inRun; Map = map; Final = final; Phases = Math.Max(1, phases); WarmUp = warmUp; }
        }
        public static readonly BossEntry[] Bosses =
        {
            new BossEntry("Bellkeeper", w => BossEncounter.Spawn<BellkeeperEncounter>(w, Vector3.zero, 0), final: true, phases: 3),
            // Skyward Spire area boss; authored practice map
            new BossEntry("Aurelion (area 1)", w => BossEncounter.Spawn<AurelionEncounter>(w, new Vector3(0, 0, -1.5f), 0), map: AurelionMap, phases: 4),   // v4 adds Last Light
            // Verdant Chimes area boss (graybox); authored practice map
            new BossEntry("Sylvara (area 3)", w => BossEncounter.Spawn<SylvaraEncounter>(w, Vector3.zero, 0), map: SylvaraMap, phases: 4,
                warmUp: w => w.Bullets.Prewarm(w.Root)),   // the Seed Storm burst-preview bars and materials (review SYL-PREVIEW-COLD)
            // Brass Foundry area boss; authored practice map
            new BossEntry("Crucivar (area 2)", w => BossEncounter.Spawn<CrucivarEncounter>(w, new Vector3(0, 0, -1.5f), 0), map: CrucivarMap, phases: 3, warmUp: CrucivarEncounter.WarmUp),
            // Metronome Court miniboss: explicit Practice entry only (inRun: false), so shuffled run slots are unchanged.
            // Run integration point for later review: set inRun to true here (docs/metronome-arena/tempo-tyrant.md).
            // Phase 2 is Broken Cadence (below 20%).
            new BossEntry("Tempo Tyrant (practice)", w => BossEncounter.Spawn<TempoTyrantEncounter>(w, new Vector3(0, 0, -2f), 0), inRun: false, map: TempoTyrantMap, phases: 2),
            // Skyward Spire miniboss, practice only (docs/bosses/chimebound-oracle.md). Picked on the Metronome Court in
            // Boss Rush instead, the court's metronome follows his clock.
            new BossEntry("Chimebound Oracle (practice)", w => BossEncounter.Spawn<ChimeboundOracleEncounter>(w, new Vector3(0, 0, -2f), 0), inRun: false, map: AurelionMap, phases: 2),
            // Skyward Spire miniboss with the parry QTE, practice only (docs/bosses/dial-warden.md)
            new BossEntry("Dial Warden (practice)", w => BossEncounter.Spawn<DialWardenEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: AurelionMap, phases: 2),
            // Skyward Spire miniboss, practice only (docs/bosses/pendulum-sentinel.md, a proposal)
            new BossEntry("Pendulum Sentinel (practice)", w => BossEncounter.Spawn<PendulumSentinelEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: AurelionMap, phases: 2),
            // Brass Foundry miniboss, practice only (docs/bosses/escapement-twins.md, a proposal): two bodies, one bar
            new BossEntry("Escapement Twins (practice)", w => BossEncounter.Spawn<EscapementTwinsEncounter>(w, EscapementTwinsRules.TickAt(90f), 90f), inRun: false, map: CrucivarMap, phases: 2),
            // Brass Foundry miniboss, practice only, placeholder look (docs/bosses/crucible-skater.md, a proposal)
            new BossEntry("Crucible Skater (practice)", w => BossEncounter.Spawn<CrucibleSkaterEncounter>(w, new Vector3(0, 0, -2f), 0), inRun: false, map: CrucivarMap, phases: 2),
            // Verdant Chimes miniboss, practice only (docs/bosses/briar-metronome.md, a proposal; placeholder body)
            new BossEntry("Briar Metronome (practice)", w => BossEncounter.Spawn<BriarMetronomeEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: SylvaraMap, phases: 2),
            // Verdant Chimes miniboss, practice only (docs/bosses/silt-carillon.md, a proposal; placeholder body)
            new BossEntry("Silt Carillon (practice)", w => BossEncounter.Spawn<SiltCarillonEncounter>(w, Vector3.zero, 0), inRun: false, map: SylvaraMap, phases: 2),
            // Verdant Chimes miniboss, practice only, placeholder visuals (docs/bosses/vesper-seedkeeper.md, a proposal)
            new BossEntry("Vesper Seedkeeper (practice)", w => BossEncounter.Spawn<VesperSeedkeeperEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: SylvaraMap, phases: 2),
            // New-player tutorial (Run/Tutorial, docs/tutorial.md): a guided Tempo Tyrant fight on Metronome Court, practice only.
            // Also LastBell.exe -lastbellTutorial. Kept last so the other entries' indices don't move.
            new BossEntry(TutorialName, w => TutorialDirector.Attach(BossEncounter.Spawn<TempoTyrantEncounter>(w, new Vector3(0, 0, -2f), 0)), inRun: false, map: TempoTyrantMap),
            // Brass Foundry miniboss, practice only (docs/bosses/rivet-cantor.md, a proposal; placeholder visuals). After the
            // tutorial so the earlier entries' indices don't move.
            new BossEntry("Rivet Cantor (practice)", w => BossEncounter.Spawn<RivetCantorEncounter>(w, new Vector3(0, 0, -1.5f), 0), inRun: false, map: CrucivarMap, phases: 2),
            // The campaign's bonus fight after a Hard Bellkeeper win (Campaign.cs); its Boss Rush card is locked until then.
            // Phase 2 is Broken Cadence, which he enters below 50%. Kept last so the other entries' indices don't move.
            new BossEntry(Campaign.BonusName, w => TempoTyrantEncounter.SpawnOverclocked(w, new Vector3(0, 0, -2f), 0), inRun: false, map: TempoTyrantMap, phases: 2),
        };
        // The area bosses and the final boss (InRun entries). Each run's own sequence is the campaign (Campaign.CreateOrder),
        // which also puts each area's minibosses before its boss.
        public static readonly int[] RunOrder = Enumerable.Range(0, Bosses.Length).Where(i => Bosses[i].InRun).OrderBy(i => Bosses[i].Final).ToArray();
        public static int[] CreateRunOrder(System.Random random = null) => Campaign.CreateOrder(random, Campaign.TutorialFirst);
        public int[] EncounterOrder { get; private set; } = (int[])RunOrder.Clone();
        public static int SylvaraPractice => Array.FindIndex(Bosses, b => b.Map == SylvaraMap);
        public const string SylvaraMap = "verdant-chimes";
        public static int CrucivarPractice => Array.FindIndex(Bosses, b => b.Map == CrucivarMap);
        public const string CrucivarMap = "brass-foundry";
        public static int AurelionPractice => Array.FindIndex(Bosses, b => b.Map == AurelionMap);
        public const string AurelionMap = "skyward-spire";
        public static int TempoTyrantPractice => Array.FindIndex(Bosses, b => b.Map == TempoTyrantMap);
        public const string TempoTyrantMap = "metronome-court";
        public static int ChimeboundOraclePractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Chimebound Oracle"));
        public static int DialWardenPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Dial Warden"));
        public const string TutorialName = "Tutorial (Tempo Tyrant)";
        public static int TutorialPractice => Array.FindIndex(Bosses, b => b.Name == TutorialName);
        public static int PendulumSentinelPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Pendulum Sentinel"));
        public static int EscapementTwinsPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Escapement Twins"));
        public static int RivetCantorPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Rivet Cantor"));
        public static int CrucibleSkaterPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Crucible Skater"));
        public static int BriarMetronomePractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Briar Metronome"));
        public static int SiltCarillonPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Silt Carillon"));
        public static int VesperSeedkeeperPractice => Array.FindIndex(Bosses, b => b.Name.StartsWith("Vesper Seedkeeper"));
        // The Tollbreaker took the Vanguard's place (Chzy18, 29 Oct), and the Vanguard was removed from the game (1 Nov).
        public static readonly string[] Classes = { "Tollbreaker", "Chimesinger", "Arcanist" };
        const int IntroTicks = 120, ClearTicks = 180, DownedTicks = 100;
        public const int PracticeAutoReadyTicks = 45;   // 0.75 s in the arena before a practice fight starts
        // Automated suites (every one writes to -lastbellOut) drive Ready themselves, so they keep the manual start.
        public static bool AutoReady = !Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
        static readonly Vector3 PlayerSocket = new Vector3(0, 0, 6.5f);

        public RunState State { get; private set; } = RunState.Menu;
        public RunMode Mode { get; private set; }
        public string ClassName { get; private set; } = "Tollbreaker";
        public Difficulty Difficulty { get; private set; } = Difficulty.Medium;   // copied from the menu at run start
        public int AllyCount { get; private set; }            // bot allies, copied from the menu at run start (RunParty); a retry keeps it
        public readonly List<PlayerController> Allies = new List<PlayerController>();
        // allies line up behind the player at the south entry, alternating left and right
        static Vector3 AllySocket(int i) => PlayerSocket + new Vector3((i % 2 == 0 ? -1 : 1) * (1.6f + 1.6f * (i / 2)), 0, 1.2f);
        public int Encounter { get; private set; }          // index into Bosses (the last is the final boss)
        public int PracticePhase { get; private set; } = 1; // practice only: the 1-based HP phase the fight starts in (a retry keeps it)
        public int Generation { get; private set; }
        public int StateTicks { get; private set; }
        public float IntroProgress => Mathf.Clamp01((float)StateTicks / IntroTicks);
        public bool Victory { get; private set; }
        public bool BonusFight => Mode == RunMode.Run && Encounter == Campaign.BonusIndex;   // the Overclocked Tempo Tyrant
        public bool BonusLost { get; private set; }   // downed in the bonus fight: the run was already won
        public bool Travelling { get; private set; }  // a scene load to the next arena is under way
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
            if (!mapArgHandled && !LastBell.Menu.MenuLaunch.Pending)
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
            if (!GetComponent<Shop.RunShopFlow>()) gameObject.AddComponent<Shop.RunShopFlow>();   // intermission shop (docs/design/run-shop-plan.md)
            LastBell.Menu.MenuSettings.ApplyFrameRate();   // Settings: VSync (off by default) and FPS cap (120 by default)
            if (RunAutoTest.Requested) RunAutoTest.Launch();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        // LAN client (Networking.LanClientWorld): this idle, disabled director holds a replica of the host's fight so RunHud
        // can draw it. Nothing here steps the replica; DetachLanReplica puts the director back to idle.
        public void AttachLanReplica(CombatWorld world, PlayerController local, IEnumerable<PlayerController> others)
        {
            World = world; Player = local; Allies.Clear(); Allies.AddRange(others); Mode = RunMode.Practice;
        }
        public void ApplyLanState(RunState state, int stateTicks, bool victory, BossEncounter boss)
        {
            // the replica never runs Go(): start or end the boss music here, as Go() does on the host
            bool changed = state != State || boss != Boss;
            State = state; StateTicks = stateTicks; Victory = victory; Boss = boss;
            if (changed) LastBell.Menu.MenuMusic.OnRunState(this, state);
        }
        public void DetachLanReplica()
        {
            World = null; Player = null; Boss = null; Allies.Clear(); State = RunState.Menu; StateTicks = 0; Victory = false;
            LastBell.Menu.MenuMusic.OnRunState(this, RunState.Menu);
        }

        void Go(RunState s)
        {
            Generation++; Transitions++; StateTicks = 0;
            Log.Add($"{Time.frameCount}: {State} -> {s} (enc {Encounter}, gen {Generation})");
            Debug.Log($"[run] {State} -> {s} · encounter {Encounter} · gen {Generation}");
            State = s;
            LastBell.Menu.MenuMusic.OnRunState(this, s);
            KeyboardMouseInput.Blocked = s == RunState.Menu || s == RunState.Results;
            StateChanged?.Invoke(s);
            if (s == RunState.Ready) WarmUpEncounter();
        }

        // Ready-screen warm-up (Chzy18, 2 Nov; perf report bellkeeper-ultimate-2026-11-02: first-use costs on the first combat
        // frame). Runs the next boss's BossEntry.WarmUp, then ReadyWarmUp for shared code (HUD fonts and the like). A failing
        // warm-up is logged and never blocks the run.
        public static event Action<RunDirector, int> ReadyWarmUp;
        void WarmUpEncounter()
        {
            if (World == null || Encounter < 0 || Encounter >= Bosses.Length) return;
            try { Bosses[Encounter].WarmUp?.Invoke(World); } catch (Exception e) { Debug.LogException(e); }
            try { ReadyWarmUp?.Invoke(this, Encounter); } catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------- requests (UI / keyboard / tests) ----------------
        public bool StartRun(string className) => StartRun(className, CreateRunOrder());
        public bool StartRun(string className, int[] order)
        {
            if (State != RunState.Menu || !Campaign.Valid(order)) return false;
            EncounterOrder = (int[])order.Clone();
            return Begin(RunMode.Run, className, EncounterOrder[0]);
        }
        // Explicit map/encounter test launch. Legacy practice still uses its authored default; this path never remaps.
        public bool StartPracticeOnSelectedMap(string className, int encounter, int phase = 1)
        {
            if (State != RunState.Menu || Array.IndexOf(Classes, className) < 0 ||
                LastBell.Menu.TestRushSetup.Incompatibility(RunMaps.Current, encounter) != null) return false;
            return Begin(RunMode.Practice, className, encounter, phase);
        }
        // A boss with its own arena loads that map first; the new scene's director starts the practice (Start below).
        public bool StartPractice(string className, int encounter, int phase = 1)
        {
            encounter = Mathf.Clamp(encounter, 0, Bosses.Length - 1);
            var map = Bosses[encounter].Map;
            if (map != null && State == RunState.Menu)
            {
                int v = RunMaps.IndexOf(map);
                if (v >= 0 && v != RunMaps.Current && RunMaps.InBuild(v)) { pendingPractice = (className, encounter, phase); return SelectMap(v); }
            }
            return Begin(RunMode.Practice, className, encounter, phase);
        }
        static (string cls, int enc, int phase)? pendingPractice;   // survives the scene load
        void Start()
        {
            if (LastBell.Menu.MenuLaunch.Consume(this)) { pendingPractice = null; return; }
            if (Campaign.Pending != null && Instance == this) { ResumeCarried(); return; }
            if (pendingPractice == null || Instance != this) return;
            var (cls, enc, ph) = pendingPractice.Value; pendingPractice = null;
            Begin(RunMode.Practice, cls, enc, ph);
        }

        bool Begin(RunMode mode, string className, int encounter, int phase = 1)
        {
            // a LAN fight is one practice encounter the lobby allows
            if (Networking.LanSessionCoordinator.Instance && (mode != RunMode.Practice || encounter < 0 || encounter >= Bosses.Length || !Networking.LanLobby.ValidEncounter(LastBell.Menu.TestRushSetup.CardName(encounter)))) return false;
            if (State != RunState.Menu) return false;
            PracticePhase = mode == RunMode.Practice && encounter >= 0 && encounter < Bosses.Length ? Mathf.Clamp(phase, 1, Bosses[encounter].Phases) : 1;
            Mode = mode; ClassName = Array.IndexOf(Classes, className) >= 0 ? className : Classes[0];
            Difficulty = RunDifficulty.Selected;
            AllyCount = Networking.LanHostRoster.Active != null ? Networking.LanHostRoster.Active.Remote.Count : RunParty.Selected;
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

        // Menu only: how many bot allies the next run or practice brings (remembered between sessions).
        public bool SelectAllies(int count)
        {
            if (State != RunState.Menu) return false;
            count = Mathf.Clamp(count, 0, RunParty.MaxAllies);
            if (RunParty.Selected != count) Log.Add($"{Time.frameCount}: allies {count}");
            RunParty.Selected = count;
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
            var mode = Mode; var cls = ClassName; int enc = Mode == RunMode.Practice ? practiceEncounter : EncounterOrder[0];
            if (mode == RunMode.Run)
            {
                // a fresh run of the same campaign: no tutorial (it's been seen) and no bonus until it is earned again
                var order = EncounterOrder.Where(i => i != Campaign.BonusIndex && i != TutorialPractice).ToArray();
                if (Campaign.Valid(order)) EncounterOrder = order;
                enc = EncounterOrder[0];
                if (Campaign.Travel && TravelTo(enc, fresh: true)) return true;
            }
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
            // bot allies (RunParty): classes spread across all three, standing in a row behind the player
            Allies.Clear();
            var lanRoster = Networking.LanHostRoster.Active;
            var allyClasses = lanRoster == null ? RunParty.AllyClasses(ClassName, AllyCount) : lanRoster.Remote.Select(m => m.ClassName).ToArray();
            for (int i = 0; i < allyClasses.Length; i++)
            {
                var a = PlayerController.Spawn(World, allyClasses[i], AllySocket(i), 180);
                a.Input = lanRoster == null ? (IInputSource)new AllyBot(i, allyClasses.Length, AllyBot.AllyMode) : lanRoster.InputFor(lanRoster.Remote[i].Id); a.AbilitiesEnabled = false; a.Presenter.GetUp();
                Allies.Add(a);
            }
            Encounter = startEncounter; practiceEncounter = startEncounter;
            RunTicks = 0; EncountersCleared = 0; Victory = false; BonusLost = false; DefeatCause = null;
            acc = 0;
        }

        void Teardown()
        {
            if (World != null) World.ClearTransient();
            if (bossFx) bossFx.ResetAll(); if (playerFx) playerFx.ResetAll();
            if (scopeParent) Destroy(scopeParent.gameObject);
            Allies.Clear();
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
            ApplyPracticePhase();   // the boss bar shows the chosen phase during the intro
            World.Boss = Boss;
            World.CombatActive = false;
            // health policy (gpt/08 §3): every run fight after the first refills health; potions carry over between minibosses
            // and refill before an area boss and the final boss (Campaign.RefillsPotions). A practice starts with a fresh player.
            int pos = Array.IndexOf(EncounterOrder, index);
            bool potions = Campaign.RefillsPotions(index);
            if (Mode == RunMode.Run && pos > 0) Player.RefreshHealth(refillPotions: potions);
            Player.PrepareForEncounter(PlayerSocket, 180);
            Player.AbilitiesEnabled = false;
            for (int i = 0; i < Allies.Count; i++)
            {
                if (Mode == RunMode.Run && pos > 0) Allies[i].RefreshHealth(refillPotions: potions);
                Allies[i].PrepareForEncounter(AllySocket(i), 180); Allies[i].AbilitiesEnabled = false;
            }
            Go(RunState.Intro);
        }

        void BeginCombat()
        {
            EncounterStarts++;
            Log.Add($"encounter start {Boss.Id}");
            World.CombatActive = true;
            World.BeginSegment();
            Player.AbilitiesEnabled = true;
            foreach (var a in Allies) a.AbilitiesEnabled = true;
            Boss.BeginCombat();
            ApplyPracticePhase();   // again after BeginCombat, so a boss that resets its phase state there keeps the pick
            Go(RunState.Combat);
        }

        // Practice phase row: start the fight in PracticePhase (1 = a normal start). The player was built fresh for this
        // practice, so health and potions are full as at any fight start.
        void ApplyPracticePhase()
        {
            if (Mode != RunMode.Practice || PracticePhase <= 1 || !Boss) return;
            bool ok = Boss.StartAtPhase(PracticePhase);
            Log.Add($"practice phase {PracticePhase} {(ok ? "ok" : "refused")} {Boss.Id} hp {Boss.Hp}/{Boss.MaxHp} '{Boss.PhaseName}'");
            if (!ok) Debug.LogWarning($"[run] practice: {Boss.Id} could not start at phase {PracticePhase} ({Boss.PhaseCount} phases)");
        }

        void EndCombat(bool bossDefeated)
        {
            EncounterEnds++;
            Log.Add($"encounter end {Boss?.Id} defeated={bossDefeated}");
            World.CombatActive = false;
            Player.AbilitiesEnabled = false;
            foreach (var a in Allies) a.AbilitiesEnabled = false;
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
                        if (kb.f8Key.wasPressedThisFrame) Boss.DebugSetHpFraction(Boss.NextPhaseFraction());
                    }
                }
            }
            if (World == null || Paused || Travelling) return;
            acc += Mathf.Min(Time.deltaTime, 0.1f);
            int guard = 0;
            using (PmSmooth.Auto()) SimSmoother.RestoreAll();   // ticks see exact sim poses
            while (acc >= Sim.Dt && guard++ < 8 && !Travelling && World != null) { acc -= Sim.Dt; using (PmTick.Auto()) Tick(); using (PmSmooth.Auto()) SimSmoother.CaptureAll(); }
            if (World == null) return;
            using (PmSmooth.Auto()) SimSmoother.PresentAll(acc / Sim.Dt);   // draw between the last two ticks: smooth at any frame rate
            World?.Bullets.Draw(World.Root);
        }

        // profiler scopes (perf agent, 2 Nov): a sim tick, the world step inside it, and the pose smoothing around it
        static readonly Unity.Profiling.ProfilerMarker PmTick = new Unity.Profiling.ProfilerMarker("LastBell.Director.Tick"),
            PmWorld = new Unity.Profiling.ProfilerMarker("LastBell.Director.WorldStep"), PmSmooth = new Unity.Profiling.ProfilerMarker("LastBell.Director.SimSmoother");

        void Tick()
        {
            int gen = Generation;
            using (PmWorld.Auto()) World.Step();
            StateTicks++;
            if (State == RunState.Combat || State == RunState.Cleared || State == RunState.Downed) RunTicks++;
            if (gen != Generation) return;
            // practice has no preparation card: it starts by itself after a short beat (tests keep the manual Ready)
            if (State == RunState.Ready && Mode == RunMode.Practice && AutoReady && StateTicks >= PracticeAutoReadyTicks && !Shop.RunShopFlow.Showing) { RequestReady(); return; }   // an area-boss practice shop waits for Ready
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
                        OnCleared();
                        Go(RunState.Cleared);
                    }
                    break;
                case RunState.Cleared:
                    if (StateTicks >= ClearTicks)
                    {
                        if (Boss) { Destroy(Boss.gameObject); Boss = null; World.Boss = null; }
                        if (Mode == RunMode.Practice) { Victory = true; Go(RunState.Results); }
                        else if (Array.IndexOf(EncounterOrder, Encounter) == EncounterOrder.Length - 1) { Victory = true; Go(RunState.Results); }
                        else
                        {
                            int next = EncounterOrder[Array.IndexOf(EncounterOrder, Encounter) + 1];
                            // another arena: carry the run into its scene, where it continues at Ready
                            if (Campaign.Travel && TravelTo(next, fresh: false)) return;
                            // run shop: the intermission before an area boss or the final boss is its preparation (Ready)
                            if (Shop.RunShopFlow.Enabled && Campaign.ShopBefore(next)) { Encounter = next; Go(RunState.Ready); }
                            else BeginIntro(next);
                        }
                    }
                    break;
                case RunState.Downed:
                    // the bonus fight comes after the run is won: losing it still ends the run in victory
                    if (StateTicks >= DownedTicks) { BonusLost = BonusFight; Victory = BonusLost; Go(RunState.Results); }
                    break;
            }
        }

        // ---------------- campaign ----------------
        // A fight was won: save tutorial / unlock progress, and a Hard Bellkeeper win earns the bonus fight.
        void OnCleared()
        {
            if (Encounter == TutorialPractice && !Campaign.TutorialDone) { Campaign.TutorialDone = true; Log.Add("campaign: tutorial done"); }
            if (Mode != RunMode.Run || Encounter != Campaign.FinalIndex || Difficulty != Difficulty.Hard) return;
            if (!Campaign.OverclockedUnlocked) { Campaign.OverclockedUnlocked = true; Log.Add("campaign: Overclocked Tempo Tyrant unlocked"); }
            int bonus = Campaign.BonusIndex;
            if (bonus >= 0 && Array.IndexOf(EncounterOrder, bonus) < 0 && Array.IndexOf(EncounterOrder, Encounter) == EncounterOrder.Length - 1)
            { EncounterOrder = EncounterOrder.Concat(new[] { bonus }).ToArray(); Log.Add("campaign: bonus fight added"); }
        }

        // Loads the arena of `next` if it isn't this one; the new scene's director resumes the run (ResumeCarried).
        // fresh: a Retry (a new run from fight 1). Returns false when the fight is on this map or its scene isn't built.
        bool TravelTo(int next, bool fresh)
        {
            int map = RunMaps.IndexOf(Campaign.MapFor(next));
            if (map < 0 || map == RunMaps.Current || !RunMaps.InBuild(map)) return false;
            var c = new Campaign.Carry
            {
                ClassName = ClassName, Difficulty = Difficulty, AllyCount = AllyCount, Order = (int[])EncounterOrder.Clone(), Next = next,
                RunsStarted = fresh ? RunsStarted + 1 : RunsStarted, RunTicks = fresh ? 0 : RunTicks, EncountersCleared = fresh ? 0 : EncountersCleared,
                Fresh = fresh, Scene = RunMaps.All[map].RunScene,
            };
            if (!fresh && Player) { c.Potions = Player.Potions; c.AllyPotions = Allies.Select(a => a.Potions).ToArray(); }
            if (!fresh && Shop.RunShopFlow.Instance) Shop.RunShopFlow.Instance.CarryForTravel();
            else Shop.RunShopFlow.DropCarried();
            Campaign.Pending = c;
            Log.Add($"{Time.frameCount}: travel to {RunMaps.All[map].Id} for {Bosses[next].Name}{(fresh ? " (fresh run)" : "")}");
            Debug.Log($"[run] travel -> {RunMaps.All[map].Id} · next {Bosses[next].Name}{(fresh ? " · fresh run" : "")}");
            Travelling = true; Paused = false;
            if (Boss) { Destroy(Boss.gameObject); Boss = null; }
            Teardown();
            RunMaps.Load(map);
            return true;
        }

        // The new arena's director: rebuild the carried run and wait at Ready (the shop opens there before an area boss).
        void ResumeCarried()
        {
            var c = Campaign.Pending; Campaign.Pending = null; pendingPractice = null;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (c.Scene != null && !string.Equals(scene, c.Scene, StringComparison.OrdinalIgnoreCase))
            { Debug.LogError($"[run] travel: expected {c.Scene}, loaded {scene}"); Shop.RunShopFlow.DropCarried(); return; }
            Mode = RunMode.Run; ClassName = c.ClassName; Difficulty = c.Difficulty; AllyCount = c.AllyCount;
            EncounterOrder = c.Order; PracticePhase = 1;
            RunsStarted = c.RunsStarted - 1;   // BuildRun counts this one
            BuildRun(c.Next);
            RunTicks = c.RunTicks; EncountersCleared = c.EncountersCleared;
            if (c.Potions >= 0) Player.CarryPotions(c.Potions);
            for (int i = 0; c.AllyPotions != null && i < Allies.Count && i < c.AllyPotions.Length; i++) Allies[i].CarryPotions(c.AllyPotions[i]);
            Log.Add($"{Time.frameCount}: arrived at {RunMaps.CurrentName} for {Bosses[c.Next].Name}");
            Go(RunState.Ready);
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main; if (!cam) return;
            cam.orthographic = false;   // some arenas' own top-down view key (C) switches the camera to orthographic
            Vector3 target, look;
            if (Player)
            {
                // an encounter can ask the camera to keep a second point in view too (Aurelion's Last Light: the boss with the
                // remaining floor, readability A2): shift toward it and pull back with the distance, same angle
                var at = Player.transform.position; float zoom = 1;
                var also = FrameAlso?.Invoke();
                if (also.HasValue) { var d = Vector3.ProjectOnPlane(also.Value - at, Vector3.up); at += d * 0.45f; zoom = 1 + 0.45f * Mathf.Clamp01((d.magnitude - 5f) / 20f); }
                target = at + FollowOffset * zoom; look = at + Vector3.up;
            }
            else { target = MenuCamPos; look = new Vector3(0, 0, 1); }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, target, ref camVel, 0.12f);
            if (Player) cam.transform.rotation = Quaternion.LookRotation(-FollowOffset + Vector3.up);
            else cam.transform.rotation = Quaternion.LookRotation(look - MenuCamPos);
            cam.fieldOfView = 44;
            if (ShotCamera.HasValue) { cam.transform.position = ShotCamera.Value.pos; cam.transform.rotation = Quaternion.LookRotation(ShotCamera.Value.look - ShotCamera.Value.pos); }
        }
        public static (Vector3 pos, Vector3 look)? ShotCamera;   // autotest gallery: a fixed overhead view of the whole arena
        public static Func<Vector3?> FrameAlso;   // set by an encounter for one phase, cleared after; null = follow the player only

        // slot on the progress track: one per boss
        public int ProgressSlot => Math.Max(0, Array.IndexOf(EncounterOrder, Encounter));
    }
}
