# Combat integration contract v1

Task and discussion: [#6](https://github.com/Risxhb/collab/issues/6). Session owner: [#7](https://github.com/Risxhb/collab/issues/7). Encounter owner: [#8](https://github.com/Risxhb/collab/issues/8).

`game/core/combat_simulation.gd` is a node-free `RefCounted`. Construct it, then call `step(commands)` once per authoritative 1/60s tick. Commands map stable integer actor IDs to `{move: Vector2, basic: bool, technique: bool, signature: bool, guard: bool}`. Only configured IDs are consumed; unknown IDs are ignored and missing commands are neutral. Movement is clamped to unit length; non-finite movement becomes zero. Network decoding, payload types, ownership, rate limits and sender validation belong to the session layer before this API is called.

`configure_roster(entries: Array) -> bool` replaces the whole roster and resets the attempt using the current seed. Entries contain a positive integer `player_id`, `class_id` (currently only `vanguard`, also the default), and an optional `build` array (default empty). IDs must be unique; counts 1–8 are accepted. Invalid input returns false without changing state, generation or configuration. Input is deep-copied and sorted by player ID, so caller order does not change processing order or spawn slots. Build entries are opaque saved configuration; no upgrade effects or transport validation are implemented. Do not edit the public roster/actor dictionaries from session or encounter code.

`actors[id]` holds each actor's independent transient state and copied class/build configuration. `player` aliases the lowest-ID actor for compatibility with the solo view, which still controls ID 1 and renders only that actor. It is not a multiplayer view. Spawns occupy a fixed four-column grid within the arena; boss health/damage and demo timings are not party-scaled. The demo aims at the lowest-ID living actor.

Ordering: increment tick; process living actors by ascending stable ID (move, activate eligible guard, resolve one eligible offensive action); check victory; schedule hazards; resolve due hazards in insertion order for living actors by ascending ID; check party defeat. Each actor has its own offensive cadence, resources, cooldowns, guard and immunity. Actors processed after a lethal boss hit still move/guard but do not spend offensive cooldowns. A victory clears pending hazards and ends the attempt before that tick's hazard resolution. Knocked-out actors cannot move, attack, guard or receive further mechanic resolutions; defeat requires all actors to be knocked out. Defeat/victory freeze subsequent steps. All end ticks are exclusive (`tick < end_tick`).

`reset(seed_value = 1)` restores tick, RNG, every actor's spawn/health/resource/cooldowns/guard/immunity, boss health, active hazards, hazard IDs, event log and outcome. It preserves roster IDs, class/build configuration and the encounter provider. Providers must be stateless functions of tick/simulation/seed or use simulation-owned state that reset explicitly restores; do not keep a private clock/RNG in a scene node.

Set `encounter_provider` to a `Callable(tick: int, context: Dictionary) -> Array[Dictionary]` to replace the demo scheduler. Context is a detached snapshot plus `phase: scheduling`; its `roster` contains `player_id`, `class_id`, `build`, and `active` (health above zero). Modifying it cannot mutate authoritative actors or configuration. Return only newly warned hazards for that tick, using unique IDs within an attempt. Do not resubmit active hazards. The simulation deep-copies and owns them until resolution. Assignment extensions must be reviewed here before integrating richer mechanics.

Outside `step`, snapshot `tick` is the last completed simulation step. The provider runs during the current step after movement/offense, so its context `tick` is the in-progress scheduling tick. Reset increments `generation` even when seed/tick return to their initial values. Transport must match both session identity and generation before accepting feedback; event IDs restart within each generation. The session input envelope will carry a sequence, target tick and processed-sequence acknowledgment. Peer IDs must not become stable player IDs. These delivery/envelope features are session responsibilities, not implemented in the solo view.

Hazard fields:

| Field | Meaning |
|---|---|
| `id`, `name` | Stable attempt-local integer ID and human attack name |
| `kind` | `cone` or `donut`; add other shapes through a shared geometry change |
| `origin` | `Vector2` in arena coordinates |
| `direction`, `half_angle` | Cone direction and half-width in radians |
| `radius`, `inner_radius` | Outer radius; donut hole radius |
| `warning_tick`, `resolve_tick` | Integer timestamps; resolve must follow warning |

`game/combat/hazard_geometry.gd` is the source for collision and visual shapes. Boundaries count as dangerous, with a one-microradian tolerance for float32 angular rounding. Collision uses the actor center. Curves are tessellated for drawing; collision uses analytic radii/angles. Cone and donut geometry do not continuously follow a player; the demo locks origin/direction when warned.

Resolved events include `schema_version`, `generation`, monotonic attempt-local `event_id`, affected `player_id`, `type: mechanic_resolved`, `tick`, `attack_id`, `attack_name`, `position` as `[x,y]`, `required_behavior`, `outcome` (`avoided`, `guarded`, `immune`, `hit`), `guarded`, and `mechanic_failed`. `source_attack_id` defaults to 0 and `lock_tick` defaults to warning time for the current fixed shapes. These provenance fields do not implement Echo or follow/lock behavior. Guard can prevent damage while `mechanic_failed` stays true. The log retains the latest 128 records, not just the last step. Clients must deduplicate on generation/event ID; the session layer still needs reliable delivery/acknowledgment or gap recovery before event-dependent multiplayer feedback is accepted.

`snapshot()` returns a detached dictionary: `schema_version: 1`, `generation`, `tick`, `seed`, `actors`, `roster`, `boss`, `hazards`, `outcome`, `events`. Actors and roster are sorted by stable ID; actor positions and hazard origins become numeric arrays. Actors expose cooldown ready ticks and defensive end ticks. Resolved events also use schema version 1. The snapshot is diagnostic/export only; no import, reconciliation or stable transport encoding exists. The snapshot omits internal RNG state, so it is not an arbitrary-tick save/checkpoint. Rehearsal phase restore must add full RNG/provider state later.

The local prototype intentionally repeats every held action when eligible. For network integration the agreed direction is held Basic plus consumed press requests for Technique/Signature/Guard, deduplicated by input sequence; this requires changing the view and simulation together before network binding. Do not resend an old press as a new command.

Accepted next-contract requirements from #7/#8 (not implemented here): independent encounter-targeting RNG; fixed/follow-until-lock/snapshot-at-lock anchors, target player ID and separate lock tick; shared geometry/count evaluation of avoid/stack/spread/guard requirements; assignment-change events for cancellation/reassignment on knockout/disconnect. Disconnect mutation/reconnection APIs are not implemented; configure_roster starts a new attempt and must not be used to remove a participant mid-fight. Two party-size tiers with explicit count parameters remain required. These changes will be implemented in the shared core before encounters or session code bind to them. No encounter script should simulate these rules privately.

Ownership: foundation owns shared core/combat/actors/UI and root project wiring; networking owns `game/network/`; encounter authoring owns `game/encounters/` and `game/data/encounters/`. Ask the foundation owner for shared changes rather than copying simulation/geometry into another lane.
