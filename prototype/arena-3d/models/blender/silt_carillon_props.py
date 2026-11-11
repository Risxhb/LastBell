# blender -b --factory-startup -P silt_carillon_props.py -- <work dir> <out dir for GLBs> [--textured]
# The Silt Carillon's two props (miniboss art pass, 9 Nov 2026; prompts.md assets 2 and 3):
#   SiltBell.glb   a Sunken Bell surfacing in its soak circle: verdigris bell with a silt cap, a silt collar at the floor;
#                  rigged (root, collar, bell, clapper) with Surface, BellLoop, Toll, Sink. Footprint r <= 0.65, height <= 0.9.
#   SiltMound.glb  a low wet silt heap for each landed Silt Fall drop (static, r <= 0.9, h <= 0.28).
# Axes: -Y forward, Z up, origin at the floor centre.
import bpy, bmesh, sys, os, math, random
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
from mathutils import Vector, noise
args = sys.argv[sys.argv.index('--') + 1:]
work, outdir = os.path.abspath(args[0]), os.path.abspath(args[1])
TEXTURED = '--textured' in args
os.makedirs(work, exist_ok=True); os.makedirs(outdir, exist_ok=True)
random.seed(3)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    return bpy.context.scene

PAL = {'silt': ((0.10, 0.08, 0.055), 0.9, 0.0), 'silt_wet': ((0.035, 0.03, 0.022), 0.4, 0.0), 'moss': ((0.11, 0.21, 0.07), 0.95, 0.0),
       'verdigris': ((0.17, 0.40, 0.29), 0.5, 0.55), 'bronze': ((0.26, 0.15, 0.05), 0.4, 0.9), 'mouth': ((0.02, 0.05, 0.045), 0.6, 0.0),
       'fleck': ((0.55, 0.52, 0.42), 0.8, 0.0)}
EMIT = {'mouth': ((0.25, 0.75, 0.6), 2.5)}
def mats():
    M = {}
    for n, (col, rough, metal) in PAL.items():
        m = bpy.data.materials.new('silt_' + n); m.use_nodes = True; p = m.node_tree.nodes['Principled BSDF']
        p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Roughness'].default_value = rough; p.inputs['Metallic'].default_value = metal
        if n in EMIT: p.inputs['Emission Color'].default_value = (*EMIT[n][0], 1); p.inputs['Emission Strength'].default_value = EMIT[n][1]
        M[n] = m
    return M

def lathe(sc, name, prof, segs=32, rfun=None):
    bm = bmesh.new(); rings = []
    for (r, z) in prof:
        if r <= 1e-6: rings.append([bm.verts.new((0, 0, z))]); continue
        rings.append([bm.verts.new(((r * (rfun(2 * math.pi * i / segs, z) if rfun else 1)) * math.sin(2 * math.pi * i / segs),
                                    -(r * (rfun(2 * math.pi * i / segs, z) if rfun else 1)) * math.cos(2 * math.pi * i / segs), z)) for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1: [bm.faces.new((a[0], b[i], b[(i + 1) % segs])) for i in range(segs)]
        elif len(b) == 1: [bm.faces.new((a[i], b[0], a[(i + 1) % segs])) for i in range(segs)]
        else: [bm.faces.new((a[i], b[i], b[(i + 1) % segs], a[(i + 1) % segs])) for i in range(segs)]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); return o

def prim(kind, name, loc=(0, 0, 0), rot=(0, 0, 0), scl=(1, 1, 1), **kw):
    f = {'cube': bpy.ops.mesh.primitive_cube_add, 'cyl': bpy.ops.mesh.primitive_cylinder_add, 'cone': bpy.ops.mesh.primitive_cone_add,
         'ico': bpy.ops.mesh.primitive_ico_sphere_add, 'uv': bpy.ops.mesh.primitive_uv_sphere_add, 'torus': bpy.ops.mesh.primitive_torus_add}[kind]
    f(location=loc, rotation=[math.radians(a) for a in rot], **kw)
    o = bpy.context.object; o.name = name; o.scale = scl; bpy.ops.object.transform_apply(location=False, rotation=True, scale=True); return o

def jitter(o, amp, freq, seed, floor=None):
    for v in o.data.vertices:
        if floor is not None and v.co.z <= floor: continue
        v.co += noise.noise_vector(Vector((v.co.x * freq + seed, v.co.y * freq, v.co.z * freq))) * amp

def finish(parts, o, bone, m, smooth=True):
    o.data.materials.clear(); o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth = smooth
    if bone: o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    parts.append(o); return o

def join(parts, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join()
    o = parts[0]; o.name = name; o.data.name = name
    print('TRIS', name, sum(len(p.vertices) - 2 for p in o.data.polygons)); return o

# ================= SiltBell =================
sc = reset(); M = mats(); parts = []
BELLP = [(0.50, 0.0), (0.485, 0.035), (0.42, 0.10), (0.35, 0.20), (0.30, 0.33), (0.285, 0.50), (0.285, 0.70), (0.25, 0.86), (0.16, 0.96), (0.0, 1.0)]
LIP, H = 0.31, 0.70                      # lip radius, bell height; the lip stands 0.06 above the floor, proud of the collar
Z0 = 0.06
k = LIP * 2
inner = [(0.0, H * 0.9)] + [(r * k * 0.86, z * H * 0.9) for r, z in reversed(BELLP[1:-1])] + [(BELLP[0][0] * k * 0.86, 0.0)]
bell = lathe(sc, 'bell', inner + [(r * k, z * H) for r, z in BELLP], 28); bell.location.z = Z0
bpy.context.view_layer.objects.active = bell; bell.select_set(True); bpy.ops.object.transform_apply(location=True)
finish(parts, bell, 'bell', M['verdigris'])
finish(parts, prim('torus', 'lip', loc=(0, 0, Z0 + 0.012), major_radius=LIP - 0.002, minor_radius=0.034, major_segments=28, minor_segments=6), 'bell', M['bronze'])
finish(parts, prim('torus', 'shband', loc=(0, 0, Z0 + 0.53), major_radius=0.178, minor_radius=0.014, major_segments=24, minor_segments=4), 'bell', M['bronze'])
finish(parts, prim('torus', 'loop', loc=(0, 0, Z0 + 0.73), rot=(90, 0, 0), major_radius=0.075, minor_radius=0.024, major_segments=14, minor_segments=5), 'bell', M['bronze'])
# a short snapped beam still through the loop (shorter than the shoulder is wide), splintered end on one side
beam = prim('cube', 'beam', loc=(0.03, 0, Z0 + 0.765), rot=(0, 9, 0), scl=(0.11, 0.035, 0.03)); jitter(beam, 0.008, 20, 3); finish(parts, beam, 'bell', M['bronze'], False)
spl = prim('cone', 'splinter', loc=(0.155, 0, Z0 + 0.79), rot=(0, 99, 0), vertices=4, radius1=0.03, radius2=0.004, depth=0.06); finish(parts, spl, 'bell', M['bronze'], False)
# a lumpy silt drape over the crown that slumps down one side
cap = prim('ico', 'siltcap', loc=(0.08, 0.04, Z0 + 0.66), subdivisions=3, radius=0.10); cap.scale = (1.2, 1.0, 0.55); bpy.ops.object.transform_apply(scale=True)
jitter(cap, 0.025, 10, 2); finish(parts, cap, 'bell', M['silt'])
tuft = prim('ico', 'tuft', loc=(-0.12, 0.08, Z0 + 0.69), subdivisions=2, radius=0.08); tuft.scale = (1.2, 1, 0.6); bpy.ops.object.transform_apply(scale=True)
jitter(tuft, 0.025, 12, 5); finish(parts, tuft, 'bell', M['moss'])
mouth = prim('cyl', 'mouthglow', loc=(0, 0, Z0 + 0.02), vertices=20, radius=LIP * 0.86, depth=0.01); finish(parts, mouth, 'bell', M['mouth'], False)
finish(parts, prim('cyl', 'clrod', loc=(0, 0, Z0 + 0.36), vertices=6, radius=0.014, depth=0.36), 'clapper', M['bronze'], False)
finish(parts, prim('uv', 'clball', loc=(0, 0, Z0 + 0.15), segments=10, ring_count=6, radius=0.06), 'clapper', M['bronze'])
def lobes(th, z): return 1 + 0.12 * math.sin(5 * th + 0.7) + 0.06 * math.sin(11 * th)
col = lathe(sc, 'collar', [(0.66, 0.0), (0.58, 0.035), (0.46, 0.06), (0.36, 0.05), (0.32, 0.0)], 32, lobes); jitter(col, 0.02, 6, 9, floor=0.001)
finish(parts, col, 'collar', M['silt'])
for i in range(5):
    a = random.uniform(0, 2 * math.pi); r = random.uniform(0.45, 0.62)
    cl = prim('ico', f'clod{i}', loc=(r * math.sin(a), -r * math.cos(a), 0.03), subdivisions=1, radius=random.uniform(0.05, 0.09))
    cl.scale = (1, 1, 0.6); bpy.ops.object.transform_apply(scale=True); jitter(cl, 0.015, 15, i); finish(parts, cl, 'collar', M['silt_wet'])
body = join(parts, 'SiltBell')
if TEXTURED:
    import silt_carillon_texture as tex
    tex.apply_prop(body, work, 'SiltBell')
B = {'root': ((0, 0, 0), (0, 0.3, 0), None), 'collar': ((0, 0, 0), (0, 0, 0.12), 'root'),
     'bell': ((0, 0, Z0), (0, 0, 0.78), 'root'), 'clapper': ((0, 0, Z0 + 0.55), (0, 0, Z0 + 0.12), 'bell')}
rig = Rig('SiltBell', B, ('clapper',))
body.parent = rig.arm; body.matrix_parent_inverse = rig.arm.matrix_world.inverted(); body.modifiers.new('Armature', 'ARMATURE').object = rig.arm
TAU = 2 * math.pi
def P(): return {'rot': {}, 'loc': {}, 'aim': {}}
REST_TILT = (8, -5, 0)
def pose(tilt, z=0.0, collar_z=0.0, clap=0.0):
    ch = P(); ch['rot']['bell'] = tilt; ch['loc']['bell'] = (0, 0, z); ch['loc']['collar'] = (0, 0, collar_z)
    # the clapper hangs under gravity (armature space), lagging the bell's tilt by `clap` deg about X
    ch['aim']['clapper'] = tuple(Vector((0, math.sin(math.radians(clap)), -math.cos(math.radians(clap)))).normalized())
    return ch
def surface(t):
    u = key(t, [(0, 0), (0.6, 1.06), (0.9, 1)])
    return pose((24 - 16 * min(1, u), -5, 0), -0.85 * (1 - u), -0.1 * (1 - min(1, u * 1.6)), ring(t, 0.55, 15, 2, 3))
def bellloop(t):
    p = TAU * t / 2.0
    return pose((REST_TILT[0] + 6 * math.sin(p), REST_TILT[1] + 4 * math.sin(p + 1.1), 0), 0.015 * math.sin(p), 0, 10 * math.sin(p - 0.9))
def toll(t):
    T = 0.12
    a = key(t, [(0, 8), (T, 30)]) if t <= T else 30 * math.exp(-2.8 * (t - T)) * math.cos(TAU * 2.0 * (t - T)) + 8 * key(t, [(T, 0), (1.0, 1)])
    return pose((a, -5, 0), 0.06 * snap(t, T, 0.05, 0.3), 0, -a * 0.6 + 34 * snap(t, T, 0.04, 0.14))
def sink(t):
    u = key(t, [(0, 0), (0.9, 1)])
    return pose((8 + 24 * u, -5 - 10 * u, 0), -0.9 * u, -0.16 * u, 10 * u)
for n, s, fn in [('Surface', 0.9, surface), ('BellLoop', 2.0, bellloop), ('Toll', 1.0, toll), ('Sink', 0.9, sink)]: rig.bake(n, s, fn)
rig.export(work, 'silt_bell', os.path.join(outdir, 'SiltBell.glb'), first='BellLoop')

# ================= SiltMound (static) =================
sc = reset(); M = mats(); parts = []
def mlobes(th, z):
    # uneven lobes (different sizes), fading toward the crown so the top stays a rounded dome
    k = 1 - min(1, z / 0.26) * 0.7
    return 1 + k * (0.17 * math.sin(3 * th + 0.3) + 0.10 * math.sin(5 * th + 2.1) + 0.06 * math.sin(11 * th + 0.9))
HEAP = [(0.70, 0.0), (0.65, 0.035), (0.56, 0.095), (0.45, 0.165), (0.31, 0.225), (0.16, 0.265), (0.0, 0.28)]
def hz(r):   # the heap's height at radius r (for setting things on it)
    for (r0, z0), (r1, z1) in zip(HEAP, HEAP[1:]):
        if r1 <= r <= r0: return z0 + (z1 - z0) * (r0 - r) / (r0 - r1)
    return 0.0
heap = lathe(sc, 'heap', HEAP, 40, mlobes)
jitter(heap, 0.035, 4, 1, floor=0.001); finish(parts, heap, None, M['silt'])
wetr = lathe(sc, 'wetring', [(0.76, 0.0), (0.72, 0.012), (0.66, 0.02)], 40, mlobes); finish(parts, wetr, None, M['silt_wet'])
for i, (a, r, rad) in enumerate([(20, 0.28, 0.16), (140, 0.36, 0.13), (250, 0.22, 0.12), (300, 0.46, 0.10)]):
    c = prim('ico', f'clod{i}', loc=(r * math.sin(math.radians(a)), -r * math.cos(math.radians(a)), hz(r) + rad * 0.25), subdivisions=2, radius=rad)
    c.scale = (1.1, 0.9, 0.65); bpy.ops.object.transform_apply(scale=True); jitter(c, rad * 0.25, 9, i); finish(parts, c, None, M['silt'])
# a broken spatter ring: flat blobs thrown out to r 0.72-0.9, with gaps
for i, a in enumerate([10, 48, 95, 128, 170, 205, 262, 300, 335]):
    a += random.uniform(-10, 10); r = random.uniform(0.74, 0.88); rad = random.uniform(0.05, 0.1)
    b = prim('uv', f'spat{i}', loc=(r * math.sin(math.radians(a)), -r * math.cos(math.radians(a)), 0.03), segments=10, ring_count=5, radius=rad)
    b.scale = (1.4, 0.9, 0.35); bpy.ops.object.transform_apply(scale=True); b.rotation_euler.z = math.radians(a); bpy.ops.object.transform_apply(rotation=True)
    finish(parts, b, None, M['silt'])
for i in range(9):
    a = random.uniform(0, 2 * math.pi); r = random.uniform(0.1, 0.6)
    f = prim('ico', f'fleck{i}', loc=(r * math.sin(a), -r * math.cos(a), hz(r) + 0.005), subdivisions=1, radius=random.uniform(0.03, 0.045)); f.scale = (1.3, 1, 0.5); bpy.ops.object.transform_apply(scale=True); finish(parts, f, None, M['fleck'], False)
mound = join(parts, 'SiltMound')
if TEXTURED:
    import silt_carillon_texture as tex
    tex.apply_prop(mound, work, 'SiltMound')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'silt_mound.blend'))
bpy.ops.object.select_all(action='DESELECT'); mound.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(outdir, 'SiltMound.glb'), export_format='GLB', use_selection=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', os.path.join(outdir, 'SiltMound.glb'), os.path.getsize(os.path.join(outdir, 'SiltMound.glb')))
