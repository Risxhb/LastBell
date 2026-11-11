using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Party cards (UI sub-thread C, docs/plans/ui-uniformity-plan-20261110.md): one small card per ally in the fight, bot
    // (RunParty) or LAN player, in the bottom-left corner, clear of the floor telegraphs, the boss bar and the player panel.
    // Main-menu language: an ink strip with a 3 px Gold left bar (UiImgui.InfoStrip), a spaced Gold eyebrow and an Ivory
    // title, theme tokens only. Each card shows the class, the health and the down / revive state:
    //   standing  - liquid health bar (HealthPlayer, chip trail, hit flash) and the HP number
    //   down      - Danger bar and "DOWN 24 s"; the bar is the bleed-out left, the revive fills over it in Ivory ("REVIVING")
    //   revived   - "REVIVED" in GoldBright for 2 s after getting up
    //   dead      - the card dims to half and reads "DEAD"
    // Drawn by RunHud.DrawHud (one call); reads live state only and changes nothing. Works on a LAN host (names from
    // LanHostRoster) and a LAN client (names from NameOf, set by LanClientWorld).
    public static class RunPartyCards
    {
        // LAN client: the lobby name of a replica player (null for none). The host resolves names from LanHostRoster.
        public static System.Func<PlayerController, string> NameOf;
        // What the last repainted frame showed, one entry per card: "title|class|hp/max|state" (tests and captures).
        public static readonly List<string> Shown = new List<string>();

        public const float W = 248, H = 58, Gap = 8;
        sealed class Track { public int lastHp = -1; public float trail = 1, hold, flash, upUntil; public bool wasDown; }
        static readonly Dictionary<PlayerController, Track> tracks = new Dictionary<PlayerController, Track>();
        static readonly List<PlayerController> allies = new List<PlayerController>();
        static GUIStyle eyebrow, title, value, status; static int styleHeight = -1;
        static readonly Dictionary<string, string> spaced = new Dictionary<string, string>();
        static readonly string[] downText = new string[64], numbers = new string[1000];

        static void Styles()
        {
            if (styleHeight == Screen.height && title != null) return;
            styleHeight = Screen.height;
            eyebrow = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleLeft, UiTheme.Gold);
            title = UiTheme.Style(UiTheme.SizeNumeric + 1, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            value = UiTheme.Style(UiTheme.SizeLabel + 1, true, TextAnchor.MiddleRight, UiTheme.IvoryBody);   // readable at 768p
            status = UiTheme.Style(UiTheme.SizeMicro, true, TextAnchor.MiddleRight, UiTheme.Ivory);
        }
        static string Spaced(string s) { if (!spaced.TryGetValue(s, out var r)) spaced[s] = r = UiTheme.Spaced(s); return r; }
        static string Num(int n) => n >= 0 && n < numbers.Length ? numbers[n] ??= n.ToString() : n.ToString();
        static string Down(int s) { s = Mathf.Clamp(s, 0, downText.Length - 1); return downText[s] ??= "DOWN  " + s + " s"; }
        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        static float a = 1;
        static Color Fa(Color c) => A(c, a);
        static Rect S(float x, float y, float w, float h) => UiTheme.R(x, y, w, h);

        // Allies of the local player in the current fight, in world order (bots, or the other LAN players).
        public static List<PlayerController> Allies(RunDirector d)
        {
            allies.Clear();
            if (d == null || d.World == null || d.World.Players.Count < 2) return allies;
            foreach (var p in d.World.Players) if (p != null && p != d.Player) allies.Add(p);
            return allies;
        }
        // The card's title: the player's lobby name in a LAN fight, otherwise (a bot) its class.
        public static string Title(RunDirector d, PlayerController p) { var n = Name(d, p); return string.IsNullOrEmpty(n) ? p.ClassName : n; }
        static string Name(RunDirector d, PlayerController p)
        {
            if (NameOf != null) return NameOf(p);
            var roster = Networking.LanHostRoster.Active;
            if (roster == null) return null;
            int i = d.World.Players.IndexOf(p);
            return i > 0 && i - 1 < roster.Remote.Count ? roster.Remote[i - 1].Name : null;
        }

        // left, bottom: virtual units (RunHud's left content edge and the bottom margin); cards stack upwards.
        public static void Draw(RunDirector d, float left, float bottom)
        {
            var list = Allies(d);
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) Shown.Clear();
            if (list.Count == 0) { if (tracks.Count > 0) tracks.Clear(); return; }
            Styles();
            float y = bottom - list.Count * H - (list.Count - 1) * Gap;
            foreach (var p in list) { Card(d, p, left, y, repaint); y += H + Gap; }
        }

        static void Card(RunDirector d, PlayerController p, float x, float y, bool repaint)
        {
            if (!tracks.TryGetValue(p, out var t)) tracks[p] = t = new Track();
            int max = Mathf.Max(1, p.MaxHealth), hp = Mathf.Clamp(p.Hp, 0, max);
            float f = hp / (float)max, now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (repaint)
            {
                // the trail holds 0.3 s after a hit, then closes like the boss bar's chip; a hit flashes the pool
                if (t.lastHp >= 0 && hp < t.lastHp) { t.hold = 0.3f; t.flash = 1; }
                if (t.wasDown && !p.Down) t.upUntil = now + 2f;
                t.lastHp = hp; t.wasDown = p.Down;
                t.flash = Mathf.MoveTowards(t.flash, 0, dt / 0.18f);
                if (f >= t.trail || p.Down) t.trail = f;
                else if (t.hold > 0) t.hold -= dt;
                else { t.trail = Mathf.Lerp(t.trail, f, 1 - Mathf.Exp(-dt * 7f)); if (t.trail - f < 0.001f) t.trail = f; }
            }
            string name = Name(d, p), cls = p.ClassName ?? "";
            bool bot = string.IsNullOrEmpty(name);
            string top = bot ? cls : name;                       // title: the LAN name, or the bot's class
            string eyebrowText = bot ? Spaced("Bot") : Spaced(cls);
            a = p.Dead ? 0.5f : 1f;
            var old = GUI.color; GUI.color = new Color(old.r, old.g, old.b, old.a * a);   // labels; fills take Fa()

            // UiImgui.InfoStrip's look (ink strip, 3 px left bar), drawn here so a dead card can dim. The ink is denser than the
            // world-text strip (critic round 1: red floors tinted the cards), more so while down; the bar turns with the state and
            // pulses 3-5 px while down so a downed ally is seen at a glance.
            bool downNow = p.Down && !p.Dead;
            UiTheme.Fill(S(x, y, W, H), Fa(A(UiTheme.InkDeep, downNow ? 0.9f : 0.8f)));
            float barW = downNow ? 4 + Mathf.Sin(now * 6f) : 3;
            bool reviving = downNow && p.ReviveProgress > 0.001f;   // the revive turns the bar Ivory, as it does the bell ring
            UiTheme.Fill(S(x, y, reviving ? 3 : barW, H), Fa(p.Dead ? UiTheme.IvoryMicro : reviving ? UiTheme.Ivory : downNow ? UiTheme.Danger : UiTheme.Gold));

            const float pad = 14;
            float cx = x + pad, cw = W - pad - 10;
            UiTheme.Label(S(cx, y + 6, cw, 14), eyebrowText, eyebrow);
            UiTheme.Label(S(cx, y + 20, cw, 20), top, title);

            // right of the title: the HP number, or the state
            string st; Color sc;
            if (p.Dead) { st = "DEAD"; sc = UiTheme.IvoryMuted; }
            else if (p.Down && p.ReviveProgress > 0.001f) { st = "REVIVING"; sc = UiTheme.Ivory; }
            else if (p.Down) { st = Down(Mathf.CeilToInt(PlayerController.BleedOutSeconds - p.DownedTicks / (float)Sim.Hz)); sc = UiTheme.Ivory; }
            else if (now < t.upUntil) { st = "REVIVED"; sc = UiTheme.GoldBright; }
            else { st = Num(hp); sc = f < 0.3f ? UiTheme.Danger : UiTheme.IvoryMuted; }
            var stSt = p.Down || now < t.upUntil ? status : value;
            // DOWN: Ivory on a small red tag (red text vanished over red telegraphs)
            if (downNow && p.ReviveProgress <= 0.001f) { float tw = stSt.CalcSize(new GUIContent(st)).x / UiTheme.Scale + 12; UiTheme.Fill(S(cx + cw - tw + 6, y + 22, tw, 16), UiTheme.BossRed); }
            UiTheme.Label(S(cx, y + 20, cw, 20), st, stSt, sc);

            // bar: 6 px trough; health (chip trail + flash), or while down the bleed-out left with the revive over it
            float bx = cx, by = y + H - 14, bw = cw;
            UiTheme.Fill(S(bx, by, bw, 6), Fa(A(UiTheme.BarBack, 0.9f)));
            if (p.Down && !p.Dead)
            {
                float bleed = Mathf.Clamp01(1 - p.DownedTicks / (PlayerController.BleedOutSeconds * Sim.Hz));
                // the bleed-out drains as a muted Ivory timer (not red: it read as a full health bar); the revive fills over it
                // Ivory .55 with a 1 px red underline; the last quarter runs out in red
                UiTheme.Fill(S(bx, by, bw * bleed, 6), bleed < 0.25f ? UiTheme.BossRed : A(UiTheme.Ivory, 0.55f));
                UiTheme.Fill(S(bx, by + 5, bw * bleed, 1), UiTheme.BossRed);
                if (p.ReviveProgress > 0) UiTheme.Fill(S(bx, by, bw * Mathf.Clamp01(p.ReviveProgress), 6), UiTheme.Ivory);
            }
            else if (!p.Dead)
            {
                if (t.trail > f) UiTheme.Fill(S(bx, by, bw * t.trail, 6), UiTheme.Chip);
                UiTheme.Fill(S(bx, by, bw * f, 6), Color.Lerp(UiTheme.HealthPlayer, UiTheme.HealthPlayerHi, t.flash));
                UiTheme.Fill(S(bx, by, bw * f, 1), A(UiTheme.HealthPlayerHi, 0.6f));   // highlight line
            }
            GUI.color = old;
            if (repaint) Shown.Add(top + "|" + cls + "|" + hp + "/" + max + "|" + st);
        }
    }
}
