# The Crucible Skater

**Status: proposal** (November 3, 2026). Practice-only encounter with placeholder visuals; not in the run. Sources read: `docs/design/content-rules.md` (this file follows it; conflicts below), Chzy18's Aurelion v4, Crucivar v4 and Sylvara v3 code, the Pendulum Sentinel / Dial Warden / Chimebound Oracle practice encounters, and the earlier Crucible Skater draft (kept in git history; this design replaces it).

Code: `unity/Assets/LastBell/Run/Encounters/CrucibleSkater/`. Launch: `Play Crucible Skater Practice.cmd` (`LastBell.exe -lastbellPractice skater`), or Boss Rush → Brass Foundry → Minibosses → Crucible Skater. Test: `LastBell.exe -crucibleSkaterTest -lastbellOut <dir>`.

## Theme

**The Crucible Skater, pour-runner of the Brass Foundry.** A small, quick brass figure (about 1.7 m, shorter than a hero) on two long skate blades, with a little crucible of glowing brass balanced on its head and an ivory mask. The foundry floor is its rink. Everything it does is a figure it skates, and the blades leave hot brass behind them that cools and fades. It doesn't overpower the party: it out-skates them. The party decides **where its route goes** (who it skates through, where the trails end up), **whether it gets to finish a run** (someone steps in front of it) and **who it chases next** (each player takes a turn as its target).

Look: weathered brass body, ivory mask, cobalt cavity in the blade housings, a resonant-gold molten core in the crucible. Coral only on its warnings. Model (9 Nov art pass): a scripted Blender automaton figure skater with 20 clips (`Characters/CrucibleSkater`), and a ladle prop (`Characters/CrucibleLadle`); report in `build-evidence/miniboss-art-20261109/crucible-skater/report.md`. FX and audio are still open.

## Rules this fight follows

- Every warning is a real hit shape in the new raid style (`HazardSystem.RaidStyle`, Brass Foundry motif). Hot trails use the shared molten slag colour, which is never used for a strike.
- Each hazard hits a player at most once. No iframes, no teleporting players, no knockback (stun 0.1 s only).
- Every mechanic is escapable walking at 4.2 m/s and at 6 m/s after a 0.3 s reaction (the autotest's pure checks prove each one).
- Melee and ranged are both threatened by all four mechanics (table below).
- Pools and trails always expire. No enrage.
- Ivory numerals over heads show order; they never hurt.

## The four mechanics

Numbers are Medium, out of 100 player HP. Easy and Hard scale damage by ×0.6 / ×1.3 (the run's rule); Easy also stretches every warning by ×1.25.

### 1. Figure Carve — the party draws its route (bait)

- **Cast:** ivory numerals **I, II, III…** appear over the players, farthest from the skater first. A lane (2.4 m wide, to the floor's edge) is drawn from the skater to I, from I to II, and so on. Each lane follows its numbered player for **1.5 s**, then locks.
- **The skate:** it crouches **0.9 s**, then skates the legs back to back at 16 m/s. Its body hits anyone in the leg it's on: **40, Strike**. The blow carries to the edge as it arrives.
- **Trail:** every leg it skated leaves a hot trail (1.6 m wide) that burns **20/s** for **7 s**, then cools and fades.
- **What you do:** if you're numbered, stand where you want a trail to end up (the rim, all on one side), then step sideways out of the lane once it locks. Everyone else: stay out of the lanes, then out of the trails.
- **Party sizes:**
  - **Solo:** three legs, all through you. Each leg follows you until the previous leg begins, so you keep sidestepping.
  - **2 players:** I → II → I (three legs).
  - **3 players:** I → II → III.
  - **4 players:** I → II → III → IV.
- **Threat:** melee stands where leg I starts; ranged players are the waypoints.
- **Hard, Retrace:** after the last leg it skates the whole route back again (reverse order, each segment re-telegraphed 1.0 s before it passes), so the fresh lanes run right beside the hot trails.

### 2. Body Check — someone has to stop it (intercept)

- **Cast:** the skater picks its quarry, the **farthest player**, who gets the carried-mark marker. A **block ring** (2 m) appears right in front of it with a numeral (how many blockers it wants), and a charge lane (2.6 m) runs from beyond the ring through the quarry to the edge. Both follow the quarry for 1.7 s, lock, and it charges at **2.5 s**.
- **Blocked** (anyone but the quarry is in the ring when it charges): the blockers split **60** between them, and the skater trips. It's **stuck for 3.5 s**, the fight's bonus punish window. No trail.
- **Not blocked:** it charges down the lane: **55, Strike**, to anyone in it, quarry included if they didn't sidestep. It leaves a hot trail along its path (20/s, 7 s), which can cut the rink in half.
- **Party sizes:**
  - **Solo:** you are the quarry and may block yourself. Either stand in the ring and take 60 to earn the stagger, or sidestep the lane and take nothing.
  - **2 players:** the other player blocks (1 needed).
  - **3 players:** 1 needed. The third keeps clear, or joins to halve the hit.
  - **4 players:** 2 needed (30 each). The quarry and the fourth stay out of the lane.
- **Below 50%: Turns.** Two checks back to back. The second takes the farthest player **who wasn't the first quarry**, so the job moves on.
- **Threat:** the ring is point-blank, so melee must commit or get out; the lane runs to the far player and on to the wall.

### 3. Crucible Toss — catch the pour (shared soak)

- **Cast:** the skater flicks its crucible and two ladles of molten brass fly to spots **5.4 m from the centre**, on opposite sides (one of four layouts). Each landing ring (2.4 m) shows a big ivory numeral: how many players it needs.
- **Landing (5.0 s):** the players inside split **70**. Over-soaking lowers each share. **An empty pour floods the rink: party wipe**, with a loud numeral telegraph (the settled empty-soak rule). One player alone takes 70 and survives from full.
- **Party sizes:**
  - **Solo:** two pours of one, landing 2.5 s apart (5.0 s, then 7.5 s). Catch one, walk to the other.
  - **2 players:** 1 + 1, landing together.
  - **3 players:** 2 + 1.
  - **4 players:** 2 + 2.
- **Threat:** the soak spots pull melee off the boss and pull ranged in from the rim.

### 4. Rink Lap → Pirouette — in, then out

- **Rink Lap:** it skates to the rim and the **outer band (8.5 m to the wall)** lights up. After **2.0 s** it laps the whole rink in 3.0 s. Anyone in the band within 20° of it as it passes takes **45, Strike**. Get inside 8.5 m.
- **Pirouette:** as the lap ends it skates to the centre and spins. A **6 m circle** lights up and lands **2.0 s** after the lap: **50, Strike**. Now get out.
- **Dizzy:** then it wobbles at the centre for **3.5 s** with nothing live. This is the **major punish window**, every cast.
- **Party sizes:** the same for everyone.
- **Threat:** the band covers ranged and the edge; the circle covers melee.

### Threat check (content rules §5)

| Mechanic | Next to the boss | At 8 m or more |
|---|---|---|
| Figure Carve | leg I starts on it | the numbered players are the waypoints |
| Body Check | the block ring is point-blank | the lane runs to the farthest player |
| Crucible Toss | the pours pull you off it | the pours pull you in from the rim |
| Rink Lap → Pirouette | the Pirouette | the band |

Range is punished by the band, Body Check (it aims at the farthest player) and Figure Carve (waypoints are farthest first). No spot is safe for a whole phase.

## Phases and rotation

- **Phase 1 (100–50%):** Figure Carve → Body Check → Crucible Toss → Rink Lap/Pirouette, with 1 s between attacks, then repeat. About 25 s per loop.
- **At 50%:** it finishes the current attack, then a harmless 2 s **reheat**: the crucible flares and it spins in place. It stays damageable; no heal, same bar.
- **Phase 2 (below 50%):**
  - Figure Carve gets **one more leg** (solo 4; 2p I-II-I-II; 3p/4p return to I at the end), and trails last **9 s**.
  - Body Check becomes **Turns** (two quarries in a row).
  - Crucible Toss and Rink Lap come **together**. The pours land as the lap ends, inside the band's safe zone. That's the at-most-two overlap; the Pirouette comes after the pours land.
  - Rotation: Figure Carve → Turns → Toss + Lap/Pirouette.
- **Max HP:** 8400 solo base (doubled from 4200 in review, November 3), **party-scaled** with the shared formula: ×(1 + 0.7 per extra fighter), so 2 players 14 280, 3 players 20 160, 4 players 26 040. Bots that don't attack (the default) don't count. Still to be checked against measured DPS.

## Difficulty

| | Easy | Medium | Hard |
|---|---|---|---|
| All warnings | ×1.25 | as above | as above |
| Damage taken | ×0.6 | ×1 | ×1.3 |
| Named extra | — | — | **Retrace** (Figure Carve skated back again) |

## Placeholders and open work

- Body: the rigged CrucibleSkater model (20 clips); the code-built "STAND-IN" primitives remain as the fallback when the model is missing. No final FX or audio (the shared `BossFx` resolve flashes only).
- Thrown ladles use the CrucibleLadle model (sphere fallback), on the host and LAN clients; trails are plain slag telegraphs.

## Conflicts with the content rules

- The earlier draft had *Overflow* (whole-floor hit, safe only on cooled slabs) and a beat clock. Dropped: safe-on-slag clashes with the shared slag colour meaning "burns you", and a beat clock wasn't needed.
- The earlier draft said "no dash required, survivable at 80% speed". This design is held to the standard rule instead (4.2 and 6 m/s after a 0.3 s reaction).
- The empty-pour wipe follows the settled empty-soak rule. Its telegraph is the big numeral ring, and the fight introduces it in Phase 1 before Phase 2 layers it.

## Review answers (November 3, 2026)

1. **HP:** doubled to 8400 (solo base).
2. **Body Check solo** ("block yourself for the stagger"): kept as designed; the team will playtest it. **To test:** does solo blocking feel worth the 60 damage, or should solo get the dodge-only version?
3. **Party-scaled HP:** on (`PartyScaled`, the shared 0.7 per extra fighter).
4. **Run slot:** open. The gameplay loop isn't decided yet; several bosses will likely share each arena, so the skater stays practice-only on Brass Foundry for now.
5. **Model:** remodel for this smaller skater (the old 2.4 m bowl-on-three-legs rig doesn't fit). Queued for the next usage reset; card on the lane board.

## Still open

- Bots in a party of 2 never block Body Check: the generic soak bot logic ignores that the ring excludes the quarry, so the lone ally never takes it. Blocking itself works (the test proves it with a placed player); fixing the bot is a shared `AllyBot` change.
