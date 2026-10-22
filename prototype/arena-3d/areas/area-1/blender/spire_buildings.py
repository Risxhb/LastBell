# Area 1 — The Skyward Spire: the landmark buildings, modelled in Blender with real geometry.
# Every window has a raised arch moulding, tracery bars, a sill and a gold star; buttresses step back; spires carry
# crocket rows and gold bands; domes have raised ribs; lanterns are open columns. Edges get a small bevel so they
# catch the light. Replaces the code-built cathedral(), domedBelfry(), armillaryTower(), rotunda() and needle()
# landmarks of skyward-spire-env.js (which still provides floor, rim, bridges, kiosks, filler spires and cloud anchors).
#   blender -b -P spire_buildings.py -- <out.glb> [<out.blend>]
# Positions are the env file's (three.js: x east, y up, z south). Blender is z-up: three (x, y, z) -> Blender (x, -z, y),
# and a three rotation.y of t is a Blender rotation about z of t. Top-level object names match the env groups, so
# names starting "Occluder" are still hidden in the gameplay view and "ArmillaryRing" children still turn.
import bpy, bmesh, math, sys, os
import numpy as np
from mathutils import Matrix, Vector, Euler

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'SkywardSpire_Buildings.glb')
OUT_BLEND = args[1] if len(args) > 1 else ''
TAU = math.tau

bpy.ops.wm.read_factory_settings(use_empty=True)
rng = np.random.default_rng(21)

# ---------------------------------------------------------------- materials (K_ prefix: kept apart from the env GLB's)
def ashlar_image(name, base, rows, size=1024, seed=1):
    r = np.random.default_rng(seed)
    img = np.empty((size, size, 3), np.float32); img[:] = np.array(base) * 0.82          # grout
    rh = size // rows
    for row in range(rows):
        x = -(row % 2) * size // 10
        while x < size:
            bw = int(size / 8 + r.random() * size / 7); v = (r.random() - 0.5) * 0.07
            x0, x1 = max(0, x + 3), min(size, x + bw - 3); y0, y1 = row * rh + 3, row * rh + rh - 3
            if x1 > x0:
                tone = np.clip(np.array(base) * (1 + v) + (r.random() - 0.5) * 0.02, 0, 1)
                img[y0:y1, x0:x1] = tone
                img[y0:y0 + 4, x0:x1] *= 1.05                                                       # lit top edge
                img[y1 - 5:y1, x0:x1] *= 0.93                                                       # worn lower edge
            x += bw
    img *= (1 + (r.random((size, size, 1)) - 0.5) * 0.06)                                         # fine grain
    img = np.clip(img, 0, 1)
    im = bpy.data.images.new(name, size, size)
    px = np.concatenate([img[::-1], np.ones((size, size, 1), np.float32)], axis=2)
    im.pixels.foreach_set(px.ravel()); im.pack()
    return im

def material(name, color, rough=0.6, metal=0.0, emit=None, emit_k=0.0, image=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = rough; bsdf.inputs['Metallic'].default_value = metal
    if emit:
        bsdf.inputs['Emission Color'].default_value = (*emit, 1); bsdf.inputs['Emission Strength'].default_value = emit_k
    if image:
        t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = image
        m.node_tree.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    return m

def srgb(h):  # hex -> linear rgb
    c = [((h >> s) & 255) / 255 for s in (16, 8, 0)]
    return tuple((v / 12.92) if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)

ASH = ashlar_image('K_Ashlar', [0.98, 0.92, 0.78], 8, seed=3)
ASH2 = ashlar_image('K_AshlarShade', [0.94, 0.86, 0.72], 6, seed=7)
MAT = {
    'stone': material('K_IvoryBlocks', (1, 1, 1), 0.7, image=ASH),
    'stoneShade': material('K_IvoryShade', (1, 1, 1), 0.78, image=ASH2),
    'ivory': material('K_Ivory', srgb(0xefe4cc), 0.6),
    'gold': material('K_Gold', srgb(0xe8c25a), 0.3, 0.65),
    'goldDark': material('K_GoldDark', srgb(0x9c7a3a), 0.45, 0.55),
    'cobalt': material('K_Cobalt', srgb(0x2b3f8f), 0.3, 0.2),
    'glass': material('K_StarGlass', srgb(0x2a3a7c), 0.25, 0.15, emit=srgb(0x2a3c7e), emit_k=0.6),
    'shadow': material('K_ArchShadow', srgb(0x8a90a8), 0.9),
}

# ---------------------------------------------------------------- mesh builder
class Mesh:
    """Collects parts into one bmesh with per-face material slots; UVs are box-projected in world metres."""
    def __init__(self):
        self.bm = bmesh.new(); self.slots = []
    def slot(self, m):
        if m not in self.slots: self.slots.append(m)
        return self.slots.index(m)
    def _done(self, verts, m, M):
        bmesh.ops.transform(self.bm, matrix=M, verts=verts)
        i = self.slot(m)
        for f in {f for v in verts for f in v.link_faces}: f.material_index = i
        return verts
    def box(self, sx, sy, sz, loc, m, rz=0.0, M=Matrix()):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        return self._done(r['verts'], m, M @ Matrix.Translation(loc) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Diagonal((sx, sy, sz, 1)))
    def cyl(self, r1, r2, h, loc, m, seg=16, M=Matrix(), rz=0.0):
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=max(r2, 1e-4), depth=h)
        return self._done(r['verts'], m, M @ Matrix.Translation(loc) @ Matrix.Rotation(rz, 4, 'Z'))
    def sphere(self, r, loc, m, u=16, v=8, hemi=False, sz=1.0, M=Matrix()):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=r)
        verts = res['verts']
        if hemi:
            kill = [x for x in verts if x.co.z < -1e-4]
            for x in verts:
                if -1e-4 <= x.co.z < 0: x.co.z = 0
            bmesh.ops.delete(self.bm, geom=kill, context='VERTS'); verts = [x for x in verts if x.is_valid]
        return self._done(verts, m, M @ Matrix.Translation(loc) @ Matrix.Diagonal((1, 1, sz, 1)))
    def tube(self, pts, rad, m, seg=6, closed=False, M=Matrix()):
        pts = [Vector(p) for p in pts]; n = len(pts); rings = []
        prev_n = None
        for i, p in enumerate(pts):
            a = pts[(i - 1) % n] if (closed or i > 0) else p; b = pts[(i + 1) % n] if (closed or i < n - 1) else p
            t = (b - a).normalized() if (b - a).length > 1e-6 else Vector((0, 0, 1))
            ref = prev_n if prev_n is not None else (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))
            nn = (ref - t * ref.dot(t)).normalized(); bn = t.cross(nn); prev_n = nn
            rings.append([self.bm.verts.new(p + (nn * math.cos(k / seg * TAU) + bn * math.sin(k / seg * TAU)) * rad) for k in range(seg)])
        verts = [v for r in rings for v in r]
        for i in range(n if closed else n - 1):
            r0, r1 = rings[i], rings[(i + 1) % n]
            for k in range(seg): self.bm.faces.new((r0[k], r0[(k + 1) % seg], r1[(k + 1) % seg], r1[k]))
        if not closed:
            self.bm.faces.new(list(reversed(rings[0]))); self.bm.faces.new(rings[-1])
        return self._done(verts, m, M)
    def ngon(self, pts, m, M=Matrix(), out=(0, 1, 0)):
        """A flat panel, turned to face `out` (local; +y = out of the wall), so it isn't culled from the front."""
        vs = [self.bm.verts.new(Vector(p)) for p in pts]; f = self.bm.faces.new(vs)
        f.normal_update()
        if f.normal.dot(Vector(out)) < 0: f.normal_flip()
        return self._done(vs, m, M)
    def to_object(self, name, bevel=0.0):
        me = bpy.data.meshes.new(name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        uv = self.bm.loops.layers.uv.new('UVMap')
        for f in self.bm.faces:                                   # box projection, 2.5 m of ashlar per texture tile
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                c = l.vert.co
                l[uv].uv = ((c.y, c.z) if ax == 0 else (c.x, c.z) if ax == 1 else (c.x, c.y))
                l[uv].uv = (l[uv].uv[0] / 2.5, l[uv].uv[1] / 2.5)
        self.bm.to_mesh(me); self.bm.free()
        for m in self.slots: me.materials.append(m)
        ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
        for p in me.polygons: p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(35))                # round shafts and domes smooth, box corners crisp
        if bevel:
            md = ob.modifiers.new('Bevel', 'BEVEL'); md.width = bevel; md.segments = 1; md.limit_method = 'ANGLE'
            md.angle_limit = math.radians(40); md.harden_normals = False
        return ob

# ---------------------------------------------------------------- details
def arch_outline(w, h, n=10):
    """Equilateral pointed arch, base at z = 0, total height h (x across, z up)."""
    hs = max(0.0, h - w * math.sqrt(3) / 2); pts = [(-w / 2, 0), (-w / 2, hs)]
    for i in range(1, n + 1):
        a = math.pi - (math.pi / 3) * i / n; pts.append((w / 2 + w * math.cos(a), hs + w * math.sin(a)))
    for i in range(n - 1, -1, -1):
        a = (math.pi / 3) * i / n; pts.append((-w / 2 + w * math.cos(a), hs + w * math.sin(a)))
    pts.append((w / 2, 0)); return pts

def face_matrix(cx, cy, cz, out_angle):
    """Local frame on a wall: x along the wall, y out of it, z up; out_angle = direction of the outward normal."""
    nx, ny = math.cos(out_angle), math.sin(out_angle)
    return Matrix(((ny, nx, 0, cx), (-nx, ny, 0, cy), (0, 0, 1, cz), (0, 0, 0, 1)))   # x = (ny, -nx): a proper rotation (det +1), not a mirror

def star(r0, r1, k=4):
    return [(math.cos(i / (2 * k) * TAU + math.pi / 2) * (r1 if i % 2 == 0 else r0), math.sin(i / (2 * k) * TAU + math.pi / 2) * (r1 if i % 2 == 0 else r0)) for i in range(2 * k)]

def lancet(mesh, F, w, h, lights=2, simple=False):
    """A window on frame F (y = 0 is the wall surface): glass, raised moulding, mullions, tracery, sill, gold star.
    simple: glass and moulding only, for storeys that sit down in the cloud."""
    o = arch_outline(w, h, 6 if simple else 8)
    mesh.ngon([(x, 0.08, z) for x, z in o], MAT['glass'], F)   # glass sits inside the raised moulding, clear of the wall
    mesh.tube([(x, 0.07, z) for x, z in o[:-1]] , 0.08, MAT['gold'], 3 if simple else 4, False, F)   # jamb and arch moulding
    if simple: return
    hood = arch_outline(w + 0.34, h + 0.22)[1:-1]
    mesh.tube([(x, 0.1, z) for x, z in hood], 0.06, MAT['ivory'], 3, False, F)                       # gold hood mould
    mesh.box(w + 0.4, 0.22, 0.12, (0, 0.1, -0.06), MAT['ivory'], M=F)                               # sill
    hs = max(0.0, h - w * math.sqrt(3) / 2)
    for k in range(1, lights):                                                                      # mullions
        x = -w / 2 + w * k / lights; mesh.box(0.06, 0.06, hs, (x, 0.1, hs / 2), MAT['gold'], M=F)
    rr = w * 0.26; cz = hs + w * 0.38
    ring = [(math.cos(i / 12 * TAU) * rr, 0.1, cz + math.sin(i / 12 * TAU) * rr) for i in range(12)]
    mesh.tube(ring, 0.045, MAT['gold'], 3, True, F)
    for k in range(4):                                                                              # quatrefoil lobes inside the oculus
        a = k * math.pi / 2; lx, lz = math.cos(a) * rr * 0.45, cz + math.sin(a) * rr * 0.45
        mesh.tube([(lx + math.cos(i / 8 * TAU) * rr * 0.42, 0.105, lz + math.sin(i / 8 * TAU) * rr * 0.42) for i in range(8)], 0.025, MAT['gold'], 3, True, F)                                                  # tracery oculus
    mesh.ngon([(x, 0.11, z + hs * 0.45) for x, z in star(w * 0.07, w * 0.32)], MAT['gold'], F)      # gold star

def pinnacle(mesh, x, y, z, h, w, M=Matrix()):
    mesh.box(w, w, h, (x, y, z + h / 2), MAT['ivory'], M=M)
    mesh.box(w * 1.25, w * 1.25, 0.07, (x, y, z + h), MAT['gold'], M=M)
    mesh.cyl(w * 0.62, 0, h * 1.1, (x, y, z + h + h * 0.55), MAT['ivory'], 4, M=M, rz=math.pi / 4)
    for k in range(4):                                                                              # crockets climbing the edges
        for f in (0.3, 0.6):
            a = k * math.pi / 2 + math.pi / 4; rr = w * 0.62 * (1 - f) * 1.15
            mesh.box(w * 0.16, w * 0.16, w * 0.16, (x + math.cos(a) * rr, y + math.sin(a) * rr, z + h + h * 1.1 * f), MAT['ivory'], a, M)
    mesh.sphere(w * 0.14, (x, y, z + h + h * 1.15), MAT['gold'], 8, 6, M=M)

def finial(mesh, x, y, z, s=1.0, M=Matrix()):
    mesh.cyl(0.07 * s, 0.11 * s, 0.25 * s, (x, y, z + 0.12 * s), MAT['gold'], 8, M=M)
    mesh.sphere(0.15 * s, (x, y, z + 0.38 * s), MAT['gold'], 10, 6, M=M)
    mesh.cyl(0.05 * s, 0, 0.7 * s, (x, y, z + 0.8 * s), MAT['gold'], 6, M=M)
    mesh.box(0.32 * s, 0.04 * s, 0.04 * s, (x, y, z + 0.75 * s), MAT['gold'], M=M); mesh.box(0.04 * s, 0.32 * s, 0.04 * s, (x, y, z + 0.75 * s), MAT['gold'], M=M)

def crocket_spire(mesh, x, y, z, r, h, seg=8, M=Matrix(), mat=None):
    mesh.cyl(r, 0, h, (x, y, z + h / 2), mat or MAT['ivory'], seg, M=M, rz=math.pi / seg)
    for f in (0.25, 0.6): mesh.cyl(r * (1 - f) + 0.05, r * (1 - f) + 0.05, 0.12, (x, y, z + h * f), MAT['gold'], seg, M=M, rz=math.pi / seg)
    for k in range(seg):
        a = k / seg * TAU
        for f in np.linspace(0.15, 0.8, 4):
            rr = r * (1 - f) * 1.04; cs = max(0.08, r * 0.12); mesh.box(cs, cs, cs, (x + math.cos(a) * rr, y + math.sin(a) * rr, z + h * f), MAT['ivory'], a + math.pi / 4, M)
    finial(mesh, x, y, z + h - 0.15, max(0.8, r * 0.6), M=M)

def square_tower_storey(mesh, x, y, z, w, h, lights=2, wins=1, gold_course=True, M=Matrix()):
    mesh.box(w, w, h, (x, y, z + h / 2), MAT['stone'], M=M)
    for sx in (-1, 1):
        for sy in (-1, 1):                                                                          # stepped corner buttresses
            for i, (bw, bh) in enumerate(((0.55, h), (0.42, h * 0.6))):
                mesh.box(bw, bw, bh, (x + sx * (w / 2 + bw * 0.15), y + sy * (w / 2 + bw * 0.15), z + bh / 2), MAT['ivory'], M=M)
    if gold_course: mesh.box(w * 1.08, w * 1.08, 0.18, (x, y, z + h), MAT['gold'], M=M)
    mesh.box(w * 1.05, w * 1.05, 0.12, (x, y, z + 0.06), MAT['goldDark'], M=M)
    ww = min(w / wins * 0.62, h * 0.34)
    for f in range(4):
        ang = f * math.pi / 2
        for k in range(wins):
            off = (k + 0.5) / wins * w - w / 2
            F = M @ Matrix.Translation((x, y, 0)) @ face_matrix(0, 0, 0, ang) @ Matrix.Translation((off, w / 2, z + h * 0.14))
            lancet(mesh, F, ww, h * 0.72, lights, z < -10)

def round_tier(mesh, x, y, z, r, h, n, mat, M=Matrix()):
    simple = z < -10   # deeper storeys sit in the cloud: plain glass and moulding only
    mesh.cyl(r, r, h, (x, y, z + h / 2), mat, 20 if simple else 32, M=M)
    ring = [(x + math.cos(i / 40 * TAU) * r * 1.07, y + math.sin(i / 40 * TAU) * r * 1.07, z + h) for i in range(40)]
    mesh.tube(ring, 0.12, MAT['gold'], 4, True, M)                                                  # cornice
    for i in range(0 if simple else 24):                                                                             # dentils
        a = i / 32 * TAU; mesh.box(0.16, 0.12, 0.14, (x + math.cos(a) * r * 1.01, y + math.sin(a) * r * 1.01, z + h - 0.18), MAT['ivory'], a, M)
    ww = min(h * 0.3, r * TAU / n * 0.55)
    for i in range(n):
        a = i / n * TAU
        F = M @ Matrix.Translation((x, y, 0)) @ face_matrix(0, 0, 0, a) @ Matrix.Translation((0, r, z + h * 0.15))
        lancet(mesh, F, ww, h * 0.68, 2, simple)
        b = a + math.pi / n                                                                         # engaged column between bays
        mesh.cyl(0.13, 0.13, h * 0.9, (x + math.cos(b) * r, y + math.sin(b) * r, z + h * 0.47), MAT['ivory'], 8, M=M)
        mesh.box(0.32, 0.32, 0.12, (x + math.cos(b) * r, y + math.sin(b) * r, z + h * 0.92), MAT['gold'], b, M)

def ribbed_dome(mesh, x, y, z, r, mat, ribs=12, sz=1.15, M=Matrix()):
    mesh.sphere(r, (x, y, z), mat, 24, 10, hemi=True, sz=sz, M=M)
    for k in range(ribs):
        a = k / ribs * TAU
        pts = [(x + math.cos(a) * r * 1.015 * math.cos(t), y + math.sin(a) * r * 1.015 * math.cos(t), z + r * sz * 1.015 * math.sin(t)) for t in np.linspace(0, math.pi / 2 * 0.96, 10)]
        mesh.tube(pts, r * 0.035, MAT['gold'], 5, False, M)
    mesh.tube([(x + math.cos(i / 40 * TAU) * r * 1.03, y + math.sin(i / 40 * TAU) * r * 1.03, z + 0.05) for i in range(40)], r * 0.05, MAT['gold'], 5, True, M)

def lantern(mesh, x, y, z, r, cols=6, M=Matrix()):
    mesh.cyl(r * 1.1, r * 1.1, 0.2, (x, y, z + 0.1), MAT['gold'], 16, M=M)
    for i in range(cols):
        a = i / cols * TAU; mesh.cyl(r * 0.1, r * 0.1, r * 1.3, (x + math.cos(a) * r, y + math.sin(a) * r, z + 0.2 + r * 0.65), MAT['ivory'], 8, M=M)
    mesh.cyl(r * 1.12, r * 1.05, 0.25, (x, y, z + 0.32 + r * 1.3), MAT['gold'], 16, M=M)
    return z + 0.45 + r * 1.3

# ---------------------------------------------------------------- landmark types
def place(name, x3, z3, rot=0.0):
    return Matrix.Translation((x3, -z3, 0)) @ Matrix.Rotation(rot, 4, 'Z')

def gothic_cluster(name, x3, z3, rot, w, d, tiers):
    me = Mesh(); M = Matrix()
    h0 = tiers[0]; up = sum(tiers[1:]) or 8
    # below the floor: undercroft, then separate tower shafts falling into the cloud
    me.box(w * 0.97, d * 0.97, 4, (0, 0, -2), MAT['stone'])
    shafts = [(-0.38, 0.17), (-0.13, 0.2), (0.13, 0.2), (0.38, 0.17)] if w >= 14 else [(-0.33, 0.24), (0, 0.3), (0.33, 0.24)] if w >= 9 else [(-0.22, 0.26), (0.22, 0.26)]
    for fx, fw in shafts:
        sw = w * fw
        for i in range(3): square_tower_storey(me, fx * w, 0, -12 - 8 * i, sw * (1 - i * 0.07), 8, 1, 1)
        me.cyl(sw * 0.6, 0, 5, (fx * w, 0, -32.5), MAT['stoneShade'], 4, rz=math.pi / 4)
    # the hall: buttressed bays with lancets, a pierced parapet and a ridge roof with a gold crest
    me.box(w, d, h0, (0, 0, h0 / 2), MAT['stone'])
    me.box(w * 1.05, d * 1.05, 0.25, (0, 0, h0), MAT['gold'])
    bays = max(2, int(w // 2.6))
    for side in (-1, 1):
        for k in range(bays):
            bx = -w / 2 + (k + 0.5) * w / bays
            F = face_matrix(bx, side * d / 2, 0, -math.pi / 2 if side < 0 else math.pi / 2) @ Matrix.Translation((0, 0, h0 * 0.12))
            lancet(me, F, min(1.2, w / bays * 0.45), h0 * 0.72, 2)
        for k in range(bays + 1):
            bx = -w / 2 + k * w / bays
            for i, (bw, bh, off) in enumerate(((0.7, h0, 0.45), (0.55, h0 * 0.55, 0.85))):
                me.box(bw, bw, bh, (bx, side * (d / 2 + off * 0.6), bh / 2), MAT['ivory'])
            pinnacle(me, bx, side * (d / 2 + 0.3), h0, 1.6, 0.42)
    for sx in (-1, 1):
        F = face_matrix(sx * w / 2, 0, 0, 0 if sx > 0 else math.pi) @ Matrix.Translation((0, 0, h0 * 0.12))
        lancet(me, F, min(1.6, d * 0.3), h0 * 0.75, 3)
    me.cyl(d * 0.42, d * 0.42, w * 0.96, (0, 0, 0), MAT['stoneShade'], 3, M=Matrix.Translation((0, 0, h0 + d * 0.21)) @ Matrix.Rotation(math.pi / 2, 4, 'Y') @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    me.tube([(-w * 0.48, 0, h0 + d * 0.42), (w * 0.48, 0, h0 + d * 0.42)], 0.1, MAT['gold'], 6)
    for k in range(bays): me.cyl(0.08, 0, 0.7, (-w / 2 + (k + 0.5) * w / bays, 0, h0 + d * 0.42 + 0.35), MAT['gold'], 6)
    # belfries above the roof
    n = 4 if w >= 14 else 3 if w >= 9 else 2
    sw = min(w * (0.17 if n == 4 else 0.22), d * 0.5)
    for k in range(n):
        tx = (k - 1.5) * w * 0.25 if n == 4 else (k - 1) * w * 0.33 if n == 3 else (k - 0.5) * w * 0.42
        mid = (n == 3 and k == 1) or (n == 4 and k in (1, 2))
        th = up * (1.15 if mid else 0.8 + 0.1 * (k % 2)); z = h0 + 0.25; tw = sw
        for f, wins in ((0.55, 1), (0.45, 1)):
            sh = th * f; square_tower_storey(me, tx, 0, z, tw, sh, 2, wins); z += sh + 0.2; tw *= 0.86
        for sx in (-1, 1):
            for sy in (-1, 1): pinnacle(me, tx + sx * tw * 0.55, sy * tw * 0.55, z, 1.4, tw * 0.17)
        for f in range(4):                                                                          # gablets on each face
            a = f * math.pi / 2
            G = Matrix.Translation((tx, 0, z)) @ face_matrix(0, 0, 0, a) @ Matrix.Translation((0, tw * 0.45, 0))
            me.ngon([(-tw * 0.4, 0, 0), (tw * 0.4, 0, 0), (0, 0, tw * 0.55)], MAT['ivory'], G)
            me.tube([(-tw * 0.4, 0.02, 0), (0, 0.02, tw * 0.55), (tw * 0.4, 0.02, 0)], 0.04, MAT['gold'], 4, False, G)
        crocket_spire(me, tx, 0, z, tw * 0.62, tw * (4.4 if mid else 3.6))
        for side in (-1, 1):                                                                        # flying buttress to the hall eaves
            p0 = Vector((tx, side * (d / 2 + 0.4), h0 + 1.0)); p1 = Vector((tx, side * sw * 0.5, h0 + 0.25 + th * 0.4))
            mid_p = (p0 + p1) / 2 + Vector((0, 0, 0.6))
            me.tube([p0, mid_p, p1], 0.18, MAT['ivory'], 6)
    ob = me.to_object(name); ob.matrix_world = place(name, x3, z3, rot); return ob

def domed_belfry(name, x3, z3, r, tiers, dome_mat, spire):
    me = Mesh()
    for i in range(4): round_tier(me, 0, 0, -8 * (i + 1), r * (0.96 - i * 0.06), 8, 5, MAT['stone'] if i == 0 else MAT['stoneShade'])
    z, cr = 0.0, r
    for h in tiers:
        round_tier(me, 0, 0, z, cr, h, 5, MAT['stone'])
        for i in range(12):                                                                         # balustrade posts on the cornice
            a = (i + 0.5) / 12 * TAU; pinnacle(me, math.cos(a) * cr * 1.05, math.sin(a) * cr * 1.05, z + h, 0.7, 0.22)
        z += h + 0.22; cr *= 0.86
    ribbed_dome(me, 0, 0, z, cr * 0.95, dome_mat, 12)
    lz = lantern(me, 0, 0, z + cr * 0.95 * 1.1, cr * 0.28)
    ribbed_dome(me, 0, 0, lz, cr * 0.3, MAT['gold'], 8, 1.0)
    me.cyl(cr * 0.06, 0, spire, (0, 0, lz + cr * 0.3 + spire / 2), MAT['gold'], 8)
    me.sphere(cr * 0.09, (0, 0, lz + cr * 0.32), MAT['gold'], 10, 6)
    ob = me.to_object(name); ob.matrix_world = place(name, x3, z3); return ob

def armillary_tower(name, x3, z3, r, h, sphere):
    me = Mesh()
    for i in range(4): round_tier(me, 0, 0, -7.5 * (i + 1), r * (0.95 - i * 0.06), 7.5, 5, MAT['stoneShade'])
    round_tier(me, 0, 0, 0, r, h, 6, MAT['stone'])
    z = h + 0.22
    me.cyl(r * 0.98, r * 0.98, 0.3, (0, 0, z + 0.15), MAT['ivory'], 32)
    for i in range(8):                                                                              # open bell lantern
        a = i / 8 * TAU; me.cyl(0.11, 0.13, 2.2, (math.cos(a) * r * 0.8, math.sin(a) * r * 0.8, z + 1.4), MAT['gold'], 8)
    me.sphere(r * 0.38, (0, 0, z + 2.3), MAT['goldDark'], 16, 8, hemi=True, sz=-1.2)
    me.cyl(r * 0.98, r * 0.92, 0.35, (0, 0, z + 2.65), MAT['gold'], 32)
    for i in range(8):
        a = i / 8 * TAU + 0.2; pinnacle(me, math.cos(a) * r * 0.9, math.sin(a) * r * 0.9, z + 2.8, 0.6, 0.18)
    s = sphere; cz = z + 2.8 + s * 1.75
    me.cyl(s * 0.35, s * 0.5, s * 0.35, (0, 0, z + 2.8 + s * 0.18), MAT['gold'], 16)
    me.cyl(s * 0.08, s * 0.12, s * 0.5, (0, 0, z + 2.8 + s * 0.55), MAT['gold'], 8)
    me.tube([(math.cos(t) * s, 0, cz + math.sin(t) * s) for t in np.linspace(0, TAU, 48, endpoint=False)], s * 0.045, MAT['gold'], 6, True)   # meridian
    me.tube([(math.cos(t) * s, math.sin(t) * s, cz) for t in np.linspace(0, TAU, 48, endpoint=False)], s * 0.045, MAT['gold'], 6, True)       # equator
    me.sphere(s * 0.3, (0, 0, cz), MAT['cobalt'], 16, 10)
    finial(me, 0, 0, cz + s * 1.02, s * 0.7)
    ob = me.to_object(name); ob.matrix_world = place(name, x3, z3)
    for tilt, rr in ((0.41, 0.86), (-0.7, 0.72)):                                                   # turning rings, kept as their own objects
        rm = Mesh(); rm.tube([(math.cos(t) * s * rr, math.sin(t) * s * rr, 0) for t in np.linspace(0, TAU, 40, endpoint=False)], s * 0.05, MAT['gold'], 6, True)
        rm.sphere(s * 0.08, (s * rr, 0, 0), MAT['gold'], 8, 6)
        ring = rm.to_object('ArmillaryRing', 0); ring.parent = ob
        ring.matrix_parent_inverse = Matrix(); ring.matrix_basis = Matrix.Translation((0, 0, cz)) @ Matrix.Rotation(tilt, 4, 'X')
    return ob

def rotunda(name, x3, z3, r, dome_mat, centre_mat, turrets):
    me = Mesh()
    for i in range(3): round_tier(me, 0, 0, -7 * (i + 1), r * (1.0 - i * 0.08), 7, 10, MAT['stoneShade'])
    me.cyl(r * 1.12, r * 1.12, 0.4, (0, 0, 0.2), MAT['ivory'], 48)
    me.cyl(r * 0.78, r * 0.78, 3, (0, 0, 1.9), MAT['shadow'], 32)
    for i in range(16):                                                                             # colonnade: base, shaft, capital
        a = i / 16 * TAU; px, py = math.cos(a) * r * 0.95, math.sin(a) * r * 0.95
        me.box(0.42, 0.42, 0.2, (px, py, 0.5), MAT['ivory'], a); me.cyl(0.14, 0.12, 2.6, (px, py, 1.9), MAT['ivory'], 10)
        me.box(0.4, 0.4, 0.18, (px, py, 3.25), MAT['gold'], a)
    me.cyl(r * 1.02, r, 0.5, (0, 0, 3.6), MAT['ivory'], 48)
    me.tube([(math.cos(i / 48 * TAU) * r * 1.06, math.sin(i / 48 * TAU) * r * 1.06, 3.9) for i in range(48)], 0.12, MAT['gold'], 5, True)
    ribbed_dome(me, 0, 0, 4.0, r * 0.8, dome_mat, 16, 1.05)
    lz = lantern(me, 0, 0, 4.0 + r * 0.84, r * 0.2)
    ribbed_dome(me, 0, 0, lz, r * 0.22, centre_mat, 8, 1.0)
    finial(me, 0, 0, lz + r * 0.22, 1.2)
    for i in range(turrets):                                                                        # ring of small domed turrets
        a = i / turrets * TAU + 0.3; px, py = math.cos(a) * r * 1.05, math.sin(a) * r * 1.05
        me.cyl(0.5, 0.55, 2.2, (px, py, 1.5), MAT['stone'], 16)
        lantern(me, px, py, 2.6, 0.38, 6)
        ribbed_dome(me, px, py, 3.25, 0.45, MAT['gold'], 6, 1.1); finial(me, px, py, 3.7, 0.6)
    ob = me.to_object(name); ob.matrix_world = place(name, x3, z3); return ob

def needle(name, x3, z3, h, w=0.9):
    me = Mesh(); M = Matrix()
    for i in range(3): round_tier(me, 0, 0, -8 * (i + 1), w * (0.92 - i * 0.07), 8, 4, MAT['stoneShade'])
    round_tier(me, 0, 0, 0, w, h * 0.55, 4, MAT['stone'])
    round_tier(me, 0, 0, h * 0.55 + 0.22, w * 0.8, h * 0.25, 4, MAT['stone'])
    z = h * 0.8 + 0.44
    for i in range(4):
        a = i / 4 * TAU + math.pi / 4; pinnacle(me, math.cos(a) * w * 0.8, math.sin(a) * w * 0.8, z, 0.8, 0.18)
    crocket_spire(me, 0, 0, z, w * 0.72, h * 0.42)
    ob = me.to_object(name); ob.matrix_world = place(name, x3, z3); return ob

# ---------------------------------------------------------------- the landmarks (mirror skyward-spire-env.js; keep in sync)
gothic_cluster('Cathedral_NW', -27, -24, 0.62, 16, 8, [5, 18, 10])
gothic_cluster('Cathedral_NW2', -33, -9, 0.3, 8, 5, [5, 12])
gothic_cluster('Cathedral_NE', 28, -25, -0.6, 16, 8, [5, 18, 9])
gothic_cluster('Cathedral_NE2', 34, -11, -0.25, 8, 5, [5, 12])
gothic_cluster('Cathedral_NW3', -12.5, -33, 0.15, 9, 6, [12, 7])
gothic_cluster('Cathedral_NE3', 12.5, -33, -0.15, 9, 6, [12, 7])
armillary_tower('Armillary_NW', -16.2, -15.2, 2.4, 6, 1.9)
armillary_tower('Armillary_NE', 16.4, -15.0, 2.4, 6, 1.9)
armillary_tower('Occluder_Armillary_SW', -15.8, 15.2, 2.3, 5, 1.7)
domed_belfry('Belfry_W', -23, 13.5, 2.8, [10, 7], MAT['cobalt'], 5)
domed_belfry('Belfry_E', 23, 13.5, 2.8, [10, 7], MAT['cobalt'], 5)
domed_belfry('Occluder_Belfry_SE', 28, 16, 2.2, [7, 5], MAT['cobalt'], 4)
domed_belfry('Occluder_Belfry_SW', -29, 16, 2.0, [6, 4], MAT['gold'], 3)
rotunda('Occluder_Rotunda_SW', -21.5, 24, 3.2, MAT['gold'], MAT['cobalt'], 6)
rotunda('Occluder_Rotunda_SE', 19.5, 24.5, 3.4, MAT['ivory'], MAT['cobalt'], 8)
for i, (x, z, h) in enumerate([(-38, -31, 26), (-35, -36, 19), (-42, -27, 15), (38, -31, 26), (35, -36, 19), (42, -27, 15),
                               (-20, -28, 24), (-30, -18, 20), (-8.5, -25, 15), (-35, -26, 18), (-27, -34, 22), (-42, -14, 16),
                               (20, -28, 24), (30, -18, 20), (8.5, -25, 15), (35, -26, 18), (27, -34, 22), (42, -14, 16), (-31, 0, 14), (31, 0, 14)]):
    needle(f'Needle_{i}', x, z, h, 1.1 if i < 6 else 0.9)
# extra spires filling the open sky beside the bridges (north side; the south ones join the occluders below)
for i, (x, z, h) in enumerate([(-9, -20, 13), (9, -20, 13), (-22, -6, 12), (22, -6, 12), (-5, -30, 17), (5, -30, 17)]):
    needle(f'NeedleX_{i}', x, z, h, 0.8)
south = bpy.data.objects.new('Occluder_Needles_S', None); bpy.context.collection.objects.link(south)
for i, (x, z, h) in enumerate([(-27, 11, 15), (-34, 22, 13), (-12, 21, 9), (-30, 31, 11), (27, 11, 15), (34, 22, 13), (12, 21, 9), (30, 31, 11), (-40, 6, 12), (40, 6, 12), (-8, 26, 10), (8, 26, 10), (-20, 8, 11), (20, 8, 11)]):
    ob = needle(f'NeedleS_{i}', x, z, h); ob.parent = south

# ---------------------------------------------------------------- export (only when run directly; spire_kit.py reuses the parts above)
if __name__ == "__main__":
    root = bpy.data.objects.new('SkywardSpire_Buildings', None); bpy.context.collection.objects.link(root)
    for ob in list(bpy.context.collection.objects):
        if ob is not root and ob.parent is None: ob.parent = root
    tris = 0
    dg = bpy.context.evaluated_depsgraph_get()
    for ob in bpy.context.collection.objects:
        if ob.type == 'MESH':
            ev = ob.evaluated_get(dg); m = ev.to_mesh(); m.calc_loop_triangles(); tris += len(m.loop_triangles); ev.to_mesh_clear()
    print('TRIS', tris)
    if OUT_BLEND: bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_apply=True, export_image_format='JPEG', export_image_quality=88,
                              export_yup=True, export_materials='EXPORT')
    print('EXPORTED', OUT, os.path.getsize(OUT))
