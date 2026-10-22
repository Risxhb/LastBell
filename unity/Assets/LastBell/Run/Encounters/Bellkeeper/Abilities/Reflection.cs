using System.Collections.Generic;

namespace LastBell.Run
{
    // Reflection of the Hour (`bellkeeper.reflection`), Bellkeeper ability set v2 — scaffold stub.
    // Brief: bosses/4-shattered-hour-bellkeeper/abilities-v2/03-reflection.md. Owned by the Reflection of the Hour ability session: replace this
    // stub with the real ability (hazards, telegraph presentation, Easy/Hard numbers). Contract: BellkeeperAbility.cs.
    public sealed class ReflectionAbility : BellkeeperAbility
    {
        public override string Id => BellkeeperAbilities.Reflection;
        public override string Name => "Reflection of the Hour";
        public override bool Built => false;

        // Placeholder: shows the name for 1 s and spawns nothing, so the v2 phase plan runs before the ability exists.
        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name + " (not built)");
            yield return 60;
        }
    }
}
