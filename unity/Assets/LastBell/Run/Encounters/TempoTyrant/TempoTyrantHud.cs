using UnityEngine;

namespace LastBell.Run
{
    // Redundant, colour-independent tempo and combo cues for The Tempo Tyrant (readable with audio muted):
    //   - tempo panel: the word HASTE / NORMAL, BPM, a swinging mini metronome driven by the same clock, and a
    //     "SHIFT 1-2-3" countdown during the three accented transition ticks;
    //   - Threefold Severance / Hollow Crown order numbers on the floor: the imminent strike large and boxed,
    //     upcoming ones small and bracketed, spent ones removed;
    //   - large announcements for non-damaging tells (closing rush, Broken Cadence);
    //   - a diagnostics line (tempo state, pending transition, beat, frenzy/chain, music status).
    public sealed class TempoTyrantHud : MonoBehaviour
    {
        TempoTyrantEncounter boss;
        static Texture2D white;
        GUIStyle big, num, numSmall, small, panel;
        public static bool ShowDiagnostics = true;

        public void Bind(TempoTyrantEncounter owner) { boss = owner; }

        void Styles()
        {
            if (big != null) return;
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); white.hideFlags = HideFlags.HideAndDontSave; }
            big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            num = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            numSmall = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = true };
            panel = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
        }

        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }
        static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            var o = GUI.color; GUI.color = new Color(0, 0, 0, 0.85f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
            GUI.color = c; GUI.Label(r, s, st); GUI.color = o;
        }

        void OnGUI()
        {
            var d = RunDirector.Instance;
            if (!boss || d == null || d.Boss != boss || (d.State != RunState.Combat && d.State != RunState.Intro)) return;
            Styles();
            var c = boss.Clock;
            bool haste = c.State == Tempo.Haste;
            var tint = haste ? new Color(1f, 0.72f, 0.3f) : new Color(0.45f, 0.95f, 0.85f);

            // ---- tempo panel (top right) ----
            float pw = 250, ph = 118, px = Screen.width - pw - 16, py = 44;
            Fill(new Rect(px, py, pw, ph), new Color(0.03f, 0.04f, 0.07f, 0.78f));
            Fill(new Rect(px, py, 4, ph), tint);
            Shadowed(new Rect(px + 14, py + 6, 150, 30), haste ? "HASTE  >>>" : "NORMAL  —", panel, tint);
            GUI.Label(new Rect(px + 14, py + 36, 150, 20), $"{c.Bpm:0} BPM · {(haste ? "fast ticks" : "slow ticks")}", small);
            if (c.Transitioning) Shadowed(new Rect(px + 14, py + 56, 160, 30), "SHIFT " + (c.TransitionTick + 1) + " / 3", panel, Color.white);
            else if (c.Pending) GUI.Label(new Rect(px + 14, py + 60, 160, 20), "shift after this attack", small);
            else GUI.Label(new Rect(px + 14, py + 60, 160, 20), $"next shift ~{Mathf.Max(0, (c.Duration - c.StateAge) / (float)Sim.Hz):0}s", small);
            if (boss.Frenzy) Shadowed(new Rect(px + 14, py + 84, 170, 26), "BROKEN CADENCE", numSmall, new Color(1f, 0.45f, 0.35f));
            // mini metronome: the same rod phase as the arena's metronome
            var pivot = new Vector2(px + pw - 40, py + ph - 14);
            Fill(new Rect(pivot.x - 22, pivot.y - 2, 44, 6), new Color(0.75f, 0.55f, 0.25f));
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(c.RodPhase * 28f, pivot);
            Fill(new Rect(pivot.x - 2, pivot.y - 82, 4, 82), Color.white);
            Fill(new Rect(pivot.x - 7, pivot.y - 62, 14, 12), tint);
            GUI.matrix = m;

            // ---- strike order numbers on the floor ----
            var cam = Camera.main;
            if (cam)
                foreach (var (at, n, imminent) in boss.Numbers)
                {
                    var s = cam.WorldToScreenPoint(at + Vector3.up * 0.2f); if (s.z <= 0) continue;
                    var p = new Vector2(s.x, Screen.height - s.y);
                    if (imminent)
                    {
                        Fill(new Rect(p.x - 32, p.y - 32, 64, 64), new Color(0, 0, 0, 0.55f));
                        Fill(new Rect(p.x - 32, p.y - 32, 64, 3), Color.white); Fill(new Rect(p.x - 32, p.y + 29, 64, 3), Color.white);
                        Fill(new Rect(p.x - 32, p.y - 32, 3, 64), Color.white); Fill(new Rect(p.x + 29, p.y - 32, 3, 64), Color.white);
                        Shadowed(new Rect(p.x - 32, p.y - 32, 64, 64), n.ToString(), num, new Color(1f, 0.55f, 0.42f));
                    }
                    else Shadowed(new Rect(p.x - 30, p.y - 18, 60, 36), "(" + n + ")", numSmall, new Color(0.85f, 0.85f, 0.8f, 0.8f));
                }

            // ---- announcements (non-damaging tells) ----
            if (!string.IsNullOrEmpty(boss.Announcement))
                Shadowed(new Rect(0, Screen.height * 0.24f, Screen.width, 44), boss.Announcement, big, boss.Announcement.StartsWith("BROKEN") ? new Color(1f, 0.45f, 0.35f) : tint);

            // ---- diagnostics ----
            if (ShowDiagnostics)
                GUI.Label(new Rect(px, py + ph + 4, pw + 6, 64),
                    $"tempo {c.State} age {c.StateAge}/{c.Duration}{(c.Pending ? " PENDING" : "")}{(c.Transitioning ? " TRANSITION" : "")} · beat {c.Beat:0.0} · commits {c.Commits}\n" +
                    $"frenzy {(boss.Frenzy ? "on" : "off")} chain {(boss.ChainDue ? "due" : "-")} · {boss.Current}\n" +
                    (boss.Audio ? boss.Audio.StatusText : ""), small);
        }
    }
}
