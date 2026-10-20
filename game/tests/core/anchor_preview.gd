extends SceneTree
## Render-only fixture. Run without --headless; output directory must already exist.
var capture_directory: String

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var args := OS.get_cmdline_user_args()
	var argument_index := args.find("--capture-dir")
	if argument_index < 0 or argument_index + 1 >= args.size():
		push_error("Pass -- --capture-dir <existing absolute directory>")
		quit(1)
		return
	capture_directory = args[argument_index + 1]
	if not capture_directory.is_absolute_path() or not DirAccess.dir_exists_absolute(capture_directory):
		push_error("Capture directory must be an existing absolute path")
		quit(1)
		return
	var view: Variant = load("res://main.tscn").instantiate()
	root.add_child(view)
	view.set_process(false)
	view.sim.encounter_provider = func(_tick: int, _context: Dictionary) -> Array: return []
	for mode in ["follow_until_lock", "snapshot_at_lock"]:
		view.sim.reset()
		view.sim.queue_hazard({"id": 1, "name": "Lock Preview", "kind": "donut",
			"origin": Vector2(400, 400), "radius": 130.0, "inner_radius": 0.0,
			"warning_tick": 0, "lock_tick": 30, "resolve_tick": 90,
			"anchor": mode, "target_player_id": 1})
		for index in range(10):
			view.sim.step({1: {"move": Vector2(1, 0)}})
		await capture(view, mode)
		for index in range(25):
			view.sim.step({1: {"move": Vector2(1, 0)}})
		await capture(view, mode + "-locked")
	# An assignment-only log exercises rendering without a mechanic outcome.
	view.sim.configure_roster([{"player_id": 1}, {"player_id": 2}])
	view.sim.queue_hazard({"id": 1, "name": "Cancelled", "kind": "donut",
		"origin": Vector2(400, 400), "radius": 130.0, "inner_radius": 0.0,
		"warning_tick": 0, "lock_tick": 30, "resolve_tick": 90,
		"anchor": "follow_until_lock", "target_player_id": 2})
	view.sim.actors[2].hp = 0
	view.sim.step({})
	await capture(view, "cancelled")
	print("Anchor preview: 5 rendered captures")
	view.queue_free()
	quit()

func capture(view: Node2D, label: String) -> void:
	view.queue_redraw()
	await process_frame
	await RenderingServer.frame_post_draw
	var result := root.get_texture().get_image().save_png(capture_directory.path_join("anchor-" + label + ".png"))
	if result != OK:
		push_error("Failed saving capture: %s" % error_string(result))
		quit(1)
