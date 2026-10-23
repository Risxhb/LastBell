using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LastBell.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastBell.Menu
{
    // Explicit opt-in player checks using real countdown ticks and live audio sources.
    public sealed class MenuMusicTransitionTest : MonoBehaviour
    {
        readonly List<string> report = new List<string>(), errors = new List<string>();
        readonly List<string> trace = new List<string> { "fight,tick,progress,incoming,outgoing,menu" };
        int failures;
        string output;
        MenuMusic music;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!Environment.GetCommandLineArgs().Contains("-lastbellMusicTransitionTest")) return;
            Application.runInBackground = true;
            var root = new GameObject("Boss music transition validation");
            DontDestroyOnLoad(root); root.AddComponent<MenuMusicTransitionTest>();
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-lastbellOut");
            output = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.persistentDataPath + "/music-transition-test";
            Directory.CreateDirectory(output); Application.logMessageReceived += Log;
            bool hadVolume = PlayerPrefs.HasKey("LastBell.Menu.MusicVolume");
            float savedVolume = MenuMusic.Ensure().Volume;
            yield return Until(() => FindAnyObjectByType<ConfrontationMenu>()?.StartButton || FindAnyObjectByType<MainMenu>()?.StartButton, 45, "initial menu");
            music = MenuMusic.Ensure(); music.SetVolume(.5f); music.ResumeTrack();
            yield return new WaitForSecondsRealtime(1);
            var menu = music.MenuSource;
            Check("menu starts a supported track", IsMenuTrack(menu) && menu.isPlaying);
            for (int i = 0; i < 3; i++)
            {
                music.NextTrack(); yield return new WaitForSecondsRealtime(.15f);
                Check("playlist track plays " + i, IsMenuTrack(menu) && menu.isPlaying);
            }
            int menuSample = menu.timeSamples;
            Check("launch run", MenuLaunch.Normal("Vanguard"));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 45, "run preparation");
            var d = RunDirector.Instance;
            if (!d) { Finish(hadVolume, savedVolume); yield break; }
            Check("initial preparation retains menu playback", music.Source == menu && menu.isPlaying && menu.timeSamples > menuSample);
            var order = (int[])d.EncounterOrder.Clone();
            d.RequestReady();
            for (int fight = 0; fight < order.Length; fight++)
            {
                yield return Until(() => d.State == RunState.Intro, 8, "countdown " + fight);
                var incoming = music.Source;
                var outgoing = music.GetComponents<AudioSource>().FirstOrDefault(s => s != menu && s != incoming && s.isPlaying);
                Check("correct incoming boss track " + fight, incoming != menu && incoming.clip && incoming.clip.name == MenuMusic.TrackFor(d.Boss) && incoming.isPlaying);
                if (fight > 0) Check("previous boss still plays at next countdown " + fight, outgoing && outgoing.volume > .4f);
                yield return Until(() => d.StateTicks >= 28, 3, "partial fade " + fight);
                yield return new WaitForEndOfFrame();
                Check("partial fade follows countdown " + fight, Close(incoming.volume, music.Volume * d.IntroProgress) && incoming.volume > .05f && incoming.volume < music.Volume - .05f);
                if (fight == 0)
                {
                    Check("menu fades while still playing", menu.isPlaying && Close(menu.volume, music.Volume * (1 - d.IntroProgress)));
                    d.SetPaused(true); int tick = d.StateTicks, sample = incoming.timeSamples; float gain = incoming.volume;
                    yield return new WaitForSecondsRealtime(.35f);
                    Check("paused countdown freezes fade but music continues", d.StateTicks == tick && Close(incoming.volume, gain) && incoming.timeSamples > sample);
                    music.SetVolume(0);
                    Check("mute covers all fade sources", music.GetComponents<AudioSource>().All(s => s.volume == 0));
                    music.SetVolume(.25f);
                    Check("volume change retains fade proportions", Close(incoming.volume, .25f * d.IntroProgress) && Close(menu.volume, .25f * (1 - d.IntroProgress)));
                    music.SetVolume(.5f); d.SetPaused(false);
                }
                bool followsTicks = true, conserved = true;
                while (d.State == RunState.Intro)
                {
                    yield return new WaitForEndOfFrame();
                    if (d.State != RunState.Intro) break;
                    followsTicks &= Close(incoming.volume, music.Volume * d.IntroProgress);
                    conserved &= Close(music.GetComponents<AudioSource>().Sum(s => s.volume), music.Volume);
                    trace.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F4},{3:F4},{4:F4},{5:F4}", fight, d.StateTicks, d.IntroProgress, incoming.volume, outgoing ? outgoing.volume : 0, menu.volume));
                }
                Check("fade tracks every sampled countdown tick " + fight, followsTicks && conserved);
                Check("full boss volume exactly at combat entry " + fight, d.State == RunState.Combat && incoming.isPlaying && Close(incoming.volume, music.Volume));
                Check("outgoing sources silent at zero " + fight, !menu.isPlaying && (!outgoing || !outgoing.isPlaying) && music.GetComponents<AudioSource>().Where(s => s != incoming).All(s => s.volume == 0));
                int combatSample = incoming.timeSamples;
                d.Boss.DebugDefeat();
                yield return Until(() => d.State == RunState.Cleared, 3, "boss clear " + fight);
                yield return new WaitForSecondsRealtime(.35f);
                Check("boss track continues through clear " + fight, music.Source == incoming && incoming.isPlaying && incoming.timeSamples > combatSample && Close(incoming.volume, music.Volume));
                if (fight < order.Length - 1)
                    yield return Until(() => d.State == RunState.Intro, 5, "next boss countdown " + fight);
            }
            yield return Until(() => d.State == RunState.Results, 5, "victory results");
            var last = music.Source; int resultSample = last.timeSamples;
            yield return new WaitForSecondsRealtime(.3f);
            Check("victory results retain final boss track", d.Victory && last != menu && last.isPlaying && last.timeSamples > resultSample);
            Check("retry accepted", d.RequestRetry());
            yield return new WaitForSecondsRealtime(.35f);
            Check("retry preparation retains previous boss", d.State == RunState.Ready && music.Source == last && last.isPlaying && Close(last.volume, music.Volume));
            d.RequestReady(); yield return Until(() => d.StateTicks >= 25, 3, "retry countdown");
            Check("retry crossfades from previous boss", music.Source != last && last.isPlaying && music.Source.isPlaying && music.Source.volume > 0 && last.volume > 0);
            int previousIndex = music.TrackIndex;
            d.SetPaused(true); Check("return during countdown accepted", MenuReturnBridge.Return(false));
            yield return Until(() => !RunDirector.Instance && (FindAnyObjectByType<ConfrontationMenu>()?.StartButton || FindAnyObjectByType<MainMenu>()?.StartButton), 45, "main menu return");
            yield return new WaitForSecondsRealtime(.7f);
            Check("main menu starts next playlist track", music.Source == menu && IsMenuTrack(menu) && menu.isPlaying && music.TrackIndex == (previousIndex + 1) % 3 && menu.time < 8 && Close(menu.volume, music.Volume));
            Check("menu return stops both boss sources", music.GetComponents<AudioSource>().Where(s => s != menu).All(s => !s.isPlaying && s.volume == 0));
            Check("single persistent owner and three reusable sources", FindObjectsByType<MenuMusic>().Length == 1 && music.GetComponents<AudioSource>().Length == 3);
            // Return to gameplay once more to check defeat and a same-boss retry.
            Check("launch defeat check", MenuLaunch.Normal("Vanguard"));
            yield return Until(() => RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 45, "defeat preparation");
            d = RunDirector.Instance;
            d.RequestReady(); yield return Until(() => d.State == RunState.Combat, 4, "defeat combat");
            last = music.Source; d.Player.ReceiveHit(new Hazard { Source = "Music validation", Kind = HazardKind.Ground, Damage = 999999 });
            yield return Until(() => d.State == RunState.Downed, 3, "downed");
            Check("downed retains boss track", music.Source == last && last.isPlaying && Close(last.volume, music.Volume));
            yield return Until(() => d.State == RunState.Results, 4, "defeat results");
            Check("defeat results retain boss track", !d.Victory && music.Source == last && last.isPlaying);
            d.RequestRetry(); yield return new WaitForSecondsRealtime(.2f);
            Check("defeat retry retains boss track", music.Source == last && last.isPlaying);
            d.RequestReady(); yield return new WaitForEndOfFrame();
            Check("same boss retry begins silent incoming track", music.Source != last && music.Source.clip == last.clip && music.Source.isPlaying && last.isPlaying && music.Source.volume < last.volume);
            yield return Until(() => d.StateTicks >= 25, 3, "interrupted retry countdown");
            d.SetPaused(true); Check("retry during countdown accepted", d.RequestRetry());
            last = music.Source; yield return new WaitForSecondsRealtime(.6f);
            Check("interrupted countdown settles on boss music in preparation", d.State == RunState.Ready && music.Source == last && last.isPlaying && Close(last.volume, music.Volume) && !menu.isPlaying);
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 4, "retry combat");
            d.SetPaused(true); MenuReturnBridge.Return(false);
            yield return Until(() => !RunDirector.Instance, 45, "final return");
            yield return new WaitForSecondsRealtime(.6f);
            music.PauseTrack(); yield return new WaitForSecondsRealtime(.15f);
            Check("manual menu pause remains available", music.UserPaused && !menu.isPlaying);
            music.NextTrack(); Check("manual next preserves pause", music.UserPaused && !menu.isPlaying);
            music.ResumeTrack(); yield return new WaitForSecondsRealtime(.15f);
            Check("manual menu resume remains available", !music.UserPaused && menu.isPlaying);
            Check("zero runtime errors", errors.Count == 0);
            Finish(hadVolume, savedVolume);
        }
        static bool IsMenuTrack(AudioSource source) => source.clip && new[] { "MainMenu2", "MainMenuOpening", "MainMenuMusic" }.Contains(source.clip.name);
        static bool Close(float a, float b) => Mathf.Abs(a - b) < .015f;
        IEnumerator Until(Func<bool> ready, float seconds, string name)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ready() && Time.realtimeSinceStartup < end) yield return null;
            Check(name, ready());
        }
        void Check(string name, bool pass) { report.Add((pass ? "PASS " : "FAIL ") + name); if (!pass) failures++; Debug.Log(report.Last()); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
        void Finish(bool hadVolume, float volume)
        {
            if (music) music.SetVolume(volume);
            if (!hadVolume) PlayerPrefs.DeleteKey("LastBell.Menu.MusicVolume");
            PlayerPrefs.Save(); report.AddRange(errors);
            report.Add("RESULT " + report.Count(s => s.StartsWith("PASS ")) + " passed / " + failures + " failed");
            File.WriteAllLines(Path.Combine(output, "report.txt"), report); File.WriteAllLines(Path.Combine(output, "fade-trace.csv"), trace);
            Application.logMessageReceived -= Log;
            Application.Quit(failures == 0 ? 0 : 1);
        }
    }
}
