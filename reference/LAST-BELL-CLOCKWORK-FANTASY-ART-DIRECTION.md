# LAST BELL
## Clockwork Fantasy — Visual Art Direction Bible

**Document type:** Art direction and production reference
**Version:** 1.2 — adopted shared visual style; confirmed 2.5D isometric direction
**Date:** October 20, 2026
**Status:** Shared visual style adopted by Risxhb on October 20, 2026; production assets and unvalidated technical details remain planned
**Project:** *Last Bell*, a 2.5D isometric action raid roguelite
**Intended readers:** Creative director, concept artists, 3D/2D artists, animators, VFX artists, UI/UX designers, technical artists, and Godot developers

> **One-sentence vision:** A radiant, suspended clockwork city where heroic figures battle enormous bell-powered guardians across beautifully crafted mechanical arenas—painterly and whimsical at a glance, precise and dangerous when the bell tolls.

---

## Contents

1. [Purpose, project context, and source of truth](#1-purpose-project-context-and-source-of-truth)
2. [Creative thesis and signature visual identity](#2-creative-thesis-and-signature-visual-identity)
3. [Art pillars and non-negotiables](#3-art-pillars-and-non-negotiables)
4. [Style language and visual references](#4-style-language-and-visual-references)
5. [Color system](#5-color-system)
6. [Lighting, atmosphere, and post-processing](#6-lighting-atmosphere-and-post-processing)
7. [Shapes, silhouettes, proportions, and materials](#7-shapes-silhouettes-proportions-and-materials)
8. [Architecture, environments, and arena design](#8-architecture-environments-and-arena-design)
9. [Character and class art direction](#9-character-and-class-art-direction)
10. [Boss art direction: The Bellkeeper](#10-boss-art-direction-the-bellkeeper)
11. [Combat readability and mechanic telegraphs](#11-combat-readability-and-mechanic-telegraphs)
12. [Combat VFX and animation language](#12-combat-vfx-and-animation-language)
13. [UI, typography, and icons](#13-ui-typography-and-icons)
14. [Camera, framing, and readability budgets](#14-camera-framing-and-readability-budgets)
15. [Sound-to-visual coordination](#15-sound-to-visual-coordination)
16. [Godot implementation approach](#16-godot-implementation-approach)
17. [Asset pipeline and production specifications](#17-asset-pipeline-and-production-specifications)
18. [Vertical-slice art brief](#18-vertical-slice-art-brief)
19. [Quality gates and testing checklist](#19-quality-gates-and-testing-checklist)
20. [Ready-to-use creative briefs and prompts](#20-ready-to-use-creative-briefs-and-prompts)
21. [Decisions still to be made](#21-decisions-still-to-be-made)
22. [Implementation reference and change log](#22-implementation-reference-and-change-log)

---

## 1. Purpose, project context, and source of truth

This document establishes the **adopted shared visual language** for *Last Bell*. Use it when designing characters, weapons, spells/abilities, bosses, environments, animation, VFX, icons and UI. It translates stylized, painterly action-game readability combined with brass-and-bells clockwork fantasy into a consistent design reference for every collaborator.

The owner adopted the style, not every proposed feature or example value in this document. Camera tuning, budgets, tooling and future mechanics remain subject to current project decisions and validation. See [collaborator guidance](../AGENTS.md) and [visual review workflow](../docs/workflow.md#visual-design-review). This repository copy of the owner-supplied reference incorporates the confirmed 2.5D and eight-player direction.

### 1.1 What the game aspires to be

The broader proposed game is a **1–8 player 2.5D isometric action raid roguelite** in which players master boss mechanics while creating varied builds between encounters. Its provisional world is a **suspended clockwork city with bell-driven guardians**. Planned systems include additional classes, encounter types, upgrades, relics, co-op, and repeated/echoed mechanics. **These are not implemented features merely because they appear in this art bible.**

### 1.2 What is actually implemented as of the supplied context

The available local Godot project is a **single-player combat experiment** with **Vanguard versus the Bellkeeper** in a fixed arena. It has movement, Basic/Technique/Signature/Guard, health and charge, cooldown feedback, a seeded alternating **cone** and **donut** hazard schedule, hit/guard/immunity results, victory/defeat, and restart. The visuals are **placeholder shapes and text**. Source inspection, not a runtime playtest, is the basis for this summary.

In particular:

- The prototype runs a **fixed 60-tick-per-second simulation** and currently draws its arena and UI from a `Node2D` presentation script.
- Project metadata describes **Godot 4.7 / GL Compatibility** with a **1280 × 800** viewport; the header names Godot 4.7.2. These are source details, not a confirmed running installation.
- Player position and collision are governed by combat simulation and hazard geometry, **not** by the decorative artwork.
- 2.5D gameplay with modeled 3D visuals and a fixed isometric camera is now the **confirmed target**. The Node2D build is a temporary combat prototype; migration is planned, not implemented. Renderer selection still needs profiling.
- This imported reference originally described an older solo context. The current branch adds a 1–8 actor roster and player-anchor/lock support; the view remains solo. Use [the combat contract](../docs/combat-contract.md) for current APIs and [our implementation plan](../docs/plans/2-5d-isometric-foundation.md) for delivery order.

### 1.3 Priority order when constraints conflict

1. **Accurate gameplay and telegraph geometry**
2. **Player, boss, and safe-zone readability**
3. **Accessibility and input feedback**
4. **Stable performance, especially if co-op is added**
5. **Artistic atmosphere and ornamentation**

**Rule:** If a beautiful gear, bloom effect, or boss animation makes a hazard difficult to read, simplify or suppress it during the mechanic.

---

## 2. Creative thesis and signature visual identity

**Working aesthetic name:** *Gilded Twilight Clockwork Fantasy*.

This is **fantasy first, machinery second**. It is not a soot-black industrial steampunk city and not a realistic Victorian factory. Machines feel ancient, ritualistic, celestial, and carefully crafted. Every gear looks like it serves a musical or astronomical purpose; every guardian seems to operate according to a forgotten ceremonial score.

Imagine an airborne city suspended over violet clouds. Cobalt roofs and ivory towers float beside enormous exposed brass mechanisms. Amber light passes through translucent clock faces. A giant bell rings in the distance, and a precisely drawn gold warning ring cuts across a cool, dark stone platform. Our heroes respond with bright, readable actions that puncture the gloom.

### The emotional blend

| Quality | Target feeling | Visual expression |
|---|---|---|
| Wonder | The world is worth exploring | Floating architecture, distant bells, layered skies |
| Adventure | Approachable, energetic | Stylized proportions, expressive posing, saturated accents |
| Ritual | Boss fights feel ceremonial | Symmetry, concentric rings, pendulums, clock numerals |
| Precision | Mechanics are fair and learnable | Crisp silhouettes, stable top-down spatial cues, clean telegraphs |
| Threat | A single mistake matters | Strong contrast, restrained environmental motion during attacks |
| Hope | A heroic core shines through | Warm gold, ivory highlights, vivid ability color |

### A memorable screenshot should contain

- A **clear player silhouette** in the foreground or lower arena.
- A monumental but **readable clockwork boss** at or near the focal point.
- A **recognizable bell/gear motif** in the arena, scenery, or architecture.
- A cool atmospheric environment punctuated by **warm gold machinery**.
- A combat warning that stands out more clearly than decorative lights.

---

## 3. Art pillars and non-negotiables

### Pillar A — Painterly stylized 3D, not realism

Use simplified geometry, appealing color blocks, deliberate edge treatments, and hand-authored material breakup. Details should feel painted and sculpted, not photogrammetric. Texture detail adds character at close range but should not become noise in the game camera.

### Pillar B — Clockwork as sacred architecture

Use bells, escapements, ring mechanisms, suspended bridges, astronomical discs, harmonics, and impossible mechanical balancing. Avoid generic pipes, smoke stacks, and random gears glued to surfaces.

### Pillar C — Clarity at combat distance

Players must distinguish **player / teammate / boss / hostile region / safe region / decorative environment** within a glance. Readability is verified at the actual target gameplay camera and at reduced viewing sizes.

### Pillar D — Musical rhythm made visible

Animations imply beats, releases, pendulum swings, and ringing resonance. The sequence should be recognizable even with audio muted.

### Pillar E — Deliberate contrast

Warm mechanical metal versus cool midnight stone. Detailed silhouettes against simplified floors. Limited bright accents, not everything glowing at once.

### Non-negotiables

- Telegraph shapes take precedence over ornament.
- The displayed lethal/warning region must agree with authoritative collision geometry.
- Hit feedback must distinguish **failed positioning**, **guarded mechanics**, **immunity**, and **damage taken**.
- Colors must not be the only channel carrying important information.
- The art must work with the **existing compact four-action design**.

---

## 4. Style language and visual references

### 4.1 Broad reference

*Windblown* and *Hades* are the owner-selected creative references. For Last Bell, target energetic movement, strong silhouettes, decisive combat feedback, deliberate isometric composition, painterly color, and readable attacks. These are design goals, not claims about either reference game's engine or asset pipeline. Build original characters, environments, materials, icons, and encounter sequences. Retain the compact four-action kit until playtesting supports a gameplay change.

### 4.2 Originalizing the reference

The distinct *Last Bell* signature is:

- **Bell architecture:** cast-bronze bells, engraved resonators, hollow towers.
- **Mechanical choreography:** planetary gear rings, animated pendulums, ratchet releases.
- **Ceremonial garments:** fabric ribbons, short capes, enamel armor, shaped collars.
- **Skyborne geography:** broken platforms, suspended terraces, violet cloud oceans.
- **Sound made physical:** harmonic rings and resonant light bands, not ordinary fireball effects.

### 4.3 Descriptive style formula

> **60% stylized fantasy adventure + 25% ceremonial clockwork architecture + 15% ethereal celestial magic.**

These percentages are creative shorthand—not budgets, rendering coefficients, or measured requirements.

### 4.4 Avoid

- Grimy brown-on-brown realism.
- Tiny rivets and micro-greebles everywhere.
- Sharp comic-book outlines around every asset.
- Photorealistic metals or mirror-like floors.
- Uncontrolled neon bloom.
- Gear decorations that overlap the player's play space.
- A boss so ornate that its pose and attack direction cannot be recognized.

---

## 5. Color system

**Palette status:** Proposed starting tokens. Test against actual target displays, color-vision simulations, grayscale screenshots, and in-game VFX before locking.

### 5.1 Foundational palette

| Token | Hex | Primary use | Rule |
|---|---|---|---|
| Midnight Cobalt | `#17263F` | Deep stone, shadows, unlit architecture | Visual resting color |
| Slate Blue | `#314C69` | Arena floor, stone bridges | Main playfield base |
| Clockwork Brass | `#C38D46` | Bells, gear rims, trim | Medium-emphasis metal |
| Resonant Gold | `#F6C968` | Hero highlights, sacred machinery | Avoid overusing behind warnings |
| Porcelain Ivory | `#F4E9D1` | Bone, enamel, text highlights | High-contrast detail |
| Twilight Violet | `#655491` | Mist, sky, arcane depth | Ambient/world separation |
| Sky Teal | `#55C9CE` | Cool magic and select class accents | Friendly VFX candidate |
| Ember Coral | `#EB685D` | Boss attack feedback, impact moments | Hostile cue candidate |
| Deep Ink | `#101825` | UI surfaces, darkest silhouette cuts | Legibility anchor |
| Cloud Lavender | `#A8A4D1` | Far-background fog and horizon | Desaturated distance cue |

### 5.2 Distribution guideline

For a typical neutral arena screenshot, use approximately:

- **55% cool, dark base** — stone and shadows.
- **25% moderately saturated environment** — violet, blue, and selective teal.
- **15% warm metal and focal architecture** — brass and gold.
- **5% highest-brightness accents** — boss core, player action, warning emphasis.

This is an art-directing heuristic. It should not override visibility requirements during an attack.

### 5.3 Palette ownership

| Channel | Suggested default | Secondary signal |
|---|---|---|
| Friendly/player | Teal or ivory with class-specific accent | Unique silhouette and movement trails |
| Boss/body | Brass, midnight, porcelain | Scale and pose |
| Pre-impact hazard | Coral/amber warning zone | Hatch/stripe/pulse and explicit boundary |
| Safe-zone guidance | Neutral/cool unobstructed floor | Clear empty area and boundary shape |
| Guarded resolution | Ivory/gold shield flash | Shield icon, blocking impact animation |
| Invulnerability resolution | Desaturated spectral ripple | Ghosted silhouette, immunity icon |
| Direct hit | Coral impact and sharp white contact | HP change, damage shake, hit indicator |

**Accessibility note:** These colors are a proposed visual language, not a rule that friendly = blue and dangerous = red must stand alone. Pattern, outline, movement, and iconography should redundantly encode important states.

---

## 6. Lighting, atmosphere, and post-processing

### 6.1 Lighting hierarchy

1. **Readable main light:** soft, directional, showing character volumes.
2. **Warm practical lights:** bells, lanterns, core chambers, mechanical seams.
3. **Cool environment bounce:** blues and violets to separate metal from background.
4. **Sparse accent lighting:** ability impacts, boss phases, key interactables.

Lighting should convey **sculpted shape**, not physically perfect exposure.

### 6.2 Painterly shadow system

- Prefer **two to three broad tone bands** rather than harsh realistic microshadowing.
- Keep shadows colored: deep blue-violet instead of pure black.
- Use soft contact shadows at character feet to clarify grounding.
- Make floor cast shadows subdued enough that a donut or cone remains legible.

### 6.3 Atmosphere

- Violet haze in distant architecture.
- Slow drifting particles above or beyond the playable plane.
- Cloud layers below floating platforms.
- Low-frequency clockwork motion in the background.

**During active hazards:** lower particle density, quiet decorative flashing, and ensure atmospheric effects never draw over the telegraph boundary.

### 6.4 Post-processing policy

| Effect | Default direction | Risk control |
|---|---|---|
| Bloom | Subtle; focused on golden cores | Clamp or suppress near warnings |
| Color grading | Cool shadows, warm highlights | Preserve hazard hue separation |
| Vignette | Very light or none | Never darken edge safe zones excessively |
| Chromatic aberration | Off in normal gameplay | Avoid making telegraphs fuzzy |
| Depth of field | Off for active play | Do not blur player or floor |
| Camera shake | Short and restrained | Accessibility toggle and limits |
| Motion blur | Off by default | Preserve precision and clarity |

---

## 7. Shapes, silhouettes, proportions, and materials

### 7.1 Three shape families

| Family | Silhouette | Application |
|---|---|---|
| **Heroic wedge** | Strong diagonal shapes, compact triangle | Players, weapons, directional attacks |
| **Ceremonial circle** | Rings, arcs, bells, orreries | Bosses, cores, arenas, major landmarks |
| **Architectural vertical** | Tapered tower, spire, suspended strut | City skyline and scene framing |

Players should read as **nimble wedges**, major bosses as **imposing circles and vertical forms**. This helps recognition even in silhouette-only tests.

### 7.2 Shape rhythm

- Big shapes: readable at low resolution and combat zoom.
- Medium shapes: class and faction identity.
- Small shapes: engraving, seams, buckles, and highlights.

Suggested level of attention: **70/25/5** across big/medium/small detail. Treat it as an art test, not a polygon budget.

### 7.3 Material bible

| Material | Visual treatment | Avoid |
|---|---|---|
| Aged brass | Warm diffuse base, broad bright edge accents, slight green/umber shadow variation | Mirror reflections |
| Polished gold | Reserved for cores, relics, bosses' sacred trim | Using it on every prop |
| Enamel | Cream, cobalt, or teal smooth plates with painted chips | Photo-scanned roughness |
| Dark slate | Broad planar breakup, stylized cracks | Busy floor patterns |
| Fabric | Chunky folds, dynamic tails, visible color grouping | Tiny ornamental prints |
| Energy glass | Soft inner gradients, clear bright rim | Transparent overlap over hazards |
| Resonance magic | Concentric rings, spark motes, oscillating linework | Generic smoke and particle storms |

### 7.4 Ornament rule

Every major ornate element should communicate at least one of: **function, faction, progression, timing, or lore**. For example, the Bellkeeper's chest dial tracks a mechanical cycle; it is not simply a random clock glued to a robot.

---

## 8. Architecture, environments, and arena design

### 8.1 World premise

The city is suspended by giant unseen harmonic mechanisms. Bells act as generators, clocks, warnings, and cultural relics. Machinery is infrastructural and mythic: the whole civilization appears to have been built to keep time, maintain balance, and hold something ancient in place.

### 8.2 Environment families — concepts, not current levels

| Zone | Palette and landmarks | Mood | Combat concern |
|---|---|---|---|
| **The Belfry Crown** | Ivory towers, brass bells, blue roofs | Grand, ceremonial | Open, unobstructed floor |
| **The Pendulum Gardens** | Violet flora, exposed weights, swaying bridges | Beautiful, unstable | Background swing must not mimic attack cue |
| **The Escapement Foundry** | Cobalt steel, molten-gold channels | Energetic, industrial-fantasy | Hot channels must not look like lethal zones unless they are |
| **The Astral Observatory** | Porcelain disks, star maps, aurora sky | Celestial, scholarly | Floor star patterns must remain lower contrast than telegraphs |
| **The Hollow Choir** | Dark bells, cracked resonators, lavender fog | Solemn, dangerous | Maintain boss/player rim separation |

### 8.3 Arena floor rules

The floor is the game's **information canvas**. It may be attractive, but its top priority is allowing players to evaluate geometry in under a second.

- Main tile shapes should be broad with **low-frequency** pattern variation.
- Decorative rings or engravings must look clearly **non-interactive** and never resemble live attacks.
- Use a calmer central arena and concentrate ornate details in a peripheral border.
- Keep the visible ground beneath players unbroken and high-contrast against silhouettes.
- Put tall architecture primarily **outside the collision-playable bounds**.
- If the art depicts holes or gaps, gameplay must confirm whether those are traversable; don't create false affordances.

### 8.4 Anchor encounter concept: The Bellkeeper's Dais

A circular ceremonial platform is mounted into a larger rectangle of dark blue stone. Giant bell supports frame the outer silhouette. A deep brass dial sits beneath the Bellkeeper, subtly marking the cardinal directions. The skyline contains suspended towers and gold-lit machinery. During a warning, the floor's engraved rings dim; the **mechanic telegraph** becomes the dominant graphic.

> **Important implementation boundary:** The current prototype's authoritative arena is a rectangle with origin `(60, 140)` and size `(820, 600)`, not a circular collider. If concept art shows a circular dais, it must sit **within** or **around** that rectangle without visually suggesting an incorrect playable boundary.

---

## 9. Character and class art direction

### 9.1 Shared character language

- Exaggerated, compact proportions that survive top-down perspective.
- Distinct shoulder/head/weapon silhouette for each class.
- Oversized, simple weapon shapes; fewer tiny surface details.
- Cloth and metallic trims that echo bell ropes, clock hands, and pendulum arcs.
- Hero highlights focused near **head, shoulders, and weapon**, not beneath feet where telegraphs live.
- Animation posing that communicates facing and action windup at camera distance.

### 9.2 Vanguard — existing prototype character, proposed visual redesign

**Gameplay fact:** Vanguard currently has Basic, Technique, Signature, and Guard; the current build uses placeholder art.
**Visual proposal:** A stout, heroic sentinel with cobalt enamel armor, a semicircular brass guard plate, ivory under-cloth, and a narrow glowing bell-shaped emblem.

- **Silhouette:** Broad shoulders, tapered lower torso, left-facing shield mass, right-handed weapon.
- **Color emphasis:** Cobalt + ivory + brass.
- **Material emphasis:** Enamel and brushed metal.
- **Movement:** Deliberate, weighty footsteps, readable acceleration, small cloth follow-through.
- **Guard cue:** A clean faceted harmonic shield, held briefly around the character's center.
- **Basic cue:** Quick gold-edged slash.
- **Technique cue:** Stronger directional flash with compressed energy.
- **Signature cue:** A large, unmistakable golden clock-hand arc.

*These effect appearances are proposals; they do not change existing damage, range, or cooldown rules.*

### 9.3 Arcanist — proposed class, not implemented

**Visual concept:** A mobile keeper of celestial instruments using segmented floating lenses and violet/teal spectral geometry.

- **Silhouette:** Tall collar, narrow robe tails, floating circular device behind one shoulder.
- **Accent:** Twilight violet and sky teal.
- **Attack grammar:** Intersecting lines, star-etched discs, focused beams.
- **Distinguishing feature:** Straight-line geometry rather than Vanguard's wedge-shaped metallic arcs.

### 9.4 Conductor — proposed class, not implemented

**Visual concept:** A support-oriented bell musician whose gestures orchestrate resonant energy. Its final class role and mechanics remain design decisions.

- **Silhouette:** Long split-tail mantle, baton or tuning-fork staff, ring-shaped ornament.
- **Accent:** Ivory and warm resonant gold, with controlled teal.
- **Effect grammar:** Pulses, concentric soft rings, hovering chimes.
- **Clarity rule:** Beneficial rings must differ strongly from hostile donut telegraphs in **line style, fill, timing, and iconography**, not just color.

### 9.5 Co-op readability proposal

If multiplayer is implemented, assign each player a persistent **outline or shape tag**, optional nameplate, and low-profile ground indicator. Character VFX should gracefully reduce in opacity or particle count when up to eight players overlap. The target is team cooperation, not a wall of personal effects.

---

## 10. Boss art direction: The Bellkeeper

### 10.1 Design statement

The Bellkeeper is a **living cathedral bell in the body of a mechanical warden**. It feels like an ancient civic guardian that enforces timing, not a generic armored monster.

### 10.2 Anatomy and silhouette

- **Primary mass:** Towering hooded bell torso with tapered bronze shoulder armor.
- **Head:** Minimal ivory mask or recessed dial face; a tiny, intense focal glow.
- **Core:** Suspended clapper or pendulum visible through the chest cavity.
- **Limbs:** Segmented brass-and-slate arms with heavy forearms that describe attack directions.
- **Back silhouette:** Partial ring mechanism framing the shoulders like a broken halo.
- **Lower body:** Massive, planted mechanical legs or an anchored pedestal-like frame.

**Clarity target:** At combat scale, a viewer should instantly identify **head, chest/core, active arm, and orientation**. Reduce ornaments until those read.

### 10.3 Materials and palette

- Weathered brass as dominant warm material.
- Midnight cobalt structural cavities.
- Porcelain ivory mask and inlays.
- Single resonant-gold core as strongest resting glow.
- Restrained coral/orange surge **only when attacking**.

### 10.4 Idle / windup / resolve / recovery acting

1. **Idle:** Slow gear breathing, measured pendulum, muted core glow.
2. **Windup:** Bell housing rotates toward intended direction; an arm or core aligns; glow tightens.
3. **Warning:** Environment quiets; floor telegraph is fully visible.
4. **Resolve:** Decisive clap/strike with one short radiating shock and clear contact timing.
5. **Recovery:** Bell oscillation decays; armor relaxes; visual density returns to normal.

Avoid long fiery explosions after resolution that could be confused with the next warning.

### 10.5 Boss attack visual briefs — current mechanics

**Bell Sweep (implemented cone geometry):** The Bellkeeper turns its resonator toward the captured target direction, braces, and projects a wedge of segmented harmonic lines. Use a strong outer radial boundary and a lighter interior hatching pattern. Its direction **locks at hazard spawn** rather than tracking the player throughout the warning.

**Hollow Toll (implemented donut geometry):** The Bellkeeper compresses its core; a broad ring of resonant energy appears between **inner and outer danger radii**, while the inner pocket remains clearly readable as safe. Use two crisp concentric boundaries and distinct interior striping. The safe region is not marked safe merely by a color that can be misread.

**Future phase ideas (not implemented):** Discordant bells, time signatures, harmonic inversions, echoed warnings, and moving pendulum lanes. Add only after the prototype's existing mechanics are consistently readable.

---

## 11. Combat readability and mechanic telegraphs

This section is a **production constraint**, not optional decoration.

### 11.1 Current authoritative behavior

From the supplied `GAME-CONTEXT.md`:

| Mechanic | Current gameplay facts | Art implication |
|---|---|---|
| Bell Sweep | Cone radius **380** units; half-angle **π/5 = 36°**, full angle **72°**; aimed at player position with seeded angular variation when spawned | Draw warning using the simulation's actual origin/direction/half-angle/radius |
| Hollow Toll | Donut with inner radius **155**, outer radius **380** units; centered on boss | Visually distinguish inner safe pocket from dangerous annulus |
| Schedule | Hazards at ticks **1, 211, 421, ...** every **210 ticks = 3.5 s** | Do not build timing from animation guesses |
| Warn time | Resolves **90 ticks = 1.5 s** after warning spawns | Match countdown/pulses to tick-based resolve time |
| Locking | Hazard geometry locks at spawn | Telegraph does **not** continually track players |
| Boundary | Geometry boundaries are dangerous | Outer and inner line treatment must not visually imply safety on the exact line |
| Hit test | **Player center point** is checked | Center marker/feet target must align with meaningful collision center |
| Guard | Active **24 ticks = 0.4 s**, prevented damage if active at resolution | Show success at guarding, but do not portray it as a clean positional solve |
| Immunity | Hit grants **45 ticks = 0.75 s** immunity | Indicate health protection separately from safe positioning |

### 11.2 Telegraph layered design

Treat a dangerous region as three nested design layers:

1. **Geometry layer (mandatory):** exact boundary and fill derived from simulation hazard data.
2. **Identity layer:** line vocabulary (radial slices for cones, concentric notches for donuts), subtle pattern, optional symbol.
3. **Timing layer:** pulse or closing indicator driven by `(resolve_tick - tick) / 60` seconds.

The geometry layer must still communicate danger with the identity and timing layers switched off.

### 11.3 Warning animation example (proposal)

| Relative warning interval | Motion | Player information |
|---|---|---|
| 1.50–1.00 s remaining | Zone appears promptly; boundaries stable | Where danger will happen |
| 1.00–0.50 s remaining | Slow rhythmic edge pulse | Time is advancing |
| 0.50–0.15 s remaining | Modest pulse frequency increase | Urgency |
| 0.15–0.00 s remaining | Boundary reaches sharpest contrast | Precise impending resolve |
| Resolve tick | Very short flash synchronized to simulation | Mechanic resolves **now** |
| After resolution | Warning clears; outcome feedback displayed | What happened and why |

Values above describe *visual pacing* only; gameplay timings remain authoritative.

### 11.4 Outcome feedback grammar

Crucially, in the current simulation a player **inside** a resolving hazard is marked `mechanic_failed: true` even if Guard or post-hit immunity prevents damage. A visual system must reflect that distinction:

| Resolution event | Positioning | Health consequence | Recommended feedback |
|---|---|---|---|
| `avoided` | Successful | No hit | Brief clean edge fade; unobtrusive success tick |
| `guarded` | Failed mechanic | Damage blocked | Shield impact + “Guarded” or shield icon; not “Perfect dodge” |
| `immune` | Failed mechanic | Damage prevented by immunity | Soft spectral ripple + “Immune”; not “Avoided” |
| `hit` | Failed mechanic | Health lost | Impact flash, HP change, concise hit indicator |

Preserve the information even when color effects are reduced or audio is muted. Resolution explanations in the current UI concern the **most recent event**, not a full replay.

### 11.5 Friendly effects versus hostile telegraphs

- Friendly circles: thin broken or dotted soft outer edge, little or no filled floor area, distinct ally symbol.
- Hostile donuts: filled annulus with solid readable inner and outer danger borders, rhythmic warning treatment.
- Avoid overlapping friendly circles and hostile warnings of equal contrast.
- Suppress or raise friendly particles away from the ground plane while mechanics are active.

### 11.6 Geometry validation rule

The prototype already shares hazard shape dictionaries between geometry and draw code. Keep that principle: **simulation produces hazard geometry; presentation samples that exact geometry**. Never rebuild angles/radii independently from an artist-authored sprite approximation. Use shader masking or mesh generation only if it can be validated against the same membership logic.

---

## 12. Combat VFX and animation language

### 12.1 Effects taxonomy

| Source | Graphic shape | Motion quality | Budget priority |
|---|---|---|---|
| Vanguard Basic | Tapered slash/wedge | Snappy | Medium |
| Vanguard Technique | Compact heavy impact with directional arc | Compressed, forceful | Medium-high |
| Vanguard Signature | Large clock-hand sweep and brief gold afterimage | Dramatic, then clears | High, short duration |
| Vanguard Guard | Faceted shield shell and bell-rim glint | Immediate, stable active window | High for defensive clarity |
| Boss Bell Sweep | Filled wedge with harmonic ray marks | Clockwise tension to fixed resolve | **Highest** |
| Boss Hollow Toll | Annulus with two solid danger boundaries | Concentric pulse | **Highest** |
| Environmental machinery | Gear turn / pendulum swing | Slow, metronomic | Lowest during mechanics |

### 12.2 Motion principles

- Anticipation: readable pose before high-impact motion, without changing authoritative start time.
- Action: strong directional arcs, fast beginnings, decisive contact frame.
- Follow-through: limited to upper body and decorative trails; **never obscure next telegraph**.
- Rhythm: release events align with the simulation tick, not a free-running animation timer.
- Scale: large boss telegraphs are designed for game-camera framing, not cinematic close-ups.

### 12.3 VFX layering proposal

From back to front:

1. World background and sky.
2. Arena floor and static decals.
3. **Ground telegraph fill**.
4. **Ground telegraph boundaries/patterns**.
5. Character grounding/shadows.
6. Player and boss readable bodies.
7. Weapon/ability effects (limited).
8. Damage/guard/immunity and other critical feedback.
9. HUD and textual indicators.

Actual rendering may require adjusted draw ordering, but telegraph lines should stay unobscured at player decision points.

### 12.4 Screen-space caps — initial QA targets, not engine settings

- Active telegraph contours remain crisp at native resolution and a 50%-size screenshot.
- Nonessential particles should not dominate the main character outline.
- Boss resolution flash should clear quickly enough to perceive the next warning.
- Players may turn off screen shake, heavy particles, and intense bloom without losing mechanic information.

---

## 13. UI, typography, and icons

### 13.1 HUD identity

A restrained **horological instrument panel**: midnight-ink surfaces, thin brass dividers, enamel gauges, minimal ornamental screws, and readable modern text. It should complement the fantasy setting without sacrificing legibility.

### 13.2 HUD priorities

1. Player health, active state, and immediate mechanic danger.
2. Boss health and warning context.
3. Cooldowns and current charge/resource.
4. Latest mechanic-resolution explanation.
5. Background flavor and decoration.

The current prototype already exposes health, charge, cooldowns, and last resolution; these should be **reskinned**, not replaced by new mechanics without a design decision.

### 13.3 Proposed component styling

| Component | Proposed design | Usability requirement |
|---|---|---|
| Player health | Five bold enamel pips in the current solo prototype | Easily count remaining health |
| Boss health | Wide centered gauge with brass frame | Clean contrast against scene |
| Charge | Six-segment resonator dial for current cap of six | Exact filled segments visible |
| Ability slots | Four uniform action tiles with distinct glyphs | Keyboard/controller legend legible |
| Cooldown | Radial mask + numeric time (when appropriate) | No color-only ready state |
| Guard | Shield icon and short active flash | Active duration visibly distinct from cooldown |
| Resolution | Small anchored card: Avoided/Guarded/Immune/Hit | Clearly states mechanics vs damage |
| Victory / defeat | Large ceremonial bell motif, controlled animation | Outcome obvious, restart discoverable |

### 13.4 Typography

- **Display headings:** A legible, moderately expressive serif or inscription-inspired face.
- **Combat numbers and labels:** A highly readable sans-serif with open numerals.
- **Damage text:** Short, bold, and sparse; never cover telegraph edges.
- **Avoid:** overly ornate clock numerals for essential HUD information.

Font licensing and actual font selection remain to be decided.

### 13.5 Iconography grammar

- Action icons: one central glyph, thick silhouette, minimal interior strokes.
- Boss mechanic icons: wedge for sweep, concentric ring for donut.
- Positive/negative state: use distinct symbols, not just a gold/coral tint.
- Stroke weights should remain meaningful when scaled down to HUD tile size.

---

## 14. Camera, framing, and readability budgets

### 14.1 Camera proposal

The confirmed target is a **fixed isometric Camera3D over a fully 3D arena and modeled characters**. Start with orthographic projection, a 45-degree yaw and approximately 35-degree downward elevation as blockout values; tune framing and zoom in playtests. All ground geometry shares one mapping into the XZ plane, so projected circles and cones stay consistent with hit testing. Camera rotation is deferred for the slice.

**Current-source caveat:** The prototype is presently 2D-drawn at a 1280 × 800 project viewport. It does **not** establish a 3D camera, perspective lens, or character-model scale.

### 14.2 Framing hierarchy

- Player avatar is always locatable in under one glance.
- Boss pose and telegraph origin are usually both visible.
- HUD does not obscure critical corners of the arena.
- Tall environmental assets do not occlude the playfield.
- On zoom changes, maintain collision-space clarity and text legibility.

### 14.3 Readability tests

Use a consistent test harness to capture:

1. Idle screenshot with player and boss.
2. Bell Sweep at initial warn and just before resolve.
3. Hollow Toll at initial warn and just before resolve.
4. Guarded failure, immune failure, hit, avoided outcome.
5. Reduced-resolution screenshot and grayscale screenshot.
6. One-, four-, and eight-character overlap fixtures; separate network/human co-op checks when integrated.

Playtesters should be able to explain **which region is dangerous** and **where the avatar's center needs to be** without relying on surrounding narration.

---

## 15. Sound-to-visual coordination

The art and audio share one language: **timing**.

| Audio event | Visual partner | Constraint |
|---|---|---|
| Pre-attack metallic inhale | Core retracts, dial aligns | Telegraph already communicates danger |
| Bell strike | Flash, shock ring, decisive boss pose | Synchronized to authoritative resolve tick |
| Guard contact | Bell-clapper click + shield facets | Distinct from clean avoidance |
| Damage hit | Short metal fracture + coral impact | HP loss visually explicit |
| Mechanic avoided | Quiet resolved chime | Avoid overwhelming encounter mix |
| Victory | Harmonic chord + glowing bell crest | Does not hide end-state UI |

Any rhythm effects must respect gameplay timing and accessibility. The player should not need to hear a bell to perform a mechanic.

---

## 16. Godot implementation approach

**Scope note:** The owner confirmed the 2.5D isometric direction. The implementation details below are planned and must be verified against the current branch; they are not implemented render features.

### 16.1 Preserve the existing separation of concerns

The available source map is:

| Existing file | Current responsibility | Suggested visual integration |
|---|---|---|
| `game/core/combat_simulation.gd` | Authoritative deterministic gameplay and snapshots | Keep rendering decisions out of simulation |
| `game/combat/hazard_geometry.gd` | Cone/donut membership and rendering geometry helpers | Continue using one geometry source for draw/collision |
| `game/ui/combat_view.gd` | Input, fixed-step timing, arena/HUD drawing | Gradually layer art-driven presentation and feedback |
| `game/main.tscn` | Current root scene | Compose appropriate art nodes after technical review |
| `game/project.godot` | Project config, rendering settings | Test shaders against project's GL Compatibility choice |

### 16.2 Selected track: 2.5D presentation with modeled 3D visuals

- Compose a Node3D arena, fixed isometric Camera3D, modeled characters, stylized materials, and a separate CanvasLayer HUD.
- Keep authoritative combat on its existing Vector2 plane; use one documented adapter into world XZ. Cosmetic height is not gameplay elevation.
- Map actor feet, boss center, arena bounds and every telegraph through the same adapter. Camera-relative input is converted back to simulation movement before commands are submitted.
- Generate floor meshes from shared hazard geometry, including a true donut hole; use explicit depth/layer rules to prevent z-fighting and occlusion.
- Start with the pinned editor and current GL Compatibility renderer. Profile a representative scene before proposing a renderer change.
- Retain the Node2D view only as a temporary regression/debug fixture during conversion. The selected 2.5D approach uses modeled 3D visuals on the existing planar combat simulation; a sprite-only or pre-rendered-character pipeline is not assumed.

See [the foundation implementation plan](../docs/plans/2-5d-isometric-foundation.md) for ownership, sequence, acceptance gates and integration dependencies.

### 16.3 Visual state must derive from fixed-step state

Do not independently animate attack warnings using a generic wall-clock timer. Suggested presentation contract:

```text
simulation snapshot / hazard dictionary
             |
             +--> hazard kind and unique ID
             +--> origin, direction, radius / inner radius / outer radius
             +--> warn_tick, resolve_tick, current tick
             |
             v
      visual hazard adapter
             |
             +--> exact filled danger shape
             +--> exact boundary draw
             +--> remaining-time pulse
             +--> accessibility alternate pattern
```

Interpolate decorative movement as appropriate, but render a hazard's **dangerous footprint** from the current authoritative geometry.

### 16.4 Texture and shader suggestions

- Use hand-painted base color with broad warm/cool breakup.
- Use simple stylized lighting and broad painted material breakup; profile any custom stepped-diffuse shader.
- Use rim lighting selectively so characters separate from slate floors.
- Use shader parameters for telegraph opacity and pulse, but **do not change the shape mask to alter gameplay geometry**.
- In 3D, separate floor, telegraphs, feet markers, actors and effects using explicit depth and visibility rules.
- Test GL Compatibility before relying on features requiring another render method.

### 16.5 Deterministic visual events

The simulation logs hazard outcomes but does not document a comprehensive attack/VFX event stream. If detailed animation syncing becomes necessary, propose a **small event adapter** or presentation event list and validate it against `CombatSimulation.step(...)`. Avoid embedding gameplay rules into particles, animation trees, or boss sprite frames.

### 16.6 Source verification before implementation

The supplied context dates to **October 20, 2026**. Before changing code, inspect actual relevant scripts to confirm current signatures and behavior; re-check the current branch and pending foundation PRs. This document does not imply access to the full private repository or a live Godot runtime.

---

## 17. Asset pipeline and production specifications

### 17.1 Recommended folder organization (proposal)

```text
art/
  direction/
    LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md
    palette.md
  concepts/
    characters/
    bosses/
    arenas/
  source/
    blender/
    painting/
    vfx/
  exports/
    characters/
    bosses/
    environments/
    ui/
    vfx/
  qa/
    readability_captures/
    color_vision_tests/
```

This structure is suggested only; it is **not** claimed to exist in the repository.

### 17.2 Naming conventions

Suggested format: `lb_<category>_<subject>_<variant>_<purpose>_v###`.

Examples:

- `lb_char_vanguard_default_idle_v001`
- `lb_boss_bellkeeper_phase01_mesh_v001`
- `lb_env_belfry_dais_floor_albedo_v001`
- `lb_vfx_bell_sweep_warning_v001`
- `lb_ui_guard_icon_default_v001`

Keep texture types, animation names, and source licenses discoverable in an asset manifest.

### 17.3 Deliverables per hero or boss

- Orthographic silhouette sheet (including gameplay angle).
- Color and material callouts.
- Front / three-quarter / rear exploratory turns as useful.
- Attack anticipation and resolve key poses.
- In-game-size screenshot at actual camera framing.
- Hazard overlap test with effects active.
- Final source file and exported engine asset.

### 17.4 Early budgets

Do not set arbitrary production-wide triangle or texture budgets from concept art alone. First profile a representative arena on intended hardware. Then establish budgets for textures, draw calls, particles, screen overdraw, and animation rigs using measured evidence. Set a higher priority on **predictable telegraph contrast** than on cosmetic detail density.

### 17.5 Review rhythm

Each art asset should receive two separate approvals:

1. **Identity review:** Does it look and feel like *Last Bell*?
2. **Gameplay review:** Does it remain correct, readable, and performant during a boss mechanic?

Passing the identity review never substitutes for gameplay review.

---

## 18. Vertical-slice art brief

### 18.1 Target

Replace placeholders with enough original art to prove the **visual identity and boss-mechanic clarity** in the current Vanguard-versus-Bellkeeper encounter—without expanding gameplay scope prematurely.

### 18.2 Minimal content package

| Asset | Deliverable | Priority |
|---|---|---|
| Vanguard | Single readable character design and animated basic state set | P0 |
| Bellkeeper | Silhouette-correct boss with idle/windup/resolve poses | P0 |
| Arena floor | Low-noise clockwork stone floor and peripheral trim | P0 |
| Bell Sweep | Cone fill + boundary + timer pulse from exact simulation data | P0 |
| Hollow Toll | Donut fill + both boundaries + timer pulse | P0 |
| Outcome feedback | Separate Avoided/Guarded/Immune/Hit feedback | P0 |
| Combat HUD | Reskinned health, charge, abilities, boss health | P1 |
| Background skyline | Distant 3D belfry silhouettes and sky backdrop | P1 |
| Ambient gears | Sparse low-distraction rotation | P2 |
| Intro/victory flourish | Bell-focused visual signature | P2 |

### 18.3 Suggested production order

1. **Blockout:** Confirm floor contrast, silhouettes, bounds, and telegraphs with flat colors.
2. **Identity pass:** Establish chosen brass/blue/violet palette and material style.
3. **Animation pass:** Build boss windup and resolve poses with simulation timing.
4. **Feedback pass:** Distinguish four outcome states and defensive states.
5. **Atmosphere pass:** Add skyline, gentle particles, and ornamental life only after mechanical clarity is approved.
6. **Polish/optimization:** Profile, accessibility-check, and reduce overdraw.

### 18.4 Definition of done for the art slice

The slice is **art-complete for review** when a new player can identify the avatar, boss, cone danger, donut danger, safe area, and post-mechanic outcome without explanation; there are no visible contradictions between painted bounds and simulation geometry; and scene readability survives decorative effects.

**Not required for this milestone:** full co-op, new classes, new bosses, or additional mechanic types. A working 3D scene and isometric camera are required for this art milestone; eight-player integration remains a separate slice gate.

---

## 19. Quality gates and testing checklist

### Gate 1 — Concept coherence

- [ ] Three artists independently describe the style using similar terms.
- [ ] Clockwork elements feel functional, ritualistic, and original.
- [ ] Gold accents are focused rather than everywhere.
- [ ] Player and boss silhouettes remain distinctive without textures.

### Gate 2 — Gameplay readability

- [ ] The danger region is recognizable at initial warning and right before resolution.
- [ ] Bell Sweep orientation is stable after spawn.
- [ ] Hollow Toll inner and outer dangerous boundaries are clear.
- [ ] Visual danger footprint matches the geometry helper.
- [ ] A center-point test near an exact boundary is not visually misleading.
- [ ] Floor ornaments do not resemble live warnings.
- [ ] Guarded and immune outcomes are **not** labeled as clean mechanic avoidance.

### Gate 3 — Accessibility

- [ ] Warning patterns remain distinguishable in grayscale.
- [ ] Text and warning lines remain legible at reduced output size.
- [ ] Non-color signals distinguish helpful from harmful effects.
- [ ] Reduced motion / reduced shake / reduced flash options are scoped.
- [ ] Muted audio does not prevent interpreting mechanic timing.

### Gate 4 — Technical correctness

- [ ] Presentation reads hazard values from authoritative state.
- [ ] Warning duration follows simulation ticks.
- [ ] Restart restores correct visual state without stale animations.
- [ ] Victory/defeat do not leave unresolved visual hazards on screen.
- [ ] Performance is profiled on actual target hardware.
- [ ] Selected shaders are compatible with the project's chosen renderer.

### Gate 5 — Team handoff

- [ ] Each asset has a clearly named owner and purpose.
- [ ] Source and exported files are retained and versioned appropriately.
- [ ] Gameplay screenshots at target resolution accompany approval.
- [ ] Any intentional deviations from this art guide are documented.

---

## 20. Ready-to-use creative briefs and prompts

These prompts are **original concept starting points**. They do not guarantee particular results from an image tool, and any production use should be revised by an artist to preserve style consistency and asset originality.

### 20.1 Key art / hero screenshot prompt

> Stylized painterly 3D fantasy game scene, high oblique top-down action-game camera, a heroic armored sentinel confronting a gigantic cathedral-bell automaton in a suspended clockwork city. Ancient ceremonial brass gears, ivory stone, dark cobalt slate arena, distant violet clouds, skyborne gothic clock towers, warm resonant golden cores, elegant exaggerated silhouettes, hand-authored painted material appearance, restrained toon-like shadows, readable gameplay composition. A precise coral-and-gold cone warning is clearly visible on the ground with crisp edges, strong separation of dangerous floor and character silhouettes. Adventurous, wondrous, cinematic yet visibly playable. Original character and world designs, no lettering, no watermark, no photorealism, no busy ground noise, no oversized explosions.

### 20.2 Bellkeeper boss concept prompt

> Full-body original fantasy boss concept sheet of a monumental clockwork guardian called the Bellkeeper: cast-brass bell-shaped torso, recessed ivory ceremonial mask, exposed swinging pendulum core, segmented cobalt mechanical arms, broken circular halo of gears behind shoulders, narrow resonant gold chest light. Heroically intimidating rather than horror, elegant painterly stylized 3D game art, large clean silhouette and few high-meaning ornaments, front and three-quarter views, material callouts, neutral background, suitable for clear high-angle boss fights. Avoid steampunk smoke, photorealistic rust, excessive tiny gears, and visual clutter.

### 20.3 Arena concept prompt

> Top-down playable boss arena concept for a vibrant clockwork fantasy raid roguelite. Large calm slate-blue rectangular combat floor with a ceremonial circular brass dial integrated into its center, decorative ivory-and-gold belfry architecture outside the gameplay bounds, floating city and violet cloud abyss beyond, clean readable ground textures, restrained ambient machinery, strong player-to-floor contrast. Painterly stylized fantasy, soft directional lighting, clear free space for cone and donut attack telegraphs, no UI, no text, no characters, no ambiguous pits inside the combat floor.

### 20.4 VFX brief: Bell Sweep

> Design a floor-anchored cone warning that expresses a resonant bell shockwave: precise solid radial edges, translucent coral/amber danger fill, subtle segmented harmonic lines, rhythmical edge pulses progressing over 1.5 seconds, restrained impact flare. It must remain readable over dark blue stone, in grayscale, and at reduced screen size. The cone shape is generated from simulation data, not artist-estimated measurements.

### 20.5 UI brief

> Design a compact in-game HUD for a stylized clockwork fantasy action raid roguelite: dark ink and blue enamel panels, thin brass frames, porcelain highlights, modern highly readable labels, five player health pips, six charge segments, four action icons with cooldown readability, boss health bar, small nonintrusive mechanic-outcome feedback card. Ceremonial horological details without heavy skeuomorphic clutter. HUD should leave the combat floor and boss telegraphs unobstructed.

---

## 21. Decisions still to be made

This bible offers defaults, but the following require project-owner approval or playtest evidence:

1. **Renderer and budgets:** full Node3D presentation is confirmed; profile renderer, lighting and performance on agreed hardware.
2. **Final camera:** high oblique angle, zoom range, and projection behavior.
3. **Class lineup and roles:** planned class names exist in design context, but class visuals and final mechanics are open.
4. **Target platforms:** hardware targets and rendering budgets are not established by the supplied files.
5. **Style intensity:** how painterly versus how cel-shaded / geometric.
6. **Character proportions:** chibi-adjacent versus moderately heroic, tested in real combat shots.
7. **Accessibility options:** exact user-facing settings and alternate colors/patterns.
8. **Asset pipeline:** modeled and rigged 3D assets; settle modeling tools, glTF export conventions, scale and animation naming.
9. **Final fonts:** licensing, localization, and readability testing.
10. **World canon:** city naming, factions, chronology, and precise significance of the bells.

### Suggested default decisions for the first art pass

- Start with a **3D blockout and fixed isometric camera**, preserving the existing fixed-step combat core.
- Adopt **Midnight Cobalt / Slate Blue / Clockwork Brass / Resonant Gold / Twilight Violet** as the foundation.
- Keep **the arena floor quiet**; concentrate detail on boss, characters, and scenery outside playable bounds.
- Make **Bell Sweep and Hollow Toll** the art team's core readability test cases.
- Prioritize **one high-quality boss arena** over multiple partially art-directed levels.

---

## 22. Implementation reference and change log

### Source material used

This document is grounded in these user-provided project notes:

- `GAME-CONTEXT.md` — inspected gameplay behavior, hazard rules, architecture, implementation versus proposals; reviewed October 20, 2026.
- `README.md` — explains that the context files are documentation, not live repository/game access.
- `STARTER-PROMPT.md` — requests that source-derived facts be distinguished from planned systems and recommendations.

The owner adopted this document's overall clockwork-fantasy style, palette families, silhouette language and readability priorities as the shared design reference. Example asset briefs and production steps guide implementation; they do not claim matching assets already exist. Numeric budgets, feature ideas and technical proposals still require validation against current decisions and source.

The local reference folder also contains `art style reference.webp`; it has not been assessed or imported in this documentation change. This plan is grounded in the Markdown reference and the owner's direction.

### Change log

| Version | Date | Notes |
|---|---|---|
| 1.2 | 2026-10-20 | Owner adopted the visual style for all collaborators; linked agent instructions, onboarding and visual review workflow |
| 1.1 | 2026-10-20 | Confirmed 2.5D/isometric direction, Windblown and Hades inspirations, eight-player target, and foundation migration plan |
| 1.0 | 2026-10-20 | Initial comprehensive visual-art-direction proposal, aligned with supplied prototype context |

---

## Final art-direction statement

**Last Bell should feel like a playable illuminated clockwork storybook: warm bells and celestial mechanisms suspended in cool twilight, expressive heroes facing sacred mechanical giants, and every dangerous geometry drawn with uncompromising fairness.**

The visual promise is not just beauty. It is **beauty that teaches the fight**.
