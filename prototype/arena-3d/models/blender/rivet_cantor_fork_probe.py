# blender -b <rivet_cantor_rig.blend> -P rivet_cantor_fork_probe.py -- <out dir>
# Renders the left hand and the fork (rest pose) with the fork mask's vertices (distance bands along wrist -> fork tip)
# drawn as coloured points, to choose where the hand ends and the fork begins before cutting the fork out.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector
out = os.path.abspath(sys.argv[sys.argv.index('--') + 1:][0]); os.makedirs(out, exist_ok=True)
WR_L, FORK = np.array((1.30, -0.61, 1.23)), np.array((1.62, -1.40, 0.48))
sc = bpy.context.scene
body = next(o for o in sc.objects if o.type == 'MESH' and o.parent and o.parent.type == 'ARMATURE')
arm = body.parent
arm.data.pose_position = 'REST'
co = np.array([body.matrix_world @ v.co for v in body.data.vertices])
d = FORK - WR_L; u = (co - WR_L) @ d / (d @ d)
dist = np.linalg.norm(co - (WR_L + np.clip(u, 0, 1)[:, None] * d), axis=1)
print('BODY', body.name, len(co), 'bbox', co.min(0).round(2), co.max(0).round(2))
for lo in np.arange(-0.1, 1.2, 0.1):
    m = (u >= lo) & (u < lo + 0.1) & (dist < 0.3)
    if m.any(): print(f'BAND u {lo:+.1f}: {m.sum():5d} verts, radius max {dist[m].max():.2f}, x {co[m,0].min():.2f}..{co[m,0].max():.2f}')
# vertex colours by band: u<0.15 red (hand), 0.15-0.3 yellow, >0.3 green (fork), only within 0.3 of the axis
me = body.data
ca = me.color_attributes.new('probe', 'BYTE_COLOR', 'POINT')
for i in range(len(co)):
    if dist[i] < 0.3 and u[i] > -0.1:
        c = (1, 0, 0, 1) if u[i] < 0.15 else (1, 1, 0, 1) if u[i] < 0.3 else (0, 1, 0, 1)
    else: c = (0.5, 0.5, 0.5, 1)
    ca.data[i].color = c
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'VERTEX'; sc.display.shading.light = 'STUDIO'
me.color_attributes.active_color = ca
sc.render.resolution_x, sc.render.resolution_y = 900, 900
cd = bpy.data.cameras.new('c'); cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam
for name, pos in (('front', (1.6, -4.2, 1.2)), ('side', (4.2, -1.0, 1.0))):
    cam.location = pos; cam.rotation_euler = (Vector((1.45, -1.0, 0.9)) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    cd.lens = 50
    sc.render.filepath = os.path.join(out, f'fork_probe_{name}.png'); bpy.ops.render.render(write_still=True)
