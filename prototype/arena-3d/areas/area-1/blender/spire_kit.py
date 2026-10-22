# Area 1 — The Skyward Spire: everything around the floor, modelled in Blender.
# Loads the landmark buildings from spire_buildings.py, then models the rest of the scene: the curb and turned-baluster
# balustrade, the rim kiosks and gate pavilions, the drum wall with blind arcades and the tiered underside, the four
# bridges (paving, balusters, domed posts, pier arches), the hanging star banners, the kiosks on columns, the filler
# spires, the far spire ring and floating islands, and the skybridges. Only the painted floor disc stays in
# skyward-spire-env.js. Repeated parts share one mesh (linked duplicates), so the glTF stays small.
# Also exports a CloudAnchor empty (scale = footprint radius) for every structure; Unity's builder wraps each one in
# a collar of cloud puffs. Names the runtime looks for: BannerCloth* (sway), ArmillaryRing* (turn), Occluder* (hidden
# in the gameplay view).
#   blender -b -P spire_kit.py -- <out.glb> [<out.blend>]
# Coordinates: three.js (x east, y up, z south) as in the env file; B() converts to Blender (x, -z, y).
import bpy, math, os, sys
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(HERE, '..', 'models', 'SkywardSpire_Kit.glb')
OUT_BLEND = args[1] if len(args) > 1 else ''
G = {'__name__': 'spire_buildings', '__file__': os.path.join(HERE, 'spire_buildings.py')}
exec(compile(open(G['__file__'], encoding='utf-8').read(), G['__file__'], 'exec'), G)
Mesh, MAT, material, srgb, ashlar_image = G['Mesh'], G['MAT'], G['material'], G['srgb'], G['ashlar_image']
pinnacle, finial, crocket_spire, round_tier, ribbed_dome, lantern, lancet, arch_outline, face_matrix, star = (
    G[k] for k in ('pinnacle', 'finial', 'crocket_spire', 'round_tier', 'ribbed_dome', 'lantern', 'lancet', 'arch_outline', 'face_matrix', 'star'))
TAU = math.tau
rng = np.random.default_rng(31)

R, RAIL, LEDGE, BW = 14.0, 14.65, 15.6, 4.2           # floor radius, balustrade line, rim edge, bridge width (env file)
GAP = BW / 2 + 0.35
coll = bpy.context.collection
buildings = [ob for ob in coll.objects if ob.parent is None]

def B(x, y, z): return Vector((x, -z, y))
def group(name, parent=None):
    g = bpy.data.objects.new(name, None); coll.objects.link(g)
    if parent: g.parent = parent
    return g
def instance(src, name, M, parent=None):
    ob = bpy.data.objects.new(name, src.data); coll.objects.link(ob); ob.matrix_world = M
    if parent: ob.parent = parent; ob.matrix_parent_inverse = Matrix()
    return ob
def near_bridge(a, half, r):
    for k in range(4):
        d = abs(((a - k * math.pi / 2) % TAU + TAU + math.pi) % TAU - math.pi)
        if d * r < half: return True
    return False
def rz(a): return Matrix.Rotation(a, 4, 'Z')
def at_angle(a, r, z=0.0):   # three angle a (xz plane) -> Blender frame at radius r: local x along the tangent, y outward
    return face_matrix(math.cos(a) * r, -math.sin(a) * r, z, -a)
def planar_uv(ob, u_axis, v_axis):   # 0..1 UVs from the mesh's own bounds (banners carry a picture, not ashlar)
    me = ob.data; co = np.array([v.co[:] for v in me.vertices])
    lo, hi = co.min(0), co.max(0); uv = me.uv_layers[0].data
    for l in me.loops:
        c = co[l.vertex_index]
        uv[l.index].uv = ((c[u_axis] - lo[u_axis]) / (hi[u_axis] - lo[u_axis] or 1), (c[v_axis] - lo[v_axis]) / (hi[v_axis] - lo[v_axis] or 1))

# ---------------------------------------------------------------- extra materials
def banner_image(w=256, h=1024):
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    t = np.abs(x / w - 0.5) * 2
    img = np.stack([0.10 + 0.04 * (1 - t), 0.20 + 0.08 * (1 - t), 0.54 + 0.14 * (1 - t)], -1)
    img *= (1 - 0.08 * ((x // (w / 8)) % 2))[..., None]                                   # cloth folds
    gold = np.array([0.91, 0.74, 0.41]); edge = np.minimum(np.minimum(x, w - 1 - x), np.minimum(y, h - 1 - y))
    img[(edge < 16)] = gold; img[(edge > 26) & (edge < 30)] = gold
    dx, dy = x - w / 2, y - h * 0.36; rr = np.hypot(dx, dy); th = np.arctan2(dy, dx)
    img[rr < 18 + 62 * np.abs(np.cos(4 * th)) ** 4] = gold                                # eight-point star
    img[np.abs(rr - 62) < 3] = gold; img[np.abs(rr - 72) < 1.5] = gold
    for cx in (0.3, 0.5, 0.7): img[np.hypot(x - w * cx, y - h * 0.66) < 8] = gold
    im = bpy.data.images.new('K_BannerCloth', w, h)
    im.pixels.foreach_set(np.concatenate([img[::-1], np.ones((h, w, 1), np.float32)], 2).ravel()); im.pack()
    return im
MAT['banner'] = material('K_Banner', (1, 1, 1), 0.75, image=banner_image())
MAT['deck'] = material('K_BridgeDeck', (1, 1, 1), 0.7, image=ashlar_image('K_Paving', [0.97, 0.92, 0.80], 4, seed=11))

# ---------------------------------------------------------------- shared parts
def pavilion_mesh():
    me = Mesh()
    me.cyl(0.66, 0.72, 0.55, (0, 0, 0.27), MAT['ivory'], 24)
    me.tube([(math.cos(i / 32 * TAU) * 0.7, math.sin(i / 32 * TAU) * 0.7, 0.56) for i in range(32)], 0.04, MAT['gold'], 4, True)
    for i in range(6):
        a = i / 6 * TAU; px, py = math.cos(a) * 0.5, math.sin(a) * 0.5
        me.box(0.18, 0.18, 0.08, (px, py, 0.6), MAT['ivory'], a); me.cyl(0.06, 0.07, 0.9, (px, py, 1.08), MAT['ivory'], 10)
        me.box(0.17, 0.17, 0.08, (px, py, 1.55), MAT['gold'], a)
    me.cyl(0.4, 0.4, 0.95, (0, 0, 1.05), MAT['shadow'], 16)
    me.cyl(0.72, 0.6, 0.26, (0, 0, 1.68), MAT['ivory'], 24)
    me.tube([(math.cos(i / 32 * TAU) * 0.72, math.sin(i / 32 * TAU) * 0.72, 1.8) for i in range(32)], 0.04, MAT['gold'], 4, True)
    ribbed_dome(me, 0, 0, 1.82, 0.46, MAT['gold'], 8, 1.15)
    finial(me, 0, 0, 2.3, 0.8)
    return me.to_object('Kiosk')

def baluster(me, x, y, z, h, M=Matrix()):   # turned: base, belly, neck, cap
    me.box(0.16, 0.16, 0.06, (x, y, z + 0.03), MAT['ivory'], M=M)
    me.cyl(0.05, 0.075, h * 0.45, (x, y, z + 0.06 + h * 0.22), MAT['ivory'], 8, M=M)
    me.cyl(0.075, 0.045, h * 0.4, (x, y, z + 0.06 + h * 0.65), MAT['ivory'], 8, M=M)
    me.box(0.15, 0.15, 0.05, (x, y, z + 0.06 + h * 0.87), MAT['ivory'], M=M)

def balustrade_bay_mesh(L):   # one straight bay along local x, centred; y = outward
    me = Mesh()
    me.box(L, 0.42, 0.22, (0, 0, 0.11), MAT['ivory'])
    n = max(1, int(L / 0.32))
    for i in range(n): baluster(me, -L / 2 + (i + 0.5) * L / n, 0, 0.22, 0.48)
    me.box(L * 1.01, 0.34, 0.1, (0, 0, 0.78), MAT['ivory'])
    me.box(L * 1.02, 0.4, 0.05, (0, 0, 0.855), MAT['gold'])
    me.box(L, 0.04, 0.16, (0, 0.21, 0.11), MAT['gold'])                                      # gilded plinth band outside
    return me.to_object('BalustradeBay')

def banner_mesh(w, h):   # cloth hanging from z = 0 down to -h, with a gentle belly, across local x
    me = Mesh(); pts = []
    for j in range(9):
        z = -h * j / 8; bow = math.sin(j / 8 * math.pi) * 0.06
        pts.append([(x, bow * (1 - (2 * x / w) ** 2), z) for x in np.linspace(-w / 2, w / 2, 5)])
    vs = [[me.bm.verts.new(p) for p in row] for row in pts]
    for j in range(8):
        for i in range(4): me.bm.faces.new((vs[j][i], vs[j + 1][i], vs[j + 1][i + 1], vs[j][i + 1]))
    me.slot(MAT['banner'])
    ob = me.to_object('BannerCloth'); planar_uv(ob, 0, 2)
    return ob

# ---------------------------------------------------------------- rim
gRim = group('Kit_Rim')
me = Mesh()
seg = 192; inner = [me.bm.verts.new((math.cos(i / seg * TAU) * R, math.sin(i / seg * TAU) * R, 0.03)) for i in range(seg)]
outer = [me.bm.verts.new((math.cos(i / seg * TAU) * LEDGE, math.sin(i / seg * TAU) * LEDGE, 0.03)) for i in range(seg)]
for i in range(seg): me.bm.faces.new((inner[i], outer[i], outer[(i + 1) % seg], inner[(i + 1) % seg]))
me.slot(MAT['ivory'])
for r, t, z in ((R, 0.05, 0.04), (LEDGE, 0.09, 0.0), (LEDGE + 0.04, 0.1, -0.15), (LEDGE + 0.12, 0.12, -0.9), (LEDGE + 0.14, 0.15, -5.8)):
    me.tube([(math.cos(i / 160 * TAU) * r, math.sin(i / 160 * TAU) * r, z) for i in range(160)], t, MAT['gold'], 5, True)
me.cyl(LEDGE, LEDGE, 6, (0, 0, -3.05), MAT['stone'], 96)                                      # drum wall
for i in range(48):                                                                            # blind arcade with gold hood moulds
    a = (i + 0.5) / 48 * TAU; F = face_matrix(math.cos(a) * LEDGE, math.sin(a) * LEDGE, 0, a)
    o = arch_outline(1.0, 3.4); me.ngon([(x, 0.02, z - 4.9) for x, z in o], MAT['shadow'], F)
    me.tube([(x, 0.06, z - 4.9) for x, z in o[:-1]], 0.06, MAT['ivory'], 4, False, F)
    me.tube([(x, 0.1, z - 5.0) for x, z in arch_outline(1.3, 3.6)[1:-1]], 0.05, MAT['gold'], 3, False, F)
    b = a + TAU / 96; me.box(0.32, 0.32, 6, (math.cos(b) * (LEDGE + 0.1), math.sin(b) * (LEDGE + 0.1), -3), MAT['ivory'], b)
    me.box(0.4, 0.4, 0.14, (math.cos(b) * (LEDGE + 0.12), math.sin(b) * (LEDGE + 0.12), -0.4), MAT['gold'], b)
for rt, rb, h, y, m in ((LEDGE + 0.3, LEDGE - 0.4, 1, -6.5, 'stoneShade'), (LEDGE - 0.4, 11, 6, -10, 'stone'), (11, 6.5, 8, -17, 'stoneShade'),
                        (6.5, 2, 10, -26, 'stone'), (2, 0.2, 6, -34, 'gold')):
    me.cyl(rt, rb, h, (0, 0, y), MAT[m], 64)
for y, r in ((-7.1, LEDGE - 0.35), (-13.1, 11), (-21.1, 6.5)):
    me.tube([(math.cos(i / 96 * TAU) * r, math.sin(i / 96 * TAU) * r, y) for i in range(96)], 0.14, MAT['gold'], 5, True)
for i in range(16):                                                                            # hanging pinnacles under the rim
    a = (i + 0.25) / 16 * TAU
    if near_bridge(a, 3, LEDGE): continue
    rr, ln = LEDGE - 1.2, 3 + (i * 5 % 4) * 1.5; x, y = math.cos(a) * rr, -math.sin(a) * rr
    me.cyl(0.4, 0.35, ln, (x, y, -7 - ln / 2), MAT['stone'], 8)
    me.cyl(0.46, 0.46, 0.14, (x, y, -7 - ln), MAT['gold'], 8)
    me.cyl(0, 0.38, 2.2, (x, y, -8.1 - ln), MAT['ivory'], 8)
    for k in range(4):
        b = k * math.pi / 2; me.box(0.12, 0.12, 0.12, (x + math.cos(b) * 0.25, y + math.sin(b) * 0.25, -7.8 - ln), MAT['ivory'], b)
ob = me.to_object('Rim_Drum'); ob.parent = gRim
# balustrade bays on the four arcs between the bridge mouths
a0 = (GAP + 0.4) / RAIL; span = math.pi / 2 - 2 * a0; nb = round(span * RAIL / 0.75); L = span * RAIL / nb
bay = balustrade_bay_mesh(L * 1.01); bay.parent = gRim
first = True
for k in range(4):
    for i in range(nb):
        a = k * math.pi / 2 + a0 + (i + 0.5) / nb * span
        if first: bay.matrix_world = at_angle(a, RAIL); first = False
        else: instance(bay, 'BalustradeBay', at_angle(a, RAIL), gRim)
# kiosks: seven small ones on each arc, a larger pair at each bridge mouth
kiosk = pavilion_mesh(); kiosk.parent = gRim; first = True
def put_kiosk(x3, z3, s, parent=gRim):
    global first
    M = Matrix.Translation(B(x3, 0.05, z3)) @ Matrix.Diagonal((s, s, s, 1))
    if first: kiosk.matrix_world = M; first = False; return kiosk
    return instance(kiosk, 'Kiosk', M, parent)
for k in range(4):
    a0k, a1k = k * math.pi / 2 + (GAP + 2.6) / RAIL, (k + 1) * math.pi / 2 - (GAP + 2.6) / RAIL
    for i in range(7):
        a = a0k + (a1k - a0k) * i / 6; put_kiosk(math.cos(a) * RAIL, math.sin(a) * RAIL, 0.55)
    a = k * math.pi / 2
    for s in (-1, 1):
        lx, lz = RAIL + 0.2, s * (GAP + 0.55)
        put_kiosk(lx * math.cos(a) - lz * math.sin(a), lx * math.sin(a) + lz * math.cos(a), 0.95)
# gate thresholds
me = Mesh()
me.box(1.6, BW + 1.6, 0.06, (R + 0.8, 0, 0.03), MAT['ivory'])
for s in (-1, 1): me.box(1.6, 0.07, 0.07, (R + 0.8, s * (BW / 2 + 0.8), 0.06), MAT['gold'])
me.box(0.07, BW + 1.6, 0.07, (LEDGE, 0, 0.06), MAT['gold'])
gate = me.to_object('Threshold'); gate.parent = gRim; gate.matrix_world = rz(0)
for k in (1, 2, 3): instance(gate, 'Threshold', rz(-k * math.pi / 2), gRim)
# long star banners hanging over the drum either side of each bridge mouth
gBan = group('Kit_RimBanners', gRim)
rim_banner = banner_mesh(1.15, 4.2); rim_banner.parent = gBan; first_b = True
rod = Mesh(); rod.cyl(0.05, 0.05, 1.5, (0, 0, 0), MAT['gold'], 8, M=Matrix.Rotation(math.pi / 2, 4, 'Y'))
rod.sphere(0.08, (0.75, 0, 0), MAT['gold'], 8, 6); rod.sphere(0.08, (-0.75, 0, 0), MAT['gold'], 8, 6)
rod_ob = rod.to_object('BannerRod'); rod_ob.parent = gBan; first_r = True
for k in range(4):
    for s in (-1, 1):
        for off in (1.5, 3.4):
            a = k * math.pi / 2 + s * (GAP + off) / LEDGE; F = at_angle(a, LEDGE + 0.12, -0.05)
            Mb = F @ Matrix.Rotation(0.5, 4, 'X')
            if first_b: rim_banner.matrix_world = Mb; first_b = False
            else: instance(rim_banner, 'BannerCloth', Mb, gBan)
            if first_r: rod_ob.matrix_world = F; first_r = False
            else: instance(rod_ob, 'BannerRod', F, gBan)

# ---------------------------------------------------------------- bridges (one shared mesh in the bridge's own frame: x outward)
gBr = group('Kit_Bridges')
x0, x1 = LEDGE - 0.1, 46.0; ln = x1 - x0; mid = (x0 + x1) / 2; hw = BW / 2
me = Mesh()
me.box(ln, BW, 0.5, (mid, 0, -0.25), MAT['deck'])
me.box(ln, BW + 0.5, 1.2, (mid, 0, -1.1), MAT['stoneShade'])
me.box(ln, BW + 0.62, 0.12, (mid, 0, -0.5), MAT['gold']); me.box(ln, BW + 0.56, 0.1, (mid, 0, -1.7), MAT['goldDark'])
posts = np.arange(17.2, x1 - 1, 3.2)
for s in (-1, 1):
    y = s * (hw - 0.15)
    me.box(ln, 0.32, 0.18, (mid, y, 0.09), MAT['ivory'])
    me.box(ln, 0.3, 0.1, (mid, y, 0.72), MAT['ivory']); me.box(ln, 0.36, 0.05, (mid, y, 0.795), MAT['gold'])
    for xb in np.arange(x0 + 0.3, x1 - 0.2, 0.34):
        if np.min(np.abs(posts - xb)) > 0.32: baluster(me, xb, y, 0.18, 0.48)
    for px in posts:                                                                           # domed posts
        me.box(0.4, 0.4, 1.3, (px, y, 0.65), MAT['ivory'])
        me.box(0.48, 0.48, 0.07, (px, y, 1.3), MAT['gold'])
        ribbed_dome(me, px, y, 1.33, 0.22, MAT['gold'], 6, 1.2); me.sphere(0.06, (px, y, 1.62), MAT['gold'], 8, 6)
    for i in range(10): me.box(0.5, 0.05, 0.6, (x0 + 0.9 + i * (ln - 1.8) / 9, s * (hw + 0.27), -0.95), MAT['cobalt'])
for i in range(5):                                                                             # pointed arches between piers
    p0, p1 = x0 + i / 5 * ln - ln / 10 + ln / 10, x0 + (i + 1) / 5 * ln
    px = x0 + (i + 0.5) / 5 * ln - ln / 10
    me.box(0.9, 0.9, 12, (px, 0, -7.7), MAT['stone']); me.cyl(0, 0.65, 3, (px, 0, -15.2), MAT['stoneShade'], 4, rz=math.pi / 4)
    for zb in (-2.2, -6.5): me.box(1.05, 1.05, 0.16, (px, 0, zb), MAT['gold'])
    w_arch = ln / 5 - 0.9; ax = px + ln / 10
    o = arch_outline(w_arch, 2.4, 8)
    for s in (-1, 1): me.tube([(ax + x, s * (BW / 2 + 0.1), -4.0 + z) for x, z in o[1:-1]], 0.24, MAT['ivory'], 5)
bridge = me.to_object('Bridge'); bridge.parent = gBr
plaq = Mesh()
for px in (19.2, 28.5, 38):
    plaq.box(1.5, 0.62, 0.03, (px, 0, 0.012), MAT['gold']); plaq.box(1.36, 0.48, 0.04, (px, 0, 0.018), MAT['cobalt'])
    plaq.ngon([(px + x * 0.6, z * 0.6, 0.042) for x, z in star(0.06, 0.2)], MAT['gold'], out=(0, 0, 1))
plaques = plaq.to_object('BridgePlaques'); plaques.parent = gBr
br_banner = banner_mesh(2.5, 1.0); planar_uv(br_banner, 2, 0); br_banner.parent = gBr; firstbb = True   # long side along the bridge
for name, ang in (('Bridge_E', 0), ('Bridge_S', math.pi / 2), ('Bridge_W', math.pi), ('Bridge_N', -math.pi / 2)):
    M = rz(-ang)
    if name == 'Bridge_E': bridge.matrix_world = M; bridge.name = name; plaques.matrix_world = M
    else: instance(bridge, name, M, gBr)
    if name == 'Bridge_W': instance(plaques, 'BridgePlaques', M, gBr)
    for s in (-1, 1):                                                                          # banners draped over the balustrade
        for px in posts:
            Mb = M @ Matrix.Translation((px + 1.6, -s * (hw + 0.02), 0.66)) @ face_matrix(0, 0, 0, -math.pi / 2 if s > 0 else math.pi / 2) @ \
                 Matrix.Rotation(0.65, 4, 'X')   # outward-facing frame, bottom swung out over the edge
            if firstbb: br_banner.matrix_world = Mb; firstbb = False
            else: instance(br_banner, 'BannerCloth', Mb, gBr)

# ---------------------------------------------------------------- kiosks on columns (W and E arcs)
gK = group('Kit_Kiosks')
col = Mesh(); col.cyl(0.9, 0.7, 20, (0, 0, -10), MAT['stoneShade'], 16)
for z in (-3, -9, -15): col.cyl(0.95, 0.95, 0.14, (0, 0, z), MAT['gold'], 16)
for z in (-1.2, -7, -13):
    for i in range(4):
        a = i / 4 * TAU; col.box(0.3, 0.06, 1.6, (math.cos(a) * 0.86, math.sin(a) * 0.86, z), MAT['cobalt'], a + math.pi / 2)
col_ob = col.to_object('KioskColumn'); col_ob.parent = gK; firstc = True
KIOSKS = [(-17.2, -5.6, 1.6), (-19.6, -8.4, 1.3), (17.4, -5.6, 1.6), (19.8, -8.4, 1.3), (-17.6, 3.8, 1.3), (17.6, 3.8, 1.3)]
for x, z, s in KIOSKS:
    M = Matrix.Translation(B(x, 0, z)) @ Matrix.Diagonal((s, s, 1, 1))
    if firstc: col_ob.matrix_world = M; firstc = False
    else: instance(col_ob, 'KioskColumn', M, gK)
    put_kiosk(x, z, s, gK)

# ---------------------------------------------------------------- spires
gSp = group('Kit_Spires')
def filler_spire(name, x3, z3, top, w, kind, simple=False):
    me = Mesh(); base = top - 14 - rng.random() * 18; h = top - base - w * 3; y = base + h
    me.cyl(w * 0.7, w * 0.8, h, (0, 0, base + h / 2), MAT['stone'], 12 if simple else 20)
    bands = 2 + int(rng.random() * 3)
    for i in range(1, bands + 1): me.cyl(w * 0.78, w * 0.78, 0.15, (0, 0, base + h * i / bands * 0.98), MAT['gold'], 12 if simple else 20)
    if not simple:
        nw = 3 + int(rng.random() * 3)
        for i in range(nw):
            a = i / nw * TAU; lancet(me, face_matrix(0, 0, 0, a) @ Matrix.Translation((0, w * 0.7, y - w * 2.6)), w * 0.4, w * 1.9, 1, True)
    if kind == 0:
        me.cyl(w * 0.72, 0, w * 3, (0, 0, y + w * 1.5), MAT['ivory'], 12); me.sphere(w * 0.12, (0, 0, y + w * 3.05), MAT['gold'], 8, 6)
    elif kind == 1:
        ribbed_dome(me, 0, 0, y, w * 0.72, MAT['gold'], 8); finial(me, 0, 0, y + w * 0.8, w * 0.6)
    elif kind == 2:
        ribbed_dome(me, 0, 0, y, w * 0.72, MAT['cobalt'], 8); me.cyl(w * 0.08, 0, w * 2, (0, 0, y + w * 1.8), MAT['gold'], 6)
    else:
        if simple: me.cyl(w * 0.5, 0, w * 5, (0, 0, y + w * 2.5), MAT['ivory'], 8)
        else: crocket_spire(me, 0, 0, y, w * 0.5, w * 5)
    ob = me.to_object(name); ob.matrix_world = Matrix.Translation(B(x3, 0, z3)) @ rz(rng.random() * TAU); ob.parent = gSp
    return ob
FILLER = [(-30, -30, 14, 1.6, 0), (-37, -20, 10, 1.4, 1), (-13, -28, 6, 1.2, 1), (-21, -31, 16, 1.5, 0), (13, -28, 6, 1.2, 1), (21, -31, 16, 1.5, 0),
          (30, -30, 14, 1.6, 0), (37, -20, 10, 1.4, 1), (-38, -3, 8, 1.6, 0), (-36, 4, 14, 1.4, 1), (38, -3, 8, 1.6, 0), (37, 4, 14, 1.4, 1),
          (-33, 11, 6, 1.4, 1), (33, 11, 6, 1.4, 1), (31, 27, 3, 1.6, 0), (-31, 27, 2, 1.6, 0), (26, 35, -2, 1.4, 0), (-40, 18, 4, 1.5, 0), (40, 18, 4, 1.5, 0)]
spire_anchors = []
for i, (x, z, top, w, kind) in enumerate(FILLER):
    if kind == 1 and top < 8: kind = 3
    filler_spire(f'Spire_{i}', x, z, top, w, kind); spire_anchors.append((x, z, w * 1.2))
gAb = group('Kit_Abyss')
for i in range(40):                                                                            # far ring, deep in the abyss
    a = rng.random() * TAU; d = 48 + rng.random() * 60
    ob = filler_spire(f'FarSpire_{i}', math.cos(a) * d, math.sin(a) * d, -6 - rng.random() * 22 + (10 if d > 80 else 0), 1.4 + rng.random() * 1.6, int(rng.random() * 4), True)
    ob.parent = gAb; spire_anchors.append((math.cos(a) * d, math.sin(a) * d, 2.0))
# floating islands far out: three shared designs, instanced with scale and turn
def island_mesh(seed):
    r = np.random.default_rng(seed); me = Mesh()
    me.cyl(7, 6, 2, (0, 0, -1), MAT['stoneShade'], 16); me.cyl(0, 6, 14, (0, 0, -9), MAT['stoneShade'], 12)
    me.tube([(math.cos(i / 32 * TAU) * 7, math.sin(i / 32 * TAU) * 7, 0) for i in range(32)], 0.25, MAT['gold'], 4, True)
    for i in range(3 + seed % 3):
        b = r.random() * TAU; rr = 0 if i == 0 else 2 + r.random() * 3.5; ht = 18 + r.random() * 12 if i == 0 else 8 + r.random() * 10; ww = 1.8 if i == 0 else 0.9
        x, y = math.cos(b) * rr, math.sin(b) * rr
        me.cyl(ww, ww * 1.1, ht, (x, y, ht / 2), MAT['stone'], 12); me.cyl(ww * 1.1, ww * 1.1, 0.25, (x, y, ht * 0.7), MAT['gold'], 12)
        if r.random() < 0.6: me.cyl(ww, 0, ww * 4, (x, y, ht + ww * 2), MAT['ivory'], 12)
        else: ribbed_dome(me, x, y, ht, ww, MAT['cobalt'] if r.random() < 0.5 else MAT['gold'], 8)
    return me.to_object(f'Island_{seed}')
isl = [island_mesh(s) for s in (1, 2, 3)]
for ob in isl: ob.parent = gAb
for i in range(18):
    a = (i + rng.random() * 0.6) / 18 * TAU; d = 110 + rng.random() * 90; s = 0.8 + rng.random() * 0.7
    M = Matrix.Translation(B(math.cos(a) * d, -10 + rng.random() * 30, math.sin(a) * d)) @ rz(rng.random() * TAU) @ Matrix.Diagonal((s, s, s, 1))
    if i < 3: isl[i].matrix_world = M
    else: instance(isl[i % 3], 'Island', M, gAb)

# ---------------------------------------------------------------- skybridges
def skybridge(name, p0, p1, w):
    a, b = B(*p0), B(*p1); d = b - a; L = d.length; me = Mesh()
    me.box(L, w, 0.45, (0, 0, 0), MAT['ivory'])
    for s in (-1, 1):
        for xb in np.arange(-L / 2 + 0.2, L / 2, 0.34): baluster(me, xb, s * (w / 2 - 0.09), 0.22, 0.42)
        me.box(L, 0.2, 0.06, (0, s * (w / 2 - 0.09), 0.72), MAT['gold'])
    o = arch_outline(L * 0.9, L * 0.32, 10)
    for s in (-1, 1): me.tube([(x, s * w * 0.4, -0.25 - (L * 0.32 - z)) for x, z in o[1:-1]], 0.16, MAT['ivory'], 5)
    ob = me.to_object(name); ob.matrix_world = Matrix.Translation((a + b) / 2) @ rz(math.atan2(d.y, d.x)); ob.parent = gK
skybridge('Skybridge_NW', (-36, 9, -28), (-28, 9, -21), 1.4)
skybridge('Skybridge_E', (33, 7, -9), (31, 7, -1.5), 1.3)

# ---------------------------------------------------------------- cloud anchors
gA = group('CloudAnchors')
def anchor(x3, z3, r):
    e = bpy.data.objects.new('CloudAnchor', None); coll.objects.link(e); e.location = B(x3, 0, z3); e.scale = (r, r, r); e.parent = gA
for i in range(12): a = (i + 0.5) / 12 * TAU; anchor(math.cos(a) * 10.5, math.sin(a) * 10.5, 4.2)    # the dais sinks into cloud
for ang in (0, math.pi / 2, math.pi, -math.pi / 2):                                                    # bridge piers
    for i in range(5):
        px = x0 + (i + 0.5) / 5 * ln - ln / 10; anchor(px * math.cos(ang), px * math.sin(ang), 1.5)
for x, z, s in KIOSKS: anchor(x, z, 1.2 * s)
for x, z, r in spire_anchors: anchor(x, z, r)
for ob in buildings:                                                                                   # landmark footprints
    for o in ([ob] + list(ob.children) if ob.type == 'EMPTY' else [ob]):
        if o.type != 'MESH': continue
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        xs, ys = [c.x for c in bb], [c.y for c in bb]
        r = 0.45 * max(max(xs) - min(xs), max(ys) - min(ys)) * 0.6
        anchor((max(xs) + min(xs)) / 2, -(max(ys) + min(ys)) / 2, max(1.0, r))

# ---------------------------------------------------------------- export
gB = group('Kit_Buildings')
for ob in buildings: ob.parent = gB
root = group('SkywardSpire_Kit')
for g in (gRim, gBr, gK, gSp, gAb, gA, gB): g.parent = root
tris = 0; dg = bpy.context.evaluated_depsgraph_get(); meshes = set()
for ob in coll.objects:
    if ob.type == 'MESH' and ob.data.name not in meshes:
        meshes.add(ob.data.name); ev = ob.evaluated_get(dg); m = ev.to_mesh(); m.calc_loop_triangles(); tris += len(m.loop_triangles); ev.to_mesh_clear()
print('TRIS', tris, 'unique meshes', len(meshes), 'objects', len(coll.objects))
if OUT_BLEND: bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_apply=True, export_image_format='JPEG', export_image_quality=88,
                          export_yup=True, export_materials='EXPORT')
print('EXPORTED', OUT, os.path.getsize(OUT))
