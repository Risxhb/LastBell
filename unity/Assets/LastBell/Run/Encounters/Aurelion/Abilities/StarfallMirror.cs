using System.Collections.Generic;

namespace LastBell.Run
{
    // 02 Starfall Mirror (aurelion.starfall_mirror), brief abilities-v2/02-starfall_mirror.md. STUB: the Starfall Mirror
    // session fills this in.
    // Contract: Run() puts a star on each standing player plus a mirror star at their reflection through the arena
    // centre (solo: star I, then star II from star I's lock). Returns on the tick the last star resolves. The phase plan
    // starts it inside other patterns (0.7 s into Noon Shadow, 0.8 s into Grand Orrery, at the Horizon's start), so it
    // must not assume it runs alone.
    public sealed class StarfallMirror : AurelionAbility
    {
        public override string Id => "aurelion.starfall_mirror";
        public override string Name => "Starfall Mirror";

        public IEnumerable<int> Run()
        {
            Say(Name);
            yield return N <= 1 ? T(5.1f) : T(3.1f);
        }
    }
}
