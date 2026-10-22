using System.Collections.Generic;

namespace LastBell.Run
{
    // 01 Meridian Lances (aurelion.meridian_lances), brief abilities-v2/01-meridian_lances.md. STUB: the Meridian Lances
    // session fills this in.
    // Contract: Run(beams, volleys, cascade) fires 3 or 4 full-arena beams through the boss. Volley 1 aims through the
    // farthest player; volley 2 (volleys == 2) through the nearest, or, with cascade (Phase 3), rotated 22.5° from
    // volley 1 without tracking. Returns on the tick the last volley resolves.
    public sealed class MeridianLances : AurelionAbility
    {
        public override string Id => "aurelion.meridian_lances";
        public override string Name => "Meridian Lances";

        public IEnumerable<int> Run(int beams, int volleys, bool cascade)
        {
            Say(Name);
            yield return T(2.4f);
            if (volleys > 1) yield return T(2.2f);
        }
    }
}
