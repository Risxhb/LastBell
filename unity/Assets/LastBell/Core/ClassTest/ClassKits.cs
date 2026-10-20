using System;

namespace LastBell.ClassTest
{
    // Provisional class-test kits. Vanguard numbers match the legacy Godot reference
    // (prototype-upgrades.md); Arcanist numbers are first guesses to tune in the arena.
    public enum ActionSlot { Basic = 0, Technique = 1, Signature = 2, Guard = 3 }

    public sealed class ActionDef
    {
        public string Name;
        public float Range;        // max player-centre to target-centre distance; 0 = no range check
        public int Damage;
        public int Cooldown;       // ticks
        public int ResourceDelta;  // + generates, - spends (must have enough)
        public int CastTicks;      // > 0: rooted cast, damage lands when the cast completes
        public int CancelCooldown; // cooldown applied when a cast is cancelled
        public string Description;
    }

    public sealed class GuardDef
    {
        public string Name;
        public int Window;         // ticks guard is active
        public int Cooldown;
        public float BlinkDistance;// > 0: guard also moves the player along the move input
        public string Description;
    }

    public sealed class ClassKit
    {
        public string Id;
        public string Name;
        public string Identity;
        public string ResourceName;
        public int ResourceCap;
        public ActionDef Basic, Technique, Signature;
        public GuardDef Guard;

        public ActionDef Offensive(ActionSlot slot)
        {
            switch (slot)
            {
                case ActionSlot.Basic: return Basic;
                case ActionSlot.Technique: return Technique;
                case ActionSlot.Signature: return Signature;
                default: throw new ArgumentOutOfRangeException("slot");
            }
        }
    }

    public static class ClassKits
    {
        public const int SharedCadence = 18; // offensive actions share this lockout

        // Vanguard 2.0, "The Clockwork Duelist" (reference/LAST-BELL-VANGUARD-CLASS-REDESIGN.md).
        // Slot data here is for HUD, range and cooldown display; behaviour lives in VanguardKit.cs (VanguardRules).
        public static readonly ClassKit Vanguard = new ClassKit
        {
            Id = "vanguard",
            Name = "Vanguard",
            Identity = "Mobile sword-and-shield duelist. Build Resonance with combos, counter designated strikes, punish with Reversal Rush and Final Hour.",
            ResourceName = "Resonance",
            ResourceCap = 6,
            Basic = new ActionDef { Name = "Clockblade Combo", Range = 175, Damage = 2, Cooldown = 0, ResourceDelta = 1,
                Description = "Three directional hits; the third sweeps wide. +1 Resonance per hit. Basic during or right after a dash: lunging Dash Attack." },
            Technique = new ActionDef { Name = "Pendulum Rush", Range = 170, Damage = 9, Cooldown = 0, ResourceDelta = -3,
                Description = "Spend 3 Resonance: shield-first charge into a strike. After a full combo or a counter it becomes Reversal Rush." },
            Signature = new ActionDef { Name = "Final Hour", Range = 270, Damage = 18, Cooldown = 360,
                Description = "Wide golden clock-hand arc. Bonus damage against a Reversal-marked target." },
            Guard = new GuardDef { Name = "Resonant Guard", Window = 15, Cooldown = 180,
                Description = "0.25 s guard. Catching a counterable strike grants Resonance and primes Reversal Rush." },
        };

        // Legacy Vanguard from the Godot reference. Kept for parity checks and prototype-upgrades.md comparisons.
        public static readonly ClassKit VanguardLegacy = new ClassKit
        {
            Id = "vanguard_legacy",
            Name = "Vanguard (legacy)",
            Identity = "Close range. Build charge, then choose when to commit the stored strike.",
            ResourceName = "Charge",
            ResourceCap = 6,
            Basic = new ActionDef { Name = "Strike", Range = 155, Damage = 2, Cooldown = 24, ResourceDelta = 1,
                Description = "+1 Charge. Hold to repeat." },
            Technique = new ActionDef { Name = "Stored Strike", Range = 185, Damage = 9, Cooldown = 48, ResourceDelta = -3,
                Description = "Spend 3 Charge." },
            Signature = new ActionDef { Name = "Bell Breaker", Range = 270, Damage = 18, Cooldown = 360,
                Description = "Big hit on a long cooldown." },
            Guard = new GuardDef { Name = "Brace", Window = 24, Cooldown = 180,
                Description = "Block hazards for 0.4 s." },
        };

        public static readonly ClassKit Arcanist = new ClassKit
        {
            Id = "arcanist",
            Name = "Arcanist",
            Identity = "Ranged. Spend energy on the move, or root yourself for a big cast.",
            ResourceName = "Energy",
            ResourceCap = 6,
            Basic = new ActionDef { Name = "Spark", Range = 420, Damage = 2, Cooldown = 24, ResourceDelta = 1,
                Description = "+1 Energy. Hold to repeat." },
            Technique = new ActionDef { Name = "Arc Lance", Range = 460, Damage = 8, Cooldown = 48, ResourceDelta = -3,
                Description = "Spend 3 Energy." },
            Signature = new ActionDef { Name = "Resonant Cast", Range = 460, Damage = 24, Cooldown = 360,
                CastTicks = 60, CancelCooldown = 60,
                Description = "1 s rooted cast. Moving or guarding cancels it." },
            Guard = new GuardDef { Name = "Ward Step", Window = 24, Cooldown = 180, BlinkDistance = 110,
                Description = "Block for 0.4 s and blink in your move direction." },
        };

        public static readonly ClassKit Healer = new ClassKit
        {
            Id = "healer", Name = "Healer",
            Identity = "Attack to restore Focus. Mend allies; anticipate damage with a cleansing ward.",
            ResourceName = "Focus", ResourceCap = 6,
            Basic = new ActionDef { Name = "Gleam", Range = 420, Damage = 2, Cooldown = 36,
                Description = "Every third landed hit restores 1 Focus." },
            Technique = new ActionDef { Name = "Mend", Range = 360, Cooldown = 180, ResourceDelta = -2,
                CastTicks = 30, CancelCooldown = 60, Description = "Heal 1, then 1 at 3 and 6 seconds. Refresh replaces regeneration." },
            Signature = new ActionDef { Name = "Sanctuary", Range = 180, Cooldown = 1200, ResourceDelta = -5,
                Description = "Immediately heal nearby living allies for 3." },
            Guard = new GuardDef { Name = "Purifying Ward", Window = 0, Cooldown = 360,
                Description = "1 Focus: shield an ally for 1 damage for 4 seconds; cleanse Chill." },
        };
        public static readonly ClassKit[] All = { Vanguard, Arcanist, Healer };
    }
}
