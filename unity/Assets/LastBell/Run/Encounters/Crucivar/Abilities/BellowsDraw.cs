using System.Collections.Generic;

namespace LastBell.Run
{
    // 03 Bellows Draw (crucivar.bellows_draw), brief 03-crucivar.bellows_draw.md. STUB: the Bellows Draw session fills it in.
    // Contract: Run() is the full ability (slag rim + edge, inhale, blast) and returns when the blast has resolved (the slag
    // edge keeps burning on its own). SlagRim() spawns only the rim and its burning edge, returns at once, and is paired
    // by the phase plan with Mould Press or Gear Chain.
    public sealed class BellowsDraw : CrucivarAbility
    {
        public override string Id => "crucivar.bellows_draw";

        public IEnumerable<int> Run()
        {
            Boss.SetCurrent("Bellows Draw");
            SlagRim();
            yield return T(3.5f);
        }

        public void SlagRim() { }
    }
}
