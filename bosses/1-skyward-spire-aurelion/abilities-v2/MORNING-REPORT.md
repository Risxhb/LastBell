# Aurelion ability set v2: morning report (proposal)

**State:** designed, built, **autotested: 292 passed, 0 failed, 0 errors** (full Aurelion suite, v1 and v2, in the Windows player). **Not human-approved:** nothing is until Chzy18 plays it.

Branch: `feature/aurelion-abilities-v2` (integration). The tested head is local commit `5afa691`, with this report on top.

**The draft PR into `feature/boss-aurelion` is not open yet, and GitHub is not up to date.** Since about 10:50 every push to Risxhb/collab fails with *"remote: Your account is suspended"* (HTTP 403). `origin/feature/aurelion-abilities-v2` stops at `3f3b54e`. The last few commits exist only in the local repo shared by all the worktrees under `last bell/`: the phase-plan test fixes and this report. Once the account works again, run `git push` from `last bell/aurelion-abilities` and open the PR.

## Update: v2.1 (harder, after Chzy18's playtest)

Spec: `ABILITIES-v2.1.md`. **State:** built, **autotested 398 passed, 0 failed, 0 errors** (integrated run 4 at local `4b8e7a3`; report `last bell/aurelion-logs/aurelion-integration/autotest-4/autotest-report.txt`). Escape check passes every case at Easy, Medium and Hard (`escape-check-v2.1-*.md`). Not human-approved.

- **Zenith Collapse** (new, all levels):
  - He walks to the arena centre, then a Ground annulus round him leaves a safe disc of 4.5 m at N ≤ 2 (5.0 m in Phase 3), 5.5 m at N 3–4 and 6.5 m at N ≥ 5.
  - 45 damage. It resolves after 3.0 s (Hard 2.4 s, Easy 3.75 s).
  - It's always paired with Meridian Lances, which resolve 0.3 s after it.
  - Gold orrery rings slide in to the disc edge.
- **Constant layering** on Medium and Hard (Easy keeps v2): two patterns in almost every block, 0.3 s gaps, the 3.5 s punish unchanged. Pairs are timed by resolve tick through each ability's `Duration()`.
- **Hard windows ×0.7**, with these exceptions:
  - Lances volley 2 tracks 1.2 s.
  - The cascade and Orrery stop at the 2.0 s floor.
  - Starfall keeps 1.1 s when paired with Lances.
- Every Hard escape from the worst spot needs a dash or a sprint. The tightest margin is 0.35 s (own Starfall star, with a dash).
- **Playtest:** `LastBell.exe -lastbellBossHp 0.2` (new switch: scales every boss's HP), then practice → "Aurelion (area 1)".
- **Deviation from the spec:** he doesn't cast Starfall during the walk to the centre. It would make three patterns at the start of the Zenith pair, and he only walks once because he stays at the centre.
- **Open (from the spec):**
  - Should Medium also tighten?
  - Is Zenith Collapse always at the centre too predictable?

## How to play it

1. Build the Windows player as usual (`Last Bell > Build` or `RunBuild.BuildWindows`), or open `unity/` in the editor.
2. Run menu → practice → **"Aurelion (area 1)"**. That loads Skyward Spire and fights Aurelion with **ability set v2** (now the default).
3. Launch switches:
   - `LastBell.exe -lastbellAbilitySet v1`: the old five attacks (Meridian Cut, Zenith Pins, Processional Thrust, Crown Reversal, Meridian Alignment), for comparison.
   - `LastBell.exe -lastbellAbility aurelion.<id>`: Aurelion's practice slot fires one ability on its own, on loop. IDs: `aurelion.meridian_lances`, `aurelion.starfall_mirror`, `aurelion.noon_shadow`, `aurelion.orrery_closing`, `aurelion.hour_hands`. Its phase variant follows his HP phase (F8 jumps to the next phase).
   - **F6** in practice cycles the same single-ability loop: off → each ability → off.
   - Difficulty from the run menu. Hard adds Hour Hands inside each Horizon (Phases 2 and 3).
4. Dev keys: F9 defeat, F8 next phase, F6 single-ability loop.

## What's in the build

| Ability | Code | State |
|---|---|---|
| Meridian Lances | `Encounters/Aurelion/Abilities/MeridianLances.cs` | complete: built, all checks pass |
| Starfall Mirror | `.../StarfallMirror.cs` | complete: built, all checks pass |
| Noon Shadow | `.../NoonShadow.cs` | complete: built, all checks pass (stand-in look: no sun-ray hatching yet) |
| Orrery Closing (signature, with the Horizon) | `.../OrreryClosing.cs` | complete: built, all checks pass |
| Hour Hands (Hard only) | `.../HourHands.cs` | complete: built, all checks pass |
| Phase plan, transitions, punish, v1 switch, practice hook | `Encounters/Aurelion/AurelionEncounter.V2.cs` | complete: built, all checks pass |

None are partial or not started. "Complete" means prototype-complete: stand-in telegraphs and reused v1 boss clips, no new art, audio or animation.

The phase plan follows ABILITIES.md §3 exactly:
- Perfect Order: Lances (3 beams) > Starfall > Noon > Orrery > punish.
- Misalignment at 65 %: harmless 1.5 s transition, still form 1. Lances (4 beams, 2 volleys) > Noon + Starfall > Lances > Orrery + Horizon (Starfall inside) > punish.
- Exposed Meridian at 30 %: the **second form** (harmless 1.5 s, same HP bar, no heal). Cascading Lances > Noon and Afternoon > Grand Orrery > punish.
- Hard adds Hour Hands inside each Horizon, which stretches to 6 s.
- The 3.5 s open-chest punish follows each Orrery block.
- A burst through both thresholds plays one transition.

Telegraphs are stand-ins on the shared hazard telegraph (coral/amber, crisp edges, the real hit shape). Gold orrery rings come with Orrery Closing; Noon has a gold gnomon line; Hour Hands goes from a thin gold line to coral at lock; Starfall reflections get a porcelain outline. Boss clips reuse v1's (`MeridianAlignment`, `ZenithPins`, `OpenChest`, `PhaseShift`).

## Test results

**Integrated run 3 at `5afa691`: ALL PASSED: 292 passed, 0 failed, 0 errors logged.**
- Windows player, Unity 6000.6.6f1, `-lastbellAutotest -lastbellAurelionOnly`.
- Report: `last bell/aurelion-logs/aurelion-integration/autotest-3/autotest-report.txt`.

| Section | Passed | Failed |
|---|---|---|
| Aurelion v1 (old set, now behind `-lastbellAbilitySet v1`) | 51 | 0 |
| Meridian Lances | 70 | 0 |
| Starfall Mirror | 30 | 0 |
| Noon Shadow | 35 | 0 |
| Orrery Closing | 47 | 0 |
| Hour Hands | 30 | 0 |
| Phase plan, transitions, punish, practice hook, v1 switch | 29 | 0 |

Each ability's section covers the brief's eight checks:
1. spawn, lock and resolve ticks at Easy, Medium and Hard;
2. escape routes with scripted players at 4.2 and 6 m/s, a 0.25 s reaction, finite stamina and one dash where the brief allows it;
3. a player standing still is hit;
4. a player standing still is hit both next to the boss and at 8 m+ or at the rim;
5. one hit per instance;
6. the solo rule and N = 2, 4 and 8;
7. boss death mid-windup cancels on the same tick;
8. the Easy and Hard differences.

The phase-plan section checks:
- each phase's block order;
- one queued transition at 65 %, and the second form at 30 % with the same HP bar and no heal;
- a burst through both thresholds playing one transition;
- the 3.5 s punish with no live hazard;
- at most two damaging patterns at once;
- Hour Hands on Hard only;
- the Noon + Starfall pairing (both resolve on the same tick, and edge runners clear both with one dash each);
- the `-lastbellAbility` loop for every ability;
- `-lastbellAbilitySet v1`.

The integrated runs were not clean at first. Run 1 at `e63ef17` gave 269 passed and 22 failed; run 2 at `3f3b54e` gave 290 and 8. Every failure was in test code, never in an ability:
- Scripted players couldn't dash: the harness turned off `AbilitiesEnabled`, which also drops dash input.
- Lances read the previous cast's lock tick.
- Noon expected the wrong lit-side angles.
- Orrery's crown walker aimed at a spot inside the crown.
- Hour Hands' solo case stood where the knockback lands between two hours.
- The phase plan's edge runner ran into the boss's body.

**Screenshots and clips:** the six PNGs in the report folder are from the v1 section (phase 1–3, the Alignment rings and the boss-death windup). The v2 checks don't capture screenshots or video yet. The next useful step is a short capture per ability with `RunAutoTest.Capture`.

## Escape check

The design escape check (`tools/encounter-escape/aurelion-v2-escape-check.ps1`, results in `escape-check-medium.md` / `escape-check-easy.md`) passes every case with 0.62–0.84 s of margin at 4.2 m/s on Medium. In the game, each ability's autotest checks the routes with scripted players walking at 4.2 m/s (and 6 m/s), a 0.25 s reaction, and finite stamina (one dash where the brief allows it). Results are under "Test results".

## Conflicts with settled rules

None new. The design's conflicts (ABILITIES.md §7) were kept as written.

## Open questions

From the integration (Abilities session):
1. **Boss position between blocks.** He stays where he is; he doesn't walk toward the party. The Orrery escape check assumes he can be up to 6 m out, so moving him is a design call.
2. **Solo Hour Hands outlasts the 6 s Horizon.** Solo spoke II resolves 4.0 s after Starfall's resolve, about 7.1 s into the Horizon. That is legal (at most two patterns, the Horizon has expired), but the spoke is no longer "inside" it.
3. **Easy keeps the Horizon at 4 s** (as the brief says) while the warnings stretch ×1.25, so on Easy the Horizon covers less of Starfall's warning.
4. The ABILITIES.md §8 questions still stand: the second form at 30 %, HP scaling, Noon's line through the centre or the boss, the Starfall mirror point, Hour Hands on Easy, and whether to keep v1.

5. **Hour Hands: spoke II's snapping to hours leaves gaps.** Beyond about 4.6 m from him a player can stand between two hours, outside the 1.8 m spoke. Solo, spoke I's knockback can land a still player in that gap, so spoke II misses. That matches the brief's geometry. Should spoke II aim at the exact bearing, or should the spoke be wider?

From the ability sessions (details in each brief's build notes):
- **Lances:** the cascade is locked at once and always turns +22.5°. With nobody standing, it aims along his facing.
- **Noon:** the cone reaches the rim + 0.5 m, not 28 m (same hits). The line itself is unsafe (0.3 m margin). Sun-ray hatching and the white-gold flash are not built yet.
- **Orrery:**
  - The Horizon doesn't hit again anyone the rim already hit (one instance).
  - Its expiry uses the shared resolve flash.
  - At 4.2 m/s the worst case needs a dash (as designed).
- **Starfall:** a downed player's star and reflection are withdrawn.
- **Hour Hands:** players who share an hour on the lock share the first spoke; the duplicate spokes are withdrawn.

## Blocked / environment

- **GitHub account suspended** (see the top). The commits are safe in the local repo. Nothing after `3f3b54e` has been pushed, and the draft PR isn't open.
- **Build noise.** One ability branch once committed re-saved scenes, scene `.meta` files with new GUIDs, and URP settings. It was reverted on the integration branch and on that ability branch. Every session now restores those paths after a build.
- **Disk usage.** Noon Shadow's scan found about 80 GB in per-worktree Unity Library caches (2.8–3.7 GB each) across the boss trees. The Skyward Spire session freed about 8.3 GB by deleting its own video-capture frames.

- **Drive C: is full** (between 0.1 and 2.5 GB free overnight). A Unity Library is 3.3 GB and a player build 1.3 GB, so only one worktree in this tree could build.
  - The ability sessions compile-checked with Roslyn (`tools/aurelion/compile-check.sh`).
  - Every Unity run went through the Meridian Lances worktree, the one with a complete Library, on a detached checkout of the integration branch.
- Not deleted, waiting for Chzy18's OK:
  - `aurelion-abilities/unity/Library`, a 1.5 GB half copy that the auto-mode classifier refused to delete;
  - `aurelion-logs/autotest-1..22`, about 9 GB of old v1 runs belonging to the Skyward Spire session.
