using System.Collections.Generic;

namespace LastBell.Run
{
    // The Last Toll Tide (`bellkeeper.toll_tide`), Bellkeeper ability set v2 — scaffold stub.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/04-toll-tide.md. Owned by the Last Toll Tide ability session: replace this
    // stub with the real ability (hazards, telegraph presentation, Easy/Hard numbers). Contract: BellkeeperAbility.cs.
    public sealed class TollTideAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.TollTide;
        public override string Name => "The Last Toll Tide";
        public override bool Built => false;

        // Placeholder: shows the name for 1 s and spawns nothing, so the v2 phase plan runs before the ability exists.
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name + " (not built)");
            yield return 60;
        }
    }
}
