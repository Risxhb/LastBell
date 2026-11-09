using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // One Aurelion ability-set-v2 ability (bosses/1-skyward-spire-aurelion/abilities-v2/ABILITIES.md, proposal). Each
    // ability is one class in Abilities/ that spawns its hazards through W.Hazards and yields ticks like an encounter
    // script (`yield return n` = resume n ticks later). The phase plan (AurelionEncounter.V4Script) decides when it runs
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
        // Last Light cuts the running block off at 20 % (Chzy18, 2 Nov): drop any state the bots or the HUD would still read
        public virtual void Abort() { }

        // Easy stretches every warning, track and window by 1.25 (ABILITIES.md §5); gaps and the punish stay as authored.
        public float WarnScale => W.Difficulty == Difficulty.Easy ? 1.25f : 1f;
        protected int T(float seconds) => Sim.Ticks(seconds * WarnScale);
        // v2.1 §2: on Hard every lock-to-hit WINDOW is x0.7 (never below `floor`, e.g. Orrery's 2.0 s); tracks stay as on
        // Medium (use T for them). Easy stretches windows like every warning (x1.25).
        // v4 Medium (Chzy18, 1 Nov: "make medium harder"): windows x0.85, halfway to Hard; the same floors apply
        protected int Win(float seconds, float floor = 0) { seconds *= PhaseWindow; return Sim.Ticks(Hard && !AurelionArgs.WindowsMedium ? Mathf.Max(seconds * HardWindow, floor) : MediumV4 ? Mathf.Max(seconds * MediumWindow, floor) : seconds * WarnScale); }
        // Phases 2 and 3 harder (Chzy18, 2 Nov: "P2 and 3 are too easy"): shorter windows, same floors (proposal; no damage: rejected)
        // 3 Nov (Chzy18 "I want them harder especially p3"): x0.85 / x0.75, from x0.9 / x0.8
        public const float P2Window = 0.85f, P3Window = 0.75f;
        protected float PhaseWindow => Boss.Phase == 2 ? P2Window : Boss.Phase == 3 ? P3Window : 1f;
        public const float HardWindow = 0.7f, MediumWindow = 0.85f;
        protected bool MediumV4 => W.Difficulty == Difficulty.Medium;
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
        // counted in place, not via Standing(): bots read it through WellRadius on every probe (perf 3 Nov: 0.5-1.4 MB a frame in Total Eclipse)
        protected int N { get { int n = 0; var ps = W.Players; for (int i = 0; i < ps.Count; i++) if (ps[i] && !ps[i].Down) n++; return Math.Max(1, n); } }
        // aim targets: standing and not reviving an ally (revive rule, 4 Nov: "No mechanic aims at them"; Codex AUR-TARGET-OTHER,
        // 7 Nov). Only who an attack is aimed at: counts, soaks, timings and collisions still use every standing player, so a
        // reviver inside someone else's hazard is still hit.
        protected List<PlayerController> Aimable() => W.Players.FindAll(p => p && !p.Down && p.Reviving == null);
        protected PlayerController FarthestAim(Vector3 from) => Aimable().OrderByDescending(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        protected PlayerController NearestAim(Vector3 from) => Aimable().OrderBy(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        protected PlayerController Farthest(Vector3 from) => Standing().OrderByDescending(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        protected PlayerController Nearest(Vector3 from) => Standing().OrderBy(p => Sim.Flat(p.transform.position - from).sqrMagnitude).FirstOrDefault();
        public const int LastLightCap = 90;   // the biggest hit Last Light's scaling may build, after the difficulty scale
        protected Hazard Spawn(Hazard h)
        {
            if (string.IsNullOrEmpty(h.Source) || h.Source == "Hazard") h.Source = Name;
            h.Damage = V3Tier(h.Damage);
            // v4 Last Light, the soft enrage: every fallen slice makes his attacks 10 % stronger
            // capped so a hit never lands above 90 after the difficulty scale (no one-shots, design rules 5): Hard 69 x 1.3
            if (Boss.Light != null && Boss.Phase >= 4 && h.Damage > 0)
                h.Damage = Mathf.Min(Mathf.RoundToInt(h.Damage * Boss.Light.DamageScale), Mathf.Max(h.Damage, Mathf.FloorToInt(LastLightCap / W.DamageTakenScale)));
            if (h.BurnPerSecond > 0) h.FillToLock = true;   // a burn counts down to its lock, then holds: no slow fake countdown
            Boss.Track(W.Hazards.Spawn(h));
            if (h.Tele != null)
            {
                h.Tele.Motif(SkywardMotif);
                // draw order (readability A2/A4): standing burns under everything, tracking warnings in the middle, a
                // locked footprint over them (it is the one that will hit)
                if (h.BurnPerSecond > 0) h.Tele.Order = -1;
                else if (h.Locked) h.Tele.Order = 1;
                else h.Tele.LockOnTop = true;
            }
            return h;
        }
        // every Aurelion warning wears the Skyward Spire motif (light rays, orbital arcs, sun bars) over the shared plain
        // outline, outward-fill countdown and resolve flash (Reference/Telegraph-Style v2, gpt/08-design-rules.md §5).
        // Cues that are not "get out" warnings (Meridian Towers' cobalt soaks) set their own look after Spawn.
        public const int SkywardMotif = 1;
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
    //   v4, the mythic set, is the only fight (Chzy18, 1 Nov: older versions retired, -lastbellAbilitySet removed)
    //   -lastbellAbility <id>       fire one ability on its own, on loop, e.g. aurelion.meridian_lances
    // In practice F6 cycles the same single-ability loop: off > each ability in turn > off.
    public static class AurelionArgs
    {
        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        public static string SoloOverride;    // F6 / tests: the single ability to loop (null = command line)
        // test switch (HARDER-PROMPT target 7): Hard damage and mechanics with Medium window lengths
        public static bool WindowsMediumOverride;
        public static bool WindowsMedium => WindowsMediumOverride || Environment.GetCommandLineArgs().Any(x => x.Equals("-lastbellWindowsMedium", StringComparison.OrdinalIgnoreCase));
        public static string Solo => SoloOverride ?? Arg("-lastbellAbility");
        public static readonly string[] Ids = { "aurelion.meridian_lances", "aurelion.starfall_mirror", "aurelion.orrery_closing", "aurelion.hour_hands", "aurelion.zenith_collapse", "aurelion.ecliptic_order", "aurelion.zenith_marks", "aurelion.meridian_towers", "aurelion.sunspots", "aurelion.total_eclipse", "aurelion.severance", "aurelion.last_light", "aurelion.meridian_recall" };
        public static bool Known(string id) => id != null && Ids.Contains(id);
    }
}
