using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // v4 Severance: Day and Night (aurelion.severance), Phase 3, abilities-v4/ABILITIES-v4.md §3.5 (proposal). Source:
    // L'ura's Severance (two dimensions; crossing kills, an empty side wipes), adapted: no one-shots, the line moves.
    //   - A meridian line (coral, hostile) splits the dais through the centre; the far half is Night (violet), the near half Day.
    //     Each player's side is where they stand when it lands.
    //   - The line travels 24 degrees/s for 15 s, turning back at 5 s and again at 10 s (each turn warned 1 s ahead): keep moving
    //     ahead of it. (Chzy18 3 Nov: "the day night mechanic really doesn't do much at all. it is very slow"; was 13.5 degrees/s
    //     for 20 s with one turn, after a 3 s split.)
    //   - Each half has its own danger (3 Nov): every 3 s a 2.5 m circle lands at the feet of everyone in Day, and 1.5 s later at
    //     everyone in Night (Falling Meridians' size, warning and 30 damage), so both halves keep moving, in turn.
    //   - Crossing the line (by moving, or by letting it sweep over you) is a heavy hit (50) every time.
    //   - Co-op (N >= 2): if every standing player is on one side for 0.5 s, the other half falls: the party wipes.
    //   - Patterns during it: Starfall Mirror and Meridian Lances (4 beams) twice each, back to back. Solo: the empty-side rule
    //     never applies; the circles follow whichever half you stand in.
    //   - Ends with Reunion (co-op only): a tower at the centre that every player must stand in (an empty one wipes).
    public sealed class Severance : AurelionAbility
    {
        public override string Id => "aurelion.severance";
        public override string Name => "Severance";

        // Chzy18 2026-10-23 "Severance should move a lot faster", and again 3 Nov: 24 degrees/s (about 5.9 m/s at the rim, so near
        // the rim you sprint; nearer the centre you walk), turning back at 5 s and 10 s, each turn warned 1 s ahead
        public const float LengthS = 15f, DegPerS = 24f, LegS = 5f, EmptySideS = 0.5f, BackWarnS = 1.0f;
        public const float HalfEveryS = 3f, HalfRadius = 2.5f;
        public const int HalfDamage = 30;
        float yaw0; int lineTick;
        // the line's yaw `t` seconds into the sweep: forward, back, forward, a leg of LegS each
        public float YawAt(float t) { t = Mathf.Clamp(t, 0, LengthS); int leg = Mathf.Min(2, (int)(t / LegS)); float u = t - leg * LegS; return yaw0 + DegPerS * (leg == 1 ? LegS - u : u); }
        public float YawIn(float seconds) => Splitting ? LineYaw : YawAt(lineTick / (float)Sim.Hz + seconds);   // bots: where it will be
        public const float SplitS = 2f;   // 3 s until 3 Nov
        public bool Splitting { get; private set; }               // the 3 s before the sides lock
        readonly Dictionary<PlayerController, int> planned = new Dictionary<PlayerController, int>();
        public int Planned(PlayerController p) => planned.TryGetValue(p, out int s) ? s : 0;   // -1 Day, +1 Night, 0 free (solo)
        public const int Damage = 50;
        static readonly Color LineFill = new Color(1.35f, 0.42f, 0.32f), LineEdge = new Color(2.2f, 1.45f, 0.65f),
            // Night is a shadow, not a colour (Chzy18 accepted 2026-10-28; gpt/08 §4: no Arcanist violets in boss cues): a neutral
            // near-black that darkens the floor, a thin silver edge, and words for the sides (work/severance-night-20261028)
            NightFill = new Color(0.05f, 0.05f, 0.05f), NightRim = new Color(0.86f, 0.87f, 0.88f),
            NightText = new Color(0.92f, 0.93f, 0.94f), DayText = new Color(1f, 0.93f, 0.70f), CallText = new Color(0.92f, 0.93f, 0.94f);   // neutral: no hue near his StarSilver or comets
        const float NightShade = 0.7f;    // the Night half dims the bright floor by about half (a state region, not a warning; 0.55 measured only a 38 % drop)
        const float LineW = 0.36f;   // presentation: the hit is a change of side (0.05 m), the stroke just has to read
        static readonly float[] StartYaws = { 0f, 60f, 120f };

        public bool Active { get; private set; }
        public override void Abort() { Active = false; Splitting = false; }
        public float LineYaw { get; private set; }                 // the line runs along LineYaw through the centre
        public int Crossings { get; private set; }
        public int Casts { get; private set; }
        public int HalfCircles { get; private set; }   // test evidence: circles cast on the halves this cast
        readonly Dictionary<PlayerController, int> side = new Dictionary<PlayerController, int>();

        // +1 Night (left of the line's direction), -1 Day; 0 on the line itself
        public int SideOf(Vector3 p) => SideOfAt(p, LineYaw);
        public int SideOfAt(Vector3 p, float yaw) { var d = Sim.Dir(yaw); var v = Sim.Flat(p - Centre); float c = d.x * v.z - d.z * v.x; return Mathf.Abs(c) < 0.05f ? 0 : c > 0 ? 1 : -1; }
        public float DistToLine(Vector3 p) { var d = Sim.Dir(LineYaw); var v = Sim.Flat(p - Centre); return Mathf.Abs(d.x * v.z - d.z * v.x); }
        public int Assigned(PlayerController p) => side.TryGetValue(p, out int s) ? s : 0;

        public IEnumerable<int> Run()
        {
            Say(Name);
            Active = true; Crossings = 0; HalfCircles = 0; side.Clear();
            yaw0 = StartYaws[Casts++ % StartYaws.Length]; LineYaw = yaw0; lineTick = 0;
            // Telegraph style (audit F06): the line is the hostile part, two coral/amber half-strokes from the compass star whose
            // fill sweeps outward during the split (the countdown to the lock), then hold bright while it is live. Night is
            // lighting only: a shadow with no motif, drawn under the line, and the halves are named on the floor.
            var line = new[] { W.BossFx.Lane(Rim + 0.5f, LineW), W.BossFx.Lane(Rim + 0.5f, LineW) };
            foreach (var l in line) { l.Tint(LineFill, LineEdge); l.Motif(AurelionAbility.SkywardMotif); l.Progress(0); l.Order = 3; }
            var night = W.BossFx.Cone(Rim + 0.5f, 90f); night.Tint(NightFill, NightRim); night.Shade(NightShade); night.Motif(2); night.Progress(0); night.Order = -1;   // under every warning, like the Total Eclipse umbra
            PlaceLine(line); night.Place(Centre, LineYaw - 90f);
            var nightLbl = RunHud.Label(Vector3.zero, "NIGHT ☾", NightText, 30); var dayLbl = RunHud.Label(Vector3.zero, "DAY ☉", DayText, 30);
            PlaceNames(nightLbl, dayLbl);
            // 3 s to split before the sides lock: co-op, half the party is called Day and half Night (party order, alternating)
            Splitting = true; planned.Clear(); Clip("SeveranceSplit", Sim.Ticks(SplitS));   // glaive overhead along the line, brought down as the sides lock
            var party = Standing();
            for (int i = 0; i < party.Count; i++) planned[party[i]] = party.Count == 1 ? 0 : i % 2 == 0 ? -1 : 1;
            // co-op: each player's own call above them for the split (the banner alone names no one)
            var calls = new List<RunHud.WorldLabel>();
            if (party.Count > 1) foreach (var p in party) calls.Add(RunHud.Label(Vector3.up * 2.4f, planned[p] > 0 ? "NIGHT ☾" : "DAY ☉", planned[p] > 0 ? NightText : DayText, 26, p.transform));
            RunHud.Banner(party.Count > 1 ? "SEVERANCE — SPLIT: DAY AND NIGHT" : "SEVERANCE — DAY AND NIGHT", CallText, SplitS);
            for (int t = 0; t < Sim.Ticks(SplitS); t++) { foreach (var l in line) l.Progress(t / (float)Sim.Ticks(SplitS)); yield return 1; }
            Splitting = false; foreach (var l in line) { l.Progress(1); l.Lock(); }
            foreach (var c in calls) c.Hide();
            foreach (var p in Standing()) { int s = SideOf(p.transform.position); side[p] = s == 0 ? 1 : s; }
            Glow(1.6f); Flare(1.2f);

            var side2 = Patterns().GetEnumerator(); int wait = 0; bool on = true;
            int len = Sim.Ticks(LengthS), empty = 0;
            for (int t = 0; t < len; t++)
            {
                lineTick = t; LineYaw = YawAt(t / (float)Sim.Hz);
                if (t == Sim.Ticks(LegS - BackWarnS) || t == Sim.Ticks(2 * LegS - BackWarnS)) RunHud.Banner("THE LINE TURNS BACK", new Color(1f, 0.85f, 0.5f), BackWarnS);
                PlaceLine(line);
                night.Place(Centre, LineYaw - 90f);
                PlaceNames(nightLbl, dayLbl);
                Cross();
                int every = Sim.Ticks(HalfEveryS);
                if (t > 0 && t % every == 0) FallOnHalf(-1);                       // Day
                if (t > every / 2 && (t - every / 2) % every == 0) FallOnHalf(1);  // Night, half a beat later
                if (N >= 2)
                {
                    bool day = false, nightOn = false;
                    foreach (var p in Standing()) { if (Assigned(p) > 0) nightOn = true; else day = true; }
                    empty = day && nightOn ? 0 : empty + 1;
                    if (empty > Sim.Ticks(EmptySideS)) { Boss.Wipe("Severance — the " + (day ? "Night" : "Day") + " half fell", Id); break; }
                }
                if (on) { if (wait > 0) wait--; else if (side2.MoveNext()) wait = Mathf.Max(1, side2.Current) - 1; else on = false; }
                yield return 1;
            }
            foreach (var l in line) l.Expire(); night.Expire();
            nightLbl.Hide(); dayLbl.Hide();
            Active = false;
            if (N >= 2) foreach (var w in Boss.Towers.RunReunion()) yield return w;
        }

        IEnumerable<int> Patterns()
        {
            yield return Sim.Ticks(1f);
            foreach (var w in Boss.Starfall.Run(true)) yield return w;
            yield return Sim.Ticks(0.5f);
            foreach (var w in Boss.Lances.Run(4, 1, false)) yield return w;
            yield return Sim.Ticks(0.5f);
            foreach (var w in Boss.Starfall.Run(true)) yield return w;
            yield return Sim.Ticks(0.5f);
            foreach (var w in Boss.Lances.Run(4, 1, false)) yield return w;
        }

        // a circle at the feet of every standing player in that half (-1 Day, +1 Night; solo: the half you stand in)
        void FallOnHalf(int half)
        {
            var ps = W.Players;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (!p || p.Down) continue;
                int s = N >= 2 ? Assigned(p) : SideOf(p.transform.position);
                if (s != half) continue;
                var h = Spawn(new Hazard
                {
                    Source = Name, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = Sim.Flat(p.transform.position),
                    Radius = HalfRadius, LockAt = 0, ResolveAt = Boss.Meridians.Warn, Damage = HalfDamage, Knockback = 0, Stun = 0,
                });
                h.OnResolve = hz => W.BossFx.ChimeResolve(hz.Origin, hz.Radius);
                HalfCircles++;
            }
        }

        void PlaceLine(BellkeeperFx.Telegraph[] line) { line[0].Place(Centre, LineYaw); line[1].Place(Centre, LineYaw + 180f); }
        // the side names sit in the middle of each half, just above the floor, and turn with the line
        void PlaceNames(RunHud.WorldLabel night, RunHud.WorldLabel day)
        { night.Pos = Centre + Sim.Dir(LineYaw - 90f) * (Rim * 0.55f) + Vector3.up * 0.2f; day.Pos = Centre + Sim.Dir(LineYaw + 90f) * (Rim * 0.55f) + Vector3.up * 0.2f; }

        // the line is a wall: every change of side (you moved over it, or it swept over you) is a heavy hit
        void Cross()
        {
            foreach (var p in Standing())
            {
                int now = SideOf(p.transform.position);
                if (now == 0 || !side.TryGetValue(p, out int was) || now == was) continue;
                side[p] = now; Crossings++;
                p.ReceiveHit(new Hazard { Source = Name, AttackId = Id, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = p.transform.position, Damage = Damage, Knockback = 0, Stun = 15 });
            }
        }
    }
}
