// U3 0.1 REVIEW ONLY. No Unity, transport or combat implementation dependency.
// DTOs are mutable serialization candidates: adapters MUST detach/validate before publishing.
using System;
using System.Collections.Generic;

namespace LastBell.Foundation.Proposal
{
    public enum Life { Standing, Downed, Inactive }
    public enum CharacterClass { Vanguard, Chimesinger, Arcanist }
    public enum ActionKind { Basic, Offensive1, Offensive2, Offensive3, Defensive, Dash, Jump, Potion, Revive }
    public enum ActionStatus { Queued, Activated, Released, Rejected, Cancelled, Expired, Superseded, Unknown }
    public enum RejectReason { None, Ownership, StaleAttempt, Expired, Conflict, Inactive, Downed, Cooldown, Charges, Busy, Target, Unsupported }
    public enum ShapeKind { Circle, Annulus, Cone, Lane }
    public enum AnchorMode { Fixed, FollowUntilLock, SnapshotAtLock }
    public enum DamageKind { Strike, Ground }
    public enum Outcome { Avoided, Hit, Cancelled }
    public enum EventKind { Damage, Heal, ShieldGranted, ShieldExpired, LifecycleRefill, Action, Assignment, Phase, Recovery, ClassState }
    public enum RunPhase { Lobby, Preparation, Fight, Clear, Intermission, Results, Ended }
    public enum ResetKind { FreshRun, PracticeRestart, NextEncounter }
    [Flags] public enum Capability { None = 0, Solo = 1, FullCheckpoint = 2, AllClasses = 4, Run = 8, Coop = 16, Replay = 32, GroupMechanics = 64 }

    public struct Planar
    {
        public float X, Z;
        public Planar(float x, float z) { X = x; Z = z; }
        public bool Finite { get { return !float.IsNaN(X) && !float.IsNaN(Z) && !float.IsInfinity(X) && !float.IsInfinity(Z); } }
        public float LengthSquared { get { return X * X + Z * Z; } }
    }

    public sealed class MovementTuning
    {
        public int Hz = 60, DashTicks = 12, DashRecoveryTicks = 4, JumpTicks = 36, RegenDelayTicks = 45, StunTicks = 15, KnockbackTicks = 9;
        public float Walk = 6, Sprint = 9, DashDistance = 3, JumpPeak = .8f;
        public float StaminaMax = 100, SprintPerSecond = 25, JumpCost = 20, DashCost = 30, RegenPerSecond = 20;
        public float ActorRadius = .45f, ActorCentreBound = 11.5f, BossCentreBound = 9.5f, KnockbackDistance = 1.5f;
    }

    public struct Scope
    {
        public Guid SessionId;
        public ulong RunId, AttemptId, SegmentId;
    }
    public sealed class RosterEntry { public ulong PlayerId, ActorId; public CharacterClass Class; public bool Active, Ready; }
    public sealed class InputFrame
    {
        public Scope Scope;
        public ulong PlayerId, Sequence;
        public long TargetTick;
        public Planar Move, AimPoint;
        public bool BasicHeld, SprintHeld, BraceHeld;
        public ActionRequest[] Presses = new ActionRequest[0];
    }
    public sealed class ActionRequest
    {
        public ulong PlayerId, ActionId, TargetActorId;
        public Scope Scope;
        public ActionKind Kind;
        public Planar AimPoint;
        public long TargetTick, ExpiresTick;
    }
    public sealed class ActionResult
    {
        public ulong PlayerId, ActorId, ActionId;
        public Scope Scope;
        public long Tick;
        public ActionStatus Status;
        public RejectReason Reason;
    }
    public sealed class CooldownState { public string AbilityId = ""; public int RemainingTicks, Charges, RechargeTicks; }
    public sealed class CastState
    {
        public ulong ActionId, AttackInstanceId;
        public string AbilityId = "";
        public long BeginTick, ReleaseTick, RecoveryEndTick;
        public bool Released;
        public Planar Aim;
    }
    public sealed class MotionState
    {
        public Planar Position, Facing, Velocity, DashDirection, KnockbackDirection;
        public float Radius, Height, Stamina, KnockbackRemaining;
        public int JumpElapsed, DashRemaining, DashRecovery, StunRemaining, KnockbackRemainingTicks, SinceStaminaSpend;
    }
    public sealed class VanguardState
    {
        public int ComboStep;
        public long ComboPreservedUntil, OpeningExpires, BraceStarted, BraceEnds, BraceCooldownEnds;
        public bool Opening, Braced, OpeningGrantedThisBrace;
        public string OpeningSource = "";
        public Planar BraceFacing;
    }
    public sealed class EchoState { public ulong Id, SourceAttackId; public int StoredDamage; }
    public sealed class BellState
    {
        public ulong Id, OwnerPlayerId, TargetActorId;
        public long ExpiresTick;
        public int Strength;
        public EchoState[] Echoes = new EchoState[0];
    }
    public sealed class ChimesingerState { public int Harmony, HarmonyCap; public BellState[] Bells = new BellState[0]; }
    public sealed class SealState
    {
        public ulong Id, OwnerPlayerId, TargetActorId, InscriptionSourceAttackId;
        public long CreatedTick, RipeTick, ExpiresTick;
        public bool Inscribed;
    }
    public sealed class ArcanistState
    {
        public int Ticks, ShieldHp;
        public bool AnchorSet;
        public Planar AnchorPosition;
        public long AnchorReturnDeadline, AnchorCooldownEnds, ShieldExpiresTick;
        public SealState[] Seals = new SealState[0];
    }
    public sealed class ActorState
    {
        public ulong ActorId, PlayerId;
        public CharacterClass Class;
        public Life Life;
        public int Hp, MaxHp, PotionCharges, PotionCooldownTicks, RunDownCount;
        public MotionState Motion = new MotionState();
        public CastState Cast;
        public ActionRequest BufferedPress;
        public CooldownState[] Cooldowns = new CooldownState[0];
        // Exactly the selected class payload is non-null. Restore rejects mismatches.
        public VanguardState Vanguard;
        public ChimesingerState Chimesinger;
        public ArcanistState Arcanist;
    }
    public sealed class Shape
    {
        public ShapeKind Kind;
        public Planar Origin, Forward;
        public float Radius, InnerRadius, HalfAngleRadians, Length, HalfWidth;
    }
    public sealed class AssignmentChange
    {
        public ulong AttackInstanceId, OldPlayerId, NewPlayerId;
        public long Tick;
        public bool Cancelled;
        public string Reason = "";
    }
    public sealed class EncounterIdentity
    {
        public string EncounterId = "", AttackId = "", Block = "", Variation = "";
        public int Phase, Tier;
        public ulong Seed;
    }
    public sealed class HazardState
    {
        public ulong InstanceId, TargetPlayerId, SourceAttackId;
        public EncounterIdentity Context = new EncounterIdentity();
        public AnchorMode Anchor;
        public DamageKind DamageKind;
        public Shape LiveShape = new Shape(), LockedShape;
        public long WarningTick, LockTick, ResolveTick;
        public bool Locked, Cancelled, JumpClearable;
        public float ClearanceHeight, KnockbackDistance;
        public int Damage, StunTicks, KnockbackTicks;
        public Planar BossPositionAtWarning;
        public ulong[] ResolvedActors = new ulong[0];
        public AssignmentChange[] AssignmentHistory = new AssignmentChange[0];
    }
    public sealed class MarkerState { public ulong Id, TargetPlayerId; public Planar Position; public long StartTick, EndTick; public string Label = ""; }
    public sealed class RngState { public string Algorithm = ""; public int Version; public ulong Seed, State0, State1, DrawCount; }
    public sealed class BossState
    {
        public ulong ActorId;
        public string DefinitionId = "", ProviderBlock = "";
        public Planar Position, Facing, ChargeStart, ChargeEnd;
        public float BodyRadius;
        public int Hp, MaxHp, Phase, ProviderCursor;
        public bool Solid, Vulnerable;
        public long ChargeStartTick, ChargeEndTick, RecoveryStartTick, RecoveryEndTick;
        public int[] PendingPhases = new int[0];
        public float[] PhaseHpThresholds = new float[0];
        public HazardState RememberedEcho;
        public HazardState[] QueuedHazards = new HazardState[0];
    }
    public sealed class ProjectileState
    {
        public ulong Id, OwnerPlayerId, SourceActorId, AttackInstanceId;
        public string AbilityId = "";
        public Planar Position, Velocity;
        public float Height, Radius, Range, Travelled;
        public int Damage;
        public ulong[] ResolvedTargets = new ulong[0];
    }
    public sealed class ReviveState { public ulong ReviverPlayerId, TargetPlayerId; public long StartTick; public int ProgressTicks, RequiredTicks; }
    public sealed class HitResolution
    {
        public ulong AttackInstanceId, TargetActorId, EchoSourceAttackId;
        public EncounterIdentity Context = new EncounterIdentity();
        public Shape LockedGeometry;
        public Planar BossPositionAtWarning, PositionAtResolve, RequestedKnockback, ActualKnockback;
        public float Height;
        public bool Jumping, Dashing, Downed;
        public int StunTicks;
        public Outcome Outcome;
        public AssignmentChange[] AssignmentHistory = new AssignmentChange[0];
    }
    public sealed class CombatEvent
    {
        public int SchemaVersion = 1;
        public Scope Scope;
        public ulong EventId, AttackInstanceId, ActionId, SourceActorId, OwnerPlayerId, TargetActorId, TargetPlayerId, ParentEventId;
        public string AbilityId = "", Variant = "";
        public long Tick;
        public EventKind Kind;
        public int Requested, Mitigated, Absorbed, Effective, Excess, HpBefore, HpAfter;
        public bool Fatal, Downed;
        public HitResolution Resolution;
        public ActionResult ActionResult;
        public AssignmentChange Assignment;
        public PhaseChange PhaseChange;
        public RecoveryChange RecoveryChange;
        public ClassChange ClassChange;
    }
    public sealed class PhaseChange { public ulong BossActorId; public int Before, After; public long Tick; }
    public sealed class RecoveryChange { public ulong BossActorId; public bool Vulnerable; public long BeginTick, EndTick; }
    public sealed class ClassChange
    {
        public ulong ActorId, ObjectId, TargetActorId, SourceAttackId;
        public string StateId = "", Reason = "";
        public int Before, After;
        public long ExpiresTick;
    }
    public sealed class AbilityTotals
    {
        public ulong OwnerPlayerId;
        public string AbilityId = "";
        public long Damage, Healing, Absorbed, DamageTaken, Hits, Largest;
    }
    public sealed class SegmentState
    {
        public Scope Scope;
        public string EncounterId = "";
        public long StartTick, EndTick, EligibleCombatTicks;
        public bool Closed, DetailTruncated;
        public long DamageDone, DamageTaken, Healing, Absorbed;
        public AbilityTotals[] AbilityTotals = new AbilityTotals[0];
        public ulong AccountingThroughEventId;
        public CombatEvent[] RetainedDetail = new CombatEvent[0], FatalRecap = new CombatEvent[0];
    }
    public sealed class InputLedger
    {
        public ulong PlayerId, LastInputSequence, LastActionId;
        public InputFrame[] PendingInputs = new InputFrame[0];
        public ActionResult[] ActionResults = new ActionResult[0];
        public ActionRequest[] PendingActions = new ActionRequest[0];
        // Preserve accepted payloads across restore for same-ID/different-payload conflict detection.
        public ActionRequest[] AcceptedRequests = new ActionRequest[0];
        public ulong AcknowledgedEventId;
    }
    public sealed class Checkpoint
    {
        public int SchemaVersion = 1;
        public Capability Capabilities;
        public Scope Scope;
        public long Tick;
        public bool SessionActive, CombatActive, Practice;
        public RunPhase RunPhase;
        public int EncounterIndex, Tier;
        public MovementTuning Movement = new MovementTuning();
        public RosterEntry[] Roster = new RosterEntry[0];
        public ActorState[] Actors = new ActorState[0];
        public BossState Boss;
        public RngState EncounterRng, CombatRng;
        public HazardState[] Hazards = new HazardState[0];
        public ProjectileState[] Projectiles = new ProjectileState[0];
        public MarkerState[] Markers = new MarkerState[0];
        public ReviveState[] Revives = new ReviveState[0];
        public InputLedger[] InputLedgers = new InputLedger[0];
        public ulong NextAttackId, NextActorId, NextEventId, FirstRetainedEventId;
        public CombatEvent[] RetainedEvents = new CombatEvent[0];
        public SegmentState[] AccountingHistory = new SegmentState[0];
    }
    public sealed class ProviderContext { public Checkpoint DetachedState; public float BossHpFraction; }
    public enum EncounterCommandKind { Schedule, Retarget, Cancel, MoveBoss, Phase, Recovery, Marker }
    public sealed class EncounterCommand
    {
        public EncounterCommandKind Kind;
        public HazardState Hazard;
        public MarkerState Marker;
        public ulong AttackInstanceId, NewTargetPlayerId;
        public Planar Position;
        public int Phase;
        public long EndTick;
        public string Reason = "";
    }
    public interface IEncounterProvider { EncounterCommand[] Propose(ProviderContext context, RngState encounterRng); }
    public interface ISharedGeometry
    {
        bool Contains(Shape shape, Planar actorCentre, float actorRadius);
        Planar[] Outline(Shape shape, int arcSegments);
        Planar SweepAndConstrain(Planar from, Planar to, float actorRadius, BossState boss, MovementTuning tuning);
    }
    public interface ICombatAuthority
    {
        Capability Supported { get; }
        Checkpoint CaptureDetached();
        ActionResult[] Step(InputFrame[] ownedInputs); // exactly one 60 Hz tick, authority verifies ownership binding upstream
        bool TryRestoreAtomic(Checkpoint candidate, out string reason);
        void Reset(ResetKind kind, ulong seed);
        CombatEvent[] ReadEvents(ulong afterEventId, out bool checkpointRequired);
    }

    // Small executable contract helpers. They do not implement simulation/disk collision.
    public static class ContractRules
    {
        public static int DurationTicks(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > int.MaxValue / 60.0) throw new ArgumentOutOfRangeException("seconds");
            return (int)Math.Ceiling(seconds * 60);
        }
        public static Planar ClampMove(Planar move)
        {
            if (!move.Finite) throw new ArgumentException("Nonfinite move");
            // Double precision avoids float overflow from otherwise finite hostile input.
            double length = Math.Sqrt((double)move.X * move.X + (double)move.Z * move.Z);
            return length <= 1 ? move : new Planar((float)(move.X / length), (float)(move.Z / length));
        }
        public static bool ValidTiming(HazardState h) { return h.WarningTick >= 0 && h.WarningTick <= h.LockTick && h.LockTick < h.ResolveTick; }
        // Header guard only. Full TryRestoreAtomic additionally validates every DTO and cross-reference.
        public static bool SupportsCheckpointHeader(Checkpoint c, Capability supported)
        {
            return c != null && c.SchemaVersion == 1 && c.Scope.SessionId != Guid.Empty && c.Scope.AttemptId > 0 && c.Tick >= 0
                && (supported & Capability.FullCheckpoint) != 0 && (c.Capabilities & ~supported) == 0;
        }
        public static string EventKey(CombatEvent e) { return e.Scope.SessionId.ToString("N") + ":" + e.Scope.AttemptId + ":" + e.EventId; }
        public static string HitKey(ulong attempt, ulong attack, ulong target) { return attempt + ":" + attack + ":" + target; }
        public static double Dps(long effectiveDamage, long eligibleTicks) { return eligibleTicks <= 0 ? 0 : effectiveDamage * 60.0 / eligibleTicks; }
        public static void DamageAmounts(CombatEvent e, int hp, int requested, int mitigated, int absorbed)
        {
            if (hp < 0 || requested < 0 || mitigated < 0 || mitigated > requested || absorbed < 0 || absorbed > requested - mitigated) throw new ArgumentOutOfRangeException();
            e.Kind = EventKind.Damage; e.Requested = requested; e.Mitigated = mitigated; e.Absorbed = absorbed;
            int available = requested - mitigated - absorbed;
            e.HpBefore = hp; e.Effective = Math.Min(hp, available); e.Excess = available - e.Effective;
            e.HpAfter = hp - e.Effective; e.Fatal = hp > 0 && e.HpAfter == 0;
        }
        public static void HealAmounts(CombatEvent e, int hp, int maxHp, int requested)
        {
            if (hp < 0 || maxHp < hp || requested < 0) throw new ArgumentOutOfRangeException();
            e.Kind = EventKind.Heal; e.Requested = requested; e.HpBefore = hp;
            e.Mitigated = e.Absorbed = 0; e.Fatal = false;
            e.Effective = Math.Min(requested, maxHp - hp); e.Excess = requested - e.Effective; e.HpAfter = hp + e.Effective;
        }
    }
    public sealed class AccountingFixture
    {
        readonly Guid session;
        readonly ulong attempt;
        readonly HashSet<string> seen = new HashSet<string>();
        public long Damage { get; private set; }
        public long Healing { get; private set; }
        public long Absorbed { get; private set; }
        public AccountingFixture(Guid sessionId, ulong attemptId) { session = sessionId; attempt = attemptId; }
        public bool Apply(CombatEvent e)
        {
            if (e.Scope.SessionId != session || e.Scope.AttemptId != attempt || !seen.Add(ContractRules.EventKey(e))) return false;
            if (e.Kind == EventKind.Damage) { Damage += e.Effective; Absorbed += e.Absorbed; }
            if (e.Kind == EventKind.Heal) Healing += e.Effective;
            return true;
        }
    }
}
