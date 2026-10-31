# Sylvara v3: bullet-hell fight (Verdant Chimes boss)

Four phases, back to back with no intermission. There are no punish windows (Sylvara exception in `gpt/08-design-rules.md` §5 and `docs/design/content-rules.md`).

| Phase | HP band | Doc | State |
|---|---|---|---|
| 1 Spiral Bloom | 100–75 % | [P1-SPIRAL-BLOOM.md](P1-SPIRAL-BLOOM.md) | accepted 28 Oct · **built** |
| 2 Two-Colour Choir | 75–50 % | [P2-TWO-COLOUR-CHOIR.md](P2-TWO-COLOUR-CHOIR.md) | accepted 28 Oct · **built** |
| 3 Hollow Bloom | 50–25 % | [P3-HOLLOW-BLOOM.md](P3-HOLLOW-BLOOM.md) | accepted 28 Oct · **built** (bud cutting is a stand-in, see below) |
| 4 Seed Storm + enrage | 25–0 % | [P4-SEED-STORM.md](P4-SEED-STORM.md) | accepted 28/29 Oct · **built** |

Chzy18 accepted the full fight for the Unity build on 29 Oct. The enrage timing was accepted the same day: a step every 24 s, a cap at loop 5 (1:36), and Last Storm at 2:00 of Phase 4, on the same clock for every difficulty.

## Built (Unity, this branch)
- `unity/Assets/LastBell/Run/Combat/BulletSystem.cs` adds shared hostile bullets.
  - Each bullet hits once, with no stun or knockback (`Hazard.Flinch = false`).
  - Bullets can be coloured (Phase 2) and can split (Phase 4 seeds).
- `unity/Assets/LastBell/Run/Encounters/SylvaraEncounter.cs` is the v3 fight:
  - HP 9000 (solo base; Chzy18 asked for lower on 29 Oct, was 13500);
  - rooted at the arena centre;
  - phase changes at 75/50/25 %.
- The autotest `-lastbellSylvara` section covers:
  - every phase's mechanics;
  - colour rules;
  - bud cutting;
  - the enrage steps and Last Storm;
  - single hits;
  - clean-up on death.

## Design only / not built yet
- **Hard and Easy extras** for each phase: proposal in [DIFFICULTY-EXTRAS.md](DIFFICULTY-EXTRAS.md), sent for review 31 Oct. The demo has a Level row (Easy / Medium / Hard).
- **Choir Bud damage.** In Unity, buds are cut by standing within 3 m, as in the demo. Real class damage on buds needs boss parts that can be targeted.
- **HP tuning.** 9000 is the current solo base, not yet tuned against measured damage, and there is no party scaling yet. The target is for a normal party to finish Phase 4 in about 75–90 s.
- **Pod gap tick marks** from the demo.
- **Sylvara's own model and VFX.** She uses the existing rigged stand-in, and bullets are plain emissive spheres.

## Demo
`demo/sylvara-v3-demos.html` is the browser demo used for every decision. It's a single file: open it in a browser. "Full fight" (Z) runs all four phases, and "P4 enrage" (V) has jump buttons for the enrage steps.
