using System.Collections.Generic;

namespace LastBell.Run
{
    // 04 Orrery Closing (aurelion.orrery_closing), the signature, brief abilities-v2/04-orrery_closing.md. STUB: the
    // Orrery Closing session fills this in.
    // Contract: Run(horizonTicks) closes the rim ring (8.5 m out from the arena centre) and the crown (3.6 m on the
    // boss), both locked at once. It returns on the tick they resolve. horizonTicks > 0 (Phase 2+: 240, Hard 360) keeps
    // the rim hitting for that long after the resolve (the Horizon, Hazard.ActiveTicks); HorizonLive is true until it
    // expires. The phase plan runs Starfall / Hour Hands inside the Horizon and starts the punish when it ends.
    public sealed class OrreryClosing : AurelionAbility
    {
        public override string Id => "aurelion.orrery_closing";
        public override string Name => "Orrery Closing";

        int horizonEnd = -1;
        public bool HorizonLive => W != null && horizonEnd >= 0 && W.Tick < horizonEnd;

        public IEnumerable<int> Run(int horizonTicks)
        {
            Say(Name);
            int warn = T(2.3f);
            horizonEnd = horizonTicks > 0 ? W.Tick + warn + horizonTicks : -1;
            yield return warn;
        }
    }
}
