# blender -b -P crucible_skater_rig.py -- <work dir> <out.glb> [--textured <tex dir>]
# The Crucible Skater (9 Nov 2026 remodel, docs/bosses/crucible-skater.md): a small brass automaton figure skater
# (~1.7 m to the top of the crucible) built from script, no Meshy source. Each part is rigid and pinned whole to one bone
# (ball joints hide the gaps). Prompts: build-evidence/miniboss-art-20261109/crucible-skater/prompts.md.
# Axes: -Y forward (the ivory mask), +X = its left, Z up. Unity's glTF import turns -Y into +Z.
#
# Clips (30 fps): Idle 3.0 loop, Walk 1.0 loop, CarveTrack 1.0 loop, CarveCrouch 0.9, CarveLoop 0.5 loop, CarveTurn 0.35,
# CheckWindup 0.8, CheckLoop 0.8 loop, ChargeLoop 0.4 loop, Trip 0.6, StuckLoop 1.2 loop, TossRaise 0.6, TossLoop 1.0 loop,
# TossFlick 0.5, LapLoopL / LapLoopR 0.8 loops, PirouetteLoop 0.5 loop, DizzyLoop 1.2 loop, Reheat 2.0, Defeat 2.5 (held).
import bpy, bmesh, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
from mathutils import Vector, Matrix, Euler, Quaternion
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])
os.makedirs(work, exist_ok=True)
TEX = args[args.index('--textured') + 1] if '--textured' in args else None

# fresh scene
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------- skeleton (rest pose) ----------------
SH = {'L': (0.17, 0.0, 1.17)}; SH['R'] = mirror(SH['L'])
ARM = {'L': [(0.17, 0.0, 1.17), (0.255, 0.01, 0.905), (0.375, -0.03, 0.665), (0.40, -0.05, 0.585)]}
ARM['R'] = [mirror(p) for p in ARM['L']]
LEG = {'L': [(0.085, 0.0, 0.82), (0.11, -0.012, 0.485), (0.12, 0.0, 0.15), (0.12, -0.15, 0.085)]}
LEG['R'] = [mirror(p) for p in LEG['L']]
NP = 8
B = {'root': ((0, 0, 0), (0, 0.3, 0), None),
     'hips': ((0, 0, 0.80), (0, 0, 0.90), 'root'),
     'spine': ((0, 0, 0.90), (0, 0, 1.20), 'hips'),
     'head': ((0, 0, 1.22), (0, 0, 1.44), 'spine'),
     'crucible': ((0, 0, 1.47), (0, 0, 1.69), 'root')}
for i in range(NP):
    a = 2 * math.pi * (i + 0.5) / NP; c, s = math.cos(a), math.sin(a)
    B[f'petal.{i}'] = ((0.12 * c, 0.12 * s, 0.875), (0.34 * c, 0.34 * s, 0.70), 'hips')
for sd in 'LR':
    a = ARM[sd]; l = LEG[sd]
    B['upperarm.' + sd] = (a[0], a[1], 'spine'); B['forearm.' + sd] = (a[1], a[2], 'upperarm.' + sd); B['hand.' + sd] = (a[2], a[3], 'forearm.' + sd)
    B['thigh.' + sd] = (l[0], l[1], 'hips'); B['shin.' + sd] = (l[1], l[2], 'thigh.' + sd); B['foot.' + sd] = (l[2], l[3], 'shin.' + sd)
BONES = [n for n in B if n != 'root']

# ---------------- materials ----------------
MATS = {'Brass': ((0.78, 0.60, 0.30), 0.85, 0.42, 0), 'BrassDark': ((0.40, 0.28, 0.13), 0.8, 0.55, 0),
        'Ivory': ((0.94, 0.90, 0.80), 0.0, 0.45, 0), 'Cobalt': ((0.16, 0.24, 0.55), 0.3, 0.5, 0),
        'Molten': ((1.0, 0.26, 0.02), 0.0, 0.35, 1.0), 'Steel': ((0.55, 0.57, 0.60), 0.9, 0.3, 0),
        'Dark': ((0.05, 0.05, 0.08), 0.0, 0.6, 0)}
MI = {n: i for i, n in enumerate(MATS)}
def make_material(n, col, metal, rough, emit):
    m = bpy.data.materials.new(n); m.use_nodes = True; p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if emit:
        p.inputs['Emission Color'].default_value = (*col, 1); p.inputs['Emission Strength'].default_value = emit
        p.inputs['Base Color'].default_value = (col[0] * 0.3, col[1] * 0.3, col[2] * 0.3, 1)
    return m

# ---------------- geometry (one bmesh, every primitive tagged with a bone and a material) ----------------
bm = bmesh.new(); dl = bm.verts.layers.deform.verify()
GI = {n: i for i, n in enumerate(BONES)}
def tag(verts, bone, mat, smooth=True):
    fs = set()
    for v in verts:
        v[dl][GI[bone]] = 1.0
        fs.update(v.link_faces)
    for f in fs: f.material_index = MI[mat]; f.smooth = smooth
def placed(M):
    return M
def frame(z_dir):
    z = Vector(z_dir).normalized(); return z.to_track_quat('Z', 'Y').to_matrix().to_4x4()
def sphere(bone, mat, c, r, seg=14, rings=8, rot=(0, 0, 0)):
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    M = Matrix.Translation(c) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*r, 1))
    bmesh.ops.transform(bm, matrix=M, verts=res['verts']); tag(res['verts'], bone, mat)
    return res['verts']
def cyl(bone, mat, p0, p1, r0, r1, seg=12, caps=True, smooth=True):
    p0, p1 = Vector(p0), Vector(p1); d = p1 - p0
    res = bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=d.length)
    M = Matrix.Translation((p0 + p1) / 2) @ frame(d)
    bmesh.ops.transform(bm, matrix=M, verts=res['verts']); tag(res['verts'], bone, mat, smooth)
    return res['verts']
def box(bone, mat, c, size, rot=(0, 0, 0)):
    res = bmesh.ops.create_cube(bm, size=1.0)
    M = Matrix.Translation(c) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
    bmesh.ops.transform(bm, matrix=M, verts=res['verts']); tag(res['verts'], bone, mat, False)
    return res['verts']
def torus(bone, mat, c, R, r, axis=(0, 0, 1), seg=20, seg2=6, arc=1.0):
    M = Matrix.Translation(c) @ frame(axis); vs = []; rows = []
    n = seg if arc >= 1 else seg + 1
    for i in range(n):
        a = 2 * math.pi * arc * i / seg; row = []
        for j in range(seg2):
            b = 2 * math.pi * j / seg2; rr = R + r * math.cos(b)
            row.append(bm.verts.new(M @ Vector((rr * math.cos(a), rr * math.sin(a), r * math.sin(b)))))
        rows.append(row); vs += row
    for i in range(n if arc >= 1 else n - 1):
        a, b2 = rows[i], rows[(i + 1) % n]
        for j in range(seg2): bm.faces.new((a[j], b2[j], b2[(j + 1) % seg2], a[(j + 1) % seg2]))
    tag(vs, bone, mat); return vs
def panel(bone, mat, rows, thick=0.012):
    # a curved plate from rows of points (outer surface), given a thickness inward along -normal
    g = [[bm.verts.new(p) for p in row] for row in rows]; vs = [v for row in g for v in row]
    fs = []
    for i in range(len(g) - 1):
        for j in range(len(g[0]) - 1): fs.append(bm.faces.new((g[i][j], g[i][j + 1], g[i + 1][j + 1], g[i + 1][j])))
    ext = bmesh.ops.extrude_face_region(bm, geom=fs)
    nv = [e for e in ext['geom'] if isinstance(e, bmesh.types.BMVert)]
    bm.normal_update()
    for v in nv:
        # push inward toward the bone axis (petals: toward the Z axis)
        d = Vector((v.co.x, v.co.y, 0)); d = d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))
        v.co -= d * thick
    tag(vs + nv, bone, mat); return vs + nv

BR, BD, IV, CO, MO, ST, DK = 'Brass', 'BrassDark', 'Ivory', 'Cobalt', 'Molten', 'Steel', 'Dark'
# hips: pelvis and waist
sphere('hips', BD, (0, 0, 0.835), (0.105, 0.085, 0.075))
cyl('hips', BR, (0, 0, 0.84), (0, 0, 0.95), 0.085, 0.075, seg=14)
torus('hips', IV, (0, 0, 0.885), 0.12, 0.012, seg=20, seg2=5)
# skirt: eight overlapping brass petals with an ivory hem
for i in range(NP):
    a0 = 2 * math.pi * (i + 0.5) / NP; rows = []
    for k, (rad, z, half) in enumerate(((0.115, 0.885, 0.36), (0.22, 0.80, 0.42), (0.34, 0.705, 0.44))):
        row = []
        for j in range(4):
            a = a0 + half * (j / 3 - 0.5) * 2
            cup = 0.012 * math.sin(math.pi * j / 3)
            row.append(((rad + cup) * math.cos(a), (rad + cup) * math.sin(a), z))
        rows.append(row)
    panel(f'petal.{i}', BR, rows, 0.014)
    c, s = math.cos(a0), math.sin(a0)
    box(f'petal.{i}', IV, (0.345 * c, 0.345 * s, 0.70), (0.035, 0.25, 0.022), rot=(0, 0, math.degrees(a0)))
# spine: bodice, chest core, shoulder balls
sphere('spine', BR, (0, 0, 1.075), (0.15, 0.11, 0.15), seg=16, rings=10)
cyl('spine', BR, (0, 0, 0.92), (0, 0, 1.00), 0.064, 0.10, seg=14)
torus('spine', BD, (0, 0, 1.205), 0.05, 0.016, seg=14, seg2=5)
cyl('spine', BD, (0, 0.10, 1.07), (0, 0.19, 1.07), 0.016, 0.016, seg=8)
box('spine', CO, (0, 0.198, 1.07), (0.13, 0.018, 0.035))
cyl('spine', MO, (0, -0.085, 1.075), (0, -0.118, 1.075), 0.062, 0.058, seg=16)
torus('spine', BD, (0, -0.108, 1.075), 0.068, 0.021, axis=(0, -1, 0), seg=16, seg2=5)
for sd in 'LR':
    sphere('spine', BD, SH[sd], (0.07, 0.07, 0.07))
# head: neck, hood, mask, eyes, collar
cyl('head', BD, (0, 0, 1.18), (0, 0, 1.27), 0.034, 0.04, seg=10)
sphere('head', BR, (0, 0.012, 1.335), (0.118, 0.118, 0.13), seg=16, rings=10)
sphere('head', IV, (0, -0.078, 1.33), (0.113, 0.078, 0.144), seg=16, rings=10)
for x in (-0.042, 0.042):
    box('head', DK, (x, -0.150, 1.352), (0.05, 0.014, 0.022))
box('head', DK, (0, -0.150, 1.275), (0.034, 0.012, 0.008))
cyl('head', BD, (0, 0, 1.43), (0, 0, 1.485), 0.06, 0.085, seg=14)
rim = torus('head', BR, (0, -0.105, 1.335), 0.112, 0.02, axis=(0, -1, 0), seg=22, seg2=6)
for v in rim: v.co.z = 1.335 + (v.co.z - 1.335) * 1.28
box('head', BD, (0, 0.10, 1.37), (0.025, 0.07, 0.16), rot=(-15, 0, 0))
# crucible: bowl, lip, molten top, spout, handles, band
cyl('crucible', BR, (0, 0, 1.475), (0, 0, 1.665), 0.15, 0.25, seg=20)
torus('crucible', BD, (0, 0, 1.668), 0.25, 0.022, seg=24, seg2=6)
torus('crucible', BD, (0, 0, 1.56), 0.20, 0.013, seg=24, seg2=5)
sphere('crucible', MO, (0, 0, 1.664), (0.235, 0.235, 0.032), seg=20, rings=6)
cyl('crucible', BR, (0, -0.235, 1.635), (0, -0.33, 1.668), 0.05, 0.02, seg=10)
for x in (-0.265, 0.265):
    torus('crucible', BD, (x, 0, 1.585), 0.045, 0.013, axis=(0, 1, 0), seg=12, seg2=5)
# arms
for sd in 'LR':
    a = ARM[sd]
    cyl('upperarm.' + sd, BR, a[0], a[1], 0.062, 0.052, seg=12)
    sphere('forearm.' + sd, BD, a[1], (0.058, 0.058, 0.058), seg=10, rings=6)
    cyl('forearm.' + sd, BR, a[1], a[2], 0.052, 0.042, seg=12)
    torus('forearm.' + sd, IV, a[2], 0.046, 0.012, axis=Vector(a[2]) - Vector(a[1]), seg=10, seg2=4)
    h = (Vector(a[2]) + Vector(a[3])) / 2
    sphere('hand.' + sd, BR, h, (0.046, 0.036, 0.075), seg=10, rings=6,
           rot=(math.degrees(math.atan2(a[3][1] - a[2][1], a[2][2] - a[3][2])), 0, 0))
# legs, boots, blades
for sd in 'LR':
    l = LEG[sd]; x = l[2][0]
    sphere('thigh.' + sd, BD, l[0], (0.085, 0.085, 0.085), seg=10, rings=6)
    cyl('thigh.' + sd, BR, l[0], l[1], 0.076, 0.06, seg=12)
    sphere('shin.' + sd, BD, l[1], (0.074, 0.074, 0.074), seg=10, rings=6)
    cyl('shin.' + sd, BR, l[1], (x, 0, 0.29), 0.068, 0.06, seg=12)
    cyl('shin.' + sd, BR, (x, 0, 0.13), (x, 0, 0.38), 0.074, 0.08, seg=14)
    torus('shin.' + sd, IV, (x, 0, 0.38), 0.08, 0.014, seg=14, seg2=5)
    box('shin.' + sd, CO, (x, -0.072, 0.25), (0.06, 0.02, 0.18))
    # foot: boot toe, sole, cobalt housings, struts, blade with its hot edge and a curled toe
    box('foot.' + sd, BR, (x, -0.06, 0.115), (0.12, 0.22, 0.075))
    box('foot.' + sd, BD, (x, -0.04, 0.076), (0.125, 0.27, 0.016))
    for sx in (-1, 1):
        box('foot.' + sd, CO, (x + sx * 0.062, -0.05, 0.115), (0.008, 0.15, 0.048))
    for y in (0.06, -0.16):
        cyl('foot.' + sd, ST, (x, y, 0.072), (x, y, 0.03), 0.014, 0.012, seg=8)
    box('foot.' + sd, ST, (x, -0.05, 0.022), (0.014, 0.84, 0.034))
    box('foot.' + sd, MO, (x, -0.05, 0.0035), (0.017, 0.78, 0.007))
    torus('foot.' + sd, ST, (x, -0.47, 0.06), 0.045, 0.009, axis=(1, 0, 0), seg=10, seg2=4, arc=0.35)

# split hard edges so flat caps shade flat and curved parts shade smooth (no auto smooth in glTF)
bm.normal_update()
hard = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(48)]
bmesh.ops.split_edges(bm, edges=hard)
me = bpy.data.meshes.new('CrucibleSkater')
bm.to_mesh(me); bm.free()
body = bpy.data.objects.new('CrucibleSkater', me); bpy.context.scene.collection.objects.link(body)
for n in BONES: body.vertex_groups.new(name=n)
for n, (col, metal, rough, emit) in MATS.items(): me.materials.append(make_material(n, col, metal, rough, emit))
for p in me.polygons: p.use_smooth = p.use_smooth
print('TRIS', sum(len(p.vertices) - 2 for p in me.polygons), 'VERTS', len(me.vertices))

# ---------------- rig ----------------
class SkaterRig(Rig):
    def assign_groups(self):
        self.body.parent = self.arm; self.body.matrix_parent_inverse = self.arm.matrix_world.inverted()
        am = self.body.modifiers.new('Armature', 'ARMATURE'); am.object = self.arm
    def apply(self, ch):
        pbs = self.pbs
        for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
        for n, e in ch.get('rot', {}).items():
            M = pbs[n].bone.matrix_local.to_3x3(); pbs[n].rotation_quaternion = (M.inverted() @ R(*e) @ M).to_quaternion()
        for n, v in ch.get('loc', {}).items():
            M = pbs[n].bone.matrix_local.to_3x3(); pbs[n].location = M.inverted() @ Vector(v)
        bpy.context.view_layer.update()
        for (up, lo), (target, pole) in ch.get('ik', {}).items(): self.ik2(up, lo, target, pole)
        for n, d in ch.get('aim', {}).items(): self.aim(n, d)
        # arms: wrist targets relative to the shoulder in the posed spine's frame
        bpy.context.view_layer.update()
        sp = pbs['spine']; Rsp = sp.matrix.to_3x3() @ sp.bone.matrix_local.to_3x3().inverted()
        for sd, (off, pole, hand) in ch.get('arms', {}).items():
            sh = pbs['upperarm.' + sd].head.copy()
            tgt = ch.get('wabs', {}).get(sd)
            tgt = Vector(tgt) if tgt is not None else sh + Rsp @ Vector(off)
            self.ik2('upperarm.' + sd, 'forearm.' + sd, tgt, Rsp @ Vector(pole))
            self.aim('hand.' + sd, Rsp @ Vector(hand))
        # the crucible: on the head, in both hands (carry) or dropped on the floor (drop)
        bpy.context.view_layer.update()
        cb = pbs['crucible']; hb = pbs['head']
        rel = hb.bone.matrix_local.inverted() @ cb.bone.matrix_local
        tilt = Euler([math.radians(a) for a in ch.get('ctilt', (0, 0, 0))]).to_matrix().to_4x4()
        L0, Q0, _ = (hb.matrix @ rel).decompose()
        zt = Q0 @ Vector((0, 1, 0)); qs = Vector((0, 0, 1)).rotation_difference(zt)
        Q1 = Quaternion().slerp(qs, 1 - ch.get('level', 0.7)) @ qs.inverted() @ Q0
        Mh = Matrix.Translation(L0) @ Q1.to_matrix().to_4x4() @ tilt
        M = Mh
        carry = ch.get('carry', 0.0)
        if carry > 0:
            g = (pbs['hand.L'].head + pbs['hand.L'].tail + pbs['hand.R'].head + pbs['hand.R'].tail) / 4
            Rc = Rsp @ Euler([math.radians(a) for a in ch.get('ctiltc', (0, 0, 0))]).to_matrix()
            Mc = Matrix.Translation(g - Rc @ Vector((0, 0, 0.115))) @ (Rc @ cb.bone.matrix_local.to_3x3()).to_4x4()
            M = blend(Mh, Mc, carry)
        drop = ch.get('drop', 0.0)
        if drop > 0:
            loc, rot = ch['dropat']
            Md = Matrix.Translation(loc) @ (Euler([math.radians(a) for a in rot]).to_matrix() @ cb.bone.matrix_local.to_3x3()).to_4x4()
            M = blend(M, Md, drop)
        cb.matrix = M
def blend(A, Bm, u):
    la, ra, _ = A.decompose(); lb, rb, _ = Bm.decompose()
    return Matrix.Translation(la.lerp(lb, u)) @ ra.slerp(rb, u).to_matrix().to_4x4()

rig = SkaterRig('CrucibleSkater', B, ())
rig.assign_groups()

# ---------------- pose parameters ----------------
FD = nrm((0, -0.15, -0.065))
REST = dict(hip=(0, 0, 0), hr=(0, 0, 0), sr=(0, 0, 0), hd=(0, 0, 0), root=(0, 0, 0), rootloc=(0, 0, 0),
            aL=(0.12, 0, 0.15), aR=(-0.12, 0, 0.15), fL=FD, fR=FD, kL=(0, -1, 0.15), kR=(0, -1, 0.15),
            wL=(0.205, -0.03, -0.505), wR=(-0.205, -0.03, -0.505), pL=(1, 0.6, 0), pR=(-1, 0.6, 0),
            hL=(0.3, -0.25, -1), hR=(-0.3, -0.25, -1), flare=0.0, carry=0.0, level=0.7, ctilt=(0, 0, 0), ctiltc=(0, 0, 0), drop=0.0)
def P(**kw):
    p = dict(REST); p.update(kw); return p
def lerp(a, b, u):
    if isinstance(a, tuple): return tuple(x + (y - x) * u for x, y in zip(a, b))
    return a + (b - a) * u
def mix(a, b, u): return {k: lerp(a[k], b[k], u) for k in a}
def seq(t, pts):
    # pts: [(time, pose)], smooth between keys
    if t <= pts[0][0]: return dict(pts[0][1])
    for (t0, p0), (t1, p1) in zip(pts, pts[1:]):
        if t <= t1: return mix(p0, p1, smooth((t - t0) / max(1e-6, t1 - t0)))
    return dict(pts[-1][1])
def addp(p, **kw):
    q = dict(p)
    for k, v in kw.items(): q[k] = add(q[k], v) if isinstance(v, tuple) else q[k] + v
    return q
def setp(p, **kw):
    q = dict(p); q.update(kw)
    return q
def mirror_pose(p):
    m = lambda v: (-v[0], v[1], v[2]); r = lambda e: (e[0], -e[1], -e[2])
    q = dict(p)
    for k in ('hip', 'rootloc'): q[k] = m(p[k])
    for k in ('hr', 'sr', 'hd', 'root', 'ctilt', 'ctiltc'): q[k] = r(p[k])
    for a, b in (('aL', 'aR'), ('fL', 'fR'), ('kL', 'kR'), ('wL', 'wR'), ('pL', 'pR'), ('hL', 'hR')):
        q[a], q[b] = m(p[b]), m(p[a])
    return q
def flare_rot(i, deg):
    a = 2 * math.pi * (i + 0.5) / NP; axis = Vector((-math.sin(a), math.cos(a), 0))
    e = Matrix.Rotation(math.radians(-deg), 3, axis).to_euler('XYZ'); return tuple(math.degrees(x) for x in e)
DROPAT = ((0.55, -0.40, 0.21), (174, 0, 20))
def build(p):
    Mroot = Matrix.Translation(p['rootloc']) @ R(*p['root']).to_4x4()
    Rr = R(*p['root'])
    ch = {'rot': {'root': p['root'], 'hips': p['hr'], 'spine': p['sr'], 'head': p['hd']},
          'loc': {'root': p['rootloc'], 'hips': p['hip']}, 'ik': {}, 'aim': {}, 'arms': {},
          'carry': p['carry'], 'level': p['level'], 'ctilt': p['ctilt'], 'ctiltc': p['ctiltc'], 'drop': p['drop'], 'dropat': DROPAT}
    for s in 'LR':
        ch['ik'][('thigh.' + s, 'shin.' + s)] = (tuple(Mroot @ Vector(p['a' + s])), tuple(Rr @ Vector(p['k' + s])))
        ch['aim']['foot.' + s] = tuple(Rr @ Vector(p['f' + s]))
        ch['arms'][s] = (p['w' + s], p['p' + s], p['h' + s])
    if p['flare']:
        for i in range(NP): ch['rot'][f'petal.{i}'] = flare_rot(i, p['flare'])
    return ch
def yawv(v, deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg)); return (v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2])

# ---------------- poses ----------------
ARMS_IDLE = dict(wL=(0.24, -0.07, -0.43), wR=(-0.24, -0.07, -0.43), hL=(0.5, -0.3, -1), hR=(-0.5, -0.3, -1))
IDLE = P(hip=(0, 0, -0.06), aL=(0.13, -0.02, 0.15), aR=(-0.13, 0.02, 0.15), flare=3, **ARMS_IDLE)
TUCK = P(hip=(0, 0.06, -0.22), hr=(54, 0, 0), sr=(4, 0, 0), hd=(-24, 0, 0), aL=(0.15, 0.06, 0.15), aR=(-0.15, 0.06, 0.15),
         kL=(0.2, -1, 0), kR=(-0.2, -1, 0), wL=(0.12, 0.34, -0.37), wR=(-0.12, 0.34, -0.37), pL=(1, 0, 0.3), pR=(-1, 0, 0.3),
         hL=(0, 0.6, -1), hR=(0, 0.6, -1), flare=4)
ARAB = P(hip=(-0.05, 0.0, -0.03), hr=(28, 0, 0), sr=(-14, 0, 0), hd=(-14, 0, 0), aL=(0.10, 0.74, 0.80), aR=(-0.07, -0.02, 0.15),
         fL=nrm((0, 1, 0.0)), kL=(0, 0, -1), wR=(-0.02, -0.60, 0.10), pR=(-1, 0, -0.3), hR=(0, -1, 0),
         wL=(0.36, 0.30, -0.18), pL=(1, 0, 0), hL=(0.6, 0.6, -0.2), flare=6)
CROUCH = P(hip=(0, 0.06, -0.31), hr=(52, 0, 0), sr=(16, 0, 0), hd=(-50, 0, 0), aL=(0.16, 0.06, 0.15), aR=(-0.16, 0.06, 0.15),
           kL=(0.25, -1, 0), kR=(-0.25, -1, 0), wL=(0.15, 0.22, -0.43), wR=(-0.15, 0.22, -0.43), pL=(1, 0, 0.3), pR=(-1, 0, 0.3),
           hL=(0, 0.5, -1), hR=(0, 0.5, -1), flare=4)
CHECK = P(hip=(0, 0, -0.18), hr=(22, 0, -32), sr=(14, 15, -38), hd=(-16, 0, 56), aL=(0.24, 0.10, 0.15), aR=(-0.20, -0.12, 0.15),
          kL=(0.6, -1, 0), kR=(-0.6, -1, 0), wL=(0.10, -0.15, -0.20), wR=(-0.08, -0.17, -0.18), pL=(1, 0.3, 0.2), pR=(-1, 0.3, 0.2),
          hL=(-0.3, -1, 0.3), hR=(0.3, -1, 0.3), flare=4)
CHARGE = P(hip=(0, 0.03, -0.22), hr=(50, 0, -32), sr=(14, 12, -34), hd=(-30, 0, 50), ctilt=(18, 0, 0), level=0.4, aL=(0.18, 0.06, 0.15), aR=(-0.18, -0.02, 0.15),
           kL=(0.4, -1, 0), kR=(-0.4, -1, 0), wL=(0.14, -0.20, -0.14), wR=(-0.10, -0.22, -0.14), pL=(1, 0.2, 0.5), pR=(-1, 0.2, 0.5),
           hL=(0, -1, 0.4), hR=(0.3, -1, 0.4), flare=4)
PITCH = P(hip=(0, -0.10, -0.12), hr=(40, 0, 0), sr=(4, 0, 0), hd=(-14, 0, 0), aL=(0.14, 0.18, 0.22), aR=(-0.12, 0.10, 0.15),
          wL=(0.30, 0.30, 0.30), wR=(-0.30, 0.30, 0.30), pL=(1, 0, 0), pR=(-1, 0, 0), hL=(0.4, 0.6, 0.6), hR=(-0.4, 0.6, 0.6), ctilt=(28, 0, 0), level=0.2, flare=6)
STUCK = P(hip=(0, 0.02, -0.38), hr=(20, -18, 10), sr=(12, 0, 4), hd=(-6, 14, 0), aL=(0.42, -0.14, 0.15), aR=(-0.13, 0.36, 0.06),
          fL=nrm((1, -0.4, -0.15)), fR=nrm((0, 1, -0.05)), kL=(0.5, -1, 0.6), kR=(0, -1, -0.2),
          wL=(0.26, -0.22, -0.36), pL=(1, 0, 0), hL=(0.3, -0.5, -1), wR=(-0.2, -0.3, -0.4), pR=(-1, 0.5, 0), hR=(0, 0, -1),
          ctilt=(8, -24, 0), level=0.3, flare=3)
STUCK_WR = (-0.46, -0.16, 0.06)
GRIP_HEAD = dict(wL=(0.09, 0.0, 0.355), wR=(-0.09, 0.0, 0.355), pL=(1, 0.4, -0.2), pR=(-1, 0.4, -0.2), hL=(0, 0, 1), hR=(0, 0, 1))
OVER = P(hip=(0, 0, -0.03), sr=(-8, 0, 0), hd=(-6, 0, 0), aL=(0.14, 0.02, 0.15), aR=(-0.14, -0.02, 0.15),
         wL=(0.09, 0.03, 0.51), wR=(-0.09, 0.03, 0.51), pL=(1, 0.3, 0), pR=(-1, 0.3, 0), hL=(0, 0, 1), hR=(0, 0, 1), carry=1.0, flare=4)
FLICK = P(hip=(0, -0.04, -0.07), hr=(10, 0, 0), sr=(14, 0, 0), hd=(6, 0, 0), aL=(0.16, -0.04, 0.15), aR=(-0.14, 0.06, 0.15),
          wL=(0.09, -0.38, 0.32), wR=(-0.09, -0.38, 0.32), pL=(1, 0.3, -0.5), pR=(-1, 0.3, -0.5), hL=(0, -1, 0.6), hR=(0, -1, 0.6),
          carry=1.0, ctiltc=(26, 0, 0), flare=4)
LAPL = P(hip=(0.10, 0.02, -0.11), hr=(16, 19, 6), sr=(6, 6, 0), hd=(-10, -14, 0), wL=(0.08, 0.10, -0.44), wR=(-0.46, -0.16, -0.06),
         pL=(1, 0.3, 0), pR=(-1, 0, -0.5), hL=(0, 0.3, -1), hR=(-1, -0.2, 0), flare=10)
SPIN = P(hip=(0.0, 0, 0.0), aR=(-0.03, 0.0, 0.15), aL=(-0.04, -0.12, 0.36), fL=nrm((0, -0.25, -1)), kL=(0.7, -1, 0),
         wL=(-0.06, -0.16, -0.12), wR=(0.06, -0.16, -0.12), pL=(1, 0.3, 0), pR=(-1, 0.3, 0), hL=(-1, -0.3, 0.3), hR=(1, -0.3, 0.3), flare=25)
DIZZY = P(hip=(0, 0, -0.10), hr=(8, 0, 0), aL=(0.16, 0.0, 0.15), aR=(-0.16, 0.0, 0.15), kL=(-0.4, -1, 0), kR=(0.4, -1, 0),
          wL=(0.30, -0.10, -0.38), wR=(-0.30, -0.10, -0.38), pL=(1, -0.3, 0.6), pR=(-1, -0.3, 0.6), hL=(0.2, 0, -1), hR=(-0.2, 0, -1), flare=3)
REHEAT = P(hip=(0, 0, 0.055), sr=(-10, 0, 0), hd=(-25, 0, 0), level=0.1, aL=(0.13, 0.0, 0.205), aR=(-0.13, 0.0, 0.205), fL=nrm((0, -0.35, -1)),
           fR=nrm((0, -0.35, -1)), wL=(0.42, -0.02, 0.26), wR=(-0.42, -0.02, 0.26), pL=(0, 1, 0), pR=(0, 1, 0), hL=(1, 0, 0.4), hR=(-1, 0, 0.4), flare=18)
LIE = P(root=(-84, 0, 6), rootloc=(0, 0.10, 0.09), hd=(10, 12, 0), aL=(0.24, -0.05, 0.28), aR=(-0.26, 0.0, 0.32),
        fL=nrm((0.6, 0.1, -1)), fR=nrm((-0.6, 0.1, -1)), kL=(1, -0.3, 0), kR=(-1, -0.3, 0), wL=(0.40, 0.05, 0.05), wR=(-0.36, -0.10, -0.25), pL=(0, 1, 0), pR=(0, 1, 0),
        hL=(1, 0, 0), hR=(-1, 0, -0.5), drop=1.0, flare=12)

# ---------------- clips ----------------
def idle(t):
    s = math.sin(2 * math.pi * t / 3.0)
    return build(addp(IDLE, hip=(0.035 * s, 0, 0.008 * math.sin(4 * math.pi * t / 3.0)), hr=(0, 3 * s, 0), sr=(0, -2 * s, 0), hd=(0, -1.5 * s, 0),
                      wL=(0.02 * s, 0, 0), wR=(0.02 * s, 0, 0)))
def strides(base, t, per, amp, lift, swing):
    p = 2 * math.pi * t / per; s = math.sin(p)
    q = dict(base)
    for sd, sg in (('L', 1), ('R', -1)):
        ph = s * sg; push, rec = max(0, ph), max(0, -ph)
        a = base['a' + sd]
        q['a' + sd] = (a[0] + sg * amp * push, a[1] + 0.9 * amp * push - 0.3 * amp * rec, a[2] + lift * rec)
        q['f' + sd] = nrm(yawv(base['f' + sd], sg * 32 * push))
    q = addp(q, hip=(-0.05 * s, 0, 0.012 * math.cos(2 * p)), hr=(0, -4 * s, 4 * s), sr=(0, 0, -5 * s), hd=(0, 3 * s, 2 * s))
    q['wL'] = add(q['wL'], (-swing * s, 0, 0)); q['wR'] = add(q['wR'], (-swing * s, 0, 0))
    return q
WALK = P(hip=(0, 0.02, -0.14), hr=(20, 0, 0), sr=(4, 0, 0), hd=(-14, 0, 0), aL=(0.12, 0.03, 0.15), aR=(-0.12, 0.03, 0.15),
         kL=(0.2, -1, 0), kR=(-0.2, -1, 0), wL=(0.22, -0.10, -0.40), wR=(-0.22, -0.10, -0.40), flare=6)
def walk(t): return build(strides(WALK, t, 1.0, 0.30, 0.06, 0.30))
def carve_track(t):
    s = math.sin(2 * math.pi * t / 1.0)
    return build(addp(ARAB, hr=(0, 2.5 * s, 0), hip=(0, 0, 0.01 * s), wR=(0, 0, 0.02 * s), aL=(0, 0, 0.04 * s)))
def carve_crouch(t):
    return build(seq(t, [(0, ARAB), (0.45, CROUCH), (0.9, addp(CROUCH, hip=(0, 0.01, -0.02)))]))
def carve_loop(t):
    p = 2 * math.pi * t / 0.5; s = math.sin(p)
    q = strides(TUCK, t, 0.5, 0.22, 0.05, 0.0)
    q['wL'] = add(TUCK['wL'], (0, -0.32 * max(0, s), -0.2 * max(0, s))); q['hL'] = nrm(add(TUCK['hL'], (0, -1.5 * max(0, s), -0.5)))
    q['wR'] = add(TUCK['wR'], (0, -0.32 * max(0, -s), -0.2 * max(0, -s))); q['hR'] = nrm(add(TUCK['hR'], (0, -1.5 * max(0, -s), -0.5)))
    return build(q)
STOP = P(hip=(0, 0, -0.40), hr=(8, -20, 0), sr=(6, 0, 0), hd=(-22, 10, 0), root=(0, 0, 85),
         aL=(0.22, -0.02, 0.15), aR=(-0.18, 0.04, 0.15), fL=nrm(yawv(FD, 0)), fR=nrm(yawv(FD, 0)), kL=(0.3, -1, 0), kR=(-0.3, -1, 0),
         wL=(0.50, -0.06, 0.02), wR=(-0.50, -0.06, 0.02), pL=(0, 1, 0), pR=(0, 1, 0), hL=(1, 0, 0), hR=(-1, 0, 0), flare=16)
def carve_turn(t):
    return build(seq(t, [(0, TUCK), (0.08, STOP), (0.16, addp(STOP, hip=(0, 0, 0.05))), (0.35, TUCK)]))
def check_windup(t):
    return build(seq(t, [(0, IDLE), (0.25, addp(CHECK, hip=(0, 0, 0.05))), (0.55, addp(CHECK, hip=(0, 0, -0.03))), (0.8, CHECK)]))
def check_loop(t):
    s = math.sin(2 * math.pi * t / 0.8)
    q = addp(CHECK, hr=(2.5 * s, 0, 0), sr=(0, 0, 2 * s), hip=(0, 0, 0.012 * s))
    for sd, t0 in (('L', 0.15), ('R', 0.55)):
        k = snap(t, t0, 0.08, 0.14)
        q['a' + sd] = add(q['a' + sd], (0, -0.04 * k, 0.06 * k)); q['f' + sd] = nrm(add(q['f' + sd], (0, 0, -0.9 * k)))
    return build(q)
def charge_loop(t): return build(strides(CHARGE, t, 0.4, 0.16, 0.08, 0.03))
def trip(t):
    q = seq(t, [(0, CHECK), (0.15, PITCH), (0.32, addp(PITCH, hip=(0, 0, -0.18), hr=(-14, 0, 0))), (0.6, STUCK)])
    q['carry'] = 0; q = addp(q, ctilt=(ring(t, 0.15, 18, 2.2, 3.5), ring(t, 0.3, -10, 1.8, 3), 0))
    ch = build(q)
    if t > 0.32: ch['wabs'] = {'R': tuple(Vector(STUCK_WR) * smooth((t - 0.32) / 0.28) + Vector((-0.3, -0.3, 0.5)) * (1 - smooth((t - 0.32) / 0.28)))}
    return ch
def stuck_loop(t):
    u = key(t, [(0, 0), (0.45, 1), (0.65, 1), (0.9, 0), (1.2, 0)])
    q = addp(STUCK, hip=(0, -0.02 * u, 0.09 * u + 0.012 * ring(t, 0.9, 1, 2.5, 4)), hr=(-8 * u, 0, 0), ctilt=(6 * math.sin(2 * math.pi * t / 0.6), 10 * math.sin(2 * math.pi * t / 1.2), 0))
    ch = build(q); ch['wabs'] = {'R': STUCK_WR}; return ch
def toss_raise(t):
    grip = P(hip=(0, 0, -0.04), aL=(0.14, 0.02, 0.15), aR=(-0.14, -0.02, 0.15), flare=4, **GRIP_HEAD)
    q = seq(t, [(0, IDLE), (0.22, grip), (0.6, OVER)])
    q['carry'] = key(t, [(0, 0), (0.2, 0), (0.26, 1), (0.6, 1)])
    return build(q)
def toss_loop(t):
    a = 2 * math.pi * t / 1.0; c, s = math.cos(a), math.sin(a)
    q = addp(OVER, wL=(0.10 * c, 0.10 * s, -0.02 + 0.01 * s), wR=(0.10 * c, 0.10 * s, -0.02 + 0.01 * s), sr=(-4 * s, 4 * c, 0), ctiltc=(14 * s, -14 * c, 0), hip=(0.015 * c, 0, 0))
    return build(q)
def toss_flick(t):
    back = addp(OVER, sr=(-8, 0, 0), wL=(0, 0.10, -0.02), wR=(0, 0.10, -0.02), ctiltc=(-12, 0, 0))
    home = P(hip=(0, 0, -0.04), aL=(0.14, 0.02, 0.15), aR=(-0.14, -0.02, 0.15), flare=4, carry=1.0, **GRIP_HEAD)
    q = seq(t, [(0, OVER), (0.12, back), (0.22, FLICK), (0.36, home), (0.5, addp(IDLE, carry=0.0))])
    q['carry'] = key(t, [(0, 1), (0.38, 1), (0.42, 0), (0.5, 0)])
    return build(q)
def lap(t, base):
    p = 2 * math.pi * t / 0.8; c = 0.5 - 0.5 * math.cos(p); s = math.sin(p)
    q = dict(base)
    q['aR'] = (-0.22 + 0.46 * c, -0.12 * s, 0.15 + 0.12 * max(0, s))
    q['aL'] = (0.22 - 0.16 * c, 0.10 * s + 0.08 * c, 0.15)
    q['fR'] = nrm(yawv(FD, 20 * c)); q['fL'] = nrm(yawv(FD, -15 * c))
    q = addp(q, hip=(0.02 * c, 0, 0.012 * math.cos(2 * p)), hr=(0, 0, -4 * s), wR=(0, -0.06 * s, 0.03 * s))
    return q
def lap_l(t): return build(lap(t, LAPL))
def lap_r(t): return build(mirror_pose(lap(t, LAPL)))
def pirouette_loop(t):
    p = 2 * math.pi * t / 0.5
    return build(addp(SPIN, flare=3 * math.sin(2 * p), hr=(0.8 * math.sin(p), 0.8 * math.cos(p), 0), hip=(0, 0, 0.004 * math.sin(2 * p))))
def dizzy_loop(t):
    p = 2 * math.pi * t / 1.2
    q = addp(DIZZY, hr=(12 * math.sin(p), 16 * math.cos(p), 0), sr=(14 * math.sin(p + 1), 18 * math.cos(p + 1), 0),
             hd=(22 * math.sin(p + 2), 30 * math.cos(p + 2), 0), hip=(0.09 * math.cos(p), 0.05 * math.sin(p), -0.05 + 0.05 * math.sin(2 * p)),
             aL=(0.10 * math.sin(2 * p), 0.40 * math.sin(p), 0), aR=(-0.08 * math.sin(2 * p + 1), -0.10 * math.sin(p), 0),
             wL=(0.04 + 0.12 * math.sin(p + 0.5), -0.16 * math.cos(p), 0.06 + 0.08 * math.sin(2 * p)), wR=(-0.04 + 0.12 * math.sin(p + 2), 0.16 * math.cos(p + 1), 0.06 + 0.08 * math.cos(2 * p)),
             ctilt=(18 * math.sin(p + 2.6), 20 * math.cos(p + 2.6), 0), flare=3 * math.sin(2 * p), level=0.0)
    return build(q)
def reheat(t):
    q = seq(t, [(0, IDLE), (0.5, REHEAT), (1.5, addp(REHEAT, hip=(0, 0, 0.01))), (2.0, IDLE)])
    k = key(t, [(0, 0), (0.5, 1), (1.5, 1), (2.0, 0)])
    q = addp(q, hd=(0, 4 * k * math.sin(2 * math.pi * t * 3), 0), flare=6 * k * math.sin(2 * math.pi * t * 2))
    return build(q)
def defeat(t):
    w = max(0.0, 1 - t / 1.1)
    p = 2 * math.pi * t / 0.7
    wob = addp(DIZZY, hr=(10 * w * math.sin(p), 14 * w * math.cos(p), 0), sr=(8 * w * math.sin(p + 1), 10 * w * math.cos(p + 1), 0),
               hd=(10 * w * math.sin(p + 2), 14 * w * math.cos(p + 2), 0))
    q = seq(t, [(0, wob), (1.0, wob), (1.8, LIE), (2.5, LIE)]) if t > 1.0 else wob
    q['drop'] = key(t, [(0, 0), (0.85, 0), (1.35, 1), (2.5, 1)])
    q['root'] = add(q['root'], (ring(t, 1.8, 3.5, 2.2, 4), 0, 0))
    return build(q)

CLIPS = [('Idle', 3.0, idle), ('Walk', 1.0, walk), ('CarveTrack', 1.0, carve_track), ('CarveCrouch', 0.9, carve_crouch),
         ('CarveLoop', 0.5, carve_loop), ('CarveTurn', 0.35, carve_turn), ('CheckWindup', 0.8, check_windup),
         ('CheckLoop', 0.8, check_loop), ('ChargeLoop', 0.4, charge_loop), ('Trip', 0.6, trip), ('StuckLoop', 1.2, stuck_loop),
         ('TossRaise', 0.6, toss_raise), ('TossLoop', 1.0, toss_loop), ('TossFlick', 0.5, toss_flick),
         ('LapLoopL', 0.8, lap_l), ('LapLoopR', 0.8, lap_r), ('PirouetteLoop', 0.5, pirouette_loop),
         ('DizzyLoop', 1.2, dizzy_loop), ('Reheat', 2.0, reheat), ('Defeat', 2.5, defeat)]

if TEX:
    import crucible_skater_tex as T
    T.apply_textures(body, TEX)

for name, sec, fn in CLIPS: rig.bake(name, sec, fn)
# export like miniboss_rig_lib.Rig.export, with a lower JPEG quality so the textured GLB stays under 3.5 MB
QUALITY = int(os.environ.get('SKATER_JPEG', '82'))
rig.arm.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'crucible_skater_rig.blend'))
bpy.ops.object.select_all(action='DESELECT'); rig.arm.select_set(True); body.select_set(True)
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=QUALITY)
print('CLIPS', [a.name for a in bpy.data.actions]); print('EXPORTED', out_glb, os.path.getsize(out_glb))
print('TRIS_FINAL', sum(len(p.vertices) - 2 for p in body.data.polygons))
