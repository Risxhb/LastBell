# New-player tutorial (October 31, 2026)

A guided fight against the **Tempo Tyrant** on Metronome Court that teaches the controls, the Arcanist's playstyle, every boss mechanic type in the game so far, and the party mechanics with three bot allies.

## Starting it

- **Boss Rush** → Metronome Court → the **Tutorial** card (Bosses row) → Launch. The class picked in Select Class is used.
- `LastBell.exe -lastbellTutorial [-lastbellClass Arcanist|Vanguard|Chimesinger]` (class defaults to the Arcanist). Workspace launcher: `Play Tutorial.cmd`.
- Esc → Main Menu returns to the menu; Retry runs it again.

## How it works

- Entry: `RunDirector.Bosses` "Tutorial (Tempo Tyrant)" (practice only, kept last). It spawns a normal `TempoTyrantEncounter` and attaches `TutorialDirector` (`unity/Assets/LastBell/Run/Tutorial/`).
- The director runs a tick script from `BossEncounter.BeforeStep`. Each step shows text in a panel on the left; text pages wait for **Enter**.
- **Boss attacks** are his real script, one block at a time: `ForcedNext` + `BeginCombat`, halted at the block boundary (`BossEncounter.Halt`). Between steps he stands still.
- **Shared mechanics** are plain `Hazard`s through the `HazardSystem` (no new damage code): soak towers (`Soak`, `ShareTotal`/`ShareCount`), spreads (`Affects`), burning pools (`BurnPerSecond`), Shade/Sun marks (`Affects`), a chain (`HazardShape.Tether`, 6–10 m band), a following circle, and jump-clearable low bars (`JumpClearable`).
- An attempt fails if the player is hit by that step's mechanic (or a tower is short, or burn damage > 15). Failed steps retry, up to 3 attempts, then move on.
- **Allies:** three bots (Vanguard, Chimesinger, Arcanist) always join. They are `AllyBot`s (dodge only, no attacks) wrapped in `TutorialBot`, which walks them to the jobs the director gives them (towers, spread spots, mark sides).
- **Boss can't die early:** `BossEncounter.HpFloor` = 25% of his HP (above Broken Cadence) until the finale, then 3% during the Falling Hours step, then 0 for "Finish him". `DebugDefeat` (F9) still works.
- **Nobody goes down:** a mercy heal tops players up below 40 HP and revives anyone downed (counted in the autotest report).
- The `WipeIfEmpty` rule is shown in text but not applied, so an empty tower can't end the tutorial.

## Steps

1. Basics: welcome, move (gold ring), dash, sprint/jump, potion (F).
2. Arcanist: overview, Magic Missile (tap and volley), Disintegrate (build 2+ glyphs), Rune Sigil, Arcane Comet, Arcane Surge, the rotation. Vanguard and Chimesinger get a one-page "coming later" note instead.
3. Tempo Tyrant: Haste/Normal, Threefold Severance, Sundering Wake (backswing, fissures, mirrored fissure in Haste), Hollow Crown, Spiral Slices (+ every other slice), Closing Rush, then low bars.
4. Party: allies intro, soak towers (2 towers of 2), spread marks, burning pools, Shade/Sun marks, chain to a post, following circles.
5. Finale: Broken Cadence + Falling Hours (HP set to 9%, survive one chain), then "Finish him" → practice victory and a "Tutorial complete" panel.

## Testing

`LastBell.exe -lastbellTutorialTest -lastbellOut <folder>` plays it end to end with a bot and writes `tutorial-report.txt` plus a screenshot per step. Scripts: `tools/tutorial/build.sh`, `tools/tutorial/run-test.sh` (workspace root).

## Not covered yet

- Vanguard and Chimesinger class sections.
- Aurelion-only memory mechanics (Ecliptic Order numerals, Severance sides, Total Eclipse wells, Sunspot chains) and Crucivar's pulls/jets are not drilled one by one; the tutorial teaches their shared building blocks.
- Telegraph colours follow whatever style the hazards currently use (Telegraph-Style v2 was being applied to the Tempo Tyrant at the same time).
