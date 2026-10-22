# Shared by crucivar_build.py (scripted model) and crucivar_meshy_rig.py (Meshy sculpt): materials, the armature
# builder and the hard-surface helpers. Loaded with exec() into the calling script, so it uses that script's globals
# (sc, parts). Axes: -Y forward, +X = Crucivar's LEFT (crucible), -X = its RIGHT (hammer), Z up, metres.
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, Euler, Quaternion

# ---------------- materials ----------------
def mat(name, color, metal=0.0, rough=0.5, emit=None, strength=1.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*color, 1); b.inputs['Metallic'].default_value = metal
    b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = strength
    return m

IRON = mat('Iron', (0.045, 0.040, 0.038), 0.75, 0.55)
IRON_DARK = mat('IronDark', (0.022, 0.020, 0.020), 0.6, 0.7)
BRASS = mat('Brass', (0.55, 0.36, 0.12), 1.0, 0.32)
BRASS_DARK = mat('BrassAged', (0.30, 0.19, 0.07), 1.0, 0.45)
OXIDE = mat('Oxide', (0.14, 0.27, 0.22), 0.35, 0.55)
CERAMIC = mat('Ceramic', (0.42, 0.36, 0.29), 0.0, 0.8)
SCORCH = mat('Scorched', (0.12, 0.09, 0.07), 0.0, 0.9)
MOLTEN = mat('Molten', (1.0, 0.35, 0.05), 0.0, 0.4, (1.0, 0.30, 0.03), 1.0)
EMBER = mat('Ember', (0.6, 0.12, 0.02), 0.0, 0.5, (0.9, 0.22, 0.03), 1.0)
AMBER = mat('AmberEye', (1.0, 0.7, 0.25), 0.0, 0.3, (1.0, 0.62, 0.18), 1.0)

# ---------------- armature ----------------
def make_armature(B):
    # B: {name: (head, tail, parent)}
    ad = bpy.data.armatures.new('CrucivarRig'); arm = bpy.data.objects.new('CrucivarRig', ad); sc.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
    for n, (h, t, p) in B.items():
        e = ad.edit_bones.new(n); e.head, e.tail = h, t
        if p: e.parent = ad.edit_bones[p]; e.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm, ad

# ---------------- geometry helpers ----------------
parts = []
def finish(bm, name, material, bone, pivot=(0, 0, 0), bevel=0.0, smooth=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = smooth
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob)
    ob.data.materials.append(material)
    pv = Vector(pivot)
    me.transform(Matrix.Translation(-pv)); ob.location = pv
    if bevel > 0:
        bv = ob.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 2; bv.limit_method = 'ANGLE'
        bv.angle_limit = math.radians(40); bv.harden_normals = False
    parts.append((ob, bone)); return ob

def xf(rot=(0, 0, 0), loc=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))

def box(name, size, loc, material, bone, rot=(0, 0, 0), pivot=None, bevel=0.03):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, size), verts=bm.verts)
    return finish(bm, name, material, bone, pivot or loc, bevel)

def cyl(name, r1, r2, depth, loc, material, bone, rot=(0, 0, 0), seg=24, pivot=None, bevel=0.02, smooth=True, scale=(1, 1, 1)):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, pivot or loc, bevel, smooth)

def sphere(name, r, loc, material, bone, scale=(1, 1, 1), seg=20, rings=12, rot=(0, 0, 0)):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, loc, 0, True)

def ring(name, r, thick, loc, material, bone, rot=(0, 0, 0), seg=40, scale=(1, 1, 1), height=None):
    # a flat band (short tube) around an axis: brass hoops on the barrel and the limbs
    h = height if height is not None else thick
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=seg, radius1=r + thick / 2, radius2=r + thick / 2, depth=h)
    bmesh.ops.create_cone(bm, cap_ends=False, segments=seg, radius1=r - thick / 2, radius2=r - thick / 2, depth=h)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.transform(bm, matrix=xf(rot, loc, scale), verts=bm.verts)
    return finish(bm, name, material, bone, loc, 0, True)

def rivets(name, centre, r, n, material, bone, rot=(0, 0, 0), size=0.035, scale=(1, 1, 1), arc=(0, 360)):
    bm = bmesh.new()
    M = xf(rot, centre, (1, 1, 1))
    for i in range(n):
        a = math.radians(arc[0] + (arc[1] - arc[0]) * (i + 0.5) / n)
        p = M @ Vector((math.cos(a) * r * scale[0], math.sin(a) * r * scale[1], 0))
        g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size)
        bmesh.ops.translate(bm, vec=p, verts=g['verts'])
    return finish(bm, name, material, bone, centre, 0, True)

def merge(name, objs, material=None):
    # join static pieces on one bone into one mesh (fewer nodes, same look)
    if not objs: return None
    for o in objs:
        for m in o.modifiers: pass
    ctx = bpy.context
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    ctx.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); j = ctx.view_layer.objects.active; j.name = name
    return j

def shell(name, r, a0, a1, z0, z1, thick, material, bone, sy=0.86, seg=8):
    # a curved plate on the barrel: angles in degrees from the front (-Y) toward +X
    bm = bmesh.new(); rows = []
    for rr in (r, r + thick):
        row = []
        for i in range(seg + 1):
            a = math.radians(a0 + (a1 - a0) * i / seg)
            for z in (z0, z1):
                row.append(bm.verts.new((math.sin(a) * rr, -math.cos(a) * rr * sy, z)))
        rows.append(row)
    def v(layer, i, top): return rows[layer][2 * i + (1 if top else 0)]
    for i in range(seg):
        bm.faces.new((v(1, i, 0), v(1, i + 1, 0), v(1, i + 1, 1), v(1, i, 1)))     # outer
        bm.faces.new((v(0, i, 1), v(0, i + 1, 1), v(0, i + 1, 0), v(0, i, 0)))     # inner
        bm.faces.new((v(0, i, 1), v(1, i, 1), v(1, i + 1, 1), v(0, i + 1, 1)))     # top
        bm.faces.new((v(0, i + 1, 0), v(1, i + 1, 0), v(1, i, 0), v(0, i, 0)))     # bottom
    bm.faces.new((v(0, 0, 0), v(1, 0, 0), v(1, 0, 1), v(0, 0, 1)))
    bm.faces.new((v(0, seg, 1), v(1, seg, 1), v(1, seg, 0), v(0, seg, 0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    am = math.radians((a0 + a1) / 2); zm = (z0 + z1) / 2
    for dz in (z1 - 0.07, z0 + 0.07):   # four rivets
        for da in (0.3, 0.7):
            aa = math.radians(a0 + (a1 - a0) * da); rr = r + thick + 0.01
            g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.028)
            bmesh.ops.translate(bm, vec=(math.sin(aa) * rr, -math.cos(aa) * rr * sy, dz), verts=g["verts"])
    return finish(bm, name, material, bone, pivot=(math.sin(am) * r, -math.cos(am) * r * sy, zm), bevel=0.012)

