# Arcanist handover for testing (2026-10-21)

The user asked the Arcanist session to hand the Arcanist to the session testing the Chimesinger and Vanguard.
Status: model, rig and all clips are user-reviewed in videos and the browser viewer. Not committed. No Unity controller yet.

## Files (all new, Arcanist-only; repo root = unity-session/)
- Viewer: `prototype/arena-3d/arcanist-viewer.html` (works from disk or http://localhost:8765/arcanist-viewer.html)
- Browser model: `prototype/arena-3d/models/arcanist.glb` (WebP) + `models/arcanist-glb.js` (base64 wrapper)
- Unity model: `unity/Assets/LastBell/Characters/Arcanist/Resources/Characters/Arcanist.glb` (JPEG, glTFast) — just written; Unity imports it when brought to front
- Scripts: `prototype/arena-3d/models/blender/arcanist_prep.py`, `arcanist_rig.py`, `models/build-arcanist.ps1`
- Meshy uploads: `models/upload/arcanist-{front,side,back,astrolabe}.png`
- Meshy sources (do NOT commit): `models/upload/Meshy/Meshy_AI_Arcanist_Body_*.glb`, `Meshy_AI_Arcanist_Astrolabe_*.glb`
- Rebuild: `powershell -File models/build-arcanist.ps1 -RunRef <Soldier.glb>` (Mixamo three.js Soldier.glb, not committed;
  a copy is in the Arcanist session scratchpad `mocap/Soldier.glb`). Builds the browser GLB, the js wrapper and the Unity GLB.

## Design source
Branch `handoff/arcanist-animation-20261021`, `docs/character-references/arcanist-claude-handoff.md` (+ sheet png).
That kit replaces ClassKits.cs's Arcanist (Spark/Arc Lance/Resonant Cast/Ward Step). Gameplay rules belong to the combat owner.

## Clips (30 fps) and integration markers (normalised clip time)
| Clip | Frames | Use | Markers |
|---|---|---|---|
| Idle / Run / Sprint | 60 / 21 / 16 | loops (run+sprint arms+legs from mocap) | — |
| Clockbolt, Clockbolt2 | 10 | Basic; alternate R/L for hold-to-repeat | release .35, recover .55 |
| TimeSeal | 16 | Offensive 1 | apply .58, recover .74 |
| ArcLance | 20 | Offensive 2, brief rooted cast | release .45, cancel_before .45, recover .62 |
| Convergence | 22 | Offensive 3, detonate | detonate .55, recover .7 |
| TemporalAnchor | 15 | Defensive: place | place .5, recover .65 |
| AnchorReturn | 10 | Defensive: recast/return | teleport + shield .3, recover .75 |
| Dodge | 12 | Clockstep dash (baked for a 4.5 m burst) | move .1-.7 |
| Jump | 36 | | takeoff .22, land .74 |
Same table is written by the rig to `%TEMP%\lastbell-arcanist-build\arcanist_clips.json`.

## Rig notes
- Bones: Vanguard-like body (hips/spine/chest/neck/head, upperarm/forearm/hand, thigh/shin/foot .L/.R), no finger bones.
- `orbit`: non-deforming bone carrying the separate Astrolabe mesh (floats by his right shoulder, animated per clip).
- Robe: 16 chains x 3 bones `robeNN_k`, baked cloth sim (gravity, drag from travel speed, leg collision). Loops are seamless.
- Model faces -Y in Blender (= +Z forward in glTF/Unity, same as the Vanguard). Height 1.8 m.

## Unity
No Arcanist controller/spawner yet. The Vanguard's `VanguardTestController` auto-spawns in scenes with `KeyArtArena`; only one test
character should auto-spawn. The user hasn't chosen how to switch characters (suggested: a swap key). If you build a switcher,
`VanguardTestController.cs` (PlayableGraph, no Animator Controller) is the template; the clip names above drop in.

## Known small issues
- Time Seal's dial gesture reads weakly from the gameplay camera; a sliver of trouser can show at the top of one sprint stride.
- glTFast may not read KHR_materials_emissive_strength: the astrolabe orb glow may need a boost in Unity (Vanguard does x2.5).
