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

HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, 'crucivar_common.py'), encoding='utf-8').read())
parts = []

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
arm, ad = make_armature(B)

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

exec(open(os.path.join(HERE, 'crucivar_anim.py'), encoding='utf-8').read())

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(OUT) if RENDER is None else RENDER, 'crucivar.blend'))
# export
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
for ob, _ in final: ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_apply=True)
print('EXPORTED', OUT, os.path.getsize(OUT))
if RENDER: render_set(RENDER)

