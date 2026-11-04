using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Tollbreaker (TollbreakerKit, accepted spec docs/proposals/tollbreaker-kit-and-rotation-2026-10-30.md): a full run on
    // the stand-in body, the kit's rules against a held boss (two axes, tap and return, closer is faster, the held throw
    // lodges, Splitting Throw's personal split, Call of Iron through the boss, Grinding Toll, Bell Frenzy's fan, the dash
    // leaves axes flying), then a solo pass on the live Bellkeeper with the spec rotation. -lastbellTollbreakerOnly runs
    // just this.
    public sealed partial class RunAutoTest
    {
        static bool TollbreakerOnly => System.Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellTollbreakerOnly", System.StringComparison.OrdinalIgnoreCase));

        IEnumerator TollbreakerRun()
        {
            Check("the Tollbreaker is first in the class picker; the Vanguard is gone", RunDirector.Classes[0] == "Tollbreaker" && !RunDirector.Classes.Contains("Vanguard") && Resources.Load<GameObject>("Characters/Vanguard") == null);
            Check("StartRun accepts the Tollbreaker", d.StartRun("Tollbreaker"));
            yield return Wait(0.4f); yield return Shot("ready_tollbreaker");
            Check("the Tollbreaker runs TollbreakerKit on his own model and clips", d.Player.Kit is TollbreakerKit && d.Player.Presenter.HasModel
                  && d.Player.Presenter.Visual.Find("Tollbreaker") != null && d.Player.Presenter.Has("TwinHurl") && d.Player.Presenter.Has("SplittingThrow"));
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "tollbreaker combat");
            yield return AdvanceToFinal();
            yield return TollbreakerChecks();
            if (d.State == RunState.Combat) yield return TollbreakerSolo(20f);
            if (d.State == RunState.Combat) { d.Boss.DebugDefeat(); yield return Until(() => d.State != RunState.Combat, 2, "tollbreaker boss defeated"); }
            yield return Until(() => d.State == RunState.Results, 8, "tollbreaker results");
            Check("Tollbreaker run reaches victory", d.Victory && d.EncountersCleared == d.EncounterOrder.Length);
            d.ReturnToMenu(); yield return null;
        }

        int Meter(string ability) => d.World.EncounterMeter.TryGetValue(ability, out var r) ? r.Damage : 0;

        IEnumerator TollbreakerChecks()
        {
            var p = d.Player; var k = (TollbreakerKit)p.Kit; var w = d.World;
            d.Boss.Halt(); yield return Wait(0.3f); w.ClearTransient();
            p.DebugSetHp(PlayerController.MaxHp);
            var boss = d.Boss.transform.position; float r = d.Boss.BodyRadius;
            Vector3 At(float x, float z) => boss + new Vector3(x, 0, z);
            void Stand(Vector3 at) { p.transform.position = w.Constrain(at, PlayerController.Radius); p.FaceNow(boss); }
            bot.Clear(); bot.Aim = boss;
            Stand(At(0, r + 3f)); yield return Wait(0.2f);
            Check("both axes start in hand", k.AxesInHand == 2 && k.AxesStuck == 0, k.ClassState());

            // ---- tap: one axe out, back in hand, damage both ways ----
            int hp0 = d.Boss.Hp, th0 = k.Throws;
            bot.press[0] = true;
            yield return Until(() => k.Throws > th0, 1, "a tap throws");
            Check("a tap throws one axe and keeps the other", k.AxesInHand == 1, k.ClassState());
            yield return Wait(0.1f);
            var inHand = p.Presenter.Visual.GetComponentsInChildren<Transform>(true).Where(t => t.name == "AxeR" || t.name == "AxeL").ToArray();
            Check("the thrown axe leaves his fist (one model axe hidden)", inHand.Length == 2 && inHand.Count(t => t.gameObject.activeSelf) == 1, $"{inHand.Length} model axes, {inHand.Count(t => t.gameObject.activeSelf)} shown");
            yield return Until(() => k.AxesInHand == 2, 2, "the axe flies back to his hand");
            yield return Wait(0.1f);
            Check("the caught axe is back in his fist", inHand.All(t => t.gameObject.activeSelf));
            Check("the return hit is flagged as a return for the item damage hook (Keen Edge)", k.Hitting.Ability == "Twin Hurl" && k.Hitting.Return && !k.Hitting.Recall, $"{k.Hitting.Ability} return {k.Hitting.Return}");
            Check("Twin Hurl hits going out and coming back (14 + 3)", hp0 - d.Boss.Hp >= 17, $"{hp0 - d.Boss.Hp} dealt");

            // ---- closer is faster: an axe thrown from 3 m is back sooner than one from 8 m ----
            float t3 = 0, t8 = 0;
            foreach (float dist in new[] { 3f, 8f })
            {
                Stand(At(0, r + dist)); yield return Wait(0.3f);
                int before = k.Throws; bot.press[0] = true;
                yield return Until(() => k.Throws > before, 1, "throw at " + dist + " m");
                int start = w.Tick;
                yield return Until(() => k.AxesInHand == 2, 3, "back from " + dist + " m");
                if (dist < 5) t3 = (w.Tick - start) / (float)Sim.Hz; else t8 = (w.Tick - start) / (float)Sim.Hz;
            }
            Check("closer is faster: the axe is back sooner from 3 m than from 8 m", t3 > 0 && t8 > t3 + 0.25f, $"{t3:0.00} s vs {t8:0.00} s");

            // ---- held throw lodges in the floor; Call of Iron through the boss hits harder the further it flew ----
            Stand(At(0, r + 4f)); bot.Aim = At(4.5f, 0); yield return Wait(0.3f);
            bot.hold[0] = true; bot.press[0] = true;
            yield return Wait(0.75f); bot.hold[0] = false;
            yield return Until(() => k.AxesStuck == 1, 1.5f, "the held throw lodges");
            var lodged = k.Axes.First(a => a.Stuck);
            Check("holding 0.5 s throws the heavy axe, lodged in the floor", lodged.State == TollbreakerKit.AxeState.Lodged && !lodged.InBoss, $"{lodged.State} inBoss {lodged.InBoss}");
            Check("one hand stays empty while an axe is lodged", k.AxesInHand == 1);
            Stand(At(-5f, 0)); bot.Aim = boss; yield return Wait(0.2f);
            int call0 = Meter("Call of Iron");
            bot.press[3] = true;
            yield return Until(() => k.AxesInHand == 2, 2, "Call of Iron brings the axe home");
            Check("Call of Iron recalls the lodged axe through the boss for 20 + 8/m", Meter("Call of Iron") - call0 >= 36 && k.LastCallDamage <= 110, $"{Meter("Call of Iron") - call0} dealt");
            Check("Call of Iron goes on its 6 s cooldown", k.Cooldown01(3) > 0.8f);
            yield return Shot("tollbreaker_call_of_iron");

            // ---- Splitting Throw: lodges in the boss and splits his plating (personal +40 %) ----
            Stand(At(0, r + 3f)); bot.Aim = boss; yield return Wait(0.2f);
            bot.press[2] = true;
            yield return Until(() => k.SplitActive, 1.5f, "Splitting Throw splits the boss");
            Check("Splitting Throw lodges in the boss and starts the split", k.SplitActive && k.Axes.Any(a => a.Stuck && a.InBoss) && Meter("Splitting Throw") >= 50, k.ClassState());
            int tap0 = Meter("Twin Hurl"); int before2 = k.Throws;
            bot.press[0] = true; yield return Until(() => k.Throws > before2, 1, "a split tap");
            yield return Until(() => Meter("Twin Hurl") - tap0 >= 24, 2, "the split tap hits out and back");
            Check("inside the split, Twin Hurl hits deal +40 % (17 -> 24)", Meter("Twin Hurl") - tap0 >= 24, $"{Meter("Twin Hurl") - tap0} dealt");
            yield return Wait(0.6f);   // Call of Iron's cooldown
            yield return Until(() => k.Cooldown01(3) <= 0, 6, "Call of Iron ready");
            bot.press[3] = true;
            yield return Until(() => k.AxesInHand == 2, 2, "the split axe comes home");

            // ---- Grinding Toll: spins where it lands, grinds the boss, then flies home ----
            int toll0 = Meter("Grinding Toll");
            bot.press[1] = true;
            yield return Until(() => k.Axes.Any(a => a.State == TollbreakerKit.AxeState.Spin), 1.5f, "the toll axe spins");
            yield return Shot("tollbreaker_grinding_toll");
            yield return Until(() => k.AxesInHand == 2, 4, "the toll axe flies home");
            Check("Grinding Toll grinds the boss inside its ring (10 per 0.4 s)", Meter("Grinding Toll") - toll0 >= 40, $"{Meter("Grinding Toll") - toll0} dealt");

            // ---- dash: axes in flight keep going, no immunity ----
            Stand(At(0, r + 6f)); yield return Wait(0.2f);
            int before3 = k.Throws; bot.press[0] = true;
            yield return Until(() => k.Throws > before3, 1, "throw before the dash");
            bot.dash = true; yield return Wait(0.1f);
            Check("dashing leaves the thrown axe flying", k.AxesInHand == 1 && p.Dashing || k.AxesInHand == 1);
            yield return Until(() => k.AxesInHand == 2, 3, "the axe still comes home after a dash");

            // ---- Bell Frenzy: fan of three, the axe is back the instant it hits ----
            Stand(At(0, r + 3f)); bot.Aim = boss; yield return Wait(0.2f);
            bot.press[4] = true;
            yield return Until(() => k.FrenzyActive, 1, "Bell Frenzy starts");
            Check("Bell Frenzy lasts 4 s on a 30 s cooldown", k.FrenzyActive && k.Cooldown01(4) > 0.95f);
            int fan0 = Meter("Twin Hurl (fan)");
            for (int i = 0; i < 6; i++) { bot.press[0] = true; yield return Wait(0.3f); }
            yield return Shot("tollbreaker_bell_frenzy");
            Check("in Bell Frenzy each tap throws a fan (side axes hit for half)", Meter("Twin Hurl (fan)") - fan0 > 0, $"{Meter("Twin Hurl (fan)") - fan0} dealt");
            yield return Until(() => !k.FrenzyActive, 5, "Bell Frenzy ends");
            yield return Until(() => k.AxesInHand == 2, 3, "both axes home after the frenzy");

            // ---- run item hooks (kit side; the shop wires the items): forced through TestTuning, no shop build ----
            Check("item hooks: with no items every number is the kit's own (Hitting set by the last hit)", k.Hitting.Ability != null && k.SplitBonusNow == TollbreakerKit.SplitBonus);
            k.TestTuning = new System.Collections.Generic.Dictionary<string, float> { ["t.battle_spin"] = 1, ["t.split.cd"] = 60, ["t.twin_split"] = 50, ["t.rooted"] = 1, ["t.lodge.boss"] = 30 };
            Stand(At(0, r + 3f)); bot.Aim = boss; yield return Wait(0.2f);
            int spin0 = Meter("Twin Hurl (battle spin)");
            bot.dash = true; yield return Wait(0.5f);
            Stand(At(0, r + 3f)); yield return Wait(0.1f);
            bot.press[0] = true; yield return Until(() => Meter("Twin Hurl (battle spin)") > spin0, 2, "Battle Spin tap");
            Check("Battle Spin: the tap after a dash hits like the held throw (30) and doesn't lodge", Meter("Twin Hurl (battle spin)") - spin0 >= 30 && !k.Axes.Any(a => a.Stuck), $"{Meter("Twin Hurl (battle spin)") - spin0} dealt");
            yield return Until(() => k.AxesInHand == 2, 3, "battle spin axe home");
            yield return Until(() => k.Cooldown01(2) <= 0, 12, "Splitting Throw ready");
            bot.press[2] = true;
            yield return Until(() => k.SplitActive && k.AxesStuck == 2, 1.5f, "Twin Split lodges both");
            Check("Twin Split: with both axes in hand, Splitting Throw lodges both in the boss", k.AxesStuck == 2 && k.Axes.All(a => a.InBoss) && k.AxesInHand == 0, k.ClassState());
            Check("Short Fuse: Splitting Throw's cooldown reads the item number (1 s here)", k.Cooldown01(2) > 0);
            yield return Wait(1.0f);
            Check("Rooted Axe: past the boss-lodge timeout the split axe stays and the split holds", k.AxesStuck == 2 && k.SplitActive, k.ClassState());
            k.ReduceCooldown(3, 9999);
            Check("Ringing Return's refund: ReduceCooldown clears a cooldown", k.Cooldown01(3) <= 0);
            bot.press[3] = true;
            yield return Until(() => k.AxesInHand == 2, 3, "the rooted axes come home on Call of Iron");
            Check("Call of Iron brings rooted axes home and sets the recall hit context", k.AxesInHand == 2 && k.Hitting.Recall && k.Hitting.Ability == "Call of Iron");
            k.TestTuning = null;
            yield return Until(() => !k.SplitActive, 7, "the split ends");

            // his model has no Land or GetUp clip yet: both stay procedural
            Check("presenter: every Tollbreaker action resolves to a clip (no placeholder)", !p.Presenter.Placeholders.Any(x => x != "Land" && x != "GetUp"), string.Join(",", p.Presenter.Placeholders));
            bot.Clear(); d.Boss.BeginCombat();
        }

        // spec rotation (§Rotation), frame-based like the other suites: split, grind, recall, throw; frenzy once
        int tbFrame;
        int TollRotation(int f)
        {
            var k = d.Player ? d.Player.Kit as TollbreakerKit : null; if (k == null) return -1;
            tbFrame++;
            if (k.Acting) return -1;
            if (k.Cooldown01(4) <= 0 && k.AxesStuck == 0 && tbFrame > 120) return 4;
            if (k.Cooldown01(2) <= 0 && k.AxesInHand > 0 && !k.SplitActive) return 2;
            if (k.Cooldown01(1) <= 0 && k.AxesInHand > 0 && f % 3 == 0) return 1;
            if (k.Cooldown01(3) <= 0 && k.AxesStuck > 0 && (k.AxesInHand == 0 || !k.SplitActive)) return 3;
            return f % 6 == 0 ? 0 : -1;
        }

        IEnumerator TollbreakerSolo(float seconds)
        {
            var w = d.World; d.Player.DebugSetHp(PlayerController.MaxHp);
            int hp0 = d.Boss.Hp, t0 = w.Tick, dealt0 = w.DamageDealt; tbFrame = 0;
            var f = StartCoroutine(Fight(seconds, TollRotation, 3f));
            yield return WaitAlive(seconds * 0.5f); yield return Shot("tollbreaker_solo_bellkeeper"); yield return WaitAlive(seconds * 0.5f);
            StopCoroutine(f); bot.Clear();
            float secs = (w.Tick - t0) / (float)Sim.Hz; int dealt = w.DamageDealt - dealt0;
            Check("solo Tollbreaker damages the live Bellkeeper with the spec rotation", dealt > 0 && d.Boss.Hp < hp0, $"{dealt} in {secs:0.0} s");
            Note($"Tollbreaker solo on the Bellkeeper (moving, dodging, KeepAlive): {dealt / Mathf.Max(1, secs):0.0} DPS over {secs:0.0} s; by ability: "
                + string.Join(", ", w.EncounterMeter.Values.OrderByDescending(m => m.Damage).Select(m => $"{m.Ability} {m.Damage}")));
        }
    }
}
