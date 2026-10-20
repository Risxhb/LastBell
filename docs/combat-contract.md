# Combat integration contract v0

Task and discussion: [#6](https://github.com/Risxhb/collab/issues/6). Session owner: [#7](https://github.com/Risxhb/collab/issues/7). Encounter owner: [#8](https://github.com/Risxhb/collab/issues/8).

`game/core/combat_simulation.gd` is a node-free `RefCounted`. Construct it, then call `step(commands)` once per authoritative 1/60s tick. Commands map stable integer actor IDs to `{move: Vector2, basic: bool, technique: bool, signature: bool, guard: bool}`. The foundation only consumes ID 1; other IDs are ignored. Missing commands are neutral. Movement is clamped to unit length; non-finite movement becomes zero. Network decoding, payload types, ownership, rate limits and sender validation belong to the session layer before this API is called.

Ordering: increment tick, move, activate eligible guard, resolve one eligible offensive action, check victory, schedule hazards, resolve due hazards, check defeat. Guard is independent of the shared offensive cadence. A victory clears pending hazards and ends the attempt before that tick's hazard resolution. Defeat/victory freeze subsequent steps. All end ticks are exclusive (`tick < end_tick`).

`reset(seed_value = 1)` restores tick, RNG, player position/health/resource/cooldowns/guard/immunity, boss health, active hazards, hazard IDs, event log and outcome. It preserves the encounter provider. Providers must be stateless functions of tick/simulation/seed or use simulation-owned state that reset explicitly restores; do not keep a private clock/RNG in a scene node.

Set `encounter_provider` to a `Callable(tick: int, simulation: RefCounted) -> Array[Dictionary]` to replace the demo scheduler. Return only newly warned hazards for that tick, using unique IDs within an attempt. Do not resubmit active hazards. The simulation deep-copies and owns them until resolution. Reset and assignment extensions must be reviewed here before integrating richer mechanics.

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

Resolved events include `tick`, `attack_id`, `attack_name`, `position` as `[x,y]`, `required_behavior`, `outcome` (`avoided`, `guarded`, `immune`, `hit`), `guarded`, and `mechanic_failed`. This is actual authoritative resolve state. Guard can prevent damage while `mechanic_failed` stays true. The log retains the latest 128 records; it is not persistent run telemetry. Event IDs/sequence numbers, multi-player assignments and richer diagrams are next revisions.

`snapshot()` returns a detached dictionary: `schema_version: 0`, `tick`, `seed`, `actors`, `boss`, `hazards`, `outcome`, `events`. Actor positions and hazard origins become numeric arrays. Actors expose cooldown ready ticks and defensive end ticks. The snapshot is diagnostic/export only; no import, reconciliation or stable transport encoding exists. The snapshot omits internal RNG state, so it is not an arbitrary-tick save/checkpoint. Rehearsal phase restore must add full RNG/provider state later.

Ownership: foundation owns shared core/combat/actors/UI and root project wiring; networking owns `game/network/`; encounter authoring owns `game/encounters/` and `game/data/encounters/`. Ask the foundation owner for shared changes rather than copying simulation/geometry into another lane.
