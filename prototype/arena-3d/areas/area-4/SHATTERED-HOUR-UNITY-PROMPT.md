# Prompt: build The Throne of the Final Toll in Unity (Area 4, The Shattered Hour — final boss arena)

Paste everything below the line into the agent or tool that will do the Unity work. The layout reference image is `prototype/arena-3d/areas/area-4/ref-topdown-layout.webp`, a labelled top-down plan. Give it to the agent together with this prompt.

---

## Role and goal

You are building the final-boss arena of **Last Bell**, a co-op (up to 8 players) 2.5D isometric boss-fight game. The project is Unity 6 with URP 17.6, at `unity-session/unity/`. The art bar is Hades/Windblown: painterly, stylized low-poly, strong silhouettes, readable at gameplay distance.

Build **The Throne of the Final Toll** (Area 4, *The Shattered Hour*) so that it matches the attached top-down plan, `ref-topdown-layout.webp`. The plan is a layout and mood reference, not exact geometry. Where it disagrees with the numbers below, the numbers win.

This is an **environment only**. The final boss, its attacks and its phases are not designed yet. Don't invent any gameplay. The fissure and the four rim stations are **decoration only**.

## File rules (strict)

- Create files **only** under `Assets/LastBell/Areas/Area4/`. Don't edit anything in `Assets/LastBell/Arenas/`, `Characters/`, `Core/`, `Scenes/` or `VFX/`, and don't touch project settings. Other people own those.
- Expected layout:
  - `Area4/Editor/ShatteredHourArenaBuilder.cs`: editor menu items **Last Bell › Areas › Area 4 – Shattered Hour** (build once, then open) and **…› Rebuild**. Follow the pattern of `Assets/LastBell/Arenas/Editor/KeyArtArenaBuilder.cs`. Read it first.
  - `Area4/ShatteredHourArena.cs`: the runtime MonoBehaviour (`[ExecuteAlways]`) for ambient motion, camera views and the "quiet" dimming (see Runtime behaviour).
  - `Area4/ShatteredHour_FinalArena.unity`: the scene.
  - `Area4/Prefabs/LB_ENV_ShatteredHour_Arena.prefab` and `Area4/Prefabs/LB_ENV_FinalToll_BrokenBell.prefab`.
  - `Area4/Generated/`: generated meshes, textures and materials.
  - `Area4/Shaders/`: any custom shaders (skybox, fissure, fade).
  - `Area4/Shots/`: verification screenshots.
- Namespace: `LastBell.Areas.Area4` (editor code in `LastBell.Areas.Area4.EditorTools`).
- Match the existing code style: short top-of-file comment saying what the file does and where it mirrors the prototype, compact C#, no frameworks.

## Units, axes and the gameplay camera

- 1 unit = 1 m. Arena centre is at the origin, floor top at **y = 0**. **North = −Z** (away from the camera, toward the bell), **South = +Z** (toward the camera, the entrance).
- **Gameplay camera** (the same as every other Last Bell arena, so classes feel identical): perspective, FOV **44**, position **(0, 29, 22.5)**, looking at **(0, 0, 1)**. It doesn't rotate.
- **Establishing camera** (for screenshots only): FOV 48, position (0, 25, 36), looking at (0, 3, −8).
- **Top-down check camera** (matches the plan): orthographic, size 22, position (0, 60, 0), looking straight down. Use it to compare against the reference image.
- `ShatteredHourArena` switches between the three views with **C** (Gameplay → Establishing → TopDown) and has a public `SetView(View)` method.

## Layout and exact dimensions

Read the plan as a clock face: North at 12 (bell), East at 3, South at 6 (entrance), West at 9.

### 1. Combat floor (the playable area)

- A perfect circle of **radius R = 13.5 m**, flat and level at y = 0. Build it as a 128-segment disc mesh. It must stay completely unobstructed: no props, steps or bumps inside the circle.
- **Collision:** a `MeshCollider` (or a flat box) for the floor on layer `ArenaFloor`, plus a boundary made of **64 thin box colliders** forming a ring at radius R, 3 m tall, on layer `ArenaBoundary`. Players can't leave the circle. The openings to the stairs and bridges are also closed by this boundary. All walkways (south causeway, east and west bridges, north stair and landing) are scenery: players stay on the round floor (decided by Chzy18, 2026-10-21).
- **Floor surface (painted texture, 4096² base map, plus a 2048² normal map and a 2048² emissive mask):**
  - Large, irregular, hand-painted flagstones (1.5–2.5 m slabs, loosely radial near the centre and freer further out). Colours: midnight cobalt-charcoal `#1E2433`–`#2A3142`, with slightly bluer slabs `#252D45` and very dark grout `#0E1018`. Add subtle wear, chips and faint hairline cracks. Keep it low contrast so telegraphs read on top.
  - **Engraved brass inlay** (thin, about 4–6 cm wide, colour `#B8904E`, emissive mask at **0.25** strength):
    - **Central medallion:** an outer ring at r 3.2 m, a second ring at r 2.6 m, 24 sunburst rays between r 1.0 and 2.6 (alternating long and short), an inner ring at r 0.6 m, and a small raised disc at the exact centre. This is the brightest decoration on the floor (emissive 0.45), and it still stays below telegraph brightness.
    - **Concentric rings** at r 5.0, 7.5 and 10.2 m (very faint, about 3 cm wide), and an outer band from r 12.6 to 13.5 with 48 short tick marks and 12 long ones.
    - **Axis lines:** thin straight lines along N–S and E–W, running from the medallion (r 3.2) to the outer band (r 12.6).
    - **Axis nodes:** small eight-point star marks (0.5 m across) where the axis lines cross the r 10.2 ring (N, E, S, W), and smaller diamond marks at r 6.5 on the same axes.
    - **Diagonal markers:** small arrowhead or fleur marks at r 11.4 on the four diagonals, pointing at the rim stations.
  - **Decorative fissure** (gold, emissive): two jagged segments, not one continuous crack.
    - Segment A runs from the NW rim station (r 12.3 at 135°, i.e. up-left on screen) inward to the medallion's outer ring (r 3.2).
    - Segment B runs from the medallion's outer ring on the opposite side out to the SE rim station (r 12.3 at −45°).
    - The cracks don't cross the medallion. Use jagged polylines (segment length 0.3–0.6 m, angle jitter ±25°), 0.12–0.22 m wide, with 2–3 short branches each (0.8–1.5 m).
    - Shading: charred dark lip `#0A0816` about 0.15 m on each side, a gold body `#E2A650`, and a thin ivory core `#FFF2D0`. HDR emissive intensity is **2.0** on the core, fading to 0 at the lips.
    - It must look like **old, static, settled** damage: no pulsing, no flicker, no particles rising from it, and no red or coral tones (coral is reserved for hostile telegraphs). A very slow 6-second shimmer of ±5% is the most motion allowed.

### 2. Raised rim (parapet ring)

- An ornate stone parapet ring from r 13.5 to r 15.1 (1.6 m thick). Its **inner face is 0.9 m tall** (low, so players at the edge are never hidden). The outer face drops into the platform drum.
- Materials: blackened slate stone `#2B2A3A` with **aged-brass trim** `#9C7A44` on the cap and a second brass band 0.3 m below the cap. The cap has a small gothic moulding profile.
- **Gothic pinnacles (spires):** 20 of them, sitting on the **outer** half of the parapet. They cluster in pairs either side of each rim station and each entrance, as in the plan. Height 2.5–4.5 m, square base 0.6 m, crocketed spire tops, slate with brass finials.
- **Small lanterns:** 16 iron lanterns on the parapet cap with warm-amber flames `#FFB860`. Point lights only on 8 of them, range 4 m, no shadows.
- **Platform drum below the rim:** a cylinder from r 15.1, dropping 4 m, with arched gothic window recesses and buttresses every 15°. Below that, a jagged inverted rock spire 18 m deep (low-poly, flat-shaded `#231E33`).

### 3. Four rim stations (decoration only)

- Placed on the four **diagonals** (NE 45°, NW 135°, SW 225°, SE 315°). Each **centre sits at r 13.2**, so the station is half embedded in the parapet and bulges outward, as in the plan.
- Each station:
  - A round dial platform of **radius 1.9 m**, top **flush with the floor (y = 0.02)**. It gets no collider of its own: the floor and boundary colliders handle it.
  - Brass outer ring, two inner engraved rings, 8 rune dots and a raised domed centre boss (0.5 m radius, 0.25 m tall) in polished brass.
  - Two short gothic pinnacles flank it on the parapet (see §2), plus a small pointed-arch gate frame behind it on the outer side.
- **Dormant look:** brass reflects light, but the emissive stays at most **0.1**. They must not read as safe zones, soak circles or towers. No glow rims, no light pools on the floor, no idle particles.

### 4. Entrances and walkways (four gaps in the parapet)

| Where | Gap width | Structure | Notes |
|---|---|---|---|
| **South (entrance)** | 4.0 m | 6 steps going **down** 1.2 m outward, then a straight **causeway 4 m wide** running 26 m south (out of frame) | Flanked by gothic walls and buttresses with two hanging **crimson banners** (`#6E1A22`, gold sunburst emblem). Matching pinnacle pairs on both sides of the gap. This is the grand approach in the plan. |
| **North (to the bell)** | 3.0 m | 8 steps going **up** 1.6 m outward to a 4 × 4 m landing directly under the bell | The landing is a dark stone pedestal with brass trim. Nothing stands on it. |
| **East and West** | 3.5 m | A flat bridge 3.5 m wide and 8 m long, with low side walls, ending at a ruined pointed-arch gateway whose far side breaks off into the void | Bridge deck slate `#3A3E52`, slightly lighter than the floor. |

All walkway surfaces sit outside the boundary collider, so they're visual only for now.

### 5. The cracked celestial bell (`LB_ENV_FinalToll_BrokenBell`)

- A monumental bronze bell hangs **north** of the arena, above the north landing. The bell's vertical axis sits at x = 0, z = −20. Its height comes from the framing rule below.
- Size: mouth radius **5.5 m**, height **8 m**, classic bell profile (lathe: shoulder, waist, flared lip), wall thickness about 0.35 m. Material: dark aged bronze `#5E4A30`, metallic 0.75, smoothness 0.45, with three engraved gold bands (lip, waist, shoulder) bearing clock numerals.
- **Damage:** a wedge about 30° wide missing from the lip on the camera-facing (south) side. One broken shard floats 2 m away from the gap, slowly rotating. Add 6–9 glowing hairline cracks running up from the gap (HDR gold, intensity 2.5).
- Lit from inside: a warm interior emissive (`#FFA850`, HDR 1.5) and one warm point light inside (range 20 m, no shadows). A very faint additive light cone below the mouth is allowed (alpha 0.08 or less, and it must not hit the combat floor).
- **Gameplay-camera framing rule:** with the gameplay camera, the bell's **lower lip and the glowing interior must be visible along the top edge of the frame**, the way the plan shows it cut off at the top. Set the bell's height so the lowest point of its lip sits at **y ≈ 2.0**, and tilt it **about 20° toward the arena** (mouth facing south/down). Check it in the gameplay screenshot and adjust y and tilt until the lip and cracks read.
- Held by **two huge chains** (link 0.8 m, iron `#2A2440`) rising out of frame, plus a crown loop.
- **Behind the bell:** a giant vertical **astrolabe / clock ring** of radius 14 m at z = −30, standing upright and facing the arena. It has an outer brass ring, an inner numeral ring, spokes and a broken section about 70° wide. It turns slowly (1 revolution every 4 minutes).

### 6. The void and surrounding architecture

- **Skybox:** a custom skybox shader in `Area4/Shaders/`, a violet-indigo cosmic void.
  - Colours: base `#0B0820`, mid `#2A1758`, highlight nebula `#7B4FD0` with lavender wisps `#9C86E8`.
  - Layered fbm nebula clouds with slow drift, and sparse twinkling stars.
  - Below the platform, a slow swirling vortex disc at y = −75, radius 160, with violet-blue arms and a dim pale core. Keep its core dim so it doesn't bloom out past the bottom of the frame.
- **Floating gothic ruins** (all outside r 17, none on the camera side between the camera and the floor):
  - 10–14 broken arcade sections (two to four pointed arches with columns), tilted 5–25°, at r 22–40, y −20 to +12.
  - Corner arches as in the plan: big arcades top-left, top-right and right.
- **Floating stone pillars and debris:** about 30 inverted spire chunks and about 200 low-poly rocks (instanced) at r 17–70 and y −45 to +15. Each bobs slowly (amplitude 0.4 m, period 15–25 s) and spins slowly.
- **Diagonal chains:** 4 giant chains crossing the frame corners (top-left, top-right, bottom-left, bottom-right in the plan), anchored off-screen and sagging into the void. Links are 1.2 m. Use a lower LOD for links far from the camera.
- **Hanging bells:** 6–8 small bronze bells (1–2 m) on chains at r 20–30 to the sides and back, swaying slightly (±0.7°).
- **Light shafts:** 3–4 faint warm vertical light shafts in the void behind the arena (additive, alpha 0.12 or less).

## Lighting and post-processing (URP)

- **Main light:** directional, colour `#C8B8FF` (cool lavender moonlight), intensity 1.1, angle 55° down from the north-west, soft shadows. The shadow distance is 60 m.
- **Warm accent:** the bell's interior light and the lanterns provide the warm gold counterpoint. Keep the floor mostly cool so gold reads as precious.
- **Ambient:** Gradient lighting: sky `#6E4FC2`, equator `#2B2358`, ground `#0E0A1E`.
- **Fog:** exponential squared, colour `#2A1B55`, density 0.010. The floor at gameplay distance must stay crisp.
- **Volume (global):**
  - Tonemapping ACES
  - Bloom threshold 1.0, intensity 0.6, scatter 0.6
  - Color Adjustments: post-exposure +0.1, contrast +10, saturation +5
  - Vignette 0.25
  - White Balance temperature −8
  - No depth of field in the gameplay view.
- **Reflection probe:** one box probe over the floor, so the brass picks up the violet void.

## Readability rules (must pass)

1. Floor decoration (medallion, rings, axis lines, ticks) must be clearly **dimmer and lower in contrast** than a live telegraph. To check, drop a temporary test decal: a coral `#FF6B5A` circle 4 m across at about 60% opacity, placed at (5, 0.02, 3). It must pop against the floor at a glance. Delete it after the screenshot.
2. The fissure and the stations must **not** read as hazards: no red or coral, no pulsing, no particles, no ground glow pools.
3. **Nothing taller than 0.9 m inside r 15.1.** Pinnacles and walls on the **south half** of the rim (screen-bottom side) get a `ShatteredHourOccluderFade` component. It fades them to 25% when a player is within 3 m behind them from the camera's view. Use dithered transparency so it works with URP opaque rendering.
4. The playable circle must stay readable at **8 players**. Place 8 temporary capsules (0.6 m wide) evenly at r 11, take a screenshot, then remove them.

## Runtime behaviour (`ShatteredHourArena.cs`)

- **Ambient motion:** the clock rings turn, debris bobs, hanging bells sway, the bell shard drifts, and the fissure has its slow ±5% shimmer.
- **`public bool quiet`:** when true, floor decoration emissive (medallion, rings, fissure) eases down to 40% over 0.3 s, and back up when it turns false. This matches `quiet` in `KeyArtArena.cs` (hazards are up, so decoration dims).
- **View switching** with C, and `SetView(View)`. Only one camera is used; it is moved between the three poses.
- **Optional presentation hooks** (for later; leave them unused): `SetPresentationStage(int 0..3)` for Arrival, Opening, Escalation and Final. They only scale ambient motion speed and the bell glow. No gameplay.

## Performance budget

- Visible from the gameplay camera: at most 350k triangles and at most 180 draw calls (use static batching for the rim and architecture, GPU instancing for debris, links and lanterns).
- One shadow-casting directional light, at most 10 additional realtime point lights, none of them casting shadows.
- Textures at most 2048², except the floor base map at 4096². Use texture compression defaults.

## Build order (stop and screenshot after each step)

1. **Blockout:** floor disc, boundary colliders, parapet ring, four station discs, four entrance gaps with stairs and bridges, the bell as a simple lathe, the gameplay camera. Take screenshots from the gameplay and top-down cameras.
2. **Floor art:** paint the flagstones, brass inlay and fissure textures, and set up the materials. Take screenshots from the gameplay and top-down cameras, plus the telegraph test.
3. **Rim and stations detail:** pinnacles, lanterns, brass trim, station dials, banners, drum and rock underside.
4. **Bell and astrolabe:** full bell with crack, shard, chains, interior glow, and the clock ring behind it. Re-check the bell framing rule.
5. **Void:** skybox, vortex, floating ruins, pillars, debris, diagonal chains, hanging bells, light shafts.
6. **Lighting and post:** final light, fog, volume and reflection probe, occluder fade, `quiet` dimming.
7. **Prefabs and scene:** save the two prefab roots and the scene. Re-running **Rebuild** must regenerate everything cleanly.

## Scene hierarchy

```
ShatteredHour_FinalArena (scene)
├─ Lighting            (directional light, global volume, reflection probe)
├─ Cameras             (Main Camera + ShatteredHourArena)
├─ LB_ENV_ShatteredHour_Arena   (prefab)
│  ├─ Floor            (disc, MeshCollider, layer ArenaFloor)
│  ├─ Boundary         (64 box colliders, layer ArenaBoundary, no renderers)
│  ├─ Rim              (parapet, pinnacles, lanterns, drum, rock)
│  ├─ RitualStations   (RitualStation_NE, _NW, _SW, _SE)
│  ├─ Entrances        (Entrance_South, Stair_North, Bridge_East, Bridge_West)
│  ├─ Void             (vortex, ruins, pillars, debris, chains, hanging bells, light shafts)
│  └─ ClockRings
└─ LB_ENV_FinalToll_BrokenBell  (prefab: bell, shard, chains, interior light, astrolabe ring)
```

## Deliverables and acceptance

- The scene opens from the menu item, builds without errors or warnings, and **Rebuild** works.
- Save these 1920×1080 screenshots to `Area4/Shots/`: `gameplay.png`, `establishing.png`, `topdown.png`, `telegraph-test.png`, `eight-players.png`.
- `topdown.png` is visibly the same layout as `ref-topdown-layout.webp`:
  - round floor with central medallion, axis lines and nodes
  - two-segment fissure from NW to SE
  - four diagonal stations embedded in the rim
  - grand south stair and causeway with crimson banners, north stair to the bell, east and west bridges
  - pinnacles around the rim, bell at the top, violet void with ruins and chains
- `gameplay.png` shows the bell lip and cracks along the top edge, and a fully readable floor.
- Reply with a short list of: files created, anything you deviated from and why, and the open questions.

## Out of scope (don't do)

- No boss, enemy, attack, telegraph logic, damage zone, phase logic or UI.
- Don't make the stations or the fissure interactive.
- Don't change the shared gameplay camera values, input, characters or other arenas.
- Don't commit, push or post anywhere unless you're told to.
