using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate: The Conductor's Hand checks (ULTIMATE.md §3.6; ConductorsHand.cs).
    // Harness: RunAutoTest.Bellkeeper.Ultimate.cs (UltStart, BkStep, UltStepAlive, bkHits, UltimateBot).
    public sealed partial class RunAutoTest
    {
        static readonly string[] HandPhrases = { "1L", "1R", "2L", "2R", "3L", "3R", "4L", "4R" };
        ConductorsHand hand;
        BellkeeperEncounter handSub;
        readonly List<(int tick, Hazard h)> handSpawns = new List<(int, Hazard)>();

        IEnumerator HandStart(string segment = null)
        {
            yield return UltStart(null, segment);
            hand = bk != null ? bk.Mechanic<ConductorsHand>() : null;
            handSpawns.Clear();
            if (bk != null && bk != handSub) { handSub = bk; bk.HazardSpawned += (a, h) => { if (a == hand) handSpawns.Add((d.World.Tick, h)); }; }
        }
        UltCue HandCue(string phrase, float a = 7f, float b = 8.5f, int phase = 2) => new UltCue { Variant = "phrase:" + phrase, At = new[] { a, b }, Phase = phase, Seed = 3 };
        // hits on p from this cast's own hazards
        int HandHitsOn(PlayerController p) { var ids = new HashSet<int>(handSpawns.Select(s => s.h.Id)); return bkHits.Count(x => x.Player == p && ids.Contains(x.HazardId)); }
        int HandHitsOn(PlayerController p, IEnumerable<Hazard> set) { var ids = new HashSet<int>(set.Select(h => h.Id)); return bkHits.Count(x => x.Player == p && ids.Contains(x.HazardId)); }
        // the party on the far +Z wall, so the phrase's Leaning Bell falls on them (the worst crossing)
        List<PlayerController> HandParty(int n, float r = 11.8f)
        {
            var party = new List<PlayerController> { d.Player };
            for (int i = 1; i < n; i++) party.Add(BkAdd(Vector3.zero, RunDirector.Classes[i % RunDirector.Classes.Length]));
            for (int i = 0; i < n; i++) { float a = (n == 1 ? 0 : -18f + 36f * i / (n - 1)) * Mathf.Deg2Rad; BkPlace(party[i], new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r); }
            return party;
        }

        IEnumerator UltConductorsHandChecks()
        {
            Note("---- Ultimate: The Conductor's Hand ----");

            // ---- the phrase table and its seeded order (§7) ----
            var p2 = Enumerable.Range(1, 4).Select(l => ConductorsHand.PhraseFor(2, l, 11).fingers).ToList();
            Check("Hand: a phase's four loops show the four finger counts once each (seeded order)", p2.Distinct().Count() == 4 && p2.All(f => f >= 1 && f <= 4), string.Join(",", p2));
            Check("Hand: the order repeats every four loops and is the same for the same seed",
                  ConductorsHand.PhraseFor(2, 5, 11) == ConductorsHand.PhraseFor(2, 1, 11) && ConductorsHand.PhraseFor(5, 2, 99) == ConductorsHand.PhraseFor(5, 2, 99));
            var swings = Enumerable.Range(0, 40).Select(s => ConductorsHand.PhraseFor(2, 1, s).right).ToList();
            Check("Hand: the swing varies with the seed (both swings occur)", swings.Contains(true) && swings.Contains(false));
            var prac = Enumerable.Range(1, 8).Select(l => ConductorsHand.PhraseFor(0, l, 0)).Select(x => $"{x.fingers}{(x.right ? "R" : "L")}").ToList();
            Check("Hand: practice walks all eight phrases, starting with 1L (Leaning Bell, no partner)", prac.Distinct().Count() == 8 && prac[0] == "1L", string.Join(" ", prac));
            Check("Hand: phrase variants parse (phrase:3R), the bare plan variant doesn't",
                  ConductorsHand.TryParse("phrase:3R", out int pf, out bool pr) && pf == 3 && pr && !ConductorsHand.TryParse("phrase", out _, out _) && !ConductorsHand.TryParse("phrase:5L", out _, out _));

            // ---- spawn and resolve ticks, for every phrase: the phrase on the launch tick, real telegraphs only 2.0 s before ----
            foreach (var ph in HandPhrases)
            {
                yield return HandStart();
                if (bk == null) yield break;
                BkPlace(d.Player, new Vector3(0, 0, 6));
                bk.UltLaunch(hand, HandCue(ph));
                int L = hand.LastCastTick;
                bool shown = hand.PhraseShown && hand.Phrase == ph;
                var stopped = bk.Mechanic<StoppedHour>();
                bool stopOk = ph[0] != '4' || (stopped.LastCastTick == L && stopped.Cue.Variant == "lash" && Mathf.Approximately(stopped.Cue.AtOr(0, -1), 5f) && Mathf.Approximately(stopped.Cue.AtOr(1, -1), 7f) && hand.StoppedChoice == "launched");
                UltStepAlive(Sim.Ticks(8.5f) + 2);
                int t1 = L + Sim.Ticks(7f), t2 = L + Sim.Ticks(8.5f);
                var res = handSpawns.Select(s => (s.tick, s.h, at: s.tick + s.h.ResolveAt - 1)).ToList();
                bool firstOk = ph[0] == '4' ? hand.First.Count == 0 : hand.First.Count > 0 && res.Where(x => hand.First.Contains(x.h)).All(x => x.tick == t1 - ConductorsHand.TellTicks && x.at == t1);
                bool secondOk = hand.Second.Count > 0 && res.Where(x => hand.Second.Contains(x.h)).All(x => x.tick == t2 - ConductorsHand.TellTicks && x.at == t2);
                bool nothingEarly = res.All(x => x.tick >= t1 - ConductorsHand.TellTicks);
                Check($"Hand {ph} ({hand.Reading}): phrase on the launch tick; move 1 drawn 2.0 s before 7.0 s, move 2 2.0 s before 8.5 s; nothing earlier",
                      shown && firstOk && secondOk && nothingEarly && stopOk,
                      $"spawns {string.Join(" ", res.Select(x => $"{x.h.Source}@{(x.tick - L) / 60f:0.00}->{(x.at - L) / 60f:0.00}"))}{(ph[0] == '4' ? " · stopped " + hand.StoppedChoice + " " + stopped.Cue : "")}");
                bool rewound = ph[1] == 'R' ? hand.Second.All(h => h.Source.StartsWith(ConductorsHand.RewindPrefix)) : hand.Second.Count == 1 && hand.Second[0].Source == UltShapes.HeartSource;
                Check($"Hand {ph}: the swing names move 2 ({(ph[1] == 'R' ? "the first move's footprint again" : "Heart Toll")})", rewound, string.Join(", ", hand.Second.Select(h => h.Source)));
                if (ph[1] == 'R' && ph[0] != '4')
                {
                    bool same = ph[0] == '3' ? hand.Second.Count == hand.First.Count && hand.Second.Zip(hand.First, (a, b) => Sim.Flat(a.Origin - b.Origin).magnitude < 0.01f).All(x => x)
                              : hand.Second[0].Shape == hand.First[0].Shape && Mathf.Abs(Mathf.DeltaAngle(hand.Second[0].Yaw, hand.First[0].Yaw)) < 0.1f && hand.Second[0].Inner == hand.First[0].Inner;
                    Check($"Hand {ph}: the rewound copy is the first footprint exactly, 40 damage", same && hand.Second.All(h => h.Damage == UltShapes.StandardDamage));
                }
                Check($"Hand {ph}: presentation clears after move 2 (phrase and rewind marks)", !hand.PhraseShown && hand.RewindMarks == 0);
            }

            // ---- the intended route (GUIDE: read the phrase, go at once) at 4.2 m/s, finite stamina, N = 1/2/4/8, every phrase ----
            int lethalHits = 0, routes = 0, routeFails = 0; float minStamina = 100; var failed = new List<string>();
            bool cracked = false, wiped = false;
            foreach (int n in new[] { 1, 2, 4, 8 })
                foreach (var ph in HandPhrases)
                {
                    yield return HandStart();
                    if (bk == null) yield break;
                    var party = HandParty(n);
                    for (int i = 0; i < n; i++) { var b = UltimateParty.Drive(party[i], i, n); b.Speed = 4.2f; b.Attacks = false; }
                    bk.UltLaunch(hand, HandCue(ph));
                    int end = d.World.Tick + Sim.Ticks(8.5f) + 3;
                    while (d.World.Tick < end) { BkStep(1); foreach (var p in party) minStamina = Mathf.Min(minStamina, p.Stamina); }
                    routes++;
                    int hits = party.Sum(p => HandHitsOn(p));
                    lethalHits += bkHits.Count(x => x.Damage >= UltimateState.Lethal && handSpawns.Any(s => s.h.Id == x.HazardId));
                    cracked |= bk.Ult.CrackedCount > 0; wiped |= bk.Ult.Wipes > 0;
                    if (hits > 0 || party.Any(p => p.Down)) { routeFails++; failed.Add($"N{n} {ph}: {hits} hits{(party.Any(p => p.Down) ? ", down" : "")} [{bkHits.FirstOrDefault().Source}]"); }
                }
            Check("Hand: the phrase-reading route clears every phrase from the far wall at 4.2 m/s with finite stamina, N = 1/2/4/8 (strat bots)",
                  routeFails == 0, $"{routes - routeFails}/{routes} clean, stamina min {minStamina:0}{(failed.Count > 0 ? " · " + string.Join("; ", failed.Take(6)) : "")}");
            Check("Hand: its own moves never one-shot, wipe or crack (that's the Stopped Hour's chains alone)", lethalHits == 0 && !wiped && !cracked, $"lethal {lethalHits}, wipe {wiped}, cracked {cracked}");

            // ---- without the phrase: the 2.0 s telegraph alone is too short for the Leaning Bell from the far wall ----
            foreach (var ph in new[] { "1L", "1R" })
            {
                yield return HandStart();
                if (bk == null) yield break;
                HandParty(1, 12.5f);
                bk.UltLaunch(hand, HandCue(ph));
                int L = hand.LastCastTick;
                // a player who ignores the phrase and reacts 0.6 s (the escape proof's reaction) after the real telegraph
                BkStep(Sim.Ticks(5f) + 36);
                var miss = UltimateParty.Drive(d.Player, 0, 1, UltimateBot.Skill.Miss); miss.Speed = 4.2f; miss.Attacks = false;
                BkStep(Sim.Ticks(3.5f) + 3);
                Check($"Hand {ph}: ignoring the phrase fails the Leaning Bell from the far wall (2.0 s telegraph, 0.6 s reaction)", HandHitsOn(d.Player, hand.First) == 1,
                      $"hits {HandHitsOn(d.Player, hand.First)}, ended at z {d.Player.transform.position.z:0.0}");
            }

            // ---- melee and ranged danger: a still player next to the boss and one at 10 m (solo each) ----
            var threat = new List<string>(); bool threatOk = true;
            foreach (var ph in HandPhrases)
            {
                if (ph[0] == '4' && !bk.Mechanic<StoppedHour>().Built) { threat.Add($"{ph}: Stopped Hour not built here"); continue; }
                var got = new List<bool>();
                foreach (var at in new[] { new Vector3(0, 0, 2.0f), new Vector3(0, 0, 10f) })
                {
                    yield return HandStart();
                    if (bk == null) yield break;
                    BkPlace(d.Player, at);
                    bk.UltLaunch(hand, HandCue(ph));
                    UltStepAlive(Sim.Ticks(8.5f) + 3);
                    got.Add(HandHitsOn(d.Player) > 0 || bkHits.Any(x => x.Player == d.Player));
                }
                threat.Add($"{ph}: melee {(got[0] ? "hit" : "safe")}, ranged {(got[1] ? "hit" : "safe")}");
                bool wantMelee = ph != "2R";   // Toll Tide + its rewound tide never reach a player hugging the boss (recorded)
                if (ph[0] != '4' && (!got[1] || (wantMelee && !got[0]))) threatOk = false;   // 4x: the Stopped Hour's own rules decide (noted)
            }
            Check("Hand: phrases 1-3 hit a still ranged player, and all but 2R also a still melee player (4x noted)", threatOk, string.Join(" · ", threat));

            // ---- boss death mid-phrase cancels everything ----
            foreach (var ph in new[] { "3R", "4R" })
            {
                yield return HandStart();
                if (bk == null) yield break;
                HandParty(2, 6f);
                bk.UltLaunch(hand, HandCue(ph));
                UltStepAlive(Sim.Ticks(6.7f));
                int live = d.World.Hazards.Count, hitsBefore = bkHits.Count; bool shown = hand.PhraseShown;
                bk.DebugDefeat();
                BkStep(Sim.Ticks(2.5f));
                Check($"Hand {ph}: boss death mid-phrase cancels its moves, the Stopped Hour and the phrase", live > 0 && shown && d.World.Hazards.Count == 0 && !hand.Running
                      && !hand.PhraseShown && !bk.Mechanic<StoppedHour>().Running && bkHits.Count == hitsBefore, $"{live} live before, hits after {bkHits.Count - hitsBefore}");
            }

            // ---- the plan's cues: P2 (7, 8.5), P4 block 3 (7, 9.5), P5 (the fissure line) — phase-start practice ----
            BellkeeperEncounter.PushTimersOn = false;
            yield return HandStart("P2");
            if (bk == null) yield break;
            bk.BeginCombat();
            UltStepAliveUntil(() => hand.Casts > 0, 10);
            int l2 = hand.LastCastTick;
            Check("Hand in P2 (phase-start practice): the phrase at the loop's start, moves at 7.0 and 8.5 s", hand.Casts == 1 && l2 == UltTick0() && hand.Cue.Phase == 2
                  && hand.FirstAt - l2 == Sim.Ticks(7) && hand.SecondAt - l2 == Sim.Ticks(8.5f), $"{hand.Phrase} {hand.Cue}");
            UltStepAlive(Sim.Ticks(8.6f));
            Check("Hand in P2: move 2 resolved on its tick", hand.Second.Count > 0 && handSpawns.Where(s => hand.Second.Contains(s.h)).All(s => s.tick + s.h.ResolveAt - 1 == l2 + Sim.Ticks(8.5f)), hand.Reading);
            yield return HandStart("P4");
            if (bk == null) yield break;
            bk.BeginCombat();
            int tp4 = d.World.Tick;
            UltStepAliveUntil(() => hand.Casts > 0, Sim.Ticks(45));
            Check("Hand in P4: block 3 (after two replays and punishes), moves at 7.0 and 9.5 s", hand.Casts == 1 && hand.Cue.Phase == 4 && hand.SecondAt - hand.LastCastTick == Sim.Ticks(9.5f),
                  $"at {(hand.LastCastTick - tp4) / 60f:0.0}s {hand.Cue}");
            UltStepAlive(Sim.Ticks(9.6f));
            Check("Hand in P4: move 2 drawn 2.0 s before 9.5 s", handSpawns.Where(s => hand.Second.Contains(s.h)).All(s => s.tick == hand.SecondAt - ConductorsHand.TellTicks) && hand.Second.Count > 0);
            yield return HandStart("P5");
            if (bk == null) yield break;
            BkPlace(d.Player, UltimateState.FissureNormal * -6f);
            bk.BeginCombat();
            UltStepAliveUntil(() => hand.Casts > 0, 10);
            Check("Hand in P5: its Leaning Bell uses the fissure line, falling on the party's side", hand.Cue.Phase == 5 && Vector3.Dot(hand.LeanDir, -UltimateState.FissureNormal) > 0.999f, $"{hand.Phrase} dir {hand.LeanDir}");
            BellkeeperEncounter.PushTimersOn = true;

            // ---- practice hook: -lastbellAbility ult_conductors_hand loops through the phrases ----
            yield return UltStart(UltimateMechanics.ConductorsHand);
            if (bk == null) yield break;
            hand = bk.Mechanic<ConductorsHand>();
            bk.BeginCombat();
            var seen = new List<string>();
            for (int i = 0; i < Sim.Ticks(24f) && seen.Count < 2; i++) { UltStepAlive(1); if (hand.Running && (seen.Count == 0 || hand.Casts > seen.Count) && hand.Phrase != "") seen.Add(hand.Phrase); }
            Check("Hand practice hook: loops, one phrase after another (1L, then 2R)", bk.PracticeAbility == hand && seen.Count == 2 && seen[0] == "1L" && seen[1] == "2R", string.Join(" ", seen));

            // ---- evidence: what each phrase looks like at the game camera (stand-in), and one real-time clip ----
            foreach (var ph in new[] { "1L", "2R", "3L", "4R" })
            {
                yield return HandStart();
                if (bk == null) yield break;
                var party = HandParty(4, 8f);
                bk.UltLaunch(hand, HandCue(ph));
                d.SetPaused(false);
                yield return Wait(1.0f);
                yield return Shot($"ult_hand_phrase_{ph}");
                d.SetPaused(true);
            }
            yield return HandStart();
            if (bk == null) yield break;
            {
                var party = HandParty(4, 11.8f);
                for (int i = 0; i < party.Count; i++) { var b = UltimateParty.Drive(party[i], i, party.Count); b.Speed = 4.2f; b.Attacks = false; }
                bk.UltLaunch(hand, HandCue("1R"));
                d.SetPaused(false);
                StartCoroutine(Capture("ult_hand_1R", 9 * 60));
                float t = 0; bool tele = false;
                while (t < 9f) { BkTopUp(); t += Time.deltaTime; if (!tele && t >= 5.6f) { tele = true; yield return Shot("ult_hand_1R_telegraph"); continue; } yield return null; }
                yield return null;
                d.SetPaused(true);
                Check("Hand clip 1R: the reading party is never hit", d.World.Players.All(p => HandHitsOn(p) == 0));
            }
        }
    }
}
