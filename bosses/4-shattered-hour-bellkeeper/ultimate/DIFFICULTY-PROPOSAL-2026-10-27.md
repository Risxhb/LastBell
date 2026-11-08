# Ultimate difficulty: Hard is the baseline (proposal)

Status: **proposal**, 27 Oct 2026. No code is changed until Chzy18 accepts it through the review inbox.
Rule: `gpt/08-design-rules.md` §5 (Chzy18, 27 Oct). The Ultimate's numbers as designed are **Hard**, and Easy and Medium are scaled down from them.

## First, a measurement problem
The 27 Oct playtest (`playtest-reports/bellkeeper-ultimate-2026-10-27.md`) measured the **retired v1 fight**, not v2.
- Its sweep key `pt_sweep` isn't one of the v2 keys, so `RunAutoTest.Bellkeeper.Ultimate.cs:28` sets `V2Override = false`.
- The logs prove it: segments T1@62 and T2@219 (v2 skips both), and deaths to Root Hour, Reflection, Tolling Hour and Cracked Hour (v1 mechanics).
- So the 5:45 / 6:00 party clears and the solo table don't describe the fight we ship. The v2 data we have is from the lane's own bots at ×1.0: a perfect party of 4 kills in about 6:01 with 0 deaths, and the trio clears P5. v2 solo has never been swept.

The numbers below are starting points. Step 1 measures v2 before anything is locked.

## What changes
| | Hard (baseline) | Medium | Easy |
|---|---|---|---|
| Damage taken | ×1.0 (today ×1.3) | ×0.75 | ×0.55 |
| Boss HP (after the solo/party fit) | ×1.0 | ×0.9 | ×0.8 |
| Push timers and the P5 Last Stand | as designed (Last Stand 10 s) | +10 % (Last Stand 11 s) | +20 % (Last Stand 12 s) |
| Lethal warnings (teeth, clapper, shards, debts, slam) | as designed | as designed | +0.3 s lead |
| Mechanics removed | none | none | none (open question 2) |

- **Only the Ultimate changes.** It overrides `DamageTakenScale` with its own table, so the other bosses keep Easy ×0.6 / Hard ×1.3. The menu note for Hard on the Ultimate reads "the fight as designed".
- **Solo keeps the same phases** (25 Oct rule). Solo still gets only the HP fit (×0.65) and the push timers, now per level.
- **Why HP and timers, not just damage.** In the playtest, the solo losses that weren't deaths were DPS losses at the P5 enrage (9.5 %, 6.8 %). A damage cut alone doesn't fix that. HP ×0.8 plus a 20 % longer timer covers those gaps with room to spare.
- **Escape check:** longer warnings on Easy only add time, so every Hard window that passes still passes. Each change re-runs the existing escape and perfect-bot checks.

## Targets (bot sweep, all from P1, v2)
| Level | Perfect party of 4 | Perfect solo, each class | Late party (+0.25 s) |
|---|---|---|---|
| Hard | kills at about 6:00 | not required for bots (open question 1) | wipes |
| Medium | kills | kills for all three classes | may wipe or kill |
| Easy | kills | kills for all three classes | kills |

## Steps once accepted
1. **v2 measurement sweep:** Perfect and Late, solo for each class and a party of 4, at today's ×1.0. This is test code only, run under the Unity lock.
2. Add the Ultimate difficulty table: damage, HP, timers, Last Stand length, Easy warning lead.
3. Re-sweep all three levels and tune the multipliers to hit the targets. Report the results to the inbox.
4. Merge `feature/confrontation-menu` first (controls and the new Arcanist kit), so the solo Arcanist is measured on the real kit.

## Open questions for Chzy18
1. **Hard solo:** must a perfect solo bot clear Hard, or is Hard solo "possible with good human play" (the §5 standard), proved by Easy/Medium bot clears plus a human attempt?
2. **Easy mechanic removal:** keep every mechanic on Easy (this proposal), or drop one, such as the second sound ring in P5 or the double hammer in P4?
3. **Late bots on Hard:** the playtest's Late-party clears were on v1. On v2 the lane's slow-reacting bots wiped in P5 with the boss at 0.8 %. Should "Late wipes on Hard" be a required check?
