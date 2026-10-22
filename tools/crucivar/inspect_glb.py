# blender -b -P inspect_glb.py -- <in.glb> <out prefix>
# Quick look at a Meshy GLB: size, triangle count, materials, and front / side / back renders.
import bpy, sys, math
from mathutils import Vector
src, out = sys.argv[sys.argv.index('--') + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH']
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
print('INFO', src.split('\\')[-1], 'objects', len(meshes), 'tris', tris, 'min', tuple(round(v, 3) for v in lo), 'max', tuple(round(v, 3) for v in hi))
print('MATS', [m.name for m in bpy.data.materials], 'IMGS', [(i.name, tuple(i.size)) for i in bpy.data.images])
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 700
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.9, 0.87, 0.8, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.2
sun = bpy.data.objects.new('S', bpy.data.lights.new('S', 'SUN')); sc.collection.objects.link(sun); sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
cam = bpy.data.objects.new('C', bpy.data.cameras.new('C')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; size = hi - lo; cam.data.ortho_scale = max(size.x, size.y, size.z) * 1.15
ctr = (lo + hi) / 2
for name, yaw in (('front', 0), ('side', 90), ('back', 180)):
    a = math.radians(yaw); d = max(size) * 3
    cam.location = ctr + Vector((math.sin(a) * d, -math.cos(a) * d, 0))
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = f'{out}_{name}.png'; bpy.ops.render.render(write_still=True)
