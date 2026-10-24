using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Chime Debts (`bellkeeper.ult_chime_debts`), Bellkeeper Ultimate v2 — proposal, card 02 as changed for P1 (the debt is
    // paid on your relay duty spot: "pay on your numeral" can't fit inside a 3 s beat), built 2026-10-24, not human-approved.
    // Each player's debt comes due on a relay beat (hour I first, one beat apart; the timer shows over the head). On that
    // tick a porcelain debt ring (r 2.5 m) is left where they stand. 8 s later it detonates: the ring and a 2 m chime line
    // from the ring to the boss, 60 each (two hits kill). Until 1.5 s before, ring and line are ivory memory outlines; then
    // they become the live coral telegraph. Nothing is unavoidable: the relay's routes run clear of every detonation for a
    // party that waits outside a detonating ring or line and steps onto its spot after.
    // Cue: At = the pay times (s after launch) of hours I, II, III, IV; players 5-8 pay with their hour's first holder.
    // Solo / duo / trio: one debt per player (hours I..n).
    public sealed class ChimeDebts : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ChimeDebts;
        public override string Name => "Chime Debts";

        public const string Source = "Chime Debt";
        public const float RingRadius = 2.5f, LineWidth = 2f, DetonateSeconds = 8f, LiveSeconds = 1.5f, TimerShown = 6f;
        public const int Damage = 60;

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
            foreach (var p in W.Players)
            {
                int h = Hour(p); if (h < 1 || h > at.Length) continue;
                debts.Add(new Debt { Owner = p, PayTick = TickAt(at[h - 1]), DetTick = TickAt(at[h - 1] + DetonateSeconds) });
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
            var toBoss = Sim.Flat(Boss.transform.position) - at; float len = Mathf.Max(0.5f, toBoss.magnitude - Boss.BodyRadius);
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
