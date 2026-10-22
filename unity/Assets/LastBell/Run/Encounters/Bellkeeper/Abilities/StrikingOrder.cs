using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Striking Order (`bellkeeper.striking_order`), Bellkeeper ability set v2 (proposal, co-op mechanic).
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/02-striking-order.md.
    // Ivory numerals I, II, III… go over the standing players' heads in a seeded order. Each numeral's ring is a Ground
    // Circle that follows its owner at 6 m/s, locks 2.0 s before it rings and hits anyone inside (25, one hit per ring
    // per player). Ring I rings on cast tick 180, then one every 60 ticks. Solo: I–IV all on you. N = 2: A, B, A, B.
    // N = 3–8: one ring each (at least four), r 2.2 m from N = 5. A numeral whose owner goes down before its ring locks
    // is withdrawn. Easy: step 1.4 s, lock 2.5 s, radius −0.4 m, ring I at 3.75 s (ABILITIES.md §6, warnings ×1.25).
    // Hard = Medium (damage ×1.3 is applied by the hit). 1.0 s recovery after the last ring.
    public sealed class StrikingOrderAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.StrikingOrder;
        public override string Name => "Striking Order";

        public const int Damage = 25, RecoveryTicks = 60;
        public const float Follow = 6f;
        public static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        // per level, set at the start of each cast
        public int FirstRing { get; private set; } = 180;   // cast tick of ring I
        public int Step { get; private set; } = 60;         // ticks between rings (and between ring spawns)
        public int LockLead { get; private set; } = 120;    // a ring locks this many ticks before it rings
        public float RingRadius { get; private set; } = 2.6f;

        // the current cast (tests): ring k belongs to Owners[k]; Rings[k] stays null if it never spawned
        readonly List<PlayerController> owners = new List<PlayerController>();
        Hazard[] rings = new Hazard[0];
        public IReadOnlyList<PlayerController> Owners => owners;
        public IReadOnlyList<Hazard> Rings => rings;
        public readonly List<int> SpawnedAt = new List<int>(), RangAt = new List<int>();   // cast ticks
        int castStart;
        StrikingOrderFx fx;
        public int NumeralsShown => fx == null ? 0 : fx.NumeralCount;
        public int NumeralsOn(PlayerController p) => fx == null ? 0 : fx.NumeralsOn(p);

        public override IEnumerable<int> Cast()
        {
            ClearCast();
            castStart = W.Tick;
            bool easy = Level == Difficulty.Easy;
            FirstRing = easy ? 225 : 180; Step = easy ? 84 : 60; LockLead = easy ? 150 : 120;

            // numerals go to the standing players in a seeded order (the cast count seeds it: same fight, same order)
            var party = new List<PlayerController>();
            foreach (var p in W.Players) if (!p.Down) party.Add(p);
            if (party.Count == 0) yield break;
            var rng = new System.Random(7919 * Casts + party.Count);
            for (int i = party.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (party[i], party[j]) = (party[j], party[i]); }
            int n = party.Count, count = Mathf.Clamp(Mathf.Max(4, n), 4, Numerals.Length);
            RingRadius = (n >= 5 ? 2.2f : 2.6f) - (easy ? 0.4f : 0f);
            for (int k = 0; k < count; k++) owners.Add(party[k % n]);
            rings = new Hazard[count];

            Boss.ShowAttack(Name);
            Boss.Presenter?.PlayOnce("ShatteredChime", 2.63f);
            fx = new StrikingOrderFx(owners);

            int last = (count - 1) * Step + FirstRing;
            for (int t = 0; t <= last; t++)
            {
                if (t % Step == 0 && t / Step < count) SpawnRing(t / Step, t);
                for (int k = 0; k < count; k++)
                {
                    if (fx.Dropped(k) || !owners[k].Down) continue;
                    var h = rings[k];
                    if (h != null && (h.Cancelled || h.Locked)) continue;   // locked: it rings where it stands
                    if (h != null) W.Hazards.Cancel(h);                     // owner down before lock: withdrawn
                    fx.Drop(k);
                }
                int next = NextPending();
                if (next >= 0)
                {
                    var o = owners[next].transform.position;
                    Boss.Face(Sim.YawTo(Boss.transform.position, o, Boss.transform.eulerAngles.y), 240);
                    Boss.Glow(1f - Mathf.Clamp01((next * Step + FirstRing - t) / (float)FirstRing));
                }
                fx.Tick(rings);
                yield return 1;
            }
            fx.Tick(rings);
            yield return RecoveryTicks;
            fx.Clear(); fx = null;
        }

        void SpawnRing(int k, int t)
        {
            var who = owners[k];
            if (who.Down) return;   // never spawns; the loop drops its numeral
            // spawned on cast tick t, the hazard ages once on that same tick, so it rings on cast tick t + FirstRing
            var h = Spawn(new Hazard
            {
                Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Radius = RingRadius,
                Origin = Sim.Flat(who.transform.position), ResolveAt = FirstRing + 1, LockAt = FirstRing + 1 - LockLead,
                Damage = Damage, Stun = 15, Knockback = 1.5f,
            });
            h.Track = hz => hz.Origin = Vector3.MoveTowards(hz.Origin, Sim.Flat(who.transform.position), Follow * Sim.Dt);
            h.OnResolve = hz =>
            {
                RangAt.Add(W.Tick - castStart);
                W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                Boss.Flare(0.6f);
                fx?.Rang(k);
            };
            rings[k] = h; SpawnedAt.Add(t);
            fx.Spawned(k, h);
        }

        int NextPending()
        {
            for (int k = 0; k < rings.Length; k++) if (!fx.Dropped(k) && !fx.HasRung(k)) return k;
            return -1;
        }

        void ClearCast()
        {
            owners.Clear(); rings = new Hazard[0]; SpawnedAt.Clear(); RangAt.Clear();
            fx?.Clear(); fx = null;
        }

        public override void Cancel()
        {
            base.Cancel();
            ClearCast();
        }
    }
}
