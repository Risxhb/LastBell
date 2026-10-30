# Crucivar v4: fight sheet

**Status: accepted (Chzy18 via the review inbox, 29 Oct 2026): keep about 4 min solo, not longer like Aurelion.** The three phases are accepted for now. This sheet sets the whole fight: HP bands, boss HP, co-op scaling, casting HP, the enrages, and Easy/Hard. The HP is built and tested; the numbers are placeholders until people play it.

## The fight at a glance

| Phase | HP band | Name | Loop | Enrage |
|---|---|---|---|---|
| 1 | 100–65 % | Forge Rite | about 20 s + 3 s punish | soft: Ember Brand pools fill the floor for the rest of the phase |
| 2 | 65–30 % | Foundry Uprising | castings, then 3.5 s punish, then Casting Order and Anvil Quake | hard: **Pour Flood** wipes the party if any casting is still alive 30 s after it rises (solo 36 s, Easy 40 s) |
| 3 | 30–0 % | Molten Crown | 21.5 s cycle + 3.5 s punish | hard: **Furnace Eruption** wipes the party on the 4th crown, about 90 s into the phase (Easy: 5th crown, about 115 s) |

- **65 %:** the second form (insulation falls, glow deepens). Phase shifts are 1.5 s, harmless, no heal. Phase 1's pools clear.
- **30 %:** Open Pressure starts with the first Molten Crown cycle, and Phase 2's pools clear.
- **No overall fight timer.** Each phase has its own enrage instead, so you can't bank time in an easy phase.

## Measured damage (watch bots, 29 Oct)

The watch bots are all Vanguard on scripted input. They dodge and use their abilities, but they're weaker than people: they crowd the melee ring and miss uptime. Measure mode runs a fixed number of loops per phase and logs the damage the players dealt. Logs: `test-out/m2/measure-1.log`, `-2.log`, `-4.log`.

| Party | Phase 1 team DPS | Phase 2 team DPS (boss + castings) | Phase 3 team DPS |
|---|---|---|---|
| 1 | 31.0 | 38.3 | 39.2 (2 cycles, then the bot went down) |
| 2 | 39.3 | 79.5 | too short to tell |
| 4 | 62.8 | 117.2 | the bots wiped in Phase 2 |

- Phases 2 and 3 have more damage because of the 3.5 s punish windows and the castings' 50 % painshare.
- In Phase 1, bot damage doesn't scale well with more bots: 4 bots deal 2.0× the solo damage, because they block each other at the boss. In Phase 2, 4 bots deal 3.1× the solo damage.
- **The bots wipe in Phase 2 (4 bots) and Phase 3 (all party sizes).** They take lots of downs (heals at <40 HP: 4 / 6 / 12 solo), and a hit while low kills them. That's a limit of the bots, not a balance finding, but it does show the fight is hard. A person needs to play it to judge.

## Boss HP (built)

**8500 solo, +0.9 per extra fighter.** `CrucivarEncounter.V4Hp` and `PartyStep`.

| Party | HP | Phase 1 | Phase 2 | Phase 3 | Total (with shifts) |
|---|---|---|---|---|---|
| 1 (bot damage) | 8500 | 96 s | 78 s | 65 s | **about 4:05** |
| 4 (if people scale linearly, 4× solo) | 31450 | 89 s | 72 s | 60 s | about 3:45 |
| 4 (bot damage) | 31450 | 175 s | 94 s | ? | over 5 min |

- **Target:** gpt/08 gives area bosses about 3–4 min. You asked for a fight close to Aurelion's difficulty, so I aimed at the long end: about 4 min solo. Aurelion is the 5–7 min exception, with base 12000 and +1.25 per fighter.
- **Eruption margin, solo:** Phase 3 takes about 65 s, and the eruption comes at about 90 s. That leaves one spare cycle. If you lose much damage to downs, you'll see the eruption.
- **The step is 0.9, not Aurelion's 1.25:** more players also bring more towers and more castings, and the bots show that damage doesn't scale perfectly. With people who scale linearly, 4 players finish a bit faster than solo.
- The step uses everyone in the fight when the boss spawns.

## Casting HP (built)

**300 per casting, +0.7 per extra player** (it was a 150 demo value). `FoundryUprising.CastHp`.
- **Solo:** two castings in a row, each about 9 s of damage at 35 DPS. With the rise, they're done in about 22 s of the 36 s timer.
- **4 players:** three castings of 930 HP. At the bots' 117 DPS that takes about 24 s of the 30 s, which is tight. If people find it too tight, lower the per-player step before the base HP.

## Easy and Hard

The difficulty damage scale is unchanged (damage scale only, per the difficulty rule). HP is the same on every difficulty.

| Phase | Easy | Hard (named extra) |
|---|---|---|
| 1 Forge Rite | warnings ×1.25 | none yet: **open question 3** |
| 2 Foundry Uprising | paths drawn 2.5 s, Pour Flood timer 40 s, damage ×0.6 | **Cast in Order**: castings must die in numeral order; killing one out of order makes every turret fire a Ring Wave at once (built) |
| 3 Molten Crown | towers rise 6.0 s ahead, crown warned 4.0 s, eruption on the 5th crown (built), damage ×0.6 | **Second Press**: the fullest tower in each set re-stamps 2.0 s after landing (built 30 Oct, proposal) |

## Built and tested
- `CrucivarEncounter`: v4 HP 8500, PartyStep 0.9, and the helper `V4MaxHp(players)`. `ForgeRiteTest` checks 8500 solo and 31450 for 4 players.
- `FoundryUprising.CastHp`: 300.
- `CrucivarWatch -crucivarMeasure -crucivarBots n`: the measure mode. It logs each phase once, holds the boss just above the threshold until the loops are done, and logs a partial phase if the fight ends first.

## Still to do (lane plan)
1. Hard extra for Phase 1 (Second Press for Phase 3 is built).
2. Escape checks for v4 (worst cases are listed in each phase doc).
3. The telegraph look and the fire VFX pass.
4. Merge Azure `feature/confrontation-menu`. That fixes the old controls. Then check that the party count at boss spawn includes the menu bots, rebuild, and run the autotest under the lock.
5. One Azure PR into `feature/confrontation-menu`, after your OK through the review inbox.

## Open questions
1. ~~Fight length~~: settled 29 Oct, keep about 4 min solo.
2. PartyStep 0.9: should co-op be longer than solo (Aurelion's 1.25) or about the same?
3. Phase 1 Hard: does it need a named extra, or is the damage scale enough?
4. Should passive menu bots count toward the HP, as Aurelion's do not?
