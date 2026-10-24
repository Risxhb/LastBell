# LAST BELL

## Boss Design Document 01 — The Bellkeeper

**Version:** 1.0  
**Status:** Proposed encounter design — not yet implemented or playtested  
**Engine:** Unity (target)  
**Genre:** Fast-paced action raid roguelite  
**Players:** 1–4 (target)  
**Date:** October 20, 2026  
**Boss design philosophy:** Scripted sequences with controlled random variations; high punishment and skill-based recovery; no mandatory party roles.

> **Core vision:** The Bellkeeper is a towering clockwork guardian whose attacks resemble an ancient mechanical performance: precise, rhythmic, and increasingly complex. Players master a learnable performance through repeated attempts while adapting to clearly communicated changes in targets, positions, and orientation.

---

## 1. Design foundations

### 1.1 Design influences

*Last Bell* combines:

- **Windblown / Hades-inspired action combat:** Responsive movement, dodging, positioning, punish windows, and build variation.
- **High-end raid encounter mastery:** Memorizing a complex encounter, executing mechanics under pressure, and improving through repeated attempts.
- **Roguelite replayability:** Familiar boss mechanics remain interesting when faced with different builds and team compositions.

These are design inspirations, not claims that the target games use the same internal systems.

### 1.2 Non-negotiable boss pillars

| Pillar | Design requirement |
|---|---|
| Difficult but fair | Players can determine why they failed. No invisible damage, deceptive hitboxes, or unreadable attacks. |
| Repetition creates mastery | Repeated attempts teach recognizable sequences, timings, and solutions. |
| High punishment | Significant errors can cost most of a player's health or end an attempt. |
| Skill-based recovery | An error does not always force a restart. Strong subsequent play can recover a limited amount. |
| Escalation through combinations | Later phases layer and recombine mechanics taught earlier. |
| No mandatory roles | All required mechanics work without a Tank, DPS, or Healer. |

**Core rule:** Difficulty should primarily come from executing learned mechanics, not unpredictably guessing the next attack or grinding through excess boss health.

### 1.3 Repetition goals

A successful difficulty curve moves through four player experiences:

1. **Recognition:** Learn tells and basic safe regions.
2. **Execution:** Successfully avoid individual attacks, but struggle with their combinations.
3. **Adaptation:** Solve known mechanics from changing positions or while playing different builds.
4. **Consistency:** Execute the whole encounter, capitalize on openings, and recover intelligently from occasional errors.

The ideal post-death reaction is: *“I understand what went wrong, and I can fix it on the next attempt.”*

---

## 2. Boss profile

| Attribute | Proposed target |
|---|---|
| Name | **The Bellkeeper** |
| Boss type | Mechanical duel / execution encounter |
| Difficulty | High — introductory mastery boss |
| Party size | 1–4 |
| Role requirements | None |
| Phases | Three, with a Phase 3 signature finale |
| First-clear fight duration | Approximately 3–4 minutes (playtesting target) |
| Core skills tested | Telegraph reading, spatial positioning, dodging, target tracking, combo execution, controlled aggression |
| Arena | Open clockwork dais with a visually calm floor and readable bounds |
| Visual identity | Monumental brass-and-cobalt bell guardian; ceremonial clockwork motions and harmonic effects |

### 2.1 Encounter fantasy

The Bellkeeper fights with the precision of a ritual machine. Its animations, clockwork parts, bells, and chimes reinforce attack timing. It does not behave like a chaotic monster: its escalating routine is a performance that players gradually learn to complete.

The concept draws on *Last Bell*'s proposed clockwork-fantasy art direction: aged brass, midnight cobalt stone, resonant gold highlights, ivory details, and violet skyborne architecture. The combat floor is an information surface first and a decorative surface second.

### 2.2 Technical context

This design targets **Unity 3D**, with a stylized high-oblique gameplay camera. Older project documentation describes a **Godot** solo prototype involving the Bellkeeper and two warning shapes. That prototype is historical reference only; these new attack timings, damage levels, phases, recovery mechanics, and Unity systems are **proposals**, not implemented source behavior.

---

## 3. The four core attacks

Values below are **starting tuning targets** in proposed Unity-world meters and seconds. They are subject to validation against actual arena dimensions, player movement speed, dodge distance, collision geometry, and camera projection. Damage percentages are relative to player maximum HP.

| Attack | Behavior | Warning target | Punishment target | Skill tested |
|---|---|---:|---:|---|
| **Bell Sweep** | Directional **80° full-width cone** extending **8 m** | 0.95 s | ~50% max HP | Read facing and escape a committed cone |
| **Hollow Toll** | Dangerous annulus **2.6–6.8 m** from the boss | 1.10 s | ~45% max HP | Choose the inner or outer safe region |
| **Pendulum Charge** | **2.2 m-wide** charge lane toward a locked target location | 0.80 s | ~50% max HP | Dodge sideways and track boss repositioning |
| **Shattered Chime** | Delayed **1.4 m-radius** marked explosions | 1.20 s | ~25% max HP per hit | Manage timed movement and ground space |

**Important:** The new Bell Sweep angle and dimensions differ from the legacy prototype's 72° cone and source-coordinate radii. Do not mix old prototype units or timings with the proposed Unity dimensions.

### 3.1 Bell Sweep

**Behavior:** A large directional cone extending from the Bellkeeper.

- The Bellkeeper rotates toward the target during the first **0.30 s** of its warning.
- At the end of that tracking interval, the boss's facing and the cone geometry **lock**.
- The remainder of the windup is fixed. The attack does not bend toward a dodging player.
- Players escape past either cone edge or move beyond its range.
- The final hit checks authoritative combat geometry, not an animator pose or approximate VFX.

**Visual tell:** The boss aligns its resonator arm and projects a crisp sector warning on the floor.

**Later use:** Appears immediately after a charge or alongside the aftermath of delayed explosion markers.

### 3.2 Hollow Toll

**Behavior:** A dangerous circular ring around the boss, with a safe region near its center and another beyond its outer radius.

- The inner edge is at **2.6 m** and the outer edge at **6.8 m**.
- The boss visibly compresses its chest bell before release.
- Players choose a safe pocket based on their position when the warning appears.
- The ring is anchored to the boss's position defined for that attack; once telegraphed, dangerous geometry must not silently drift.

**Visual tell:** Two clearly outlined concentric circles with danger fill only in the annular region.

**Later use:** Resolves after the boss charges to a new location, forcing players to account for repositioning.

### 3.3 Pendulum Charge

**Behavior:** The Bellkeeper charges down a visible lane toward a selected target location.

- The target is selected under documented encounter targeting rules.
- A clearly shown lane appears and **locks before movement**.
- The boss commits to the lane and does not unexpectedly redirect mid-charge.
- The boss ends at a new position, changing the geometry of the next mechanic.
- Players dodge perpendicular to the lane rather than simply retreating along it.

**Visual tell:** A strong linear telegraph, aligned body posture, and pendulum-like weight shift.

**Later use:** Starts the Pursuit combo and the signature Final Toll.

### 3.4 Shattered Chime

**Behavior:** Delayed explosion markers appear near players or approved positions.

- Markers can track briefly during initial acquisition, but must **lock** before the final warning period.
- A locked marker cannot jump to a new position.
- Distinct outlines and timing cues communicate where and when the blast will occur.
- Solo play uses sequential markers to test motion planning.
- Co-op can distribute distinct markers across players to create spacing pressure.

**Visual tell:** Circular, patterned danger markers with clear countdown rhythm.

**Later use:** Combined with Bell Sweep to test whether players can escape one region without entering another.

### 3.5 Attack animation contract

All normal attacks use the following logical states:

1. **Windup:** Recognizable animation and visible telegraph; permitted tracking occurs only in a defined sub-window.
2. **Lock:** Target and damaging geometry become fixed before resolution.
3. **Resolve:** The hit check happens at the authoritative simulation time.
4. **Recovery:** The boss commits to a brief vulnerable interval, rewarding successful play.
5. **Decision:** The Encounter Director advances to the next authored attack/block.

Normal recovery intervals should initially be tested near **0.6–0.9 s**. Signature sequences may reward completion with roughly **2.0 s** of vulnerability. These are tuning targets, not required constants.

**Player rhythm:** Read → Dodge → Position → Punish → Prepare.

---

## 4. Phase structure and attack timeline

### 4.1 Phase rules shared by the encounter

- Phase boundaries are based on **boss HP**.
- When HP crosses a phase boundary, the transition is **queued** until the active pattern finishes.
- The boss never interrupts an already shown telegraph with an unrelated lethal attack.
- Boss damage output can shorten the amount of repeated material, but players should not be able to unintentionally skip all of the encounter's essential teaching and mastery moments.
- Attack orders are authored. Approved variations are selected by seeded rules at defined decision points.

### 4.2 Phase 1 — The First Toll (100–70% HP)

**Goal:** Teach all four mechanical verbs separately.

| Order | Action | What it teaches |
|---:|---|---|
| 1 | Bell Sweep | Interpret directional telegraphs and step out of a cone |
| 2 | Hollow Toll | Find the correct safe region around the boss |
| 3 | Pendulum Charge | Evade a committed charge and track the new boss location |
| 4 | Shattered Chime | Handle delayed ground markers |
| 5 | Recovery window | Attack after demonstrating survival and positioning |

The order is **fixed** and loops until the phase transition becomes eligible. Boss facing and individual targets may still depend on player position, but players always learn the same high-level attack order.

### 4.3 Phase 2 — The Dissonance (70–35% HP)

**Goal:** Combine familiar attacks into learnable, increasingly punishing blocks.

| Pattern block | Fixed sequence | Permitted variation |
|---|---|---|
| **A: Pursuit** | Pendulum Charge → Hollow Toll | Selected target and left/right charge approach |
| **B: Discord** | Shattered Chime → Bell Sweep | Explosion placement within prevalidated positions |
| **C: Reversal** | Hollow Toll → Pendulum Charge | Approved mirrored charge path |

**Block order:** A → B → C → repeat until the next phase boundary is reached.

The player already knows the component attacks. The new problem is how the end position from one mechanic affects the solution to the next.

### 4.4 Phase 3 — The Final Toll (35–0% HP)

**Goal:** Demonstrate mastery through a longer, more threatening execution sequence.

Phase 3 opens with the signature sequence:

```text
The Final Toll

1. Pendulum Charge
       ↓
2. Hollow Toll (at the boss's new position)
       ↓
3. Shattered Chime (delayed marked explosions)
       ↓
4. Bell Sweep (after facing locks)
       ↓
5. Extended vulnerability window
```

**Target total sequence duration:** Approximately **6–8 s**, subject to readability and movement testing.

A shorter Pursuit block follows, then The Final Toll repeats. This maintains a recognizable high-pressure rhythm without turning the final phase into unstructured attack spam.

The Final Toll's decisive final strike may target about **80% maximum HP damage**. This makes execution especially important, but a healthy player who survives an error might still be able to recover. Lethal signature failures should be rare, unmistakably telegraphed, and consistently solvable.

### 4.5 Sequence construction rule

Every combined sequence must provide a **reachable safe solution** using the baseline player movement/dodge/defense kit, including for the worst approved random variation and relevant party sizes.

A combination is invalid if its hazards overlap in a way that forces unavoidable damage without an explicitly communicated and universally available counterplay.

---

## 5. Scripted sequences with controlled variation

### 5.1 Design philosophy

**The attack sequence stays learnable; the circumstances of executing it can change.**

Randomness should make players observe rather than only recite a memorized path. It must never secretly rewrite what an attack means.

| Allowed | Prohibited |
|---|---|
| Select a different eligible player to target | Retarget after the attack's announced lock point |
| Mirror a charge approach left versus right | Steer a supposedly committed charge toward a dodging player |
| Place explosions at different prevalidated spots | Create explosion danger without enough notice to escape |
| Begin the same combo from a new boss position | Insert unannounced moves into a learned sequence |
| Select one of two recognizable, signaled variants | Randomize warning duration invisibly during execution |

A useful **qualitative initial heuristic** is **80% fixed encounter structure / 20% controlled variation**. This is not a required probability distribution or a measured value.

### 5.2 Example: Phase 2 Pattern A — Pursuit

```text
Attempt 1:
  Charge down-right → boss lands right of center → Hollow Toll

Attempt 2:
  Charge down-left  → boss lands left of center  → Hollow Toll

Shared player knowledge:
  Charge ALWAYS precedes Hollow Toll in Pattern A.
  The player must read where the boss will finish before choosing a safe region.
```

Both variants test the same knowledge, yet the player cannot blindly travel to the same point each time.

### 5.3 Randomization contract

- Each encounter attempt stores an **encounter seed**.
- Pattern variation is selected from an **approved finite choice set**.
- Random selection occurs at an explicit **pattern/block boundary** or documented target-acquisition point.
- Targeting and damaging geometry are frozen at the defined lock moment.
- No new random roll may modify locked damage geometry.
- Rehearsal can use a chosen seed to reproduce a troublesome variation.
- All combinations are validated for solvability, animation clarity, arena boundaries, and camera readability.
- If a sampled variation is invalid for current player positions, the director should choose a prevalidated fallback **before** beginning the tell, never alter geometry secretly mid-warning.

### 5.4 Mechanics must remain readable

Random choices cannot cause:

- Warnings shorter than players can reasonably react to.
- An attack that visually indicates one safe zone but damages another.
- A charge that creates an unsolvable follow-up ring near arena walls.
- Hazard visual clutter that hides a second mechanic.
- Two independently fair attacks becoming an unavoidable overlap.

---

## 6. High punishment and skill-based recovery

### 6.1 Damage categories

| Failure level | Illustrative punishment | Intended result |
|---|---|---|
| **Minor** | 10–20% maximum HP | Noticeable mistake, still recoverable |
| **Major** | 40–65% maximum HP | One error sharply reduces survival margin |
| **Critical** | Potentially lethal | Reserved for major, well-taught signature moments |

**Balancing principle:** Two major mistakes in a short span should usually kill the player; one major mistake should not always end the attempt.

Do not solve difficulty by making every error fatal or by inflating boss HP. Those approaches reduce meaningful recovery and turn repetition into tedium.

### 6.2 Proposed universal system — Resonance Reclaim

**Status:** Optional gameplay feature, not confirmed as an implemented mechanic.

A limited portion of recently lost health becomes temporarily **recoverable** after a major hit. The player may reclaim that HP by executing gameplay well.

Possible first iteration:

1. Player takes a major hit.
2. A small capped portion of the lost HP enters a recoverable pool.
3. The player successfully evades the next meaningful mechanic.
4. The player lands an attack during a vulnerability window.
5. That completed skill sequence restores the capped recoverable HP.

**Example using 100 maximum HP:**

| Event | Current HP | Recoverable HP |
|---|---:|---:|
| Player starts healthy | 100 | 0 |
| Player receives a 50-damage hit | 50 | 15 |
| Player cleanly evades next mechanic | 50 | 15 |
| Player attacks during the punish window | 65 | 0 |
| Alternative: recovery opportunity expires | 50 | 0 |

### 6.3 Resonance Reclaim constraints

- Suggested initial opportunity: **8 s**.
- Suggested frequency cap: **once per boss phase**.
- Requires demonstrated successful play, not passive waiting.
- Does not require healing or a specific class.
- Recovered HP is capped and cannot reward intentionally trading hits.
- Recovery requirements must remain possible for melee and ranged builds.
- Time limits and phase transition behavior must be explicitly defined in implementation.

### 6.4 Post-hit protection

A brief, visually communicated post-hit invulnerability interval should prevent unfair multihit chains. However:

> **Failed positioning and health damage are separate outcomes.**

If a player stands inside a resolving danger zone but blocks or ignores its damage through guard/invulnerability, the combat log records that the **positioning mechanic was failed**, even if health did not drop. A clean dodge is a separate success state.

This distinction is important for meaningful post-attempt feedback.

### 6.5 Boss recovery and offensive agency

Surviving difficult patterns must create moments to attack. Test regular recovery intervals around **0.6–0.9 s** and a longer **~2 s** vulnerability interval after The Final Toll. Tune these for all intended weapon ranges, player animations, cooldowns, and mobility builds.

---

## 7. Solo and cooperative design

The base mechanics, visual language, and universal answers remain consistent across group sizes. Cooperative scaling adds target distribution and spatial responsibilities rather than merely multiplying health.

| Mechanic | Solo | Two players | Three–four players |
|---|---|---|---|
| Bell Sweep | Targets the player | One marked target | One clearly marked eligible target |
| Hollow Toll | Same basic safe geometry | Both resolve it | All resolve it |
| Pendulum Charge | Targets player location | Eligible target chosen visibly | Predictable distributed target selection |
| Shattered Chime | Sequential markers | Distinct marker assignments | Multiple marked players / greater spacing pressure |
| The Final Toll | Full attack sequence | Full sequence plus co-op positioning | Full sequence plus more demanding spacing |

### 7.1 Multiplayer fairness rules

- Players targeted by a mechanic must be **identifiable before lock**.
- No core mechanic assumes access to external healing, tank mitigation, or a class-exclusive mobility skill.
- Group size affects marker distribution, safe-space planning, and target selection within prevalidated solvable bounds.
- Consider boss HP scaling separately from mechanical scaling.
- Validate extreme team compositions, including all-offense parties.
- Target selection and damage outcomes must have a consistent multiplayer authority model when networking is implemented.

### 7.2 Optional roles — extension hooks only

| Role direction | Optional enhancement | Baseline solution still available |
|---|---|---|
| Tank-oriented | Intercept a charge, extend guard, or mitigate damage for allies | Dodge, position, and use universal defense |
| Healer/support-oriented | Improve recovery, supply limited shields, or help teammates recover after mistakes | Use universal Resonance Reclaim/defense options |
| DPS-oriented | Greater burst efficiency during vulnerability windows | All builds can deal enough damage to win |

**Role design principle:** A role changes **how** a mechanic is handled, not **whether** it is possible to handle it.

The Bellkeeper design should remain viable if traditional Tank/DPS/Healer roles are never introduced.

---

## 8. Visual readability and attack language

*Last Bell*'s painterly clockwork-fantasy aesthetic serves mechanical clarity. The encounter is intended for stylized low-poly 3D with a fixed or controlled high-oblique view.

### 8.1 Boss and arena direction

- Bell-shaped brass torso and monumental, readable silhouette.
- Clockwork arms telegraph facing and intended attack direction.
- A resonant gold core communicates charging and releases.
- Midnight-cobalt floor provides stable visual contrast.
- Decorative gears, rings, and engraved dials remain low contrast during active hazards.
- The playfield is readable even when atmospheric effects or audio are disabled.

### 8.2 Telegraph rules

- Hazard fill and boundaries must match **authoritative hit geometry**.
- Use patterns/outlines as well as colors to distinguish danger.
- Mark both the inside and outside boundary of Hollow Toll.
- Bell Sweep orientation remains visually fixed once locked.
- Audio cues reinforce, but never replace, visual tells.
- Attack resolution occurs at a clearly represented gameplay time, not when a particle happens to finish playing.
- Reduce decorative VFX when overlapping attacks make the danger shape hard to see.

### 8.3 Recommended attack feedback states

| Outcome | Meaning | Feedback example |
|---|---|---|
| **Avoided** | Player was outside the damaging region | Quiet success cue |
| **Guarded** | Player was inside but prevented damage with guard | Shield impact; mechanic positioning still failed |
| **Immune** | Player was inside but received no damage because of invulnerability | Spectral ripple; mechanic positioning still failed |
| **Hit** | Player was inside and lost health | Impact flash, HP reduction, and mechanic ID |

---

## 9. Rehearsal and repetition systems

### 9.1 Expedition mode

The ordinary roguelite experience includes build decisions, reward progression, and meaningful defeat consequences. Winning a complete encounter gives its real progression and rewards.

### 9.2 Rehearsal mode

Once a boss is discovered, a non-rewarding practice space can provide:

- Full-fight practice.
- Practice of previously reached phases and specific pattern blocks.
- A standardized reference build for fair learning.
- Seed selection/replay for reproducible variations.
- Very fast restart after failure.
- No expedition progression rewards.

This resolves a core genre tension: boss mastery requires quick repeated attempts, while roguelite expeditions can otherwise require long traversal between opportunities to practice.

### 9.3 Failure feedback

Provide a concise post-death explanation that distinguishes where the player failed from how much damage was taken.

**Illustrative failure report:**

```text
ENCOUNTER FAILED
Boss:                  The Bellkeeper
Phase reached:         3 — The Final Toll
Fatal mechanic:        Bell Sweep
Mechanics avoided:     12 / 15

WHAT HAPPENED
You remained inside the locked cone when Bell Sweep resolved.

HOW TO IMPROVE
Once the boss locks facing, move beyond either radial cone edge
before the strike.
```

At minimum, record boss phase, attack/pattern ID, chosen variation, player position at resolution, outcome category, damage, and whether the player used guard or invulnerability. Where possible, log phase progression across attempts.

### 9.4 Initial player testing hypothesis

For experienced action-game players, one exploratory target is approximately **8–20 meaningful first-clear attempts**. This is a **hypothesis for playtesting, not a desired quota**. Lower clear rates alone do not prove quality; evidence of learning, explainable deaths, and increasingly reliable execution matters more.

---

## 10. Unity implementation architecture

**Scope:** Architecture proposal. No Unity migration or implementation is implied by this document.

### 10.1 Runtime responsibilities

| Unity component/system | Responsibility |
|---|---|
| **EncounterDirector** | Current phase, authored sequence index, thresholds, pattern transitions, seed ownership |
| **PatternSequence** | ScriptableObject or equivalent data describing fixed attack order and variation choices |
| **AttackDefinition** | Shape, damage, windup, target tracking window, lock moment, hit frames, recovery |
| **BossCombatController** | Executes boss movement and state transitions, exposes authoritative combat events |
| **TelegraphController** | Renders floor warnings from the actual damage geometry and timing data |
| **EncounterRandomizer** | Deterministic, seeded selection among approved variations |
| **MechanicResolver** | Evaluates avoidance, guarded failures, immunity, hit damage, and recovery rules |
| **MechanicAnalytics** | Records attempts, deaths, mechanic outcomes, variation seeds, and progression |
| **OptionalRoleModules** | Future independent hooks for class-specific defense/support/offense capabilities |

### 10.2 Data-driven encounter blocks

Below is a **human-readable example schema**, not a Unity-native asset or a validated C# implementation:

```yaml
pattern_id: P2_PURSUIT
phase: 2

attacks:
  - PendulumCharge
  - HollowToll

variation:
  charge_direction:
    options: [left, right]
    selection: seeded
  target:
    selection: eligible_player

rules:
  attack_order_locked: true
  charge_target_locks_during_windup: true
  allow_unannounced_attacks: false
  phase_transition: after_pattern

recovery_window_seconds: 0.8
```

### 10.3 Authoritative timing

- Simulation time decides whether an attack starts, locks, deals damage, resolves, or ends.
- Animations communicate that state but do **not** invent or override damage windows.
- When possible, use a deterministic fixed-timestep combat simulation.
- Presentation may interpolate between authoritative states but never offset the hit check.
- Targeting must be stable and reproducible for a given recorded seed and player-state sequence.
- Any networked implementation must authoritatively resolve targets and damage.

### 10.4 Proposed implementation progression

| Milestone | Deliverable | Acceptance criterion |
|---|---|---|
| **1. Combat foundation** | Responsive player movement, dodge, attacks, boss targeting | Movement and damage timing feel trustworthy |
| **2. Core boss** | All four standalone attacks | Each is readable and avoidable |
| **3. Fixed script** | Phase 1 teaching sequence | Players can learn a repeatable rhythm |
| **4. Controlled variation** | Seeded target selection and mirrored approaches | Variations remain fair and legible |
| **5. Combined patterns** | Phase 2 blocks and Final Toll | All combinations have viable safe solutions |
| **6. Recovery/repetition** | Resonance Reclaim prototype, rehearsal, feedback logging | Mistakes hurt, but learning and clutch recovery are visible |

Start by proving the encounter in a solo Unity slice. Design interfaces with co-op in mind, but avoid letting multiplayer/role features delay validation of the fundamental attack kit.

---

## 11. Encounter validation and fairness gates

Before accepting a pattern, verify:

- [ ] Its damaging geometry matches the displayed telegraph footprint.
- [ ] It has enough readable warning time for the intended player movement and dodge kit.
- [ ] Its attack target and angle cannot change after the defined lock point.
- [ ] At least one reachable avoidance path exists from permitted player positions.
- [ ] Overlapping attacks do not produce unavoidable damage under valid play.
- [ ] The pattern remains solvable for **solo play**.
- [ ] Additional targets and markers remain solvable for each planned co-op group size.
- [ ] No Tank, Healer, or DPS-specific action is required to pass.
- [ ] Arena boundaries, boss repositioning, and obstructions do not invalidate the solution.
- [ ] Camera framing and floor contrast preserve telegraph visibility.
- [ ] Audio-off and accessible visual variants preserve critical information.
- [ ] Recovery windows allow offense by the intended build range.
- [ ] Each permitted random choice is reproducible from the saved seed.
- [ ] Phase thresholds cannot interrupt active warning geometry.
- [ ] Mechanic failures and damage outcomes are logged separately.

Automate geometry/timing assertions where feasible; still require human playtesting for perceived readability and enjoyment.

---

## 12. Balance and analytics

Measure progression instead of judging difficulty only by a low win rate.

| Metric | What to learn |
|---|---|
| Attempts before first clear | Overall mastery demand and retry fatigue |
| Phase reached per attempt | Whether players steadily advance |
| Fatal mechanics | Which patterns are disproportionately confusing or punishing |
| Success rates by pattern | Whether teaching produces actual mastery |
| Recovery activations | Whether skill-based recovery is meaningful without being exploitable |
| Damage dealt during windows | Whether avoiding attacks creates satisfying offense |
| Build diversity in successful clears | Whether victory depends too heavily on narrow builds |
| Clear rates across party sizes | Whether co-op variants preserve fair challenge |
| Retry time | Whether losses lead quickly back to useful practice |
| Self-reported death clarity | Whether players understand what to do differently |

### 12.1 Adjustment hierarchy

When a mechanic is disproportionately failing playtests:

1. Check **telegraph visibility and collision accuracy**.
2. Check **safe-path feasibility** at actual movement speed/dodge distances.
3. Check whether the player was **taught** the mechanic before it appeared in a combination.
4. Check **variation fairness** and boss positioning at pattern start.
5. Only then adjust **warning duration, damage, cadence, or complexity**.

Never disguise a defective or unfair mechanic as intentional difficulty.

---

## 13. Final design contract

The following ten rules should govern the Bellkeeper and future *Last Bell* bosses:

1. Fixed, learnable sequences form each encounter's backbone.
2. Controlled randomness alters target, orientation, or location—not secret gameplay rules.
3. Difficulty escalates through combining learned attacks and demanding reliable execution.
4. Major errors are highly punishing, but some successful recoveries remain possible.
5. All mandatory mechanics are solvable through universal player abilities.
6. Solo and co-op share the same underlying mechanical language.
7. Tank/DPS/Healer interactions remain optional extension hooks.
8. Telegraphs, collision geometry, and hit timing must agree.
9. Failed positioning and actual damage are different recorded outcomes.
10. Repeated attempts must reward understanding, precision, and consistency.

### Intended emotional arc

The first Bellkeeper attempt should feel overwhelming. Soon players recognize its individual moves. Later, they recognize its entire choreography but must still execute it perfectly enough under pressure. Victory comes through learned mastery.

When they return with a different roguelite build, the fight remains familiar without being trivial: they know **what** the boss will ask them to do, but must rethink **how** they use their abilities to do it.

> **Last Bell's boss promise:** Punishing fights that become more satisfying as players master them—not merely easier as characters gain power.

---

## 14. Open production decisions

This design intentionally leaves several questions unresolved until the combat prototype and playtests provide evidence:

- Final player movement speed, dodge range, and dodge immunity timing.
- Arena dimensions and placement constraints for the four attack shapes.
- Whether Resonance Reclaim is adopted, replaced, or cut.
- Exact timing and damage tuning for all attacks.
- How many required/optional repeats appear in each phase.
- Boss target-selection fairness and co-op threat distribution.
- Final camera framing and warning shader style.
- Multiplayer networking authority/rollback/latency policy.
- Whether a traditional role/class system is introduced.
- Rehearsal unlock timing and its relationship to expeditions.

**Production recommendation:** Lock the universal player movement/defense feel and implement each of the four standalone attacks before finalizing the multi-attack sequences.

---

### Related project documents

- `GAME-CONTEXT.md` — historical solo Godot prototype and original Bellkeeper mechanics.
- `LAST-BELL-UNITY-CLOCKWORK-FANTASY-ART-DIRECTION.md` — proposed Unity URP clockwork-fantasy visual direction.

**Document status:** Proposed design specification. Not a substitute for implementation tests, collision verification, or a playable Unity build.
