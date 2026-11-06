# Sylvara v3: Easy and Hard extras (accepted 31 Oct)

Status: **accepted** (review inbox, 31 Oct), including the softer Easy Last Storm below. Built in Unity. Medium is the accepted fight, and every number in the phase docs is Medium.

The rule is from `gpt/08-design-rules.md` (difficulty levels):
- The build already scales damage taken (Easy ×0.6, Hard ×1.3).
- A boss may add its own changes per level:
  - Easy gets longer warnings.
  - Hard gets **one named extra mechanic**, not just faster or harder-hitting attacks.
- Every level must still have a route out.

Try it in the demo: `demo/sylvara-v3-demos.html`, **Level** row in the top bar. It works with the full fight (Z) and with the P4 enrage run (V).

| Phase | Hard: the one extra mechanic | Easy |
|---|---|---|
| 1 Spiral Bloom | **Every pod is Doubled.** A second ring fires 0.6 s after each pod, rotated half a step so it fills the first ring's gaps. On Medium only the 14.5 s pods do this. | Pods warn 3.0 s (was 2.5), jets warn 2.0 s (was 1.5), the Pruning Arc warns 2.5 / 3.3 s (was 2.0 / 2.8), and the spiral turns 12°/s (was 15). |
| 2 Two-Colour Choir | **Double toll.** Each toll's 20-petal two-colour burst fires twice, 0.5 s apart. | Lanes fire every 0.5 s (Medium 0.35 s after the 29 Oct playtest), and the second counter-turning spiral set never joins. |
| 3 Hollow Bloom | **One more Choir Bud per wave:** 3/3/4/4 at 1/2/3/4 players. | Buds bloom after 15 s (was 12), and the Hollow Spiral has 3 arms (was 4). |
| 4 Seed Storm | **Seeds burst into 10 petals** (was 8). | Seed rings start at 6 seeds every 2.5 s (Medium: 8 every 2.0 s). The enrage steps are the same: one more seed and 0.1 s sooner every 24 s loop. |

- **Enrage:** the Phase 4 clock is the same on all three levels (accepted 29 Oct; the 31 Oct review kept it).
- **Easy Last Storm (accepted 31 Oct):** the review chose "Soften on Easy". It still starts at 2:00 with the same 3 s banner, and it still ends the fight, only slower.

  | | Medium and Hard | Easy |
  |---|---|---|
  | Seed rings | 14 seeds every 0.6 s | 12 seeds every 0.8 s |
  | Spiral | 4 arms, 5 m/s, every 0.15 s, 12°/s | 3 arms, 4.5 m/s, every 0.15 s, 10°/s |
  | Autopilot damage per minute (demo, 8 runs) | about 1,260 (Medium) | about 340 (after the ×0.6 scale) |
  | Full-HP player goes down in | about 5 s | about 18 s |

  Hard keeps Medium's Last Storm (its ×1.3 damage already makes it faster).
- **Route out:**
  - Doubled pods (Hard P1) already run on Medium at 14.5 s with a route.
  - The double toll (Hard P2) is a second ring 0.5 s later with a fresh random rotation. At 4.5 m/s the two rings travel 2.25 m apart, so each one is dodged on its own, and half of each ring is the other colour.
  - The extra bud (Hard P3) is one more 3-arm spiral at 10 m.
  - 10-petal bursts (Hard P4) put petals 36° apart (was 45°). A player fits between two petals once they are about 2.4 m from the burst (about 1.9 m on Medium), so standing right next to a seed when it bursts gets you hit on both levels.
- **Demo autopilot (one run each, autopilot player, 31 Oct, measured before the softer Easy Last Storm):**

  | Level | Full fight | P4 enrage run |
  |---|---|---|
  | Easy | 121 damage, peak 222 bullets | 165 damage |
  | Medium | 176 damage, peak 313 bullets | 280 damage |
  | Hard | 252 damage, peak 332 bullets | 871 damage (most of it in Last Storm) |

  Damage includes the ×0.6 / ×1.3 scale.

## Hard: more to dodge (5 Nov, done as recommended, review later)

Chzy18 (5 Nov, through the overview): make Hard harder with more to dodge, not more damage (gpt/08, 2 Nov). Medium and Easy are unchanged. Each addition is its own commit on `feature/sylvara-v3`, so any one can be reverted alone. This adds to the 31 Oct "one extra mechanic per phase"; the 31 Oct review said doubled pods were enough for Hard Phase 1, and this request supersedes that.

| Phase | Hard addition | Route out | Commit |
|---|---|---|---|
| 1 Spiral Bloom | **Mirrored Jets.** Every Waterfall Jet also pours from the opposite wall, sliding the other way, with its own 1.5 s wedge warning. | Each stream is a thin line that sweeps 40°; the wedge shows where. Up to 8 lines at 4 players, about 230 drops in all. | 39ec8b3 |
| 2 Two-Colour Choir | **Choir Pods.** Seed Pods drop on the farthest players at 16.5 s and land at 19 s, before the 20 s toll. Their coral petals hit every colour. | The usual pod route: leave the 2.5 m circle; the 12-petal ring has 30° gaps. | 1b90383 |
| 3 Hollow Bloom | **Counter Spiral.** From 4 s, a 2-arm spiral (12°/s, 4 m/s) turns the other way under the Hollow Spiral. | It keeps the same gap pattern (7 shots of every 9), so the crossing lanes still open. | a4a328b |
| 4 Seed Storm | **Storm Arc.** A Pruning Arc at 15 s in each loop before Last Storm; both cuts land by 17.8 s, before the 18 s pods. | As in Phases 1 and 3: the 60° side slivers are safe. | 2109399 |

No addition needs a jump, a class or immunity, and each works the same for 1 and 4 players (the jets and pods pick their targets by distance). Last Storm is unchanged.

## Open questions
1. Hard Phase 1: doubled pods are enough (31 Oct review).
2. Easy Last Storm: the numbers above were accepted on 31 Oct.
3. Built in Unity (31 Oct): `SylvaraEncounter` reads `CombatWorld.Difficulty` for the accepted extras, and the autotest checks each level.
   - As in the demo, the Easy Pruning Arc warning applies wherever the Arc appears (Phases 1 and 3). The Easy pod warning and the Hard doubled pods apply to Phase 1 pods only.
