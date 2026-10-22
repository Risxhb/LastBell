using System.Collections.Generic;

namespace LastBell.Run
{
    // 04 The Great Casting (crucivar.great_casting), brief 04-crucivar.great_casting.md. STUB: the Great Casting session
    // fills this in. Contract: Run(spokes) with spokes 0 (phase 1: pour only), 3 (phase 2) or 6 (phase 3); returns when
    // the last hazard has resolved (3.5 s pour only, 7.0 s with the march).
    public sealed class GreatCasting : CrucivarAbility
    {
        public override string Id => "crucivar.great_casting";

        public IEnumerable<int> Run(int spokes)
        {
            Boss.SetCurrent("The Great Casting");
            yield return T(spokes > 0 ? 7.0f : 3.5f);
        }
    }
}
