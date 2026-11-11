# blender -b <9 Nov rivet_cantor_rig.blend> -P rivet_cantor_mallet.py -- <out dir> <out RivetCantor.glb> <clay|tex>
# Swaps the Rivet Cantor's tuning fork for the 3 Nov design's rivet-headed mallet (miniboss art pass, 9 Nov 2026; prompt:
# build-evidence/miniboss-art-20261109/rivet-cantor/prompts.md, "Asset 4"). A prop swap, not a re-rig:
#   1. delete the fork's vertices from the body (weighted wholly to hand.L along wrist -> fork tip; the fist stays),
#   2. build the mallet from code and bone-parent it to hand.L along the fork's old axis (same reach, 1.12 m),
#   3. re-export the GLB with the rig's own export settings (every weight, action and timing unchanged),
#   4. render pose sheets (close and gameplay camera) at the clips' key frames, and a mallet turnaround.
# Input: build-evidence/miniboss-models-20261109/rivet/rivet_cantor_rig.blend (the source of the 9 Nov in-game GLB).
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector, Matrix, noise

args = sys.argv[sys.argv.index('--') + 1:]
OUT, GLB, MODE = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2]
TEX = MODE == 'tex'
os.makedirs(OUT, exist_ok=True)
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
sc = bpy.context.scene
body = next(o for o in sc.objects if o.type == 'MESH' and o.parent and o.parent.type == 'ARMATURE')
arm = body.parent
for o in list(sc.objects):   # the rig blend's bone-display shapes etc. stay out of renders
    if o not in (arm, body) and o.type == 'MESH': o.hide_render = True
WR_L, FORK = np.array((1.30, -0.61, 1.23)), np.array((1.62, -1.40, 0.48))
CUT_U = 0.16   # the fist ends here (fork_probe: the prongs start at ~0.17 of wrist -> tip)

# ------------------------------------------------------------------ 1. the fork out
arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
co = np.array([body.matrix_world @ v.co for v in body.data.vertices])
d = FORK - WR_L; u = (co - WR_L) @ d / (d @ d)
dist = np.linalg.norm(co - (WR_L + np.clip(u, 0, 1)[:, None] * d), axis=1)
kill = np.nonzero((u >= CUT_U) & (u < 1.15) & (dist < 0.3))[0]
bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[int(i)] for i in kill], context='VERTS'); bm.to_mesh(body.data); bm.free()
print('FORK_REMOVED', len(kill), 'verts')

# ------------------------------------------------------------------ 2. the mallet (local: shaft along +Z from the wrist)
def new_obj(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob); return ob
def cyl(name, r, h, seg, at=(0, 0, 0), axis='Z', r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
    ob = new_obj(name, bm)
    if axis == 'X': ob.rotation_euler = (0, math.pi / 2, 0)
    ob.location = at; return ob
def tag(ob, idx):
    a = ob.data.attributes.get('mid') or ob.data.attributes.new('mid', 'INT', 'FACE')
    for i in range(len(ob.data.polygons)): a.data[i].value = idx
def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); ob = bpy.context.view_layer.objects.active; ob.name = name; ob.data.name = name; return ob

random.seed(11)
# model round 2 (critic R1): shaft 0.11 m; a leather grip with wrap ridges below the fist; a heavy collar with
# strap cheeks; one raised cobalt band; end flanges; taller rivet domes inset in them; the head rolled 18 deg off the foot line
# final model (critic R2): shaft +0.06 m so ForkPlant meets the floor (+0.10 would sink Idle's head), roll 40 deg so
# HammerLoop's head clears the shin, flat bevelled striking lands, a 0.025 m band, a Ø0.20 collar, longer cheeks
SHAFT, HEAD_R, HEAD_L = 1.01, 0.17, 0.50
parts = []
p = cyl('shaft', 0.055, SHAFT, 12, at=(0, 0, SHAFT / 2)); tag(p, 1); parts.append(p)
# the handle runs along the forearm's axis (as the fork did): the fist covers 0-0.17 m, so the leather grip shows just
# below it and the butt end stays hidden inside the fist (a pommel behind the wrist would sit inside the forearm)
p = cyl('grip', 0.06, 0.24, 14, at=(0, 0, 0.21)); tag(p, 3); parts.append(p)
for z in (0.13, 0.19, 0.25, 0.31):
    p = cyl(f'wrap{z}', 0.07, 0.022, 14, at=(0, 0, z)); tag(p, 3); parts.append(p)
p = cyl('butt', 0.062, 0.05, 14, at=(0, 0, 0.0)); tag(p, 0); parts.append(p)
for z in (0.36, 0.56, 0.74):
    p = cyl(f'ferrule{z}', 0.068, 0.04, 14, at=(0, 0, z)); tag(p, 5); parts.append(p)
p = cyl('collar', 0.10, 0.08, 16, at=(0, 0, SHAFT - HEAD_R - 0.03), r2=0.085); tag(p, 0); parts.append(p)
for sx in (-1, 1):   # strap cheeks: iron straps riveted down the barrel either side of the shaft
    st = cyl(f'strap{sx}', 0.03, 0.20, 6, at=(sx * 0.065, 0, SHAFT - HEAD_R + 0.01)); tag(st, 1); st.scale = (1.0, 0.6, 1.0); parts.append(st)
p = cyl('barrel', HEAD_R, HEAD_L - 0.07, 24, at=(0, 0, SHAFT), axis='X'); tag(p, 0); parts.append(p)
p = cyl('band', HEAD_R + 0.025, 0.10, 24, at=(0, 0, SHAFT), axis='X'); tag(p, 2); parts.append(p)
for sx in (-1, 1):
    # the striking faces: a Ø0.38 flange and a Ø0.26 peened rivet dome, 0.10 m proud, inset in it
    p = cyl(f'flange{sx}', 0.19, 0.035, 24, at=(sx * (HEAD_L / 2 - 0.0525), 0, SHAFT), axis='X'); tag(p, 0); parts.append(p)
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=22, v_segments=10, radius=1.0)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context='VERTS')
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary])
    for v in bm.verts:
        if v.co.z > 0.05: v.co -= v.co.normalized() * (0.025 + 0.025 * noise.noise(v.co * 4.0 + Vector((sx, 0, 0))))
        v.co = Vector((v.co.x * 0.11, v.co.y * 0.11, v.co.z * 0.07))
    dome = new_obj(f'dome{sx}', bm); tag(dome, 4)
    dome.rotation_euler = (0, sx * math.pi / 2, 0); dome.location = (sx * (HEAD_L / 2 - 0.035), 0, SHAFT); parts.append(dome)
    # the flat land round the dome: a bevelled ring face on the flange (0.04 m wide)
    p = cyl(f'land{sx}', 0.17, 0.012, 24, at=(sx * (HEAD_L / 2 - 0.029), 0, SHAFT), axis='X', r2=0.15); tag(p, 0)
    p.rotation_euler = (0, sx * math.pi / 2, 0); parts.append(p)
for o in parts:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mal = join(parts, 'Mallet')
if TEX:
    import rivet_cantor_props_tex as T
    TM = T.materials()
    # texture round 2 (critic R1): the mallet's own materials. A darker, rougher brass barrel (#B8893F, rough 0.38-0.45)
    # with bright crowns (#EBC883, rough 0.25-0.3) and dark seams; a matte blackened shaft (metal 0.7, rough 0.55-0.6, a
    # light pit noise) so shaft + ferrules stop reading as a bead chain; dull ferrules (#A87E3A); matte leather.
    # final refine (critic R2): edge wear only on true edges (pointiness 0.62-0.72; small cylinders read as all-edge at
    # the default), barrel #C79A4C rough 0.32-0.38, a bright crown centre (#E8C27A, rough 0.2, ~0.10 m across) on each
    # striking face, near-black iron (metal 0.85), dull ferrules (#A8803E, 0.45), darker leather (#2E2119, 0.65)
    E = (0.62, 0.72)
    brass = T._metal('mallet_brass', T.srgb('C79A4C'), T.srgb('E8C27A'), T.srgb('5A3E1C'), rough=(0.32, 0.38), cav_rough=0.6, bump=0.3, edge=E)
    crown = T._metal('mallet_crown', T.srgb('C79A4C'), T.srgb('E8C27A'), T.srgb('5A3E1C'), rough=(0.33, 0.37), cav_rough=0.55, bump=0.25, edge=E)
    nt = crown.node_tree; bsdf = nt.nodes['Principled BSDF']
    tc = T._n(nt, 'ShaderNodeTexCoord').outputs['Object']
    sep = T._n(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(tc, sep.inputs[0])
    off = T._math(nt, 'SUBTRACT', sep.outputs['Z'], SHAFT)
    cxyz = T._n(nt, 'ShaderNodeCombineXYZ'); nt.links.new(sep.outputs['Y'], cxyz.inputs['Y']); nt.links.new(off, cxyz.inputs['Z'])
    rad = T._vmath(nt, 'LENGTH', cxyz.outputs[0])
    centre = T._ramp(nt, rad, [(0.035, (1, 1, 1)), (0.075, (0, 0, 0))])
    col0 = bsdf.inputs['Base Color'].links[0].from_socket; r0 = bsdf.inputs['Roughness'].links[0].from_socket
    nt.links.new(T._rgb_mix(nt, centre, col0, T.srgb('E8C27A')), bsdf.inputs['Base Color'])
    nt.links.new(T._rgb_mix(nt, centre, r0, (0.2,) * 3), bsdf.inputs['Roughness'])
    iron = T._metal('mallet_iron', T.IRON, T.srgb('3A3632'), tuple(c * 0.5 for c in T.IRON), rough=(0.55, 0.58), cav_rough=0.7, metal=0.85, bump=0.08, peen=False, edge=(0.7, 0.8))
    ferrule = T._metal('mallet_ferrule', T.srgb('A8803E'), T.srgb('B88C46'), T.srgb('5A3E1C'), rough=(0.45, 0.48), cav_rough=0.6, bump=0.12, peen=False, edge=(0.8, 0.9))
    leather = T._metal('leather', T.srgb('2E2119'), T.srgb('4A3426'), T.srgb('140D08'), rough=(0.65, 0.7), cav_rough=0.8, metal=0.0, bump=0.12, peen=False, edge=E)
    slots = [brass, iron, TM['enamel'], leather, crown, ferrule]
else:
    clay = bpy.data.materials.new('clay'); clay.use_nodes = True
    clay.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.55, 0.55, 0.55, 1)
    slots = [clay] * 6
for s in slots: mal.data.materials.append(s)
a = mal.data.attributes['mid']
for pl in mal.data.polygons: pl.material_index = a.data[pl.index].value
for pl in mal.data.polygons: pl.use_smooth = True
bpy.ops.object.select_all(action='DESELECT'); mal.select_set(True); bpy.context.view_layer.objects.active = mal
bpy.ops.object.shade_auto_smooth(angle=math.radians(40))
print('MALLET_TRIS', sum(len(pl.vertices) - 2 for pl in mal.data.polygons))

# place along the fork's old axis (rest pose) and bone-parent to hand.L
z = Vector(d / np.linalg.norm(d)); x = z.cross(Vector((0, 0, 1))).normalized()   # the head lies horizontal-across at rest
ROLL = float(os.environ.get('MALLET_ROLL', '40'))   # degrees about the shaft
x = Matrix.Rotation(math.radians(ROLL), 3, z) @ x; y = z.cross(x)
M = Matrix.Translation(Vector(WR_L.tolist())) @ Matrix((x, y, z)).transposed().to_4x4()
mal.parent = arm; mal.parent_type = 'BONE'; mal.parent_bone = 'hand.L'
bpy.context.view_layer.update(); mal.matrix_world = M
bpy.context.view_layer.update()

if TEX:
    body.hide_render = True   # bake the mallet on its own (no body AO on it)
    T.bake({'Mallet': [mal]}, OUT, 1024)
    body.hide_render = False
arm.data.pose_position = 'POSE'

# ------------------------------------------------------------------ 3. export (the rig's own settings)
arm.animation_data.action = bpy.data.actions['Idle']
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'rivet_cantor_rig_mallet.blend'))
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); body.select_set(True); mal.select_set(True)
bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', GLB, os.path.getsize(GLB), 'CLIPS', sorted(a.name for a in bpy.data.actions))

# ------------------------------------------------------------------ 4. renders
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 24
sc.view_settings.view_transform = 'AgX'
w = sc.world or bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.16, 0.16, 0.17, 1)
for o in list(sc.objects):
    if o.type == 'LIGHT': o.hide_render = True
ld = bpy.data.lights.new('key', 'SUN'); ld.energy = 3.5; ld.angle = math.radians(6)
lo = bpy.data.objects.new('key', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(45), 0, math.radians(-30))
fm = bpy.data.materials.new('floor'); fm.use_nodes = True; fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.05, 0.05, 0.055, 1)
bm = bmesh.new(); bmesh.ops.create_circle(bm, cap_ends=True, segments=64, radius=12); fl = new_obj('floor', bm); fl.data.materials.append(fm)
cd = bpy.data.cameras.new('cam'); cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam

def shoot(path, pos, look, ortho=None, fov=40, size=(480, 480)):
    cd.type = 'ORTHO' if ortho else 'PERSP'
    if ortho: cd.ortho_scale = ortho
    else: cd.sensor_fit = 'VERTICAL'; cd.angle_y = math.radians(fov)
    cam.location = pos; cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path); px = np.array(img.pixels[:]).reshape(size[1], size[0], 4); bpy.data.images.remove(img)
    return px

POSES = [('Idle', 0.0), ('ChorusCall', 0.35 / 1.2), ('VerseLoop', 0.0), ('HammerRaise', 1.0), ('HammerLoop', 0.0),
         ('ForkPlant', 0.8 / 1.2), ('ForkStrike', 0.55), ('DrumSlam', 1.4 / 2.2), ('DialLoop', 0.25), ('Defeat', 0.9)]
def sheet(name, cam_pos, look, **kw):
    cells = []
    for act, t in POSES:
        A = bpy.data.actions[act]; arm.animation_data.action = A; f0, f1 = A.frame_range
        sc.frame_set(int(round(f0 + (f1 - f0) * t)))
        cells.append(shoot(os.path.join(OUT, '_cell.png'), cam_pos, look, **kw))
    h, w_ = cells[0].shape[:2]; cols = 5; rows = (len(cells) + cols - 1) // cols
    out = np.zeros((h * rows, w_ * cols, 4)); out[..., 3] = 1
    for i, c in enumerate(cells):
        r, k = divmod(i, cols); y = (rows - 1 - r) * h; out[y:y + h, k * w_:(k + 1) * w_] = c
    img = bpy.data.images.new(name, w_ * cols, h * rows); img.pixels = out.ravel().tolist()
    img.filepath_raw = os.path.join(OUT, name + '.png'); img.file_format = 'PNG'; img.save()
    print('SHEET', name, ' / '.join(f'{a}@{t:.2f}' for a, t in POSES))
# close: three-quarter front from his left (the mallet side); gameplay: a player 5 m in front, the game's follow camera
sheet('pose_sheet_close', (4.0, -6.0, 3.3), (0.3, -0.4, 1.75), ortho=5.4)   # tall enough for ForkStrike's overhead mallet
sheet('pose_sheet_game', (0, -5.0 - 9.7, 13.0), (0, -5.0, 1.0), fov=44, size=(640, 360))
# the mallet alone, four angles
for o in (body, fl): o.hide_render = True
arm.animation_data.action = bpy.data.actions['Idle']; arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
mc = mal.matrix_world.translation + mal.matrix_world.to_3x3() @ Vector((0, 0, 0.55))
for i, ang in enumerate((0, 90, 200, 300)):
    v = Matrix.Rotation(math.radians(ang), 3, 'Z') @ Vector((0, -2.6, 0.9))
    shoot(os.path.join(OUT, f'mallet_turn_{i}.png'), mc + v, mc, ortho=1.6, size=(480, 480))
print('DONE')
