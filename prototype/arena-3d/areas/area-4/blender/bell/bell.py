# blender -b -P bell.py -- <unity Models dir>
# The cracked celestial bell (hero), modelled in Blender. Pivot at the centre of the mouth (lip at z 0), the break
# faces +Y (Unity +Z, toward the camera once the builder tilts the bell -45°).
#   Bell_Body   128-segment surface of revolution from the 41-point bell profile (outer wall, rounded lip, inner wall)
#               with a jagged 30° break; UVs match the BellBronze/BellCracks/BellMetal paint made in Unity
#               (u across the covered angle, v = profile arc length). Slot: 0 painted bronze.
#   Bell_Parts  wall faces on both jagged break edges, a thick worn lip rim, three gold bands with raised numeral
#               studs (broken at the gap), cast crown loop, iron yoke and straps, lathed clapper with a brass collar.
#               Slots: 0 bronze, 1 brass, 2 polished brass, 3 iron, 4 fracture (break walls).
#   Bell_Shard / Bell_Shard2  two thick broken pieces of wall placed and animated by the builder.
#               Slots: 0 bronze, 1 fracture rim.
# The break uses the same per-row gap formula as ShatteredHourArenaBuilder.BellGap, so geometry and paint agree.
import bpy, bmesh, sys, os, math
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset()
PAINT, BRONZE, BRASS, POL, IRON, FRAC = lb.slots(['BellPaint', 'Bronze', 'Brass', 'Polished', 'Iron', 'Fracture'])

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

# shards: a strip of outer wall with a chipped edge, given real thickness; the fracture rim is slot 1 (dark broken bronze)
def shard(name, rows, seg, fn, jag):
    o = lb.lathe(name, prof[rows[0]:rows[1]], seg, BRONZE, fn); o.data.materials.append(IRON)
    bm = bmesh.new(); bm.from_mesh(o.data)
    for k, v in enumerate(bm.verts): v.co += v.co.normalized() * jag * ((k * 0.618) % 1.0 - 0.5)
    bm.to_mesh(o.data); bm.free()
    m = o.modifiers.new('sol', 'SOLIDIFY'); m.thickness = 0.28; m.offset = -1; m.use_rim = True; m.material_offset_rim = 1
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='sol')
    lb.cube_uv(o); return lb.finish(o)
sh1 = shard('Bell_Shard', (8, 32), 6, lambda j: math.pi - 0.22 - 0.05 * (j % 3), 0.12)
sh2 = shard('Bell_Shard2', (14, 28), 4, lambda j: math.pi - 0.12 - 0.04 * (j % 2), 0.1)

lb.preview(os.path.join(here, 'renders', 'bell.png'), (9.5, 12.0, 4.5), (0, 0, 4.0))
lb.export([body, bp, sh1, sh2], os.path.join(out, 'Bell', 'Bell.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'bell.blend')); print('done')
