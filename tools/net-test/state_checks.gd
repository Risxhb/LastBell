extends SceneTree
const State = preload("res://network/session_state.gd")
var checks := 0
var failures := 0

func _initialize() -> void:
	call_deferred("_run")

func expect(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		printerr("FAIL: " + label)

func envelope(s: RefCounted, kind: String, fields: Dictionary = {}) -> Dictionary:
	var message := {"session_id": s.session_id, "generation": s.generation, "kind": kind}
	message.merge(fields)
	return message

func packet(s: RefCounted, seq: int, fields: Dictionary = {}) -> Dictionary:
	var message := envelope(s, "input", {"seq": seq, "target_tick": s.tick + 1, "move": [1.0, 1.0], "basic": true, "technique": true, "signature": false, "guard": true})
	message.merge(fields, true)
	return message

func _run() -> void:
	var s = State.new("unit-test")
	for peer in range(1, 9):
		expect(s.join(peer).ok, "join %d" % peer)
	expect(not s.join(9).ok, "reject ninth")
	expect(s.snapshot().players.size() == 8, "eight total including host")
	expect(not s.join(2).ok, "no duplicate connection")
	expect(s.request(99, envelope(s, "ready", {"ready": true}), 0).reason == "unknown_sender", "authenticate sender")
	expect(s.request(2, envelope(s, "start"), 0).reason == "host_only", "host controls start")
	expect(s.request(1, envelope(s, "start"), 0).reason == "players_not_ready", "unready gate")
	expect(s.request(2, envelope(s, "ready", {"ready": "true"}), 0).reason == "invalid_ready", "strict bool")
	expect(s.request(2, envelope(s, "class", {"class_id": "arcanist"}), 0).reason == "invalid_class", "class allowlist")
	for peer in range(1, 9):
		expect(s.request(peer, envelope(s, "class", {"class_id": "vanguard"}), 0).ok, "duplicate vanguards allowed")
		expect(s.request(peer, envelope(s, "ready", {"ready": true}), 0).ok, "ready")
	var old_packet := packet(s, 1)
	expect(s.request(1, envelope(s, "start"), 0).ok, "host start")
	expect(s.generation == 1 and s.phase == "running", "generation on start")
	expect(s.request(2, old_packet, 0).reason == "wrong_generation", "old generation rejected")
	expect(not s.join(9).ok, "no mid-encounter join")
	expect(s.request(2, envelope(s, "class", {"class_id": "vanguard"}), 0).reason == "encounter_in_progress", "class frozen")
	var forged := packet(s, 1, {"player_id": 1})
	expect(s.request(2, forged, 0).reason == "unexpected_input_field", "cannot forge actor")
	for field in ["health", "damage", "loot", "phase"]:
		expect(s.request(2, packet(s, 1, {field: 999}), 0).reason == "unexpected_input_field", "reject authoritative " + field)
	expect(s.request(2, packet(s, 1, {"session_id": "other"}), 0).reason == "wrong_session", "session isolation")
	expect(s.request(2, packet(s, 1, {"move": [NAN, 0]}), 0).reason == "invalid_movement", "reject NaN")
	expect(s.request(2, packet(s, 1, {"move": [INF, 0]}), 0).reason == "invalid_movement", "reject infinity")
	expect(s.request(2, packet(s, 1, {"move": ["1", 0]}), 0).reason == "invalid_movement", "reject string movement")
	expect(s.request(2, packet(s, 1, {"move": [0]}), 0).reason == "invalid_movement", "movement length")
	expect(s.request(2, packet(s, 1, {"basic": 1}), 0).reason == "invalid_action", "strict action bool")
	expect(s.request(2, packet(s, 1, {"target_tick": 1000}), 0).reason == "tick_out_of_bounds", "future tick")
	expect(s.request(2, packet(s, 1, {"target_tick": -7}), 0).reason == "tick_out_of_bounds", "past tick")
	expect(s.request(2, packet(s, 2000), 0).reason == "sequence_jump", "bounded sequence jump")
	expect(s.request(2, packet(s, 1), 0).ok, "accept input")
	expect(s.request(2, packet(s, 1), 0).reason == "stale_sequence", "deduplicate press")
	expect(s.processed[2] == 0, "not acknowledged before tick")
	var commands: Dictionary = s.advance()
	expect(commands.has(2) and not commands.has(1), "sender owns ID, missing neutral")
	expect(is_equal_approx(commands[2].move.length(), 1.0), "normalize diagonal")
	expect(commands[2].guard and commands[2].technique, "press received")
	expect(s.processed[2] == 1, "processed acknowledgment")
	expect(s.advance().is_empty(), "press consumed once")
	expect(s.request(2, packet(s, 2, {"technique": false}), 1000).ok, "held basic next sample")
	expect(s.request(2, packet(s, 3, {"technique": true}), 1000).ok, "coalesced press")
	commands = s.advance()
	expect(commands[2].technique and s.processed[2] == 3, "latest sample keeps press and ack")
	expect(s.request(2, packet(s, 4, {"target_tick": s.tick + 5}), 1000).ok, "queue future input")
	expect(s.request(2, packet(s, 5, {"target_tick": s.tick + 1}), 1000).reason == "target_tick_regression", "no future input can rewind processed acknowledgments")
	for index in range(5):
		s.advance()
	expect(s.processed[2] == 4, "acknowledgments remain monotonic")
	var detached: Dictionary = s.snapshot()
	detached.players[0].ready = false
	expect(s.players[1].ready, "detached roster")
	expect(s.request(3, packet(s, 1), 1000).ok, "queued disconnect input")
	s.leave(3)
	expect(not s.advance().has(3) and not s.pending.has(3), "disconnect discards input immediately")
	var ids := State.new("slots")
	expect(ids.join(1).player_id == 1, "host id")
	expect(ids.join(55).player_id == 2, "stable id differs from peer")
	ids.leave(55)
	var returning: Dictionary = ids.join(56)
	expect(returning.player_id == 3 and returning.slot == 2, "slot reuse does not reuse identity")
	var rates := State.new("rate")
	rates.join(1)
	for index in range(120):
		expect(rates.request(1, envelope(rates, "sync"), 0).ok, "within rate")
	expect(rates.request(1, envelope(rates, "sync"), 999).reason == "rate_limited", "rate overflow")
	expect(rates.request(1, envelope(rates, "sync"), 1000).ok, "window resets")
	print(JSON.stringify({"suite": "session_state", "checks": checks, "failures": failures}))
	quit(0 if failures == 0 else 1)
