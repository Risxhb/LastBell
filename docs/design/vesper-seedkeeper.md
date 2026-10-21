# Vesper Seedkeeper, Custodian of the Last Bloom — encounter specification

Draft v0.1 · October 21, 2026 · Lane: mini-bosses / Verdant Chimes 3-3 · Source packet: Codex's nine mini-boss handoff, branch `docs/nine-mini-bosses-20261021` (commit `a2ab311`, `docs/mini-bosses/`), section 9.

Status: **specification only, nothing here is implemented.** It targets the Unity/C# production path and follows the structure of the [Sylvara spec](https://github.com/Risxhb/collab/pull/50) and the [Bellkeeper spec](https://github.com/Risxhb/collab/pull/16), so all three can share one validator and one set of contract requests.

The Seedkeeper is the **third mini-boss of Verdant Chimes**, fought on the area's shared floor after the Briar Metronome and the Silt Carillon. The packet's identity, abilities, geometry, timings and damage are taken as given. This spec settles what the packet leaves open or contradicts: how distances are measured, how the hovering body is hit, how marks behave in parties, exact ticks, schedules, the four difficulty tiers and escape evidence.

Concept art: `3-3-verdant-chimes-vesper-seedkeeper.png` (both phases) and `area-sheet-03-verdant-chimes.png` in the packet.

Companion tool: [`tools/encounter-escape/vesper-escape-check.ps1`](../../tools/encounter-escape/vesper-escape-check.ps1), about 8 s.

## 1. Rules this encounter follows

Where the packet and the [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) disagree, the game design wins:

- **No invincibility of any kind.** Dash gives no immunity, and there is no post-hit immunity. Damage is avoided only by leaving the footprint. Each attack instance hits a player at most once.
- **Cursor aim, cooldown attacks, finite stamina.** Boss abilities run on the packet's cooldowns; players' attacks never spend movement stamina. No pattern requires a dash, sprint or jump (§5.3).
- **Vanguard Brace is allowed and never required.** It is the approved partial frontal mitigation. Against the Seedkeeper it applies to Petal Fan, Alternating Petals, Gathering Thread and Vesper Closure when the Vanguard faces the boss. Planting Mark and the Bloom ring are ground hazards, so they bypass it. (The packet text that calls this a conflict is outdated.)
- **No mandatory healer.** Chimesinger healing helps but is never assumed. Arcanist seals and Chimesinger bells persist through the phase change with their timers running.
- **Health:** health refills before each boss after the first, and potions carry over between mini-bosses. As the area's third mini-boss, the Seedkeeper sees whatever potions the party has left. Its damage is tuned with that in mind (§8).
- **Exactly two phases.** This is the mini-boss contract, unlike the three-phase area bosses.
- **Damage geometry is the telegraph.** Hits are checked against authoritative shared geometry, never animation or VFX. Lanterns, cords, petals, motes and roots are visual only except where a footprint says otherwise.
- **Locks are final.** Nothing retargets or moves after lock.
- **One damaging pattern at a time** at Steady Ticking and below. Striking Hour adds one tested overlap (§9).

## 2. Baseline values

**Units are metres and seconds; the simulation runs at 60 Hz.**

| Value | Used here | Source |
|---|---|---|
| Walk | 6 m/s (gate) and 4.2 m/s (also checked) | Game design; Unity prototype #42 |
| Dash | 3 m over 0.20 s, 30 stamina, no immunity | Game design |
| Stamina | 100; dash 30, jump 20, sprint 25/s; regenerates 20/s after 0.75 s | Game design |
| Player hit margin | 0.3 m; touching a border counts as inside. Also checked at the packet's 0.7 m (0.45 m radius + 0.25 m clearance) | Bellkeeper and Sylvara specs |
| Player health | 100 HP | Packet |
| Boss body | **1.2 m collision radius**, a ring drawn on the floor under the vessel; the same in both phases (§5.4) | This spec |
| Boss position | Centre stays within **3.0 m** of the arena centre | Packet |
| Arena | **Placeholder:** circle, player centres ≤ 11.5 m from the centre (the packet's 24 m diameter minus a player width) | Same placeholder as Sylvara; real Verdant Chimes collision needed (D7) |
| Hit-stun | 9 ticks (0.15 s); **no knockback** on any Seedkeeper attack | Packet |

**How distances are measured.** The packet's contract says geometry is measured from the boss's collision edge unless stated otherwise. Its own spot check for Vesper Closure measured from the centre instead. This spec follows the contract everywhere. In centre terms, with the 1.2 m body:

| Attack | Packet value | From the boss centre |
|---|---|---|
| Petal Fan / Alternating Petals | 5 m reach | 6.2 m |
| Gathering Thread | 9 m line | 10.2 m |
| Vesper Closure | 2.2 m disk | 3.4 m |
| Planting Mark, Bloom ring | Centred on the mark, not the boss | Unchanged |

Measuring from the centre would leave Closure only 1 m of danger outside the body, so a melee player could ignore it by stepping back once.

## 3. Attack vocabulary

W/A/R = warning to first damage / active time / guaranteed recovery after active ends. CD = minimum time from recovery end to the next use. Damage uses the packet's normalized values: **M = 18, H = 26** at Steady Ticking.

### 3.1 Phase 1 — Closed Archive

| ID | Attack | Shape | Targeting | Tracking and lock | Warning → damage | Active | Recovery | CD | Damage |
|---|---|---|---|---|---|---|---|---|---|
| `planting_mark` | **Planting Mark** | Circle r 1.6 m on each marked player | 1 / 2 / 3 players (§6) | Follows its owner for 48 ticks (0.8 s), then locks; **72 ticks after lock** | 120 (2.0 s) | **72 (1.2 s)** of roots: anyone inside at impact or who walks in is hit, once per volley | 180 (3.0 s), starting when the roots retract | 480 (8 s) | M |
| `petal_fan` | **Petal Fan** | Cone from the boss centre, 90°, 6.2 m | Nearest standing player | Facing locks at warning start | 96 (1.6 s) | 18 (0.3 s) | 162 (2.7 s) | 360 (6 s) | M |
| `gathering_thread` | **Gathering Thread** | Line from the boss centre, 1.4 m wide, 10.2 m, clipped by the wall | Rotating target | Aim tracks for 48 ticks, then locks; **60 ticks after lock** | 108 (1.8 s) | 18 (0.3 s) | 186 (3.1 s) | 480 (8 s) | H |

**Reading the packet's Planting Mark timing.** The packet says the disk "freezes 1.2 s before impact" and also lists 1.2 s as its active time. Here those are two different 1.2 s spans. The disk locks 1.2 s before impact. After impact the roots stay up for 1.2 s, and the punish window (recovery) starts when they retract. That matches the packet's "punish 3 s after roots retract".

**Per-attack notes:**
- **Planting Mark.** A lantern nods toward the owner with a seed rattle. A dashed disk follows the owner, then locks with a root-pin glyph and counts down. At impact roots fill the disk. When they sink, the glyph disappears and the floor shows a clear "spent" ring. The boss holds still from warning to the end of recovery, which is the packet's stated weakness.
- **Petal Fan.** Closed petals point forward with a breathy bell. The cone is fixed at warning, so melee steps beside the vessel and ranged crosses the nearest edge.
- **Gathering Thread.** One tether straightens. A bright dashed line tracks the target, then turns solid when it locks. It damages only on the line and never pulls.

### 3.2 Phase 2 — Open Archive

| ID | Attack | Shape | Timing (ticks from warning start) | Recovery | CD | Damage |
|---|---|---|---|---|---|---|
| `bloom_and_rest` | **Bloom and Rest** (replaces Planting Mark) | Disk r 1.6 m exactly as Planting Mark, then an **annulus 1.6–3.2 m** around the same centre | Disk: warn 0, lock 48, impact 120, roots 120–191. Ring: warns at 192 (when the roots retract), resolves at 300, active 300–317 | 216 (3.6 s) | 600 (10 s) | Disk M, ring H |
| `alternating_petals` | **Alternating Petals** (replaces Petal Fan) | Cone I: 90°, 6.2 m, as Petal Fan. Cone II: 70°, 6.2 m, sharing cone I's edge, on a seeded side | I: warn 0, active 96–113. II: shown as a dim numbered "II" outline from tick 0; its warning fills when I clears (114); active 204–221 | 192 (3.2 s) | 480 (8 s) | M per cone |
| `gathering_thread` | **Gathering Thread** (unchanged) | As Phase 1 | As Phase 1 | 186 | 480 | H |
| `vesper_closure` | **Vesper Closure** (new) | Circle r 3.4 m around the boss centre (2.2 m from the body edge) | Locks at warning; active 102–119 | 228 (3.8 s) while the petals reopen | 720 (12 s) | H |

**Bloom and Rest in detail.** This is the fight's signature and its main lesson: *the danger you placed becomes the safe spot.*
- The disk behaves exactly like Planting Mark.
- When the roots retract, the same centre shows a **hollow-centre glyph** and the ring's fresh 108-tick warning starts. The hollow is the retracted disk and is safe.
- A player standing just outside the disk steps 0.6 m inward to the hollow or about 1.6 m outward.
- The ring is a separate, separately warned instance, so it can hit someone the disk also hit.

**Alternating Petals.** Cone II is previewed from the start, so players can plan their step into cone I's cleared space. Its damaging warning is still a full 90 ticks after cone I clears.

**Vesper Closure.** The six petals fold with a descending six-note chord. The footprint is fixed around the boss at warning. Melee steps out and returns for the 3.8 s punish, and ranged holds outside and casts.

## 4. Phases and schedule

| Phase | Name | Boss HP | Visual |
|---|---|---|---|
| 1 | Closed Archive | 100–50 % | Closed bud, three lanterns hung close |
| 2 | Open Archive | 50–0 % | Six broad petals open, gold core exposed, lantern cords extended |

**Phase rules** (packet contract, made exact):
- Crossing 50 % queues the transition. The active attack finishes and its hazards expire, then the boss runs a **120-tick (2.0 s) harmless transformation**. It stays targetable, with no heal and no new health bar.
- A burst that crosses the threshold queues one transition. A lethal burst ends the fight at once.
- **Defeat** cancels every warning, mark and ring on that tick. Defeat pose: the petals close slowly, the lanterns lower to the floor, one last low bell.
- **The exposed core is not a weak point.** All body hits deal normal damage in both phases.
- **Cooldown hold.** If an ability isn't off cooldown when its turn comes, the boss holds its recovery pose. Windows are never shortened.
- **Drift.** Between one attack's recovery end and the next warning, the boss may drift up to 1.0 m at 1.5 m/s toward the party's centroid, staying within the 3 m bound. It never drifts during warnings, active time or recovery. Its body pushes players aside without damage (R20).

### 4.1 Phase 1 loop (16.0 s)

**Planting Mark → Petal Fan → Gathering Thread → repeat.**

Cooldown gaps: Mark 9.8 s (CD 8), Fan 11.4 s (CD 6), Thread 10.8 s (CD 8). No holds needed. Variation comes from target rotation and the Thread's target, never from reading player input.

### 4.2 Phase 2 loop (26.8 s)

**Bloom and Rest → Vesper Closure → Alternating Petals → Gathering Thread → repeat.**

Closure follows the Bloom punish, so a melee player who greedily holds the core after the ring has to step out once. This is the packet's own example sequence. Cooldown gaps: Bloom 17.9 s (CD 10), Closure 21.0 s (CD 12), Petals 19.9 s (CD 8), Thread 21.6 s (CD 8).

**Fight length.** At the packet's 90–150 s target this is about 3–4 Phase 1 loops and 2–3 Phase 2 loops. Tune boss HP to hit it (§8).

## 5. Fairness: escape, stamina, collision

### 5.1 Method

Same as the Sylvara and Bellkeeper specs. `vesper-escape-check.ps1`:
- samples every boss position in the 3 m bound (by symmetry) and every direction in 10° steps
- samples every mark position in the arena, including marks beside the boss body
- finds the reachable danger point farthest from safety, with shapes expanded by the player margin, player centres inside the arena and outside the boss body
- converts that into walk-only and walk-plus-one-dash times, then into the minimum window per difficulty

Multi-beat attacks are checked beat by beat. Spent footprints count as safe, and the ring's hollow counts as safe. **Three rings** are checked at the closest spacing the party rule allows (§6). Reaction margins: First Chime 0.60 s (walk only), Steady Ticking 0.45, Striking Hour 0.35, Midnight Toll 0.30.

### 5.2 Results (October 21, 2026, placeholder arena)

| Attack | d_max (0.3 m margin) | d_max (0.7 m margin) | Worst case |
|---|---|---|---|
| Planting Mark disk | 1.91 m | 2.31 m | Any mark centre |
| Bloom ring (one) | 1.77 m | 2.40 m | Mark far from the boss, mid-ring |
| Bloom rings (three, 4.8 m apart) | 2.15 m | 2.53 m | Between three rings |
| Petal Fan / Petals I | 2.86 m | 3.26 m | Boss at the centre, mid-cone |
| Alternating Petals II | 2.55 m | 2.95 m | Boss at the centre |
| Gathering Thread | 1.05 m | 1.45 m | Sideways out of the line |
| Vesper Closure | 2.20 m | 2.20 m | From body contact |
| Evensong seeds (Midnight Toll, §9) | 1.90 m | 2.30 m | From a seed centre |

Minimum window in ticks (0.3 m margin), compared with this spec's window after lock:

| Attack | First Chime, walk 6 / 4.2 | Steady Ticking | Striking Hour | Midnight Toll | Spec window |
|---|---|---|---|---|---|
| Planting Mark disk | 56 / 64 | 35 | 29 | 26 | 72 after lock |
| Bloom ring (three) | 58 / 67 | 36 | 30 | 27 | 108 |
| Petal Fan / Petals I | 65 / **77** | 39 | 33 | 30 | 96 |
| Alternating Petals II | 62 / 73 | 38 | 32 | 29 | 90 after cone I clears |
| Gathering Thread | 47 / 51 | 32 | 26 | 23 | 60 after lock |
| Vesper Closure | 58 / 68 | 36 | 30 | 27 | 102 |
| Evensong seeds | 55 / 64 | 35 | 29 | 26 | 90 |

**Findings:**
- **Every packet timing is walk-solvable at every tier**, even at the 4.2 m/s prototype walk with First Chime's 0.6 s reaction. No dash is ever needed.
- **The tightest is Petal Fan** at 77 of 96 ticks for a 4.2 m/s walker. It still passes with the packet's larger 0.7 m clearance (83 of 96).
- **The packet's disk-to-ring worry holds up.** Three rings at the minimum spacing still leave each hollow fully clear and every point within 2.15 m of safety.

### 5.3 Stamina

The baseline at every tier is walk-solvable, so stamina is only spent by choice: dashing back to the core for extra uptime. The stamina rule shared with the other specs (at most one required dash per 2.25 s) is never tested.

### 5.4 Body, hovering and cursor aim

The packet's hovering vessel needs rules for both collision and hits:

- **Collision:** a 1.2 m-radius circle on the floor, drawn as a soft shadow ring under the vessel. It is the same in both phases, so the transformation never shoves anyone. Lanterns, cords and petals have **no collision**.
- **Hurt volume:** a vertical cylinder from the floor to the top of the vessel, so ground-plane cursor aim hits it and melee reaches it despite the hover. Radius **1.2 m in Phase 1 and 1.8 m in Phase 2**, because the open petals widen the silhouette. Every hit counts as body damage, and the core isn't a weak point.
- **Lanterns are not targets.** They don't absorb projectiles (shots pass through) and they don't take damage. In Phase 2 the extended cords swing outside the body, and letting them eat shots would punish ranged players for aiming at what looks like the boss.
- **Hover height:** the bottom of the vessel sits about 0.6 m above the floor, inside melee reach for every class.
- **Anti-chain rule.** The closest pair that can hit the same player is the disk impact and the ring: 180 ticks apart, against a need of 9 (stun) + 36 (escape). Cone I → cone II: 108 ticks apart. Both pass easily.

## 6. Party size (1–8)

Same geometry and timing at every party size. Only mark count changes:

| Party | Planting Mark / Bloom marks | Everything else |
|---|---|---|
| 1 | 1 | Same |
| 2–4 | 2 | Same |
| 5–8 | 3 | Same |

**Mark rules** (this settles the packet's "merge/reject placements and expire roots" item):
- **Owners** rotate round-robin by stable player ID from a seeded offset. No player is marked in two consecutive volleys if enough other standing players exist.
- **Cancel, don't merge.** At lock, if two marks are closer than **3.8 m** in Phase 1 or **4.8 m** in Phase 2, the mark of the higher player ID **fizzles** with a visible cancel cue. Merging would move a locked footprint, which breaks "locks are final". Cancelling only ever removes danger.
- **Why 4.8 m.** It's the hollow radius minus the margin (1.3 m) plus the ring's outer radius plus the margin (3.5 m). Every surviving hollow is fully clear of every other ring, so "stand in your hollow" is always a valid answer. 3.8 m is 2 × (1.6 + 0.3), the distance at which two disks would touch.
- **Roots expire.** They retract after their 72-tick active time, and Bloom rings end after their active period. No ground hazard outlives its attack, so seeds can't stack into a soft-lock.
- **Marks are tagged** with a shape on the HUD and over the player, not colour alone.
- **Downs and disconnects:** a mark owner downed before lock has that mark cancelled. After lock, the mark is world geometry and resolves normally. Downed players hold no assignments and aren't hit. Disconnect counts as downed.
- **No class requirement.** Nothing depends on class, and duplicate-class parties work.

At eight players, three marks plus their rings cover at most 3 × 32 m² ≈ 97 m² of a 415 m² arena, and the 4.8 m rule spaces them out.

## 7. Practice

Practice starts the Seedkeeper at full health and full potions, in Phase 1, from the practice seed. A Phase 2 practice start at 50 % HP is recommended, so players can rehearse Bloom and Rest without first clearing Phase 1.

## 8. Damage and boss HP

- **Damage** (Steady Ticking): M 18 (Planting Mark, Bloom disk, Petal Fan, each Alternating cone) and H 26 (Bloom ring, Gathering Thread, Vesper Closure).
  - The worst single mistake is 26 HP.
  - Failing both Bloom beats costs 44 HP.
  - These sit below the Bellkeeper's final-boss hits (25–30) and suit a third mini-boss fought on carried-over potions.
  - Tier multipliers: First Chime ×0.8, Striking Hour ×1.0, Midnight Toll ×1.2.
- **Boss HP** = measured reference sustained DPS × **120 s** (solo), measured while moving and punishing, not on a stationary dummy. At an illustrative 40 DPS that's 4800 HP.
- **Party scaling:** `base × (1 + 0.6 × (n − 1))`, HP only, matching the Bellkeeper's current value (its D6 is still open).

## 9. Difficulty tiers

| Tier | Seedkeeper changes |
|---|---|
| **1 — First Chime** | Isolated teaching. Alternating Petals is a single cone with no second beat. Bloom ring warning +30 ticks. Every recovery +0.5 s |
| **2 — Steady Ticking** | The baseline above |
| **3 — Striking Hour** | One authored overlap in Phase 1: **Petal Fan warns when Planting Mark impacts** (Planting Mark's recovery moves to after the Fan). The safe answer is "bait the mark to a flank, then step beside the vessel": the cone faces the nearest player, and the mark's owner has already left it. Recoveries −20 %, but Bloom's and Closure's never below 3.0 s, so the 3–4 s major punish survives. Needs validation (T3) |
| **4 — Midnight Toll** | Striking Hour plus **Evensong** |

**Evensong** (Midnight Toll's added mechanic, original to this spec):
- When Vesper Closure warns, three seed glyphs appear on the floor, 120° apart and 5 m from the body edge, rotated until all three sit inside the arena.
- They resolve as 1.6 m disks **90 ticks after Closure resolves** (M damage, a separate instance).
- Players leaving the Closure have to pick an exit between the seeds. The corridors between seeds are about 6.9 m wide, and the seeds are visible from the start of the Closure warning, so the route can be planned before the first damage.
- Fits the boss's identity: the archive keeps planting.

## 10. Requests to the shared C# contract (#6)

Reuses the Bellkeeper's and Sylvara's: R1 circle, R4 reassign or cancel before lock, R5 presentation markers, R6 seeded RNG, R10 lane, R11 mutable boss position, R12 HP phases, R13 HP damage, R14 recovery event, R16 one hit per instance, R18 standing state, R20 body collision, R22 active-duration hazards, R23 ordered hazard groups, R25 sector. New:

| # | Need | Why |
|---|---|---|
| R26 | **Annulus shape** with inner and outer radius, where the inner disk is safe | Bloom ring. The Bellkeeper's Hollow Toll needs the same shape |
| R27 | **Cancel-at-lock with a visible fizzle event**, applied by a spacing rule across simultaneous markers | Mark spacing (§6) |
| R28 | **Hurt volume separate from collision**, with a per-phase radius and decorative parts excluded from projectile hits | Hovering body and lanterns (§5.4) |

## 11. Model, animation and asset notes

- **Silhouette:** ivory lotus-bud vessel about 2.5 m tall, aged-brass rings, a calm carved face, gold core, three seed-lanterns on root cords. Phase 2 opens into **exactly six** broad petals with the core exposed and the cords extended. The concept art matches both counts.
- **Readability from the isometric camera.** The floor shadow ring must read as the collision edge. The lantern that nods for Planting Mark and the tether that straightens for the Thread must be clear from above.
- **Rig:** the petals open and close (Closure folds all six), three independent lantern cords, a face that tilts to its target.
- **Animations:** idle hover, nod (Mark), petal point (Fan), tether pull (Thread), open transformation, fold (Closure), defeat (petals close, lanterns lower).
- **VFX:** coral/amber hatching for every warning. The root-pin glyph appears on lock. A clear "spent" ring and a **hollow-centre glyph** mark the safe centre. Cone II shows a dim "II" preview. The Thread is dashed while tracking and solid once locked. Turquoise inlays are decoration only and never a hazard edge.
- **Audio:** seed rattle (Mark), breathy bell (Fan), taut string (Thread), a rising tone as the ring warns, a descending six-note chord (Closure). Each has a visual, so the fight reads with audio off.
- **Reward:** Vesper seed reliquary and six-petal cloak clasp, cosmetic only (the packet's proposal; it needs collection-screen support later).

## 12. Validation tests

| # | Check |
|---|---|
| T1 | Rerun the escape check on the real Verdant Chimes arena and the final body collider, at both movement sets and both margins |
| T2 | Both phase loops at every tier, with real collision, finite stamina and hit-stun from every hit position |
| T3 | Striking Hour's Mark + Fan overlap: for every mark position and the nearest-player cone, a walk route exists that avoids both |
| T4 | Parties of 1, 2, 4 and 8, including duplicate classes, with downs and disconnects mid-warning; check the cancel-at-lock rule and repeat-mark rotation |
| T5 | The transition crossed during a multi-beat attack (Bloom, Alternating Petals): one transition only, nothing skipped, no new hazards during the 2 s |
| T6 | Projectiles pass through the lanterns, and every class can hit the hovering body in both phases |
| T7 | Melee and ranged clear times within 15–20 % for players of similar skill (packet target) |

## 13. Human decisions needed

| # | Decision | Recommendation |
|---|---|---|
| D1 | Measure reaches from the body edge (contract) or the centre (packet's Closure spot check) | **Body edge**, so Closure reaches 3.4 m from the centre (§2) |
| D2 | Multi-mark handling: merge or cancel | **Cancel at lock**, with 3.8 m spacing in Phase 1 and 4.8 m in Phase 2 (§6) |
| D3 | Lanterns: hittable boss parts or decoration | **Decoration**: no hits, no collision, shots pass through (§5.4) |
| D4 | Hurt radius in Phase 2 | **1.8 m**, matching the open petals; collision stays 1.2 m |
| D5 | Damage M 18 / H 26 at Steady Ticking | As the packet, below the final boss |
| D6 | Midnight Toll mechanic | **Evensong** seeds after Closure (§9) |
| D7 | Verdant Chimes arena dimensions and collision | Needed for T1; this spec uses the 11.5 m placeholder, shared with Sylvara |
