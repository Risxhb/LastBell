using System;

namespace LastBell.ClassTest
{
    // Initial tuning against five HP and one-pip hazards every 3.5 seconds.
    public static class HealerRules
    {
        public const int DirectHeal = 1, HotHeal = 1, HotInterval = 180, HotDuration = 360;
        public const int AreaHeal = 3, WardCost = 1, Shield = 1, ShieldDuration = 240;
        public const int RegenInterval = 180, HitsPerFocus = 3, ChillDuration = 300;
        public const float ChillSpeed = 0.65f;
    }

    public sealed partial class TrainingSim
    {
        public bool IsHealer { get { return Kit.Id == "healer"; } }
        public readonly TrainingPlayer Ally = new TrainingPlayer { Id = 2 };
        public TrainingPlayer[] SupportActors { get { return new[] { Player, Ally }; } }
        int gleamHits, castTarget;
        public int CurrentCastTarget { get { return castTarget; } }
        public static bool Eligible(TrainingPlayer a) { return a != null && a.Present && a.Friendly && a.Alive && a.Position.IsFinite; }

        static void ClearSupport(TrainingPlayer a)
        {
            a.Shield = a.ShieldUntil = a.HotNext = a.HotUntil = a.ChillUntil = 0;
        }
        void ResetSupport()
        {
            gleamHits = castTarget = 0;
            Player.Present = Player.Friendly = true;
            Ally.Present = Ally.Friendly = true;
            Ally.Hp = MaxHp;
            Ally.Position = new Vec2(560, 590);
            Ally.ImmuneUntil = Ally.KnockedOutAt = 0;
            ClearSupport(Ally);
        }
        void Emit(TrainingEventKind kind, ActionSlot slot, TrainingPlayer target, string effect, int amount = 0, int overheal = 0, int until = 0)
        {
            Log(kind, slot, 0, effect + " -> " + target.Id + (amount != 0 ? " " + amount : ""));
            var e = Events[Events.Count - 1];
            e.SourceId = 1; e.TargetId = target.Id; e.Effect = effect;
            e.Amount = amount; e.Overheal = overheal; e.Until = until;
        }
        void ChangeFocus(int delta)
        {
            int before = Player.Resource;
            Player.Resource = Math.Max(0, Math.Min(Kit.ResourceCap, before + delta));
            if (before != Player.Resource) Emit(TrainingEventKind.ResourceChanged, ActionSlot.Basic, Player, "Focus", Player.Resource - before);
        }
        public TrainingPlayer SelectSupportTarget(int id, float range)
        {
            TrainingPlayer best = null;
            foreach (var a in SupportActors)
            {
                if (!Eligible(a) || Vec2.Distance(Player.Position, a.Position) > range || (id != 0 && id != a.Id)) continue;
                if (best == null || a.Hp < best.Hp) best = a;
            }
            return best;
        }
        void Heal(TrainingPlayer a, int amount, ActionSlot slot, string effect)
        {
            if (!Eligible(a)) return;
            int actual = Math.Min(amount, Math.Max(0, MaxHp - a.Hp));
            a.Hp += actual;
            Emit(TrainingEventKind.Healed, slot, a, effect, actual, amount - actual);
        }
        void StepSupport()
        {
            if (!IsHealer) return;
            if (!Eligible(Player))
            {
                CancelMend("Caster unavailable");
                foreach (var a in SupportActors)
                {
                    if (a.HotUntil > 0) Emit(TrainingEventKind.EffectExpired, ActionSlot.Technique, a, "Mend: caster unavailable");
                    a.HotNext = a.HotUntil = 0;
                }
            }
            if (Eligible(Player) && Tick % HealerRules.RegenInterval == 0) ChangeFocus(1);
            foreach (var a in SupportActors)
            {
                if (!Eligible(a)) { ClearSupport(a); continue; }
                if (a.ShieldUntil > 0 && Tick >= a.ShieldUntil)
                {
                    a.Shield = a.ShieldUntil = 0;
                    Emit(TrainingEventKind.EffectExpired, ActionSlot.Guard, a, "Ward");
                }
                if (a.ChillUntil > 0 && Tick >= a.ChillUntil)
                {
                    a.ChillUntil = 0;
                    Emit(TrainingEventKind.EffectExpired, ActionSlot.Guard, a, "Chill");
                }
                if (a.HotNext > 0 && Tick >= a.HotNext && Tick <= a.HotUntil)
                {
                    Heal(a, HealerRules.HotHeal, ActionSlot.Technique, "Mend tick");
                    a.HotNext += HealerRules.HotInterval;
                }
                if (a.HotUntil > 0 && Tick >= a.HotUntil)
                {
                    a.HotNext = a.HotUntil = 0;
                    Emit(TrainingEventKind.EffectExpired, ActionSlot.Technique, a, "Mend");
                }
            }
        }
        void CancelMend(string reason)
        {
            if (!Player.Casting) return;
            Player.CastUntil = 0;
            Player.ReadyAt[1] = Tick + Kit.Technique.CancelCooldown;
            Player.AttackUntil = Tick;
            Emit(TrainingEventKind.CastCancelled, ActionSlot.Technique, Player, reason);
            castTarget = 0; // committed Focus is not refunded
        }
        void StepHealer(TrainingCommand cmd)
        {
            var p = Player;
            if (!Eligible(p)) { CancelMend("Caster unavailable"); return; }
            Vec2 move = cmd.Move.IsFinite ? cmd.Move.LimitLength(1) : new Vec2();
            bool dashStart = WantsDash(cmd); UpdateFacing(cmd, move);
            if (p.Casting && (move.Length > CastCancelThreshold || dashStart || (cmd.Guard && Tick >= p.ReadyAt[3] && p.Resource >= HealerRules.WardCost && SelectSupportTarget(cmd.SupportTarget, Kit.Technique.Range) != null))) CancelMend("Interrupted");
            if (dashStart) StartDash(move);
            if (!StepDashMotion() && !p.Casting) p.Position = ClampToArena(p.Position + move * (Speed / TickRate * (Tick < p.ChillUntil ? HealerRules.ChillSpeed : 1f)));
            if (cmd.Guard)
            {
                var target = SelectSupportTarget(cmd.SupportTarget, Kit.Technique.Range);
                string reason = Tick < p.ReadyAt[3] ? "cooling down" : p.Resource < HealerRules.WardCost ? "need Focus" : target == null ? "invalid target / range" : null;
                if (reason == null)
                {
                    ChangeFocus(-HealerRules.WardCost);
                    p.ReadyAt[3] = Tick + Kit.Guard.Cooldown;
                    target.Shield = HealerRules.Shield; target.ShieldUntil = Tick + HealerRules.ShieldDuration;
                    Emit(TrainingEventKind.ShieldApplied, ActionSlot.Guard, target, "Ward", target.Shield, 0, target.ShieldUntil);
                    if (target.ChillUntil > Tick)
                    {
                        target.ChillUntil = 0;
                        Emit(TrainingEventKind.Cleansed, ActionSlot.Guard, target, "Chill", 1);
                    }
                    Emit(TrainingEventKind.CastCompleted, ActionSlot.Guard, target, "Ward");
                }
                else if (cmd.GuardPressed) Log(TrainingEventKind.ActionBlocked, ActionSlot.Guard, 0, reason);
            }
            if (p.Casting)
            {
                var target = SelectSupportTarget(castTarget, Kit.Technique.Range);
                if (target == null) CancelMend("Target unavailable / range");
                else if (Tick >= p.CastUntil)
                {
                    p.CastUntil = 0;
                    Heal(target, HealerRules.DirectHeal, ActionSlot.Technique, "Mend");
                    target.HotNext = Tick + HealerRules.HotInterval;
                    target.HotUntil = Tick + HealerRules.HotDuration;
                    Emit(TrainingEventKind.CastCompleted, ActionSlot.Technique, target, "Mend", 0, 0, target.HotUntil);
                    castTarget = 0;
                }
            }
            foreach (var slot in new[] { ActionSlot.Signature, ActionSlot.Technique, ActionSlot.Basic })
            {
                if (!cmd.Held(slot)) continue;
                string reason = slot == ActionSlot.Basic ? TryAttack(slot) : TrySupport(slot, cmd.SupportTarget);
                if (reason == null) break;
                if (cmd.Pressed(slot)) Log(TrainingEventKind.ActionBlocked, slot, 0, Kit.Offensive(slot).Name + ": " + reason);
            }
        }
        string TrySupport(ActionSlot slot, int targetId)
        {
            var p = Player; var def = Kit.Offensive(slot);
            if (p.Casting) return "casting";
            if (Tick < p.ReadyAt[(int)slot]) return "cooling down";
            if (Tick < p.AttackUntil) return "attack cadence";
            if (p.Resource < -def.ResourceDelta) return "need " + (-def.ResourceDelta) + " Focus";
            var target = slot == ActionSlot.Signature ? Player : SelectSupportTarget(targetId, def.Range);
            if (target == null) return "invalid target / range";
            ChangeFocus(def.ResourceDelta);
            p.ReadyAt[(int)slot] = Tick + def.Cooldown;
            p.AttackUntil = Tick + Math.Max(ClassKits.SharedCadence, def.CastTicks);
            if (slot == ActionSlot.Technique)
            {
                p.CastSlot = slot; p.CastUntil = Tick + def.CastTicks; castTarget = target.Id;
                Emit(TrainingEventKind.CastStarted, slot, target, "Mend", 0, 0, p.CastUntil);
            }
            else
            {
                foreach (var a in SupportActors)
                    if (Eligible(a) && Vec2.Distance(p.Position, a.Position) <= def.Range) Heal(a, HealerRules.AreaHeal, slot, "Sanctuary");
                Emit(TrainingEventKind.CastCompleted, slot, p, "Sanctuary");
            }
            return null;
        }
        // Shared hazard damage path for healer and training ally; immunity precedes shields.
        public void HurtSupport(TrainingPlayer a, int amount)
        {
            if (!IsHealer || !Eligible(a) || amount <= 0 || Invulnerable || Tick < a.ImmuneUntil || Tick < a.DashIframeUntil) return;
            int absorbed = Tick < a.ShieldUntil ? Math.Min(a.Shield, amount) : 0;
            a.Shield -= absorbed; amount -= absorbed;
            if (absorbed > 0) Emit(TrainingEventKind.ShieldAbsorbed, ActionSlot.Guard, a, "Ward", absorbed);
            a.ImmuneUntil = Tick + ImmunityTicks;
            if (amount == 0) return;
            a.Hp = Math.Max(0, a.Hp - amount);
            a.ChillUntil = Tick + HealerRules.ChillDuration;
            Emit(TrainingEventKind.HazardResolved, ActionSlot.Guard, a, "Hit + Chill", amount, 0, a.ChillUntil);
            if (a == Player) CancelMend("Damage interrupted");
            if (!a.Alive)
            {
                a.KnockedOutAt = Tick; ClearSupport(a);
                if (a == Player) foreach (var actor in SupportActors) { actor.HotNext = actor.HotUntil = 0; }
                Emit(TrainingEventKind.KnockedOut, ActionSlot.Guard, a, "Knocked out");
            }
        }
    }
}
