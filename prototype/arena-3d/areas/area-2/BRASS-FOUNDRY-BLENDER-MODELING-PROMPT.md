# Prompt: Hand-model the Brass Foundry environment in Blender

Paste everything below the line into a Claude Code session that has the `unity-session` checkout open.

---

## Task

Today the Brass Foundry arena (Area 2) is built entirely from code: `BrassFoundryArenaBuilder.cs` stacks boxes, lathes and sectors and paints procedural textures. It scores **7.0 / 10** with an art critic, and code-built shapes are the ceiling. The rim teeth read as crates, the towers as stacked cylinders, and the chains as low-poly rings.

Replace the environment's **objects** with **hand-modelled Blender assets**. That means real silhouettes, sculpted chips and wear, proper bevels and UV-unwrapped, baked textures. Bring them into Unity so the arena reaches **8 / 10 or better** at the Hades/Windblown bar: painterly, chunky, crisp and readable from the gameplay camera.

Keep what already works:
- the layout and sizes
- the flowing-lava shader (`Shaders/MoltenFlow.shader`) and its time driver
- lighting, fog, post and particles
- the floor (a calm, flat disc)
- the gameplay objects and every automated check

This is an **art replacement pass**, not a redesign.

## Read first

1. `gpt/08-design-rules.md`: these settled rules override everything.
2. `gpt/02-visual-style-guide.md` and `gpt/05-arenas.md`: style source of truth.
3. In `prototype/arena-3d/areas/area-2/`:
   - `ref-brass-foundry-topdown.webp`: the reference. Keep it open the whole time.
   - `README.md`
   - `BRASS-FOUNDRY-UNITY-PROMPT.md`: layout and sizes.
   - `BRASS-FOUNDRY-DETAIL-PASS-PROMPT.md`: the last pass, its rules and checks.
   - `shots/unity-*.jpg`: the current look.
4. The current code in `unity/Assets/LastBell/Areas/Area2/`:
   - `Editor/BrassFoundryArenaBuilder.cs`: positions, radii, counts, materials, checks.
   - `BrassFoundryArena.cs`, `Area2Particles.cs` and `Shaders/MoltenFlow.shader`.
5. How this project already drives Blender from scripts:
   - `prototype/arena-3d/models/build-bellkeeper.ps1`
   - `prototype/arena-3d/models/blender/bellkeeper2_prep.py`
   - Blender is `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`.

**Last critic's open items:**
- The rim should be one readable ring of wedge-shaped gear teeth.
- The lava crust looks too cellular.
- The city below looks like flat boxes.
- The towers read as stacked cylinders.
- The brass strips look like flat planks.
- The chains are low-poly.

## Rules you must keep

- **Design rules:**
  - Decoration never looks like a hazard.
  - The floor stays calm, with no glow; coral telegraphs must be the most eye-catching thing on it.
  - Run layout: centre origin, entry to the south (+Z), floor at y = 0, walkable radius of 14 m.
  - Fixed high-oblique camera; nothing may hide the floor from it.
- **Run-loop contract (don't break it):**
  - Keep `public static void BrassFoundryArenaBuilder.Build()`.
  - Keep the scene path `Assets/LastBell/Areas/Area2/BrassFoundry_Arena.unity` and the root `LB_ENV_BrassFoundry_Arena`.
  - Keep `Gameplay/BossSpawn`, `PlayerSpawn_1` to `_8` and `Boundary`.
  - Look-check props must be named `ScaleReference_Vanguard`, `TelegraphCheck` or `TelegraphTest`, or exist only in the batch capture.
- **File ownership:**
  - Edit only `unity/Assets/LastBell/Areas/Area2/**`, `prototype/arena-3d/areas/area-2/**` and a new `prototype/arena-3d/areas/area-2/blender/` folder for `.blend` sources and Python.
  - Don't edit the run loop, other areas, characters, `ProjectSettings` or URP assets. Stop and ask if one needs to change.
- **Large files:**
  - Don't commit big binaries (`.blend`, `.glb`, texture PNGs over about 1 MB).
  - Publish them as an **asset pack**, following `docs/asset-packs.md` and `asset-packs.json`: a new `brass-foundry-models-v1` release, with `.meta` files included, and only single large files, never code.
  - Commit the Blender **Python scripts** that build and export the assets, so they can be rebuilt.
  - Ask the user before publishing the release.
- **Git:**
  - Work on `feature/class-test-arena` and run `git pull --ff-only` first.
  - Never stash, reset or switch branches. Stage only your own files, by path.
  - Commit and push when done (approved for Last Bell).
  - End the commit message with the attribution line you've been given.
- **Unity:**
  - The user's editor usually has the project open. Build and capture in a scratch copy of the project, run in batch mode, then copy back only `Areas/Area2` (the same flow as the last pass).

## Modelling standards

Work in **Blender 5.2**. Model every asset by hand: real topology, not booleans left messy or decimated scans. Drive Blender from Python wherever it helps repeatability (scene setup, naming, export, bakes). The forms themselves should be modelled with intent: bevels, silhouettes, chips and sculpted wear.

- **Scale and axes:**
  - 1 Blender unit = 1 m. Model with Z up; export glTF/FBX with +Y up for Unity.
  - Each asset's origin is at its base centre. For ring pieces, put the origin at the arena centre so they rotate into place.
  - Apply transforms before export.
- **Style:**
  - Chunky, readable silhouettes with slightly exaggerated proportions.
  - Big bevels: 4–8 cm on stone, 1–2 cm on brass.
  - Hand-carved chips and cracks on stone edges, worn brass on edges, and rust and soot in the recesses.
  - Painterly, not photoreal: hand-painted or stylised baked albedo, readable at gameplay distance (about 30 m).
- **Polycount (triangles, LOD0):**

| Asset | Triangles |
|---|---|
| Rim tooth | 600–1,500 each |
| Tower | 8–15 k |
| Gate | 10–15 k |
| Outer wall module | 2–5 k |
| Chain link | 150–300 |
| Lantern | 500–1,000 |
| Gantry module | 3–6 k |
| City building | 300–1,500 |

  - Whole arena: under about 1.5 M triangles. Add LOD1 for towers, wall modules and city buildings (about 40%).
- **Textures:**
  - **Trim sheets and tiling materials** are shared across assets, rather than unique textures everywhere. Make at least:
    - a **foundry stone** trim (blocks, chamfers, chipped edges)
    - an **iron plate and rivet** trim
    - a **brass** trim (bands, caps, worn edges)
    - a **wood plank** deck
  - **Bake per asset** where it matters (towers, gate, rim teeth): normal, AO and curvature from a high-poly or sculpt onto the game mesh.
  - Texel density: 256–512 px/m on hero pieces near the floor (rim, gate, bridges), 128 px/m on towers and walls, 32–64 px/m on the city.
  - Output URP Lit sets: BaseMap, Normal and MetallicSmoothness (alpha = smoothness), plus Occlusion where baked.
  - Keep total arena texture memory under about 150 MB.
- **UVs:** clean, non-overlapping UV0 for baked assets; trim-sheet UVs on modular pieces. No stretched or mirrored UVs where text or emblems show.
- **Naming:** `SM_Area2_<Asset>_<Variant>` for meshes, `M_Area2_<Material>` for materials, `T_Area2_<Material>_<Map>` for textures.
- **Emissive parts:** windows, furnace mouths and lantern cores are separate material slots, so the existing glow materials and the quiet dimming in `BrassFoundryArena.cs` keep working. Name those slots `Glow`, `GlowDim` and `GlowWarm`.

## Assets to model

Keep the current placement data (radii, angles and counts from the builder). Each asset replaces the matching code-built group.

1. **Rim teeth (most important):**
   - Make 3 big and 3 small variants: wedge-shaped, tapering outward, with a chamfered top.
   - They need chiselled block faces, a brass corner cap or band on some, rivets, and a few chips.
   - Together they must read as **one continuous gear-tooth crown** around the floor, with lava glowing in the gaps.
   - Also model the inner curb ring segment with its brass strip.
2. **Corner towers:**
   - Model one far-tower variant (about 14 m) and one camera-side variant (about 8 m; it must pass the occlusion check).
   - Each has three stepped tiers with crenellations and machinery, a lantern stage with deep-set window slits and mullions, and a ribbed iron dome with dormers and a finial.
   - The far variant also gets a chimney.
3. **Furnace gate (north):**
   - Massive pillars with stepped brass-banded caps and a lintel with a real cog emblem.
   - A portcullis with riveted bars, set in a recessed furnace mouth with an inner arch.
   - Side furnace windows.
4. **Outer wall modules:**
   - Make 3–4 straight-curved modules that tile around r 18.6–23.
   - They need terraces, buttresses, pipe runs with flanged joints, furnace hatches, steam vents and a catwalk piece.
5. **Gantries and entry bridge:**
   - A plank-deck module with brass nosing and an iron truss underside.
   - A parapet, a suspension pylon, an end gatehouse, the entry gate pillars and the far arch.
6. **Chains and lanterns:**
   - A proper oval link (16 × 8 segments) laid along paths, as instances or a curve plus array.
   - A cage lantern with an iron frame, a glass core in a glow slot, a cap and a hook.
7. **City below:**
   - 6–10 modular foundry buildings: sawtooth roofs, chimney stacks, gantries and lit window rows.
   - Arrange them in the existing three depth layers, around the lava streets and pools.
8. **Floor inlays (optional):**
   - The floor disc and its texture stay as they are.
   - You may model the compass medallion and spear tips as bevelled brass meshes if that reads better. Keep them 1–3 cm high, with no glow.

Keep in code: the floor disc, the lava channel and city lava meshes (they need the MoltenFlow UV layout), the glow curtain, lights, fog, sky, post, particles and gameplay objects.

## Pipeline

1. **Set up:** create `prototype/arena-3d/areas/area-2/blender/`. Put the shared trim sheets and materials in `area2_library.blend`, and one `.blend` per asset group (`rim.blend`, `towers.blend`, `gate.blend`, `wall.blend`, `bridges.blend`, `props.blend`, `city.blend`).
2. **Block out:** export the current code-built arena as a guide mesh (a glTF of `LB_ENV_BrassFoundry_Arena`). Model every asset to fit it, so sizes match.
3. **Model, sculpt, bake, texture** each asset to the standards above.
4. **Export with a script:** `export_area2.py` runs in background mode (`blender -b <file> -P export_area2.py -- <out>`). It writes one FBX or glTF per asset into `unity/Assets/LastBell/Areas/Area2/Models/` and textures into `Areas/Area2/Textures/`. Use the import settings the project already uses for the Bellkeeper GLB (glTFast is installed), or FBX with an AssetPostprocessor that sets scale 1, materials external, read/write off and LOD groups.
5. **Unity integration:**
   - Change `BrassFoundryArenaBuilder` so each code group is swapped for prefab instances placed with the same data: rim teeth per slot, towers at the four diagonals, wall modules per segment, and so on.
   - Add one flag, `UseModelledAssets`, defaulting to on.
   - When the models are missing (pack not installed), fall back to the existing code geometry, so a fresh clone still builds.
   - Assign URP Lit materials, built from the exported textures, in an editor script, and keep the glow slots wired to the existing glow materials.
   - Static batching on, GPU instancing on repeated props, LOD groups on towers, walls and city buildings.
6. **Rebuild from empty** every time: delete `Generated/` and the scene first.

## Acceptance (put the results in your report)

1. **Models:** every listed asset group is hand-modelled, UV'd, textured and placed. The code-built versions are only used as a fallback.
2. **Checks still pass:**
   - The lava flow check: channel above 4/255, floor below 0.5/255.
   - Occlusion: 0 of 128 floor points blocked.
   - The telegraph test still pops.
   - No console errors or warnings from Area 2, no pink shaders and no z-fighting.
3. **Budget:** triangles, draw calls after batching, texture memory and lights are all logged in `unity-report.txt` and within budget. Explain anything over.
4. **Run loop:** `RunBuild.BuildScene` works, `BatchVerifyRunScene` passes, and the play-mode autotest is run with its count reported.
5. **Fresh clone:** with the asset pack installed (`setup-assets.cmd`), the menu builds the modelled arena. Without it, the build falls back to code geometry and logs one clear warning.
6. **Screenshots** (JPG, in `shots/`):
   - game, top, hero and game-tele
   - close-ups of a rim stretch, a tower, the gate and a city corner
   - a Blender viewport turntable or contact sheet of the modelled assets
7. **Docs:** `README.md` documents the Blender sources, the export script, the asset pack and how to re-export.

## Critic loop

When acceptance passes, spawn a critic subagent:
- Give it the reference image, the new screenshots, the asset contact sheet, `unity-report.txt` and this prompt.
- Have it score 1–10 against: layout fidelity 30%, materials and colour 25%, lighting and atmosphere 20%, detail and craft 15%, readability 10%. It must also name the top 6 fixes.

Refine (mostly in Blender) and re-run the critic until it scores **8 / 10** or after **4 refinement rounds**. Report each round's score.

## Report back (the user reads on a phone)

Keep it short and plain:
- What was modelled.
- Before and after screenshots.
- The critic scores per round.
- Acceptance results.
- What's left.
- The commit link, and the asset-pack release (ask before publishing).
