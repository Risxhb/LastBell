# Run items: balance pass on paper

**Status: accepted by Chzy18 on 2026-11-02** (review inbox, all six as one block). Written by the Items session, 2026-11-02. This closes catalogue open question 1, "for the balance pass" (Chzy18, 2026-10-28), from the numbers we already have. Under the 2 Nov "keep working" rule I wrote it now instead of waiting. Playtests can still retune any number later.

**Sources:**
- `docs/design/items/CATALOGUE.md`: the balance notes and open questions, and budgets of Common +5, Rare +8, Keystone +12 net, with Temper at +50%
- `docs/proposals/meta-shop-spec-2026-10-29.md` §3 (cross-class gating) and §8 (power budget)
- `docs/proposals/meta-loot-and-toll-2026-10-29.md` (open question 8, Toll items)
- `gpt/08-design-rules.md` (soloable, nothing required, defensive caps)
- Parties are now 1–4 players (Chzy18, 1 Nov), so an item counts at most 3 allies

## Proposed decisions

| # | Item | Now | Proposed | Why |
|---|---|---|---|---|
| 1 | Glass Edge (A09, Arcanist Common) | +12% damage, −8% max HP; Tempered +18% | **+8% damage, −8% max HP; Tempered +12%** | +12% is more than twice the Common budget of +5. With the −8% HP trade, +8% is close to budget. It stays a Common, so the Arcanist's tier mix and shop stock don't change |
| 2 | Lone Bell (G07, general Rare) | +15% while no ally is within 8 m; Tempered +22.5% | **+12%; Tempered +18%** | Solo it's always on, so it works as a flat +15% against a +8 budget. +12% still gives solo players a strong pick, which suits a harder solo run |
| 3 | Formation (G08, general Rare, Party) | +6% per ally within 5 m, up to +18%; Tempered +27%; solo +6% | **unchanged** | Its full value needs all 3 allies within 5 m, which boss spreads often break. With at most 4 players, the cap of 3 allies is reached exactly, never beyond |
| 4 | Fourth Choice (G09, general Rare) | built as the shop version: 7 stock slots instead of 6 (+1 class slot) | **keep the shop version as final** | The pick-offer version has no place in a shop-only run (meta: shop only, 28 Oct). One more slot is choice, not power, so it fits a Rare |
| 5 | Resonant Rune (A19, Arcanist Rare, Ring) | built gated: offered only with a Chimesinger in the party; the loot doc's solo fallback (Sigil +10%) never shows | **keep gated; drop the fallback text** | Spec §3 gates cross-class items when that class is absent, so the fallback could never be bought. Nothing requires it, and solo play doesn't lose an item it could have had |
| 6 | Toll IV and VII items (T01–T10) | 10 unnamed slots | **name them after playtests, and aim them at the thin slots** | The loot doc already suggests waiting until playtests show which slots are thin. On paper the thin slots are the Arcanist's single Keystone and the general pool, which has no Keystone |

## What changes in code (shop lane, once accepted)

- **Glass Edge** (`ItemCatalog.cs`): `Value` 12 → 8, `Tempered` 18 → 12.
- **Lone Bell** (`ItemCatalog.cs`): `Value` 15 → 12, `Tempered` 22.5 → 18.
- Nothing else changes in code. Fourth Choice and Resonant Rune are already built as proposed.

## Rule check

- Nothing here touches dash, dodge or post-hit immunity, potions, or the defensive caps. Glass Edge's −8% max HP is a loss, not a gain, so the +20% cap doesn't apply.
- Soloable, nothing required: solo players keep Lone Bell and Formation's flat +6%. Resonant Rune is the only gated item, and it's an extra, never a requirement.

## Open questions

1. Accept all six decisions as one block (like the Temper values on 28 Oct)?
