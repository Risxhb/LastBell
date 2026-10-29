# Crucivar v4, Phase 3: Molten Crown

**Status: accepted for now (Chzy18 via the review inbox, 28 Oct 2026), the hard cut as built (5c83988). Numbers are placeholders.** Chzy18 picked this direction on 28 Oct from three options:
- **A, Molten Crown:** picked.
- **B, Chained Crucible:** tethered pairs and line stacks.
- **C, Meltdown:** a remix of every attack under a heat timer.

## Hard cut (built 28 Oct, 5c83988)

Chzy18 found the first version too easy ("we need more"), so every layer from the browser mock-up was added:
1. **Tighter towers:** set B lands 1.6 s after set A (not 2.0 s), on the opposite side of the boss.
2. **Hidden counts:** on even cycles each mould shows "?" instead of its count. You have to remember the last cycle's counts.
3. **Pours during the towers:** Casting Order at 3.5 s aims through the players while they soak. The pours land at 5.5–7.0 s.
4. **Backdraft after set B:** pull, then the 5 m blast, starting 0.2 s after the last set lands.
5. **A lone lava turret:** it stays on the rim all phase and fires a Lane Stream every 4 s.
6. **Tower slag:** every landed tower leaves a 2 m slag pool (20/s) until the phase ends.
7. **Heat:** every hit and burn gets +15 % per crown, up to ×1.45 before the eruption.

The cycle is 21.5 s plus a 3.5 s punish. Code: `Abilities/MoltenCrown.cs`. Test: `MoltenCrownTest`, with v4 at 45/45. Watch it with `crucivar-playtest/Watch bots - Crucivar v4 Phase 3 Molten Crown.cmd`.

## Phase
- **HP band:** 30–0 % (Open Pressure). The second form is already in place, so there's no new transformation. Pools from Phase 2 clear at 30 %.
- **Theme:** the furnace overflows. Crucivar casts soak towers in moulds and the party has to fill them. After every tower set, the molten lava rim rises for good, so the arena keeps shrinking. If the fight drags on, the furnace erupts.
- **Compared with Aurelion v4:** this plays the part of Aurelion's Phase 3 and Phase 4 together. It has staggered soaks where a failure wipes the party, like Meridian Towers, and a floor that keeps getting smaller as the soft enrage, like Last Light.

## Mechanics (Medium)

### Mould Towers (R&S block 2: soak towers, staggered)
- **Shape:** 2 m casting moulds placed 4–7 m from the boss, inside the current edge.
- **Count dots:** each tower shows how many players it needs.
- **Timing:** they rise 5.0 s before landing. **Set A lands at 5.0 s and set B at 7.0 s**, so you soak A and then run to B.
- **Landing:**
  - Only who is inside at the moment it lands counts.
  - Enough players inside: no damage.
  - Too few: **Molten Overflow**, the party wipes. It's introduced in the first cycle, with a big count-dot warning.
  - Extra players beyond the count, farthest from the centre first, take a heavy hit (50).
- **Solo:** three one-player towers in a row, at 5.0, 7.0 and 9.0 s.
- **Melee and ranged:** both have to move into the 4–7 m ring, so nobody can stay at the wall or hug the boss.

| Party | Set A | Set B |
|---|---|---|
| 1 | 1, 1, 1 in a row | — |
| 2 | 1 + 1 | 1 + 1 |
| 4 | 2 + 2 | 2 + 2 |
| 8 | 2 + 2 + 2 + 2 | 4 + 4 |

### Crown Rise (R&S block 17: temporary arena edges, made permanent here)
- **Shape:** a ring from the new edge out to the old one, warned 3.0 s ahead.
- **After it lands:** the ring stays as **molten crown**, Ground, burning 50/s, until the fight ends.
- **Edge per cycle:** 13.7 m, then 11 m, then 9 m, then 7 m.
- **Fourth crown (the hard enrage):** **Furnace Eruption**, the whole floor floods and the party wipes. It has a 5 s warning that's impossible to miss. At the measured damage it should come after about four cycles (about 100 s) in the phase.
- **Ranged threat:** the edge is the danger, so ranged players get pushed in a little further every cycle.

### Kept from Phases 1 and 2
- **Ember Brand** at 9.5 s, after the towers, so a brand never lands on a shared tower. Its pools last all phase, and with the arena shrinking that's a strong soft enrage.
- **Furnace Spin** at 14.0 s: the jets now reach the crown edge.
- **Anvil Quake** at 19.0 s: the bands scale to the current edge (0–3 m, 3 m to 60 % of the edge, then out to the edge).

## Timeline: one cycle of about 26 s

| t (s) | Event | Live patterns |
|---|---|---|
| 0.0 | Mould Towers set A rise (count dots) | towers |
| 2.0 | set B rises | towers |
| 5.0 | set A lands | towers |
| 7.0 | set B lands (solo: third tower at 9.0) | towers |
| 9.5 | Ember Brand (lands 13.0) | brands + pools |
| 10.0 | Crown Rise warned (lands 13.0) | brands + crown + pools |
| 14.0 | Furnace Spin (live 15.5–19.5) | jets + crown + pools |
| 19.0 | Anvil Quake (20.4 / 21.2 / 22.0) | jets + quake + crown + pools |
| 22.5 | **punish 3.5 s** (furnace venting) | pools + crown |
| 26.0 | next cycle, one ring smaller | |

- **First cycle:** no Furnace Spin, to introduce the towers and the crown.
- **Overlaps:** three or four patterns at once from the second cycle on.

## Easy and Hard
- **Easy:**
  - towers rise 6.0 s ahead;
  - Crown Rise warned 4.0 s ahead;
  - Eruption on the fifth crown instead of the fourth;
  - damage ×0.6.
- **Hard (named extra: Second Press returns):** the fullest tower in each set re-stamps 2.0 s after it lands, pushing toward the centre. Whoever just soaked it has to step out. This is the existing v2 mechanic.

## Escape check (to add once accepted)
Worst cases to check:
- a set A tower soaker running to the far set B tower, 2.0 s, at 4.2 m/s with one dash;
- the crown landing while a player carries a brand at the edge;
- Furnace Spin at a 7 m edge, where following the gap at mid-range has to stay possible.

## Conflicts with the rules
None found.
- Fail-wipes and an enrage are allowed since the 26 Oct change.
- The edge only hurts through the telegraphed Crown Rise, never as a surprise.
- Every tower can be soaked by any class.

## Open questions
1. Are the crown sizes right (13.7 → 11 → 9 → 7 m, then erupt)?
2. Should brand pools and pour channels clear when the crown rises, or keep piling up?
3. Tower counts for 3 and 5–7 players. My default is to split the party as evenly as possible across 2 towers per set (3 sets for 7 or more).
4. Should the crown be a burn (50/s, you can stand in it briefly) or a hard wall?
