extends Control
const Session = preload("res://network/enet_session.gd")
var session: Node
var address: LineEdit
var port: SpinBox
var roster: Label
var status: Label
var ready_button: Button
var start_button: Button
var close_button: Button
var host_button: Button
var join_button: Button
var is_ready := false
var sample_time := 0.0
var technique_press := false
var signature_press := false
var guard_press := false

func _ready() -> void:
	var panel := VBoxContainer.new()
	panel.position = Vector2(32, 32)
	panel.add_theme_constant_override("separation", 12)
	add_child(panel)
	var title := Label.new()
	title.text = "Last Bell — Standalone Session Experiment"
	panel.add_child(title)
	var help := Label.new()
	help.text = "Host or join by IP. Eight total slots; duplicate Vanguards allowed.\nThis test sends input and acknowledgments; it does not run combat."
	panel.add_child(help)
	address = LineEdit.new()
	address.text = "127.0.0.1"
	address.placeholder_text = "Host IP address"
	panel.add_child(address)
	port = SpinBox.new()
	port.min_value = 1024
	port.max_value = 65535
	port.value = 27841
	panel.add_child(port)
	var buttons := HBoxContainer.new()
	panel.add_child(buttons)
	host_button = _button(buttons, "Host", _host)
	join_button = _button(buttons, "Join", _join)
	ready_button = _button(buttons, "Ready / Unready", _ready_request)
	start_button = _button(buttons, "Start (host; everyone ready)", _start_request)
	close_button = _button(buttons, "Close session", _close)
	status = Label.new()
	status.text = "Not connected"
	panel.add_child(status)
	roster = Label.new()
	panel.add_child(roster)
	var controls := Label.new()
	controls.text = "Running: WASD / arrows, hold J, press K / L / Space.\nClose and reopen the scene for a new session. No combat integration yet."
	panel.add_child(controls)
	session = Session.new()
	session.name = "Session"
	add_child(session)
	session.state_changed.connect(_state)
	session.status_changed.connect(func(value: String): status.text = value)
	session.rejected.connect(func(value: String): status.text = "Rejected / ended: " + value)
	ready_button.disabled = true
	start_button.disabled = true

func _button(parent: Node, text: String, action: Callable) -> Button:
	var button := Button.new()
	button.text = text
	button.pressed.connect(action)
	parent.add_child(button)
	return button

func _host() -> void:
	if session.host(int(port.value)) == OK:
		host_button.disabled = true
		join_button.disabled = true

func _join() -> void:
	if session.join(address.text.strip_edges(), int(port.value)) == OK:
		host_button.disabled = true
		join_button.disabled = true

func _ready_request() -> void:
	is_ready = not is_ready
	session.command("ready", {"ready": is_ready})

func _start_request() -> void:
	session.command("start")

func _close() -> void:
	session.shutdown()
	ready_button.disabled = true
	start_button.disabled = true

func _state(snapshot: Dictionary) -> void:
	ready_button.disabled = snapshot.phase != "lobby"
	start_button.disabled = not session.is_host or snapshot.phase != "lobby"
	var text := "Phase: %s | tick: %d | generation: %d\n" % [snapshot.phase, snapshot.tick, snapshot.generation]
	for player in snapshot.players:
		text += "Slot %d | ID %d | %s | %s%s\n" % [player.slot, player.player_id, player.class_id, "READY" if player.ready else "waiting", " (you)" if player.player_id == session.local_player_id else ""]
	text += "Processed inputs: " + str(snapshot.processed_sequences)
	roster.text = text

func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_K:
			technique_press = true
		if event.keycode == KEY_L:
			signature_press = true
		if event.keycode == KEY_SPACE:
			guard_press = true

func _process(delta: float) -> void:
	if session == null or session.latest.get("phase") != "running" or not session.connected:
		return
	sample_time += delta
	if sample_time < 1.0 / 30.0:
		return
	sample_time = 0.0
	var move := Vector2(float(Input.is_physical_key_pressed(KEY_D) or Input.is_physical_key_pressed(KEY_RIGHT)) - float(Input.is_physical_key_pressed(KEY_A) or Input.is_physical_key_pressed(KEY_LEFT)), float(Input.is_physical_key_pressed(KEY_S) or Input.is_physical_key_pressed(KEY_DOWN)) - float(Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP)))
	session.submit_input(move, Input.is_physical_key_pressed(KEY_J), technique_press, signature_press, guard_press)
	technique_press = false
	signature_press = false
	guard_press = false

func _exit_tree() -> void:
	if session != null:
		session.shutdown()
