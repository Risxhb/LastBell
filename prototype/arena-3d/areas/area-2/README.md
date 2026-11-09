# Area 2: The Brass Foundry (look mockup)

A three.js look study for the arena shared by Area 2's three minibosses and its area boss. It's built as close as possible to the Brass Foundry panel of `Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png`. Open `brass-foundry.html` in Chrome or Edge. It needs an internet connection for three.js.

- `?view=game`: the Bellkeeper arena's gameplay camera (the default)
- `?view=hero`: the reference's main painting angle
- `?view=top`: the reference's top view
- `?tele`: places a sample attack telegraph and dims the floor glow, to check that decoration stays quieter than attacks
- `?spawns`: marks eight player spots
- `?clean`: hides the tabs

## What's in it

The arena is built in `foundry-env.js` (`window.buildFoundryEnv(THREE, scene)`, root `LB_ENV_BrassFoundry_Arena`).

- **Floor:** a wheel-shaped bronze floor 26.4 m across, flat and unobstructed.
  - Eight raised brass spokes with studs.
  - Eight furnace-window grids (5 × 3 rounded cells behind iron mullions).
  - A toothed gear medallion around a painted sun with a glowing core.
  - Raised brass rings, gold-engraved rings, clock ticks, scrollwork, cartouches, brass-filled cracks, rivets and soot.
- **Rim:** a chunky raised masonry rim with brass trim. It has merlons, small machinery (gear housings, valves, braziers) and four closed portcullis gateways.
- **Gate arms:** each arm has two furnace houses with chimneys. A truss bridge, hung from chains and lit by cage lanterns, leads to a pier tower.
- **Below the floor:** an arched furnace drum with a corbelled cornice, pilasters, barred and glowing furnace mouths, and two crucible spouts pouring molten falls. A stepped base and support legs descend into the abyss.
- **Around the arena:** gothic foundry towers with tiered cornices, merlons, balconies, buttresses, arched glowing windows, lantern stages and spires. Some towers pour molten falls from furnace mouths, and two have crane arms hanging cage lanterns. There are gantries, chains from above with cage counterweights, and far silhouettes in the haze.
- **Atmosphere:** an ember-red sky, orange fog, glowing haze layers over a molten sea, rising embers and chimney smoke.

Molten metal only appears below the floor or behind grates, so nothing on the floor reads as a hazard. Boss identities and mechanics aren't designed yet.

The Unity build prompt (from `ref-brass-foundry-topdown.webp`) is `BRASS-FOUNDRY-UNITY-PROMPT.md`.

Renders are in `shots/` (`compare-*.jpg` puts the three.js mockup next to the reference; `unity-*.jpg` are the Unity build).

## Unity

Built from `BRASS-FOUNDRY-UNITY-PROMPT.md` and `ref-brass-foundry-topdown.webp` in `unity/Assets/LastBell/Areas/Area2/`.

- **Last Bell > Area 2 – Brass Foundry** builds the scene on first use and opens it; **Rebuild Area 2 – Brass Foundry** regenerates it. Everything is generated from code: meshes, textures, materials, sky, fog, lights, post and FX. `Generated/` and the scene are git-ignored (about 47 MB) because the menu item rebuilds them.
- In Play mode, C cycles the gameplay camera, the top-down view and the hero angle. `quiet` dims the molten and window glow while telegraphs are up.
- Batch build and screenshots:
  ```
  Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.BatchBuildAndCapture [-shots <dir>]
  ```
  It also writes `unity-report.txt`, an occlusion check. The gameplay camera must see all 128 floor sample points.
- The playable floor is r ≤ 14, with a ring of boundary colliders at r 14.35. `BossSpawn` and `PlayerSpawn_1` to `PlayerSpawn_8` are under `Gameplay`. The floor uses the `ArenaFloor` layer if the project defines one; it doesn't yet, so the floor stays on Default.
- First build: a critic loop scored it 5.5 → 6.5 → 6.5 → 6.5 → 6.8 / 10.

### Detail pass (`BRASS-FOUNDRY-DETAIL-PASS-PROMPT.md`)

- **Flowing lava:** `Shaders/MoltenFlow.shader` (`LastBell/Area2/MoltenFlow`) is unlit, HDR, fog-aware and SRP Batcher compatible. It is used on the channel ring, the falls down the drum, and the city rivers and pools.
  - **Flow:** two-phase flow-map blending, with noise stretched 2:1 along the flow and domain-warped. The flow is faster mid-stream than at the banks (parabolic profile).
  - **Crust:** plates in two sizes break apart and re-form, with darker crusted banks and a hot mid-line. A noise generator measures crust coverage: 46%.
  - **Life:** a ±8% pulse, popping bubbles and a 3 cm vertex undulation.
  - **Quiet:** `_Quiet` dims emission to 60%.
- **Clock:** time comes from the global `_LB_Area2Time`. `BrassFoundryArena` sets it from `Time.time` in Play mode. In edit mode it uses `EditorApplication.update` (30 fps), so the lava also flows in the Scene and Game views (turn on Always Refresh in the Scene view). The batch capture sets it directly.
- **Driver:** `BrassFoundryArena` also lerps `quiet` over 0.25 s. It flickers the molten and lantern lights (±10%, de-synced), lets the glow curtain breathe, and flickers the furnace glow.
- **Particles:** `Area2Particles.cs` is a small CPU particle emitter, since the project has no ParticleSystem module. It drives:
  - embers rising from the channel
  - heat haze, channel sparks and gate sparks
  - steam vents on the outer wall
  - chimney smoke
  - smoke banks below
  
  None of it spawns over the floor.
- **Textures:** all generated in code, with mipmaps and anisotropic filtering at 8.
  - Floor 4096: albedo, height (imported at 2048), AO and smoothness maps.
  - Two masonry variants at 2048.
  - Iron plate (with rust streaks), deck planks and molten noise at 1024.
  - Brass at 512, and a sky panorama at 2048 × 1024.
  - A shared detail normal at 512.
- **Texel density (px/m):** floor 146, rim 341, decks 171, masonry 1024, iron 512.
- **Budget:**
  - About 131 renderers before batching, 31 materials and 281 k triangles.
  - 78 unshadowed point lights.
  - 93.6 MB of textures (budget 120).
- **Detail added:**
  - **Rim:** single chamfered gear teeth with lava gaps between them.
  - **Outer wall:** a three-tier terraced ring with buttresses, flanged pipes, furnace hatches, steam vents and catwalks.
  - **Towers:** three stepped tiers with crenellations, a merlon crown, machinery boxes, dome dormers, varied window glow, and chimneys on the far pair.
  - **Gantries:** pylons at r 26, truss undersides and end gatehouses.
  - **Entry bridge:** a far arch.
  - **Furnace gate:** a recessed furnace mouth with a gradient glow and halo, and a toothed gear emblem.
  - **City below:** three depth layers with continuous lava ribbons and pools in the streets.
  - **Atmosphere:** exp² fog.
- **Checks in `unity-report.txt`:**
  - The lava flow check compares the same gameplay frame at clock 0 and 1 s. The channel must change by more than 4/255 and the floor by less than 0.5/255. Result: 13.1 against 0.00.
  - Occlusion: 0 of 128 points blocked.
  - Stats and texel density.
- **Run scene check:** `BatchVerifyRunScene` checks the run loop's `Run_BrassFoundry.unity` (after `RunBuild.BuildScene`): the driver and the lava materials are there, and the lava flows (13.3 against 0.00, in `unity-runscene.txt`).
- **Critic loop:** 6.9 → 7.0 → 7.0 → 7.0 → 7.0 / 10. It did not reach 8. Open items:
  - The rim teeth should be wedge-shaped and evenly spaced, so they read as one gear ring.
  - The lava crust is still a little cellular, with too much yellow-white.
  - The city rivers are too bright from the top view.
  - The abyss needs more depth layers and a horizon glow.
  - The lava ring should run unbroken past the corner towers.

### Blender-modelled assets (`BRASS-FOUNDRY-BLENDER-MODELING-PROMPT.md`)

- **Source:** `blender/area2_models.py`, for Blender 5.2. It models every environment asset with real geometry:
  - **Rim teeth:** wedge-shaped gear teeth with a 45° chamfer, chiselled planar chips, a lighter capstone tier, and brass bands or rivets.
  - **Towers:** two tiered towers with crenellations, machinery, recessed window slits with mullions, a ribbed dome with dormers and a finial, and a chimney on the far pair.
  - **Furnace gate:** banded pillars, a lintel with a toothed cog, a riveted portcullis and a recessed furnace mouth.
  - **Outer wall:** terraced modules with a hatch, a buttress or a steam vent, and flanged pipes.
  - **Bridges:** plank deck modules with an iron truss, plus a suspension pylon, an end gatehouse and an entry gate pillar.
  - **Props:** a cage lantern and an oval chain link.
  - **City:** six foundry buildings, including sawtooth halls and stacks.

  Material slots are named for this builder's URP materials (Stone, Masonry, Iron, Brass, Glow and so on). UVs are box-projected in metres to match the tiling textures.
- **Export:** one `.glb` per asset into `unity/Assets/LastBell/Areas/Area2/Models/`, about 80 k triangles in total.
  - Command: `blender -b --factory-startup -P area2_models.py -- <out_dir> [contact_sheet.png]`.
  - In Unity: **Last Bell > Area 2 – Build Models (Blender)**. The arena build also runs it automatically when `Models/` is missing and Blender is installed (`LASTBELL_BLENDER` overrides the path).
  - `Models/` (about 3 MB) is committed, so teammates without Blender get the models too. Re-run the script, or the menu item, after changing `area2_models.py`.
- **Unity:** `BrassFoundryArenaBuilder` reads each model's mesh and slot names and appends the geometry into its batches, so draw calls stay low. Each asset is placed with the same layout data:
  - one even crown of teeth, with a molten slot glowing in every gap
  - a 9° wall module per segment
  - 2 m deck modules
  - towers at the diagonals
  - city buildings scaled to their slots

  Lanterns get a standalone mesh so they can sway. Without the models (no Blender), the builder logs one warning and falls back to the code-built geometry.
- **Checks:**
  - Lava flow: 10.4 against 0.00.
  - Occlusion: 0 of 128 blocked.
  - Budget: 827 k triangles, 115 renderers, 32 materials, 97.6 MB of textures and 78 lights.
- **Critic loop:** 6.8 → 7.0 → 7.0 → 7.2 → 7.2 / 10. It did not reach 8. Open items:
  - The teeth still read as boxy from the gameplay camera.
  - The lava moat should be a wider, continuous band.
  - The sky and fog need an orange underglow and depth haze.
  - The floor inlays should be duller.
  - The city skyline should be more varied.

  No sculpt or texture bakes were made: the assets use the project's tiling textures.

### 2 Nov art pass (accepted as a task by Chzy18 through the review inbox)

The rim and moat now follow the reference rather than the earlier critic's "wedge" idea.
- **Molten ring:** wider (r 16.0–18.65 m) and higher (y −0.7). It starts right behind the rim teeth, so from the gameplay camera it reads as one continuous glowing band. The lava has larger, softer bands and a brighter orange-white core. Constants: `ChannelIn`, `ChannelOut`, `ChannelY`.
- **Rim:** big stepped merlons alternate with small ones, in large-block rim stone with aged-bronze caps. They sit on a continuous low parapet with a brass cap band, and lava glows in every gap. A second ring of small merlons stands on the outer wall's first tier, across the lava.
- **Towers:** slightly larger, with their bases at the moat's outer edge.
- **Floor:** a riveted dark-brass band at the edge, a warmer slate, and inlays in their own `FloorInlayBronze` material (desaturated, a step below the trim brass).
- **Light and sky:**
  - The sky panorama now uses latitude–longitude mapping. It was "6 frames", which showed a stretched horizon band as a flat rust wash below the arena.
  - Below the horizon, the sky glows orange in patches.
  - The pit floor glows faintly, so the city reads as silhouettes.
  - More pit lights.
  - Half the moat lights now light the merlon backs and the outer wall.
  - Two rim lights on the camera-side towers.
  - Warm light on the floor's outer ring (light only, no glow).
  - The embers are orange.
- **GUID-stable rebuilds:** `SaveAsset` now overwrites meshes, materials and the post profile in place, so a rebuild keeps every `Generated/` GUID. The run scene, the menu showcase and the current-player-assets pack reference those GUIDs.
- **Copies:** `BatchRefreshCopies` re-copies the run scene (keeping its .meta) and re-bakes only the Brass Foundry stage of the main-menu showcase. `BatchViewShots` renders look-check views of the saved scene.
- **Checks:**
  - Lava flow: 16.2 against 0.00. The run scene gives 16.2 too.
  - Occlusion: 0 of 128 blocked.
  - Budget: 851 k triangles, 115 renderers, 33 materials, 97.6 MB of textures and 88 lights.
- **Critic loop:** 7.3 → 7.4 → 7.6 → 7.7 → 7.7 / 10, against 7.2 before. It did not reach 8. Open items:
  - Crisper bevels and brass straps on the merlons.
  - Depth haze, heat shimmer and a vignette into the abyss.
  - A darker crust edge and brighter cracks in the lava.
  - Brass that reads as raised metal.
  - Chain pylons at the gantry and bridge mouths.
  - Real silhouette buildings in place of the flat city cards.
