using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Last Light (aurelion.last_light), Phase 4's floor fill, abilities-v4/ABILITIES-v4.md §3.6 (proposal). Sources:
    // Ansurek's room-filling silk, Dimensius's stage 3 pressure. Every 12 s (10 s from the fifth) a shadow sector (a 45-degree wedge from the
    // centre past the rim, Ground) darkens for good and falls into the sky (accepted 1 Nov: stepping in drops you, see StepFalls; it burned 25/s before). The order is authored so the
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
        public Transform BossBody => Body;   // presentation: the camera frames him during the phase (LastLightFloorFx)

        // ---- the fall (Chzy18 picked "B: Fall" over the 25/s burn and accepted 40 damage, review inbox, 1 Nov) ----
        // Stepping onto a fallen slice drops you: you land back on the nearest standing floor at once and take FallDamage
        // (before the difficulty scale: Easy 24, Medium 40, Hard 52), never a one-shot from full health; two falls in a
        // row can kill. No way across a hole (no jumping in the game, Chzy18 2 Nov; a dash does not carry you over): walk round it.
        // The wedge keeps its burn rate for its look and for ally-bot avoidance, but in fall mode nobody stands in it
        // long enough to burn (the fall moves you out on the tick you step in). The old burn-only mode was removed on 2 Nov.
        public const int FallDamage = 40;
        public const string FallSource = "Last Light — the fall";
        public static int FallStun => Sim.Ticks(0.6f);
        public const float Footing = 1.0f;   // the apex where all eight wedges meet stays solid this far out
        public const float Clearance = 1.0f; // you land at least this far from every hole edge
        public readonly List<(PlayerController p, Vector3 from, Vector3 to, int damage)> Falls = new List<(PlayerController, Vector3, Vector3, int)>();

        // over a fallen (locked) slice, by the body centre; clear widens each slice by that many metres at the point
        public bool OverHole(Vector3 pos, float clear = 0)
        {
            var v = Sim.Flat(pos - Centre); float d = v.magnitude;
            var live = W.Hazards.Live;
            if (d < Footing) return false;
            foreach (var h in Sectors)
                if (h.Locked && !h.Cancelled && live.Contains(h) && Vector3.Angle(Sim.Dir(h.Yaw), v) <= Half + Mathf.Rad2Deg * Mathf.Atan2(clear, d)) return true;
            return false;
        }

        // the nearest standing floor, Clearance from every hole and inside the rim
        public Vector3 Landing(Vector3 from)
        {
            Vector3 best = Centre; float bestD = float.MaxValue;
            for (float r = Footing + 0.5f; r <= Rim - 0.5f; r += 0.5f)
                for (int a = 0; a < 360; a += 3)
                {
                    var c = Centre + Sim.Dir(a) * r;
                    if (OverHole(c, Clearance)) continue;
                    float d = Sim.Flat(c - from).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = c; }
                }
            return best;
        }

        // every tick (AurelionEncounter.StepAlways) while v4 runs
        public void StepFalls()
        {
            if (Sectors.Count == 0) return;
            foreach (var p in W.Players)
            {
                if (!p || p.Down || !OverHole(p.transform.position)) continue;
                var from = p.transform.position; var to = Landing(from);
                p.Relocate(to);
                int before = p.Hp;
                p.ReceiveHit(new Hazard { Source = FallSource, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = to, Radius = 0.1f, Damage = FallDamage, Stun = FallStun, Knockback = 0 });
                Falls.Add((p, from, to, before - p.Hp));
                RunHud.Float(to + Vector3.up * 3.0f, "FELL", new Color(0.7f, 0.85f, 1f));
            }
        }

        public IEnumerable<int> Run()
        {
            Sectors.Clear(); Falls.Clear();
            RunHud.Banner("LAST LIGHT — THE FLOOR IS FALLING", new Color(1f, 0.6f, 0.5f), 2.5f);
            for (int k = 0; k < Order.Length; k++)
            {
                yield return Sim.Ticks(k < FastFrom ? EveryS : FastS) - Sim.Ticks(WarnS);
                // 3 s of warning: the wedge locks at once and starts burning when the warning ends (LockAt)
                var h = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Cone, Kind = HazardKind.Ground, Origin = Centre, Yaw = Order[k], HalfAngle = Half,
                    Radius = Rim + 0.5f, LockAt = Sim.Ticks(WarnS), ResolveAt = Sim.Ticks(600f), BurnPerSecond = Burn, Knockback = 0, Pit = true,
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
