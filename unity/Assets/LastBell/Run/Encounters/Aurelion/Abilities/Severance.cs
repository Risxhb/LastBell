using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Severance: Day and Night (aurelion.severance), Phase 3, abilities-v4/ABILITIES-v4.md §3.5 (proposal). Source:
    // L'ura's Severance (two dimensions; crossing kills, an empty side wipes), adapted: no one-shots, the line moves.
    //   - A gold meridian line splits the dais through the centre; the far half is Night (violet), the near half Day.
    //     Each player's side is where they stand when it lands.
    //   - The line turns 90 degrees over 25 s, so both halves sweep round the arena: keep moving ahead of it.
    //   - Crossing the line (by moving, or by letting it sweep over you) is a heavy hit (50) every time.
    //   - Co-op (N >= 2): if every standing player is on one side for 0.5 s, the other half falls: the party wipes.
    //   - Patterns during it: Starfall Mirror at 2 s and 14 s, Meridian Lances (4 beams) at 8 s and 20 s (with the line,
    //     three patterns). Solo: the empty-side rule never applies.
    //   - Ends with Reunion (co-op only): a tower at the centre that every player must stand in (an empty one wipes).
    public sealed class Severance : AurelionAbility
    {
        public override string Id => "aurelion.severance";
        public override string Name => "Severance";

        public const float LengthS = 25f, TurnDeg = 90f, EmptySideS = 0.5f;
        public const int Damage = 50;
        static readonly Color Gold = new Color(2.2f, 1.7f, 0.6f), NightFill = new Color(0.42f, 0.22f, 0.7f), NightRim = new Color(0.9f, 0.55f, 1.8f);
        static readonly float[] StartYaws = { 0f, 60f, 120f };

        public bool Active { get; private set; }
        public float LineYaw { get; private set; }                 // the line runs along LineYaw through the centre
        public int Crossings { get; private set; }
        public int Casts { get; private set; }
        readonly Dictionary<PlayerController, int> side = new Dictionary<PlayerController, int>();

        // +1 Night (left of the line's direction), -1 Day; 0 on the line itself
        public int SideOf(Vector3 p) => SideOfAt(p, LineYaw);
        public int SideOfAt(Vector3 p, float yaw) { var d = Sim.Dir(yaw); var v = Sim.Flat(p - Centre); float c = d.x * v.z - d.z * v.x; return Mathf.Abs(c) < 0.05f ? 0 : c > 0 ? 1 : -1; }
        public float DistToLine(Vector3 p) { var d = Sim.Dir(LineYaw); var v = Sim.Flat(p - Centre); return Mathf.Abs(d.x * v.z - d.z * v.x); }
        public int Assigned(PlayerController p) => side.TryGetValue(p, out int s) ? s : 0;

        public IEnumerable<int> Run()
        {
            Say(Name);
            Active = true; Crossings = 0; side.Clear();
            float yaw0 = StartYaws[Casts++ % StartYaws.Length]; LineYaw = yaw0;
            RunHud.Banner("SEVERANCE — DAY AND NIGHT", new Color(0.85f, 0.7f, 1f), 2f);
            foreach (var p in Standing()) { int s = SideOf(p.transform.position); side[p] = s == 0 ? 1 : s; }
            var line = W.BossFx.Lane(2 * (Rim + 0.5f), 0.16f); line.Tint(Gold, Gold); line.Progress(1); line.Lock();
            var night = W.BossFx.Cone(Rim + 0.5f, 90f); night.Tint(NightFill, NightRim); night.Progress(0.35f);
            Glow(1.6f); Flare(1.2f);

            var side2 = Patterns().GetEnumerator(); int wait = 0; bool on = true;
            int len = Sim.Ticks(LengthS), empty = 0;
            for (int t = 0; t < len; t++)
            {
                LineYaw = yaw0 + TurnDeg * t / len;
                line.Place(Centre - Sim.Dir(LineYaw) * (Rim + 0.5f), LineYaw);
                night.Place(Centre, LineYaw - 90f);
                Cross();
                if (N >= 2)
                {
                    bool day = false, nightOn = false;
                    foreach (var p in Standing()) { if (Assigned(p) > 0) nightOn = true; else day = true; }
                    empty = day && nightOn ? 0 : empty + 1;
                    if (empty > Sim.Ticks(EmptySideS)) { Boss.Wipe("Severance — the " + (day ? "Night" : "Day") + " half fell"); break; }
                }
                if (on) { if (wait > 0) wait--; else if (side2.MoveNext()) wait = Mathf.Max(1, side2.Current) - 1; else on = false; }
                yield return 1;
            }
            line.Expire(); night.Expire();
            Active = false;
            if (N >= 2) foreach (var w in Boss.Towers.RunReunion()) yield return w;
        }

        IEnumerable<int> Patterns()
        {
            yield return Sim.Ticks(2f);
            foreach (var w in Boss.Starfall.Run()) yield return w;
            yield return Sim.Ticks(1f);
            foreach (var w in Boss.Lances.Run(4, 1, false)) yield return w;
            yield return Sim.Ticks(1.5f);
            foreach (var w in Boss.Starfall.Run()) yield return w;
            yield return Sim.Ticks(1f);
            foreach (var w in Boss.Lances.Run(4, 1, false)) yield return w;
        }

        // the line is a wall: every change of side (you moved over it, or it swept over you) is a heavy hit
        void Cross()
        {
            foreach (var p in Standing())
            {
                int now = SideOf(p.transform.position);
                if (now == 0 || !side.TryGetValue(p, out int was) || now == was) continue;
                side[p] = now; Crossings++;
                p.ReceiveHit(new Hazard { Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Damage = Damage, Knockback = 0, Stun = 15 });
            }
        }
    }
}
