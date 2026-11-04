# Bellkeeper Ultimate v2 — P2 "Broken Hour" (locked)

**Status:** designed (browser sim), built, bot-tested, and **approved for now by Chzy18 on 2026-10-24** ("Lets call it for p2 for now and move to p3"). Git tag: `bellkeeper-ult-v2-p2-locked`. P1 is in `P1-v2-LOCKED.md`.

Code: `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Ultimate/BrokenHour.cs` and the P2 plan in `BellkeeperUltimate.cs`. Sim: `tools/bellkeeper-ultimate/sims/` (p2solve.js, p2-template.html). Tests: `bots_p2v2`, `bots_p2v2_8`, `bots_p2v2_video`, `bots_p2v2_watch`.

## The phase
- P1 runs straight into P2; v1's T1 is skipped.
- First beat 6 s in, then every **3 s**. No boss attacks.
- P2 ends at 70 % boss HP, after at least 60 s. The push timer is 150 s.
- In the v2 slice, clearing P2 ends the attempt as a win.

## Every beat (three rules on the tick)
- **Plate:** the hand ticks 90° to the next plate, with P1's rewinds (runs of 6, 4, 5 and 3 beats). The hours soak it in turn: I, II, III, IV.
  - Plate empty: **WIPE**.
  - Anyone else on the plate: 60.
- **Inverted Bell:** the bell slams the half of the arena away from the plate.
  - Everyone stands on the plate's half, except the **INVERTED** players, who stand inside the slam.
  - Wrong half: 999.
- **Gear Train:** I–III and II–IV are chained.
  - Each chain must be 6–10 m on the beat. Over 10 m it snaps: **WIPE**. Under 6 m it grinds: 60 to both.
  - Chains that cross: 60 to all four players.

## INVERTED
- INVERTED is always the soaker's partner (hour + 2), so it changes hands every beat.
- The holder passes it by touch (within 1.5 m) to a player who doesn't hold it. Then it can't move again for 1 s.
- Whoever receives it keeps it until the beat.
- The wrong holders on a beat: **WIPE**.
- On the first beat it is dealt directly, not passed.
- Party sizes:
  - **Below 4 players:** INVERTED is dealt each beat, and it vanishes on beats with no partner hour.

## Bot strat (the perfect bots)
- A party plan per beat, the sim's solver in C#.
- Spots sit 0.9 m inside their half. Chains sit 0.5 m inside 6–10 m and at least 1 m apart.
- The touch has a meeting point and time; remembering it is the memory job.
- The run's own player gets the same plan as a teal beam, plus an orange touch disc.

## Bot results at lock (ult-v2-p2-7)

| Party | Result |
|---|---|
| Perfect 4, from P2 | Clear, 19/19 beats, 18 passes, 0 damage |
| Perfect 4, P1 + P2 | Clear at 2:14, 0 damage, 0 potions |
| Perfect 8 | Clear, 26/26 beats, 50 passes, 0 damage |
| Trio | Clear, 0 damage |
| Miss 4 | Wipes on beat 2 (a forgotten pass) |
| Late 4 (+0.25 s) | Clears: not punished yet |

## Open
- The Late bots still clear.
- Card 03 (Gear Train) and card 04 (Inverted Bell) show their older versions.
- The bots' plan assumes the run's player follows the beam.
