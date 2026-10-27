# Prompt: hand-model the Verdant Chimes environment in Blender

Paste everything below the line into the agent that will do the work.

---

You are replacing the **code-generated geometry** of **The Verdant Chimes** (Area 3 of *Last Bell*, a co-op 2.5D isometric boss game in Unity URP, quality bar Windblown / Hades) with **hand-modelled environment art built in Blender 5.2**.

Today every rock, tower, aqueduct, belfry, tree and vine is assembled from primitives in `verdant-arena.js` (cylinders, noise-displaced lathes, extruded arch outlines, icosahedron leaves). Five critic rounds topped out at 7.5/10. The remaining complaints are all about the art itself:
- the forms look procedural
- the silhouettes repeat
- the rock and foliage are blobs
- the scene has no value depth

The way past that is real modelling, so treat this as an art job, not a code job.

## 0. What "hand-modelled" means here (read this first)

You drive Blender through its Python API (`blender -b -P script.py`) and, where it helps, through the Blender window with computer use. That is fine: the user wants authored assets, not a ban on scripts. **The result must be modelled like an artist would**, not generated:
- Every asset is a deliberate, individually shaped mesh, built by placing and editing vertices, edge loops and faces, bevels, extrusions, sculpt strokes and booleans with purpose. No "cylinder plus noise" or random instancing to fake a form.
- Each asset has a clear, designed silhouette that reads from the high game camera. Variations are separately authored, or edited from a base, never random seeds.
- Rock is sculpted: planes cut with the Clay Strips, Scrape and Pinch brushes, with chunky stylised facets in the Windblown or Hades style, then remeshed or decimated to game resolution.
- Organic forms (roots, vines, canopies) are modelled from curves and cards placed by hand along the form, not scattered.
- Textures are hand-painted or baked from the sculpt: albedo with planned light/dark values, plus an AO and curvature bake. No noise textures standing in for detail.
- Before building each asset, look at the reference image and say in one line what you are matching.

**Show work early:** render a turntable or a three-quarter view of each asset family as soon as it exists, and send it to the user before moving on.

## 1. Read first

| What | Path (from `last bell/`) |
|---|---|
| Settled design rules (override everything) | `gpt/08-design-rules.md`, root `AGENTS.md` |
| Art source of truth | `gpt/README.md`, `gpt/02-visual-style-guide.md`, `gpt/05-arenas.md`, `gpt/images/09-arena-verdant-chimes.png`, `gpt/images/01-key-art-style-board.png` |
| Layout target (top-down) | `unity-session/prototype/arena-3d/areas/area-3/reference/verdant-chimes-topdown.webp` |
| Arena brief and detail brief | `.../area-3/VERDANT-CHIMES-UNITY-PROMPT.md`, `.../area-3/VERDANT-CHIMES-DETAIL-PASS-PROMPT.md` |
| Windblown method | `research/windblown-artstyle.html`: flat low-detail albedo with planned values; the lighting makes the form (§1, "Art pipeline notes") |
| Current build and its screenshots | `.../area-3/README.md`, `.../area-3/shots/unity-d8-angle.png`, `unity-d7-*.png` |
| Existing Blender pipeline to copy patterns from | `unity-session/prototype/arena-3d/models/blender/*.py`, `models/build-vanguard.ps1` (headless Blender, glb export) |
| Blender | `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` |

## 2. What stays, and what you replace

**Keep, don't change:**
- **The combat floor:** the disc, its 4K painted textures (rings, cross, mosaic) and its colliders. It is gameplay-critical and already approved. The floor plane stays exactly at y = 0 with radius 14.6 m, and the playable radius is 14 m.
- **The layout:** every position and footprint in `verdant-arena.js` (the rim, four bridges, the long south entry bridge, four belfries on the diagonals, waterfall positions, aqueduct and spire placement). Use it as the placement map.
- **All Unity-side systems:**
  - shaders: CustomLit, cloud fog, mist layers, waterfall, pool, particles
  - `LookSettings`
  - `VerdantChimesArena.cs`: bells, gear, quiet mode, wind
  - the camera, the readability rules and the run-loop entry point `VerdantChimesBuilder.Build` (`Run/Editor/RunBuild.cs` calls it)
- **Waterfall sheets:** they stay shader cards. You may model nicer sheet meshes (curved, thick lip) if you keep the UV convention: v = 1 at the lip, and a `FallLip` empty at each lip.

**Replace with hand-modelled assets:** everything else that is solid geometry (§3).

## 3. Asset list (a modular kit, then placement)

Build a **kit** of reusable, hand-made pieces, then place them in a Blender **layout scene** that matches the current layout. Sizes are in metres; Blender is Z-up, so export with Y-up for Unity.

| Family | Pieces (each hand-made) | Notes |
|---|---|---|
| **Platform** | rim curb blocks (4 variants: whole, cracked, sunk, broken corner); the brass band; the drum wall under the curb as segments with stone courses; 6 under-rim arcade bays (round arch, broken arch, collapsed); the rock root under the platform | Curb top at 0.45–0.6 m; the curb must not cover the floor inside 14.6 m |
| **Bridges** | deck segments; parapet pieces (whole and broken); entry-bridge posts with brass caps and lantern brackets; arch supports and piers; N/E/W ruined gothic gate arches | Keep the deck at y = 0, 4 m wide; the entry bridge is 21 m long |
| **Belfries** | 2 tall (NE/NW, 7–9 m) and 2 short (SE/SW, ≤ 5 m) towers: square stone shaft, pointed open arch facing the arena, broken crown and merlons, timber beam and bracing, chain; the **bell** as its own object named `Bell` (pivot at the hang point; Unity sways it); the rock bastion under each | Short camera-side towers must never hide a player on the floor |
| **Rock** | 6 to 8 distinct sculpted spires and cliffs: overhangs, ledges, cracks, a broken crown, a different silhouette each; 3 bastion rocks; 3 floating islands; 10 or more boulders and rubble | Chunky stylised planes, not noise; the top 30% gets a moss cap |
| **Aqueducts** | arch bay (round), arch bay (pointed), broken bay, a second-tier bay, pier with capital and keystones, cornice strips, a water channel on top with a spill lip, fallen blocks | Vary heights in steps; channels spill into the waterfalls |
| **Foliage** | 3 tree types (tall broadleaf, leaning, small shrub) with hand-placed leaf-card clusters (darker undersides, lit tops); ivy curtains (4 lengths: 1, 2.5, 4 and 6 m); fern and grass cards; white lily clusters; moss caps and moss mats shaped to the stone they sit on | Leaf and flower cards use alpha-cut painted textures; vertex colour R = sway weight for the existing wind |
| **Props** | lanterns (bronze cage, amber core) as objects named `Lantern`; the medallion gear named `MedallionGear` | Keep these names: Unity code finds them |

**Look targets for every piece:**
- Silhouette first. The high three-quarter game camera sees mostly tops and outer faces, so put the detail there.
- Bevel or chamfer every cut-stone edge so the light catches it.
- Stones are slightly irregular, never perfect boxes.
- Masonry has readable courses, keystones and chips.
- Moss and ivy hug the forms they grow on, thickest on upward-facing ledges and in joints.
- Palette, from the art guide: warm grey stone (#B9B3A6 to #9E998D), aged brass (#B0893F), deep teal shadows, mid-saturation greens, white lilies. **Value depth matters:** darker rock bases and undersides, lighter tops.

## 4. Technical spec

**Topology:**
- Clean, game-ready meshes.
- Hard-surface pieces are low to mid poly with bevels: 300–3,000 triangles per kit piece.
- Rocks are sculpted, then decimated or retopologised: 2,000–8,000 triangles for hero spires, 300–1,500 for boulders.
- Whole scene: ≤ 1.5 M triangles in view.

**UVs:**
- Every mesh is UV-unwrapped with no stretching and sensible seams.
- Share **trim sheets and atlases** between kit pieces: one stone atlas, one rock, one foliage, one metal.

**Textures:**
- 2K atlases, hand-painted or baked: albedo with planned values, plus a baked AO and curvature mask.
- Albedo stays fairly flat and low-detail. The Windblown method lets the CustomLit shader make the form.
- Foliage cards: albedo with alpha.

**Vertex colours:**
- R = wind sway weight on foliage (0 at the root, 1 at the tips).
- On rock and stone, an optional G = moss mask.

**Naming:**
- Every object gets a clear name and a material named for its role: `Stone`, `StoneDark`, `Rock`, `Moss`, `Ivy`, `CanopyLeaf`, `Brass`, `Bell`, `Timber`, `LanternMetal`, `Flower`, `Fern`, `GlowAmber`, and so on.
- The Unity builder remaps materials **by name** to the LastBell CustomLit shader. If you add a new material role, add its case in `VerdantChimesBuilder.DressMaterials`.
- Keep the empties and objects the code looks for: `FallLip` (one per waterfall), `Lantern`, `Bell`, `MedallionGear`, `BossAnchor`, `PlayerSpawns` with `Spawn1`–`Spawn8`, and `EntryPoint`. Positions must match the current build.

**Export:**
- Use a headless build script, `prototype/arena-3d/areas/area-3/blender/build-verdant.py`, run by `build-verdant.ps1`. It opens the `.blend`, applies modifiers, and exports **one glb with Y-up**: `VerdantChimes_Art.glb`. Include textures, and use instancing where the same kit piece repeats.
- Save the source as `areas/area-3/blender/verdant_chimes.blend`, with the kit in its own collection and the layout in another.

**Unity:**
- Add a source switch to `VerdantChimesBuilder`: if `Source/VerdantChimes_Art.glb` exists, use it for the solid environment. Keep the floor from the current pipeline, and keep the code-generated version as a fallback.
- Map the glb's textures into CustomLit (`_BaseMap`), and use vertex colour R as `_Sway`.
- Waterfalls, mist, clouds, particles and glows keep coming from the existing builder steps.
- Run `BatchBuildAndCapture` in a scratch copy of the project. The user's editor usually has the real project open.

## 5. Rules you must keep

- `gpt/08-design-rules.md` wins:
  - The combat floor stays flat, clear and readable.
  - Decorative art never looks like a hazard.
  - The camera is fixed: (0, 29, −22.5), FOV 44, looking at (0, 0, −1).
- Re-run the readability capture with `quiet` on: coral telegraphs and teal rings on the stone and on the mosaic. Nothing may get worse.
- **No boss mechanics.** This is environment art only.
- **Files:**
  - Work only in `prototype/arena-3d/areas/area-3/` and `unity/Assets/LastBell/Areas/Area3/`.
  - Don't touch other areas, characters, the Bellkeeper arena, shared project settings or `Packages/manifest.json`.
  - Ask before anything shared.
- **Large files** (`.blend`, glb, textures over 1 MB) go in a versioned **asset pack** (`asset-packs.json`, `tools/asset-packs/New-AssetPack.ps1`, PR #67), not Git. Ask the user before publishing a release, because it's public.
- **Git:** checkout `unity-session` on `feature/class-test-arena`. Stage only your own files by path. Commits end with the project's attribution line.

## 6. Process

1. **Style test (stop and show).** Build three hero pieces first:
   - one sculpted rock spire
   - one belfry with its bell
   - one aqueduct bay with ivy

   Render each in Blender from the game camera angle and a three-quarter view, under a sun matching `LookSettings`. Put them in Unity next to the current procedural versions. Send the user before/after images and **wait for a yes** before building the rest.
2. Build the kit family by family, in the order of §3. Send a contact sheet of each family when it's done.
3. Build the layout scene, matching every position in the current build, then export.
4. Integrate in Unity: rebuild, capture, then check readability and the triangle budget.
5. **Review loop:** have a critic agent rate the result out of 10 against this prompt, the two Area 3 briefs and the reference. Compare it with `unity-d8-angle.png`, the last procedural build, which scored 7.5. Refine and re-run up to 4 times, or until it scores 8 or more.

## 7. Done when

- [ ] Every solid environment object is a hand-modelled, UV-mapped, textured asset from the Blender kit. No procedural primitives remain except the floor disc, waterfall cards and atmosphere.
- [ ] Silhouettes are varied and designed: no two spires alike, aqueducts not cloned, foliage reads as leaves.
- [ ] The scene has clear value depth: dark bases and lit tops against the pale cloud sea.
- [ ] All code-facing names and positions are preserved, so the bells sway, lanterns glow, waterfalls get their effects and markers work. The run loop still builds this map.
- [ ] Readability capture passes, and the scene is inside the triangle budget.
- [ ] The `.blend`, glb and textures are delivered (asset pack once the user agrees); the build script is in Git; before/after screenshots and the critic score are sent to the user.
