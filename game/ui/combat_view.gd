extends Node2D
const Simulation = preload("res://core/combat_simulation.gd")
const Geometry = preload("res://combat/hazard_geometry.gd")
const INK := Color("e5edf7")
const MUTED := Color("8c9eb6")
const GOLD := Color("edc47a")
const TEAL := Color("7ce0ce")
var sim := Simulation.new()
var accumulator: float = 0.0
var font: Font = ThemeDB.fallback_font
var capture_frames: int = 0

func _ready() -> void:
	_bind("left", [KEY_A, KEY_LEFT], -1, JOY_AXIS_LEFT_X)
	_bind("right", [KEY_D, KEY_RIGHT], 1, JOY_AXIS_LEFT_X)
	_bind("up", [KEY_W, KEY_UP], -1, JOY_AXIS_LEFT_Y)
	_bind("down", [KEY_S, KEY_DOWN], 1, JOY_AXIS_LEFT_Y)
	_bind("basic", [KEY_J], 0, -1, JOY_BUTTON_A)
	_bind("technique", [KEY_K], 0, -1, JOY_BUTTON_X)
	_bind("signature", [KEY_L], 0, -1, JOY_BUTTON_Y)
	_bind("guard", [KEY_SPACE], 0, -1, JOY_BUTTON_B)
	_bind("restart", [KEY_R], 0, -1, JOY_BUTTON_START)
	var args := OS.get_cmdline_user_args()
	if "--capture-tick" in args:
		for index in range(int(args[args.find("--capture-tick") + 1])):
			sim.step({})

func _bind(action: String, keys: Array, axis_value: float = 0.0, axis: int = -1, button: int = -1) -> void:
	if InputMap.has_action(action):
		return
	InputMap.add_action(action, 0.2)
	for key in keys:
		var event := InputEventKey.new()
		event.physical_keycode = key
		InputMap.action_add_event(action, event)
	if axis >= 0:
		var event := InputEventJoypadMotion.new()
		event.axis = axis
		event.axis_value = axis_value
		InputMap.action_add_event(action, event)
	if button >= 0:
		var event := InputEventJoypadButton.new()
		event.button_index = button
		InputMap.action_add_event(action, event)

func _process(delta: float) -> void:
	if Input.is_action_just_pressed("restart"):
		sim.reset(sim.seed_value)
		accumulator = 0.0
	accumulator += minf(delta, 0.25)
	while accumulator >= 1.0 / Simulation.TICK_RATE:
		var command := {"move": Input.get_vector("left", "right", "up", "down")}
		for action in ["basic", "technique", "signature", "guard"]:
			command[action] = Input.is_action_pressed(action)
		sim.step({1: command})
		accumulator -= 1.0 / Simulation.TICK_RATE
	queue_redraw()
	if "--capture" in OS.get_cmdline_user_args():
		capture_frames += 1
		if capture_frames == 8:
			await RenderingServer.frame_post_draw
			var args := OS.get_cmdline_user_args()
			var destination := args[args.find("--capture") + 1]
			get_viewport().get_texture().get_image().save_png(destination)
			get_tree().quit()

func _text(position: Vector2, value: String, size: int = 18, color: Color = INK) -> void:
	draw_string(font, position, value, HORIZONTAL_ALIGNMENT_LEFT, -1, size, color)

func _draw() -> void:
	draw_rect(Rect2(0, 0, 1280, 800), Color("0a101d"))
	_text(Vector2(60, 42), "L A S T   B E L L", 25, GOLD)
	_text(Vector2(60, 75), "COMBAT EXPERIMENT   /   01", 13, MUTED)
	_text(Vector2(540, 45), "VANGUARD  vs  THE BELLKEEPER", 18)
	_text(Vector2(540, 75), "Solo rehearsal   |   Seed %d   |   %05.1fs" % [sim.seed_value, sim.tick / 60.0], 14, MUTED)
	draw_line(Vector2(60, 103), Vector2(1220, 103), Color("29354b"))
	draw_rect(Simulation.ARENA, Color("101c2d"))
	for x in range(80, 880, 40):
		draw_line(Vector2(x, 140), Vector2(x, 740), Color("182539"))
	for y in range(160, 740, 40):
		draw_line(Vector2(60, y), Vector2(880, y), Color("182539"))
	draw_arc(Simulation.BOSS_POSITION, 155, 0, TAU, 96, Color("33445b"), 1.0, true)
	_text(Vector2(414, 529), "BASIC REACH", 12, MUTED)
	for hazard in sim.hazards:
		_draw_hazard(hazard)
	draw_rect(Simulation.ARENA, Color("4a5b73"), false, 2.0)
	_draw_boss()
	_draw_player()
	_draw_panel()
	_text(Vector2(60, 776), "MOVE  WASD / arrows     •     ATTACK  J / K / L     •     GUARD  Space     •     RESTART  R", 15, MUTED)
	if sim.outcome != "playing":
		draw_rect(Rect2(180, 300, 580, 170), Color(0.03, 0.05, 0.09, 0.96))
		_text(Vector2(220, 350), "BELLKEEPER SILENCED" if sim.outcome == "victory" else "THE BELL STILL TOLLS", 27, GOLD)
		_text(Vector2(220, 389), "Press R / Start to restart the same seed.", 20)
		_text(Vector2(220, 428), "Health, cooldowns and the encounter reset together.", 16, MUTED)

func _draw_hazard(hazard: Dictionary) -> void:
	var progress := clampf(float(sim.tick - int(hazard.warning_tick)) / float(hazard.resolve_tick - hazard.warning_tick), 0.0, 1.0)
	var fill := Color(0.95, 0.37, 0.26, 0.15 + 0.30 * progress)
	var edge := Color("ffb185")
	if hazard.kind == "donut":
		for quad in Geometry.ring_quads(hazard):
			_clipped_polygon(quad, fill)
		for radius in [hazard.inner_radius, hazard.radius]:
			var arc := PackedVector2Array()
			for index in range(129):
				arc.append(hazard.origin + Vector2.from_angle(TAU * index / 128.0) * float(radius))
			_clipped_line(arc, edge)
	else:
		var polygon := Geometry.polygon(hazard)
		_clipped_polygon(polygon, fill)
		polygon.append(polygon[0])
		_clipped_line(polygon, edge)
	var state := "LOCKED"
	if not hazard.get("locked", true):
		state = "TRACKING" if hazard.get("anchor") == "follow_until_lock" else "PENDING LOCK"
	_text(Vector2(80, 169), "%s  •  %s  •  %.1fs" % [hazard.name.to_upper(), state, (hazard.resolve_tick - sim.tick) / 60.0], 17, edge)
	draw_rect(Rect2(80, 181, 220, 4), Color("343e50"))
	draw_rect(Rect2(80, 181, 220 * progress, 4), edge)

func _arena_polygon() -> PackedVector2Array:
	var bounds: Rect2 = Simulation.ARENA
	return PackedVector2Array([bounds.position, Vector2(bounds.end.x, bounds.position.y), bounds.end, Vector2(bounds.position.x, bounds.end.y)])

func _clipped_polygon(polygon: PackedVector2Array, color: Color) -> void:
	for clipped in Geometry2D.intersect_polygons(polygon, _arena_polygon()):
		draw_colored_polygon(clipped, color)

func _clipped_line(line: PackedVector2Array, color: Color) -> void:
	for clipped in Geometry2D.intersect_polyline_with_polygon(line, _arena_polygon()):
		if clipped.size() >= 2:
			draw_polyline(clipped, color, 2, true)

func _draw_boss() -> void:
	var center: Vector2 = Simulation.BOSS_POSITION
	draw_circle(center, 45, Color("263247"))
	draw_arc(center, 49, 0, TAU, 64, GOLD, 2, true)
	draw_colored_polygon(PackedVector2Array([center + Vector2(0, -32), center + Vector2(28, 22), center + Vector2(-28, 22)]), GOLD)
	draw_circle(center + Vector2(0, 18), 5, Color("162033"))
	_text(center + Vector2(-71, -72), "THE BELLKEEPER", 16, GOLD)
	draw_rect(Rect2(center + Vector2(-100, -60), Vector2(200, 6)), Color("354055"))
	draw_rect(Rect2(center + Vector2(-100, -60), Vector2(200.0 * sim.boss_hp / Simulation.BOSS_MAX_HP, 6)), GOLD)

func _draw_player() -> void:
	var position: Vector2 = sim.player.position
	if sim.tick < int(sim.player.guard_until):
		draw_arc(position, 30, 0, TAU, 48, TEAL, 4, true)
	if sim.tick < int(sim.player.attack_until):
		draw_line(position, Simulation.BOSS_POSITION, GOLD, 3, true)
	var color := TEAL if sim.tick >= int(sim.player.immune_until) or sim.tick % 8 < 4 else Color("edc47a")
	draw_circle(position + Vector2(0, 4), 17, Color("060c17"))
	draw_colored_polygon(PackedVector2Array([position + Vector2(0, -19), position + Vector2(15, 12), position + Vector2(0, 5), position + Vector2(-15, 12)]), color)
	_text(position + Vector2(-23, 38), "1 / YOU", 12, color)

func _draw_panel() -> void:
	draw_rect(Rect2(915, 130, 305, 610), Color("111c2d"))
	_text(Vector2(938, 164), "VANGUARD", 22, TEAL)
	_text(Vector2(938, 189), "Close the gap. Bank your strike.", 14, MUTED)
	for index in range(5):
		draw_rect(Rect2(940 + index * 46, 212, 36, 13), TEAL if index < int(sim.player.hp) else Color("334055"))
	_text(Vector2(938, 257), "CHARGE   %d / 6" % sim.player.resource, 15, GOLD)
	for index in range(6):
		draw_circle(Vector2(947 + index * 39, 276), 7, GOLD if index < int(sim.player.resource) else Color("334055"))
	var actions := ["basic", "technique", "signature", "guard"]
	var labels := ["J / A     Basic", "K / X     Technique", "L / Y     Signature", "Space / B     Guard"]
	for index in range(4):
		var action: String = actions[index]
		var remaining := maxi(0, int(sim.player.ready_at[action]) - sim.tick)
		var status := "READY" if remaining == 0 else "%.1fs" % (remaining / 60.0)
		if action == "technique" and remaining == 0 and int(sim.player.resource) < 3:
			status = "3 CHARGE"
		_text(Vector2(938, 323 + index * 49), labels[index], 16)
		_text(Vector2(938, 342 + index * 49), status, 12, TEAL if status == "READY" else MUTED)
	draw_line(Vector2(938, 513), Vector2(1196, 513), Color("334055"))
	_text(Vector2(938, 543), "LAST RESOLUTION", 13, MUTED)
	var event: Dictionary = {}
	for index in range(sim.events.size() - 1, -1, -1):
		var candidate: Dictionary = sim.events[index]
		if candidate.type != "mechanic_resolved" or candidate.player_id != sim.player.id:
			continue
		if event.is_empty():
			event = candidate
		if sim.outcome != "defeat" or candidate.outcome == "hit":
			event = candidate
			break
	if event.is_empty():
		_text(Vector2(938, 575), "Read the warning. Find safety.", 15)
		_text(Vector2(938, 603), "Hold J to repeat Basic in range.", 14, MUTED)
		_text(Vector2(938, 627), "Guard protects for 0.4 seconds.", 14, MUTED)
	else:
		_text(Vector2(938, 575), event.attack_name, 19, GOLD)
		_text(Vector2(938, 604), "%s  /  %.2fs" % [event.outcome.to_upper(), event.tick / 60.0], 15, TEAL if event.outcome != "hit" else Color("ffb185"))
		_text(Vector2(938, 632), "Leave the cone." if "cone" in event.required_behavior else "Use the center or outer edge.", 14, MUTED)
		_text(Vector2(938, 658), "Guard does not erase a missed", 13, MUTED)
		_text(Vector2(938, 677), "positioning requirement.", 13, MUTED)
