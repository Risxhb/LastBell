# blender -b arcanist_prep.blend -P arcanist_rig.py -- <work dir> <out.glb> <mocap run glb> [unity out.glb]
# Rigs the prepared Arcanist, floats the astrolabe at his right shoulder, keys locomotion and the handoff kit
# (branch handoff/arcanist-animation-20261021), exports a GLB. Adapted from chimesinger_rig.py (no staff, no grip solve).
# Axes: -Y forward, +X = character's left, Z up. Body rotations are degrees in rest-armature axes
# (X: negative swings a leg forward / positive bends a knee). Arms are posed by directions in chest space (arm_dirs).
import bpy, sys, os, math, json, numpy as np
from mathutils import Vector, Matrix, Euler, Quaternion
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Arcanist']
astro = bpy.data.objects.get('Astrolabe')

# ---------------- armature (joint positions measured from grid renders of the prepared mesh) ----------------
LX = -0.01                         # the legs sit 1 cm right of the bounding-box centre
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.25, 0), None, False),
    'hips': ((0, -0.06, 0.95), (0, -0.06, 1.06), 'root', True),
    'spine': ((0, -0.06, 1.06), (0, -0.06, 1.2), 'hips', True),
    'chest': ((0, -0.06, 1.2), (0, -0.06, 1.42), 'spine', True),
    'neck': ((0, -0.06, 1.42), (0, -0.06, 1.5), 'chest', True),
    'head': ((0, -0.06, 1.5), (0, -0.05, 1.74), 'neck', True),
    # the astrolabe floats on its own: 'orbit' carries it (located each frame in set_pose, never rotated by the body)
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
# robe: NCH chains of 3 segments hang around the pelvis (every 22.5 deg, offset half a step from the front), shaped from the mesh:
# each joint sits on the robe's outer surface at that angle and height. RobeSim moves them; nothing is hand-keyed.
_c0 = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', _c0); _c0 = _c0.reshape(-1, 3)
NCH = 16; CH_STEP = 360 / NCH
CH_A = [((CH_STEP / 2 + CH_STEP * i + 180) % 360) - 180 for i in range(NCH)]
CH_Z = [0.93, 0.72, 0.52, 0.34]
PCY = -0.06                                   # pelvis axis (y) the angles are measured around
_ra = np.degrees(np.arctan2(_c0[:, 0], -(_c0[:, 1] - PCY))); _rr = np.hypot(_c0[:, 0], _c0[:, 1] - PCY)
CHAIN = []
for i, a in enumerate(CH_A):
    pts, last = [], 0.0
    for k, zz in enumerate(CH_Z):
        m = (np.abs((_ra - a + 180) % 360 - 180) < CH_STEP * 0.4) & (np.abs(_c0[:, 2] - zz) < 0.03)
        r = float(np.percentile(_rr[m], 90)) if m.sum() > 8 else last
        r = max(r, last); last = r; t = math.radians(a)
        pts.append((r * math.sin(t), PCY - r * math.cos(t), zz))
    CHAIN.append(pts)
    for k in range(3):
        B[f'robe{i:02d}_{k}'] = (pts[k], pts[k + 1], 'hips' if k == 0 else f'robe{i:02d}_{k - 1}', True)
CLOTH = [n for n in B if n.startswith('robe')]
ad = bpy.data.armatures.new('ArcanistRig'); arm = bpy.data.objects.new('ArcanistRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: explicit by region (heat weighting fails on Meshy meshes, see the Chimesinger notes) ----------------
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
me = body.data; nv = len(me.vertices); W = np.zeros((nv, len(names)), np.float32)
co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
x, y, z = co[:, 0], co[:, 1], co[:, 2]; ax = np.abs(x)
# per-vertex texture colour (display sRGB): tells the ivory collar from the dark hair it touches
_img = body.data.materials[0].node_tree.nodes['Principled BSDF'].inputs['Base Color'].links[0].from_node.image
_S = _img.size[0]; _px = np.array(_img.pixels[:], np.float32).reshape(_S, _S, 4)
_uv = np.empty(len(me.loops) * 2); me.uv_layers.active.data.foreach_get('uv', _uv); _uv = _uv.reshape(-1, 2)
_lv = np.empty(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', _lv)
_q = np.clip((_uv * (_S - 1)).round().astype(int), 0, _S - 1); _c = _px[_q[:, 1], _q[:, 0], :3]
col = np.zeros((nv, 3)); np.add.at(col, _lv, _c); col /= np.maximum(np.bincount(_lv, minlength=nv), 1)[:, None]
val = col.max(1); sat = (val - col.min(1)) / np.maximum(val, 1e-6)
cl = lambda a: np.clip(a, 0, 1)
skin = (col[:, 0] > 0.5) & (col[:, 0] > col[:, 1]) & (col[:, 1] > col[:, 2]) & (col[:, 0] - col[:, 2] > 0.1) & (sat > 0.12) & (sat < 0.5)
def blend_rows(mask, rows): W[:] = W * (1 - mask[:, None]) + rows * mask[:, None]
def zeros(): return np.zeros((nv, len(names)), np.float32)
def sm(a): a = cl(a); return a * a * (3 - 2 * a)
_sp, _ch, _nk = sm((z - 1.03) / 0.07), sm((z - 1.17) / 0.07), sm((z - 1.4) / 0.05)
W[:, gi['hips']] = 1 - _sp; W[:, gi['spine']] = _sp * (1 - _ch); W[:, gi['chest']] = _ch * (1 - _nk); W[:, gi['neck']] = _nk
def seg_d(h, t_):
    h, t_ = np.array(h), np.array(t_); d = t_ - h; u = np.clip(((co - h) @ d) / (d @ d), 0, 1)
    return np.linalg.norm(co - (h + u[:, None] * d), axis=1)
leg_core = np.zeros(nv, bool); arm_all = np.zeros(nv, bool)
for s, n in ((1, 'L'), (-1, 'R')):
    side = (x * s) > 0
    # arm: outboard of the torso above the fingertips, split along the shoulder -> fingertip line (|x|, z)
    S0, T0 = np.array([0.17, 1.42]), np.array([0.32, 0.87]); dvec = T0 - S0; L = np.linalg.norm(dvec); dvec /= L
    t = ((np.stack([ax, z], 1) - S0) @ dvec) / L
    # torso (and robe) half-width by height; the bell sleeves and hands sit just outboard of it
    half = np.interp(z, [0.86, 0.98, 1.05, 1.15, 1.25, 1.35, 1.42], [0.255, 0.255, 0.2, 0.18, 0.16, 0.15, 0.14])
    # ...and near the arm's own line (keeps the belt pouches and chains that hang beside the hands on the hips)
    dA = np.minimum.reduce([seg_d(B[f'{b}.{n}'][0], B[f'{b}.{n}'][1]) for b in ('upperarm', 'forearm', 'hand')])
    rA = np.interp(z, [0.86, 1.0, 1.05, 1.25, 1.45], [0.07, 0.07, 0.11, 0.09, 0.09])
    a = cl((ax - half) / 0.025) * cl((rA - dA) / 0.015 + 0.5) * ((z > 0.86) & (z < 1.45) & side)
    # below the cuff the bare hand sits against the robe's edge: decide by colour (skin vs cloth / brass)
    nearh = side & (z > 0.84) & (z < 1.0) & (ax > 0.21) & (dA < 0.09)
    a = np.where(nearh, (skin | (dA < 0.025)).astype(np.float32), a)
    a = np.where(z < 1.3, (a > 0.5).astype(np.float32), a)      # below the shoulder: hard split, so the seam can be cut
    up, fo = 1 - sm((t - 0.36) / 0.1), sm((t - 0.36) / 0.1) * (1 - sm((t - 0.74) / 0.06))
    r = zeros(); r[:, gi[f'upperarm.{n}']] = up; r[:, gi[f'forearm.{n}']] = fo; r[:, gi[f'hand.{n}']] = 1 - up - fo
    sh = sm((0.15 - t) / 0.1)            # shoulder cap: share with the chest
    r[:, gi['chest']] += sh * up; r[:, gi[f'upperarm.{n}']] *= 1 - sh
    blend_rows(a.astype(np.float32), r); arm_all |= a > 0.5
    # legs: the trousers and boots under the robe (distance from the rest leg axis against the leg's radius there)
    dl = np.minimum(seg_d(B[f'thigh.{n}'][0], B[f'thigh.{n}'][1]), seg_d(B[f'shin.{n}'][0], B[f'shin.{n}'][1]))
    rad = np.interp(z, [0.05, 0.3, 0.4, 0.55, 0.75, 0.94], [0.1, 0.08, 0.095, 0.1, 0.09, 0.1])
    # (the boot's foot reaches 20 cm forward of the ankle: measured against the foot bone too)
    dfoot = seg_d(B[f'foot.{n}'][0], (B[f'foot.{n}'][1][0], -0.26, 0.03))
    # below the robe's hem (z 0.34) everything near the leg is boot
    # (above the boots the trousers are dark: light cloth there is the robe's inner panel hanging over them)
    # (the baggy trousers bulge past that radius: dark cloth close to the leg is trouser too)
    core = side & (z < 0.94) & ~((z > 0.4) & (val > 0.45)) & ((dl < rad) | ((val < 0.22) & (dl < rad + 0.035) & (z < 0.88)) | ((z < 0.2) & (dfoot < 0.08)) | ((z < 0.06) & (np.abs(x - LX - s * 0.06) < 0.13)) | ((z < 0.34) & (np.abs(x - LX - s * 0.06) < 0.11) & (y > -0.2) & (y < 0.1)))
    r = zeros(); hp = sm((z - 0.84) / 0.1); kn = sm((z - 0.45) / 0.08); an = sm((z - 0.08) / 0.06)
    r[:, gi['hips']] = hp; r[:, gi[f'thigh.{n}']] = (1 - hp) * kn
    r[:, gi[f'shin.{n}']] = (1 - kn) * an; r[:, gi[f'foot.{n}']] = 1 - an
    blend_rows(core.astype(np.float32), r); leg_core |= core
# robe: everything else below the belt. Angle around the pelvis picks the panel, height the segment.
cloth = (z < 1.0) & ~leg_core & ~arm_all
ang = np.degrees(np.arctan2(x, -(y + 0.06)))       # 0 front, +90 his left, +-180 back
_u = ((ang - CH_A[0]) % 360) / CH_STEP; i0 = np.floor(_u).astype(int) % NCH; fr = (_u - np.floor(_u)).astype(np.float32); i1 = (i0 + 1) % NCH
tz = np.clip((CH_Z[0] - z) / 0.2, 0.5, 2.5)
segw = np.stack([cl(1 - np.abs(tz - (k + 0.5))) for k in range(3)], 1); segw /= segw.sum(1, keepdims=True)
top = sm((z - 0.88) / 0.08)
r = zeros(); r[:, gi['hips']] = top
for k in range(3):
    cols = np.array([gi[f'robe{j:02d}_{k}'] for j in range(NCH)])
    for idx, w in ((i0, 1 - fr), (i1, fr)): np.add.at(r, (np.arange(nv), cols[idx]), (1 - top) * w * segw[:, k])
blend_rows(cloth.astype(np.float32), r)
# head and hair; the tall ivory collar around them stays with the neck and chest
rn = np.hypot(x, y + 0.05)
# (pale face skin is warm and more saturated than the ivory: it must stay on the head, or the face tears when the head turns)
# (the eye whites are pale too: the face front above the chin is never collar)
collar = (z > 1.42) & (z < 1.62) & (rn > 0.075) & (val > 0.5) & (sat < 0.2) & ~skin & ~((y < -0.09) & (z > 1.55))
r = zeros(); r[:, gi['head']] = 1
blend_rows((sm((z - 1.47) / 0.05) * ~collar).astype(np.float32), r)
r = zeros(); r[:, gi['neck']] = 0.6; r[:, gi['chest']] = 0.4
blend_rows(collar.astype(np.float32), r)
order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, 1)
W = np.where(keep, W, 0); s_ = W.sum(1, keepdims=True); W = np.where(s_ > 0, W / np.maximum(s_, 1e-6), 0)
W[s_[:, 0] == 0, gi['hips']] = 1
# Meshy builds the body from hundreds of loose parts (fingers, pouches, cuffs, tassels). A small part split between two
# limb families tears when they move apart, so its minority vertices copy the weights of their nearest majority vertex.
def family_of(Wm):
    F = {'armL': ['upperarm.L', 'forearm.L', 'hand.L'], 'armR': ['upperarm.R', 'forearm.R', 'hand.R'],
         'legL': ['thigh.L', 'shin.L', 'foot.L'], 'legR': ['thigh.R', 'shin.R', 'foot.R'], 'robe': CLOTH,
         'trunk': ['hips', 'spine', 'chest', 'neck'], 'head': ['head']}
    keys = list(F); S = np.stack([Wm[:, [gi[b] for b in F[k]]].sum(1) for k in keys], 1)
    return np.argmax(S, 1), keys
E0 = np.array([e.vertices[:] for e in me.edges]); lab = np.arange(nv)
while True:
    m = np.minimum(lab[E0[:, 0]], lab[E0[:, 1]]); new = lab.copy()
    np.minimum.at(new, E0[:, 0], m); np.minimum.at(new, E0[:, 1], m); new = new[new]
    if (new == lab).all(): break
    lab = new
famv, fkeys = family_of(W); fixed = 0
for isl in np.unique(lab):
    idx = np.nonzero(lab == isl)[0]
    if len(idx) < 4 or len(idx) > 4000: continue
    cnt = np.bincount(famv[idx], minlength=len(fkeys)); maj = np.argmax(cnt)
    bad = idx[famv[idx] != maj]
    if not len(bad) or cnt[maj] < 0.6 * len(idx): continue
    good = idx[famv[idx] == maj]
    nn = good[np.argmin(((co[bad][:, None] - co[good][None]) ** 2).sum(2), 1)]
    W[bad] = W[nn]; fixed += len(bad)
print('ISLANDS', len(np.unique(lab)), 'REASSIGNED', fixed)
print('WEIGHTS cloth', int(cloth.sum()), 'legs', int(leg_core.sum()), 'arms', int(arm_all.sum()), 'collar', int(collar.sum()))
for g in body.vertex_groups: g.remove(list(range(nv)))
for j, n in enumerate(names):
    vg = body.vertex_groups[n]
    for i in np.nonzero(W[:, j] > 0)[0]: vg.add([int(i)], float(W[i, j]), 'REPLACE')
# Meshy fuses touching parts: cut edges between families that really separate (left vs right leg, an arm vs the
# robe / hips). Runs after the groups are written: bmesh keeps them per vertex, while deleting renumbers the vertices.
import bmesh
E = np.array([e.vertices[:] for e in me.edges])
FAM = {'legL': ['thigh.L', 'shin.L', 'foot.L'], 'legR': ['thigh.R', 'shin.R', 'foot.R'],
       'armL': ['upperarm.L', 'forearm.L', 'hand.L'], 'armR': ['upperarm.R', 'forearm.R', 'hand.R'],
       'low': ['hips'], 'robe': CLOTH, 'waist': ['spine']}
fam = np.full(nv, '', object)
for k, bs in FAM.items(): fam[W[:, [gi[b] for b in bs]].sum(1) > 0.6] = k
fam[collar] = 'collar'; fam[W[:, gi['head']] > 0.6] = 'head'      # the hair brushes the collar: let them part
fa, fb = fam[E[:, 0]], fam[E[:, 1]]
pair = lambda p, q: ((fa == p) & (fb == q)) | ((fa == q) & (fb == p))
def limb_cut():
    # an edge from a limb vertex (> 0.6 on that limb) to one with almost none of it (< 0.1) is a fused contact, not a
    # joint, unless it sits at the shoulder (z > 1.3) or the hip (z > 0.8)
    out = np.zeros(len(E), bool); zE = np.minimum(co[E[:, 0], 2], co[E[:, 1], 2])
    for k, top in (('armL', 1.3), ('armR', 1.3), ('legL', 0.8), ('legR', 0.8)):
        s_ = W[:, [gi[b] for b in FAM[k]]].sum(1); a_, b_ = s_[E[:, 0]], s_[E[:, 1]]
        out |= (((a_ > 0.6) & (b_ < 0.1)) | ((b_ > 0.6) & (a_ < 0.1))) & (zE < top)
    return out
far = np.nonzero(pair('legL', 'legR') | pair('armL', 'low') | pair('armR', 'low') | pair('armL', 'robe') | pair('armR', 'robe') | pair('armL', 'waist') | pair('armR', 'waist')
                | pair('legL', 'robe') | pair('legR', 'robe') | pair('head', 'collar') | limb_cut())[0]
bm = bmesh.new(); bm.from_mesh(me); bm.edges.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.edges[int(i)] for i in far], context='EDGES'); bm.to_mesh(me); bm.free(); me.update()
print('SEAMS_CUT', len(far))
# Cutting leaves slivers (a few triangles of boot trim or hem) cut off on the wrong side: give each small loose piece
# the weights of the nearest vertex of a large piece, so it rides along with what it actually touches.
from mathutils import kdtree
E1 = np.array([e.vertices[:] for e in me.edges]); nv1 = len(me.vertices); lab = np.arange(nv1)
while True:
    m = np.minimum(lab[E1[:, 0]], lab[E1[:, 1]]); new = lab.copy()
    np.minimum.at(new, E1[:, 0], m); np.minimum.at(new, E1[:, 1], m); new = new[new]
    if (new == lab).all(): break
    lab = new
size = np.bincount(lab, minlength=nv1)[lab]; small = size < 150
co1 = np.array([v.co[:] for v in me.vertices]); kd = kdtree.KDTree(int((~small).sum()))
for i in np.nonzero(~small)[0]: kd.insert(co1[i], int(i))
kd.balance()
vgs = list(body.vertex_groups)
for piece in np.unique(lab[small]):
    idx = np.nonzero(lab == piece)[0]
    j = min((kd.find(co1[i]) for i in idx), key=lambda h: h[2])[1]     # the piece rides as one, on its closest contact
    src = {g.group: g.weight for g in me.vertices[j].groups}
    for g in vgs:
        if g.index in src: g.add([int(i) for i in idx], src[g.index], 'REPLACE')
        else: g.remove([int(i) for i in idx])
print('SLIVERS', int(small.sum()), 'verts in', len(np.unique(lab[small])), 'pieces')
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
for m in list(body.modifiers): body.modifiers.remove(m)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
if astro:
    # orb at the orbit bone's head; the device hangs upright in its own frame (pendant down)
    astro.matrix_world = Matrix.Translation(B['orbit'][0])
    astro.parent = arm; astro.parent_type = 'BONE'; astro.parent_bone = 'orbit'
    astro.matrix_parent_inverse = (arm.matrix_world @ ad.bones['orbit'].matrix_local @ Matrix.Translation((0, ad.bones['orbit'].length, 0))).inverted()

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
    def step(s, dt, vel=(0, 0, 0), gust=0.0):
        bpy.context.view_layer.update()
        M = np.array(pbs['hips'].matrix @ pbs['hips'].bone.matrix_local.inverted())
        T = s.R @ M[:3, :3].T + M[:3, 3]
        if s.P is None: s.P, s.Q = T.copy(), T.copy()
        air = -np.array(vel, float) + np.array([0, gust, 0])
        OUT = np.array([(math.sin(math.radians(a_)), -math.cos(math.radians(a_)), 0) for a_ in CH_A]) @ M[:3, :3].T
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
        # 'htw' 0 keeps the twist out of the pelvis (the leg IK is front-to-back only, so a turning pelvis drags the feet)
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(x=hp, y=g('roll', 0.0), z=tw * 0.4 * g('htw', 1.0))},
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
    # the sway lives in the spine and chest only: both feet stay planted
    K = {'chest': [(0, 1.5 * b)] * 2, 'tw': [(0, -3 + 3 * math.sin(p + 0.5))] * 2, 'htw': [(0, 0.0)] * 2,
         'lu': [(0, (0.35, 0.05 + 0.03 * math.sin(p + 0.6), -0.94))] * 2, 'ru': [(0, (-0.35, 0.05 + 0.03 * math.sin(p + 0.9), -0.94))] * 2,
         'drop': [(0, 0.01 * (1 - math.cos(p)))] * 2, 'nod': [(0, -1.5 * b)] * 2,
         'orb': [(0, (0, 0, 0.03 * math.sin(p)))] * 2, 'spin': [(0, 360 * t)] * 2, 'otilt': [(0, 4 * math.sin(p + 1))] * 2}
    s = base_pose(0, K); s['_ground'] = 0.0      # soles pinned to the floor through the breathing bob
    return s

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
def jump(f, t):
    crouch = key(t, [(0, 0), (0.15, 1), (0.22, 0.2), (0.74, 0), (0.8, 0.7), (0.88, 0.5), (1, 0)])
    ext = key(t, [(0, 0), (0.18, 0), (0.24, 1), (0.3, 0), (1, 0)])
    air = key(t, [(0, 0), (0.28, 0), (0.4, 1), (0.62, 1), (0.74, 0), (1, 0)])
    h = 0.0
    if 0.22 < t < 0.76: u = (t - 0.22) / 0.54; h = 0.5 * 4 * u * (1 - u)
    pitch = 8 * crouch + 8 * crouch * (t > 0.7)
    # arms: back on the crouch, up and out in the air for balance
    sw = lambda c, a_, b_: tuple(slerpv(slerpv(STANCE[c], a_, crouch), b_, air))
    K = {'ru': [(0, sw('ru', (-0.4, 0.55, -0.73), (-0.85, -0.2, 0.3)))] * 2, 'rf': [(0, sw('rf', (-0.3, 0.7, -0.6), (-0.75, -0.4, 0.5)))] * 2,
         'lu': [(0, sw('lu', (0.4, 0.55, -0.73), (0.85, -0.2, 0.3)))] * 2, 'lf': [(0, sw('lf', (0.3, 0.7, -0.6), (0.75, -0.4, 0.5)))] * 2,
         'flow': [(0, -20 * air + 15 * crouch)] * 2, 'flare': [(0, 12 * air)] * 2,
         'orb': [(0, (0, 0, -0.15 * air + 0.05 * crouch))] * 2, 'spin': [(0, 360 * t)] * 2}
    s = base_pose(0, K)
    s.update({'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=pitch)},
              'spine': {'rot': Rzyx(x=2 + 8 * crouch - 6 * air)}, 'head': {'rot': Rzyx(x=-18 * crouch + 4 * air)}})
    s.update(leg(-1, -4 - 60 * crouch - 70 * air + 6 * ext, 10 + 85 * crouch + 120 * air - 10 * ext, foot=25 * ext - 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    s.update(leg(1, -2 - 55 * crouch - 55 * air + 2 * ext, 8 + 80 * crouch + 110 * air - 8 * ext, foot=25 * ext - 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    land = key(t, [(0, 0), (0.74, 0), (0.8, 1), (0.9, 0), (1, 0)])
    if land > 0:   # landing: arms reach forward for balance
        s.update(arms_from(lambda n, d=None: {'ru': slerpv(STANCE['ru'], (-0.35, -0.75, -0.55), land), 'rf': slerpv(STANCE['rf'], (-0.2, -0.95, -0.2), land),
                 'lu': slerpv(STANCE['lu'], (0.35, -0.75, -0.55), land), 'lf': slerpv(STANCE['lf'], (0.2, -0.95, -0.2), land)}.get(n, Vector(STANCE[n]).normalized() if n in STANCE else (d if d is not None else False))))
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
        names.update({f'thigh.{n}': f'{m}UpLeg', f'shin.{n}': f'{m}Leg', f'foot.{n}': f'{m}Foot', f'toe.{n}': f'{m}ToeBase',
                      f'arm.{n}': f'{m}Arm', f'fore.{n}': f'{m}ForeArm', f'wrist.{n}': f'{m}Hand', f'mid.{n}': f'{m}HandMiddle1',
                      f'idx.{n}': f'{m}HandIndex1', f'pinky.{n}': f'{m}HandPinky1'})
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
    for m_ in [m_ for m_ in bpy.data.meshes if m_ not in meshes0]: bpy.data.meshes.remove(m_)
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
        return Vector((d.x * 0.7, -math.sin(a) * r, -math.cos(a) * r)).normalized()
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
if not (len(args) > 2 and os.path.exists(args[2])):
    raise SystemExit('mocap run clip required (pass the Soldier.glb path): hand-keyed runs were rejected')
MREST, MSMP, MPH = load_mocap(args[2]); MN = len(MSMP)
def run_arms(smp, sprinting):
    # Arms retargeted from the same mocap run as the legs: upper arm, forearm and hand directions plus the palm facing,
    # taken relative to the clip's chest so they swing with its torso. Opened ~7 deg away from the body so the bell
    # sleeves clear the robe, and the swing is scaled up a little for the sprint.
    Dm = smp['spine2'][0] @ MREST['spine2'][0].inverted(); C = Dm.inverted()
    P = lambda k: smp[k][1]
    out = {}
    for n, k in (('L', 1), ('R', -1)):
        def d(a_, b_, out_=0.12):
            v = C @ (P(b_) - P(a_)).normalized(); v.x += k * out_
            if sprinting:   # bigger fore-aft swing: scale the angle from straight down in the sagittal plane
                a = math.atan2(-v.y, -v.z) * 1.2; r = math.hypot(v.y, v.z); v = Vector((v.x, -math.sin(a) * r, -math.cos(a) * r))
            return v.normalized()
        up, fo, hd = d(f'arm.{n}', f'fore.{n}'), d(f'fore.{n}', f'wrist.{n}'), d(f'wrist.{n}', f'mid.{n}', 0.05)
        across = C @ (P(f'idx.{n}') - P(f'pinky.{n}'))
        palm = hd.cross(across) * k               # palm side of the hand (toward the thigh at rest)
        out.update(arm_dirs(n, up, fo, hd, palm))
    return out
def loco(t, sprinting):
    i = int(round(t * MN)) % MN; p = 2 * math.pi * t
    pose = mocap_pose(MREST, MSMP[i], stride=1.15 if sprinting else 0.9, lean=14 if sprinting else 6)
    pose.update(run_arms(MSMP[i], sprinting))
    pose.update({'_flow': (35 if sprinting else 22) + 6 * math.sin(2 * p),
                 '_vel': (0, -(6.5 if sprinting else 4.2), 0), '_oyaw': 0.0, '_orb': (0.02, 0.04 if sprinting else 0.02, 0.02 * math.sin(2 * p)), '_spin': 360 * t * 2, '_otilt': -12 if sprinting else -6})
    return pose
run = lambda f, t: loco(t, False); sprint = lambda f, t: loco(t, True)
RUN_FRAMES, SPRINT_FRAMES = MN, round(MN / 1.3)
if os.environ.get('ARC_EXPLORE'):     # authoring: run a pose experiment with the rig set up, then stop
    exec(open(os.environ['ARC_EXPLORE']).read()); raise SystemExit(0)
acts = [bake('Idle', 60, idle, True), bake('Run', RUN_FRAMES, run, True), bake('Sprint', SPRINT_FRAMES, sprint, True),
        bake('Dodge', 12, dodge, False), bake('Jump', 36, jump, False)]
acts += [bake(name, frames, kit_pose(K), False) for name, (frames, K) in KIT.items()]
arm.animation_data.action = acts[0]
set_pose({})
with open(os.path.join(work, 'arcanist_clips.json'), 'w') as fh:
    json.dump({a.name: {'frames': int(a.frame_range[1] - a.frame_range[0]), 'events': CLIP_EVENTS.get(a.name, {})} for a in acts}, fh, indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'arcanist_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='WEBP', export_image_quality=90)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
if len(args) > 3 and args[3]:
    bpy.ops.export_scene.gltf(filepath=args[3], export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                              export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
    print('EXPORTED', args[3], os.path.getsize(args[3]))
