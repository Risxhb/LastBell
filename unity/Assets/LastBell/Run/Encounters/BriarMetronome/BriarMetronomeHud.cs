using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Development-only diagnostics for the Briar Metronome. The fight shows players no ability text: this overlay is OFF
    // by default and only F7 (or the acceptance test) turns it on.
    public sealed class BriarMetronomeHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        BriarMetronomeEncounter b;
        readonly StringBuilder sb = new StringBuilder();

        public void Bind(BriarMetronomeEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!ShowDiagnostics || !b || b.Dead || RunHud.LanClientView) return;   // dev overlay: host only (a LAN client's replica is never stepped)
            GUI.depth = 10;
            sb.Clear();
            sb.AppendLine("[DEV] Briar Metronome (F7, placeholder)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Bpm:0} BPM   beat {b.BeatIndex}   {b.Level}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Cuts {b.CutsLanded} (hits {b.CutHits})   collar hits {b.CollarHits}   hedge hits {b.HedgeHits}   rooted windows {b.RootedLog.Count}");
            sb.AppendLine($"Grafts cut {b.GraftsCut} bloomed {b.GraftsBloomed} alive {b.GraftsAlive} (held hits {b.GraftHeld})   seeds {b.SeedsBurst} (hits {b.SeedHits})   weights {b.WeightsLanded} (hits {b.WeightHits})");
            EncounterCallouts.Diagnostics(sb.ToString(), 900);
        }
    }
}
