# Renders for rivet_cantor_props.py: turnaround sheets and gameplay-camera mock frames (EEVEE).
# The mock frames copy the game's cameras: gameplay = player + (0, 13 up, 9.7 back), 44 deg vertical FOV
# (RunDirector.FollowOffset); close = 4.5-6.5 m from the subject, 2.4 m up, 40 deg (the autotest's close camera).
# The floor, lava, telegraph discs, lanes and players are stand-ins drawn to match the 9 Nov in-game frames
# (build-evidence/miniboss-models-20261109/models1/rivet/), not the game's own shaders.
import bpy, math, os
from mathutils import Vector, Matrix

def mat(name, base, metal=0.0, rough=0.6, emit=None, estr=0.0, alpha=1.0):
    name = 'mock_' + name   # never collide with the props' own materials ('slate', 'brass'...)
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*base, 1); b.inputs['Metallic'].default_value = metal; b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*emit, 1); b.inputs['Emission Strength'].default_value = estr
    if alpha < 1:
        b.inputs['Alpha'].default_value = alpha; m.surface_render_method = 'BLENDED'
    return m

def new_scene(name, world=(0.02, 0.016, 0.012), strength=1.0):
    s = bpy.data.scenes.new(name)
    s.render.engine = 'BLENDER_EEVEE'
    s.eevee.taa_render_samples = 32
    s.render.resolution_x, s.render.resolution_y = 1280, 720
    s.view_settings.view_transform = 'AgX'; s.view_settings.look = 'AgX - Medium High Contrast'
    w = bpy.data.worlds.new(name + 'W'); w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (*world, 1); bg.inputs[1].default_value = strength
    s.world = w
    return s

def link(s, ob): s.collection.objects.link(ob); return ob

def inst(s, src, loc=(0, 0, 0), rot=None, scale=1.0, mtx=None):
    o = src.copy(); o.data = src.data   # shared mesh
    link(s, o)
    if mtx is not None: o.matrix_world = mtx
    else:
        o.location = loc
        if rot: o.rotation_euler = rot
        o.scale = (scale, scale, scale)
    return o

def prim(s, kind, name, loc, size, material, rot=(0, 0, 0)):
    import bmesh
    bm = bmesh.new()
    if kind == 'disc': bmesh.ops.create_circle(bm, cap_ends=True, segments=64, radius=1.0)
    elif kind == 'cyl': bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=1.0, radius2=1.0, depth=1.0)
    elif kind == 'cube': bmesh.ops.create_cube(bm, size=1.0)
    elif kind == 'capsule':
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.5)
        for v in bm.verts: v.co.z = v.co.z + (0.4 if v.co.z > 0 else -0.4)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); link(s, o)
    o.location = loc; o.scale = size; o.rotation_euler = rot
    me.materials.append(material)
    for p in me.polygons: p.use_smooth = True
    return o

def ring(s, name, loc, r_in, r_out, material, z=0.0):
    import bmesh
    bm = bmesh.new(); seg = 96; outer = []; inner = []
    for k in range(seg):
        a = 2 * math.pi * k / seg
        outer.append(bm.verts.new((math.cos(a) * r_out, math.sin(a) * r_out, 0)))
        inner.append(bm.verts.new((math.cos(a) * r_in, math.sin(a) * r_in, 0)))
    for k in range(seg):
        bm.faces.new((inner[k], inner[(k + 1) % seg], outer[(k + 1) % seg], outer[k]))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); link(s, o); o.location = (loc[0], loc[1], z); me.materials.append(material); return o

def camera(s, pos, look, fov=44, ortho=None):
    cd = bpy.data.cameras.new('cam'); c = bpy.data.objects.new('cam', cd); link(s, c)
    if ortho: cd.type = 'ORTHO'; cd.ortho_scale = ortho
    else:
        cd.sensor_fit = 'VERTICAL'; cd.angle_y = math.radians(fov)
    c.location = pos; c.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    s.camera = c; return c

def light(s, kind, loc, energy, color=(1, 1, 1), rot=(0, 0, 0), size=1.0):
    ld = bpy.data.lights.new('l', kind); ld.energy = energy; ld.color = color
    if kind == 'SUN': ld.angle = math.radians(8)
    if kind == 'AREA': ld.size = size
    o = bpy.data.objects.new('l', ld); link(s, o); o.location = loc; o.rotation_euler = rot; return o

def render(s, path):
    s.render.filepath = path
    bpy.ops.render.render(write_still=True, scene=s.name)
    print('RENDER', path)

# ---------------------------------------------------------------- the chain (same rule as RivetCantorProps.cs)
LINK_PITCH = 0.32
def chain_sag(dist, safe=7.0):
    return min(1.0, 0.06 + 0.28 * max(0.0, safe - dist))

def chain_points(a, b, sag):
    a, b = Vector(a), Vector(b)
    n = 48; pts = []
    for i in range(n + 1):
        t = i / n; pts.append(a.lerp(b, t) - Vector((0, 0, 1)) * sag * 4 * t * (1 - t))
    # resample at LINK_PITCH by arc length
    out = []; acc = 0.0; nxt = LINK_PITCH / 2
    for i in range(n):
        p, q = pts[i], pts[i + 1]; seg = (q - p).length
        while acc + seg >= nxt:
            f = (nxt - acc) / seg; out.append((p.lerp(q, f), (q - p).normalized())); nxt += LINK_PITCH
        acc += seg
    return out

def lay_chain(s, link_src, a, b, safe=7.0):
    dist = (Vector((a[0], a[1], 0)) - Vector((b[0], b[1], 0))).length
    sag = chain_sag(dist, safe)
    objs = []
    for i, (p, t) in enumerate(chain_points(a, b, sag)):
        y = t; side = y.cross(Vector((0, 0, 1)));
        if side.length < 1e-3: side = Vector((1, 0, 0))
        x = side.normalized(); z = x.cross(y).normalized()
        m = Matrix((x, y, z)).transposed().to_4x4()
        roll = Matrix.Rotation(math.pi / 2 if i % 2 else 0.0, 4, 'Y')
        objs.append(inst(s, link_src, mtx=Matrix.Translation(p) @ m @ roll))
    return objs

# ---------------------------------------------------------------- turnarounds
def turnarounds(assets, out, tex):
    s = new_scene('turn', world=(0.18, 0.18, 0.19), strength=1.0)
    floor = prim(s, 'disc', 'floor', (0, 0, -0.001), (30, 30, 1), mat('tfloor', (0.30, 0.30, 0.31), rough=0.8))
    light(s, 'SUN', (0, 0, 10), 3.0, (1.0, 0.95, 0.88), rot=(math.radians(40), 0, math.radians(30)))
    light(s, 'AREA', (-3, -4, 4), 300, (0.9, 0.9, 1.0), rot=(math.radians(55), 0, math.radians(-35)), size=4)
    head, plate = assets['RivetHead']; link_o = assets['ChainLink'][0]; sh = assets['ChainShackle'][0]
    # row 1: the rivet at three moments (punch top, rest, sunk = plate only) from the front, plus a top view
    xs = [-3.6, -1.8, 0.0]
    for i, (x, hz) in enumerate(zip(xs, (0.25, 0.0, -0.3))):
        inst(s, plate, (x, 2.2, -0.03 if i == 2 else 0), scale=0.7 if i == 2 else 1.0); inst(s, head, (x, 2.2, hz))
    inst(s, plate, (2.0, 2.2, 0), rot=(0, 0, 0.3)); inst(s, head, (2.0, 2.2, 0), rot=(0, 0, 0.3))
    # row 2: one link at four angles, and a short hanging chain
    for i, ang in enumerate((0, 45, 90, 135)):
        inst(s, link_o, (-3.6 + i * 0.75, 0.0, 0.5), rot=(math.radians(ang), 0, math.radians(20)), scale=2.0)
    lay_chain(s, link_o, (0.4, 0.0, 1.0), (3.6, 0.0, 1.0), safe=4.2)
    # row 3: the shackle at four angles, then the shackle with a chain hanging off it
    for i, ang in enumerate((0, 50, 90, 200)):
        inst(s, sh, (-3.6 + i * 1.0, -2.2, 0.9), rot=(0, 0, math.radians(ang)), scale=1.6)
    inst(s, sh, (1.0, -2.2, 1.2))
    lay_chain(s, link_o, (1.0, -2.2, 1.2), (3.6, -2.2, 1.0), safe=3.4)
    camera(s, (0.0, -9.5, 7.0), (0.0, 0.0, 0.4), ortho=9.6)
    s.render.resolution_x, s.render.resolution_y = 1600, 1200
    render(s, os.path.join(out, 'turnaround_sheet.png'))
    # close detail: rivet and shackle at real size from a 3/4 view
    camera(s, (-3.3, -0.6, 2.2), (-1.8, 2.2, 0.15), fov=32)
    s.render.resolution_x, s.render.resolution_y = 1280, 720
    render(s, os.path.join(out, 'turnaround_rivet_close.png'))
    camera(s, (-1.6, -4.4, 1.5), (-1.5, -2.2, 0.7), fov=32)
    render(s, os.path.join(out, 'turnaround_shackle_close.png'))

# ---------------------------------------------------------------- the arena mock
def arena(s, boss_glb, boss_yaw=0.0):
    prim(s, 'disc', 'floor', (0, 0, 0), (12.6, 12.6, 1), mat('slate', (0.055, 0.057, 0.062), 0.2, 0.62))
    brass_line = mat('inlay', (0.55, 0.40, 0.18), 1.0, 0.35)
    for r in (2.0, 2.15, 6.8, 12.3, 12.6):
        ring(s, f'in{r}', (0, 0), r - 0.03, r + 0.03, brass_line, z=0.004)
    for ang in (0, 90):
        prim(s, 'cube', f'line{ang}', (0, 0, 0.004), (24.4, 0.05, 0.004), brass_line, rot=(0, 0, math.radians(ang)))
    ring(s, 'lava', (0, 0), 12.9, 18.0, mat('lava', (0.9, 0.3, 0.05), 0, 0.5, emit=(1.0, 0.38, 0.06), estr=1.4), z=-0.3)
    ring(s, 'rim', (0, 0), 12.6, 12.9, mat('rimstone', (0.08, 0.07, 0.065), 0, 0.7), z=0.0)
    light(s, 'SUN', (0, 0, 20), 1.6, (1.0, 0.86, 0.70), rot=(math.radians(28), 0, math.radians(-20)))
    for k in range(6):
        a = 2 * math.pi * k / 6
        light(s, 'POINT', (math.cos(a) * 14.5, math.sin(a) * 14.5, 1.5), 900, (1.0, 0.45, 0.12))
    if boss_glb and os.path.exists(boss_glb):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=boss_glb)
        new = [o for o in bpy.data.objects if o not in before]
        for o in new:
            for c in list(o.users_collection): c.objects.unlink(o)
            # only the rig and its mesh (the importer also brings the bone-display icosphere)
            if o.type == 'ARMATURE' or (o.parent and o.parent.type == 'ARMATURE'): link(s, o)
        roots = [o for o in new if o.parent is None]
        for r in roots: r.rotation_euler.z += boss_yaw
        arm = next((o for o in new if o.type == 'ARMATURE'), None)
        if arm and arm.animation_data:
            idle = bpy.data.actions.get('Idle')
            if idle: arm.animation_data.action = idle
        return arm
    return None

def player(s, name, at, tint=(0.18, 0.22, 0.30)):
    prim(s, 'capsule', name, (at[0], at[1], 0.9), (0.46, 0.46, 1.0), mat('cloth' + name, tint, 0, 0.7))
    prim(s, 'capsule', name + 'h', (at[0], at[1], 1.68), (0.32, 0.32, 0.28), mat('skin', (0.6, 0.45, 0.35), 0, 0.6))

TELE = None
def rivet_tele(s, at):
    global TELE
    fill = mat('tele_fill', (1.0, 0.82, 0.76), 0, 0.8, emit=(1.0, 0.80, 0.74), estr=1.2, alpha=0.7)
    edge = mat('tele_edge', (0.75, 0.30, 0.22), 0, 0.8, emit=(0.8, 0.3, 0.2), estr=1.2)
    prim(s, 'disc', 'tele', (at[0], at[1], 0.012), (0.9, 0.9, 1), fill)
    ring(s, 'teler', at, 0.72, 0.9, edge, z=0.014)
    ring(s, 'telei', at, 0.30, 0.42, edge, z=0.015)

def lane(s, a, b, w=1.0):
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0)); d = b - a
    prim(s, 'cube', 'lane', ((a + b) / 2).to_tuple()[:2] + (0.016,), (d.length, w, 0.01),
         mat('lane', (0.85, 0.35, 0.25), 0, 0.8, emit=(0.9, 0.35, 0.22), estr=1.0, alpha=0.55), rot=(0, 0, math.atan2(d.y, d.x)))

def tether_circle(s, at):
    prim(s, 'disc', 'tc', (at[0], at[1], 0.008), (7.0, 7.0, 1), mat('tc', (0.45, 0.10, 0.07), 0, 0.8, emit=(0.5, 0.1, 0.06), estr=0.8, alpha=0.45))
    ring(s, 'tcr', at, 6.88, 7.0, mat('tcr', (1.0, 0.55, 0.25), 0, 0.5, emit=(1.0, 0.55, 0.25), estr=3.0), z=0.01)

def game_cam(s, at):
    p = Vector((at[0], at[1], 0)); camera(s, p + Vector((0, -9.7, 13.0)), p + Vector((0, 0, 1.0)), fov=44)

def rivet_scene(assets, out, boss, tex):
    s = new_scene('rivet')
    arena(s, boss)
    head, plate = assets['RivetHead']
    you = (0.8, -5.2); player(s, 'you', you); player(s, 'ally', (-3.5, 2.5), (0.30, 0.20, 0.18))
    # the first X (aimed at the nearest player): rivets at 2, 4 have popped; 6 is popping now; 8, 10 pending.
    aim = math.atan2(you[1], you[0])
    for l in range(4):
        d = Vector((math.cos(aim + l * math.pi / 2), math.sin(aim + l * math.pi / 2), 0))
        for i, r in enumerate((2, 4, 6, 8, 10)):
            p = d * r
            if i < 2:   # popped earlier: sinking / gone
                if i == 1: inst(s, plate, p.to_tuple()); inst(s, head, (p.x, p.y, -0.18))
            elif i == 2: inst(s, plate, p.to_tuple()); inst(s, head, (p.x, p.y, 0.2))   # the punch
            else: rivet_tele(s, p)
    # the counter X (45 deg): pending discs from the edge inward
    for l in range(4):
        a2 = aim + math.pi / 4 + l * math.pi / 2; d = Vector((math.cos(a2), math.sin(a2), 0))
        for r in (4, 6, 8, 10):
            rivet_tele(s, d * r)
    game_cam(s, you)
    render(s, os.path.join(out, 'mock_rivet_game.png'))
    d = Vector((math.cos(aim), math.sin(aim), 0)) * 6
    side = Vector((-d.y, d.x, 0)).normalized()
    camera(s, (d + side * 3.4 - d.normalized() * 2.6 + Vector((0, 0, 2.4))).to_tuple(), (d.x, d.y, 0.25), fov=40)
    render(s, os.path.join(out, 'mock_rivet_close.png'))

def chain_scene(assets, out, boss, tex):
    s = new_scene('chain')
    arena(s, boss)
    link_o = assets['ChainLink'][0]; sh = assets['ChainShackle'][0]
    # pair I-II slack (5.2 m: will snap), pair III-IV taut (7.6 m: safe), seen from player I
    A, B = (-1.5, -5.5), (2.2, -3.6)
    C, D = (-7.5, 2.0), (-2.0, 7.2)
    tether_circle(s, A); tether_circle(s, B)
    for (n, p, t) in (('I', A, (0.18, 0.22, 0.30)), ('II', B, (0.30, 0.20, 0.18)), ('III', C, (0.2, 0.28, 0.2)), ('IV', D, (0.28, 0.26, 0.16))):
        player(s, n, p, t)
    for (p, q) in ((A, B), (C, D)):
        lane(s, p, q)
        dv = (Vector((q[0], q[1], 0)) - Vector((p[0], p[1], 0))).normalized()
        pa = Vector((p[0], p[1], 0)) + dv * 0.55 + Vector((0, 0, 1.15)); qb = Vector((q[0], q[1], 0)) - dv * 0.55 + Vector((0, 0, 1.15))
        inst(s, sh, pa.to_tuple(), rot=(0, 0, math.atan2(dv.y, dv.x) + math.pi / 2), scale=1.2)
        inst(s, sh, qb.to_tuple(), rot=(0, 0, math.atan2(dv.y, dv.x) + math.pi / 2), scale=1.2)
        lay_chain(s, link_o, (pa.x, pa.y, pa.z - 0.02), (qb.x, qb.y, qb.z - 0.02))
    game_cam(s, A)
    render(s, os.path.join(out, 'mock_chain_game.png'))
    camera(s, (A[0] - 2.5, A[1] - 3.6, 2.4), ((A[0] + B[0]) / 2 - 0.6, (A[1] + B[1]) / 2 - 0.4, 0.9), fov=40)
    render(s, os.path.join(out, 'mock_chain_close.png'))
    # solo: chained to the drum (the drum hand, rest pose: about (-1.6, -1.15, 0.95) in the boss's frame)
    s2 = new_scene('drum')
    arena(s2, boss)
    P = (-2.5, -6.2); player(s2, 'solo', P)
    tether_circle(s2, (0, 0))
    drum = Vector((-1.62, -1.15, 0.95)); dv = (Vector((P[0], P[1], 0)) - Vector((drum.x, drum.y, 0))).normalized()
    pa = Vector((P[0], P[1], 0)) - dv * 0.55 + Vector((0, 0, 1.15))
    inst(s2, sh, drum.to_tuple(), rot=(0, 0, math.atan2(dv.y, dv.x) + math.pi / 2), scale=1.2)
    inst(s2, sh, pa.to_tuple(), rot=(0, 0, math.atan2(dv.y, dv.x) + math.pi / 2), scale=1.2)
    lay_chain(s2, link_o, (drum.x, drum.y, drum.z - 0.02), (pa.x, pa.y, pa.z - 0.02))
    game_cam(s2, P)
    render(s2, os.path.join(out, 'mock_drumchain_game.png'))

def render_all(assets, out, boss, tex):
    turnarounds(assets, out, tex)
    rivet_scene(assets, out, boss, tex)
    chain_scene(assets, out, boss, tex)
