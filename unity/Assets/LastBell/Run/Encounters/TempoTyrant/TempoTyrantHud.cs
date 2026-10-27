using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Redundant, colour-independent tempo and combo cues for The Tempo Tyrant (readable with audio muted). Laid out in
    // virtual 1080p units through UiTheme (Scale = Screen.height / 1080, clamped), house fonts, frames and shadowed labels.
    //   - tempo cue (no panel): the word HASTE / NORMAL and a swinging mini metronome whose weight slides with the tempo;
    //     the shift shows only in its last 3 s and as "SHIFT n / 3" on the accented ticks (BPM and timings: F7);
    //   - frenzy badge (Broken Cadence), text only, once the centre banner has cleared;
    //   - Threefold Severance / Hollow Crown order numbers on the floor, kept inside a safe screen rect: the imminent strike
    //     large and boxed, upcoming ones small and bracketed, spent ones removed;
    //   - large announcements for non-damaging tells (closing rush, Broken Cadence, tempo shift);
    //   - diagnostics (tempo state, pending transition, beat, frenzy/chain, music status): F7, or the acceptance test.
    public sealed class TempoTyrantHud : MonoBehaviour
    {
        TempoTyrantEncounter boss;
        GUIStyle heading, numeric, body, display, num, numSmall, badge, diag;
        int styledFor; float weightT;
        public static bool ShowDiagnostics;
        // Risxhb, Oct 24: no strike-order numerals on the floor; the warning shapes alone carry the order
        public static bool ShowStrikeNumbers = false;

        public void Bind(TempoTyrantEncounter owner) { boss = owner; }

        // virtual layout constants (1080p units)
        const float PanelW = 336, PanelH = 140, PanelTop = 64, Pad = UiTheme.G3;

        void Styles()
        {
            if (heading != null && styledFor == Screen.height) return;
            styledFor = Screen.height;
            heading = UiTheme.Style(UiTheme.SizeHeading, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            numeric = UiTheme.Style(UiTheme.SizeNumeric, true, TextAnchor.MiddleLeft, UiTheme.Ivory);
            body = UiTheme.Style(UiTheme.SizeBody, false, TextAnchor.MiddleLeft, UiTheme.IvoryBody);
            display = UiTheme.Style(UiTheme.SizeDisplay, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            num = UiTheme.Style(48, true, TextAnchor.MiddleCenter, Color.white);
            numSmall = UiTheme.Style(30, true, TextAnchor.MiddleCenter, UiTheme.Ivory);
            badge = UiTheme.Style(20, true, TextAnchor.MiddleLeft, UiTheme.Danger);
            diag = UiTheme.Style(13, false, TextAnchor.UpperLeft, UiTheme.IvoryMuted, true);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        // the warning length a typical indicator has right now (Wake charge), so the panel shows Haste casting faster
        float WarningSeconds() => boss.Clock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.WakeWarn, boss.Clock.State)) / (float)Sim.Hz;
        float GapSeconds() => boss.Clock.TicksFor(TempoTyrantEncounter.Of(TempoTyrantEncounter.SevGap, boss.Clock.State)) / (float)Sim.Hz;
        readonly System.Collections.Generic.List<Vector2> placed = new System.Collections.Generic.List<Vector2>();

        void OnGUI()
        {
            GUI.depth = 10;   // beneath RunHud, so its tooltips and modals stay on top
            var d = RunDirector.Instance;
            if (!boss || d == null || d.Boss != boss || (d.State != RunState.Combat && d.State != RunState.Intro)) return;
            if (d.Paused || d.State == RunState.Results) return;   // never draw over the pause or results modal
            Styles();
            float s = UiTheme.Scale;
            var c = boss.Clock;
            bool haste = c.State == Tempo.Haste;
            var tint = haste ? UiTheme.Haste : UiTheme.Calm;

            // ---- tempo cue (top right of the centred 1920 band), no panel: the tempo word in its tint and a mini metronome
            // whose weight slides like the arena's (low = fast). BPM and warning lengths live in the F7 diagnostics; the
            // shift is shown only as an exception (the last 3 s, then SHIFT n / 3 on the accented ticks).
            float vw = UiTheme.VirtualWidth, band = Mathf.Min(vw, 1920f);
            float vx = (vw + band) * 0.5f - UiTheme.G4 - PanelW;   // 32 px margin; on 21:9 the band keeps it off the edge
            var pr = UiTheme.R(vx, PanelTop, PanelW, PanelH);
            float px = pr.x, py = pr.y, pw = pr.width, ph = pr.height;
            float tx = px + Pad * s, tw = pw - Pad * s - 110 * s;
            UiTheme.Label(new Rect(tx, py + 16 * s, tw + 40 * s, 34 * s), haste ? "HASTE  >>>" : "NORMAL  —", heading, tint);
            float shiftIn = Mathf.Max(0, (c.Duration - c.StateAge) / (float)Sim.Hz);
            if (c.Transitioning) UiTheme.Label(new Rect(tx, py + 52 * s, tw + 40 * s, 34 * s), "SHIFT " + (c.TransitionTick + 1) + " / 3", heading, UiTheme.Ivory);
            else if (!c.Pending && shiftIn <= 3f)
            {
                var nextTint = haste ? UiTheme.Calm : UiTheme.Haste;
                UiTheme.Label(new Rect(tx, py + 52 * s, tw + 40 * s, 34 * s), $"shift in {Mathf.CeilToInt(shiftIn)}", heading, new Color(nextTint.r, nextTint.g, nextTint.b, 0.8f));
            }
            // mini metronome: 96 px rod, 4 px wide, on a gold base; the weight eases between Haste (low) and Normal (high)
            weightT = Mathf.MoveTowards(weightT, haste ? 0f : 1f, Time.unscaledDeltaTime / 0.5f);
            float wordW = heading.CalcSize(new GUIContent(haste ? "HASTE  >>>" : "NORMAL  —")).x;
            var pivot = new Vector2(Mathf.Min(tx + wordW + 40 * s, px + pw - (Pad + 34) * s), py + 33 * s + 48 * s);   // beside the word, word at the rod's middle
            var ink = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.85f);
            UiTheme.Fill(new Rect(pivot.x - 28 * s + 2 * s, pivot.y - 2 * s + 2 * s, 56 * s, 7 * s), ink);   // a 2 px ink shadow separates it from the scenery
            UiTheme.Fill(new Rect(pivot.x - 28 * s, pivot.y - 2 * s, 56 * s, 7 * s), UiTheme.Gold);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(c.RodPhase * 28f, pivot);
            float wy = Mathf.Lerp(22f, 82f, Mathf.SmoothStep(0, 1, weightT));
            UiTheme.Fill(new Rect(pivot.x - 2f * s + 2 * s, pivot.y - 96 * s + 2 * s, 4 * s, 96 * s), ink);
            UiTheme.Fill(new Rect(pivot.x - 9 * s + 2 * s, pivot.y - (wy + 8) * s + 2 * s, 18 * s, 16 * s), ink);
            UiTheme.Fill(new Rect(pivot.x - 2f * s, pivot.y - 96 * s, 4 * s, 96 * s), UiTheme.Ivory);
            UiTheme.Fill(new Rect(pivot.x - 9 * s, pivot.y - (wy + 8) * s, 18 * s, 16 * s), tint);
            GUI.matrix = m;
            // frenzy: the centre banner owns the moment; the small badge (text only, no box) appears once the banner clears
            float below = py + 92 * s;
            if (boss.Frenzy && !(boss.Announcement ?? "").StartsWith("BROKEN"))
            {
                var br = new Rect(tx, below, pw, 30 * s);
                UiTheme.Label(br, "|  BROKEN CADENCE", badge, UiTheme.Haste);
                below = br.yMax + UiTheme.G1 * s;
            }

            // ---- strike order numbers on the floor, clamped into a safe screen rect ----
            var cam = Camera.main;
            placed.Clear();
            if (cam && ShowStrikeNumbers)
                foreach (var (at, n, imminent) in boss.Numbers)
                {
                    var sp = cam.WorldToScreenPoint(at + Vector3.up * 0.2f); if (sp.z <= 0) continue;
                    var p = new Vector2(Mathf.Clamp(sp.x, Screen.width * 0.08f, Screen.width * 0.92f), Mathf.Clamp(Screen.height - sp.y, Screen.height * 0.18f, Screen.height * 0.85f));
                    // never stack two labels: push sideways until 90 px (scaled) clear of every label already drawn
                    for (int guard = 0; guard < 6; guard++)
                    {
                        bool clash = false;
                        foreach (var q in placed) if ((q - p).magnitude < 90 * s) { clash = true; p.x += (p.x >= q.x ? 1 : -1) * 95 * s; }
                        if (!clash) break;
                    }
                    p.x = Mathf.Clamp(p.x, Screen.width * 0.06f, Screen.width * 0.94f); placed.Add(p);
                    // no backing box: the floor stays visible and the live warning stays the heaviest graphic. The next
                    // strike is a large ivory numeral with a dark offset shadow; later ones are small and half-dim.
                    var inkShadow = new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.85f);
                    if (imminent)
                    {
                        float h = 40 * s; var box = new Rect(p.x - h, p.y - h, 2 * h, 2 * h);
                        UiTheme.Label(new Rect(box.x + 3 * s, box.y + 3 * s, box.width, box.height), n.ToString(), num, inkShadow);
                        UiTheme.Label(box, n.ToString(), num, UiTheme.Ivory);
                    }
                    else
                    {
                        var pill = new Rect(p.x - 30 * s, p.y - 20 * s, 60 * s, 40 * s);
                        var dim = new Color(UiTheme.Ivory.r, UiTheme.Ivory.g, UiTheme.Ivory.b, 0.75f);
                        UiTheme.Label(new Rect(pill.x + 3 * s, pill.y + 3 * s, pill.width, pill.height), n.ToString(), numSmall, new Color(inkShadow.r, inkShadow.g, inkShadow.b, 0.85f));
                        UiTheme.Label(pill, n.ToString(), numSmall, dim);
                    }
                }

            // ---- announcements (non-damaging tells): Display spaced on a 56 px Ink 0.5 strip ----
            if (!string.IsNullOrEmpty(boss.Announcement))
            {
                string text = UiTheme.Spaced(boss.Announcement);
                float textW = display.CalcSize(new GUIContent(text)).x;
                float sw = Mathf.Min(Screen.width, textW + 2 * UiTheme.G6 * s), sh = 56 * s;
                var strip = new Rect((Screen.width - sw) * 0.5f, Screen.height * 0.20f, sw, sh);   // above the play area, clear of floor markers
                UiTheme.Label(new Rect(strip.x + 3 * s, strip.y + 3 * s, strip.width, strip.height), text, display, new Color(UiTheme.Ink.r, UiTheme.Ink.g, UiTheme.Ink.b, 0.9f));
                UiTheme.Label(strip, text, display, boss.Announcement.StartsWith("BROKEN") ? UiTheme.Danger : tint);
            }

            // ---- diagnostics (F7, dev only) ----
            if (ShowDiagnostics)
            {
                var dr = new Rect(px, below, pw, 96 * s);
                UiTheme.Fill(dr, UiTheme.InkStrip);
                UiTheme.Label(new Rect(dr.x + UiTheme.G1 * s, dr.y + 4 * s, dr.width - 2 * UiTheme.G1 * s, dr.height - 6 * s),
                    $"tempo {c.State} age {c.StateAge}/{c.Duration}{(c.Pending ? " PENDING" : "")}{(c.Transitioning ? " TRANSITION" : "")} · beat {c.Beat:0.0} · commits {c.Commits}\n" +
                    $"{c.Bpm:0.##} BPM · Wake {WarningSeconds():0.0} s · gap {GapSeconds():0.0} s · frenzy {(boss.Frenzy ? "on" : "off")} chain {(boss.ChainDue ? "due" : "-")} · {boss.Current}\n" +
                    (boss.Audio ? boss.Audio.StatusText : ""), diag, UiTheme.IvoryMuted);
            }
        }
    }
}
