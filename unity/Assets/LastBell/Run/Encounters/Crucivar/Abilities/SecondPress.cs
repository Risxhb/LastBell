using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // 05 Second Press (crucivar.second_press), Hard only, brief 05-crucivar.second_press.md (proposal).
    // Run(result) is called right after Mould Press resolves (on Hard, or from its practice hook). The telegraph appears at
    // once and the hammer re-stamps the fullest mould 2.0 s after the towers (6.2 s on the phase clock, while the phase-2
    // slag edge still burns): an r 2.2 circle on that tower's centre, Strike, telegraph 2.0 s locked at 0, 25 damage,
    // 1.5 m knockback toward the arena centre (never out into the phase-2 slag edge), 0.25 s stun. Hard's x1.3 damage
    // taken applies in PlayerController like every hit. Solo (Sequential): it presses the last tower (II). Otherwise the
    // most players at resolve; ties go to the lowest numeral, then NE > SE > SW > NW. Returns once the press has resolved.
    public sealed class SecondPress : CrucivarAbility
    {
        public override string Id => "crucivar.second_press";

        public const float Warn = 2.0f, Radius = 2.2f, Knock = 1.5f, StunSeconds = 0.25f;
        public const int Damage = 25;
        const float CrossWidth = 0.42f;
        static readonly Color CrossFill = new Color(0.95f, 0.40f, 0.28f), CrossBorder = new Color(2.3f, 1.25f, 0.45f);   // coral face, hot brass edge

        // the last press, for tests and the HUD
        public Hazard LastHazard { get; private set; }
        public int LastPick { get; private set; } = -1;
        public int StartTick { get; private set; }
        public int Runs { get; private set; }

        public IEnumerable<int> Run(MouldResult result)
        {
            Boss.SetCurrent("Second Press");
            StartTick = W.Tick; LastHazard = null; Runs++;
            LastPick = Pick(result);
            if (LastPick < 0) yield break;   // no tower resolved: nothing to press
            var centre = Flat(result.Centres[LastPick]);

            // the warning starts the moment the towers resolve; the hammer wrenches free and lifts during it
            int warn = T(Warn);
            var h = W.Hazards.Spawn(new Hazard
            {
                Source = "Second Press", Shape = HazardShape.Circle, Kind = HazardKind.Strike, Origin = centre, Radius = Radius,
                LockAt = 0, ResolveAt = warn, Damage = Damage, Knockback = Knock, Stun = Sim.Ticks(StunSeconds),
                Push = p => -Flat(p),   // toward the arena centre, away from the slag edge
            });
            LastHazard = h;

            // hammer-face cross: two crossed bars inside the circle, one along the bay diagonal (presentation only)
            float len = Radius * 1.6f, yaw = centre.sqrMagnitude > 0.01f ? Mathf.Atan2(centre.x, centre.z) * Mathf.Rad2Deg : 0;
            var cross = new List<LastBell.VFX.BellkeeperFx.Telegraph>();
            foreach (float y in new[] { yaw, yaw + 90 })
            {
                var bar = W.BossFx.Lane(len, CrossWidth);
                bar.Tint(CrossFill, CrossBorder); bar.Lock();
                bar.Place(centre - Sim.Dir(y) * (len / 2), y);
                cross.Add(bar);
            }
            h.OnResolve = hz =>
            {
                foreach (var bar in cross) bar.Resolve();
                W.BossFx.DiebreakerResolve(hz.Origin, hz.Radius);
                Boss.Flare(1.2f); Boss.Rig?.HammerLand();
            };
            Boss.Presenter?.PlayOnce("Diebreaker", warn / (float)Sim.Hz);

            // three ratchet notches across the warning (the hammer's sound; stand-in until the audio pass)
            for (int i = 0; i < warn && W.Hazards.Live.Contains(h); i++)
            {
                float k = i / (float)warn;
                foreach (var bar in cross) bar.Progress(k);
                Boss.Glow(0.3f + 0.7f * k);
                if (i == 0 || i == warn / 3 || i == 2 * warn / 3) Boss.Rig?.Ratchet((i * 3 / warn + 1) / 3f);
                yield return 1;
            }
            for (int guard = 0; guard < 10 && W.Hazards.Live.Contains(h); guard++) yield return 1;
        }

        // Which tower gets pressed: index into the result arrays, or -1 when nothing resolved.
        public static int Pick(MouldResult r)
        {
            int n = r?.Centres?.Length ?? 0;
            if (n == 0) return -1;
            if (r.Sequential) return n - 1;   // solo: the last tower soaked (II)
            int best = 0;
            for (int i = 1; i < n; i++)
            {
                int c = Of(r.Counts, i, 0), cb = Of(r.Counts, best, 0);
                if (c != cb) { if (c > cb) best = i; continue; }
                int nu = Of(r.Numerals, i, i + 1), nb = Of(r.Numerals, best, best + 1);
                if (nu != nb) { if (nu < nb) best = i; continue; }
                if (Of(r.Bays, i, 99) < Of(r.Bays, best, 99)) best = i;
            }
            return best;
        }
        static int Of(int[] a, int i, int fallback) => a != null && i < a.Length ? a[i] : fallback;
    }
}
