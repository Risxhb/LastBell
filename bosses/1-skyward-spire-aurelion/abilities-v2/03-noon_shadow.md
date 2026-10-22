# 03 — Noon Shadow (`aurelion.noon_shadow`) — proposal

**Status:** proposal. It is designed and escape-checked, but not built. The full set is in `ABILITIES.md` (same folder), and the escape check is `tools/encounter-escape/aurelion-v2-escape-check.ps1`.

**Boss:** Aurelion, the Meridian Regent. **Arena:** Skyward Spire, play bound 13.9 m.

## Fantasy and look

Aurelion plants the glaive like a sundial's gnomon, and the halo rises behind him as a noon sun. A long gnomon shadow line falls across the whole dais through the central compass star. Then the side where most of the court stands is flooded with noon light.

- **Telegraph:** the lit half is a coral/amber fill with fine radial "sun ray" hatching. The dividing line is drawn as a crisp gold gnomon shadow. The fill brightens toward noon.
- **Resolve:** a white-gold flash across the half, then gone.

## Shape (Ground)

- **Shape:** half the arena, split by a straight line through the **arena centre**. Build it as `HazardShape.Cone` with apex (0, 0), `HalfAngle` 90° and `Radius` 28 m. The cone's built-in margin handles the line, and no new shape is needed.
- **Which half:** take the direction from the centre to the **mean position of the standing players**; the lit half is the one containing that mean. If the mean is within 1 m of the centre, use the direction to the farthest player.
  - With 2 players on opposite sides, the mean is near the centre, so the farthest player's half is lit.
- **Tag:** Ground (sunlight from above), so Brace doesn't help.

## Timing (Medium)

- **Noon:** the side is picked and **locked at the warning start**. It resolves **3.8 s (228 ticks)** later.
- **Afternoon** (solo every time; everyone in Phase 3): warns on Noon's resolve tick, on the **other** half, and resolves **3.8 s** later.
- **R&S basis:** cleave, minimum 2.2 s and median 4.0 s; R&S picks the side from player positions (`average_pl_pos`).
- **Easy:** ×1.25.

## Damage

**45** (heavy: two of these plus anything else downs a player). No knockback; stun 0.25 s.

## Escape route

| Case | Worst escape | Window | 4.2 m/s walk / walk+dash | 6 m/s walk / walk+dash | Margin at 4.2 m/s |
|---|---|---|---|---|---|
| Half arena | 14.0 m | 3.8 s | 3.58 / 3.14 s | 2.58 / 2.35 s | 0.66 s (needs 1 dash at 4.2; 0.22 s walk-only) |
| With Starfall (edge runner) | 14.2 m | 3.8 s | 3.18 s (dash route) | 2.38 s | 0.62 s |

- **Worst case:** standing at the far edge of the lit half, straight across from the line.
- **Melee threat:** he usually stands near the centre, so melee on the lit side cross a few metres. The punish is losing uptime, and melee who ignore the line take 45.
- **Ranged threat:** **this is a main anti-range move.** A ranged player at the lit rim faces a 14 m run with one dash. Standing at range on the majority side is the worst place on the map.

## Co-op and solo

- **Co-op rule:** the majority side is lit, so the group crosses together. At N = 8 in Phase 2 the group must also spread for Starfall on the safe half.
- **Solo rule:** **Noon and Afternoon.** Your half, then the other half 3.8 s later. You must cross twice.

| N | Behaviour |
|---|---|
| 1 | Noon (your half) → Afternoon (the other half) |
| 2 | Tie → the half holding the farther player |
| 4 | The majority half |
| 8 | The majority half, plus spreading for Starfall afterwards (Phase 2) |

## Phase use

- **Perfect Order:** alone (solo: Noon and Afternoon).
- **Misalignment:** paired with Starfall Mirror. Starfall starts **0.7 s** into Noon, locks 2.0 s later and resolves on Noon's tick.
- **Exposed Meridian:** Noon and Afternoon for everyone.
- **Punish window:** none of its own.

## Escape-check plan (autotest)

- A player standing still on the lit side is hit.
- A player at the far lit edge (13.5 m) runs across with one dash after 0.25 s. They are not hit, and the stamina use is checked.
- N = 2 tie: the half holding the farther player is lit.
- N = 4 with 3 players on one side: that side is lit.
- Solo: Afternoon appears on Noon's resolve tick, on the other half.
- Paired with Starfall: the edge runner clears both.

**R&S source:** §4 block 12 (one-side cleave). §5 proposal 9 (Lopsided Toll).

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
