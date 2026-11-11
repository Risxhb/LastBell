# blender -b -P crucible_ladle_build.py -- <out.glb> [--textured <tex dir>]
# The Crucible Skater's thrown "ladle of brass" (prompts.md A2): a deep brass ladle cup brimming with molten brass and a
# short molten drip under it, ~0.7 m across like the sphere it replaces. Static mesh, no rig; the code tumbles it in flight.
# Origin at the cup's centre so the flight path matches the old sphere's.
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix, Euler
args = sys.argv[sys.argv.index('--') + 1:]
out_glb = os.path.abspath(args[0])
TEX = args[args.index('--textured') + 1] if '--textured' in args else None
bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {'Brass': ((0.78, 0.60, 0.30), 0.85, 0.42, 0), 'BrassDark': ((0.40, 0.28, 0.13), 0.8, 0.55, 0),
        'Molten': ((1.0, 0.26, 0.02), 0.0, 0.35, 1.0)}
MI = {n: i for i, n in enumerate(MATS)}
bm = bmesh.new()
def tag(vs, mat):
    fs = set()
    for v in vs: fs.update(v.link_faces)
    for f in fs: f.material_index = MI[mat]; f.smooth = True
def place(res, M, mat): bmesh.ops.transform(bm, matrix=M, verts=res['verts']); tag(res['verts'], mat); return res['verts']
# cup: the lower part of a sphere, open at the top, with a rolled lip
res = bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=0.28)
top = [v for v in res['verts'] if v.co.z > 0.065]
bmesh.ops.delete(bm, geom=top, context='VERTS')
cup = [v for v in bm.verts]
bmesh.ops.scale(bm, vec=(1, 1, 0.85), verts=cup); tag(cup, 'Brass')
for f in bm.faces: f.material_index = MI['Brass']; f.smooth = True
def torus(c, R, r, mat, seg=24, seg2=6, axis=(0, 0, 1)):
    M = Matrix.Translation(c) @ Vector(axis).to_track_quat('Z', 'Y').to_matrix().to_4x4(); rows = []; vs = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; row = []
        for j in range(seg2):
            b = 2 * math.pi * j / seg2; rr = R + r * math.cos(b)
            row.append(bm.verts.new(M @ Vector((rr * math.cos(a), rr * math.sin(a), r * math.sin(b)))))
        rows.append(row); vs += row
    for i in range(seg):
        a, b2 = rows[i], rows[(i + 1) % seg]
        for j in range(seg2): bm.faces.new((a[j], b2[j], b2[(j + 1) % seg2], a[(j + 1) % seg2]))
    tag(vs, mat)
lip_r = math.sqrt(0.28 ** 2 - 0.065 ** 2)
torus((0, 0, 0.065 * 0.85), lip_r, 0.026, 'Brass')
torus((0, 0, -0.09), 0.24, 0.014, 'Brass')
# molten brimming dome and the drip under the cup
place(bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=8, radius=1), Matrix.Translation((0, 0, 0.07)) @ Matrix.Diagonal((lip_r - 0.01, lip_r - 0.01, 0.085, 1)), 'Molten')
place(bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=0.085), Matrix.Translation((0.0, 0, -0.19)), 'Molten')
place(bmesh.ops.create_cone(bm, cap_ends=True, segments=14, radius1=0.084, radius2=0.004, depth=0.36), Matrix.Translation((0.06, 0, -0.36)) @ Euler((0, math.radians(18), 0)).to_matrix().to_4x4(), 'Molten')
# handle: a short stub with a ring end
place(bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.042, radius2=0.034, depth=0.24), Matrix.Translation((0.36, 0, 0.06)) @ Euler((0, math.radians(75), 0)).to_matrix().to_4x4(), 'Brass')
torus((0.50, 0, 0.09), 0.045, 0.016, 'BrassDark', seg=12, seg2=5, axis=(0, 1, 0))
bm.normal_update()
me = bpy.data.meshes.new('CrucibleLadle'); bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new('CrucibleLadle', me); bpy.context.scene.collection.objects.link(ob)
for n, (col, metal, rough, emit) in MATS.items():
    m = bpy.data.materials.new(n); m.use_nodes = True; p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if emit: p.inputs['Emission Color'].default_value = (*col, 1); p.inputs['Emission Strength'].default_value = emit; p.inputs['Base Color'].default_value = (col[0] * 0.3, col[1] * 0.3, col[2] * 0.3, 1)
    me.materials.append(m)
if TEX:
    sys.path.append(os.path.dirname(os.path.abspath(__file__)))
    import crucible_skater_tex as T
    T.apply_ladle(ob, TEX)
print('TRIS', sum(len(p.vertices) - 2 for p in me.polygons))
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_image_format='JPEG', export_image_quality=82)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
