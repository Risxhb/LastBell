using System.Collections.Generic;

namespace LastBell.Run
{
    // 03 Noon Shadow (aurelion.noon_shadow), brief abilities-v2/03-noon_shadow.md. STUB: the Noon Shadow session fills
    // this in.
    // Contract: Run(afternoon) lights the half of the arena holding the players' mean (Noon), locked at once. With
    // afternoon (Phase 3, everyone) or solo (always), the other half follows on Noon's resolve tick. Returns on the tick
    // the last half resolves.
    public sealed class NoonShadow : AurelionAbility
    {
        public override string Id => "aurelion.noon_shadow";
        public override string Name => "Noon Shadow";

        public IEnumerable<int> Run(bool afternoon)
        {
            Say(Name);
            yield return T(3.8f);
            if (afternoon || N <= 1) yield return T(3.8f);
        }
    }
}
