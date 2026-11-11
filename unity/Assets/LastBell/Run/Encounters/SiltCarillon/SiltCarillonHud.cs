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
                foreach (var (at, need) in shown)
                {
                    var s = cam.WorldToScreenPoint(at + Vector3.up * 1.2f);
                    if (s.z <= 0) continue;
                    string t = need >= 0 && need < Roman.Length ? Roman[need] : need.ToString();
                    EncounterCallouts.Numeral(new Vector2(s.x, Screen.height - s.y), t);
                }
            }
            if (!ShowDiagnostics) return;
            sb.Clear();
            sb.AppendLine("[DEV] Silt Carillon (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Bpm:0} BPM   beat {b.BeatIndex}   {b.Level}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Tides {b.TideCasts} (mud live {b.TideLive}, drained early {b.DrainedLog.Count})   bells rung {b.BellsRung}, empty {b.BellsEmpty}   backwash {b.BackwashCount}");
            sb.AppendLine($"Silt Fall drops {b.DropsLanded}/{b.DropsSpawned}, mounds {b.MoundsSet}/{b.MoundsSpawned}   peal bands {b.PealBands}");
            EncounterCallouts.Diagnostics(sb.ToString(), 760);
        }
    }
}
