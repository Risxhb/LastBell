using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // -lastbellAurelionDemo (with -lastbellAurelionOnly): a watchable v4 fight at normal speed. The player and three
    // allies are all AllyBot Perfect (they know the Ecliptic order and their marks, take their towers, wells and sides)
    // and attack, on Hard. A caption names and explains whatever he is doing. Use -lastbellBossHp to keep it short.
    // -lastbellDemoRecord also saves the fight as 30 fps JPEG frames (frames_aurelion_v4_demo/) for an MP4. The window
    // stays up 5 s after the fight ends.
    public sealed partial class RunAutoTest
    {
        static bool AurelionDemo => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellAurelionDemo", StringComparison.OrdinalIgnoreCase));
        static bool DemoRecord => Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellDemoRecord", StringComparison.OrdinalIgnoreCase));

        // one line per ability: what it does and what to do
        static readonly Dictionary<string, string> DemoLines = new Dictionary<string, string>
        {
            ["Meridian Lances"] = "Beams through the whole arena, aimed at a player, then they lock: step sideways out of the beam.",
            ["Cascading Lances"] = "Two volleys of beams, the second turned 22.5 degrees: clear the first, then the second.",
            ["Starfall Mirror"] = "A star follows each player, with a mirror copy on the far side: step off your star, avoid every mirror.",
            ["Meridian Towers"] = "Stand in each numbered tower, in order, when it lands. An EMPTY TOWER WIPES THE PARTY.",
            ["Ecliptic Order"] = "Memorise the numbered rings before they go dark: the floor is struck everywhere except ring I, then II, then III.",
            ["Zenith Collapse"] = "Everything except a disc at his feet erupts: get close. The disc shrinks every cast (Solar Hunger).",
            ["Sunspots"] = "Each sigil blows up where its carrier stands: drop it at the edge and walk away. The pools burn until the phase ends.",
            ["Zenith Marks"] = "SUN over your head = be far (beyond 8.5 m). SHADE = be close (within 5.5 m). Two-step marks flip.",
            ["Total Eclipse"] = "Stay inside the moving blue light wells and break his Corona Shield within 25 s, or the party wipes.",
            ["Orrery Closing"] = "The rim and a crown round him close: stand in the band between. The rim then stays lit (the Horizon).",
            ["Hour Hands"] = "Hard only: a clock hand sweeps out for each player: step off yours.",
            ["Severance"] = "Day and Night: never cross the turning gold line, nor let it sweep over you. Keep a player on each side.",
            ["Reunion"] = "Everyone into the centre tower.",
            ["Last Light"] = "A slice of the floor burns for good every 15 s. Kill him before the eighth, or Final Noon.",
        };

        static string DemoBody(string block)
        {
            var parts = block.Replace("Horizon", "Orrery Closing").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim()).Distinct().Where(p => DemoLines.ContainsKey(p)).Select(p => DemoLines[p]);
            return string.Join("  ", parts);
        }

        void DemoCaption(AurelionEncounter b)
        {
            string block = b.BlockLog.Count > 0 ? b.BlockLog[b.BlockLog.Count - 1].block : "";
            if (b.Current == "phase shift") { RunHud.CaptionTitle = "Phase change: " + b.PhaseName; RunHud.CaptionBody = "A harmless transformation. The next phase adds more at once."; return; }
            if (block == "punish" || b.Current == "open chest") { RunHud.CaptionTitle = "PUNISH WINDOW"; RunHud.CaptionBody = "His chest is open and nothing is coming for 3.5 s: full damage now, drink a potion if you need one."; return; }
            if (block == "Last Light")
            {
                string now = DemoLines.Keys.FirstOrDefault(k => b.Current.StartsWith(k)) ?? "";
                bool extra = now.Length > 0 && now != "Last Light";
                RunHud.CaptionTitle = "Last Light" + (extra ? " + " + now : "");
                RunHud.CaptionBody = DemoLines["Last Light"] + (extra ? "  " + DemoLines[now] : "");
                return;
            }
            if (b.Marks.CueVisible && !block.Contains("Zenith Marks")) block += " + Zenith Marks";
            RunHud.CaptionTitle = block.Length > 0 ? block : b.PhaseName;
            RunHud.CaptionBody = DemoBody(block);
        }

        bool demoRecording;
        IEnumerator DemoRecorder(string name)
        {
            var cdir = Path.Combine(dir, "frames_" + name); Directory.CreateDirectory(cdir);
            Time.captureFramerate = 30; int i = 0;
            while (demoRecording)
            {
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(cdir, $"{i++:00000}.jpg"), tex.EncodeToJPG(85)); Destroy(tex);
            }
            Time.captureFramerate = 0;
            Note($"video frames {name}: {i} (30 fps)");
        }

        IEnumerator AurelionDemoFight()
        {
            Note("---------- Aurelion v4 demo: 4 perfect bots, Hard ----------");
            AurelionArgs.SetOverride = null; AurelionArgs.SoloOverride = null;
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            // -lastbellDemoClass, -lastbellDemoLevel, -lastbellDemoAllies: the solo reference runs (Codex proposal 1) use the same fight
            string cls = AurelionArgs.Arg("-lastbellDemoClass") ?? "Arcanist";
            var level = Enum.TryParse(AurelionArgs.Arg("-lastbellDemoLevel") ?? "Hard", true, out Difficulty lv) ? lv : Difficulty.Hard;
            int allies = int.TryParse(AurelionArgs.Arg("-lastbellDemoAllies") ?? "3", out int na) ? Mathf.Clamp(na, 0, 7) : 3;
            d.SelectDifficulty(level);
            d.SelectAllies(allies);
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = true;
            var me = allies == 0 ? new AllyBot(0, 1, BotMode.Perfect) : new AllyBot(allies, allies + 1, BotMode.Perfect);
            d.InputOverride = me;
            d.StartPractice(cls, RunDirector.AurelionPractice);
            int spire = RunMaps.IndexOf(RunDirector.AurelionMap);
            yield return Until(() => RunMaps.Current == spire && RunDirector.Instance && RunDirector.Instance.State == RunState.Ready, 30, "Skyward Spire practice ready");
            if (RunDirector.Instance != d) { directorLog.AddRange(d.Log); d = RunDirector.Instance; }
            d.InputOverride = me; if (d.Player) d.Player.Input = me;
            d.RequestReady();
            yield return Until(() => d.State == RunState.Combat, 15, "combat");
            var b = d.Boss as AurelionEncounter;
            if (b == null) yield break;
            Note($"demo: party of {d.World.Players.Count} ({string.Join(", ", d.World.Players.Select(p => p.ClassName))}), boss HP {b.MaxHp}");
            if (DemoRecord) { demoRecording = true; StartCoroutine(DemoRecorder("aurelion_v4_demo")); }
            d.World.PlayerHit += (p, h, dmg) =>
            {
                if (p.Hp > 0 || b.WipeCause != null) return;
                string blk = b.BlockLog.Count > 0 ? b.BlockLog[b.BlockLog.Count - 1].block : "?";
                Note($"demo death @{b.FightTicks / (float)Sim.Hz:0.0}s: {p.ClassName} by {h.Source} {dmg} in [{blk}], potions left {p.Potions} | {(p.Input as AllyBot)?.DebugLine}");
            };
            // the two damage windows (Codex proposal 1): Corona Shield (25 s) and Last Light (88 s)
            int eclT0 = -1, eclSec = -1, eclMax = 0, eclLeft = 0, p4T0 = -1, p4Hp0 = 0; bool eclDone = false;
            bool fast = Environment.GetCommandLineArgs().Any(a => a.Equals("-lastbellDemoFast", StringComparison.OrdinalIgnoreCase)) && !DemoRecord;
            if (fast) Time.timeScale = 4;   // measurement runs (no recording) at 4x
            string last = ""; float t = 0;
            while (t < 480 && d.State == RunState.Combat)
            {
                DemoCaption(b);
                if (!eclDone && b.Eclipse.Active) eclLeft = b.ShieldHp;
                if (!eclDone && b.Eclipse.Active && eclT0 < 0) { eclT0 = d.World.Tick; eclMax = b.ShieldMax; }
                if (!eclDone && eclT0 >= 0 && b.Eclipse.Active && (d.World.Tick - eclT0) / Sim.Hz > eclSec && (eclSec = (d.World.Tick - eclT0) / Sim.Hz) >= 0)
                    foreach (var p in d.World.Players)
                        Note($"demo corona @{(d.World.Tick - eclT0) / (float)Sim.Hz:0}s: shield {b.ShieldHp} | {p.ClassName} hp {p.Hp} inwell {b.Eclipse.InWell(p.transform.position)} at {Sim.Flat(p.transform.position)} | {(p.Input as AllyBot)?.DebugLine}");
                if (!eclDone && eclT0 >= 0 && !b.Eclipse.Active) { eclDone = true; Note($"demo window Corona: {eclMax - b.ShieldHp} of {eclMax} in {(d.World.Tick - eclT0) / (float)Sim.Hz:0.0} s (limit 25 s), {(b.Eclipse.Broken ? "broken" : "held")}"); }
                if (p4T0 < 0 && b.Phase >= 4) { p4T0 = d.World.Tick; p4Hp0 = b.Hp; }
                if (b.Towers.Towers.Exists(tw => tw.ResolveTick >= d.World.Tick) && d.World.Tick % 15 == 0)
                    foreach (var p in d.World.Players)
                    {
                        var tw = b.Towers.NextFor(p, d.World.Tick);
                        Note($"towers @{b.FightTicks / (float)Sim.Hz:0.0}s {p.ClassName}: tower {(tw == null ? "-" : tw.Index + " at " + Sim.Flat(tw.At))} | {(p.Input as AllyBot)?.DebugLine}");
                    }
                if (b.PhaseName != last) { last = b.PhaseName; Note($"demo @{b.FightTicks / (float)Sim.Hz:0.0}s: {last}"); }
                t += Time.deltaTime; yield return null;
            }
            if (!eclDone && eclT0 >= 0) Note($"demo window Corona: {eclMax - eclLeft} of {eclMax} in {(d.World.Tick - eclT0) / (float)Sim.Hz:0.0} s (limit 25 s), held");
            if (p4T0 >= 0) Note($"demo window Last Light: {p4Hp0 - b.Hp} of {p4Hp0} in {(d.World.Tick - p4T0) / (float)Sim.Hz:0.0} s (floor deadline 88 s), {b.Light.Fallen} slices fallen");
            Note($"demo damage dealt: {d.World.DamageDealt} by the party; player {d.Player?.ClassName}");
            Time.timeScale = 1;
            int secs = b.FightTicks / Sim.Hz;
            RunHud.CaptionTitle = b.Dead ? "AURELION DEFEATED" : "THE PARTY FELL";
            RunHud.CaptionBody = $"{secs / 60}:{secs % 60:00}  ·  {d.World.Players.Count(p => !p.Down)} of {d.World.Players.Count} standing";
            Note($"demo end @{b.FightTicks / (float)Sim.Hz:0.0}s: {(b.Dead ? "boss defeated" : "party down: " + (b.WipeCause ?? d.World.LastDamageSource) + $", boss at {b.Hp} of {b.MaxHp}")}; standing {d.World.Players.Count(p => !p.Down)} of {d.World.Players.Count}");
            if (b.Telemetry) foreach (var s in b.Telemetry.Summary()) Note("demo telemetry: " + s);
            foreach (var g in b.Telemetry.Rows.Where(r => r.Kind == "hit").GroupBy(r => r.Player)) Note($"demo hits {g.Key}: " + string.Join(", ", g.GroupBy(r => r.Source).Select(s => $"{s.Key} {s.Count()}x{s.Sum(r => r.Value):0}")));
            Check("demo: the fight ran to an end", d.State != RunState.Combat, d.State.ToString());
            t = 0; while (t < 5) { t += Time.deltaTime; yield return null; }
            demoRecording = false; yield return null; yield return null;
            RunHud.CaptionTitle = null;
            AllyBot.AllyMode = BotMode.Perfect; AllyBot.Attacks = false;   // back to the menu defaults
            d.SelectAllies(0);
            d.InputOverride = bot;
        }
    }
}
