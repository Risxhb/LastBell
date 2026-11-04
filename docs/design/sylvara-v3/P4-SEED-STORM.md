# Sylvara v3, Phase 4: Seed Storm (last phase)

**Status: picked (Chzy18, 28 Oct 2026). The full fight was accepted for the Unity build on 29 Oct. The enrage timing was accepted on 29 Oct.** It has been played only in the browser demo: the "Phase 4 new" row, "G · Seed Storm", and inside the "Full fight" run. Built in Unity on `feature/sylvara-v3` (see README.md for what is built and what is design only).

## Phase
- **HP band:** the last phase. Thresholds get set once the fight is accepted as a whole.
- **Position:** rooted at the centre. Her form stays split from Phase 3.
- **No rest:** no punish window (the Sylvara exception in `gpt/08-design-rules.md` §5).
- **Enrage:** yes, with the timing below (accepted by Chzy18, 29 Oct).

## Loop (24 s, repeats), Medium numbers, 100 player HP

| t (s) | Mechanic | Shape | Warning | Damage | Melee threat | Ranged threat |
|---|---|---|---|---|---|---|
| 0 → | **Slow Spiral** | 2 arms, 4.5 m/s, a petal every 0.22 s, turning −8°/s | travel | 10 | near her | the outer ring |
| every 2 s | **Seed Ring** | 8 big seeds (0.6 m) from her rim at 3 m/s, in a random rotation. On alternate rings they burst after 1.6 s (about 6 m out) or 2.4 s (about 9 m out), each into 8 petals at 4.5 m/s | the seed itself, visible in flight | seed 16, petal 12 | short-fuse bursts send petals back at her | long-fuse bursts reach the wall |
| 6, 18 | **Seed Pods** | on the farthest 1/1/2/3 players (as in Phase 1) | 2.5 s | 16 + 12 | | dropped on you |
| 12 | **Root Ring** | 3.5 m disc at her feet | 2 s | 18 | yes | — |

- **Splitting bullets** are new in this phase: dodge the seed first, then the burst.
- **Peak density in the demo:** about 160 bullets.
- **Difficulty in the demo:** about Phase 3's level after the easing.

## Players
- **Solo:** pods come one after another, all on you. The storm is the same for everyone.
- **Group:** at 8 players there's less room between bursts, and three pods land at a time.

## Settled (dated)
- **28 Oct:** Chzy18 turned down the first four Phase 4 options (Overgrowth, Final Chorus, Last Bloom, Rootfall) and the other new ones (Requiem Bells, Vine Sweep, Orbiting Choir). Seed Storm was picked.
- **28 Oct:** the fight is Spiral Bloom → Two-Colour Choir → Hollow Bloom (second form) → Seed Storm. The demo's "Full fight" runs them back to back (about 105 s) for review before any Unity work.

- **29 Oct:** the enrage timing was accepted (review inbox): a step every 24 s, capped at loop 5 (1:36), and Last Storm at 2:00 of Phase 4, with the same clock on every difficulty.
- **29 Oct:** the full four-phase fight was accepted as the plan for the Unity build (review inbox). There's no intermission; the phases run back to back. Phase 4 gets an enrage, with the timing to be proposed as a review item.

## Enrage (accepted, 29 Oct)
Demo: the "P4 enrage" row, "Seed Storm, enrage" (option V), with Loop 1 / 3 / 5 / Last Storm jump buttons. Timer starts when Phase 4 starts.

| Phase 4 time | Step | Seed Ring |
|---|---|---|
| 0:00 | loop 1 | 8 seeds every 2.0 s |
| 0:24 | loop 2, banner "The storm thickens" | 9 / 1.9 s |
| 0:48 | loop 3 | 10 / 1.8 s |
| 1:12 | loop 4 | 11 / 1.7 s |
| 1:36 | loop 5 (cap) | 12 / 1.6 s |
| 1:57 | "LAST STORM" banner, 3 s | — |
| 2:00 | **Last Storm, hard enrage**: the slow spiral is replaced by 4 arms (5 m/s, every 0.15 s, 12°/s) | 14 seeds every 0.6 s |

- Pods and the Root Ring keep their places in every loop.
- **Tuning target:** set Phase 4 HP so a normal party kills her in about 75–90 s, so most kills land in loop 4 and Last Storm is reached only by a party that is short on damage.
- **Difficulty:** the same clock on Easy, Medium and Hard (accepted). Last Storm is softer on Easy (12 seeds every 0.8 s, 3-arm spiral; accepted 31 Oct, see [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md)).
- **Demo measurements** (bots, damage per player per minute):

  | Players | Loop 1 | Loop 3 | Loop 5 | Last Storm |
  |---|---|---|---|---|
  | 1 | 70 | 60 | 180 | ~1120 |
  | 4 | 130 | 145 | 105 | ~970 |
  | 8 | 125 | 170 | 140 | ~770 |

  The soft steps stay under Phase 2's level (about 220/min). Last Storm downs a full-HP player in about 5–8 s.

## Open questions
1. **Hard and Easy:** accepted 31 Oct, see [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md); built in Unity. The softer Easy Last Storm was accepted the same day.
