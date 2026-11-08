# Bellkeeper v1 model — build brief

Goal: replace the in-game "Bellwarden" model (made from the older #37 sheet) with one that matches `04-bellkeeper-v1.png`, using the same route as the Vanguard, Chimesinger and Arcanist: Meshy multi-view image-to-3D, then Blender scripts (prep → rig → clips → Unity `.glb`).

## 1. Meshy inputs (ready)

`unity-session/prototype/arena-3d/models/upload/`:
- `bellkeeper-v1-front.png`
- `bellkeeper-v1-side.png` (its left side: the conducting LEFT hand faces the camera, pendulum behind)
- `bellkeeper-v1-back.png`

These are cut from the sheet: 1024 px squares on plain paper, with the scale figures, labels and rules removed. The source is only about 450 px tall per view, so expect soft detail.

**Meshy settings:** Multi-image to 3D with these three images, symmetry **off** (the two hands differ), PBR textures on, highest available polycount. Name it `Bellkeeper_v1`. Download the textured `.glb` into `upload/Meshy/`.

**What to check in Meshy's preview before downloading:**
- RIGHT hand = pendulum bell-head, LEFT hand = long conducting fingers (not mirrored).
- Clapper visible in the chest crack.
- Three broken rings behind the back, not fused into one disc.
- Feet and lower legs reasonably compact (see §2).

If a view comes out wrong, regenerate. Don't fix it in the texture.

## 2. Requirements from the fight spec (v0.3.1)

- **Height:** 5 m in game (the sheet says 5.5 m; the spec keeps 5 m for the camera until the import is checked).
- **Footprint (D8):** collision stays a 1.4 m circle. The lower legs and feet must fit a **1.6 m radius** from the body centre, or the prep step narrows the stance. Anything that still overhangs fades near the camera. The sheet's stance is wider than that, so expect to close the stance by about 15–20 %.
- **Separable parts for the fight** (cut in Blender after import, so Meshy doesn't need to deliver them separately):
  - three clock rings, which rotate independently; the back ring turns in reverse as the Unfinished Hour tell;
  - chest shell halves, which separate in phase 2 and open in phase 3;
  - clapper (the defeat catch-and-release), pendulum hand head, chains.
- **Clips** (existing names, so the encounter code doesn't change): Idle, Walk, BellSweep, HollowToll, PendulumCharge, ShatteredChime, Vulnerable, PhaseShift, Defeat. New: `EchoTell` (back ring reversing, conducting hand raised).
- **Glow:** mask eye line, clapper core (brighter by phase), sparse gold fissures. Purple stays out of the model.

## 3. After the `.glb` arrives

1. `bellkeeper_prep.py`: normalise to 5 m, narrow the stance to the footprint, decimate to about 60k tris, glow mask.
2. Part cut: rings, shell halves, clapper, pendulum head, assigned to bones.
3. `bellkeeper_rig.py`: rebuilt for the new silhouette, all clips above, plus phase variants of the shell and halo driven at runtime.
4. Export to `unity/Assets/LastBell/Characters/Bellkeeper/Resources/Characters/Bellkeeper.glb`, build, then autotest plus a contact sheet against the reference.
