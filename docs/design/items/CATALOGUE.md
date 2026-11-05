# Item catalogue

**Status:** living catalogue, owned by the Items session. Classes: **Tollbreaker, Chimesinger and Arcanist**. The 20 Tollbreaker items (TB01–TB20) were accepted on 2026-11-01 (`docs/proposals/tollbreaker-items-2026-10-31.md`). The rest were accepted through the shop spec and build plan (slice 1) and the review inbox (Chzy18, 2026-10-28), with this catalogue's Temper values. The retired Vanguard's rows and cut items were deleted on 2026-11-02; they stay in git history. The 10 Toll items (T01–T10) were accepted on 2026-11-05 and get tuned after playtests. Nothing is a proposal right now. New rows start as proposals. Written 2026-10-28.

**Sources:** `AGENTS.md`; `gpt/08-design-rules.md` (§0.3, §1, §2, §3, §4); `docs/proposals/meta-shop-spec-2026-10-29.md` (final, §12 answers); `docs/proposals/meta-loot-and-toll-2026-10-29.md` §1.4–1.9 (the 70-item draft pool and the dropped list); `docs/design/run-shop-plan.md` §3 (item framework, slice 1); `unity/Assets/LastBell/Run/Shop/ItemCatalog.cs` (built values).

**Where things stand:**
- Slice 1 is built and on Azure as [PR 10](https://dev.azure.com/ChzandZum/_git/Last%20Bell/pullrequest/10) (`feature/run-shop`), which isn't merged yet. The shop autotest passes 71/71.
- **Icons: all accepted (Chzy18, review inbox, 2026-10-29)** as the item-art direction: slice 1 in `work/shop-art-20261029/icons/`, the pool in `work/item-icons-20261030/icons/`, and the accepted redraws of Shared Ringing (C16) and Long Lungs (G02) in `work/item-icons-20261031/` (64 and 128 px).
- The Chimesinger, Arcanist and general pool items are built on local `feature/run-shop` (`96624b2`), not yet built in Unity or tested. Their cards show text only until the icons are wired in.
- **Tollbreaker (2026-11-01):** all 20 items are built on Azure PR 14 (feature/tollbreaker-class, 523232d, shop suite 145/145), and the Vanguard items are out of the shop (Chzy18 asked for the Vanguard to be removed completely). **Tollbreaker icons: all 20 accepted (Chzy18, review inbox, 2026-11-01)**, in `work/item-icons-tollbreaker-20261101/icons/` (64 and 128 px).

## How to read the table

- **Tier:** C = Common (90 Chimes, budget +5), R = Rare (150, +8), K = Keystone (240, +12 net, at most 2 per player per run, Shop 2 on).
- **Tempered (+):** the item after a Temper (110 Chimes, once per item). The rule is +50% of the effect. Where the effect is a yes/no or a whole number, the + version is written out. "—" means it can't be tempered.
- **Solo:** "works" means the item needs nothing from allies. A combo item states its fallback. "Gated" means the shop only offers it when the named class is in the party (spec §3; party bots count).
- **Hooks:** the code it needs. "built" means slice 1 already has it. "new: …" names the hook to add. Hooks inside a class kit go to that kit's owner as a request (Characters lanes, Risx for the Arcanist).
- **Icon:** accepted = approved icon, ready to wire in; redraw needed = accepted direction, but this icon must be redrawn first; none = not made yet.

## The catalogue

| # | Item | Class | Tier | Tags | Effect | Tempered (+) | Solo | Hooks | Status | Icon |
|---|---|---|---|---|---|---|---|---|---|---|
| TB01 | Keen Edge | Tollbreaker | C | | Twin Hurl's outward hit +20% (14 → 17) | +30% | works | built: damage hook | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB02 | Short Fuse | Tollbreaker | C | | Splitting Throw cooldown −20% (10 → 8 s) | −30% | works | new: `SplitCd` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB03 | Wide Grind | Tollbreaker | C | | Grinding Toll's ring is 30% wider (1.5 → 1.95 m across) | +45% | works | new: `TollRadius` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB04 | Long Grind | Tollbreaker | C | | Grinding Toll spins 2.5 s instead of 2 s | 2.8 s | works | new: `TollSpinTicks` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB05 | Quick Catch | Tollbreaker | C | | Returning axes fly 20% faster (18 → 21.6 m/s) | +30% | works | new: `ReturnSpeed` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB06 | Heavy Haft | Tollbreaker | C | | The held (lodging) Twin Hurl throw +20% damage (30 → 36) | +30% | works | built: damage hook | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB07 | Iron Pull | Tollbreaker | C | | Call of Iron +1.6 per metre (8 → 9.6), cap 110 → 125 per axe | +2.4 per metre, cap 132 | works | new: `CallPerMetre`, `CallMax` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB08 | Battle Spin | Tollbreaker | C | | After a dash, the next Twin Hurl tap hits like a held throw (30) without lodging | that hit also +15% | works | new: dash-end event | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB09 | Close Quarters | Tollbreaker | C | | Twin Hurl hits +12% while you're within 4 m of the boss | +18% | works | built: damage hook; new: distance to boss | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB10 | Deep Split | Tollbreaker | R | | Splitting Throw's bonus lasts 8 s instead of 6 (the axe stays lodged to match) | 9 s | works | new: `SplitTicks`, `BossLodgeTicks` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB11 | Twin Split | Tollbreaker | R | | With both axes in hand, Splitting Throw throws both; the second lodges beside the first for 50%. The bonus doesn't stack, and both hands are empty until Call of Iron | second 75% | works | new: two-axe split | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB12 | Quick Grind | Tollbreaker | R | | Grinding Toll hits every 0.3 s instead of 0.4 s | 0.27 s | works | new: `TollEvery` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB13 | Ringing Return | Tollbreaker | R | | Each axe Call of Iron sends through the boss takes 2 s off Splitting Throw's cooldown | 3 s | works | new: recall-hit event, cooldown refund | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB14 | Backswing | Tollbreaker | R | | Returning Twin Hurl axes hit 6 instead of 3 | 7 | works | new: `TapBack` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01, rebased to the 3-damage return the same day | accepted |
| TB15 | Anchor Line | Tollbreaker | R | | Floor-lodged axes last 12 s instead of 8; Call of Iron from the floor +20% | +30% | works | new: `FloorLodgeTicks`; built: damage hook | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB16 | Berserker's Pace | Tollbreaker | R | | Each Twin Hurl hit from within 4 m adds +2% damage for 3 s, up to +10%. Lost when you're hit | +3% each, up to +15% | works | new: stack counter, distance to boss; built: on-hit reset | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB17 | Riven Iron | Tollbreaker | R | | An axe Call of Iron sends through the boss cracks it for 4 s: your own Splitting Throw and Call of Iron hits on it +15%. Personal only | +22.5% | works | built: Crack state (personal); new: recall trigger | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB18 | War Cry | Tollbreaker | R | Party | Bell Frenzy gives allies within 6 m +10% damage for its 4 s | +15% | gives it to you | built: ally range, damage buff | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB19 | Frenzied Hour | Tollbreaker | K | | Bell Frenzy cooldown 30 → 18 s, but it lasts 3 s instead of 4 and the fan's side axes deal 35% | cooldown 15 s | works | new: `FrenzyCd`, `FrenzyTicks`, `FanSide` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| TB20 | Rooted Axe | Tollbreaker | K | | Splitting Throw's axe stays in the boss until Call of Iron, and the split lasts as long, but the bonus is +25% and Call of Iron's cooldown is 9 s | bonus +30% | works | new: no boss timeout while split, `SplitBonus`, `CallCd` | **built (Azure PR 14, 523232d)**; accepted 2026-11-01 | accepted |
| C01 | Bright Note | Chimesinger | C | | Resonant Note +10% damage, +15% projectile speed | +15% / +22% | works | built: damage hook; new: note speed | **built (96624b2, local, untested)** | accepted |
| C02 | Heavy Bell | Chimesinger | C | | Bellstrike +20% damage | +30% | works | built: damage hook | **built (PR 10, not merged)** | accepted |
| C03 | Wide Refrain | Chimesinger | C | | Harmonic Refrain cone +25% angle | +37% | works | new: Refrain angle | **built (96624b2, local, untested)** | accepted |
| C04 | Long Refrain | Chimesinger | C | | Harmonic Refrain range +2 m | +3 m | works | new: Refrain range | **built (96624b2, local, untested)** | accepted |
| C05 | Swift Chorus | Chimesinger | C | | Mending Chorus cooldown −15% | −22.5% | works | built: Chorus cooldown | **built (PR 10, not merged)** | accepted |
| C06 | Wide Chorus | Chimesinger | C | | Mending Chorus radius +30% | +45% | works | new: Chorus radius | **built (96624b2, local, untested)** | accepted |
| C07 | Light Step | Chimesinger | C | | Harmonic Step costs 24 stamina instead of 30 | 21 | works | new: dash stamina cost | **built (96624b2, local, untested)** | accepted |
| C08 | Grace Note | Chimesinger | C | | After Harmonic Step, the next Resonant Note deals double damage | +150% | works | new: dash-end event in ChimesingerKit | **built (96624b2, local, untested)** | accepted |
| C09 | Chord | Chimesinger | R | | Every 4th Resonant Note splits into 3 aimed notes in a narrow spread (no homing) | every 3rd | works | new: note split | **built (96624b2, local, untested)** | accepted |
| C11 | Third Echo | Chimesinger | R | | Echo cap 2 → 3 | — | works | new: echo cap | **built (96624b2, local, untested)** | accepted |
| C12 | Fortissimo | Chimesinger | R | | Crescendo +12% damage per echo it detonates | +18% per echo | works | new: echo count at detonation | **built (96624b2, local, untested)** | accepted |
| C13 | Encore | Chimesinger | R | | Crescendo leaves 1 echo on the bell | — | works | new: post-Crescendo echo | **built (96624b2, local, untested)** | accepted |
| C14 | Note Trail | Chimesinger | R | | Harmonic Step ends by firing 3 notes toward the cursor | 4 notes | works | new: dash-end event, note burst | **built (96624b2, local, untested)** | accepted |
| C15 | Overflow | Chimesinger | R | | Chorus overheal adds to the next Crescendo: +1% per 2 HP, up to +25% | up to +37% | works (self-heal overheal) | new: overheal amount from Chorus | **built (96624b2, local, untested)** | accepted |
| C16 | Shared Ringing | Chimesinger | R | Ring | Every 5th ally hit on a ringing target adds an echo (cap applies) | every 4th | counts your Resonant Notes | new: hits-on-ringing-target counter | **built (96624b2, local, untested)** | accepted (redrawn 2026-10-31) |
| C17 | Struck Together | Chimesinger | R | Ring | An ally's heavy hit on a ringing target rings the bell once for 30% of Bellstrike | 45% | every 6th Resonant Note does it | new: heavy-hit event on ringing target | **built (96624b2, local, untested)** | accepted |
| C18 | Chorus of Valour | Chimesinger | R | Party | Allies your Chorus touches get +12% damage for 4 s | +18% | gives it to you | built | **built (PR 10, not merged)** | accepted |
| C19 | Great Bell | Chimesinger | K | | Only one bell, twice as large, echo cap 4, Crescendo cooldown +30% | cooldown penalty +15% | works | new: bell count, size, echo cap, Crescendo cooldown | **built (96624b2, local, untested)** | accepted |
| C20 | Choir Leader | Chimesinger | K | | Chorus heals 25% less, but each cast detonates bells as a half-strength Crescendo | 75% strength | works | new: Chorus → Crescendo trigger | **built (96624b2, local, untested)** | accepted |
| A01 | Quick Carving | Arcanist | C | | Disintegrate builds glyphs 20% faster | 30% | works | new: glyph build rate | **built (96624b2, local, untested)** | accepted |
| A02 | Wide Beam | Arcanist | C | | Disintegrate width +30% | +45% | works | new: beam width | **built (96624b2, local, untested)** | accepted |
| A03 | Steady Hand | Arcanist | C | | Disintegrate turns 25% faster while steered | 37% | works | new: beam turn rate | **built (96624b2, local, untested)** | accepted |
| A04 | Split Missile | Arcanist | C | | A full charge fires 4 missiles instead of 3 | — | works | new: missile charge cap | **built (96624b2, local, untested)** | accepted |
| A05 | Wide Sigil | Arcanist | C | | Rune Sigil radius +25% (rune outline, no filled disc) | +37% | works | new: Sigil radius | **built (96624b2, local, untested)** | accepted |
| A06 | Unbound Cast | Arcanist | C | | Sigil and Comet without a glyph deal 50% instead of 35% | 57.5% | works | built | **built (PR 10, not merged)** | accepted |
| A07 | Blink Charge | Arcanist | C | | After Blink, the next Magic Missile is fully charged | that volley +15% damage | works | new: dash-end event in ArcanistKit | **built (96624b2, local, untested)** | accepted |
| A08 | Far Blink | Arcanist | C | | Blink distance +0.6 m. No iframes | +0.9 m | works | new: dash distance | **built (96624b2, local, untested)** | accepted |
| A09 | Glass Edge | Arcanist | C | | +8% damage, −8% max HP *(balance pass 2026-11-02, was +12%)* | +12% damage (HP loss unchanged) | works | built: damage hook; new: max HP stat | **built (96624b2, local, untested)** | accepted |
| A10 | Fourth Glyph | Arcanist | R | | Glyph cap 3 → 4 | — | works | built: glyph cap | **built (96624b2, local, untested)** | accepted |
| A11 | Missile Volley | Arcanist | R | | Three quick taps fire a free 4th missile | free missile +50% damage | works | new: tap counter | **built (96624b2, local, untested)** | accepted |
| A12 | Twin Sigil | Arcanist | R | | A Sigil cast with a glyph places two sigils in a short line | — | works | new: Sigil count | **built (96624b2, local, untested)** | accepted |
| A13 | Hasty Comet | Arcanist | R | | Comet delay 3 → 2.2 s, −15% damage | −7.5% damage | works | new: Comet delay | **built (96624b2, local, untested)** | accepted |
| A14 | Great Comet | Arcanist | R | | Comet delay 3 → 3.8 s, +40% damage, larger impact | +60% damage | works | new: Comet delay, radius | **built (96624b2, local, untested)** | accepted |
| A15 | Starfall | Arcanist | R | | Comet lands as 3 impacts in a line along your aim, each 40% of a Comet *(per-impact number is new here)* | each 50% | works | new: Comet impact pattern | **built (96624b2, local, untested)** | accepted |
| A16 | Long Surge | Arcanist | R | | Surge 2.5 s per glyph instead of 2 s | 2.75 s | works | built | **built (PR 10, not merged)** | accepted |
| A17 | Surge Echo | Arcanist | R | | When Surge ends, get 1 glyph back | — | works | new: Surge-end event | **built (96624b2, local, untested)** | accepted |
| A18 | Glyph Brand | Arcanist | R | Glyph | Comet brands the boss 5 s; every 8 ally hits on it carve you 1 glyph | every 6 hits | counts your missiles | new: boss brand state (like Crack), hit counter | **built (96624b2, local, untested)** | accepted |
| A19 | Resonant Rune | Arcanist | R | Ring | A Sigil going off on a ringing target adds an echo to the bell | — | gated: Chimesinger in party (final, balance pass 2026-11-02) | new: Sigil-on-ringing-target event | **built (96624b2, local, untested)** | accepted |
| A20 | Perpetual Beam | Arcanist | K | | Glyph cap 2; Sigil and Comet without a glyph deal 70% | 87.5% | works | built | **built (PR 10, not merged)** | accepted |
| G01 | Deep Breath | general | C | | Stamina regen starts 0.6 s after use instead of 0.75 s | 0.53 s | works | built | **built (PR 10, not merged)** | accepted |
| G02 | Long Lungs | general | C | | +15 max stamina | +22 | works | new: max stamina stat | **built (96624b2, local, untested)** | accepted (redrawn 2026-10-31) |
| G03 | Sprinter | general | C | | Sprint drains 21/s instead of 25/s | 19/s | works | built | **built (PR 10, not merged)** | accepted |
| G04 | Sure Footing | general | C | | Knockback taken −30%. Stun on hit stays | — (already at the −30% cap) | works | new: knockback scale + cap clamp | **built (96624b2, local, untested)** | accepted |
| G05 | Strong Brew | general | C | | Potions heal 48 instead of 40. Carry-over unchanged | 52 | works | built | **built (PR 10, not merged)** | accepted |
| G06 | Spare Flask | general | R | | +1 potion at each refill. Carry-over unchanged | — | works | built | **built (PR 10, not merged)** | accepted |
| G07 | Lone Bell | general | R | | +12% damage while no ally is within 8 m *(balance pass 2026-11-02, was +15%)* | +18% | works (always active solo) | built | **built (PR 10, not merged)** | accepted |
| G08 | Formation | general | R | Party | +6% damage per ally within 5 m, up to +18% | +9% per ally, up to +27% | flat +6% (+9%) | built: ally range, damage hook | **built (96624b2, local, untested)** | accepted |
| G09 | Fourth Choice | general | R | | **Shop version (final, balance pass 2026-11-02):** your stock has 7 slots instead of 6 for the rest of the run (+1 class slot) | — | works | new: stock size in ShopRun | **built (96624b2, local, untested)** | accepted |
| G10 | Light Feet | general | C | | +10% move speed for 1 s after a dash | +15% | works | new: move speed buff | **built (96624b2, local, untested)** | accepted |
| T01 | Overchannel | Arcanist | K | Toll IV | Each Disintegrate step that hits carves 2 glyphs instead of 1, but you lose 1 glyph every 3 s while you're not channelling | lose 1 every 4 s | works | new: glyph carve count, glyph fade timer | **accepted (2026-11-05), tune after playtest** | none |
| T02 | Leaden Step | general | K | Toll IV | +15% damage, but each dash costs 45 stamina instead of 30. Dash distance and speed are unchanged | dash costs 40 | works | built: damage hook; new: dash stamina cost | **accepted (2026-11-05), tune after playtest** | none |
| T03 | Double Toll | Chimesinger | R | Toll IV | Bellstrike tolls again 1 s later for 40% damage, but its cooldown is 20% longer | second toll 60% | works | new: delayed repeat, Bellstrike cooldown | **accepted (2026-11-05), tune after playtest** | none |
| T04 | Short Split | Tollbreaker | R | Toll IV | Splitting Throw's bonus is +55% instead of +40%, but it lasts 4 s instead of 6 | +60% | works | built: `t.split.bonus`, `t.split.ticks`, `t.lodge.boss` | **accepted (2026-11-05), tune after playtest** | none |
| T05 | Steady Breath | general | C | Toll IV | Stamina regenerates 15% faster, but sprinting drains 28/s instead of 25/s | +22% | works | built: sprint drain; new: stamina regen rate | **accepted (2026-11-05), tune after playtest** | none |
| T06 | Lingering Rune | Arcanist | R | Toll VII | Rune Sigil goes off after 1.2 s instead of 0.8 s, and its rune stays 2 s more, hitting again for 40% if the boss is still inside (outline only) | second hit 60% | works | new: Sigil delay, lingering second hit | **accepted (2026-11-05), tune after playtest** | none |
| T07 | Patient Bell | general | K | Toll VII | Your first hit on the boss after 2 s without hitting it deals +60%; all your other hits deal −10% | first hit +80% | works | built: damage hook; new: time since your last boss hit | **accepted (2026-11-05), tune after playtest** | none |
| T08 | Low Note | Chimesinger | C | Toll VII | Resonant Note +12% damage, but its notes fly 15% slower | +18% | works | new: note projectile speed | **accepted (2026-11-05), tune after playtest** | none |
| T09 | Long Toss | Tollbreaker | C | Toll VII | The held Twin Hurl throw lodges up to 12 m away instead of 9, but hits 26 instead of 30 | hits 28 | works | new: held-throw range; built: damage hook (held throw) | **accepted (2026-11-05), tune after playtest** | none |
| T10 | Late Bloom | general | R | Toll VII | −6% damage in a fight's first phase, +10% in every later phase | +15% in later phases | works | built: damage hook; new: boss phase number | **accepted (2026-11-05), tune after playtest** | none |

**Toll items: look notes for the icons** (accepted icon style and rarity frames, class accent or neutral brass for general items, no coral, amber or danger gold):

| # | Look |
|---|---|
| T01 Overchannel | a beam splitting into two rune glyphs, with a third glyph crumbling away at the edge |
| T02 Leaden Step | a boot with an iron bell-weight chained to the heel |
| T03 Double Toll | a bell with two ripple rings, the outer one fainter |
| T04 Short Split | an axe biting deep into a cracked bell plate, with a small half-empty hourglass |
| T05 Steady Breath | a bellows breathing out an even, slow wave line |
| T06 Lingering Rune | a rune circle outline with a second, ticking ring around it |
| T07 Patient Bell | a hammer held still above a bell, with a small pause mark |
| T08 Low Note | one thick, heavy note with a long trailing tail |
| T09 Long Toss | an axe at the end of a long dotted line, stuck in a floor crack |
| T10 Late Bloom | a bell-flower bud opening on a clock hand |

**Count:** 69 live items (20 Tollbreaker, 19 Chimesinger, 20 Arcanist, 10 general), plus 10 accepted Toll items (T01–T10): 5 join the pools at Toll IV and 5 more at Toll VII.
- Built: 11 slice items (PR 10, not merged) and 38 pool items (local, untested).
- Built: the 20 Tollbreaker items (Azure PR 14, 523232d, not merged).

## Build notes

**Numbers the shop lane picked** where the catalogue gave none (`96624b2`, 2026-10-28). These are build values that playtests can tune; they aren't design changes.

| Item | Build value |
|---|---|
| Great Comet (A14) | "larger impact" = radius ×1.25 |
| Starfall (A15) | impacts 3 m apart along the aim |
| Twin Sigil (A12) | second sigil 2.5 m further along the aim |
| Encore (C13) | the kept echo is worth max(the first echo, one Refrain echo) |
| Shared Ringing (C16), Resonant Rune (A19) | each added echo is worth one Refrain echo at the bell's strength |
| Chord (C09) | side notes at ±7°, at base note damage |
| Note Trail (C14) | notes at base damage, 6° apart |
| Grace Note (C08), Blink Charge (A07) | armed by any dash, used by the next attack |
| Light Feet (G10) | lasts 1 s from the end of the dash |

**Tollbreaker build choices** (Tollbreaker lane, Azure PR 14, `523232d`, 2026-11-01). These are build values that playtests can tune; they aren't design changes.

| Item | Build choice |
|---|---|
| Close Quarters (TB09), Berserker's Pace (TB16) | "within 4 m" is his distance to the boss when he releases the throw |
| Keen Edge, Close Quarters, Berserker's Pace (TB01, TB09, TB16) | Bell Frenzy's side fan axes get none of these bonuses (Keen Edge also skips return hits) |
| War Cry (TB18) | in a party, the buff skips the Tollbreaker himself; solo, it goes to him |
| Struck Together (C17) | its "heavy hit" list includes Splitting Throw and the held Twin Hurl |

## Rule check

Every row was checked against the rules:
- **No invincibility.** No item makes a dash, dodge or Blink immune, and none gives immunity after a hit. Far Blink and Light Feet only change distance, count or speed.
- **Soloable, nothing required.** Every combo item has a solo fallback, or is gated out when solo (Resonant Rune). No encounter or class needs an item.
- **Potions (§3).** Nothing refills potions mid-fight or between minibosses. Strong Brew and Spare Flask change heal size and the refill count only.
- **Defensive caps.**
  - Max HP: no item gives max HP. The +20% clamp stays in code for future items.
  - Knockback: Sure Footing sits at the −30% cap, so it has no Temper, and no other item may reduce knockback.
  - The Tollbreaker has no defensive ability, so none of his items gives max HP, damage reduction or a shield.
- **Aiming.** Chord, Note Trail and Starfall fire along the aim. Nothing homes in or locks on.
- **Colours.** Item-added visuals never use coral or amber. Tollbreaker items reuse his kit look: thin dotted cyan, never filled. Arcanist items that resize his spells keep his own spell shades and his no-filled-disc rule. No item adds violet to anything that could read as a boss warning.
- **Dropped items** (loot doc §1.9) stay dropped: Phase Step, Recovery Grace, Iron Wall, Riposte, homing shots, an Arcanist ward, Second Wind, lifesteal, Fresh Flask and Recall.

## Balance pass (accepted by Chzy18, 2026-11-02)

Six decisions, accepted as one block (`docs/proposals/item-balance-pass-2026-11-02.md`):
- **Glass Edge** (A09): +8% damage (Tempered +12%) with −8% max HP, down from +12%.
- **Lone Bell** (G07): +12% (Tempered +18%), down from +15%, because it is always on when solo.
- **Formation** (G08): unchanged. It is conditional, and with at most 4 players its 3-ally cap fits exactly.
- **Fourth Choice** (G09): the shop version (+1 stock slot) is final.
- **Resonant Rune** (A19): stays gated on a Chimesinger in the party. The loot doc's solo fallback is dropped.
- **Toll IV and VII items**: aimed at the thin slots (the Arcanist had 1 Keystone and the general pool none). Chzy18 asked on 5 Nov for them to be drafted now: see `docs/proposals/toll-items-2026-11-05.md`.

## Open questions

None right now. The Toll items T01–T10 were accepted on 2026-11-05; their numbers get tuned after the playtest.

*Settled 2026-10-28:* the Temper values were accepted as one block, along with Starfall's per-impact damage and the Tempers for yes/no items.
