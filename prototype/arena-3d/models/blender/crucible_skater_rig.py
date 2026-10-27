# blender -b crucible_skater_prep.blend -P crucible_skater_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Crucible Skater (miniboss_prep.py ... CrucibleSkater 2.4) and keys the clips in
# docs/bosses/crucible-skater.md. Axes: -Y forward (the ivory mask and spout on the rim), +X = its left, Z up.
# A wide crucible bowl (3.5 m across) with a hub underneath, carried on three legs: front right, front left and back.
# Each leg is an upper strut from its mount under the rim to a knee, a lower strut to the ankle, and a skate blade.
# All 35 Meshy parts are rigid and pinned whole to bones.
#
# Clips: Idle 4 s loop, Walk 1.6 s loop (skating strides), SkateCrouch 0.8 s, SkateLoop 1.2 s loop, TipPour 1.0 s,
#        PourLoop 1.0 s loop, SinkBoil 1.2 s, BoilLoop 1.0 s loop, SpinStart 0.8 s, SpinLoop 1.0 s loop (Unity turns
#        the whole body; the clip holds the legs out like fan blades and sets the hub on the floor), SpinReverse 0.6 s,
#        PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

LEGS = {'R': ((-1.31, -0.64, 1.55), (-1.45, -0.72, 1.05), (-1.56, -0.92, 0.45), (-1.63, -0.86, 0.05)),
        'L': ((1.31, -0.64, 1.55), (1.45, -0.72, 1.05), (1.56, -0.92, 0.45), (1.63, -0.86, 0.05)),
        'B': ((0.0, 1.52, 1.40), (0.0, 1.62, 1.05), (0.0, 1.92, 0.45), (0.0, 1.85, 0.05))}
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'body': ((0, 0, 1.3), (0, 0, 2.3), 'root')}
for s, (m, k, a, f) in LEGS.items():
    B['thigh.' + s] = (m, k, 'body'); B['shin.' + s] = (k, a, 'thigh.' + s); B['skate.' + s] = (a, f, 'shin.' + s)
AIM = tuple(n + '.' + s for s in 'RLB' for n in ('thigh', 'shin', 'skate'))
rig = Rig('CrucibleSkater', B, AIM)

def side_of(x, y):
    best, bd = None, 9
    for s, v in LEGS.items():
        d = math.atan2(v[2][0], v[2][1]) - math.atan2(x, y); d = abs((d + math.pi) % (2 * math.pi) - math.pi)
        if d < bd: best, bd = s, d
    return best
def classify(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0); r = math.hypot(ctr[0], ctr[1])
    if ctr[2] < 1.9 and r > 1.0 and hi[2] < 1.9:
        s = side_of(ctr[0], ctr[1])
        return ('skate.' if ctr[2] < 0.5 else 'shin.' if ctr[2] < 1.2 else 'thigh.') + s
    return 'body'
rig.assign(classify)

REST = {n: tuple(rig.rest_dir[n]) for n in B}
def out_of(s):
    a = LEGS[s][2]; return Vector((a[0], a[1], 0)).normalized()
def legs(ch, splay=0.0, bend=0.0, lift=None, push=None, tilt=None):
    # IK: each ankle stays planted at its rest spot (moved by splay outward, push along Y, lift up) while the bowl
    # moves; the knee bends outward-up. bend is kept for the call sites: it only raises the knee bend direction.
    for s in 'RLB':
        o = out_of(s); l = (lift or {}).get(s, 0.0); p = (push or {}).get(s, 0.0)
        a = Vector(LEGS[s][2]) + o * splay + Vector((0, p * 0.45, 0.45 * l))
        ch['ik'] = ch.get('ik', {}); ch['ik'][('thigh.' + s, 'shin.' + s)] = (tuple(a), tuple(o + Vector((0, 0, 0.6 + bend))))
        sk = Vector(REST['skate.' + s]) + Vector((0, 0.25 * p, 0.35 * l))
        ch['aim']['skate.' + s] = tuple(sk)
    return ch
def base(t, per=4.0):
    p = 2 * math.pi * t / per
    return {'rot': {'body': (1.2 * math.sin(p), 0.8 * math.sin(p * 0.5), 0)}, 'loc': {'body': (0, 0, 0.02 * math.sin(p))}, 'aim': {}}

def idle(t): return legs(base(t))

WALK = 1.6
def walk(t):   # each leg in turn lifts, reaches forward and pushes back: a three-beat skating stride
    ch = base(t, WALK); lift = {}; push = {}
    for k, s in enumerate('RLB'):
        ph = (t / WALK - k / 3.0) % 1.0
        if ph < 0.3: u = ph / 0.3; lift[s] = 0.35 * math.sin(math.pi * u); push[s] = -0.5 + smooth(u)
        else: u = (ph - 0.3) / 0.7; lift[s] = 0; push[s] = 0.5 - u
    ch = legs(ch, 0.0, 0.1, lift, push)
    ch['rot']['body'] = (5, 2 * math.sin(2 * math.pi * t / WALK), 4 * math.sin(2 * math.pi * t / WALK))
    ch['loc']['body'] = (0, 0, -0.05 + 0.03 * math.sin(2 * math.pi * 3 * t / WALK))
    return ch

# Figure of Eight: low and leaning into the skate, gliding with alternate pushes
def skate_crouch(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    ch = legs(base(t), 0.15 * u, 0.5 * u)
    ch['rot']['body'] = (12 * u, 0, 0); ch['loc']['body'] = (0, 0, -0.28 * u)
    return ch
def skate_loop(t):
    p = 2 * math.pi * t / 1.2; s = math.sin(p)
    ch = legs(base(t, 1.2), 0.15, 0.5, push={'R': 0.4 * s, 'L': -0.4 * s, 'B': 0.25 * math.cos(p)})
    ch['rot']['body'] = (12, 6 * s, 8 * s); ch['loc']['body'] = (0, 0, -0.28 + 0.03 * abs(s))
    return ch

# Spillway Chase: the bowl tipped toward the mark, spout down, back leg extended, front knees folded
def tip_pour(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.0, 1.0)])
    ch = legs(base(t), 0.1 * u, 0.3 * u)
    ch['rot']['body'] = (28 * u + ring(t, 0.8, 2, 3, 4), 0, 0); ch['loc']['body'] = (0, -0.15 * u, -0.12 * u)
    return ch
def pour_loop(t):
    ch = tip_pour(1.0); p = 2 * math.pi * t
    ch['rot']['body'] = (28 + 2.5 * math.sin(p), 1.5 * math.sin(2 * p), 0)
    return ch

# Overflow: sinks low on splayed legs, the bowl boiling (a fast tremble)
def sink_boil(t):
    u = key(t, [(0, 0), (1.0, 1.0), (1.2, 1.0)])
    ch = legs(base(t), 0.45 * u, 0.4 * u)
    ch['loc']['body'] = (0, 0, -0.45 * u); ch['rot']['body'] = (1.5 * u * math.sin(2 * math.pi * 7 * t), 1.5 * u * math.sin(2 * math.pi * 9 * t), 0)
    return ch
def boil_loop(t):
    ch = legs(base(t, 1.0), 0.45, 0.4)
    ch['loc']['body'] = (0, 0, -0.45 + 0.015 * math.sin(2 * math.pi * 12 * t))
    ch['rot']['body'] = (1.6 * math.sin(2 * math.pi * 7 * t), 1.6 * math.sin(2 * math.pi * 9 * t), 0)
    return ch

# Crucible Spin: legs raised straight out like fan blades, the hub set down on the floor
SPIN_DROP = 0.68
def spin_pose(ch, u):
    ch.pop('ik', None)   # the legs leave the floor: plain aims from the rest pose
    for s in 'RLB':
        o = out_of(s); flat = tuple((o + Vector((0, 0, 0.05))).normalized())
        ch['aim']['thigh.' + s] = nrm(add(scale(REST['thigh.' + s], 1 - u), scale(flat, u)))
        ch['aim']['shin.' + s] = nrm(add(scale(REST['shin.' + s], 1 - u), scale(flat, u)))
        ch['aim']['skate.' + s] = nrm(add(scale(REST['skate.' + s], 1 - u), scale(tuple((o + Vector((0, 0, -0.15))).normalized()), u)))
    ch['loc']['body'] = (0, 0, -SPIN_DROP * u)
    return ch
def spin_start(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    return spin_pose(legs(base(t)), u)
def spin_loop(t):
    ch = spin_pose(legs(base(t, 1.0)), 1.0)
    ch['rot']['body'] = (2 * math.sin(2 * math.pi * 3 * t), 2 * math.cos(2 * math.pi * 3 * t), 0)   # wobble like a top
    return ch
def spin_reverse(t):   # the skates scrape the other way: a jolt and a counter-wobble
    ch = spin_pose(legs(base(t)), 1.0)
    ch['rot']['body'] = (ring(t, 0.05, 7, 3, 5), ring(t, 0.05, -5, 2.5, 5), 0)
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.5, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = legs(base(t), -0.1 * s, -0.3 * s)
    ch['loc']['body'] = (0, 0, 0.25 * s); ch['rot']['body'] = (3 * math.sin(37 * t) * s, 3 * math.sin(29 * t) * s, 0)
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.4, 1.0), (2.5, 1.0)]); tip = key(t, [(0, 0), (1.0, 0), (2.2, 1.0), (2.5, 1.0)])
    ch = legs(base(t), 0.7 * k, 0.5 * k)
    ch['loc']['body'] = (0, -0.2 * tip, -0.62 * k); ch['rot']['body'] = (14 * tip, -9 * tip, 0)
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('SkateCrouch', 0.8, skate_crouch); rig.bake('SkateLoop', 1.2, skate_loop)
rig.bake('TipPour', 1.0, tip_pour); rig.bake('PourLoop', 1.0, pour_loop)
rig.bake('SinkBoil', 1.2, sink_boil); rig.bake('BoilLoop', 1.0, boil_loop)
rig.bake('SpinStart', 0.8, spin_start); rig.bake('SpinLoop', 1.0, spin_loop); rig.bake('SpinReverse', 0.6, spin_reverse)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.4), ('SkateLoop', 0.3), ('PourLoop', 0), ('BoilLoop', 0.1),
                       ('SpinStart', 0.4), ('SpinLoop', 0), ('PhaseShift', 1.0), ('Defeat', 1.2), ('Defeat', 2.5)],
                cam=(-4.6, -7.4, 4.4), target=(0, 0, 1.0))
rig.export(work, 'crucible_skater', out_glb)
