# blender -b -P prep.py -- <in.glb> <work dir>
# Normalises the Meshy Vanguard (feet at 0, 1.8 m, faces -Y), decimates it, shrinks textures and adds a glow mask.
import bpy, sys, os, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 40000
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Vanguard'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred, 1.8 tall ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = 1.8 / (hi[2] - lo[2])
co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()

# --- merge coincident verts so the decimator treats seams sanely, then decimate ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0004); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = min(1, TARGET_TRIS / tris); dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures: shrink, then derive an emission mask from the cyan rune / eye pixels ---
mat = ob.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def shrink(node, size, name):
    # save a downscaled PNG, then point the node at that file: the imported image stays packed at full size
    # in the .blend otherwise, and the exporter would ship the 8K original
    img = node.image; img.scale(size, size)
    path = os.path.join(work, name + '.png'); img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
base = shrink(imgs['Image_0'], 2048, 'vanguard_basecolor')
shrink(imgs['Image_1'], 1024, 'vanguard_orm')
shrink(imgs['Image_2'], 2048, 'vanguard_normal')
px = np.array(base.pixels[:], dtype=np.float32).reshape(-1, 4)   # byte image: values are display (sRGB) colours
r, g, b = px[:, 0], px[:, 1], px[:, 2]
# cloth blue sits at G/B 0.3-0.5, the cyan runes at G/B >= 0.6
cyan = (b > 0.55) & (g > 0.6 * b) & (b > r + 0.15)
eyes = (b > 0.85) & (g > 0.8) & (r > 0.7) & (b >= r)
m = (cyan | eyes).astype(np.float32)
# soften: 3x3 box blur so the glow edge isn't aliased
S = 2048; m2 = m.reshape(S, S); acc = np.zeros_like(m2)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(m2, dy, 0), dx, 1)
m2 = np.clip(acc / 9 * 1.4, 0, 1).reshape(-1)
em = np.zeros_like(px); em[:, :3] = px[:, :3] * m2[:, None]; em[:, 3] = 1
emi = bpy.data.images.new('vanguard_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'vanguard_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_PIXELS', int((cyan | eyes).sum()), 'of', S * S)
en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 4.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'vanguard_prep.blend'))
