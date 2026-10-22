# blender -b -P sylvara_prep.py -- <in.glb> <work dir>
# Normalises the Meshy Sylvara ("Verdant Bellwarden" in the Meshy file name): merges the UV-seam splits, puts the
# feet on z=0 centred between the four root feet, scales her to 4 m and keeps her facing -Y. Saves the 2K textures next
# to the .blend and adds a glow mask for the turquoise eyes (the only turquoise in the texture). The mesh is already
# light (~16k tris), so nothing is decimated. Saves sylvara_prep.blend.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
HEIGHT = 4.0
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Sylvara'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# --- merge the UV-seam splits (Meshy ships ~4,500 loose pieces) so weights and normals are continuous ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')

# --- normalise: 4 m tall, feet at z=0, centred between the four feet (the fork and claw make the bbox lopsided) ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
co = (co - [0, 0, lo[2]]) * k
feet = co[co[:, 2] < 0.3]
C = np.array([[-1, -.8], [1, -.8], [-.5, .8], [.5, .8]], float) + feet[:, :2].mean(0)
for _ in range(40):
    lab = np.argmin(((feet[:, None, :2] - C[None]) ** 2).sum(-1), 1)
    C = np.array([feet[lab == i, :2].mean(0) for i in range(4)])
centre = C.mean(0); co[:, :2] -= centre
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()
print('FEET', np.round(C - centre, 2).tolist())
bpy.ops.object.shade_smooth()
ob.data.calc_loop_triangles(); print('TRIS', len(ob.data.loop_triangles))

# --- textures: save Meshy's 2K maps as files so the rig step and the exporter read the same images ---
mat = ob.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
base = save(imgs['Image_0'], 'sylvara_basecolor')
save(imgs['Image_1'], 'sylvara_orm')
save(imgs['Image_2'], 'sylvara_normal')

# --- glow mask: the turquoise eye texels, grown a little so they read from the gameplay camera ---
S = base.size[0]
px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)
r, g, b = px[..., 0], px[..., 1], px[..., 2]
eye = (g > 0.45) & (b > 0.45) & (r < 0.5) & (np.minimum(g, b) - r > 0.18)
m = eye.astype(np.float32)
for _ in range(3):   # dilate 3 texels
    acc = np.zeros_like(m)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1): acc = np.maximum(acc, np.roll(np.roll(m, dy, 0), dx, 1))
    m = acc
em = np.zeros_like(px); em[..., 0] = m * 0.25; em[..., 1] = m * 0.95; em[..., 2] = m * 0.85; em[..., 3] = 1
emi = bpy.data.images.new('sylvara_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'sylvara_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_TEXELS', int(eye.sum()), '->', int((m > 0).sum()))
en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 3.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'sylvara_prep.blend'))
