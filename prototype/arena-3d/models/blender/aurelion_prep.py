# blender -b -P aurelion_prep.py -- <in.glb> <work dir> [tris] [--measure]
# Normalises the Meshy Aurelion (feet at 0, 4.2 m to the top of the halo spire / glaive tip, faces -Y, glaive in his
# right hand = -X), decimates it, saves its textures next to the .blend and adds a glow mask for the cobalt eye slits
# and the chest pendulum. Saves aurelion_prep.blend. --measure also writes front/side orthographic renders with a
# 0.25 m grid (used to place the rig's joints) and prints the mesh's loose parts.
import bpy, sys, os, math, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
measure = '--measure' in args; args = [a for a in args if a != '--measure']
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 50000
HEIGHT = 4.2
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Aurelion'
ob.data.transform(ob.matrix_world); ob.matrix_world.identity(); ob.parent = None
for o in list(bpy.context.scene.objects):
    if o != ob: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred on the torso, 4.2 m tall ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
# centre x/y on the torso band, not the whole bbox: the glaive and halo aren't symmetric
zn = (co[:, 2] - lo[2]) / (hi[2] - lo[2]); band = co[(zn > 0.45) & (zn < 0.65)]
xs = np.sort(band[:, 0]); cx = (xs[len(xs) // 4] + xs[3 * len(xs) // 4]) / 2
ys = np.sort(band[:, 1]); cy = (ys[len(ys) // 4] + ys[3 * len(ys) // 4]) / 2
co = (co - np.array([cx, cy, lo[2]])) * k
# recentre x on the head (the torso band above still leans toward the glaive arm): median x of the mask region
head = co[(co[:, 2] > 3.25) & (co[:, 2] < 3.5) & (co[:, 1] < 0.2) & (np.abs(co[:, 0] - np.median(co[:, 0])) < 0.6)]
co[:, 0] -= np.median(head[:, 0]); print("HEAD_SHIFT", round(float(np.median(head[:, 0])), 3))
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()
print('SCALE', k)

# --- merge coincident verts, decimate ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
if tris > TARGET_TRIS:
    dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = TARGET_TRIS / tris; dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures ---
mat = ob.data.materials[0]; mat.name = 'AurelionMeshy'; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
print('TEX_NODES', list(imgs))
names = sorted(imgs)
base = save(imgs[names[0]], 'aurelion_basecolor')
for extra, nm in zip(names[1:], ('aurelion_orm', 'aurelion_normal')): save(imgs[extra], nm)

# --- loose parts report (connected components) ---
me = ob.data; nv = len(me.vertices)
co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(nv)]); uniq, part, counts = np.unique(roots, return_inverse=True, return_counts=True)
print('PARTS', len(uniq))
for p in np.argsort(-counts)[:25]:
    c = co[part == p]; print('PART', p, counts[p], np.round(c.min(0), 2), np.round(c.max(0), 2))

# --- glow mask: the cobalt eye slits and the gold chest pendulum (positions in GLOW, measured from the renders) ---
GLOW = [  # (x, y, z, radius, kind, level)
    (0.0, -0.08, 3.37, 0.22, "blue", 1.0),   # eye slits on the mask
    (-0.01, -0.20, 2.75, 0.09, "gold", 0.35), # pendulum bob in the chest opening
]
S = base.size[0]
px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)
uvl = me.uv_layers.active.data
fc = np.empty(len(me.polygons) * 3); me.polygons.foreach_get('center', fc); fc = fc.reshape(-1, 3)
r, g, b = px[..., 0], px[..., 1], px[..., 2]
level = np.zeros((S, S), np.float32); tint = np.zeros((S, S, 3), np.float32)
for gx, gy, gz, rad, kind, lv in GLOW:
    region = np.zeros((S, S), bool)
    for p in np.nonzero(np.linalg.norm(fc - (gx, gy, gz), axis=1) < rad)[0]:
        uv = np.array([uvl[i].uv[:] for i in me.polygons[int(p)].loop_indices])
        u0, v0 = np.floor(uv.min(0) * S).astype(int); u1, v1 = np.ceil(uv.max(0) * S).astype(int)
        region[max(0, v0):min(S, v1 + 1), max(0, u0):min(S, u1 + 1)] = True
    if kind == 'blue': hit = region & (b > 0.35) & (b > r * 1.35) & (b > g * 1.1)
    else: hit = region & (r > 0.45) & (g > 0.6 * r) & (b < 0.6 * g)
    level[hit] = lv; tint[hit] = (0.35, 0.55, 1.0) if kind == 'blue' else (1.0, 0.8, 0.4)
    print('GLOW', kind, int(hit.sum()), 'of region', int(region.sum()))
acc = np.zeros_like(level)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(level, dy, 0), dx, 1)
m = np.minimum(acc / 9 * 1.4, max(level.max(), 1e-6))
lum = np.clip(px[..., :3].max(-1) * 1.3, 0, 1)
em = np.zeros_like(px); em[..., :3] = tint * (m * lum)[..., None]; em[..., 3] = 1
if level.any():
    emi = bpy.data.images.new('aurelion_emissive', S, S); emi.pixels[:] = em.ravel()
    emi.filepath_raw = os.path.join(work, 'aurelion_emissive.png'); emi.file_format = 'PNG'; emi.save()
    en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
    nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 3.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'aurelion_prep.blend'))

# --- measuring renders: front (from -Y) and side (from +X), orthographic, 0.25 m grid ---
if measure:
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 1400
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1)
    gm = bpy.data.materials.new('grid'); gm.use_nodes = True; gb = gm.node_tree.nodes['Principled BSDF']
    gb.inputs['Base Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Strength'].default_value = 1
    for view, (loc, rot, axis) in {'front': ((0, -20, 2.1), (90, 0, 0), 'x'), 'side': ((20, 0, 2.1), (90, 0, 90), 'y')}.items():
        lines = []
        # grid as thin boxes in the plane through the origin, behind nothing (drawn in front with small depth)
        for i in range(-10, 11):
            v = i * 0.25
            bpy.ops.mesh.primitive_cube_add(size=1, location=((v, 0.6, 2.25) if axis == 'x' else (-0.6, v, 2.25)))
            c = bpy.context.object; c.scale = ((0.004 if i % 4 else 0.009), 0.001, 2.4) if axis == 'x' else (0.001, (0.004 if i % 4 else 0.009), 2.4); c.data.materials.append(gm); lines.append(c)
        for j in range(0, 19):
            v = j * 0.25
            bpy.ops.mesh.primitive_cube_add(size=1, location=((0, 0.6, v) if axis == 'x' else (-0.6, 0, v)))
            c = bpy.context.object; c.scale = (2.6, 0.001, (0.004 if j % 4 else 0.009)) if axis == 'x' else (0.001, 2.6, (0.004 if j % 4 else 0.009)); c.data.materials.append(gm); lines.append(c)
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5.0; cam.location = loc; cam.rotation_euler = [math.radians(a) for a in rot]
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = cam.rotation_euler; sun.data.energy = 3
        sc.render.filepath = os.path.join(work, f'measure_{view}.png'); bpy.ops.render.render(write_still=True)
        for c in lines: bpy.data.objects.remove(c)
        bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
