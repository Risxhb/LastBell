# blender -b -P pinnacle.py -- <unity Models dir> <trims dir> [renders dir]
# Area 4 rim pinnacle, modelled in Blender at three heights (2.5 / 3.5 / 4.5 m): stepped plinth, square shaft with
# chamfered corner ribs, a gablet on each face, brass band, octagonal spire with a brass collar and leaf crockets up
# its edges, orb-and-cross finial. Stone parts get a 2.5 cm, 2-segment bevel; everything is flat-shaded for the
# faceted house look. UVs are cube-projected at 2.4 m per tile to sit on the shared stone/brass trim sheets.
# Material slots: 0 stone, 1 brass, 2 polished brass (matches the Unity builder). Saves pinnacle.blend next to this
# script, exports Pinnacle/Pinnacle.fbx (objects Pinnacle_25/35/45) and renders previews.
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--') + 1:]
models, trims = args[0], args[1]
renders = args[2] if len(args) > 2 else os.path.join(os.path.dirname(__file__), 'renders')
here = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(models, 'Pinnacle'), exist_ok=True); os.makedirs(renders, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- materials (preview look in Blender; Unity assigns its own by slot) ----
def material(name, img=None, tint=(1, 1, 1), metal=0.0, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if img and os.path.exists(img):
        t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(img)
        mix = m.node_tree.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs['Factor'].default_value = 1
        mix.inputs[7].default_value = (*tint, 1)
        m.node_tree.links.new(t.outputs['Color'], mix.inputs[6]); m.node_tree.links.new(mix.outputs[2], b.inputs['Base Color'])
    else: b.inputs['Base Color'].default_value = (*tint, 1)
    return m
M_STONE = material('Stone', os.path.join(trims, 'StoneTrim_BL.png'))
M_BRASS = material('Brass', None, (0.66, 0.46, 0.2), 0.35, 0.45)  # preview colour; Unity uses the brass trim
M_POL = material('BrassPolished', None, (0.85, 0.62, 0.28), 0.45, 0.35)

# ---- small modelling helpers (each part is placed by hand below) ----
def new_obj(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); o.data.materials.append(mat); return o

def box(name, size, loc, mat, top_scale=1.0, rot_z=0.0):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
        if v.co.z > 0: v.co.x *= top_scale; v.co.y *= top_scale
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rot_z, 3, 'Z'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(loc)); return new_obj(name, bm, mat)

def cone(name, r1, r2, depth, sides, loc, mat, rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, depth / 2)))
    m = Matrix.Rotation(rot[2], 3, 'Z') @ Matrix.Rotation(rot[1], 3, 'Y') @ Matrix.Rotation(rot[0], 3, 'X')
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=m); bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(loc))
    return new_obj(name, bm, mat)

def gablet(name, loc, face_rot, mat):
    """Triangular gable plate on a shaft face: 0.48 wide, 0.42 tall, 0.12 deep, front facing +Y before rotation."""
    bm = bmesh.new(); w, h, d = 0.24, 0.42, 0.06
    pts = [(-w, -d, 0), (w, -d, 0), (0, -d, h), (-w, d, 0), (w, d, 0), (0, d, h)]
    v = [bm.verts.new(p) for p in pts]
    for f in ((0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)): bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(face_rot, 3, 'Z'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(loc)); return new_obj(name, bm, mat)

def bevel(o, width=0.025, segs=2):
    m = o.modifiers.new('Bevel', 'BEVEL'); m.width = width; m.segments = segs; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(35)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='Bevel')

def build(height):
    parts = []; sh = max(0.6, height - 2.2); top = 0.47 + sh
    # plinth and step
    parts += [box('plinth', (0.9, 0.9, 0.35), (0, 0, 0.175), M_STONE), box('step', (0.74, 0.74, 0.12), (0, 0, 0.41), M_STONE)]
    # shaft, plus corner ribs whose tops are drawn in like weathered set-offs
    parts.append(box('shaft', (0.56, 0.56, sh), (0, 0, 0.47 + sh / 2), M_STONE))
    for sx, sy in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        parts.append(box('rib', (0.17, 0.17, sh * 0.9), (sx * 0.3, sy * 0.3, 0.47 + sh * 0.45), M_STONE, top_scale=0.55, rot_z=math.radians(45)))
    for i in range(4):  # gablets crown each face
        a = i * math.pi / 2; parts.append(gablet('gablet', (math.sin(a) * 0.27, math.cos(a) * 0.27, top - 0.02), -a, M_STONE))
    parts.append(box('band', (0.64, 0.64, 0.1), (0, 0, top + 0.05), M_BRASS))
    # octagonal spire with a slight swell, brass collar at 40 %
    sp = cone('spire', 0.34, 0.0, 1.9, 8, (0, 0, top + 0.1), M_STONE, (0, 0, math.radians(22.5)))
    for v in sp.data.vertices:
        f = (v.co.z - top - 0.1) / 1.9
        if 0.05 < f < 0.95: s = 1 + 0.08 * math.sin(f * math.pi); v.co.x *= s; v.co.y *= s
    parts.append(sp)
    parts.append(cone('collar', 0.25, 0.23, 0.07, 8, (0, 0, top + 0.1 + 1.9 * 0.38), M_BRASS, (0, 0, math.radians(22.5))))
    # leaf crockets climbing the four diagonal edges
    for i in range(4):
        a = math.radians(45 + 90 * i)
        for k in (1, 2, 3):
            f = k / 4; r = 0.34 * (1 - f) * 1.05 + 0.03; z = top + 0.1 + 1.9 * f
            # a curled-leaf knob: a low icosphere, squashed flat against the edge, its top rolled outward
            bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.07)
            for v in bm.verts:
                v.co.y *= 0.55; v.co.z *= 1.25
                if v.co.z > 0: v.co.x += 0.6 * v.co.z * v.co.z / 0.09
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(a, 3, 'Z'))
            bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((math.cos(a) * r, math.sin(a) * r, z)))
            parts.append(new_obj('crocket', bm, M_BRASS))
    # orb and cross
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.1)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, top + 2.06))); parts.append(new_obj('orb', bm, M_POL))
    parts.append(cone('stem', 0.05, 0.035, 0.32, 6, (0, 0, top + 1.78), M_POL))
    parts += [box('cross', (0.05, 0.05, 0.34), (0, 0, top + 2.32), M_POL), box('crossbar', (0.2, 0.05, 0.05), (0, 0, top + 2.38), M_POL)]
    for p in parts:
        if p.data.materials[0] == M_STONE and p.name.split('.')[0] in ('plinth', 'step', 'shaft', 'rib', 'band', 'spire'): bevel(p, 0.025 if p.name.startswith('spire') is False else 0.012)
    # join: material slots in fixed order stone / brass / polished
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join(); o = bpy.context.active_object
    o.name = o.data.name = 'Pinnacle_%d' % round(height * 10)
    order = [M_STONE, M_BRASS, M_POL]; remap = {i: order.index(m) for i, m in enumerate(o.data.materials)}
    for poly in o.data.polygons: poly.material_index = remap[poly.material_index]
    while len(o.data.materials) < len(order): o.data.materials.append(None)
    for i, m in enumerate(order): o.data.materials[i] = m  # assign in place: clear() would reset every face to slot 0
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.uv.cube_project(cube_size=2.4); bpy.ops.object.mode_set(mode='OBJECT'); bpy.ops.object.shade_flat()
    o.data.calc_loop_triangles(); print(o.name, 'tris', len(o.data.loop_triangles))
    return o

objs = [build(h) for h in (2.5, 3.5, 4.5)]
for i, o in enumerate(objs): o.location.x = (i - 1) * 1.6   # line-up for the preview render only

# ---- preview renders ----
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
    try: sc.render.engine = eng; break
    except TypeError: pass
sc.render.resolution_x, sc.render.resolution_y = 1400, 1000
world = bpy.data.worlds.new('Void'); sc.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.16, 0.12, 0.3, 1); world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.6
def light(kind, energy, color, rot):
    l = bpy.data.objects.new(kind, bpy.data.lights.new(kind, 'SUN')); l.data.energy = energy; l.data.color = color; l.rotation_euler = rot; sc.collection.objects.link(l)
light('key', 3.2, (1.0, 0.92, 0.85), (math.radians(50), 0, math.radians(-40)))
light('rim', 1.5, (0.55, 0.6, 1.0), (math.radians(60), 0, math.radians(150)))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
def shoot(path, loc, look):
    cam.location = loc; d = Vector(look) - Vector(loc); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
shoot(os.path.join(renders, 'pinnacle_lineup.png'), (3.6, -6.2, 3.4), (0, 0, 2.0))
shoot(os.path.join(renders, 'pinnacle_top_closeup.png'), (1.9, -2.3, 3.9), (1.6, 0, 3.4))

# ---- export (back at the origin) and save ----
for o in objs: o.location.x = 0
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(models, 'Pinnacle', 'Pinnacle.fbx'), use_selection=True, object_types={'MESH'},
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(here, 'pinnacle.blend'))
print('done')
