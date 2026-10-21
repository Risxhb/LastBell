# Last Bell — Unity project

Unity **6000.6.6f1** (Unity 6.6) with URP. In Unity Hub: **Add → Add project from disk** → pick this `unity` folder.

## Key-art test arena

Menu **Last Bell → Test Arena (Key Art)** opens `Assets/LastBell/Arenas/KeyArt/KeyArtTestArena.unity`. It's the Bellkeeper's Dais from the browser prototype (`prototype/arena-3d/arena-env.js`): the floating platform, clock dial, parapet, lanterns, banners and the sky city. Press Play to see the banners, gears and rubble move. **C** switches between the gameplay camera and the key-art angle.

- The solid arena is `BellkeepersDais.glb`, imported with glTFast. After you change `arena-env.js`, run `node tools/export-arena-glb.mjs` from the repo root to re-export it, then use **Last Bell → Rebuild Test Arena (Key Art)**.
- `Editor/KeyArtArenaBuilder.cs` adds everything glTF can't carry: the sunset sky (`Shaders/SunsetSky.shader`), fog, ambient light, the sun, lantern lights, HDR glow materials, the floor normal map, the sea of clouds (`Shaders/CloudBillboard.shader`), post-processing and the camera. The generated assets go in `KeyArt/Generated`.
- `KeyArtArena.cs` runs the moving parts. Set `quiet` while a boss warning is up to dim the dial.

The class-test harness (**Last Bell → Class Test Arena**) is separate and still builds its own plain floor.
