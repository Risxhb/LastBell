# Run loop — character asset and animation manifest

Branch `feature/run-loop`. October 21, 2026 (overnight build). Status labels used below:
**Finished** = meets the handoff for this slice; **Rough production** = real model/clip, reviewable, needs an art pass;
**Placeholder** = stand-in so the run is playable, to be replaced.

## Conventions (all three characters)

| Item | Convention |
| --- | --- |
| Units / scale | Metres. Vanguard ≈ 1.8 m, Chimesinger ≈ 1.7 m, Bellkeeper ≈ 5 m. |
| Facing / pivot | Faces +Z in Unity (−Y in Blender), pivot between the feet on the floor. |
| Rig | Blender armature built by script, exported as glTF with the animations as separate actions. Unity imports it with glTFast as a Mecanim-compatible prefab under `Resources/Characters/<Name>.glb`. |
| Export | `export_animation_mode='ACTIONS'`, forced sampling, 30 fps. Unity copies use **JPEG** textures because glTFast can't be relied on for WebP; browser copies use WebP. |
| Root motion | None in any clip. Gameplay moves the transform; `CharacterPresenter` stretches each clip to fill the gameplay duration it's given (`Action(clip, ticks)`). Dash, charge and lunge displacement come from code only, so movement is never applied twice. |
| Playback | `Run/Combat/CharacterPresenter.cs` drives the clips through a `PlayableGraph` (no Animator Controller asset), with 0.08 s crossfades. Looping: Idle, Run, Sprint, Walk, Vulnerable, BraceHold, BraceWalk, Guard. Everything else is one-shot and holds its last frame until the next state. |
| Hit timing | Never from animation events. Contact, release and resolve ticks live in the kits and encounter scripts; the clips are authored so their contact pose lands on the same normalized time. |
| Materials | glTFast URP shaders. Emission strength is restored at runtime (×2.5 Vanguard, ×2 Chimesinger, ×5 Bellkeeper, plus metallic ×0.55 on the Bellkeeper), because glTFast drops `KHR_materials_emissive_strength`. |
| Missing clips | Resolved through a fallback table (approved name first, then older clips). Anything still missing gets a labelled procedural placeholder on the visual pivot: Hit = 0.3 s flinch, Down = fall onto the back, GetUp = reverse. |

## Vanguard — model: Rough production · rig: Rough production · animation: Rough production · VFX: Rough production

- **Unity asset:** `unity/Assets/LastBell/Characters/Vanguard/Resources/Characters/Vanguard.glb` (JPEG textures, 3.9 MB, 40k tris)
- **Browser asset:** `prototype/arena-3d/models/vanguard.glb` and `vanguard-glb.js`
- **Editable source:** Meshy multi-view source `unity-session/prototype/arena-3d/models/upload/Meshy/Meshy_AI_This_will_be_a_charac_1021003259_texture.glb` (not committed: source upload). Scripts: `prototype/arena-3d/models/blender/prep.py` (decimate, glow mask) and `rig.py` (armature, weights, sword, all clips). The rebuilt rig is written to `vanguard_rig.blend` in the work folder.
- **Rebuild:** `blender -b prep.py -- <meshy.glb> <work> 40000`, then `blender -b <work>/vanguard_prep.blend -P rig.py -- <work> <browser.glb> <Soldier.glb> <unity.glb>`. Soldier.glb is the three.js Mixamo run reference, not committed.
- **Attachment points:** sword parented to `hand.R` (bone-parented mesh). The shield is skinned rigidly to `forearm.L`. Brace FX attach at the chest, 0.75 m forward.

| Clip | Length | Loop | Gameplay use (ticks @60) | Markers (normalized) | Status |
| --- | --- | --- | --- | --- | --- |
| Idle | 2.03 s | yes | stance | — | Rough production |
| Run | 0.73 s | yes | walk 6 m/s (clip stride 4.2 m/s, played ×1.43) | — | Rough production (Mixamo-retargeted legs) |
| Sprint | 0.57 s | yes | sprint 9 m/s | — | Rough production |
| Dodge | 0.60 s | no | dash 12 ticks, played 0.3 s from 0.05 | — | Rough production |
| Jump | 1.20 s | no | 36-tick airtime, played from 0.2 | takeoff 0.22, land 0.76 | Rough production; no separate Land clip |
| SteelRhythm1 / 2 | 0.33 s | no | 20 ticks, contact tick 8 | contact 0.45 | Rough production (same motion as Combo1/2) |
| SteelRhythm3 | 0.47 s | no | 32 ticks, contact tick 15 | contact 0.5 | Rough production, heavier finisher |
| SunderingBlow | 0.70 s | no | 42 ticks, release tick 20 | windup 0–0.35, release 0.48, recovery 0.75–1 | **New tonight**, rough production |
| LastWord | 1.40 s | no | 84 ticks, release tick 48 (≈0.8 s windup) | planted windup 0.15–0.5, slam 0.57, committed hold to 0.85 | **New tonight**, rough production |
| BraceRaise | 0.20 s | no | 6 ticks | — | **New tonight** |
| BraceHold | 1.03 s | yes | held up to 90 ticks | — | **New tonight** |
| BraceWalk | 0.73 s | yes | braced movement at 35 % speed | — | **New tonight** (mocap legs + held Brace upper body) |
| BraceHit | 0.40 s | no | absorbed frontal strike (12 ticks) | impact 0.2 | **New tonight** |
| BraceLower | 0.30 s | no | 9 ticks; the cooldown starts after it | — | **New tonight** |
| Hit | 0.30 s | no | hit-stun 15 ticks | — | **New tonight** |
| Down | 0.80 s | no | down / defeat, held | buckle 0–0.35, fall to 0.85 | **New tonight**, rough |
| GetUp | 0.90 s | no | run start (revive in future co-op) | kneel 0.45 | **New tonight**, rough |
| Combo1–3, DashAttack, PendulumRush, ReversalRush, FinalHour, Guard, Counter | — | — | **superseded kit**; kept only for the old `VanguardTestController` scene | — | Legacy, not used by the run loop |

Contact sheet: `run-loop-evidence/vanguard/kit-contact-sheet.png`.

**VFX** (`Run/Combat/PlayerFx.cs`):
- Steel Rhythm: cyan crescent at blade height across the hit arc. The third hit is wider and brighter, with a floor scuff.
- Opening: a turning three-tick floor ring plus motes, so it reads by shape and not only by colour.
- Sundering Blow: chop band plus a floor split. Empowered is gold, with more sparks and a screen shake.
- Last Word: a ring that tightens over the windup, then slam pillar, dust ring and a shockwave that travels toward the cursor.
- Brace: translucent rune disc in front while raised. An absorbed hit gives a short disc flash and sparks; there's no invulnerability flash.
- Dash: cyan trail.

## Chimesinger — model: Rough production (stand-in export) · animation: Rough production / Placeholder · VFX: Rough production

- **Unity asset:** `unity/Assets/LastBell/Characters/Chimesinger/Resources/Characters/Chimesinger.glb`. This is a **stand-in**: the committed f84c4d5 `chimesinger.glb` re-exported to JPEG with Blender, with no rig or clip changes. The re-export dropped four unweighted leaf finger/thumb bones and resampled the clips to about 0.02 s shorter.
- **Owner of the real asset:** the "Chimesinger animation fixes" session. It is reworking the grips, Bellstrike, Crescendo, Mending Chorus and staff clearance in `chimesinger_rig.py`, and will export the Unity JPEG GLB to the path above. That session also owns Hit / Down / GetUp, by agreement.
- **Editable source:** `prototype/arena-3d/models/blender/chimesinger_prep.py` and `chimesinger_rig.py`, plus the Meshy body and staff uploads in `unity-session/prototype/arena-3d/models/upload/Meshy/`.
- **Attachment points:** the staff is the `staff` bone under `hand.R`.

| Clip | Length | Loop | Gameplay use (ticks) | Status |
| --- | --- | --- | --- | --- |
| Idle / Run / Sprint | 2.0 / 0.71 / 0.54 s | yes | locomotion | Rough production |
| Dodge | 0.46 s | no | Harmonic Step (12 ticks) | Rough production |
| Jump | 1.17 s | no | jump | Rough production |
| ResonantNote | 0.38 s | no | 10-tick cast, projectile released at tick 6 | Rough production |
| Bellstrike | 0.67 s | no | 18-tick cast, release at tick 10 | Rough production; rework pending in the Chimesinger session |
| HarmonicRefrain | 0.71 s | no | 22-tick cast, wave at tick 12 | Rough production |
| Crescendo | 1.0 s | no | 26-tick cast, detonation at tick 16 | Rough production; rework pending |
| MendingChorus | 0.58 s | no | heal is instant on press; 36-tick flourish, movement allowed | Rough production; rework pending |
| Hit / Down / GetUp | — | — | — | **Placeholder** (procedural tilt / fall) until the Chimesinger session adds clips |

**VFX:**
- Resonant Note: gold note head with a glitter tail.
- Bellstrike: a straight bolt. A new bell rings once; a refresh rings twice.
- Ringing bell over the boss: the cone **size and the cyan ring show strength**, and **orbiting pips show the echo count (0 / 1 / 2)**, so it reads without colour. The glow dims as the bell nears expiry.
- Harmonic Refrain: three expanding ripple arcs (directional).
- Crescendo: collapse pillar and burst rings. A fully prepared finish adds a second ring and a screen shake.
- Mending Chorus: one expanding heal ring at the 6 m radius that clears within 0.45 s, so it doesn't read as a lingering zone.
- Harmonic Step: gold note motes along the dash.

## Arcanist — model: Rough production · rig: Rough production · animation: Rough production · VFX: Rough production

Handed over by the Arcanist session on Oct 21; its notes are in [arcanist-handover.md](arcanist-handover.md). The model, rig and clips are user-reviewed in that session's videos and browser viewer. The Unity kit (`Run/Combat/ArcanistKit.cs`) and FX (`PlayerFx.Arcanist.cs`) were added in this branch.

- **Unity asset:** `unity/Assets/LastBell/Characters/Arcanist/Resources/Characters/Arcanist.glb` (JPEG). The astrolabe glow is restored ×2.5 at runtime.
- **Editable source:** `prototype/arena-3d/models/blender/arcanist_prep.py` and `arcanist_rig.py`. Rebuild with `models/build-arcanist.ps1 -RunRef <Soldier.glb>`. The Meshy sources are not committed. Viewer: `prototype/arena-3d/arcanist-viewer.html`.
- **Rig:** Vanguard-like body plus 16×3 robe chains (baked cloth), and a non-deforming `orbit` bone carrying the astrolabe. 1.8 m tall, faces +Z.

| Clip | Loop | Gameplay use (ticks) | Marker → release tick |
| --- | --- | --- | --- |
| Idle / Run / Sprint | yes | locomotion | — |
| Clockbolt / Clockbolt2 | no | 8-tick cast, alternating hands | release .35 → tick 3 |
| TimeSeal | no | 12-tick cast | apply .58 → tick 7 |
| ArcLance | no | 20-tick rooted cast (dash before release cancels) | release .45 → tick 9 |
| Convergence | no | 16-tick cast | detonate .55 → tick 9 |
| TemporalAnchor | no | 12-tick place | place .5 → tick 6 |
| AnchorReturn | no | 10-tick return, teleport plus shield on press | — |
| Dodge | no | Clockstep (the shared 3 m dash; the clip was baked for 4.5 m) | — |
| Jump | no | jump | takeoff .22 / land .74 |
| Hit / Down / GetUp | — | — | **Placeholder** (procedural) |

**VFX:**
- Clockbolt: teal bolt.
- Time Seal: a closing ring on the target.
- **Seal dials:** one dial per seal, in front of the target. The hand shows time left, a **ring of ticks marks a ripe seal** and a **centre gem marks an inscribed one**. These are shapes, not just colours.
- Arc Lance: a tightening floor ring during the cast. The normal beam is narrow; the empowered beam is wide with sparks.
- Convergence: a collapse pillar plus rings. A perfect pair gets a second ring and a screen shake.
- Temporal Anchor: an upright clock-hand marker at the spot, a streak on return, then three turning shield rings.
- Clockstep: a fractured clock-ring afterimage.

## Bellkeeper (= "Bellwarden") — model: Rough production · rig: Rough production · animation: Rough production · VFX: Rough production

"Bellwarden" in the overnight request is this boss: the Meshy source is `Meshy_AI_Bellwarden_1021031533_texture.glb` and it builds `Bellkeeper.glb`. Runtime ID `boss.bellkeeper`; nothing was renamed.

- **Unity asset:** `unity/Assets/LastBell/Characters/Bellkeeper/Resources/Characters/Bellkeeper.glb` (4.4 MB, about 55k tris, JPEG)
- **Editable source:** `prototype/arena-3d/models/blender/bellkeeper_prep.py` and `bellkeeper_rig.py`. The Meshy source is in `unity-session/.../upload/Meshy/`.
- **Rebuild:** `build-bellkeeper.ps1`. Tonight's rig was run from a copy of the existing prep blend, `%TEMP%/lastbell-bellkeeper-build`.
- **Attachment points:** `pendulum` bone (chest bob), `halo` bone (broken ring). Hit sparks land at the body radius (1.4 m) at 2.2 m height; the bell mark sits at 5.6 m.

| Clip | Length | Loop | Encounter use | Status |
| --- | --- | --- | --- | --- |
| Idle | 4.03 s | yes | between attacks | Rough production |
| Walk | 1.63 s | yes | approach / reposition, 1.75 m/s | Rough production |
| BellSweep | 2.03 s | no | Bell Sweep (strike at 0.95 s ≈ resolve tick 69 at 1.15 s; slight lead) | Rough production |
| HollowToll | 2.03 s | no | Hollow Toll (ring-out at 1.10 s; resolve tick 72 = 1.2 s) | Rough production |
| PendulumCharge | 1.83 s | no | Pendulum Charge (lunge 0.8–1.1 s = ticks 48–66) | Rough production |
| ShatteredChime | 2.63 s | no | markers (taps at 1.2 / 1.6 / 2.0 s, matching the 24-tick gaps) | Rough production |
| Vulnerable | 2.03 s | yes | recovery windows | Rough production |
| PhaseShift | 1.53 s | no | queued phase transition at 70 % and 35 % HP (90 ticks, no hazards) | **New tonight**, rough |
| Defeat | 2.53 s | no (held) | defeat: falter, kneel, slump; then sinks 1.5 m | **New tonight**, rough |

Contact sheet: `run-loop-evidence/bellkeeper-phase-defeat-sheet.png`.

**VFX:** the existing `BellkeeperFx` (cone, ring, lane and circle telegraphs; sweep/toll/charge/chime resolves; hit sparks). Added: a teal tint for jump-clearable bars, an immediate `ResetAll` between runs, and a phase-shift ring burst.

## Minibosses — Placeholder (code-built stand-ins)

`Run/Encounters/Minibosses.cs`. Each has a distinct silhouette and a visible facing eye, and none has a model or clips.

| Encounter | Silhouette | Attacks |
| --- | --- | --- |
| The Dial Warden | squat navy drum with a clock face and one long clock-hand weapon | Hand Sweep, Hollow Dial |
| The Pendulum Sentinel | tall pillar with a swinging pendulum | Pendulum Charge, Low Pendulum (jump-clearable) |
| The Chimebound Oracle | floating sphere with five orbiting bells | Shattered Chime, Dividing Chime |

## Known limitations

- The Vanguard and Bellkeeper clips are procedural keyframes from the rig scripts, not hand-polished animation. The Down/GetUp silhouettes are rough.
- Hand-to-weapon contact and mesh clearance haven't been measured for the new Vanguard clips. The Brace shield arm uses the existing `GUARD_ARM` targets.
- The Chimesinger in Unity is the pre-fix stand-in, and the grip fix is pending. The Chimesinger has no Hit/Down/GetUp clips yet.
- There's no Land clip; landing is the tail of Jump.
- No audio is in the build. Every mechanic is shown visually and named in the HUD while it warns.
