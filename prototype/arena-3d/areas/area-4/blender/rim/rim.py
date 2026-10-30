# blender -b -P rim.py -- <unity Models dir>
# Area 4 rim, modelled in Blender:
#   Parapet_Module  7.5° of parapet (R 13.5 → 15.1 m), pivot at the arena centre, outward = +Y (Unity +Z):
#                   two courses of chamfered stone blocks with staggered joints, a gothic cap moulding (inner bead,
#                   rounded top, outer drip), a brass bead on the inner lip and a brass band 0.3 m lower, and one
#                   blind pointed arch with colonnettes and a dark recess on the outer face.
#                   Slots: 0 stone, 1 stone cap, 2 brass, 3 dark stone.
#   Drum_Bay        15° of the drum below the rim (pivot at the centre, spans 0°→15°): block wall courses, a
#                   stepped buttress with set-offs and a sloped cap, a tracery window (jambs, pointed head,
#                   mullion, rose, sill), the brass band with two numeral studs.
#                   Slots: 0 stone, 1 stone cap, 2 brass, 3 polished brass, 4 window (glow variant set in Unity).
#   Rock_Spire      faceted inverted spire under the platform (top at z 0), strata-mapped. Slot: 0 rock.
import bpy, sys, os, math, random
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset()
R, RIM_OUT, RIM_H, CAP_Y = 13.5, 15.1, 0.9, 1.0
rnd = random.Random(7)

# ---------------- parapet module ----------------
STONE, CAP, BRASS, DARK = lb.slots(['Stone', 'StoneCap', 'Brass', 'StoneDark'])
rm = (R + RIM_OUT) / 2; L = rm * math.radians(7.5); x0, x1 = -L / 2, L / 2
yi, yo = R - rm, RIM_OUT - rm
parts = []
def blocks(cuts, z0, z1, y0, y1, mat, w=0.03):
    for a, b in zip(cuts[:-1], cuts[1:]):
        o = lb.box('block', (b - a - 0.012, y1 - y0, z1 - z0 - 0.012), ((a + b) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat); lb.bevel(o, w, 2); parts.append(o)
blocks([x0, -0.22, 0.48, x1], -0.45, 0.19, yi, yo, STONE)
blocks([x0, 0.12, x1], 0.19, RIM_H - 0.08, yi, yo, STONE)
cap_prof = [(R - 0.01, RIM_H - 0.08), (R - 0.05, RIM_H - 0.04), (R - 0.05, RIM_H + 0.02), (R + 0.02, RIM_H + 0.08), (R + 0.25, CAP_Y + 0.02),
            (RIM_OUT - 0.25, CAP_Y + 0.02), (RIM_OUT, RIM_H + 0.06), (RIM_OUT + 0.14, RIM_H - 0.01), (RIM_OUT + 0.14, RIM_H - 0.07), (RIM_OUT + 0.04, RIM_H - 0.09)]
cap2d = [(r - rm, z) for r, z in cap_prof]
for k in range(3):  # cap in three stones so it bends smoothly and shows joints
    a = x0 + k * L / 3; parts.append(lb.prism('cap', cap2d, L / 3 - 0.01, (a + L / 6, 0, 0), CAP, (0, 0, math.radians(90))))
for k in range(3):
    a = x0 + k * L / 3
    parts.append(lb.box('bead', (L / 3, 0.06, 0.06), (a + L / 6, yi - 0.045, RIM_H), BRASS))
    parts.append(lb.box('band', (L / 3, 0.05, 0.07), (a + L / 6, yi - 0.01, 0.58), BRASS))
# blind arch on the outer face
for x in (-0.3, 0.3): parts.append(lb.box('colonnette', (0.08, 0.07, 0.52), (x, yo + 0.02, -0.32), CAP))
parts.append(lb.pointed_arch('blind_arch', 0.6, -0.06, 0.07, 0.07, (0, yo + 0.02, 0), CAP))
parts.append(lb.box('recess', (0.52, 0.02, 0.62), (0, yo + 0.005, -0.15), DARK))
par = lb.join(parts, 'Parapet_Module', [STONE, CAP, BRASS, DARK]); lb.cube_uv(par); lb.arc_bend(par, rm); lb.finish(par)

# ---------------- drum bay ----------------
WIN = lb.slots(['Window'])[0]; POL = lb.slots(['Polished'])[0]
parts = []
rd = RIM_OUT - 0.2; Ld = rd * math.radians(15)
for c in range(6):  # six courses of wall blocks, joints staggered course to course
    z0 = -4.4 + c * 0.66; z1 = z0 + 0.66; off = 0.6 if c % 2 else 1.3
    cuts = [0.0] + [x for x in (off, off + 1.35, off + 2.7) if x < Ld - 0.3] + [Ld]
    for a, b in zip(cuts[:-1], cuts[1:]):
        o = lb.box('wall', (b - a - 0.012, 0.4, 0.648), ((a + b) / 2, 0.0, (z0 + z1) / 2), STONE); lb.bevel(o, 0.03, 2); parts.append(o)
# stepped buttress on the bay's leading edge
yb = RIM_OUT + 0.3 - rd
for size, loc, mat, rot in (((0.9, 0.6, 3.4), (0, yb, -2.1), STONE, 0), ((0.8, 0.9, 1.2), (0, yb + 0.18, -3.4), STONE, 0), ((0.7, 1.1, 0.5), (0, yb + 0.3, -4.15), STONE, 0),
                            ((0.84, 0.62, 0.12), (0, yb + 0.42, -2.75), CAP, -35), ((0.95, 0.5, 0.12), (0, yb + 0.12, -0.42), CAP, -30)):
    o = lb.box('buttress', size, loc, mat, (math.radians(rot), 0, 0)); lb.bevel(o, 0.03, 2); parts.append(o)
# tracery window in the middle of the bay
xw, yw = Ld / 2, RIM_OUT + 0.04 - rd
parts.append(lb.box('recess', (1.0, 0.08, 1.9), (xw, yw - 0.02, -2.45), WIN))
for dx in (-0.52, 0.52): o = lb.box('jamb', (0.12, 0.14, 1.5), (xw + dx, yw + 0.03, -2.55), CAP); lb.bevel(o, 0.02, 1); parts.append(o)
parts.append(lb.pointed_arch('window_head', 1.04, -1.8, 0.12, 0.14, (xw, yw + 0.03, 0), CAP))
parts.append(lb.box('mullion', (0.06, 0.07, 1.0), (xw, yw + 0.05, -2.3), BRASS))
parts.append(lb.torus('rose', 0.18, 0.025, (xw, yw + 0.05, -1.38), BRASS, (math.radians(90), 0, 0), 24, 6))
o = lb.box('sill', (1.2, 0.2, 0.1), (xw, yw + 0.08, -3.32), CAP); lb.bevel(o, 0.02, 1); parts.append(o)
# brass band with numeral studs
for k in range(3): parts.append(lb.box('band', (Ld / 3, 0.18, 0.18), (Ld / 6 + k * Ld / 3, RIM_OUT + 0.04 - rd, -0.52), BRASS))
for x in (Ld * 0.25, Ld * 0.75): parts.append(lb.box('stud', (0.12, 0.08, 0.22), (x, RIM_OUT + 0.14 - rd, -0.52), POL))
drum = lb.join(parts, 'Drum_Bay', [STONE, CAP, BRASS, POL, WIN]); lb.cube_uv(drum); lb.arc_bend(drum, rd); lb.finish(drum)

# ---------------- rock spire ----------------
ROCK = lb.slots(['Rock'])[0]
import bmesh
from mathutils import Vector
bm = bmesh.new(); sides, rings, top_r, depth = 18, 6, RIM_OUT - 0.2, 18.0; grid = []
for k in range(rings + 1):
    f = k / rings; rr = top_r * (1 - f) * (1 - f * 0.15); row = []
    for i in range(sides):
        a = i * 2 * math.pi / sides + (k % 2) * 0.17; j = 1 if k == 0 else 1 + rnd.uniform(-0.16, 0.16)
        row.append(bm.verts.new((math.cos(a) * rr * j, math.sin(a) * rr * j, -depth * f + (rnd.uniform(-0.6, 0.6) if 0 < k < rings else 0))))
    grid.append(row)
for k in range(rings):
    for i in range(sides): bm.faces.new((grid[k][i], grid[k][(i + 1) % sides], grid[k + 1][(i + 1) % sides], grid[k + 1][i]))
bm.faces.new(list(reversed(grid[0])))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.05); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
spire = lb._obj('Rock_Spire', bm, ROCK); lb.cube_uv(spire, 4.0); lb.finish(spire)

# ---------------- previews, export, save ----------------
par.location = (0, -rm, 1.5); drum.location = (-2, -rd, 5); spire.hide_render = True
lb.preview(os.path.join(here, 'renders', 'parapet_drum.png'), (6.5, -8.5, 3.5), (0.5, 0.5, 1.0))
par.location = drum.location = (0, 0, 0); spire.hide_render = False
lb.export([par, drum, spire], os.path.join(out, 'Rim', 'Rim.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'rim.blend')); print('done')
