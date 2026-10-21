# Sylvara, The Rootbound Choir — encounter specification

Draft v0.1.1 · October 21, 2026 · Lane: Bosses / Sylvara ([#47](https://github.com/Risxhb/collab/issues/47)) · Source packet: [four-boss handoff, PR #46](https://github.com/Risxhb/collab/pull/46) (`handoff/four-boss-designs-20261021`, commit `d478b55`, `docs/boss-model-review/`).

Status: **v0.1 specification, with D1–D6 adopted provisionally and a playable graybox (§15).** It is engine-independent and targets the Unity/C# production path. It follows the structure and method of the [Bellkeeper spec v0.2](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) so the two can share one validator and one set of contract requests.

Sylvara is the **area boss of Verdant Chimes**, one of the campaign's three area bosses ([#44](https://github.com/Risxhb/collab/issues/44)). She is not part of the current one-arena three-plus-one slice. The packet's abilities, geometry, timings, damage and phases are taken as given. This spec adds what the packet leaves open: ticks, schedules, targeting, party scaling, escape evidence and the decisions that still need a human.

Reference sheet: [`03-sylvara-v2.png`](https://github.com/Risxhb/collab/blob/handoff/four-boss-designs-20261021/docs/boss-model-review/03-sylvara-v2.png), the corrected version with **exactly four root legs**. The earlier biped draft is retired.

Companion tool: [`tools/encounter-escape/sylvara-escape-check.ps1`](../../tools/encounter-escape/sylvara-escape-check.ps1).

## 1. Rules this encounter follows

These come from the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) and the packet's shared combat rules. Where older documents or prototypes disagree, these win:

- **No invincibility of any kind.** Dash gives no immunity, and there is no post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once.
- **No mandatory healer, block, parry, upgrade or dash.** Any mix of Vanguard, Arcanist and Chimesinger, including duplicates, can clear.
- **Finite stamina governs routes** (§5.3). Attacks never consume it.
- **No attack can be jumped.** Jump state never changes resolution.
- **Health:** players arrive full. Health and potions refill before an area's final boss; Sylvara is Verdant Chimes' final boss, so the run's potion count is full.
- **Boss stays targetable** through phase changes. Arcanist seals, Chimesinger bells and other player setup persist with their timers running.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. Decorative light, mist, motes and roots never deal damage.
- **Locks are final.** Nothing retargets after lock.
- **At most two damaging patterns overlap**, and only with a proven common safe route. Sylvara's baseline schedule never overlaps two (§4).

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Game design #33; Unity prototype #27 / #42 |
| Sprint | 9 m/s, never required | Game design |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Bellkeeper spec |
| Player health | 100 HP | Packet |
| Boss body | **1.8 m radius** collision circle, following the planted four-root footprint (about 3.6 m across on the sheet) | This spec (§5.4) |
| Arena | **Placeholder:** circle, player centres ≤ 11.5 m and boss centre ≤ 9.0 m from the centre | No Verdant Chimes arena exists yet (D7) |

The boss-centre bound keeps a full player width between the body and the wall, so no one can be pinned. Rerun the escape check when the real arena exists.

## 3. Attack vocabulary

Five attacks. Geometry, damage and the packet's W/R/CD (first warning to damage / guaranteed recovery / minimum cooldown) are the packet's. Ticks are their exact conversion. Steady Ticking uses them unchanged.

| ID | Attack | Shape | Targeting | Tracking and lock | Resolve (ticks from warning) | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|
| `pruning_arc` | **Pruning Arc** | Cone from the boss centre, 100° wide, reach 5.5 m | Nearest standing player | Turns toward the target for 18 ticks (0.3 s), then facing locks | 96 (78 after lock) | 102 (1.7 s) | 300 (5 s) | 13 |
| `rootscript` | **Rootscript** | Three lanes from the boss centre, each 1.8 m wide and 7 m long, **60° apart** (D4); clipped by the arena wall | Centre lane at a rotating target | Locks at warning | 114 | 120 (2.0 s) | 600 (10 s) | 14 |
| `seed_refrain` | **Seed of the Refrain** | Circle, radius 1.5 m, on each selected player | 1 / 2 / 3 players (§6) | Follows its owner for 36 ticks (0.6 s), then locks | 132 (96 after lock) | 108 (1.8 s) | 660 (11 s) | 12 |
| `briar_procession` | **Briar Procession** | Lane from the boss centre, **3.6 m wide (D1; packet 2.4 m)**, up to 7 m, end clamped to the boss bound | Farthest standing player (§6) | Locks at warning; no steering | 108; then the boss travels the lane in 39 ticks (0.65 s) | 138 (2.3 s) after arrival | 720 (12 s) | 15 |
| `threefold_bloom` | **Threefold Bloom** (signature) | Three circles, radius 2 m, resolving in order I → II → III | Layout around the boss (§3.1) | Fixed at spawn | Each flower: 102 warning, then an **18-tick active period** (0.3 s) | 180 (3 s) punish after flower III is spent | 1260 (21 s) | 13 per flower |

**Per-attack notes** (behaviour from the packet, made exact):

- **Pruning Arc.** The tuning-fork arm draws back like a bow. Leaves point the swing direction, but only the crisp cone is damage. One sweep, no hidden backswing. Melee rounds the opposite shoulder; ranged steps out of the reach.
- **Rootscript.** Three fingers touch the floor and root drawings fill all three lanes at once. One eruption at resolve; the roots retract over 12 ticks and **never** remain as obstacles or hazards. With 60° spacing the expanded lanes separate 2.4 m from the boss centre, so melee has real pockets between lanes just outside the body (2.1 m). At 45° those pockets would only start at 3.1 m.
- **Seed of the Refrain.** A bell bud hangs over each selected player with a dashed circle following them, then the circle locks and counts down. A rising three-note phrase plays before resolve. One hit: no trap, root or slow is left behind.
- **Briar Procession.** Sylvara gathers her four roots and lowers the shrine; a leaf-arrow lane locks. The lane resolves on tick 108, then the boss scuttles to the lane's far end over 39 ticks. Its body pushes players aside (R20) but deals no damage. Players punish during root replanting at the far end.
- **Threefold Bloom.** See §3.1.

### 3.1 Threefold Bloom in detail

**Layout.** The three flowers sit on a 5 m ring around the boss's position at warning, centres 4.2 m apart (D3). The chain is centred on the direction of a rotating target, and the director only uses layouts whose flowers sit fully inside the arena. If the target direction doesn't fit, it rotates the chain toward the arena centre before warning.

Consequences:
- Expanded flowers overlap (2 × 2.3 m > 4.2 m), so there's no thin sliver between two flowers to misjudge.
- The chain covers about 100° of the ring. The rest of the arena stays open, so it never walls off the boss or all routes.
- A 0.6 m band remains between the body and the flowers (5 − 2.3 − 2.1 m), so melee can stay on the boss.

**Schedule (Steady Ticking, ticks from the sequence start):**

| Flower | Warning | Active (damages anyone inside, once) | Spent (safe) |
|---|---|---|---|
| I | 0 | 102–119 | from 120 |
| II | 72 | 174–191 | from 192 |
| III | 144 | 246–263 | from 264 |
| Punish | — | — | 264–443: harmless motes rise from flower III and the boss holds still |

The packet doesn't give a stagger between flowers. This spec proposes **72 ticks (1.2 s)** (D2): each flower is spent 54 ticks before the next one resolves. The escape from a flower into the previous, spent one is 2.3 m, so a walking player at 6 m/s can make it with the Steady Ticking reaction margin (50 ticks needed). That's the lesson: step back into the spent space instead of running laps.

**Flower states**, all shown on the flower and on the floor circle: closed bud (spawned, not yet warning), warning (numbered, hatched, countdown), open (active), spent (petals folded flat, clearly safe). The number (I/II/III) is always on the flower, not only in colour.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Dormant Choir | 100–65 % | Closed buds, bells hung close | Each attack on its own |
| 2 | Awakening Bloom | 65–30 % | Shoulder flowers open, bells spread | Seeds finish, then Bloom: place your seed, then reposition |
| 3 | Hollow Refrain | 30–0 % | Mask split, hollow resonance, thinner foliage shows root machinery | Rootscript into Bloom, and more Procession repositioning |

**Shared phase rules:**

- **Queued transitions.** Crossing a threshold queues the transition until the current block finishes. The transition is a 90-tick (1.5 s) non-damaging transformation; the boss stays targetable throughout.
- **No stacking.** If a burst crosses two thresholds, only the latest transition plays.
- **Defeat** cancels every warning, marker and flower at once; nothing can land after victory. Defeat pose: the shrine sits, roots relax, three descending notes, a small steady core remains.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds its recovery pose until it is. Windows are never shortened to fit a cooldown.
- **Seeded variation** (target choice, chain direction) is chosen at block start from the encounter RNG (R6), with a pre-validated fallback picked before the tell.

### 4.1 Phase 1 — Dormant Choir (fixed loop, 26.7 s)

Pruning Arc → Rootscript → Seed → Briar Procession → Pruning Arc → Threefold Bloom → repeat.

Every attack runs its full recovery before the next one warns, so nothing overlaps.

### 4.2 Phase 2 — Awakening Bloom (blocks A → B → C, 25.1 s)

| Block | Sequence | Join rule |
|---|---|---|
| **A: Seeded Bloom** | Seed → Threefold Bloom → punish | Flower I warns 12 ticks after the last seed resolves; seeds never overlap flowers |
| **B: Pursuit** | Pruning Arc → Briar Procession | Procession warns when the Arc's recovery ends |
| **C: Inscription** | Rootscript → Pruning Arc | Arc warns when the Rootscript recovery ends |

Cooldowns check out: Bloom every 1503 ticks (CD 1260), Arc gaps of 717 and 786 (CD 300).

### 4.3 Phase 3 — Hollow Refrain (blocks X → F1 → Y → F2, 42.5 s)

| Block | Sequence | Notes |
|---|---|---|
| **X: Refrain** | Seed → Pruning Arc → recovery → Threefold Bloom → punish | The packet's example. The Arc warns when the seeds resolve |
| **F1: Procession** | Briar Procession → Pruning Arc → Seed | |
| **Y: Root Prelude** | Rootscript → Threefold Bloom → punish | Flower I warns when the roots finish retracting (tick 126). Rootscript's own recovery is skipped because Bloom's punish follows |
| **F2: Procession** | Briar Procession → Pruning Arc | |

Two Blooms and two Processions per loop. Cooldown gaps: Bloom 1293 and 1257 (the second needs a 3-tick hold), Procession 1293 and 1257, Seed 1257 and 1293, Arc ≥ 330.

**Patterns never overlap in the baseline.** Every join is strictly sequential, so the two-pattern limit is only used at Striking Hour and above (§9).

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

Same as the Bellkeeper's §5.1. `sylvara-escape-check.ps1`:
- samples every boss position in the bound (0–9 m, by symmetry) and every direction in 10° steps
- finds the reachable danger point farthest from safety (0.1 m danger grid, 0.05 m boundary grid; shapes expanded by 0.3 m; player centres inside the arena and outside the boss body)
- converts that into walk-only and walk-plus-one-dash escape times, then into the minimum window per difficulty

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

**Bloom is checked as a chain.** At flower k's resolve, flowers k..III all count as danger (the player can't hide in a flower that's still warning) and spent flowers count as safe.

**Limits:** straight-line travel, no other players, no acceleration, constant-speed dash, grid accuracy about ±0.05 m. This is evidence for single attacks and the Bloom chain, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, placeholder arena)

| Attack | d_max | Worst configuration |
|---|---|---|
| Pruning Arc | **3.75 m** | Boss 7 m from centre, facing the wall |
| Rootscript, 60° spread | 1.57 m | Boss at the bound |
| Rootscript, 45° spread | 3.00 m | Boss at the bound |
| Seed of the Refrain | 1.81 m | From the seed's centre |
| Briar Procession, 2.4 m wide | 1.51 m | Sideways out of the lane |
| Briar Procession, 3.6 m wide | 2.11 m | Sideways out of the lane |
| Threefold Bloom | 2.30 m | From a flower's centre |

Minimum window in ticks, counted after lock (Arc, Seed) or from warning (the others), compared with this spec's Steady Ticking window:

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Pruning Arc | 74 / **90** | 47 | 41 | 38 | 78 |
| Rootscript (60°) | 52 / 59 | 34 | 28 | 25 | 114 |
| Seed of the Refrain | 55 / 62 | 35 | 29 | 26 | 96 |
| Briar Procession (3.6 m) | 58 / 67 | 36 | 30 | 27 | 108 |
| Threefold Bloom, per flower | 59 / 69 | 37 | 31 | 28 | 102 from warning; 54 from the previous flower being spent |

**Findings:**
- **Every packet timing is escapable at every tier**, with room to spare. Most are walk-solvable without a dash.
- **Pruning Arc is the tightest.** At the prototype's 4.2 m/s walk, its 78 ticks after lock aren't enough to walk out (90 needed), so First Chime adds 18 ticks (§9). At Steady Ticking a 4.2 m/s player needs one dash for it.
- **Bloom's "step into the spent flower" window** (54 ticks) is walk-solvable at Steady Ticking at 6 m/s (50 needed). At 4.2 m/s it needs a dash (37), or the player leaves by the chain's open ends instead.

**On the real Verdant Chimes arena** (D7: 14.0 m playable radius, so player centres ≤ 13.7 m and boss centre ≤ 11.5 m), run with `-ArenaRadius 13.7 -ProcessionWidth 3.6`, the conclusions don't change:

| Attack | d_max | First Chime, walk 6 / 4.2 | Steady Ticking | Midnight Toll |
|---|---|---|---|---|
| Pruning Arc | 3.91 m | 76 / **92** | 49 | 40 |
| Rootscript (60°) | 2.40 m | 60 / 71 | 37 | 28 |
| Seed of the Refrain | 1.81 m | 55 / 62 | 35 | 26 |
| Briar Procession (3.6 m) | 2.11 m | 58 / 67 | 36 | 27 |
| Threefold Bloom | 2.30 m | 59 / 69 | 37 | 28 |

First Chime's Arc (96 ticks after lock, §9) still covers the 4.2 m/s walker (92). The graybox (§15) keeps the 11.5 m key-art arena until the Verdant Chimes scene is brought into the run loop.

### 5.3 Stamina budget

Same rule as the Bellkeeper: a worst-case player may need **at most one dash per 2.25 s**, and at most three in a row after 5 s without one. Sprint and jump are never required.

- **Design walk:** the whole Steady Ticking baseline is walk-solvable. No dash is ever required.
- **Prototype walk:** only the Arc (one dash) and Bloom's spent-space route (one dash per flower, or the open-end route without one) need dashes. The closest pair is Bloom flowers 72 ticks apart, so dashing through all three costs 90 stamina in 2.4 s. That breaks the rule. The open-end route is always available, so the rule holds as long as the validator confirms the open-end route stays walk-solvable at 4.2 m/s for every layout (T3).

### 5.4 Collision, body and hit-stun

- **Body collider: a 1.8 m circle** matching the planted four-root footprint. The legs plant separately and never slide. Decorative roots, ferns and the trailing mist never block movement. The collider is real during Procession travel and pushes players out sideways (R20), with no damage and no wall damage.
- **Why the Procession lane must widen (D1).** With the packet's 2.4 m lane and a 3.6 m-wide body, a player standing just outside the lane is "safe" but still gets shoved by the body: a surprise collision the packet forbids. Widening the lane to the body's width (3.6 m) makes the danger exactly what the boss passes through. The escape cost is small (2.11 m against 1.51 m).
- **Hit-stun:** 9 ticks (0.15 s), the packet's starting value.
- **No knockback** on any Sylvara attack. Every hazard is a one-hit footprint, and pushes only come from the moving body. This removes wall-pinning risk entirely (D6).
- **Anti-chain rule.** For any two hazards that can hit the same player, the later resolve must come at least `stun + escape` after the earlier one. The tightest pair is Bloom: flower II resolves 72 ticks after flower I, against a 9 + 23 = 32-tick need. It passes.
- **One hit per instance** (R16). Each flower is its own instance, so standing in all three costs 39 HP. A player who enters a flower during its active period is hit once.

## 6. Party size (1–8)

**Tier A** for 1–4 players, **tier B** for 5–8, fixed at encounter start. Counts use standing, active players at each warning.

| Attack | 1 | 2–4 | 5–8 |
|---|---|---|---|
| Pruning Arc | 1 cone at the nearest player | Same | Same |
| Rootscript | 3 lanes; centre lane at the player | 3 lanes; centre lane at a rotating target | Same |
| Seed of the Refrain | 1 seed | 2 seeds | 3 seeds |
| Briar Procession | 1 lane at the player | 1 lane at the farthest player | Same |
| Threefold Bloom | 3 flowers; chain at a rotating target | Same | Same |

**Targeting rules:**
- **Nearest / farthest** are measured to the boss centre at warning; ties go to the lowest stable player ID.
- **Rotation** is round-robin by stable player ID from a seeded offset.
- **Procession stops distant parking.** It targets the farthest player, but never the same player twice in a row if another standing player is more than 6 m away. That way a lone Arcanist isn't charged every time.
- **Seed rotation.** No player gets seeds in two consecutive volleys if enough other standing players exist. This avoids repeated caster suppression.
- **Marked before lock.** Every seed owner and Procession target is tagged with a shape on the HUD and over the player, not colour alone.
- **Duplicate classes.** No assignment depends on class.
- **Revival** is the only mandatory co-op interaction, and it's never needed to clear a mechanic.

At eight players, three live seeds (31 m² expanded) plus a Bloom chain fit comfortably in the placeholder arena (415 m²). Seeds and flowers never coexist (§4).

## 7. Down, revive and disconnect

The same as the Bellkeeper's §7:
- A seed owner downed before lock: that seed is cancelled.
- A downed Procession or Rootscript target before warning: a new target is chosen.
- After lock, every hazard is world geometry and resolves normally.
- Downed and inactive players hold no assignments and aren't hit.
- Disconnect is treated as downed.

Every change emits `assignment_changed`.

**Practice** starts Sylvara at full health with full potions, in phase 1, from the practice seed. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage is the packet's, at Steady Ticking: Arc 13, Rootscript 14, Seed 12, Procession 15, Bloom 13 per flower. The worst single mistake is 15 HP, and a full Bloom failure is 39 HP. Health and potions refill before the fight, so the packet's values stand as written. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP** = measured reference sustained DPS × 165 s (solo), measured during movement and recovery, not on a stationary dummy. At an illustrative 40 DPS that's 6600 HP. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only.

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are not uniformly sped up.

| Tier | Sylvara changes |
|---|---|
| **1 — First Chime** | Arc +18 ticks after lock (96), so a 4.2 m/s walker escapes. Bloom stagger 90 ticks. All recoveries +0.5 s. Phase 3 drops F1 |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | One tested overlap: in Phase 2 block A, the three flowers appear as closed buds when the seeds **warn**, and flower I warns 30 ticks after the seeds lock instead of after they resolve. Players can see the chain while their seed still follows them, so the common safe route is "place your seed away from the chain". Flower I resolves 36 ticks after the seeds, which clears the anti-chain rule (32 needed). Recoveries −20 %, never below 0.6 s |
| **4 — Midnight Toll** | Striking Hour plus **Antiphon** |

**Antiphon** (the packet's highest-tier mechanic):
- Flower II spawns with a **second hanging bell**, visible from the start of the sequence.
- After flower III is spent, flower II re-arms with a full, fresh 102-tick warning and resolves again (ticks 264–383).
- The punish motes start after the repeat (tick 384).
- It is a separate hazard instance, so it can hit again. It counts as part of the Bloom pattern for the two-pattern limit.
- Escape is unchanged (2.3 m), and flowers I and III are spent by then, so it's walk-solvable.

## 10. Requests to the shared C# contract (#6)

Sylvara reuses the Bellkeeper's requests (R1 circle, R4 reassign/cancel before lock, R5 presentation markers, R6 seeded RNG, R7 active flag, R10 lane, R11 mutable boss position, R12 HP phases, R13 HP damage, R14 recovery event, R16 one hit per instance, R18 standing state, R20 body collision, R21 published movement constants). She adds:

| # | Need | Why |
|---|---|---|
| R22 | **Active-duration hazards:** damage applies to anyone inside during an active window (start and end ticks), once per player per instance | Bloom's 0.3 s active period. The Bellkeeper's hazards resolve on a single tick |
| R23 | **Hazard groups with ordered members**, each with its own warn, active and spent ticks, and a group ID in events | Bloom I/II/III and the Antiphon repeat |
| R24 | **Stagger meter and truthful cancel:** a full stagger cancels the current warning with a visible cancel cue and event, followed by a stagger-immunity window so players can't lock the boss down permanently | Packet's stagger rule; shared with all bosses |
| R25 | **Sector (cone) shape** with apex, facing and half-angle | Pruning Arc. The Bellkeeper needs it too but didn't list it |

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `sylvara`. Additions:
- `group_id` and `member_index` (Bloom I/II/III)
- `antiphon: true` on the repeat
- `flower_state_at_entry` for active-period hits (the player was inside at activation, or walked in during it)

Example card:

> *"Threefold Bloom, flower II (2:12, Phase 3). You were 0.8 m inside flower II when it opened. Flower I had been spent for 0.9 s: step back into it."*

## 12. Model, animation and asset notes

From the packet and sheet, with gameplay constraints:

- **Silhouette:** 4 m slender shrine on exactly four articulated root legs; branching shoulders; split ivory-stone mask with two small turquoise eyes; three chest bells in a wood-and-brass ribcage; tuning-fork arm and three-hook branch hand; broken bronze wheel on the back with ferns. Foliage concentrated on the shoulders and back so the legs and footprint read clearly from the high isometric camera.
- **The tuning-fork side must be readable**, because the Arc's warning starts with that arm. Mirror the rig so either arm can lead only if the cone warning stays the authority.
- **Rig:** four root limbs with separate planting (no sliding), mask split control, three independently swinging chest bells, tuning fork, restrained foliage secondary motion.
- **Animations:** idle (bells sway, mask still), walk/plant, prune (Arc), inscribe (Rootscript), seed, gather and scuttle (Procession), conduct (Bloom), stagger (bells clash out of rhythm, torso folds, roots brace), two phase transformations, defeat (seated shrine).
- **Flowers:** four states (closed, warning, open, spent) with numbers I/II/III; Antiphon's second bell.
- **VFX:** root drawings and eruptions, numbered flower circles, folded petals for spent, harmless motes for the punish window. Phase 3's turquoise sound arcs on the sheet are **decoration only** and must never look like a hazard edge.
- **Audio:** plucked-string snap (Arc), wooden crack (Rootscript), rising three-note phrase (Seed), root tension (Procession), distinct pitches for flowers I/II/III, three descending notes at defeat. Every cue also has a visual, so the fight reads with audio off.
- **Sheet corrections for modeling:** the Phase 2 panel shows the flowers as standing props. In the fight they are flat ground markers plus a small flower, and must not block movement.

## 13. Validation tests

| # | Check |
|---|---|
| T1 | Escape check rerun on the real Verdant Chimes arena and body collider, both movement sets |
| T2 | Every authored block in §4, at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Bloom: every layout the director can choose keeps an open-end route walk-solvable at 4.2 m/s, and never cuts off the boss |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects mid-sequence |
| T5 | Procession body push never forces a player into a live hazard or the wall |
| T6 | Stagger cancels the current warning truthfully, and no warning survives defeat or a practice reset |
| T7 | Audio-off and reduced-effects readability; Phase 3 arcs are never mistaken for damage |

## 14. Decisions

The user left these to this lane on October 21, 2026, so D1–D6 are **adopted provisionally** and used by the graybox. They stay open to playtest feedback.

| # | Decision | Status |
|---|---|---|
| D1 | Briar Procession lane width: 2.4 m (packet) with a 3.6 m body, or widen it to the body | **Adopted: 3.6 m**, so the danger is what the body passes through (§5.4) |
| D2 | Bloom stagger between flowers (the packet gives none) | **Adopted: 72 ticks (1.2 s)**; 90 on First Chime |
| D3 | Bloom layout | **Adopted: 5 m ring around the boss, centres 4.2 m apart, chain toward a rotating target** |
| D4 | Rootscript lane spread | **Adopted: 60°**, giving melee pockets just outside the body |
| D5 | Targeting: Arc at the nearest player, Procession at the farthest with no repeat | **Adopted** as written (§6) |
| D6 | Knockback | **Adopted: none for Sylvara**; hit-stun 0.15 s only |
| D7 | Verdant Chimes arena dimensions and collision | **Answered by the arena build** ([`feature/class-test-arena`](https://github.com/Risxhb/collab/tree/feature/class-test-arena) 184200b): flat, unobstructed floor with a 14.0 m playable radius. Escape results on it in §5.2 |

## 15. Graybox in the run loop

A playable graybox lives on [`feature/boss-sylvara`](https://github.com/Risxhb/collab/tree/feature/boss-sylvara), branched from the run loop ([PR #42](https://github.com/Risxhb/collab/pull/42)):

- **Where to play it:** menu → *Practice one encounter* → **Sylvara (area 3)**. It's practice only, because she isn't part of the one-arena run. She starts at full health and potions, as an area final boss. F8 jumps to the next phase (65 % / 30 %); F9 defeats her.
- **Code:** `unity/Assets/LastBell/Run/Encounters/SylvaraEncounter.cs`. It implements §3–§4 at Steady Ticking with D1–D6, including the phase loops, cooldown hold, targeting, the Bloom layout and the phase visuals (bells spreading, shoulder flowers, mask split).
- **Shared change:** `Hazard.ActiveTicks` and `OnSpent` (R22). A hazard can stay open for an active period and hit anyone inside once. Existing hazards are unchanged (`ActiveTicks` 0).
- **Body:** a labelled primitive stand-in with four root legs, split mask, three bells, tuning fork and back wheel. A `Resources/Characters/Sylvara.glb` model replaces it automatically when it exists.
- **Arena:** the run loop's key-art arena (11.5 m), matching the spec's placeholder. Moving her to the Verdant Chimes scene (14 m, §5.2) is the next integration step.
- **Autotest:** `-lastbellAutotest` now ends with a Sylvara section; `-lastbellSylvara` runs only that section. It checks:
  - all five attacks in the Phase 1 order
  - Bloom I → II → III, with 72-tick stagger and an 18-tick active period
  - one hit per flower for 13 HP, including entering mid-activation, and spent flowers being safe
  - no knockback
  - never more than one damaging pattern live
  - both queued phase transitions
  - defeat cancelling every pending hazard and Bloom prop
- **Not in the graybox yet:** other difficulty tiers (and so Antiphon), party sizes above 1, stagger (R24), and authored animation, VFX and audio.
