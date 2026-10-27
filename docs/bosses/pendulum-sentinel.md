# The Pendulum Sentinel

**Counterweight of the Heights.** A Skyward Spire miniboss: a 4.6 m ivory and cobalt bell tower on a tripod of three bladed legs, with a brass pendulum swinging inside it and three arms (two in front, one behind), each holding a brass bell-weight. Everything he does is about weight and swing: the pendulum sweeps the floor, the weights have to be balanced, and the plumb line shows the only level ground.

- **Status:** design and rig only. No encounter code yet. The rigged model with every clip below is `prototype/arena-3d/models/rigged/minibosses/PendulumSentinel.glb` (kept out of Unity's Resources until the encounter exists).
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Clockwork_S`), rigged in Blender with `prototype/arena-3d/models/blender/pendulum_sentinel_rig.py`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3 (game-wide rule).

## Rules this fight follows

The same rules as the Chimebound Oracle and the Dial Warden:

- **No ability text.** Players learn from his pose, the floor and a sound per mechanic. Names show only in the F7 overlay.
- **Everything lands on his beat:** 90 BPM above 50%, 112 BPM below. The pendulum inside him swings in time.
- **No jumping and no dash is ever required.** Every mechanic is survivable walking at 80% speed, proven by a route check.
- **No knockback.**

## The four mechanics

### 1. Shortening Swing (dodge, accelerating)

- **Tell:** he plants all three feet and the pendulum is pulled up to one side inside the tower; a deep chain-ratchet sound.
- **Each swing:** the arena is split by a line through his body. One half lights (coral, with swing arrows), then the pendulum swings through it: **50 damage** to anyone in that half.
- **The chain shortens:** each swing goes to the other half and comes faster. Above 50%: 5 swings, 4, 3, 2, 2 beats apart.
- **What you do:** stand near him and step across the dividing line after each swing. Far from him, the walk across is too long once the swings speed up.
- **Below 50%:** the dividing line turns 30° after every swing, always the same way. Players have to walk round him as well as across.

### 2. Counterweight Balance (soak split)

- **Tell:** all three arms lift and whirl their bell-weights.
- **The throw:** three landing circles (3 m) appear 8 m from him, 120° apart, one under each arm's direction. 4 beats later all three weights land.
- **Soak rules:** damage is **80, shared** among the players standing in a circle. An empty circle hits everyone for **45** (off-balance), plus 15 for each empty circle earlier in the same cast.
- **Balance:** the circles must hold numbers that differ by at most one. If they don't, the heaviest circle's soakers take 30 more.
- **Solo:** the circles sit 5 m from him (8.7 m apart) and the weights land one after another, 4 beats apart. The player soaks each in turn.
- **Two players:** the weights land in two groups: the front two together, then the back one 4 beats later.
- **Per cast:** 2 throws above 50%, 3 below. Below 50% the circles shrink to 2.5 m and the whole set turns 40° between throws.

### 3. Plumb Line (whole floor, small safe spots)

- **Tell:** the pendulum stops dead, a thin gold plumb line drops from the tower to the floor, and the whole floor shows a faint coral hatch.
- **Safe spots:** three level marks (2.2 m circles) appear on the floor, 6 to 10 m from him and at least 90° apart. In co-op there are as many marks as one per two players, at least three.
- **Countdown:** 6 beats. Then everything outside the marks is hit for **70**.
- **Below 50%:** the marks drift around him for the first 4 beats (one direction, 15° per beat) and then lock for the last 2. Each mark holds at most 2 players. A third player in a mark makes it crack, and everyone in it is hit.

### 4. Return Stroke (charge and return)

- **Tell:** the tower leans toward a target with the pendulum swung forward, and the legs bunch.
- **The charge:** a 4 m wide lane runs from him to the arena edge (shown 3 beats before). He runs it in 1 beat: **60 damage**.
- **The return:** the lane stays lit with reverse arrows. 2 beats later he swings back along it to his start: 60 again.
- **What you do:** step out of the lane and stay out until both passes are done. Crossing it in between is the classic mistake.
- **Below 50%:** three strokes in a fan, each 40° from the last (a pendulum's arc). All three lanes are shown at the start, and each fires out and back before the next.

## Phases, rotation and enrage

- **Above 50%:** Shortening Swing → Return Stroke → Counterweight Balance → Plumb Line, then repeat. Blocks are 2 beats apart.
- **At 50%:** his HP holds while he walks to the centre. The tower shudders and the arms rise; the beat goes from 90 to 112 BPM. He casts the drifting Plumb Line, then his HP is released.
- **Below 50% (combinations):**
  - Shortening Swing with Counterweight Balance: the weights land between swings, in the half that just swung.
  - Return Stroke fan.
  - Plumb Line (drifting).
  - Shortening Swing with turning lines.
- **Unbalanced (soft enrage) at 5:00:** Shortening Swing never stops, at 2 beats.
- **Max HP:** 3400.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Shortening Swing gaps (beats) | 5, 5, 4, 3, 3 | 5, 4, 3, 2, 2 | 4, 3, 2, 2, 1.5 |
| Counterweight warning | 5 beats | 4 beats | 3 beats |
| Plumb Line marks | 2.8 m | 2.2 m | 1.8 m |
| Return Stroke gap before the return | 3 beats | 2 beats | 1 beat |

## Rig clips

The tripod legs (thigh and spike foot each) and the three arms are rigid parts pinned to bones. The pendulum inside the tower is its own bone.

| Clip | Use |
|---|---|
| Idle, Walk | Walk is a tripod gait: one leg forward per step |
| SwingWind, SwingLeft, SwingRight | Shortening Swing: pendulum pulled to one side, then each swing (stretched to the beat gap) |
| WeightSpin, WeightThrow | Counterweight Balance: all three arms whirl their weights, then fling them out |
| PlumbDrop, PlumbLoop | Plumb Line: pendulum stopped, the tower settles low and still |
| ChargeLean, Charge, ChargeReturn | Return Stroke |
| PhaseShift, Defeat | |
