# Prompt: Verdant Chimes detail pass (clouds, towers, moving water and lights)

Paste everything below the line into the agent that will do the work.

---

You are doing a **detail and atmosphere pass** on **The Verdant Chimes**, the Area 3 arena of *Last Bell*: a co-op 2.5D isometric boss game built in Unity URP, with Windblown and Hades as the quality bar. The arena already exists and works. Your job is to make it much richer. Chzy18 (the user) called out three problems, and those come first:

1. **The background towers and the clouds don't interact.** The towers, spires and aqueduct piers stand in the sea of mist like solid sticks, cut by flat cloud planes. They should **fade gradually the lower they go into the clouds**, so their bases melt into mist and only their tops stay crisp.
2. **The waterfalls look still.** They must visibly flow, in the editor and in Play mode.
3. **The little lights in the air (motes) and the water should move together** as one living system: sparkles riding down the falls, mist motes rising from the impact pools, and everything drifting on the same wind.

After those, raise the general detail level (§5).

## 0. Read first

| What | Path (from `last bell/`) |
|---|---|
| Settled design rules (they override everything else) | `gpt/08-design-rules.md`, root `AGENTS.md` |
| Art source of truth | `gpt/README.md`, `gpt/02-visual-style-guide.md`, `gpt/05-arenas.md`, `gpt/images/09-arena-verdant-chimes.png` |
| The arena's original brief | `unity-session/prototype/arena-3d/areas/area-3/VERDANT-CHIMES-UNITY-PROMPT.md` |
| Top-down layout target | `unity-session/prototype/arena-3d/areas/area-3/reference/verdant-chimes-topdown.webp` |
| Windblown method (fog, sea of clouds, wind, fake lights) | `research/windblown-artstyle.html` §5, §6, §9 |
| How it is built now | `unity-session/prototype/arena-3d/areas/area-3/README.md` |
| Latest screenshots | `unity-session/prototype/arena-3d/areas/area-3/shots/unity-r5-*.png`, `unity-r6-readability.png` |

## 1. How the arena is built today (don't rebuild it, extend it)

- **Geometry** is generated in three.js by `prototype/arena-3d/areas/area-3/verdant-arena.js` (preview: `verdant-arena.html?view=game|angle|top`). `export-glb.mjs` writes `VerdantChimes.glb` plus the 4K floor textures to `unity/Assets/LastBell/Areas/Area3/Source/`.
- **Unity** lives in `unity/Assets/LastBell/Areas/Area3/`. `Editor/VerdantChimesBuilder.cs` builds the scene from the glb. Use the menu **Last Bell > Area 3 > Rebuild Verdant Chimes**, or batch mode via `BatchBuildAndCapture`, which saves screenshots to `Area3/Screenshots/`.
- **Shaders** are in `Area3/Shaders/`:
  - `CustomLit` (Windblown lighting, driven by `_LB_*` globals from `LookSettings` / `Area3_VerdantChimes_Look.asset`)
  - `Waterfall`
  - `Mist`: two flat planes at y = −33 and −27, fbm in world XZ
  - `Pool`: water at y = −36
  - `GlowBillboard`, `Sky`, `OutlineOverlay`
  - `ParticleField`: shader-animated quads for motes, petals and spray
- **Runtime:** `VerdantChimesArena.cs` sways the bells, turns the medallion gear and handles `quiet` (hazard mode dims decoration).
- **Known constraints:**
  - The **Particle System module is disabled** in this project. Don't enable it, because that edits the shared `Packages/manifest.json`. Keep using shader-driven particles (`ParticleField`) or extend them.
  - glTFast's material graphs don't compile in batch mode. The builder reads base colours from the glb JSON.
  - The user's Unity editor usually has the project open. Run batch builds in a scratch copy of the project, then copy `Area3/` back.
  - Background towers and spires are vertex-coloured rock (`Rock_vc` material, CustomLit). Aqueducts and piers use `Stone` / `StoneDark` with cut-stone detail.

## 2. Clouds and towers (top priority)

Goal: every tall object outside the arena reads as **rising out of a deep sea of cloud**. It is solid and detailed at the top, then gradually swallowed toward the base, with the boundary soft, wispy and slowly moving.

1. **Height cloud fog inside the lit shaders.** Add it to `CustomLit` and also to `Waterfall`, `Pool` and the stone/rock paths, so everything sinks the same way.
   - Factor: `cloud = smoothstep(_LB_CloudTop, _LB_CloudBottom, positionWS.y)`.
     - Suggested start: top −6 m, bottom −30 m.
     - Shape it with a power curve, so the fade starts gentle and gets thick near the bottom.
   - Break the boundary up with **the same fbm noise and scroll speed as the Mist shader**: sample `positionWS.xz * 0.03` plus time, and offset the effective height by ±3 m. The wisps on the towers should line up with the wisps in the cloud layer.
   - Blend toward the cloud colour. Lerp from the mist's thin colour (low density) to its thick colour (high density), so sunken parts take on the cloud's colour instead of a flat grey.
   - Keep the **combat floor and rim** (y ≥ −1 within 17 m of the centre) completely unaffected. Floor readability is a settled rule.
   - Put all values in `LookSettings` and expose them in the inspector: cloud top/bottom, density curve, colours, noise scale and speed.
2. **Soft mist layers instead of two hard planes.**
   - Replace the two mist planes with 5–6 stacked layers between about −10 and −36 m. Opacity rises with depth and each layer scrolls at its own speed.
   - Add **depth-fade soft intersection**: sample `_CameraDepthTexture` in the Mist shader and fade alpha over 2–4 m where a layer meets geometry. Pillars should never show a hard horizontal cut line.
3. **Cloud cards around tower bases.**
   - Add camera-facing soft cloud billboards (a new `CloudCard` shader, like Windblown's `S_Li_FogPlane`) hugging the bases of the spires, aqueduct piers, belfry bastions and bridge piers.
   - Use 3–6 per object, slowly drifting and gently pulsing, lit teal from below and a little warm on top.
   - Use soft depth fade so they never clip hard.
4. **Distance haze.**
   - Add aerial perspective on top of the height fog: the far ring (40–120 m) desaturates and lifts toward the horizon colour.
   - The nearest towers stay crisp.
   - Match the sky's horizon colour exactly, so there is no visible horizon seam.
5. **Light through cloud.**
   - Give the cloud sea a soft brightness gradient: brighter where the sun direction hits and near waterfall impacts, darker under the platform.
   - Optionally, add 3–5 faint god-ray cards slanting down into the cloud on the sun side. Keep them outside the combat floor.

Accept when, from the gameplay camera, every background tower visibly fades into cloud over at least 15–20 m of height, with no hard cut line anywhere. The fade boundary must visibly drift over a few seconds.

## 3. Waterfalls that flow

1. **Make motion work in the editor too.**
   - URP only advances `_Time` when the view repaints. Add an editor helper, or extend `VerdantChimesArena` with an `[ExecuteAlways]` toggle **Animate In Editor**. When it's on, it requests continuous Scene and Game view repaints (for example `EditorApplication.QueuePlayerLoopUpdate()` and `SceneView.RepaintAll()` on `EditorApplication.update`).
   - Document that Scene view **Always Refresh** must also be on.
   - Verify in Play mode as well.
2. **One flow speed for every sheet.**
   - Drive the streak scroll from **world-space height**, not the mesh UV repeat. The fixed `_Rep` makes long and short sheets flow at different speeds.
   - Target a believable visual speed, about 5–8 m/s near the lip, accelerating slightly lower down.
3. **Readable movement:**
   - Two or three streak layers at different speeds and widths.
   - Bright foam bands that travel down the sheet.
   - Slight sideways wobble.
   - A thicker, churning foam lip at the top.
   - Thinning and breaking into spray near the bottom, then dissolving into the cloud fog from §2.
   - Raise contrast between streaks and body so motion reads from the high camera.
4. **Shape:** sheets should arc outward from the lip, not hang as flat panels. Add slight thickness or a second offset sheet for depth.
5. **Impact:** where falls meet the cloud sea or pools, add animated foam rings and splash on the `Pool` shader. Add a rising spray plume of soft particles, using the same flow timing.

## 4. Lights and water moving together

1. **Shared drivers.** Add globals pushed by `LookSettings`:
   - `_LB_Wind` (direction, strength and a gust phase)
   - `_LB_FlowSpeed`

   Waterfalls, motes, petals, spray, ivy, flowers, tree canopies and cloud wisps all read these, so gusts move through everything together (Windblown teardown §5: one global wind texture).
2. **Waterfall sparkles.**
   - Add a new `ParticleField` mode: small bright glints spawned along each waterfall sheet that travel **down at exactly the water's flow speed** and fade near the bottom.
   - Use 20–40 per sheet. Make them warm-white near the lip and turquoise lower down.
3. **Rising mist motes.**
   - Add soft turquoise motes that lift slowly from each impact plume and pool, drift with the wind, and fade as they climb.
   - They should feel like the waterfall's breath, not random dust.
4. **Arena motes:**
   - Keep the existing motes near the rim.
   - Make them drift on `_LB_Wind`, swirl gently around the waterfalls, and gather slightly near the falls and belfry bells.
   - Keep them sparse over the combat floor, and dimmed by `quiet` (already wired through `_LB_DecoGlow`).
5. **Fake lights.** Have a few motes near lanterns and bells pick up warm colour, as if lit by them. Lantern glows pulse softly in sync with the nearest bell's sway.

## 5. More detail everywhere else

Keep it stylised and readable: the Windblown method, not photoreal.

- **Background towers and spires:**
  - Break up the stacked-cylinder look with irregular taper, overhangs, ledges, cracks, broken crowns and roots or vines hanging down the faces.
  - Add small ruins on top: walls, arches, a lantern and tree canopies.
  - Vary the silhouettes so no two look alike.
- **Aqueducts:**
  - Keystones, cornices and broken spans with fallen blocks.
  - Water channels on top that spill into the waterfalls.
  - Moss caps and ivy curtains.
  - Heights that vary in steps.
- **Belfries:**
  - Cracked masonry, timber bracing, a rope or chain on each bell.
  - Pigeons or bats as tiny animated cards (optional).
  - Lilies and ivy spilling from the broken tops.
- **Rim and bridges:**
  - Denser clumped foliage and hanging vines (1–6 m).
  - White lily clusters, fallen petals, small ferns between curb blocks.
  - Moss in every joint of the outer curb face.
  - Keep the playable floor (r < 14 m) clear and flat.
- **Foliage:**
  - Replace round blobs with clumped painterly leaf cards that sway on `_LB_Wind`.
  - Canopies should be layered clusters with darker undersides and lit tops.
- **Water far below:** visible turquoise pools between cloud gaps, foam where falls land, slow caustic shimmer.
- **Atmosphere:** drifting petals on the wind, a slow birds loop in the far distance (optional), light shafts on the sun side.
- **Performance budget:**
  - 60 fps at 1440p
  - ≤ 1.5 M triangles in view
  - ≤ 6 real lights; decorative lights stay glow billboards
  - Use GPU instancing for repeated foliage and cloud cards

## 6. Rules you must keep

- `gpt/08-design-rules.md` wins over everything here:
  - Decorative floor art must never look like a hazard.
  - Hostile telegraphs are coral or amber with crisp edges.
  - The camera is the fixed high-oblique game camera: (0, 29, −22.5), FOV 44, looking at (0, 0, −1).
- **Readability:** cloud fog, motes and glows must not affect the combat floor's contrast. With `quiet` on, everything decorative dims. Re-run the readability capture (coral discs and teal rings on stone and on the mosaic) and check nothing got worse.
- **No boss mechanics or hazards.** This is a look pass only.
- **Files:**
  - Work only in `unity/Assets/LastBell/Areas/Area3/` and `prototype/arena-3d/areas/area-3/`.
  - Don't edit shared project settings, `Packages/manifest.json`, other areas, characters or the Bellkeeper arena.
  - Ask before touching anything shared.
- **Large files:** new or updated big binaries (the glb and PNGs over 1 MB) go into a versioned **asset pack** on GitHub Releases (`asset-packs.json`, `tools/asset-packs/New-AssetPack.ps1`, `docs/asset-packs.md`, PR #67), not into Git. Bump the arena pack's version rather than replacing a published one.
- **Git:** checkout `unity-session` on `feature/class-test-arena`. Stage only your own files by path. Commits end with the project's attribution line.

## 7. Process

1. Prove the cloud fade and the flowing water first: a quick test with one tower, one waterfall and the mist. Send the user screenshots **and a short frame sequence** (for example 6 frames 0.25 s apart, or a GIF) showing the motion, before the full pass.
2. Then do the full pass: clouds and towers → water flow → lights and wind → general detail.
3. Rebuild via `BatchBuildAndCapture`. Add to the capture:
   - a **"cloud" view**: a lower angle looking across the background towers into the mist
   - a **motion strip**: the same view at t = 0, 0.5, 1.0, 1.5 s, so a reviewer can see the water and motes moving
4. Review loop: have a critic agent rate the result out of 10 against this prompt, `VERDANT-CHIMES-UNITY-PROMPT.md` and the reference. Refine and re-run up to 4 times, or until it scores 8 or more.

## 8. Done when

- [ ] Every background tower, spire, pier and bastion fades gradually into the cloud sea, over at least 15–20 m. No hard cut lines, the boundary drifts, and the colours match the mist.
- [ ] The mist is layered, soft-edged against geometry, and has cloud cards around tower bases.
- [ ] Waterfalls visibly flow in the editor (with Animate In Editor on) and in Play mode, at one consistent speed, with travelling foam, an arcing shape, impact foam and spray.
- [ ] Glints ride down the falls at the water's speed. Mist motes rise from impacts. Motes, petals, foliage, spray and cloud wisps all move on the same wind.
- [ ] Clearly more detail on towers, aqueducts, belfries, rim and foliage, and the combat floor stays flat, clear and readable (the readability capture passes with `quiet` on).
- [ ] Inside budget; large binaries shipped as an asset pack; screenshots, a motion strip and the critic score sent to the user.
