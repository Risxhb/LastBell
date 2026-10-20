class_name HazardGeometry
extends RefCounted
## All angles are radians. Boundaries count as dangerous.

static func contains_point(shape: Dictionary, point: Vector2) -> bool:
	var offset: Vector2 = point - shape.origin
	var distance: float = offset.length()
	if shape.kind == "donut":
		return distance >= shape.inner_radius and distance <= shape.radius
	if distance > shape.radius:
		return false
	if distance < 0.00001:
		return true
	# Vector2 uses float32 components; include the mathematical boundary despite rounding.
	return absf(wrapf(offset.angle() - float(shape.direction), -PI, PI)) <= float(shape.half_angle) + 0.000001

static func polygon(shape: Dictionary) -> PackedVector2Array:
	var points := PackedVector2Array()
	var origin: Vector2 = shape.origin
	if shape.kind == "donut":
		# The renderer draws this as individual quads, preserving the hole.
		return points
	points.append(origin)
	for index in range(65):
		var angle: float = shape.direction - shape.half_angle + 2.0 * shape.half_angle * index / 64.0
		points.append(origin + Vector2.from_angle(angle) * float(shape.radius))
	return points

static func ring_quads(shape: Dictionary) -> Array[PackedVector2Array]:
	var quads: Array[PackedVector2Array] = []
	for index in range(128):
		var a := Vector2.from_angle(TAU * index / 128.0)
		var b := Vector2.from_angle(TAU * (index + 1) / 128.0)
		quads.append(PackedVector2Array([
			shape.origin + a * float(shape.inner_radius), shape.origin + a * float(shape.radius),
			shape.origin + b * float(shape.radius), shape.origin + b * float(shape.inner_radius)]))
	return quads
