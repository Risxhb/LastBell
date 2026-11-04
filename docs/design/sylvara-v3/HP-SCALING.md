# Sylvara v3: HP and party scaling (accepted 1 Nov)

Status: **accepted** (review inbox, 1 Nov) and built in Unity: `SylvaraEncounter.PartyHp`, checked by the autotest. Before this the build had 9,000 HP for every party size.

## Measured (Unity autotest, Medium, 1 Nov)
These are perfect-mode bots with `AllyBot.Attacks` on, real downs and a 480 s cap. Buds take real class damage, and that damage also counts against Sylvara. Evidence: `sylvara-v3-logs/15/clear/autotest-report.txt` (local, run with `-lastbellSylvaraClear`).

| Players | Run | Result at 9,000 HP | Party DPS |
|---|---|---|---|
| 1 | Vanguard | wiped at 15 s (89 % left) | 65 |
| 1 | Chimesinger | wiped at 227 s (41 % left) | 23 |
| 1 | Arcanist | wiped at 292 s (27 % left) | 22 |
| 4 | Vanguard + 3 | cleared in 107 s (P4 60 s), 2 downs | 84 |
| 4 | Chimesinger + 3 | cleared in 95 s (P4 39 s), 1 down | 95 |
| 4 | Arcanist + 3 | cleared in 93 s (P4 36 s), 1 down | 96 |
| 8 | Vanguard + 7 | cleared in 30 s, 2 downs | 301 |
| 8 | Arcanist + 7 | cleared in 27 s, 0 downs | 330 |

Average party DPS is about 23 solo, 92 at 4 players and 315 at 8. Damage per player rises with party size (23 → 23 → 39), because pods and buds are spread over more people, so each player spends less time dodging. A straight line (one step per extra player) can't fit both 4 and 8.

## Proposed HP (Medium, the same on Easy and Hard)
**Target:** a clear in about 3:20 (200 s). That's inside the "area boss about 3–4 min" rule (`gpt/08`). Phase 4 takes 40–55 % of the clear time in the runs above, so at 200 s Phase 4 lasts about 80–110 s, close to the Phase 4 doc's 75–90 s goal. Most kills land in loops 4–5, and a short-on-damage party reaches Last Storm (2:00). A 4:00 target would push most parties into Last Storm.

| Players | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| HP | 4,600 | 9,200 | 13,800 | 18,400 | 29,600 | 40,700 | 51,900 | 63,000 |
| How it was set | measured | between 1 and 4 | between 1 and 4 | measured | between 4 and 8 | between 4 and 8 | between 4 and 8 | measured |

- It's built as a per-party-size table in `SylvaraEncounter`, replacing the shared `1 + PartyStep × (n − 1)` formula. Fighters are counted the same way as for Aurelion: players, plus bots only when bots attack.
- A table was needed because one step couldn't fit both the 4- and the 8-player measurements. With 1–4 players only, the table equals a step of 1.0, and it stays a table so it can be tuned per size.
- Bots in perfect mode probably deal more damage than people do, so real clears will likely run longer than 200 s. The table can be scaled down after playtests.

## Open questions
1. **Solo survival:** the perfect solo bot wipes at every HP. Vanguard (melee) dies at 15 s, and the ranged classes die after about 4 minutes. That's bot dodging, not HP, but it means the solo number is untested past Phase 3. Should solo get its own easing, or should it wait for a human solo playtest?
2. **Bud damage counts against Sylvara 1:1:** cutting buds is never lost damage. Keep it, or count bud damage at a lower rate?
3. **2 and 3 players** are filled in between the measured counts, not measured. Run them later if a playtest feels off.
