# blender -b chimesinger_prep.blend -P chimesinger_rig.py -- <work dir> <out.glb> [mocap run glb] [unity out.glb]
# Rigs the prepared Chimesinger, mounts the staff in her right hand, keys locomotion and the #36 kit, exports a GLB.
# Adapted from the Vanguard's rig.py. Axes: -Y forward, +X = character's left, Z up.
# Body rotations are degrees in rest-armature axes (X: negative swings a leg forward / positive bends a knee).
# Arms are posed by directions in chest space (see arm_R / arm_L), not angles: the mesh is in an A-pose and the staff
# needs exact aiming, so 'staff points here, forearm points there' is easier to author and to read back.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Matrix, Euler
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Chimesinger']
staff = bpy.data.objects.get('ChimeStaff')

# ---------------- armature (joint positions measured from grid renders of the prepared mesh) ----------------
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.25, 0), None, False),
    'hips': ((0, -0.04, 0.86), (0, -0.04, 0.98), 'root', True),
    'spine': ((0, -0.04, 0.98), (0, -0.05, 1.13), 'hips', True),
    'chest': ((0, -0.05, 1.13), (0, -0.06, 1.36), 'spine', True),
    'neck': ((0, -0.06, 1.36), (0, -0.07, 1.44), 'chest', True),
    'head': ((0, -0.07, 1.44), (0, -0.07, 1.66), 'neck', True),
}
for s, n in ((1, 'L'), (-1, 'R')):
    B.update({
        f'upperarm.{n}': ((s * 0.13, -0.06, 1.28), (s * 0.28, -0.06, 1.14), 'chest', True),
        f'forearm.{n}': ((s * 0.28, -0.06, 1.14), (s * 0.41, -0.05, 1.03), f'upperarm.{n}', True),
        f'hand.{n}': ((s * 0.41, -0.05, 1.03), (s * 0.51, -0.05, 0.95), f'forearm.{n}', True),
        f'thigh.{n}': ((s * 0.075, -0.04, 0.86), (s * 0.05, -0.04, 0.47), 'hips', True),
        f'shin.{n}': ((s * 0.05, -0.04, 0.47), (s * 0.035, -0.02, 0.10), f'thigh.{n}', True),
        f'foot.{n}': ((s * 0.035, -0.02, 0.10), (s * 0.04, -0.16, 0.02), f'shin.{n}', True),
        # coat: a front and a back panel per side, two segments each, driven from the thighs (see secondary())
        f'skirtF.{n}': ((s * 0.07, -0.17, 0.86), (s * 0.10, -0.21, 0.48), 'hips', True),
        f'skirtF2.{n}': ((s * 0.10, -0.21, 0.48), (s * 0.13, -0.23, 0.12), f'skirtF.{n}', True),
        f'skirtB.{n}': ((s * 0.07, 0.08, 0.86), (s * 0.10, 0.12, 0.48), 'hips', True),
        f'skirtB2.{n}': ((s * 0.10, 0.12, 0.48), (s * 0.13, 0.15, 0.12), f'skirtB.{n}', True),
        # bell strands hanging behind each sleeve (they hang from the back of the shoulder): gravity-hung from the chest
        f'tassel.{n}': ((s * 0.27, 0.10, 0.92), (s * 0.31, 0.13, 0.66), 'chest', True),
        f'tassel2.{n}': ((s * 0.31, 0.13, 0.66), (s * 0.37, 0.17, 0.36), f'tassel.{n}', True),
        # the wide sleeve's hanging drape: gravity-hung from the elbow so it keeps falling straight down
        f'sleeve.{n}': ((s * 0.28, -0.06, 1.14), (s * 0.30, -0.06, 0.94), f'forearm.{n}', True),
    })
# the staff rides its own (non-deforming) bone under the right hand; translating it along its axis slides the grip
# Hands: Meshy gives flat mitten hands, palms down. Measure each hand's finger direction (fd), palm normal (nrm, out of
# the palm) and the across-palm direction toward the thumb (fw), then add a two-segment finger block and a thumb that
# curl into a fist. A gripped staff runs along fw through the closed fist.
_co = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', _co); _co = _co.reshape(-1, 3)
HANDAX = {}
for s, n in ((1, 'L'), (-1, 'R')):
    Wp = np.array(B[f'hand.{n}'][0]); d = np.array([s * 0.53, -0.05, 0.935]) - Wp; Lh = np.linalg.norm(d); d /= Lh
    rel = _co - Wp; t = rel @ d; perp = np.linalg.norm(rel - np.outer(t, d), axis=1)
    h = _co[(t > -0.01) & (t < Lh + 0.03) & (perp < 0.07) & (_co[:, 2] > 0.88)]
    _, _, vt = np.linalg.svd(h - h.mean(0), full_matrices=False)
    fd = vt[0] * np.sign(vt[0] @ d); nrm = vt[2] if vt[2][2] > 0 else -vt[2]     # her palms face up/out (fingertips droop that way)
    fw = np.cross(nrm, fd); fw = fw if fw[1] < 0 else -fw                          # thumb side = her front
    HANDAX[n] = tuple(Vector(v) for v in (Wp, fd, nrm, fw))
    W_, fd_, nr_, fw_ = HANDAX[n]
    B[f'fingers.{n}'] = (tuple(W_ + fd_ * 0.08), tuple(W_ + fd_ * 0.115), f'hand.{n}', True)
    B[f'fingers2.{n}'] = (tuple(W_ + fd_ * 0.115), tuple(W_ + fd_ * 0.15), f'fingers.{n}', True)
    B[f'thumb.{n}'] = (tuple(W_ + fd_ * 0.03 + fw_ * 0.025), tuple(W_ + fd_ * 0.075 + fw_ * 0.045), f'hand.{n}', True)
def _grip_point(n):
    W_, fd_, nr_, fw_ = HANDAX[n]; return W_ + fd_ * 0.08 + nr_ * 0.035       # shaft axis: one shaft radius under the palm
B['staff'] = (tuple(_grip_point('R')), tuple(_grip_point('R') + HANDAX['R'][3] * 0.2), 'hand.R', False)
CLOTH = [n for n in B if n.startswith(('skirt', 'tassel', 'sleeve'))]
ad = bpy.data.armatures.new('ChimesingerRig'); arm = bpy.data.objects.new('ChimesingerRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: explicit by region (heat weighting fails on this mesh: the AI mesh is ~hundreds of loose
# parts and even a voxel-remeshed proxy returns no solution). Torso by height, then arms / legs / cloth / head. ----------------
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
nv = len(body.data.vertices); W = np.zeros((nv, len(names)), np.float32)
co = np.empty(nv * 3); body.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
x, y, z = co[:, 0], co[:, 1], co[:, 2]; ax = np.abs(x)
cl = lambda a: np.clip(a, 0, 1)
def blend_rows(mask, rows):
    W[:] = W * (1 - mask[:, None]) + rows * mask[:, None]
def zeros(): return np.zeros((nv, len(names)), np.float32)
def sm(a): a = cl(a); return a * a * (3 - 2 * a)
_sp, _ch, _nk = sm((z - 0.95) / 0.07), sm((z - 1.1) / 0.08), sm((z - 1.35) / 0.05)
W[:, gi['hips']] = 1 - _sp; W[:, gi['spine']] = _sp * (1 - _ch); W[:, gi['chest']] = _ch * (1 - _nk); W[:, gi['neck']] = _nk
for s, n in ((1, 'L'), (-1, 'R')):
    side = (x * s) > 0
    # strands: outboard of the coat, below the sleeve, behind the body line
    strand = side & (z < 0.91) & (z > 0.33) & (((ax > 0.265) & (y > 0.03)) | (ax > 0.3))
    r = zeros(); u = sm((z - 0.6) / 0.12)
    r[:, gi[f'tassel.{n}']] = u; r[:, gi[f'tassel2.{n}']] = 1 - u
    blend_rows(strand.astype(np.float32), r)
    # arm: everything outboard of the shoulder above the hands' lowest point, split along the shoulder -> fingertip line
    S0, T0 = np.array([0.13, 1.28]), np.array([0.53, 0.94]); dvec = T0 - S0; L = np.linalg.norm(dvec); dvec /= L
    t = ((np.stack([ax, z], 1) - S0) @ dvec) / L
    # torso half-width by height (waist 0.11 -> bust 0.16 -> shoulder); the sleeves start ~3 cm outboard of it
    half = np.interp(z, [0.86, 0.92, 0.96, 1.1, 1.22, 1.3, 1.36], [0.19, 0.19, 0.12, 0.12, 0.16, 0.14, 0.12])
    # (front of the bust and the belt pouches sit at y < -0.14; the sleeves never reach that far forward)
    a = cl((ax - half - 0.01) / 0.04) * cl((y + 0.15) / 0.03) * ((z > 0.86) & (z < 1.36) & side & ~strand)
    up, fo = 1 - sm((t - 0.33) / 0.12), sm((t - 0.33) / 0.12) * (1 - sm((t - 0.68) / 0.08))
    r = zeros(); r[:, gi[f'upperarm.{n}']] = up; r[:, gi[f'forearm.{n}']] = fo; r[:, gi[f'hand.{n}']] = 1 - up - fo
    sh = sm((0.16 - t) / 0.1)            # shoulder cap: share with the chest
    r[:, gi['chest']] += sh * up; r[:, gi[f'upperarm.{n}']] *= 1 - sh
    # drape: sleeve cloth well below the arm line, along the forearm half
    zl = S0[1] + t * L * dvec[1]; below = zl - z
    h = sm((below - 0.05) / 0.08) * sm((t - 0.3) / 0.1)
    r[:] = r * (1 - h[:, None]); r[:, gi[f'sleeve.{n}']] += h
    blend_rows(a.astype(np.float32), r)
    # legs: the stockinged core under the coat, and everything at boot height
    # distance from the rest leg axis (thigh + shin), against the stocking's radius at that height
    def seg_d(h, t_):
        h, t_ = np.array(h), np.array(t_); d = t_ - h; u = np.clip(((co - h) @ d) / (d @ d), 0, 1)
        return np.linalg.norm(co - (h + u[:, None] * d), axis=1)
    dl = np.minimum(seg_d(B[f'thigh.{n}'][0], B[f'thigh.{n}'][1]), seg_d(B[f'shin.{n}'][0], B[f'shin.{n}'][1]))
    rad = np.interp(z, [0.1, 0.3, 0.47, 0.7, 0.86], [0.06, 0.05, 0.055, 0.07, 0.09])
    core = side & (((z < 0.86) & (dl < rad + 0.012)) | ((z < 0.2) & (ax < 0.11) & (y > -0.21) & (y < 0.08)))
    r = zeros(); hp = sm((z - 0.78) / 0.1); kn = sm((z - 0.43) / 0.08); an = sm((z - 0.08) / 0.06)
    r[:, gi['hips']] = hp; r[:, gi[f'thigh.{n}']] = (1 - hp) * kn
    r[:, gi[f'shin.{n}']] = (1 - kn) * an; r[:, gi[f'foot.{n}']] = 1 - an
    blend_rows(core.astype(np.float32), r)
    if n == 'L': leg_core = core
    else: leg_core = leg_core | core
# coat / skirt: everything else below the waist. Angle around the pelvis picks the panels, height picks the segment.
cloth = (z < 0.9) & ~leg_core & ~((ax > 0.265) & (z > 0.33) & (z < 0.91) & ((y > 0.03) | (ax > 0.3)))
ang = np.degrees(np.arctan2(x, -(y + 0.04)))       # 0 front, +90 her left, +-180 back
PANELS = [('skirtF.L', 35), ('skirtB.L', 145), ('skirtB.R', -145), ('skirtF.R', -35)]
wa = np.zeros((nv, 4), np.float32)
for i, (_, a0) in enumerate(PANELS):
    d = np.abs((ang - a0 + 180) % 360 - 180); wa[:, i] = cl(1 - d / 110)
wa /= np.maximum(wa.sum(1, keepdims=True), 1e-6)
top = sm((z - 0.8) / 0.08); low = sm((0.5 - z) / 0.25)
r = zeros(); r[:, gi['hips']] = top
for i, (pn, _) in enumerate(PANELS):
    r[:, gi[pn]] = (1 - top) * wa[:, i] * (1 - low); r[:, gi[pn.replace('F.', 'F2.').replace('B.', 'B2.')]] = (1 - top) * wa[:, i] * low
blend_rows(cloth.astype(np.float32), r)
# fingers and thumb take their share of each hand's weight
for n in ('L', 'R'):
    W_, fd_, nr_, fw_ = (np.array(v) for v in HANDAX[n]); hw = W[:, gi[f'hand.{n}']].copy()
    tt = (co - W_) @ fd_; wv = (co - W_) @ fw_
    f1, f2 = sm((tt - 0.05) / 0.04), sm((tt - 0.095) / 0.035)    # wide ramps: the low-poly mitten tears on sharp ones
    th = sm((wv - 0.022) / 0.012) * (tt > 0.0) * (1 - sm((tt - 0.085) / 0.015))
    W[:, gi[f'thumb.{n}']] = hw * th
    rest = hw * (1 - th)
    W[:, gi[f'fingers.{n}']] = rest * f1 * (1 - f2); W[:, gi[f'fingers2.{n}']] = rest * f1 * f2; W[:, gi[f'hand.{n}']] = rest * (1 - f1)
# head, hair and halo move as one
r = zeros(); r[:, gi['head']] = 1
blend_rows(sm((z - 1.41) / 0.05).astype(np.float32), r)
order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, 1)
W = np.where(keep, W, 0); s_ = W.sum(1, keepdims=True); W = np.where(s_ > 0, W / np.maximum(s_, 1e-6), 0)
W[s_[:, 0] == 0, gi['hips']] = 1
print('UNWEIGHTED_FIXED', int((s_[:, 0] == 0).sum()), 'CLOTH', int(cloth.sum()), 'LEGS', int(leg_core.sum()))
uw = co[s_[:, 0] == 0]
if len(uw): print('UNWEIGHTED_AT', np.percentile(uw, [5, 50, 95], axis=0).round(2).tolist())
for g in body.vertex_groups: g.remove(list(range(nv)))
for j, n in enumerate(names):
    vg = body.vertex_groups[n]; idx = np.nonzero(W[:, j] > 0)[0]
    for i in idx: vg.add([int(i)], float(W[i, j]), 'REPLACE')
# Meshy fuses touching parts (legs, sleeve hems on the belt, strands on the coat). An edge whose two ends are driven by
# entirely different bones stretches into a sheet once they move apart, so cut it; the seams sit where parts touch.
import bmesh
E = np.array([e.vertices[:] for e in body.data.edges])
# Families that really separate: left vs right leg, a strand vs anything else, an arm vs the hips / coat.
FAM = {'legL': ['thigh.L', 'shin.L', 'foot.L'], 'legR': ['thigh.R', 'shin.R', 'foot.R'],
       'tasL': ['tassel.L', 'tassel2.L'], 'tasR': ['tassel.R', 'tassel2.R'],
       'armL': ['upperarm.L', 'forearm.L', 'hand.L', 'sleeve.L', 'fingers.L', 'fingers2.L', 'thumb.L'],
       'armR': ['upperarm.R', 'forearm.R', 'hand.R', 'sleeve.R', 'fingers.R', 'fingers2.R', 'thumb.R'],
       'low': ['hips'] + [b for b in names if b.startswith('skirt')]}
fam = np.full(nv, '', object)
for k, bs in FAM.items():
    m = W[:, [gi[b] for b in bs]].sum(1) > 0.6; fam[m] = k
fa, fb = fam[E[:, 0]], fam[E[:, 1]]
pair = lambda p, q: ((fa == p) & (fb == q)) | ((fa == q) & (fb == p))
tas = lambda f: (f == 'tasL') | (f == 'tasR')
far = np.nonzero(pair('legL', 'legR') | (tas(fa) != tas(fb)) | pair('armL', 'low') | pair('armR', 'low'))[0]
bm = bmesh.new(); bm.from_mesh(body.data); bm.edges.ensure_lookup_table()
# (runs after the groups are written: bmesh keeps them per vertex, while deleting renumbers the vertices)
bmesh.ops.delete(bm, geom=[bm.edges[int(i)] for i in far], context='EDGES'); bm.to_mesh(body.data); bm.free(); body.data.update()
print('SEAMS_CUT', len(far))
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
for m in list(body.modifiers): body.modifiers.remove(m)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- staff: gripped in the right fist ----------------
# Rest mount: staff axis along -Y through the fist, ring facing up. Poses aim it with arm_R(staff=..., ring=...).
GRIP_R = _grip_point('R')                     # inside the closed right fist
GRIP_AT = 0.58                                # metres from the butt of the 1.5 m staff
STAFF0 = HANDAX['R'][3].copy()                # through the fist, thumb side = bell end
RING0 = (Vector((0, 0, 1)) - STAFF0 * STAFF0.z).normalized()
if staff:
    Zs, Ys = STAFF0, RING0; Xs = Ys.cross(Zs)
    staff.matrix_world = Matrix.Translation(GRIP_R - STAFF0 * GRIP_AT) @ Matrix((Xs, Ys, Zs)).transposed().to_4x4()
    staff.parent = arm; staff.parent_type = 'BONE'; staff.parent_bone = 'staff'
    staff.matrix_parent_inverse = (arm.matrix_world @ ad.bones['staff'].matrix_local @ Matrix.Translation((0, ad.bones['staff'].length, 0))).inverted()

# ---------------- animation helpers ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
def Rzyx(x=0, y=0, z=0): return R(z=z) @ R(y=y) @ R(x=x)
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
V = lambda *a: Vector(a)
HEAD = {n: Vector(B[n][0]) for n in B}; TAIL = {n: Vector(B[n][1]) for n in B}
SOLE = {n: [Vector((B[n][0][0], 0.0, 0.0)), Vector((B[n][1][0], -0.17, 0.0))] for n in ('foot.L', 'foot.R')}   # heel tip, toe
def delta(n):   # posed rotation of a bone relative to its rest, in armature axes
    return pbs[n].matrix.to_3x3() @ pbs[n].bone.matrix_local.to_3x3().inverted()
def _frame(a, b):
    a = Vector(a).normalized(); b = Vector(b); b = (b - a * a.dot(b)).normalized()
    return Matrix((a, b, a.cross(b))).transposed()
UP0 = {n: (TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).normalized() for n in 'LR'}
FO0 = {n: (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).normalized() for n in 'LR'}
LEN = {n: ((TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).length, (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).length) for n in 'LR'}
def arm_dirs(n, up, fo, hand=None):
    # chest-space rotations for an arm whose upper arm points along up, forearm along fo, hand frame Dh (or none)
    Ru = UP0[n].rotation_difference(Vector(up).normalized()).to_matrix()
    Df = (Ru @ FO0[n]).rotation_difference(Vector(fo).normalized()).to_matrix() @ Ru
    out = {f'upperarm.{n}': {'rot': Ru}, f'forearm.{n}': {'rot': Ru.inverted() @ Df}}
    if hand is not None: out[f'hand.{n}'] = {'rot': Df.inverted() @ hand}
    wrist = HEAD[f'upperarm.{n}'] + Ru @ (HEAD[f'forearm.{n}'] - HEAD[f'upperarm.{n}']) + Df @ (HEAD[f'hand.{n}'] - HEAD[f'forearm.{n}'])
    return out, wrist, (hand if hand is not None else Df)
def arm_R(up, fo, staff_dir, ring=(0, -1, 0)):
    # right arm holding the staff: staff_dir is where the bell end points, ring the side the ring's face turns to
    Dh = _frame(staff_dir, ring) @ _frame(STAFF0, RING0).transposed()
    out, wrist, Dh = arm_dirs('R', up, fo, Dh)
    grip = wrist + Dh @ (GRIP_R - HEAD['hand.R'])
    return out, grip, Vector(staff_dir).normalized()
def two_bone(n, target, pole):
    S = HEAD[f'upperarm.{n}']; L1, L2 = LEN[n]; d = target - S; dist = min(d.length, L1 + L2 - 1e-3); dh = d.normalized()
    a = math.acos(max(-1, min(1, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist))))
    pp = Vector(pole) - dh * dh.dot(Vector(pole)); pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, -1))
    up = dh * math.cos(a) + pp * math.sin(a); elbow = S + up * L1
    return up, (S + dh * dist - elbow).normalized()
def slerpv(a, b, u):
    a, b = Vector(a).normalized(), Vector(b).normalized()
    return a.slerp(b, u, Vector((0, 0, 1))) if a.dot(b) > -0.999 else (a + (b - a) * u).normalized()
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value); vector values (3-tuples) are slerped
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple): return slerpv(v0, v1, u)
            return v0 + (v1 - v0) * u
    v = pts[-1][1]
    return Vector(v).normalized() if isinstance(v, tuple) else v
def keyp(t, pts):  # like key() but tuples are points: smooth linear blend, no normalising
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0)); return Vector(v0).lerp(Vector(v1), u)
    return Vector(pts[-1][1])
POINTS = {'rh'}
def leg(side, thigh, knee, foot=0.0, hip_pitch=0.0, flat=1.0, spread=0.0):
    # thigh: - = forward; knee: + = bend; flat 1 keeps the sole as it was at rest; spread: + = out to the side
    n = 'L' if side > 0 else 'R'
    f = -(hip_pitch + thigh + knee) * flat + foot
    return {f'thigh.{n}': {'rot': R(x=thigh) @ R(y=-side * spread)}, f'shin.{n}': {'rot': R(x=knee)}, f'foot.{n}': {'rot': R(x=f)}}
THIGH_LEN = (TAIL['thigh.L'] - HEAD['thigh.L']).length; SHIN_LEN = (TAIL['shin.L'] - HEAD['shin.L']).length
HIP_Z, ANKLE_Z = B['thigh.L'][0][2], B['shin.L'][1][2]
def ik_leg(side, ankle_y, hips_y, hips_z, hip_pitch=0.0, spread=2.0, foot=0.0):
    # 2-bone IK in the sagittal plane: ankle at its floor height, ankle_y metres along Y (-Y forward)
    fwd = -(ankle_y - hips_y); down = HIP_Z + hips_z - ANKLE_Z
    d = min(math.hypot(fwd, down), THIGH_LEN + SHIN_LEN - 1e-3); a, b = THIGH_LEN, SHIN_LEN
    knee = 180 - math.degrees(math.acos((a * a + b * b - d * d) / (2 * a * b)))
    thigh = -(math.degrees(math.atan2(fwd, down) + math.acos((a * a + d * d - b * b) / (2 * a * d)))) - hip_pitch
    return leg(side, thigh, knee, foot=foot, hip_pitch=hip_pitch, spread=spread)

def secondary(pose):
    # Coat panels follow the thighs (front panel pushed by a forward leg, back panel by a backward one) with a lagging
    # lower segment; '_flow' lifts the coat back (speed), '_flare' opens it outward (spins, landings).
    # Bell strands hang under gravity from the chest, with '_swing' (deg) to trail them.
    flow, flare, swing = pose.get('_flow', 0.0), pose.get('_flare', 0.0), pose.get('_swing', 0.0)
    bpy.context.view_layer.update()
    Dh = delta('hips')
    for s, n in ((1, 'L'), (-1, 'R')):
        if f'skirtF.{n}' not in pose:
            d = Dh.inverted() @ (pbs[f'thigh.{n}'].tail - pbs[f'thigh.{n}'].head).normalized()
            d0 = (TAIL[f'thigh.{n}'] - HEAD[f'thigh.{n}']).normalized()
            fwd = math.degrees(math.atan2(-d.y, -d.z) - math.atan2(-d0.y, -d0.z))   # + = thigh forward
            out = math.degrees(math.atan2(d.x * s, -d.z) - math.atan2(d0.x * s, -d0.z))
            front = -(max(fwd, 0) * 0.95 + min(fwd, 0) * 0.35) + flow * 0.4
            back = -(max(fwd, 0) * 0.35 + min(fwd, 0) * 0.95) + flow
            sp = -s * (max(out, 0) * 0.7 + flare)
            pose[f'skirtF.{n}'] = {'rot': R(x=front) @ R(y=sp)}; pose[f'skirtB.{n}'] = {'rot': R(x=back) @ R(y=sp)}
            pose[f'skirtF2.{n}'] = {'rot': R(x=front * 0.25 + flow * 0.3)}; pose[f'skirtB2.{n}'] = {'rot': R(x=back * 0.3 + flow * 0.5)}
    for n in ('L', 'R'):
        if f'sleeve.{n}' not in pose:
            pose[f'sleeve.{n}'] = {'rot': delta(f'forearm.{n}').inverted() @ R(x=swing * 0.4)}
        if f'tassel.{n}' in pose: continue
        D = delta('chest')
        pose[f'tassel.{n}'] = {'rot': D.inverted() @ R(x=swing)}; pose[f'tassel2.{n}'] = {'rot': R(x=swing * 0.5)}
    for n in CLOTH:
        v = pose[n]; pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
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
    if pose: secondary(pose)
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

# ---------------- stance and the channel-table poser ----------------
# Arm channels are chest-space directions: ru/rf right upper/forearm, rs staff (bell end), rn ring face;
# lu/lf left upper/forearm; lg 0..1 puts the left hand on the staff at la metres from the grip (+ = toward the bell).
# Body: tw twist (+ toward her left), lean, drop (m), hy hips fwd/back (m, - fwd), front right-foot step (m, + fwd),
# back left-foot step (m, + back), spread, flow/flare/swing cloth, hz extra hip height (m, jumps).
STANCE = {
    # staff carried out to her right and tilted back so the ring clears her head by ~15 cm
    'ru': (-0.45, 0.1, -0.89), 'rf': (-0.4, -0.8, -0.45), 'rs': (-0.35, 0.15, 0.92), 'rn': (0.3, -1, 0),
    'lu': (0.28, -0.05, -0.96), 'lf': (0.3, -0.4, -0.87),
}
def arms_from(g):
    ru, rf, rh = g('ru'), g('rf'), g('rh', False)
    if rh is not False: ru, rf = two_bone('R', Vector(rh), g('rpole', (-0.7, 0.2, -0.7)))
    ar, grip, sdir = arm_R(ru, rf, g('rs'), g('rn'))
    out = dict(ar)
    out['staff'] = {'loc': tuple(STAFF0 * (GRIP_AT - g('grip', GRIP_AT)))}
    out.update(fist('R', 1.0))
    lu, lf, lg = Vector(g('lu')), Vector(g('lf')), g('lg', 0.0)
    hand_rel = None
    if lg > 0:
        P = grip + sdir * g('la', -0.3); pole = g('lpole', (0.6, 0.3, -0.7))
        iu, ifo = two_bone('L', P, pole); _, _, Df0 = arm_dirs('L', iu, ifo)
        W_, fd_, nr_, fw_ = HANDAX['L']
        best = None
        for sg in (1, -1):   # either grip direction; take the one closer to how the forearm already points
            Dh = _frame(sdir * sg, Df0 @ fd_) @ _frame(fw_, fd_).transposed()
            ang = (Df0.inverted() @ Dh).to_quaternion().angle
            if best is None or ang < best[0]: best = (ang, Dh)
        Dh = best[1]
        iu, ifo = two_bone('L', P - Dh @ (_grip_point('L') - W_), pole)   # put the fist, not the wrist, on the staff
        lu, lf = slerpv(lu, iu, lg), slerpv(lf, ifo, lg)
        _, _, Df = arm_dirs('L', lu, lf)
        hand_rel = Matrix.Identity(3).to_quaternion().slerp((Df.inverted() @ Dh).to_quaternion(), lg).to_matrix()
    al, _, Df = arm_dirs('L', lu, lf, None if hand_rel is None else arm_dirs('L', lu, lf)[2] @ hand_rel); out.update(al)
    out.update(fist('L', 0.2 + 0.8 * lg))
    return out
def fist(n, k):
    # curl the finger block toward the palm (two joints) and fold the thumb over it; k = 0 open .. 1 closed
    W_, fd_, nr_, fw_ = HANDAX[n]; ax = fd_.cross(nr_).normalized(); tax = fw_.cross(nr_).normalized()
    rot = lambda axis, deg: Matrix.Rotation(math.radians(deg * k), 3, axis)
    return {f'fingers.{n}': {'rot': rot(ax, 75)}, f'fingers2.{n}': {'rot': rot(ax, 85)}, f'thumb.{n}': {'rot': rot(tax, 60)}}
_, _w, _ = arm_dirs('R', STANCE['ru'], STANCE['rf']); STANCE['rh'] = tuple(round(v, 3) for v in _w)   # stance right wrist
STANCE_HZ = 0.0
def base_pose(t, K):
    def g(n, d=None):
        if n in K: return keyp(t, K[n]) if n in POINTS else key(t, K[n])
        if d is not None: return d
        v = STANCE.get(n)
        if isinstance(v, tuple): return Vector(v) if n in POINTS else Vector(v).normalized()
        return v if v is not None else 0.0
    tw, lean, drop, hy = g('tw', 0.0), g('lean', 0.0), g('drop', 0.0), g('hy', 0.0)
    hz = STANCE_HZ - drop + g('hz', 0.0); hp = lean * 0.3
    s = {
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(x=hp, y=g('roll', 0.0), z=tw * 0.4)},
        'spine': {'rot': Rzyx(x=2 + lean * 0.7, z=tw * 0.3)}, 'chest': {'rot': Rzyx(x=g('chest', 0.0), z=tw * 0.4)},
        'head': {'rot': Rzyx(x=-lean * 0.6 + g('nod', 0.0), z=-tw * 0.6)},
        '_flow': g('flow', 0.0), '_flare': g('flare', 0.0), '_swing': g('swing', 0.0),
    }
    s.update(arms_from(g))
    sp = 2 + g('spread', 0.0)
    if STANCE_ANKLE_Y:
        s.update(ik_leg(-1, STANCE_ANKLE_Y['R'] - g('front', 0.0), hy, hz, hip_pitch=hp, spread=sp, foot=g('toeR', 0.0)))
        s.update(ik_leg(1, STANCE_ANKLE_Y['L'] + g('back', 0.0), hy, hz, hip_pitch=hp, spread=sp, foot=g('toeL', 0.0)))
    return s
STANCE_ANKLE_Y = None
# stance hip height: soft knees, both soles on the floor
_s = {'_ground': 0.0}; _s.update(leg(-1, -4, 10, spread=2)); _s.update(leg(1, -2, 8, spread=2)); set_pose(_s)
bpy.context.view_layer.update(); _hb = pbs['hips']; STANCE_HZ = (_hb.bone.matrix_local.to_3x3() @ _hb.location).z
STANCE_ANKLE_Y = {s: pbs[f'foot.{s}'].head.y for s in ('L', 'R')}
print('STANCE', round(STANCE_HZ, 3), {k: round(v, 3) for k, v in STANCE_ANKLE_Y.items()})
def kit_pose(K): return lambda f, t: base_pose(t, K)

def idle(f, t):
    p = 2 * math.pi * t; b = math.sin(p)
    K = {'chest': [(0, 1.5 * b)] * 2, 'tw': [(0, -3 + 3 * math.sin(p + 0.5))] * 2, 'roll': [(0, 1.5 * b)] * 2,
         'rs': [(0, (-0.35 + 0.03 * b, 0.15, 0.92))] * 2, 'lf': [(0, (0.3, -0.4 + 0.05 * math.sin(p + 1), -0.87))] * 2,
         'lu': [(0, (0.28, -0.05 + 0.03 * math.sin(p + 0.6), -0.96))] * 2,
         'swing': [(0, 3 * math.sin(p - 0.8))] * 2, 'drop': [(0, 0.01 * (1 - math.cos(p)))] * 2, 'nod': [(0, -1.5 * b)] * 2}
    return base_pose(0, K)

# ---------------- the Chimesinger kit (board #36). Distinct top-down silhouettes:
#   Resonant Note: one-arm flick, bell end pointed at the target.  Bellstrike: two-handed overhead chop forward.
#   Harmonic Refrain: wide flat sweep, ring face forward.  Crescendo: staff raised high, then planted with a crouch.
#   Mending Chorus: arms open wide, staff straight up overhead.  Harmonic Step (dash): low glide, staff trailing.
# Clip lengths are prototype values at 30 fps; contact frames are listed in CLIP_EVENTS for integration.
KIT = {
    # 0.4 s basic: short cock-back (no big wind-up), then the bell end snaps forward at chest height, ring flat so the
    # aim reads from above. Release at .45, held to .6.
    'ResonantNote': (12, {
        'ru': [(0, STANCE['ru']), (0.2, (-0.55, 0.25, -0.8)), (0.45, (-0.35, -0.75, -0.55)), (0.6, (-0.35, -0.75, -0.55)), (1, STANCE['ru'])],
        'rf': [(0, STANCE['rf']), (0.2, (-0.5, -0.3, 0.8)), (0.45, (-0.25, -0.6, -0.75)), (0.6, (-0.25, -0.6, -0.75)), (1, STANCE['rf'])],
        'rs': [(0, STANCE['rs']), (0.2, (-0.3, 0.3, 0.9)), (0.45, (0, -0.95, 0.2)), (0.6, (0, -0.95, 0.22)), (1, STANCE['rs'])],
        'rn': [(0, STANCE['rn']), (0.3, (0, 0, 1)), (0.6, (0, 0, 1)), (1, STANCE['rn'])],
        'lu': [(0, STANCE['lu']), (0.45, (0.55, 0.4, -0.73)), (0.6, (0.55, 0.4, -0.73)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.45, (0.5, 0.35, -0.8)), (0.6, (0.5, 0.35, -0.8)), (1, STANCE['lf'])],
        'tw': [(0, 0), (0.2, 6), (0.45, -15), (0.6, -14), (1, 0)], 'lean': [(0, 0), (0.2, -2), (0.45, 7), (0.6, 6), (1, 0)],
        'front': [(0, 0), (0.45, 0.12), (0.7, 0.12), (1, 0)], 'hy': [(0, 0), (0.45, -0.03), (0.7, -0.03), (1, 0)],
        'swing': [(0, 0), (0.45, 8), (1, 0)]}),
    # One-handed overhead swing (her arms are too short to keep both hands on a staff held out in front). Choked down
    # near the butt so the bell travels a big arc: cocked back over her right shoulder (.3), hammered down to the floor
    # a body-length ahead (.5), held to .62. The free arm reaches forward on the wind-up and flings back on the hit.
    'Bellstrike': (20, {
        'ru': [(0, STANCE['ru']), (0.3, (-0.5, 0.2, 0.85)), (0.5, (-0.25, -0.85, 0.0)), (0.62, (-0.25, -0.85, -0.05)), (1, STANCE['ru'])],
        'rf': [(0, STANCE['rf']), (0.3, (0.25, -0.15, 0.95)), (0.5, (-0.05, -0.75, 0.3)), (0.62, (-0.05, -0.75, 0.25)), (1, STANCE['rf'])],
        'rs': [(0, STANCE['rs']), (0.3, (0.1, 0.9, 0.4)), (0.42, (0.05, -0.3, 0.95)), (0.5, (0, -0.6, -0.8)), (0.62, (0, -0.58, -0.81)), (0.82, (-0.3, -0.1, 0.95)), (1, STANCE['rs'])],
        'rn': [(0, STANCE['rn']), (0.3, (1, 0, 0)), (0.62, (1, 0, 0)), (1, STANCE['rn'])],
        'grip': [(0, 0.58), (0.22, 0.2), (0.82, 0.2), (1, 0.58)],
        'lu': [(0, STANCE['lu']), (0.3, (0.3, -0.8, -0.5)), (0.5, (0.55, 0.6, -0.58)), (0.62, (0.55, 0.6, -0.58)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.3, (0.1, -0.95, 0.1)), (0.5, (0.5, 0.7, -0.5)), (0.62, (0.5, 0.7, -0.5)), (1, STANCE['lf'])],
        'tw': [(0, 0), (0.3, 15), (0.5, -12), (0.62, -11), (1, 0)], 'lean': [(0, 0), (0.3, -8), (0.5, 18), (0.62, 17), (1, 0)],
        'drop': [(0, 0), (0.3, -0.01), (0.5, 0.07), (0.62, 0.065), (1, 0)], 'hy': [(0, 0), (0.3, 0.03), (0.5, -0.09), (0.7, -0.08), (1, 0)],
        'front': [(0, 0), (0.32, 0), (0.48, 0.25), (0.75, 0.25), (1, 0)], 'back': [(0, 0), (0.48, 0.1), (0.75, 0.1), (1, 0)],
        'swing': [(0, 0), (0.3, -10), (0.55, 15), (1, 0)], 'flow': [(0, 0), (0.5, 8), (1, 0)]}),
    # flat sweep: side-load low on her right (0-.25), across the front (straight ahead at .5), finish on her left (.7)
    'HarmonicRefrain': (22, {
        'ru': [(0, STANCE['ru']), (0.25, (-0.8, 0.3, -0.5)), (0.5, (-0.35, -0.85, -0.4)), (0.7, (0.1, -0.95, -0.3)), (0.8, (0.1, -0.95, -0.3)), (1, STANCE['ru'])],
        'rf': [(0, STANCE['rf']), (0.25, (-0.6, -0.6, -0.2)), (0.5, (0.65, -0.7, -0.2)), (0.7, (0.75, -0.6, -0.2)), (0.8, (0.75, -0.6, -0.2)), (1, STANCE['rf'])],
        'grip': [(0, 0.58), (0.18, 0.15), (0.85, 0.15), (1, 0.58)],
        'rs': [(0, STANCE['rs']), (0.12, (-0.6, 0.4, 0.7)), (0.25, (-0.9, 0.3, 0.1)), (0.5, (0, -1, 0.05)), (0.7, (0.85, -0.3, 0.05)), (0.8, (0.85, -0.28, 0.06)), (1, STANCE['rs'])],
        'rn': [(0, STANCE['rn']), (0.2, (0, 0, 1)), (0.8, (0, 0, 1)), (1, STANCE['rn'])],
        'lu': [(0, STANCE['lu']), (0.25, (0.5, -0.6, -0.6)), (0.5, (0.6, 0.45, -0.65)), (0.8, (0.6, 0.5, -0.6)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.25, (0.0, -0.9, -0.4)), (0.5, (0.75, 0.45, -0.45)), (0.8, (0.8, 0.45, -0.4)), (1, STANCE['lf'])],
        'tw': [(0, 0), (0.25, -40), (0.5, 0), (0.7, 35), (0.8, 33), (1, 0)], 'lean': [(0, 0), (0.25, 5), (0.5, 9), (0.8, 8), (1, 0)],
        'drop': [(0, 0), (0.25, 0.06), (0.5, 0.08), (0.8, 0.07), (1, 0)], 'spread': [(0, 0), (0.25, 6), (0.8, 6), (1, 0)],
        'front': [(0, 0), (0.25, 0.05), (0.5, 0.2), (0.8, 0.2), (1, 0)], 'back': [(0, 0), (0.25, 0.12), (0.8, 0.12), (1, 0)],
        'hy': [(0, 0), (0.5, -0.05), (1, 0)], 'swing': [(0, 0), (0.25, -10), (0.6, 15), (1, 0)], 'flare': [(0, 0), (0.6, 10), (1, 0)]}),
    # Raise the staff up and to her right in both hands (.35-.48, left hand under the right), then drive it into the floor
    # in front with a deep crouch and a step (.56 contact, bell end up), hold to .75, rise. Hand targets are chest-space
    # wrist points the left arm can reach (her reach is ~0.38 m) while staying in front of her chest.
    'Crescendo': (30, {
        'rh': [(0, STANCE['rh']), (0.35, (-0.1, -0.3, 1.5)), (0.48, (-0.1, -0.3, 1.52)), (0.56, (-0.05, -0.33, 1.06)), (0.75, (-0.05, -0.33, 1.06)), (1, STANCE['rh'])],
        'rpole': [(0, (-0.8, 0.2, -0.5))] * 2,
        'rs': [(0, STANCE['rs']), (0.35, (-0.3, 0.1, 0.95)), (0.48, (-0.3, 0.12, 0.95)), (0.56, (0, -0.02, 1)), (0.75, (0, -0.02, 1)), (1, STANCE['rs'])],
        'rn': [(0, STANCE['rn']), (0.35, (0, -1, 0)), (0.75, (0, -1, 0)), (1, STANCE['rn'])],
        'grip': [(0, 0.58), (0.48, 0.58), (0.56, 0.88), (0.8, 0.88), (1, 0.58)],
        'lg': [(0, 0), (0.15, 0), (0.3, 1), (0.8, 1), (0.9, 0), (1, 0)],
        'la': [(0, -0.13), (0.48, -0.13), (0.54, 0.12), (1, 0.12)], 'lpole': [(0, (0.9, 0.1, -0.4))] * 2,
        # free left arm swings out in front before it takes the staff (and after it lets go), so the elbow never cuts the waist
        'lu': [(0, STANCE['lu']), (0.15, (0.55, -0.5, -0.67)), (0.9, (0.55, -0.5, -0.67)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.15, (0.25, -0.9, 0.3)), (0.9, (0.25, -0.9, 0.3)), (1, STANCE['lf'])],
        'lean': [(0, 0), (0.35, -8), (0.48, -8), (0.56, 16), (0.75, 15), (1, 0)], 'nod': [(0, 0), (0.4, -12), (0.56, 6), (1, 0)],
        'drop': [(0, 0), (0.25, 0.04), (0.35, -0.03), (0.48, -0.03), (0.56, 0.16), (0.75, 0.15), (1, 0)],
        'hz': [(0, 0), (0.4, 0.02), (0.48, 0.02), (0.53, 0), (1, 0)],
        'front': [(0, 0), (0.48, 0), (0.56, 0.2), (0.85, 0.2), (1, 0)], 'back': [(0, 0), (0.56, 0.12), (0.85, 0.12), (1, 0)],
        'hy': [(0, 0), (0.56, -0.08), (0.85, -0.07), (1, 0)], 'spread': [(0, 0), (0.56, 8), (0.85, 8), (1, 0)],
        'swing': [(0, 0), (0.45, -8), (0.6, 18), (0.8, 6), (1, 0)], 'flare': [(0, 0), (0.55, 3), (0.6, 12), (0.85, 4), (1, 0)]}),
    # instant heal: lift and open. Staff straight up just outside her head, free arm swept wide, coat blooms (peak .28)
    'MendingChorus': (18, {
        # staff raised like a torch out to her right, shaft ~30 cm clear of her head; ring faces forward
        'rh': [(0, STANCE['rh']), (0.28, (-0.3, -0.12, 1.55)), (0.6, (-0.3, -0.12, 1.54)), (1, STANCE['rh'])], 'rpole': [(0, (-0.9, 0.3, -0.3))] * 2,
        'rs': [(0, STANCE['rs']), (0.28, (-0.28, 0.0, 0.96)), (0.6, (-0.28, 0.0, 0.96)), (1, STANCE['rs'])],
        'grip': [(0, 0.58), (0.28, 0.35), (0.6, 0.35), (1, 0.58)],
        'rn': [(0, STANCE['rn']), (0.28, (0, -1, 0)), (1, STANCE['rn'])],
        'lu': [(0, STANCE['lu']), (0.12, (0.4, -0.7, -0.5)), (0.28, (0.95, -0.2, 0.15)), (0.6, (0.95, -0.2, 0.1)), (1, STANCE['lu'])],
        'lf': [(0, STANCE['lf']), (0.12, (-0.3, -0.9, 0.2)), (0.28, (0.95, -0.25, 0.2)), (0.6, (0.95, -0.25, 0.15)), (1, STANCE['lf'])],
        'tw': [(0, 0), (0.28, 10), (0.6, 9), (1, 0)], 'drop': [(0, 0), (0.12, 0.05), (0.28, -0.02), (0.6, -0.015), (1, 0)],
        'lean': [(0, 0), (0.12, 6), (0.28, -6), (0.6, -5), (1, 0)], 'chest': [(0, 0), (0.28, -6), (0.6, -5), (1, 0)],
        'nod': [(0, 0), (0.28, -10), (1, 0)], 'spread': [(0, 0), (0.28, 5), (0.6, 5), (1, 0)],
        'flare': [(0, 0), (0.3, 12), (0.6, 6), (1, 0)], 'swing': [(0, 0), (0.28, 10), (0.6, -5), (1, 0)]}),
}
CLIP_EVENTS = {  # proposed integration markers (normalised clip time): anticipation end / contact or release / cancel-before / recovery start
    'ResonantNote': {'release': 0.45, 'cancel_before': 0.4, 'recover': 0.7},
    'Bellstrike': {'contact': 0.5, 'cancel_before': 0.42, 'recover': 0.7},
    'HarmonicRefrain': {'release': 0.5, 'cancel_before': 0.4, 'recover': 0.75},
    'Crescendo': {'detonate': 0.56, 'cancel_before': 0.5, 'recover': 0.8},
    'MendingChorus': {'heal': 0.0, 'pose_peak': 0.28, 'recover': 0.6},
    'Dodge': {'move_start': 0.1, 'move_end': 0.7},
}
def dodge(f, t):
    # Harmonic Step: dip, low lean-forward glide with the staff held low and trailing, coat streaming, recover by .85
    d = key(t, [(0, 0), (0.1, 0.6), (0.25, 1), (0.7, 1), (0.85, 0.2), (1, 0)])
    K = {
        'lean': [(0, 20 * d)] * 2, 'drop': [(0, 0.08 * d)] * 2, 'hy': [(0, -0.1 * d)] * 2, 'tw': [(0, -8 * d)] * 2,
        'front': [(0, 0.3 * d)] * 2, 'back': [(0, 0.3 * d)] * 2,
        'ru': [(0, tuple(slerpv(STANCE['ru'], (-0.45, 0.3, -0.85), d)))] * 2, 'rf': [(0, tuple(slerpv(STANCE['rf'], (-0.35, 0.2, -0.9), d)))] * 2,
        'rs': [(0, tuple(slerpv(STANCE['rs'], (-0.2, 0.9, -0.2), d)))] * 2, 'rn': [(0, (0, 0, 1))] * 2,
        'lu': [(0, tuple(slerpv(STANCE['lu'], (0.4, 0.55, -0.73), d)))] * 2, 'lf': [(0, tuple(slerpv(STANCE['lf'], (0.3, 0.75, -0.6), d)))] * 2,
        'flow': [(0, 40 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2, 'flare': [(0, 10 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2,
        'swing': [(0, 20 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2,
    }
    s = base_pose(0, K)
    s['_ground'] = 0.03 * key(t, [(0, 0), (0.2, 0), (0.4, 1), (0.6, 0), (1, 0)])   # a skim, not a hop
    return s
def jump(f, t):
    crouch = key(t, [(0, 0), (0.15, 1), (0.22, 0.2), (0.74, 0), (0.8, 0.7), (0.88, 0.5), (1, 0)])
    ext = key(t, [(0, 0), (0.18, 0), (0.24, 1), (0.3, 0), (1, 0)])
    air = key(t, [(0, 0), (0.28, 0), (0.4, 1), (0.62, 1), (0.74, 0), (1, 0)])
    h = 0.0
    if 0.22 < t < 0.76: u = (t - 0.22) / 0.54; h = 0.5 * 4 * u * (1 - u)
    pitch = 12 * crouch + 15 * crouch * (t > 0.7)
    # in the air: staff out to her right, free arm up and out for balance; the landing absorbs without folding
    K = {'ru': [(0, tuple(slerpv(STANCE['ru'], (-0.85, 0.0, -0.5), air)))] * 2, 'rf': [(0, tuple(slerpv(STANCE['rf'], (-0.5, -0.75, 0.4), air)))] * 2,
         'rs': [(0, tuple(slerpv(STANCE['rs'], (-0.7, 0.2, 0.6), air)))] * 2,
         'lu': [(0, tuple(slerpv(STANCE['lu'], (0.85, -0.2, 0.3), air)))] * 2, 'lf': [(0, tuple(slerpv(STANCE['lf'], (0.7, -0.3, 0.5), air)))] * 2,
         'flow': [(0, -20 * air + 15 * crouch)] * 2, 'flare': [(0, 10 * air)] * 2, 'swing': [(0, -15 * air + 8 * crouch)] * 2}
    s = base_pose(0, K)
    s.update({'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=pitch)},
              'spine': {'rot': Rzyx(x=2 + 14 * crouch - 6 * air)}, 'head': {'rot': Rzyx(x=-18 * crouch + 4 * air)}})
    s.update(leg(-1, -4 - 60 * crouch - 45 * air + 6 * ext, 10 + 80 * crouch + 60 * air - 10 * ext, foot=25 * ext + 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    s.update(leg(1, -2 - 50 * crouch - 5 * air + 2 * ext, 8 + 76 * crouch + 20 * air - 8 * ext, foot=25 * ext + 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    land = key(t, [(0, 0), (0.74, 0), (0.8, 1), (0.9, 0), (1, 0)])
    s['_ground'] = h - 0.03 * land
    return s

# ---------------- mocap run (Mixamo-skeleton clip, e.g. three.js Soldier.glb): legs by bone direction, torso by world delta ----------------
def load_mocap(path, clip='Run'):
    objs0, acts0, meshes0 = set(bpy.data.objects), set(bpy.data.actions), set(bpy.data.meshes)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in objs0]
    src = [o for o in new if o.type == 'ARMATURE'][0]
    act = [a for a in bpy.data.actions if a not in acts0 and a.name.startswith(clip)][0]
    src.animation_data_create()
    for tr in src.animation_data.nla_tracks: tr.mute = True
    src.animation_data.action = act
    if act.slots: src.animation_data.action_slot = act.slots[0]
    names = {'hips': 'Hips', 'spine': 'Spine', 'spine2': 'Spine2', 'neck': 'Neck', 'head': 'Head'}
    for n, m in (('L', 'Left'), ('R', 'Right')):
        names.update({f'thigh.{n}': f'{m}UpLeg', f'shin.{n}': f'{m}Leg', f'foot.{n}': f'{m}Foot', f'toe.{n}': f'{m}ToeBase'})
    flip = Matrix.Rotation(math.pi, 3, 'Z')   # the clip faces +Y in Blender; ours faces -Y
    def grab():
        bpy.context.view_layer.update(); out = {}
        for k, n in names.items():
            mw = src.matrix_world @ src.pose.bones['mixamorig:' + n].matrix
            out[k] = (flip @ mw.to_3x3().normalized(), flip @ mw.translation)
        return out
    src.data.pose_position = 'REST'; rest = grab(); src.data.pose_position = 'POSE'
    f0, f1 = act.frame_range; n = round((f1 - f0) / sc.render.fps * 30)
    samples = []
    for i in range(n):
        fr = f0 + (f1 - f0) * i / n; sc.frame_set(int(fr), subframe=fr - int(fr)); samples.append(grab())
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    for a in [a for a in bpy.data.actions if a not in acts0]: bpy.data.actions.remove(a)
    for me in [me for me in bpy.data.meshes if me not in meshes0]: bpy.data.meshes.remove(me)
    ph = [(s['foot.R'][1].y - s['foot.L'][1].y) for s in samples]; k = max(abs(v) for v in ph) or 1
    print('MOCAP', clip, n, 'frames')
    return rest, samples, [v / k for v in ph]
def mocap_pose(rest, smp, stride=1.0, lean=0.0):
    D = lambda k: smp[k][0] @ rest[k][0].inverted()
    dh = D('hips'); ds = D('spine'); dc = D('spine2'); dn = D('neck'); dhd = D('head')
    dh = Matrix.Identity(3).to_quaternion().slerp(dh.to_quaternion(), 0.5).to_matrix()   # calm the pelvis twist
    sway = (smp['hips'][1].x - rest['hips'][1].x) * 0.7
    pose = {'hips': {'rot': dh, 'loc': (sway, 0, 0)}, 'spine': {'rot': R(x=lean) @ dh.inverted() @ ds},
            'chest': {'rot': ds.inverted() @ dc}, 'neck': {'rot': dc.inverted() @ dn}, 'head': {'rot': R(x=-lean * 0.6) @ dn.inverted() @ dhd}}
    def scale_pitch(d):
        a = math.atan2(-d.y, -d.z) * stride; r = math.hypot(d.y, d.z)
        return Vector((d.x * 0.6, -math.sin(a) * r, -math.cos(a) * r)).normalized()   # narrower track: her legs sit close together
    sole = []
    for side, n in ((1, 'L'), (-1, 'R')):
        hip, knee, ank, toe = (smp[f'{b}.{n}'][1] for b in ('thigh', 'shin', 'foot', 'toe'))
        fd = toe - ank; fd.x = 0
        dirs = [scale_pitch((knee - hip).normalized()), scale_pitch((ank - knee).normalized()), fd.normalized()]
        parent = dh
        for b, d in zip(('thigh', 'shin', 'foot'), dirs):
            name = f'{b}.{n}'; h, t = B[name][0], B[name][1]
            cur = parent @ (Vector(t) - Vector(h)).normalized()
            Db = cur.rotation_difference(d).to_matrix() @ parent
            pose[name] = {'rot': parent.inverted() @ Db}; parent = Db
        sole.append(min(toe.z - rest[f'toe.{n}'][1].z, ank.z - rest[f'foot.{n}'][1].z))
    pose['_ground'] = max(0.0, min(sole)) * 0.9
    return pose
MOCAP = load_mocap(args[2]) if len(args) > 2 and os.path.exists(args[2]) else None
def run_arms(s, sprinting, Dc):
    # Staff held at her right side with the bell end tipped forward, steady (a few degrees of bob), ring face locked.
    # Free left arm: a runner's swing in the sagittal plane, opposite the legs (s < 0 = right foot forward = left arm
    # forward), elbow bent ~80 deg and folding more on the forward swing. Directions are in the hips' frame: Dc (the
    # chest's posed rotation relative to the hips) is mostly cancelled so the clip's chest twist doesn't wave the staff.
    C = Matrix.Identity(3).to_quaternion().slerp(Dc.to_quaternion(), 0.85).to_matrix().inverted()
    w = lambda v: tuple(C @ Vector(v).normalized())
    sd = (-0.2, -0.6, 0.78) if sprinting else (-0.22, -0.4, 0.89)
    out, _, _ = arm_R(w((-0.42, -0.05 + 0.05 * s, -0.9)), w((-0.25, -0.85, -0.45)), w((sd[0], sd[1] + 0.04 * s, sd[2])), w((0, 0, 1)))
    amp = 38 if sprinting else 30
    ph = math.radians(-amp * s)                                   # upper arm pitch, + = forward
    bend = math.radians((85 if sprinting else 75) + 15 * max(0.0, -s))
    lu = (0.14, -math.sin(ph), -math.cos(ph))
    lf = (0.14, -math.sin(ph + bend), -math.cos(ph + bend))
    al, _, _ = arm_dirs('L', w(lu), w(lf))
    out.update(al); out.update(fist('R', 1.0)); out.update(fist('L', 0.45)); return out
if MOCAP:
    MREST, MSMP, MPH = MOCAP; MN = len(MSMP)
    def loco(t, sprinting):
        i = int(round(t * MN)) % MN; p = 2 * math.pi * t
        pose = mocap_pose(MREST, MSMP[i], stride=1.15 if sprinting else 0.9, lean=14 if sprinting else 6)
        set_pose(pose); Dc = delta('hips').inverted() @ delta('chest')
        pose.update(run_arms(MPH[i], sprinting, Dc))
        pose.update({'_flow': (32 if sprinting else 20) + 6 * math.sin(2 * p), '_swing': (30 if sprinting else 18) + 8 * math.sin(2 * p + 1.2)})
        return pose
    run = lambda f, t: loco(t, False); sprint = lambda f, t: loco(t, True)
else:
    raise SystemExit('mocap run clip required (pass the Soldier.glb path): hand-keyed runs were rejected')
RUN_FRAMES, SPRINT_FRAMES = MN, round(MN / 1.3)
acts = [bake('Idle', 60, idle, True), bake('Run', RUN_FRAMES, run, True), bake('Sprint', SPRINT_FRAMES, sprint, True),
        bake('Dodge', 14, dodge, False), bake('Jump', 36, jump, False)]
acts += [bake(name, frames, kit_pose(K), False) for name, (frames, K) in KIT.items()]
# ---------------- clipping QA: staff vs torso / head, free arm vs torso (capsules from the posed bones) ----------------
def _seg_dist(p, a, b):
    ab = b - a; u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9))); return (p - (a + ab * u)).length
def clip_qa(act):
    arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]
    worst = {'staff': 0.0, 'larm': 0.0}; frames = {'staff': 0, 'larm': 0}
    for f in range(int(act.frame_range[0]), int(act.frame_range[1]) + 1):
        sc.frame_set(f); mw = arm.matrix_world
        P = lambda n, tail=False: mw @ (pbs[n].tail if tail else pbs[n].head)
        a, b = P('hips'), P('neck', True); hd = (P('head') + P('head', True)) / 2
        torso = lambda q, r: r - _seg_dist(q, a + (b - a) * 0.12, b - (b - a) * 0.08)   # > 0 = inside
        pen = 0.0
        if staff:
            m = staff.matrix_world
            for i in range(31):
                q = m @ Vector((0, 0, 1.5 * i / 30))
                pen = max(pen, torso(q, 0.15), 0.17 - (q - hd).length)   # head zone includes the face
        hit = 0.0
        for n in ('forearm.L', 'hand.L'):
            for u in (0.0, 0.5, 1.0):
                q = P(n) + (P(n, True) - P(n)) * u; hit = max(hit, torso(q, 0.17))
        for k, v in (('staff', pen), ('larm', hit)):
            if v > 0.01: frames[k] += 1
            worst[k] = max(worst[k], v)
    print('CLIP_QA %-16s staff %2d frames (worst %.2f m)  left arm %2d frames (worst %.2f m)' % (act.name, frames['staff'], worst['staff'], frames['larm'], worst['larm']))
for a_ in acts: clip_qa(a_)
arm.animation_data.action = acts[0]
set_pose({})
import json
with open(os.path.join(work, 'chimesinger_clips.json'), 'w') as fh:
    json.dump({a.name: {'frames': int(a.frame_range[1] - a.frame_range[0]), 'events': CLIP_EVENTS.get(a.name, {})} for a in acts}, fh, indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'chimesinger_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='WEBP', export_image_quality=90)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
if len(args) > 3 and args[3]:
    bpy.ops.export_scene.gltf(filepath=args[3], export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                              export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
    print('EXPORTED', args[3], os.path.getsize(args[3]))
