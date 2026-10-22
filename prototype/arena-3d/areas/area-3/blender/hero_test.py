# Verdant Chimes style test (VERDANT-CHIMES-BLENDER-ART-PROMPT.md §6.1): three hero pieces, authored in Blender.
#   blender -b -P hero_test.py -- <out_dir>
# Every form here is designed, not generated: the spire's silhouette is a hand-written list of rings and facet cuts,
# the belfry and aqueduct are built stone by stone with bevelled edges, the bell is spun from a drawn profile, and the
# ivy is strands laid along the faces with leaf cards placed along each strand. Values (light tops, dark bases, moss
# on upward faces) are painted into vertex colours the way the Windblown method plans albedo values.
# Outputs: hero_test.blend, hero_test.glb (Y-up, named materials for the Unity CustomLit remap), renders/*.png.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.dirname(__file__)
os.makedirs(os.path.join(OUT, 'renders'), exist_ok=True)

# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
COL = {}
def collection(name):
    c = bpy.data.collections.new(name); scene.collection.children.link(c); COL[name] = c; return c

# ---------------------------------------------------------------- materials (names are the Unity remap keys)
def material(name, rgb, rough=0.85, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*[c ** 2.2 for c in rgb], 1)
    bsdf.inputs['Roughness'].default_value = rough; bsdf.inputs['Metallic'].default_value = metal
    # multiply base colour by the painted vertex colours (value plan, moss, AO)
    attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'Col'
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1.0
    mix.inputs['A'].default_value = (*[c ** 2.2 for c in rgb], 1)
    nt.links.new(attr.outputs['Color'], mix.inputs['B']); nt.links.new(mix.outputs['Result'], bsdf.inputs['Base Color'])
    return m

M = {
    'Stone': material('Stone', (0.68, 0.65, 0.59)), 'StoneDark': material('StoneDark', (0.56, 0.54, 0.49)),
    'Rock': material('Rock', (0.62, 0.61, 0.56)), 'Moss': material('Moss', (0.33, 0.47, 0.24)),
    'Ivy': material('Ivy', (0.25, 0.42, 0.22)), 'CanopyLeaf': material('CanopyLeaf', (0.40, 0.58, 0.27)),
    'Brass': material('Brass', (0.69, 0.54, 0.25), 0.35, 0.45), 'Bell': material('Bell', (0.80, 0.62, 0.28), 0.3, 0.45),
    'Timber': material('Timber', (0.36, 0.27, 0.19)), 'LanternMetal': material('LanternMetal', (0.17, 0.16, 0.13), 0.5, 0.8),
    'Flower': material('Flower', (0.96, 0.95, 0.90)), 'Pool': material('Pool', (0.20, 0.55, 0.55), 0.2),
}

# ---------------------------------------------------------------- mesh helpers
def to_object(bm, name, mat, coll, loc=(0, 0, 0)):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob); ob.location = loc
    me.materials.append(M[mat]); return ob

def bevel(ob, width=0.05, segs=2, angle=40):
    md = ob.modifiers.new('Bevel', 'BEVEL'); md.width = width; md.segments = segs; md.limit_method = 'ANGLE'; md.angle_limit = math.radians(angle)
    md.harden_normals = True
    return ob

def box(size, center=(0, 0, 0), rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rot[0], 3, 'X') @ Matrix.Rotation(rot[1], 3, 'Y') @ Matrix.Rotation(rot[2], 3, 'Z'), verts=bm.verts)
    bmesh.ops.translate(bm, vec=center, verts=bm.verts); return bm

def stone(name, size, center, coll, mat='Stone', rot=(0, 0, 0), bev=0.06, skew=(0, 0)):
    """A single cut stone: a box with its top skewed slightly (no stone is perfect) and bevelled edges."""
    bm = box(size, (0, 0, 0), rot)
    for v in bm.verts:
        if v.co.z > 0: v.co.x += skew[0]; v.co.y += skew[1]
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    ob = bevel(to_object(bm, name, mat, coll), bev, 2); paint(ob, 1, 1.1, 0.8); return ob

def boolean(target, cutter, op='DIFFERENCE'):
    md = target.modifiers.new('Bool', 'BOOLEAN'); md.operation = op; md.object = cutter; md.solver = 'EXACT'
    cutter.hide_render = True; cutter.hide_viewport = True
    return md

def apply_all(ob):
    bpy.context.view_layer.objects.active = ob
    for md in list(ob.modifiers):
        with bpy.context.temp_override(object=ob, active_object=ob):
            bpy.ops.object.modifier_apply(modifier=md.name)

def paint(ob, base=1.0, top=1.08, bottom=0.62, moss=None, zmin=None, zmax=None, facet_var=0.07):
    """Vertex-colour value plan: darker toward the base, lighter on upward faces, a little per-face variation, and
    optional moss tint on upward-facing surfaces (the Windblown 'planned values' albedo)."""
    me = ob.data
    if 'Col' not in me.color_attributes: me.color_attributes.new('Col', 'FLOAT_COLOR', 'CORNER')   # linear, so Blender and Unity read the same values
    ca = me.color_attributes['Col']; me.color_attributes.active_color = ca
    mw = ob.matrix_world; zs = [(mw @ v.co).z for v in me.vertices]
    lo = min(zs) if zmin is None else zmin; hi = max(zs) if zmax is None else zmax
    for poly in me.polygons:
        n = (mw.to_3x3() @ poly.normal).normalized()
        fv = 1 + facet_var * (((poly.index * 7919) % 97) / 97 - 0.5)
        for li in poly.loop_indices:
            z = (mw @ me.vertices[me.loops[li].vertex_index].co).z
            h = 0 if hi == lo else (z - lo) / (hi - lo)
            v = (bottom + (base - bottom) * min(1, h * 1.4)) * fv * (1 + (top - 1) * max(0, n.z))
            c = (v, v, v)
            if moss is not None and n.z > 0.4:
                k = min(1, (n.z - 0.4) * 2.5) * moss
                c = tuple(c[i] * (1 - k) + k * (0.55, 0.85, 0.42)[i] * v for i in range(3))
            ca.data[li].color = (*[min(1, x) for x in c], 1)

# ================================================================= 1. sculpted rock spire
def rock_spire(coll):
    # The silhouette, written by hand: (height, radius x, radius y, lean x, lean y). Wide root, pinched waist, an
    # overhanging ledge at 9 m, a narrower neck, then a flared, broken crown.
    # (height, radius x, radius y, lean x, lean y): wide root, strata steps, a pinched waist, an overhanging ledge at
    # 9 m, a narrower neck, a flared crown lip and a flat, broken top for the ruin to stand on
    rings = [(0.0, 3.4, 2.8, 0.0, 0.0), (1.6, 3.3, 2.75, 0.05, 0.0), (1.9, 3.05, 2.55, 0.05, 0.0), (3.6, 3.0, 2.5, 0.12, 0.02),
             (4.0, 2.75, 2.3, 0.15, 0.03), (5.6, 2.35, 1.95, 0.32, 0.08), (7.3, 2.45, 2.0, 0.48, 0.14), (8.8, 3.25, 2.75, 0.55, 0.2),
             (9.25, 3.2, 2.7, 0.55, 0.2), (9.6, 2.45, 2.05, 0.6, 0.2), (11.2, 2.25, 1.9, 0.72, 0.26), (11.5, 2.4, 2.0, 0.74, 0.27),
             (13.4, 2.05, 1.75, 0.88, 0.32), (14.5, 2.75, 2.35, 0.98, 0.35), (15.0, 2.7, 2.3, 1.0, 0.35), (15.15, 2.3, 1.95, 1.0, 0.36)]
    plan = [1.00, 0.86, 1.10, 0.93, 0.80, 1.06, 0.97, 0.78, 1.04, 0.92, 1.12, 0.88]
    twist = [0, 0, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4]                # the plan steps round every few rings
    bm = bmesh.new(); prev = None; first = None; n = len(plan)
    for (z, rx, ry, ox, oy), tw in zip(rings, twist):
        ring = []
        for k in range(n):
            a = k / n * math.tau + 0.17; m = plan[(k + tw) % n]
            ring.append(bm.verts.new((ox + math.cos(a) * rx * m, oy + math.sin(a) * ry * m, z)))
        if prev:
            for k in range(n): bm.faces.new((prev[k], prev[(k + 1) % n], ring[(k + 1) % n], ring[k]))
        else: first = ring
        prev = ring
    bm.faces.new(list(reversed(first))); bm.faces.new(prev)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # chisel cuts: three small, hand-placed planes that shear corners off the root, waist and crown (away from the ledge)
    # (no infinite chisel planes: a tilted plane shears the whole upper half off one side; the twisted twelve-sided
    # strata already give the chunky facets)
    cuts = []   # the crown stays flat: the ruin stands on it
    for co, no in cuts:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=co, plane_no=Vector(no).normalized(), clear_outer=True)
        edges = [e for e in bm.edges if e.is_boundary]
        if edges: bmesh.ops.holes_fill(bm, edges=edges)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')   # strays left by the cuts
    ob = to_object(bm, 'RockSpire', 'Rock', coll)
    # a crack split down the north face (a thin wedge boolean)
    cbm = bmesh.new()
    for co in [(-0.6, -3.5, 3.0), (-0.3, -3.5, 3.0), (0.3, -3.5, 9.0), (0.0, -3.5, 9.2), (-0.6, -1.0, 3.0), (-0.3, -1.6, 3.0), (0.3, -1.4, 9.0), (0.0, -0.8, 9.2)]:
        cbm.verts.new(co)
    v = cbm.verts; cbm.verts.ensure_lookup_table()
    for f in [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]: cbm.faces.new([v[i] for i in f])
    crack = to_object(cbm, 'RockCrack', 'Rock', coll)
    boolean(ob, crack); bevel(ob, 0.08, 2, 35); apply_all(ob)
    paint(ob, base=1.0, top=1.12, bottom=0.62, moss=0.9)
    # moss mats shaped to the upward ledges: copy the faces that look up, push them out a little, give them depth
    top = max((ob.matrix_world @ v.co).z for v in ob.data.vertices)
    tops = [ob.matrix_world @ p.center for p in ob.data.polygons if p.normal.z > 0.85 and (ob.matrix_world @ p.center).z > top - 0.3]
    cx = sum(t.x for t in tops) / len(tops); cy = sum(t.y for t in tops) / len(tops)   # middle of the flat crown
    stone('CrownStoneA', (1.1, 0.7, 0.6), (cx + 0.45, cy + 0.3, top + 0.28), coll, rot=(0, 0.06, 0.3), skew=(0.05, 0))
    stone('CrownStoneB', (0.8, 0.6, 1.3), (cx - 0.4, cy - 0.35, top + 0.62), coll, 'StoneDark', rot=(0.03, 0, -0.2))
    stone('CrownStoneC', (0.7, 0.5, 0.45), (cx + 0.6, cy - 0.6, top + 0.2), coll, rot=(0, 0, 0.9))
    ivy('SpireIvy', [drape((-1.9, -1.6, 9.2), (-0.6, -0.8, 0), 3.4, 0.12, 6, 0.4), drape((-0.6, -2.55, 9.2), (-0.2, -1, 0), 4.6, 0.12, 7, 1.3), drape((1.4, -2.3, 9.2), (0.4, -0.9, 0), 2.6, 0.1, 5, 2.2), drape((0.4, -2.0, 15.0), (0.1, -1, 0), 3.0, 0.1, 6, 0.9)], coll, 1.0, flowers=0.07)
    return ob
    mb = bmesh.new(); mb.from_mesh(ob.data)
    bmesh.ops.delete(mb, geom=[f for f in mb.faces if f.normal.z < 0.6], context='FACES')
    for vv in mb.verts: vv.co += vv.normal * 0.04
    ext = bmesh.ops.extrude_face_region(mb, geom=mb.faces[:])
    for vv in [g for g in ext['geom'] if isinstance(g, bmesh.types.BMVert)]: vv.co.z += 0.12
    moss = to_object(mb, 'RockSpireMoss', 'Moss', coll); moss.location = ob.location
    bevel(moss, 0.05, 2, 30); paint(moss, base=1.0, top=1.1, bottom=0.8)
    # a small ruin on the crown: two cut stones and a broken pier
    stone('CrownStoneA', (1.1, 0.7, 0.6), (1.4, 0.8, 15.6), coll, rot=(0, 0.1, 0.3), skew=(0.05, 0))
    stone('CrownStoneB', (0.8, 0.6, 1.3), (0.4, -0.5, 16.0), coll, 'StoneDark', rot=(0.05, 0, -0.2))
    return ob

# ================================================================= ivy (strands laid by hand, leaf cards along them)
LEAF = [(0, 0), (0.07, 0.05), (0.09, 0.14), (0.05, 0.24), (0, 0.3), (-0.05, 0.24), (-0.09, 0.14), (-0.07, 0.05)]
def ivy(name, strands, coll, leaf_scale=1.0, flowers=0):
    """strands: lists of points (top to bottom) placed against a wall; leaves alternate along each strand, larger near
    the top, angled outward from the wall (normal = facing direction)."""
    bm = bmesh.new(); fl = bmesh.new(); seed = 1
    for pts, normal in strands:
        nrm = Vector(normal).normalized()
        for i in range(len(pts) - 1):
            a, b = Vector(pts[i]), Vector(pts[i + 1]); seg = b - a; L = seg.length; steps = max(1, int(L / 0.14))
            for s in range(steps):
                seed = (seed * 1103515245 + 12345) & 0x7fffffff; r = (seed % 1000) / 1000
                p = a + seg * (s / steps); side = 1 if (s + i) % 2 else -1
                t = (i + s / steps) / max(1, len(pts) - 1)
                sc = leaf_scale * (1.15 - 0.45 * t) * (0.8 + 0.4 * r)
                tangent = seg.normalized(); bin_ = tangent.cross(nrm).normalized()
                rot = Matrix.Rotation(side * (0.6 + 0.5 * r), 3, nrm)
                up = rot @ (-tangent); right = rot @ bin_
                base = p + nrm * (0.04 + 0.05 * r) + bin_ * side * 0.04
                vs = [bm.verts.new(base + (right * x + up * y + nrm * (y * 0.35)) * sc) for x, y in LEAF]
                bm.faces.new(vs)
                if flowers and r > 1 - flowers:
                    c = base + nrm * 0.1
                    pv = [fl.verts.new(c + (bin_ * math.cos(k / 5 * math.tau) + tangent * math.sin(k / 5 * math.tau)) * 0.07 + nrm * 0.02) for k in range(5)]
                    fl.faces.new(pv)
    ob = to_object(bm, name, 'Ivy', coll); paint(ob, base=1.0, top=1.15, bottom=0.7, facet_var=0.3)
    if flowers: f = to_object(fl, name + 'Flowers', 'Flower', coll); paint(f, 1, 1, 0.95, facet_var=0.05)
    return ob

def drape(top, normal, length, wobble=0.15, kinks=5, phase=0.0):
    """One hanging strand from `top`, swaying slightly side to side as it falls (drawn, not random)."""
    nrm = Vector(normal).normalized(); side = Vector((0, 0, 1)).cross(nrm).normalized(); pts = []
    for k in range(kinks + 1):
        t = k / kinks
        pts.append(tuple(Vector(top) + Vector((0, 0, -length * t)) + side * math.sin(t * 3.1 + phase) * wobble + nrm * 0.02 * math.sin(t * 5)))
    return pts, normal

# ================================================================= 2. belfry with its bell
def bell_mesh(coll):
    # the bell profile, drawn as (radius, height) from crown to lip; spun 48 times
    prof = [(0.001, 1.00), (0.18, 0.99), (0.30, 0.94), (0.36, 0.84), (0.38, 0.70), (0.40, 0.52), (0.45, 0.34), (0.53, 0.18),
            (0.62, 0.07), (0.66, 0.02), (0.64, 0.0), (0.56, 0.03), (0.50, 0.10)]
    bm = bmesh.new(); vs = [bm.verts.new((r, 0, h - 1.0)) for r, h in prof]
    for i in range(len(vs) - 1): bm.edges.new((vs[i], vs[i + 1]))
    bmesh.ops.spin(bm, geom=bm.verts[:] + bm.edges[:], axis=(0, 0, 1), cent=(0, 0, 0), steps=48, angle=math.tau, use_duplicate=False)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = to_object(bm, 'BellMesh', 'Bell', coll)
    sm = ob.modifiers.new('Smooth', 'SUBSURF'); sm.levels = 1; sm.render_levels = 1
    # two raised bands near the lip
    for z, r in [(-0.82, 0.58), (-0.70, 0.51)]:
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.025, location=(0, 0, z), major_segments=48, minor_segments=8)
        t = bpy.context.active_object; t.name = 'BellBand'; t.data.materials.append(M['Bell'])
        for c in t.users_collection: c.objects.unlink(t)
        coll.objects.link(t); t.parent = ob
    return ob

def arch_cutter(width, spring, height, depth, name, coll, pointed=True):
    """A pointed (gothic) arch opening as a solid for booleans: straight jambs up to `spring`, two arcs meeting at
    the crown."""
    pts = [(-width / 2, 0), (width / 2, 0), (width / 2, spring)]
    r = width * 0.9 if pointed else width / 2; cx = width / 2 - r
    crown = spring + (math.sqrt(r * r - cx * cx) if pointed else r)
    for k in range(1, 12):
        t = k / 12; a = math.acos((width / 2 - cx) / r) if pointed else 0
        ang = a + (math.pi / 2 - a + (math.asin(cx / r) if pointed else 0)) * t if pointed else math.pi * t / 2
        pts.append((cx + r * math.cos(ang), spring + r * math.sin(ang)))
    pts.append((0, crown))
    for x, y in reversed(pts[3:-1]): pts.append((-x, y))
    pts.append((-width / 2, spring))
    bm = bmesh.new(); vs = [bm.verts.new((x, -depth / 2, y)) for x, y in pts]; f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    bmesh.ops.translate(bm, vec=(0, depth, 0), verts=[g for g in ext['geom'] if isinstance(g, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_object(bm, name, 'Stone', coll)

def belfry(coll, origin=Vector((0, 0, 0))):
    W, solid, cham = 3.2, 2.2, 4.4
    parts = []
    # plinth and shaft, with alternating corner quoins so the masonry reads from the game camera
    parts.append(stone('BelfryPlinth', (W + 0.5, W + 0.5, 0.5), origin + Vector((0, 0, 0.25)), coll, 'StoneDark', bev=0.08))
    shaft = stone('BelfryShaft', (W, W, solid + cham), origin + Vector((0, 0, 0.5 + (solid + cham) / 2)), coll, 'Stone', bev=0.05)
    for f, (ax, rot) in enumerate([((0, -1), 0), ((1, 0), math.pi / 2), ((0, 1), math.pi), ((-1, 0), -math.pi / 2)]):
        c = arch_cutter(1.7, 2.0, 0, W + 1, f'ArchCut{f}', coll)
        c.rotation_euler.z = rot; c.location = origin + Vector((0, 0, 0.5 + solid - 0.3))
        boolean(shaft, c)
    apply_all(shaft)
    # hollow the bell chamber
    inner = stone('ChamberCut', (W - 0.8, W - 0.8, cham + 1), origin + Vector((0, 0, 0.5 + solid + cham / 2 + 0.4)), coll)
    boolean(shaft, inner); apply_all(shaft); paint(shaft, base=1.0, top=1.1, bottom=0.68, moss=0.6)
    for zc in range(6):                                                   # quoins at every corner, alternating
        for cx, cy in [(-1, -1), (1, -1), (1, 1), (-1, 1)]:
            long = (zc + (cx + cy) // 2) % 2 == 0
            s = (0.9 if long else 0.55, 0.55 if long else 0.9, 0.55)
            q = stone(f'Quoin{zc}_{cx}{cy}', (s[0], s[1], 0.52), origin + Vector((cx * (W / 2 - s[0] / 2 + 0.06), cy * (W / 2 - s[1] / 2 + 0.06), 0.8 + zc * 1.05)), coll, 'StoneDark', bev=0.07)
            paint(q, base=1, top=1.1, bottom=0.8)
    # string course under the chamber and a cornice on top
    for name, z, h, grow in [('StringCourse', 0.5 + solid - 0.1, 0.22, 0.25), ('Cornice', 0.5 + solid + cham + 0.15, 0.35, 0.4)]:
        s = stone(name, (W + grow, W + grow, h), origin + Vector((0, 0, z)), coll, 'StoneDark', bev=0.06); paint(s, 1, 1.15, 0.85)
    # broken merlons: hand-placed, some missing, some knocked askew
    top = 0.5 + solid + cham + 0.35
    for i, (x, y, h, rz) in enumerate([(-1.3, -1.3, 0.9, 0), (0, -1.35, 0.55, 0.1), (1.3, -1.3, 1.0, 0), (1.35, 0, 0.4, -0.2),
                                       (1.3, 1.3, 0.75, 0), (-1.3, 1.3, 1.1, 0), (-1.35, 0.1, 0.5, 0.15)]):
        m = stone(f'Merlon{i}', (0.62, 0.62, h), origin + Vector((x, y, top + h / 2)), coll, 'Stone', rot=(0, 0, rz), skew=(0.04 * (i % 2), 0)); paint(m, 1, 1.1, 0.8, moss=0.7)
    # timber beam with braces, chain, and the bell (pivot at the hang point; Unity sways objects named Bell)
    bz = 0.5 + solid + cham - 0.55
    beam = stone('Beam', (W - 0.5, 0.26, 0.26), origin + Vector((0, 0, bz)), coll, 'Timber', bev=0.03); paint(beam, 1, 1.1, 0.8)
    for sx in (-1, 1):
        b = stone(f'Brace{sx}', (0.16, 0.16, 1.3), origin + Vector((sx * (W / 2 - 0.75), 0, bz - 0.45)), coll, 'Timber', rot=(0, sx * 0.65, 0), bev=0.02); paint(b, 1, 1, 0.8)
    pivot = bpy.data.objects.new('Bell', None); coll.objects.link(pivot); pivot.location = origin + Vector((0, 0, bz - 0.15))
    for k in range(5):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.06, minor_radius=0.016, location=(0, 0, -0.1 - k * 0.1), rotation=(math.pi / 2, k % 2 * math.pi / 2, 0), major_segments=12, minor_segments=6)
        t = bpy.context.active_object; t.name = 'ChainLink'; t.data.materials.append(M['LanternMetal'])
        for c in t.users_collection: c.objects.unlink(t)
        coll.objects.link(t); t.parent = pivot
    bmo = bell_mesh(coll); bmo.parent = pivot; bmo.location = (0, 0, -0.55); bmo.scale = (1.45, 1.45, 1.45)
    # ivy on the two front piers, flowers in it, and a heavy clump spilling over the cornice
    f = W / 2 + 0.06; strands = []
    for x0, ph in [(-1.25, 0.0), (-1.05, 1.2), (1.1, 2.1), (1.3, 0.6), (-1.4, 2.6)]:
        strands.append(drape(origin + Vector((x0, -f, top - 0.1)), (0, -1, 0), 2.2 + (ph * 1.3) % 3.0, 0.12, 6, ph))
    for y0, ph in [(-0.9, 0.4), (0.6, 1.7)]:
        strands.append(drape(origin + Vector((f, y0, top - 0.1)), (1, 0, 0), 2.8 + ph, 0.12, 6, ph))
    ivy('BelfryIvy', strands, coll, 1.0, flowers=0.08)
    return shaft

# ================================================================= 3. aqueduct bay with ivy
def voussoir_arch(coll, origin, radius, thick, depth, count, mat='Stone'):
    """An arch of individual wedge stones (voussoirs) with a raised keystone; each stone is its own bevelled block."""
    stones = []
    for i in range(count):
        a0 = math.pi * i / count; a1 = math.pi * (i + 1) / count; gap = 0.012
        key = i == count // 2; ro = radius + thick + (0.18 if key else 0.0) + (0.04 if i % 2 else 0)
        bm = bmesh.new(); vs = []
        for y in (-depth / 2 - (0.06 if key else 0), depth / 2 + (0.06 if key else 0)):
            for r, a in [(radius, a0 + gap), (radius, a1 - gap), (ro, a1 - gap), (ro, a0 + gap)]:
                vs.append(bm.verts.new((math.cos(a) * r, y, math.sin(a) * r)))
        for fidx in [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]: bm.faces.new([vs[k] for k in fidx])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bmesh.ops.translate(bm, vec=origin, verts=bm.verts)
        ob = bevel(to_object(bm, f'Voussoir{i}', 'StoneDark' if key else mat, coll), 0.04, 2); paint(ob, 1, 1.12, 0.85)
        stones.append(ob)
    return stones

def aqueduct_bay(coll, origin=Vector((0, 0, 0))):
    span, pier_w, h, depth = 5.0, 1.4, 9.0, 2.2
    R = span / 2 - 0.1; spring = 5.2
    for sx in (-1, 1):                                                     # piers: plinth, courses, capital
        px = origin.x + sx * (span / 2 + pier_w / 2)
        p = stone(f'PierPlinth{sx}', (pier_w + 0.4, depth + 0.4, 0.6), Vector((px, origin.y, origin.z + 0.3)), coll, 'StoneDark', bev=0.07); paint(p, 1, 1.1, 0.6)
        for c in range(7):
            w = pier_w + (0.06 if c % 2 else 0)
            s = stone(f'Pier{sx}_{c}', (w, depth, 0.68), Vector((px + (0.03 if c % 3 == 1 else 0), origin.y, origin.z + 0.6 + 0.35 + c * 0.7)), coll, 'Stone', bev=0.05, skew=(0.02 * ((c * 3) % 2), 0))
            paint(s, 1, 1.1, 0.6 + 0.05 * c, zmin=origin.z, zmax=origin.z + h)
        cap = stone(f'Capital{sx}', (pier_w + 0.35, depth + 0.25, 0.3), Vector((px, origin.y, origin.z + spring - 0.15)), coll, 'StoneDark', bev=0.06); paint(cap, 1, 1.15, 0.85)
    voussoir_arch(coll, origin + Vector((0, 0, spring)), R, 0.62, depth, 13)
    # spandrel wall above the arch, with the arch shape cut out of it
    wall = stone('Spandrel', (span + 2 * pier_w, depth - 0.2, h - spring + 0.5), origin + Vector((0, 0, spring + (h - spring + 0.5) / 2 - 0.25)), coll, 'Stone', bev=0.04)
    cut = arch_cutter(2 * (R + 0.62), 0.25, 0, depth + 1, 'SpandrelCut', coll, pointed=False)
    cut.location = origin + Vector((0, 0, spring - 0.25)); boolean(wall, cut); apply_all(wall); paint(wall, 1, 1.1, 0.8, moss=0.5, facet_var=0.0)
    # cornice, the water channel on top (a U trough with water in it), and a spill lip where it pours off
    corn = stone('Cornice', (span + 2 * pier_w + 0.5, depth + 0.5, 0.35), origin + Vector((0, 0, h + 0.18)), coll, 'StoneDark', bev=0.07); paint(corn, 1, 1.15, 0.85, moss=0.6)
    for sy in (-1, 1):
        s = stone(f'ChannelWall{sy}', (span + 2 * pier_w + 0.3, 0.35, 0.45), origin + Vector((0, sy * (depth / 2 - 0.05), h + 0.58)), coll, 'Stone', bev=0.05); paint(s, 1, 1.1, 0.9, moss=0.8)
    water = to_object(box((span + 2 * pier_w + 0.2, depth - 0.75, 0.06), origin + Vector((0, 0, h + 0.62))), 'Channel', 'Pool', coll)
    lip = stone('SpillLip', (0.5, depth - 0.6, 0.18), origin + Vector((span / 2 + pier_w + 0.3, 0, h + 0.45)), coll, 'StoneDark', bev=0.04); paint(lip, 1, 1.1, 0.9)
    # ivy curtains falling from the cornice on the front face, flowers in them; moss along the cornice
    f = -(depth / 2 + 0.3); strands = []
    for x0, L, ph in [(-3.6, 4.5, 0.3), (-3.2, 2.6, 1.4), (-2.7, 3.6, 2.2), (-0.6, 1.6, 0.8), (0.1, 2.2, 1.9), (2.5, 3.9, 0.5), (3.0, 5.6, 1.1), (3.5, 3.0, 2.7)]:
        strands.append(drape(origin + Vector((x0, f, h + 0.05)), (0, -1, 0), L, 0.14, 7, ph))
    ivy('AqueductIvy', strands, coll, 1.05, flowers=0.06)
    mb = bmesh.new()
    for i, x0 in enumerate([-3.4, -2.6, -0.2, 2.8, 3.4]):              # moss clumps sitting on the cornice
        bmesh.ops.create_icosphere(mb, subdivisions=2, radius=0.35 + 0.08 * (i % 2))
    mo = to_object(mb, 'CorniceMossTmp', 'Moss', coll); bpy.data.objects.remove(mo)
    for i, x0 in enumerate([-3.4, -2.6, -0.2, 2.8, 3.4]):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.4 + 0.08 * (i % 2), location=origin + Vector((x0, -depth / 2 - 0.05, h + 0.42)))
        t = bpy.context.active_object; t.name = f'CorniceMoss{i}'; t.scale = (1.5, 0.8, 0.45); t.data.materials.append(M['Moss'])
        for c in t.users_collection: c.objects.unlink(t)
        coll.objects.link(t); paint(t, 1, 1.15, 0.75)
    return wall

# ================================================================= build, light, render, export
hero = collection('HeroTest')
rock_spire(hero)
belfry(hero, Vector((11, 0, 4)))
aqueduct_bay(hero, Vector((24, 0, 2)))

world = bpy.data.worlds.new('VerdantSky'); scene.world = world; world.use_nodes = True
bg = world.node_tree.nodes['Background']; bg.inputs['Color'].default_value = (0.14, 0.33, 0.34, 1); bg.inputs['Strength'].default_value = 0.75
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); scene.collection.objects.link(sun)
sun.data.energy = 4.6; sun.data.color = (1.0, 0.91, 0.78); sun.data.angle = math.radians(6)
sun.rotation_euler = Vector((0.55, 0.65, -0.75)).to_track_quat('-Z', 'Y').to_euler()
fill = bpy.data.objects.new('Fill', bpy.data.lights.new('Fill', 'SUN')); scene.collection.objects.link(fill)
fill.data.energy = 0.7; fill.data.color = (0.37, 0.85, 0.82); fill.rotation_euler = (math.radians(60), 0, math.radians(40))
ground = to_object(box((80, 40, 0.2), (12, 0, -0.1)), 'Ground', 'Pool', hero)

scene.render.engine = 'BLENDER_EEVEE'; scene.render.resolution_x = 1600; scene.render.resolution_y = 900
scene.view_settings.view_transform = 'AgX' if 'AgX' in [v.identifier for v in scene.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
try: scene.eevee.use_shadows = True
except Exception: pass
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); scene.collection.objects.link(cam); scene.camera = cam

def look(cam, pos, target, lens=40):
    cam.location = pos; d = Vector(target) - Vector(pos); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens

for name, pos, tgt, lens in [('game', (12, -26, 32), (12, 0, 5), 38), ('threequarter', (-10, -26, 15), (12, 0, 6), 32),
                             ('spire', (-9, -14, 9), (0.5, 0, 8), 40), ('belfry', (17, -12, 9), (11, 0, 7.5), 40), ('aqueduct', (28, -15, 8), (24, 0, 6.5), 38)]:
    look(cam, pos, tgt, lens); scene.render.filepath = os.path.join(OUT, 'renders', f'hero-{name}.png')
    bpy.ops.render.render(write_still=True); print('RENDERED', name)

ground.hide_render = True; bpy.data.objects.remove(ground)
for o in [o for o in bpy.data.objects if o.hide_render]: bpy.data.objects.remove(o)   # boolean cutters stay out of the export
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'hero_test.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, 'hero_test.glb'), export_format='GLB', export_yup=True, export_apply=True,
                          export_attributes=True, use_selection=False)
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render)
print('TRIS', tris); print('DONE')
