# The Ledger (`ultimate.ledger`, key `ult_ledger`): build brief

**Status:** proposal. Built 2026-10-23 by the Ledger session on `feature/bellkeeper-ult-ledger` (local only). Built and autotested; **not human-approved** until Chzy18 plays it.
**Read:** `gpt/08-design-rules.md` (with "Exception, Bellkeeper Ultimate"), `ULTIMATE.md` §3.3, §5.1, §5.6, `GUIDE.md`, `BUILD.md`.
**Code:** `unity/Assets/LastBell/Run/Encounters/Bellkeeper/Ultimate/Ledger.cs` (`LedgerMechanic`). **Tests:** `Tests/RunAutoTest.Bellkeeper.Ult.Ledger.cs` (`UltLedgerChecks`).

## Rule as built

| | |
|---|---|
| **Record** (cue `"record"`, P1 loop 1 at 0 s) | Turns the shared recorder on. Keeps the first six P1 strikes of Leaning Bell, Toll Tide, Heart Toll and Reflection, grouped by resolve tick: **I** Leaning Bell (8.5), **II** Toll Tide ring (11.5), **III** Heart Toll (13.5), **IV** Reflection images (19, every image on one page), **V** Leaning Bell (22), **VI** Heart Toll (27). Clocks and tolls are heard by the recorder and ignored. The recorder turns off after VI. |
| **Look in P1** | Each kept footprint shows as a **dashed ivory outline** (`Ult.Outline`) with an **ivory Roman numeral** and an **ivory clock face whose brass hand points at that hour**, for 2 s, then fades. Boss clip `WriteLedger` at each kept page. |
| **Replay** (cue `"replay"`, P4, three times per loop) | **VI → I**, `At[0]` then 1.5 s apart. Each strike is the recorded footprint exactly as it resolved (place, size, every image). Each one is **coral for 1.2 s** before it strikes, with no dashed preview and its numeral standing on it. **60** each (heavy). The tide still pushes inward; the rest push 1.5 m outward. Boss clip `TurnLedger` (halo spins backwards) for the whole replay. |
| **Nothing recorded** | Practice from P4 (`-lastbellUltimatePhase 4`), or any new attempt that has not run P1, replays the **authored clean ledger**: both halves toward the NE station, the Heart Tolls on the centre, and the images clumped NE 5.5 m out (solo: plus your own image on the safe side, 40° round). If P1 was cut short, the missing pages come from the authored ledger. |
| **Tags** | Leaning Bell halves: Strike. Ring, Heart Toll and images: Ground (Brace never helps). |
| **N** | Identical for N = 1/2/4/8; the images page has one footprint per player (solo 2: mirror + own), 3.0 m (2.5 m from N = 5). |
| **Damage rules** | 60 per strike: one strike never kills from full HP, two do. The Ledger never wipes, never cracks, never one-shots. |
| **Boss death / halt / restart** | Cancels every pending strike, outline, numeral and clock glyph on that tick, and turns the recorder off. |

### Changes from ULTIMATE.md §3.3 (hardening, Chzy18 2026-10-23: "add and replace abilities as you see fit"; confirmed by the Ultimate session)
- **Coral 1.2 s instead of 1.5 s.** The strikes stay 1.5 s apart, so a party that pre-positions from memory loses nothing. A party that only reacts to coral gets 5.0 m plus one dash per strike, not 6.3 m plus one dash. That is the memory check the design asks for.
- **A messy P1 costs.**
  - **Scattered images run.** If any P1 image was more than 4 m from the images' centre, the replayed images are 1.5× wider, in the same places. Solo never counts as scattered.
  - **Opposite halves come back to back.** If the two Leaning Bells struck halves more than 90° apart, V strikes **0.75 s before I** (in the slot just before I's) instead of 6 s earlier. The line must be crossed in 0.75 s: stand 1 m on V's safe side, then step 2 m across.
  - A clean P1 replays exactly as recorded.
- **No reflections in stopped time.** A replayed images page that would strike while anyone is Stilled (before the resume tick) is held and never strikes. In P4 block 2 the stop (5.0–9.0 s) covers V (6.0) and IV (7.5). Without this rule, a Stilled player whose station is opposite the ledger half was forced into V *and* the clumped images (120 = certain death).
- **The one documented forced hit (P4 block 2, clean ledger).** A Stilled player whose clock station is opposite the ledger half has a hiding strip inside replayed V's half, so they take 60 (Strike). It is survivable from 61+ HP: a potion check. Stations on the ledger half or at 90° to it have a clean spot. A split ledger moves V after the stop.

## The strat (`Demands()`, GUIDE.md)
- **P1 (record a clean ledger).** At Leaning Bell I's cast (5 s), everyone stands together about 5 m out on the **ledger half**, the side of **clock III's station**. Leaning Bell V leans there by the pairing rule. Leaning Bell I falls on the side where most of the party stands, so both halves land on the same side. At Reflection's lock (17 s), everyone whose clock isn't striking then clumps on one spot: that clock's striker spot, otherwise 5 m off the ledger half's opposite side. That way the images clump. Both demands are `Memory` (a read).
- **P4 (replay).** Be outside each replayed footprint on its tick. `Since` = the tick that page was recorded in P1, `Memory` = true. `Hint` = the next spot on the shortest walking route through the six strikes, solved once per replay.

## Escape proof (paper), checked in code
Walking 4.2 m/s, finite stamina, no iframes. The player margin is 0.3 m; route spots keep 0.25 m clear.

1. **Each step:** strikes are 1.5 s apart, which is 6.3 m at 4.2 m/s before any dash. A memorised route needs at most that between two consecutive safe spots. The solver (`LedgerMechanic.Solve`) uses a 0.6 m floor grid with 0.25 m clearance and ≤ 6.3 m hops, and prefers hops ≤ 2.5 m.
2. **Some P1 play always records a solvable P4: the clean play.** With both halves on side *h* and the Heart Tolls on a boss within 3 m of the centre, one region is safe from all six strikes: the *−h* half, 4.3–6.7 m out (outside the Heart Tolls, inside the tide's 7 m edge), clear of the images. Clumped images sit on one side; solo, your own image covers at most about 70° of that 180° arc. So a party can stand on one spot for the whole replay (longest hop 0 m). In the tests the solver finds the authored and recorded clean ledgers with every hop ≤ 2.5 m. The strat bots clear them at 4.2 m/s for N = 1/2/4/8, starting 8 m out (solo: from the far wall of the hit half). Bots that play the GUIDE strat in P1 record a clean ledger by themselves (Lean I picks the ledger half from where they stand) and then clear its replay.
   - If P1's boss stood up to 3 m off-centre, the Heart Tolls shift by up to 3 m. The safe region then slides round the arc. Example: boss at 3 m on the −h side; the spot 5.4 m along the line and 0.8 m into the −h half is 5.8 m from that Heart Toll and 5.5 m from the centre, so it is still safe.
3. **A messy P1 costs, but stays survivable from memory.** Test recordings: opposite halves, with images scattered at 6 m round the boss.
   - The free floor safe from V, IV and III at once drops from about 217 m² (clean, N=4) to **109 m² (N=4)** and 104 m² (N=8). Solo images never count as scattered: 192 m².
   - Opposite halves now need the line crossed in 0.75 s. The per-hop walking budget is (gap − 0.1 s) × 4.2 m/s, so 2.7 m for that hop: from 0.55 m on one side to 0.55 m on the other is 1.1 m ✓.
   - The solver still finds a route (longest hop 1.9 m at N=4, 1.7 m solo, 2.5 m at N=8), and the strat bots clear it untouched at 4.2 m/s.
   - A messier P1 than that (images on the hiding strips, or on the clock spots in blocks 1 and 3) can be fatal. That is the party's own ledger; the clean play always exists (point 2).
4. **Without memory the long moves fail:** from the far wall of a replayed half, the crossing is 13.3 m. Coral alone gives 1.2 s × 4.2 m/s + one 3 m dash = 8.0 m. ✓ by design.
5. **Melee and ranged:** a still melee player next to the boss is struck by both Heart Tolls (VI, III). A still ranged player at 10 m is struck by the tide (II), and by a half if they are on its side.

## Rig clip list (new boss clips; don't edit the rig here)
- `WriteLedger`: the left (conducting) hand traces a numeral on the air, about 0.9 s, once per kept page in P1.
- `TurnLedger`: the halo spins backwards and the ledger pages turn, for the whole replay (about 11.5 s).

## Decisions (Ultimate session, 2026-10-23)
- The P4 block 2 forced 60 stays (a potion check), and is documented in GUIDE.md. At most one forced hit is allowed, so the images are held in stopped time.
- Coral 1.2 s: yes. Numerals: keep them, in ivory over the live coral shapes.
- The gate must bite: scattered images ×1.5 and opposite halves 0.75 s apart (built as above).
- How I read the request: "spread ×1.5 from their clump centre" is built as the **radius** ×1.5, in place. Pushing scattered images further from their centre would move them toward the wall and make the band *safer*.

## Open questions
1. Chzy18: is the messy-P1 cost the right size (×1.5 images, a 0.75 s crossing), or should a messy P1 cost more?
