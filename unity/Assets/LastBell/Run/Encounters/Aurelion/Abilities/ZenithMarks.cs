using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // v3-02 Zenith Marks (aurelion.zenith_marks), abilities-v3/ABILITIES-v3.md (proposal). Source: FFXIV debuff
    // resolution (DSR Wroth Flames, TOP Hello World): your position must suit a mark that resolves with a later pattern.
    // Run() marks every standing player SHADE (be within 5.5 m of his centre) or SUN (be beyond 8.5 m) 3.0 s before it
    // resolves (Easy 4.0 s). The mark is an ivory/cobalt word over the player (friendly identity). The verdict is a coral
    // Ground shape for that player only (Hazard.Affects): SHADE = a ring from 5.5 m out past the rim, SUN = a disc of
    // 8.5 m round him. It shows only 0.35 s before it lands (Easy 1.0 s): 50, no knockback, 0.25 s stun.
    // Solo alternates SUN / SHADE per cast (SUN first: the block before, Zenith Collapse, pulls you in); N >= 2 splits the standing players in half, rotating per cast.
    // The phase plan resolves it on the same tick as the paired Meridian Lances, so it is that pattern's partner (never a
    // third pattern), and never pairs it with Zenith Collapse.
    //   Escape (abilities-v3 escape check; the block walks him to the centre first): rim to shade is 8.7 m, beside his
    //   body to sun 7.05 m; the escape check gives every case its margin (abilities-v3). Memory test: the coral lead
    //   (0.35 s) + 0.25 s is below the reflex escape from the worst spot (rim to shade 0.83 s, body to sun 0.65 s).
    public sealed class ZenithMarks : AurelionAbility
    {
        public override string Id => "aurelion.zenith_marks";
        public override string Name => "Zenith Marks";

        public const float ShadeR = 5.5f, SunR = 8.5f, MarkS = 3.0f, MarkEasyS = 4.0f, LeadS = 0.35f, LeadEasyS = 1.0f;
        public const int Damage = 50, Stun = 15;

        static readonly Color SunText = new Color(1f, 0.93f, 0.7f), ShadeText = new Color(0.55f, 0.72f, 1f);

        public enum Mark { Shade, Sun }
        public sealed class Marked { public PlayerController Who; public Mark Mark; public Hazard Verdict; public int Step, ResolveTick; }
        public readonly List<Marked> Marks = new List<Marked>();   // test evidence: the last cast (every player's step 0, then step 1)
        public int MarkTick { get; private set; }
        public int Casts { get; private set; }
        public bool CueVisible { get; private set; }

        int Total => Easy ? Sim.Ticks(MarkEasyS) : Sim.Ticks(MarkS);
        int Lead => Sim.Ticks(Easy ? LeadEasyS : LeadS);

        public int ResolveTick { get; private set; }                         // the last step's resolve tick
        public bool Pending(int tick) => CueVisible && tick <= ResolveTick;   // a mark is up and has not resolved
        // the mark this player must keep now (its earliest step still to resolve), or null
        public Marked Current(PlayerController who, int tick) => Pending(tick) ? Marks.Where(m => m.Who == who && m.ResolveTick >= tick).OrderBy(m => m.ResolveTick).FirstOrDefault() : null;

        public static Mark SoloMark(int cast) => cast % 2 == 0 ? Mark.Sun : Mark.Shade;   // SUN first: Zenith Collapse has just pulled you in
        static Mark Flip(Mark m) => m == Mark.Shade ? Mark.Sun : Mark.Shade;
        static string Word(Mark m) => m == Mark.Shade ? "SHADE" : "SUN";

        // second >= 0: a two-step read (Misalignment, one step per Lances volley): every player's mark flips for a second
        // verdict resolving `second` ticks after Run's first tick. Both steps show from the start ("SHADE > SUN"); the
        // first word drops when it resolves.
        public IEnumerable<int> Run(int second = -1)
        {
            Marks.Clear(); MarkTick = W.Tick; CueVisible = true;
            var party = Standing();
            int cast = Casts++, total = Total, lead = Lead;
            bool two = second >= total + lead;
            ResolveTick = MarkTick + (two ? second : total - 1);
            var labels = new List<RunHud.WorldLabel>();
            for (int i = 0; i < party.Count; i++)
            {
                var m = party.Count == 1 ? SoloMark(cast) : ((i + cast) % 2 == 0 ? Mark.Shade : Mark.Sun);
                Marks.Add(new Marked { Who = party[i], Mark = m, Step = 0, ResolveTick = MarkTick + total - 1 });
                labels.Add(RunHud.Label(Vector3.up * 2.4f, two ? Word(m) + " > " + Word(Flip(m)) : Word(m), m == Mark.Shade ? ShadeText : SunText, 30, party[i].transform));
            }
            if (two) for (int i = 0; i < party.Count; i++) Marks.Add(new Marked { Who = party[i], Mark = Flip(Marks[i].Mark), Step = 1, ResolveTick = ResolveTick });

            yield return total - lead;                              // step 0's verdicts appear `lead` before its resolve
            Verdicts(0, lead);
            yield return lead - 1;                                   // step 0's resolve tick
            Flare(0.8f);
            if (two)
            {
                for (int i = 0; i < labels.Count; i++) { labels[i].Text = Word(Marks[party.Count + i].Mark); labels[i].Color = Marks[party.Count + i].Mark == Mark.Shade ? ShadeText : SunText; }
                yield return second - (total - 1) - lead + 1;        // step 1's verdicts appear `lead` before its resolve
                Verdicts(1, lead);
                yield return lead - 1;                               // step 1's resolve tick
                Flare(1f);
            }
            CueVisible = false;
            foreach (var l in labels) l.Hide();
        }

        void Verdicts(int step, int lead)
        {
            var at = BossPos;
            foreach (var m in Marks)
            {
                if (m.Step != step || !m.Who || m.Who.Down) continue;
                var who = m.Who;
                var h = m.Mark == Mark.Shade
                    ? new Hazard { Shape = HazardShape.Ring, Origin = at, Inner = ShadeR, Radius = BeyondRim }
                    : new Hazard { Shape = HazardShape.Circle, Origin = at, Radius = SunR };
                h.Source = Name; h.Kind = HazardKind.Ground; h.LockAt = 0; h.ResolveAt = lead;
                h.Damage = Damage; h.Knockback = 0; h.Stun = Stun;
                h.Affects = p => p == who;
                m.Verdict = Spawn(h);
            }
        }

        // ticks from Run's first tick to the last resolve tick. CONTRACT: keep it equal to Run.
        public int Duration(int second = -1) => second >= Total + Lead ? second : Total - 1;
    }
}
