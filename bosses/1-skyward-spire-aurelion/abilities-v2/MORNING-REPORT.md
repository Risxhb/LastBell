# Aurelion ability set v2: morning report (proposal)

**State:** designed, built, compile-checked. **Unity autotest: see "Test results".** Nothing is human-approved until Chzy18 plays it.

Branch: `feature/aurelion-abilities-v2` (integration). Draft PR into `feature/boss-aurelion`: see the PR link in the Abilities session.

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
| Meridian Lances | `Encounters/Aurelion/Abilities/MeridianLances.cs` | built, checks written |
| Starfall Mirror | `.../StarfallMirror.cs` | built, checks written |
| Noon Shadow | `.../NoonShadow.cs` | built, checks written |
| Orrery Closing (signature, with the Horizon) | `.../OrreryClosing.cs` | built, checks written |
| Hour Hands (Hard only) | `.../HourHands.cs` | built, checks written |
| Phase plan, transitions, punish, v1 switch, practice hook | `Encounters/Aurelion/AurelionEncounter.V2.cs` | built, checks written |

The phase plan follows ABILITIES.md §3 exactly:
- Perfect Order: Lances (3 beams) > Starfall > Noon > Orrery > punish.
- Misalignment at 65 %: harmless 1.5 s transition, still form 1. Lances (4 beams, 2 volleys) > Noon + Starfall > Lances > Orrery + Horizon (Starfall inside) > punish.
- Exposed Meridian at 30 %: the **second form** (harmless 1.5 s, same HP bar, no heal). Cascading Lances > Noon and Afternoon > Grand Orrery > punish.
- Hard adds Hour Hands inside each Horizon, which stretches to 6 s.
- The 3.5 s open-chest punish follows each Orrery block.
- A burst through both thresholds plays one transition.

Telegraphs are stand-ins on the shared hazard telegraph (coral/amber, crisp edges, the real hit shape). Gold orrery rings come with Orrery Closing; Noon has a gold gnomon line; Hour Hands goes from a thin gold line to coral at lock; Starfall reflections get a porcelain outline. Boss clips reuse v1's (`MeridianAlignment`, `ZenithPins`, `OpenChest`, `PhaseShift`).

## Test results

_Filled in after the integrated Unity run (see below)._

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

- **Drive C: is full** (between 0.1 and 2.5 GB free overnight). A Unity Library is 3.3 GB and a player build 1.3 GB, so only one worktree in this tree could build.
  - The ability sessions compile-checked with Roslyn (`tools/aurelion/compile-check.sh`).
  - Every Unity run went through the Meridian Lances worktree, the one with a complete Library, on a detached checkout of the integration branch.
- Not deleted, waiting for Chzy18's OK:
  - `aurelion-abilities/unity/Library`, a 1.5 GB half copy that the auto-mode classifier refused to delete;
  - `aurelion-logs/autotest-1..22`, about 9 GB of old v1 runs belonging to the Skyward Spire session.
