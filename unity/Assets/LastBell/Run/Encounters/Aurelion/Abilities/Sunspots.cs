using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Sunspots (aurelion.sunspots), abilities-v4/ABILITIES-v4.md §3.2 (proposal). Source: Queen Ansurek's Reactive
    // Toxin (staggered pops, puddles that fill the room). Marked players carry a coral sun sigil with an order numeral.
    // In numeral order, 1.5 s apart (the first 3.0 s after the marks; Easy 3.75 s, then 1.9 s), each sigil detonates where
    // its carrier stands: a Ground circle of 3.5 m, 50 (kill tier), no knockback. Each leaves a scorch pool (3.0 m,
    // 20/s, Hazard.BurnPerSecond) that lasts until the phase ends (the floor-fill exception; AurelionEncounter.PhasePools).
    // Chain reaction: a detonation that catches a player still carrying an un-detonated sigil sets it off early and the
    // party wipes (Solar Flare). Solo: you carry two in a row (I, then II on you again): drop I, move at least 3.8 m, drop
    // II. Co-op: ceil(N/2) carriers per cast, rotating, numerals I.. in order.
    //   Escape: a detonation's own carrier must leave 3.5 + 0.3 m in its 1.5 s visible window (the coral circle shows
    //   1.5 s ahead on the carrier's spot, locked): 3.8 m = 0.88 s at 6 m/s walking, 1.15 s at 4.2 m/s.
    public sealed class Sunspots : AurelionAbility
    {
        public override string Id => "aurelion.sunspots";
        public override string Name => "Sunspots";

        public const float Blast = 3.5f, Pool = 3.0f, PoolBurn = 20f, FirstS = 3.0f, StepS = 1.5f, LockS = 1.5f;
        public const int Damage = 50;
        public const int PoolTicks = 60 * 75;   // 75 s, across phase changes (v4 "hard from the start")
        static readonly Color Coral = new Color(1f, 0.45f, 0.35f);
        static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        public sealed class Spot { public PlayerController Who; public int Index, DetonateTick; public Hazard Blast, Pool; public bool Done; }
        public readonly List<Spot> Spots = new List<Spot>();     // test evidence: the last cast
        public readonly List<Hazard> Pools = new List<Hazard>(); // every pool still on the floor (cleared at phase end)
        public int Casts { get; private set; }
        public bool Chained { get; private set; }

        // the sigil this player still carries (bots: walk it to the rim before it goes off), or null
        public Spot NextFor(PlayerController who, int tick) => Spots.Find(s => s.Who == who && !s.Done && s.DetonateTick >= tick);
        public const float RimDrop = 10.5f;   // where a carrier should be when its sigil goes off (metres from him)

        int First => T(FirstS);
        int Step => T(StepS);
        int Lock => T(LockS);

        public IEnumerable<int> Run()
        {
            Say(Name);
            Spots.Clear(); Chained = false;
            var party = Standing();
            int cast = Casts++;
            if (party.Count == 1) { Spots.Add(new Spot { Who = party[0], Index = 0 }); Spots.Add(new Spot { Who = party[0], Index = 1 }); }
            else
            {
                int carriers = (party.Count + 1) / 2;
                for (int k = 0; k < carriers; k++) Spots.Add(new Spot { Who = party[(cast * carriers + k) % party.Count], Index = k });
            }
            int start = W.Tick;
            for (int k = 0; k < Spots.Count; k++) Spots[k].DetonateTick = start + First + k * Step - 1;
            var labels = new Dictionary<PlayerController, RunHud.WorldLabel>();
            foreach (var s in Spots) if (!labels.ContainsKey(s.Who)) labels[s.Who] = RunHud.Label(Vector3.up * 2.4f, "", Coral, 30, s.Who.transform);
            Glow(1.2f);

            int t = 0;
            for (int k = 0; k < Spots.Count; k++)
            {
                var s = Spots[k];
                int lockAt = First + k * Step - Lock;            // the coral circle locks on the carrier this long before
                while (t < lockAt) { Relabel(labels); t++; yield return 1; }
                if (!s.Who || s.Who.Down) { s.Done = true; continue; }
                var spot = s;
                s.Blast = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(s.Who.transform.position), Radius = Blast,
                    LockAt = 0, ResolveAt = Lock, Damage = Damage, Knockback = 0, Stun = 15,
                });
                s.Blast.OnResolve = hz => Detonate(spot);
            }
            int end = First + (Spots.Count - 1) * Step - 1;
            while (t < end) { Relabel(labels); t++; yield return 1; }
            foreach (var l in labels.Values) l.Hide();
        }

        // ticks from Run's first tick to the last detonation (= when Run returns). CONTRACT: keep it equal to Run.
        public int Duration() => First + ((Standing().Count == 1 ? 2 : (N + 1) / 2) - 1) * Step - 1;

        // each carrier's label shows the numerals it still holds, e.g. "I  II"
        void Relabel(Dictionary<PlayerController, RunHud.WorldLabel> labels)
        {
            foreach (var kv in labels)
            {
                string s = "";
                foreach (var sp in Spots) if (sp.Who == kv.Key && !sp.Done) s += (s.Length > 0 ? "  " : "") + Numerals[sp.Index];
                kv.Value.Text = s.Length > 0 ? "SUNSPOT " + s : "";
                if (s.Length == 0) kv.Value.Hide();
            }
        }

        void Detonate(Spot s)
        {
            s.Done = true;
            Flare(0.7f);
            W.BossFx.ChimeResolve(s.Blast.Origin, Blast);
            // chain reaction: the blast caught another carrier whose own sigil has not gone off yet
            foreach (var o in Spots)
                if (!o.Done && o.Who != s.Who && o.Who && !o.Who.Down && s.Blast.Contains(o.Who.transform.position))
                { Chained = true; Boss.Wipe("Solar Flare — a sunspot caught carrier " + Numerals[o.Index]); return; }
            s.Pool = Spawn(new Hazard
            {
                Source = Name + " scorch", Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = s.Blast.Origin, Radius = Pool,
                LockAt = 0, ResolveAt = PoolTicks, BurnPerSecond = PoolBurn, Knockback = 0,
            });
            Pools.Add(s.Pool);   // pools last their full 75 s, across phase changes: the floor fills from the start
        }
    }
}
