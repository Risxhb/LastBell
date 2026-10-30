# blender -b -P glb_preview.py -- <in.glb> <out.png>
# Quick look at a Meshy GLB: front and side views side by side (Eevee, plain light), plus its size and triangle count.
import bpy, sys, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
tris = sum(len(o.data.polygons) for o in meshes)
print('BOUNDS', tuple(round(v, 3) for v in lo), tuple(round(v, 3) for v in hi), 'FACES', tris, 'MESHES', len(meshes))
c = (lo + hi) / 2; size = max(hi - lo)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1400, 900
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.9, 0.88, 0.82, 1); sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.2, 0.6); sun.data.energy = 3
# duplicate rotated 90 degrees beside the original for a side view
off = (hi.x - lo.x) * 0.5 + (hi.y - lo.y) * 0.5 + size * 0.15
for o in meshes:
    d = o.copy(); d.data = o.data; sc.collection.objects.link(d)
    d.matrix_world = __import__('mathutils').Matrix.Translation((off * 2, 0, 0)) @ __import__('mathutils').Matrix.Rotation(math.radians(90), 4, 'Z') @ o.matrix_world
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(size * 1.15, (off * 2 + size) * 1.15 * 900 / 1400)
cam.location = (c.x + off, lo.y - size * 3, c.z); cam.rotation_euler = (math.radians(90), 0, 0)
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
