# Rivet Cantor props (miniboss art pass, 9 Nov 2026): RivetHead, ChainLink, ChainShackle.
#   blender -b --factory-startup -P rivet_cantor_props.py -- <out dir> <clay|tex> [<boss glb for the mock frames>]
# Builds the three props from code (no source mesh), exports one GLB each into <out dir>, saves the .blend there, and
# renders turnaround sheets plus gameplay-camera mock frames (Brass Foundry floor, telegraph discs, the boss for scale).
# Prompts: build-evidence/miniboss-art-20261109/rivet-cantor/prompts.md. 1 Blender unit = 1 m; Z up (glTF export: Y up).
# Presentation only: the game places and animates these in RivetCantorProps.cs; no hit shape lives here.
# Model round 2 (after critic round 1): smooth dented dome, grooved shank, 7 gap-free torn petals, bigger links
# (0.50 x 0.31 m, 0.09 m bar, 0.40 m pitch), a real D-shackle (continuous bow, ear lugs, crown band, forged eye).
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
OUT = os.path.abspath(args[0]); MODE = args[1] if len(args) > 1 else 'clay'
BOSS = args[2] if len(args) > 2 else ''
os.makedirs(OUT, exist_ok=True)
TEX = MODE == 'tex'
TEXRES = 1024
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

# ------------------------------------------------------------------ scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
col = sc.collection

def new_obj(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); col.objects.link(ob); return ob

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); ob = bpy.context.view_layer.objects.active; ob.name = name; ob.data.name = name
    return ob

def tris(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)

def sweep(path, radius, sides, closed=True, cap=False, name='sweep', plane=(0, 0, 1), flat=None):
    """Tube of `sides` round a planar polyline (list of Vector) lying in the plane with normal `plane`. The frame is fixed
    by that normal, so it never flips on steep segments (the round-1 shading tears). radius / flat may be numbers or
    functions of the point (flat < 1 squashes the section out of the plane)."""
    bm = bmesh.new(); rings = []; u = Vector(plane).normalized()
    n = len(path)
    for i, p in enumerate(path):
        a = path[(i - 1) % n] if (closed or i > 0) else path[i]
        b = path[(i + 1) % n] if (closed or i < n - 1) else path[i]
        t = (b - a).normalized(); w = t.cross(u).normalized()
        r = radius(p) if callable(radius) else radius
        f = (flat(p) if callable(flat) else flat) if flat is not None else 1.0
        rings.append([bm.verts.new(p + w * math.cos(2 * math.pi * k / sides) * r + u * math.sin(2 * math.pi * k / sides) * r * f) for k in range(sides)])
    m = n if closed else n - 1
    for i in range(m):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            bm.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    if cap and not closed:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm)

def cyl(name, r, h, seg, at=(0, 0, 0), axis='Z', r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    ob = new_obj(name, bm)
    if axis == 'X': ob.rotation_euler = (0, math.pi / 2, 0)
    elif axis == 'Y': ob.rotation_euler = (math.pi / 2, 0, 0)
    ob.location = at; return ob

def box(name, size, at=(0, 0, 0), rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts: v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    ob = new_obj(name, bm); ob.location = at; ob.rotation_euler = rot; return ob

def bevel(ob, w, seg=1, angle=40):
    m = ob.modifiers.new('bevel', 'BEVEL'); m.width = w; m.segments = seg; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(angle)
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)

def shade(ob, smooth_angle=None):
    for p in ob.data.polygons: p.use_smooth = smooth_angle is not None
    if smooth_angle is not None:
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.shade_auto_smooth(angle=math.radians(smooth_angle))

# ------------------------------------------------------------------ materials
def principled(name, base, metal, rough):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    return m

CLAY = principled('clay', (0.55, 0.55, 0.55), 0.0, 0.6)
M = {k: CLAY for k in ('brass', 'brass_link', 'brass_shackle', 'iron', 'polish', 'slate', 'ember', 'enamel')}
if TEX:
    import rivet_cantor_props_tex as T
    M = T.materials()

# Material ids live in a face attribute "mid" (texture round 2: object joins dropped plain material_index values on
# parts that had no slots yet, so every part came out brass). mats_for() adds the slots and applies "mid".
def _mid(ob):
    a = ob.data.attributes.get('mid')
    return a if a else ob.data.attributes.new('mid', 'INT', 'FACE')

def mats_for(ob, slots):
    ob.data.materials.clear()
    for s in slots: ob.data.materials.append(s)
    a = ob.data.attributes.get('mid')
    for p in ob.data.polygons: p.material_index = a.data[p.index].value if a else 0

def tag(ob, idx, polys=None):
    a = _mid(ob)
    for i in (range(len(ob.data.polygons)) if polys is None else polys): a.data[i].value = idx

# ------------------------------------------------------------------ 1. RivetHead (two nodes: Head, Plate)
random.seed(7)
PUNCH = 0.22   # how far the head rises above rest at the top of the punch (RivetCantorProps.cs uses the same)
def build_rivet():
    # dome: low and wide, smooth, with 9 broad shallow hammer dents
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=36, v_segments=14, radius=1.0)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context='VERTS')
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary])
    # hammer peen (model round 3): 12 flat facets cut into the unit dome (planes a little inside the surface) plus a few
    # broad shallow dents, then squashed to the 0.55 m x 0.30 m dome
    facets = []
    for i in range(12):
        az = 2 * math.pi * i / 12 + random.uniform(-0.2, 0.2); el = math.radians(random.uniform(18, 66) if i % 3 else random.uniform(55, 75))
        facets.append((Vector((math.cos(az) * math.cos(el), math.sin(az) * math.cos(el), math.sin(el))), random.uniform(0.022, 0.035)))
    dents = [Vector((math.cos(a) * 0.7, math.sin(a) * 0.7, 0.71)).normalized() for a in (0.4, 2.5, 4.4)]
    for v in bm.verts:
        z = v.co.z; dirn = v.co.normalized() if v.co.length > 1e-6 else Vector((0, 0, 1))
        if z > 0.03:
            for d, depth in facets:
                over = v.co.dot(d) - (1 - depth)
                if over > 0: v.co -= d * over
            push = sum(0.02 * math.exp(-((dirn - d).length / 0.2) ** 2) for d in dents)
            v.co -= dirn * push
        v.co.x *= 0.55; v.co.y *= 0.55; v.co.z = v.co.z * 0.30 + 0.07
    dome = new_obj('dome', bm); tag(dome, 0)
    flange = cyl('flange', 0.585, 0.08, 36, at=(0, 0, 0.04)); tag(flange, 0)
    # iron shank with a groove (two collars and a narrower neck): 0.25 m of it shows at the punch top
    sh1 = cyl('shankA', 0.225, 0.20, 18, at=(0, 0, -0.10)); tag(sh1, 1)
    sh2 = cyl('shankB', 0.195, 0.06, 18, at=(0, 0, -0.23)); tag(sh2, 1)
    sh3 = cyl('shankC', 0.225, 0.30, 18, at=(0, 0, -0.41)); tag(sh3, 1)
    # crown mark: three raised bars (a "beat" sign), 0.04 m proud
    bars = []
    for i, x in enumerate((-0.13, 0, 0.13)):
        b = box(f'bar{i}', (0.07, 0.32, 0.07), at=(x, 0, 0.36)); tag(b, 2); bars.append(b)
    head = join([dome, flange, sh1, sh2, sh3] + bars, 'Head')
    mats_for(head, [M['brass'], M['iron'], M['polish']])
    bevel(head, 0.01, 1, 50)
    shade(head, 40)
    # torn floor plate: 7 irregular petals with no gaps at the root, peeled up 20-40 deg round the hole (hinged at the root)
    # model round 3: uneven outline (roots at 0.66-0.84 m), two torn gaps, lips peeled up 0.10-0.14 m
    cuts = sorted(random.uniform(0, 2 * math.pi) for _ in range(7))
    while min((cuts[(i + 1) % 7] - cuts[i]) % (2 * math.pi) for i in range(7)) < 0.55:
        cuts = sorted(random.uniform(0, 2 * math.pi) for _ in range(7))
    petals = []; gaps = {2, 5}
    for i in range(7):
        a0 = cuts[i]; a1 = cuts[(i + 1) % 7]
        if a1 < a0: a1 += 2 * math.pi
        if i in gaps: a0 += 0.18; a1 -= 0.10   # a torn gap: the plate is missing here
        rout = random.uniform(0.66, 0.84)
        length = random.uniform(0.20, 0.30); rin = rout - length; am = (a0 + a1) / 2
        nseg = 5
        outer = [Vector((math.cos(a0 + (a1 - a0) * k / nseg) * rout, math.sin(a0 + (a1 - a0) * k / nseg) * rout, 0)) for k in range(nseg + 1)]
        inner = []
        for k in range(nseg, -1, -1):   # jagged lip: notches in and out
            ang = a0 + (a1 - a0) * k / nseg + (0 if k in (0, nseg) else random.uniform(-0.04, 0.04))
            rr = rin + (0.05 if k in (0, nseg) else random.uniform(-0.05, 0.03)) + (0.05 if k % 2 else 0)
            inner.append(Vector((math.cos(ang) * rr, math.sin(ang) * rr, 0)))
        bm = bmesh.new(); vs = [bm.verts.new(p) for p in outer + inner]
        f = bm.faces.new(vs)
        bmesh.ops.triangulate(bm, faces=[f])
        ext = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
        for v in [e for e in ext['geom'] if isinstance(e, bmesh.types.BMVert)]: v.co.z -= 0.045
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        # the torn lip (texture round 2): the side faces along the inner edge glow, nothing else
        lip = [f.index for f in bm.faces if abs(f.normal.z) < 0.5 and all(Vector((v.co.x, v.co.y)).length < rout - 0.03 for v in f.verts)]
        ob = new_obj(f'petal{i}', bm)
        lift = math.asin(min(0.9, random.uniform(0.10, 0.14) / length))
        hinge = Vector((math.cos(am) * rout, math.sin(am) * rout, 0)); tangent = Vector((-math.sin(am), math.cos(am), 0))
        R = Matrix.Translation(hinge) @ Matrix.Rotation(lift, 4, tangent) @ Matrix.Translation(-hinge)
        ob.data.transform(R); ob.location.z = 0.045
        tag(ob, 0); tag(ob, 1, lip); petals.append(ob)
    plate = join(petals, 'Plate')
    mats_for(plate, [M['slate'], M['ember']])
    shade(plate, None)
    return head, plate

# ------------------------------------------------------------------ 2. ChainLink (long axis = Y)
def stadium(a, b, n):
    """Centreline of a stud link: half-length a, half-width b, n points (two semicircles + straights)."""
    pts = []; per = n // 2
    for half in (0, 1):
        cy = (a - b) * (1 if half == 0 else -1)
        for k in range(per):
            ang = (math.pi * k / (per - 1)) + (0 if half == 0 else math.pi)
            pts.append(Vector((math.cos(ang) * b, cy + math.sin(ang) * b, 0)))
    return pts

LINK_LEN, LINK_W, LINK_BAR = 0.50, 0.31, 0.09
def build_link():
    r = LINK_BAR / 2; a = LINK_LEN / 2 - r; b = LINK_W / 2 - r
    # the bar flattens (about 0.075 x 0.065 m) where the stud meets it
    # model round 3: 22 x 7 sweep (~330 tris), the stud's bar flattened to ~0.09 x 0.06 m over a 0.16 m stretch
    ring = sweep(stadium(a, b, 22), r, 7, closed=True, name='ring', plane=(0, 0, 1),
                 flat=lambda p: 0.66 if abs(p.y) < 0.08 and abs(p.x) > b * 0.8 else 1.0); tag(ring, 0)
    stud = cyl('stud', 0.03, 2 * b, 6, axis='X'); tag(stud, 1)
    caps = []
    for sgn in (-1, 1):   # rivet caps flush with the bar's outer face (<= 0.005 m proud)
        c = cyl(f'cap{sgn}', 0.034, 0.02, 7, at=(sgn * (b + r - 0.015), 0, 0), axis='X', r2=0.024); tag(c, 0); caps.append(c)
        c.rotation_euler = (0, sgn * math.pi / 2, 0)
    link = join([ring, stud] + caps, 'ChainLink')
    mats_for(link, [M['brass_link'], M['iron']])
    shade(link, 75)   # 7-sided tube: keep it round
    return link

# ------------------------------------------------------------------ 3. ChainShackle (pivot = the eye's centre)
def build_shackle():
    # model round 3: a true D. One continuous bow: a semicircle crown on straight parallel legs (0.20 m), ending in
    # flattened ear lugs; a pin through the lugs (round head one side, hex nut the other); a raised cobalt band 0.06 m
    # wide on the crown; the eye (bar 0.05 m, outer 0.14 m) forged into the crown on a short neck, turned 90 deg so the
    # first link threads through it. Pivot = the eye's centre.
    r = 0.035; R = 0.135; cz = -0.05; leg = 0.20
    path = [Vector((R, 0, cz - leg + 0.04 * k / 2)) for k in range(0)]
    for k in range(5): path.append(Vector((R, 0, cz - leg + leg * k / 4)))
    for k in range(1, 18):
        ang = math.pi * k / 18; path.append(Vector((math.cos(ang) * R, 0, cz + math.sin(ang) * R)))
    for k in range(5): path.append(Vector((-R, 0, cz + -leg * k / 4)))
    rz = R
    bow = sweep(path, lambda p: r + (0.016 if abs(p.x) < 0.075 and p.z > cz else 0), 12, closed=False, cap=True, name='bow', plane=(0, 1, 0))
    tag(bow, 0)
    for p in bow.data.polygons:
        c = sum((bow.data.vertices[v].co for v in p.vertices), Vector()) / len(p.vertices)
        if abs(c.x) < 0.07 and c.z > cz + R - 0.05: tag(bow, 2, [p.index])
    pz = path[0].z - 0.01; px = abs(path[0].x)
    lugs = []
    for sgn in (-1, 1):
        lug = cyl(f'lug{sgn}', 0.062, 0.05, 16, at=(sgn * px, 0, pz), axis='X'); tag(lug, 1); lugs.append(lug)
    pin = cyl('pin', 0.026, 2 * px + 0.16, 12, at=(0, 0, pz), axis='X'); tag(pin, 1)
    phead = cyl('pinhead', 0.05, 0.035, 16, at=(-(px + 0.06), 0, pz), axis='X'); tag(phead, 1)
    nut = cyl('nut', 0.055, 0.05, 6, at=(px + 0.065, 0, pz), axis='X'); tag(nut, 1)
    rims = []
    for sgn in (-1, 1):   # thin brass rims either side of the enamel band
        rim = cyl(f'rim{sgn}', r + 0.02, 0.014, 14, at=(sgn * 0.078, 0, cz + rz - 0.03), axis='X'); tag(rim, 0); rims.append(rim)
    top = cz + rz + r
    neck = cyl('neck', 0.032, 0.05, 10, at=(0, 0, top + 0.012)); tag(neck, 0)
    eye_c = top + 0.04 + 0.045
    epts = [Vector((0, math.cos(2 * math.pi * k / 20) * 0.045, eye_c + math.sin(2 * math.pi * k / 20) * 0.045)) for k in range(20)]
    eye = sweep(epts, 0.025, 10, closed=True, name='eye', plane=(1, 0, 0)); tag(eye, 0)
    sh = join([bow, pin, phead, nut, neck, eye] + lugs + rims, 'ChainShackle')
    sh.data.transform(Matrix.Translation((0, 0, -eye_c)))
    mats_for(sh, [M['brass_shackle'], M['iron'], M['enamel']])
    shade(sh, 45)
    return sh

head, plate = build_rivet()
link = build_link()
shackle = build_shackle()
assets = {'RivetHead': [head, plate], 'ChainLink': [link], 'ChainShackle': [shackle]}
if TEX: T.bake(assets, OUT, TEXRES)

# ------------------------------------------------------------------ export
stats = []
for name, objs in assets.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_yup=True,
                              export_materials='EXPORT', export_image_format='JPEG' if TEX else 'AUTO', export_jpeg_quality=88)
    t = sum(tris(o) for o in objs); stats.append(f'{name}: {t} tris, {os.path.getsize(path) / 1024:.0f} KB')
with open(os.path.join(OUT, 'stats.txt'), 'w') as f: f.write('\n'.join(stats) + '\n')
print('STATS', stats)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'rivet_cantor_props.blend'))

# ------------------------------------------------------------------ renders
import rivet_cantor_props_shots as S
S.render_all(assets, OUT, BOSS, TEX)
