using System;
using System.Collections.Generic;
using System.Linq;
using LastBell.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // New-player tutorial (docs/tutorial.md): a guided fight against the Tempo Tyrant on Metronome Court.
    // Launch: Boss Rush > Tutorial card, or LastBell.exe -lastbellTutorial [-lastbellClass Arcanist].
    //
    // The director rides on the boss (RunDirector.Bosses "Tutorial (Tempo Tyrant)" spawns a normal TempoTyrantEncounter
    // and attaches this). It runs a tick script from BossEncounter.BeforeStep: each step shows instruction text, then
    // either waits for the player to do something (move, dash, cast) or has the boss perform ONE mechanic and checks
    // whether the player handled it, retrying up to MaxTries. Between steps the boss is halted (BossEncounter.Halt).
    //   - The boss's own attacks are his real script, one block at a time (ForcedNext + BeginCombat, halted at the
    //     block boundary), so they look and hit exactly as in the fight.
    //   - The shared mechanics other bosses use (soak towers, spreads, burns, per-player marks, tethers, a following
    //     circle, low bars) are plain Hazards spawned here through the HazardSystem: no new damage code.
    //   - Three bot allies (AllyBot, wrapped by TutorialBot) fill the party for the soaks; the director gives them jobs.
    //   - HpFloor keeps the boss alive until the last step tells the player to finish him.
    //   - Nobody can be downed: a mercy heal tops players up below 40 HP and revives anyone who goes down (counted).
    public sealed partial class TutorialDirector : MonoBehaviour
    {
        public static TutorialDirector Current { get; private set; }
        public const int MaxTries = 3;
        public const float EarlyFloor = 0.25f;   // above Broken Cadence (20%) until the finale steps

        TempoTyrantEncounter boss; CombatWorld W; RunDirector d;
        IEnumerator<int> script; int wait;
        bool continuePressed;

        // ---- what the HUD and the autotest read ----
        public string Section { get; private set; } = "";
        public string Title { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string Status { get => status; private set { status = value; StatusBad = false; } }   // progress / result line under the body
        string status = "";
        public bool StatusBad { get; private set; }            // the status reports a miss (drawn in Danger, else muted gold)
        public bool WaitingForContinue { get; private set; }
        public int StepIndex { get; private set; }
        public string StepId { get; private set; } = "";
        public int StepCount => Steps.Count(Applies);   // the steps this class gets
        public int StepNumber { get; private set; }     // 1-based among them
        public bool Finished { get; private set; }            // the boss is defeated after the final instruction
        public bool FinaleOpen { get; private set; }          // the floor is gone: the boss can die
        public int MercyHeals { get; private set; }
        public int MercyRevives { get; private set; }
        public float MinHpFractionBeforeFinale { get; private set; } = 1f;
        public Vector3? PlayerGoal { get; private set; }      // gold ring: where the player is asked to go
        public float GoalRadius { get; private set; } = 1.2f;
        public string GoalLabel { get; private set; } = "";
        public readonly Dictionary<PlayerController, Vector3> Jobs = new Dictionary<PlayerController, Vector3>();   // bot allies
        public readonly Dictionary<PlayerController, string> Labels = new Dictionary<PlayerController, string>();   // over-head tags (marks)
        public readonly List<(Vector3 at, string text)> FloorLabels = new List<(Vector3, string)>();                // numerals on towers
        public sealed class Record { public string Id, Title; public int Tries; public bool Passed; public string Note = ""; }
        public readonly List<Record> Records = new List<Record>();
        public int ShotSerial { get; private set; }            // the autotest captures a screenshot when this changes
        public string ShotName { get; private set; } = "";
        public PlayerController Player => d ? d.Player : null;
        public TempoTyrantEncounter Boss => boss;
        public CombatWorld World => W;

        public static BossEncounter Attach(TempoTyrantEncounter b)
        {
            var t = b.gameObject.AddComponent<TutorialDirector>();
            t.Bind(b);
            return b;
        }

        void Bind(TempoTyrantEncounter b)
        {
            Current = this; boss = b; d = RunDirector.Instance; W = d ? d.World : null;
            boss.HpFloor = Mathf.CeilToInt(boss.MaxHp * EarlyFloor);
            boss.BeforeStep += _ => Tick();
            boss.Defeated += _ => { Finished = true; Clear(); TutorialCompletePanel.Show(d, Records); };
            EnsureAllies();
        }

        void OnDestroy() { if (Current == this) Current = null; ClearMarkers(); }

        // Three bot allies for the soaks, whatever the menu's Allies row says. They join the director's Allies list here, while
        // the boss spawns inside BeginIntro, so the intro places and readies them like menu allies.
        void EnsureAllies()
        {
            if (!d || W == null) return;
            string[] classes = { "Tollbreaker", "Chimesinger", "Arcanist" };
            for (int i = d.Allies.Count; i < 3; i++)
            {
                var a = PlayerController.Spawn(W, classes[i], new Vector3(0, 0, 6.5f), 180);
                a.Input = new AllyBot(i, 3, BotMode.Perfect); a.AbilitiesEnabled = false; a.Presenter.GetUp();
                d.Allies.Add(a);
            }
            for (int i = 0; i < d.Allies.Count; i++)
            {
                var a = d.Allies[i];
                var inner = a.Input as AllyBot ?? new AllyBot(i, d.Allies.Count, BotMode.Perfect);
                a.Input = new TutorialBot(this, inner);
            }
            W.PlayerHit += OnPlayerHit;
        }

        public IReadOnlyList<PlayerController> Allies => d ? (IReadOnlyList<PlayerController>)d.Allies : Array.Empty<PlayerController>();

        public void Continue() => continuePressed = true;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && WaitingForContinue && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) continuePressed = true;
            // gamepad A as well, so the "Press A to continue" hint (InputHints: last device used) is true; gameplay reads no pad
            var pad = Gamepad.current;
            if (pad != null && WaitingForContinue && pad.buttonSouth.wasPressedThisFrame) continuePressed = true;
            UpdateMarkers();
        }

        // ---------------- tick ----------------
        void Tick()
        {
            if (W == null || Finished) return;
            if (script == null) { boss.Halt(); script = Script().GetEnumerator(); }
            Mercy();
            if (!FinaleOpen) MinHpFractionBeforeFinale = Mathf.Min(MinHpFractionBeforeFinale, boss.Hp / (float)boss.MaxHp);
            if (wait > 0) { wait--; return; }
            if (!script.MoveNext()) { script = Idle().GetEnumerator(); return; }
            wait = Mathf.Max(1, script.Current) - 1;
        }
        static IEnumerable<int> Idle() { while (true) yield return 60; }

        void Mercy()
        {
            foreach (var p in W.Players)
            {
                if (p.Down) { p.DebugRevive(); MercyRevives++; continue; }
                if (p.Hp < 40 && !(p == Player && StepId == "potion")) { p.DebugSetHp(PlayerController.MaxHp); MercyHeals++; }
            }
        }

        // ---------------- hits on the player during a step ----------------
        public readonly List<(string source, int dmg)> StepHits = new List<(string, int)>();
        public readonly List<(PlayerController who, string source, int dmg)> PartyHits = new List<(PlayerController, string, int)>();
        void OnPlayerHit(PlayerController p, Hazard h, int dmg)
        {
            if (p == Player) StepHits.Add((h.Source, dmg));
            PartyHits.Add((p, h.Source, dmg));
        }

        // ---------------- text ----------------
        void Show(string section, string title, string body, string status)
        {
            Section = section; Title = title; Body = body; Status = status;
        }
        void Snap(string name) { ShotName = name; ShotSerial++; }

        // ---------------- floor markers ----------------
        BellkeeperFx.Telegraph goalTele; Vector3 goalPlaced; float goalPlacedR;
        static readonly Color GoalGold = new Color(1.6f, 1.25f, 0.45f), JobBlue = new Color(0.45f, 0.75f, 1.6f);
        readonly List<BellkeeperFx.Telegraph> jobTeles = new List<BellkeeperFx.Telegraph>();

        void SetGoal(Vector3? at, float radius = 1.2f, string label = "")
        {
            PlayerGoal = at.HasValue ? Sim.Flat(at.Value) : (Vector3?)null; GoalRadius = radius; GoalLabel = label;
        }
        public bool AtGoal() => PlayerGoal.HasValue && Player && Sim.Flat(Player.transform.position - PlayerGoal.Value).magnitude <= GoalRadius + 0.1f;

        void UpdateMarkers()
        {
            if (W == null) return;
            if (PlayerGoal.HasValue)
            {
                if (goalTele == null || goalPlaced != PlayerGoal.Value || goalPlacedR != GoalRadius)
                {
                    goalTele?.Dismiss();
                    goalTele = W.BossFx.Ring(Mathf.Max(0.1f, GoalRadius - 0.18f), GoalRadius); goalTele.Friendly(GoalGold); goalTele.Lock();
                    goalTele.Place(PlayerGoal.Value, 0); goalPlaced = PlayerGoal.Value; goalPlacedR = GoalRadius;
                }
            }
            else if (goalTele != null) { goalTele.Dismiss(); goalTele = null; }
        }
        void ClearMarkers()
        {
            goalTele?.Dismiss(); goalTele = null; PlayerGoal = null;
            foreach (var t in jobTeles) t.Dismiss(); jobTeles.Clear();
        }
        void Clear()
        {
            SetGoal(null); Jobs.Clear(); Labels.Clear(); FloorLabels.Clear();
            foreach (var t in jobTeles) t.Dismiss(); jobTeles.Clear();
            foreach (var p in W.Players) p.Marks.Clear();
            HideTether();
        }
        void JobRing(Vector3 at, float r)
        {
            var t = W.BossFx.Ring(Mathf.Max(0.1f, r - 0.12f), r); t.Friendly(JobBlue); t.Lock(); t.Place(Sim.Flat(at), 0); jobTeles.Add(t);
        }

        // ---------------- HUD ----------------
        // The instruction card in the main-menu language (docs/ui/ui-kit.md): a UiImgui.InfoStrip (ink strip, 3 px Gold left
        // bar, no box), a spaced Gold eyebrow with the step count, the Ivory title and the 96x2 Gold rule, then the body in the
        // theme's body style. Emphasis (<b> in the step text) is drawn in GoldBright rather than fake bold. Key names in the
        // step text come from InputHints (the gameplay bindings); the continue hint follows the last device used.
        internal const float CardX = 24, CardY = 150, CardW = 660, PadL = UiTheme.G3, PadR = 120, PadY = UiTheme.G2, CardFade = 200;
        internal const int TitleSize = 28;   // the main menu's primary-row size: a card title, smaller than a screen title (52)
        // Read mid-fight, so the card body is two steps above the ramp's SizeBody (17 at 1080p: ~13 px at 900p was too small).
        internal const int BodySize = UiTheme.SizeBody + 2;
        internal static readonly Color CardInk = new Color(UiTheme.InkDeep.r, UiTheme.InkDeep.g, UiTheme.InkDeep.b, 0.78f);
        // The card's backing: an InfoStrip (ink + 3 px Gold left bar) whose last CardFade units fade out like the main menu's
        // side gradient, so it reads as an ink strip over the arena rather than a box. vh: virtual height.
        internal static void CardBack(float vh)
        {
            UiImgui.InfoStrip(new Rect(CardX, CardY, CardW - CardFade, vh), CardInk);
            UiImgui.SideGradient(new Rect(CardX + CardW - CardFade, CardY, CardFade, vh), false, CardInk.a);
        }
        Rect cardScreen;   // where the card was drawn this pass (world tags keep out from under it)
        GUIStyle sTitle, sBody, sStatus, sKey, sCount, sTag, sTagLarge; int styledFor = -1;
        readonly GUIContent measure = new GUIContent();
        string bodySource, bodyShown, eyebrowFor, eyebrow, countShown = ""; int countFor = -1, countOf = -1;
        static string emphOpen;
        void Styles()
        {
            if (styledFor == Screen.height) return;
            styledFor = Screen.height;
            sTitle = UiTheme.Style(TitleSize, true, TextAnchor.UpperLeft, UiTheme.Ivory, true);
            sBody = UiTheme.Style(BodySize, false, TextAnchor.UpperLeft, UiTheme.IvoryBody, true); sBody.richText = true;
            sStatus = UiTheme.Style(BodySize, true, TextAnchor.UpperLeft, UiTheme.GoldBright, true);
            sKey = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleLeft, UiTheme.GoldLabel);
            sCount = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.MiddleRight, UiTheme.IvoryMuted);
            sTag = UiTheme.Style(EncounterCallouts.TagSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory);   // as the encounter callouts
            sTagLarge = UiTheme.Style(EncounterCallouts.TagLargeSize, true, TextAnchor.MiddleCenter, UiTheme.Ivory);   // numerals on the floor
        }
        // the step text's <b>emphasis</b> as GoldBright (a rich-text colour: UiTheme.Label tints it with the body colour and
        // draws the shadow pass black), cached per body string
        string ShownBody()
        {
            if (ReferenceEquals(Body, bodySource)) return bodyShown;
            emphOpen ??= "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.GoldBright) + ">";
            bodySource = Body; bodyShown = Body.Replace("<b>", emphOpen).Replace("</b>", "</color>");
            return bodyShown;
        }
        float Height(GUIStyle st, string text, float width) { measure.text = text; return st.CalcHeight(measure, width); }

        void OnGUI()
        {
            if (Finished || W == null || !d || (d.State != RunState.Combat && d.State != RunState.Cleared && d.State != RunState.Results) || d.Paused) return;
            GUI.depth = -20;   // above RunHud (0) and encounter HUDs
            Styles();
            // the instruction card: left side, below the boss bar, clear of the floor shapes in the middle (laid out in screen px)
            if (!ReferenceEquals(eyebrowFor, Section)) { eyebrowFor = Section; eyebrow = UiTheme.Spaced("Tutorial · " + Section); }
            if (countFor != StepNumber || countOf != StepCount) { countFor = StepNumber; countOf = StepCount; countShown = StepNumber + " / " + StepCount; }
            string body = ShownBody(), cont = WaitingForContinue ? ContinueHint() : null;
            float x = UiTheme.V(CardX + 3 + PadL), inner = UiTheme.V(CardW - 3 - PadL - PadR), y0 = UiTheme.V(CardY);
            float th = Height(sTitle, Title, inner), bh = Height(sBody, body, inner), sh = Status.Length > 0 ? Height(sStatus, Status, inner) : 0;
            // eyebrow 20, gap 8, title, gap 8, rule 2, gap 16, body, [gap 20, status], [gap 14, continue 22]
            float h = UiTheme.V(PadY + 20 + 8) + th + UiTheme.V(8 + UiTheme.RuleHeight + 16) + bh
                + (sh > 0 ? UiTheme.V(20) + sh : 0) + (cont != null ? UiTheme.V(14 + 22) : 0) + UiTheme.V(PadY);
            cardScreen = new Rect(UiTheme.V(CardX), y0, UiTheme.V(CardW), h);
            // over-head tags and floor labels (world to screen), under the card
            var cam = Camera.main;
            if (cam && d.State == RunState.Combat)
            {
                foreach (var kv in Labels) if (kv.Key && !kv.Key.Down) WorldTag(cam, kv.Key.transform.position + Vector3.up * 3.0f, kv.Value, sTag);
                foreach (var (at, text) in FloorLabels) WorldTag(cam, at + Vector3.up * 0.4f, text, sTagLarge);
                if (PlayerGoal.HasValue && GoalLabel.Length > 0) WorldTag(cam, PlayerGoal.Value + Vector3.up * 0.4f, GoalLabel, sTag);
            }
            CardBack(h / UiTheme.Scale);
            float cy = y0 + UiTheme.V(PadY);
            UiTheme.Label(new Rect(x, cy, inner, UiTheme.V(20)), eyebrow, UiImgui.Styles.Eyebrow);
            UiTheme.Label(new Rect(x, cy, inner, UiTheme.V(20)), countShown, sCount);
            cy += UiTheme.V(20 + 8);
            UiTheme.Label(new Rect(x, cy, inner, th), Title, sTitle); cy += th + UiTheme.V(8);
            UiTheme.Fill(new Rect(x, cy, UiTheme.V(UiTheme.RuleWidth), Mathf.Max(1, UiTheme.V(UiTheme.RuleHeight))), UiTheme.Gold); cy += UiTheme.V(UiTheme.RuleHeight + 16);
            UiTheme.Label(new Rect(x, cy, inner, bh), body, sBody); cy += bh;
            if (sh > 0) { cy += UiTheme.V(20); UiTheme.Label(new Rect(x, cy, inner, sh), Status, sStatus, StatusBad ? UiTheme.Danger : UiTheme.GoldLabel); cy += sh; }
            if (cont != null) { cy += UiTheme.V(14); UiTheme.Label(new Rect(x, cy, inner, UiTheme.V(22)), cont, sKey); }
        }
        // "Press Enter to continue" / "Press A to continue", for the last device used (Update accepts both)
        static string ContinueHint()
        {
            var key = InputHints.Key(HintAction.Confirm);
            return key == "A" ? "Press A to continue" : key == "Enter" ? "Press Enter to continue" : "Press " + key + " to continue";
        }
        // a tag over a player or a floor spot: a small info strip (card ink, 2 px Gold left tick) in the encounter callouts' tag
        // style; kept 16 px inside the screen, and hidden where the card covers it
        void WorldTag(Camera cam, Vector3 world, string text, GUIStyle st)
        {
            var sp = cam.WorldToScreenPoint(world); if (sp.z <= 0) return;
            measure.text = text; var size = st.CalcSize(measure);
            float m = UiTheme.V(16), w = size.x + UiTheme.V(20), hh = EncounterCallouts.TagHeight(st);
            var r = new Rect(Mathf.Clamp(sp.x - w / 2, m, Screen.width - m - w), Mathf.Clamp(Screen.height - sp.y - hh / 2, m, Screen.height - m - hh), w, hh);
            if (r.Overlaps(cardScreen)) return;
            UiTheme.Fill(r, CardInk);
            UiTheme.Fill(new Rect(r.x, r.y, Mathf.Max(1, UiTheme.V(2)), r.height), UiTheme.Gold);
            UiTheme.Label(r, text, st);
        }
    }
}
