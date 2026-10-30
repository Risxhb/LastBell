using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Practice phase row (Chzy18, 29 Oct): every boss on the practice list starts in each phase it offers. For each boss
    // and phase: the director keeps the pick, the boss starts with HP inside that phase's band, the right phase number and
    // name, a full player, and no transition replays or heals in the first seconds. The last phase of each boss is also
    // retried, so a restarted fight comes back in the picked phase on a fresh boss.
    //   LastBell.exe -lastbellAutotest -lastbellPhaseSelectOnly -lastbellOut <dir>   (this suite alone)
    public sealed partial class RunAutoTest
    {
        static bool PhaseSelectOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellPhaseSelectOnly", StringComparison.OrdinalIgnoreCase));

        // the phase names each boss shows at the start of each phase (minibosses show no phase text by design)
        static string[] ExpectedPhaseNames(BossEncounter b) => b switch
        {
            BellkeeperEncounter _ => new[] { BellkeeperEncounter.Phase1, BellkeeperEncounter.Phase2, BellkeeperEncounter.Phase3 },
            AurelionEncounter _ => new[] { "Perfect Order", "Misalignment", "Exposed Meridian" },
            SylvaraEncounter _ => new[] { "Dormant Choir", "Awakening Bloom", "Hollow Refrain" },
            CrucivarEncounter _ => new[] { "Sealed Furnace", "Casting Heat", "Open Pressure" },
            TempoTyrantEncounter _ => new[] { "Haste", "Broken Cadence · Haste" },
            _ => new[] { "", "" },
        };

        IEnumerator PracticePhaseChecks()
        {
            Note("---- practice phase row ----");
            yield return PhaseToMenu();
            // out-of-range picks clamp to the boss's phases (the menu never offers them; -lastbellPhase refuses them)
            int bk = Array.FindIndex(RunDirector.Bosses, b => b.Final);
            Check("practice phase: a pick past the last phase clamps to it", d.StartPractice("Vanguard", bk, 99) && d.PracticePhase == RunDirector.Bosses[bk].Phases, $"phase {d.PracticePhase}");
            yield return PhaseToMenu();
            Check("practice phase: a run always starts in phase 1", d.StartRun("Vanguard") && d.PracticePhase == 1);
            yield return PhaseToMenu();
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
                for (int n = 1; n <= RunDirector.Bosses[e].Phases; n++)
                    yield return PracticeAtPhase(e, n, n == RunDirector.Bosses[e].Phases);
            yield return PhaseToMenu();
        }

        IEnumerator PhaseToMenu()
        {
            if (d.State == RunState.Menu) yield break;
            if (d.State != RunState.Results) d.SetPaused(true);
            d.ReturnToMenu();
            yield return Until(() => d.State == RunState.Menu, 2, "menu between phase checks");
        }

        IEnumerator PracticeAtPhase(int e, int n, bool last)
        {
            yield return PhaseToMenu();
            var entry = RunDirector.Bosses[e];
            string tag = $"{LastBell.Menu.TestRushSetup.CardName(e)} phase {n}";
            Check($"{tag}: practice accepted", d.StartPractice("Vanguard", e, n));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready && RunDirector.Instance.Encounter == e, 30, tag + " ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // a new scene built the player before the bot was attached
            Check($"{tag}: the director keeps the pick", d.Mode == RunMode.Practice && d.Encounter == e && d.PracticePhase == n, $"phase {d.PracticePhase}");
            d.RequestReady();
            yield return Until(() => d.State == RunState.Intro && d.Boss, 2, tag + " intro");
            if (d.Boss && n > 1) Check($"{tag}: the boss bar shows the phase during the intro", d.Boss.PhaseNumber == n && d.Boss.Hp < d.Boss.MaxHp, $"phase {d.Boss.PhaseNumber}, hp {d.Boss.Hp}/{d.Boss.MaxHp}");
            yield return Until(() => d.State == RunState.Combat, 6, tag + " combat");
            var b = d.Boss;
            if (!b || d.State != RunState.Combat) { Check($"{tag}: fight starts", false, $"state {d.State}"); yield break; }
            CheckPhaseStart(tag, entry, b, n);
            Check($"{tag}: full health and potions", d.Player.Hp == PlayerController.MaxHp && d.Player.Potions == PlayerController.MaxPotions, $"hp {d.Player.Hp}, potions {d.Player.Potions}");

            // the first seconds: no transition replays (a stale phase would queue one at once) and the boss never heals
            int changes = 0, hp0 = b.Hp; Action<BossEncounter, string> onChange = (x, s) => changes++;
            b.PhaseChanged += onChange;
            yield return WaitAlive(3f);
            if (b) b.PhaseChanged -= onChange;
            Check($"{tag}: stays in phase {n} with no transition and no heal", b && b.PhaseNumber == n && changes == 0 && b.Hp <= hp0,
                b ? $"phase {b.PhaseNumber}, {changes} phase changes, hp {hp0} -> {b.Hp}, '{b.PhaseName}'" : "boss gone");
            if (b is BellkeeperEncounter bkp && n > 1)
                Check($"{tag}: Bellkeeper wears the phase look at once (shell parted, rings offset)", bkp.ShellAngle >= 10 && bkp.RingSpread > 0.15f, $"shell {bkp.ShellAngle:0}°, rings {bkp.RingSpread:0.00} m");
            if (last && n > 1) yield return Shot("practice_phase_" + entry.Map + "_" + e + "_p" + n);

            if (last)
            {
                // a retry repeats the picked phase on a fresh boss (no phase state carried over from the old one)
                b.DebugDefeat();
                yield return Until(() => d.State == RunState.Results, 6, tag + " results");
                Check($"{tag}: retry accepted and keeps the phase", d.RequestRetry() && d.State == RunState.Ready && d.PracticePhase == n);
                d.RequestReady();
                yield return Until(() => d.State == RunState.Combat, 6, tag + " retry combat");
                if (d.Boss && d.State == RunState.Combat)
                {
                    Check($"{tag}: retry fights a fresh boss", d.Boss != b);
                    CheckPhaseStart(tag + " (retry)", entry, d.Boss, n);
                }
            }
            // test teardown: a miniboss holds at 50 % until its shift, so DebugDefeat in phase 1 needs the last phase first
            if (d.Boss && !d.Boss.Dead) { if (d.Boss.PhaseNumber < d.Boss.PhaseCount) d.Boss.StartAtPhase(d.Boss.PhaseCount); d.Boss.DebugDefeat(); }
            yield return Until(() => d.State == RunState.Results, 6, tag + " results");
            yield return PhaseToMenu();
        }

        void CheckPhaseStart(string tag, RunDirector.BossEntry entry, BossEncounter b, int n)
        {
            var starts = b.PhaseStarts; var names = ExpectedPhaseNames(b);
            Check($"{tag}: menu row matches the encounter ({entry.Phases} phases)", starts.Length == entry.Phases && starts[0] == 1f, $"encounter has {starts.Length}");
            if (n > starts.Length) return;
            float top = starts[n - 1], bottom = n < starts.Length ? starts[n] : 0;
            bool inBand = n == 1 ? b.Hp == b.MaxHp : b.Hp <= b.MaxHp * top && b.Hp > b.MaxHp * bottom;
            Check($"{tag}: HP inside the phase band", inBand, $"hp {b.Hp}/{b.MaxHp}, band {bottom:0.###}-{top:0.###}");
            Check($"{tag}: phase {n} and its name", b.PhaseNumber == n && b.PhaseName == names[Mathf.Min(n, names.Length) - 1], $"phase {b.PhaseNumber}, '{b.PhaseName}'");
        }
    }
}
