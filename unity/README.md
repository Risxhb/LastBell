# Last Bell — Unity project

Unity **6000.6.6f1** (Unity 6.6) with URP. In Unity Hub: **Add → Add project from disk** → pick this `unity` folder.

Run `setup-assets.cmd` from the repository root before opening Unity. The [current main player](../docs/main-menu/current-player.md) is the combined menu/run build; future boss and arena changes must integrate there. Use **Last Bell → Current Player → Build Windows Player**, then the repository's `Play Last Bell.cmd`. New profiles use Alternative Confrontation; saved menu choices are respected. The direct arena scenes below remain authoring/diagnostic tools.

## Key-art test arena

Menu **Last Bell → Test Arena (Key Art)** opens `Assets/LastBell/Arenas/KeyArt/KeyArtTestArena.unity`. It's the Bellkeeper's Dais from the browser prototype (`prototype/arena-3d/arena-env.js`): the floating platform, clock dial, parapet, lanterns, banners and the sky city. Press Play to see the banners, gears and rubble move. **C** switches between the gameplay camera and the key-art angle.

- The solid arena is `BellkeepersDais.glb`, imported with glTFast. After you change `arena-env.js`, run `node tools/export-arena-glb.mjs` from the repo root to re-export it, then use **Last Bell → Rebuild Test Arena (Key Art)**.
- `Editor/KeyArtArenaBuilder.cs` adds everything glTF can't carry: the sunset sky (`Shaders/SunsetSky.shader`), fog, ambient light, the sun, lantern lights, HDR glow materials, the floor normal map, the sea of clouds (`Shaders/CloudBillboard.shader`), post-processing and the camera. The generated assets go in `KeyArt/Generated`.
- `KeyArtArena.cs` runs the moving parts. Set `quiet` while a boss warning is up to dim the dial.

The playable game is the run loop: **Last Bell → Run Loop → Open Run Scene** (`Assets/LastBell/Run/RunArena.unity`, a copy of this arena with a `RunDirector`; the menu also offers the four area arenas). See `docs/run-loop/README.md`. The old Vanguard/Bellkeeper test controllers and the Unity class-test harness were removed on Oct 21; they live on in `feature/class-test-arena` history.
