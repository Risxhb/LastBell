# blender -b -P gear_kit.py -- <out dir> [<preview.png>]
# Bellkeeper Ultimate v2 P4 · The Gear Floor (proposal, ART-PLAN-v2.md batch 1): the gear train as static props, one GLB
# each, replacing GearFloor.cs's stand-in primitives. Sizes match the rules (GearFloor.cs): outer gear r 4.0 (7.2 m out),
# centre gear r 3.6, core r 0.9, pawl from 2.0 to 4.0 m on the outer rim side, the mesh bite zone r 1.45.
# Style (gpt/02-visual-style-guide.md): painterly low-poly, brass rims and teeth, slate-blue enamel faces, resonant gold
# only on the cores, ember coral only on hostile parts (pawl tips). Blender Z up, metres; glTF converts to Unity Y up.
# Every walkable top sits at Z = 0.03 so characters stand on it, not in it.
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix, Euler

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'out')
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT, exist_ok=True)

def lin(h):   # sRGB hex -> linear
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, hexc, metal=0.0, rough=0.6, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(hexc), 1); b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*lin(emit), 1); b.inputs['Emission Strength'].default_value = strength
    return m

BRASS = mat('GF_Brass', '#C38D46', 0.85, 0.38)
BRASS_DK = mat('GF_BrassAged', '#8A5E2B', 0.85, 0.5)
ENAMEL = mat('GF_Enamel', '#314C69', 0.1, 0.55)
ENAMEL_DK = mat('GF_EnamelDeep', '#17263F', 0.1, 0.6)
IVORY = mat('GF_Ivory', '#F4E9D1', 0.0, 0.5)
GOLD = mat('GF_CoreGold', '#F6C968', 0.3, 0.3, '#F6C968', 2.5)
CORAL = mat('GF_Coral', '#EB685D', 0.0, 0.45, '#EB685D', 1.6)

def xf(rot=(0, 0, 0), loc=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))

def reset():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)

def obj(name, bm, material, smooth=False, bevel=0.0):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); o.data.materials.append(material)
    if bevel > 0:
        bv = o.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 2; bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(35)
    return o

def cyl(name, r, z0, z1, material, seg=48, r_top=None, loc=(0, 0), bevel=0.02, smooth=True):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r_top is None else r_top, depth=z1 - z0)
    bmesh.ops.translate(bm, vec=(loc[0], loc[1], (z0 + z1) / 2), verts=bm.verts)
    return obj(name, bm, material, smooth, bevel)

def annulus(name, r0, r1, z0, z1, material, seg=64, bevel=0.015):
    bm = bmesh.new(); vs = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; c, s = math.cos(a), math.sin(a)
        vs.append([bm.verts.new((c * r, s * r, z)) for r in (r0, r1) for z in (z0, z1)])
    for i in range(seg):
        a, b = vs[i], vs[(i + 1) % seg]   # [inner low, inner high, outer low, outer high]
        bm.faces.new((a[1], b[1], b[3], a[3]))   # top
        bm.faces.new((a[2], b[2], b[0], a[0]))   # bottom
        bm.faces.new((a[3], b[3], b[2], a[2]))   # outer wall
        bm.faces.new((a[0], b[0], b[1], a[1]))   # inner wall
    return obj(name, bm, material, True, bevel)

def box(name, size, loc, material, rot=(0, 0, 0), bevel=0.02):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.transform(bm, matrix=xf(rot, loc, size), verts=bm.verts)
    return obj(name, bm, material, False, bevel)

def teeth(name, r_root, r_tip, n, z0, z1, material, width=0.55, tip=0.32):
    # trapezoid teeth round the rim (painterly: chunky, few faces)
    bm = bmesh.new()
    for i in range(n):
        a = 2 * math.pi * i / n; d = Vector((math.cos(a), math.sin(a), 0)); t = Vector((-d.y, d.x, 0))
        quad = [d * r_root + t * width / 2, d * r_tip + t * tip / 2, d * r_tip - t * tip / 2, d * r_root - t * width / 2]
        lo = [bm.verts.new((p.x, p.y, z0)) for p in quad]; hi = [bm.verts.new((p.x, p.y, z1)) for p in quad]
        bm.faces.new(hi); bm.faces.new(list(reversed(lo)))
        for k in range(4): bm.faces.new((lo[k], lo[(k + 1) % 4], hi[(k + 1) % 4], hi[k]))
    return obj(name, bm, material, False, 0.02)

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

TOP = 0.03   # walkable surface height

def outer_gear(dst):
    # r 4.0 to the tooth tips: enamel face, brass rim, four spokes, a hub ring round the core, 18 teeth
    parts = [cyl('face', 3.55, -0.10, TOP - 0.01, ENAMEL, 72),
             annulus('rim', 3.45, 3.78, -0.10, TOP, BRASS, 72),
             teeth('teeth', 3.72, 4.05, 18, -0.10, TOP - 0.005, BRASS),
             annulus('hubring', 0.95, 1.25, -0.10, TOP, BRASS_DK, 48),
             annulus('inlay', 2.35, 2.45, -0.02, TOP - 0.004, BRASS_DK, 64)]
    for k in range(4):
        a = 90 * k + 45
        parts.append(box('spoke%d' % k, (2.25, 0.32, 0.05), (math.cos(math.radians(a)) * 2.35, math.sin(math.radians(a)) * 2.35, TOP - 0.02), BRASS, (0, 0, a)))
    join(dst, parts)

def centre_gear(dst):
    # r 3.6: deeper enamel with an ivory hour ring under the boss, 12 teeth
    parts = [cyl('face', 3.25, -0.10, TOP - 0.01, ENAMEL_DK, 72),
             annulus('rim', 3.15, 3.45, -0.10, TOP, BRASS, 72),
             teeth('teeth', 3.40, 3.70, 12, -0.10, TOP - 0.005, BRASS, 0.75, 0.42),
             annulus('hours', 2.2, 2.32, -0.02, TOP - 0.004, IVORY, 64)]
    for k in range(12):
        a = math.radians(30 * k); parts.append(box('tick%d' % k, (0.36, 0.08, 0.03), (math.cos(a) * 2.7, math.sin(a) * 2.7, TOP - 0.012), BRASS_DK, (0, 0, 30 * k)))
    join(dst, parts)

def core(dst):
    # the hub: a brass drum with a gold glowing heart band and cap (hit it to hurt the boss)
    parts = [cyl('base', 0.92, 0.0, 0.25, BRASS_DK, 32), cyl('drum', 0.82, 0.25, 1.05, BRASS, 32),
             annulus('heart', 0.80, 0.90, 0.45, 0.85, GOLD, 32), cyl('cap', 0.62, 1.05, 1.25, BRASS, 32, 0.35),
             cyl('gem', 0.22, 1.25, 1.45, GOLD, 16, 0.05)]
    join(dst, parts)

def pawl(dst):
    # the ratchet arm on the outer side (2.0 -> 4.0 m from the gear centre along +X): brass, with a coral hostile edge
    parts = [box('arm', (2.0, 0.32, 0.42), (1.0, 0, 0.21), BRASS_DK, (0, 0, 0), 0.04),
             box('edge', (2.0, 0.36, 0.06), (1.0, 0, 0.45), CORAL, (0, 0, 0), 0.01),
             cyl('pin', 0.26, 0.0, 0.55, BRASS, 16, loc=(0, 0)),
             box('claw', (0.45, 0.5, 0.42), (2.0, 0, 0.21), BRASS, (0, 0, 20), 0.04)]
    join(dst, parts)

def hammer(dst):
    # the bell-hammer that falls onto the hammered gear: striking face at Z 0 (code lowers it from above), r 1.7
    parts = [cyl('head', 1.7, 0.0, 1.5, BRASS, 40, 1.55), annulus('band1', 1.66, 1.82, 0.15, 0.35, BRASS_DK, 40),
             annulus('band2', 1.6, 1.76, 1.15, 1.35, BRASS_DK, 40), cyl('face', 1.5, -0.04, 0.02, IVORY, 40),
             cyl('neck', 0.55, 1.5, 2.2, BRASS_DK, 20, 0.4), cyl('shaft', 0.32, 2.2, 9.0, ENAMEL_DK, 12, 0.32, bevel=0.01),
             annulus('collar', 0.3, 0.5, 4.0, 4.3, BRASS, 20)]
    join(dst, parts)

def shard(dst):
    # a brass splinter (a stretched octahedron), 0.5 m, pointing along +X (its flight direction)
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=0, radius=0.2)
    bmesh.ops.scale(bm, vec=(1.6, 0.6, 0.6), verts=bm.verts)
    obj(dst, bm, BRASS, False)

KIT = [('GF_OuterGear', outer_gear), ('GF_CentreGear', centre_gear), ('GF_Core', core), ('GF_Pawl', pawl),
       ('GF_Hammer', hammer), ('GF_Shard', shard)]
for name, build in KIT:
    reset(); build(name); export(name)

if PREVIEW:
    # the whole floor laid out as in the game (N/E/S/W at 7.2 m), from the gameplay camera's angle
    reset()
    for name, build in KIT:
        if name in ('GF_OuterGear',):
            for g in range(4):
                build('og%d' % g); o = bpy.data.objects['og%d' % g]; a = math.radians(90 * g); o.location = (math.cos(a) * 7.2, math.sin(a) * 7.2, 0)
                core('c%d' % g); bpy.data.objects['c%d' % g].location = o.location
                pawl('p%d' % g); p = bpy.data.objects['p%d' % g]; p.location = (math.cos(a) * 9.2, math.sin(a) * 9.2, 0); p.rotation_euler = (0, 0, a)
        elif name == 'GF_CentreGear': build('cg')
    hammer('hm'); bpy.data.objects['hm'].location = (0, 7.2, 3.0)
    sc = bpy.context.scene
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (*lin('#17263F'), 1)
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(45), 0, math.radians(30)); sun.data.energy = 3.5
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (0, -22, 24); cam.rotation_euler = (math.radians(42), 0, 0); cam.data.lens = 30
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True); print('PREVIEW', PREVIEW)
