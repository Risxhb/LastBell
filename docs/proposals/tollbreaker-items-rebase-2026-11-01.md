# Tollbreaker items: rebase after the return-hit change

**Status: accepted by Chzy18 on 2026-11-01** (review inbox): Backswing 3 → 6, Tempered 7. Grinding Toll's return also stays at 3 ("Keep both at 3"), so `TapBack` stays one constant. Written by the Items session, 2026-11-01. Asked for by the overview after Chzy18 picked option B in the review inbox (1 Nov): Twin Hurl's return hit drops from 6 to 3 (`TapBack`), about 70 DPS. The Tollbreaker lane makes the kit change on PR 14.

**Sources:**
- `docs/proposals/tollbreaker-items-2026-10-31.md` (accepted 2026-11-01)
- `docs/design/items/CATALOGUE.md` (budgets: Common +5, Rare +8, Temper +50%)
- `TollbreakerKit.cs` on the Tollbreaker branch: `TapBack` is an int, and line 303 uses it for every non-recall return hit

## What quotes the return hit

I checked all 20 TB items.
- **TB14 Backswing** is the only one that quotes it ("returning axes hit 12 instead of 6", Tempered 15). It needs new numbers.
- **The rest don't change:**
  - Keen Edge (outward hit), Heavy Haft (held throw) and Battle Spin (30 after a dash) don't touch the return.
  - Close Quarters (+12%) and Berserker's Pace (+2% a hit) are percentages, so they scale down on their own. Berserker's Pace still counts a return hit as a stack.
  - Quick Catch is about return speed, not damage.

## TB14 Backswing: the numbers

Measured as extra damage on one tap (outward hit plus return hit):

| | Base tap | Backswing | Gain per tap | Tempered | Gain per tap |
|---|---|---|---|---|---|
| Before (accepted) | 14 + 6 = 20 | 6 → 12 | +30% | 15 | +45% |
| **Proposed** | 14 + 3 = 17 | **3 → 6** | **+18%** | **7** | **+24%** |
| Same bonus as before | 14 + 3 = 17 | 3 → 9 | +35% | 12 | +53% |

- **Proposed: 3 → 6, Tempered 7.** It still doubles the return, as the item reads now. +18% on Twin Hurl taps fits the Rare budget: Close Quarters, a Common, gives +12%, and Berserker's Pace, a Rare, gives up to +10% with conditions.
- The accepted 6 → 12 was already over budget (+30%). Keeping its +6 bonus would make it worse (+35%), because the base is smaller.
- **Tempered:** +50% of a +3 bonus is +4.5. Damage is a whole number, so I rounded down to 7 (+4). 8 would be +5, which is over the temper step.
- **Effect text:** "Returning Twin Hurl axes hit 6 instead of 3." Tempered: 7.

## Grinding Toll's return

- `TapBack` also sets the hit when the Grinding Toll axe flies home through the boss, so that drops from 6 to 3 too. The Tollbreaker lane should confirm that's intended.
- Backswing's text says Twin Hurl only. I'd keep it that way, so the hook raises the Twin Hurl return only and leaves Grinding Toll's return at the base.

## Open questions

1. Accept Backswing at 3 → 6, Tempered 7?
2. Is it fine that Grinding Toll's return also drops to 3? (Tollbreaker lane)

Once accepted: update TB14 in the catalogue and in `tollbreaker-items-2026-10-31.md` (including the kit numbers at the top), and ask the shop lane to commit the catalogue.
