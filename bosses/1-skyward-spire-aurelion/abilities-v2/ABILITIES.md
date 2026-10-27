# Aurelion, the Meridian Regent: ability set v2 (proposal)

**Status: proposal.** This is designed and escape-checked, but not built and not approved. Nothing here is human-approved until Chzy18 plays it.

**Sources read:**
- `AGENTS.md` and `gpt/08-design-rules.md` (the settled rules, including the §5 melee-and-ranged rule)
- `research/rabbit-and-steel-boss-mechanics.md` §3.0, §4, §5 and §7
- `bosses/1-skyward-spire-aurelion/BOSS.md` and `01-aurelion-v1.png`
- `gpt/02-visual-style-guide.md`, `gpt/04-bosses.md` and `gpt/05-arenas.md`
- the current encounter: `Run/Encounters/AurelionEncounter.cs`, `RunAutoTest.Aurelion.cs` and `docs/bosses/aurelion.md` on `feature/boss-aurelion`
- `tools/encounter-escape/escape-check.ps1`, the method reused here

**What stays from the packet:**
- **Identity:** Aurelion, the Meridian Regent. A 4.2 m clockwork knight in ivory and gold, with a clock-hand glaive and a three-arc orrery halo.
- **Model:** the current rigged Meshy model.
- **Phase names:** Perfect Order 100–65 %, Misalignment 65–30 %, Exposed Meridian 30–0 %.

**What's new:** the ability set below replaces the packet's v1 set (Meridian Cut, Zenith Pins, Processional Thrust, Crown Reversal, Meridian Alignment). The v1 set stays reachable with `-lastbellAbilitySet v1`.

**Arena:** Skyward Spire, a round play bound of 13.9 m. The boss stays inside 9.5 m. His body radius is 1.1 m, the player radius is 0.45 m, and the hit margin is 0.3 m (`Hazard.Margin`).

**Escape check:** `tools/encounter-escape/aurelion-v2-escape-check.ps1`. The results are in `escape-check-medium.md` and `escape-check-easy.md`. Every case passes with **0.62–0.84 s** of margin at 4.2 m/s on Medium (summary in §6).

## 1. Design intent

The fight should feel like the sky keeping time on you. Every move is light falling on a schedule:
- meridian beams across the whole dais
- stars that mark you and your reflection
- noon sunlight across half the world
- the orrery's rings closing on the rim and on the regent himself

It is meant to be hard at every player count. Difficulty comes from learned sequences and short movement jobs, not from HP bloat or randomness.

**Ranged gets no free spot.**
- The beams always run through the **farthest** player.
- Noon Shadow is a 14 m run for a player at the far edge.
- Orrery Closing lights the whole rim, and from Phase 2 it keeps the rim lit for 4 s. Ranged players must come inside 8.2 m while melee get pushed out of the crown.

**Melee keeps close-range pressure.**
- Every beam crosses at his body, so no spot within about 3.7 m of him survives a 4-beam volley.
- The crown hits everything within 3.9 m of his centre.

## 2. The ability set (summary)

Medium values. Total warning = warning start to hit. Lock = when the aim freezes. The window is lock to hit, and it's what the escape check measures. The margin is that window minus the fastest route at 4.2 m/s.

| # | Ability (ID) | R&S block (§4) / proposal (§5) | Shape (tag) | Total warning / window | Damage | Melee threat | Ranged threat | Co-op |
|---|---|---|---|---|---|---|---|---|
| 1 | **Meridian Lances** (`aurelion.meridian_lances`) | Rays and lasers (14); cardinal lasers 2.0 / 4.0 s | 3 or 4 full-arena beams, 2.2 m wide, crossing at the boss (Strike) | 2.4 s; tracks 1.2 s, then 1.2 s window. Volley 2: 2.2 s | 35 | No safe spot within about 3.7 m of him (4 beams) | A beam always runs through the farthest player, edge to edge | — |
| 2 | **Starfall Mirror** (`aurelion.starfall_mirror`) | Spread (1) + player mirror (11); proposals 1 and 11; R&S `bp_playermirror_spread` | One 2.2 m star on each player (1.8 m at N ≥ 5), plus a mirror star at their reflection through the arena centre (Ground) | 3.1 s; follows 2.0 s, then 1.1 s window | 30 per star | Star on you wherever you are; partners' stars crowd the space round him | Same, and a ranged player's mirror lands on the opposite rim | **Yes** (solo rule below) |
| 3 | **Noon Shadow** (`aurelion.noon_shadow`) | One-side cleave (12); proposal 9; cleave 2.2 / 4.0 s | Half the arena, split by a line through the centre (Ground) | 3.8 s; locks at once | 45 | He usually stands near the centre: cross a few metres | Up to a 14 m run for a far-edge player: the main anti-range move with Orrery | **Yes** (majority side; solo rule below) |
| 4 | **Orrery Closing** (`aurelion.orrery_closing`) **signature** | Arena edge (17) + point-blank ring; field limit plus donut pairing | Rim ring from 8.5 m out past the bound, **plus** a 3.6 m crown circle on the boss (Ground). From Phase 2 the rim stays lit for 4 s (the **Horizon**) | 2.3 s; locks at once | 40 per ring | Crown: leave his body | Rim and Horizon: come inside 8.2 m and stay there | — (positional; spatial job at N = 8) |
| H | **Hour Hands** (`aurelion.hour_hands`), **Hard only** | Clock spots (13); proposal 6; clock spots 3.0 / 5.5 s | A clock-hand spoke from the boss through each player's hour, to the rim, 1.8 m wide (Strike) | 2.5 s; tracks 1.5 s, then 1.0 s window | 30 | All spokes start at him | Spokes reach the rim | **Yes** (solo rule below) |

**Rule checks:**
- **Melee and ranged:** all four core abilities threaten both. Abilities 3 and 4 punish staying at range. No spot is safe for a whole phase: the rim (4), the far side (1, 3) and behind him (1, 3) are all hit every loop.
- **Co-op:** abilities 2 and 3, plus the Hard mechanic. Each has a written solo rule.
- **Signature:** Orrery Closing. The arena's orrery rings trace its edges, and Phase 3's Grand Orrery and open-chest punish revolve around it.

## 3. Phase plan (Medium)

Times in seconds. Recovery gaps are short ("pressure never stops"). The only long gap is the **3.5 s open-chest punish** after each loop's Orrery Closing.

| Phase | Loop (in order) | Pairings (at most 2 damaging patterns) | Loop length |
|---|---|---|---|
| **Perfect Order** 100–65 % (form 1) | Lances (3 beams, 1 volley) → 1.0 → Starfall → 1.0 → Noon Shadow → 1.0 → Orrery Closing (no Horizon) → **punish 3.5** | none: every move is taught on its own | about 20 s |
| **Misalignment** 65–30 % (form 1, cracked shoulder) | Lances (4 beams, 2 volleys) → 0.6 → **Noon Shadow + Starfall** → 0.6 → Lances (4 beams, 1 volley) → 0.6 → Orrery Closing → **Horizon (4 s) + Starfall** → **punish 3.5** | Noon (which half) + Starfall (spread); Horizon (stay inside) + Starfall (spread) | about 26 s |
| **Exposed Meridian** 30–0 % (**second form**) | **Cascading Lances** (4 beams, volley 2 rotated 22.5°) → 0.6 → **Noon and Afternoon** (both halves in turn) → 0.6 → **Grand Orrery** (Orrery Closing + Starfall cast 0.8 s in) → Horizon (4 s; that Starfall resolves inside it) → **punish 3.5** | Orrery + Starfall; Horizon + Starfall | about 27 s |

**Hard** inserts Hour Hands into each Horizon (Phases 2 and 3). It warns when that Horizon's Starfall resolves, and the Horizon stretches to 6 s so it covers the spokes. That makes at most two patterns at once: Horizon + Starfall, then Horizon + Hour Hands.

**Transitions and the second form:**
- Phase changes queue until the current block ends. They play a harmless 1.5 s transformation and keep the same HP bar, with no heal. A burst through 65 % and 30 % plays one transition.
- **The second form starts at Exposed Meridian (30 %).** The chest shutters open, the halo arcs spread wide, and the pendulum glows. The moves upgrade to their Phase 3 versions.
- The boss stays targetable throughout, and player setup (seals, bells) persists.
- The open-chest punish keeps the v1 look: the glaive is planted, the chest is open and the pendulum is exposed.

**Boss HP:**
- Solo Medium: 13 500 HP, from the measured Vanguard DPS of about 90 × 150 s.
- With N players: 13 500 × (1 + 0.7 × (N − 1)). This is the packet's provisional formula and is pending the Bellkeeper D6. Damage taken does not scale with N.
- Expected length is about 3 minutes, which is about 7 loops.

**Second-form open question:** 30 % leaves the second form only a third of the fight. See Open questions.

## 4. Player counts (N = 1, 2, 4, 8)

| Ability | N = 1 | N = 2 | N = 4 | N = 8 |
|---|---|---|---|---|
| Meridian Lances | Volley 1 runs through you; volley 2 also aims at you (the "nearest") | Volley 1 through the farther player, volley 2 through the nearer one | Same rule, more bodies to keep out of the beams | Same; beam gaps near him get crowded, so melee must rotate as a group |
| Starfall Mirror | **Two stars in a row**: star I and its mirror, then star II hunts you from star I's lock and locks 2.0 s later. Same skill as a group: leave, avoid your reflection, leave again | 2 stars and 2 mirrors: spread so neither stands on the other's star or reflection | 4 + 4 circles | 8 + 8 circles, radius 1.8 m. Assign spots on a circle (checked: 8 players on a 6 m circle inside the Horizon all have a 2.1 m route) |
| Noon Shadow | **Noon and Afternoon**: your half, then the other half 3.8 s later. You must cross twice | Tie → the half holding the farther player is lit | The majority half is lit: the group crosses together | Same, and in Phase 2 the group must also spread for Starfall on the safe half |
| Orrery Closing | Same shapes | Same | Same | Same shapes; the Horizon packs 8 players inside 8.2 m with Starfall: a spatial job, not extra HP |
| Hour Hands (Hard) | **The hands strike twice**: spoke I at your hour, then spoke II tracks your new hour from spoke I's lock | One spoke per player; sharing an hour isn't extra damage, but stepping out of your spoke into your partner's is | Up to 4 spokes: hours must be spread | Up to 8 of the 12 hours used: assign hours |

## 5. Difficulty

- **Medium:** the numbers above.
- **Easy:** every warning, track and window is ×1.25, checked in `escape-check-easy.md`. Same shapes, and no Hour Hands. Damage taken is ×0.6 (the run's global rule).
- **Hard:** adds the one named mechanic, **Hour Hands**, in Phases 2 and 3 (see §3). Timings stay the same as Medium, and damage taken is ×1.3 (the global rule). Hard is not just faster attacks.

## 6. Escape check (Medium, 4.2 m/s, fastest route = walk, or one dash then walk)

All values are taken from `escape-check-medium.md`.

| Case | Worst escape | Window | Margin at 4.2 m/s | Note |
|---|---|---|---|---|
| Meridian Lances, 3 beams | 1.70 m | 1.2 s | 0.84 s | worst point beside his body, between beams |
| Meridian Lances, 4 beams (volleys 1, 2, cascade) | 2.40 m | 1.2 s | 0.79 s | same |
| Starfall Mirror, own star (N 1–4 / N 5–8) | 2.50 / 2.10 m | 1.1 s | 0.68 / 0.71 s | |
| Noon Shadow | 14.0 m | 3.8 s | 0.66 s | far edge; needs one dash at 4.2 m/s (walk only: 0.22 s) |
| Orrery Closing | 7.65 m | 2.3 s | 0.68 s | edge point behind a boss at 6 m; needs one dash at 4.2 m/s (walk only: 0.23 s) |
| Hour Hands (Hard) | 1.20 m | 1.0 s | 0.67 s | |
| Pairing: Noon + Starfall (edge runner) | 14.2 m | 3.8 s | 0.62 s | the star locks 10 m into the run; running straight on clears it |
| Pairing: Horizon + Starfall, N = 4 / N = 8 | 2.55 / 2.10 m | 1.1 s | 0.68 / 0.71 s | every player has a spot clear of all stars, mirrors, rim and crown |

Stamina: every case needs at most one dash (30 stamina). The longest chain is the Phase 3 loop: Noon and Afternoon, then Grand Orrery. That is three dashes about 4 s apart, so regeneration (20/s after 0.75 s) covers it.

## 7. Conflicts with settled rules (the rule was kept every time)

1. **Margin target vs Chzy18's v1 dash checks.** On 2026-10-21 Chzy18 chose dash checks for v1's Meridian Cut and Zenith Pins, with 0.06–0.08 s of spare time. The 2026-10-22 brief asks for 0.5–0.8 s of margin. v2 follows the newer brief, and v1 is unchanged behind `-lastbellAbilitySet v1`.
2. **"Nothing damaging under 2 s" vs "tighten toward R&S minimums".** Meridian Lances meets both by tracking its aim for 1.2 s before locking: 2.4 s total, 1.2 s window.
3. **Brief: "every co-op attack has a solo rule"** is satisfied. Noon Shadow's solo rule is a two-step sequence rather than a lighter version, so solo isn't easier.
4. **No soaks.** v2 has no shared soak, so the empty-soak wipe rule doesn't come up. That's deliberate: Aurelion's block row has no soak.
5. **Knockback.** Ground rings (Starfall, Orrery, Horizon) and Noon Shadow use **no knockback**, as the rules allow, so nobody is pinned against the bound. Lances and Hour Hands keep the standard 1.5 m sideways push.

## 8. Open questions for Chzy18

1. **When should the second form start?** 30 % (Exposed Meridian) keeps the packet's phases, but the second form then lasts only a third of the fight. One option is moving the thresholds to 70 / 40 %.
2. **HP scaling with N:** using 1 + 0.7 × (N − 1) until the Bellkeeper D6 decides.
3. **Noon Shadow at the edge.** The worst case is a 14 m run that needs one dash at 4.2 m/s, though walking at 6 m/s is fine. Is that the right amount of anti-range pressure, or should the line pass through the boss instead of the centre?
4. **Starfall mirror point:** through the arena centre (proposed: fixed and learnable) or through the boss?
5. **Hour Hands on Easy:** off, as proposed, or on with longer warnings?
6. **Does v1 stay as a practice option** after v2 is approved, or should it be removed?

## 9. Briefs

- `01-meridian_lances.md`
- `02-starfall_mirror.md`
- `03-noon_shadow.md`
- `04-orrery_closing.md`
- `05-hour_hands.md` (Hard)
