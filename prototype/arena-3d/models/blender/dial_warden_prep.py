# blender -b -P dial_warden_prep.py -- <in.glb> <work dir> [tris] [--measure]
# Normalises the Meshy Dial Warden (Meshy_AI_Celestial_Clockwork_G: feet at 0, 2.8 m to the top of the head spire,
# faces -Y, clock-hand blade in his right hand = -X, counterweight on its chain in his left = +X), decimates it and
# saves its textures next to the .blend. Saves dial_warden_prep.blend. --measure also writes front/side orthographic
# renders with a 0.25 m grid (used to place the rig's joints) and prints the mesh's loose parts. Based on aurelion_prep.py.
import bpy, sys, os, math, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
measure = '--measure' in args; args = [a for a in args if a != '--measure']
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 50000
HEIGHT = 2.8
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'DialWarden'
ob.data.transform(ob.matrix_world); ob.matrix_world.identity(); ob.parent = None
for o in list(bpy.context.scene.objects):
    if o != ob: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred on the round body, 2.8 m tall ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
# centre x/y on the sphere band only: the blade and counterweight arms aren't symmetric
zn = (co[:, 2] - lo[2]) / (hi[2] - lo[2]); band = co[(zn > 0.45) & (zn < 0.7)]
xs = np.sort(band[:, 0]); cx = (xs[len(xs) // 4] + xs[3 * len(xs) // 4]) / 2
ys = np.sort(band[:, 1]); cy = (ys[len(ys) // 4] + ys[3 * len(ys) // 4]) / 2
co = (co - np.array([cx, cy, lo[2]])) * k
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
mat = ob.data.materials[0]; mat.name = 'DialWardenMeshy'; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
print('TEX_NODES', list(imgs))
names = sorted(imgs)
base = save(imgs[names[0]], 'dial_warden_basecolor')
for extra, nm in zip(names[1:], ('dial_warden_orm', 'dial_warden_normal')): save(imgs[extra], nm)

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

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'dial_warden_prep.blend'))

# --- measuring renders: front (from -Y) and side (from +X), orthographic, 0.25 m grid ---
if measure:
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 1400
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1)
    gm = bpy.data.materials.new('grid'); gm.use_nodes = True; gb = gm.node_tree.nodes['Principled BSDF']
    gb.inputs['Base Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Strength'].default_value = 1
    for view, (loc, rot, axis) in {'front': ((0, -20, 1.4), (90, 0, 0), 'x'), 'side': ((20, 0, 1.4), (90, 0, 90), 'y')}.items():
        lines = []
        # grid as thin boxes in the plane through the origin, behind nothing (drawn in front with small depth)
        for i in range(-8, 9):
            v = i * 0.25
            bpy.ops.mesh.primitive_cube_add(size=1, location=((v, 0.9, 1.5) if axis == 'x' else (-0.9, v, 1.5)))
            c = bpy.context.object; c.scale = ((0.003 if i % 4 else 0.007), 0.001, 2.4) if axis == 'x' else (0.001, (0.004 if i % 4 else 0.009), 2.4); c.data.materials.append(gm); lines.append(c)
        for j in range(0, 13):
            v = j * 0.25
            bpy.ops.mesh.primitive_cube_add(size=1, location=((0, 0.9, v) if axis == 'x' else (-0.9, 0, v)))
            c = bpy.context.object; c.scale = (2.6, 0.001, (0.004 if j % 4 else 0.009)) if axis == 'x' else (0.001, 2.6, (0.004 if j % 4 else 0.009)); c.data.materials.append(gm); lines.append(c)
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3.6; cam.location = loc; cam.rotation_euler = [math.radians(a) for a in rot]
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = cam.rotation_euler; sun.data.energy = 3
        sc.render.filepath = os.path.join(work, f'measure_{view}.png'); bpy.ops.render.render(write_still=True)
        for c in lines: bpy.data.objects.remove(c)
        bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
