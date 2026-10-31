using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Ability set v3 (abilities-v2/ABILITIES-v3.md, escape-v3-*.md): the new numbers of the five v2 abilities, the two new
    // fillers and the v3 phase plan. Runs after the v2 tests, with CrucivarArgs.SetOverride = "v3".
    public abstract class V3TestBase : CrucivarAbilityTest
    {
        public override string Set => "v3";
        protected static Vector3 V(float x, float z) => new Vector3(x, 0, z);
        protected static int Dmg(int medium, Difficulty d) => Mathf.RoundToInt(medium * RunDifficulty.DamageTakenScale(d));
        protected static IEnumerator WaitTick(CrucivarV2AutoTest t, int tick, float timeout = 20)
        {
            float s = 0; while (t.W != null && t.W.Tick < tick && s < timeout) { s += Time.deltaTime; yield return null; }
        }
    }

    // ---------------- v3: the five abilities' new numbers ----------------
    public sealed class V3AbilitiesTest : V3TestBase
    {
        public override string Id => "crucivar.v3_abilities";
        public override int Order => 200;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // v3 is the default set, and Crucivar has the packet's 6600 HP
            var keep = CrucivarArgs.SetOverride; CrucivarArgs.SetOverride = null;
            t.Check("v3 is the default ability set (no -lastbellAbilitySet)", CrucivarArgs.Arg("-lastbellAbilitySet") != null || (CrucivarArgs.V3 && CrucivarArgs.Set == "v3"), CrucivarArgs.Set);
            CrucivarArgs.SetOverride = keep;
            yield return t.Setup(1);
            if (t.Boss != null && t.Boss.MaxHp != 6600) { t.KillBoss(); yield return t.Setup(1); }   // respawn under v3
            t.Check("v3 Crucivar has 6600 HP", t.Boss != null && t.Boss.MaxHp == 6600, t.Boss ? t.Boss.MaxHp.ToString() : "none");
            if (t.Boss == null) yield break;

            // ---- Mould Press: share 25, solo I > II > III 2.4 s apart on adjacent bays ----
            yield return t.Setup(2, at: new[] { CrucivarAbility.Bay(0) + V(0, 4), CrucivarAbility.Bay(2) + V(0, 4) });
            t.Begin(t.Boss.Mould.Run());
            yield return t.Ticks(3);
            var towers = t.Spawned("Mould Press").ToList();
            t.Check("v3 Mould Press N=2: two moulds, 25 per share", towers.Count == 2 && towers.All(s => s.H.ShareTotal == 25 && s.H.ShareCount == 1), string.Join(",", towers.Select(s => s.H.ShareTotal)));
            for (int i = 0; i < 2 && i < towers.Count; i++) t.In(t.Party[i]).Go(towers.OrderBy(s => (s.H.Origin - t.Party[i].transform.position).sqrMagnitude).First().H.Origin, 6f);
            yield return t.WaitIdle();
            t.Check("v3 Mould Press N=2: each soaker takes 25", t.Party.All(p => t.HitsOn(p, "Mould Press").Select(h => h.Damage).SequenceEqual(new[] { 25 })),
                string.Join(" | ", t.Party.Select(p => string.Join("/", t.HitsOn(p, "Mould Press").Select(h => h.Damage)))));

            yield return t.Setup(1);
            int cast = t.W.Tick;
            t.Begin(t.Boss.Mould.Run());
            yield return t.Ticks(2);
            var solo = t.Spawned("Mould Press").OrderBy(s => s.H.ResolveAt).ToList();
            t.Check("v3 solo: three moulds I, II, III", solo.Select(s => s.Source).SequenceEqual(new[] { "Mould Press I", "Mould Press II", "Mould Press III" }), string.Join(", ", solo.Select(s => s.Source)));
            if (solo.Count == 3)
            {
                t.Check("v3 solo: 4.2 s, then 2.4 s apart", solo[0].H.ResolveAt == 252 && solo[1].H.ResolveAt == 396 && solo[2].H.ResolveAt == 540, string.Join("/", solo.Select(s => s.H.ResolveAt)));
                float d01 = Vector3.Distance(solo[0].H.Origin, solo[1].H.Origin), d12 = Vector3.Distance(solo[1].H.Origin, solo[2].H.Origin);
                t.Check("v3 solo: each mould on the bay next to the last (8.49 m)", Mathf.Abs(d01 - 8.49f) < 0.05f && Mathf.Abs(d12 - 8.49f) < 0.05f, $"{d01:0.00} / {d12:0.00}");
                // the route at 4.2 m/s: stand in I, then walk to II, then III, each after the previous resolves
                var p = t.Main; t.Place(p, solo[0].H.Origin);
                for (int k = 0; k < 2; k++)
                {
                    yield return WaitTick(t, solo[k].Tick + solo[k].H.ResolveAt + 1);
                    t.In(p).Go(solo[k + 1].H.Origin, 4.2f);
                }
                yield return t.WaitIdle();
                t.Check("v3 solo: I > II > III walked at 4.2 m/s, 25 each, no wipe", !p.Down && t.HitsOn(p, "Mould Press").Count() == 3 && t.HitsOn(p, "Mould Press").All(h => h.Damage == 25),
                    string.Join("/", t.HitsOn(p, "Mould Press").Select(h => h.Source + " " + h.Damage)));
            }

            // ---- Gear Chain: 2.5 s, snap 40 ----
            yield return t.Setup(2, at: new[] { V(0, 5), V(0.5f, 5) });
            t.Begin(t.Boss.Chain.Run());
            yield return t.Ticks(2);
            var chain = t.Spawned("Gear Chain").FirstOrDefault();
            t.Check("v3 Gear Chain warns 2.5 s", chain != null && chain.H.ResolveAt == 150, chain != null ? chain.H.ResolveAt.ToString() : "none");
            yield return t.WaitIdle();
            t.Check("v3 Gear Chain: partners standing together both snap for 40", t.Party.All(p => t.HitsOn(p, "Gear Chain").Select(h => h.Damage).SequenceEqual(new[] { 40 })),
                string.Join(" | ", t.Party.Select(p => string.Join("/", t.HitsOn(p, "Gear Chain").Select(h => h.Damage)))));

            // ---- Bellows Draw: rim 8.5 m / 40, blast r 5.0 at 2.4 s / 45, pull 2.0 m (1.1-1.5 s), edge 15/s ----
            // boss at (0, -1.5): A at the wall, B hugging the body, C in the band 7 m out (pulled to 5 m, then walks out)
            yield return t.Setup(3, at: new[] { V(0, 12.5f), V(0, 0.7f), V(0, 5.5f) });
            var A = t.Party[0]; var B = t.Party[1]; var C = t.Party[2];
            cast = t.W.Tick;
            t.Begin(t.Boss.Bellows.Run());
            yield return t.Ticks(2);
            var rim = t.Spawned("Slag Rim").FirstOrDefault(); var blast = t.Spawned("Bellows Blast").FirstOrDefault();
            t.Check("v3 Slag Rim: inner 8.5 m, 40, 2.0 s", rim != null && Mathf.Approximately(rim.H.Inner, 8.5f) && rim.H.Damage == 40 && rim.H.ResolveAt == 120);
            t.Check("v3 blast: r 5.0, 45, lands at 2.4 s", blast != null && Mathf.Approximately(blast.H.Radius, 5f) && blast.H.Damage == 45 && rim != null && blast.Tick - rim.Tick + blast.H.ResolveAt == 144,
                blast != null && rim != null ? $"r {blast.H.Radius}, {blast.H.Damage}, at {blast.Tick - rim.Tick + blast.H.ResolveAt}" : "none");
            if (rim != null) cast = rim.Tick;   // the ability's t = 0
            yield return WaitTick(t, cast + Sim.Ticks(1.05f));
            float before = Vector3.Distance(Sim.Flat(C.transform.position), Sim.Flat(t.Boss.transform.position));
            yield return WaitTick(t, cast + Sim.Ticks(1.55f));
            float after = Vector3.Distance(Sim.Flat(C.transform.position), Sim.Flat(t.Boss.transform.position));
            t.Check("v3 inhale pulls 2.0 m by 1.5 s, no damage", Mathf.Abs(before - after - 2f) < 0.15f && !t.WasHit(C), $"{before:0.00} -> {after:0.00} m");
            t.In(C).Go(V(0, 6.5f), 4.2f);   // walk back out of the blast at the prototype speed
            yield return t.WaitIdle();
            t.Check("v3 wall player hit by the rim (40)", t.HitsOn(A, "Slag Rim").Select(h => h.Damage).SequenceEqual(new[] { 40 }));
            t.Check("v3 body-hugging player hit by the blast (45)", t.HitsOn(B, "Bellows Blast").Select(h => h.Damage).SequenceEqual(new[] { 45 }));
            t.Check("v3 pulled player walks back out at 4.2 m/s before the blast", !t.WasHit(C, "Bellows Blast") && !t.WasHit(C, "Slag"));
            A.DebugSetHp(PlayerController.MaxHp);
            int burnFrom = t.W.Tick; yield return t.Ticks(120);
            int burned = t.HitsOn(A, "Slag Edge").Where(h => h.Tick > burnFrom).Sum(h => h.Damage);
            t.Check("v3 slag edge burns 15/s", burned >= 28 && burned <= 31, $"{burned} in 2 s");
            t.Check("v3 Bellows: one hit per instance", t.OneHitPerInstance());

            // ---- The Great Casting ----
            // co-op split of 100, pour 3.0 s locked at 1.5; both players in the pour lane are also in the march's gap
            yield return t.Setup(2, at: new[] { V(0, 7f), V(0, 9f) });
            cast = t.W.Tick;
            t.Begin(t.Boss.Casting.Run(8));
            yield return t.Ticks(2);
            var pour = t.Spawned("Pour").FirstOrDefault();
            t.Check("v3 pour: locks at 1.5, lands at 3.0, 100 split", pour != null && pour.H.LockAt == 90 && pour.H.ResolveAt == 180 && pour.H.ShareTotal == 100);
            yield return t.WaitIdle();
            t.Check("v3 pour split: 50 each for two inside", t.Party.All(p => t.HitsOn(p, "Pour").Select(h => h.Damage).SequenceEqual(new[] { 50 })),
                string.Join(" | ", t.Party.Select(p => string.Join("/", t.HitsOn(p, "Pour").Select(h => h.Damage)))));
            var stamps = t.Spawned("Stamp").ToList();
            t.Check("v3 march: 8 spokes x 5 rings, r 1.5, 30 each", stamps.Count == 40 && stamps.All(s => Mathf.Approximately(s.H.Radius, 1.5f) && s.H.Damage == 30), $"{stamps.Count} stamps");
            var ringR = stamps.Select(s => Mathf.Round(Sim.Flat(s.H.Origin).magnitude)).Distinct().OrderByDescending(r => r).ToArray();
            t.Check("v3 march rings at 13 / 11 / 9 / 7 / 5 m", ringR.SequenceEqual(new[] { 13f, 11f, 9f, 7f, 5f }), string.Join("/", ringR));
            if (pour != null) cast = pour.Tick;
            var res = stamps.Select(s => s.Tick - cast + s.H.ResolveAt).Distinct().OrderBy(x => x).ToArray();
            t.Check("v3 march resolves rim-first 5.0 .. 7.0 s", res.SequenceEqual(new[] { 300, 330, 360, 390, 420 }), string.Join("/", res));
            t.Check("v3 the pour channel is a gap of the inward march", t.Party.All(p => !t.WasHit(p, "Stamp")));
            t.Check("v3 no return wave outside phase 3", !t.Spawned("Return").Any());

            yield return t.Setup(2, at: new[] { V(0, 7f), V(0, 9f) });
            t.Begin(t.Boss.Casting.Run(4));
            yield return t.WaitIdle();
            t.Check("v3 phase 1 march: every other spoke (4 x 5 stamps)", t.Spawned("Stamp").Count() == 20, t.Spawned("Stamp").Count().ToString());

            // phase 3: the outward return wave on the offset spokes (the pour channel among them), 7.5 .. 9.5 s
            yield return t.Setup(2, at: new[] { V(0, 7f), V(0, 9f) });
            cast = t.W.Tick;
            t.Begin(t.Boss.Casting.Run(8, returnWave: true));
            yield return t.WaitIdle();
            var ret = t.Spawned("Return").ToList();
            var pour3 = t.Spawned("Pour").FirstOrDefault(); if (pour3 != null) cast = pour3.Tick;
            var rres = ret.Select(s => s.Tick - cast + s.H.ResolveAt).Distinct().OrderBy(x => x).ToArray();
            t.Check("v3 return wave: 8 x 5 stamps, inside-out 7.5 .. 9.5 s", ret.Count == 40 && rres.SequenceEqual(new[] { 450, 480, 510, 540, 570 }), $"{ret.Count}: {string.Join("/", rres)}");
            t.Check("v3 return wave warns at least 2.0 s", ret.All(s => s.H.ResolveAt >= 120), ret.Count > 0 ? ret.Min(s => s.H.ResolveAt).ToString() : "none");
            t.Check("v3 return wave resolves only after the inward wave's last ring", rres.Length > 0 && rres[0] > 420);
            t.Check("v3 return wave: players resting in the pour channel are hit", t.Party.All(p => t.WasHit(p, "Return")));
            t.Check("v3 Great Casting: one hit per instance", t.OneHitPerInstance());

            yield return t.Setup(2, Difficulty.Easy, at: new[] { V(0, 7f), V(0, 9f) });
            t.Begin(t.Boss.Casting.Run(8, returnWave: true));
            yield return t.WaitIdle();
            t.Check("v3 Easy: no return wave", !t.Spawned("Return").Any());

            // solo: two pours, the second laid on the player as the first lands; both dodged at 4.2 m/s
            yield return t.Setup(1, at: new[] { V(0, 9f) });
            t.Begin(t.Boss.Casting.Run(0));
            yield return t.Ticks(2);
            cast = t.Spawned("Pour").Select(s => s.Tick).DefaultIfEmpty(t.W.Tick).First();
            yield return WaitTick(t, cast + 92);
            t.In(t.Main).Go(V(3.2f, 9f), 4.2f);
            yield return WaitTick(t, cast + 182);
            var second = t.Spawned("Pour II").FirstOrDefault();
            t.Check("v3 solo: a second pour (50, 2.0 s) lands on the player's spot", second != null && second.H.Damage == 50 && second.H.ResolveAt == 120 && second.H.Locked && second.H.Hits(t.Main),
                second != null ? $"{second.H.Damage}, {second.H.ResolveAt}" : "none");
            t.In(t.Main).Go(V(-0.2f, 9f), 4.2f);
            yield return t.WaitIdle();
            t.Check("v3 solo: both pours dodged at 4.2 m/s", !t.WasHit(t.Main, "Pour"), string.Join("/", t.HitsOn(t.Main).Select(h => h.Source)));

            // ---- Second Press: 35 (Hard) ----
            yield return t.Setup(1, Difficulty.Hard, at: new[] { CrucivarAbility.Bay(0) });
            var result = new MouldResult { Centres = new[] { CrucivarAbility.Bay(0) }, Numerals = new[] { 1 }, Bays = new[] { 0 }, Counts = new[] { 1 } };
            t.Begin(t.Boss.Press.Run(result));
            yield return t.WaitIdle();
            t.Check("v3 Second Press: 35, x1.3 on Hard", t.Spawned("Second Press").Any(s => s.H.Damage == 35) && t.HitsOn(t.Main, "Second Press").Select(h => h.Damage).SequenceEqual(new[] { Dmg(35, Difficulty.Hard) }),
                string.Join("/", t.HitsOn(t.Main, "Second Press").Select(h => h.Damage)));
        }
    }

    // ---------------- v3: the new fillers ----------------
    public sealed class V3FillersTest : V3TestBase
    {
        public override string Id => "crucivar.v3_fillers";
        public override int Order => 210;

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            // ---- Anvil Sweep: 120 deg cone r 5.5 at the nearest, tracks to 0.8, lands 2.0, 35 Strike ----
            yield return t.Setup(2, at: new[] { V(0, 1.0f), V(0, 9.5f) });   // boss at (0, -1.5): melee 2.5 m, ranged 11 m
            if (t.Boss == null) yield break;
            t.Begin(t.Boss.Sweep.Run());
            yield return t.Ticks(2);
            var sw = t.Spawned("Anvil Sweep").FirstOrDefault();
            t.Check("Anvil Sweep: cone half 60, r 5.5, Strike, locks 0.8 s, lands 2.0 s",
                sw != null && sw.H.Shape == HazardShape.Cone && Mathf.Approximately(sw.H.HalfAngle, 60f) && Mathf.Approximately(sw.H.Radius, 5.5f) &&
                sw.H.Kind == HazardKind.Strike && sw.H.LockAt == 48 && sw.H.ResolveAt == 120);
            yield return t.WaitIdle();
            t.Check("Anvil Sweep: the nearest (melee) player standing still takes 35", t.HitsOn(t.Party[0], "Anvil Sweep").Select(h => h.Damage).SequenceEqual(new[] { 35 }));
            t.Check("Anvil Sweep: the far player is not in it", !t.WasHit(t.Party[1], "Anvil Sweep"));

            // it follows the nearest player while tracking, then locks: stepping out after the lock at 4.2 m/s escapes
            yield return t.Setup(1, at: new[] { V(2.5f, 0f) });
            t.Begin(t.Boss.Sweep.Run());
            yield return t.Ticks(2);
            int cast = t.Spawned("Anvil Sweep").Select(s => s.Tick).DefaultIfEmpty(t.W.Tick).First();
            yield return WaitTick(t, cast + 50);
            float aimErr = Mathf.Abs(Mathf.DeltaAngle(t.Spawned("Anvil Sweep").First().H.Yaw, Sim.YawTo(t.Boss.transform.position, t.Main.transform.position, 0)));
            t.Check("Anvil Sweep tracks the nearest player until the lock", aimErr < 5f, $"{aimErr:0.0} deg off");
            t.In(t.Main).Go(V(6.5f, 3.5f), 4.2f);
            yield return t.WaitIdle();
            t.Check("Anvil Sweep: backing out after the lock at 4.2 m/s escapes", !t.WasHit(t.Main, "Anvil Sweep"));

            yield return t.Setup(1, Difficulty.Easy, at: new[] { V(0, 1.0f) });
            t.Begin(t.Boss.Sweep.Run());
            yield return t.Ticks(2);
            var swe = t.Spawned("Anvil Sweep").FirstOrDefault();
            t.Check("Anvil Sweep Easy: 1.0 / 2.5 s", swe != null && swe.H.LockAt == 60 && swe.H.ResolveAt == 150);
            yield return t.WaitIdle();
            t.Check("Anvil Sweep Easy damage", t.HitsOn(t.Main, "Anvil Sweep").Select(h => h.Damage).SequenceEqual(new[] { Dmg(35, Difficulty.Easy) }));

            // ---- Slag Spit: r 2.5 on the farthest (2 at 4+, 3 at 8), tracks to 0.8, lands 2.0, 30 Ground ----
            yield return t.Setup(1, at: new[] { V(0, 11f) });
            t.Begin(t.Boss.Spit.Run());
            yield return t.WaitIdle();
            var sp = t.Spawned("Slag Spit").ToList();
            t.Check("Slag Spit solo: one circle r 2.5, Ground, 0.8 / 2.0 s", sp.Count == 1 && Mathf.Approximately(sp[0].H.Radius, 2.5f) && sp[0].H.Kind == HazardKind.Ground && sp[0].H.LockAt == 48 && sp[0].H.ResolveAt == 120);
            t.Check("Slag Spit: a ranged player standing still takes 30", t.HitsOn(t.Main, "Slag Spit").Select(h => h.Damage).SequenceEqual(new[] { 30 }));

            yield return t.Setup(1, at: new[] { V(0, 11f) });
            t.Begin(t.Boss.Spit.Run());
            yield return t.Ticks(2);
            cast = t.Spawned("Slag Spit").Select(s => s.Tick).DefaultIfEmpty(t.W.Tick).First();
            yield return WaitTick(t, cast + 50);
            t.In(t.Main).Go(V(4f, 11f), 4.2f);
            yield return t.WaitIdle();
            t.Check("Slag Spit: walking out after the lock at 4.2 m/s escapes", !t.WasHit(t.Main, "Slag Spit"));

            yield return t.Setup(4, at: new[] { V(0, 1f), V(2, 3f), V(-3, 9f), V(3, 11f) });
            t.Begin(t.Boss.Spit.Run());
            yield return t.WaitIdle();
            t.Check("Slag Spit N=4: the two farthest are spat on", t.Spawned("Slag Spit").Count() == 2 && t.WasHit(t.Party[2], "Slag Spit") && t.WasHit(t.Party[3], "Slag Spit") &&
                !t.WasHit(t.Party[0], "Slag Spit") && !t.WasHit(t.Party[1], "Slag Spit"));

            var eight = Enumerable.Range(0, 8).Select(i => Quaternion.Euler(0, 45 * i, 0) * Vector3.forward * (3 + i)).ToArray();
            yield return t.Setup(8, at: eight);
            t.Begin(t.Boss.Spit.Run());
            yield return t.WaitIdle();
            t.Check("Slag Spit N=8: three circles", t.Spawned("Slag Spit").Count() == 3, t.Spawned("Slag Spit").Count().ToString());
            t.Check("fillers: one hit per instance", t.OneHitPerInstance());

            // the boss dying mid-windup cancels a filler in the same tick
            yield return t.Setup(2, at: new[] { V(0, 1.0f), V(0, 11f) });
            t.Begin(t.Boss.Sweep.Run());
            yield return t.Ticks(40);
            int hits = t.Hits.Count;
            t.KillBoss();
            t.Check("Anvil Sweep: boss death cancels it", t.W.Hazards.Count == 0);
            yield return t.Seconds(2.5f);
            t.Check("no damage after the boss dies", t.Hits.Count == hits);
        }
    }

    // ---------------- v3: phase plan ----------------
    public sealed class V3PhasePlanTest : V3TestBase
    {
        public override string Id => "crucivar.v3_phase_plan";
        public override int Order => 220;

        static readonly HashSet<string> Abilities = new HashSet<string>
            { "Mould Press", "Gear Chain", "Bellows Draw", "The Great Casting", "Second Press", "Anvil Sweep", "Slag Spit" };
        readonly List<(int phase, string name)> order = new List<(int, string)>();
        readonly Dictionary<string, float> held = new Dictionary<string, float>();

        // the bot stands in moulds, otherwise keeps 6.5 m from the boss inside 8.0 m of the centre, and is healed to full
        // after every hit (this test checks the order and the transitions, not survival)
        IEnumerator Watch(CrucivarV2AutoTest t, float seconds, System.Func<bool> until)
        {
            float s = 0; string last = null;
            while (s < seconds && !until() && t.D.State == RunState.Combat)
            {
                var b = t.Boss; var p = t.Main;
                if (b.Current != last)
                {
                    last = b.Current;
                    if (Abilities.Contains(last)) order.Add((b.PhaseIndex, last));
                }
                held[b.Current] = (held.TryGetValue(b.Current, out var h) ? h : 0) + Time.deltaTime;
                if (p.Hp < PlayerController.MaxHp) p.DebugSetHp(PlayerController.MaxHp);
                var mould = t.W.Hazards.Live.Where(z => z.ShareTotal > 0 && z.WipeIfEmpty).OrderBy(z => z.ResolveAt - z.Age).FirstOrDefault();
                if (mould != null) t.In(p).Go(mould.Origin, 6f);
                else
                {
                    var from = Sim.Flat(p.transform.position - b.transform.position); if (from.sqrMagnitude < 0.01f) from = Vector3.forward;
                    var spot = b.transform.position + from.normalized * 6.5f;
                    if (new Vector2(spot.x, spot.z).magnitude > 8.0f) spot = Sim.Flat(spot).normalized * 8.0f;
                    t.In(p).Go(spot, 6f);
                }
                s += Time.deltaTime; yield return null;
            }
            t.In(t.Main).Stop();
        }

        public override IEnumerator Run(CrucivarV2AutoTest t)
        {
            yield return t.Setup(1, Difficulty.Medium, hpFraction: 0.95f);
            if (t.Boss != null && t.Boss.MaxHp != 6600) { t.KillBoss(); yield return t.Setup(1, Difficulty.Medium, hpFraction: 0.95f); }
            var boss = t.Boss; if (boss == null) yield break;
            boss.TestHold = false;

            string[] p1Want = { "Mould Press", "Anvil Sweep", "Bellows Draw", "Slag Spit", "The Great Casting", "Anvil Sweep", "Gear Chain" };
            yield return Watch(t, 60, () => order.Count(o => o.phase == 1) >= 8);
            var p1 = order.Where(o => o.phase == 1).Select(o => o.name).ToList();
            t.Note("v3 phase 1 order: " + string.Join(" > ", p1));
            t.Check("v3 phase 1: Mould > Sweep > Bellows > Spit > Casting > Sweep > Chain", p1.Take(7).SequenceEqual(p1Want), string.Join(", ", p1));
            t.Check("v3 phase 1: the loop restarts with Mould Press", p1.Count >= 8 && p1[7] == "Mould Press");
            t.Check("v3 phase 1 ends its loop with a 3.0 s major punish", held.TryGetValue("Gear Chain (furnace venting)", out var pun) && pun >= 2.8f && pun <= 3.4f, $"{pun:0.00} s");

            boss.DebugSetHpFraction(0.64f);
            int tr0 = boss.Transitions;
            yield return Watch(t, 30, () => boss.PhaseIndex == 2);
            t.Check("v3 second form at 65 %: Casting Heat, one transition", boss.PhaseIndex == 2 && boss.SecondForm && boss.PhaseName == "Casting Heat" && boss.Transitions == tr0 + 1);
            string[] p2Want = { "Gear Chain", "Slag Spit", "Mould Press", "Anvil Sweep", "Bellows Draw", "Slag Spit", "The Great Casting" };
            yield return Watch(t, 70, () => order.Count(o => o.phase == 2) >= 7);
            var p2 = order.Where(o => o.phase == 2).Select(o => o.name).ToList();
            t.Note("v3 phase 2 order: " + string.Join(" > ", p2));
            t.Check("v3 phase 2: Chain > Spit > Mould > Sweep > Bellows > Spit > Casting", p2.Take(7).SequenceEqual(p2Want), string.Join(", ", p2));

            boss.DebugSetHpFraction(0.29f);
            yield return Watch(t, 40, () => boss.PhaseIndex == 3);
            t.Check("v3 Open Pressure at 30 %", boss.PhaseIndex == 3 && boss.PhaseName == "Open Pressure");
            string[] p3Want = { "The Great Casting", "Gear Chain", "Anvil Sweep", "Mould Press", "Slag Spit", "Bellows Draw", "Anvil Sweep" };
            yield return Watch(t, 70, () => order.Count(o => o.phase == 3) >= 7);
            var p3 = order.Where(o => o.phase == 3).Select(o => o.name).ToList();
            t.Note("v3 phase 3 order: " + string.Join(" > ", p3));
            t.Check("v3 phase 3: Casting > Chain > Sweep > Mould > Spit > Bellows > Sweep", p3.Take(7).SequenceEqual(p3Want), string.Join(", ", p3));
            t.Check("v3 phase 3 uses the return wave", t.Spawned("Return").Any());
            int maxP = MaxPatterns(t);
            t.Check("v3: never more than two damaging patterns at once", maxP <= 2, $"max {maxP}");
            boss.TestHold = true;
        }

        static int MaxPatterns(CrucivarV2AutoTest t)
        {
            int max = 0;
            foreach (var s in t.Spawns)
            {
                int at = s.Tick;
                int live = t.Spawns.Where(o => o.Tick <= at && at < o.Tick + o.H.ResolveAt + Mathf.Max(1, o.H.ActiveTicks))
                    .Select(o => o.H.BurnPerSecond > 0 ? "slag" : Family(o.Source)).Distinct().Count();
                max = Mathf.Max(max, live);
            }
            return max;
        }
        // one ability = one pattern; the slag rim and its edge are one; the pour, its solo second pour, the march and its
        // return wave are the Great Casting's two (pour and stamps)
        static string Family(string src) => src.StartsWith("Slag Rim") || src.StartsWith("Slag Edge") ? "slag" : src.StartsWith("Mould") ? "Mould" :
            src.StartsWith("Gear") ? "Chain" : src.StartsWith("Pour") ? "Pour" : src.StartsWith("Stamp") || src.StartsWith("Return") ? "March" : src;
    }
}
