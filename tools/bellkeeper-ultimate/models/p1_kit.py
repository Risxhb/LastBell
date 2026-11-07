# blender -b -P p1_kit.py -- <out dir> [<preview.png>] [--nobake]
# Bellkeeper Ultimate v2 · P1-P3 props (proposal, ART-PLAN-v2.md batch 4), shared by the Escapement Relay (P1), the Broken
# Hour (P2) and the Open Hour (P3), which all use four hour plates at 7 m and a hand over the boss. One GLB each:
#   CR_Plate  an hour plate: a brass dish r 1.6 (the code keeps a thin glowing rim round it to show the lit plate), an ivory
#             inner ring, twelve ticks and a centre rosette. Top at Z 0.1.
#   CR_Hand   the great clock hand over the boss's head: an ornate brass spade hand pointing +X, 0 (its arbor) to 6.6 m at the
#             tip, with a counterweight tail to -1.4 m and a ring round the arbor. 0.14 m thick.
# Style: gpt/02-visual-style-guide.md (aged brass, ivory, crafted bevels, painterly breakup). Z up, metres.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

args = [a for a in (sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []) if not a.startswith('--')]
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'out')
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT, exist_ok=True)
random.seed(5)

def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexc, metal=0.0, rough=0.6):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(hexc), 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    return m

BRASS = mat('CR_Brass', '#C38D46', 0.85, 0.4)
BRASS_DARK = mat('CR_BrassDark', '#8A6230', 0.8, 0.5)
IVORY = mat('CR_Ivory', '#F4E9D1', 0.0, 0.5)

def reset():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)

def link(name, bm, material, smooth=False, bevel=0.0):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.data.materials.append(material)
    if bevel > 0:
        bv = o.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 2; bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(40)
    return o

def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active; j.name = name; return j

def export(name):
    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True)
    print('EXPORTED', path, os.path.getsize(path))

def disc(name, r, z0, z1, material, seg=96, bevel=0.0):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=z1 - z0)
    bmesh.ops.translate(bm, vec=(0, 0, (z0 + z1) / 2), verts=bm.verts)
    return link(name, bm, material, False, bevel)

def ring(name, r0, r1, z0, z1, material, seg=96):
    bm = bmesh.new(); rows = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((c * r, s * r, z)) for (r, z) in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    for i in range(seg):
        a, b = rows[i], rows[(i + 1) % seg]
        for k in range(4): bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))
    return link(name, bm, material, True)

def box(name, size, at, rot_z, material):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1); bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=at, verts=bm.verts); bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rot_z, 3, 'Z'), verts=bm.verts)
    return link(name, bm, material)

# ---------------- CR_Plate ----------------
def plate(dst):
    parts = [disc('dish', 1.6, 0.0, 0.08, BRASS, 96, 0.02), ring('lip', 1.42, 1.6, 0.08, 0.12, BRASS_DARK),
             ring('inlay', 0.98, 1.08, 0.08, 0.095, IVORY), disc('boss', 0.36, 0.08, 0.13, BRASS, 48, 0.01)]
    for h in range(12):
        parts.append(box('tick', (0.26 if h % 3 == 0 else 0.16, 0.05, 0.02), (1.24, 0, 0.09), 2 * math.pi * h / 12, IVORY))
    for p in range(6):   # the rosette's petals
        parts.append(box('petal', (0.34, 0.12, 0.03), (0.5, 0, 0.095), 2 * math.pi * p / 6 + math.pi / 6, BRASS_DARK))
    join(dst, parts)

# ---------------- CR_Hand ----------------
def flat(name, outline, z0, z1, material, bevel=0.03):
    bm = bmesh.new(); top = [bm.verts.new((x, y, z1)) for (x, y) in outline]; bot = [bm.verts.new((x, y, z0)) for (x, y) in outline]
    bm.faces.new(top); bm.faces.new(list(reversed(bot)))
    n = len(outline)
    for k in range(n): bm.faces.new((bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]))
    return link(name, bm, material, False, bevel)

def hand(dst):
    T0, T1 = -0.07, 0.07
    # the shaft out to the spade, then the spade (a heart-shaped head) and the tip
    shaft = [(0.4, -0.22), (4.5, -0.13), (4.5, 0.13), (0.4, 0.22)]
    # the spade: two lobes swelling either side, a waist, then a long pointed tip (mirrored in Y)
    half = [(4.35, 0.12), (4.55, 0.42), (4.9, 0.66), (5.3, 0.62), (5.55, 0.38), (5.7, 0.2), (6.6, 0.0)]
    spade = half + [(x, -y) for (x, y) in reversed(half[:-1])]
    tail = [(-0.4, -0.12), (-1.0, -0.1), (-1.0, 0.1), (-0.4, 0.12)]
    parts = [flat('shaft', shaft, T0, T1, BRASS), flat('spade', spade, T0, T1, BRASS), flat('tail', tail, T0, T1, BRASS)]
    parts.append(disc('weight', 0.42, T0, T1, BRASS_DARK, 48, 0.03)); parts[-1].location.x = -1.35
    parts.append(ring('arbor', 0.32, 0.5, T0 - 0.02, T1 + 0.02, BRASS_DARK, 48))
    parts.append(disc('pin', 0.18, T0 - 0.05, T1 + 0.05, IVORY, 32))
    # a pierced ring in the spade, like a watch hand's
    r2 = ring('eye', 0.18, 0.26, T1, T1 + 0.015, IVORY, 32); r2.location.x = 5.25; parts.append(r2)
    join(dst, parts)

KIT = [('CR_Plate', plate), ('CR_Hand', hand)]
TEX = {'CR_Plate': 1024, 'CR_Hand': 1024}
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'texture_bake.py'), encoding='utf-8').read())
BAKE = '--nobake' not in sys.argv
for name, build in KIT:
    reset(); build(name)
    if BAKE: paint_and_bake(bpy.data.objects[name], TEX[name])
    export(name)

if PREVIEW:
    reset()
    for i in range(4):
        plate('p%d' % i); a = math.radians(90 * i); bpy.data.objects['p%d' % i].location = (math.cos(a) * 7, math.sin(a) * 7, 0)
    hand('h'); h = bpy.data.objects['h']; h.location = (0, 0, 4.2); h.rotation_euler = (0, 0, math.radians(90))
    sc = bpy.context.scene
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (*lin('#2A2147'), 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(45), 0, math.radians(30)); sun.data.energy = 3.5
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (0, -20, 20); cam.rotation_euler = (math.radians(45), 0, 0); cam.data.lens = 30
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True); print('PREVIEW', PREVIEW)
