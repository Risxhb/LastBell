# blender -b -P bellkeeper2_prep.py -- <in.glb> <work dir> [tris]
# Bellkeeper 2.0 (sheet 04-bellkeeper-v1.png, Meshy "Clockwork Sentinel" multi-view). Normalises the Meshy mesh
# (feet at 0, 5 m tall, faces -Y), centring it on the boots rather than the bounding box, because the pendulum hand
# widens the box to one side. Decimates the 5M-triangle source, saves its 2K textures next to the .blend and adds a
# glow mask for the mask's eye line and the chest clapper. Saves bellkeeper2_prep.blend.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 60000
HEIGHT = 5.0
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Bellkeeper2'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# --- normalise: feet at z=0, 5 m tall, centred between the boots ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
boots = co[co[:, 2] < lo[2] + 0.06 * (hi[2] - lo[2])]          # the soles: below the pendulum head's lowest point
centre = np.array([(boots[:, 0].min() + boots[:, 0].max()) / 2, (boots[:, 1].min() + boots[:, 1].max()) / 2, lo[2]])
co = (co - centre) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()
print('CENTRE_SHIFT', ((centre - (lo + hi) / 2) * k).round(3), 'BOOTS_X', ((boots[:, 0].min() - centre[0]) * k).round(2), ((boots[:, 0].max() - centre[0]) * k).round(2))

# --- merge coincident verts so the decimator treats seams sanely, then decimate in two passes ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
for target in (max(TARGET_TRIS * 8, 400000), TARGET_TRIS):
    ob.data.calc_loop_triangles(); now = len(ob.data.loop_triangles)
    if now <= target: continue
    dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = target / now; dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures: Meshy ships 2K; save them as files so the rig step and exporter read the same images ---
mat = ob.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
base = save(imgs['Image_0'], 'bellkeeper2_basecolor')
# Meshy's base colour came out ~30 % darker than the first Bellkeeper's and reads black at the gameplay camera.
# Lift the warm (brass/gold) texels toward the sheet's aged brass; the midnight-cobalt plates stay dark.
bp = np.array(base.pixels[:], dtype=np.float32).reshape(-1, 4)
warm = np.clip((bp[:, 0] - bp[:, 2]) / np.maximum(bp[:, 0], 0.05) * 2.5, 0, 1)
bp[:, :3] = np.clip(bp[:, :3] * (1 + 0.55 * warm)[:, None] + 0.03 * warm[:, None], 0, 1)
base.pixels[:] = bp.ravel(); base.save()
print('BRASS_LIFT warm texels', int((warm > 0.5).sum()))
save(imgs['Image_1'], 'bellkeeper2_orm')
save(imgs['Image_2'], 'bellkeeper2_normal')

# --- glow mask: bright gold texels, restricted to faces near the eye line and the clapper ---
# Brass covers the whole body, so colour alone can't pick the glow; restrict by where those parts sit.
S = base.size[0]
px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)
me = ob.data; uvl = me.uv_layers.active.data
fc = np.empty(len(me.polygons) * 3); me.polygons.foreach_get('center', fc); fc = fc.reshape(-1, 3)
REGIONS = [  # (x, y, z, radius, min red, glow) in metres on the prepped mesh (-Y forward), measured from renders
    tuple(float(v) for v in r.split(',')) for r in os.environ.get('BK2_GLOW', '').split(';') if r.strip()
]
r, g, b = px[..., 0], px[..., 1], px[..., 2]
gold = np.zeros((S, S), bool); level = np.zeros((S, S), np.float32); region_px = 0
for cx, cy, cz, rad, rmin, glow in REGIONS:
    region = np.zeros((S, S), bool)
    for p in np.nonzero(np.linalg.norm(fc - (cx, cy, cz), axis=1) < rad)[0]:
        uv = np.array([uvl[i].uv[:] for i in me.polygons[int(p)].loop_indices])
        u0, v0 = np.floor(uv.min(0) * S).astype(int); u1, v1 = np.ceil(uv.max(0) * S).astype(int)
        region[max(0, v0):min(S, v1 + 1), max(0, u0):min(S, u1 + 1)] = True
    hit = region & (r > rmin) & (g > 0.7 * rmin) & (b < 0.65 * g)
    gold |= hit; level[hit] = glow; region_px += int(region.sum())
m = level; acc = np.zeros_like(m)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(m, dy, 0), dx, 1)
m = np.minimum(acc / 9 * 1.4, level.max() if level.size else 0)
lum = np.clip(px[..., :3].max(-1) * 1.3, 0, 1)
em = np.zeros_like(px); em[..., 0] = m * lum; em[..., 1] = m * lum * 0.78; em[..., 2] = m * lum * 0.35; em[..., 3] = 1
emi = bpy.data.images.new('bellkeeper2_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'bellkeeper2_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_PIXELS', int(gold.sum()), 'region', region_px, 'of', S * S)
if gold.any():
    en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
    nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 3.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'bellkeeper2_prep.blend'))
