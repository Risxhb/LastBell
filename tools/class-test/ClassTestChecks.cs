using System;
using System.Collections.Generic;

namespace LastBell.ClassTest.Checks
{
    public sealed class CheckResult
    {
        public int Assertions;
        public string[] PassedScenarios;
        public string[] Failures;
    }

    public static class ClassTestChecks
    {
        static int assertions;
        static List<string> failures;
        static string scenario;

        static void Check(bool ok, string what)
        {
            assertions++;
            if (!ok) failures.Add(scenario + ": " + what);
        }

        static TrainingCommand Idle() { return new TrainingCommand(); }

        static TrainingSim AtRange(ClassKit kit, float distance)
        {
            var sim = new TrainingSim(kit, 7);
            sim.Player.Position = new Vec2(TrainingSim.BossPosition.X, TrainingSim.BossPosition.Y + distance);
            return sim;
        }

        static void Run(int ticks, TrainingSim sim, TrainingCommand cmd)
        {
            for (int i = 0; i < ticks; i++) sim.Step(cmd);
        }

        public static CheckResult Run()
        {
            assertions = 0;
            failures = new List<string>();
            var passed = new List<string>();
            var scenarios = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("vanguard_basic_range_and_charge", VanguardBasic),
                new KeyValuePair<string, Action>("vanguard_technique_spends_charge", VanguardTechnique),
                new KeyValuePair<string, Action>("shared_cadence_priority", SharedCadence),
                new KeyValuePair<string, Action>("arcanist_cast_completes", ArcanistCast),
                new KeyValuePair<string, Action>("arcanist_cast_cancelled_by_move", ArcanistCancel),
                new KeyValuePair<string, Action>("arcanist_ward_step_blinks", WardStep),
                new KeyValuePair<string, Action>("hazard_guard_immunity_knockout", HazardOutcomes),
                new KeyValuePair<string, Action>("finite_dummy_defeat_and_respawn", FiniteDummy),
                new KeyValuePair<string, Action>("reset_is_deterministic", Determinism),
                new KeyValuePair<string, Action>("movement_clamped_and_finite", Movement),
                new KeyValuePair<string, Action>("healer_rules_and_encounter", Healer),
                new KeyValuePair<string, Action>("vanguard2_clockblade_combo", V2Combo),
                new KeyValuePair<string, Action>("vanguard2_directional_hits_and_assist", V2Directional),
                new KeyValuePair<string, Action>("vanguard2_pendulum_and_reversal_rush", V2Rush),
                new KeyValuePair<string, Action>("vanguard2_final_hour_and_mark", V2FinalHour),
                new KeyValuePair<string, Action>("dash_charges_recharge_iframes", DashBasics),
                new KeyValuePair<string, Action>("dash_cancel_windows_and_dash_attack", DashCancels),
                new KeyValuePair<string, Action>("resonant_guard_counter_rules", CounterRules),
                new KeyValuePair<string, Action>("universal_dash_other_classes", DashOtherClasses),
                new KeyValuePair<string, Action>("vanguard2_deterministic_replay", V2Determinism),
            };
            foreach (var s in scenarios)
            {
                scenario = s.Key;
                int before = failures.Count;
                try { s.Value(); }
                catch (Exception e) { failures.Add(scenario + ": threw " + e.GetType().Name + " " + e.Message); }
                if (failures.Count == before) passed.Add(scenario);
            }
            return new CheckResult { Assertions = assertions, PassedScenarios = passed.ToArray(), Failures = failures.ToArray() };
        }

        static void VanguardBasic()
        {
            var far = AtRange(ClassKits.VanguardLegacy, 156);
            far.Step(new TrainingCommand { Basic = true, BasicPressed = true });
            Check(far.TotalDamage == 0, "basic at 156 > range 155 must not hit");
            Check(far.Events.Exists(e => e.Kind == TrainingEventKind.ActionBlocked && e.Text.Contains("out of range")), "out-of-range feedback logged");

            var sim = AtRange(ClassKits.VanguardLegacy, 155);
            Run(24 * 10, sim, new TrainingCommand { Basic = true });
            Check(sim.UsesBySlot[0] == 10, "hold-to-repeat basic fires every 24 ticks (got " + sim.UsesBySlot[0] + ")");
            Check(sim.TotalDamage == 20, "10 basics deal 20");
            Check(sim.Player.Resource == 6, "charge capped at 6");
        }

        static void VanguardTechnique()
        {
            var sim = AtRange(ClassKits.VanguardLegacy, 100);
            sim.Step(new TrainingCommand { Technique = true, TechniquePressed = true });
            Check(sim.TotalDamage == 0, "technique blocked with 0 charge");
            Check(sim.Events.Exists(e => e.Text.Contains("need 3 Charge")), "resource feedback logged");
            sim.Player.Resource = 3;
            sim.Step(new TrainingCommand { Technique = true });
            Check(sim.TotalDamage == 9 && sim.Player.Resource == 0, "technique deals 9 and spends 3");
            Check(sim.CooldownRemaining(ActionSlot.Technique) == 48, "technique cooldown 48");
        }

        static void SharedCadence()
        {
            var sim = AtRange(ClassKits.VanguardLegacy, 100);
            sim.Player.Resource = 6;
            var all = new TrainingCommand { Basic = true, Technique = true, Signature = true };
            sim.Step(all);
            Check(sim.UsesBySlot[2] == 1 && sim.UsesBySlot[1] == 0 && sim.UsesBySlot[0] == 0, "signature has priority, one action per tick");
            Run(17, sim, all);
            Check(sim.UsesBySlot[1] == 0 && sim.UsesBySlot[0] == 0, "nothing else during 18-tick cadence");
            sim.Step(all);
            Check(sim.UsesBySlot[1] == 1, "technique fires when cadence ends");
        }

        static void ArcanistCast()
        {
            var sim = AtRange(ClassKits.Arcanist, 400);
            sim.Step(new TrainingCommand { Signature = true, SignaturePressed = true });
            Check(sim.Player.Casting && sim.TotalDamage == 0, "cast started without immediate damage");
            Vec2 start = sim.Player.Position;
            Run(59, sim, Idle());
            Check(sim.TotalDamage == 0 && sim.Player.Casting, "still casting at tick 59");
            sim.Step(Idle());
            Check(sim.TotalDamage == 24 && !sim.Player.Casting, "cast lands for 24 after 60 ticks");
            Check(Vec2.Distance(start, sim.Player.Position) == 0f, "rooted during cast");
            sim.Step(new TrainingCommand { Basic = true });
            Check(sim.UsesBySlot[0] == 1, "basic usable right after the cast");
        }

        static void ArcanistCancel()
        {
            var sim = AtRange(ClassKits.Arcanist, 400);
            sim.Step(new TrainingCommand { Signature = true });
            Run(20, sim, Idle());
            sim.Step(new TrainingCommand { Move = new Vec2(1, 0) });
            Check(!sim.Player.Casting && sim.TotalDamage == 0, "moving cancels the cast");
            Check(sim.CooldownRemaining(ActionSlot.Signature) == 60, "cancel leaves a 60-tick cooldown");
            Check(sim.Events.Exists(e => e.Kind == TrainingEventKind.CastCancelled), "cancel logged");

            var guarded = AtRange(ClassKits.Arcanist, 400);
            guarded.Step(new TrainingCommand { Signature = true });
            guarded.Step(new TrainingCommand { Guard = true });
            Check(!guarded.Player.Casting && guarded.GuardActive, "ready guard cancels cast and guards");
        }

        static void WardStep()
        {
            var sim = AtRange(ClassKits.Arcanist, 200);
            Vec2 start = sim.Player.Position;
            sim.Step(new TrainingCommand { Move = new Vec2(1, 0), Guard = true });
            float moved = sim.Player.Position.X - start.X;
            Check(Math.Abs(moved - (110f + 250f / 60f)) < 0.01f, "blink 110 plus one tick of movement (got " + moved + ")");
            Check(sim.GuardActive, "ward step guards");

            var wall = new TrainingSim(ClassKits.Arcanist, 1);
            wall.Player.Position = new Vec2(TrainingSim.ArenaMax.X - 20, 400);
            wall.Step(new TrainingCommand { Move = new Vec2(1, 0), Guard = true });
            Check(wall.Player.Position.X == TrainingSim.ArenaMax.X - TrainingSim.ActorInset, "blink clamped to arena");

            var van = AtRange(ClassKits.VanguardLegacy, 200);
            Vec2 vStart = van.Player.Position;
            van.Step(new TrainingCommand { Move = new Vec2(1, 0), Guard = true });
            Check(Math.Abs(van.Player.Position.X - vStart.X - 250f / 60f) < 0.01f, "vanguard brace does not blink");
        }

        static void HazardOutcomes()
        {
            var h = new Hazard { Kind = HazardKind.Donut, Origin = TrainingSim.BossPosition, Radius = 380, InnerRadius = 155 };
            Check(h.Contains(new Vec2(470, 360 + 155)) && !h.Contains(new Vec2(470, 360 + 154)), "donut inner edge is dangerous");
            var c = new Hazard { Kind = HazardKind.Cone, Origin = TrainingSim.BossPosition, Radius = 380, Direction = (float)Math.PI / 2, HalfAngle = (float)Math.PI / 5 };
            Check(c.Contains(new Vec2(470, 600)) && !c.Contains(new Vec2(470, 120)), "cone faces its direction");

            // Player at spawn is 230 below the boss: inside both demo hazards.
            var sim = new TrainingSim(ClassKits.VanguardLegacy, 3) { HazardsEnabled = true };
            sim.Step(Idle());
            Check(sim.Hazards.Count == 1 && sim.Hazards[0].Kind == HazardKind.Cone, "first demo hazard is a cone");
            Run(TrainingSim.HazardWarning, sim, Idle());
            Check(sim.Player.Hp == 4, "unguarded hit removes a pip");

            Run(TrainingSim.HazardInterval - TrainingSim.HazardWarning, sim, Idle());
            Check(sim.Hazards.Count == 1 && sim.Hazards[0].Kind == HazardKind.Donut, "second demo hazard is a donut");
            Run(TrainingSim.HazardWarning - 10, sim, Idle());
            sim.Step(new TrainingCommand { Guard = true });
            Run(9, sim, Idle());
            Check(sim.Player.Hp == 4, "guard on the resolve tick prevents damage");
            Check(sim.Events.Exists(e => e.Text == "Hollow Toll: guarded"), "guarded outcome logged");

            sim.Player.Hp = 1;
            Run(TrainingSim.HazardInterval, sim, Idle());
            Check(!sim.Player.Alive && sim.Events.Exists(e => e.Kind == TrainingEventKind.KnockedOut), "knockout at 0 HP");
            Run(TrainingSim.RespawnTicks, sim, Idle());
            Check(sim.Player.Alive && sim.Player.Hp == TrainingSim.MaxHp, "respawn with full HP");

            var god = new TrainingSim(ClassKits.VanguardLegacy, 3) { HazardsEnabled = true, Invulnerable = true };
            Run(1 + TrainingSim.HazardWarning, god, Idle());
            Check(god.Player.Hp == TrainingSim.MaxHp, "invulnerable takes no damage");
        }

        static void FiniteDummy()
        {
            var sim = AtRange(ClassKits.VanguardLegacy, 100);
            sim.DummyInfinite = false;
            sim.DummyMaxHp = 10;
            sim.Reset(1);
            sim.Player.Position = new Vec2(470, 460);
            Run(24 * 5, sim, new TrainingCommand { Basic = true });
            Check(sim.DummyHp == 0 && !sim.DummyAlive, "five basics kill a 10 HP dummy");
            Check(sim.Events.Exists(e => e.Kind == TrainingEventKind.DummyDefeated), "defeat logged");
            Run(TrainingSim.DummyRespawnTicks, sim, Idle());
            Check(sim.DummyHp == 10 && sim.TotalDamage == 0, "dummy and meter reset after respawn");
        }

        static void Determinism()
        {
            Func<TrainingSim, string> trace = s =>
            {
                var parts = new List<string>();
                for (int i = 0; i < 900; i++)
                {
                    s.Step(new TrainingCommand { Move = new Vec2((float)Math.Sin(i * 0.05), (float)Math.Cos(i * 0.03)), Basic = i % 3 == 0 });
                    foreach (var h in s.Hazards) if (h.WarningTick == s.Tick) parts.Add(h.Direction.ToString("R"));
                }
                parts.Add(s.TotalDamage.ToString());
                parts.Add(s.Player.Hp.ToString());
                return string.Join(",", parts.ToArray());
            };
            var sim = new TrainingSim(ClassKits.VanguardLegacy, 42) { HazardsEnabled = true };
            string first = trace(sim);
            sim.Reset(42);
            Check(trace(sim) == first, "same seed and inputs reproduce the same run after reset");
            sim.SetClass(ClassKits.Arcanist);
            Check(sim.Tick == 0 && sim.HazardsEnabled && sim.Kit == ClassKits.Arcanist, "class switch resets but keeps options");
        }


        static void Healer()
        {
            var s = new TrainingSim(ClassKits.Healer);
            s.Player.Hp = 4;
            s.Step(new TrainingCommand { Technique = true, SupportTarget = 1 });
            Check(s.Player.Casting && s.Player.Resource == 4 && s.CooldownRemaining(ActionSlot.Technique) == 180, "Mend commits resource and cooldown at start");
            Run(29, s, new TrainingCommand { Technique = true });
            Check(s.Player.Hp == 4 && s.Player.Resource == 4, "held input cannot duplicate cast");
            s.Step(Idle());
            Check(s.Player.Hp == 5 && s.Player.HotUntil == 391, "direct heal clamps and installs timed regen");
            Run(180, s, Idle());
            Check(s.Events.Exists(e => e.Kind == TrainingEventKind.Healed && e.Effect == "Mend tick" && e.Amount == 0 && e.Overheal == 1), "full health tick reports overheal");
            s.Player.Hp = 3;
            Run(180, s, Idle());
            Check(s.Player.Hp == 4 && s.Player.HotUntil == 0, "final tick occurs exactly at expiration");

            s.Reset(1); s.Player.Hp = 1;
            s.Step(new TrainingCommand { Technique = true });
            s.Step(new TrainingCommand { Move = new Vec2(1, 0) });
            Check(!s.Player.Casting && s.Player.Resource == 4 && s.CooldownRemaining(ActionSlot.Technique) == 60, "movement interrupts, no refund, short cooldown");
            s.Reset(1); s.Step(new TrainingCommand { Technique = true, SupportTarget = 2 });
            s.Ally.Position = new Vec2(60, 140); s.Step(Idle());
            Check(!s.Player.Casting && s.Ally.HotUntil == 0, "range revalidated during cast");
            foreach (int invalid in new[] { 3, 4 })
            {
                s.Reset(1); s.Step(new TrainingCommand { Technique = true, SupportTarget = invalid });
                Check(s.Player.Resource == 6 && !s.Player.Casting, "unknown target costs nothing");
            }
            for (int mode = 0; mode < 3; mode++)
            {
                s.Reset(1);
                if (mode == 0) s.Ally.Hp = 0;
                if (mode == 1) s.Ally.Present = false;
                if (mode == 2) s.Ally.Friendly = false;
                s.Step(new TrainingCommand { Technique = true, Guard = true, SupportTarget = 2 });
                Check(s.Player.Resource == 6 && !s.Player.Casting && s.Ally.Shield == 0, "dead/removed/hostile ally rejected");
            }
            s.Reset(1); s.Player.Resource = 0;
            s.Step(new TrainingCommand { Technique = true, Signature = true, Guard = true });
            Check(!s.Player.Casting && s.Player.ReadyAt[1] == 0 && s.Player.ReadyAt[2] == 0 && s.Player.ReadyAt[3] == 0, "insufficient Focus spends no cooldown");

            s.Reset(1); s.Ally.ChillUntil = 100; s.Ally.ImmuneUntil = 50;
            s.Step(new TrainingCommand { Guard = true, SupportTarget = 2 });
            Check(s.Ally.Shield == 1 && s.Ally.ChillUntil == 0 && s.Ally.ImmuneUntil == 50, "ward shields, cleanses Chill, preserves ineligible immunity");
            s.HurtSupport(s.Ally, 1);
            Check(s.Ally.Shield == 1, "immunity checked before shield");
            Run(50, s, Idle()); s.HurtSupport(s.Ally, 1);
            Check(s.Ally.Hp == 5 && s.Ally.Shield == 0, "shield absorbs one pip");
            s.Reset(1); s.Step(new TrainingCommand { Guard = true, SupportTarget = 2 });
            s.Player.ReadyAt[3] = 0; s.Step(new TrainingCommand { Guard = true, SupportTarget = 2 });
            Check(s.Ally.Shield == 1 && s.Ally.ShieldUntil == 242, "wards replace rather than stack");
            Run(239, s, Idle()); Check(s.Ally.Shield == 1, "shield lives until exclusive end");
            s.Step(Idle()); Check(s.Ally.Shield == 0, "shield expires at end tick");

            s.Reset(1); s.Ally.Hp = 1;
            s.Step(new TrainingCommand { Technique = true, SupportTarget = 2 }); Run(30, s, Idle());
            Run(149, s, Idle()); s.Step(new TrainingCommand { Technique = true, SupportTarget = 2 }); Run(30, s, Idle());
            Check(s.Ally.HotNext == s.Tick + 180, "Mend refresh replaces tick schedule");
            int hp = s.Ally.Hp; Run(179, s, Idle()); Check(s.Ally.Hp == hp, "refresh does not leave duplicate ticks");
            s.Step(Idle()); Check(s.Ally.Hp == hp + 1, "refreshed tick heals once");
            s.Ally.Present = false; s.Step(Idle()); Check(s.Ally.HotUntil == 0, "despawn clears regeneration");

            s.Reset(1); s.Player.Hp = 1; s.Ally.Hp = 4;
            s.Step(new TrainingCommand { Signature = true });
            Check(s.Player.Hp == 4 && s.Ally.Hp == 5 && s.Player.Resource == 1 && s.CooldownRemaining(ActionSlot.Signature) == 1200, "area emergency heals both with cap and expensive cooldown");
            s.Reset(1); s.Ally.Position = s.Player.Position + new Vec2(181, 0); s.Ally.Hp = 1;
            s.Step(new TrainingCommand { Signature = true }); Check(s.Ally.Hp == 1, "area excludes beyond radius");
            s.Reset(1); s.Step(new TrainingCommand { Technique = true }); s.HurtSupport(s.Player, 1);
            Check(!s.Player.Casting, "unabsorbed damage interrupts");
            s.Player.ImmuneUntil = 0; s.HurtSupport(s.Player, 10);
            Check(!s.Player.Alive && s.Player.HotUntil == 0, "lethal damage clears effects");
            Run(120, s, Idle()); Check(s.Player.Alive && s.Player.Resource == 6, "training respawn restores healer");
            s.Reset(1); s.Player.Resource = 0;
            Run(73, s, new TrainingCommand { Basic = true });
            Check(s.TotalDamage == 6 && s.Player.Resource == 1, "three landed basics grant one Focus");

            s.Reset(1); s.Step(new TrainingCommand { Technique = true, SupportTarget = 2 });
            s.Ally.Hp = 0; s.Step(Idle());
            Check(!s.Player.Casting && s.Ally.Hp == 0, "target death cancels without revival");
            s.Reset(1); s.Step(new TrainingCommand { Technique = true, SupportTarget = 2 }); Run(30, s, Idle());
            s.Player.Present = false; s.Step(Idle());
            Check(s.Ally.HotUntil == 0, "caster despawn removes owned regeneration");

            // Representative unattended support fight: no invulnerability, both actors exposed.
            s = new TrainingSim(ClassKits.Healer, 3) { HazardsEnabled = true, DummyInfinite = false };
            bool killed = false, died = false, allyDied = false;
            for (int i = 0; i < 6000 && !killed; i++)
            {
                var target = s.SelectSupportTarget(0, 360);
                bool hurt = target != null && target.Hp <= 3;
                s.Step(new TrainingCommand { Basic = true, Technique = hurt,
                    Signature = s.Player.Hp <= 2 || s.Ally.Hp <= 2,
                    Guard = s.Hazards.Exists(h => h.ResolveTick - s.Tick == 1) });
                died |= !s.Player.Alive; allyDied |= !s.Ally.Alive;
                killed = !s.DummyAlive;
            }
            Check(killed && !died && !allyDied, "Healer defeats 180 HP dummy while sustaining both actors through hazards");
        }

        // ---------- Vanguard 2.0 and universal dash (reference/LAST-BELL-VANGUARD-CLASS-REDESIGN.md §10) ----------
        static TrainingCommand Basic() { return new TrainingCommand { Basic = true, BasicPressed = true }; }
        static TrainingCommand Tech() { return new TrainingCommand { Technique = true, TechniquePressed = true }; }
        static TrainingCommand Sig() { return new TrainingCommand { Signature = true, SignaturePressed = true }; }
        static TrainingCommand Guard() { return new TrainingCommand { Guard = true, GuardPressed = true }; }
        static TrainingCommand DashCmd(float mx = 0, float my = 0) { return new TrainingCommand { Dash = true, DashPressed = true, Move = new Vec2(mx, my) }; }
        static int EventTick(TrainingSim s, TrainingEventKind k) { var e = s.Events.FindLast(x => x.Kind == k); return e == null ? -1 : e.Tick; }

        static void V2Combo()
        {
            var s = AtRange(ClassKits.Vanguard, 120);
            Check(s.IsVanguard && s.Player.Resource == 0, "new Vanguard starts at 0 Resonance");
            s.Step(Basic());
            Check(s.VAction == VanguardAction.Hit1 && s.TotalDamage == 0, "first hit starts with startup, no instant damage");
            Run(5, s, Idle());
            Check(s.TotalDamage == 2 && s.Player.Resource == 1, "hit 1 lands on its active tick for 2, +1 Resonance");
            s.Reset(7); s.Player.Position = new Vec2(470, 480);
            Run(41, s, new TrainingCommand { Basic = true });
            Check(s.TotalDamage == 8 && s.Player.Resource == 3, "held Basic chains 2+2+4 and grants Resonance per hit (got " + s.TotalDamage + ")");
            Check(s.ReversalReady && s.Events.Exists(e => e.Text == "Reversal ready (combo)"), "completed chain primes Reversal");
            Check(Vec2.Distance(s.Player.Position, TrainingSim.BossPosition) >= VanguardRules.RushStopDistance - 0.01f, "lunges stop at the boss body");
            Run(200, s, new TrainingCommand { Basic = true });
            Check(s.Player.Resource == 6, "Resonance caps at 6");

            var r = AtRange(ClassKits.Vanguard, 120);
            r.Step(Basic()); Run(16 + VanguardRules.ComboWindow + 5, r, Idle());
            r.Step(Basic());
            Check(r.VAction == VanguardAction.Hit1, "chain resets to hit 1 after the combo window");
            var c = AtRange(ClassKits.Vanguard, 120);
            c.Step(Basic()); Run(16, c, Idle()); c.Step(Basic());
            Check(c.VAction == VanguardAction.Hit2, "press inside the window continues to hit 2");
        }

        static void V2Directional()
        {
            var s = AtRange(ClassKits.Vanguard, 120);
            s.Step(new TrainingCommand { Basic = true, BasicPressed = true, Aim = new Vec2(0, 1) });
            Run(6, s, Idle());
            Check(s.TotalDamage == 0 && s.Player.Resource == 0, "swing aimed away misses and grants nothing");
            Check(s.Events.Exists(e => e.Text == "Clockblade 1: missed"), "miss is logged");
            var a = new TrainingSim(ClassKits.Vanguard, 1); a.Player.Position = new Vec2(530, 470); // ~29 degrees off the default facing
            a.Step(Basic()); Run(6, a, Idle());
            Check(a.TotalDamage == 2, "soft assist turns a near-miss onto the dummy");
            var far = new TrainingSim(ClassKits.Vanguard, 1); far.Player.Position = new Vec2(650, 470); // ~58 degrees off
            far.Step(Basic()); Run(6, far, Idle());
            Check(far.TotalDamage == 0, "assist is modest: wide misses stay misses");
        }

        static void V2Rush()
        {
            var s = AtRange(ClassKits.Vanguard, 250);
            s.Step(Tech());
            Check(s.VAction == VanguardAction.None && s.Player.Resource == 0 && s.Events.Exists(e => e.Text.Contains("need 3 Resonance")), "Technique rejected below 3 Resonance with no spend");
            s.Player.Resource = 4;
            s.Step(Tech());
            Check(s.VAction == VanguardAction.Rush && s.Player.Resource == 1 && !s.RushEmpowered, "Pendulum Rush spends 3");
            Run(VanguardRules.RushStartup + VanguardRules.RushTravel, s, Idle());
            float d = Vec2.Distance(s.Player.Position, TrainingSim.BossPosition);
            Check(Math.Abs(d - (250 - VanguardRules.RushSpeed * VanguardRules.RushTravel)) < 0.5f, "rush travels its full distance (now " + d + ")");
            Check(s.TotalDamage == VanguardRules.RushDamage, "rush strike deals 9");

            var r = AtRange(ClassKits.Vanguard, 120);
            Run(41, r, new TrainingCommand { Basic = true }); Run(30, r, Idle());
            int before = r.TotalDamage;
            r.Step(Tech());
            Check(r.RushEmpowered && !r.ReversalReady, "full combo turns the next rush into Reversal Rush and consumes the state");
            Run(VanguardRules.RushTotal, r, Idle());
            Check(r.TotalDamage - before == VanguardRules.ReversalDamage && r.DummyMarked, "Reversal Rush deals 15 and marks the target");
            var x = AtRange(ClassKits.Vanguard, 120);
            Run(41, x, new TrainingCommand { Basic = true }); Run(VanguardRules.ReversalReadyTicks + 5, x, Idle());
            x.Step(Tech());
            Check(!x.RushEmpowered, "ReversalReady expires");
        }

        static void V2FinalHour()
        {
            var s = AtRange(ClassKits.Vanguard, 220);
            s.Step(Sig());
            Check(s.VAction == VanguardAction.FinalHour && s.CooldownRemaining(ActionSlot.Signature) == 360, "Final Hour commits its 6 s cooldown at start");
            Run(VanguardRules.FinalStartup, s, Idle());
            Check(s.TotalDamage == 18, "Final Hour hits for 18 at range 220");
            s.Step(Sig());
            Check(s.Events.Exists(e => e.Text == "Final Hour: cooling down") || s.VAction == VanguardAction.FinalHour, "recast blocked while cooling down");

            var m = AtRange(ClassKits.Vanguard, 120);
            Run(41, m, new TrainingCommand { Basic = true }); Run(30, m, Idle());
            m.Step(Tech()); Run(VanguardRules.RushTotal, m, Idle());
            int before = m.TotalDamage;
            m.Step(Sig()); Run(VanguardRules.FinalStartup, m, Idle());
            Check(m.TotalDamage - before == 27 && !m.DummyMarked, "marked target takes +9 and the mark is consumed");
            Run(VanguardRules.MarkTicks, m, Idle());
            Check(!m.DummyMarked, "mark expires deterministically");
        }

        static void DashBasics()
        {
            var s = new TrainingSim(ClassKits.Vanguard, 1);
            Vec2 start = s.Player.Position;
            s.Step(DashCmd(1, 0));
            Check(s.Dashing && s.Player.DashCharges == 1, "dash spends a charge");
            Run(DashRules.Duration - 1, s, Idle());
            Check(Math.Abs(s.Player.Position.X - start.X - DashRules.Distance) < 0.01f, "dash covers its distance in 12 ticks");
            s.Step(Idle());
            Check(!s.Dashing, "dash ends after 12 ticks");
            s.Step(DashCmd(-1, 0));
            Check(s.Player.DashCharges == 0, "second charge available immediately");
            Run(DashRules.Duration - 1, s, Idle());
            s.Step(DashCmd(1, 0));
            Check(s.Player.DashCharges == 0 && !s.Dashing, "no charge, no dash");
            Run(DashRules.Recharge - s.Tick, s, Idle());
            Check(s.Player.DashCharges == 0, "first charge not back before 72 ticks");
            s.Step(Idle());
            Check(s.Player.DashCharges == 1, "first charge returns at 72 ticks");
            Run(DashRules.Recharge, s, Idle());
            Check(s.Player.DashCharges == 2, "charges recover one at a time");

            var h = new TrainingSim(ClassKits.Vanguard, 3) { HazardsEnabled = true };
            Run(84, h, Idle());
            h.Step(DashCmd());            // dash toward the boss: still inside the cone at resolve
            Run(6, h, Idle());
            Check(h.Player.Hp == 5 && h.Events.Exists(e => e.Text == "Bell Sweep: immune (dash)"), "dash i-frames prevent damage but record failed positioning");
        }

        static void DashCancels()
        {
            var s = AtRange(ClassKits.Vanguard, 120);
            s.Step(Basic()); s.Step(Idle()); s.Step(DashCmd(1, 0)); // pressed during startup
            Run(6, s, Idle());
            Check(s.TotalDamage == 2, "hit still lands when dash is pressed during startup");
            Check(EventTick(s, TrainingEventKind.DashUsed) == 7, "buffered dash cancels recovery right after the hit tick (got " + EventTick(s, TrainingEventKind.DashUsed) + ")");

            var f = AtRange(ClassKits.Vanguard, 220);
            f.Step(Sig()); Run(23, f, Idle()); f.Step(DashCmd(1, 0));
            Run(8, f, Idle());
            Check(EventTick(f, TrainingEventKind.DashUsed) == 1 + VanguardRules.FinalCancelAfter, "Final Hour keeps its commitment until tick 30");

            var d = new TrainingSim(ClassKits.Vanguard, 1);
            d.Step(DashCmd()); Run(3, d, Idle()); d.Step(Basic());
            Run(DashRules.Duration - 4, d, Idle());
            Check(d.VAction == VanguardAction.DashAttack, "Basic during a dash becomes a Dash Attack");
            Run(VanguardRules.DashAttack.Startup, d, Idle());
            Check(d.TotalDamage == VanguardRules.DashAttack.Damage && d.ComboNext == 0, "Dash Attack lunges in for 3 and does not advance the chain");
        }

        static void CounterRules()
        {
            var s = new TrainingSim(ClassKits.Vanguard, 3) { HazardsEnabled = true, CounterableStrikes = true, Invulnerable = true };
            Run(84, s, Idle()); s.Step(Guard()); Run(6, s, Idle());
            Check(s.Events.Exists(e => e.Text == "Bell Sweep: guarded") && s.Player.Resource == 0 && !s.ReversalReady, "guarding a non-counterable sweep: guarded, no Resonance, no counter");
            Run(474 - s.Tick, s, Idle());
            Check(s.Hazards.Exists(h => h.Counterable && h.Name == "Pendulum Strike"), "third demo hazard is the counterable Pendulum Strike");
            s.Player.ReadyAt[3] = 0; s.Step(Guard()); Run(481 - s.Tick, s, Idle());
            Check(s.Events.Exists(e => e.Kind == TrainingEventKind.CounterSuccess) && s.Player.Resource == 1 && s.ReversalReady, "guard on the strike: counter, +1 Resonance, Reversal primed");
            Check(!s.Events.Exists(e => e.Text.StartsWith("Pendulum Strike: hit")), "countered strike deals nothing");

            var late = new TrainingSim(ClassKits.Vanguard, 3) { HazardsEnabled = true, CounterableStrikes = true, Invulnerable = true };
            Run(440, late, Idle()); late.Step(Guard()); Run(481 - late.Tick, late, Idle());
            Check(!late.Events.Exists(e => e.Kind == TrainingEventKind.CounterSuccess), "guard that ended before the strike resolves does not counter");

            var arc = new TrainingSim(ClassKits.Arcanist, 3) { HazardsEnabled = true, CounterableStrikes = true, Invulnerable = true };
            Run(474, arc, Idle()); arc.Player.ReadyAt[3] = 0; arc.Step(Guard()); Run(481 - arc.Tick, arc, Idle());
            Check(arc.Events.Exists(e => e.Text == "Pendulum Strike: guarded") && !arc.Events.Exists(e => e.Kind == TrainingEventKind.CounterSuccess), "counter is a Vanguard reward only");
        }

        static void DashOtherClasses()
        {
            var a = AtRange(ClassKits.Arcanist, 400);
            a.Step(new TrainingCommand { Signature = true });
            Run(10, a, Idle());
            Vec2 before = a.Player.Position;
            a.Step(DashCmd(1, 0));
            Check(!a.Player.Casting && a.Events.Exists(e => e.Kind == TrainingEventKind.CastCancelled), "dash cancels the Arcanist cast");
            Run(DashRules.Duration - 1, a, Idle());
            Check(Math.Abs(a.Player.Position.X - before.X - DashRules.Distance) < 0.01f, "Arcanist dashes the full distance");

            var h = new TrainingSim(ClassKits.Healer);
            h.Step(new TrainingCommand { Technique = true, SupportTarget = 1 });
            h.Step(DashCmd(0, -1));
            Check(!h.Player.Casting && h.Events.Exists(e => e.Kind == TrainingEventKind.CastCancelled), "dash interrupts Mend");
            Check(h.Player.DashCharges == 1, "Healer has the universal dash");
        }

        static void V2Determinism()
        {
            Func<TrainingSim, string> trace = s =>
            {
                var parts = new List<string>();
                for (int i = 0; i < 1200; i++)
                {
                    var c = new TrainingCommand { Move = new Vec2((float)Math.Sin(i * 0.07), (float)Math.Cos(i * 0.05)), Basic = i % 4 != 0, BasicPressed = i % 4 == 1 };
                    if (i % 97 == 0) { c.Dash = c.DashPressed = true; }
                    if (i % 131 == 0) { c.Technique = c.TechniquePressed = true; }
                    if (i % 211 == 0) { c.Guard = c.GuardPressed = true; }
                    if (i % 400 == 0) { c.Signature = c.SignaturePressed = true; }
                    s.Step(c);
                }
                parts.Add(s.TotalDamage.ToString()); parts.Add(s.Player.Resource.ToString()); parts.Add(s.Player.Hp.ToString());
                parts.Add(s.Player.Position.X.ToString("R")); parts.Add(s.Player.Position.Y.ToString("R")); parts.Add(s.EventSerial.ToString());
                return string.Join(",", parts.ToArray());
            };
            var sim = new TrainingSim(ClassKits.Vanguard, 11) { HazardsEnabled = true, CounterableStrikes = true };
            string first = trace(sim);
            sim.Reset(11);
            string second = trace(sim);
            Check(second.Substring(0, second.LastIndexOf(',')) == first.Substring(0, first.LastIndexOf(',')), "same seed and commands replay identically after reset (" + first + " vs " + second + ")");
            Check(sim.Player.Resource >= 0 && sim.Player.Resource <= 6, "Resonance stays within 0..6");
        }

        static void Movement()
        {
            var sim = new TrainingSim(ClassKits.VanguardLegacy, 1);
            sim.Step(new TrainingCommand { Move = new Vec2(float.NaN, 1) });
            Check(sim.Player.Position.X == TrainingSim.Spawn.X && sim.Player.Position.Y == TrainingSim.Spawn.Y, "non-finite move ignored");
            sim.Step(new TrainingCommand { Move = new Vec2(3, 4) });
            Check(Math.Abs(Vec2.Distance(sim.Player.Position, TrainingSim.Spawn) - 250f / 60f) < 0.001f, "move limited to speed");
            Run(600, sim, new TrainingCommand { Move = new Vec2(-1, -1) });
            Check(sim.Player.Position.X == TrainingSim.ArenaMin.X + 14 && sim.Player.Position.Y == TrainingSim.ArenaMin.Y + 14, "clamped 14 inside the arena");
        }
    }
}
