using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Last Light (aurelion.last_light), Phase 4's floor fill, abilities-v4/ABILITIES-v4.md §3.6 (proposal). Sources:
    // Ansurek's room-filling silk, Dimensius's stage 3 pressure. Every 12 s (10 s from the fifth) a shadow sector (a 45-degree wedge from the
    // centre past the rim, Ground) darkens for good and burns 25/s (Hazard.BurnPerSecond). The order is authored so the
    // lit floor stays one connected piece as long as possible. When the eighth sector falls the floor is gone: Final
    // Noon wipes the party (88 s into Phase 4); each fallen slice makes all his attacks 10 % stronger. Run() returns only by wiping, so the encounter runs it beside the
    // Phase 4 rotation (AurelionEncounter.Together).
    public sealed class LastLight : AurelionAbility
    {
        public override string Id => "aurelion.last_light";
        public override string Name => "Last Light";

        // the soft enrage (Chzy18 2026-10-23): slices fall every 12 s, then every 10 s from the fifth; each fallen slice adds
        // DamagePerSlice to all his attacks (AurelionAbility.Spawn). The room is gone 88 s into the phase.
        public const float EveryS = 12f, FastS = 10f, WarnS = 3.0f, Burn = 25f, Half = 22.5f, DamagePerSlice = 0.10f;
        public const int FastFrom = 4;
        public float DamageScale => 1f + DamagePerSlice * Fallen;
        public static readonly float[] Order = { 0f, 180f, 90f, 270f, 45f, 225f, 135f, 315f };
        public readonly List<Hazard> Sectors = new List<Hazard>();
        public int Fallen => Sectors.Count;

        public IEnumerable<int> Run()
        {
            Sectors.Clear();
            RunHud.Banner("LAST LIGHT — THE FLOOR IS FALLING", new Color(1f, 0.6f, 0.5f), 2.5f);
            for (int k = 0; k < Order.Length; k++)
            {
                yield return Sim.Ticks(k < FastFrom ? EveryS : FastS) - Sim.Ticks(WarnS);
                // 3 s of warning: the wedge locks at once and starts burning when the warning ends (LockAt)
                var h = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Centre, Yaw = Order[k], HalfAngle = Half,
                    Radius = Rim + 0.5f, LockAt = Sim.Ticks(WarnS), ResolveAt = Sim.Ticks(600f), BurnPerSecond = Burn, Knockback = 0,
                });
                Sectors.Add(h); Boss.PhasePools.Add(h);
                yield return Sim.Ticks(WarnS);
                Flare(0.8f);
                RunHud.Banner($"THE LIGHT WANES — HIS ATTACKS +{Mathf.RoundToInt(DamagePerSlice * 100 * Fallen)} %", new Color(1f, 0.6f, 0.5f), 1.5f);
            }
            Boss.Wipe("Final Noon — the last light went out");
            while (true) yield return 60;
        }
    }
}
