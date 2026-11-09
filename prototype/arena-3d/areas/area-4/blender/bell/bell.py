# blender -b -P bell.py -- <unity Models dir>
# The cracked celestial bell (hero), modelled in Blender. Pivot at the centre of the mouth (lip at z 0), the break
# faces +Y (Unity +Z, toward the camera once the builder tilts the bell -45°).
#   Bell_Body   128-segment surface of revolution from the 41-point bell profile (outer wall, rounded lip, inner wall)
#               with a jagged 30° break; UVs match the BellBronze/BellCracks/BellMetal paint made in Unity
#               (u across the covered angle, v = profile arc length). Slot: 0 painted bronze.
#   Bell_Parts  wall faces on both jagged break edges, a thick worn lip rim, three gold bands with raised numeral
#               studs (broken at the gap), cast crown loop, iron yoke and straps, lathed clapper with a brass collar.
#               Slots: 0 bronze, 1 brass, 2 polished brass, 3 iron, 4 fracture (break walls).
#   Bell_Shard, Bell_Shard2..5  five detached wall pieces from the same profile (largest first) with the shared bands
#               and arch row, re-centred on their own origins; placed and animated by the builder.
#               Slots: 0 bronze, 1 fracture rim, 2 patina (inner face), 3 brass ornament.
# The break uses the same per-row gap formula as ShatteredHourArenaBuilder.BellGap, so geometry and paint agree.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset()
PAINT, BRONZE, BRASS, POL, IRON, FRAC, PATINA = lb.slots(['BellPaint', 'Bronze', 'Brass', 'Polished', 'Iron', 'Fracture', 'Patina'])

def bell_profile(k):
    p = [(0, 10.4), (1.6, 10.3), (2.7, 9.8), (3.3, 8.8), (3.5, 7.2), (3.6, 5.4), (3.95, 3.7), (4.6, 2.2), (5.6, 1.0), (6.6, 0.25), (6.9, 0), (6.7, -0.12),
         (6.0, 0.15), (5.0, 0.9), (4.1, 2.1), (3.45, 3.7), (3.1, 5.6), (2.95, 7.4), (2.6, 8.9), (1.6, 9.5), (0, 9.65)]
    return [(x * k, y * k) for x, y in p]
def densify(p, sub):
    o = []
    for i in range(len(p) - 1):
        p0, p1, p2, p3 = p[max(0, i - 1)], p[i], p[i + 1], p[min(len(p) - 1, i + 2)]
        for s in range(sub):
            t = s / sub; t2, t3 = t * t, t * t * t
            o.append(tuple(0.5 * (2 * p1[c] + (-p0[c] + p2[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t2 + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * t3) for c in (0, 1)))
    o.append(p[-1]); return o
K_SCALE = 5.5 / 6.9; prof = densify(bell_profile(K_SCALE), 2); LIP = 20; GAP_HALF = math.radians(15)
def gap(j):  # must match ShatteredHourArenaBuilder.BellGap
    h = (j * 0.6180339887) % 1.0
    return GAP_HALF * ((1.3 if j % 3 == 0 else 0.8) + 0.35 * h) * ((1 + 0.35 * j / LIP) if j <= LIP else 1.3)
def radius_at(y):
    for i in range(LIP):
        if prof[i][1] >= y >= prof[i + 1][1]:
            t = (y - prof[i + 1][1]) / max(1e-4, prof[i][1] - prof[i + 1][1]); return prof[i + 1][0] + (prof[i][0] - prof[i + 1][0]) * t
    return prof[LIP][0]
def bpt(phi, r, z): return (-math.sin(phi) * r, math.cos(phi) * r, z)   # same angle convention as lb.lathe

# ornament shared by the bell and its shards (one parent, one pitch): a row of raised pointed arches with fleur tips
# standing on a thin sill band (y 3.62) under the gold band at y 6.08, one arch every PITCH radians round the bell.
PITCH, ORN_Y0, ORN_SPRING, ORN_APEX, ORN_SILL = math.radians(20), 3.78, 4.95, 5.85, 3.62
def surf(phi, y, lift): return bpt(phi, radius_at(y) + lift, y)
def strip(points, w, h, mat):
    """Raised strip following the bell surface through (phi, y) points: one bevel-free box per segment."""
    bm = bmesh.new()
    for (p0, y0), (p1, y1) in zip(points, points[1:]):
        a, b = Vector(surf(p0, y0, 0)), Vector(surf(p1, y1, 0)); d = (b - a).normalized()
        n = Vector((-math.sin((p0 + p1) / 2), math.cos((p0 + p1) / 2), 0)); s = d.cross(n).normalized() * (w / 2)
        vs = [bm.verts.new(p) for p in (a - s, a + s, b + s, b - s, a - s + n * h, a + s + n * h, b + s + n * h, b - s + n * h)]
        for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)): bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return lb._obj('strip', bm, mat)
def arch(pc, mat):
    hw = PITCH * 0.38; legs = 6; out = []
    for side in (-1, 1):
        pts = [(pc + side * hw, ORN_Y0)] + [(pc + side * hw, ORN_SPRING)]
        for k in range(1, legs + 1):   # pointed (two-centred) head: each side curves in to the apex
            t = k / legs; pts.append((pc + side * hw * (1 - t) ** 1.35, ORN_SPRING + (ORN_APEX - ORN_SPRING) * math.sin(t * math.pi / 2)))
        out.append(strip(pts, 0.1, 0.06, mat))
    tip = lb.cone('fleur', 0.1, 0.32, 4, surf(pc, ORN_APEX + 0.02, 0.03), mat, (0, 0, -pc)); out.append(tip)
    for side in (-1, 1): out.append(lb.sphere('fleur_lobe', 0.07, surf(pc + side * 0.035, ORN_APEX + 0.1, 0.04), mat, 6, 4))
    return out

body = lb.lathe('Bell_Body', prof, 128, PAINT, gap); lb.finish(body, 0.0001, flat=False)

parts = []
# wall faces on both break edges: outer row j joined to inner row K-1-j
bm = bmesh.new(); K = len(prof)
for s in (1, -1):
    for j in range(LIP):
        i0, i1 = K - 1 - j, K - 2 - j
        vs = [bm.verts.new(bpt(s * gap(jj), prof[jj][0], prof[jj][1])) for jj in (j, j + 1, i1, i0)]
        bm.faces.new(vs)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); parts.append(lb._obj('break_faces', bm, FRAC))
# lip rim, bands, numerals (Blender angle 90° = +Y = the break)
lg = math.degrees(gap(LIP)) * 1.05
parts.append(lb.torus('lip_rim', prof[LIP][0] + 0.02, 0.11, (0, 0, prof[LIP][1] - 0.03), BRONZE, seg=128, tube=8, arc=360 - 2 * lg, start=90 + lg))
for y in (0.12, 2.72, 6.08):
    r = radius_at(y) + 0.04
    parts.append(lb.torus('band', r, 0.12, (0, 0, y), BRASS, seg=128, tube=8, arc=316, start=112))
    for i in range(12):
        a = math.radians(112 + 12 + i * (316 - 24) / 11)
        parts.append(lb.box('numeral', (0.2, 0.1, 0.26), (math.cos(a) * (r + 0.08), math.sin(a) * (r + 0.08), y), POL, (0, 0, a - math.pi / 2)))
gmax = max(gap(j) for j in range(4, 16)) + PITCH * 0.5
parts.append(lb.torus('sill', radius_at(ORN_SILL) + 0.03, 0.07, (0, 0, ORN_SILL), BRASS, seg=128, tube=6, arc=360 - 2 * math.degrees(gmax - PITCH * 0.3), start=90 + math.degrees(gmax - PITCH * 0.3)))
for k in range(math.ceil(gmax / PITCH), math.floor((2 * math.pi - gmax) / PITCH) + 1): parts += arch(k * PITCH, BRASS)
top = prof[0][1]
parts.append(lb.torus('crown', 0.75, 0.24, (0, 0, top + 0.7), BRONZE, (math.radians(90), 0, 0), 32, 10))
o = lb.box('yoke', (3.4, 0.55, 0.45), (0, 0, top + 1.55), IRON); lb.bevel(o, 0.04, 2); parts.append(o)
for x in (-0.55, 0.55): o = lb.box('strap', (0.14, 0.4, 1.0), (x, 0, top + 1.0), IRON); lb.bevel(o, 0.02, 1); parts.append(o)
clapper = [(0.09, top - 0.6), (0.11, 2.6), (0.2, 2.1), (0.42, 1.6), (0.62, 1.05), (0.58, 0.6), (0.36, 0.3), (0.0, 0.22)]
cl = lb.lathe('clapper', clapper, 24, IRON); parts.append(cl)
parts.append(lb.torus('collar', 0.2, 0.07, (0, 0, 2.15), POL, seg=24, tube=8))
# clapper head: a forged ball with two raised bands and a teardrop tip, so it reads as cast iron rather than a blob
parts += [lb.torus('clapper_band', 0.61, 0.06, (0, 0, 1.0), POL, seg=24, tube=6), lb.torus('clapper_band', 0.57, 0.05, (0, 0, 0.6), POL, seg=24, tube=6),
          lb.cone('clapper_tip', 0.18, 0.35, 8, (0, 0, 0.25), IRON, (math.pi, 0, 0))]
bp = lb.join(parts, 'Bell_Parts', [BRONZE, BRASS, POL, IRON, FRAC]); lb.cube_uv(bp); lb.finish(bp)

# shards: five detached pieces of wall cut from the same parent profile, largest to smallest, each carrying the same
# bands and arch row (concept: Reference/Codex-Corrections-20261025/17). Jagged cut edges, real wall thickness,
# patina on the inner face. Each piece is re-centred on its own origin; the builder places them round the break.
# Slots: 0 bronze, 1 fracture rim, 2 patina (inner face), 3 brass ornament.
def shard(name, width, ys, seed):
    rows = (min(j for j in range(LIP + 1) if prof[j][1] <= ys[0]), max(j for j in range(LIP + 1) if prof[j][1] >= ys[1]))
    rr = random.Random(seed); g = math.pi - width / 2
    o = lb.lathe(name, prof[rows[0]:rows[1] + 1], max(4, int(width / math.radians(4))), BRONZE, lambda j: g)
    bm = bmesh.new(); bm.from_mesh(o.data); top, bot = prof[rows[0]][1], prof[rows[1]][1]
    for v in bm.verts:   # jag the cut edges: the side columns sideways, the top and bottom rows up and down
        phi = math.atan2(-v.co.x, v.co.y) % (2 * math.pi); edge_side = min(abs(phi - g), abs(phi - (2 * math.pi - g))) < 1e-3
        if edge_side:
            d = rr.uniform(-0.09, 0.09); c, s = math.cos(d), math.sin(d); x, y = v.co.x, v.co.y; v.co.x, v.co.y = x * c - y * s, x * s + y * c
        if abs(v.co.z - top) < 1e-3 or abs(v.co.z - bot) < 1e-3: v.co.z += rr.uniform(-0.28, 0.28)
    bm.to_mesh(o.data); bm.free()
    for m in (IRON, PATINA): o.data.materials.append(m)
    sol = o.modifiers.new('sol', 'SOLIDIFY'); sol.thickness = 0.24; sol.offset = -1; sol.use_rim = True; sol.material_offset_rim = 1
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='sol')
    for p in o.data.polygons:   # inner face (normal toward the axis) takes the patina slot
        c = p.center; r = Vector((c.x, c.y, 0))
        if p.material_index == 0 and r.length > 1e-3 and p.normal.dot(r.normalized()) < -0.3: p.material_index = 2
    orn = []
    for y in (2.72, ORN_SILL, 6.08):
        if bot - 0.1 < y < top + 0.1: orn.append(lb.torus('band', radius_at(y) + 0.04, 0.12, (0, 0, y), BRASS, seg=max(6, int(width / math.radians(3))), tube=8, arc=math.degrees(width) * 0.92, start=90 + math.degrees(g) + math.degrees(width) * 0.04))
    k0 = math.ceil((g + PITCH * 0.45) / PITCH); k1 = math.floor((2 * math.pi - g - PITCH * 0.45) / PITCH)
    for k in range(k0, k1 + 1): orn += arch(k * PITCH, BRASS)
    if orn:
        ob = lb.join(orn, 'orn', [BRASS])
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); ob.select_set(True); bpy.context.view_layer.objects.active = o; bpy.ops.object.join()
    o = lb.join([o], name, [BRONZE, IRON, PATINA, BRASS])
    cen = sum((v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
    for v in o.data.vertices: v.co -= cen
    lb.cube_uv(o); return lb.finish(o)
shards = [shard('Bell_Shard', math.radians(w), rows, 50 + i) for i, (w, rows) in enumerate([(132, (6.9, 1.6)), (96, (6.6, 2.0)), (72, (6.4, 2.3)), (50, (6.3, 2.5)), (32, (6.2, 2.6))])]
for k, s in enumerate(shards[1:], 2): s.name = s.data.name = 'Bell_Shard%d' % k

lb.preview(os.path.join(here, 'renders', 'bell.png'), (9.5, 12.0, 4.5), (0, 0, 4.0))
lb.export([body, bp] + shards, os.path.join(out, 'Bell', 'Bell.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'bell.blend')); print('done')
