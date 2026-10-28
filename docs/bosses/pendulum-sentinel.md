# The Pendulum Sentinel

**Counterweight of the Heights.** A Skyward Spire miniboss: a 4.6 m ivory and cobalt bell tower (about 2.5× a 1.8 m hero) on a tripod of three bladed legs. A brass pendulum swings inside him, and he has three arms (two in front, one behind), each holding a brass bell-weight. Everything he does is about weight and swing: the pendulum sweeps the floor in shrinking arcs, the weights must be caught, and the plumb line shows the only level ground.

- **Status:** proposal (Risxhb reviewed it on 26 Oct 2026; it still needs Chzy18's approval). Being built as a practice fight on its own Boss Rush entry: **Skyward Spire → Pendulum Sentinel**, in the Minibosses row. Start it directly with `LastBell.exe -lastbellPractice pendulum-sentinel`.
- **Sources read:**
  - `docs/design/content-rules.md`
  - the Chimebound Oracle and Dial Warden designs
  - the 21 Oct nine-miniboss proposal (`docs/mini-bosses/design.md` on `docs/nine-mini-bosses-20261021`)
  - Risxhb's review notes of 26 Oct
- **Results, 26 Oct 2026:** player `builds/pendulum-sentinel-20261026/run2` (0 errors / 50 warnings).
  - Acceptance test **70/0**, zero runtime errors (`-pendulumSentinelTest`, `tools/pendulum-sentinel/build-and-test.sh`). Evidence: `build-evidence/pendulum-sentinel-20261026/run2/test/`.
  - It covers walking routes for the whole swing combo from 584 start positions at 4.2 and 6 m/s on every level and tempo.
  - It covers hits on players standing still next to him and at long range for each attack.
  - It covers soak shares, 9 orbs per missed weight, plumb crowding and drift, the stuck window, Hard's Backswing, the 50% transformation and defeat cleanup.
- **In the main player since 27 Oct 2026:** `Play-Last Bell` was rebuilt with the first version of this fight (Pendulum Sentinel 70/0, Confrontation 110/0). `Play Pendulum Sentinel Practice.cmd` starts it. Full suite results are in `docs/current-handoffs.md`.
- **Harder pass, 27 Oct 2026:** player `builds/pendulum-sentinel-20261026/harder1` (0 errors / 50 warnings). Acceptance test **84/0**, zero runtime errors. Evidence: `build-evidence/pendulum-sentinel-20261026/harder1/test/`.
  - Swing routes: 2,917 boss and player positions per level, tempo and speed. Swing 1 is at most 2.0 s on Medium (2.14 s below 50%).
  - Stroke routes, single and triple: 434 positions each.
  - Plumb Line: the worst walk is 9.0 m, with time for 9.9 m.
  - `Play-Last Bell` still has the first version; it needs another rebuild for this one.
- **Bigger-shapes pass, 27 Oct 2026:** Built to `builds/pendulum-sentinel-20261026/harder2/LastBell.exe` (0 errors, 39 warnings); the Pendulum acceptance test passes 92 of 92 (evidence in `build-evidence/pendulum-sentinel-20261026/harder2/`). In the solo weights run, 7 weights burst into 63 orbs; the test bot, which doesn't dodge orbs, was hit by 32 of them, and the other 31 bounced 3 or 4 times and vanished. Needs a playtest; not yet in `Play-Last Bell`.
- **Body:** Chzy18's Meshy model (`Meshy_AI_Celestial_Clockwork_S`), rigged in Blender with `prototype/arena-3d/models/blender/pendulum_sentinel_rig.py`. Unity copy: `unity/Assets/LastBell/Characters/PendulumSentinel/Resources/Characters/PendulumSentinel.glb`.
- **Code:** `unity/Assets/LastBell/Run/Encounters/PendulumSentinel/`.
- **Numbers:** first-pass targets for Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3 (game-wide rule). HP is a starting value, to be set from measured damage.

## Rules this fight follows

- **No ability text.** Players learn from his pose, the floor and a sound for each mechanic. Names show only in the F7 overlay.
- **Everything lands on his beat:** 90 BPM above 50%, 112 BPM below. The pendulum inside him swings in time.
- **No jump and no dash is ever required.** No invincibility anywhere. Each hazard hits a player at most once. No knockback (he fights near the edge often).
- **Telegraphs** are coral and amber, drawn from the real hit shape.
- **No enrage**, and every pool or zone expires.
- **Melee and ranged are both in danger:** see the threat column in each attack.

## The four mechanics

> **Harder pass, 27 Oct 2026.** Risxhb asked for roughly 5× the difficulty: faster casts, the Return Stroke cleave after he lands, and three charges in quick succession below 50%. Every warning is now as short as the content-rules escape check allows (4.2 and 6 m/s after a 0.3 s reaction, from every start position). Hits are about 25% heavier, so two mistakes in a row now down a player.
>
> **Bigger-shapes pass, 27 Oct 2026.** Risxhb asked for larger mechanics wherever they didn't already cover half the arena, Return Stroke more often, smaller Plumb Line safe spots, and Counterweight Balance as a soak whose failure bursts into bouncing orbs. The bigger shapes need slightly longer warnings to stay escapable; each one below is the shortest that passes the escape check.

### 1. Shortening Swing: Strike

A four-swing combo. **He lunges at his target first**, then each swing is faster and narrower than the last, and **he turns to face his target before each one**, so the next arc lands where the target now stands.

| Swing | Arc | Warning above 50% (90 BPM) | Below 50% (112 BPM) | Damage |
|---|---|---|---|---|
| 1 | 180° (half the arena) | the walk out, 1.5–3 beats (1.0–2.0 s) | 1.5–4 beats (0.8–2.1 s) | 55 |
| 2 | 150° (was 135°) | 1.75 beats (1.17 s) | 2 beats (1.07 s) | 55 |
| 3 | 120° (was 90°) | 1.5 beats (1.0 s) | 2 beats (1.07 s) | 55 |
| 4 | 90° (was 60°) | 1.5 beats (1.0 s) | 1.75 beats (0.94 s) | 55 |

The whole combo takes about 4.5 s (8.3 s in the first version).

- **The lunge:** he lurches straight at his target and stops 3 m short of them, never past the boss bound (9.5 m). It does no damage.
- **Swing 1's warning** is exactly as long as the worst-placed player inside the arc needs to walk out at 4.2 m/s: that walk, plus the 0.3 s reaction and a quarter second, rounded up to a half beat. It is never under 1.5 beats. The lunge keeps that walk short, so ranged players no longer get a 4 s head start.
- **Shape:** a cone from his body to the arena edge, centred on where he faces when its warning starts. The facing locks for the whole warning.
- **Tell:** the lunge, then the pendulum pulled up to one side and a ratcheting chain. Each swing's floor warning fills outward, and the pendulum swings through on the hit.
- **Escape:** step out of the cone to the side, toward and past his shoulder. The acceptance test proves every start position survives the combo, at 4.2 and 6 m/s, from five boss positions, on every level and tempo.
- **Target:** his current target (the player he faces). In co-op it's the player with the highest threat.
  - Below 50%, swings 2 and 4 turn to the player **farthest** from him.
  - Solo, every swing turns to the one player.
- **Threat:** melee, the cone starts at his body. Ranged, it reaches the edge.
- **Hard, Backswing:** swings 3 and 4 also hit the cone directly behind him, 1 beat after the front cone. The safe ground becomes the two side wedges.

### 2. Counterweight Balance: Ground soak

- **Tell:** all three arms lift and whirl their bell-weights.
- **The throw:** three landing circles (**4 m**, was 3 m), one along each arm's direction, 8 m from him (kept inside the arena). The weights land **2.5 beats** later (3 below 50%, so a player under a circle's centre can still walk out of the bigger circle).
- **A soak:** a weight is absorbed by anyone inside its circle **when it lands**, or by anyone who **steps into it while it lies on the floor**. It lies there for **3 beats** (2.0 s; 1.6 s below 50%), its ring counting down. Absorbing it deals **75 damage, shared** equally by everyone inside at that moment, and no orbs. One player absorbing alone survives from full HP.
- **Not absorbed in time:** it bursts into **9 orbs** (0.5 m, **25 damage** each) that fly out from its centre at **7 m/s** and **bounce off the arena edge 3 or 4 times** before they vanish. Chevrons inside each circle show their first paths. An orb pops on the first player it hits.
- **Solo:** the circles sit 5.5 m out and land one after another, **2 beats** apart. Absorbing all three costs 225 HP, so a solo player absorbs one or two and dodges the bouncing orbs of the rest.
- **Per cast:** **3 throws** in both phases. The next throw goes up as soon as the last one lands, so weights from one throw can still be lying on the floor while the next is in the air. Below 50% the whole set turns 40° between throws. He waits for every orb to vanish before his next attack.
- **Threat:** melee, the circles are 8 m out, so absorbing them pulls players off him, and bouncing orbs cross the centre. Ranged, the orbs bounce off the edge where they stand.

### 3. Plumb Line: Ground, whole floor with small safe marks

- **Tell:** the pendulum stops dead. A thin gold plumb line drops from the tower to the floor and the whole floor shows a faint coral hatch. He walks to the centre first.
- **Safe marks:** **four** level marks (**1.2 m** circles; 1.8 m in the harder pass, 2.2 m in the first version), 6 to 10 m from him and 90° apart. In co-op there is one mark per two players, at least four. Two players fit in one mark only by standing close together.
- **Countdown:** **4 beats** (2.7 s; was 6). Then everything outside the marks is hit for **90**.
- **Why four marks:** with three, the worst spot on the floor is 12.2 m from a mark, which needs about 5 beats. With four 1.2 m marks it is 9.6 m, with time for 9.9 m, so the countdown can stay 4 beats.
- **Crowding:** a mark holds at most 2 players. A third player cracks it, and everyone in it is hit.
- **Below 50%:** a 5-beat countdown (2.7 s). The marks drift round him for the first 3 beats (one direction, 15° per beat, 45° in all), then lock for the last 2. The drift is kept at 15° per beat so a walking player can keep pace with a mark.
- **Threat:** melee, the marks are at least 6 m out. Ranged, they are at most 10 m out, so the edge is never safe.

### 4. Return Stroke: Strike lane, then a Ground cleave where he lands

- **Tell:** the tower leans toward his target, the pendulum swings forward and the legs bunch.
- **How often:** **every other attack** in both phases (was once per four).
- **The charge:** a **5.5 m** wide lane (was 4 m) from him to the **arena edge**, shown for **2 beats** (1.33 s; 3 in the first version). He charges it in **0.75 beat**: **75** to anyone his body passes, and the blow carries on to the edge, hitting the rest of the lane as he slams to a stop. His body stops at the boss bound (9.5 m from the centre).
- **The cleave, after he lands:** **0.75 beat after he slams the edge**, the pendulum swings back and cleaves the **half-circle** (radius **9 m**, was 7 m) **behind him**: round where he stopped, on the side he charged from. **65** damage. Its floor warning is drawn with the lane, so players can see where it will land.
- **Stuck:** after the cleave he stays planted for **3.5 s** with nothing else live. This is the fight's major punish window. Then he walks back toward the centre.
- **Escape:** step out of the lane, then keep out of the 9 m half-circle round its end. Players at the wall beside where he stops are safe, as is anyone more than 9 m back from that point.
- **Below 50%: three strokes back to back.** Each new lane appears **the moment he lands**, aimed at his target, while the last stroke's cleave is still coming. Each lane shows for 2.5 beats (1.34 s at 112 BPM; the wider lane needs it), so the charges land about every 1.75 s. The 3.5 s stuck window comes after the third cleave.
- **Threat:** melee, the cleave covers the ground behind him where melee players chase him to the wall. Ranged, the lane runs to the edge, and he ends up beside whoever stood there.
- The acceptance test proves the target can walk clear of every lane and cleave, single and triple, from every start position at 4.2 and 6 m/s.

## Phases and rotation

- **Above 50%:** Shortening Swing → Return Stroke → Counterweight Balance → Return Stroke → Plumb Line → Return Stroke, then repeat. Blocks are **1 beat** apart (was 2), and each attack's recovery is half a beat.
- **At 50%:** he finishes the current attack, then plays a 2 s harmless transformation. The tower shudders and the arms rise. He stays damageable throughout and doesn't heal. The beat goes from 90 to 112 BPM.
- **Below 50%:** at most two patterns overlap. Return Stroke (triple) comes every other attack:
  - Plumb Line, drifting.
  - Return Stroke, triple.
  - Shortening Swing with Counterweight Balance: one throw lands during swing 3 and lies as a soak through swing 4.
  - Return Stroke, triple.
  - Shortening Swing, alone.
  - Return Stroke, triple.
- **Max HP:** 3000 (starting value).

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Swing warnings (beats, above 50%) | swing 1 the walk out +1 (at least 2.5), then 2.75, 2.5, 2.5 | swing 1 the walk out (at least 1.5), then 1.75, 1.5, 1.5 | as Medium |
| Counterweight warning / lying soak | 3.5 beats (4 below 50%) / 4 beats | 2.5 beats (3 below 50%) / 3 beats | as Medium |
| Plumb Line marks | 1.8 m | 1.2 m | 1.2 m |
| Return Stroke lane shown / cleave after landing | 3 beats / 1.25 (3.5 / 1.25 below 50%) | 2 / 0.75 (2.5 / 0.75 below 50%) | as Medium |
| Named extra mechanic | none | none | **Backswing** (swings 3 and 4 also hit behind him) |

## Rig clips

The tripod legs (thigh and spike foot each) and the three arms are rigid parts pinned to bones. The pendulum inside the tower is its own bone.

| Clip | Use |
|---|---|
| Idle, Walk | Walk is a tripod gait: one leg forward per step |
| SwingWind, SwingLeft, SwingRight | Shortening Swing: the pendulum pulled to one side, then each swing (stretched to the warning) |
| WeightSpin, WeightThrow | Counterweight Balance: all three arms whirl their weights, then fling them out |
| PlumbDrop, PlumbLoop | Plumb Line: pendulum stopped, the tower settles low and still |
| ChargeLean, Charge, WallSlam, StuckLoop | Return Stroke: lean, run, the slam at the end and the 3.5 s stuck window (ChargeReturn is spare). Charge also plays for the swing lunge |
| PhaseShift, Defeat | |

## Not done yet

- A human playtest for difficulty and readability.
- Music on his 90/112 BPM beat (the default boss music plays).
- Co-op is only exercised with test roster players; networking is on hold.

## Conflicts with the settled rules

- **Faster casts (27 Oct), resolved.** §5 says "don't uniformly speed attacks up to make a fight harder." Risxhb asked for faster casts and then ruled that speed tuning is fine when a mechanic takes a while to go off; §5 now records that exception. Every warning is shorter, but only down to what the §2 escape check allows; the harder pass also adds new pressure (the lunge, the landing cleave, the triple charge, a third throw), not only speed.

- **Soft enrage removed.** The first draft had a 5:00 soft enrage. Content rules §5 forbid enrage timers.
- **Phase change.** The first draft held his HP at 50% while he cast. §5 says the boss stays targetable through a phase change with a short harmless transformation, so that is what he does now.
- **Hard mode.** The first draft's Hard was mostly faster warnings. §5 asks for one named extra mechanic, so Hard is now Backswing.
- **Return Stroke's wall.** §7 keeps boss positions inside 9.5 m, so his body stops there. The lane and its damage still run to the floor's edge, as Risxhb asked.
- **Counterweight Balance (27 Oct, Risxhb's call).** §5 says an empty shared soak wipes the party. Risxhb made it a soak whose failure is a burst of orbs that bounce 3–4 times, not a wipe. It is built that way. The soak damage is tuned so one player absorbing alone survives (§5). The bouncing orbs are not covered by the escape-route check (their paths depend on where everyone stands); judge them in the playtest.

## Open questions for Chzy18

- Below 50%, swings 2 and 4 turn to the farthest player. Their warnings are fixed, and a far player who stood outside swing 1 may have too far to walk in co-op. Solo is unaffected (every swing aims at the one player). Either size those warnings like swing 1, or drop the twist.
