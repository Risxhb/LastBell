# Rivet Cantor, Tuner of the Great Cast — encounter specification

Draft v0.1 · October 21, 2026 · Lane: Mini-bosses / Brass Foundry 2-1 · Source packet: [nine mini-boss handoff, PR #49](https://github.com/Risxhb/collab/pull/49) (`docs/nine-mini-bosses-20261021`, commit `a2ab311`, `docs/mini-bosses/design.md` §4 and the shared mini-boss contract).

Status: **specification only, nothing here is implemented.** It is engine-independent and targets the Unity/C# production path. It follows the structure and method of the [Bellkeeper spec](https://github.com/Risxhb/collab/blob/docs/bellkeeper-spec/docs/design/bellkeeper.md) ([PR #16](https://github.com/Risxhb/collab/pull/16)) and the [Sylvara spec](https://github.com/Risxhb/collab/blob/docs/sylvara-spec/docs/design/sylvara.md) ([PR #50](https://github.com/Risxhb/collab/pull/50)), so all of them can share one validator and one set of contract requests.

Rivet Cantor is the **first of Brass Foundry's three mini-bosses** (then Crucible Skater, then Escapement Twins, then the area boss Crucivar) in the campaign reference ([#44](https://github.com/Risxhb/collab/issues/44)). It is not part of the current one-arena three-plus-one slice. The packet's identity, abilities, geometry, timings and damage are taken as given. This spec adds what the packet leaves open: ticks, schedules, targeting, party scaling, the four difficulty tiers, escape evidence, and fixes where the packet breaks its own rules (§14).

Reference art: the Rivet Cantor row of [`02-brass-foundry.png`](https://github.com/Risxhb/collab/blob/docs/nine-mini-bosses-20261021/docs/mini-bosses/02-brass-foundry.png), Phase 1 left and Phase 2 right.

Companion tool: [`tools/encounter-escape/rivet-cantor-escape-check.ps1`](../../tools/encounter-escape/rivet-cantor-escape-check.ps1).

## 1. Rules this encounter follows

These come from the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) and the packet's shared mini-boss contract. Where they disagree, the game design wins:

- **No invincibility of any kind.** Dash gives no immunity, and there is no post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once; separately warned beats are separate instances.
- **No mandatory healer, block, parry, upgrade or dash.** Any mix of Vanguard, Arcanist and Chimesinger, including duplicates, can clear. Vanguard's Brace is the approved partial frontal mitigation; nothing here assumes it.
- **Cursor aim, cooldown-only boss attacks.** The boss never reads player inputs: nothing reacts to a dash, cast or button press.
- **Finite stamina governs routes** (§5.3). Attacks never consume it.
- **No attack can be jumped.** Jump state never changes resolution.
- **Health:** health refills before each boss; potions carry between mini-bosses. Rivet Cantor is the first fight of the area, so players arrive at full health with whatever potions they have left after Skyward Spire's area boss.
- **Boss stays targetable** through the phase change. Arcanist seals, Chimesinger bells and other player setup persist with their timers running.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. The mallet and clamp poses are tells, not hitboxes.
- **Locks are final.** Nothing retargets after lock.
- **No contact damage, knockback, slow, stun or damage over time**, apart from the shared 0.15 s hit-stun.
- **One damaging pattern at a time.** Multi-beat attacks retire each footprint before the next one resolves.

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.** As in the packet, reach is measured **from the boss collision boundary** unless stated otherwise.

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Game design #33; Unity prototype #27 / #42 |
| Sprint | 9 m/s, never required | Game design |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside | Bellkeeper spec (replaces the packet's 0.45 m + 0.25 m estimate) |
| Player health | 100 HP | Packet |
| Boss body | **1.6 m radius** collision circle (the 3.2 m broad anvil torso); legs plant inside it | This spec |
| Boss walk | **3.0 m/s**, slower than every player (§3.2) | This spec |
| Arena | **Placeholder:** circle, player centres ≤ 11.5 m and boss centre ≤ 9.5 m from the centre (the packet proposes a 24 m diameter) | No Brass Foundry arena collision exists yet (§14, C9) |

The boss-centre bound keeps a full player width between the body and the wall, so no one can be pinned. Rerun the escape check when the real arena exists.

## 3. Attack vocabulary

Six attacks: three in Phase 1, two evolved plus one new in Phase 2, and Tuning Fork kept unchanged. Geometry, damage and the packet's W/A/R/CD (first warning to damage / active duration / guaranteed recovery / minimum cooldown) are the packet's. Ticks are their exact conversion. Steady Ticking uses them unchanged except where §14 says otherwise.

| ID | Attack | Phase | Shape | Targeting | Tracking and lock | Warning → active (ticks) | Active | Recovery | Cooldown | Damage |
|---|---|---|---|---|---|---|---|---|---|---|
| `rivet_beat` | **Rivet Beat** | 1 | Cone from the boss centre, 90° wide, reach 3.5 m from the body (5.1 m from centre) | Nearest standing player | Locks at warning | 72 | 12 | 120 (2.0 s) | 240 (4 s) | 18 (M) |
| `held_note` | **Held Note** | 1, and first beat of Split Note | Disk, radius 2 m, centred 2 m ahead of the body (3.6 m from centre) | Nearest standing player | **Tracks 48 ticks, then locks 72 before impact** (C2) | 120 | 15 | 192 (3.2 s), stuck mallet | 480 (8 s) | 26 (H) |
| `tuning_fork` | **Tuning Fork** | 1 and 2 | Lane 1.4 m wide, from the body edge to 10 m, clipped by the arena | Rotating ranged target (§6) | Tracks 42 ticks, then locks **54 before damage** (packet's 0.9 s) | 96 | 21 | 156 (2.6 s) | 420 (7 s) | 18 (M) |
| `alternating_rivets` | **Alternating Rivets** | 2 | Two cones, 75° wide, reach 3.5 m from the body (C6), on the boss's **left and right flanks**, numbered I and II | Facing locks toward the nearest standing player; I is the flank with more players (seeded tie-break) | Both lock at warning | I: 72; II warns at tick 84 (when I clears) for 78 | 12 each | 180 (3.0 s) after II | 360 (6 s) | 18 (M) per beat |
| `split_note` | **Split Note** | 2 | Held Note's disk, then **two parallel lanes 1.2 m wide and 6 m long from the body edge, with a 2.6 m clear gap between them** (C1; packet 2 m) along the disk's axis | As Held Note; lanes follow the locked facing | Disk as Held Note; lanes are fixed at the disk's impact | Disk 120; lanes warn at tick 135 for 90 | 15 each | 216 (3.6 s) after the lanes | 600 (10 s) | 26 disk (H), 18 lanes (M) |
| `rest_stroke` | **Rest Stroke** (new) | 2 | Ring pulse around the body, reach 2 m (3.6 m from centre) | None | Centred on the boss | 96 | 15 | 210 (3.5 s), arms inert | 660 (11 s) | 18 (M) |

**Per-attack notes** (behaviour from the packet, made exact):

- **Rivet Beat.** The mallet comes to the shoulder with a sharp ping. It is a short, low pose and must never look like Held Note's overhead pose. Melee steps to a flank; ranged stays out of the 5.1 m reach.
- **Held Note.** The mallet goes straight overhead and a rising metallic hum starts. A three-tooth countdown sits on the clamp: **tooth 1 drops at lock (tick 48)**, tooth 2 at tick 84 and tooth 3 at impact (tick 120). Until tooth 1 drops, the disk outline slides with the target. After that it is latched and drawn solid. The packet's lesson is "wait for the lock, then sidestep", and the first tooth makes the lock readable. The mallet sticks in the floor for the 3.2 s recovery. This is the encounter's main punish.
- **Tuning Fork.** The clamp points and sings. The lane outline tracks for 0.7 s, then latches 0.9 s before the bolt. Ranged players sidestep rather than run down the lane. It deals damage only; there is no cast-interrupt effect (C5).
- **Alternating Rivets.** The packet's "two opposite cones" are read as the two flanks, which is where Phase 1 taught melee to stand. This keeps the packet's "hold the unmarked rear" answer valid. Front and rear stay open (two 105° pockets). Cone I warns and resolves, then cone II warns for its own full 1.3 s with a lower ping. Each cone shows its numeral. Players can stay in front or behind, or cross into cone I after it clears.
- **Split Note.** The first beat is identical to Held Note, tell and lock included. On impact, a fork-shaped outline draws the two lanes along the same axis. Their centre gap is the disk's axis, so a player who sidestepped the disk walks back to the axis, or out to an outer flank. The disk is spent before the lanes resolve. The 3.6 s punish starts after the lanes.
- **Rest Stroke.** The clamp opens with a hollow clang and a ring outline appears around the boss. After the pulse, both arms hang limp for 3.5 s. Melee briefly steps out and comes back; ranged keeps casting.

### 3.1 Phase 2 visual rule

The off-beat is shown **on the second mallet face** (the split resonator lights for beat II of Alternating Rivets and for Split Note's lanes). It is never hidden in VFX. Phase 2 changes the silhouette (flared soundboard plates, split mallet head), not just the emission colour.

### 3.2 Movement between attacks

Rivet Cantor walks at 3.0 m/s to bring its next target within 2–4 m of its body. The approach lasts at most 90 ticks (1.5 s), then the next attack warns from wherever the boss stands. Tuning Fork and Rest Stroke need no approach. The boss never moves during a warning, active window or recovery. When a player is in its path, it stops and steers around them rather than pushing: there is no body shove on this boss.

## 4. Phases and timeline

| Phase | Name | Boss HP | Visual | What it teaches |
|---|---|---|---|---|
| 1 | Tempered Measure | 100–50 % | Closed soundboard, single mallet face | Shoulder beat versus overhead hold; lane locks |
| 2 | Split Harmonic | 50–0 % | Flared plates, split mallet with two connected faces | Two-beat reads: beat I, then beat II; disk then lanes |

**Phase rules** (from the packet's shared contract):

- **Queued transition.** Crossing 50 % queues the transition. The current attack finishes, its hazards expire, and its recovery runs in full (a punish window is never shortened). Then a 120-tick (2 s) harmless transformation plays: plates flare, mallet head splits. The boss is targetable throughout. No heal, no new health bar.
- **One transition only.** A burst that crosses 50 % queues one transition; a lethal burst ends the encounter immediately.
- **Phase 2 starts fresh:** its first block is always Alternating Rivets, and Phase 2 cooldowns start ready. Tuning Fork keeps its running cooldown.
- **Defeat** cancels every warning and outline at once; nothing lands after victory. Defeat pose: the mallet lowers to the floor and rings one clean, held note; the core stays lit.
- **Cooldown hold.** If an attack's cooldown isn't ready when its turn comes, the boss holds its recovery pose until it is. Windows are never shortened to fit a cooldown.
- **Seeded variation** (Phase 2 order, flank choice, ranged target) is chosen at block start from the encounter RNG (R6), before the tell.
- **No third phase, enrage or death retaliation.**

### 4.1 Phase 1 — Tempered Measure (fixed loop, 807 ticks = 13.45 s plus approaches)

Rivet Beat → Held Note → Tuning Fork → repeat.

This is the packet's example ("Held Note → long punish → Tuning Fork"). Cooldown gaps: Rivet Beat 603 (CD 240), Tuning Fork 534 (CD 420), Held Note 477 against CD 480. That 3-tick shortfall is covered by a **3-tick cooldown hold** in Held Note's place, which is invisible in play. Phase 1 has no order variation, because the two other orderings break a cooldown on the next loop. Variation comes from target choice.

### 4.2 Phase 2 — Split Harmonic (loop of four, 1404 ticks = 23.4 s plus approaches)

Alternating Rivets → Split Note → {Tuning Fork, Rest Stroke in seeded order} → repeat.

Opening with Rivets → Split Note is the packet's example. Both orders of the last two attacks, and any switch between them from one loop to the next, keep every cooldown: Rivets 1050, Split Note 948, Tuning Fork ≥ 810, Rest Stroke ≥ 810. There are no holds.

**Patterns never overlap.** Every join is strictly sequential.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

Same as the Bellkeeper's §5.1 and Sylvara's §5.1. `rivet-cantor-escape-check.ps1`:
- samples every boss position in the bound (0–9.5 m, by symmetry) and every facing in 10° steps
- finds the reachable danger point farthest from safety (0.1 m danger grid, 0.05 m boundary grid; shapes expanded by 0.3 m; player centres inside the arena and outside the boss body)
- converts that into walk-only and walk-plus-one-dash escape times, then into the minimum window per difficulty

Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

**Multi-beat attacks are checked beat by beat.** When beat II of Alternating Rivets resolves, cone I is spent, so the danger is one 75° cone. Split Note's lanes are checked with the disk spent.

**Edge rule (C4)** is applied by default. Without it, a Rivet Beat aimed at the wall from 7 m needs a 3.5 m escape, and Rest Stroke with the boss at the bound needs 3.7 m. In both cases the player is trapped between the shape and the wall. `-NoEdgeRule` reproduces those numbers.

**Limits:** straight-line travel, no other players, no acceleration, constant-speed dash, grid accuracy about ±0.05 m. This is evidence for single attacks and their beats, not proof that every combination is solvable.

### 5.2 Results (run on October 21, 2026, placeholder arena, edge rule on)

| Attack | d_max | Worst configuration |
|---|---|---|
| Rivet Beat (90°) | **2.40 m** | Boss 5 m from centre |
| Held Note / Split Note disk | 2.31 m | From the disk centre |
| Tuning Fork | 1.40 m | Boss at the bound, lane along the wall |
| Alternating Rivets, per beat | 2.21 m | Boss 5 m from centre |
| Split Note lanes | 1.57 m | Boss at the bound |
| Rest Stroke | 2.00 m | From the body edge |

Split Note's centre corridor for player centres is **2.0 m** (2.6 m gap minus the margin on both sides), which meets the packet's 2 m corridor rule. The packet's 2 m gap would leave 1.4 m.

Minimum window in ticks, counted after lock (Held Note, Tuning Fork) or from warning (the others), compared with this spec's Steady Ticking window:

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Rivet Beat | 60 / 71 | 37 | 31 | 28 | 72 |
| Held Note / Split Note disk | 60 / 69 | 37 | 31 | 28 | 72 after lock |
| Tuning Fork | 50 / **56** | 33 | 27 | 24 | 54 after lock |
| Alternating Rivets, per beat | 59 / 68 | 36 | 30 | 27 | 72 (I), 78 (II) |
| Split Note lanes | 52 / 59 | 34 | 28 | 25 | 90 |
| Rest Stroke | 56 / 65 | 35 | 29 | 26 | 96 |

**Findings:**
- **Every packet timing is escapable at every tier**, given the edge rule.
- **The whole Steady Ticking baseline is walk-solvable at both speeds.** The tightest is Rivet Beat at 4.2 m/s: 0.45 s reaction + 0.57 s walk = 62 ticks, against 72. No attack ever needs a dash.
- **Tuning Fork's 0.9 s lock is 2 ticks short for a walk-only 4.2 m/s player on First Chime** (56 needed, 54 given), so First Chime locks it 6 ticks earlier (§9).
- **Rivet Beat on First Chime at 4.2 m/s** passes by 1 tick (71 against 72). First Chime adds 12 ticks anyway, for teaching.

### 5.3 Stamina budget

Same rule as the Bellkeeper and Sylvara: a worst-case player may need **at most one dash per 2.25 s**, and at most three in a row after 5 s without one. Sprint and jump are never required.

Rivet Cantor needs no dash at all at Steady Ticking or First Chime, at either walk speed. Dashes are optional uptime: for example, dashing back to the flank during Held Note's stuck-mallet recovery.

### 5.4 Collision, body, edge rule and hit-stun

- **Body collider: a 1.6 m circle** around the torso. The legs plant inside it; the mallet and clamp never block movement. The boss stops for players and steers around them rather than pushing (§3.2).
- **Edge rule (C4).** Rivet Beat and both Alternating Rivets cones are legal only if their outer arc stays at least one player width (0.6 m) from the wall plus the margin. Rest Stroke is legal only if its ring leaves the same corridor, which means the boss centre must be within 7.0 m of the arena centre. When a cast fails the rule, the director first rotates the facing toward the arena centre in 10° steps. If no facing passes (Rest Stroke), the boss walks inward before warning. Both happen before the tell, so warnings never change. This is the packet's own "rotate or reject casts that eliminate the escape corridor".
- **Hit-stun:** 9 ticks (0.15 s), the packet's ceiling.
- **No knockback** on any Rivet Cantor attack.
- **Anti-chain rule.** For any two hazards that can hit the same player, the later one must resolve at least `stun + escape` after the earlier one.
  - Split Note (disk, then lanes): the lanes resolve 90 ticks after the disk, against a need of 9 + 50 = 59 at 4.2 m/s walking. It passes.
  - Alternating Rivets: a player hit by cone I is on the opposite flank from cone II, so the pair can't chain.
  - Midnight Toll's return bolt (§9): 93 ticks after the first bolt, against 9 + 38. It passes.
- **One hit per instance** (R16). Each beat, disk and lane pair is its own instance. A player who takes the disk and then also stands in a lane takes 26 + 18.

## 6. Party size (1–8)

The packet's rule: same geometry and timings for every party size. Rivet Cantor has **no targeted markers**, so the 1/2/3 marker scaling doesn't apply. Party size changes only who the boss faces.

| Attack | 1 | 2–8 |
|---|---|---|
| Rivet Beat, Alternating Rivets | Faces the player | Faces the nearest standing player |
| Held Note, Split Note | Tracks the player | Tracks the nearest standing player |
| Tuning Fork | Aims at the player | Aims at a rotating standing player more than 4 m from the body; if none, the nearest |
| Rest Stroke | Centred on the boss | Same |

**Targeting rules:**
- **Nearest** is measured to the body edge at warning; ties go to the lowest stable player ID.
- **Tuning Fork rotation** is round-robin by stable player ID from a seeded offset, skipping the previous target if another eligible player exists. This keeps the lane on casters, as the packet intends ("tuning lane interrupts casts"), without repeatedly pinning one Arcanist.
- **Flank choice** for Alternating Rivets: cone I goes to the flank holding more standing players within reach. On a tie it is seeded.
- **Marked before lock.** The Tuning Fork target is tagged with a shape on the HUD and over the player, not by colour alone.
- **Duplicate classes.** No assignment depends on class.
- **Crowding.** Eight players fit around a 1.6 m body: the two flank pockets and the rear each hold several melee players without overlap. Hazard density never scales with player count.

## 7. Down, revive and disconnect

The same as the Bellkeeper's §7:
- A target downed before lock: a new nearest or rotating target is chosen.
- After lock, every hazard is world geometry and resolves normally. A target lost after lock leaves the marked location unchanged.
- Downed and inactive players hold no assignments and aren't hit.
- Disconnect is treated as downed.

Every change emits `assignment_changed`.

**Practice** starts Rivet Cantor at full health in Phase 1, from the practice seed, with the run's current potion count. **Fresh run** resets everything.

## 8. Damage and boss HP

Damage is the packet's normalized scale at Steady Ticking: medium 18 and heavy 26 against 100 HP. Light (10) isn't used. The worst single mistake is 26 HP (Held Note). The worst two-hit sequence is a full Split Note failure, 44 HP. First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.

**Boss HP** = measured reference sustained DPS × 120 s (solo), measured during movement and recovery, not on a stationary dummy. That is the middle of the packet's 90–150 s target. At an illustrative 40 DPS that's 4800 HP. Co-op multiplier: `1 + 0.7 × (n − 1)`, applied to HP only. Tune HP after measured clears, and tune movement and recovery first (packet).

## 9. Difficulty tiers

Mechanics change, not just numbers. Warnings are never uniformly sped up. Every tier keeps at least one 3–4 s major recovery (Held Note, and Split Note in Phase 2), as the packet requires.

| Tier | Rivet Cantor changes |
|---|---|
| **1 — First Chime** | Rivet Beat warning +12 ticks (84). Tuning Fork locks 60 ticks before damage (warning 102). All recoveries +0.5 s. Phase 2 opens with one Rest Stroke-free loop: Rivets → Split Note → Tuning Fork |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | Minor recoveries −20 % (Rivet Beat 96, Tuning Fork 125, Alternating Rivets 144, Rest Stroke 168); Held Note and Split Note recoveries unchanged. Phase 1 becomes **Measure Break**: Rivet Beat → Held Note → Rivet Beat → Tuning Fork (929 ticks, all cooldowns met). The second Rivet Beat catches melee leaving the stuck mallet late, and the Tuning Fork that follows always aims at the farthest player |
| **4 — Midnight Toll** | Striking Hour plus **Returning Note** |

**Returning Note** (Midnight Toll's added mechanic, this spec's proposal; the packet names none for this boss):
- Tuning Fork's bolt reaches the end of its lane and comes back along the same locked lane.
- It is visible from the first warning: both tines of the clamp glow, instead of one.
- After the first bolt's active window, the lane stays outlined with a numeral **II** and a falling pitch. It re-warns for a full 72 ticks, then is active for 21.
- It is a separate hazard instance, so it can hit again. Recovery (125 ticks) starts after the return.
- The lesson: stay out of a lane you just left. Escape is the same as the first bolt (1.4 m).

## 10. Requests to the shared C# contract (#6)

Rivet Cantor reuses the Bellkeeper's and Sylvara's requests: R1 circle, R4 reassign/cancel before lock, R5 presentation markers, R6 seeded RNG, R7 active flag, R10 lane, R11 mutable boss position, R12 HP phases, R13 HP damage, R14 recovery event, R16 one hit per instance, R18 standing state, R20 body collision, R21 published movement constants, R22 active-duration hazards (every attack here has an active window), R23 ordered hazard groups (Alternating Rivets I/II, Split Note disk and lanes, Returning Note), and R25 sector shape. It adds:

| # | Need | Why |
|---|---|---|
| R26 | **Arena-aware legality before warning:** the director can test a candidate cast against the arena bound and rotate or reposition before the tell, emitting a `cast_adjusted` event | The edge rule (§5.4). Sylvara's Bloom chain rotation needs the same thing |
| R27 | **Ring (annulus) shape** with inner radius at the body edge | Rest Stroke. A plain circle works too, since players can't stand inside the body, but an annulus keeps the drawn outline and the hit area the same |

Rivet Cantor has no stagger mechanic, so it doesn't use R24.

## 11. Resolved records

Same fields as the Bellkeeper's §12, with `encounter_id` = `rivet_cantor`. Additions:
- `group_id` and `member_index` (Rivets I/II, Split Note disk/lanes, Returning Note)
- `cast_adjusted` with the original and the legal facing or position (edge rule)
- `lock_tick` on Held Note and Tuning Fork, so a death card can say where the lock happened

Example card:

> *"Held Note (1:04, Phase 1). You were 1.1 m inside the disk. It locked 1.2 s before impact, when the first tooth dropped. Sidestep after the tooth, not before."*

## 12. Model, animation and asset notes

From the packet and sheet, with gameplay constraints:

- **Silhouette:** compact, broad anvil-shaped torso about 3.2 m across, charcoal enamel with an aged-brass soundboard, an ivory recessed face mask with a brass halo, one gold core in the chest, and two sturdy legs. The right arm ends in a large drum-shaped ceremonial mallet; the left arm ends in a flat two-tine tuning-fork clamp. It is clearly smaller than Crucivar, with no furnace torso and no crucible hammer.
- **Mallet attachment (C7):** the mallet is built into the arm, as the packet and the Phase 1 view show. The Phase 2 view shows a hand gripping a handle. The model keeps the integrated mount in both phases, and only the head splits.
- **Phase 2:** the chest soundboard plates hinge open into two wing-like panels, and the mallet head separates into two connected parallel faces. Arms and legs are unchanged.
- **Poses must differ in silhouette from the high isometric camera:** shoulder (Rivet Beat, mallet low and back), overhead (Held Note, mallet vertical), clamp-forward (Tuning Fork), clamp-open (Rest Stroke), and arms out to both sides (Alternating Rivets).
- **Rig:** mallet arm, clamp arm with two tines that can glow independently (Returning Note), three countdown teeth on the clamp, hinged soundboard plates, split mallet faces, planted feet with no sliding.
- **Animations:** idle, walk, shoulder beat, overhead hold and stuck-mallet recovery, fork point and sing, flank rivets I and II, split note, rest stroke with limp arms, the 2 s transformation, defeat.
- **VFX:** coral/amber outlines with hatching and numerals for every footprint, the latched (solid) versus tracking (dashed) outline states, a fork-shaped outline for Split Note's lanes, and the ring for Rest Stroke. Turquoise is never used for danger. Furnace light in the arena is decoration only.
- **Audio:** sharp ping (Rivet Beat), rising hum and three tooth clicks (Held Note), sung tone (Tuning Fork), high then low ping (Rivets I/II), hollow clang (Rest Stroke), falling pitch (Returning Note), one held clean note at defeat. Every cue also has a visual, so the fight reads with audio off.
- **Reward proposal (packet):** Perfect-Pitch rivet trophy and a tuning-fork weapon ornament. Cosmetic only; the inventory and collection systems are deferred.

## 13. Validation tests

| # | Check |
|---|---|
| T1 | Escape check rerun on the real Brass Foundry arena and body collider, both movement sets |
| T2 | Every authored loop in §4, at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Edge rule: no Rivet Beat, Rivets cone or Rest Stroke ever warns without a player-wide corridor to the wall, and every adjustment happens before the tell |
| T4 | Parties of 1–8, including all-Vanguard, all-Arcanist and all-Chimesinger, with downs and disconnects mid-warning |
| T5 | Phase threshold crossed during Held Note, during its stuck-mallet recovery, and during a lethal burst |
| T6 | Readability: shoulder versus overhead pose and tracking versus latched outlines at the isometric camera, audio off, reduced effects |
| T7 | Melee and ranged median clear times within 15–20 % of each other (packet), tuning movement and recovery before HP |

## 14. Choices made in this spec

The packet left these open or broke its own rules. Per the user's direction, earlier project decisions win, and anything not yet discussed is decided here. Each item can be reopened.

| # | Item | Decision |
|---|---|---|
| C1 | Split Note's 2 m centre gap leaves a 1.4 m player corridor, below the packet's own 2 m minimum | **Gap widened to 2.6 m** (2.0 m corridor) |
| C2 | Held Note says "wait for lock" but gives no lock time; the shared contract locks at warning unless stated | **Tracks 48 ticks, locks 72 before impact**, shown by the first countdown tooth |
| C3 | No target selection for party play | Nearest player for melee attacks; a rotating player more than 4 m away for Tuning Fork (§6) |
| C4 | Cones and the pulse can trap players against the wall | **Edge rule** (§5.4), from the packet's own "rotate or reject" |
| C5 | "Tuning lane interrupts casts" | Damage and movement pressure only; no interrupt effect |
| C6 | Alternating Rivets: what "opposite" cones means, and their reach | **Left and right flanks**, keeping front and rear open; reach 3.5 m like Rivet Beat |
| C7 | Phase 2 art shows a hand-held mallet | Integrated mallet arm in both phases |
| C8 | The packet says its numbers fit the third tier (Striking Hour) | **Packet numbers are the Steady Ticking baseline**, the same convention as the Bellkeeper and Sylvara specs. Rivet Cantor opens the area, so it shouldn't start at the hard tier |
| C9 | No Brass Foundry arena dimensions | 11.5 m placeholder circle, the same as Sylvara; rerun T1 when the real arena exists |
| C10 | No Midnight Toll mechanic for this boss | **Returning Note** (§9) |
| C11 | Hit margin: the packet estimates 0.45 m + 0.25 m clearance; the Bellkeeper spec uses 0.3 m | **0.3 m**, the established spec value |
| C12 | Boss walk speed is not given | 3.0 m/s, approach capped at 1.5 s |
