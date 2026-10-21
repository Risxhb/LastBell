# blender -b <rigged .blend> -P contact_sheet.py -- <out.png> <Action:t,t,t> [<Action:t,...> ...]
# Renders a pose contact sheet (one row per action, one column per normalized time) from a three-quarter camera
# with Workbench, for checking authored clips without Unity. Labels are not drawn; rows follow the argument order.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
out, specs = args[0], args[1:]
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
sc.render.resolution_x = sc.render.resolution_y = 320; sc.render.film_transparent = False
sc.world = sc.world or bpy.data.worlds.new('W')
arm = next(o for o in sc.objects if o.type == 'ARMATURE')
cam_data = bpy.data.cameras.new('SheetCam'); cam = bpy.data.objects.new('SheetCam', cam_data); sc.collection.objects.link(cam)
cam_data.type = 'ORTHO'; cam_data.ortho_scale = 3.4
# three-quarter front view (character faces -Y), slightly from above like the gameplay camera
cam.location = Vector((2.6, -4.2, 2.6)); d = Vector((0, 0, 0.95)) - cam.location
cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); sc.camera = cam
rows = []
for spec in specs:
    name, ts = spec.split(':'); ts = [float(t) for t in ts.split(',')]
    act = bpy.data.actions[name]; arm.animation_data.action = act
    f0, f1 = act.frame_range
    row = []
    for t in ts:
        sc.frame_set(int(round(f0 + (f1 - f0) * t)))
        path = os.path.join(os.path.dirname(out), '_cell.png'); sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path); px = np.array(img.pixels[:]).reshape(320, 320, 4); bpy.data.images.remove(img)
        row.append(px)
    rows.append(row)
cols = max(len(r) for r in rows)
sheet = np.ones((320 * len(rows), 320 * cols, 4))
for i, r in enumerate(rows):
    for j, px in enumerate(r):
        y = (len(rows) - 1 - i) * 320
        sheet[y:y + 320, j * 320:(j + 1) * 320] = px
img = bpy.data.images.new('sheet', 320 * cols, 320 * len(rows)); img.pixels = sheet.ravel().tolist()
img.filepath_raw = out; img.file_format = 'PNG'; img.save()
print('SHEET', out, len(rows), 'x', cols)
