# blender -b -P station.py -- <unity Models dir>
# Area 4 rim station (dormant decoration), modelled in Blender, centred on its own origin, outward = +Y (Unity +Z):
# flush engraved dial disc (UV 0..1 for the StationDial texture), two bevelled brass rings, stepped dark plinth,
# domed polished boss with a finial, a bent half-ring of parapet blocks with its cap moulding around the outer half,
# four small gothic posts on the cap and a pointed-arch gate with mullion and rose tracery behind.
# Slots: 0 stone, 1 stone cap, 2 brass, 3 polished brass, 4 dial, 5 pedestal.
import bpy, bmesh, sys, os, math
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset(); RIM_H, CAP_Y = 0.9, 1.0
STONE, CAP, BRASS, POL, DIAL, PED = lb.slots(['Stone', 'StoneCap', 'Brass', 'Polished', 'Dial', 'Pedestal'])
parts = []

# dial disc with planar 0..1 UVs
bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); n = 96; R = 1.9
c = bm.verts.new((0, 0, 0.02)); ring = [bm.verts.new((math.cos(i / n * 2 * math.pi) * R, math.sin(i / n * 2 * math.pi) * R, 0.02)) for i in range(n)]
for i in range(n):
    f = bm.faces.new((c, ring[i], ring[(i + 1) % n]))
    for l in f.loops: l[uv].uv = (l.vert.co.x / (2 * R) + 0.5, l.vert.co.y / (2 * R) + 0.5)
parts.append(lb._obj('dial', bm, DIAL))
parts.append(lb.torus('ring_outer', 1.92, 0.07, (0, 0, 0.02), BRASS, seg=96, tube=10))
parts.append(lb.torus('ring_inner', 1.6, 0.035, (0, 0, 0.025), POL, seg=96, tube=8))
parts.append(lb.cyl('plinth', 1.95, 0.46, 48, (0, 0, -0.47), PED)); parts.append(lb.cyl('plinth_step', 2.25, 0.36, 48, (0, 0, -0.8), PED))
# raised boss on two stepped brass collars, so it reads from the game camera
parts += [lb.cyl('collar_low', 0.72, 0.1, 32, (0, 0, 0.02), BRASS), lb.cyl('collar_high', 0.56, 0.12, 32, (0, 0, 0.12), POL),
          lb.sphere('boss', 0.46, (0, 0, 0.24), POL, 32, 16, (1, 1, 0.62)), lb.cone('finial', 0.08, 0.32, 8, (0, 0, 0.5), POL)]
# 24 raised brass hour ticks inlaid in the dial, long at the quarters, plus four cardinal plates
for i in range(24):
    a = i / 24 * 2 * math.pi; L = 0.42 if i % 6 == 0 else (0.26 if i % 2 == 0 else 0.16); rr = 1.52 - L / 2
    parts.append(lb.box('tick', (0.05 if i % 6 else 0.09, L, 0.03), (math.sin(a) * rr, math.cos(a) * rr, 0.035), BRASS, (0, 0, -a)))
for i in range(4):
    a = i * math.pi / 2 + math.pi / 4; parts.append(lb.box('glyph_plate', (0.22, 0.12, 0.03), (math.sin(a) * 1.05, math.cos(a) * 1.05, 0.035), POL, (0, 0, -a)))
for i in range(12):   # hour numerals: one to three raised bars at each hour, read as I / II / III from the game camera
    a = i / 12 * 2 * math.pi; n = i % 3 + 1
    for k in range(n):
        off = (k - (n - 1) / 2) * 0.07; rr = 0.86
        parts.append(lb.box('numeral', (0.035, 0.16, 0.03), (math.sin(a) * rr + math.cos(a) * off, math.cos(a) * rr - math.sin(a) * off, 0.035), POL, (0, 0, -a)))

# bent half-ring of blocks and cap around the outer half (±100° about +Y)
rm = 2.225; Lb = rm * math.radians(200); bulge = []
for course, (z0, z1, off) in enumerate(((-0.45, 0.19, 0.0), (0.19, RIM_H - 0.08, 0.35))):
    x = -Lb / 2; cuts = [x]
    while x < Lb / 2 - 0.5: x += 0.7 if not cuts[1:] and off else 0.7; cuts.append(min(x + (off if len(cuts) == 1 else 0), Lb / 2))
    cuts[-1] = Lb / 2
    for a, b in zip(cuts[:-1], cuts[1:]):
        if b - a < 0.1: continue
        o = lb.box('bulge_block', (b - a - 0.012, 0.55, z1 - z0 - 0.012), ((a + b) / 2, 0, (z0 + z1) / 2), STONE); lb.bevel(o, 0.025, 2); bulge.append(o)
cap2d = [(r - rm, z) for r, z in ((1.93, RIM_H - 0.08), (1.9, RIM_H + 0.02), (2.0, CAP_Y + 0.02), (2.45, CAP_Y + 0.02), (2.6, RIM_H - 0.01), (2.6, RIM_H - 0.08))]
for k in range(12): bulge.append(lb.prism('bulge_cap', cap2d, Lb / 12 - 0.01, (-Lb / 2 + (k + 0.5) * Lb / 12, 0, 0), CAP, (0, 0, math.radians(90))))
bj = lb.join(bulge, 'bulge', [STONE, CAP]); lb.cube_uv(bj); lb.arc_bend(bj, rm); parts.append(bj)

def post(a_deg):
    a = math.radians(a_deg); px, py = math.sin(a) * 2.25, math.cos(a) * 2.25; ps = []
    for size, z, mat in (((0.34, 0.34, 0.2), CAP_Y + 0.1, STONE), ((0.24, 0.24, 0.5), CAP_Y + 0.45, STONE), ((0.3, 0.3, 0.05), CAP_Y + 0.72, BRASS)):
        o = lb.box('post', size, (px, py, z), mat, (0, 0, -a)); lb.bevel(o, 0.015, 1); ps.append(o)
    ps.append(lb.cone('post_spire', 0.16, 0.55, 8, (px, py, CAP_Y + 0.74), STONE, (0, 0, math.radians(22.5))))
    ps.append(lb.sphere('post_orb', 0.045, (px, py, CAP_Y + 1.32), POL, 8, 5)); return ps
for a in (-60, -20, 20, 60): parts += post(a)

# gate frame behind the station
gy = 2.35
for x in (-0.95, 0.95):
    o = lb.box('gate_post', (0.36, 0.36, 1.8), (x, gy, CAP_Y + 0.9), STONE); lb.bevel(o, 0.02, 1); parts.append(o)
    parts.append(lb.cone('gate_fin', 0.2, 0.6, 8, (x, gy, CAP_Y + 1.8), STONE, (0, 0, math.radians(22.5))))
parts.append(lb.pointed_arch('gate_arch', 1.9, CAP_Y + 1.8, 0.26, 0.34, (0, gy, 0), BRASS))
parts.append(lb.box('gate_mullion', (0.1, 0.12, 1.98), (0, gy, CAP_Y + 0.99), BRASS))
parts.append(lb.torus('gate_rose', 0.34, 0.06, (0, gy, CAP_Y + 1.8 + 0.8), BRASS, (math.radians(90), 0, 0), 32, 6))
parts.append(lb.sphere('gate_orb', 0.09, (0, gy, CAP_Y + 1.8 + 1.65 + 0.1), BRASS, 10, 6))

st = lb.join(parts, 'Rim_Station', [STONE, CAP, BRASS, POL, DIAL, PED])
# trim UVs on everything except the dial (its planar UVs stay)
bpy.ops.object.select_all(action='DESELECT'); st.select_set(True); bpy.context.view_layer.objects.active = st
bpy.ops.object.mode_set(mode='EDIT'); bm = bmesh.from_edit_mesh(st.data)
for f in bm.faces: f.select = f.material_index != 4
bmesh.update_edit_mesh(st.data); bpy.ops.uv.cube_project(cube_size=2.4); bpy.ops.object.mode_set(mode='OBJECT')
lb.finish(st); bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))   # smooth dome and rings, crisp block edges
lb.preview(os.path.join(here, 'renders', 'station.png'), (3.2, -5.0, 3.4), (0, 0.6, 0.5))
lb.export([st], os.path.join(out, 'Station', 'Station.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'station.blend')); print('done')
