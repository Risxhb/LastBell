using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellSeveranceShots (with -lastbellAurelionGallery -lastbellAurelionOnly): Severance's "Night is a shadow" look
    // (Chzy18 accepted 2026-10-28, work/severance-night-20261028) solo and with 4 and 8 players, an attacking Arcanist casting
    // beside it in the parties, overhead and from the gameplay camera. Screenshots only; no checks.
    public sealed partial class RunAutoTest
    {
        static bool SeveranceShots => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellSeveranceShots", StringComparison.OrdinalIgnoreCase));

        IEnumerator SeveranceShotsAll()
        {
            Note("---------- Severance captures (Night is a shadow) ----------");
            AurelionArgs.SoloOverride = null;
            bool attacks = AllyBot.Attacks;
            foreach (var cam in new[] { "", "_gamecam" })
            {
                RunDirector.ShotCamera = cam == "" ? (new Vector3(0, 27, 17), new Vector3(0, 0, 1.5f)) : ((Vector3, Vector3)?)null;
                foreach (int n in new[] { 1, 4, 8 }) yield return SeveranceShotsFor(n, cam);
            }
            AllyBot.Attacks = attacks;
            RunDirector.ShotCamera = null;
            yield return V2Teardown();
            AurelionArgs.SetOverride = null;
        }

        IEnumerator SeveranceShotsFor(int n, string cam)
        {
            AurelionArgs.SetOverride = "v4";
            yield return V2Setup(n, Difficulty.Medium, null, 3, Vector3.zero);
            if (V2Boss == null) yield break;
            // every player is a Perfect bot so each takes its called side; in a party the last one is an attacking Arcanist
            if (n > 1)
            {
                var last = V2Party[n - 1]; V2W.Players.Remove(last); v2Inputs.Remove(last); Destroy(last.gameObject);
                var arc = PlayerController.Spawn(V2W, "Arcanist", new Vector3(4, 0, 4), 180);
                arc.AbilitiesEnabled = true; arc.PrepareForEncounter(new Vector3(4, 0, 4), 180); V2Party[n - 1] = arc;
                var ai = new V2Input(); arc.Input = ai; v2Inputs[arc] = ai;   // the setup helpers address every party member's test input
            }
            AllyBot.Attacks = n > 1;
            for (int i = 0; i < V2Party.Count; i++) V2Party[i].Input = new AllyBot(i, V2Party.Count, BotMode.Perfect) { HoldRange = V3Ranged };
            V2Begin(V2Boss.Sever.Run());
            int at = 0;
            foreach (var (when, tag) in new[] { (Sim.Ticks(1.8f), "split"), (Sim.Ticks(5f), "live"), (Sim.Ticks(8f), "live2") })
            {
                yield return V2Ticks(when - at); at = when;
                foreach (var p in V2Party) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp);
                yield return Shot($"aurelion_severance_{n}p_{tag}{cam}");
            }
            AllyBot.Attacks = false;
            yield return V2WaitIdle(40);
            V3Unbot();
        }
    }
}
