# 02 — Starfall Mirror (`aurelion.starfall_mirror`) — proposal

**Status:** proposal. It is designed and escape-checked, but not built. The full set is in `ABILITIES.md` (same folder), and the escape check is `tools/encounter-escape/aurelion-v2-escape-check.ps1`.

**Boss:** Aurelion, the Meridian Regent. **Arena:** Skyward Spire, play bound 13.9 m.

## Fantasy and look

The halo arcs tilt and point at every player. A falling star is called down onto each of them, and the polished sky dais shows each star's **reflection**: a second star falls on the point mirrored through the arena's central compass star.

- **Telegraph:** a coral/amber disc with a crisp rim and a small gold star glyph in the centre. It follows its player and shows a shrinking "sundial shadow" ring as the lock nears.
- **Mirror circles:** drawn the same way, with a thin porcelain after-image outline round them, so it reads that they belong to someone.
- **Lock:** a bright edge flash plus a chime.
- **Resolve:** a short white-gold star-burst, then gone. No lingering pool.

## Shape (Ground)

- **Star:** a circle on each player, radius **2.2 m** at N ≤ 4 and **1.8 m** at N ≥ 5, so 16 circles fit. Use `HazardShape.Circle`.
- **Mirror star:** a circle of the same radius at the player's position reflected through the **arena centre**, (−x, −z). It tracks and locks with its star.
- **Tag:** Ground, so Brace never helps.

## Timing (Medium)

- **Normal volley:** each star follows its player for **2.0 s (120 ticks)**, **locks**, and resolves **1.1 s (66 ticks)** later. That's 3.1 s total, with the mirror locked at the same moment.
- **Solo second star:** star II starts on star I's lock tick (120), follows for 2.0 s, locks at 240 and resolves at 306. It has its own mirror.
- **R&S basis:** spread circles, minimum 2.0 s and median 4.0 s, tightened because the shape is simple. R&S has this exact pairing as `bp_playermirror_spread`.
- **Easy:** ×1.25.

## Damage

**30** per circle. Standing in two circles (two instances) takes 60. No knockback (Ground ring, nobody pinned); stun 0.25 s.

## Escape route

| Case | Worst escape | Window | 4.2 m/s walk / walk+dash | 6 m/s walk / walk+dash | Margin at 4.2 m/s |
|---|---|---|---|---|---|
| Own star, N 1–4 | 2.50 m | 1.1 s | 0.85 / 0.42 s | 0.67 / 0.42 s | 0.68 s |
| Own star, N 5–8 | 2.10 m | 1.1 s | 0.75 / 0.39 s | 0.60 / 0.39 s | 0.71 s |
| Inside the Horizon, N = 8 on a 6 m circle | 2.10 m | 1.1 s | | | 0.71 s |

- **Worst case:** leaving the centre of your own star, without stepping into a partner's star or anyone's mirror.
- **Melee threat:** the star lands on you wherever you are. With several melee hugging him, stars overlap partners, so they must fan round him.
- **Ranged threat:** same, and a ranged player's mirror lands on the opposite rim, where another ranged player may stand.

## Co-op and solo

- **Co-op rule:** spread so no player stands in another player's star or mirror at resolve.
- **Solo rule:** **two stars in a row** (above). Same skill as a group: leave your star, avoid your own reflection, then leave again.

| N | Behaviour |
|---|---|
| 1 | Star I + mirror, then star II + mirror (sequence) |
| 2 | 2 stars + 2 mirrors |
| 4 | 4 + 4 |
| 8 | 8 + 8 at radius 1.8 m; assign spots on a ring |

## Phase use

- **Perfect Order:** alone.
- **Misalignment:** paired with Noon Shadow (Starfall starts **0.7 s** into Noon, so both resolve on the same tick), and inside the Horizon after Orrery Closing.
- **Exposed Meridian:** Grand Orrery, cast **0.8 s** into Orrery Closing; it resolves inside the Horizon.
- Each pairing is "where to stand" plus "stand apart": at most 2 patterns.
- **Punish window:** none of its own.

## Escape-check plan (autotest)

- A player standing still at their lock point is hit.
- A player walking 2.5 m away after a 0.25 s reaction from the lock tick is not hit.
- Two players 1 m apart: each is hit by the other's star if they don't separate.
- Mirror: a player standing on another player's mirror point is hit.
- N = 8 scripted ring inside an active Horizon: all eight escape.
- Solo: star II spawns on star I's lock tick.

**R&S source:** §4 block 1 (spread) + block 11 (player mirror). §5 proposals 1 and 11.

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
