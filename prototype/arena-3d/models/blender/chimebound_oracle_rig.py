# blender -b chimebound_oracle_prep.blend -P chimebound_oracle_rig.py -- <work dir> <out.glb>
# Rigs the prepared Chimebound Oracle (miniboss_prep.py ... ChimeboundOracle 4.0: 4 m from the lowest bell to the top
# finial, the cobalt sphere about 2 m across) and keys the clips the encounter's poses use.
# Axes: -Y forward (the eye-lens faces -Y), +X = his left, Z up.
#
# The Meshy mesh is 133 rigid loose parts; every part is pinned whole to one bone (no deforming weights):
#   core      the sphere, its eye-lens and the ornaments on its surface
#   wheel     the gold frame of arcs. The five bells sit on one ring round the sphere in the front (XZ) plane, so the
#             wheel turns about the forward axis (Y) like a clock face: it rocks like a pendulum at rest and spins
#             round the sphere when he casts
#   bell.0-4  each bell with its finial and clapper ball, pivoting at its hanger; each one counter-turns against
#             the wheel so it keeps hanging, and swings on its own
#   float     parent of everything at the sphere's centre: hover height, lean and turn
#
# Clips (Unity stretches each one-shot to the pose's build-up time; loops play while the pose is held):
#   Idle 4 s loop, Walk 2 s loop (a forward drift)
#   StaffRaise 1.0 s -> LanesLoop 2 s       Toll Lanes: rises, tips back to face the metronome, the wheel spins
#   FloorStrike 1.0 s (impact 0.6)           Chime Wells: drops onto the floor, bells flung outward
#   ArmsSpread 1.0 s -> ChainLoop 1.5 s      Carillon Chain: bells pushed out on the ring and shaking
#   Point 0.8 s -> PointLoop 2 s             Resonant Mark: leans at the target, the top bell thrown forward
#   ClapperWindup 1.0 s -> WindLoop 1 s      Clapper Strike: rears back, the wheel winds back
#   ClapperSwing 0.5 s (strike 0.2)          ...and lunges, the wheel whips round, bells swing forward
#   RingMetronome 1.5 s -> TollLoop 2 s      Grand Toll / Last Bell: rises high, every bell swings together
#   Recover 0.8 s, Defeat 2.5 s (held)
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['ChimeboundOracle']

S = 4.0 / 2.2                                         # survey numbers were measured on the 2.2 m prep
C = Vector((0, 0, 0.94 * S))                          # sphere centre
SPHERE_R = 0.60 * S                                   # sphere plus its surface ornaments
# bell cups (x, cup bottom, cup top) from the part survey: top, middle left/right, bottom left/right
BELLS = [(0.0, 1.63, 1.90), (0.81, 0.92, 1.32), (-0.81, 0.92, 1.32), (0.56, 0.14, 0.54), (-0.56, 0.14, 0.54)]
HOVER = 0.45                                          # rest hover: the lowest bells clear the floor by this much

B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'float': (tuple(C), tuple(C + Vector((0, 0, 0.6))), 'root'),
     'core': (tuple(C), tuple(C + Vector((0, -0.8, 0))), 'float'),
     'wheel': (tuple(C), tuple(C + Vector((0, -1.2, 0))), 'float')}
for i, (bx, lo, hi) in enumerate(BELLS):
    piv = (bx * S, 0, (hi + 0.12) * S)
    B[f'bell.{i}'] = (piv, (bx * S, 0, (lo - 0.2) * S), 'wheel')
ad = bpy.data.armatures.new('ChimeboundOracleRig'); arm = bpy.data.objects.new('ChimeboundOracleRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = n not in ('root', 'float')
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- rigid weights: one bone per loose part ----------------
me = body.data; nv = len(me.vertices)
co = np.empty(nv * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(nv)
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(nv)]); _, part = np.unique(roots, return_inverse=True)
bone_of = np.empty(nv, dtype=object); counts = {}
for p in range(part.max() + 1):
    m = part == p; c = co[m]; lo, hi = c.min(0), c.max(0)
    kind = None
    for i, (bx, blo, bhi) in enumerate(BELLS):   # a bell part lies wholly inside its bell's box
        if lo[0] > (bx - 0.28) * S and hi[0] < (bx + 0.28) * S and lo[2] > (blo - 0.26) * S and hi[2] < (bhi + 0.33) * S and abs(lo[1]) < 0.3 * S and hi[1] < 0.3 * S:
            kind = f'bell.{i}'; break
    if kind is None and (hi - lo).max() < 0.12 * S:   # small pieces (clapper tips, hanger beads) go by their centre
        ctr = c.mean(0)
        for i, (bx, blo, bhi) in enumerate(BELLS):
            if abs(ctr[0] - bx * S) < 0.2 * S and (blo - 0.3) * S < ctr[2] < (bhi + 0.25) * S and abs(ctr[1]) < 0.25 * S:
                kind = f'bell.{i}'; break
    if kind is None:
        far = np.linalg.norm(c - np.array(C), axis=1).max()
        pendant = abs(c[:, 0].mean()) < 0.15 * S and hi[2] < C.z - 0.4 * S   # the spike hanging under the sphere
        kind = 'core' if far < SPHERE_R or pendant else 'wheel'
    bone_of[m] = kind; counts[kind] = counts.get(kind, 0) + 1
print('PART_BONES', counts)
for n in B:
    if n not in ('root', 'float'): body.vertex_groups.new(name=n)
for n in B:
    idx = [int(i) for i in np.nonzero(bone_of == n)[0]]
    if idx: body.vertex_groups[n].add(idx, 1.0, 'REPLACE')
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- animation helpers (as dial_warden_rig.py: rotations in armature axes) ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
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

def bell_out(i):   # unit vector from the sphere centre out to bell i's hanger, in the XZ plane
    h = Vector(B[f'bell.{i}'][0]) - C; h.y = 0; return h.normalized()

def apply(ch):
    # ch: float_loc (x, y, z), float (euler deg), core (euler), wheel (deg about Y, + turns the top toward +X),
    #     swing[i] (x, y deg: bell i swinging forward/back and sideways), spread (m: bells pushed out along the ring)
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    def rot(n, m):
        M = pbs[n].bone.matrix_local.to_3x3(); pbs[n].rotation_quaternion = (M.inverted() @ m @ M).to_quaternion()
    def loc(n, v):
        M = pbs[n].bone.matrix_local.to_3x3(); pbs[n].location = M.inverted() @ Vector(v)
    loc('float', ch.get('float_loc', (0, 0, HOVER)))
    rot('float', R(*ch.get('float', (0, 0, 0))))
    rot('core', R(*ch.get('core', (0, 0, 0))))
    w = ch.get('wheel', 0.0); rot('wheel', R(0, w, 0))
    sw = ch.get('swing', [(0, 0)] * 5); spread = ch.get('spread', 0.0); push = ch.get('push', [0.0] * 5)
    for i in range(5):
        n = f'bell.{i}'
        # counter-turn the wheel so the bell keeps hanging, then its own swing
        rot(n, R(sw[i][0], -w + sw[i][1], 0))
        loc(n, tuple(bell_out(i) * (spread + push[i])))

def bake_clip(name, seconds, fn):
    frames = round(seconds * 30)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        apply(fn(f / 30))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

# ---------------- clips ----------------
PH = [0.0, 1.1, 2.3, 3.4, 4.6]   # per-bell phase offsets so they never swing as one unless told to
def idle_swing(t, amp=5.0, per=4.0):
    return [(amp * math.sin(2 * math.pi * t / per + PH[i]), 0.6 * amp * math.sin(2 * math.pi * 2 * t / per + PH[i] * 1.7)) for i in range(5)]
def base(t, per=4.0):
    p = 2 * math.pi * t / per
    return {'float_loc': (0, 0, HOVER + 0.07 * math.sin(p)), 'float': (1.5 * math.sin(p + 0.5), 0, 0),
            'wheel': 9.0 * math.sin(p), 'swing': idle_swing(t, 5.0, per), 'core': (0, 0, 3 * math.sin(p * 0.5))}

def idle(t): return base(t)

def walk(t):   # drift: leans into the travel, bells trail backward
    ch = base(t, 2.0); p = 2 * math.pi * t / 2.0
    ch['float'] = (10 + 1.5 * math.sin(p), 0, 2 * math.sin(p))
    ch['swing'] = [(-14 + 4 * math.sin(p + PH[i]), 3 * math.sin(p + PH[i])) for i in range(5)]
    ch['wheel'] = 5 * math.sin(p)
    return ch

# Toll Lanes: rises, tips back to face the metronome behind him, the wheel spins round the sphere
LIFT = 0.45; TIP = -14
def staff_raise(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.0, 1.0)])
    ch = base(t)
    ch['float_loc'] = (0, 0, HOVER + LIFT * u + 0.06 * snap(t, 0.8, 0.1, 0.2))
    ch['float'] = (TIP * u, 0, 0); ch['core'] = (-6 * u, 0, 0)
    ch['wheel'] = 9 * math.sin(2 * math.pi * t / 4) * (1 - u) + 120 * smooth(t / 1.0)
    ch['spread'] = 0.06 * u
    return ch
def lanes_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0)
    ch['float_loc'] = (0, 0, HOVER + LIFT + 0.05 * math.sin(p)); ch['float'] = (TIP + 1.5 * math.sin(p), 0, 0); ch['core'] = (-6, 0, 0)
    ch['wheel'] = 120 + 360 * t / 2.0          # one turn per loop: seamless
    ch['spread'] = 0.06
    ch['swing'] = [(4 * math.sin(2 * p + PH[i]), 10 * math.sin(p + PH[i])) for i in range(5)]   # bells lag the turn
    return ch

# Chime Wells: drops onto the floor; bells flung outward on impact, ringing
def floor_strike(t):
    T = 0.6
    rise = key(t, [(0, 0), (0.4, 1.0), (T - 0.04, 1.0), (T, -1.0), (1.0, -0.7)])
    ch = base(t)
    ch['float_loc'] = (0, 0, HOVER + 0.35 * max(rise, 0) - 0.4 * max(-rise, 0))
    ch['float'] = (-8 * max(rise, 0) + 10 * max(-rise, 0), 0, 0)
    ch['spread'] = 0.12 * snap(t, T, 0.04, 0.35)
    ch['swing'] = [(ring(t, T, 28, 2.4, 3) + 3 * math.sin(4 * t + PH[i]), ring(t, T, 18 * (1 if B[f'bell.{i}'][0][0] >= 0 else -1), 2.0, 3)) for i in range(5)]
    ch['wheel'] = 9 * math.sin(2 * math.pi * t / 4) + ring(t, T, 10, 1.4, 3)
    return ch

# Carillon Chain: bells pushed out on the ring and shaking hard
def arms_spread(t):
    u = key(t, [(0, 0), (0.7, 1.0), (1.0, 1.0)])
    ch = base(t)
    ch['spread'] = 0.22 * u; ch['float_loc'] = (0, 0, HOVER + 0.2 * u); ch['float'] = (-4 * u, 0, 0)
    ch['swing'] = [(16 * u * math.sin(2 * math.pi * 6 * t + PH[i]), 12 * u * math.sin(2 * math.pi * 5 * t + PH[i] * 2)) for i in range(5)]
    return ch
def chain_loop(t):
    ch = base(t, 1.5)
    ch['spread'] = 0.22 + 0.03 * math.sin(2 * math.pi * 2 * t / 1.5); ch['float_loc'] = (0, 0, HOVER + 0.2); ch['float'] = (-4, 0, 0)
    ch['swing'] = [(16 * math.sin(2 * math.pi * 6 * t / 1.5 * 1.5 + PH[i]), 12 * math.sin(2 * math.pi * 4 * t / 1.5 * 1.5 + PH[i] * 2)) for i in range(5)]
    ch['wheel'] = 6 * math.sin(2 * math.pi * t / 1.5)
    return ch

# Resonant Mark: leans toward the target (Unity faces him at it) and throws the top bell forward
def point(t):
    u = key(t, [(0, 0), (0.5, 1.0), (0.8, 1.0)])
    ch = base(t)
    ch['float'] = (14 * u, 0, 0); ch['core'] = (8 * u, 0, 0)
    sw = idle_swing(t); sw[0] = (40 * u + ring(t, 0.5, 8, 2.5, 4), 0); ch['swing'] = sw
    push = [0.0] * 5; push[0] = 0.12 * u; ch['push'] = push
    ch['wheel'] = 9 * math.sin(2 * math.pi * t / 4) * (1 - u)
    return ch
def point_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0)
    ch['float'] = (14 + 1.5 * math.sin(p), 0, 0); ch['core'] = (8, 0, 0); ch['wheel'] = 0
    sw = idle_swing(t, 4, 2.0); sw[0] = (40 + 3 * math.sin(2 * p), 0); ch['swing'] = sw
    push = [0.0] * 5; push[0] = 0.12; ch['push'] = push
    return ch

# Clapper Strike: rears back with the wheel wound back, then lunges and whips the wheel round
def clapper_windup(t):
    u = key(t, [(0, 0), (0.85, 1.0), (1.0, 1.0)])
    ch = base(t)
    ch['float'] = (-18 * u, 0, 6 * u); ch['float_loc'] = (0, 0.35 * u, HOVER + 0.15 * u)
    ch['wheel'] = 9 * math.sin(2 * math.pi * t / 4) * (1 - u) - 70 * u
    ch['swing'] = [(-22 * u + 3 * math.sin(3 * t + PH[i]), 0) for i in range(5)]
    return ch
def wind_loop(t):
    p = 2 * math.pi * t
    ch = base(t, 1.0)
    ch['float'] = (-18 + math.sin(p), 0, 6); ch['float_loc'] = (0, 0.35, HOVER + 0.15)
    ch['wheel'] = -70 - 3 * math.sin(p)
    ch['swing'] = [(-22 + 3 * math.sin(2 * p + PH[i]), 2 * math.sin(p + PH[i])) for i in range(5)]
    return ch
def clapper_swing(t):
    T = 0.2
    s = key(t, [(0, 0), (T, 1.0), (0.5, 0.75)])
    ch = base(t)
    ch['float'] = (-18 + 32 * s, 0, 6 - 12 * s); ch['float_loc'] = (0, 0.35 - 0.8 * s, HOVER + 0.15 - 0.05 * s)
    ch['wheel'] = -70 + 250 * s
    ch['swing'] = [(-22 + 48 * s + ring(t, T, 10, 3, 4), ring(t, T, 8, 2.5, 4)) for i in range(5)]
    return ch

# Grand Toll / Last Bell: rises high, tips back to the metronome, every bell swings together on the beat
def ring_metronome(t):
    u = key(t, [(0, 0), (1.1, 1.0), (1.5, 1.0)])
    ch = base(t)
    ch['float_loc'] = (0, 0, HOVER + 0.9 * u); ch['float'] = (-20 * u, 0, 0); ch['core'] = (-8 * u, 0, 0)
    ch['wheel'] = 9 * math.sin(2 * math.pi * t / 4) * (1 - u); ch['spread'] = 0.1 * u
    ch['swing'] = [(36 * u * math.sin(2 * math.pi * t / 1.0), 0) for i in range(5)]
    return ch
def toll_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0)
    ch['float_loc'] = (0, 0, HOVER + 0.9 + 0.04 * math.sin(p)); ch['float'] = (-20, 0, 0); ch['core'] = (-8, 0, 0)
    ch['wheel'] = 0; ch['spread'] = 0.1
    ch['swing'] = [(36 * math.sin(2 * p), 0) for i in range(5)]   # two full swings per loop: every bell as one
    return ch

def recover(t):
    ch = base(t)
    ch['swing'] = [(ring(t, 0.0, 10, 1.5, 3) + s[0], s[1]) for s in idle_swing(t)]
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.6, 1.0), (2.5, 1.0)])
    ch = {'float_loc': (0, 0, HOVER - 0.95 * HOVER * k), 'float': (16 * k, 0, -10 * k), 'core': (12 * k, 0, 0),
          'wheel': 9 * (1 - k) * math.sin(t) + 23 * k,
          'swing': [(ring(t, 1.6, 25, 1.4, 2.4) + 18 * k, ring(t, 1.6, 14, 1.1, 2.4)) for i in range(5)],
          'spread': -0.05 * k}
    return ch

acts = [bake_clip('Idle', 4.0, idle), bake_clip('Walk', 2.0, walk),
        bake_clip('StaffRaise', 1.0, staff_raise), bake_clip('LanesLoop', 2.0, lanes_loop),
        bake_clip('FloorStrike', 1.0, floor_strike),
        bake_clip('ArmsSpread', 1.0, arms_spread), bake_clip('ChainLoop', 1.5, chain_loop),
        bake_clip('Point', 0.8, point), bake_clip('PointLoop', 2.0, point_loop),
        bake_clip('ClapperWindup', 1.0, clapper_windup), bake_clip('WindLoop', 1.0, wind_loop),
        bake_clip('ClapperSwing', 0.5, clapper_swing),
        bake_clip('RingMetronome', 1.5, ring_metronome), bake_clip('TollLoop', 2.0, toll_loop),
        bake_clip('Recover', 0.8, recover), bake_clip('Defeat', 2.5, defeat)]
print('CLIPS', [a.name for a in acts])

# ---------------- preview strip: one frame from each clip, three-quarter view (checked before export) ----------------
if '--preview' in args:
    eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 640
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.75, 0.78, 1)
    bpy.ops.mesh.primitive_plane_add(size=12); floor = bpy.context.object
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.location = (-7.5, -10.5, 6.5); cam.rotation_euler = (Vector((0, 0, 2.3)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (math.radians(35), 0, math.radians(-30)); sun.data.energy = 3.5
    shots = [('Idle', 0.0), ('Walk', 0.5), ('LanesLoop', 0.6), ('FloorStrike', 0.62), ('ChainLoop', 0.3), ('PointLoop', 0.5),
             ('WindLoop', 0.0), ('ClapperSwing', 0.22), ('TollLoop', 0.25), ('Defeat', 2.5)]
    for name, tt in shots:
        arm.animation_data.action = bpy.data.actions[name]; sc.frame_set(1 + round(tt * 30))
        sc.render.filepath = os.path.join(work, f'pose_{name}.png'); bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(floor); bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)

arm.animation_data.action = acts[0]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'chimebound_oracle_rig.blend'))
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); body.select_set(True)
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
