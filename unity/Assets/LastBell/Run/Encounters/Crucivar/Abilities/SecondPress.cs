using System.Collections.Generic;

namespace LastBell.Run
{
    // 05 Second Press (crucivar.second_press), Hard only, brief 05-crucivar.second_press.md. STUB: the Second Press session
    // fills this in. Contract: Run(result) is called right after Mould Press resolves (on Hard, or from its practice hook);
    // it re-stamps the fullest mould and returns when that press has resolved.
    public sealed class SecondPress : CrucivarAbility
    {
        public override string Id => "crucivar.second_press";

        public IEnumerable<int> Run(MouldResult result)
        {
            Boss.SetCurrent("Second Press");
            yield return Sim.Ticks(2.0f);
        }
    }
}
