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
        GUIStyle style, numeral, shadow;
        readonly StringBuilder sb = new StringBuilder();
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
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false };
            style.normal.textColor = new Color(0.85f, 0.95f, 1f);
            sb.Clear();
            sb.AppendLine("[DEV] Vesper Seedkeeper (F7, placeholder)");
            sb.AppendLine($"{(b.Phase2 ? "Phase 2" : "Phase 1")}   {b.Level}   block {b.Blocks}");
            sb.AppendLine("Now: " + b.Current);
            sb.AppendLine($"Cords severed {b.CordsSevered} missed {b.CordsMissed} lane hits {b.CordLaneHits} surge hits {b.SurgeHits}");
            sb.AppendLine($"Seeds planted {b.SeedsPlanted} withered {b.SeedsWithered} withdrawn {b.SeedsWithdrawn}   blooms {b.Blooms.Count}");
            sb.AppendLine($"Nightfall lit {b.NightLit} dark {b.NightDark} guttered {b.NightGuttered} umbra {b.UmbraHits} late bloom {b.LateBloomHits}");
            sb.AppendLine($"Tide ebb hits {b.EbbHits} flow hits {b.FlowHits}   punish windows {b.PunishLog.Count}");
            GUI.Label(new Rect(16, Screen.height * 0.30f, 760, 160), sb.ToString(), style);
        }

        void Numerals()
        {
            var w = World; var cam = Camera.main;
            if (w == null || cam == null || w.Boss != b) return;
            numeral ??= new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.04f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            shadow ??= new GUIStyle(numeral);
            numeral.normal.textColor = new Color(1f, 0.96f, 0.86f); shadow.normal.textColor = new Color(0, 0, 0, 0.7f);
            var live = w.Hazards.Live; int shownReplica = 0;
            for (int i = 0; i < live.Count; i++)
            {
                var h = live[i];
                if (h == null || h.Cancelled || !h.Soak || h.ShareCount <= 0 || h.Age >= h.ResolveAt) continue;
                if (h.Replica) shownReplica++;
                var s = cam.WorldToScreenPoint(h.Origin + Vector3.up * 0.2f);
                if (s.z <= 0) continue;
                var r = new Rect(s.x - 40, Screen.height - s.y - 30, 80, 60);
                string txt = h.ShareCount.ToString();
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), txt, shadow);
                GUI.Label(r, txt, numeral);
            }
            if (shownReplica > ReplicaNumeralsShown) ReplicaNumeralsShown = shownReplica;
        }
    }

    // LAN client: a Vesper cord view. The host sends the cord at its stake; this stretches the glowing bar from the replica
    // vessel (flat, 1 m up, as the host draws it) to the stake every frame.
    public sealed class VesperNetCord : MonoBehaviour
    {
        Transform boss, bar;
        public void Bind(Transform vessel, Transform view) { boss = vessel; bar = view; }
        void LateUpdate()
        {
            if (!bar || !boss) return;
            var from = Sim.Flat(boss.position); var to = Sim.Flat(transform.position);
            var d = to - from;
            bar.position = (from + to) / 2 + Vector3.up * 1.0f;
            bar.rotation = Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d : Vector3.forward);
            bar.localScale = new Vector3(0.08f, 0.08f, d.magnitude);
        }
    }
}
