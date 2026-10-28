using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Phase 2 "Foundry Uprising" proposal: turret and casting count, the casting placement and link, paths drawn before
    // the bullets, still players hit, painshare, killing every casting ends the uprising with no flood, and an uprising
    // left alone floods and downs the party.
    public sealed class FoundryUprisingTest : CrucivarAbilityTest
    {
        public override string Id => "crucivar.v4_foundry_uprising";
        public override string Set => "v4";
        public override int Order => 310;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            yield return t.Setup(4, at: new[] { new Vector3(0, 0, 3f), new Vector3(0, 0, -13f), new Vector3(9, 0, 0), new Vector3(-6, 0, 5) });
            if (t.Boss == null) yield break;
            var up = t.Boss.Uprising;
            int c0 = t.W.Tick;
            t.Begin(up.Run());
            yield return t.Seconds(3f);
            t.Check("4 players: three turrets, three castings as boss parts", up.Turrets.Count == 3 && t.W.Parts.Count(p => p.Alive) == 3, $"{up.Turrets.Count} turrets, {t.W.Parts.Count} parts");
            t.Check("each casting sits 7.5 m out, across the arena from its turret", up.Turrets.All(x => Mathf.Abs(Sim.Flat(x.Casting.Pos).magnitude - FoundryUprising.CastRing) < 0.05f && Vector3.Dot(Sim.Flat(x.Pos).normalized, Sim.Flat(x.Casting.Pos).normalized) < -0.99f));
            t.Check("Pour Flood lands at 30 s", up.Flood != null && up.Flood.ResolveAt == Sim.Ticks(30f), $"{up.Flood?.ResolveAt}");

            float s = 0;
            while (s < 9f) { foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp); s += Time.deltaTime; yield return null; }
            var path = t.Spawned("Lane Stream path").Concat(t.Spawned("Ring Wave path")).Concat(t.Spawned("Spiral Arm path")).OrderBy(x => x.Tick).FirstOrDefault();
            var bullet = t.Spawns.Where(x => x.Source.EndsWith("bullet")).OrderBy(x => x.Tick).FirstOrDefault();
            t.Check("every pattern draws its path at least 2.0 s before the first bullet", path != null && bullet != null && path.H.ResolveAt >= Sim.Ticks(2f) && bullet.Tick - path.Tick >= Sim.Ticks(2f) - 1,
                path == null || bullet == null ? "none" : $"path at {path.Tick - c0}, bullet at {bullet.Tick - c0}");
            t.Check("still players get hit by bullets", t.Hits.Any(h => h.Source.EndsWith("bullet")), $"{t.Hits.Count(h => h.Source.EndsWith("bullet"))} bullet hits");
            t.Check("the still player at the wall gets hit", t.WasHit(t.Party[1]), string.Join(",", t.HitsOn(t.Party[1]).Select(h => h.Source).Distinct()));

            var first = up.Turrets[0];
            int boss0 = t.Boss.Hp;
            int dealt = t.W.DamagePart(first.Casting, 100, first.Casting.Pos, "test");
            t.Check("painshare: half the damage to a casting also hits Crucivar", dealt == 100 && boss0 - t.Boss.Hp == 50, $"dealt {dealt}, boss -{boss0 - t.Boss.Hp}");

            foreach (var x in up.Turrets.ToList()) t.W.DamagePart(x.Casting, 99999, x.Casting.Pos, "test");
            yield return t.Until(() => !t.Boss.TestBusy, 3f, "uprising over");
            t.Check("killing every casting sinks every turret and ends the uprising", !t.Boss.TestBusy && up.Turrets.All(x => !x.Up) && !t.W.Parts.Any(p => p.Alive));
            yield return t.Seconds(2f);
            t.Check("no flood once the turrets are down", t.Party.All(p => !p.Down) && up.Floods == 0);
            t.Check("one hit per instance", t.OneHitPerInstance());
            yield return t.Shot("v4_foundry_uprising");

            // left alone, the flood wipes the party
            foreach (var p in t.Party) p.DebugSetHp(PlayerController.MaxHp);
            t.Begin(up.Run());
            s = 0;
            while (s < 28f) { foreach (var p in t.Party) if (p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp); s += Time.deltaTime; yield return null; }
            yield return t.Until(() => t.Party.All(p => p.Down), 6f, "flood");
            t.Check("an uprising left standing floods at 30 s and downs the whole party", t.Party.All(p => p.Down) && up.Floods == 1, $"down {t.Party.Count(p => p.Down)}/4, floods {up.Floods}");
        }
    }
}
