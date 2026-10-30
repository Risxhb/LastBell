# blender -b -P miniboss_prep.py -- <in.glb> <work dir> <Name> <height m> [--tris N] [--yaw DEG] [--band LO HI] [--measure]
# Shared prep for the Meshy miniboss models (generalised from dial_warden_prep.py). Brings one raw Meshy GLB into the
# rig scripts' frame: feet (or the lowest point) at z = 0, centred in x/y on a horizontal band of the body, scaled to
# <height> from the lowest to the highest vertex, facing -Y. Merges duplicate verts, decimates to --tris (default
# 50,000), saves its textures as <name>_basecolor/_orm/_normal.png and the scene as <name>_prep.blend.
#   --yaw DEG     turn the mesh about Z first (Meshy usually faces -Y already; use this when a model faces another way)
#   --band LO HI  the fraction of the height used to centre x/y (default 0.4 0.7: the torso, not asymmetric weapons)
#   --measure     also renders front / side / top / three-quarter views with a 0.25 m grid (to place joints), and
#                 prints the loose parts (Meshy meshes are many rigid pieces, which the rigs pin whole to bones).
import bpy, sys, os, math, numpy as np
from mathutils import Matrix
args = sys.argv[sys.argv.index('--') + 1:]
def opt(name, n=1, default=None):
    if name not in args: return default
    i = args.index(name); v = args[i + 1:i + 1 + n]; del args[i:i + 1 + n]
    return v if n > 1 else v[0]
measure = '--measure' in args
if measure: args.remove('--measure')
TARGET_TRIS = int(opt('--tris', 1, 50000)); YAW = float(opt('--yaw', 1, 0)); band = [float(b) for b in opt('--band', 2, ['0.4', '0.7'])]
src, work, NAME, HEIGHT = args[0], args[1], args[2], float(args[3])
slug = ''.join('_' + c.lower() if c.isupper() and i else c.lower() for i, c in enumerate(NAME))
os.makedirs(work, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for m in meshes: m.data.transform(m.matrix_world); m.matrix_world.identity(); m.parent = None
if len(meshes) > 1:
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes: m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]; bpy.ops.object.join()
ob = meshes[0]; ob.name = ob.data.name = NAME
for o in list(bpy.context.scene.objects):
    if o != ob: bpy.data.objects.remove(o)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
if YAW: ob.data.transform(Matrix.Rotation(math.radians(YAW), 4, 'Z'))

co = np.empty(len(ob.data.vertices) * 3); ob.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
lo, hi = co.min(0), co.max(0); k = HEIGHT / (hi[2] - lo[2])
zn = (co[:, 2] - lo[2]) / (hi[2] - lo[2]); sel = co[(zn > band[0]) & (zn < band[1])]
if len(sel) < 100: sel = co
xs = np.sort(sel[:, 0]); ys = np.sort(sel[:, 1])
cx = (xs[len(xs) // 4] + xs[3 * len(xs) // 4]) / 2; cy = (ys[len(ys) // 4] + ys[3 * len(ys) // 4]) / 2
co = (co - np.array([cx, cy, lo[2]])) * k
ob.data.vertices.foreach_set('co', co.ravel()); ob.data.update()
print('SCALE', k, 'BBOX', np.round(co.min(0), 3), np.round(co.max(0), 3))

bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
ob.data.calc_loop_triangles(); tris = len(ob.data.loop_triangles)
if tris > TARGET_TRIS:
    dec = ob.modifiers.new('dec', 'DECIMATE'); dec.ratio = TARGET_TRIS / tris; dec.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec')
ob.data.calc_loop_triangles(); print('TRIS', tris, '->', len(ob.data.loop_triangles))
bpy.ops.object.shade_smooth()

mat = ob.data.materials[0]; mat.name = NAME + 'Meshy'; nt = mat.node_tree
def save(node, name):
    img = node.image; path = os.path.join(work, name + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    small = bpy.data.images.load(path); small.colorspace_settings.name = img.colorspace_settings.name
    node.image = small; bpy.data.images.remove(img); small.name = name
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
def feeding(sock):
    return sock.links[0].from_node if sock.is_linked else None
def find_img(node, depth=0):
    if node is None or depth > 4: return None
    if node.type == 'TEX_IMAGE': return node
    for s in node.inputs:
        if s.is_linked:
            r = find_img(s.links[0].from_node, depth + 1)
            if r: return r
    return None
done = set()
for sock, suffix in (('Base Color', 'basecolor'), ('Metallic', 'orm'), ('Roughness', 'orm'), ('Normal', 'normal')):
    n = find_img(feeding(bsdf.inputs[sock]))
    if n and n.name not in done and suffix not in [d.split(':')[1] for d in done if ':' in d]:
        save(n, f'{slug}_{suffix}'); done.add(n.name); done.add(n.name + ':' + suffix)
print('TEXTURES', sorted(d for d in done if ':' in d))

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
for p in np.argsort(-counts)[:40]:
    c = co[part == p]; print('PART', p, counts[p], 'min', np.round(c.min(0), 2), 'max', np.round(c.max(0), 2), 'ctr', np.round(c.mean(0), 2))

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, f'{slug}_prep.blend'))

if measure:
    sc = bpy.context.scene
    eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 1200
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.2
    gm = bpy.data.materials.new('grid'); gm.use_nodes = True; gb = gm.node_tree.nodes['Principled BSDF']
    gb.inputs['Base Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Color'].default_value = (1, 0, 0, 1); gb.inputs['Emission Strength'].default_value = 1
    lo, hi = co.min(0), co.max(0); ext = max(hi[0] - lo[0], hi[1] - lo[1], hi[2]) * 1.15; mid = hi[2] / 2
    n = int(math.ceil(ext / 0.25)) + 2
    def grid(plane):   # thin red bars every 0.25 m (thicker every metre) in the plane through the origin
        out = []
        for i in range(-n, n + 1):
            v = i * 0.25; w = 0.008 if i % 4 == 0 else 0.003
            for along in (0, 1):
                if plane == 'xz': loc, sca = ((v, 0, mid), (w, 0.001, ext)) if along == 0 else ((0, 0, v), (ext * 2, 0.001, w))
                elif plane == 'yz': loc, sca = ((0, v, mid), (0.001, w, ext)) if along == 0 else ((0, 0, v), (0.001, ext * 2, w))
                else: loc, sca = ((v, 0, 0), (w, ext * 2, 0.001)) if along == 0 else ((0, v, 0), (ext * 2, w, 0.001))
                if plane != 'xy' and along == 1 and (v < -0.01 or v > hi[2] + 0.3): continue
                bpy.ops.mesh.primitive_cube_add(size=1, location=loc); c = bpy.context.object; c.scale = sca
                c.data.materials.append(gm); c.visible_shadow = False; out.append(c)
        return out
    views = {'front': ((0, -30, mid), (90, 0, 0), 'xz', True), 'side': ((30, 0, mid), (90, 0, 90), 'yz', True),
             'top': ((0, 0, 30), (0, 0, 0), 'xy', True), 'persp': ((-5.5 * ext / 3, -7 * ext / 3, mid + 3 * ext / 3), (63, 0, -38), None, False)}
    for view, (loc, rot, plane, ortho) in views.items():
        lines = grid(plane) if plane else []
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
        if ortho: cam.data.type = 'ORTHO'; cam.data.ortho_scale = ext * 1.1
        cam.location = loc; cam.rotation_euler = [math.radians(a) for a in rot]
        if not ortho:
            d = cam.location - __import__('mathutils').Vector((0, 0, mid)); cam.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(40), 0, math.radians(-30)); sun.data.energy = 3
        sc.render.filepath = os.path.join(work, f'measure_{view}.png'); bpy.ops.render.render(write_still=True)
        for c in lines: bpy.data.objects.remove(c)
        bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
    print('MEASURED', work)
