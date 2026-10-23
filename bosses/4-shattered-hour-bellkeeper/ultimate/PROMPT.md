# Prompt: The Bellkeeper — Ultimate (paste into the Shattered Hour session)

You are the **Shattered Hour (Bellkeeper)** session. Your job: turn the Bellkeeper — the final boss of Last Bell, The Sovereign of the Shattered Hour — into the hardest fight in the game, at the difficulty of a **Final Fantasy XIV Ultimate**. Ultimates take static groups weeks of progression; ours doesn't have to take weeks, but it must take **many attempts** (dozens, not a handful) before a party or a skilled solo player clears it, and every attempt should teach something. **It must be VERY HARD. Don't propose milder versions.**

Work in this order and don't skip steps: Part 0 (already decided) → Part 1 (research) → Part 2 (design) → Part 3 (proof on paper) → Part 4 (build) → Part 5 (bot clear + report).

---

## Part 0 — Already decided (don't ask)

**Chzy18, 2026-10-23: "forget those rules."** The encounter limits in `gpt/08-design-rules.md` that hold Ultimates back don't apply to the Bellkeeper Ultimate. Design as an Ultimate would:
1. **Overlap:** any number of damaging patterns at once (3, 4 or more), as long as a route exists.
2. **Enrage:** a hard enrage that ends the attempt, plus per-phase DPS checks (push timers) that fail the attempt if the party's damage is too low.
3. **Wipes:** a failed mechanic may end the attempt for the whole party, even solo.
4. **One-shots:** allowed. Mechanic failures may kill outright from full HP; heavy hits may too where an Ultimate would.

The rest are your calls (provisional; Chzy18 can change them later):
5. **Who gets it:** a separate **"Ultimate"** entry (practice list and a run option), so a roguelite run isn't walled; Easy/Medium/Hard keep the v2 fight.
6. **Length and structure:** 10–14 min at target DPS, 5 phases plus 2 transitions, the second form at about 50 %.
7. **Practice tooling:** practice may start at any phase (`-lastbellUltimatePhase N` and a menu option); a real attempt always starts at phase 1.
8. **Healing pressure:** unavoidable raidwides that leave the party low for the next mechanic. Potions are the only guaranteed healing, so running out is a real failure.

Still holding (they're how the game plays, not difficulty caps): every mechanic still has a real way to survive it (no "unavoidable kill"), no player teleports, no class is required (any party, including all one class, can clear), the boss never heals or gets a new health bar, and players aim with the mouse (no lock-on). Iframes stay out: dodging is moving, not immunity — that's what makes it hard.

Record these in `gpt/08-design-rules.md` as "Exception, Bellkeeper Ultimate (Chzy18, 2026-10-23)", next to Aurelion's, and keep the repo copy in sync.

---

## Part 1 — Research (sources, in your own words)

Read widely and **summarise in your own words**: no copied guide text, no datamined files, no decompiled data in the repo (the GitHub account was suspended partly over public decompile data — see memory `last-bell-github-suspended`). Link sources; quote at most a short phrase.

**FFXIV Ultimates** (every one released up to today; check for any newer than this list):
- The Unending Coil of Bahamut (UCoB)
- The Weapon's Refrain (UWU)
- The Epic of Alexander (TEA)
- Dragonsong's Reprise (DSR)
- The Omega Protocol (TOP)
- Futures Rewritten (FRU)

For each: phase list, each phase's signature mechanic, what makes it hard (memory, assignment puzzles, reading debuffs, simultaneous resolution, DPS checks, healing checks, rotations), how long a "phase" lasts, how many unique mechanics, typical clear pull counts / weeks. Use the Console Games Wiki, the official Lodestone, community strat sites (e.g. Hector Hectorson / raidplan write-ups, The Balance), and well-known video guides for timelines.

**WoW Mythic final bosses since Mythic difficulty arrived (Warlords of Draenor, 2014) through today:** Blackhand, Archimonde, Xavius, Gul'dan, Kil'jaeden, Argus, G'huun, Jaina, Queen Azshara, N'Zoth, Sire Denathrius, Sylvanas Windrunner, the Jailer, Raszageth, Sarkareth, Fyrakk, Queen Ansurek, Chrome King Gallywix, Dimensius, L'ura (Midnight Falls), and any newer. Same summary fields, plus Race to World First pull counts. Sources: Method, Icy Veins, warcraft.wiki.gg, Wowhead, Mythic Trap.

**Deliverable:** `bosses/4-shattered-hour-bellkeeper/ultimate/RESEARCH.md`: one row per fight (what makes it hard, its 2–3 signature mechanics, pull counts), then a **pattern library**: the 15–25 recurring Ultimate/Mythic *design techniques* (e.g. "mechanic recorded now, resolved later", "debuff puzzle where each player reads their own assignment", "two halves of the party resolve different mechanics", "fake-out telegraph that resolves in reverse", "the boss replays a whole earlier phase faster", "adds that must die at the same time", "DPS check that ends the phase", "knockback into a safe spot that only exists if you were positioned right"), each with which fights use it and how the Bellkeeper could use it.

### Don't overlap Aurelion (or the other bosses)
Aurelion v4 (`aurelion-design/bosses/1-skyward-spire-aurelion/abilities-v4/ABILITIES-v4.md`) already takes L'ura, Dimensius and Queen Ansurek. **Do not reuse these techniques as Bellkeeper signatures:**
- numbered towers where an empty tower wipes (Meridian Towers)
- staggered marked detonations that leave pools and chain-react (Sunspots)
- an attack that grows every cast plus a pull ("Devour"/Solar Hunger)
- a light-well intermission with a damage shield (Total Eclipse / Corona Shield)
- the arena split into two halves the party must divide across (Severance: Day/Night)
- wedges of floor filling permanently toward an enrage (Last Light)
- Limit-Cut numerals over heads with a beam through each player (Ecliptic Order)
- Zenith Marks, Meridian Lances, Starfall Mirror, Hour Hands, Orrery Closing/Horizon
Also don't copy Crucivar's or Sylvara's signatures (soak towers/tethers/edges; pass-the-mark/groups) — the Bellkeeper may *quote* the three areas once as a final exam, clearly reworked.

Note: the Bellkeeper v2 set already has **Striking Order** (numerals in order), which overlaps Aurelion's Ecliptic Order. The Ultimate must replace or rework it into something that is only the Bellkeeper's.

**The Bellkeeper's own lane is time.** He refused to ring the final second; he records, rewinds, stops, replays and plays futures out of order. Lean on the Ultimate techniques that match that (look at TEA's Fate Calibration, FRU's Ultimate Relativity, TOP's Program Loop / Pantokrator / Hello World, DSR's Wrath of the Heavens / Death of the Heavens, UWU's Ultimate Annihilation/Suppression, UCoB's Golden Bahamut "remembered" Tenstrike/Grand Octet) and invent original Last Bell versions: a rewound timeline the party must re-dodge from memory, a recorded future shown first and played later in a different order, personal "clock" debuffs each player resolves at their own time, a stopped-time window where only some players can act.

---

## Part 2 — Design (`bosses/4-shattered-hour-bellkeeper/ultimate/ULTIMATE.md`, proposal)

Keep: identity, model (Bellkeeper 2.0), phase-look system (closed shell → parted → open chest), the Throne of the Final Toll arena (13.0 m), and the v2 vocabulary where it serves (Leaning Bell, Reflection, Toll Tide, Unfinished Hour) — reworked, faster-layered, recombined. Keep v2 reachable (`-lastbellAbilitySet v2`).

Must have:
- **5 phases + 2 transitions**, each phase a distinct "fight" with its own signature mechanic and its own lesson, as Ultimates do. At least one phase must be a **replay** of an earlier phase under new rules (the Unfinished Hour made real). The last phase is the hardest and ends in a hard enrage.
- **Signature Ultimate mechanics:** at least 6 new named mechanics, each listed with its Ultimate/Mythic inspiration (technique, not copy). At least 3 must be **memory/assignment puzzles** (each player must know where *they* go from *their own* marker), and at least 2 must be **simultaneous resolutions** (several rules checked on the same tick).
- **Every player count has the same difficulty.** 1, 2, 4, 8 each get written versions; solo gets more assignments, never fewer. Groups get spatial jobs that need coordination (call-outs), not more HP.
- **Melee and ranged both in danger** (rules §5), no permanently safe spot, and the major punish window (3–4 s) still exists in every phase — Ultimates have downtime, it's just short.
- **No randomness that can't be solved:** variation is seeded and readable (Ultimates have a fixed timeline with a few known random branches; list ours).
- **Damage:** a mistake costs a lot; mechanic failures may one-shot (Part 0). Raidwides tuned so potions are a resource you can run out of.
- **DPS checks:** per-phase push targets measured from real bot DPS, not guessed (rules: "set HP from measured DPS").
- **Timeline:** a second-by-second table per phase at Medium: every telegraph's warn/lock/resolve, how many patterns live, which players act. A **decision-density** number per phase (decisions per 10 s); target clearly higher than v2 and higher than Aurelion v4.
- Difficulty: a separate Ultimate entry (Part 0 item 5); Easy/Medium/Hard keep v2.
- Conflicts with settled rules and open questions, as always.

### Fit the Bellkeeper's look (required for every mechanic)
Read first: `bosses/4-shattered-hour-bellkeeper/BOSS.md` and its sheet `04-bellkeeper-v1.png`, `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md`, `gpt/05-arenas.md` (Shattered Hour), the Shattered Hour arena handoff (`Reference/LAST-BELL-ARENA-ART-HANDOFF/LAST-BELL-SHATTERED-HOUR-ARENA.md`) and `gpt/08-design-rules.md` §6–§7.
- **Every mechanic is something the Bellkeeper physically does with his own body or his arena:** the pendulum right hand (strikes, swings, sweeps), the conducting left fingers (directs, points, "conducts" the timeline), the clapper in the chest crack (the heart toll), the split bell shell (opens, leans, tolls), the three broken clock rings and halo (rotate, desync, reverse, spread), the chains, the monumental cracked bell behind the north rim, the gold fissure, the four rim dial stations, the cosmic void beyond the rim. No generic fireballs, lasers or summoned monsters; no weapons he doesn't have.
- **Time is the visual language:** clock faces, hour marks, Roman numerals, hands that sweep, rings that run backwards, echo after-images in porcelain ivory, cracked shock-fronts, stopped moments (frozen motes, a pendulum hanging mid-swing), rewinds (effects playing in reverse). A recorded or future mechanic must *look* recorded (dashed ivory outline, clock glyph) until it becomes live (coral fill).
- **Ceremonial and heroic, not horror** (rules §6): brass, midnight cobalt, porcelain ivory, one resonant-gold core. **Coral/amber only on live hazards**; friendly markers (numerals, team rims, personal clocks) use ivory/brass/cobalt and never coral.
- **Readable at the game camera with 8 players and 4 patterns live:** every telegraph is the real hit shape with crisp edges; decor dims during warnings; the floor stays a calm canvas; no particle soup. Distinct audio rhythm per mechanic (toll, tick, rewind) is part of the design, even if audio is added later.
- **The phases escalate the look** with the existing phase-look system: closed shell → parted shell and offset rings → open chest, spread halo, brighter core; the final phase is the Final Toll, where the great bell behind the rim finally starts to ring. The death sequence (from BOSS.md: he catches then releases the clapper and the background bell sounds the complete note) is the payoff for the many attempts.
- For each new mechanic in `ULTIMATE.md`, write a **Look** line: which body part or arena piece does it, what the windup/telegraph/resolve look like, and which animation clip or new clip it needs (list new clips for the Bellkeeper 2.0 rig, `prototype/arena-3d/models/blender/bellkeeper2_rig.py`).

## Part 3 — Proof on paper (before any build)
- Extend `tools/encounter-escape/` with `bellkeeper-ultimate-escape-check.ps1`: every mechanic's worst case at 4.2 and 6 m/s with finite stamina, every simultaneous resolution checked as one route, and a **stamina trace per phase** (no route may need more stamina than exists). Fix failures by changing timing/size/position, never immunity.
- Write the "intended solution" for every mechanic (where each player goes, by marker), so bots and humans have a strat.

## Part 4 — Build
- Same tree as v2: an **"Ultimate"** child session owning `feature/bellkeeper-ultimate`, one child per mechanic (cap 8 at once), each in its own worktree, one class per mechanic under `Run/Encounters/Bellkeeper/Ultimate/`.
- Machine rules: one Unity build at a time via `.unity-build-lock` (owner.txt; break only if > 45 min old and idle); build in your own worktree; commit only your own hunks; large binaries via asset packs.
- **GitHub is suspended:** commit locally only, push nothing until Chzy18 says the account is back (memory `last-bell-github-suspended`), then push slowly.
- Practice hooks: `-lastbellUltimate`, `-lastbellUltimatePhase N`, `-lastbellAbility <id>`.
- Autotest per mechanic: spawn/resolve ticks, escape route at 4.2 m/s, still players hit (melee and ranged), solo and N = 2/4/8 versions, the wipe/one-shot rule fires exactly when it should (and never when it shouldn't), boss death cancels everything, phase-start practice works.

## Part 5 — Proof it's beatable, and report
- **Perfect bots must clear it** (like Aurelion v4: "go till the bots win"): 4 scripted perfect AllyBots, and a solo perfect bot, beat it on Hard at the intended DPS. Record a demo video (encode with Stremio's ffmpeg, see memory `last-bell-aurelion-mythic`; videos stay out of git).
- **Imperfect bots must fail**: bots with 0.25 s extra reaction, or that miss one assignment per phase, must wipe — that's the evidence it's Ultimate-hard.
- `ultimate/MORNING-REPORT.md` and `ultimate/GUIDE.md` (a player-facing strat guide per phase, the way Ultimate guides are written): what's playable, how to launch, pass/fail counts, escape results, bot results, decision density per phase vs v2 and Aurelion v4, what's designed/built/tested/human-approved (nothing human-approved until Chzy18 plays it).
