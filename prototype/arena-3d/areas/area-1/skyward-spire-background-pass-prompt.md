# Work order: Skyward Spire background pass (resolution, detail, towers fading into cloud)

Hand this whole file to the Area 1 session (or any agent working on Area 1). Run it end to end, then report with before/after images.

---

## Goal

Make everything around the Area 1 combat floor look higher-resolution, more detailed and more natural. The most important part: **where towers meet clouds, they must fade out of sight.** Today a tower shaft runs down into a flat cloud card and stops at a hard edge. It should dissolve gradually into mist and cloud, paler and bluer with depth, until it's gone, the way `reference-topdown.webp` shows it.

Leave the combat floor, the bridges' walkable decks, the colliders and gameplay readability as they are.

## Where things stand

- **Scene:** `unity/Assets/LastBell/Areas/Area1/SkywardSpire_Arena.unity` (Unity 6000.6.6f1, URP). Built by `Editor/SkywardSpireBuilder.cs` (menu **Last Bell → Rebuild Area 1**; batch method `BatchBuildAndCapture`). Runtime: `SkywardSpireArena.cs`, where **T** toggles the gameplay and top-down views.
- **Geometry:** authored in `prototype/arena-3d/areas/area-1/skyward-spire-env.js`, exported by `export-glb.mjs` to `SkywardSpire.glb` and imported by glTFast.
  - Top-level groups: `Floor`, `Rim`, `Bridges`, `Belfries`, `Spires`, `Abyss`.
  - Camera-side groups are named `Occluder…` and hidden in the gameplay view.
  - Static meshes are merged per material, and the material names are stable: `IvoryBlocks`, `IvoryShade`, `Ivory`, `Gold`, `GoldDark`, `Cobalt`, `StarWindow`, `Banner`, `ArchShadow`, `BridgeDeck`, `Floor`.
- **Clouds:** about 300 camera-facing quads using `Arenas/Shaders/CloudBillboard.shader` with one 256 px puff texture (`Generated/SkyCloudPuff.png`), plus a flat blue `SkyBelow` quad at y −75.
- **Tower shafts:** they extend to about y −30…−40 with no fade. Fog is linear and global; the top-down view borrows a 0–700 m range.
- **Score:** the last critic rated the top-down view 6/10 (atmosphere 4, buildings 5). Its specific complaints:
  - flat white fog cards with hard horizontal edges at tower bases
  - towers too saturated all the way down
  - background reads low-res and repetitive (same window everywhere, plain shafts)
- **Lock:** the user's Unity editor usually has `unity/` open, which locks it. Build and capture on a scratch copy of the project in batch mode (robocopy without `Temp`, `Logs`, `UserSettings`). Copy only `Assets/LastBell/Areas/**` back when it's done.

## Required work

### 1. Towers that fade out of sight (top priority)

- Write a URP shader, `Areas/Area1/Shaders/SkyArchitecture.shader` (HLSL, or Shader Graph saved in the Area1 folder), for every background building material. It does standard PBR lit shading plus three things:
  - **Height fade toward cloud colour.** Below `_FadeTop` (about y −4), blend albedo and lighting toward the cloud tint (`#E8EEF8` near the top, `#B9CBE6` deeper) with a smooth curve. The blend should reach about 85% by `_FadeBottom` (about y −28).
  - **Dithered or alpha dissolve near the bottom.** Fade to fully invisible by `_VanishY` (about y −36) using screen-space dither (opaque, no sorting problems) and a little world-space noise. That way the edge breaks up like mist instead of a straight line.
  - **Aerial perspective.** Using distance from the floor centre and depth below the floor, shift the colour toward pale sky blue and lower contrast, so far towers read paler than near ones.
- In the builder, swap the glTF materials on everything under `Belfries`, `Spires`, `Abyss` and the `Rim` underside for SkyArchitecture versions. Keep each material's base map, colour, metallic and smoothness. Don't touch `Floor`, `BridgeDeck` or anything above y −1 on the rim and bridges.
- Bridge piers and the hanging pinnacles under the dais use the same fade.
- Tune it so that from the top-down view, no tower shows a hard bottom edge or a hard line where it crosses a cloud.

### 2. Clouds that wrap around towers (soft intersections)

- Make soft-particle clouds. Either extend a copy of the billboard shader into `Areas/Area1/Shaders/SkyCloud.shader`, or override it, but **don't edit `Arenas/Shaders/CloudBillboard.shader`**: it belongs to the Bellkeeper arena. The shader should:
  - sample the camera depth texture and fade cloud alpha where it nears opaque geometry, over about 3–6 m, so clouds never cut a straight line across a tower
  - turn on `requiresDepthTexture` per camera through `UniversalAdditionalCameraData`. **Don't change the shared URP assets.**
- Light the clouds:
  - a bright top and sun side (`#FFFFFF`)
  - a soft lavender-blue underside (`#B9CBE6`)
  - a thin warm rim facing the sun
  - a vertical gradient baked into the puff texture, plus a sun-direction term in the shader
- Build a puff atlas at 1024² with 6–8 distinct cumulus shapes: billowy tops, flatter bottoms, soft fbm edges. It replaces the single 256 px puff.
- Lay the clouds out in three layers:
  - **Deep sea (y −45…−70):** large, dense banks with sky-blue gaps (deep sky `#4F8FD6` to `#9CC4EE`), as in the bottom half of the reference.
  - **Waist band (y −14…−32):** clusters of 4–10 puffs spawned around each tower and pier at its fade height, so every tower visibly sinks into its own cloud. This is the key fix. Give larger towers bigger clusters.
  - **Wisps (y −4…−10):** a few thin, semi-transparent strands drifting between towers. Keep them out of the gameplay view's floor area.
- Add slow motion: drift, a slight scale "breathing" (±3%) and opacity variation. Update `SkywardSpireArena.cs` to match.
- Optional, if time allows: replace the flat `SkyBelow` quad with a large cloud-sea plane under the deep layer, using scrolling fbm noise. It fills the gaps without a hard horizon.

### 3. Higher resolution and detail on the background

- **Textures:**
  - Ivory ashlar at 1024² with subtle per-block variation and edge wear.
  - Window texture at 256×512 as tall lancets with tracery: a central mullion, a trefoil head, gold leading and a smaller gold star. Add 2–3 window variants so facades don't look like wallpaper.
  - Banner at 256×1024.
  - Keep the floor at 3072 as it is.
- **Geometry, landmark towers** (`Armillary_NW`/`NE`/`SW`, `Belfry_W`/`E`, `Cathedral_*`, rotundas):
  - corner pinnacles with crockets
  - gold string-courses every storey
  - blind arcades between windows
  - flying buttresses on the cathedral masses
  - 3–5 needle spires on each cathedral roof
  - gold finials and orbs
  - fewer, taller windows per storey
  - Raise cylinder segment counts to 24–32 on anything near the floor.
- **Variety:** no two filler spires identical. Mix cone, gold-dome and cobalt-dome tops, heights and widths. Add a few broken or slender silhouettes for rhythm.
- **Distant skyline (60–200 m out):** a ring of simplified spire clusters and floating cathedral islands with strong aerial perspective (pale and blue). They should give depth past the near ring without competing with it.
- **Performance:** add LODs to filler and distant spires and keep draw calls reasonable. Target ≥ 60 fps at 1080p on a mid-range GPU in the gameplay view. Re-check the GLB size: the current one is about 54 MB. Use JPEG for colour textures if needed.

### 4. Keep in sync

- Mirror the geometry changes in `skyward-spire-env.js`, so the browser look page and the GLB stay the source of truth. Shader-only effects (fade, soft clouds) live in Unity. A rough fog or fade approximation in the browser is fine.
- Update both READMEs (`prototype/arena-3d/areas/area-1/README.md` and `unity/Assets/LastBell/Areas/Area1/README.md`).

## Must hold

- The combat floor stays flat, clean and untouched, and decorative gold stays quieter than warnings. Re-run the telegraph check capture.
- The gameplay view never loses floor to background geometry or clouds. Wisps stay outside the floor area in that view.
- No changes to other sessions' files: Bellkeeper arena files, `CloudBillboard.shader`, the shared URP assets, characters, `index.html`.
- Settled design rules in `gpt/08-design-rules.md` don't apply to pure background art, but don't invent boss mechanics.

## Verification

1. Capture before and after for:
   - `topdown.png`
   - `gameplay.png`
   - `gameplay-telegraphs.png`
   - a new `abyss-closeup.png`: a camera about 25 m out, looking down at 45° across a tower base and its clouds
2. Run a critic agent each round, using the earlier round-4/5 critic prompt and its measurement rule. Add this criterion: *"Do towers fade naturally into cloud, with no hard edges where cloud and tower meet?"* Target an overall score of ≥ 8/10 and atmosphere ≥ 7. Refine and re-run up to 4 times, or stop when the target is met.
3. Make a side-by-side of reference / before / after (top-down) plus the abyss close-up, and send it to the user.
4. Commit only Area 1 files and push to `feature/class-test-arena` (pushing is OK for Last Bell work).
