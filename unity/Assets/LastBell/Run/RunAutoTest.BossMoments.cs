using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using LastBell.Run.Presentation;

namespace LastBell.Run
{
    // Boss intros and defeats (Presentation lane, 8 Nov 2026):
    //   LastBell.exe -lastbellAutotest -lastbellBossMomentsOnly -lastbellOut <dir>
    // For each main boss, with the moments on and then off: the intro lasts exactly IntroTicks and Cleared exactly ClearTicks,
    // the first 600 combat ticks hash the same, the card lines are right, every boss shows an entrance clip, the defeat
    // freezes, slows and holds the boss's Defeat clip, the camera framing is handed back, and the moment allocates nothing per
    // frame. A windowed run also takes before/after pictures of the intro card and the defeat.
    public sealed partial class RunAutoTest
    {
        static bool BossMomentsOnly => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellBossMomentsOnly", StringComparison.OrdinalIgnoreCase));
        const int MomentTicks = 600;

        IEnumerator BossMomentsChecks()
        {
            Note("---------- boss intros and defeats (Presentation lane) ----------");
            float t0 = Time.realtimeSinceStartup;
            Check("moments: on by default (settings stub)", PresentationSettings.BossMoments);
            Check("moments: the BossMoments component rides on the director", d.GetComponent<BossMoments>() != null);
            long probe0 = GC.GetAllocatedBytesForCurrentThread(); var junk = new byte[4096]; bool counter = GC.GetAllocatedBytesForCurrentThread() - probe0 >= 4096 && junk.Length > 0;
            for (int e = 0; e < RunDirector.Bosses.Length; e++)
            {
                if (!RunDirector.Bosses[e].InRun) continue;
                string cls = RunDirector.Classes[e % RunDirector.Classes.Length], tag = "moments: " + LastBell.Menu.TestRushSetup.CardName(e);
                var on = new MomentPass(); var off = new MomentPass();
                yield return MomentRun(e, cls, true, on, counter, e == RunDirector.SylvaraPractice || e == RunDirector.BellkeeperUltimatePractice ? "after" : null);
                yield return MomentRun(e, cls, false, off, false, e == RunDirector.SylvaraPractice || e == RunDirector.BellkeeperUltimatePractice ? "before" : null);
                Note($"{tag}: intro {on.IntroTicks}/{off.IntroTicks} ticks, cleared {on.ClearTicks}/{off.ClearTicks} ticks, entrance '{on.Entrance}', role '{on.Role}', title '{on.Epithet}', defeat clip '{on.DefeatClip}', fight time '{on.FightTime}'");
                Check($"{tag}: the intro lasts the same with and without the moments", on.IntroTicks == off.IntroTicks && on.IntroTicks == 120, $"{on.IntroTicks} vs {off.IntroTicks} ticks");
                Check($"{tag}: Cleared lasts the same with and without the moments", on.ClearTicks == off.ClearTicks && on.ClearTicks == 180, $"{on.ClearTicks} vs {off.ClearTicks} ticks");
                int dv = Diverge(on.Hashes, off.Hashes);
                Check($"{tag}: combat tick results identical with and without the moments", dv < 0, dv < 0 ? $"{MomentTicks} ticks" : $"first differs at tick {dv}");
                Check($"{tag}: the card names the role and the area, and the title", on.Role.Contains("·") && !string.IsNullOrEmpty(on.Epithet), $"'{on.Role}' / '{on.Epithet}'");
                Check($"{tag}: the boss shows an entrance clip", !string.IsNullOrEmpty(on.Entrance) && on.Entrance != "-", on.Entrance);
                Check($"{tag}: the camera frames the boss in the intro and is back on the player before combat", on.FramedIntro && on.FreeAtCombat, $"framed {on.FramedIntro}, free at combat {on.FreeAtCombat}");
                Check($"{tag}: the kill freezes, then slows the boss, then plays on", on.Froze && on.Slowed && on.Recovered, $"froze {on.Froze}, slowed {on.Slowed}, back to 1 {on.Recovered}");
                Check($"{tag}: the Defeat clip plays and holds to the end of Cleared", on.DefeatClip != null && (on.DefeatClip.Contains("Defeat") || on.DefeatClip == "Vulnerable"), on.DefeatClip ?? "none");
                Check($"{tag}: the gold flash and the fight time show", on.Flashed && on.FightTime.Contains(":"), $"flash {on.Flashed}, time '{on.FightTime}'");
                Check($"{tag}: without the moments: no flash, no slow motion, no framing", !off.Flashed && !off.Slowed && !off.FramedIntro);
                Check($"{tag}: the camera framing is handed back after Cleared", on.FrameReleased);
                if (counter) Check($"{tag}: nothing in the moments allocates per frame", on.Alloc == 0 && on.Frames > 0, $"{on.Alloc} bytes over {on.Frames} frames");
            }
            PresentationSettings.MomentsTestOff = false;
            yield return PhaseToMenu();
            d.SelectAllies(0);
            Note($"moments suite took {Time.realtimeSinceStartup - t0:0} s");
        }

        sealed class MomentPass
        {
            public int IntroTicks, ClearTicks, Frames; public long Alloc; public ulong[] Hashes = new ulong[MomentTicks];
            public string Entrance = "", Role = "", Epithet = "", DefeatClip, FightTime = "";
            public bool FramedIntro, FreeAtCombat, Froze, Slowed, Recovered, Flashed, FrameReleased;
        }

        // A practice of boss e with a party of four Perfect bots: the intro and Cleared run on the director's own clock (that is
        // what must not change), the first MomentTicks of combat are stepped by hand with a fixed seed and hashed, then the
        // boss is defeated through the normal death path.
        IEnumerator MomentRun(int e, string cls, bool on, MomentPass r, bool measure, string shots)
        {
            PresentationSettings.MomentsTestOff = !on;
            yield return PhaseToMenu();
            d.SelectAllies(RunParty.MaxAllies);
            if (!d.StartPractice(cls, e)) { Check("moments: practice accepted " + e, false); yield break; }
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready && RunDirector.Instance.Encounter == e, 30, "moments ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            var me = new AllyBot(RunParty.MaxAllies, RunParty.MaxAllies + 1, BotMode.Perfect);
            d.InputOverride = me; if (d.Player) d.Player.Input = me;
            bool attacks = AllyBot.Attacks; AllyBot.Attacks = true;
            Func<Vector3?> before = RunDirector.FrameAlso;
            var at = new System.Collections.Generic.Dictionary<RunState, int>(); var dir = d;
            Action<RunState> mark = s => at[s] = dir.World != null ? dir.World.Tick : -1; d.StateChanged += mark;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Intro, 5, "moments intro");
            bool shot = false;
            while (d.State == RunState.Intro)
            {
                if (RunDirector.FrameAlso != null && RunDirector.FrameAlso != before) r.FramedIntro = true;
                if (shots != null && !shot && d.IntroProgress >= 0.45f) { shot = true; yield return Shot("moments_" + shots + "_intro"); }
                yield return null;
            }
            r.IntroTicks = at.TryGetValue(RunState.Combat, out int ct) && at.TryGetValue(RunState.Intro, out int it) ? ct - it : -1;
            r.FreeAtCombat = RunDirector.FrameAlso == before;
            r.Entrance = BossMoments.EntranceClip; r.Role = BossMoments.RoleLine(d); r.Epithet = BossMoments.Epithet(d.Boss) ?? "";
            if (d.State != RunState.Combat) { AllyBot.Attacks = attacks; d.StateChanged -= mark; yield break; }

            d.SetPaused(true); UnityEngine.Random.InitState(4242); var w = d.World;
            for (int t = 0; t < MomentTicks; t++)
            {
                if (d.Player && !d.Player.Down && d.Player.Hp < 40) d.Player.DebugSetHp(d.Player.MaxHealth);
                w.Step(); r.Hashes[t] = WorldHash(w, d.Boss);
                if ((t & 1) == 1) yield return null;
            }
            d.SetPaused(false);
            // the kill: the normal death path (a miniboss-style hold is lifted by the last phase first)
            var boss = d.Boss; var pres = boss ? boss.GetComponentInChildren<CharacterPresenter>() : null;
            if (boss && boss.PhaseNumber < boss.PhaseCount) boss.StartAtPhase(boss.PhaseCount);
            if (boss) boss.DebugDefeat();
            yield return Until(() => d.State == RunState.Cleared, 3, "moments cleared");
            bool shotFlash = false, shotCard = false;
            if (measure) { BossMoments.AllocBytes = 0; BossMoments.MeasuredFrames = 0; }
            int frame = 0;
            while (d.State == RunState.Cleared)
            {
                frame++;
                if (measure && frame == 2) BossMoments.Measuring = true;
                if (pres)
                {
                    if (pres.TimeScale == 0) r.Froze = true;
                    if (pres.TimeScale > 0 && pres.TimeScale < 0.5f) r.Slowed = true;
                    if (r.Slowed && pres.TimeScale == 1) r.Recovered = true;
                    r.DefeatClip = pres.CurrentClip;
                }
                if (BossMoments.Flash > 0.05f) r.Flashed = true;
                if (shots != null && !shotFlash && d.StateTicks >= 8) { shotFlash = true; BossMoments.Measuring = false; yield return Shot("moments_" + shots + "_kill"); BossMoments.Measuring = measure; }
                if (shots != null && !shotCard && d.StateTicks >= 100) { shotCard = true; BossMoments.Measuring = false; yield return Shot("moments_" + shots + "_defeated"); BossMoments.Measuring = measure; }
                yield return null;
            }
            BossMoments.Measuring = false; r.Alloc = BossMoments.AllocBytes; r.Frames = BossMoments.MeasuredFrames;
            r.ClearTicks = at.TryGetValue(RunState.Results, out int rt) && at.TryGetValue(RunState.Cleared, out int cl) ? rt - cl : -1;
            d.StateChanged -= mark;
            r.FightTime = BossMoments.FightTime ?? "";
            r.FrameReleased = RunDirector.FrameAlso == before || RunDirector.FrameAlso == null;
            AllyBot.Attacks = attacks; d.InputOverride = bot; if (d.Player) d.Player.Input = bot;
            yield return PhaseToMenu();
        }
    }
}
