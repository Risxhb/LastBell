using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // 01 Mould Press (crucivar.mould_press), brief 01-crucivar.mould_press.md. STUB: the Mould Press session fills this in.
    // Contract: Run() raises the moulds for the current alive-player count and returns when the last tower has resolved,
    // raising Resolved with the per-tower result first (Second Press reads it).
    public sealed class MouldPress : CrucivarAbility
    {
        public override string Id => "crucivar.mould_press";
        public event Action<MouldResult> Resolved;
        public MouldResult Last { get; private set; }

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Mould Press");
            yield return T(4.2f);
            Last = new MouldResult { Centres = new Vector3[0], Numerals = new int[0], Bays = new int[0], Counts = new int[0] };
            Resolved?.Invoke(Last);
        }
    }
}
