# Healer implementation and presentation handoff

Implemented in the Unity **class-test arena**, not the absent production U1 combat core. This checkout contains Unity Assets only; the Godot folder in the sibling session-network checkout is the legacy reference. No transport is connected to this simulation. It is local, single-player, with a support training ally. No multiplayer authority/synchronization claim is made.

## Direction (Chzy18, 2026-10-20)

Co-op is the focus; solo viability isn't a current goal. The Healer is a hybrid class that must keep real damage output alongside its healing. Resonance Reclaim (Bellkeeper design reference v1.0, §6.2) is on hold and is not part of this kit.

## Select and play

Follow `class-test-arena.md` to copy Assets/LastBell into a Unity project and use **Last Bell > Play Class Test Arena**. Select **3** or cycle **Tab** to Healer. Move with WASD/arrows or left stick. Toggle **H** for hazards and **U** for the finite 180 HP dummy. **R** resets both actors, effects, resources and cooldowns. The ally is the extra capsule beside the player and remains stationary to create support pressure. A defeated ally stays down until reset; Sanctuary does not revive.

| Slot | Keyboard/mouse | Gamepad | Initial tuning |
| --- | --- | --- | --- |
| Gleam | J / left mouse | South / A | 2 damage, range 420, cooldown 0.6s; every third landed hit grants 1 Focus |
| Mend | K / right mouse | West / X | 2 Focus, range 360, 0.5s rooted cast, 3s cooldown; heal 1 now and 1 at 3s and 6s after completion |
| Sanctuary | L or Q | North / Y | 5 Focus, instant, 20s cooldown; heal 3 to each living friendly actor within 180 of caster |
| Purifying Ward | Space | RB | 1 Focus, range 360, instant, 6s cooldown; absorb 1 damage for 4s and remove Chill |

Auto targeting selects the lowest **absolute HP** eligible actor within range (both have five maximum HP); ties prefer self. HUD buttons select Auto, Self, or Ally. Hold **Left Shift / LB** to force self for Mend/Ward. Explicit invalid targets never fall back to another actor. Selection locks when Mend starts. Sanctuary is caster-centered, making positioning the targeting action. All actions can be held to repeat under the existing slot rules.

The eight requested mechanics are composed into four existing slots: Mend supplies direct healing and regeneration; Sanctuary combines area and emergency recovery; Ward supplies protection and cleanse; Gleam supplies offense and the passive. There is no class progression in this arena, so the full base kit is available immediately. No unrelated upgrade system or extra ability bindings were introduced.

## Rules and initial balance

- Five HP and one-pip hazards every 3.5s are inherited from the arena. Focus starts at 6, caps at 6, and regenerates 1 every 180 simulation ticks on the global clock while the caster is eligible/alive. Every third successful Gleam hit adds 1; healing and shielding never generate Focus. Holding Gleam produces about 3.33 damage/second versus Vanguard's basic 5. Initial values favor support over solo damage and need human feel testing.
- `ClassKits.cs` owns slot range, damage, cast, cooldown and Mend/Sanctuary cost. `HealerRules` in `Healer.cs` owns healing, status durations, Ward cost, passive and regeneration tuning. All timing uses 60 Hz integer ticks.
- Range checks use actor centers and inclusive boundaries. The open arena has no obstacles or line-of-sight system; no artificial LOS restriction is added. Invalid, removed, hostile, dead, non-finite-position or out-of-range targets cannot receive support. Invulnerability prevents damage but does not prevent beneficial effects.
- Costs and cooldowns commit on successful activation (Mend at cast start). Failed validation spends nothing. Full-health healing is allowed and consumes normal costs; events distinguish actual healing from overheal. Ward with no Chill still applies its shield and consumes normal costs. There are no critical heals, healing modifiers, threat or healing scores in the existing arena.
- Basic, Mend and Sanctuary share the existing 18-tick action cadence and Signature > Technique > Basic priority. Mend extends the lockout to its cast end. Ward is independent and resolves before attacks/hazards. Repeated inputs cannot bypass cooldowns or duplicate casts/ticks.
- Movement above the existing 0.2 threshold, successful Ward input, or unabsorbed damage interrupts Mend. Losing target eligibility/range also cancels. Cancellation spends the committed cost and replaces Mend's remaining cooldown with 1s; it produces no healing. Moving is allowed for all instant actions. Sanctuary cannot revive. Existing automatic player respawn after 2s is a training convenience, not a Healer ability.
- Mend refresh **replaces** the prior tick schedule/duration; no stacking or immediate bonus tick. Final tick occurs at the inclusive six-second endpoint, then regeneration expires. Applied regeneration persists beyond cast range. Recipient death/removal clears its effects; caster death/removal clears owned regeneration. Previously applied wards persist on surviving allies until expiration. Class changes/reset clear all effects.
- Ward replaces shield strength and refreshes duration; shields do not add. Immunity/invulnerability is checked before absorption. Shield absorbs first, remaining damage hits HP; a fully absorbed hit does not interrupt, apply Chill or lower HP. Damage attempts grant the inherited 0.75s immunity window. Shield expiration uses an exclusive end tick.
- The healer training encounter adds **Chill**, the only cleanse-eligible effect: unabsorbed hazard damage slows movement to 65% for 5s, refreshing rather than stacking. Ward removes it immediately. Guard, immunity, cooldowns and positive effects are not cleansable. With one eligible effect there is no priority ambiguity. Ally movement is stationary, so Chill is displayed there for cleanse testing; its movement impact is testable on the player.

## Claude handoff: no final artwork included

The view continues to use existing primitives/materials and functional HUD. Gameplay lives in `TrainingSim` and must remain independent of presentation. Consume `Events` using the monotonically increasing `EventSerial`, as `ClassTestArena.ConsumeEvents` already does. The buffer retains 64 recent events; it is a local presentation stream, not a reliable network log. Poll state for initial binding or when events were missed.

Support events expose `Tick`, `Kind`, `Slot`, `SourceId` (1), `TargetId` (1 self / 2 ally), `Effect`, `Amount`, `Overheal`, `Until` and `Text`. Amount is actual healing, shield strength/absorption or signed resource change according to kind. Until is the absolute effect/cast end tick when supplied; zero means absent. Existing offensive events retain `Damage` and `Text` (dummy is not a support actor).

| Event / state | Presentation requirement |
| --- | --- |
| CastStarted, Player.CastUntil, CurrentCastTarget | Mend wind-up, locked target cue, progress over 30 ticks; cancel animation on interruption |
| CastCancelled | Show reason; stop casting effects; no heal impact |
| Healed | Distinguish actual HP restored from Overheal; tick versus direct impact via Effect |
| CastCompleted (Mend) | Direct heal impact plus regeneration attachment through Until |
| CastCompleted (Sanctuary) | Caster-centered radius-180 recovery pulse; individual Healed events identify recipients |
| ShieldApplied / ShieldAbsorbed | Ward attachment, remaining strength, absorption impact; refresh replaces existing visual |
| Cleansed | Chill removal cue on target; no fake cleanse feedback when nothing removed |
| EffectExpired | Remove Ward, Mend or Chill visuals; also poll state after death/reset |
| ResourceChanged / Player.Resource | Focus meter and passive gain cue; no gameplay changes from animation callbacks |
| ActionUsed / ActionBlocked | Gleam projectile/impact; invalid target, range, cast lock, cooldown or cost feedback |
| HazardResolved / KnockedOut / Respawned / Reset | Damage/Chill feedback, dead state, cleanup and training respawn |

Animation states: idle, locomotion, Gleam attack, rooted Mend cast, cast interruption, instant Sanctuary, instant Ward, hit and knockout. Read `Hp`, `Shield`, `ShieldUntil`, `HotNext`, `HotUntil`, `ChillUntil`, `Present` and `Friendly` on actors. Read `ReadyAt`/`CooldownRemaining`, `CastUntil` and Resource on the player. Healing and damage are authoritative in the simulation, never on animation completion. No revival animation is required.

## Verification and limitations

Run from unity-session: `pwsh -NoProfile -File tools/class-test/check.ps1`.

Result: **75 assertions, 11 scenarios, zero failures**, including all prior class checks. Healer checks cover cap/overheal, repeated input, cost/cooldown timing, cancellation, hostile/dead/removed/unknown targets, range revalidation, resource failure, immunity ordering, cleanse eligibility, shield replacement and exact expiration, regeneration refresh/final tick/cleanup, target death, caster despawn, passive generation and a full finite-dummy encounter. The encounter keeps both actors alive through hazards while defeating the 180 HP dummy without invulnerability.

This compiles and executes the scene-independent C# core. **Unity editor rendering, scene compilation, physical keyboard/controller input, HUD layout and human balance have not been verified.** This checkout lacks ProjectSettings/Packages and no Unity editor was found at the standard install locations. Use the editor steps above for that final playtest. No production multiplayer adapter exists here; server validation/reconciliation must be added at that system's integration boundary rather than claiming this local harness is network-ready.

Changed systems: class registration/data (`ClassKits.cs`), simulation dispatch/actor/event state (`TrainingSim.cs`), new support rules (`Healer.cs`), arena input/ally placeholder/HUD (`ClassTestArena.cs`), regression/encounter checks (`ClassTestChecks.cs`), and this handoff. No final character art, icons, animations or VFX were authored.
