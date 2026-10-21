# Prototype upgrades — first six

> **Deferred, reference only (October 21, 2026).** The [current game design](https://github.com/Risxhb/collab/blob/docs/boss-rush-decisions/docs/game-design.md) (#29, PR #33) defers upgrades, removes Guard, attack resources and post-hit immunity, and retires the six-upgrade milestone gate. This document has no implementation or acceptance obligation.
>
> Several entries depend on mechanics that no longer exist: Bell-Timed Riposte (Guard), Overflow Bulwark and Close Quarters (resource), and Overflow Bulwark's immunity. If upgrades return, revisit these against the cooldown-only kit. The pipeline rules (trigger → eligibility → limit → effect → log, no recursion) still apply.

Draft v0.1 · October 20, 2026 · Owner: `chzy18/claude` ([#8](https://github.com/Risxhb/collab/issues/8)) · Implementation owner: build system in [#6](https://github.com/Risxhb/collab/issues/6).

Status: **design input only.** These are the six prototype upgrades for the first milestone (plan §12). They are Vanguard upgrades, because Vanguard is the only class in the first fight. They're written against the legacy reference kit, so the numbers can be compared after the C# port:

| Action | Range | Damage | Cooldown | Resource |
|---|---|---|---|---|
| Basic | 155 | 2 | 24 | +1 |
| Technique | 185 | 9 | 48 | −3 |
| Signature | 270 | 18 | 360 | — |
| Guard | — | — | 180 (24-tick window) | — |

Resource is capped at 6, and offensive actions share an 18-tick cadence. All values are tunable.

Each upgrade has to **change a decision during the Bellkeeper fight** (plan §7), not just add a percentage. The "decision" row names that decision, and it's what playtesters should be able to explain afterwards.

## Pipeline rules (all six)

Every effect runs **trigger → eligibility → cooldown/charge check → effect → event log** (plan §7).

- **No recursion.** An effect never triggers an upgrade, except where the "proc interactions" row explicitly allows it. Upgrade-generated damage is not a Basic, Technique or Signature cast.
- **Authority.** Effects are evaluated in the authoritative simulation tick, after the action that triggers them, and recorded in that tick's event log.
- **Mechanics stay separate.** No upgrade turns a failed mechanic into a success. Upgrades can prevent damage, but `mechanic_failed` stays true.
- **Logging.** Each trigger logs one `upgrade_triggered` event with `schema_version`, `generation`, `event_id`, `tick`, `player_id`, `upgrade_id`, `effect` and the values that changed.
- **Reset.** Reset clears all transient upgrade state: charges, stacks, internal cooldowns and pending repeats.

## 1. Bell-Timed Riposte

| Field | Definition |
|---|---|
| Trigger | Guard is active on a tick when **any** boss hazard resolves, whether or not the player is inside it |
| Eligibility | Riposte internal cooldown ready; the player is not knocked out |
| Cooldown / charges | Internal cooldown 360 ticks; holds 1 charge; the charge expires 120 ticks after the trigger |
| Effect | The next Technique within 120 ticks costs 0 resource and deals +5 damage (9 → 14) |
| Proc interactions | The empowered Technique **can** trigger *Steady Stance* bonus damage. It spends no resource, so it **cannot** trigger *Overflow Bulwark* |
| Log | `upgrade_triggered {upgrade_id: "bell_timed_riposte", effect: "empower_technique", expires_tick}`, then `{effect: "consumed"}` or `{effect: "expired"}` |
| Decision changed | Spend Guard on the bell's beat for offence, or hold it as an emergency save. Guard's 180-tick cooldown makes this a real cost. It rewards learning the resolve rhythm without rewarding standing in hazards |

## 2. Echoing Strike

| Field | Definition |
|---|---|
| Trigger | A Signature cast |
| Eligibility | Always |
| Cooldown / charges | None beyond Signature's own cooldown |
| Effect | Leaves a friendly **strike mark** at the player's position. After 90 ticks the strike repeats for 9 damage (50%), but only if the player's centre is within 60 units of the mark and within Signature range of the boss |
| Proc interactions | The repeat is not a Signature cast. It triggers nothing |
| Log | `{upgrade_id: "echoing_strike", effect: "mark_placed", position}`, then `{effect: "repeat_hit", damage}` or `{effect: "repeat_missed", reason: "left_mark"}` |
| Decision changed | Where and when to cast Signature. Casting inside a spot that will be safe 1.5 s later (for example the Hollow Toll hole) pays off. Casting before a Bell Sweep forces a choice between the repeat and safety. It mirrors the boss's own Echo theme |

The friendly strike mark must follow the art guide's friendly-circle rules (§11.5): a thin dotted edge, no floor fill, and the ally glyph. It must never resemble a Tolling Mark.

## 3. Steady Stance

| Field | Definition |
|---|---|
| Trigger | The player has not moved for 30 consecutive ticks (zero movement command after clamping) |
| Eligibility | Not knocked out |
| Cooldown / charges | None; it's a state, not a proc |
| Effect | Basic and Technique deal +1 damage. The bonus persists for 45 ticks after the player starts moving again |
| Proc interactions | Applies to Bell-Timed Riposte's empowered Technique. Doesn't apply to Echoing Strike's repeat |
| Log | `{upgrade_id: "steady_stance", effect: "active"}` and `{effect: "ended"}` when it toggles (not every tick) |
| Decision changed | When to plant and when to reposition. The 45-tick grace lets a player who moves early for a Hollow Toll keep the bonus, which rewards anticipation over last-moment dodging |

## 4. Overflow Bulwark

| Field | Definition |
|---|---|
| Trigger | A Technique that spends resource while the resource is at the cap (6) |
| Eligibility | Bulwark internal cooldown ready |
| Cooldown / charges | Internal cooldown 600 ticks |
| Effect | Grants 60 ticks of damage immunity, using the existing `immune_until` rule. A hazard during this window resolves as `immune` |
| Proc interactions | Not triggered by a zero-cost Riposte Technique. Grants nothing that triggers other upgrades |
| Log | `{upgrade_id: "overflow_bulwark", effect: "immunity", until_tick}`. The resulting resolved event must name the immunity source so the failure card can say "immune (Overflow Bulwark)" |
| Decision changed | Hoard resource to 6 before spending, giving up steady Technique damage for a protective window timed before a hard mechanic |

Failure-card note: the shared event needs an `immunity_source` field (`hit_recovery` or `overflow_bulwark`). Without it, "immune" can't be explained truthfully. This is requested from #6.

## 5. Close Quarters

| Field | Definition |
|---|---|
| Trigger | A Basic hit while the player's centre is within 100 units of the boss |
| Eligibility | Always |
| Cooldown / charges | None |
| Effect | That Basic generates +2 resource instead of +1 (still capped at 6) |
| Proc interactions | Faster resource feeds Overflow Bulwark. Nothing else |
| Log | `{upgrade_id: "close_quarters", effect: "bonus_resource", amount: 1}` |
| Decision changed | How close to stand. Inside 100 units you're already inside the Hollow Toll hole (radius 155), and a Bell Sweep is narrowest near its origin, so sidestepping is short. The cost is that Tolling Mark and Gathering Peal pull you away from the boss, so this build rewards routing straight back to it after each assignment |

## 6. Long Reach

| Field | Definition |
|---|---|
| Trigger | Passive |
| Eligibility | Always |
| Cooldown / charges | None |
| Effect | Basic range 155 → 200, Technique range 185 → 240 |
| Proc interactions | None. Close Quarters still uses its own 100-unit check |
| Log | One `{upgrade_id: "long_reach", effect: "applied"}` event at encounter start, so the failure card and run summary can list active upgrades |
| Decision changed | Whether to stay in melee. With 200–240 range you can keep attacking from a spread position or a stack placed away from the boss, instead of returning to melee between mechanics. It's the opposite pull to Close Quarters, which gives the class two build directions (plan §7) |

## Coverage check

| Decision axis | Upgrades |
|---|---|
| Guard timing | Bell-Timed Riposte |
| Positioning commitment | Echoing Strike, Steady Stance |
| Resource rhythm | Overflow Bulwark, Close Quarters |
| Range and routing | Close Quarters, Long Reach |

There are two build directions: **close and planted** (Close Quarters + Steady Stance + Overflow Bulwark) and **mobile and timed** (Long Reach + Echoing Strike + Bell-Timed Riposte). Each has one defensive interaction (Bulwark or Riposte), and no survival check depends on drawing a particular upgrade (plan §4). Offer generation, rerolls and simultaneous picks belong to #6.
