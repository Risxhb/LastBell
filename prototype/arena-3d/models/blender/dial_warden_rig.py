# blender -b dial_warden_prep.blend -P dial_warden_rig.py -- <work dir> <out.glb>
# Rigs the prepared Dial Warden (2.8 m, from dial_warden_prep.py), keys his clips and exports a GLB with JPEG textures.
# Axes: -Y forward, +X = his left, Z up. The clock-hand blade is in his right hand (-X); the counterweight hangs on a
# chain from his left (+X).
#
# The Meshy mesh is 114 rigid loose parts, so there are no heat weights: every part is pinned whole to one bone. The
# one exception is part 48, a single straight piece running from the right shoulder down the arm and out to the blade
# tip: its vertices below the wrist go to 'blade.R', the rest to 'arm.R'. His right arm is therefore one straight
# limb from the shoulder (the clock hand), with a wrist joint for the blade.
#
# Posing: the body, head, dial and legs use Euler rotations in rest-armature axes (as in aurelion_rig.py: X + tips
# the top of a bone forward, toward -Y; Y + tilts it toward +X; Z + turns it toward his left). The arms, blade and
# chain are aimed instead: each gets an absolute direction in armature axes, so a pose reads as "blade points up".
#
# Clips (Unity stretches each one-shot to the attack's length, so the key moment stays at the same fraction):
#   Idle 4 s loop, Walk 1.2 s loop
#   SkyRaise 1.5 s (blade up by 0.9)               SkyLoop 2 s loop          Constellation
#   Hoist 1.0 s (weight up and whirling by 0.8)    HoistLoop 1 s loop        Tip the Scales
#   WeightSlam 2.0 s (impact at 1.2)               BalanceLoop 2 s loop      (the weigh: he rocks like a scale)
#   HandPlant 1.2 s (blade level at 0.8)           HandLoop 2 s loop         Arrest the Hand
#   Caught 0.6 s (jolt at 0.08)                    BreakThrough 1.0 s (surge at 0.25)
#   GroundSlam 2.2 s (impact at 1.3)               Collapse 1.6 s (arms clench at 1.1)   Expanding / collapsing slam
#   PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math, numpy as np
from mathutils import Vector, Euler, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['DialWarden']

# ---------------- armature (joints from the part survey and the 0.25 m grid renders of the prepped mesh) ----------------
CX = 0.04                               # the body's centre line (the mesh sits 4 cm toward his left)
def mx(p): return (2 * CX - p[0], p[1], p[2])   # mirror a right-side point to the left
SHOULDER_R, WRIST_R, TIP = (-0.90, 0.20, 1.92), (-1.48, -0.65, 1.00), (-2.17, -1.68, 0.0)
SHOULDER_L, WRIST_L, WEIGHT = (0.98, 0.20, 1.92), (1.62, -0.07, 0.97), (1.67, -0.12, 0.57)
HIP_R, KNEE_R, ANKLE_R, TOE_R = (-0.75, 0.08, 1.10), (-0.78, -0.06, 0.85), (-0.86, -0.18, 0.30), (-0.88, -0.60, 0.05)
B = {  # name: (head, tail, parent)
    'root': ((0, 0, 0), (0, 0.6, 0), None),
    'hips': ((CX, 0.12, 0.95), (CX, 0.12, 1.25), 'root'),
    'body': ((CX, 0.18, 1.49), (CX, 0.18, 2.05), 'hips'),
    'head': ((CX, 0.19, 2.12), (CX, 0.19, 2.80), 'body'),
    'dial': ((CX, -0.56, 1.52), (CX, -0.90, 1.52), 'body'),
    'arm.R': (SHOULDER_R, WRIST_R, 'body'),
    'blade.R': (WRIST_R, TIP, 'arm.R'),
    'arm.L': (SHOULDER_L, WRIST_L, 'body'),
    'chain.L': (WRIST_L, WEIGHT, 'arm.L'),
    'thigh.R': (HIP_R, KNEE_R, 'hips'), 'shin.R': (KNEE_R, ANKLE_R, 'thigh.R'), 'foot.R': (ANKLE_R, TOE_R, 'shin.R'),
    'thigh.L': (mx(HIP_R), mx(KNEE_R), 'hips'), 'shin.L': (mx(KNEE_R), mx(ANKLE_R), 'thigh.L'), 'foot.L': (mx(ANKLE_R), mx(TOE_R), 'shin.L'),
}
ad = bpy.data.armatures.new('DialWardenRig'); arm = bpy.data.objects.new('DialWardenRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = n != 'root'
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
sizes = np.bincount(part)
blade_part = None
for p in range(part.max() + 1):   # the straight shoulder-to-tip piece: the one part that touches the floor at the tip
    c = co[part == p]
    if c[:, 2].min() < 0.02 and c[:, 0].max() < -0.85 and c[:, 1].min() < -1.4: blade_part = p
assert blade_part is not None, 'blade piece not found'
HEAD_PARTS = set()
bone_of = np.empty(nv, dtype=object)
counts = {}
for p in range(part.max() + 1):
    m = part == p; c = co[m]; ctr = c.mean(0); lo, hi = c.min(0), c.max(0); dx = ctr[0] - CX
    if p == blade_part:
        bone_of[m] = np.where(c[:, 2] < WRIST_R[2], 'blade.R', 'arm.R'); counts['blade.R/arm.R (split)'] = int(m.sum()); continue
    if hi[1] < -0.3 and 0.7 < ctr[2] < 2.25 and abs(dx) < 0.6: kind = 'dial'                    # the chest dial's cross and rings
    elif ctr[2] > 2.05 and abs(dx) < 0.3 and 0.1 < ctr[1] < 0.25: kind = 'head'                # the head and its ring
    elif dx > 1.36 and ctr[2] < 1.0: kind = 'chain.L'                                          # chain links and the weight
    elif abs(dx) >= 0.92 and ctr[2] >= 0.83 and (ctr[2] >= 1.2 or abs(dx) >= 1.2): kind = 'arm.L' if dx > 0 else 'arm.R'   # (hip gears: |dx| 0.93 at z 1.1; toes reach |dx| 1.2 at the floor)
    elif ctr[2] < 1.27 and abs(dx) >= 0.35:
        side = 'L' if dx > 0 else 'R'
        kind = ('foot.' if ctr[2] < 0.3 else 'shin.' if ctr[2] < 0.85 else 'thigh.') + side
    else: kind = 'body'
    bone_of[m] = kind; counts[kind] = counts.get(kind, 0) + 1
print('PART_BONES', counts)
for n in B:
    if n != 'root': body.vertex_groups.new(name=n)
for n in B:
    if n == 'root': continue
    idx = [int(i) for i in np.nonzero(bone_of == n)[0]]
    if idx: body.vertex_groups[n].add(idx, 1.0, 'REPLACE')
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- animation helpers ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
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
def nrm(v): v = Vector(v); return v.normalized()
def dirs(t, pts):   # key() over direction vectors, renormalised
    return tuple(nrm(key(t, pts)))

REST_DIR = {n: (Vector(B[n][1]) - Vector(B[n][0])).normalized() for n in ('arm.R', 'blade.R', 'arm.L', 'chain.L')}
AIM_ORDER = ('arm.R', 'blade.R', 'arm.L', 'chain.L')
def aim(name, d, twist=0.0):
    # point bone `name` along armature-space direction d from wherever its head is now (parents already posed)
    bpy.context.view_layer.update()
    pb = pbs[name]; head = pb.head.copy(); d = nrm(d)
    M = Matrix.Rotation(math.radians(twist), 3, d) @ REST_DIR[name].rotation_difference(d).to_matrix()
    pb.matrix = Matrix.Translation(head) @ (M @ pb.bone.matrix_local.to_3x3()).to_4x4()

# rest directions as tuples, for poses that keep a limb where it hangs
ARM_R0, BLADE0, ARM_L0 = tuple(REST_DIR['arm.R']), tuple(REST_DIR['blade.R']), tuple(REST_DIR['arm.L'])
DOWN = (0.0, 0.0, -1.0)
EULER = ('hips', 'body', 'head', 'dial', 'thigh.R', 'shin.R', 'foot.R', 'thigh.L', 'shin.L', 'foot.L')
def pose_of(ch):
    p = {}
    for n in EULER:
        if n in ch: p[n] = {'rot': R(*ch[n])}
    p.setdefault('hips', {})['loc'] = ch.get('hips_loc', (0, 0, 0))
    p.setdefault('head', {})['loc'] = ch.get('head_loc', (0, 0, 0))
    return p

def bake_clip(name, seconds, fn):
    frames = round(seconds * 30)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        ch = fn(f / 30)   # loop clips are periodic in their length, so the last frame matches the first
        set_pose(pose_of(ch))
        aims = ch.get('aim', {})
        for n in AIM_ORDER:
            if n in aims: aim(n, *aims[n]) if isinstance(aims[n][0], tuple) else aim(n, aims[n])
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

# ---------------- clips ----------------
def tick_dial(t, per=0.5, step=11.25):
    # the chest dial ticks like a clock: one notch every `per` seconds, each notch snapping in over 0.06 s
    n = math.floor(t / per); u = (t - n * per) / 0.06
    return (0, step * (n + smooth(u)), 0)

def base(t, period=4.0):
    # alive under every clip: a slow breath (the sphere rises a little), the head bobbing in its collar,
    # the counterweight swaying on its chain
    p = 2 * math.pi * t / period; br = math.sin(p)
    return {
        'body': (-1.0 * br, 0, 0), 'head': (2.0 * math.sin(p + 0.7), 0, 6 * math.sin(p * 0.5 + 0.3) if period >= 4 else 0),
        'head_loc': (0, 0, 0.015 * br), 'hips_loc': (0, 0, 0.01 * br),
        'dial': tick_dial(t),
        'aim': {'chain.L': (0.12 * math.sin(p + 0.4), 0.05 * math.sin(p), -1.0)},
    }

def idle(t):
    ch = base(t)
    ch['aim'].update({'arm.R': add(ARM_R0, (0, 0.03 * math.sin(2 * math.pi * t / 4 + 1), 0)), 'arm.L': ARM_L0})
    return ch

WALK_CYCLE = 1.2   # two short heavy steps per cycle
def walk(t):
    ph = 2 * math.pi * t / WALK_CYCLE; s = math.sin(ph); lift = lambda q: max(0.0, math.sin(q))
    ch = base(t, WALK_CYCLE)
    ch.update({
        'thigh.R': (-22 * s, 0, 0), 'shin.R': (18 * lift(ph + math.pi), 0, 0), 'foot.R': (10 * s, 0, 0),
        'thigh.L': (22 * s, 0, 0), 'shin.L': (18 * lift(ph), 0, 0), 'foot.L': (-10 * s, 0, 0),
        'hips_loc': (0, 0, -0.04 + 0.05 * abs(math.cos(ph))), 'hips': (0, 4 * s, 0),
        'body': (4, -3 * s, 3 * s), 'head': (0, 2 * s, 0),
        'dial': tick_dial(t, 0.3, 11.25 * 0.3 / 0.25),   # 4 notches per cycle = 45 degrees, seamless
    })
    ch['aim'].update({'arm.R': add(ARM_R0, (0, 0.12 * s, 0)), 'arm.L': add(ARM_L0, (0, -0.15 * s, 0)),
                      'chain.L': (0.05, 0.25 * s, -1.0)})
    return ch

# Constellation: the blade rises to point at the sky and he looks up; the dial spins fast while he "draws"
SKY = (-0.18, 0.05, 1.0)
def sky_raise(t):
    u = key(t, [(0, 0), (0.9, 1.0), (1.5, 1.0)])
    ch = base(t)
    ch.update({'body': (-10 * u, 0, -6 * u), 'head': (-14 * u, 0, 0), 'hips_loc': (0, 0, -0.06 * u + 0.04 * snap(t, 0.9, 0.1, 0.4))})
    ch['aim'].update({'arm.R': dirs(t, [(0, ARM_R0), (0.45, (-0.75, -0.55, 0.2)), (0.9, SKY)]),
                      'blade.R': dirs(t, [(0, BLADE0), (0.45, (-0.7, -0.6, 0.3)), (0.9, SKY), (1.5, SKY)]),
                      'arm.L': dirs(t, [(0, ARM_L0), (0.9, (0.75, -0.1, -0.65))])})
    ch['dial'] = (0, 180 * smooth(t / 1.5), 0)
    return ch
def sky_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0)
    ch.update({'body': (-10, 0, -6 + 2 * math.sin(p)), 'head': (-14 + 2 * math.sin(p + 1), 0, 0)})
    ch['aim'].update({'arm.R': add(SKY, (0.04 * math.sin(p), 0.03 * math.cos(p), 0)), 'blade.R': add(SKY, (0.06 * math.sin(p), 0.05 * math.cos(p), 0)),
                      'arm.L': (0.75, -0.1, -0.65)})
    ch['dial'] = (0, 180 * t, 0)   # half a turn a second; 2 s = 360 degrees, seamless
    return ch

# Tip the Scales: the blade is planted point-down in front of him and the counterweight whirls overhead
PLANT_ARM, PLANT_BLADE = (-0.32, -0.62, -0.72), (0.05, -0.35, -0.94)
UP_L = (0.2, 0.08, 1.0)
def whirl(t, rate=1.0):
    a = 2 * math.pi * rate * t
    return (math.cos(a), math.sin(a), -0.35)
def hoist(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.0, 1.0)])
    ch = base(t)
    ch.update({'body': (-4 * u, 0, 8 * u), 'hips_loc': (0, 0, -0.08 * u)})
    ch['aim'].update({'arm.R': dirs(t, [(0, ARM_R0), (0.5, PLANT_ARM)]), 'blade.R': dirs(t, [(0, BLADE0), (0.4, (-0.2, -0.8, 0.5)), (0.55, PLANT_BLADE)]),
                      'arm.L': dirs(t, [(0, ARM_L0), (0.5, (0.9, -0.2, 0.3)), (0.8, UP_L)]),
                      'chain.L': nrm(key(t, [(0, DOWN), (0.8, whirl(0.8))]))})
    return ch
def hoist_loop(t):
    ch = base(t, 1.0)
    ch.update({'body': (-4, 0, 8 + 2 * math.sin(2 * math.pi * t)), 'hips_loc': (0, 0, -0.08)})
    ch['aim'].update({'arm.R': PLANT_ARM, 'blade.R': PLANT_BLADE, 'arm.L': add(UP_L, (0.05 * math.cos(2 * math.pi * t), 0.05 * math.sin(2 * math.pi * t), 0)),
                      'chain.L': whirl(t + 0.8)})
    return ch
def weight_slam(t):
    # 0-0.9 the whirl winds up and he leans back; 1.2 the weight is flung forward and down onto its point; he holds
    # the arm out along the chain, then hauls it back and resettles by 2.0
    T = 1.2
    lean = key(t, [(0, 0), (0.9, 1.0), (T, -1.0), (1.55, -0.8), (2.0, 0)])
    ch = base(t)
    ch.update({'body': (-10 * lean, 0, 8 - 18 * max(0, -lean)), 'hips_loc': (0, 0, -0.08 - 0.1 * snap(t, T, 0.05, 0.4)),
               'head': (6 * max(0, -lean), 0, 0)})
    ch['aim'].update({'arm.R': dirs(t, [(0, PLANT_ARM), (1.6, PLANT_ARM), (2.0, ARM_R0)]),
                      'blade.R': dirs(t, [(0, PLANT_BLADE), (1.6, PLANT_BLADE), (2.0, BLADE0)]),
                      'arm.L': dirs(t, [(0, UP_L), (0.9, (0.25, 0.45, 0.9)), (T, (0.35, -0.85, -0.2)), (1.55, (0.4, -0.85, -0.25)), (2.0, ARM_L0)]),
                      'chain.L': nrm(key(t, [(0, whirl(0.8)), (0.9, whirl(1.7)), (T, (0.2, -0.95, -0.25)), (1.55, (0.15, -0.9, -0.4)), (2.0, DOWN)]))})
    return ch
def balance_loop(t):
    # the weigh: he rocks side to side with both arms out like the pans of a scale
    p = 2 * math.pi * t / 2.0; s = math.sin(p)
    ch = base(t, 2.0)
    ch.update({'body': (0, 9 * s, 0), 'head': (0, -5 * s, 0), 'hips_loc': (0, 0, -0.05)})
    ch['aim'].update({'arm.R': (-0.95, -0.15, -0.25 - 0.3 * s), 'blade.R': (-0.95, -0.2, -0.2 - 0.35 * s),
                      'arm.L': (0.95, -0.15, -0.25 + 0.3 * s), 'chain.L': (0.1 * s, 0, -1)})
    return ch

# Arrest the Hand: the blade held out level in front of him, tip toward the floor out at the hours (Unity turns his
# whole body to follow the sweep); the dial spins with the hand
HOLD_ARM, HOLD_BLADE = (0.18, -0.82, -0.45), (0.12, -0.98, -0.16)
def hand_plant(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.2, 1.0)])
    ch = base(t)
    ch.update({'body': (8 * u, 0, -10 * u), 'hips_loc': (0, 0, -0.1 * u - 0.05 * snap(t, 0.8, 0.08, 0.3)), 'head': (4 * u, 0, 0),
               'thigh.R': (-8 * u, 0, 0), 'thigh.L': (10 * u, 0, 0)})
    ch['aim'].update({'arm.R': dirs(t, [(0, ARM_R0), (0.4, (-0.8, -0.3, 0.3)), (0.8, HOLD_ARM)]),
                      'blade.R': dirs(t, [(0, BLADE0), (0.4, (-0.85, -0.2, 0.4)), (0.8, HOLD_BLADE)]),
                      'arm.L': dirs(t, [(0, ARM_L0), (0.8, (0.85, 0.35, -0.4))])})
    return ch
def hand_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0)
    ch.update({'body': (8, 0, -10), 'hips_loc': (0, 0, -0.1), 'head': (4, 0, 0), 'thigh.R': (-8, 0, 0), 'thigh.L': (10, 0, 0)})
    tremble = 0.012 * math.sin(2 * math.pi * 7 * t)
    ch['aim'].update({'arm.R': add(HOLD_ARM, (tremble, 0, 0)), 'blade.R': add(HOLD_BLADE, (tremble, 0, 0.01 * math.sin(p))),
                      'arm.L': (0.85, 0.35, -0.4), 'chain.L': (0.15 * math.sin(p), 0.1, -1)})
    ch['dial'] = (0, 360 * t / 2.0, 0)
    return ch
def caught(t):
    # the hand is stopped dead: a jolt back through the arm into the body, the dial halts with a shudder
    j = snap(t, 0.08, 0.04, 0.45)
    ch = hand_loop(0)
    ch.update({'body': (8 - 12 * j, 0, -10 + 6 * j), 'hips_loc': (0, 0.08 * j, -0.1), 'head': (4 - 10 * j, 0, 0)})
    ch['aim'].update({'arm.R': add(HOLD_ARM, (-0.25 * j, 0.25 * j, 0.15 * j)), 'blade.R': add(HOLD_BLADE, (-0.35 * j, 0.15 * j, 0.25 * j))})
    ch['dial'] = (0, ring(t, 0.08, 14, 4, 4), 0)
    return ch
def break_through(t):
    # the parry fails: he wrenches the hand onward with his whole body, the dial whirls
    T = 0.25
    s = key(t, [(0, 0), (T, 1.0), (0.6, 1.0), (1.0, 0)])
    ch = hand_loop(0)
    ch.update({'body': (14 * s, 0, -10 - 25 * s), 'hips_loc': (0, -0.1 * s, -0.1 - 0.06 * s), 'head': (4 + 6 * s, 0, -10 * s)})
    ch['aim'].update({'arm.R': add(HOLD_ARM, (0.5 * s, -0.1 * s, -0.05 * s)), 'blade.R': add(HOLD_BLADE, (0.6 * s, 0, -0.05 * s))})
    ch['dial'] = (0, 720 * smooth(t / 1.0), 0)
    return ch

# The slam that expands outward and then collapses back in: both arms up, a short hop, the whole body slams down
# (impact 1.3: the wave leaves the centre). Collapse: arms spread wide as the wave comes back, then clench in (1.1).
UP_R = (-0.3, 0.05, 0.95)
def ground_slam(t):
    T = 1.3
    up = key(t, [(0, 0), (0.8, 1.0), (1.05, 1.0), (T, 0.0), (2.2, 0.0)])
    crouch = key(t, [(0, 0), (0.6, 1.0), (0.85, -0.6), (1.05, -1.0), (T, 1.4), (1.7, 1.2), (2.2, 0.6)])
    ch = base(t)
    ch.update({'hips_loc': (0, 0, -0.18 * max(crouch, 0) + 0.28 * max(-crouch, 0)),
               'body': (-10 * up + 16 * (1 - up) * min(1, t / T), 0, 0), 'head': (-10 * up + 10 * snap(t, T, 0.04, 0.4), 0, 0),
               'thigh.R': (-14 * max(crouch, 0), 0, 0), 'shin.R': (22 * max(crouch, 0), 0, 0), 'foot.R': (-8 * max(crouch, 0), 0, 0),
               'thigh.L': (-14 * max(crouch, 0), 0, 0), 'shin.L': (22 * max(crouch, 0), 0, 0), 'foot.L': (-8 * max(crouch, 0), 0, 0)})
    slamR, slamB, slamL = (-0.55, -0.55, -0.62), (-0.2, -0.45, -0.87), (0.55, -0.5, -0.66)
    ch['aim'].update({'arm.R': dirs(t, [(0, ARM_R0), (0.8, UP_R), (1.05, UP_R), (T, slamR), (2.2, slamR)]),
                      'blade.R': dirs(t, [(0, BLADE0), (0.8, (-0.35, 0.2, 0.92)), (1.05, (-0.35, 0.25, 0.9)), (T, slamB), (2.2, slamB)]),
                      'arm.L': dirs(t, [(0, ARM_L0), (0.8, (0.3, 0.05, 0.95)), (1.05, (0.3, 0.05, 0.95)), (T, slamL), (2.2, slamL)]),
                      'chain.L': nrm(key(t, [(0, DOWN), (0.8, (0.2, 0.3, -0.9)), (1.05, (0.1, 0.4, 0.9)), (T + 0.05, (0.3, -0.6, -0.75)), (2.2, (0.3, -0.6, -0.75))]))})
    ch['dial'] = (0, 90 * smooth(t / T), 0)
    return ch
def collapse(t):
    T = 1.1
    spread = key(t, [(0, 0), (0.6, 1.0), (T - 0.05, 1.0), (T + 0.08, -1.0), (1.6, -0.7)])
    ch = base(t)
    ch.update({'hips_loc': (0, 0, -0.12 + 0.06 * max(spread, 0) - 0.08 * max(-spread, 0)),
               'body': (-6 * max(spread, 0) + 12 * max(-spread, 0), 0, 0), 'head': (-8 * max(spread, 0) + 6 * max(-spread, 0), 0, 0)})
    wideR, wideL, inR, inL = (-0.95, -0.15, -0.15), (0.95, -0.15, -0.15), (0.45, -0.8, -0.4), (-0.35, -0.82, -0.45)
    ch['aim'].update({'arm.R': dirs(t, [(0, (-0.55, -0.55, -0.62)), (0.6, wideR), (T - 0.05, wideR), (T + 0.08, inR), (1.6, inR)]),
                      'blade.R': dirs(t, [(0, (-0.2, -0.45, -0.87)), (0.6, (-0.98, -0.1, -0.1)), (T - 0.05, (-0.98, -0.1, -0.1)), (T + 0.08, (0.6, -0.75, -0.25)), (1.6, (0.6, -0.75, -0.25))]),
                      'arm.L': dirs(t, [(0, (0.55, -0.5, -0.66)), (0.6, wideL), (T - 0.05, wideL), (T + 0.08, inL), (1.6, inL)]),
                      'chain.L': nrm(key(t, [(0, (0.3, -0.6, -0.75)), (0.6, (0.6, 0, -0.8)), (T + 0.1, (-0.3, -0.5, -0.8)), (1.6, DOWN)]))})
    ch['dial'] = (0, 90 - 90 * smooth(t / 1.6), 0)
    return ch

def phase_shift(t):
    # 2 s, no damage: the dial spins up to a blur, he spreads his arms and the sphere shudders
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = base(t)
    ch.update({'body': (-8 * s + 2 * math.sin(40 * t) * s, 0, 0), 'head': (-12 * s, 0, 0), 'hips_loc': (0, 0, 0.05 * s)})
    ch['aim'].update({'arm.R': dirs(t, [(0, ARM_R0), (0.6, (-0.9, 0.1, 0.4)), (1.5, (-0.9, 0.1, 0.4)), (2.0, ARM_R0)]),
                      'blade.R': dirs(t, [(0, BLADE0), (0.6, (-0.85, 0.15, 0.5)), (1.5, (-0.85, 0.15, 0.5)), (2.0, BLADE0)]),
                      'arm.L': dirs(t, [(0, ARM_L0), (0.6, (0.9, 0.1, 0.4)), (1.5, (0.9, 0.1, 0.4)), (2.0, ARM_L0)])})
    ch['dial'] = (0, 1440 * smooth(t / 2.0), 0)
    return ch

def defeat(t):
    # 2.5 s, held: the knees give, the sphere tips forward onto its blade, the head sinks into the collar, the weight drops
    k = key(t, [(0, 0), (1.2, 1.0), (2.5, 1.0)]); slump = key(t, [(0, 0), (0.8, 0), (2.0, 1.0), (2.5, 1.0)])
    ch = {'hips_loc': (0, 0, -0.3 * k), 'body': (18 * slump, 0, 4 * slump), 'head': (20 * slump, 0, 0), 'head_loc': (0, 0, -0.22 * slump),
          'thigh.R': (-20 * k, 0, 0), 'shin.R': (32 * k, 0, 0), 'foot.R': (-12 * k, 0, 0),
          'thigh.L': (-20 * k, 0, 0), 'shin.L': (32 * k, 0, 0), 'foot.L': (-12 * k, 0, 0),
          'dial': (0, ring(t, 0.1, 25, 1.2, 1.4), 0),
          'aim': {'arm.R': dirs(t, [(0, ARM_R0), (2.0, (-0.45, -0.7, -0.55))]), 'blade.R': dirs(t, [(0, BLADE0), (2.0, (-0.2, -0.6, -0.78))]),
                  'arm.L': dirs(t, [(0, ARM_L0), (2.0, (0.4, -0.3, -0.87))]), 'chain.L': dirs(t, [(0, DOWN), (1.0, (0.3, -0.5, -0.8))])}}
    return ch

acts = [bake_clip('Idle', 4.0, idle), bake_clip('Walk', WALK_CYCLE, walk),
        bake_clip('SkyRaise', 1.5, sky_raise), bake_clip('SkyLoop', 2.0, sky_loop),
        bake_clip('Hoist', 1.0, hoist), bake_clip('HoistLoop', 1.0, hoist_loop),
        bake_clip('WeightSlam', 2.0, weight_slam), bake_clip('BalanceLoop', 2.0, balance_loop),
        bake_clip('HandPlant', 1.2, hand_plant), bake_clip('HandLoop', 2.0, hand_loop),
        bake_clip('Caught', 0.6, caught), bake_clip('BreakThrough', 1.0, break_through),
        bake_clip('GroundSlam', 2.2, ground_slam), bake_clip('Collapse', 1.6, collapse),
        bake_clip('PhaseShift', 2.0, phase_shift), bake_clip('Defeat', 2.5, defeat)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
set_pose({})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'dial_warden_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
