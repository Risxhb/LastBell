# Area 1 — The Skyward Spire (Unity)

Area 1's shared arena: 3 minibosses and the area boss fight here. It is built from `prototype/arena-3d/areas/area-1/reference-topdown.webp` following `skyward-spire-unity-build-prompt.md` (both in that folder).

- **Open:** menu **Last Bell → Area 1: Skyward Spire** opens `SkywardSpire_Arena.unity`. **Rebuild Area 1** regenerates the scene from scratch. Press Play, then **T** to switch between the gameplay camera and the top-down view framed like the reference.
- **Geometry:** `SkywardSpire.glb` holds all the solid geometry, materials and floor textures. It is exported from `skyward-spire-env.js` by `node prototype/arena-3d/areas/area-1/export-glb.mjs`, the same pipeline as the Bellkeeper's Dais. After changing the env file, re-export and then rebuild.
- **Builder:** `Editor/SkywardSpireBuilder.cs` adds what glTF can't carry:
  - the day sky (`Shaders/SkywardSky.shader`), fog, ambient light, the sun and a reflection probe
  - the cloud sea, using `Arenas/Shaders/CloudBillboard.shader`
  - floor and invisible-wall colliders, 8 spawn points, post-processing and the camera
  - a Vanguard for scale, and a hidden `Gameplay/TelegraphCheck` with placeholder warning shapes (not boss mechanics)
- **Runtime:** `SkywardSpireArena.cs` sways the banners, turns the armillary rings, drifts the clouds and handles the views. Groups named `Occluder…` (camera-side towers) are hidden in the gameplay view. Set `quiet` while a warning is up to dim the floor's gold.
- **Batch:** `Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area1.EditorTools.SkywardSpireBuilder.BatchBuildAndCapture` writes `Captures/topdown.png`, `gameplay.png` and `gameplay-telegraphs.png`.

## Layout

Units are metres. North is −z, and the gameplay camera sits on the south (entry) side at +z, as in the key-art arena. Note that the build prompt said north = +Z; this follows the existing project convention instead.

| Element | Size |
|---|---|
| Plaza (walkable, flat) | radius 14 m |
| Lapis disc | radius 9 m |
| Balustrade line | radius 14.65 m, with 28 small gold-domed kiosks and larger gate pairs at the 4 bridge mouths |
| Bridges | 4.2 m wide, in a cross |

Landmarks follow the reference by quadrant:
- NW and NE: cathedral masses with armillary towers
- W and E: blue-domed belfries
- SW: armillary tower with a bell lantern
- bottom corners: rotundas
- gold-tipped needle spires in every corner

## Notes

- The top-down view is tipped 27° from the south, as the painting is: its floor reads 0.89 high to wide. While that view is active it borrows a longer shadow range from the shared URP asset (the default is 50 m) and hands it back afterwards.
- A critic agent rated the top-down capture against the reference over 5 rounds: 5 → 5 → 5 → 6 → 6/10. Floor layout scores 8/10, and the measured proportions match. The remaining gap is in the surrounding buildings (sculpted gothic detail, spire density), the depth fog and cloud masses, and painted texture.
