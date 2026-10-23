using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // One Aurelion ability-set-v2 ability (bosses/1-skyward-spire-aurelion/abilities-v2/ABILITIES.md, proposal). Each
    // ability is one class in Abilities/ that spawns its hazards through W.Hazards and yields ticks like an encounter
    // script (`yield return n` = resume n ticks later). The phase plan (AurelionEncounter.V2Script) decides when it runs
    // and plays the gaps and the open-chest punish; an ability's Run returns when its last damaging hazard has resolved
    // (the Horizon is the one exception: Orrery Closing returns when the rim and crown resolve, and the Horizon stays
    // lit on its own ActiveTicks). Boss death clears every pending hazard in the killing tick (BossEncounter.Die), so
    // abilities need no death handling of their own.
    public abstract class AurelionAbility
    {
        public abstract string Id { get; }            // "aurelion.<id>", the -lastbellAbility value
        public abstract string Name { get; }          // hazard Source / HUD name
        protected AurelionEncounter Boss { get; private set; }
        protected CombatWorld W { get; private set; }
        public void Bind(AurelionEncounter boss, CombatWorld world) { Boss = boss; W = world; }

        // Easy stretches every warning, track and window by 1.25 (ABILITIES.md §5); gaps and the punish stay as authored.
        public float WarnScale => W.Difficulty == Difficulty.Easy ? 1.25f : 1f;
        protected int T(float seconds) => Sim.Ticks(seconds * WarnScale);
        // v2.1 §2: on Hard every lock-to-hit WINDOW is x0.7 (never below `floor`, e.g. Orrery's 2.0 s); tracks stay as on
        // Medium (use T for them). Easy stretches windows like every warning (x1.25).
        protected int Win(float seconds, float floor = 0) => Sim.Ticks(Hard && !AurelionArgs.WindowsMedium ? Mathf.Max(seconds * HardWindow, floor) : seconds * WarnScale);
        public const float HardWindow = 0.7f;
        // v3 damage tiers (HARDER-PROMPT target 5): every hit is punish (<= 30) or kill (>= 50) at Medium, nothing between:
        // Meridian Lances 35 -> 30, Orrery Closing 40 and Zenith Collapse 45 -> 50
        public static int V3Tier(int dmg) => dmg <= 30 ? dmg : dmg < 40 ? 30 : Math.Max(50, dmg);
        protected bool Hard => W.Difficulty == Difficulty.Hard;
        protected bool Easy => W.Difficulty == Difficulty.Easy;

        // the encounter, for the ability
        protected Transform Body => Boss.transform;
        protected Vector3 BossPos => Sim.Flat(Boss.transform.position);
        protected int Phase => Boss.Phase;                         // 1 Perfect Order, 2 Misalignment, 3 Exposed Meridian
        protected bool SecondForm => Boss.SecondForm;              // Exposed Meridian (30 %)
        protected List<PlayerController> Standing() => W.Players.FindAll(p => p && !p.Down);
        protected int N => Math.Max(1, Standing().Count);
        protected PlayerController Farthest(Vector3 from) => Standing().OrderByDescending(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        protected PlayerController Nearest(Vector3 from) => Standing().OrderBy(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        protected Hazard Spawn(Hazard h) { if (string.IsNullOrEmpty(h.Source) || h.Source == "Hazard") h.Source = Name; if (AurelionArgs.V3 || AurelionArgs.V4) h.Damage = V3Tier(h.Damage); return Boss.Track(W.Hazards.Spawn(h)); }
        protected void Say(string current) => Boss.SetCurrent(current);
        protected void Glow(float k) => Boss.Glow(k);
        protected void Flare(float f = 1) => Boss.Flare(f);
        protected void Clip(string name, int ticks) => Boss.PlayClip(name, ticks);

        // arena: Skyward Spire is round, bound 13.9 m, centred on the origin (RunMaps.cs); the boss stays inside 9.5 m
        public static float Rim => CombatWorld.PlayerBound;
        public static readonly Vector3 Centre = Vector3.zero;
        public const float BeyondRim = 28f;                        // full-arena lanes / rings reach past the bound
    }

    // Launch switches (practice only):
    //   -lastbellAbilitySet v1|v2|v3|v4   v4 (default) is the mythic set (abilities-v4); v3 the Milestone A set; v2 is v2.1; v1 the packet's five attacks
    //   -lastbellAbility <id>       fire one v2 ability on its own, on loop, e.g. aurelion.noon_shadow
    // In practice F6 cycles the same single-ability loop: off > each ability in turn > off.
    public static class AurelionArgs
    {
        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        public const string DefaultSet = "v4";   // Chzy18 2026-10-22: every level gets the mythic set; v2 / v3 stay reachable by flag
        public static string SetOverride;     // tests force a set without a command line
        public static string SoloOverride;    // F6 / tests: the single ability to loop (null = command line)
        public static bool V1 => (SetOverride ?? Arg("-lastbellAbilitySet") ?? DefaultSet).Equals("v1", StringComparison.OrdinalIgnoreCase);
        public static bool V3 => (SetOverride ?? Arg("-lastbellAbilitySet") ?? DefaultSet).Equals("v3", StringComparison.OrdinalIgnoreCase);
        public static bool V4 => (SetOverride ?? Arg("-lastbellAbilitySet") ?? DefaultSet).Equals("v4", StringComparison.OrdinalIgnoreCase);
        // test switch (HARDER-PROMPT target 7): Hard damage and mechanics with Medium window lengths
        public static bool WindowsMediumOverride;
        public static bool WindowsMedium => WindowsMediumOverride || Environment.GetCommandLineArgs().Any(x => x.Equals("-lastbellWindowsMedium", StringComparison.OrdinalIgnoreCase));
        public static string Solo => SoloOverride ?? Arg("-lastbellAbility");
        public static readonly string[] Ids = { "aurelion.meridian_lances", "aurelion.starfall_mirror", "aurelion.noon_shadow", "aurelion.orrery_closing", "aurelion.hour_hands", "aurelion.zenith_collapse", "aurelion.ecliptic_order", "aurelion.zenith_marks", "aurelion.meridian_towers", "aurelion.sunspots", "aurelion.total_eclipse", "aurelion.severance", "aurelion.last_light" };
        public static bool Known(string id) => id != null && Ids.Contains(id);
    }
}
