# Build prompt: Area 2, The Brass Foundry arena (Unity URP)

Paste everything below the line into a Claude Code or Codex session that has the `unity-session` checkout open.

---

## Task

Build **The Brass Foundry** arena for Last Bell in Unity, matching the top-down reference image as closely as possible:

`prototype/arena-3d/areas/area-2/ref-brass-foundry-topdown.webp` (open it and keep it in front of you the whole time).

Last Bell is a co-op (up to 8 players) 2.5D isometric boss-fight game. The art bar is Hades/Windblown: painterly, readable, high-oblique camera. This arena is shared by Area 2's three minibosses and its area boss, so the combat floor must stay **flat, open and calm**. The bosses aren't designed yet: build no boss, no mechanics, no hazards.

The project is Unity **6000.6.6f1**, URP 17.6, Input System 1.20 and glTFast, at `unity/`. Follow the existing arena pattern in `unity/Assets/LastBell/Arenas/` (`Editor/KeyArtArenaBuilder.cs` and `KeyArtArena.cs`): an editor builder script creates the scene from code, a small runtime MonoBehaviour handles views and animation, and a batch-mode method saves review screenshots.

## Files you own (create new files only)

```
unity/Assets/LastBell/Areas/Area2/
  BrassFoundryArena.cs                 runtime: views, quiet dimming, molten scroll, lantern sway
  Editor/BrassFoundryArenaBuilder.cs   builds the scene + generated assets
  Generated/                           materials, textures (PNG), meshes (.asset), post volume profile
  BrassFoundry_Arena.unity             the scene
```

Namespaces: `LastBell.Areas.Area2` and `LastBell.Areas.Area2.EditorTools`. Add these menu items: **Last Bell > Area 2 – Brass Foundry** (build once, then open) and **Last Bell > Rebuild Area 2 – Brass Foundry**. Add a batch entry point too: `BrassFoundryArenaBuilder.BatchBuildAndCapture`. It saves `unity-game.png`, `unity-top.png` and `unity-hero.png` to `prototype/arena-3d/areas/area-2/shots/`. Reuse the `Capture()` approach from `KeyArtArenaBuilder` (copy it; don't edit that file).

**Don't edit** `prototype/arena-3d/index.html`, anything in `Arenas/KeyArt`, `KeyArtArenaBuilder.cs`, `KeyArtArena.cs`, or any character files. Other sessions own them. Don't switch git branches. Stage only your own files, and ask before pushing (the repo is public).

## Coordinates and scale

- 1 unit = 1 m, Y up. The arena centre (boss start) is (0, 0, 0). The combat floor top is at y = 0.
- **Image → world mapping:** the image's top is **−Z** (the Furnace Gate), its bottom is **+Z** (the Entry Bridge, toward the gameplay camera), its right is **+X** and its left is **−X**.
- Measured from the image (the floor disc is about 470 px wide = 28 m, so about 16.8 px per metre):

| Element | Radius / size |
|---|---|
| Open combat floor (playable) | r ≤ **14.0 m** |
| Floor border band | r 13.3 to 14.0 |
| Faint concentric seams | r 2.7, 7.0, 11.2 |
| Centre medallion | outer ring r 2.7, inner ring r 2.0, star points r 1.8 / 1.1 |
| Furnace rim (block ring) | r 14.0 to 16.8 |
| Molten channel | r 16.8 to 18.6, surface at y = −1.4 |
| Outer foundry wall | r 18.6 to 22 |
| Corner towers (×4, at 45°, 135°, 225° and 315°) | centre r ≈ 20, diameter ≈ 7 |
| East/West external gantries | width 5.5, from r 14 out to r 34+ |
| South Entry Bridge | width 4.5, from r 14 out to r 34+ |
| North Furnace Gate | gatehouse centred at r ≈ 19.5 on −Z, width ≈ 8 |

## Build it piece by piece

Build everything from code-generated meshes (boxes, cylinders, lathes, extruded 2D shapes) plus generated textures. There are no external models. Use helper functions (`Box`, `Cylinder`, `Lathe`, `ExtrudeShape`, `RingSector`) and GPU-instanced materials for repeated parts. Bevel or chamfer the edges of every block (a 2–4 cm bevel, or a baked edge highlight in the texture) so the painterly highlights read like the reference.

### 1. Open combat floor (r ≤ 14): calm and readable

- **One flat disc mesh** at y = 0, UVs mapped planar (u = (x + 14) / 28, v = (z + 14) / 28). It uses a generated **2048² albedo, normal and mask** texture set.
- **Slate:** dark charcoal slate with a slight cool blue (base about `#3C3F44`, variation from `#33363B` to `#4A4D52`). It's matte (smoothness ≈ 0.15) with very subtle worn lighter patches near the centre and soot toward the rim.
- **Slabs:** large slabs, cut by the concentric seams (r 2.7, 7.0, 11.2, 13.3) and **16 radial seams** (every 22.5°). Seams are thin, 2–3 cm and dark, with a faint bevel in the normal map. Add a few hairline cracks. **No glow anywhere on the floor.**
- **Border band** (r 13.3 to 14): slightly darker slab with a thin brass edge strip at r 13.95.
- **Gold cross:** four straight brass inlay lines (8–10 cm wide) along ±X and ±Z, from the medallion (r 2.7) to r 13.2. Make them raised meshes 1 cm tall with metallic brass `#B8893E` (metallic 0.9, smoothness 0.55). They are not emissive.
- **Spear-tip ornaments** (×4, on each cross line): an elongated brass diamond or spear head from r 10.4 to 12.6, about 0.9 m wide, with small side fins. Add a small rectangular brass plate (0.8 × 0.5 m) at r 12.9. Copy the shape from the image: the E and W ones have a crossbar like an anchor, and the N and S ones are slimmer.
- **Diagonal plaques** (×4, at 45°, 135°, 225° and 315°, r ≈ 10.6 to 12.6): trapezoid plates about 2.2 m wide, slightly darker than the slate, with a double brass outline and a small inner engraved line, rotated to face the centre.
- **Centre medallion:** a brass outer ring (r 2.55 to 2.7) and an inner ring (r 1.9 to 2.0). Inside them sits an **8-point compass star**: 4 long points to r 1.8 along the cross axes and 4 short points to r 1.1 on the diagonals. A small disc sits at the centre. It's raised 2–3 cm, with a dark slate fill between the rings and fine engraved tick marks. It has no glow (the boss stands here).
- Keep the whole floor quieter than any attack telegraph. Test this with a coral-red disc (see Acceptance).

### 2. Furnace rim (r 14 to 16.8): chunky blocks like gear teeth

- **Inner curb:** a low continuous curb at r 14.0 to 14.4, 0.25 m high, with a brass top strip. This is the visual edge of the playable area.
- **Block ring:** about **44 blocks** around the ring, leaving openings for the 4 entries (N, E, S, W). They alternate:
  - **Big blocks:** 1.6–2.0 m wide (tangential), 2.0–2.4 m deep (radial), 1.1–1.4 m high. Some have a smaller block stacked on top (0.6 m) or a stepped back half.
  - **Small blocks** in between: 0.9–1.2 m wide, 1.2 m deep, 0.6–0.8 m high.
  - Material: charcoal masonry and iron plate (`#2E2C2B` to `#45403B`) with **brass straps and corner caps**, rivet rows and chipped edges. Vary each block's size by ±10% with a seeded random (seed 7) so the ring doesn't look stamped.
- Under the blocks, a ring wall runs down to the molten channel (y −1.4), with corbels.

### 3. Molten channel (r 16.8 to 18.6)

- A recessed ring channel whose molten surface sits at y = −1.4. Use an emissive material with HDR orange (`#FF7A1F` × 4–6 intensity) and a scrolling noise-crust texture. Crust is dark `#3A1206` and the hot cracks are bright `#FFD27A`. Scroll it slowly along the ring.
- Add a soft additive glow sheet just above it, plus **orange point lights** (6–8, range 6, no shadows) under the rim. They uplight the outer faces of the rim blocks and the inner faces of the outer wall, exactly like the image's glowing ring.
- Rising ember particles (a shared particle system, ~300, slow) and thin heat-haze smoke.
- This is set dressing, not a hazard. It's outside the playable radius, so add no damage volume. Players can't reach it (see Gameplay setup).

### 4. Outer foundry wall (r 18.6 to 22)

- A ring of heavy masonry and iron wall segments, 1.5–3 m high above y = 0, with buttresses every ~15°. It has brass bands, machinery boxes, pipes, and small glowing furnace slits (emissive `#FFB050`, keep them small). The top is uneven and blocky, like the image.
- It continues downward (to y −25) as the foundry's outer drum, with arches and ribs, and fades into the haze.

### 5. Corner towers (×4, at r ≈ 20 on the diagonals)

- Round tiered towers about 7 m in diameter: a wide base drum, 2–3 stepped tiers with brass-banded cornices, and a lantern stage with **tall slit windows glowing orange**. The cap is a squat dome or cone with a brass finial and a crown of small merlons. Top them with a ring of blocky machinery, as in the image.
- **Heights:**
  - Far towers (−Z side, i.e. NE and NW in the image): top at about y = 14.
  - Camera-side towers (+Z, i.e. SE and SW): top at about y = 7. They must not hide any part of the floor (r ≤ 14) from the gameplay camera; see Acceptance.
- **Heavy chains** run from each tower top out and down to anchors on the outer wall and beyond (2–3 per tower). Chain links are about 0.6 m tall and 0.12 m thick, dark iron, as one instanced mesh.

### 6. External gantries (E and W)

- Wide deck platforms 5.5 m wide that start at the rim openings (r 14) and run out past r 34. The deck is at y = 0, flush with the floor, so it reads as one surface. A **one-way visual barrier**, a low brass-capped threshold, sits at r 14.
- Deck: dark iron plates in a 1 m grid with rivets and a brass edge trim. Side parapets are 0.9 m high with posts every 1.5 m. Lamp posts with small cage lanterns stand every ~6 m.
- Underneath: deep girders and an X-truss. **Big chains** run from tall pillars at about r 26 to the deck sides, angled out like suspension chains.

### 7. Entry Bridge (S, +Z, toward the camera)

- A 4.5 m wide bridge from the rim opening at r 14 out past r 34, deck at y = 0.
- **Gate pillars** at r ≈ 14.5, x = ±2.8: square stone pillars 1.2 m wide and 4 m tall, with brass caps and small lantern tops. **Drawbridge chains** run from the pillar tops down to the bridge's side rails at r ≈ 19.
- Deck: dark plates with a **brass centre strip** and a **gold arrow inlay pointing toward the arena** at r 19–22, plus side curbs with brass caps. Keep the pillars low enough that they don't block the floor from the gameplay camera.

### 8. Furnace Gate (N, −Z)

- A gatehouse in the rim opening at −Z. It has two massive pillars about 2 m square and 6 m tall with stepped brass-banded caps. A heavy lintel carries a brass gear emblem. The gate is **closed**, with iron bars or a portcullis lit warm from behind. Small furnace windows are on the pillar sides. A short dark deck leads out behind it, and chains run up and out.

### 9. Surroundings and sky

- The arena floats above a **lower foundry city** 20–60 m below: blocky buildings, roofs, chimney stacks and gantries, all dark, with **molten rivers and pools** between them (emissive), and smoke columns.
- Use a few tall chimney stacks and towers in the far distance (silhouettes).
- Sky: dark smoky brown-grey overhead `#1E1714`, warming to an orange haze `#B5501E` toward the horizon. Use URP fog (linear, start 40, end 220, colour `#6A3018`) so depth fades into warm haze.
- Keep everything below and around the arena darker than the molten ring, so the ring frames the floor.

## Lighting and post-processing (URP)

- **Key light:** a soft directional light from above and behind (rotation ≈ (60°, −20°, 0)), neutral-warm `#F2D6B8`, intensity 1.2, with soft shadows. The shadow distance covers r 30.
- **Ambient:** a gradient with sky `#4A3C38`, equator `#5A3020` and ground `#FF6A1A` (low), so undersides pick up the furnace glow.
- **Molten uplight:** the channel point lights above, plus a weak orange directional light from below (−60° pitch) for the drum and towers.
- **Post volume:** ACES tonemapping, Bloom (threshold 1.0, intensity 0.6, scatter 0.6), a slight vignette (0.25), and colour adjustments (contrast +10, saturation −5). Save it as `Generated/BrassFoundryPost.asset`.
- **The floor must stay evenly lit and slightly cool.** All the drama lives in the rim, channel, towers and depth.

## Runtime component (`BrassFoundryArena.cs`)

- Use `[ExecuteAlways]`, the same pattern as `KeyArtArena.cs`.
- `enum View { Gameplay, TopDown, Hero }`. Press **C** to cycle views, and expose `SetView(View)`.
  - **Gameplay:** fov 44, position (0, 29, 22.5), look at (0, 0, 1). This is identical to the Bellkeeper arena.
  - **TopDown:** an orthographic camera straight down, size about 26, framed like the reference image (−Z at the top of the screen).
  - **Hero:** fov 40, position (−17, 34, 34), look at (1, −4, −1).
- `public bool quiet`: while attack telegraphs are up, it lerps the molten intensity and window glows down to about 60% over 0.25 s.
- It animates the molten UV scroll, gives chains and lanterns a slight sway (±2°), and plays embers.

## Gameplay setup

- **Floor collider:** a flat MeshCollider (or a thin box) for r ≤ 14 on layer `ArenaFloor` (create the layer if it's missing; document it).
- **Boundary:** an invisible cylinder of capsule or box colliders at r = 14.2, 3 m high, so players and enemies can't leave the floor. The gantries, bridge and gate are **decor only** for now. Put the boundary across their openings too.
- Empty transforms named `BossSpawn` at (0, 0, 0) and `PlayerSpawn_1` to `PlayerSpawn_8` on a 10 m ring, starting at +Z.
- Name the root **`LB_ENV_BrassFoundry_Arena`**, with children `CombatFloor`, `FurnaceRim`, `MoltenChannel`, `OuterWall`, `Towers`, `Gantries`, `EntryBridge`, `FurnaceGate`, `Chains`, `Surroundings`, `Lighting`, `FX` and `Gameplay`. Mark static geometry Static (batching) and keep FX and animated parts non-static.

## Performance budget

- 60 fps at 1080p on a mid-range GPU.
- Use GPU instancing for blocks, chain links, rivets and posts.
- Fewer than 1,500 draw calls after batching; about 1.5 M triangles total.
- One shadowed directional light; up to 12 unshadowed point lights.
- Generated textures at 2048 max, saved as PNG in `Generated/` (not regenerated every play).

## Acceptance (check each one and include the screenshots in your report)

1. **TopDown shot** placed next to the reference image. The composition matches: floor, rim blocks, molten ring, 4 corner towers, E/W gantries, S Entry Bridge, N Furnace Gate, and the gold cross, spear tips, plaques and compass medallion in the same places.
2. **Gameplay shot:** the whole floor (r ≤ 14) is visible. Verify by raycasting from the gameplay camera to 64 points on the r = 14 circle and 64 on r = 10, and log any that hit something other than the floor. **Zero hits allowed.**
3. **Telegraph test:** temporarily place a coral-red disc (`#FF4A1E`, alpha 0.35, r 4) at (5.5, 0.02, −2.5) and save `unity-game-tele.png`. The disc must be the most eye-catching thing on the floor. Remove it from the saved scene afterwards.
4. No pink shaders, no console errors, no z-fighting (keep every decor surface at least 1 cm above y = 0 or below it; no coplanar faces).
5. The batch command works:
   ```
   Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area2.EditorTools.BrassFoundryArenaBuilder.BatchBuildAndCapture
   ```

When you're done, report what you built, the three screenshots (plus the telegraph test), anything that differs from the reference and why, and the files created. Don't commit or push unless asked.
