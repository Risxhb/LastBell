using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 01 Mould Press (crucivar.mould_press), brief 01-crucivar.mould_press.md. Crucivar slams the die and casting moulds
    // rise from the die bays: shared soaks (circle r 2.0, Ground) whose numeral is how many players each needs. 20 per
    // share, split among those inside (missing shares fall on them); an empty mould downs every standing player.
    // Pairs alternate NE+SW, NW+SE; 5+ players use all four bays. Solo: two moulds on adjacent bays in sequence, II
    // resolving 2.6 s after I. Run() returns when the last tower has resolved, raising Resolved first (Second Press).
    public sealed class MouldPress : CrucivarAbility
    {
        public override string Id => "crucivar.mould_press";
        public event Action<MouldResult> Resolved;
        public MouldResult Last { get; private set; }

        public const float MouldRadius = 2.0f, Warning = 4.2f, SoloGap = 2.6f;
        public const int SharePerPlayer = 20, StunTicks = 15;   // 0.25 s, no knockback

        // which pair the next cast uses: 0 = NE+SW, 1 = NW+SE (bay indices in CrucivarAbility order NE, SE, SW, NW)
        int cast;
        static readonly int[][] Pairs = { new[] { 0, 2 }, new[] { 3, 1 } };

        // the brief's table: one numeral per tower, for N standing players at cast (N = 1 is the solo sequence)
        public static int[] Numerals(int n) => n switch
        {
            <= 2 => new[] { 1, 1 },
            3 => new[] { 2, 1 },
            4 => new[] { 2, 2 },
            5 => new[] { 2, 1, 1, 1 },
            6 => new[] { 2, 2, 1, 1 },
            7 => new[] { 2, 2, 2, 1 },
            _ => new[] { 2, 2, 2, 2 },
        };

        // bays for a cast: two towers = the current pair; four = the current pair, then the other; solo = the current
        // pair's first bay, then the next pair's first bay (adjacent, 8.5 m apart)
        public static int[] BaysFor(int pair, int towers, bool solo)
        {
            var a = Pairs[pair & 1]; var b = Pairs[(pair + 1) & 1];
            if (solo) return new[] { a[0], b[0] };
            return towers <= 2 ? new[] { a[0], a[1] } : new[] { a[0], a[1], b[0], b[1] };
        }

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Mould Press");
            int n = Mathf.Max(1, Standing().Count);   // downed players don't count
            bool solo = n == 1;
            var numerals = Numerals(n);
            var bays = BaysFor(cast, numerals.Length, solo);
            cast++;

            int warn = T(Warning), gap = T(SoloGap);
            var towers = new List<Hazard>();
            var centres = new List<Vector3>(); var nums = new List<int>(); var bayList = new List<int>(); var counts = new List<int>();
            for (int i = 0; i < numerals.Length; i++)
            {
                int k = numerals[i];
                var h = new Hazard
                {
                    Source = "Mould Press " + (solo ? (i == 0 ? "I" : "II") : k.ToString()),
                    Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Bay(bays[i]), Radius = MouldRadius,
                    LockAt = 0, ResolveAt = warn + (solo ? gap * i : 0),
                    Damage = SharePerPlayer * k, ShareTotal = SharePerPlayer * k, ShareCount = k, WipeIfEmpty = true,
                    Knockback = 0, Stun = StunTicks,
                };
                int bay = bays[i];
                h.OnResolve = hz =>
                {
                    // inside = the players this tower split its total between (a wipe hits everyone but nobody was inside)
                    centres.Add(hz.Origin); nums.Add(hz.ShareCount); bayList.Add(bay);
                    counts.Add(W.Players.Count(p => hz.Hit.Contains(p) && hz.Hits(p)));
                    W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                    Boss.Flare(0.8f);
                };
                towers.Add(W.Hazards.Spawn(h));
            }
            MouldPressFx.Show(W, towers, solo);

            for (int i = 0; towers.Any(h => W.Hazards.Live.Contains(h)); i++)
            {
                Boss.Glow(Mathf.Clamp01(i / (float)warn));
                yield return 1;
            }
            Boss.Glow(0);

            Last = new MouldResult { Centres = centres.ToArray(), Numerals = nums.ToArray(), Bays = bayList.ToArray(), Counts = counts.ToArray(), Sequential = solo };
            Resolved?.Invoke(Last);
        }
    }
}
