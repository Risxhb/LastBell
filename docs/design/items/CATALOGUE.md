# Item catalogue

**Status:** living catalogue, owned by the Items session. 69 named items are **accepted**: the 15 slice-1 items through the shop spec and build plan, and 54 more, with this catalogue's Temper values, through the review inbox (Chzy18, 2026-10-28; the Second Step rewrite 2026-10-29). Twin Bells was cut on 2026-10-29. Only the 10 Toll slots are still proposals. New rows start as proposals. Written 2026-10-28.

**Sources:** `AGENTS.md`; `gpt/08-design-rules.md` (§0.3, §1, §2, §3, §4); `docs/proposals/meta-shop-spec-2026-10-29.md` (final, §12 answers); `docs/proposals/meta-loot-and-toll-2026-10-29.md` §1.4–1.9 (the 70-item draft pool and the dropped list); `docs/design/run-shop-plan.md` §3 (item framework, slice 1); `unity/Assets/LastBell/Run/Shop/ItemCatalog.cs` (built values).

**Where things stand:**
- Slice 1 is built and on Azure as [PR 10](https://dev.azure.com/ChzandZum/_git/Last%20Bell/pullrequest/10) (`feature/run-shop`), which isn't merged yet. The shop autotest passes 71/71.
- **Icons: all 69 accepted (Chzy18, review inbox, 2026-10-29)** as the item-art direction. The slice-1 icons are in `work/shop-art-20261029/icons/` and the other 54 in `work/item-icons-20261030/icons/` (64 and 128 px). Long Reach (V04), Shared Ringing (C16) and Long Lungs (G02) must be redrawn before they go into the game (Codex task 1 in `CODEX-TASKS-2026-10-31.md`, output in `work/item-icons-20261031/`).
- The other 54 are accepted. 53 are built on local `feature/run-shop` (`96624b2`), not yet built in Unity or tested. The Second Step rewrite (accepted 2026-10-29) is built in `1d5364e`, also untested; shop build 4 is waiting for the Unity lock. Twin Bells was cut. Their cards show text only until the icons are wired in.

## How to read the table

- **Tier:** C = Common (90 Chimes, budget +5), R = Rare (150, +8), K = Keystone (240, +12 net, at most 2 per player per run, Shop 2 on).
- **Tempered (+):** the item after a Temper (110 Chimes, once per item). The rule is +50% of the effect. Where the effect is a yes/no or a whole number, the + version is written out. "—" means it can't be tempered.
- **Solo:** "works" means the item needs nothing from allies. A combo item states its fallback. "Gated" means the shop only offers it when the named class is in the party (spec §3; party bots count).
- **Hooks:** the code it needs. "built" means slice 1 already has it. "new: …" names the hook to add. Hooks inside a class kit go to that kit's owner as a request (Characters lanes, Risx for the Arcanist).
- **Icon:** accepted = approved icon, ready to wire in; redraw needed = accepted direction, but this icon must be redrawn first; none = not made yet.

## The catalogue

| # | Item | Class | Tier | Tags | Effect | Tempered (+) | Solo | Hooks | Status | Icon |
|---|---|---|---|---|---|---|---|---|---|---|
| V01 | Tempered Edge | Vanguard | C | | Steel Rhythm hit 3 +20% damage | +30% | works | built: damage hook | **built (PR 10, not merged)** | accepted |
| V02 | Quick Hands | Vanguard | C | | Combo window between Steel Rhythm hits +0.3 s | +0.45 s | works | new: combo window in VanguardKit2 | **built (96624b2, local, untested)** | accepted |
| V03 | Wide Arc | Vanguard | C | | Hit 3 cleaves a 30% wider arc | 45% wider | works | new: hit-3 arc number | **built (96624b2, local, untested)** | accepted |
| V04 | Long Reach | Vanguard | C | | Sundering Blow range +1.5 m | +2.25 m | works | new: Sunder range number | **built (96624b2, local, untested)** | accepted, **redraw needed** |
| V05 | Countdown | Vanguard | C | | Last Word cooldown −20% | −30% | works | built: Last Word cooldown | **built (PR 10, not merged)** | accepted |
| V06 | Grit | Vanguard | C | | +8% max HP (counts toward the +20% cap) | +12% | works | new: max HP stat + cap clamp | **built (96624b2, local, untested)** | accepted |
| V07 | Clockwork Step | Vanguard | C | | Clockstep distance 3 → 3.6 m. No iframes | 3.9 m | works | new: dash distance | **built (96624b2, local, untested)** | accepted |
| V08 | Steady Stance | Vanguard | C | | Brace's movement slow is halved. Mitigation unchanged | slow cut by 75% | works | new: Brace move slow | **built (96624b2, local, untested)** | accepted |
| V09 | Momentum | Vanguard | C | | After Clockstep, the next Steel Rhythm hit counts as hit 3 | that hit also +15% damage | works | new: dash-end event in VanguardKit2 | **built (96624b2, local, untested)** | accepted |
| V10 | Double Opening | Vanguard | R | | Opening stacks to 2. Sundering Blow spends both for a wider split impact | double-spent Sunder +15% damage | works | new: Opening stack cap, Sunder spend | **built (96624b2, local, untested)** | accepted |
| V11 | Pendulum Cut | Vanguard | R | | Sundering Blow's line keeps cutting for 1 s | 1.5 s | works | new: lingering Sunder hazard (friendly, kit look) | **built (96624b2, local, untested)** | accepted |
| V12 | Heavy Hour | Vanguard | R | | Last Word windup 0.8 → 1.1 s, +45% damage, wider fan | +67% damage (windup unchanged) | works | new: Last Word windup, fan width | **built (96624b2, local, untested)** | accepted |
| V13 | Quick Verdict | Vanguard | R | | Last Word windup 0.8 → 0.5 s, −20% damage | −10% damage | works | new: Last Word windup | **built (96624b2, local, untested)** | accepted |
| V14 | Iron Echo | Vanguard | R | | Last Word's fan repeats 0.6 s later for 50% damage | 75% | works | new: delayed repeat of an ability hit | **built (96624b2, local, untested)** | accepted |
| V15 | Second Step | Vanguard | R | | A dash within 1.5 s of another dash costs 20 stamina instead of 30. No iframes *(rewrite accepted 2026-10-29)* | 15 stamina | works | new: dash stamina cost, time since last dash | **built (1d5364e, local, untested)** | accepted |
| V16 | Rhythm Keeper | Vanguard | R | | 3 full Steel Rhythm combos in a row without being hit: +10% damage until hit | +15% | works | new: combo counter; built: on-hit reset, damage hook | **built (96624b2, local, untested)** | accepted |
| V17 | Cracked Armour | Vanguard | R | Crack | Sundering Blow cracks the boss 4 s; heavy hits on it +15% (Last Word, Bellstrike, Crescendo, Rune Sigil, Arcane Comet) | +22.5% | your own Last Word | built | **built (PR 10, not merged)** | accepted |
| V18 | Rally | Vanguard | R | Party | A Last Word hit gives allies within 6 m +10% damage for 4 s | +15% | gives it to you | built: ally range, damage buff (from slice 1) | **built (96624b2, local, untested)** | accepted |
| V19 | Toll of Steel | Vanguard | K | | Steel Rhythm becomes 4 hits; hit 4 sends a small Last Word fan (40%). Opening comes from hit 4 | fan 60% | works | new: combo length, Opening source | **built (96624b2, local, untested)** | accepted |
| V20 | Oathbound | Vanguard | K | | Sundering Blow can't be empowered; every 3rd Opening resets Last Word | every 2nd Opening | works | built | **built (PR 10, not merged)** | accepted |
| C01 | Bright Note | Chimesinger | C | | Resonant Note +10% damage, +15% projectile speed | +15% / +22% | works | built: damage hook; new: note speed | **built (96624b2, local, untested)** | accepted |
| C02 | Heavy Bell | Chimesinger | C | | Bellstrike +20% damage | +30% | works | built: damage hook | **built (PR 10, not merged)** | accepted |
| C03 | Wide Refrain | Chimesinger | C | | Harmonic Refrain cone +25% angle | +37% | works | new: Refrain angle | **built (96624b2, local, untested)** | accepted |
| C04 | Long Refrain | Chimesinger | C | | Harmonic Refrain range +2 m | +3 m | works | new: Refrain range | **built (96624b2, local, untested)** | accepted |
| C05 | Swift Chorus | Chimesinger | C | | Mending Chorus cooldown −15% | −22.5% | works | built: Chorus cooldown | **built (PR 10, not merged)** | accepted |
| C06 | Wide Chorus | Chimesinger | C | | Mending Chorus radius +30% | +45% | works | new: Chorus radius | **built (96624b2, local, untested)** | accepted |
| C07 | Light Step | Chimesinger | C | | Harmonic Step costs 24 stamina instead of 30 | 21 | works | new: dash stamina cost | **built (96624b2, local, untested)** | accepted |
| C08 | Grace Note | Chimesinger | C | | After Harmonic Step, the next Resonant Note deals double damage | +150% | works | new: dash-end event in ChimesingerKit | **built (96624b2, local, untested)** | accepted |
| C09 | Chord | Chimesinger | R | | Every 4th Resonant Note splits into 3 aimed notes in a narrow spread (no homing) | every 3rd | works | new: note split | **built (96624b2, local, untested)** | accepted |
| C10 | ~~Twin Bells~~ | Chimesinger | R | | ~~Two bells can ring at once (boss and a spawned part)~~ | — | — | — | **cut (Chzy18, 2026-10-29)** | — |
| C11 | Third Echo | Chimesinger | R | | Echo cap 2 → 3 | — | works | new: echo cap | **built (96624b2, local, untested)** | accepted |
| C12 | Fortissimo | Chimesinger | R | | Crescendo +12% damage per echo it detonates | +18% per echo | works | new: echo count at detonation | **built (96624b2, local, untested)** | accepted |
| C13 | Encore | Chimesinger | R | | Crescendo leaves 1 echo on the bell | — | works | new: post-Crescendo echo | **built (96624b2, local, untested)** | accepted |
| C14 | Note Trail | Chimesinger | R | | Harmonic Step ends by firing 3 notes toward the cursor | 4 notes | works | new: dash-end event, note burst | **built (96624b2, local, untested)** | accepted |
| C15 | Overflow | Chimesinger | R | | Chorus overheal adds to the next Crescendo: +1% per 2 HP, up to +25% | up to +37% | works (self-heal overheal) | new: overheal amount from Chorus | **built (96624b2, local, untested)** | accepted |
| C16 | Shared Ringing | Chimesinger | R | Ring | Every 5th ally hit on a ringing target adds an echo (cap applies) | every 4th | counts your Resonant Notes | new: hits-on-ringing-target counter | **built (96624b2, local, untested)** | accepted, **redraw needed** |
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
| A09 | Glass Edge | Arcanist | C | | +12% damage, −8% max HP | +18% damage (HP loss unchanged) | works | built: damage hook; new: max HP stat | **built (96624b2, local, untested)** | accepted |
| A10 | Fourth Glyph | Arcanist | R | | Glyph cap 3 → 4 | — | works | built: glyph cap | **built (96624b2, local, untested)** | accepted |
| A11 | Missile Volley | Arcanist | R | | Three quick taps fire a free 4th missile | free missile +50% damage | works | new: tap counter | **built (96624b2, local, untested)** | accepted |
| A12 | Twin Sigil | Arcanist | R | | A Sigil cast with a glyph places two sigils in a short line | — | works | new: Sigil count | **built (96624b2, local, untested)** | accepted |
| A13 | Hasty Comet | Arcanist | R | | Comet delay 3 → 2.2 s, −15% damage | −7.5% damage | works | new: Comet delay | **built (96624b2, local, untested)** | accepted |
| A14 | Great Comet | Arcanist | R | | Comet delay 3 → 3.8 s, +40% damage, larger impact | +60% damage | works | new: Comet delay, radius | **built (96624b2, local, untested)** | accepted |
| A15 | Starfall | Arcanist | R | | Comet lands as 3 impacts in a line along your aim, each 40% of a Comet *(per-impact number is new here)* | each 50% | works | new: Comet impact pattern | **built (96624b2, local, untested)** | accepted |
| A16 | Long Surge | Arcanist | R | | Surge 2.5 s per glyph instead of 2 s | 2.75 s | works | built | **built (PR 10, not merged)** | accepted |
| A17 | Surge Echo | Arcanist | R | | When Surge ends, get 1 glyph back | — | works | new: Surge-end event | **built (96624b2, local, untested)** | accepted |
| A18 | Glyph Brand | Arcanist | R | Glyph | Comet brands the boss 5 s; every 8 ally hits on it carve you 1 glyph | every 6 hits | counts your missiles | new: boss brand state (like Crack), hit counter | **built (96624b2, local, untested)** | accepted |
| A19 | Resonant Rune | Arcanist | R | Ring | A Sigil going off on a ringing target adds an echo to the bell | — | gated: Chimesinger in party (see open question 1) | new: Sigil-on-ringing-target event | **built (96624b2, local, untested)** | accepted |
| A20 | Perpetual Beam | Arcanist | K | | Glyph cap 2; Sigil and Comet without a glyph deal 70% | 87.5% | works | built | **built (PR 10, not merged)** | accepted |
| G01 | Deep Breath | general | C | | Stamina regen starts 0.6 s after use instead of 0.75 s | 0.53 s | works | built | **built (PR 10, not merged)** | accepted |
| G02 | Long Lungs | general | C | | +15 max stamina | +22 | works | new: max stamina stat | **built (96624b2, local, untested)** | accepted, **redraw needed** |
| G03 | Sprinter | general | C | | Sprint drains 21/s instead of 25/s | 19/s | works | built | **built (PR 10, not merged)** | accepted |
| G04 | Sure Footing | general | C | | Knockback taken −30%. Stun on hit stays | — (already at the −30% cap) | works | new: knockback scale + cap clamp | **built (96624b2, local, untested)** | accepted |
| G05 | Strong Brew | general | C | | Potions heal 48 instead of 40. Carry-over unchanged | 52 | works | built | **built (PR 10, not merged)** | accepted |
| G06 | Spare Flask | general | R | | +1 potion at each refill. Carry-over unchanged | — | works | built | **built (PR 10, not merged)** | accepted |
| G07 | Lone Bell | general | R | | +15% damage while no ally is within 8 m | +22.5% | works (always active solo) | built | **built (PR 10, not merged)** | accepted |
| G08 | Formation | general | R | Party | +6% damage per ally within 5 m, up to +18% | +9% per ally, up to +27% | flat +6% (+9%) | built: ally range, damage hook | **built (96624b2, local, untested)** | accepted |
| G09 | Fourth Choice | general | R | | **Shop version (new):** your stock has 7 slots instead of 6 for the rest of the run (+1 class slot) | — | works | new: stock size in ShopRun | **built (96624b2, local, untested)** | accepted |
| G10 | Light Feet | general | C | | +10% move speed for 1 s after a dash | +15% | works | new: move speed buff | **built (96624b2, local, untested)** | accepted |
| T01–T05 | *Toll IV items* | any | — | | 5 sidegrades, not named yet (Toll IV reward, spec §10) | | | | proposal (unnamed) | none |
| T06–T10 | *Toll VII items* | any | — | | 5 more, not named yet (Toll VII reward) | | | | proposal (unnamed) | none |

**Count:** 69 named items (20 Vanguard, 19 Chimesinger, 20 Arcanist, 10 general) plus 10 Toll slots. 15 accepted and built (PR 10, not merged), 54 accepted and built locally (untested, `96624b2` + `1d5364e`), 1 cut (Twin Bells), 10 Toll slots still proposals.

## Build notes

**Numbers the shop lane picked** where the catalogue gave none (`96624b2`, 2026-10-28). These are build values that playtests can tune; they aren't design changes.

| Item | Build value |
|---|---|
| Pendulum Cut (V11) | 10 damage every 0.25 s along the original cut: 4 ticks (6 tempered) |
| Double Opening (V10) | "wider split impact" = Sunder arc ×1.6 |
| Heavy Hour (V12) | "wider fan" = Last Word shockwave width ×1.4 |
| Toll of Steel (V19) | hit-4 fan = a shockwave at 70% of Last Word's width |
| Great Comet (A14) | "larger impact" = radius ×1.25 |
| Starfall (A15) | impacts 3 m apart along the aim |
| Twin Sigil (A12) | second sigil 2.5 m further along the aim |
| Encore (C13) | the kept echo is worth max(the first echo, one Refrain echo) |
| Shared Ringing (C16), Resonant Rune (A19) | each added echo is worth one Refrain echo at the bell's strength |
| Chord (C09) | side notes at ±7°, at base note damage |
| Note Trail (C14) | notes at base damage, 6° apart |
| Momentum (V09), Grace Note (C08), Blink Charge (A07) | armed by any dash, used by the next attack |
| Light Feet (G10) | lasts 1 s from the end of the dash |

**Held items, resolved 2026-10-29:**
- **Second Step (V15):** the shared dash has no cooldown, only stamina, so the old "twice within 1.5 s, the second costs 45" would only make the second dash cost more. **Rewrite accepted (Chzy18, 2026-10-29):** a dash within 1.5 s of another dash costs 20 stamina instead of 30 (15 tempered). No iframes.
- **Twin Bells (C10):** no boss parts can be targeted yet. **Cut from the pool (Chzy18, 2026-10-29):** the hold was rejected.

## Rule check

Every row was checked against the rules:
- **No invincibility.** No item makes a dash, dodge or Blink immune, and none gives immunity after a hit. Clockwork Step, Second Step, Far Blink and Light Feet only change distance, count or speed.
- **Soloable, nothing required.** Every combo item has a solo fallback, or is gated out when solo (Resonant Rune). No encounter or class needs an item.
- **Potions (§3).** Nothing refills potions mid-fight or between minibosses. Strong Brew and Spare Flask change heal size and the refill count only.
- **Defensive caps.**
  - Max HP: Grit is +8% (+12% tempered). The code must clamp the total at +20%, so later Toll items can't push past it.
  - Knockback: Sure Footing sits at the −30% cap, so it has no Temper, and no other item may reduce knockback.
  - Brace: no item changes Brace mitigation or makes it work on Ground hazards. Steady Stance only changes the movement slow.
- **Aiming.** Chord, Note Trail and Starfall fire along the aim. Nothing homes in or locks on.
- **Colours.** Item-added visuals never use coral or amber. Pendulum Cut, Iron Echo and Toll of Steel reuse the Vanguard kit look. Arcanist items that resize his spells keep his own spell shades and his no-filled-disc rule. No item adds violet to anything that could read as a boss warning.
- **Dropped items** (loot doc §1.9) stay dropped: Phase Step, Recovery Grace, Iron Wall, Riposte, homing shots, an Arcanist ward, Second Wind, lifesteal, Fresh Flask and Recall.

## Balance notes (for a later balance pass)

- **Over budget on paper:**
  - Glass Edge, a Common, gives +12% damage against a +5 budget. Its −8% HP is the trade.
  - Formation+ reaches +27% and Lone Bell+ reaches +22.5%. Both are conditional.
- **Thin slots:**
  - The Arcanist has 1 Keystone; the Vanguard and Chimesinger have 2 each.
  - The general pool has no Keystone.
- **Fourth Choice** was written for pick offers. It's built as the shop version above (+1 stock slot) until the balance pass decides.
- **Resonant Rune** (A19): the loot doc gives it a fallback ("without a Chimesinger: Sigil +10%"). Spec §3 gates cross-class items out when that class isn't in the party, so the fallback would never show. It's built gated until the balance pass decides.

## Open questions

1. **For the balance pass** (Chzy18, 2026-10-28): Fourth Choice, the Resonant Rune gate, and the over-budget items listed above.
2. Should the **Toll IV and VII items** be named now or after playtests? (Loot doc open question 8.)

*Settled 2026-10-28:* the Temper values were accepted as one block, along with Starfall's per-impact damage and the Tempers for yes/no items.
