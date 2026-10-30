# 01 — Meridian Lances (`aurelion.meridian_lances`) — proposal

**Status:** proposal. It is designed and escape-checked, but not built. The full set is in `ABILITIES.md` (same folder), and the escape check is `tools/encounter-escape/aurelion-v2-escape-check.ps1`.

**Boss:** Aurelion, the Meridian Regent, 4.2 m tall. **Arena:** Skyward Spire, play bound 13.9 m; the boss stays inside 9.5 m and his body radius is 1.1 m.

## Fantasy and look

Aurelion lifts his clock-hand glaive to the zenith and the halo arcs align. Meridian lines of light snap across the whole dais like a star chart being drawn: thin gold guide lines first, then the full beams. Each beam is one long straight line through his body, reaching both rims.

- **Telegraph:** a coral/amber lane fill with crisp edges and fine star-chart hatching inside. While the aim is still tracking, the beams show as thin gold guide lines with dim fill. At the lock they snap to full width with a bright edge flash and a tick sound.
- **Resolve:** a white-gold beam flash along each lane, gone in 0.2 s.

## Shape (Strike)

- **Beams:** 3 beams in Phase 1 (60° apart), 4 beams from Phase 2 (45° apart).
- **Each beam:** a full-length lane 2.2 m wide through the boss centre, running both ways to past the rim. Build each beam as two `HazardShape.Lane` instances with opposite yaws from the boss centre, 28 m long, width 2.2 m. **Each beam pair must count as one instance per player.** Either share one `Hit` set across the two halves, or make one hazard with a custom `Contains` in your class. A player is hit at most once per beam, and at most once per volley if you merge the beams. Your call; write it down.
- **Tag:** Strike, so Brace mitigates it.

## Timing (Medium; 60 Hz ticks)

- **Volley 1:** the beams are aimed so **one beam runs through the farthest player**. The aim tracks that player's angle for **1.2 s (72 ticks)** at most 120°/s, then **locks** and resolves **1.2 s (72 ticks)** later. That's 2.4 s total.
- **Volley 2** (Phase 2+): warns on volley 1's resolve tick. One beam runs through the **nearest** player. It tracks for **1.0 s (60)**, locks, and resolves **1.2 s (72)** later. That's 2.2 s total.
- **Cascade** (Phase 3, second form): volley 2 is instead **rotated 22.5° from volley 1** and does not track. It shows locked at once, and is still 2.2 s total with a 1.2 s window.
  - The new safe wedges sit on volley 1's just-resolved beam lines. Players far out must move about 5 m sideways, which is the anti-range half of this move.
- **R&S basis:** cardinal lasers, minimum 2.0 s and median 4.0 s. The total stays at or above 2 s (settled). The tighter number is the lock window.
- **Easy:** every time ×1.25. **Hard:** no change (the Hard mechanic is Hour Hands).

## Damage

**35** at Medium. Stun 0.25 s and the standard 1.5 m knockback, pushing sideways out of the lane (`Hazard.PushDir` for lanes).

## Escape route (from `escape-check-medium.md`)

| Case | Worst escape | Window | 4.2 m/s walk / walk+dash | 6 m/s walk / walk+dash | Margin at 4.2 m/s |
|---|---|---|---|---|---|
| 3 beams | 1.70 m | 1.2 s | 0.65 / 0.36 s | 0.53 / 0.36 s | 0.84 s |
| 4 beams (volley 1, volley 2, cascade) | 2.40 m | 1.2 s | 0.82 / 0.41 s | 0.65 / 0.41 s | 0.79 s |

- **Worst case:** standing against his body between two beams. With 4 beams no point within about 3.7 m of his centre is safe, so melee must step out to the wedge gaps and come back.
- **Melee threat:** every beam crosses at his body. Hugging him is always inside a beam. Move about 2.4 m out to the middle of a wedge.
- **Ranged threat:** volley 1 always runs through the farthest player, rim to rim. Sidestep 1.4 m. In the cascade, a player in a far wedge must move about 5 m sideways onto the old beam line. Ranged casters time their stationary casts between volleys.

## Co-op and player counts

Not a co-op mechanic: the rule is positional. The aim uses the farthest and nearest players.

| N | Behaviour |
|---|---|
| 1 | Volley 1 through you, volley 2 also through you (you are the nearest) |
| 2 | Volley 1 through the farther player, volley 2 through the nearer one |
| 4 | Same rule; more bodies to keep out of the beams |
| 8 | Same; the wedge gaps near him get crowded, so melee must rotate together |

## Phase use

- **Perfect Order:** 3 beams, 1 volley.
- **Misalignment:** 4 beams, 2 volleys, and later a 4-beam single volley.
- **Exposed Meridian:** cascade.
- Never paired with another damaging pattern.
- **Punish window:** none of its own. The loop's punish comes after Orrery Closing.

## Escape-check plan (autotest worst cases)

- A scripted player stands against his body on a beam line, then walks to the wedge centre at 4.2 m/s after a 0.25 s reaction, starting at the lock tick. It must not be hit.
- A player standing still on the farthest-player beam at 12 m is hit.
- For the cascade: a player at 12 m in a volley 1 wedge walks sideways to the old beam line after volley 1 resolves (one dash allowed). It must not be hit.

**R&S source:** §4 block 14 (rays and lasers, cardinal crosses). §5: none directly (this is Aurelion's row).

## Build notes (built and autotested, not human-approved)

Code: `Encounters/Aurelion/Abilities/MeridianLances.cs`. Checks: `RunAutoTest.Aurelion.V2.MeridianLances.cs`.

- **One hit per beam:** each beam is two `Lane` hazards (28 m × 2.2 m) from his centre with opposite yaws, so Brace sees every hit as coming from him. Both halves resolve on the same tick. Whichever half is tested first copies its hit set into the other half in its `OnResolve`, which runs before the other half's hit test. A player is therefore hit at most once per beam. Beams are separate instances, not merged per volley. Where two beams cross is inside his body, which players can't enter. No shared-system change was needed.
- **Tracking:** all beams of a volley share one aim. It turns at most 2° per tick (120°/s) toward the nearest beam line to the target, since the lines repeat every 180/beams degrees. Origin follows his position until the lock.
- **Tick convention:** this follows `HazardSystem`. A volley spawned on tick t tracks on ticks t…t+71, locks on t+71 and resolves on t+143. It is warned for 144 ticks (2.4 s), with exactly 72 ticks from the lock to the resolve. Volley 2 spawns on volley 1's resolve tick. `Run` returns on the last resolve tick.
- **Look:** the shared telegraph is used with no new art. It shows dim before the lock, gives a lock flash, then a 0.2 s resolve flash. The boss plays the v1 `MeridianAlignment` clip as a stand-in for "glaive raised to the zenith". The HUD reads "Cascading Lances" for the Phase 3 variant.

### Open questions (calls made overnight)

1. **Cascade lock:** volley 2 of the cascade is locked from the moment it appears (`LockAt 0`) and resolves 2.2 s later. "1.2 s window" was read as the design figure for reaching the old line after volley 1 resolves. The alternative was to show it as guide lines for 1.0 s before snapping.
2. **Cascade direction:** the cascade always rotates +22.5° (clockwise seen from above). Should it alternate or pick a random side?
3. **No targets:** with no standing player, the aim falls back to his facing.
4. **Turn-rate cap with 3 beams:** the 60° spacing means the aim never has to turn more than 30°. A player who crosses to a new wedge simply hands the aim to the next line.

---

## How to build it (ability session)

You are the session for this one ability. Work only on this ability. Your parent is the **Abilities** session (`abilities-aurelion`), which owns `feature/aurelion-abilities-v2` and integrates the five abilities.

**Setup**
- Work in your own worktree, branched from `feature/aurelion-abilities-v2`. Commit on **`feature/aurelion-ability-<id>`**, where `<id>` is the ID after `aurelion.`.
- The Abilities session puts a skeleton on `feature/aurelion-abilities-v2` before it starts you:
  - `AurelionAbility` (base class)
  - the v2 scheduler in `AurelionEncounter`
  - the `-lastbellAbility <id>` practice hook
  - a scripted-player test helper
- Implement your ability in **`unity/Assets/LastBell/Run/Encounters/Aurelion/Abilities/<AbilityName>.cs`**, one class, so ability sessions never edit the same lines.
- If you truly need a shared-system change, such as a new hazard shape, put it in a small separate commit and tell the Abilities session. The only shared files you may touch are your own section of `RunAutoTest.Aurelion.V2.<AbilityName>.cs` and the registration line the skeleton marks for you.

**Mechanics**
- Use the shared hazard system only. That means `Hazard`, `HazardShape` (Circle, Ring, Cone, Lane) and `HazardSystem` on `CombatWorld`, with one hit per instance, `Hazard.Margin` 0.3 m, `HitOnResolve`, `ActiveTicks` and `HazardSystem.Cancel`. Don't build a parallel combat system.
- The game runs at 60 Hz. Convert seconds to ticks with `Sim.Ticks(s)`. Read the difficulty from `CombatWorld.Difficulty`: Easy ×1.25 on every warning, track and window.
- Hostile telegraphs are coral or amber, with crisp edges, drawn from the real hit shape, in the area's look (§ Look). Use simple stand-in meshes or particles if no art exists.

**Practice hook:** `LastBell.exe -lastbellAbility aurelion.<id>` (and the dev key the skeleton provides) fires your ability on its own, repeatedly, in Aurelion's practice slot on Skyward Spire.

**Autotest checks.** Add them in your own partial file, `RunAutoTest.Aurelion.V2.<AbilityName>.cs`, called from the skeleton:
1. the hazard spawns, locks and resolves on the right ticks
2. the escape route works at 4.2 m/s with finite stamina (a scripted player that walks, plus one dash where this brief says so)
3. a player standing still gets hit
4. a scripted player next to the boss and one standing still at long range (8 m+, and at the arena edge) both get hit if they don't move
5. one hit per instance
6. the solo rule and the N = 2 / 4 / 8 rules, using scripted players from the helper
7. boss death mid-windup cancels the hazard on the same tick
8. the Easy and Hard differences

**Builds and handoff**
- **Unity lock:** only one Unity build or test runs at a time, machine-wide. Before any Unity command, `mkdir "C:\Users\BS_Ch\Documents\Codex\last bell\.unity-build-lock"`. If that fails, wait 60 s and retry. Write your session name into `owner.txt` inside it, and remove the folder when you finish, including on failure. A lock older than 45 minutes whose owner is idle may be broken; say so in your report.
- **Build and test:** `aurelion-logs/build-and-test.sh <n> [-lastbellAurelionOnly]` shows the pattern. Copy it and point it at your worktree. In a fresh worktree, run `tools/asset-packs/Get-AssetPacks.ps1` once. Copying `run-loop/unity/Library` into your worktree's `unity/` folder makes the first import fast.
- When your ability is ready, push your branch and **SendMessage the Abilities session** with your test results.

## Rules for every session in this tree

- **Settled rules in `gpt/08-design-rules.md` win:**
  - no iframes, grace windows or post-hit immunity
  - no block, parry or counter (Brace is −60 % on Strike only)
  - no teleporting players
  - at most two damaging patterns at once
  - one hit per hazard instance
  - no enrage timers, and pools always expire
  - every co-op attack has a written solo rule
  - empty soaks wipe the party, and their telegraph must be impossible to miss
  - no encounter requires a specific class
  - every attack has a real escape route at 4.2 and 6 m/s
  - melee and ranged must both be in danger: most attacks threaten both, at least one punishes staying at range, and no spot is permanently safe (§5)
- **Stay in your lane:**
  - Don't edit other bosses' branches, `feature/run-loop` directly, the class kits, class rigs or animation, the main menu, or networking.
  - Shared-system changes go in a small separate commit that the Abilities session reviews.
- **Unity builds are heavy and many sessions share one PC.** Take the lock above before any Unity command.
- **Large binaries** (GLB, big PNG, video) go through asset packs (`docs/asset-packs.md`), not git.
- **Build and test in your own worktree,** never in a checkout another session is using.
- **Commits:**
  - Commit only your own changes, with `git -c user.name=Chzy18 -c user.email=345140526+Chzy18@users.noreply.github.com`.
  - Push often. Pushing is pre-authorized.
  - Keep "designed", "built", "tested" and "human-approved" as separate states. Nothing is human-approved until Chzy18 plays it.
- **Usage:** if you run out of usage, the session pauses until the limit resets. Commit and push before every long step.
- **Chzy18 is asleep.** Don't stop to ask questions: make the call, write it under "Open questions", and keep going.
- **End with a short report:** Completed / Next / Blocked, with evidence such as test counts and screenshots.
