using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // The Silt Carillon's screen layer (placeholder):
    // - the big numeral over each sunken bell (how many players it wants), always on: an empty bell hits the whole party, so
    //   its count must be impossible to miss (content rules §5)
    // - development-only diagnostics, OFF by default; F7 (or the acceptance test) turns them on
    public sealed class SiltCarillonHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        SiltCarillonEncounter b;
        GUIStyle style, numeral, shadow;
        readonly StringBuilder sb = new StringBuilder();
        readonly System.Collections.Generic.List<(Vector3 at, int need)> shown = new System.Collections.Generic.List<(Vector3 at, int need)>();
        static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        public void Bind(SiltCarillonEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!b || b.Dead) return;
            GUI.depth = 10;
            var cam = Camera.main;
            if (cam) b.ShownBells(shown);
            if (cam && shown.Count > 0)
            {
                numeral ??= new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                shadow ??= new GUIStyle(numeral);
                numeral.normal.textColor = new Color(0.95f, 1f, 0.95f);
                shadow.normal.textColor = new Color(0.05f, 0.12f, 0.1f, 0.85f);
                foreach (var (at, need) in shown)
                {
                    var s = cam.WorldToScreenPoint(at + Vector3.up * 1.2f);
                    if (s.z <= 0) continue;
                    var r = new Rect(s.x - 80, Screen.height - s.y - 40, 160, 80);
                    string t = need >= 0 && need < Roman.Length ? Roman[need] : need.ToString();
                    GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), t, shadow);
                    GUI.Label(r, t, numeral);
                }
            }
            if (!ShowDiagnostics) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
            style.normal.textColor = new Color(0.85f, 0.95f, 1f);
            sb.Clear();
            sb.AppendLine("[DEV] Silt Carillon (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Bpm:0} BPM   beat {b.BeatIndex}   {b.Level}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Tides {b.TideCasts} (mud live {b.TideLive}, drained early {b.DrainedLog.Count})   bells rung {b.BellsRung}, empty {b.BellsEmpty}   backwash {b.BackwashCount}");
            sb.AppendLine($"Silt Fall drops {b.DropsLanded}/{b.DropsSpawned}, mounds {b.MoundsSet}/{b.MoundsSpawned}   peal bands {b.PealBands}");
            GUI.Label(new Rect(16, Screen.height * 0.30f, 760, 160), sb.ToString(), style);
        }
    }
}
