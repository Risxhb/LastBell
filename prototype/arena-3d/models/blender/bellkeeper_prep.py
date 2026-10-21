# blender -b -P bellkeeper_prep.py -- <in.glb> <work dir> [tris]
# Normalises the Meshy Bellkeeper (feet at 0, 5 m tall, faces -Y), decimates it, saves its 2K textures next to the
# .blend and adds a glow mask for the mask's focal light and the pendulum bob. Saves bellkeeper_prep.blend.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 55000
HEIGHT = 5.0
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Bellkeeper'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred, 5 m tall ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()

# --- merge coincident verts so the decimator treats seams sanely, then decimate ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.001); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = min(1, TARGET_TRIS / tris); dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures: Meshy already ships 2K; save them as files so the rig step and exporter read the same images ---
mat = ob.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
base = save(imgs['Image_0'], 'bellkeeper_basecolor')
save(imgs['Image_1'], 'bellkeeper_orm')
save(imgs['Image_2'], 'bellkeeper_normal')

# --- glow mask: bright resonant-gold texels, but only on faces of the mask's face plate and the pendulum bob ---
# Brass all over the body is the same hue, so colour alone can't pick the glow; restrict by where the face sits.
S = base.size[0]
px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)   # byte image: display (sRGB) colours
me = ob.data; uvl = me.uv_layers.active.data
fc = np.empty(len(me.polygons) * 3); me.polygons.foreach_get('center', fc); fc = fc.reshape(-1, 3)
REGIONS = [  # (x, y, z, radius, min red, glow) in metres on the prepped mesh (-Y forward), measured from renders
    (0, -0.30, 4.33, 0.12, 0.75, 1.0),    # focal light on the mask: only the bright gold dot, not the ivory
    (0, -0.43, 2.97, 0.20, 0.35, 0.22),   # pendulum bob: the whole sphere, dimmer so its shading still reads
]
r, g, b = px[..., 0], px[..., 1], px[..., 2]
gold = np.zeros((S, S), bool); level = np.zeros((S, S), np.float32); region_px = 0
for cx, cy, cz, rad, rmin, glow in REGIONS:
    region = np.zeros((S, S), bool)
    # rasterise the UV bounding boxes of the faces inside the sphere into a texel mask
    for p in np.nonzero(np.linalg.norm(fc - (cx, cy, cz), axis=1) < rad)[0]:
        uv = np.array([uvl[i].uv[:] for i in me.polygons[int(p)].loop_indices])
        u0, v0 = np.floor(uv.min(0) * S).astype(int); u1, v1 = np.ceil(uv.max(0) * S).astype(int)
        region[max(0, v0):min(S, v1 + 1), max(0, u0):min(S, u1 + 1)] = True
    hit = region & (r > rmin) & (g > 0.7 * rmin) & (b < 0.65 * g)
    gold |= hit; level[hit] = glow; region_px += int(region.sum())
region = region_px
m = level; acc = np.zeros_like(m)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(m, dy, 0), dx, 1)
m = np.minimum(acc / 9 * 1.4, level.max())
# tint by the texel's own colour so the bob keeps its hammered-metal variation
lum = np.clip(px[..., :3].max(-1) * 1.3, 0, 1)
em = np.zeros_like(px); em[..., 0] = m * lum; em[..., 1] = m * lum * 0.78; em[..., 2] = m * lum * 0.35; em[..., 3] = 1
emi = bpy.data.images.new('bellkeeper_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'bellkeeper_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_PIXELS', int(gold.sum()), 'region', region, 'of', S * S)
if gold.any():
    en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
    nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 3.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'bellkeeper_prep.blend'))
