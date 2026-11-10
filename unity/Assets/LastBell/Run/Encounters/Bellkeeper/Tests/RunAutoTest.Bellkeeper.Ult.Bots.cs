using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bellkeeper Ultimate bot fights: UltRun steps one fight of strat bots (UltimateBot, GUIDE.md) in exact combat ticks with
    // the director paused, so a 12-minute fight takes a minute or two of real time. Used by the phase slices (Ult.P1V2.cs),
    // the full fight (Ult.V2Fight.cs) and the level and seed sweeps. (The v1 plan's DPS, clear and video runs were deleted
    // with it, Chzy18 1 Nov.)
    public sealed partial class RunAutoTest
    {
        static readonly string[] PartyClasses = { "Tollbreaker", "Chimesinger", "Arcanist", "Tollbreaker" };   // Tollbreaker replaces the retired Vanguard (2 Nov)

        sealed class UltFight
        {
            public string Label; public bool Killed, Wiped; public string Cause; public int Ticks, Deaths, Potions;
            public readonly List<(BellkeeperEncounter.UltSegment seg, int tick)> Segments = new List<(BellkeeperEncounter.UltSegment, int)>();
            public readonly Dictionary<BellkeeperEncounter.UltSegment, int> Decisions = new Dictionary<BellkeeperEncounter.UltSegment, int>();
            public readonly List<string> DeathLines = new List<string>();
            public int Dealt; public float Hp01;
            public string[] Clips = new string[0];   // the boss's cast clips played (graphics audit, 8 Nov)
            public override string ToString() =>
                $"{Label}: {(Killed ? "KILL" : Wiped ? "WIPE (" + Cause + ")" : "time cap")} at {Ticks / 3600}:{Ticks / 60 % 60:00} · boss {Hp01:P1} left · " +
                $"deaths {Deaths} · potions {Potions} · segments {string.Join(" ", Segments.Select(s => $"{s.seg}@{s.tick / 60f:0}s"))}";
        }
        UltFight ultLast;

        // One stepped fight: n strat bots of `skill` (the run's player is bot 0), from `segment` (null = P1), until the boss
        // dies, the party is down, or capSeconds pass.
        IEnumerator UltRun(string label, int n, UltimateBot.Skill skill, string segment = null, float capSeconds = 1200, string cls0 = "Tollbreaker",
                           Action<UltFight> perSecond = null /* every 0.25 s */, bool pushOn = true, Difficulty level = Difficulty.Hard)
        {
            ultLast = null;
            if (AurelionArgs.Arg("-lastbellUltSteerLog") != null) OpenHour.LogSteer = GearFloor.Log = true;   // tests: every bot hit and cornered route
            yield return UltStart(null, segment, cls0, level);
            if (bk == null) yield break;
            BellkeeperEncounter.PushTimersOn = pushOn;
            var classes = PartyClasses.Skip(1).ToArray();
            var bots = UltimateParty.Fill(d.World, n, skill, n > 1 ? new[] { cls0 }.Concat(classes).ToArray() : new[] { cls0 });
            // -lastbellUltViaAllyBot: the same party through the shared AllyBot (what other suites and tests drive players with)
            if (AurelionArgs.Arg("-lastbellUltViaAllyBot") != null && skill == UltimateBot.Skill.Perfect)
                for (int i = 0; i < d.World.Players.Count; i++) { var ab = new AllyBot(i, d.World.Players.Count, BotMode.Perfect); d.World.Players[i].Input = ab; bots[i] = ab.Ultimate; }
            // the seed also turns the party's starting spots round the boss
            var turn = Quaternion.Euler(0, (BellkeeperEncounter.SeedOffset * 47) % 360, 0);
            foreach (var p in d.World.Players) BkPlace(p, turn * p.transform.position);
            foreach (var b in bots) b.Speed = PlayerController.WalkSpeed;
            var f = new UltFight { Label = label };
            var down = new HashSet<PlayerController>();
            int potions0 = d.World.Players.Sum(p => p.PotionsDrunk);
            bk.BeginCombat();
            int t0 = d.World.Tick, cap = Sim.Ticks(capSeconds), lastSeg = -1, decisions0 = 0;
            while (!bk.Dead && d.World.AnyStanding && d.World.Tick - t0 < cap)
            {
                for (int i = 0; i < 240 && !bk.Dead && d.World.AnyStanding && d.World.Tick - t0 < cap; i++)
                {
                    d.World.Step();
                    if ((int)bk.Segment != lastSeg && bk.SegmentStartTick >= 0)
                    {
                        int dec = bots.Sum(b => b.Decisions);
                        if (lastSeg >= 0) f.Decisions[(BellkeeperEncounter.UltSegment)lastSeg] = dec - decisions0;
                        decisions0 = dec; lastSeg = (int)bk.Segment; f.Segments.Add((bk.Segment, d.World.Tick - t0));
                    }
                    foreach (var p in d.World.Players)
                        if (p.Down && down.Add(p))
                        {
                            var last = bkHits.LastOrDefault(h => h.Player == p);
                            f.DeathLines.Add($"{p.name} down at {(d.World.Tick - t0) / 60f:0.0}s in {bk.Segment} by {last.Source} ({last.Damage}) · {(p.Input as UltimateBot)?.DebugLine}");
                        }
                    if (perSecond != null && (d.World.Tick - t0) % 15 == 0) perSecond(f);
                }
                yield return null;
            }
            if (lastSeg >= 0) f.Decisions[(BellkeeperEncounter.UltSegment)lastSeg] = bots.Sum(b => b.Decisions) - decisions0;
            f.Killed = bk.Dead; f.Wiped = !d.World.AnyStanding; f.Cause = bk.Ult.WipeCause ?? d.World.LastDamageSource;
            f.Ticks = d.World.Tick - t0; f.Deaths = down.Count; f.Potions = d.World.Players.Sum(p => p.PotionsDrunk) - potions0;
            f.Dealt = d.World.DealtBy.Values.Sum(); f.Hp01 = bk.Hp / (float)bk.MaxHp;
            f.Clips = bk.UltClipsPlayed.OrderBy(c => c).ToArray();
            ultLast = f;
            Note(f.ToString());
            foreach (var l in f.DeathLines.Take(6)) Note("   " + l);
            Note("   plan: " + string.Join(" · ", bk.PlanLog.Where(e => e.what.StartsWith("segment") || e.what.StartsWith("wipe") || e.what.StartsWith("second"))
                .Select(e => $"{e.what}@{(e.tick - t0) / 60f:0.0}s")) + $" · max HP {bk.MaxHp}");
            BellkeeperEncounter.PushTimersOn = true;
        }

    }
}
