# blender -b vanguard_prep.blend -P rig.py -- <work dir> <out.glb>
# Rigs the prepared Vanguard, adds the sword, keys idle/run/attack/dodge/jump and exports a GLB.
# Axes: -Y forward, +X = character's left (shield side), Z up. Rotations below are degrees in rest-armature axes:
#   X: negative swings a limb forward, positive bends a knee / leans the torso forward. Y: +abducts the right arm.
#   Z: + turns the torso toward the character's left.
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Matrix, Euler
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Vanguard']
CX = -0.03

# ---------------- armature ----------------
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.25, 0), None, False),
    'hips': ((CX, 0, 0.95), (CX, 0, 1.05), 'root', True),
    'spine': ((CX, 0, 1.05), (CX, 0, 1.2), 'hips', True),
    'chest': ((CX, 0, 1.2), (CX, 0, 1.42), 'spine', True),
    'neck': ((CX, -0.01, 1.42), (CX, -0.02, 1.52), 'chest', True),
    'head': ((CX, -0.02, 1.52), (CX, -0.02, 1.78), 'neck', True),
    'upperarm.R': ((-0.27, 0, 1.34), (-0.38, -0.01, 1.1), 'chest', True),
    'forearm.R': ((-0.38, -0.01, 1.1), (-0.41, -0.03, 0.86), 'upperarm.R', True),
    'hand.R': ((-0.41, -0.03, 0.86), (-0.42, -0.04, 0.76), 'forearm.R', True),
    'upperarm.L': ((0.21, 0, 1.34), (0.3, -0.01, 1.1), 'chest', True),
    'forearm.L': ((0.3, -0.01, 1.1), (0.32, -0.03, 0.86), 'upperarm.L', True),
    'hand.L': ((0.32, -0.03, 0.86), (0.33, -0.04, 0.76), 'forearm.L', True),
    'thigh.R': ((-0.14, 0, 0.93), (-0.22, -0.02, 0.5), 'hips', True),
    'shin.R': ((-0.22, -0.02, 0.5), (-0.27, 0.03, 0.1), 'thigh.R', True),
    'foot.R': ((-0.27, 0.03, 0.1), (-0.28, -0.16, 0.03), 'shin.R', True),
    'thigh.L': ((0.08, 0, 0.93), (0.15, -0.02, 0.5), 'hips', True),
    'shin.L': ((0.15, -0.02, 0.5), (0.2, 0.03, 0.1), 'thigh.L', True),
    'foot.L': ((0.2, 0.03, 0.1), (0.21, -0.16, 0.03), 'shin.L', True),
    'cape1': ((CX, 0.17, 1.3), (CX, 0.23, 0.85), 'chest', True),
    'cape2': ((CX, 0.23, 0.85), (CX, 0.3, 0.35), 'cape1', True),
}
ad = bpy.data.armatures.new('VanguardRig'); arm = bpy.data.objects.new('VanguardRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: heat-weight a watertight proxy, transfer, then fix shield / cape / tabard ----------------
bpy.ops.object.select_all(action='DESELECT')
proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'proxy'; sc.collection.objects.link(proxy)
rm = proxy.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.012
bpy.context.view_layer.objects.active = proxy; bpy.ops.object.modifier_apply(modifier='rm')
for c in ('cape1', 'cape2'): ad.bones[c].use_deform = False
proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
for c in ('cape1', 'cape2'): ad.bones[c].use_deform = True
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
nv = len(body.data.vertices); W = np.zeros((nv, len(names)), np.float32)
for v in body.data.vertices:
    for g in v.groups: W[v.index, g.group] = g.weight
co = np.empty(nv * 3); body.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
x, y, z = co[:, 0], co[:, 1], co[:, 2]
cl = lambda a: np.clip(a, 0, 1)
def blend_to(mask, target_row):
    W[:] = W * (1 - mask[:, None]) + target_row * mask[:, None]
def row(**kw):
    r = np.zeros(len(names), np.float32)
    for k, v in kw.items(): r[gi[k.replace('_', '.')]] = v
    return r
# shield (outboard of the left arm) rides the left forearm rigidly
shield = ((x > 0.345) & (z > 0.3) & (z < 1.22)).astype(np.float32)
W[shield > 0] = row(forearm_L=1)
# cape: behind the back, blended in by depth; split chest -> cape1 -> cape2 by height
capem = cl((y - 0.11) / 0.05) * cl((1.22 - z) / 0.12) * (np.abs(x - CX) < 0.32)
t = cl((0.95 - z) / 0.5)          # 0 near shoulders .. 1 at the hem
up = cl((z - 1.0) / 0.2)
cape_rows = np.stack([np.zeros(nv)] * len(names), 1).astype(np.float32)
cape_rows[:, gi['chest']] = up; cape_rows[:, gi['cape1']] = (1 - up) * (1 - t); cape_rows[:, gi['cape2']] = (1 - up) * t
W[:] = W * (1 - capem[:, None]) + cape_rows * capem[:, None]
# front tabard between the legs follows the hips, not the thighs
tab = cl((-0.1 - y) / 0.04) * (np.abs(x - CX) < 0.1) * (z > 0.3) * (z < 0.82)
blend_to(tab.astype(np.float32), row(hips=1))
# normalise, keep the 4 biggest influences
order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, 1)
W = np.where(keep, W, 0); s = W.sum(1, keepdims=True); W = np.where(s > 0, W / np.maximum(s, 1e-6), 0)
W[s[:, 0] == 0, gi['hips']] = 1
print('UNWEIGHTED_FIXED', int((s[:, 0] == 0).sum()))
for g in body.vertex_groups: g.remove(list(range(nv)))
for j, n in enumerate(names):
    vg = body.vertex_groups[n]; idx = np.nonzero(W[:, j] > 0)[0]
    for i in idx: vg.add([int(i)], float(W[i, j]), 'REPLACE')
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
for m in list(body.modifiers): body.modifiers.remove(m)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- glow tint: cyan, not white ----------------
mat = body.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
emi_node = [n for n in nt.nodes if n.type == 'TEX_IMAGE' and n.image.name.startswith('vanguard_emissive')][0]
ep = np.array(emi_node.image.pixels[:], np.float32).reshape(-1, 4); lum = ep[:, :3].max(1)
ep[:, 0] = lum * 0.35; ep[:, 1] = lum * 0.85; ep[:, 2] = lum * 1.0
emi_node.image.pixels[:] = ep.ravel(); emi_node.image.save()
bsdf.inputs['Emission Strength'].default_value = 2.5

# ---------------- sword: crystal blade, gold guard, navy grip ----------------
def pmat(name, col, metal=0.0, rough=0.5, emit=None, es=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if emit: p.inputs['Emission Color'].default_value = (*emit, 1); p.inputs['Emission Strength'].default_value = es
    return m
m_blade = pmat('sword_blade', (0.04, 0.22, 0.85), 0.0, 0.25, (0.05, 0.3, 1.0), 0.45)
m_rune = pmat('sword_rune', (0.6, 0.95, 1.0), 0.0, 0.3, (0.35, 0.85, 1.0), 6.0)
m_gold = pmat('sword_gold', (0.72, 0.47, 0.17), 0.85, 0.38)
m_grip = pmat('sword_grip', (0.03, 0.04, 0.09), 0.0, 0.7)
import bmesh
def mesh_obj(name, verts, faces, mat):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(mat); return o
def extrude_outline(name, pts, depth, mat, bevel=0.0):
    # pts: 2D outline in the XY plane (x = width, y = length); extruded along Z
    bm = bmesh.new(); vs = [bm.verts.new((px, py, -depth / 2)) for px, py in pts]
    f = bm.faces.new(vs); r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]: v.co.z += depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(mat)
    if bevel:
        bv = o.modifiers.new('bv', 'BEVEL'); bv.width = bevel; bv.segments = 1; bv.limit_method = 'ANGLE'
    return o
L = 0.95
blade = extrude_outline('blade', [(0, 0), (0.05, 0.03), (0.062, 0.12), (0.055, L * 0.78), (0.03, L * 0.92), (0, L), (-0.03, L * 0.92), (-0.055, L * 0.78), (-0.062, 0.12), (-0.05, 0.03)], 0.022, m_blade, 0.008)
# crystal ridge: pull the centre line out so the blade reads faceted
for v in blade.data.vertices:
    pass
parts = [blade]
for i in range(4):  # glowing glyph strips down the fuller
    yy = 0.18 + i * 0.17
    for side in (-1, 1):
        g = extrude_outline(f'rune{i}{side}', [(-0.012, yy), (0.012, yy), (0.012, yy + 0.1), (-0.012, yy + 0.1)], 0.006, m_rune)
        g.location.z = side * 0.013; parts.append(g)
guard = extrude_outline('guard', [(-0.17, -0.01), (-0.13, 0.035), (-0.04, 0.03), (0, 0.06), (0.04, 0.03), (0.13, 0.035), (0.17, -0.01), (0.12, -0.035), (0, -0.03), (-0.12, -0.035)], 0.05, m_gold, 0.01)
gem = extrude_outline('gem', [(0, -0.03), (0.03, 0.0), (0, 0.03), (-0.03, 0)], 0.06, m_rune); gem.location.y = 0.005
grip = extrude_outline('grip', [(-0.018, -0.2), (0.018, -0.2), (0.018, -0.03), (-0.018, -0.03)], 0.036, m_grip, 0.006)
pommel = extrude_outline('pommel', [(0, -0.26), (0.035, -0.225), (0, -0.19), (-0.035, -0.225)], 0.045, m_gold, 0.008)
parts += [guard, gem, grip, pommel]
bpy.ops.object.select_all(action='DESELECT')
for p in parts:
    p.select_set(True)
    bpy.context.view_layer.objects.active = p
    for mmod in list(p.modifiers): bpy.ops.object.modifier_apply(modifier=mmod.name)
bpy.context.view_layer.objects.active = blade; bpy.ops.object.join(); sword = blade; sword.name = sword.data.name = 'VanguardSword'
# sword frame: grip along local -Y.. +Y blade. Hold it in the right fist, blade forward and slightly up.
fist = Vector((-0.415, -0.06, 0.79))
# roll 90 deg about the blade axis so the edges face up/down (flats to the sides), as a fist actually holds a sword
sword.matrix_world = Matrix.Translation(fist) @ (Euler((math.radians(-160), 0, 0)).to_matrix().to_4x4()) @ Matrix.Rotation(math.radians(90), 4, 'Y') @ Matrix.Translation((0, 0.115, 0))
pb_hand = arm.pose.bones['hand.R']
sword.parent = arm; sword.parent_type = 'BONE'; sword.parent_bone = 'hand.R'
bone_world = arm.matrix_world @ ad.bones['hand.R'].matrix_local @ Matrix.Translation((0, ad.bones['hand.R'].length, 0))
sword.matrix_parent_inverse = bone_world.inverted()

# ---------------- animation helpers ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
def Rzyx(x=0, y=0, z=0): return R(z=z) @ R(y=y) @ R(x=x)
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
# sole points (heel, toe) of each foot in rest armature space, used to plant the feet on the floor
SOLE = {n: [Vector((B[n][0][0], 0.06, 0.0)), Vector((B[n][1][0], -0.17, 0.0))] for n in ('foot.L', 'foot.R')}
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        if n.startswith('_'): continue
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
    if '_ground' in pose:
        # ground lock: raise/lower the hips so the lowest sole point sits on the floor (+ optional flight height)
        bpy.context.view_layer.update()
        lowest = min((pbs[n].matrix @ pbs[n].bone.matrix_local.inverted() @ p).z for n, ps in SOLE.items() for p in ps)
        hb = pbs['hips']; M = hb.bone.matrix_local.to_3x3()
        hb.location = hb.location + M.inverted() @ Vector((0, 0, pose['_ground'] - lowest))
def bake(name, frames, fn, loop):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + (1 if loop else 0)):
        set_pose(fn(f, f / frames))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + (1 if loop else 0))
    for fc in getattr(act, 'fcurves', []) or []:
        for k in fc.keyframe_points: k.interpolation = 'LINEAR'
    return act
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value) pairs; values may be tuples
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            return tuple(a + (b - a) * u for a, b in zip(v0, v1)) if isinstance(v0, tuple) else v0 + (v1 - v0) * u
    return pts[-1][1]
def leg(side, thigh, knee, foot=None, hip_pitch=0.0, flat=1.0, spread=0.0):
    # The mesh stands with legs splayed ~10 deg and toes out: bring the leg in under the hip and turn the toes
    # forward (align), then pitch it fore/aft. thigh: - = forward; knee: + = bend. flat: 1 keeps the sole level.
    n = 'L' if side > 0 else 'R'
    align = R(z=-side * 9) @ R(y=side * (7 - spread))
    f = -(hip_pitch + thigh + knee) * flat + (foot or 0.0)
    return {f'thigh.{n}': {'rot': R(x=thigh) @ align}, f'shin.{n}': {'rot': R(x=knee)}, f'foot.{n}': {'rot': R(z=-side * 9) @ R(x=f)}}   # +9 on the thigh = 18: the mesh's feet point 18 deg out
THIGH_LEN, SHIN_LEN, HIP_Z, ANKLE_Z = 0.438, 0.406, 0.93, 0.1
def ik_leg(side, ankle_y, hips_y, hips_z, hip_pitch=0.0, spread=4.0, foot=0.0):
    # 2-bone IK in the sagittal plane: place the ankle at floor height, ankle_y metres along Y (-Y = forward)
    fwd = -(ankle_y - hips_y); down = HIP_Z + hips_z - ANKLE_Z
    d = min(math.hypot(fwd, down), THIGH_LEN + SHIN_LEN - 1e-3)
    a, b = THIGH_LEN, SHIN_LEN
    knee = 180 - math.degrees(math.acos((a * a + b * b - d * d) / (2 * a * b)))
    thigh = -(math.degrees(math.atan2(fwd, down) + math.acos((a * a + d * d - b * b) / (2 * a * d)))) - hip_pitch
    return leg(side, thigh, knee, foot=foot, hip_pitch=hip_pitch, spread=spread)
# a relaxed guard stance shared by idle / recovery: sword forward, shield up, knees soft, toes forward
def stance(breath=0.0):
    s = {
        'upperarm.R': {'rot': Rzyx(x=-18, y=8)}, 'forearm.R': {'rot': Rzyx(x=-35)},
        'upperarm.L': {'rot': Rzyx(x=-12, y=-6)}, 'forearm.L': {'rot': Rzyx(x=-30, z=10)},
        'chest': {'rot': Rzyx(x=3 + breath)}, 'spine': {'rot': Rzyx(x=2)},
        'hips': {'loc': (0, 0, 0)}, '_ground': 0.0,
    }
    s.update(leg(-1, -6, 18, spread=4)); s.update(leg(1, -2, 16, spread=4))
    return s

set_pose(stance()); bpy.context.view_layer.update()
_hb = pbs['hips']; STANCE_HZ = (_hb.bone.matrix_local.to_3x3() @ _hb.location).z
STANCE_ANKLE_Y = {s: pbs[f'foot.{s}'].head.y for s in ('L', 'R')}
print('STANCE', round(STANCE_HZ, 3), {k: round(v, 3) for k, v in STANCE_ANKLE_Y.items()})
def idle(f, t):
    p = 2 * math.pi * t; s = stance(3 * math.sin(p))
    s['hips'] = {'loc': (0.012 * math.sin(p), 0, 0), 'rot': Rzyx(y=1.5 * math.sin(p), z=-4)}
    s['chest'] = {'rot': Rzyx(x=3 + 3 * math.sin(p), z=5)}
    s['upperarm.R'] = {'rot': Rzyx(x=-18 + 2 * math.sin(p + 0.8), y=8)}; s['upperarm.L'] = {'rot': Rzyx(x=-12 + 2 * math.sin(p + 1.2), y=-6)}
    s.update(leg(-1, -5, 20 + 4 * math.sin(p), spread=4)); s.update(leg(1, 4, 8 + 2 * math.sin(p), spread=4))
    s['head'] = {'rot': Rzyx(x=-2 * math.sin(p))}
    s['hand.R'] = {'rot': Rzyx(x=3 * math.sin(p + 1))}; s['forearm.L'] = {'rot': Rzyx(x=-30 + 2 * math.sin(p + 0.5), z=10)}
    s['cape1'] = {'rot': Rzyx(x=2 + 3 * math.sin(p - 0.6))}
    return s
def gait(t, fwd, back, kick, comp, lean, flight, cape, hip_pitch=0.0):
    # Left leg is forward at p = pi/2. Stance runs from forward extreme (contact) back to toe-off, compressing the
    # knee at mid-stance; swing folds the knee early (heel lift) then reaches forward. Hip height comes from the
    # ground lock, so the planted foot never floats or sinks.
    p = 2 * math.pi * t; sp = math.sin(p)
    mid, amp = (fwd - back) / 2, (fwd + back) / 2
    pose = {
        'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=hip_pitch, z=-8 * sp)},
        'spine': {'rot': Rzyx(x=lean)}, 'chest': {'rot': Rzyx(x=lean * 0.3, z=15 * sp)}, 'head': {'rot': Rzyx(x=-(lean + hip_pitch) * 0.7, z=-6 * sp)},
        'cape1': {'rot': Rzyx(x=cape + 5 * math.sin(2 * p))}, 'cape2': {'rot': Rzyx(x=cape * 0.5 + 16 * math.sin(2 * p + 1.6))},
        '_ground': flight * max(0.0, -math.cos(2 * p)),
    }
    for side, ph in ((1, p), (-1, p + math.pi)):
        s, c = math.sin(ph), math.cos(ph)
        thigh = -mid - amp * s
        knee = 24 + comp * max(0.0, -c) ** 1.5 + kick * max(0.0, math.cos(ph + 0.7)) ** 2
        toe_off = 35 * max(0.0, math.cos(ph - 4.65)) ** 3 - 12 * max(0.0, c)
        pose.update(leg(side, thigh, knee, foot=toe_off, hip_pitch=hip_pitch, flat=0.65 + 0.35 * max(0.0, -c), spread=3))
    return pose, sp
def run(f, t):
    pose, sp = gait(t, fwd=34, back=21, kick=85, comp=24, lean=7, flight=0.02, cape=28)
    pose.update({
        'upperarm.R': {'rot': Rzyx(x=-6 - 20 * sp, y=26)}, 'forearm.R': {'rot': Rzyx(x=-34 - 8 * sp)}, 'hand.R': {'rot': Rzyx(x=18 * sp - 12)},
        'upperarm.L': {'rot': Rzyx(x=-22 + 10 * sp, y=-8)}, 'forearm.L': {'rot': Rzyx(x=-55, z=12)},
    })
    return pose
def sprint(f, t):
    # longer stride, deeper lean from the pelvis, sword held forward with a pumping arm, shield tucked to the chest
    pose, sp = gait(t, fwd=42, back=26, kick=110, comp=34, lean=18, flight=0.04, cape=40, hip_pitch=10)
    pose.update({
        'upperarm.R': {'rot': Rzyx(x=-8 - 28 * sp, y=26)}, 'forearm.R': {'rot': Rzyx(x=-48 + 10 * sp)}, 'hand.R': {'rot': Rzyx(x=18 * sp - 12)},
        'upperarm.L': {'rot': Rzyx(x=-30 + 22 * sp, y=-10)}, 'forearm.L': {'rot': Rzyx(x=-75, z=15)},
    })
    return pose
def attack(f, t):
    # quick wind-up with weight back (0-.18), whip across (.18-.32), hold (.32-.5), settle (.5-1).
    # The slash stops in front of the body (sweep 35) so the blade never crosses the shield side.
    tw = key(t, [(0, 0), (0.18, -40), (0.32, 30), (0.5, 26), (0.6, 32), (1, 0)])
    sweep = key(t, [(0, 0), (0.18, -55), (0.32, 35), (0.5, 32), (1, 0)])
    abd = key(t, [(0, 8), (0.18, 80), (0.32, 80), (0.5, 70), (1, 8)])
    elb = key(t, [(0, -35), (0.18, -95), (0.32, -10), (0.5, -15), (1, -35)])
    lunge = key(t, [(0, 0), (0.18, 0), (0.32, 1), (0.5, 1), (0.6, 0.85), (1, 0)])
    back = key(t, [(0, 0), (0.18, 1), (0.3, 0), (1, 0)])
    s = stance(); del s['_ground']
    hy, hz = 0.04 * back - 0.15 * lunge, STANCE_HZ - 0.02 * back - 0.03 * lunge
    s.update({
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(z=tw * 0.4)},
        'spine': {'rot': Rzyx(x=4 + 8 * lunge - 4 * back, z=tw * 0.3)}, 'chest': {'rot': Rzyx(x=3, z=tw * 0.4)},
        'head': {'rot': Rzyx(z=-tw * 0.6)},
        'upperarm.R': {'rot': R(z=sweep) @ Rzyx(y=abd, x=-20)}, 'forearm.R': {'rot': Rzyx(x=elb)},
        'hand.R': {'rot': Rzyx(z=key(t, [(0, 0), (0.18, -30), (0.32, 20), (1, 0)]))},
        'upperarm.L': {'rot': Rzyx(x=-30 + 15 * lunge, y=-10 - 12 * lunge)}, 'forearm.L': {'rot': Rzyx(x=-50 + 10 * lunge, z=10)},
        'cape1': {'rot': Rzyx(x=4 + 12 * lunge, z=-tw * 0.3)},
    })
    # braced split: front (right) knee bends over the foot, back (left) leg straight and driving, both soles flat
    s.update(ik_leg(-1, STANCE_ANKLE_Y['R'] - 0.32 * lunge, hy, hz, spread=4 + 4 * lunge))
    s.update(ik_leg(1, STANCE_ANKLE_Y['L'] + 0.22 * lunge, hy, hz, spread=4 + 4 * lunge))
    return s
# ---------------- Vanguard kit (docs/design/vanguard-v2-implementation.md) ----------------
# Each ability is a table of keyed channels over normalized time; move_pose turns them into a full-body pose with
# both feet planted by IK. Channels (degrees unless noted):
#   tw torso twist (+ = toward the shield side), lean forward, drop hips (m), hy hips forward/back (m, - = forward)
#   sweep sword-arm yaw (- = back on the sword side, + = across the front), abd sword-arm lift out to the side,
#   raise sword-arm pitch (- = forward/up), elb sword elbow (- = bend), wx/wz wrist pitch/twist
#   sx/sy/sz shield-arm pitch/abduct/yaw, sel shield elbow; front right-foot step (m, + = forward),
#   back left-foot step (m, + = backward), spread extra stance width, cape extra cape lift
# Clip lengths follow the 60 Hz tick timings in the report (30 fps here).
def _frame(a, b):
    # orthonormal basis with a as the first axis and b (made perpendicular) as the second, as matrix columns
    a = a.normalized(); b = (b - a * a.dot(b)).normalized()
    return Matrix((a, b, a.cross(b))).transposed()
UPPER_L0 = (Vector(B['upperarm.L'][1]) - Vector(B['upperarm.L'][0])).normalized()
FORE_L0 = (Vector(B['forearm.L'][1]) - Vector(B['forearm.L'][0])).normalized()
SHIELD_N0 = Vector((1, 0, 0))   # the shield faces outward (+X) in the rest pose
def shield_arm(upper_dir, fore_dir, normal):
    # Place the shield arm directly in chest space: upper arm along upper_dir, forearm along fore_dir, shield face
    # toward normal. Returns rotations in set_pose's convention (relative to the parent's posed frame).
    Ru = UPPER_L0.rotation_difference(Vector(upper_dir).normalized()).to_matrix()
    Df = _frame(Vector(fore_dir), Vector(normal)) @ _frame(FORE_L0, SHIELD_N0).transposed()
    return Ru, Ru.inverted() @ Df
# shield-up targets (chest space: -Y forward, +X the shield side): forearm across the chest, face forward
GUARD_ARM = shield_arm((0.3, -0.7, -0.5), (-1, -0.2, 0.15), (0, -1, 0.15))
BASH_ARM = shield_arm((0.15, -0.95, -0.25), (-1, -0.35, 0.05), (0.1, -1, 0.05))
def _blend(a, b, w): return a.to_quaternion().slerp(b.to_quaternion(), max(0.0, min(1.0, w))).to_matrix()
def move_pose(t, K):
    g = lambda n, d=0.0: key(t, K[n]) if n in K else d
    tw, lean, drop, hy = g('tw'), g('lean'), g('drop'), g('hy')
    hz = STANCE_HZ - drop; hp = lean * 0.3
    s = stance(); del s['_ground']
    s.update({
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(x=hp, z=tw * 0.4)},
        'spine': {'rot': Rzyx(x=4 + lean * 0.7, z=tw * 0.3)}, 'chest': {'rot': Rzyx(x=3, z=tw * 0.4)},
        'head': {'rot': Rzyx(x=-lean * 0.6, z=-tw * 0.6)},
        'upperarm.R': {'rot': R(z=g('sweep')) @ Rzyx(y=g('abd', 8), x=g('raise', -18))},
        'forearm.R': {'rot': Rzyx(x=g('elb', -35))}, 'hand.R': {'rot': Rzyx(x=g('wx'), z=g('wz'))},
        'upperarm.L': {'rot': R(z=g('sz')) @ Rzyx(x=g('sx', -12), y=g('sy', -6))}, 'forearm.L': {'rot': Rzyx(x=g('sel', -30), z=10)},
        'cape1': {'rot': Rzyx(x=4 + g('cape'), z=-tw * 0.3)}, 'cape2': {'rot': Rzyx(x=g('cape') * 0.5)},
    })
    guard, bash = g('guard'), g('bash')   # 0..1: shield up in front (guard), pushed out (bash)
    if guard > 0:
        tu, tf = (_blend(GUARD_ARM[0], BASH_ARM[0], bash), _blend(GUARD_ARM[1], BASH_ARM[1], bash))
        s['upperarm.L'] = {'rot': _blend(s['upperarm.L']['rot'], tu, guard)}
        s['forearm.L'] = {'rot': _blend(s['forearm.L']['rot'], tf, guard)}
    sp = 4 + g('spread')
    s.update(ik_leg(-1, STANCE_ANKLE_Y['R'] - g('front'), hy, hz, hip_pitch=hp, spread=sp))
    s.update(ik_leg(1, STANCE_ANKLE_Y['L'] + g('back'), hy, hz, hip_pitch=hp, spread=sp))
    return s
def step_keys(pts, scale=1.0): return [(a, b * scale) for a, b in pts]
KIT = {
    # Clockblade Combo hit 1 (16 ticks): flat forehand from the sword side; stops short of the shield (sweep <= 20),
    # the torso carries the arc and the shield opens out of the way
    'Combo1': (10, {
        'tw': [(0, 0), (0.25, -30), (0.45, 32), (0.65, 28), (1, 0)], 'sweep': [(0, 0), (0.25, -50), (0.45, 20), (0.65, 12), (1, 0)],
        'abd': [(0, 8), (0.25, 75), (0.45, 78), (0.65, 65), (1, 8)], 'elb': [(0, -35), (0.25, -90), (0.45, -12), (0.65, -18), (1, -35)],
        'wz': [(0, 0), (0.25, -30), (0.45, 20), (1, 0)], 'wx': [(0, 0), (0.45, 35), (0.65, 30), (1, 0)],
        'sy': [(0, -6), (0.45, -22), (1, -6)],
        'front': [(0, 0), (0.45, 0.15), (0.7, 0.15), (1, 0)],
        'hy': [(0, 0), (0.45, -0.05), (0.7, -0.05), (1, 0)], 'drop': [(0, 0), (0.45, 0.03), (1, 0)], 'lean': [(0, 0), (0.45, 6), (1, 0)]}),
    # hit 2 (16 ticks): backhand from in front of the face out to the sword side, elbow kept soft, its own small step
    'Combo2': (10, {
        'tw': [(0, 0), (0.25, 30), (0.45, -30), (0.65, -26), (1, 0)], 'sweep': [(0, 0), (0.25, 50), (0.45, -60), (0.65, -52), (1, 0)],
        'abd': [(0, 8), (0.25, 45), (0.45, 65), (0.65, 58), (1, 8)], 'raise': [(0, -18), (0.25, -75), (0.45, -20), (1, -18)],
        'elb': [(0, -35), (0.25, -90), (0.45, -25), (0.65, -28), (1, -35)], 'wz': [(0, 0), (0.25, 35), (0.45, -45), (0.65, -38), (1, 0)],
        'wx': [(0, 0), (0.45, 50), (0.65, 44), (1, 0)],   # lays the blade level along the cut
        'sy': [(0, -6), (0.25, -28), (0.5, -15), (1, -6)], 'back': [(0, 0), (0.45, 0.12), (0.7, 0.12), (1, 0)],
        'front': [(0, 0), (0.45, 0.08), (0.7, 0.08), (1, 0)],
        'hy': [(0, 0), (0.45, -0.04), (1, 0)], 'drop': [(0, 0), (0.45, 0.03), (1, 0)]}),
    # hit 3 (26 ticks): broad flat 170-degree finisher with a big step, hips low, shield held clear until the end
    'Combo3': (14, {
        'tw': [(0, 0), (0.3, -55), (0.5, 38), (0.7, 34), (1, 0)], 'sweep': [(0, 0), (0.3, -75), (0.5, 40), (0.7, 22), (1, 0)],
        'abd': [(0, 8), (0.3, 70), (0.5, 85), (0.7, 70), (1, 8)], 'raise': [(0, -18), (0.3, -5), (0.5, -10), (1, -18)],
        'elb': [(0, -35), (0.3, -80), (0.5, -5), (0.7, -12), (1, -35)], 'wz': [(0, 0), (0.3, -35), (0.5, 25), (1, 0)],
        'wx': [(0, 0), (0.5, 40), (0.7, 35), (1, 0)],
        'drop': [(0, 0), (0.3, 0.04), (0.5, 0.09), (0.7, 0.08), (1, 0)], 'lean': [(0, 0), (0.3, 2), (0.5, 10), (0.7, 10), (1, 0)],
        'front': [(0, 0), (0.3, 0), (0.42, 0.32), (0.75, 0.3), (1, 0)], 'back': [(0, 0), (0.42, 0.18), (0.75, 0.18), (1, 0)],
        'hy': [(0, 0), (0.3, 0.02), (0.5, -0.13), (0.75, -0.12), (1, 0)],
        'sx': [(0, -12), (0.5, 0), (1, -12)], 'sy': [(0, -6), (0.5, -32), (0.85, -28), (1, -6)], 'cape': [(0, 0), (0.55, 14), (1, 0)]}),
    # Dash Attack (20 ticks): out of a dash, low lunge, blade trailing clear of the legs, then a rising diagonal cut
    'DashAttack': (10, {
        'lean': [(0, 18), (0.3, 22), (0.6, 10), (1, 0)], 'drop': [(0, 0.12), (0.3, 0.14), (0.6, 0.06), (1, 0)],
        'front': [(0, 0.3), (0.3, 0.38), (0.7, 0.3), (1, 0)], 'back': [(0, 0.25), (0.6, 0.2), (1, 0)], 'hy': [(0, -0.12), (0.6, -0.1), (1, 0)],
        'tw': [(0, -20), (0.25, -30), (0.5, 30), (1, 0)], 'sweep': [(0, -60), (0.25, -70), (0.5, 25), (0.7, 18), (1, 0)],
        'abd': [(0, 40), (0.25, 50), (0.5, 70), (0.7, 65), (1, 8)], 'raise': [(0, 10), (0.25, 15), (0.5, -75), (0.7, -65), (1, -18)],
        'elb': [(0, -40), (0.25, -60), (0.5, -10), (1, -35)], 'wz': [(0, 0), (0.5, 40), (1, 0)],
        'sx': [(0, -40), (1, -12)], 'sy': [(0, -10), (0.5, -24), (1, -6)], 'cape': [(0, 25), (1, 0)]}),
    # Pendulum Rush (4 + 14 ticks, then the strike): brace, shield-first charge with a bob, short horizontal strike
    'PendulumRush': (21, {
        'tw': [(0, 0), (0.1, -30), (0.48, -35), (0.6, 30), (0.78, 26), (1, 0)], 'lean': [(0, 0), (0.1, 18), (0.48, 22), (0.6, 12), (1, 0)],
        'drop': [(0, 0), (0.1, 0.08), (0.3, 0.13), (0.48, 0.1), (0.6, 0.08), (1, 0)],
        'front': [(0, 0), (0.1, 0.05), (0.35, 0.28), (0.5, 0.32), (0.78, 0.3), (1, 0)],
        'back': [(0, 0), (0.1, 0.1), (0.35, 0.22), (0.5, 0.2), (0.78, 0.2), (1, 0)],
        'hy': [(0, 0), (0.5, -0.1), (0.78, -0.1), (1, 0)],
        'guard': [(0, 0), (0.1, 1), (0.48, 1), (0.6, 0.2), (0.8, 0), (1, 0)], 'sy': [(0, -6), (0.6, -22), (0.85, -20), (1, -6)],
        'sweep': [(0, 0), (0.1, -40), (0.48, -45), (0.6, 22), (0.78, 15), (1, 0)], 'abd': [(0, 8), (0.1, 35), (0.48, 40), (0.6, 80), (0.78, 70), (1, 8)],
        'raise': [(0, -18), (0.1, 30), (0.48, 32), (0.6, -20), (1, -18)], 'wx': [(0, 0), (0.1, 35), (0.48, 35), (0.6, 35), (0.78, 30), (1, 0)],
        'elb': [(0, -35), (0.1, -40), (0.48, -40), (0.6, -10), (0.78, -15), (1, -35)], 'wz': [(0, 0), (0.48, -30), (0.6, 20), (1, 0)],
        'cape': [(0, 0), (0.15, 25), (0.5, 35), (0.7, 15), (1, 0)]}),
    # Reversal Rush: the same charge, a held overhead beat, then a slam into the floor in front (the shockwave)
    'ReversalRush': (24, {
        'tw': [(0, 0), (0.1, -30), (0.42, -35), (0.52, -10), (0.57, -8), (0.63, 5), (0.8, 5), (1, 0)],
        'lean': [(0, 0), (0.1, 18), (0.42, 22), (0.52, 0), (0.57, -4), (0.63, 35), (0.8, 30), (1, 0)],
        'drop': [(0, 0), (0.1, 0.08), (0.42, 0.1), (0.52, 0.02), (0.57, 0.02), (0.63, 0.22), (0.72, 0.19), (0.8, 0.2), (1, 0)],
        'front': [(0, 0), (0.1, 0.05), (0.32, 0.28), (0.45, 0.3), (0.8, 0.3), (1, 0)],
        'back': [(0, 0), (0.1, 0.1), (0.32, 0.22), (0.45, 0.25), (0.8, 0.25), (1, 0)],
        'hy': [(0, 0), (0.45, -0.1), (0.8, -0.12), (1, 0)],
        'guard': [(0, 0), (0.1, 1), (0.42, 1), (0.52, 0), (1, 0)], 'sy': [(0, -6), (0.5, -25), (0.8, -25), (1, -6)],
        'sweep': [(0, 0), (0.1, -40), (0.42, -45), (0.52, -10), (0.63, 22), (1, 0)],
        'abd': [(0, 8), (0.1, 35), (0.42, 40), (0.52, 25), (0.63, 15), (1, 8)],
        'raise': [(0, -18), (0.1, 30), (0.42, 32), (0.52, -170), (0.57, -165), (0.63, -75), (0.8, -72), (1, -18)],
        'elb': [(0, -35), (0.1, -40), (0.42, -40), (0.52, -60), (0.57, -60), (0.63, -20), (0.8, -22), (1, -35)],
        'wx': [(0, 0), (0.1, 35), (0.42, 35), (0.52, -20), (0.57, -20), (0.63, 15), (0.8, 12), (1, 0)],
        'cape': [(0, 0), (0.15, 25), (0.45, 35), (0.6, 5), (0.75, 8), (1, 0)]}),
    # Final Hour (18-tick startup, 48 total): sword up like a clock hand at twelve, drops level on the sword side,
    # then one wide horizontal sweep across the front, a short hold with drift, recover
    'FinalHour': (30, {
        'tw': [(0, 0), (0.3, -28), (0.37, -45), (0.5, 45), (0.62, 42), (0.75, 38), (1, 0)],
        'lean': [(0, 0), (0.3, -6), (0.37, 4), (0.5, 15), (0.62, 14), (0.75, 12), (1, 0)],
        'drop': [(0, 0), (0.3, 0.05), (0.37, 0.08), (0.5, 0.12), (0.75, 0.11), (1, 0)], 'spread': [(0, 0), (0.3, 6), (0.75, 6), (1, 0)],
        'front': [(0, 0), (0.3, 0.1), (0.42, 0.3), (0.75, 0.3), (1, 0)], 'back': [(0, 0), (0.3, 0.15), (0.42, 0.2), (0.75, 0.2), (1, 0)],
        'hy': [(0, 0), (0.3, 0.03), (0.5, -0.1), (0.75, -0.1), (1, 0)],
        'sweep': [(0, 0), (0.3, -20), (0.37, -80), (0.5, 45), (0.62, 40), (0.75, 35), (1, 0)],
        'abd': [(0, 8), (0.3, 40), (0.37, 85), (0.5, 88), (0.75, 80), (1, 8)],
        'raise': [(0, -18), (0.3, -155), (0.37, -90), (0.5, -15), (0.75, -10), (1, -18)],
        'elb': [(0, -35), (0.3, -20), (0.37, -10), (0.5, -5), (0.75, -8), (1, -35)],
        'wz': [(0, 0), (0.3, -10), (0.5, 30), (1, 0)], 'wx': [(0, 0), (0.5, 40), (0.75, 35), (1, 0)],
        'sy': [(0, -6), (0.3, -40), (0.75, -40), (1, -6)], 'sx': [(0, -12), (0.3, 5), (0.75, 5), (1, -12)],
        'cape': [(0, 0), (0.3, -5), (0.45, 18), (0.75, 12), (1, 0)]}),
    # Resonant Guard: shield snaps up in 0.1 s, held 1.5 s (user call; the rules' active window is only 15 ticks),
    # lowered over 0.25 s. 56 frames = 1.85 s. Up at 3/56, down from 48/56.
    'Guard': (56, {
        'guard': [(0, 0), (0.054, 1), (0.865, 1), (1, 0)], 'bash': [(0, 0), (0.054, 0.3), (0.865, 0.3), (1, 0)],
        'sweep': [(0, 0), (0.054, -55), (0.865, -55), (1, 0)], 'abd': [(0, 8), (0.054, 35), (0.865, 35), (1, 8)],
        'raise': [(0, -18), (0.054, 0), (0.865, 0), (1, -18)], 'elb': [(0, -35), (0.054, -30), (0.865, -30), (1, -35)],
        'wx': [(0, 0), (0.054, -10), (0.865, -10), (1, 0)],
        # small settle so the long hold isn't frozen
        'drop': [(0, 0), (0.054, 0.12), (0.15, 0.1), (0.5, 0.11), (0.865, 0.1), (1, 0)], 'lean': [(0, 0), (0.054, 6), (0.865, 6), (1, 0)],
        'spread': [(0, 0), (0.054, 4), (0.865, 4), (1, 0)], 'cape': [(0, 0), (0.054, 6), (0.3, 2), (0.6, 4), (1, 0)],
        'back': [(0, 0), (0.054, 0.1), (0.865, 0.1), (1, 0)], 'tw': [(0, 0), (0.054, 12), (0.865, 12), (1, 0)]}),
    # Counter (a guard that caught a counterable strike): big shield bash, then a riposte slash
    'Counter': (14, {
        'guard': [(0, 1), (0.6, 1), (1, 0)], 'bash': [(0, 0.3), (0.25, 1), (0.45, 0.8), (0.7, 0), (1, 0)],
        'sweep': [(0, -40), (0.35, -60), (0.55, 30), (0.7, 25), (1, 0)], 'abd': [(0, 25), (0.35, 45), (0.55, 80), (0.7, 70), (1, 8)],
        'raise': [(0, 25), (0.35, 20), (0.55, -15), (1, -18)], 'elb': [(0, -30), (0.35, -90), (0.55, -10), (0.7, -15), (1, -35)],
        'wz': [(0, 0), (0.35, -30), (0.55, 20), (1, 0)], 'wx': [(0, -10), (0.35, 20), (0.55, 35), (1, 0)],
        'drop': [(0, 0.1), (0.25, 0.12), (0.5, 0.08), (1, 0)], 'lean': [(0, 6), (0.25, 22), (0.5, 14), (1, 0)],
        'tw': [(0, 12), (0.25, 22), (0.55, 25), (0.7, 20), (1, 0)], 'front': [(0, 0), (0.25, 0.3), (0.6, 0.28), (1, 0)],
        'back': [(0, 0.1), (0.25, 0.15), (0.6, 0.15), (1, 0)], 'hy': [(0, 0), (0.25, -0.15), (0.6, -0.12), (1, 0)],
        'cape': [(0, 0), (0.3, 12), (1, 0)]}),
}
def dodge(f, t):
    # Clockstep: dip, low forward dash with shield leading, drift, step out of it
    d = key(t, [(0, 0), (0.12, 0.4), (0.28, 1), (0.6, 0.9), (0.8, 0.5), (1, 0)])
    e = key(t, [(0, 0), (0.6, 0), (0.85, 1), (1, 1)])          # trailing leg returns to stance once
    lift = key(t, [(0, 0), (0.6, 0), (0.72, 1), (0.85, 0), (1, 0)])
    land = key(t, [(0, 0), (0.6, 0), (0.68, 1), (0.85, 0), (1, 0)])
    c = key(t, [(0, 0), (0.45, 1), (1, 0)])                    # cape lags the body
    sc = key(t, [(0, 0), (0.28, 0), (0.6, 1), (0.8, 0), (1, 0)])  # legs scissor in the air: front reaches for the floor
    s = stance()
    s.update({
        'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=20 * d + 8 * land, z=10 * d)},
        '_ground': 0.12 * key(t, [(0, 0), (0.15, 0), (0.35, 1), (0.62, 0), (1, 0)]),
        'spine': {'rot': Rzyx(x=2 + 14 * d + 6 * sc)}, 'chest': {'rot': Rzyx(x=3 + 8 * d, z=-15 * d)}, 'head': {'rot': Rzyx(x=-28 * d, z=8 * d)},
        'upperarm.R': {'rot': Rzyx(x=-18 + 35 * d, y=8 + 20 * d)}, 'forearm.R': {'rot': Rzyx(x=-35 - 25 * d)}, 'hand.R': {'rot': Rzyx(x=-45 * d)},
        'upperarm.L': {'rot': Rzyx(x=-12 - 50 * d, y=-6)}, 'forearm.L': {'rot': Rzyx(x=-30 - 30 * d, z=10)},
        'cape1': {'rot': Rzyx(x=4 + 30 * c)}, 'cape2': {'rot': Rzyx(x=18 * c)},
    })
    hp = 20 * d + 8 * land
    s.update(leg(-1, -6 - 55 * d + 20 * sc, 18 + 62 * d - 25 * sc + 15 * land, hip_pitch=hp, spread=4))
    s.update(leg(1, (-2 + 24 * d + 12 * sc) * (1 - e) - 2 * e - 10 * lift, 16 + 30 * d * (1 - e) + 30 * lift, hip_pitch=hp, flat=1 - 0.6 * d * (1 - e), spread=4))
    return s
def jump(f, t):
    crouch = key(t, [(0, 0), (0.15, 1), (0.22, 0.2), (0.74, 0), (0.8, 1), (0.88, 0.75), (1, 0)])
    ext = key(t, [(0, 0), (0.18, 0), (0.24, 1), (0.3, 0), (1, 0)])     # push-off: legs straight, toes pointed
    air = key(t, [(0, 0), (0.28, 0), (0.4, 1), (0.62, 1), (0.74, 0), (1, 0)])
    h = 0.0
    if 0.22 < t < 0.76: u = (t - 0.22) / 0.54; h = 0.6 * 4 * u * (1 - u)
    s = stance()
    pitch = 12 * crouch + 15 * crouch * (t > 0.7)
    s.update({
        'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=pitch)},
        'spine': {'rot': Rzyx(x=2 + 14 * crouch - 6 * air)}, 'chest': {'rot': Rzyx(x=3 + 6 * crouch)}, 'head': {'rot': Rzyx(x=-18 * crouch + 4 * air)},
        'upperarm.R': {'rot': Rzyx(x=-18 - 8 * crouch - 4 * air - 20 * ext, y=8 + 16 * air)}, 'hand.R': {'rot': Rzyx(x=35 * air)}, 'forearm.R': {'rot': Rzyx(x=-35 - 25 * crouch - 5 * air)},
        'upperarm.L': {'rot': Rzyx(x=-12 + 20 * crouch - 10 * air, y=-6)}, 'forearm.L': {'rot': Rzyx(x=-30 - 15 * air, z=10)},
        'cape1': {'rot': Rzyx(x=4 - 10 * air + 20 * crouch)}, 'cape2': {'rot': Rzyx(x=-15 * air + 10 * crouch)},
    })
    s.update(leg(-1, -6 - 60 * crouch - 45 * air + 6 * ext, 18 + 77 * crouch + 52 * air - 13 * ext, foot=35 * ext + 15 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=4))
    s.update(leg(1, -2 - 50 * crouch - 10 * air + 2 * ext, 16 + 74 * crouch + 25 * air - 11 * ext, foot=35 * ext + 15 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=4))
    land = key(t, [(0, 0), (0.74, 0), (0.8, 1), (0.9, 0), (1, 0)])
    s['_ground'] = h - 0.03 * land   # lowest sole rides the jump arc, so takeoff and landing stay continuous
    return s

# ---------------- mocap run: retarget a Mixamo-skeleton run clip (e.g. three.js Soldier.glb) onto the legs/torso ----------------
# Legs are matched by bone direction (so our splayed rest pose doesn't carry over), torso by world rotation delta.
# Arms stay authored (sword and shield), driven by the clip's step phase.
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
TOE_IN = 18
def mocap_pose(rest, smp, stride=1.0, lean=0.0):
    D = lambda k: smp[k][0] @ rest[k][0].inverted()
    dh = D('hips'); ds = D('spine'); dc = D('spine2'); dn = D('neck'); dhd = D('head')
    # half the pelvis twist: the clip swings the hips a lot, which reads as wobble with a sword and shield. Legs are
    # matched by world direction below, so this only calms the torso.
    dh = Matrix.Identity(3).to_quaternion().slerp(dh.to_quaternion(), 0.5).to_matrix()
    sway = (smp['hips'][1].x - rest['hips'][1].x) * 0.88
    pose = {'hips': {'rot': dh, 'loc': (sway, 0, 0)}, 'spine': {'rot': R(x=lean) @ dh.inverted() @ ds},
            'chest': {'rot': ds.inverted() @ dc}, 'neck': {'rot': dc.inverted() @ dn}, 'head': {'rot': R(x=-lean * 0.6) @ dn.inverted() @ dhd}}
    def scale_pitch(d):
        a = math.atan2(-d.y, -d.z) * stride; r = math.hypot(d.y, d.z)
        return Vector((d.x, -math.sin(a) * r, -math.cos(a) * r)).normalized()
    sole = []
    for side, n in ((1, 'L'), (-1, 'R')):
        hip, knee, ank, toe = (smp[f'{b}.{n}'][1] for b in ('thigh', 'shin', 'foot', 'toe'))
        # feet point straight down the run line (no sideways toe), whatever the clip's foot does
        fd = toe - ank; fd.x = 0
        dirs = [scale_pitch((knee - hip).normalized()), scale_pitch((ank - knee).normalized()), fd.normalized()]
        parent = dh
        for b, d, pre in zip(('thigh', 'shin', 'foot'), dirs, (R(z=-side * 9) @ R(y=side * 7), Matrix.Identity(3), Matrix.Identity(3))):
            name = f'{b}.{n}'; h, t = B[name][0], B[name][1]
            cur = parent @ pre @ (Vector(t) - Vector(h)).normalized()
            Db = cur.rotation_difference(d).to_matrix() @ parent @ pre
            if b == 'foot': Db = Matrix.Rotation(math.radians(-side * TOE_IN), 3, 'Z') @ Db   # the mesh's feet point 18 deg out from their bones
            pose[name] = {'rot': parent.inverted() @ Db}; parent = Db
        sole.append(min(toe.z - rest[f'toe.{n}'][1].z, ank.z - rest[f'foot.{n}'][1].z))
    pose['_ground'] = max(0.0, min(sole)) * 0.9
    return pose
MOCAP = load_mocap(args[2]) if len(args) > 2 and os.path.exists(args[2]) else None
def mocap_arms(pose, s, sprinting):
    if sprinting:
        pose.update({'upperarm.R': {'rot': Rzyx(x=-8 - 28 * s, y=26)}, 'forearm.R': {'rot': Rzyx(x=-48 + 10 * s)}, 'hand.R': {'rot': Rzyx(x=18 * s - 12)},
                     'upperarm.L': {'rot': Rzyx(x=-30 + 22 * s, y=-10)}, 'forearm.L': {'rot': Rzyx(x=-75, z=15)}})
    else:
        pose.update({'upperarm.R': {'rot': Rzyx(x=-6 - 20 * s, y=26)}, 'forearm.R': {'rot': Rzyx(x=-34 - 8 * s)}, 'hand.R': {'rot': Rzyx(x=18 * s - 12)},
                     'upperarm.L': {'rot': Rzyx(x=-22 + 10 * s, y=-8)}, 'forearm.L': {'rot': Rzyx(x=-55, z=12)}})
    return pose
if MOCAP:
    MREST, MSMP, MPH = MOCAP; MN = len(MSMP)
    def run(f, t):
        i = int(round(t * MN)) % MN; p = 2 * math.pi * t
        pose = mocap_pose(MREST, MSMP[i])
        pose.update({'cape1': {'rot': Rzyx(x=28 + 5 * math.sin(2 * p))}, 'cape2': {'rot': Rzyx(x=14 + 16 * math.sin(2 * p + 1.6))}})
        return mocap_arms(pose, MPH[i], False)
    def sprint(f, t):
        i = int(round(t * MN)) % MN; p = 2 * math.pi * t
        pose = mocap_pose(MREST, MSMP[i], stride=1.2, lean=12)
        pose.update({'cape1': {'rot': Rzyx(x=40 + 5 * math.sin(2 * p))}, 'cape2': {'rot': Rzyx(x=20 + 16 * math.sin(2 * p + 1.6))}})
        return mocap_arms(pose, MPH[i], True)
RUN_FRAMES = MN if MOCAP else 20
SPRINT_FRAMES = round(MN / 1.3) if MOCAP else 16
acts = [bake('Idle', 60, idle, True), bake('Run', RUN_FRAMES, run, True), bake('Sprint', SPRINT_FRAMES, sprint, True),
        bake('Dodge', 18, dodge, False), bake('Jump', 36, jump, False)]
acts += [bake(name, frames, (lambda K: lambda f, t: move_pose(t, K))(K), False) for name, (frames, K) in KIT.items()]
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'vanguard_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='WEBP', export_image_quality=90)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
if len(args) > 3 and args[3]:
    # Unity copy: glTFast can't be relied on for WebP, so ship JPEG textures there
    bpy.ops.export_scene.gltf(filepath=args[3], export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                              export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
    print('EXPORTED', args[3], os.path.getsize(args[3]))