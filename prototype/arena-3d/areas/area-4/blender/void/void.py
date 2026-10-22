# blender -b -P void.py -- <unity Models dir>
# Area 4 void props, modelled in Blender:
#   Arcade_2 / Arcade_3 / Arcade_4  floating gothic ruins: clustered columns (core + four shafts), stepped bases,
#                   brass-banded capitals, pointed arches with an inner order and spandrels, a moulded entablature
#                   with string course and cornice, broken ends (3- and 4-arch variants) and a rock underside.
#                   Arches face ±Y (Unity ±Z). Slots: 0 dark stone, 1 brass, 2 stone cap.
#   Rock_0..5       faceted void rocks: subdivided, jittered, squashed, each with its own silhouette. Slot: 0 rock.
#   SpireChunk_0..3 inverted broken spires with a squared block on top. Slots: 0 rock, 1 dark stone.
#   Chain_Link / Chain_Link_Low  oval forged link (long axis +Y = Unity +Z, length 1.04 m before builder scaling).
#   Hanging_Bell    small cousin of the hero bell (same profile at 0.32 scale, painted-bronze UVs), crown, lip band,
#                   clapper. Slots: 0 painted bronze, 1 brass, 2 bronze, 3 iron.
#   Lantern         iron post and frame, four glass panes, flame, roof with spire, ring handle.
#                   Slots: 0 iron, 1 glass, 2 flame.
import bpy, bmesh, sys, os, math, random
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset(); rnd = random.Random(23)
DARK, BRASS, CAP, ROCK, IRON, GLASS, FLAME, PAINT, BRONZE = lb.slots(['StoneDark', 'Brass', 'StoneCap', 'Rock', 'Iron', 'Glass', 'Flame', 'BellPaint', 'Bronze'])

def B(name, size, loc, mat, rot=(0, 0, 0), bev=0.03):
    o = lb.box(name, size, loc, mat, rot)
    if bev: lb.bevel(o, bev, 2)
    return o

def arcade(arches, variant):
    span, col_h = 3.4, 7.0; w = arches * span; p = []
    for i in range(arches + 1):
        x = -w / 2 + i * span; broken = variant > 0 and i == arches; h = col_h * 0.5 if broken else col_h
        p.append(B('core', (0.9, 0.9, h), (x, 0, h / 2), DARK))
        for sx, sy in ((0.55, 0), (-0.55, 0), (0, 0.55), (0, -0.55)): p.append(B('shaft', (0.29, 0.29, h), (x + sx, sy, h / 2), DARK, bev=0.015))
        p += [B('base', (1.35, 1.35, 0.36), (x, 0, 0.18), CAP), B('base2', (1.12, 1.12, 0.24), (x, 0, 0.48), CAP)]
        if not broken: p += [B('cap_band', (1.12, 1.12, 0.1), (x, 0, col_h - 0.08), BRASS, bev=0), B('capital', (1.3, 1.3, 0.3), (x, 0, col_h + 0.15), CAP)]
        else:
            for k in range(3): p.append(B('broken_top', (rnd.uniform(0.4, 0.8), rnd.uniform(0.4, 0.8), 0.4), (x + rnd.uniform(-0.4, 0.4), rnd.uniform(-0.3, 0.3), h + 0.2 + k * 0.15), DARK, (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), rnd.uniform(0, 1.5))))
        skip = broken or (variant == 2 and i == arches - 1)
        if i < arches and not skip:
            cx = x + span / 2
            p.append(lb.pointed_arch('arch', span - 0.9, col_h + 0.3, 0.5, 0.9, (cx, 0, 0), DARK))
            p.append(lb.pointed_arch('inner_order', span - 1.4, col_h + 0.3, 0.16, 1.0, (cx, 0.0, 0), CAP))
            p.append(B('spandrel', (span * 0.55, 0.85, 0.6), (cx, 0, col_h + 0.3 + (span - 0.9) * 0.87 + 0.2), DARK))
    eh = col_h + 0.3 + (span - 0.9) * 0.87 + 0.75; ew = w * 0.6 if variant == 2 else w + 0.8; ex = -w * 0.2 if variant == 2 else 0
    p += [B('entablature', (ew, 1.05, 0.6), (ex, 0, eh), DARK), B('string_course', (ew + 0.05, 1.1, 0.08), (ex, 0, eh - 0.34), BRASS, bev=0),
          B('cornice', (ew + 0.3, 1.3, 0.22), (ex, 0, eh + 0.4), CAP), B('foundation', (w + 1.4, 1.8, 1.2), (0, 0, -0.6), DARK)]
    p.append(lb.cone('underside', 1.4, 5.0, 6, (0, 0, -1.2), DARK, (math.pi, 0, 0)))
    o = lb.join(p, 'Arcade_%d' % arches, [DARK, BRASS, CAP]); lb.cube_uv(o); return lb.finish(o)

arcades = [arcade(2, 0), arcade(3, 1), arcade(4, 2)]

# rocks: convex hull of ~25 points, so 20-40 planar facets; a flat, slightly tilted top and a tapered underside
rocks = []
for i in range(6):
    rr = random.Random(100 + i); sx, sy = 1 + 0.2 * (i % 3), 0.8 + 0.15 * (i % 2); bm = bmesh.new(); top = 0.35 + 0.1 * (i % 2); tilt = rr.uniform(-0.12, 0.12)
    for k in range(11 + i % 3):   # top rim
        a = k / (11 + i % 3) * 2 * math.pi + rr.uniform(-0.25, 0.25); r = rr.uniform(0.8, 1.0)
        x, y = math.cos(a) * r * sx, math.sin(a) * r * sy; bm.verts.new((x, y, top + tilt * x + rr.uniform(-0.04, 0.04)))
    for k in range(8 + i % 3):   # waist
        a = rr.uniform(0, 2 * math.pi); r = rr.uniform(0.85, 1.05); bm.verts.new((math.cos(a) * r * sx, math.sin(a) * r * sy, rr.uniform(-0.15, 0.15)))
    for k in range(4):           # tapered keel
        bm.verts.new((rr.uniform(-0.3, 0.3) * sx, rr.uniform(-0.25, 0.25) * sy, -0.7 - 0.35 * k - 0.2 * (i % 3)))
    bmesh.ops.convex_hull(bm, input=bm.verts); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4), verts=bm.verts, edges=bm.edges)
    o = lb._obj('Rock_%d' % i, bm, ROCK); lb.bevel(o, 0.04, 1, 60); lb.cube_uv(o, 4.0); rocks.append(lb.finish(o))

chunks = []
for i in range(4):
    r, d, sides = 1.2 + 0.45 * i, 4 + 2 * i, 6 + i
    bm = bmesh.new(); rows = []
    for k in range(5):
        f = k / 4; rr = r * (1 - f) * (1 - f * 0.15)
        rows.append([bm.verts.new((math.cos(a) * rr * (1 + rnd.uniform(-0.25, 0.25) * (k > 0)), math.sin(a) * rr * (1 + rnd.uniform(-0.25, 0.25) * (k > 0)), -d * f + (rnd.uniform(-0.5, 0.5) if 0 < k < 4 else 0)))
                     for a in (j * 2 * math.pi / sides + k * 0.2 for j in range(sides))])
    for k in range(4):
        for j in range(sides): bm.faces.new((rows[k][j], rows[k][(j + 1) % sides], rows[k + 1][(j + 1) % sides], rows[k + 1][j]))
    bm.faces.new(list(reversed(rows[0]))); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.02); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    sp = lb._obj('chunk', bm, ROCK)
    # broken top: an asymmetric hull of dressed stone, tilted, instead of a plain block
    hb = bmesh.new(); hr = random.Random(40 + i)
    for k in range(12): a = hr.uniform(0, 2 * math.pi); rr2 = r * hr.uniform(0.45, 0.85); hb.verts.new((math.cos(a) * rr2, math.sin(a) * rr2 * 0.85, hr.uniform(-0.05, 1) * r * (0.35 + 0.4 * (math.cos(a) > 0))))
    bmesh.ops.convex_hull(hb, input=hb.verts); bmesh.ops.recalc_face_normals(hb, faces=hb.faces); blk = lb._obj('block', hb, DARK); lb.bevel(blk, 0.04, 1, 50)
    # one or two stepped ledges of dressed stone partway down, so the spire isn't a plain inverted pyramid
    ledges = [lb.cyl('ledge', r * 0.85 * (1 - 0.25 * k), 0.5, 7, (rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), -d * (0.22 + 0.25 * k) - 0.25), ROCK, (rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), 0.3 * k + 0.2 * i), r_top=r * 0.95 * (1 - 0.25 * k))
              for k in range(1 + i % 2)]
    o = lb.join([sp, blk] + ledges, 'SpireChunk_%d' % i, [ROCK, DARK]); lb.cube_uv(o, 4.0); chunks.append(lb.finish(o))

link = lb.torus('Chain_Link', 0.26, 0.075, (0, 0, 0), IRON, seg=20, tube=8, scale=(1, 1.55, 1)); lb.cube_uv(link); lb.finish(link, flat=False)
link_low = lb.torus('Chain_Link_Low', 0.26, 0.075, (0, 0, 0), IRON, seg=10, tube=5, scale=(1, 1.55, 1)); lb.cube_uv(link_low); lb.finish(link_low, flat=False)

# hanging bell: same densified profile as the hero bell at 0.32 scale, no break
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
hb = lb.lathe('hb_body', densify(bell_profile(0.32), 2), 28, PAINT); lb.finish(hb, flat=False)
hparts = [hb, lb.torus('hb_band', 2.2, 0.07, (0, 0, 0.05), BRASS, seg=48, tube=6), lb.torus('hb_crown', 0.4, 0.12, (0, 0, 3.6), BRONZE, (math.radians(90), 0, 0), 16, 6),
          lb.sphere('hb_clapper', 0.35, (0, 0, 0.3), IRON, 10, 6)]
hang = lb.join(hparts, 'Hanging_Bell', [PAINT, BRASS, BRONZE, IRON])
bpy.ops.object.select_all(action='DESELECT'); hang.select_set(True); bpy.context.view_layer.objects.active = hang
bpy.ops.object.mode_set(mode='EDIT'); bm = bmesh.from_edit_mesh(hang.data)
for f in bm.faces: f.select = f.material_index != 0
bmesh.update_edit_mesh(hang.data); bpy.ops.uv.cube_project(cube_size=2.4); bpy.ops.object.mode_set(mode='OBJECT'); lb.finish(hang, flat=False)

lp = [B('post', (0.07, 0.07, 0.44), (0, 0, 0.22), IRON, bev=0.01), B('base', (0.3, 0.3, 0.04), (0, 0, 0.45), IRON, bev=0.01)]
for i in range(4):
    a = math.radians(i * 90 + 45); lp.append(B('frame', (0.035, 0.035, 0.38), (math.cos(a) * 0.13, math.sin(a) * 0.13, 0.65), IRON, bev=0))
    b = math.radians(i * 90); lp.append(B('pane', (0.17, 0.015, 0.32), (math.sin(b) * 0.1, math.cos(b) * 0.1, 0.65), GLASS, (0, 0, -b), bev=0))
lp += [B('flame', (0.11, 0.11, 0.26), (0, 0, 0.63), FLAME, bev=0), lb.cone('roof', 0.23, 0.2, 4, (0, 0, 0.85), IRON, (0, 0, math.radians(45))),
       lb.cone('roof_spire', 0.05, 0.18, 4, (0, 0, 1.03), IRON), lb.torus('handle', 0.06, 0.012, (0, 0, 1.24), IRON, (math.radians(90), 0, 0), 16, 4)]
lantern = lb.join(lp, 'Lantern', [IRON, GLASS, FLAME]); lb.cube_uv(lantern)
# glass and flame get a 0..1 square per face, so the builder's centre-to-rim glow texture fills each pane
bm = bmesh.new(); bm.from_mesh(lantern.data); uvl = bm.loops.layers.uv.active
for f in bm.faces:
    if f.material_index == 0 or abs(f.normal.z) > 0.7: continue
    t = (-f.normal.y, f.normal.x); w, h, z0 = (0.17, 0.32, 0.49) if f.material_index == 1 else (0.11, 0.26, 0.5)
    for l in f.loops: l[uvl].uv = ((l.vert.co.x * t[0] + l.vert.co.y * t[1]) / w + 0.5, (l.vert.co.z - z0) / h)
bm.to_mesh(lantern.data); bm.free(); lb.finish(lantern)

for k, o in enumerate(arcades): o.location = ((k - 1) * 16, 0, 0)
for o in rocks + chunks + [link, link_low, hang, lantern]: o.hide_render = True
lb.preview(os.path.join(here, 'renders', 'arcades.png'), (4, -32, 8), (0, 0, 4), lens=35)
for o in arcades: o.hide_render = True
for k, o in enumerate(rocks + chunks): o.hide_render = False; o.location = ((k % 5 - 2) * 4, (k // 5) * 6, 4)
lb.preview(os.path.join(here, 'renders', 'rocks.png'), (0, -18, 10), (0, 3, 2), lens=35)
for o in arcades + rocks + chunks: o.location = (0, 0, 0); o.hide_render = False
lb.export(arcades + rocks + chunks + [link, link_low, hang, lantern], os.path.join(out, 'Void', 'Void.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'void.blend')); print('done')
