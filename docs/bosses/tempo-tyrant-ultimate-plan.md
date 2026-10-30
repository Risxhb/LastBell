# Tempo Tyrant: ultimate-difficulty plan

Drafted October 28, 2026 at Risxhb's request. Goal: the Tempo Tyrant is the post-game superboss, on par with an FFXIV Ultimate or a WoW mythic raid boss. Players should struggle and need hundreds of pulls to make progress.

Current kit and numbers: [tempo-tyrant.md](tempo-tyrant.md). Code: `unity/Assets/LastBell/Run/Encounters/TempoTyrant/`.

Sections 1–3 are Risxhb's direct asks and get built first. Section 4 holds proposals that need Risxhb's yes before they're built.

## 0. Rules exception (do this first)

`docs/design/content-rules.md` caps overlaps at **two damaging patterns** and bans **enrage timers**. Only the Bellkeeper Ultimate has an exception. Broken Cadence stacking (section 3) breaks the two-pattern cap.

- Add a **Tempo Tyrant exception** next to the Bellkeeper Ultimate one, worded the same way: any number of overlapping patterns as long as a route exists.
- Keep these rules: every mechanic has a real way to survive, no iframes, no player teleports, each hazard hits a player at most once, no class required.
- The rules doc is shared with Chzy18, so note it as Risxhb's decision with the date.

## 1. Sundering Wake: charge, backswing, charge again

What stays: the charge down a 3 m lane at the farthest player, the fissure along the travelled path, and the Mirrored Wake in Haste.

What's new:

- **Backswing.** When the charge ends, he swings his greatsword back. The hit is a half-circle (about 6 m) behind his landing point, facing back along the charge lane.
  - It is almost immediate: **1 beat in Haste**, about 2 beats in Normal.
  - To keep it fair, it's previewed as a dashed shape at the landing marker from the start of the charge warning. It goes live on arrival.
  - **Who it punishes:** players who sidestep the charge and stay behind him, and melee who chase him down the lane.
- **Haste: charge again straight away.** After the backswing he re-targets the farthest player and charges at once. There is no centre-up and no recovery between charges.
  - Start with **2 charges** in Haste and 1 in Normal.
  - Each charge leaves its own fissure (and mirror), so the lanes can cross. Each fissure is its own hazard.
- **Numbers to start from:**
  - Backswing 26 damage, with knockback.
  - The second charge's warning is shorter: about 3 beats in Haste.
  - Tune both down to the route-check floor.
- **Code:**
  - Split `Wake(bool inChain)` into a charge step that can loop.
  - Add a `Backswing` hazard (a half-disc around his landing point, `Kind = Strike`).
  - Add a `TyrantPose` for the backswing, a placeholder if the rig has no clip.
  - Extend `TempoTyrantRoutes` and `tools/tempo-tyrant/routes_check.py` to cover charge, backswing, charge, plus the fissures.

## 2. Closing Rush: faster and more often

He still rushes the pursuit target when that player stays far away. Starting values:

| Value | Now | New |
| --- | --- | --- |
| `RushTell` | 2 beats | 1 beat |
| `RushSpeed` | 16 m/s | 24 m/s |
| `RushSustainTicks` (far time before a rush) | 90 (1.5 s) | 30 (0.5 s) |
| `RushCooldownTicks` | 8 s | 3 s |
| `RushDist` | 9 m | 7.5 m |

- It can also fire inside attack blocks where he isn't locked in place: between the Haste charges, and during the Slices walk-in.
- **Open question for Risxhb:** the rush does no damage today. Should its stop ring hit? The proposal is a 3 m stop burst for 20 damage, previewed by the existing stop ring.

## 3. Broken Cadence: attacks stack on top of each other

Today, below 20%, each Haste stretch opens with Wake then Severance in order. New behaviour:

- **Attacks overlap.** The next attack starts while the current one is still resolving, about 2–4 beats before its last hit, not after its recovery.
  - Possible pairings: Wake (with backswing) + Crown, Severance + Wake fissures, Crown + Spiral Slices, Severance + Spiral Slices.
- **Never two slice attacks at once.** Spiral Slices and its every-other part count as one slice attack; no second slice attack may start until the first has fully resolved.
- **Everything activates much faster.** Every warning is about 35% shorter than its Haste value. Each warning is then raised back to the route-check floor for that combination, so every pairing still has a route.
- This runs in **both tempos** (Normal too), not only in Haste.
- **Code:**
  - Replace `FrenzyChain()` with an overlap scheduler. It picks the next attack from the allowed pairings and starts it at an offset.
  - Keep the existing rule that the boss never moves mid-Severance.
  - Since Severance locks his position, Wake can't run during a Severance. Pair Severance only with hazards that are already on the floor: a pending fissure, or slices.
- **Routes:** add a combination check for every allowed pairing at 6 m/s walk, sprint and finite stamina. If a combo can't pass, drop it rather than ship it.

## 4. Proposals for ultimate difficulty (need Risxhb's yes)

These are what turn "hard" into "hundreds of pulls". Don't build them until Risxhb approves.

1. **Real HP and length.** HP goes from 2,600 (test value) to a value giving an 8–10 min clear for a good party, with co-op scaling like Aurelion's.
2. **Hard enrage: "Final Measure".** At the time limit, a full-floor hit wipes the party.
3. **Off-Beat vulnerability debuff.** Each hit taken adds +25% damage taken for 8 s, stacking. Sloppy play then becomes lethal: a second Severance hit while debuffed kills.
4. **Lethal failures.** On Hard, Severance strike 3 and a Hollow Crown hit taken while debuffed are one-shots.
5. **Normal stops being a breather.** Normal warnings tighten toward Haste values: the "fully survivable walking" note is dropped.
6. **Unpredictable tempo shifts.** Passage lengths vary (for example 10–16 s), so players read the 3 accent ticks rather than counting.
7. **Phase 2 at 60%.** A new mechanic between the current kit and Broken Cadence, for example a party-wide tempo soak that needs the group on the beat. To be designed with Risxhb.
8. **Tutorial version.** The Tutorial plans a slower Tempo Tyrant. Keep today's constants in one named profile (for example `TempoProfile.Tutorial`), so the ultimate numbers don't erase them.

## 5. Build and test

- Work only in `Run/Encounters/TempoTyrant/`, the Tempo Tyrant docs, `tools/tempo-tyrant/` and the one rules-exception line. Other sessions have uncommitted work in this checkout (Pendulum Sentinel, Chimebound Oracle, Arcanist).
- Test with `Play Tempo Tyrant Practice.cmd` (`-lastbellPractice`). Run the `collab/docs/frame-pacing.md` checks for the new attacks.
- Run only our checks: the Tempo Tyrant autotest plus menu flow. Skip Chzy18's suites.
- There is one shared Unity build lock on this PC, so expect waits.
- Autotest additions:
  - Backswing hits a player behind his landing point and misses one in front.
  - In Haste the second charge re-targets.
  - The rush timing matches the new values.
  - Broken Cadence never runs two slice attacks at once.
  - Every overlap pairing has a route.
- Update `tempo-tyrant.md` and `docs/current-handoffs.md` when done. Don't commit or push unless Risxhb asks.

## Order

1. Rules exception (section 0).
2. Wake rework (section 1).
3. Closing Rush (section 2).
4. Broken Cadence overlap scheduler (section 3).
5. Separate build, then Tempo Tyrant autotest and route checks.
6. Report, with section 4 still waiting on Risxhb's answers.
