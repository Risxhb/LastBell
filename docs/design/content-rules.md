# Last Bell: design rules for new content

**Read this before you design or change any boss, miniboss, attack, arena, class ability or game system.** These rules come from decisions Chzy18 has already made. They override anything older, including older wiki pages, old reference-sheet labels and earlier packets.

Last updated October 25, 2026 by chzy18/claude. The latest change: the Bellkeeper is fought only in its Ultimate version (§1, §5). Before that came the melee-and-ranged danger rule (§5) and Chzy18's answers from the Rabbit and Steel study (`docs/research/rabbit-and-steel/rabbit-and-steel-boss-mechanics.md` §7 on branch `docs/rabbit-and-steel-study`). Each rule is marked:
- **Settled:** decided. Don't change it and don't re-ask about it.
- **Provisional:** current working value. Use it, but you can propose changes.

## 0. How to use these rules

1. **Settled rules win.** If a request, a collaborator's note, or an older doc conflicts with a settled rule, follow the rule. Say plainly in your output what conflicted and that you kept the rule. Don't silently drop the request, and don't silently adopt it either. *(Settled)*
2. **New designs are proposals.** Label new bosses, attacks, lore and systems "proposal" until Chzy18 approves them. *(Settled)*
3. **Don't invent mechanics nobody asked for.** That includes new resources, new defensive abilities, revive rules, upgrades and shops. If a design needs one, list it as an open question. *(Settled)*
4. **Ask instead of guessing.** If something here doesn't cover your case, list it under "Open questions" in your output.

## 1. What the game is

- **Settled:** top-down co-op action **boss rush** with roguelite runs. Every enemy is a boss: no normal enemies or trash mobs.
  - **Settled:** parts the boss spawns count as part of the boss, not as trash mobs. One example is small bells whose hits also damage the boss.
- **Settled:** design and tune for **co-op**, 1–8 players. Solo must still be possible, but don't sacrifice the co-op experience for it.
- **Settled:** campaign of four areas, 13 encounters:
  - Areas 1–3 each have **3 minibosses, then 1 area boss**, all in that area's one shared arena:
    - Area 1: The Skyward Spire
    - Area 2: The Brass Foundry
    - Area 3: The Verdant Chimes
  - Area 4, **The Shattered Hour**, has **one final boss** (the Bellkeeper) in its own arena, The Throne of the Final Toll. **Settled (Chzy18, 2026-10-25):** the Bellkeeper is fought **only in its Ultimate version**; the normal v2 fight is retired.
- **Settled:** run flow within an area is miniboss 1 → miniboss 2 → miniboss 3 → intermission (future shop, not functional yet) → area boss.
- **Settled:** Unity URP is the engine. Gameplay rules live in shared C#, not in art or animation.

## 2. Movement, dodging and getting hit

- **Settled: no invincibility frames anywhere.**
  - Dashing, dodging and jumping never make a player immune.
  - A dash only avoids an attack by physically carrying you out of the shape before it resolves.
  - There's no blanket immunity after being hit, and no "grace window" on a timed dodge.
- **Settled:** each hazard instance can hit a given player **at most once**. This prevents multi-hit chains.
- **Settled:** jumping is a real action and can clear low attacks, such as a jumpable bar. It isn't general immunity.
- **Settled:** no block, parry, counter, auto-target or lock-on. Every ability aims at the mouse cursor.
- **Settled:** bosses never teleport players. Players walk to their spots.
- **Provisional:** a boss may apply a temporary slow or root, but only if every escape route still works at 4.2 m/s while it's active.
- **Provisional numbers:**
  - Movement speeds: walk 6 m/s, sprint 9 m/s, dash 3 m in 0.2 s, jump about 0.8 m over 0.6 s.
  - Stamina: 100 total. Sprint drains 25/s, a jump costs 20, a dash costs 30. It regenerates at 20/s after a 0.75 s pause.
  - On hit: 0.25 s stun and about 1.5 m knockback. A boss may use no knockback if knockback would pin players against walls.
  - Validate escape routes against **both** speeds: 4.2 m/s prototype walk and 6 m/s design walk.

## 3. Health, potions and run resets

- **Settled:** health refills **before every boss after the first**.
- **Settled: potions do not reset between minibosses.**
  - Potions you spend on miniboss 1 stay spent through miniboss 2 and 3.
  - Health and potions **both** refill before the area's final boss.
  - A fresh run starts full.
- **Provisional:** 5 potions per refill, +40 HP each, 1 s between drinks, key F.
- **Settled:** stamina, cooldowns and class state (such as Opening, Harmony, bells and seals) reset before each encounter.
- **Settled:** if the player goes down on the same tick the boss dies, **defeat wins**.
- **Sidelined, don't design around it:** Resonance Reclaim (earning back lost HP by playing well).

## 4. Classes

- **Settled:** three starting classes with approved kits (#31, #36, #41). Don't redesign them inside a boss or arena brief.
  - **Vanguard:** melee sword and shield.
    - Abilities: Steel Rhythm (3-hit basic), Sundering Blow, Last Word (heavy finisher, about 0.8 s windup), hold-to-Brace, Clockstep dash.
  - **Chimesinger:** ranged sound-magic damage dealer with an immediate burst heal. This class replaced the old "Healer".
    - Abilities: Resonant Note, Bellstrike, Harmonic Refrain, Crescendo, Mending Chorus (heal), Harmonic Step dash.
  - **Arcanist:** ranged time magic with timed setup.
    - Abilities: Clockbolt, Time Seal, Arc Lance, Convergence, Temporal Anchor, Clockstep dash.
- **Settled:** the support class must keep **real damage output** as well as healing. A class that only heals is never acceptable.
- **Settled:** no encounter may **require** a specific class, a healer, Brace or any one ability. Every party, including all duplicates of one class, must be able to clear.
- **Settled:** Brace is **partial frontal mitigation** (about −60 %), not a block or immunity.
  - It works on **Strike** hazards: cones, lanes, sweeps, bars.
  - It never works on **Ground** hazards: rings, donuts, chime circles, ground eruptions.
  - Tag every new attack as Strike or Ground.
- **Settled:** phase changes don't clear player setup. Seals, bells and inscriptions persist and their timers keep running.
- **Provisional:** all class damage numbers and cooldowns are prototype values.

## 5. Bosses: encounter rules

Each rule below is settled unless it says otherwise.

- **Readable, learnable patterns over randomness.** Difficulty comes from learned sequences, not from guessing or from piles of HP.
- **Telegraphs:**
  - Every damaging attack has a ground telegraph built from the real hit shape, not painted art.
  - Hostile telegraphs are coral or amber with crisp edges.
  - Friendly effects never use those colours.
  - **Style reference (Settled, Chzy18 2026-10-23):** the approved v2 telegraph sheets in Chzy18's local `Reference/Telegraph-Style/`. These aren't in the repo yet, because of the GitHub suspension. Each area has its own motif inside the shape, an outward-fill countdown over a fixed outline, a short flash on resolve, and scenery that dims during warnings. Friendly effects are thin, dotted and unfilled.
  - **Provisional:** markers that show who a player is or which team they're on:
    - ivory numerals over heads for order (I, II, III…)
    - brass and cobalt rims for two teams
    - They show identity only and never hurt. See `docs/research/rabbit-and-steel/marker-colours-mockup.svg` on branch `docs/rabbit-and-steel-study`.
  - Decorative floor art must never look like a hazard.
- **Every attack needs a real escape route**, tested with finite stamina and no iframes, for melee and ranged players.
- **Melee and ranged must both be in danger** (Chzy18, 2026-10-22: "ranged is too easy right now"). This applies to all bosses and minibosses.
  - **Most mechanics threaten both.** At least three quarters of a boss's core attacks must be dangerous to a player hugging the boss *and* to a player at long range (8 m or more, up to the arena edge).
  - **Something punishes staying at range.** Every boss and miniboss has at least one attack that does this, for example:
    - hazards anchored to the arena edge
    - attacks that target the farthest player
    - sweeps that come in from the edge
    - temporary edge limits that push players inward
    - a soak or safe zone near the boss
  - **No permanently safe spot.** The outer ring and the area behind the boss are never safe for a whole phase. Ranged players must reposition several times per phase, and time their stationary casts around it.
  - **Melee keeps close-range pressure:** body slams, point-blank rings, cleaves around the boss.
  - Each attack's brief states how it threatens melee and how it threatens ranged. Tests check that a player standing still next to the boss and one standing still at long range both get hit.
  - This is about danger, not class requirements: every class must be able to survive each mechanic by moving.
- **At most two damaging patterns overlap,** and only after the route is checked.
- **Punish windows:**
  - Every boss has at least one **3–4 s major recovery**, long enough for Vanguard's heavy attack and the casters' stationary casts.
  - Don't uniformly speed attacks up to make a fight harder.
- **No enrage timers.** No soft-enrage that slowly fills the floor. Pools and zones always expire.
- **Exception, Bellkeeper Ultimate (Chzy18, 2026-10-23: "forget those rules").** The Bellkeeper's **Ultimate** fight (since 2026-10-25 the only Bellkeeper fight: the normal v2 fight is retired) is tuned to Final Fantasy XIV Ultimate difficulty. The limits above don't hold it back: any number of overlapping patterns (as long as a route exists), a hard enrage plus per-phase DPS checks, party-wide wipes on a failed mechanic even solo, and **one-shots** for mechanic failures (and heavy hits where an Ultimate would). 10–14 min, 5 phases plus 2 transitions; practice may start at any phase. **Still settled for it:** every mechanic has a real way to survive it, no player teleports, no class is required, the boss never heals or gets a new health bar, mouse aiming, no iframes.
  - **Settled (Chzy18, 2026-10-25):** the Ultimate must stay **soloable**. Solo is held to the same standard as every other fight: possible with good play, even if it takes many attempts. It also keeps the **Easy/Medium/Hard** levels like every other boss.
- **Phases:**
  - **Provisional:** area bosses have 3 HP phases, minibosses have 2.
  - **Settled:**
    - The boss stays targetable through a phase change.
    - A transition waits until the current attack finishes, then plays a short harmless transformation.
    - The boss never heals or gets a new health bar.
    - Crossing two thresholds with one burst queues only one transition.
    - **Area bosses and the final boss** change into a **second form** partway through the fight. The change follows the rules above: a harmless transformation, no heal and the same health bar. Minibosses don't get a second form.
- **The boss dying mid-windup** cancels all its pending hazards in that same tick.
- **Difficulty levels — Settled (Chzy18, 2026-10-22): three levels, Easy, Medium and Hard**, chosen on the run menu. This replaces the earlier four provisional tiers (First Chime, Steady Ticking, Striking Hour, Midnight Toll).
  - Write your numbers for **Medium**, the baseline. Old "Steady Ticking" numbers count as Medium.
  - The run copies the level at start; it can't change mid-run, and a retry keeps it. Encounter code reads it from `CombatWorld.Difficulty` (`feature/run-loop`).
  - **Provisional:** in the current build the level only scales damage taken from boss attacks (Easy ×0.6, Hard ×1.3). Each boss may add its own per-level changes: Easy with longer warnings, Hard with **one named extra mechanic**, not just faster or harder-hitting attacks. Every level's windows must still pass the escape check.
- **Co-op scaling** comes from spreading targets and giving players spatial jobs, not just multiplying HP.
- **Co-op mechanics:**
  - **Settled:** every co-op attack states its **solo (1-player) behaviour** next to the attack, for example "solo: the line becomes a normal lane you dodge".
  - **Settled: shared soaks** (a ring that needs N players inside):
    - The damage is cut into N shares.
    - Each player inside takes one share.
    - Missing players' shares fall on whoever is inside.
    - An **empty soak wipes the party**. Its escape route is "get in", so its telegraph must be impossible to miss: a loud chime and a large numeral. Introduce it before a boss relies on it.
    - Tune the total so one player taking every share still survives from full HP.
  - **Provisional:** a dash can break a player-to-player tether.
  - **Provisional:** a mark passed between players skips players who are downed.
- **Provisional encounter length:**
  - Minibosses 60–150 s, area bosses about 3–4 min. This range is still being tuned.
  - Test builds use shortened HP.
  - Set HP from measured DPS, not by guessing.

## 6. Bosses: look

- **Settled:** ancient ceremonial clockwork guardians, heroic and intimidating, **not horror**.
  - Bell, clock, pendulum and orrery motifs.
  - Circular or vertical silhouettes. Heroes read as wedges.
- **Settled:** at game-camera scale you can instantly see the head, core, active limb and facing.
- **Settled:** palette is weathered brass, midnight-cobalt cavities, porcelain-ivory masks, and one resonant-gold core. **Coral only when attacking.**
- **Settled:** a model sheet shows front, side, back and three-quarter views on one baseline, with scale against a 1.8 m human.
  - State the height in the text. If the sheet's proportions disagree with your text, call it out.

## 7. Arenas

- **Settled:** follow the four-area art handoff (`docs/arena-references/LAST-BELL-ARENA-ART-HANDOFF/` on branch `handoff/four-area-arenas-20261021`) and the art direction doc (`docs/arena-references/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md`, same branch).
- **Settled:** fixed high-oblique camera, about 50–60°.
  - The playable floor stays **broad, flat and unobstructed** for up to 8 players.
  - Tall architecture sits outside the playable area or fades when it blocks the view.
- **Settled:** every arena shares the run layout:
  - centre origin
  - entry to the south (+z)
  - floor at y = 0
  - walkable radius at least 13.5 m
  - a play bound per arena, just inside that arena's wall, so players can walk up to it (Chzy18, Oct 21; it replaces the shared 11.5 m bound). Values are in `Run/RunMaps.cs` (`feature/run-loop`): test map ±12.5 m square, Skyward Spire 13.9 m, Brass Foundry 13.7 m, Verdant Chimes 13.5 m, Shattered Hour 13.0 m. Boss positions stay inside 9.5 m.
- **Settled:** scenery is never a surprise lethal edge. Only gameplay code makes something dangerous.
- **Settled:** the Bellkeeper key-art arena is the **test map** and may become a tutorial.

## 8. Controls

- **Settled:**
  - Movement: WASD to move, the mouse to aim.
  - Abilities: 1 basic (or LMB), then 2, 3 and 4.
  - 5 or RMB dashes.
  - 6 is the extra ability: Chimesinger's Mending Chorus, Arcanist's Temporal Anchor.
  - Other keys: F potion, Space jump, Shift sprint.
- **Settled:** don't use J/K/L/Q layouts. Gamepads aren't supported yet.

## 9. Style hard rules (all content)

- Original designs only. Windblown and Hades set the quality bar, but don't copy them.
- Keep existing characters exactly on-model to their sheets.
- No soot, rust realism, photorealism, gore, horror, neon bloom or comic outlines.
- Ability and dash icons never show an invulnerability bubble.

## 10. What every boss or arena brief should include

1. Status line: "proposal" plus the sources you read, this file included.
2. Identity and look, with height and scale.
3. Attacks:
   - shape and size
   - melee threat and ranged threat
   - Strike or Ground tag
   - warning time
   - damage at Medium
   - escape route
   - solo (1-player) behaviour, for co-op attacks
   - punish window
4. Phase plan using the rules in §5.
5. Hard mechanic (the one named extra mechanic on Hard).
6. **Conflicts:** anything in the request that broke a settled rule, and what you did instead.
7. **Open questions** for Chzy18.

## 11. Repository and publishing

- **Settled (Chzy18, 2026-10-23): never commit or publish third-party game material.** That covers:
  - decompiled or disassembled code
  - extracted game files, data tables, string or text tables, file and script indexes, symbol or function-name dumps
  - ripped art, audio or models
  
  This applies to every game, Rabbit and Steel and Windblown included. Keep such research evidence **local and gitignored**. Only our own written analysis (mechanic descriptions, timings in our own words and numbers, design proposals) may be committed.
- **Settled:** research folders are never pushed without Chzy18 reviewing them first, even when pushing is otherwise allowed.
- **Settled:** large binaries (GLB, big textures, video) go in asset packs, not git (`docs/asset-packs.md`).
- **Settled (Risx, 2026-10-26): GitHub is not retired. The repository lives on both GitHub and Azure DevOps.** This replaces the 2026-10-25 note that GitHub was retired.
  - GitHub: https://github.com/Risxhb/LastBell (remote `origin`). Work is still pushed there, and it hosts the web demo. Asset packs download from its Releases.
  - Azure DevOps: https://dev.azure.com/ChzandZum/_git/Last%20Bell (project "Last Bell", remote `azure`). Push there once a piece of work is finished and tested.
  - The main branch is Risx's `feature/confrontation-menu`.
  - The lane board and the Azure wiki get major, finalized updates only.
- **Settled (Chzy18, 2026-10-23): push to Azure rarely, in bigger batches.**
  - Commit locally as often as you like.
  - Push only when a piece of work is finished and tested, or when Chzy18 asks. That's at most about one push per lane per day, and never a push per commit.
  - Use one branch and one Azure PR per boss or feature into `feature/confrontation-menu`, not one per ability or sub-task. Combine sub-task branches locally first.
  - Start from the latest Azure branch, build and run the autotest (with the Unity lock) before pushing, never force-push, and never push straight to `feature/confrontation-menu`.
  - Keep PR comments and board edits few and deliberate, with no automated chatter or status pings.

## Where the details live

- Wiki [Decisions and scope](https://github.com/Risxhb/collab/wiki/Decisions-and-Scope) and [Game design](https://github.com/Risxhb/collab/wiki/Game-Design).
- Run loop rules: `docs/run-loop/README.md` on branch `feature/run-loop` ([PR #42](https://github.com/Risxhb/collab/pull/42)).
- Bellkeeper spec: `docs/design/bellkeeper.md` on branch `docs/bellkeeper-spec` ([PR #16](https://github.com/Risxhb/collab/pull/16)).
- Arena handoff: `docs/arena-references/LAST-BELL-ARENA-ART-HANDOFF/LAST-BELL-SHATTERED-HOUR-ARENA.md` on branch `handoff/four-area-arenas-20261021`.

Keep this file current: when Chzy18 settles a new gameplay rule, add it here in the same change that records the decision.
