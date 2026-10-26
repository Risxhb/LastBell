# Aurelion v3 (Milestone A): playtest

*Proposal build, local branch `feature/aurelion-v3`. Design: `ABILITIES-v3.md`.*

## How to launch

The build starts with the harder set already on:

```
LastBell.exe -lastbellAbilitySet v3 -lastbellBossHp 0.2
```

1. In the run menu, choose **Hard**.
2. Pick your class.
3. Choose practice slot 4, **Aurelion**.
4. Press **A** to cycle allies (0–7). Allies are passive bots: they do the mechanics but never attack.

`-lastbellAbilitySet v2` brings back yesterday's v2.1 for comparison.

## What changed

Two memory mechanics replace the two pure "see it, step out" moves.

### Ecliptic Order (replaces Noon Shadow, every phase)

- **What happens:**
  1. He walks to the centre.
  2. 3 brass rings appear on the floor, numbered **I, II, III** in ivory (4 rings from Misalignment on).
  3. After **2.5 s** the rings and numbers go dark.
  4. A strike then hits **the whole floor except ring I**, then except ring II, and so on. Each strike is 2.2 s after the one before.
- **What you can see:** each strike shows coral only **0.35 s** before it lands. You can't find the ring by reflex; you have to remember the order.
- **Damage:** 50 per strike, 65 on Hard.
- **The trick:** read the numbers, then plan the walk I → II → III. Consecutive rings are always at least 120° apart round the centre.

### Zenith Marks (replaces the plain Orrery Closing, Phases 1–2)

- **What happens:**
  1. He walks to the centre.
  2. A word appears over your head: **SHADE** (cobalt) or **SUN** (ivory).
  3. **3.0 s later** it resolves on the same tick as his Meridian Lances.
- **What to do:**
  - **SHADE:** be within 5.5 m of him.
  - **SUN:** be more than 8.5 m away.
- **What you can see:** the coral verdict shows only 0.35 s before it lands.
- **Damage:** 50, 65 on Hard.
- **Solo:** the marks alternate, SUN first (Zenith Collapse has just pulled you in).
- **The trick:** the Lances' beams are aimed at you while you run in or out.
- **Misalignment:** a two-step read. "SHADE > SUN" means be in his shade for the first Lances volley, then out in the sun for the second.

### Damage tiers (v3 only)

Every hit is now either **punish** (30 or less) or **kill** (50 or more):

| Ability | Before | Now |
|---|---|---|
| Meridian Lances | 35 | 30 |
| Orrery Closing | 40 | 50 |
| Zenith Collapse | 45 | 50 |

### Unchanged

- Lances + Starfall
- Zenith Collapse + Lances
- Phase 3's Grand Orrery with Hour Hands on Hard
- the 3.5 s punish window

## Keys

| Key | What it does |
|---|---|
| **F6** | Practice one ability on loop. Cycles through every ability; Ecliptic Order and Zenith Marks are the last two. |
| **F8** | Jump to the next phase. |
| **F9** | Defeat the boss. |
| **A** (run menu) | Allies 0–7. |
| **[ / ]** (run menu) | Change map. |

## What to tell us after 3 pulls

1. Did you go down at least once in your first 3 pulls?
   - The goal is yes.
2. Did you ever die without knowing why?
   - The goal is no. If yes, which ability, and what you saw.
3. Ecliptic Order:
   - Could you read the numerals in time?
   - Was the 2.5 s too short or too long?
4. Zenith Marks:
   - Did you notice the word over your head?
   - Was SHADE vs SUN clear without colour?
5. Is Hard now hard? Is Medium too hard?
   - Medium also gets the new mechanics: open question 1.
