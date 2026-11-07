# Area 4 hand-model plan

Status: built. Every asset is modelled in **Blender 5.2**. Each one has its own script under `blender/<Asset>/` (run with `blender -b -P <script> -- <out dir>`; the `.blend` is saved next to it and isn't in Git). It is exported as FBX to `unity/Assets/LastBell/Areas/Area4/Models/<Asset>/` and placed by `ShatteredHourArenaBuilder`. If a model is missing, the builder falls back to the old procedural piece, so the run loop never breaks.

## Assets

| Asset | Script | Notes |
|---|---|---|
| Trim sheets: stone blocks and aged brass (2048²) | `common/trims.py` | PNGs are committed in `Models/Trims/` |
| Pinnacle, 3 heights | `pinnacle/pinnacle.py` | camera-side (south) pinnacles use the dimmer `AgedBrassDim` crockets |
| Parapet module, drum bay, rock spire | `rim/rim.py` | |
| Rim station | `station/station.py` | |
| Cracked bell, parts, five shards | `bell/bell.py` | arch row on the waist, shared with the shards |
| Astrolabe, two side rings, fragment | `rings/rings.py` | cobalt star-map band, 60-tick scale, numerals counter-clockwise on the outer wall |
| Entrances: south set, north stair, east/west bridges | `entrances/entrances.py` | |
| Ruins, rocks, spire chunks, chain links, hanging bell, lantern | `void/void.py` | rocks in three families |

## Pass of 2 Nov 2026 (critic round-5 leftovers)

Follows the concept references Chzy18 approved on 27 Oct (`Reference/Codex-Corrections-20261025/`, astrolabe from `followup-3-edits-v2`).
- **Astrolabe:** now the approved band. It has a deep cobalt star-map wall between two brass rims, and both rims carry the same 60-station scale (every fifth tick major). Twelve medallions with numerals 1–12 sit on the outer wall only, counter-clockwise seen from above. "Above" here is the side the numerals stand up toward, which faces away from the game camera, so they read upright on the ring's upper arc. The camera-side face is unlettered. The centre is hollow (no rete or spokes), with brass struts inside. The side rings use the same band with blank star medallions.
- **Rocks:** three families from concept sheet 16. Tilted slabs with an overhang and hanging shards, clusters of split shards with a carved pointed-arch niche, and faceted boulders. Each has a tapered keel.
- **Bell shards:** five detached wall pieces cut from the bell's own profile, largest first, as on concept sheet 17. They have jagged cut edges, 24 cm wall thickness and a patina inner face. They carry the same bands and arch row as the bell, at one 20° pitch, so all five read as parts of one bell. They drift as one merged cluster beside the break.
- **Void and colour:** the sky is darker between the nebula pockets, shadows have a cool violet tint, and contrast is up slightly. Crimson banners now hang either side of the north gap.
- **Floor:** grime settles into the slab joints.

## Conventions

- Metres, Blender Z-up. FBX export uses forward −Z, up Y, with the axis conversion baked in.
- Material slot order is fixed per asset and matches the Unity material list in the builder. `lb.join` sorts faces by slot, because Unity numbers FBX submeshes by first use.
- Trim-sheet UVs are cube-projected at 2.4 m per tile.
- Rebuilding in Unity regenerates `Generated/`. Keep the existing GUIDs of `Generated/` assets when committing, because the run scene and the menu showcase reference them.
