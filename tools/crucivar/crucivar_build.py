# blender -b -P crucivar_build.py -- <out.glb> [<render dir>]
# Builds Crucivar, The Furnace Heart (bosses/2-brass-foundry-crucivar/BOSS.md, sheet 02-crucivar-v1.png) as a rigid
# hard-surface model: every plate is its own mesh, parented to one bone, so the machine moves like a machine (no skin
# stretching). Keys the body clips the encounter plays and exports a GLB for Unity (glTFast, Mecanim clips).
# Axes: -Y forward, +X = Crucivar's LEFT (crucible), -X = its RIGHT (hammer), Z up, metres; 3.8 m to the chimney top.
# Rotations are degrees in rest-armature axes (same convention as the Bellkeeper rig):
#   X: + tips the top of a bone forward, so + swings a hanging arm back; Y: + tilts the top toward +X;
#   Z: + turns toward its left.
# Nodes the encounter drives from code (CrucivarRig.cs) are never keyed here: Shutter_0..2, FlankVent_L/R, BackVent,
# Insulation_*, VentFin_*, FlywheelGuard, Flywheel, Gauge, CrucibleLip. Their objects keep an identity rotation and a
# pivot at the hinge, so a local rotation in Unity swings them about the right edge.
# Design fixes against the v1 sheet: vents on all four sides (belly shutters front, flank grilles left/right, exhaust
# under the flywheel at the back) so Pressure Release's front/back OR left/right pairs read on the model; the phase 3
# vent crown is the shoulder fins only; the back access plate is dark iron, not a glowing weak spot.
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix, Euler, Quaternion

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), 'Crucivar.glb')
RENDER = args[1] if len(args) > 1 else None
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = 30

# ---------------- materials ----------------
def mat(name, color, metal=0.0, rough=0.5, emit=None, strength=1.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1); b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = strength
    return m

IRON = mat('Iron', (0.045, 0.040, 0.038), 0.75, 0.55)
IRON_DARK = mat('IronDark', (0.022, 0.020, 0.020), 0.6, 0.7)
BRASS = mat('Brass', (0.55, 0.36, 0.12), 1.0, 0.32)
BRASS_DARK = mat('BrassAged', (0.30, 0.19, 0.07), 1.0, 0.45)
OXIDE = mat('Oxide', (0.14, 0.27, 0.22), 0.35, 0.55)
CERAMIC = mat('Ceramic', (0.42, 0.36, 0.29), 0.0, 0.8)
SCORCH = mat('Scorched', (0.12, 0.09, 0.07), 0.0, 0.9)
MOLTEN = mat('Molten', (1.0, 0.35, 0.05), 0.0, 0.4, (1.0, 0.30, 0.03), 1.0)
EMBER = mat('Ember', (0.6, 0.12, 0.02), 0.0, 0.5, (0.9, 0.22, 0.03), 1.0)
AMBER = mat('AmberEye', (1.0, 0.7, 0.25), 0.0, 0.3, (1.0, 0.62, 0.18), 1.0)

# ---------------- armature ----------------
B = {  # name: (head, tail, parent)
    'root': ((0, 0, 0), (0, 0.6, 0), None),
    'pelvis': ((0, 0, 1.05), (0, 0, 1.4), 'root'),
    'torso': ((0, 0, 1.4), (0, 0, 3.0), 'pelvis'),
    'thigh.R': ((-0.78, 0, 1.08), (-0.80, -0.06, 0.62), 'pelvis'),
    'shin.R': ((-0.80, -0.06, 0.62), (-0.80, 0.0, 0.26), 'thigh.R'),
    'foot.R': ((-0.80, 0.0, 0.26), (-0.80, -0.5, 0.1), 'shin.R'),
    'thigh.L': ((0.78, 0, 1.08), (0.80, -0.06, 0.62), 'pelvis'),
    'shin.L': ((0.80, -0.06, 0.62), (0.80, 0.0, 0.26), 'thigh.L'),
    'foot.L': ((0.80, 0.0, 0.26), (0.80, -0.5, 0.1), 'shin.L'),
    'upperarm.R': ((-1.62, 0.0, 2.85), (-1.80, -0.05, 2.05), 'torso'),
    'forearm.R': ((-1.80, -0.05, 2.05), (-1.86, -0.25, 1.30), 'upperarm.R'),
    'hammer.R': ((-1.86, -0.25, 1.30), (-1.88, -0.30, 0.55), 'forearm.R'),
    'upperarm.L': ((1.62, 0.0, 2.85), (1.78, -0.05, 2.10), 'torso'),
    'forearm.L': ((1.78, -0.05, 2.10), (1.84, -0.30, 1.45), 'upperarm.L'),
    'crucible.L': ((1.84, -0.30, 1.45), (1.86, -0.40, 1.05), 'forearm.L'),
}
ad = bpy.data.armatures.new('CrucivarRig'); arm = bpy.data.objects.new('CrucivarRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- geometry helpers ----------------
parts = []
def finish(bm, name, material, bone, pivot=(0, 0, 0), bevel=0.0, smooth=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = smooth
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob)
    ob.data.materials.append(material)
    pv = Vector(pivot)
    me.transform(Matrix.Translation(-pv)); ob.location = pv
    if bevel > 0:
        bv = ob.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 2; bv.limit_method = 'ANGLE'
        bv.angle_limit = math.radians(40); bv.harden_normals = False
    parts.append((ob, bone)); return ob

def xf(rot=(0, 0, 0), loc=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))

def box(name, size, loc, material, bone, rot=(0, 0, 0), pivot=None, bevel=0.03):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, size), verts=bm.verts)
    return finish(bm, name, material, bone, pivot or loc, bevel)

def cyl(name, r1, r2, depth, loc, material, bone, rot=(0, 0, 0), seg=24, pivot=None, bevel=0.02, smooth=True, scale=(1, 1, 1)):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, pivot or loc, bevel, smooth)

def sphere(name, r, loc, material, bone, scale=(1, 1, 1), seg=20, rings=12, rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, loc, 0, True)

def ring(name, r, thick, loc, material, bone, rot=(0, 0, 0), seg=40, scale=(1, 1, 1), height=None):
    # a flat band (short tube) around an axis: brass hoops on the barrel and the limbs
    h = height if height is not None else thick
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=seg, radius1=r + thick / 2, radius2=r + thick / 2, depth=h)
    bmesh.ops.create_cone(bm, cap_ends=False, segments=seg, radius1=r - thick / 2, radius2=r - thick / 2, depth=h)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, loc, 0, True)

def rivets(name, centre, r, n, material, bone, rot=(0, 0, 0), size=0.035, scale=(1, 1, 1), arc=(0, 360)):
    bm = bmesh.new()
    M = xf(rot, centre, (1, 1, 1))
    for i in range(n):
        a = math.radians(arc[0] + (arc[1] - arc[0]) * (i + 0.5) / n)
        p = M @ Vector((math.cos(a) * r * scale[0], math.sin(a) * r * scale[1], 0))
        g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size)
        bmesh.ops.translate(bm, vec=p, verts=g['verts'])
    return finish(bm, name, material, bone, centre, 0, True)

def merge(name, objs, material=None):
    # join static pieces on one bone into one mesh (fewer nodes, same look)
    if not objs: return None
    for o in objs:
        for m in o.modifiers: pass
    ctx = bpy.context
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    ctx.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); j = ctx.view_layer.objects.active; j.name = name
    return j

# ================= BODY =================
# --- pelvis / skirt ---
cyl('Skirt', 1.18, 1.32, 0.42, (0, 0, 1.12), IRON, 'pelvis', scale=(1, 0.85, 1), seg=32)
ring('SkirtBand', 1.30, 0.06, (0, 0, 1.30), BRASS, 'pelvis', scale=(1, 0.86, 1), height=0.09)
for s in (-1, 1):
    cyl('HipCap' + str(s), 0.36, 0.36, 0.34, (0.78 * s, 0, 1.06), BRASS_DARK, 'pelvis', rot=(0, 90, 0))
    rivets('HipCapRiv' + str(s), (0.78 * s + 0.18 * s, 0, 1.06), 0.27, 10, BRASS, 'pelvis', rot=(0, 90, 0))

# --- barrel torso ---
TZ = 2.1
cyl('Barrel', 1.30, 1.38, 1.85, (0, 0, TZ), IRON, 'torso', scale=(1, 0.86, 1), seg=40)
for z, w in ((1.32, 0.10), (2.12, 0.08), (2.98, 0.12)):
    ring('Hoop%.2f' % z, 1.39 if z < 2.5 else 1.35, 0.07, (0, 0, z), BRASS, 'torso', scale=(1, 0.86, 1), height=w, seg=48)
    rivets('HoopRiv%.2f' % z, (0, 0, z), 1.43 if z < 2.5 else 1.39, 28, BRASS, 'torso', scale=(1, 0.86, 1))
# hammered brass breast plate band
cyl('BreastPlate', 1.40, 1.36, 0.42, (0, 0, 2.62), BRASS_DARK, 'torso', scale=(1, 0.87, 1), seg=40)
# shoulder yoke and collar
cyl('Yoke', 1.22, 0.95, 0.35, (0, 0, 3.15), IRON, 'torso', scale=(1, 0.82, 1), seg=32)
ring('Collar', 0.96, 0.08, (0, 0, 3.33), BRASS, 'torso', scale=(1, 0.82, 1), height=0.08)

# --- front: three belly furnace windows (molten), mullions, hinged shutter frames ---
FY = -1.155   # barrel front surface at the windows
for i, x in enumerate((-0.52, 0.0, 0.52)):
    zc = 1.72
    box('Window%d' % i, (0.36, 0.08, 0.52), (x, FY + 0.03, zc), MOLTEN, 'torso', bevel=0.01)
    box('WindowRecess%d' % i, (0.46, 0.06, 0.62), (x, FY + 0.07, zc), IRON_DARK, 'torso', bevel=0.01)
    # shutter: brass frame with two bars, hinged on its top edge
    hz = zc + 0.33; sy = FY - 0.04
    bm = bmesh.new()
    for (w, h, cx, cz) in ((0.50, 0.07, 0, -0.035), (0.50, 0.07, 0, -0.63), (0.07, 0.66, -0.215, -0.33), (0.07, 0.66, 0.215, -0.33),
                           (0.40, 0.045, 0, -0.24), (0.40, 0.045, 0, -0.43)):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (x + cx, sy, hz + cz), (w, 0.05, h)), verts=g['verts'])
    finish(bm, 'Shutter_%d' % i, BRASS, 'torso', pivot=(x, sy, hz), bevel=0.012)
box('MullionTop', (1.62, 0.09, 0.09), (0, FY - 0.01, 2.09), BRASS_DARK, 'torso')
box('MullionBot', (1.62, 0.09, 0.09), (0, FY - 0.01, 1.36), BRASS_DARK, 'torso')

# --- flank vents (left/right Pressure Release) ---
for s, tag in ((1, 'L'), (-1, 'R')):
    x = 1.335 * s
    box('FlankGlow' + tag, (0.06, 0.62, 0.50), (x - 0.03 * s, 0.05, 1.78), EMBER, 'torso', bevel=0.01)
    box('FlankFrame' + tag, (0.08, 0.76, 0.64), (x - 0.06 * s, 0.05, 1.78), IRON_DARK, 'torso')
    bm = bmesh.new()
    for k in range(5):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (x + 0.04 * s, 0.05, 2.06 - 0.12 * k - 0.04), (0.035, 0.72, 0.07)), verts=g['verts'])
    for yy in (-0.33, 0.43):
        g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (x + 0.04 * s, yy, 1.80), (0.04, 0.06, 0.60)), verts=g['verts'])
    finish(bm, 'FlankVent_' + tag, BRASS, 'torso', pivot=(x + 0.04 * s, 0.05, 2.10), bevel=0.01)

# --- head: furnace-door face, heavy brow, amber eye ports, toothed grille, hood ---
HZ = 3.17   # centre of the furnace door; the arched hood tops out at 3.8 m with the chimney
HY = -0.55
box("HoodBox", (1.18, 0.82, 0.70), (0, HY, HZ), IRON, "torso", bevel=0.07)
cyl("HoodArch", 0.59, 0.59, 0.82, (0, HY, HZ + 0.33), IRON, "torso", rot=(90, 0, 0), seg=32, scale=(1, 0.62, 1))
for s_ in (-1, 1):
    box("HoodRib" + str(s_), (0.08, 0.86, 0.72), (0.56 * s_, HY, HZ), BRASS_DARK, "torso", bevel=0.02)
box("FaceRim", (1.04, 0.06, 0.70), (0, -0.95, HZ), BRASS, "torso", bevel=0.02)
cyl("FaceRimArch", 0.52, 0.52, 0.06, (0, -0.95, HZ + 0.33), BRASS, "torso", rot=(90, 0, 0), seg=32, scale=(1, 0.62, 1))
box("FaceDoor", (0.90, 0.08, 0.66), (0, -0.99, HZ), IRON_DARK, "torso", bevel=0.03)
cyl("FaceDoorArch", 0.45, 0.45, 0.08, (0, -0.99, HZ + 0.33), IRON_DARK, "torso", rot=(90, 0, 0), seg=32, scale=(1, 0.62, 1))
box("Brow", (1.12, 0.26, 0.15), (0, -1.06, HZ + 0.27), IRON, "torso", rot=(-16, 0, 0), bevel=0.05)
box("BrowPlate", (1.0, 0.05, 0.08), (0, -1.19, HZ + 0.26), BRASS, "torso", rot=(-16, 0, 0), bevel=0.015)
for s_ in (-1, 1):
    cyl("EyeSocket" + str(s_), 0.125, 0.125, 0.07, (0.22 * s_, -1.03, HZ + 0.12), IRON, "torso", rot=(90, 0, 0), seg=20)
    cyl("Eye" + str(s_), 0.095, 0.095, 0.05, (0.22 * s_, -1.06, HZ + 0.12), AMBER, "torso", rot=(90, 0, 0), seg=20)
    ring("EyeRim" + str(s_), 0.115, 0.03, (0.22 * s_, -1.05, HZ + 0.12), BRASS, "torso", rot=(90, 0, 0), height=0.05, seg=24)
box("GrilleBack", (0.56, 0.04, 0.24), (0, -1.02, HZ - 0.15), EMBER, "torso", bevel=0.01)
bm = bmesh.new()
for k in range(6):
    xk = -0.225 + 0.09 * k
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (xk, -1.05, HZ - 0.12), (0.045, 0.05, 0.22)), verts=g["verts"])
    g = bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=0.032, radius2=0.0, depth=0.08)
    bmesh.ops.transform(bm, matrix=xf((180, 0, 45), (xk, -1.05, HZ - 0.27)), verts=g["verts"])
finish(bm, "GrilleTeeth", BRASS, "torso", pivot=(0, -1.05, HZ - 0.15), bevel=0)
box("GrilleBar", (0.62, 0.06, 0.05), (0, -1.05, HZ - 0.01), BRASS_DARK, "torso", bevel=0.01)
for z in (HZ + 0.18, HZ - 0.2):
    cyl("DoorHinge%.2f" % z, 0.045, 0.045, 0.16, (-0.47, -1.02, z), BRASS, "torso", seg=12)
box("Latch", (0.08, 0.07, 0.16), (0.45, -1.04, HZ), BRASS, "torso")

# --- chimney (behind the head, on its left) ---
CX, CY = 0.52, 0.42
cyl('Chimney', 0.22, 0.19, 0.95, (CX, CY, 3.35), IRON, 'torso', seg=20)
for z in (3.05, 3.42, 3.74):
    ring('ChimBand%.2f' % z, 0.215, 0.05, (CX, CY, z), BRASS, 'torso', height=0.07, seg=24)
ring('ChimMouth', 0.18, 0.05, (CX, CY, 3.81), EMBER, 'torso', height=0.05, seg=24)
cyl('ChimSoot', 0.15, 0.15, 0.02, (CX, CY, 3.79), IRON_DARK, 'torso', seg=20)
cyl('ChimPipe', 0.09, 0.09, 0.70, (CX + 0.28, CY + 0.05, 3.10), BRASS_DARK, 'torso', seg=12)

# --- pressure gauge on the left breast (Pressure Release cue) ---
cyl('GaugeCase', 0.17, 0.17, 0.08, (0.78, -1.00, 2.62), BRASS, 'torso', rot=(90, 0, 0), seg=24)
cyl('GaugeFace', 0.14, 0.14, 0.02, (0.78, -1.045, 2.62), CERAMIC, 'torso', rot=(90, 0, 0), seg=24)
bm = bmesh.new(); g = bmesh.ops.create_cube(bm, size=1.0)
bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0.78, -1.06, 2.62 + 0.055), (0.02, 0.01, 0.11)), verts=g['verts'])
finish(bm, 'Gauge', IRON_DARK, 'torso', pivot=(0.78, -1.06, 2.62))

# --- shoulder pistons (shoulder to hip, both sides) ---
for s in (-1, 1):
    top = Vector((1.18 * s, 0.30, 2.95)); bot = Vector((0.98 * s, 0.38, 1.35))
    mid = (top + bot) / 2; d = top - bot; L = d.length
    ang = math.degrees(math.atan2(d.x, d.z))
    cyl('PistonSleeve' + str(s), 0.10, 0.10, L * 0.55, mid + d * 0.2, OXIDE, 'torso', rot=(0, ang, 0), seg=16)
    cyl('PistonRod' + str(s), 0.05, 0.05, L * 0.5, mid - d * 0.22, BRASS, 'torso', rot=(0, ang, 0), seg=12)

# --- insulation (sheds at Casting Heat): scorched ceramic tiles on the chest corners, shoulders and back ---
def shell(name, r, a0, a1, z0, z1, thick, material, bone, sy=0.86, seg=8):
    # a curved plate on the barrel: angles in degrees from the front (-Y) toward +X
    bm = bmesh.new(); rows = []
    for rr in (r, r + thick):
        row = []
        for i in range(seg + 1):
            a = math.radians(a0 + (a1 - a0) * i / seg)
            for z in (z0, z1):
                row.append(bm.verts.new((math.sin(a) * rr, -math.cos(a) * rr * sy, z)))
        rows.append(row)
    def v(layer, i, top): return rows[layer][2 * i + (1 if top else 0)]
    for i in range(seg):
        bm.faces.new((v(1, i, 0), v(1, i + 1, 0), v(1, i + 1, 1), v(1, i, 1)))     # outer
        bm.faces.new((v(0, i, 1), v(0, i + 1, 1), v(0, i + 1, 0), v(0, i, 0)))     # inner
        bm.faces.new((v(0, i, 1), v(1, i, 1), v(1, i + 1, 1), v(0, i + 1, 1)))     # top
        bm.faces.new((v(0, i + 1, 0), v(1, i + 1, 0), v(1, i, 0), v(0, i, 0)))     # bottom
    bm.faces.new((v(0, 0, 0), v(1, 0, 0), v(1, 0, 1), v(0, 0, 1)))
    bm.faces.new((v(0, seg, 1), v(1, seg, 1), v(1, seg, 0), v(0, seg, 0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    am = math.radians((a0 + a1) / 2); zm = (z0 + z1) / 2
    for dz in (z1 - 0.07, z0 + 0.07):   # four rivets
        for da in (0.3, 0.7):
            aa = math.radians(a0 + (a1 - a0) * da); rr = r + thick + 0.01
            g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.028)
            bmesh.ops.translate(bm, vec=(math.sin(aa) * rr, -math.cos(aa) * rr * sy, dz), verts=g["verts"])
    return finish(bm, name, material, bone, pivot=(math.sin(am) * r, -math.cos(am) * r * sy, zm), bevel=0.012)

# insulation (sheds at Casting Heat): curved scorched-ceramic plates on the lower barrel and the breast band
ins = [(-62, -38, 1.42, 2.04), (38, 62, 1.42, 2.04), (-128, -100, 1.42, 2.04), (100, 128, 1.42, 2.04),
       (-62, -36, 2.44, 2.80), (36, 62, 2.44, 2.80), (-122, -98, 2.44, 2.80), (98, 122, 2.44, 2.80)]
for i, (a0, a1, z0, z1) in enumerate(ins):
    shell("Insulation_%d" % i, 1.41 if z0 < 2.2 else 1.43, a0, a1, z0, z1, 0.07, CERAMIC, "torso")

# --- back: tanks, insulated pipes, ladder, toothed flywheel + guard, exhaust vent, dark access plate ---
BY = 1.02
for s in (-1, 1):
    cyl('Tank' + str(s), 0.26, 0.26, 1.15, (0.82 * s, BY + 0.05, 2.35), OXIDE, 'torso', seg=20)
    sphere('TankCapT' + str(s), 0.26, (0.82 * s, BY + 0.05, 2.93), BRASS_DARK, 'torso', scale=(1, 1, 0.45))
    sphere('TankCapB' + str(s), 0.26, (0.82 * s, BY + 0.05, 1.77), BRASS_DARK, 'torso', scale=(1, 1, 0.45))
    for z in (2.05, 2.65): ring('TankBand%d%.2f' % (s, z), 0.27, 0.04, (0.82 * s, BY + 0.05, z), BRASS, 'torso', height=0.06, seg=20)
# insulated pipes: wrapped segments
for s in (-1, 1):
    for k in range(6):
        cyl('PipeWrap%d_%d' % (s, k), 0.085, 0.085, 0.13, (0.45 * s, BY + 0.18, 1.55 + 0.17 * k), CERAMIC if k % 2 else SCORCH, 'torso', seg=12, bevel=0.01)
for k in range(6):
    box('Rung%d' % k, (0.46, 0.05, 0.05), (0, BY + 0.38, 1.6 + 0.24 * k), BRASS_DARK, 'torso', bevel=0.01)
for s in (-1, 1):
    box('Rail' + str(s), (0.05, 0.05, 1.45), (0.24 * s, BY + 0.38, 2.2), BRASS_DARK, 'torso', bevel=0.01)
box('AccessPlate', (0.70, 0.06, 0.55), (0, BY + 0.12, 1.55), IRON_DARK, 'torso', bevel=0.02)
rivets('AccessRiv', (0, BY + 0.16, 1.55), 0.34, 12, BRASS_DARK, 'torso', rot=(90, 0, 0), size=0.025, scale=(1, 0.75, 1))
# flywheel: toothed rim, six spokes, hub; turns about Y (its axle points backward)
FW = Vector((0, BY + 0.62, 2.45)); FR = 0.62
bm = bmesh.new()
for k in range(24):   # segmented rim
    a = 2 * math.pi * (k + 0.5) / 24
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, math.degrees(a) + 90), (math.cos(a) * (FR - 0.05), math.sin(a) * (FR - 0.05), 0), (2 * math.pi * FR / 24 * 1.08, 0.11, 0.12)), verts=g['verts'])
for k in range(24):
    a = 2 * math.pi * k / 24
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, math.degrees(a)), (math.cos(a) * (FR + 0.04), math.sin(a) * (FR + 0.04), 0), (0.08, 0.07, 0.09)), verts=g['verts'])
for k in range(6):
    a = 2 * math.pi * k / 6
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, math.degrees(a)), (math.cos(a) * FR * 0.48, math.sin(a) * FR * 0.48, 0), (FR * 0.9, 0.07, 0.06)), verts=g['verts'])
g = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=0.13, radius2=0.11, depth=0.16)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bmesh.ops.transform(bm, matrix=xf((90, 0, 0), FW, (1, 1, 1)), verts=bm.verts)
finish(bm, 'Flywheel', BRASS, 'torso', pivot=FW, bevel=0.01, smooth=False)
cyl('FlyAxle', 0.08, 0.08, 0.5, FW - Vector((0, 0.28, 0)), IRON, 'torso', rot=(90, 0, 0), seg=12)
cyl('FlyBoss', 0.2, 0.24, 0.18, FW - Vector((0, 0.5, 0)), IRON, 'torso', rot=(90, 0, 0), seg=20)
# guard: a half-moon iron cover over the top of the flywheel, hinged on Crucivar's right edge (-X)
bm = bmesh.new()
segs = 16
verts = []
for k in range(segs + 1):
    a = math.pi * k / segs
    verts.append((math.cos(a) * (FR + 0.12), math.sin(a) * (FR + 0.12)))
ring_pts = [Vector((x, 0, z)) for x, z in verts] + [Vector((-(FR + 0.12), 0, 0)), Vector((FR + 0.12, 0, 0))]
vs = [bm.verts.new(p) for p in [Vector((x, 0, z)) for x, z in verts]]
c = bm.verts.new(Vector((0, 0, 0)))
for k in range(segs): bm.faces.new((c, vs[k + 1], vs[k]))
bmesh.ops.transform(bm, matrix=Matrix.Translation(FW + Vector((0, 0.14, 0.02))), verts=bm.verts)
guard = finish(bm, 'FlywheelGuard', IRON, 'torso', pivot=FW + Vector((-(FR + 0.12), 0.14, 0)), bevel=0)
sol = guard.modifiers.new('solid', 'SOLIDIFY'); sol.thickness = 0.06
box('BackVentGlow', (0.70, 0.05, 0.28), (0, BY + 0.06, 1.98), EMBER, 'torso', bevel=0.01)
bm = bmesh.new()
for k in range(4):
    g = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, BY + 0.12, 2.10 - 0.075 * k - 0.04), (0.76, 0.035, 0.05)), verts=g['verts'])
finish(bm, 'BackVent', BRASS, 'torso', pivot=(0, BY + 0.12, 2.13), bevel=0.008)

# --- phase 3 vent fins: a fan of brass blades on each shoulder (folded until Open Pressure) ---
for s, tag in ((-1, 'R'), (1, 'L')):
    for k in range(5):
        a = -40 + 20 * k
        root = Vector((1.02 * s, 0.20 + 0.13 * (k - 2), 3.10))
        bm = bmesh.new(); g = bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0.42), (0.07, 0.20, 0.84)), verts=g['verts'])
        g = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.05, radius2=0.035, depth=0.14)
        bmesh.ops.transform(bm, matrix=xf((0, 0, 0), (0, 0, 0.9), (1, 1, 1)), verts=g['verts'])
        rotm = Euler((math.radians(a * 0.6), math.radians(-42 * s), 0), 'XYZ').to_matrix().to_4x4()
        bmesh.ops.transform(bm, matrix=Matrix.Translation(root) @ rotm, verts=bm.verts)
        ob = finish(bm, 'VentFin_%s%d' % (tag, k), BRASS_DARK, 'torso', pivot=root, bevel=0.01)
        # keep the fin's own axis as the object's axis so Unity scales it along the blade
        ob.data.transform(rotm.inverted()); ob.rotation_euler = Euler((math.radians(a * 0.6), math.radians(-42 * s), 0), 'XYZ')

# ================= LEGS =================
for s, tag in ((-1, 'R'), (1, 'L')):
    x = 0.79 * s
    cyl('Thigh' + tag, 0.34, 0.30, 0.42, (x, -0.03, 0.86), IRON, 'thigh.' + tag, seg=20)
    ring('ThighBand' + tag, 0.34, 0.05, (x, -0.03, 0.98), BRASS, 'thigh.' + tag, height=0.08, seg=24)
    cyl('ThighPiston' + tag, 0.07, 0.07, 0.5, (x + 0.30 * s, 0.12, 0.85), BRASS, 'thigh.' + tag, seg=12)
    box('ThighPlate' + tag, (0.50, 0.14, 0.36), (x, -0.36, 0.86), BRASS_DARK, 'thigh.' + tag, rot=(8, 0, 0), bevel=0.04)
    sphere('Knee' + tag, 0.27, (x, -0.10, 0.62), BRASS_DARK, 'shin.' + tag)
    cyl('KneeCap' + tag, 0.18, 0.12, 0.10, (x, -0.33, 0.64), BRASS, 'shin.' + tag, rot=(90, 0, 0), seg=16)
    cyl('Shin' + tag, 0.30, 0.38, 0.40, (x, -0.02, 0.43), IRON, 'shin.' + tag, seg=20)
    ring('ShinBand' + tag, 0.36, 0.05, (x, -0.02, 0.30), BRASS, 'shin.' + tag, height=0.07, seg=24)
    box('Foot' + tag, (0.78, 1.05, 0.26), (x, -0.18, 0.13), IRON, 'foot.' + tag, bevel=0.06)
    box('FootTrim' + tag, (0.82, 1.08, 0.06), (x, -0.18, 0.27), BRASS, 'foot.' + tag, bevel=0.02)
    for k in (-1, 0, 1):
        box('Toe%s%d' % (tag, k), (0.22, 0.30, 0.20), (x + 0.26 * k, -0.78, 0.10), BRASS_DARK, 'foot.' + tag, bevel=0.04)
    cyl('HeelWheel' + tag, 0.16, 0.16, 0.20, (x, 0.42, 0.16), BRASS, 'foot.' + tag, rot=(0, 90, 0), seg=16)
    rivets('FootRiv' + tag, (x, -0.18, 0.28), 0.36, 10, BRASS, 'foot.' + tag, size=0.03, scale=(1, 1.4, 1))

# ================= RIGHT ARM: the hammer =================
cyl('ShoulderPivotR', 0.22, 0.22, 0.4, (-1.45, 0, 2.85), IRON, 'torso', rot=(0, 90, 0), seg=16)
for i_, (r_, z_, h_) in enumerate(((0.68, 2.80, 0.26), (0.56, 3.02, 0.22), (0.40, 3.19, 0.16))):
    cyl("PauldronR%d" % i_, r_, r_ * 0.92, h_, (-1.64, 0.0, z_), BRASS if i_ != 1 else BRASS_DARK, "upperarm.R", seg=28, scale=(1, 0.9, 1), bevel=0.03)
ring('PauldronRimR', 0.70, 0.06, (-1.64, 0, 2.68), BRASS_DARK, 'upperarm.R', height=0.08, seg=28)
rivets('PauldronRivR', (-1.64, 0, 2.86), 0.70, 18, BRASS, 'upperarm.R')
cyl('UpperArmR', 0.36, 0.32, 0.62, (-1.74, -0.03, 2.35), IRON, 'upperarm.R', seg=20)
ring('UpperArmBandR', 0.36, 0.05, (-1.74, -0.03, 2.25), OXIDE, 'upperarm.R', height=0.08, seg=24)
sphere('ElbowR', 0.33, (-1.80, -0.05, 2.03), BRASS_DARK, 'forearm.R')
cyl('ForearmR', 0.42, 0.50, 0.66, (-1.84, -0.16, 1.66), IRON, 'forearm.R', rot=(-14, 0, 0), seg=24)
for z in (1.86, 1.48):
    ring('ForearmBandR%.2f' % z, 0.49, 0.05, (-1.84, -0.16 - (1.66 - z) * 0.25, z), BRASS, 'forearm.R', rot=(-14, 0, 0), height=0.08, seg=28)
cyl('ForearmPistonR', 0.06, 0.06, 0.55, (-2.18, -0.10, 1.66), BRASS, 'forearm.R', rot=(-14, 0, 0), seg=12)
# hammer head: a heavy die block with brass corner plates and the foundry cross emblem on its faces
HC = Vector((-1.90, -0.34, 0.78))
box('HammerHead', (1.02, 1.02, 0.92), HC, IRON, 'hammer.R', bevel=0.07)
for sx in (-1, 1):
    for sy in (-1, 1):
        for sz in (-1, 1):
            box('HCorner%d%d%d' % (sx, sy, sz), (0.26, 0.26, 0.24), HC + Vector((0.42 * sx, 0.42 * sy, 0.38 * sz)), BRASS_DARK, 'hammer.R', bevel=0.03)
for face, rot in ((Vector((0, -0.52, 0)), (90, 0, 0)), (Vector((0.52, 0, 0)), (0, 90, 0)), (Vector((-0.52, 0, 0)), (0, 90, 0))):
    ring('Emblem%s' % str(rot), 0.20, 0.035, HC + face, BRASS, 'hammer.R', rot=rot, height=0.04, seg=24)
    box('EmblemV%s' % str(rot), (0.04, 0.04, 0.5) if rot[0] == 90 else (0.04, 0.04, 0.5), HC + face, BRASS, 'hammer.R', rot=(0, 0, 0), bevel=0.005)
    box('EmblemH%s' % str(rot), (0.5, 0.04, 0.04) if rot[0] == 90 else (0.04, 0.5, 0.04), HC + face, BRASS, 'hammer.R', bevel=0.005)
box('HammerFace', (0.96, 0.96, 0.08), HC - Vector((0, 0, 0.48)), BRASS_DARK, 'hammer.R', bevel=0.02)
box('HammerBand', (1.06, 1.06, 0.10), HC + Vector((0, 0, 0.20)), BRASS, 'hammer.R', bevel=0.02)
rivets('HammerRiv', HC + Vector((0, 0, 0.27)), 0.47, 16, BRASS, 'hammer.R', size=0.03)
cyl('WristR', 0.30, 0.34, 0.2, (-1.88, -0.30, 1.28), BRASS_DARK, 'hammer.R', seg=20)

# ================= LEFT ARM: the pouring crucible =================
cyl('ShoulderPivotL', 0.22, 0.22, 0.4, (1.45, 0, 2.85), IRON, 'torso', rot=(0, 90, 0), seg=16)
for i_, (r_, z_, h_) in enumerate(((0.58, 2.82, 0.24), (0.47, 3.01, 0.2), (0.33, 3.16, 0.14))):
    cyl("PauldronL%d" % i_, r_, r_ * 0.92, h_, (1.62, 0.0, z_), BRASS if i_ != 1 else BRASS_DARK, "upperarm.L", seg=28, scale=(1, 0.9, 1), bevel=0.03)
ring('PauldronRimL', 0.60, 0.06, (1.62, 0, 2.70), BRASS_DARK, 'upperarm.L', height=0.08, seg=28)
rivets('PauldronRivL', (1.62, 0, 2.86), 0.60, 16, BRASS, 'upperarm.L')
cyl('UpperArmL', 0.30, 0.26, 0.58, (1.71, -0.03, 2.40), IRON, 'upperarm.L', seg=20)
ring('UpperArmBandL', 0.30, 0.05, (1.71, -0.03, 2.30), OXIDE, 'upperarm.L', height=0.08, seg=24)
sphere('ElbowL', 0.23, (1.78, -0.05, 2.08), BRASS_DARK, 'forearm.L')
cyl('ForearmL', 0.29, 0.34, 0.55, (1.81, -0.18, 1.76), IRON, 'forearm.L', rot=(-20, 0, 0), seg=20)
ring('ForearmBandL', 0.34, 0.05, (1.82, -0.22, 1.62), BRASS, 'forearm.L', rot=(-20, 0, 0), height=0.08, seg=24)
CC = Vector((1.86, -0.40, 1.18))
cyl('CrucibleBowl', 0.42, 0.50, 0.50, CC, BRASS_DARK, 'crucible.L', seg=28)
ring('CrucibleLipRing', 0.50, 0.07, CC + Vector((0, 0, 0.25)), BRASS, 'crucible.L', height=0.09, seg=32)
cyl('CrucibleMetal', 0.43, 0.43, 0.04, CC + Vector((0, 0, 0.22)), MOLTEN, 'crucible.L', seg=28)
cyl('CrucibleBase', 0.30, 0.26, 0.16, CC - Vector((0, 0, 0.32)), IRON, 'crucible.L', seg=20)
box('Spout', (0.20, 0.32, 0.10), CC + Vector((0, -0.55, 0.22)), BRASS, 'crucible.L', rot=(-12, 0, 0), bevel=0.02)
rivets('CrucibleRiv', CC + Vector((0, 0, 0.05)), 0.51, 14, BRASS, 'crucible.L', size=0.03)
for s in (-1, 1):
    cyl('CradleArm' + str(s), 0.05, 0.05, 0.55, CC + Vector((0.42 * s, 0.05, 0.25)), IRON, 'crucible.L', seg=10)
lip = bpy.data.objects.new('CrucibleLip', None); sc.collection.objects.link(lip); lip.location = CC + Vector((0, -0.72, 0.24))
parts.append((lip, 'crucible.L'))

# ---------------- bake modifiers, parent everything rigidly to its bone ----------------
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
for ob, bone in parts:
    if ob.type == 'MESH' and ob.modifiers:
        ev = ob.evaluated_get(dg); me = bpy.data.meshes.new_from_object(ev)
        old = ob.data; ob.modifiers.clear(); ob.data = me; bpy.data.meshes.remove(old)
# join static pieces per bone (code-driven nodes stay separate)
KEEP = ('Shutter_', 'FlankVent_', 'BackVent', 'Insulation_', 'VentFin_', 'FlywheelGuard', 'Flywheel', 'Gauge', 'CrucibleLip')
by_bone = {}
for ob, bone in parts:
    if ob.type != 'MESH' or ob.name.startswith(KEEP): continue
    by_bone.setdefault(bone, []).append(ob)
final = [(ob, bone) for ob, bone in parts if ob.type != 'MESH' or ob.name.startswith(KEEP)]
for bone, obs in by_bone.items():
    j = merge('Body_' + bone.replace('.', '_'), obs)
    final.append((j, bone))
for ob, bone in final:
    mw = ob.matrix_world.copy()
    ob.parent = arm; ob.parent_type = 'BONE'; ob.parent_bone = bone
    bpy.context.view_layer.update()
    ob.matrix_world = mw
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o, _ in final if o.type == 'MESH')
print('PARTS', len(final), 'TRIS', tris)

# ---------------- animation ----------------
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
LEG = {}
for s in 'RL':
    LEG[s] = (ad.bones['thigh.' + s].head_local.copy(), ad.bones['shin.' + s].head_local.copy(), ad.bones['foot.' + s].head_local.copy())

def apply(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        if n.startswith('_'): continue
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        rot = v.get('rot', (0, 0, 0)); pb.rotation_quaternion = (M.inverted() @ R(*rot) @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])

def plant(feet):
    # two-bone IK in armature space: each ankle goes to its rest point plus an offset, the knee bends forward
    bpy.context.view_layer.update()
    for s, (h0, k0, a0) in LEG.items():
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        dx, dy, dz, pitch = feet.get(s, (0, 0, 0, 0))
        H = th.head.copy(); A = a0 + Vector((dx, dy, dz))
        L1, L2 = (k0 - h0).length, (a0 - k0).length
        d = min((A - H).length, L1 + L2 - 1e-4); u = (A - H).normalized()
        pole = Vector((0, -1, 0.15)); v = (pole - pole.project(u)).normalized()
        ca = max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)))
        K = H + L1 * (ca * u + math.sqrt(max(0, 1 - ca * ca)) * v)
        for pb, a, b in ((th, H, K), (sh, K, A)):
            r0 = pb.bone.matrix_local.to_3x3(); rest = (pb.bone.tail_local - pb.bone.head_local).normalized()
            pb.matrix = Matrix.Translation(a) @ (rest.rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ (R(x=pitch) @ ft.bone.matrix_local.to_3x3()).to_4x4()
    bpy.context.view_layer.update()

def sm(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):
    # piecewise smooth interpolation through (time, value) pairs; values may be tuples
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = sm((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple): return tuple(a + (b - a) * u for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * u
    return pts[-1][1]
def jolt(t, t0, amp, f=9.0, damp=7.0):
    return 0 if t < t0 else amp * math.exp(-damp * (t - t0)) * math.sin(f * (t - t0))

# base stance: crucible held at the waist in front, the hammer hanging heavy
def base(t, breathe=True):
    b = 0.5 + 0.5 * math.sin(2 * math.pi * t / 4.0) if breathe else 0
    return {
        'pelvis': {'loc': (0, 0, -0.03 * b)},
        'torso': {'rot': (1.5 * b, 0, 0)},
        'upperarm.R': {'rot': (-6, 6, 0)}, 'forearm.R': {'rot': (-10, 0, 0)}, 'hammer.R': {'rot': (0, 0, 0)},
        'upperarm.L': {'rot': (-12, -6, 0)}, 'forearm.L': {'rot': (-38, 0, 0)}, 'crucible.L': {'rot': (36, 0, 0)},
        '_feet': {},
    }
def mix(p, **over):
    for k, v in over.items():
        k = k.replace('__', '.')
        if k == '_feet': p['_feet'] = v; continue
        cur = p.setdefault(k, {})
        for kk, vv in v.items():
            if kk == 'rot' and 'rot' in cur: cur['rot'] = tuple(a + b for a, b in zip(cur['rot'], vv))
            elif kk == 'loc' and 'loc' in cur: cur['loc'] = tuple(a + b for a, b in zip(cur['loc'], vv))
            else: cur[kk] = vv
    return p
def crouch(depth, wide=0.0):
    return {'R': (-wide, 0, depth, 0), 'L': (wide, 0, depth, 0)}

def idle(t): return base(t)

WALK = 1.6
def walk(t):
    p = base(t, False); ph = 2 * math.pi * t / WALK
    # compression walk: pistons compress first, the body drops onto each planted foot
    bob = -0.07 * abs(math.sin(ph))
    mix(p, pelvis={'loc': (0.04 * math.sin(ph), 0, bob)}, torso={'rot': (4, 3 * math.sin(ph), 2 * math.sin(ph))},
        upperarm__R={'rot': (-10 * math.sin(ph), 0, 0)}, upperarm__L={'rot': (10 * math.sin(ph), 0, 0)})
    def foot(q):
        lift = max(0, math.sin(q)) * 0.16
        return (0, -0.28 * math.cos(q), lift, -10 * max(0, math.sin(q)))
    p['_feet'] = {'R': foot(ph), 'L': foot(ph + math.pi)}
    # the planted foot holds; the pelvis drop is taken by the knees
    for s in 'RL':
        dx, dy, dz, pt = p['_feet'][s]; p['_feet'][s] = (dx, dy, dz - bob, pt)
    return p

def diebreaker(t):
    # three ratchet notches up (0.2 / 0.6 / 1.0 s), a held peak, then the slam lands exactly at 1.8 s
    notch = 0
    for t0, a in ((0.15, -55), (0.55, -105), (0.95, -150)):
        if t >= t0: notch = a if t >= t0 + 0.14 else notch + (a - notch) * sm((t - t0) / 0.14)
    arm = notch if t < 1.6 else key(t, [(1.6, -150), (1.8, -48)])
    lean = key(t, [(0, 0), (1.2, -8), (1.6, -10), (1.8, 20)])
    drop = key(t, [(0, 0), (1.2, 0.04), (1.6, 0.04), (1.8, -0.22)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, drop)}, torso={'rot': (lean + jolt(t, 0.15, 2) + jolt(t, 0.55, 2) + jolt(t, 0.95, 2), 0, key(t, [(0, 0), (1.2, -14), (1.8, 6)]))},
        upperarm__R={'rot': (arm + 6, key(t, [(0, 0), (1.2, -12), (1.8, -6)]), 0)}, forearm__R={'rot': (key(t, [(0, 0), (1.0, -20), (1.8, 10)]), 0, 0)},
        upperarm__L={'rot': (key(t, [(0, 0), (1.6, 10), (1.8, -10)]), key(t, [(0, 0), (1.6, -20), (1.8, -12)]), 0)})
    p['_feet'] = crouch(-max(0, -drop) * 0.0, 0.0)
    p['_feet'] = {'R': (0, -0.1 * sm(t / 1.8), 0, 0), 'L': (0, 0.1 * sm(t / 1.8), 0, 0)}
    return p

def hammer_lodged(t):
    # hammer stuck in the floor after the slam: two tugs, then the extraction and reset (2.4 s)
    tug = jolt(t, 0.3, -6, 8, 3) + jolt(t, 0.95, -8, 8, 3)
    ext = sm((t - 1.5) / 0.75)
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.22 * (1 - ext))}, torso={'rot': (20 * (1 - ext) + tug, 0, 6 * (1 - ext))},
        upperarm__R={'rot': ((-48 + 6) * (1 - ext) + tug * 0.5, -6 * (1 - ext), 0)}, forearm__R={'rot': (10 * (1 - ext), 0, 0)},
        upperarm__L={'rot': (-10 * (1 - ext), -12 * (1 - ext), 0)})
    p['_feet'] = {'R': (0, -0.1 * (1 - ext), 0, 0), 'L': (0, 0.1 * (1 - ext), 0, 0)}
    return p

def slag_cast(t):
    # the crucible rises to shoulder height, tracks, then tips and flings at 2.0 s
    lift = key(t, [(0, 0), (0.6, 1), (2.0, 1)])
    tip = key(t, [(0, 0), (1.3, 0), (1.85, 1), (2.0, 0.85)])
    p = base(t, False)
    mix(p, torso={'rot': (-4 * lift, 0, 10 * lift)},
        upperarm__L={'rot': (-60 * lift, -14 * lift, 0)}, forearm__L={'rot': (10 * lift, 0, 0)}, crucible__L={'rot': (-36 * lift + 95 * tip, 0, 0)},
        upperarm__R={'rot': (8 * lift, 6 * lift, 0)})
    return p

def rail_ram(t):
    # 1.8 s windup: crouch, hammer swung back as a counterweight, heel wheels showing; 0.6 s run; the braking lean
    wind = key(t, [(0, 0), (1.2, 1), (1.8, 1)]); run = key(t, [(1.8, 0), (1.95, 1), (2.4, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.22 * wind + 0.08 * run)}, torso={'rot': (16 * wind + 14 * run, 0, 0)},
        upperarm__R={'rot': (55 * wind - 115 * run, 8 * wind, 0)}, forearm__R={'rot': (-15 * run, 0, 0)},
        upperarm__L={'rot': (-30 * wind + 10 * run, -18 * wind, 0)}, forearm__L={'rot': (-20 * wind, 0, 0)})
    p['_feet'] = {'R': (0, 0.15 * wind - 0.3 * run, 0.08 * run, 18 * wind), 'L': (0, -0.25 * wind + 0.2 * run, 0, 0)}
    return p

def release(sides):
    def f(t):
        # brace, chest swells (lean back), the selected vents blow on the last frames; arms clear the vents
        b = key(t, [(0, 0), (0.5, 1), (1.7, 1)]); hit = jolt(t, 1.62, 5, 14, 6)
        p = base(t, False)
        if sides:
            mix(p, pelvis={'loc': (0, 0, -0.10 * b)}, torso={'rot': (4 * b + hit, 0, 0)},
                upperarm__R={'rot': (-55 * b, 10 * b, 0)}, upperarm__L={'rot': (-55 * b, -10 * b, 0)},
                forearm__R={'rot': (-20 * b, 0, 0)}, forearm__L={'rot': (-10 * b, 0, 0)})
        else:
            mix(p, pelvis={'loc': (0, 0, -0.12 * b)}, torso={'rot': (-7 * b - hit, 0, 0)},
                upperarm__R={'rot': (0, 38 * b, 0)}, upperarm__L={'rot': (0, -38 * b, 0)}, forearm__R={'rot': (-25 * b, 0, 0)})
        p['_feet'] = {'R': (-0.08 * b, 0, 0, 0), 'L': (0.08 * b, 0, 0, 0)}
        return p
    return f

CAST = 3.0
def casting(t):
    # braced pouring stance (the Phase 2 sheet pose): crucible tipped forward, hammer cocked high, a stamp at each strip
    b = key(t, [(0, 0), (0.5, 1), (CAST, 1)])
    stamp = sum(jolt(t, t0, 1, 16, 7) for t0 in (1.8, 2.4, 3.0))
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.24 * b + 0.03 * stamp)}, torso={'rot': (12 * b + 2 * stamp, 0, 8 * b)},
        upperarm__R={'rot': (-120 * b, -10 * b, 0)}, forearm__R={'rot': (-30 * b, 0, 0)},
        upperarm__L={'rot': (-40 * b, -16 * b, 0)}, crucible__L={'rot': (55 * b, 0, 0)})
    p['_feet'] = {'R': (-0.18 * b, -0.10 * b, 0, 0), 'L': (0.18 * b, 0.25 * b, 0, 0)}
    return p

def vulnerable(t):
    # recovery / braking: slumped, hammer braced on the floor, steam heave (loops)
    h = math.sin(2 * math.pi * t / 2.0)
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.16 + 0.02 * h)}, torso={'rot': (14 + 2 * h, 0, -4)},
        upperarm__R={'rot': (-30, -4, 0)}, forearm__R={'rot': (8, 0, 0)}, upperarm__L={'rot': (8, -4, 0)}, forearm__L={'rot': (10, 0, 0)})
    return p

def phase_shift(t):
    # rears back and spreads its arms while the plates shed / the vents open, then settles (1.5 s)
    r = key(t, [(0, 0), (0.5, 1), (1.0, 1), (1.5, 0)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, 0.05 * r)}, torso={'rot': (-14 * r, 0, 0)},
        upperarm__R={'rot': (-30 * r, 45 * r, 0)}, upperarm__L={'rot': (-30 * r, -45 * r, 0)}, forearm__R={'rot': (-30 * r, 0, 0)})
    return p

def defeat(t):
    # kneels, the crucible comes up under the belly for a harmless final pour, then it locks solid (3.0 s, holds)
    k = key(t, [(0, 0), (1.2, 1), (3.0, 1)]); c = key(t, [(1.0, 0), (2.0, 1), (3.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0.12 * k, -0.42 * k)}, torso={'rot': (16 * k, 0, 0)},
        upperarm__R={'rot': (-25 * k, -6 * k, 0)}, forearm__R={'rot': (12 * k, 0, 0)},
        upperarm__L={'rot': (-40 * c, 20 * c, 0)}, forearm__L={'rot': (-40 * c, 0, 0)}, crucible__L={'rot': (-30 * c, 0, 0)})
    p['_feet'] = {'R': (0, 0.05 * k, 0, 0), 'L': (0, 0.05 * k, 0, 0)}
    return p

def bake(name, seconds, fn, loop=False):
    frames = max(2, round(seconds * 30))
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        t = f / 30 if not loop else (f / frames) * seconds
        p = fn(t); apply(p); plant(p.get('_feet', {}))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

acts = [bake('Idle', 4.0, idle, True), bake('Walk', WALK, walk, True),
        bake('Diebreaker', 1.8, diebreaker), bake('HammerLodged', 2.4, hammer_lodged),
        bake('SlagCast', 2.0, slag_cast), bake('RailRam', 2.4, rail_ram),
        bake('ReleaseFrontBack', 1.7, release(False)), bake('ReleaseSides', 1.7, release(True)),
        bake('CastingCycle', CAST, casting), bake('Vulnerable', 2.0, vulnerable, True),
        bake('PhaseShift', 1.5, phase_shift), bake('Defeat', 3.0, defeat)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
apply(base(0)); plant({})

# ---------------- optional preview renders (turnaround + attack poses) ----------------
def render_set(outdir):
    os.makedirs(outdir, exist_ok=True)
    # previews only: brighter emission (glTFast drops emissive strength; the game boosts glow itself) and folded fins
    for m in (MOLTEN, EMBER, AMBER): m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 2.2
    fins = [o for o, _ in final if o.name.startswith("VentFin_")]
    def fold(k):
        for o in fins: o.scale = (1, 1, k)
    try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError: sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 900, 1000
    sc.render.film_transparent = False
    world = bpy.data.worlds.new('W'); sc.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.93, 0.89, 0.80, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.45
    sc.view_settings.view_transform = 'AgX'
    key_l = bpy.data.objects.new('Key', bpy.data.lights.new('Key', 'SUN')); sc.collection.objects.link(key_l)
    key_l.data.energy = 3.2; key_l.rotation_euler = Euler((math.radians(50), math.radians(-25), math.radians(-35)))
    rim = bpy.data.objects.new('Rim', bpy.data.lights.new('Rim', 'SUN')); sc.collection.objects.link(rim)
    rim.data.energy = 1.6; rim.data.color = (1.0, 0.75, 0.5); rim.rotation_euler = Euler((math.radians(60), 0, math.radians(160)))
    floor = bpy.data.objects.new('Floor', bpy.data.meshes.new('Floor')); sc.collection.objects.link(floor)
    bm = bmesh.new(); bmesh.ops.create_circle(bm, cap_ends=True, segments=48, radius=4.0); bm.to_mesh(floor.data); bm.free()
    floor.data.materials.append(mat('FloorM', (0.10, 0.07, 0.05), 0.6, 0.5))
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    def shot(name, yaw_deg, action=None, t=0.0, elev=12, dist=11.5):
        if action:
            arm.animation_data.action = bpy.data.actions[action]
            sc.frame_set(int(round(t * 30)) + 1)
        else:
            arm.animation_data.action = acts[0]; sc.frame_set(1)
        a = math.radians(yaw_deg); e = math.radians(elev)
        target = Vector((0, 0, 1.9))
        cam.location = target + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(outdir, name + '.png'); bpy.ops.render.render(write_still=True)
        print('RENDER', name)
    fold(0.15)
    shot('01_front', 0); shot('02_side', 90); shot('03_back', 180); shot('04_threequarter', -35, elev=22)
    shot('05_diebreaker_peak', -30, 'Diebreaker', 1.55, 18); shot('06_diebreaker_slam', -30, 'Diebreaker', 1.8, 18)
    shot('07_slag', -40, 'SlagCast', 1.9, 18); shot('08_ram', 70, 'RailRam', 1.6, 15); shot('09_casting', -30, 'CastingCycle', 2.0, 18)
    shot('10_release', -20, 'ReleaseFrontBack', 1.6, 15); shot('11_defeat', -30, 'Defeat', 3.0, 18)
    fold(1.0); shot('12_open_pressure', -25, 'PhaseShift', 0.7, 18); shot('13_open_pressure_back', 150, None, 0, 18)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(OUT) if RENDER is None else RENDER, 'crucivar.blend'))
# export
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for ob, _ in final: ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_apply=True)
print('EXPORTED', OUT, os.path.getsize(OUT))
if RENDER: render_set(RENDER)
