using System;
using UnityEngine;

namespace LastBell.Run.Presentation
{
    // Boss intros and defeats (Presentation lane, 8 Nov 2026; proposal, done as recommended, review later). The shared moment
    // around every boss fight, with no change to fight timing: the intro still lasts RunDirector's 120 ticks and Cleared its
    // 180, and nothing here steps, delays or reads into the sim.
    //   Intro: the name card (RunHud.IntroCard) gets a role and area line, the title, and cinematic bars; the camera frames the
    //     boss with the player until 0.75 of the intro, so it is back on the player before combat; a boss with no entrance
    //     clip of its own plays an existing one (the Bellkeeper: PhaseShift), so every boss moves at its entrance.
    //   Defeat: the killing moment freezes every character for a few frames, a soft gold flash lifts off the screen, the boss
    //     plays its Defeat clip in brief slow motion and then holds its last frame, the camera frames the fallen boss, and the
    //     "Defeated" card (RunHud.ClearedCard) arrives after the beat, with the fight time.
    // LAN clients poll the replicated run state each frame, so they get the same moments (the host's clips come through the
    // replicated pose; a client never starts a clip itself).
    // Sound lane hooks: IntroStarted, NameLanded, DefeatMoment (C# events, presentation thread).
    [DefaultExecutionOrder(9500)]
    public sealed class BossMoments : MonoBehaviour
    {
        public static event Action<BossEncounter> IntroStarted, NameLanded, DefeatMoment;

        public const float FrameUntil = 0.75f;                     // intro progress: the camera returns to the player after this
        public const float DefeatFreeze = 0.1f, SlowFor = 0.5f, SlowScale = 0.3f, SlowEase = 0.3f, FlashLife = 0.5f;
        static readonly string[] EntranceClips = { "Entrance", "Intro", "PhaseShift" };   // existing clips only, first found

        RunDirector d;
        RunState seenState = RunState.Menu; BossEncounter seenBoss; int combatStartTicks;
        float defeatAt = -1; bool landed, entranceChecked; CharacterPresenter bossPresenter;
        // camera framing: a translation added after the director's camera (and removed after rendering, like CameraShake), so
        // the camera's rotation never changes. Movement input is mapped through the camera's flattened forward, and even a
        // tiny pitch change rounds that vector differently, so RunDirector.FrameAlso (which pulls the offset back) is not used.
        bool frameWant; Vector3 applied; Camera cam;
        public static float FrameWeight { get; private set; }
        public static bool Framing => Instance && Instance.frameWant;

        // read by RunHud's cards and the tests
        public static string EntranceClip { get; private set; }     // the clip the boss showed at its entrance ("" = none)
        public static string FightTime { get; private set; } = "";  // "4:32", set when the boss falls
        public static float DefeatSeconds => Instance && Instance.defeatAt >= 0 ? Time.unscaledTime - Instance.defeatAt : -1;
        public static float Flash { get; private set; }
        // card strings, built once per intro or defeat (RunHud draws them every frame without building text)
        public static string IntroRoleSpaced { get; private set; } = "";
        public static string IntroEpithet { get; private set; }
        public static string FightTimeLine { get; private set; } = "";
        public static readonly string DefeatedSpaced = UiTheme.Spaced("Defeated");
        public static int Intros, Defeats;
        public static BossMoments Instance { get; private set; }

        void Awake() { d = GetComponent<RunDirector>(); Instance = this; }
        void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Restore;
        void OnDisable() { UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Restore; Unshift(); FrameWeight = 0; }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void TakeFrame() => frameWant = true;
        void ReleaseFrame() => frameWant = false;
        void Restore(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c) { if (c == cam) Unshift(); }
        void Unshift() { if (cam && applied != Vector3.zero) cam.transform.position -= applied; applied = Vector3.zero; }

        // after RunDirector.LateUpdate and before CameraShake (10000): shift toward the boss and back a little, eased
        void LateUpdate()
        {
            Unshift();
            float dt = Time.unscaledDeltaTime;
            FrameWeight = Mathf.MoveTowards(FrameWeight, frameWant ? 1 : 0, dt / 0.4f);
            cam = Camera.main;
            var b = d ? d.Boss : null; var p = d ? d.Player : null;
            if (FrameWeight <= 0 || !cam || !b || !p || RunDirector.ShotCamera.HasValue) return;
            float w = FrameWeight * FrameWeight * (3 - 2 * FrameWeight);
            var to = Vector3.ProjectOnPlane(SimSmoother.SimPosition(b.transform) - p.transform.position, Vector3.up);
            applied = (Vector3.ClampMagnitude(to * 0.42f, 6f) - cam.transform.forward * 2.5f) * w;
            cam.transform.position += applied;
        }

        // allocation check (RunAutoTest.BossMoments)
        public static bool Measuring; public static long AllocBytes; public static int MeasuredFrames;

        void Update()
        {
            Unshift();   // before RunDirector.LateUpdate reads the camera (a batch run renders nothing, so Restore never runs)
            if (d == null) return;
            var state = d.State; var boss = d.Boss;
            if (state != seenState || boss != seenBoss) { OnState(seenState, state, boss); seenState = state; seenBoss = boss; return; }
            long a0 = Measuring ? HitFeel.AllocNow() : 0;
            bool on = PresentationSettings.BossMoments;
            float dt = Time.unscaledDeltaTime;
            Flash = Mathf.MoveTowards(Flash, 0, dt / FlashLife);
            if (state == RunState.Intro && boss)
            {
                if (!entranceChecked && d.StateTicks >= 3) { entranceChecked = true; Entrance(boss, on); }
                if (!landed && d.IntroProgress >= 0.3f) { landed = true; NameLanded?.Invoke(boss); }
                if (on && d.IntroProgress < FrameUntil) TakeFrame(); else ReleaseFrame();
            }
            else if (state == RunState.Cleared && defeatAt >= 0)
            {
                float t = Time.unscaledTime - defeatAt;
                if (bossPresenter)
                {
                    float s = !on ? 1 : t < DefeatFreeze ? 0 : t < DefeatFreeze + SlowFor ? SlowScale : Mathf.Lerp(SlowScale, 1, (t - DefeatFreeze - SlowFor) / SlowEase);
                    bossPresenter.TimeScale = Mathf.Clamp01(s);
                    if (on) bossPresenter.HoldOneShot();   // the Defeat pose stays to the end of Cleared
                }
                if (on && boss) TakeFrame(); else ReleaseFrame();
            }
            if (Measuring) { AllocBytes += Math.Max(0, HitFeel.AllocNow() - a0); MeasuredFrames++; }
        }

        void OnState(RunState from, RunState to, BossEncounter boss)
        {
            ReleaseFrame();
            if (bossPresenter) bossPresenter.TimeScale = 1;
            bool on = PresentationSettings.BossMoments;
            if (to == RunState.Intro && boss)
            {
                Intros++; landed = false; entranceChecked = false; EntranceClip = ""; defeatAt = -1; FightTime = "";
                IntroRoleSpaced = UiTheme.Spaced(RoleLine(d)); IntroEpithet = Epithet(boss);
                bossPresenter = boss.GetComponentInChildren<CharacterPresenter>();
                IntroStarted?.Invoke(boss);
            }
            else if (to == RunState.Combat) combatStartTicks = d.RunTicks;
            else if (to == RunState.Cleared && boss)
            {
                Defeats++; defeatAt = Time.unscaledTime;
                bossPresenter = boss.GetComponentInChildren<CharacterPresenter>();
                int secs = Mathf.Max(0, d.RunTicks - combatStartTicks) / Sim.Hz;
                FightTime = combatStartTicks > 0 || d.RunTicks > 0 ? $"{secs / 60}:{secs % 60:00}" : "";
                FightTimeLine = secs > 0 ? "Fight time " + FightTime : "";
                if (IntroRoleSpaced.Length == 0) { IntroRoleSpaced = UiTheme.Spaced(RoleLine(d)); IntroEpithet = Epithet(boss); }
                if (on)
                {
                    Flash = 1;
                    if (PresentationSettings.HitStop) HitFeel.Stop(DefeatFreeze);   // everyone freezes on the killing frame (ignored if a hit just did)
                }
                DefeatMoment?.Invoke(boss);
            }
            else defeatAt = -1;
        }

        // A boss whose own code started no entrance clip plays the first existing one (no new animation; the host only)
        void Entrance(BossEncounter boss, bool on)
        {
            var p = bossPresenter; if (!p) return;
            if (p.Busy) { EntranceClip = p.CurrentClip; return; }
            if (!on || RunHud.LanClientView) return;
            foreach (var c in EntranceClips)
                if (p.HasClip(c)) { p.PlayOnce(c, Mathf.Max(0.5f, (1 - d.IntroProgress) * 2f * 0.9f)); EntranceClip = c; return; }
        }

        // the soft gold flash at the kill (under the HUD)
        static readonly Color FlashGold = new Color(1f, 0.93f, 0.74f);
        void OnGUI()
        {
            if (Flash <= 0.01f || Event.current.type != EventType.Repaint) return;
            GUI.depth = 4;
            var o = GUI.color; var c = FlashGold; c.a = 0.42f * Flash * Flash; GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = o;
        }

        // ---------------- card text (RunHud) ----------------
        // "Area boss · The Skyward Spire", "Final boss · The Throne of the Final Toll", "Practice · The Brass Foundry"
        public static string RoleLine(RunDirector d)
        {
            int enc = d.Encounter, area = Campaign.AreaOf(enc);
            string where = area == Campaign.FinalAreaIndex ? "The Throne of the Final Toll" : area >= 0 && area < Campaign.Areas.Length ? "The " + Campaign.Areas[area].Name : null;
            string role = d.Mode == RunMode.Practice ? "Practice" : area == Campaign.FinalAreaIndex ? "Final boss" : Campaign.IsAreaBoss(enc) ? "Area boss" : Campaign.StageLabel(d.EncounterOrder, d.ProgressSlot);
            if (where == null || (d.Mode == RunMode.Run && !Campaign.IsAreaBoss(enc) && area != Campaign.FinalAreaIndex)) return role;
            return role + " · " + where;
        }
        // the title under the name: the boss's own epithet, or the Bellkeeper's from gpt/04 ("Sovereign of the Shattered Hour")
        public static string Epithet(BossEncounter b)
        {
            string full = b ? b.DisplayName : ""; int comma = full.IndexOf(',');
            if (comma > 0) return full.Substring(comma + 1).Trim();
            return b is BellkeeperEncounter ? "Sovereign of the Shattered Hour" : null;
        }
    }
}
