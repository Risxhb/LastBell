using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // In-run HUD and party cards check (UI sub-thread C, docs/plans/ui-uniformity-plan-20261110.md). Launch a practice fight
    // with bot allies and this flag:
    //   LastBell.exe -lastbellPractice tempo -lastbellClass Arcanist -lastbellAllies 3 -lastbellHudTest -lastbellOut <dir>
    // It checks that every ally has a card (class, health), then hurts one ally, downs it and waits for the bots to revive it
    // (a real revive; if none starts in 15 s the revive channel is driven by the test and that is noted), and captures each
    // state, plus the theme banner and demo caption. The local player and the other allies are kept topped up so the fight
    // lasts; the boss is not touched. Writes hud-report.txt; exits 0 when every check passed.
    public sealed class HudPartyCardsTest : MonoBehaviour
    {
        static bool Arg(string a) => Environment.GetCommandLineArgs().Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
        static string OutDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellOut", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "hud-test");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Arg("-lastbellHudTest") || FindAnyObjectByType<HudPartyCardsTest>()) return;
            var go = new GameObject("HUD party cards test"); DontDestroyOnLoad(go); go.AddComponent<HudPartyCardsTest>();
        }

        readonly List<string> lines = new List<string>();
        int pass, fail, errors, shots; string dir, res;
        RunDirector d; PlayerController target; bool topUp;

        void Start()
        {
            dir = OutDir; Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            Application.runInBackground = true;
            StartCoroutine(Main());
        }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t) { if (t == LogType.Exception || t == LogType.Error) { errors++; if (errors < 30) lines.Add("      LOG " + t + ": " + msg); } }
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            var line = $"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  — " + detail)}";
            lines.Add(line); Debug.Log("[hud-test] " + line);
        }
        void Note(string s) { lines.Add("      " + s); Debug.Log("[hud-test] " + s); }
        IEnumerator Shot(string name)
        {
            if (Application.isBatchMode) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{++shots:00}_{name}_{res}.png"), tex.EncodeToPNG()); Destroy(tex);
        }
        IEnumerator Wait(float s) { float t = 0; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Until(Func<bool> cond, float seconds, string what, bool required = true)
        {
            float t = 0;
            while (t < seconds) { d = RunDirector.Instance; if (d && cond()) yield break; t += Time.unscaledDeltaTime; yield return null; }
            if (required) Check("in time: " + what, false, $"{seconds}s, state {(d ? d.State.ToString() : "no director")}");
        }
        string CardOf(PlayerController p)
        {
            int i = RunPartyCards.Allies(d).IndexOf(p);
            return i >= 0 && i < RunPartyCards.Shown.Count ? RunPartyCards.Shown[i] : "";
        }

        // keep the fight going: the local player and every ally but the test's target stay at full health
        void Update()
        {
            if (!topUp || !d || d.World == null) return;
            foreach (var p in d.World.Players)
                if (p != null && p != target && !p.Down && p.Hp < p.MaxHealth) p.DebugSetHp(p.MaxHealth);
        }

        IEnumerator Main()
        {
            res = $"{Screen.width}x{Screen.height}";
            yield return Wait(1f);
            Note("resolution " + res);
            // -lastbellOut turns off the practice auto-start (RunDirector.AutoReady): the test readies itself
            yield return Until(() => (d.State == RunState.Ready || d.State == RunState.Combat) && d.World != null, 60, "practice arena");
            if (d && d.State == RunState.Ready) { yield return Wait(0.5f); d.RequestReady(); }
            yield return Until(() => d.State == RunState.Combat && d.Boss && d.World != null, 30, "practice combat");
            if (!d || d.State != RunState.Combat) { Finish(); yield break; }
            topUp = true;
            yield return Wait(4f);   // the bots spread out and the class state fills

            var allies = RunPartyCards.Allies(d).ToList();
            Check("practice has bot allies (-lastbellAllies)", allies.Count >= 1, allies.Count + " allies");
            if (allies.Count == 0) { Finish(); yield break; }
            yield return null; yield return null;
            Check("a party card per ally", RunPartyCards.Shown.Count == allies.Count, string.Join("; ", RunPartyCards.Shown));
            Check("cards name each ally's class", allies.All(a => CardOf(a).Contains("|" + a.ClassName + "|")));
            Check("no card for the local player", !allies.Contains(d.Player));
            yield return Shot("party-standing");

            // hurt
            target = allies[0];
            target.DebugSetHp(Mathf.RoundToInt(target.MaxHealth * 0.22f));
            yield return Wait(0.15f);
            Check("card shows the hurt ally's health", CardOf(target).Contains("|" + target.Hp + "/" + target.MaxHealth + "|"), CardOf(target));
            yield return Shot("party-hurt");

            // down
            target.ForceDown("HUD test", null);
            yield return Wait(0.6f);
            Check("card shows the downed ally as DOWN", target.Down && CardOf(target).Contains("|DOWN"), CardOf(target));
            yield return Shot("party-down");

            // revive: the bots channel it themselves; if no revive starts in 15 s, the test drives the channel
            bool natural = true;
            yield return Until(() => target.ReviveProgress > 0.05f || !target.Down, 15, "a bot starts the revive", false);
            if (target.Down && target.ReviveProgress <= 0.05f)
            {
                natural = false; Note("no bot started the revive in 15 s: the test drives the revive channel (ReviveProgress), then Revive()");
                for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime) { target.ReviveProgress = 0.45f; yield return null; }
            }
            if (target.Down)
            {
                if (!natural) target.ReviveProgress = 0.45f;
                yield return null; yield return null;
                Check("card shows the revive in progress (REVIVING)", CardOf(target).Contains("|REVIVING"), CardOf(target));
                yield return Shot("party-reviving");
            }
            if (natural) yield return Until(() => !target.Down, 10, "the bots finish the revive");
            if (target.Down && !target.Dead) target.Revive();
            yield return null; yield return null;
            Note(natural ? "revived by a bot" : "revived by the test");
            Check("card shows the ally back up (REVIVED)", !target.Down && CardOf(target).Contains("|REVIVED"), CardOf(target));
            yield return Shot("party-revived");
            yield return Wait(2.3f);
            Check("REVIVED clears after 2 s: health again", CardOf(target).Contains("|" + target.Hp + "|") || CardOf(target).EndsWith("|" + target.Hp), CardOf(target));
            target = null;

            // banner and demo caption on the theme
            RunHud.Banner("ENRAGE", UiTheme.Danger, 3);
            RunHud.CaptionTitle = "Party cards"; RunHud.CaptionBody = "Bots and LAN allies: class, health, and the down and revive state, bottom left.";
            yield return Wait(0.3f);
            Check("banner is showing", RunHud.CurrentBanner == "ENRAGE");
            yield return Shot("banner-caption");
            RunHud.CaptionTitle = null; RunHud.CaptionBody = null;

            // key hints follow InputHints; gameplay hints stay keyboard/mouse even after a gamepad press
            InputHints.Force(InputHints.Device.Gamepad);
            Check("gameplay hints stay keyboard on a gamepad (InputHints)", InputHints.Slot(0) == "LMB" && InputHints.Key(HintAction.Potion) == "F" && InputHints.Key(HintAction.Dash) == "RMB",
                  $"{InputHints.Slot(0)} {InputHints.Slot(1)} {InputHints.Key(HintAction.Dash)} {InputHints.Key(HintAction.Potion)}");
            InputHints.Force(null);
            Finish();
        }

        void Finish()
        {
            topUp = false;
            Check("no errors or exceptions in the log", errors == 0, errors + " logged");
            lines.Insert(0, $"RESULT {pass} passed, {fail} failed ({res})");
            File.WriteAllLines(Path.Combine(dir, "hud-report.txt"), lines);
            Debug.Log("[hud-test] " + lines[0]);
            Application.Quit(fail == 0 ? 0 : 1);
        }
    }
}
