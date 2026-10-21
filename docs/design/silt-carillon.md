# Silt Carillon, Warden of the Water Steps — encounter specification

Draft v0.1 · October 21, 2026 · Lane: Mini-bosses / Verdant Chimes 2 · Source packet: [nine mini-boss handoff, PR #49](https://github.com/Risxhb/collab/pull/49) (`docs/nine-mini-bosses-20261021`, commit `a2ab311`, `docs/mini-bosses/`), section 8.

Status: **specification only, nothing here is implemented.** It is engine-independent and targets the Unity/C# production path. It uses the structure and method of the Bellkeeper spec ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the Sylvara spec ([PR #50](https://github.com/Risxhb/collab/pull/50)), so all of them can share one validator and one set of contract requests.

The Silt Carillon is the **second mini-boss of Verdant Chimes**, between the Briar Metronome and the Vesper Seedkeeper, ahead of the area boss Sylvara ([#44](https://github.com/Risxhb/collab/issues/44)). It is not part of the current one-arena three-plus-one slice. The packet's identity, abilities, geometry, timings and damage are taken as given except where §14 records a change. This spec adds what the packet leaves open: ticks, schedules, targeting, party scaling, escape evidence and four difficulty tiers.

Companion tool: [`tools/encounter-escape/silt-carillon-escape-check.ps1`](../../tools/encounter-escape/silt-carillon-escape-check.ps1).

## 1. Rules this encounter follows

These come from the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) and the packet's shared mini-boss contract. Where they disagree, the game design wins:

- **No invincibility of any kind.** Dash gives no immunity, and there is no post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once.
- **No mandatory healer, parry, upgrade or dash.** Any mix of Vanguard, Arcanist and Chimesinger, including duplicates, can clear. **Vanguard Brace** is the approved partial frontal mitigation; it applies to this boss's strike attacks and is bypassed by its ground attacks (§3).
- **Finite stamina governs routes** (§5.3). Attacks never consume it.
- **No attack can be jumped.** None of the Silt Carillon's hazards is jump-clearable.
- **Health and potions:** health refills before this fight; potions spent against the Briar Metronome stay spent. Damage here therefore costs resources for the rest of the area.
- **Cursor aim, cooldown-only abilities:** nothing in the fight requires lock-on, auto-target or a resource the kits don't have.
- **Boss stays targetable** through the phase change. Arcanist seals, Chimesinger bells and other player setup persist with their timers running.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. Moss, ferns, mist and the turquoise inlays never deal damage.
- **Locks are final.** Nothing retargets after lock.
- **Exactly two phases**, one damaging pattern at a time in the baseline. Striking Hour and Midnight Toll add authored joins (§9), never more than two overlapping patterns.

**Difficulty baseline.** The packet calls its numbers a working fit for the third tier. To stay consistent with the Bellkeeper and Sylvara specs, this spec uses the packet's numbers as **Steady Ticking** and builds the other tiers from them (§9, D4).

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.** Geometry is measured from the boss's collision boundary unless stated otherwise.

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Game design #33; Unity prototype #27 / #42 |
| Sprint | 9 m/s, never required | Game design |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Bellkeeper spec (replaces the packet's 0.45 + 0.25 m estimate) |
| Player health | 100 HP | Packet |
| Boss | **2.7 m tall, 1.8 m radius collision circle** at the four planted brass-root feet (D1) | Packet height; this spec's collider |
| Arena | **Placeholder:** circle, player centres ≤ 11.5 m and boss centre ≤ 9.0 m from the centre | Packet proposes a 24 m floor; no Verdant Chimes collision exists yet (D9) |

The boss-centre bound keeps a full player width between the body and the wall, so no one can be pinned. Rerun the escape check when the real arena exists.

## 3. Attack vocabulary

Six attacks: three in Phase 1, two evolved plus one unchanged plus one new in Phase 2. Ticks are exact conversions of the packet's W/A/R/CD (warning to first damage / active duration / guaranteed recovery / minimum cooldown from recovery end).

**Lane frame.** Every lane starts at the boss's front tangent (1.8 m ahead of its centre) and runs straight along the locked facing; walls clip it. Lane **I is on the boss's left, II in the centre, III on its right**, matching the left, centre and right bells.

| ID | Attack | Phase | Shape | Targeting and lock | Resolve (ticks from warning) | Active | Recovery | Cooldown | Damage | Kind |
|---|---|---|---|---|---|---|---|---|---|---|
| `spillway_note` | **Spillway Note** | 1 | Lane 2 m wide, 9 m long | Faces the rotating target for 48 ticks, then locks (1 s before resolve) | 108 | 21 (0.35 s) | 168 (2.8 s) | 360 (6 s) | 18 | Strike |
| `silt_steps` | **Silt Steps** | 1 | Three parallel lanes 1.5 m wide, 7 m long, **centres 2 m apart** (D2) | Lane II at the rotating target; locks at warning | I 108, II 192, III 276 | 15 each (0.25 s) | 216 (3.6 s) after III | 660 (11 s) | 18 per lane | Ground |
| `bellback` | **Bellback** | 1 and 2 | Cone from the boss centre, 110° wide, behind the boss, to 3.5 m past the boundary (5.3 m from centre) | 30-tick visible turn that puts the rear toward the nearest standing player, then facing locks at warning (D5) | 90 | 18 (0.3 s) | 150 (2.5 s) | 360 (6 s) | 18 | Strike |
| `split_spillway` | **Split Spillway** | 2 | Two parallel lanes 1.5 m wide, 9 m long, with a **2.6 m clear corridor** between them (D3) | One lane (alternating left/right each cast) tracks the rotating target for 54 ticks, then locks (1 s before resolve) | 114 | 21 (0.35 s) | 180 (3.0 s) | 420 (7 s) | 18 (once, both lanes are one instance) | Strike |
| `broken_steps` | **Broken Steps** | 2 | Silt Steps' lanes, order **I → III → II** | As Silt Steps | I 108, III 198, II 288 | 15 each | 228 (3.8 s) after II | 720 (12 s) | 18 per lane | Ground |
| `basin_toll` | **Basin Toll** (new) | 2 | Ring 2.5–5 m past the boundary (4.3–6.8 m from centre) | Centred on the boss; fixed at warning | 114 | 18 (0.3 s) | 204 (3.4 s) | 600 (10 s) | 26 | Ground |

**Kind** controls Brace: Strike attacks come from the boss and a Vanguard facing the boss can Brace them; Ground attacks are floor projections that bypass Brace.

**Per-attack notes** (behaviour from the packet, made exact):

- **Spillway Note.** The left bell tilts and a hollow drip-chime plays. The lane follows its target for 48 ticks, then visibly latches (border thickens, countdown starts) 60 ticks before resolve. Melee steps around the front corner beside the body; ranged crosses the lane sideways. Punish: 2.8 s.
- **Silt Steps.** All three lanes appear at warning, each with its numeral on the floor, and resolve in order with a separate bell note per lane (left, centre, right bell). Each lane is fully spent before the next resolves: 84 ticks between resolves, so a lane's fresh countdown runs 69 ticks after the previous lane clears. Because the lanes sit 2 m apart centre to centre, the 0.5 m strips between them are not safe once the player margin is added. The three lanes behave as one 5.5 m band, and **the refuge is the lane that just fired**: the packet's "follow lane I after it clears". Punish: 3.6 s after III.
- **Bellback.** The aqueduct shell rises with a grinding stone chord. Before warning, the boss turns its rear toward the nearest standing player over 30 ticks. The turn is slow, visible and harmless, so this is never an instant anti-backstab. Facing locks when the cone appears. Leave the rear for a flank. Punish: 2.5 s.
- **Split Spillway.** Twin drip-chime from the two outer bells. Both lanes resolve as one event, so standing in both is still one hit. The marked corridor is 2.6 m wide, leaving 2.0 m usable after the player margin on each side (the packet's ≥ 2 m corridor rule). Melee uses the corridor in front of the face or the flanks; ranged uses the corridor or steps outside. Punish: 3 s.
- **Broken Steps.** Same band as Silt Steps. At warning, a three-glyph sequence (I · III · II) appears on the floor in front of the boss, and the next lane's numeral and bell pulse. 90 ticks between resolves (fresh countdown 75 ticks after each clear). The order is always on the floor, never hidden memory; the trap is habit, not knowledge. Punish: 3.8 s after II.
- **Basin Toll.** All three bells descend and a deep bowl tone plays. The danger ring leaves a 1.9 m inner refuge band against the body (2.1–4.0 m from centre for player centres) and everything outside 7.1 m. Take the inner refuge, or pick the nearest outer edge and walk back in on recovery. This is the fight's only heavy hit. Punish: 3.4 s, the major recovery for Vanguard's heavy attack and the casters' stationary commitments.

**Body and movement.** Between attacks the boss walks at up to 1.5 m/s for at most 48 ticks, then plants all four feet for 12 ticks before the next warning. It walks a short arc toward the arena centre that keeps the nearest standing player 3–6 m from its boundary; it never retreats indefinitely from melee. Its body pushes players aside while walking (R20) but deals no contact damage.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Clear Channels | 100–50 % | Shell closed, three bells hung level | Each attack on its own; the spent lane is the refuge |
| 2 | Uneven Flow | 50–0 % | Back arch lifts, bells hang at staggered lengths, gold regulator core shows, ferns unfold | Read the announced order, choose the inner refuge |

**Shared phase rules:**

- **Queued transition.** Crossing 50 % finishes the current attack through its last active window (all lanes of a Steps sequence), retires its hazards, skips the rest of its recovery and plays a 120-tick (2 s) harmless transformation. The boss stays targetable; no heal, no new health bar.
- **One transition only.** A burst that crosses 50 % queues one transition. A lethal burst ends the fight immediately.
- **Defeat** cancels every warning and lane at once; nothing can land after victory. Defeat pose: the boss kneels its four legs, the bells settle and ring a clear water tone.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds its planted pose until it is. Windows are never shortened to fit a cooldown. The baseline loops below never need a hold.
- **Seeded variation** (rotating targets, Striking Hour orders) is chosen at block start from the encounter RNG (R6), with a pre-validated fallback chosen before the tell.

### 4.1 Phase 1 — Clear Channels (fixed loop, 1272 ticks = 21.2 s)

Walk → Spillway Note (297) → walk → Silt Steps (507) → walk → Bellback (288, including the turn) → repeat. Each walk slot is 60 ticks (48 walk + 12 plant).

This is the packet's Phase 1 example: Silt Steps → follow lane I → 3.6 s punish → Bellback → leave the rear.

Cooldowns check out (gap from recovery end to the next warning of the same attack): Spillway Note 975 (CD 360), Silt Steps 765 (CD 660), Bellback 984 (CD 360).

### 4.2 Phase 2 — Uneven Flow (fixed loop, 1710 ticks = 28.5 s)

Walk → Split Spillway (315) → walk → Broken Steps (531) → walk → Basin Toll (336) → walk → Bellback (288) → repeat.

This contains the packet's Phase 2 example: Broken Steps → wait in spent lane I while III, then II resolve → 3.8 s punish → Basin Toll → take the nearest refuge.

Cooldowns: Split Spillway 1395 (CD 420), Broken Steps 1179 (CD 720), Basin Toll 1374 (CD 600), Bellback 1422 (CD 360).

**Patterns never overlap in the baseline.** Every join is strictly sequential with a walk slot between.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

Same as the Bellkeeper's §5.1 and Sylvara's §5.1. `silt-carillon-escape-check.ps1`:
- samples every boss position in the bound (0–9 m, by symmetry) and every facing in 10° steps (full circle for the Steps chain, whose order isn't mirror-symmetric)
- finds the reachable danger point farthest from safety (0.1 m danger grid, 0.05 m boundary grid; shapes expanded by 0.3 m; player centres inside the arena and outside the boss body)
- converts that into walk-only and walk-plus-one-dash escape times, then into the minimum window per difficulty

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

**Steps are checked as a chain.** At lane k's resolve, every lane not yet spent counts as danger (the player may not shelter in a lane that is still warning), and spent lanes count as safe. The window for lane I is its full warning; for later lanes it's the time from the previous lane clearing.

**Limits:** straight-line travel, no other players, no acceleration, constant-speed dash, grid accuracy about ±0.05 m. This is evidence for single attacks and the Steps chain, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, placeholder arena; about 1 minute)

| Attack | d_max | Worst configuration |
|---|---|---|
| Spillway Note | 1.70 m | Boss at the bound, lane along the wall |
| Split Spillway | 2.01 m | Boss at the bound |
| Steps, first lane (all three pending) | 4.11 m | Boss 8 m out, band along the wall |
| Steps, second lane (two adjacent lanes pending) | 3.84 m | Boss at the bound |
| Silt Steps, lane III (last) | 2.08 m | Boss at the bound |
| Broken Steps, lane II (last) | 1.40 m | Boss at the bound |
| Bellback | 3.99 m | Boss 7 m out, rear toward the wall |
| Basin Toll | 2.58 m | From the middle of the ring |
| Ebb (Midnight Toll only, §9) | 4.38 m | Boss at the bound, player between body and wall |

Minimum window in ticks against this spec's Steady Ticking window. The window is counted after lock for the two Spillways, from the previous lane clearing for later Steps lanes, and from warning for the rest:

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Spillway Note | 53 / **61** | 34 | 28 | 25 | 60 |
| Split Spillway | 57 / **65** | 36 | 30 | 27 | 60 |
| Steps, first lane | 78 / 95 | 55 | 49 | 46 | 108 |
| Silt Steps, lane II | **75 / 91** | 51 | 45 | 42 | 69 |
| Silt Steps, lane III | 57 / 66 | 36 | 30 | 27 | 69 |
| Broken Steps, lane III | 75 / **91** | 51 | 45 | 42 | 75 |
| Broken Steps, lane II | 50 / 56 | 33 | 27 | 24 | 75 |
| Bellback | 76 / **93** | 54 | 48 | 45 | 90 |
| Basin Toll | 62 / 73 | 38 | 32 | 29 | 114 |
| Ebb | — | — | — | 50 | 90 |

Bold marks windows the baseline misses. First Chime fixes them (§9).

**Findings:**
- **Every packet timing is escapable at Steady Ticking and above at both walk speeds**, with room to spare.
- **At the 6 m/s design walk, the whole Steady Ticking baseline is walk-solvable.** Walk-only needs (reaction 0.45 s) are 66 ticks for the second Steps lane against 69 and 75 available, and 67 for Bellback against 90.
- **At the 4.2 m/s prototype walk,** the second lane of a Steps sequence needs one dash in the worst wall-side layout (82 walk-only against 69 or 75). Bellback and both Spillways stay walk-solvable (84 / 90, 51 / 60, 56 / 60).
- **The tight cases are all wall cases.** The band or rear cone pinned against the wall turns a 1–2 m sidestep into a 4 m walk. The director rotates Steps away from the wall (§6), which only makes these numbers smaller.
- **The packet's 2 m Split Spillway corridor would also be escapable**, but only 1.4 m of it is usable after margins. D3 widens it to meet the packet's own corridor rule.

### 5.3 Stamina budget

Same rule as the Bellkeeper and Sylvara: a worst-case player may need **at most one dash per 2.25 s**, and at most three in a row after 5 s without one. Sprint and jump are never required.

- **Design walk:** no dash is ever required.
- **Prototype walk:** at most one dash per Steps sequence (the second lane), at least 9 s apart. Well inside the rule.

### 5.4 Collision, body and hit-stun

- **Body collider: a 1.8 m circle** at the four planted feet. The legs plant separately and never slide. Moss, hanging bells and ferns never block movement; players can't walk under the chest. Collision never plugs the arena: at the boss-centre bound, a full player width remains between the body and the wall.
- **Lanes start at the front tangent**, so the pockets beside the body are always outside every lane. That gives the packet's "step around the front corner" a real place to stand.
- **Hit-stun:** 9 ticks (0.15 s), the packet's starting value.
- **No knockback** on any Silt Carillon attack (D7). Pushes only come from the walking body, which never walks during a warning.
- **Anti-chain rule.** For any two hazards that can hit the same player, the later resolve must come at least `stun + escape` after the earlier one. The tightest pair is consecutive Steps lanes: 84 ticks apart against 9 + 55 = 64 needed at 4.2 m/s with a dash. It passes.
- **One hit per instance** (R16). Each Steps lane is its own instance, so failing a whole Steps sequence costs 54 HP. Split Spillway's two lanes are one instance.

## 6. Party size (1–8)

**The Silt Carillon has no targeted markers**, so nothing scales with party size: the same geometry and timing serve solo and eight players. Only boss HP scales (§8).

| Attack | Aim |
|---|---|
| Spillway Note | Rotating target |
| Silt / Broken Steps | Lane II at the rotating target at warning |
| Split Spillway | Alternating lane on the rotating target |
| Bellback | Rear toward the nearest standing player |
| Basin Toll | Boss-centred |

**Targeting rules:**
- **Rotation** is round-robin by stable player ID from a seeded offset, over standing, active players at each warning. Solo, it's always the player.
- **Nearest** is measured to the boss centre at the start of the turn; ties go to the lowest stable player ID.
- **Wall rotation for Steps.** If more than half of the band's length would be clipped by the wall, the facing rotates toward the arena centre in 30° steps until it isn't, before warning.
- **Marked before lock.** The current Spillway target is tagged with a shape on the HUD and over the player, not colour alone.
- **Eight players.** Basin Toll's inner refuge band is about 18 m around at its middle, against 4.8 m of player bodies at eight players. The Steps band (5.5 × 7 m) covers 39 m² of the 415 m² placeholder arena.
- **Duplicate classes.** No assignment depends on class.

## 7. Down, revive and disconnect

The same as the Bellkeeper's §7:
- A downed or disconnected Spillway target before lock: a new target is chosen.
- After lock, every hazard is world geometry and resolves normally.
- Downed and inactive players hold no assignments and aren't hit.
- Disconnect is treated as downed.

Every change emits `assignment_changed`.

**Practice** starts the Silt Carillon at full health in Phase 1, from the practice seed, with health refilled. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage is the packet's, at Steady Ticking: 18 for Spillway Note, each Steps lane, Bellback and Split Spillway; 26 for Basin Toll. That matches the current run-loop mini-boss stand-ins (18–22). The worst single mistake is 26 HP, and a full Steps failure is 54. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP** = measured reference sustained solo DPS × 120 s, measured during movement and recovery, not on a stationary dummy. That aims for the packet's 90–150 s target. At an illustrative 40 DPS that's 4800 HP. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only. Tune recoveries and walk slots before HP if melee and ranged clear times differ by more than 15–20 %.

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are not uniformly sped up.

| Tier | Silt Carillon changes |
|---|---|
| **1 — First Chime** | Spillway Note and Split Spillway lock 72 ticks before resolve (tracking stays 48 / 54, warnings +12). Bellback warning 102. Steps lanes 108 ticks apart (fresh countdown 93). All recoveries +0.5 s. Phase 2 drops Bellback from the loop. Every baseline miss in §5.2 is then walk-solvable at 4.2 m/s |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | **Varied order:** Broken Steps draws its order from the five non-left-to-right orders of I, II, III, always shown as glyphs at warning. **Steps into Note** (Phase 1): Spillway Note's warning starts when lane III clears, so players settling into spent lanes must read a tracking lane; Steps' 3.6 s punish moves to after the Note, keeping Phase 1's major opening. **Toll into Back** (Phase 2): Bellback's turn starts when Basin Toll's ring clears, catching players who settled in the inner refuge. Recoveries −20 %, never below 0.6 s, except Basin Toll's 3.4 s, which stays as the major opening |
| **4 — Midnight Toll** | Striking Hour plus **Ebb** |

**Ebb** (the highest-tier mechanic, D8): Basin Toll gains a second beat.
- From Basin Toll's warning, a second, dim ring marked **II** shows the inner refuge (boss boundary to 2.5 m), so the follow-up is visible before the first toll lands.
- When the outer ring clears, ring II arms with a fresh 90-tick warning and a rising bowl tone, then resolves (18 active, 18 damage).
- The refuge flips: players in the inner band step outward across the spent ring; players outside stay out.
- It is a separate hazard instance. It counts as part of the Basin Toll pattern for the two-pattern limit.
- Escape is 4.38 m worst case (wall side), 50 ticks needed against 90, walk-solvable at 4.2 m/s (81).

Striking Hour joins leave only spent hazards when the next one warns, so there's never more than one live damaging pattern. Every Striking Hour window clears its §5.2 minimum.

## 10. Requests to the shared C# contract (#6)

The Silt Carillon reuses the Bellkeeper's and Sylvara's requests (R1, R4, R5, R6, R7, R10 lane, R11, R12, R13, R14, R16, R18, R20, R21; R22 active-duration hazards; R23 ordered hazard groups; R25 sector shape). It adds:

| # | Need | Why |
|---|---|---|
| R26 | **Pre-warning reorientation** as a published boss action (start, end, facing) that is not a warning and cannot deal damage | Bellback's visible turn, so records and validators can tell it apart from an instant attack |
| R27 | **Group-level order display:** a hazard group publishes its full member order at group warning, plus a `next_member` event when each member clears | Broken Steps' I · III · II glyphs and the pulse on the next lane, driven from the same truth as the hits |

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `silt_carillon`. Additions:
- `group_id` and `member_index` (Steps lanes I/II/III, Ebb's ring II)
- `lane_order` on Steps groups (for example `[1,3,2]`)
- `turned_from` on Bellback (the facing before the R26 turn)

Example card:

> *"Broken Steps, lane III (1:48, Phase 2). You were 0.6 m inside lane III when it fired. Lane I had been spent for 1.2 s: the glyphs read I · III · II."*

## 12. Model, animation and asset notes

From the packet and sheet, with gameplay constraints:

- **Silhouette:** squat moss-covered stone chime chest on **exactly four** short brass-root legs; three thick bells under the front arch; small ivory face above them; rounded aqueduct-shell back with a turquoise rose window. Quadrupedal and compact, clearly not Sylvara's tall shrine.
- **Scale (D1):** the sheet reads about 4 m tall and 6 m across next to its 1.8 m figure, but the packet says 2.7 m. Model it 2.7 m tall with the leg tips on the 1.8 m collider circle (about 3.6 m across). Keep the sheet's proportions otherwise.
- **The rear must read from the isometric camera**, because Bellback's warning is the shell rising. Keep the back shell's silhouette distinct from the bell front: the rose window and shell ridge face back, the face and bells face front.
- **Bell-to-lane mapping:** left bell = lane I, centre = II, right = III. In Phase 2 the bells hang at staggered lengths (centre lowest on the sheet); the bell for the next Broken Steps lane swings as its numeral pulses.
- **Rig:** four legs with separate planting (no sliding), three independently swinging bells, liftable back arch and shell, face, fern unfurl for Phase 2, restrained moss secondary motion.
- **Animations:** idle (bells sway), walk and plant, conduct (Steps), left-bell tilt (Spillway Note), outer-bell tilt (Split Spillway), turn and shell rise (Bellback), bells descend (Basin Toll), phase transformation (arch lifts, core shows, ferns unfold), defeat (legs kneel, clear water tone).
- **VFX:** coral lanes with amber hatching, large floor numerals I/II/III, a glyph strip for the Broken Steps order, a spent-lane state (dry, flat, clearly safe). Waterfalls, mist and turquoise inlays are **decoration only** and never share a hazard's colour or edge. Water is never on the floor.
- **Audio:** hollow drip-chime (Spillway), twin drip-chime (Split), a distinct note per lane (Steps), grinding stone chord (Bellback), deep bowl tone (Basin Toll), rising bowl tone (Ebb), water-tone victory sound. Every cue also has a visual, so the fight reads with audio off.
- **Reward proposal:** a Clear-Water chime trophy with the water-tone victory sound. Cosmetic only, deferred with the rest of rewards.

## 13. Validation tests

| # | Check |
|---|---|
| T1 | Escape check rerun on the real Verdant Chimes arena and body collider, both movement sets |
| T2 | Both phase loops at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Steps: every wall-rotated layout keeps the second-lane escape within its window at 4.2 m/s with one dash |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects during a Steps sequence and during Bellback's turn |
| T5 | The 50 % threshold during each Steps lane, during Bellback's turn and during Basin Toll: one transition, no hazard survives into it, seals and bells persist |
| T6 | Brace mitigates Spillway, Split Spillway and Bellback only when facing the boss, and never Steps, Basin Toll or Ebb |
| T7 | Audio-off and reduced-effects readability; Broken Steps order is readable from the glyphs alone; turquoise decoration is never mistaken for a lane |
| T8 | Melee and ranged clear times within 15–20 % for similar mastery; Basin Toll's recovery is long enough for Vanguard's heavy attack from the inner refuge |

## 14. Decisions made in this draft

The user asked for packet conflicts to follow existing decisions and for open points to be decided here. These are the choices, open to review:

| # | Decision | Choice | Why |
|---|---|---|---|
| D1 | Boss scale and collider | 2.7 m tall (packet text), 1.8 m collider; rescale the sheet's ~4 m figure | Geometry is measured from the boundary; the packet's numbers assume its own size |
| D2 | Steps lane spacing ("2 m apart" is ambiguous) | **Centres 2 m apart**, a 5.5 m band | With 2 m edge gaps, players stand in the gaps and the lane order never matters. As a band, the spent lane is the refuge, which is what the packet describes |
| D3 | Split Spillway corridor | **2.6 m** (packet 2 m) | Leaves 2 m usable after the 0.3 m margin each side, the packet's own corridor rule |
| D4 | Which tier the packet's numbers are | **Steady Ticking** | Consistent with the Bellkeeper and Sylvara specs |
| D5 | When Bellback fires | **Fixed loop slot**, after a 30-tick visible turn | The packet forbids an instant anti-backstab; a scheduled, telegraphed turn can't punish a backstab instantly |
| D6 | Targeting | Rotating target for Spillways and Steps; nearest for Bellback | No markers, so nothing needs party scaling |
| D7 | Knockback | **None**; hit-stun 0.15 s only | Packet default; avoids wall pinning beside the Steps band |
| D8 | Midnight Toll mechanic | **Ebb**, Basin Toll's inner refuge tolls second | Original to this boss, visible before it's lethal, reverses the lesson the fight teaches |
| D9 | Arena | Placeholder 11.5 m player bound | No Verdant Chimes collision exists yet; rerun T1 when it does |
| D10 | Damage | Packet values (18 / 26) | Matches the run-loop mini-boss stand-ins |
