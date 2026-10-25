using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Chime Debts (`bellkeeper.ult_chime_debts`), Bellkeeper Ultimate v2 — proposal, card 02 as changed for P1, built
    // 2026-10-24, not human-approved. Each player's debt comes due on their next relay SOAK beat after the round opens (the
    // timer shows over the head): on that tick a porcelain debt ring (r 2.5 m) is left on the plate they soak. 6 s later
    // (two beats, on the beat) it detonates: the ring and a 2 m chime line toward the boss, stopping 4 m short of him, 999
    // each (a one-shot: the dead player's next duty goes empty, so one missed debt costs the run). Until 1.5 s before, ring
    // and line are ivory memory outlines; then they become the live coral telegraph. Nothing is unavoidable: every route
    // holds a clear spot on the beat (the debt ring sits where that beat's chain holder would stand, so they step round it). (Card 02 paid on a numeral and dealt 60; a debt paid on a ✕ beside the boss closed
    // the crossing lane round him, so P1 pays on the plate, and 60 did not cost the run.)
    // Cue: At[0] = the round opens (s after launch). Without a running relay (practice): At = pay times of hours I..IV.
    // 5-8 players: both holders of an hour soak together, so they pay together.
    public sealed class ChimeDebts : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ChimeDebts;
        public override string Name => "Chime Debts";

        public const string Source = "Chime Debt";
        public const float RingRadius = 2.5f, LineWidth = 2f, DetonateSeconds = 6f, LiveSeconds = 1.5f, TimerShown = 6f,
            LineStop = 4f;   // the chime line stops 4 m from the boss: the ring round him (the relay's ✕ at 3.2 m) stays a lane for crossing
        public const int Damage = UltimateState.Lethal;   // card 02 had 60 (two hits kill); lethal since 2026-10-24: a 60 hit did not cost the run

        sealed class Debt
        {
            public PlayerController Owner; public int PayTick, DetTick; public bool Paid, Live, Done;
            public Hazard Ring, Line; public BellkeeperFx.Telegraph RingOutline, LineOutline;
        }
        readonly List<Debt> debts = new List<Debt>();
        public int Detonated { get; private set; }

        public override UltCue DefaultCue() => new UltCue { Variant = "practice", At = new[] { 6f, 9, 12, 15 } };

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            Clear();
            var at = Cue.At ?? new float[0];
            var relay = Boss.UltRelay;
            foreach (var p in W.Players)
            {
                int pay = -1;
                if (relay != null && relay.Running && relay.NextBeatTick >= 0)
                {
                    // the first relay beat at or after the round opens on which this player soaks
                    int open = TickAt(Cue.At0(0)), beatTicks = Sim.Ticks(EscapementRelay.BeatSeconds);
                    for (int k = 0; k < 8 && pay < 0; k++)
                    {
                        int t = relay.NextBeatTick + k * beatTicks;
                        if (t >= open && relay.DutyOf(p, relay.Beat + k) == 0) pay = t;
                    }
                }
                else { int h = Hour(p); if (h >= 1 && h <= at.Length) pay = TickAt(at[h - 1]); }
                if (pay >= 0) debts.Add(new Debt { Owner = p, PayTick = pay, DetTick = pay + Sim.Ticks(DetonateSeconds) });
            }
            while (true)
            {
                bool open = false;
                foreach (var d in debts)
                {
                    if (d.Done) continue; open = true;
                    if (!d.Paid && Now >= d.PayTick) Pay(d);
                    if (d.Paid && !d.Live && Now >= d.DetTick - Sim.Ticks(LiveSeconds)) GoLive(d);
                    if (d.Live && Now >= d.DetTick) { d.Done = true; Detonated++; }
                }
                Notes();
                if (!open) break;
                yield return 1;
            }
            ClearNotes();
        }

        int Hour(PlayerController p) { int i = W.Players.IndexOf(p); return i < 0 ? 0 : i % 4 + 1; }

        void Pay(Debt d)
        {
            d.Paid = true;
            if (d.Owner.Down) { d.Done = true; return; }
            var at = Sim.Flat(d.Owner.transform.position);
            d.Ring = new Hazard { Source = Source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = RingRadius, LockAt = 0, Damage = Damage };
            var toBoss = Sim.Flat(Boss.transform.position) - at; float len = Mathf.Max(0.5f, toBoss.magnitude - LineStop);
            d.Line = new Hazard { Source = Source + " line", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = at, Yaw = Quaternion.LookRotation(toBoss.sqrMagnitude > 1e-4f ? toBoss : Vector3.forward).eulerAngles.y, Length = len, Width = LineWidth, LockAt = 0, Damage = Damage };
            d.RingOutline = Ult.Outline(UltimateState.Footprint.Of(d.Ring, Now));
            d.LineOutline = Ult.Outline(UltimateState.Footprint.Of(d.Line, Now));
            W.BossFx.ChimeResolve(at, 1.2f);
            Boss.PlanLog.Add((Now, $"debt {UltimateState.Roman[Hour(d.Owner)]} paid"));
        }
        void GoLive(Debt d)
        {
            d.Live = true;
            d.RingOutline?.Dismiss(); d.LineOutline?.Dismiss(); d.RingOutline = d.LineOutline = null;
            int left = d.DetTick - Now;
            d.Ring.ResolveAt = UltShapes.ResolveIn(left); d.Line.ResolveAt = UltShapes.ResolveIn(left);
            var ring = Spawn(d.Ring); Spawn(d.Line);
            ring.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); W.BossFx.TollResolve(hz.Origin, 0.4f, hz.Radius); };
        }

        // the guide: never be inside a paid debt's ring or line when it detonates (a memory until it turns coral)
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running) return;
            foreach (var d in debts)
            {
                if (!d.Paid || d.Done || d.Ring == null || d.Live) continue;   // live: the hazard itself is the rule
                var ring = d.Ring; var line = d.Line;
                into.Add(new BotDemand
                {
                    Tick = d.DetTick, Since = d.PayTick, Memory = true, From = this, Why = "debt " + UltimateState.Roman[Hour(d.Owner)],
                    Hint = ring.Origin + Sim.Flat(ring.Origin).normalized * (RingRadius + 1f),
                    Ok = q => !ring.Contains(q) && !line.Contains(q),
                });
            }
        }

        void Notes()
        {
            foreach (var d in debts)
            {
                var s = Ult.Of(d.Owner);
                float left = (d.PayTick - Now) / (float)Sim.Hz;
                s.DebtNote = !d.Paid && left <= TimerShown && !d.Owner.Down ? $"DEBT {left:0.0}s" : null;
            }
        }
        void ClearNotes() { foreach (var d in debts) if (d.Owner) Ult.Of(d.Owner).DebtNote = null; }
        void Clear()
        {
            foreach (var d in debts) { d.RingOutline?.Dismiss(); d.LineOutline?.Dismiss(); }
            ClearNotes(); debts.Clear();
        }

        public override void Cancel() { base.Cancel(); if (Boss?.Ult != null) Clear(); else debts.Clear(); }
    }
}
