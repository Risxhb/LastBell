# blender -b -P transitions_kit.py -- <out dir> [<preview.png>]
# Bellkeeper Ultimate v2 transitions (proposal, ART-PLAN-v2.md batch 2), one GLB each:
#   FB_Floor      The Floor Breaks: the arena floor as ~130 slate flagstones in rings round the boss (r 1.2 -> 11.9 m), every
#                 stone its own node (Tile_<ring>_<i>) so GearFloor.cs can crack and drop them in the two waves. Grout gaps
#                 show the void beneath. Top at Z 0.05 (over the pit disc, under the gears at 0.1).
#   GB_GreatBell  The Train Runs Wild ends with the great bell coming down over the arena; P5 is fought inside it. Two nodes:
#                 Wall (the flared lip up to 1.4 m: what you see from inside) and Dome (hidden once it has landed, so the gameplay
#                 camera, 13 m up, looks down into the bell). Mouth r 12.8 m, 20 m tall, brass outside, dark bronze inside.
# Style: gpt/02-visual-style-guide.md (slate blue stone with broad planar breakup; aged brass; no grime). Blender Z up, metres.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'out')
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT, exist_ok=True)
random.seed(11)

def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexc, metal=0.0, rough=0.6, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(hexc), 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*lin(emit), 1); b.inputs['Emission Strength'].default_value = strength
    return m

SLATE = [mat('FB_Slate%d' % i, c, 0.0, 0.75) for i, c in enumerate(('#314C69', '#2B4560', '#36536F', '#28405A'))]
BRASS = mat('GB_Brass', '#C38D46', 0.85, 0.4)
BRONZE_IN = mat('GB_BronzeInside', '#5A3B1C', 0.6, 0.6)
GOLDTRIM = mat('GB_Gold', '#F6C968', 0.6, 0.3, '#F6C968', 0.6)
IVORY = mat('GB_Ivory', '#F4E9D1', 0.0, 0.5)

def reset():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)

def link(name, bm, material, smooth=False, bevel=0.0):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.data.materials.append(material)
    if bevel > 0:
        bv = o.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 1; bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(40)
    return o

def export(name):
    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True)
    print('EXPORTED', path, os.path.getsize(path))

# ---------------- FB_Floor ----------------
RINGS = [1.2, 2.6, 4.2, 5.8, 7.4, 9.0, 10.4, 11.9, 13.5]   # out to the arena floor's edge (Area 4 builder: R 13.5)
TOP, DEPTH, GROUT = 0.05, 0.18, 0.06

def stone(name, r0, r1, a0, a1, material):
    # an annular-sector flagstone, its pivot at its own centre (so code can tip it), slightly domed and jittered
    seg = max(2, int((a1 - a0) / math.radians(6)))
    cr, ca = (r0 + r1) / 2, (a0 + a1) / 2
    centre = Vector((math.cos(ca) * cr, math.sin(ca) * cr, 0))
    g = GROUT / 2
    pts = []
    for i in range(seg + 1):
        a = a0 + (a1 - a0) * i / seg; aa = a + (g / r0 if i == 0 else -g / r0 if i == seg else 0)
        pts.append(Vector((math.cos(aa) * (r0 + g), math.sin(aa) * (r0 + g), 0)))
    for i in range(seg, -1, -1):
        a = a0 + (a1 - a0) * i / seg; aa = a + (g / r1 if i == 0 else -g / r1 if i == seg else 0)
        pts.append(Vector((math.cos(aa) * (r1 - g), math.sin(aa) * (r1 - g), 0)))
    h = TOP + random.uniform(-0.012, 0.012)
    bm = bmesh.new()
    top = [bm.verts.new(p - centre + Vector((0, 0, h))) for p in pts]
    bot = [bm.verts.new(p - centre + Vector((0, 0, h - DEPTH))) for p in pts]
    bm.faces.new(top); bm.faces.new(list(reversed(bot)))
    n = len(pts)
    for k in range(n): bm.faces.new((bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]))
    o = link(name, bm, material, False, 0.03); o.location = centre
    return o

def floor():
    count = 0
    for ri in range(len(RINGS) - 1):
        r0, r1 = RINGS[ri], RINGS[ri + 1]
        n = max(6, int(round(2 * math.pi * (r0 + r1) / 2 / 2.3)))
        off = random.uniform(0, 2 * math.pi / n)
        cuts = sorted(off + 2 * math.pi * (i + random.uniform(-0.22, 0.22)) / n for i in range(n))
        for i in range(n):
            a0 = cuts[i]; a1 = cuts[(i + 1) % n] + (2 * math.pi if i == n - 1 else 0)
            stone('Tile_%d_%d' % (ri, i), r0, r1, a0, a1, random.choice(SLATE)); count += 1
    print('TILES', count)

# ---------------- GB_GreatBell ----------------
# lathe profile (r, z) from the mouth up the outside; the inside is the same shrunk by the wall thickness
PROFILE = [(12.8, 0.0), (12.95, 0.5), (12.3, 1.4), (11.0, 2.4), (9.6, 3.6), (8.6, 5.6), (8.2, 8.5), (8.1, 11.5), (7.8, 14.0), (7.0, 16.4), (5.2, 18.4), (2.6, 19.6), (0.0, 20.0)]
THICK = 0.55

def lathe(name, prof, material, seg=72, inside=False):
    bm = bmesh.new(); rings = []
    for (r, z) in prof:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg; ring.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)))
        rings.append(ring)
    for j in range(len(prof) - 1):
        for i in range(seg):
            a, b, c, d = rings[j][i], rings[j][(i + 1) % seg], rings[j + 1][(i + 1) % seg], rings[j + 1][i]
            try: bm.faces.new((d, c, b, a) if inside else (a, b, c, d))
            except ValueError: pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
    return link(name, bm, material, True)

def band(name, r, z0, z1, material, seg=72):
    bm = bmesh.new(); vs = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; c, s = math.cos(a), math.sin(a)
        vs.append([bm.verts.new((c * rr, s * rr, z)) for rr in (r, r + 0.25) for z in (z0, z1)])
    for i in range(seg):
        a, b = vs[i], vs[(i + 1) % seg]
        bm.faces.new((a[3], b[3], b[2], a[2])); bm.faces.new((a[1], b[1], b[3], a[3])); bm.faces.new((a[2], b[2], b[0], a[0]))
    return link(name, bm, material, True)

def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active; j.name = name; return j

def bell():
    split = 1.4   # only the flared lip stays once landed: a wall curving in overhead would hide the arena edge from the camera
    lower = [p for p in PROFILE if p[1] <= split]; upper = [p for p in PROFILE if p[1] >= split]
    inner = lambda prof: [(max(0.0, r - THICK), z) for (r, z) in prof]
    wall = [lathe('w_out', lower, BRASS), lathe('w_in', inner(lower), BRONZE_IN, inside=True),
            band('w_bow', 12.85, 0.25, 0.75, GOLDTRIM), band('w_band', 12.75, 0.95, 1.15, IVORY)]
    # the lip joining outside to inside at the mouth
    bm = bmesh.new(); seg = 72; o_, i_ = [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg; o_.append(bm.verts.new((math.cos(a) * 12.8, math.sin(a) * 12.8, 0))); i_.append(bm.verts.new((math.cos(a) * (12.8 - THICK), math.sin(a) * (12.8 - THICK), 0)))
    for i in range(seg): bm.faces.new((i_[i], i_[(i + 1) % seg], o_[(i + 1) % seg], o_[i]))
    wall.append(link('w_lip', bm, BRASS, True))
    join('Wall', wall)
    dome = [lathe('d_out', upper, BRASS), lathe('d_in', inner(upper), BRONZE_IN, inside=True), band('d_band', 8.15, 9.6, 10.1, GOLDTRIM), band('d_band2', 7.85, 13.6, 14.0, IVORY)]
    # the crown: a hanging loop (canon) on top
    bpy.ops.mesh.primitive_torus_add(major_radius=1.8, minor_radius=0.45, location=(0, 0, 21.4), rotation=(math.radians(90), 0, 0)); t = bpy.context.active_object; t.data.materials.append(BRASS); dome.append(t)
    join('Dome', dome)

reset(); floor(); export('FB_Floor')
reset(); bell(); export('GB_GreatBell')

if PREVIEW:
    reset(); floor()
    bell(); bpy.data.objects['Wall'].location.z = 4.0; bpy.data.objects['Dome'].location.z = 4.0
    for o in bpy.data.objects:
        if o.name.startswith('Tile_') and random.random() < 0.18: o.location.z -= random.uniform(0.6, 2.5); o.rotation_euler = (random.uniform(-0.6, 0.6), random.uniform(-0.6, 0.6), 0)
    sc = bpy.context.scene
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (*lin('#655491'), 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(50), 0, math.radians(35)); sun.data.energy = 3.5
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (0, -42, 30); cam.rotation_euler = (math.radians(58), 0, 0); cam.data.lens = 28
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True); print('PREVIEW', PREVIEW)
