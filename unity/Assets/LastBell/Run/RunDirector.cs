using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    public enum RunState { Menu, Ready, Intro, Combat, Cleared, Intermission, Downed, Results }
    public enum RunMode { Run, Practice }

    // Run flow in the single arena (single-arena plan §5):
    //   Menu -> Ready (preparation) -> [Intro -> Combat -> Cleared] x3 minibosses -> Intermission (future shop)
    //   -> Intro -> Combat (Bellkeeper) -> Cleared -> Results (victory)
    //   any Combat -> Downed -> Results (defeat).  Results -> Retry (fresh run, encounter 1) or Menu.
    // Practice runs one chosen encounter with the same rules and never advances or unlocks the run.
    // Every transition goes through Go(), which bumps a generation number: timers and requests from an older state are
    // ignored, and requests are only honoured in the state they belong to, so repeated Ready/Continue/Retry presses can't
    // double-start an encounter or skip a state. Gameplay advances on a 60 Hz tick; the director checks run outcomes after
    // each tick (defeat wins a same-tick tie with boss death, spec §4).
    public sealed class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        public static readonly string[] SlotNames = { "Dial Warden", "Pendulum Sentinel", "Chimebound Oracle", "Intermission", "Bellkeeper" };
        public static readonly string[] Classes = { "Vanguard", "Chimesinger", "Arcanist" };
        const int IntroTicks = 120, ClearTicks = 180, DownedTicks = 100;
        static readonly Vector3 PlayerSocket = new Vector3(0, 0, 6.5f), BossSocket = new Vector3(0, 0, -1.5f);

        public RunState State { get; private set; } = RunState.Menu;
        public RunMode Mode { get; private set; }
        public string ClassName { get; private set; } = "Vanguard";
        public int Encounter { get; private set; }          // 0..3 (3 = final boss)
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
        Camera cam; Vector3 camVel;
        static readonly Vector3 FollowOffset = new Vector3(0, 13f, 9.7f), MenuCamPos = new Vector3(0, 29, 22.5f);

        void Awake()
        {
            Instance = this;
            cam = Camera.main;
            HazardSystem.FindArena();
            if (!GetComponent<RunHud>()) gameObject.AddComponent<RunHud>();
            Application.targetFrameRate = 120;
            if (RunAutoTest.Requested && !GetComponent<RunAutoTest>()) gameObject.AddComponent<RunAutoTest>();
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
        public bool StartRun(string className) => Begin(RunMode.Run, className, 0);
        // Practice-only boss slots after the run's four encounters (area bosses that aren't in the run yet).
        public const int PracticeCrucivar = 4;
        public bool StartPractice(string className, int encounter) => Begin(RunMode.Practice, className, Mathf.Clamp(encounter, 0, PracticeCrucivar));

        bool Begin(RunMode mode, string className, int encounter)
        {
            if (State != RunState.Menu) return false;
            Mode = mode; ClassName = Array.IndexOf(Classes, className) >= 0 ? className : "Vanguard";
            BuildRun(encounter);
            Go(RunState.Ready);
            return true;
        }

        public bool RequestReady()
        {
            if (State != RunState.Ready || Paused) return false;
            BeginIntro(Encounter);
            return true;
        }

        public bool RequestContinue()
        {
            if (State != RunState.Intermission || Paused) return false;
            BeginIntro(3);
            return true;
        }

        public bool RequestRetry()
        {
            if (State != RunState.Results && !Paused) return false;
            var mode = Mode; var cls = ClassName; int enc = Mode == RunMode.Practice ? practiceEncounter : 0;
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
            if (bossFx == null) { bossFx = LastBell.VFX.BellkeeperFx.Create(); bossFx.transform.SetParent(transform, false); playerFx = PlayerFx.Create(transform); }
            World = new CombatWorld(scopeParent, bossFx, playerFx);
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
            Boss = index switch
            {
                0 => BossEncounter.Spawn<DialWarden>(World, BossSocket, 0),
                1 => BossEncounter.Spawn<PendulumSentinel>(World, BossSocket, 0),
                2 => BossEncounter.Spawn<ChimeboundOracle>(World, BossSocket, 0),
                PracticeCrucivar => BossEncounter.Spawn<CrucivarEncounter>(World, BossSocket, 0),
                _ => BossEncounter.Spawn<BellkeeperEncounter>(World, Vector3.zero, 0),
            };
            World.Boss = Boss;
            World.CombatActive = false;
            if (index > 0) Player.RefreshHealth(refillPotions: index == 3);   // health policy: see PlayerController.RefreshHealth
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
                        else if (State == RunState.Intermission) RequestContinue();
                    }
                    // dev shortcuts (labelled in the HUD): finish the current boss / jump it to its next phase
                    if (State == RunState.Combat && Boss && !Boss.Dead)
                    {
                        if (kb.f9Key.wasPressedThisFrame) Boss.DebugDefeat();
                        if (kb.f8Key.wasPressedThisFrame) Boss.DebugSetHpFraction(Boss.NextPhaseFraction);
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
                        else if (Encounter == 2) { Player.PrepareForEncounter(PlayerSocket, 180); Go(RunState.Intermission); }
                        else if (Encounter == 3) { Victory = true; Go(RunState.Results); }
                        else BeginIntro(Encounter + 1);
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
            Vector3 target, look;
            if (Player) { target = Player.transform.position + FollowOffset; look = Player.transform.position + Vector3.up; }
            else { target = MenuCamPos; look = new Vector3(0, 0, 1); }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, target, ref camVel, 0.12f);
            if (Player) cam.transform.rotation = Quaternion.LookRotation(-FollowOffset + Vector3.up);
            else cam.transform.rotation = Quaternion.LookRotation(look - MenuCamPos);
            cam.fieldOfView = 44;
        }

        // slot label for the progress track: 0..2 minibosses, 3 intermission, 4 final
        public int ProgressSlot => State == RunState.Intermission ? 3 : Encounter >= 3 ? 4 : Encounter;
    }
}
