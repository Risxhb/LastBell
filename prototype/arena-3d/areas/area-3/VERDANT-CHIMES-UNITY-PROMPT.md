# Build prompt: The Verdant Chimes arena in Unity (Last Bell, Area 3)

Paste everything below the line into the agent that will build the scene.

---

You are building **The Verdant Chimes**, the Area 3 arena of *Last Bell*, as a Unity URP scene. Last Bell is a co-op (up to 8 players) 2.5D isometric boss-fight game. Windblown and Hades are the quality bar. This one arena hosts all four Area 3 fights (3 minibosses and 1 area boss). Those bosses are **not designed yet**, so build the space only: no boss, no mechanics, no hazards.

## 1. References (read these first)

| What | Path (repo root `unity-session/`) |
|---|---|
| **Primary layout: top-down concept (the target)** | `prototype/arena-3d/areas/area-3/reference/verdant-chimes-topdown.webp` |
| Mood and angle view, approved area board (panel 3) | `../Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png` |
| Area brief | `../Reference/LAST-BELL-ARENA-ART-HANDOFF/LAST-BELL-SHATTERED-HOUR-ARENA.md` (Area 3 row) |
| Art direction | `../Reference/LAST-BELL-CLOCKWORK-FANTASY-ART-DIRECTION.md` |
| Windblown style teardown (lighting model, LookSettings, outlines, fog, glow) | `../research/windblown-artstyle.html` |
| three.js look prototype (scale, camera, LOOK values, animation) | `prototype/arena-3d/areas/area-3/verdant-env.js`, `verdant-look.html` |
| Existing Unity arena to copy patterns from | `unity/Assets/LastBell/Arenas/Editor/KeyArtArenaBuilder.cs`, `unity/Assets/LastBell/Arenas/KeyArtArena.cs` |

The top-down concept wins over the board where they differ. It has a cross-and-rings brass floor, a turquoise mosaic centre, four bridges with a longer **entry bridge** to the south, ruined **belfries** with brass bells on the diagonals, and waterfalls pouring off every edge.

## 2. Project rules

- Unity **6000.6.6f1**, URP, project folder `unity-session/unity/`. Packages already installed: URP, glTFast, Input System.
- Put everything under `unity/Assets/LastBell/Areas/Area3/`. Scene: `VerdantChimes_Arena.unity`. Prefab root: `LB_ENV_VerdantChimes_Arena`.
- **Create new files only.** Don't edit the Bellkeeper/KeyArt arena, `index.html`, character folders or shared Core code. If a shared change is needed (for example a common LookSettings or CustomLit shader), stop and ask.
- Follow the KeyArt pattern: an editor builder `Areas/Area3/Editor/VerdantChimesBuilder.cs` with menu items **Last Bell > Area 3 > Open Verdant Chimes** and **Rebuild Verdant Chimes**. The scene must be rebuildable from code plus source assets. Add a runtime `VerdantChimesArena.cs` (`[ExecuteAlways]`) for the moving parts and the `quiet` flag.
- Batch builds work: `Unity.exe -batchmode -projectPath unity -executeMethod <Builder>.Build -quit`. The Hub's "New project" templates are broken; don't use them.
- Git: the checkout is `unity-session` on `feature/class-test-arena`. Don't switch branches, and stage only your files by path. Ask before pushing; the repo is public.

## 3. Coordinates, scale and camera

Units are metres. The arena centre is the world origin, and the floor top is `y = 0`. **North = +Z**. The entry bridge points **south (−Z)**, toward the camera.

| Element | Size |
|---|---|
| Combat floor disc | radius **14.6 m** (inside the curb) |
| Playable radius (boundary collider) | **14.0 m**: the same area as the Bellkeeper dais, so 8 players fit |
| Rim curb | 14.6–15.4 m, 0.45–0.6 m tall, with a brass band on its top outer edge |
| Turquoise mosaic disc | radius **5.8 m** |
| Inner brass ring (inside the mosaic) | radius 3.1 m |
| Centre compass-rose medallion | radius 1.9 m |
| Mid brass ring (on the grey stone) | radius 7.3 m |
| Outer brass ring | radius 14.2 m |
| Cross axes (brass strips N–S and E–W) | full diameter, 0.12 m wide, continuing down each bridge's centre line |
| Brass roundels | at every axis/ring crossing: radius 0.9 m on the outer ring (4), 0.7 m on the mid ring (4), 0.45 m where the axes meet the inner ring (4) |
| Bridges N, E, W | 4.0 m wide, 10–12 m long, ending at ruined gate-arches or tower stubs |
| Entry bridge S | 4.0 m wide, **18–22 m** long, with low parapets, brass-capped posts every ~3 m and lanterns on alternate posts |
| Belfries | 4, on the diagonals, centred at about 16.5 m radius just outside the rim; 7–9 m tall |

**Gameplay camera** (copy the prototype so all arenas match): perspective, **FOV 44° vertical**, position **(0, 29, −22.5)**, `LookAt(0, 0, −1)` (about 53° pitch). Also add an "angle" view for screenshots: FOV 40, position (−34, 30, −40), LookAt (1, −3, 2). Let `C` toggle between them, as `KeyArtArena` does. From the gameplay camera the whole floor must be visible, with the rim just inside the frame edges.

## 4. Combat floor (most important: it must stay readable)

- **Perfectly flat and unobstructed** inside 14 m. Nothing taller than 3 cm sits inside the playable radius. Brass inlays and roundels are flush, or raised 2 cm at most, with bevelled edges.
- **Stone:** large, worn, warm light-grey flagstones (around #B9B3A6 to #9E998D) laid in **concentric rings**, with slabs cut at irregular lengths (0.7–2.2 m), not a grid. Thin dark grout, and a few cracks and chipped corners. The middle of the floor is the cleanest; moss gathers in the grout and gets thicker toward the rim (outer 3 m). Use world-space peak/valley noise for gentle colour drift (Windblown teardown §4), not hand-painted variation.
- **Brass inlay:** aged gold (#B0893F, highlights #D9B866), slightly worn, with verdigris in recesses only. Rings, cross axes and roundels as in §3. Each roundel is a small engraved compass or gear disc.
- **Turquoise mosaic:** a disc of small square tiles (8–12 cm) in mixed teal-turquoise (#2E8C8A, #47A9A2, #6CC2B8, with a few darker #1F5F63), set in rings. The brass inner ring and spokes divide it into quarters. The centre medallion is brass: an 8-point compass star with a gear-tooth edge. It has a **faint** emissive turquoise in the grout lines only, max ~0.6 HDR, slow 4–6 s breathing pulse.
- **Readability rule:** telegraphs must always win. Keep floor decoration mid-value and low-saturation. While `quiet` is on (a hazard is up), the mosaic glow, motes and lantern pulse dim to ~35% within 0.25 s (boss design §8.1, as in `KeyArtArena`). **Flag to the user:** the turquoise centre could clash with teal player effects (Arcanist beams, Healer friendly rings). Keep it desaturated, and check it against them before calling it done.
- Floor material: one atlas or tiling set with an albedo, a normal map from a height map, and a mask (R = moss, G = brass, B = emissive grout). Bake the brass layout into a radial decal or mask texture (4K), not hundreds of meshes.

## 5. Rim, bridges and gateways

- **Curb:** cut-stone blocks with bevelled edges, some cracked or sunk, and a brass band along the top outer edge. Gaps for the four bridges. Ivy and moss spill over the outer face, with long vines hanging 1–6 m.
- **Flowers:** clusters of **white lilies / small white blossoms** along the rim, on the belfries and on the bridges, as in the top-down concept. Use these instead of the pink trees from the earlier mockup. They should be a soft accent, never bright enough to look like VFX.
- **Bridges:** stone decks on arched supports dropping into the abyss. Brass edge strips along both deck edges, and a brass centre line continuing the floor's cross axis. Low parapets with broken sections. Moss in the joints.
- **Entry bridge (S):** longer and more formal. Stone posts with small brass caps every ~3 m, hanging lanterns on alternate posts (warm amber glow), and a gate threshold where it meets the rim. Treat it as the players' **arrival path**. Whether it closes during a fight is a design decision you shouldn't make. Leave a disabled `EntryGate` collider in place and note it.
- **N, E and W bridges** end in ruined gothic gate-arches or tower stubs, 10–12 m out, overgrown, partly collapsed.

## 6. Overgrown belfries (diagonals)

Four ruined bell towers just outside the rim at NE, NW, SE and SW, each different:

- A square stone tower, 3 × 3 m footprint, 7–9 m tall, with a pointed gothic **open arch** on the side facing the arena. A **brass bell** (0.9–1.2 m tall, warm gold with darker patina, not green) hangs from a timber or iron beam inside.
- Broken tops: missing merlons and a cracked spire stub on one or two. Heavy ivy curtains, moss on ledges, white flowers.
- Extra broken arches and wall fragments bridging between the belfries and the rim, as in the concept, outside the playable radius.
- **Camera-side belfries (SE, SW) must not hide players.** Keep them shorter (≤ 5 m), or add Windblown-style see-through dithering for geometry between the camera and players (teardown §8). Shorter is the default.
- The bells sway gently (±3°, slow, out of phase).

## 7. Waterfalls, abyss and surroundings

- **Waterfall edge:** water pours off the platform rim between the bridges, and off the bridge arches, in wide sheets, 4–10 m wide each. It falls 30–40 m into **turquoise pools** with white foam and mist. Use a scrolling-noise water shader: a bright foam lip at the top, streaks, soft edges, fading into mist at the bottom. Add spray particle systems at the lips and at the base.
- **Below:** a deep teal abyss with glowing turquoise water far down. Add a layer of drifting **mist / sea of clouds** between −30 and −40 m (teardown §6: two scrolling noise layers lit teal, with soft depth fade).
- **Surroundings:** ruined **aqueduct arcades** (rows of tall stone arches) on rock pillars around the arena at 25–60 m, many with their own waterfalls. Mossy rock spires with dark-green tree canopies. Floating ruin fragments. Keep the camera-side (south) surroundings low, so nothing rises into the play view. Detail density drops with distance, and fog takes the far ring.
- **Life:** turquoise fireflies/motes over the floor edges, a few drifting white petals, birds optional. All of it dims with `quiet`.

## 8. Look: Windblown method (teardown §1–§9)

- **Lighting model:** build `SG_LB_CustomLit` (an Unlit Shader Graph with a Custom Function) from the HLSL in the teardown. It needs a remapped half-Lambert band, brush-noise terminator, a shadow tint colour, a fake top ambient, a backlight rim, and a metal LUT for brass. Every stone, wood, foliage and brass material uses it. If a shared version already exists elsewhere in the project, use it instead of making a copy, and ask before changing it.
- **LookSettings asset** `Area3_VerdantChimes_Look.asset` (ScriptableObject, applied by a `LookSettingsApplier`) holding every global. Starting values from the prototype's `LOOK`:
  - Remap min/max **0.38 / 0.66**, brush noise strength **0.10**
  - Shadow tint: cool teal-green, about #3E6A66
  - Top ambient **#5F9E86**, start 0.2, end 0.95, strength 0.6
  - Rim/backlight **#BFF5E8**, strength 0.42, tightness 3.2
- **Sun:** warm, soft (#FFE0B0, intensity ~2.2), from the upper left behind the arena (rotation about (55°, 135°, 0)), with soft shadows. Add a faint teal fill from the camera side (#5FD8D0, ~0.6).
- **Ambient / fog:** gradient ambient (sky #9CC8C4, ground #1B2624). Height + distance fog from teal #0F363B at 40 m to deep #0A2A30, blended over the sky so distant ruins melt into mist.
- **Outlines:** depth+normal edge pass (URP Full Screen Pass Renderer Feature). Ink #0C2422 at ~60% opacity, thin, fading out beyond 40–110 m. Lit surfaces only, not water, mist or glows.
- **Post:** ACES, mild bloom (threshold ~0.9, intensity ~0.45), a light grade (teal shadows, warm highlights, +5% contrast), soft vignette, very light grain. Post only finishes the look; it shouldn't make it.
- **Glow:** decorative lanterns use **glow billboards** (additive, soft, slow pulse) instead of real lights (teardown §9). Use at most 4–6 real point lights for the whole arena.
- **Assets:** clean low-detail albedo with planned values; let the lighting create form. Smooth, high-enough-poly silhouettes (no visible faceting on round forms: bells, arches, foliage clumps). Bevel all cut stone. Foliage is clumped painterly cards or blobs with wind sway: one global wind texture moving ivy, flowers and trees together (teardown §5).

## 9. Runtime and gameplay hooks (no mechanics)

- `VerdantChimesArena.cs`:
  - `quiet` bool (dims decoration)
  - view toggle `C`
  - bell sway, waterfall/mist scroll, mote drift, slow 0.01 rad/s turn of the centre medallion's outer gear ring (visual only)
- Colliders:
  - a flat floor collider
  - a circular boundary at **14.0 m** (inward-facing, made of segments)
  - disabled `EntryGate` on the south gap
  - no colliders on decoration
- Empty marker transforms:
  - `BossAnchor` at (0, 0, 0)
  - `PlayerSpawns` with 8 points on a 10 m radius arc across the south half
  - `EntryPoint` at the inner end of the entry bridge
  Boss designers will use these later.
- Layers: floor on `Ground`, decoration on `Environment`, water and mist on `TransparentFX`.

## 10. Performance budget

- 60 fps at 1440p on an RTX-class desktop
- ≤ 1.5 M triangles in view
- ≤ 400 draw calls (use GPU instancing for repeated stones, foliage and flowers; static batching for the rest)
- ≤ 6 real lights
- One 4K floor set, everything else 2K or lower

## 11. Steps

1. Blockout: floor disc, rim, 4 bridges, belfry boxes, gameplay camera. Screenshot from the gameplay and angle cameras, put them next to the top-down reference, and **send to the user before going further**.
2. Floor materials (stone, brass layout mask, mosaic), CustomLit + LookSettings, lighting, fog, outlines. Screenshot again.
3. Belfries, bridges and the entry bridge, rim dressing, foliage and flowers.
4. Waterfalls, abyss, mist, aqueduct surroundings, motes.
5. Runtime script, colliders, markers, `quiet` test, performance pass.

## 12. Done when

- [ ] From the gameplay camera, the layout clearly reads as the top-down concept: rings + cross brass floor, turquoise centre, 4 bridges with a longer south entry, a belfry on each diagonal, waterfalls on every edge.
- [ ] The floor inside 14 m is flat and clear, and 8 player capsules fit comfortably.
- [ ] Nothing on the camera side hides a player standing anywhere on the floor.
- [ ] With `quiet` on, a coral test telegraph and a teal test ring both read clearly over the floor and the mosaic.
- [ ] The look matches the Windblown method: coloured shadows, painterly terminator, thin outlines, restrained post, no faceted round shapes.
- [ ] The scene rebuilds from the menu item, and the budget in §10 holds.
- [ ] Before/after screenshots (gameplay and angle) saved in `Areas/Area3/Screenshots/` and sent to the user.

**Don't:** invent boss mechanics or hazards, put props on the combat floor, use pink trees (that was the old mockup), use real lights for every lantern, build the look mainly in post, or edit other sessions' files.
