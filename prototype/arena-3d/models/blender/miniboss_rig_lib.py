# Shared helpers for the miniboss rig scripts (<boss>_rig.py), generalised from dial_warden_rig.py.
# The Meshy miniboss meshes are many rigid loose parts, so a rig pins each part whole to one bone (no deforming
# weights), then keys clips by evaluating a pose function every frame at 30 fps.
#
# Pose dictionaries (one per frame):
#   'rot': {bone: (x, y, z)}   Euler degrees about the bone's head, in ARMATURE axes (X + tips a bone's top toward -Y,
#                              i.e. forward; Y + tilts it toward +X; Z + turns it toward his left)
#   'loc': {bone: (x, y, z)}   offset in armature axes (metres)
#   'aim': {bone: dir | (dir, twist)}   point the bone along an armature-space direction from wherever its head is
#                              after its parents are posed (bones aimed in the order the rig lists them)
# Axes everywhere: -Y forward (the model's face), +X = the model's left, Z up. Unity's glTF import turns -Y into +Z.
import bpy, os, math, numpy as np
from mathutils import Vector, Euler, Matrix

def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            return tuple(a + (b - a) * u for a, b in zip(v0, v1)) if isinstance(v0, tuple) else v0 + (v1 - v0) * u
    return pts[-1][1]
def snap(t, t0, rise=0.06, fall=0.25):
    if t < t0 - rise or t > t0 + fall: return 0.0
    return smooth((t - (t0 - rise)) / rise) if t < t0 else 1 - smooth((t - t0) / fall)
def ring(t, t0, amp, freq=1.6, damp=2.2):
    return 0.0 if t < t0 else amp * math.exp(-damp * (t - t0)) * math.sin(2 * math.pi * freq * (t - t0))
def add(*vs): return tuple(sum(c) for c in zip(*vs))
def scale(v, k): return tuple(c * k for c in v)
def nrm(v): return tuple(Vector(v).normalized())
def dirs(t, pts): return nrm(key(t, pts))
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
def mirror(p, cx=0.0): return (2 * cx - p[0], p[1], p[2])

class Rig:
    def __init__(self, body_name, bones, aim_order=(), no_deform=('root',)):
        self.sc = bpy.context.scene; self.sc.render.fps = 30
        self.body = bpy.data.objects[body_name]; self.B = bones; self.aim_order = tuple(aim_order)
        ad = bpy.data.armatures.new(body_name + 'Rig'); self.arm = arm = bpy.data.objects.new(body_name + 'Rig', ad)
        self.sc.collection.objects.link(arm)
        bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
        for n, (h, t, p) in bones.items():
            e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = n not in no_deform
            if p: e.parent = ad.edit_bones[p]; e.use_connect = False
        bpy.ops.object.mode_set(mode='OBJECT')
        self.no_deform = set(no_deform)
        self.pbs = arm.pose.bones
        for pb in self.pbs: pb.rotation_mode = 'QUATERNION'
        self.rest_dir = {n: (Vector(bones[n][1]) - Vector(bones[n][0])).normalized() for n in bones}
        self._survey()

    def _survey(self):
        me = self.body.data; nv = len(me.vertices)
        co = np.empty(nv * 3); me.vertices.foreach_get('co', co); self.co = co.reshape(-1, 3)
        ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
        par = np.arange(nv)
        def find(a):
            while par[a] != a: par[a] = par[par[a]]; a = par[a]
            return a
        for a, b in ed:
            ra, rb = find(a), find(b)
            if ra != rb: par[ra] = rb
        roots = np.array([find(i) for i in range(nv)]); _, self.part = np.unique(roots, return_inverse=True)
        self.nparts = self.part.max() + 1

    def assign(self, classify):
        # classify(c) -> bone name, or a per-vertex array of bone names (to split one part between bones)
        bone_of = np.empty(len(self.co), dtype=object); counts = {}
        for p in range(self.nparts):
            m = self.part == p; c = self.co[m]
            k = classify(c)
            bone_of[m] = k
            for name in (np.unique(k) if isinstance(k, np.ndarray) else [k]): counts[name] = counts.get(name, 0) + 1
        print('PART_BONES', dict(sorted(counts.items())))
        for n in self.B:
            if n not in self.no_deform: self.body.vertex_groups.new(name=n)
        for n in self.B:
            if n in self.no_deform: continue
            idx = [int(i) for i in np.nonzero(bone_of == n)[0]]
            if idx: self.body.vertex_groups[n].add(idx, 1.0, 'REPLACE')
        missing = [n for n in self.B if n not in self.no_deform and n not in counts]
        if missing: print('BONES_WITHOUT_PARTS', missing)
        self.body.parent = self.arm; self.body.matrix_parent_inverse = self.arm.matrix_world.inverted()
        am = self.body.modifiers.new('Armature', 'ARMATURE'); am.object = self.arm

    def heat_weights(self, pin=None, rigid=(), voxel=0.025, fallback='body', max_influence=4):
        # For bodies Meshy fused into one shell (as aurelion_rig.py): heat-weight a watertight voxel copy, transfer the
        # weights back, then pin the loose parts. pin(c) -> bone name (whole part to that bone) or None (keep heat
        # weights). Bones in `rigid` never pull on the shell; they only carry pinned parts.
        sc = self.sc; arm = self.arm; body = self.body; ad = arm.data
        bpy.ops.object.select_all(action='DESELECT')
        proxy = body.copy(); proxy.data = body.data.copy(); proxy.name = 'proxy'; sc.collection.objects.link(proxy)
        for m in list(proxy.modifiers): proxy.modifiers.remove(m)
        rm = proxy.modifiers.new('rm', 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = voxel
        bpy.context.view_layer.objects.active = proxy; bpy.ops.object.modifier_apply(modifier='rm')
        for c in rigid: ad.bones[c].use_deform = False
        proxy.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active = arm
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
        for c in rigid: ad.bones[c].use_deform = True
        print('PROXY_GROUPS', len(proxy.vertex_groups), 'proxy verts', len(proxy.data.vertices))
        bpy.ops.object.select_all(action='DESELECT')
        for n in self.B:
            if n not in self.no_deform and n not in body.vertex_groups: body.vertex_groups.new(name=n)
        dt = body.modifiers.new('dt', 'DATA_TRANSFER'); dt.object = proxy; dt.use_vert_data = True
        dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
        dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
        bpy.context.view_layer.objects.active = body; body.select_set(True)
        bpy.ops.object.modifier_apply(modifier='dt')
        bpy.data.objects.remove(proxy)
        names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
        me = body.data; nv = len(me.vertices); W = np.zeros((nv, len(names)), np.float32)
        for v in me.vertices:
            for g in v.groups: W[v.index, g.group] = g.weight
        counts = {}
        biggest = np.bincount(self.part).max()
        for p in range(self.nparts):
            m = self.part == p
            if pin is None or m.sum() == biggest: continue
            k = pin(self.co[m])
            if k is None: continue
            W[m] = 0; W[m, gi[k]] = 1; counts[k] = counts.get(k, 0) + 1
        print('PINNED_PARTS', counts)
        for c in rigid:
            j = gi[c]; W[W[:, j] < 0.999, j] = 0
        order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :max_influence], True, 1)
        W = np.where(keep, W, 0); s = W.sum(1, keepdims=True); W = np.where(s > 0, W / np.maximum(s, 1e-6), 0)
        W[s[:, 0] == 0, gi[fallback]] = 1
        print('UNWEIGHTED_FIXED', int((s[:, 0] == 0).sum()))
        for g in body.vertex_groups: g.remove(list(range(nv)))
        for j, n in enumerate(names):
            vg = body.vertex_groups[n]
            for i in np.nonzero(W[:, j] > 0)[0]: vg.add([int(i)], float(W[i, j]), 'REPLACE')
        body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
        for m in list(body.modifiers): body.modifiers.remove(m)
        am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm
        self.W, self.wnames = W, names

    def distance_weights(self, pin=None, rigid=(), power=6.0, max_influence=3, bias=None):
        # For thin or open bodies where heat weighting fails: each vertex follows the bone segments nearest to it,
        # weighted 1/d^power over its closest `max_influence` bones. Bones in `rigid` only carry pinned parts.
        # bias: {bone: metres} subtracted from that bone's distance (to grow its reach).
        body = self.body
        names = [n for n in self.B if n not in self.no_deform]
        for n in names:
            if n not in body.vertex_groups: body.vertex_groups.new(name=n)
        gi = {g.name: g.index for g in body.vertex_groups}
        co = self.co; D = np.full((len(co), len(names)), 1e3, np.float32)
        for j, n in enumerate(names):
            if n in rigid: continue
            h, t = np.array(self.B[n][0]), np.array(self.B[n][1]); d = t - h; L2 = max(d.dot(d), 1e-9)
            u = np.clip((co - h) @ d / L2, 0, 1); D[:, j] = np.linalg.norm(co - (h + u[:, None] * d), axis=1) - (bias or {}).get(n, 0.0)
        D = np.maximum(D, 0.01)
        W = 1.0 / D ** power
        order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :max_influence], True, 1)
        W = np.where(keep, W, 0); W /= W.sum(1, keepdims=True)
        counts = {}
        if pin is not None:
            for p in range(self.nparts):
                m = self.part == p; k = pin(self.co[m])
                if k is None: continue
                W[m] = 0; W[m, names.index(k)] = 1; counts[k] = counts.get(k, 0) + 1
        print('PINNED_PARTS', counts)
        for j, n in enumerate(names):
            vg = body.vertex_groups[n]; idx = np.nonzero(W[:, j] > 1e-4)[0]
            for i in idx: vg.add([int(i)], float(W[i, j]), 'REPLACE')
        body.parent = self.arm; body.matrix_parent_inverse = self.arm.matrix_world.inverted()
        am = body.modifiers.new('Armature', 'ARMATURE'); am.object = self.arm
        print('DISTANCE_WEIGHTS', len(names), 'bones')

    def override(self, mask, bone):
        # after heat_weights: give every vertex where mask(co) is true wholly to `bone` (props fused into the shell)
        m = mask(self.co); idx = [int(i) for i in np.nonzero(m)[0]]
        for g in self.body.vertex_groups: g.remove(idx)
        self.body.vertex_groups[bone].add(idx, 1.0, 'REPLACE')
        print('OVERRIDE', bone, len(idx))

    def cut_bridges(self, mask, bones):
        # delete the faces joining a masked prop to vertices whose main bone is in `bones` (Meshy welds props to limbs)
        import bmesh
        m = mask(self.co); me = self.body.data
        gi = {g.name: g.index for g in self.body.vertex_groups}; want = {gi[b] for b in bones}
        main = np.full(len(me.vertices), -1)
        for v in me.vertices:
            if v.groups: main[v.index] = max(v.groups, key=lambda g: g.weight).group
        bm = bmesh.new(); bm.from_mesh(me)
        kill = [f for f in bm.faces if any(m[v.index] for v in f.verts) and any(main[v.index] in want and not m[v.index] for v in f.verts)]
        bmesh.ops.delete(bm, geom=kill, context='FACES_ONLY'); bm.to_mesh(me); bm.free(); me.update()
        print('CUT_BRIDGES', len(kill))

    # ---------------- posing ----------------
    def aim(self, name, d, twist=0.0):
        bpy.context.view_layer.update()
        pb = self.pbs[name]; head = pb.head.copy(); d = Vector(d).normalized()
        M = Matrix.Rotation(math.radians(twist), 3, d) @ self.rest_dir[name].rotation_difference(d).to_matrix()
        pb.matrix = Matrix.Translation(head) @ (M @ pb.bone.matrix_local.to_3x3()).to_4x4()

    def apply(self, ch):
        for pb in self.pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
        for n, e in ch.get('rot', {}).items():
            M = self.pbs[n].bone.matrix_local.to_3x3(); self.pbs[n].rotation_quaternion = (M.inverted() @ R(*e) @ M).to_quaternion()
        for n, v in ch.get('loc', {}).items():
            M = self.pbs[n].bone.matrix_local.to_3x3(); self.pbs[n].location = M.inverted() @ Vector(v)
        done = set()
        for (up, lo), (target, pole) in ch.get('ik', {}).items():
            self.ik2(up, lo, target, pole); done.update((up, lo))
        aims = ch.get('aim', {})
        for n in self.aim_order:
            if n in aims and n not in done:
                v = aims[n]
                if isinstance(v[0], (tuple, list)): self.aim(n, v[0], v[1])
                else: self.aim(n, v)

    def ik2(self, up, lo, target, pole):
        # two-bone IK: put the end of `lo` on `target` (armature space), the joint bending toward `pole` (a direction)
        bpy.context.view_layer.update()
        head = self.pbs[up].head.copy(); T = Vector(target)
        L1 = (Vector(self.B[up][1]) - Vector(self.B[up][0])).length; L2 = (Vector(self.B[lo][1]) - Vector(self.B[lo][0])).length
        d = T - head; dist = min(max(d.length, abs(L1 - L2) + 1e-4), L1 + L2 - 1e-4); dn = d.normalized()
        a = (L1 * L1 - L2 * L2 + dist * dist) / (2 * dist); h = math.sqrt(max(0.0, L1 * L1 - a * a))
        pv = Vector(pole); pv = (pv - dn * pv.dot(dn)); pv = pv.normalized() if pv.length > 1e-6 else Vector((0, 0, 1))
        knee = head + dn * a + pv * h; end = head + dn * dist
        self.aim(up, knee - head); self.aim(lo, end - knee)

    def bake(self, name, seconds, fn):
        frames = round(seconds * 30)
        act = bpy.data.actions.new(name); act.use_fake_user = True
        self.arm.animation_data_create(); self.arm.animation_data.action = act
        for f in range(frames + 1):
            self.apply(fn(f / 30))
            for pb in self.pbs:
                pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
                pb.keyframe_insert('location', frame=f + 1, group=pb.name)
        act.frame_range = (1, frames + 1)
        return act

    # ---------------- checks and output ----------------
    def preview(self, work, shots, cam=(-7.5, -10.5, 6.5), target=(0, 0, 2.0), size=520, sheet='poses_sheet.png'):
        # one frame per (clip, seconds) from a three-quarter front view, assembled into one contact sheet
        sc = self.sc
        eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
        sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
        sc.render.resolution_x = sc.render.resolution_y = size
        sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
        sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.75, 0.78, 1)
        bpy.ops.mesh.primitive_plane_add(size=30); floor = bpy.context.object
        c = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(c); sc.camera = c
        c.location = cam; c.rotation_euler = (Vector(target) - Vector(cam)).to_track_quat('-Z', 'Y').to_euler()
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
        sun.rotation_euler = (math.radians(35), 0, math.radians(-30)); sun.data.energy = 3.5
        files = []
        for name, tt in shots:
            self.arm.animation_data.action = bpy.data.actions[name]; sc.frame_set(1 + round(tt * 30))
            f = os.path.join(work, f'pose_{name}_{tt:.2f}.png'); sc.render.filepath = f; bpy.ops.render.render(write_still=True); files.append((name, f))
        for o in (floor, c, sun): bpy.data.objects.remove(o)
        # contact sheet (5 per row) with Blender's own image API, so no PIL is needed inside Blender
        cols = 5; rows = (len(files) + cols - 1) // cols
        out = np.zeros((rows * size, cols * size, 4), np.float32)
        for i, (_, f) in enumerate(files):
            im = bpy.data.images.load(f); px = np.array(im.pixels[:], np.float32).reshape(size, size, 4)
            r, cc = divmod(i, cols); out[(rows - 1 - r) * size:(rows - r) * size, cc * size:(cc + 1) * size] = px
            bpy.data.images.remove(im)
        img = bpy.data.images.new('sheet', cols * size, rows * size, alpha=True); img.pixels = out.ravel()
        img.filepath_raw = os.path.join(work, sheet); img.file_format = 'PNG'; img.save()
        print('PREVIEW', os.path.join(work, sheet), [n for n, _ in files])

    def export(self, work, slug, out_glb, first='Idle'):
        self.arm.animation_data.action = bpy.data.actions[first]
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, slug + '_rig.blend'))
        bpy.ops.object.select_all(action='DESELECT'); self.arm.select_set(True); self.body.select_set(True)
        bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                                  export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
        print('CLIPS', [a.name for a in bpy.data.actions])
        print('EXPORTED', out_glb, os.path.getsize(out_glb))

def part_map(rig, work, views=(('front', (0, -30, 2), (90, 0, 0)), ('side', (30, 0, 2), (90, 0, 90))), ortho=6.0, mid=2.0, size=900):
    # renders the rest pose with every part coloured by its bone (to check the assignment by eye)
    import colorsys
    sc = rig.sc; body = rig.body
    names = [g.name for g in body.vertex_groups]
    mats = []
    for i, n in enumerate(names):
        m = bpy.data.materials.new('pm_' + n); m.use_nodes = True
        col = colorsys.hsv_to_rgb((i * 0.61803) % 1, 0.8, 0.95)
        m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*col, 1); mats.append(m)
    me = body.data; old = list(me.materials); me.materials.clear()
    for m in mats: me.materials.append(m)
    gi = {g.index: k for k, g in enumerate(body.vertex_groups)}
    vg = np.zeros(len(me.vertices), np.int64)
    for v in me.vertices:
        if v.groups: vg[v.index] = gi[max(v.groups, key=lambda g: g.weight).group]
    for poly in me.polygons: poly.material_index = int(vg[poly.vertices[0]])
    eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = size
    for view, loc, rot in views:
        c = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(c); sc.camera = c
        c.data.type = 'ORTHO'; c.data.ortho_scale = ortho; c.location = (loc[0], loc[1], mid); c.rotation_euler = [math.radians(a) for a in rot]
        sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = c.rotation_euler; sun.data.energy = 3
        sc.render.filepath = os.path.join(work, f'partmap_{view}.png'); bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(c); bpy.data.objects.remove(sun)
    me.materials.clear()
    for m in old: me.materials.append(m)
    for poly in me.polygons: poly.material_index = 0
    print('PARTMAP', names)
