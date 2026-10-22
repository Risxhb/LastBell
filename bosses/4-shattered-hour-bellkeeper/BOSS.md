# The Bellkeeper — Sovereign of the Shattered Hour — boss packet

Split from Codex's four-boss handoff: branch `handoff/four-boss-designs-20261021` (commit `d478b55`), [draft PR #46](https://github.com/Risxhb/collab/pull/46), board [#47](https://github.com/Risxhb/collab/issues/47). This file holds only this boss's section plus the shared rules every boss must follow; the original combined files are on that branch under `docs/boss-model-review/`. Everything here is a proposal, not approved art, tested balance or implemented gameplay.

Reference sheet: [04-bellkeeper-v1.png](04-bellkeeper-v1.png)

---

## Scope and sources

Campaign references establish Skyward Spire, Brass Foundry, Verdant Chimes, and Shattered Hour / Throne of the Final Toll. These are three major area bosses plus the ultimate boss, separate from the nine minibosses. The older one-arena four-encounter plan is a prototype scope, not the campaign roster. Exact arena dimensions/collision remain TBD. All new lore, names, measurements and tuning below are proposals.

Reviewed source materials: LAST-BELL-SHATTERED-HOUR-ARENA.md and original arena artwork; October 21 Arena-Level-Design/prompts.md; LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md; current APPROVED-KIT.md briefs for Vanguard, Arcanist and Chimesinger; collab/docs/design/bellkeeper.md v0.2. Newer approved class briefs override older no-healing/no-resource blanket statements for the relevant classes. Preserve historical Bellkeeper references; this proposes its campaign evolution.

Style: painterly stylized low-poly 3D, fixed high-oblique isometric camera, readable silhouettes and large material shapes. Strong warning geometry must dominate restrained gold, turquoise, molten and cosmic decoration. Use outlined hatched danger, countdown marks, and a visibly latched outline after tracking stops. Decorative light never deals damage. Images are concept references; in-image generated prose is illustrative, not gameplay authority. Correct multiview inconsistencies during modeling. Sylvara's final reference is the corrected FOUR-root-leg version.

## Shared combat constraints

Vanguard has close combos, ~0.8-second ultimate windup and partial frontal Brace. Arcanist has brief stationary Lance casts and timed seal setup. Chimesinger has bell/echo rotation and immediate burst heal. Bosses remain targetable through ordinary phase changes, preserving seals/bells/inscriptions while their timers continue. No mandatory healer, block, parry, upgrade, or dash invulnerability. Finite stamina and physical avoidance govern routes. Existing test and target movement differ: validate both 4.2 m/s prototype walk and proposed 6 m/s design walk. Proposed design movement also includes 9 m/s sprint, 3 m/0.2 s dash, 100 stamina, 30 dash cost, 20 jump cost, 25/s sprint drain and 20/s regeneration after 0.75 s; these are not all verified runtime constants.

All damage below uses 100 player HP. All times are seconds; geometry is metres before player-radius expansion. W/R/CD means first warning-to-damage / guaranteed boss recovery / minimum reselection cooldown. Cooldown never overrides safe scheduling. Persistent hazards specifically noted may remain during recovery. No attack below assumes jump clearance. Single attacks deduplicate hits; this is not global immunity. Hit-stun is 15 ticks (0.25 s) with 1.5 m knockback and validate post-hit routes. No invisible contact damage or forced wall damage.

Targeted circle volleys select one player solo, two for parties of 2–4, three for 5–8; rotate eligible recipients. Avoid repeated caster suppression. At most two damaging patterns overlap, only with a proven common safe route; otherwise sequence them. Account for body collision, walls, finite stamina, player radius, reaction margin, down/disconnect states and every party size. No instant retargeting after lock. Charging bosses leave a traversable wall corridor.


## 4. Bellkeeper — Sovereign of the Shattered Hour / Throne of the Final Toll

### Identity and model

Guardian created to toll when the city could no longer sustain time; instead arrested that instant, trapping the realms in their duties. It refuses the final second rather than seeking conquest. Monumental background bell moves silently, chest pendulum starts, body descends on chains which detach on landing; incomplete deep note. Feeling: reaching the mechanism behind all previous suffering.

5.5 m tall hooded cathedral-bell torso, massive planted legs, heavy forearms, incomplete halo. Front: small recessed ivory mask under hood, thin gold eye line, split shell and suspended clapper behind vertical crack. Side: bell overhangs hips, thick shoulder axles, RIGHT pendulum striking hand, LEFT long blunt conducting fingers. Back: three incomplete clock rings at different angles, blackened spine, broken chain attachments and cobalt plates. Aged brass, bronze, cobalt, chipped ivory, sparse gold fissures; purple stays in recesses/environment. Slow deliberate steps, decisive releases, shell swings about clapper; idle beat sometimes pauses. Stagger: off-center clapper strike, torso twist/arm brace, rings desynchronize. Death: opens chest, catches then releases clapper; background bell sounds complete note; knees fold, rings stop.

### Abilities

**Numbers changed to the current game's values (2026-10-21, user decision).** The table below uses the encounter spec v0.3 (collab PR #16, `docs/design/bellkeeper.md`) and the Unity run loop (PR #42, `BellkeeperEncounter.cs`) at Steady Ticking, in ticks at 60 Hz. The packet's original values are kept in brackets for reference; spec v0.3 §2.1 compares them. There are no per-attack cooldowns: the fight runs fixed loops and blocks.

| Ability | Geometry | Warning / recovery (ticks) | Damage |
|---|---|---|---|
| Bell Sweep | 80-degree cone, reach 6.5 | 18 tracking + 51 after lock / 36 [packet 2 s / 1.8 s / CD 6] | 30; 45 in the Final Toll [17] |
| Hollow Toll | Annulus 2.6–6.8 [3.5–7] | 72 / 40 [2.2 s / 2.2 s / CD 11] | 25 [16] |
| Pendulum March | 2.2 lane to target + 2.75 overshoot, clamped to the boss bound [2.6 lane, up to 9] | 48, then 18-tick travel [1.9 s, 0.7 s surge, 2.5 s / CD 12] | 30 [18] |
| Shattered Chime | Circles radius 1.4, 24 ticks apart | 30 follow + 48 after lock [0.5 + 1.7 s / 1.8 s / CD 10] | 15 [14] |
| The Unfinished Hour | One stored world-space footprint repeat | 90 (100 on First Chime) / 120 [≥ 2.2 s / 3.5 s / CD 24] | Same as the recorded attack: 30 / 25 / 30 [14] |

Hit-stun 15 ticks (0.25 s) with 1.5 m knockback [packet ~0.15 s]. Boss collision 1.4 m radius. Final Toll recovery 120 ticks (2.0 s) [3.5 s]. Party HP scaling `1 + 0.6 × (n − 1)` [0.7]. Boss HP: Unity test 3600, fight target 3–4 min [9600 at 40 DPS × 240 s]. Borrowed Second pause: 60 ticks.

Sweep: arm draws across mouth, shell tracks 0.3 then locks. Deep metal draw precedes compressed-sound fan. Melee rounds opposite flank; ranged leaves cone/tip; low hanging arm gives punish. Do not copy old unsafe 0.95-second 8 m sweep timing; the 6.5 m cone with 51 ticks after lock passes the escape check.

Toll: chest compresses, rising hum and two crisp boundaries with empty safe center. Center locks at warning; boss planted. Horizontal sound band flashes only in marked annulus. Melee moves inward, ranged chooses nearest viable inner/outer safety. Inner radius must fit players outside boss collider.

March: knees compress, rings align vertically, lane/endpoint lock. Heavy 18-tick (0.3 s) forward surge after warning, no steering/drag/pin. Move laterally; melee pursues endpoint, ranged uses stationary landing recovery.

Chime: halo fragments hover over selected players; circles follow 30 ticks (0.5 s) then lock for 48 ticks (0.8 s). Brittle falling ring, one burst, dissolve. Melee rotates around body; ranged leaves marked cast spot; bounded markers, no eight-circle floor saturation.

Unfinished Hour signature: record one resolved Sweep/Toll/March footprint, leave faint dashed INACTIVE outline plus clock at original anchor. Reverse-turning back ring changes it to active hatched warning with a full 90-tick (1.5 s) countdown. Repeat once in ORIGINAL WORLD position, never following boss/player. Pair with at most one current attack only when common reachable safety exists. Melee exploits present boss being elsewhere; ranged cannot assume distance guarantees safety. Erase memory after sequence, open chest 120 ticks (2.0 s). No player rewind, boss healing, or reversed cooldowns.

### Phases, patterns, assets and exploits

100–70% Last Guardian: closed shell/compact halo; demonstrate physical attacks and isolated echo. 70–35% Broken Hour: separating shell, machinery visible, offset rings; March → Toll and Chime → Sweep with full individual warnings. 35–0% Final Toll: chest fully open, halo spread, brighter core; longer authored sequence. Example: March → landing recovery → Toll → Chime → remembered March alongside separately warned Sweep → 2.0 s punish. Only overlap that last pair after common-route validation; otherwise sequential. Tests Aurelion commitment, Crucivar lanes, Sylvara reuse plus world-position memory.

Highest-tier Borrowed Second: an explicitly numbered pause stops/resumes an echo's visible countdown; warning never lies or resolves while stopped. Tests premature re-entry.

Assets: bell-shell rig, clapper/mask, heavy limbs, independent broken rings, chain release and opening panels; movement, attacks, record/release, phases, stagger and clapper defeat. Inactive/active echo distinction, standard hazard families, controlled resonance; distinct current/remembered audio rhythms. One-memory cap, clear on reset/death. Camera-aware fading if model occludes floor; conservative body width. Floor fissure and four rim stations remain decorative, no invented environmental damage.


## Phases (from phases.md)

| Boss | Phase 1 | Phase 2 | Phase 3 |
|---|---|---|---|
| Bellkeeper | 100–70%: Last Guardian. Compact rings and closed shell. Introduce sweep, toll, march, chime and one isolated memory repeat. | 70–35%: Broken Hour. Separated shell and offset rings. Short two-pattern combinations and one stored world-space attack footprint. | 35–0%: Final Toll. Fully open chest and spread halo. March, toll, chimes and a memory/sweep pairing when a common safe route exists; 2.0-second final recovery. |

Transitions queue until the active attack finishes, then use a brief readable transformation without damaging the player. Bosses remain targetable; seals, bells and inscriptions persist, with timers continuing normally. Do not stack multiple phase attacks if burst damage crosses thresholds. At most two damaging patterns overlap, only after route validation with real collision, finite stamina and no assumed invulnerability. Avoidance works for melee and ranged characters; retain punish windows rather than uniformly accelerating attacks.

## Balance, difficulty and implementation

Reference sustained DPS must be measured during representative movement/recovery, not only on a stationary dummy. Solo HP = measured reference sustained DPS × target duration. Targets: Aurelion 150 s, Crucivar 165 s, Sylvara 165 s, Bellkeeper 240 s. At illustrative 40 DPS: 6000/6600/6600/9600 HP. These are examples, not existing runtime values. Co-op multiplier 1 + 0.6 × (active players − 1) (spec v0.3 D6), tune to actual clear time; no automatic player-count damage increase. Duplicate classes remain viable.

Difficulty tiers: First Chime isolated patterns/longer warnings; Steady Ticking baseline above; Striking Hour tested combinations; Midnight Toll additional named mechanic per boss. Do not uniformly accelerate warnings. Require feasible baseline-kit routes and preserve recovery. A Vanguard ultimate and meaningful ranged setup must fit windows; no repeated forced phase cleanse. Boss stagger threshold prevents permanent player interrupt locks, and a full stagger cancels its warning truthfully.

Transitions queue until current authored attack ends, then use non-damaging visible transformations. Targetability and player setup persist. If burst crosses multiple thresholds, do not stack multiple phase attacks. Defeat immediately cancels hazards, markers and memory; practice/fresh-run reset clears all encounter state. Down/disconnect removes impossible assignments.

Shared implementation: attack scheduler, distance/target rules, geometry-derived warnings, phase queue, damage deduplication, marker capacity, charge endpoint checks, authoritative damage/event/reset interfaces. Bellkeeper adds one stored footprint/timeline. No private duplicate hit-testing engine. Models, clips, VFX and sound lists above are required authored assets; meshes and gameplay are not delivered by PNG sheets.

Verify graybox routes with actual arena/body/player geometry, slower prototype movement and design movement, finite stamina and no assumed iframes; include post-hit recovery, wall pinning, 1–8 players, audio-off readability and reduced effects. Finalize detailed models only after viable routes. Existing potions/healing/runtime changes should be reconciled with current owner rather than silently overwritten by this concept packet.

## Image-generation prompt (provenance)

Use case: stylized-concept. Create a polished landscape character MODEL REFERENCE SHEET for the game LAST BELL, painterly stylized low-poly 3D fantasy, credible sculpted game models, large readable shapes, intricate but controlled clockwork details. Warm ivory paper, navy typography, thin brass separators, consistent art-book layout. Upper 60% shows full-body FRONT, SIDE, BACK neutral model turnarounds at identical scale, plus a tiny 1.8m human scale silhouette. Lower 40% shows THREE clearly different full-body phase poses, labeled with phase number and HP range. Keep same anatomy/identity/equipment consistent across views. All feet and weapons fully visible. No overlapping panels, no illegible paragraphs, no photorealism. These are visual concept references, not actual 3D files. Title: THE BELLKEEPER. Subtitle: SOVEREIGN OF THE SHATTERED HOUR • 5.5 m. Ultimate boss, massive ancient living cathedral bell with a hooded split brass bell-shell torso and enormous planted mechanical legs. Tiny recessed ivory mask beneath hood, single narrow gold eye line. Suspended gold clapper inside chest vertical crack. Heavy segmented brass/cobalt arms; RIGHT hand is a massive pendulum striking head, LEFT is long blunt brass conducting fingers. Three broken clock rings at different angles behind back, blackened spine, broken chain attachments. Aged brass blackened bronze midnight cobalt chipped ivory sparse gold fissures. Grounded imposing civic guardian, no human robes, not generic knight. Phase 1 '100–70% • LAST GUARDIAN': closed bell shell, compact halo, deliberate pose. Phase 2 '70–35% • BROKEN HOUR': bell-shell sections separated slightly, exposed clapper machinery, offset halo rings, extended conducting hand with a small dashed remembered attack footprint. Phase 3 '35–0% • FINAL TOLL': chest shell dramatically open, fully exposed luminous clapper, broken halo spread wider, overhead pendulum striking pose; controlled gold resonance and violet rim light, no obscuring explosion. Small arena motif dark broken cathedral and monumental cracked bell, keep background secondary.
