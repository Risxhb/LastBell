# Aurelion ability set v2.1: harder (proposal)

**Status:** proposal, playtest-driven. **Base:** `ABILITIES.md` (v2), as built on `feature/aurelion-abilities-v2` at `681e7f8`.

**Why:** Chzy18 playtested on 2026-10-22, solo, ranged, Hard, with 20 % boss HP. The feedback: *"way way too easy for hard difficulty. I had no problem dodging the mechanics."* He then chose three changes:
1. **constant layering**
2. **tighter windows**
3. **a new ability** if one is needed

v2.1 does all three. Every v2 shape, damage value and solo/N rule is unchanged unless listed below.

## 1. New core ability: Zenith Collapse (`aurelion.zenith_collapse`), all difficulties

**Fantasy:** the Regent strides to the meridian, the arena centre, and lifts the glaive straight up. Every orrery ring folds inward at once and the whole sky falls on the dais, except a small disc of shade at his feet, under the halo.
- **Telegraph:** the whole annulus is coral/amber with a crisp inner edge. The orrery decor rings slide inward from the rim to the safe-disc edge while it charges.

**Shape (Ground):**
- an annulus centred on the **boss**, inner radius **4.5 m** (N ≤ 2), **5.5 m** (N 3–4) or **6.5 m** (N ≥ 5), and outer radius 30 m, past the bound
- It is **only cast with the boss at the arena centre.** The block starts with him walking there (`WalkTo(Vector3.zero)`; at 2.2 m/s from 9.5 m this takes up to 4.3 s, and other layers run meanwhile, see §3). Then the telegraph appears.

**Timing:**
- locks at once
- resolves **3.0 s** later on Medium, **2.4 s** on Hard, **3.75 s** on Easy

**Damage and hit:** **45**, no knockback, stun 0.25 s.

**Escape:** the worst case is a player at the rim, 9.7 m from the safe disc (N ≤ 2).

| Speed | Walk | Walk + 1 dash | Sprint |
|---|---|---|---|
| 4.2 m/s | 2.56 s | 2.12 s | 1.33 s (9 m/s, uses 27 stamina) |
| 6 m/s | 1.87 s | 1.74 s | 1.33 s |

| Level | Window | Margin (fastest route) |
|---|---|---|
| Medium | 3.0 s | 1.67 s sprint, 0.88 s dash |
| Hard | 2.4 s | 1.07 s sprint, 0.28 s dash |

**Threats:**
- **Melee threat:** none on its own. That's the point: this is the "come close" move. Its layer partner, Meridian Lances, makes the safe disc deadly for anyone standing still. With 3 beams nothing within 2.8 m of his centre is safe; with 4 beams, nothing within 3.7 m.
- **Ranged threat:** the whole outer arena. Ranged players must commit and run in on every cast.

**Players and phases:**
- **Solo:** the same disc. Solo gets the paired Lances aimed at you, so you must find a gap inside the disc.
- **N = 8:** a 6.5 m disc. Eight players fit between his beams only by taking assigned wedges.
- **Phases:** every phase, always paired with Meridian Lances (§3). Lances fires so it resolves **0.3 s after** the Collapse.

**R&S source:** "get in the circle" / safe-zone-near-boss (§4 block 2's stack geometry, inverted). The brief allows it explicitly: *"a 'come close' mechanic, such as a soak or safe zone near the boss"*.

## 2. Tighter windows on Hard

On **Hard**, every **lock-to-hit window is ×0.7**. Tracking times stay the same, so the warning stays readable and the punish after the lock is sharper.
- The total warning stays ≥ 2 s (settled rule): v2's Lances volley 2 track is raised from 1.0 to **1.2 s** on Hard.
- Orrery Closing has no track, so on Hard it stops at **2.0 s**, the floor.

| Ability | Medium window | **Hard window** | Hard total warning | Hard fastest route at 4.2 m/s (margin) |
|---|---|---|---|---|
| Meridian Lances (3 / 4 beams) | 1.2 s | **0.84 s** | 2.04 s | dash 0.36 / 0.41 s (0.48 / 0.43 s) |
| Starfall Mirror | 1.1 s | **0.77 s** | 2.77 s | dash 0.42 s (0.35 s) |
| Noon Shadow / Afternoon | 3.8 s | **2.66 s** | 2.66 s | sprint 14 m in 1.81 s (0.85 s); dash + walk 3.14 s doesn't make it, so **sprint is required** from the far edge |
| Orrery Closing | 2.3 s | **2.0 s** (floor) | 2.0 s | sprint 7.65 m in 1.10 s (0.90 s); dash 1.62 s (0.38 s) |
| Hour Hands (Hard) | 1.0 s | **0.7 s** | 2.2 s | dash 0.33 s (0.37 s) |
| Zenith Collapse (new) | 3.0 s | **2.4 s** | 2.4 s | sprint 1.33 s (1.07 s) |

Every Hard escape needs a dash or a sprint from the worst spot; walking alone is too slow on purpose. **Stamina:** the longest Hard chain is Noon and Afternoon, two sprints of about 1.6 s each (40 stamina each), 2.66 s apart. Regeneration (20/s after 0.75 s) brings back about 38 between them, so a player who also dashes in the paired Starfall must budget. That budgeting is the intended skill. Easy and Medium windows are unchanged.

## 3. Constant layering (Medium and Hard; Easy keeps v2's plan)

There are two damaging patterns at once in **almost every block from Phase 1**, always "where to stand" + "stand apart / find the gap". Never more than two. Gaps between blocks drop from 1.0 / 0.6 s to **0.3 s**, and the 3.5 s open-chest punish stays the only rest.

| Phase | Loop (in order) |
|---|---|
| **Perfect Order** | **Lances (3) + Starfall**: Starfall first, Lances starts 0.5 s later so both resolve within 0.2 s → **Noon + Starfall** (as v2 Phase 2) → **Zenith Collapse + Lances (3)** → Orrery Closing → **Horizon + Starfall** → **punish 3.5** |
| **Misalignment** | **Lances (4, 2 volleys) + Starfall** (with volley 2) → **Noon + Starfall** → **Zenith Collapse + Lances (3)** → Orrery Closing → **Horizon + Starfall** (Hard: then Horizon + Hour Hands) → **punish 3.5** |
| **Exposed Meridian** (second form) | **Cascading Lances + Starfall** (with the cascade) → **Noon and Afternoon**, with Starfall on Afternoon → **Zenith Collapse + Lances (4)**, safe disc **5.0 m** at N ≤ 2 so pockets exist between the beams → **Grand Orrery** (Orrery + Starfall) → **Horizon + Starfall** (Hard: + Hour Hands) → **punish 3.5** |

While Aurelion walks to the centre for Zenith Collapse, he keeps the previous block's second layer going (Starfall). He is never idle.

**Joint routes:**

| Pairing | Worst case | Fastest route at 4.2 m/s | Window: Medium / Hard | Margin: Medium / Hard |
|---|---|---|---|---|
| Lances + Starfall | Leave your own star (2.5 m) and end in a beam gap (≤ 1.6 m extra). The windows resolve together; the beams' lock comes after the star's. | dash 3 m + 1.1 m walk: 0.78 s | 1.1 / 0.77 s | 0.32 s / **fails at 4.2** (sprint 0.71 s → 0.06 s) |
| Zenith Collapse + Lances | A rim player runs 9.7 m in, then up to 1.6 m sideways into a pocket | sprint 11.3 m in 1.51 s | 3.0 / 2.4 s | 1.49 / 0.89 s |

**Lances + Starfall on Hard therefore needs a fix.** The Starfall window in that pairing is **not** cut (it stays 1.1 s), and only the beams' window is cut. That gives a Hard margin of 1.1 − 0.78 = 0.32 s at 4.2 m/s, and more at 6 m/s.

**The build must re-run the escape check** (`tools/encounter-escape/aurelion-v2-escape-check.ps1`) with Hard windows, a sprint route and the two new pairings, and adjust any case that fails by size or position, never by immunity.

## 4. Conflicts and open questions

**Conflicts:**
- **The v2 brief's 0.5–0.8 s margin target** is superseded on Hard by Chzy18's "tighter windows" choice. Hard margins are now 0.06–1.07 s on the fastest route (sprint or dash), all ≥ 0 with finite stamina. Medium keeps ≥ 0.3 s.
- **The v2 brief asked for 4 core abilities;** Chzy18 has now approved adding a fifth ("If you have to create a new ability").
- **The rule says Hard should add a named mechanic, "not just faster attacks".** Hard keeps Hour Hands as its named mechanic; the faster windows are on top, at Chzy18's request.

**Open questions:**
- Should Medium also get some tightening (e.g. ×0.85)? v2.1 keeps Medium's windows and adds only layering and Zenith Collapse.
- Zenith Collapse forces the boss to the centre once per loop. Is that predictability fine, or should he sometimes cast it off-centre with a smaller disc?
