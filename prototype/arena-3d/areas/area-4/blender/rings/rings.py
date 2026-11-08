# blender -b -P rings.py -- <unity Models dir>
# Area 4 clock rings, modelled in Blender around the Z axis (Unity Y, the builder's spin axis). They follow the approved
# astrolabe concept (Reference/Codex-Corrections-20261025/followup-3-edits-v2, Chzy18 27 Oct): a deep band whose outer
# wall is a cobalt star-map inset between two brass rims.
#   Astrolabe   r 14 m, 3 m deep band over 320° (the broken gap holds the drifting fragments). Both rims carry the same
#               60-station engraved scale (every fifth tick major) on their faces and outer walls. Twelve numeral
#               medallions sit on the outer wall only, counter-clockwise seen from above (above = the side the numerals
#               stand up toward, which faces away from the game camera), with diamond studs and four-point stars between.
#               The face toward the camera shows only the unlettered rims and ticks. Hollow centre, brass struts inside.
#   Ring_Right / Ring_Low   smaller bands in the same style with blank medallions, three spokes, a hub and a broken end.
#   Astro_Fragment  a curved piece of the astrolabe band (single brass slot).
# Angles follow Unity's polar convention through U(): Unity (x, y, z) = (-bx, bz, by), so theta maps to 180°-theta.
# Slots: 0 brass, 1 polished brass, 2 iron (engraving, numerals), 3 cobalt (star-map inset).
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common')); import lb

out = sys.argv[sys.argv.index('--') + 1:][0]; here = os.path.dirname(os.path.abspath(__file__))
lb.reset(); BRASS, POL, IRON, COBALT = lb.slots(['Brass', 'Polished', 'Iron', 'Cobalt'])
COBALT.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.08, 0.1, 0.32, 1)
def U(theta): return math.radians(180 - theta)

def band(name, r0, r1, z0, z1, t0, t1, mat, step=1.5):
    bm = bmesh.new(); n = max(1, int(abs(t1 - t0) / step)); rows = []
    for i in range(n + 1):
        a = U(t0 + (t1 - t0) * i / n); c, s = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((c * r, s * r, z)) for r, z in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    for i in range(n):
        for k in range(4): bm.faces.new((rows[i][k], rows[i + 1][k], rows[i + 1][(k + 1) % 4], rows[i][(k + 1) % 4]))
    if abs(t1 - t0) < 360: bm.faces.new(rows[0]); bm.faces.new(list(reversed(rows[-1])))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return lb._obj(name, bm, mat)

def frame(o, theta, r, z, up=-1):
    """Moves a part built in a local frame (x = reading direction, y = up, z = outward) onto the outer wall at theta."""
    a = U(theta); n = Vector((math.cos(a), math.sin(a), 0)); u = Vector((0, 0, up)); rt = u.cross(n)
    m = Matrix(((rt.x, u.x, n.x), (rt.y, u.y, n.y), (rt.z, u.z, n.z)))
    for v in o.data.vertices: v.co = m @ v.co + n * r + Vector((0, 0, z))
    o.data.update(); return o

def numeral(text, size, depth):
    cu = bpy.data.curves.new('n', 'FONT'); cu.body = text; cu.size = size; cu.extrude = depth / 2; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'
    ob = bpy.data.objects.new('n', cu); bpy.context.collection.objects.link(ob)
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get())); bpy.data.objects.remove(ob)
    for v in me.vertices: v.co.z += depth / 2
    o = bpy.data.objects.new('numeral', me); bpy.context.collection.objects.link(o); o.data.materials.append(IRON); return o

def star(name, size, mat):  # flat four-point star, local z outward
    bm = bmesh.new(); c = bm.verts.new((0, 0, 0.06)); ring = []
    for k in range(8):
        a = k * math.pi / 4; r = size if k % 2 == 0 else size * 0.22; ring.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, 0)))
    for k in range(8): bm.faces.new((c, ring[k], ring[(k + 1) % 8]))
    bm.faces.new(list(reversed(ring))); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return lb._obj(name, bm, mat)

def diamond(name, w, h, mat):
    bm = bmesh.new(); c = bm.verts.new((0, 0, 0.1)); ring = [bm.verts.new(p) for p in ((w, 0, 0), (0, h, 0), (-w, 0, 0), (0, -h, 0))]
    for k in range(4): bm.faces.new((c, ring[k], ring[(k + 1) % 4]))
    bm.faces.new(list(reversed(ring))); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return lb._obj(name, bm, mat)

def tick(theta, r_out, length, z, face, w=0.05):
    """An engraved tick: on a rim face (face=±1, radial line) or on the outer rim wall (face=0, line along the axis)."""
    a = U(theta)
    if face: return lb.box('tick', (w, length, 0.03), (math.cos(a) * (r_out - length / 2 - 0.03), math.sin(a) * (r_out - length / 2 - 0.03), z), IRON, (0, 0, a - math.pi / 2))
    return lb.box('tick', (w, 0.03, length), (math.cos(a) * (r_out + 0.005), math.sin(a) * (r_out + 0.005), z), IRON, (0, 0, a - math.pi / 2))

def clock_ring(name, rad, depth, arc, start, spokes, numerals):
    t0, t1 = start, start + arc; parts = []; hz = depth / 2; rim_h = depth * 0.18; wall = 0.42 if numerals else 0.32
    rin = rad - wall - 0.45
    parts.append(band('cobalt', rad - wall, rad - 0.2, -hz + rim_h, hz - rim_h, t0, t1, COBALT))          # star-map inset, inner face too
    for s in (1, -1):
        z0, z1 = sorted((s * (hz - rim_h), s * hz))
        parts.append(band('rim', rin, rad, z0, z1, t0, t1, BRASS))
        parts.append(lb.torus('rim_bead', rad, 0.07, (0, 0, s * hz), POL, seg=int(rad * 12), tube=6, arc=arc, start=180 - t1))
        parts.append(lb.torus('rim_inner', rin, 0.05, (0, 0, s * hz), POL, seg=int(rad * 9), tube=5, arc=arc, start=180 - t1))
        parts.append(lb.torus('rim_step', rad + 0.02, 0.04, (0, 0, s * (hz - rim_h)), POL, seg=int(rad * 10), tube=5, arc=arc, start=180 - t1))
    # the 60-station scale on both rim faces and both rim walls, every fifth tick major
    for i in range(60):
        th = start + i * 6
        if th > t1 - 0.5: break
        major = i % 5 == 0; fl = (rad - rin) * (0.75 if major else 0.4); wl = rim_h * (0.8 if major else 0.45)
        for s in (1, -1):
            parts.append(tick(th, rad, fl, s * (hz + 0.01), s, 0.07 if major else 0.045))
            if numerals or major: parts.append(tick(th, rad, wl, s * (hz - rim_h / 2), 0, 0.07 if major else 0.045))
    # twelve medallions on the outer wall, counter-clockwise seen from above (numbers fall in Blender angle)
    med_r = min(hz - rim_h - 0.12, 0.78)
    for n in range(1, 13):
        th = 180 - (90 - 30 * n)   # Blender angle 90° - 30n, written as theta for U()
        th = start + (th - start) % 360
        if th < t0 + 4 or th > t1 - 4: continue
        disc = lb.cyl('medallion', med_r, 0.14, 32, (0, 0, 0), POL, (0, 0, 0)); frame(disc, th, rad - 0.2, 0)
        ring = lb.torus('medallion_ring', med_r, 0.06, (0, 0, 0.14), BRASS, seg=32, tube=5); frame(ring, th, rad - 0.2, 0)
        parts += [disc, ring]
        if numerals:
            g = numeral(str(n), med_r * 1.05, 0.05); frame(g, th, rad - 0.2 + 0.13, 0); parts.append(g)
        else:
            parts.append(frame(star('medallion_star', med_r * 0.7, IRON), th, rad - 0.2 + 0.14, 0))
        for s in (1, -1): parts.append(frame(diamond('stud', 0.14, 0.26, POL), th, rad - 0.2, s * (hz - rim_h - 0.12)))
        mid = th + 15
        if mid < t1 - 3: parts.append(frame(star('star', med_r * 0.55, POL), mid, rad - 0.2, 0))
        if mid < t1 - 3: parts.append(lb.box('strut', (0.16, 0.12, depth - 2 * rim_h), (math.cos(U(mid)) * (rad - wall - 0.06), math.sin(U(mid)) * (rad - wall - 0.06), 0), BRASS, (0, 0, U(mid) - math.pi / 2)))
    for i in range(spokes):
        th = start + (i + 0.5) * arc / spokes; a = U(th); L = rin - 0.4
        parts.append(lb.box('spoke', (0.55, L, 0.4), (math.cos(a) * L / 2, math.sin(a) * L / 2, 0), BRASS, (0, 0, a - math.pi / 2)))
        parts.append(lb.box('spoke_rib', (0.18, L, 0.08), (math.cos(a) * L / 2, math.sin(a) * L / 2, 0.22), POL, (0, 0, a - math.pi / 2)))
    if spokes:
        parts += [lb.cyl('hub', 0.75, 0.6, 24, (0, 0, -0.3), IRON), lb.torus('hub_collar', 0.75, 0.08, (0, 0, 0.3), POL, seg=32, tube=5)]
    # jagged broken end: the band snaps off in three stepped, offset shards
    for k in range(3):
        th = t1 + 2.5 + k * 3.2; a = U(th)
        parts.append(lb.box('shard', (0.9 - 0.2 * k, rad - rin, depth * (0.8 - 0.2 * k)), (math.cos(a) * (rad + rin) / 2, math.sin(a) * (rad + rin) / 2, (k - 1) * 0.25 * depth), BRASS, (0.15 * k, 0.1, a - math.pi / 2)))
    o = lb.join(parts, name, [BRASS, POL, IRON, COBALT]); lb.cube_uv(o); lb.finish(o); return o

astro = clock_ring('Astrolabe', 14, 3.0, 320, 95, 0, True)
right = clock_ring('Ring_Right', 10, 1.6, 290, 200, 3, False)
low = clock_ring('Ring_Low', 8, 1.4, 300, 30, 3, False)
fr = band('Astro_Fragment', 13.2, 14.0, -1.4, 1.4, 177, 183, BRASS, step=1)
for v in fr.data.vertices: v.co.x -= 13.6       # centre the piece on its own origin
lb.cube_uv(fr); lb.finish(fr)

right.location = (26, 0, 0); low.location = (-22, 0, 0)
lb.preview(os.path.join(here, 'renders', 'rings.png'), (0, -10, 34), (0, 2, 0), lens=30)
right.location = low.location = (0, 0, 0)
right.hide_render = low.hide_render = fr.hide_render = True
lb.preview(os.path.join(here, 'renders', 'astrolabe-wall.png'), (0, -24, -9), (0, -13, 0), lens=40)   # outer wall, seen from below the numerals' feet
lb.preview(os.path.join(here, 'renders', 'astrolabe-face.png'), (0, -26, 16), (0, -12, 0), lens=35)    # the camera-side face
right.hide_render = low.hide_render = fr.hide_render = False
lb.export([astro, right, low, fr], os.path.join(out, 'Rings', 'Rings.fbx'))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'rings.blend')); print('done')
