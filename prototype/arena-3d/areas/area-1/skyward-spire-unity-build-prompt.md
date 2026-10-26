# Build prompt: The Skyward Spire arena in Unity

Hand this whole file to the agent or developer building the scene. The target image is `reference-topdown.webp` (in this folder): a top-down view of the arena. Match it as closely as possible. When this text and the image disagree, the image wins.

---

## Task

Build **Area 1 — The Skyward Spire**, the shared arena for Area 1's three minibosses and its area boss, as a Unity scene. Seen from directly above, it must match `reference-topdown.webp`: the same layout, proportions, colours and surrounding buildings. From the gameplay camera it must also read as a clean, open combat floor.

## Project facts

- Unity **6000.6.6f1**, URP. Project: `unity-session/unity/`. Git checkout: `unity-session`, branch `feature/class-test-arena`. Don't switch branches.
- Create new files only, under `unity/Assets/LastBell/Areas/Area1/`. Don't edit other sessions' files: `Arenas/KeyArt/*`, `KeyArtArenaBuilder.cs`, characters, `index.html`.
- Reuse, don't copy: the shaders `Assets/LastBell/Arenas/Shaders/CloudBillboard.shader` (billboard cloud puffs, `_Tint`, `_Opacity`) and the patterns in `Assets/LastBell/Arenas/Editor/KeyArtArenaBuilder.cs`. Those patterns are: a menu item to open and rebuild; `Build()` generating materials into a `Generated/` folder; a global post `Volume` with Bloom; `Capture()` with `SingleCameraRequest` for screenshots; and a `BatchBuildAndCapture()` batch-mode entry.
- Deliverables:
  - `Areas/Area1/SkywardSpire_Arena.unity`
  - The prefab root `LB_ENV_SkywardSpire_Arena` (`Areas/Area1/Prefabs/`)
  - The builder `Areas/Area1/Editor/SkywardSpireBuilder.cs`, with the menu items **Last Bell → Area 1: Skyward Spire** and **Rebuild Area 1**
  - The runtime `Areas/Area1/SkywardSpireArena.cs` for animation and the `quiet` dimming flag
  - Generated assets in `Areas/Area1/Generated/`
- The scene must be fully rebuildable from the builder script. No hand-placed-only content.

## Units, axes, scale

1 unit = 1 m. Y is up. The arena centre is at the origin. **North = +Z** (the top of the reference image), **east = +X**. The south bridge (−Z) is the **entry bridge**. Measured from the reference (floor diameter ≈ 456 px → 28 m, so 1 px ≈ 6.1 cm):

| Element | Size |
|---|---|
| Combat floor (ivory plaza), walkable | radius **14 m**, perfectly flat at y = 0 |
| Lapis centre disc | radius **8.6 m** |
| Balustrade ring (centre line) | radius **14.6 m**; wall height 0.9 m |
| Bridges | 4, deck width **4.4 m**, length ≥ 22 m from the rim (run out of frame), deck at y = 0 |
| Cardinal diamond emblems | centred at r = **11.7 m** on N/E/S/W |
| Diagonal square markers | centred at r = **12.6 m** on NE/SE/SW/NW |
| Big belfries (four quadrants) | centres ~**24–30 m** from the origin, tops 10–25 m above the floor, bases far below in the clouds (−60 m) |

**Gameplay rule:** nothing solid stands inside r = 14 m. Players walk only on the plaza and the first ~3 m of each bridge mouth. Add a `MeshCollider` floor and invisible wall colliders on the balustrade line, with gaps at the bridge mouths. Each bridge ends at an invisible wall 3 m out.

## Scene hierarchy

```
LB_ENV_SkywardSpire_Arena
├─ Floor            (plaza disc + lapis disc + inlays, colliders)
├─ Rim              (balustrade, pavilion posts, outer drum wall, underside)
├─ Bridges/Bridge_N, _E, _S (entry), _W
├─ Belfries/        (the named landmark towers below)
├─ Spires/          (filler spires and turrets between the belfries)
├─ Abyss/           (cloud sea, distant towers)
├─ Lighting/        (sun, fill, reflection probe)
└─ Gameplay/        (colliders, spawn points, camera rig)
```

## 1. Combat floor

**Ivory plaza (r 8.6–14 m).**
- Warm cream marble (`#E9DCC0` to `#F1E6CF`), matte (smoothness 0.25).
- Laid in **2 concentric courses** of large curved flagstones, each ~2.5–3 m long, with hairline seams (`#C9B79A`, 1–2 cm). Bake the seams into a 4K albedo, plus a normal map with a very slight bevel.
- Two thin **gold inlay circles** in the ivory: at r = 8.75 m (8 cm wide) and r = 13.9 m (8 cm), plus a faint one at r = 12.2 m (4 cm).
- **Cardinal diamond emblems** at N/E/S/W (r = 11.7 m): a vertical lozenge 1.1 m long and 0.7 m wide. The fill is cobalt enamel `#22408F` with a lighter centre, inside a 6 cm raised gold frame, with a small gold dot in the middle. Point the long axis along the bridge.
- **Diagonal markers** at NE/SE/SW/NW (r = 12.6 m): a small gold-outlined square rotated 45°, 0.55 m across, with an ivory centre and a gold dot.
- Thin gold **chevron lines** (3 cm) from each diagonal marker toward the rim, as in the reference.

**Lapis disc (r ≤ 8.6 m).**
- Deep lapis blue `#1F2E6E` to `#2A3C86`, with subtle mottling and very faint large tile seams. Smoothness 0.55, so it reads as polished stone.
- Gold inlay, raised 2–3 mm with metallic 0.9 and smoothness 0.75:
  - **Eight-point compass star** at the centre. The four cardinal points reach r = 2.9 m and the four diagonal points r = 1.7 m. Each point is split down its spine into a lit half (`#F2C76A`) and a shaded half (`#B8862F`) to read as faceted. It has a small round boss at the centre.
  - **Concentric rings:** r = 1.2 m (4 cm), 2.0 m (3 cm), 3.4 m (5 cm), 4.4 m (4 cm), 6.2 m (5 cm), and a thicker edge ring at 8.6 m (14 cm).
  - **Eight radial lines** (3 cm) from r = 2.0 to 8.6 m at 0°, 45°, 90° and so on.
  - **Ring studs:** 8 small gold rings (outer r 0.22 m, 4 cm wide) where the radials cross r = 6.2 m. Then 8 more on r = 4.4 m, offset 22.5°.
- Gold glow: emission ≤ 0.15. **Decorative inlay must stay well below boss telegraph brightness.** The `quiet` flag in `SkywardSpireArena.cs` lerps floor emission to 0 while a warning is up.

Build the floor as one disc mesh (r = 14 m, at least 128 segments) with a planar UV. Paint albedo, normal, mask (metallic/smoothness) and emission textures at **4096²**, either generated by the builder (C# `Texture2D` painting, like the browser mockup's floor painter in `skyward-spire.html`) or authored. Thin gold should live in the textures, not as geometry. Only the star may be a slightly raised mesh.

## 2. Rim

- **Balustrade** on r = 14.6 m, broken by the four bridge mouths (gaps of 4.4 m + 1 m). Its parts:
  - an ivory base plinth 0.25 m high
  - turned ivory balusters (0.12 m, ~0.35 m spacing) or solid arcaded panels
  - an ivory top rail with a **gold coping strip**, 0.85 m high
- **Pavilion posts:** **24 evenly spaced** small round ivory kiosks on the balustrade line (skip any that fall in a bridge gap). Each is:
  - a cylinder 0.55 m in radius and 1.6 m tall, with 4 tiny arched openings
  - a gold cornice ring
  - a **ribbed gold dome** (8 ribs, radius 0.6 m)
  - a gold finial with a small orb

  These are the most recognisable rim feature in the reference. Model one and instance it.
- **Bridge-mouth gate posts:** at each bridge mouth, two larger pavilion posts (1.2× scale) flank the opening. Add a slightly wider ivory threshold slab (6 m wide, 1.5 m deep) with a gold edge.
- **Outer drum wall** below the rim: a vertical ivory wall from y = 0 to −6 m, with tall gothic arched blind windows (1 m × 3.5 m), gold trims and small corbels. Then a tiered underside that tapers to a point at about −30 m, with gold bands and a few hanging pinnacles.

## 3. Bridges (×4)

- A straight ivory deck, 4.4 m wide and 0.6 m thick, of large cream slabs with a thin **cobalt inlay line** along the centre.
- Low ivory side walls (0.7 m) with gold caps.
- **Banners:** a row on each side, every 4 m. Each is tall, narrow cobalt cloth, 0.8 m × 3 m, hanging from a gold crossbar on an ivory post with a **small gold dome on top**. The banner has a gold border and a **gold four-point star** emblem. Leave the cloth slightly concave, with a cloth sim or a simple vertex sway in `SkywardSpireArena`.
- Under the deck: ivory gothic arches every 6 m with gold bands, and pier columns dropping into the clouds.
- Where the north bridge leaves the frame, add a far gate structure. The south (entry) bridge continues to a landing platform out of view.

## 4. Ivory belfries and spires (the surrounding buildings)

**These carry the look: follow the reference closely, quadrant by quadrant.** All are white-ivory gothic: warm white `#F2EADB`, cobalt stained glass `#2A3F9A` with gold leading, and gold `#E2B45C` trims. They stand **outside the rim**, rise out of the clouds, and are joined by slender ivory skybridges and flying buttresses.

| Where (top-down) | What |
|---|---|
| **NW (top-left)** | A massive cathedral tower block: stacked square tiers with tall pointed-arch cobalt windows, each with a gold star. It has corner pinnacles and a crowning **large gold armillary sphere** (3–4 interlocking gold rings around a small gold globe, ~3 m across) on a short ivory drum. A small "Ivory Belfry" round pavilion with a gold dome stands just outside the rim at about 315°. |
| **NE (top-right)** | A mirror of NW with variation: a cathedral mass with arched cobalt windows, buttresses and pinnacles. A **large gold armillary sphere** crowns a cylindrical belfry close to the rim at about 45°. |
| **W and E (middle edges)** | Tall **round belfries**: an ivory cylinder (r 3 m) with tall cobalt panels bearing gold stars, a gold cornice, then a **deep cobalt dome ribbed in gold**, then a gold lantern and a tall gold needle spire. These sit just south of the west and east bridges. |
| **SW (lower-left)** | A round tower with an open gold-columned bell lantern, topped by a **gold armillary sphere**. Further out and lower, a round rotunda with a gold dome and small domed turrets. |
| **SE (lower-right)** | A round rotunda with a blue-and-gold dome and a ring of small gold-domed turrets (the bottom-right corner of the reference). Further up, a tall belfry with cobalt panels and a gold spire. |
| **Everywhere between** | Filler: 30–50 smaller ivory spires, needle pinnacles and turrets with gold caps and orbs. Scatter them at varying heights so they recede down into the clouds, getting smaller and paler with depth. |

**Build approach:** make a small kit of modular parts and assemble the landmarks from it. Use prefabs plus GPU instancing:
- square tier, round drum, arched window bay, buttress, pinnacle
- gold dome (ribbed), cobalt dome (ribbed), lantern, needle spire, armillary sphere
- balcony, skybridge segment

Bake lighting on static geometry. Use LODs on the filler spires.

## 5. Cloud abyss and sky

- A bright sky: a gradient skybox, blue zenith `#3E78D8` to a pale horizon `#CFE2FA`, with no sunset tones.
- **Cloud sea** 25–70 m below the floor: layered billboard puffs using `LastBell/CloudBillboard` (tint near white `#F4F7FF`, opacity 0.8–0.95). Leave clear gaps of sky blue between banks, as in the reference. Add a few puffs around the tower bases at −10 to −20 m so the towers rise out of cloud.
- Linear fog (pale blue `#BCD3F3`, start 60 m, end 400 m) so deep structures fade paler and bluer.
- A few distant small spires and floating towers half-hidden in cloud.

## 6. Lighting and post

- Sun (directional): warm white `#FFF0D6`, intensity ~2.5, from the **upper-left / north-west, high** (pitch ~60°), so shadows fall to the lower right as in the reference. Use soft shadows with a cascade covering ±40 m.
- Ambient: gradient or skybox, sky `#D6E6FF`, ground `#B9AC92`. Add a reflection probe at the centre so the gold reflects sky.
- Post `Volume`: ACES tonemapping, Bloom (threshold 1.0, intensity ~0.4), mild color adjustments (+5 saturation, slight warm white balance), and a subtle vignette.

## 7. Cameras

- **Gameplay camera** (default): fixed high-oblique perspective, FOV 44°, position (0, 29, −22.5), looking at (0, 0, −1). Note the sign: north is +Z here, so the camera sits on the south (entry) side looking north. Nothing may block the plaza from this view. Fade or hide any tower geometry that crosses the frustum between the camera and the floor.
- **Top-down capture camera**: orthographic, size ~32, straight down, framed like the reference with north up. Toggle it with **T** in Play mode.
- Add `Capture()` and a batch entry, following `KeyArtArenaBuilder`, that write `Areas/Area1/Captures/topdown.png` and `gameplay.png` at 1920×1080.

## 8. Readability rules (must hold)

- The floor stays flat, clean and prop-free. Don't add rubble, cracks, debris or decals inside r = 14 m.
- All decorative gold and enamel stays visibly quieter than hostile telegraphs. Test with a generic red-orange circle and cone (deep red-orange fill, dark outer rim; these are placeholders, not boss mechanics) drawn over both the ivory and the lapis areas. Warnings must read instantly on both.
- Don't invent boss mechanics. Diamond emblems, markers and stars are decoration only.

## 9. Done when

1. **Last Bell → Area 1: Skyward Spire** opens the scene, and **Rebuild Area 1** regenerates it from scratch without errors.
2. `topdown.png` placed side by side with `reference-topdown.webp` matches:
   - the floor proportions: lapis disc ≈ 62% of the plaza radius
   - the four bridges in a cross with banners
   - 24 gold-domed pavilion posts on the rim
   - 4 cardinal diamonds and 4 diagonal markers
   - the 8-point star and rings
   - armillary spheres at NW, NE and SW
   - blue-domed belfries at W and E
   - rotundas in the bottom corners
   - clouds with blue gaps below
3. `gameplay.png` shows the whole plaza unobstructed, with the Vanguard (`Characters/Vanguard`) for scale and a placeholder telegraph clearly readable.
4. The scene runs at ≥ 60 fps at 1080p on a mid-range GPU: instanced filler, LODs, baked lighting.
5. Only files under `Areas/Area1/` are added. Commit locally on `feature/class-test-arena`, staging by path. **Ask before pushing** (the repo is public).
