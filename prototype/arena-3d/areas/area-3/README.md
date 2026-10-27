# Area 3: The Verdant Chimes

The Area 3 arena: overgrown clockwork ruins, a turquoise mosaic, belfries with brass bells, and waterfalls off every edge. One arena hosts all four Area 3 fights (3 minibosses + 1 area boss). The floor is a flat, open disc with a playable radius of 14 m, the same area as the Bellkeeper dais. No boss or mechanics are designed here.

The layout target is the user's top-down concept, `reference/verdant-chimes-topdown.webp`. The full spec is `VERDANT-CHIMES-UNITY-PROMPT.md`.

## Files

**Unity build (current):**
- `verdant-arena.js` + `verdant-arena.html`: the arena source in three.js.
  - Views: `?view=game|angle|top`.
  - Options: `?quiet`, `?rings`, `?clean`.
  - `?export` prepares the Unity export.
- `export-glb.mjs`: writes four files to `unity/Assets/LastBell/Areas/Area3/Source/`: `VerdantChimes.glb` and the 4K floor textures (albedo, height, mask). Run it with `node prototype/arena-3d/areas/area-3/export-glb.mjs`.
- `shots/unity-r1…r5-*.png`: the Unity review screenshots from each critic round.

**Earlier look mockups:** `verdant-look.html` + `verdant-env.js`.

## Unity

Everything lives in `unity/Assets/LastBell/Areas/Area3/`. Use **Last Bell > Area 3 > Open Verdant Chimes**, or **Rebuild Verdant Chimes** after a new export.

To build in batch mode and save screenshots to `Area3/Screenshots/`:

```
Unity.exe -batchmode -projectPath unity -executeMethod LastBell.Areas.Area3.EditorTools.VerdantChimesBuilder.BatchBuildAndCapture -quit
```

Main pieces:
- `Shaders/CustomLit.shader`: Windblown's lighting model (`research/windblown-artstyle.html` §1), written as hand-made URP HLSL. It includes the floor mask and cut-stone detail.
- `Area3_VerdantChimes_Look.asset` (`LookSettings`): every look value in one asset, pushed as `_LB_*` globals.
- The other shaders: waterfall, mist, pool, glow billboard (used for the lanterns), sky, depth outline overlay, and shader-driven particles. The Particle System module is disabled in this project, so the particles don't use it.
- `VerdantChimesArena.cs`: bell sway, the medallion gear, and `quiet`. While `quiet` is on, the brass dims, the mosaic greys and the glow turns off, so telegraphs win. Press `C` to switch views.
- Gameplay: a floor collider, a 14 m boundary ring, a disabled `EntryGate`, plus `BossAnchor`, `PlayerSpawns` (8) and `EntryPoint` markers.

## Notes

- glTFast's own material graphs don't compile in batch mode in this project. The builder reads base colours from the glb's JSON instead.
- The floor and decoration use the Default layer, because the project has no Ground or Environment layers. Water and mist use TransparentFX.
- The scene isn't added to Build Settings, which is a shared file.
- Whether the entry bridge closes during a fight is still undecided. The `EntryGate` collider is in place but switched off.
