# blender -b --factory-startup -P aurelion_clipsheet.py -- <Aurelion.glb> <out.png> Clip [Clip ...]
# Review sheet for aurelion_rig.py: imports the exported GLB the way any glTF reader would, and renders each named clip at
# 0, 1/3, 2/3 and its last frame (the strike, for the v4 wind-ups) from a front three-quarter view, one row per clip,
# into a single contact sheet (Workbench, textured, plain backdrop).
import bpy, sys, os, math, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, out, clips = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2:]
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
for mat in bpy.data.materials:
    b = mat.node_tree and next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b and b.inputs['Base Color'].links: mat.node_tree.nodes.active = b.inputs['Base Color'].links[0].from_node
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.6
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if arm.animation_data:
    for t in arm.animation_data.nla_tracks: t.mute = True
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.45
sh.background_type = 'VIEWPORT'; sh.background_color = (0.62, 0.64, 0.68)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
m = bpy.data.materials.new('Floor'); m.diffuse_color = (0.42, 0.43, 0.46, 1); bpy.context.object.data.materials.append(m)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
# glTF +Z forward becomes Blender -Y: he faces -Y. Front three-quarter from his right, slightly above.
cam.location = Vector((-5.2, -7.6, 3.6)); cam.data.lens = 40
d = Vector((0, 0, 2.1)) - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
W, H = 300, 360
sc.render.resolution_x, sc.render.resolution_y = W, H; sc.render.resolution_percentage = 100
sc.render.image_settings.file_format = 'PNG'
tmp = os.path.join(os.path.dirname(out), '_sheet_tmp.png')
sheet = np.zeros((H * len(clips), W * 4, 4), np.float32)
for r, name in enumerate(clips):
    act = bpy.data.actions.get(name)
    if act is None: print('MISSING', name); continue
    arm.animation_data.action = act
    f0, f1 = act.frame_range
    for c, u in enumerate((0, 1 / 3, 2 / 3, 1)):
        sc.frame_set(int(round(f0 + (f1 - f0) * u)))
        sc.render.filepath = tmp; bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(tmp, check_existing=False)
        px = np.array(img.pixels[:], np.float32).reshape(H, W, 4)
        y0 = H * (len(clips) - 1 - r)   # image rows start at the bottom
        sheet[y0:y0 + H, c * W:(c + 1) * W] = px
        bpy.data.images.remove(img)
    print('ROW', name)
im = bpy.data.images.new('sheet', W * 4, H * len(clips), alpha=True)
im.pixels = sheet.ravel(); im.filepath_raw = out; im.file_format = 'PNG'; im.save()
os.remove(tmp)
print('SHEET', out)
