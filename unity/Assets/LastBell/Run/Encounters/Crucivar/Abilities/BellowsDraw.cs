using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 03 Bellows Draw (crucivar.bellows_draw), brief 03-crucivar.bellows_draw.md: the ranged punisher. The phase plan has
    // already walked the boss to the anvil. From the cast (t = 0, Medium; Easy stretches every time by 1.25):
    //   0.0 -> 2.0  Slag Rim   annulus on the arena centre, inner 9.5 m out to the wall, 30 Ground, no knockback
    //   2.0 -> 7.0  Slag Edge  the same annulus as standing slag, 10/s (overlaps never add up), then it cools and expires
    //   0.5 -> 3.5  Blast      circle 4.5 m around the boss, 30 Ground, 1.5 m knockback outward
    //   1.2 -> 2.2  inhale telegraph (all vents open, amber chevrons pointing in); 2.2 -> 2.6 everyone standing is pulled
    //               1.5 m toward the boss (PlayerController.Pull: no damage, stun or interrupt, never a teleport)
    // Two damaging patterns at most (rim/edge + blast). Run() returns when the blast has resolved; the edge keeps burning.
    // SlagRim() spawns only the rim and its edge and returns at once (paired with Mould Press or Gear Chain).
    public sealed class BellowsDraw : CrucivarAbility
    {
        public override string Id => "crucivar.bellows_draw";

        public const float RimInner = 9.5f, BlastRadius = 4.5f, PullDistance = 1.5f;
        public const int RimDamage = 30, BlastDamage = 30; public const float EdgeBurn = 10f, BlastKnock = 1.5f;
        public const float RimAt = 2.0f, EdgeUntil = 7.0f, BlastFrom = 0.5f, BlastAt = 3.5f, InhaleWarn = 1.2f, PullFrom = 2.2f, PullTo = 2.6f;
        public static float RimOuter => Rim + 0.5f;   // the wall's inner face: the whole floor past 9.5 m
        // v3 (ABILITIES-v3.md, escape-v3-*.md): rim from 8.5 m for 40, edge 15/s; blast r 5.0 for 45 warned from the cast to
        // 2.4 s; the 2 m pull runs 1.1-1.5 s so a pulled player keeps the same 0.9 s to walk back out before the blast
        public const float RimInnerV3 = 8.5f, BlastRadiusV3 = 5.0f, PullDistanceV3 = 2.0f, EdgeBurnV3 = 15f;
        public const int RimDamageV3 = 40, BlastDamageV3 = 45;
        public const float BlastFromV3 = 0f, BlastAtV3 = 2.4f, InhaleWarnV3 = 0.1f, PullFromV3 = 1.1f, PullToV3 = 1.5f;

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Bellows Draw");
            bool v3 = V3;
            float blastFrom = v3 ? BlastFromV3 : BlastFrom, blastAt = v3 ? BlastAtV3 : BlastAt, inhaleWarn = v3 ? InhaleWarnV3 : InhaleWarn,
                pullFromS = v3 ? PullFromV3 : PullFrom, pullToS = v3 ? PullToV3 : PullTo, pullDist = v3 ? PullDistanceV3 : PullDistance;
            Boss.Presenter.PlayOnce("ReleaseFrontBack", blastAt * WarnScale);
            SlagRim();
            int now = 0, total = T(blastAt);
            foreach (var w in Until(T(blastFrom), now, total)) yield return w;
            now = T(blastFrom);

            var centre = Flat(Boss.transform.position);
            var blast = W.Hazards.Spawn(new Hazard { Source = "Bellows Blast", Shape = HazardShape.Circle, Kind = HazardKind.Ground,
                Origin = centre, Radius = v3 ? BlastRadiusV3 : BlastRadius, LockAt = 0, ResolveAt = T(blastAt) - now, Damage = v3 ? BlastDamageV3 : BlastDamage, Knockback = BlastKnock });
            blast.OnResolve = hz => { W.BossFx.BellowsBlast(hz.Origin, hz.Radius); Boss.Flare(1.3f); Boss.Rig.VentsAll(false); };

            foreach (var w in Until(T(inhaleWarn), now, total)) yield return w;
            now = Mathf.Max(now, T(inhaleWarn));

            // inhale: vents open and chevrons crawl inward until the pull ends; the pull itself is applied one tick at a time
            // so a boss death (the script stops) ends it on the same tick
            Boss.Rig.VentsAll(true);
            Boss.SetCurrent("Bellows Draw (inhale)");
            int pullFrom = T(pullFromS), pullTo = T(pullToS), wave = Sim.Ticks(0.35f), nextWave = now;
            float perTick = pullDist / Mathf.Max(1, pullTo - pullFrom);
            for (; now < total; now++)
            {
                if (now == nextWave && now < pullTo) { W.BossFx.InhaleChevrons(centre, pullDist, 0.7f); nextWave += wave; }
                if (now >= pullFrom && now < pullTo)
                    foreach (var p in Standing())
                    {
                        var to = Flat(centre - p.transform.position);
                        if (to.magnitude > Boss.BodyRadius + 0.4f) p.Pull(to, perTick, 1);
                    }
                Boss.Glow(now / (float)total);
                if (now == pullTo) Boss.SetCurrent("Bellows Draw (exhale)");
                yield return 1;
            }
        }

        // Slag Rim + Slag Edge only (the pairing part). Returns at once; both hazards run on their own.
        public void SlagRim()
        {
            var rim = W.Hazards.Spawn(new Hazard { Source = "Slag Rim", Shape = HazardShape.Ring, Kind = HazardKind.Ground,
                Origin = Vector3.zero, Inner = V3 ? RimInnerV3 : RimInner, Radius = RimOuter, LockAt = 0, ResolveAt = T(RimAt), Damage = V3 ? RimDamageV3 : RimDamage, Knockback = 0 });
            float burn = V3 ? EdgeBurnV3 : EdgeBurn;
            rim.OnResolve = hz =>
            {
                W.BossFx.SlagRimPour(hz.Origin, hz.Inner, hz.Radius);
                W.Hazards.Spawn(new Hazard { Source = "Slag Edge", Shape = HazardShape.Ring, Kind = HazardKind.Ground,
                    Origin = hz.Origin, Inner = hz.Inner, Radius = hz.Radius, LockAt = 0, ResolveAt = T(EdgeUntil) - T(RimAt), BurnPerSecond = burn });
            };
        }

        // waits from `now` until tick `at`, capped by the ability's end
        static IEnumerable<int> Until(int at, int now, int total)
        {
            int n = Mathf.Min(at, total) - now;
            if (n > 0) yield return n;
        }
    }
}
