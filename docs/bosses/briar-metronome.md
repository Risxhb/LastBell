# The Briar Metronome (proposal)

**Keeper of the Garden's Time.** A Verdant Chimes miniboss. Sylvara's garden grows wild; the Briar Metronome keeps it in time. It is a tall gardener-clock of brass roots and wood with a metronome pendulum in its chest and long pruning shears. It does three things, always on its beat: it **prunes** the space around it, it **grows hedges** that pen the garden into beds, and it **tends the players as seasons**. Each player is a season (I Spring, II Summer, III Autumn, IV Winter), and the season decides which growths are theirs.

- **Status:** proposal (content rules §0.2), practice only (`inRun: false`), on the Verdant Chimes map. Placeholder body (primitives), no model, no final FX or audio. Practice: `LastBell.exe -lastbellPractice briar` or `Play Briar Metronome Practice.cmd`.
- **Code:** `unity/Assets/LastBell/Run/Encounters/BriarMetronome/` (`BriarMetronomeEncounter`, pure rules in `BriarMetronomeRules`, the acceptance test `BriarMetronomeAutoTest`, F7 diagnostics).
- **Replaces** the old design (In/Out cuts, Overgrowth, Weight of Seasons, Lost Seasons seed puzzle). The old text and rig notes stay in git history. The rigged model `prototype/arena-3d/models/rigged/minibosses/BriarMetronome.glb` can replace the placeholder later.
- **Numbers** are for **Medium**, out of 100 player HP. Easy and Hard scale boss damage ×0.6 and ×1.3 (the shared rule) and change the things listed under Difficulty.

## How it differs from Sylvara

Same garden, opposite fight. Sylvara is a bullet-hell with no rests where only your colour hurts you. The Briar Metronome fires **no bullets**: it is a slow, readable telegraph fight on a steady metronome beat, with a real punish window. Its seasons are **ownership** (your graft, your seed), not "your colour hurts you". Its growths change the **shape of the arena** (hedges, bramble patches) instead of filling it with petals.

## Rules this fight follows

- **Everything lands on its beat:** 108 BPM above 50% (a beat is 33 ticks, 0.55 s), 132 BPM below (27 ticks, 0.45 s). A tick sounds on every beat (placeholder: none yet).
- **Raid warnings:** every warning uses the Telegraph-Style v2 look (`HazardSystem.RaidStyle`).
- **Escape routes** are checked walking at 4.2 m/s after a 0.3 s reaction (the acceptance test checks the cut chain and every ground shape).
- **No knockback** on any of its hits (a short 0.1 s stun only), so hedges never pin anyone.
- **Melee and ranged both in danger:** Pruning Tempo (both), Hedgerow (Root Collar near, the first hedge at the farthest player), Ripening (the weight hunts the farthest player). Season Grafts send every player out to a pen and back through the boss's inner path.
- **No enrage.** Hedges and bramble patches always wither.

## Seasons

At the start each player gets a season numeral, shown as an ivory numeral over their head (identity only, it never hurts):

| Players | Seasons |
|---|---|
| 1 | I II III IV all yours |
| 2 | player 1: I and III, player 2: II and IV |
| 3 | player 1: I and IV, player 2: II, player 3: III |
| 4 | one each |

If a player goes down, their seasons pass on to the next standing player (content rules: a mark skips downed players).

## The four abilities

### 1. Pruning Tempo — in and out, on the beat (Strike / Ground)

It stands still and snaps its shears on the beat. A chain of cuts, each one of:

- **Out:** the shears sweep round it, a **6 m circle** (Strike). Safe: away from it.
- **In:** roots erupt from **3.5 m to the edge** (Ground). Safe: hug it.
- **Across (phase 2; Hard: always):** a **90° cone to the edge**, aimed at the farthest player when it appears (two cuts ahead) and then fixed (Strike). Safe: beside it.

**45 damage** each. The chain is **4 cuts, 3 beats apart** above 50% and **6 cuts, 3.5 beats apart** below (1.67 s and 1.59 s; Easy: 4 beats). The first cut gives 7 beats of warning; after that the current cut and the next one are always shown (the current one on top). The order is random but never repeats Across twice.

- **Escape:** between In-safe (inside 3.2 m) and Out-safe (outside 6.3 m) is a 3.1 m walk, 1.04 s at 4.2 m/s with the reaction; the gaps are 1.67 s (phase 1) and 1.59 s (phase 2). Phase 2 needs the extra half beat: a player at the far wall who sees an In cut appear two cuts ahead has 9.7 m to walk. Because the next cut is always shown, you wait for the current cut on the side nearest the next safe area (and outside the next Across cone). The acceptance test walks every phase 1 chain and 40 random phase 2 chains from every start on the floor at 4.2 m/s with this lookahead.
- **Melee:** Out hits anyone hugging it. **Ranged:** In hits everything from 3.5 m to the wall.
- **2 / 3 / 4 players:** everyone reads the same chain; nothing to split. In co-op the Across cone follows whoever is farthest, so the ranged players have to pick their side early. **Solo:** the same.

### 2. Hedgerow — the garden is penned (Strike, then standing)

It drives its shears into the floor (it stays planted until the grafts are done).

1. **Root Collar:** a 3.5 m circle round it, 2.5 beats (Ground, **40**). Melee steps out.
2. **Four hedges** grow from 4 m out to the wall, 90° apart, **the first one aimed at the farthest player**. Each is a 1.6 m wide lane that grows **3 m per beat** in 3 m sections from 4 beats after the cast; whoever a section grows over takes **40** (Strike). Every section is shown from the start, so you step sideways (1.1 m) long before it reaches you.
3. Grown sections stay as **bramble** (a standing burn, **25 per second**, no knockback) until the grafts are cut or bloom, then wither.

The hedges split the floor into **four pens**, one per season. The pens connect only through the **inner path**: the ring inside 3.2 m round its body where no hedge grows (or by walking through bramble and paying for it).

- **Melee:** the Root Collar. **Ranged:** the first hedge grows under the farthest player.
- **2 / 3 / 4 players / solo:** always four hedges. Everyone just steps off the hedge lines.

### 3. Season Grafts — cut your own growth (boss parts)

When the hedges are grown, a **graft** sprouts in the middle of each pen, 8.5 m out, carrying that pen's season numeral. A graft is a boss part (like Sylvara's Choir Buds): any class's attacks hit it, and every point dealt to a graft is also dealt to the boss.

- **Only its season can cut it:** a graft takes damage only while **its owner stands within 3.5 m of it**. Otherwise hits show "held". (If its owner is down, anyone can cut it.)
- **Graft HP 90; it blooms after 12 s**, or **15 s** when someone owns two grafts (1–3 players). The time shows as a filling ring at its base.
- **Bloom:** when the time runs out, a **6 m circle** round it warns for about 2 s (3.5 beats, 4.5 below 50%), then bursts (**50**, Ground) and every standing player also takes **15** pollen.
- When every graft is cut or bloomed the hedges wither and it pulls its shears out of the floor: **Rooted, 3.5 s**, nothing live. This is the fight's major punish window.

| Players | What you do |
|---|---|
| 4 | each player goes to their own pen and cuts one graft |
| 3 | player 1 owns I and IV (adjacent pens: through the inner path, or across one hedge), the others one each |
| 2 | each player owns two **opposite** grafts: cut one, run back through the inner path past the boss, cut the other |
| 1 | only grafts I and III sprout (15 s before they bloom) |

### 4. Ripening — seeds and the metronome weight (Ground)

It shakes its moss mantle; a seed falls toward every player, and it whirls the weight on its chain.

- **Seeds:** a **4 m circle** follows each player for 4 beats, then locks and bursts 1.5 beats later (**30**). A seed never hurts its own carrier, only others inside it: **spread out**. Where it bursts it leaves a **2.2 m bramble patch** (20 per second) for 8 beats, so the floor changes under the party's feet.
- **Metronome Weight:** a **5 m circle** follows the **farthest player** (at most 6 m/s) for 3 beats, then locks and lands 3 beats later (below 50%: follows 3.5 beats, lands 4 beats after locking) for **60** (Ground). Everyone steps out of it, the target included.
- **Below 50%:** a second weight is thrown at the player farthest from where the first one landed.
- **Melee:** nothing direct (it is the ranged-punishing attack). **Ranged:** the weight always goes to the farthest player.
- **2 players:** two seeds, easy spacing; the farther player handles the weight. **3–4 players:** fan out to different directions round it before the seeds lock; the weight target walks away from the group. **Solo:** your seed cannot hurt you, but walk off your own bramble patch; the weight follows you.

## Phases, rotation and punish window

- **Phase 1 (100–50%), 108 BPM:** Pruning Tempo (4 cuts) → Hedgerow + Season Grafts → Rooted → Ripening, repeat.
- **At 50%:** it finishes the current attack, then a **2 s harmless shift** (the pendulum swings faster; it stays damageable, no heal, same bar). The beat goes to 132 BPM.
- **Phase 2 (50–0%), 132 BPM:** Pruning Tempo (6 cuts with Across) → Ripening (two weights) → Hedgerow + Season Grafts → Rooted, repeat.
- **At most two damaging patterns overlap:** standing bramble plus one timed attack.
- **Punish window:** Rooted, 3.5 s after every Hedgerow (content rules §5).
- **Max HP 3200** (provisional; set from measured DPS later).

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| Cut spacing (above / below 50%) | 4 / 4 beats | 3 / 3.5 beats | 3 / 3.5 beats |
| Across in phase 1 | no | no | **yes (Hard's extra mechanic)** |
| Root Collar warning | 3.5 beats | 2.5 beats | 2.5 beats |
| Graft HP / bloom time (4 players) | 70 / 15 s | 90 / 12 s | 90 / 12 s |
| Seed lock to burst | 2 beats | 1.5 beats | 1.5 beats |

## Open questions for review

1. **Telegraph motif:** Telegraph-Style v2 has motifs for Skyward Spire and Brass Foundry only. The fight uses the shared default (`RaidStyle`, Skyward light rays) until a Verdant Chimes motif exists. Add a leaf/vine motif?
2. **Grafts by owner:** "only its season can cut it" is checked by the owner standing next to it (the game doesn't know who dealt a hit). Is that clear enough, or should hits be tagged with the attacker?
3. **Season markers** are plain ivory numerals over heads (the provisional identity marker). Do we want seasonal colours on top (still not coral/amber)?
4. **HP 3200 and graft HP 90** were set without measured DPS; the bots' cut times in the test are the only data.
5. **LAN:** hazards replicate as usual, but the placeholder hedges, grafts and numerals are local props and won't show on a LAN client yet.
6. Should the Rooted window reward cutting every graft (e.g. a longer window), or stay the same either way? (Content rules say not to invent new systems, so it stays the same for now.)
