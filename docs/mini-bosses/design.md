# Last Bell — Nine mini-bosses

Design proposal • October 21, 2026 • Hard difficulty • Exactly two phases each

These are encounter designs and concept-art references, not implemented or playtested content. No gameplay code is changed. The three existing slice mini-boss identities are preserved and expanded; six guardians are new proposals. All new lore, locations within arenas, rewards, transformations and numbers are proposals. The established area bosses Aurelion, Crucivar and Sylvara, and the final Bellkeeper, remain separate encounters.

## Established direction and sources

Reviewed local sources: overview (local reference: `../../gpt/01-game-overview.md`), visual guide (local reference: `../../gpt/02-visual-style-guide.md`), existing mini-bosses (local reference: `../../gpt/04-bosses.md`), areas (local reference: `../../gpt/05-arenas.md`), current design (local reference: `../../collab-wiki/Game-Design.md`), the three approved class briefs (local reference: `../Classes/README.md`), and area-boss reference (local reference: `../Boss-Model-Review/design-handoff.md`). The later class briefs supersede older blanket restrictions: Vanguard has partial frontal Brace, Arcanist has timed seals, and Chimesinger has Harmony/echo management and an immediate burst heal. None is required for encounter survival.

The campaign reference has three regular areas and a fourth final-boss area. This packet supplies three mini-bosses each for Skyward Spire, Brass Foundry and Verdant Chimes. Assigning the existing slice trio to Skyward Spire is a proposal consistent with their visual language, not a claim that campaign placement is already implemented.

| Area | Established visual rules | Application to this roster |
|---|---|---|
| Skyward Spire | Radiant ivory belfries, cobalt central dial, immaculate brass, pale clouds, astronomical ornaments; orderly and hopeful | Civic timekeeping instruments: a broad clock drum, tall pendulum tower and compact floating bell constellation. Phase changes reveal misalignment beneath immaculate shells. |
| Brass Foundry | Charcoal slate/iron plates, brass inlays, heavy gear rim, exterior furnace channels, chains and gantries; energetic and forged | Tuning, casting and escapement machinery; broad soundboard, low skating crucible and paired counterweight towers. Heat stays controlled and purposeful, with no soot-heavy industrial realism. |
| Verdant Chimes | Sage-grey stone, restrained turquoise dial, aged brass, ruined aqueducts, waterfalls, perimeter moss and ferns; serene and ancient | Garden timekeeper, aqueduct chime mechanism and seed vessel. Living growth frames functional mechanisms; no gore, horror or random root clutter. |

Painterly stylized 3D; large readable forms; clear face, core and active weapon. Gold cores glow at rest; hostile effects use coral/amber outlines, hatching and countdown marks. Turquoise decoration never doubles as danger. Phase 2 changes silhouette and function, not just emission color.

## Roster

| Area | Boss / title | Role | Signature mechanic |
|---|---|---|---|
| Skyward Spire | Dial Warden — Keeper of the Missing Minute | Direct timing duel | Read the offset second stroke, then exploit the wound-down dial |
| Skyward Spire | Pendulum Sentinel — Counterweight of the Heights | Movement and arena control | Cross behind a committed charge before its announced return |
| Skyward Spire | Chimebound Oracle — Auditor of Five Notes | Tactical reading | Follow visible bell order to distinguish the sounding and silent sectors |
| Brass Foundry | Rivet Cantor — Tuner of the Great Cast | Direct timing duel | Alternating short and delayed mallet beats with different silhouettes |
| Brass Foundry | Crucible Skater — Keeper of the Cooling Ring | Movement and arena control | Bait temporary casting trails and cross the cooled route |
| Brass Foundry | Escapement Twins — The Two Who Agree | Tactical redirection | Bait one tower's strike into its opposite pawl for a shared stagger |
| Verdant Chimes | Briar Metronome — Pruner of Lost Seasons | Direct spacing duel | Alternate inward and outward cuts without chasing the blade |
| Verdant Chimes | Silt Carillon — Warden of the Water Steps | Movement and arena control | Move through a measured procession of retracting chime lanes |
| Verdant Chimes | Vesper Seedkeeper — Custodian of the Last Bloom | Tactical placement | Bait a seed mark, let it root, then use the space it vacates |

## Shared encounter contract

**Arena:** all three mini-bosses within an area use its existing shared circular floor, sequentially. Their named stations below are entrance/staging landmarks, not new rooms. Proposed combat diameter is 24 m pending the actual area collision meshes. Broad, level continuous floor; no walkable elevation, interior pillars, permanent pools, pits or unreachable ledges. Low rim prevents falling. Tall decor, foliage, furnaces and waterfalls remain outside. Cover is decorative only: neither combatant gains projectile cover. Players can route either direction around the boss; collision never plugs the entire ring. Temporary hazards are encounter effects, not permanent changes to the floor.

**Tuning units:** seconds and metres; geometry is measured from boss collision boundary unless specified from arena center. Initial balance reference is a 100-HP unmitigated player: light (L) 10, medium (M) 18, heavy (H) 26 damage. These are normalized estimates, not runtime damage constants. No damage over time, slow, stun or knockback unless explicitly listed. Prototype hit-stun target: at most 0.15 s; validate every subsequent escape without post-hit immunity. One hit maximum per player per individual attack event. Separate clearly warned beats can each hit. No contact damage.

**Table notation:** W/A/R = warning-to-first-damage / active duration / minimum recovery, in seconds. CD is minimum interval from recovery end to next selection of that ability. It is never permission to interrupt a recovery. Default facing and footprint lock at warning start with a visibly latched boundary; only attacks explicitly listing a later target lock may track during the early warning. Recovery means no new attack windup or boss locomotion, unless a table explicitly describes a stopped hazard still active. Boss remains damageable. Melee and ranged counters apply to each row; the recovery gives ranged casting time and melee approach plus attacks. Where approach would consume the opening, move the boss closer or lengthen recovery; never assume range that the class lacks.

**Hard difficulty:** working fit is the proposed third tier, Striking Hour, without claiming its name is approved. Challenge comes from authored sequences, not uniform acceleration or excessive HP. Initial encounter target: 90–150 seconds with an appropriately played baseline solo kit, then tune HP from measured DPS. No enrage, healing requirement or mandatory upgrade. Not every opening fits a full rotation: each boss includes a 3–4 s major recovery for Vanguard's heavy attack and the casters' stationary commitments. Timed seals/bells continue normally and are not cleared by phase changes.

**Exactly two phases:** Phase 1 at 100–50%; Phase 2 at 50–0%. Finish the active attack, expire its hazards, then use a 2 s harmless transformation. Remain targetable; no heal or new health bar. A lethal burst ends the encounter immediately. Surviving a threshold-crossing burst queues one transition only. Phase 2 replaces the two named evolved attacks, retains the third Phase 1 attack unchanged, and adds one new attack. No third phase or secret death retaliation.

**Movement fairness:** walk/jump/sprint/dash exist; dash has no assumed iframes. Validate both the recorded 4.2 m/s prototype walk and proposed 6 m/s design walk. Start with 0.30 s reaction allowance, player collision radius 0.45 m and additional 0.25 m clearance; actual capsule replaces the estimate. Walkable escape distance must be no more than speed × (remaining warning − reaction), accounting for boss/wall collision. Dash and jump provide optional uptime; no listed pattern requires spending stamina to survive. Low bars are only jump-clearable if their authored height rule exists; every bar also has a ground route. No route may require passing through a boss or jumping a radial ground pulse.

**Sequencing:** one damaging pattern at a time in this first design pass. Internal multi-beat abilities explicitly retire the previous footprint before the next impact and provide fresh warnings. Persistent hazards expire before the next unrelated attack starts. Schedulers cannot skip recovery to force a combo. Vary legal choices within the described sequences to prevent rote single-button defense, but never read inputs or secretly shorten warnings. At arena edges, rotate or reject casts that eliminate the escape corridor. Each safe corridor must remain at least 2 m wide after collision margins; large cones use side corridors and a capped outer range.

**Party scaling:** solo and 2–8 players use the same geometry and timings. Targeted markers select 1 solo, 2 for parties of 2–4, and 3 for parties of 5–8, rotating recipients; maximum simultaneous markers is three, not one per player. Merge intersecting markers into a single damage event and reject placements that obstruct all exits. Mechanic targets exclude downed/disconnected players; a target lost after lock leaves the marked location unchanged. Never require distinct classes or simultaneous switches. Tune HP against actual group damage; do not scale hazard density with party count. Cosmetic pods, bells and towers are boss parts, not normal enemies or adds.

## 1. Dial Warden — Keeper of the Missing Minute

**Identity and appearance.** The Spire's public dial custodian still erases every irregular minute from its ledger. Existing squat cobalt drum silhouette, one long clock-hand weapon, brass rim and ivory face are retained. Proposed height 2.8 m against a 1.8 m hero. Short planted legs and a small counterbalance arm emphasize slow pivots. The dial is its readable facing, not a shield that blocks ranged attacks.

**Station and arena.** Enters from the southern Meridian Gate onto the Spire's shared ivory/cobalt disk. Remains near the central dial, with at least 3 m of traversal space around its body. Cardinal bridges are sealed outside the combat boundary. No cover, floor elevation or static hazard; clockwise and counterclockwise flank routes remain open.

**Phase 1 — Measured Duty.** Tracks slowly, prefers 2–5 m range, and advances toward distant targets between abilities. Does not turn during committed swings.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee counter / ranged counter; punish |
|---|---|---|---|
| Hand Sweep | Clock hand lifts horizontally; two ticks and a coral 100° cone, reach 4 m | 1.4/0.25/2.0; 5; M | Circle to the unmarked flank / stay beyond reach while changing angle; 2 s flank or cast opening |
| Hollow Dial | Core contracts, low toll; annulus 2.5–5 m from body, inner region safe | 1.8/0.3/2.6; 9; M | Stay close / step to nearest inner or outer boundary, never dash through an active band; 2.6 s stationary opening |
| Minute Pin | Blade points at target with a rising tick; 1.5 m wide line, reach 10 m, locked for final 0.9 s | 1.5/0.2/2.8; 7; H | Sidestep toward its shoulder / move perpendicular after lock; 2.8 s blade-extraction opening |

**Phase 2 — The Slipped Minute.** Dial splits into offset concentric plates, showing its gold escapement. The same single blade unfolds at the elbow. No floor damage in the transition. Alternates offset swing sequences with narrow thrusts; cannot rotate freely between beats.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Hand Sweep → Offset Sweep | First cone as before; second 80° cone offset 70°, marked from the first warning and numbered II; fresh 1.2 s warning after first clears, opposite tick pitch | 1.4 then 1.2/0.25 each/3.2; 6; M per beat | Move to the visible common safe flank / reposition across the cleared first cone; 3.2 s after second stroke |
| Hollow Dial → Unwound Dial | First ring unchanged; after it clears, an outlined 2 m inner burst warns for 1.4 s with inward-pointing chevrons | 1.8 then 1.4/0.3 each/3.0; 10; M per beat | Stay at outer edge of initial inner refuge, then leave it / use outer safety and move inward after second resolves; 3 s |
| NEW: Stolen Quarter | Quarter-face opens, loud ratchet; 60° sector to 7 m, aiming locks 1 s early, then hand jams | 1.8/0.35/3.6; 12; H | Step behind adjacent shoulder / cross nearest sector edge; 3.6 s exposed escapement |

**Difficulty and examples.** Melee must abandon the comfortable inner ring before the second burst; ranged must read pins and sector locks rather than orbit at maximum distance. Rear camping prompts a visibly telegraphed reorientation between attacks, not an instant rear hit. Its weakness is committed facing. P1: Pin → sidestep and punish → Hollow Dial → choose nearest safe radius and punish. P2: Offset Sweep → wait for beat II and punish → Unwound Dial → move outward after ring clears → 3 s damage window. Attacks remain separated by their listed recoveries.

**Reward proposal:** Missing-Minute dial medallion, a collection/lore trophy with clock-hand weapon cosmetic potential. No stat or progression system assumed.

## 2. Pendulum Sentinel — Counterweight of the Heights

**Identity and appearance.** Existing tall pillar guardian balances the Spire's suspended bridges. Proposed 4.6 m tower on two narrow articulated legs, ivory arch, cobalt braces and one suspended brass pendulum; tiny counterweight arms. Its windups shift the entire tower, readable from above.

**Station and arena.** Stages beside the western Counterweight Bridge, fights on the same flat Spire disk. Charge endpoints stop at least 3 m before the wall; do not intersect bridge gates. No cover or elevation. Boss travel opens side routes instead of pinning players against the rim.

**Phase 1 — Balanced Weight.** Holds midrange, faces the selected target, then commits to straight travel. Walks back toward the middle between patterns rather than endlessly chasing one ranged player.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Pendulum Charge | Body leans; chain tightens and bell clicks; 2 m lane up to 9 m, locks 1 s early | 1.8/0.65/3.2; 8; H | Leave lane sideways and follow behind / sidestep, then approach stopped body; 3.2 s, endpoint chosen within 5 m of the bait target when possible |
| Low Pendulum | Pendulum lowers with deep scrape; straight low bar extends 4 m to one side, sweeps a marked 90° sector | 1.6/0.45/2.4; 6; M | Step to opposite side; optional authored jump / leave sector or hold outside 4 m; 2.4 s |
| Anchor Drop | Tower rises on feet; downward arrows and heavy chain clunk; 2 m radial ground impact | 1.5/0.25/2.8; 7; M | Step out then return / keep clear and cast; 2.8 s, grounded and stationary |

**Phase 2 — Unbalanced Return.** Side panels unfold into balance fins and the pendulum extends lower. Charge paths become two-part choices; no new arena obstruction.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Charge → Return Stroke | After first charge ends, original lane gains reverse arrows, two descending bells; 1.6 s warning before return, no retargeting | 1.8 then 1.6/0.65 each/3.8; 10; H per leg | Stay alongside lane, cross behind only after first body passes / leave lane and use second endpoint; 3.8 s final recovery, intermediate pause is not full punish |
| Low Pendulum → Counter-Swing | First sector as before, second on opposite side marked II, fresh 1.4 s warning after first clears | 1.6 then 1.4/0.45 each/3.0; 8; M per beat | Cross to cleared first side / stay outside or cross after clearance; 3 s |
| NEW: Counterweight Cast | Weight swings forward and lands in a 2 m circle within 7 m; ringing chain, latched circle for final 1 s; connecting chain is decorative | 1.8/0.3/3.3; 11; H | Leave circle and attack body / step aside and use retrieval animation to cast; 3.3 s |

**Difficulty and examples.** Melee must follow the eventual stop, not sprint after the outbound charge. Ranged must release a planted cast when the lane locks; the return discourages habitual backpedaling. Finite stops and long final recoveries are the weakness. P1: Charge → move sideways and follow into punish → Anchor Drop → leave/reenter. P2: Return Stroke → hold beside lane through both passes → 3.8 s punish → Counter-Swing → cross to cleared side → punish. Bar jumps improve uptime but are never mandatory.

**Reward proposal:** Counterweight chain trophy and pendulum-themed dash-trail cosmetic concept; no movement-stat reward needed.

## 3. Chimebound Oracle — Auditor of Five Notes

**Identity and appearance.** Existing floating sphere with exactly five orbiting bells audits the Spire's harmonic schedule. 2.2 m sphere plus bell orbit, bottom always within normal melee reach. Cobalt sphere, ivory eye-mask, brass bells with raised I–V glyphs and a gold slit core. No invisible flying immunity.

**Station and arena.** Appears before the northern Five-Note Belfry. Same clear Spire disk. Hovers around center within a 3 m leash; glyph sectors are temporary telegraphs. No cover or elevation; projected glyphs never require interaction with distant scenery.

**Phase 1 — Ordered Choir.** Uses a stationary casting posture, drifts only between attacks and faces the next sounding bell. Players read order and placement rather than chase a teleporting caster.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Shattered Chime | Bell I glows coral, short three-note cue; targeted radius-1.5 m circles, lock 1.1 s early | 1.9/0.25/2.7; 7; M | Place marker beside boss then move around body / place at current lane, then sidestep; 2.7 s |
| Dividing Chime | One bell swings ahead of sphere, bright wedge and rising note; 70° cone to 7 m | 1.6/0.3/2.8; 6; M | Walk to a flank / cross nearest cone edge, never run straight away inside it; 2.8 s |
| Silent Fifth | Five equal sectors within 6 m; four bells rise with I–IV, V stays low with a hollow-circle glyph; four sectors pulse once, V is safe | 2.4/0.25/3.4; 11; H | Move to V near boss / use V or outside 6 m; 3.4 s. Reject cast unless every player's shortest collision-safe route fits warning budget |

**Phase 2 — Broken Order.** Sphere opens like an iris; the same five bells separate into stepped heights. Bells still show their actual sounding order; nothing requires audio-only memory.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Shattered → Afterchime | Original circles resolve and disappear; their old positions show hollow outlines, then a fresh 1.5 s warning and lower echo note before one repeat | 1.9 then 1.5/0.25 each/3.2; 9; M per beat | Orbit without returning to old mark / use a new casting lane; 3.2 s after echo |
| Silent Fifth → Passing Silence | First pulse unchanged; then the adjacent sector becomes the clearly numbered safe sector with a 2 s fresh warning; old pulse gone | 2.4 then 2.0/0.25 each/3.8; 12; H per beat | Step across adjacent boundary near body / reposition in outer space or follow safe sector; 3.8 s |
| NEW: Unison Needle | All bells point toward one target, converging overtone; 1.5 m wide beam to 10 m, final 1 s locked | 1.8/0.25/3.0; 8; H | Step toward flank / sidestep beam and cast; 3 s |

**Difficulty and examples.** Melee reads which bell stays silent while preserving core access; ranged loses safe stationary camping to circles and needles, but can choose an outside route during sector puzzles. Oracle's weakness is a fixed reachable body through long performances. P1: Shattered → leave mark and punish → Silent Fifth → follow V and punish. P2: Afterchime → stay off old footprints through repeat → punish → Passing Silence → cross one adjacent boundary → 3.8 s punish. Do not combine circles with the sector mechanic in this version.

**Reward proposal:** Fifth Note bell charm and five-note victory sound; optional collection/cosmetic direction only.

## 4. Rivet Cantor — Tuner of the Great Cast

**Identity and appearance.** A workshop tuning instrument tests every cast component by striking it to pitch. Proposed 3.2 m broad anvil-like torso, charcoal enamel, aged brass soundboard, ivory recessed face, two sturdy legs, one ceremonial tuning mallet and one flat tuning-fork clamp. Smaller and more compact than Crucivar; no furnace torso or gigantic crucible hammer.

**Station and arena.** Southern Tuning Gantry at the Foundry's shared charcoal disk. Exterior chains and furnaces provide context without cover or floor hazards. Holds the middle, leaving both flanks accessible; no height advantage.

**Phase 1 — Tempered Measure.** Walks into 2–4 m range. Its different mallet postures identify short and held beats; timing difficulty comes from reading them, not random identical windups.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Rivet Beat | Mallet at shoulder, sharp ping; 90° cone reach 3.5 m | 1.2/0.2/2.0; 4; M | Step to flank / remain outside reach while lining up cast; 2 s |
| Held Note | Mallet straight overhead, visible three-tooth countdown, rising metallic hum; radius-2 m impact centered 2 m ahead | 2.0/0.25/3.2; 8; H | Wait for lock then sidestep, attack from side / leave marked disk and cast during stuck-mallet recovery; 3.2 s |
| Tuning Fork | Clamp points and sings; 1.4 m wide sound bolt lane to 10 m, lock 0.9 s early | 1.6/0.35/2.6; 7; M | Circle to arm side / sidestep rather than run down lane; 2.6 s |

**Phase 2 — Split Harmonic.** Soundboard plates flare and mallet face separates into two connected resonators. The off-beat is visually shown on the second mallet face; never hide it in VFX.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Rivet Beat → Alternating Rivets | Two opposite 75° cones, numbered I/II; second warns for 1.3 s after first clears, high then low ping | 1.2 then 1.3/0.2 each/3.0; 6; M per beat | Hold unmarked rear or cross into cleared side / avoid both telegraphed arcs; 3 s |
| Held Note → Split Note | First disk unchanged; after impact a fork-shaped outline marks two parallel 1.2 m wide, 6 m long lanes, separated by 2 m clear corridor, 1.5 s warning | 2.0 then 1.5/0.25 each/3.6; 10; H first, M lanes | Step beside disk then into indicated central gap / use gap or outer flank; 3.6 s after resonance |
| NEW: Rest Stroke | Clamp opens with hollow clang; 2 m radial pulse around body, then both arms hang inert | 1.6/0.25/3.5; 11; M | Briefly disengage and reenter / hold safe distance and cast; 3.5 s |

**Difficulty and examples.** Melee must distinguish the shoulder beat from overhead hold; ranged must react to sound-lane locks during rotation setup. No pattern reacts to a player's dodge button. P1: Held Note → sidestep late lock → long punish → Tuning Fork → sidestep. P2: Alternating Rivets → finish both beats → punish → Split Note → leave disk, use central gap → 3.6 s punish. The boss's audible rests and stuck mallet are dependable weaknesses.

**Reward proposal:** Perfect-Pitch rivet trophy and tuning-fork weapon ornament; no mandatory stat gain.

## 5. Crucible Skater — Keeper of the Cooling Ring

**Identity and appearance.** A small transport guardian once carried measured brass between the Foundry's casting stations. A low 2.4 m tall circular bowl on exactly three articulated skate legs, frontal ivory mask and gold reservoir slit. Two short integrated pouring spouts; no human arms. The three-point silhouette contrasts with both Cantor and Crucivar.

**Station and arena.** Eastern Cooling Station on the shared disk. External channels remain decoration. Temporary casts are bounded to one side and expire; rim stays walkable. No pillars, cover or elevation.

**Phase 1 — Measured Pour.** Slides to medium distance between attacks, then plants all three skates before pouring. Cannot attack while freely circling the player.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Casting Run | Skates align and scrape; straight 2 m lane, 7 m travel, final 1 s locked. Entire lane warned before motion; trail lasts only through active time | 1.8/1.2/3.5; 9; M once per event | Move alongside lane, follow cooled trail to stop / sidestep then close toward endpoint; 3.5 s after whole trail cools |
| Measured Spill | Spout tips with pouring chime; one targeted radius-1.8 m pool per marked recipient, final 1 s locked | 2.0/1.5/3.0; 8; M once per pool volley | Bait at flank then use opposite flank / relocate out of marked pool; 3 s after pools expire |
| Rim Kick | Front skate lifts and clicks; forward 100° sector to 3.5 m | 1.4/0.25/2.5; 5; M | Move behind adjacent side / leave sector or stay out; 2.5 s |

**Phase 2 — Open Casting Ring.** Bowl rim rises exposing segmented rotating pour ring; skates spread into broad fins. Casting shifts direction after an explicit pause, not continuous homing.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Casting Run → Elbow Cast | First run as before; after trail cools, a perpendicular 5 m lane warns 1.6 s with bent arrow, then second run | 1.8 then 1.6/1.2 each/4.0; 11; M per leg | Move to inner elbow only after first cools, approach final stop / sidestep second lane; 4 s |
| Measured Spill → Paired Pour | First pools as before; once expired, second marks appear at newly sampled positions with 1.8 s warning; no invisible prediction | 2.0 then 1.8/1.5 each/3.5; 10; M per volley | Place second on alternate flank / move to cooled former space; 3.5 s after second expires |
| NEW: Empty the Ladle | Raised ring hums, annulus 2.5–5 m warns with radial tick marks | 1.8/0.3/3.4; 12; H | Use inner safety / choose nearest boundary and use empty-reservoir pause; 3.4 s |

**Difficulty and examples.** Melee must predict the final stop rather than spend stamina chasing; ranged must give up a convenient firing position when spill marks latch. The boss can never leave a permanent trail or skate indefinitely. P1: Spill → bait on one side, wait for cooling → punish → Casting Run → sidestep and follow cool lane. P2: Elbow Cast → use cleared inner elbow, approach final stop → 4 s punish → Empty Ladle → select safe radius. Slow grounded pour/recovery is the core weakness.

**Reward proposal:** Cooled-Brass rosette trophy and harmless molten-to-brass material cosmetic; no damage floor effect granted.

## 6. Escapement Twins — The Two Who Agree

**Identity and appearance.** A paired calibration engine has two conflicting definitions of the correct beat. One boss and one health bar: two offset 3.8 m counterweight towers on one squat tracked plinth, opposing ivory faces, brass rocker bridge and central gold ratchet. Both towers and base accept damage into shared HP; no independent adds or mandatory weak-point aiming.

**Station and arena.** Northern Escapement Bench, shared Foundry disk. Base stays near center; combined collision width capped at 3.5 m, leaving full flank circuits. Its two pawls are mounted near the base and visible from gameplay camera. No external switches, cover or elevation.

**Phase 1 — Agreement.** Rotates slowly between attacks, alternates which tower acts. Players can win by normal attacks; manipulating the mechanism provides a generous opening rather than a required damage gate.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Left Verdict | Left face flashes with single knock; 1.5 m lane to 8 m, locks 1 s early; tower's projected strike can cross opposite exposed pawl | 1.9/0.25/2.6; 7; M | Bait across right pawl, sidestep / align from beyond pawl then leave lane; successful pawl hit extends recovery to 4 s |
| Right Rebuttal | Right face raises with double knock; 100° cone to 4 m, fixed facing from warning start | 1.6/0.3/2.7; 6; M | Move around left flank / stay outside or cross edge; 2.7 s |
| Common Measure | Both pawls close visibly, chord rings; annulus 2.5–5.5 m | 2.0/0.3/3.0; 10; H | Hold inner safety / take nearest ring boundary; 3 s |

**Phase 2 — Disagreement.** Towers tilt apart; rocker bridge extends and both pawls become exposed. Body remains one target. The active tower's numbered face determines which direction the next lane comes from.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Left Verdict → Alternating Verdict | Either tower may launch the lane; illuminated I and raised pawl identify sender. Same one-lane geometry, no simultaneous crossfire | 1.9/0.25/2.8; 7; M | Bait toward opposite pawl and leave lane / do same at distance; correct self-strike gives 4 s recovery, limited once per cast |
| Right Rebuttal → Rebuttal Pair | First cone as before; opposite tower previews its separate 80° cone and double knock, fresh 1.5 s warning after clearance | 1.6 then 1.5/0.3 each/3.4; 9; M per beat | Move into cleared first side / cross marked edge after first ends; 3.4 s |
| NEW: Deadbeat | Ratchet stops, both masks dim; two parallel 1.5 m lanes from towers to 8 m with clear 2 m central corridor; triple clock click | 2.0/0.3/3.6; 12; H | Stay in central corridor near base / use central corridor or outside lanes; 3.6 s |

**Difficulty and examples.** Melee must line up a self-strike without circling into the opposite cone; ranged trades effortless distance for an angle that earns the stagger. Base collision must not prevent baiting: lanes originate beyond collision boundary and a pawl extends into each legal firing arc. If geometry cannot support it, move the pawl, not the player through the body. No guaranteed self-strike chain: at most one extended recovery per cast; CD persists. P1: Verdict → bait across pawl and sidestep → 4 s stagger → Common Measure. P2: Alternating Verdict → read sender and bait → punish → Rebuttal Pair → wait for both → 3.4 s punish.

**Reward proposal:** Paired Pawl emblem, a mirrored equipment ornament concept; no required puzzle ability unlocked.

## 7. Briar Metronome — Pruner of Lost Seasons

**Identity and appearance.** A terrace maintenance guardian prunes every growth that falls outside an ancient seasonal rhythm. Slender 3.4 m brass metronome on exactly two root-wrapped legs; ivory wedge mask, moss mantle, violet petals, one clock-hand pruning blade and one counterweight arm. The blade is a functional timekeeping/pruning tool, not a generic scythe monster.

**Station and arena.** Southern Gardener's Arch, Verdant shared sage-stone disk. Moss remains outside low rim. Flat quiet turquoise inlay; no root walls, cover or elevated platforms. Leaning body never hides the ground warning.

**Phase 1 — Patient Pruning.** Stalks within 2–4 m, plants feet for cuts, steps closer between abilities if a player retreats. Inward and outward shapes have different blade poses and arrow directions.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Inward Prune | Hook curls toward chest, dry wooden tick; 100° annular sector 2–4.5 m | 1.6/0.3/2.6; 6; M | Move inside 2 m or behind shoulder / cross side edge or stay outside; 2.6 s |
| Outward Prune | Blade folds across chest, sharp chime; solid 90° cone to 3 m | 1.4/0.25/2.5; 5; M | Leave cone sideways / hold outside, change angle; 2.5 s |
| Seedline Thrust | Blade points and root cord tightens; 1.4 m line to 9 m, locked final 1 s | 1.8/0.3/3.0; 8; H | Sidestep to counterweight side / leave line perpendicular; 3 s |

**Phase 2 — Unseasonal Growth.** Chest shutters unfold like fern fronds, pendulum counterweight extends sideways and blade rotates into a more obvious hook. Roots stay attached to the boss, not permanent floor blockers.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Inward → Returning Prune | Initial annular sector unchanged; cleared blade retracts into a 75° inner cone to 2.5 m, fresh 1.4 s warning and inward arrows | 1.6 then 1.4/0.3 each/3.4; 8; M per beat | Use inner refuge then move sideways before retraction / stay outer or cross cleared flank; 3.4 s |
| Outward → Overgrown Reach | Initial cone unchanged; next outward annular sector 3–5 m warns 1.5 s with expanding chevrons, previous cone gone | 1.4 then 1.5/0.25 each/3.2; 8; M per beat | Sidestep first then remain inside cleared reach / step inward or beyond expanding edge; 3.2 s |
| NEW: Winter Rest | Fronds close with brittle crackle; targeted 2 m circle within 8 m, lock 1 s early, single fallen-petal pulse | 1.8/0.3/3.5; 11; H | Bait beside body and circle away / leave marked circle; 3.5 s while counterweight rewinds |

**Difficulty and examples.** Melee must read whether the hook protects or threatens the inner space; ranged must keep changing firing lanes because of thrusts and petal marks. No adaptive punishment of a specific defensive input. P1: Inward Prune → take inner refuge and punish → Outward Prune → move to flank. P2: Returning Prune → inner then side → punish → Overgrown Reach → avoid first cone, return inside expanded cut → punish. Its planted root feet and fixed facing are the weakness.

**Reward proposal:** Lost-Season pruning charm and violet-petal cosmetic accent, never a required healing source.

## 8. Silt Carillon — Warden of the Water Steps

**Identity and appearance.** An aqueduct regulator still measures the flow of vanished water with three heavy chimes. Squat moss-covered stone chest, 2.7 m tall, on exactly four short brass-root legs. Three thick bells hang under its frontal arch; ivory face above them; rounded aqueduct-shell back. It is quadrupedal and compact, clearly distinct from Sylvara's tall humanoid root form.

**Station and arena.** Western Spillway Belfry, same Verdant disk. Actual waterfalls remain outside the arena and never wash players off the rim. Hazards are coral resonance projections on the dry floor, not turquoise water blended into decoration. No cover or elevation.

**Phase 1 — Clear Channels.** Walks a short arc toward center between patterns and plants all four feet to conduct. Prefers 3–6 m and does not retreat indefinitely from melee.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Spillway Note | Left bell tilts, hollow drip-chime; 2 m wide line to 9 m, locks 1 s early | 1.8/0.35/2.8; 6; M | Step around front corner / cross lane sideways; 2.8 s |
| Silt Steps | Three parallel 1.5 m wide, 7 m long lanes 2 m apart, numbered I–III; resolve sequentially with separate bell notes, each clears before next | 1.8 first, 1.4 each later/0.25 each/3.6; 11; M per lane | Move into cleared lane I toward body / cross behind finished lanes, not ahead of III; 3.6 s after III |
| Bellback | Shell rises, grinding stone chord; 110° rear cone to 3.5 m | 1.5/0.3/2.5; 6; M | Leave rear for flank / move outside rear wedge; 2.5 s. Must deliberately face away and show rear windup; never instant anti-backstab |

**Phase 2 — Uneven Flow.** Back arch lifts, bells descend to staggered lengths, gold regulator core becomes visible and small ferns unfold. Deliberately different lane order is always numbered on the floor.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Spillway Note → Split Spillway | Two parallel 1.5 m lanes to 9 m with a 2 m clear central corridor, twin drip-chime, lock 1 s early | 1.9/0.35/3.0; 7; M, combined event once | Use middle gap or flank / use marked corridor; 3 s |
| Silt Steps → Broken Steps | Same lanes but visible order I–III–II; sequence glyphs appear at start and current next lane pulses; fresh 1.5 s warnings | 1.8 first, 1.5 later/0.25 each/3.8; 12; M per lane | Use cleared first lane and wait for actual order / avoid habitual left-to-right movement; 3.8 s |
| NEW: Basin Toll | All bells descend, deep bowl tone; annulus 2.5–5 m | 1.9/0.3/3.4; 10; H | Take inner refuge / choose nearest edge, approach on recovery; 3.4 s |

**Difficulty and examples.** Melee follows cleared channels to retain uptime; ranged must read the changed order instead of holding the far side forever. Lane order requires observation, not hidden memory. P1: Silt Steps → follow lane I after it clears → 3.6 s punish → Bellback → leave rear. P2: Broken Steps → wait in cleared I while III then II resolve → 3.8 s punish → Basin Toll → take nearest radial refuge. The slow grounded conducting posture is the weakness.

**Reward proposal:** Clear-Water chime trophy with a water-tone victory sound, no stamina or healing dependency.

## 9. Vesper Seedkeeper — Custodian of the Last Bloom

**Identity and appearance.** A twilight seed archive continues planting harmonic patterns on a terrace with nothing left to cultivate. Floating 2.5 m ivory lotus vessel with aged brass rings, carved front face, gold core and three tethered seed-lanterns. Low hovering body remains melee reachable. Phase changes never add independent attackable minions.

**Station and arena.** Northern Vesper Arbor, shared Verdant disk. Lanterns are boss parts; their projections do not add collision. No new foliage, cover or elevated safe perches on the playable floor. Boss drifts within 3 m of center between casts.

**Phase 1 — Closed Archive.** Marks a placement, waits for commitment, then retracts. Players control where the danger settles; it never predicts a future dodge destination.

| Ability | Telegraph and geometry | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Planting Mark | One lantern nods, seed rattle; radius-1.6 m targeted disk, freezes 1.2 s before impact and shows root-pin glyph | 2.0/1.2/3.0; 8; M once per volley | Bait on one flank and circle away / place away from intended next cast position; 3 s after roots retract |
| Petal Fan | Closed petals point forward, breathy bell; 90° cone to 5 m | 1.6/0.3/2.7; 6; M | Move beside vessel / cross nearest cone edge; 2.7 s |
| Gathering Thread | One tether straightens, bright dashed aim line becomes solid; 1.4 m line to 9 m, final 1 s locked | 1.8/0.3/3.1; 8; H | Step around body / sidestep and cast; 3.1 s. Thread deals damage only on marked line, no pull |

**Phase 2 — Open Archive.** Lotus opens into exactly six broad petals and three lantern cords extend. Core is visible, but all body hits still count; this is not a weak-point damage gate.

| Ability | Evolution/new behavior and tell | W/A/R; CD; effect | Melee / ranged response; punish |
|---|---|---|---|
| Planting Mark → Bloom and Rest | Initial disk as before. After roots retract, same center shows a 1.6–3.2 m annulus with hollow center and fresh 1.8 s warning, then brief bloom | 2.0 then 1.8/1.2 then 0.3/3.6; 10; M first, H ring | Leave original disk, then use its cleared center or stay outside bloom / choose nearer ring boundary; 3.6 s. Reject multi-mark geometry without a common route |
| Petal Fan → Alternating Petals | First cone unchanged; second adjacent 70° cone previews II and warns 1.5 s after first clears | 1.6 then 1.5/0.3 each/3.2; 8; M per beat | Stay behind or step into cleared first fan / cross to cleared lane; 3.2 s |
| NEW: Vesper Closure | Six petals fold with descending six-note chord; radius-2.2 m disk around vessel | 1.7/0.3/3.8; 12; H | Step outside then return / hold outside and cast; 3.8 s while petals reopen |

**Difficulty and examples.** Melee must place seeds where they do not block the return to the core; ranged must budget a relocation between casts. The puzzle is knowing when the formerly dangerous center becomes safe, always signaled by disappearing roots and a hollow-center glyph. P1: Planting Mark → bait on flank and leave → roots retract → punish → Thread → sidestep. P2: Bloom and Rest → leave disk, return only after roots clear, hold hollow center → punish → Vesper Closure → leave close radius and reenter. Boss immobility through seed cycles is the weakness.

**Reward proposal:** Vesper seed reliquary and six-petal cloak clasp cosmetic; no new required combat ability.

## Balance comparison and first tuning pass

These are expected tendencies, not measured results. Hard melee/ranged parity means similar decision pressure and feasible opportunities, not identical time on target.

| Boss | Melee advantage / challenge | Ranged advantage / challenge | Likely exploit and prevention | First tuning priority |
|---|---|---|---|---|
| Dial Warden | Inner ring refuge / must leave second inner burst | Early cone safety / narrow pins break stationary casts | Permanent rear camping: slow visible reorientation between casts | Offset-cone common safe flank; approach time after sector |
| Pendulum Sentinel | Close landing punish / chasing wrong endpoint | Early lane view / return crosses preferred lane | Wall pin: stop 3 m before boundary and reject plugged routes | Endpoint distance and real collision during return |
| Chimebound Oracle | Short travel between safe sectors / marker placement near body | Outside-sector refuge / repeated marks and needle | Park outside all puzzles: needle and markers remain legal there | Sector reachability at 4.2 m/s; legible five-bell count |
| Rivet Cantor | Stable flank access / held versus short beat | Safe distance from mallet / tuning lane interrupts casts | Identical-looking timing guesses: distinct overhead and shoulder poses | Readable beat differences, generous final recovery |
| Crucible Skater | Easy access while pouring / final-stop prediction | Can hit moving bowl / repeated repositioning | Endless kite or permanent slag: bounded travel and expiration | Chase cost, all trails cool before reward window |
| Escapement Twins | Easier bait angle near pawls / collision routing | Clear view of sender / must angle for stagger | Infinite self-stagger: once per cast, no CD reset | Verify physical self-strike geometry, shared hitbox not double damage |
| Briar Metronome | Access to safe inner arcs / outward cut punishes greed | Safe outer reach / thrust and petal marks | One universal orbit direction: visibly alternate cut direction | Inner-to-side escape and read from high-oblique camera |
| Silt Carillon | Reach body through cooled first lane / changed order | Good overview / no permanent distant safe lane | Memorize left-to-right: clearly announced I–III–II in P2 | Lane widths, bell order and ability to approach during recovery |
| Vesper Seedkeeper | Stationary target / cannot bury core access in seeds | Easy bait placement / must move between casts | Pool stacking soft-lock: merge/reject placements and expire roots | Disk-to-ring safety in parties and body-to-marker separation |

**Initial feasibility spot checks, not runtime validation:** at 4.2 m/s, a final 1 s target lock minus 0.30 s reaction allows 2.94 m of walking. Exiting the center of a 2 m circle with 0.45 m radius plus 0.25 m clearance needs 2.70 m. A 1.5 m-wide lane requires 1.45 m from center to clear. Leaving a 2.2 m self-centered disk requires at most 2.90 m from its center, less from a valid non-overlapping melee position, and its full 1.7 s warning supports 5.88 m after reaction. These checks do not prove complex sectors, multiple markers or boss collision paths. Sector attacks must be rejected/reoriented or warning extended when the shortest actual escape exceeds the budget.

**Before calling these balanced:** playtest all nine solo with each class, then mixed and duplicate-class parties of 2, 4 and 8. Include empty stamina, nearest wall, overlapping targeted marks, missed rotation setup, low damage, down/disconnect mid-warning and phase threshold during multi-beat attacks. Measure unavoidable hits, safe-route distances, melee approach time, ranged stationary-cast windows, total active damage time and kill-time differences. Compare players with similar class mastery; target median melee/ranged clear times within roughly 15–20% initially, then tune movement/recovery before HP. Verify seals/bells persist through transitions and no encounter assumes Chimesinger healing or Vanguard mitigation. Simulate meaningful mistakes without letting one hit produce an unavoidable multi-hit chain.

**Rewards and progression:** the project defers shops, equipment and progression detail. All nine rewards here are distinctive trophy/cosmetic proposals, not claims of existing inventory support. They can appear in a future collection screen or end-of-run presentation. None changes encounter solvability.

## Art deliverables

Three area sheets accompany this specification, each with three boss rows and both phases, player scale silhouettes and area vignettes. These are visual concepts, not rigged models, final topology, animation assets or accepted production art. The written anatomy and mechanics govern any generated-art discrepancy. Original generation prompts and tool method are saved in prompts.md.
