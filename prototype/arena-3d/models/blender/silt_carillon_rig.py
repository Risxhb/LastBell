# blender -b silt_carillon_prep.blend -P silt_carillon_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Silt Carillon (miniboss_prep.py ... SiltCarillon 2.7) and keys the clips in
# docs/bosses/silt-carillon.md. Axes: -Y forward (the arch, the face and the three bells), +X = its left, Z up.
# A mossy ivory dome (one Meshy piece with its front arch) on four root legs. Three bells hang under the arch on
# short link chains. Each leg is a single piece from hip to roots: it is split into a leg bone and a foot bone with a
# smooth blend across the ankle, so the roots can stay flat on the floor while the leg swings. Everything else is rigid.
#
# Clips: Idle 4 s loop, Walk 1.6 s loop (four-legged), Squat 0.8 s, TideLoop 2 s loop, TollLeft / TollMiddle /
#        TollRight 0.8 s (the bell strikes at 0.3), DomeShake 1.2 s, Rear 1.0 s, RearLoop 1.0 s loop, PhaseShift 2.0 s,
#        Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

LEGS = {'FR': (-1.01, -0.62), 'FL': (1.08, -0.62), 'BR': (-0.99, 1.23), 'BL': (1.06, 1.23)}
HIP_Z, ANK_Z = 1.1, 0.3
BELLS = {'R': -0.38, 'M': 0.04, 'L': 0.45}
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'body': ((0.04, 0.3, 1.1), (0.04, 0.3, 2.4), 'root')}
for s, (x, y) in LEGS.items():
    B['leg.' + s] = ((x, y, HIP_Z), (x, y, ANK_Z), 'body'); B['foot.' + s] = ((x, y, ANK_Z), (x, y - 0.3, 0.0), 'leg.' + s)
for s, x in BELLS.items():
    B['bell.' + s] = ((x, -0.78, 1.38), (x, -0.85, 0.5), 'body')
rig = Rig('SiltCarillon', B)

def nearest_leg(x, y): return min(LEGS, key=lambda s: math.hypot(LEGS[s][0] - x, LEGS[s][1] - y))
def classify(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0)
    if ctr[1] < -0.6 and 0.45 < ctr[2] < 1.42 and abs(ctr[0]) < 0.62 and hi[2] < 1.45:
        return 'bell.' + min(BELLS, key=lambda s: abs(BELLS[s] - ctr[0]))
    if hi[2] < 1.2 and len(c) > 300:   # a whole leg: leg above the ankle, foot below
        s = nearest_leg(ctr[0], ctr[1])
        return np.where(c[:, 2] > ANK_Z, 'leg.' + s, 'foot.' + s)
    if ctr[2] < 1.0 and math.hypot(*(np.array(LEGS[nearest_leg(ctr[0], ctr[1])]) - ctr[:2])) < 0.5:
        s = nearest_leg(ctr[0], ctr[1]); return ('leg.' if ctr[2] > ANK_Z else 'foot.') + s
    return 'body'
rig.assign(classify)
# blend leg and foot across the ankle (0.18-0.48 m) so the split never opens a seam
gi = {g.name: g.index for g in rig.body.vertex_groups}
for s in LEGS:
    leg, foot = rig.body.vertex_groups['leg.' + s], rig.body.vertex_groups['foot.' + s]
    idx = [v.index for v in rig.body.data.vertices if any(g.group in (gi['leg.' + s], gi['foot.' + s]) for g in v.groups)]
    for i in idx:
        z = rig.co[i, 2]; w = smooth((0.48 - z) / 0.30)
        leg.add([i], 1 - w, 'REPLACE'); foot.add([i], w, 'REPLACE')

def base(t, per=4.0):
    p = 2 * math.pi * t / per
    ch = {'rot': {'body': (0.8 * math.sin(p), 0, 0.6 * math.sin(p * 0.5))}, 'loc': {'body': (0, 0, 0.012 * math.sin(p))}}
    for k, s in enumerate('RML'):
        ch['rot']['bell.' + s] = (4 * math.sin(p + k * 1.3), 2 * math.sin(2 * p + k), 0)
    return ch
def plant(ch, drop=0.0, splay=0.0, pitch=0.0):
    # keep the feet flat: legs splay outward by `splay` degrees and the feet counter-turn the body's pitch
    ch['loc']['body'] = add(ch['loc'].get('body', (0, 0, 0)), (0, 0, -drop))
    for s, (x, y) in LEGS.items():
        sx = -1 if x < 0 else 1; sy = -1 if y < 0 else 1
        ch['rot']['leg.' + s] = add(ch['rot'].get('leg.' + s, (0, 0, 0)), (-sy * splay * 0.6 - pitch, sx * splay, 0))
        ch['rot']['foot.' + s] = add(ch['rot'].get('foot.' + s, (0, 0, 0)), (sy * splay * 0.6 + pitch, -sx * splay, 0))
    return ch

def idle(t): return plant(base(t))

WALK = 1.6
def walk(t):   # diagonal pairs: front-right with back-left, then front-left with back-right
    ch = base(t, WALK); ph = 2 * math.pi * t / WALK
    for s, off in (('FR', 0), ('BL', 0), ('FL', math.pi), ('BR', math.pi)):
        q = ph + off; sw = math.sin(q); lift = max(0.0, math.cos(q))
        ch['rot']['leg.' + s] = (-16 * sw, 0, 0); ch['rot']['foot.' + s] = (16 * sw - 22 * lift, 0, 0)
        ch['loc']['leg.' + s] = (0, 0, 0.06 * lift)
    ch['loc']['body'] = (0, 0, 0.03 * abs(math.sin(ph)))
    ch['rot']['body'] = (1.5 * math.sin(2 * ph), 2 * math.sin(ph), 2.5 * math.sin(ph))
    for k, s in enumerate('RML'): ch['rot']['bell.' + s] = (-8 + 6 * math.sin(2 * ph + k), 4 * math.sin(ph + k), 0)
    return ch

# Rising Silt: squats low on splayed legs, rocking with the tide
def squat(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    ch = plant(base(t), 0.22 * u, 16 * u)
    for k, s in enumerate('RML'): ch['rot']['bell.' + s] = (ring(t, 0.7, 10, 1.5, 3), 0, 0)
    return ch
def tide_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = plant(base(t, 2.0), 0.22 + 0.04 * math.sin(p), 16)
    ch['rot']['body'] = (3 * math.sin(p), 0, 0)
    for k, s in enumerate('RML'): ch['rot']['bell.' + s] = (-6 * math.sin(p - 0.6 - 0.3 * k), 0, 0)
    return ch

# Triple Toll: one bell swung hard (forward, then back), the dome leaning into it
def toll(which):
    def fn(t):
        T = 0.3
        ch = plant(base(t))
        sw = key(t, [(0, 0), (0.18, -1.0), (T, 1.0), (0.8, 0)]) + ring(t, T, 0.35, 2.5, 3)
        ch['rot']['bell.' + which] = (-55 * sw, 0, 0)
        lean = {'R': -1, 'M': 0, 'L': 1}[which]
        ch['rot']['body'] = (5 * snap(t, T, 0.1, 0.4), 4 * lean * snap(t, T, 0.1, 0.4), 0)
        for s in 'RML':
            if s != which: ch['rot']['bell.' + s] = (ring(t, T, 12, 2.2, 3.5), 0, 0)
        return ch
    return fn

# Sediment: the whole dome shaken, mud flung off
def dome_shake(t):
    s = key(t, [(0, 0), (0.15, 1.0), (0.9, 1.0), (1.2, 0)])
    ch = plant(base(t), 0.06 * s)
    ch['rot']['body'] = (3 * s * math.sin(2 * math.pi * 6 * t), 3 * s * math.sin(2 * math.pi * 7 * t), 6 * s * math.sin(2 * math.pi * 5 * t))
    for k, b in enumerate('RML'): ch['rot']['bell.' + b] = (25 * s * math.sin(2 * math.pi * 6 * t + k), 15 * s * math.sin(2 * math.pi * 5 * t + k), 0)
    return ch

# Aqueduct Fall: rears up on its back legs, front legs pawing, bells swinging wildly
REAR = 22
def rear_pose(ch, u, t):
    ch['rot']['body'] = (-REAR * u, 0, 0); ch['loc']['body'] = (0, 0.25 * u, 0.35 * u)
    for s in ('BR', 'BL'): ch['rot']['leg.' + s] = (REAR * u, 0, 0)
    for k, s in enumerate(('FR', 'FL')):
        paw = math.sin(2 * math.pi * 1.5 * t + k * math.pi)
        ch['rot']['leg.' + s] = (REAR * u - 30 * u - 12 * u * paw, 0, 0); ch['rot']['foot.' + s] = (-25 * u, 0, 0)
    for k, b in enumerate('RML'): ch['rot']['bell.' + b] = (30 * u * math.sin(2 * math.pi * 2 * t + k * 1.7), 20 * u * math.sin(2 * math.pi * 1.5 * t + k), 0)
    return ch
def rear(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.0, 1.0)])
    return rear_pose(base(t), u, t)
def rear_loop(t):
    ch = rear_pose(base(t, 1.0), 1.0, t)
    ch['loc']['body'] = (0, 0.25, 0.35 + 0.03 * math.sin(2 * math.pi * t))
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = plant(base(t), -0.1 * s)
    ch['rot']['body'] = (-6 * s + 2 * math.sin(40 * t) * s, 0, 0)
    for k, b in enumerate('RML'): ch['rot']['bell.' + b] = (40 * s * math.sin(2 * math.pi * 2.5 * t + k), 0, 0)
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.5, 1.0), (2.5, 1.0)])
    ch = plant(base(t), 0.55 * k, 32 * k)
    ch['rot']['body'] = (8 * k, -6 * k, 0)
    for j, b in enumerate('RML'): ch['rot']['bell.' + b] = (ring(t, 1.2, 20, 1.2, 1.8) + 10 * k, 0, 0)
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('Squat', 0.8, squat); rig.bake('TideLoop', 2.0, tide_loop)
rig.bake('TollLeft', 0.8, toll('L')); rig.bake('TollMiddle', 0.8, toll('M')); rig.bake('TollRight', 0.8, toll('R'))
rig.bake('DomeShake', 1.2, dome_shake); rig.bake('Rear', 1.0, rear); rig.bake('RearLoop', 1.0, rear_loop)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.4), ('TideLoop', 0), ('TollLeft', 0.18), ('TollMiddle', 0.32),
                       ('DomeShake', 0.5), ('RearLoop', 0.2), ('PhaseShift', 1.0), ('Defeat', 2.5), ('Walk', 1.2)],
                cam=(-4.4, -6.6, 3.6), target=(0, 0, 1.1))
rig.export(work, 'silt_carillon', out_glb)
