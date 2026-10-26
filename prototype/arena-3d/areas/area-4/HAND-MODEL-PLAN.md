# Area 4 hand-model plan

Status: proposal, following `SHATTERED-HOUR-HAND-MODEL-PROMPT.md`. Every asset is modelled in **Blender 5.2**. Each one has its own script and `.blend` under `blender/<Asset>/` (run with `blender -b -P <script> -- <out dir>`), is exported as FBX to `unity/Assets/LastBell/Areas/Area4/Models/<Asset>/`, and is placed by `ShatteredHourArenaBuilder`. If a model is missing, the builder falls back to the old procedural piece, so the run loop never breaks.

## Order and status

| # | Asset | Texture set | Budget (tris) | Status |
|---|---|---|---|---|
| 0 | Trim sheets: stone blocks and aged brass (2048², painted in Blender) | shared | – | in progress |
| 1 | Pinnacle, 3 heights (pipeline proof) | stone + brass trims | 2.5k each | in progress |
| 2 | Parapet module (15° arc), end caps; drum bay | trims | 3k | next |
| 3 | Rim station | unique 2048² dial + trims | 8k | |
| 4 | Cracked bell + shards, clapper, yoke | unique 2 × 2048² | 25k | |
| 5 | Astrolabe ring | brass trim + unique numerals | 15k | |
| 6 | South entrance set, north stair and landing, east/west bridge set | trims | 2–6k | |
| 7 | Floating gothic ruins (3 modules) | trims | 6k | |
| 8 | Void rocks (6) and spire chunks (4) | strata trim | 0.4–1.5k | |
| 9 | Chain link (+LOD), hanging bell | iron, bronze | 120 / 2k | |
| 10 | Side clock rings (2) | brass trim | 6k | |
| 11 | Lantern | iron + glass | 800 | |

## Conventions

- Metres, Blender Z-up. FBX export uses forward −Z, up Y, with the axis conversion baked in. Pivots are at the base centre.
- Material slot order is fixed per asset and matches the Unity material list in the builder. Pinnacle slots: 0 stone, 1 brass, 2 polished brass.
- Trim-sheet UVs are cube-projected at 2.4 m per tile, matching the stone sheet's four 0.6 m courses.
- Bevel modifier (2–4 cm, 2 segments, angle-limited) plus a weighted-normal modifier, both applied before export.
- Binaries (`.blend`, FBX, PNG over 1 MB) are kept out of Git until the `area4-models-v1` asset pack is approved and published.
