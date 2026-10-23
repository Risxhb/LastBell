using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Meridian Towers (aurelion.meridian_towers), abilities-v4/ABILITIES-v4.md §3.1 (proposal). Source: FFXIV towers,
    // L'ura's must-hold objects. Numbered brass columns of light (ivory numeral, cobalt rim, a loud flare when they rise):
    // each needs at least one player inside when it resolves. They resolve in numeral order 2.0 s apart,
    // the first 4.0 s after they appear (Easy 5.0 s, then 2.5 s). A tower's 6 damage (Medium) is split among the
    // players inside (one player taking it all survives from full). An EMPTY tower wipes the party (Total Eclipse; the
    // fight-ending-wipe exception for Aurelion). Solo: 3 towers in a row, N 2: 2, N 3-4: 3, N 5+: 4.
    // Tower spots are authored (a 6.5 m ring round the centre, one of three layouts per cast); consecutive towers are at
    // least 120 degrees apart (11.3 m), so a soloist must run between them: edge to edge 6.0 m at most 6.4 m, 0.68 s
    // spare at 6 m/s walking in the 2.0 s step, 0.94 s at 4.2 m/s with dash + sprint.
    public sealed class MeridianTowers : AurelionAbility
    {
        public override string Id => "aurelion.meridian_towers";
        public override string Name => "Meridian Towers";

        public const float Radius = 2.5f, Ring = 6.5f, FirstS = 4.0f, StepS = 2.0f, FirstEasyS = 5.0f, StepEasyS = 2.5f;
        public const float ReunionR = 4.5f;
        public const int Damage = 6;   // a chip: the wipe is the threat (solo soaks every tower itself, nine in Phases 1-2)
        static readonly Color Ivory = new Color(1f, 0.96f, 0.86f), CobaltFill = new Color(0.18f, 0.28f, 0.62f), CobaltRim = new Color(0.6f, 0.9f, 2.2f);
        static readonly string[] Numerals = { "I", "II", "III", "IV" };
        // authored layouts (yaw round the centre, in resolve order): consecutive >= 120 degrees
        static readonly float[][] Layouts = { new[] { 0f, 135f, 270f, 45f }, new[] { 90f, 210f, 330f, 150f }, new[] { 180f, 300f, 60f, 240f } };

        public sealed class Tower { public Vector3 At; public Hazard H; public int Index, ResolveTick; public readonly List<PlayerController> In = new List<PlayerController>(), Owners = new List<PlayerController>(); }
        public readonly List<Tower> Towers = new List<Tower>();   // test evidence: the last cast
        public int Casts { get; private set; }
        public bool Failed { get; private set; }                 // the last cast had an empty tower (party wiped)

        // the tower this player should be in now (bots, tests): solo, the next one still to resolve; co-op, its own tower
        // (assigned when they rise: party index mod count), or, when every owner of an earlier tower is down, the backup: the
        // nearest standing player covers it; Reunion, the one tower
        public Tower NextFor(PlayerController who, int tick)
        {
            if (Towers.Count == 0 || !who || who.Down) return null;
            var party = Standing();
            if (party.Count == 1 || Towers.Count == 1) return Towers.Find(t => t.ResolveTick >= tick);
            foreach (var t in Towers)
            {
                if (t.ResolveTick < tick) continue;
                bool owned = t.Owners.Exists(p => p && !p.Down);
                if (owned && t.Owners.Contains(who)) return t;
                if (!owned && party.OrderBy(p => Sim.Flat(p.transform.position - t.At).sqrMagnitude).First() == who) return t;
            }
            return null;
        }

        public static int CountFor(int n) => n == 1 ? 3 : n <= 2 ? 2 : n <= 4 ? 3 : 4;   // solo: three in a row
        int First => Sim.Ticks(Easy ? FirstEasyS : FirstS);
        int Step => Sim.Ticks(Easy ? StepEasyS : StepS);

        public IEnumerable<int> Run()
        {
            Say(Name);
            Towers.Clear(); Failed = false;
            int count = CountFor(N);
            var party = Standing();
            var layout = Layouts[Casts++ % Layouts.Length];
            Glow(1.3f); Flare(0.6f);
            var labels = new List<RunHud.WorldLabel>();
            for (int k = 0; k < count; k++)
            {
                var t = new Tower { At = Centre + Sim.Dir(layout[k]) * Ring, Index = k, ResolveTick = W.Tick + First + k * Step - 1 };
                for (int i = k; i < party.Count; i += count) t.Owners.Add(party[i]);   // fixed when they rise
                var tower = t;
                var h = new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = t.At, Radius = Radius,
                    LockAt = 0, ResolveAt = First + k * Step, Damage = Damage, Knockback = 0, Stun = 0, HitOnResolve = false, Soak = true,
                };
                h.OnResolve = hz => Resolve(tower);
                t.H = Spawn(h);
                t.H.Tele?.Tint(CobaltFill, CobaltRim);           // a soak, not a hazard: cobalt, "get in"
                labels.Add(RunHud.Label(t.At + Vector3.up * 0.4f, Numerals[k], Ivory, 54));
                Towers.Add(t);
            }
            int last = First + (count - 1) * Step - 1;
            for (int i = 0; i < last; i++)
            {
                for (int k = 0; k < Towers.Count; k++) if (W.Tick > Towers[k].ResolveTick) labels[k].Hide();
                yield return 1;
            }
            foreach (var l in labels) l.Hide();
        }

        // Reunion (Severance, co-op): one tower at the centre (4.5 m: he stands in it, so a 3 m tower left a 0.5 m strip round
        // his body for the whole party), 3.0 s; everyone should be inside. An empty one wipes; each
        // standing player left outside takes a heavy hit (50).
        public IEnumerable<int> RunReunion()
        {
            Say("Reunion");
            Towers.Clear(); Failed = false;
            int at = Sim.Ticks(Easy ? 3.75f : 3.0f);
            var t = new Tower { At = Centre, Index = 0, ResolveTick = W.Tick + at - 1 };
            var h = new Hazard { Source = "Reunion", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Centre, Radius = ReunionR, LockAt = 0, ResolveAt = at, Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false, Soak = true };
            h.OnResolve = hz =>
            {
                t.In.Clear();
                foreach (var p in W.Players) if (p && !p.Down && hz.Contains(p.transform.position)) t.In.Add(p);
                if (t.In.Count == 0) { Failed = true; Boss.Wipe("Reunion — nobody came back"); return; }
                foreach (var p in W.Players)
                    if (p && !p.Down && !t.In.Contains(p)) p.ReceiveHit(new Hazard { Source = "Reunion", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Damage = 50, Knockback = 0, Stun = 15 });
                W.BossFx.ChimeResolve(Centre, ReunionR);
            };
            t.H = Spawn(h); t.H.Tele?.Tint(CobaltFill, CobaltRim);
            var label = RunHud.Label(Centre + Vector3.up * 0.4f, "REUNION", Ivory, 44);
            Towers.Add(t);
            yield return at - 1;
            label.Hide();
        }

        // ticks from Run's first tick to the last tower's resolve tick. CONTRACT: keep it equal to Run.
        public int Duration() => First + (CountFor(N) - 1) * Step - 1;

        void Resolve(Tower t)
        {
            t.In.Clear();
            foreach (var p in W.Players) if (p && !p.Down && t.H.Contains(p.transform.position)) t.In.Add(p);
            if (t.In.Count == 0) { Failed = true; Boss.Wipe("Total Eclipse — tower " + Numerals[t.Index] + " was empty"); return; }
            int share = Mathf.CeilToInt(t.H.Damage / (float)t.In.Count);   // one player taking it all survives from full
            foreach (var p in t.In)
                p.ReceiveHit(new Hazard { Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = t.At, Damage = share, Knockback = 0, Stun = 0 });
            W.BossFx.ChimeResolve(t.At, Radius);
        }
    }
}
