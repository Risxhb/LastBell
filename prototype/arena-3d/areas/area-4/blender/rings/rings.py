# blender -b -P rings.py -- <unity Models dir>
# Area 4 clock rings, modelled in Blender around the Z axis (Unity Y, the builder's spin axis):
#   Astrolabe   r 14 m, 290° of thick brass band (2.4 m) with a rolled polished edge, an inner bead, a dark engraved
#               channel, a 72-mark degree scale, ivory numeral plates with raised brass numerals, a numeral ring,
#               an eccentric rete (two rings and four curved arms with star pointers), four spokes, hub and pointer.
#   Ring_Right / Ring_Low   smaller banded rings with numerals and three thick spokes (no wire-hoop look).
# Angles follow Unity's polar convention through U(): Unity (x, y, z) = (-bx, bz, by), so theta maps to 180°-theta.
# Slots: 0 brass, 1 polished brass, 2 iron, 3 ivory.
import bpy, bmesh, sys, os, math
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset(); BRASS, POL, IRON, IVORY = lb.slots(['Brass', 'Polished', 'Iron', 'Ivory'])
def U(theta): return math.radians(180 - theta)
def P(theta, r, z=0.0): a = U(theta); return (math.cos(a) * r, math.sin(a) * r, z)

def band(name, r0, r1, z0, z1, t0, t1, mat, step=1.5):
    bm = bmesh.new(); n = max(1, int(abs(t1 - t0) / step)); rows = []
    for i in range(n + 1):
        a = U(t0 + (t1 - t0) * i / n); c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((c * r, s * r, z)) for r, z in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    for i in range(n):
        for k in range(4): bm.faces.new((rows[i][k], rows[i + 1][k], rows[i + 1][(k + 1) % 4], rows[i][(k + 1) % 4]))
    if abs(t1 - t0) < 360: bm.faces.new(rows[0]); bm.faces.new(list(reversed(rows[-1])))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return lb._obj(name, bm, mat)

def radial_box(name, theta, r, z, size, mat):  # size: (tangent, radial, height)
    return lb.box(name, (size[0], size[1], size[2]), P(theta, r, z), mat, (0, 0, U(theta) - math.pi / 2))

def clock_ring(name, rad, w, arc, start, spokes, astro=False):
    t0, t1 = start, start + arc; parts = []
    # stepped profile, deepest at the rim: polished outer lip, main band, recessed dark (oxidised) inner step
    d = 0.42 if astro else 0.38
    parts.append(band('lip', rad - 0.32, rad, -d, d, t0, t1, POL))
    parts.append(band('band', rad - w + 0.2, rad - 0.32, -d * 0.72, d * 0.72, t0, t1, BRASS))
    parts.append(band('inner_step', rad - w - 0.1, rad - w + 0.2, -d * 0.85, d * 0.85, t0, t1, IRON))
    parts.append(lb.torus('edge', rad, 0.16, (0, 0, 0), POL, seg=160, tube=8, arc=arc, start=180 - t1))
    parts.append(lb.torus('bead', rad - w - 0.25, 0.07, (0, 0, 0), BRASS, seg=160, tube=6, arc=arc, start=180 - t1))
    parts.append(band('channel', rad - w * 0.64, rad - w * 0.56, d * 0.72, d * 0.72 + 0.04, t0, t1, IRON))
    for i in range(72):
        th = start + i * 5
        if th > t1 - 1: break
        parts.append(radial_box('degree', th, rad - 0.5, d * 0.72 + 0.03, (0.05, 0.3 if i % 6 == 0 else 0.16, 0.05), POL))
    for i in range(12):
        th = start + 15 + i * 30
        if th > t1 - 5: break
        parts.append(radial_box('plate', th, rad - w / 2 - 0.05, 0.0, (w * (0.5 if i % 3 == 0 else 0.3), w * 0.45, d * 1.6), IVORY))
        parts.append(radial_box('numeral', th, rad - w / 2 - 0.05, d * 0.8 + 0.02, (w * 0.16, w * 0.45, 0.06), POL))
        if i % 3 == 0: parts.append(radial_box('numeral', th + 1.6, rad - w / 2 - 0.05, d * 0.8 + 0.02, (w * 0.16, w * 0.45, 0.06), POL))
    for i in range(spokes):
        th = start + (i + 0.5) * arc / spokes; parts += [radial_box('spoke', th, (rad - w) / 2, 0, (0.7, rad - w, 0.5), BRASS), radial_box('spoke_rib', th, (rad - w) / 2, 0.27, (0.22, rad - w, 0.08), POL)]
    if not astro:   # jagged broken end: the band snaps off in three stepped, offset shards
        for k in range(3):
            th = t1 + 2.5 + k * 3.2
            parts.append(radial_box('shard', th, rad - w / 2 + (k - 1) * 0.25, (k - 1) * 0.3, (0.9 - 0.2 * k, w * (0.85 - 0.2 * k), d * (1.6 - 0.3 * k)), BRASS))
        parts.append(lb.cyl('hub', 0.7, 0.6, 16, (0, 0, -0.3), POL))
    if astro:
        parts.append(lb.torus('numeral_ring', 9.6, 0.22, (0, 0, 0), BRASS, seg=192, tube=6))
        parts.append(lb.torus('rete_a', 6.2, 0.22, (-1.8, 0.6, 0.05), POL, seg=128, tube=6))
        parts.append(lb.torus('rete_b', 4.0, 0.18, (1.4, -0.9, 0.08), BRASS, seg=96, tube=6))
        for i in range(4):   # curved rete arms tapering in three steps to a star-pointer tip
            c = P(i * 90 + 200, 2.6, 0.1)
            for k, tube in enumerate((0.26, 0.18, 0.11)):
                parts.append(lb.torus('rete_arm', 5.2, tube, c, POL, seg=16, tube=6, arc=25.5, start=i * 90 + k * 25))
            e = math.radians(i * 90 + 75); tip = (c[0] + math.cos(e) * 5.2, c[1] + math.sin(e) * 5.2, c[2])
            parts.append(lb.cone('rete_tip', 0.22, 0.8, 4, tip, POL, (math.radians(-90), 0, e)))
        for i in range(4): parts.append(radial_box('spoke', i * 90 + 20, 5.3, 0, (0.5, 8.0, 0.45), BRASS))
        for i in range(9):
            th, rr = i * 40 + 13, 3.5 + (i % 3) * 2.2
            parts.append(lb.cone('star', 0.18, 0.9, 3, P(th, rr, 0.15), POL, (math.radians(90), 0, U(th))))
        # stepped hub: dark iron drum, polished collar, engraved ring and a domed boss with eight ribs
        parts += [lb.cyl('hub', 1.5, 0.7, 32, (0, 0, -0.35), IRON), lb.torus('hub_collar', 1.5, 0.12, (0, 0, 0.35), POL, seg=48, tube=6),
                  lb.torus('hub_ring', 1.0, 0.06, (0, 0, 0.36), BRASS, seg=48, tube=6), lb.cyl('hub_boss', 0.6, 0.35, 24, (0, 0, 0.35), POL, r_top=0.3)]
        for i in range(8): parts.append(radial_box('hub_rib', i * 45, 1.05, 0.38, (0.1, 0.8, 0.08), POL))
        parts.append(radial_box('pointer', 60, 4.5, 0.3, (0.3, 9.2, 0.12), IRON))
    o = lb.join(parts, name, [BRASS, POL, IRON, IVORY]); lb.cube_uv(o); lb.finish(o); return o

astro = clock_ring('Astrolabe', 14, 3.2, 290, 110, 0, True)   # wide band so the scale reads from the game camera
right = clock_ring('Ring_Right', 10, 1.4, 290, 200, 3)
low = clock_ring('Ring_Low', 8, 1.2, 300, 30, 3)
frag = []
for i in range(4):
    frag.append(lb.box('fragment_%d' % i, (0.7 + 0.1 * i, 1.2 - 0.1 * i, 0.24), (0, 0, 0), BRASS, (0.3 * i, 0.5, 0.2 * i)))
fr = lb.join(frag, 'Astro_Fragment', [BRASS]); lb.cube_uv(fr); lb.finish(fr)

right.location = (26, 0, 0); low.location = (-22, 0, 0)
lb.preview(os.path.join(here, 'renders', 'rings.png'), (0, -10, 34), (0, 2, 0), lens=30)
right.location = low.location = (0, 0, 0)
lb.export([astro, right, low, fr], os.path.join(out, 'Rings', 'Rings.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'rings.blend')); print('done')
