using System;
using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Fracture of Time (`bellkeeper.ult_fracture`), Bellkeeper Ultimate — proposal. Design: ultimate/ULTIMATE.md §3.8, §5.7.
    // Cue (set by the plan, BellkeeperUltimate.cs): P5 start, no At. Runs until the boss dies (or the Thirteenth Hour wipes).
    //
    // The arena's gold fissure (UltimateState.FissureDir, NW <-> SE through the centre; ShatteredHourArenaBuilder.Fissure()
    // walks the same line from 3.2 m to 12.3 m each side) cracks open for all of P5:
    // - Burn: a 1.4 m lane across the whole floor through the centre burns 30/s (Ground, a BurnPerSecond hazard). It
    //   cracks open over 1.0 s (outline only), then burns. The P5 Leaning Bells use the fissure as their line, so reaching
    //   the safe half means crossing it: a dash spends ~0.13 s inside (4 HP), a 4.2 m/s walk ~0.48 s (14 HP).
    // - Flare (hardening, proposal): the fissure erupts on every toll of the great bell, the Thirteenth Hour's beat
    //   (6 s, 12 ... 72 s after P5 start; XIII at 78 s is the enrage): a 2.4 m lane along it, 60 (heavy), Ground, coral for
    //   1.2 s. Every P5 Leaning Bell has a toll inside its warning (loop 1: 2.0 s in, loop 2: 3.0 s in), so the crossing is
    //   "before the toll" (or, in loop 1, the 1.5 s right after it). Every Stopped Hour stop holds a toll too: a Stilled
    //   player whose station is NW or SE (the fissure's own ends) must hide beside the fissure, not on it (1.65-1.9 m off
    //   its axis at 6.6-7.5 m behind the boss, the Stopped Hour's strip, still puts his body in the chain's path;
    //   on the axis, burn + flare = 93 of 100 HP).
    // Melee and ranged: the lane runs from the boss's body to both walls; standing still on it anywhere burns, and the
    // flare hits the whole length. Solo and N = 2/4/8: identical (everyone crosses to the same side; no split).
    public sealed class FractureOfTime : UltimateMechanic
    {
        public override string Id => UltimateMechanics.Fracture;
        public override string Name => "Fracture of Time";

        public const float Width = 1.4f, BurnPerSecond = 30f;
        public const int OpenTicks = 60;                                 // cracks open (outline only) before it burns
        public const float FlareWidth = 2.4f, FlareBeat = 6f, FlareEnrage = 78f;
        public const int FlareDamage = UltShapes.HeavyDamage, FlareWarnTicks = 72;   // 60, coral for 1.2 s
        public const string FlareSource = "Fracture flare";
        // half-widths at which a player (body margin included) is inside: the burn 1.0 m, the flare 1.5 m
        public static float BurnHalf => Width / 2 + Hazard.Margin;
        public static float FlareHalf => FlareWidth / 2 + Hazard.Margin;
        // the lane covers every floor point along the fissure (square bounds have far corners)
        static float HalfLength => CombatWorld.PlayerBound * 1.42f + 0.5f;

        // signed distance from the fissure's axis, + on the FissureNormal (NE) side
        public static float Side(Vector3 p) => Vector3.Dot(Sim.Flat(p), UltimateState.FissureNormal);
        public static bool InFissure(Vector3 p) => Mathf.Abs(Side(p)) <= BurnHalf;
        public static bool InFlare(Vector3 p) => Mathf.Abs(Side(p)) <= FlareHalf;

        public Hazard Burn { get; private set; }                         // tests: the live burn lane
        public readonly List<int> FlareTicks = new List<int>();          // world ticks this cast's flares resolve
        BellkeeperFx.Telegraph glow;
        public BellkeeperFx.Telegraph Glow => glow;                      // tests: the fissure's glow (presentation)

        static readonly Color GlowFill = new Color(0.98f, 0.52f, 0.2f), GlowBorder = new Color(2.2f, 1.25f, 0.45f);   // amber: a live hazard

        // a lane along the whole fissure (a fresh Hazard; the caller sets timing and damage)
        public static Hazard Lane(string source, float width) => new Hazard
        {
            Source = source, Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = -UltimateState.FissureDir * HalfLength,
            Yaw = Quaternion.LookRotation(UltimateState.FissureDir).eulerAngles.y, Length = 2 * HalfLength, Width = width,
        };

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            FlareTicks.Clear();
            for (int k = 1; k * FlareBeat < FlareEnrage - 0.01f; k++) FlareTicks.Add(TickAt(k * FlareBeat));
            var burn = Lane(Name, Width);
            burn.LockAt = OpenTicks; burn.ResolveAt = int.MaxValue / 4; burn.BurnPerSecond = BurnPerSecond; burn.Damage = 0; burn.Knockback = 0; burn.Stun = 0;
            Burn = Spawn(burn);
            int next = 0;
            for (int i = 0; ; i++)
            {
                if (i == OpenTicks) ShowGlow();
                if (glow != null) glow.Progress(0.8f + 0.2f * Mathf.Sin(i * 0.11f));   // the crack breathes
                while (next < FlareTicks.Count && Now >= FlareTicks[next] - FlareWarnTicks)
                {
                    int due = FlareTicks[next++];
                    if (due < Now) continue;
                    var f = Lane(FlareSource, FlareWidth);
                    f.LockAt = 0; f.ResolveAt = UltShapes.ResolveIn(due - Now); f.Damage = FlareDamage;
                    f.OnResolve = hz => { for (int s = -4; s <= 4; s++) W.BossFx.ChimeResolve(UltimateState.FissureDir * (3f * s), FlareWidth / 2 + 0.3f); };
                    Spawn(f);
                }
                yield return 1;
            }
        }

        void ShowGlow()
        {
            DropGlow();
            glow = W.BossFx.Lane(2 * HalfLength, Width);
            glow.Tint(GlowFill, GlowBorder); glow.Lock();
            glow.Place(-UltimateState.FissureDir * HalfLength, Quaternion.LookRotation(UltimateState.FissureDir).eulerAngles.y);
        }
        void DropGlow() { if (glow != null) glow.Dismiss(); glow = null; }

        public override void Cancel() { DropGlow(); Burn = null; FlareTicks.Clear(); base.Cancel(); }

        // GUIDE.md "Fracture of Time": never stand in the fissure (the bot's burn cost); be off it on every toll (the great
        // bell's beat, known from P5's start); a Stilled player hides beside it, not on it; and on a Leaning Bell across the
        // fissure, cross to the safe half (dash across) before the toll.
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running || p == null || p.Down) return;
            var pos = Sim.Flat(p.transform.position);
            float side = Side(pos), sign = side >= 0 ? 1 : -1;
            var n = UltimateState.FissureNormal; var foot = pos - n * side;
            foreach (int t in FlareTicks)
            {
                if (t < Now || t > Now + 6 * Sim.Hz) continue;
                into.Add(new BotDemand { Tick = t, Since = LaunchTick, Ok = q => !InFlare(q), Hint = foot + n * sign * (FlareHalf + 0.7f), Why = "fissure flare (toll)", From = this });
            }
            // Stilled during P5: the spot must be off the fissure (and off the flare when a toll falls in the stop)
            UltimateState.Status s = null;
            foreach (var kv in Ult.All) if (kv.Key == p) s = kv.Value;   // read only: no status for players without one
            if (s != null && s.StillFrom > Now && s.StillUntil > s.StillFrom)
            {
                bool flare = false; foreach (int t in FlareTicks) if (t >= s.StillFrom && t < s.StillUntil) flare = true;
                var bp = Sim.Flat(Boss.transform.position);
                var hint = bp - (s.Station >= 0 ? UltimateState.StationDir(s.Station) : Sim.Flat(pos - bp).normalized * -1) * 7f;
                float hs = Side(hint), need = flare ? FlareHalf + 0.25f : BurnHalf + 0.7f;   // 1.75 m: the Stopped Hour's P5 strip (1.65-1.9 m at 6.6-7.5 m)
                if (Mathf.Abs(hs) < need) hint += n * ((hs >= 0 ? 1 : -1) * need - hs);
                Func<Vector3, bool> ok = flare ? (Func<Vector3, bool>)(q => !InFlare(q)) : q => !InFissure(q);
                into.Add(new BotDemand { Tick = s.StillFrom, Since = s.HourglassFrom >= 0 ? s.HourglassFrom : Now, Ok = ok, Hint = hint, Why = "Stilled beside the fissure, not on it", From = this });
            }
            // a Leaning Bell across the fissure: be on its safe half, clear of the burn (dash across)
            var lean = Boss.UltLean; var half = lean?.Half;
            if (half != null && lean.Running && !half.Cancelled && half.Age < half.ResolveAt && Mathf.Abs(Vector3.Dot(lean.LeanDir, n)) > 0.98f)
            {
                var hit = lean.LeanDir;
                into.Add(new BotDemand
                {
                    Tick = Now + (half.ResolveAt - half.Age), Since = Now - half.Age + 1, Ok = q => Vector3.Dot(Sim.Flat(q), hit) < -BurnHalf,
                    Hint = foot - hit * (FlareHalf + 0.9f), Why = "cross the fissure (dash)", From = this,
                });
            }
        }

        // -lastbellAbility ult_fracture: the fissure burns and flares on the 6 s beat, with a Leaning Bell across it every
        // 18 s timed like P5's first (the toll 2.0 s into its 3.5 s warning).
        public override IEnumerable<int> Practice()
        {
            Boss.Launch(this);
            int t0 = W.Tick;
            for (int k = 0; Running; k++)
            {
                int due = t0 + Sim.Ticks(10f + 18f * k);
                while (W.Tick < due && Running) yield return 1;
                if (!Running) yield break;
                Boss.UltMove(Boss.UltLean, BellkeeperEncounter.LeanTuning(MajorityNormal(W)));
            }
        }
        // the half most standing players are on (the plan's FissureLean rule): the bell falls there
        public static Vector3 MajorityNormal(CombatWorld w)
        {
            int plus = 0, minus = 0;
            foreach (var p in w.Players) { if (p.Down) continue; if (Side(p.transform.position) > 0) plus++; else minus++; }
            return minus > plus ? -UltimateState.FissureNormal : UltimateState.FissureNormal;
        }
    }
}
