# Verdant Chimes, hand-modelled environment kit + layout (VERDANT-CHIMES-BLENDER-ART-PROMPT.md).
#   blender -b -P build_verdant.py -- <out_dir> [--renders]
# Reads layout.json (every placement from verdant-arena.js, three.js world coords: x east, y up, -z north) and builds the
# solid environment from authored kit pieces: worn, chipped, bevelled cut stone; voussoir arches; coursed piers; seven
# hand-drawn rock profiles; three tree types with placed leaf clusters; ivy strands; lanterns; belfries with bells.
# Assemblies merge their stones into one mesh per material; repeated kit pieces are linked duplicates (shared meshes).
# Exports VerdantChimes_Art.glb (Y-up; Unity's builder turns the root 180 degrees like the procedural glb), plus
# art_materials.json (material name -> base colour) for the Unity remap, and optional renders.
import bpy, bmesh, math, os, sys, json
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = ARGS[0] if ARGS else os.path.dirname(__file__)
RENDERS = '--renders' in ARGS
HERE = os.path.dirname(os.path.abspath(__file__))
L = json.load(open(os.path.join(HERE, 'layout.json')))
R, PLAY_R, BRIDGE_W = 14.6, 14.0, 4.0
EXITS = [0, math.pi / 2, math.pi, math.pi * 1.5]
DIAG = [math.pi * 0.25, math.pi * 0.75, math.pi * 1.25, math.pi * 1.75]
GAP = math.asin((BRIDGE_W / 2 + 0.2) / (R + 0.5))
def ang_dist(a, b): return abs(math.atan2(math.sin(a - b), math.cos(a - b)))
def near_exit(a, tol): return any(ang_dist(a, e) < tol for e in EXITS)
def near_diag(a, tol): return any(ang_dist(a, e) < tol for e in DIAG)
def T(p): return Vector((p[0], -p[2], p[1]))                     # three.js world -> Blender world
def hsh(*k):                                                     # deterministic per-piece variation (not random scatter)
    h = 2166136261
    for v in k:
        for ch in repr(v): h = ((h ^ ord(ch)) * 16777619) & 0xffffffff
    return (h % 10000) / 10000

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
def collection(name, parent=None):
    c = bpy.data.collections.new(name); (parent or scene.collection).children.link(c); return c
KIT, ART = collection('Kit'), collection('Layout')
KIT.hide_render = True

# ------------------------------------------------------------------ palette (names are the Unity remap keys)
PALETTE = {
    'Stone': (0.70, 0.67, 0.61), 'StoneDark': (0.55, 0.53, 0.48), 'Deck': (0.66, 0.63, 0.57), 'Rock': (0.60, 0.59, 0.54),
    'Moss': (0.34, 0.48, 0.24), 'Ivy': (0.24, 0.41, 0.21), 'CanopyLeaf': (0.38, 0.57, 0.26), 'CanopyDark': (0.15, 0.26, 0.15),
    'Bark': (0.30, 0.23, 0.17), 'Timber': (0.36, 0.27, 0.19), 'Brass': (0.69, 0.54, 0.25), 'BrassDark': (0.48, 0.37, 0.18),
    'Bell': (0.80, 0.62, 0.28), 'LanternMetal': (0.17, 0.16, 0.13), 'Flower': (0.96, 0.95, 0.90), 'Fern': (0.31, 0.50, 0.23),
    'GlowAmber': (1.0, 0.77, 0.47), 'Pool': (0.20, 0.55, 0.55),
}
MATS = {}
def material(name):
    if name in MATS: return MATS[name]
    rgb = PALETTE[name]; m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF'); lin = tuple(c ** 2.2 for c in rgb)
    bsdf.inputs['Base Color'].default_value = (*lin, 1); bsdf.inputs['Roughness'].default_value = 0.85
    if name in ('Brass', 'BrassDark', 'Bell', 'LanternMetal'): bsdf.inputs['Metallic'].default_value = 0.45; bsdf.inputs['Roughness'].default_value = 0.35
    if name == 'GlowAmber': bsdf.inputs['Emission Color'].default_value = (*lin, 1); bsdf.inputs['Emission Strength'].default_value = 4
    vc = nt.nodes.new('ShaderNodeVertexColor'); vc.layer_name = 'Col'
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1.0
    mix.inputs['A'].default_value = (*lin, 1); nt.links.new(vc.outputs['Color'], mix.inputs['B']); nt.links.new(mix.outputs['Result'], bsdf.inputs['Base Color'])
    MATS[name] = m; return m

# ------------------------------------------------------------------ geometry primitives (bmesh, no modifiers)
def bm_box(sx, sy, sz):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts); return bm

def bm_bevel(bm, offset, segs=2):
    if offset > 0: bmesh.ops.bevel(bm, geom=bm.edges[:], offset=offset, segments=segs, profile=0.5, affect='EDGES', clamp_overlap=True)
    return bm

def cut(bm, co, no):
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=co, plane_no=Vector(no).normalized(), clear_outer=True)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges: bmesh.ops.holes_fill(bm, edges=edges)

def stone_bm(sx, sy, sz, key, bev=0.05, chips=1, skew=0.03):
    """A worn cut stone: slightly out of square, one or two corners knocked off, edges bevelled."""
    bm = bm_box(sx, sy, sz)
    for v in bm.verts:                                            # nothing is perfectly square
        v.co.x += (hsh(key, 'x', round(v.co.z, 2)) - 0.5) * skew * 2 * (1 if v.co.z > 0 else 0.4)
        v.co.y += (hsh(key, 'y', round(v.co.x, 2)) - 0.5) * skew * 2 * (1 if v.co.z > 0 else 0.4)
    for c in range(chips):                                        # knocked-off corners
        if hsh(key, 'chip', c) < 0.45: continue
        s = Vector(((1 if hsh(key, c, 'a') > 0.5 else -1), (1 if hsh(key, c, 'b') > 0.5 else -1), (1 if hsh(key, c, 'c') > 0.35 else -1)))
        corner = Vector((s.x * sx / 2, s.y * sy / 2, s.z * sz / 2)); d = s.normalized()
        depth = min(sx, sy, sz) * (0.18 + 0.2 * hsh(key, c, 'd'))
        cut(bm, corner - d * depth, d)
    return bm_bevel(bm, min(bev, sx * 0.2, sy * 0.2, sz * 0.2), 2)

def bm_merge(dst, src, mat4):
    me = bpy.data.meshes.new('tmp'); src.transform(mat4); src.to_mesh(me); dst.from_mesh(me); bpy.data.meshes.remove(me)

def M4(loc=(0, 0, 0), rz=0.0, rx=0.0, ry=0.0, s=(1, 1, 1)):
    return Matrix.Translation(Vector(loc)) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X') @ Matrix.Diagonal((*s, 1))

# ------------------------------------------------------------------ painting: the value plan in vertex colours
def paint_bm(bm, zlo, zhi, base=1.05, top=1.14, bottom=0.55, moss=0.0, wear=0.12, var=0.04, key=0, grime=True):
    """Darker toward the base of the assembly, lighter on up-facing planes, bevel/chip faces (not axis-aligned) lighter
    as worn edges, moss tint on up-facing ledges, and a little per-face variation."""
    lay = bm.loops.layers.float_color.get('Col') or bm.loops.layers.float_color.new('Col')
    for f in bm.faces:
        n = f.normal; axis = max(abs(n.x), abs(n.y), abs(n.z))
        fv = 1 + var * (hsh(key, f.index % 97) - 0.5) * 2
        edge = 1 + wear * (1 - min(1, (axis - 0.7) / 0.28)) if axis < 0.98 else 1.0
        for lp in f.loops:
            z = lp.vert.co.z; h = 0.0 if zhi == zlo else max(0.0, min(1.0, (z - zlo) / (zhi - zlo)))
            v = (bottom + (base - bottom) * min(1, h * 1.3)) if grime else base
            v *= fv * edge * (1 + (top - 1) * max(0, n.z))
            c = [v, v, v]
            if moss > 0 and n.z > 0.45:
                k = min(1, (n.z - 0.45) * 2.2) * moss
                c = [c[i] * (1 - k) + k * (0.56, 0.86, 0.44)[i] * v for i in range(3)]
            lp[lay] = (*[min(1.3, x) for x in c], 1.0)

class Asm:
    """An assembly: per-material bmesh batches that become one object per material."""
    def __init__(self, name): self.name = name; self.b = {}
    def bm(self, mat):
        if mat not in self.b: self.b[mat] = bmesh.new()
        return self.b[mat]
    def add(self, mat, src, mat4=None, paint=None):
        if mat4 is not None: src.transform(mat4)
        if paint is not None: paint_bm(src, *paint[0], **paint[1])
        me = bpy.data.meshes.new('tmp'); src.to_mesh(me); self.bm(mat).from_mesh(me); bpy.data.meshes.remove(me); src.free()
    def stone(self, mat, size, loc, key, rz=0.0, rx=0.0, ry=0.0, zr=None, moss=0.0, bev=0.05, chips=1, top=1.14, bottom=0.55):
        bm = stone_bm(*size, key, bev, chips)
        bm.transform(M4(loc, rz, rx, ry))
        zlo, zhi = zr if zr else (loc[2] - size[2] / 2, loc[2] + size[2] / 2)
        paint_bm(bm, zlo, zhi, top=top, bottom=bottom, moss=moss, key=key)
        self.add(mat, bm)
    def build(self, coll, origin=None):
        obs = []
        for mat, bm in self.b.items():
            me = bpy.data.meshes.new(f'{self.name}_{mat}'); bm.normal_update(); bm.to_mesh(me); bm.free()
            me.materials.append(material(mat)); ob = bpy.data.objects.new(f'{self.name}_{mat}', me); coll.objects.link(ob); obs.append(ob)
        self.b = {}; return obs

def instance(src_objs, coll, loc, rz=0.0, s=1.0, name=None):
    """Linked duplicates of a kit piece (shared mesh data), parented to one empty."""
    root = bpy.data.objects.new(name or src_objs[0].name.split('_')[0], None); coll.objects.link(root)
    root.location = loc; root.rotation_euler = (0, 0, rz); root.scale = (s, s, s)
    for o in src_objs:
        d = o.copy(); coll.objects.link(d); d.parent = root; d.matrix_parent_inverse = Matrix.Identity(4)
    return root

# ------------------------------------------------------------------ ivy (strands laid along faces, leaves along each strand)
LEAF = [(0, 0), (0.07, 0.05), (0.09, 0.14), (0.05, 0.24), (0, 0.3), (-0.05, 0.24), (-0.09, 0.14), (-0.07, 0.05)]
def ivy(asm, strands, leaf_scale=1.0, flowers=0.06, key=0):
    leaves = bmesh.new(); fl = bmesh.new(); lay = leaves.loops.layers.float_color.new('Col'); flay = fl.loops.layers.float_color.new('Col'); n = 0
    for pts, normal in strands:
        nrm = Vector(normal).normalized()
        for i in range(len(pts) - 1):
            a, b = Vector(pts[i]), Vector(pts[i + 1]); seg = b - a; Ls = seg.length; steps = max(1, int(Ls / 0.15))
            tangent = seg.normalized(); bi = tangent.cross(nrm).normalized()
            steps = max(1, int(Ls / 0.1))
            for s in range(steps):
                n += 1; r = hsh(key, n); p = a + seg * (s / steps); side = 1 if n % 2 else -1
                t = (i + s / steps) / max(1, len(pts) - 1); sc = leaf_scale * (1.15 - 0.45 * t) * (0.8 + 0.4 * r)
                rot = Matrix.Rotation(side * (0.6 + 0.5 * r), 3, nrm); up = rot @ (-tangent); right = rot @ bi
                base = p + nrm * (0.04 + 0.05 * r) + bi * side * 0.04
                vs = [leaves.verts.new(base + (right * x + up * y + nrm * (y * 0.35)) * sc) for x, y in LEAF]
                f = leaves.faces.new(vs); v = 0.75 + 0.4 * (1 - t) * (0.7 + 0.3 * r)
                for lp in f.loops: lp[lay] = (v, v, v, 1)
                if flowers and r > 1 - flowers:
                    c = base + nrm * 0.1
                    pv = [fl.verts.new(c + (bi * math.cos(k / 5 * math.tau) + tangent * math.sin(k / 5 * math.tau)) * 0.07 + nrm * 0.02) for k in range(5)]
                    ff = fl.faces.new(pv)
                    for lp in ff.loops: lp[flay] = (1, 1, 1, 1)
    asm.add('Ivy', leaves); asm.add('Flower', fl)

def drape(top, normal, length, wobble=0.15, kinks=6, phase=0.0):
    nrm = Vector(normal).normalized(); side = Vector((0, 0, 1)).cross(nrm).normalized(); pts = []
    for k in range(kinks + 1):
        t = k / kinks
        pts.append(tuple(Vector(top) + Vector((0, 0, -length * t)) + side * math.sin(t * 3.1 + phase) * wobble + nrm * 0.02 * math.sin(t * 5)))
    return pts, tuple(nrm)

def moss_mat(asm, top_center, sx, sy, rz, key, drip=True, flowers=3):
    """Moss grown on a stone's top face: an irregular outline inset from the edges, a thin cushion, and a flap that
    spills over one edge and hangs down the face (so it reads as growing on that stone, not sitting on it)."""
    bm = bmesh.new(); N = 14; pts = []
    for k in range(N):
        a = k / N * math.tau; r = 0.62 + 0.33 * hsh(key, k)
        pts.append((math.cos(a) * sx / 2 * r, math.sin(a) * sy / 2 * r))
    bot = [bm.verts.new((x, y, 0.0)) for x, y in pts]; topv = [bm.verts.new((x * 0.9, y * 0.9, 0.05 + 0.04 * hsh(key, i, 'h'))) for i, (x, y) in enumerate(pts)]
    bm.faces.new(topv)
    for i in range(N): bm.faces.new((bot[i], bot[(i + 1) % N], topv[(i + 1) % N], topv[i]))
    if drip:                                                     # a flap over the +x edge, hanging 0.15-0.4 m down the face
        side = 1 if hsh(key, 'side') > 0.5 else -1; w = sy * (0.35 + 0.3 * hsh(key, 'w')); hang = 0.15 + 0.25 * hsh(key, 'hang')
        x0 = side * sx / 2 + side * 0.03
        q = [bm.verts.new((x0, -w / 2, 0.04)), bm.verts.new((x0, w / 2, 0.04)), bm.verts.new((x0 + side * 0.02, w / 2 * 0.7, -hang)), bm.verts.new((x0 + side * 0.02, -w / 2 * 0.8, -hang * 0.8))]
        bm.faces.new(q if side > 0 else q[::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.transform(M4(top_center, rz))
    paint_bm(bm, top_center[2] - 0.4, top_center[2] + 0.1, top=1.2, bottom=0.7, key=key, wear=0, var=0.1); asm.add('Moss', bm)
    if flowers:
        fl = bmesh.new(); lay = fl.loops.layers.float_color.new('Col'); c0 = Vector(top_center) + Vector((0, 0, 0.08))
        for k in range(flowers):
            c = c0 + Vector(((hsh(key, k, 'fx') - 0.5) * sx * 0.4, (hsh(key, k, 'fy') - 0.5) * sy * 0.4, 0))
            for p in range(5):
                pa = p / 5 * math.tau
                f = fl.faces.new([fl.verts.new(c), fl.verts.new(c + Vector((math.cos(pa - 0.35), math.sin(pa - 0.35), 0.25)) * 0.09), fl.verts.new(c + Vector((math.cos(pa + 0.35), math.sin(pa + 0.35), 0.25)) * 0.09)])
                for lp in f.loops: lp[lay] = (1, 1, 1, 1)
        asm.add('Flower', fl)

def moss_clump(asm, loc, r, key, flowers=4):
    """A moss mat: a cluster of low, irregular lumps spreading over the surface (an uneven outline, not a disc), with
    lilies in tight clusters on the tallest lump."""
    for m in range(4):
        bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        for v in bm.verts:
            k = 1 + (hsh(key, m, round(v.co.x, 2), round(v.co.y, 2)) - 0.5) * 0.5; v.co *= k
        a = hsh(key, m, 'a') * math.tau; d = r * 0.55 * (m > 0) * (0.6 + 0.4 * hsh(key, m, 'd')); rr = r * (0.7 if m == 0 else 0.45 + 0.2 * hsh(key, m))
        c = (loc[0] + math.cos(a) * d, loc[1] + math.sin(a) * d, loc[2] - 0.04 * m)
        bm.transform(M4(c, hsh(key, m) * 6, s=(rr * 1.4, rr * 1.0, rr * (0.45 if m == 0 else 0.3))))
        paint_bm(bm, loc[2] - r * 0.4, loc[2] + r * 0.4, top=1.2, bottom=0.62, key=(key, m), wear=0, var=0.1); asm.add('Moss', bm)
    fl = bmesh.new(); lay = fl.loops.layers.float_color.new('Col')
    for k in range(flowers):
        a = hsh(key, k) * math.tau; d = r * 0.25 * hsh(key, k, 'd'); c = Vector(loc) + Vector((math.cos(a) * d, math.sin(a) * d, r * 0.3))
        for p in range(5):
            pa = p / 5 * math.tau
            vs = [fl.verts.new(c), fl.verts.new(c + Vector((math.cos(pa - 0.35), math.sin(pa - 0.35), 0.25)) * 0.09), fl.verts.new(c + Vector((math.cos(pa + 0.35), math.sin(pa + 0.35), 0.25)) * 0.09)]
            f = fl.faces.new(vs)
            for lp in f.loops: lp[lay] = (1, 1, 1, 1)
    asm.add('Flower', fl)

# ------------------------------------------------------------------ arches
def arch_wall(asm, mat, width, height, radius, spring, depth, loc, rz, key, pointed=False, zr=None, moss=0.4):
    """A wall with an arch opening cut through it, built directly (front and back strips between the arch curve and the
    outer rectangle, plus the reveal), so no boolean is needed."""
    bm = bmesh.new(); N = 24
    def arch_pt(t):                                               # t: 0 = left springer, 1 = right springer
        if not pointed: a = math.pi * (1 - t); return (math.cos(a) * radius, spring + math.sin(a) * radius)
        rr = radius * 1.8; cx = radius - rr
        if t <= 0.5: a = math.pi - math.acos((radius - (-cx)) / rr) * 0 ; ang = math.pi + math.atan2(0, 1)
        # pointed: two arcs of radius rr centred at +-(radius - rr)
        side = -1 if t < 0.5 else 1; u = t * 2 if t < 0.5 else (1 - t) * 2
        c = -side * (rr - radius); a0 = 0 if side > 0 else math.pi; amax = math.acos((rr - radius) / rr)
        ang = (math.pi - amax * u) if side < 0 else (amax * u)
        return (c + math.cos(ang) * rr, spring + math.sin(ang) * rr)
    def outer_pt(t):                                              # matching point on the outer rectangle (left, top, right)
        per = height * 2 + width; d = t * per
        if d < height: return (-width / 2, d)
        if d < height + width: return (-width / 2 + (d - height), height)
        return (width / 2, height - (d - height - width))
    rows = []
    for y in (-depth / 2, depth / 2):
        inner = [bm.verts.new((arch_pt(i / N)[0], y, arch_pt(i / N)[1])) for i in range(N + 1)]
        inner = [bm.verts.new((-radius, y, 0))] + inner + [bm.verts.new((radius, y, 0))]
        outer = [bm.verts.new((outer_pt(i / (N + 2))[0], y, outer_pt(i / (N + 2))[1])) for i in range(N + 3)]
        rows.append((inner, outer))
    for side, (inner, outer) in enumerate(rows):
        for i in range(len(inner) - 1):
            q = (inner[i], inner[i + 1], outer[i + 1], outer[i]); bm.faces.new(q if side else q[::-1])
    (fi, fo), (bi_, bo) = rows
    for i in range(len(fi) - 1): bm.faces.new((fi[i], bi_[i], bi_[i + 1], fi[i + 1]))           # reveal
    for i in range(len(fo) - 1): bm.faces.new((fo[i], fo[i + 1], bo[i + 1], bo[i]))            # outer skin
    bm.faces.new((fi[0], fo[0], bo[0], bi_[0])); bm.faces.new((fi[-1], bi_[-1], bo[-1], fo[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.transform(M4(loc, rz)); z0 = loc[2]
    paint_bm(bm, *(zr or (z0, z0 + height)), moss=moss, key=key, wear=0.0, var=0.0)
    asm.add(mat, bm)

def voussoirs(asm, center, rz, radius, thick, depth, count, key, mat='Stone', zr=None, pointed=False):
    """An arch of individual wedge stones with a raised keystone."""
    for i in range(count):
        a0, a1 = math.pi * i / count, math.pi * (i + 1) / count; g = 0.012; keyst = i == count // 2
        ro = radius + thick + (0.16 if keyst else 0) + (0.04 if i % 2 else 0)
        bm = bmesh.new(); vs = []
        for y in (-depth / 2 - (0.05 if keyst else 0), depth / 2 + (0.05 if keyst else 0)):
            for r, a in [(radius, a0 + g), (radius, a1 - g), (ro, a1 - g), (ro, a0 + g)]:
                ax, az = math.cos(a) * r, math.sin(a) * r
                if pointed: az *= 1.0 + 0.55 * (1 - abs(math.cos(a)))   # lift the crown into a point
                vs.append(bm.verts.new((ax, y, az)))
        for f in [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]: bm.faces.new([vs[k] for k in f])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm_bevel(bm, 0.035, 2); bm.transform(M4(center, rz))
        paint_bm(bm, *(zr or (center[2] - 1, center[2] + radius)), key=(key, i), top=1.12, bottom=0.8)
        asm.add('StoneDark' if keyst else mat, bm)

def coursed_pier(asm, base, rz, w, d, h, key, zr, plinth=True, capital=True, mat='Stone'):
    z = base[2]; ca, sa = math.cos(rz), math.sin(rz)
    def at(lx, lz): return (base[0] + ca * lx, base[1] + sa * lx, lz)
    if plinth: asm.stone('StoneDark', (w + 0.35, d + 0.35, 0.5), at(0, z + 0.25), (key, 'pl'), rz, zr=zr, bev=0.06); z += 0.5
    c = 0
    while z < base[2] + h - 0.35:
        hh = min(0.62 + 0.1 * hsh(key, c), base[2] + h - 0.3 - z); ww = w + (0.05 if c % 2 else 0)
        asm.stone(mat, (ww, d, hh - 0.02), at(0.03 * ((c * 7) % 3 - 1), z + hh / 2), (key, c), rz, zr=zr, bev=0.04); z += hh; c += 1
    if capital: asm.stone('StoneDark', (w + 0.3, d + 0.25, 0.3), at(0, base[2] + h - 0.15), (key, 'cap'), rz, zr=zr, bev=0.05, top=1.15)

# ------------------------------------------------------------------ rock: seven hand-drawn profiles, bastions, root
# each profile: rings of (height from top, radius factor x, y, lean x, y), a 12-sided plan, plan twist per ring, and a
# buttress lobe (offset, radius, height) so the silhouettes are not symmetric
SPIRES = [
    dict(rings=[(0, .95, .82, 0, 0), (-0.4, 1.05, .9, 0, 0), (-1.2, .82, .7, -.05, 0), (-3.4, .75, .66, -.1, .05), (-3.8, 1.02, .9, -.1, .05), (-4.2, .8, .7, -.12, .05),
                (-6.5, .7, .62, -.2, .1), (-9, .82, .7, -.3, .12), (-12, .95, .8, -.35, .15), (-16, 1.1, .92, -.4, .2)], buttress=((0.7, -0.4), 0.55, 9), crown='ruin'),
    dict(rings=[(0, .7, .6, .1, 0), (-0.6, .85, .7, .1, 0), (-2.0, .95, .8, .05, 0), (-2.3, 1.15, .95, .05, 0), (-2.7, .9, .75, 0, 0), (-5, .78, .7, -.1, 0),
                (-7.5, .72, .64, -.15, -.05), (-8, .95, .82, -.15, -.05), (-8.4, .8, .7, -.18, -.05), (-12, .9, .76, -.2, -.1), (-16, 1.05, .9, -.25, -.12)], buttress=((-0.8, 0.3), 0.5, 6), crown='broken'),
    dict(rings=[(0, 1.0, .75, 0, 0), (-0.5, 1.08, .8, 0, 0), (-2.5, .9, .7, .15, .05), (-5.5, .8, .62, .3, .1), (-6, 1.0, .8, .3, .1), (-6.4, .82, .66, .32, .1),
                (-10, .76, .62, .45, .15), (-13, .88, .7, .5, .18), (-16, 1.02, .85, .55, .2)], buttress=((0.2, 0.75), 0.45, 11), crown='tree'),
    dict(rings=[(0, .85, .85, 0, 0), (-0.3, .95, .95, 0, 0), (-1.8, .72, .72, 0, 0), (-2.2, .9, .9, 0, 0), (-2.6, .7, .7, 0, 0), (-4.5, .66, .66, .05, .05),
                (-4.9, .86, .86, .05, .05), (-5.3, .68, .68, .05, .05), (-9, .74, .74, .1, .1), (-13, .9, .9, .15, .15), (-16, 1.05, 1.05, .2, .2)], buttress=((-0.6, -0.6), 0.5, 7), crown='ruin'),
    dict(rings=[(0, .9, .7, 0, 0), (-0.8, .95, .75, 0, 0), (-1.2, .78, .6, 0, 0), (-4, .7, .58, -.2, .1), (-4.5, .92, .76, -.2, .1), (-7, .8, .66, -.3, .15),
                (-7.4, .95, .8, -.3, .15), (-7.8, .76, .62, -.32, .15), (-12, .86, .7, -.4, .2), (-16, 1.0, .82, -.45, .22)], buttress=((0.75, 0.2), 0.6, 12), crown='broken'),
    dict(rings=[(0, .75, .9, 0, 0), (-0.5, .85, 1.0, 0, 0), (-3, .75, .86, .1, -.1), (-3.3, .95, 1.08, .1, -.1), (-3.7, .78, .9, .12, -.1), (-6.5, .7, .8, .2, -.2),
                (-10, .8, .9, .28, -.28), (-16, .98, 1.1, .35, -.35)], buttress=((-0.2, -0.85), 0.5, 8), crown='tree'),
    dict(rings=[(0, 1.05, .9, 0, 0), (-0.3, 1.12, .96, 0, 0), (-1.0, .9, .78, 0, 0), (-2.8, .85, .74, .05, 0), (-3.2, 1.08, .94, .05, 0), (-3.6, .86, .74, .05, 0),
                (-6, .78, .68, .1, .05), (-6.4, 1.0, .86, .1, .05), (-6.8, .8, .7, .12, .05), (-11, .88, .76, .18, .1), (-16, 1.02, .88, .22, .12)], buttress=((0.5, 0.6), 0.5, 10), crown='ruin'),
]
PLAN = [1.00, 0.86, 1.10, 0.93, 0.80, 1.06, 0.97, 0.78, 1.04, 0.92, 1.12, 0.88]

def rock_lathe(rings, plan, twist_every=2, root=60.0, cap=True):
    bm = bmesh.new(); n = len(plan); prev = None; first = None
    full = list(rings) + [(rings[-1][0] - root * 0.4, rings[-1][1] * 1.15, rings[-1][2] * 1.15, rings[-1][3], rings[-1][4]),
                          (rings[-1][0] - root, rings[-1][1] * 0.25, rings[-1][2] * 0.25, rings[-1][3], rings[-1][4])]
    for ri, (z, rx, ry, ox, oy) in enumerate(full):
        tw = ri // twist_every; ring = []
        for k in range(n):
            a = k / n * math.tau + 0.17; m = plan[(k + tw) % n]
            ring.append(bm.verts.new((ox + math.cos(a) * rx * m, oy + math.sin(a) * ry * m, z)))
        if prev:
            for k in range(n): bm.faces.new((ring[k], ring[(k + 1) % n], prev[(k + 1) % n], prev[k]))
        else: first = ring
        prev = ring
    if cap: bm.faces.new(first)
    bm.faces.new(prev[::-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm

def moss_cap(bm, zfrom):
    """Moss over the top of a rock, thickest on up-facing faces, fading out below `zfrom` (tints the painted colours)."""
    lay = bm.loops.layers.float_color['Col']
    for f in bm.faces:
        for lp in f.loops:
            z = lp.vert.co.z
            if z < zfrom: continue
            k = min(1, (z - zfrom) / 2.5) * (0.35 + 0.65 * max(0, f.normal.z))
            c = lp[lay]; lp[lay] = (c[0] * (1 - k) + 0.52 * k, c[1] * (1 - k) + 0.80 * k, c[2] * (1 - k) + 0.40 * k, 1)

def spire_kit(i):
    """Kit spire i: the hand-drawn profile at unit radius, a buttress lobe leaning on it, moss on its ledges, a crown."""
    P = SPIRES[i]; asm = Asm(f'Spire{i}'); rad = 3.0
    rings = [(z, rx * rad, ry * rad, ox * rad, oy * rad) for z, rx, ry, ox, oy in P['rings']]
    bm = rock_lathe(rings, PLAN[i:] + PLAN[:i]); paint_bm(bm, -16, 0, top=1.16, bottom=0.42, moss=0.95, key=('sp', i), wear=0.05, var=0.0); moss_cap(bm, -5.0); asm.add('Rock', bm)
    (bx, by), br, bh = P['buttress']
    lobe = [(-bh * 0.45, br * 0.8, br * 0.7, 0, 0), (-bh * 0.5, br * 1.0, br * 0.85, 0, 0), (-bh * 0.8, br * 0.85, br * 0.75, 0.05, 0), (-bh, br * 1.0, br * 0.9, 0.1, 0.05)]
    lb = rock_lathe([(z, rx * rad, ry * rad, ox * rad, oy * rad) for z, rx, ry, ox, oy in lobe], PLAN[i + 3:] + PLAN[:i + 3], root=40)
    lb.transform(M4((bx * rad, by * rad, 0))); paint_bm(lb, -16, 0, top=1.14, bottom=0.55, moss=0.95, key=('lb', i), wear=0.05); asm.add('Rock', lb)
    for k, (z, rx, ry, ox, oy) in enumerate(rings):               # moss cushions on every overhanging ledge
        if k and rx > rings[k - 1][1] * 1.12 and z > -12:
            for m in range(3):
                a = (m / 3 + hsh(i, k)) * math.tau
                moss_clump(asm, (ox + math.cos(a) * rx * 0.92, oy + math.sin(a) * ry * 0.92, z + 0.1), 0.5, (i, k, m), flowers=2)
    top = rings[0]
    if P['crown'] == 'ruin':
        asm.stone('Stone', (1.4, 0.55, 1.6), (top[3] - 0.4, top[4] + 0.3, 0.8), ('cr', i, 0), 0.3, moss=0.6)
        asm.stone('StoneDark', (0.9, 0.7, 0.5), (top[3] + 0.6, top[4] - 0.3, 0.25), ('cr', i, 1), 1.1, moss=0.5)
        arch_wall(asm, 'Stone', 2.4, 2.8, 0.7, 1.5, 0.45, (top[3] - 0.2, top[4] - 0.6, 0), 0.4, ('cra', i), pointed=True, zr=(0, 2.8))
    elif P['crown'] == 'broken':
        for m in range(4): asm.stone('StoneDark', (0.8, 0.7, 0.6 + 0.3 * m % 1.0), (top[3] + math.cos(m * 1.7) * 1.2, top[4] + math.sin(m * 1.7) * 1.0, 0.35), ('cb', i, m), m * 0.7, ry=0.2 * (m % 2), moss=0.6)
    for m in range(4):                                            # moss cushions on the crown
        a = (m / 4 + 0.13 * i) * math.tau
        moss_clump(asm, (top[3] + math.cos(a) * top[1] * 0.6, top[4] + math.sin(a) * top[2] * 0.6, 0.05), 0.75, ('cm', i, m), flowers=3)
    for m in range(3):                                            # ivy over the crown edge
        a = (m / 3 + 0.21 * i) * math.tau; nx, ny = math.cos(a), math.sin(a)
        ivy(asm, [drape((top[3] + nx * top[1] * 0.98, top[4] + ny * top[2] * 0.98, -0.2), (nx, ny, 0), 3 + 2.5 * hsh(i, m), 0.12, 6, m * 1.3)], key=(i, m), flowers=0.05)
    return asm.build(KIT)

BASTIONS = [
    [(0, 1.0, .92, 0, 0), (-0.6, 1.04, .95, 0, 0), (-2.5, .9, .82, .05, 0), (-3, .98, .88, .05, 0), (-6, .7, .64, .1, .05), (-10, .5, .45, .15, .08), (-14, .3, .28, .2, .1)],
    [(0, 1.0, .95, 0, 0), (-1.2, .92, .86, 0, 0), (-1.6, 1.0, .92, 0, 0), (-4.5, .78, .72, -.1, 0), (-8, .58, .52, -.15, -.05), (-12, .38, .34, -.2, -.08)],
    [(0, 1.0, .9, 0, 0), (-0.8, .98, .9, 0, 0), (-3.5, .82, .74, .08, .08), (-3.9, .9, .8, .08, .08), (-7, .62, .56, .12, .12), (-11, .42, .38, .16, .16)],
]
def bastion_kit(i, rad):
    asm = Asm(f'Bastion{i}')
    bm = rock_lathe([(z, rx * rad, ry * rad, ox * rad, oy * rad) for z, rx, ry, ox, oy in BASTIONS[i]], PLAN[i * 2:] + PLAN[:i * 2], root=8)
    paint_bm(bm, -14, 0, top=1.14, bottom=0.5, moss=0.95, key=('ba', i), wear=0.05); asm.add('Rock', bm)
    for m in range(5):
        a = m / 5 * math.tau + i
        moss_clump(asm, (math.cos(a) * rad * 0.8, math.sin(a) * rad * 0.75, 0.05), 0.7, ('bm', i, m), flowers=3)
        ivy(asm, [drape((math.cos(a + 0.6) * rad * 0.99, math.sin(a + 0.6) * rad * 0.95, -0.2), (math.cos(a + 0.6), math.sin(a + 0.6), 0), 2.5 + 2 * hsh(i, m), 0.1, 6, m)], key=('bi', i, m))
    return asm.build(KIT)

# ------------------------------------------------------------------ trees: three types, leaf clusters placed by hand
LEAFC = [(0, 0), (0.1, 0.06), (0.13, 0.2), (0.07, 0.34), (0, 0.4), (-0.07, 0.34), (-0.13, 0.2), (-0.1, 0.06)]
TREES = [
    dict(trunk=[(0, 0, 0), (0.25, 0.1, 1.3), (-0.15, 0.25, 2.5), (0.1, 0.0, 3.4)], width=0.34,
         clusters=[(0, 0, 4.2, 1.5), (1.3, 0.4, 3.7, 1.1), (-1.2, 0.5, 3.8, 1.15), (0.3, -1.2, 3.6, 1.05), (-0.4, 1.0, 4.6, 0.95), (0.9, -0.6, 4.5, 0.9)]),
    dict(trunk=[(0, 0, 0), (0.5, 0.0, 1.0), (1.2, 0.1, 2.0), (1.6, 0.2, 2.8)], width=0.3,
         clusters=[(1.7, 0.2, 3.5, 1.3), (2.6, 0.3, 3.0, 0.95), (0.9, -0.5, 3.2, 0.9), (1.4, 0.9, 3.9, 0.85), (2.2, -0.4, 3.8, 0.8)]),
    dict(trunk=[(0, 0, 0), (-0.1, 0.1, 0.8), (0.05, 0.0, 1.5)], width=0.22,
         clusters=[(0, 0, 2.0, 1.0), (0.7, 0.2, 1.7, 0.75), (-0.6, -0.3, 1.8, 0.7), (0.1, 0.6, 2.4, 0.6)]),
]
def tree_kit(i):
    D = TREES[i]; asm = Asm(f'Tree{i}')
    cu = bpy.data.curves.new('trunk', 'CURVE'); cu.dimensions = '3D'; sp = cu.splines.new('BEZIER'); sp.bezier_points.add(len(D['trunk']) - 1)
    for p, c in zip(sp.bezier_points, D['trunk']):
        p.co = c; p.handle_left_type = p.handle_right_type = 'AUTO'
    for k, p in enumerate(sp.bezier_points): p.radius = 1.0 - 0.55 * k / max(1, len(D['trunk']) - 1)
    cu.bevel_depth = D['width']; cu.bevel_resolution = 3; cu.resolution_u = 6; cu.use_fill_caps = True
    ob = bpy.data.objects.new('trunk', cu); scene.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get())); bpy.data.objects.remove(ob)
    bm = bmesh.new(); bm.from_mesh(me); bpy.data.meshes.remove(me); paint_bm(bm, 0, 3.5, top=1.0, bottom=0.7, key=('tr', i)); asm.add('Bark', bm)
    for ci, (cx, cy, cz, cr) in enumerate(D['clusters']):
        core = bmesh.new(); bmesh.ops.create_icosphere(core, subdivisions=2, radius=cr * 0.38)
        core.transform(M4((cx, cy, cz - cr * 0.1), s=(1, 1, 0.8))); paint_bm(core, cz - cr, cz + cr, top=1.2, bottom=0.6, key=('tc', i, ci), wear=0); asm.add('CanopyDark', core)
        leaves = bmesh.new(); lay = leaves.loops.layers.float_color.new('Col'); N = int(130 * cr * cr)
        for k in range(N):                                        # golden-spiral placement over the cluster
            y = 1 - 2 * (k + 0.5) / N; rr = math.sqrt(1 - y * y); th = k * 2.39996
            nrm = Vector((math.cos(th) * rr, math.sin(th) * rr, y * 0.85)).normalized()
            if nrm.z < -0.65: continue                            # open underside
            pos = Vector((cx, cy, cz)) + nrm * cr * (0.82 + 0.18 * hsh(i, ci, k))
            up = Vector((0, 0, 1)) if abs(nrm.z) < 0.9 else Vector((1, 0, 0)); right = up.cross(nrm).normalized(); upv = nrm.cross(right)
            rot = Matrix.Rotation(hsh(i, ci, k, 'r') * math.tau, 3, nrm); right, upv = rot @ right, rot @ upv
            sc = 2.2 + 1.0 * hsh(i, ci, k, 's')
            vs = [leaves.verts.new(pos + (right * x + upv * yv + nrm * yv * 0.3) * sc) for x, yv in LEAFC]
            f = leaves.faces.new(vs); v = 0.55 + 0.6 * max(0, nrm.z * 0.5 + 0.5)        # lit tops, darker undersides
            for lp in f.loops: lp[lay] = (v, v, v, 1)
        asm.add('CanopyLeaf', leaves)
    return asm.build(KIT)

# ------------------------------------------------------------------ lantern, bell, small kit
def lantern_kit():
    asm = Asm('LanternKit')
    for k in range(6):
        a = k / 6 * math.tau; bm = bm_box(0.035, 0.035, 0.44); bm.transform(M4((math.cos(a) * 0.15, math.sin(a) * 0.15, 0))); paint_bm(bm, -0.3, 0.3, grime=False, key=k); asm.add('LanternMetal', bm)
    for z in (-0.22, 0.22):
        bm = bmesh.new(); bmesh.ops.create_circle(bm, cap_ends=True, segments=12, radius=0.19); bm.transform(M4((0, 0, z)))
        bmesh.ops.extrude_face_region(bm, geom=bm.faces[:]); bmesh.ops.translate(bm, vec=(0, 0, 0.04), verts=[v for v in bm.verts if v.co.z > z + 0.001 or abs(v.co.z - z) < 1e-6 and False])
        paint_bm(bm, -0.3, 0.3, grime=False, key=z); asm.add('LanternMetal', bm)
    cap = bmesh.new(); bmesh.ops.create_cone(cap, cap_ends=True, segments=12, radius1=0.22, radius2=0.02, depth=0.2); cap.transform(M4((0, 0, 0.33)))
    paint_bm(cap, 0, 0.5, grime=False, key='cap'); asm.add('BrassDark', cap)
    core = bmesh.new(); bmesh.ops.create_icosphere(core, subdivisions=2, radius=0.1); core.transform(M4(s=(1, 1, 1.5))); paint_bm(core, 0, 1, grime=False, wear=0); asm.add('GlowAmber', core)
    return asm.build(KIT)

def bell_kit():
    prof = [(0.001, 1.00), (0.18, 0.99), (0.30, 0.94), (0.36, 0.84), (0.38, 0.70), (0.40, 0.52), (0.45, 0.34), (0.53, 0.18), (0.62, 0.07), (0.66, 0.02), (0.64, 0.0), (0.56, 0.03), (0.50, 0.10)]
    bm = bmesh.new(); vs = [bm.verts.new((r, 0, h - 1.0)) for r, h in prof]
    for i in range(len(vs) - 1): bm.edges.new((vs[i], vs[i + 1]))
    bmesh.ops.spin(bm, geom=bm.verts[:] + bm.edges[:], axis=(0, 0, 1), cent=(0, 0, 0), steps=48, angle=math.tau)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    paint_bm(bm, -1, 0, top=1.15, bottom=0.85, grime=True, key='bell', wear=0)
    for z, r in [(-0.82, 0.6), (-0.70, 0.53)]:
        t = bmesh.new(); bmesh.ops.create_circle(t, segments=48, radius=r); t.transform(M4((0, 0, z)))
        e = bmesh.ops.extrude_edge_only(t, edges=t.edges[:]); bmesh.ops.translate(t, vec=(0, 0, 0.05), verts=[g for g in e['geom'] if isinstance(g, bmesh.types.BMVert)])
        bmesh.ops.scale(t, vec=(1.0, 1.0, 1.0), verts=t.verts); paint_bm(t, -1, 0, grime=False, key=z); bm_merge(bm, t, Matrix.Identity(4))
    me = bpy.data.meshes.new('BellMesh'); bm.to_mesh(me); bm.free(); me.materials.append(material('Bell'))
    ob = bpy.data.objects.new('BellMesh', me); KIT.objects.link(ob)
    sub = ob.modifiers.new('S', 'SUBSURF'); sub.levels = 1
    return ob

def fern_kit():
    asm = Asm('FernKit'); bm = bmesh.new(); lay = bm.loops.layers.float_color.new('Col')
    for k in range(9):                                            # fronds arching out from the crown
        a = k / 9 * math.tau + 0.3; L = 0.55 + 0.15 * hsh('f', k); pts = []
        for s in range(7):
            t = s / 6; r = L * t; z = 0.35 * math.sin(t * math.pi * 0.8)
            pts.append(Vector((math.cos(a) * r, math.sin(a) * r, z)))
        for s in range(6):
            p0, p1 = pts[s], pts[s + 1]; side = Vector((-math.sin(a), math.cos(a), 0)); w = 0.09 * (1 - s / 7)
            vs = [bm.verts.new(p0 - side * w), bm.verts.new(p0 + side * w), bm.verts.new(p1 + side * w * 0.8), bm.verts.new(p1 - side * w * 0.8)]
            f = bm.faces.new(vs); v = 0.7 + 0.3 * (s / 6)
            for lp in f.loops: lp[lay] = (v, v, v, 1)
    asm.add('Fern', bm); return asm.build(KIT)

# ------------------------------------------------------------------ the platform: curb, brass band, drum, arcades, root
def platform():
    asm = Asm('Rim'); zr = (-0.1, 0.6); n = 64
    for i in range(n):
        a = (i + 0.5) / n * math.tau
        if near_exit(a, GAP): continue
        v = hsh('curb', i); sunk = v < 0.12; h = 0.32 if sunk else 0.5 + 0.08 * hsh('ch', i); w = math.tau * (R + 0.4) / n - 0.06
        p = (math.cos(a) * (R + 0.4), -math.sin(a) * (R + 0.4), h / 2 - 0.05)
        asm.stone('Stone' if v > 0.4 else 'StoneDark', (0.8, w, h), p, ('curb', i), rz=-a, rx=(hsh('cr', i) - 0.5) * 0.06, zr=zr, moss=0.5, chips=2, bev=0.06)
        if hsh('cm', i) < 0.6: moss_mat(asm, (p[0], p[1], h - 0.05), 0.8, w, -a, ('cm', i), flowers=3 if hsh('cf', i) < 0.7 else 0)
        ro = R + 0.85; ox, oy = math.cos(a), -math.sin(a); tx, ty = -oy, ox
        ivy(asm, [drape((ox * ro + tx * off, oy * ro + ty * off, h - 0.1), (ox, oy, 0), 1.5 + 5.5 * hsh('cil', i, off), 0.14, 7, i + off) for off in (-0.45, 0.0, 0.45) if hsh('ci', i, off) < 0.8], key=('ci', i), flowers=0.1)
    # brass band on the curb's outer top edge, one arc per quadrant, laid as short worn segments
    for e in EXITS:
        t0, t1 = e + GAP, e + math.pi / 2 - GAP; segs = 22
        for k in range(segs):
            a = t0 + (t1 - t0) * (k + 0.5) / segs; L = (t1 - t0) * (R + 0.82) / segs
            bm = bm_box(0.06, L * 0.98, 0.09); bm_bevel(bm, 0.015, 1); bm.transform(M4((math.cos(a) * (R + 0.82), -math.sin(a) * (R + 0.82), 0.38), -a))
            paint_bm(bm, 0, 1, grime=False, key=('bb', k), wear=0.2); asm.add('Brass', bm)
    # drum wall under the curb: six courses of cut stone, staggered
    for c in range(6):
        z = -0.2 - c * 0.5 - 0.25; m = 78; off = 0.5 if c % 2 else 0.0
        for i in range(m):
            a = (i + off) / m * math.tau; rr = R + 0.85 - c * 0.03; w = math.tau * rr / m - 0.05
            p = (math.cos(a) * (rr - 0.25), -math.sin(a) * (rr - 0.25), z)
            asm.stone('StoneDark', (0.5, w, 0.47), p, ('dr', c, i), rz=-a, zr=(-3.6, 0.4), chips=1, bev=0.04, moss=0.0, bottom=0.42, top=1.05)
    obs = asm.build(ART)
    # arcade bays under the rim, between bridges and belfries (a collapsed bay now and then)
    arc = Asm('RimArcade'); segs = 24; rad = R + 1.15
    for i in range(segs):
        a = (i + 0.5) / segs * math.tau
        if near_exit(a, GAP + 0.08) or near_diag(a, 0.14): continue
        if hsh('arc', i) < 0.15: continue
        span = math.tau * rad / segs; c = Vector((math.cos(a) * rad, -math.sin(a) * rad, -3.3)); rz = -a + math.pi / 2
        arcade_bay(arc, c, rz, span, 3.4, 0.6, ('rimarc', i), zr=(-9.5, -3.3), pier_w=0.6, depth=0.55, spring=2.6, top_wall=True, down=True)
        o2 = Vector((math.cos(a), -math.sin(a), 0)); t2 = Vector((math.sin(a), math.cos(a), 0))
        ivy(arc, [drape(c + o2 * 0.32 + t2 * (k - 1.5) * span * 0.28, tuple(o2), 1.5 + 3.0 * hsh('rai', i, k), 0.14, 7, k) for k in range(4) if hsh('ra', i, k) < 0.7], key=('rai', i), flowers=0.08)
    obs += arc.build(ART)
    # the rock root the platform stands on
    root = Asm('PlatformRoot')
    rings = [(-3.2, R + 0.7, R + 0.7, 0, 0), (-4.0, R + 0.4, R + 0.4, 0, 0), (-5.5, R - 0.6, R - 0.6, 0, 0), (-6.0, R - 0.2, R - 0.2, 0, 0), (-8.5, R - 2.2, R - 2.2, .3, 0),
             (-12, 11.5, 11.2, .5, .2), (-16, 9, 8.8, .6, .3), (-22, 6.5, 6.3, .7, .3), (-29, 4.0, 3.8, .7, .3), (-36, 1.8, 1.7, .6, .2)]
    plan = (PLAN * 4)[:40]
    bm = rock_lathe(rings, plan, twist_every=1, root=6, cap=False); paint_bm(bm, -36, -3.2, top=1.1, bottom=0.45, moss=0.6, key='root', wear=0.04)
    root.add('Rock', bm); obs += root.build(ART)
    return obs

def arcade_bay(asm, center, rz, span, height, thick, key, zr, pier_w=1.2, depth=2.0, spring=None, top_wall=True, down=False, pointed=False):
    """One arcade bay: two coursed piers, a voussoir arch and the wall above it. `center` is the top-middle of the bay."""
    spring = spring if spring is not None else height * 0.55
    radius = span / 2 - pier_w / 2 - 0.05; base = center - Vector((0, 0, height)); ca, sa = math.cos(rz), math.sin(rz)
    for sx in (-1, 1):
        coursed_pier(asm, base + Vector((ca * sx * (span / 2 - pier_w / 2), sa * sx * (span / 2 - pier_w / 2), 0)), rz, pier_w, depth, spring, (key, sx), zr, plinth=not down)
    voussoirs(asm, base + Vector((0, 0, spring)), rz, radius, thick, depth, 11, key, zr=zr, pointed=pointed)
    if top_wall:
        w = span; hh = height - spring
        arch_wall(asm, 'Stone', w, hh, radius + thick, 0, depth * 0.85, base + Vector((0, 0, spring)), rz, (key, 'w'), pointed=pointed, zr=zr)

# ------------------------------------------------------------------ bridges
def bridge(B):
    a = B['a']; entry = B['entry']; asm = Asm('EntryBridge' if entry else 'Bridge'); x0, ln = B['x0'], B['len']; x1 = x0 + ln; hw = BRIDGE_W / 2
    ca, sa = math.cos(a), -math.sin(a); rz = -a                     # bridge axis in Blender (three z -> -y)
    def P(lx, ly, z): return (ca * lx - sa * ly * -1 * -1 * 0 + (-sa) * 0 + ca * 0 + 0, 0, 0)
    def W(lx, lz_off, z):                                          # local (along, across, up) -> world
        return (ca * lx + math.sin(a) * lz_off * -1 * -1 * 0 + (-(-math.sin(a))) * 0, 0, z)
    ux = Vector((math.cos(a), -math.sin(a), 0)); uy = Vector((math.sin(a), math.cos(a), 0))   # along, across (left-handed in Blender)
    def at(lx, ly, z): v = ux * lx + uy * ly; return (v.x, v.y, z)
    zr = (-8, 0.6)
    k = 0; x = x0
    while x < x1 - 0.05:                                           # deck slabs, two per row, staggered
        L = min(1.05 + 0.25 * hsh('dk', a, k), x1 - x)
        for side, w in ((-1, 2.1 + 0.3 * hsh('dw', a, k)), (1, None)):
            ww = w if w else BRIDGE_W - (2.1 + 0.3 * hsh('dw', a, k)); cy = -hw + ww / 2 if side < 0 else hw - ww / 2
            asm.stone('Deck', (L - 0.03, ww - 0.03, 0.6), at(x + L / 2, cy, -0.3), ('dk', a, k, side), rz, zr=zr, chips=1, bev=0.03, top=1.05, bottom=0.9)
        x += L; k += 1
    for ly in (-hw + 0.18, 0, hw - 0.18):                          # brass strips (edges and centre line)
        bm = bm_box(ln, 0.12, 0.03); bm_bevel(bm, 0.01, 1); bm.transform(M4(at(x0 + ln / 2, ly, 0.012), rz)); paint_bm(bm, 0, 1, grime=False, key=('bs', a, ly), wear=0.2); asm.add('Brass', bm)
    for ly in (-hw + 0.17, hw - 0.17):                             # parapets or entry posts
        if not entry:
            xx = x0 + 0.4; j = 0
            while xx < x1 - 0.6:
                if hsh('pp', a, ly, j) > 0.15:
                    asm.stone('Stone', (1.2, 0.34, 0.48 + 0.08 * hsh('ph', a, j)), at(xx + 0.6, ly, 0.24), ('pp', a, ly, j), rz, zr=zr, moss=0.6, chips=2)
                    if hsh('pm', a, ly, j) < 0.5: moss_mat(asm, at(xx + 0.6, ly, 0.47), 1.2, 0.34, rz, ('pm', a, ly, j), flowers=2)
                xx += 1.3; j += 1
        else:
            xx = x0 + 1.2; j = 0
            while xx < x1 - 0.5:
                asm.stone('Stone', (0.42, 0.42, 1.15), at(xx, ly, 0.57), ('ep', ly, j), rz, zr=zr, chips=1)
                cone = bmesh.new(); bmesh.ops.create_cone(cone, cap_ends=True, segments=4, radius1=0.3, radius2=0.0, depth=0.32)
                cone.transform(M4(at(xx, ly, 1.32), rz + math.pi / 4)); paint_bm(cone, 0, 2, grime=False, key=('cap', ly, j)); asm.add('Brass', cone)
                if xx + 1.5 < x1: asm.stone('StoneDark', (2.5, 0.3, 0.42), at(xx + 1.5, ly, 0.2), ('er', ly, j), rz, zr=zr, chips=1)
                xx += 3; j += 1
    # arcade walls under both deck edges, coursed piers dropping into the cloud
    bays = max(2, round(ln / 5.2)); span = ln / bays
    for ly in (-hw + 0.3, hw - 0.3):
        for b in range(bays):
            c = Vector(at(x0 + (b + 0.5) * span, ly, -0.7)); arcade_bay(asm, c, rz, span, 7.0, 0.55, ('ba', a, ly, b), zr=(-8, 0), pier_w=1.0, depth=0.6, spring=3.6)
    for b in range(bays + 1):
        coursed_pier(asm, Vector(at(x0 + b * span, 0, -34)), rz, 1.7, 1.7, 26.6, ('bp', a, b), (-34, -7.4), plinth=False)
    # ivy off the deck edges
    for j, xx in enumerate([x0 + 1 + i * 1.7 for i in range(int(ln / 1.7))]):
        for ly in (-hw, hw):
            if hsh('bi', a, j, ly) < 0.55: ivy(asm, [drape(at(xx, ly + (0.05 if ly > 0 else -0.05), -0.2), tuple(uy * (1 if ly > 0 else -1)), 1.2 + 3.5 * hsh('bil', a, j, ly), 0.13, 6, j)], key=('bi', a, j, ly), flowers=0.07)
    if not entry:                                                  # ruined gothic gate arch at the far end
        gx = x1 - 0.4
        for side in (-1, 1):
            coursed_pier(asm, Vector(at(gx, side * (hw + 0.45), 0)), rz + math.pi / 2, 0.9, 0.9, 3.4, ('gj', a, side), (0, 6.5), plinth=True, capital=True)
        voussoirs(asm, Vector(at(gx, 0, 3.4)), rz + math.pi / 2, hw + 0.05, 0.6, 0.9, 13, ('gv', a), zr=(0, 6.5), pointed=True)
        asm.stone('StoneDark', (1.0, 1.4, 1.5), at(gx, -1.6, 6.2), ('gc', a), rz, ry=0.25, zr=(0, 6.5), moss=0.7)
        for side in (-1, 1):
            ivy(asm, [drape(at(gx - 0.5, side * (hw + 0.5), 5.4), tuple(-ux), 4.2 + hsh('gi', a, side), 0.14, 7, side)], key=('gi', a, side), flowers=0.1)
    else:                                                          # arrival threshold posts at the rim
        for side in (-1, 1):
            asm.stone('Stone', (0.6, 0.6, 1.4), at(x0 + 0.3, side * (hw + 0.2), 0.7), ('th', side), rz, zr=zr)
            cone = bmesh.new(); bmesh.ops.create_cone(cone, cap_ends=True, segments=4, radius1=0.42, radius2=0.0, depth=0.4); cone.transform(M4(at(x0 + 0.3, side * (hw + 0.2), 1.6), rz + math.pi / 4))
            paint_bm(cone, 0, 2, grime=False, key=('thc', side)); asm.add('Brass', cone)
    return asm.build(ART)

# ------------------------------------------------------------------ belfries
def belfry(Bf, bell_src):
    front = Bf['front']; W = 3.2; solid = 1.4 if front else 2.2; cham = 3.6 if front else 4.4
    o = T(Bf['p']); rz = Bf['rotY']; asm = Asm(f"Belfry{Bf['idx']}"); zr = (o.z, o.z + solid + cham + 1.2)
    ca, sa = math.cos(rz), math.sin(rz)
    def at(lx, ly, lz): return (o.x + ca * lx - sa * ly, o.y + sa * lx + ca * ly, o.z + lz)
    asm.stone('StoneDark', (W + 0.5, W + 0.5, 0.5), at(0, 0, 0.25), ('bp', Bf['idx']), rz, zr=zr, bev=0.08)
    # shaft: solid base of coursed blocks, then four piers framing pointed arch openings
    z = 0.5; c = 0
    while z < 0.5 + solid - 0.05:
        hh = min(0.55, 0.5 + solid - z)
        for f in range(4):
            fa = rz + f * math.pi / 2; fx, fy = math.cos(fa - rz), math.sin(fa - rz)
            asm.stone('Stone', (W - 0.1 if f % 2 == 0 else 0.45, 0.45 if f % 2 == 0 else W - 1.0, hh - 0.02), at(fy * (W / 2 - 0.22) if f % 2 else 0, -fx * (W / 2 - 0.22) if f % 2 == 0 else 0, z + hh / 2), ('bs', Bf['idx'], c, f), rz, zr=zr, chips=1, bev=0.04)
        z += hh; c += 1
    asm.stone('StoneDark', (W + 0.25, W + 0.25, 0.22), at(0, 0, 0.5 + solid + 0.05), ('sc', Bf['idx']), rz, zr=zr, top=1.15)
    for f in range(4):                                              # chamber faces: arch wall with a pointed opening
        arch_wall(asm, 'Stone', W, cham, 0.85, cham * 0.42, 0.42, at(*[v * (W / 2 - 0.21) for v in (math.sin(f * math.pi / 2), -math.cos(f * math.pi / 2))], 0.5 + solid + 0.16), rz + f * math.pi / 2, ('bw', Bf['idx'], f), pointed=True, zr=zr, moss=0.3)
    for zc in range(int((solid + cham) / 1.0)):                     # corner quoins, alternating long and short
        for cx, cy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            lng = (zc + (cx + cy) // 2) % 2 == 0; sx, sy = (0.9, 0.55) if lng else (0.55, 0.9)
            asm.stone('StoneDark', (sx, sy, 0.5), at(cx * (W / 2 - sx / 2 + 0.06), cy * (W / 2 - sy / 2 + 0.06), 0.8 + zc * 1.0), ('q', Bf['idx'], zc, cx, cy), rz, zr=zr, chips=1, bev=0.06)
    ztop = 0.5 + solid + cham + 0.16
    asm.stone('StoneDark', (W + 0.45, W + 0.45, 0.35), at(0, 0, ztop + 0.17), ('co', Bf['idx']), rz, zr=zr, moss=0.6, top=1.15)
    pattern = [1.0, 0.65, 0.0, 0.4, 0.85, 0.3]                       # a broken parapet: heights per block along each side
    for f in range(4):
        fa = f * math.pi / 2; nx, ny = math.sin(fa), -math.cos(fa); tx, ty = -ny, nx
        for b, ph in enumerate(pattern[f % 2:] + pattern[:f % 2]):
            hh = ph * (0.85 if front else 1.25) * (0.8 + 0.4 * hsh('bc', Bf['idx'], f, b))
            if hh < 0.15: continue
            off = (b - 2.5) * 0.6; lx = nx * (W / 2 + 0.05) + tx * off; ly = ny * (W / 2 + 0.05) + ty * off
            for course in range(1 if hh < 0.6 else 2):
                ch = hh if hh < 0.6 else hh / 2
                asm.stone('Stone', (0.58, 0.42, ch - 0.02), at(lx, ly, ztop + 0.35 + course * ch + ch / 2), ('bc', Bf['idx'], f, b, course), rz + fa, zr=zr, moss=0.85, chips=2)
            if hh > 0.5 and hsh('bcm', Bf['idx'], f, b) < 0.5: moss_mat(asm, at(lx, ly, ztop + 0.35 + hh - 0.03), 0.58, 0.42, rz + fa, ('bcm', Bf['idx'], f, b), flowers=2)
    if not front:                                                   # timber frame standing on the broken top
        for cx, cy in ((-1, -1), (1, -1), (1, 1), (-1, 1)): asm.stone('Timber', (0.18, 0.18, 1.4), at(cx * 1.1, cy * 1.1, ztop + 1.05), ('tf', Bf['idx'], cx, cy), rz, zr=zr, bev=0.02, bottom=0.8)
        for ang in (0, math.pi / 2): asm.stone('Timber', (2.5, 0.16, 0.16), at(0, 0, ztop + 1.7), ('tb', Bf['idx'], ang), rz + ang, zr=zr, bev=0.02, bottom=0.85)
    bz = 0.5 + solid + cham - 0.4
    asm.stone('Timber', (W - 0.5, 0.26, 0.26), at(0, 0, bz), ('beam', Bf['idx']), rz, zr=zr, bev=0.03, top=1.0, bottom=0.85)
    for sx in (-1, 1): asm.stone('Timber', (0.16, 0.16, 1.3), at(sx * (W / 2 - 0.75), 0, bz - 0.45), ('br', Bf['idx'], sx), rz, ry=sx * 0.65, zr=zr, bev=0.02)
    for k in range(6):                                              # chain down to the bell
        t = bmesh.new(); bmesh.ops.create_circle(t, segments=10, radius=0.055); e = bmesh.ops.extrude_edge_only(t, edges=t.edges[:])
        bmesh.ops.scale(t, vec=(0.7, 0.7, 1), verts=[g for g in e['geom'] if isinstance(g, bmesh.types.BMVert)])
        t.transform(M4(at(0, 0, bz - 0.15 - k * 0.1), rz + (k % 2) * math.pi / 2, rx=math.pi / 2)); paint_bm(t, 0, 1, grime=False, key=k); asm.add('LanternMetal', t)
    for m in range(9 if front else 6):                              # moss and lilies on the cornice
        a = m / (9 if front else 6) * math.tau + 0.4
        moss_clump(asm, at(math.cos(a) * 1.75, math.sin(a) * 1.75, ztop + 0.38), 0.45, ('bmc', Bf['idx'], m), flowers=4)
    strands = []                                                    # ivy on the piers, not across the openings
    for f in range(4):
        fa = f * math.pi / 2; nx, ny = math.sin(fa), -math.cos(fa)
        for off in (-1.35, -0.75, 0.75, 1.35):
            p = at(nx * (W / 2 + 0.07) + ny * off * 0, ny * (W / 2 + 0.07) - nx * off * 0, 0)
            lx = nx * (W / 2 + 0.07) - ny * off; ly = ny * (W / 2 + 0.07) + nx * off
            if hsh('bvi', Bf['idx'], f, off) < 0.85:
                strands.append(drape(at(lx, ly, ztop + 0.2), (ca * nx - sa * ny, sa * nx + ca * ny, 0), 1.5 + (solid + cham) * hsh('bvl', Bf['idx'], f, off), 0.12, 7, f + off))
    ivy(asm, strands, key=('bvi', Bf['idx']), flowers=0.08)
    # a broken arch fragment flanking the tower along the rim
    side = 1 if Bf['idx'] % 2 else -1
    arch_wall(asm, 'Stone', 2.6, 2.4 if front else 3.6, 0.75, 0.9 if front else 1.6, 0.5, at(side * 3.0, 0.9, 0), rz, ('bfa', Bf['idx']), pointed=True, zr=zr)
    obs = asm.build(ART)
    pivot = bpy.data.objects.new('Bell', None); ART.objects.link(pivot); pivot.location = at(0, 0, bz - 0.15)
    b = bell_src.copy(); ART.objects.link(b); b.parent = pivot; b.matrix_parent_inverse = Matrix.Identity(4); b.location = (0, 0, -0.55)
    s = 1.25 if front else 1.45; b.scale = (s, s, s)
    return obs

# ------------------------------------------------------------------ aqueducts
def aqueduct(A):
    cx, cz = A['c']; o = Vector((cx, -cz, 0)); rz = A['rotY']; asm = Asm('Aqueduct'); ln, bays, h = A['len'], A['bays'], A['h']; bay = ln / bays
    ux = Vector((math.cos(rz), math.sin(rz), 0)); uy = Vector((-math.sin(rz), math.cos(rz), 0))
    def at(lx, ly, z): v = o + ux * lx + uy * ly; return (v.x, v.y, z)
    zr = (A['topY'] - h - 6, A['topY'] + 1)
    for k in range(bays):
        px = -ln / 2 + k * bay; top = A['tops'][k]
        if k in A['fallen']:                                        # a collapsed span: stubs and fallen blocks
            for m in range(4):
                asm.stone('StoneDark', (0.8 + 0.5 * hsh('fb', cx, k, m), 1.2 + hsh('fw', cx, k, m), 0.6 + 0.4 * hsh('fh', cx, k, m)), at(px + bay * (0.25 + 0.5 * hsh('fx', cx, k, m)), (hsh('fy', cx, k, m) - 0.5) * 1.5, top - h + h * 0.45 - 6 * hsh('fz', cx, k, m)), ('fb', cx, k, m), rz + m, rx=(hsh('frx', k, m) - 0.5) * 1.0, ry=(hsh('fry', k, m) - 0.5) * 1.2, zr=zr)
            continue
        c = Vector(at(px + bay / 2, 0, top)); arcade_bay(asm, c, rz, bay, h, 0.62, ('aq', cx, k), zr, pier_w=1.3, depth=2.2, spring=h * 0.56, pointed=A['pointed'])
        asm.stone('StoneDark', (bay + 0.3, 2.9, 0.35), at(px + bay / 2, 0, top + 0.17), ('acn', cx, k), rz, zr=zr, moss=0.7, top=1.15)
        for sy in (-1, 1): asm.stone('Stone', (bay + 0.1, 0.35, 0.45), at(px + bay / 2, sy * 1.15, top + 0.57), ('acw', cx, k, sy), rz, zr=zr, moss=0.8)
        water = bm_box(bay, 1.9, 0.06); water.transform(M4(at(px + bay / 2, 0, top + 0.6), rz)); paint_bm(water, 0, 1, grime=False, wear=0); asm.add('Pool', water)
        if A['tier'] and hsh('tier', cx, k) < 0.8:                  # second tier: two small arches on top
            for t in range(2):
                arcade_bay(asm, Vector(at(px + bay * (0.25 + 0.5 * t), 0, top + 4.0)), rz, bay / 2, 3.2, 0.35, ('at', cx, k, t), zr, pier_w=0.55, depth=1.4, spring=1.7, pointed=A['pointed'])
        nrm = tuple(uy)
        if hsh('aiv', cx, k) < 0.75: ivy(asm, [drape(at(px + bay * (0.3 + 0.4 * j), 1.25, top + 0.1), nrm, 2.5 + 5 * hsh('ail', cx, k, j), 0.15, 7, j) for j in range(3)], key=('aiv', cx, k), flowers=0.05)
        if hsh('amc', cx, k) < 0.6: moss_clump(asm, at(px + bay / 2, 0.8, top + 0.85), 0.8, ('amc', cx, k), flowers=4)
    for k in range(bays + 1):                                       # tall piers dropping into the cloud
        px = -ln / 2 + k * bay; top = A['tops'][min(k, bays - 1)]
        coursed_pier(asm, Vector(at(px, 0, top - h - 50)), rz, 2.6, 2.6, 50, ('ap', cx, k), zr, plinth=False)
    return asm.build(ART)

# ================================================================== build everything
print('KIT'); spires = [spire_kit(i) for i in range(len(SPIRES))]
bast = {}
trees = [tree_kit(i) for i in range(3)]; lantern = lantern_kit(); bell = bell_kit(); fern = fern_kit()
print('PLATFORM'); platform()
print('BRIDGES'); [bridge(B) for B in L['bridges']]
print('BELFRIES'); [belfry(Bf, bell) for Bf in L['belfries']]
print('AQUEDUCTS'); [aqueduct(A) for A in L['aqueducts']]
print('ROCKS')
si = 0
for S in L['spires']:
    p = T(S['p'])
    if S['kind'] == 'spire':
        k = si % len(spires); si += 1
        instance(spires[k], ART, p, rz=hsh('rz', S['seed']) * math.tau, s=S['rad'] / 3.0, name='Spire')
    else:
        key = (S['kind'], round(S['rad'], 1)); bi = len(bast) % len(BASTIONS)
        if key not in bast: bast[key] = bastion_kit(bi, S['rad'])
        if S['kind'] == 'stub': instance(spires[(si + 3) % len(spires)], ART, p, rz=hsh('st', S['seed']) * math.tau, s=S['rad'] / 3.0, name='Stub')
        else: instance(bast[key], ART, p, rz=hsh('bz', S['seed']) * math.tau, name='Bastion')
print('TREES')
for i, Tr in enumerate(L['trees']): instance(trees[i % 3], ART, T(Tr['p']), rz=hsh('tz', i) * math.tau, s=Tr['s'], name='Tree')
for p in L['lanterns']: instance(lantern, ART, T(p), name='Lantern')
for i in range(300):                                                # ferns along the rim, outside the playable radius
    a = (i + hsh('fa', i) * 0.6) / 300 * math.tau
    if near_exit(a, GAP): continue
    d = PLAY_R + 0.45 + 0.12 * hsh('fd', i) if i % 2 else R + 0.95
    instance(fern, ART, Vector((math.cos(a) * d, -math.sin(a) * d, 0.0 if d < R else 0.45)), rz=hsh('fz', i) * math.tau, s=0.55 + 0.4 * hsh('fs', i), name='Fern')

# ---- bake ambient occlusion into the vertex colours: crevices, joints and undersides darken the way they would with a
# painted AO pass. Cycles on the GPU, one bake over every unique mesh, then multiplied into the painted 'Col'.
import addon_utils, numpy as np
addon_utils.enable('cycles'); scene.render.engine = 'CYCLES'
try:
    cp = bpy.context.preferences.addons['cycles'].preferences; cp.compute_device_type = 'OPTIX'; cp.refresh_devices()
    for d in cp.devices: d.use = d.type != 'CPU'
    scene.cycles.device = 'GPU'
except Exception as e: print('GPU setup failed', e)
scene.cycles.samples = 16; scene.render.bake.use_selected_to_active = False
world = bpy.data.worlds.new('AOWorld'); scene.world = world
try: world.light_settings.distance = 1.6
except Exception: pass
uniq = {}
for o in list(ART.all_objects) + list(KIT.all_objects):
    if o.type == 'MESH' and o.data.name not in uniq and 'Col' in o.data.color_attributes and len(o.data.polygons) > 0: uniq[o.data.name] = o
KIT.hide_render = False
for o in KIT.all_objects: o.hide_set(False); o.hide_render = False
bpy.ops.object.select_all(action='DESELECT'); baked = 0
for o in uniq.values():
    me = o.data
    ao = me.color_attributes.get('AO') or me.color_attributes.new('AO', 'FLOAT_COLOR', 'CORNER'); me.color_attributes.active_color = ao
    o.select_set(True); baked += 1
bpy.context.view_layer.objects.active = next(iter(uniq.values()))
bpy.ops.object.bake(type='AO', target='VERTEX_COLORS', margin=0)
for o in uniq.values():
    me = o.data; n = len(me.loops); col = np.empty(n * 4, np.float32); ao = np.empty(n * 4, np.float32)
    me.color_attributes['Col'].data.foreach_get('color', col); me.color_attributes['AO'].data.foreach_get('color', ao)
    k = 0.45 + 0.55 * ao.reshape(-1, 4)[:, :1]; c = col.reshape(-1, 4); c[:, :3] *= k
    me.color_attributes['Col'].data.foreach_set('color', c.ravel()); me.color_attributes.remove(me.color_attributes['AO'])
    me.color_attributes.active_color = me.color_attributes['Col']
print('AO BAKED', baked)
scene.render.engine = 'BLENDER_EEVEE'
for o in KIT.all_objects: o.hide_set(True)

json.dump({k: list(v) for k, v in PALETTE.items()}, open(os.path.join(OUT, 'art_materials.json'), 'w'), indent=1)
tris = 0
for o in ART.all_objects:
    if o.type == 'MESH': tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
print('TRIS', tris, 'OBJECTS', len(ART.all_objects))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'verdant_chimes.blend'))

# export only the layout (the kit collection is the instance source)
for o in KIT.all_objects: o.hide_set(True) if o.name in bpy.context.view_layer.objects else None
bpy.ops.object.select_all(action='DESELECT')
for o in ART.all_objects: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, 'VerdantChimes_Art.glb'), export_format='GLB', export_yup=True, use_selection=True, export_apply=True)
print('EXPORTED')

if RENDERS:
    world = bpy.data.worlds.new('Sky'); scene.world = world; world.use_nodes = True
    bg = world.node_tree.nodes['Background']; bg.inputs['Color'].default_value = (0.16, 0.36, 0.37, 1); bg.inputs['Strength'].default_value = 0.8
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); scene.collection.objects.link(sun); sun.data.energy = 4.4; sun.data.color = (1, 0.91, 0.78)
    sun.rotation_euler = Vector((0.55, 0.65, -0.75)).to_track_quat('-Z', 'Y').to_euler()
    scene.render.engine = 'BLENDER_EEVEE'; scene.render.resolution_x = 1600; scene.render.resolution_y = 900
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); scene.collection.objects.link(cam); scene.camera = cam
    for name, pos, tgt, lens in [('angle', (-34, -40, 30), (1, 2, -3), 32), ('game', (0, -22.5, 29), (0, -1, 0), 26)]:
        cam.location = pos; cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
        scene.render.filepath = os.path.join(OUT, 'renders', f'kit-{name}.png'); bpy.ops.render.render(write_still=True); print('RENDERED', name)
print('DONE')
