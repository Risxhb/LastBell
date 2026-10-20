extends RefCounted
## Transport-free, host-owned session state. No combat rules or scene dependencies.
const MAX_PLAYERS := 8
const RATE_LIMIT := 120
const CLASSES := ["vanguard"]
var session_id: String
var generation := 0
var tick := 0
var phase := "lobby"
var players: Dictionary = {}
var peer_players: Dictionary = {}
var next_id := 1
var revision := 0
var last_received: Dictionary = {}
var processed: Dictionary = {}
var last_target: Dictionary = {}
var pending: Dictionary = {}
var rate_windows: Dictionary = {}
var event_id := 0
var events: Array[Dictionary] = []

func _init(identity: String = "") -> void:
	session_id = identity
	if session_id.is_empty():
		session_id = Crypto.new().generate_random_bytes(16).hex_encode()

func join(peer_id: int) -> Dictionary:
	if peer_players.has(peer_id):
		return {"ok": false, "reason": "already_joined"}
	if phase != "lobby":
		return {"ok": false, "reason": "encounter_in_progress"}
	if players.size() >= MAX_PLAYERS:
		return {"ok": false, "reason": "session_full"}
	var used: Array = []
	for player in players.values():
		used.append(player.slot)
	var slot := 1
	while slot in used:
		slot += 1
	var id := next_id
	next_id += 1
	players[id] = {"player_id": id, "slot": slot, "class_id": "vanguard", "ready": false}
	peer_players[peer_id] = id
	processed[id] = 0
	last_received[id] = 0
	last_target[id] = 0
	_record("joined", id)
	return {"ok": true, "player_id": id, "slot": slot}

func leave(peer_id: int) -> void:
	if not peer_players.has(peer_id):
		return
	var id: int = peer_players[peer_id]
	players.erase(id)
	peer_players.erase(peer_id)
	last_received.erase(id)
	processed.erase(id)
	last_target.erase(id)
	pending.erase(id)
	rate_windows.erase(peer_id)
	_record("disconnected", id)
	# Active mechanic cancellation/reassignment is an integration prerequisite,
	# not simulated by this standalone session experiment.

func request(peer_id: int, message: Dictionary, now_ms: int) -> Dictionary:
	if not peer_players.has(peer_id):
		return _reject("unknown_sender")
	if not _rate_allowed(peer_id, now_ms):
		return _reject("rate_limited")
	if message.get("session_id") != session_id:
		return _reject("wrong_session")
	if typeof(message.get("generation")) != TYPE_INT or message.generation != generation:
		return _reject("wrong_generation")
	var id: int = peer_players[peer_id]
	match message.get("kind"):
		"class":
			if phase != "lobby":
				return _reject("encounter_in_progress")
			if typeof(message.get("class_id")) != TYPE_STRING or message.class_id not in CLASSES:
				return _reject("invalid_class")
			players[id].class_id = message.class_id
			players[id].ready = false
			_record("class_changed", id)
		"ready":
			if phase != "lobby":
				return _reject("encounter_in_progress")
			if typeof(message.get("ready")) != TYPE_BOOL:
				return _reject("invalid_ready")
			players[id].ready = message.ready
			_record("ready_changed", id)
		"start":
			if peer_id != 1:
				return _reject("host_only")
			if phase != "lobby":
				return _reject("encounter_in_progress")
			for player in players.values():
				if not player.ready:
					return _reject("players_not_ready")
			generation += 1
			tick = 0
			phase = "running"
			pending.clear()
			for actor_id in players:
				last_received[actor_id] = 0
				processed[actor_id] = 0
				last_target[actor_id] = 0
			event_id = 0
			events.clear()
			_record("started", id)
		"input":
			return _input(id, message)
		"sync":
			pass
		_:
			return _reject("unsupported_command")
	return {"ok": true}

func _input(id: int, message: Dictionary) -> Dictionary:
	if phase != "running":
		return _reject("not_running")
	# Exact allowlist: no client-owned health, damage, loot, phase or player ID.
	var keys := ["session_id", "generation", "kind", "seq", "target_tick", "move", "basic", "technique", "signature", "guard"]
	for key in message:
		if key not in keys:
			return _reject("unexpected_input_field")
	if typeof(message.get("seq")) != TYPE_INT or message.seq <= last_received[id]:
		return _reject("stale_sequence")
	if message.seq > last_received[id] + 1024:
		return _reject("sequence_jump")
	if typeof(message.get("target_tick")) != TYPE_INT or message.target_tick < tick - 6 or message.target_tick > tick + 12:
		return _reject("tick_out_of_bounds")
	if typeof(message.get("move")) != TYPE_ARRAY or message.move.size() != 2:
		return _reject("invalid_movement")
	for component in message.move:
		if typeof(component) not in [TYPE_INT, TYPE_FLOAT] or not is_finite(float(component)):
			return _reject("invalid_movement")
	for action in ["basic", "technique", "signature", "guard"]:
		if typeof(message.get(action)) != TYPE_BOOL:
			return _reject("invalid_action")
	var movement := Vector2(float(message.move[0]), float(message.move[1]))
	if not movement.is_finite():
		return _reject("invalid_movement")
	movement = movement.limit_length(1.0)
	var at_tick: int = maxi(tick + 1, message.target_tick)
	if at_tick < int(last_target[id]):
		return _reject("target_tick_regression")
	var queue: Dictionary = pending.get(id, {})
	var command: Dictionary = {"move": movement, "basic": message.basic, "technique": message.technique, "signature": message.signature, "guard": message.guard, "seq": message.seq}
	if queue.has(at_tick):
		for action in ["technique", "signature", "guard"]:
			command[action] = command[action] or queue[at_tick][action]
	queue[at_tick] = command
	pending[id] = queue
	last_received[id] = message.seq
	last_target[id] = at_tick
	return {"ok": true}

func advance() -> Dictionary:
	if phase != "running":
		return {}
	tick += 1
	var commands: Dictionary = {}
	for id in players:
		var queue: Dictionary = pending.get(id, {})
		if queue.has(tick):
			var command: Dictionary = queue[tick]
			processed[id] = command.seq
			command.erase("seq")
			commands[id] = command
			queue.erase(tick)
	return commands

func snapshot() -> Dictionary:
	var roster: Array[Dictionary] = []
	for id in players:
		roster.append(players[id].duplicate(true))
	return {"schema_version": 1, "session_id": session_id, "generation": generation, "revision": revision, "tick": tick, "phase": phase, "players": roster, "processed_sequences": processed.duplicate(true), "events": events.duplicate(true)}

func _rate_allowed(peer_id: int, now_ms: int) -> bool:
	var window: Dictionary = rate_windows.get(peer_id, {"start": now_ms, "count": 0})
	if now_ms - int(window.start) >= 1000:
		window = {"start": now_ms, "count": 0}
	window.count += 1
	rate_windows[peer_id] = window
	return window.count <= RATE_LIMIT

func _record(kind: String, player_id: int) -> void:
	revision += 1
	event_id += 1
	events.append({"session_id": session_id, "generation": generation, "event_id": event_id, "tick": tick, "kind": kind, "player_id": player_id})
	if events.size() > 128:
		events.pop_front()

func _reject(reason: String) -> Dictionary:
	return {"ok": false, "reason": reason}
