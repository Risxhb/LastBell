# Crucivar — The Furnace Heart (Brass Foundry) — boss packet

Split from Codex's four-boss handoff: branch `handoff/four-boss-designs-20261021` (commit `d478b55`), [draft PR #46](https://github.com/Risxhb/collab/pull/46), board [#47](https://github.com/Risxhb/collab/issues/47). This file holds only this boss's section plus the shared rules every boss must follow; the original combined files are on that branch under `docs/boss-model-review/`. Everything here is a proposal, not approved art, tested balance or implemented gameplay.

Reference sheet: [02-crucivar-v1.png](02-crucivar-v1.png)

---

## Scope and sources

Campaign references establish Skyward Spire, Brass Foundry, Verdant Chimes, and Shattered Hour / Throne of the Final Toll. These are three major area bosses plus the ultimate boss, separate from the nine minibosses. The older one-arena four-encounter plan is a prototype scope, not the campaign roster. Exact arena dimensions/collision remain TBD. All new lore, names, measurements and tuning below are proposals.

Reviewed source materials: LAST-BELL-SHATTERED-HOUR-ARENA.md and original arena artwork; October 21 Arena-Level-Design/prompts.md; LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md; current APPROVED-KIT.md briefs for Vanguard, Arcanist and Chimesinger; collab/docs/design/bellkeeper.md v0.2. Newer approved class briefs override older no-healing/no-resource blanket statements for the relevant classes. Preserve historical Bellkeeper references; this proposes its campaign evolution.

Style: painterly stylized low-poly 3D, fixed high-oblique isometric camera, readable silhouettes and large material shapes. Strong warning geometry must dominate restrained gold, turquoise, molten and cosmic decoration. Use outlined hatched danger, countdown marks, and a visibly latched outline after tracking stops. Decorative light never deals damage. Images are concept references; in-image generated prose is illustrative, not gameplay authority. Correct multiview inconsistencies during modeling. Sylvara's final reference is the corrected FOUR-root-leg version.

## Shared combat constraints

Vanguard has close combos, ~0.8-second ultimate windup and partial frontal Brace. Arcanist has brief stationary Lance casts and timed seal setup. Chimesinger has bell/echo rotation and immediate burst heal. Bosses remain targetable through ordinary phase changes, preserving seals/bells/inscriptions while their timers continue. No mandatory healer, block, parry, upgrade, or dash invulnerability. Finite stamina and physical avoidance govern routes. Existing test and target movement differ: validate both 4.2 m/s prototype walk and proposed 6 m/s design walk. Proposed design movement also includes 9 m/s sprint, 3 m/0.2 s dash, 100 stamina, 30 dash cost, 20 jump cost, 25/s sprint drain and 20/s regeneration after 0.75 s; these are not all verified runtime constants.

All damage below uses 100 player HP. All times are seconds; geometry is metres before player-radius expansion. W/R/CD means first warning-to-damage / guaranteed boss recovery / minimum reselection cooldown. Cooldown never overrides safe scheduling. Persistent hazards specifically noted may remain during recovery. No attack below assumes jump clearance. Single attacks deduplicate hits; this is not global immunity. Start hit-stun near 0.15 s and validate post-hit routes. No invisible contact damage or forced wall damage.

Targeted circle volleys select one player solo, two for parties of 2–4, three for 5–8; rotate eligible recipients. Avoid repeated caster suppression. At most two damaging patterns overlap, only with a proven common safe route; otherwise sequence them. Account for body collision, walls, finite stamina, player radius, reaction margin, down/disconnect states and every party size. No instant retargeting after lock. Charging bosses leave a traversable wall corridor.


## 2. Crucivar — The Furnace Heart / Brass Foundry

### Identity and model

Former engine casting replacement clock parts, now endlessly melting finished parts to create a perfect heart. Gantry lowers locked chassis, chains release, furnace opens and right hammer unfolds; blast-furnace inhalation. Feeling: industrial weight and controlled violence.

3.8 m tall, nearly as broad; barrel torso, short piston legs, oversized RIGHT hammer and smaller LEFT pouring crucible. Front: blackened furnace-door face, heavy brow, amber eye apertures, toothed vent grille and three luminous belly shutters. Side: squat chimney, shoulder-to-hip pistons, thick-lipped crucible. Back: tanks, insulated pipes, toothed flywheel and dark access plate, not a glowing weak button. Charcoal iron, hammered brass, oxidized joints, scorched ceramic, localized molten seams and broad heat bands. Steps compress pistons first; idle shutters breathe; dragging hammer never deals untelegraphed damage. Stagger: compressed knees, steam, hammer brace. Death: heat fades white/orange to red, kneels, harmless final pour into own crucible, locks solid.

### Abilities

| Ability | Geometry | W/R/CD | Damage |
|---|---|---|---|
| Diebreaker | Radius 3 circle immediately ahead of hammer | 1.8/2.4/6 | 18 |
| Slag Cast | Bounded radius 1.4 circles; pools last 3 s | 2/1.6/10 | 10 impact; 4/s standing |
| Rail Ram | 2.6-wide lane, up to 8 travel | 1.8/2.5/12 | 16 |
| Pressure Release | Opposite 70-degree sectors, reach 5 | 1.7/2/9 | 12 |
| Casting Cycle | Three sequential strips, width 2 | 1.8 each/3.5/22 | 14/strip |

Diebreaker: overhead hammer ratchets through three notches; toothed landing circle locks immediately. Flat brass flash and clang, no extra invisible shockwave. Melee rounds hammer shoulder, ranged stays outside; lodged hammer gives 2.4 s punish.

Slag: tips crucible; cauldron icons track for 0.6 then lock for 1.4; visible arcs arrive at resolve. Hatched pools cool dark after 3 s then become harmless. Melee places away from approach route, ranged leaves spot without obstructing future route. Overlapping pools do not multiply standing damage. Pool geometry/capacity must leave safe routes.

Ram: hammer counterweight, heel wheels exposed, pressure hiss, immediate lane lock. Travel takes 0.6 s. Sidestep; melee follows endpoint and ranged attacks braking broadside. No wall pin.

Release: front/back OR left/right shutter pair opens, locking selected opposite sectors; pressure-gauge cue and brief steam hit. Melee rotates to perpendicular safe sectors, ranged beyond 5 m can cast; steam clears rapidly.

Casting signature: braced pouring stance creates three numbered die-stamp strips. Each gets 1.8 s warning, damages for 0.25 s, cools and becomes safe before next resolves. Cross behind completed strikes rather than run endlessly ahead. Cold corridor must fit player bodies. Melee crosses toward body, ranged relocates across lanes. Final cooling hiss begins 3.5 s recovery.

### Phases, patterns, assets and exploits

100–65% Sealed Furnace: intact insulation, isolated attacks. 65–30% Casting Heat: insulation sheds exposing hot pipes; Slag precedes Casting only if routes remain. 30–0% Open Pressure: flywheel guard and shoulder vents open; Ram changes position/angles before Casting, no blanket speed increase. Example: Slag → recovery → Casting → long punish → Ram → Diebreaker. Heavy attacks also provide best stationary windows for both roles.

Highest-tier Second Press: repeat one numbered cooled strip with fresh 1.8 s warning, never sole remaining escape route.

Assets: mechanical rig, pistons, hammer/crucible, shutters, flywheel, vents; compression walk, ratchet/extraction, pour, ram/brake, vent variants, casting, stagger/shutdown/phases. Cooling slag, stamped warnings, narrow steam, restrained sparks; pressure/ratchet/forge/cooling sounds. Pools always expire; no permanent coverage soft-enrage. External furnace scenery stays decorative, no surprise lethal perimeter.


## Phases (from phases.md)

| Boss | Phase 1 | Phase 2 | Phase 3 |
|---|---|---|---|
| Crucivar | 100–65%: Sealed Furnace. Insulated chassis. Isolated hammer, slag, ram and vent attacks. | 65–30%: Casting Heat. Insulation falls away. Slag placement precedes sequential casting strips, preserving a cold route. | 30–0%: Open Pressure. Flywheel guard and shoulder vents open. Rams change casting angles; directional vents pressure close players. |

Transitions queue until the active attack finishes, then use a brief readable transformation without damaging the player. Bosses remain targetable; seals, bells and inscriptions persist, with timers continuing normally. Do not stack multiple phase attacks if burst damage crosses thresholds. At most two damaging patterns overlap, only after route validation with real collision, finite stamina and no assumed invulnerability. Avoidance works for melee and ranged characters; retain punish windows rather than uniformly accelerating attacks.

## Balance, difficulty and implementation

Reference sustained DPS must be measured during representative movement/recovery, not only on a stationary dummy. Solo HP = measured reference sustained DPS × target duration. Targets: Aurelion 150 s, Crucivar 165 s, Sylvara 165 s, Bellkeeper 240 s. At illustrative 40 DPS: 6000/6600/6600/9600 HP. These are examples, not existing runtime values. Co-op trial multiplier 1 + 0.7 × (active players − 1), tune to actual clear time; no automatic player-count damage increase. Duplicate classes remain viable.

Difficulty tiers: First Chime isolated patterns/longer warnings; Steady Ticking baseline above; Striking Hour tested combinations; Midnight Toll additional named mechanic per boss. Do not uniformly accelerate warnings. Require feasible baseline-kit routes and preserve recovery. A Vanguard ultimate and meaningful ranged setup must fit windows; no repeated forced phase cleanse. Boss stagger threshold prevents permanent player interrupt locks, and a full stagger cancels its warning truthfully.

Transitions queue until current authored attack ends, then use non-damaging visible transformations. Targetability and player setup persist. If burst crosses multiple thresholds, do not stack multiple phase attacks. Defeat immediately cancels hazards, markers and memory; practice/fresh-run reset clears all encounter state. Down/disconnect removes impossible assignments.

Shared implementation: attack scheduler, distance/target rules, geometry-derived warnings, phase queue, damage deduplication, marker capacity, charge endpoint checks, authoritative damage/event/reset interfaces. Bellkeeper adds one stored footprint/timeline. No private duplicate hit-testing engine. Models, clips, VFX and sound lists above are required authored assets; meshes and gameplay are not delivered by PNG sheets.

Verify graybox routes with actual arena/body/player geometry, slower prototype movement and design movement, finite stamina and no assumed iframes; include post-hit recovery, wall pinning, 1–8 players, audio-off readability and reduced effects. Finalize detailed models only after viable routes. Existing potions/healing/runtime changes should be reconciled with current owner rather than silently overwritten by this concept packet.

## Image-generation prompt (provenance)

Use case: stylized-concept. Create a polished landscape character MODEL REFERENCE SHEET for the game LAST BELL, painterly stylized low-poly 3D fantasy, credible sculpted game models, large readable shapes, intricate but controlled clockwork details. Warm ivory paper, navy typography, thin brass separators, consistent art-book layout. Upper 60% shows full-body FRONT, SIDE, BACK neutral model turnarounds at identical scale, plus a tiny 1.8m human scale silhouette. Lower 40% shows THREE clearly different full-body phase poses, labeled with phase number and HP range. Keep same anatomy/identity/equipment consistent across views. All feet and weapons fully visible. No overlapping panels, no illegible paragraphs, no photorealism. These are visual concept references, not actual 3D files. Title: CRUCIVAR — THE FURNACE HEART. Subtitle: BRASS FOUNDRY • 3.8 m. Broad squat industrial forging automaton, short piston legs, barrel furnace torso, massive RIGHT hammer forearm, smaller LEFT pouring-crucible forearm. Black iron furnace-door face, two amber eye ports, toothed grille, three bright orange belly shutters. Side chimney assembly; back has tanks pipes and large brass flywheel. Charcoal iron, hammered brass, scorched ceramic insulation, localized molten orange seams, no flesh. Phase 1 '100–65% • SEALED FURNACE': closed insulated chassis heavy hammer ready. Phase 2 '65–30% • CASTING HEAT': some insulation panels shed exposing hot pipes, left crucible pouring a small controlled molten stream, raised hammer. Phase 3 '30–0% • OPEN PRESSURE': flywheel guard open, shoulder vent fins unfolded releasing narrow steam jets, wide braced casting pose. Consistent asymmetrical arm placement in all views. Small arena motif industrial brass furnace rim.
