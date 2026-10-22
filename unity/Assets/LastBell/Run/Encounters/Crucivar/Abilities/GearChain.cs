using System.Collections.Generic;

namespace LastBell.Run
{
    // 02 Gear Chain (crucivar.gear_chain), brief 02-crucivar.gear_chain.md. STUB: the Gear Chain session fills this in.
    // Contract: Run() clamps the chains and returns when they have resolved.
    public sealed class GearChain : CrucivarAbility
    {
        public override string Id => "crucivar.gear_chain";

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Gear Chain");
            yield return T(3.0f);
        }
    }
}
