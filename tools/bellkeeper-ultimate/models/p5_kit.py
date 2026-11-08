# blender -b -P p5_kit.py -- <out dir> [<preview.png>] [--nobake]
# Bellkeeper Ultimate v2 · P5 Final Toll (proposal, ART-PLAN-v2.md batch 3), one GLB each, sizes from FinalToll.cs:
#   FT_BellFloor  the floor inside the landed bell: dark bronze, out to the bell's inner wall (r 12.25), engraved rings and
#                 twelve ivory hour inlays near the rim. Top at Z 0.02 (telegraphs draw just above it).
#   FT_Platform   a Last Stand platform: a broken piece of the bell's lip laid flat, r 1.9 (PlatR), top at Z 0.12, jagged edge.
#   FT_Plate      the SOAK plate: a brass dish r 1.6 (PlateSize) with an hour ring, ivory centre.
#   FT_Clapper    the clapper that strikes the lane: an iron rod and a bronze teardrop, pivot at the rod's top (origin),
#                 hanging down -Z (9 m to the ball's centre). The code swings it across the lane on the toll.
#   FT_BellShard  a falling bell shard (the Last Stand): a curved bronze fragment ~1.2 m, brass outside, dark inside.
# Style: gpt/02-visual-style-guide.md (aged brass, dark bronze, ivory inlays, painterly breakup; no grime). Z up, metres.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector

args = [a for a in (sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []) if not a.startswith('--')]
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'out')
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT, exist_ok=True)
random.seed(23)

def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexc, metal=0.0, rough=0.6, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(hexc), 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*lin(emit), 1); b.inputs['Emission Strength'].default_value = strength
    return m

BRONZE = mat('FT_BronzeDark', '#4A3219', 0.55, 0.6)
BRASS = mat('FT_Brass', '#C38D46', 0.85, 0.4)
IVORY = mat('FT_Ivory', '#F4E9D1', 0.0, 0.5)
IRON = mat('FT_Iron', '#2E2A3C', 0.5, 0.45)
GOLD = mat('FT_Gold', '#F6C968', 0.6, 0.3, '#F6C968', 0.5)

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

def ring_prism(name, r0, r1, z0, z1, material, seg=128, a0=0.0, a1=2 * math.pi):
    # an annulus (or an arc of one) extruded from z0 to z1
    full = abs(a1 - a0 - 2 * math.pi) < 1e-6; n = seg if full else max(2, int(seg * (a1 - a0) / (2 * math.pi)))
    bm = bmesh.new(); rows = []
    for i in range(n + (0 if full else 1)):
        a = a0 + (a1 - a0) * i / n; c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((c * r, s * r, z)) for (r, z) in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    m = len(rows)
    for i in range(m if full else m - 1):
        a, b = rows[i], rows[(i + 1) % m]
        for k in range(4): bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))
    if not full: bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    return link(name, bm, material, True)

def disc(name, r, z0, z1, material, seg=128):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=z1 - z0)
    bmesh.ops.translate(bm, vec=(0, 0, (z0 + z1) / 2), verts=bm.verts)
    return link(name, bm, material, False)

# ---------------- FT_BellFloor ----------------
def bell_floor(dst):
    parts = [disc('base', 12.25, -0.12, 0.02, BRONZE, 160)]
    for r in (2.3, 5.5, 9.0, 11.6):   # engraved rings: thin raised brass
        parts.append(ring_prism('ring', r - 0.06, r + 0.06, 0.02, 0.032, BRASS, 160))
    for h in range(12):   # ivory hour inlays near the rim, and short brass ticks between them
        a = math.radians(90 - 30 * h)
        bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
        bmesh.ops.scale(bm, vec=(0.9 if h % 3 == 0 else 0.55, 0.22, 0.012), verts=bm.verts)
        bmesh.ops.translate(bm, vec=(10.3, 0, 0.026), verts=bm.verts)
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=__import__('mathutils').Matrix.Rotation(a, 3, 'Z'), verts=bm.verts)
        parts.append(link('hour', bm, IVORY))
    parts.append(disc('boss', 1.2, 0.02, 0.035, BRASS, 64))
    join(dst, parts)

# ---------------- FT_Platform ----------------
def platform(dst):
    # an irregular, roughly round slab with a jagged broken edge; the outward side keeps a strip of the bell's lip (brass bow)
    seg = 28; R = 1.9; bm = bmesh.new(); top, bot = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg; r = R * (1 + random.uniform(-0.06, 0.04)) + (0.08 if i % 3 == 0 else 0)
        top.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, 0.12 + random.uniform(-0.01, 0.01))))
        bot.append(bm.verts.new((math.cos(a) * r * 0.82, math.sin(a) * r * 0.82, -0.55 + random.uniform(-0.15, 0.1))))
    bm.faces.new(top); bm.faces.new(list(reversed(bot)))
    for k in range(seg): bm.faces.new((bot[k], bot[(k + 1) % seg], top[(k + 1) % seg], top[k]))
    slab = link('slab', bm, BRONZE, False, 0.04)
    bow = ring_prism('bow', 1.55, 1.85, 0.12, 0.2, BRASS, 64, math.radians(-60), math.radians(60))   # +X = outward (code faces it)
    inl = ring_prism('inlay', 0.9, 0.98, 0.12, 0.135, IVORY, 64)
    join(dst, [slab, bow, inl])

# ---------------- FT_Plate ----------------
def plate(dst):
    parts = [disc('dish', 1.6, 0.0, 0.08, BRASS, 96), ring_prism('lip', 1.42, 1.6, 0.08, 0.14, BRASS, 96),
             ring_prism('hours', 0.95, 1.05, 0.08, 0.095, BRONZE, 96), disc('centre', 0.42, 0.08, 0.11, IVORY, 48)]
    for h in range(12):
        a = 2 * math.pi * h / 12; bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
        bmesh.ops.scale(bm, vec=(0.2, 0.06, 0.02), verts=bm.verts); bmesh.ops.translate(bm, vec=(1.22, 0, 0.09), verts=bm.verts)
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=__import__('mathutils').Matrix.Rotation(a, 3, 'Z'), verts=bm.verts)
        parts.append(link('tick', bm, IVORY))
    join(dst, parts)

# ---------------- FT_Clapper ----------------
def clapper(dst):
    L = 9.0
    rod = disc('rod', 0.16, -L + 0.6, 0.0, IRON, 16)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.45, minor_radius=0.12, location=(0, 0, 0.2), rotation=(math.radians(90), 0, 0)); eye = bpy.context.active_object; eye.data.materials.append(IRON)
    # the teardrop: a lathe, widest low down
    prof = [(0.0, 0.9), (0.35, 0.75), (0.7, 0.3), (0.95, -0.25), (1.05, -0.7), (0.9, -1.1), (0.55, -1.35), (0.0, -1.42)]
    bm = bmesh.new(); seg = 32; rings = []
    for (r, z) in prof:
        rings.append([bm.verts.new((math.cos(2 * math.pi * i / seg) * r, math.sin(2 * math.pi * i / seg) * r, z - L)) for i in range(seg)])
    for j in range(len(prof) - 1):
        for i in range(seg):
            try: bm.faces.new((rings[j][i], rings[j][(i + 1) % seg], rings[j + 1][(i + 1) % seg], rings[j + 1][i]))
            except ValueError: pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
    ball = link('ball', bm, BRONZE, True)
    band = ring_prism('band', 0.98, 1.08, -L - 0.62, -L - 0.42, BRASS, 48)
    join(dst, [rod, eye, ball, band])

# ---------------- FT_BellShard ----------------
def bell_shard(dst):
    # a curved fragment of the bell's wall: an arc of a thick shell with a jagged outline
    bm = bmesh.new(); n = 7; R0, R1 = 2.6, 2.95; outer, inner = [], []
    outline = []
    for i in range(n + 1):
        a = math.radians(-14 + 28 * i / n); outline.append((a, 0.55 + random.uniform(-0.15, 0.15)))
    for i in range(n, -1, -1):
        a = math.radians(-14 + 28 * i / n); outline.append((a, -0.55 + random.uniform(-0.15, 0.15)))
    for (a, z) in outline:
        outer.append(bm.verts.new((math.cos(a) * R1 - 2.78, math.sin(a) * R1, z)))
        inner.append(bm.verts.new((math.cos(a) * R0 - 2.78, math.sin(a) * R0, z * 0.92)))
    bm.faces.new(outer); bm.faces.new(list(reversed(inner)))
    m = len(outer)
    for k in range(m): bm.faces.new((inner[k], inner[(k + 1) % m], outer[(k + 1) % m], outer[k]))
    sh = link('shell', bm, BRASS, False, 0.03)
    join(dst, [sh])

KIT = [('FT_BellFloor', bell_floor), ('FT_Platform', platform), ('FT_Plate', plate), ('FT_Clapper', clapper), ('FT_BellShard', bell_shard)]
TEX = {'FT_BellFloor': 2048, 'FT_Platform': 1024, 'FT_Plate': 1024, 'FT_Clapper': 1024, 'FT_BellShard': 512}
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'texture_bake.py'), encoding='utf-8').read())
BAKE = '--nobake' not in sys.argv
for name, build in KIT:
    reset(); build(name)
    if BAKE: paint_and_bake(bpy.data.objects[name], TEX[name])
    export(name)

if PREVIEW:
    reset()
    bell_floor('fl')
    for h in range(4):
        platform('pl%d' % h); a = math.radians(45 + 90 * h); p = bpy.data.objects['pl%d' % h]; p.location = (math.cos(a) * 3.4, math.sin(a) * 3.4, 0.3); p.rotation_euler = (0, 0, a)
    plate('pt'); bpy.data.objects['pt'].location = (5.5, 0, 0)
    clapper('cl'); c = bpy.data.objects['cl']; c.location = (0, 0, 11.5); c.rotation_euler = (math.radians(35), 0, 0)
    for i in range(3):
        bell_shard('bs%d' % i); s = bpy.data.objects['bs%d' % i]; s.location = (-4 + 3 * i, -5, 2 + i); s.rotation_euler = (random.random(), random.random(), random.random())
    sc = bpy.context.scene
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (*lin('#2A2147'), 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(45), 0, math.radians(30)); sun.data.energy = 3.5
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (0, -22, 24); cam.rotation_euler = (math.radians(42), 0, 0); cam.data.lens = 30
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True); print('PREVIEW', PREVIEW)
