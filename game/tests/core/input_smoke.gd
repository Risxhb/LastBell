extends SceneTree
## Run with --fixed-fps 60 so the real view's accumulator receives reproducible time.
var failures: int = 0

func _initialize() -> void:
	call_deferred("run")

func check(condition: bool, message: String) -> void:
	if not condition:
		failures += 1
		push_error(message)

func frames(count: int) -> void:
	for index in range(count):
		await process_frame

func run() -> void:
	var view: Variant = load("res://main.tscn").instantiate()
	root.add_child(view)
	await frames(2)
	var start: Vector2 = view.sim.player.position
	Input.action_press("up")
	await frames(30)
	Input.action_release("up")
	check(view.sim.player.position.y < start.y - 100, "View routes movement into simulation")
	Input.action_press("basic")
	await frames(25)
	Input.action_release("basic")
	check(view.sim.boss_hp < 180 and view.sim.player.resource > 0, "Hold Basic routes attacks and charge into HUD state")
	Input.action_press("guard")
	await frames(2)
	Input.action_release("guard")
	check(view.sim.player.ready_at.guard > view.sim.tick, "View routes guard input")
	Input.action_press("restart")
	await frames(1)
	Input.action_release("restart")
	await frames(1)
	check(view.sim.tick <= 3 and view.sim.player.hp == 5 and view.sim.boss_hp == 180, "Restart input restores encounter immediately")
	check(view.sim.player.resource == 0 and view.sim.player.ready_at.guard == 0, "Restart input clears resource and cooldown")
	print("Input/view smoke: 5 checks, %d failures" % failures)
	view.queue_free()
	quit(0 if failures == 0 else 1)
