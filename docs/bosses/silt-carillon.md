# The Silt Carillon

**Warden of the Water Steps.** A Verdant Chimes miniboss: a 2.7 m dome of mossy ivory stone with blue rose windows, carried on four brass-root legs. Three bells hang under its front arch below a small ivory face. It once measured the flow of an aqueduct by tolling the water steps. Its fight is a tide that rises and falls in rings, three bells that call a sequence, silt that has to be cleared and stones that fall from the old aqueduct.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/SiltCarillon.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Verdant_Bell_Shrine`), rigged in Blender with `prototype/arena-3d/models/blender/silt_carillon_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3.

## Rules this fight follows

- **No ability text.** Names only in the F7 overlay.
- **Everything lands on its beat:** 96 BPM above 50%, 116 BPM below.
- **No jumping and no dash required.** Every mechanic is survivable walking at 80% speed.
- **No knockback.**
- **Its danger colour is coral, never turquoise.** The arena's turquoise water stays decoration; resonance on the floor is coral.

## The four mechanics

### 1. Rising Silt (moving safe ring)

- **Tell:** it squats on its four legs and the rose windows dim; a low rushing sound.
- **The rings:** the floor is cut into 5 rings, each 2.4 m wide, from its body to the edge.
- **The tide:** the tide floods inward one ring per 2 beats, from the outside, then ebbs back out. At any moment one ring is dry (bright), the next one in is drying, and the rest are flooded.
- **Damage:** **35** per beat to anyone in a flooded ring.
- **What you do:** walk in a ring at a time with the dry ring, then back out with it.
- **Below 50%:** the dry ring is not whole. It has a 120° flooded arc that turns with it, so players walk around as well as in and out.

### 2. Triple Toll (memory and dodge)

- **Tell:** the three bells light up one by one in an order (left, middle, right in some order), each with its own note and floor glyph.
- **The shapes:**
  - **Left bell:** a 120° cone from its front to the edge.
  - **Middle bell:** a 6 m circle around it.
  - **Right bell:** a ring from 4 m to the edge.
  - **45 damage** each.
- **The order:** after showing the order (one bell per beat), it waits 2 beats, then tolls them in that order, 2 beats apart.
  - The floor warning shows only the shape coming next, and only 1 beat ahead.
  - The order has to be remembered from the lights.
- **Below 50%:** a sequence of 5 tolls (bells can repeat), shown 2 per beat.

### 3. Sediment (soak to clear)

- **Tell:** it shakes its dome; mud slaps onto the floor in pools.
- **The pools:** 3 pools of silt (2.5 m) appear around the arena; 4 for 5+ players.
- **Clearing:** a pool clears after a player has stood in it for 2 beats in total; standing in it does no damage. The pools set after 10 beats.
- **Fail:** each pool still there when they set turns to stone and erupts: **40 to everyone**, plus 10 for each other uncleared pool.
- **Solo:** the 3 pools are within reach of each other. The route is checked: walking at 80% speed clears all three with 2 beats spare.
- **Below 50%:** each clearing leaves the clearer **Silted** for 8 beats; a Silted player can't clear another pool. Solo is exempt (one pool fewer instead).

### 4. Aqueduct Fall (falling stones, dense dodge)

- **Tell:** it rears up on its back legs and the bells swing wildly. The sky shadows of falling stone appear.
- **The stones:** for 8 beats, 6 stones land per beat at random spots (2 m circles). Each shadow grows for 2 beats before its stone lands.
- **Damage:** **30** per stone.
- **Fairness:** the stones are placed so that every 2 m spot on the floor has a stone-free neighbour reachable in time. No stone lands on a spot where a stone landed the beat before.
- **Below 50%:** 9 per beat, and the stones leave rubble that lasts 4 beats (stone-coloured, harmless, but no new stone lands on it). Standing in rubble is the safe way to wait.

## Phases, rotation and enrage

- **Above 50%:** Triple Toll → Rising Silt → Sediment → Aqueduct Fall, then repeat.
- **At 50%:** HP holds. The back arch lifts, the bells drop lower, a gold regulator core shows in the dome, the beat goes from 96 to 116 BPM, and it casts a Rising Silt with the turning flooded arc.
- **Below 50% (combinations):**
  - Triple Toll during Aqueduct Fall.
  - Sediment with Rising Silt: the pools sit in the rings, so you clear them when the tide lets you.
  - Triple Toll (five tolls).
  - Aqueduct Fall with rubble.
- **Flood (soft enrage) at 5:00:** the tide never stops, and the dry ring shrinks each cycle.
- **Max HP:** 3500.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Tide speed | 3 beats per ring | 2 beats per ring | 2 beats per ring, flooded arc above 50% too |
| Triple Toll floor warning | the whole order | the next shape, 1 beat ahead | none (lights only) |
| Sediment time to set | 12 beats | 10 beats | 8 beats |
| Stones per beat | 4 | 6 | 8 |

## Rig clips

The dome, each leg (upper and lower) and each of the three bells are rigid parts pinned to bones.

| Clip | Use |
|---|---|
| Idle, Walk | four-legged walk, bells swaying |
| Squat, TideLoop | Rising Silt: low on its legs, rocking with the tide |
| TollLeft, TollMiddle, TollRight | Triple Toll: each bell swung hard, the body leaning into it |
| DomeShake | Sediment |
| Rear, RearLoop | Aqueduct Fall: up on its back legs, the bells swinging wildly |
| PhaseShift, Defeat | |
