# blender -b <work>/crucivar_prep.blend -P crucivar_meshy_rig.py -- <work> <out.glb> [<render dir>]
# The Meshy sculpt of Crucivar (prepped by crucivar_meshy_prep.py: 3.8 m, feet at 0, faces -Y, ~60k tris) on the same
# rigid rig, clips and code-driven parts as the scripted model (crucivar_build.py), so the encounter and CrucivarRig
# work unchanged:
#   - the sculpt is cut into rigid pieces by region (each face goes to one bone: no stretching metal);
#   - the sculpted flywheel is cut out as its own node, so CrucivarRig spins the real one;
#   - shutters, flank vents, back vent, gauge, insulation plates, flywheel guard, shoulder fins and the crucible lip are
#     added on the sculpt's surface (raycast), with the same names and pivots as before.
# Joint positions were measured from the prep step's 0.25 m grid renders (measure_front / side / back).
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree
args = sys.argv[sys.argv.index('--') + 1:]
WORK, OUT = args[0], args[1]
RENDER = args[2] if len(args) > 2 else None
HERE = os.path.dirname(os.path.abspath(__file__))
sc = bpy.context.scene; sc.render.fps = 30
exec(open(os.path.join(HERE, 'crucivar_common.py'), encoding='utf-8').read())
parts = []
body = bpy.data.objects['Crucivar']
# the add-on parts take the sculpt's palette: coppery brass, dark scorched tiles
def recolour(m, rgb): m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*rgb, 1)
recolour(BRASS, (0.42, 0.22, 0.09)); recolour(BRASS_DARK, (0.22, 0.12, 0.05)); recolour(CERAMIC, (0.065, 0.05, 0.04))

# ---------------- armature (measured on the sculpt) ----------------
B = {
    'root': ((0, 0, 0), (0, 0.6, 0), None),
    'pelvis': ((0, -0.05, 0.95), (0, -0.05, 1.3), 'root'),
    'torso': ((0, 0, 1.3), (0, 0, 3.0), 'pelvis'),
    'thigh.R': ((-0.70, -0.10, 0.95), (-0.70, -0.30, 0.58), 'pelvis'),
    'shin.R': ((-0.70, -0.30, 0.58), (-0.70, -0.15, 0.22), 'thigh.R'),
    'foot.R': ((-0.70, -0.15, 0.22), (-0.70, -0.60, 0.08), 'shin.R'),
    'thigh.L': ((0.76, -0.10, 0.95), (0.76, -0.30, 0.58), 'pelvis'),
    'shin.L': ((0.76, -0.30, 0.58), (0.76, -0.15, 0.22), 'thigh.L'),
    'foot.L': ((0.76, -0.15, 0.22), (0.76, -0.60, 0.08), 'shin.L'),
    'upperarm.R': ((-1.05, -0.05, 2.72), (-1.45, -0.30, 2.05), 'torso'),
    'forearm.R': ((-1.45, -0.30, 2.05), (-1.65, -0.70, 1.15), 'upperarm.R'),
    'hammer.R': ((-1.65, -0.70, 1.15), (-1.70, -1.00, 0.50), 'forearm.R'),
    'upperarm.L': ((1.08, -0.05, 2.62), (1.35, -0.30, 2.02), 'torso'),
    'forearm.L': ((1.35, -0.30, 2.02), (1.45, -0.55, 1.60), 'upperarm.L'),
    'crucible.L': ((1.45, -0.55, 1.60), (1.50, -0.65, 1.00), 'forearm.L'),
}
arm, ad = make_armature(B)

# ---------------- surface queries on the sculpt (before it is cut) ----------------
dg = bpy.context.evaluated_depsgraph_get()
bvh = BVHTree.FromObject(body, dg)
def surf(origin, direction):
    loc, nrm, _, _ = bvh.ray_cast(Vector(origin), Vector(direction).normalized(), 10.0)
    if loc is None: raise RuntimeError('no surface hit from %s' % (origin,))
    return loc, nrm

# ---------------- cut the sculpt into rigid parts ----------------
FW_C = Vector((-0.225, 0, 2.125)); FW_R = 0.60   # flywheel centre (x, z) and radius on the back, from measure_back
me = body.data
fc = np.empty(len(me.polygons) * 3); me.polygons.foreach_get('center', fc); fc = fc.reshape(-1, 3)
x, y, z = fc[:, 0], fc[:, 1], fc[:, 2]
label = np.full(len(fc), 'torso', dtype=object)
label[z < 1.15] = 'pelvis'
# legs: below the skirt, away from the codpiece
legs = (z < 0.92) & (np.abs(x) > 0.30)
label[legs & (x < 0)] = 'R'; label[legs & (x > 0)] = 'L'
for s in ('R', 'L'):
    m = label == s
    label[m & (z < 0.24)] = 'foot.' + s; label[m & (z >= 0.24) & (z < 0.60)] = 'shin.' + s; label[m & (z >= 0.60)] = 'thigh.' + s
# hammer arm (Crucivar's right, -X): the pauldron above 2.25 m, everything further out below
ham = ((x < -0.92) & (z > 2.25) & (y < 0.18)) | (x < -1.12)
label[ham & (z < 1.12)] = 'hammer.R'; label[ham & (z >= 1.12) & (z < 2.05)] = 'forearm.R'; label[ham & (z >= 2.05)] = 'upperarm.R'
# crucible arm (+X): pauldron above 2.1 m; the bowl and forearm further out, leaving the left leg alone
cru = ((x > 0.95) & (z > 2.1) & (y < 0.18)) | ((x > 1.05) & (z > 0.60) & ~((z < 0.88) & (x < 1.20)))
label[cru & (z < 1.62)] = 'crucible.L'; label[cru & (z >= 1.62) & (z < 2.02)] = 'forearm.L'; label[cru & (z >= 2.02)] = 'upperarm.L'
# arm faces far from their own bone are torso bits caught by the region boxes (pipes, collar): give them back, so
# nothing flies off with a raised limb
REACH = {'upperarm.R': 0.58, 'forearm.R': 0.62, 'hammer.R': 1.00, 'upperarm.L': 0.58, 'forearm.L': 0.55, 'crucible.L': 1.10}
UP = {'upperarm.R': 'torso', 'forearm.R': 'upperarm.R', 'hammer.R': 'forearm.R', 'upperarm.L': 'torso', 'forearm.L': 'upperarm.L', 'crucible.L': 'forearm.L'}
moved = 0
for _ in range(3):
    for n, reach in REACH.items():
        h, t = np.array(B[n][0]), np.array(B[n][1]); d = t - h
        idx = np.nonzero(label == n)[0]
        if not len(idx): continue
        p = fc[idx]; u = np.clip(((p - h) @ d) / (d @ d), 0, 1)
        far = np.linalg.norm(p - (h + u[:, None] * d), axis=1) > reach
        label[idx[far]] = UP[n]; moved += int(far.sum())
print('REASSIGNED', moved)
# the sculpted flywheel: faces standing proud of the back inside the wheel's disc
disc = (np.hypot(x - FW_C.x, z - FW_C.z) < FW_R) & (label == 'torso') & (y > 0.80)
yd = y[disc]
print('FLYWHEEL_Y', np.percentile(yd, [5, 25, 50, 75, 95]).round(3) if len(yd) else 'none')
FW_YMIN = float(np.percentile(yd, 35)) if len(yd) else 9
label[disc & (y > FW_YMIN)] = 'Flywheel'
for n in sorted(set(label)): print('PART', n, int((label == n).sum()))

def cut(name, faces, bone, pivot=(0, 0, 0)):
    ob = body.copy(); ob.data = body.data.copy(); ob.name = ob.data.name = name; sc.collection.objects.link(ob)
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    keep = set(int(i) for i in faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    bm.to_mesh(ob.data); bm.free()
    pv = Vector(pivot); ob.data.transform(Matrix.Translation(-pv)); ob.location = pv
    parts.append((ob, bone)); return ob
for n in sorted(set(label)):
    idx = np.nonzero(label == n)[0]
    if n == 'Flywheel':
        fwy = float(y[idx].mean())
        cut('Flywheel', idx, 'torso', pivot=(FW_C.x, fwy, FW_C.z))
    else:
        cut('Body_' + n.replace('.', '_'), idx, n)
bpy.data.objects.remove(body)
FW_FRONT = float(y[label == 'Flywheel'].max()) if (label == 'Flywheel').any() else 1.1

# ---------------- code-driven parts, placed on the sculpt ----------------
# belly shutters: brass frames over the three sculpted furnace windows, hinged on their top edge
for i, wx in enumerate((-0.35, 0.01, 0.39)):
    loc, _ = surf((wx, -3, 1.57), (0, 1, 0)); sy = loc.y - 0.035; hz = 1.57 + 0.25
    bm = bmesh.new()
    for (w, h, cx, cz) in ((0.30, 0.05, 0, -0.025), (0.30, 0.05, 0, -0.475), (0.05, 0.50, -0.125, -0.25), (0.05, 0.50, 0.125, -0.25),
                           (0.22, 0.03, 0, -0.17), (0.22, 0.03, 0, -0.33)):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (wx + cx, sy, hz + cz), (w, 0.04, h)), verts=g['verts'])
    finish(bm, 'Shutter_%d' % i, BRASS, 'torso', pivot=(wx, sy, hz), bevel=0.008)
# flank vents (left / right Pressure Release): an ember slot behind a hinged brass grille on each side
for s, tag in ((1, 'L'), (-1, 'R')):
    loc, _ = surf((3 * s, 0.0, 1.55), (-s, 0, 0)); fx = loc.x
    box('FlankGlow' + tag, (0.05, 0.50, 0.38), (fx - 0.01 * s, 0.0, 1.55), EMBER, 'torso', bevel=0.008)
    box('FlankFrame' + tag, (0.05, 0.60, 0.48), (fx - 0.025 * s, 0.0, 1.55), IRON_DARK, 'torso', bevel=0.01)
    bm = bmesh.new()
    for k in range(4):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (fx + 0.03 * s, 0.0, 1.74 - 0.11 * k - 0.04), (0.03, 0.56, 0.06)), verts=g['verts'])
    for yy in (-0.27, 0.27):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (fx + 0.03 * s, yy, 1.57), (0.035, 0.05, 0.46)), verts=g['verts'])
    finish(bm, 'FlankVent_' + tag, BRASS, 'torso', pivot=(fx + 0.03 * s, 0.0, 1.78), bevel=0.008)
# back exhaust under the flywheel
loc, _ = surf((FW_C.x, 3, 1.35), (0, -1, 0)); by = loc.y
box('BackVentGlow', (0.55, 0.04, 0.24), (FW_C.x, by + 0.01, 1.35), EMBER, 'torso', bevel=0.008)
box('BackVentFrame', (0.65, 0.04, 0.32), (FW_C.x, by - 0.005, 1.35), IRON_DARK, 'torso', bevel=0.01)
bm = bmesh.new()
for k in range(3):
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (FW_C.x, by + 0.04, 1.45 - 0.09 * k - 0.03), (0.62, 0.03, 0.05)), verts=g['verts'])
finish(bm, 'BackVent', BRASS, 'torso', pivot=(FW_C.x, by + 0.04, 1.49), bevel=0.006)
# flywheel guard: an arched brass cage over the top of the wheel, hinged on Crucivar's right edge (-X)
GR = FW_R + 0.07; GY = FW_FRONT + 0.05
bm = bmesh.new(); segs = 14
for k in range(segs):   # the arch band
    a0, a1 = math.pi * k / segs, math.pi * (k + 1) / segs; am = (a0 + a1) / 2
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, -math.degrees(am), 0), (math.cos(am) * GR, 0, math.sin(am) * GR), (0.07, 0.05, 2 * GR * math.sin((a1 - a0) / 2) * 1.08)), verts=g['verts'])
for k in range(1, 6):   # radial bars
    a = math.pi * k / 6
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, -math.degrees(a) + 90, 0), (math.cos(a) * GR * 0.58, 0, math.sin(a) * GR * 0.58), (0.035, 0.035, GR * 0.8)), verts=g['verts'])
g = bmesh.ops.create_cube(bm, size=1.0)
bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0.02), (2 * GR + 0.08, 0.05, 0.06)), verts=g['verts'])   # base bar
bmesh.ops.transform(bm, matrix=Matrix.Translation((FW_C.x, GY, FW_C.z)), verts=bm.verts)
finish(bm, 'FlywheelGuard', BRASS, 'torso', pivot=(FW_C.x - GR, GY, FW_C.z), bevel=0.006)
# pressure gauge on the belly band, right of the windows
loc, _ = surf((0.70, -3, 1.57), (0, 1, 0)); gy = loc.y - 0.03
cyl('GaugeCase', 0.13, 0.13, 0.07, (0.70, gy, 1.57), BRASS, 'torso', rot=(90, 0, 0), seg=24)
cyl('GaugeFace', 0.105, 0.105, 0.02, (0.70, gy - 0.04, 1.57), CERAMIC, 'torso', rot=(90, 0, 0), seg=24)
bm = bmesh.new(); g = bmesh.ops.create_cube(bm, size=1.0)
bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0.70, gy - 0.055, 1.57 + 0.04), (0.016, 0.01, 0.085)), verts=g['verts'])
finish(bm, 'Gauge', IRON_DARK, 'torso', pivot=(0.70, gy - 0.055, 1.57))
# insulation (sheds at Casting Heat): scorched ceramic plates riveted onto the torso, placed on its surface
INS = [(-72, 1.15), (72, 1.15), (-128, 1.35), (128, 1.35), (-62, 2.15), (62, 2.15), (-122, 2.15), (122, 2.15)]
for i, (a, zc) in enumerate(INS):
    d = Vector((math.sin(math.radians(a)), -math.cos(math.radians(a)), 0))
    loc, nrm = surf(d * 3 + Vector((0, 0, zc)), -d)
    nrm = Vector((nrm.x, nrm.y, 0)).normalized() if abs(nrm.z) < 0.7 else d
    yaw = math.degrees(math.atan2(nrm.x, -nrm.y))
    centre = loc + nrm * 0.035
    bm = bmesh.new(); g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0), (0.42, 0.06, 0.38)), verts=g['verts'])
    for rx in (-0.15, 0.15):
        for rz in (-0.13, 0.13):
            g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.022)
            bmesh.ops.translate(bm, vec=(rx, -0.035, rz), verts=g['verts'])
    bmesh.ops.transform(bm, matrix=Matrix.Translation(centre) @ Euler((0, 0, math.radians(yaw))).to_matrix().to_4x4(), verts=bm.verts)
    finish(bm, 'Insulation_%d' % i, CERAMIC, 'torso', pivot=tuple(centre), bevel=0.015)
# shoulder vent fins (folded until Open Pressure): a fan of brass blades between each chimney and pauldron
for s, tag in ((-1, 'R'), (1, 'L')):
    for k in range(4):
        a = -30 + 20 * k
        root = Vector((0.98 * s, 0.10 + 0.14 * (k - 1.5), 2.92))
        rot = Euler((math.radians(a * 0.6), math.radians(-38 * s), 0), 'XYZ')
        bm = bmesh.new(); g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0.36), (0.06, 0.17, 0.72)), verts=g['verts'])
        g = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.045, radius2=0.03, depth=0.12)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0.78), (1, 1, 1)), verts=g['verts'])
        ob = finish(bm, 'VentFin_%s%d' % (tag, k), BRASS_DARK, 'torso', pivot=(0, 0, 0), bevel=0.008)
        ob.location = root; ob.rotation_euler = rot
# crucible spout: where the pour leaves
lip = bpy.data.objects.new('CrucibleLip', None); sc.collection.objects.link(lip); lip.location = (1.52, -0.72, 0.72)
parts.append((lip, 'crucible.L'))

# ---------------- bake modifiers, parent rigidly ----------------
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for ob, bone in parts:
    if ob.type == 'MESH' and ob.modifiers:
        ev = ob.evaluated_get(dg); m2 = bpy.data.meshes.new_from_object(ev)
        old = ob.data; ob.modifiers.clear(); ob.data = m2; bpy.data.meshes.remove(old)
final = list(parts)
for ob, bone in final:
    mw = ob.matrix_world.copy()
    ob.parent = arm; ob.parent_type = 'BONE'; ob.parent_bone = bone
    bpy.context.view_layer.update(); ob.matrix_world = mw
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _ in final if o.type == 'MESH')
print('PARTS', len(final), 'TRIS', tris)

# ---------------- clips: the sculpt's rest pose is already the idle stance ----------------
BASE_ARMS = {}
exec(open(os.path.join(HERE, 'crucivar_anim.py'), encoding='utf-8').read())

if RENDER: bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RENDER, 'crucivar_meshy.blend'))
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for ob, _ in final: ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_apply=True, export_image_format='JPEG', export_image_quality=90)
print('EXPORTED', OUT, os.path.getsize(OUT))
if RENDER:
    mm = bpy.data.materials.get('CrucivarMeshy')
    if mm: mm.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 3.0
    render_set(RENDER)
