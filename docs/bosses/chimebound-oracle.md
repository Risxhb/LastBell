# The Chimebound Oracle

The first of three Metronome Court minibosses. The other two, the Pendulum Sentinel and the Dial Warden, are not built yet. The Oracle is a bullet-hell caster. He is meant to feel about as hard as a current WoW heroic raid boss.

- **Status:** practice only, on its own Boss Rush entry: **Metronome Court → Chimebound Oracle (practice)**. The run order is unchanged.
- **Body:** a labelled stand-in, made from code-built shapes: a hooded robe, a bell staff and a hanging clapper. Final model, rig and animation are still to come.
- **Code:** `unity/Assets/LastBell/Run/Encounters/ChimeboundOracle/`.
- **Numbers:** every value below is a first-pass target for Medium. None is approved balance. Damage is out of 100 player HP.

## Rules this fight follows

- **No ability text reaches the player.** The phase line and intro lesson are empty. Ability names appear only in the F7 developer overlay, which is off by default, so removing it later changes nothing players see.
- **Players learn from the tells:** the Oracle's pose, the floor warnings and a sound for each mechanic.
- **Every strike lands on a metronome beat.** The court's metronome follows the Oracle's own clock.
- **No jumping.** A soak chain replaces the jump check.
- **No dash is ever required.** The lane fairness check proves every pattern can be survived walking at 80% speed.

## Phases and tempo

| | Above 50% | Below 50% |
|---|---|---|
| Metronome | Normal, **140 BPM** (weight high) | Haste, **185 BPM** (weight low, warm light) |
| Lanes | Rounds, Odds & Evens | Called Changes |
| Well groups | 2 groups of 3 wells, 6 orbs each | 2 groups of 4 wells, **9** orbs each |
| Soak chain | 3 circles, 3 s each | 5 circles, 2 s each, with lanes striking |

- **Grand Toll (50%):** once his HP reaches half, it holds there. At the next block boundary he walks to the centre, faces the metronome and rings it. Over three accented beats the weight slides down and the tempo commits to Haste for the rest of the fight.
- **The recital:** while his HP still holds, he plays one Called Changes cast at the new speed. After that he can be damaged again.
- **Last Bell (soft enrage, the berserk):** at 4:00 of combat, every lane strikes together. Between volleys, a group of 5 wells drops (9 orbs each).

## Abilities

| Ability | Tell (no text) | What happens | Damage |
|---|---|---|---|
| **Toll Lanes** | Staff raised toward the metronome. Rune lanes light up. Each lane's own pitch plays one beat before it strikes (low on the left, high on the right). | The floor is 8 north–south lanes, each 3.125 m wide. A strike rolls from the metronome end toward the camera over 0.15 s. Each lane hits a player at most once. | 45 |
| **Chime Wells** | Staff struck on the floor, a low hum, amber circles. Inside each circle, one chevron arrow per orb points outward the way that orb will fly. | Wells drop in groups: the whole group lands together, warns for 4 beats, then detonates together, firing orbs from each rim. Above 50%: two groups of 3, 6 orbs each, never while lanes are striking. Below 50%: two groups of 4, 9 orbs each. The two wells in Called Changes + Wells also fire a second ring 2 beats later, turned 20° (shown by dimmer arrows). In the Last Bell: groups of 5. | 25 eruption, 15 per orb |
| **Carillon Chain** | Arms spread, waist bells shake, the whole route shown dim. | Only the lit circle counts: stand in it for 0.5 s. A brass thread on the floor points to the next circle, and each one chimes higher than the last. Standing in an unlit circle does nothing. A circle that runs out goes off and hits everyone. Below 50%, a single lane strikes every 3 beats, never on the lit circle or the next two. | 30 to everyone per miss |
| **Resonant Mark** | Arm pointed at a player, a violet ring on them that ticks and tightens on each beat (twice per beat at the end). | After about 6 s, a small well drops where the player stands. Two beats later it fires 3 orbs. Marks up to 3 players. | 20, then 15 per orb |
| **Clapper Strike** | Clapper swung back, a whoosh. | He walks into reach, then hits a 7 m, 100° cone after about 1.3 s. | 60 |

### Toll Lanes patterns

- **Rounds:** lanes 1→8, one every 2 beats, each with a 4-beat warning. At most two lanes are lit at once, so the player walks behind the wave.
- **Odds & Evens:** lanes 2, 4, 6 and 8 together, then 1, 3, 5 and 7 four beats later. The player steps one lane over.
- **Called Changes:** every cast is a new order.
  - All 8 lane warnings appear at once (one per lane, drawn 14 cm narrower than the lane so neighbours never share a border). Each rune fills from the moment it appears until its strike, so **the fastest-filling rune fires first**.
  - 8 lanes over 6 beats: four single lanes and two pairs.
  - **Order rules:**
    - No neighbouring lanes on consecutive beats, and a pair never holds neighbours.
    - The two pairs are never back to back, so at most two lanes are ever in their last half-second.
    - From every starting lane, the cast (and its Hard echo) must be survivable walking at 80% speed after a 0.25 s reaction. `OracleLanes.Survivable` checks this with 1D reachability across the lanes.

## Difficulty

- **Easy:** lane and well warnings are 6 beats instead of 4, and soak timers are 1.4× longer. Boss damage is ×0.6 (game-wide rule).
- **Medium:** everything as described above.
- **Hard:** adds **Echo**. Below 50%, each lane strikes again 2 beats after its first hit, so the lane you just stepped into becomes dangerous again. Boss damage is ×1.3 (game-wide rule).
- The 9 orbs below 50% stay the same on every difficulty.

## Rotation

- **Above 50%:** Rounds → Wells → Clapper → Mark → Carillon → Odds & Evens → Wells → Clapper → Carillon, then repeat.
- **Below 50%:** Called → Wells burst → Mark → Called + Wells → Clapper → Carillon → Called → Mark → Wells burst → Carillon, then repeat.
- Blocks are separated by 2 beats. A Mark is quick to cast, and its drop lands during the next block.

## Tunables

All tunables are constants at the top of `ChimeboundOracleEncounter.cs`:

- BPM: 140 / 185.
- Damage values, warnings in beats, orb counts and speeds (5 / 6.5 m/s).
- Soak radius (1.6 m), dwell time (30 ticks), chain step distances and live times.
- Mark length, cone size and timing, enrage time.
- Max HP: 3000.

## Validation

The acceptance test is `ChimeboundOracleAutoTest`, run with `-chimeboundOracleTest -lastbellOut <dir>`, or with `tools/chimebound-oracle/build-and-test.sh <run>`. It checks:

- Registration.
- Fairness over 1,000 shuffled Called Changes casts each on Medium, Easy and Hard.
- Lane order, spacing, beat alignment and chimes.
- Hit-once damage for every mechanic.
- A walking dodger reading only the floor warnings takes no lane hit through Rounds, Odds & Evens and three Called Changes casts.
- Well and orb hits, orb counts and the wave turn.
- Soak order, misses and pressure lanes.
- Mark count and drop position.
- Clapper damage.
- Grand Toll HP hold and the tempo commit, including the metronome weight and light.
- Echo on Hard, Easy warnings, Last Bell and the defeat cleanup.

**Results, October 25, 2026** (Windows player `builds/chimebound-oracle-20261025/run4`, 0 errors / 46 warnings):

- **Chimebound Oracle 62/0**, zero runtime errors.
- Called Changes: 1000/1000 shuffled casts passed on each difficulty, with an average of 27 tries per cast (34 on Hard).
- **Other suites on the same shared code (run3):** Tempo Tyrant 136/0, run autotest 230/0, menu flow 140/0, Confrontation 106/0.
- Captures are in `build-evidence/chimebound-oracle-20261025/run4/test/`.

## Not done yet

- The final model, rig and animation.
- A music track on the 140/185 BPM grid. The default boss music plays now, so the beat-locked strikes don't line up with it.
- A human playtest for difficulty and readability.
- At 4:00 the arena's frenzy effects turn on, but the metronome doesn't swing wider.
- Co-op has not been tested; the soak chain counts any player. Networking is on hold.
