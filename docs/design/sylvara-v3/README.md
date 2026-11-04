# Sylvara v3: bullet-hell fight (Verdant Chimes boss)

Four phases, back to back with no intermission. There are no punish windows (Sylvara exception in `gpt/08-design-rules.md` §5 and `docs/design/content-rules.md`).

| Phase | HP band | Doc | State |
|---|---|---|---|
| 1 Spiral Bloom | 100–75 % | [P1-SPIRAL-BLOOM.md](P1-SPIRAL-BLOOM.md) | accepted 28 Oct · **built** |
| 2 Two-Colour Choir | 75–50 % | [P2-TWO-COLOUR-CHOIR.md](P2-TWO-COLOUR-CHOIR.md) | accepted 28 Oct · **built** |
| 3 Hollow Bloom | 50–25 % | [P3-HOLLOW-BLOOM.md](P3-HOLLOW-BLOOM.md) | accepted 28 Oct · **built** |
| 4 Seed Storm + enrage | 25–0 % | [P4-SEED-STORM.md](P4-SEED-STORM.md) | accepted 28/29 Oct · **built** |

Chzy18 accepted the full fight for the Unity build on 29 Oct. The enrage timing was accepted the same day: a step every 24 s, a cap at loop 5 (1:36), and Last Storm at 2:00 of Phase 4, on the same clock for every difficulty.

## Built (Unity, this branch)
- `unity/Assets/LastBell/Run/Combat/BulletSystem.cs` adds shared hostile bullets.
  - Each bullet hits once, with no stun or knockback (`Hazard.Flinch = false`).
  - Bullets can be coloured (Phase 2) and can split (Phase 4 seeds).
- `unity/Assets/LastBell/Run/Encounters/SylvaraEncounter.cs` is the v3 fight:
  - HP by party size: 4,600 solo up to 18,400 at 4 fighters (accepted 1 Nov; the game is 1–4 players, [HP-SCALING.md](HP-SCALING.md));
  - rooted at the arena centre;
  - phase changes at 75/50/25 %.
- The autotest `-lastbellSylvara` section covers:
  - every phase's mechanics;
  - colour rules;
  - bud cutting;
  - the enrage steps and Last Storm;
  - single hits;
  - clean-up on death.

## Also built
- **Hard and Easy extras** for each phase: accepted 31 Oct ([DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md)). Last Storm is softer on Easy.
- **Choir Buds as boss parts:** 120 HP each, cut by any class's normal attacks; damage dealt to a bud also comes off Sylvara.
- **HP by party size** (accepted 1 Nov, [HP-SCALING.md](HP-SCALING.md)).
- **Model, attack props and animations** (accepted 1–3 Nov, [ATTACK-VISUALS.md](ATTACK-VISUALS.md)), shipped in the `sylvara-assets` 3.0.0 pack, plus her 11 attack icons.

## Not built yet
- **Pod gap tick marks** from the demo.

## Demo
`demo/sylvara-v3-demos.html` is the browser demo used for every decision. It's a single file: open it in a browser. "Full fight" (Z) runs all four phases, and "P4 enrage" (V) has jump buttons for the enrage steps.
