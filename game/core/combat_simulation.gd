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
var boss_hp: int = BOSS_MAX_HP
var hazards: Array[Dictionary] = []
var events: Array[Dictionary] = []
var outcome: String = "playing"
var rng := RandomNumberGenerator.new()
var next_hazard_id: int = 0
# Callable(tick, simulation) -> Array[Dictionary]. Empty means use demo schedule.
# Provider owns no hidden mutable state; reset must reproduce its output.
var encounter_provider: Callable

func _init() -> void:
	reset()

func reset(new_seed: int = 1) -> void:
	seed_value = new_seed
	rng.seed = new_seed
	tick = 0
	boss_hp = BOSS_MAX_HP
	player = {"id": 1, "position": SPAWN, "hp": 5, "resource": 0,
		"ready_at": {"basic": 0, "technique": 0, "signature": 0, "guard": 0},
		"guard_until": 0, "immune_until": 0, "attack_until": 0, "last_action": ""}
	hazards.clear()
	events.clear()
	next_hazard_id = 0
	outcome = "playing"

func step(commands: Dictionary) -> void:
	if outcome != "playing":
		return
	tick += 1
	var command: Dictionary = commands.get(1, {})
	var move: Vector2 = command.get("move", Vector2.ZERO)
	if not move.is_finite():
		move = Vector2.ZERO
	move = move.limit_length(1.0)
	var position: Vector2 = player.position + move * SPEED / TICK_RATE
	player.position = position.clamp(ARENA.position + Vector2(14, 14), ARENA.end - Vector2(14, 14))
	if command.get("guard", false) and tick >= int(player.ready_at.guard):
		player.guard_until = tick + GUARD_TICKS
		player.ready_at.guard = tick + int(COOLDOWNS.guard)
	for action in ["signature", "technique", "basic"]:
		if command.get(action, false) and _attack(action):
			break # Shared offensive cadence; guard is independent.
	if boss_hp <= 0:
		outcome = "victory"
		hazards.clear()
		return
	if encounter_provider.is_valid():
		var scheduled: Array = encounter_provider.call(tick, self)
		for hazard in scheduled:
			hazards.append(hazard.duplicate(true))
	elif tick % 210 == 1:
		_spawn_demo_hazard()
	var pending: Array[Dictionary] = []
	for hazard in hazards:
		if tick >= int(hazard.resolve_tick):
			resolve_hazard(hazard)
		else:
			pending.append(hazard)
	hazards = pending
	if int(player.hp) <= 0:
		outcome = "defeat"

func _attack(action: String) -> bool:
	if tick < int(player.ready_at[action]) or tick < int(player.attack_until):
		return false
	if action == "technique" and int(player.resource) < 3:
		return false
	if player.position.distance_to(BOSS_POSITION) > float(RANGES[action]):
		return false
	player.ready_at[action] = tick + int(COOLDOWNS[action])
	player.attack_until = tick + 18
	player.last_action = action
	if action == "basic":
		player.resource = mini(int(player.resource) + 1, 6)
	elif action == "technique":
		player.resource -= 3
	boss_hp = maxi(0, boss_hp - int(DAMAGE[action]))
	return true

func _spawn_demo_hazard() -> void:
	next_hazard_id += 1
	var is_cone := next_hazard_id % 2 == 1
	var direction := (Vector2(player.position) - BOSS_POSITION).angle() + rng.randf_range(-0.15, 0.15)
	hazards.append({"id": next_hazard_id, "name": "Bell Sweep" if is_cone else "Hollow Toll",
		"kind": "cone" if is_cone else "donut", "origin": BOSS_POSITION,
		"direction": direction, "half_angle": PI / 5.0, "radius": 380.0,
		"inner_radius": 155.0, "warning_tick": tick, "resolve_tick": tick + 90})

func resolve_hazard(hazard: Dictionary) -> void:
	var inside := Geometry.contains_point(hazard, player.position)
	var guarded := inside and tick < int(player.guard_until)
	var result := "avoided"
	if inside:
		if guarded:
			result = "guarded"
		elif tick < int(player.immune_until):
			result = "immune"
		else:
			player.hp = maxi(0, int(player.hp) - 1)
			player.immune_until = tick + IMMUNITY_TICKS
			result = "hit"
	events.append({"tick": tick, "attack_id": hazard.id, "attack_name": hazard.name,
		"position": [player.position.x, player.position.y], "outcome": result,
		"required_behavior": "Leave the marked cone" if hazard.kind == "cone" else "Move inside the inner ring or beyond the outer ring",
		"guarded": guarded, "mechanic_failed": inside})
	if events.size() > 128:
		events.pop_front()

func snapshot() -> Dictionary:
	var actor: Dictionary = player.duplicate(true)
	actor.position = [player.position.x, player.position.y]
	var shapes: Array[Dictionary] = []
	for hazard in hazards:
		var shape: Dictionary = hazard.duplicate(true)
		shape.origin = [hazard.origin.x, hazard.origin.y]
		shapes.append(shape)
	return {"schema_version": 0, "tick": tick, "seed": seed_value, "actors": [actor],
		"boss": {"hp": boss_hp, "max_hp": BOSS_MAX_HP, "position": [BOSS_POSITION.x, BOSS_POSITION.y]},
		"hazards": shapes, "outcome": outcome, "events": events.duplicate(true)}
