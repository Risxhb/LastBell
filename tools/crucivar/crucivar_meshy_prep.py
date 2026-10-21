# blender -b -P crucivar_meshy_prep.py -- <in.glb> <work dir> [tris]
# Normalises the Meshy Crucivar ("Forge Sentinel": feet at 0, 3.8 m tall, faces -Y, hammer on its right = -X),
# decimates it, saves its 2K textures next to the .blend and builds a glow mask for the molten texels (belly windows,
# eyes, chimney mouths, crucible metal, joint glows). Saves crucivar_prep.blend plus measuring renders (0.25 m grid).
import bpy, sys, os, math, numpy as np
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
src, work = args[0], args[1]; os.makedirs(work, exist_ok=True)
TARGET_TRIS = int(args[2]) if len(args) > 2 else 60000
HEIGHT = 3.8
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = ob.data.name = 'Crucivar'
# bake any import transform into the mesh
ob.data.transform(ob.matrix_world); ob.matrix_world.identity(); ob.parent = None
bpy.context.view_layer.objects.active = ob; ob.select_set(True)

# --- normalise: feet at z=0, centred on the torso, 3.8 m tall ---
co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
# centre x/y on the torso band (z 40-70 % of the height), not the whole bbox: the hammer and crucible aren't symmetric
zn = (co[:, 2] - lo[2]) / (hi[2] - lo[2]); band = co[(zn > 0.4) & (zn < 0.7)]
# torso = the central cluster: take the middle 50 % of x in the band
xs = np.sort(band[:, 0]); cx = (xs[len(xs) // 4] + xs[3 * len(xs) // 4]) / 2
ys = np.sort(band[:, 1]); cy = (ys[len(ys) // 4] + ys[3 * len(ys) // 4]) / 2
co = (co - np.array([cx, cy, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()
print('SCALE', k, 'CENTRE', cx, cy)

# --- merge coincident verts, decimate ---
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = min(1, TARGET_TRIS / tris); dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

# --- textures ---
mat = ob.data.materials[0]; mat.name = 'CrucivarMeshy'; nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
    return small
imgs = {n.image.name: n for n in nt.nodes if n.type == 'TEX_IMAGE'}
print('TEX_NODES', list(imgs))
base = save(imgs['Image_0'], 'crucivar_basecolor')
if 'Image_1' in imgs: save(imgs['Image_1'], 'crucivar_orm')
if 'Image_2' in imgs: save(imgs['Image_2'], 'crucivar_normal')

# --- glow mask: molten orange is brighter and redder than the brass highlights ---
S = base.size[0]
px = np.array(base.pixels[:], dtype=np.float32).reshape(S, S, 4)
r, g, b = px[..., 0], px[..., 1], px[..., 2]
molten = (r > 0.82) & (g > 0.25 * r) & (g < 0.72 * r) & (b < 0.32 * r)
level = molten.astype(np.float32)
acc = np.zeros_like(level)
for dy in (-1, 0, 1):
    for dx in (-1, 0, 1): acc += np.roll(np.roll(level, dy, 0), dx, 1)
m = np.clip(acc / 9 * 1.3, 0, 1)
lum = np.clip(px[..., :3].max(-1) * 1.1, 0, 1)
em = np.zeros_like(px); em[..., 0] = m * lum; em[..., 1] = m * lum * 0.45; em[..., 2] = m * lum * 0.12; em[..., 3] = 1
emi = bpy.data.images.new('crucivar_emissive', S, S); emi.pixels[:] = em.ravel()
emi.filepath_raw = os.path.join(work, 'crucivar_emissive.png'); emi.file_format = 'PNG'; emi.save()
print('GLOW_PIXELS', int(molten.sum()), 'of', S * S)
en = nt.nodes.new('ShaderNodeTexImage'); en.image = emi; en.location = (-600, -500)
nt.links.new(en.outputs['Color'], bsdf.inputs['Emission Color']); bsdf.inputs['Emission Strength'].default_value = 1.0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'crucivar_prep.blend'))

# --- measuring renders: orthographic front / side / back / top with a 0.25 m grid (0.5 m lines brighter) ---
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = sc.render.resolution_y = 1000
w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.92, 0.9, 0.85, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.3
sun = bpy.data.objects.new('S', bpy.data.lights.new('S', 'SUN')); sc.collection.objects.link(sun); sun.data.energy = 2.5; sun.rotation_euler = (math.radians(45), 0, math.radians(-20))
gm = bpy.data.materials.new('Grid'); gm.use_nodes = True; gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0, 0.4, 1, 1)
gm.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (0, 0.4, 1, 1); gm.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 3
import bmesh
def grid(name, plane):
    bm = bmesh.new()
    for i in range(-12, 13):
        v = i * 0.25; t = 0.006 if i % 2 else 0.012
        for horiz in (True, False):
            g = bmesh.ops.create_cube(bm, size=1.0)
            if plane == 'xz':
                sz = (6.0, t, t) if horiz else (t, t, 6.0); loc = (0, 3.0, v + 2.0) if horiz else (v, 3.0, 2.0)
            elif plane == 'yz':
                sz = (t, 6.0, t) if horiz else (t, t, 6.0); loc = (-3.0, 0, v + 2.0) if horiz else (-3.0, v, 2.0)
            else:
                sz = (6.0, t, t) if horiz else (t, 6.0, t); loc = (0, v, -0.05) if horiz else (v, 0, -0.05)
            bmesh.ops.transform(bm, matrix=__import__('mathutils').Matrix.LocRotScale(Vector(loc), None, Vector(sz)), verts=g['verts'])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); o.data.materials.append(gm); return o
cam = bpy.data.objects.new('C', bpy.data.cameras.new('C')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5.0
for name, loc, plane in (('front', (0, -10, 1.9), 'xz'), ('side', (10, 0, 1.9), 'yz'), ('back', (0, 10, 1.9), 'xz'), ('top', (0, 0, 12), 'xy')):
    gobj = grid('grid_' + name, plane)
    if name == 'side': gobj.location = (0, 0, 0)
    cam.location = loc
    target = Vector((0, 0, 1.9)) if name != 'top' else Vector((0, 0, 0))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(work, 'measure_' + name + '.png'); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(gobj)
print('DONE')
