# blender -b sylvara_rig.blend -P sylvara_mask_sheet.py -- <out dir>   (review renders only; not part of the build)
# blender -b sylvara_rig.blend -P mask_sheet.py -- <out dir>: the mask closed and split open (second form), close-up and full
import bpy, sys, os, math
from mathutils import Vector, Matrix
out = sys.argv[sys.argv.index('--') + 1]; os.makedirs(out, exist_ok=True)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = 520; sc.render.resolution_y = 620
sc.view_settings.view_transform = 'Standard'
if not sc.world: sc.world = bpy.data.worlds.new('W')
sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.42, 0.45, 0.43, 1)
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); cam.data.type = 'ORTHO'; sc.camera = cam
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sun.data.energy = 3.0; sun.rotation_euler = (0.7, 0.1, -0.6); sc.collection.objects.link(sun)
glow = bpy.data.objects.new('Glow', bpy.data.lights.new('Glow', 'POINT')); glow.data.color = (0.25, 1.0, 0.9); glow.data.energy = 0
glow.location = (0.12, -0.55, 3.25); sc.collection.objects.link(glow)
core = bpy.data.objects.new('Core', bpy.data.meshes.new('Core')); sc.collection.objects.link(core)
import bmesh; bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.09); bm.to_mesh(core.data); bm.free()
mat = bpy.data.materials.new('CoreMat'); mat.use_nodes = True; nt = mat.node_tree; e = nt.nodes.new('ShaderNodeEmission')
e.inputs[0].default_value = (0.15, 0.85, 0.75, 1); e.inputs[1].default_value = 1.6; nt.links.new(e.outputs[0], nt.nodes['Material Output'].inputs[0])
core.data.materials.append(mat); core.location = (0.12, -0.5, 3.25); core.hide_render = True
arm = bpy.data.objects['SylvaraRig']; arm.animation_data.action = bpy.data.actions['Idle']
def pose(open_deg):
    bpy.context.view_layer.update()
    for n, s in (('mask.L', 1), ('mask.R', -1)):
        pb = arm.pose.bones[n]; h = arm.matrix_world @ pb.head
        slide = Matrix.Translation((0.09 * s * open_deg / 16, 0, 0))
        pb.matrix = slide @ Matrix.Translation(h) @ Matrix.Rotation(math.radians(s * open_deg), 4, "Z") @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()
SHOTS = [('closed_close', 0, 1.7, (0.12, -0.6, 3.25), (0.9, -5, 3.6)), ('open_close', 16, 1.7, (0.12, -0.6, 3.25), (0.9, -5, 3.6)),
         ('closed_full', 0, 7.2, (0, 0, 2.7), (-5.5, -9.0, 5.2)), ('open_full', 16, 7.2, (0, 0, 2.7), (-5.5, -9.0, 5.2))]
for name, deg, ortho, look, at in SHOTS:
    arm.animation_data.action = None
    for pb in arm.pose.bones: pb.matrix_basis = Matrix()
    for pb in arm.pose.bones:
        if pb.name.startswith('mask'): pb.matrix_basis = Matrix()
    pose(deg)
    glow.data.energy = 25 if deg else 0; core.hide_render = deg == 0
    cam.data.ortho_scale = ortho; cam.location = at; cam.rotation_euler = (Vector(look) - Vector(at)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(out, name + '.png'); bpy.ops.render.render(write_still=True); print('SHOT', name)
