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
- A critic loop scored it 5.5 → 6.5 → 6.5 → 6.5 → 6.8 / 10. Open items: the rim blocks still read a little crate-like, the molten ring is a flat band, the city below needs glowing lava rivers, and the towers read as lids from above.
