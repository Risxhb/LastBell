# U3 0.1 review proposal — foundation #6

October 21, 2026. Owner: risxhb/codex. Status: **review proposal; no consumer adoption or multiplayer implementation**. Review target: draft [#42](https://github.com/Risxhb/collab/pull/42), `18e89ed264eb3ba2cd0f18b50dfb7eaacc6f3149`. This answers [Claude's R1–R21](https://github.com/Risxhb/collab/issues/6#issuecomment-6308571871) and tasks 3, 5 and 6 in [the task list](https://github.com/Risxhb/collab/issues/6#issuecomment-6311643559). It supersedes legacy Guard/counter/iframe contracts; it does not adopt prototype balance.

The companion [U3.Contracts.cs](U3.Contracts.cs) is a dependency-free, representative C# type surface. [Fixtures.cs](Fixtures.cs) exercises identity, input, hazard timing, event accounting and checkpoint header rejection using that same surface. Neither file implements a combat simulation, geometry engine, complete restore validator, wire codec or Unity adapter. The typed checkpoint describes the required complete state, not a claim that #42 can restore it. A consumer must reject unsupported capabilities and unknown schema versions explicitly.

## R1–R21 disposition

“Accept” means accepted in this proposed foundation interface, subject to consumer review and implementation gates below. It does not mark an encounter, its numbers, or its Unity implementation accepted.

| Request | Decision | Contract and reason |
| --- | --- | --- |
| R1 circle | Accept | Circle is a shared shape for Shattered Chime; no view-only approximation for damage. |
| R2 requirement | Accept `Avoid`; defer stack/spread/shelter; reject Guard | Bellkeeper only needs avoid. Group checks need party-count, eligibility and assignment policy tests before binding; no unused Guard mechanic. |
| R3 lock=resolve | Reject/withdrawn | Telegraph attacks require `warning <= lock < resolve`. Instant authored damage uses a distinct event, never a zero-warning danger shape. |
| R4 retarget/cancel | Accept | Provider proposes changes before lock; authority validates and emits history. Sweep retargets deterministically to an eligible standing player; Chime cancels on owner loss. After lock freeze geometry; loss of owner cancels assignment requirements, not already locked avoid geometry. |
| R5 presentation marker | Accept | Separate Marker record with ID, position/target and lifetime. Cannot cause collision, damage or eligibility. Memory of Midnight remains an optional content decision. |
| R6 encounter RNG | Accept | Independent seeded stream with algorithm/version, state and draw count in checkpoint; combat draws never alter encounter targeting. |
| R7 active flag | Accept | SessionActive differs from CombatActive; both are authoritative, checkpointed. Paused/lobby/intermission/outcome cannot accept offensive actions. |
| R8 resolved records | Accept | Typed resolution carries context, frozen geometry, warning boss pose, resolve poses/height/dodge, actual outcome/consequences, assignments and Echo source. No “guarded/immune” legacy enum. |
| R9 immunity_source | Reject/withdrawn | No invulnerability source. Brace and Anchor are finite mitigation/absorption, recorded separately. |
| R10 lane/rectangle | Accept | Oriented rectangular lane using origin/forward, length and half-width. Low bar is this shape plus height eligibility. |
| R11 mutable boss | Accept | Boss position/facing/body are shared mutable authoritative state. Boss-anchored warning captures its pose; later movement does not drag a frozen telegraph. |
| R12 HP phases | Accept | Provider receives HP fraction and current/pending phase. Threshold crossing queues phases; commit at authored safe block boundary with phase event. Multiple thresholds cannot lose later transitions. |
| R13 damage | Accept | Per-instance requested damage against configurable MaxHp (baseline 100); actual HP loss is capped, mitigation/absorption/overkill explicit. Values remain prototype. |
| R14 vulnerability | Accept | Typed recovery/vulnerability state with begin/end tick and event; presentation cannot decide damage multipliers. |
| R15 counterable | Reject/withdrawn | No counter/parry/old Guard loop. |
| R16 once per attack/player | Accept | Dedup by attempt + attack instance + target actor. Record successful resolution even if shield absorbs all; a distinct attack can hit in the same tick. No post-hit immunity. |
| R17 stun/knockback | Accept | Author duration/vector/distance; default 15 ticks and 1.5 m away from locked origin. Resolve collision through shared movement; record requested and actual displacement. Brief tuning remains reviewable. |
| R18 actor life | Accept | Standing, Downed, Inactive in provider context; only standing active players can receive damage/assignments. Downed is not disconnected. |
| R19 jump | Accept | Non-clearable by default. Only explicitly clearable hazards compare authoritative foot height to authored clearance at resolution; jumping never ignores ordinary attacks. |
| R20 boss body | Accept | Radius, solid flag and charge trajectory authoritative; shared swept body collision and wall limits include charges. No teleport-through-body shortcut. |
| R21 movement | Accept delegated #33 defaults | 6 m/s walk, 9 sprint, 3 m/12-tick dash; #42 documents these defaults but its dash currently applies 13 increments (see [run review](https://github.com/Risxhb/collab/blob/archive/docs-foundation-u3-review-20261021/docs/reviews/run-loop-42.md)). Superseded 4.2/4.5 test values are not U1. Full table below becomes shared tuning data for game and validator. |

## Units, clock and ordered step

Metres on planar X/Z; `Planar.Z` maps to Unity z. Height is metres above the floor; yaw is represented by a unit planar forward vector. Time is integer ticks at **60 Hz**; snapshot tick is the last completed tick. Reset begins at tick 0. Positive duration conversion is `ceil(seconds * 60)` with authored integer ticks preferred. Do not use frame delta or mixed `RoundToInt` policies for authority. Per-step movement is speed/60. No cross-platform bit-identical float promise is made; host authority owns results.

Starting tuning (delegated/prototype, publish once and consume in runtime and escape validator):

| Field | U1 starting value |
| --- | --- |
| Walk / sprint | 6 / 9 m/s |
| Dash | 3 m uniformly over 12 ticks (15 m/s), collision can shorten it; 4-tick recovery proposal |
| Stamina | Max 100; sprint 25/s only while actually requesting eligible movement; jump 20, dash 30 |
| Regeneration | 20/s after 45 complete ticks without spending; never exceed maximum |
| Jump | 36 ticks, peak 0.8 m; `height = 4*peak*u*(1-u)`; no general immunity |
| Player body / arena | radius 0.45 m; circular actor-centre bound 11.5 m matching #42; equivalent physical floor radius 11.95 m; boss-centre bound 9.5 m |
| Hit | baseline 15 ticks stun; 1.5 m knockback over 9 ticks, constrained by arena and solid boss |
| Ability input buffer | proposal: 9 ticks (0.15 s); one queued press, newer press replaces older with explicit Superseded result |

The actor-centre bound is intentional compatibility with #42, rather than accidentally subtracting radius twice. Content may supply a smaller arena. Shared collision must keep both boss separation and the outer bound true after push-out (including boss near wall); iterate/sweep to a valid solution or reject an impossible spawn. Do not clamp wall then push outside it. Dash must move exactly 12 increments, not one increment on activation plus twelve later. Jump lands at tick 36; stun/knockback do not pause airborne progression.

Proposed single-tick order, to be agreed before adapting #42:

1. Validate session/attempt and owned input; decrement/expire timers due at tick start (including seal expiry); select new actions deterministically by actor ID and per-player request sequence. Missing input is neutral, never stale held movement.
2. Apply eligible dash/jump/movement and boss movement with shared continuous collision. Down/inactive actors cannot act. Advance class casts and contacts; commit costs/cooldowns at authored release, never animation events. Resolve player damage to boss in stable attack-ID order; queue death/phase transitions rather than mutating live collections while enumerating.
3. Run provider against a detached post-movement context; validate returned authoring commands. Apply pre-lock changes, capture lock geometry, then resolve due hazards in stable instance/target order. Already-dead bosses schedule no new attacks; cancel their pending hazards before this resolve stage, consistent with #42's windup-death policy.
4. Apply damage, finite mitigation/shield, stun and collision-constrained knockback; emit records. Evaluate downs/party wipe and victory. Defeat has precedence if both results occur. Commit queued phase/block transitions and lifecycle changes; dispatch detached events after collection iteration finishes.
5. Commit checkpoint/event watermark and frozen segment end if applicable. Tick advances once, including inactive steps; no combat timer or DPS interval advances while combat is inactive.

This ordering preserves #42's “boss killed during windup cancels pending hazard” behavior. Simultaneous lethal player/boss effects already resolved before cancellation produce defeat. If a consumer wants different simultaneous-hit semantics, it must propose that change and fixtures, not depend on Unity update order.

## IDs, commands and results

`SessionId` identifies the match; `PlayerId` identifies roster membership independent of connection; `ActorId` identifies the current spawned actor; `RunId` identifies a run/history grouping; `AttemptId` increments on combat reset; `SegmentId` identifies one boss fight within that run/attempt. Fresh-run retry creates a new RunId and AttemptId. Advancing to a new fight creates a new SegmentId. No recycled attempt/actor IDs while stale packets may remain.

Authenticate sender → stable player at the session boundary. Never trust a payload PlayerId for ownership. Reject NaN/infinity and invalid enum/range/size before normalization; clamp movement magnitude to 1; aim is planar point within published arena/aim bounds. Zero movement means stand; zero-length aim retains last valid facing; zero-move dash uses last valid facing. Input sequence is monotonic per player and attempt. Future target ticks have a bounded queue; old/expired requests cannot silently activate later.

Use held Basic, Sprint and BraceHeld; all other abilities, dash, jump, potion and revive start are press requests with stable ActionId. Brace starts through an action request and remains up only while held (maximum 90 ticks), so retransmitted “start” cannot restart it. In a future separate-action transport mode, remove edge bits from whole-input dispatch; never run both paths for one press. Input receipt/ack, Queued, Activated, Released, Rejected, Cancelled, Expired and Superseded are distinct. Queued does not mean eligible/activated. Repeat same ID and identical payload returns the stored result without reactivation; same ID/different payload is Conflict. Retry timeout reports Unknown until receipt or checkpoint reconciliation; it does not assume failure or create a new press.

Authority checks active/standing, movement restrictions, cooldown/charges, class prerequisites, target validity and expiry on actual activation/release. Buffer expires after 9 ticks, including while temporarily ineligible; release before contact produces no hit/cooldown if the kit allows cancellation. After release costs remain spent. Result includes rejection reason, action ID, actor and tick; the checkpoint retains sequence/action watermarks plus bounded pending/terminal result records. A terminal record cannot be evicted while the accepted retry window still admits its ID.

## Hazards, provider and failure truth

`ISharedGeometry.Contains` and `Outline` operate on the same typed Shape and actor footprint. Cone, annulus, circle and oriented lane are shared shapes. Low Pendulum is a lane with JumpClearable/clearance, never a separate view-only collider. Normative boundary rule: touching counts as contained; circle/annulus use the actor's radius and cone/lane use disk-versus-shape overlap, including corners. The type surface declares the geometry seam; production disk intersection and swept collision remain required. A polygon outline is presentation, never the damage test.

Each attack has globally unique attempt-local InstanceId, attack definition ID, block/variation/phase/tier, damage kind (Strike or Ground), warning/lock/resolve ticks, anchor policy, target ID, capture state and optional SourceAttackId for Echo provenance. Boss pose at warning and locked geometry are retained in the record. FollowUntilLock updates position/direction after movement through lock; SnapshotAtLock samples at lock; Fixed never tracks. On lock, position and direction freeze together. Retarget/cancel before lock is an encounter decision validated by authority: new target must be standing and active, tie-break by stable ID using the independent encounter RNG where authored. Every change emits old/new target, reason and tick. No eligible replacement cancels. After lock a Sweep/avoid stays frozen; never snaps to a new player on resolve. Cancellation has a terminal event and never deals damage.

Provider receives read-only detached actors (class and life), roster size/active count, boss pose/body/HP fraction/current and queued phase, tier/segment/tick, seeded encounter RNG state and current authoring block. It returns typed schedule/move/phase/recovery/marker/assignment commands, not actor mutations, damage calls, Unity objects or independent hit tests. The authority validates all returned definitions. Provider cursors, queued commands, remembered Echo attack and one-Echo-live invariant are checkpoint state. Targeting draws use explicit algorithm/version and separate state; resetting seed alone cannot restore a partially consumed stream.

Resolved records retain each eligible actor's position, height, dash/jump flags at resolve; locked shape; boss pose at warning; instance/attack/block/variation/phase/tier/seed; assignments; source/Echo; whether the shape was avoided or hit; raw damage, mitigated/absorbed/actual HP loss and overkill; HP before/after/fatal/downed; stun duration; requested and actual knockback. A hit wholly absorbed by Anchor is still Hit with 0 HP loss. Mechanic success is separate from damage and future stack/spread rules. Failure cards quote authoritative positions/geometry and actual consequences, never inferred animation state.

## Checkpoint, reset and lifecycle

Checkpoint is an **atomic detached** value at a completed tick. Readers cannot mutate live simulation through nested arrays/classes. It includes schema and capability set; session/run/attempt/segment IDs and phase; active flags; roster/class configuration; actor IDs and all HP/stamina/movement/jump/dash/stun/knockback state; action cast/release/recovery/buffer and cooldown/charge timers; class state below; boss HP/pose/body/charge trajectory, vulnerability, phases/queued transitions/provider cursor; hazards/locked shapes/assignment history/hit target sets; projectiles with owner/ability/collision/travel/release state; markers; independent RNG states; revive requests/progress/down counts; pending inputs/action results and dedup maps; event next ID, first retained and acknowledged watermarks; segment intervals, accumulated accounting/history. No Unity Transform, coroutine, callback or presenter reference is serialized.

Restore validates version/capabilities, IDs, finite numbers, shape timing, life/config compatibility and all cross-references into a temporary candidate, then commits everything or nothing. Failed restore leaves current world unchanged. Restoring the same checkpoint and same commands must give the same authoritative next outcomes within supported runtime determinism. Old-attempt packets/events are rejected after reset. Event IDs are strictly increasing per attempt, assigned only by authority; consumer key is SessionId + AttemptId + EventId. Bounded event history exposes first retained and next IDs; a gap requests replay, and a gap before retention requires atomic checkpoint plus watermark. Snapshot replay must not reapply HP or re-emit death feedback. Snapshot state owns HP; event consumption owns feedback/accounting replay with dedup.

| Operation | Preserve | Clear/reset |
| --- | --- | --- |
| Next encounter | Stable roster/classes, run identity and per-player run down count, carried potion charges between minibosses, run accounting | New segment; HP full before bosses 2–4; HP and potions full before area boss; stamina/cooldowns/setup/hazards/projectiles/assignments fresh |
| Fresh run/retry | Session and chosen roster/classes | New RunId/AttemptId, encounter 1, fresh health/potions/stamina, all class/action/target state, down counts, RNG seeded, new accounting history group |
| Practice restart | Chosen encounter/tier/roster/classes | New AttemptId and practice segment; full combat reset and seed; never advances run or changes run history |
| Down | Run down count increments for that player | No offense/movement/assignments; queued actions cancel; transient cast stops; target requirements cancel/reassign |
| Revive | That player's run down count | Progress belongs to identified reviver/target; interruption/down/disconnect cancels; no heal implicitly revives |
| Disconnect/inactive | Stable roster identity reserved per session policy | Stop acting/hit eligibility; cancel/reassign requirements; reject stale owner commands |
| Host loss | Reported result/history if locally retained | End authoritative session; no migration/reconnect acceptance claimed |

Revive baseline remains a proposal: 3 s + 2 s per previous down **of the target** (180 + 120*(downCount-1) ticks). Range, restored HP, channel interruption and terminal timeout require explicit tuning before co-op capability is enabled. Late joins remain lobby-only in the minimum profile; reconnect/resume, host migration and transport choice are deferred with #7 on hold. Duplicate classes are valid.

## U1 approved class exceptions

Sources: [Vanguard #31](https://github.com/Risxhb/collab/issues/31#issuecomment-6306130658), [Chimesinger #36](https://github.com/Risxhb/collab/issues/36), [Arcanist #41](https://github.com/Risxhb/collab/issues/41). These supersede conflicting generic four-slot/no-resource/no-healing language only for the approved kits. Action IDs are semantic abilities, not key numbers or fixed four-slot arrays; input bindings and HUD layout are adapters. Dashes remain separate movement actions. Generic movement stamina never pays an attack cost.

| Class | Required shared state and events |
| --- | --- |
| Vanguard | Basic combo/contact/recovery and preserved-step expiry; Opening boolean/expiry/provenance (single non-stacking strength); Special/Ultimate cooldowns and release; Brace active/facing/start/end/cooldown-from-lowering and one Opening grant per use. Frontal eligible **Strike** hits reduce damage 60% and suppress knockback; rear and Ground bypass. Not full immunity/parry. Preserve combo step 180 ticks after dash; pre-release cancel refunds/avoids cost and damage; after release retains commitment. Empowered Special consumes Opening and refunds 120 ticks of remaining Last Word cooldown. Ultimate independent of Opening. Events: contact/release, Opening grant/consume/expire, Brace raise/absorb/lower and cancel. |
| Chimesinger | Harmony amount/cap; two Bellstrike charges/recharge; owned per-target Bell IDs, expiry, strength and two stored Echo IDs/amounts/provenance; cast/cooldowns and exactly-once detonation. Heal/dash preserve setup while timers progress. Chorus heals immediately, caster and standing allies in radius, capped at max HP; no resurrection/HoT/lingering zone. Events: Harmony change, bell apply/refresh/strengthen/expire, echo store, detonate, per-target heal. No ultimate. Harmony spending is an approved exception. Numeric caps/cooldowns/damage remain tuning. |
| Arcanist | Ticks 0–3; two seal charges/recharge; separate owned Seal IDs/targets/created/expiry/ripe-start/inscribed state and inscription source. Empowered hit inscribes oldest uninscribed seal per target. Convergence and expiry atomically consume each seal once (expiry at tick start wins ties in this proposal). Anchor position/return deadline/cooldown, finite shield remaining/expiry. Clockstep cancels unfinished Lance preserving Ticks; defense/dash preserve seals/inscriptions while clocks run. Events: Tick gain/spend/waste, seal add/ripe/inscribe/consume/expire, Anchor record/return/expire, shield grant/absorb/expire. No time stop, health rewind or invulnerability. |

Arcanist prototype choices **remain proposals**, not retroactive approvals: Tick generation on hit only, empowered Lance spending on miss, missed Seal spending charge, maximum concurrent seals during recharge, and multi-target Convergence scaling. Anchor return must use shared valid-position/collision resolution; return into a boss/wall cannot teleport to an invalid point. Finite shield absorption does not suppress stun/knockback unless separately authored; Brace's knockback exception is specific. Down/reset/target death clears or cancels target-bound setup; records retain provenance for recap. Shield and healing are events distinct from HP damage.

## Staged adoption and review gates

Minimum U3-A consumer increment: one solo arena/fight/class, owned finite input, one 60 Hz step, movement/stamina/no-iframe dash, four shapes and height rule, damage/events, atomic practice reset and supported-state checkpoint. Bind only capabilities actually implemented. Reuse #42's shapes/run/kit behavior behind adapters after its owner agrees; extract authoritative rules into scene-independent C#, with Unity presentation consuming them. No replacement parallel authority.

U3-B: three approved class states, charges/buffer/cancel correctness, phase/RNG/provider checkpoint and shared combat accounting; complete full run/practice resets. U3-C: down/revive and active/inactive assignments, authenticated roster/session, replay/checkpoint recovery and actual transport. #7 stays on hold; defining C types does not restart it. Optional Echo content/group mechanics need separate encounter adoption.

Before binding, foundation and encounter/network owners review units/order/identity. Required consumer fixtures: once-per-attack with two different same-tick attacks; retarget/cancel and target loss exactly at lock; frozen geometry after boss moves; circle/annulus/cone/lane boundary disk tests; jump at clearance equality; boss/wall/charge collision; 12-step dash displacement; finite-stamina escape with same data; phase crossings and queued phase events; RNG checkpoint continuation; clone/restore no aliasing; stale-attempt presses/events; receipt replay/conflict/expiry/unknown; full reset of every kit; seal expiry/Convergence tie; target death during projectile dispatch; zero-HP shielded hit; overkill and heal overheal; history replay no duplicate accounting; solo and later 2/8-player revival/assignments. Standalone passing fixtures are not Unity/build/Internet or human acceptance.
