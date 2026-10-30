# blender -b aurelion_prep.blend -P aurelion_rig.py -- <work dir> <out.glb>
# Rigs the prepared Aurelion (4.2 m, from aurelion_prep.py), keys his clips and exports a GLB with JPEG textures.
# Axes: -Y forward, +X = Aurelion's left, Z up; the glaive is in his right hand (-X). Rotations are degrees in rest-
# armature axes (see set_pose): X: + tips the top of a bone forward (toward -Y), so + swings a hanging arm back;
# Y: + tilts the top toward +X (his left); Z: + turns toward his left. Same conventions as bellkeeper_rig.py.
# Clip timing matches AurelionEncounter (Unity stretches each clip to the attack's length, so the strike frame lands
# on the hazard's resolve tick): MeridianCut 3.1 s (strike 1.6), ZenithPins 3.15 s (lock 1.3, strike 1.75),
# ProcessionalThrust 4.4 s (lunge 1.7-2.2), CrownReversal 3.3 s (snap 1.5), MeridianAlignment 3.8 s (pulses 2.0, 3.8),
# OpenChest 2 s loop, PhaseShift 1.5 s, Defeat 2.5 s, Idle 4 s loop, Walk 1.4 s loop.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Aurelion']

# ---------------- armature (joints measured from the 0.25 m grid renders of the prepped mesh) ----------------
GRIP = (-0.93, -0.25, 2.42)   # right fist around the glaive shaft
PIVOT = (-0.01, -0.20, 2.97)  # pendulum rod top inside the chest opening; the bob hangs at z 2.75
HALO = (0.0, 0.42, 3.35)      # behind the shoulders, between the two pairs of arcs
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.6, 0), None, False),
    'hips': ((0, 0, 2.1), (0, 0, 2.42), 'root', True),
    'spine': ((0, 0, 2.42), (0, 0, 2.72), 'hips', True),
    'chest': ((0, 0, 2.72), (0, 0, 3.12), 'spine', True),
    'neck': ((0, -0.02, 3.12), (0, -0.03, 3.28), 'chest', True),
    'head': ((0, -0.03, 3.28), (0, -0.03, 3.9), 'neck', True),
    'upperarm.R': ((-0.52, 0, 2.85), (-0.6, -0.05, 2.5), 'chest', True),
    'forearm.R': ((-0.6, -0.05, 2.5), (-0.9, -0.22, 2.44), 'upperarm.R', True),
    'hand.R': ((-0.9, -0.22, 2.44), (-0.98, -0.26, 2.38), 'forearm.R', True),
    'glaive': (GRIP, (GRIP[0], GRIP[1], GRIP[2] + 0.6), 'hand.R', True),
    'upperarm.L': ((0.48, 0, 2.85), (0.56, 0, 2.38), 'chest', True),
    'forearm.L': ((0.56, 0, 2.38), (0.62, -0.03, 1.85), 'upperarm.L', True),
    'hand.L': ((0.62, -0.03, 1.85), (0.64, -0.05, 1.6), 'forearm.L', True),
    'thigh.R': ((-0.22, 0, 2.08), (-0.36, 0, 1.03), 'hips', True),
    'shin.R': ((-0.36, 0, 1.03), (-0.5, 0.02, 0.31), 'thigh.R', True),
    'foot.R': ((-0.5, 0.02, 0.31), (-0.62, -0.3, 0.03), 'shin.R', True),
    'thigh.L': ((0.21, 0, 2.08), (0.35, 0, 1.03), 'hips', True),
    'shin.L': ((0.35, 0, 1.03), (0.49, 0.02, 0.31), 'thigh.L', True),
    'foot.L': ((0.49, 0.02, 0.31), (0.61, -0.3, 0.03), 'shin.L', True),
    'pendulum': (PIVOT, (PIVOT[0], PIVOT[1], 2.75), 'chest', True),
    'halo': (HALO, (HALO[0], HALO[1], HALO[2] + 0.4), 'chest', True),
    'halo.R': ((-0.12, 0.42, 3.3), (-0.12, 0.42, 3.7), 'halo', True),
    'halo.L': ((0.12, 0.42, 3.3), (0.12, 0.42, 3.7), 'halo', True),
}
RIGID = ('glaive', 'pendulum', 'halo', 'halo.R', 'halo.L')
ad = bpy.data.armatures.new('AurelionRig'); arm = bpy.data.objects.new('AurelionRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: heat-weight a watertight proxy, transfer, then pin the loose parts ----------------
bpy.ops.object.select_all(action='DESELECT')
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'proxy'; sc.collection.objects.link(proxy)
rm = proxy.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.025
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
def row(**kw):
    r = np.zeros(len(names), np.float32)
    for k, v in kw.items(): r[gi[k.replace('_', '.')]] = v
    return r
# loose parts (connected components) and their mean texture colour (navy banners vs ivory armour)
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(nv)]); _, part = np.unique(roots, return_inverse=True)
base = next(n.image for n in body.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image.name == 'aurelion_basecolor')
S = base.size[0]; px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)
uv = me.uv_layers.active.data; vuv = np.zeros((nv, 2))
for l in me.loops: vuv[l.vertex_index] = uv[l.index].uv
vcol = px[np.clip((vuv[:, 1] * S).astype(int), 0, S - 1), np.clip((vuv[:, 0] * S).astype(int), 0, S - 1), :3]
counts = {}
for p in range(part.max() + 1):
    m = part == p; c = co[m]; lo, hi = c.min(0), c.max(0); ctr = (lo + hi) / 2; col = vcol[m].mean(0)
    navy = col[2] > col[0] * 1.15
    if m.sum() == np.bincount(part).max(): continue                    # the main shell: heat weights
    if lo[1] > 0.2 and lo[2] > 2.9: kind = 'halo' if abs(ctr[0]) < 0.1 else ('halo.R' if ctr[0] < 0 else 'halo.L')
    elif hi[0] < -0.66: kind = 'glaive'                                # shaft, blade, rings, grip
    elif lo[2] > 3.95: kind = 'halo'                                   # the floating spire above the halo
    elif np.linalg.norm(ctr - (-0.01, -0.195, 2.755)) < 0.08: kind = 'pendulum'
    elif navy and lo[2] > 0.4 and 2.1 < hi[2] < 2.7: kind = 'hips'      # waist banners hang from the belt
    elif abs(ctr[0]) < 0.25 and lo[2] < 1.0 and 2.3 < hi[2] < 2.7: kind = 'hips'   # the ivory centre tabard
    else: continue
    W[m] = row(**{kind.replace('.', '_'): 1}); counts[kind] = counts.get(kind, 0) + int(m.sum())
    print('PIN', kind, int(m.sum()), np.round(lo, 2), np.round(hi, 2), 'navy' if navy else '')
print('RIGID_PARTS', counts)
# rigid bones never pull on the shell
for c in RIGID:
    j = gi[c]; W[W[:, j] < 0.999, j] = 0
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

# ---------------- animation helpers (same as bellkeeper_rig.py) ----------------
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
def key(t, pts):
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            return tuple(a + (b - a) * u for a, b in zip(v0, v1)) if isinstance(v0, tuple) else v0 + (v1 - v0) * u
    return pts[-1][1]
def snap(t, t0, rise=0.06, fall=0.25):
    if t < t0 - rise or t > t0 + fall: return 0.0
    return smooth((t - (t0 - rise)) / rise) if t < t0 else 1 - smooth((t - t0) / fall)
def ring(t, t0, amp, freq=1.6, damp=2.2):
    return 0.0 if t < t0 else amp * math.exp(-damp * (t - t0)) * math.sin(2 * math.pi * freq * (t - t0))

LEGS = {s: (Vector(B['thigh.' + s][0]), Vector(B['shin.' + s][0]), Vector(B['shin.' + s][1])) for s in 'LR'}
def plant_feet(feet=None):
    # two-bone IK in armature space: ankles to their rest points (plus an optional (dy, dz, pitch) step offset)
    for s, (h0, k0, a0) in LEGS.items():
        bpy.context.view_layer.update()
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        dy, dz, pitch = (feet or {}).get(s, (0, 0, 0))
        H = th.head.copy(); A = a0 + Vector((0, dy, dz))
        L1, L2 = (k0 - h0).length, (a0 - k0).length
        d = min((A - H).length, L1 + L2 - 1e-4); u = (A - H).normalized()
        pole = (k0 - h0) - (k0 - h0).project(a0 - h0)
        if pole.length < 1e-4: pole = Vector((0, -1, 0))
        v = (pole - pole.project(u)).normalized()
        ca = max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)))
        K = H + L1 * (ca * u + math.sqrt(1 - ca * ca) * v)
        for pb, a, b in ((th, H, K), (sh, K, A)):
            r0 = pb.bone.matrix_local.to_3x3(); rest = (pb.bone.tail_local - pb.bone.head_local).normalized()
            pb.matrix = Matrix.Translation(a) @ (rest.rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ (R(x=pitch) @ ft.bone.matrix_local.to_3x3()).to_4x4()

EXTRA = ('hips_loc', 'halo_loc', '_feet', 'glaive')
def pose_of(ch):
    p = {}
    for n, v in ch.items():
        if n in EXTRA: continue
        p[n] = {'rot': R(*v)}
    p.setdefault('hips', {})['loc'] = ch.get('hips_loc', (0, 0, 0))
    p.setdefault('halo', {})['loc'] = ch.get('halo_loc', (0, 0, 0))
    return p
def add(*vs): return tuple(sum(c) for c in zip(*vs))
GLAIVE_DOWN = GRIP[2]   # shaft length from the grip down to the butt (it rests on the floor)
def aim_glaive(rot):
    # the glaive's direction is authored absolutely in armature axes (0,0,0 = upright as in the rest pose), so arm
    # rotations never flip it; it stays in the fist, sliding up the shaft if the butt would go through the floor
    bpy.context.view_layer.update()
    g = pbs['glaive']; head = g.head.copy(); M = R(*rot)
    up = M @ Vector((0, 0, 1)); butt = head - up * GLAIVE_DOWN
    if butt.z < 0 and up.z > 0.2: head = head + up * (-butt.z / up.z)
    g.matrix = Matrix.Translation(head) @ (M @ g.bone.matrix_local.to_3x3()).to_4x4()

def bake_clip(name, seconds, fn, loop=False):
    frames = round(seconds * 30)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        ch = fn(f / 30)   # loop clips are periodic in their length, so the last frame matches the first
        set_pose(pose_of(ch)); plant_feet(ch.get('_feet'))
        aim_glaive(ch.get('glaive', (0, 0, 0)))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

# ---------------- clips ----------------
def base_pose(t, period=4.0):
    # alive under every clip: one slow breath, the pendulum's idle beat, the halo arcs drifting a little
    p = 2 * math.pi * t / period; br = math.sin(p)
    return {
        'chest': (-1.2 * br, 0, 0), 'head': (1.5, 0, 0), 'neck': (0.6 * br, 0, 0),
        'pendulum': (0, 16 * math.sin(2 * math.pi * t / (period / max(1, round(period)))), 0),   # ~one beat a second, whole beats per loop
        'halo.L': (0, 1.5 * math.sin(p + 0.8), 0), 'halo.R': (0, -1.5 * math.sin(p + 2.0), 0),
        'halo_loc': (0, 0, 0.03 * math.sin(p + 0.4)),
    }

def idle(t):
    # measured stillness: breath, a head turn that leads the chest, the glaive swaying a degree or two
    p = 2 * math.pi * t / 4.0
    ch = base_pose(t)
    ch.update({
        'hips': (0, 0.5 * math.sin(p + 0.9), 0), 'spine': (1.0 * math.sin(p), 0.5 * math.sin(p + 0.9), 0),
        'chest': add(ch['chest'], (0, 0, 1.2 * math.sin(p + 1.6))),
        'head': (1.5 + 1.0 * math.sin(p + 0.6), 0, 4.0 * math.sin(p + 0.4)),
        'upperarm.L': (-1.5 * math.sin(p + 0.7), 1.0 * math.sin(p), 0), 'forearm.L': (-2.0 * math.sin(p + 1.2), 0, 0),
        'glaive': (1.5 * math.sin(p + 0.3), 1.0 * math.sin(p + 1.1), 0),
    })
    return ch

WALK_CYCLE, WALK_STRIDE = 1.4, 1.54   # two steps per cycle: Unity walks him at STRIDE / (CYCLE / 2) = 2.2 m/s
def walk(t):
    # heel-to-toe measured steps; the glaive arm stays steady, the free arm swings against the legs
    ph = (t / WALK_CYCLE) % 1.0; p = 2 * math.pi * ph
    def foot(q):
        q %= 1.0
        if q < 0.5: return (-WALK_STRIDE / 2 + WALK_STRIDE * q / 0.5, 0.0, 0.0)
        u = (q - 0.5) / 0.5
        return (WALK_STRIDE / 2 - WALK_STRIDE * smooth(u), 0.28 * math.sin(math.pi * u), -12 * math.sin(math.pi * u))
    side = math.sin(p)
    ch = base_pose(t, WALK_CYCLE)
    ch.update({
        '_feet': {'L': foot(ph), 'R': foot(ph + 0.5)},
        'hips_loc': (0.05 * side, 0, -0.16 - 0.05 * math.cos(2 * p)),
        'hips': (2, -2 * side, 4 * side), 'spine': (2, 1 * side, -2.5 * side),
        'chest': add(ch['chest'], (2, 0, -3.5 * side)), 'head': (1.5, 0, 3 * side),
        'upperarm.L': (16 * side, -3, 0), 'forearm.L': (-10 + 6 * side, 0, 0),
        'upperarm.R': (-4 * side, 0, 0), 'glaive': (3 * side, 0, 0),
    })
    return ch

def meridian_cut(t):
    # 1.6 s windup: the glaive is drawn up and back over his right shoulder as the torso coils right (three clicks);
    # at 1.6 it cuts across his front to the left; he holds the lowered blade through the 1.5 s punish window.
    T = 1.6
    c = key(t, [(0, 0), (0.6, 0.7), (T - 0.08, 1.0), (T + 0.06, -1.0), (T + 0.45, -1.05), (2.6, -0.6), (3.1, 0)])
    clicks = sum(snap(t, x, 0.03, 0.12) for x in (0.45, 0.85, 1.25))
    ch = base_pose(t)
    ch.update({
        'hips': (0, 0, -8 * c), 'spine': (key(t, [(0, 0), (T, -3), (T + .1, 8), (3.1, 0)]), 0, -12 * c),
        'chest': add(ch['chest'], (key(t, [(0, 0), (T, -5), (T + .1, 10), (T + .6, 8), (3.1, 0)]), 0, -18 * c - 1.5 * clicks)),
        'head': (key(t, [(0, 1.5), (T, -3), (T + .1, 6), (3.1, 1.5)]), 0, 12 * c),
        'upperarm.R': (key(t, [(0, 0), (T - .05, -80), (T + .06, -70), (T + .45, -40), (2.6, -30), (3.1, 0)]),
                       key(t, [(0, 0), (T - .05, 40), (T + .06, -10), (T + .45, -20), (3.1, 0)]), 0),
        'forearm.R': (key(t, [(0, 0), (T - .05, -50), (T + .06, -5), (3.1, 0)]), 0, 0),
        'glaive': (key(t, [(0, 0), (T - .05, -40), (T + .06, 82), (T + .45, 100), (2.6, 96), (3.1, 0)]),
                   key(t, [(0, 0), (T - .05, -30), (T + .06, 0), (3.1, 0)]),
                   key(t, [(0, 0), (T - .05, -50), (T + .06, 55), (T + .45, 65), (3.1, 0)])),
        'upperarm.L': (key(t, [(0, 0), (T - .05, -20), (T + .08, 20), (3.1, 0)]), key(t, [(0, 0), (T, -15), (T + .1, 10), (3.1, 0)]), 0),
        'hips_loc': (0, 0, key(t, [(0, 0), (T - .1, -0.1), (T + .06, -0.18), (T + .5, -0.15), (3.1, 0)])),
        'halo.L': (0, ring(t, T, 8, 1.4, 2.5), 0), 'halo.R': (0, ring(t, T, -8, 1.4, 2.5), 0),
    })
    return ch

def zenith_pins(t):
    # the halo points at the recipients: his free hand rises and the arcs tip forward while the circles track; at the
    # lock (1.3) they snap still; at 1.75 the hand drops like a conductor's beat and the spears land.
    L, T = 1.3, 1.75
    up = key(t, [(0, 0), (0.7, 1.0), (T - 0.05, 1.0), (T + 0.1, 0.25), (3.15, 0)])
    ch = base_pose(t)
    ch.update({
        'upperarm.L': (-130 * up + 50 * snap(t, T, 0.05, 0.3), -25 * up, 0), 'forearm.L': (-20 * up, 0, 0),
        'hand.L': (-25 * up, 0, 0),
        'chest': add(ch['chest'], (-6 * up, 0, 4 * up)), 'head': (1.5 - 12 * up + 8 * snap(t, T, 0.05, 0.3), 0, 0),
        'halo': (key(t, [(0, 0), (L, 22), (T, 22), (T + .1, -6), (3.15, 0)]), 0, 0),
        'halo.L': (0, key(t, [(0, 0), (L, 14), (T, 14), (T + .15, 0)]) + ring(t, T, 6, 1.8, 3), 0),
        'halo.R': (0, key(t, [(0, 0), (L, -14), (T, -14), (T + .15, 0)]) + ring(t, T, -6, 1.8, 3), 0),
        'halo_loc': (0, 0, key(t, [(0, 0), (L, 0.18), (T, 0.18), (T + .1, 0.0), (3.15, 0)])),
    })
    return ch

def processional_thrust(t):
    # 1.7 s: levels the glaive at his hip and draws the shoulder back; 1.7-2.2 the lunge down the lane (Unity moves
    # him), blade leading; then the blade is extracted and he resettles.
    T, L = 1.7, 2.2
    draw = key(t, [(0, 0), (1.0, 1.0), (T - 0.03, 1.0), (T + 0.08, 0), (4.4, 0)])
    lunge = key(t, [(0, 0), (T - 0.03, 0), (T + 0.1, 1.0), (L, 1.0), (L + 0.6, 0.7), (3.6, 0.3), (4.4, 0)])
    ch = base_pose(t)
    ch.update({
        'hips_loc': (0, 0.1 * draw - 0.15 * lunge, -0.18 * draw - 0.22 * lunge),
        'hips': (6 * lunge, 0, -10 * draw + 6 * lunge), 'spine': (8 * lunge - 3 * draw, 0, -10 * draw + 8 * lunge),
        'chest': add(ch['chest'], (10 * lunge - 4 * draw, 0, -16 * draw + 14 * lunge)),
        'head': (1.5 - 6 * lunge, 0, 10 * draw - 8 * lunge),
        'upperarm.R': (40 * draw - 75 * lunge, 10 * draw, 0), 'forearm.R': (-55 * draw - 10 * lunge, 0, 0),
        'glaive': (88 * max(draw, lunge), 0, -6 * draw),
        'upperarm.L': (-20 * draw + 25 * lunge, -10 * draw, 0), 'forearm.L': (-25 * draw, 0, 0),
        'halo_loc': (0, 0.15 * lunge, 0), 'halo': (-10 * lunge, 0, 0),
    })
    return ch

def crown_reversal(t):
    # he looks over his left shoulder and the halo fans out (winding); at 1.5 the arcs snap once and the glaive is
    # swung back through the rear sector; recovery to 3.3.
    T = 1.5
    look = key(t, [(0, 0), (0.5, 1.0), (T + 0.4, 1.0), (3.3, 0)])
    fan = key(t, [(0, 0), (T - 0.05, 1.0), (T + 0.05, -0.3), (T + 0.5, 0), (3.3, 0)])
    ch = base_pose(t)
    ch.update({
        'neck': (0, 0, 35 * look), 'head': (1.5, 0, 45 * look), 'chest': add(ch['chest'], (0, 0, 18 * look)),
        'spine': (0, 0, 8 * look),
        'halo.L': (0, 30 * fan + ring(t, T, 10, 2.0, 3), 0), 'halo.R': (0, -30 * fan - ring(t, T, 10, 2.0, 3), 0),
        'halo': (0, 0, -40 * fan),
        'upperarm.R': (key(t, [(0, 0), (T - .1, -15), (T + .08, 55), (T + .6, 40), (3.3, 0)]), key(t, [(0, 0), (T, 25), (3.3, 0)]), 0),
        'glaive': (key(t, [(0, 0), (T - .1, 20), (T + .08, -105), (T + .6, -90), (3.3, 0)]), 0, key(t, [(0, 0), (T + .08, 25), (3.3, 0)])),
    })
    return ch

def meridian_alignment(t):
    # 0.0-0.6 raise and plant the glaive butt-first; then he stays still while the rings charge: pulse I at 2.0 (a
    # small push, halo rings), pulse II at 3.8 (the chest throws open).
    P1, P2 = 2.0, 3.8
    lift = key(t, [(0, 0), (0.35, 1.0), (0.5, 0.0), (3.8, 0)])
    plant = snap(t, 0.52, 0.04, 0.4)
    ch = base_pose(t)
    ch.update({
        'upperarm.R': (-25 * lift, 8 * lift, 0), 'forearm.R': (-15 * lift, 0, 0),
        'hips_loc': (0, 0, -0.08 * plant - 0.06 * snap(t, P1, 0.08, 0.4) + key(t, [(0, 0), (P2 - 0.6, -0.12), (P2, -0.14), (P2 + 0.05, 0.02)])),
        'chest': add(ch['chest'], (-6 * snap(t, P1, 0.08, 0.5) + key(t, [(0, 0), (P2 - 0.6, 8), (P2 - 0.05, 10), (P2, -14)]), 0, 0)),
        'head': (1.5 - 10 * snap(t, P1, 0.08, 0.5) + key(t, [(0, 0), (P2 - 0.6, 8), (P2, -12)]), 0, 0),
        'upperarm.L': (key(t, [(0, 0), (1.0, -20), (P2 - 0.6, -10), (P2, -35)]), key(t, [(0, 0), (1.0, -35), (P2, -60)]), 0),
        'forearm.L': (key(t, [(0, 0), (1.0, -20), (P2, -5)]), 0, 0),
        'halo.L': (0, key(t, [(0, 0), (P1, 12), (P2, 24)]) + ring(t, P1, 6, 1.6, 2.5), 0),
        'halo.R': (0, key(t, [(0, 0), (P1, -12), (P2, -24)]) - ring(t, P1, 6, 1.6, 2.5), 0),
        'halo_loc': (0, 0.05 * t / P2, key(t, [(0, 0), (P1, 0.1), (P2, 0.25)])),
        'pendulum': (0, 16 * math.sin(2 * math.pi * t / 1.0) * (1 - min(1, t / P1)) + ring(t, P2, 30, 1.2, 1.5), 0),
    })
    return ch

def open_chest(t):
    # 2 s loop, the punish window: chest thrown open and up, arms wide and low, halo arcs spread, pendulum swinging hard
    p = 2 * math.pi * t / 2.0
    return {
        'hips_loc': (0, 0.04, -0.14 + 0.02 * math.sin(p)), 'hips': (-3, 0, 0),
        'spine': (-4, 0, 0), 'chest': (-12 + 1.5 * math.sin(p), 0, 0), 'neck': (-4, 0, 0), 'head': (-10, 0, 2 * math.sin(p)),
        'upperarm.L': (-30, -60, 0), 'forearm.L': (-5, 0, 0), 'upperarm.R': (-8, 10, 0),
        'pendulum': (0, 26 * math.sin(2 * math.pi * t / 1.0), 0),
        'halo.L': (0, 24 + 2 * math.sin(p), 0), 'halo.R': (0, -24 - 2 * math.sin(p + 1), 0),
        'halo_loc': (0, 0.05, 0.25 + 0.03 * math.sin(p)),
    }

def phase_shift(t):
    # 1.5 s, no damage: he gathers (arcs close, chest folds), then rings out with the arcs flung apart and settles
    T = 0.75
    g = key(t, [(0, 0), (T - 0.2, 1.0), (T, 1.0), (T + 0.1, -1.0), (T + 0.45, -0.7), (1.5, 0)])
    ch = base_pose(t)
    ch.update({
        'hips_loc': (0, 0, key(t, [(0, 0), (T - .2, -0.22), (T + .1, 0.04), (1.5, 0)])),
        'spine': (8 * g, 0, 0), 'chest': add(ch['chest'], (12 * g, 0, 0)), 'head': (1.5 + 10 * g, 0, 0),
        'upperarm.L': (-25 * max(g, 0) - 40 * max(-g, 0), -55 * max(-g, 0), 0), 'forearm.L': (-50 * max(g, 0), 0, 0),
        'halo.L': (0, -10 * max(g, 0) + 30 * max(-g, 0), 0), 'halo.R': (0, 10 * max(g, 0) - 30 * max(-g, 0), 0),
        'halo': (0, 0, key(t, [(0, 0), (T, -15), (1.5, 0)])),
        'halo_loc': (0, 0, key(t, [(0, 0), (T, -0.08), (T + .15, 0.28), (1.5, 0)])),
        'pendulum': (0, ring(t, T, 30, 1.5, 1.6), 0),
    })
    return ch

def defeat(t):
    # 2.5 s, held: he plants the glaive, sinks to his knees, the pendulum runs down and the halo falls inert
    plant = key(t, [(0, 0), (0.4, 1.0), (2.5, 1.0)]); kneel = key(t, [(0, 0), (0.5, 0), (1.6, 1.0), (2.5, 1.0)])
    slump = key(t, [(0, 0), (1.2, 0), (2.2, 1.0), (2.5, 1.0)])
    return {
        'hips_loc': (0, 0.08 + 0.15 * kneel, -0.85 * kneel), 'hips': (8 * kneel, 0, 0),
        'spine': (6 * kneel + 10 * slump, 0, 0), 'chest': (8 * kneel + 12 * slump, 0, 0), 'neck': (6 * slump, 0, 0),
        'head': (10 * kneel + 18 * slump, 0, 4 * slump),
        'upperarm.R': (-20 * plant, 10 * plant, 0), 'forearm.R': (-10 * plant, 0, 0), 'glaive': (-6 * plant * kneel, 0, 0),
        'upperarm.L': (10 * slump, 0, 0), 'forearm.L': (-15 * slump, 0, 0),
        'pendulum': (0, ring(t, 0.1, 22, 1.0, 1.2), 0),
        'halo.L': (0, -35 * slump, 0), 'halo.R': (0, 35 * slump, 0),
        'halo': (25 * slump, 0, 8 * slump), 'halo_loc': (0, 0.1 * slump, -0.45 * slump),
    }

acts = [bake_clip('Idle', 4.0, idle, loop=True), bake_clip('Walk', WALK_CYCLE, walk, loop=True),
        bake_clip('MeridianCut', 3.1, meridian_cut), bake_clip('ZenithPins', 3.15, zenith_pins),
        bake_clip('ProcessionalThrust', 4.4, processional_thrust), bake_clip('CrownReversal', 3.3, crown_reversal),
        bake_clip('MeridianAlignment', 3.8, meridian_alignment), bake_clip('OpenChest', 2.0, open_chest, loop=True),
        bake_clip('PhaseShift', 1.5, phase_shift), bake_clip('Defeat', 2.5, defeat)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'aurelion_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
