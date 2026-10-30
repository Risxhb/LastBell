using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LastBell.Run
{
    // Ultimate v2 seed sweep: the same Perfect runs over several seeds (BellkeeperEncounter.SeedOffset turns the party's
    // starting spots and the phases' seeded choices), so a bot change is judged by a clear rate, not one deterministic path.
    //   -lastbellBellkeeperUltimate -lastbellTestAbility bots_v2_seeds [-lastbellUltSeeds 8] [-lastbellUltRuns p3,fight]
    //   p3     P3 alone from its practice start, party of 4; the attempt ends (a pass) when P3 is cleared
    //   fight  the full fight from P1 on Hard, party of 4
    public sealed partial class RunAutoTest
    {
        IEnumerator UltV2SeedSweep()
        {
            Note("---------- Ultimate v2: seed sweep (Perfect party of 4, Hard) ----------");
            int seeds = int.TryParse(AurelionArgs.Arg("-lastbellUltSeeds"), out int n) ? n : 8;
            string rn = AurelionArgs.Arg("-lastbellUltRuns") ?? "p3,fight";
            int seed0 = BellkeeperEncounter.SeedOffset;
            OpenHour.LogSteer = AurelionArgs.Arg("-lastbellUltSteerLog") != null; GearFloor.Log = OpenHour.LogSteer;   // every cornered P3 route search and every P3 hit
            var causes = new Dictionary<string, int>();
            foreach (var kind in new[] { "p3", "fight" })
            {
                if (!rn.Split(',').Contains(kind)) continue;
                int pass = 0; var times = new List<int>();
                for (int s = 0; s < seeds; s++)
                {
                    BellkeeperEncounter.SeedOffset = seed0 + s; BellkeeperEncounter.V2Override = true; BellkeeperEncounter.V2StopAfterP4 = false;
                    if (kind == "p3")
                        yield return UltRun($"seed {s} · P3 alone", 4, UltimateBot.Skill.Perfect, "P3", 300, "Vanguard",
                            f => { if (bk != null && !bk.Dead && bk.Segment != BellkeeperEncounter.UltSegment.P3) bk.DebugDefeat(); });
                    else
                        yield return UltRun($"seed {s} · full fight", 4, UltimateBot.Skill.Perfect, null, 900, "Vanguard");
                    var f2 = ultLast; if (f2 == null) continue;
                    // P3 alone passes when P3 ends with nobody down in it (what follows is P4's business)
                    bool ok = kind == "p3" ? f2.Segments.Any(x => x.seg == BellkeeperEncounter.UltSegment.P4) && !f2.DeathLines.Any(l => l.Contains(" in P3 ")) : f2.Killed && f2.Deaths == 0;
                    if (ok) { pass++; times.Add(f2.Ticks / 60); }
                    foreach (var l in f2.DeathLines)
                    {
                        int by = l.IndexOf(" by "), par = l.IndexOf(" (", by + 4);
                        string c = by < 0 ? l : l.Substring(by + 4, (par > by ? par : l.Length) - by - 4);
                        int inSeg = l.IndexOf(" in "); string seg = inSeg < 0 ? "?" : l.Substring(inSeg + 4, 2);
                        string key = $"{kind} {seg}: {c}"; causes[key] = causes.TryGetValue(key, out int had) ? had + 1 : 1;
                    }
                }
                Note($"SEEDS {kind}: {pass}/{seeds} clean clears" + (times.Count > 0 ? $" · clear times {string.Join(" ", times.Select(x => $"{x / 60}:{x % 60:00}"))}" : ""));
            }
            foreach (var kv in causes.OrderByDescending(x => x.Value)) Note($"   deaths: {kv.Value}x {kv.Key}");
            BellkeeperEncounter.SeedOffset = seed0; BellkeeperEncounter.V2Override = null;
        }
    }
}
