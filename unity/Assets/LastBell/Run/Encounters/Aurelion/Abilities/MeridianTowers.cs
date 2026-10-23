using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Meridian Towers (aurelion.meridian_towers), abilities-v4/ABILITIES-v4.md §3.1 (proposal; tower redesign, Chzy18
    // 2026-10-23: "just one person has to be in it", "no unavoidable damage"). Source: FFXIV towers with a soak count.
    // Each tower shows how many players it needs (ivory dots; solo towers show their order numeral). A tower OPENS 3.0 s
    // before it lands; while open, a gold ring fills whenever at least that many players stand inside, and stops (not
    // resets) when they don't. It needs 1.5 s of filling (Easy: open 3.75 s, same 1.5 s).
    //   - Filled when it lands: no damage at all. Players beyond the count take a heavy hit (50): the extras, farthest
    //     from its centre first, so a party can't just pile into one.
    //   - Not filled: Total Eclipse, the party wipes (the fight-ending-wipe exception; telegraph: the big cobalt column,
    //     the dots and the ring).
    //   - Solo: 3 towers of one, in order, 2.5 s apart (first 5.0 s after they rise): fill each early and leave before it
    //     lands. Co-op: 2-3 towers that land together, their counts adding up to the party (N 2: 1+1, N 4: 2+2 or 1+3,
    //     N 8: 3+3+2), assigned when they rise in party order. A tower whose players have all gone down goes dark (no wipe).
    // Spots: a 6.5 m ring round the centre, one of three authored layouts per cast, towers at least 120 degrees apart.
    public sealed class MeridianTowers : AurelionAbility
    {
        public override string Id => "aurelion.meridian_towers";
        public override string Name => "Meridian Towers";

        public const float Radius = 2.5f, Ring = 6.5f, FirstS = 5.0f, StepS = 2.5f, FirstEasyS = 6.0f, StepEasyS = 3.0f;
        public const float OpenS = 5.0f, OpenEasyS = 6.0f, HoldS = 1.5f;   // open from the moment they rise (solo: later towers open on rise too)
        public const float ReunionR = 4.5f;
        public const int ExtraDamage = 50;
        static readonly Color Ivory = new Color(1f, 0.96f, 0.86f), CobaltFill = new Color(0.18f, 0.28f, 0.62f), CobaltRim = new Color(0.6f, 0.9f, 2.2f);
        static readonly Color GoldFill = new Color(0.6f, 0.45f, 0.15f), GoldRim = new Color(2.2f, 1.7f, 0.6f);
        static readonly string[] Numerals = { "I", "II", "III", "IV" };
        // authored layouts (yaw round the centre): consecutive >= 120 degrees
        static readonly float[][] Layouts = { new[] { 0f, 135f, 270f, 45f }, new[] { 90f, 210f, 330f, 150f }, new[] { 180f, 300f, 60f, 240f } };

        public sealed class Tower
        {
            public Vector3 At; public Hazard H; public int Index, ResolveTick, Need, Held;
            public readonly List<PlayerController> In = new List<PlayerController>(), Owners = new List<PlayerController>();
            internal LastBell.VFX.BellkeeperFx.Telegraph Fill;
            public bool Resolved;
        }
        public readonly List<Tower> Towers = new List<Tower>();   // test evidence: the last cast
        public int Casts { get; private set; }
        public bool Failed { get; private set; }                 // the last cast had a tower not held (party wiped)
        public int HoldTicks => Sim.Ticks(HoldS);
        int OpenTicks => Sim.Ticks(Easy ? OpenEasyS : OpenS);
        public bool IsOpen(Tower t, int tick) => !t.Resolved && tick >= t.ResolveTick - OpenTicks + 1;

        // the counts per party size (co-op towers land together); N 4 alternates 2+2 and 1+3
        public static int[] NeedsFor(int n, int cast)
        {
            switch (n)
            {
                case 1: return new[] { 1, 1, 1 };
                case 2: return new[] { 1, 1 };
                case 3: return new[] { 1, 2 };
                case 4: return cast % 2 == 0 ? new[] { 2, 2 } : new[] { 1, 3 };
                case 5: return new[] { 2, 3 };
                case 6: return new[] { 3, 3 };
                case 7: return new[] { 2, 2, 3 };
                default: return new[] { 3, 3, 2 };
            }
        }

        // the tower this player should be in now (bots, tests): solo, the next one not yet filled; co-op, its own tower
        // while unfilled (assigned when they rise); Reunion, the one tower
        public Tower NextFor(PlayerController who, int tick)
        {
            if (Towers.Count == 0 || !who || who.Down) return null;
            var party = Standing();
            if (Towers.Count == 1) return Towers.Find(t => t.ResolveTick >= tick);
            if (party.Count == 1) return Towers.Find(t => t.ResolveTick >= tick && t.Held < HoldTicks);
            return Towers.Find(t => t.ResolveTick >= tick && t.Owners.Contains(who));
        }
        // inside a tower that is not this player's (co-op): an extra, and a heavy hit when it lands
        public bool InOtherTower(PlayerController who, Vector3 q, int tick)
        {
            if (Towers.Count < 2 || Standing().Count < 2) return false;
            foreach (var t in Towers)
                if (t.ResolveTick >= tick && !t.Owners.Contains(who) && Sim.Flat(q - t.At).magnitude <= Radius + Hazard.Margin + 0.3f) return true;
            return false;
        }

        int First => Sim.Ticks(Easy ? FirstEasyS : FirstS);
        int Step => Sim.Ticks(Easy ? StepEasyS : StepS);

        public IEnumerable<int> Run()
        {
            Say(Name);
            Towers.Clear(); Failed = false;
            var party = Standing();
            bool solo = party.Count == 1;
            var needs = NeedsFor(party.Count, Casts);
            var layout = Layouts[Casts++ % Layouts.Length];
            Glow(1.3f); Flare(0.6f);
            var labels = new List<RunHud.WorldLabel>();
            int owner = 0;
            for (int k = 0; k < needs.Length; k++)
            {
                int resolveAt = solo ? First + k * Step : First;
                var t = new Tower { At = Centre + Sim.Dir(layout[k]) * Ring, Index = k, Need = needs[k], ResolveTick = W.Tick + resolveAt - 1 };
                for (int i = 0; i < needs[k] && owner < party.Count; i++) t.Owners.Add(party[owner++]);   // fixed when they rise
                var tower = t;
                var h = new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = t.At, Radius = Radius,
                    LockAt = 0, ResolveAt = resolveAt, Damage = 0, Knockback = 0, Stun = 0, HitOnResolve = false, Soak = true,
                };
                h.OnResolve = hz => Resolve(tower);
                t.H = Spawn(h);
                t.H.Tele?.Tint(CobaltFill, CobaltRim);           // a soak, not a hazard: cobalt, "get in"
                t.Fill = W.BossFx.Ring(Radius - 0.3f, Radius); t.Fill.Place(t.At, 0); t.Fill.Tint(GoldFill, GoldRim); t.Fill.Progress(0);
                labels.Add(RunHud.Label(t.At + Vector3.up * 0.4f, solo ? Numerals[k] : Dots(t.Need), Ivory, solo ? 54 : 46));
                Towers.Add(t);
            }
            int last = Duration();
            for (int i = 0; i <= last; i++)
            {
                foreach (var t in Towers)
                {
                    if (t.Resolved) continue;
                    if (IsOpen(t, W.Tick))
                    {
                        int inside = 0;
                        foreach (var p in W.Players) if (p && !p.Down && t.H.Contains(p.transform.position)) inside++;
                        if (inside >= t.Need && t.Held < HoldTicks) t.Held++;
                    }
                    t.Fill.Progress(t.Held / (float)HoldTicks);
                }
                for (int k = 0; k < Towers.Count; k++) if (Towers[k].Resolved) labels[k].Hide();
                if (i < last) yield return 1;
            }
            foreach (var l in labels) l.Hide();
        }

        static string Dots(int n) => n == 1 ? "•" : n == 2 ? "• •" : "• • •";

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
        public int Duration() => Standing().Count == 1 ? First + 2 * Step - 1 : First - 1;

        void Resolve(Tower t)
        {
            t.Resolved = true;
            t.Fill?.Expire();
            t.In.Clear();
            foreach (var p in W.Players) if (p && !p.Down && t.H.Contains(p.transform.position)) t.In.Add(p);
            bool anyOwner = t.Owners.Count == 0 || t.Owners.Exists(p => p && !p.Down);
            if (!anyOwner) return;   // its players all went down: the tower goes dark
            if (t.Held < HoldTicks)
            {
                Failed = true;
                string which = Towers.Count > 1 && Standing().Count == 1 ? "tower " + Numerals[t.Index] : "a tower";
                Boss.Wipe($"Total Eclipse — {which} was not held ({t.Need} needed)");
                return;
            }
            // extras: players beyond the count, the farthest from its centre first
            var extra = t.In.OrderBy(p => Sim.Flat(p.transform.position - t.At).sqrMagnitude).Skip(t.Need).ToList();
            foreach (var p in extra)
                p.ReceiveHit(new Hazard { Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = t.At, Damage = ExtraDamage, Knockback = 0, Stun = 15 });
            W.BossFx.ChimeResolve(t.At, Radius);
        }
    }
}
