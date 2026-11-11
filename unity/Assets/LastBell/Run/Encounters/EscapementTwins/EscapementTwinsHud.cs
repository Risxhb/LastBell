using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Development-only diagnostics for the Escapement Twins. Players see no ability text: this overlay is OFF by default
    // and only F7 (or the acceptance test) turns it on, so removing it later changes nothing players see.
    public sealed class EscapementTwinsHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        EscapementTwinsEncounter b;
        readonly StringBuilder sb = new StringBuilder();

        public void Bind(EscapementTwinsEncounter boss) { b = boss; }

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
            sb.AppendLine("[DEV] Escapement Twins (F7)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Bpm:0} BPM   beat {b.BeatIndex}   axis {b.Axis:0}   {b.Level}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Pallet Lock: {b.LocksResolved} locks, {b.Swaps} swaps, {b.LockHits} hits   Escape Wheel: {b.VolleysResolved} volleys, {b.SpokeHits} hits");
            sb.AppendLine($"Counterpoise: {b.SoaksResolved} circles, {b.SoakPlayers} soakers, {b.SoakEmpty} empty   Lock and Release: lane {b.LaneHits}, shock {b.ShockHits}, landing {b.LandHits}, recoil {b.RecoilHits}, punish windows {b.PunishLog.Count}");
            EncounterCallouts.Diagnostics(sb.ToString(), 820);
        }
    }
}
