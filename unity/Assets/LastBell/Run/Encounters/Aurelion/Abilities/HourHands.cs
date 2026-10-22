using System.Collections.Generic;

namespace LastBell.Run
{
    // H Hour Hands (aurelion.hour_hands), Hard only, brief abilities-v2/05-hour_hands.md. STUB: the Hour Hands session
    // fills this in.
    // Contract: Run() sends a 1.8 m clock-hand spoke from the boss through each standing player's hour (snapped to 30°);
    // solo, spoke II follows from spoke I's lock. Returns on the tick the last spoke resolves. The phase plan only calls
    // it on Hard (and in practice, on any level).
    public sealed class HourHands : AurelionAbility
    {
        public override string Id => "aurelion.hour_hands";
        public override string Name => "Hour Hands";

        public IEnumerable<int> Run()
        {
            Say(Name);
            yield return N <= 1 ? T(4.0f) : T(2.5f);
        }
    }
}
