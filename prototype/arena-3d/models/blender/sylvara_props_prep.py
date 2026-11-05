# Sylvara v3 attack props from Chzy18's Meshy runs (1 Nov; concept sheets in deliveries/SYLVARA-ATTACK-PROPS-2026-11-01):
# scale each to its written size, set its pivot, cut it down for runtime and halve the texture, then export to Unity.
#   blender -b -P sylvara_props_prep.py -- <meshy upload dir> <unity SylvaraProps dir> [render <dir>]
# Sizes are the handoff's (written sizes win over the sheets). The glb files are gitignored art, not committed.
import bpy, sys, os, glob, math
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = args[0], args[1]
RENDER = args[3] if len(args) > 3 and args[2] == 'render' else None
os.makedirs(OUT, exist_ok=True)

# name: (Meshy file prefix, size axis, size in m, pivot, target faces, texture px)
#   pivot 'base' = centred, resting on z = 0 (floor props); 'centre' = centred on the hit point (the flying seed)
PROPS = {
    'ChoirBud':   ('Meshy_AI_Blossom_Bell_Lantern_', 'z', 1.70, 'base', 6000, 1024),   # 0.9 m wide, 1.7 m tall
    'StormSeed':  ('Meshy_AI_Gilded_Lotus_Orb_', 'xy', 1.10, 'centre', 2400, 512),     # 1.1 m across, inside the 0.6 m hit radius
    'SeedPod':    ('Meshy_AI_Verdant_Reliquary_', 'z', 1.10, 'base', 3000, 512),       # 1.1 m tall
    'RootModule': ('Meshy_AI_Elderwood_Relic_', 'z', 1.10, 'base', 2500, 512),         # one root piece; Unity rings them inside 3.5 m
}

def clear():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for x in list(coll):
            if x.users == 0: coll.remove(x)

def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return Vector(min(p[i] for p in pts) for i in range(3)), Vector(max(p[i] for p in pts) for i in range(3))

for name, (prefix, axis, size, pivot, faces, tex) in PROPS.items():
    clear()
    src = sorted(glob.glob(os.path.join(SRC, prefix + '*_texture.glb')))
    if not src: print('MISSING', name, prefix); continue
    bpy.ops.import_scene.gltf(filepath=src[-1])
    ms = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects:   # drop Meshy's empty parents: keep the mesh with its world transform
        if o.type == 'MESH' and o.parent:
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms: o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1: bpy.ops.object.join(); ms = [bpy.context.view_layer.objects.active]
    o = ms[0]; o.name = name; o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lo, hi = bounds([o]); ext = hi - lo
    cur = ext.z if axis == 'z' else max(ext.x, ext.y)
    s = size / cur
    c = (lo + hi) / 2
    shift = Vector((-c.x, -c.y, -lo.z if pivot == 'base' else -c.z))
    o.data.transform(__import__('mathutils').Matrix.Translation(shift))
    o.scale = (s, s, s); bpy.ops.object.transform_apply(scale=True)
    before = len(o.data.polygons)
    # Meshy splits vertices along UV seams; weld them first, or the collapse tears the surface open at every seam
    import bmesh
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005); bm.to_mesh(o.data); bm.free()
    if before > faces:
        d = o.modifiers.new('Decimate', 'DECIMATE'); d.ratio = faces / before
        bpy.ops.object.modifier_apply(modifier=d.name)
    for img in bpy.data.images:
        if img.size[0] > tex: img.scale(tex, tex)
    for p in o.data.polygons: p.use_smooth = True
    lo, hi = bounds([o]); ext = hi - lo
    print('PROP', name, 'from', os.path.basename(src[-1]), 'faces', before, '->', len(o.data.polygons),
          'size', tuple(round(v, 3) for v in ext), 'radius', round(max(Vector((v.x, v.y)).length for v in [o.matrix_world @ w.co for w in o.data.vertices]), 3))
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_image_format='JPEG', export_jpeg_quality=88)
    print('EXPORTED', path, os.path.getsize(path))
    if RENDER:
        sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x = sc.render.resolution_y = 600
        sc.view_settings.view_transform = 'Standard'
        if not sc.world: sc.world = bpy.data.worlds.new('W')
        sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.55, 0.55, 1)
        cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.0
        cam.location = (2.2, -3.2, 1.6 if pivot == 'base' else 0.6); look = Vector((0, 0, 0.85 if pivot == 'base' else 0))
        cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler(); sc.camera = cam
        sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sun.data.energy = 3.5; sun.rotation_euler = (0.8, 0.2, 0.7); sc.collection.objects.link(sun)
        sc.render.filepath = os.path.join(RENDER, name + '.png'); bpy.ops.render.render(write_still=True)
