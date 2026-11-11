# blender -b --factory-startup -P silt_carillon_build.py -- <work dir> <out.glb> [--textured]
# Builds the Silt Carillon body from scratch (miniboss art pass, 9 Nov 2026; no Meshy mesh, the 26 Oct model is not
# reused), rigs it with miniboss_rig_lib and keys the clips in build-evidence/miniboss-art-20261109/silt-carillon/prompts.md.
# A drowned bell tower: silt skirt with drowned steps, an open six-arch arcade (three bells hang in the front arches),
# a moulded cornice with moss drapes, a bell-shaped ivory dome with a face (two lancet-window eyes), and a lantern with a
# gold crown. Axes: -Y forward (the face), +X = its left, Z up, metres. Every part is rigid and pinned whole to one bone.
# Fits BodyRadius 1.6 / HitHeight 2.0 / MarkHeight 4.4 (SiltCarillonEncounter.Awake); stone inside r 1.5, skirt to r 1.82.
import bpy, bmesh, sys, os, math, random
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
from mathutils import Vector, Matrix, Euler, noise
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])
TEXTURED = '--textured' in args
os.makedirs(work, exist_ok=True)
random.seed(1109)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# ---------------- materials (flat in the model rounds; silt_carillon_texture.py bakes the textured set) ----------------
PAL = {
    'stone': ((0.70, 0.66, 0.52), 0.85, 0.0), 'stone_dark': ((0.42, 0.40, 0.33), 0.9, 0.0),
    'moss': ((0.11, 0.21, 0.07), 0.95, 0.0), 'moss_dark': ((0.05, 0.10, 0.04), 0.95, 0.0),
    'silt': ((0.10, 0.08, 0.055), 0.9, 0.0), 'silt_wet': ((0.035, 0.03, 0.022), 0.45, 0.0),
    'verdigris': ((0.11, 0.27, 0.18), 0.55, 0.6), 'bronze': ((0.26, 0.15, 0.05), 0.4, 0.9),
    'reed': ((0.16, 0.17, 0.06), 0.9, 0.0),
    'eye': ((0.05, 0.12, 0.35), 0.4, 0.0), 'crown': ((0.75, 0.52, 0.14), 0.3, 0.8),
}
EMIT = {'eye': ((0.30, 0.55, 1.0), 1.6), 'crown': ((1.0, 0.68, 0.22), 1.6)}
MATS = {}
for n, (col, rough, metal) in PAL.items():
    m = bpy.data.materials.new('silt_' + n); m.use_nodes = True
    p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Roughness'].default_value = rough; p.inputs['Metallic'].default_value = metal
    if n in EMIT:
        p.inputs['Emission Color'].default_value = (*EMIT[n][0], 1); p.inputs['Emission Strength'].default_value = EMIT[n][1]
    MATS[n] = m

PARTS = []   # (object, bone)
def finish(o, bone, mat, smooth=True):
    o.data.materials.clear(); o.data.materials.append(MATS[mat])
    for p in o.data.polygons: p.use_smooth = smooth
    vg = o.vertex_groups.new(name=bone); vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    PARTS.append((o, bone)); return o

def mesh_obj(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); return o

def lathe(name, prof, segs=48, rfun=None, closed=False):
    # revolve a (r, z) profile round Z; th = 0 is the front (-Y), th = 90 deg is +X. r == 0 points collapse to a pole.
    bm = bmesh.new(); rings = []
    for (r, z) in prof:
        if r <= 1e-6: rings.append([bm.verts.new((0, 0, z))]); continue
        ring = []
        for i in range(segs):
            th = 2 * math.pi * i / segs; rr = r * (rfun(th, z) if rfun else 1)
            ring.append(bm.verts.new((rr * math.sin(th), -rr * math.cos(th), z)))
        rings.append(ring)
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        if len(a) == 1 and len(b) == 1: continue
        if len(a) == 1:
            for i in range(segs): bm.faces.new((a[0], b[i], b[(i + 1) % segs]))
        elif len(b) == 1:
            for i in range(segs): bm.faces.new((a[i], b[0], a[(i + 1) % segs]))
        else:
            for i in range(segs): bm.faces.new((a[i], b[i], b[(i + 1) % segs], a[(i + 1) % segs]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm)

def prim(kind, name, loc=(0, 0, 0), rot=(0, 0, 0), scl=(1, 1, 1), **kw):
    f = {'cube': bpy.ops.mesh.primitive_cube_add, 'cyl': bpy.ops.mesh.primitive_cylinder_add, 'cone': bpy.ops.mesh.primitive_cone_add,
         'ico': bpy.ops.mesh.primitive_ico_sphere_add, 'uv': bpy.ops.mesh.primitive_uv_sphere_add, 'torus': bpy.ops.mesh.primitive_torus_add}[kind]
    f(location=loc, rotation=[math.radians(a) for a in rot], **kw)
    o = bpy.context.object; o.name = name; o.scale = scl
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o

def jitter(o, amp, freq=3.0, seed=0.0, keep_bottom=None):
    for v in o.data.vertices:
        c = v.co; n = noise.noise_vector(Vector((c.x * freq + seed, c.y * freq, c.z * freq)))
        if keep_bottom is not None and c.z <= keep_bottom: continue
        v.co = c + n * amp

def polar(th_deg, r, z): th = math.radians(th_deg); return (r * math.sin(th), -r * math.cos(th), z)
def outward(th_deg): th = math.radians(th_deg); return Vector((math.sin(th), -math.cos(th), 0))

def boolean(target, cutters, op='DIFFERENCE'):
    for c in cutters:
        m = target.modifiers.new('b', 'BOOLEAN'); m.operation = op; m.object = c; m.solver = 'EXACT'
        bpy.context.view_layer.objects.active = target; bpy.ops.object.modifier_apply(modifier=m.name)
        bpy.data.objects.remove(c)

# ---------------- geometry ----------------
ARCH = [0, 60, 120, 180, 240, 300]          # arch centres (deg from the front, toward +X)
FRONT = {'C': 0, 'L': 60, 'R': -60}           # the three bell arches
PIERS = [a + 30 for a in ARCH]

# 1. silt skirt: a lumpy flared heap (lobes fade toward the top)
def lobes(th, z):
    k = max(0.0, 1 - z / 0.56)
    return 1 + k * (0.06 * math.sin(7 * th + 1.3) + 0.035 * math.sin(13 * th + 0.4) + 0.02 * noise.noise(Vector((math.cos(th) * 3, math.sin(th) * 3, z * 4))))
skirt = finish(lathe('skirt', [(1.80, 0.0), (1.76, 0.07), (1.65, 0.19), (1.55, 0.33), (1.47, 0.47), (1.30, 0.56), (0.0, 0.57)], 56, lobes), 'base', 'silt')
wet = finish(lathe('skirt_wet', [(1.83, 0.0), (1.81, 0.05), (1.77, 0.075)], 56, lobes), 'base', 'silt_wet')
# drowned steps at the front, half buried, stepping up to the front arch
for i, (y, z, w) in enumerate([(-1.80, 0.05, 0.95), (-1.60, 0.19, 0.88), (-1.43, 0.34, 0.80)]):
    s = prim('cube', f'step{i}', loc=(0.02 * i, y, z), rot=(random.uniform(-3, 3), random.uniform(-2, 2), random.uniform(-4, 4)), scl=(w / 2, 0.16, 0.085))
    finish(s, 'base', 'stone_dark', smooth=False)
blk = prim('cube', 'block', loc=polar(75, 1.66, 0.12), rot=(12, -8, 30), scl=(0.2, 0.16, 0.14)); finish(blk, 'base', 'stone_dark', smooth=False)
blk2 = prim('cube', 'block2', loc=polar(-150, 1.68, 0.08), rot=(-9, 15, 10), scl=(0.16, 0.22, 0.1)); finish(blk2, 'base', 'stone_dark', smooth=False)
# reed tufts (never at the front)
for k, (th, r) in enumerate([(105, 1.46), (118, 1.44), (-120, 1.46), (-132, 1.44), (200, 1.47), (160, 1.45)]):
    base = Vector(polar(th, r, 0.42))
    for j in range(4):
        h = random.uniform(0.45, 0.8); tilt = outward(th) * random.uniform(0.1, 0.3) + Vector((random.uniform(-.1, .1), random.uniform(-.1, .1), 0))
        top = base + Vector((0, 0, h)) + tilt * h
        c = prim('cone', f'reed{k}_{j}', loc=(base + top) / 2, vertices=4, radius1=0.022, radius2=0.002, depth=(top - base).length)
        c.rotation_euler = (top - base).to_track_quat('Z', 'Y').to_euler(); bpy.ops.object.transform_apply(rotation=True)
        finish(c, 'base', 'reed', smooth=False)

# 2. plinth ring, the arcade shell with six arches cut, a dark core, back-arch infill walls
finish(lathe('plinth', [(1.15, 0.45), (1.50, 0.45), (1.53, 0.60), (1.45, 0.66), (1.15, 0.66)], 48, closed=True), 'drum', 'stone_dark', smooth=False)
shell = lathe('arcade', [(1.18, 0.62), (1.42, 0.62), (1.42, 1.78), (1.18, 1.78)], 48, closed=True)
cut = []
for a in ARCH:
    o = outward(a); bx = prim('cube', 'cutb', loc=Vector(polar(a, 1.3, 0.62 + 0.62 / 2 - 0.02)), rot=(0, 0, a), scl=(0.40, 0.5, 0.33))
    cy = prim('cyl', 'cutc', loc=Vector(polar(a, 1.3, 1.25)), rot=(90, 0, a), vertices=20, radius=0.40, depth=1.0)
    cut += [bx, cy]
boolean(shell, cut); finish(shell, 'drum', 'stone', smooth=False)
finish(lathe('core', [(0.0, 0.62), (1.12, 0.62), (1.12, 1.80), (0.0, 1.80)], 32), 'drum', 'silt_wet')
for a in (120, 180, 240):
    w = prim('cube', f'infill{a}', loc=polar(a, 1.25, 1.12), rot=(0, 0, a), scl=(0.42, 0.04, 0.52)); finish(w, 'drum', 'silt', smooth=False)
    sill = prim('cube', f'sill{a}', loc=polar(a, 1.33, 0.66), rot=(0, 0, a), scl=(0.42, 0.12, 0.03)); finish(sill, 'drum', 'silt', smooth=False)
for a in PIERS:   # pier faces: a raised pilaster on each pier, so the arcade reads at range
    p = prim('cube', f'pilaster{a}', loc=polar(a, 1.45, 1.2), rot=(0, 0, a), scl=(0.15, 0.05, 0.56)); finish(p, 'drum', 'stone', smooth=False)
# 3. cornice + moss drapes (two sway bones: its left / right half)
finish(lathe('cornice', [(1.10, 1.74), (1.40, 1.74), (1.53, 1.79), (1.57, 1.86), (1.50, 1.93), (1.10, 1.93)], 48, closed=True), 'drum', 'stone', smooth=False)
for a in PIERS + [150, 165, 195, 210, 12, -20]:
    n = random.choice((0, 1, 2, 3, 3, 4))
    for j in range(n):
        th = a + random.uniform(-13, 13); L = random.uniform(0.14, 0.46)
        d = prim('cone', f'drape{a}_{j}', loc=polar(th, 1.56, 1.80 - L / 2), rot=(180, 0, th), vertices=5, radius1=random.uniform(0.05, 0.14), radius2=0.012, depth=L)
        d.scale = (1, 0.35, 1); bpy.ops.object.transform_apply(scale=True)
        jitter(d, 0.012, 9, a + j)
        finish(d, 'moss.L' if math.sin(math.radians(th)) >= 0 else 'moss.R', 'moss')

# 4. bell dome (an upturned bell: flared lip, waist, shoulder), bronze lip ring, moss caps
DOME = [(0.0, 1.90), (1.50, 1.90), (1.63, 1.94), (1.60, 2.02), (1.42, 2.10), (1.20, 2.26), (1.05, 2.46), (0.99, 2.66), (0.99, 2.84), (0.93, 2.98), (0.76, 3.09), (0.42, 3.17), (0.0, 3.19)]
def dome_r(z):
    pts = DOME[1:]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        if z0 <= z <= z1: return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return 0.0
finish(lathe('dome', DOME[1:], 56), 'dome', 'stone')
finish(lathe('domeunder', [(0.0, 1.905), (1.50, 1.905)], 56), 'dome', 'silt_wet')
lip = prim('torus', 'domelip', loc=(0, 0, 1.975), major_radius=1.585, minor_radius=0.07, major_segments=56, minor_segments=8); finish(lip, 'dome', 'bronze')
finish(prim('torus', 'domeband', loc=(0, 0, 2.86), major_radius=0.99, minor_radius=0.03, major_segments=48, minor_segments=5), 'dome', 'bronze')
# a plug under the dome on the dome bone: when the dome tips (Peal, Drained) it shows stone, never the hollow
finish(lathe('domeplug', [(0.0, 1.50), (1.08, 1.50), (1.10, 1.72), (1.46, 1.76), (1.50, 1.905), (0.0, 1.905)], 40), 'dome', 'stone_dark')
def blob(name, th, z, r, flat, seed):
    rr = dome_r(z); n = (outward(th) * 0.75 + Vector((0, 0, 0.65 if z > 2.9 else 0.3))).normalized()
    at = Vector(polar(th, rr - 0.02, z))
    o = prim('ico', name, loc=(0, 0, 0), subdivisions=3, radius=r); o.scale = (1, 1, flat); bpy.ops.object.transform_apply(scale=True)
    jitter(o, r * 0.32, 1.6 / r, seed)
    o.rotation_euler = n.to_track_quat('Z', 'Y').to_euler(); o.location = at; bpy.ops.object.transform_apply(location=True, rotation=True)
    return o
for k, (th, z, r) in enumerate([(180, 3.06, 0.36), (160, 2.92, 0.30), (205, 2.98, 0.28), (195, 2.55, 0.22)]):
    finish(blob(f'mosscap{k}', th, z, r, 0.55, 1 + k), 'dome', 'moss')
for k, (th, z, r) in enumerate([(140, 2.62, 0.24), (-140, 2.45, 0.24), (100, 2.95, 0.2), (-100, 2.3, 0.18), (230, 2.25, 0.2), (42, 2.92, 0.14), (-44, 2.98, 0.13)]):
    finish(blob(f'moss{k}', th, z, r, 0.55, 10 + k), 'dome', 'moss_dark' if k % 2 else 'moss')
# 5. the face: two arched lancet windows (eyes), stone surrounds, a sorrowful brow, a grille mouth plate
def on_dome(x, z, inset):
    # a point on the dome's front surface at height z, x across, pushed `inset` m into it; and the yaw that faces out
    r = dome_r(z); y = -math.sqrt(max(0.01, r * r - x * x)); n = Vector((x, y, 0)).normalized()
    return Vector((x, y, z)) - n * inset, math.degrees(math.atan2(x, -y))
for side in (-1, 1):
    x = 0.24 * side; z0 = 2.55
    at, yaw = on_dome(x, z0 + 0.12, 0.03)
    sur = prim('cube', 'eyeframe', loc=at, rot=(0, 0, yaw), scl=(0.15, 0.06, 0.26)); finish(sur, 'dome', 'stone_dark', smooth=False)
    at, yaw = on_dome(x, z0 + 0.06, -0.015)
    eye = prim('cube', 'eye', loc=at, rot=(0, 0, yaw), scl=(0.105, 0.03, 0.17)); finish(eye, 'dome', 'eye', smooth=False)
    at, yaw = on_dome(x, z0 + 0.23, -0.015)
    top = prim('cyl', 'eyetop', loc=at, rot=(90, 0, yaw), vertices=14, radius=0.105, depth=0.06); finish(top, 'dome', 'eye')
    # brow: one carved ridge per eye that meets the other in the middle (inner ends raised: sorrowful), set into the dome
    at, yaw = on_dome(0.2 * side, z0 + 0.48, 0.02)
    br = prim('cube', 'brow', loc=at, rot=(0, 13 * side, yaw), scl=(0.22, 0.05, 0.04)); finish(br, 'dome', 'stone', smooth=False)
at, yaw = on_dome(0, 2.36, 0.02)
mp = prim('cube', 'mouthplate', loc=at, rot=(-8, 0, 0), scl=(0.26, 0.05, 0.1)); finish(mp, 'dome', 'stone', smooth=False)
for i in (-1, 0, 1):
    at, yaw = on_dome(0.11 * i, 2.36, -0.035)
    sl = prim('cube', 'slot', loc=at, rot=(-8, 0, yaw), scl=(0.03, 0.015, 0.07)); finish(sl, 'dome', 'silt_wet', smooth=False)
# 6. lantern + crown: a ring base, four posts, a cap, a small gold bell inside and a finial
finish(prim('cyl', 'lanbase', loc=(0, 0, 3.22), vertices=16, radius=0.43, depth=0.1), 'lantern', 'stone_dark', smooth=False)
for k in range(4):
    a = 45 + 90 * k; finish(prim('cyl', f'post{k}', loc=polar(a, 0.33, 3.47), vertices=6, radius=0.055, depth=0.42), 'lantern', 'stone', smooth=False)
finish(prim('cone', 'lancap', loc=(0, 0, 3.80), vertices=16, radius1=0.53, radius2=0.08, depth=0.26), 'lantern', 'verdigris', smooth=False)
BELLP = [(0.50, 0.0), (0.48, 0.04), (0.41, 0.10), (0.34, 0.30), (0.31, 0.60), (0.30, 0.84), (0.22, 0.96), (0.0, 1.0)]
def bell_mesh(name, rad, h, segs=24):
    # hollow bell: inner pole -> inner wall down to the lip -> outer wall up to the crown pole (lip at z 0, rad = lip radius)
    k = rad * 2
    inner = [(0.0, h * 0.9)] + [(r * k * 0.86, z * h * 0.9) for r, z in reversed(BELLP[1:-1])] + [(BELLP[0][0] * k * 0.86, 0.0)]
    outer = [(r * k, z * h) for r, z in BELLP]
    return lathe(name, inner + outer, segs)
cb = bell_mesh('crownbell', 0.15, 0.24, 16); cb.location = (0, 0, 3.30); bpy.ops.object.select_all(action='DESELECT'); cb.select_set(True)
bpy.context.view_layer.objects.active = cb; bpy.ops.object.transform_apply(location=True); finish(cb, 'lantern', 'crown')
finish(prim('uv', 'finial', loc=(0, 0, 4.00), segments=14, ring_count=10, radius=0.11), 'lantern', 'crown')
finish(prim('cone', 'spike', loc=(0, 0, 4.17), vertices=8, radius1=0.04, radius2=0.0, depth=0.16), 'lantern', 'crown', smooth=False)
# 7. three hanging bells in the front arches (yokes on the drum; bell + crown loop on bell.*; clapper on clapper.*)
HANG = {}
for s, a in FRONT.items():
    hp = Vector(polar(a, 1.30, 1.56)); HANG[s] = hp
    y = prim('cube', f'yoke{s}', loc=hp + Vector((0, 0, 0.06)), rot=(0, 0, a), scl=(0.40, 0.05, 0.045)); finish(y, 'drum', 'bronze', smooth=False)
    b = bell_mesh(f'bell{s}', 0.25, 0.46); b.location = hp - Vector((0, 0, 0.52)); bpy.ops.object.select_all(action='DESELECT'); b.select_set(True)
    bpy.context.view_layer.objects.active = b; bpy.ops.object.transform_apply(location=True); finish(b, 'bell.' + s, 'verdigris')
    lp = prim('torus', f'bellloop{s}', loc=hp - Vector((0, 0, 0.04)), rot=(90, 0, a), major_radius=0.05, minor_radius=0.014, major_segments=10, minor_segments=4); finish(lp, 'bell.' + s, 'bronze')
    br = prim('torus', f'belllip{s}', loc=hp - Vector((0, 0, 0.51)), major_radius=0.245, minor_radius=0.022, major_segments=24, minor_segments=4); finish(br, 'bell.' + s, 'bronze')
    rod = prim('cyl', f'clrod{s}', loc=hp - Vector((0, 0, 0.27)), vertices=6, radius=0.012, depth=0.36); finish(rod, 'clapper.' + s, 'bronze', smooth=False)
    ball = prim('uv', f'clball{s}', loc=hp - Vector((0, 0, 0.46)), segments=8, ring_count=6, radius=0.045); finish(ball, 'clapper.' + s, 'bronze')

# ---------------- join into one mesh ----------------
bpy.ops.object.select_all(action='DESELECT')
for o, _ in PARTS: o.select_set(True)
body = PARTS[0][0]; bpy.context.view_layer.objects.active = body
bpy.ops.object.join(); body.name = 'SiltCarillon'; body.data.name = 'SiltCarillon'
tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
print('BODY_TRIS', tris, 'verts', len(body.data.vertices), 'materials', [m.name for m in body.data.materials])

if TEXTURED:
    import silt_carillon_texture as tex
    tex.apply(body, work)

# ---------------- rig ----------------
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'base': ((0, 0, 0), (0, 0, 0.5), 'root'),
     'drum': ((0, 0, 0.5), (0, 0, 1.88), 'base'),
     'dome': ((0, 0, 1.92), (0, 0, 3.15), 'drum'),
     'lantern': ((0, 0, 3.17), (0, 0, 4.1), 'dome'),
     'moss.L': ((0.9, 0, 1.85), (1.0, 0, 1.45), 'drum'), 'moss.R': ((-0.9, 0, 1.85), (-1.0, 0, 1.45), 'drum')}
for s in FRONT:
    hp = HANG[s]
    B['bell.' + s] = (tuple(hp), tuple(hp - Vector((0, 0, 0.5))), 'drum')
    B['clapper.' + s] = (tuple(hp - Vector((0, 0, 0.09))), tuple(hp - Vector((0, 0, 0.5))), 'bell.' + s)
AIM = ('bell.C', 'bell.L', 'bell.R', 'clapper.C', 'clapper.L', 'clapper.R')
# Rig() surveys loose parts and makes vertex groups itself in assign(); the groups already exist here, so bind directly
rig = Rig('SiltCarillon', B, AIM)
body.parent = rig.arm; body.matrix_parent_inverse = rig.arm.matrix_world.inverted()
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = rig.arm

# ---------------- poses ----------------
TAU = 2 * math.pi
def P(): return {'rot': {}, 'loc': {}, 'aim': {}}
def bells(ch, out=(0, 0, 0), side=(0, 0, 0), lag=0.6, clap=(0, 0, 0), lean=None):
    # each hanging bell aims (armature space, so it keeps hanging under gravity when the drum tilts) at its outward swing
    # out[i] deg (+ = out through the arch) and sideways swing side[i] deg; the clapper lags by `lag` and adds clap[i]
    for i, s in enumerate('CLR'):
        o = outward(FRONT[s]); tv = Vector((-o.y, o.x, 0))
        def d(a, b):
            v = Vector((0, 0, -1)) * math.cos(math.radians(a)) * math.cos(math.radians(b)) + o * math.sin(math.radians(a)) + tv * math.sin(math.radians(b))
            if lean: v += Vector(lean)
            return tuple(v.normalized())
        ch['aim']['bell.' + s] = d(out[i], side[i])
        ch['aim']['clapper.' + s] = d(out[i] * lag + clap[i], side[i] * lag)
    return ch
def moss(ch, a): ch['rot']['moss.L'] = (0, a, 0); ch['rot']['moss.R'] = (0, -a, 0); return ch
def tower(ch, x=0, y=0, z=0, up=0, dx=0, dome=(0, 0, 0), lantern=(0, 0, 0), lan_loc=None):
    ch['rot']['drum'] = (x, y, z); ch['loc']['drum'] = (dx, 0, up); ch['rot']['dome'] = dome; ch['rot']['lantern'] = lantern
    if lan_loc: ch['loc']['lantern'] = lan_loc
    return ch

def idle(t):
    p = TAU * t / 4.0
    ch = tower(P(), x=0.6 * math.sin(p), y=0.5 * math.sin(p + 1.3), up=0.02 * math.sin(p), dome=(1.0 * math.sin(p + 0.5), 0.6 * math.sin(p + 2.0), 0))
    return moss(bells(ch, out=tuple(4 * math.sin(p + k * 2.1) for k in range(3)), side=tuple(2 * math.sin(p + k) for k in range(3))), 3 * math.sin(p * 2))
def walk(t):
    p = TAU * t / 1.6
    ch = tower(P(), x=3.5 + 1.5 * math.sin(2 * p), y=8 * math.sin(p), up=0.06 * abs(math.sin(p)), dx=0.06 * math.sin(p), dome=(0, -3.5 * math.sin(p - 0.5), 0), lantern=(0, -4 * math.sin(p - 0.9), 0))
    ch['loc']['base'] = (0.06 * math.sin(p - 0.4), 0, 0.02 * abs(math.sin(p)))
    return moss(bells(ch, out=(10 * math.sin(2 * p),) * 3, side=tuple(-16 * math.sin(p - 0.7 - 0.1 * k) for k in range(3))), -9 * math.sin(p - 0.8))
def tideraise(t):
    up = key(t, [(0, 0), (0.45, 0.16), (0.7, 0.16), (0.85, -0.05), (1.2, 0)])
    lean = key(t, [(0, 0), (0.45, -10), (0.7, -10), (0.85, 3), (1.2, 0)])
    sh = ring(t, 0.7, 2.0, 6, 4)
    ch = tower(P(), x=lean, y=sh, up=up, dome=(key(t, [(0, 0), (0.5, -4), (0.75, 3), (1.2, 0)]), 0, 0), lantern=(key(t, [(0, 0), (0.55, -6), (0.8, 6), (1.2, 0)]), 0, 0))
    ch['loc']['base'] = (0, 0, key(t, [(0, 0), (0.45, 0.07), (0.7, 0.0)]))
    o = key(t, [(0, 0), (0.4, 40), (0.7, 16), (0.9, -12), (1.2, 0)])
    return moss(bells(ch, out=(o, o * 0.9, o * 0.9)), key(t, [(0, 0), (0.5, 10), (0.9, -6), (1.2, 0)]))
def tideloop(t):
    p = TAU * t / 2.4
    ch = tower(P(), x=4.5 * math.sin(p), y=4 * math.sin(p + math.pi / 2), up=0.05 + 0.03 * math.sin(2 * p), dome=(2.5 * math.sin(p - 0.6), 2 * math.sin(p + 0.9), 0), lantern=(4 * math.sin(p - 1.2), 3 * math.sin(p + 0.3), 0))
    lean = (0.45 * math.sin(p - 0.8), 0.3 * math.sin(p - 0.2), 0)
    return moss(bells(ch, out=(12,) * 3, lean=lean), 10 * math.sin(p - 1.0))
def belltoll(t):
    T = 0.30
    o = key(t, [(0, 0), (0.12, -16), (T, 40)]) if t <= T else 40 * math.exp(-3.0 * (t - T)) * math.cos(TAU * 1.8 * (t - T))
    clap = (-14 * snap(t, T, 0.2, 0.05),) * 3
    ch = tower(P(), up=-0.07 * snap(t, T, 0.05, 0.3), x=2.0 * snap(t, T, 0.06, 0.4), dome=(ring(t, T, 3.0, 4, 4), 0, 0))
    return moss(bells(ch, out=(o, o * 0.95, o * 0.95), lag=0.75, clap=clap), 4 * snap(t, T, 0.1, 0.5))
def siltpour(t):
    k = key(t, [(0, 0), (0.5, 1), (0.8, 1)])
    ch = tower(P(), x=6 * k, dome=(1 * k, 0, 0), lantern=(10 * k, 0, 0), up=0.04 * k)
    return moss(bells(ch, out=(-12 * k, 10 * k, 10 * k), side=(0, 34 * k, -34 * k)), 8 * k)
def pourloop(t):
    beat = 0.625; j = snap(t % beat, 0.08, 0.07, 0.4)
    ch = tower(P(), x=6 + 2 * j, dome=(1 + 1.5 * j, 0, 0), lantern=(10 + 6 * j, 0, 0), up=0.04 - 0.10 * j)
    sh = 12 * j * math.sin(TAU * 5 * (t % beat))
    return moss(bells(ch, out=(-12 + sh, 10 + sh, 10 + sh), side=(0, 34, -34)), 8 + 6 * j)
def pealwind(t):
    k = key(t, [(0, 0), (0.7, 1), (1.0, 1)])
    ch = tower(P(), x=-4 * k, dome=(-10 * k, 0, 0), lantern=(key(t, [(0, 0), (0.6, 4), (1.0, -5)]), 0, 0))
    return moss(bells(ch, out=(-20 * k,) * 3), -4 * k)
def pealloop(t):
    tr = 1.5 * math.sin(TAU * 6 * t)
    ch = tower(P(), x=-4, dome=(-10 + tr, 0.4 * math.sin(TAU * 4 * t), 0), lantern=(-5 + 2 * math.sin(TAU * 6 * t + 1), 0, 0))
    return moss(bells(ch, out=(-20 + 2 * math.sin(TAU * 3 * t),) * 3), -4)
def pealstrike(t):
    T = 0.20
    d = key(t, [(0, -8), (T, 11)]) if t <= T else 11 * math.exp(-3.5 * (t - T)) * math.cos(TAU * 2.2 * (t - T)) + key(t, [(T, 0), (0.9, -5)])
    ch = tower(P(), x=1.5 * snap(t, T, 0.05, 0.3), y=ring(t, T, 1.5, 5, 4), dome=(d, 0, 0), lantern=(-0.6 * d, 0, 0))
    o = key(t, [(0, -14), (T, 24)]) if t <= T else 24 * math.exp(-3 * (t - T)) * math.cos(TAU * 1.6 * (t - T))
    return moss(bells(ch, out=(o,) * 3, lag=0.7), 5 * snap(t, T, 0.05, 0.4))
SLUMP = dict(x=14, up=-0.30, dome=(4, 0, 0), lantern=(0, 10, 0))
def drainslump(t):
    k = key(t, [(0, 0), (0.5, 1.12), (0.8, 1)])
    ch = tower(P(), x=SLUMP['x'] * k, up=SLUMP['up'] * min(1, k), dome=(7 * k, 0, 0), lantern=(0, 20 * k, 0))
    o = ring(t, 0.4, 10, 1.5, 3)
    return moss(bells(ch, out=(o, o, o)), -12 * min(1, k))
def drainedloop(t):
    p = TAU * t / 2.0
    ch = tower(P(), x=14 + 1.0 * math.sin(p), up=-0.30 + 0.015 * math.sin(p), dome=(7 + 0.8 * math.sin(p - 0.5), 0, 0), lantern=(0, 20, 0))
    return moss(bells(ch, out=tuple(1 * math.sin(p + k) for k in range(3))), -12 + 1.5 * math.sin(p))
def drainrecover(t):
    k = key(t, [(0, 1), (0.55, -0.12), (0.8, 0)])
    ch = tower(P(), x=14 * k, y=ring(t, 0.55, 2, 5, 4), up=-0.30 * max(0, k), dome=(7 * k, 0, 0), lantern=(0, 20 * max(0, k), 0))
    return moss(bells(ch, out=(ring(t, 0.5, 18, 1.6, 3),) * 3), -8 * max(0, k))
def phaseshift(t):
    wob = 4 * math.sin(TAU * 3 * t) * key(t, [(0, 0), (0.15, 1), (0.6, 1), (0.8, 0)])
    spin = key(t, [(0.45, 0), (1.6, 360)])
    fl = key(t, [(0.4, 0), (0.85, 70), (1.45, 70), (1.8, 0)])
    lift = key(t, [(0.4, 0), (1.0, 0.1), (1.55, 0.1), (1.7, 0)])
    ch = tower(P(), x=wob * 0.5, y=wob, up=-0.06 * snap(t, 1.72, 0.05, 0.25), dome=(0, 0, spin), lantern=(0, 0, -spin * 0.5), lan_loc=(0, 0, lift))
    return moss(bells(ch, out=(fl,) * 3, lag=0.85), 10 * snap(t, 1.72, 0.1, 0.3) + wob)
def defeat(t):
    k = key(t, [(0, 0), (2.0, 1), (2.5, 1)])
    # the lantern lands on its side across the drowned steps (top toward its right), clear of the drum, inside r 1.8
    lan = key(t, [(0.5, (0, 0, 0)), (0.9, (-0.1, -0.7, 0.35)), (1.2, (-0.2, -1.95, -0.3)), (1.55, (0.27, -1.72, -1.92)), (1.7, (0.27, -1.68, -1.87))])
    lx = key(t, [(0.5, 0), (1.2, -40), (1.55, 0)]); ly = key(t, [(0.5, 0), (1.2, -50), (1.55, -116), (1.7, -112)])
    ch = tower(P(), x=3 * k, y=14 * k + ring(t, 2.0, 2, 3, 4), up=-0.45 * k, dome=(2 * k, 0, 0), lantern=(lx, ly, 0), lan_loc=lan)
    ch['rot']['base'] = (0, 8 * k, 0); ch['loc']['base'] = (0, 0, -0.15 * k)
    o = ring(t, 0.3, 20, 1.2, 1.5)
    return moss(bells(ch, out=(o, -o, o)), -10 * k)

CLIPS = [('Idle', 4.0, idle), ('Walk', 1.6, walk), ('TideRaise', 1.2, tideraise), ('TideLoop', 2.4, tideloop),
         ('BellToll', 1.0, belltoll), ('SiltPour', 0.8, siltpour), ('PourLoop', 1.25, pourloop), ('PealWind', 1.0, pealwind),
         ('PealLoop', 1.0, pealloop), ('PealStrike', 0.9, pealstrike), ('DrainSlump', 0.8, drainslump), ('DrainedLoop', 2.0, drainedloop),
         ('DrainRecover', 0.8, drainrecover), ('PhaseShift', 2.0, phaseshift), ('Defeat', 2.5, defeat)]
for n, sec, fn in CLIPS: rig.bake(n, sec, fn)
rig.export(work, 'silt_carillon', out_glb)
print('TRIS', tris)
