# blender -b -P chimesinger_prep.py -- <in.glb> <work dir> [target tris] [staff.glb]
# Normalises the Meshy Chimesinger (feet at 0, 1.8 m to the top of the halo, faces -Y), decimates it, shrinks
# textures and adds a glow mask for the halo and the gold/blue gems. Adapted from the Vanguard's prep.py.
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 50000
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Chimesinger'
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred, 1.8 tall including the halo ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = 1.8 / (hi[2] - lo[2])
co = (co - np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()

# --- merge coincident verts so the decimator treats seams sanely, then decimate ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0003); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
# Hands keep far more detail than the rest (they have to close into a fist around the staff): split them off past the
# wrist cuff, decimate each part to its own budget, join back. The cuff hides the seam.
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
sel = np.zeros(len(co), bool)
for s in (1, -1):
    Wp = np.array([s * 0.41, -0.05, 1.03]); d = np.array([s * 0.53, -0.05, 0.935]) - Wp; Lh = np.linalg.norm(d); d /= Lh
    rel = co - Wp; t = rel @ d; perp = np.linalg.norm(rel - np.outer(t, d), axis=1)
    sel |= (t > 0.012) & (t < Lh + 0.04) & (perp < 0.07) & (co[:, 2] > 0.86)
me = ob.data; me.vertices.foreach_set('select', sel)
ev = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ev); me.edges.foreach_set('select', sel[ev.reshape(-1, 2)].all(1))
ps = np.array([all(sel[i] for i in p.vertices) for p in me.polygons]); me.polygons.foreach_set('select', ps)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
hands = [o for o in bpy.context.selected_objects if o is not ob][0]
HAND_TRIS = 7000
for o, budget in ((ob, TARGET_TRIS - HAND_TRIS), (hands, HAND_TRIS)):
    o.data.calc_loop_triangles(); n = len(o.data.loop_triangles)
    dm = o.modifiers.new('dec', 'DECIMATE'); dm.ratio = min(1, budget / n); dm.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='dec')
    print('PART_TRIS', o.name, n, '->', budget)
bpy.ops.object.select_all(action='DESELECT'); hands.select_set(True); ob.select_set(True)
bpy.context.view_layer.objects.active = ob; bpy.ops.object.join()
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures: shrink to 2K files the nodes point at, then derive an emission mask ---
mat = ob.data.materials[0]; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def shrink(node, size, name):
    # save a downscaled PNG and repoint the node at it, so the exporter doesn't ship the packed original
    img = node.image
    if img.size[0] != size: img.scale(size, size)
    path = os.path.join(work, name + '.png'); img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
base = shrink(imgs['Image_0'], 2048, 'chimesinger_basecolor')
shrink(imgs['Image_1'], 1024, 'chimesinger_orm')
shrink(imgs['Image_2'], 2048, 'chimesinger_normal')

# Glow: the halo (geometry above/behind the head) and the small blue gems. The halo is found in UV space by
# rasterising the UVs of faces whose verts sit in the halo region, so the mask follows the texture islands.
S = 2048
px = np.array(base.pixels[:], dtype=np.float32).reshape(-1, 4)   # byte image: display (sRGB) colours
r, g, b = px[:, 0], px[:, 1], px[:, 2]
gems = (b > 0.45) & (b > r + 0.18) & (b > g + 0.05)
me = ob.data; co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
uv = np.empty(len(me.loops) * 2); me.uv_layers.active.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
lv = np.empty(len(me.loops), np.int64); me.loops.foreach_get('vertex_index', lv)
head_top = 1.8 * 0.93
halo_v = (co[:, 2] > 1.8 * 0.80) & ((co[:, 2] > head_top) | (np.abs(co[:, 0]) > 0.15) | (co[:, 1] > 0.06))
halo = np.zeros((S, S), bool)
for p in me.polygons:
    ls = range(p.loop_start, p.loop_start + p.loop_total)
    if not all(halo_v[lv[l]] for l in ls): continue
    q = (uv[list(ls)] * (S - 1)).round().astype(int); x0, y0 = q.min(0); x1, y1 = q.max(0)
    halo[max(0, y0):y1 + 1, max(0, x0):x1 + 1] = True
gold = (r > 0.35) & (r > b + 0.12)            # only the gold parts of the halo islands glow
m = ((halo.reshape(-1) & gold) * 0.6 + gems).clip(0, 1).astype(np.float32)
m2 = m.reshape(S, S); acc = np.zeros_like(m2)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(m2, dy, 0), dx, 1)
m2 = np.clip(acc / 9 * 1.4, 0, 1).reshape(-1)
em = np.zeros_like(px); em[:, :3] = px[:, :3] * m2[:, None]; em[:, 3] = 1
emi = bpy.data.images.new('chimesinger_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'chimesinger_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_PIXELS halo', int((halo.reshape(-1) & gold).sum()), 'gems', int(gems.sum()), 'of', S * S)
en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 2.0

# --- staff (separate Meshy model): shaft along +Z, butt at z=0, 1.5 m long, ring plane = XZ ---
if len(args) > 3 and os.path.exists(args[3]):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=args[3])
    st = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
    st.name = st.data.name = 'ChimeStaff'
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active = st; st.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    sc = np.empty(len(st.data.vertices) * 3); st.data.vertices.foreach_get('co', sc); sc = sc.reshape(-1, 3)
    lo, hi = sc.min(0), sc.max(0); k = 1.5 / (hi[2] - lo[2])
    shaft = sc[sc[:, 2] < lo[2] + 0.3 * (hi[2] - lo[2])]          # lower shaft only: the banners hang off-centre
    c = np.array([np.median(shaft[:, 0]), np.median(shaft[:, 1]), lo[2]])
    sc = (sc - c) * k
    # the ring is widest across one horizontal axis; make that X
    if np.ptp(sc[:, 1]) > np.ptp(sc[:, 0]): sc = sc[:, [1, 0, 2]] * np.array([1, -1, 1])
    st.data.vertices.foreach_set('co', sc.ravel()); st.data.update()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0003); bpy.ops.object.mode_set(mode='OBJECT')
    st.data.calc_loop_triangles(); n = len(st.data.loop_triangles)
    d = st.modifiers.new('dec', 'DECIMATE'); d.ratio = min(1, 9000 / n); d.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec'); bpy.ops.object.shade_smooth()
    st.data.calc_loop_triangles(); print('STAFF_TRIS', n, '->', len(st.data.loop_triangles))
    snt = st.data.materials[0].node_tree; st.data.materials[0].name = 'chimestaff'
    simgs = {nd.image.name: nd for nd in snt.nodes if nd.type == 'TEX_IMAGE'}
    for src_name, size, name in (('Image_0', 1024, 'chimestaff_basecolor'), ('Image_1', 512, 'chimestaff_orm'), ('Image_2', 1024, 'chimestaff_normal')):
        nd = next((v for kk, v in simgs.items() if kk.split('.')[0] == src_name), None)
        if nd: shrink(nd, size, name)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'chimesinger_prep.blend'))
