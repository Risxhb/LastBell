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
        public string Status { get; private set; } = "";      // progress / result line under the body
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
        GUIStyle sTitle, sBody, sSection, sStatus, sTag, sKey; float styledFor = -1;
        void Styles()
        {
            float k = Screen.height / 1080f;
            if (Mathf.Approximately(styledFor, k)) return;
            styledFor = k;
            sSection = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * k), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.93f, 0.74f, 0.38f) } };
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(28 * k), fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(1f, 0.95f, 0.85f) } };
            sBody = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19 * k), wordWrap = true, richText = true, normal = { textColor = new Color(0.92f, 0.9f, 0.86f) } };
            sStatus = new GUIStyle(sBody) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.62f, 0.9f, 1f) } };
            sKey = new GUIStyle(sBody) { fontSize = Mathf.RoundToInt(16 * k), normal = { textColor = new Color(0.93f, 0.74f, 0.38f) } };
            sTag = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * k), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        static Texture2D panelTex;
        void OnGUI()
        {
            if (Finished || W == null || !d || (d.State != RunState.Combat && d.State != RunState.Cleared && d.State != RunState.Results) || d.Paused) return;
            GUI.depth = -20;   // above RunHud (0) and encounter HUDs
            Styles();
            if (!panelTex) { panelTex = new Texture2D(1, 1); panelTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.82f)); panelTex.Apply(); }
            float k = Screen.height / 1080f;
            var cam = Camera.main;
            // over-head tags and floor labels (world to screen)
            if (cam && d.State == RunState.Combat)
            {
                foreach (var kv in Labels) if (kv.Key && !kv.Key.Down) WorldTag(cam, kv.Key.transform.position + Vector3.up * 3.0f, kv.Value, k);
                foreach (var (at, text) in FloorLabels) WorldTag(cam, at + Vector3.up * 0.4f, text, k);
                if (PlayerGoal.HasValue && GoalLabel.Length > 0) WorldTag(cam, PlayerGoal.Value + Vector3.up * 0.4f, GoalLabel, k);
            }
            // the instruction panel: left side, below the boss bar, clear of the floor shapes in the middle
            float w = 560 * k, x = 24 * k, y = 150 * k, pad = 18 * k, inner = w - 2 * pad;
            float h = pad;
            h += sSection.CalcHeight(new GUIContent("X"), inner) + 4 * k;
            h += sTitle.CalcHeight(new GUIContent(Title), inner) + 6 * k;
            h += sBody.CalcHeight(new GUIContent(Body), inner) + 8 * k;
            if (Status.Length > 0) h += sStatus.CalcHeight(new GUIContent(Status), inner) + 6 * k;
            if (WaitingForContinue) h += sKey.CalcHeight(new GUIContent("X"), inner) + 4 * k;
            h += pad;
            GUI.DrawTexture(new Rect(x, y, w, h), panelTex);
            GUI.DrawTexture(new Rect(x, y, 4 * k, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.93f, 0.74f, 0.38f), 0, 0);
            float cy = y + pad;
            string head = Finished ? "TUTORIAL" : $"TUTORIAL · {Section.ToUpperInvariant()} · {StepNumber}/{StepCount}";
            GUI.Label(new Rect(x + pad, cy, inner, 30 * k), head, sSection); cy += sSection.CalcHeight(new GUIContent("X"), inner) + 4 * k;
            float th = sTitle.CalcHeight(new GUIContent(Title), inner); GUI.Label(new Rect(x + pad, cy, inner, th), Title, sTitle); cy += th + 6 * k;
            float bh = sBody.CalcHeight(new GUIContent(Body), inner); GUI.Label(new Rect(x + pad, cy, inner, bh), Body, sBody); cy += bh + 8 * k;
            if (Status.Length > 0) { float shh = sStatus.CalcHeight(new GUIContent(Status), inner); GUI.Label(new Rect(x + pad, cy, inner, shh), Status, sStatus); cy += shh + 6 * k; }
            if (WaitingForContinue) GUI.Label(new Rect(x + pad, cy, inner, 30 * k), "Press Enter to continue", sKey);
        }
        void WorldTag(Camera cam, Vector3 world, string text, float k)
        {
            var sp = cam.WorldToScreenPoint(world); if (sp.z <= 0) return;
            var size = sTag.CalcSize(new GUIContent(text));
            var r = new Rect(sp.x - size.x / 2 - 8 * k, Screen.height - sp.y - size.y / 2 - 3 * k, size.x + 16 * k, size.y + 6 * k);
            GUI.DrawTexture(r, panelTex);
            GUI.Label(r, text, sTag);
        }
    }
}
