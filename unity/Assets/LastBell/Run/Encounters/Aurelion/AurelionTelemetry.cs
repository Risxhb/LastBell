using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Per-fight telemetry for Aurelion (abilities-v3 HARDER-PROMPT §2, Step 0). Lives on the boss's GameObject from his
    // first script step (v2 and v3 sets) and logs:
    //   - every hit taken, by ability (the hazard's Source)
    //   - every resolution's escape margin per affected standing player: the signed distance from the player to the
    //     shape's edge (hit test margin included) at the resolve tick, divided by 6 m/s; negative = inside (hit)
    //   - the time each phase started, and the potions drunk
    // When the boss is destroyed (the fight ends) it appends the rows to Application.persistentDataPath/
    // aurelion-telemetry.csv; autotest runs skip the file and read the in-memory log instead (Summary()).
    public sealed class AurelionTelemetry : MonoBehaviour
    {
        public const float DesignWalk = 6f;
        public sealed class Row { public string Kind, Source, Player; public float Value; public int Tick; }
        public readonly List<Row> Rows = new List<Row>();
        CombatWorld w; AurelionEncounter boss; int startTick; string set;

        public void Bind(AurelionEncounter b, CombatWorld world)
        {
            boss = b; w = world; startTick = w.Tick;
            set = AurelionArgs.V1 ? "v1" : AurelionArgs.V3 ? "v3" : "v2.1";
            w.PlayerHit += OnHit;
            w.Hazards.Resolving += OnResolving;
            boss.PhaseChanged += OnPhase;
            Add("phase", boss.PhaseName, "", 0);
        }

        public void Clear() { Rows.Clear(); startTick = w != null ? w.Tick : 0; }

        void Add(string kind, string source, string player, float value) =>
            Rows.Add(new Row { Kind = kind, Source = source, Player = player, Value = value, Tick = w != null ? w.Tick : 0 });

        string Who(PlayerController p) => p ? p.ClassName + "#" + w.Players.IndexOf(p) : "?";
        void OnHit(PlayerController p, Hazard h, int dmg) => Add("hit", h.Source, Who(p), dmg);
        void OnPhase(BossEncounter b, string name) => Add("phase", name, "", (w.Tick - startTick) / (float)Sim.Hz);

        void OnResolving(Hazard h)
        {
            if (!h.HitOnResolve || h.BurnPerSecond > 0) return;
            foreach (var p in w.Players)
                if (p && !p.Down && (h.Affects == null || h.Affects(p)))
                    Add("margin", h.Source, Who(p), Signed(h, p.transform.position) / DesignWalk);
        }

        // signed distance (m) from p to the hit shape's edge, hit-test margin included: > 0 outside (safe), < 0 inside
        public static float Signed(Hazard h, Vector3 p)
        {
            const float M = Hazard.Margin;
            var v = Sim.Flat(p - h.Origin); float d = v.magnitude;
            switch (h.Shape)
            {
                case HazardShape.Circle: return d - (h.Radius + M);
                case HazardShape.Ring: return Mathf.Max(h.Inner - M - d, d - (h.Radius + M));
                case HazardShape.Cone:
                {
                    if (d < 0.6f) return d - 0.6f;
                    float off = Vector3.Angle(Sim.Dir(h.Yaw), v) - h.HalfAngle;
                    float side = off >= 90 ? d : d * Mathf.Sin(off * Mathf.Deg2Rad);   // < 0 inside the arc
                    return Mathf.Max(d - (h.Radius + M), side - M);
                }
                default:
                {
                    var l = Quaternion.Euler(0, -h.Yaw, 0) * v;
                    float qx = Mathf.Abs(l.x) - (h.Width / 2 + M), qz = Mathf.Max(-M - l.z, l.z - (h.Length + M));
                    return new Vector2(Mathf.Max(qx, 0), Mathf.Max(qz, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qz), 0);
                }
            }
        }

        // human-readable lines for the autotest report
        public IEnumerable<string> Summary()
        {
            var hits = Rows.Where(r => r.Kind == "hit").GroupBy(r => r.Source).Select(g => $"{g.Key} {g.Count()} ({g.Sum(r => r.Value):0} dmg)");
            yield return "hits by ability: " + (hits.Any() ? string.Join(", ", hits) : "none");
            var margins = Rows.Where(r => r.Kind == "margin").GroupBy(r => r.Source)
                .Select(g => $"{g.Key} min {g.Min(r => r.Value):0.00} s / median {g.Select(r => r.Value).OrderBy(x => x).ElementAt(g.Count() / 2):0.00} s (n {g.Count()})");
            yield return "escape margins at resolve: " + string.Join(", ", margins);
            yield return "phases: " + string.Join(", ", Rows.Where(r => r.Kind == "phase").Select(r => $"{r.Source} @{r.Value:0.0}s"));
            if (w != null) yield return "potions drunk: " + string.Join(", ", w.Players.Where(p => p).Select(p => p.ClassName + " " + p.PotionsDrunk));
        }

        void OnDestroy()
        {
            if (w != null) { w.PlayerHit -= OnHit; w.Hazards.Resolving -= OnResolving; }
            if (boss != null) boss.PhaseChanged -= OnPhase;
            if (RunAutoTest.Requested || Rows.Count == 0) return;
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "aurelion-telemetry.csv");
                bool fresh = !File.Exists(path);
                string run = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), diff = w != null ? w.Difficulty.ToString() : "";
                var lines = new List<string>();
                if (fresh) lines.Add("run,set,difficulty,kind,source,player,value,tick");
                foreach (var r in Rows) lines.Add($"{run},{set},{diff},{r.Kind},{r.Source},{r.Player},{r.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{r.Tick}");
                if (w != null) foreach (var p in w.Players) if (p) lines.Add($"{run},{set},{diff},potions,,{Who(p)},{p.PotionsDrunk},{w.Tick}");
                File.AppendAllLines(path, lines);
            }
            catch (Exception e) { Debug.LogWarning("[aurelion telemetry] " + e.Message); }
        }
    }
}
