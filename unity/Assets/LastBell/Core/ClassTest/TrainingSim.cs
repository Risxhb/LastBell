using System;
using System.Collections.Generic;

namespace LastBell.ClassTest
{
    // Scene-independent class-test arena: one player, one training dummy, optional demo hazards.
    // One Step() is one 60 Hz tick. Units and arena match the legacy Godot reference.
    // This is a test harness, not the production combat core (U1 owns that).
    public struct Vec2
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Y * s); }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public float Angle { get { return (float)Math.Atan2(Y, X); } }
        public bool IsFinite { get { return !(float.IsNaN(X) || float.IsNaN(Y) || float.IsInfinity(X) || float.IsInfinity(Y)); } }
        public Vec2 LimitLength(float max)
        {
            float len = Length;
            return len > max && len > 0f ? this * (max / len) : this;
        }
        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }
    }

    public struct TrainingCommand
    {
        public Vec2 Move;
        public int SupportTarget; // 0 = automatic, 1 = self, 2 = training ally
        public Vec2 Aim;          // aim direction (right stick / mouse); zero = keep facing (soft assist toward the dummy)
        public bool Dash, DashPressed; // universal Clockstep Dash (held / new press)
        public bool Basic, Technique, Signature, Guard;            // held this tick
        public bool BasicPressed, TechniquePressed, SignaturePressed, GuardPressed; // new press, for feedback only

        public bool Held(ActionSlot s)
        {
            switch (s)
            {
                case ActionSlot.Basic: return Basic;
                case ActionSlot.Technique: return Technique;
                case ActionSlot.Signature: return Signature;
                default: return Guard;
            }
        }
        public bool Pressed(ActionSlot s)
        {
            switch (s)
            {
                case ActionSlot.Basic: return BasicPressed;
                case ActionSlot.Technique: return TechniquePressed;
                case ActionSlot.Signature: return SignaturePressed;
                default: return GuardPressed;
            }
        }
    }

    public enum HazardKind { Cone, Donut, Circle }

    public sealed class Hazard
    {
        public int Id;
        public string Name;
        public HazardKind Kind;
        public Vec2 Origin;
        public float Radius, InnerRadius, Direction, HalfAngle;
        public int WarningTick, ResolveTick;
        public bool Counterable; // a designated physical strike that Resonant Guard can precision-counter

        // Boundaries are dangerous: points exactly on an edge are inside.
        public bool Contains(Vec2 p)
        {
            Vec2 d = p - Origin;
            float dist = d.Length;
            if (dist > Radius) return false;
            if (Kind == HazardKind.Donut) return dist >= InnerRadius;
            if (Kind == HazardKind.Circle) return true;
            if (dist == 0f) return true;
            double diff = Math.Abs(Math.IEEERemainder(d.Angle - Direction, 2 * Math.PI));
            return diff <= HalfAngle;
        }
    }

    public enum TrainingEventKind
    {
        ActionUsed, ActionBlocked, CastStarted, CastCancelled,
        GuardUsed, HazardResolved, KnockedOut, Respawned, DummyDefeated, Reset,
        Healed, ShieldApplied, ShieldAbsorbed, EffectExpired, Cleansed, CastCompleted, ResourceChanged,
        DashUsed, CounterSuccess, ReversalReady, Marked
    }

    public sealed class TrainingEvent
    {
        public int Tick;
        public TrainingEventKind Kind;
        public ActionSlot Slot;
        public int Damage;
        public int SourceId, TargetId, Amount, Overheal, Until;
        public string Effect;
        public string Text;
    }

    public sealed class TrainingPlayer
    {
        public int Id = 1;
        public bool Present = true, Friendly = true;
        public int Shield, ShieldUntil, HotNext, HotUntil, ChillUntil;
        public Vec2 Position;
        public int Hp;
        public int Resource;
        public readonly int[] ReadyAt = new int[4];
        public int GuardUntil, ImmuneUntil, AttackUntil;
        public int CastUntil;          // 0 = not casting
        public ActionSlot CastSlot;
        public int KnockedOutAt;       // 0 = alive
        public float Facing = (float)(-Math.PI / 2); // plane angle; spawn faces the dummy
        public int DashCharges = 2, DashRechargeAt, DashUntil, DashIframeUntil;
        public Vec2 DashDir;
        public bool Alive { get { return Hp > 0; } }
        public bool Casting { get { return CastUntil > 0; } }
    }

    public sealed partial class TrainingSim
    {
        public const int TickRate = 60;
        public const float Speed = 250f;
        public const int MaxHp = 5;
        public const int ImmunityTicks = 45;
        public const int RespawnTicks = 120;
        public const int DummyRespawnTicks = 120;
        public const int DpsWindowTicks = 600;
        public const int HazardInterval = 210;
        public const int HazardWarning = 90;
        public const float CastCancelThreshold = 0.2f;
        public static readonly Vec2 ArenaMin = new Vec2(60, 140), ArenaMax = new Vec2(880, 740);
        public const float ActorInset = 14f;
        public static readonly Vec2 BossPosition = new Vec2(470, 360);
        public static readonly Vec2 Spawn = new Vec2(470, 590);

        public ClassKit Kit { get; private set; }
        public int Seed { get; private set; }
        public int Tick { get; private set; }
        public readonly TrainingPlayer Player = new TrainingPlayer();
        public readonly List<Hazard> Hazards = new List<Hazard>();
        public readonly List<TrainingEvent> Events = new List<TrainingEvent>(); // recent, capped
        public int EventSerial { get; private set; } // total events ever logged; never resets

        // Options (toggled live in the arena).
        public bool HazardsEnabled;
        public bool CounterableStrikes; // adds the counterable Pendulum Strike to the demo hazard cycle
        public bool Invulnerable;
        public bool DummyInfinite = true;
        public int DummyMaxHp = 180;

        // Dummy and damage meter.
        public int DummyHp { get; private set; }
        public int DummyDefeatedAt { get; private set; }
        public int TotalDamage { get; private set; }
        public int FirstHitTick { get; private set; }
        public readonly int[] DamageBySlot = new int[3];
        public readonly int[] UsesBySlot = new int[3];
        public int LastKillTicks { get; private set; }
        readonly int[] damageRing = new int[DpsWindowTicks];
        int windowDamage;

        Random rng;
        int nextHazardId;
        int hazardCycle;

        public TrainingSim(ClassKit kit, int seed = 1)
        {
            Kit = kit;
            Reset(seed);
        }

        public void SetClass(ClassKit kit)
        {
            Kit = kit;
            Reset(Seed);
        }

        public void Reset(int seed)
        {
            Seed = seed;
            rng = new Random(seed);
            Tick = 0;
            nextHazardId = 0;
            hazardCycle = 0;
            Hazards.Clear();
            Events.Clear();
            ResetPlayer();
            ResetSupport();
            DummyHp = DummyMaxHp;
            DummyDefeatedAt = 0;
            ResetMeter();
            Log(TrainingEventKind.Reset, ActionSlot.Basic, 0, Kit.Name + " ready");
        }

        public void ResetMeter()
        {
            TotalDamage = 0;
            FirstHitTick = 0;
            Array.Clear(DamageBySlot, 0, 3);
            Array.Clear(UsesBySlot, 0, 3);
            Array.Clear(damageRing, 0, damageRing.Length);
            windowDamage = 0;
        }

        void ResetPlayer()
        {
            Player.Position = Spawn;
            Player.Hp = MaxHp;
            Player.Resource = IsHealer ? Kit.ResourceCap : 0;
            ClearSupport(Player);
            for (int i = 0; i < 4; i++) Player.ReadyAt[i] = 0;
            Player.GuardUntil = Player.ImmuneUntil = Player.AttackUntil = 0;
            Player.CastUntil = 0;
            Player.KnockedOutAt = 0;
            Player.Facing = (float)(-Math.PI / 2);
            Player.DashCharges = DashRules.Charges; Player.DashRechargeAt = Player.DashUntil = Player.DashIframeUntil = 0;
            ResetVanguard();
        }

        // Damage per second over the last DpsWindowTicks (or since the first hit, if shorter).
        public float Dps
        {
            get
            {
                if (FirstHitTick == 0) return 0f;
                int span = Math.Min(DpsWindowTicks, Tick - FirstHitTick + 1);
                return windowDamage * (float)TickRate / Math.Max(1, span);
            }
        }

        public float DistanceToDummy { get { return Vec2.Distance(Player.Position, BossPosition); } }
        public bool DummyAlive { get { return DummyInfinite || DummyHp > 0; } }

        public int CooldownRemaining(ActionSlot s) { return Math.Max(0, Player.ReadyAt[(int)s] - Tick); }
        public bool GuardActive { get { return Tick < Player.GuardUntil; } }

        public void Step(TrainingCommand cmd)
        {
            Tick++;
            int slotIndex = Tick % DpsWindowTicks;
            windowDamage -= damageRing[slotIndex];
            damageRing[slotIndex] = 0;

            if (!DummyInfinite && DummyHp <= 0 && Tick - DummyDefeatedAt >= DummyRespawnTicks)
            {
                DummyHp = DummyMaxHp;
                ResetMeter();
            }

            StepSupport();
            if (Player.Alive) StepPlayer(cmd);
            else if (Tick - Player.KnockedOutAt >= RespawnTicks)
            {
                ResetPlayer();
                Hazards.Clear();
                Log(TrainingEventKind.Respawned, ActionSlot.Basic, 0, "Respawned");
            }

            if (HazardsEnabled && Tick % HazardInterval == 1) SpawnDemoHazard();
            for (int i = Hazards.Count - 1; i >= 0; i--)
            {
                if (Tick >= Hazards[i].ResolveTick)
                {
                    ResolveHazard(Hazards[i]);
                    Hazards.RemoveAt(i);
                }
            }
            if (!HazardsEnabled && Hazards.Count > 0) Hazards.Clear();
        }

        void StepPlayer(TrainingCommand cmd)
        {
            if (IsVanguard) { StepVanguard(cmd); return; }
            if (IsHealer) { StepHealer(cmd); return; }
            var p = Player;
            Vec2 move = cmd.Move.IsFinite ? cmd.Move.LimitLength(1f) : new Vec2(0, 0);
            bool wantsMove = move.Length > CastCancelThreshold;
            UpdateFacing(cmd, move);

            bool guardReady = Tick >= p.ReadyAt[(int)ActionSlot.Guard];
            bool dashStart = WantsDash(cmd);
            if (p.Casting && (wantsMove || dashStart || (cmd.Guard && guardReady)))
            {
                p.ReadyAt[(int)p.CastSlot] = Tick + Kit.Offensive(p.CastSlot).CancelCooldown;
                p.CastUntil = 0;
                p.AttackUntil = Tick;
                Log(TrainingEventKind.CastCancelled, p.CastSlot, 0, Kit.Offensive(p.CastSlot).Name + " cancelled");
            }
            if (dashStart) StartDash(move);
            if (StepDashMotion()) { }
            else if (!p.Casting) p.Position = ClampToArena(p.Position + move * (Speed / TickRate));

            if (cmd.Guard && guardReady)
            {
                p.GuardUntil = Tick + Kit.Guard.Window;
                p.ReadyAt[(int)ActionSlot.Guard] = Tick + Kit.Guard.Cooldown;
                if (Kit.Guard.BlinkDistance > 0 && move.Length > 0.01f)
                    p.Position = ClampToArena(p.Position + move * (Kit.Guard.BlinkDistance / move.Length));
                Log(TrainingEventKind.GuardUsed, ActionSlot.Guard, 0, Kit.Guard.Name);
            }
            else if (cmd.GuardPressed && Tick < p.ReadyAt[(int)ActionSlot.Guard])
            {
                Log(TrainingEventKind.ActionBlocked, ActionSlot.Guard, 0, Kit.Guard.Name + ": cooling down");
            }

            if (p.Casting && Tick >= p.CastUntil)
            {
                p.CastUntil = 0;
                var def = Kit.Offensive(p.CastSlot);
                if (DummyAlive && DistanceToDummy <= def.Range) DealDamage(p.CastSlot, def);
                else Log(TrainingEventKind.ActionBlocked, p.CastSlot, 0, def.Name + ": fizzled");
            }

            // Shared offensive cadence: first eligible action in priority order fires.
            ActionSlot[] order = { ActionSlot.Signature, ActionSlot.Technique, ActionSlot.Basic };
            foreach (var slot in order)
            {
                if (!cmd.Held(slot)) continue;
                string reason = TryAttack(slot);
                if (reason == null) break;
                if (cmd.Pressed(slot)) Log(TrainingEventKind.ActionBlocked, slot, 0, Kit.Offensive(slot).Name + ": " + reason);
            }
        }

        // Returns null on success, otherwise the reason it was blocked.
        string TryAttack(ActionSlot slot)
        {
            var p = Player;
            var def = Kit.Offensive(slot);
            if (p.Casting) return "casting";
            if (Tick < p.ReadyAt[(int)slot]) return "cooling down";
            if (Tick < p.AttackUntil) return "attack cadence";
            if (def.ResourceDelta < 0 && p.Resource < -def.ResourceDelta) return "need " + (-def.ResourceDelta) + " " + Kit.ResourceName;
            if (!DummyAlive) return "no target";
            if (def.Range > 0 && DistanceToDummy > def.Range) return "out of range";

            p.ReadyAt[(int)slot] = Tick + def.Cooldown;
            p.Resource = Math.Max(0, Math.Min(Kit.ResourceCap, p.Resource + def.ResourceDelta));
            if (def.CastTicks > 0)
            {
                p.CastUntil = Tick + def.CastTicks;
                p.CastSlot = slot;
                p.AttackUntil = p.CastUntil;
                Log(TrainingEventKind.CastStarted, slot, 0, def.Name + " casting");
                return null;
            }
            p.AttackUntil = Tick + ClassKits.SharedCadence;
            DealDamage(slot, def);
            return null;
        }

        void DealDamage(ActionSlot slot, ActionDef def)
        {
            if (IsHealer && ++gleamHits % HealerRules.HitsPerFocus == 0) ChangeFocus(1);
            DealDamageAmount(slot, def.Name, def.Damage);
        }

        void DealDamageAmount(ActionSlot slot, string name, int amount)
        {
            if (FirstHitTick == 0) FirstHitTick = Tick;
            TotalDamage += amount;
            DamageBySlot[(int)slot] += amount;
            UsesBySlot[(int)slot]++;
            damageRing[Tick % DpsWindowTicks] += amount;
            windowDamage += amount;
            Log(TrainingEventKind.ActionUsed, slot, amount, name);
            if (DummyInfinite) return;
            DummyHp = Math.Max(0, DummyHp - amount);
            if (DummyHp == 0)
            {
                DummyDefeatedAt = Tick;
                LastKillTicks = Tick - FirstHitTick;
                Log(TrainingEventKind.DummyDefeated, slot, 0,
                    "Dummy down in " + (LastKillTicks / (float)TickRate).ToString("0.0") + " s");
            }
        }

        void SpawnDemoHazard()
        {
            nextHazardId++;
            int step = hazardCycle++ % (CounterableStrikes ? 3 : 2);
            if (step == 2)
            {
                // Pendulum Strike: a designated physical strike on the player's position. Avoidable by moving,
                // or precision-countered by Resonant Guard (Vanguard redesign §5). Proposed, not in the legacy prototype.
                Hazards.Add(new Hazard
                {
                    Id = nextHazardId, Name = "Pendulum Strike", Kind = HazardKind.Circle, Origin = Player.Position,
                    Radius = VanguardRules.StrikeRadius, WarningTick = Tick, ResolveTick = Tick + VanguardRules.StrikeWarning, Counterable = true,
                });
                return;
            }
            bool cone = step == 0;
            float jitter = (float)(rng.NextDouble() * 0.3 - 0.15);
            Hazards.Add(new Hazard
            {
                Id = nextHazardId,
                Name = cone ? "Bell Sweep" : "Hollow Toll",
                Kind = cone ? HazardKind.Cone : HazardKind.Donut,
                Origin = BossPosition,
                Radius = 380f,
                InnerRadius = 155f,
                Direction = (Player.Position - BossPosition).Angle + jitter,
                HalfAngle = (float)(Math.PI / 5),
                WarningTick = Tick,
                ResolveTick = Tick + HazardWarning,
            });
        }

        void ResolveHazard(Hazard h)
        {
            if (IsHealer)
            {
                foreach (var actor in SupportActors)
                    if (Eligible(actor) && h.Contains(actor.Position)) HurtSupport(actor, 1);
                return;
            }
            var p = Player;
            if (!p.Alive || !h.Contains(p.Position))
            {
                if (p.Alive) Log(TrainingEventKind.HazardResolved, ActionSlot.Guard, 0, h.Name + ": avoided");
                return;
            }
            if (IsVanguard && h.Counterable && Tick < p.GuardUntil) { CounterSuccess(h); return; }
            string outcome;
            if (Tick < p.GuardUntil) outcome = "guarded";
            else if (Tick < p.DashIframeUntil) outcome = "immune (dash)"; // dodge i-frames prevent damage; positioning still failed
            else if (Tick < p.ImmuneUntil) outcome = "immune";
            else if (Invulnerable) outcome = "hit (invulnerable)";
            else
            {
                p.Hp--;
                p.ImmuneUntil = Tick + ImmunityTicks;
                outcome = "hit";
            }
            Log(TrainingEventKind.HazardResolved, ActionSlot.Guard, 0, h.Name + ": " + outcome);
            if (p.Hp <= 0)
            {
                p.Hp = 0;
                p.CastUntil = 0;
                p.KnockedOutAt = Tick;
                Log(TrainingEventKind.KnockedOut, ActionSlot.Guard, 0, "Knocked out");
            }
        }

        public static Vec2 ClampToArena(Vec2 v)
        {
            float x = Math.Max(ArenaMin.X + ActorInset, Math.Min(ArenaMax.X - ActorInset, v.X));
            float y = Math.Max(ArenaMin.Y + ActorInset, Math.Min(ArenaMax.Y - ActorInset, v.Y));
            return new Vec2(x, y);
        }

        void Log(TrainingEventKind kind, ActionSlot slot, int damage, string text)
        {
            EventSerial++;
            Events.Add(new TrainingEvent { Tick = Tick, Kind = kind, Slot = slot, Damage = damage, Text = text });
            if (Events.Count > 64) Events.RemoveAt(0);
        }
    }
}
