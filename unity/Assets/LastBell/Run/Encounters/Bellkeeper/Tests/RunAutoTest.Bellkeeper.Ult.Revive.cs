using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Revives in the Ultimate (accepted 4 Nov, PR 14): the Ultimate's bots (UltimateBot) get a downed ally up as AllyBot does
    // for the other bosses. -lastbellTestAbility bots_revive: in each phase, a party of 4 from its practice start; 3 s in, one
    // bot is forced down, and another bot must revive it before it bleeds out (20 s). A run that ends sooner (the downed
    // player's own job left undone wipes the party) is reported, not failed: the bots can only revive while the fight goes on.
    public sealed partial class RunAutoTest
    {
        IEnumerator UltReviveChecks()
        {
            Note("---------- Ultimate · bot revives ----------");
            foreach (var seg in new[] { "P1", "P2", "P3", "P4", "P5" })
            {
                PlayerController downed = null; int downAt = -1, upAt = -1, start = -1, upHp = 0;
                yield return UltRun($"revive {seg} party4", 4, UltimateBot.Skill.Perfect, seg, 32, "Tollbreaker", f =>
                {
                    if (start < 0) start = d.World.Tick;
                    if (downed == null && d.World.Tick - start >= Sim.Ticks(3))
                    {
                        downed = d.World.Players.Skip(1).FirstOrDefault(p => !p.Down);
                        if (downed != null) { downed.ForceDown("revive test"); downAt = d.World.Tick; }
                    }
                    if (downed != null && upAt < 0 && !downed.Down) { upAt = d.World.Tick; upHp = downed.Hp; }   // its HP as it stands (the run may wipe later)
                    // trace: each standing bot's job and gap to the downed player, every second until the revive
                    if (downed != null && upAt < 0 && (d.World.Tick - downAt) % 60 == 0 && bk?.Mechanics != null)
                    {
                        var m = bk.Mechanics.FirstOrDefault(x => x.Running);
                        Note($"   {(d.World.Tick - downAt) / 60}s: downed at ({downed.transform.position.x:0.0},{downed.transform.position.z:0.0}) · " + string.Join(" · ",
                            d.World.Players.Where(p => p != downed && !p.Down).Select(p => $"{p.ClassName} {Sim.Flat(p.transform.position - downed.transform.position).magnitude:0.0}m {m?.Job(p) ?? (p.Input as UltimateBot)?.DebugLine}")));
                    }
                });
                if (ultLast == null || downed == null) { Check($"revive {seg}: a bot was forced down", false); continue; }
                float took = upAt >= 0 ? (upAt - downAt) / 60f : -1, ran = (start + ultLast.Ticks - downAt) / 60f;
                string what = upAt >= 0 ? $"{downed.ClassName} up after {took:0.0} s with {upHp} HP" : $"{downed.ClassName} not revived ({ran:0.0} s of fight after the down; {ultLast})";
                if (upAt < 0 && ran < PlayerController.BleedOutSeconds) { Note($"   revive {seg}: the run ended {ran:0.0} s after the down, before any revive · {what}"); continue; }
                Check($"revive {seg}: a bot revives the downed player before they bleed out", upAt >= 0 && took < PlayerController.BleedOutSeconds, what);
            }
        }
    }
}
