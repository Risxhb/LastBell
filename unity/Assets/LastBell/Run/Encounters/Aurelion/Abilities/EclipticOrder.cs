using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // v3-01 Ecliptic Order (aurelion.ecliptic_order), abilities-v3/ABILITIES-v3.md (proposal). Source: FFXIV Limit Cut
    // (ordering and memory), made a solo-first floor mechanic.
    // Run(count) walks him to the arena centre, then lights `count` anchors (3 in Perfect Order, 4 later) on a 5 m circle
    // round the centre, each with an ivory numeral I..IV and a brass rim (friendly identity, never hostile colour). The
    // anchors and numerals show for 2.5 s, then go dark (Easy keeps them lit). Strike k resolves at
    // 3.2 + 2.2 x (k-1) s from the reveal: it hits everything EXCEPT a safe disc on anchor k (Ground, 50, no knockback,
    // 0.25 s stun). Each strike shows coral only 0.35 s before it lands (Easy 1.0 s), too late to find the disc by
    // reflex: consecutive anchors are at least 120 degrees apart (8.66 m), so you must remember the order.
    // Escape (reaction 0.25 s, one 3 m dash, sprint x1.5; abilities-v3 escape check):
    //   strike I from the far rim: 18.9 m - 2.7 = 16.2 m; at base 4.2 m/s 2.61 s of 3.2 s (0.59 s), at 6 m/s 1.98 s
    //   next strike from the far side of the last disc: 10 m (opposite anchors); at 4.2 m/s 1.63 s of 2.2 s (0.57 s)
    // Memory test (HARDER-PROMPT §8): the 0.35 s coral lead + 0.25 s is below the reflex escape from the worst spot in
    // the last disc (8.66 m: dash 0.2 s + 5.66 m sprint = 0.83 s).
    // The orders are authored (cycled per cast, never random); the safe disc grows with the party: 3.0 m at N <= 2,
    // 3.5 m at N <= 4, 4.5 m above. Hard keeps Medium timings: the difficulty is the memory, not the window.
    public sealed class EclipticOrder : AurelionAbility
    {
        public override string Id => "aurelion.ecliptic_order";
        public override string Name => "Ecliptic Order";

        public const float RevealV4S = 2.0f;   // v4: the numerals show 2.0 s (strike I still lands at 3.2 s)
        public const float AnchorRadius = 5f, RevealS = 2.5f, FirstS = 3.2f, StepS = 2.2f, LeadS = 0.35f, LeadEasyS = 1.0f;
        public const int Damage = 50, Stun = 15;

        // authored orders (yaw of each anchor round the centre, in firing order): consecutive anchors >= 120 degrees apart
        public static readonly float[][] Orders3 = { new[] { 0f, 120f, 240f }, new[] { 90f, 330f, 210f }, new[] { 30f, 270f, 150f } };
        public static readonly float[][] Orders4 = { new[] { 0f, 180f, 60f, 240f }, new[] { 90f, 270f, 30f, 210f }, new[] { 315f, 135f, 15f, 195f } };

        static readonly Color Ivory = new Color(1f, 0.96f, 0.86f), BrassFill = new Color(0.55f, 0.42f, 0.18f), BrassRim = new Color(1.4f, 1.1f, 0.5f);
        static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        // test evidence: the last cast's anchors (firing order), its strikes, and the ticks of reveal / hide / each strike
        public readonly List<Vector3> Anchors = new List<Vector3>();
        public readonly List<Hazard> Strikes = new List<Hazard>();
        public int RevealTick { get; private set; } = -1;
        public int HideTick { get; private set; }
        public bool CueVisible { get; private set; }               // the numerals (the memory cue)
        public int Casts { get; private set; }

        public float SafeRadius => N <= 2 ? 3.0f : N <= 4 ? 3.5f : 4.5f;
        int Reveal => Easy ? int.MaxValue : T(AurelionArgs.V4 ? RevealV4S : RevealS);
        int First => T(FirstS);
        int Step => T(StepS);
        int Lead => Sim.Ticks(Easy ? LeadEasyS : LeadS);
        public int StrikeTick(int k) => First + k * Step;           // ticks from the reveal
        public int StrikeAt(int k) => RevealTick + StrikeTick(k) - 1;   // world tick strike k resolves on
        // the strike still to come at world tick `tick` (its safe disc is where to stand), or -1 when no cast is running
        public int NextStrike(int tick)
        {
            if (RevealTick < 0) return -1;
            for (int k = 0; k < Anchors.Count; k++) if (StrikeAt(k) >= tick) return k;
            return -1;
        }

        public IEnumerable<int> Run(int count)
        {
            foreach (var w in Boss.WalkToCentre()) yield return w;
            Say(Name);
            count = Mathf.Clamp(count, 3, 4);
            var order = (count == 3 ? Orders3 : Orders4)[Casts % 3];
            Casts++;
            Anchors.Clear(); Strikes.Clear();
            foreach (var yaw in order) Anchors.Add(Centre + Sim.Dir(yaw) * AnchorRadius);

            // the reveal: brass rims and ivory numerals (friendly identity), hidden after 2.5 s
            RevealTick = W.Tick; HideTick = -1; CueVisible = true;
            var rims = new List<LastBell.VFX.BellkeeperFx.Telegraph>(); var labels = new List<RunHud.WorldLabel>();
            float r = SafeRadius;
            for (int k = 0; k < count; k++)
            {
                var rim = W.BossFx.Ring(r - 0.12f, r); rim.Place(Anchors[k], 0); rim.Tint(BrassFill, BrassRim); rim.Progress(1); rims.Add(rim);
                labels.Add(RunHud.Label(Anchors[k] + Vector3.up * 0.3f, Numerals[k], Ivory, 46));
            }
            Glow(1.2f); Clip("MeridianAlignment", StrikeTick(count - 1));

            int t = 0;
            for (int k = 0; k < count; k++)
            {
                int spawnAt = StrikeTick(k) - Lead;
                if (Reveal < spawnAt && t < Reveal) { yield return Reveal - t; t = Reveal; Hide(rims, labels); }
                yield return spawnAt - t; t = spawnAt;
                Strike(k, Lead);
            }
            yield return StrikeTick(count - 1) - t - 1;          // resumes on the last strike's resolve tick
            Hide(rims, labels);
        }

        void Hide(List<LastBell.VFX.BellkeeperFx.Telegraph> rims, List<RunHud.WorldLabel> labels)
        {
            if (!CueVisible) return;
            CueVisible = false; HideTick = W.Tick;
            foreach (var rim in rims) rim.Expire();
            foreach (var l in labels) l.Hide();
        }

        // ticks from Run's first tick (after the walk) to the last strike's resolve tick. CONTRACT: keep it equal to Run.
        public int Duration(int count) => StrikeTick(Mathf.Clamp(count, 3, 4) - 1) - 1;

        Hazard Strike(int k, int lead)
        {
            var h = new Hazard
            {
                Source = Name, Shape = HazardShape.Ring, Kind = HazardKind.Ground,
                Origin = Anchors[k], Inner = SafeRadius, Radius = BeyondRim,
                LockAt = 0, ResolveAt = lead, Damage = Damage, Knockback = 0, Stun = Stun,
            };
            h.OnResolve = hz => Flare(0.9f);
            Strikes.Add(Spawn(h));
            return h;
        }
    }
}
