using System;
using System.Collections.Generic;
using LastBell.Foundation.Proposal;

static class Fixtures
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; }
    static void Throws(Action action, string name) { try { action(); } catch (ArgumentException) { passed++; return; } throw new Exception(name); }
    static CombatEvent Event(Scope scope, ulong id) { return new CombatEvent { Scope = scope, EventId = id }; }
    public static int Main()
    {
        var scope = new Scope { SessionId = Guid.Parse("72d6c6cb-1304-419f-933b-40fa03d0d801"), RunId = 1, AttemptId = 7, SegmentId = 3 };
        Check(ContractRules.DurationTicks(.2) == 12 && ContractRules.DurationTicks(.75) == 45, "published integer durations");
        Check(ContractRules.DurationTicks(.001) == 1, "positive sub-tick warning rounds up");
        Throws(() => ContractRules.DurationTicks(double.NaN), "NaN duration rejected");
        Throws(() => ContractRules.ClampMove(new Planar(float.PositiveInfinity, 0)), "infinite input rejected");
        Throws(() => ContractRules.ClampMove(new Planar(0, float.NaN)), "NaN input rejected");
        var clamped = ContractRules.ClampMove(new Planar(float.MaxValue, float.MaxValue));
        Check(clamped.Finite && Math.Abs(clamped.LengthSquared - 1) < .00001, "finite huge input safely normalized");
        Check(ContractRules.ClampMove(new Planar(0, 0)).LengthSquared == 0, "zero movement remains neutral");
        Check(!ContractRules.ValidTiming(new HazardState { WarningTick = 1, LockTick = 2, ResolveTick = 2 }), "withdrawn same-tick lock rejected");
        Check(ContractRules.ValidTiming(new HazardState { WarningTick = 1, LockTick = 1, ResolveTick = 2 }), "fixed warning lock accepted");
        var tuning = new MovementTuning(); double travelled = 0;
        for (int i = 0; i < tuning.DashTicks; i++) travelled += (double)tuning.DashDistance / tuning.DashTicks;
        Check(Math.Abs(travelled - 3) < .00001, "12-step dash totals 3 m");
        Check(tuning.ActorCentreBound == 11.5f && tuning.ActorRadius == .45f, "centre bound is explicit");
        var hits = new HashSet<string>();
        Check(hits.Add(ContractRules.HitKey(7, 50, 2)), "first attack hit");
        Check(!hits.Add(ContractRules.HitKey(7, 50, 2)), "same attack target dedup");
        Check(hits.Add(ContractRules.HitKey(7, 51, 2)), "distinct attack can hit same tick: no iframe");
        Check(hits.Add(ContractRules.HitKey(7, 50, 3)), "same attack different player");
        Check(hits.Add(ContractRules.HitKey(8, 50, 2)), "attempt identity prevents key collision");
        var lethal = Event(scope, 1); ContractRules.DamageAmounts(lethal, 10, 30, 0, 0);
        Check(lethal.Effective == 10 && lethal.Excess == 20 && lethal.Fatal && lethal.HpAfter == 0, "overkill capped without losing requested amount");
        var brace = Event(scope, 2); ContractRules.DamageAmounts(brace, 100, 30, 18, 0);
        Check(brace.Effective == 12 && !brace.Fatal, "60 percent Brace mitigation");
        var shield = Event(scope, 3); ContractRules.DamageAmounts(shield, 10, 25, 0, 25);
        Check(shield.Effective == 0 && shield.Absorbed == 25 && !shield.Fatal, "fully absorbed is zero HP loss");
        var heal = Event(scope, 4); ContractRules.HealAmounts(heal, 90, 100, 30);
        Check(heal.Effective == 10 && heal.Excess == 20 && heal.HpAfter == 100, "heal capped and overheal tracked");
        Throws(() => ContractRules.DamageAmounts(Event(scope, 9), 10, 10, 0, 11), "invalid absorption rejected");
        var ledger = new AccountingFixture(scope.SessionId, scope.AttemptId);
        Check(ledger.Apply(lethal) && !ledger.Apply(lethal), "repeat event replay is idempotent");
        Check(ledger.Apply(brace) && ledger.Apply(shield) && ledger.Apply(heal), "distinct same scope events accepted");
        Check(ledger.Damage == 22 && ledger.Healing == 10 && ledger.Absorbed == 25, "effective totals separated");
        var oldScope = scope; oldScope.AttemptId--;
        Check(!ledger.Apply(Event(oldScope, 5)), "stale attempt rejected");
        var foreignScope = scope; foreignScope.SessionId = Guid.NewGuid();
        Check(!ledger.Apply(Event(foreignScope, 6)), "foreign session rejected");
        Check(ContractRules.Dps(100, 0) == 0 && ContractRules.Dps(100, 60) == 100, "zero-time and normal DPS");
        var segment = new SegmentState { Closed = true, EndTick = 600, EligibleCombatTicks = 300, DamageDone = 500 };
        Check(ContractRules.Dps(segment.DamageDone, segment.EligibleCombatTicks) == 100, "frozen duration independent of later world ticks");
        var checkpoint = new Checkpoint { Scope = scope, Capabilities = Capability.Solo | Capability.FullCheckpoint, Tick = 30 };
        Check(!ContractRules.SupportsCheckpointHeader(checkpoint, Capability.Solo), "consumer without restoration rejects checkpoint");
        Check(ContractRules.SupportsCheckpointHeader(checkpoint, Capability.Solo | Capability.FullCheckpoint), "supported checkpoint header accepted");
        checkpoint.Capabilities |= Capability.Coop;
        Check(!ContractRules.SupportsCheckpointHeader(checkpoint, Capability.Solo | Capability.FullCheckpoint), "unsupported co-op state rejected");
        checkpoint.Capabilities = Capability.Solo;
        checkpoint.SchemaVersion++;
        Check(!ContractRules.SupportsCheckpointHeader(checkpoint, Capability.Solo | Capability.FullCheckpoint), "unknown checkpoint schema rejected");
        Console.WriteLine("U3 proposal fixtures: " + passed + " passed. Contract helpers only; no combat/Unity/network acceptance.");
        return 0;
    }
}
