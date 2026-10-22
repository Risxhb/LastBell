using System.Collections.Generic;

namespace LastBell.Run
{
    // Striking Order (`bellkeeper.striking_order`), Bellkeeper ability set v2 — scaffold stub.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/02-striking-order.md. Owned by the Striking Order ability session: replace this
    // stub with the real ability (hazards, telegraph presentation, Easy/Hard numbers). Contract: BellkeeperAbility.cs.
    public sealed class StrikingOrderAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.StrikingOrder;
        public override string Name => "Striking Order";
        public override bool Built => false;

        // Placeholder: shows the name for 1 s and spawns nothing, so the v2 phase plan runs before the ability exists.
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name + " (not built)");
            yield return 60;
        }
    }
}
