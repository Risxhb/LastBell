# blender -b -P entrances.py -- <unity Models dir>
# Area 4 entrances (scenery; players stay on the round floor), modelled in Blender in each entrance's local frame
# (origin at the arena centre, outward = +Y = Unity +Z, up = Z):
#   South_Approach  six bevelled treads with brass nosings and sloped stringers, a 26 m flagstone causeway with a
#                   brass centre inlay, moulded low walls with brass trim, buttresses with small pinnacles, rock
#                   supports. Slots: 0 deck, 1 brass, 2 stone, 3 stone cap, 4 polished, 5 rock.
#   Gatehouse       tall wall block with brass trim and coping, two pinnacle spires, blind tracery on both long
#                   faces, folded swallowtail crimson banner on a brass rod. Origin at its own centre.
#                   Slots: 0 stone, 1 brass, 2 stone cap, 3 polished, 4 banner.
#   Pylon           smaller pier with brass trim, octagonal spire and a smaller banner. Same slots as the gatehouse.
#   North_Stair     three treads with nosings, a dark pedestal landing with an engraved brass ring, moulded side
#                   walls, rock support. Slots: 0 deck, 1 brass, 2 pedestal, 3 polished, 4 stone, 5 stone cap, 6 rock.
#   Bridge          (slot 7 banner) flagstone deck with inlay, moulded walls with posts, corbels, an under-arch, a ruined gateway with
#                   clustered columns and tracery fragments, a ragged broken end with falling rubble.
#                   Slots: 0 deck, 1 brass, 2 stone, 3 stone cap, 4 polished, 5 dark stone, 6 rock.
import bpy, bmesh, sys, os, math, random
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset(); rnd = random.Random(11)
DECK, BRASS, STONE, CAP, POL, ROCK, PED, DARK, BANNER = lb.slots(['Deck', 'Brass', 'Stone', 'StoneCap', 'Polished', 'Rock', 'Pedestal', 'StoneDark', 'Banner'])
RIM_OUT = 15.1

def B(name, size, loc, mat, rot=(0, 0, 0), bev=0.03):
    o = lb.box(name, size, loc, mat, rot)
    if bev: lb.bevel(o, bev, 2)
    return o

def small_post(x, y, z):
    return [B('post', (0.34, 0.34, 0.2), (x, y, z + 0.1), STONE, bev=0.015), B('post', (0.24, 0.24, 0.5), (x, y, z + 0.45), STONE, bev=0.015),
            B('post', (0.3, 0.3, 0.05), (x, y, z + 0.72), BRASS, bev=0), lb.cone('post_spire', 0.16, 0.55, 8, (x, y, z + 0.74), STONE, (0, 0, math.radians(22.5))),
            lb.sphere('post_orb', 0.045, (x, y, z + 1.32), POL, 8, 5)]

def support(r, depth, sides, loc):
    bm = bmesh.new(); rows = []
    for k in range(4):
        f = k / 3; rr = r * (1 - f)
        rows.append([bm.verts.new((loc[0] + math.cos(i * 2 * math.pi / sides + k * 0.3) * rr * (1 + rnd.uniform(-0.15, 0.15) * (k > 0)),
                                   loc[1] + math.sin(i * 2 * math.pi / sides + k * 0.3) * rr * (1 + rnd.uniform(-0.15, 0.15) * (k > 0)), loc[2] - depth * f)) for i in range(sides)])
    for k in range(3):
        for i in range(sides): bm.faces.new((rows[k][i], rows[k][(i + 1) % sides], rows[k + 1][(i + 1) % sides], rows[k + 1][i]))
    bm.faces.new(list(reversed(rows[0]))); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.02); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return lb._obj('support', bm, ROCK)

def banner(w, h, loc):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); cols, rows = 8, 3; g = []
    for yy in range(rows + 1):
        row = []
        for xx in range(cols + 1):
            fx, fy = xx / cols, yy / rows
            row.append(bm.verts.new((loc[0] + (fx - 0.5) * w, loc[1] + 0.07 * math.sin(fx * math.pi * 4) * (0.6 + 0.4 * (1 - fy)), loc[2] + (fy - 0.5) * h)))
        g.append(row)
    for yy in range(rows):
        for xx in range(cols):
            f = bm.faces.new((g[yy][xx], g[yy][xx + 1], g[yy + 1][xx + 1], g[yy + 1][xx]))
            for l, (a, b) in zip(f.loops, ((xx, yy), (xx + 1, yy), (xx + 1, yy + 1), (xx, yy + 1))): l[uv].uv = (1 - a / cols, b / rows)
    return lb._obj('banner', bm, BANNER)

def finish(parts, name, order, keep_uv_mat=None):
    o = lb.join(parts, name, order)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bm = bmesh.from_edit_mesh(o.data)
    keep = order.index(keep_uv_mat) if keep_uv_mat in order else -1
    for f in bm.faces: f.select = f.material_index != keep
    bmesh.update_edit_mesh(o.data); bpy.ops.uv.cube_project(cube_size=2.4); bpy.ops.object.mode_set(mode='OBJECT')
    return lb.finish(o)

# ---------------- south approach ----------------
p = []
for i in range(6):
    top = -0.2 * (i + 1); y = RIM_OUT - 0.3 + 0.5 * i
    p.append(B('tread', (4.0, 0.5, 3 + top), (0, y + 0.25, (top - 3) / 2), DECK)); p.append(B('nosing', (4.0, 0.08, 0.04), (0, y + 0.04, top + 0.01), BRASS, bev=0))
for x in (-2.12, 2.12): p.append(B('stringer', (0.28, 3.4, 0.7), (x, RIM_OUT + 1.2, -0.85), STONE, (math.radians(21.8), 0, 0)))
cy0, cl = RIM_OUT + 2.7, 26.0
for k in range(6):  # causeway in long slabs so joints read
    a = cy0 + k * cl / 6; p.append(B('causeway', (4.0, cl / 6 - 0.02, 1.0), (0, a + cl / 12, -1.7), DECK))
p.append(B('inlay', (0.08, cl, 0.02), (0, cy0 + cl / 2, -1.19), BRASS, bev=0))
for x in (-2.25, 2.25):
    p.append(B('wall', (0.45, cl + 3, 0.84), (x, cy0 + cl / 2 - 1.5, -0.78), STONE)); p.append(B('coping', (0.58, cl + 3, 0.1), (x, cy0 + cl / 2 - 1.5, -0.32), CAP, bev=0.02))
    p.append(B('trim', (0.05, cl + 3, 0.04), (x - math.copysign(0.27, x), cy0 + cl / 2 - 1.5, -0.42), BRASS, bev=0))
    for k in range(6):
        y = cy0 + 2 + k * 4.2; p.append(B('buttress', (0.6, 0.7, 1.8), (x * 1.18, y, -1.2), STONE)); p += small_post(x, y, -0.27)
for k in range(3): p.append(support(1.6, 14, 6, (0, cy0 + 6 + k * 8, -2.2)))
south = finish(p, 'South_Approach', [DECK, BRASS, STONE, CAP, POL, ROCK])

def spire_pinnacle(x, y, z, h=2.4):
    sh = 0.6; q = [B('pin', (0.9, 0.9, 0.35), (x, y, z + 0.175), STONE, bev=0.02), B('pin', (0.56, 0.56, sh), (x, y, z + 0.35 + sh / 2), STONE, bev=0.02),
                   B('pin', (0.64, 0.64, 0.1), (x, y, z + 0.4 + sh), BRASS, bev=0)]
    q.append(lb.cone('pin_spire', 0.34, 1.6, 8, (x, y, z + 0.45 + sh), STONE, (0, 0, math.radians(22.5)))); q.append(lb.sphere('pin_orb', 0.09, (x, y, z + 2.1 + sh), POL, 8, 5)); return q

# ---------------- gatehouse ----------------
g = [B('wall', (1.4, 3.4, 4.4), (0, 0, -0.4), STONE), B('trim', (1.6, 3.6, 0.12), (0, 0, 1.88), BRASS, bev=0), B('coping', (1.5, 3.5, 0.14), (0, 0, 2.0), CAP, bev=0.02)]
for yy in (-1.1, 1.1): g += spire_pinnacle(0, yy, 2.07)
for side in (-1, 1):
    for k in range(2):
        yy = -0.8 + k * 1.6; xx = side * 0.71
        for d in (-0.45, 0.45): g.append(B('col', (0.08, 0.09, 1.6), (xx, yy + d, -0.7), CAP, bev=0))
        g.append(lb.pointed_arch('blind', 0.9, 0.1, 0.09, 0.08, (xx, yy, 0), CAP, (0, 0, math.radians(90))))
g.append(banner(1.3, 4.0, (0, 1.76, -0.25))); g.append(B('rod', (1.45, 0.06, 0.06), (0, 1.8, 1.8), POL, bev=0))
gate = finish(g, 'Gatehouse', [STONE, BRASS, CAP, POL, BANNER], BANNER)

# ---------------- pylon ----------------
q = [B('pier', (1.0, 1.0, 3.6), (0, 0, -0.6), STONE), B('trim', (1.12, 1.12, 0.1), (0, 0, 1.22), BRASS, bev=0),
     B('plinth', (1.4, 1.4, 0.3), (0, 0, 0.15), CAP, bev=0.03), B('plinth2', (1.18, 1.18, 0.18), (0, 0, 0.39), CAP, bev=0.02),   # stepped plinth at deck level
     B('band', (1.06, 1.06, 0.06), (0, 0, 0.75), BRASS, bev=0), B('cap_mould', (1.25, 1.25, 0.12), (0, 0, 1.33), CAP, bev=0.02)]
q.append(lb.cone('spire', 0.45, 2.2, 8, (0, 0, 1.27), STONE, (0, 0, math.radians(22.5))))
q.append(banner(0.95, 3.2, (0, 0.55, -0.25))); q.append(B('rod', (1.05, 0.05, 0.05), (0, 0.58, 1.35), POL, bev=0))
pylon = finish(q, 'Pylon', [STONE, BRASS, CAP, POL, BANNER], BANNER)

# ---------------- north stair ----------------
n = []
for i in range(3):
    top = 0.2 * (i + 1); y = RIM_OUT - 0.3 + 0.3 * i
    n.append(B('tread', (3.0, 0.3, 2 + top), (0, y + 0.15, (top - 2) / 2), DECK)); n.append(B('nosing', (3.0, 0.08, 0.04), (0, y + 0.04, top + 0.01), BRASS, bev=0))
ly = RIM_OUT - 0.3 + 0.9 + 0.75
n += [B('landing', (4, 1.5, 3), (0, ly, 0.6 - 1.5), PED), B('landing_trim', (4.1, 1.6, 0.06), (0, ly, 0.6), BRASS, bev=0)]
n.append(lb.torus('landing_ring', 0.6, 0.03, (0, ly, 0.64), POL, seg=48, tube=6))
for x in (-1.65, 1.65): n += [B('side', (0.3, 1.6, 0.9), (x, RIM_OUT + 0.55, 0.45), STONE), B('side_cap', (0.38, 1.7, 0.08), (x, RIM_OUT + 0.55, 0.93), CAP, bev=0.02)]
n.append(support(2.6, 12, 6, (0, ly, -2.4)))
north = finish(n, 'North_Stair', [DECK, BRASS, PED, POL, STONE, CAP, ROCK])

# ---------------- bridge ----------------
b = []; by = RIM_OUT - 0.3 + 3.5
for k in range(5): b.append(B('deck', (3.5, 7 / 5 - 0.02, 0.6), (0, by - 3.5 + (k + 0.5) * 7 / 5, -0.32), DECK))
b.append(B('inlay', (0.06, 7, 0.02), (0, by, -0.005), BRASS, bev=0))
for x in (-1.9, 1.9):
    b += [B('side', (0.3, 7, 0.6), (x, by, 0.15), STONE), B('coping', (0.4, 7, 0.08), (x, by, 0.49), CAP, bev=0.02)]
    for k in range(4): b += small_post(x, by - 3 + k * 2, 0.53)
    for k in range(5): b += [B('corbel', (0.4, 0.4, 0.3), (x * 0.95, by - 3 + k * 1.5, -0.75), STONE), B('corbel', (0.28, 0.28, 0.25), (x * 0.92, by - 3 + k * 1.5, -1.0), STONE)]
b.append(lb.pointed_arch('under_arch', 3.4, -4.2, 0.4, 3.0, (0, by, 0), DARK, (0, 0, math.radians(90))))
for k in range(4): b.append(B('broken', (0.75, rnd.uniform(0.5, 1.4), 0.55), (-1.2 + k * 0.8, by + 3.8 + rnd.random() * 0.9, -0.32 - rnd.random() * 0.4), DECK, (rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2), rnd.uniform(-0.2, 0.2))))
for k in range(6): b.append(B('rubble', (rnd.uniform(0.3, 0.8),) * 3, (rnd.uniform(-1.6, 1.6), by + 4.5 + rnd.random() * 2.5, rnd.uniform(-4, -1)), DECK, (rnd.random() * 1.5, rnd.random() * 1.5, rnd.random() * 1.5)))
b.append(support(1.4, 10, 6, (0, by, -0.6)))
gy = by + 2.6
for x, h in ((-1.95, 4.5), (1.95, 3.2)):
    b.append(B('column', (0.7, 0.7, h), (x, gy, h / 2), STONE))
    for d in (-0.4, 0.4): b.append(B('shaft', (0.22, 0.22, h), (x, gy + d, h / 2), STONE, bev=0.015))
    b += [B('capital', (0.9, 0.9, 0.2), (x, gy, h + 0.1), CAP, bev=0.02), B('capital_band', (0.78, 0.78, 0.06), (x, gy, h - 0.05), BRASS, bev=0)]
    b += [B('plinth', (1.05, 1.05, 0.35), (x, gy, 0.18), CAP, bev=0.03), B('plinth2', (0.88, 0.88, 0.2), (x, gy, 0.45), CAP, bev=0.02),   # stepped base, mid brass band, cap moulding
          B('mid_band', (0.8, 0.8, 0.07), (x, gy, h * 0.55), BRASS, bev=0), B('cap_mould', (1.0, 1.0, 0.1), (x, gy, h + 0.25), CAP, bev=0.02)]
b.append(lb.pointed_arch('gate_arch', 3.9, 4.5, 0.45, 0.6, (0, gy, 0), STONE, keep=0.55))
b.append(lb.torus('rose_fragment', 0.7, 0.06, (0.6, gy, 5.2), CAP, (math.radians(90), 0, 0), 24, 6, arc=160, start=200))
b.append(B('mullion_fragment', (0.08, 0.1, 1.0), (0.9, gy, 4.7), CAP, (0, math.radians(8), 0), bev=0))
for x, h in ((-1.95, 4.5), (1.95, 3.2)):   # crimson banners on the arena face of the gateway columns carry the accent colour east and west
    b.append(banner(0.62, h * 0.48, (x, gy - 0.45, h * 0.62))); b.append(B('banner_rod', (0.72, 0.05, 0.05), (x, gy - 0.47, h * 0.62 + h * 0.24 + 0.03), POL, bev=0))
bridge = finish(b, 'Bridge', [DECK, BRASS, STONE, CAP, POL, DARK, ROCK, BANNER], BANNER)

for o, x in ((south, 0), (gate, 6), (pylon, 9), (north, -8), (bridge, -14)): o.location.x = x
lb.preview(os.path.join(here, 'renders', 'entrances.png'), (8, -14, 14), (-2, 18, -1), lens=28)
for o in (south, gate, pylon, north, bridge): o.location.x = 0
lb.export([south, gate, pylon, north, bridge], os.path.join(out, 'Entrances', 'Entrances.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'entrances.blend')); print('done')
