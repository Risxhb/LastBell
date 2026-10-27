# blender -b -P arcanist_prep.py -- <in.glb> <work dir> [target tris] [astrolabe.glb]
# Normalises the Meshy Arcanist (feet at 0, 1.8 m to the top of the hair, faces -Y), decimates it, shrinks textures
# and adds a glow mask for the teal/blue gems. The astrolabe (separate Meshy model) is normalised to 0.5 m tall with
# its orb at the origin. Adapted from the Chimesinger's chimesinger_prep.py.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 50000
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Arcanist'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def verts(o):
    co = np.empty(len(o.data.vertices) * 3); o.data.vertices.foreach_get('co', co); return co.reshape(-1, 3)

# --- normalise: feet at z=0, centred, 1.8 tall ---
co = verts(ob); lo, hi = co.min(0), co.max(0); k = 1.8 / (hi[2] - lo[2])
co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()

def decimate(o, budget, merge=0.0003):
    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=merge); bpy.ops.object.mode_set(mode='OBJECT')
    o.data.calc_loop_triangles(); n = len(o.data.loop_triangles)
    d = o.modifiers.new('dec', 'DECIMATE'); d.ratio = min(1, budget / n); d.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec'); bpy.ops.object.shade_smooth()
    o.data.calc_loop_triangles(); return n, len(o.data.loop_triangles)

n0, n1 = decimate(ob, TARGET_TRIS); print('TRIS', n0, '->', n1)

def shrink(node, size, name):
    # save a downscaled PNG and repoint the node at it, so the exporter doesn't ship the packed original
    img = node.image
    if img.size[0] != size: img.scale(size, size)
    path = os.path.join(work, name + '.png'); img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small

def shrink_all(o, prefix, sizes):
    nt = o.data.materials[0].node_tree; o.data.materials[0].name = prefix
    imgs = {n.image.name.split('.')[0]: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
    out = {}
    for key, (size, name) in sizes.items():
        if key in imgs: out[key] = shrink(imgs[key], size, prefix + '_' + name)
    return nt, out

nt, tex = shrink_all(ob, 'arcanist', {'Image_0': (2048, 'basecolor'), 'Image_1': (1024, 'orm'), 'Image_2': (2048, 'normal')})
bsdf = nt.nodes['Principled BSDF']

def glow(base, mask_fn, name, strength):
    S = base.size[0]
    px = np.array(base.pixels[:], dtype=np.float32).reshape(-1, 4)   # byte image: display (sRGB) colours
    m = mask_fn(px[:, 0], px[:, 1], px[:, 2]).astype(np.float32).reshape(S, S); acc = np.zeros_like(m)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1): acc += np.roll(np.roll(m, dy, 0), dx, 1)
    m = np.clip(acc / 9 * 1.4, 0, 1).reshape(-1)
    em = np.zeros_like(px); em[:, :3] = px[:, :3] * m[:, None]; em[:, 3] = 1
    emi = bpy.data.images.new(name, S, S); emi.pixels[:] = em.ravel()
    emi.filepath_raw = os.path.join(work, name + '.png'); emi.file_format = 'PNG'; emi.save()
    return emi, int((m > 0.5).sum())

def hook_emission(nt, emi, strength):
    b = nt.nodes['Principled BSDF']; en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
    nt.links.new(en.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = strength

# Body glow: only small bright teal/sky-blue gems (the robe is a deep, darker blue: value well under 0.5).
gem = lambda r, g, b: (b > 0.6) & (g > 0.45) & (b > r + 0.2)
emi, n = glow(tex['Image_0'], gem, 'arcanist_emissive', 2.0); hook_emission(nt, emi, 2.0)
print('GLOW_PIXELS body', n)

# --- astrolabe (separate Meshy model): 0.5 m tall, orb (brightest blue cluster) at the origin, rings unchanged ---
if len(args) > 3 and os.path.exists(args[3]):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=args[3])
    st = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
    st.name = st.data.name = 'Astrolabe'
    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = st; st.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    sc = verts(st); lo, hi = sc.min(0), sc.max(0); k = 0.5 / (hi[2] - lo[2])
    # the ring cage is the widest band; its centre (mid of the x/y extent at the cage's height) is the orb
    cage = sc[(sc[:, 2] > lo[2] + 0.35 * (hi[2] - lo[2]))]
    c = np.array([(cage[:, 0].min() + cage[:, 0].max()) / 2, (cage[:, 1].min() + cage[:, 1].max()) / 2,
                  (cage[:, 2].min() + cage[:, 2].max()) / 2])
    st.data.vertices.foreach_set('co', ((sc - c) * k).ravel()); st.data.update()
    n0, n1 = decimate(st, 14000, merge=0.0001); print('ASTRO_TRIS', n0, '->', n1)
    snt, stex = shrink_all(st, 'astrolabe', {'Image_0': (1024, 'basecolor'), 'Image_1': (512, 'orm'), 'Image_2': (1024, 'normal')})
    orb = lambda r, g, b: (b > 0.55) & (b > r + 0.15)
    emi, n = glow(stex['Image_0'], orb, 'astrolabe_emissive', 3.0); hook_emission(snt, emi, 3.0)
    print('GLOW_PIXELS astrolabe', n)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'arcanist_prep.blend'))
