# Sylvara v3, Phase 3: Hollow Bloom (second form)

**Status: accepted for now (Chzy18, 28 Oct 2026).** It has been played only in the browser demo (Phase 3 row, "A · Hollow Bloom", in `demo/sylvara-v3-demos.html`). Built in Unity on `feature/sylvara-v3` (see README.md for what is built and what is design only).

## Phase
- **HP band:** set later. Four phases, no intermission (Chzy18, 29 Oct).
- **Second form, "Hollow Refrain":** at the start of the phase her mask splits and her foliage thins, showing the machinery inside. The transformation is harmless: no heal, the same health bar, and it waits for the current attack to finish.
- **Position:** rooted at the centre.
- **No rest:** no punish window (the Sylvara exception in `gpt/08-design-rules.md` §5).
- **Boss parts:** the Choir Buds are boss parts. Damage to a bud also hits Sylvara, so cutting buds keeps the damage going.

## Loop (24 s, repeats), Medium numbers, 100 player HP

| t (s) | Mechanic | Shape | Warning | Damage | Melee threat | Ranged threat |
|---|---|---|---|---|---|---|
| 0 | **The mask splits** | harmless transformation, 1.6 s banner | — | — | | |
| 0 → | **Hollow Spiral** | 4 arms from the open mask, 4.5 m/s, a petal every 0.18 s, gaps every 9th–7th, turning 10°/s | travel | 10 | the arms are tight near her | the arms sweep the outer ring |
| 1 | **Choir Buds, wave 1** | 2/2/3/4 buds (N 1/2/4/8), 10 m out and evenly spaced. Each bud fires its own 3-arm spiral (4 m/s, every 0.3 s, turning 30°/s). Solo: they sprout one after another, 4 s apart | coral ring timer around each bud | 10 per petal | — | someone has to walk out and cut each bud |
| | bud bloom | a bud left alive for 12 s bursts into 20 petals at 5 m/s | the bud's timer ring | 12 | | |
| 8 | **Feet Pod** | 3.0 m circle at her centre, then 12 petals point-blank (as in Phase 1) | 2.5 s | 16 + 12 | yes | the petals reach the wall |
| 12 | **Choir Buds, wave 2** | as wave 1 | | | | |
| 15 | **Pruning Arc** | a 120° cut in front, then 120° behind 0.8 s later, 8 m long, each throwing a fan of 9 petals (as in Phase 1) | 2.0 / 2.8 s | 20 + 12 | the side slivers are the only safe ground | the petal fans |
| 20 | **Seed Pods** | on the farthest 1/1/2/3 players (as in Phase 1) | 2.5 s | 16 + 12 | | dropped on you |

- **Cutting a bud:** the demo cuts a bud at 30/s while you stand within 3 m (100 HP). In Unity (built 31 Oct) buds take real class damage: each bud is a boss part with 120 HP, the nearest live part takes the hit (melee, lines and projectiles alike), and every point dealt to a bud also comes off Sylvara. Standing next to a bud no longer cuts it. Party bots walk out and attack buds. The autotest checks that one player attacking a bud cuts it in under 6 s.
- **Peak density in the demo:** about 150–220 bullets.
- **Difficulty in the demo:** it was eased twice on 28 Oct and now sits at or below Phase 2. Chzy18 called the first versions "insane".

## Players
- **Solo:** buds sprout one at a time, 4 s apart, so you cut them in order. Pods come one after another, all on you.
- **Group:** buds are spread evenly around the wall, so the party splits up to cut them. At 8 players, 4 buds per wave, each with its own spiral.

## Settled for this phase (dated)
- **28 Oct:** Phase 3 is Hollow Bloom (option A). The others were Mirror Choir, Root Cage and Shattered Refrain.
- **28 Oct:** eased twice ("seem insane", "still seems hard"): 8 → 6 → 4 arms, buds bloom after 12 s, and a bloom is one 20-petal ring.
- **28 Oct:** accepted for now.

## Open questions
1. **Bud HP and placement:** fixed 10 m from the centre, or anchored to the arena's root pillars?
2. **Uncut buds:** today a bloom is just one petal ring. Should an uncut bud also make her spiral faster for the rest of the loop?
3. **Hard and Easy:** accepted 31 Oct, see [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md); built in Unity.
