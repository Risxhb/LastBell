extends Node
## ENet adapter; the model exposes the same commands independently of ENet.
signal state_changed(snapshot: Dictionary)
signal status_changed(message: String)
signal rejected(reason: String)
signal commands_ready(tick: int, commands: Dictionary)
const State = preload("res://network/session_state.gd")
var state: RefCounted
var transport: ENetMultiplayerPeer
var latest: Dictionary = {}
var local_player_id := 0
var is_host := false
var connected := false
var closing := false
var accumulator := 0.0
var input_seq := 0
var received_events: Dictionary = {}
var max_generation := -1
var max_revision := -1
var pending_rejections: Dictionary = {}

func _ready() -> void:
	multiplayer.peer_connected.connect(_peer_connected)
	multiplayer.peer_disconnected.connect(_peer_disconnected)
	multiplayer.connected_to_server.connect(_connected_to_server)
	multiplayer.connection_failed.connect(_connection_failed)
	multiplayer.server_disconnected.connect(_server_disconnected)

func host(port: int, bind_address: String = "*") -> Error:
	is_host = true
	transport = ENetMultiplayerPeer.new()
	transport.set_bind_ip(bind_address)
	# Allow overflow peers briefly so the application can explain rejection.
	var error := transport.create_server(port, 16, 2)
	if error != OK:
		status_changed.emit("Host failed: " + error_string(error))
		return error
	multiplayer.multiplayer_peer = transport
	state = State.new()
	local_player_id = state.join(1).player_id
	connected = true
	_publish()
	status_changed.emit("Hosting on port %d" % port)
	return OK

func join(address: String, port: int) -> Error:
	is_host = false
	transport = ENetMultiplayerPeer.new()
	var error := transport.create_client(address, port, 2)
	if error != OK:
		status_changed.emit("Join failed: " + error_string(error))
		return error
	multiplayer.multiplayer_peer = transport
	status_changed.emit("Connecting to %s:%d" % [address, port])
	return OK

func command(kind: String, fields: Dictionary = {}) -> Dictionary:
	if not connected or latest.is_empty():
		return {"ok": false, "reason": "not_connected"}
	var message: Dictionary = fields.duplicate(true)
	message.merge({"kind": kind, "session_id": latest.session_id, "generation": latest.generation}, true)
	if is_host:
		return _dispatch(1, message)
	_request.rpc_id(1, message)
	return {"ok": true, "pending": true}

func submit_input(move: Vector2, basic: bool, technique: bool, signature: bool, guard: bool) -> void:
	if not connected or latest.get("phase") != "running":
		return
	input_seq += 1
	var message := {"kind": "input", "session_id": latest.session_id, "generation": latest.generation, "seq": input_seq, "target_tick": int(latest.tick) + 3, "move": [move.x, move.y], "basic": basic, "technique": technique, "signature": signature, "guard": guard}
	if is_host:
		_dispatch(1, message)
	else:
		_input_packet.rpc_id(1, message)

func _process(delta: float) -> void:
	if closing or not is_host or state == null:
		return
	accumulator += minf(delta, 0.25)
	while accumulator >= 1.0 / 60.0:
		accumulator -= 1.0 / 60.0
		if state.phase == "running":
			var commands: Dictionary = state.advance()
			commands_ready.emit(state.tick, commands)
			if state.tick % 3 == 0:
				_publish()
	var now := Time.get_ticks_msec()
	for peer_id in pending_rejections.keys():
		if now >= int(pending_rejections[peer_id]):
			transport.disconnect_peer(peer_id)
			pending_rejections.erase(peer_id)

func _peer_connected(peer_id: int) -> void:
	if not is_host or closing:
		return
	transport.get_peer(peer_id).set_timeout(5, 1000, 2000)
	var result: Dictionary = state.join(peer_id)
	if not result.ok:
		_denied.rpc_id(peer_id, result.reason)
		pending_rejections[peer_id] = Time.get_ticks_msec() + 250
		status_changed.emit("Rejected peer: " + result.reason)
		return
	_welcome.rpc_id(peer_id, result.player_id)
	_publish()

func _peer_disconnected(peer_id: int) -> void:
	if not is_host or closing:
		return
	pending_rejections.erase(peer_id)
	state.leave(peer_id)
	_publish()
	status_changed.emit("Peer disconnected: %d" % peer_id)

func _connected_to_server() -> void:
	transport.get_peer(1).set_timeout(5, 1000, 2000)
	connected = true
	status_changed.emit("Connected; awaiting authoritative roster")

func _connection_failed() -> void:
	connected = false
	status_changed.emit("Connection failed")
	rejected.emit("connection_failed")

func _server_disconnected() -> void:
	connected = false
	latest = {}
	status_changed.emit("Session ended: host disconnected")
	rejected.emit("host_disconnected")

@rpc("any_peer", "call_remote", "reliable", 0)
func _request(message: Dictionary) -> void:
	if is_host:
		_dispatch(multiplayer.get_remote_sender_id(), message)

@rpc("any_peer", "call_remote", "reliable", 1)
func _input_packet(message: Dictionary) -> void:
	if is_host:
		_dispatch(multiplayer.get_remote_sender_id(), message)

func _dispatch(peer_id: int, message: Dictionary) -> Dictionary:
	var result: Dictionary = state.request(peer_id, message, Time.get_ticks_msec())
	if not result.ok:
		if peer_id == 1:
			rejected.emit(result.reason)
		else:
			_denied.rpc_id(peer_id, result.reason)
	elif message.get("kind") != "input":
		_publish()
	return result

func _publish() -> void:
	latest = state.snapshot()
	state_changed.emit(latest.duplicate(true))
	for peer_id in state.peer_players:
		if peer_id != 1:
			_snapshot.rpc_id(peer_id, latest)

@rpc("authority", "call_remote", "reliable", 0)
func _welcome(player_id: int) -> void:
	if not is_host:
		local_player_id = player_id

@rpc("authority", "call_remote", "reliable", 0)
func _snapshot(snapshot: Dictionary) -> void:
	if is_host or snapshot.get("schema_version") != 1:
		return
	if not latest.is_empty() and snapshot.session_id != latest.session_id:
		return
	var gen: int = snapshot.generation
	var rev: int = snapshot.revision
	if gen < max_generation or (gen == max_generation and rev < max_revision):
		return
	if gen > max_generation:
		input_seq = 0
		received_events.clear()
	max_generation = gen
	max_revision = rev
	latest = snapshot.duplicate(true)
	for event in latest.events:
		received_events[event.event_id] = true
	state_changed.emit(latest.duplicate(true))

@rpc("authority", "call_remote", "reliable", 0)
func _denied(reason: String) -> void:
	rejected.emit(reason)
	status_changed.emit("Request rejected: " + reason)

func shutdown() -> void:
	if closing:
		return
	closing = true
	connected = false
	if transport != null:
		transport.close()
	if is_inside_tree() and multiplayer != null:
		multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	latest.clear()
	status_changed.emit("Session closed")
