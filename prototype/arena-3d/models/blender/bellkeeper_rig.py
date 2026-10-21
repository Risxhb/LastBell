# blender -b bellkeeper_prep.blend -P bellkeeper_rig.py -- <work dir> <out.glb>
# Rigs the prepared Bellkeeper (5 m, from bellkeeper_prep.py), keys a ritual-machine idle and exports a GLB with JPEG
# textures (glTFast can't be relied on for WebP). Look only: no attack clips yet.
# Axes: -Y forward, +X = the Bellkeeper's left, Z up. Rotations are degrees in rest-armature axes (see set_pose):
#   X: + tips the top of a bone forward (toward -Y), so + swings a hanging arm back; Y: + tilts the top toward +X
#   (the boss's left), so + lifts the right arm out and - lifts the left arm out; Z: + turns toward its left.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
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

# ---------------- attacks (docs: reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md §3) ----------------
# Clips are in place: Unity turns the boss during the tracking window and moves it down the charge lane.
# The resolve moment of each clip sits exactly at the design's warning time, so the game can start the clip on the
# telegraph tick. Channels are keyed in seconds with key(); the legs are solved by plant_feet() so the boots stay put.
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value) pairs; values may be tuples
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            return tuple(a + (b - a) * u for a, b in zip(v0, v1)) if isinstance(v0, tuple) else v0 + (v1 - v0) * u
    return pts[-1][1]
def snap(t, t0, rise=0.06, fall=0.25):  # a quick 0 -> 1 -> 0 pulse at t0 (chime taps, impacts)
    if t < t0 - rise or t > t0 + fall: return 0.0
    return smooth((t - (t0 - rise)) / rise) if t < t0 else 1 - smooth((t - t0) / fall)
def ring(t, t0, amp, freq=1.6, damp=2.2):  # damped swing started at t0: pendulum and halo follow-through
    return 0.0 if t < t0 else amp * math.exp(-damp * (t - t0)) * math.sin(2 * math.pi * freq * (t - t0))
def halo_rot(spin, tilt=0.0):
    Q = HALO_N.to_track_quat('Z', 'Y').to_matrix()
    return Q @ R(z=spin) @ Q.inverted() @ R(x=tilt)

LEGS = {s: (Vector(B['thigh.' + s][0]), Vector(B['shin.' + s][0]), Vector(B['shin.' + s][1])) for s in 'LR'}
def plant_feet():
    # two-bone IK in armature space: the ankle stays at its rest point, the knee bends in the rest leg's plane
    for s, (h0, k0, a0) in LEGS.items():
        bpy.context.view_layer.update()
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        H = th.head.copy(); A = a0.copy()
        L1, L2 = (k0 - h0).length, (a0 - k0).length
        d = min((A - H).length, L1 + L2 - 1e-4); u = (A - H).normalized()
        pole = (k0 - h0) - (k0 - h0).project(a0 - h0)          # rest knee offset from the hip-ankle line
        v = (pole - pole.project(u)).normalized()
        ca = max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)))
        K = H + L1 * (ca * u + math.sqrt(1 - ca * ca) * v)
        for pb, a, b in ((th, H, K), (sh, K, A)):
            r0 = pb.bone.matrix_local.to_3x3(); rest = (pb.bone.tail_local - pb.bone.head_local).normalized()
            pb.matrix = Matrix.Translation(a) @ (rest.rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ ft.bone.matrix_local.to_3x3().to_4x4()

def pose_of(ch):
    # ch: bone -> (x, y, z) degrees, plus 'hips_loc', 'halo_spin', 'halo_tilt', 'halo_loc' extras
    p = {}
    for n, v in ch.items():
        if n in ('hips_loc', 'halo_spin', 'halo_tilt', 'halo_loc'): continue
        p[n] = {'rot': R(*v)}
    p.setdefault('hips', {})['loc'] = ch.get('hips_loc', (0, 0, 0))
    p['halo'] = {'rot': halo_rot(ch.get('halo_spin', 0), ch.get('halo_tilt', 0)), 'loc': ch.get('halo_loc', (0, 0, 0))}
    return p
def add(*vs): return tuple(sum(c) for c in zip(*vs))

def breath(t):  # a little life under every attack so holds never freeze
    b = math.sin(2 * math.pi * t / 4.0)
    return {'chest': (-1.2 * b, 0, 0), 'head': (2.5, 0, 0)}

def bell_sweep(t):
    # 0.95 s windup: the right (resonator) arm draws back and out while the torso coils right; at 0.95 it whips
    # across the front to the left; ~0.8 s recovery hanging in the follow-through.
    T = 0.95
    w = [(0, 0), (0.55, 0.8), (T - 0.05, 1.0), (T + 0.08, -1.0), (T + 0.35, -1.05), (2.0, 0)]
    c = key(t, w)                                   # +1 coiled right .. -1 unwound left
    crouch = key(t, [(0, 0), (T - 0.1, -0.14), (T + 0.08, -0.22), (T + 0.5, -0.18), (2.0, 0)])
    ch = breath(t)
    ch.update({
        'hips': (0, 0, -9 * c), 'spine': (key(t, [(0, 0), (T, -3), (T + .1, 8), (2, 0)]), 0, -12 * c),
        'chest': add(ch['chest'], (key(t, [(0, 0), (T, -6), (T + .1, 12), (T + .6, 8), (2, 0)]), 0, -24 * c)),
        'neck': (0, 0, 6 * c), 'head': (key(t, [(0, 2.5), (T, -4), (T + .1, 8), (2, 2.5)]), 0, 10 * c),
        'upperarm.R': (key(t, [(0, 0), (T - .05, 38), (T + .08, -78), (T + .5, -60), (2, 0)]),
                       key(t, [(0, 0), (T - .05, 72), (T + .08, 58), (T + .5, 40), (2, 0)]), 0),
        'forearm.R': (key(t, [(0, 0), (T - .05, -55), (T + .08, -8), (T + .5, -20), (2, 0)]), 0, 0),
        'hand.R': (key(t, [(0, 0), (T - .05, 25), (T + .08, -20), (2, 0)]), 0, 0),
        'upperarm.L': (key(t, [(0, 0), (T - .05, -28), (T + .08, 25), (T + .5, 15), (2, 0)]),
                       key(t, [(0, 0), (T - .05, 18), (T + .08, -25), (2, 0)]), 0),
        'forearm.L': (key(t, [(0, 0), (T - .05, -35), (T + .1, -15), (2, 0)]), 0, 0),
        'pendulum': (0, key(t, [(0, 0), (T - .05, 16), (T + .02, 0)]) + ring(t, T, -28, 1.4, 2.0), 0),
        'hips_loc': (0, 0, crouch),
        'halo_spin': key(t, [(0, 0), (T - .05, -8), (T + .1, 10), (2, 0)]) + ring(t, T + .1, 5, 1.0, 2.5),
    })
    return ch

def hollow_toll(t):
    # 1.10 s windup: the chest bell compresses (hunch, arms fold in, deep crouch); at 1.10 it rings out: chest throws
    # back, arms fling wide and down, the pendulum swings hard; ~0.9 s recovery.
    T = 1.10
    g = key(t, [(0, 0), (T - 0.12, 1.0), (T, 1.0), (T + 0.07, -1.0), (T + 0.5, -0.8), (2.0, 0)])   # +gather, -release
    ch = breath(t)
    ch.update({
        'hips_loc': (0, key(t, [(0, 0), (T - .1, 0.08), (T + .07, -0.04), (2, 0)]),
                     key(t, [(0, 0), (T - .12, -0.42), (T, -0.45), (T + .07, -0.05), (T + .25, -0.2), (2, 0)])),
        'hips': (6 * max(g, 0), 0, 0),
        'spine': (12 * g, 0, 0), 'chest': add(ch['chest'], (16 * g, 0, 0)), 'neck': (-4 * g, 0, 0),
        'head': (2.5 + 10 * max(g, 0) - 6 * max(-g, 0), 0, 0),
        'upperarm.R': (-45 * max(g, 0) - 15 * max(-g, 0), -22 * max(g, 0) + 55 * max(-g, 0), 0),
        'upperarm.L': (-45 * max(g, 0) - 15 * max(-g, 0), 22 * max(g, 0) - 55 * max(-g, 0), 0),
        'forearm.R': (-70 * max(g, 0) - 10 * max(-g, 0), 0, 0), 'forearm.L': (-70 * max(g, 0) - 10 * max(-g, 0), 0, 0),
        'hand.R': (20 * max(g, 0) - 15 * max(-g, 0), 0, 0), 'hand.L': (20 * max(g, 0) - 15 * max(-g, 0), 0, 0),
        'pendulum': (key(t, [(0, 0), (T - .1, 12), (T, 14)]) * (t < T) + ring(t, T, -34, 1.5, 1.8), 0, 0),
        'halo_loc': (0, 0, key(t, [(0, 0), (T - .1, -0.12), (T + .07, 0.22), (T + .5, 0.1), (2, 0)])),
        'halo_spin': ring(t, T, 14, 1.2, 2.0), 'halo_tilt': key(t, [(0, 0), (T - .1, 6), (T + .1, -8), (2, 0)]),
    })
    return ch

def pendulum_charge(t):
    # 0.80 s windup: the boss rears back and swings its weight like a pendulum (arms back, bob swung back);
    # 0.80-1.10 it lunges down the lane (Unity moves it), body pitched forward; lands with a stomp; recovery to 1.8 s.
    T, L = 0.80, 1.10
    lean = key(t, [(0, 0), (T - .05, -1.0), (T + .06, 1.0), (L, 1.0), (L + .1, -0.25), (L + .45, 0.15), (1.8, 0)])
    ch = breath(t)
    ch.update({
        'hips': (8 * lean, 0, 0),
        'hips_loc': (0, key(t, [(0, 0), (T - .05, 0.22), (T + .06, -0.12), (L, -0.12), (L + .1, 0), (1.8, 0)]),
                     key(t, [(0, 0), (T - .05, -0.15), (T + .06, -0.08), (L, -0.1), (L + .08, -0.32), (L + .4, -0.15), (1.8, 0)])),
        'spine': (10 * lean, 0, 0), 'chest': add(ch['chest'], (14 * lean, 0, 0)), 'neck': (-6 * lean, 0, 0),
        'head': (2.5 - 8 * lean, 0, 0),
        'upperarm.R': (key(t, [(0, 0), (T - .05, -30), (T + .06, 50), (L, 55), (L + .1, -20), (1.8, 0)]), 12, 0),
        'upperarm.L': (key(t, [(0, 0), (T - .05, -30), (T + .06, 50), (L, 55), (L + .1, -20), (1.8, 0)]), -12, 0),
        'forearm.R': (key(t, [(0, 0), (T - .05, -40), (T + .06, -10), (L + .1, -35), (1.8, 0)]), 0, 0),
        'forearm.L': (key(t, [(0, 0), (T - .05, -40), (T + .06, -10), (L + .1, -35), (1.8, 0)]), 0, 0),
        'pendulum': (key(t, [(0, 0), (T - .05, -22), (T + .06, 30), (L, 32)]) * (t < L) + ring(t, L, 36, 1.5, 2.0), 0, 0),
        'halo_loc': (0, key(t, [(0, 0), (T - .05, -0.1), (T + .06, 0.25), (L, 0.25), (L + .3, 0), (1.8, 0)]), 0),
        'halo_tilt': key(t, [(0, 0), (T + .06, 10), (L + .2, -6), (1.8, 0)]),
    })
    return ch

def shattered_chime(t):
    # 1.20 s windup: both arms rise and the halo spins up as the boss calls the chimes; the markers resolve at 1.2,
    # 1.6 and 2.0 s (solo); each resolve is a conducting strike (right, left, both); recovery to 2.6 s.
    T = 1.20; taps = (T, T + 0.4, T + 0.8)
    up = key(t, [(0, 0), (T - .15, 1.0), (taps[2] + .1, 1.0), (2.6, 0)])
    r1, l1, b1 = snap(t, taps[0]), snap(t, taps[1]), snap(t, taps[2])
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, -0.08 * up - 0.1 * (r1 + l1 + b1)),
        'spine': (-4 * up, 0, 0), 'chest': add(ch['chest'], (-8 * up + 6 * (r1 + l1 + b1), 0, 4 * r1 - 4 * l1)),
        'head': (2.5 - 14 * up + 10 * (r1 + l1 + b1), 0, 0),
        'upperarm.R': (-100 * up + 45 * (r1 + b1), 32 * up, 0), 'upperarm.L': (-100 * up + 45 * (l1 + b1), -32 * up, 0),
        'forearm.R': (-30 * up + 25 * (r1 + b1), 0, 0), 'forearm.L': (-30 * up + 25 * (l1 + b1), 0, 0),
        'hand.R': (-20 * up + 30 * (r1 + b1), 0, 0), 'hand.L': (-20 * up + 30 * (l1 + b1), 0, 0),
        'pendulum': (0, 10 * math.sin(2 * math.pi * t / 0.8) * up + ring(t, taps[2], 15, 1.5, 2.5), 0),
        'halo_loc': (0, 0, 0.25 * up),
        'halo_spin': key(t, [(0, 0), (T, 40), (2.6, 0)]) + 12 * (r1 + l1 + b1),
    })
    return ch

def vulnerable(t):
    # loop (2 s): the bell falters. Hunched and sagging, halo dropped and tilted, pendulum barely ticking, a shudder.
    p = 2 * math.pi * t / 2.0; sh = 0.6 * math.sin(7 * p) * (0.5 + 0.5 * math.sin(p))
    return {
        'hips_loc': (0, 0.05, -0.3 + 0.03 * math.sin(p)), 'hips': (6, 0, 0),
        'spine': (10 + sh, 0, 0), 'chest': (16 + 2 * math.sin(p), 0, sh), 'neck': (8, 0, 0), 'head': (24, 0, 3 * math.sin(p)),
        'upperarm.R': (-12, -6, 0), 'upperarm.L': (-12, 6, 0), 'forearm.R': (-25, 0, 0), 'forearm.L': (-25, 0, 0),
        'hand.R': (15, 0, 0), 'hand.L': (15, 0, 0),
        'pendulum': (0, 4 * math.sin(p), 0),
        'halo_loc': (0, 0.05, -0.32 + 0.03 * math.sin(p + 1)), 'halo_tilt': 14, 'halo_spin': -10 + 2 * math.sin(p),
    }

def bake_clip(name, seconds, fn, loop=False):
    frames = round(seconds * 30)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        set_pose(pose_of(fn(f / 30))); plant_feet()
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

acts = [bake('Idle', 120, idle, True),
        bake_clip('BellSweep', 2.0, bell_sweep), bake_clip('HollowToll', 2.0, hollow_toll),
        bake_clip('PendulumCharge', 1.8, pendulum_charge), bake_clip('ShatteredChime', 2.6, shattered_chime),
        bake_clip('Vulnerable', 2.0, vulnerable, loop=True)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'bellkeeper_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
