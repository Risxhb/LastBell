# blender -b -P turntable.py -- <out dir>
# Opens each asset .blend and renders a four-view turntable strip (45/135/225/315 degrees) per hero object,
# stitched into one PNG per object with Blender's bundled numpy. Uses lb.preview lighting.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector
here = os.path.dirname(os.path.abspath(__file__)); root = os.path.join(here, '..')
sys.path.append(os.path.join(root, 'common')); import lb
out = sys.argv[sys.argv.index('--') + 1:][0]; os.makedirs(out, exist_ok=True)
SKIP = {'Pinnacle_25', 'Pinnacle_35', 'Arcade_2', 'Arcade_3', 'Rock_1', 'Rock_2', 'Rock_3', 'Rock_4', 'Rock_5', 'SpireChunk_1', 'SpireChunk_2',
        'SpireChunk_3', 'Chain_Link_Low', 'Bell_Shard2', 'Astro_Fragment', 'Ring_Low'}
W, H = 640, 520
for blend in ('pinnacle', 'rim', 'station', 'bell', 'rings', 'entrances', 'void'):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(root, blend, blend + '.blend'))
    sc = bpy.context.scene
    if sc.world: bpy.data.worlds.remove(sc.world)   # lb.preview rebuilds world + lights when missing
    for o in [o for o in sc.objects if o.type == 'LIGHT']: bpy.data.objects.remove(o)
    meshes = [o for o in sc.objects if o.type == 'MESH' and o.name not in SKIP]
    for o in meshes:
        for m in sc.objects:
            if m.type == 'MESH': m.hide_render = m is not o
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]; lo = Vector([min(v[i] for v in bb) for i in range(3)]); hi = Vector([max(v[i] for v in bb) for i in range(3)])
        c = (lo + hi) / 2; rad = max((hi - lo).length / 2, 0.3); dist = rad * 2.6
        tiles = []
        for k in range(4):
            a = math.radians(45 + 90 * k); cam = c + Vector((math.cos(a) * dist, math.sin(a) * dist, rad * 0.9))
            p = os.path.join(out, '_tmp_%d.png' % k); lb.preview(p, tuple(cam), tuple(c), res=(W, H), lens=40)
            img = bpy.data.images.load(p); px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4); tiles.append(px); bpy.data.images.remove(img)
        strip = np.concatenate(tiles, axis=1)
        im = bpy.data.images.new(o.name, W * 4, H, alpha=True); im.pixels = strip.ravel().tolist()
        im.filepath_raw = os.path.join(out, o.name + '.png'); im.file_format = 'PNG'; im.save(); print('turntable', o.name)
for k in range(4):
    try: os.remove(os.path.join(out, '_tmp_%d.png' % k))
    except OSError: pass
print('done')
