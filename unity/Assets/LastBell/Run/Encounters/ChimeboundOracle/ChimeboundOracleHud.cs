using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Development-only diagnostics for the Chimebound Oracle. The fight shows players no ability text: this overlay is
    // OFF by default and only F7 (or the acceptance test) turns it on, so removing it later changes nothing players see.
    public sealed class ChimeboundOracleHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        ChimeboundOracleEncounter b;
        GUIStyle style;
        readonly StringBuilder sb = new StringBuilder();

        public void Bind(ChimeboundOracleEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!ShowDiagnostics || !b || b.Dead) return;
            GUI.depth = 10;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
            style.normal.textColor = new Color(0.85f, 0.95f, 1f);
            sb.Clear();
            sb.AppendLine("[DEV] Chimebound Oracle (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}{(b.HoldHp ? " (HP held)" : "")}{(b.Enraged ? "  LAST BELL" : "")}   {b.Clock.State} {b.Clock.Bpm:0} BPM   beat {b.Clock.Beat:0.0}");
            sb.AppendLine("Now: " + b.Current);
            if (b.LastPattern != null)
            {
                sb.Append("Lanes: " + b.LastPatternName + "  ");
                foreach (var s in b.LastPattern) sb.Append(s.B >= 0 ? $"[{s.A + 1}+{s.B + 1}] " : $"[{s.A + 1}] ");
                sb.AppendLine();
            }
            if (b.ChainActive) sb.AppendLine($"Soak chain {b.ChainIndex + 1}/{b.ChainLength}   soaked {b.Soaks}  missed {b.Misses}");
            sb.AppendLine($"Orbs live {b.OrbCount}   marks {b.ActiveMarks}   lane hits {b.LaneHits}  orb hits {b.OrbHits}");
            GUI.Label(new Rect(16, Screen.height * 0.30f, 700, 160), sb.ToString(), style);
        }
    }
}
