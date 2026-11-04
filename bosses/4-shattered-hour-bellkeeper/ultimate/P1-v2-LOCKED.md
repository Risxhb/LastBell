# Bellkeeper Ultimate v2 — P1 "Last Guardian" (locked)

**Status:** designed, built, bot-tested, and **approved for now by Chzy18 on 2026-10-24** after the readability-pass clip ("That's much better. Let's lock that in for now for p1"). Later phases are still v1 and not approved. Git tag: `bellkeeper-ult-v2-p1-locked`.

Code: `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Ultimate/EscapementRelay.cs`, `ChimeDebts.cs`, the P1 plan in `BellkeeperUltimate.cs` (`P1V2Loop`). Tests: `Tests/RunAutoTest.Bellkeeper.Ult.P1V2.cs` (`bots_p1v2`, `bots_p1v2_video`, `bots_p1v2_watch`).

## The phase
- No boss attacks. The whole phase is the Escapement Relay with Chime Debts on top, and you attack during it.
- First beat 6 s after the pull, then one beat every **2.5 s**.
- P1 ends at 85 % boss HP, but not before 72 s (two debt rounds). The push timer is 150 s.
- In the v2 slice, clearing P1 ends the attempt as a win. The later phases are not rebuilt yet.

## Escapement Relay
- Four brass plates at N/E/S/W, 7 m from the boss. The hour hand points at the lit plate and ticks 90° clockwise each beat.
- Each player has an hour numeral (I–IV, party order). Every beat each player has one job:
  - **SOAK:** stand on the lit PLATE (alone).
  - **WEIGHT R / WEIGHT L:** stand on the ✕ 3.2 m out, 90° right or left of the hand (in melee reach).
  - **CHAIN:** stand in the blue band behind the boss: at least 9 m out, on the half away from the hand.
- Your job steps along **SOAK → WEIGHT R → WEIGHT L → CHAIN → SOAK** each beat. This is card 01's order, so you travel round the dial. The two long hops (about 11–12 m) need a sprint.
- **REWIND:** after runs of 7, 4, 6, 3, 8 and 5 beats, the hand reverses. Everyone retraces their route, so the job order runs backwards. "REWIND NEXT BEAT" flashes one beat before.
- Fails, checked on the beat:

| Fail | Result |
|---|---|
| Plate empty | WIPE |
| Extra player on the plate | 60 to each player on it |
| Chain short, or on the near half | 999 lane through the plate |
| A ✕ empty | 999 cone (120°) on that side |

- Party sizes:
  - **Trio:** SOAK → WEIGHT L → CHAIN.
  - **Duo:** SOAK → CHAIN.
  - **Solo:** SOAK every beat.

## Chime Debts
- One round per 36 s loop. Each player pays on their next SOAK beat after the round opens. The debt timer shows in their label.
- The debt is a ring, 3.6 m in radius and centred on the plate, plus a 2 m wide chime line toward the boss that stops 4 m short of him.
- It flashes bright for **1.5 s**, then is a memory: there's no telegraph on the floor.
- **One beat later** it strikes for 999. On that beat it covers the ✕ on its side, except the ✕'s inner edge (2.0–3.1 m from the boss). That weight holder must remember the debt and stand boss-side, so **a forgotten debt kills**. Their next job then goes empty, which wipes the party.
- If a rewind brings the dial back onto the beat the debt was paid on, the debt is erased. This can't happen with a one-beat delay, but the rule is kept for the code.

## Readability (part of what was locked)
- The hand is drawn twice: a glowing floor line to the lit plate, and a bar above the boss's head.
- The ✕ marks are 2.6 m, marked L and R. The chain zone is a blue band with an ivory line at 9 m.
- The words PLATE, CHAIN, L and R are shown on the spots.
- Each player's label shows their hour and job, plus the debt timer, in the job's colour: gold SOAK, white WEIGHT, blue CHAIN. Your own label says YOU.
- The text under the boss bar repeats your job and your next one.
- **Practice aid:** a teal beam on your own spot (`EscapementRelay.ShowMySpot`, on by default).

## Bot results at lock (ult-v2-p1-15, live watch ult-v2-p1-watch)

| Party | Result |
|---|---|
| Perfect 4, 8 and 3 | Clear, 28/28 beats, 5 rewinds, 0 damage, 0 potions |
| Miss 4 (forgets the first debt round) | Wipes |
| Late 4 (+0.25 s) | Clears: not punished yet |
| Perfect duo and solo | Wipe at the 150 s push, because their damage is too low. Needs its own tuning. |

## Open
- Late bots still clear.
- Duo and solo HP/push tuning.
- Should the practice aid be off for real attempts?
- Card 01 and card 02 still show the older versions and need updating to match this spec.
