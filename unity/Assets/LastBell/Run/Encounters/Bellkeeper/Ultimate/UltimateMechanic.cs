using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // One mechanic of the Bellkeeper Ultimate (specs: bosses/4-shattered-hour-bellkeeper/ultimate/P1-v2-LOCKED.md, P2-v2-LOCKED.md,
    // P5-v2-PROPOSAL.md). Each lives in its own file in this folder; the encounter's plan (BellkeeperUltimate.cs) only decides
    // when each one starts and with which cue.
    //
    // Contract (on top of BellkeeperAbility's: Cast() is a tick coroutine, tick 0 = the launch tick, hazards via Spawn()):
    // - The plan sets NextCue and launches; Cue holds it for the whole cast (DefaultCue() if launched bare, e.g. practice).
    //   Cue.At lists the cast's resolve times in seconds after launch; what each entry means is the mechanic's own (its
    //   header comment says). A mechanic must resolve exactly on those ticks: simultaneous resolutions are aligned by them.
    // - Shared systems: Ult (BellkeeperEncounter.Ult, UltimateState.cs): stations, fissure, personal status and ivory
    //   glyphs, Stilled, Cracked Hour, first-body lanes, the ledger recorder, wipes. Standard shapes: UltShapes.
    // - Demands(): the perfect bot's strat (GUIDE.md), as position rules per player. The bot (UltimateBot) solves them
    //   together with every live hazard; an imperfect bot reacts later or skips one Memory demand per phase.
    // - Boss death, halt, restart, phase change: Cancel() (drop presentation and state); the encounter already cancelled
    //   the hazards and clears statuses itself.
    public abstract class UltimateMechanic : BellkeeperAbility
    {
        public UltCue NextCue;
        public UltCue Cue { get; private set; } = new UltCue();
        internal void TakeCue() { Cue = NextCue ?? DefaultCue(); NextCue = null; }
        // the cue a bare launch uses (the -lastbellAbility practice loop, tests)
        public virtual UltCue DefaultCue() => new UltCue();

        protected UltimateState Ult => Boss.Ult;
        protected int Party => Boss.PartySize;
        // Easy shows lethal warnings 0.3 s earlier (RI-005, accepted 27 Oct); only the warning moves, never the hit
        protected float WarnLead => Boss.UltWarnLeadFor;
        protected int Now => W.Tick;
        // world tick of this cast's launch, and of a cue time (seconds after launch)
        protected int LaunchTick => LastCastTick;
        protected int TickAt(float seconds) => LastCastTick + Sim.Ticks(seconds);

        // The boss model's cast clip for this moment (Bellkeeper2.glb; graphics audit 8 Nov), stretched to end on the resolve.
        // Presentation only: nothing in the fight reads it.
        protected void CastClip(string clip, float seconds) => Boss.UltCastClip(clip, seconds);

        // Spawn on this mechanic's behalf from shared code (UltimateState lanes): tests see it in Spawned.
        public Hazard SpawnShared(Hazard h) => Spawn(h);

        // The perfect bot's strat for player p: what it must satisfy and when. Add nothing when the mechanic asks nothing of p.
        public virtual void Demands(PlayerController p, List<BotDemand> into) { }

        // v2 phases the position rules can't express (bullet hell, a moving floor): the mechanic steers bot p itself. vel is
        // the bot's own walking velocity (world, m/s; the floor may add to it). Return false to leave the bot to Demands.
        public virtual bool Steer(PlayerController p, UltimateBot.Skill skill, out Vector3 vel) { vel = default; return false; }
        // ...and what a steered bot attacks: aim point, and whether its whole kit is safe (else basic attacks from range only)
        public virtual bool AttackTarget(PlayerController p, out Vector3 aim, out float reach, out bool fullKit) { aim = default; reach = 0; fullKit = false; return false; }
        // ...and before a press that roots or slows it for `seconds` (at `scale`): does the route search still find a way with
        // no hit, standing that long first? (the searches count the whole root, not only their first leg)
        public virtual bool SafeToRoot(PlayerController p, float seconds, float scale) => true;
        // ...and the steered bot's current job ("revive": UltimateBot stands still beside the downed ally when SafeToRoot allows)
        public virtual string Job(PlayerController p) => null;

        // Revives (PlayerController, accepted 4 Nov; AllyBot.ReviveJob for the other bosses): a bot with no job of its own goes to
        // the nearest downed ally (not dead, within 14 m, nobody else channelling on them) and stands 0.9 m beside them, on its side
        public const float ReviveStand = 0.9f, ReviveReach = 14f;
        public static bool ReviveSpot(PlayerController me, Func<Vector3, bool> ok, out Vector3 spot)
        {
            spot = default; PlayerController best = null; var pos = Sim.Flat(me.transform.position); float bd = ReviveReach;
            foreach (var p in me.World.Players)
            {
                if (p == me || !p || !p.Down || p.Dead) continue;
                bool taken = false; foreach (var q in me.World.Players) if (q != me && q && !q.Down && q.Reviving == p) taken = true;   // two are no faster
                float dd = Sim.Flat(p.transform.position - pos).magnitude;
                if (!taken && dd < bd) { bd = dd; best = p; }
            }
            if (best == null) return false;
            var at = Sim.Flat(best.transform.position); var off = pos - at;
            spot = at + (off.magnitude > 0.1f ? off.normalized : Vector3.forward) * ReviveStand;
            return ok == null || ok(spot);
        }
        // beside a downed ally, close enough to channel (PlayerController.ReviveRange, with a margin for the floor's drift)
        public static bool BesideDowned(PlayerController me)
        {
            var pos = Sim.Flat(me.transform.position);
            foreach (var p in me.World.Players)
                if (p != me && p && p.Down && !p.Dead && Sim.Flat(p.transform.position - pos).magnitude < PlayerController.ReviveRange - 0.1f) return true;   // 0.2 short left a bot that stopped 1.3 m off a riding body walking
            return false;
        }
        // Combat start: make this mechanic's scene objects now, hidden, so its cast only shows them (a phase change no longer
        // builds a whole set: 60-91 ms hitches, performance agent 2 Nov). Cancel() still drops them; the cast then builds as before.
        public virtual void Prebuild() { }
        // the steered phases' replan rhythm (performance agent, 2 Nov: P4 at about 7 fps, all in the bots' route searches): each bot
        // searches every ReplanTicks on its own tick, so at most one bot searches per tick. A route holds its first velocity for a
        // 0.2 s leg anyway, so 0.1 s between searches stays inside what the search planned.
        protected virtual int ReplanTicks => 6;   // P5 keeps 3 (FinalToll): at 6 its party lost the kill by 1 %
        protected bool SearchTurn(PlayerController p) { int i = W.Players.IndexOf(p); return ((Now - i) % ReplanTicks + ReplanTicks) % ReplanTicks == 0; }
        // the route searches' slowdown: (seconds, scale) for the current action, or for a press being tested
        protected float rootTestFor = -1, rootTestScale = 1;
        protected bool lastClean;   // the last search found a route with no hit through every leg
        protected bool RootTest(float seconds, float scale, Action search)
        {
            rootTestFor = seconds; rootTestScale = scale;
            try { search(); } finally { rootTestFor = -1; rootTestScale = 1; }
            return lastClean;
        }
        protected (float forS, float scale) SlowOf(PlayerController p)
        {
            float ms = p.Kit != null ? Mathf.Clamp01(p.Kit.MoveScale) : 1f, left = p.Kit != null ? p.Kit.SlowLeft : 0;
            if (rootTestFor < 0) return (left, ms);
            return (Mathf.Max(left, rootTestFor), Mathf.Min(ms, rootTestScale));
        }

        // -lastbellAbility ult_<id>: the default practice loop (Setup, Cast with the default cue, 2 s gap). Override when
        // the mechanic needs partners (e.g. a recorded P1 before a replay).
        public override IEnumerable<int> Practice()
        {
            foreach (var w in base.Practice()) yield return w;
        }

        public override void Cancel() { base.Cancel(); }

        // a world label (OnGUI), shadowed, in an EncounterCallouts theme style. Labels drawn in the same GUI pass don't overlap: one that would land on an earlier one moves
        // up a row (performance agent's screenshots, 2 Nov: two players side by side read "IV II gap IV" in P5). One copy for every phase.
        static readonly List<Rect> placed = new List<Rect>(); static int placedFrame = -1; static EventType placedEvent;
        // Unity caches laid-out text per colour, so a label whose colour changes every frame makes a new cache entry each frame
        // (performance agent, 3 Nov). Blinks and fades step through a few fixed values instead.
        protected static float Steps(float t, int n = 6) => Mathf.Round(t * n) / n;
        protected static float Blink => Steps(Mathf.PingPong(Time.unscaledTime * 3, 1));
        protected static void Label(Camera cam, Vector3 world, float height, string text, Color c, GUIStyle style)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, height, world.z)); if (sp.z <= 0) return;
            if (placedFrame != Time.frameCount || placedEvent != Event.current.type) { placed.Clear(); placedFrame = Time.frameCount; placedEvent = Event.current.type; }
            float w = style.CalcSize(new GUIContent(text)).x + UiTheme.V(20), h = EncounterCallouts.TagHeight(style);
            var box = new Rect(sp.x - w / 2, Screen.height - sp.y - h / 2, w, h);
            for (int tries = 0; tries < 4; tries++)
            {
                bool hit = false; foreach (var q in placed) if (q.Overlaps(box)) { hit = true; box.y = q.y - h; break; }
                if (!hit) break;
            }
            placed.Add(box);
            EncounterCallouts.TagBack(box, c);   // ink strip + a tick in the mechanic's colour; the word itself in Ivory
            UiTheme.Label(box, text, style, EncounterCallouts.Alpha(UiTheme.Ivory, c.a));   // shadowed, tinted through GUI.color (no re-layout when the colour blinks)
        }
    }

    // How the plan starts one cast of a mechanic.
    public sealed class UltCue
    {
        public string Variant = "";                 // mechanic-specific mode, e.g. Pendulum Clocks "singles" / "pairs"
        public float[] At = Array.Empty<float>();   // resolve times in seconds after launch (meaning: the mechanic's header)
        public int Phase;                           // Ultimate phase 1..5 (0 = transition or practice)
        public int Seed;                            // seeded, readable variation (ULTIMATE.md §7)
        public int Count;                           // mechanic-specific count (tolls, steps), 0 = default
        public float At0(float fallback) => At != null && At.Length > 0 ? At[0] : fallback;
        public float AtOr(int i, float fallback) => At != null && At.Length > i ? At[i] : fallback;
        public override string ToString() => $"{Variant}[{string.Join(",", At ?? Array.Empty<float>())}] p{Phase} s{Seed}";
    }

    // One rule a mechanic puts on one player for the perfect bot: be somewhere Ok(position) says on world tick Tick.
    // Since: the first world tick a real player could know the rule (the numeral, glyph, phrase or telegraph appears).
    // Hint: the guide's spot (a known-good position). Memory: the rule needs a read or a memory (not a visible coral
    // shape); an imperfect bot may miss one such assignment per phase. MinHp: be at least this healthy by Tick (potions).
    public struct BotDemand
    {
        public int Tick, Since;
        public Func<Vector3, bool> Ok;
        public Vector3 Hint;
        public bool Memory;
        public int MinHp;
        public string Why;
        public UltimateMechanic From;
    }

    // The Ultimate's mechanics (the v2 fight: P1 relay + debts, P2 Broken Hour, P3 Open Hour, P4 Gear Floor, P5 Final Toll), one
    // instance of each per encounter. Keys: -lastbellAbility ult_<id>. The v1 Ultimate plan's mechanics were deleted (Chzy18, 1 Nov).
    public static class UltimateMechanics
    {
        public const string EscapementRelay = "bellkeeper.ult_escapement_relay", ChimeDebts = "bellkeeper.ult_chime_debts", BrokenHour = "bellkeeper.ult_broken_hour",
            OpenHour = "bellkeeper.ult_open_hour", GearFloor = "bellkeeper.ult_gear_floor", FinalToll = "bellkeeper.ult_final_toll";
        public static readonly string[] Ids = { EscapementRelay, ChimeDebts, BrokenHour, OpenHour, GearFloor, FinalToll };

        public static UltimateMechanic[] CreateAll() => new UltimateMechanic[]
        {
            new EscapementRelay(), new ChimeDebts(), new BrokenHour(), new OpenHour(), new GearFloor(), new FinalToll(),
        };
    }
}
