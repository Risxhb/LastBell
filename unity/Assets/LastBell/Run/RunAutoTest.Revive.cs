using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Revives (docs/proposals/revives-2026-11-04.md): a downed ally gets up after 3 s of a still ally beside them, a hit on
    // the reviver starts it over, 30 s unrevived is dead (no revive), and a bot ally comes over to revive a downed player.
    // Runs with the party checks (-lastbellPartyOnly). The boss's hazards are cleared every frame so only the test hurts.
    public sealed partial class RunAutoTest
    {
        sealed class HoldStill : IInputSource { public PlayerInput Read(PlayerController who) => new PlayerInput { Aim = who.transform.position + Vector3.forward }; }

        IEnumerator ReviveChecks()
        {
            Note("---------- revives ----------");
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            Check("revives: the downed HUD art loads (three class bells, the prompt, a readable ring)", new[] { "tollbreaker", "chimesinger", "arcanist" }.All(c => RunHud.Icon("revive/downed-" + c)) && RunHud.Icon("revive/revive-prompt") && RunHud.Icon("revive/revive-ring") && RunHud.Icon("revive/revive-ring").isReadable);
            Check("revives: one ally selected", d.SelectAllies(1) && RunParty.Selected == 1);
            d.StartPractice("Tollbreaker", 0); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 6, "revive practice combat");
            if (d.State != RunState.Combat || d.Allies.Count != 1) { Check("revives: party practice started", false, $"{d.State}, {d.Allies.Count} allies"); yield break; }
            var me = d.Player; var ally = d.Allies[0];
            // calm: no boss hazards; pinned: the reviver held beside the downed ally (the boss walking through shoves them)
            bool calm = true; PlayerController pinA = null, pinB = null; Vector3 pinOff = Vector3.zero;
            IEnumerator Calm() { while (calm) { d.World?.Hazards.ClearAll(); if (pinA && pinB) pinA.transform.position = pinB.transform.position + pinOff; yield return null; } }
            StartCoroutine(Calm());
            me.Input = new HoldStill(); ally.Input = new HoldStill();
            var w = d.World;

            // 1) a still ally beside a downed one gets them up in 3 s, with 40 HP
            pinA = me; pinB = ally; pinOff = new Vector3(0.9f, 0, 0);
            me.transform.position = ally.transform.position + pinOff;
            ally.ForceDown("revive test");
            Check("revives: a downed ally is down, not dead", ally.Down && !ally.Dead);
            Check("revives: the fight goes on while one player stands", d.State == RunState.Combat && w.AnyStanding);
            int t0 = w.Tick;
            yield return Until(() => !ally.Down, 8, "the ally revived");
            float took = (w.Tick - t0) / (float)Sim.Hz;
            Check("revives: a still ally beside them revives them in about 3 s", !ally.Down && took >= 2.9f && took <= 3.4f, $"{took:0.00} s");
            Check("revives: revived with 40 HP and counted", ally.Hp == PlayerController.ReviveHp && ally.Revives == 1, $"hp {ally.Hp}, revives {ally.Revives}");
            Check("revives: the GetUp clip plays (no placeholder)", ally.Presenter.CurrentClip == "GetUp", ally.Presenter.CurrentClip);

            // 2) a hit on the reviver starts the revive over
            ally.ForceDown("revive test");
            yield return Until(() => ally.ReviveProgress > 0.45f, 4, "half a revive");
            yield return Shot("revive_hud_half");   // the downed bell in its half-filled ring, the prompt beside it
            me.ReceiveHit(new Hazard { Source = "revive test hit", Damage = 5, Stun = 0, Knockback = 0, Origin = me.transform.position });
            int hitTick = w.Tick; yield return Until(() => w.Tick >= hitTick + 2, 1, "two ticks");
            Check("revives: a hit on the reviver resets the revive", ally.ReviveProgress < 0.1f, $"progress {ally.ReviveProgress:0.00}");
            for (int s = 0; s < 6 && ally.Down; s++)
            {
                int ts = w.Tick; yield return Until(() => !ally.Down || w.Tick >= ts + Sim.Hz, 3, "a second");
                if (ally.Down) Note($"after the hit {s + 1} s: {Sim.Flat(ally.transform.position - me.transform.position).magnitude:0.00} m, channel {me.CanChannelRevive} (stun {me.Stunned}, dash {me.Dashing}, acting {me.Kit.Acting}, move {me.Last.Move}, hurt {me.LastHurtTick}/{w.Tick}), progress {ally.ReviveProgress:0.00}");
            }
            Check("revives: the revive finishes after the hit", !ally.Down && ally.Revives == 2);

            // 3) moving away pauses it; 30 s unrevived is dead, and the dead can't be revived
            pinOff = new Vector3(6f, 0, 0); me.transform.position = ally.transform.position + pinOff;
            ally.ForceDown("revive test");
            yield return Until(() => ally.Dead, PlayerController.BleedOutSeconds + 6, "bled out");
            Check("revives: no revive from 6 m away", ally.ReviveProgress == 0, $"progress {ally.ReviveProgress:0.00}");
            Check("revives: 30 s unrevived is dead", ally.Dead && Mathf.Abs(ally.DownedTicks / (float)Sim.Hz - PlayerController.BleedOutSeconds) < 0.1f, $"{ally.DownedTicks / (float)Sim.Hz:0.0} s");
            Check("revives: the Death clip plays", ally.Presenter.CurrentClip == "Death", ally.Presenter.CurrentClip);
            pinOff = new Vector3(0.9f, 0, 0); me.transform.position = ally.transform.position + pinOff;
            int t1 = w.Tick; yield return Until(() => w.Tick >= t1 + Sim.Ticks(4f), 8, "4 s beside the dead ally");
            Check("revives: the dead can't be revived", ally.Down && ally.Dead && ally.ReviveProgress == 0);

            // 4) a bot ally walks over and revives a downed player (the player down, the ally a bot again)
            pinA = pinB = null;
            d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "revive practice results");
            d.ReturnToMenu(); yield return null;
            d.StartPractice("Tollbreaker", 0); d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 6, "second revive practice combat");
            me = d.Player; ally = d.Allies[0]; w = d.World;
            Check("revives: each fight starts with everyone up", !ally.Down && !ally.Dead && !me.Down);
            me.Input = new HoldStill();
            ally.transform.position = me.transform.position + new Vector3(5f, 0, 0);
            me.ForceDown("revive test");
            int t2 = w.Tick; var bot = ally.Input as AllyBot;
            for (int s = 0; s < 12 && me.Down; s++)
            {
                int ts = w.Tick; yield return Until(() => !me.Down || w.Tick >= ts + Sim.Hz, 3, "a second");
                if (me.Down) Note($"revive bot {s + 1} s: {Sim.Flat(ally.transform.position - me.transform.position).magnitude:0.00} m, channel {ally.CanChannelRevive}, reviving {ally.Reviving != null}, progress {me.ReviveProgress:0.00} | {bot?.DebugLine}");
            }
            Check("revives: a bot ally comes over and revives the downed player", !me.Down && me.Revives == 1, $"{(w.Tick - t2) / (float)Sim.Hz:0.0} s");
            calm = false;
            if (d.Boss && !d.Boss.Dead) d.Boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Results, 8, "revive practice results");
            d.ReturnToMenu(); yield return null;
            d.SelectAllies(0);
        }
    }
}
