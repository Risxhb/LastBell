using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Chime Debts (`bellkeeper.ult_chime_debts`), Bellkeeper Ultimate v2 — proposal, card 02 as changed for P1, built
    // 2026-10-24; P1 approved for now by Chzy18 2026-10-24 (ultimate/P1-v2-LOCKED.md). Each player's debt comes due on their next relay SOAK beat after the round opens (the
    // timer shows over the head): on that tick a porcelain debt ring (r 3.6 m) is left on the plate they soak. One beat
    // later, on the beat, it detonates: the ring and a 2 m chime line toward the boss, stopping 4 m short of him, 999 each
    // (a one-shot: the dead player's next duty goes empty, so one missed debt costs the run). Ring and line show as ivory
    // outlines for 1.5 s only; then they are a memory until they strike (no coral telegraph). On that beat the ring covers
    // the counterweight ✕ on its side of the dial all but its boss side: the weight holder there must stand on the inner
    // edge of the ✕ (r 2.0-3.1), so forgetting the debt kills them. Nothing is unavoidable: every other duty spot is clear.
    // (A relay REWIND that brought the dial back onto the beat a debt was paid on would erase it; at one beat it can't.)
    // (Card 02 paid on a numeral, dealt 60 and stayed visible; a debt paid on a ✕ beside the boss closed the crossing lane
    // round him, 60 did not cost the run, and a visible ring was dodged by bots that skipped the memory.)
    // Cue: At[0] = the round opens (s after launch). Without a running relay (practice): At = pay times of hours I..IV.
    // 5-8 players: both holders of an hour soak together, so they pay together.
    public sealed class ChimeDebts : UltimateMechanic
    {
        public override string Id => UltimateMechanics.ChimeDebts;
        public override string Name => "Chime Debts";

        public const string Source = "Chime Debt";
        public const float RingRadius = 3.6f,   // on its beat it covers the ✕ beside it (r 3.2) all but its boss side: forget it and that weight holder dies
            LineWidth = 2f, ShownSeconds = 1.5f, TimerShown = 6f,
            LineStop = 4f;   // the chime line stops 4 m from the boss: the ring round him (the relay's ✕ at 3.2 m) stays a lane for crossing
        public const int DetonateBeats = 1;
        public static float DetonateSeconds => DetonateBeats * EscapementRelay.BeatSeconds;
        public const int Damage = UltimateState.Lethal;   // card 02 had 60 (two hits kill); lethal since 2026-10-24: a 60 hit did not cost the run

        sealed class Debt
        {
            public PlayerController Owner; public int PayTick, DetTick; public bool Paid, Done, Erased;
            public Hazard Ring, Line; public BellkeeperFx.Telegraph RingOutline, LineOutline; public Transform Flash;
        }
        readonly List<Debt> debts = new List<Debt>();
        public int Detonated { get; private set; }
        public int Erased { get; private set; }
        public static bool LogDetonations;   // tests: who stood where at each detonation

        public override UltCue DefaultCue() => new UltCue { Variant = "practice", At = new[] { 6f, 9, 12, 15 } };

        public override IEnumerable<int> Cast()
        {
            Boss.ShowAttack(Name);
            Clear();
            var at = Cue.At ?? new float[0];
            var relay = Boss.UltRelay;
            foreach (var p in W.Players)
            {
                int pay = -1; bool erased = false;
                if (relay != null && relay.Running && relay.NextBeatTick >= 0)
                {
                    // the first relay beat at or after the round opens on which this player soaks; a rewind that brings the
                    // dial back onto that beat when the debt falls due erases it (its owner would be soaking on the ring)
                    int open = TickAt(Cue.At0(0)), beatTicks = Sim.Ticks(EscapementRelay.BeatSeconds);
                    for (int k = 0; k < 12 && pay < 0; k++)
                    {
                        int t = relay.NextBeatTick + k * beatTicks;
                        if (t >= open && relay.DutyOf(p, relay.BeatAhead(k)) == 0) { pay = t; erased = relay.BeatAhead(k + DetonateBeats) == relay.BeatAhead(k); }
                    }
                }
                else { int h = Hour(p); if (h >= 1 && h <= at.Length) pay = TickAt(at[h - 1]); }
                if (pay >= 0) debts.Add(new Debt { Owner = p, PayTick = pay, DetTick = pay + Sim.Ticks(DetonateSeconds), Erased = erased });
            }
            while (true)
            {
                bool open = false;
                foreach (var d in debts)
                {
                    if (d.Done) continue; open = true;
                    if (!d.Paid && Now >= d.PayTick) Pay(d);
                    if (d.Paid && d.RingOutline != null && Now >= d.PayTick + Sim.Ticks(ShownSeconds)) Fade(d);   // from here on it is a memory
                    if (d.Paid && Now >= d.DetTick)
                    {
                        d.Done = true; Fade(d);
                        if (d.Erased) { Erased++; W.BossFx.ChimeResolve(d.Ring.Origin, 0.8f); Boss.PlanLog.Add((Now, "debt erased by the rewind")); }
                        else
                        {
                            Detonated++;
                            if (LogDetonations)
                                Debug.Log($"[debt] {UltimateState.Roman[Hour(d.Owner)]} at ({d.Ring.Origin.x:0.0},{d.Ring.Origin.z:0.0}) detonates · " + string.Join(" · ", W.Players.ConvertAll(p =>
                                    $"{UltimateState.Roman[Hour(p)]} ({p.transform.position.x:0.0},{p.transform.position.z:0.0}){(d.Ring.Contains(p.transform.position) || d.Line.Contains(p.transform.position) ? " IN" : "")}")));
                        }
                    }
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
            // the ring is the plate's own: centred on the plate, wherever on it the owner stands (so the ✕ beside it always keeps its inner edge clear)
            for (int i = 0; i < 4; i++) { var plate = EscapementRelay.PlanDir(90 * i) * EscapementRelay.PlateRadius; if ((plate - at).magnitude <= EscapementRelay.OnPlate + 0.5f) at = plate; }
            int left = d.DetTick - Now;
            d.Ring = new Hazard { Source = Source, Shape = HazardShape.Circle, Kind = HazardKind.Ground, Origin = at, Radius = RingRadius, LockAt = 0, ResolveAt = UltShapes.ResolveIn(left), Damage = Damage, Hidden = true };
            var toBoss = Sim.Flat(Boss.transform.position) - at; float len = Mathf.Max(0.5f, toBoss.magnitude - LineStop);
            d.Line = new Hazard { Source = Source + " line", Shape = HazardShape.Lane, Kind = HazardKind.Ground, Origin = at, Yaw = Quaternion.LookRotation(toBoss.sqrMagnitude > 1e-4f ? toBoss : Vector3.forward).eulerAngles.y, Length = len, Width = LineWidth, LockAt = 0, ResolveAt = UltShapes.ResolveIn(left), Damage = Damage, Hidden = true };
            d.RingOutline = Ult.Outline(UltimateState.Footprint.Of(d.Ring, Now));
            d.LineOutline = Ult.Outline(UltimateState.Footprint.Of(d.Line, Now));
            d.Flash = FlashRing(d.Ring.Origin, d.Line);
            W.BossFx.ChimeResolve(at, 1.2f);
            Boss.PlanLog.Add((Now, $"debt {UltimateState.Roman[Hour(d.Owner)]} paid" + (d.Erased ? " (a rewind will erase it)" : "")));
            if (d.Erased) return;
            // no floor telegraph after the first 2 s: the ring and its line are memories until they strike
            var ring = Spawn(d.Ring); Spawn(d.Line);
            ring.OnResolve = hz => { W.BossFx.ChimeResolve(hz.Origin, hz.Radius); W.BossFx.TollResolve(hz.Origin, 0.4f, hz.Radius); };
        }
        void Fade(Debt d) { d.RingOutline?.Dismiss(); d.LineOutline?.Dismiss(); d.RingOutline = d.LineOutline = null; if (d.Flash) Object.Destroy(d.Flash.gameObject); d.Flash = null; }

        // the 1.5 s look of a paid debt: a bright porcelain ring of segments round the plate and a bar along its chime line
        static Material porcelain;
        Transform FlashRing(Vector3 at, Hazard line)
        {
            if (!porcelain) porcelain = Prim.Material(new Color(1f, 0.93f, 0.8f), 3f);
            var root = new GameObject("Chime Debt (stand-in)").transform; root.SetParent(W.Root, false); root.position = new Vector3(at.x, 0, at.z);
            for (int i = 0; i < 28; i++)
            {
                float deg = 360f * i / 28, a = deg * Mathf.Deg2Rad;
                Seg(Prim.Make(PrimitiveKind.Cube, root, new Vector3(Mathf.Sin(a) * RingRadius, 0.08f, Mathf.Cos(a) * RingRadius), new Vector3(0.6f, 0.05f, 0.22f), Color.white, 0, Quaternion.Euler(0, deg, 0)));
            }
            var bar = Prim.Make(PrimitiveKind.Cube, root, Vector3.zero, new Vector3(LineWidth, 0.04f, line.Length), Color.white);
            bar.rotation = Quaternion.Euler(0, line.Yaw, 0); bar.position = new Vector3(at.x, 0.07f, at.z) + Sim.Dir(line.Yaw) * line.Length * 0.5f; Seg(bar);
            return root;
        }
        static void Seg(Transform t) { var r = t.GetComponent<Renderer>(); r.sharedMaterial = porcelain; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // the guide: never be inside a paid debt's ring or line when it detonates (a memory: it shows for 2 s only)
        public override void Demands(PlayerController p, List<BotDemand> into)
        {
            if (!Running) return;
            foreach (var d in debts)
            {
                if (!d.Paid || d.Done || d.Ring == null || d.Erased) continue;
                var ring = d.Ring; var line = d.Line;
                into.Add(new BotDemand
                {
                    Tick = d.DetTick, Since = d.PayTick, Memory = true, From = this, Why = "debt " + UltimateState.Roman[Hour(d.Owner)],
                    Hint = Sim.Flat(ring.Origin).normalized * 2.6f,   // the safe inner edge of the ✕ the ring covers
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
            foreach (var d in debts) Fade(d);
            ClearNotes(); debts.Clear();
        }

        public override void Cancel() { base.Cancel(); if (Boss?.Ult != null) Clear(); else debts.Clear(); }
    }
}
