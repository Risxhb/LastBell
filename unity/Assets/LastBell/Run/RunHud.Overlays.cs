using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // The run overlays in the main menu's language (UI sub-thread B, docs/plans/ui-uniformity-plan-20261110.md §3): Pause,
    // Results (victory, defeat with the death recap, the LAN client's view), the Ready card and the downed card. No boxes:
    // a dim and the centre band behind Pause and Results, an ink strip with a gold left bar for the small cards, the
    // centred header (eyebrow, Ivory title, 96x2 rule), text rows with the gold strip (UiImgui.Row) and full keyboard /
    // controller focus (ImguiFocus: arrows / dpad / stick, Enter / A, Esc / B). Hints come from InputHints.
    public sealed partial class RunHud
    {
        readonly ImguiFocus pauseFocus = new ImguiFocus(), resultsFocus = new ImguiFocus(), readyFocus = new ImguiFocus();
        int pauseSeen = -9, resultsSeen = -9, readySeen = -9;
        GUIStyle ovCardTitle, ovPrimaryInset, ovRecap, ovRecapR, ovRecapMuted, ovRecapHead, ovCause;
        static GUIStyle ovHint; static int ovHintHeight = -1;
        int ovStyleHeight = -1;
        static readonly HintLine PauseHints = new HintLine((HintAction.Confirm, "Select"), (HintAction.Back, "Resume"));
        static readonly HintLine SelectHint = new HintLine((HintAction.Confirm, "Select"));
        static readonly HintLine ReadyHints = new HintLine((HintAction.Ready, "Ready"), (HintAction.Pause, "Pause"));
        static readonly HintLine ResultHints = new HintLine((HintAction.Confirm, "Select"), (HintAction.Retry, "Retry"), (HintAction.MainMenu, "Main Menu"));

        void OverlayStyles()
        {
            if (ovStyleHeight == Screen.height && ovCardTitle != null) return;
            ovStyleHeight = Screen.height;
            ovCardTitle = UiTheme.Style(30, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            int inset = Mathf.RoundToInt(UiTheme.V(16));
            ovPrimaryInset = new GUIStyle(UiImgui.Styles.PrimaryLeft) { padding = new RectOffset(inset, inset, 0, 0) };
            ovRecap = UiTheme.Style(UiTheme.SizeNumeric, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            ovRecapR = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleRight, UiTheme.IvoryBody);
            ovRecapMuted = UiTheme.Style(UiTheme.SizeLabel, false, TextAnchor.MiddleLeft, UiTheme.IvoryMuted);
            ovRecapHead = UiTheme.Style(UiTheme.SizeLabel, true, TextAnchor.MiddleLeft, UiTheme.Danger);
            ovCause = UiTheme.Style(UiTheme.SizeHeading, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
        }
        // the footer hint line: 14 px IvoryMuted (the kit's Hint is 12 px Micro, too small over a bright floor)
        internal static GUIStyle HintStyle
        {
            get
            {
                if (ovHint == null || ovHintHeight != Screen.height) { ovHintHeight = Screen.height; ovHint = UiTheme.Style(14, false, TextAnchor.MiddleCenter, UiTheme.IvoryMuted); }
                return ovHint;
            }
        }

        // True on the first OnGUI pass of the first frame an overlay is drawn again (it wasn't drawn last frame): the screen
        // then puts focus on its main action and drops that frame's Confirm / Back, which opened it or belongs to the HUD.
        internal static bool FreshFrame(ref int seen)
        {
            int f = Time.frameCount; if (seen == f) return false;
            bool fresh = seen != f - 1; seen = f; return fresh;
        }

        // Keys outside the focus lists (Update): R / M on Results, and the gamepad's Start toggling Pause (the keyboard's Esc
        // toggles it in RunDirector). Enter / A and Esc / B go through ImguiFocus.
        void OverlayInput()
        {
            if (d == null) return;
            var kb = Keyboard.current;
            if (kb != null && !LanClientView && d.State == RunState.Results)   // a LAN client's Retry / Main Menu are the host's
            {
                if (kb.rKey.wasPressedThisFrame) d.RequestRetry();
                else if (kb.mKey.wasPressedThisFrame) d.ReturnToMenu();
            }
            // Start (bound nowhere else) resumes at any time, but opens Pause only in a fight's own states: never on the menu,
            // during a boss intro or over Results
            var pad = Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame)
            {
                if (d.Paused) d.SetPaused(false);
                else if (d.State != RunState.Menu && d.State != RunState.Intro && d.State != RunState.Results) d.SetPaused(true);
            }
        }

        // ---------------------------------------------------------------- shared pieces
        // the centred overlay column: header, rows, hint line, over the centre band
        static float ColumnX(float w) => UiTheme.VirtualWidth / 2 - w / 2;
        static void HintText(float y, string text) { if (!string.IsNullOrEmpty(text)) UiTheme.Label(UiTheme.R(0, y, UiTheme.VirtualWidth, 22), text, HintStyle); }
        // the centre overlays' dim (Pause, Results): the HUD underneath reads as background, not as a second layer
        void DeepDim() { Dim(); UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), A(UiTheme.Backdrop, 0.4f)); }
        // A small card (Ready, downed, the recap tooltip): ink that fades out over its right part (the main menu's side
        // gradient, mirrored) so it reads as a strip over the arena, not a box, with a 4 px Gold bar on the left.
        internal static void SoftStrip(Rect v, float fade = 220)
        {
            // the solid part ends exactly where the gradient starts (whole screen pixels, no overlap) and matches its
            // ~0.985 ink, so no seam shows between them
            float solid = Mathf.Max(0, v.width - fade * 0.57f);
            var r = UiTheme.R(v.x, v.y, solid, v.height); float x0 = Mathf.Round(r.x), x1 = Mathf.Round(r.xMax);
            UiTheme.Fill(new Rect(x0, r.y, x1 - x0, r.height), A(UiTheme.GradientInk, 0.9f * 0.985f));
            var g = UiTheme.R(0, v.y, fade, v.height); var o = GUI.color; GUI.color = new Color(1, 1, 1, 0.9f);
            GUI.DrawTextureWithTexCoords(new Rect(x1, g.y, g.width, g.height), UiTheme.SideGradient, new Rect(1, 0, -1, 1), true); GUI.color = o;
            UiTheme.Fill(UiTheme.R(v.x, v.y, 4, v.height), UiTheme.Gold);
        }
        string Ellipsize(GUIStyle st, string t, float maxW)
        {
            if (Wv(st, t) <= maxW) return t;
            while (t.Length > 1 && Wv(st, t + "…") > maxW) t = t.Substring(0, t.Length - 1);
            return t.TrimEnd() + "…";
        }
        // a side column's head: spaced Gold eyebrow and the 96x2 rule
        void ColumnHead(float x, float y, float w, string text)
        {
            UiTheme.Label(S(x, y, w, 20), UiTheme.Spaced(text), UiImgui.Styles.Eyebrow);
            UiImgui.Rule(x, y + 26);
        }
        string RunEyebrow => d.Mode == RunMode.Practice ? "Practice" : RunDifficulty.Name(d.Difficulty) + " run";

        // ---------------------------------------------------------------- Ready card
        // Campaign preparation: an ink strip with a gold left bar under the boss bar, the stage as eyebrow, the arena as title,
        // what this stretch holds, and Ready as the one (focused) row. Enter readies in RunDirector too; A comes through here.
        void ReadyCard()
        {
            bool practice = d.Mode == RunMode.Practice;
            // practice shows no preparation card (Risxhb, October 25, 2026): the fight starts by itself (RunDirector.AutoReady)
            if (practice) return;
            if (Shop.RunShopFlow.Showing || d.Paused) return;   // the intermission shop replaces the preparation card
            OverlayStyles();
            bool fresh = FreshFrame(ref readySeen);
            var s = UiImgui.Styles;
            string head = CampaignHead();
            string body = d.EncounterOrder.Length == 1 ? "One fight: the Bellkeeper." : CampaignBody();
            const float pad = UiTheme.G4;
            // body clamped to 2 lines: widen the strip (not the text) until it fits
            float w = 640, twoLines = 2 * s.Body.lineHeight / UiTheme.Scale + 2;
            while (w < 960 && Hv(s.Body, body, w - 2 * pad) > twoLines) w += 40;
            float bh = Mathf.Min(Hv(s.Body, body, w - 2 * pad), twoLines);
            float h = pad + 26 + 38 + 6 + 2 + 14 + bh + 14 + 48 + 8 + 22 + pad - 8;
            float x = ColumnX(w) - 60, y0 = 168, tx = x + pad;
            SoftStrip(new Rect(x, y0, w + 160, h));
            readyFocus.Begin(); if (fresh) readyFocus.Reset(0);
            float y = UiImgui.Header(new Rect(tx, y0 + pad - 4, w - 2 * pad, 0), RunDifficulty.Name(d.Difficulty), head, false, null, ovCardTitle) + 14;
            T(tx, y, w - 2 * pad, bh + 4, body, s.Body); y += bh + 14;
            if (UiImgui.Row(readyFocus, new Rect(tx - 16, y, 340, 48), "Ready", ovPrimaryInset)) d.RequestReady();
            y += 56;
            var hs = HintStyle; var al = hs.alignment; hs.alignment = TextAnchor.MiddleLeft;
            UiTheme.Label(S(tx, y, w, 20), ReadyHints.Text, hs); hs.alignment = al;
        }

        // ---------------------------------------------------------------- downed card
        // Solo wipe moment before Results: the ink strip with the gold bar, "Down", what struck you (its boss attack icon when
        // catalogued) and, for a scripted wipe, the attack on a second line.
        void DownedCard()
        {
            OverlayStyles();
            const float pad = UiTheme.G3;
            // a catalogued boss attack shows its 64 px icon, with the attack name and boss beside it (BossAttackIcons)
            // the last hit only if it took you to 0 and is the defeat cause; a scripted wipe (an empty tower, Severance, Last
            // Light) downs you with its own cause, and shows its family's icon with the attack name and boss on a second line
            // (8 Nov icons). The enrage has no family: text only.
            var wd = d.World; BossAttackIcons.Attack ka = null; bool scripted = false;
            if (wd != null)
            {
                int n = wd.TakenLog.Count;
                if (n > 0 && wd.TakenLog[n - 1].hpAfter <= 0 && wd.TakenLog[n - 1].source == wd.LastDamageSource)
                {
                    ka = TakenAttack(wd, n - 1);
                    scripted = NamesOwnCause(wd.TakenLog[n - 1].source, ka);
                }
                else { ka = BossAttackIcons.Get(wd.LastDamageAttackId); scripted = ka != null; }
            }
            var ki = BossAttackIcons.Icon(ka);
            string a = "Struck by ", cause = ka != null && !scripted ? ka.Label : d.DefeatCause ?? "?";
            float wa = Wv(ovCause, a), textW = Mathf.Min(620, wa + Wv(ovCause, cause) + 4), w = pad + (ki != null ? 64 + UiTheme.G2 : 0) + textW + pad;
            // above the arena's centre (the boss and its telegraphs), just under the boss bar
            float h = pad + 20 + 8 + 30 + (scripted ? 26 : 0) + pad, x = ColumnX(w), y = 150;
            h = Mathf.Max(h, ki != null ? 64 + 2 * pad : 0);
            SoftStrip(new Rect(x, y, w + 160, h));
            float tx = x + pad;
            if (ki != null) { GUI.DrawTexture(S(tx, y + h / 2 - 32, 64, 64), ki, ScaleMode.ScaleToFit, true); tx += 64 + UiTheme.G2; }
            float tw = x + w - pad - tx, yy = y + pad;
            UiTheme.Label(S(tx, yy, tw, 20), UiTheme.Spaced("Down"), UiImgui.Styles.Eyebrow); yy += 28;
            T(tx, yy, wa + 2, 30, a, ovCause, UiTheme.IvoryBody);
            T(tx + wa, yy, tw - wa, 30, Ellipsize(ovCause, cause, tw - wa), ovCause, UiTheme.Gold); yy += 30;
            if (scripted) T(tx, yy, tw, 26, ka.Label, ovRecap, UiTheme.IvoryMuted);
        }

        // ---------------------------------------------------------------- Pause
        void Pause()
        {
            OverlayStyles();
            bool fresh = FreshFrame(ref pauseSeen);
            DeepDim();
            bool test = LastBell.Menu.MenuReturnBridge.TestSession, practice = d.Mode == RunMode.Practice;
            var s = UiImgui.Styles;
            int nb = test ? 4 : 3;
            const float CW = 480, primaryH = 56, rowH = 48;
            float headH = 26 + 65 + 4 + 2, h = headH + 36 + primaryH + (nb - 1) * rowH + 24 + 20;
            float cx = ColumnX(CW), y0 = UiTheme.VirtualHeight / 2 - h / 2;
            UiImgui.Band(new Rect(0, y0 - 220, UiTheme.VirtualWidth, h + 440));
            pauseFocus.Begin(); if (fresh) pauseFocus.Reset(0);
            float y = UiImgui.Header(new Rect(cx, y0, CW, 0), RunEyebrow, "Paused", true) + 36;
            if (UiImgui.Row(pauseFocus, new Rect(cx, y, CW, primaryH), "Resume", s.Primary)) d.SetPaused(false);
            y += primaryH;
            if (UiImgui.Row(pauseFocus, new Rect(cx, y, CW, rowH), practice ? "Restart Practice" : "Restart Run")) d.RequestRetry();
            y += rowH;
            if (UiImgui.Row(pauseFocus, new Rect(cx, y, CW, rowH), "Main Menu")) d.ReturnToMenu();
            y += rowH;
            if (test && UiImgui.Row(pauseFocus, new Rect(cx, y, CW, rowH), "Test Setup")) LastBell.Menu.MenuReturnBridge.Return(true);
            if (test) y += rowH;
            HintText(y + 24, PauseHints.Text);
            if (pauseFocus.Cancelled && !fresh && d.Paused) d.SetPaused(false);   // B (Esc already resumed in RunDirector)
        }

        // ---------------------------------------------------------------- Results
        // LAN client results: the host decides Retry or Main Menu; the client can only wait or leave.
        void LanResults()
        {
            OverlayStyles();
            bool fresh = FreshFrame(ref resultsSeen);
            DeepDim();
            const float CW = 560;
            float cx = ColumnX(CW), y0 = 300;
            UiImgui.Band(new Rect(0, y0 - 150, UiTheme.VirtualWidth, 520));
            resultsFocus.Begin(); if (fresh) resultsFocus.Reset(0);
            float y = UiImgui.Header(new Rect(cx, y0, CW, 0), "LAN session", d.Victory ? "Victory" : "Defeat", true) + 22;
            const string body = "Waiting for the host to retry or leave.";
            T(cx, y, CW, Hv(bodyC, body, CW) + 4, body, bodyC); y += 56;
            if (UiImgui.Row(resultsFocus, new Rect(cx, y, CW, 48), "Leave Session")) LanLeave?.Invoke();
            HintText(y + 72, SelectHint.Text);
        }

        // Results: the centre column (header, summary, stats, Retry as the main action, Main Menu), the death recap on the
        // left after a defeat and the run's damage meter on the right, over a deeper dim and the centre band.
        void Results()
        {
            OverlayStyles();
            bool fresh = FreshFrame(ref resultsSeen);
            DeepDim();
            bool test = LastBell.Menu.MenuReturnBridge.TestSession, practice = d.Mode == RunMode.Practice;
            var w = d.World; var s = UiImgui.Styles;
            string title = d.Victory ? (practice ? "Practice Cleared" : "Victory") : "Defeat";
            string summary = d.Victory ? (practice ? "Practice result only: the run is unchanged." : d.BonusLost ? "The Bellkeeper is silenced and the run is won. The Overclocked Tempo Tyrant kept time: his card waits in Boss Rush."
                                       : d.BonusFight ? "The Overclocked Tempo Tyrant is broken. Nothing is left to ring." : "The Bellkeeper is silenced. The run is cleared."
                                         + (d.Difficulty == Difficulty.Hard ? "" : " Win on Hard to face the Overclocked Tempo Tyrant."))
                                       : $"Downed by {d.DefeatCause} during {(d.Boss ? d.Boss.DisplayName : RunDirector.Bosses[d.Encounter].Name)}.";
            float secs = d.RunTicks / 60f;
            var rows = new List<(string, string)>
            {
                ("Time in combat", $"{Mathf.FloorToInt(secs / 60f)}:{Mathf.FloorToInt(secs % 60f):00}"),
                ("Encounters cleared", d.EncountersCleared.ToString()),
            };
            if (w != null) { rows.Add(("Damage dealt", w.DamageDealt.ToString())); rows.Add(("Damage taken", w.DamageTaken.ToString())); }

            bool recap = !d.Victory && w != null && w.TakenLog.Count > 0, meter = w != null && w.RunMeter.Count > 0;
            float vw = UiTheme.VirtualWidth;
            const float rowH = 32, y0 = 190;
            float CW = UiTheme.VirtualWidth < 1700 ? 480 : 560;   // 4:3: a narrower centre leaves the recap room
            // side columns: as wide as fit beside the centre (4:3 included), at least the meter's width
            float gap = vw < 1700 ? UiTheme.G4 : 72, SW = Mathf.Clamp((Mathf.Min(vw, 1920) - CW - 2 * gap - 2 * UiTheme.G4) / 2, MeterW, 440);
            float cx = ColumnX(CW);
            UiImgui.Band(new Rect(0, y0 - 150, vw, 900));
            resultsFocus.Begin(); if (fresh) resultsFocus.Reset(0);

            // centre: header, summary, stats, actions
            float y = UiImgui.Header(new Rect(cx, y0, CW, 0), RunEyebrow, title, true) + 22;
            float sh = Hv(bodyC, summary, CW);
            T(cx, y, CW, sh + 4, summary, bodyC); y += sh + 22;
            foreach (var (k, v) in rows)
            {
                T(cx, y, 300, rowH, k, rowLabel);
                T(cx, y, CW, rowH, v, numR);
                F(cx, y + rowH - 1, CW, 1, UiTheme.RuleFaint);
                y += rowH;
            }
            y += 30;
            if (UiImgui.Row(resultsFocus, new Rect(cx, y, CW, 56), practice ? "Retry Practice" : "Retry Run", s.Primary)) d.RequestRetry();
            y += 56;
            if (UiImgui.Row(resultsFocus, new Rect(cx, y, CW, 48), "Main Menu")) d.ReturnToMenu();
            y += 48;
            if (test && UiImgui.Row(resultsFocus, new Rect(cx, y, CW, 48), "Test Setup")) LastBell.Menu.MenuReturnBridge.Return(true);
            if (test) y += 48;
            HintText(y + 24, ResultHints.Text);

            // right: damage meter, unframed, tops aligned with the header
            if (meter) Meter(cx + CW + gap + (SW - MeterW) / 2, y0 + 16, w.RunMeter, "DAMAGE · whole " + (practice ? "practice" : "run"), d.RunTicks / 60f, 0, false);

            // left: death recap — the last hits in order, the killing blow marked with a Danger bar and its own label
            if (recap)
            {
                int from = Mathf.Max(0, w.TakenLog.Count - 6);
                float rx = cx - gap - SW, yy = y0;
                ColumnHead(rx, yy, SW, "Death recap"); yy += 40;
                T(rx, yy, SW, 20, "last hits taken, oldest first", ovRecapMuted); yy += 28;
                const float numW = 150;   // damage, hp, time
                int last = w.TakenLog[w.TakenLog.Count - 1].tick, hover = -1;
                var mouse = Event.current.mousePosition;
                for (int i = from; i < w.TakenLog.Count; i++)
                {
                    var e = w.TakenLog[i]; bool fatal = i == w.TakenLog.Count - 1 && e.hpAfter <= 0;
                    // a catalogued boss attack shows its icon (44 px on the killing blow, 24 px otherwise) and "Attack · Boss";
                    // the name always sits beside the icon, because several icons look alike at small sizes (BossAttackIcons)
                    var at = TakenAttack(w, i); var icon = BossAttackIcons.Icon(at);
                    float rh = fatal ? (icon != null ? 70 : 54) : 34, ty = yy + (fatal ? 22 : 0) + (fatal ? 0 : 1), ic = fatal ? 44 : 24, lx = rx;
                    if (fatal) { F(rx - 14, yy + 2, SW + 14, rh - 4, A(UiTheme.Danger, 0.10f)); F(rx - 14, yy + 2, 3, rh - 4, UiTheme.Danger); T(rx, yy + 4, SW, 18, UiTheme.Spaced("Killing blow"), ovRecapHead); }
                    float lineH = fatal ? ic : 32;
                    if (icon != null) { GUI.DrawTexture(S(rx, ty + (lineH - ic) / 2, ic, ic), icon, ScaleMode.ScaleToFit, true); lx = rx + ic + 10; }
                    string what = at != null ? at.Label : e.source; float ww = SW - numW - 10 - (lx - rx);
                    T(lx, ty, ww, lineH, Ellipsize(ovRecap, what, ww), ovRecap, fatal ? UiTheme.Ivory : UiTheme.IvoryBody);
                    T(rx + SW - numW, ty, 44, lineH, "-" + e.damage, ovRecapR, fatal ? UiTheme.Danger : UiTheme.IvoryBody);
                    T(rx + SW - numW + 46, ty, 56, lineH, "hp " + e.hpAfter, ovRecapR, UiTheme.IvoryMuted);
                    T(rx + SW - numW + 102, ty, 48, lineH, $"{(e.tick - last) / 60f:0.0}s", ovRecapR, UiTheme.IvoryMuted);
                    if (!fatal) F(rx, yy + rh - 1, SW, 1, UiTheme.RuleFaint);
                    if (at != null && S(rx - 14, yy, SW + 14, rh).Contains(mouse)) hover = i;
                    yy += rh;
                }
                if (hover < 0 && PinRecapTooltip) hover = w.TakenLog.Count - 1;
                var ha = hover >= 0 ? TakenAttack(w, hover) : null;
                if (ha != null) AttackTooltip(rx, yy + 14, SW, ha, $"Hit you for {w.TakenLog[hover].damage} (hp {w.TakenLog[hover].hpAfter} after){(hover == w.TakenLog.Count - 1 ? ": the killing blow." : ".")}");
            }
        }

        // Boss recap tooltips (BossAttackIcons): the attack ID recorded per hit, parallel to TakenLog.
        // PinRecapTooltip shows the killing blow's details without a mouse (autotest screenshots).
        public static bool PinRecapTooltip;
        // A lethal hit that names its own cause, not just its attack (a Bellkeeper Ultimate wipe: "The escapement skips (empty
        // plate)"), keeps that text on the downed card, with its family's icon and name on the second line like a ForceDown wipe.
        internal static bool NamesOwnCause(string source, BossAttackIcons.Attack a)
        {
            if (a == null || string.IsNullOrEmpty(source) || string.Equals(source, a.name, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (a.aliases != null) foreach (var al in a.aliases) if (string.Equals(source, al, System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static BossAttackIcons.Attack TakenAttack(CombatWorld w, int i) => i < w.TakenAttackIds.Count ? BossAttackIcons.Get(w.TakenAttackIds[i]) : null;

        // Boss attack details (virtual units): an ink strip with the gold left bar (as the in-run tooltips), the 64 px icon
        // with the attack name, boss and tag beside it, then its shape.
        void AttackTooltip(float x, float y, float width, BossAttackIcons.Attack a, string line)
        {
            const float pad = UiTheme.G2, ic = 64;
            string body = a.shape + (string.IsNullOrEmpty(line) ? "" : "\n" + line);
            float tw = width - 2 * pad - ic - 12, bh = Hv(descSt, body, tw), h = Mathf.Max(ic + 2 * pad, pad + 22 + 20 + bh + pad);
            UiImgui.InfoStrip(new Rect(x, y, width, h));
            var icon = BossAttackIcons.Icon(a);
            if (icon != null) GUI.DrawTexture(S(x + pad, y + pad, ic, ic), icon, ScaleMode.ScaleToFit, true);
            float tx = x + pad + ic + 12;
            T(tx, y + pad, tw, 22, a.name.ToUpper(), heading);
            T(tx, y + pad + 22, tw, 20, $"{a.bossName}   ·   {a.tag}", eyebrow);
            T(tx, y + pad + 44, tw, bh + 4, body, descSt);
        }
    }

    // A footer hint line built from InputHints ("Enter  Select      Esc  Resume" / "A  Select      B  Resume"), cached per
    // device so OnGUI builds no strings. Items whose key is empty on a device are left out there.
    public sealed class HintLine
    {
        readonly (HintAction action, string label)[] items; string kb, pad;
        public HintLine(params (HintAction action, string label)[] items) { this.items = items; }
        public string Text => InputHints.Last == InputHints.Device.Gamepad ? pad ?? (pad = Build(InputHints.Device.Gamepad)) : kb ?? (kb = Build(InputHints.Device.KeyboardMouse));
        string Build(InputHints.Device device)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (a, label) in items)
            {
                string k = InputHints.Key(a, device); if (string.IsNullOrEmpty(k)) continue;
                if (sb.Length > 0) sb.Append("        ");
                sb.Append(k).Append("  ").Append(label);
            }
            return sb.ToString();
        }
    }
}
