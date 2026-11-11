# Escapement Twins props (9 Nov 2026 art pass): the Pallet markers (brass sun, cobalt moon) worn over marked players and
# the Counterpoise weight (Roman numeral I-IV) hung over each soak circle. Prompts: build-evidence/miniboss-art-20261109/
# escapement-twins/prompts.md. Scripted geometry, no source mesh.
#
#   blender -b -P escapement_twins_props.py -- <out_dir> [--textured] [--tex 1024] [--towers <dir with Tick/Tock GLBs>] [--no-renders]
#
# Writes <out_dir>/PalletMarkBrass.glb, PalletMarkCobalt.glb, CounterpoiseWeight.glb, turnaround_*.png and mock in-game
# frames (gameplay camera 13 m up / 9.7 m back, FOV 44; close camera 3 m up / 8 m back, FOV 40; the autotest overhead).
# Axes: -Y is the front (it becomes Unity +Z, which the game turns to the camera), Z up, origin = the marker's centre /
# the weight's bottom edge. --textured: UV-unwraps each asset into one atlas and bakes base colour, metal/rough, emission
# and normal maps from the procedural materials (Cycles), then exports with those images.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else 'props_out')
TEXTURED = '--textured' in argv
TEX = int(argv[argv.index('--tex') + 1]) if '--tex' in argv else 1024
TOWERS = argv[argv.index('--towers') + 1] if '--towers' in argv else None
RENDERS = '--no-renders' not in argv
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

def link(ob, coll=None):
    (coll or scene.collection).objects.link(ob); return ob

def new_mesh_obj(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(bpy.data.objects.new(name, me))

def set_active(ob):
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob

def apply_all(ob):
    set_active(ob)
    for m in list(ob.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

def shade_smooth(ob, angle=35):
    set_active(ob)
    try: bpy.ops.object.shade_auto_smooth(angle=math.radians(angle))
    except Exception:
        try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
        except Exception: bpy.ops.object.shade_smooth()
    # bake the smoothing into custom normals so the GLB has no geometry-nodes modifier left
    for m in list(ob.modifiers):
        try: bpy.ops.object.modifier_apply(modifier=m.name)
        except Exception: ob.modifiers.remove(m)

def join(objs, name):
    objs = [o for o in objs if o]
    set_active(objs[0])
    for o in objs[1:]: o.select_set(True)
    bpy.ops.object.join(); ob = bpy.context.view_layer.objects.active; ob.name = name; ob.data.name = name
    return ob

def assign(ob, mat):
    ob.data.materials.clear(); ob.data.materials.append(mat); return ob

def bevel(ob, w, seg=2, limit='ANGLE', angle=40):
    m = ob.modifiers.new('bevel', 'BEVEL'); m.width = w; m.segments = seg; m.limit_method = limit
    if limit == 'ANGLE': m.angle_limit = math.radians(angle)
    m.harden_normals = False
    return m

# ---------------------------------------------------------------- primitives (bmesh)
def prism(name, poly, depth, y0=0.0, mat=None, bev=0.0, seg=2):
    """Extrude a 2D polygon in the XZ plane (front face at y0 - depth/2, i.e. -Y) along Y."""
    bm = bmesh.new()
    vs = [bm.verts.new((x, y0 + depth / 2, z)) for x, z in poly]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]: v.co.y -= depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = new_mesh_obj(name, bm)
    if bev: bevel(ob, bev, seg)
    if mat: assign(ob, mat)
    return ob

def circle_pts(cx, cz, r, n, a0=0.0, a1=2 * math.pi, rz=None):
    rz = r if rz is None else rz
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cz + rz * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]

def cyl_y(name, r, depth, y, z=0.0, x=0.0, verts=32, mat=None, bev=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=(x, y, z), rotation=(math.pi / 2, 0, 0))
    ob = bpy.context.active_object; ob.name = name
    if bev: bevel(ob, bev, 2)
    if mat: assign(ob, mat)
    return ob

def torus(name, R, r, loc, rot=(math.pi / 2, 0, 0), maj=32, mnr=8, mat=None):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=maj, minor_segments=mnr, location=loc, rotation=rot)
    ob = bpy.context.active_object; ob.name = name
    if mat: assign(ob, mat)
    return ob

def dome_y(name, r, depth, y, z=0.0, mat=None, seg=24, rings=8):
    """A shallow dome facing -Y: a UV hemisphere squashed along Y."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings * 2, radius=1.0)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y > 1e-4], context='VERTS')
    for v in bm.verts: v.co = Vector((v.co.x * r, y + v.co.y * depth, z + v.co.z * r))
    # close the back
    edges = [e for e in bm.edges if e.is_boundary]
    if edges: bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = new_mesh_obj(name, bm)
    if mat: assign(ob, mat)
    return ob

def jewel(name, top, bottom, w, d, mat):
    """An elongated faceted jewel pointing down (-Z): the pallet tip."""
    bm = bmesh.new()
    n = 6; mid = top - (top - bottom) * 0.28
    ring = [bm.verts.new((w * math.cos(2 * math.pi * i / n + math.pi / 6), d * math.sin(2 * math.pi * i / n + math.pi / 6), mid)) for i in range(n)]
    t = bm.verts.new((0, 0, top)); b = bm.verts.new((0, 0, bottom))
    for i in range(n):
        bm.faces.new((ring[i], ring[(i + 1) % n], t)); bm.faces.new((ring[(i + 1) % n], ring[i], b))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return assign(new_mesh_obj(name, bm), mat)

def star(name, r_out, r_in, points, depth, y, z=0.0, rot=0.0, mat=None):
    poly = []
    for i in range(points * 2):
        a = rot + math.pi / 2 + math.pi * i / points; rr = r_out if i % 2 == 0 else r_in
        poly.append((rr * math.cos(a), z + rr * math.sin(a)))
    return prism(name, poly, depth, y0=y, mat=mat)

# ---------------------------------------------------------------- materials
def principled(name, base, metal=0.0, rough=0.5, emit=None, emit_k=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = emit_k
    return m

def srgb(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

# flat model-round materials (the texture rounds replace their node trees, see texture_materials())
PAL = {
    'Brass':   dict(base=srgb('#C9A050'), metal=1.0, rough=0.32),
    'Ivory':   dict(base=srgb('#F0E2C0'), metal=0.0, rough=0.3, emit=srgb('#FFD890'), emit_k=1.2),
    'StarGold':dict(base=srgb('#E8B030'), metal=1.0, rough=0.25),
    'Ruby':    dict(base=srgb('#C8401A'), metal=0.0, rough=0.08, emit=srgb('#FF7A1E'), emit_k=2.0),
    'Cobalt':  dict(base=srgb('#2E4FB8'), metal=0.25, rough=0.3),
    'MoonFace':dict(base=srgb('#DCE6FA'), metal=0.0, rough=0.3, emit=srgb('#B8D0FF'), emit_k=1.2),
    'Silver':  dict(base=srgb('#C8D2E6'), metal=1.0, rough=0.28),
    'Sapphire':dict(base=srgb('#1E3FA8'), metal=0.0, rough=0.08, emit=srgb('#9CC4FF'), emit_k=2.0),
    'Bronze':  dict(base=srgb('#6B4A2A'), metal=0.9, rough=0.55),
    'Iron':    dict(base=srgb('#1C1A1A'), metal=0.6, rough=0.7),
    'Glyph':   dict(base=srgb('#FFF2D0'), metal=0.0, rough=0.4, emit=srgb('#FFF2D0'), emit_k=2.5),
}
MAT = {k: principled(k, **v) for k, v in PAL.items()}

# ---------------------------------------------------------------- the Pallet markers
# round 2 (critic 1): symmetric rays on the axes and diagonals (the down ray is the pallet jewel, the loop sits on the up
# ray), a smaller ivory face so the rays carry the outline; the moon is cobalt on the front with a small pale inset, in a
# thicker, nearly closed silver ring.
RAY_LONG, RAY_SHORT, DISC_R = 0.39, 0.30, 0.16   # refine 2: longer rays (critic 2: the sun read as a dot at range)
THICK = 0.08

def pallet_tip(prefix, metal, gem, top):
    """The downward pallet jewel in a slim collar; top = where the collar meets the medallion."""
    collar = prism(prefix + 'claw', [(-0.045, top + 0.04), (0.045, top + 0.04), (0.032, top - 0.05), (-0.032, top - 0.05)], 0.05, mat=metal, bev=0.006)
    return [collar, jewel(prefix + 'jewel', top - 0.035, top - 0.235, 0.058, 0.04, gem)]

def loop(prefix, metal, z):
    return torus(prefix + 'loop', 0.038, 0.013, (0, 0, z), maj=16, mnr=6, mat=metal)

def ray(name, a_deg, L, base_w, r0, mat):
    a = math.radians(a_deg); c, s = math.cos(a), math.sin(a); n = (-s, c); hw = base_w / 2
    bl = (r0 * c + hw * n[0], r0 * s + hw * n[1]); br = (r0 * c - hw * n[0], r0 * s - hw * n[1]); tip = (L * c, L * s)
    bm = bmesh.new()
    v = [bm.verts.new((bl[0], 0.018, bl[1])), bm.verts.new((br[0], 0.018, br[1])), bm.verts.new((tip[0], 0.0, tip[1])),
         bm.verts.new((r0 * c, -0.032, r0 * s))]
    bm.faces.new((v[0], v[3], v[2])); bm.faces.new((v[3], v[1], v[2])); bm.faces.new((v[1], v[0], v[2])); bm.faces.new((v[0], v[1], v[3]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return assign(new_mesh_obj(name, bm), mat)

def build_sun():
    p = []
    p.append(cyl_y('sun_disc', DISC_R, THICK * 0.8, 0.0, verts=40, mat=MAT['Ivory'], bev=0.01))
    p.append(dome_y('sun_dome', DISC_R * 0.84, 0.03, -THICK * 0.4, mat=MAT['Ivory']))
    p.append(torus('sun_bezel', DISC_R + 0.005, 0.02, (0, -0.005, 0), maj=40, mnr=8, mat=MAT['Brass']))
    p.append(star('sun_star', 0.1, 0.045, 8, 0.02, -THICK * 0.4 - 0.025, mat=MAT['StarGold']))
    r0 = DISC_R + 0.01
    for a in (45, 135, 225, 315): p.append(ray(f'sun_long{a}', a, RAY_LONG, 0.117, r0, MAT['Brass']))
    for a in (0, 90, 180): p.append(ray(f'sun_short{a}', a, RAY_SHORT, 0.091, r0, MAT['Brass']))
    p += pallet_tip('sun_', MAT['Brass'], MAT['Ruby'], -DISC_R - 0.01)
    p.append(loop('sun_', MAT['Brass'], RAY_SHORT + 0.035))
    for o in p:
        if o.modifiers: apply_all(o)
    ob = join(p, 'PalletMarkBrass'); ob.scale = (1.08, 1.08, 1.08); apply_all(ob); shade_smooth(ob, 30)   # texture refine 2: size parity
    return ob

def crescent_poly(R=0.28, r=0.235, off=0.13, n=40, dx=0.0):
    """Outer circle (0,0,R) minus inner circle (off,0,r): thick side left (-X), horns opening right (+X)."""
    x = (R * R - r * r + off * off) / (2 * off); a = math.acos(max(-1, min(1, x / R)))
    outer = [(R * math.cos(t), R * math.sin(t)) for t in [a + (2 * math.pi - 2 * a) * i / n for i in range(n + 1)]]
    hx, hz = outer[-1]
    b0 = math.atan2(hz, hx - off); b1 = math.atan2(outer[0][1], outer[0][0] - off)
    if b1 < b0: b1 += 2 * math.pi
    inner = [(off + r * math.cos(t), r * math.sin(t)) for t in [b0 - (b0 - (b1 - 2 * math.pi)) * i / n for i in range(1, n)]]
    return [(px + dx, pz) for px, pz in outer + inner]

RING_R, RING_W = 0.322, 0.03   # refine 2: thinner, closed ring; the crescent tilts 20 degrees so it can't read as a C
def build_moon():
    p = []
    tilt = math.radians(20)
    def rot(poly): return [(x * math.cos(tilt) - z * math.sin(tilt), x * math.sin(tilt) + z * math.cos(tilt)) for x, z in poly]
    p.append(prism('moon_crescent', rot(crescent_poly(0.29, 0.245, 0.12)), THICK, mat=MAT['Cobalt'], bev=0.012))
    # the small pale inset on the crescent's thick side (the cobalt stays the front colour)
    p.append(prism('moon_face', rot(crescent_poly(0.255, 0.278, 0.12)), 0.02, y0=-THICK / 2 - 0.006, mat=MAT['MoonFace']))
    ring = torus('moon_ring', RING_R, RING_W, (0, 0, 0), maj=48, mnr=8, mat=MAT['Silver']); apply_all(ring)
    p.append(ring)
    p += pallet_tip('moon_', MAT['Silver'], MAT['Sapphire'], -RING_R - RING_W + 0.01)
    p.append(loop('moon_', MAT['Silver'], RING_R + RING_W + 0.035))
    for o in p:
        if o.modifiers: apply_all(o)
    ob = join(p, 'PalletMarkCobalt'); shade_smooth(ob, 30)
    return ob

# ---------------------------------------------------------------- the Counterpoise weight
W_HALF, W_DEPTH = 0.475, 0.25
W_SCALE = 0.85   # round 2

def weight_profile(scale=1.0, n=14):
    pts = [(-0.30, 0.0), (0.30, 0.0), (0.475, 0.22), (0.475, 0.85)]
    pts += [(0.475 * math.cos(t), 0.85 + 0.30 * math.sin(t)) for t in [math.pi * i / n for i in range(1, n)]]
    pts += [(-0.475, 0.85), (-0.475, 0.22)]
    return [(x * scale, 0.575 + (z - 0.575) * scale) for x, z in pts]

def glyph_I(x0, h, stroke, serif):
    """Boxes (x, z, w, h) of a serif I whose left edge is x0, bottom at 0."""
    return [(x0 + (serif - stroke) / 2, 0, stroke, h), (x0, 0, serif, stroke * 0.55), (x0, h - stroke * 0.55, serif, stroke * 0.55)], serif

def glyph_V(x0, h, stroke, serif):
    w = h * 0.68
    return [('V', x0, w, h, stroke), (x0 - 0.01, h - stroke * 0.55, serif, stroke * 0.55), (x0 + w - serif + 0.01, h - stroke * 0.55, serif, stroke * 0.55)], w

def numeral_obj(name, text, y_front, cz, max_w=0.66, h=0.52, stroke=0.12, mat=None):
    serif = stroke * 2.1; gap = 0.075   # refine 2: wider spacing (I and II merged in the overhead shot)
    boxes = []; x = 0.0
    for ch in text:
        b, w = (glyph_I if ch == 'I' else glyph_V)(x, h, stroke, serif); boxes += b; x += w + gap
    total = x - gap; k = min(1.0, max_w / total)
    objs = []
    depth = 0.035
    for b in boxes:
        if b[0] == 'V':
            _, bx, w, hh, st = b
            # two slanted strokes meeting at the bottom centre
            for side in (0, 1):
                top_x = bx + (st * 0.5 if side == 0 else w - st * 0.5)
                bot_x = bx + w / 2
                dx = top_x - bot_x
                poly = [(bot_x - st * 0.45 + (0 if side == 0 else 0), 0), (bot_x + st * 0.45, 0), (top_x + st * 0.5, hh), (top_x - st * 0.5, hh)]
                objs.append(prism(f'{name}_v{side}', [((px - total / 2) * k, cz + (pz - h / 2) * k) for px, pz in poly], depth, y0=y_front - depth / 2, mat=mat))
        else:
            bx, bz, w, hh = b
            poly = [(bx, bz), (bx + w, bz), (bx + w, bz + hh), (bx, bz + hh)]
            objs.append(prism(f'{name}_b', [((px - total / 2) * k, cz + (pz - h / 2) * k) for px, pz in poly], depth, y0=y_front - depth / 2, mat=mat))
    ob = join(objs, name); return ob

def build_weight():
    p = []
    body = prism('w_body', weight_profile(n=10), W_DEPTH, mat=MAT['Bronze'], bev=0.03, seg=2)
    p.append(body)
    # the dial field (dark iron), slightly proud of the body front, and its brass bezel
    field_c = 0.60; field = [(0.33 * math.cos(t) * 1.0, field_c + 0.36 * math.sin(t)) for t in [2 * math.pi * i / 48 for i in range(48)]]
    # rounded-square field (superellipse) reads as a dial plate
    def sq(rx, rz, n=48, e=4.0):
        out = []
        for i in range(n):
            t = 2 * math.pi * i / n; c, s = math.cos(t), math.sin(t)
            out.append((rx * math.copysign(abs(c) ** (2 / e), c), field_c + rz * math.copysign(abs(s) ** (2 / e), s)))
        return out
    p.append(prism('w_field', sq(0.37, 0.37), 0.02, y0=-W_DEPTH / 2 - 0.005, mat=MAT['Iron']))
    # bezel: a tube along the field's outline
    curve = bpy.data.curves.new('w_bezel', 'CURVE'); curve.dimensions = '3D'
    sp = curve.splines.new('POLY'); pts = sq(0.395, 0.395, 40)
    sp.points.add(len(pts) - 1)
    for i, (x, z) in enumerate(pts): sp.points[i].co = (x, -W_DEPTH / 2 - 0.012, z, 1)
    sp.use_cyclic_u = True; curve.bevel_depth = 0.025; curve.bevel_resolution = 1
    bz = link(bpy.data.objects.new('w_bezel', curve)); set_active(bz); bpy.ops.object.convert(target='MESH'); assign(bz, MAT['Brass'])
    p.append(bz)
    # four rivets
    for sx in (-1, 1):
        for sz in (-1, 1):
            bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=0.032, location=(sx * 0.38, -W_DEPTH / 2 - 0.005, field_c + sz * 0.38))
            r = bpy.context.active_object; r.scale.y = 0.6; assign(r, MAT['Brass']); p.append(r)
    # cap rim (brass band round the arch) and the hook ring + chain
    cap = prism('w_cap', [(0.475 * 1.04, 0.9)] + [(x, z) for x, z in weight_profile(1.04, n=10) if z > 0.9] + [(-0.475 * 1.04, 0.9)], W_DEPTH * 0.6, mat=MAT['Brass'], bev=0.015)
    # (cap is the arch silhouette above 0.9 m, slightly larger: a brass crown band seen behind the bronze)
    cap.location.y = 0.0; p.append(cap)
    p.append(cyl_y('w_boss', 0.07, W_DEPTH * 0.7, 0.0, z=1.17, verts=12, mat=MAT['Brass']))
    p.append(torus('w_hook', 0.07, 0.018, (0, 0, 1.26), rot=(math.pi / 2, 0, 0), maj=14, mnr=5, mat=MAT['Brass']))
    # round 2: four interlocking dark-bronze links, alternating planes, the top one hanging from an open hook
    for i in range(4):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.05, minor_radius=0.014, major_segments=12, minor_segments=5, location=(0, 0, 0))
        l = bpy.context.active_object
        l.rotation_euler = (math.pi / 2, 0, 0) if i % 2 == 0 else (math.pi / 2, 0, math.pi / 2)
        apply_all(l); l.scale.z = 1.5; apply_all(l); l.location = (0, 0, 1.36 + i * 0.105)
        assign(l, MAT['Bronze']); p.append(l)
    # bottom finial (points down)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.14, radius2=0.0, depth=0.15, location=(0, 0, -0.075), rotation=(math.pi, 0, 0))
    fin = bpy.context.active_object; assign(fin, MAT['Brass']); p.append(fin)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=0.04, location=(0, 0, -0.17)); k = bpy.context.active_object; assign(k, MAT['Brass']); p.append(k)
    for o in p:
        if o.modifiers or o.scale != Vector((1, 1, 1)) or o.rotation_euler != Euler((0, 0, 0)): apply_all(o)
    ob = join(p, 'CounterpoiseBody'); shade_smooth(ob, 35)
    nums = []
    for txt in ('I', 'II', 'III', 'IV'):
        n = numeral_obj('Numeral_' + txt, txt, -W_DEPTH / 2 - 0.015 - 0.0175, field_c, mat=MAT['Glyph'])
        apply_all(n); shade_smooth(n, 30); nums.append(n)
    for o in [ob] + nums:
        o.scale = (W_SCALE, W_SCALE, W_SCALE); apply_all(o)
    root = link(bpy.data.objects.new('CounterpoiseWeight', None))
    for o in [ob] + nums: o.parent = root
    return root, ob, nums

# ---------------------------------------------------------------- texture stage
def tri_count(obs):
    t = 0
    for o in obs:
        if o.type != 'MESH': continue
        for poly in o.data.polygons: t += len(poly.vertices) - 2
    return t

def nodes_for(mat_name, nt):
    """Procedural node set per material: returns sockets (base, metal, rough, emit, height)."""
    N = nt.nodes; L = nt.links
    def node(t, **kw):
        n = N.new(t)
        for k, v in kw.items(): setattr(n, k, v)
        return n
    tc = node('ShaderNodeTexCoord'); obj_co = tc.outputs['Object']
    def noise(scale, detail=6, rough=0.55, dist=0.0, co=None):
        n = node('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = rough; n.inputs['Distortion'].default_value = dist
        L.new((co or obj_co), n.inputs['Vector']); return n.outputs['Fac']
    def ramp(fac, stops):
        r = node('ShaderNodeValToRGB'); cr = r.color_ramp
        cr.elements[0].position, cr.elements[0].color = stops[0][0], (*stops[0][1], 1)
        cr.elements[1].position, cr.elements[1].color = stops[-1][0], (*stops[-1][1], 1)
        for pos, col in stops[1:-1]:
            e = cr.elements.new(pos); e.color = (*col, 1)
        L.new(fac, r.inputs['Fac']); return r.outputs['Color']
    def val(v):
        n = node('ShaderNodeValue'); n.outputs[0].default_value = v; return n.outputs[0]
    def rgb(c):
        n = node('ShaderNodeRGB'); n.outputs[0].default_value = (*c, 1); return n.outputs[0]
    def mix(fac, a, b):
        m = node('ShaderNodeMix'); m.data_type = 'RGBA'; L.new(fac, m.inputs['Factor']); L.new(a, m.inputs[6]); L.new(b, m.inputs[7]); return m.outputs[2]
    def math_(op, a, b=None, clamp=True):
        m = node('ShaderNodeMath'); m.operation = op; m.use_clamp = clamp
        if isinstance(a, (int, float)): m.inputs[0].default_value = a
        else: L.new(a, m.inputs[0])
        if b is not None:
            if isinstance(b, (int, float)): m.inputs[1].default_value = b
            else: L.new(b, m.inputs[1])
        return m.outputs[0]
    ao = node('ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.inputs['Distance'].default_value = 0.06
    cav = ao.outputs['AO']   # 1 open, 0 in cavities
    # edge wear: the bevel normal differs from the true normal on edges (Cycles Bevel node)
    bev = node('ShaderNodeBevel'); bev.samples = 8; bev.inputs['Radius'].default_value = 0.012
    geo = node('ShaderNodeNewGeometry')
    dot = node('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'; L.new(bev.outputs['Normal'], dot.inputs[0]); L.new(geo.outputs['Normal'], dot.inputs[1])
    edge = math_('SUBTRACT', 1.0, dot.outputs['Value']); edge = math_('MULTIPLY', edge, 18.0)
    k = mat_name
    S = PAL[k]
    if k in ('Brass', 'StarGold', 'Silver'):
        pal = {'Brass': ('#9A7430', '#B8903C', '#FFE6A0'), 'StarGold': ('#9A6A10', '#E8B030', '#FFE08A'), 'Silver': ('#C4CCDC', '#DCE3F0', '#F6F9FF')}[k]
        grime = {'Brass': 0.9, 'StarGold': 1.2, 'Silver': 0.25}[k]   # refine 2: a darker crease so the rays read on the salmon floor   # texture critic 1: polished, wear on the edges only
        n1 = noise(14, 8, 0.6); n2 = noise(60, 4, 0.5)
        tone = ramp(n1, [(0.3, srgb(pal[0])), (0.55, srgb(pal[1])), (0.8, srgb(pal[1]))])
        tone = mix(math_('MULTIPLY', math_('SUBTRACT', 1.0, cav), grime), tone, rgb(tuple(c * 0.45 for c in srgb(pal[0]))))   # grime in cavities
        base = mix(math_('MULTIPLY', edge, 0.9), tone, rgb(srgb(pal[2])))                          # polished worn edges
        rough = math_('ADD', math_('MULTIPLY', n2, 0.06 if k == 'Silver' else 0.12), 0.2 if k == 'Silver' else 0.22)
        rough = math_('ADD', rough, math_('MULTIPLY', math_('SUBTRACT', 1.0, cav), 0.2))
        metal = val(1.0); emit = rgb((0, 0, 0)); height = math_('MULTIPLY', noise(90, 3, 0.4), 0.3)
    elif k in ('Bronze', 'Iron'):
        pal = {'Bronze': ('#5E4024', '#6B4A2A', '#8A6238'), 'Iron': ('#1A191B', '#1E1D1F', '#3A3532')}[k]
        n1 = noise(9 if k == 'Bronze' else 70, 6, 0.5); n2 = noise(35, 6, 0.6, dist=0.4)   # iron: a fine grain
        tone = ramp(n1, [(0.35, srgb(pal[0])), (0.65, srgb(pal[1]))])   # texture critic 1: an even tone (not burl / tortoiseshell)
        soot = rgb(srgb('#1C1A1A'))
        sep = node('ShaderNodeSeparateXYZ'); L.new(obj_co, sep.inputs[0])
        low = node('ShaderNodeMapRange'); L.new(sep.outputs['Z'], low.inputs['Value']); low.inputs[1].default_value = 0.25; low.inputs[2].default_value = 0.0
        sootf = math_('ADD', math_('MULTIPLY', math_('SUBTRACT', 1.0, cav), 1.4), math_('MULTIPLY', low.outputs[0], 0.7 if k == 'Bronze' else 0.0))
        tone = mix(sootf, tone, soot)
        base = mix(math_('MULTIPLY', edge, 0.8 if k == 'Bronze' else 0.3), tone, rgb(srgb(pal[2])))
        rough = math_('ADD', math_('MULTIPLY', n2, 0.12), S['rough'] - 0.05)
        metal = val(1.0 if k == 'Bronze' else S['metal']); emit = rgb((0, 0, 0)); height = math_('MULTIPLY', n2, 0.6)
    elif k in ('Ivory', 'MoonFace'):
        pal = {'Ivory': ('#D8C49A', '#F2E6C6'), 'MoonFace': ('#C8D4F0', '#E4ECFF')}[k]
        n1 = noise(6, 5, 0.5)
        vor = node('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs['Scale'].default_value = 26; L.new(obj_co, vor.inputs['Vector'])
        crack = math_('LESS_THAN', vor.outputs['Distance'], 0.025)
        tone = ramp(n1, [(0.3, srgb(pal[0])), (0.7, srgb(pal[1]))])
        base = mix(math_('MULTIPLY', crack, 0.35), tone, rgb(srgb(pal[0])))
        base = mix(math_('MULTIPLY', math_('SUBTRACT', 1.0, cav), 1.2), base, rgb(srgb(pal[0])))
        rough = math_('ADD', math_('MULTIPLY', crack, 0.3), 0.22)
        metal = val(0.0)
        E = tuple(c * 0.6 for c in srgb('#FFE6B0' if k == 'Ivory' else '#CFE0FF'))   # refine 2: x2 in Unity = 1.2 (soft, so the disc doesn't swallow the rays)
        emit = rgb(E)
        height = math_('MULTIPLY', crack, -0.4)
    elif k == 'Cobalt':
        n1 = noise(10, 6, 0.55); n2 = noise(80, 3, 0.5)
        tone = ramp(n1, [(0.3, srgb('#2A48A8')), (0.7, srgb('#3354C2'))])   # texture critic 1: flat enamel, not lapis
        base = mix(math_('MULTIPLY', math_('SUBTRACT', 1.0, cav), 0.8), tone, rgb(srgb('#1A2C70')))
        base = mix(math_('MULTIPLY', edge, 0.9), base, rgb(srgb('#C8D2E6')))   # silver shows through on worn edges
        rough = math_('ADD', math_('MULTIPLY', n2, 0.06), 0.16)
        metal = math_('MULTIPLY', edge, 0.9); emit = rgb(tuple(c * 0.4 for c in srgb('#3D63FF'))); height = math_('MULTIPLY', n2, 0.1)   # refine 2: the cobalt must survive at range
    elif k in ('Ruby', 'Sapphire'):
        lo, hi = ('#C0461A', '#E86A28') if k == 'Ruby' else ('#1E3FA8', '#7AA8FF')
        lw = node('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.4
        base = mix(lw.outputs['Facing'], rgb(srgb(hi)), rgb(srgb(lo)))
        rough = val(0.08); metal = val(0.0); emit = mix(lw.outputs['Facing'], rgb(tuple(c * 0.8 for c in S['emit'])), rgb(tuple(c * 0.25 for c in S['emit'])))
        height = val(0.0)
    elif k == 'Glyph':
        n1 = noise(30, 4, 0.5)
        base = ramp(n1, [(0.3, srgb('#F2E2B8')), (0.7, srgb('#FFF6DC'))])
        base = mix(math_('MULTIPLY', edge, 0.6), base, rgb(srgb('#FFFFFF')))
        nrm = node('ShaderNodeSeparateXYZ'); L.new(geo.outputs['Normal'], nrm.inputs[0])
        face = math_('LESS_THAN', nrm.outputs['Y'], -0.5)   # front faces only (-Y): no glowing side walls
        emit = mix(face, rgb((0, 0, 0)), rgb(tuple(c * 0.5 for c in S['emit'])))
        rough = val(0.35); metal = val(0.0); height = val(0.0)
    return base, metal, rough, emit, height

def bake_asset(objs, slug):
    """Atlas-unwrap objs together, bake base / metal / rough / emission / normal from the procedural materials, and rebuild
    one material per asset that uses the baked images (what glTF carries)."""
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 16; scene.cycles.device = 'CPU'
    try: scene.cycles.use_denoising = False
    except Exception: pass
    meshes = [o for o in objs if o.type == 'MESH']
    # unwrap all together into one atlas
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    for o in meshes:
        while len(o.data.uv_layers) < 1: o.data.uv_layers.new(name='UVMap')
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.6, scale_to_bounds=False)
    try: bpy.ops.uv.pack_islands(margin=0.004, rotate=True, shape_method='CONCAVE')
    except Exception: pass
    bpy.ops.object.mode_set(mode='OBJECT')
    imgs = {}
    for ch in ('base', 'metal', 'rough', 'emit', 'normal'):
        im = bpy.data.images.new(f'{slug}_{ch}', TEX, TEX, alpha=False, float_buffer=False)
        im.colorspace_settings.name = 'sRGB' if ch in ('base', 'emit') else 'Non-Color'
        imgs[ch] = im
    # per material: build the procedural graph once, then rewire an Emission output per channel and bake EMIT
    mats = []
    for o in meshes:
        for s in o.material_slots:
            if s.material and s.material not in mats: mats.append(s.material)
    graphs = {}
    for m in mats:
        nt = m.node_tree; nt.nodes.clear()
        socks = nodes_for(m.name.split('.')[0], nt)
        out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Strength'].default_value = 1
        nt.links.new(em.outputs[0], out.inputs['Surface'])
        bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); bump = nt.nodes.new('ShaderNodeBump'); bump.inputs['Distance'].default_value = 0.004
        nt.links.new(socks[4], bump.inputs['Height']); nt.links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
        tex = nt.nodes.new('ShaderNodeTexImage'); nt.nodes.active = tex
        graphs[m] = (socks, em, out, bsdf, tex)
    def bake(ch, idx, kind='EMIT'):
        for m, (socks, em, out, bsdf, tex) in graphs.items():
            tex.image = imgs[ch]; m.node_tree.nodes.active = tex
            for l in list(em.inputs['Color'].links): m.node_tree.links.remove(l)
            for l in list(out.inputs['Surface'].links): m.node_tree.links.remove(l)
            if kind == 'EMIT':
                s = socks[idx]
                m.node_tree.links.new(s, em.inputs['Color']); m.node_tree.links.new(em.outputs[0], out.inputs['Surface'])
            else:
                m.node_tree.links.new(bsdf.outputs[0], out.inputs['Surface'])
        for o in bpy.context.selected_objects: o.select_set(False)
        for o in meshes: o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        scene.render.bake.margin = 6
        if kind == 'EMIT': bpy.ops.object.bake(type='EMIT')
        else: bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
    bake('base', 0); bake('metal', 1); bake('rough', 2); bake('emit', 3); bake('normal', None, 'NORMAL')
    # pack metal (B) + rough (G) into one glTF metallicRoughness image
    import numpy as np
    mr = bpy.data.images.new(f'{slug}_metalrough', TEX, TEX, alpha=False); mr.colorspace_settings.name = 'Non-Color'
    met = np.array(imgs['metal'].pixels[:]).reshape(-1, 4); rou = np.array(imgs['rough'].pixels[:]).reshape(-1, 4)
    px = np.ones_like(met); px[:, 0] = 1.0; px[:, 1] = rou[:, 0]; px[:, 2] = met[:, 0]
    mr.pixels[:] = px.ravel()
    texdir = os.path.join(OUT, 'textures'); os.makedirs(texdir, exist_ok=True)
    for im in list(imgs.values()) + [mr]:
        im.filepath_raw = os.path.join(texdir, im.name + '.png'); im.file_format = 'PNG'; im.save()
    # the export material: one per asset
    fm = bpy.data.materials.new(slug); fm.use_nodes = True; nt = fm.node_tree
    b = nt.nodes['Principled BSDF']
    def img(im, nonc):
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = im; return t
    tb = img(imgs['base'], False); nt.links.new(tb.outputs['Color'], b.inputs['Base Color'])
    tm = img(mr, True); sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(tm.outputs['Color'], sep.inputs['Color'])
    nt.links.new(sep.outputs['Green'], b.inputs['Roughness']); nt.links.new(sep.outputs['Blue'], b.inputs['Metallic'])
    te = img(imgs['emit'], False); nt.links.new(te.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 1.0
    tn = img(imgs['normal'], True); nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
    for o in meshes:
        o.data.materials.clear(); o.data.materials.append(fm)
    scene.render.engine = EEVEE
    return fm

# ---------------------------------------------------------------- build
sun = build_sun(); moon = build_moon(); wroot, wbody, wnums = build_weight()
assets = {'PalletMarkBrass': [sun], 'PalletMarkCobalt': [moon], 'CounterpoiseWeight': [wbody] + wnums}

EEVEE = 'BLENDER_EEVEE'
try: scene.render.engine = EEVEE
except TypeError: EEVEE = 'BLENDER_EEVEE_NEXT'; scene.render.engine = EEVEE

if TEXTURED:
    for slug, obs in assets.items(): bake_asset(obs, slug)

# keep one hidden copy of each for exports; place the originals at the origin
stats = []
def export(slug, objs, root=None):
    for o in bpy.context.selected_objects: o.select_set(False)
    sel = ([root] if root else []) + objs
    for o in sel: o.hide_set(False); o.select_set(True)
    path = os.path.join(OUT, slug + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_yup=True,
                              export_animations=False, export_materials='EXPORT', export_image_format='AUTO')
    stats.append((slug, tri_count(objs), os.path.getsize(path)))

export('PalletMarkBrass', [sun]); export('PalletMarkCobalt', [moon]); export('CounterpoiseWeight', [wbody] + wnums, wroot)
with open(os.path.join(OUT, 'stats.txt'), 'w') as f:
    for s in stats: f.write(f'{s[0]}: {s[1]} tris, {s[2] / 1e6:.2f} MB\n')
print('STATS', stats)
if not RENDERS: sys.exit(0)

# ---------------------------------------------------------------- render helpers
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX' if 'AgX' in [i.name for i in bpy.types.ColorManagedViewSettings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
try: scene.eevee.taa_render_samples = 32
except Exception: pass

def world(color, strength):
    w = bpy.data.worlds.new('w'); w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs['Color'].default_value = (*color, 1); bg.inputs['Strength'].default_value = strength
    scene.world = w

def cam(name, pos, look, fov_v, lens_vertical=True):
    c = bpy.data.cameras.new(name); c.sensor_fit = 'VERTICAL' if lens_vertical else 'AUTO'; c.angle = math.radians(fov_v)
    c.clip_start = 0.05; c.clip_end = 300
    o = link(bpy.data.objects.new(name, c)); o.location = pos
    d = Vector(look) - Vector(pos); o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return o

def shot(camobj, path):
    scene.camera = camobj; scene.render.filepath = path; bpy.ops.render.render(write_still=True)

def light(kind, loc, energy, color=(1, 1, 1), rot=None, size=None):
    l = bpy.data.lights.new('l', kind); l.energy = energy; l.color = color
    if size is not None and kind == 'SUN': l.angle = size
    o = link(bpy.data.objects.new('l', l)); o.location = loc
    if rot: o.rotation_euler = rot
    return o

def instance(src_objs, name, loc, rot_z=0.0, show=None):
    """A linked copy of an asset's mesh objects under an empty."""
    e = link(bpy.data.objects.new(name, None)); e.location = loc; e.rotation_euler = (0, 0, rot_z)
    for o in src_objs:
        if show and o.name not in show and o.name.startswith('Numeral_'): continue
        c = o.copy(); link(c); c.parent = e; c.matrix_parent_inverse = Matrix.Identity(4); c.location = (0, 0, 0); c.hide_render = False; c.hide_set(False)
    return e

def face_camera(e, camobj, full=True):
    """Billboard like the game: the asset's front (-Y) toward the camera."""
    d = camobj.location - e.location
    if not full: d.z = 0
    e.rotation_euler = (-d).to_track_quat('Y', 'Z').to_euler()

# ---------------------------------------------------------------- turnarounds (neutral studio)
studio = bpy.data.collections.new('studio'); scene.collection.children.link(studio)
world((0.32, 0.32, 0.34), 0.9)
key = light('SUN', (0, 0, 10), 3.0, (1, 0.97, 0.92), rot=(math.radians(50), 0, math.radians(-35)))
rim = light('SUN', (0, 0, 10), 1.2, (0.85, 0.9, 1.0), rot=(math.radians(60), 0, math.radians(150)))
for o in [sun, moon, wbody] + wnums: o.hide_render = True
def turnaround(slug, objs, center, dist, show=None):
    files = []
    for i, (yaw, elev) in enumerate([(0, 8), (35, 18), (90, 8), (180, 8), (0, 55)]):
        e = instance(objs, 'ta_' + slug, (0, 0, 0), show=show)
        a = math.radians(-90 + yaw)
        pos = Vector((center[0] + dist * math.cos(a) * math.cos(math.radians(elev)), center[1] + dist * math.sin(a) * math.cos(math.radians(elev)), center[2] + dist * math.sin(math.radians(elev))))
        c = cam('ta_cam', pos, center, 30)
        f = os.path.join(OUT, f'turn_{slug}_{i}.png'); shot(c, f); files.append(f)
        for ch in list(e.children): bpy.data.objects.remove(ch, do_unlink=True)
        bpy.data.objects.remove(e, do_unlink=True); bpy.data.objects.remove(c, do_unlink=True)
    return files

TA = {}
TA['PalletMarkBrass'] = turnaround('PalletMarkBrass', [sun], (0, 0, -0.03), 2.4)
TA['PalletMarkCobalt'] = turnaround('PalletMarkCobalt', [moon], (0, 0, -0.03), 2.4)
for txt in ('I', 'II', 'III', 'IV'):
    TA['CounterpoiseWeight_' + txt] = turnaround('CounterpoiseWeight_' + txt, [wbody] + wnums, (0, 0, 0.66), 4.6, show={'Numeral_' + txt})

# ---------------------------------------------------------------- mock Brass Foundry arena with the real towers
bpy.data.objects.remove(key, do_unlink=True); bpy.data.objects.remove(rim, do_unlink=True)
world((0.06, 0.035, 0.02), 1.0)
light('SUN', (0, 0, 20), 2.2, (1.0, 0.86, 0.7), rot=(math.radians(35), 0, math.radians(20)), size=math.radians(3))
for a in range(0, 360, 45):
    light('POINT', (17 * math.cos(math.radians(a)), 17 * math.sin(math.radians(a)), 1.5), 900, (1.0, 0.45, 0.12))
floor_m = principled('floor', srgb('#34302E'), 0.2, 0.75)
pink_m = principled('hostile', srgb('#8A5A58'), 0.0, 0.85, srgb('#C07068'), 0.08)
inlay_m = principled('inlay', srgb('#B8904A'), 1.0, 0.35)
lava_m = principled('lava', srgb('#C04A10'), 0.0, 0.6, srgb('#FF5A08'), 1.2)
soak_m = principled('soak', srgb('#D89090'), 0.0, 0.7, srgb('#F0A0A0'), 0.6)
brim_m = principled('brassrim', srgb('#FFD070'), 0.0, 0.5, srgb('#FFC050'), 4.0)
crim_m = principled('cobaltrim', srgb('#70B0FF'), 0.0, 0.5, srgb('#60A0FF'), 4.0)
cloth_m = principled('cloth', srgb('#8A8EA0'), 0.0, 0.7)
skin_m = principled('skin', srgb('#E8D8C8'), 0.0, 0.6)

def disc(name, r, z, mat, verts=96):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=0.01, location=(0, 0, z)); o = bpy.context.active_object; o.name = name; assign(o, mat); return o
def ring(name, r, w, z, mat, loc=(0, 0)):
    o = torus(name, r, w, (loc[0], loc[1], z), rot=(0, 0, 0), maj=96, mnr=4, mat=mat); o.scale.z = 0.2; return o
def dotted(name, r, z, mat, loc, n=40):
    for i in range(n):
        a = 2 * math.pi * i / n
        bpy.ops.mesh.primitive_cube_add(size=1, location=(loc[0] + r * math.cos(a), loc[1] + r * math.sin(a), z), rotation=(0, 0, a + math.pi / 2))
        o = bpy.context.active_object; o.scale = (0.32, 0.07, 0.02); assign(o, mat)

arena = []
FLOOR_R = 15.0
def build_floor(hostile):
    disc('floor', FLOOR_R, -0.005, pink_m if hostile else floor_m)
    torus('lava', FLOOR_R + 1.2, 1.0, (0, 0, -0.3), rot=(0, 0, 0), maj=96, mnr=8, mat=lava_m)
    for r in (3.0, 8.0, 12.5): ring('inlay', r, 0.05, 0.004, inlay_m)
    for a in (0, 90):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.004), rotation=(0, 0, math.radians(a))); o = bpy.context.active_object; o.scale = (2 * FLOOR_R, 0.08, 0.01); assign(o, inlay_m)

def player(loc, yaw=0.0):
    e = link(bpy.data.objects.new('player', None)); e.location = loc
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.28, depth=1.1, location=(loc[0], loc[1], 0.75)); b = bpy.context.active_object; assign(b, cloth_m)
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.42, radius2=0.2, depth=0.6, location=(loc[0], loc[1], 0.3)); assign(bpy.context.active_object, cloth_m)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.2, location=(loc[0], loc[1], 1.55)); assign(bpy.context.active_object, skin_m)
    return Vector(loc)

towers = {}
def import_tower(name, loc, camobj=None):
    if not TOWERS: return None
    path = os.path.join(TOWERS, name + '.glb')
    if not os.path.exists(path): return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    root = link(bpy.data.objects.new(name + '_root', None)); root.location = loc
    for o in new:
        if o.parent is None: o.parent = root
    return root

def clear_scene_dynamic(keep):
    for o in list(bpy.data.objects):
        if o not in keep: bpy.data.objects.remove(o, do_unlink=True)

base_keep = set(bpy.data.objects)   # assets + lights
TICK, TOCK = Vector((-6, 0, 0)), Vector((6, 0, 0))

def tower_face(root, camobj):
    if root: face_camera(root, camobj, full=False)

frames = []
def scene_pallet(label):
    """Pallet Lock, 4 players: two brass on Tick's disc, two cobalt on Tock's, gameplay camera on a brass player."""
    build_floor(hostile=True)
    for c, m in ((TICK, brim_m), (TOCK, crim_m)):
        bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=4.5, depth=0.01, location=(c.x, c.y, 0.0)); assign(bpy.context.active_object, floor_m)
        dotted('rim', 4.5, 0.01, m, c)
    tk = import_tower('EscapementTick', TICK); tc = import_tower('EscapementTock', TOCK)
    ps = [player((-4.2, -2.0, 0)), player((-7.3, 1.8, 0)), player((4.4, -2.3, 0)), player((7.0, 2.2, 0))]
    marks = [sun, sun, moon, moon]
    p0 = ps[0]
    cams = {'game': cam('game', p0 + Vector((0, -9.7, 13)), p0 + Vector((0, 0, 1)), 44),
            'close': cam('close', p0 + Vector((1.2, -8, 3)), p0 + Vector((1.2, 0, 2.3)), 40),
            'over': cam('over', (0, -15, 30), (0, 0.5, 0), 50)}
    for k, c in cams.items():
        es = []
        for p, mk in zip(ps, marks):
            e = instance([mk], 'mark', p + Vector((0, 0, 2.6))); face_camera(e, c); es.append(e)
        tower_face(tk, c); tower_face(tc, c)
        f = os.path.join(OUT, f'game_pallet_{k}.png'); shot(c, f); frames.append(f)
        for e in es:
            for ch in list(e.children): bpy.data.objects.remove(ch, do_unlink=True)
            bpy.data.objects.remove(e, do_unlink=True)
    clear_scene_dynamic(base_keep)

WEIGHT_Z = 1.3   # bottom edge above the circle (EscapementTwinsEncounter.Numeral)
def scene_counter(label, needs=('II', 'I')):
    """Counterpoise, 3 players: II beyond Tick, I beyond Tock (circles 4 m beyond each twin, 3 m radius)."""
    build_floor(hostile=False)
    spots = [Vector((-10, 0, 0)), Vector((10, 0, 0))]
    for s in spots:
        bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=3.0, depth=0.01, location=(s.x, s.y, 0.006)); assign(bpy.context.active_object, soak_m)
        ring('soakrim', 3.0, 0.08, 0.012, brim_m, loc=(s.x, s.y))
    tk = import_tower('EscapementTick', TICK); tc = import_tower('EscapementTock', TOCK)
    ps = [player((-10.6, -0.8, 0)), player((-9.3, 0.9, 0)), player((9.0, -1.4, 0))]
    p0 = ps[0]
    cams = {'game': cam('game', p0 + Vector((0, -9.7, 13)), p0 + Vector((0, 0, 1)), 44),
            'close': cam('close', spots[0] + Vector((0, -8, 3)), spots[0] + Vector((0, 0, 2.3)), 40),
            'over': cam('over', (0, -15, 30), (0, 0.5, 0), 50),
            'far': cam('far', ps[2] + Vector((0, -9.7, 13)), ps[2] + Vector((0, 0, 1)), 44)}
    for k, c in cams.items():
        es = []
        for s, n in zip(spots, needs):
            e = instance([wbody] + wnums, 'weight', s + Vector((0, 0, WEIGHT_Z)), show={'Numeral_' + n}); face_camera(e, c); es.append(e)
        tower_face(tk, c); tower_face(tc, c)
        f = os.path.join(OUT, f'game_counter_{k}.png'); shot(c, f); frames.append(f)
        for e in es:
            for ch in list(e.children): bpy.data.objects.remove(ch, do_unlink=True)
            bpy.data.objects.remove(e, do_unlink=True)
    clear_scene_dynamic(base_keep)

scene_pallet('pallet'); scene_counter('counter')

# ---------------------------------------------------------------- contact sheets (PIL is not in Blender: use bpy images)
def sheet(files, cols, out, w=640, h=360):
    import numpy as np
    rows = (len(files) + cols - 1) // cols
    canvas = np.zeros((rows * h, cols * w, 4), dtype=np.float32); canvas[..., 3] = 1
    for i, f in enumerate(files):
        im = bpy.data.images.load(f); im.scale(w, h)
        px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r = rows - 1 - i // cols; c = i % cols
        canvas[r * h:(r + 1) * h, c * w:(c + 1) * w] = px
        bpy.data.images.remove(im)
    out_im = bpy.data.images.new('sheet', cols * w, rows * h, alpha=False); out_im.pixels[:] = canvas.ravel()
    out_im.filepath_raw = out; out_im.file_format = 'PNG'; out_im.save(); bpy.data.images.remove(out_im)

for slug, files in TA.items(): sheet(files, 5, os.path.join(OUT, f'sheet_turn_{slug}.png'), 512, 288)
sheet([f for f in frames if 'pallet' in f], 3, os.path.join(OUT, 'sheet_game_pallet.png'), 960, 540)
sheet([f for f in frames if 'counter' in f], 2, os.path.join(OUT, 'sheet_game_counter.png'), 960, 540)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'escapement_twins_props.blend'))
print('DONE', OUT)
