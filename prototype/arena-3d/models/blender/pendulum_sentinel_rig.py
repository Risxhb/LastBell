# blender -b pendulum_sentinel_prep.blend -P pendulum_sentinel_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Pendulum Sentinel (miniboss_prep.py ... PendulumSentinel 4.6) and keys the clips in
# docs/bosses/pendulum-sentinel.md. Axes: -Y forward, +X = his left, Z up.
# The body is three-fold: an ivory bell tower on three legs (front right, front left, back) and three arms above them,
# each a single rigid piece from the shoulder down to its brass bell-weight. A pendulum (rod and disc) hangs inside
# the tower's front arch and is its own bone. All 123 Meshy parts are pinned whole to bones.
#
# Clips: Idle 4 s loop, Walk 1.5 s loop (tripod gait), SwingWind 1.0 s, SwingLeft 1.0 s, SwingRight 1.0 s
#        (Shortening Swing; the pass through the middle is at 0.5), WeightSpin 2.0 s loop, WeightThrow 1.0 s
#        (release 0.35), PlumbDrop 1.2 s, PlumbLoop 2.0 s loop, ChargeLean 1.0 s, Charge 0.8 s loop,
#        ChargeReturn 1.0 s, WallSlam 0.8 s (impact 0.1), StuckLoop 2 s loop, PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

# ---------------- skeleton (from the part survey and the 0.25 m grid renders) ----------------
LEGS = {'R': ((-0.54, -0.25, 1.63), (-0.82, -0.43, 0.55), (-0.88, -0.50, 0.0)),
        'L': ((0.54, -0.25, 1.63), (0.82, -0.43, 0.55), (0.88, -0.50, 0.0)),
        'B': ((0.0, 0.68, 1.63), (0.0, 1.02, 0.55), (0.0, 1.10, 0.0))}
ARMS = {'R': ((-0.55, -0.42, 3.30), (-0.97, -0.50, 2.05)),
        'L': ((0.55, -0.42, 3.30), (0.97, -0.50, 2.05)),
        'B': ((0.0, 0.85, 3.30), (0.0, 1.17, 2.05))}
PIVOT, BOB = (0.0, -0.33, 3.36), (0.0, -0.45, 2.24)
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'hips': ((0, 0.1, 1.63), (0, 0.1, 1.95), 'root'),
     'body': ((0, 0.1, 1.95), (0, 0.1, 3.75), 'hips'),
     'pendulum': (PIVOT, BOB, 'body')}
for s, (h, a, t) in LEGS.items():
    B['thigh.' + s] = (h, a, 'hips'); B['foot.' + s] = (a, t, 'thigh.' + s)
for s, (sh, w) in ARMS.items():
    B['arm.' + s] = (sh, w, 'body')
AIM = ('thigh.R', 'foot.R', 'thigh.L', 'foot.L', 'thigh.B', 'foot.B', 'arm.R', 'arm.L', 'arm.B')
rig = Rig('PendulumSentinel', B, AIM)

def azimuth_side(x, y, table):
    best, bd = None, 9
    for s, p in table.items():
        d = math.atan2(p[0], p[1]) - math.atan2(x, y); d = abs((d + math.pi) % (2 * math.pi) - math.pi)
        if d < bd: best, bd = s, d
    return best
LEG_XY = {s: (v[1][0], v[1][1]) for s, v in LEGS.items()}
ARM_XY = {s: (v[1][0], v[1][1]) for s, v in ARMS.items()}
def classify(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0); r = math.hypot(ctr[0], ctr[1] - 0.1)
    if abs(ctr[0]) < 0.22 and hi[0] - lo[0] < 0.45 and ctr[1] < -0.2 and 1.95 < ctr[2] < 3.45 and hi[2] - lo[2] < 1.5 and lo[1] > -0.52:
        return 'pendulum'
    if ctr[2] > 1.7 and r > 0.7: return 'arm.' + azimuth_side(ctr[0], ctr[1], ARM_XY)
    if ctr[2] < 1.75 and r > 0.3:
        s = azimuth_side(ctr[0], ctr[1], LEG_XY)
        return ('thigh.' if ctr[2] > LEGS[s][1][2] + 0.04 else 'foot.') + s
    return 'body'
rig.assign(classify)

# ---------------- poses ----------------
REST = {n: tuple(rig.rest_dir[n]) for n in B}
def base(t, per=4.0):
    p = 2 * math.pi * t / per; br = math.sin(p)
    return {'rot': {'body': (0.8 * br, 0, 0), 'pendulum': (0, 6 * math.sin(2 * p), 0)},
            'loc': {'hips': (0, 0, 0.015 * br)},
            'aim': {('arm.' + s): add(REST['arm.' + s], (0.03 * math.sin(p + k), 0.03 * math.cos(p + k), 0)) for k, s in enumerate('RLB')}}
def legs_rest(ch, splay=0.0, crouch=0.0):
    # splay pushes the knees (ankle joints) outward from the centre; crouch lowers the hips
    for s, (h, a, t) in LEGS.items():
        out = Vector((a[0], a[1] - 0.1, 0)).normalized()
        ch['aim']['thigh.' + s] = add(REST['thigh.' + s], tuple(out * splay))
        ch['aim']['foot.' + s] = add(REST['foot.' + s], tuple(out * splay * 0.6))
    ch['loc']['hips'] = add(ch['loc'].get('hips', (0, 0, 0)), (0, 0, -crouch))
    return ch

def idle(t): return legs_rest(base(t))

WALK = 1.5
def walk(t):
    # tripod gait: each leg in turn lifts and reaches forward while the other two push back
    ch = base(t, WALK); ch['rot']['body'] = (4, 0, 0)
    bob = 0
    for k, s in enumerate('RLB'):
        ph = (t / WALK - k / 3.0) % 1.0
        if ph < 1 / 3:   # swing: lift and move forward
            u = ph * 3; lift = math.sin(math.pi * u); fwd = -0.35 + 0.7 * smooth(u)
        else:            # stance: slide back under the body
            u = (ph - 1 / 3) * 1.5; lift = 0; fwd = 0.35 - 0.7 * u
        bob += lift
        ch['aim']['thigh.' + s] = add(REST['thigh.' + s], (0, -fwd * 0.55, 0.25 * lift))
        ch['aim']['foot.' + s] = add(REST['foot.' + s], (0, -fwd * 0.4, 0.5 * lift))
    ch['loc']['hips'] = (0, 0, -0.03 + 0.04 * math.sin(2 * math.pi * 3 * t / WALK))
    ch['rot']['hips'] = (0, 2 * math.sin(2 * math.pi * t / WALK), 3 * math.sin(2 * math.pi * t / WALK))
    ch['rot']['pendulum'] = (8 * math.sin(2 * math.pi * 3 * t / WALK), 4 * math.sin(2 * math.pi * t / WALK), 0)
    return ch

# Shortening Swing: the pendulum is pulled up to his right, then swings across; the tower lurches against it
SW = 62
OUT = 38   # the pendulum first tips forward out of the open front arch, so its sideways swing clears the tower
def swing_wind(t):
    u = key(t, [(0, 0), (0.85, 1.0), (1.0, 1.0)])
    ch = legs_rest(base(t), 0.08 * u, 0.12 * u)
    ch['rot']['pendulum'] = (-OUT * u, -SW * u, 0); ch['rot']['body'] = (0, 6 * u, 0)
    ch['rot']['hips'] = (0, 0, -8 * u)
    return ch
def swing(t, frm, to):
    # 0..1 s: the pendulum swings from `frm` to `to` degrees; it passes the middle at 0.5 (the strike lands then)
    u = 0.5 - 0.5 * math.cos(math.pi * min(1, t / 1.0)); a = frm + (to - frm) * u
    ch = legs_rest(base(t), 0.08, 0.12)
    ch['rot']['pendulum'] = (-OUT, a, 0); ch['rot']['body'] = (0, -a / SW * 6, 0)
    ch['rot']['hips'] = (0, 0, a / SW * 8)
    ch['loc']['hips'] = add(ch['loc']['hips'], (0, 0, -0.06 * snap(t, 0.5, 0.15, 0.3)))
    for s in 'RLB': ch['aim']['arm.' + s] = add(REST['arm.' + s], (-0.25 * a / SW, 0, 0))   # the weights trail the swing
    return ch
def swing_left(t): return swing(t, -SW, SW)
def swing_right(t): return swing(t, SW, -SW)

# Counterweight Balance: all three arms whirl their weights, then fling them outward
def whirl_dir(s, a, lift=0.55):
    out = Vector((ARMS[s][1][0], ARMS[s][1][1] - 0.1, 0)).normalized()
    side = Vector((-out.y, out.x, 0))
    return tuple((out * (0.85 + 0.25 * math.cos(a)) + side * 0.35 * math.sin(a) + Vector((0, 0, -0.45 + lift * 0.4))).normalized())
def weight_spin(t):   # 2 s loop, two whirls
    ch = legs_rest(base(t, 2.0), 0.1, 0.1)
    for k, s in enumerate('RLB'): ch['aim']['arm.' + s] = whirl_dir(s, 2 * math.pi * 2 * t / 2.0 + k * 2.1)
    ch['rot']['body'] = (0, 0, 20 * t / 2.0 * 0 + 6 * math.sin(2 * math.pi * 2 * t / 2.0))
    ch['rot']['pendulum'] = (0, 15 * math.sin(2 * math.pi * 2 * t / 2.0), 0)
    return ch
def weight_throw(t):
    T = 0.35
    ch = legs_rest(base(t), 0.1, 0.1 + 0.12 * snap(t, T, 0.1, 0.5))
    for k, s in enumerate('RLB'):
        out = Vector((ARMS[s][1][0], ARMS[s][1][1] - 0.1, 0)).normalized()
        fling = tuple((out + Vector((0, 0, 0.25))).normalized())
        ch['aim']['arm.' + s] = dirs(t, [(0, whirl_dir(s, k * 2.1)), (T, fling), (0.65, fling), (1.0, REST['arm.' + s])])
    ch['rot']['body'] = (ring(t, T, 4, 2, 4), 0, 0)
    ch['rot']['pendulum'] = (ring(t, T, 20, 1.5, 2.5), 0, 0)
    return ch

# Plumb Line: the pendulum stops dead and hangs plumb, the tower settles low on splayed legs, arms hang straight
def plumb_drop(t):
    u = key(t, [(0, 0), (0.7, 1.0), (1.2, 1.0)])
    ch = legs_rest(base(t), 0.22 * u, 0.3 * u)
    ch['rot']['pendulum'] = (ring(t, 0.7, 6, 3, 5), 0, 0); ch['rot']['body'] = (0, 0, 0)
    for s in 'RLB': ch['aim']['arm.' + s] = dirs(t, [(0, REST['arm.' + s]), (0.7, add(scale(REST['arm.' + s], 0.3), (0, 0, -1)))])
    return ch
def plumb_loop(t):
    ch = legs_rest(base(t, 2.0), 0.22, 0.3)
    ch['rot']['pendulum'] = (0, 0.6 * math.sin(2 * math.pi * 5 * t / 2.0), 0); ch['rot']['body'] = (0.3 * math.sin(2 * math.pi * t / 2.0), 0, 0)
    for s in 'RLB': ch['aim']['arm.' + s] = add(scale(REST['arm.' + s], 0.3), (0, 0, -1))
    return ch

# Return Stroke: leans in with the pendulum swung forward, runs the lane, swings back
LEAN = 18
def lean_pose(ch, u, back=False):
    sgn = -1 if back else 1
    ch['rot']['hips'] = (sgn * LEAN * u, 0, 0); ch['rot']['pendulum'] = (-sgn * 35 * u, 0, 0)
    for s in 'RLB': ch['aim']['arm.' + s] = add(REST['arm.' + s], (0, sgn * 0.6 * u, 0.15 * u))
    return ch
def charge_lean(t):
    u = key(t, [(0, 0), (0.85, 1.0), (1.0, 1.0)])
    ch = legs_rest(base(t), 0.06 * u, 0.18 * u)
    ch = lean_pose(ch, u)
    ch['aim']['thigh.B'] = add(REST['thigh.B'], (0, 0.35 * u, -0.1 * u))
    return ch
def charge(t):   # 0.8 s loop: fast skittering tripod run, leaning hard
    ch = walk(t * WALK / 0.8); ch = lean_pose(ch, 1.0)
    ch['rot']['hips'] = (LEAN + 3 * math.sin(2 * math.pi * 3 * t / 0.8), 0, 0)
    return ch
def charge_return(t):
    u = key(t, [(0, 1.0), (0.3, -1.0), (0.8, -1.0), (1.0, 0)])
    ch = legs_rest(base(t), 0.06, 0.18)
    ch['rot']['hips'] = (LEAN * u, 0, 0); ch['rot']['pendulum'] = (-35 * u, 0, 0)
    for s in 'RLB': ch['aim']['arm.' + s] = add(REST['arm.' + s], (0, 0.6 * u, 0.15 * abs(u)))
    return ch

# Return Stroke's end: he hits the arena edge (impact at 0.1), crumples forward against it and rebounds; then stays
# stuck there, leaning, trembling, the pendulum still ringing (the fight's major punish window)
def wall_slam(t):
    T = 0.1
    k = snap(t, T, 0.06, 0.5)
    lean = key(t, [(0, 1.0), (T, 1.4), (0.35, 0.7), (0.8, 0.85)])
    ch = legs_rest(base(t), 0.18 * key(t, [(0, 0.3), (0.4, 1.0)]), 0.18 + 0.2 * key(t, [(0, 0), (0.4, 1.0)]))
    ch['rot']['hips'] = (LEAN * lean, 3 * k, 0); ch['rot']['body'] = (ring(t, T, 6, 4, 4), ring(t, T, 4, 3, 4), 0)
    ch['rot']['pendulum'] = (-35 + ring(t, T, 50, 1.6, 2.5), ring(t, T, 25, 1.2, 2.5), 0)
    for s in 'RLB': ch['aim']['arm.' + s] = add(REST['arm.' + s], (0, -0.5 * k, -0.2 * k))
    return ch
def stuck_loop(t):   # 2 s loop
    p = 2 * math.pi * t / 2.0
    ch = legs_rest(base(t, 2.0), 0.18, 0.38)
    ch['rot']['hips'] = (LEAN * 0.85, 0, 0)
    ch['rot']['body'] = (0.8 * math.sin(2 * math.pi * 6 * t), 0.6 * math.sin(2 * math.pi * 5 * t), 0)
    ch['rot']['pendulum'] = (-25 + 8 * math.sin(p), 14 * math.sin(2 * p), 0)
    for s in 'RLB': ch['aim']['arm.' + s] = add(REST['arm.' + s], (0, 0.1, -0.2))
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = legs_rest(base(t), 0.1 * s, 0.1 * s)
    ch['rot']['body'] = (2 * math.sin(45 * t) * s, 0, 3 * math.sin(31 * t) * s)
    ch['rot']['pendulum'] = (-OUT * s, 70 * s * math.sin(2 * math.pi * 2.5 * t), 0)
    for s2 in 'RLB':
        out = Vector((ARMS[s2][1][0], ARMS[s2][1][1] - 0.1, 0)).normalized()
        ch['aim']['arm.' + s2] = dirs(t, [(0, REST['arm.' + s2]), (0.6, tuple((out + Vector((0, 0, 0.9))).normalized())), (1.5, tuple((out + Vector((0, 0, 0.9))).normalized())), (2.0, REST['arm.' + s2])])
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.4, 1.0), (2.5, 1.0)]); tip = key(t, [(0, 0), (0.9, 0), (2.2, 1.0), (2.5, 1.0)])
    ch = legs_rest(base(t), 0.55 * k, 0.75 * k)
    ch['rot']['hips'] = (10 * tip, -14 * tip, 0); ch['rot']['body'] = (0, 0, 0)
    ch['rot']['pendulum'] = (0, ring(t, 1.0, 30, 0.7, 1.2), 0)
    for s in 'RLB': ch['aim']['arm.' + s] = dirs(t, [(0, REST['arm.' + s]), (1.6, add(scale(REST['arm.' + s], 0.6), (0, 0, -1.2)))])
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('SwingWind', 1.0, swing_wind); rig.bake('SwingLeft', 1.0, swing_left); rig.bake('SwingRight', 1.0, swing_right)
rig.bake('WeightSpin', 2.0, weight_spin); rig.bake('WeightThrow', 1.0, weight_throw)
rig.bake('PlumbDrop', 1.2, plumb_drop); rig.bake('PlumbLoop', 2.0, plumb_loop)
rig.bake('ChargeLean', 1.0, charge_lean); rig.bake('Charge', 0.8, charge); rig.bake('ChargeReturn', 1.0, charge_return)
rig.bake('WallSlam', 0.8, wall_slam); rig.bake('StuckLoop', 2.0, stuck_loop)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.3), ('SwingWind', 1.0), ('SwingLeft', 0.5), ('WeightSpin', 0.4),
                       ('WeightThrow', 0.4), ('PlumbLoop', 0), ('ChargeLean', 1.0), ('WallSlam', 0.2), ('StuckLoop', 0.5), ('Defeat', 2.5)],
                cam=(-4.6, -7.0, 3.9), target=(0, 0, 2.1))
rig.export(work, 'pendulum_sentinel', out_glb)
