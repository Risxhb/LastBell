# Last Bell: design rules for new content

**Read this before you design or change any boss, miniboss, attack, arena, class ability or game system.** These rules come from decisions Chzy18 has already made. They override anything older, including older wiki pages, old reference-sheet labels and earlier packets.

Synced on 8 November 2026 against Chzy18's design-rules pack (`gpt/08-design-rules.md`, workspace copy) and the workspace `AGENTS.md`, plus the recorded revive decisions of 4–5 November. This file replaces the 25 October copy; it is written as a current baseline, not as a changelog. If this file and a newer decision by Chzy18 disagree, the newer decision wins: fix this file.

Each rule is marked:
- **Settled:** decided. Don't change it and don't re-ask about it.
- **Provisional:** current working value. Use it, but you can propose changes.

## 0. How to use these rules

1. **Settled rules win.** If a request, a collaborator's note, or an older doc conflicts with a settled rule, follow the rule. Say plainly in your output what conflicted and that you kept the rule. Don't silently drop the request, and don't silently adopt it either. *(Settled)*
2. **New designs are proposals.** Label new bosses, attacks, lore and systems "proposal" and list open questions instead of guessing. *(Settled)*
3. **New mechanics may be proposed, and built without waiting.** New resources, defensive abilities, upgrades, shops and similar systems are welcome. Build your recommended version as its own undoable commit, send it to the review inbox marked "done as recommended, review later", and undo it if Chzy18 rejects it (Chzy18, 4 Nov: "I don't want anything held up because I didn't accept"). This never overrides another settled rule. *(Settled)*
4. **Keep working without a go-ahead.** Work through your lane's list without waiting to be told "go". Stop only for things Chzy18 alone can do: playtests, publishing asset packs, anything needing a token you don't have, and accept/reject decisions that can't be undone. *(Settled, 2 Nov)*
5. **Review inbox.** Items for Chzy18's review go to the review inbox (a Claude session; its id is in the workspace `AGENTS.md`) with the lane, a one-line title, a short summary, evidence paths and one accept/reject question. *(Settled)*
6. **Ask instead of guessing.** If something here doesn't cover your case, list it under "Open questions" in your output.

## 1. What the game is

- **Settled:** top-down co-op action **boss rush** with roguelite runs. Every enemy is a boss: no normal enemies or trash mobs.
  - Parts the boss spawns count as part of the boss, not as trash mobs (for example small bells whose hits also damage the boss).
- **Settled:** design and tune for **co-op, 1–4 players** (Chzy18, 1 Nov). Solo must still be possible, but don't sacrifice the co-op experience for it. Mechanics, HP scaling and tests cover 1, 2, 3 and 4 players only. Don't design, tune or test for more than 4.
- **Settled:** campaign of four areas, 13 encounters:
  - Areas 1–3 each have **3 minibosses, then 1 area boss**, all in that area's one shared arena:
    - Area 1: The Skyward Spire
    - Area 2: The Brass Foundry
    - Area 3: The Verdant Chimes
  - Area 4, **The Shattered Hour**, has **one final boss**, the Bellkeeper, in its own arena, The Throne of the Final Toll. The Bellkeeper is fought **only in its Ultimate version**; the normal v2 fight is retired.
- **Settled: current main-boss versions.** Aurelion **v4**, Crucivar **v4**, Sylvara **v3** and the **Bellkeeper Ultimate**. Older versions are retired; don't design against them.
- **Settled:** run flow within an area is miniboss 1 → miniboss 2 → miniboss 3 → shop → area boss.
- **Settled (29 Oct): items come only from a shop.** The intermission before each main boss is a **shop**, where players spend **Chimes**, a run currency. There's **no free item pick after minibosses**. Items last the whole run and are gone when it ends. Item design and balance belong to the Items lane; the catalogue is `docs/design/items/CATALOGUE.md`. Still open: prices, sharing Chimes in co-op, a final shop before the Bellkeeper, and the Toll ladder.
- **Settled:** Unity URP is the engine. Gameplay rules live in shared C#, not in art or animation.

## 2. Movement, dodging and getting hit

- **Settled: no invincibility frames anywhere.**
  - Dashing and dodging never make a player immune.
  - A dash only avoids an attack by physically carrying you out of the shape before it resolves.
  - There's no immunity after being hit, after being revived, and no "grace window" on a timed dodge.
- **Settled:** each hazard instance can hit a given player **at most once**.
- **Settled (2 Nov): no jumping.** Players have no jump. Never design an attack, hole or bar that needs one.
- **Settled:** no block, parry, counter, auto-target or lock-on. Every ability aims at the mouse cursor.
- **Settled:** bosses never teleport players. Players walk to their spots.
- **Provisional:** a boss may apply a temporary slow or root, but only if every escape route still works at 4.2 m/s while it's active.
- **Provisional numbers:**
  - Movement: walk 6 m/s, sprint 9 m/s, dash 3 m in 0.2 s.
  - Stamina: 100 total. Sprint drains 25/s, a dash costs 30. It regenerates at 20/s after a 0.75 s pause.
  - On hit: 0.25 s stun and about 1.5 m knockback. A boss may use no knockback if knockback would pin players against walls.
  - Validate escape routes against **both** 4.2 m/s (prototype walk) and 6 m/s (design walk).

## 3. Health, potions, revives and run resets

- **Settled:** health refills **before every boss after the first**.
- **Settled: potions do not reset between minibosses.**
  - Potions spent on miniboss 1 stay spent through miniboss 2 and 3.
  - Health and potions **both** refill before the area boss.
  - A fresh run starts full.
- **Provisional:** 5 potions per refill, +40 HP each, 1 s between drinks, key F.
- **Settled:** stamina, cooldowns and class state (such as axes, glyphs, Harmony, bells and seals) reset before each encounter.
- **Settled (4–5 Nov): revives.**
  - At **0 HP** a player is **downed**: they can't move, attack, dash or drink, and they don't count as standing for soaks, spreads or targeting.
  - A standing ally who **stands still within 1.5 m** for **3 s** revives them; no key is needed. Moving or acting pauses the channel, and **any hit on the reviver resets it to 0**. Two revivers are no faster.
  - The revived player gets up with **40 HP** and full stamina, and keeps their potions. **No immunity** afterwards.
  - A player not revived within **30 s** is **dead** for the rest of the fight. Everyone is back at full HP for the next fight.
  - The party loses when nobody is standing. Solo, going down ends the attempt.
  - Every class revives the same way; no class has a revive ability, and no encounter may depend on one.
- **Settled:** if the player goes down on the same tick the boss dies, **defeat wins**.
- **Sidelined, don't design around it:** Resonance Reclaim (earning back lost HP by playing well).

## 4. Classes

- **Settled:** three starting classes: **Tollbreaker, Chimesinger and Arcanist**. Don't redesign them inside a boss or arena brief.
  - **Tollbreaker** (in the game since 1 Nov; the retired Vanguard is deleted, with no later unlock): a mid-range berserker who throws two war axes. No defensive ability; survival comes from the dash and positioning.
    - **Axes are his resource:** an axe in flight or lodged can't be thrown until it returns. Returning axes come back sooner when he stands close, so close range raises his damage.
    - Abilities: Twin Hurl (LMB; returning axes, hold to lodge one), Grinding Toll (2; the axe spins in place at the cursor for 2 s, no bounce or seeking), Splitting Throw (3; lodges in the boss or floor for 6 s), Call of Iron (4; recalls lodged axes in a line, damage grows with distance), Bell Frenzy (5; a 4 s empowerment), and the shared dash (RMB).
  - **Chimesinger:** ranged sound-magic damage dealer with an immediate burst heal. Abilities: Resonant Note, Bellstrike, Harmonic Refrain, Crescendo, Mending Chorus (5, heal), Harmonic Step dash.
  - **Arcanist:** ranged arcane glass cannon (Risx's redesign, accepted 27 Oct). No defensive ability; survival comes from Blink and positioning.
    - Abilities: Magic Missile (LMB; tap, or hold to charge up to 3), Disintegrate (2; steered channelled beam that **builds glyphs**), Rune Sigil (3; 0.8 s skill shot, **spends 1 glyph**), Arcane Comet (4; 3 s skill shot, his biggest hit, **spends 1 glyph**), Arcane Surge (5; empowerment that **spends every glyph**), Blink (RMB; the shared dash with a teleport look).
    - Glyphs: max 3, no decay. Sigil and Comet without a glyph deal 35%. There is no Astral Orb pet.
    - His spells use violet shades and never filled glowing floor discs, so boss warnings must not use those violets.
    - Full design: `docs/design/arcanist-redesign.md`.
- **Settled:** the support class keeps **real damage output** as well as healing. A class that only heals is never acceptable.
- **Settled:** no encounter may **require** a specific class, a healer or any one ability. Every party, including all duplicates of one class, must be able to clear.
- **Settled:** there is no Brace or other defensive ability today. Still **tag every attack** as **Strike** (cones, lanes, sweeps, bars) or **Ground** (rings, donuts, chime circles, eruptions); the tag has no effect today.
- **Settled:** phase changes don't clear player setup. Seals, bells, lodged axes and inscriptions persist and their timers keep running.
- **Provisional:** all class damage numbers and cooldowns are prototype values.

## 5. Bosses: encounter rules

Each rule below is settled unless it says otherwise.

- **Readable, learnable patterns over randomness.** Difficulty comes from learned sequences, not from guessing or piles of HP.
- **Harder means more to dodge, not more damage.** To make a fight harder, add things to dodge. Don't raise hit damage, and don't uniformly speed attacks up.
- **Telegraphs:**
  - Every damaging attack has a ground telegraph built from the real hit shape, not painted art.
  - Hostile telegraphs are coral or amber with crisp edges; friendly effects never use those colours and are thin, dotted and unfilled.
  - Each area has its own motif inside the shape, an outward-fill countdown over a fixed outline, a short flash on resolve, and scenery that dims during warnings (telegraph style sheets, v2).
  - **Provisional:** identity markers (ivory numerals over heads, brass and cobalt team rims) show identity only and never hurt.
  - Decorative floor art must never look like a hazard.
- **Every attack needs a real escape route**, tested with finite stamina and no iframes, for close and ranged players.
- **Close and ranged players must both be in danger** (22 Oct). This applies to all bosses and minibosses.
  - At least three quarters of a boss's core attacks threaten a player hugging the boss *and* one at long range (8 m or more).
  - Every boss and miniboss has at least one attack that punishes staying at range (edge hazards, farthest-player targeting, sweeps from the edge, temporary edge limits, a soak near the boss).
  - No permanently safe spot: the outer ring and the area behind the boss are never safe for a whole phase.
  - Close-range pressure still matters with no melee class, because the Tollbreaker gains damage by standing close.
  - Each attack's brief states how it threatens close and ranged players. Every class must be able to survive each mechanic by moving.
- **Revivers and downed players:** no aimed mechanic picks a downed player or an active reviver. Hazards already in play can still hit them, and they get no immunity.
- **Overlap, enrage and wipes: no fixed limits** (26 Oct, every boss).
  - Any number of overlapping damaging patterns, as long as a real route through them exists and is checked.
  - Enrage and scaling pressure are allowed: hard enrages, growing mechanics, pools or floor that last for a phase.
  - A failed mechanic may end the fight for the whole party, even solo, if its telegraph is impossible to miss and the fight introduces it first.
  - Still settled: no one-shots on avoidable hits (so a potion can save you), except the Bellkeeper Ultimate; every pattern has a real escape route while it runs.
- **Punish windows:**
  - Every boss has at least one **3–4 s major recovery**, long enough for each class's biggest hit (Bell Frenzy, Arcane Comet and Disintegrate, Crescendo).
  - **Exception, Sylvara v3:** her bullet-hell fight has **no punish windows in any phase**; players attack while dodging.
- **Boss-specific settings:**
  - **Aurelion v4:** no one-shots; an avoidable hit costs a heavy share of HP. Co-op first, **5–7 min**, 4 phases (exception to the length below).
  - **Bellkeeper Ultimate:** tuned to Ultimate difficulty (many attempts to clear), with a target clear of **about 6 minutes**, 5 phases plus 2 transitions; practice may start at any phase. Allowed: any overlap with a route, a hard enrage and per-phase DPS checks, party-ending mechanic failures, and one-shots for failures and Ultimate-style heavy hits.
    - Still required: a real way to survive every mechanic, no player teleports, no class required, no boss heal or new health bar, mouse aiming, no iframes.
    - **Soloable with every class on Easy, Medium and Hard.** Its **baseline is Hard**; Easy and Medium are scaled down from it.
- **Phases:**
  - **Provisional:** area bosses have 3 HP phases, minibosses 2 (named boss plans above take precedence).
  - The boss stays targetable through a phase change; a transition waits for the current attack, then plays a short harmless transformation.
  - The boss never heals or gets a new health bar. Crossing two thresholds with one burst queues one transition.
  - Area bosses and the final boss change into a **second form** partway through, under the same rules. Minibosses don't.
- **The boss dying mid-windup** cancels all its pending hazards in that tick.
- **Difficulty: Easy, Medium and Hard**, chosen on the run menu.
  - Write numbers for **Medium**, the baseline (except the Bellkeeper Ultimate: Hard).
  - The run copies the level at start; it can't change mid-run and a retry keeps it. Code reads `CombatWorld.Difficulty`.
  - **Provisional:** today the level scales boss damage only (Easy ×0.6, Hard ×1.3). Per-boss changes add longer warnings on Easy and **one named extra mechanic** on Hard. Every level must pass the escape check.
- **Co-op scaling** comes from spreading targets and giving players spatial jobs, not just multiplying HP.
- **Co-op mechanics:**
  - Every co-op attack states its **solo behaviour** next to it.
  - **Shared soaks:** damage is split into N shares; each player inside takes one, missing shares fall on those inside, and an **empty soak wipes the party** (loud chime, large numeral, introduced first). One player taking every share must survive from full HP.
  - **Provisional:** a dash can break a player-to-player tether; a passed mark skips downed players.
- **Provisional encounter length:** minibosses 60–150 s, area bosses about 3–4 min (named exceptions above). Test builds use shortened HP; set HP from measured DPS.

## 6. Bosses: look

- **Settled:** ancient ceremonial clockwork guardians, heroic and intimidating, **not horror**. Bell, clock, pendulum and orrery motifs; circular or vertical silhouettes (heroes read as wedges).
- **Settled:** at game-camera scale you can instantly see the head, core, active limb and facing.
- **Settled:** weathered brass, midnight-cobalt cavities, porcelain-ivory masks and one resonant-gold core. **Coral only when attacking.**
- **Settled:** a model sheet shows front, side, back and three-quarter views on one baseline, scaled against a 1.8 m human, with the height stated in text.

## 7. Arenas

- **Settled:** follow the four-area arena art handoff and the art direction.
- **Settled:** fixed high-oblique camera, about 50–60°. The playable floor stays **broad, flat and unobstructed for up to 4 players**; tall architecture sits outside it or fades when it blocks the view.
- **Settled:** shared run layout: centre origin, entry to the south (+z), floor at y = 0, walkable radius at least 13.5 m, and a per-arena play bound just inside the wall (`unity/Assets/LastBell/Run/RunMaps.cs`). Boss positions stay inside 9.5 m.
- **Settled:** scenery is never a surprise lethal edge, and arenas have no holes or bars that need a jump. Only gameplay code makes something dangerous.
- **Settled:** the Bellkeeper key-art arena is the **test map**.

## 8. Controls

- **Settled, shared by every class:** WASD to move, mouse to aim; basic attack **LMB**; dash **RMB**; abilities **2, 3, 4, 5**; keys **1 and 6 unused**; F potion; Shift sprint; **Space unused** (no jump).
- **Settled:** no J/K/L/Q layouts. Gamepads aren't supported yet.

## 9. Style hard rules (all content)

- Original designs only. Windblown and Hades set the quality bar; don't copy them.
- Keep existing characters exactly on-model to their sheets.
- No soot, rust realism, photorealism, gore, horror, neon bloom or comic outlines.
- Ability and dash icons never show an invulnerability bubble.

## 10. What every boss or arena brief should include

1. Status line: "proposal" plus the sources you read, this file included.
2. Identity and look, with height and scale.
3. Attacks: shape and size; close and ranged threat; Strike or Ground tag; warning time; damage at Medium; escape route; solo behaviour for co-op attacks; punish window (Sylvara v3: "none, settled exception").
4. Phase plan using §5.
5. Hard mechanic: the one named extra thing to dodge on Hard.
6. **Conflicts:** anything in the request that broke a settled rule, and what you did instead.
7. **Open questions** for Chzy18.

## 11. Repository, testing and publishing

- **Settled: never commit or publish third-party game material**, from any game: no decompiled or disassembled code, no extracted files, data or string tables, indexes or symbol dumps, and no ripped art, audio or models. Keep research evidence local and gitignored; commit only our own written analysis.
- **Settled:** research folders are never pushed without Chzy18 reviewing them first.
- **Settled:** large binaries go in asset packs, not git (`docs/asset-packs.md`).
- **Settled: two remotes.** The repository lives on GitHub (https://github.com/Risxhb/LastBell, remote `origin`, which also hosts the web demo) and Azure DevOps (https://dev.azure.com/ChzandZum/_git/Last%20Bell, remote `azure`). The integration branch is `feature/confrontation-menu`. From Chzy18's PC, push to Azure only for now; Risx mirrors finished branches to GitHub.
- **Settled: push rarely, in bigger batches.** Commit locally as often as you like; push only finished, tested work (about one push per lane per day at most). One branch and one Azure PR per boss or feature into `feature/confrontation-menu`. Start from the latest Azure branch. Never force-push and never push straight to `feature/confrontation-menu`. Keep PR comments few.
- **Settled: test less.**
  - Before a push, run only your own boss's suite (or your lane's tests), with a Unity lock slot. One full autotest runs **nightly** on `feature/confrontation-menu` after Risx's merges.
  - Use the smoke run for routine checks. Run bot fight sweeps only when balance numbers change (Hard plus one party run). Take screenshots and galleries only when the look changes.
  - Test only the **current version** of each boss, and only the **4 main bosses** (Aurelion v4, Crucivar v4, Sylvara v3, Bellkeeper Ultimate). No miniboss fights in the full autotest, smoke run, bot sweeps or class tests; Risx's miniboss tests stay in code behind their own flag.
- **Settled: Unity lock.** Up to three slots via `tools/unity-lock.sh` (workspace) and its priority queue. Slot 3 opens only with at least 8 GB of RAM free; a performance capture takes all slots. Release after every run and remove only a lock you took. Priority (`UNITY_LOCK_PRIORITY`): Aurelion 1, Ultimate 2, Tollbreaker 3, Crucivar 4, Sylvara 5, everything else 9.
- **Settled: delete what isn't current.** Each lane deletes its own obsolete code, tests, flags, menu entries, launchers, docs, worktrees, builds, logs and branches without asking, and reports it in one line. Never touch another lane's material. Never delete a worktree with uncommitted or unpushed work. Code deletions go through the normal branch and PR. Delete a remote branch only if it's yours with no open PR, never `feature/confrontation-menu`.
- **Settled: lane board.** When your work changes a lane's state, update its row in `status-board/lanes.json` (workspace) with neutral wording and `updatedBy`; a scheduled task publishes it.
- **Settled: ownership.** Ability work belongs in its boss's lane. Item design and balance belong to the Items lane; shop code belongs to the Run loop and shop lane.

## Where the details live

| Topic | Location |
|---|---|
| Owner's rules pack (source of truth for these rules, art and look) | `gpt/08-design-rules.md` and `gpt/README.md` (workspace) |
| Agent workflow, inbox id, lock and board details | `AGENTS.md` (workspace root) |
| Run loop and controls table | `docs/run-loop/README.md` |
| Arcanist design | `docs/design/arcanist-redesign.md` |
| Sylvara v3 design | `docs/design/sylvara-v3/README.md` |
| Item catalogue | `docs/design/items/CATALOGUE.md` |
| Asset packs | `docs/asset-packs.md` |
| Revive, Tollbreaker kit and shop proposals | `docs/proposals/` (workspace) |
| Telegraph style, arena handoff, class sheets | `Reference/` (workspace) |
