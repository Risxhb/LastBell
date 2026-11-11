# Briar Metronome props (miniboss art pass, 9 Nov 2026): the bramble hedge module (variants A-C) and the season graft
# (variants I-IV), built from code, exported as GLBs and rendered for review.
#   blender -b -P briar_props.py -- <stage model|texture> <out dir> [unity props dir] [boss.glb] [floor.png]
# out dir gets <name>.glb, turnaround sheets and game-camera frames; when the unity props dir is given the GLBs are also
# copied there (Resources/BriarProps). Sizes follow build-evidence/miniboss-art-20261109/briar-metronome/prompts.md.
# Blender axes: Z up, -Y toward the gameplay camera; a Unity point (x, y, z) is Blender (x, z, y).
import bpy, bmesh, sys, os, math, random, shutil
from mathutils import Vector, Matrix, noise

args = sys.argv[sys.argv.index('--') + 1:]
STAGE, OUT = args[0], args[1]
UNITY = args[2] if len(args) > 2 and args[2] != '-' else None
BOSS = args[3] if len(args) > 3 and args[3] != '-' else None
FLOOR = args[4] if len(args) > 4 and args[4] != '-' else None
TEX = STAGE == 'texture'
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- palette (Verdant Chimes art_materials.json + prompts)
PAL = {
    'Leaf':      (0.15, 0.26, 0.15), 'Ivy': (0.24, 0.41, 0.21), 'LeafLight': (0.26, 0.42, 0.20), 'Mound': (0.24, 0.37, 0.20), 'Bark': (0.30, 0.23, 0.17), 'Cane': (0.36, 0.24, 0.16),
    'Thorn':     (0.85, 0.80, 0.66), 'Brass': (0.57, 0.52, 0.37), 'BrassDark': (0.42, 0.38, 0.27), 'Moss': (0.34, 0.48, 0.24),
    'EndGrain':  (0.62, 0.50, 0.36), 'Blossom': (0.96, 0.95, 0.90), 'Summer': (0.30, 0.55, 0.22), 'Plum': (0.38, 0.16, 0.22),
    'Ochre':     (0.46, 0.44, 0.22), 'Frost': (0.72, 0.82, 0.90), 'Husk': (0.62, 0.52, 0.40), 'WinterBark': (0.36, 0.32, 0.28), 'SpringLeaf': (0.48, 0.66, 0.30),
}
METAL = {'Brass': 0.55, 'BrassDark': 0.5}   # lower than real brass so the band reads bright without reflections
ROUGH = {'Brass': 0.35, 'BrassDark': 0.45, 'Frost': 0.3, 'Blossom': 0.6}

def lin(c):   # palette values are sRGB (as the arena's art_materials.json); Blender and glTF colours are linear
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*lin(PAL[name]), 1)
    b.inputs['Metallic'].default_value = METAL.get(name, 0.0)
    b.inputs['Roughness'].default_value = ROUGH.get(name, 0.85)
    return m

class Builder:
    """Collects geometry into one bmesh with per-face material indices."""
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.mats = []
    def mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)
    def add(self, verts, faces, m):
        i = self.mi(m); vs = [self.bm.verts.new(v) for v in verts]
        for f in faces:
            try: self.bm.faces.new([vs[k] for k in f]).material_index = i
            except ValueError: pass
    def tube(self, pts, radii, m, sides=6, cap=True):
        rings = []
        for k, p in enumerate(pts):
            d = (pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]).normalized()
            a = d.orthogonal().normalized(); b = d.cross(a)
            r = radii[k] if isinstance(radii, (list, tuple)) else radii
            rings.append([p + (a * math.cos(2 * math.pi * s / sides) + b * math.sin(2 * math.pi * s / sides)) * r for s in range(sides)])
        verts = [v for ring in rings for v in ring]; faces = []
        for k in range(len(pts) - 1):
            for s in range(sides):
                s2 = (s + 1) % sides
                faces.append((k * sides + s, k * sides + s2, (k + 1) * sides + s2, (k + 1) * sides + s))
        if cap:
            verts.append(pts[-1] + (pts[-1] - pts[-2]).normalized() * (radii[-1] if isinstance(radii, (list, tuple)) else radii) * 0.5)
            tip = len(verts) - 1; base = (len(pts) - 1) * sides
            for s in range(sides): faces.append((base + s, base + (s + 1) % sides, tip))
        self.add(verts, faces, m)
    def box(self, c, size, m, rot=None):
        c = Vector(c); hx, hy, hz = (s / 2 for s in size); R = rot or Matrix.Identity(3)
        corners = [Vector((x, y, z)) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
        verts = [c + R @ v for v in corners]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        self.add(verts, faces, m)
    def blob(self, c, r, m, scale=(1, 1, 1), subdiv=1, seed=0, jitter=0.25, keep=None):
        tmp = bmesh.new(); bmesh.ops.create_icosphere(tmp, subdivisions=subdiv, radius=1.0)
        verts = []
        for v in tmp.verts:
            n = noise.noise(v.co * 1.7 + Vector((seed * 3.1, seed * 1.7, seed * 0.3)))
            p = v.co * (1 + jitter * n)
            p = Vector((p.x * scale[0], p.y * scale[1], p.z * scale[2])) * r + Vector(c)
            if keep: p = keep(p)
            verts.append(p)
        faces = [[v.index for v in f.verts] for f in tmp.faces]; tmp.free()
        self.add(verts, faces, m)
    def leaf(self, at, direction, length, width, m, up=Vector((0, 0, 1)), thick=0.012):
        d = Vector(direction).normalized(); side = d.cross(up)
        if side.length < 1e-4: side = Vector((1, 0, 0))
        side.normalize(); n = side.cross(d).normalized()
        a = Vector(at); tip = a + d * length; mid = a + d * (length * 0.45)
        verts = [a, mid + side * width / 2, tip, mid - side * width / 2, mid + n * thick, mid - n * thick]
        faces = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (1, 0, 5), (2, 1, 5), (3, 2, 5), (0, 3, 5)]
        self.add(verts, faces, m)
    def finish(self):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[:], dist=1e-5)
        under = [f for f in self.bm.faces if all(v.co.z < 1e-3 for v in f.verts) and len(f.verts) >= 3]
        bmesh.ops.delete(self.bm, geom=under, context='FACES_ONLY')
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])   # outward normals: Unity culls back faces
        me = bpy.data.meshes.new(self.name); self.bm.normal_update(); self.bm.to_mesh(me); self.bm.free()
        for m in self.mats: me.materials.append(mat(m))
        ob = bpy.data.objects.new(self.name, me); bpy.context.scene.collection.objects.link(ob)
        smooth = {i for i, m in enumerate(self.mats) if m in ('Summer',)}
        for p in me.polygons: p.use_smooth = p.material_index in smooth
        return ob

# ---------------------------------------------------------------- hedge module: 1.0 m along Y, <= 1.2 m wide, ~0.95 m tall
# Round 2 refine: lumpy flanks and an irregular skyline (no ruler-flat faces), canes as side-exiting drooping arcs, leaf tufts
# on the crown, a chamfered underside with splayed stems, and the brass tick stake inside the mass (nothing over the lane).
HEDGE_PROFILE = [(0.20, 0.75), (0.26, 0.92), (0.34, 1.04), (0.44, 1.12), (0.55, 1.15), (0.64, 1.14), (0.72, 1.10), (0.80, 1.05), (0.86, 1.00)]

def hedge_width(z):
    for (z0, w0), (z1, w1) in zip(HEDGE_PROFILE, HEDGE_PROFILE[1:]):
        if z <= z1: return w0 + (w1 - w0) * (z - z0) / (z1 - z0)
    return HEDGE_PROFILE[-1][1]

def lump(x, y, z, v):
    return 0.10 * max(0.0, noise.noise(Vector((x * 2.2 + v * 7.3, y * 3.7, z * 3.1)))) + 0.025 * noise.noise(Vector((y * 11, z * 11, x * 5 + v)))

def hedge(variant):
    rnd = random.Random(201 + variant); B = Builder('BriarHedge_' + 'ABC'[variant])
    NX, NY = 6, 12; zs = [z for z, _ in HEDGE_PROFILE]; L = 1.0; TOP = HEDGE_PROFILE[-1][0]
    def crown(x, y, fade):
        h = TOP + 0.06 * noise.noise(Vector((x * 3, y * 4.5, 3.0 + variant))) * fade
        if variant == 2: h -= 0.12 * math.exp(-(y / 0.22) ** 2)                                  # C: saddle dip
        h -= 0.08 * fade * max(0, math.cos((y + 0.15 * variant) * math.pi * 2.0)) ** 8             # crown notches
        return h
    ring = [(-1, z) for z in zs] + [(-1 + 2 * k / NX, None) for k in range(1, NX)] + [(1, z) for z in reversed(zs)] + [(1 - 2 * k / NX, 'b') for k in range(1, NX)]
    rows = []
    for j in range(NY + 1):
        y = -L / 2 + L * j / NY; fade = math.sin(math.pi * j / NY) ** 0.6; row = []
        for sx, z in ring:
            if z is None: p = Vector((sx * HEDGE_PROFILE[-1][1] / 2 * (1 + 0.05 * fade), y, crown(sx, y, fade)))
            elif z == 'b': p = Vector((sx * 0.3, y, 0.2))
            else:
                w = hedge_width(z) / 2; zz = z if z != TOP else crown(sx, y, fade)
                p = Vector((sx * min(0.6, w + lump(sx, y, z, variant) * fade), y, zz))
            row.append(p)
        rows.append(row)
    n = len(ring); verts = [p for row in rows for p in row]
    for j in range(NY):
        for i in range(n):
            q = (j * n + i, j * n + (i + 1) % n, (j + 1) * n + (i + 1) % n, (j + 1) * n + i)
            zc = sum(verts[k].z for k in q) / 4
            B.add([verts[k] for k in q], [(0, 1, 2, 3)], 'Ivy' if zc > 0.7 else 'Leaf')
    for j in (0, NY): B.add([verts[j * n + i] for i in range(n)], [tuple(range(n))], 'Leaf')
    # leaf tufts on the crown: 3-leaf sprigs rising 0.05-0.1 m
    for k in range(5):
        y = -0.4 + 0.8 * (k + rnd.random()) / 5; x = rnd.uniform(-0.35, 0.35); base = Vector((x, y, crown(x / 0.5, y, 1) - 0.02))
        for q in range(3):
            a = q * 2.1 + rnd.random(); B.leaf(base, Vector((math.cos(a), math.sin(a), 1.1)), rnd.uniform(0.11, 0.15), 0.06, 'Moss')
    # splayed woody stems under the chamfer, two crossing pairs
    for k in range(3):
        y = -0.3 + 0.3 * k; side = 1 if k % 2 else -1; x = side * rnd.uniform(0.02, 0.12)
        out = side * math.tan(math.radians(rnd.uniform(15, 25))) * 0.26
        if k in (1, 2): out = -out * 1.4                                                            # the crossing pairs
        B.tube([Vector((x, y, 0)), Vector((x + out, y + rnd.uniform(-0.04, 0.04), 0.27))], [0.045, 0.03], 'Bark', sides=5, cap=False)
    # canes: A flat crown + 3 side canes; B one 0.3 m arch + 4 side canes; C 2 canes spilling over one side
    plan = {0: ['side'] * 3, 1: ['arch'] + ['side'] * 4, 2: ['spill', 'spill', 'side']}[variant]
    for k, kind in enumerate(plan):
        y0 = -0.4 + 0.8 * (k + 0.5) / len(plan) + rnd.uniform(-0.04, 0.04)
        side = 1 if (variant == 2 and kind == 'spill') else (1 if k % 2 == 0 else -1)
        if kind == 'arch':
            pts = [Vector((-0.2, y0, 0.6)), Vector((-0.12, y0 + 0.03, 1.1)), Vector((0.05, y0 + 0.06, 1.16)), Vector((0.2, y0 + 0.1, 1.05)), Vector((0.3, y0 + 0.12, 0.88))]
        else:
            hgt = rnd.uniform(0.5, 0.8) if kind == 'side' else 0.8; drop = 0.22 if kind == 'side' else 0.42
            hgt = 0.86 + 0.15 - 0.12 if kind == 'side' else hgt
            pts = [Vector((side * 0.25, y0, hgt - 0.05)), Vector((side * 0.5, y0 + 0.04, hgt + 0.12)), Vector((side * 0.6, y0 + 0.08, hgt + 0.1)),
                   Vector((side * 0.6, y0 + 0.11, hgt - drop * 0.5)), Vector((side * 0.58, y0 + 0.13, hgt - drop))]
        B.tube(pts, [0.038, 0.034, 0.029, 0.023, 0.015], 'Cane', sides=5)
        for t in (1, 2, 3):
            p = pts[t]; d = Vector((side * 0.6, rnd.uniform(-1, 1), 0.8)).normalized()
            B.tube([p, p + d * 0.045], [0.014, 0.002], 'Thorn', sides=3, cap=False)
    # the brass tick: a 0.04 m BrassDark stake on the centreline (inside the mass) poking 0.18 m out of the crown, notched collar
    B.box((0, -L / 2 + 0.1, 0.55), (0.04, 0.04, 1.1), 'Brass')
    B.box((0, -L / 2 + 0.1, 0.99), (0.07, 0.07, 0.035), 'BrassDark')
    B.box((0, -L / 2 + 0.1, 1.05), (0.06, 0.06, 0.02), 'Brass')
    for x in (0.585, -0.585):
        B.tube([Vector((x, -L / 2, 0.6)), Vector((x, L / 2, 0.6))], 0.015, 'Brass', sides=4, cap=False)
    return B.finish()

# ---------------------------------------------------------------- season graft: stump + brass band + season scion, 1.5 m
def graft(season):
    rnd = random.Random(7 + season); B = Builder('BriarGraft_' + ('I', 'II', 'III', 'IV')[season])
    # moss mound, r 0.62, 0.12 high (bloom-timer ring r 0.9 stays clear); darker than round 1 so it doesn't pull the eye
    B.blob((0, 0, 0), 0.62, 'Mound', scale=(1, 1, 0.2), subdiv=2, seed=season, jitter=0.12, keep=lambda p: Vector((p.x, p.y, max(0, p.z))))
    # stump: tapered 12-sided log 0.45 m, sawn top
    sides, rings = 12, 4; verts = []; faces = []
    for k in range(rings + 1):
        z = 0.06 + 0.42 * k / rings; r = 0.27 - 0.04 * k / rings
        for s in range(sides):
            a = 2 * math.pi * s / sides; rr = r * (1 + 0.07 * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, z * 4 + season))))
            verts.append(Vector((math.cos(a) * rr, math.sin(a) * rr, z)))
    for k in range(rings):
        for s in range(sides): faces.append((k * sides + s, k * sides + (s + 1) % sides, (k + 1) * sides + (s + 1) % sides, (k + 1) * sides + s))
    B.add(verts, faces, 'Bark')
    top = [verts[rings * sides + s] for s in range(sides)]
    B.add(top + [Vector((0, 0, 0.48))], [(s, (s + 1) % sides, sides) for s in range(sides)], 'EndGrain')
    for k in range(4):
        a = math.radians(45 + 90 * k + rnd.uniform(-15, 15)); d = Vector((math.cos(a), math.sin(a), 0))
        B.tube([d * 0.2 + Vector((0, 0, 0.2)), d * 0.38 + Vector((0, 0, 0.08)), d * 0.55 + Vector((0, 0, 0.0))], [0.09, 0.06, 0.03], 'Bark', sides=5)
    # brass grafting band: two full-value rings 0.03 m proud of the bark, and a 0.12 x 0.08 tick plate facing the camera (-Y)
    for z in (0.29, 0.38):
        B.tube([Vector((math.cos(2 * math.pi * s / 16) * 0.285, math.sin(2 * math.pi * s / 16) * 0.285, z)) for s in range(17)], 0.022, 'Brass', sides=4, cap=False)
    B.box((0, -0.3, 0.335), (0.14, 0.03, 0.09), 'Brass')
    for t in (-0.04, 0, 0.04): B.box((t, -0.318, 0.35), (0.012, 0.01, 0.035), 'BrassDark')
    def whip(t):  # t 0..1, the scion's spine from 0.48 to 1.48 m
        return Vector((0.06 * math.sin(t * math.pi * 1.4), 0.04 * math.sin(t * math.pi * 0.9), 0.48 + 1.0 * t))
    spine = [whip(k / 10) for k in range(11)]
    B.box((0, 0, 0.47), (0.36, 0.04, 0.06), 'Bark')                     # the cleft wedge
    end = {0: 11, 1: 9, 2: 11, 3: 9}[season]
    B.tube(spine[:end], [0.06 - 0.045 * k / (end - 1) for k in range(end)], 'WinterBark' if season == 3 else 'Bark', sides=6)
    if season == 0:   # Spring: 4 clusters of 3-4 ivory flowers (0.14 m) forming a 0.45 m cloud at 1.0-1.45 m, Moss leaves behind
        for k in range(6):
            t = 0.5 + 0.45 * k / 5; a = k * 2.3
            B.leaf(whip(t), Vector((math.cos(a), math.sin(a), 0.4)), 0.2, 0.1, 'Moss')
        for k, (t, a, rr) in enumerate([(0.58, 0.3, 0.13), (0.72, 2.0, 0.15), (0.85, 3.9, 0.12), (0.97, 5.4, 0.06), (0.8, 1.1, 0.17), (0.92, 3.0, 0.14)]):
            centre = whip(t) + Vector((math.cos(a) * rr, math.sin(a) * rr, 0))
            for c3 in range(4 if k % 2 else 3):
                b = a + c3 * 1.9; c = centre + Vector((math.cos(b) * 0.09, math.sin(b) * 0.09, 0.03 * (c3 % 2)))
                for q in range(5):
                    pa = q * 2 * math.pi / 5 + c3
                    B.leaf(c, Vector((math.cos(pa), math.sin(pa), 0.25)), 0.09, 0.085, 'Blossom')
    elif season == 1:   # Summer: a clipped topiary ball 0.6 m wide, base at 0.95 m, a brass tie under it
        top = 1.5; c = whip(0.75); c = Vector((c.x, c.y, 1.23))
        B.blob(c, 0.26, 'Summer', scale=(1.12, 1.12, 0.95), subdiv=2, seed=41, jitter=0.1)
        for k in range(10):
            a = k * 2 * math.pi / 10 + (0.3 if k % 2 else 0); el = 0.4 if k % 2 else -0.2
            d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
            B.blob(c + Vector((d.x * 0.2, d.y * 0.2, d.z * 0.17)), 0.12, 'Summer', scale=(1, 1, 0.65), subdiv=1, seed=k + 50,
                   keep=lambda p: Vector((p.x, p.y, max(0.95, min(p.z, top)))))
        w = whip(0.45)
        B.tube([w + Vector((math.cos(2 * math.pi * i / 10) * 0.05, math.sin(2 * math.pi * i / 10) * 0.05, 0)) for i in range(11)], 0.012, 'Brass', sides=4, cap=False)
    elif season == 2:   # Autumn: 8 staggered leaves hanging 35-50 degrees down (2 below 0.9 m), 40% plum, 6 fallen on the mound
        for k in range(8):
            t = 0.38 + 0.58 * k / 7 if k > 1 else 0.3 + 0.08 * k; p = whip(t); a = k * 2.4 + rnd.uniform(-0.3, 0.3); droop = math.radians(rnd.uniform(35, 50))
            d = Vector((math.cos(a) * math.cos(droop), math.sin(a) * math.cos(droop), -math.sin(droop)))
            B.tube([p, p + d * 0.05], [0.006, 0.004], 'Bark', sides=3, cap=False)
            B.leaf(p + d * 0.05, d, 0.34, 0.21, 'Plum' if k in (1, 3, 6) else 'Ochre')
        for k in range(6):
            a = k * 1.05 + 0.4; rr = 0.34 + 0.08 * (k % 3); p = Vector((math.cos(a) * rr, math.sin(a) * rr, 0.115 - 0.03 * rr))
            B.leaf(p, Vector((math.cos(a + 1.2), math.sin(a + 1.2), 0)), 0.17, 0.11, 'Plum' if k % 2 else 'Ochre', thick=0.006)
    else:   # Winter: a bare crown 0.5 m wide (3 forks + 2 sub-forks), flat frost caps on the last 0.15 m of each tip, 2 pale husks
        base = whip(0.8)
        forks = [(20, 0.42, 0.75), (140, 0.4, 0.7), (260, 0.4, 0.85)]
        def frost(p0, p1, r):   # a flat cap riding the top edge of a twig segment
            d = (p1 - p0); m = (p0 + p1) / 2 + Vector((0, 0, r * 0.9)); yaw = math.atan2(d.y, d.x); pitch = math.atan2(d.z, Vector((d.x, d.y)).length)
            B.box(m, (d.length, r * 2.2, 0.012), 'Frost', rot=(Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(-pitch, 3, 'Y')))
        for i, (deg, ln, rise) in enumerate(forks):
            a = math.radians(deg); d = Vector((math.cos(a), math.sin(a), rise)).normalized()
            mid = base + d * ln * 0.5; q = base + d * ln
            B.tube([base, mid, q], [0.03, 0.02, 0.012], 'WinterBark', sides=5)
            frost(q - d * 0.15, q, 0.013)
            if i < 2:
                sd = Vector((math.cos(a + 0.9), math.sin(a + 0.9), 0.75)).normalized(); e = mid + sd * 0.25
                B.tube([mid, e], [0.016, 0.008], 'WinterBark', sides=4)
                frost(e - sd * 0.15, e, 0.01)
        for k in range(2):
            B.blob(whip(0.55 + 0.15 * k) + Vector((0.08 * (1 - 2 * k), 0, -0.05)), 0.06, 'Husk', scale=(0.8, 0.8, 1.3), subdiv=1, seed=k + 70)
    return B.finish()

# ---------------------------------------------------------------- texture stage: UVs + baked atlas per object
# Texture round 2 (critic tex r1): per-material colour ramps in the Verdant Chimes palette (sRGB, converted), leaf-sized
# voronoi instead of camo blotches, a top-light / dark-base value gradient, AO strength per material (baked as a mask, so
# brass and blossoms aren't crushed), real brass values (albedo ~0.6, metal 0.9, roughness 0.35), end-grain rings, and
# never-seen faces (hedge end caps) shrunk in the UV layout.
TEXSPEC = {   # name: (pattern, [ramp stops sRGB], AO strength, roughness)
    'Leaf':      ('leaf',  [(0.13, 0.23, 0.13), (0.17, 0.29, 0.16), (0.24, 0.41, 0.21)], 0.45, 0.9),
    'Ivy':       ('leaf',  [(0.13, 0.23, 0.13), (0.17, 0.29, 0.16), (0.24, 0.41, 0.21)], 0.45, 0.9),
    'Moss':      ('leaf',  [(0.30, 0.44, 0.22), (0.36, 0.50, 0.25), (0.42, 0.56, 0.28)], 0.5, 0.9),
    'Mound':     ('soft',  [(0.22, 0.34, 0.18), (0.26, 0.39, 0.21), (0.30, 0.43, 0.23)], 0.5, 0.95),
    'Bark':      ('bark',  [(0.27, 0.21, 0.15), (0.32, 0.25, 0.18), (0.38, 0.29, 0.20)], 0.5, 0.85),
    'WinterBark':('bark',  [(0.31, 0.28, 0.24), (0.36, 0.32, 0.28), (0.42, 0.38, 0.33)], 0.5, 0.85),
    'Cane':      ('bark',  [(0.33, 0.22, 0.15), (0.38, 0.26, 0.18), (0.46, 0.32, 0.22)], 0.5, 0.8),
    'Thorn':     ('flat',  [(0.80, 0.75, 0.62), (0.88, 0.83, 0.70)], 0.2, 0.6),
    'Brass':     ('metal', [(0.56, 0.50, 0.32), (0.62, 0.55, 0.36), (0.68, 0.61, 0.40)], 0.4, 0.35),
    'BrassDark': ('metal', [(0.38, 0.34, 0.24), (0.42, 0.38, 0.27), (0.47, 0.42, 0.30)], 0.4, 0.45),
    'EndGrain':  ('grain', [(0.40, 0.31, 0.21), (0.48, 0.38, 0.27), (0.54, 0.43, 0.31)], 0.3, 0.8),
    'Blossom':   ('flat',  [(0.93, 0.92, 0.86), (0.97, 0.96, 0.91)], 0.1, 0.6),
    'Summer':    ('crown', [(0.24, 0.41, 0.21), (0.34, 0.51, 0.26), (0.42, 0.58, 0.28)], 0.35, 0.85),
    'Plum':      ('flat',  [(0.40, 0.15, 0.27), (0.46, 0.17, 0.30), (0.52, 0.21, 0.34)], 0.3, 0.75),
    'Ochre':     ('flat',  [(0.44, 0.43, 0.22), (0.48, 0.47, 0.24), (0.53, 0.52, 0.27)], 0.3, 0.75),
    'Frost':     ('flat',  [(0.72, 0.81, 0.89), (0.78, 0.87, 0.94)], 0.2, 0.35),
    'Husk':      ('flat',  [(0.56, 0.47, 0.36), (0.66, 0.56, 0.43)], 0.4, 0.8),
    'SpringLeaf':('leaf',  [(0.36, 0.52, 0.25), (0.48, 0.66, 0.30)], 0.4, 0.85),
    'LeafLight': ('leaf',  [(0.22, 0.38, 0.19), (0.30, 0.45, 0.23)], 0.5, 0.9),
}

def procedural(m):
    """Swaps a flat material for a procedural one (for baking): colour = ramp(pattern) x top-light x base darkening."""
    nt = m.node_tree; b = nt.nodes['Principled BSDF']; kind, stops, ao, rough = TEXSPEC[m.name]
    L = nt.links.new; N = nt.nodes.new
    tc = N('ShaderNodeTexCoord'); geo = N('ShaderNodeNewGeometry')
    def noise(scale, vec=None, detail=4.0):
        n = N('ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        L(vec or tc.outputs['Object'], n.inputs['Vector']); return n.outputs['Fac']
    def voronoi(scale):
        v = N('ShaderNodeTexVoronoi'); v.inputs['Scale'].default_value = scale; L(tc.outputs['Object'], v.inputs['Vector']); return v.outputs['Distance']
    def mix(a, bb, fa):   # fa * a + (1 - fa) * bb as math nodes
        m1 = N('ShaderNodeMath'); m1.operation = 'MULTIPLY'; L(a, m1.inputs[0]); m1.inputs[1].default_value = fa
        m2 = N('ShaderNodeMath'); m2.operation = 'MULTIPLY_ADD'; L(bb, m2.inputs[0]); m2.inputs[1].default_value = 1 - fa; L(m1.outputs[0], m2.inputs[2])
        return m2.outputs[0]
    if kind == 'leaf':  fac = mix(voronoi(40.0), noise(12.0), 0.7)
    elif kind == 'soft': fac = mix(voronoi(30.0), noise(8.0), 0.5)
    elif kind == 'crown':
        sep = N('ShaderNodeSeparateXYZ'); L(geo.outputs['Normal'], sep.inputs[0])
        nz = N('ShaderNodeMapRange'); L(sep.outputs['Z'], nz.inputs['Value']); nz.inputs['From Min'].default_value = -1.0
        fac = mix(nz.outputs['Result'], voronoi(25.0), 0.6)
    elif kind == 'bark':
        mp = N('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (40, 40, 6); L(tc.outputs['Object'], mp.inputs['Vector'])
        fac = noise(1.0, mp.outputs['Vector'], 6.0)
    elif kind == 'grain':
        wv = N('ShaderNodeTexWave'); wv.wave_type = 'RINGS'; wv.rings_direction = 'Z'; wv.inputs['Scale'].default_value = 50.0; wv.inputs['Distortion'].default_value = 1.5
        L(tc.outputs['Object'], wv.inputs['Vector']); fac = mix(wv.outputs['Fac'], noise(20.0), 0.75)
    else: fac = noise(18.0)
    ramp = N('ShaderNodeValToRGB'); cr = ramp.color_ramp
    while len(cr.elements) < len(stops): cr.elements.new(0.5)
    for i, c in enumerate(stops): cr.elements[i].position = i / (len(stops) - 1); cr.elements[i].color = (*lin(c), 1)
    # contrast: squeeze the pattern toward the middle (0.3-0.7) so no stop dominates as a blotch
    mr = N('ShaderNodeMapRange'); L(fac, mr.inputs['Value']); mr.inputs['From Min'].default_value = 0.2; mr.inputs['From Max'].default_value = 0.8
    L(mr.outputs['Result'], ramp.inputs['Fac'])
    col = ramp.outputs['Color']
    if kind not in ('metal', 'flat'):
        # top faces +15%, the bottom 0.2 m of the object -25%
        sep = N('ShaderNodeSeparateXYZ'); L(geo.outputs['Normal'], sep.inputs[0])
        top = N('ShaderNodeMapRange'); L(sep.outputs['Z'], top.inputs['Value']); top.inputs['From Min'].default_value = 0.0; top.inputs['To Min'].default_value = 1.0; top.inputs['To Max'].default_value = 1.15
        sp = N('ShaderNodeSeparateXYZ'); L(tc.outputs['Object'], sp.inputs[0])
        low = N('ShaderNodeMapRange'); L(sp.outputs['Z'], low.inputs['Value']); low.inputs['From Max'].default_value = 0.2; low.inputs['To Min'].default_value = 0.75
        if m.name in ('Leaf', 'Ivy'):   # hedge mass: one smooth ramp from the base (-25%) to the crown (+25%)
            low.inputs['From Min'].default_value = 0.2; low.inputs['From Max'].default_value = 0.9; low.inputs['To Min'].default_value = 0.75; low.inputs['To Max'].default_value = 1.25
        mul = N('ShaderNodeMath'); mul.operation = 'MULTIPLY'; L(top.outputs['Result'], mul.inputs[0]); L(low.outputs['Result'], mul.inputs[1])
        mx = N('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs['Factor'].default_value = 1.0
        cc = N('ShaderNodeCombineColor'); [L(mul.outputs[0], cc.inputs[k]) for k in range(3)]
        L(col, mx.inputs['A']); L(cc.outputs['Color'], mx.inputs['B']); col = mx.outputs['Result']
    L(col, b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = 0.0   # Cycles' diffuse-colour bake drops metals to black; metal comes from the METAL map

def shrink_unseen(ob):
    """Hedge end caps (large flat faces facing +-Y) get a quarter of their UV size, then the islands are repacked."""
    bm = bmesh.new(); bm.from_mesh(ob.data); uv = bm.loops.layers.uv.active
    for f in bm.faces:
        if abs(f.normal.y) > 0.95 and f.calc_area() > 0.3:
            c = sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
            for l in f.loops: l[uv].uv = c + (l[uv].uv - c) * 0.25
    bm.to_mesh(ob.data); bm.free()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')

def bake(ob, size):
    import numpy as np
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 32
    try: sc.cycles.device = 'GPU'
    except Exception: pass
    sc.world = sc.world or bpy.data.worlds.new('W'); sc.world.light_settings.distance = 0.12   # AO distance (short: small parts don't stamp the faces under them)
    for o in bpy.context.scene.objects: o.select_set(False)
    # the other variants sit at the same origin: hide them or the AO bake sees them as occluders
    others = [o for o in bpy.context.scene.objects if o != ob and not o.hide_render]
    for o in others: o.hide_render = True
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    shrink_unseen(ob)
    for m in ob.data.materials: procedural(m)
    imgs = {}
    def emit(value_of):
        for m in ob.data.materials:
            b = m.node_tree.nodes['Principled BSDF']
            b.inputs['Emission Color'].default_value = (value_of(m.name),) * 3 + (1,); b.inputs['Emission Strength'].default_value = 1
    for kind, prep in (('DIFFUSE', None), ('AO', None), ('ROUGHNESS', None),
                       ('METAL', lambda: emit(lambda n: 0.9 if TEXSPEC[n][0] == 'metal' else 0.0)),
                       ('AOSTR', lambda: emit(lambda n: TEXSPEC[n][2]))):
        img = bpy.data.images.new(f'{ob.name}_{kind}', size, size); imgs[kind] = img
        if prep: prep()
        for m in ob.data.materials:
            nt = m.node_tree
            for n in [n for n in nt.nodes if n.name == 'BakeTarget']: nt.nodes.remove(n)
            t = nt.nodes.new('ShaderNodeTexImage'); t.name = 'BakeTarget'; t.image = img; nt.nodes.active = t
        if kind == 'DIFFUSE': sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
        bpy.ops.object.bake(type='EMIT' if kind in ('METAL', 'AOSTR') else kind, margin=4)
    if os.environ.get('BRIAR_RAW'):
        for k, im in imgs.items():
            im.filepath_raw = os.path.join(OUT, 'tex', 'raw', f'{ob.name}_{k}.png'); im.file_format = 'PNG'
            os.makedirs(os.path.dirname(im.filepath_raw), exist_ok=True); im.save()
    px = lambda im: np.array(im.pixels[:]).reshape(size, size, 4)
    d, ao, r, mt, st = px(imgs['DIFFUSE']), px(imgs['AO']), px(imgs['ROUGHNESS']), px(imgs['METAL']), px(imgs['AOSTR'])
    alb = d.copy(); alb[..., :3] = d[..., :3] * (1 - st[..., :1] * (1 - ao[..., :1]))
    orm = np.ones_like(d); orm[..., 1] = r[..., 0] ** 2; orm[..., 2] = mt[..., 0]   # the ROUGHNESS bake stores sqrt(roughness)
    out_alb = bpy.data.images.new(ob.name + '_albedo', size, size); out_alb.pixels[:] = alb.ravel()
    out_orm = bpy.data.images.new(ob.name + '_orm', size, size, is_data=True); out_orm.pixels[:] = orm.ravel()
    for im in (out_alb, out_orm):
        im.filepath_raw = os.path.join(OUT, 'tex', im.name + '.png'); im.file_format = 'PNG'
        os.makedirs(os.path.dirname(im.filepath_raw), exist_ok=True); im.save()
    m = bpy.data.materials.new(ob.name + '_mat'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    ta = nt.nodes.new('ShaderNodeTexImage'); ta.image = out_alb; nt.links.new(ta.outputs['Color'], b.inputs['Base Color'])
    to = nt.nodes.new('ShaderNodeTexImage'); to.image = out_orm; out_orm.colorspace_settings.name = 'Non-Color'
    sp = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(to.outputs['Color'], sp.inputs['Color'])
    nt.links.new(sp.outputs['Green'], b.inputs['Roughness']); nt.links.new(sp.outputs['Blue'], b.inputs['Metallic'])
    ob.data.materials.clear(); ob.data.materials.append(m)
    for p in ob.data.polygons: p.material_index = 0
    ob.select_set(False)
    for o in others: o.hide_render = False

# ---------------------------------------------------------------- export
def export(ob):
    for o in bpy.context.scene.objects: o.select_set(False)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    path = os.path.join(OUT, ob.name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=88,
                              export_apply=True, export_yup=True)
    ob.select_set(False)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if UNITY:
        os.makedirs(UNITY, exist_ok=True); shutil.copy(path, os.path.join(UNITY, ob.name + '.glb'))
    return path, tris, os.path.getsize(path)

# ---------------------------------------------------------------- review renders
def setup_render(w, h):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = w; sc.render.resolution_y = h; sc.render.resolution_percentage = 100
    sc.world = sc.world or bpy.data.worlds.new('W'); sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']; bg.inputs['Color'].default_value = (0.55, 0.62, 0.58, 1); bg.inputs['Strength'].default_value = 0.45
    sc.view_settings.view_transform = 'Standard'
    if not bpy.data.objects.get('Sun'):
        sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sun.data.energy = 3.2; sun.data.angle = math.radians(8)
        sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-30)); sc.collection.objects.link(sun)

def camera(loc, look, fov=40, ortho=None):
    sc = bpy.context.scene
    cam = bpy.data.objects.get('Cam') or bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.location = Vector(loc); cam.rotation_euler = (Vector(look) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    else: cam.data.type = 'PERSP'; cam.data.sensor_fit = 'VERTICAL'; cam.data.angle = math.radians(fov)
    sc.camera = cam

def shoot(path):
    bpy.context.scene.render.filepath = path; bpy.ops.render.render(write_still=True)

def sheet(paths, cols, out, label_h=0):
    import numpy as np   # Blender's Python has no PIL
    ims = [bpy.data.images.load(p) for p in paths]; w, h = ims[0].size; rows = (len(ims) + cols - 1) // cols
    canvas = np.zeros((h * rows, w * cols, 4), dtype=np.float32); canvas[..., 3] = 1
    for i, im in enumerate(ims):
        px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r = rows - 1 - i // cols; c = i % cols   # Blender pixel rows start at the bottom
        canvas[r * h:(r + 1) * h, c * w:(c + 1) * w] = px
    s = bpy.data.images.new(os.path.basename(out), w * cols, h * rows); s.pixels[:] = canvas.ravel()
    s.filepath_raw = out; s.file_format = 'PNG'; s.save()
    for im in ims: bpy.data.images.remove(im)

def turnaround(ob, name, scale):
    hide = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o != ob]
    for o in hide: o.hide_render = True
    old = ob.location.copy(); ob.location = (0, 0, 0)
    setup_render(420, 420); paths = []
    for k, yaw in enumerate((0, 90, 180, 315)):
        a = math.radians(yaw - 90); r = 6
        camera((math.cos(a) * r, math.sin(a) * r, 2.2 if k < 3 else 4.5), (0, 0, scale * 0.45), ortho=scale)
        p = os.path.join(OUT, 'frames', f'_t_{name}_{k}.png'); shoot(p); paths.append(p)
    sheet(paths, 4, os.path.join(OUT, 'frames', f'turn_{name}.png'))
    for p in paths: os.remove(p)
    ob.location = old
    for o in hide: o.hide_render = False

def u2b(x, y, z): return Vector((x, z, y))
def sdir(yaw): a = math.radians(yaw); return Vector((math.sin(a), 0, math.cos(a)))   # Unity Sim.Dir

def arena(hedges, grafts):
    """Lays the props out as the encounter does (spoke0 = 30 deg, Verdant Chimes bound 13.5) round the boss."""
    sc = bpy.context.scene
    # floor
    bpy.ops.mesh.primitive_plane_add(size=30); fl = bpy.context.active_object; fl.name = 'Floor'
    fm = bpy.data.materials.new('FloorMat'); fm.use_nodes = True; fb = fm.node_tree.nodes['Principled BSDF']; fb.inputs['Roughness'].default_value = 0.9
    if FLOOR:
        t = fm.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(FLOOR); fm.node_tree.links.new(t.outputs['Color'], fb.inputs['Base Color'])
    else: fb.inputs['Base Color'].default_value = (0.36, 0.42, 0.30, 1)
    fl.data.materials.append(fm)
    # telegraph mocks (coral, as Telegraph-Style v2 ground lanes and the bloom-timer ring)
    tm = bpy.data.materials.new('Tele'); tm.use_nodes = True; tb = tm.node_tree.nodes['Principled BSDF']
    tb.inputs['Base Color'].default_value = (1.0, 0.42, 0.32, 1); tb.inputs['Emission Color'].default_value = (1.0, 0.35, 0.25, 1); tb.inputs['Emission Strength'].default_value = 0.6
    tb.inputs['Alpha'].default_value = 0.45; tm.surface_render_method = 'BLENDED' if hasattr(tm, 'surface_render_method') else tm.blend_method
    spoke0, bound = 30.0, 13.5
    sections = []; r = 4.0
    while r < bound + 0.6 - 0.05: sections.append((r, min(3.0, bound + 0.6 - r))); r += 3.0
    for k in range(4):
        yaw = spoke0 + 90 * k; d = sdir(yaw)
        bpy.ops.mesh.primitive_plane_add(size=1); lane = bpy.context.active_object
        lane.scale = (1.6, bound + 0.6 - 4.0, 1); mid = d * (4.0 + (bound + 0.6 - 4.0) / 2)
        lane.location = u2b(mid.x, 0.02, mid.z); lane.rotation_euler = (0, 0, -math.radians(yaw)); lane.data.materials.append(tm)
        mod = 0
        for frm, ln in sections:
            n = max(1, round(ln / 1.0)); m = ln / n
            for j in range(n):
                c = d * (frm + m * (j + 0.5)); src = hedges[(k * 7 + mod) % len(hedges)]; mod += 1
                o = src.copy(); o.data = src.data; sc.collection.objects.link(o)
                o.location = u2b(c.x, 0, c.z); o.rotation_euler = (0, 0, -math.radians(yaw)); o.scale = (1, m, 1)
    for k in range(4):   # pens between spokes
        yaw = spoke0 + 45 + 90 * k; c = sdir(yaw) * 8.5
        src = grafts[k]; o = src.copy(); o.data = src.data; sc.collection.objects.link(o); o.location = u2b(c.x, 0, c.z)
        bpy.ops.mesh.primitive_torus_add(major_radius=0.9, minor_radius=0.04, location=u2b(c.x, 0.03, c.z)); bpy.context.active_object.data.materials.append(tm)
        # a player capsule for scale (0.4 m radius, 1.8 m) beside the graft
        p = c + sdir(yaw + 90) * 1.6
        bpy.ops.mesh.primitive_cylinder_add(radius=0.4, depth=1.8, location=u2b(p.x, 0.9, p.z)); pc = bpy.context.active_object
        pm = bpy.data.materials.new('Player'); pm.use_nodes = True; pm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.35, 0.45, 0.85, 1); pc.data.materials.append(pm)
    if BOSS:
        bpy.ops.import_scene.gltf(filepath=BOSS)

def main():
    clear()
    hedges = [hedge(v) for v in range(3)]
    grafts = [graft(s) for s in range(4)]
    stats = []
    for ob in hedges + grafts:
        if TEX: bake(ob, 1024)
        stats.append((ob.name, *export(ob)))
    for ob in hedges: ob.location.x += 100
    for ob in grafts: ob.location.x += 200
    os.makedirs(os.path.join(OUT, 'frames'), exist_ok=True)
    for ob in hedges: ob.location.x -= 100; turnaround(ob, ob.name, 1.5); ob.location.x += 100
    for ob in grafts: ob.location.x -= 200; turnaround(ob, ob.name, 1.9); ob.location.x += 200
    # arena frames
    arena(hedges, grafts)
    for ob in hedges + grafts: ob.hide_render = True
    setup_render(1280, 720)
    camera(u2b(0, 11, -14), u2b(0, 1.4, 0)); shoot(os.path.join(OUT, 'frames', 'game_overview.png'))
    camera(u2b(0, 11, -19), u2b(0, 1.4, -5)); shoot(os.path.join(OUT, 'frames', 'game_overview_near.png'))
    # close cameras on the nearest hedge and graft (7 m in front, 2.6 m up, as the autotest's close shots)
    hc = sdir(210) * 8.5; gc = sdir(255) * 8.5   # spoke 210 and pen 255 face the camera side
    to_cam = Vector((0, 0, -1))
    camera(u2b(hc.x - 5, 2.6, hc.z - 5), u2b(hc.x, 0.6, hc.z)); shoot(os.path.join(OUT, 'frames', 'close_hedge.png'))
    camera(u2b(gc.x, 2.6, gc.z - 7), u2b(gc.x, 0.9, gc.z), fov=40); shoot(os.path.join(OUT, 'frames', 'close_graft.png'))
    camera(u2b(gc.x, 11, gc.z - 14), u2b(gc.x, 1.4, gc.z)); shoot(os.path.join(OUT, 'frames', 'game_graft_pen.png'))
    with open(os.path.join(OUT, 'stats.txt'), 'w') as f:
        for name, path, tris, size in stats: f.write(f'{name}\t{tris} tris\t{size} bytes\n')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'briar_props.blend'))

main()
