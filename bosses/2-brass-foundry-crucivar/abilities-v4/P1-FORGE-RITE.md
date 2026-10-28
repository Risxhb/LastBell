# Crucivar v4, Phase 1: Forge Rite

**Status: accepted for now (Chzy18, 27 Oct 2026).** It may be tuned harder later.
- Prototype: `-lastbellAbilitySet v4`. Code: `unity/Assets/LastBell/Run/Encounters/Crucivar/Abilities/ForgeRite.cs`. Test: `ForgeRiteTest` 21/21 (7a71793).
- Watch it in `crucivar-playtest/Watch bots - Crucivar v4 Forge Rite.cmd`.

## Phase
- **HP band:** 100–65 % (Sealed Furnace). HP is still a placeholder: 6600, with no co-op scaling yet. It gets set from measured DPS once every phase is accepted.
- **Position:** Crucivar walks to the arena centre at the pull and stays there, so every pattern is the same on all sides.

## Loop (20.5 s, then a 3.0 s punish), Medium numbers

| t (s) | Mechanic | Shape | Kind | Warning | Damage | Melee threat | Ranged threat |
|---|---|---|---|---|---|---|---|
| 0.0 | **Ember Brand** | 3 m circle on each player, follows them | Ground | 3.5 s | 30 to anyone else inside | brands drop in the melee ring | carriers drift to the edge, and pools there box ranged players in |
| | brand pool | 2 m circle under the carrier | Ground | — | 20/s burn | — | — |
| 2.0 | **Furnace Spin** | 4 lanes, 1.8 m wide, boss to the wall, 90° apart, sweeping 40°/s for 4 s; the direction flips each loop | Strike | 1.5 s | 30 per jet | the gap near the body is narrow | the gap at the wall moves 9.8 m/s, so ranged must come within about 6 m |
| 5.0 | **Backdraft** | everyone pulled 2 m in (1.6–2.0 s), then a 5 m blast at 3.0 s | Ground | 3.0 s | 45 + 1.5 m push | the blast | the pull drags edge players toward it |
| 8.5 | **Casting Order** | pours I–IV: 2.6 m lanes, boss to the wall, through each numbered player; aim locks 1.5 s before landing; land 0.5 s apart at 10.5–12.0 s | Ground | 2.0 s + | 30 each | lanes start at the body | lanes run to the wall |
| | pour channel | the lane, for 10 s | Ground | — | 20/s burn | — | — |
| 11.0 | **Anvil Quake** | three bands landing inside-out: 0–4 m at 12.4 s, 4–9 m at 13.2 s, 9 m to the wall at 14.0 s | Strike | 1.4 s | 40 per band | the inner band | the outer band; step inward after it fires |
| 13.5 | **Ember Brand** | again | | | | | |
| 17.5–20.5 | **Punish** | vents cooling, boss vulnerable | | | | | |

- **Overlap:** up to three live patterns, counting pools.
- **Memory:** the numerals over players show on odd Casting Orders and hide on even ones, which replay the same order.
- **Solo:** you are I, II and III in Casting Order. The brand only hurts others, so solo it's about pool placement.

## Settled for this phase (dated)
- **26 Oct:** Ember Brand pools last until the phase ends. The phase change clears them.
- **27 Oct:** the jets reach the wall, so out-ranging doesn't work.
- **27 Oct:** Hammerfall (front/back cleave plus slam) was removed as unfair right after the pours. Anvil Quake replaces it, landing after the last pour.
- **27 Oct:** the boss stands at the arena centre. Following the Furnace Spin gap at mid-range is the intended dodge.
- **27 Oct:** pools piling up through a long phase is an accepted soft enrage. The watch bots carry their brands out to the wall.
- **27 Oct:** Phase 1 accepted for now.

## Not done yet
- **Easy:** for now, every warning is only stretched by 1.25.
- **Hard:** no named extra mechanic yet.
- **Escape-check numbers:** the v4 entries aren't in `crucivar-escape-check.ps1` yet.
- **Telegraph look and fire VFX:** still placeholder shapes.
- **Co-op rules:** N = 2/8 rules are still to be written. The prototype scales brands and numerals by party size, but only N = 4 has been watched.
