# The Escapement Twins

**Status: proposal** (November 3, 2026). Practice-only Brass Foundry miniboss with rigged models (since November 10, 2026). Replaces the October 28 design (still in git history). Sources read: `docs/design/content-rules.md`, the old Escapement Twins design, Chzy18's Aurelion v4 (Meridian Towers, Zenith Marks, Ecliptic Order), Crucivar v4 (Casting Order, punish windows) and Sylvara v3 (Two-Colour Choir), and the Pendulum Sentinel / Chimebound Oracle encounters. Code: `unity/Assets/LastBell/Run/Encounters/EscapementTwins/`.

## Identity

An escapement is the part of a clock that locks and releases the gear train in turn: tick, tock. The Twins are its two pallets made into guardians:

- **Tick** (brass and gold, a sun face) and **Tock** (cobalt and silver, a moon face). Two 3.6 m clock towers that stand about 12 m apart on one axis through the arena centre.
- **One health bar.** Hitting either twin damages the same bar. Neither can die alone.
- **The fight is about their link:** the party splits between them, each half answering to its own twin, and whenever the escapement swaps, everyone crosses over. When they lock together in the middle, that's the opening to punish.
- **Look:** the 26 Oct model split into two rigged tower GLBs, `EscapementTick.glb` and `EscapementTock.glb`, plus an `EscapementWheel.glb` prop for the Escape Wheel (all under `unity/Assets/LastBell/Characters/`). The towers face the camera with a warm (Tick) / cool (Tock) tint; strike frames are timed by `BossClipCues`. Over LAN, Tock's clip travels as `Look[3..4]`. Brass or cobalt markers over players' heads. The primitive towers remain only as a fallback if a model can't load. No final effects or audio yet.

Every warning uses the raid indicator look (`HazardSystem.RaidStyle`). No ability text reaches players: names show only in the F7 diagnostics overlay.

## Numbers

Medium, out of 100 player HP. Easy and Hard scale boss damage by ×0.6 and ×1.3 (the shared rule), plus the per-level changes listed under Difficulty.

- **HP:** 3000, one bar. Not party-scaled (bots don't attack by default).
- **Tempo:** 100 BPM above 50 % (a beat is 0.6 s), 120 BPM below (0.5 s). Every hit lands on a beat.
- **Escape check:** every warning gives at least `distance / 4.2 m/s + 0.3 s` reaction from the worst start, and the autotest checks it.

## Abilities

Four abilities, in this order above 50 %: **Pallet Lock → Escape Wheel → Counterpoise → Lock and Release**, then repeat. Between abilities the twins' axis turns 45° (they walk round the centre), so the split is never in the same place twice.

### 1. Pallet Lock (the split). Ground.

- **Tell:** every player gets a brass or cobalt marker over their head. A dotted brass disc (4.5 m) appears under Tick and a dotted cobalt disc under Tock. Those discs are identity, not hazards.
- **The lock:** 8 beats later (4.8 s), each twin locks: everything **outside its disc** is hit, but only for players wearing **its** colour. Brass players must be on Tick's disc, cobalt players on Tock's. **50**.
- **The swap:** as the first lock lands, every marker flips. The second lock lands 6 beats later (3.6 s). Everyone crosses to the other twin; the two groups pass each other in the middle.
- **Below 50 %:** three locks (two swaps). The first comes 9 beats after the tell (4.5 s at 120 BPM), the swaps 7 beats apart (3.5 s).
- **Melee threat:** hugging the wrong twin is a hit. **Ranged threat:** nobody may stay farther than 4.5 m from a twin, so the whole outer ring is unsafe.
- **Party:**
  - 2 players: one brass, one cobalt. They swap sides each lock.
  - 3 players: two brass, one cobalt. The pair crosses one way and the single player the other.
  - 4 players: two and two.
  - Solo: one brass marker that flips each lock, so the player walks between the twins.
- **Warning:** the longest swap walk is 12.3 m (far edge of one disc to inside the other): 2.9 s plus 0.3 s reaction, so 3.6 s and 3.5 s pass. The first lock can find a player on the far side of the arena, 15.5 m away: 4.0 s, so 4.8 s and 4.5 s pass.

### 2. Escape Wheel (alternating fans). Strike.

- **Tell:** a gold escape wheel spins up on the floor between them.
- **The fans:** Tick fires a fan of six 20° spokes from its own body to the floor's edge, 60° apart. Then Tock fires its own fan of six. They alternate: **6 volleys** above 50 %, 8 below. Each volley is shown the moment the previous one lands (only one fan is on the floor at a time), 1.5 s warning (2.0 s for the first, 1.3 s below 50 %). **40** per spoke.
- **Aim:** each volley's first spoke points at the player **farthest** from that twin, and the fan steps one tooth (15°) each time. The two fans come from two different points, so a gap safe from Tick is rarely safe from Tock.
- **Melee threat:** the spokes start at the twin's body (point-blank is always inside). **Ranged threat:** they reach the edge and the first spoke aims at the farthest player.
- **Party:** nothing to assign; the farthest player is aimed at, so whoever stays back keeps moving. With 3–4 players the farthest from Tick and the farthest from Tock are usually different people.
- **Solo:** the same; the spokes aim at you.
- **Warning:** the worst escape is from the middle of a spoke at the far edge, 19.7 m from the twin that fires it: 3.7 m to the side, 1.19 s with reaction. 1.5 s and 1.3 s pass.

### 3. Counterpoise (paired soaks). Ground.

- **Tell:** the twins raise their pallet arms. Two numbered soak circles (3 m) appear, one just outside each twin (4 m beyond it, away from the centre).
- **The landing:** both land together 7 beats later (4.2 s; 8 beats, 4.0 s, below 50 %). Each circle deals **75**, shared by everyone inside it. Nobody in a circle **wipes the party** (the settled empty-soak rule). The numeral shows how many it wants.
- **Party:**
  - 2 players: I and I (one each).
  - 3 players: II on Tick's side, I on Tock's.
  - 4 players: II and II.
  - Solo: a single circle between the twins, total **70**.
  - One player alone in a circle takes 75 (98 on Hard) and survives from full HP.
- **Below 50 %:** two sets back to back. The second set is on the perpendicular axis (the floor's sides, not the twins' sides, 8 m out), 7 beats (3.5 s) after the first lands, so the pairs have to run.
- **Melee threat:** the circles are 4 m behind each twin, so melee walks round them. **Ranged threat:** the circles sit 10 m out; everyone must be in one.
- **Warning:** the worst walk into a circle is 14 m (from the edge of the floor on the perpendicular axis): 3.6 s, so 4.2 s and 4.0 s pass. The second set below 50 % is at most 12.8 m from the first: 3.35 s needed, 3.5 s given.
- **Order:** Pallet Lock always comes before Counterpoise, so the party has just practised going to its twin.

### 4. Lock and Release (the punish window). Strike, then Ground.

- **Tell:** both twins lean in toward each other. A 3 m lane joins them, and a ring (from 7 m out to the edge) is shown round the meeting point.
- **Lock:** 4 beats later (2.4 s) they slam together in the middle. Anyone in the lane takes **60** (Strike). Two beats later the shock ring lands, everyone farther than 7 m from the middle takes **55** (Ground). So: step out of the lane, then come in.
- **Punish window:** locked together for **3.5 s**. Nothing is live, and both bodies are in the middle, side by side.
- **Release:** they spring apart on a new axis (turned 90°). Their landing circles (2.5 m, **45**) are shown 2 beats before they leap.
- **Hard (named extra), Recoil:** the paths they spring along are also hit (two 2.5 m lanes from the middle to each landing, **45**).
- **Melee threat:** the lane between them and the landing circles. **Ranged threat:** the shock ring.
- **Party:** no assignment. Everyone uses the punish window.
- **Solo:** the same.
- **Warning:** leaving the 3 m lane takes 1.8 m (0.73 s); the ring then needs at most 6.7 m in 2 beats plus the lane's lead (2.4 s + 1.2 s overall, 1.9 s needed).

## Phases

- **50 % shift:** the current ability finishes, then a 2 s harmless transformation: the twins stand back to back and spin. HP holds (no heal, same bar) and they stay hittable. The tempo goes from 100 to 120 BPM.
- **Below 50 %:** the order becomes **Lock and Release → Pallet Lock (3 locks) → Escape Wheel (8 volleys) → Counterpoise (two sets)**.
- **No enrage** (content rules §5).
- **At most two damaging patterns overlap** (the lane and its ring, or the two Counterpoise circles). Each is checked by the autotest.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Pallet Lock warning | +2 beats | first 8, swaps 6 (9 and 7 below 50 %) | same |
| Escape Wheel volley warning | 2.0 s | 1.5 s (1.3 s below 50 %) | same |
| Counterpoise warning | +2 beats | 7 beats (8 below 50 %) | same |
| Lock and Release | lane +1 beat | 4 beats | **Recoil** lanes |
| Damage | ×0.6 | ×1 | ×1.3 |

## Ally bots and tests

The mechanics are built from the shared hazard rules the ally bots already read. Pallet Lock rings only hit their own colour (`Hazard.Affects`), so bots step inside their own disc. Counterpoise circles are generic soaks (`Hazard.Soak`, `ShareCount`, `ShareTotal`, `WipeIfEmpty`), which bots fill nearest-first. The rest are ordinary shapes to dodge.

`-escapementTwinsTest -lastbellOut <dir>` runs the fight with Perfect bots at party sizes 2 and 4. It checks that each ability fires and resolves, that the escape timing holds and the 50 % shift happens, and that nothing is left on the floor or thrown as an exception.

## Open questions

1. **Shared bar, two bodies:** should damage to the twins need to stay balanced (an "out of step" penalty)? It is not built: the content rules say not to add new mechanics nobody asked for.
2. **Tock's body:** Tock is a damageable boss part. Players walk through it; only Tick blocks movement. Should Tock block too? That needs a small change to the shared `CombatWorld.Constrain`.
3. **Run and LAN:** it is practice only (`inRun: false`) and not in the LAN lobby list. Should it join the Brass Foundry run pool once Crucivar's area has its minibosses?
4. **Model:** the old Meshy model and rig (`prototype/arena-3d/models/rigged/minibosses/EscapementTwins.glb`) is one body. This design needs two separate towers, so the model should be split into two.
