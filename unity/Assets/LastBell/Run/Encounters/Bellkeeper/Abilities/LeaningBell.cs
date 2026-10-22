using System.Collections.Generic;

namespace LastBell.Run
{
    // Leaning Bell (`bellkeeper.leaning_bell`), Bellkeeper ability set v2 — scaffold stub.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/01-leaning-bell.md. Owned by the Leaning Bell ability session: replace this
    // stub with the real ability (hazards, telegraph presentation, Easy/Hard numbers). Contract: BellkeeperAbility.cs.
    public sealed class LeaningBellAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.LeaningBell;
        public override string Name => "Leaning Bell";
        public override bool Built => false;

        // Placeholder: shows the name for 1 s and spawns nothing, so the v2 phase plan runs before the ability exists.
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name + " (not built)");
            yield return 60;
        }
    }
}
