using System;

namespace LastBell.ClassTest
{
    // Vanguard 2.0 "Clockwork Duelist" and the universal Clockstep Dash
    // (reference/LAST-BELL-VANGUARD-CLASS-REDESIGN.md). Every number is a provisional tuning target in
    // 60 Hz ticks and legacy arena units, not a balance claim.

    public static class DashRules
    {
        public const int Charges = 2;          // doc: 2 charges
        public const int Recharge = 72;        // doc: 1.2 s per charge, recovered one at a time
        public const int Duration = 12;        // doc: 0.20 s
        public const float Distance = 150f;    // tuning target (baseline run covers 50 units in 0.2 s)
        public const int Iframes = 10;         // tuning target; damage is prevented, positioning still fails
        public const int Buffer = 9;           // doc: ~0.15 s input buffering
    }

    public sealed class ComboHit
    {
        public readonly int Startup, Total, Damage; public readonly float Range, ArcDeg, Lunge;
        public ComboHit(int startup, int total, int damage, float range, float arcDeg, float lunge)
        { Startup = startup; Total = total; Damage = damage; Range = range; ArcDeg = arcDeg; Lunge = lunge; }
    }

    public static class VanguardRules
    {
        // Clockblade Combo: three fast directional hits, the third a broad sweeping finisher.
        public static readonly ComboHit[] Combo =
        {
            new ComboHit(5, 16, 2, 160, 110, 1.4f),
            new ComboHit(5, 16, 2, 160, 110, 1.4f),
            new ComboHit(8, 26, 4, 175, 170, 1.0f),
        };
        public static readonly ComboHit DashAttack = new ComboHit(6, 20, 3, 170, 120, 4.5f);
        public const int ComboWindow = 24;      // ticks after a hit's recovery in which the next hit continues the chain
        public const int DashAttackWindow = 6;  // Basic pressed during a dash or this many ticks after it
        public const int ResonanceCap = 6;

        public const int RushCost = 3, RushStartup = 4, RushTravel = 14, RushTotal = 30;
        public const float RushSpeed = 11f, RushStopDistance = 70f, RushHitRange = 110f, RushArcDeg = 160f;
        public const int RushDamage = 9, ReversalDamage = 15;
        public const float ShockwaveRadius = 140f;

        public const int FinalStartup = 18, FinalTotal = 48, FinalCancelAfter = 30, FinalDamage = 18, FinalMarkBonus = 9;
        public const float FinalRange = 270f, FinalArcDeg = 220f;

        public const int ReversalReadyTicks = 150, MarkTicks = 240;
        public const float SoftAssistDeg = 30f, SoftAssistExtraRange = 40f, BodyContact = 40f;

        public const float StrikeRadius = 80f;  // counterable Pendulum Strike (proposed boss attack)
        public const int StrikeWarning = 60;
    }

    public enum VanguardAction { None, Hit1, Hit2, Hit3, DashAttack, Rush, FinalHour }

    public sealed partial class TrainingSim
    {
        public bool IsVanguard { get { return Kit.Id == "vanguard"; } }
        public VanguardAction VAction { get; private set; }
        public int VActionStart { get; private set; }
        public int VActionHitTick { get; private set; }
        public int VActionEnd { get; private set; }
        public int ComboNext { get; private set; }     // 0..2: which combo hit comes next
        public int ComboExpire { get; private set; }
        public int ReversalReadyUntil { get; private set; }
        public int MarkUntil { get; private set; }     // ReversalMark on the dummy
        public bool RushEmpowered { get; private set; }
        public bool ReversalReady { get { return Tick < ReversalReadyUntil; } }
        public bool DummyMarked { get { return Tick < MarkUntil; } }
        public bool Dashing { get { return Tick < Player.DashUntil; } }
        Vec2 rushDir;
        int bufBasic, bufTech, bufSig, bufDash, lastDashEnd;

        void ResetVanguard()
        {
            VAction = VanguardAction.None; VActionStart = VActionHitTick = VActionEnd = 0;
            ComboNext = 0; ComboExpire = 0; ReversalReadyUntil = 0; MarkUntil = 0; RushEmpowered = false;
            bufBasic = bufTech = bufSig = bufDash = lastDashEnd = 0; dashAttackQueued = false;
        }

        static float AngDiff(float a, float b) { return (float)Math.Atan2(Math.Sin(a - b), Math.Cos(a - b)); }
        static Vec2 Dir(float angle) { return new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)); }

        // ---------- Facing (shared) ----------
        void UpdateFacing(TrainingCommand cmd, Vec2 move)
        {
            if (cmd.Aim.IsFinite && cmd.Aim.Length > 0.2f) Player.Facing = cmd.Aim.Angle;
            else if (move.Length > 0.2f) Player.Facing = move.Angle;
        }

        // Modest soft-target assistance: snap toward the dummy when it is nearly in front and nearly in reach.
        void SoftAssist(float range)
        {
            var p = Player; Vec2 to = BossPosition - p.Position;
            if (!DummyAlive || to.Length > range + VanguardRules.SoftAssistExtraRange) return;
            if (Math.Abs(AngDiff(to.Angle, p.Facing)) <= VanguardRules.SoftAssistDeg * Math.PI / 180) p.Facing = to.Angle;
        }

        bool InArc(float range, float arcDeg)
        {
            Vec2 to = BossPosition - Player.Position; float d = to.Length;
            if (!DummyAlive || d > range) return false;
            if (d <= VanguardRules.BodyContact) return true;
            return Math.Abs(AngDiff(to.Angle, Player.Facing)) <= arcDeg * Math.PI / 360;
        }

        // ---------- Universal Clockstep Dash ----------
        bool DashAllowedNow()
        {
            if (!IsVanguard) return true;
            switch (VAction)
            {
                case VanguardAction.None: return true;
                case VanguardAction.FinalHour: return Tick >= VActionStart + VanguardRules.FinalCancelAfter; // keeps Signature commitment
                default: return Tick > VActionHitTick; // cancels recovery only, after the hit has resolved
            }
        }

        // Call once per tick from every class. Handles recharge, press buffering and availability.
        bool WantsDash(TrainingCommand cmd)
        {
            var p = Player;
            if (p.DashCharges < DashRules.Charges && Tick >= p.DashRechargeAt)
            {
                p.DashCharges++;
                if (p.DashCharges < DashRules.Charges) p.DashRechargeAt = Tick + DashRules.Recharge;
            }
            if (cmd.DashPressed) bufDash = Tick;
            if (bufDash == 0 || Tick - bufDash > DashRules.Buffer) return false;
            if (p.DashCharges <= 0 || Dashing || !DashAllowedNow()) return false;
            bufDash = 0;
            return true;
        }

        void StartDash(Vec2 move)
        {
            var p = Player;
            Vec2 dir = move.Length > 0.1f ? move * (1f / move.Length) : Dir(p.Facing);
            if (p.DashCharges == DashRules.Charges) p.DashRechargeAt = Tick + DashRules.Recharge;
            p.DashCharges--;
            p.DashDir = dir; p.DashUntil = Tick + DashRules.Duration; p.DashIframeUntil = Tick + DashRules.Iframes;
            p.Facing = dir.Angle; lastDashEnd = p.DashUntil;
            if (IsVanguard) VAction = VanguardAction.None;
            Log(TrainingEventKind.DashUsed, ActionSlot.Guard, 0, "Clockstep Dash");
        }

        bool StepDashMotion()
        {
            var p = Player;
            if (Tick >= p.DashUntil) return false;
            p.Position = ClampToArena(p.Position + p.DashDir * (DashRules.Distance / DashRules.Duration));
            return true;
        }

        // ---------- Vanguard ----------
        bool Pending(int buf, bool held) { return held || (buf > 0 && Tick - buf <= DashRules.Buffer); }
        bool dashAttackQueued;

        void StepVanguard(TrainingCommand cmd)
        {
            var p = Player;
            Vec2 move = cmd.Move.IsFinite ? cmd.Move.LimitLength(1f) : new Vec2(0, 0);
            if (cmd.BasicPressed) bufBasic = Tick;
            if (cmd.TechniquePressed) bufTech = Tick;
            if (cmd.SignaturePressed) bufSig = Tick;

            if (cmd.Guard && Tick >= p.ReadyAt[(int)ActionSlot.Guard])
            {
                p.GuardUntil = Tick + Kit.Guard.Window;
                p.ReadyAt[(int)ActionSlot.Guard] = Tick + Kit.Guard.Cooldown;
                Log(TrainingEventKind.GuardUsed, ActionSlot.Guard, 0, Kit.Guard.Name);
            }
            else if (cmd.GuardPressed && Tick < p.ReadyAt[(int)ActionSlot.Guard])
                Log(TrainingEventKind.ActionBlocked, ActionSlot.Guard, 0, Kit.Guard.Name + ": cooling down");

            if (WantsDash(cmd)) StartDash(move);
            if (StepDashMotion()) { if (cmd.BasicPressed) dashAttackQueued = true; return; } // queues the Dash Attack

            if (VAction == VanguardAction.None)
            {
                UpdateFacing(cmd, move);
                p.Position = ClampToArena(p.Position + move * (Speed / TickRate));
            }
            else AdvanceVanguardAction(cmd);

            if (VAction == VanguardAction.None) TryStartVanguardAction(cmd);
        }

        void BeginAction(VanguardAction a, int hitAfter, int total)
        {
            VAction = a; VActionStart = Tick; VActionHitTick = Tick + hitAfter; VActionEnd = Tick + total;
        }

        void TryStartVanguardAction(TrainingCommand cmd)
        {
            var p = Player;
            bool sig = Pending(bufSig, cmd.Signature), tech = Pending(bufTech, cmd.Technique), basic = Pending(bufBasic, cmd.Basic) || dashAttackQueued;
            if (sig)
            {
                if (Tick >= p.ReadyAt[(int)ActionSlot.Signature])
                {
                    bufSig = 0; UpdateFacing(cmd, new Vec2(0, 0)); SoftAssist(VanguardRules.FinalRange);
                    p.ReadyAt[(int)ActionSlot.Signature] = Tick + Kit.Signature.Cooldown;
                    BeginAction(VanguardAction.FinalHour, VanguardRules.FinalStartup, VanguardRules.FinalTotal);
                    ComboNext = 0; Log(TrainingEventKind.CastStarted, ActionSlot.Signature, 0, "Final Hour winds up");
                    return;
                }
                if (cmd.SignaturePressed) Log(TrainingEventKind.ActionBlocked, ActionSlot.Signature, 0, "Final Hour: cooling down");
            }
            if (tech)
            {
                if (p.Resource >= VanguardRules.RushCost)
                {
                    bufTech = 0; UpdateFacing(cmd, new Vec2(0, 0)); SoftAssist(VanguardRules.RushHitRange + VanguardRules.RushSpeed * VanguardRules.RushTravel);
                    p.Resource -= VanguardRules.RushCost;
                    RushEmpowered = ReversalReady; if (RushEmpowered) ReversalReadyUntil = 0;
                    rushDir = Dir(p.Facing);
                    BeginAction(VanguardAction.Rush, VanguardRules.RushStartup + VanguardRules.RushTravel, VanguardRules.RushTotal);
                    ComboNext = 0;
                    Log(TrainingEventKind.CastStarted, ActionSlot.Technique, 0, RushEmpowered ? "Reversal Rush" : "Pendulum Rush");
                    return;
                }
                if (cmd.TechniquePressed) Log(TrainingEventKind.ActionBlocked, ActionSlot.Technique, 0, "Pendulum Rush: need " + VanguardRules.RushCost + " Resonance");
            }
            if (basic)
            {
                bufBasic = 0; UpdateFacing(cmd, new Vec2(0, 0));
                if (dashAttackQueued || (lastDashEnd > 0 && Tick <= lastDashEnd + VanguardRules.DashAttackWindow))
                {
                    lastDashEnd = 0; dashAttackQueued = false; SoftAssist(VanguardRules.DashAttack.Range);
                    BeginAction(VanguardAction.DashAttack, VanguardRules.DashAttack.Startup, VanguardRules.DashAttack.Total);
                    return;
                }
                int step = Tick <= ComboExpire ? ComboNext : 0;
                var hit = VanguardRules.Combo[step]; SoftAssist(hit.Range);
                BeginAction(VanguardAction.Hit1 + step, hit.Startup, hit.Total);
            }
        }

        void AdvanceVanguardAction(TrainingCommand cmd)
        {
            var p = Player;
            float toBoss = Vec2.Distance(p.Position, BossPosition);
            switch (VAction)
            {
                case VanguardAction.Hit1: case VanguardAction.Hit2: case VanguardAction.Hit3: case VanguardAction.DashAttack:
                {
                    var hit = VAction == VanguardAction.DashAttack ? VanguardRules.DashAttack : VanguardRules.Combo[VAction - VanguardAction.Hit1];
                    if (Tick < VActionHitTick && toBoss > VanguardRules.RushStopDistance) p.Position = ClampToArena(p.Position + Dir(p.Facing) * hit.Lunge);
                    if (Tick == VActionHitTick) ResolveVanguardHit(hit);
                    break;
                }
                case VanguardAction.Rush:
                {
                    int t = Tick - VActionStart;
                    if (t < VanguardRules.RushStartup) { if (cmd.Aim.IsFinite && cmd.Aim.Length > 0.2f) { p.Facing = cmd.Aim.Angle; rushDir = Dir(p.Facing); } } // aim correction only
                    else if (t < VanguardRules.RushStartup + VanguardRules.RushTravel && toBoss > VanguardRules.RushStopDistance)
                        p.Position = ClampToArena(p.Position + rushDir * VanguardRules.RushSpeed);
                    if (Tick == VActionHitTick) ResolveRush();
                    break;
                }
                case VanguardAction.FinalHour:
                    if (Tick == VActionHitTick) ResolveFinalHour();
                    break;
            }
            if (Tick >= VActionEnd) VAction = VanguardAction.None;
        }

        void GainResonance(int n)
        {
            Player.Resource = Math.Max(0, Math.Min(VanguardRules.ResonanceCap, Player.Resource + n));
        }

        void ResolveVanguardHit(ComboHit hit)
        {
            bool dashAttack = VAction == VanguardAction.DashAttack;
            string name = dashAttack ? "Dash Attack" : "Clockblade " + (VAction - VanguardAction.Hit1 + 1);
            if (dashAttack) ComboNext = 0;
            else { ComboNext = (VAction - VanguardAction.Hit1 + 1) % 3; ComboExpire = VActionEnd + VanguardRules.ComboWindow; }
            if (!InArc(hit.Range, hit.ArcDeg)) { Log(TrainingEventKind.ActionBlocked, ActionSlot.Basic, 0, name + ": missed"); return; }
            DealDamageAmount(ActionSlot.Basic, name, hit.Damage);
            GainResonance(1); // once per successful attack action, never per target
            if (VAction == VanguardAction.Hit3)
            {
                ReversalReadyUntil = Tick + VanguardRules.ReversalReadyTicks;
                Log(TrainingEventKind.ReversalReady, ActionSlot.Technique, 0, "Reversal ready (combo)");
            }
        }

        void ResolveRush()
        {
            float d = Vec2.Distance(Player.Position, BossPosition);
            bool strike = InArc(VanguardRules.RushHitRange, VanguardRules.RushArcDeg);
            if (RushEmpowered)
            {
                if (strike || (DummyAlive && d <= VanguardRules.ShockwaveRadius))
                {
                    DealDamageAmount(ActionSlot.Technique, "Reversal Rush", VanguardRules.ReversalDamage);
                    MarkUntil = Tick + VanguardRules.MarkTicks;
                    Log(TrainingEventKind.Marked, ActionSlot.Technique, 0, "Reversal mark");
                }
                else Log(TrainingEventKind.ActionBlocked, ActionSlot.Technique, 0, "Reversal Rush: missed");
            }
            else if (strike) DealDamageAmount(ActionSlot.Technique, "Pendulum Rush", VanguardRules.RushDamage);
            else Log(TrainingEventKind.ActionBlocked, ActionSlot.Technique, 0, "Pendulum Rush: missed");
        }

        void ResolveFinalHour()
        {
            if (!InArc(VanguardRules.FinalRange, VanguardRules.FinalArcDeg)) { Log(TrainingEventKind.ActionBlocked, ActionSlot.Signature, 0, "Final Hour: missed"); return; }
            bool marked = DummyMarked;
            if (marked) MarkUntil = 0;
            DealDamageAmount(ActionSlot.Signature, marked ? "Final Hour (marked)" : "Final Hour", VanguardRules.FinalDamage + (marked ? VanguardRules.FinalMarkBonus : 0));
        }

        // A designated counterable strike caught by an active Resonant Guard: no damage, positioning not failed,
        // +1 Resonance and Reversal primed. Non-counterable hazards that touch Guard stay "guarded" (failed positioning).
        void CounterSuccess(Hazard h)
        {
            GainResonance(1);
            ReversalReadyUntil = Tick + VanguardRules.ReversalReadyTicks;
            Log(TrainingEventKind.CounterSuccess, ActionSlot.Guard, 0, h.Name + ": countered");
            Log(TrainingEventKind.ReversalReady, ActionSlot.Technique, 0, "Reversal ready (counter)");
        }
    }
}
