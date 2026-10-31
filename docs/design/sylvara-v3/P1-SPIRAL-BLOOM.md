# Sylvara v3, Phase 1: Spiral Bloom

**Status: accepted (Chzy18, 28 Oct 2026).** It has been played only in the browser demo. Built in Unity on `feature/sylvara-v3` (see README.md for what is built and what is design only).
- Demo: `bosses/3-verdant-chimes-sylvara/phase1-demos/sylvara-phase1-demos.html` (option 5, "Spiral Bloom"). Published privately at https://claude.ai/artifact/45LQCHGWZK7WMcAtheEw6T.
- v3 is a full bullet-hell rework. It replaces the v2 set (Canticle, Two Choirs, Listening Roots, Refrain Seed, Antiphon) as the plan. v2 stays on `feature/sylvara-abilities-v2` for parts.

## Phase
- **HP band:** 100–? %. The thresholds get set once every phase is accepted. Four phases, no intermission (Chzy18, 29 Oct).
- **Position:** Sylvara stays rooted at the arena centre. Her body is 1.6 m.
- **No rest:** no punish window. Chzy18 removed punish windows from the whole fight on 28 Oct (an exception in `gpt/08-design-rules.md` §5).

## Loop (30 s, repeats), Medium numbers, 100 player HP

| t (s) | Mechanic | Shape | Kind | Warning | Damage | Melee threat | Ranged threat |
|---|---|---|---|---|---|---|---|
| 0 → | **Petal Spiral** | 4 arms from her rim, petals at 5 m/s, one every 0.15 s, gaps every 9th–7th petal; spins 15°/s | bullets | travel time | 10 per petal | arms are tight near her | at 10 m you walk about 2.6 m/s just to stay between arms |
| 3 | **Seed Pods** | 2.5 m circle on the farthest 1/1/2/3 players (N 1/2/4/8), tracks 0.6 s; bursts into 12 petals at 5.5 m/s; turquoise ticks mark the gaps | Ground + bullets | 2.5 s | 16 + 12/petal | rings cross the inner area | pods are dropped on you |
| 6 | **Waterfall Jets** | a solid stream (every 0.07 s, 9 m/s) from the wall to her rim; the source slides 40° along the wall over 2 s, starting 20° before the target | bullets | 1.5 s wedge + arrow | 14 per drop | — | one per player past 8 m (else the farthest); at the edge the sweep outruns walking, so step back past its start or move inward |
| 8–9 | **Toll** | the spin reverses | — | 1 s banner | — | | |
| 10 | **Feet Pod** | 3.0 m circle at her centre, then 12 petals point-blank | Ground + bullets | 2.5 s | 16 + 12 | yes | petals reach the wall |
| 13 | **Pruning Arc** | 120° cut, 8 m, toward the nearest player (tracks 0.3 s); the back 120° cut lands 0.8 s later; each cut throws 9 petals (15° apart) from 8 m to the wall | Strike + bullets | 2.0 / 2.8 s | 20 + 12 | safe only in the two 60° side slivers | the petal fans cross the outer ring |
| 14.5 | **Doubled Pods** | as Seed Pods, plus a second ring 0.6 s later rotated half a step, so it fills the first ring's gaps | | 2.5 s | | | |
| 18 | **Waterfall Jets** | again | | | | | |
| 21–22 | **Toll** + **2nd set** | the spin reverses again; a second spiral joins (3 arms, 4 m/s, 20°/s, counter-turning, wider gaps) | bullets | 1 s | 10 | | |
| 23.5 | **Triangle Pods** | three pods 7 m out, 120° apart, the first toward the farthest player | | 2.5 s | | | |
| 27 | **Pruning Arc** | again | | | | | |

- **Overlap:** the spiral always runs, plus up to two other patterns. The 26 Oct rules allow this as long as a route exists.
- **Solo:** pods come one after another, 0.8 s apart, all on you (2 at 3 s, 1 each later; the triangle is sequential). The jet aims at you.
- **Group:** at 8 players, three pods land at a time and each far player gets their own jet.

## Settled for this phase (dated)
- **28 Oct:** the fight is full bullet hell, with as many phases as the other main bosses.
- **28 Oct:** Phase 1 is Petal Spiral and Seed Pods merged into one fight.
- **28 Oct:** no punish window in Phase 1.
- **28 Oct:** the edge curtains became sweeping Waterfall Jets, and the single cone became a front-then-back Pruning Arc with petal fans. Chzy18 found both "not very threatening" before.
- **28 Oct:** Phase 1 accepted.

## Open questions
1. **Bullet hitbox:** the demo uses a 0.4 m player radius and 0.35 m bullets. Keep that, or add a smaller hurtbox for bullets only (a new rule)?
2. **New shared code:** hostile bullets need a shared projectile system (one hit per bullet, cleared on boss death or phase change). Today only player projectiles exist.
3. **Hard and Easy:** proposals in [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md) (one review packet, 31 Oct).
4. **Escape check:** the jets and the Arc side slivers need entries in `tools/encounter-escape/` before any build.
