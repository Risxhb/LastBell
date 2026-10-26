using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Total Eclipse (aurelion.total_eclipse), the intermission at 70 %, abilities-v4/ABILITIES-v4.md §3.4 (proposal).
    // Sources: Dimensius's Event Horizon (survive in the safe zones), Ansurek's intermission shield. He stays targetable.
    //   - Corona Shield: absorbs 700 per fighting player (AurelionEncounter.Absorb): solo that is 700 in 25 s, 28 DPS while
    //     moving between wells (a ranged class does about 60 standing, RunAutoTest V4Dps). Break it within 25 s or Total
    //     Eclipse wipes the party (the damage-check / enrage exception). Breaking it ends the intermission at once.
    //   - Light wells: 4 cobalt-rimmed circles of 3.5 m (6 of 3.0 m at N >= 5) orbit the centre at 6 m, 15 degrees/s.
    //     Umbra: a player outside every well for more than 2.0 s straight takes 10 every second after that (chip; it can't
    //     kill fast, it drains potions).
    //   - Meanwhile Meridian Lances (3 beams) every 6 s and Starfall Mirror after 2 s: with the Umbra, three patterns.
    public sealed class TotalEclipse : AurelionAbility
    {
        public override string Id => "aurelion.total_eclipse";
        public override string Name => "Total Eclipse";

        public const int ShieldPerFighter = 700;   // Chzy18 2026-10-23: sized by party, not HP (groups broke 3.5 % of HP in ~10 s)
        public const float LimitS = 25f, WellOrbit = 6f, WellSpin = 15f, UmbraGraceS = 2.0f;
        public const int UmbraDamage = 10;
        static readonly Color WellFill = new Color(0.22f, 0.32f, 0.7f), WellRim = new Color(0.9f, 1.2f, 2.4f);

        // 700 per fighting player (players, and bot allies only when they attack): about 22 s for a party of 4 at the damage the
        // perfect bots deal while dodging, 28 damage per second solo
        public int ShieldFor() { int f = 0; foreach (var p in W.Players) if (p && !p.Down && (!(p.Input is AllyBot) || AllyBot.Attacks)) f++; return ShieldPerFighter * Mathf.Max(1, f); }
        public float WellRadius => N >= 5 ? 3.0f : 3.5f;   // a star (2.5 m with its edge) on you must not fill the whole well
        public int WellCount => N >= 5 ? 6 : 4;
        public readonly List<Vector3> Wells = new List<Vector3>();   // current well centres (tests, bots)
        public bool Active { get; private set; }
        public bool Broken { get; private set; }
        public int UmbraHits { get; private set; }
        readonly Dictionary<PlayerController, int> dark = new Dictionary<PlayerController, int>();

        public bool InWell(Vector3 p) { foreach (var c in Wells) if (Sim.Flat(p - c).magnitude <= WellRadius - 0.1f) return true; return false; }

        public IEnumerable<int> Run()
        {
            Say(Name);
            Active = true; Broken = false; UmbraHits = 0; dark.Clear();
            Boss.RaiseShield(ShieldFor());
            RunHud.Banner("TOTAL ECLIPSE — BREAK THE CORONA", new Color(1f, 0.85f, 0.5f), 2.5f);
            Glow(2f); Flare(1.5f);
            var rings = new List<BellkeeperFx.Telegraph>();
            for (int i = 0; i < WellCount; i++) { var r = W.BossFx.Ring(WellRadius - 0.15f, WellRadius); r.Tint(WellFill, WellRim); r.Progress(1); rings.Add(r); Wells.Add(Vector3.zero); }

            // the other patterns run on their own schedule, stepped here one tick at a time
            var side = Schedule().GetEnumerator(); int sideWait = 0; bool sideOn = true;
            int limit = Sim.Ticks(LimitS);
            for (int t = 0; t < limit; t++)
            {
                float a0 = t * Sim.Dt * WellSpin;
                for (int i = 0; i < Wells.Count; i++) { Wells[i] = Centre + Sim.Dir(a0 + 360f * i / Wells.Count) * WellOrbit; rings[i].Place(Wells[i], 0); }
                Umbra();
                if (sideOn) { if (sideWait > 0) sideWait--; else if (side.MoveNext()) sideWait = Mathf.Max(1, side.Current) - 1; else sideOn = false; }
                if (Boss.ShieldHp <= 0) { Broken = true; break; }
                yield return 1;
            }
            foreach (var r in rings) r.Expire();
            Wells.Clear(); Active = false;
            if (Broken) { RunHud.Banner("CORONA BROKEN", new Color(0.7f, 0.9f, 1f), 1.5f); Flare(2f); }
            else Boss.Wipe("Total Eclipse — the Corona Shield held");
            Boss.RaiseShield(0);
        }

        IEnumerable<int> Schedule()
        {
            yield return Sim.Ticks(2f);
            foreach (var w in AurelionEncounter.Together(Boss.Starfall.Run(true), Sim.Ticks(1f), Boss.Lances.Run(3, 1, false))) yield return w;
            while (true)
            {
                yield return Sim.Ticks(2.5f);
                foreach (var w in Boss.Lances.Run(3, 1, false)) yield return w;
            }
        }

        void Umbra()
        {
            foreach (var p in W.Players)
            {
                if (!p || p.Down) continue;
                dark.TryGetValue(p, out int d);
                d = InWell(p.transform.position) ? 0 : d + 1;
                dark[p] = d;
                int grace = Sim.Ticks(UmbraGraceS);
                if (d > grace && (d - grace) % Sim.Hz == 1)
                {
                    UmbraHits++;
                    p.ReceiveHit(new Hazard { Source = "Umbra", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Damage = UmbraDamage, Knockback = 0, Stun = 0 });
                }
            }
        }
    }
}
