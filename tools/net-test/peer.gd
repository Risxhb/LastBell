extends SceneTree
const Session = preload("res://network/enet_session.gd")
var session: Node
var role := "client"
var scenario := "smoke"
var expected := 2
var report := ""
var port := 27841
var elapsed := 0.0
var ready_sent := false
var started := false
var unready_checked := false
var max_players := 0
var max_tick := 0
var saw_ack := false
var saw_departure := false
var reason := ""
var failure := ""
var requests := 0
var steps_with_commands := 0
var max_commands := 0
var crash_marked := false
var logged_revision := -1
var logged_generation := -1

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(0, args.size() - 1, 2):
		match args[i]:
			"--role": role = args[i + 1]
			"--scenario": scenario = args[i + 1]
			"--expected": expected = int(args[i + 1])
			"--report": report = args[i + 1]
			"--port": port = int(args[i + 1])
	call_deferred("_start")

func _start() -> void:
	session = Session.new()
	session.name = "Session"
	root.add_child(session)
	session.rejected.connect(_rejected)
	session.status_changed.connect(func(text: String): print(role + ": " + text))
	session.commands_ready.connect(_commands)
	session.state_changed.connect(_state_changed)
	var result: Error
	if role == "host":
		result = session.host(port, "127.0.0.1")
	else:
		result = session.join("127.0.0.1", port)
	if result != OK:
		_finish(false, "transport_setup_failed")

func _process(delta: float) -> bool:
	elapsed += delta
	if session == null:
		return false
	var snapshot: Dictionary = session.latest
	if not snapshot.is_empty():
		max_players = maxi(max_players, snapshot.players.size())
		max_tick = maxi(max_tick, snapshot.tick)
		if max_players == expected and snapshot.players.size() < expected:
			saw_departure = true
		if role != "overflow" and snapshot.phase == "lobby" and elapsed >= 0.8 and not ready_sent:
			session.command("class", {"class_id": "vanguard"})
			session.command("ready", {"ready": true})
			ready_sent = true
		if role == "host" and not unready_checked:
			var early: Dictionary = session.command("start")
			if early.get("reason") != "players_not_ready":
				failure = "unready_start_accepted"
			unready_checked = true
		if role == "host" and snapshot.phase == "lobby" and snapshot.players.size() == expected:
			var all_ready := true
			for player in snapshot.players:
				all_ready = all_ready and player.ready
			if all_ready and (scenario != "capacity" or elapsed >= 2.5):
				var result: Dictionary = session.command("start")
				started = result.ok
		if snapshot.phase == "running" and role != "overflow":
			started = true
			# At most 60 requests/second, held Basic and occasional press.
			if int(elapsed * 30.0) > requests:
				requests = int(elapsed * 30.0)
				session.submit_input(Vector2(0.25, 0), true, requests % 30 == 0, false, false)
			var processed: Dictionary = snapshot.processed_sequences
			if int(processed.get(session.local_player_id, 0)) > 0:
				saw_ack = true
	if scenario in ["crash-host", "crash-client"] and max_tick >= 90 and not crash_marked:
		if (scenario == "crash-host" and role == "host") or (scenario == "crash-client" and role == "client"):
			var marker := FileAccess.open(report + ".crash-ready", FileAccess.WRITE)
			marker.store_string("ready")
			crash_marked = true
	if scenario == "host-loss" and role == "host" and max_tick >= 90:
		_finish(failure.is_empty() and started, "intentional_host_shutdown")
		return false
	if scenario == "disconnect" and role == "client" and max_tick >= 90:
		_finish(failure.is_empty() and saw_ack, "intentional_client_shutdown")
		return false
	if role == "overflow" and reason in ["session_full", "encounter_in_progress"]:
		# Capacity scenario holds lobby until overflow is admitted/rejected;
		# running rejection alone must not count as proof of full capacity.
		_finish(reason == "session_full", reason)
		return false
	if scenario in ["host-loss", "crash-host"] and role == "client" and reason == "host_disconnected":
		_finish(started and saw_ack and failure.is_empty(), reason)
		return false
	if elapsed > (10.0 if scenario in ["crash-host", "crash-client"] else 6.0):
		var ok := started and max_players == expected and saw_ack and failure.is_empty()
		if role == "host":
			ok = ok and steps_with_commands > 0
		if scenario in ["disconnect", "crash-client"] and role == "host":
			ok = ok and saw_departure
		if scenario in ["host-loss", "crash-host"] and role == "client":
			ok = false
		_finish(ok, failure if not failure.is_empty() else "completed")
	if elapsed > 14.0:
		_finish(false, "timeout")
	return false

func _state_changed(snapshot: Dictionary) -> void:
	if snapshot.revision != logged_revision or snapshot.generation != logged_generation:
		logged_revision = snapshot.revision
		logged_generation = snapshot.generation
		print(JSON.stringify({"type": "session_state", "role": role, "tick": snapshot.tick, "generation": snapshot.generation, "revision": snapshot.revision, "players": snapshot.players.size(), "events": snapshot.events}))

func _commands(_tick: int, commands: Dictionary) -> void:
	if not commands.is_empty():
		steps_with_commands += 1
		max_commands = maxi(max_commands, commands.size())

func _rejected(value: String) -> void:
	reason = value
	if value not in ["players_not_ready", "host_disconnected", "session_full", "encounter_in_progress"]:
		failure = value

func _finish(ok: bool, result: String) -> void:
	var data := {"role": role, "scenario": scenario, "passed": ok, "reason": result, "max_players": max_players, "max_tick": max_tick, "processed_ack": saw_ack, "commands_ticks": steps_with_commands, "max_commands_per_tick": max_commands, "departure": saw_departure, "elapsed_seconds": elapsed}
	print(JSON.stringify(data))
	if not report.is_empty():
		var file := FileAccess.open(report, FileAccess.WRITE)
		if file != null:
			file.store_string(JSON.stringify(data, "  "))
	session.shutdown()
	quit(0 if ok else 1)
