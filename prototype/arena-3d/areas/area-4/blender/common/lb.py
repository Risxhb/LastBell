# Shared Blender modelling tools for the Area 4 assets (imported by each asset script; each asset is still authored
# part by part in its own script). Units are metres, Z up. Unity conventions on export: forward -Z, up Y, baked.
#   reset()                          empty scene
#   slots(names)                     preview materials, one per Unity material slot, in slot order
#   box / cyl / cone / sphere / ico / torus / prism   bmesh parts placed in world space, one material each
#   arc_bend(o, r_mid)               bends a straight part (x along the tangent, y outward) onto a circle around Z
#   lathe(profile, seg, gap_fn)      surface of revolution with the same UV layout the Unity bell paint expects
#   bevel(o, w, segs)                angle-limited bevel, applied
#   join(parts, name, order)         one object, faces keep their slot from `order`
#   cube_uv(o, size) / planar_uv(o)  trim-sheet UVs (2.4 m tile) or 0..1 top-down UVs
#   export(objs, path)               FBX for Unity      preview(objs, path, ...)  Eevee render for review
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

PREVIEW = {
    'stone': ((0.2, 0.2, 0.28), 0, 0.85), 'stonecap': ((0.26, 0.26, 0.34), 0, 0.8), 'stonedark': ((0.14, 0.13, 0.2), 0, 0.9),
    'deck': ((0.28, 0.29, 0.36), 0, 0.8), 'pedestal': ((0.12, 0.12, 0.17), 0, 0.7),
    'brass': ((0.66, 0.46, 0.2), 0.35, 0.45), 'polished': ((0.85, 0.62, 0.28), 0.45, 0.35), 'iron': ((0.15, 0.13, 0.22), 0.3, 0.6),
    'bronze': ((0.37, 0.29, 0.19), 0.4, 0.5), 'ivory': ((0.9, 0.86, 0.77), 0, 0.5), 'glass': ((1.0, 0.7, 0.4), 0, 0.2),
    'flame': ((1.0, 0.55, 0.2), 0, 0.5), 'window': ((0.05, 0.04, 0.07), 0, 0.8), 'dial': ((0.1, 0.13, 0.26), 0.2, 0.5),
    'banner': ((0.43, 0.1, 0.13), 0, 0.85), 'rock': ((0.16, 0.14, 0.22), 0, 0.95),
}

def slots(names):
    out = []
    for n in names:
        m = bpy.data.materials.get(n) or bpy.data.materials.new(n); m.use_nodes = True
        col, metal, rough = PREVIEW.get(n.lower(), ((0.5, 0.5, 0.5), 0, 0.8))
        b = m.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value = (*col, 1)
        b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
        if n.lower() in ('glass', 'flame'):
            b.inputs['Emission Color'].default_value = (*col, 1); b.inputs['Emission Strength'].default_value = 2.0
        out.append(m)
    return out

def _obj(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); o.data.materials.append(mat); return o

def _place(bm, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    for v in bm.verts: v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
    m = Matrix.Rotation(rot[2], 3, 'Z') @ Matrix.Rotation(rot[1], 3, 'Y') @ Matrix.Rotation(rot[0], 3, 'X')
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=m); bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(loc))

def box(name, size, loc, mat, rot=(0, 0, 0), top_scale=1.0, top_shift=(0, 0)):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        if v.co.z > 0: v.co.x = v.co.x * top_scale + top_shift[0] / max(size[0], 1e-6); v.co.y = v.co.y * top_scale + top_shift[1] / max(size[1], 1e-6)
    _place(bm, loc, rot, size); return _obj(name, bm, mat)

def cyl(name, r, h, sides, loc, mat, rot=(0, 0, 0), r_top=None):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=r, radius2=r if r_top is None else r_top, depth=h)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, h / 2))); _place(bm, loc, rot); return _obj(name, bm, mat)

def cone(name, r, h, sides, loc, mat, rot=(0, 0, 0)):
    return cyl(name, r, h, sides, loc, mat, rot, 0.0)

def sphere(name, r, loc, mat, segs=10, rings=6, scale=(1, 1, 1)):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=r); _place(bm, loc, (0, 0, 0), scale); return _obj(name, bm, mat)

def ico(name, r, loc, mat, subdiv=1, scale=(1, 1, 1), rot=(0, 0, 0), jitter=0.0, seed=0):
    import random; rnd = random.Random(seed)
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    for v in bm.verts: v.co *= 1 + rnd.uniform(-jitter, jitter)
    _place(bm, loc, rot, scale); return _obj(name, bm, mat)

def torus(name, R, r, loc, mat, rot=(0, 0, 0), seg=48, tube=8, arc=360.0, start=0.0, scale=(1, 1, 1)):
    bm = bmesh.new(); rings = []
    n = seg if arc >= 360 else seg + 1
    for i in range(n):
        a = math.radians(start + arc * i / seg); d = Vector((math.cos(a), math.sin(a), 0))
        rings.append([bm.verts.new(d * (R + r * math.cos(b)) + Vector((0, 0, r * math.sin(b)))) for b in (j / tube * 2 * math.pi for j in range(tube))])
    for i in range(seg):
        a, b = rings[i], rings[(i + 1) % n]
        for j in range(tube): bm.faces.new((a[j], b[j], b[(j + 1) % tube], a[(j + 1) % tube]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); _place(bm, loc, rot, scale); return _obj(name, bm, mat)

def prism(name, pts2d, depth, loc, mat, rot=(0, 0, 0)):
    """Extrudes a 2D outline (x, z) by `depth` along y, centred."""
    bm = bmesh.new(); f = [bm.verts.new((x, -depth / 2, z)) for x, z in pts2d]; b = [bm.verts.new((x, depth / 2, z)) for x, z in pts2d]
    bm.faces.new(f); bm.faces.new(list(reversed(b)))
    for i in range(len(f)): j = (i + 1) % len(f); bm.faces.new((f[i], f[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); _place(bm, loc, rot); return _obj(name, bm, mat)

def pointed_arch(name, span, spring, thick, depth, loc, mat, rot=(0, 0, 0), segs=6, keep=1.0):
    """Equilateral pointed arch ring as one solid: outer and inner arcs joined, in the x-z plane."""
    pts_o, pts_i = [], []
    for side in (-1, 1):
        c = Vector((-side * span / 2, spring)); arc = []
        for i in range(segs + 1):
            if i / segs > keep: break
            a = math.radians(180 - 60 * i / segs) if side < 0 else math.radians(60 * i / segs)
            arc.append((c.x + span * math.cos(a), c.y + span * math.sin(a)))
        if side > 0: arc.reverse()
        (pts_o.extend(arc) if side < 0 else pts_o.extend(arc))
    # build outline: outer path then inner path reversed (inner = span shrunk by thick)
    inner = []
    s2 = span - 2 * thick
    for side in (-1, 1):
        c = Vector((-side * s2 / 2, spring)); arc = []
        for i in range(segs + 1):
            if i / segs > keep: break
            a = math.radians(180 - 60 * i / segs) if side < 0 else math.radians(60 * i / segs)
            arc.append((c.x + s2 * math.cos(a), c.y + s2 * math.sin(a)))
        if side > 0: arc.reverse()
        inner.extend(arc)
    outline = pts_o + list(reversed(inner))
    # remove the duplicated apex points
    clean = []
    for p in outline:
        if not clean or (Vector(p) - Vector(clean[-1])).length > 1e-4: clean.append(p)
    if keep >= 1.0:
        return prism(name, clean, depth, loc, mat, rot)
    # broken arch: one half only (left), as a thick open band
    half_o = pts_o[:len(pts_o) // 2]; half_i = inner[:len(inner) // 2]
    return prism(name, half_o + list(reversed(half_i)), depth, loc, mat, rot)

def arc_bend(o, r_mid, x_scale=1.0):
    """x (tangent) -> angle around Z at radius r_mid, y -> extra radius outward. Keeps z."""
    for v in o.data.vertices:
        x, y, z = v.co; a = x * x_scale / r_mid; r = r_mid + y
        v.co = Vector((r * math.sin(a), r * math.cos(a), z))
    o.data.update()

def lathe(name, prof, seg, mat, gap_fn=None):
    """prof: [(radius, height)], phi from +Y toward +X (Blender) so phi = 0 faces the Unity camera side after export.
    uv.x = i/seg over the covered angle, uv.y = normalised profile arc length (same as the Unity BellBronze paint)."""
    acc = [0.0]
    for k in range(1, len(prof)): acc.append(acc[-1] + math.dist(prof[k], prof[k - 1]))
    acc = [a / acc[-1] for a in acc]
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); grid = []
    for i in range(seg + 1):
        row = []
        for j, (r, h) in enumerate(prof):
            g = gap_fn(j) if gap_fn else 0.0; ph = g + (2 * math.pi - 2 * g) * i / seg
            row.append(bm.verts.new((-math.sin(ph) * r, math.cos(ph) * r, h)))   # Blender (x, y, z) -> Unity (-x, z, y): phi 0 faces Unity +Z
        grid.append(row)
    for i in range(seg):
        for j in range(len(prof) - 1):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            for loop, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))): loop[uv].uv = (ii / seg, acc[jj])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, mat)

def bevel(o, w=0.025, segs=2, angle=35):
    m = o.modifiers.new('Bevel', 'BEVEL'); m.width = w; m.segments = segs; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(angle)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier='Bevel')

def join(parts, name, order):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join(); o = bpy.context.active_object
    o.name = o.data.name = name
    remap = {i: order.index(m) for i, m in enumerate(o.data.materials)}
    for poly in o.data.polygons: poly.material_index = remap[poly.material_index]
    while len(o.data.materials) < len(order): o.data.materials.append(None)
    for i, m in enumerate(order): o.data.materials[i] = m   # in place: clear() would reset every face to slot 0
    while len(o.data.materials) > len(order): o.data.materials.pop()
    # Unity numbers FBX submeshes by first use, so sort faces by slot to keep its order equal to `order`
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.sort_elements(type='MATERIAL', elements={'FACE'}); bpy.ops.object.mode_set(mode='OBJECT')
    return o

def cube_uv(o, size=2.4, keep_existing_uv=False):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    if not keep_existing_uv: bpy.ops.uv.cube_project(cube_size=size)
    bpy.ops.object.mode_set(mode='OBJECT')

def finish(o, merge=0.0005, flat=True):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=merge)
    bpy.ops.object.mode_set(mode='OBJECT')
    (bpy.ops.object.shade_flat() if flat else bpy.ops.object.shade_smooth())
    o.data.calc_loop_triangles(); print(o.name, 'tris', len(o.data.loop_triangles)); return o

def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
                             use_mesh_modifiers=True, add_leaf_bones=False)
    print('exported', path)

def preview(path, cam_loc, look, res=(1400, 1000), lens=50):
    sc = bpy.context.scene
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try: sc.render.engine = eng; break
        except TypeError: pass
    sc.render.resolution_x, sc.render.resolution_y = res
    if not sc.world:
        w = bpy.data.worlds.new('Void'); sc.world = w; w.use_nodes = True
        w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.16, 0.12, 0.3, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.6
        for kind, e, col, rot in (('key', 3.2, (1, 0.92, 0.85), (50, 0, -40)), ('rim', 1.5, (0.55, 0.6, 1.0), (60, 0, 150))):
            l = bpy.data.objects.new(kind, bpy.data.lights.new(kind, 'SUN')); l.data.energy = e; l.data.color = col
            l.rotation_euler = tuple(math.radians(a) for a in rot); sc.collection.objects.link(l)
    cam = sc.camera
    if not cam:
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = lens; cam.location = cam_loc; d = Vector(look) - Vector(cam_loc); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    os.makedirs(os.path.dirname(path), exist_ok=True); sc.render.filepath = path; bpy.ops.render.render(write_still=True); print('render', path)
