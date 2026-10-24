using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Redundant, colour-independent tempo and combo cues for The Tempo Tyrant (readable with audio muted). Everything is
    // scaled by Screen.height / 1080.
    //   - tempo panel: the word HASTE / NORMAL, BPM, the current warning length in seconds (Haste casts faster), a swinging
    //     mini metronome driven by the same clock, and a "SHIFT n / 3" countdown during the accented transition ticks;
    //   - frenzy badge (Broken Cadence) under the panel;
    //   - Threefold Severance / Hollow Crown order numbers on the floor, kept inside a safe screen rect: the imminent strike
    //     large and boxed, upcoming ones small and bracketed, spent ones removed;
    //   - large announcements for non-damaging tells (closing rush, Broken Cadence, tempo shift);
    //   - diagnostics (tempo state, pending transition, beat, frenzy/chain, music status): F7, or the acceptance test.
    public sealed class TempoTyrantHud : MonoBehaviour
    {
        TempoTyrantEncounter boss;
        static Texture2D white;
        GUIStyle big, num, numSmall, small, panel, badge;
        int styledFor;
        public static bool ShowDiagnostics;

        public void Bind(TempoTyrantEncounter owner) { boss = owner; }

        float S => Screen.height / 1080f;

        void Styles()
        {
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; }
            if (big != null && styledFor == Screen.height) return;
            styledFor = Screen.height; float s = S;
            big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            num = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(48 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            numSmall = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), alignment = TextAnchor.UpperLeft, wordWrap = true };
            panel = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            badge = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
        static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            var o = GUI.color; GUI.color = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
            GUI.color = c; GUI.Label(r, s, st); GUI.color = o;
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
            var d = RunDirector.Instance;
            if (!boss || d == null || d.Boss != boss || (d.State != RunState.Combat && d.State != RunState.Intro)) return;
            Styles();
            float s = S;
            var c = boss.Clock;
            bool haste = c.State == Tempo.Haste;
            var tint = haste ? new Color(1f, 0.72f, 0.3f) : new Color(0.45f, 0.95f, 0.85f);

            // ---- tempo panel (top right) ----
            float pw = 330 * s, ph = 140 * s, px = Screen.width - pw - 18 * s, py = 44 * s;
            Fill(new Rect(px, py, pw, ph), new Color(0.03f, 0.04f, 0.07f, 0.8f));
            Fill(new Rect(px, py, 5 * s, ph), tint);
            Shadowed(new Rect(px + 16 * s, py + 6 * s, 190 * s, 34 * s), haste ? "HASTE  >>>" : "NORMAL  —", panel, tint);
            GUI.Label(new Rect(px + 16 * s, py + 40 * s, pw - 100 * s, 44 * s), $"{c.Bpm,6:0.##} BPM\nWake {WarningSeconds():0.0} s · gap {GapSeconds():0.0} s", small);
            if (c.Transitioning) Shadowed(new Rect(px + 16 * s, py + 88 * s, pw - 100 * s, 34 * s), "SHIFT " + (c.TransitionTick + 1) + " / 3", panel, Color.white);
            else if (c.Pending) GUI.Label(new Rect(px + 16 * s, py + 92 * s, pw - 100 * s, 22 * s), "shift after this attack", small);
            else GUI.Label(new Rect(px + 16 * s, py + 92 * s, pw - 100 * s, 22 * s), $"next shift ~{Mathf.Max(0, (c.Duration - c.StateAge) / (float)Sim.Hz):0} s", small);
            // mini metronome: the same rod phase as the arena's metronome
            var pivot = new Vector2(px + pw - 44 * s, py + ph - 16 * s);
            Fill(new Rect(pivot.x - 24 * s, pivot.y - 2 * s, 48 * s, 7 * s), new Color(0.75f, 0.55f, 0.25f));
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(c.RodPhase * 28f, pivot);
            Fill(new Rect(pivot.x - 2.5f * s, pivot.y - 92 * s, 5 * s, 92 * s), Color.white);
            Fill(new Rect(pivot.x - 8 * s, pivot.y - 70 * s, 16 * s, 14 * s), tint);
            GUI.matrix = m;
            // frenzy badge under the panel
            if (boss.Frenzy)
            {
                var br = new Rect(px, py + ph + 6 * s, pw, 34 * s);
                Fill(br, new Color(0.35f, 0.06f, 0.04f, 0.85f)); Fill(new Rect(br.x, br.y, 5 * s, br.height), new Color(1f, 0.45f, 0.35f));
                Shadowed(br, "BROKEN CADENCE", badge, new Color(1f, 0.6f, 0.5f));
            }

            // ---- strike order numbers on the floor, clamped into a safe screen rect ----
            var cam = Camera.main;
            placed.Clear();
            if (cam)
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
                    if (imminent)
                    {
                        float h = 34 * s, t = 3 * s;
                        Fill(new Rect(p.x - h, p.y - h, 2 * h, 2 * h), new Color(0.95f, 0.38f, 0.12f, 0.95f));   // live-border orange
                        Fill(new Rect(p.x - h, p.y - h, 2 * h, t), Color.white); Fill(new Rect(p.x - h, p.y + h - t, 2 * h, t), Color.white);
                        Fill(new Rect(p.x - h, p.y - h, t, 2 * h), Color.white); Fill(new Rect(p.x + h - t, p.y - h, t, 2 * h), Color.white);
                        Shadowed(new Rect(p.x - h, p.y - h, 2 * h, 2 * h), n.ToString(), num, Color.white);
                    }
                    else
                    {
                        Fill(new Rect(p.x - 30 * s, p.y - 18 * s, 60 * s, 36 * s), new Color(0.05f, 0.06f, 0.09f, 0.75f));   // dark pill
                        Shadowed(new Rect(p.x - 30 * s, p.y - 18 * s, 60 * s, 36 * s), "(" + n + ")", numSmall, new Color(1f, 0.96f, 0.86f));
                    }
                }

            // ---- announcements (non-damaging tells) ----
            if (!string.IsNullOrEmpty(boss.Announcement))
                Shadowed(new Rect(0, Screen.height * 0.24f, Screen.width, 48 * s), boss.Announcement, big, boss.Announcement.StartsWith("BROKEN") ? new Color(1f, 0.45f, 0.35f) : tint);

            // ---- diagnostics (F7) ----
            if (ShowDiagnostics)
            {
                float dy = py + ph + (boss.Frenzy ? 46 : 6) * s;
                Fill(new Rect(px, dy, pw, 76 * s), new Color(0, 0, 0, 0.55f));
                GUI.Label(new Rect(px + 6 * s, dy + 2 * s, pw - 8 * s, 74 * s),
                    $"tempo {c.State} age {c.StateAge}/{c.Duration}{(c.Pending ? " PENDING" : "")}{(c.Transitioning ? " TRANSITION" : "")} · beat {c.Beat:0.0} · commits {c.Commits}\n" +
                    $"frenzy {(boss.Frenzy ? "on" : "off")} chain {(boss.ChainDue ? "due" : "-")} · {boss.Current}\n" +
                    (boss.Audio ? boss.Audio.StatusText : ""), small);
            }
        }
    }
}
