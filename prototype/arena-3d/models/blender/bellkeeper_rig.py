# blender -b bellkeeper_prep.blend -P bellkeeper_rig.py -- <work dir> <out.glb>
# Rigs the prepared Bellkeeper (5 m, from bellkeeper_prep.py), keys a ritual-machine idle and exports a GLB with JPEG
# textures (glTFast can't be relied on for WebP). Look only: no attack clips yet.
# Axes: -Y forward, +X = the Bellkeeper's left, Z up. Rotations are degrees in rest-armature axes (see set_pose):
#   X: + tips the top of a bone forward (toward -Y), Y: + rolls it toward its left, Z: + turns it toward its left.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Bellkeeper']

# ---------------- armature (joint positions measured from slices and orthographic renders of the prepped mesh) ----------------
PIVOT = (0, -0.43, 3.62)      # top of the pendulum rod inside the chest cavity; the bob hangs at z 2.97
HALO = (0, 0.30, 4.26)        # centre of the broken halo ring, which leans back about 22 degrees
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.6, 0), None, False),
    'hips': ((0, 0, 2.2), (0, 0, 2.5), 'root', True),
    'spine': ((0, 0, 2.5), (0, 0, 2.95), 'hips', True),
    'chest': ((0, 0, 2.95), (0, 0, 3.6), 'spine', True),
    'neck': ((0, 0.02, 3.6), (0, 0.0, 3.95), 'chest', True),
    'head': ((0, 0.0, 3.95), (0, -0.05, 4.7), 'neck', True),
    'halo': (HALO, (HALO[0], HALO[1] - 0.2, HALO[2] + 0.5), 'chest', True),
    'pendulum': (PIVOT, (PIVOT[0], PIVOT[1], 2.97), 'chest', True),
    'upperarm.R': ((-0.78, 0, 3.4), (-1.05, 0, 2.85), 'chest', True),
    'forearm.R': ((-1.05, 0, 2.85), (-1.3, -0.08, 2.05), 'upperarm.R', True),
    'hand.R': ((-1.3, -0.08, 2.05), (-1.32, -0.22, 1.5), 'forearm.R', True),
    'upperarm.L': ((0.78, 0, 3.4), (1.05, 0, 2.85), 'chest', True),
    'forearm.L': ((1.05, 0, 2.85), (1.3, -0.08, 2.05), 'upperarm.L', True),
    'hand.L': ((1.3, -0.08, 2.05), (1.32, -0.22, 1.5), 'forearm.L', True),
    'thigh.R': ((-0.4, 0, 2.2), (-0.55, -0.05, 1.2), 'hips', True),
    'shin.R': ((-0.55, -0.05, 1.2), (-0.75, 0, 0.4), 'thigh.R', True),
    'foot.R': ((-0.75, 0, 0.4), (-0.85, -0.45, 0.05), 'shin.R', True),
    'thigh.L': ((0.4, 0, 2.2), (0.55, -0.05, 1.2), 'hips', True),
    'shin.L': ((0.55, -0.05, 1.2), (0.75, 0, 0.4), 'thigh.L', True),
    'foot.L': ((0.75, 0, 0.4), (0.85, -0.45, 0.05), 'shin.L', True),
}
ad = bpy.data.armatures.new('BellkeeperRig'); arm = bpy.data.objects.new('BellkeeperRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: heat-weight a watertight proxy, transfer, then make the loose parts rigid ----------------
RIGID = ('halo', 'pendulum')   # placed by hand below, kept out of the heat solve so they don't pull the hood/chest
bpy.ops.object.select_all(action='DESELECT')
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'proxy'; sc.collection.objects.link(proxy)
rm = proxy.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.03
bpy.context.view_layer.objects.active = proxy; bpy.ops.object.modifier_apply(modifier='rm')
for c in RIGID: ad.bones[c].use_deform = False
proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
for c in RIGID: ad.bones[c].use_deform = True
print('PROXY_GROUPS', len(proxy.vertex_groups))
bpy.ops.object.select_all(action='DESELECT')
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
dt = body.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = proxy; dt.use_vert_data = True
dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
bpy.context.view_layer.objects.active = body; body.select_set(True)
bpy.ops.object.modifier_apply(modifier='dt')
bpy.data.objects.remove(proxy)

names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
me = body.data; nv = len(me.vertices); W = np.zeros((nv, len(names)), np.float32)
for v in me.vertices:
    for g in v.groups: W[v.index, g.group] = g.weight
co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
x, y, z = co[:, 0], co[:, 1], co[:, 2]
def row(**kw):
    r = np.zeros(len(names), np.float32)
    for k, v in kw.items(): r[gi[k.replace('_', '.')]] = v
    return r
# loose parts (connected components): the halo is four arcs, the bob, the front tabard and a back ornament
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(nv)]); _, part = np.unique(roots, return_inverse=True)
def part_box(p): c = co[part == p]; return c.min(0), c.max(0)
for p in range(part.max() + 1):
    lo, hi = part_box(p); m = part == p
    if m.sum() > nv * 0.5: continue
    if lo[2] > 3.4 and (hi[1] > 0.4 or lo[2] > 4.4): W[m] = row(halo=1); print('PART halo', m.sum())
    elif lo[1] < -0.2 and lo[2] > 2.7 and hi[2] < 3.3 and abs(lo[0]) < 0.3: W[m] = row(pendulum=1); print('PART bob', m.sum())
    elif hi[1] < 0 and lo[2] < 1.0: W[m] = row(hips=1); print('PART tabard', m.sum())
    elif lo[1] > 0.3: W[m] = row(chest=1); print('PART back', m.sum())
# the pendulum rod is part of the body shell: a thin column under the pivot
rod = (np.abs(x) < 0.05) & (np.abs(y - PIVOT[1]) < 0.07) & (z > 3.1) & (z < PIVOT[2] - 0.02)
W[rod] = row(pendulum=1); print('ROD', int(rod.sum()))
# the pendulum must not drag anything else
W[~rod & (W[:, gi['pendulum']] < 0.999), gi['pendulum']] = 0
W[W[:, gi['halo']] < 0.999, gi['halo']] = 0
# normalise, keep the 4 biggest influences
order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, 1)
W = np.where(keep, W, 0); s = W.sum(1, keepdims=True); W = np.where(s > 0, W / np.maximum(s, 1e-6), 0)
W[s[:, 0] == 0, gi['chest']] = 1
print('UNWEIGHTED_FIXED', int((s[:, 0] == 0).sum()))
for g in body.vertex_groups: g.remove(list(range(nv)))
for j, n in enumerate(names):
    vg = body.vertex_groups[n]; idx = np.nonzero(W[:, j] > 0)[0]
    for i in idx: vg.add([int(i)], float(W[i, j]), 'REPLACE')
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
for m in list(body.modifiers): body.modifiers.remove(m)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- animation helpers (same conventions as the Vanguard's rig.py) ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
def bake(name, frames, fn, loop):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + (1 if loop else 0)):
        set_pose(fn(f, f / frames))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + (1 if loop else 0))
    return act

# halo spin axis: the ring's normal (it leans back, so its normal points forward and down)
HALO_N = Vector((0, -1.5, -0.6)).normalized()
def idle(f, t):
    # a ritual machine at rest: one slow breath per loop, the pendulum ticks twice (side to side, which reads from the
    # gameplay camera in front), the halo drifts and turns a few degrees in its own plane. Feet stay planted.
    p = 2 * math.pi * t
    br = math.sin(p)                      # breath: chest rises and the shoulders lift on the in-breath
    sway = math.sin(p + 0.9)              # a slower-looking side sway, offset from the breath
    swing = math.sin(2 * p)               # pendulum: 2 ticks per 4 s loop
    return {
        'hips': {'rot': R(y=0.6 * sway)},
        'spine': {'rot': R(x=1.2 * br, y=0.8 * sway)},
        'chest': {'rot': R(x=-1.6 * br, y=0.6 * sway, z=1.0 * math.sin(p + 2.0))},
        'neck': {'rot': R(x=0.8 * br)},
        'head': {'rot': R(x=2.5 + 1.2 * math.sin(p + 0.6), z=-1.5 * sway)},
        'upperarm.R': {'rot': R(x=-1.5 * math.sin(p + 0.5), y=-1.2 * br)},
        'forearm.R': {'rot': R(x=-2.0 * math.sin(p + 1.0))},
        'hand.R': {'rot': R(x=-2.5 * math.sin(p + 1.4))},
        'upperarm.L': {'rot': R(x=-1.5 * math.sin(p + 0.7), y=1.2 * br)},
        'forearm.L': {'rot': R(x=-2.0 * math.sin(p + 1.2))},
        'hand.L': {'rot': R(x=-2.5 * math.sin(p + 1.6))},
        # legs counter the hips so the boots don't move
        'thigh.R': {'rot': R(y=-0.6 * sway)}, 'thigh.L': {'rot': R(y=-0.6 * sway)},
        'pendulum': {'rot': R(y=14 * swing)},
        'halo': {'rot': Vector(HALO_N).to_track_quat('Z', 'Y').to_matrix() @ R(z=3.5 * math.sin(p))
                 @ Vector(HALO_N).to_track_quat('Z', 'Y').to_matrix().inverted() @ R(x=1.5 * math.sin(p + 1.2)),
                 'loc': (0, 0, 0.05 * math.sin(p + 0.3))},
    }
acts = [bake('Idle', 120, idle, True)]
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'bellkeeper_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
