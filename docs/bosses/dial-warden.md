# The Dial Warden

A Skyward Spire miniboss with four mechanics and no filler attacks. Everything he does is one of the four, and below 50% they overlap. He is meant to be the hardest of the minibosses: a raid-style fight built around memory, group positioning and one quick-time event.

- **Status:** practice only, on its own Boss Rush entry: **Skyward Spire → Dial Warden (practice)**, in the Minibosses row. The run order is unchanged. Start it directly with `LastBell.exe -lastbellPractice dial`.
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Clockwork_G`), rigged in Blender. Prep and rig scripts: `prototype/arena-3d/models/blender/dial_warden_prep.py` and `dial_warden_rig.py`. Unity copy: `unity/Assets/LastBell/Characters/DialWarden/Resources/Characters/DialWarden.glb`.
- **Code:** `unity/Assets/LastBell/Run/Encounters/DialWarden/`.
- **Numbers:** every value is a first-pass target for Medium, not approved balance. Damage is out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3 (game-wide rule).

## Rules this fight follows

- **No ability text.** The phase line and intro lesson are empty. Names appear only in the F7 developer overlay.
- **Players learn from the tells:** his pose, the floor and a sound for each mechanic.
- **Everything lands on his beat:** 120 BPM above 50%, 150 BPM below. His chest dial ticks with it.
- **No jumping and no dash is ever required.** Every mechanic is survivable walking at 80% speed. Automated route checks prove this for Constellation and Mainspring.
- **No knockback** from any of his hits.

## The rig

The Meshy mesh is 114 rigid parts, so each part is pinned whole to one bone. There are no deforming weights.

- **Bones:** hips, body (the sphere), head, chest dial, right arm and blade, left arm and counterweight chain, and thigh, shin and foot on each side.
- **Right arm:** the arm and blade are one straight piece from shoulder to tip, split at the wrist. The whole arm is the clock hand.
- **Clips (16):**

| Clip | Length | Use |
|---|---|---|
| Idle, Walk | 4 s / 1.2 s loops | |
| SkyRaise, SkyLoop | 1.5 s / 2 s loop | Constellation: blade to the sky, dial spinning |
| Hoist, HoistLoop | 1 s / 1 s loop | Tip the Scales: blade planted, weight whirled overhead |
| WeightSlam, BalanceLoop | 2 s (impact 60%) / 2 s loop | the throw, then rocking like a scale during the weigh |
| HandPlant, HandLoop | 1.2 s / 2 s loop | Arrest the Hand: blade held level, dial turning |
| Caught, BreakThrough | 0.6 s / 1 s | a parry lands / fails |
| GroundSlam, Collapse | 2.2 s (impact 59%) / 1.6 s (clench 69%) | Mainspring |
| PhaseShift, Defeat | 2 s / 2.5 s | |

## The four mechanics

### 1. Constellation (memorise a path)

- **Tell:** the blade rises to point at the sky, his dial spins, and a high star chime sounds for each line.
- **Draw:**
  - 6 stars appear on the floor (1.5 m circles).
  - Lines draw between them one at a time, one per beat, tracing a path through all 6. Each line rings the next note of a rising scale.
  - Then the lines fade, leaving only the stars.
- **Fire:**
  - After a gap, the path fires in the order it was drawn, one segment per beat.
  - Each segment is a 1.2 m-wide beam along its line, and its two stars erupt with it. 45 damage per hit.
  - A segment re-flashes half a beat before it fires.
- **Placement:** stars sit within 10 m of the centre, at least 4 m apart, and no segment is shorter than 4 m.
- **Below 50%:** two constellations, gold and silver, 5 stars each.
  - Their lines are drawn alternately.
  - Gold fires in drawn order and silver in reverse, on alternating beats (8 segments over 8 beats).
- **Fairness:** `DialWardenRoutes.Check` works on a 0.5 m grid in 9-tick steps, with every hit area padded by 15 cm. It checks that the set of survivable starting cells is not empty, and that every point on the floor can walk to one of them (at 80% walking speed) during the warning. Each cast is rolled until it passes (up to 40 rolls). When a Mainspring fires with a Constellation, the gap before firing is at least 6 beats.

### 2. Tip the Scales (group balance)

- **Tell:** he plants the blade point-down and whirls the counterweight overhead.
- **Each throw:**
  - **Target:** a landing circle (2.5 m) appears 5–9 m from the centre.
  - **Mirror point:** at the same moment, a balance needle runs from the centre to the mirror point on the opposite side. A target circle sits there (2.5 m).
  - **Impact:** 3 beats later, the weight lands. 60 damage to anyone under it.
  - **The weigh:** 4 beats after impact. The **average position of all standing players** must be inside the target circle.
- **Fail:** an off-balance weigh hits everyone for 35, plus 15 for each earlier miss in the same cast.
- **Per cast:** 3 throws, each landing at least 90° away from the last.
- **Solo vs co-op:** solo, stand in the target. In co-op, the group arranges itself so its centre sits there. Players who are down don't count.
- **Below 50%:** two weights land together, at most 120° apart. The target is the mirror of their midpoint, pushed out to at least 4 m from the centre.

### 3. Arrest the Hand (the one QTE)

- **Tell:** he walks to the centre and plants himself with the blade held level. His dial turns with the hand.
- **The hour markers:**
  - 12 markers ring the dial 10 m out. Some light up.
  - **How many:** solo 1. In co-op, one for every two players (rounded up), at most 4.
  - **Which:** chosen from hours 3–11, at least 2 hours apart.
- **The lap:** 4 beats after the plant, the hand sweeps one full lap clockwise from 12, one hour per beat. It hits anyone between 4 m and the edge as it passes, for 35 damage. The circle within 4 m of him is safe.
- **The QTE:**
  - **When it opens:** 4 beats before the hand reaches a lit hour, for whoever stands on that hour (within 1.4 m).
  - **Bracing:** that player is braced (rooted) until it resolves.
  - **The keys:** a sequence of directions (W A S D or arrow keys), then **E**, timed to the moment the hand arrives.
  - **Timing:** directions can be entered at any pace. E only counts within ±8 ticks (±0.13 s) of the arrival.
  - **Failing:** a wrong key, E too early, or nothing by the end of the window.
- **Catch:** the hand stops dead for a beat (clang, Caught clip). The parrier isn't hit, and the lap continues.
- **Miss:** the hand breaks through and keeps going, hitting the braced player too. A lit hour nobody stands on also counts as a miss.
  - After the lap, the hand runs one more lap at double speed over the whole floor, with no safe circle.
  - That lap hits everyone once for **55 × the number of misses**.
  - Two misses kill on Medium.
- **Below 50%:**
  - The hand runs counter-clockwise.
  - In co-op, one more hour lights.
  - Solo keeps one hour but adds one key to the sequence.

**The QTE box** (`DialWardenQteHud`) floats above the braced player's head:

- **Layout:** a small dark box with a thin gold edge and one key cap per input. Directions show as arrows with their W/A/S/D letter in the corner; the last cap is **E**.
- **States:** finished caps dim to gold, the current cap is bright with a thick gold edge, and upcoming caps are muted.
- **The E cap:** a ring closes onto it, landing exactly on the arrival beat. It turns bright gold inside the window.
- **Result:** success flashes the box gold; failure turns the caps red and shakes the box. The box then fades.
- **No words**, and it shows only for the local player.

### 4. Mainspring (expands outward, then collapses back in)

- **Tell:** the gaps appear 6 beats before the slam (8 for the double slam below 50%), each a lit sector from his feet to the edge: outward gaps bright teal, return gaps dim. In the last 4 beats both arms go up, he hops, then the whole body slams down.
- **Out:**
  - **The wave:** a glowing ring wall with gaps leaves his body (2 m) and runs out to the balustrade at 7 m/s.
  - **Gaps:** 3 gaps of 30°.
  - **Hits:** anyone the wall passes, outside a gap, takes 45.
- **Back:**
  - **The rebound:** at the edge it rebounds and collapses inward at 9 m/s. Its gaps are turned 60° from the outward ones.
  - **The implosion:** one beat after it reaches him, the centre bursts within 5 m for 45 damage.
- **What you do:** stand in an outward gap close to him, slide sideways into a return gap before the wave comes back, then step out past 5 m.
- **Below 50%:** two slams, 2 beats apart, each with its own gaps (the second wave's outward gaps are turned 20–40° from the first). The waves run 25% faster with his beat.
- **Fairness:** the same route check runs over the wave's path. The warning lengths come from tuning with `tools/dial-warden/routes_tune.py`, an offline copy of the check. A 4-beat warning left the far side of the arena too far from a gap below 50%, and Hard's first plan of 2 gaps left no route at all.

## Phases, rotation and enrage

- **Above 50% (teaching):** Constellation → Tip the Scales → Arrest the Hand → Mainspring, then repeat. Blocks are 2 beats apart.
- **At 50%:**
  - His HP holds at half while he walks to the centre and plays PhaseShift.
  - His beat goes from 120 to 150 BPM.
  - He casts the below-50% Arrest the Hand. His HP is released when it ends.
- **Below 50% (combinations):**
  - Constellation with Mainspring: the wave launches as the path starts firing.
  - Tip the Scales with a single Constellation: the weights land while the path fires.
  - Arrest the Hand, reversed.
  - Double Mainspring.
- **Midnight (soft enrage) at 5:00:** gapless Mainspring waves every 6 beats.
- **Max HP:** 3600.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Constellation lines | stay faintly visible | fade after drawing | fade after drawing, no re-flash |
| Constellation gap before firing | 6 beats | 4 beats | 3 beats (6 when a Mainspring fires with it) |
| Tip the Scales target circle | 3.5 m | 2.5 m | 2.0 m |
| Weight landing warning | 4 beats | 3 beats | 3 beats |
| QTE keys (directions + E) | 2 + E | 3 + E | 4 + E |
| QTE prompt lead | 5 beats | 4 beats | 3 beats |
| Parry window | ±12 ticks | ±8 ticks | ±5 ticks |
| Mainspring gaps | 4 of 36° | 3 of 30° | 3 of 26°, return gaps turned 90° |

## Not done yet

- A music track on his 120/150 BPM beat. Default boss music plays for now.
- Glow on his body's stars for the Constellation tell (it needs an emissive mask on the texture).
- A human playtest for difficulty and readability.
- Co-op hasn't been tested; networking is on hold. Tip the Scales and Arrest the Hand are written for 1–8 players.
