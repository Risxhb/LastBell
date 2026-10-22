# Area 2, The Brass Foundry: environment assets modelled in Blender 5.2 and exported as one .glb per asset.
#   blender -b --factory-startup -P area2_models.py -- <out_dir> [contact_sheet.png]
# Coordinates: 1 unit = 1 m, Z up. Radial pieces face outward along -Y (the arena centre is toward +Y); after the glTF
# export and glTFast import that is local +Z in Unity, which is how BrassFoundryArenaBuilder places them. Every asset is
# real topology: chamfered blocks (bevel on the sharp edges only), carved wear (chipped bevels and a few deeper chips on
# stone), stepped profiles, recessed windows, riveted straps. Materials are slots named for the Unity materials they map
# to (Stone, Masonry, MasonryDark, Iron, Brass, DarkBrass, Plank, Roof, Chain, City, CityRoof, Slate, Glow, GlowDim,
# GlowWarm, GlowHot, Furnace); UVs are box-projected in metres to match the project's tiling textures.
import bpy, bmesh, math, os, random, sys, zlib
from mathutils import Vector, Matrix

V = Vector
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.abspath('area2_models')
SHEET = os.path.abspath(argv[1]) if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)
TAU = math.tau

MATDEF = {  # viewport/preview colours only; Unity remaps every slot by name
    'Stone': ((0.42, 0.40, 0.38), 0, 0.8, None), 'Masonry': ((0.50, 0.47, 0.45), 0, 0.85, None),
    'MasonryDark': ((0.33, 0.31, 0.30), 0, 0.85, None), 'Iron': ((0.22, 0.21, 0.20), 0.6, 0.55, None),
    'Brass': ((0.55, 0.40, 0.18), 0.85, 0.45, None), 'DarkBrass': ((0.32, 0.22, 0.12), 0.8, 0.55, None),
    'Plank': ((0.28, 0.24, 0.20), 0.1, 0.8, None), 'Roof': ((0.20, 0.19, 0.19), 0.5, 0.6, None),
    'Chain': ((0.14, 0.13, 0.12), 0.75, 0.5, None), 'City': ((0.20, 0.18, 0.17), 0, 0.9, None),
    'CityRoof': ((0.12, 0.10, 0.10), 0.3, 0.7, None), 'Slate': ((0.18, 0.18, 0.20), 0, 0.8, None),
    'Glow': ((1, 0.5, 0.15), 0, 1, (1, 0.48, 0.13)), 'GlowDim': ((0.6, 0.2, 0.05), 0, 1, (0.6, 0.2, 0.05)),
    'GlowWarm': ((0.9, 0.32, 0.1), 0, 1, (0.9, 0.32, 0.1)), 'GlowHot': ((1, 0.65, 0.25), 0, 1, (1, 0.65, 0.25)),
    'Furnace': ((1, 0.55, 0.2), 0, 1, (1, 0.55, 0.2)),
}


def material(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF'); col, met, rough, em = MATDEF[name]
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Metallic'].default_value = met; b.inputs['Roughness'].default_value = rough
    if em: b.inputs['Emission Color'].default_value = (*em, 1); b.inputs['Emission Strength'].default_value = 3
    m.diffuse_color = (*(em or col), 1)
    return m


# ---------------------------------------------------------------- primitives (each in its own temporary bmesh) ----
def hexa_bm(P):
    """Hexahedron from 8 corners: bottom quad 0-3 counter-clockwise seen from above, top quad 4-7 in the same order."""
    tb = bmesh.new(); vs = [tb.verts.new(p) for p in P]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)): tb.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:]); return tb


def prism_bm(prof, width):
    """Solid from a closed side profile [(y, z), ...] (counter-clockwise seen from +X), extruded across X with half-width
    width(y) / 2 on each side."""
    tb = bmesh.new()
    L = [tb.verts.new((-width(y) / 2, y, z)) for y, z in prof]; Rr = [tb.verts.new((width(y) / 2, y, z)) for y, z in prof]
    tb.faces.new(Rr); tb.faces.new(L[::-1]); n = len(prof)
    for i in range(n): j = (i + 1) % n; tb.faces.new((L[i], L[j], Rr[j], Rr[i]))
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:]); return tb


def box_bm(sx, sy, sz, c=(0, 0, 0)):
    x, y, z = sx / 2, sy / 2, sz / 2; cx, cy, cz = c
    q = [(-x, -y), (x, -y), (x, y), (-x, y)]
    return hexa_bm([V((cx + a, cy + b, cz - z)) for a, b in q] + [V((cx + a, cy + b, cz + z)) for a, b in q])


def lathe_bm(prof, seg, a0=0.0, a1=TAU):
    """Surface of revolution around Z; profile (r, z) bottom to top; faces point away from the axis."""
    tb = bmesh.new(); full = abs(a1 - a0 - TAU) < 1e-6; n = seg if full else seg + 1
    rings = [[tb.verts.new((math.cos(a0 + (a1 - a0) * i / seg) * r, math.sin(a0 + (a1 - a0) * i / seg) * r, z)) for i in range(n)] for r, z in prof]
    for j in range(len(prof) - 1):
        for i in range(seg):
            i2 = (i + 1) % n if full else i + 1
            tb.faces.new((rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]))
    bmesh.ops.remove_doubles(tb, verts=tb.verts[:], dist=1e-5)
    return tb


def cyl_bm(r, z0, z1, seg=16, caps=True):
    prof = ([(0, z0)] if caps else []) + [(r, z0), (r, z1)] + ([(0, z1)] if caps else [])
    return lathe_bm(prof, seg)


def arc_bm(r0, r1, a0, a1, z0, z1, seg):
    """Closed curved block between radii r0..r1 and angles a0..a1, centred on -Y at angle 0 (x = sin a·r, y = -cos a·r)."""
    tb = bmesh.new(); P = lambda a, r, z: tb.verts.new((math.sin(a) * r, -math.cos(a) * r, z))
    cols = [[P(a0 + (a1 - a0) * i / seg, r, z) for r, z in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))] for i in range(seg + 1)]
    for i in range(seg):
        A, B = cols[i], cols[i + 1]
        for k in range(4): tb.faces.new((A[k], A[(k + 1) % 4], B[(k + 1) % 4], B[k]))
    tb.faces.new(cols[0]); tb.faces.new(cols[-1][::-1])
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:]); return tb


def torus_bm(R, rt, sy=1.0, U=16, Vn=8):
    """Chain link: a torus in the XZ plane, stretched along Z."""
    tb = bmesh.new(); grid = []
    for i in range(U):
        u = TAU * i / U; d = V((math.cos(u), 0, math.sin(u))); row = []
        for j in range(Vn):
            v = TAU * j / Vn; n = d * math.cos(v) + V((0, 1, 0)) * math.sin(v)
            row.append(tb.verts.new(V((math.cos(u) * R, 0, math.sin(u) * R * sy)) + n * rt))
        grid.append(row)
    for i in range(U):
        for j in range(Vn): tb.faces.new((grid[i][j], grid[(i + 1) % U][j], grid[(i + 1) % U][(j + 1) % Vn], grid[i][(j + 1) % Vn]))
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:]); return tb


def T(x=0, y=0, z=0): return Matrix.Translation((x, y, z))
def RZ(a): return Matrix.Rotation(a, 4, 'Z')
def RX(a): return Matrix.Rotation(a, 4, 'X')
def RY(a): return Matrix.Rotation(a, 4, 'Y')
def polar(a, r, z=0): return V((math.sin(a) * r, -math.cos(a) * r, z))  # angle 0 = -Y (outward)


# ---------------------------------------------------------------- asset ----
class Asset:
    def __init__(s, name):
        s.name = name; s.bm = bmesh.new(); s.mats = []; s.rng = random.Random(zlib.crc32(name.encode()))

    def mi(s, m):
        if m not in s.mats: s.mats.append(m)
        return s.mats.index(m)

    def add(s, tb, mat, M=None, bevel=0.0, wear=0.0, cuts=0, seg=2, chips=0):
        """Merge a temporary bmesh: optional subdivision (so wear has vertices to carve), a chamfer on the sharp
        edges only, carved wear on the chamfers and a few deeper chips, then the placement matrix."""
        r = s.rng
        if cuts: bmesh.ops.subdivide_edges(tb, edges=tb.edges[:], cuts=cuts, use_grid_fill=True)
        bev_verts = []
        if bevel > 0:
            sharp = [e for e in tb.edges if e.is_manifold and e.calc_face_angle(0) > 0.6]
            if sharp:
                res = bmesh.ops.bevel(tb, geom=sharp, offset=bevel, offset_type='OFFSET', segments=seg, profile=0.5, affect='EDGES', clamp_overlap=True)
                bev_verts = list({v for f in res['faces'] for v in f.verts})
        if wear > 0 and bev_verts:
            tb.normal_update()
            push = {}
            for v in bev_verts:
                if r.random() < 0.4: push[v] = push.get(v, 0) + r.uniform(0.3, 1.0) * wear
            for v, d in push.items(): v.co -= v.normal * d
            for _ in range(chips):  # chiselled chips: verts near one chamfer corner are cut flat onto a tilted plane
                c = r.choice(bev_verts); rad = r.uniform(0.14, 0.28)
                pn = (c.normal + V((r.uniform(-.3, .3), r.uniform(-.3, .3), r.uniform(-.3, .3)))).normalized()
                p0 = c.co - pn * wear * r.uniform(2.0, 3.5)
                for v in tb.verts:
                    if (v.co - c.co).length < rad:
                        h = (v.co - p0).dot(pn)
                        if h > 0: v.co -= pn * h
        if M is not None: bmesh.ops.transform(tb, matrix=M, verts=tb.verts[:])
        m = s.mi(mat); vm = {}
        for v in tb.verts: vm[v] = s.bm.verts.new(v.co)
        for f in tb.faces:
            try: s.bm.faces.new([vm[v] for v in f.verts]).material_index = m
            except ValueError: pass
        tb.free()

    def box(s, mat, size, c=(0, 0, 0), M=None, **kw): s.add(box_bm(*size, c), mat, M, **kw)

    def rivet(s, mat, at, M=None):
        s.add(box_bm(0.075, 0.075, 0.075, at), mat, M, bevel=0.025, seg=2)

    def build(s):
        bm = s.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
        bm.normal_update()
        uvl = bm.loops.layers.uv.new('UVMap')
        for f in bm.faces:
            n = f.normal; ax, ay, az = abs(n.x), abs(n.y), abs(n.z); plank = s.mats[f.material_index] == 'Plank'
            for l in f.loops:
                p = l.vert.co
                if az >= ax and az >= ay: uv = (p.y, p.x) if plank else (p.x, p.y)
                elif ax >= ay: uv = (p.y, p.z)
                else: uv = (p.x, p.z)
                l[uvl].uv = uv
        me = bpy.data.meshes.new(s.name); bm.to_mesh(me); bm.free()
        for m in s.mats: me.materials.append(material(m))
        try:
            me.shade_smooth(); me.set_sharp_from_angle(angle=math.radians(40))
        except AttributeError:
            for p in me.polygons: p.use_smooth = False
        ob = bpy.data.objects.new(s.name, me); bpy.context.scene.collection.objects.link(ob)
        return ob


ASSETS = []
def done(a): ASSETS.append(a.build())


# ---------------------------------------------------------------- rim teeth ----
def tooth(name, W, D, H, variant):
    """A gear tooth of the furnace rim: wedge (narrower outward), top rising outward, heavy carved chamfers."""
    a = Asset(name); wi = W / 2.0; ch = min(0.6, H * 0.45)
    width = lambda y: wi + (W - wi) * (-y / D)
    prof = [(0, 0), (-D, 0), (-D, H), (-ch, H), (0, H - ch)]
    a.add(prism_bm(prof, width), 'Stone', bevel=0.035, wear=0.012, cuts=4, seg=1, chips=9)
    y0 = -D * 0.5  # capstone: a lighter, separately cut tier on the outer half
    a.add(prism_bm([(y0, H), (-D + 0.08, H), (-D + 0.08, H + 0.3), (y0 - 0.1, H + 0.3)], lambda y: width(y) - 0.12), 'Masonry', bevel=0.03, wear=0.01, cuts=2, seg=1, chips=3)
    hi = H; ztop = lambda y: H
    if variant == 'band':  # riveted brass strap over the top and down both flanks
        y = -D * 0.55; w = width(y) + 0.06; z = ztop(y)
        a.box('DarkBrass', (w, 0.34, 0.09), (0, y, z + 0.02), bevel=0.02)
        for sx in (-1, 1):
            a.box('DarkBrass', (0.09, 0.34, z - 0.05), (sx * w / 2, y, (z - 0.05) / 2 + 0.02), bevel=0.02)
            for k in range(3): a.rivet('Brass', (sx * (w / 2 + 0.03), y, 0.25 + k * (z - 0.4) / 2))
    elif variant == 'stack':  # a smaller block stacked on the outer half, brass corner caps on the inner edge
        y0, y1 = -D * 0.42, -D + 0.12; w0, w1 = width(y0) * 0.66, width(y1) * 0.66; z0, z1 = ztop(y0) - 0.04, ztop(y1) - 0.04; h = H * 0.42
        P = [V((-w0 / 2, y0, z0)), V((-w1 / 2, y1, z1)), V((w1 / 2, y1, z1)), V((w0 / 2, y0, z0)),
             V((-w0 / 2, y0, z0 + h)), V((-w1 / 2, y1, z1 + h * 1.08)), V((w1 / 2, y1, z1 + h * 1.08)), V((w0 / 2, y0, z0 + h))]
        a.add(hexa_bm(P), 'Stone', bevel=0.06, wear=0.025, cuts=2, chips=2)
        for sx in (-1, 1): a.box('Brass', (0.2, 0.2, 0.18), (sx * (width(-ch) / 2 - 0.06), -ch - 0.08, H - 0.06), bevel=0.03)
    elif variant == 'rivets':  # iron plate on the outer end, a rivet row on the arena face
        a.box('Iron', (W * 0.6, 0.55, 0.06), (0, -D + 0.32, H + 0.01), bevel=0.02)
        for k in range(5): a.rivet('Iron', ((k - 2) * wi * 0.17, 0.02, (H - ch) * 0.5))
    elif variant == 'cap':  # brass cap plate across the outer top
        a.box('DarkBrass', (W * 0.8, 0.4, 0.05), (0, -D + 0.25, H + 0.01), bevel=0.015)
        for sx in (-1, 1): a.rivet('Brass', (sx * W * 0.3, -D + 0.25, H + 0.05))
    done(a)


for v, var in (('A', 'band'), ('B', 'rivets'), ('C', 'band')): tooth('SM_Area2_RimTooth_Big_' + v, 2.1, 2.55, 1.3, var)
for v, var in (('A', 'plain'), ('B', 'cap'), ('C', 'rivets')): tooth('SM_Area2_RimTooth_Small_' + v, 1.3, 1.7, 0.9, var)


# ---------------------------------------------------------------- corner towers ----
def ring(a, mat, r_in, r_out, z0, z1, seg=64, lip=0.04):
    """Cornice ring with a chamfered outer lip (a closed lathe profile)."""
    a.add(lathe_bm([(r_in, z0), (r_out - lip, z0), (r_out, z0 + lip), (r_out, z1 - lip), (r_out - lip, z1), (r_in, z1)], seg), mat)


def tower(name, top, chimney):
    a = Asset(name); t1, t2, t3 = top * 0.45, top * 0.72, top * 0.88; r = a.rng
    # shaft tiers (stepped), brass and iron bands
    a.add(lathe_bm([(3.6, -28), (3.6, t1)], 64), 'MasonryDark')
    ring(a, 'Roof', 3.2, 3.95, t1, t1 + 0.42); ring(a, 'Brass', 3.9, 4.0, t1 + 0.08, t1 + 0.34, lip=0.02)
    a.add(lathe_bm([(3.05, t1 + 0.42), (3.05, t2)], 64), 'Masonry')
    ring(a, 'Roof', 2.65, 3.35, t2, t2 + 0.35); ring(a, 'Brass', 3.3, 3.4, t2 + 0.06, t2 + 0.29, lip=0.02)
    a.add(lathe_bm([(2.45, t2 + 0.35), (2.45, t3)], 64), 'MasonryDark')
    ring(a, 'Roof', 2.15, 2.8, t3, t3 + 0.28); ring(a, 'Brass', 2.76, 2.84, t3 + 0.04, t3 + 0.24, lip=0.02)
    for z in (t1 - 2.4, (t1 + t2) / 2): ring(a, 'DarkBrass', 3.0 if z > t1 else 3.55, 3.12 if z > t1 else 3.68, z, z + 0.25, lip=0.02)
    # buttress fins down the shaft, with stepped heads
    for i in range(8):
        b = (i + 0.5) / 8 * TAU; M = T(*polar(b, 3.72)) @ RZ(b)
        a.box('MasonryDark', (0.55, 0.5, t1 + 27.5), (0, 0, (t1 - 28) / 2 - 0.25), M, bevel=0.05)
        a.box('Masonry', (0.62, 0.62, 0.5), (0, -0.05, t1 - 0.6), M, bevel=0.05, wear=0.02, cuts=1)
    # crenellations: merlons with carved wear on each cornice; machinery boxes on the first
    for count, rr, z, sz in ((16, 3.78, t1 + 0.42, (0.62, 0.32, 0.48)), (12, 3.2, t2 + 0.35, (0.52, 0.28, 0.42)), (12, 2.6, t3 + 0.28, (0.6, 0.48, 0.66))):
        for i in range(count):
            b = (i + 0.5) / count * TAU if count != 12 or rr > 3 else i / count * TAU
            a.box('Masonry', sz, (0, 0, sz[2] / 2), T(*polar(b, rr, z)) @ RZ(b), bevel=0.04, wear=0.02, cuts=1, chips=1)
    for i in range(8):
        b = i / 8 * TAU; M = T(*polar(b, 3.4, t1 + 0.42)) @ RZ(b)
        a.box('Iron', (0.55, 0.5, 0.48), (0, 0, 0.24), M, bevel=0.03)
        a.add(cyl_bm(0.07, 0, 0.35), 'Brass', M @ T(0.12, 0, 0.48))
        a.add(lathe_bm([(0.12, 0.4), (0.12, 0.47)], 12), 'Brass', M @ T(0.12, 0, 0))
    # recessed window slits: dark reveal, glow pane set back, iron mullion and transom
    def slit(rr, b, z, w, h, mat):
        M = T(*polar(b, rr, z)) @ RZ(b)
        for sx in (-1, 1): a.box('Iron', (0.06, 0.16, h + 0.12), (sx * (w / 2 + 0.03), -0.08, 0), M)
        for sz in (-1, 1): a.box('Iron', (w + 0.18, 0.16, 0.06), (0, -0.08, sz * (h / 2 + 0.03)), M)
        a.box(mat, (w, 0.03, h), (0, -0.02, 0), M)
        a.box('Iron', (0.04, 0.05, h), (0, -0.05, 0), M); a.box('Iron', (w, 0.05, 0.04), (0, -0.05, h * 0.18), M)
    sh = t3 - t2 - 0.9
    for i in range(10): slit(2.45, (i + 0.5) / 10 * TAU, t2 + 0.35 + 0.25 + sh / 2, 0.28, sh, ('GlowHot', 'Glow', 'GlowWarm')[i % 3])
    for i in range(8):
        slit(3.6, (i + 0.5) / 8 * TAU, t1 - 1.6, 0.22, 1.3, 'GlowWarm' if i % 2 else 'Glow')
        slit(3.05, (i + 1) / 8 * TAU, (t1 + t2) / 2 + 0.9, 0.2, 0.8, 'Glow')
    # dome: iron shell, brass ribs, dormer windows, finial
    dome = [(2.3, t3 + 0.28), (2.22, t3 + 0.75), (1.95, t3 + 1.35), (1.5, t3 + 1.9), (0.9, t3 + 2.3), (0.35, t3 + 2.5), (0, t3 + 2.55)]
    a.add(lathe_bm(dome, 64), 'Roof')
    for i in range(8):
        b = i / 8 * TAU
        a.add(lathe_bm([(rr + 0.035, z) for rr, z in dome[:-1]], 1, b - 0.03, b + 0.03), 'DarkBrass')
        if i % 2: continue
        bd = b + TAU / 16; M = T(*polar(bd, 1.72, t3 + 1.35)) @ RZ(bd)
        a.box('Roof', (0.48, 0.5, 0.5), (0, 0, 0), M, bevel=0.03)
        a.add(lathe_bm([(0.36, 0.25), (0, 0.55)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof', M)
        a.box('GlowDim', (0.2, 0.03, 0.3), (0, -0.265, -0.04), M); a.box('Roof', (0.04, 0.04, 0.3), (0, -0.28, -0.04), M)
    a.add(cyl_bm(0.08, t3 + 2.45, t3 + 3.6, 10), 'Brass')
    a.add(lathe_bm([(0, t3 + 3.15), (0.2, t3 + 3.3), (0.2, t3 + 3.42), (0, t3 + 3.6)], 12), 'Brass')
    if chimney:  # chimney toward the arena (+Y), as in the reference
        c = V((0, 3.0, 0))
        a.add(cyl_bm(0.34, t2 + 0.35, t3 + 4.5, 16), 'Iron', T(*c))
        for z in (t3 + 1.0, t3 + 2.8, t3 + 4.3): a.add(lathe_bm([(0.34, z), (0.42, z + 0.03), (0.42, z + 0.2), (0.34, z + 0.23)], 16), 'Brass', T(*c))
        a.add(lathe_bm([(0.26, t3 + 4.5), (0, t3 + 4.5)], 16), 'GlowDim', T(*c))
    done(a)


tower('SM_Area2_Tower_Far', 14.0, True)
tower('SM_Area2_Tower_Near', 8.0, False)


# ---------------------------------------------------------------- furnace gate (origin on the gate line, arena = +Y) ----
def gate():
    a = Asset('SM_Area2_FurnaceGate')
    for sx in (-1, 1):
        x = sx * 3.25
        a.box('MasonryDark', (2.0, 2.0, 9.0), (x, 0, 1.5), bevel=0.08, wear=0.03, cuts=3, chips=4)
        for k in range(3): a.box('DarkBrass', (2.12, 2.12, 0.18), (x, 0, 0.8 + k * 2.1), bevel=0.02)
        a.box('DarkBrass', (2.36, 2.36, 0.26), (x, 0, 6.12), bevel=0.03)
        a.box('Stone', (1.62, 1.62, 0.8), (x, 0, 6.65), bevel=0.06, wear=0.02, cuts=1)
        a.box('Brass', (1.82, 1.82, 0.15), (x, 0, 7.12), bevel=0.02)
        a.add(lathe_bm([(1.08, 7.2), (0, 8.55)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof')
        a.add(lathe_bm([(0, 8.4), (0.11, 8.55), (0.04, 9.2), (0, 9.25)], 8), 'Brass', T(x, 0, 0))
        for wy, wz, w, h in ((1.0, 2.6, 0.5, 1.1),):  # arena-facing furnace window
            M = T(x, wy, wz)
            a.box('Iron', (w + 0.2, 0.12, 0.08), (0, 0.02, h / 2 + 0.06), M); a.box('Iron', (w + 0.2, 0.12, 0.08), (0, 0.02, -h / 2 - 0.06), M)
            for s2 in (-1, 1): a.box('Iron', (0.08, 0.12, h + 0.2), (s2 * (w / 2 + 0.06), 0.02, 0), M)
            a.box('GlowWarm', (w, 0.03, h), (0, 0.025, 0), M); a.box('Iron', (0.05, 0.06, h), (0, 0.04, 0), M)
        a.box('GlowWarm', (0.06, 0.5, 1.2), (x + sx * 1.0, 0, 3.0))  # outer-side window
    # lintel with a toothed cog on the arena face
    a.box('MasonryDark', (4.6, 1.6, 1.3), (0, 0, 5.45), bevel=0.06, wear=0.025, cuts=2, chips=2)
    a.box('DarkBrass', (4.7, 1.7, 0.15), (0, 0, 6.15), bevel=0.02)
    C = T(0, 0.82, 5.45) @ RX(-math.pi / 2)  # cog axis along +Y
    a.add(lathe_bm([(0.44, 0), (0.55, 0), (0.55, 0.12), (0.44, 0.12)], 32), 'Brass', C)
    a.add(lathe_bm([(0.44, 0.04), (0, 0.04)], 32), 'Slate', C)
    a.add(lathe_bm([(0, 0), (0.15, 0), (0.15, 0.17), (0, 0.17)], 16), 'Brass', C)
    for i in range(16): a.box('Brass', (0.16, 0.13, 0.12), (0.62, 0, 0.06), C @ RZ(i / 16 * TAU), bevel=0.02)
    for i in range(6): a.box('Brass', (0.32, 0.06, 0.08), (0.29, 0, 0.08), C @ RZ(i / 6 * TAU))
    # recessed furnace mouth: side walls, inner arch lintel, back wall, gradient glow (Furnace UV v = height in m)
    for sx in (-1, 1): a.box('MasonryDark', (0.25, 1.7, 4.85), (sx * 2.3, -0.25, 2.42), bevel=0.04)
    a.box('MasonryDark', (4.85, 1.7, 0.3), (0, -0.25, 4.82), bevel=0.04)
    a.box('Furnace', (4.4, 0.05, 4.7), (0, -0.95, 2.35))
    a.box('MasonryDark', (4.6, 0.5, 4.9), (0, -1.25, 2.45))
    # portcullis: riveted iron bars with spiked feet
    for i in range(9):
        x = -2.05 + i * 4.1 / 8
        a.box('Iron', (0.16, 0.16, 4.6), (x, 0.42, 2.45), bevel=0.03)
        a.add(lathe_bm([(0.09, 0.15), (0, -0.05)], 4, math.pi / 4, math.pi / 4 + TAU), 'Iron', T(x, 0.42, 0))
    for j in range(4):
        z = 0.65 + j * 1.15; a.box('Iron', (4.25, 0.12, 0.12), (0, 0.5, z), bevel=0.02)
        for i in range(9): a.rivet('Brass', (-2.05 + i * 4.1 / 8, 0.57, z))
    done(a)


gate()


# ---------------------------------------------------------------- outer wall modules (9° of arc, centred on -Y) ----
def wall(name, kind):
    a = Asset(name); r = a.rng; h = 1.0; A0, A1 = -math.radians(4.5), math.radians(4.5); e = 0.002
    h1, h2, h3 = 1.5, 2.4, 3.4
    a.add(arc_bm(18.6, 23.0, A0 + e, A1 - e, -1.7, h1, 8), 'MasonryDark', bevel=0.05)
    a.add(arc_bm(18.5, 18.85, A0 + e, A1 - e, h1, h1 + 0.1, 8), 'DarkBrass')
    a.add(arc_bm(19.7, 23.0, A0 + e, A1 - e, h1, h2, 8), 'MasonryDark', bevel=0.05)
    a.add(arc_bm(19.62, 19.82, A0 + e, A1 - e, h2, h2 + 0.1, 8), 'Iron')
    a.add(arc_bm(20.9, 23.0, A0 + e, A1 - e, h2, h3, 8), 'Masonry', bevel=0.05)
    a.add(arc_bm(20.82, 21.02, A0 + e, A1 - e, h3, h3 + 0.1, 8), 'Iron')
    for k in range(r.randint(1, 3)):  # blocky crest
        b = r.uniform(A0 + 0.02, A1 - 0.02)
        a.box('Masonry', (r.uniform(0.9, 1.5), 1.1, r.uniform(0.45, 0.9)), (0, 0, 0), T(*polar(b, 22.0, h3 + 0.3)) @ RZ(b), bevel=0.05, wear=0.02, cuts=1, chips=1)
    # outer face: pipe runs with brass flanges (chords between the module ends)
    for z, pr in ((0.4, 0.17), (1.15, 0.12)):
        P0, P1 = polar(A0 + 0.01, 23.2, z), polar(A1 - 0.01, 23.2, z); d = (P1 - P0); L = d.length
        M = T(*P0) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        a.add(cyl_bm(pr, 0, L, 12, caps=False), 'Iron', M)
        for t in (0.02, 0.5, 0.98): a.add(cyl_bm(pr * 1.45, L * t - 0.06, L * t + 0.06, 12), 'DarkBrass', M)
    if kind == 'hatch':  # furnace hatch on the channel face: brass frame, dim glow, bars
        M = T(*polar(0, 18.6, -0.55)) @ RZ(0)
        a.box('DarkBrass', (1.0, 0.12, 0.85), (0, 0.04, 0), M, bevel=0.02)
        a.box('GlowDim', (0.72, 0.04, 0.58), (0, 0.11, 0), M)
        for k in range(4): a.box('Iron', (0.05, 0.05, 0.6), (-0.27 + k * 0.18, 0.14, 0), M)
    if kind == 'buttress':
        M = T(*polar(0, 23.9)) @ RZ(0)
        a.box('MasonryDark', (2.6, 2.2, 33.6), (0, 0, (3.6 - 30) / 2), M, bevel=0.07, wear=0.025, cuts=4, chips=3)
        a.box('Iron', (2.8, 2.4, 0.3), (0, 0, 3.7), M, bevel=0.03)
        a.box('Masonry', (1.6, 1.4, 0.9), (0, 0, 4.3), M, bevel=0.05, wear=0.02, cuts=1)
    if kind == 'vent':  # steam vent stack on the top tier
        c = polar(0, 22.0, h3)
        a.add(cyl_bm(0.32, 0, 1.3, 14), 'Iron', T(*c))
        a.add(lathe_bm([(0.32, 1.1), (0.42, 1.14), (0.42, 1.3), (0.32, 1.34)], 14), 'Brass', T(*c))
        a.add(lathe_bm([(0.24, 1.31), (0, 1.31)], 14), 'Slate', T(*c))
    done(a)


wall('SM_Area2_Wall_Hatch', 'hatch'); wall('SM_Area2_Wall_Buttress', 'buttress'); wall('SM_Area2_Wall_Vent', 'vent')


# ---------------------------------------------------------------- bridges ----
def deck(name, W):
    """2 m deck module along -Y: plank top at z = 0, brass nosing, iron truss underneath."""
    a = Asset(name)
    a.box('Plank', (W, 2.0, 0.5), (0, -1.0, -0.25))
    for sx in (-1, 1):
        a.box('Brass', (0.14, 2.0, 0.06), (sx * (W / 2 - 0.07), -1.0, 0.03), bevel=0.015)
        gx = sx * (W / 2 - 0.3)
        a.box('Iron', (0.3, 2.0, 0.3), (gx, -1.0, -0.65), bevel=0.03); a.box('Iron', (0.3, 2.0, 0.3), (gx, -1.0, -2.3), bevel=0.03)
        a.box('Iron', (0.22, 0.22, 1.65), (gx, -1.0, -1.48), bevel=0.03)
        for k in (-1, 1):  # X brace
            ang = math.atan2(1.55, 1.0) * k
            a.box('Iron', (0.12, math.hypot(1.0, 1.55), 0.12), (0, 0, 0), T(gx, -0.5 if k < 0 else -1.5, -1.48) @ RX(ang), bevel=0.02)
        for y in (-0.15, -1.85): a.rivet('Brass', (sx * (W / 2 - 0.12), y, 0.07))
    a.box('Iron', (W - 0.6, 0.25, 0.3), (0, -1.0, -0.65), bevel=0.03)
    done(a)


deck('SM_Area2_Deck_55', 5.5); deck('SM_Area2_Deck_45', 4.5)


def pylon():
    a = Asset('SM_Area2_Pylon')
    a.box('MasonryDark', (1.1, 1.1, 35), (0, 0, -10.5), bevel=0.06, wear=0.025, cuts=6, chips=4)
    for z in (0.6, 6.6): a.box('DarkBrass', (1.3, 1.3, 0.25), (0, 0, z), bevel=0.02)
    a.add(lathe_bm([(0.95, 7.0), (0, 8.3)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof')
    a.add(lathe_bm([(0, 8.2), (0.08, 8.3), (0.03, 8.8), (0, 8.85)], 8), 'Brass')
    a.add(torus_bm(0.18, 0.05, 1.0, 12, 6), 'Iron', T(0, 0, 6.95))
    done(a)


def gatehouse(W):
    """The block cluster a gantry runs into (inner face at y = 0, extends outward along -Y)."""
    a = Asset('SM_Area2_Gatehouse')
    a.box('MasonryDark', (W + 3, 6, 16), (0, -3, -4), bevel=0.08, wear=0.03, cuts=4, chips=5)
    a.box('DarkBrass', (W + 3.3, 6.3, 0.3), (0, -3, 4.1), bevel=0.03)
    a.box('Stone', (W, 3.6, 2.4), (0, -3.4, 5.4), bevel=0.07, wear=0.03, cuts=2, chips=2)
    a.add(lathe_bm([(W * 0.62, 6.6), (0, 8.8)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof', T(0, -3.4, 0))
    for sx in (-1, 1):
        a.box('Stone', (1.2, 1.2, 6.5), (sx * (W / 2 + 0.9), -0.3, 1.25), bevel=0.06, wear=0.025, cuts=2, chips=2)
        a.add(lathe_bm([(0.85, 4.5), (0, 5.8)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof', T(sx * (W / 2 + 0.9), -0.3, 0))
        a.box('GlowWarm', (0.45, 0.06, 1.2), (sx * (W / 2 - 0.4), 0.02, 2.6))
        a.box('Iron', (0.6, 0.1, 0.08), (sx * (W / 2 - 0.4), 0.04, 3.25)); a.box('Iron', (0.6, 0.1, 0.08), (sx * (W / 2 - 0.4), 0.04, 1.95))
    a.box('GlowDim', (W - 1.4, 0.06, 2.3), (0, 0.02, 1.15))
    a.box('DarkBrass', (W - 0.8, 0.3, 0.35), (0, 0.1, 2.45), bevel=0.03)
    done(a)


def gate_pillar():
    a = Asset('SM_Area2_GatePillar')
    a.box('Stone', (1.2, 1.2, 10), (0, 0, -1), bevel=0.06, wear=0.03, cuts=3, chips=3)
    a.box('DarkBrass', (1.45, 1.45, 0.22), (0, 0, 4.1), bevel=0.02)
    a.box('Iron', (0.7, 0.7, 0.12), (0, 0, 4.27), bevel=0.02)
    a.box('GlowWarm', (0.3, 0.06, 0.9), (0, 0.62, 2.3))
    done(a)


pylon(); gatehouse(5.5); gate_pillar()


# ---------------------------------------------------------------- props ----
def lantern():
    a = Asset('SM_Area2_Lantern')
    for sx, sy in ((1, 1), (1, -1), (-1, 1), (-1, -1)): a.box('Iron', (0.12, 0.12, 1.5), (sx * 0.5, sy * 0.5, 0), bevel=0.02)
    for z in (-0.8, 0.8): a.box('Iron', (1.3, 1.3, 0.14), (0, 0, z), bevel=0.03)
    for i in range(4):
        M = RZ(i * math.pi / 2)
        a.box('Iron', (0.06, 0.06, 1.5), (0, 0.55, 0), M); a.box('Iron', (1.0, 0.06, 0.06), (0, 0.55, 0.2), M)
    a.add(lathe_bm([(0.95, 0.87), (0.2, 1.4), (0, 1.5)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof')
    a.add(lathe_bm([(0, -1.45), (0.5, -0.87)], 4, math.pi / 4, math.pi / 4 + TAU), 'Roof')
    a.add(torus_bm(0.22, 0.06, 1.3, 12, 6), 'Iron', T(0, 0, 1.85))
    a.box('Glow', (0.85, 0.85, 1.35))
    done(a)


def chain_link():
    a = Asset('SM_Area2_ChainLink')
    a.add(torus_bm(0.34, 0.11, 1.45, 16, 8), 'Chain')
    done(a)


lantern(); chain_link()


# ---------------------------------------------------------------- city buildings (6 × 6 m footprint, 12 m tall) ----
def windows(a, w, d, z0, z1, rows, mat_cycle):
    r = a.rng
    for face in range(4):
        M = RZ(face * math.pi / 2); half = (w if face % 2 == 0 else d) / 2; depth = (d if face % 2 == 0 else w) / 2
        n = max(2, int(half * 2 / 1.3))
        for row in range(rows):
            z = z0 + (z1 - z0) * (row + 0.5) / rows
            for k in range(n):
                if r.random() < 0.45: continue
                x = -half + (k + 0.5) * half * 2 / n
                a.box(r.choice(mat_cycle), (0.32, 0.06, 1.1), (x, -depth - 0.02, z), M)


def city(name, kind):
    a = Asset(name); r = a.rng
    if kind == 'hall':  # foundry hall with a sawtooth roof whose steep faces glow
        a.box('City', (6, 6, 8), (0, 0, 4), bevel=0.06)
        for k in range(3):
            y0 = -3 + k * 2
            P = [V((-3, y0, 8)), V((-3, y0 + 2, 8)), V((3, y0 + 2, 8)), V((3, y0, 8)), V((-3, y0, 8.02)), V((-3, y0 + 2, 9.6)), V((3, y0 + 2, 9.6)), V((3, y0, 8.02))]
            a.add(hexa_bm(P), 'CityRoof')
            a.box('GlowWarm', (5.6, 0.05, 1.3), (0, y0 + 2.01, 8.8))
        windows(a, 6, 6, 1, 7, 3, ('GlowWarm', 'Glow'))
    elif kind == 'stack':  # block with a banded chimney stack
        a.box('City', (5, 5, 11), (0, 0, 5.5), bevel=0.06)
        a.box('CityRoof', (5.3, 5.3, 0.4), (0, 0, 11.2), bevel=0.04)
        a.add(cyl_bm(0.8, 11, 19, 16), 'City', T(1.2, 1.2, 0))
        for z in (13, 15.5, 18): a.add(lathe_bm([(0.8, z), (0.95, z + 0.04), (0.95, z + 0.3), (0.8, z + 0.34)], 16), 'DarkBrass', T(1.2, 1.2, 0))
        a.add(lathe_bm([(0.6, 19), (0, 19)], 16), 'GlowDim', T(1.2, 1.2, 0))
        windows(a, 5, 5, 1, 10, 4, ('GlowWarm', 'Glow', 'GlowWarm'))
    elif kind == 'stepped':
        a.box('City', (6, 6, 7), (0, 0, 3.5), bevel=0.06)
        a.box('City', (4, 4, 4), (0, 0, 9), bevel=0.05)
        a.add(lathe_bm([(2.9, 11), (0, 13.2)], 4, math.pi / 4, math.pi / 4 + TAU), 'CityRoof')
        windows(a, 6, 6, 1, 6, 2, ('GlowWarm',))
    elif kind == 'furnace':  # squat furnace house: a glowing mouth and twin stacks
        a.box('City', (6, 6, 6), (0, 0, 3), bevel=0.06)
        a.box('CityRoof', (6.3, 6.3, 0.35), (0, 0, 6.15), bevel=0.04)
        a.box('Furnace', (2.6, 0.06, 2.4), (0, -3.03, 1.2)); a.box('Iron', (3.0, 0.2, 0.25), (0, -3.05, 2.5), bevel=0.03)
        for x in (-1.6, 1.6):
            a.add(cyl_bm(0.6, 6, 15, 14), 'City', T(x, 1.4, 0))
            a.add(lathe_bm([(0.45, 15), (0, 15)], 14), 'GlowDim', T(x, 1.4, 0))
        windows(a, 6, 6, 3.5, 5.5, 1, ('Glow',))
    elif kind == 'gantry':
        a.box('City', (6, 4, 10), (0, 0, 5), bevel=0.06)
        a.box('Iron', (6.6, 1.2, 0.2), (0, -2.6, 6), bevel=0.02)
        for x in (-3, -1, 1, 3): a.box('Iron', (0.1, 0.1, 0.9), (x, -3.15, 6.55))
        a.box('GlowDim', (2.0, 1.2, 0.06), (0, 0, 10.03))
        windows(a, 6, 4, 1, 9, 3, ('GlowWarm', 'Glow'))
    elif kind == 'narrow':  # tall narrow house with a gable roof
        a.box('City', (4, 6, 10), (0, 0, 5), bevel=0.05)
        P = [V((-2.1, -3.1, 10)), V((-2.1, 3.1, 10)), V((2.1, 3.1, 10)), V((2.1, -3.1, 10)), V((-0.02, -3.1, 12.6)), V((-0.02, 3.1, 12.6)), V((0.02, 3.1, 12.6)), V((0.02, -3.1, 12.6))]
        a.add(hexa_bm(P), 'CityRoof')
        windows(a, 4, 6, 1, 9, 4, ('GlowWarm', 'Glow'))
    done(a)


for n, k in (('A', 'hall'), ('B', 'stack'), ('C', 'stepped'), ('D', 'furnace'), ('E', 'gantry'), ('F', 'narrow')): city('SM_Area2_City_' + n, k)


# ---------------------------------------------------------------- export ----
total = 0
for ob in ASSETS:
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons); total += tris
    for o in bpy.context.scene.objects: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, ob.name + '.glb'), export_format='GLB', use_selection=True,
                              export_apply=True, export_yup=True, export_texcoords=True, export_normals=True, export_materials='EXPORT')
    print(f'AREA2 exported {ob.name}: {tris} tris, materials {[m.name for m in ob.data.materials]}')
print(f'AREA2 total {len(ASSETS)} assets, {total} tris')

# ---------------------------------------------------------------- contact sheet ----
if SHEET:  # every asset on a grid, front three-quarter ortho view, Workbench with cavity and shadows
    cols, gap, x, z, row_h, n = 7, 2.5, 0.0, 0.0, 0.0, 0
    for ob in ASSETS:
        bb = [ob.matrix_world @ V(c) for c in ob.bound_box]
        lo = V((min(v.x for v in bb), min(v.y for v in bb), max(min(v.z for v in bb), -4))); hi = V((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
        sx, sz = hi.x - lo.x, hi.z - lo.z
        if n and n % cols == 0: x = 0.0; z -= row_h + gap * 3; row_h = 0.0
        ob.location = (x - lo.x, -(lo.y + hi.y) / 2, z - lo.z)
        x += sx + gap; row_h = max(row_h, sz); n += 1
    bpy.context.view_layer.update()
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'; sh = scn.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.show_shadows = True
    scn.render.resolution_x, scn.render.resolution_y = 2400, 1500; scn.render.filepath = SHEET
    xs = [v.x for o in ASSETS for v in (o.matrix_world @ V(c) for c in o.bound_box)]; zs = [v.z for o in ASSETS for v in (o.matrix_world @ V(c) for c in o.bound_box)]
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(max(xs) - min(xs), (max(zs) - max(min(zs), -60)) * 1.6) * 1.05
    cx, cz = (min(xs) + max(xs)) / 2, (max(zs) + max(min(zs), -60)) / 2
    cam.location = (cx, -120, cz + 25); cam.rotation_euler = (math.radians(78), 0, 0); cam.data.clip_end = 1000
    bpy.ops.render.render(write_still=True)
    print('AREA2 contact sheet', SHEET)
