# Sylvara v3: HP and party scaling (accepted 1 Nov)

Status: **accepted** (review inbox, 1 Nov) and built in Unity: `SylvaraEncounter.PartyHp`, checked by the autotest. The game is 1–4 players (Chzy18, 1 Nov).

## HP (the same on Easy, Medium and Hard)
**Target:** a clear in about 3:20 (200 s). That's inside the "area boss about 3–4 min" rule (`gpt/08`). Phase 4 takes 40–55 % of the clear time in the measured runs, so at 200 s Phase 4 lasts about 80–110 s, close to the Phase 4 doc's 75–90 s goal. A 4:00 target would push most parties into Last Storm (2:00 of Phase 4).

| Fighters | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| HP | 4,600 | 9,200 | 13,800 | 18,400 |
| How it was set | measured | between 1 and 4 | between 1 and 4 | measured |

- It's a per-party-size table in `SylvaraEncounter`, so each size can be tuned on its own. Fighters are counted the same way as for Aurelion: players, plus bots only when bots attack.
- Bots in perfect mode probably deal more damage than people do, so real clears will likely run longer than 200 s. The table can be scaled down after playtests.

## Measured
**1 Nov (Medium, at 9,000 HP for every size):** perfect-mode bots with `AllyBot.Attacks` on, real downs and a 480 s cap. Party damage per second was about 23 solo and 92 at 4 players, which set the table above. Those runs used the classes of the time; Vanguard has since been retired.

**3 Nov (Hard, Tollbreaker, the HP above):** solo wiped in Phase 2 with 51 % left; 4 players wiped in Phase 4 with 19 % left. Evidence: `sylvara-v3-logs/33/clear/autotest-report.txt` (local, `-lastbellSylvaraClear`).

## Open questions
1. **Solo survival:** the perfect solo bot wipes before the end, which is bot dodging rather than HP. The solo number waits for a human solo playtest.
2. **Bud damage counts against Sylvara 1:1:** cutting buds is never lost damage. Keep it, or count bud damage at a lower rate?
3. **2 and 3 players** are filled in between the measured counts, not measured. Run them if a playtest feels off.
