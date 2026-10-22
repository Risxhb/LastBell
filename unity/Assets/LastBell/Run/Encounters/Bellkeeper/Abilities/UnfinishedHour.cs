using System.Collections.Generic;

namespace LastBell.Run
{
    // The Unfinished Hour (`bellkeeper.unfinished_hour`), Bellkeeper ability set v2 — scaffold stub.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/05-unfinished-hour.md. Owned by the Unfinished Hour ability session: replace this
    // stub with the real ability (hazards, telegraph presentation, Easy/Hard numbers). Contract: BellkeeperAbility.cs.
    public sealed class UnfinishedHourAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.UnfinishedHour;
        public override string Name => "The Unfinished Hour";
        public override bool Built => false;

        // Placeholder: shows the name for 1 s and spawns nothing, so the v2 phase plan runs before the ability exists.
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name + " (not built)");
            yield return 60;
        }
    }
}
