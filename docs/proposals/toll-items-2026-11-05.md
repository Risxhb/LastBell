# Toll IV and VII items: 10 sidegrades

**Status: proposal (drafted 5 Nov, tune after playtest). Done as recommended, review later.** Written by the Items session on 2026-11-05. Chzy18 asked through the overview for these to be drafted now, not after playtests.

**Sources:**
- `docs/design/items/CATALOGUE.md` (budgets and the 2 Nov balance pass, which names the thin slots)
- `docs/proposals/meta-loot-and-toll-2026-10-29.md` §1.4 (budget) and §2.2: Toll IV adds 5 items to every pool, and Toll VII adds 5 more. Both are sidegrades within budget.
- `docs/proposals/meta-shop-spec-2026-10-29.md` §8 (power budget) and §10 (the Toll ladder in its shop form)
- `gpt/08-design-rules.md`: no invincibility or post-hit immunity, no jump, no class required, 1–4 players, and potions per §3

## What these are

- These are **sidegrades**: each one trades something away, so they add choices without raising the power curve. They stay inside the shop budgets: Common +5, Rare +8, Keystone +12 net.
- They're in stock only on runs at Toll IV or higher (T01–T05) and Toll VII or higher (T06–T10). They're bought like any other item.
- **The thin slots are filled first:** the Arcanist gets his second Keystone (T01), and the general pool gets its first two Keystones (T02, T07). The other 7 give each class and the general pool one new choice.

## The items

| # | Item | Class | Tier | Toll | Effect | Tempered (+) | Solo | Hooks | Look (icon note) |
|---|---|---|---|---|---|---|---|---|---|
| T01 | Overchannel | Arcanist | K | IV | Each Disintegrate step that hits carves 2 glyphs instead of 1, but you lose 1 glyph every 3 s while you're not channelling | lose 1 every 4 s | works | new: glyph carve count, glyph fade timer | a beam splitting into two rune glyphs, with a third glyph crumbling away at the edge |
| T02 | Leaden Step | general | K | IV | +15% damage, but each dash costs 45 stamina instead of 30. Dash distance and speed are unchanged | dash costs 40 | works | built: damage hook; new: dash stamina cost | a boot with an iron bell-weight chained to the heel |
| T03 | Double Toll | Chimesinger | R | IV | Bellstrike tolls again 1 s later for 40% damage, but its cooldown is 20% longer | second toll 60% | works | new: delayed repeat of a hit, Bellstrike cooldown | a bell with two ripple rings, the outer one fainter |
| T04 | Short Split | Tollbreaker | R | IV | Splitting Throw's bonus is +55% instead of +40%, but it lasts 4 s instead of 6 (the axe stays lodged as long) | +60% | works | built: `t.split.bonus`, `t.split.ticks`, `t.lodge.boss` | an axe biting deep into a cracked bell plate, with a small half-empty hourglass |
| T05 | Steady Breath | general | C | IV | Stamina regenerates 15% faster, but sprinting drains 28/s instead of 25/s | +22% | works | built: sprint drain; new: stamina regen rate | a bellows breathing out an even, slow wave line |
| T06 | Lingering Rune | Arcanist | R | VII | Rune Sigil goes off after 1.2 s instead of 0.8 s, and its rune stays for 2 s more, hitting again for 40% if the boss is still inside (outline only, no filled disc) | second hit 60% | works | new: Sigil delay, lingering second hit | a rune circle outline with a second, ticking ring around it |
| T07 | Patient Bell | general | K | VII | Your first hit on the boss after 2 s without hitting it deals +60%; all your other hits deal −10% | first hit +80% | works | built: damage hook; new: time since your last boss hit | a hammer held still above a bell, with a small pause mark |
| T08 | Low Note | Chimesinger | C | VII | Resonant Note deals +12% damage, but its notes fly 15% slower | +18% | works | new: note projectile speed | one thick, heavy note with a long trailing tail |
| T09 | Long Toss | Tollbreaker | C | VII | The held Twin Hurl throw lodges up to 12 m away instead of 9, but hits 26 instead of 30 | hits 28 | works | new: held-throw range; built: damage hook (held throw) | an axe at the end of a long dotted line, stuck in a floor crack |
| T10 | Late Bloom | general | R | VII | −6% damage in a fight's first phase and +10% in every later phase | +15% in later phases (the −6% is unchanged) | works | built: damage hook; new: boss phase number | a bell-flower bud opening on a clock hand |

**Icons:** use the accepted item-icon style and rarity frames, with each class's accent and neutral brass for general items. No boss-warning coral or amber, and nothing that could read as danger gold.

## Rule check

- **No invincibility or post-hit immunity.** No item gives immunity, damage reduction, max HP or a shield. Leaden Step makes dashing cost more, and its distance stays the same.
- **No jump.** Nothing adds or needs a jump.
- **Soloable, nothing required.** All 10 work solo. None is gated on another class, and no encounter needs one.
- **1–4 players.** No item scales with party size.
- **Melee and ranged danger (§5).**
  - Long Toss lets the Tollbreaker set a longer floor lodge, but it only pays back through the recall line across the boss, and it costs damage on the throw itself.
  - Short Split rewards a burst inside the split window, not a change in range.
- **Aiming.** Nothing seeks or locks on. Lingering Rune stays where it was placed.
- **Potions (§3).** No item touches potions.
- **Colours.** Lingering Rune keeps the Arcanist's outline-only rule. Item visuals use no boss-warning colours.

## Balance notes (paper only, tune after playtest)

- **Overchannel:** about twice as many glyphs while channelling, so more Surges and full-strength Sigils and Comets. Moving and dodging costs glyphs, so it's strongest in calm phases. Net about +12.
- **Leaden Step:** +15% against fewer dashes per stamina bar (3 → 2 from full). Fights with many dodges cut its value. Net about +12.
- **Patient Bell:** best for players who time big hits into openings, and worst for steady attackers. Its real value depends on each boss's rhythm, so it's the first one to check in a playtest.
- **Late Bloom:** the first phase is usually the shortest, so it's about +6–7 overall. A Rare would normally be +8.
- **Short Split:** about the same total bonus (55% × 4 s against 40% × 6 s), but more of it lands inside one Bell Frenzy.

## Open questions

1. Accept the 10 items as drafted? They're done as recommended, so a rejection undoes them.
2. Should Patient Bell's 2 s gap be tuned per boss after the first playtest?
