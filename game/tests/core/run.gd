extends SceneTree
const Sim = preload("res://core/combat_simulation.gd")
const Geometry = preload("res://combat/hazard_geometry.gd")
var checks: int = 0
var failures: int = 0

func _initialize() -> void:
	_test_geometry()
	_test_combat_rules()
	_test_guard_and_immunity()
	_test_reset_and_seed()
	_test_provider_and_snapshot()
	_test_roster()
	_test_party_combat()
	print("Combat foundation: %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		push_error(message)

func shape(kind: String = "cone") -> Dictionary:
	return {"id": 1, "name": "Test", "kind": kind, "origin": Vector2.ZERO,
		"radius": 100.0, "inner_radius": 50.0, "direction": 0.0,
		"half_angle": PI / 4, "warning_tick": 1, "resolve_tick": 10}

func _test_geometry() -> void:
	var cone := shape()
	check(Geometry.contains_point(cone, Vector2.ZERO), "Cone origin must be inside")
	check(Geometry.contains_point(cone, Vector2(100, 0)), "Cone outer boundary is dangerous")
	check(not Geometry.contains_point(cone, Vector2(100.01, 0)), "Outside cone radius is safe")
	check(Geometry.contains_point(cone, Vector2.from_angle(PI / 4) * 80), "Cone angle boundary is dangerous")
	check(not Geometry.contains_point(cone, Vector2.from_angle(PI / 4 + 0.001) * 80), "Outside cone angle is safe")
	cone.direction = PI - 0.05
	check(Geometry.contains_point(cone, Vector2(-80, -1)), "Angle wrap crosses minus PI correctly")
	var donut := shape("donut")
	check(not Geometry.contains_point(donut, Vector2(49.99, 0)), "Donut hole is safe")
	check(Geometry.contains_point(donut, Vector2(50, 0)), "Donut inner boundary is dangerous")
	check(Geometry.contains_point(donut, Vector2(100, 0)), "Donut outer boundary is dangerous")
	check(not Geometry.contains_point(donut, Vector2(100.01, 0)), "Outside donut is safe")
	check(Geometry.ring_quads(donut).size() == 128, "Donut rendering preserves its hole")

func _test_combat_rules() -> void:
	var sim := Sim.new()
	sim.step({1: {"basic": true}})
	check(sim.boss_hp == Sim.BOSS_MAX_HP and sim.player.resource == 0, "Out-of-range Basic cannot damage or generate charge")
	sim.player.position = Sim.BOSS_POSITION + Vector2(100, 0)
	sim.step({1: {"technique": true}})
	check(sim.boss_hp == Sim.BOSS_MAX_HP, "Technique needs charge")
	sim.step({1: {"basic": true}})
	check(sim.player.resource == 1 and sim.boss_hp == Sim.BOSS_MAX_HP - 2, "Basic grants charge on a valid attack")
	var first_hp: int = sim.boss_hp
	for index in range(23):
		sim.step({1: {"basic": true}})
	check(sim.boss_hp == first_hp, "Holding Basic cannot bypass cooldown")
	sim.step({1: {"basic": true}})
	check(sim.boss_hp == first_hp - 2, "Basic fires at cooldown boundary")
	sim.player.resource = 3
	sim.player.attack_until = sim.tick
	sim.step({1: {"technique": true, "signature": true, "basic": true}})
	check(sim.player.last_action == "signature" and sim.player.resource == 3, "Simultaneous offense follows priority and only fires once")
	sim.player.attack_until = sim.tick
	sim.step({1: {"technique": true}})
	check(sim.player.resource == 0, "Technique spends exactly three charge")
	sim.player.position = Vector2(200, 200)
	var previous: Vector2 = sim.player.position
	sim.step({1: {"move": Vector2(100, 100)}})
	check(is_equal_approx(previous.distance_to(sim.player.position), Sim.SPEED / 60), "Movement input is clamped, including diagonals")
	for index in range(300):
		sim.step({1: {"move": Vector2(-1, -1)}})
	check(sim.player.position.x >= 74 and sim.player.position.y >= 154, "Player stays inside arena")

func _test_guard_and_immunity() -> void:
	var sim := Sim.new()
	sim.player.position = Vector2(75, 0)
	var hazard := shape()
	sim.step({1: {"guard": true}})
	# Movement clamping runs before resolution; place actor in the test geometry.
	sim.player.position = Vector2(75, 0)
	sim.resolve_hazard(hazard)
	check(sim.player.hp == 5 and sim.events.back().outcome == "guarded", "Guard prevents eligible damage")
	check(sim.events.back().mechanic_failed, "Guard does not erase a positioning failure")
	sim.tick = sim.player.guard_until
	sim.resolve_hazard(hazard)
	check(sim.player.hp == 4 and sim.events.back().outcome == "hit", "Guard expires at its exact end tick")
	sim.resolve_hazard(hazard)
	check(sim.player.hp == 4 and sim.events.back().outcome == "immune", "Overlapping hits only remove one pip")
	sim.tick = sim.player.immune_until
	sim.resolve_hazard(hazard)
	check(sim.player.hp == 3, "Damage resumes at immunity boundary")
	sim.player.position = Vector2(-75, 0)
	sim.resolve_hazard(hazard)
	check(sim.events.back().outcome == "avoided", "Avoidance is distinct from immunity")
	# Resolve-tick guard activation precedes damage.
	sim.reset()
	var timed := shape("donut")
	timed.origin = Sim.SPAWN
	timed.inner_radius = 0.0
	timed.resolve_tick = 1
	sim.hazards.append(timed)
	sim.step({1: {"guard": true}})
	check(sim.player.hp == 5 and sim.events[0].outcome == "guarded", "Guard input on resolve tick precedes damage")
	# Knockout freezes the attempt and preserves the cause.
	sim.reset()
	sim.player.hp = 1
	sim.hazards.append(timed)
	sim.step({})
	check(sim.outcome == "defeat" and sim.player.hp == 0, "Zero pips ends attempt")
	var frozen_tick: int = sim.tick
	sim.step({1: {"guard": true}})
	check(sim.tick == frozen_tick, "Defeated attempts cannot continue simulation")

func _test_reset_and_seed() -> void:
	var sim := Sim.new()
	var initial: Dictionary = sim.snapshot()
	for index in range(800):
		sim.step({1: {"move": Vector2(0.1, -0.1), "basic": true, "guard": true}})
	sim.reset()
	var restored: Dictionary = sim.snapshot()
	check(restored.generation == initial.generation + 1, "Reset increments generation to reject stale attempt packets")
	restored.erase("generation")
	initial.erase("generation")
	check(restored == initial, "Reset restores all transient combat state")
	var baseline := Sim.new()
	baseline.reset()
	for index in range(70):
		var commands := {1: {"move": Vector2(0.5, 0.2)}}
		sim.step(commands)
		baseline.step(commands)
	check(sim.snapshot() == baseline.snapshot(), "Reset restores seeded random schedule")
	var other := Sim.new()
	other.reset(15)
	other.step({})
	baseline.reset(1)
	baseline.step({})
	check(other.hazards[0].direction != baseline.hazards[0].direction, "Different seeds select different demo cone directions")

func _test_provider_and_snapshot() -> void:
	var sim := Sim.new()
	sim.encounter_provider = func(_tick: int, context: Dictionary) -> Array:
		context.actors[0].hp = 0
		return []
	sim.step({})
	check(sim.hazards.is_empty(), "Encounter provider replaces demo scheduling")
	check(sim.player.hp == 5, "Encounter provider cannot modify authoritative actor state through context")
	var copy: Dictionary = sim.snapshot()
	copy.actors[0].hp = 0
	copy.actors[0].ready_at.guard = 9999
	check(sim.player.hp == 5 and sim.player.ready_at.guard == 0, "Snapshot is detached from authoritative state")
	check(copy.actors[0].position is Array, "Snapshot uses transport-friendly positions")
	var hazard := shape()
	sim.player.position = Vector2(75, 0)
	sim.resolve_hazard(hazard)
	sim.resolve_hazard(hazard)
	check(sim.events[1].event_id == sim.events[0].event_id + 1, "Resolved events have monotonic deduplication IDs")
	check(sim.events[0].generation == sim.generation and sim.events[0].player_id == 1, "Event identity includes attempt and affected actor")
	sim.player.position = Sim.BOSS_POSITION + Vector2(100, 0)
	for index in range(4000):
		sim.step({1: {"basic": true, "technique": true, "signature": true}})
		if sim.outcome == "victory":
			break
	check(sim.outcome == "victory" and sim.boss_hp == 0, "Valid offense can finish the encounter")

func _test_roster() -> void:
	for count in range(1, 9):
		var sim := Sim.new()
		var entries: Array = []
		for index in range(count):
			entries.append({"player_id": 100 + index * 3, "class_id": "vanguard", "build": [{"id": "placeholder"}]})
		entries.reverse()
		check(sim.configure_roster(entries), "Party count %d configures" % count)
		check(sim.actors.size() == count and sim.player.id == 100, "Stable IDs are sorted independently of entry order")
		var initial: Dictionary = sim.snapshot()
		var spawn_positions: Array = []
		for actor in sim.actors.values():
			spawn_positions.append(actor.position)
		check(spawn_positions.size() == count and _unique_positions(spawn_positions) == count, "Party spawns are distinct")
		entries[0].build[0].id = "changed by caller"
		check(sim.snapshot() == initial, "Configuration is detached from caller input")
		for index in range(15):
			sim.step({100: {"move": Vector2(1, 0), "guard": true}})
		sim.actors[100].build[0].id = "transient mutation"
		sim.reset()
		var restored: Dictionary = sim.snapshot()
		check(restored.generation == initial.generation + 1, "Party retry advances generation")
		initial.erase("generation")
		restored.erase("generation")
		check(restored == initial, "Retry preserves roster/build and restores every actor")
	var sim := Sim.new()
	var before: Dictionary = sim.snapshot()
	var invalid: Array = [[], [null], [{"player_id": 0}], [{"player_id": -1}],
		[{"player_id": 1.5}], [{"player_id": "1"}], [{"player_id": 1}, {"player_id": 1}],
		[{"player_id": 1, "class_id": "arcanist"}], [{"player_id": 1, "build": {}}]]
	var nine: Array = []
	for id in range(1, 10):
		nine.append({"player_id": id})
	invalid.append(nine)
	for entries in invalid:
		check(not sim.configure_roster(entries) and sim.snapshot() == before, "Invalid roster rejects atomically")

func _unique_positions(positions: Array) -> int:
	var unique: Dictionary = {}
	for position in positions:
		unique[position] = true
	return unique.size()

func _test_party_combat() -> void:
	var sim := Sim.new()
	sim.configure_roster([{"player_id": 42}, {"player_id": 7}])
	sim.encounter_provider = func(_tick: int, context: Dictionary) -> Array:
		context.roster[0].build.append("mutation")
		context.actors[1].hp = 0
		return []
	var idle_position: Vector2 = sim.actors[42].position
	sim.step({7: {"move": Vector2(1, 0)}, 1: {"signature": true}, 999: {"guard": true}})
	check(sim.actors[7].position.x > Sim.SPAWN.x and sim.actors[42].position == idle_position, "Only addressed stable ID moves; missing and unknown IDs are neutral")
	check(sim.actors[42].hp == 5 and sim.roster[0].build.is_empty(), "Provider cannot mutate party actors or build config")
	for actor in sim.actors.values():
		actor.position = Sim.BOSS_POSITION + Vector2(100, 0)
	sim.step({7: {"basic": true, "guard": true}, 42: {"signature": true}})
	check(sim.boss_hp == Sim.BOSS_MAX_HP - 20, "Both actors contribute independent attacks")
	check(sim.actors[7].resource == 1 and sim.actors[42].resource == 0, "Actor resources are independent")
	check(sim.actors[7].ready_at.basic > 0 and sim.actors[42].ready_at.basic == 0, "Actor cooldown dictionaries are independent")
	var hazard := shape("donut")
	hazard.origin = Sim.BOSS_POSITION
	hazard.radius = 200.0
	hazard.inner_radius = 0.0
	sim.resolve_hazard(hazard)
	check(sim.actors[7].hp == 5 and sim.actors[42].hp == 4, "Guard and damage resolve per actor")
	check(sim.events[0].player_id == 7 and sim.events[0].outcome == "guarded" and sim.events[1].player_id == 42 and sim.events[1].outcome == "hit", "Party events identify affected actor in stable ID order")
	var detached: Dictionary = sim.snapshot()
	detached.actors[1].ready_at.signature = 0
	detached.roster[0].build.append("mutation")
	check(sim.actors[42].ready_at.signature > 0 and sim.roster[0].build.is_empty(), "Party snapshot and roster are detached")
	sim.actors[7].hp = 0
	var knocked_position: Vector2 = sim.actors[7].position
	sim.step({7: {"move": Vector2(1, 0), "signature": true}})
	check(sim.outcome == "playing" and sim.actors[7].position == knocked_position and sim.actors[7].ready_at.signature == 0, "Knocked-out actor cannot act while living teammate continues")
	check(not sim.snapshot().roster[0].active and sim.snapshot().roster[1].active, "Provider roster marks knockout inactive")
	var event_count: int = sim.events.size()
	sim.resolve_hazard(hazard)
	check(sim.events.size() == event_count + 1 and sim.events.back().player_id == 42, "Hazards skip knocked-out actors")
	sim.actors[42].hp = 0
	sim.step({})
	check(sim.outcome == "defeat", "Party defeat requires all actors knocked out")
	sim.reset(55)
	sim.boss_hp = 1
	for actor in sim.actors.values():
		actor.position = Sim.BOSS_POSITION + Vector2(100, 0)
	hazard.resolve_tick = 1
	sim.hazards.append(hazard)
	sim.step({7: {"basic": true}, 42: {"signature": true}})
	check(sim.outcome == "victory" and sim.hazards.is_empty() and sim.events.is_empty(), "Party victory clears hazards before same-tick damage")
	check(sim.actors[42].ready_at.signature == 0, "Later actor does not spend cooldown after lethal attack")
