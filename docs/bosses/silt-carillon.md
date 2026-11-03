# The Silt Carillon

**Proposal (November 3, 2026), not yet approved by Chzy18.** It replaces the earlier Silt Carillon design (Rising Silt rings, Triple Toll, Sediment, Aqueduct Fall), which stays in git history. The rigged Meshy model from that design (`prototype/arena-3d/models/rigged/minibosses/SiltCarillon.glb`) is not used yet: the practice build uses a primitive stand-in.

**Warden of the Drowned Steps.** A Verdant Chimes miniboss. Long ago it was the bell-tower that tolled the river's flood stages. The river silted it up, and now it rings the tide in itself. It is a squat dome of mossy ivory stone on a mud-caked base, with three sunken bells hanging under its arches. It barely moves. It controls the floor instead: mud rolls in and out across the arena, bells surface in the silt and have to be rung, and silt pours on anyone who stands still.

- **Area:** The Verdant Chimes (shares Sylvara's arena, `verdant-chimes`, player bound 13.5 m).
- **Related to Sylvara, but different:** Sylvara is a bullet-hell with no punish windows and no rest. The Carillon is slow and heavy. There are no bullets: the danger is *ground* that changes under you, and the fight has a guaranteed rest (Drained) whenever the party rings its bells. The same garden, gone under water.
- **Status:** practice only (`inRun: false`), placeholder body, raid warning look (Telegraph-Style v2, `HazardSystem.RaidStyle`) on every warning. No final model, FX or audio.
- **Code:** `unity/Assets/LastBell/Run/Encounters/SiltCarillon/`. Launch: `Play Silt Carillon Practice.cmd` (`-lastbellPractice silt`). Test: `LastBell.exe -siltCarillonTest -lastbellOut <folder>`.

## Rules this fight follows (content rules)

- No ability text in the game: names appear only in the F7 diagnostics overlay (hazards carry no mechanic tag, so the boss panel shows none).
- Everything lands on the beat: **96 BPM above 50%, 116 BPM below**.
- No dodge immunity, no jump or dash required: every warning passes the escape check walking at 4.2 m/s after a 0.3 s reaction.
- No knockback on any of its hits (they would push players into the mud).
- Danger is coral/amber; the bells you must stand in are ivory/teal friendly rims with a large numeral.
- Shared soaks follow §5 exactly: shares split between the players inside, missing shares fall on those inside, one player alone survives from full HP, an **empty bell wipes the party**. The first bell set of the fight is introduced with a longer warning.
- Two HP phases (50%), a harmless 2 s transformation that waits for the current attack, no heal, no enrage.
- At most two damaging patterns at once (the tide plus one other).
- Major punish window: **Drained**, 3.5 s with nothing live, after every rung bell set.

## The four abilities

### 1. Silt Tide (the safe ground shifts)

Mud floods part of the floor. Once the warning fills, the mud stays as a standing pool that burns anyone in it.

- **High Tide (above 50%):** the ring from **7 m** to the edge floods. Warning **5 beats** (3.1 s), then mud for up to **16 beats** (10 s), **25 HP/s**. It ebbs at the end of its attack, or at once when the bells are rung.
- **Crosscurrent (below 50%):** the floor is cut into six 60° sectors from the Carillon. Three alternate sectors flood (warning **4 beats**), stay mud for **6 beats**, then drain while the other three get their 4-beat warning, and so on. The safe ground rotates round the tower. Hug a sector line and you only take a step each swap; stand at the edge and you walk up to 7 m.
- **Melee:** Crosscurrent sectors run right up to its body. High Tide leaves the centre dry, but always comes with the bells or the Peal, which hit the centre.
- **Ranged:** High Tide takes the whole outer ring away (the fight's "staying at range is punished" attack).
- **Hard, Backwash (the named extra mechanic):** when mud drains, the drained ground strikes once **1.5 beats** later (**30**). Wait a beat and a half before you step into freshly drained ground.

### 2. Sunken Bells (shared soaks that push the tide back)

As a tide rises, bells surface in the dry ground. Each is a shared soak with a large numeral: the number of players it wants.

- **Where:** High Tide: two bells on opposite sides of the tower, 4.5 m out (the dry disc). Crosscurrent: in two of the three dry sectors, 6 m out. Radius 1.6 m (room for two).
- **When:** they surface with the tide's warning and toll **8 beats** later above 50% (3 beats after the mud locks) and **10 beats** later below 50% (the farther bell of a pair is reachable from anywhere in that time). The first set of the fight gets 3 more beats, Easy 2 more.
- **Damage:** **60** per bell, split between everyone inside. Fewer than the numeral: the missing shares fall on those inside (one player alone takes 60 and lives). **An empty bell wipes the party** (§5).
- **Reward:** when every bell of the set has been rung, the tide drains at once and the Carillon is **Drained**: it slumps for **3.5 s** with nothing live. This is the major punish window.
- **Melee and ranged:** a bell is somewhere specific, so nobody can just stay where they are.

| Party | Bells | What players do |
|---|---|---|
| 1 (solo) | one bell **I** | ring it yourself |
| 2 | **I** and **I**, opposite sides | the pair splits round the tower |
| 3 | **II** and **I** | a pair and a single |
| 4 | **II** and **II**, opposite sides | two pairs |

Bot allies (0–3) take bells like players do (the generic soak job: each bell takes its count, nearest first).

### 3. Silt Fall (sediment weighs down whoever stands still)

Silt pours from its arches onto every player, once per beat.

- **The drops:** each standing player gets **4 drops** (**5** below 50%), one per beat. A drop is a 1.8 m circle that follows its player, locks **1.5 beats** before it lands (**1.75** at 116 BPM, so it is never under 0.9 s), and hits for **30**.
- **Mounds:** each landed drop leaves a silt mound (same circle) that burns **20 HP/s** for **6 beats**, then sets and disappears.
- **What you do:** keep walking. A moving player is never hit; a player who stands still is. The mounds you leave behind are your trail, so walk where your trail won't block the others.
- **Melee and ranged:** every player is targeted wherever they stand.
- **Co-op:** drops don't care whose they are: one landing on two players hits both. **2 players:** take opposite sides of the tower. **3:** thirds. **4:** quadrants, each walking their own arc. **Solo:** one trail. In 4-player co-op the floor fills with up to 16 mounds, so the party needs a plan, but the mounds always expire.

### 4. Drowned Peal (in, out, in)

The great bell under the dome tolls and the sound rolls across the floor in three bands:

- **A:** circle 0–4.5 m (round the tower), **B:** ring 4.5–9 m, **C:** ring 9 m–edge. **40** each.
- **Outgoing peal:** A, then B, then C. **Ebb peal (below 50%):** C, then B, then A.
- **Timing:** the first band lands after **4 beats**, then one band every **1.5 beats**. All three are shown from the start; the next to land is the one on top, filling fastest.
- **What you do:** stand next to the band that lands first, step into it once it has landed, and stay there. Hug the tower in an outgoing peal and you must step out to 4.8 m and back in. At range you get close to the 9 m line before the first band lands.
- **Melee and ranged:** band A hits the hugger, band C hits the long-range player.
- **Co-op:** individual. With High Tide or Crosscurrent at the same time, the safe ground is smaller and has to be shared.

## Phases and rotation

Each attack ends only when its last hazard has gone (mounds included), then 1 beat of rest.

- **Phase 1 (100–50%, 96 BPM):** High Tide + Sunken Bells (Drained) → Silt Fall → Outgoing Peal → High Tide + Outgoing Peal → Silt Fall → repeat.
- **50%:** it finishes the current attack, then 2 s of harmless transformation (it stays damageable, nothing spawns), and the tempo rises to 116 BPM.
- **Phase 2 (50–0%, 116 BPM):** Crosscurrent + Sunken Bells (Drained) → Silt Fall (5 drops) → Ebb Peal → Crosscurrent + Outgoing Peal → Silt Fall → repeat.
- **Max HP:** 3200 (not party-scaled; bots don't attack by default).
- **No enrage** (§5).

## Numbers by difficulty

Medium is the baseline. Every level also scales boss damage (Easy ×0.6, Hard ×1.3).

| | Easy | Medium | Hard |
|---|---|---|---|
| High Tide warning | 6 beats | 5 beats | 5 beats |
| Crosscurrent warning | 5 beats | 4 beats | 4 beats |
| Bell toll after they surface | 10 / 12 beats | 8 / 10 beats | 8 / 10 beats |
| Silt Fall drop lock-to-land | +0.5 beat | 1.5 (1.75 at 116) | 1.5 (1.75 at 116) |
| Drowned Peal first band / gap | 5 / 2 beats | 4 / 1.5 beats | 4 / 1.5 beats |
| Extra mechanic | – | – | **Backwash** |

## Escape checks (4.2 m/s, 0.3 s reaction)

The acceptance test checks these, plus a walker for each one:

- High Tide: edge to inside 7 m (6.8 m to walk) in 5 beats at 96 BPM: 11.9 m of walking time.
- Crosscurrent: the middle of a sector at the edge to the nearest dry sector (7.05 m) in 4 beats at 116 BPM: 7.4 m.
- Silt Fall: from the centre of a locked drop out of it (2.1 m) in 1.5 beats at 96 / 1.75 at 116: 2.7 / 2.5 m.
- Drowned Peal: across a band boundary into the band that just landed (0.6 m) in 1.5 beats at 116: 2.0 m.
- Bells: from anywhere on the floor to the farther bell of a pair (18 m above 50%, 19.5 m below) in 8 beats at 96 BPM / 10 beats at 116 BPM: 19.7 / 20.5 m.

## Open questions for review

1. **Empty bell = wipe.** That's what §5 requires of a shared soak, but it is harsh for a miniboss. Is a big party-wide hit acceptable instead?
2. **Area motif:** the shader has no Verdant Chimes motif yet, so every warning uses the default raid motif (Skyward light rays). Should we add one?
3. **Rest between attacks:** Drained is the only 3.5 s window. Should the Peal have a short recovery too?
4. **Model:** use the old rigged Meshy model once its clips are remapped, or make a new one?
5. **Run slot:** practice only for now. Which miniboss slot should it take in the Verdant Chimes run?
