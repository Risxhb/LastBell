# Tollbreaker items: 20 class items to replace the archived Vanguard items

**Status: accepted by Chzy18 on 2026-11-01** (review inbox). Riven Iron was changed to personal only. The 4 slice items swap in with the class code swap. The Vanguard rows stay in the catalogue, marked archived. Written by the Items session on 2026-10-31, at Chzy18's request through the overview ("Queue them").

**Sources:**
- `AGENTS.md`
- `gpt/08-design-rules.md` §0, §2, §3, §4 (Tollbreaker, Brace retired) and §5 (melee + ranged danger)
- `docs/proposals/tollbreaker-kit-and-rotation-2026-10-30.md` (accepted build spec)
- `TollbreakerKit.cs` on the Tollbreaker branch (Azure PR 14, commit `39364c1`)
- `docs/design/items/CATALOGUE.md` (format, budgets, caps)
- the shop spec §8 (power budget)

**What this replaces:**
- All 20 Vanguard rows in the catalogue (V01–V20), including the 4 slice-1 items already built in PR 10.
- Steady Stance (V08) is cut because it depends on Brace. Its slot gets a new item.
- Once these are accepted, the V rows become "archived (class retired)", and these TB rows take their place.

## The kit these items are built on

These are the numbers from the accepted spec (all TUNE):
- **Twin Hurl (LMB):**
  - A tap hits 14 going out and 6 coming back, with a 0.25 s throw rate and a 9 m range.
  - Returning axes fly at 18 m/s, so the closer he stands, the faster he throws ("closer is faster").
  - Holding for 0.5 s gives a heavier throw that hits 30 and lodges: 6 s in the boss, 8 s in the floor.
- **Grinding Toll (2):** an 8 s cooldown. The axe spins in place for 2 s, 1.5 m across, hitting 10 every 0.4 s. It never seeks.
- **Splitting Throw (3):** a 10 s cooldown with a 0.4 s windup. It hits 50 and lodges. In the boss, it gives his own Twin Hurl and Grinding Toll hits **+40% for 6 s**. The bonus is personal.
- **Call of Iron (4):** a 6 s cooldown. Lodged axes fly back in a straight line. An axe that crosses the boss hits 20 + 8 per metre, up to 110.
- **Bell Frenzy (5):** a 30 s cooldown, lasting 4 s. Axes come back the instant they hit, and each tap throws a fan of 3; the side axes deal 50%.
- **Dash:** the shared dash. No invincibility and no defensive ability.

## The items

The tiers mirror the old Vanguard set: 9 Commons, 9 Rares (one Crack combo and one Party combo) and 2 Keystones. Budgets follow the shop spec: Common +5, Rare +8, Keystone +12 net, and Temper +50%.

| # | Item | Tier | Tags | Effect | Tempered (+) | Solo | Hooks (TollbreakerKit) | Replaces |
|---|---|---|---|---|---|---|---|---|
| TB01 | Keen Edge | C | | Twin Hurl's outward hit +20% (14 → 17) | +30% | works | built: damage hook (Twin Hurl) | V01 Tempered Edge (slice) |
| TB02 | Short Fuse | C | | Splitting Throw cooldown −20% (10 → 8 s) | −30% | works | new: `SplitCd` from item stats | V05 Countdown (slice) |
| TB03 | Wide Grind | C | | Grinding Toll's ring is 30% wider (1.5 → 1.95 m across) | +45% | works | new: `TollRadius` | V03 Wide Arc |
| TB04 | Long Grind | C | | Grinding Toll spins 2.5 s instead of 2 s (one more hit) | 2.8 s | works | new: `TollSpinTicks` | V04 Long Reach |
| TB05 | Quick Catch | C | | Returning axes fly 20% faster (18 → 21.6 m/s) | +30% | works | new: `ReturnSpeed` | V02 Quick Hands |
| TB06 | Heavy Haft | C | | The held (lodging) Twin Hurl throw +20% damage (30 → 36) | +30% | works | built: damage hook (held throw) | V06 Grit |
| TB07 | Iron Pull | C | | Call of Iron +1.6 per metre (8 → 9.6), cap 110 → 125 per axe | +2.4 per metre, cap 132 | works | new: `CallPerMetre`, `CallMax` | V07 Clockwork Step |
| TB08 | Battle Spin | C | | After a dash, the next Twin Hurl tap hits like a held throw (30) without lodging | that hit also +15% | works | new: dash-end event | V09 Momentum |
| TB09 | Close Quarters | C | | Twin Hurl hits +12% while you're within 4 m of the boss | +18% | works | built: damage hook; new: distance to boss at release | V08 Steady Stance (cut, Brace) |
| TB10 | Deep Split | R | | Splitting Throw's bonus lasts 8 s instead of 6 (the axe stays lodged to match) | 9 s | works | new: `SplitTicks`, `BossLodgeTicks` from item stats | V10 Double Opening |
| TB11 | Twin Split | R | | With both axes in hand, Splitting Throw throws both. The second lodges beside the first for 50% damage. The bonus doesn't stack, and both hands are empty until Call of Iron | second 75% | works | new: two-axe split throw | V11 Pendulum Cut |
| TB12 | Quick Grind | R | | Grinding Toll hits every 0.3 s instead of 0.4 s | 0.27 s | works | new: `TollEvery` | V12 Heavy Hour |
| TB13 | Ringing Return | R | | Each axe Call of Iron sends through the boss takes 2 s off Splitting Throw's cooldown | 3 s | works | new: on-recall-hit event, cooldown refund | V13 Quick Verdict |
| TB14 | Backswing | R | | Returning Twin Hurl axes hit 12 instead of 6 | 15 | works | new: `TapBack` | V14 Iron Echo |
| TB15 | Anchor Line | R | | Floor-lodged axes last 12 s instead of 8, and Call of Iron from the floor deals +20% | +30% | works | new: `FloorLodgeTicks`; built: damage hook (recall, floor source) | V15 Second Step |
| TB16 | Berserker's Pace | R | | Each Twin Hurl hit from within 4 m adds +2% damage for 3 s, up to +10%. Lost when you're hit | +3% each, up to +15% | works | new: stack counter, distance to boss; built: on-hit reset | V16 Rhythm Keeper |
| TB17 | Riven Iron | R | | An axe Call of Iron sends through the boss cracks it for 4 s: **your own** Splitting Throw and Call of Iron hits on it deal +15%. Personal only, with no ally effect (Chzy18, 2026-11-01) | +22.5% | works | built: Crack state (Cracked Armour), personal; new: recall trigger | V17 Cracked Armour (slice) |
| TB18 | War Cry | R | Party | Bell Frenzy gives allies within 6 m +10% damage for its 4 s | +15% | gives it to you | built: ally range, damage buff (Chorus of Valour, Rally) | V18 Rally |
| TB19 | Frenzied Hour | K | | Bell Frenzy cooldown 30 → 18 s, but it lasts 3 s instead of 4, and the fan's side axes deal 35% instead of 50% | cooldown 15 s | works | new: `FrenzyCd`, `FrenzyTicks`, `FanSide` | V19 Toll of Steel |
| TB20 | Rooted Axe | K | | Splitting Throw's axe stays in the boss until Call of Iron (no 6 s timeout), and the split lasts as long. The bonus is +25% instead of +40%, and Call of Iron's cooldown is 9 s instead of 6 | bonus +30% | works | new: no boss timeout while split, `SplitBonus`, `CallCd` | V20 Oathbound (slice) |

**Shop slice:** TB01, TB02, TB17 and TB20 will take the place of the 4 Vanguard items built in PR 10 **at the class code swap**, after the Aurelion and Ultimate playtests (Chzy18, 2026-11-01). Until then the Vanguard items stay in the shop. These 4 were chosen because they reuse hooks that already exist (the damage hook, cooldowns, the Crack state) or need only constant overrides.

## Rule check

- **No invincibility, no defensive ability.** No item gives immunity, damage reduction, max HP or a shield. Grit's slot became Heavy Haft. Battle Spin only changes the attack after a dash.
- **No seeking.** Grinding Toll still stops at the cursor. Every throw and recall flies in a straight line, and nothing turns toward the boss.
- **Soloable, nothing required.** 19 items need nothing from allies. War Cry has a solo fallback. No item depends on another class.
- **Melee + ranged danger (§5).**
  - Items change his damage, not where boss attacks can reach him.
  - Close-range items (Close Quarters, Berserker's Pace, Quick Catch, Battle Spin) make standing close pay more.
  - The two items that help when he's pushed out (Iron Pull, Anchor Line) only pay through the recall line across the boss. No item makes standing far away better than standing close.
- **Brace is retired.** Steady Stance is cut, and no item mentions Brace.
- **The split stays personal** (Chzy18, 2026-10-30). Riven Iron is a separate mark from Call of Iron, and it's personal too (Chzy18, 2026-11-01).
- **Potions (§3).** No item touches potions.
- **Colours.** Item effects reuse his kit look: thin dotted cyan, never filled, never coral, amber or violet.
- **Phase changes.** Rooted Axe's lodged axe persists through a phase change, like other player setup (§4). It resets before each encounter (§3).

## Balance notes

- **Strongest on paper:**
  - Twin Split: in a split window, the second axe adds 25 damage for a slower throw rate.
  - Ringing Return with two recalled axes: up to 4 s off a 10 s cooldown.
  - Both need a playtest before the numbers are trusted.
- **Rooted Axe:** an endless +25% on Twin Hurl and Grinding Toll is about the +12 net Keystone budget, because he throws one-handed the whole time.
- **Frenzied Hour:** Frenzy uptime goes from about 13% to about 17%, at a weaker fan.

## Decisions (Chzy18, 2026-11-01)

1. **Riven Iron:** personal only. It boosts only the Tollbreaker's own Splitting Throw and Call of Iron hits, with no ally Crack.
2. **Slice swap:** with the class swap. The 4 Vanguard items stay in the shop until the class code swap after the Aurelion and Ultimate playtests.
3. **Old Vanguard rows:** kept in the catalogue, marked archived.
