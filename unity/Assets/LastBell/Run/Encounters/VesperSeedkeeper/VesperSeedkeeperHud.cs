using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastBell.Run
{
    // Vesper Seedkeeper presentation helpers (placeholder):
    // - soak numerals: every live soak of this fight (stakes, seeds, blooms) shows how many players it wants, large, over
    //   its centre (content rules §5: a soak's telegraph must be impossible to miss). Identity only, no ability text.
    // - F7 diagnostics, OFF by default (the fight shows players no ability names).
    public sealed class VesperSeedkeeperHud : MonoBehaviour
    {
        public static bool ShowDiagnostics;
        public static int ReplicaNumeralsShown;   // LAN test: most soak numerals a client drew at once from hazard stand-ins
        VesperSeedkeeperEncounter b;
        readonly StringBuilder sb = new StringBuilder();
        static readonly string[] Numbers = { "0", "1", "2", "3", "4", "5", "6", "7", "8" };   // no per-frame ToString
        CombatWorld World => RunDirector.Instance ? RunDirector.Instance.World : null;

        public void Bind(VesperSeedkeeperEncounter boss) { b = boss; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) ShowDiagnostics = !ShowDiagnostics;
        }

        void OnGUI()
        {
            if (!b || b.Dead) return;
            GUI.depth = 10;
            Numerals();
            if (!ShowDiagnostics) return;
            sb.Clear();
            sb.AppendLine("[DEV] Vesper Seedkeeper (F7, placeholder)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Level}   block {b.Blocks}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Cords severed {b.CordsSevered} missed {b.CordsMissed} lane hits {b.CordLaneHits} surge hits {b.SurgeHits}");
            sb.AppendLine($"Seeds planted {b.SeedsPlanted} withered {b.SeedsWithered} withdrawn {b.SeedsWithdrawn}   blooms {b.Blooms.Count}");
            sb.AppendLine($"Nightfall lit {b.NightLit} dark {b.NightDark} guttered {b.NightGuttered} umbra {b.UmbraHits} late bloom {b.LateBloomHits}");
            sb.AppendLine($"Tide ebb hits {b.EbbHits} flow hits {b.FlowHits}   punish windows {b.PunishLog.Count}");
            EncounterCallouts.Diagnostics(sb.ToString(), 760);
        }

        void Numerals()
        {
            var w = World; var cam = Camera.main;
            if (w == null || cam == null || w.Boss != b) return;
            var live = w.Hazards.Live; int shownReplica = 0;
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h == null || h.Cancelled || !h.Soak || h.ShareCount <= 0 || h.Age >= h.ResolveAt) continue;
                if (h.Replica) shownReplica++;
                var s = cam.WorldToScreenPoint(h.Origin + Vector3.up * 0.2f);
                if (s.z <= 0) continue;
                EncounterCallouts.Numeral(new Vector2(s.x, Screen.height - s.y), Numbers[Mathf.Min(h.ShareCount, Numbers.Length - 1)], 1, EncounterCallouts.Styles.NumeralSmall);
            }
            if (shownReplica > ReplicaNumeralsShown) ReplicaNumeralsShown = shownReplica;
        }
    }
}
