using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Bot and top-up helpers for the Aurelion v4 suite and gallery. The v3 Milestone A checks that lived here (Ecliptic Order,
    // Zenith Marks, the bot proof and decision density on the v3 set) were retired on 1 Nov (Chzy18: only the current fight is tested).
    public sealed partial class RunAutoTest
    {
        const float V3Ranged = 8.5f;   // the bots hold a ranged player's gap (Chzy18 plays solo ranged)

        void V3Bot(PlayerController p, BotMode mode) => p.Input = new AllyBot(0, 1, mode) { HoldRange = V3Ranged };
        void V3Unbot() { foreach (var p in V2Party) if (p && v2Inputs.TryGetValue(p, out var i)) p.Input = i; }
        // runs a body with every standing party member topped up each frame (an ability check, not a survival check)
        IEnumerator V3FireToppedUp(IEnumerable<int> body, float timeout = 30)
        {
            V2Begin(body); float t = 0;
            while (V2Boss != null && V2Boss.TestBusy && t < timeout)
            {
                foreach (var p in V2Party) if (p && !p.Down) p.DebugSetHp(PlayerController.MaxHp);
                t += Time.deltaTime; yield return null;
            }
        }
        string V3HitList() => string.Join(", ", V2HitsOn(V2Main).Select(h => $"{h.Source} {h.Damage}"));
    }
}
