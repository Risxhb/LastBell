# blender -b <rig .blend> -P pose_render.py -- <out.png> <Action> <t,t,...> [view: front|side|threeq]
# Renders one action at several normalised times side by side (Eevee, larger than contact_sheet.py) for close checks.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
out, action, ts = args[0], args[1], [float(t) for t in args[2].split(',')]
view = args[3] if len(args) > 3 else 'threeq'
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
sc.render.resolution_x, sc.render.resolution_y = 520, 640
arm = next(o for o in sc.objects if o.type == 'ARMATURE')
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5.6
pos = {'front': (0, -9, 2.2), 'side': (9, 0, 2.2), 'threeq': (6, -7, 4.2)}[view]
cam.location = pos; cam.rotation_euler = (Vector((0, 0, 2.0)) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
act = bpy.data.actions[action]; arm.animation_data.action = act; f0, f1 = act.frame_range
cells = []
for t in ts:
    sc.frame_set(int(round(f0 + (f1 - f0) * t)))
    p = os.path.join(os.path.dirname(out), '_pose.png'); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(p); a = np.array(im.pixels[:]).reshape(640, 520, 4); cells.append(a); bpy.data.images.remove(im)
sheet = np.concatenate(cells, axis=1)
img = bpy.data.images.new('sheet', sheet.shape[1], sheet.shape[0]); img.pixels[:] = sheet.ravel()
img.filepath_raw = out; img.file_format = 'PNG'; img.save()
