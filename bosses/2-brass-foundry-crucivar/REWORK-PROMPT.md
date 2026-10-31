# Prompt: rework Crucivar, the Furnace Heart (v4, phase-gated)

Paste everything below the line into a fresh Crucivar design session.

---

You are reworking **Crucivar, the Furnace Heart**, the Brass Foundry's area boss in Last Bell (Unity 6000.6.6f1 URP co-op boss rush, repo Risxhb/collab). Chzy18 played ability sets v2 and v3 and wants a full rework that is **at or above Aurelion's difficulty** (Aurelion v4: mythic raid end-boss level).

## How we work: one phase at a time

1. **Design one phase. Stop for Chzy18's acceptance before designing the next.**
   - Start with Phase 1. For each phase, offer **2–4 distinct options**, give a recommendation, then wait.
   - Once a phase is accepted, write it up in full and only then move on.
2. **Order:** P1 → (intermission / add phase, if Chzy18 wants one) → P2 → second form → P3 → P4 / enrage → the full fight sheet.
3. **No code until every phase is accepted.** Then build in one integration branch with one session per mechanic, as in the v2 build plan.
4. Everything is a **proposal** until Chzy18 accepts it. Anything a rule doesn't cover goes under **Open questions**: don't guess.

## Read first

1. `AGENTS.md` and `gpt/08-design-rules.md`. Settled rules win. Note especially:
   - §5's overlap, enrage and wipe rules, and the **Aurelion** and **Bellkeeper Ultimate** exceptions;
   - the telegraph style (§5, Oct 23).
2. `research/rabbit-and-steel-boss-mechanics.md`: §3.0 recovered timings, §4's 20 building blocks, "How R&S stacks two blocks", §5 proposals and §7 Chzy18's answers. **This is the main mechanics source.** Name the R&S block(s) behind every mechanic.
3. The difficulty bar: `aurelion-design/bosses/1-skyward-spire-aurelion/abilities-v4/ABILITIES-v4.md` and its `GUIDE.md`. Read them for how difficulty was built:
   - memory and assignments;
   - three or more overlapping patterns;
   - mechanics that scale every cast;
   - staggered soaks where a chain reaction wipes;
   - a soft enrage (the floor falls), then a hard enrage at 6:30;
   - 10,000 HP solo with co-op scaling `1 + 0.7 × (fighters − 1)`;
   - 3.5 s punishes, 0.1 s gaps;
   - proven beatable by four perfect AllyBots on Hard.
4. Current Crucivar:
   - `bosses/2-brass-foundry-crucivar/BOSS.md`, `abilities-v2/ABILITIES.md`, `ABILITIES-v3.md` (12602a7) and `MORNING-REPORT.md`;
   - code in `unity/Assets/LastBell/Run/Encounters/Crucivar/` (seven abilities exist: Mould Press, Gear Chain, Bellows Draw, The Great Casting, Second Press, Anvil Sweep, Slag Spit; reuse what fits).
5. Look:
   - `gpt/README.md`, `gpt/04-bosses.md`, `gpt/05-arenas.md`;
   - the Brass Foundry arena;
   - `Reference/Telegraph-Style/` (Brass Foundry sheet).

## Difficulty target

- **At or above Aurelion v4 on every level** (Easy, Medium, Hard). Easy may only lengthen warnings and lower damage. It never removes the phase structure.
- **Allowed for every boss since 2026-10-26** (Chzy18 removed the old caps; see `gpt/08-design-rules.md` §5):
  - three or more overlapping damaging patterns;
  - enrage plus scaling pressure (a hard enrage, mechanics that grow every cast, a floor that fills);
  - a failed mechanic may end the fight for the whole party, even solo (with an impossible-to-miss telegraph, introduced first).

  Use them; Aurelion v4 does.
- **Still settled:**
  - no iframes, no grace windows, no post-hit immunity;
  - no one-shots on avoidable hits (heavy hits only);
  - one hit per hazard instance;
  - no teleporting players;
  - no class required, and every party (all duplicates included) can clear;
  - solo stays possible;
  - the boss never heals or gets a new health bar;
  - every route is checked at 4.2 m/s and 6 m/s with finite stamina;
  - melee and ranged are both in danger;
  - no permanently safe spot.
- **Hard comes from:** memory, assignments, layering, scaling and wipes. Never from shorter windows than the rules allow, and never from HP bloat. Set HP from measured DPS, as Aurelion did.

## Fantasy brief: a foundry on fire

Fire has to be everywhere and readable:
- molten pours and channels;
- slag pools that cool;
- crucible spills;
- bellows blasts and backdraft;
- ember rain;
- a rising lava rim;
- quench steam;
- furnace vents;
- casting moulds;
- anvils;
- the hammer (RIGHT hand) and crucible (LEFT hand).

Constraints:
- **Scenery is never a surprise lethal edge.** The perimeter lava only hurts through telegraphed gameplay, such as a rising lava ring with its own warning.
- **Lava dims during warnings** (dark red-brown), per the telegraph style.
- **Coral/amber only on real hazards.**
- Molten pools always expire, except a floor-fill enrage if one is granted.

## Chzy18's add-phase idea (develop it as one option, not a requirement)

**The idea:** turrets rise out of the lava and fire bullet-hell patterns. Each turret has a mob tied to it somewhere in the arena. Killing the mob stops its turret, which sinks back into the lava.

Fit it to the rules like this:
- **The mobs must be Crucivar's own parts, not trash mobs** (settled: every enemy is a boss; boss-part summons are allowed). Examples: molten castings poured from his crucible, or slag-wardens he forged. Use R&S **painshare** (block 20): damage to a mob also damages Crucivar.
- **Bullets** follow R&S block 15 (marching and enlarging bullets, `bp_fire_*`).
  - Every bullet stream draws its real path on the floor first: lanes, a ring wave, or a spiral arm.
  - It gets at least the 2.0 s warning floor unless Chzy18 grants an exception. R&S bullet lines go under 2 s only with a dot hitbox. Ask before going below.
- **Turret–mob links** must be readable: a tether or a matching numeral/colour (R&S blocks 7 and 9, using friendly identity colours).
- Mobs must be killable by **melee and ranged**, so place them where a Vanguard can reach them.
- **Solo rule:** fewer turrets, or turrets that come up in sequence.
- **N = 2 / 4 / 8:** turret count scales; give players spatial jobs.
- **Failure:** state what happens if the mobs aren't killed in time, for example the turrets' fire floods the floor as a wipe. Its telegraph must be impossible to miss.
- **Placement:** an intermission (like Aurelion's Total Eclipse) or the second-form transition.

## What each phase write-up contains

1. **Name, HP band, theme,** and how its difficulty compares with Aurelion's phase at the same point.
2. **Each mechanic:**
   - shape and size;
   - R&S block(s);
   - Strike or Ground;
   - warning time;
   - damage at Medium;
   - melee threat and ranged threat;
   - escape route;
   - solo rule and N = 2 / 4 / 8 rules;
   - failure cost (heavy hit / wipe);
   - how it scales across casts;
   - fire look.
3. **The phase timeline:**
   - a second-by-second loop table;
   - overlap count per beat;
   - gaps;
   - the punish window (a 3–4 s major recovery is still required);
   - where each new mechanic is introduced before it's layered.
4. **Escape check:** extend `tools/encounter-escape/crucivar-escape-check.ps1` and report every worst case on Easy, Medium and Hard. PASS needs ≥ 0.5 s margin.
5. **Hard's named extra mechanic** for the phase, if any. Easy's changes.
6. **Conflicts** with settled rules and what you did.
7. **Open questions.**

## When all phases are accepted

- Full fight sheet:
  - phase HP bands;
  - the second-form change (settled: harmless, same bar);
  - the enrage, if granted;
  - boss HP and co-op scaling.
- Build plan in the v2 style:
  - one session per mechanic;
  - the shared build lock;
  - autotests for every mechanic: spawn/resolve ticks, the still-melee and still-ranged checks, solo and N = 2/4/8, death mid-windup, Easy/Hard;
  - a phase-plan test;
  - **AllyBot proof that four perfect bots beat it on Hard**, as for Aurelion;
  - a solo route check.
- Keep v3 reachable with `-lastbellAbilitySet v3`, and make the rework the default.
- Commit locally as `git -c user.name=Chzy18 -c user.email=345140526+Chzy18@users.noreply.github.com`.
- **Don't push while the GitHub account is suspended.**
