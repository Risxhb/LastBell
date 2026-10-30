# blender -b escapement_twins_prep.blend -P escapement_twins_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Escapement Twins (miniboss_prep.py ... EscapementTwins 3.8) and keys the clips in
# docs/bosses/escapement-twins.md. Axes: -Y forward, +X = the body's left, Z up.
# One tracked plinth carrying two towers: the sun tower on its right (-X) and the moon tower on its left (+X), each
# with a halo ring round its spire. Between them, the gold escape wheel (it turns in the front plane, about Y) under a
# rocker bridge that rocks tick and tock. All 269 Meshy parts are rigid and pinned whole to bones.
#
# Clips: Idle 4 s loop, Walk 1.2 s loop, FacesOut 0.8 s, PolarityLoop 1.2 s loop (the towers bow in turn, on the beat
#        at 100 BPM), WheelSpin 0.8 s, WheelLoop 2.4 s loop (four 90-degree ratchet steps, rocker tick-tock),
#        WheelReverse 0.6 s, FacesDown 0.8 s, VesicaLoop 2 s loop, AgreeAim 1.0 s, AgreeFire 0.6 s (fire at 0.1),
#        PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

TX = 1.47; GEAR = (0.0, 0.0, 1.64)
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'base': ((0, 0, 0.3), (0, 0, 1.0), 'root'),
     'tower.R': ((-TX, 0, 1.08), (-TX, 0, 3.7), 'base'), 'tower.L': ((TX, 0, 1.08), (TX, 0, 3.7), 'base'),
     'halo.R': ((-TX, 0, 3.29), (-TX, -0.5, 3.29), 'tower.R'), 'halo.L': ((TX, 0, 3.29), (TX, -0.5, 3.29), 'tower.L'),
     'wheel': (GEAR, (0, -0.6, 1.64), 'base'),
     'rocker': ((0, 0, 2.5), (0, 0, 2.9), 'base')}
rig = Rig('EscapementTwins', B)

def classify(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0); ax = abs(ctr[0]); s = 'L' if ctr[0] > 0 else 'R'
    if ctr[2] < 1.05: return 'base'
    if 1.0 < ax < 1.95 and 2.85 < ctr[2] < 3.75 and hi[1] - lo[1] < 0.15: return 'halo.' + s
    if ax >= 0.95: return 'tower.' + s
    if ax < 0.6 and ctr[1] < 0.25 and math.hypot(ctr[0], ctr[2] - GEAR[2]) < 0.6 and hi[2] < 2.25: return 'wheel'
    if ctr[2] > 1.85: return 'rocker'
    return 'base'
rig.assign(classify)

def base_pose(t, per=4.0):
    p = 2 * math.pi * t / per
    return {'rot': {'tower.R': (0.8 * math.sin(p), 0, 0), 'tower.L': (0.8 * math.sin(p + math.pi), 0, 0),
                    'halo.R': (0, 90 * t / per, 0), 'halo.L': (0, -90 * t / per, 0),
                    'wheel': (0, 45 * math.floor(t / (per / 4)) + 45 * smooth(((t % (per / 4)) / (per / 4)) / 0.15), 0) if per == 4.0 else (0, 0, 0),
                    'rocker': (0, 4 * math.sin(p * 2), 0)},
            'loc': {'base': (0, 0, 0.01 * math.sin(p))}}
# the idle wheel: one 45-degree tick per second, 4 per loop = 180 degrees; the gear's 16 teeth make that seamless

def idle(t): return base_pose(t)

def walk(t):   # the tracks roll: the plinth bobs on its treads, the towers sway against the motion
    p = 2 * math.pi * t / 1.2
    ch = base_pose(t, 1.2)
    ch['loc']['base'] = (0, 0, 0.025 * abs(math.sin(p)))
    ch['rot']['base'] = (1.5 * math.sin(2 * p), 0, 0)
    ch['rot']['tower.R'] = (-3 + 2 * math.sin(p), 1.5 * math.sin(p), 0); ch['rot']['tower.L'] = (-3 + 2 * math.sin(p + 1), 1.5 * math.sin(p + 1), 0)
    return ch

OUT_Z, OUT_Y = 50, 10
def faces_out(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    ch = base_pose(t)
    ch['rot']['tower.R'] = (0, -OUT_Y * u, -OUT_Z * u); ch['rot']['tower.L'] = (0, OUT_Y * u, OUT_Z * u)
    ch['rot']['halo.R'] = (0, 120 * smooth(t / 0.8), 0); ch['rot']['halo.L'] = (0, -120 * smooth(t / 0.8), 0)
    return ch
def polarity_loop(t):
    # tick on the right (sun) tower at 0, tock on the left (moon) at 0.6: each bows sharply toward the floor and back
    ch = base_pose(t, 1.2)
    bR = snap(t, 0.08, 0.08, 0.4) + snap(t, 1.28, 0.08, 0.4); bL = snap(t, 0.68, 0.08, 0.4)
    ch['rot']['tower.R'] = (14 * bR, -OUT_Y, -OUT_Z); ch['rot']['tower.L'] = (14 * bL, OUT_Y, OUT_Z)
    ch['rot']['halo.R'] = (0, 180 * t / 1.2, 0); ch['rot']['halo.L'] = (0, -180 * t / 1.2, 0)
    ch['rot']['rocker'] = (0, 12 * (bL - bR), 0)
    return ch

def ratchet(t, per, steps, step_deg):
    # step_deg every per/steps seconds, each step snapping in over the first 15% of its slot
    slot = per / steps; n = math.floor(t / slot); u = ((t - n * slot) / slot) / 0.15
    return step_deg * (n + smooth(u))
def wheel_spin(t):
    u = key(t, [(0, 0), (0.6, 1.0), (0.8, 1.0)])
    ch = base_pose(t)
    ch['rot']['rocker'] = (0, 0, 0); ch['loc']['rocker'] = (0, 0, -0.08 * u)
    ch['rot']['wheel'] = (0, 180 * smooth(t / 0.8) ** 2, 0)
    ch['rot']['tower.R'] = (-4 * u, 3 * u, 0); ch['rot']['tower.L'] = (-4 * u, -3 * u, 0)
    return ch
def wheel_loop(t, sign=1):
    ch = base_pose(t, 2.4)
    ch['rot']['wheel'] = (0, sign * ratchet(t, 2.4, 4, 90), 0)
    beat = math.floor(t / 0.6); u = (t - beat * 0.6) / 0.6
    side = 1 if beat % 2 == 0 else -1
    ch['rot']['rocker'] = (0, side * 12 * (1 - smooth(u / 0.5)) - side * 12 * smooth((u - 0.5) / 0.5), 0)
    ch['loc']['rocker'] = (0, 0, -0.08)
    ch['rot']['tower.R'] = (-4 + 3 * snap(u, 0.05, 0.04, 0.3) * (side > 0), 3, 0); ch['rot']['tower.L'] = (-4 + 3 * snap(u, 0.05, 0.04, 0.3) * (side < 0), -3, 0)
    return ch
def wheel_reverse(t):
    ch = base_pose(t)
    ch['rot']['wheel'] = (0, ring(t, 0.05, -25, 2.5, 4), 0)
    ch['rot']['rocker'] = (0, ring(t, 0.02, 28, 2.0, 3.5), 0); ch['loc']['rocker'] = (0, 0, -0.08)
    ch['rot']['tower.R'] = (-4 + ring(t, 0.05, 5, 3, 4), 3, 0); ch['rot']['tower.L'] = (-4 - ring(t, 0.05, 5, 3, 4), -3, 0)
    return ch

DOWN_X, DOWN_Z = 24, 14
def faces_down(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    ch = base_pose(t)
    ch['rot']['tower.R'] = (DOWN_X * u, 0, DOWN_Z * u); ch['rot']['tower.L'] = (DOWN_X * u, 0, -DOWN_Z * u)
    return ch
def vesica_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base_pose(t, 2.0)
    ch['rot']['tower.R'] = (DOWN_X + 2 * math.sin(p), 0, DOWN_Z); ch['rot']['tower.L'] = (DOWN_X + 2 * math.sin(p + math.pi), 0, -DOWN_Z)
    ch['rot']['halo.R'] = (0, 180 * t / 2.0, 0); ch['rot']['halo.L'] = (0, -180 * t / 2.0, 0)
    return ch

AIM_Z = 18
def agree_aim(t):
    u = key(t, [(0, 0), (0.8, 1.0), (1.0, 1.0)])
    ch = base_pose(t)
    ch['rot']['tower.R'] = (8 * u, 4 * u, AIM_Z * u); ch['rot']['tower.L'] = (8 * u, -4 * u, -AIM_Z * u)
    ch['rot']['halo.R'] = (0, 360 * smooth(t / 1.0) ** 2, 0); ch['rot']['halo.L'] = (0, -360 * smooth(t / 1.0) ** 2, 0)
    ch['rot']['wheel'] = (0, 0, 0)
    return ch
def agree_fire(t):
    T = 0.1
    k = snap(t, T, 0.05, 0.45)
    ch = base_pose(t)
    ch['rot']['tower.R'] = (8 - 22 * k, 4, AIM_Z * (1 - 0.3 * k)); ch['rot']['tower.L'] = (8 - 22 * k, -4, -AIM_Z * (1 - 0.3 * k))
    ch['loc']['base'] = (0, 0.12 * k, 0); ch['rot']['wheel'] = (0, ring(t, T, 20, 3, 4), 0)
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0.6)])
    ch = base_pose(t)
    ch['rot']['tower.R'] = (2 * math.sin(41 * t) * s, -14 * s, 0); ch['rot']['tower.L'] = (2 * math.sin(37 * t) * s, 14 * s, 0)
    ch['loc']['rocker'] = (0, 0, 0.18 * s); ch['rot']['rocker'] = (0, 6 * math.sin(23 * t) * s, 0)
    ch['rot']['wheel'] = (0, 1080 * smooth(t / 2.0), 0)
    ch['rot']['halo.R'] = (0, 720 * smooth(t / 2.0), 0); ch['rot']['halo.L'] = (0, -720 * smooth(t / 2.0), 0)
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.6, 1.0), (2.5, 1.0)])
    ch = {'rot': {'tower.R': (20 * k, -12 * k, 6 * k), 'tower.L': (16 * k, 16 * k, -4 * k), 'rocker': (0, 18 * k, 0),
                  'wheel': (0, ring(t, 0.2, 30, 0.8, 1.6) + 10 * k, 0), 'halo.R': (40 * k, 0, 0), 'halo.L': (-30 * k, 0, 0),
                  'base': (3 * k, 2 * k, 0)},
          'loc': {'base': (0, 0, -0.06 * k), 'rocker': (0, 0, -0.1 * k)}}
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', 1.2, walk)
rig.bake('FacesOut', 0.8, faces_out); rig.bake('PolarityLoop', 1.2, polarity_loop)
rig.bake('WheelSpin', 0.8, wheel_spin); rig.bake('WheelLoop', 2.4, wheel_loop); rig.bake('WheelReverse', 0.6, wheel_reverse)
rig.bake('FacesDown', 0.8, faces_down); rig.bake('VesicaLoop', 2.0, vesica_loop)
rig.bake('AgreeAim', 1.0, agree_aim); rig.bake('AgreeFire', 0.6, agree_fire)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('FacesOut', 0.8), ('PolarityLoop', 0.1), ('WheelLoop', 0.3), ('WheelReverse', 0.1),
                       ('VesicaLoop', 0), ('AgreeAim', 1.0), ('AgreeFire', 0.15), ('PhaseShift', 1.0), ('Defeat', 2.5)],
                cam=(-5.5, -9.0, 4.6), target=(0, 0, 1.7))
rig.export(work, 'escapement_twins', out_glb)
