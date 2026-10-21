# The Bellkeeper — encounter specification

Draft v0.3.1 · October 21, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8)) · Reviewer: `risxhb/codex`; `chzy18/codex` for session-facing rules.

Status: **specification only; decisions D1–D9 made (§13).** It is engine-independent and targets the Unity/C# production path. The Unity run loop ([PR #42](https://github.com/Risxhb/collab/pull/42), `BellkeeperEncounter.cs`) implements v0.2 at Steady Ticking; v0.3 is not implemented yet (§15).

**What v0.3 changes.** It takes the identity, model, phase names and the new signature mechanic from Codex's boss packet ([PR #46](https://github.com/Risxhb/collab/pull/46), board [#47](https://github.com/Risxhb/collab/issues/47), `docs/boss-model-review/`, Bellkeeper section, sheet `04-bellkeeper-v1.png`). It **keeps v0.2's numbers**: geometry, windows, damage, recoveries, hit-stun, knockback and party scaling stay what the spec and the Unity build already use. Where the packet proposed a different number, §2.1 lists both and says which one this spec uses. New numbers exist only where v0.2 had nothing (the Unfinished Hour schedule, §3.1) and are built from v0.2 values.

v0.2 (commit `995478f`) replaced v0.1's Guard, attack resources, post-hit immunity, upgrades and the legacy Godot arena. Those are superseded by the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) ([#29](https://github.com/Risxhb/collab/issues/29), [PR #33](https://github.com/Risxhb/collab/pull/33)) and the [single-arena slice plan](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/plans/single-arena-slice.md).

The original attack set comes from the [Bellkeeper v1.0 reference](https://github.com/Risxhb/collab/blob/docs/bellkeeper-design-reference/reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md) (#17), preserved unchanged. Every number is an initial tunable value.

Shared geometry, damage, RNG, reset, events, movement and stamina belong to the foundation lane ([#6](https://github.com/Risxhb/collab/issues/6)). This spec says what the encounter needs from them (§11); it never defines private hit testing.

Companion documents:
- [encounter test matrix](../testing/encounters.md)
- [escape checker](../../tools/encounter-escape/escape-check.ps1) and its [v0.3 results](../../tools/encounter-escape/results-2026-10-21-v0.3.md)
- [prototype upgrades](prototype-upgrades.md), now deferred reference only

## 1. Where the Bellkeeper sits

**Campaign.** The packet places the Bellkeeper as the **ultimate boss** at Shattered Hour / Throne of the Final Toll, after the three area bosses (Skyward Spire, Brass Foundry, Verdant Chimes) and separate from the nine minibosses. Exact arena dimensions and collision are still TBD there.

**Prototype slice.** The playable run loop still uses the one-arena gauntlet:

**Dial Warden → Pendulum Sentinel → Chimebound Oracle → intermission → The Bellkeeper**

Each miniboss teaches one part of the Bellkeeper's vocabulary first (§9). The packet calls this prototype scope, not the campaign roster. This spec is checked against the slice's key-art arena (§2) until the Throne arena has measured bounds; then the escape check must be rerun with those bounds (decision D9).

### 1.1 Identity and model

A guardian built to toll when the city could no longer sustain time. Instead it arrested that instant, trapping the realms in their duties. It refuses the final second rather than seeking conquest. Intro: the monumental background bell moves silently, the chest pendulum starts, the body descends on chains that detach on landing, and an incomplete deep note sounds.

Model ([sheet](https://github.com/Risxhb/collab/blob/handoff/four-boss-designs-20261021/docs/boss-model-review/04-bellkeeper-v1.png)):
- **Front:** hooded cathedral-bell torso, small recessed ivory mask under the hood with a thin gold eye line, split shell with the suspended gold clapper behind a vertical crack.
- **Side:** the bell overhangs the hips; thick shoulder axles; massive planted legs.
- **Hands:** the RIGHT hand is a pendulum striking head, the LEFT has long blunt conducting fingers.
- **Back:** three incomplete clock rings at different angles, a blackened spine, broken chain attachments and cobalt plates.
- **Materials:** aged brass, blackened bronze, midnight cobalt, chipped ivory and sparse gold fissures. Purple stays in recesses and the environment.
- **Motion:** slow deliberate steps and decisive releases; the shell swings about the clapper; the idle beat sometimes pauses.
- **Stagger:** off-centre clapper strike, torso twist with an arm brace, rings desynchronise.
- **Death:** the chest opens, catches then releases the clapper; the background bell finally sounds the complete note; knees fold and the rings stop.

**Size.** The sheet draws it 5.5 m tall; the Unity presenter currently scales the model to 5 m (`CharacterPresenter.Create(…, 5f, …)`). Height is presentation only; keep 5 m until the final model is imported, then match the sheet if the camera allows (§14).

**Collision stays 1.4 m radius** (v0.2, Unity `BodyRadius`). The sheet's planted legs read much wider than that. Hollow Toll's inner safe pocket only exists while `body + player radius < inner radius − margin` (1.4 + 0.3 < 2.6 − 0.3). The escape check (§5.2) shows the pocket survives a 1.6–1.8 m body, though it shrinks to a 0.2 m band at 1.8 m, and is **gone at 2.0 m**: the worst Toll escape jumps from 4.25 m to 7.53 m and needs 85 ticks at Steady Ticking against the current 72. So the legs must sit inside a ~1.4–1.6 m footprint, use camera-aware fading, or the Toll's inner radius must grow (decision D8).

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Game-design defaults (#33) | Unity run loop (#42 `PlayerController`) | Used here |
|---|---|---|---|
| Walk | 6 m/s | 6 m/s | 6 m/s. The older 4.2 m/s test walk is still printed by the checker |
| Sprint | 9 m/s | 9 m/s | Not assumed in any escape |
| Dodge | 3 m over 0.20 s | 3 m over 12 ticks | 3 m over 0.20 s |
| Stamina | 100; dodge 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Same | Same (§5.3) |
| Invincibility | None, including dodge and post-hit | None (dodging never makes you immune) | None |
| Arena | Proposed 24 m Clockwork Dais | Key-art arena: player centres ≤ 11.5 m, boss centre ≤ 9.5 m from the centre | Key-art arena bounds |
| Boss body | Real collision | 1.4 m radius | 1.4 m; player centres stay ≥ 1.7 m from the boss centre |
| Player hit margin | — | 0.3 m (touching a border counts) | 0.3 m: danger shapes are expanded by it |
| Player health | — | 100 HP | 100 HP (§8) |
| Phase shift | — | 90 ticks, non-damaging | 90 ticks (1.5 s), non-damaging, boss stays targetable |

If the shared movement constants change, rerun `tools/encounter-escape/escape-check.ps1` with the new values and update §5. Request R21 asks for the constants to be published as shared data.

### 2.1 Packet numbers vs the numbers used here

The packet (PR #46) proposed its own values. This spec uses the existing ones:

| Item | Packet | **Used here (v0.2 / Unity)** |
|---|---|---|
| Bell Sweep | 80° cone, 6.5 m; 2.0 s warning; 1.8 s recovery; 17 damage | 80° cone, 6.5 m; 18 tracking + 51 after lock (1.15 s); 36-tick recovery in phase 1; **30** damage |
| Hollow Toll | Annulus 3.5–7 m; 2.2 s warning; 2.2 s recovery; 16 damage | Annulus **2.6–6.8 m**; 72-tick warning (1.2 s); 40-tick recovery; **25** damage |
| Pendulum March | 2.6 m lane, up to 9 m; 1.9 s warning; 0.7 s surge; 2.5 s recovery; 18 damage | **2.2 m** lane to the target + 2.75 m overshoot, clamped to the boss bound; 48-tick warning (0.8 s); **18-tick** (0.3 s) travel; **30** damage |
| Shattered Chime | 1.4 m circles; 0.5 s follow + 1.7 s lock; 14 damage | 1.4 m circles; **30 follow + 48 after lock** (0.5 + 0.8 s); 24 ticks apart; **15** damage |
| Unfinished Hour | ≥ 2.2 s repeat warning; 3.5 s chest-open recovery; 24 s cooldown; 14 echo damage | **90-tick** warning (v0.2's Memory of Midnight value); **120-tick** recovery (v0.2's Final Toll recovery); schedule-placed, no cooldown; **damage of the recorded attack** (§3.1) |
| Final Toll last Sweep | — | 45 damage |
| Cooldowns | Per-attack minimum reselection cooldowns | None: v0.2 uses fixed loops and blocks, not a selector |
| Hit-stun | ~0.15 s | **15 ticks (0.25 s)** plus 1.5 m knockback (§5.4) |
| Chime targets | 1 solo, 2 for 2–4 players, 3 for 5–8 | v0.2 table (§6): 3 sequential markers solo; every player gets 2 at 2–4; `ceil(n/2)` players × 2 at 5–8 |
| Boss HP | Measured DPS × 240 s (6000–9600 at an illustrative 40 DPS) | Unity test build 3600 (shortened); fight target 3–4 min at Steady Ticking; tuned by the foundation |
| Party HP scaling | `1 + 0.7 × (n − 1)` | `1 + 0.7 × (n − 1)` (D6: the packet's value adopted, for difficulty) |
| Phase 3 sequence recovery | 3.5 s | 120 ticks (2.0 s) |

## 3. Attack vocabulary

Four attacks, the Unfinished Hour signature, and The Final Toll sequence. Runtime IDs are unchanged from v0.2 so code and events keep working; the display names follow the packet.

| ID | Attack | Shape | Tracking and lock | Resolve | Taught by |
|---|---|---|---|---|---|
| `bell_sweep` | **Bell Sweep** | Cone from the boss centre, 6.5 m, 80° wide | Boss turns toward the target for 18 ticks (0.30 s), then facing and cone lock | Lock + tier window (§5.2) | Dial Warden's Hand Sweep |
| `hollow_toll` | **Hollow Toll** | Annulus 2.6–6.8 m around the boss's position **at warning** | Fixed at warning; never drifts | Warning + tier window | Dial Warden's Hollow Dial |
| `pendulum_charge` | **Pendulum March** (v0.2: Pendulum Charge) | Lane 2.2 m wide from the boss to the target's position + 2.75 m overshoot, clamped to the boss bound | Lane locks **at warning** | Warning + tier window; then the boss travels the lane in 18 ticks and ends at its far end. No steering, drag or pin | Pendulum Sentinel |
| `shattered_chime` | **Shattered Chime** | Circles of radius 1.4 m, spawned on a marked player 24 ticks apart | Each marker follows its owner at 6 m/s for 30 ticks, then locks | Lock + tier window per marker | Chimebound Oracle |
| `unfinished_hour` | **The Unfinished Hour** (new) | The exact locked footprint of one resolved Sweep, Toll or March, at its **original world position** | Fixed: never follows the boss or a player | 90 ticks after its warning (§3.1) | Its source attack |
| `final_toll` | **The Final Toll** | Sequence: March → Toll at the landing point → Chime → remembered March paired with Bell Sweep → long recovery | Each step keeps its own lock rule | §4.3 | — |

**Rules for every attack:**
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry at the resolve tick, never against animation or VFX timing.
- **Locks are final.** Once an attack locks, no random roll or retarget can move it.
- **Moving boss.** Hollow Toll and Bell Sweep use the boss's position at the moment they warn. After a March that position is the lane's end.
- **No jump clearance.** None of the Bellkeeper's attacks can be avoided by jumping. Jump-clearable hazards belong to the Pendulum Sentinel's Low Pendulum (request R19).
- **Two at once, at most.** No more than two damaging patterns can be warning at the same time. A Chime volley counts as one. An inactive memory outline (§3.1) is not a warning and doesn't count; an active echo does.

### 3.1 The Unfinished Hour

The packet's signature, replacing v0.2's tier-4-only Memory of Midnight. It runs at **every tier**.

- **Recording.** The encounter records the locked footprint (shape, origin, facing or lane, radii) of one resolved Sweep, Toll or March, chosen by the schedule (§4). It keeps **one** memory; recording a new one replaces the old.
- **Inactive outline.** From the source's resolve until the echo warns, the floor shows a faint dashed, unfilled outline with a clock glyph at the original anchor. It deals no damage and must be distinguishable in grayscale (R5).
- **Activation.** The reverse-turning back ring is the tell. The outline becomes the standard active hatched warning with a full countdown.
- **Warning: 90 ticks** (1.5 s) from activation to resolve. That is v0.2's Memory of Midnight warning. It is above every tier's minimum for the three source shapes (§5.2; worst is the Toll echo at 79 ticks walk-only), so the echo never needs a dodge.
- **Resolve.** Once, at the original world position. Afterwards the memory is erased.
- **Damage** equals the source attack's Steady Ticking damage: Sweep 30, Toll 25, March 30 (§8, decision D7).
- **Recovery.** After the echo resolves the chest opens for **120 ticks** (2.0 s), the punish window. No player rewind, no boss healing, no reversed cooldowns.
- **Pairing.** An echo may resolve on the same tick as one live attack only where a common safe route has been checked (§5.2). Otherwise it is sequenced.
- **Party size.** World geometry, identical at every count. In tier B (5–8 players) the first of the two Sweep cones is the one recorded.
- **Reset.** Practice start, retry, fresh run, defeat and wipe all clear the memory. Defeat cancels an active echo immediately.
- **Provenance.** Echo events carry `source_attack_id`, `source_instance_id` and the recorded tick.

## 4. Phases and timeline

Phases are **HP-based**:

| Phase | Name (v0.2 name) | Boss HP | Look | Content |
|---|---|---|---|---|
| 1 | **Last Guardian** (The First Toll) | 100–70 % | Closed shell, compact halo | Recognition: every attack alone, plus one isolated echo per loop |
| 2 | **Broken Hour** (The Dissonance) | 70–35 % | Shell sections separated, clapper machinery visible, offset rings | Two-pattern blocks, each with its own warnings; one remembered footprint per cycle |
| 3 | **Final Toll** (The Final Toll) | 35–0 % | Chest fully open, halo spread, brighter core | The Final Toll sequence with the memory/Sweep pairing |

**Shared phase rules:**
- **Queued transitions.** Crossing a threshold queues the transition until the active pattern or block finishes, then plays the 90-tick non-damaging transformation. The boss stays targetable; player setup (seals, bells, inscriptions) persists and its timers keep running. If one burst crosses two thresholds, only one transition plays and no phase attacks stack.
- **Defeat.** Boss defeat immediately cancels pending hazards, markers and the memory, and no damage can land after victory. If the last standing player goes down on the same tick the boss dies, defeat wins (foundation #6 must make this deterministic).
- **Seeded variation.** Variations are chosen by the seeded encounter RNG (R6) at block boundaries or documented target-acquisition points. If a sampled variation is invalid for current positions, the director picks a pre-validated fallback **before** the tell begins.
- **No enrage.** Fight length is tuned with boss HP (§8).

### 4.1 Phase 1 — Last Guardian (fixed order, loops)

Bell Sweep → Hollow Toll → Pendulum March → Shattered Chime → recovery (54 ticks) → **Unfinished Hour** (isolated) → recovery (120 ticks) → walk back toward the centre → repeat.

- The recorded attack rotates per loop: loop 1 records the **Sweep**, loop 2 the **Toll**, loop 3 the **March**, then repeats.
- **Isolated** means nothing else is warning while the echo warns. The boss stands planted; its current position is usually elsewhere, which is what melee players can exploit.

### 4.2 Phase 2 — Broken Hour (blocks, A → B → C → echo → repeat)

| Block | Sequence | Permitted variation | Gap inside the block |
|---|---|---|---|
| **A: Pursuit** | Pendulum March → Hollow Toll at the landing point | Target; left or right overshoot side | The Toll warns on the landing tick |
| **B: Discord** | Shattered Chime → Bell Sweep | Marker owners (§6) | The Sweep warns when the last marker locks |
| **C: Reversal** | Hollow Toll → Pendulum March | Mirrored March path | The March warns when the Toll resolves |
| **Echo** | Unfinished Hour (isolated), then 120-tick recovery | — | Warns 30 ticks after Block C's March lands |

The recorded attack rotates per cycle: cycle 1 records Block B's **Sweep**, cycle 2 Block A's **Toll**, cycle 3 Block C's **March**. The inactive outline stays visible through the blocks in between; it never counts toward the two-pattern limit. This replaces v0.2's 60-tick recovery at the end of the cycle.

### 4.3 Phase 3 — Final Toll

The Final Toll → Pursuit (A) → The Final Toll → … until defeat.

**Steady Ticking schedule** (ticks from the start of the sequence; design movement set):

| Step | Warn | Lock | Resolve | Note |
|---|---|---|---|---|
| Pendulum March (**recorded**) | 0 | 0 | 48 | The boss travels to the lane end over ticks 48–66. The inactive lane outline appears at 48 |
| Hollow Toll | 66 | 66 | 138 | Centred on the landing point. 72-tick window (walk needs 70) |
| Shattered Chime ×1 per owner, then +24 | 138 / 162 / 186 | +30 | +78 (216 / 240 / 264) | 48 ticks after lock. Walk-only needs 45 |
| Unfinished Hour: remembered March | 265 | 265 | **355** | Activates the tick after the last marker resolves, so the Chime volley, the echo and the Sweep are never three at once. 90-tick warning |
| Bell Sweep (45 damage) | 286 | 304 | **355** | 51 ticks after lock; needs one dodge (48). Resolves on the same tick as the echo |
| Recovery (chest open) | 355 | — | 475 | 2.0 s punish window |

Total: 7.92 s (v0.2: 7.15 s; v1.0 targets 6–8 s). The Sweep moves 46 ticks later than in v0.2 so that it can pair with the echo.

**Why the pairing is allowed.** The remembered lane always ends where the boss stands, and the Sweep fans out from the same point. The checker's union of the two shapes (§5.2) has the same worst case as the Sweep alone (3.84 m, 48 ticks with one dodge), so the 51-tick window after lock covers both. The schedule still costs **one dodge**, so Final Toll → Pursuit → Final Toll stays within the stamina budget (§5.3).

**Anti-chain.** The last Chime marker (264) can stun a player until 279; the Sweep is still tracking until 304, leaving 76 ticks to resolve. Hit by both the echo and the Sweep on 355 costs 75 HP; they are two distinct instances, so per-instance dedup allows both (D7).

## 5. Fairness: escape, stamina, collision and hit-stun

### 5.1 Method

`tools/encounter-escape/escape-check.ps1` samples:
- every boss position in the bound (radius 0–9.5 m, using the arena's symmetry)
- every attack direction, in 10° steps (30° for the pairing)
- every reachable danger point on a 0.1 m grid (inside the arena, outside the boss body where the boss is present, inside the shape expanded by 0.3 m)

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

The window counts from **lock** for tracking attacks and from **warning** for attacks fixed at warning, including echoes.

**Echo variants.** An echo replays at a position the boss has left, so the checker drops the body there: players may stand inside the old body circle. **Pairing** checks the union of the longest remembered March lane arriving at the boss and a live Sweep from the boss, for every boss position, lane direction and cone direction, with the body present. Run it with `-Pairing` (about a minute).

**Limits.** Travel is straight-line, with no routing around the boss body. There are no other players and no acceleration, and the dodge has constant speed. The grid sampling is accurate to about ±0.05 m. These checks are evidence for single attacks and the one checked pairing, not proof that every combination can be solved.

### 5.2 Results (run on October 21, 2026, v0.3)

`escape-check.ps1 -SweepRadius 6.5 -Pairing`, full output in [results-2026-10-21-v0.3.md](../../tools/encounter-escape/results-2026-10-21-v0.3.md).

| Attack | d_max | Worst configuration |
|---|---|---|
| Bell Sweep, 6.5 m | **3.84 m** | Boss 6 m from centre, facing the wall |
| Hollow Toll | **4.25 m** | Boss 5 m from centre: outer safety cut off by the wall, so the inner pocket is the escape |
| Pendulum March | 1.41 m | Any lane: sideways escape |
| Shattered Chime | 1.71 m | From a marker's centre |
| Bell Sweep echo (no body) | 3.84 m | Same as the live Sweep |
| Hollow Toll echo (no body) | 4.25 m | Same as the live Toll |
| March echo + Bell Sweep, same tick | **3.84 m** | Boss 6 m from centre; the lane adds nothing to the Sweep's worst case |

Minimum window in ticks (design movement set: walk 6 m/s, dodge 3 m in 0.2 s):

| Attack | First Chime (walk) | Steady Ticking | Striking Hour | Midnight Toll | Used at Steady Ticking |
|---|---|---|---|---|---|
| Bell Sweep, after lock | 75 | 48 | 42 | 39 | 51 |
| Hollow Toll, from warning | 79 | 52 | 46 | 43 | 72 |
| Pendulum March, from warning | 51 | 33 | 27 | 24 | 48 |
| Shattered Chime, after lock | 54 | 34 | 28 | 25 | 48 |
| Sweep / Toll echo, from warning | 75 / 79 | 48 / 52 | 42 / 46 | 39 / 43 | 90 |
| March echo + Sweep, after the Sweep's lock | 75 | 48 | 42 | 39 | 51 |

With the older 4.2 m/s test walk the dodge tiers barely change; walk-only First Chime gets longer (Sweep 91, Toll and Toll echo 97). The 90-tick echo warning fails that case for the Toll echo, so First Chime uses a **100-tick** echo warning (table below).

**Body size sensitivity** (Toll, Steady Ticking minimum from warning): 1.4 m body 52 ticks; 1.6 m 52; 1.8 m 52 (inner pocket only 0.2 m wide); **2.0 m 85, pocket gone** (§1.1, D8). The Sweep is unaffected.

**Proposed windows per tier** (ticks; includes a small margin over the minimum):

| Attack | First Chime | Steady Ticking | Striking Hour | Midnight Toll |
|---|---|---|---|---|
| Bell Sweep, tracking then after lock | 18 + 78 | 18 + 51 | 18 + 45 | 18 + 39 |
| Hollow Toll, from warning | 84 | 72 | 60 | 54 |
| Pendulum March, from warning | 54 | 48 | 42 | 36 |
| Shattered Chime, follow then after lock | 30 + 57 | 30 + 48 | 30 + 36 | 30 + 30 |
| Unfinished Hour, from activation | 100 | 90 | 90 | 90 |
| Recovery after each attack | 72 (1.2 s) | 54 (0.9 s) | 42 (0.7 s) | 36 (0.6 s) |
| Recovery after an echo or The Final Toll | 150 (2.5 s) | 120 (2.0 s) | 120 | 120 |

The Toll and Chime Steady Ticking values (72, 30 + 48) are the ones the Unity build and §4.3 already use. Combined sequences can use longer windows than these, never shorter.

### 5.3 Stamina budget

100 stamina, 30 per dodge, regenerating at 20/s after 0.75 s without spending. Regaining one dodge's worth takes 2.25 s.

**Rule: in any authored sequence, a worst-case player may need at most one dodge per 2.25 s.** They may also need at most three dodges in a row, provided the preceding 5 s required none. Sprinting and jumping are never required. Echoes are walk-solvable at every tier, so they never add a dodge.

The validator checks this with the stamina model, starting each block from the worst-case stamina left by the previous one. §4.3's Final Toll needs one dodge (for the paired Sweep), so Final Toll → Pursuit → Final Toll stays within budget.

### 5.4 Collision, pinning, knockback and hit-stun

- **Boss body collision is real** (1.4 m, §1.1).
  - The Toll's inner safe pocket is a thin band, 1.7–2.3 m from the boss centre. A Toll echo has no body inside it, so its pocket is the whole 0–2.3 m disc.
  - The March's lane end is clamped to 9.5 m and players to 11.5 m, so a player can always stand between the boss and the wall (2.0 m gap, 1.7 m needed).
- **Knockback:** 1.5 m away from the hazard's origin. That means sideways out of a lane, radially away from the apex or centre for a cone or ring (including echoes), and away from a Chime's centre. Knockback stops at the arena wall and the boss body, and deals no wall damage.
- **Hit-stun:** 15 ticks (0.25 s) on any Bellkeeper hit.
- **Anti-chain rule.** For any two hazards that can hit the same player, the later one's resolve must come at least `stun + escape` after the earlier one's. The escape is measured from every knocked-back position, using a dodge if the stamina rule allows one. The validator must check every such pair at every tier, including echo pairs.
- **One hit per attack instance.** A single hazard instance damages a player at most once (request R16). An echo is a new instance, distinct from its source.

## 6. Party size (1–8)

**Tier** comes from the roster size at encounter start: **A** for 1–4, **B** for 5–8. It doesn't change mid-fight. Per-attack counts use the **standing, active** players at each warning (R7, R18). Party size is independent of difficulty.

| Count | Tier | Bell Sweep | Pendulum March | Shattered Chime |
|---|---|---|---|---|
| 1 | A | 1 cone at the player | 1 lane at the player | 3 sequential markers on the player |
| 2–4 | A | 1 cone at one seeded target, clearly marked before lock | 1 lane; targets rotate (below) | Every standing player gets 2 sequential markers |
| 5–8 | B | **2 cones**, at targets from different subgroups; the second resolves 18 ticks after the first | 1 lane (one boss body); targets rotate | `ceil(n/2)` marked players with 2 markers each, alternating subgroups between volleys |

Hollow Toll, the Unfinished Hour and The Final Toll's structure are the same at every count.

**Rules:**
- **Subgroups (tier B).** Standing players sorted by stable player ID: even indexes are group 1, odd indexes group 2. Odd counts put the extra player in group 1.
- **Rotating March target.** Round-robin through standing players by ID, starting from a seeded offset.
- **Marked before lock.** Every targeted player is identifiable before lock. The HUD tag uses a shape, not colour alone.
- **Avoid repeated caster suppression.** Chime owners and Sweep targets rotate so the same player isn't chosen twice in a row when another standing player is eligible.
- **Floor capacity.** At 8 players, at most 8 markers are live at once: 72.6 m² of expanded marker area in a 415 m² arena.
- **Two cones in tier B** were not sampled together, nor with an echo. The validator checks their union for every seed. If a seed fails, the stagger is widened, never the warning shortened.
- **Duplicate classes.** No assignment depends on class. All-Vanguard, all-Arcanist and any mix must be able to clear. No mandatory healer, block, parry or dash invulnerability.
- **Revival is the only mandatory co-op interaction**, and it's never required to clear a mechanic.

## 7. Down, revive and disconnect

| Situation | Policy | Event |
|---|---|---|
| Sweep target downed or inactive **before lock** | Retarget to a seeded standing player. If none is left the run has already failed | `assignment_changed` (old, new, reason) |
| March target | Chosen at warning from standing players; the lane locks at warning | — |
| Chime owner downed or inactive **before lock** | Cancel that marker. A following marker doesn't jump to another player | `assignment_changed` with `cancelled: true` |
| Any hazard **after lock**, and every echo | World geometry; resolves normally. It can hit revivers standing in it | — |
| Downed player | Not targetable and not hit by resolving hazards. Holds no assignments | — |
| Disconnect | The session layer marks the player inactive (R7); the encounter treats them as downed for targeting | — |

**Revive is a risk choice.** The proposed channel is 3 s + 2 s per previous down. A reviver has to choose a spot that stays safe through the next attack, including a pending echo's outline.

**Downed recovery (D4, decided):** on First Chime only, the end of a Phase 2 block adds 1.5 s of recovery while any player is downed. Higher tiers never slow down for a down.

**Practice vs fresh run:** practice starts the Bellkeeper directly with full health, stamina, phase 1, no memory and the practice seed, and never unlocks run progress. A fresh run resets everything at encounter 1; the Bellkeeper's seed comes from the run seed and the encounter index.

## 8. Damage, health and boss HP

**Steady Ticking damage** (per hit, 100 HP; D2, as used by the Unity build):

| Attack | Damage |
|---|---|
| Bell Sweep | 30 |
| Hollow Toll | 25 |
| Pendulum March | 30 |
| Shattered Chime, per marker | 15 |
| Unfinished Hour echo | Same as the recorded attack (30 / 25 / 30) |
| Bell Sweep in The Final Toll | 45 |

Scale ×0.7 on First Chime, ×1.0 on Striking Hour and ×1.2 on Midnight Toll.

**Health between bosses** follows the run-loop decisions of October 21 (#42): health refills before each boss after the first, potions (+40 HP) carry over between minibosses, and health and potions both refill before the area's final boss. The packet asks that potions and healing stay with their current owner; this spec doesn't change them.

**Boss HP** sets fight length (target 3–4 min at Steady Ticking; the packet's 240 s fits) and is the foundation's to tune from measured sustained DPS during movement, not a stationary dummy. The Unity test build uses 3600, shortened. Party scaling: `base × (1 + 0.7 × (n − 1))`, HP only, never mechanics (D6).

## 9. Content classification and reuse

| Encounter | Teaches | Feeds the Bellkeeper's |
|---|---|---|
| Dial Warden | Hand Sweep (locked cone), Hollow Dial (ring) | Bell Sweep, Hollow Toll |
| Pendulum Sentinel | Pendulum Charge (lane), Low Pendulum (jump-clearable bar) | Pendulum March; also sets the "only marked attacks can be jumped" rule |
| Chimebound Oracle | Shattered Chime, Dividing Chime (cone after markers) | Shattered Chime, Block B (Discord) |
| The Bellkeeper | Combines all four, then remembers them (Unfinished Hour) | — |

The packet notes that the Final Toll also tests what the campaign area bosses teach: Aurelion's commitment, Crucivar's lanes and Sylvara's reuse of world positions. Those links apply once those bosses exist.

**Material retired from v0.1/v0.2, kept as optional ideas:** Tolling Mark (spread) and Gathering/Sanctuary Peal (stack/shelter), as Chimebound Oracle or Striking Hour assignments; Tracking Sweep, merged into Bell Sweep's tracking; Final Hour (guard enrage), removed; Memory of Midnight, **replaced by the Unfinished Hour** at every tier; six upgrades, deferred ([prototype-upgrades.md](prototype-upgrades.md)).

## 10. Difficulty tiers

Mechanics change, not just HP.

| Tier | Bellkeeper changes |
|---|---|
| **1 — First Chime** | Walk-only windows (§5.2), longer recoveries, 100-tick echo warning. Phase 2 blocks get a 1.5 s pause between attacks. The Final Toll's echo and Sweep are **sequenced**: the Sweep resolves first, the echo 45 ticks later. Solo Chime: 2 markers instead of 3 |
| **2 — Steady Ticking** | The baseline described in §3–§8 |
| **3 — Striking Hour** | Shorter windows and recoveries. Block B's Sweep warns while the second marker is still tracking. Tier B Chime owners are staggered by subgroup. Phase 3 drops the Pursuit block between Final Tolls (stamina rule still applies) |
| **4 — Midnight Toll** | Shortest windows, plus **Borrowed Second** |

**Borrowed Second** (the packet's top-tier mechanic, replacing Memory of Midnight there): once per Final Toll, the active echo's countdown **pauses for 60 ticks**, showing a numbered pause on its clock glyph, then resumes. The warning never lies: it stays drawn and active while paused, and never resolves while stopped. The echo's resolve (415) and the following recovery move 60 ticks later; the Sweep keeps its own timing (355), so the anti-chain rule applies: 15 stun + 18 reaction (Midnight Toll) + 15 walking out of the lane = 48 ≤ 60. It tests premature re-entry. The 60-tick value is new; no earlier number exists.

## 11. Requests to the shared C# contract (#6)

| # | Need | Status |
|---|---|---|
| R1 | Circle shape (centre, radius) in shared geometry | **Needed** (Shattered Chime) |
| R2 | Per-hazard requirement evaluation | **Reduced:** the Bellkeeper only needs `avoid` |
| R3 | `lock_tick == resolve_tick` for follow anchors | **Withdrawn** |
| R4 | Provider-selected reassignment or cancellation before lock | **Needed** (§7) |
| R5 | Presentation-only markers issued by the provider, never resolved | **Needed at every tier** (inactive memory outline) |
| R6 | Independent seeded encounter RNG in reset and checkpoint state | **Needed** |
| R7 | Session-provided active/inactive flag | **Needed** |
| R8 | Resolved-event fields | **Revised** (§12) |
| R9 | `immunity_source` | **Withdrawn** |
| R10 | Lane/rectangle shape (origin, direction, length, half-width) | **Needed** (March; Sentinel bar with R19) |
| R11 | Mutable boss position; boss-anchored hazards take the position at warning; included in reset and checkpoints | **Needed** |
| R12 | HP-phase thresholds with queued transitions and a phase event; boss HP fraction in the provider context | **Needed** |
| R13 | Per-hazard damage in HP | **Needed** |
| R14 | Boss vulnerability/recovery state event | **Needed** |
| R15 | `counterable` flag and `countered` outcome | **Withdrawn** |
| R16 | One hit per attack instance per player | **Needed** |
| R17 | Hit-stun and knockback on resolve, recorded in the event | **Needed** |
| R18 | Standing/downed/inactive state in the provider context | **Needed** |
| R19 | Per-hazard height eligibility; default not clearable | **Needed** (Sentinel) |
| R20 | Boss body collision as shared state, including during March travel | **Needed** |
| R21 | Movement and stamina constants published as shared data | **Needed** |
| R22 | **Re-issue a recorded locked hazard shape** at its stored world position as a new instance, with a pausable countdown (Borrowed Second); the stored footprint lives in reset and checkpoint state | **New** (Unfinished Hour) |

## 12. Resolved records and failure feedback

Each resolved hazard record, or the group record it references, needs:

- **Identity:** `schema_version`, `generation`, `event_id`, `player_id`, plus `encounter_id` (`bellkeeper`), `phase`, `difficulty`, `seed`.
- **Attack:** `attack_id`, `attack_instance_id` (R16 de-duplication), `attack_name`, `pattern_id` or `block_id` (for example `P2_B_DISCORD`), and the chosen variation.
- **Time:** the authoritative resolve tick and elapsed fight time.
- **Geometry as resolved:** shape and locked origin; facing or lane direction; radii, length and width; warning, lock and pause ticks; target or owner player ID; the boss position at warning **and at resolve** (they differ for echoes).
- **Positions:** the affected player's centre at resolve, their jump and dodge state, and for co-op every standing player's centre.
- **Outcome:** `avoided` or `hit` only; `damage`; `hit_stun_ticks`; the knockback vector; `downed` if it caused a down.
- **Assignment history:** linked `assignment_changed` events.
- **Echo provenance:** `source_attack_id`, `source_instance_id` and recorded tick.

Example card the data must support:

> *"The Unfinished Hour (2:41, Phase 3). It replayed the Pendulum March lane from 2:36 at its original place, not where the Bellkeeper stood. You were 0.6 m inside the lane when it resolved. Step out of the dashed lane before the back ring turns."*

## 13. Decisions

Decided October 21, 2026. The user delegated D1–D9 with one direction: **the main boss fights are supposed to be hard.** Hard here means lethal mistakes and tight but always-escapable windows, never an unfair one: every window still passes the §5 escape check, and no change relies on invulnerability.

| # | Decision | Decided |
|---|---|---|
| D1 | Bell Sweep: 6.5 m at v1.0 timing, or 8 m with longer windows | **6.5 m, 80°.** 8 m can't be escaped at the v1.0 timing; longer windows would make the fight easier, not harder |
| D2 | Damage values with the current health policy | **v0.2 values (§8):** Sweep 30, Toll 25, March 30, Chime 15, Final Toll Sweep 45. The packet's 14–18 would let a player absorb six or more hits; at these values three or four mistakes end the run |
| D3 | Unity post-hit immunity and potions | **Resolved (#42):** no dodge immunity; potions keep their refill rules (§8) |
| D4 | Extra recovery while a player is downed | **First Chime only.** On Steady Ticking and above the schedule doesn't slow down for a down; reviving is a risk the team takes inside the normal windows |
| D5 | Unfinished Hour placement | **Every tier** (§3.1, §4), with Borrowed Second added on Midnight Toll |
| D6 | Boss HP scaling with party size | **`base × (1 + 0.7 × (n − 1))`** (the packet's value). Co-op fights last a little longer than solo instead of shorter |
| D7 | Echo damage, and echo + Sweep both hitting on one tick | **Echo deals the recorded attack's damage; both may hit (75 HP).** The union is escape-checked (§5.2), so standing in both is a double mistake |
| D8 | Body collider vs the new model's wide legs | **Keep the 1.4 m collider.** The new model's lower legs and feet must fit a 1.6 m-radius footprint (model brief, §14), and anything wider fades near the camera. Never above 1.8 m without enlarging the Toll |
| D9 | Arena | **Key-art slice bounds now**; rerun §5 when the Throne of the Final Toll has measured collision |

## 14. Visual notes

Follow the [art-direction guide](https://github.com/Risxhb/collab/blob/docs/2-5d-isometric-direction/reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md) and the [spell-effects guidelines](https://github.com/Risxhb/collab/pull/34). Telegraphs are drawn from simulation shapes and triggered by simulation events. Strong warning geometry dominates the gold, turquoise, molten and cosmic decoration; decorative light never deals damage.

- **Bell Sweep:** the arm draws across the mouth; a deep metal draw precedes a compressed-sound fan. Tracking cue during the 0.30 s turn, then a hard lock with a visibly latched outline. The low hanging arm after release is the punish read.
- **Hollow Toll:** the chest compresses with a rising hum; two crisp boundaries and an empty centre. The sound band flashes only in the marked annulus. The thin inner pocket needs its own readability check at the gameplay camera.
- **Pendulum March:** knees compress and the rings align vertically; a lane with a direction chevron and a landing marker, so players can read where the next Toll will be.
- **Shattered Chime:** halo fragments hover over the marked players; patterned circles with the owner's shape tag, a follow cue, a lock cue and a countdown ring; a brittle falling ring, one burst, dissolve.
- **Unfinished Hour:** inactive = faint dashed outline plus a clock glyph, low contrast; active = the standard hatched warning, triggered by the reverse-turning back ring. Never mistakable for each other, including in grayscale and with effects reduced. Current and remembered attacks use distinct audio rhythms.
- **Phases:** closed shell → separated shell with offset rings → open chest with spread halo, as on the sheet.
- **Camera:** fade the model where it would hide the floor around it.
- **Eight players:** shape tags on markers and cones, others' effects reduced, and every boundary crisp.

## 15. Implementation notes for the run loop

The Unity build (`BellkeeperEncounter.cs` on `feature/run-loop`) already matches v0.3's numbers. To reach v0.3 it needs:
- the phase names and the March display name (IDs unchanged);
- recording one footprint, the inactive outline (R5) and the echo hazard (R22), with the rotations in §4.1–§4.2;
- phase 1 and phase 2 echo steps with their 120-tick recoveries, replacing phase 2's 60-tick recovery;
- the §4.3 schedule: echo at 265–355, Sweep moved to 286 / 304 / 355, recovery to 475;
- memory cleared on defeat, retry and practice start, plus autotest rows for each.
