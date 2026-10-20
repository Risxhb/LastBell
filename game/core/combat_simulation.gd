class_name CombatSimulation
extends RefCounted
## Node-free authoritative simulation. One step is exactly one tick.
const Geometry = preload("res://combat/hazard_geometry.gd")
const TICK_RATE := 60
const SPEED := 250.0
const ARENA := Rect2(60, 140, 820, 600)
const SPAWN := Vector2(470, 590)
const BOSS_POSITION := Vector2(470, 360)
const BOSS_MAX_HP := 180
const COOLDOWNS := {"basic": 24, "technique": 48, "signature": 360, "guard": 180}
const RANGES := {"basic": 155.0, "technique": 185.0, "signature": 270.0}
const DAMAGE := {"basic": 2, "technique": 9, "signature": 18}
const GUARD_TICKS := 24
const IMMUNITY_TICKS := 45

var tick: int = 0
var seed_value: int = 1
var player: Dictionary = {}
var actors: Dictionary = {}
var roster: Array[Dictionary] = [{"player_id": 1, "class_id": "vanguard", "build": []}]
var boss_hp: int = BOSS_MAX_HP
var hazards: Array[Dictionary] = []
var events: Array[Dictionary] = []
var outcome: String = "playing"
var rng := RandomNumberGenerator.new()
var next_hazard_id: int = 0
var generation: int = 0
var next_event_id: int = 0
var used_hazard_ids: Dictionary = {}
# Callable(tick, detached_context) -> Array[Dictionary]. Empty means use demo schedule.
# Provider owns no hidden mutable state; reset must reproduce its output.
var encounter_provider: Callable

func _init() -> void:
	reset()

## Replace the entire roster atomically and begin a fresh attempt with the same seed.
## IDs are stable game IDs, never transport peer IDs. Only Vanguard is implemented.
func configure_roster(entries: Array) -> bool:
	if entries.is_empty() or entries.size() > 8:
		return false
	var configured: Array[Dictionary] = []
	var ids: Dictionary = {}
	for entry in entries:
		if not entry is Dictionary:
			return false
		var id_value: Variant = entry.get("player_id")
		if not id_value is int or id_value <= 0 or ids.has(id_value):
			return false
		if entry.get("class_id", "vanguard") != "vanguard" or not entry.get("build", []) is Array:
			return false
		ids[id_value] = true
		configured.append({"player_id": id_value, "class_id": "vanguard", "build": entry.get("build", []).duplicate(true)})
	configured.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a.player_id < b.player_id)
	roster = configured
	reset(seed_value)
	return true

func reset(new_seed: int = 1) -> void:
	generation += 1
	next_event_id = 0
	seed_value = new_seed
	rng.seed = new_seed
	tick = 0
	boss_hp = BOSS_MAX_HP
	actors.clear()
	for index in range(roster.size()):
		var member: Dictionary = roster[index]
		actors[member.player_id] = {"id": member.player_id, "class_id": member.class_id,
			"build": member.build.duplicate(true), "position": SPAWN + Vector2((index % 4) * 40, floori(index / 4.0) * 40),
			"hp": 5, "resource": 0,
			"ready_at": {"basic": 0, "technique": 0, "signature": 0, "guard": 0},
			"guard_until": 0, "immune_until": 0, "attack_until": 0, "last_action": ""}
	# Compatibility alias for the solo view; multi-actor callers must use actors[id].
	player = actors[roster[0].player_id]
	hazards.clear()
	events.clear()
	next_hazard_id = 0
	used_hazard_ids.clear()
	outcome = "playing"

func step(commands: Dictionary) -> void:
	if outcome != "playing":
		return
	tick += 1
	for member in roster:
		var actor: Dictionary = actors[member.player_id]
		if int(actor.hp) > 0:
			_step_actor(actor, commands.get(member.player_id, {}))
	if boss_hp <= 0:
		outcome = "victory"
		hazards.clear()
		return
	if encounter_provider.is_valid():
		var context := snapshot()
		context["phase"] = "scheduling"
		var scheduled: Array = encounter_provider.call(tick, context)
		for hazard in scheduled:
			if hazard is Dictionary:
				queue_hazard(hazard)
	elif tick % 210 == 1:
		_spawn_demo_hazard()
	var anchored: Array[Dictionary] = []
	for hazard in hazards:
		if _update_anchor(hazard):
			anchored.append(hazard)
	var pending: Array[Dictionary] = []
	for hazard in anchored:
		if tick >= int(hazard.resolve_tick):
			resolve_hazard(hazard)
		else:
			pending.append(hazard)
	hazards = pending
	var any_alive := false
	for actor in actors.values():
		any_alive = any_alive or int(actor.hp) > 0
	if not any_alive:
		outcome = "defeat"

## Trusted encounter API, not a network decoder. Admit only warnings for this tick.
## Invalid or reused definitions leave authoritative state unchanged.
func queue_hazard(definition: Dictionary) -> bool:
	if outcome != "playing":
		return false
	var id_value: Variant = definition.get("id")
	if not id_value is int or id_value <= 0 or used_hazard_ids.has(id_value):
		return false
	if not definition.get("name") is String or definition.get("kind") not in ["cone", "donut"]:
		return false
	var origin: Variant = definition.get("origin")
	if not origin is Vector2 or not origin.is_finite():
		return false
	var warning: Variant = definition.get("warning_tick")
	var resolve: Variant = definition.get("resolve_tick")
	var lock: Variant = definition.get("lock_tick", warning)
	if not warning is int or not resolve is int or not lock is int:
		return false
	if warning != tick or lock < warning or resolve <= lock:
		return false
	for field in ["radius", "inner_radius", "direction", "half_angle"]:
		var value: Variant = definition.get(field, 0.0)
		if not (value is float or value is int) or not is_finite(float(value)):
			return false
	if float(definition.get("radius", 0)) <= 0:
		return false
	if definition.kind == "donut":
		if not definition.has("inner_radius") or float(definition.inner_radius) < 0 or float(definition.inner_radius) > float(definition.radius):
			return false
	elif not definition.has("direction") or not definition.has("half_angle") or float(definition.half_angle) < 0 or float(definition.half_angle) > PI:
		return false
	var anchor: Variant = definition.get("anchor", "fixed")
	var component: Variant = definition.get("anchor_component", "origin")
	if anchor not in ["fixed", "follow_until_lock", "snapshot_at_lock"] or component not in ["origin", "direction"]:
		return false
	if anchor == "fixed" and lock != warning:
		return false
	if component == "direction" and definition.kind != "cone":
		return false
	if anchor != "fixed":
		var target_id: Variant = definition.get("target_player_id")
		if not target_id is int or not actors.has(target_id) or int(actors[target_id].hp) <= 0:
			return false
	var hazard: Dictionary = definition.duplicate(true)
	hazard["anchor"] = anchor
	hazard["anchor_component"] = component
	hazard["lock_tick"] = lock
	hazard["locked"] = anchor == "fixed"
	_update_anchor(hazard)
	hazards.append(hazard)
	used_hazard_ids[id_value] = true
	return true

func _update_anchor(hazard: Dictionary) -> bool:
	if hazard.get("anchor", "fixed") == "fixed" or hazard.get("locked", false):
		return true
	var target_id: int = hazard.target_player_id
	if not actors.has(target_id) or int(actors[target_id].hp) <= 0:
		next_event_id += 1
		events.append({"schema_version": 2, "generation": generation, "event_id": next_event_id,
			"type": "assignment_changed", "tick": tick, "attack_id": hazard.id,
			"attack_name": hazard.name, "old_player_id": target_id, "new_player_id": 0,
			"cancelled": true, "reason": "target_unavailable"})
		_trim_events()
		return false
	if hazard.anchor == "follow_until_lock" or tick >= int(hazard.lock_tick):
		var position: Vector2 = actors[target_id].position
		if hazard.get("anchor_component", "origin") == "direction":
			var offset: Vector2 = position - Vector2(hazard.origin)
			if not offset.is_zero_approx():
				hazard.direction = offset.angle()
		else:
			hazard.origin = position
	if tick >= int(hazard.lock_tick):
		hazard["locked"] = true
	return true

func _step_actor(actor: Dictionary, command: Dictionary) -> void:
	var move: Vector2 = command.get("move", Vector2.ZERO)
	if not move.is_finite():
		move = Vector2.ZERO
	move = move.limit_length(1.0)
	var position: Vector2 = actor.position + move * SPEED / TICK_RATE
	actor.position = position.clamp(ARENA.position + Vector2(14, 14), ARENA.end - Vector2(14, 14))
	if command.get("guard", false) and tick >= int(actor.ready_at.guard):
		actor.guard_until = tick + GUARD_TICKS
		actor.ready_at.guard = tick + int(COOLDOWNS.guard)
	for action in ["signature", "technique", "basic"]:
		if boss_hp <= 0:
			break
		if command.get(action, false) and _attack(action, actor):
			break # Shared offensive cadence; guard is independent.

func _attack(action: String, actor: Dictionary) -> bool:
	if tick < int(actor.ready_at[action]) or tick < int(actor.attack_until):
		return false
	if action == "technique" and int(actor.resource) < 3:
		return false
	if actor.position.distance_to(BOSS_POSITION) > float(RANGES[action]):
		return false
	actor.ready_at[action] = tick + int(COOLDOWNS[action])
	actor.attack_until = tick + 18
	actor.last_action = action
	if action == "basic":
		actor.resource = mini(int(actor.resource) + 1, 6)
	elif action == "technique":
		actor.resource -= 3
	boss_hp = maxi(0, boss_hp - int(DAMAGE[action]))
	return true

func _spawn_demo_hazard() -> void:
	next_hazard_id += 1
	var is_cone := next_hazard_id % 2 == 1
	var target: Dictionary = player
	for member in roster:
		if int(actors[member.player_id].hp) > 0:
			target = actors[member.player_id]
			break
	var direction := (Vector2(target.position) - BOSS_POSITION).angle() + rng.randf_range(-0.15, 0.15)
	queue_hazard({"id": next_hazard_id, "name": "Bell Sweep" if is_cone else "Hollow Toll",
		"kind": "cone" if is_cone else "donut", "origin": BOSS_POSITION,
		"direction": direction, "half_angle": PI / 5.0, "radius": 380.0,
		"inner_radius": 155.0, "warning_tick": tick, "resolve_tick": tick + 90})

func resolve_hazard(hazard: Dictionary) -> void:
	for member in roster:
		var actor: Dictionary = actors[member.player_id]
		if int(actor.hp) > 0:
			_resolve_actor_hazard(hazard, actor)

func _resolve_actor_hazard(hazard: Dictionary, actor: Dictionary) -> void:
	var inside := Geometry.contains_point(hazard, actor.position)
	var guarded := inside and tick < int(actor.guard_until)
	var result := "avoided"
	if inside:
		if guarded:
			result = "guarded"
		elif tick < int(actor.immune_until):
			result = "immune"
		else:
			actor.hp = maxi(0, int(actor.hp) - 1)
			actor.immune_until = tick + IMMUNITY_TICKS
			result = "hit"
	next_event_id += 1
	events.append({"schema_version": 2, "generation": generation, "event_id": next_event_id,
		"player_id": actor.id, "type": "mechanic_resolved", "tick": tick,
		"source_attack_id": hazard.get("source_attack_id", 0),
		"lock_tick": hazard.get("lock_tick", hazard.warning_tick),
		"anchor": hazard.get("anchor", "fixed"), "target_player_id": hazard.get("target_player_id", 0),
		"hazard_origin": [hazard.origin.x, hazard.origin.y], "hazard_direction": hazard.get("direction", 0.0),
		"attack_id": hazard.id, "attack_name": hazard.name,
		"position": [actor.position.x, actor.position.y], "outcome": result,
		"required_behavior": "Leave the marked cone" if hazard.kind == "cone" else "Move inside the inner ring or beyond the outer ring",
		"guarded": guarded, "mechanic_failed": inside})
	_trim_events()

func _trim_events() -> void:
	if events.size() > 128:
		events.pop_front()

func snapshot() -> Dictionary:
	var actor_snapshots: Array[Dictionary] = []
	var roster_snapshot: Array[Dictionary] = roster.duplicate(true)
	for member in roster_snapshot:
		var actor: Dictionary = actors[member.player_id].duplicate(true)
		actor.position = [actor.position.x, actor.position.y]
		actor_snapshots.append(actor)
		member["active"] = int(actor.hp) > 0
	var shapes: Array[Dictionary] = []
	for hazard in hazards:
		var shape: Dictionary = hazard.duplicate(true)
		shape.origin = [hazard.origin.x, hazard.origin.y]
		shapes.append(shape)
	return {"schema_version": 2, "generation": generation, "tick": tick, "seed": seed_value,
		"actors": actor_snapshots, "roster": roster_snapshot,
		"boss": {"hp": boss_hp, "max_hp": BOSS_MAX_HP, "position": [BOSS_POSITION.x, BOSS_POSITION.y]},
		"hazards": shapes, "outcome": outcome, "events": events.duplicate(true)}
