# blender -b sylvara_prep.blend -P sylvara_rig.py -- <work dir> <out.glb>
# Rigs the prepared Sylvara (4 m, four root legs, from sylvara_prep.py), keys her clips and exports a GLB with JPEG
# textures. Same conventions as bellkeeper_rig.py:
# Axes: -Y forward, +X = Sylvara's left, Z up. Rotations are degrees in armature axes (see set_pose):
#   X: + swings a hanging bone's tip back, - raises it forward; Y: + moves a hanging bone's tip toward -X (lifts the right
#   arm out), - lifts the left arm out; Z: + turns toward her left.
# Her right hand (-X) holds the tuning fork, her left (+X) is the three-hook branch hand. The four root legs are solved
# with two-bone IK every frame, so the feet stay planted (packet: "deliberate separate root planting, no sliding").
# Clip names match SylvaraEncounter's PlayOnce calls; Unity stretches each clip to the attack's ticks.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Sylvara']

# ---------------- armature (joints measured from z-slices of the prepped mesh) ----------------
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.6, 0), None, False),
    'hips': ((0, 0, 2.1), (0, 0, 2.5), 'root', True),
    'spine': ((0, 0, 2.5), (0, 0, 2.9), 'hips', True),
    'chest': ((0, 0, 2.9), (0, 0, 3.4), 'spine', True),
    'head': ((0.1, -0.3, 3.05), (0.12, -0.62, 3.6), 'chest', True),
    'upperarm.R': ((-0.72, -0.15, 3.1), (-1.05, -0.35, 2.25), 'chest', True),
    'forearm.R': ((-1.05, -0.35, 2.25), (-1.2, -0.45, 1.45), 'upperarm.R', True),
    'hand.R': ((-1.2, -0.45, 1.45), (-1.38, -0.55, 1.12), 'forearm.R', True),     # the grip; the tines are attached whole below
    'upperarm.L': ((0.8, -0.1, 3.1), (1.3, -0.25, 2.25), 'chest', True),
    'forearm.L': ((1.3, -0.25, 2.25), (1.6, -0.45, 1.6), 'upperarm.L', True),
    'hand.L': ((1.6, -0.45, 1.6), (1.75, -0.6, 0.85), 'forearm.L', True),
}
# four root legs: hip, knee, ankle (feet from sylvara_prep's FEET), and the knee's bend direction
LEG = {
    'FR': ((-0.85, -0.15, 2.3), (-1.18, -0.42, 1.3), (-1.17, -0.72, 0.35), (-1, -0.3, 0)),
    'FL': ((0.85, -0.15, 2.3), (1.2, -0.42, 1.3), (1.2, -0.73, 0.35), (1, -0.3, 0)),
    'BR': ((-0.22, 0.15, 2.15), (-0.36, 0.5, 1.25), (-0.48, 0.76, 0.35), (-1, 0.3, 0)),
    'BL': ((0.22, 0.15, 2.15), (0.4, 0.5, 1.25), (0.51, 0.7, 0.35), (1, 0.3, 0)),
}
for s, (h, k, a, _) in LEG.items():
    B['thigh.' + s] = (h, k, 'hips', True)
    B['shin.' + s] = (k, a, 'thigh.' + s, True)
    B['foot.' + s] = (a, (a[0], a[1] - 0.25, 0.02), 'shin.' + s, True)
ad = bpy.data.armatures.new('SylvaraRig'); arm = bpy.data.objects.new('SylvaraRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: distance to each bone segment, soft-blended between the nearest bones ----------------
# Heat weighting fails on this mesh (spread roots and foliage cards; ~95 % of vertices came back empty), so weights
# come from the rest skeleton directly: each vertex follows the bones whose segments are nearest, blended over ~7 cm.
# The parts that sit too close to the wrong limb (fork tines, claw hooks, mask) are pinned below.
import bmesh
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
me = body.data
ARM = {n for n in names if n.startswith(('upperarm', 'forearm', 'hand'))}
LEGB = {n for n in names if n.startswith(('thigh', 'shin', 'foot'))}
arm_cols = np.array([n in ARM for n in names]); leg_cols = np.array([n in LEGB for n in names])
def verts():
    c = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', c); return c.reshape(-1, 3)
def distance_weights(c):
    D = np.full((len(c), len(names)), 1e9, np.float32)
    for n in names:
        if n == 'head': continue          # the head only takes the mask (pinned below), not the torso around it
        h, t = np.array(B[n][0], float), np.array(B[n][1], float); v = t - h
        u = np.clip(((c - h) @ v) / (v @ v), 0, 1)
        D[:, gi[n]] = np.linalg.norm(c - (h + u[:, None] * v), axis=1)
    return np.exp(-(D - D.min(1, keepdims=True)) / 0.07).astype(np.float32)

# Each arm hangs along its front leg and the shell bridges them with faces, which stretch into sheets when an arm lifts.
# Split the mesh along every edge where an arm face meets a leg face, then keep arm vertices off leg bones and back.
dom = distance_weights(verts()).argmax(1)
kind = np.where(arm_cols[dom], 1, np.where(leg_cols[dom], 2, 0))   # 1 arm, 2 leg, 0 body
bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
face_kind = {f.index: int(np.bincount([kind[v.index] for v in f.verts], minlength=3).argmax()) for f in bm.faces}
seam = [e for e in bm.edges if len(e.link_faces) == 2 and {face_kind[f.index] for f in e.link_faces} == {1, 2}]
bmesh.ops.split_edges(bm, edges=seam)
bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
vkinds = [{face_kind[f.index] for f in v.link_faces} for v in bm.verts]   # split keeps face indices
bm.to_mesh(me); bm.free(); me.update()
print('SEAM_EDGES', len(seam))
co = verts(); nv = len(co); x, y, z = co[:, 0], co[:, 1], co[:, 2]
W = distance_weights(co)
only_arm = np.array([k == {1} or k == {0, 1} for k in vkinds]); only_leg = np.array([k == {2} or k == {0, 2} for k in vkinds])
W[np.ix_(only_arm, leg_cols)] = 0; W[np.ix_(only_leg, arm_cols)] = 0
def row(**kw):
    r = np.zeros(len(names), np.float32)
    for k, v in kw.items(): r[gi[k.replace('_', '.')]] = v
    return r
# The fork's two tines are separate loose pieces (~30 vertices each) that hang beside the front-right leg: attach each
# whole piece to the hand so the fork stays rigid. The three hooks of the branch hand are part of the main shell, beside
# the front-left leg, so they are picked by position.
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
_, part = np.unique([find(i) for i in range(nv)], return_inverse=True)
fork = np.zeros(nv, bool)
for p in range(part.max() + 1):
    m = part == p; c = co[m]
    if m.sum() < 200 and c[:, 0].max() < -1.1 and c[:, 2].min() < 0.45 and c[:, 2].max() - c[:, 2].min() > 0.8: fork |= m
claw = (x > 1.4) & (z > 0.6) & (z < 2.1) & (y < -0.2)
W[fork] = row(hand_R=1); W[claw] = row(hand_L=1)
# every other small loose piece (leaf cards, flowers, brass bits) moves as one rigid unit: its averaged weights
small = 0
for p in range(part.max() + 1):
    m = part == p
    if m.sum() < 200 and not fork[m].any(): W[m] = W[m].mean(0); small += 1
print('RIGID_PIECES', small)
# the mask is ivory stone: keep it rigid on the head
mask = (z > 2.95) & (y < -0.45) & (np.abs(x - 0.12) < 0.35)
W[mask] = row(head=1)
print('PINNED fork', int(fork.sum()), 'claw', int(claw.sum()), 'mask', int(mask.sum()))
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

# ---------------- animation helpers (bellkeeper_rig.py conventions) ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value) pairs
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            return tuple(a + (b - a) * u for a, b in zip(v0, v1)) if isinstance(v0, tuple) else v0 + (v1 - v0) * u
    return pts[-1][1]
def snap(t, t0, rise=0.06, fall=0.25):
    if t < t0 - rise or t > t0 + fall: return 0.0
    return smooth((t - (t0 - rise)) / rise) if t < t0 else 1 - smooth((t - t0) / fall)
def add(*vs): return tuple(sum(c) for c in zip(*vs))

def plant_feet(feet=None):
    # two-bone IK in armature space per root leg: the ankle goes to its rest point plus an optional (dy, dz) step
    # offset; the knee bends toward the leg's pole direction (outward and a little forward or back)
    for s, (h0, k0, a0, pole) in LEG.items():
        bpy.context.view_layer.update()
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        dy, dz = (feet or {}).get(s, (0, 0))
        H = th.head.copy(); A = Vector(a0) + Vector((0, dy, dz))
        L1, L2 = (Vector(k0) - Vector(h0)).length, (Vector(a0) - Vector(k0)).length
        d = min((A - H).length, L1 + L2 - 1e-4); u = (A - H).normalized()
        pv = Vector(pole); v = (pv - pv.project(u)).normalized()
        ca = max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)))
        K = H + L1 * (ca * u + math.sqrt(1 - ca * ca) * v)
        for pb, a, b in ((th, H, K), (sh, K, A)):
            r0 = pb.bone.matrix_local.to_3x3(); rest = (pb.bone.tail_local - pb.bone.head_local).normalized()
            pb.matrix = Matrix.Translation(a) @ (rest.rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ ft.bone.matrix_local.to_3x3().to_4x4()

def pose_of(ch):
    p = {}
    for n, v in ch.items():
        if n in ('hips_loc', '_feet'): continue
        p[n] = {'rot': R(*v)}
    p.setdefault('hips', {})['loc'] = ch.get('hips_loc', (0, 0, 0))
    return p

def breath(t):  # a slow listening sway under every clip so holds never freeze
    b = math.sin(2 * math.pi * t / 4.0)
    return {'chest': (-1.0 * b, 0, 0.8 * math.sin(2 * math.pi * t / 4.0 + 1.3)), 'head': (2, 0, 0)}

# ---------------- clips ----------------
def idle(t):
    # 4 s loop: the garden is listening. A slow breath, the shrine sways a little over planted roots, the mask stays
    # almost still while the arms drift.
    p = 2 * math.pi * t / 4.0; br = math.sin(p); sw = math.sin(p + 0.9)
    ch = breath(t)
    ch.update({
        'hips_loc': (0.02 * sw, 0, 0.03 * br), 'hips': (0, 0.8 * sw, 0), 'spine': (1.0 * br, 0.6 * sw, 0),
        'head': (2 + 0.6 * math.sin(p + 0.5), 0, -1.0 * sw),
        'upperarm.R': (-1.5 * math.sin(p + 0.5), 1.0 * br, 0), 'forearm.R': (-1.5 * math.sin(p + 1.0), 0, 0),
        'upperarm.L': (-2.0 * math.sin(p + 0.8), -1.2 * br, 0), 'forearm.L': (-2.5 * math.sin(p + 1.2), 0, 0),
        'hand.L': (-3.0 * math.sin(p + 1.6), 0, 0),
    })
    return ch

WALK_CYCLE, WALK_STRIDE = 0.9, 0.72   # Unity's Approach moves her at 1.6 m/s: stance 0.45 s x 1.6 m/s = 0.72 m
def walk(t):
    # diagonal root gait: front-right with back-left, then front-left with back-right. Each root slides back under the
    # body while planted, then lifts and replants ahead.
    ph = (t / WALK_CYCLE) % 1.0
    def foot(q):
        q %= 1.0
        if q < 0.5: return (-WALK_STRIDE / 2 + WALK_STRIDE * q / 0.5, 0.0)
        u = (q - 0.5) / 0.5
        return (WALK_STRIDE / 2 - WALK_STRIDE * smooth(u), 0.3 * math.sin(math.pi * u))
    p = 2 * math.pi * ph; side = math.sin(p)
    ch = breath(t)
    ch.update({
        '_feet': {'FR': foot(ph), 'BL': foot(ph), 'FL': foot(ph + 0.5), 'BR': foot(ph + 0.5)},
        'hips_loc': (0.04 * side, 0, -0.08 - 0.05 * math.cos(2 * p)), 'hips': (3, -1.5 * side, 3 * side),
        'chest': add(ch['chest'], (3, 0, -3 * side)), 'head': (2, 0, 2 * side),
        'upperarm.R': (-8 * side, 3, 0), 'upperarm.L': (8 * side, -3, 0),
    })
    return ch

def pruning_arc(t):
    # 1.8 s, resolve at 1.6 (the cone's resolve): the fork arm draws back and out like a bow while the torso coils to
    # her right; at 1.6 one plucked sweep across her front to the left; no backswing.
    T = 1.6
    c = key(t, [(0, 0), (0.9, 0.85), (T - 0.05, 1.0), (T + 0.07, -1.0), (1.8, -1.0)])
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, key(t, [(0, 0), (T, -0.12), (T + .1, -0.2), (1.8, -0.18)])),
        'hips': (0, 0, -7 * c), 'spine': (0, 0, -10 * c),
        'chest': add(ch['chest'], (key(t, [(0, 0), (T, -4), (T + .1, 10), (1.8, 8)]), 0, -20 * c)),
        'head': (2, 0, 8 * c),
        'upperarm.R': (key(t, [(0, 0), (T - .05, 30), (T + .07, -70), (1.8, -62)]),
                       key(t, [(0, 0), (T - .05, 70), (T + .07, 40), (1.8, 32)]), 0),
        'forearm.R': (key(t, [(0, 0), (T - .05, -50), (T + .07, -10), (1.8, -15)]), 0, 0),
        'hand.R': (key(t, [(0, 0), (T - .05, 20), (T + .07, -15), (1.8, -10)]), 0, 0),
        'upperarm.L': (key(t, [(0, 0), (T - .05, -20), (T + .07, 15), (1.8, 10)]), key(t, [(0, 0), (T - .05, 15), (T + .07, -20), (1.8, -15)]), 0),
    })
    return ch

def rootscript(t):
    # 2.1 s, resolve at 1.9: the three-hook hand reaches down and touches the floor ahead (root drawings spread), she
    # holds the touch while the lanes fill, then the floor cracks up at 1.9 and she jerks back.
    T = 1.9
    reach = key(t, [(0, 0), (0.6, 1.0), (T - 0.05, 1.0), (T + 0.1, 0.25), (2.1, 0.3)])
    jolt = snap(t, T, 0.05, 0.2)
    ch = breath(t)
    ch.update({
        'hips_loc': (0, -0.1 * reach, -0.4 * reach + 0.08 * jolt),
        'hips': (6 * reach, 0, 4 * reach), 'spine': (10 * reach - 6 * jolt, 0, 4 * reach),
        'chest': add(ch['chest'], (16 * reach - 10 * jolt, 0, 6 * reach)), 'head': (2 - 6 * reach + 10 * jolt, 0, 0),
        'upperarm.L': (-55 * reach, -10 * reach, 0), 'forearm.L': (-30 * reach, 0, 0), 'hand.L': (-25 * reach + 15 * jolt, 0, 0),
        'upperarm.R': (12 * reach, 18 * reach, 0), 'forearm.R': (-10 * reach, 0, 0),
    })
    return ch

def seed(t):
    # 2.2 s, resolve at 2.2: both arms rise slowly to call the buds over the players; a rising three-note phrase plays
    # in the last 0.9 s (three small lifts of the head and chest), and the arms open on the eruption.
    up = key(t, [(0, 0), (1.1, 1.0), (2.0, 1.0), (2.2, 1.15)])
    n1, n2, n3 = snap(t, 1.4, .05, .2), snap(t, 1.7, .05, .2), snap(t, 2.0, .05, .2)
    lift = n1 + 1.3 * n2 + 1.6 * n3
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, 0.06 * up), 'spine': (-4 * up, 0, 0),
        'chest': add(ch['chest'], (-8 * up - 3 * lift, 0, 0)), 'head': (2 - 10 * up - 4 * lift, 0, 0),
        'upperarm.R': (-70 * up, 35 * up, 0), 'forearm.R': (-20 * up, 0, 0),
        'upperarm.L': (-70 * up, -35 * up, 0), 'forearm.L': (-20 * up, 0, 0), 'hand.L': (-15 * up - 6 * lift, 0, 0),
    })
    return ch

def procession(t):
    # 2.45 s: she gathers her four roots and lowers the shrine (to 1.8 s, the lane's resolve), then scuttles forward
    # for 0.65 s (Unity moves her): fast alternating root steps under a forward-pitched body.
    T, E = 1.8, 2.45
    low = key(t, [(0, 0), (1.0, 1.0), (E, 1.0)])
    run = 0 if t < T else min(1, (t - T) / 0.08)
    q = max(0.0, t - T) * 6.0     # six steps per second while scuttling
    def step(off):
        u = (q + off) % 1.0
        return (0.25 * math.cos(2 * math.pi * u) * run, 0.22 * max(0, math.sin(2 * math.pi * u)) * run)
    ch = breath(t)
    ch.update({
        '_feet': {'FR': step(0), 'BL': step(0), 'FL': step(0.5), 'BR': step(0.5)},
        'hips_loc': (0, -0.05 * low, -0.45 * low), 'hips': (8 * low + 6 * run, 0, 0),
        'spine': (6 * low, 0, 0), 'chest': add(ch['chest'], (8 * low + 6 * run, 0, 0)), 'head': (2 - 10 * low, 0, 0),
        'upperarm.R': (25 * low, 10 * low, 0), 'forearm.R': (-15 * low, 0, 0),
        'upperarm.L': (25 * low, -10 * low, 0), 'forearm.L': (-15 * low, 0, 0),
    })
    return ch

def bloom(t):
    # 2 s, from the start of Threefold Bloom: she conducts with the tuning fork raised, one downbeat per flower
    # (the flowers' warnings start 1.2 s apart), the branch hand opening toward the floor.
    up = key(t, [(0, 0), (0.35, 1.0), (1.75, 1.0), (2.0, 0.6)])
    b = snap(t, 0.45, .06, .3) + snap(t, 1.0, .06, .3) + snap(t, 1.55, .06, .3)
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, 0.05 * up - 0.05 * b), 'spine': (-3 * up, 0, 0),
        'chest': add(ch['chest'], (-6 * up + 5 * b, 0, -6 * up)), 'head': (2 - 8 * up + 5 * b, 0, 0),
        'upperarm.R': (-105 * up + 22 * b, 20 * up, 0), 'forearm.R': (-25 * up + 10 * b, 0, 0), 'hand.R': (-10 * up + 15 * b, 0, 0),
        'upperarm.L': (-40 * up, -45 * up, 0), 'forearm.L': (-10 * up, 0, 0), 'hand.L': (-20 * up, 0, 0),
    })
    return ch

def vulnerable(t):
    # 2 s loop (recoveries and the Bloom punish): the torso folds, the roots brace, the shrine sags and shudders.
    p = 2 * math.pi * t / 2.0; sh = 0.7 * math.sin(7 * p) * (0.5 + 0.5 * math.sin(p))
    return {
        'hips_loc': (0, 0.04, -0.28 + 0.03 * math.sin(p)), 'hips': (5, 0, 0),
        'spine': (10 + sh, 0, 0), 'chest': (16 + 2 * math.sin(p), 0, sh), 'head': (20, 0, 3 * math.sin(p)),
        'upperarm.R': (-10, 6, 0), 'forearm.R': (-15, 0, 0), 'upperarm.L': (-10, -6, 0), 'forearm.L': (-20, 0, 0),
        'hand.L': (12, 0, 0),
    }

def phase_shift(t):
    # 1.5 s, no damage: the shrine gathers, then throws its chest back with the arms flung wide (the bells spread and
    # the flowers open), and settles.
    T = 0.75
    g = key(t, [(0, 0), (T - 0.2, 1.0), (T, 1.0), (T + 0.1, -1.0), (T + 0.45, -0.7), (1.5, 0)])
    ch = breath(t)
    ch.update({
        'hips_loc': (0, 0, key(t, [(0, 0), (T - .2, -0.25), (T + .1, 0.05), (1.5, 0)])),
        'spine': (8 * g, 0, 0), 'chest': add(ch['chest'], (12 * g, 0, 0)), 'head': (2 + 10 * g, 0, 0),
        'upperarm.R': (-25 * max(g, 0) - 50 * max(-g, 0), 60 * max(-g, 0), 0),
        'upperarm.L': (-25 * max(g, 0) - 50 * max(-g, 0), -60 * max(-g, 0), 0),
        'forearm.R': (-50 * max(g, 0), 0, 0), 'forearm.L': (-50 * max(g, 0), 0, 0),
    })
    return ch

def defeat(t):
    # 2.5 s, held: the shrine settles into a seated pose; the roots relax outward, the mask bows and the arms rest.
    sit = key(t, [(0, 0), (0.4, 0.15), (1.8, 1.0), (2.5, 1.0)]); bow = key(t, [(0, 0), (1.0, 0), (2.3, 1.0), (2.5, 1.0)])
    return {
        'hips_loc': (0, 0.1 * sit, -1.05 * sit), 'hips': (4 * sit, 0, 0),
        'spine': (6 * sit + 8 * bow, 0, 0), 'chest': (8 * sit + 10 * bow, 0, 0), 'head': (12 * sit + 22 * bow, 0, 4 * bow),
        'upperarm.R': (-8 * sit, -10 * sit, 0), 'forearm.R': (-20 * sit, 0, 0),
        'upperarm.L': (-8 * sit, 10 * sit, 0), 'forearm.L': (-25 * sit, 0, 0), 'hand.L': (20 * bow, 0, 0),
    }

def bake_clip(name, seconds, fn):
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

acts = [bake_clip('Idle', 4.0, idle), bake_clip('Walk', WALK_CYCLE, walk),
        bake_clip('PruningArc', 1.8, pruning_arc), bake_clip('Rootscript', 2.1, rootscript), bake_clip('Seed', 2.2, seed),
        bake_clip('Procession', 2.45, procession), bake_clip('Bloom', 2.0, bloom), bake_clip('Vulnerable', 2.0, vulnerable),
        bake_clip('PhaseShift', 1.5, phase_shift), bake_clip('Defeat', 2.5, defeat)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'sylvara_rig.blend'))
os.makedirs(os.path.dirname(out_glb), exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
