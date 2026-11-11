using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Development-only diagnostics for the Pendulum Sentinel. The fight shows players no ability text: this overlay is
    // OFF by default and only F7 (or the acceptance test) turns it on, so removing it later changes nothing players see.
    public sealed class PendulumSentinelHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        PendulumSentinelEncounter b;
        readonly StringBuilder sb = new StringBuilder();

        public void Bind(PendulumSentinelEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!ShowDiagnostics || !b || b.Dead) return;
            GUI.depth = 10;
            sb.Clear();
            sb.AppendLine("[DEV] Pendulum Sentinel (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Bpm:0} BPM   beat {b.BeatIndex}   {b.Level}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Swings hit {b.SwingHits} (+{b.BackswingHits} backswing)   weights absorbed {b.WeightCatches} burst {b.WeightMisses} lying {b.LyingCount}   orbs {b.OrbCount} live, {b.OrbBounces} bounces, {b.OrbHits} hits");
            sb.AppendLine($"Plumb hits {b.PlumbHits} (cracked marks {b.PlumbCracks})   stroke hits {b.StrokeHits}, cleave {b.CleaveHits}   stuck windows {b.StuckLog.Count}");
            EncounterCallouts.Diagnostics(sb.ToString(), 760);
        }
    }
}
