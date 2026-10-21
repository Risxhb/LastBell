# blender -b rig.blend -P poses.py -- <out.png> <yaw> Action:frame Action:frame ...
import bpy, sys, math, os, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
out, yaw, shots = args[0], float(args[1]), args[2:]
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 420, 600
w = sc.world or bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get('Background'); bg.inputs[0].default_value = (0.5, 0.5, 0.52, 1)
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.data.energy = 2.5; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.9
a = math.radians(yaw); p = math.radians(12); d = 6; c = Vector((0, 0, 1.1))
cam.location = c + Vector((d * math.sin(a) * math.cos(p), -d * math.cos(a) * math.cos(p), d * math.sin(p)))
cam.rotation_euler = (math.radians(90 - 12), 0, a)
arm = [o for o in sc.objects if o.type == 'ARMATURE'][0]
tmp = []
for i, s in enumerate(shots):
    an, fr = s.split(':'); act = bpy.data.actions[an]
    arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]
    sc.frame_set(int(fr)); f = out + f'.{i}.png'; sc.render.filepath = f; bpy.ops.render.render(write_still=True); tmp.append(f)
imgs = [bpy.data.images.load(f) for f in tmp]; W, H = imgs[0].size
strip = np.zeros((H, W * len(imgs), 4), np.float32)
for i, im in enumerate(imgs): strip[:, i * W:(i + 1) * W] = np.array(im.pixels[:], np.float32).reshape(H, W, 4)
o = bpy.data.images.new('strip', W * len(imgs), H); o.pixels[:] = strip.ravel(); o.filepath_raw = out; o.file_format = 'PNG'; o.save()
for f in tmp: os.remove(f)
