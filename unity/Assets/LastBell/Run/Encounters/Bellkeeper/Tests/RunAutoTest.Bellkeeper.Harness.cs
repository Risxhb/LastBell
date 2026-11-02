using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // The Bellkeeper Ultimate test harness shared by its check files (RunAutoTest.Bellkeeper.Ultimate.cs and Ult.*.cs):
    // -lastbellTestAbility <key> narrows the Ultimate run to one part. BkStep / BkStepUntil advance the world in exact combat
    // ticks, BkAdd adds scripted extras, BkPlace moves a player, and every hit is recorded in bkHits. (The normal Bellkeeper
    // v2 checks that used to live here were deleted with the v2 set, Chzy18 1 Nov.)
    public sealed partial class RunAutoTest
    {
        static string BkFilter
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.FindIndex(args, a => a.Equals("-lastbellTestAbility", StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? BellkeeperAbilities.Normalize(args[i + 1]) : null;
            }
        }

        BellkeeperEncounter bk;
        public struct BkHit { public int Tick; public PlayerController Player; public string Source; public int HazardId; public int Damage; }
        readonly List<BkHit> bkHits = new List<BkHit>();

        // ---------------- harness ----------------

        IEnumerator BkAdopt(int map, RunState state)
        {
            yield return Until(() => RunMaps.Current == map && RunDirector.Instance && RunDirector.Instance.State == state, 30, "map loaded: " + RunMaps.All[map].Name);
            if (RunDirector.Instance != d) { d = RunDirector.Instance; d.InputOverride = bot; bot.Clear(); }
            if (d.Player) d.Player.Input = bot;   // the new scene built the player before the bot was attached
        }
        // Back to the menu with the practice override cleared.
        IEnumerator BkLeave()
        {
            if (d.State != RunState.Menu) { d.SetPaused(true); d.ReturnToMenu(); yield return null; }
            BellkeeperEncounter.AbilityOverride = null;
            d.SelectDifficulty(Difficulty.Medium);
            bk = null; bot.Clear();
        }

        void BkStep(int ticks) { for (int i = 0; i < ticks; i++) d.World.Step(); }
        // steps until cond holds (checked before each tick); returns the ticks taken, or -1 if it never held
        int BkStepUntil(Func<bool> cond, int maxTicks)
        {
            for (int i = 0; i <= maxTicks; i++) { if (cond()) return i; if (i < maxTicks) d.World.Step(); }
            return -1;
        }
        PlayerController BkAdd(Vector3 pos, string cls = "Arcanist") => ScriptedParty.Add(d.World, pos, cls);
        ScriptedPlayer BkScript(PlayerController p) => ScriptedParty.Script(p);
        static void BkPlace(PlayerController p, Vector3 pos) { p.transform.position = new Vector3(pos.x, 0, pos.z); }
        int BkHitsOn(PlayerController p, string source = null) => bkHits.Count(h => h.Player == p && (source == null || h.Source == source));
        int BkPatternsLive() => d.World.Hazards.Live.Select(h => h.Source).Distinct().Count();
        void BkTopUp() { foreach (var p in d.World.Players) if (!p.Down && p.Hp < 60) p.DebugSetHp(PlayerController.MaxHp); }
        // farthest point of the floor from `from`, along `dir` (circle or square bound)
        static Vector3 BkEdge(Vector3 dir, float inset = 0.05f)
        {
            dir = Sim.Flat(dir).normalized; float b = CombatWorld.PlayerBound - inset;
            if (!CombatWorld.SquareBound) return dir * b;
            float k = b / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z)); return dir * k;
        }
        // A short real-time clip of the current boss script (the director runs, the player is kept alive).
        IEnumerator BkClip(string name, float seconds)
        {
            bk.BeginCombat(); d.SetPaused(false);
            StartCoroutine(Capture(name, Mathf.RoundToInt(seconds * 60)));
            float t = 0; while (t < seconds) { BkTopUp(); t += Time.deltaTime; yield return null; }
            yield return null;
            d.SetPaused(true); bk.Halt(); d.World.ClearTransient();
        }

    }
}
