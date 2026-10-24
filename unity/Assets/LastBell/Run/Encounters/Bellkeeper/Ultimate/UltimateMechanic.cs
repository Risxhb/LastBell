using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // One signature mechanic of the Bellkeeper Ultimate (proposal, bosses/4-shattered-hour-bellkeeper/ultimate/ULTIMATE.md
    // §3). Each lives in its own file in this folder and is built by its own session; the encounter's Ultimate plan
    // (BellkeeperUltimate.cs, ULTIMATE.md §5) only decides when each one starts and with which cue.
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
        protected int Now => W.Tick;
        // world tick of this cast's launch, and of a cue time (seconds after launch)
        protected int LaunchTick => LastCastTick;
        protected int TickAt(float seconds) => LastCastTick + Sim.Ticks(seconds);

        // Spawn on this mechanic's behalf from shared code (UltimateState lanes): tests see it in Spawned.
        public Hazard SpawnShared(Hazard h) => Spawn(h);

        // The perfect bot's strat for player p: what it must satisfy and when. Add nothing when the mechanic asks nothing of p.
        public virtual void Demands(PlayerController p, List<BotDemand> into) { }

        // -lastbellAbility ult_<id>: the default practice loop (Setup, Cast with the default cue, 2 s gap). Override when
        // the mechanic needs partners (e.g. a recorded P1 before a replay).
        public override IEnumerable<int> Practice()
        {
            foreach (var w in base.Practice()) yield return w;
        }

        public override void Cancel() { base.Cancel(); }
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

    // The Ultimate's mechanics, one instance of each per encounter (ULTIMATE.md §3 order). Keys: -lastbellAbility ult_<id>.
    public static class UltimateMechanics
    {
        public const string PendulumClocks = "bellkeeper.ult_pendulum_clocks", StoppedHour = "bellkeeper.ult_stopped_hour",
            Ledger = "bellkeeper.ult_ledger", BackwardsBell = "bellkeeper.ult_backwards_bell", TwoFutures = "bellkeeper.ult_two_futures",
            ConductorsHand = "bellkeeper.ult_conductors_hand", TollingHour = "bellkeeper.ult_tolling_hour",
            ThirteenthHour = "bellkeeper.ult_thirteenth_hour", AreaQuotes = "bellkeeper.ult_area_quotes", Fracture = "bellkeeper.ult_fracture",
            // v2 (ULTIMATE-PROMPT-v2.md, cards-v2): the P1 slice
            EscapementRelay = "bellkeeper.ult_escapement_relay", ChimeDebts = "bellkeeper.ult_chime_debts";
        public static readonly string[] Ids = { PendulumClocks, StoppedHour, Ledger, BackwardsBell, TwoFutures, ConductorsHand, TollingHour, ThirteenthHour, AreaQuotes, Fracture, EscapementRelay, ChimeDebts };

        public static UltimateMechanic[] CreateAll() => new UltimateMechanic[]
        {
            new PendulumClocks(), new StoppedHour(), new LedgerMechanic(), new BackwardsBell(), new TwoFutures(),
            new ConductorsHand(), new TollingHour(), new ThirteenthHour(), new AreaQuotes(), new FractureOfTime(),
            new EscapementRelay(), new ChimeDebts(),
        };
    }
}
