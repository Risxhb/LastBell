# Prompt: detail and high-resolution pass on The Throne of the Final Toll (Area 4)

Paste everything below the line into a Claude Code session working in this repository (the Area 4 session or a fresh one). It assumes the first Unity build already exists (commit `ade14c7`, built from `SHATTERED-HOUR-UNITY-PROMPT.md`). This pass upgrades that build; it doesn't start over.

---

## Your task

Go over the entire Area 4 arena, **The Throne of the Final Toll** (The Shattered Hour, final-boss arena), object by object, and raise every object from procedural blockout to finished, high-resolution stylized art. Do this through more geometry where silhouettes need it, crafted bevels, painted texture detail, normal maps and better materials. Keep the layout, the scale and the readability that already work.

The quality target is **8/10 from an independent critic** (see "Critic loop" at the end). The last build scored **6.5/10**: layout 8, readability 8, floor 6.5, rim/stations/entrances 6.5, bell/astrolabe 5.5, void 5.5, lighting/colour 5.

This is a **proposal-level art pass**. It covers environment art only: no boss, no attacks, no gameplay.

## Read first (in this order)

1. `AGENTS.md` (repo root) and `gpt/08-design-rules.md`: settled rules. §9 has the style hard rules.
2. `gpt/02-visual-style-guide.md`: palette tokens, art pillars, material treatments, the "avoid" list. **This is the art source of truth.**
3. `gpt/05-arenas.md` (Area 4 entry) and `gpt/images/10-arena-shattered-hour.png`. That sheet's **Material & Detail Studies** and **Key Landmarks** panels are the main detail target:
   - floor stone with brass inlay
   - a rim station close-up
   - cathedral stone with chains
   - the cracked bell
   - broken arches
   - the southern arrival bridge
4. `unity-session/prototype/arena-3d/areas/area-4/ref-topdown-layout.webp`: the user's plan (layout target).
5. `unity-session/prototype/arena-3d/areas/area-4/SHATTERED-HOUR-UNITY-PROMPT.md`: the original build spec, with dimensions and readability rules. Those still apply.
6. `Reference/LAST-BELL-ARENA-ART-HANDOFF/LAST-BELL-SHATTERED-HOUR-ARENA.md` and its mood image `artwork/a_dramatic_high_detail_fantasy_sci_fi_scene_a_to.png`.
7. The current code:
   - `unity-session/unity/Assets/LastBell/Areas/Area4/Editor/ShatteredHourArenaBuilder.cs` (generates everything)
   - `ShatteredHourArena.cs`
   - `ShatteredHourOccluderFade.cs`
   - `Shaders/*`
   - the last screenshots in `Area4/Shots/`

## Hard rules (don't break these)

- **Files:** create or edit only under `unity-session/unity/Assets/LastBell/Areas/Area4/` and `unity-session/prototype/arena-3d/areas/area-4/`.
  - Don't touch other areas, `Arenas/`, `Characters/`, `Core/`, `Scenes/`, `VFX/`, the project settings, or any shared `.meta` file outside Area 4.
  - Copying whole folders can overwrite shared `.meta` files such as `Areas.meta`, which happened once. Copy only `Areas/Area4` and `Areas/Area4.meta`.
- **Git:** you're in the shared checkout `unity-session` on `feature/class-test-arena`, where other sessions are working too.
  - Don't switch branches, stash or reset.
  - Stage only Area 4 paths by name.
  - Push is allowed. If a push is rejected because the remote moved, fetch and check whether your commit is already on the remote before doing anything else.
- **The Unity Editor may have the project open** (`Temp/UnityLockfile`). If it does, build in a scratch copy, then copy back only `Areas/Area4` and `Areas/Area4.meta`:
  1. Copy `Packages`, `ProjectSettings`, `Assets/Settings`, `Assets/InputSystem_Actions.*` and `Assets/LastBell/Areas/Area4` into your scratchpad.
  2. Run `Unity.exe -batchmode -projectPath <copy> -executeMethod LastBell.Areas.Area4.EditorTools.ShatteredHourArenaBuilder.BatchBuildAndCapture -logFile <log>`. Unity is 6000.6.6f1 and lives in `C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\`.
- **Keep:**
  - the gameplay camera: perspective, FOV 44, position (0, 29, 22.5), looking at (0, 0, 1)
  - the playable floor: flat circle, R 13.5 m, exact collision, nothing taller than 0.9 m inside the rim
  - the boundary colliders
  - the overall layout
  - the readability tests: the coral telegraph test circle must still pop, and 8 players at r 11 must still read
- **The fissure and the four rim stations stay dormant decoration.** No pulsing, no particles, no red or coral, no ground glow pools.
- **Style hard rules (gpt/02 and gpt/08 §9):**
  - painterly stylized, low-poly-leaning
  - crafted bevels and hand-painted breakup
  - 2–3 broad tone bands
  - coloured shadows, never pure black
  - **No** photorealism, rust realism, soot, micro-greebles or rivets everywhere, busy floor textures, mirror metals, heavy comic outlines, neon bloom, or bright white dais.
  - Detail budget: about 70% big shapes, 25% medium, 5% small. Every ornament should signal function or lore (bells, clock rings, escapements, astronomical marks), never random gears.
- **Rendering gotchas found in the first build:**
  - URP strips `_EMISSION` if `globalIlluminationFlags` is `EmissiveIsBlack`. Use `RealtimeEmissive`.
  - `Camera.RenderToCubemap` in batch mode corrupts URP state (ZBinning errors). Don't use it.
  - The image viewer may cache a PNG by path. Copy each round's shots to a new numbered folder before viewing them.
  - Always render a warm-up frame after changing views.

## How to add detail and resolution

Pick per object, cheapest first:

1. **Better procedural geometry in the builder:**
   - real bevels and chamfers (inset and extrude edges by 2–6 cm on stone blocks and trims)
   - more segments on curves (rings, bell, drum: at least 96–192 around)
   - proper gothic profiles: pointed arches from two arcs with a thickness profile, mouldings swept along paths, tapered spires with crockets
   - Write small mesh helpers (sweep a 2D profile along a path or circle; bevelled box; lathe with smoothing groups) and reuse them.
2. **Painted textures generated in C#** (as the floor already is):
   - 2048² for hero objects (bell, rim, stations, bridges)
   - 1024² trim sheets for repeated stone and brass
   - 4096² only for the floor albedo
   - Every hero material gets albedo with hand-painted-style breakup (broad planar value shifts, chipped edges, soft AO in creases) plus a **normal map** (from a generated height map) plus a smoothness mask.
   - Use **one shared stone trim sheet** and **one brass trim sheet** for most objects, so detail stays consistent and draw calls stay low.
3. **Optional modelling in Blender** for a few hero pieces (the bell, a rim station, a pinnacle, an arcade module), only if procedural geometry can't reach the look:
   - Blender is available (`blender-work/` has examples).
   - Script it with `blender --background --python`.
   - Export FBX or glTF into `Area4/Models/`.
   - Keep each piece under the triangle budget and stylized (bevel modifier plus faceted shading, not sculpted realism).

Generated textures go in `Area4/Generated/`. Import them with the `ShatteredHourTextureImport` postprocessor: normal maps as normal maps, mipmaps on, aniso 8.

## Object-by-object targets

For each object, keep the position and scale from the current build unless noted.

### 1. Combat floor (playable)

- **Problem now:** the flagstones read as an even, generated Voronoi "turtle shell" with heavy grout.
- **Target:** large, quiet, dark slabs that read like the plan's floor-stone detail study.
  - Slabs 1.5–2.5 m, loosely radial near the medallion (keep the polar zone inside r 7.5), freer further out, with some long and some short slabs.
  - Per-slab value ±8% and a few `#252D45` cobalt slabs in the `#1E2433`–`#2A3142` range.
  - Thin, soft grout at about 60% of its current contrast.
  - Painted edge wear, sparse hairline cracks, and very soft large-scale tonal drift.
  - Albedo stays 4096²; normal 2048² with gentle bevels only, so it never looks bumpy from the gameplay camera.
- **Brass inlay:** make it read as **flush engraved brass with a bevelled edge and a darker engraved line inside**, not flat paint.
  - Medallion: add a fine second ring of tiny engraved marks between r 2.6 and 3.2, and small fleur tips on the long rays.
  - Make the axis stars, the r 6.5 diamonds and the r 11.4 arrowheads crisp and about 30% bigger than now. They should still sit below the medallion and well below telegraphs.
  - The 48 + 12 rim ticks must be visible from the gameplay camera.
- **Fissure:** a fine flush gold inlay crack (`gpt/05`: "walkable, not a hazard").
  - Jagged, 0.12–0.22 m wide.
  - A thin `#0A0816` charred lip, a gold `#E2A650` body and a thin ivory core.
  - Colour comes from emission over a dark albedo, so the lavender key light can't tint it pink.
  - Add 2–3 tiny broken stone chips along its edges in the texture.
  - It must never look like lightning or a hazard.

### 2. Rim parapet, cap and drum

- **Parapet wall:** use the stone trim sheet with chamfered block courses (about 0.6 m tall, staggered), painted chips, and a slightly darker lower course.
- **Cap:** a real gothic moulding profile (drip edge plus rounded top) swept around the ring. Use slate, with a thin brass bead on the inner edge and the second brass band 0.3 m lower on the inner face. Both bands are bevelled.
- **Low arcading on the outer face** (blind pointed arches, 0.6 m wide, every 2.2 m) gives the busy, ornate rim the plan shows.
- **Drum below the rim:**
  - buttresses with stepped set-offs and a sloped cap
  - deep arched window recesses with tracery: one mullion and a small rose circle
  - every third window glows faintly warm (emission 0.3 or less)
  - a brass band with numeral studs
  - Then the rock spire: faceted, with painted strata bands.
- **Pinnacles** (all 20):
  - square shaft with corner buttress ribs and gabled faces
  - a crocketed, tapered spire (crockets as small brass leaves)
  - a cross or orb brass finial
  - Use three height variants, slate plus brass, with no flat black faces. Check that all normals face outward.
- **Lanterns:** an iron lantern with four glass panes (energy-glass style: soft inner gradient, bright rim), a small roof spire and a ring handle. Amber flame `#FFB860`; flames read about 2× larger than now. Keep 8 point lights, range 5 m.

### 3. Four rim stations (dormant)

Match the plan's "rim station detail" study:

- A stepped circular plinth with two bevelled brass rings and an engraved dial face. Paint 24 ticks, 8 rune glyphs and one inner ring of tiny numerals into a 2048² texture plus normal map.
- A domed polished-brass centre boss with a small finial.
- Four short gothic posts around the outer half, plus a pointed-arch gate frame with tracery behind it.
- Emission at 0.1 or less. No glow ring on the floor.

### 4. Entrances

**South arrival (match the plan's "southern arrival bridge" landmark):**
- The grand stair gets bevelled treads with brass nosings and stone stringers.
- The causeway gets slate flagstones (same trim sheet), a brass centre inlay, and low walls with mouldings and buttress pinnacles every 4 m.
- Tall gatehouse towers either side of the stair get blind tracery, spires and **large crimson banners** (`#6E1A22`, gold sunburst, about 1.3 × 4 m). Hang them from brass rods with swallowtail ends and two cloth folds modelled as gentle bends.
- A second pair of pylons further out carries smaller banners.
- Check that the banners read in the establishing and south-entrance shots.

**North stair:** brass nosings and side walls with mouldings. The landing becomes a dark pedestal with an engraved brass ring. Leave clearance under the bell's tilted lip; it currently sits at y ≈ 1.2.

**East and west bridges:**
- flagstone deck (not planks)
- moulded side walls with posts and small spires
- under-arches or corbels beneath the deck
- a ruined pointed-arch gateway with tracery fragments
- a ragged broken far end, with stone chunks hanging in the void

### 5. The cracked celestial bell (`LB_ENV_FinalToll_BrokenBell`)

- **Mesh:** a smooth high-resolution lathe (128+ segments, a profile with 30+ points, a real wall thickness and a rounded lip). Model the 30° missing wedge with **broken jagged edges**, not a clean slice.
- **Material:** dark aged bronze `#5E4A30`, metallic about 0.6, smoothness about 0.35. Add painted breakup: lighter worn edges and darker recesses. Keep it from reading white (reflection probe) or flat tan.
- **Bands:** three engraved gold bands with raised Roman numerals and astronomical glyphs, as texture plus normal.
- **Cracks:**
  - 6–7 fine cracks
  - gold emission about 2.5
  - modelled as thin grooves with emissive cores
  - branching, and thinner toward their tips
- **Interior:** an emissive gradient (bright `#FFA850` near the lip, falling off inside) plus a warm point light that rakes the north stair.
- **Shard:** 1–2 broken shards with matching jagged edges float near the gap.
- **Crown and chains:** a cast crown with a yoke beam, and two heavy chains with proper oval links rising out of frame.
- **Gameplay framing:** the bell must read as **a bell mouth with glowing lip and cracks** along the top roughly 100 px of `gameplay.png`, not as a dark slab. Adjust the tilt (−32° now) and the height (lip y ≈ 6.3 m now) within the clearance limits.

### 6. Astrolabe and clock rings

- **Astrolabe** (r 14 behind the bell): a thick brass band with raised numerals and a degree scale, an inner rete (star map) with curved arms and star pointers, and a hub with a pointer. Keep the 70° broken section, with jagged ends and a few small fragments drifting.
- **Side rings (2):** the same treatment, smaller, with spokes. Remove anything that reads as a rollercoaster rung or a thin wire hoop.

### 7. Void and surrounding architecture

- **Floating arcades:** use real gothic modules (two to four pointed arches on clustered columns with capitals and bases, a moulded entablature, broken ends, a rock underside). Use fewer, larger pieces than now, set at r 30–45. Give them brass edge trim on the capitals.
  - Push the top-corner pieces back or down so the top edge of the gameplay frame belongs to the bell.
- **Inverted spire chunks and rocks:** faceted, with painted strata, a cool rim light and violet distance haze.
- **Chains:** proper oval links, about 1.2 m, dark iron `#2A2440`, with a slight sag.
- **Hanging bells:** small versions of the hero bell, bronze, with a brass lip band and clapper.
- **Void depth:**
  - Separate values: the platform must silhouette against the void. Darken the skybox mid-tones near the platform toward `#0B0820`, and keep brighter nebula bands (`#7B4FD0` / `#9C86E8`) behind the bell and at the horizon.
  - A dim violet-blue spiral vortex below the platform, visible past the rim.
  - 2–3 warm light shafts behind the bell (alpha about 0.1) as the gold counterpoint.
  - Layered fog: 0.012–0.016 near, with far haze in `#A8A4D1`.

### 8. Lighting and colour (do this last, then re-check readability)

- Aim for the screenshot mix in `gpt/02`: about 55% cool dark base, about 25% violet or blue environment, about 15% warm brass/gold, about 5% brightest accents.
- Floor and stone read cobalt-charcoal, not lavender. Lower the ambient sky intensity if needed.
- Brass reads as aged brass: warm diffuse base with broad bright edge highlights.
- Bloom stays subtle and only on gold cores.
- Add a soft cool rim or bounce light so the dark architecture separates from the void.

## Performance budget (gameplay camera)

- 500k triangles or fewer visible.
- 200 draw calls or fewer: use static batching for the rim and architecture, and GPU instancing for repeated modules, links, lanterns and debris.
- One shadow-casting directional light, and at most 10 non-shadow point lights.
- Textures: 2048² maximum except the floor albedo (4096²). Share trim sheets.
- Report the measured numbers (from the Frame Debugger or `UnityStats` in a capture).

## Process

1. **Audit first.** Make a short checklist of every object group with its current state, the target from this prompt, and the approach (procedural, texture or Blender). Save it to `prototype/arena-3d/areas/area-4/DETAIL-PASS-CHECKLIST.md`.
2. Build in this order, rendering the five standard shots plus `south-entrance.png` after each step:
   1. trim sheets and helpers
   2. floor
   3. rim and drum
   4. stations
   5. entrances
   6. bell and astrolabe
   7. void and architecture
   8. lighting
3. Keep `BatchBuildAndCapture` working and add any new shots it needs (for example `bell-closeup.png` and `station-closeup.png`). **Rebuild** must still regenerate everything cleanly.
4. Commit after each major step, with a short message. Stage only Area 4 paths.

## Critic loop

After the full pass, spawn an independent critic subagent. Give it:
- the plan, the mood image, `gpt/images/10-arena-shattered-hour.png` and `gpt/02-visual-style-guide.md`
- this prompt
- the newest shot folder

Ask it for an overall score out of 10, sub-scores (layout, floor, rim/stations/entrances, bell/astrolabe, void, lighting/colour, readability) and ranked concrete fixes. Tell it the intentional constraints so it doesn't penalise them: the fixed camera, the flat floor, dormant decoration, the procedural build.

Refine and re-run the critic **up to 4 times, or until it scores 8/10 or higher**. Copy each round's shots to a new folder.

## Deliverables

- The updated Area 4 builder, shaders, textures, any models, the scene and both prefabs, all regenerating cleanly from the menu **Last Bell › Areas › Area 4 – Shattered Hour (Rebuild)**.
- These shots in `Area4/Shots/`:
  - `gameplay.png`
  - `establishing.png`
  - `topdown.png`
  - `south-entrance.png`
  - `bell-closeup.png`
  - `station-closeup.png`
  - `telegraph-test.png`
  - `eight-players.png`
- A short report for the user, who reads on a phone, so keep it plain and brief:
  - the critic scores for each round
  - what changed
  - the measured triangles and draw calls
  - any rule you bent and why
  - open questions

## Decided by Chzy18 (2026-10-21)

- Players stay on the round floor. The south causeway, the east and west bridges and the north stair are scenery; the boundary colliders close every gap.
- The north landing under the bell is scenery too. Nobody walks on it.
