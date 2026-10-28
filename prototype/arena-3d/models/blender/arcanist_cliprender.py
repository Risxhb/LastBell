# blender -b --factory-startup -P arcanist_cliprender.py -- <Arcanist.glb> <out dir> Clip [Clip ...]
# Review renders for arcanist_addclips.py: imports the (merged) GLB the way any glTF reader would, plays each named clip
# and renders every frame from three cameras into <out dir>/<Clip>/<view>_<frame>.png (Workbench, textured, plain
# backdrop, floor with contact shadow). Views: 'high' = gameplay-like (57 deg down, from behind his left shoulder, the
# character about 1/6 of the frame height), 'front' = close front three-quarter from his right, 'side' = his right
# profile. arcanist_clipsheet.py turns them into contact sheets and GIFs.
import bpy, sys, os, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, out, clips = args[0], args[1], args[2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)   # the importer's bone shape
for mat in bpy.data.materials:     # Workbench shows the active image node: make it the base colour, not the emissive mask
    b = mat.node_tree and next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b and b.inputs['Base Color'].links: mat.node_tree.nodes.active = b.inputs['Base Color'].links[0].from_node
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.6
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for t in arm.animation_data.nla_tracks: t.mute = True
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.45
sh.show_cavity = False; sh.background_type = 'VIEWPORT'; sh.background_color = (0.62, 0.64, 0.68)
sc.display.shadow_shift = 0.02
sc.render.film_transparent = False
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); floor = bpy.context.object
m = bpy.data.materials.new('Floor'); m.diffuse_color = (0.42, 0.43, 0.46, 1); floor.data.materials.append(m)
# floor marks to judge sliding: a thin line along the stance heel line (y 0.06) and one down the centre line
mm = bpy.data.materials.new('Mark'); mm.diffuse_color = (0.22, 0.22, 0.25, 1)
for loc, scale in (((0, 0.06, 0.001), (0.35, 0.004, 1)), ((0, -0.1, 0.001), (0.004, 0.5, 1))):
    bpy.ops.mesh.primitive_plane_add(size=2, location=loc); s = bpy.context.object; s.scale = scale; s.data.materials.append(mm)
def camera(name, loc, target, lens, res):
    cd = bpy.data.cameras.new(name); cd.lens = lens; cd.sensor_fit = 'VERTICAL'; cd.sensor_height = 24
    c = bpy.data.objects.new(name, cd); sc.collection.objects.link(c); c.location = loc
    c.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return c, res
def orbit_pos(az, el, dist, tgt):
    # az: degrees from his front (-Y) toward his left (+X)
    a, e = math.radians(az), math.radians(el)
    return tuple(Vector(tgt) + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist)
VIEWS = {
    # 24 mm-high sensor, 50 mm lens: 27 deg vertical field; at 14 m the frame is ~6.7 m tall, so 1.8 m is ~1/6 of it
    'high': camera('high', orbit_pos(150, 57, 14.0, (0, 0, 0.9)), (0, 0, 0.9), 50, (800, 600)),
    'front': camera('front', orbit_pos(-40, 14, 5.2, (0, -0.1, 1.05)), (0, -0.1, 1.05), 50, (560, 640)),
    'side': camera('side', orbit_pos(-90, 6, 5.2, (0, -0.15, 1.05)), (0, -0.15, 1.05), 50, (560, 640)),
}
for name in clips:
    act = bpy.data.actions[name]; arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1]))
    os.makedirs(os.path.join(out, name), exist_ok=True)
    for view, (cam, (rx, ry)) in VIEWS.items():
        sc.camera = cam; sc.render.resolution_x, sc.render.resolution_y = rx, ry
        for f in range(f0, f1 + 1):
            sc.frame_set(f); sc.render.filepath = os.path.join(out, name, f'{view}_{f - f0:03d}.png')
            bpy.ops.render.render(write_still=True)
    print('RENDERED', name, f1 - f0 + 1, 'frames')
