# The Bellkeeper — encounter specification

Draft v0.2 · October 21, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8)) · Reviewer: `risxhb/codex`; `chzy18/codex` for session-facing rules.

Status: **specification only, nothing here is implemented.** It is engine-independent and targets the Unity/C# production path.

This revision replaces v0.1 (commit `f2f9713`), which assumed Guard, attack resources, post-hit immunity, upgrades and the legacy Godot arena. Those are superseded by the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) ([#29](https://github.com/Risxhb/collab/issues/29), [PR #33](https://github.com/Risxhb/collab/pull/33)) and the [single-arena slice plan](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/plans/single-arena-slice.md).

The attack set and phase structure come from the supplied [Bellkeeper v1.0 reference](https://github.com/Risxhb/collab/blob/docs/bellkeeper-design-reference/reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md) (#17), which is preserved unchanged. Where this spec differs from v1.0, it says why. Every number is an initial tunable value.

Shared geometry, damage, RNG, reset, events, movement and stamina belong to the foundation lane ([#6](https://github.com/Risxhb/collab/issues/6)). This spec says what the encounter needs from them (§11); it never defines private hit testing.

Companion documents:
- [encounter test matrix](../testing/encounters.md)
- [escape checker](../../tools/encounter-escape/escape-check.ps1)
- [prototype upgrades](prototype-upgrades.md), now deferred reference only

## 1. Where the Bellkeeper sits

The Bellkeeper is the **final boss** of the run:

**Dial Warden → Pendulum Sentinel → Chimebound Oracle → intermission → The Bellkeeper**

All five stages are in one arena. Each miniboss teaches one part of the Bellkeeper's vocabulary first, so the final fight tests combination and execution rather than introducing new verbs (§9).

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.** v0.1's legacy Godot units are retired. The v1.0 comparison on #8 drew 1 m = 40 legacy units for illustration only; nothing here should be converted back.

| Value | Game-design defaults (#33) | Current Unity test (#27 `feature/class-test-arena`) | Used here |
|---|---|---|---|
| Walk | 6 m/s | 4.2 m/s (`VanguardTestController.RunSpeed`) | Both checked (§5); design defaults are the gate |
| Sprint | 9 m/s | 6.5 m/s | Not assumed in any escape |
| Dodge | 3 m over 0.20 s | 4.5 m, clip-length duration | Both checked; Unity dodge duration assumed 0.40 s |
| Stamina | 100; dodge 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Not implemented in the test | Design values (§5.3) |
| Invincibility | None, including dodge and post-hit | 0.75 s post-hit immunity in `PlayerVitals` | **None.** The Unity test conflicts with the design (§12, D3) |
| Arena | Proposed 24 m Clockwork Dais | Key-art arena: player centres ≤ 11.5 m, boss centre ≤ 9.5 m from the centre | Unity test bounds |
| Boss body | Real collision | 1.4 m radius | 1.4 m; player centres stay ≥ 1.7 m from the boss centre |
| Player hit margin | — | 0.3 m (touching a border counts) | 0.3 m: danger shapes are expanded by it |
| Player health | — | 100 HP | 100 HP (§8) |

If the shared movement constants change in U1/U3, rerun `tools/encounter-escape/escape-check.ps1` with the new values and update §5. Request R21 asks for the constants to be published as shared data.

## 3. Attack vocabulary

Four attacks plus one signature sequence, from v1.0 §3. The shapes match the current Unity test, except for the recommended Bell Sweep radius (§5.2, decision D1).

| ID | Attack | Shape | Tracking and lock | Resolve | Taught by |
|---|---|---|---|---|---|
| `bell_sweep` | **Bell Sweep** | Cone from the boss centre: **6.5 m recommended** (v1.0: 8 m), 80° wide | Boss turns toward the target for 18 ticks (0.30 s), then facing and cone lock | Lock + tier window (§5.2) | Dial Warden's Hand Sweep |
| `hollow_toll` | **Hollow Toll** | Annulus 2.6–6.8 m around the boss's position **at warning** | Fixed at warning; never drifts | Warning + tier window | Dial Warden's Hollow Dial |
| `pendulum_charge` | **Pendulum Charge** | Lane 2.2 m wide from the boss to the target's position + 2.75 m overshoot, clamped to the boss bound | Lane locks **at warning** | Warning + tier window; then the boss travels the lane in 18 ticks and ends at its far end | Pendulum Sentinel |
| `shattered_chime` | **Shattered Chime** | Circles of radius 1.4 m, spawned on a marked player 24 ticks apart | Each marker follows its owner at 6 m/s for 30 ticks, then locks | Lock + tier window per marker | Chimebound Oracle |
| `final_toll` | **The Final Toll** | Sequence: Charge → Toll at the new boss position → Chime → Bell Sweep → long recovery | Each step keeps its own lock rule | §4.3 | — |

**Rules for every attack:**
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry at the resolve tick, never against animation or VFX timing.
- **Locks are final.** Once an attack locks, no random roll or retarget can move it.
- **Moving boss.** Hollow Toll and Bell Sweep use the boss's position at the moment they warn. After a Pendulum Charge that position is the lane's end.
- **No jump clearance.** None of the Bellkeeper's attacks can be avoided by jumping; jump state doesn't change resolution. Jump-clearable hazards belong to the Pendulum Sentinel's Low Pendulum (request R19).
- **Two at once, at most.** No more than two damaging patterns can be warning at the same time. A Chime volley counts as one.

## 4. Phases and timeline

Phases are **HP-based**, as in v1.0 §4:

| Phase | Name | Boss HP | Content |
|---|---|---|---|
| 1 | The First Toll | 100–70 % | Recognition |
| 2 | The Dissonance | 70–35 % | Combination |
| 3 | The Final Toll | 35–0 % | Execution |

**Shared phase rules:**
- **Queued transitions.** Crossing a threshold queues the transition until the active pattern or block finishes. No telegraph is ever interrupted by an unrelated lethal attack.
- **Defeat.** Boss defeat immediately cancels pending hazards and markers, and no damage can land after victory. If the last standing player goes down on the same tick the boss dies, defeat wins. That's the single-arena plan's proposed tie rule, which foundation #6 must make deterministic.
- **Seeded variation.** Variations are chosen by the seeded encounter RNG (R6) at block boundaries or documented target-acquisition points. If a sampled variation is invalid for current positions, the director picks a pre-validated fallback **before** the tell begins (v1.0 §5.3).
- **No enrage.** The slice has no timed enrage, a change from v0.1. Fight length is tuned with boss HP (§8).

### 4.1 Phase 1 — The First Toll (fixed order, loops)

Bell Sweep → Hollow Toll → Pendulum Charge → Shattered Chime → recovery → repeat.

Each attack also has its own short recovery (§5.4). After a full loop the boss walks back toward the centre, as the Unity test already does, so the next Hollow Toll has outer safe space.

### 4.2 Phase 2 — The Dissonance (blocks, A → B → C → repeat)

| Block | Sequence | Permitted variation | Gap inside the block |
|---|---|---|---|
| **A: Pursuit** | Pendulum Charge → Hollow Toll at the landing point | Target; left or right overshoot side | The Toll warns on the landing tick |
| **B: Discord** | Shattered Chime → Bell Sweep | Marker owners (§6) | The Sweep warns when the last marker locks |
| **C: Reversal** | Hollow Toll → Pendulum Charge | Mirrored charge path | The Charge warns when the Toll resolves |

### 4.3 Phase 3 — The Final Toll

The Final Toll → Pursuit (A) → The Final Toll → … until defeat.

**Steady Ticking schedule** (ticks from the start of the sequence; design movement set, recommended 6.5 m Sweep):

| Step | Warn | Lock | Resolve | Note |
|---|---|---|---|---|
| Pendulum Charge | 0 | 0 | 48 | The boss travels to the lane end over ticks 48–66 |
| Hollow Toll | 66 | 66 | 138 | Centred on the landing point. The 72-tick window lets players walk out (needs 70) |
| Shattered Chime ×1 per owner, then +24 | 138 / 162 / 186 | +30 | +78 (216 / 240 / 264) | 48 ticks after lock. Walk-only needs 45, so continuous movement solves it without dodging |
| Bell Sweep | 240 | 258 | 309 | 51 ticks after lock; needs one dodge (48) |
| Recovery | 309 | — | 429 | 2.0 s vulnerability window |

Total: 7.15 s (v1.0 targets 6–8 s). This schedule costs **one dodge** for a worst-case player, which is what makes it repeatable on the stamina budget in §5.3.

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

`tools/encounter-escape/escape-check.ps1` samples:
- every boss position in the bound (radius 0–9.5 m, using the arena's symmetry)
- every attack direction, in 10° steps
- every reachable danger point on a 0.1 m grid (inside the arena, outside the boss body, inside the shape expanded by 0.3 m)

For each danger point it measures the straight-line distance to the nearest safe point, using a 0.05 m boundary grid. It then converts the worst case into escape time:
- **walk-only:** distance ÷ walk speed
- **walk + one dodge:** the dodge distance at dodge speed, then the rest at walk speed

The minimum window for a difficulty is that difficulty's **reaction margin** plus the escape time:

| Tier | Reaction margin | Dodge allowed? |
|---|---|---|
| First Chime | 0.60 s | No, walk-only must pass |
| Steady Ticking | 0.45 s | One dodge per attack |
| Striking Hour | 0.35 s | One dodge per attack |
| Midnight Toll | 0.30 s | One dodge per attack |

The window counts from **lock** for tracking attacks (the final footprint is known only then) and from **warning** for attacks fixed at warning.

**Limits.** Travel is straight-line, with no routing around the boss body. There are no other players and no acceleration, and the dodge has constant speed. The grid sampling is accurate to about ±0.05 m. These checks are evidence for single attacks, not proof that every combination can be solved; §10 covers combinations.

### 5.2 Results (run on October 21, 2026)

Worst-case escape distances:

| Attack | d_max | Worst configuration |
|---|---|---|
| Bell Sweep, 8 m (v1.0) | **5.12 m** | Boss 4 m from centre, cone facing the wall: players can't run past its end, only out of its sides |
| Bell Sweep, 6.5 m (recommended) | **3.84 m** | Boss 6 m from centre, facing the wall |
| Bell Sweep, 8 m narrowed to 70° | 4.61 m | Boss 4 m from centre |
| Hollow Toll | **4.25 m** | Boss 5 m from centre: outer safety cut off by the wall, so the inner pocket is the escape |
| Pendulum Charge | 1.41 m | Any lane: sideways escape |
| Shattered Chime | 1.71 m | From a marker's centre |

Minimum window in ticks (design movement set: walk 6 m/s, dodge 3 m in 0.2 s), compared with the current Unity test and v1.0:

| Attack | First Chime (walk) | Steady Ticking | Striking Hour | Midnight Toll | Current Unity / v1.0 |
|---|---|---|---|---|---|
| Bell Sweep 8 m, after lock | 88 | 61 | 55 | 52 | **39: fails every tier** |
| Bell Sweep 6.5 m, after lock | 75 | 48 | 42 | 39 | 39 |
| Hollow Toll, from warning | 79 | 52 | 46 | 43 | 66 |
| Pendulum Charge, from warning | 51 | 33 | 27 | 24 | 48 |
| Shattered Chime, after lock | 54 | 34 | 28 | 25 | 42 |

With the Unity test's slower walk (4.2 m/s) the dodge tiers barely change (Sweep 6.5 m: 48 / 42 / 39). Walk-only First Chime gets longer (Sweep 6.5 m: 91; Toll: 97). The script prints both sets.

**Finding.** v1.0's 0.95 s Bell Sweep (0.30 s tracking + 0.65 s after lock) is **not escapable at any difficulty** with the 8 m cone once arena walls are included. Not even a dodge at the Midnight Toll reaction margin is enough. A shield, parry or immunity can't be the fix (#29).

**Decision D1. Recommendation: shorten the cone to 6.5 m and keep 80°.** It keeps v1.0's 0.65 s post-lock window exactly at Midnight Toll, and it makes the Sweep and Hollow Toll similar in reach. The alternative is to keep 8 m and lengthen the post-lock window to 88 / 63 / 57 / 54 ticks. Narrowing to 70° barely helps (4.61 m).

**Proposed windows per tier** (ticks; includes a small margin over the minimum):

| Attack | First Chime | Steady Ticking | Striking Hour | Midnight Toll |
|---|---|---|---|---|
| Bell Sweep, tracking then after lock (6.5 m) | 18 + 78 | 18 + 51 | 18 + 45 | 18 + 39 |
| Hollow Toll, from warning | 84 | 66 | 60 | 54 |
| Pendulum Charge, from warning | 54 | 48 | 42 | 36 |
| Shattered Chime, follow then after lock | 30 + 57 | 30 + 42 | 30 + 36 | 30 + 30 |
| Recovery after each attack | 72 (1.2 s) | 54 (0.9 s) | 42 (0.7 s) | 36 (0.6 s) |
| Recovery after The Final Toll | 150 (2.5 s) | 120 (2.0 s) | 120 | 120 |

Combined sequences can use longer windows than these (for example §4.3), but never shorter.

### 5.3 Stamina budget

The design proposal is 100 stamina, 30 per dodge, regenerating at 20/s after 0.75 s without spending. Regaining one dodge's worth therefore takes 2.25 s.

**Rule: in any authored sequence, a worst-case player may need at most one dodge per 2.25 s.** They may also need at most three dodges in a row, provided the preceding 5 s required none. Sprinting and jumping are never required.

The validator checks this with the stamina model, starting each block from the worst-case stamina left by the previous one. §4.3's Final Toll needs one dodge (for the Sweep), so Final Toll → Pursuit → Final Toll stays within budget.

An earlier draft that gave every step its shortest window needed three dodges (Toll, Chime and Sweep, 90 stamina) and was **not** repeatable. That's why the Final Toll's Toll and Chime use walk-solvable windows.

### 5.4 Collision, pinning, knockback and hit-stun

- **Boss body collision is real.** Its consequences:
  - The Toll's inner safe pocket is a thin band, 1.7–2.3 m from the boss centre (body plus player radius, up to the expanded inner edge). The escape check accounts for this.
  - The Charge's lane end is clamped to 9.5 m and players to 11.5 m, so a player can always stand between the boss and the wall (2.0 m gap, 1.7 m needed).
- **Knockback** (proposal): 1.5 m away from the hazard's origin. That means sideways out of a lane, radially away from the boss for a cone or ring, and away from a Chime's centre. Knockback stops at the arena wall and the boss body, and deals no wall damage.
- **Hit-stun** (proposal): 15 ticks (0.25 s) on any Bellkeeper hit.
- **Anti-chain rule.** For any two hazards that can hit the same player, the later one's resolve must come at least `stun + escape` after the earlier one's. The escape is measured from every knocked-back position, using a dodge if the stamina rule allows one.
  - In §4.3, the last Chime marker (264) can stun a player who then faces the already locked Sweep (309). That leaves 30 ticks after stun, against a 20-tick dodge escape: it passes with a dodge.
  - The validator must check every such pair at every tier.
- **One hit per attack instance.** A single hazard instance damages a player at most once (request R16). That replaces blanket post-hit immunity. Distinct instances, such as two different Chime markers, can each hit.

## 6. Party size (1–8)

**Tier** comes from the roster size at encounter start: **A** for 1–4, **B** for 5–8. It doesn't change mid-fight. Per-attack counts use the **standing, active** players at each warning (R7, R18). Party size is independent of difficulty: all four difficulty tiers apply at every count.

| Count | Tier | Bell Sweep | Pendulum Charge | Shattered Chime |
|---|---|---|---|---|
| 1 | A | 1 cone at the player | 1 lane at the player | 3 sequential markers on the player |
| 2–4 | A | 1 cone at one seeded target, clearly marked before lock | 1 lane; targets rotate (below) | Every standing player gets 2 sequential markers |
| 5–8 | B | **2 cones**, at targets from different subgroups; the second resolves 18 ticks after the first | 1 lane (one boss body); targets rotate | `ceil(n/2)` marked players with 2 markers each, alternating subgroups between volleys |

Hollow Toll and The Final Toll's structure are the same at every count.

**Rules:**
- **Subgroups (tier B).** Standing players sorted by stable player ID: even indexes are group 1, odd indexes are group 2. Odd counts put the extra player in group 1. Groups only spread targets apart; they don't change any requirement.
- **Rotating charge target.** Round-robin through standing players by ID, starting from a seeded offset. Everyone gets charged in turn, which is v1.0's "predictable distributed target selection".
- **Marked before lock.** Every targeted player is identifiable before lock (v1.0 §7.1). The HUD tag uses a shape, not colour alone.
- **Floor capacity.** At 8 players, at most 8 markers are live at once: 72.6 m² of expanded marker area in a 415 m² arena. Overlapping markers each hit; spacing pressure is intended.
- **Two cones in tier B** were not sampled together. The validator checks their union for every seed. If a seed fails, the stagger is widened, never the warning shortened.
- **Duplicate classes.** No assignment depends on class, now or later. All-Vanguard, all-Arcanist and any mix must be able to clear. No mandatory healer.
- **Revival is the only mandatory co-op interaction**, and it's never required to clear a mechanic.

v0.1's spread (Tolling Mark) and stack (Gathering Peal / Sanctuary Peal) mechanics are **not** part of the Bellkeeper baseline. They're listed as optional material in §9.

## 7. Down, revive and disconnect

| Situation | Policy | Event |
|---|---|---|
| Sweep target downed or inactive **before lock** | Retarget to a seeded standing player. If none is left the run has already failed | `assignment_changed` (old, new, reason) |
| Charge target | Chosen at warning from standing players; the lane locks at warning, so there's nothing to reassign | — |
| Chime owner downed or inactive **before lock** | Cancel that marker. A following marker doesn't jump to another player | `assignment_changed` with `cancelled: true` |
| Any hazard **after lock** | World geometry; resolves normally. It can hit revivers standing in it | — |
| Downed player | Not targetable and not hit by resolving hazards. Holds no assignments | — |
| Disconnect | The session layer marks the player inactive (R7); the encounter treats them as downed for targeting. Removal timing belongs to #7 | — |

**Revive is a risk choice.** The proposed channel is 3 s + 2 s per previous down. That's longer than any recovery window, so a reviver has to choose a spot that stays safe through the next attack.

**Decision D4:** should the encounter add a fixed extra recovery (proposal: +1.5 s at the end of a Phase 2 block) while any player is downed? It helps revives without changing the attack order, but it makes the fight easier for struggling groups. Recommendation: yes for First Chime and Steady Ticking, no for higher tiers.

**Practice vs fresh run:**
- **Practice** starts the Bellkeeper directly: full health, the full stamina and cooldown reset, phase 1, and the encounter seed from the practice seed. It never unlocks run progress.
- **Fresh run** resets everything at encounter 1. The Bellkeeper's encounter seed is derived from the run seed and the encounter index, so a run is reproducible end to end.

## 8. Damage, health and boss HP

v1.0 proposes 50 / 45 / 50 / 25 % of max HP, and about 80 % for The Final Toll's last strike. The Unity test uses those values plus five +40 HP potions.

The current plan carries health across the whole run, with **no healing and no automatic intermission heal** (single-arena plan §6). At those percentages a player arrives at the final boss after three minibosses, and two Bellkeeper hits end the run.

**Decision D2. Proposal for Steady Ticking** (per hit, 100 HP):

| Attack | Damage |
|---|---|
| Bell Sweep | 30 |
| Hollow Toll | 25 |
| Pendulum Charge | 30 |
| Shattered Chime, per marker | 15 |
| Bell Sweep at the end of The Final Toll | 45 |

Scale ×0.7 on First Chime, ×1.0 on Striking Hour (difficulty there comes from mechanics) and ×1.2 on Midnight Toll.

Alternatives:
- keep v1.0's values and add a full refill at the intermission (a run-policy change)
- keep v1.0's values and accept a very lethal finale

The Unity test's potions are an unapproved healing system, since healing is deferred in #29 (D3).

**Boss HP** sets fight length (target 3–4 min at Steady Ticking) and is the foundation's to tune. Proposal: `base × (1 + 0.6 × (n − 1))` for n players, applying to HP only and never to mechanics.

## 9. Content classification and reuse

| Encounter | Teaches (single-arena plan §3) | Feeds the Bellkeeper's |
|---|---|---|
| Dial Warden | Hand Sweep (locked cone), Hollow Dial (ring) | Bell Sweep, Hollow Toll |
| Pendulum Sentinel | Pendulum Charge (lane), Low Pendulum (jump-clearable bar) | Pendulum Charge; also sets the "only marked attacks can be jumped" rule |
| Chimebound Oracle | Shattered Chime, Dividing Chime (cone after markers) | Shattered Chime, Block B (Discord) |
| The Bellkeeper | Combines all four; Midnight Toll adds Memory of Midnight | — |

The minibosses should use the same shape families and the same escape-check method, with their own shorter windows checked by the script.

**Material retired from v0.1, kept as optional ideas:**
- **Tolling Mark (spread) and Gathering/Sanctuary Peal (stack/shelter):** candidates for the Chimebound Oracle or Striking Hour co-op assignments, not baseline. They would need the R2 requirement kinds.
- **Tracking Sweep:** merged into Bell Sweep's 0.30 s tracking.
- **Remembered Sweep (Echo):** becomes Memory of Midnight (§10.4).
- **Final Hour (guard enrage):** removed, because there is no Guard. The slice has no enrage.
- **Six upgrades:** deferred reference ([prototype-upgrades.md](prototype-upgrades.md)).

## 10. Difficulty tiers

Mechanics change, not just HP. Names are proposals for lore review.

| Tier | Bellkeeper changes |
|---|---|
| **1 — First Chime** | Walk-only windows (§5.2), longer recoveries. Phase 2 blocks get a 1.5 s pause between attacks. The Final Toll uses walk-only windows for every step. Solo Chime: 2 markers instead of 3 |
| **2 — Steady Ticking** | The baseline described in §3–§8 |
| **3 — Striking Hour** | Shorter windows and recoveries. Block B's Sweep warns while the second marker is still tracking (Chime and Sweep overlap). Tier B Chime owners are staggered by subgroup. Phase 3 drops the Pursuit block between Final Tolls (stamina rule still applies, §5.3) |
| **4 — Midnight Toll** | Shortest windows (§5.2), plus **Memory of Midnight** |

**Memory of Midnight** (the Echo, scoped to the top tier):
- **Recording.** In each Phase 2 Block B, the Bell Sweep is **recorded**: its locked absolute footprint (position and facing) is stored. Only one is retained; a new recording replaces the old one.
- **Memory mark.** From the recorded Sweep's resolve until its return, the floor shows a dotted, unfilled outline with a bell-memory glyph at low contrast. It deals no damage and must be distinguishable in grayscale (R5).
- **Return.** In the next Block A, the remembered cone returns at its recorded footprint, warning together with the Hollow Toll and resolving 30 ticks after it. It has a 90-tick warning: the minimum after lock is 39–57 ticks, and its position has been previewed since recording. In Phase 3 it returns during The Final Toll's Chime step instead.
- **Limits.** At most one Echo at a time, and it counts toward the two-pattern limit.
- **Provenance.** Its events carry `source_attack_id` and the recorded tick.

## 11. Requests to the shared C# contract (#6)

These update v0.1's R1–R9 and the R10–R15 from the [#8 comparison](https://github.com/Risxhb/collab/issues/8#issuecomment-6301563765). The foundation owner decides how to implement them.

| # | Need | Status |
|---|---|---|
| R1 | Circle shape (centre, radius) in shared geometry | **Needed** (Shattered Chime) |
| R2 | Per-hazard requirement evaluation | **Reduced:** the Bellkeeper only needs `avoid`. `spread`, `stack` and `shelter` only if the §9 optional mechanics are adopted. `guard` withdrawn |
| R3 | `lock_tick == resolve_tick` for follow anchors | **Withdrawn**: Chime locks before resolve |
| R4 | Provider-selected reassignment or cancellation before lock | **Needed** (§7) |
| R5 | Presentation-only markers issued by the provider, never resolved | **Needed** (Memory of Midnight mark) |
| R6 | Independent seeded encounter RNG in reset and checkpoint state | **Needed** |
| R7 | Session-provided active/inactive flag | **Needed** |
| R8 | Resolved-event fields | **Revised** (§12) |
| R9 | `immunity_source` | **Withdrawn**: no immunity |
| R10 | Lane/rectangle shape (origin, direction, length, half-width) | **Needed** (Charge; Sentinel bar with R19) |
| R11 | Mutable boss position; boss-anchored hazards take the position at warning; included in reset and checkpoints | **Needed** |
| R12 | HP-phase thresholds with queued transitions and a phase event; boss HP fraction in the provider context | **Needed** |
| R13 | Per-hazard damage in HP (100-HP model, not 5 pips) | **Needed** |
| R14 | Boss vulnerability/recovery state event | **Needed** |
| R15 | `counterable` flag and `countered` outcome | **Withdrawn**: no counters |
| R16 | One hit per attack instance per player, replacing blanket post-hit immunity | **New** |
| R17 | Hit-stun and knockback on resolve (duration, direction rule, distance, stopping at walls and the boss body), recorded in the event | **New** |
| R18 | Standing/downed/inactive state in the provider context; downed players hold no assignments and aren't hit | **New** |
| R19 | Per-hazard height eligibility (`jump_clearable`, clearance height); default not clearable | **New** (Sentinel; Bellkeeper uses the default) |
| R20 | Boss body collision as shared state (radius, push-out), including during charge travel | **New** |
| R21 | Movement and stamina constants (walk, sprint, dodge distance and profile, stamina costs and regen, player radius, arena bounds) published as shared data | **New**, so the validator and the game use the same numbers |

## 12. Resolved records and failure feedback

Each resolved hazard record, or the group record it references, needs:

- **Identity:** `schema_version`, `generation`, `event_id`, `player_id`, plus `encounter_id` (`bellkeeper`), `phase`, `difficulty`, `seed`.
- **Attack:**
  - `attack_id`
  - `attack_instance_id` (used for R16 de-duplication)
  - `attack_name`
  - `pattern_id` or `block_id` (for example `P2_B_DISCORD`)
  - the chosen variation
- **Time:** the authoritative resolve tick and elapsed fight time.
- **Geometry as resolved:**
  - shape and locked origin
  - facing or lane direction
  - radii, length and width
  - warning tick and lock tick
  - target or owner player ID
  - the boss position at warning
- **Positions:** the affected player's centre at resolve, their jump and dodge state, and for co-op every standing player's centre, so the failure card can draw the arena diagram.
- **Outcome:** `avoided` or `hit` only (no `guarded` or `immune`); `damage`; `hit_stun_ticks`; the knockback vector; `downed` if it caused a down.
- **Assignment history:** linked `assignment_changed` events.
- **Echo provenance:** `source_attack_id` and recorded tick, for Memory of Midnight.

Recording dodge state matters even without iframes. It lets the card say "you dodged, but into the cone".

Example card the data must support:

> *"Bell Sweep (2:41, Phase 3, The Final Toll). The cone locked facing the north-east wall at 2:40.6. You were 1.2 m inside its left edge when it resolved. Dodge sideways past the edge after the facing locks."*

## 13. Human decisions needed

| # | Decision | Recommendation |
|---|---|---|
| D1 | Bell Sweep: 6.5 m at v1.0 timing, or 8 m with longer windows | 6.5 m (§5.2) |
| D2 | Damage values with carry-over health and no healing | Lower values (§8) |
| D3 | The Unity test's 0.75 s post-hit immunity and potions conflict with the design (no iframes, healing deferred) | Replace with per-instance dedup (R16); remove potions or label them as test-only. That's #27/#6's code, not this lane's |
| D4 | Extra recovery while a player is downed | Yes on tiers 1–2 (§7) |
| D5 | Where Memory of Midnight returns (§10) | As written |
| D6 | Boss HP scaling with party size | `base × (1 + 0.6 × (n − 1))` (§8) |

## 14. Visual notes

Follow the [art-direction guide](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) and the [spell-effects guidelines](https://github.com/Risxhb/collab/pull/34). Telegraphs are drawn from simulation shapes and triggered by simulation events.

- **Bell Sweep:** radial harmonic lines with a strong edge. A tracking cue during the 0.30 s turn, then a hard lock cue; the boss's resonator faces the locked direction.
- **Hollow Toll:** two solid boundaries and an empty hole. The thin inner pocket (§5.4) needs its own readability check at the gameplay camera.
- **Pendulum Charge:** a lane with a direction chevron and a landing marker at the far end, so players can read where the next Toll will be.
- **Shattered Chime:** a patterned circle carrying the owner's shape tag. A follow cue, a lock cue and a countdown ring.
- **Memory mark:** dotted, unfilled, low contrast, with the memory glyph. Never mistakable for a live warning.
- **Eight players:** shape tags on markers and cones, others' effects reduced (PR #34's focus rule), and every boundary crisp.
