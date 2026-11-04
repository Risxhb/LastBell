# Campaign loop (Start Run)

Agreed with Risx on November 4, 2026. Code: `unity/Assets/LastBell/Run/Campaign.cs` (order, arenas, unlocks, travel
carry), `RunDirector.cs` (flow), `TempoTyrantEncounter.cs` (Overclocked). Branch `feature/campaign-loop`.

## The loop

| Stretch | Arena | Fights |
| --- | --- | --- |
| Tutorial (first run only) | Metronome Court | The guided Tempo Tyrant tutorial |
| Area 1 | Skyward Spire | Chimebound Oracle, Dial Warden, Pendulum Sentinel (shuffled) → shop → **Aurelion** |
| Area 2 | Brass Foundry | Escapement Twins, Rivet Cantor, Crucible Skater (shuffled) → shop → **Crucivar** |
| Area 3 | Verdant Chimes | Briar Metronome, Silt Carillon, Vesper Seedkeeper (shuffled) → shop → **Sylvara** |
| Final | Shattered Hour | shop → **Bellkeeper** |
| Bonus (Hard win only) | Metronome Court | **Overclocked Tempo Tyrant** |

- **Start Run** is clickable again (it was greyed out since Oct 27). The first run starts with the tutorial; once it is
  cleared (in a run or from Boss Rush) later runs start in Skyward Spire. Saved in PlayerPrefs
  (`LastBell.Campaign.TutorialDone`).
- **Travel:** after an area boss the run moves to the next arena's scene. The class, difficulty, bot allies, order,
  time, clears, potions and the shop wallet/items are carried; the new arena opens at a preparation card (Ready).
- **Health:** refills before every fight after the first. **Potions** carry over between minibosses and refill before
  each area boss and the Bellkeeper. The **shop** opens only before area bosses and the Bellkeeper (4 visits).
- **Hard Bellkeeper win:** saves the unlock (`LastBell.Campaign.OverclockedUnlocked`), then the bonus fight. Losing the
  bonus still ends the run as a victory. The **Boss Rush card** "Overclocked Tempo Tyrant" is shown "(locked)" and
  can't launch until the unlock.
- **Retry** starts a fresh run (no tutorial, no bonus) back in Skyward Spire. Death still restarts the run.
- HUD: the track shows one arena's stretch at a time; intro/cleared/preparation cards name the arena and stage
  ("Brass Foundry · Miniboss 2 of 3", "Area boss", "Final boss", "Bonus fight").

## Overclocked Tempo Tyrant (provisional numbers)

Same kit, pushed: 3600 HP (from 2600), Broken Cadence from 50% (from 20%), Falling Hours from 20% (from 10%), and the
Mirrored Wake in both tempos (Haste only before). Every overlap is still a route-checked Broken Cadence pairing; a
mirrored lane in Normal has more time than the Haste one that is already checked. Practice: `-lastbellPractice overclocked`.

## Tests

`LastBell.exe -lastbellCampaignTest -lastbellShopBot -lastbellOut <dir>` plays the whole loop through Start Run's own
launch path (fights ended with the dev defeat): order, arena per fight, shop visits, carried potions and Chimes,
tutorial flag, unlock and lock, the bonus boss and its 50% Broken Cadence, bonus loss = victory, Retry, and a Medium
run with no bonus. Other automated suites (`-lastbellOut`) keep a run in one arena and never get the tutorial, so they
hold their director through a run; they see the 13-fight campaign. Script: `tools/campaign/build-and-test.ps1`.

Results, Nov 4 (players `builds/campaign-20261104/r2`–`r4`, 0 errors / 65 warnings): campaign 89/0, Confrontation
109/0, Arcanist 34/0 (r3); flow stage 8 quick 231/0 (r4); tutorial 45/0 and Tempo Tyrant 180/0 (r2, unaffected by the
later test-only fixes); Tempo, Oracle and tutorial launches 0 exceptions. Fixes found on the way: the dev defeat (F9)
couldn't finish the Chimebound Oracle in phase 1 (his 50% hold), and the potion pips threw with more than 5 potions.

## Open

- Not played by a person. Dial Warden's parry QTE is in Area 1 as built (the design rules say no parry).
- Minibosses are practice-tuned placeholders in several cases; run tuning (HP, potions) is untested by play.
- The Overclocked numbers and whether a lost bonus should be retried from Boss Rush only are proposals.
- LAN: unchanged (practice-only lobby).
