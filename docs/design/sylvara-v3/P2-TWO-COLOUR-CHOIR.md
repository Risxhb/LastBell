# Sylvara v3, Phase 2: Two-Colour Choir

**Status: accepted (Chzy18, 28 Oct 2026).** It has been played only in the browser demo (option 7, "B · Two-Colour Choir", in `demo/sylvara-v3-demos.html`). Built in Unity on `feature/sylvara-v3` (see README.md for what is built and what is design only).

## Phase
- **HP band:** set later. Four phases, no intermission (Chzy18, 29 Oct).
- **Theme:** Awakening Bloom. Her flowers open, and her bells split into a brass choir and a cobalt choir.
- **Position:** rooted at the centre, as in Phase 1.
- **Colours:** at the start, players alternate brass and cobalt by slot. **Only bullets of your own colour hurt you.** Bullets of the other colour are drawn faded. A toll swaps every player's colour.
- **Punish:** none. Chzy18 removed punish windows from the whole fight on 28 Oct, which is an exception to the rule in `gpt/08-design-rules.md` §5.

## Loop (21 s, repeats), Medium numbers, 100 player HP

| t (s) | Mechanic | Shape | Warning | Damage | Melee threat | Ranged threat |
|---|---|---|---|---|---|---|
| 0 → loop end | **Choir Spiral** | 6 arms from her rim, alternating brass and cobalt; 5 m/s, a petal every 0.13 s, gaps every 9th–7th; turns 14°/s | travel | 10 | 3 of the arms are always your colour, tight near her | the arms sweep the outer ring |
| 0 → 10 | **Brass lanes, north** | parallel bullet lanes 2.4 m apart, the whole arena, travelling south at 6 m/s, a bullet every 0.25 s, drifting 0.4 m/s | travel | 12 | the lanes run through her | the lanes reach the wall |
| 6 → 20 | **Cobalt lanes, east** | as above, travelling west | | 12 | | |
| 10 → loop end | **Second set** | 4 arms, cobalt/brass, 4 m/s, a petal every 0.18 s, counter-turning at 18°/s, wider gaps | travel | 10 | | |
| 12 | **Root ring** | 3.5 m disc at her feet | 2 s | 18 | yes | — |
| 14.5 → 20 | **Brass lanes, south** | as above, travelling north. The north lanes have cleared by then, so a colour never has two lane sets in the same direction. | | 12 | | |
| each toll | **Toll** | everyone's colour swaps: a 1 s warning (your ring blinks the next colour), then a **Toll Burst** of 20 petals at 5 m/s from her rim, alternating colours | 1 s | 12 | point-blank | the ring reaches the wall |

- **Tolls:** 5, 10, 15 and 20 s in groups; 4, 8, 12, 16 and 20 s solo.
- **Peak density:** about 470 bullets at once in the demo, at 1, 4 and 8 players.
- **Always a route:** each colour has lanes in only one direction at a time, so there's always a column between your colour's lanes. Your colour's arms pass through that column, so you weave along it.

## Players
- **Solo:** your colour swaps every 4 s instead of 5, so there's one more toll burst. Everything else is the same.
- **Group:** half the party is brass and half cobalt. At 8 players, four bodies share each colour's safe columns. Toll swaps flip which columns are safe for everyone at the same moment.

## Settled for this phase (dated)
- **28 Oct:** Phase 2 is the Two-Colour Choir (option B). Chzy18 picked it over Choir Buds, Seed Relay and Hush and Chorus.
- **28 Oct:** made harder with more projectiles: 6 arms, a second set, three lane sets and toll bursts.
- **28 Oct:** Phase 2 accepted.
- **28 Oct:** no punish window in Phase 2 or anywhere else in the fight ("take the punish mechanic out … no more").

## Open questions
1. **Colour readability in Unity:** brass vs cobalt has to read at the gameplay camera next to the Verdant lighting and the coral hostile colour. Every bullet still needs a coral rim to say "hostile".
2. **Down players:** a downed player keeps their colour. Do tolls skip them?
3. **Hard and Easy:** proposals in [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md) (one review packet, 31 Oct).
4. **Unused options:** Choir Buds, Seed Relay and Hush and Chorus are still in the demo. They are spare parts (no intermission, 29 Oct).

## Playtest change (29 Oct, Unity build)
Chzy18 played the Unity build: "slow the ball down a little bit in p2 and space them out more so you can run in between". Choir lanes now travel at 5 m/s (was 6), sit 3.4 m apart (was 2.4) and fire every 0.35 s (was 0.25). The colour spirals are 4.2 and 3.5 m/s (were 5 and 4), firing every 0.17 and 0.22 s (were 0.13 and 0.18). The toll burst moves at 4.5 m/s (was 5).
