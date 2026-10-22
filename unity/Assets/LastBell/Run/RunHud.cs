using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Functional IMGUI HUD and menus for the run loop (stand-in for final UI art). Everything shown is read from the
    // live gameplay state each frame; nothing here changes rules. Hazard names are spelled out while they warn, so the
    // fight reads with audio off.
    public sealed class RunHud : MonoBehaviour
    {
        struct FloatText { public Vector3 pos; public string text; public Color color; public float age; }
        static readonly List<FloatText> floats = new List<FloatText>();
        public static void Float(Vector3 world, string text, Color c) { if (floats.Count < 80) floats.Add(new FloatText { pos = world, text = text, color = c }); }
        public static void ClearTransient() { floats.Clear(); }

        RunDirector d; GUIStyle title, big, mid, small, tiny, num, box, btn, mapBtn, slot; Texture2D white;
        static int menuClass;   // static: survives the scene load when the menu switches map
        public static bool ShowMeter;   // Tab

        static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            ["Shattered Chime"] = "move: the marker follows you, then locks",
            ["Bell Sweep"] = "leave the cone", ["Bell Sweep (Final Toll)"] = "leave the cone (heavy)", ["Hollow Toll"] = "inside the inner circle or outside the ring",
            ["Meridian Cut"] = "it follows you, then locks: DASH out (or stay beyond the tip)", ["Zenith Pins"] = "it follows you, then locks: DASH off your circle",
            ["Processional Thrust"] = "step sideways before the blade reaches you", ["Crown Reversal"] = "leave his back: go to the flank",
            ["Meridian Alignment"] = "ring I: stand close inside it or far outside", ["Meridian Alignment II"] = "ring II: hug him or get outside it",
            [BellkeeperEncounter.March] = "step sideways out of the lane",
            [BellkeeperEncounter.Echo] = "it repeats where it struck before, not where the boss stands",
        };

        void Awake() { d = GetComponent<RunDirector>(); }

        void Update()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i]; f.age += Time.deltaTime; f.pos += Vector3.up * Time.deltaTime * 1.2f; floats[i] = f;
                if (f.age > 0.9f) floats.RemoveAt(i);
            }
            var kb = Keyboard.current; if (kb == null || d == null) return;
            if (kb.tabKey.wasPressedThisFrame) ShowMeter = !ShowMeter;
            if (d.State == RunState.Results)
            {
                if (kb.rKey.wasPressedThisFrame) d.RequestRetry();
                else if (kb.mKey.wasPressedThisFrame) d.ReturnToMenu();
            }
            if (d.State == RunState.Menu && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) d.StartRun(RunDirector.Classes[menuClass]);
            if (d.State == RunState.Menu && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
            {
                int n = RunMaps.All.Length, step = kb.rightBracketKey.wasPressedThisFrame ? 1 : n - 1;
                d.SelectMap((Mathf.Max(0, RunMaps.Current) + step) % n);
            }
            if (d.State == RunState.Menu && kb.dKey.wasPressedThisFrame)
                d.SelectDifficulty((Difficulty)(((int)RunDifficulty.Selected + 1) % RunDifficulty.Names.Length));
            if (d.State == RunState.Menu && kb.aKey.wasPressedThisFrame)
                d.SelectAllies((RunParty.Selected + 1) % (RunParty.MaxAllies + 1));
        }

        void Styles()
        {
            if (title != null) return;
            white = Texture2D.whiteTexture;
            title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(1f, 0.86f, 0.55f);
            big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; big.normal.textColor = Color.white;
            mid = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true }; mid.normal.textColor = new Color(0.92f, 0.92f, 0.95f);
            small = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold }; small.normal.textColor = new Color(0.95f, 0.95f, 0.97f);
            tiny = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true }; tiny.normal.textColor = new Color(0.85f, 0.87f, 0.92f);
            num = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box);
            btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 36 };
            mapBtn = new GUIStyle(btn) { fontSize = 13 };
            slot = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperCenter, wordWrap = true, fontStyle = FontStyle.Bold }; slot.normal.textColor = Color.white;
        }

        void Rect(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
        void Bar(Rect r, float frac, Color fill)
        {
            Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.65f));
            Rect(r, new Color(0.1f, 0.12f, 0.2f, 0.9f));
            Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), fill);
        }
        Rect Panel(float w, float h, float y = -1)
        {
            var r = new Rect(Screen.width / 2f - w / 2, y < 0 ? Screen.height / 2f - h / 2 : y, w, h);
            Rect(r, new Color(0.04f, 0.05f, 0.1f, 0.86f));
            Rect(new Rect(r.x, r.y, r.width, 3), new Color(0.95f, 0.75f, 0.35f, 0.9f));
            return r;
        }

        void OnGUI()
        {
            if (d == null) return;
            Styles();
            float w = Screen.width, h = Screen.height;
            switch (d.State)
            {
                case RunState.Menu: Menu(); return;
                case RunState.Results: Results(); break;
            }
            if (d.World == null) return;
            Track();
            Boss();
            PlayerPanel();
            AllyPanel();
            Floats();
            HazardLabels();
            switch (d.State)
            {
                case RunState.Ready:
                {
                    var r = Panel(600, 178, h - 376);   // just above the player panel (h - 192)
                    GUI.Label(new Rect(r.x, r.y + 10, r.width, 34), d.Mode == RunMode.Practice ? "PRACTICE — " + RunDirector.Bosses[d.Encounter].Name : "PREPARATION", big);
                    GUI.Label(new Rect(r.x + 20, r.y + 46, r.width - 40, 70), RunDifficulty.Name(d.Difficulty) + ". " + (d.Mode == RunMode.Practice ? "Practice never advances the run." : RunDirector.RunOrder.Length == 1 ? "One fight: the Bellkeeper." : $"{RunDirector.RunOrder.Length} bosses in a row. Health refills before each boss; potions carry over until the final boss refills them."), mid);
                    if (GUI.Button(new Rect(r.x + r.width / 2 - 110, r.y + 126, 220, 36), "READY  (Enter)", btn)) d.RequestReady();
                    break;
                }
                case RunState.Intro:
                    if (d.Boss != null)
                    {
                        var r = Panel(640, 116, h * 0.18f);
                        string head = d.Mode == RunMode.Practice ? "Practice" : d.ProgressSlot < RunDirector.RunOrder.Length - 1 ? $"Boss {d.ProgressSlot + 1} of {RunDirector.RunOrder.Length}" : "Final boss";
                        GUI.Label(new Rect(r.x, r.y + 8, r.width, 26), head, mid);
                        GUI.Label(new Rect(r.x, r.y + 34, r.width, 36), d.Boss.DisplayName.ToUpper(), big);
                        GUI.Label(new Rect(r.x, r.y + 72, r.width, 24), d.Boss.Lesson + (d.Boss.Placeholder ? "   ·   [stand-in boss]" : ""), mid);
                        GUI.Label(new Rect(r.x, r.y + 94, r.width, 20), $"Fight begins in {Mathf.Max(0, (120 - d.StateTicks) / 60f):0.0}s", tiny);
                    }
                    break;
                case RunState.Cleared:
                {
                    var r = Panel(560, 84, h * 0.2f);
                    GUI.Label(new Rect(r.x, r.y + 8, r.width, 36), (d.Boss ? d.Boss.DisplayName : "Boss") + " falls silent", big);
                    string next = d.Mode == RunMode.Practice ? "Practice complete" : d.ProgressSlot == RunDirector.RunOrder.Length - 1 ? "The last bell has rung" : "Next: " + RunDirector.Bosses[RunDirector.RunOrder[d.ProgressSlot + 1]].Name;
                    GUI.Label(new Rect(r.x, r.y + 48, r.width, 26), "Combat over · " + next, mid);
                    break;
                }
                case RunState.Downed:
                {
                    var r = Panel(560, 80, h * 0.25f);
                    GUI.Label(new Rect(r.x, r.y + 8, r.width, 36), "DOWN", big);
                    GUI.Label(new Rect(r.x, r.y + 46, r.width, 26), "Struck by " + (d.DefeatCause ?? "?"), mid);
                    break;
                }
            }
            if (d.Paused) Pause();
            GUI.Label(new Rect(w - 330, h - 22, 320, 20), "dev: F9 defeat boss · F8 next phase · Esc pause", tiny);
            GUI.Label(new Rect(14, h - 22, 420, 20), "map: " + RunMaps.CurrentName + "   ·   difficulty: " + RunDifficulty.Name(d.Difficulty), tiny);
        }

        void Menu()
        {
            float w = Screen.width, h = Screen.height;
            var r = Panel(720, 680);
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 54), "LAST BELL", title);
            GUI.Label(new Rect(r.x, r.y + 72, r.width, 22), "Boss-rush test build · pick a class, a map and a difficulty", mid);
            GUI.Label(new Rect(r.x + 30, r.y + 110, 200, 22), "Class", small);
            for (int i = 0; i < RunDirector.Classes.Length; i++)
            {
                var br = new Rect(r.x + 30 + i * 222, r.y + 134, 214, 36);
                bool on = menuClass == i;
                if (GUI.Toggle(br, on, (on ? "> " : "") + RunDirector.Classes[i], btn)) menuClass = i;
            }
            string desc = menuClass == 0
                ? "Sword and shield. 1 Steel Rhythm (3-hit combo; hit 3 grants Opening) · 2 Sundering Blow · 3 Last Word · 4 hold Brace (frontal 60% reduction)."
                : menuClass == 1
                ? "Bell staff. 1 Resonant Note (+Harmony) · 2 Bellstrike (2 charges, rings a bell) · 3 Harmonic Refrain (adds echoes) · 4 Crescendo (detonate) · 6 Mending Chorus (instant heal)."
                : "Time magic. 1 Clockbolt (+Tick, max 3) · 2 Time Seal (2 charges, 6 s, ripe last 2 s) · 3 Arc Lance (3 Ticks: inscribes a seal) · 4 Convergence (detonate seals) · 6 Temporal Anchor (recast to return + shield).";
            GUI.Label(new Rect(r.x + 30, r.y + 176, r.width - 60, 40), desc, tiny);
            int cur = RunMaps.Current;
            GUI.Label(new Rect(r.x + 30, r.y + 216, r.width - 60, 22), "Map  ·  " + (cur >= 0 ? RunMaps.All[cur].Name + " — " + RunMaps.All[cur].Note : RunMaps.CurrentName) + "   ([ ] to switch)", small);
            for (int i = 0; i < RunMaps.All.Length; i++)
            {
                var mr = new Rect(r.x + 30 + i * 133, r.y + 240, 127, 36);
                bool on = cur == i;
                if (GUI.Toggle(mr, on, (on ? "> " : "") + RunMaps.All[i].Name, mapBtn) && !on) d.SelectMap(i);
            }
            var level = RunDifficulty.Selected;
            GUI.Label(new Rect(r.x + 30, r.y + 286, 330, 22), "Difficulty  ·  " + RunDifficulty.Name(level) + "   (D)", small);
            for (int i = 0; i < RunDifficulty.Names.Length; i++)
            {
                var dr = new Rect(r.x + 30 + i * 110, r.y + 310, 104, 36);
                bool on = (int)level == i;
                if (GUI.Toggle(dr, on, (on ? "> " : "") + RunDifficulty.Names[i], btn) && !on) d.SelectDifficulty((Difficulty)i);
            }
            // bot allies (RunParty): 0 = solo; classes spread across all three
            int allies = RunParty.Selected;
            GUI.Label(new Rect(r.x + 370, r.y + 286, 320, 22), "Allies (bots)  ·  " + (allies == 0 ? "solo" : allies + "   party of " + (allies + 1)) + "   (A)", small);
            for (int i = 0; i <= RunParty.MaxAllies; i++)
            {
                var ar = new Rect(r.x + 370 + i * 40, r.y + 310, 36, 36);
                bool on = allies == i;
                if (GUI.Toggle(ar, on, i.ToString(), btn) && !on) d.SelectAllies(i);
            }
            if (GUI.Button(new Rect(r.x + r.width / 2 - 140, r.y + 364, 280, 40), "START RUN  (Enter)", btn)) d.StartRun(RunDirector.Classes[menuClass]);
            GUI.Label(new Rect(r.x + 30, r.y + 418, 300, 22), "Practice one boss", small);
            for (int i = 0; i < RunDirector.Bosses.Length; i++)
                if (GUI.Button(new Rect(r.x + 30 + i * (r.width - 60) / RunDirector.Bosses.Length, r.y + 442, (r.width - 60) / RunDirector.Bosses.Length - 6, 36), RunDirector.Bosses[i].Name, btn)) d.StartPractice(RunDirector.Classes[menuClass], i);   // a boss with its own arena loads it first
            GUI.Label(new Rect(r.x + 30, r.y + 492, r.width - 60, 120),
                "Controls: WASD move · mouse aims · 1 / LMB basic · 2 3 4 class actions · 6 Mending Chorus / Temporal Anchor · 5 / RMB dash · F potion · Space jump · Shift sprint · Esc pause\n" +
                "Movement stamina: sprint, jump and dash share one bar. No invincibility frames (dodging does not make you immune). F drinks a potion (+40 HP).\nHealth refills before each boss; potions carry over between bosses and refill before the final boss. Retry restarts at the first boss.\n" +
                "Only real boss encounters are offered (the stand-in minibosses were removed); Sylvara is practice only, in Verdant Chimes. Arcanist kit numbers are prototype tuning. Dev keys in combat: F9 defeat boss, F8 next phase.", tiny);
            if (GUI.Button(new Rect(r.x + r.width - 130, r.y + r.height - 50, 100, 36), "Quit", btn)) Application.Quit();
        }

        void Results()
        {
            var r = Panel(560, 300);
            GUI.Label(new Rect(r.x, r.y + 16, r.width, 50), d.Victory ? (d.Mode == RunMode.Practice ? "PRACTICE CLEARED" : "VICTORY") : "DEFEAT", title);
            var w = d.World;
            string lines = d.Victory ? (d.Mode == RunMode.Practice ? "Practice result only: the run is unchanged." : "The Bellkeeper is silenced. The run is cleared.")
                                     : $"Downed by {d.DefeatCause} during {(d.Boss ? d.Boss.DisplayName : RunDirector.Bosses[d.Encounter].Name)}.";
            lines += $"\n\nClass: {d.ClassName}   ·   {RunDifficulty.Name(d.Difficulty)}   ·   Time in combat: {d.RunTicks / 60f / 60f:0}:{d.RunTicks / 60f % 60:00}   ·   Encounters cleared: {d.EncountersCleared}";
            if (w != null) lines += $"\nDamage dealt {w.DamageDealt}   ·   Damage taken {w.DamageTaken}";
            GUI.Label(new Rect(r.x + 20, r.y + 76, r.width - 40, 120), lines, mid);
            if (w != null) Meter(r.x + r.width + 16, r.y, w.RunMeter, "DAMAGE · whole " + (d.Mode == RunMode.Practice ? "practice" : "run"), d.RunTicks / 60f);
            if (!d.Victory && w != null && w.TakenLog.Count > 0)
            {
                // PF-06 death recap: the last hits in order, the killing blow marked
                int from = Mathf.Max(0, w.TakenLog.Count - 6);
                float rx = r.x - 336, ry = r.y; Rect(new Rect(rx, ry, 320, 40 + 20 * (w.TakenLog.Count - from)), new Color(0.03f, 0.04f, 0.08f, 0.82f));
                GUI.Label(new Rect(rx + 8, ry + 4, 300, 20), "DEATH RECAP · last hits taken", small);
                float yy = ry + 26; int last = w.TakenLog[w.TakenLog.Count - 1].tick;
                for (int i = from; i < w.TakenLog.Count; i++)
                {
                    var e = w.TakenLog[i]; bool fatal = i == w.TakenLog.Count - 1;
                    GUI.Label(new Rect(rx + 8, yy, 310, 20), $"{(fatal ? "KILLING BLOW " : "")}{e.source}  -{e.damage}  (hp {e.hpAfter}, {(e.tick - last) / 60f:0.0}s)", tiny);
                    yy += 20;
                }
            }
            if (GUI.Button(new Rect(r.x + 40, r.y + 230, 220, 40), d.Mode == RunMode.Practice ? "RETRY PRACTICE  (R)" : "RETRY RUN  (R)", btn)) d.RequestRetry();
            if (GUI.Button(new Rect(r.x + r.width - 260, r.y + 230, 220, 40), "MAIN MENU  (M)", btn)) d.ReturnToMenu();
        }

        void Pause()
        {
            var r = Panel(420, 220);
            GUI.Label(new Rect(r.x, r.y + 14, r.width, 34), "PAUSED", big);
            if (GUI.Button(new Rect(r.x + 90, r.y + 60, 240, 36), "RESUME  (Esc)", btn)) d.SetPaused(false);
            if (GUI.Button(new Rect(r.x + 90, r.y + 106, 240, 36), d.Mode == RunMode.Practice ? "RESTART PRACTICE" : "RESTART RUN", btn)) d.RequestRetry();
            if (GUI.Button(new Rect(r.x + 90, r.y + 152, 240, 36), "MAIN MENU", btn)) d.ReturnToMenu();
        }

        void Track()
        {
            if (d.Mode == RunMode.Practice) { GUI.Label(new Rect(14, 12, 400, 22), "PRACTICE — does not advance the run", small); return; }
            float x = 14, y = 12;
            for (int i = 0; i < RunDirector.RunOrder.Length; i++)
            {
                int cur = d.ProgressSlot; bool done = i < cur || (i == cur && d.State == RunState.Cleared) || (d.State == RunState.Results && d.Victory);
                var c = done ? new Color(0.95f, 0.75f, 0.35f) : i == cur ? new Color(0.55f, 0.85f, 1f) : new Color(0.3f, 0.32f, 0.4f);
                Rect(new Rect(x, y, 112, 26), new Color(0, 0, 0, 0.55f));
                Rect(new Rect(x, y + 22, 112, 4), c);
                GUI.Label(new Rect(x + 6, y + 3, 110, 20), (done ? "+ " : i == cur ? "> " : "") + RunDirector.Bosses[RunDirector.RunOrder[i]].Name, tiny);
                x += 118;
            }
        }

        void Boss()
        {
            var b = d.Boss; if (b == null) return;
            float w = Screen.width, bw = Mathf.Min(720, w * 0.55f), x = w / 2 - bw / 2;
            GUI.Label(new Rect(x, 44, bw, 28), b.DisplayName.ToUpper() + (b.Placeholder ? "  (stand-in)" : ""), big);
            Bar(new Rect(x, 74, bw, 14), (float)b.Hp / b.MaxHp, new Color(0.92f, 0.41f, 0.36f));
            if (b is BellkeeperEncounter) { Rect(new Rect(x + bw * 0.7f, 72, 2, 18), Color.white); Rect(new Rect(x + bw * 0.35f, 72, 2, 18), Color.white); }
            if (b is AurelionEncounter) { Rect(new Rect(x + bw * 0.65f, 72, 2, 18), Color.white); Rect(new Rect(x + bw * 0.30f, 72, 2, 18), Color.white); }
            GUI.Label(new Rect(x, 90, 300, 20), b.PhaseName, small);
            GUI.Label(new Rect(x + bw - 140, 90, 140, 20), $"{b.Hp} / {b.MaxHp}", small);
            // what is coming, in words (audio-off readability)
            var hz = d.World.Hazards.Live; var seen = new HashSet<string>(); float yy = 112;
            foreach (var hzd in hz)
            {
                if (!seen.Add(hzd.Source)) continue;
                Hints.TryGetValue(hzd.Source, out var hint);
                GUI.Label(new Rect(x, yy, bw, 22), $"! {hzd.Source.ToUpper()} — {hint}", small); yy += 20;
            }
        }

        // Skill bar icons by Codex (#43 / PR #45): Resources/Run/Icons/<file>.png, 256 px. Slot order = the kit's slots,
        // then the class's dash, then the potion. A missing icon falls back to the ability name.
        static readonly Dictionary<string, string[]> SlotIconNames = new Dictionary<string, string[]>
        {
            ["Vanguard"] = new[] { "vanguard_steel_rhythm", "vanguard_sundering_blow", "vanguard_last_word", "vanguard_brace", "vanguard_dash" },
            ["Chimesinger"] = new[] { "chimesinger_resonant_note", "chimesinger_bellstrike", "chimesinger_harmonic_refrain", "chimesinger_crescendo", "chimesinger_mending_chorus", "chimesinger_harmonic_step" },
            ["Arcanist"] = new[] { "arcanist_clockbolt", "arcanist_time_seal", "arcanist_arc_lance", "arcanist_convergence", "arcanist_temporal_anchor", "arcanist_clockstep" },
        };
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        public static Texture2D Icon(string file)
        {
            if (!icons.TryGetValue(file, out var t)) icons[file] = t = Resources.Load<Texture2D>("Run/Icons/" + file);
            return t;
        }
        // slot 0..n-1 = kit slots, n = dash; returns null when the class or icon is unknown
        public static Texture2D SlotIcon(string className, int slot) =>
            SlotIconNames.TryGetValue(className, out var names) && slot < names.Length ? Icon(names[slot]) : null;

        // one bar tile: keybind on top, icon, state line at the bottom; the cooldown darkens the tile from the top and
        // the state line spells out seconds / charges, so readiness never relies on colour
        Rect Tile(float x, float y, string key, Texture2D icon, string name, string state, float cooldown01, bool dim)
        {
            const float W = 74, H = 88;
            var r = new Rect(x, y, W, H);
            Rect(r, new Color(0.06f, 0.08f, 0.15f, 0.95f));
            Rect(new Rect(r.x, r.y, r.width, 2), new Color(0.76f, 0.55f, 0.27f, 0.9f));
            var ir = new Rect(r.x + (W - 52) / 2, r.y + 18, 52, 52);
            var old = GUI.color;
            if (icon != null) { GUI.color = dim || cooldown01 > 0 ? new Color(0.78f, 0.78f, 0.82f, 0.9f) : Color.white; GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit, true); GUI.color = old; }
            else GUI.Label(new Rect(r.x, r.y + 22, W, 30), name, slot);
            if (cooldown01 > 0) Rect(new Rect(r.x, r.y + 17, W, (H - 33) * Mathf.Clamp01(cooldown01)), new Color(0, 0, 0, 0.4f));
            GUI.Label(new Rect(r.x, r.y + 3, W, 16), "[" + key + "]", slot);
            Rect(new Rect(r.x, r.y + H - 16, W, 16), new Color(0, 0, 0, 0.55f));
            GUI.Label(new Rect(r.x, r.y + H - 16, W, 16), state, slot);
            return r;
        }

        // bot allies (RunParty): class and health, left edge
        void AllyPanel()
        {
            if (d.Allies.Count == 0) return;
            float x = 14, y = Screen.height * 0.35f;
            Rect(new Rect(x - 4, y - 24, 196, 24 + d.Allies.Count * 30), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(x, y - 22, 190, 20), "ALLIES (bots)", tiny);
            for (int i = 0; i < d.Allies.Count; i++)
            {
                var a = d.Allies[i]; if (!a) continue;
                float yy = y + i * 30;
                GUI.Label(new Rect(x, yy, 190, 18), a.ClassName + (a.Down ? "   (down)" : "   " + a.Hp), tiny);
                Bar(new Rect(x, yy + 17, 186, 7), a.Hp / (float)PlayerController.MaxHp, a.Down ? new Color(0.4f, 0.4f, 0.45f) : new Color(0.45f, 0.85f, 0.55f));
            }
        }

        void PlayerPanel()
        {
            var p = d.Player; if (p == null) return;
            var k = p.Kit; int n = k.SlotNames.Length;
            const float tw = 74, gap = 6;
            float barW = (n + 2) * tw + (n + 1) * gap;
            float w = Screen.width, h = Screen.height, pw = Mathf.Max(520, barW), x = w / 2 - pw / 2, y = h - 184;
            Rect(new Rect(x - 10, y - 8, pw + 20, 184), new Color(0.03f, 0.04f, 0.08f, 0.6f));
            GUI.Label(new Rect(x, y - 4, 300, 20), $"{p.ClassName}   {p.Hp} / {PlayerController.MaxHp}", small);
            Bar(new Rect(x, y + 16, pw, 14), (float)p.Hp / PlayerController.MaxHp, new Color(0.96f, 0.79f, 0.41f));
            Bar(new Rect(x, y + 36, pw, 6), p.Stamina / PlayerController.MaxStamina, new Color(0.45f, 0.85f, 0.75f));
            GUI.Label(new Rect(x + pw - 160, y - 4, 160, 20), "stamina " + Mathf.RoundToInt(p.Stamina), small);
            float bx = w / 2 - barW / 2, by = y + 50; int hover = -1;
            for (int i = 0; i < n; i++)
            {
                var r = Tile(bx + i * (tw + gap), by, k.SlotKeys[i], SlotIcon(p.ClassName, i), k.SlotNames[i], k.SlotState(i), k.Cooldown01(i), false);
                if (r.Contains(Event.current.mousePosition)) hover = i;
            }
            bool dashReady = p.Stamina >= PlayerController.DashCost;
            var dr = Tile(bx + n * (tw + gap), by, "5 / RMB", SlotIcon(p.ClassName, n), p.ClassName == "Chimesinger" ? "Harmonic Step" : p.ClassName == "Arcanist" ? "Clockstep" : "Dash",
                          dashReady ? "ready" : "stamina", 0, !dashReady);
            var pr = Tile(bx + (n + 1) * (tw + gap), by, "F", Icon("potion"), "Potion",
                          new string('●', p.Potions) + new string('○', PlayerController.MaxPotions - p.Potions), 0, p.Potions == 0);
            string cs = k.ClassState();
            if (!string.IsNullOrEmpty(cs)) GUI.Label(new Rect(x - 130, y + 141, pw + 260, 20), cs, small);   // wide: Arcanist seals list two seals
            string hint = d.State == RunState.Combat ? "WASD move · mouse aim · Space jump · Shift sprint · Tab damage meter · hover a skill for details" : "Abilities are off outside combat";
            GUI.Label(new Rect(x, y + 161, pw, 18), hint, tiny);
            if (pr.Contains(Event.current.mousePosition)) Tooltip(x, y, $"POTION  [F]\n+{PlayerController.PotionHeal} HP, 1 s between drinks. {p.Potions}/{PlayerController.MaxPotions} left.\nPotions carry over between minibosses; health and potions refill before the final boss.");
            else if (dr.Contains(Event.current.mousePosition)) Tooltip(x, y, "DASH  [5 / RMB]\nShort burst (3 m in 0.2 s) in the move direction. Costs 30 stamina; sprint and jump share the bar.\nNo invincibility: get out of the shape before it resolves. Cancels basic attacks and unreleased windups.");
            else if (hover >= 0) Tooltip(x, y, k.Tooltip(hover));
            if (ShowMeter && d.World != null) Meter(Screen.width - 330, 150, d.World.EncounterMeter, "DAMAGE · this encounter", (d.World.Tick - d.World.SegmentStartTick) / 60f);
        }

        void Tooltip(float x, float y, string text)
        {
            var h = tiny.CalcHeight(new GUIContent(text), 500) + 12;
            var r = new Rect(x + 10, y - 16 - h, 500, h);
            Rect(r, new Color(0.02f, 0.03f, 0.07f, 0.95f)); Rect(new Rect(r.x, r.y, 3, r.height), new Color(0.95f, 0.75f, 0.35f));
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 16, r.height - 8), text, tiny);
        }

        // PF-07 first pass: per-ability damage, share, hits, largest hit and DPS over the segment. Not Details! parity.
        void Meter(float x, float y, Dictionary<string, CombatWorld.MeterRow> rows, string title, float seconds)
        {
            var list = new List<CombatWorld.MeterRow>(rows.Values); list.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            int total = 0; foreach (var r in list) total += r.Damage;
            float h = 48 + 20 * Mathf.Max(1, list.Count);
            Rect(new Rect(x, y, 316, h), new Color(0.03f, 0.04f, 0.08f, 0.82f));
            GUI.Label(new Rect(x + 8, y + 4, 300, 20), $"{title} · {total} · {(seconds > 0.5f ? total / seconds : 0):0} DPS", small);
            GUI.Label(new Rect(x + 8, y + 24, 300, 18), "ability                 dmg     %    hits  max", tiny);
            float yy = y + 42;
            foreach (var r in list)
            {
                float f = total > 0 ? (float)r.Damage / total : 0;
                Rect(new Rect(x + 8, yy + 2, 300 * f, 16), new Color(0.95f, 0.7f, 0.3f, 0.35f));
                GUI.Label(new Rect(x + 8, yy, 150, 20), r.Ability, tiny);
                GUI.Label(new Rect(x + 150, yy, 160, 20), $"{r.Damage,6}  {f * 100,4:0}%  {r.Hits,4}  {r.Largest,4}", tiny);
                yy += 20;
            }
        }

        void Floats()
        {
            var cam = Camera.main; if (!cam) return; float h = Screen.height;
            foreach (var f in floats)
            {
                var s = cam.WorldToScreenPoint(f.pos); if (s.z < 0) continue;
                var c = f.color; c.a = 1 - Mathf.Clamp01((f.age - 0.5f) / 0.4f); num.normal.textColor = c;
                GUI.Label(new Rect(s.x - 60, h - s.y - 14, 120, 28), f.text, num);
            }
        }

        void HazardLabels()
        {
            var cam = Camera.main; if (!cam) return; float h = Screen.height;
            foreach (var hz in d.World.Hazards.Live)
            {
                if (!hz.JumpClearable) continue;
                var mid = hz.Origin + Sim.Dir(hz.Yaw) * hz.Length * 0.5f;
                var s = cam.WorldToScreenPoint(mid); if (s.z < 0) continue;
                num.normal.textColor = new Color(0.6f, 1f, 1f);
                GUI.Label(new Rect(s.x - 60, h - s.y - 14, 120, 28), "JUMP", num);
            }
        }
    }
}
