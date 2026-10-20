extends SceneTree
var view: Control
var checks := 0
var failures := 0

func _initialize() -> void:
	call_deferred("_run")

func check(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		printerr("FAIL: " + label)

func _run() -> void:
	view = load("res://scenes/network_test/session_test.tscn").instantiate()
	root.add_child(view)
	await process_frame
	check(view.ready_button.disabled and view.start_button.disabled, "initial buttons")
	view.host_button.emit_signal("pressed")
	await process_frame
	check(view.session.connected and view.session.is_host, "host button creates server")
	check("Slot 1" in view.roster.text and "(you)" in view.roster.text, "authoritative roster displayed")
	view.start_button.emit_signal("pressed")
	check(view.session.latest.phase == "lobby" and "players_not_ready" in view.status.text, "unready status displayed")
	view.ready_button.emit_signal("pressed")
	view.start_button.emit_signal("pressed")
	await process_frame
	check(view.session.latest.phase == "running" and view.ready_button.disabled, "ready and start buttons")
	if "--capture" in OS.get_cmdline_user_args() and DisplayServer.get_name() != "headless":
		await process_frame
		await RenderingServer.frame_post_draw
		var args := OS.get_cmdline_user_args()
		var index := args.find("--capture")
		get_root().get_texture().get_image().save_png(args[index + 1])
	view.close_button.emit_signal("pressed")
	check(not view.session.connected and view.ready_button.disabled, "close button stops session")
	print(JSON.stringify({"suite": "session_view", "checks": checks, "failures": failures}))
	root.remove_child(view)
	view.free()
	quit(0 if failures == 0 else 1)
