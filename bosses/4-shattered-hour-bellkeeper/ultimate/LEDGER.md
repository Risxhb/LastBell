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

### Change from ULTIMATE.md §3.3 (hardening, Chzy18 2026-10-23: "add and replace abilities as you see fit")
- **Coral 1.2 s instead of 1.5 s.** The strikes stay 1.5 s apart, so a party that pre-positions from memory loses nothing. A party that only reacts to coral gets 5.0 m plus one dash per strike, not 6.3 m plus one dash. That is the memory check the design asks for.

## The strat (`Demands()`, GUIDE.md)
- **P1 (record a clean ledger).** At Leaning Bell I's cast (5 s), everyone stands together about 5 m out on the **ledger half**, the side of **clock III's station**. Leaning Bell V leans there by the pairing rule. Leaning Bell I falls on the side where most of the party stands, so both halves land on the same side. At Reflection's lock (17 s), everyone whose clock isn't striking then clumps on one spot: that clock's striker spot, otherwise 5 m off the ledger half's opposite side. That way the images clump. Both demands are `Memory` (a read).
- **P4 (replay).** Be outside each replayed footprint on its tick. `Since` = the tick that page was recorded in P1, `Memory` = true. `Hint` = the next spot on the shortest walking route through the six strikes, solved once per replay.

## Escape proof (paper), checked in code
Walking 4.2 m/s, finite stamina, no iframes. The player margin is 0.3 m; route spots keep 0.25 m clear.

1. **Each step:** strikes are 1.5 s apart, which is 6.3 m at 4.2 m/s before any dash. A memorised route needs at most that between two consecutive safe spots. The solver (`LedgerMechanic.Solve`) uses a 0.6 m floor grid with 0.25 m clearance and ≤ 6.3 m hops, and prefers hops ≤ 2.5 m.
2. **Some P1 play always records a solvable P4: the clean play.** With both halves on side *h* and the Heart Tolls on a boss within 3 m of the centre, one region is safe from all six strikes: the *−h* half, 4.3–6.7 m out (outside the Heart Tolls, inside the tide's 7 m edge), clear of the images. Clumped images sit on one side; solo, your own image covers at most about 70° of that 180° arc. So a party can stand on one spot for the whole replay (longest hop 0 m). In the tests the solver finds the authored and recorded clean ledgers with every hop ≤ 2.5 m. The strat bots clear them at 4.2 m/s for N = 1/2/4/8, starting 8 m out (solo: from the far wall of the hit half). Bots that play the GUIDE strat in P1 record a clean ledger by themselves (Lean I picks the ledger half from where they stand) and then clear its replay.
   - If P1's boss stood up to 3 m off-centre, the Heart Tolls shift by up to 3 m. The safe region then slides round the arc. Example: boss at 3 m on the −h side; the spot 5.4 m along the line and 0.8 m into the −h half is 5.8 m from that Heart Toll and 5.5 m from the centre, so it is still safe.
3. **A messy P1 records a nastier P4, mostly through what sits on top of it.** The tests measured one messy recording (opposite halves, four images spread at 6 m) with no overlays. A walking route still exists (longest hop 0.8 m) and the strat bots took 0 hits. The free floor a player frozen from V to III can stand on (P4 block 2's Stopped Hour) shrinks from about 217 m² (clean, N=4) to 163 m². The gate bites hardest when the scattered images sit on the hiding strips behind the boss, or on the clock spots in blocks 1 and 3. On its own, with exact recorded shapes, a messy ledger is harder but rarely unsolvable (see open question 4).
4. **Without memory the long moves fail:** from the far wall of a replayed half, the crossing is 13.3 m. Coral alone gives 1.2 s × 4.2 m/s + one 3 m dash = 8.0 m. ✓ by design.
5. **Melee and ranged:** a still melee player next to the boss is struck by both Heart Tolls (VI, III). A still ranged player at 10 m is struck by the tide (II), and by a half if they are on its side.

## Rig clip list (new boss clips; don't edit the rig here)
- `WriteLedger`: the left (conducting) hand traces a numeral on the air, about 0.9 s, once per kept page in P1.
- `TurnLedger`: the halo spins backwards and the ledger pages turn, for the whole replay (about 11.5 s).

## Open questions
1. **P4 block 2 (Stopped Hour resume on III).** Stilled players are frozen from 5.0 to 9.0 s, while replayed V (6.0), IV (7.5) and III (9.0) land. Take a Stilled player whose P4 clock station is on the **opposite** side from the ledger half. Their hiding strip behind the boss lies inside the replayed Leaning Bell V half, so they take 60 (Strike, survivable from 61+ HP) to avoid the 999 lash. A Stilled player whose station is on the ledger half, or at 90° to it, has a clean spot. Should the Stopped Hour in block 2 avoid giving the opposite station to a Stilled player, or is this a forced heavy hit the party must enter topped up for? (Kept as built; the plan and Stopped Hour belong to other sessions.)
2. Is coral 1.2 s right, or should it go back to the design's 1.5 s?
3. Should the replayed footprints keep their numerals on the live coral shape (built that way for readability), or show coral only?
4. **A stronger gate?** If the gate should bite on its own, one option is for the replayed images to **spread** (radius × 1.5, "the ink runs"). Clumped images then still sit on the hit half, and a solo player's own image still leaves about 70° of the safe band. Four scattered images would cover nearly the whole band. Not built: it breaks "exactly as they resolved". Chzy18's call.
