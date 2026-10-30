# blender -b --factory-startup -P arcanist_astrolabe_build.py -- <out dir> [Arcanist.glb for on-body renders] [--glb <out .glb>]
# Builds the Arcanist's new astrolabe procedurally (remodel plan, build-evidence/arcanist-orb-20261028/remodel-plan.md,
# step 1: geometry; materials are flat placeholders until step 2). Everything is driven by the numbers below.
#   Frame: Blender Z up, the device's front is -Y (its main ring faces him forward), pivot = the globe's centre. The
#   glTF exporter's Y-up conversion turns this into the frame of the shipped mesh (node 'Astrolabe' under 'orbit').
#   Hierarchy (one object per future glTF node; the code drives them by name):
#     Astrolabe_new (empty, the pivot)
#       Astro_Frame      fixed: top and bottom collars with the outer ring's spindle pins
#       Astro_RingOuter  spins about Z (its pins are the spindle)
#         Astro_RingMid    turns about its local X (pins on the outer ring); rest tilt MID_REST
#           Astro_RingInner  turns about its local Z (pins on the mid ring); rest turn INNER_REST
#       Astro_Disc       navy enamel disc with a brass rim behind the globe
#       Astro_Globe      dark glass sphere;  Astro_Core  small emissive sphere inside it
#       Astro_Finial     loop, sapphire bead and swept arm on the top collar
#       Astro_Pendant    pivot at its hang point: chain, caged glass bulb, lilac crystal
#       Astro_Charm1..3  small drops on the bottom collar, each pivoted at its top
#   With every ring angle at 0 the three rings are coplanar, facing front: the Disintegrate lens pose.
# Outputs: review renders (device alone, clay, poses, on his shoulder, true gameplay size next to the old device),
# stats.txt (triangles per part), and with --glb the device alone as a glTF for the patch step.
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]; BODY = argv[1] if len(argv) > 1 and not argv[1].startswith('--') else None
GLB = argv[argv.index('--glb') + 1] if '--glb' in argv else None
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- numbers (metres, degrees)
SEG_O, SEG_M, SEG_I = 48, 44, 40           # segments around each ring
OUTER = dict(r=0.165, w=0.022, d=0.015)    # centreline radius, radial face width, depth along the ring's normal
MID = dict(r=0.130, w=0.018, d=0.013)
INNER = dict(r=0.096, w=0.015, d=0.011)
BEVEL = 0.0025                             # bevelled outer band edge (catches a highlight); hard edges, see obj()
PIN_R = 0.0042; BOSS_R = 0.011; BOSS_D = 0.004; BOSS_GEM = 0.010
MID_REST, INNER_REST, OUTER_IDLE = 55.0, 50.0, -30.0   # rest pose: no ring edge-on to the front or the high-behind camera
GLOBE_R, CORE_R = 0.045, 0.022
DISC_R, DISC_Y, DISC_D, DISC_HOLE, DISC_DISH = 0.060, -0.050, 0.004, 0.018, 0.006   # in FRONT of the globe (the game camera is behind him), an aperture for the beam, dished toward the globe
COLLAR_Z = 0.186                           # top and bottom collars sit just outside the outer ring (+/-)
PEND_TOP = -0.200                          # the pendant's hang point
CHAIN = 4; LINK = 0.016
BULB_R = 0.028; CRYSTAL_L = 0.052
CHARM_AT, CHARM_DROP, CHARM_GEM = (250, 290, 320), 0.024, 0.007   # hung from the outer ring (degrees around it)

def mat(name, col, metal=0.0, rough=0.5, emit=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*col, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = strength
    return m

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
M_BRASS = mat('astrolabe_brass', (0.80, 0.60, 0.26), 0.85, 0.32)
M_ENAMEL = mat('astrolabe_enamel', (0.035, 0.03, 0.09), 0.0, 0.35, (0.16, 0.12, 0.4), 0.15)
M_GLASS = mat('astrolabe_glass', (0.02, 0.016, 0.06), 0.0, 0.08, (0.16, 0.13, 0.44), 0.4)
M_GLASS.node_tree.nodes['Principled BSDF'].inputs['Alpha'].default_value = 0.55   # see-through, so the core shows (glTF alphaMode BLEND)
try: M_GLASS.surface_render_method = 'BLENDED'
except Exception: M_GLASS.blend_method = 'BLEND'
M_GEM = mat('astrolabe_gem', (0.45, 0.35, 0.95), 0.0, 0.15, (0.72, 0.62, 1.0), 0.8)
M_CORE = mat('astrolabe_core', (1, 1, 1), 0.0, 0.5, (0.92, 0.86, 1.0), 12.0)

# ---------------------------------------------------------------- mesh helpers (all build into a bmesh, then an object)
def obj(name, bm, mats, parent=None, loc=(0, 0, 0)):
    bm.edges.ensure_lookup_table()
    sharp = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(35)]
    if sharp: bmesh.ops.split_edges(bm, edges=sharp)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    if parent: o.parent = parent
    o.location = loc
    return o

def band(bm, R, w, d, seg=48, plane='XZ', mi=0, bevel=BEVEL, arc=(0, 360)):
    # a flat ring band: rectangular cross-section (radial width w, depth d) with bevelled corners, swept around the
    # ring's normal. 'XZ' = the ring faces -Y/+Y (front/back).
    b = min(bevel, w / 3, d / 3)
    # six points: the outer edge is bevelled (it catches the highlight), the inner edge is square
    prof = [(-w / 2, -d / 2), (w / 2 - b, -d / 2), (w / 2, -d / 2 + b), (w / 2, d / 2 - b), (w / 2 - b, d / 2), (-w / 2, d / 2)]
    full = abs(arc[1] - arc[0]) >= 360; n = seg if full else max(4, int(seg * abs(arc[1] - arc[0]) / 360))
    rings = []
    for i in range(n if full else n + 1):
        a = math.radians(arc[0] + (arc[1] - arc[0]) * i / n)
        radial = Vector((math.cos(a), 0, math.sin(a))) if plane == 'XZ' else Vector((math.cos(a), math.sin(a), 0))
        normal = Vector((0, 1, 0)) if plane == 'XZ' else Vector((0, 0, 1))
        rings.append([bm.verts.new(radial * (R + px) + normal * pz) for px, pz in prof])
    k = len(prof)
    for i in range(len(rings) - (0 if full else 1)):
        r0, r1 = rings[i], rings[(i + 1) % len(rings)]
        for j in range(k):
            f = bm.faces.new((r0[j], r0[(j + 1) % k], r1[(j + 1) % k], r1[j])); f.material_index = mi
    if not full:
        for rr in (rings[0], rings[-1]):
            f = bm.faces.new(rr); f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

def cyl(bm, p0, p1, r, seg=12, mi=0, r1=None):
    p0, p1 = Vector(p0), Vector(p1); h = (p1 - p0).length
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r1 is None else r1, depth=h)
    q = (p1 - p0).to_track_quat('Z', 'Y'); M = Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=M, verts=res['verts'])
    for f in {f for v in res['verts'] for f in v.link_faces}: f.material_index = mi

def sphere(bm, c, r, seg=16, rings=10, mi=0, scale=(1, 1, 1)):
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(c) @ Matrix.Diagonal((*scale, 1)), verts=res['verts'])
    for f in {f for v in res['verts'] for f in v.link_faces}: f.material_index = mi

def torus(bm, c, R, r, axis='Y', seg=24, rseg=8, mi=0):
    vs = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; row = []
        for j in range(rseg):
            b = 2 * math.pi * j / rseg; rad = R + r * math.cos(b); z = r * math.sin(b)
            p = Vector((rad * math.cos(a), z, rad * math.sin(a))) if axis == 'Y' else Vector((rad * math.cos(a), rad * math.sin(a), z)) if axis == 'Z' else Vector((z, rad * math.cos(a), rad * math.sin(a)))
            row.append(bm.verts.new(Vector(c) + p))
        vs.append(row)
    for i in range(seg):
        for j in range(rseg):
            f = bm.faces.new((vs[i][j], vs[(i + 1) % seg][j], vs[(i + 1) % seg][(j + 1) % rseg], vs[i][(j + 1) % rseg])); f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=list({f for row in vs for v in row for f in v.link_faces}))

def tube(bm, pts, r0, r1, seg=8, mi=0):
    # a tapered tube along a polyline (the finial's swept arm)
    pts = [Vector(p) for p in pts]; rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u = t.cross(Vector((0, 1, 0))); u = u.normalized() if u.length > 1e-6 else Vector((1, 0, 0)); w = t.cross(u)
        r = r0 + (r1 - r0) * i / (len(pts) - 1)
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * j / seg) + w * math.sin(2 * math.pi * j / seg)) * r) for j in range(seg)])
    for i in range(len(rings) - 1):
        for j in range(seg):
            f = bm.faces.new((rings[i][j], rings[i][(j + 1) % seg], rings[i + 1][(j + 1) % seg], rings[i + 1][j])); f.material_index = mi
    f = bm.faces.new(rings[0][::-1]); f.material_index = mi
    tip = bm.verts.new(pts[-1] + (pts[-1] - pts[-2]).normalized() * r1 * 2)
    for j in range(seg): f = bm.faces.new((rings[-1][j], rings[-1][(j + 1) % seg], tip)); f.material_index = mi

def flange(bm, r0, r1, y0, tilt, thick, seg, mi=0):
    # a flat ring (r0..r1) facing -Y, its outer edge tilted `tilt` degrees toward +Y; sharp edges
    t = math.tan(math.radians(tilt))
    loops = [(r0, y0 - thick / 2), (r1, y0 - thick / 2 + (r1 - r0) * t), (r1, y0 + thick / 2 + (r1 - r0) * t), (r0, y0 + thick / 2)]
    vs = [[bm.verts.new(Vector((r * math.cos(2 * math.pi * i / seg), y, r * math.sin(2 * math.pi * i / seg)))) for (r, y) in loops] for i in range(seg)]
    for i in range(seg):
        for j in range(4):
            f = bm.faces.new((vs[i][j], vs[(i + 1) % seg][j], vs[(i + 1) % seg][(j + 1) % 4], vs[i][(j + 1) % 4])); f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=list({f for row in vs for v in row for f in v.link_faces}))

def dish(bm, r0, r1, y0, depth, thick, seg, mi=0):
    # an annulus (hole r0, rim r1) curved like a shallow cap: its rim is `depth` nearer the globe (+Y) than its hole
    prof = []
    for r in (r0 + (r1 - r0) * k / 4 for k in range(5)):
        prof.append((r, y0 + depth * ((r - r0) / (r1 - r0)) ** 2))
    loops = [(r, y - thick / 2) for (r, y) in prof] + [(r, y + thick / 2) for (r, y) in prof[::-1]]
    vs = [[bm.verts.new(Vector((r * math.cos(2 * math.pi * i / seg), y, r * math.sin(2 * math.pi * i / seg)))) for (r, y) in loops] for i in range(seg)]
    k = len(loops)
    for i in range(seg):
        for j in range(k):
            f = bm.faces.new((vs[i][j], vs[(i + 1) % seg][j], vs[(i + 1) % seg][(j + 1) % k], vs[i][(j + 1) % k])); f.material_index = mi
    bmesh.ops.recalc_face_normals(bm, faces=list({f for row in vs for v in row for f in v.link_faces}))

def gem(bm, c, r, h_up, h_dn, seg=6, mi=0):
    top = bm.verts.new(Vector(c) + Vector((0, 0, h_up))); bot = bm.verts.new(Vector(c) - Vector((0, 0, h_dn)))
    ring = [bm.verts.new(Vector(c) + Vector((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), 0))) for i in range(seg)]
    for i in range(seg):
        f = bm.faces.new((ring[i], ring[(i + 1) % seg], top)); f.material_index = mi
        f = bm.faces.new((ring[(i + 1) % seg], ring[i], bot)); f.material_index = mi

# ---------------------------------------------------------------- build
root = bpy.data.objects.new('Astrolabe_new', None); sc.collection.objects.link(root); root.empty_display_size = 0.05

# frame: top and bottom collars (short brass cylinders with a lip) and the spindle pins into the outer ring
bm = bmesh.new()
for s in (1, -1):
    z0 = s * (OUTER['r'] + OUTER['w'] / 2)              # outer ring's outer edge
    cyl(bm, (0, 0, z0), (0, 0, s * COLLAR_Z), PIN_R, 10)                     # spindle pin
    cyl(bm, (0, 0, s * COLLAR_Z), (0, 0, s * (COLLAR_Z + 0.012)), 0.013, 14)  # collar
    torus(bm, (0, 0, s * (COLLAR_Z + 0.006)), 0.0135, 0.0025, axis='Z', seg=16, rseg=5)
cyl(bm, (0, 0, -(COLLAR_Z + 0.012)), (0, 0, PEND_TOP + 0.002), 0.004, 8)    # the pendant's hanger
frame = obj('Astro_Frame', bm, [M_BRASS], root)

# outer ring (spins about Z) with the bosses where the mid ring's pins come through on X
bm = bmesh.new(); band(bm, OUTER['r'], OUTER['w'], OUTER['d'], SEG_O)
for s in (1, -1):
    x_out = s * (OUTER['r'] + OUTER['w'] / 2); x_in = s * (MID['r'] + MID['w'] / 2)
    cyl(bm, (x_in, 0, 0), (x_out + s * BOSS_D, 0, 0), PIN_R, 10)
    cyl(bm, (x_out, 0, 0), (x_out + s * BOSS_D, 0, 0), BOSS_R, 12)
    sphere(bm, (x_out + s * (BOSS_D + BOSS_GEM * 0.35), 0, 0), BOSS_GEM, 10, 6, mi=1, scale=(0.6, 1, 1))   # the concept's side jewel
outer = obj('Astro_RingOuter', bm, [M_BRASS, M_GEM], root)

bm = bmesh.new(); band(bm, MID['r'], MID['w'], MID['d'], SEG_M)
for s in (1, -1):
    z_out = s * (MID['r'] - MID['w'] / 2); z_in = s * (INNER['r'] + INNER['w'] / 2)
    cyl(bm, (0, 0, z_in), (0, 0, z_out), PIN_R * 0.9, 10)
    cyl(bm, (0, 0, s * (MID['r'] + MID['w'] / 2)), (0, 0, s * (MID['r'] + MID['w'] / 2 + BOSS_D * 0.8)), BOSS_R * 0.85, 14)
mid = obj('Astro_RingMid', bm, [M_BRASS], outer)
mid.rotation_euler = (math.radians(MID_REST), 0, 0)

bm = bmesh.new(); band(bm, INNER['r'], INNER['w'], INNER['d'], SEG_I)
inner = obj('Astro_RingInner', bm, [M_BRASS], mid)
inner.rotation_euler = (0, 0, math.radians(INNER_REST))

# enamel disc behind the globe, with a brass rim
bm = bmesh.new()
dish(bm, DISC_HOLE, DISC_R, DISC_Y, DISC_DISH, DISC_D, 32, mi=1)
flange(bm, DISC_R - 0.004, DISC_R, DISC_Y + DISC_DISH, 20.0, 0.0015, 32)   # flat brass flange, tilted 20 deg toward the globe
flange(bm, DISC_HOLE - 0.002, DISC_HOLE, DISC_Y - DISC_D / 2, 0.0, 0.003, 16)   # a 2 mm lip round the aperture
disc = obj('Astro_Disc', bm, [M_BRASS, M_ENAMEL], root)

bm = bmesh.new(); sphere(bm, (0, 0, 0), GLOBE_R, 24, 14); globe = obj('Astro_Globe', bm, [M_GLASS], root)
bm = bmesh.new(); sphere(bm, (0, 0, 0), CORE_R, 24, 16); core = obj('Astro_Core', bm, [M_CORE], root)

# finial: a stem, a sapphire bead, a loop and a swept arm (the concept's hook) off to his right
bm = bmesh.new()
zt = COLLAR_Z + 0.012
cyl(bm, (0, 0, zt), (0, 0, zt + 0.014), 0.0045, 10)
sphere(bm, (0, 0, zt + 0.025), 0.013, 12, 8, mi=1)
torus(bm, (0, 0, zt + 0.050), 0.014, 0.0028, axis='Y', seg=16, rseg=5)
cyl(bm, (0, 0, zt + 0.033), (0, 0, zt + 0.034), 0.004, 8)
tube(bm, [(-0.004, 0, zt + 0.03), (-0.030, 0, zt + 0.040), (-0.058, 0, zt + 0.050), (-0.080, 0, zt + 0.056), (-0.092, 0, zt + 0.056), (-0.100, 0, zt + 0.050), (-0.102, 0, zt + 0.041)], 0.0045, 0.0013)   # swept arm with a 15 mm downturned hook
finial = obj('Astro_Finial', bm, [M_BRASS, M_GEM], root)

# pendant: pivot at its hang point; chain links, a caged glass bulb, the lilac crystal
bm = bmesh.new(); z = 0.0
for i in range(CHAIN):
    torus(bm, (0, 0, z - LINK / 2), LINK / 2, 0.0018, axis='Y' if i % 2 == 0 else 'X', seg=10, rseg=4); z -= LINK * 0.85
cb = z - BULB_R
sphere(bm, (0, 0, cb), BULB_R * 0.92, 12, 8, mi=1)
for k in range(3):   # the cage: three brass meridians
    a = math.pi * k / 3
    for j in range(6):
        t0, t1 = math.pi * j / 6, math.pi * (j + 1) / 6
        p0 = Vector((math.sin(t0) * math.cos(a), math.sin(t0) * math.sin(a), math.cos(t0))) * BULB_R + Vector((0, 0, cb))
        p1 = Vector((math.sin(t1) * math.cos(a), math.sin(t1) * math.sin(a), math.cos(t1))) * BULB_R + Vector((0, 0, cb))
        cyl(bm, p0, p1, 0.0016, 4)
cyl(bm, (0, 0, cb + BULB_R), (0, 0, cb + BULB_R + 0.004), 0.006, 10)
cyl(bm, (0, 0, cb - BULB_R - 0.004), (0, 0, cb - BULB_R), 0.005, 10)
gem(bm, (0, 0, cb - BULB_R - 0.004 - CRYSTAL_L * 0.25), 0.009, CRYSTAL_L * 0.25, CRYSTAL_L * 0.75, mi=2)
pend = obj('Astro_Pendant', bm, [M_BRASS, M_GLASS, M_GEM], root, loc=(0, 0, PEND_TOP))

charms = []
for k in range(3):
    a = math.radians(CHARM_AT[k]); ro = OUTER['r'] + OUTER['w'] / 2; top = Vector((ro * math.cos(a), 0, ro * math.sin(a)))
    bm = bmesh.new()
    torus(bm, (0, 0, -0.004), 0.004, 0.0012, axis='X', seg=10, rseg=4)
    gem(bm, (0, 0, -0.008 - CHARM_DROP * 0.35), CHARM_GEM, CHARM_DROP * 0.35, CHARM_DROP * 0.65, seg=6)
    charms.append(obj(f'Astro_Charm{k + 1}', bm, [M_BRASS], outer, loc=top))

PARTS = [frame, outer, mid, inner, disc, globe, core, finial, pend] + charms
_cl = bpy.data.lights.new('CoreLight', 'POINT'); _cl.energy = 1.5; _cl.color = (0.78, 0.72, 1.0); _cl.shadow_soft_size = 0.01
core_light = bpy.data.objects.new('CoreLight', _cl); sc.collection.objects.link(core_light); core_light.parent = root
with open(os.path.join(OUT, 'stats.txt'), 'w') as f:
    tot = 0
    for o in PARTS:
        o.data.calc_loop_triangles(); n = len(o.data.loop_triangles); tot += n; f.write(f'{o.name}\t{n}\n')
    f.write(f'TOTAL\t{tot}\n')
print('TRIS', tot)

def set_pose(mid_a, inner_a, outer_a=0.0):
    outer.rotation_euler = (0, 0, math.radians(outer_a)); mid.rotation_euler = (math.radians(mid_a), 0, 0); inner.rotation_euler = (0, 0, math.radians(inner_a))

if '--flat' not in argv:
    # step 2: baked brass, enamel with star windows, pale glass, emissive gem and core (arcanist_astrolabe_materials.py)
    exec(open(os.path.join(os.path.dirname(os.path.abspath(sys.argv[sys.argv.index('-P') + 1])), 'arcanist_astrolabe_materials.py'), encoding='utf-8').read())

if GLB:
    for o in bpy.data.objects: o.select_set(o == root or o in PARTS)
    bpy.ops.export_scene.gltf(filepath=GLB, use_selection=True, export_yup=True, export_apply=False, export_animations=False, export_image_format='AUTO')
    print('EXPORTED', GLB)

# ---------------------------------------------------------------- review renders
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'BLENDER_EEVEE_NEXT'
sc.view_settings.view_transform = 'AgX'
try: sc.eevee.taa_render_samples = 32
except Exception: pass
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True; nt = w.node_tree; bg = nt.nodes['Background']
tc = nt.nodes.new('ShaderNodeTexCoord'); sep = nt.nodes.new('ShaderNodeSeparateXYZ'); ramp = nt.nodes.new('ShaderNodeValToRGB')
nt.links.new(tc.outputs['Generated'], sep.inputs[0]); nt.links.new(sep.outputs['Z'], ramp.inputs[0])
ramp.color_ramp.elements[0].position = 0.45; ramp.color_ramp.elements[0].color = (0.05, 0.06, 0.14, 1)   # navy below (the arena floor)
ramp.color_ramp.elements[1].position = 0.6; ramp.color_ramp.elements[1].color = (0.85, 0.80, 0.68, 1)   # warm cream sky above (the arena stone)
nt.links.new(ramp.outputs[0], bg.inputs['Color']); bg.inputs['Strength'].default_value = 0.8
def light(name, kind, loc, energy, color=(1, 1, 1), size=1.0, target=(0, 0, 1.2)):
    ld = bpy.data.lights.new(name, kind); ld.energy = energy; ld.color = color
    if kind == 'AREA': ld.size = size
    o = bpy.data.objects.new(name, ld); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); return o
BLOOM = False
try:
    # bloom, like the game's post-processing (Blender 5 compositor node group)
    ng = bpy.data.node_groups.new('Bloom', 'CompositorNodeTree'); sc.compositing_node_group = ng
    ng.interface.new_socket('Image', in_out='OUTPUT', socket_type='NodeSocketColor')
    rl = ng.nodes.new('CompositorNodeRLayers'); gl = ng.nodes.new('CompositorNodeGlare'); go = ng.nodes.new('NodeGroupOutput')
    for k, v in (('Type', 'Bloom'), ('Threshold', 1.0), ('Strength', 0.6), ('Size', 0.6)):
        if k in gl.inputs:
            try: gl.inputs[k].default_value = v
            except Exception: pass
    if hasattr(gl, 'glare_type'): gl.glare_type = 'BLOOM'
    ng.links.new(rl.outputs['Image'], gl.inputs['Image']); ng.links.new(gl.outputs['Image'], go.inputs[0]); BLOOM = True
except Exception as e: print('BLOOM unavailable', e)
print('BLOOM', BLOOM)
light('Key', 'SUN', (3, -4, 6), 3.2, (1.0, 0.95, 0.88)); light('Rim', 'AREA', (-3, 4, 3), 400, (0.7, 0.75, 1.0), 3); light('Fill', 'AREA', (-4, -3, 2), 150, (0.9, 0.9, 1.0), 4)
def camera(name, loc, target, lens=50):
    cd = bpy.data.cameras.new(name); cd.lens = lens; cd.sensor_fit = 'VERTICAL'; cd.sensor_height = 24; cd.clip_start = 0.01
    c = bpy.data.objects.new(name, cd); sc.collection.objects.link(c); c.location = loc
    c.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); return c
def sph(az, el, dist, tgt):
    a, e = math.radians(az), math.radians(el)
    return tuple(Vector(tgt) + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist)
def render(cam, res, path):
    sc.camera = cam; sc.render.resolution_x, sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)

set_pose(MID_REST, INNER_REST, OUTER_IDLE)
cd = os.path.join(OUT, 'close'); os.makedirs(cd, exist_ok=True)
tgt = Vector((0, 0, -0.02))
VIEWS = (('front', 0, 5, 1.35), ('front34', -40, 15, 1.35), ('right_side', -90, 5, 1.35), ('back', 180, 5, 1.35),
         ('top', 0, 85, 1.2), ('below', 20, -45, 1.35), ('iso_gameplay_angle', 150, 57, 1.35), ('detail_core', -15, 10, 0.6))
for tag, az, el, dist in VIEWS:
    t = Vector((0, 0, 0)) if tag == 'detail_core' else tgt
    render(camera('c_' + tag, sph(az, el, dist, t), t), (900, 900), os.path.join(cd, f'{tag}.png'))
# clay
saved = {o: list(o.data.materials) for o in PARTS}
clay = mat('clay', (0.7, 0.7, 0.7), 0, 0.6)
for o in PARTS:
    for i in range(len(o.data.materials)): o.data.materials[i] = clay
for tag, az, el in (('clay_front', 0, 5), ('clay_front34', -40, 15), ('clay_side', -90, 5), ('clay_top', 0, 85)):
    render(camera('k_' + tag, sph(az, el, 1.35, tgt), tgt), (900, 900), os.path.join(cd, f'{tag}.png'))
for o, ms in saved.items():
    for i, m in enumerate(ms): o.data.materials[i] = m
# poses: rest, lens (rings coplanar facing front), surge (inner ring snapped flat inside the mid ring), spun apart
pd = os.path.join(OUT, 'poses'); os.makedirs(pd, exist_ok=True)
cam = camera('p_cam', sph(-30, 12, 1.35, tgt), tgt)
for tag, pose in (('rest', (MID_REST, INNER_REST, OUTER_IDLE)), ('lens', (0, 0, 0)), ('surge', (0, -OUTER_IDLE, OUTER_IDLE)), ('apart', (35, 140, 50)), ('turned90', (MID_REST, INNER_REST, 90))):
    set_pose(pose[0], pose[1], pose[2]); inner.scale = (0.58,) * 3 if tag == 'surge' else (1, 1, 1)   # Surge: the inner ring tightens onto the globe
    render(cam, (700, 700), os.path.join(pd, f'{tag}.png'))
inner.scale = (1, 1, 1); set_pose(MID_REST, INNER_REST, OUTER_IDLE)

# on his shoulder, and true gameplay size next to the old device
if BODY:
    bpy.ops.import_scene.gltf(filepath=BODY)
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); body = bpy.data.objects['Arcanist']; old = bpy.data.objects['Astrolabe']
    for t in arm.animation_data.nla_tracks: t.mute = True
    act = bpy.data.actions['Idle']; arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]
    sc.frame_set(int(act.frame_range[0]))
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); floor = bpy.context.object
    fm = mat('Floor', (0.16, 0.15, 0.22), 0, 0.8); floor.data.materials.append(fm)
    root.parent = old; root.matrix_parent_inverse = Matrix.Identity(4); root.location = (0, 0, 0); root.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    bd = os.path.join(OUT, 'body'); os.makedirs(bd, exist_ok=True)
    def both(tag, cam, res):
        old.hide_render = True
        for o in PARTS: o.hide_render = False
        render(cam, res, os.path.join(bd, f'{tag}_new.png'))
        old.hide_render = False
        for o in PARTS: o.hide_render = True
        render(cam, res, os.path.join(bd, f'{tag}_old.png'))
        for o in PARTS: o.hide_render = False
        old.hide_render = True
    both('shoulder34', camera('b_sh', sph(-55, 12, 2.4, (-0.25, 0, 1.45)), (-0.25, 0, 1.45)), (640, 640))
    both('front', camera('b_fr', sph(-15, 8, 4.2, (0, -0.1, 1.05)), (0, -0.1, 1.05)), (560, 640))
    both('back34', camera('b_bk', sph(150, 15, 3.0, (-0.2, 0, 1.3)), (-0.2, 0, 1.3)), (600, 640))
    # gameplay: the game's camera is high behind him (about 55 deg down); he is ~75 px tall at 1600x900
    both('gameplay', camera('b_px', sph(180, 55, 24.0, (0, 0, 0.9)), (0, 0, 0.9)), (1600, 900))
    for tag, col in (('navy', (0.010, 0.017, 0.05)), ('cream', (0.69, 0.60, 0.42))):   # #1A2340 and #D9CCAE, linear
        fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*col, 1)
        both('gameplay_' + tag, bpy.data.objects['b_px'], (1600, 900))
    fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.16, 0.15, 0.22, 1)
    both('gameplay_side', camera('b_px2', sph(110, 55, 24.0, (0, 0, 0.9)), (0, 0, 0.9)), (1600, 900))
    set_pose(0, 0, 0); old.hide_render = True
    render(bpy.data.objects['b_px'], (1600, 900), os.path.join(bd, 'gameplay_lens_new.png')); set_pose(MID_REST, INNER_REST, OUTER_IDLE)
print('DONE')
