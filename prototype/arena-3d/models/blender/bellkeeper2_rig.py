# blender -b bellkeeper2_prep.blend -P bellkeeper2_rig.py -- <work dir> <out.glb>
# Bellkeeper 2.0: rigs the prepared mesh (5 m, from bellkeeper2_prep.py; sheet 04-bellkeeper-v1.png), keys the
# encounter's clips and exports a GLB with JPEG textures (glTFast can't be relied on for WebP). Animation conventions
# and most clips come from bellkeeper_rig.py (the first Bellkeeper); the skeleton, the stance and the loose parts are
# this model's: three independent clock rings, a chest clapper, two chest shell halves that open in the punish window.
# Axes: -Y forward, +X = the Bellkeeper's left, Z up. Rotations are degrees in rest-armature axes (see set_pose):
#   X: + tips the top of a bone forward (toward -Y), so + swings a hanging arm back; Y: + tilts the top toward +X
#   (the boss's left), so + lifts the right arm out and - lifts the left arm out; Z: + turns toward its left.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Bellkeeper2']; me = body.data; nv = len(me.vertices)
co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)

# ---------------- loose parts (connected components) ----------------
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    r = a
    while par[r] != r: r = par[r]
    while par[a] != r: par[a], a = r, par[a]
    return r
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(nv)]); _, part, cnt = np.unique(roots, return_inverse=True, return_counts=True)
main = int(np.argmax(cnt))
def box(m): c = co[m]; return c.min(0), c.max(0)

# ---------------- stance (decision D8): keep the boots near the 1.6 m footprint ----------------
# Meshy put the boot edges 1.87 m out. Narrow each boot about its own centre and pull it in slightly, fading the change
# out up the thigh so the hips are untouched. The arms, the pendulum head and the hip chains are left alone.
x, y, z = co[:, 0].copy(), co[:, 1].copy(), co[:, 2].copy()
BOOT_C = 1.07                       # boot centres at x = +-1.07 (front render)
arm_l = np.zeros(nv, bool)
for p in range(len(cnt)):
    lo, hi = box(part == p)
    if p != main and lo[0] > 0.5 and hi[0] > 1.9: arm_l |= part == p          # the left arm and fingers are one part
pend_head = (x < -1.75) & (z > 0.5)
chains = (np.abs(x) < 0.42) & (z > 0.7)
leg = (z < 1.9) & (np.abs(x) > 0.15) & ~arm_l & ~pend_head & ~chains
w = np.clip((1.9 - z) / 0.9, 0, 1)[leg]                                       # 1 below the knee, 0 at the hip
sg = np.sign(x[leg]); k = 1 - 0.18 * w
x[leg] = sg * (BOOT_C - 0.12 * w) + (x[leg] - sg * BOOT_C) * k
y[leg] = y[leg] * (1 - 0.12 * w)
co = np.stack([x, y, z], 1); me.vertices.foreach_set('co', co.ravel()); me.update()
sole = co[(co[:, 2] < 0.4) & leg]
print('STANCE boots x %.2f..%.2f, max radius %.2f' % (sole[:, 0].min(), sole[:, 0].max(), np.hypot(sole[:, 0], sole[:, 1]).max()))

# ---------------- rigid pieces: rings, clapper, chest shell halves ----------------
# Each is cut free of the body along its border (edges split), so opening the shell or spinning a ring never
# stretches faces. Labels: 0 body, 1 ring.C, 2 ring.L, 3 ring.R, 4 pendulum, 5 shell.L, 6 shell.R.
PIVOT = (0, -0.55, 3.0)       # clapper rod top inside the chest crack; the bob hangs at z ~2.25
RIGID_NAMES = (None, 'ring.C', 'ring.L', 'ring.R', 'pendulum', 'shell.L', 'shell.R')
ring = np.zeros(nv, bool)
for p in range(len(cnt)):
    lo, hi = box(part == p)
    if p != main and lo[1] > 0.35 and lo[2] > 2.7: ring |= part == p                # the separate arcs
ring |= (y > 0.82) & (z > 2.7) & ~((np.abs(x) < 0.2) & (z < 3.05))              # behind the back plate (not the spine chain)
ring |= (z > 4.55) & (y > 0.15)                                                 # above the hood
bob = (np.abs(x) < 0.2) & (z > 2.08) & (z < 2.48) & (y < -0.3)
rod = (np.abs(x) < 0.06) & (np.abs(y - PIVOT[1]) < 0.12) & (z >= 2.48) & (z < PIVOT[2])
shell = (y < -0.55) & (z > 2.45) & (z < 3.75) & (np.abs(x) > 0.1) & (np.abs(x) < 0.95) & ~(bob | rod)
lab = np.zeros(nv, np.int8)
lab[ring & (x >= -0.65) & (x <= 0.95)] = 1; lab[ring & (x > 0.95)] = 2; lab[ring & (x < -0.65)] = 3
lab[bob | rod] = 4; lab[shell & (x > 0)] = 5; lab[shell & (x < 0)] = 6
import bmesh
bm = bmesh.new(); bm.from_mesh(me)
old = bm.verts.layers.int.new('old')
for v in bm.verts: v[old] = v.index
flab = {}
for f in bm.faces:
    ls = [int(lab[v.index]) for v in f.verts]; flab[f.index] = max(set(ls), key=ls.count)
cut = [e for e in bm.edges if len({flab[f.index] for f in e.link_faces}) > 1]
bmesh.ops.split_edges(bm, edges=cut)
bm.verts.index_update(); bm.faces.index_update()
# a face keeps its label through the split; give each vertex the label of its faces (all agree after the split)
vlab = np.zeros(len(bm.verts), np.int8)
for f in bm.faces:
    for v in f.verts: vlab[v.index] = flab[f.index]
oidx = np.array([v[old] for v in bm.verts])
bm.to_mesh(me); bm.free(); me.update()
nv = len(me.vertices); co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
arm_l, pend_head, chains = arm_l[oidx], pend_head[oidx], chains[oidx]
print('SPLIT', len(cut), 'edges; pieces', {RIGID_NAMES[i]: int((vlab == i).sum()) for i in range(1, 7)})

# ---------------- armature (joint positions measured from metric orthographic renders of the prepped mesh) ----------------
HALO = (0, 0.95, 3.9)         # centre of the back (main) ring, which leans back about 24 degrees
RING_L = (1.1, 0.85, 3.65); RING_R = (-1.05, 0.8, 3.75)
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.6, 0), None, False),
    'hips': ((0, 0, 2.0), (0, 0, 2.35), 'root', True),
    'spine': ((0, 0, 2.35), (0, 0, 2.9), 'hips', True),
    'chest': ((0, 0, 2.9), (0, 0, 3.65), 'spine', True),
    'neck': ((0, 0.0, 3.65), (0, -0.05, 3.9), 'chest', True),
    'head': ((0, -0.05, 3.9), (0, -0.1, 4.5), 'neck', True),
    'shell.R': ((-0.9, -0.75, 3.7), (-0.9, -0.85, 2.5), 'chest', True),
    'shell.L': ((0.9, -0.75, 3.7), (0.9, -0.85, 2.5), 'chest', True),
    'halo': (HALO, (HALO[0], HALO[1] + 0.2, HALO[2] + 0.45), 'chest', False),
    'ring.C': (HALO, (HALO[0], HALO[1] + 0.2, HALO[2] + 0.45), 'halo', True),
    'ring.L': (RING_L, (RING_L[0], RING_L[1] + 0.2, RING_L[2] + 0.45), 'halo', True),
    'ring.R': (RING_R, (RING_R[0], RING_R[1] + 0.2, RING_R[2] + 0.45), 'halo', True),
    'pendulum': (PIVOT, (PIVOT[0], PIVOT[1], 2.25), 'chest', True),
    'upperarm.R': ((-1.1, -0.1, 3.4), (-1.6, -0.15, 2.55), 'chest', True),
    'forearm.R': ((-1.6, -0.15, 2.55), (-1.95, -0.25, 1.85), 'upperarm.R', True),
    'hand.R': ((-1.95, -0.25, 1.85), (-2.3, -0.5, 1.05), 'forearm.R', True),
    'upperarm.L': ((1.1, -0.1, 3.4), (1.6, -0.15, 2.55), 'chest', True),
    'forearm.L': ((1.6, -0.15, 2.55), (1.85, -0.2, 1.9), 'upperarm.L', True),
    'hand.L': ((1.85, -0.2, 1.9), (1.95, -0.3, 1.0), 'forearm.L', True),
    'thigh.R': ((-0.55, 0, 2.0), (-0.85, -0.05, 1.15), 'hips', True),
    'shin.R': ((-0.85, -0.05, 1.15), (-0.99, 0, 0.4), 'thigh.R', True),
    'foot.R': ((-0.99, 0, 0.4), (-0.99, -0.55, 0.05), 'shin.R', True),
    'thigh.L': ((0.55, 0, 2.0), (0.85, -0.05, 1.15), 'hips', True),
    'shin.L': ((0.85, -0.05, 1.15), (0.99, 0, 0.4), 'thigh.L', True),
    'foot.L': ((0.99, 0, 0.4), (0.99, -0.55, 0.05), 'shin.L', True),
}
ad = bpy.data.armatures.new('Bellkeeper2Rig'); arm = bpy.data.objects.new('Bellkeeper2Rig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: distance to bone segments, then make the loose parts rigid ----------------
# Bone-heat finds no solution on this mesh (thin plates, rings, chains; even on a voxel proxy), and a rigid clockwork
# body doesn't need it: each vertex follows its nearest bones by inverse distance, limited by side and by part.
RIGID = ('ring.C', 'ring.L', 'ring.R', 'pendulum', 'shell.L', 'shell.R')   # assigned by region below
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
x, y, z = co.T
D = np.full((nv, len(names)), np.inf, np.float32)
for n in names:
    if n in RIGID: continue
    h, t = np.array(B[n][0]), np.array(B[n][1]); v = t - h
    u = np.clip(((co - h) @ v) / (v @ v), 0, 1)
    D[:, gi[n]] = np.linalg.norm(co - (h + u[:, None] * v), axis=1)
def only(mask, allowed):     # restrict some vertices to a set of bones
    bad = [gi[n] for n in names if n not in allowed]
    D[np.ix_(np.nonzero(mask)[0], bad)] = np.inf
LIMB = lambda s: [b + '.' + s for b in ('upperarm', 'forearm', 'hand', 'thigh', 'shin', 'foot')]
D[np.ix_(np.nonzero(x > 0.25)[0], [gi[n] for n in LIMB('R')])] = np.inf     # no crossing to the other side
D[np.ix_(np.nonzero(x < -0.25)[0], [gi[n] for n in LIMB('L')])] = np.inf
D[np.ix_(np.nonzero(z > 2.3)[0], [gi[n] for n in ('thigh.L', 'thigh.R', 'shin.L', 'shin.R', 'foot.L', 'foot.R')])] = np.inf
D[np.ix_(np.nonzero(z < 1.6)[0], [gi[n] for n in ('chest', 'neck', 'head', 'upperarm.L', 'upperarm.R')])] = np.inf
only(arm_l, ('upperarm.L', 'forearm.L', 'hand.L'))
only(pend_head, ('hand.R',))
only(chains & (z < 2.0), ('hips',))
only((x < -1.25) & (z > 1.6) & (z < 3.5) & ~pend_head, ('upperarm.R', 'forearm.R', 'hand.R', 'chest'))
W = 1 / (D + 0.08) ** 4
W[~np.isfinite(D)] = 0
def row(**kw):
    r = np.zeros(len(names), np.float32)
    for k, v in kw.items(): r[gi[k.replace('_', '.')]] = v
    return r
# the rigid pieces cut free above follow one bone each
for i in range(1, 7):
    W[vlab == i] = row(**{RIGID_NAMES[i].replace('.', '_'): 1})
rigid_any = vlab > 0
for c in RIGID: W[~rigid_any & (W[:, gi[c]] < 0.999), gi[c]] = 0
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
HALO_N = Vector((0, -2.05, -0.9)).normalized()
def ring_spin(deg):   # turn one ring in its own (leaning) plane about its own centre
    Q = HALO_N.to_track_quat('Z', 'Y').to_matrix()
    return Q @ R(z=deg) @ Q.inverted()
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
        # the three rings drift out of step with each other
        'ring.C': {'rot': ring_spin(4 * math.sin(p))}, 'ring.L': {'rot': ring_spin(-6 * math.sin(p + 1.3))},
        'ring.R': {'rot': ring_spin(5 * math.sin(p + 2.4))},
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
def plant_feet(feet=None):
    # two-bone IK in armature space: the ankle goes to its rest point (plus an optional (dy, dz, pitch) step offset
    # per side for walking), the knee bends in the rest leg's plane
    for s, (h0, k0, a0) in LEGS.items():
        bpy.context.view_layer.update()
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        dy, dz, pitch = (feet or {}).get(s, (0, 0, 0))
        H = th.head.copy(); A = a0 + Vector((0, dy, dz))
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
        ft.matrix = Matrix.Translation(A) @ (R(x=pitch) @ ft.bone.matrix_local.to_3x3()).to_4x4()

def pose_of(ch):
    # ch: bone -> (x, y, z) degrees, plus 'hips_loc', 'halo_spin', 'halo_tilt', 'halo_loc' extras
    p = {}
    for n, v in ch.items():
        if n in ('hips_loc', 'halo_spin', 'halo_tilt', 'halo_loc', '_feet', 'rings'): continue
        p[n] = {'rot': R(*v)}
    p.setdefault('hips', {})['loc'] = ch.get('hips_loc', (0, 0, 0))
    p['halo'] = {'rot': halo_rot(ch.get('halo_spin', 0), ch.get('halo_tilt', 0)), 'loc': ch.get('halo_loc', (0, 0, 0))}
    # 'rings': (back, left, right) extra spins in degrees. The chest shell halves stay at rest in every clip: the
    # encounter opens them at runtime per phase and in punish windows (BellkeeperEncounter.PhaseLook).
    for n, d in zip(('ring.C', 'ring.L', 'ring.R'), ch.get('rings', (0, 0, 0))): p[n] = {'rot': ring_spin(d)}
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
        # sagging but upright enough that the open chest faces the players (the old hunch hid it)
        'hips_loc': (0, 0.05, -0.3 + 0.03 * math.sin(p)), 'hips': (2, 0, 0),
        'spine': (3 + sh, 0, 0), 'chest': (-4 + 2 * math.sin(p), 0, sh), 'neck': (6, 0, 0), 'head': (18, 0, 3 * math.sin(p)),
        'upperarm.R': (-12, -6, 0), 'upperarm.L': (-12, 6, 0), 'forearm.R': (-25, 0, 0), 'forearm.L': (-25, 0, 0),
        'hand.R': (15, 0, 0), 'hand.L': (15, 0, 0),
        'pendulum': (0, 4 * math.sin(p), 0),
        'halo_loc': (0, 0.05, -0.2 + 0.03 * math.sin(p + 1)), 'halo_tilt': 6, 'halo_spin': -10 + 2 * math.sin(p),
        # the punish window (spec v0.3 §3.1): the encounter opens the chest; the rings fall out of step
        'rings': (-8 + 3 * math.sin(p), 12, -15),
    }

def echo_tell(t):
    # 1.5 s, the Unfinished Hour's warning (90 ticks): the back ring turns BACKWARDS a full third of a turn while the
    # left (conducting) hand lifts and points at the remembered footprint; the side rings stall. Plays once per echo.
    T = 1.5
    lift = key(t, [(0, 0), (0.35, 1.0), (T - 0.25, 1.0), (T, 0.3)])
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, -0.08 * lift), 'chest': add(ch['chest'], (-4 * lift, 0, 6 * lift)), 'head': (2.5 - 6 * lift, 0, 8 * lift),
        'upperarm.L': (-70 * lift, -28 * lift, 0), 'forearm.L': (-25 * lift, 0, 0), 'hand.L': (-20 * lift, 0, 0),
        'upperarm.R': (6 * lift, -4 * lift, 0),
        'pendulum': (0, 6 * math.sin(2 * math.pi * t / 0.75) * (1 - lift * 0.7), 0),
        'rings': (key(t, [(0, 0), (T, -120)]), key(t, [(0, 0), (0.3, 6), (T, 6)]), key(t, [(0, 0), (0.3, -6), (T, -6)])),
        'halo_loc': (0, 0.05 * lift, 0.1 * lift),
    })
    return ch

WALK_CYCLE, WALK_STRIDE = 1.6, 1.4   # s per two steps, m per step: Unity moves the boss at STRIDE / (CYCLE / 2) = 1.75 m/s
def walk(t):
    # a heavy, planted machine stride: each boot slides back under the body while planted, then swings forward with a
    # low lift; the hips drop on each contact and sway over the standing leg; arms and pendulum swing against the legs.
    ph = (t / WALK_CYCLE) % 1.0; p = 2 * math.pi * ph
    def foot(q):
        q %= 1.0
        if q < 0.5: return (-WALK_STRIDE / 2 + WALK_STRIDE * q / 0.5, 0.0, 0.0)                 # planted, sliding back
        u = (q - 0.5) / 0.5
        return (WALK_STRIDE / 2 - WALK_STRIDE * smooth(u), 0.32 * math.sin(math.pi * u), -14 * math.sin(math.pi * u))
    side = math.sin(p)                          # + while the left foot is planted
    ch = breath(t)
    ch.update({
        '_feet': {'L': foot(ph), 'R': foot(ph + 0.5)},
        'hips_loc': (0.07 * side, 0, -0.2 - 0.07 * math.cos(2 * p)),
        'hips': (3, -2.5 * side, 5 * side), 'spine': (3, 1.5 * side, -3 * side),
        'chest': add(ch['chest'], (4, 0, -5 * side)), 'head': (2.5, 0, 3 * side),
        'upperarm.R': (-14 * side, 4, 0), 'upperarm.L': (14 * side, -4, 0),
        'forearm.R': (-12 - 6 * side, 0, 0), 'forearm.L': (-12 + 6 * side, 0, 0),
        'pendulum': (6 + 4 * math.sin(2 * p + 0.8), 9 * math.sin(p + 0.6), 0),
        'halo_loc': (0, 0, 0.04 * math.sin(2 * p + 1.0)), 'halo_spin': 3 * math.sin(p + 1.2),
    })
    return ch

def phase_shift(t):
    # 1.5 s phase transition (run loop, HP 70 % / 35 %): the bell gathers (hunch, arms in), then rings out with arms
    # flung wide and the halo spinning up, and settles back to its stance. No damage: presentation for the queued shift.
    T = 0.75
    g = key(t, [(0, 0), (T - 0.2, 1.0), (T, 1.0), (T + 0.1, -1.0), (T + 0.45, -0.7), (1.5, 0)])
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, key(t, [(0, 0), (T - .2, -0.3), (T + .1, 0.05), (1.5, 0)])),
        'spine': (10 * g, 0, 0), 'chest': add(ch['chest'], (14 * g, 0, 0)), 'head': (2.5 + 12 * g, 0, 0),
        'upperarm.R': (-30 * max(g, 0) - 60 * max(-g, 0), 70 * max(-g, 0), 0),
        'upperarm.L': (-30 * max(g, 0) - 60 * max(-g, 0), -70 * max(-g, 0), 0),
        'forearm.R': (-60 * max(g, 0), 0, 0), 'forearm.L': (-60 * max(g, 0), 0, 0),
        'pendulum': (ring(t, T, -30, 1.5, 1.6), 0, 0),
        'halo_loc': (0, 0, key(t, [(0, 0), (T, -0.1), (T + .15, 0.35), (1.5, 0)])),
        'halo_spin': key(t, [(0, 0), (T, -20), (1.5, 160)]), 'halo_tilt': key(t, [(0, 0), (T + .1, -10), (1.5, 0)]),
        # the rings desynchronise on the ring-out
        'rings': (key(t, [(0, 0), (1.5, -60)]), key(t, [(0, 0), (1.5, 90)]), key(t, [(0, 0), (1.5, -45)])),
    })
    return ch

def defeat(t):
    # 2.5 s, held on the last frame: the bell falters, then the machine sinks to its knees and slumps forward,
    # halo dropping askew and the pendulum running down.
    sag = key(t, [(0, 0), (0.5, 1.0), (2.5, 1.0)]); kneel = key(t, [(0, 0), (0.6, 0), (1.6, 1.0), (2.5, 1.0)])
    slump = key(t, [(0, 0), (1.2, 0), (2.2, 1.0), (2.5, 1.0)])
    ch = {
        'hips_loc': (0, 0.05 + 0.2 * kneel, -0.3 * sag - 0.75 * kneel), 'hips': (6 * sag + 10 * kneel, 0, 0),
        'spine': (10 * sag + 14 * slump, 0, 0), 'chest': (16 * sag + 18 * slump, 0, 4 * slump), 'neck': (8 * sag, 0, 0),
        'head': (24 * sag + 16 * slump, 0, 6 * slump),
        'upperarm.R': (-12 * sag + 25 * slump, -6, 0), 'upperarm.L': (-12 * sag + 25 * slump, 6, 0),
        'forearm.R': (-25 * sag, 0, 0), 'forearm.L': (-25 * sag, 0, 0), 'hand.R': (15, 0, 0), 'hand.L': (15, 0, 0),
        # the clapper swings once, is caught, then released to hang still (the encounter opens the chest); the rings stop
        'pendulum': (key(t, [(0, 0), (0.3, -35), (0.7, -35), (1.0, 0)]) + ring(t, 1.0, 12, 1.0, 1.5), 0, 0),
        'rings': (key(t, [(0, 0), (0.8, -20), (2.5, -22)]), key(t, [(0, 0), (0.8, 15), (2.5, 16)]), key(t, [(0, 0), (0.8, -10), (2.5, -11)])),
        'halo_loc': (0, 0.05, -0.32 * sag - 0.5 * slump), 'halo_tilt': 14 * sag + 26 * slump, 'halo_spin': -10 * sag - 25 * slump,
    }
    return ch

def bake_clip(name, seconds, fn, loop=False):
    frames = round(seconds * 30)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        ch = fn(f / 30); set_pose(pose_of(ch)); plant_feet(ch.get('_feet'))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

acts = [bake('Idle', 120, idle, True),
        bake_clip('BellSweep', 2.0, bell_sweep), bake_clip('HollowToll', 2.0, hollow_toll),
        bake_clip('PendulumCharge', 1.8, pendulum_charge), bake_clip('ShatteredChime', 2.6, shattered_chime),
        bake_clip('Vulnerable', 2.0, vulnerable, loop=True), bake_clip('Walk', WALK_CYCLE, walk, loop=True),
        bake_clip('PhaseShift', 1.5, phase_shift), bake_clip('Defeat', 2.5, defeat), bake_clip('EchoTell', 1.5, echo_tell)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'bellkeeper2_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
