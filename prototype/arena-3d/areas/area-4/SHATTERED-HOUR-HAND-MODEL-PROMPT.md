# Prompt: hand-model the objects of The Throne of the Final Toll (Area 4)

Paste everything below the line into a Claude Code session in this repository (the Area 4 session or a fresh one). It assumes the current procedural build (latest commit `87d19d4`, built by `ShatteredHourArenaBuilder.cs`) and the decisions already made. This pass replaces the procedural meshes with individually modelled assets. It does not change the layout.

---

## Your task

Go into Area 4, **The Throne of the Final Toll** (The Shattered Hour, final-boss arena), and replace every procedurally generated object with a **hand-modelled asset**. Each object becomes its own Blender model: blocked out, shaped, bevelled, UV-unwrapped, textured and exported, the way an environment artist would build it, then placed in the existing scene.

The Unity builder stops generating geometry and only places the finished models. The layout, scale, collision and readability of the current build stay exactly as they are.

Target: **8/10 from an independent critic**, against the art bar of Hades and Windblown (painterly, stylized low-poly, crafted bevels). The procedural build peaked at 7/10. Its weakest areas were the bell and astrolabe, the void props and the generic box shapes.

This is environment art only: no boss, no attacks, no gameplay.

## What "hand-modelled" means here (read this first)

You work in **Blender 5.2** (installed at `C:\Program Files\Blender Foundation\Blender 5.2\`).

- **Author every asset individually, in its own `.blend` file.** Make deliberate modelling decisions for that object: silhouette, edge loops, bevel widths, damage, wear. Driving Blender with a Python script (bmesh edits, modifiers you then apply, sculpt or proportional-edit moves, UV operations) is fine, and so is driving the Blender UI with computer use. What is **not** allowed is reusing a generic generator: no "one function makes every arch" and no runtime mesh generation in Unity.
- **Look at your work like an artist.** After every major step, render the asset in Blender (Eevee, three-quarter view plus the gameplay-camera angle, with the arena lighting colours) and look at it before moving on. Fix what reads badly.
- **Everything is modelled in Blender** (decided by Chzy18), including the hero pieces: the cracked bell, the astrolabe and the rim station. No AI image-to-3D and no outside model packs.
- Be honest in your report about how each asset was made (scripted Blender operations, UI edits through computer use, or both).

## Read first (in this order)

1. `AGENTS.md` (repo root) and `gpt/08-design-rules.md`. §9 has the style hard rules.
2. `gpt/02-visual-style-guide.md`: palette, materials, shape language, and the avoid list. This is the art source of truth.
3. `gpt/05-arenas.md` (Area 4) and `gpt/images/10-arena-shattered-hour.png`, especially its **Material & Detail Studies** and **Key Landmarks** panels:
   - floor stone with brass inlay
   - the rim station
   - cathedral stone with chains
   - the cracked bell
   - the broken arches
   - the southern arrival bridge

   These are the modelling targets.
4. `unity-session/prototype/arena-3d/areas/area-4/ref-topdown-layout.webp`: the user's layout plan.
5. `SHATTERED-HOUR-UNITY-PROMPT.md` (dimensions, readability rules) and `SHATTERED-HOUR-DETAIL-PASS-PROMPT.md` (per-object detail targets and decisions), both in that folder.
6. The current build:
   - `unity-session/unity/Assets/LastBell/Areas/Area4/Editor/ShatteredHourArenaBuilder.cs`, which holds the exact positions, rotations and sizes of every object
   - the screenshots in `Area4/Shots/`
   - `Shots/stats.txt`
7. Existing Blender pipelines to copy conventions from:
   - `unity-session/prototype/arena-3d/models/blender/*.py`
   - `unity-session/prototype/arena-3d/models/build-*.ps1`
   - `blender-work/`

## Decisions already made (don't change them)

- Players stay on the **round floor** (R 13.5 m). The south causeway, the east and west bridges, and the north stair and landing are **scenery**. The 64 boundary colliders close every gap.
- The gold fissure and the four rim stations are **dormant decoration**: no glow pulse, no particles, no red or coral, no floor glow pools.
- Gameplay camera: perspective, FOV 44, position (0, 29, 22.5), looking at (0, 0, 1). The bell can only appear along the top edge of the frame. It is tilted −45° so its mouth faces the camera; keep that read.
- The run loop builds a run scene from this arena (`Assets/LastBell/Run/Editor/RunBuild.cs` calls `ShatteredHourArenaBuilder.Build()`). Keep that entry point, the scene path `Area4/ShatteredHour_FinalArena.unity`, and the `ShatteredHourArena` component API (`quiet`, `SetView`, `SetPresentationStage`).

## Hard rules

- **Files:**
  - Unity assets only under `unity-session/unity/Assets/LastBell/Areas/Area4/`; models go in `Area4/Models/<AssetName>/`.
  - Blender sources and scripts go under `unity-session/prototype/arena-3d/areas/area-4/blender/<AssetName>/`.
  - Don't touch other areas, characters, project settings, or any shared `.meta` outside Area 4. When syncing from a scratch project, copy only `Areas/Area4` and `Areas/Area4.meta`.
- **Large binaries** (`.blend`, FBX/GLB over about 1 MB, textures over 1 MB) are **not committed to Git**. Publish them as a versioned asset pack on GitHub Releases with the existing tooling (`tools/asset-packs/New-AssetPack.ps1`, manifest `asset-packs.json`, see `docs/asset-packs.md`; PR #67, branch `tools/asset-packs`).
  - Pack name: `area4-models-v<N>`. Never replace a published version.
  - Commit code, small text assets, `.meta` files, prefabs, materials and the manifest entry.
  - Check with the user before the first publish.
- **Git:** the shared checkout is `unity-session` on `feature/class-test-arena`, and other sessions work there too.
  - Don't switch branches, stash or reset.
  - Stage only Area 4 paths by name.
  - Push is allowed. If a push is rejected, fetch and check whether your commit is already on the remote.
- **Unity Editor lock:** if `unity/Temp/UnityLockfile` exists, build in a scratch copy of the project. Copy `Packages`, `ProjectSettings`, `Assets/Settings`, `Assets/InputSystem_Actions.*` and `Assets/LastBell/Areas/Area4`, then run the batch method there.
  - Unity is 6000.6.6f1.
  - The batch method is `LastBell.Areas.Area4.EditorTools.ShatteredHourArenaBuilder.BatchBuildAndCapture`.
- **Style** (gpt/02, gpt/08 §9):
  - painterly stylized low-poly, crafted bevels, broad planar breakup, 2–3 tone bands, coloured shadows
  - No photorealism, sculpted realism, rust or soot, micro-greebles or rivets everywhere, mirror metals, comic outlines, neon bloom, or bright white dais.
  - Detail budget: about 70% big shapes, 25% medium, 5% small. Every ornament means something (bells, clock rings, escapements, astronomical marks), never random gears.
- **Known rendering gotchas:**
  - URP strips `_EMISSION` if `globalIlluminationFlags` is `EmissiveIsBlack`; use `RealtimeEmissive`.
  - Don't use `Camera.RenderToCubemap` in batch mode.
  - Metallic surfaces mirror the bright realtime probe, so give hero metals a metallic/smoothness map.
  - Copy each round's screenshots to a new folder before viewing them; the viewer caches by path.
  - Render a warm-up frame after each view change.

## Asset list

Model these. Sizes and positions come from the current builder, so match them.

| # | Asset | Variants | Notes | Triangle budget (each) |
|---|---|---|---|---|
| 1 | **Cracked celestial bell** (hero) | 1 + 2 shards | Mouth radius 5.5 m, height about 8.3 m. Thick wall, rounded lip rim, jagged 30° break with visible wall thickness, three engraved gold bands with raised numerals and astronomical glyphs, cast crown, iron yoke, clapper with brass collar. Cracks are modelled grooves (or a baked groove normal) with a separate thin emissive strip or emissive mask; they branch and taper. Inside goes dark, with a warm band just inside the lip. | 25k |
| 2 | **Astrolabe ring** (hero) | 1 | Radius 14 m, 70° broken section with jagged ends and 3–4 drifting fragments. Thick brass band with a degree scale and raised numerals, a dark engraved channel, a rete (curved arms and star pointers) and a hub with a pointer. | 15k |
| 3 | **Rim station** (hero) | 1, used 4× | Stepped circular plinth, two bevelled brass rings, engraved dial face (24 ticks, astronomical marks, small numerals) as texture plus normal, domed boss with finial, four small gothic posts, and a pointed-arch gate with tracery. Flush with the floor. | 8k |
| 4 | **Parapet module** | straight 15° arc segment + 2 end caps | Stone block courses, a gothic cap moulding with drip edge, a brass bead and inner band, blind pointed arcading on the outer face. Must tile around R 13.5–15.1 and leave the gaps of the current build. | 3k |
| 5 | **Drum bay** | 1 (15°), used 24× | Buttress with stepped set-offs and a sloped cap, an arched window recess with mullion and rose, a brass band segment with a numeral stud. Use a glowing-window material variant on every third bay. | 3k |
| 6 | **Rock spire underside** | 1 | Faceted inverted spire under the platform, with painted strata. | 4k |
| 7 | **Pinnacle** | 3 heights (2.5, 3.5, 4.5 m) | Square shaft with corner ribs and gablets, octagonal crocketed spire with brass leaf crockets, orb and cross finial. | 2.5k |
| 8 | **Lantern** | 1 | Iron frame, four energy-glass panes (soft inner gradient, bright rim), amber flame card or mesh, roof spire, ring handle. | 800 |
| 9 | **South entrance set** | stair, causeway segment (4 m), wall segment, gatehouse, pylon, banner | Bevelled treads with brass nosings and stringers, flagstone deck with brass inlay, moulded low walls with small pinnacles, gatehouse with blind tracery and spires, folded swallowtail crimson banners on brass rods. | 6k gatehouse, 2k others |
| 10 | **North stair and landing** | 1 | Three steps, pedestal landing with an engraved brass ring. It must clear the bell lip (lip y ≈ 1.2 over the landing). | 2k |
| 11 | **East/west bridge set** | deck segment, ruined gateway, broken end | Flagstone deck, moulded walls with posts and spires, corbels, under-arch, gateway with tracery fragments, ragged end with hanging chunks. | 5k |
| 12 | **Floating gothic ruins** | 3 modules (2-, 3- and 4-arch, broken ends) | Clustered columns, stepped bases, brass-banded capitals, pointed arches with an inner order, moulded entablature, rock underside. | 6k |
| 13 | **Void rocks and spire chunks** | 6 rocks + 4 spire chunks | Faceted, with painted strata and silhouettes chunky enough to read at distance. | 400–1.5k |
| 14 | **Chain link** | 1 (+ low LOD) | Oval forged link, dark iron. Used for diagonal chains, bell chains and hanging-bell chains. | 120 / 40 |
| 15 | **Hanging bell** | 1 | A small cousin of the hero bell, with crown, lip band and clapper. | 2k |
| 16 | **Side clock rings** | 2 | Banded brass with spokes, numerals and a broken arc. No wire-hoop look. | 6k |

**Stays procedural** (don't model these):
- the flat floor disc and its painted 4096² texture: it is decoration on a collision-accurate flat circle, already approved
- the boundary colliders
- the skybox, vortex and light-shaft shaders
- lighting and post-processing

Re-check the lighting at the end against the new assets.

## Technical specs for every model

- **Units and axes:** metres, Blender Z-up. Export to Unity as FBX: Apply Transform, forward −Z, up Y, scale 1.0. Check in Unity that each object lands at scale 1 with rotation (0, 0, 0).
- **Pivots:**
  - modules: at the base centre, on the floor plane
  - curved rim modules: at the arena origin, so they snap by rotation
  - the bell: at the centre of the mouth
- **Topology:**
  - clean quads where they deform shading, triangles allowed on flat faces
  - bevel modifier (1–3 segments, 2–6 cm) applied, with weighted normals or custom split normals so faces stay flat and edges catch light
  - no n-gons in the export
- **UVs:**
  - Hero assets: unique UVs at about **512 px/m**, packed into 2048² sets (the bell may use 2 × 2048²).
  - Modules: share **one stone trim sheet and one brass trim sheet** (2048² each), with trim strips laid out for mouldings, block courses and bands at about 256 px/m.
  - Keep UV islands straight on architectural trims.
- **Textures** (PNG): albedo, normal (OpenGL convention, imported as normal map), metallic/smoothness (R = metallic, A = smoothness), emission where needed, and baked AO multiplied into the albedo.
  - Paint in the house style: broad planar value shifts, chipped edges, soft AO, hand-painted colour breakup. No photo textures.
  - Bake AO, curvature and any high-to-low detail in Blender (Cycles bake).
- **Materials in Unity:** URP Lit, one material per texture set, named `M_Area4_<Asset>`. Keep the existing material names where the runtime script uses them (the floor emission material and the bell glow material are wired into `ShatteredHourArena`).
- **LOD:** LOD1 at 50% for rocks, chains, pinnacles and ruins. Hero assets don't need LODs.
- **Colliders:** none on scenery. Only the floor disc and the boundary ring keep colliders.

## Integration in Unity

1. Import each FBX into `Area4/Models/<Asset>/` with a small `AssetPostprocessor` in `Area4/Editor/`: scale 1, generate lightmap UVs off, normals "Import", materials "None" (assign the Unity materials yourself).
2. Make a prefab per asset in `Area4/Prefabs/Parts/`.
3. Change `ShatteredHourArenaBuilder` so `Build()` **places prefabs** at the exact transforms the procedural code used (rim modules every 15° around the origin, pinnacles and lanterns at the same angles, and so on).
   - Remove the mesh-generation code that is no longer used. Keep only what is still needed: floor texture painting, boundary colliders, lighting, shaders, cameras, stats and screenshots.
   - Keep static-mesh consolidation for draw calls, merging placed prefab meshes per material where it makes sense.
4. Keep the occluder fade on the tall camera-side pieces: south pinnacles and gatehouses.
5. Keep `BatchBuildAndCapture` and all screenshots:
   - `gameplay`, `establishing`, `topdown`, `south-entrance`, `bell-closeup`, `station-closeup`, `telegraph-test`, `eight-players`, and `stats.txt`
   - plus a new `turntables/` folder with one Blender render per asset
6. Budget from the gameplay camera: **500k triangles or fewer**, **200 draw calls or fewer** (before batching), one shadow-casting light, at most 10 point lights, textures at most 2048² (the floor albedo stays at 4096²).

## Process

1. **Plan.** Write `prototype/arena-3d/areas/area-4/HAND-MODEL-PLAN.md`: the asset list with order, texture sets and budgets.
2. **Trim sheets first:** the stone and brass trim sheets, each with a test plane and a short wall section rendered in Blender.
3. **One asset at a time**, in this order:
   1. pinnacle (to prove the pipeline end-to-end into Unity)
   2. parapet and drum modules
   3. rim station
   4. bell
   5. astrolabe
   6. entrances
   7. ruins
   8. rocks
   9. chains and hanging bells
   10. side rings
   11. lanterns

   For each asset:
   - **Blockout:** primary shapes only. Render it at gameplay distance and check the silhouette against the sheet.
   - **Shape and detail:** secondary forms, bevels, damage.
   - **UV, bake and paint**, then export.
   - **Place in Unity,** run the batch build, and check the gameplay and close-up shots.
   - **Commit** the code and small files. Add the binaries to the next asset-pack version.
4. After the pinnacle works end-to-end, show the user its Blender turntable and its in-game shot before you continue. This is the quick mockup before heavy work, the same way earlier passes did it.
5. **Critic loop** once all assets are in:
   - Spawn an independent critic subagent with the plan, the mood image, `gpt/images/10-arena-shattered-hour.png`, `gpt/02-visual-style-guide.md`, this prompt, the newest shots and the turntables.
   - Ask for an overall score and sub-scores (layout, floor, rim/stations/entrances, bell/astrolabe, void props, lighting/colour, readability), plus ranked concrete fixes.
   - Tell it the intentional constraints: the fixed camera, the flat floor, dormant decoration, scenery walkways.
   - Refine and re-run **up to 4 times, or until it scores 8/10 or higher**.

## Deliverables

- One Blender source per asset under `areas/area-4/blender/`, with its scripts, published in an `area4-models-v<N>` asset pack.
- FBX files, textures, materials and prefabs under `Area4/Models/` and `Area4/Prefabs/Parts/`.
- The updated builder that places the models, with the menu **Last Bell › Areas › Area 4 – Shattered Hour (Rebuild)** still working and the run loop still building.
- The screenshots, turntables and `stats.txt`.
- A short report for the user, who reads on a phone, so keep it plain and brief:
  - critic scores for each round
  - how each asset was made in Blender
  - measured triangles and draw calls
  - anything that bent a rule and why
  - open questions
