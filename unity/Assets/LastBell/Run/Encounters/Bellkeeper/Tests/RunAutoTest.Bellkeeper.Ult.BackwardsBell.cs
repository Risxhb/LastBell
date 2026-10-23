using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: Backwards Bell checks (ULTIMATE.md §3.4, hardened: the rewound copies strike the complement).
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot).
    //   LastBell.exe -lastbellAutotest -lastbellBellkeeperUltimate -lastbellTestAbility ult_backwards_bell
    public sealed partial class RunAutoTest
    {
        BackwardsBell bbM;
        readonly Dictionary<int, int> bbSpawnTick = new Dictionary<int, int>();   // hazard id -> world tick it spawned (this mechanic's)
        readonly List<Hazard> bbHz = new List<Hazard>();

        // an Ultimate fight with the boss halted at the centre, listening to this mechanic's spawns
        IEnumerator BbStart(string ability = null, string segment = null)
        {
            yield return UltStart(ability, segment);
            bbM = bk ? bk.Mechanic<BackwardsBell>() : null;
            bbSpawnTick.Clear(); bbHz.Clear();
            if (bk != null) bk.HazardSpawned += (a, h) => { if (a == bbM && !bbSpawnTick.ContainsKey(h.Id)) { bbSpawnTick[h.Id] = d.World.Tick; bbHz.Add(h); } };
        }
        int BbHits(PlayerController p = null, string source = null) => bkHits.Count(h => bbSpawnTick.ContainsKey(h.HazardId) && (p == null || h.Player == p) && (source == null || h.Source == source));
        int BbResolveTick(Hazard h) => h != null && bbSpawnTick.TryGetValue(h.Id, out int s) ? s + h.ResolveAt - 1 : -1;
        static UltCue BbCue(int seed = 0) => new UltCue { Variant = "block", At = new[] { 3.5f, 5f, 6.5f }, Phase = 2, Seed = seed };
        // A halted boss doesn't step running moves, so a block runs through the practice loop (-lastbellAbility
        // ult_backwards_bell) with the chosen cue: BbPractice(seed), place players, then BbGo() launches it.
        IEnumerator BbPractice(int seed = 0) { yield return BbStart("ult_backwards_bell"); if (bbM != null) bbM.PracticeCue = BbCue(seed); }
        void BbGo() { bk.BeginCombat(); for (int i = 0; i < 5 && bbM.Casts == 0; i++) d.World.Step(); }

        IEnumerator UltBackwardsBellChecks()
        {
            Note("Ultimate: Backwards Bell (hardened: each rewound copy strikes everywhere its original did not)");

            // ---- timing, shapes and numbers (one bare launch, boss at the centre) ----
            yield return BbPractice();
            if (bk == null) yield break;
            BkPlace(d.Player, new Vector3(0, 0, 6));
            BbGo();
            int l = bbM.LastCastTick;
            UltStepAliveUntil(() => !bbM.Running, 900);
            Check("Backwards Bell: built (not the stub), key ult_backwards_bell", bbM.Built && bbM.Key == "ult_backwards_bell");
            var want = new (string src, int at, int dmg)[]
            {
                ("Leaning Bell", 210, 50), ("Heart Toll", 300, 40), ("Toll Tide", 390, 40),
                ("Rewound Toll Tide", 510, 40), ("Rewound Heart Toll", 570, 40), ("Rewound Leaning Bell", 630, 40),
            };
            var got = bbHz.Select(h => (src: h.Source, at: BbResolveTick(h) - l, dmg: h.Damage)).OrderBy(x => x.at).ToList();
            Check("Backwards Bell: A, B, C resolve at 3.5 / 5.0 / 6.5 s, rewinds C', B', A' at C + 2.0 / 3.0 / 4.0 s (exact ticks, Ultimate damage)",
                  got.Count == 6 && want.Select((w, i) => got[i].src == w.src && got[i].at == w.at && got[i].dmg == w.dmg).All(x => x),
                  string.Join(", ", got.Select(x => $"{x.src} {x.at}t {x.dmg}")));
            var copies = bbHz.Where(h => h.Source.StartsWith(BackwardsBell.RewindPrefix)).ToList();
            Check("Backwards Bell: each rewound copy is drawn coral exactly 1.5 s before it strikes",
                  copies.Count == 3 && copies.All(h => h.ResolveAt - 1 == BackwardsBell.CopyWarnTicks), string.Join(", ", copies.Select(h => h.ResolveAt - 1)));
            var originals = bbHz.Where(h => !h.Source.StartsWith(BackwardsBell.RewindPrefix)).ToList();
            Check("Backwards Bell: originals use the Ultimate warnings (Leaning 3.5 s, Heart Toll 2.0 s, tide 2.5 s)",
                  originals.Count == 3 && originals.Single(h => h.Source == "Leaning Bell").ResolveAt - 1 == UltShapes.LeanTicks
                  && originals.Single(h => h.Source == "Heart Toll").ResolveAt - 1 == UltShapes.HeartTicks && originals.Single(h => h.Source == "Toll Tide").ResolveAt - 1 == UltShapes.TideTicks);
            var lean = bbM.LeanDir;
            var aC = copies.FirstOrDefault(h => h.Source == "Rewound Leaning Bell"); var bC = copies.FirstOrDefault(h => h.Source == "Rewound Heart Toll"); var cC = copies.FirstOrDefault(h => h.Source == "Rewound Toll Tide");
            var aO = originals.FirstOrDefault(h => h.Source == "Leaning Bell");
            Check("Backwards Bell: A strikes the LeanDir half; rewound A strikes exactly the other half",
                  aO != null && aC != null && aO.Contains(lean * 6) && !aO.Contains(-lean * 6) && aC.Contains(-lean * 6) && !aC.Contains(lean * 6) && aC.Kind == HazardKind.Strike);
            Check("Backwards Bell: rewound tide = the disc inside 7 m (out past 7.3 m is safe)",
                  cC != null && cC.Contains(Vector3.forward * 6.9f) && cC.Contains(Vector3.zero) && !cC.Contains(Vector3.forward * 7.4f) && cC.Kind == HazardKind.Ground);
            Check("Backwards Bell: rewound Heart Toll = everything beyond 4 m of the boss, to the wall (inside 3.7 m is safe)",
                  bC != null && bC.Contains(Vector3.right * 4.1f) && bC.Contains(Vector3.right * (CombatWorld.PlayerBound - 0.1f)) && !bC.Contains(Vector3.right * 3.6f) && bC.Kind == HazardKind.Ground);
            Check("Backwards Bell: never wipes, never cracks", bk.Ult.Wipes == 0 && bk.Ult.CrackedCount == 0);

            // ---- still players: melee hugging the boss and ranged at 10 m both get hit ----
            yield return BbPractice();
            if (bk == null) yield break;
            var melee = d.Player; BkPlace(melee, new Vector3(0, 0, bk.BodyRadius + 0.7f));
            var ranged = BkAdd(new Vector3(0, 0, -10));
            BbGo();
            UltStepAliveUntil(() => !bbM.Running, 900);
            Check("Backwards Bell: a still melee player is hit (Heart Toll, then the rewound tide)",
                  BbHits(melee, "Heart Toll") == 1 && BbHits(melee, "Rewound Toll Tide") == 1, string.Join(", ", bkHits.Where(h => h.Player == melee).Select(h => h.Source)));
            Check("Backwards Bell: a still ranged player is hit (the tide, then the rewound Heart Toll)",
                  BbHits(ranged, "Toll Tide") == 1 && BbHits(ranged, "Rewound Heart Toll") == 1, string.Join(", ", bkHits.Where(h => h.Player == ranged).Select(h => h.Source)));
            // the identical-copy loophole is closed: the spot that dodges A, B and C is struck by a rewind
            yield return BbPractice();
            if (bk == null) yield break;
            BbGo();
            BkPlace(d.Player, -bbM.LeanDir * 5.5f);
            UltStepAliveUntil(() => !bbM.Running, 900);
            Check("Backwards Bell: standing in the band on A's safe half dodges A, B, C but not the rewinds",
                  BbHits(d.Player, "Leaning Bell") + BbHits(d.Player, "Heart Toll") + BbHits(d.Player, "Toll Tide") == 0 && BbHits(d.Player) >= 1,
                  string.Join(", ", bkHits.Where(h => h.Player == d.Player).Select(h => h.Source)));

            // ---- the intended route at 4.2 m/s, walking only (no dash), solo and N = 2 / 4 / 8, every lean ----
            foreach (var (n, seed) in new[] { (1, 0), (1, 1), (1, 2), (1, 3), (2, 1), (4, 2), (8, 3) })
                yield return BbRoute(n, seed);

            // ---- boss death mid-rewind cancels everything ----
            yield return BbPractice();
            if (bk == null) yield break;
            BkPlace(d.Player, new Vector3(0, 0, 5));
            BbGo();
            int tc = bbM.OriginalTicks[2];
            UltStepAliveUntil(() => d.World.Tick >= tc + BackwardsBell.FirstRewindTicks - 20, 900);
            int live = d.World.Hazards.Count, fxBefore = bbM.FxCount; bkHits.Clear();
            bk.DebugDefeat();
            bool gone = d.World.Hazards.Count == 0 && !bbM.Running && bbM.FxCount == 0 && bbM.Copies.All(h => h == null);
            BkStep(300);
            Check("Backwards Bell: boss death mid-rewind cancels the copies, arrows and the cast; no hit after", live > 0 && gone && BbHits() == 0 && !d.Player.Down, $"{live} live, {fxBefore} fx before");

            // ---- phase-start practice (P2): launched at 8 s, rewound A on the clocks III+IV tick (18.5 s) ----
            yield return BbStart(null, "P2");
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = false;
            bk.BeginCombat();
            UltStepAliveUntil(() => bbM.Casts > 0, Sim.Ticks(10));
            int t0 = UltTick0();
            bool cue = bbM.Cue.Variant == "block" && bbM.Cue.At.Length == 3 && Mathf.Approximately(bbM.Cue.At[2], 6.5f);
            UltStepAliveUntil(() => !bbM.Running, Sim.Ticks(14));
            Check("Backwards Bell: practice from P2 launches it at 8 s with its block cue; C' 16.5, B' 17.5, A' 18.5 s (with clocks III+IV)",
                  bk.Segment == BellkeeperEncounter.UltSegment.P2 && bbM.LastCastTick - t0 == Sim.Ticks(8) && cue
                  && bbM.CopyTicks[0] - t0 == Sim.Ticks(16.5f) && bbM.CopyTicks[1] - t0 == Sim.Ticks(17.5f) && bbM.CopyTicks[2] - t0 == Sim.Ticks(18.5f)
                  && bbHz.Count(h => h.Source.StartsWith(BackwardsBell.RewindPrefix)) == 3,
                  $"launch {(bbM.LastCastTick - t0) / 60f:0.00}s, A' {(bbM.CopyTicks[2] - t0) / 60f:0.00}s, {bbHz.Count} hazards");
            BellkeeperEncounter.PushTimersOn = true;

            // ---- practice hook ----
            yield return BbStart("ult_backwards_bell");
            if (bk == null) yield break;
            bk.BeginCombat();
            int pt = UltStepAliveUntil(() => bbM.Casts > 0, 300);
            int six = UltStepAliveUntil(() => bbHz.Count >= 6, 800);
            Check("Backwards Bell: -lastbellAbility ult_backwards_bell loops the block alone (A, B, C and three rewinds)",
                  bk.PracticeAbility == bbM && pt >= 0 && six >= 0 && bk.Abilities.All(a => a == bbM || a.Casts == 0), $"cast after {pt}, six after {six}");

            // ---- the public rewind API (the Conductor's Hand) ----
            yield return BbStart();
            if (bk == null) yield break;
            BkPlace(d.Player, new Vector3(-6, 0, 6));
            int at = d.World.Tick + 90;
            var back = bbM.RewindOf(UltShapes.LeaningHalf(d.World, Vector3.left, 10), at);
            var same = bbM.RewindOf(UltShapes.HeartToll(d.World, Vector3.zero, 10), at, invert: false);
            var fp = bbM.RewindOf(UltimateState.Footprint.Of(UltShapes.Tide(d.World, 10), d.World.Tick), at);
            Check("RewindOf: spawns now on Backwards Bell's behalf and resolves exactly on the given tick",
                  back != null && BbResolveTick(back) == at && BbResolveTick(same) == at && BbResolveTick(fp) == at && !bbM.Running);
            Check("RewindOf: invert = the other half / the disc inside a ring; invert:false = the same footprint",
                  back != null && same != null && fp != null
                  && back.Contains(new Vector3(5, 0, 0)) && !back.Contains(new Vector3(-5, 0, 0)) && same.Shape == HazardShape.Circle && same.Contains(Vector3.right * 3.5f) && !same.Contains(Vector3.right * 5f)
                  && fp.Shape == HazardShape.Circle && fp.Contains(Vector3.forward * 6.5f) && !fp.Contains(Vector3.forward * 8f));
            UltStepAlive(95);
            Check("RewindOf: the copies resolve on time and miss a player standing clear of all three", BbHits(d.Player) == 0 && d.World.Hazards.Count == 0);
            Check("Backwards Bell: no wipe and no Cracked Hour in any check", bk.Ult.Wipes == 0 && bk.Ult.CrackedCount == 0);
        }

        // N strat bots (UltimateBot, 4.2 m/s, no attacks, no dash = walking only, finite stamina) from a ring 6 m out clear one block.
        IEnumerator BbRoute(int n, int seed)
        {
            yield return BbPractice(seed);
            if (bk == null) yield break;
            var party = new List<PlayerController> { d.Player };
            for (int i = 1; i < n; i++) party.Add(BkAdd(Vector3.zero, RunDirector.Classes[i % RunDirector.Classes.Length]));
            var bots = new List<UltimateBot>();
            for (int i = 0; i < n; i++)
            {
                BkPlace(party[i], Sim.Dir(360f * i / n + 15f + 40f * seed) * 6f);
                party[i].DebugSetHp(PlayerController.MaxHp);
                var b = UltimateParty.Drive(party[i], i, n); b.Speed = 4.2f; b.Attacks = false; b.UseDash = false; bots.Add(b);
            }
            bkHits.Clear();
            BbGo();
            int guard = 0; float minStam = PlayerController.MaxStamina;
            var trace = new List<string>();
            while (bbM.Running && guard++ < 900)
            {
                BkStep(1);
                foreach (var p in party) minStam = Mathf.Min(minStam, p.Stamina);
                int tcT = bbM.OriginalTicks[2];
                if (d.World.Tick == tcT || d.World.Tick == tcT + 30 || d.World.Tick == tcT + 90)
                    trace.Add($"t{(d.World.Tick - tcT) / 60f:+0.0} {party[0].name.Split(' ').Last()} [{bots[0].DebugLine}]");
                for (int k = 0; k < 3; k++)
                    if (d.World.Tick == bbM.CopyTicks[k])
                        foreach (var p in party) { var q = Sim.Flat(p.transform.position); trace.Add($"{"CBA"[k]}' {p.name.Split(' ').Last()} r{q.magnitude:0.00} side{Vector3.Dot(q, bbM.LeanDir):0.0}"); }
            }
            int hits = BbHits();
            if (hits > 0) Note("  route trace: " + string.Join(" | ", trace));
            Check($"Backwards Bell: the guide's route clears at 4.2 m/s walking (N = {n}, lean {UltimateState.StationNames[seed % 4]}): no hit, nobody down",
                  hits == 0 && party.All(p => !p.Down) && bbHz.Count == 6,
                  $"{hits} hits ({string.Join(", ", bkHits.Where(h => bbSpawnTick.ContainsKey(h.HazardId)).Select(h => h.Player.name + ":" + h.Source))}) · min stamina {minStam:0} · {bots[0].DebugLine}");
            d.Player.Input = bot;
        }
    }
}
