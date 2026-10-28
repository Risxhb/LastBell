# blender -b --factory-startup -P arcanist_addclips.py -- <Arcanist.glb> <out.json> [validate] [Clip ...]
# Adds the redesign kit clips (docs/design/arcanist-fx-prompts.md, A1-A10) to the shipped Arcanist without rebuilding it.
# The Meshy sources arcanist_rig.py was built from are gone, so this rebuilds its armature from the shipped GLB instead:
# the body bones come from arcanist_rig.py's hard-coded joint table B (checked against the GLB's rest nodes), the robe
# chain joints (CHAIN, originally measured from the mesh) are read back from the robe bones' rest heads, and the last
# joint of each chain is where robeNN_2's axis meets the hem height (CH_Z[3]). The pose toolkit below (R .. KIT, mirror,
# CLIP_EVENTS) is copied verbatim from arcanist_rig.py; the only change is RobeSim.step's optional 'flare' / 'lift' air.
# Instead of exporting through Blender's glTF exporter, each baked frame is captured straight into glTF node-local TRS
# (the exporter's convention: a bone's node is its pose relative to its parent bone, the root bone carries the Z-up ->
# Y-up turn), and written to <out.json> for arcanist_mergeclips.py, which appends them to the original GLB.
# 'validate' re-bakes the shipped Idle / Clockbolt / ArcLance / Convergence clips with this rebuilt rig and compares
# them with the GLB's own samples: that proves the rebuilt rig, the cloth sim and the node-frame conversion at once.
import bpy, sys, os, math, json, struct, numpy as np
from mathutils import Vector, Matrix, Euler, Quaternion
args = sys.argv[sys.argv.index('--') + 1:]
src_glb, out_json = args[0], args[1]
VALIDATE = 'validate' in args[2:]
ONLY = [a for a in args[2:] if a != 'validate']
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30

# ---------------- the shipped GLB ----------------
def read_glb(p):
    d = open(p, 'rb').read()
    jl = struct.unpack('<I', d[12:16])[0]; J = json.loads(d[20:20 + jl])
    bl = struct.unpack('<I', d[20 + jl:24 + jl])[0]; return J, d[28 + jl:28 + jl + bl]
GJ, GB = read_glb(src_glb)
NODES = GJ['nodes']; NID = {n['name']: i for i, n in enumerate(NODES)}
NPARENT = {c: i for i, n in enumerate(NODES) for c in n.get('children', [])}
JOINTS = [NODES[j]['name'] for j in GJ['skins'][0]['joints']]
def node_local(n):
    t = n.get('translation', [0, 0, 0]); r = n.get('rotation', [0, 0, 0, 1]); s = n.get('scale', [1, 1, 1])
    return Matrix.Translation(t) @ Quaternion((r[3], r[0], r[1], r[2])).to_matrix().to_4x4() @ Matrix.Diagonal((s[0], s[1], s[2], 1))
def node_global(i):
    m = node_local(NODES[i])
    while i in NPARENT: i = NPARENT[i]; m = node_local(NODES[i]) @ m
    return m
CONV = Matrix.Rotation(-math.pi / 2, 4, 'X')        # Blender armature space (Z up) -> glTF (Y up): the root bone's node
def rest_arm(name): return CONV.inverted() @ node_global(NID[name])   # a bone's rest frame in armature space

# ---------------- armature: arcanist_rig.py's joint table, robe chains read back from the GLB ----------------
LX = -0.01                         # the legs sit 1 cm right of the bounding-box centre
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.25, 0), None, False),
    'hips': ((0, -0.06, 0.95), (0, -0.06, 1.06), 'root', True),
    'spine': ((0, -0.06, 1.06), (0, -0.06, 1.2), 'hips', True),
    'chest': ((0, -0.06, 1.2), (0, -0.06, 1.42), 'spine', True),
    'neck': ((0, -0.06, 1.42), (0, -0.06, 1.5), 'chest', True),
    'head': ((0, -0.06, 1.5), (0, -0.05, 1.74), 'neck', True),
    'orbit': ((-0.5, 0.08, 1.58), (-0.5, 0.08, 1.76), 'root', False),
}
for s, n in ((1, 'L'), (-1, 'R')):
    B.update({
        f'upperarm.{n}': ((s * 0.17, -0.05, 1.42), (s * 0.27, -0.04, 1.22), 'chest', True),
        f'forearm.{n}': ((s * 0.27, -0.04, 1.22), (s * 0.31, -0.06, 1.02), f'upperarm.{n}', True),
        f'hand.{n}': ((s * 0.31, -0.06, 1.02), (s * 0.32, -0.07, 0.88), f'forearm.{n}', True),
        f'thigh.{n}': ((LX + s * 0.075, -0.06, 0.94), (LX + s * 0.058, -0.06, 0.48), 'hips', True),
        f'shin.{n}': ((LX + s * 0.058, -0.06, 0.48), (LX + s * 0.052, -0.02, 0.10), f'thigh.{n}', True),
        f'foot.{n}': ((LX + s * 0.052, -0.02, 0.10), (LX + s * 0.055, -0.2, 0.02), f'shin.{n}', True),
    })
NCH = 16; CH_STEP = 360 / NCH
CH_A = [((CH_STEP / 2 + CH_STEP * i + 180) % 360) - 180 for i in range(NCH)]
CH_Z = [0.93, 0.72, 0.52, 0.34]
PCY = -0.06
CHAIN = []
for i in range(NCH):
    pts = [tuple(rest_arm(f'robe{i:02d}_{k}').translation) for k in range(3)]
    M2 = rest_arm(f'robe{i:02d}_2'); h2 = M2.translation; y2 = M2.col[1].xyz.normalized()
    pts.append(tuple(h2 + y2 * ((CH_Z[3] - h2.z) / y2.z)))      # the hem joint: robeNN_2's axis at the hem height
    CHAIN.append(pts)
    for k in range(3):
        B[f'robe{i:02d}_{k}'] = (pts[k], pts[k + 1], 'hips' if k == 0 else f'robe{i:02d}_{k - 1}', True)
CLOTH = [n for n in B if n.startswith('robe')]
# the hard-coded body table must agree with the shipped rest pose (heads, and bone axes for the leaf bones' tails)
_eh = max((rest_arm(n).translation - Vector(B[n][0])).length for n in B)
_ea = max(math.degrees(rest_arm(n).col[1].xyz.normalized().angle((Vector(B[n][1]) - Vector(B[n][0])).normalized())) for n in B)
print('REBUILD heads max err %.2e m, bone axes max err %.4f deg' % (_eh, _ea))
assert _eh < 1e-4 and _ea < 0.05, 'joint table does not match the GLB'
ad = bpy.data.armatures.new('ArcanistRig'); arm = bpy.data.objects.new('ArcanistRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- copied verbatim from arcanist_rig.py (lines 229-605), except RobeSim.step's '(addclips)' lines ----------------
# ---------------- animation helpers ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
def Rzyx(x=0, y=0, z=0): return R(z=z) @ R(y=y) @ R(x=x)
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
HEAD = {n: Vector(B[n][0]) for n in B}; TAIL = {n: Vector(B[n][1]) for n in B}
SOLE = {n: [Vector((B[n][0][0], 0.06, 0.0)), Vector((B[n][1][0], -0.22, 0.0))] for n in ('foot.L', 'foot.R')}   # heel, toe
def delta(n): return pbs[n].matrix.to_3x3() @ pbs[n].bone.matrix_local.to_3x3().inverted()
def _frame(a, b):
    a = Vector(a).normalized(); b = Vector(b); b = (b - a * a.dot(b)).normalized()
    return Matrix((a, b, a.cross(b))).transposed()
UP0 = {n: (TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).normalized() for n in 'LR'}
FO0 = {n: (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).normalized() for n in 'LR'}
HA0 = {n: (TAIL[f'hand.{n}'] - HEAD[f'hand.{n}']).normalized() for n in 'LR'}
LEN = {n: ((TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).length, (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).length) for n in 'LR'}
def arm_dirs(n, up, fo, hd=None, palm=None):
    # chest-space rotations: upper arm along up, forearm along fo, hand (fingers) along hd with the palm facing palm
    Ru = UP0[n].rotation_difference(Vector(up).normalized()).to_matrix()
    Df = (Ru @ FO0[n]).rotation_difference(Vector(fo).normalized()).to_matrix() @ Ru
    out = {f'upperarm.{n}': {'rot': Ru}, f'forearm.{n}': {'rot': Ru.inverted() @ Df}}
    if hd is not None:
        Dh = (Df @ HA0[n]).rotation_difference(Vector(hd).normalized()).to_matrix() @ Df
        if palm is not None:
            # roll about the finger axis until the palm (rest palm faces the thigh: -s*X) points along palm
            s = 1 if n == 'L' else -1; f = (Dh @ HA0[n]).normalized(); p0 = Dh @ Vector((-s, 0, 0))
            p0 = (p0 - f * f.dot(p0)).normalized(); p1 = Vector(palm); p1 = (p1 - f * f.dot(p1))
            if p1.length > 1e-6: Dh = p0.rotation_difference(p1.normalized()).to_matrix() @ Dh
        out[f'hand.{n}'] = {'rot': Df.inverted() @ Dh}
    return out
def two_bone(n, target, pole):
    S = HEAD[f'upperarm.{n}']; L1, L2 = LEN[n]; d = Vector(target) - S; dist = min(d.length, L1 + L2 - 1e-3); dh = d.normalized()
    a = math.acos(max(-1, min(1, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist))))
    pp = Vector(pole) - dh * dh.dot(Vector(pole)); pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, -1))
    up = dh * math.cos(a) + pp * math.sin(a); elbow = S + up * L1
    return up, (S + dh * dist - elbow).normalized()
def slerpv(a, b, u):
    a, b = Vector(a).normalized(), Vector(b).normalized()
    return a.slerp(b, u, Vector((0, 0, 1))) if a.dot(b) > -0.999 else (a + (b - a) * u).normalized()
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value); direction tuples are slerped
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple): return slerpv(v0, v1, u)
            return v0 + (v1 - v0) * u
    v = pts[-1][1]
    return Vector(v).normalized() if isinstance(v, tuple) else v
def keyp(t, pts):  # like key() but tuples are points
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1: return Vector(v0).lerp(Vector(v1), smooth((t - t0) / max(1e-6, t1 - t0)))
    return Vector(pts[-1][1])
POINTS = {'rh', 'lh', 'orb'}
def leg(side, thigh, knee, foot=0.0, hip_pitch=0.0, flat=1.0, spread=0.0):
    n = 'L' if side > 0 else 'R'; f = -(hip_pitch + thigh + knee) * flat + foot
    return {f'thigh.{n}': {'rot': R(x=thigh) @ R(y=-side * spread)}, f'shin.{n}': {'rot': R(x=knee)}, f'foot.{n}': {'rot': R(x=f)}}
THIGH_LEN = (TAIL['thigh.L'] - HEAD['thigh.L']).length; SHIN_LEN = (TAIL['shin.L'] - HEAD['shin.L']).length
HIP_Z, ANKLE_Z = B['thigh.L'][0][2], B['shin.L'][1][2]
def ik_leg(side, ankle_y, hips_y, hips_z, hip_pitch=0.0, spread=2.0, foot=0.0):
    fwd = -(ankle_y - hips_y); down = HIP_Z + hips_z - ANKLE_Z
    d = min(math.hypot(fwd, down), THIGH_LEN + SHIN_LEN - 1e-3); a, b = THIGH_LEN, SHIN_LEN
    knee = 180 - math.degrees(math.acos((a * a + b * b - d * d) / (2 * a * b)))
    thigh = -(math.degrees(math.atan2(fwd, down) + math.acos((a * a + d * d - b * b) / (2 * a * d)))) - hip_pitch
    return leg(side, thigh, knee, foot=foot, hip_pitch=hip_pitch, spread=spread)

ORB0 = HEAD['orbit']
class RobeSim:
    # Verlet points on the CHAIN joints (NCH x 4). Joint 0 rides the hips; the rest feel reduced gravity, a shape spring
    # back to their rest place on the posed hips (stiffer near the belt), drag against the air (the character's speed:
    # clips are in place, so '_vel' tells it how fast he is really moving, '_flow' adds a gust), length constraints along
    # each chain, soft spacing to the neighbouring chains (keeps the hem together) and the leg capsules (legs push it).
    G = np.array([0, 0, -9.8]); K = np.array([0, 40.0, 12.0, 5.0]); DRAG = 1.5; DAMP = 0.04; SUB = 4; LIM = (75, 45, 40)
    def __init__(s):
        s.R = np.array(CHAIN)                                            # (NCH, 4, 3) rest joints
        s.L = np.linalg.norm(s.R[:, 1:] - s.R[:, :-1], axis=2)           # segment lengths
        s.N = np.linalg.norm(np.roll(s.R, -1, 0) - s.R, axis=2)          # spacing to the next chain
        s.P = None
    def reset(s): s.P = None
    def step(s, dt, vel=(0, 0, 0), gust=0.0, flare=0.0, lift=0.0):   # (addclips: + flare, lift)
        bpy.context.view_layer.update()
        M = np.array(pbs['hips'].matrix @ pbs['hips'].bone.matrix_local.inverted())
        T = s.R @ M[:3, :3].T + M[:3, 3]
        if s.P is None: s.P, s.Q = T.copy(), T.copy()
        air = -np.array(vel, float) + np.array([0, gust, 0])
        OUT = np.array([(math.sin(math.radians(a_)), -math.cos(math.radians(a_)), 0) for a_ in CH_A]) @ M[:3, :3].T
        # (addclips) extra air: 'flare' blows outward from the pelvis axis (negative gathers the robe in), 'lift' blows up.
        # Both default to 0, which leaves the original clips' simulation unchanged.
        if flare or lift: air = air + flare * OUT[:, None, :] + np.array([0, 0, lift])
        caps = [(np.array(pbs[f'{b}.{n}'].head), np.array(pbs[f'{b}.{n}'].tail), rad)
                for n in 'LR' for b, rad in (('thigh', 0.19), ('shin', 0.14))]
        h = dt / s.SUB
        for _ in range(s.SUB):
            v = (s.P - s.Q) / h
            acc = s.G + s.K[None, :, None] * (T - s.P) + s.DRAG * (air - v)
            mv = (s.P - s.Q) * (1 - s.DAMP) + acc * h * h
            n_ = np.linalg.norm(mv, axis=2, keepdims=True); mv *= np.minimum(1, 0.04 / np.maximum(n_, 1e-9))   # <= 4.8 m/s
            s.P, s.Q = s.P + mv, s.P
            for _ in range(3):
                s.P[:, 0] = T[:, 0]
                for k in range(3):
                    d = s.P[:, k + 1] - s.P[:, k]; d = d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-6)
                    # bend limit: a segment turns at most LIM[k] from the one above it (the first from its hips-carried rest)
                    ref = (T[:, 1] - T[:, 0]) if k == 0 else (s.P[:, k] - s.P[:, k - 1])
                    ref = ref / np.maximum(np.linalg.norm(ref, axis=1, keepdims=True), 1e-6)
                    cosl = math.cos(math.radians(s.LIM[k])); c = np.sum(d * ref, 1)
                    bad = c < cosl
                    if bad.any():
                        perp = d[bad] - ref[bad] * c[bad, None]; perp /= np.maximum(np.linalg.norm(perp, axis=1, keepdims=True), 1e-6)
                        d[bad] = ref[bad] * cosl + perp * math.sqrt(1 - cosl * cosl)
                    s.P[:, k + 1] = s.P[:, k] + d * s.L[:, k:k + 1]
                d = np.roll(s.P, -1, 0) - s.P; l = np.linalg.norm(d, axis=2, keepdims=True)
                tgt = np.clip(l, s.N[..., None] * 0.8, s.N[..., None] * 1.3)
                corr = d * (1 - tgt / np.maximum(l, 1e-6)) * 0.5; corr[:, 0] = 0
                s.P += corr; s.P -= np.roll(corr, 1, 0)
                for a, b, rad in caps:
                    ab = b - a; ab2 = ab @ ab
                    # each chain's own side of a leg: its outward direction (carried by the hips) tipped up, made
                    # perpendicular to the leg; a panel meeting a raised thigh goes over and in front of it, never under
                    side = OUT + np.array([0, 0, 0.6]); side = side - np.outer(side @ ab, ab) / ab2
                    side /= np.maximum(np.linalg.norm(side, axis=1, keepdims=True), 1e-6)
                    for k in range(1, 4):
                        # the joint, and the middle of the segment above it (moved by moving the joint)
                        for f_ in (1.0, 0.5):
                            q = s.P[:, k - 1] + (s.P[:, k] - s.P[:, k - 1]) * f_
                            u = np.clip(((q - a) @ ab) / ab2, 0, 1); c = a + u[:, None] * ab; d = q - c
                            l = np.linalg.norm(d, axis=1); hit = l < rad
                            if not hit.any(): continue
                            wrong = (np.sum(d * side, 1) < 0) | (l < rad * 0.5)
                            nrm = np.where(wrong[:, None], side, d / np.maximum(l, 1e-6)[:, None])
                            dist = np.where(wrong, rad + np.maximum(0, -np.sum(d * side, 1)), rad - l)
                            s.P[hit, k] += nrm[hit] * (dist[hit] / f_)[:, None]
        s.P[:, 0] = T[:, 0]
    def write(s):
        # bone rotations from the simulated joints (minimal swing from the hips-carried rest direction)
        bpy.context.view_layer.update(); Dh = delta('hips')
        for i in range(NCH):
            Dp = Dh
            for k in range(3):
                d0 = Vector(s.R[i, k + 1] - s.R[i, k]); d = Vector(s.P[i, k + 1] - s.P[i, k])
                Dw = (Dh @ d0).rotation_difference(d).to_matrix() @ Dh
                pb = pbs[f'robe{i:02d}_{k}']; M = pb.bone.matrix_local.to_3x3()
                pb.rotation_quaternion = (M.inverted() @ (Dp.inverted() @ Dw) @ M).to_quaternion(); Dp = Dw
ROBE = RobeSim()
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        if n.startswith('_') or n in CLOTH: continue
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
    if '_ground' in pose:
        bpy.context.view_layer.update()
        lowest = min((pbs[n].matrix @ pbs[n].bone.matrix_local.inverted() @ p).z for n, ps in SOLE.items() for p in ps)
        hb = pbs['hips']; M = hb.bone.matrix_local.to_3x3()
        hb.location = hb.location + M.inverted() @ Vector((0, 0, pose['_ground'] - lowest))
    if '_orb' in pose:
        # the astrolabe: '_orb' is its target in chest space (offset from its rest spot), carried by the chest's position
        # and heading only (it floats level), plus its own spin '_spin' (deg about Z) and tilt '_otilt'
        bpy.context.view_layer.update()
        C = pbs["chest"].matrix
        f = delta('chest') @ Vector((0, -1, 0)); yaw = math.atan2(f.x, -f.y) * pose.get('_oyaw', 1.0)     # heading of the chest's forward ('_oyaw' 0: ignore the twist)
        Rz = Matrix.Rotation(yaw, 3, 'Z'); c0 = HEAD['chest']; c1 = C.translation
        target = c1 + Rz @ (ORB0 + Vector(pose['_orb']) - c0)
        pb = pbs['orbit']; M = pb.bone.matrix_local.to_3x3()
        pb.location = M.inverted() @ (target - ORB0)
        pb.rotation_quaternion = (M.inverted() @ (Rz @ R(x=pose.get('_otilt', 0.0)) @ R(z=pose.get('_spin', 0.0))) @ M).to_quaternion()
def bake(name, frames, fn, loop):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    def sim(pose):
        set_pose(pose); ROBE.step(1 / 30, pose.get('_vel', (0, 0, 0)), pose.get('_flow', 0.0) * 0.06)
    ROBE.reset()
    for f in (range(2 * frames) if loop else [0] * 25): sim(fn(f % frames, (f % frames) / frames))
    # simulate the keyed frames, then clean the cloth up: loops have their drift over the cycle removed so the last
    # frame meets the first, and every joint track gets two passes of a 1-2-1 filter (cyclic for loops)
    n = frames + (1 if loop else 0); poses, Ps = [], []
    for f in range(n):
        p = fn(f, f / frames); set_pose(p); ROBE.step(1 / 30, p.get('_vel', (0, 0, 0)), p.get('_flow', 0.0) * 0.06)
        poses.append(p); Ps.append(ROBE.P.copy())
    Ps = np.array(Ps)
    if loop:
        Ps -= (Ps[-1] - Ps[0])[None] * (np.arange(n) / (n - 1))[:, None, None, None]
        C = Ps[:-1]
        for _ in range(2): C = (np.roll(C, 1, 0) + 2 * C + np.roll(C, -1, 0)) / 4
        Ps = np.concatenate([C, C[:1]])
    else:
        for _ in range(2): Ps[1:-1] = (Ps[:-2] + 2 * Ps[1:-1] + Ps[2:]) / 4
    for f in range(n):
        set_pose(poses[f]); ROBE.P = Ps[f]; ROBE.write()
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + (1 if loop else 0))
    return act

# ---------------- stance and the channel-table poser ----------------
# Arm channels are chest-space directions: ru/rf/rd right upper arm / forearm / fingers, rp right palm facing;
# lu/lf/ld/lp the same on the left. rh / lh: wrist points (two-bone IK, with rpole / lpole).
# Body: tw twist (+ toward his left), lean, drop (m), hy hips fwd/back (m, - fwd), front right-foot step (m, + fwd),
# back left-foot step (m, + back), spread, flow/flare robe, hz extra hip height (m, jumps), nod, chest, roll.
# Astrolabe: orb (offset from its rest spot behind the right shoulder, chest space, m), spin (deg), otilt (deg).
STANCE = {
    'ru': (-0.35, 0.05, -0.94), 'rf': (-0.2, -0.35, -0.92), 'rd': (-0.1, -0.3, -0.95), 'rp': (0.8, 0.2, 0.0),
    'lu': (0.35, 0.05, -0.94), 'lf': (0.2, -0.35, -0.92), 'ld': (0.1, -0.3, -0.95), 'lp': (-0.8, 0.2, 0.0),
}
def arms_from(g):
    out = {}
    for n in 'RL':
        k = n.lower(); up, fo = g(k + 'u'), g(k + 'f')
        h = g(k + 'h', False)
        if h is not False: up, fo = two_bone(n, h, g(k + 'pole', (-0.7 if n == 'R' else 0.7, 0.3, -0.6)))
        out.update(arm_dirs(n, up, fo, g(k + 'd'), g(k + 'p')))
    return out
STANCE_HZ = 0.0; STANCE_ANKLE_Y = None
def base_pose(t, K):
    def g(n, d=None):
        if n in K: return keyp(t, K[n]) if n in POINTS else key(t, K[n])
        if d is not None: return d
        v = STANCE.get(n)
        if isinstance(v, tuple): return Vector(v).normalized()
        return v if v is not None else 0.0
    tw, lean, drop, hy = g('tw', 0.0), g('lean', 0.0), g('drop', 0.0), g('hy', 0.0)
    hz = STANCE_HZ - drop + g('hz', 0.0); hp = lean * 0.3
    s = {
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(x=hp, y=g('roll', 0.0), z=tw * 0.4)},
        'spine': {'rot': Rzyx(x=2 + lean * 0.7, z=tw * 0.3)}, 'chest': {'rot': Rzyx(x=g('chest', 0.0), z=tw * 0.4)},
        'head': {'rot': Rzyx(x=-lean * 0.6 + g('nod', 0.0), z=-tw * 0.6)},
        '_flow': g('flow', 0.0), '_flare': g('flare', 0.0),
        '_orb': tuple(g('orb', (0, 0, 0))), '_spin': g('spin', 0.0), '_otilt': g('otilt', 0.0),
    }
    s.update(arms_from(g))
    sp = 2 + g('spread', 0.0)
    if STANCE_ANKLE_Y:
        s.update(ik_leg(-1, STANCE_ANKLE_Y['R'] - g('front', 0.0), hy, hz, hip_pitch=hp, spread=sp, foot=g('toeR', 0.0)))
        s.update(ik_leg(1, STANCE_ANKLE_Y['L'] + g('back', 0.0), hy, hz, hip_pitch=hp, spread=sp, foot=g('toeL', 0.0)))
    return s
_s = {'_ground': 0.0}; _s.update(leg(-1, -4, 10, spread=2)); _s.update(leg(1, -2, 8, spread=2)); set_pose(_s)
bpy.context.view_layer.update(); _hb = pbs['hips']; STANCE_HZ = (_hb.bone.matrix_local.to_3x3() @ _hb.location).z
STANCE_ANKLE_Y = {s: pbs[f'foot.{s}'].head.y for s in ('L', 'R')}
print('STANCE', round(STANCE_HZ, 3), {k: round(v, 3) for k, v in STANCE_ANKLE_Y.items()})
def kit_pose(K): return lambda f, t: base_pose(t, K)

def idle(f, t):
    p = 2 * math.pi * t; b = math.sin(p)
    K = {'chest': [(0, 1.5 * b)] * 2, 'tw': [(0, -3 + 3 * math.sin(p + 0.5))] * 2, 'roll': [(0, 1.2 * b)] * 2,
         'lu': [(0, (0.35, 0.05 + 0.03 * math.sin(p + 0.6), -0.94))] * 2, 'ru': [(0, (-0.35, 0.05 + 0.03 * math.sin(p + 0.9), -0.94))] * 2,
         'drop': [(0, 0.01 * (1 - math.cos(p)))] * 2, 'nod': [(0, -1.5 * b)] * 2,
         'orb': [(0, (0, 0, 0.03 * math.sin(p)))] * 2, 'spin': [(0, 360 * t)] * 2, 'otilt': [(0, 4 * math.sin(p + 1))] * 2}
    return base_pose(0, K)

# ---------------- the Arcanist kit (handoff/arcanist-animation-20261021). PF-05 asks for fast chaining: short anticipation,
# the hit lands early, recovery is cancellable (CLIP_EVENTS). Distinct top-down silhouettes:
#   Clockbolt: one-hand palm flick (alternating hands).   Time Seal: off hand traces a dial, then stamps it forward.
#   Arc Lance: hands gather at the chest with the astrolabe in front as a lens, then both palms thrust (brief root).
#   Convergence: hand raised with the astrolabe overhead, then slammed down and forward.
#   Temporal Anchor: crouch, palm to the floor (place); arms snap across the chest, astrolabe whirls (return).
#   Clockstep (Dodge): low forward glide, arms swept back.
# Astrolabe 'orb' offsets are from its rest spot beside the right shoulder (chest space, m).
# The arms are short (shoulder to wrist 0.42 m): wrist targets 'rh' / 'lh' (chest-space points, two-bone IK) stay inside
# that reach so the elbows read straight without popping.
ORB_LENS = (0.5, -0.64, -0.14)        # in front of the thrust palms
ORB_HIGH = (0.17, -0.15, 0.62)        # above the raised right hand
ORB_SLAM = (0.35, -0.7, -0.22)        # hovering in front of the slammed hand
ORB_UP = (0.4, 0.12, 0.3)             # above and behind the head (out of the way of a floor touch)
PALM_OUT_R = dict(rd=(-0.1, -0.4, 0.9), rp=(0, -1, 0.15))      # fingers up, palm toward the target
REST_RH, REST_LH = (-0.3, -0.12, 1.05), (0.3, -0.12, 1.05)    # wrists near the stance pose, as IK targets
def dial(t0, t1, c, r, n=10):
    # wrist points tracing a circle in the chest's XZ plane from t0 to t1
    return [(t0 + (t1 - t0) * k / n, (c[0] + r * math.cos(2 * math.pi * k / n), c[1], c[2] + r * math.sin(2 * math.pi * k / n))) for k in range(n + 1)]
KIT = {
    # 0.33 s: cock back by the ear (.15), flick out wide and forward with the chest turned in; bolt at .35
    'Clockbolt': (10, {
        'rh': [(0, REST_RH), (0.15, (-0.27, 0.0, 1.5)), (0.35, (-0.33, -0.33, 1.42)), (0.55, (-0.33, -0.33, 1.41)), (1, REST_RH)],
        'rpole': [(0, (-0.6, 0.4, -0.6))] * 2,
        'rd': [(0, STANCE['rd']), (0.15, (0, 0.2, 0.98)), (0.35, PALM_OUT_R['rd']), (0.55, PALM_OUT_R['rd']), (1, STANCE['rd'])],
        'rp': [(0, STANCE['rp']), (0.15, (0.3, -0.9, 0.2)), (0.35, PALM_OUT_R['rp']), (0.55, PALM_OUT_R['rp']), (1, STANCE['rp'])],
        'tw': [(0, 0), (0.15, -8), (0.35, 23), (0.55, 22), (1, 0)], 'lean': [(0, 0), (0.15, -2), (0.35, 6), (1, 0)],
        'front': [(0, 0), (0.35, 0.08), (0.6, 0.08), (1, 0)],
        'lu': [(0, STANCE['lu']), (0.35, (0.4, 0.3, -0.87)), (1, STANCE['lu'])],
        'orb': [(0, (0, 0, 0)), (0.35, (0.05, -0.08, -0.03)), (1, (0, 0, 0))], 'spin': [(0, 0), (0.35, 40), (1, 60)]}),
    # 0.53 s: off hand traces a 12 cm dial in front of the left shoulder (.12-.42), pulls back (.5), punches it out (.58)
    'TimeSeal': (16, {
        'lh': [(0, REST_LH)] + dial(0.12, 0.42, (0.22, -0.4, 1.32), 0.15) + [(0.5, (0.2, -0.18, 1.36)), (0.58, (0.16, -0.46, 1.32)), (0.74, (0.16, -0.46, 1.31)), (1, REST_LH)],
        'lpole': [(0, (0.7, 0.2, -0.7))] * 2,
        'ld': [(0, STANCE['ld']), (0.12, (0.1, -0.3, 0.95)), (0.27, (0.75, -0.3, 0.6)), (0.42, (0.1, -0.3, 0.95)), (0.58, (0, -0.35, 0.94)), (0.74, (0, -0.35, 0.94)), (1, STANCE['ld'])],
        'lp': [(0, STANCE['lp']), (0.12, (0, -1, 0.1)), (0.74, (0, -1, 0.1)), (1, STANCE['lp'])],
        'ru': [(0, STANCE['ru']), (0.58, (-0.45, 0.3, -0.84)), (1, STANCE['ru'])],
        'tw': [(0, 0), (0.12, -8), (0.5, -4), (0.58, -18), (0.74, -17), (1, 0)], 'lean': [(0, 0), (0.5, -3), (0.58, 10), (0.74, 9), (1, 0)],
        'back': [(0, 0), (0.58, 0.12), (0.8, 0.12), (1, 0)], 'front': [(0, 0), (0.5, 0), (0.58, 0.12), (0.8, 0.12), (1, 0)], 'hy': [(0, 0), (0.58, -0.06), (1, 0)], 'drop': [(0, 0), (0.58, 0.04), (1, 0)],
        'orb': [(0, (0, 0, 0)), (0.58, (0.1, -0.1, 0.05)), (1, (0, 0, 0))], 'spin': [(0, 0), (0.12, 0), (0.42, -360), (0.58, -330), (1, -330)]}),
    # 0.67 s: gather at the chest (.3, palms facing), astrolabe swings out front as a lens; both arms lock out at .45
    'ArcLance': (20, {
        'rh': [(0, REST_RH), (0.3, (-0.07, -0.3, 1.22)), (0.45, (-0.11, -0.42, 1.46)), (0.62, (-0.11, -0.42, 1.46)), (1, REST_RH)],
        'lh': [(0, REST_LH), (0.3, (0.07, -0.3, 1.22)), (0.45, (0.11, -0.42, 1.46)), (0.62, (0.11, -0.42, 1.46)), (1, REST_LH)],
        'rpole': [(0, (-0.8, 0.2, -0.5))] * 2, 'lpole': [(0, (0.8, 0.2, -0.5))] * 2,
        'rd': [(0, STANCE['rd']), (0.3, (0.6, -0.4, 0.7)), (0.45, PALM_OUT_R['rd']), (0.62, PALM_OUT_R['rd']), (1, STANCE['rd'])],
        'rp': [(0, STANCE['rp']), (0.3, (0.9, -0.3, 0)), (0.45, PALM_OUT_R['rp']), (0.62, PALM_OUT_R['rp']), (1, STANCE['rp'])],
        'ld': [(0, STANCE['ld']), (0.3, (-0.6, -0.4, 0.7)), (0.45, (0.1, -0.4, 0.9)), (0.62, (0.1, -0.4, 0.9)), (1, STANCE['ld'])],
        'lp': [(0, STANCE['lp']), (0.3, (-0.9, -0.3, 0)), (0.45, (0, -1, 0.15)), (0.62, (0, -1, 0.15)), (1, STANCE['lp'])],
        'drop': [(0, 0), (0.3, 0.07), (0.45, 0.08), (0.62, 0.08), (1, 0)], 'lean': [(0, 0), (0.3, -4), (0.45, 9), (0.62, 8), (1, 0)],
        'front': [(0, 0), (0.3, 0.1), (0.8, 0.1), (1, 0)], 'back': [(0, 0), (1, 0)],
        'hy': [(0, 0), (0.45, -0.03), (0.8, -0.03), (1, 0)], 'spread': [(0, 0), (0.3, 4), (0.8, 4), (1, 0)],
        'flow': [(0, 0), (0.45, 10), (1, 0)], 'nod': [(0, 0), (0.3, 6), (0.45, -2), (1, 0)],
        'orb': [(0, (0, 0, 0)), (0.3, ORB_LENS), (0.62, ORB_LENS), (1, (0, 0, 0))],
        'otilt': [(0, 0), (0.3, 90), (0.62, 90), (1, 0)], 'spin': [(0, 0), (0.3, 180), (0.45, 540), (0.62, 640), (1, 720)]}),
    # 0.73 s: right hand raised out to his right (.35), astrolabe climbs above it and spins up; slam at .55 into a deep
    # crouch, hand ending at waist height out front with the astrolabe hovering just past it; held to .7
    'Convergence': (22, {
        'ru': [(0, STANCE['ru']), (0.35, (-0.5, -0.1, 0.86)), (0.45, (-0.45, -0.18, 0.87)), (0.55, (-0.25, -0.85, -0.45)), (0.7, (-0.25, -0.85, -0.47)), (1, STANCE['ru'])],
        'rf': [(0, STANCE['rf']), (0.35, (-0.3, -0.15, 0.94)), (0.45, (-0.25, -0.25, 0.94)), (0.55, (-0.05, -0.95, -0.3)), (0.7, (-0.05, -0.95, -0.32)), (1, STANCE['rf'])],
        'rd': [(0, STANCE['rd']), (0.35, (-0.1, -0.1, 0.99)), (0.55, (0, -0.85, -0.5)), (0.7, (0, -0.85, -0.5)), (1, STANCE['rd'])],
        'rp': [(0, STANCE['rp']), (0.35, (0.2, -0.9, 0.2)), (0.55, (0, -0.3, -0.95)), (0.7, (0, -0.3, -0.95)), (1, STANCE['rp'])],
        'lu': [(0, STANCE['lu']), (0.35, (0.85, 0.1, -0.5)), (0.55, (0.7, 0.55, -0.45)), (0.7, (0.7, 0.55, -0.45)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.35, (0.9, -0.3, -0.3)), (0.55, (0.6, 0.6, -0.5)), (0.7, (0.6, 0.6, -0.5)), (1, STANCE['lf'])],
        'lp': [(0, STANCE['lp']), (0.35, (0, 0, -1)), (1, STANCE['lp'])],
        'lean': [(0, 0), (0.35, -8), (0.45, -9), (0.55, 27), (0.7, 26), (1, 0)], 'tw': [(0, 0), (0.35, 10), (0.55, -10), (0.7, -9), (1, 0)],
        'drop': [(0, 0), (0.2, 0.03), (0.35, -0.02), (0.45, -0.02), (0.55, 0.25), (0.7, 0.24), (1, 0)],
        'front': [(0, 0), (0.45, 0), (0.55, 0.25), (0.85, 0.25), (1, 0)], 'back': [(0, 0), (0.55, 0.12), (0.85, 0.12), (1, 0)],
        'hy': [(0, 0), (0.55, -0.07), (0.85, -0.06), (1, 0)], 'spread': [(0, 0), (0.55, 6), (0.85, 6), (1, 0)],
        'nod': [(0, 0), (0.4, -14), (0.55, 8), (1, 0)], 'flare': [(0, 0), (0.5, 0), (0.58, 10), (0.85, 4), (1, 0)], 'flow': [(0, 0), (0.55, 8), (1, 0)],
        'orb': [(0, (0, 0, 0)), (0.35, ORB_HIGH), (0.47, ORB_HIGH), (0.55, ORB_SLAM), (0.7, ORB_SLAM), (1, (0, 0, 0))],
        'otilt': [(0, 0), (0.35, 0), (0.55, 90), (0.7, 90), (1, 0)], 'spin': [(0, 0), (0.35, 360), (0.47, 900), (0.55, 1080), (1, 1260)]}),
    # 0.5 s: drop into a kneel and press the palm to the floor ahead (anchor set at .5), astrolabe up and back; rise
    'TemporalAnchor': (15, {
        'ru': [(0, STANCE['ru']), (0.5, (-0.15, -0.5, -0.85)), (0.65, (-0.15, -0.5, -0.85)), (1, STANCE['ru'])],
        'rf': [(0, STANCE['rf']), (0.5, (-0.05, -0.55, -0.83)), (0.65, (-0.05, -0.55, -0.83)), (1, STANCE['rf'])],
        'rd': [(0, STANCE['rd']), (0.5, (0, -0.75, -0.66)), (0.65, (0, -0.75, -0.66)), (1, STANCE['rd'])],
        'rp': [(0, STANCE['rp']), (0.5, (0, 0.6, -0.8)), (0.65, (0, 0.6, -0.8)), (1, STANCE['rp'])],
        'lu': [(0, STANCE['lu']), (0.5, (0.75, 0.3, -0.6)), (1, STANCE['lu'])], 'lf': [(0, STANCE['lf']), (0.5, (0.85, 0.1, -0.5)), (1, STANCE['lf'])],
        'drop': [(0, 0), (0.5, 0.42), (0.65, 0.42), (1, 0)], 'lean': [(0, 0), (0.5, 34), (0.65, 33), (1, 0)], 'nod': [(0, 0), (0.5, 14), (1, 0)],
        'front': [(0, 0), (0.5, 0.26), (0.75, 0.26), (1, 0)], 'back': [(0, 0), (0.5, 0.22), (0.75, 0.22), (1, 0)], 'hy': [(0, 0), (0.5, -0.06), (1, 0)],
        'spread': [(0, 0), (0.5, 5), (0.75, 5), (1, 0)], 'flare': [(0, 0), (0.5, 6), (1, 0)],
        'orb': [(0, (0, 0, 0)), (0.5, ORB_UP), (0.65, ORB_UP), (1, (0, 0, 0))],
        'spin': [(0, 0), (0.5, 180), (1, 240)]}),
    # 0.33 s recall: a ward, wrists crossed in front of the sternum (right in front), fingers up, elbows tucked, chin down
    # (teleport at .3); fling both arms open .75
    'AnchorReturn': (10, {
        'rh': [(0, REST_RH), (0.3, (0.04, -0.33, 1.3)), (0.45, (0.04, -0.33, 1.3)), (0.75, (-0.5, -0.14, 1.2)), (1, REST_RH)],
        'lh': [(0, REST_LH), (0.3, (-0.04, -0.25, 1.31)), (0.45, (-0.04, -0.25, 1.31)), (0.75, (0.5, -0.14, 1.2)), (1, REST_LH)],
        'rpole': [(0, (-0.45, 0.2, -0.87))] * 2, 'lpole': [(0, (0.45, 0.2, -0.87))] * 2,
        'rd': [(0, STANCE['rd']), (0.3, (0.3, -0.15, 0.94)), (0.45, (0.3, -0.15, 0.94)), (0.75, (-0.75, -0.25, -0.6)), (1, STANCE['rd'])],
        'rp': [(0, STANCE['rp']), (0.3, (0, 1, 0)), (0.45, (0, 1, 0)), (0.75, (-0.2, -0.95, 0.2)), (1, STANCE['rp'])],
        'ld': [(0, STANCE['ld']), (0.3, (-0.3, -0.15, 0.94)), (0.45, (-0.3, -0.15, 0.94)), (0.75, (0.75, -0.25, -0.6)), (1, STANCE['ld'])],
        'lp': [(0, STANCE['lp']), (0.3, (0, 1, 0)), (0.45, (0, 1, 0)), (0.75, (0.2, -0.95, 0.2)), (1, STANCE['lp'])],
        'drop': [(0, 0), (0.3, 0.06), (0.75, -0.01), (1, 0)], 'lean': [(0, 0), (0.3, 8), (0.75, -15), (1, 0)], 'nod': [(0, 0), (0.3, 14), (0.75, -6), (1, 0)],
        'chest': [(0, 0), (0.3, 6), (0.75, -6), (1, 0)], 'flare': [(0, 0), (0.3, 0), (0.75, 10), (1, 0)],
        'orb': [(0, (0, 0, 0)), (0.3, (0.15, 0.05, 0.05)), (1, (0, 0, 0))], 'spin': [(0, 0), (0.75, 720), (1, 760)], 'otilt': [(0, 0), (0.3, 30), (1, 0)]}),
}
def mirror(K):
    # left-handed version of a right-handed clip: swap the arm channels and reflect them (and twist / orb) across X
    flip = lambda v: (-v[0], v[1], v[2]) if isinstance(v, tuple) else -v
    out = {}
    for k, pts in K.items():
        nk = {'r': 'l', 'l': 'r'}.get(k[0], k[0]) + k[1:] if k[:2] in ('ru', 'rf', 'rd', 'rp', 'rh', 'lu', 'lf', 'ld', 'lp', 'lh') else k
        if k[:2] in ('ru', 'rf', 'rd', 'rp', 'rh', 'lu', 'lf', 'ld', 'lp', 'lh') or k == 'tw': out[nk] = [(t, flip(v)) for t, v in pts]
        else: out[nk] = pts
    return out
KIT['Clockbolt2'] = (KIT['Clockbolt'][0], mirror(KIT['Clockbolt'][1]))
CLIP_EVENTS = {  # integration markers (normalised clip time): release / contact, cancel_before (dash may cancel up to here
    # without spending), recover (from here any action may cancel the rest)
    'Clockbolt': {'release': 0.35, 'recover': 0.55}, 'Clockbolt2': {'release': 0.35, 'recover': 0.55},
    'TimeSeal': {'apply': 0.58, 'recover': 0.74},
    'ArcLance': {'cast_start': 0.0, 'release': 0.45, 'cancel_before': 0.45, 'recover': 0.62},
    'Convergence': {'detonate': 0.55, 'recover': 0.7},
    'TemporalAnchor': {'place': 0.5, 'recover': 0.65}, 'AnchorReturn': {'teleport': 0.3, 'shield': 0.3, 'recover': 0.75},
    'Dodge': {'move_start': 0.1, 'move_end': 0.7}, 'Jump': {'takeoff': 0.22, 'land': 0.74},
}
def dodge(f, t):
    # Clockstep: dip, low lean-forward glide with both arms swept back, robe streaming, recover by .85
    d = key(t, [(0, 0), (0.1, 0.6), (0.25, 1), (0.7, 1), (0.85, 0.2), (1, 0)])
    K = {'lean': [(0, 38 * d)] * 2, 'drop': [(0, 0.2 * d)] * 2, 'hy': [(0, -0.1 * d)] * 2, 'tw': [(0, -6 * d)] * 2,
         'front': [(0, 0.3 * d)] * 2, 'back': [(0, 0.3 * d)] * 2,
         'ru': [(0, tuple(slerpv(STANCE['ru'], (-0.2, 0.35, -0.92), d)))] * 2, 'rf': [(0, tuple(slerpv(STANCE['rf'], (-0.1, 0.5, -0.86), d)))] * 2,
         'lu': [(0, tuple(slerpv(STANCE['lu'], (0.2, 0.35, -0.92), d)))] * 2, 'lf': [(0, tuple(slerpv(STANCE['lf'], (0.1, 0.5, -0.86), d)))] * 2,
         'rd': [(0, tuple(slerpv(STANCE['rd'], (-0.3, 0.85, -0.4), d)))] * 2, 'ld': [(0, tuple(slerpv(STANCE['ld'], (0.3, 0.85, -0.4), d)))] * 2,
         'flow': [(0, 28 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2, 'flare': [(0, 4 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2,
         'orb': [(0, (0.05, 0.25 * key(t, [(0, 0), (0.4, 1), (1, 0)]), -0.05 * d))] * 2, 'spin': [(0, 300 * t)] * 2, 'otilt': [(0, -20 * d)] * 2}
    s = base_pose(0, K)
    s['_ground'] = 0.03 * key(t, [(0, 0), (0.2, 0), (0.4, 1), (0.6, 0), (1, 0)])   # a skim, not a hop
    s['_vel'] = (0, -min(10.0, 17.7 * math.sin(math.pi * t)), 0)        # the 4.5 m Clockstep burst (capped for the cloth)
    return s

# ================================ arcanist_addclips.py ================================
# ---------------- capture: the posed armature as glTF node-local TRS ----------------
def capture():
    bpy.context.view_layer.update(); out = {}
    for n in JOINTS:
        pb = pbs[n]; L = (pb.parent.matrix.inverted() @ pb.matrix) if pb.parent else CONV @ pb.matrix
        t, q, _ = L.decompose(); out[n] = (tuple(t), (q.x, q.y, q.z, q.w))
    return out
def hand_frame(n):
    # posed hand.<n>: wrist (bone head), finger axis, palm normal (rest palm faces the thigh, -s*X; see arm_dirs)
    pb = pbs[f'hand.{n}']; M = pb.matrix; s = 1 if n == 'L' else -1
    return M.translation.copy(), M.col[1].xyz.normalized(), (delta(f'hand.{n}') @ Vector((-s, 0, 0))).normalized()
def set_pose2(pose):
    set_pose(pose)
    lens, spy = pose.get('_lens', 0.0), pose.get('_spinY', 0.0)
    if lens > 0 or spy:
        # Disintegrate: the astrolabe (its main ring faces forward at rest) turns like a wheel about its forward axis
        # ('_spinY', deg) and hangs '_lensd' m in front of the right palm, level with it, along the chest's heading;
        # '_lens' blends it there from where '_orb' put it
        bpy.context.view_layer.update()
        f = delta('chest') @ Vector((0, -1, 0)); Rz = Matrix.Rotation(math.atan2(f.x, -f.y) * pose.get('_oyaw', 1.0), 3, 'Z')
        pb = pbs['orbit']; M = pb.bone.matrix_local.to_3x3()
        pb.rotation_quaternion = (M.inverted() @ (Rz @ R(x=pose.get('_otilt', 0.0)) @ R(y=spy) @ R(z=pose.get('_spin', 0.0))) @ M).to_quaternion()
        if lens > 0:
            wr, fg, pn = hand_frame('R')
            target = wr + fg * 0.07 + Rz @ Vector((0, -1, 0)) * pose.get('_lensd', 0.4)
            cur = pb.matrix.translation.copy()
            pb.location = M.inverted() @ (cur.lerp(target, lens) - ORB0)
DIAG = []
def sole(pb):   # posed heel point of a foot (SOLE[0]), armature space
    return tuple(pb.matrix @ pb.bone.matrix_local.inverted() @ SOLE[pb.name][0])
def sim_step(p):
    ROBE.step(1 / 30, p.get('_vel', (0, 0, 0)), p.get('_flow', 0.0) * 0.06, p.get('_rflare', 0.0), p.get('_rlift', 0.0))
def bake2(name, frames, fn, loop, legacy=False):
    # bake() from arcanist_rig.py (same cloth pre-roll, loop drift removal and 1-2-1 smoothing), capturing node TRS
    # instead of keyframes. legacy: the original sampling (t = f / frames, a non-loop clip's last frame stops short of
    # t = 1). New clips: non-loop frames run t = 0 .. 1 inclusive, so the last frame is the authored end (held) pose and
    # normalised clip time equals t; loops run t = 0 .. 1 with the closing frame equal to the first.
    tt = (lambda f: f / frames) if (legacy or loop) else (lambda f: f / (frames - 1))
    def sim(pose): set_pose2(pose); sim_step(pose)
    ROBE.reset()
    for f in (range(2 * frames) if loop else [0] * 25): sim(fn(f % frames, (f % frames) / frames))
    n = frames + (1 if loop else 0); poses, Ps = [], []
    for f in range(n):
        p = fn(f, tt(f)); set_pose2(p); sim_step(p)
        poses.append(p); Ps.append(ROBE.P.copy())
    Ps = np.array(Ps)
    if loop:
        Ps -= (Ps[-1] - Ps[0])[None] * (np.arange(n) / (n - 1))[:, None, None, None]
        C = Ps[:-1]
        for _ in range(2): C = (np.roll(C, 1, 0) + 2 * C + np.roll(C, -1, 0)) / 4
        Ps = np.concatenate([C, C[:1]])
    else:
        for _ in range(2): Ps[1:-1] = (Ps[:-2] + 2 * Ps[1:-1] + Ps[2:]) / 4
    caps = []; DIAG.clear()
    for f in range(n):
        set_pose2(poses[f]); ROBE.P = Ps[f]; ROBE.write(); caps.append(capture())
        DIAG.append([sole(pbs['foot.L']), sole(pbs['foot.R']), tuple(pbs['hips'].head)])
    return caps

# ---------------- validation against the shipped clips ----------------
CT = {5126: np.float32}
def accessor(i):
    a = GJ['accessors'][i]; bv = GJ['bufferViews'][a['bufferView']]; nc = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[a['type']]
    return np.frombuffer(GB, np.float32, a['count'] * nc, bv.get('byteOffset', 0) + a.get('byteOffset', 0)).reshape(a['count'], nc)
def glb_clip(name):
    A = next(a for a in GJ['animations'] if a['name'] == name); out = {}
    for c in A['channels']:
        s = A['samplers'][c['sampler']]
        out[(NODES[c['target']['node']]['name'], c['target']['path'])] = (accessor(s['input'])[:, 0], accessor(s['output']))
    return out
def sample(times, vals, t):
    if t <= times[0]: return vals[0]
    if t >= times[-1]: return vals[-1]
    j = int(np.searchsorted(times, t)); u = (t - times[j - 1]) / (times[j] - times[j - 1])
    return vals[j - 1] * (1 - u) + vals[j] * u
def qang(a, b):
    # angle between two rotations (deg) from the quaternion chord: well conditioned near zero, unlike acos(dot)
    a, b = np.array(a, float), np.array(b, float); a /= np.linalg.norm(a); b /= np.linalg.norm(b)
    if np.dot(a, b) < 0: b = -b
    return math.degrees(4 * math.asin(min(1.0, np.linalg.norm(a - b) / 2)))
def compare(name, caps):
    ref = glb_clip(name); worst_r = {}; worst_t = {}
    for f, cap in enumerate(caps):
        t = (f + 1) / 30                          # the exporter keyed frame f + 1
        for n, (tr, q) in cap.items():
            rq = sample(*ref[(n, 'rotation')], t); rq = rq / np.linalg.norm(rq)
            ang = qang(rq, q)
            dt = float(np.linalg.norm(sample(*ref[(n, 'translation')], t) - np.array(tr)))
            worst_r[n] = max(worst_r.get(n, 0), ang); worst_t[n] = max(worst_t.get(n, 0), dt)
    keys = ['hips', 'chest', 'upperarm.R', 'hand.L', 'thigh.R', 'robe00_1', 'robe09_2', 'orbit']
    print('VALIDATE %-12s frames %d  max rot err %.4f deg (%s)  max trans err %.2e m (%s)' % (
        name, len(caps), max(worst_r.values()), max(worst_r, key=worst_r.get), max(worst_t.values()), max(worst_t, key=worst_t.get)))
    print('   per node rot deg:', {k: round(worst_r[k], 4) for k in keys}, ' trans mm:', {k: round(worst_t[k] * 1000, 3) for k in keys})
    return max(worst_r.values()), max(worst_t.values())
def rest_check():
    set_pose({}); cap = capture(); wr = wt = 0
    for n, (tr, q) in cap.items():
        nd = NODES[NID[n]]; r = nd.get('rotation', [0, 0, 0, 1]); t0 = nd.get('translation', [0, 0, 0])
        wr = max(wr, qang(r, q)); wt = max(wt, float(np.linalg.norm(np.array(t0) - tr)))
    print('VALIDATE rest pose vs GLB nodes: max rot err %.5f deg, max trans err %.2e m' % (wr, wt))
    return wr, wt
if VALIDATE:
    res = {'rest': rest_check()}
    res['Idle'] = compare('Idle', bake2('Idle', 60, idle, True, legacy=True))
    for nm in ('Clockbolt', 'ArcLance', 'Convergence', 'Clockbolt2'):
        fr, K = KIT[nm]; res[nm] = compare(nm, bake2(nm, fr, kit_pose(K), False, legacy=True))
    res['Dodge'] = compare('Dodge', bake2('Dodge', 12, dodge, False, legacy=True))
    json.dump(res, open(out_json, 'w'), indent=1); raise SystemExit(0)

# ---------------- the redesign kit (docs/design/arcanist-fx-prompts.md A1-A10) ----------------
# Casts play on an upper-body layer (spine subtree + orbit) while he walks and full-body while he stands, so cast_pose
# keeps the hips square to the planted feet (no hips yaw: the twist lives in spine / chest), plants both feet with the
# stance IK (optional 'liftL' / 'liftR' raise a foot during a step, 'toeL' / 'toeR' rise onto the toes) and grounds the
# soles. The head counter-turns the twist ('hyaw' / 'nod' on top), so it stays on the target.
# Extra channels: chest / spine roll 'croll', head tilt 'htilt', robe air 'rflare' (outward, m/s) and 'rlift' (up),
# Disintegrate lens 'lens' (0..1 blend of the astrolabe to 'lensd' m in front of the right palm).
def cast_pose(t, K):
    s = base_pose(t, K)
    g = lambda n, d=0.0: (key(t, K[n]) if n in K else d)
    tw, lean, drop, hy = g('tw'), g('lean'), g('drop'), g('hy')
    hz = STANCE_HZ - drop + g('hz'); hp = lean * 0.3; sp = 2 + g('spread')
    s['hips']['rot'] = Rzyx(x=hp, y=g('roll'))
    s['spine']['rot'] = Rzyx(x=2 + lean * 0.7, y=g('croll') * 0.4, z=tw * 0.5)
    s['chest']['rot'] = Rzyx(x=g('chest'), y=g('croll') * 0.6, z=tw * 0.6)
    s['head']['rot'] = Rzyx(x=-lean * 0.6 + g('nod'), y=g('htilt'), z=-tw * 1.1 + g('hyaw'))
    for side, n, ay in ((-1, 'R', STANCE_ANKLE_Y['R'] - g('front')), (1, 'L', STANCE_ANKLE_Y['L'] + g('back'))):
        leg_ = lambda sp_: ik_leg(side, ay, hy, hz - g('lift' + n), hip_pitch=hp, spread=sp_, foot=g('toe' + n))
        s.update(leg_(sp))
        if ANK_X:   # ik_leg ignores the splay: solve the spread that keeps this ankle on its stance line (no side slide)
            x0 = ankle_x(s, n); s.update(leg_(sp + 1)); x1 = ankle_x(s, n)
            s.update(leg_(sp + (ANK_X[n] - x0) / (x1 - x0)))
    s['_ground'] = 0.0
    s.update({'_rflare': g('rflare'), '_rlift': g('rlift'), '_lens': g('lens'), '_lensd': g('lensd', 0.4)})
    return s
def ankle_x(s, n):
    # posed ankle (shin tail) x of a pose dict, from the rest joints and the rotations alone (no scene update)
    Dh = s['hips']['rot']; hip = HEAD['hips'] + Vector(s['hips']['loc']) + Dh @ (HEAD[f'thigh.{n}'] - HEAD['hips'])
    Dt = Dh @ s[f'thigh.{n}']['rot']; knee = hip + Dt @ (TAIL[f'thigh.{n}'] - HEAD[f'thigh.{n}'])
    return (knee + Dt @ s[f'shin.{n}']['rot'] @ (TAIL[f'shin.{n}'] - HEAD[f'shin.{n}'])).x
ANK_X = None
ANK_X = {n: ankle_x(cast_pose(0, {}), n) for n in 'LR'}      # the stance ankles' x
def spin_of(rate, total_turns=None):
    # astrolabe spin (deg) as the integral of a piecewise-linear rate [(t, deg per unit t)], so it never stutters the way
    # eased keys would; scaled to end on whole turns (the next clip starts its spin at 0)
    ts = np.linspace(0, 1, 401); r = np.interp(ts, [p[0] for p in rate], [p[1] for p in rate])
    cum = np.concatenate([[0], np.cumsum((r[1:] + r[:-1]) / 2 * np.diff(ts))])
    turns = total_turns if total_turns is not None else max(0, round(cum[-1] / 360))
    if cum[-1] > 0: cum = cum * (turns * 360 / cum[-1])
    return lambda t: float(np.interp(t, ts, cum))
def mirror2(K):
    # arcanist_rig.mirror, plus the roll / head-yaw / head-tilt channels this file adds
    out = mirror(K)
    for k in ('croll', 'hyaw', 'htilt'):
        if k in out: out[k] = [(t, -v) for t, v in out[k]]
    return out
def clip(K, extra=None):
    def fn(f, t):
        s = cast_pose(t, K)
        if extra: extra(s, t)
        return s
    return fn
def with_spin(sp):
    def ex(s, t): s['_spin'] = sp(t)
    return ex

# reference points (chest space at rest, m). Shoulders (+-0.17, -0.05, 1.42); wrist reach 0.428 m.
PALM_FWD_R = (-0.15, -1, 0.12)                      # right palm toward the target with the chest turned ~11 deg left
CHEEK_R = (-0.2, -0.1, 1.5)                         # wrist cocked beside the right cheek
REACH = 0.4272                                      # wrist this far from the shoulder: elbow bent ~13 deg, reads straight
def reach(d, n='R'): return tuple(Vector(HEAD[f'upperarm.{n}']) + Vector(d).normalized() * REACH)
def reach_ang(world_deg, tw_deg, drop=0.1):        # straight right arm at world_deg from forward (+ = toward his left)
    a = math.radians(world_deg - tw_deg * 1.1); return reach((math.sin(a), -math.cos(a), -drop))
def reach_dir(tw_deg, drop=0.1):                    # a straight right arm pointing at the target (world forward)
    a = math.radians(tw_deg * 1.1); return reach((-math.sin(a), -math.cos(a), -drop))
TAP_EXT = reach_dir(20)

# A1 Magic Missile tap: cock to the cheek (.14), snap straight out palm-first (.3, missile leaves at .35), hold the
# line to .55 so taps chain, recover. MissileTap2 is the mirror.
TAP_HALF = (-0.25, -0.24, 1.3)                      # recovering: elbow folded back, hand in front of the chest
MISSILE_TAP = {
    'rh': [(0, REST_RH), (0.143, CHEEK_R), (0.3, TAP_EXT), (0.4, (TAP_EXT[0], TAP_EXT[1] + 0.02, TAP_EXT[2] - 0.01)), (0.57, TAP_HALF), (1, REST_RH)],
    'rpole': [(0, (-0.6, 0.4, -0.6))] * 2,
    'rd': [(0, STANCE['rd']), (0.143, (0, 0.2, 0.98)), (0.3, (-0.12, -0.35, 0.93)), (0.4, (-0.12, -0.4, 0.9)), (0.57, (0.1, -0.6, 0.75)), (1, STANCE['rd'])],
    'rp': [(0, STANCE['rp']), (0.143, (0.3, -0.9, 0.2)), (0.3, PALM_FWD_R), (0.4, PALM_FWD_R), (0.57, (0.6, -0.6, 0.2)), (1, STANCE['rp'])],
    'lu': [(0, STANCE['lu']), (0.3, (0.4, 0.18, -0.9)), (0.55, (0.4, 0.18, -0.9)), (1, STANCE['lu'])],
    'lf': [(0, STANCE['lf']), (0.3, (0.25, -0.1, -0.96)), (1, STANCE['lf'])],
    'tw': [(0, 0), (0.143, -8), (0.3, 20), (0.4, 19), (0.57, 9), (1, 0)], 'lean': [(0, 0), (0.143, -2), (0.3, 4), (0.45, 3), (1, 0)],
    'croll': [(0, 0), (0.3, -3), (0.45, -3), (1, 0)], 'flow': [(0, 0), (0.3, 5), (1, 0)],
    'orb': [(0, (0, 0, 0)), (0.3, (0.0, 0.07, -0.02)), (1, (0, 0, 0))], 'otilt': [(0, 0), (0.3, 12), (1, 0)]}
def tap_extra(s, t): s['_spin'] = TAP_SPIN(t); s['_oyaw'] = 0.3     # the astrolabe barely follows the twist: stays off his face
TAP_SPIN = spin_of([(0, 0), (0.3, 1500), (1, 0)], 1)

# A2 Magic Missile charge: right hand up and back beside the shoulder, palm up and open toward the astrolabe; left hand
# across the chest, palm out; lean back, chin up. Settled by .8, held (the clip's last frame is the held pose).
CHARGE_RH = (-0.36, 0.07, 1.51)                     # shoulder height, drawn back behind the shoulder line
CHARGE_LH = (-0.04, -0.25, 1.27)
CHARGE_ORB = (-0.11, 0.06, 0.16)                    # up and out, clear of the raised palm
CHARGE_END = {'rd': (-0.3, 0.9, 0.3), 'rp': (-0.45, 0.05, 0.89), 'ld': (-0.9, -0.2, 0.3), 'lp': (0, -1, 0.05)}
MISSILE_CHARGE = {
    'rh': [(0, REST_RH), (0.4, (-0.37, -0.04, 1.33)), (0.8, CHARGE_RH), (1, CHARGE_RH)],
    'rpole': [(0, (-0.4, 0.5, -0.8))] * 2,
    'rd': [(0, STANCE['rd']), (0.8, CHARGE_END['rd']), (1, CHARGE_END['rd'])],
    'rp': [(0, STANCE['rp']), (0.8, CHARGE_END['rp']), (1, CHARGE_END['rp'])],
    'lh': [(0, REST_LH), (0.45, (0.12, -0.25, 1.2)), (0.8, CHARGE_LH), (1, CHARGE_LH)],
    'lpole': [(0, (0.7, 0.2, -0.7))] * 2,
    'ld': [(0, STANCE['ld']), (0.8, CHARGE_END['ld']), (1, CHARGE_END['ld'])],
    'lp': [(0, STANCE['lp']), (0.8, CHARGE_END['lp']), (1, CHARGE_END['lp'])],
    'tw': [(0, 0), (0.8, -8), (1, -8)], 'lean': [(0, 0), (0.8, -9), (1, -9)], 'nod': [(0, 0), (0.8, -13), (1, -13)],
    'chest': [(0, 0), (0.8, -5), (1, -5)], 'flow': [(0, 0), (0.6, 4), (1, 2)],
    'orb': [(0, (0, 0, 0)), (0.8, CHARGE_ORB), (1, CHARGE_ORB)], 'otilt': [(0, 0), (0.8, -12), (1, -12)]}
CHARGE_SPIN = spin_of([(0, 200), (0.8, 600), (1, 0)], 1)

# A3 Magic Missile volley: from the charge pose the right arm sweeps out wide (.12) and around in a fan (darts released
# ~.2) to point at the target palm-first (.38); left hand pulls back to the hip. Ready to chain by .7.
VOLLEY_EXT = reach_dir(14, 0.08)
MISSILE_VOLLEY = {
    'rh': [(0, CHARGE_RH), (0.125, reach_ang(-68, -8, -0.02)), (0.25, reach_ang(-28, 2, 0.03)), (0.375, reach_ang(0, 10, 0.07)), (0.5, reach_ang(15, 15, 0.1)),
           (0.625, TAP_HALF), (1, REST_RH)],
    'rpole': [(0, (-0.4, 0.5, -0.8)), (0.125, (-0.2, 0.3, -0.95)), (0.375, (-0.6, 0.4, -0.6))],
    'rd': [(0, CHARGE_END['rd']), (0.125, (-0.85, -0.35, 0.35)), (0.25, (-0.45, -0.6, 0.65)), (0.375, (0.0, -0.4, 0.9)), (0.5, (0.0, -0.45, 0.88)), (0.625, (0.1, -0.6, 0.75)), (1, STANCE['rd'])],
    'rp': [(0, CHARGE_END['rp']), (0.125, (0.0, -1, 0.1)), (0.375, PALM_FWD_R), (0.5, PALM_FWD_R), (0.625, (0.6, -0.6, 0.2)), (1, STANCE['rp'])],
    'lh': [(0, CHARGE_LH), (0.3, (0.27, 0.06, 1.02)), (0.65, (0.27, 0.05, 1.02)), (1, REST_LH)],
    'lpole': [(0, (0.7, 0.2, -0.7))] * 2,
    'ld': [(0, CHARGE_END['ld']), (0.3, (0.05, 0.15, -0.99)), (0.65, (0.05, 0.15, -0.99)), (1, STANCE['ld'])],
    'lp': [(0, CHARGE_END['lp']), (0.3, (-1, 0, 0)), (1, STANCE['lp'])],
    'tw': [(0, -8), (0.125, -8), (0.25, 2), (0.375, 10), (0.5, 15), (0.625, 6), (1, 0)], 'lean': [(0, -7), (0.25, 4), (0.4, 5), (1, 0)],
    'nod': [(0, -9), (0.3, 0)], 'chest': [(0, -3), (0.375, 2), (1, 0)], 'croll': [(0, 0), (0.375, -4), (0.6, -2), (1, 0)],
    'flow': [(0, 2), (0.3, 10), (1, 0)], 'rflare': [(0, 0), (0.25, 1.5), (0.6, 0)],
    'orb': [(0, CHARGE_ORB), (0.25, (0.0, 0.06, 0.05)), (1, (0, 0, 0))], 'otilt': [(0, -12), (0.25, 15), (1, 0)]}
def volley_extra(s, t): s['_spin'] = VOLLEY_SPIN(t); s['_oyaw'] = 0.4
VOLLEY_SPIN = spin_of([(0, 600), (0.2, 2000), (1, 100)], 2)

# A4 Disintegrate start (held on the last frame): both hands gather at the chest (.35) while the left foot steps back
# 0.25 m, then the right arm thrusts out (.72) palm open and vertical, the left hand gripping the wrist from below; the
# astrolabe swings off the shoulder to hang 0.4 m in front of the palm as the lens; head turned 8 deg away, chin down.
DIS_RH = reach((0.16, -0.98, -0.1))                 # thrust straight out, slightly in toward the centre line
DIS_LH = (-0.06, -0.37, 1.24)                       # under the right forearm, fingers wrapping the wrist from below
DIS_END = {
    'rh': DIS_RH, 'rd': (0.05, -0.2, 0.98), 'rp': PALM_FWD_R, 'lh': DIS_LH, 'ld': (-0.3, -0.6, 0.75), 'lp': (-0.15, 0.75, 0.6),
    'tw': 10, 'lean': 6, 'drop': 0.04, 'hy': 0.04, 'back': 0.25, 'spread': 4, 'nod': 10, 'hyaw': 14, 'chest': 2,
    'lens': 1.0, 'flow': 30, 'orb': (0.4, -0.95, -0.13)}     # orb ~ where the lens puts it
DIS_POLES = {'rpole': [(0, (-0.7, 0.2, -0.7))] * 2, 'lpole': [(0, (0.6, 0.1, -0.8))] * 2}
DIS_START = dict(DIS_POLES, **{
    'rh': [(0, REST_RH), (0.35, (-0.13, -0.2, 1.3)), (0.72, DIS_RH), (1, DIS_RH)],
    'rd': [(0, STANCE['rd']), (0.35, (0.2, -0.3, 0.93)), (0.72, DIS_END['rd']), (1, DIS_END['rd'])],
    'rp': [(0, STANCE['rp']), (0.35, (0.1, -1, 0.2)), (0.72, PALM_FWD_R), (1, PALM_FWD_R)],
    'lh': [(0, REST_LH), (0.35, (0.03, -0.2, 1.22)), (0.6, (-0.03, -0.3, 1.27)), (0.8, DIS_LH), (1, DIS_LH)],
    'ld': [(0, STANCE['ld']), (0.35, (-0.6, -0.5, 0.6)), (0.8, DIS_END['ld']), (1, DIS_END['ld'])],
    'lp': [(0, STANCE['lp']), (0.35, (-0.5, 0.3, 0.8)), (0.8, DIS_END['lp']), (1, DIS_END['lp'])],
    'tw': [(0, 0), (0.35, -4), (0.72, 12), (1, 10)], 'lean': [(0, 0), (0.35, -3), (0.72, 9), (1, 6)],
    'drop': [(0, 0), (0.35, 0.02), (0.72, 0.05), (1, 0.04)], 'hy': [(0, 0), (0.5, 0.04), (1, 0.04)],
    'back': [(0, 0), (0.12, 0), (0.5, 0.25), (1, 0.25)], 'liftL': [(0, 0), (0.12, 0), (0.3, 0.06), (0.5, 0), (1, 0)],
    'spread': [(0, 0), (0.5, 4), (1, 4)], 'nod': [(0, 0), (0.35, 3), (0.72, 11), (1, 10)], 'hyaw': [(0, 0), (0.4, 0), (0.8, 14), (1, 14)],
    'chest': [(0, 0), (0.35, 4), (0.72, 1), (1, 2)], 'croll': [(0, 0), (0.72, -2), (1, -2)],
    'lens': [(0, 0), (0.42, 0), (0.85, 1), (1, 1)],
    'orb': [(0, (0, 0, 0)), (0.42, (-0.22, -0.3, 0.18)), (0.85, DIS_END['orb'])],    # out to his right first, clear of the shoulder
    'flow': [(0, 0), (0.6, 0), (0.75, 35), (1, 50)], 'rflare': [(0, 0), (0.72, 1.0), (1, 0.5)]})
DIS_START_SPIN = spin_of([(0, 300), (0.5, 300), (0.8, 0), (1, 0)], 1)           # a whirl off the shoulder
DIS_START_SPINY = spin_of([(0, 0), (0.3, 0), (0.6, 500), (1, 252)], 1)        # the lens wheel, ending near the hold's speed
def dis_spins(sz, sy):
    def ex(s, t): s['_spin'] = sz(t); s['_spinY'] = sy(t)
    return ex

# A5 Disintegrate hold (1 s loop): the start's end pose under strain. Both wrists shake together (the grip holds) with a
# ~6 Hz tremor of 2-3 deg plus 5 / 7 Hz partials, robe and sleeves pushed back (flow gusting at 3 / 5 Hz), torso braced,
# the lens spinning 2 turns a second. Every term has whole cycles per second, so the loop closes exactly.
def dis_hold(f, t):
    w = 2 * math.pi * t
    tr = Vector((0.02 * math.sin(6 * w) + 0.007 * math.sin(5 * w + 1.3), 0.006 * math.sin(7 * w + 0.4),
                 0.016 * math.sin(6 * w + 1.9) + 0.007 * math.sin(7 * w + 2.6)))
    hand = 0.2 * math.sin(6 * w + 0.8)
    K = dict(DIS_POLES)
    for k_, v in DIS_END.items():
        if k_ in ('rh', 'lh'): v = tuple(Vector(v) + tr)
        if k_ == 'rd': v = (v[0] + hand * 0.5, v[1], v[2])
        K[k_] = [(0, v)] * 2
    rc = max(0.0, math.sin(3 * w)) ** 2                         # recoil pulse on every second tremor beat
    K['lean'] = [(0, DIS_END['lean'] - 1.8 * rc + 0.4 * math.sin(2 * w))] * 2
    K['chest'] = [(0, DIS_END['chest'] + 3 - 1.5 * rc)] * 2       # braced: chest forward, shoulders rounded
    K['croll'] = [(0, -2 + 1.0 * math.sin(3 * w + 1.0))] * 2
    K['tw'] = [(0, DIS_END['tw'] + 1.0 * math.sin(3 * w + 0.5))] * 2
    K['nod'] = [(0, DIS_END['nod'] - 2.0 * rc + 0.6 * math.sin(6 * w + 2.2))] * 2
    K['flow'] = [(0, 50 + 50 * math.sin(3 * w) + 25 * math.sin(5 * w + 0.7))] * 2     # gusting (the cloth saturates if pushed harder)
    K['rflare'] = [(0, 0.5 + 3.5 * math.sin(4 * w + 1.1))] * 2
    s = cast_pose(0, K); s['_spin'] = 0.0; s['_spinY'] = 720 * t
    return s

# A6 Disintegrate end (0.2 s): the right arm snaps back to the chest (.35), the left hand lets go and drops, a quick
# shake-out of the right hand (.43 / .57 / .71), the astrolabe drifts back to the shoulder, the back foot steps in.
DIS_RD = DIS_END['rd']
DIS_END_CLIP = dict(DIS_POLES, **{
    'rh': [(0, DIS_RH), (0.28, (-0.13, -0.19, 1.28)), (0.5, (-0.28, -0.14, 1.12)), (1, REST_RH)],
    'rd': [(0, DIS_RD), (0.28, (0.35, -0.3, 0.88)), (0.43, (-0.1, -0.5, -0.85)), (0.57, (-0.6, -0.3, -0.75)), (0.71, (0.3, -0.3, -0.9)), (0.86, (-0.55, -0.3, -0.78)), (1, STANCE['rd'])],
    'rp': [(0, PALM_FWD_R), (0.28, (0.4, 0.5, 0.1)), (0.5, STANCE['rp']), (1, STANCE['rp'])],
    'lh': [(0, DIS_LH), (0.3, (0.24, -0.16, 1.12)), (1, REST_LH)],
    'ld': [(0, DIS_END['ld']), (0.3, (0.1, -0.4, -0.9)), (1, STANCE['ld'])],
    'lp': [(0, DIS_END['lp']), (0.3, STANCE['lp']), (1, STANCE['lp'])],
    'tw': [(0, 10), (0.28, 3), (1, 0)], 'lean': [(0, 6), (0.28, -3), (1, 0)], 'drop': [(0, 0.04), (0.7, 0.02), (1, 0)],
    'hy': [(0, 0.04), (0.9, 0), (1, 0)], 'back': [(0, 0.25), (0.2, 0.25), (0.85, 0), (1, 0)],
    'liftL': [(0, 0), (0.2, 0), (0.5, 0.05), (0.85, 0), (1, 0)], 'spread': [(0, 4), (0.85, 0), (1, 0)],
    'nod': [(0, 10), (0.3, 2), (1, 0)], 'hyaw': [(0, 14), (0.5, 0)], 'chest': [(0, 2), (0.28, 5), (1, 0)], 'croll': [(0, -2), (0.57, 2), (0.71, -1), (1, 0)],
    'lens': [(0, 1), (0.14, 0.6), (0.3, 0)],
    'orb': [(0, DIS_END['orb']), (0.3, (0.0, -0.6, 0.12)), (0.6, (-0.06, -0.2, 0.12)), (0.9, (0, 0, 0))],   # out front-right, high, then home
    'flow': [(0, 60), (0.5, 8), (1, 0)], 'rflare': [(0, 2.0), (0.5, 0)]})
DIS_END_SPINY = spin_of([(0, 500), (0.5, 700), (1, 0)], 1)     # the lens whirls down upright as it drifts home

# A7 Rune Sigil (0.3 s): the right hand traces a rune on a plane in front of the chest tilted toward the target (it reads
# from the high camera): down-stroke (.1-.2), hook (.2-.27), cross-stroke (.31-.4), then flicks the palm down at the
# target spot (.5, placement) with a shoulder dip; left hand near the belt.
RUNE_C = Vector((-0.1, -0.36, 1.36)); RUNE_U = Vector((1, 0, 0)); RUNE_V = Vector((0, -0.5, 0.87)).normalized()
def rune(u, v): return tuple(RUNE_C + RUNE_U * u + RUNE_V * v)
RUNE_FLICK = reach((-0.05, -0.8, -0.6))
RUNE_DRAW_D, RUNE_DRAW_P = (0.1, -0.75, 0.65), (0, -0.55, -0.83)        # fingertips leading, palm toward the plane
RUNE_SIGIL = {
    'rh': [(0, REST_RH), (0.06, (-0.15, -0.4, 1.58)), (0.18, (-0.15, -0.36, 1.25)), (0.235, (-0.28, -0.35, 1.19)), (0.294, (-0.37, -0.33, 1.32)),
           (0.355, (-0.38, -0.33, 1.46)), (0.42, (0.0, -0.38, 1.45)), (0.5, RUNE_FLICK), (0.65, RUNE_FLICK), (1, REST_RH)],
    'rpole': [(0, (-0.7, 0.3, -0.65))] * 2,
    'rd': [(0, STANCE['rd']), (0.06, RUNE_DRAW_D), (0.42, RUNE_DRAW_D), (0.5, (-0.05, -0.9, -0.25)), (0.65, (-0.05, -0.9, -0.3)), (1, STANCE['rd'])],
    'rp': [(0, STANCE['rp']), (0.06, RUNE_DRAW_P), (0.42, RUNE_DRAW_P), (0.5, (0, -0.4, -0.92)), (0.65, (0, -0.4, -0.92)), (1, STANCE['rp'])],
    'lh': [(0, REST_LH), (0.3, (0.22, -0.15, 1.03)), (0.7, (0.22, -0.15, 1.03)), (1, REST_LH)],
    'ld': [(0, STANCE['ld']), (0.3, (-0.2, -0.3, -0.93)), (0.7, (-0.2, -0.3, -0.93)), (1, STANCE['ld'])],
    'lp': [(0, STANCE['lp']), (0.3, (-0.9, 0.4, 0)), (1, STANCE['lp'])],
    'tw': [(0, 0), (0.1, 3), (0.42, 3), (0.5, 8), (0.65, 7), (1, 0)], 'lean': [(0, 0), (0.42, 1), (0.5, 4), (0.65, 3.5), (1, 0)],
    'drop': [(0, 0), (0.42, 0), (0.5, 0.02), (0.65, 0.02), (1, 0)], 'chest': [(0, 0), (0.42, 0), (0.5, 5), (0.65, 4), (1, 0)],
    'croll': [(0, 0), (0.5, -3), (1, 0)], 'nod': [(0, 0), (0.15, 4), (0.42, 4), (0.5, 14), (0.65, 12), (1, 0)], 'rflare': [(0, 0), (0.55, 0), (0.7, -1.5), (0.9, 0)],
    'orb': [(0, (0, 0, 0)), (0.5, (0.0, 0.05, 0.03)), (1, (0, 0, 0))], 'otilt': [(0, 0), (0.5, 20), (1, 0)]}
RUNE_SPIN = spin_of([(0, 100), (0.45, 900), (1, 100)], 1)

# A8 Astral Orb (0.75 s): the astrolabe drifts in to meet the left hand, which reaches up and across and opens beside it
# (.25-.55: charging, rings spin up), then lifts and opens upward (.7-.85: the orb lifts free) as he rises onto his toes
# and looks up; ends neutral with the left arm lowering.
ORB_IN = (0.3, -0.5, -0.03)                         # floats in front of his right chest, ~0.4 m from the head
ASTRAL_ORB = {
    'lh': [(0, REST_LH), (0.25, (0.06, -0.4, 1.47)), (0.55, (0.05, -0.41, 1.48)), (0.66, (0.06, -0.4, 1.47)), (0.8, (0.09, -0.3, 1.73)),
           (0.86, (0.1, -0.28, 1.71)), (0.93, (0.2, -0.22, 1.45)), (1, (0.26, -0.13, 1.1))],
    'lpole': [(0, (0.7, 0.2, -0.7)), (0.25, (0.9, 0.0, -0.4)), (0.66, (0.9, 0.0, -0.4)), (0.8, (0.8, 0.2, -0.3)), (1, (0.7, 0.2, -0.7))],
    'ld': [(0, STANCE['ld']), (0.25, (-0.1, -0.35, 0.93)), (0.66, (-0.1, -0.35, 0.93)), (0.8, (-0.1, -0.15, 0.98)), (0.86, (-0.1, -0.15, 0.98)), (1, STANCE['ld'])],
    'lp': [(0, STANCE['lp']), (0.25, (-1, 0.0, 0.1)), (0.66, (-1, 0.0, 0.1)), (0.8, (-0.6, -0.2, 0.75)), (0.86, (-0.6, -0.2, 0.75)), (1, STANCE['lp'])],
    'ru': [(0, STANCE['ru']), (0.8, (-0.5, 0.15, -0.85)), (1, STANCE['ru'])], 'rf': [(0, STANCE['rf']), (0.8, (-0.35, -0.2, -0.9)), (1, STANCE['rf'])],
    'tw': [(0, 0), (0.25, -10), (0.66, -10), (0.82, -4), (1, 0)], 'lean': [(0, 0), (0.25, 2), (0.66, 1), (0.8, -5), (1, 0)],
    'hyaw': [(0, 0), (0.25, -14), (0.66, -12), (0.85, -5), (1, 0)], 'nod': [(0, 0), (0.25, 8), (0.66, 6), (0.8, -18), (1, 0)],
    'htilt': [(0, 0), (0.8, -4), (1, 0)], 'croll': [(0, 0), (0.25, -3), (0.8, 3), (1, 0)], 'chest': [(0, 0), (0.8, -4), (1, 0)],
    'toeL': [(0, 0), (0.62, 0), (0.78, 14), (0.88, 12), (1, 0)], 'toeR': [(0, 0), (0.62, 0), (0.78, 14), (0.88, 12), (1, 0)],
    'flow': [(0, 0), (0.8, 3), (1, 0)], 'rlift': [(0, 0), (0.7, 0), (0.82, 4.0), (1, 0)],
    'orb': [(0, (0, 0, 0)), (0.25, ORB_IN), (0.66, ORB_IN), (0.8, (0.3, -0.5, 0.06)), (1, (0, 0, 0))],
    'otilt': [(0, 0), (0.25, -10), (0.66, -10), (0.82, 0)]}
ORB_SPIN = spin_of([(0, 0), (0.25, 200), (0.55, 2200), (0.8, 2600), (1, 600)], 4)

# A9 Arcane Comet (0.4 s): both arms sweep out and up overhead, palms up, head back (0-.38), the astrolabe climbing
# above; then a hard pull down and forward, both hands pointing at the target (.42-.5, mark at .5), knees bending;
# robe lifts on the upswing and drops on the pull; hold to .72, recover.
COMET_UP_R, COMET_PT_R = (-0.27, -0.03, 1.82), reach((0.05, -0.92, -0.4))
COMET_MID_R = reach((-0.08, -1, 0.05))                 # the pull passes through arms-forward at shoulder height
mx = lambda p: (-p[0], p[1], p[2])
ARCANE_COMET = {
    'rh': [(0, REST_RH), (0.18, (-0.45, -0.12, 1.5)), (0.36, COMET_UP_R), (0.4, (-0.26, -0.04, 1.83)), (0.47, COMET_MID_R), (0.53, COMET_PT_R), (0.72, COMET_PT_R), (1, REST_RH)],
    'lh': [(0, REST_LH), (0.18, mx((-0.45, -0.12, 1.5))), (0.36, mx(COMET_UP_R)), (0.4, mx((-0.26, -0.04, 1.83))), (0.47, mx(COMET_MID_R)), (0.53, mx(COMET_PT_R)), (0.72, mx(COMET_PT_R)), (1, REST_LH)],
    'rpole': [(0, (-0.8, 0.3, -0.5)), (0.36, (-0.9, 0.3, 0.1)), (0.47, (-0.8, 0.2, -0.5)), (0.53, (-0.7, 0.2, -0.7))],
    'lpole': [(0, (0.8, 0.3, -0.5)), (0.36, (0.9, 0.3, 0.1)), (0.47, (0.8, 0.2, -0.5)), (0.53, (0.7, 0.2, -0.7))],
    'rd': [(0, STANCE['rd']), (0.18, (-0.8, -0.2, 0.55)), (0.36, (-0.75, 0.05, 0.66)), (0.4, (-0.75, 0.05, 0.66)), (0.47, (-0.05, -0.95, 0.2)), (0.53, (0.05, -0.92, -0.38)), (0.72, (0.05, -0.92, -0.38)), (1, STANCE['rd'])],
    'rp': [(0, STANCE['rp']), (0.18, (0, -0.2, 1)), (0.4, (0.2, 0, 1)), (0.47, (0, -0.2, -1)), (0.53, (0, -0.35, -0.94)), (0.72, (0, -0.35, -0.94)), (1, STANCE['rp'])],
    'ld': [(0, STANCE['ld']), (0.18, (0.8, -0.2, 0.55)), (0.36, (0.75, 0.05, 0.66)), (0.4, (0.75, 0.05, 0.66)), (0.47, (0.05, -0.95, 0.2)), (0.53, (-0.05, -0.92, -0.38)), (0.72, (-0.05, -0.92, -0.38)), (1, STANCE['ld'])],
    'lp': [(0, STANCE['lp']), (0.18, (0, -0.2, 1)), (0.4, (-0.2, 0, 1)), (0.47, (0, -0.2, -1)), (0.53, (0, -0.35, -0.94)), (0.72, (0, -0.35, -0.94)), (1, STANCE['lp'])],
    'lean': [(0, 0), (0.36, -9), (0.4, -10), (0.47, 4), (0.53, 13), (0.72, 12), (1, 0)], 'nod': [(0, 0), (0.25, -26), (0.4, -30), (0.47, -4), (0.53, 8), (0.72, 7), (1, 0)],
    'chest': [(0, 0), (0.36, -6), (0.53, 4), (1, 0)], 'drop': [(0, 0), (0.36, -0.015), (0.4, -0.015), (0.47, 0.03), (0.55, 0.075), (0.72, 0.07), (1, 0)],
    'hy': [(0, 0), (0.53, 0.035), (0.72, 0.035), (1, 0)], 'spread': [(0, 0), (0.53, 3), (0.72, 3), (1, 0)],
    'flow': [(0, 0), (0.36, -10), (0.53, 10), (1, 0)], 'rlift': [(0, 0), (0.12, 12), (0.36, 16), (0.43, 0), (0.5, -8), (0.65, 0)],
    'rflare': [(0, 0), (0.2, 5), (0.36, 6), (0.43, 0)],
    'orb': [(0, (0, 0, 0)), (0.36, (0.12, 0.12, 0.5)), (0.47, (0.05, 0.02, 0.48)), (0.56, (0.15, -0.55, 0.15)), (0.66, (0.38, -0.85, -0.25)), (0.75, (0.38, -0.85, -0.26)), (1, (0, 0, 0))],
    'otilt': [(0, 0), (0.47, 0), (0.62, 60), (0.75, 60), (1, 0)]}
COMET_SPIN = spin_of([(0, 100), (0.38, 2400), (0.5, 1200), (1, 100)], 3)

# A10 Blink arrival (0.25 s): starts folded (half-crouch 0.25 m down, wrists crossed tight on the chest, head down, robe
# gathered on the legs), unfolds: arms flare out and down to the sides by .4, chest opens, robe and sleeves flare out,
# settle by 1. Both feet stay where they land.
BLINK_IN = {
    'rh': [(0, (0.1, -0.2, 1.38)), (0.4, (-0.47, -0.08, 1.15)), (0.6, (-0.44, -0.1, 1.12)), (1, REST_RH)],
    'lh': [(0, (-0.1, -0.17, 1.42)), (0.4, (0.47, -0.08, 1.15)), (0.6, (0.44, -0.1, 1.12)), (1, REST_LH)],
    'rpole': [(0, (-0.3, 0.1, -0.95)), (0.4, (-0.6, 0.6, -0.5)), (1, (-0.7, 0.3, -0.6))],
    'lpole': [(0, (0.3, 0.1, -0.95)), (0.4, (0.6, 0.6, -0.5)), (1, (0.7, 0.3, -0.6))],
    'rd': [(0, (0.45, 0.05, 0.89)), (0.4, (-0.65, -0.15, -0.74)), (0.6, (-0.6, -0.15, -0.78)), (1, STANCE['rd'])],
    'rp': [(0, (0, 1, 0)), (0.4, (-0.2, -1, 0.1)), (0.6, (-0.2, -1, 0.1)), (1, STANCE['rp'])],
    'ld': [(0, (-0.45, 0.05, 0.89)), (0.4, (0.65, -0.15, -0.74)), (0.6, (0.6, -0.15, -0.78)), (1, STANCE['ld'])],
    'lp': [(0, (0, 1, 0)), (0.4, (0.2, -1, 0.1)), (0.6, (0.2, -1, 0.1)), (1, STANCE['lp'])],
    'drop': [(0, 0.25), (0.45, -0.01), (0.65, 0.015), (1, 0)], 'lean': [(0, 20), (0.4, -5), (0.65, 1), (1, 0)],
    'hy': [(0, 0.07), (0.45, 0), (1, 0)], 'nod': [(0, 24), (0.4, -5), (0.7, 1), (1, 0)], 'chest': [(0, 8), (0.4, -7), (0.7, 1), (1, 0)],
    'spread': [(0, 4), (0.45, 2), (1, 0)],
    'rflare': [(0, -4), (0.12, -3), (0.35, 6), (0.55, -3), (0.75, -5), (0.92, -1.5), (1, 0)], 'rlift': [(0, 0), (0.3, 2.0), (0.6, -2), (0.85, 0)], 'flow': [(0, 0), (0.4, -4), (0.7, -12), (1, -4)],
    'orb': [(0, (0.14, -0.02, -0.1)), (0.4, (-0.03, 0.03, 0.04)), (1, (0, 0, 0))], 'otilt': [(0, 25), (0.4, -10), (1, 0)]}
BLINK_SPIN = spin_of([(0, 2400), (0.4, 1200), (1, 100)], 2)

NEW = {   # name: (frames, fn, loop, events)
    'MissileTap': (8, clip(MISSILE_TAP, tap_extra), False, {'release': 0.35, 'recover': 0.55}),
    'MissileTap2': (8, clip(mirror2(MISSILE_TAP), tap_extra), False, {'release': 0.35, 'recover': 0.55}),
    'MissileCharge': (12, clip(MISSILE_CHARGE, with_spin(CHARGE_SPIN)), False, {'hold': 1.0}),
    'MissileVolley': (9, clip(MISSILE_VOLLEY, volley_extra), False, {'release': 0.2, 'recover': 0.7}),
    'DisintegrateStart': (14, clip(DIS_START, dis_spins(DIS_START_SPIN, DIS_START_SPINY)), False, {'hold': 1.0}),
    'DisintegrateHold': (30, dis_hold, True, {}),
    'DisintegrateEnd': (8, clip(DIS_END_CLIP, dis_spins(lambda t: 0.0, DIS_END_SPINY)), False, {}),
    'RuneSigil': (18, clip(RUNE_SIGIL, with_spin(RUNE_SPIN)), False, {'place': 0.5, 'recover': 0.65}),
    'AstralOrb': (22, clip(ASTRAL_ORB, with_spin(ORB_SPIN)), False, {'charge_start': 0.25, 'release': 0.8}),
    'ArcaneComet': (24, clip(ARCANE_COMET, with_spin(COMET_SPIN)), False, {'mark': 0.5, 'recover': 0.72}),
    'BlinkIn': (15, clip(BLINK_IN, with_spin(BLINK_SPIN)), False, {}),
}

# ---------------- bake and write ----------------
out = {}
for name, (frames, fn, loop, ev) in NEW.items():
    if ONLY and name not in ONLY: continue
    caps = bake2(name, frames, fn, loop)
    tr = {n: [c[n][0] for c in caps] for n in JOINTS}; rot = {}
    for n in JOINTS:
        qs = [np.array(c[n][1]) for c in caps]
        if qs[0][3] < 0: qs[0] = -qs[0]
        for i in range(1, len(qs)):
            if np.dot(qs[i], qs[i - 1]) < 0: qs[i] = -qs[i]
        rot[n] = [q.tolist() for q in qs]
    if loop:   # the closing frame is the first one exactly (they already agree to float noise)
        for n in JOINTS: tr[n][-1] = tr[n][0]; rot[n][-1] = rot[n][0]
    n = len(caps); d = np.array(DIAG)
    # checks: soles on the floor, planted feet not sliding (heel point, horizontal), hips drift; loops: last == first
    print('CHECK %-18s sole z min %.3f max %.3f | foot slide L %.3f R %.3f m | hips xy range %.3f m' % (
        name, d[:, :, 2].min(), d[:, 0:2, 2].max(), np.ptp(d[:, 0, :2], 0).max(), np.ptp(d[:, 1, :2], 0).max(), np.ptp(d[:, 2, :2], 0).max()))
    out[name] = {'frames': frames, 'loop': loop, 'events': ev, 'times': [i / 30 for i in range(n)], 'translation': tr, 'rotation': rot}
    print('BAKED %-18s %2d samples (%.3f s)' % (name, n, (n - 1) / 30))
json.dump(out, open(out_json, 'w'))
print('WROTE', out_json)
