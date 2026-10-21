# Last Bell — single-arena playable slice

October 20, 2026. **Requested scope:** three minibosses and one final boss, all playable sequentially in **one arena**. This is the near-term complete-run milestone, extending the earlier one-encounter engineering increment. It does not claim implementation, final balance, new collaborator acknowledgment or a delivery date.

Read [current design](../game-design.md), [architecture](../architecture.md) and [production plan](../../raid-simulator-plan.md). Boss names, timing/size/health proposals and intermission rules below are planning recommendations. Existing no-block/parry/iframe, cooldown-only attack, finite movement stamina and three-class/four-difficulty/solo–8 requirements remain.

## 1. What the player plays

Choose class and difficulty → ready/start → **Dial Warden** → **Pendulum Sentinel** → **Chimebound Oracle** → short intermission representing the future shop → **The Bellkeeper** → victory/results.

These are three distinct miniboss encounters and a larger final encounter, not three phases of one boss. They share an arena and reusable attack systems, but have distinct silhouettes, attack orders, movement behavior and encounter IDs. Proposed successful run: **8–12 minutes**, a tuning target, not an estimate of development effort.

The first end-to-end engineering build can use one class, one difficulty and geometric boss stand-ins. That is an internal progress build. The intended slice acceptance still tracks three starting classes, four tiers and solo/full eight-player evidence separately; do not silently call the reduced build the complete slice.

## 2. One reusable arena

Propose **The Clockwork Dais**: a circular, approximately **24 m diameter** floor, fixed isometric camera, visible low wall and unobstructed central fighting space. Test this size before detailed art; resize if eight-player safety/camera tests fail.

- One Unity gameplay scene, one arena collision layout, one camera rig and one spawn layout. Do not load a different level for each boss.
- Eight marked player start sockets on a safe arc/ring, one initial boss socket, validated boss repositioning destinations. Start sockets and player footprints cannot overlap.
- Boss-body collision is real. Keep an escape corridor around the boss and wall; charges cannot pin players in unavoidable hazard regions.
- No falling deaths or moving floor geometry in this slice. Knockback stops at arena collision; no extra wall damage is assumed.
- Decorative clock hands/rim lights identify the current encounter without changing collision. Keep floor warnings dominant.
- After each defeat, cancel hazards, remove defeated-boss collision, clear transient effects and prepare the next boss in this same scene.
- Menu, practice and model viewer can be separate screens/scenes. “One arena” constrains combat content, not all UI.

Claude primarily authors environment, silhouettes and readability. Codex integrates camera/scene/menu/build and the shared collision/rule adapters. Use labeled temporary models until production assets exist.

## 3. Encounter roster and teaching sequence

| Encounter | Proposed duration | Two core patterns and behavior | Lesson / distinction |
| --- | --- | --- | --- |
| **1 — Dial Warden** | 60–90 s | **Hand Sweep:** a locked cone. **Hollow Dial:** dangerous ring with inner/outer safety. Slow pivoting, mostly central position; isolated patterns first, then a short alternating sequence. | Read facing, leave a cone and choose ring safety while attacking. Compact broad silhouette with one clock-hand weapon. |
| **2 — Pendulum Sentinel** | 75–105 s | **Pendulum Charge:** locked lane, lateral avoidance, real boss repositioning. **Low Pendulum:** clearly labeled low crossing bar that can be jumped or routed around, using an explicit height-clearance rule. | Sideways movement, jump timing, stamina restraint and attacking after commitment. Tall swinging silhouette; recovery after a charge. |
| **3 — Chimebound Oracle** | 75–105 s | **Shattered Chime:** delayed circles placed from visible marked positions, then locked. **Dividing Chime:** a familiar cone after markers are placed; initially sequential, later an authored safe overlap. | Place danger deliberately, plan the next safe region and keep moving without wasting dodge. Small floating bell-cluster silhouette. |
| **4 — The Bellkeeper** | 3–4 min | Reuses **Bell Sweep, Hollow Toll, Pendulum Charge and Shattered Chime** from the supplied reference, combining the mechanics taught above. Larger presence and longer multi-phase fight. | Demonstrate mastery of the full movement/positioning vocabulary. Distinct original bell-shaped brass body. |

Each miniboss begins with an isolated teaching demonstration; later repetitions vary only tested target/orientation choices. No normal mobs, adds, mandatory healer, block/parry or resource/upgrade checks.

Implement five reusable mechanic families: cone, annulus, charge lane, delayed circle and low jump-clearable bar. The bar reuses lane geometry with a height condition rather than creating a separate collision engine. Player mouse aiming is independent from boss target selection.

## 4. Bellkeeper final fight

Use the [supplied v1.0 reference](https://github.com/Risxhb/collab/blob/docs/bellkeeper-design-reference/reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md) as input, with the newer gameplay rules taking precedence. Preserve that source unchanged.

- **100–70% HP — Recognition:** demonstrate the four attacks once before longer combinations, then repeat short sequences.
- **70–35% — Combination:** Charge → Ring; Markers → Cone; Ring → Charge. Different starting position/locked target can change routing, not hidden rules.
- **35–0% — The Final Toll:** Charge → Ring at the new boss position → delayed Markers → locked Cone → clear recovery window.
- Queue phase changes until the active authored pattern finishes; defeat immediately cancels pending attacks and cannot deal damage after victory.
- Initial budget: at most two simultaneous damaging patterns. Higher difficulty uses authored schedules, not unconstrained random overlaps.
- Do not copy the reference's 0.95-second Sweep or large damage percentages blindly. Recalculate with actual movement, finite stamina, collision and reaction margin. No iframe-based escape.

The extra tier-four final-boss ability is **Memory of Midnight**: preview one recorded attack footprint, then repeat it at that fixed location during a later safe, authored block. Store at most one remembered attack and show its return clearly. This is a proposed scoped use of Echo, not a baseline requirement on all tiers.

## 5. Run flow and authoritative state

Proposed state machine:

```text
Preparation
  -> Intro -> Miniboss1 -> Clear
  -> Intro -> Miniboss2 -> Clear
  -> Intro -> Miniboss3 -> Intermission
  -> FinalIntro -> FinalBoss -> Victory

Any combat -> Defeat (solo death / no standing co-op players)
Defeat -> RetryRun -> Preparation / encounter 1
Practice -> selected encounter -> practice result -> retry or menu
```

The host/shared rules own run index, active encounter ID, difficulty, roster, boss state, down counts and state transitions. Clients cannot advance encounters or report boss death. One authoritative clock advances combat.

Suggested components, subject to U3 review:
- **Arena scene:** permanent floor/camera/spawns; swappable boss prefab.
- **RunDirector:** start/clear/intermission/defeat/victory and ready boundaries; no attack hit testing.
- **EncounterDefinition:** identity, prefab, attack schedule, phase thresholds and per-count/per-tier parameters.
- **EncounterDirector:** starts authored attacks through the existing shared geometry/damage seam.
- **RunState:** seed, roster/classes, encounter index, difficulty and per-player down counts.
- **PracticeController:** isolated encounter selection and complete checkpoint/reset behavior.

These are proposed responsibilities, not a second production rules implementation or approval to bypass #6 contracts.

## 6. Transitions, failure and practice

**Between bosses:** proposed short 5–8-second clear/ready phase; show next boss name, clear old hazards, recover stamina/reset cooldowns and reposition standing players safely. In co-op require the shared ready boundary before start. No encounter advances while an unresolved downed player would be stranded.

**Health policy proposal:** carry health across the run for the first test build, with no automatic intermission heal or healing ability. Healing remains a future decision. Tune damage/run length for this policy; record it as replaceable rather than a settled release rule.

**Revive proposal for the slice:** no bleedout timer initially. Standing teammates may channel the provisional 3 s + 2 s per previous down of that player, within a defined tested interaction range; moving, taking a hit or releasing input cancels channel. Only one channel completes for a downed actor; propose restoring 25% max health. These numbers/rules require review and are not previously confirmed healing design. The down count persists across bosses and clears only on a fresh run. With nobody standing, immediately fail the run; no indefinite all-downed state.

**Shop intermission:** after miniboss 3, show a simple in-arena “Intermission — shop arrives later” panel with Continue/Ready. No merchant, currency, purchases, healing or upgrade-choice implementation. Never advertise functional shopping.

**Failure:** end attacks, show boss/attack cause and offer Retry Run or Practice. Retry starts encounter 1 with fresh health/stamina/cooldowns/RNG/run down counts. Retain selected class/difficulty; practice progress cannot become run progress.

**Practice:** select any of the four slice encounters without an unlock dependency; repeat full fights, with phase starts optional later. Reset the full actor/boss/timeline/assignments/random state. A solo practice clear never unlocks the next live run encounter or grants rewards.

## 7. Four difficulties and every party count

Build/test the standard tier first, then use encounter data for four authored variants. Names remain proposed.

| Tier | Slice treatment |
| --- | --- |
| First Chime | Longer warnings, isolated patterns, generous recovery and safe regions. No required class/support action. |
| Steady Ticking | Intended base encounter schedules and ordinary movement pressure. |
| Striking Hour | Combinations of taught attacks, staggered assignments and shorter recovery; finite-stamina paths still validated. |
| Midnight Toll | Adds a new boss-specific ability: Warden **Retoll** (previewed second cone), Sentinel **Return Stroke** (announced return charge), Oracle **Afterchime** (announced marker repeat), Bellkeeper **Memory of Midnight**. Teach each addition before combining it. |

These are added abilities for each boss on the highest tier, not only HP multipliers. Verify stamina and escape budgets; do not shorten every warning uniformly.

For counts 1–8, use bounded marker counts/subgroups and explicit odd-count rules. Do not place eight large circles without tested floor capacity. Boss HP may scale for fight duration, but player count does not automatically increase damage or difficulty tier. Down/revive/disconnect cancels/reassigns impossible live mechanics. Scripted eight-client capacity and eight-human readability are separate evidence gates.

## 8. Implementation order and ownership

| Step | Deliverable / owner | Exit check |
| --- | --- | --- |
| **S0 — Playable shell** | Foundation #6 pins Unity/shared contract; Codex #30 makes scene/start/retry; Claude #31 supplies graybox | Clean checkout and Windows build; move/aim/attack/dodge/jump; truthful one-pattern hit feedback |
| **S1 — Four-slot run skeleton** | Codex #30 integrates shared RunDirector; foundation owns lifecycle rules; Claude #8 supplies four encounter definitions | Four named stand-ins can be defeated in order, intermission/ready works, victory occurs only after slot 4; failure/reset work at every slot |
| **S2 — Three real minibosses** | Claude #8 authors schedules/identity/content; #6 supplies shared mechanics; #30 integrates | All three playable with distinct attack loops, finite-stamina safe paths and no leftover hazards/collision |
| **S3 — Final Bellkeeper** | Claude #8 reconciles phases/reference; foundation/Codex integrate | All phases, combined patterns and clear recovery; repeated final fight and full run pass |
| **S4 — Roster, difficulties, practice/revival** | Foundation shares rules; Codex #30 integrates three class kits/UI; Claude #8/#31 content and four-tier variants | Required roster without unapproved healing, four practice fights, per-player revive escalation, top-tier added abilities |
| **S5 — Co-op and slice acceptance** | Chzy18/Codex #7 networking; all lanes review | Every count 2–8 and full eight-player Internet/readability, down/disconnect/host-loss and complete Windows-run acceptance |
| **S6 — Presentation polish** | Claude #31 content/viewer; #30 integration; humans review | Gameplay-camera boss identities, readable warnings, audio-off cues and measured representative performance |

S1 is deliberately early: prove a complete four-fight run before polishing one boss indefinitely. S2/S3 then replace placeholder schedules without changing run flow. Polish and viewer work can proceed once seams are agreed; neither final models nor a full viewer feature set blocks the first end-to-end run.

Do not start new unassigned agents or duplicate lanes. This plan proposes work sequencing within existing responsibilities, not evidence that another operator has started it. No calendar estimate until bootstrap and the first working loop establish actual throughput.

## 9. Definition of a complete slice

- One arena, three distinct minibosses, one larger final boss, fixed order and honest future-shop intermission.
- Actual cursor-aimed cooldown combat, finite stamina/jump/sprint/dodge, collision, no iframes, clear stun/knockback and full failure/practice handling.
- Three starting classes and four difficulties tracked to acceptance; a staged one-class/one-tier build stays labeled partial. Third-class identity/support effects remain an explicit kit decision; no mandatory healing.
- Run failure always restarts at encounter 1; practice can retry each encounter; revive counts persist correctly through all bosses.
- Each encounter has reachable safe routes at all supported counts/tiers; final tier adds a boss-specific ability; no mandatory purchases/upgrades.
- Fresh Windows checkout/build completes the entire run and returns to menu/retry; repeated runs have no stale hazards, actor state, assignment or event effects.
- Unity, solo, scripted capacity, separate-network 2–8, eight-human feel/readability and performance evidence are recorded separately. No full acceptance from a standalone or browser run.

## 10. Focused validation

Check the run-transition matrix at all four slots, near-simultaneous last-player/boss death (define a single deterministic precedence through #6), boss death during a warning, repeated Continue/Ready input, stale encounter callbacks, checkpoint generations, revive interruption/duplicate completion and host loss during intermission. Proposed last-player/boss simultaneous-death policy: defeat takes precedence; document/test before implementation.

For geometry, include wall/boss pinning, charge endpoint limits, occupied spawn sockets, jumping during knockback, empty stamina, minimum reaction margin, chained hit-stun and marker capacity. Sweep/body collision and warning footprint use the same authoritative rules.

Human review asks whether each miniboss teaches something distinguishable and whether the final fight feels like a synthesis rather than an unrelated difficulty spike. Adjust readability and feasible routes before increasing punishment.
