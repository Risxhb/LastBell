# blender -b briar_metronome_prep.blend -P briar_metronome_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Briar Metronome (miniboss_prep.py ... BriarMetronome 3.4) and keys the clips in
# docs/bosses/briar-metronome.md. Axes: -Y forward, +X = its left, Z up.
# A tall figure of brass roots under a moss mantle, with an ivory mask, a metronome pendulum in its open chest, a long
# curved pruning blade in its right hand (-X) and an orb on a short chain in its left (+X).
# Each leg is one Meshy piece from hip to foot and the mantle is one sheet, so the body is skinned by distance to the
# bones (Rig.distance_weights; heat weighting fails on the thin roots); the blade, the orb and the chest pendulum are
# pinned rigid to their own bones.
#
# Clips: Idle 4 s loop, Walk 1.6 s loop, CutIn 1.2 s / CutOut 1.2 s / CutAcross 1.2 s (tell held to 0.7, strike 0.75),
#        BladePlant 1.0 s (plant 0.6), PlantLoop 2 s loop, OrbWhirl 0.8 s, WhirlLoop 1.0 s loop, OrbThrow 1.0 s
#        (release 0.35), MantleShake 1.2 s, PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

CX = -0.02
SH_R, EL_R, WR_R, TIP = (-0.45, 0.0, 2.72), (-0.60, -0.08, 2.38), (-0.80, -0.22, 2.0), (-1.30, -0.92, 0.35)
SH_L, EL_L, WR_L, ORB = (0.43, 0.05, 2.67), (0.70, 0.06, 1.98), (1.06, -0.09, 1.47), (1.31, -0.08, 1.0)
HIP_R, KNEE_R, ANK_R, TOE_R = (-0.40, -0.02, 1.90), (-0.57, -0.06, 1.23), (-0.73, -0.05, 0.22), (-0.77, -0.35, 0.02)
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'hips': ((CX, 0.05, 1.80), (CX, 0.05, 2.2), 'root'),
     'chest': ((CX, 0.05, 2.2), (CX, 0.0, 2.75), 'hips'),
     'head': ((-0.05, -0.12, 2.75), (-0.05, -0.25, 3.35), 'chest'),
     'pendulum': ((0.0, -0.11, 2.62), (0.0, -0.11, 1.97), 'chest'),
     'upperarm.R': (SH_R, EL_R, 'chest'), 'forearm.R': (EL_R, WR_R, 'upperarm.R'), 'blade': (WR_R, TIP, 'forearm.R'),
     'upperarm.L': (SH_L, EL_L, 'chest'), 'forearm.L': (EL_L, WR_L, 'upperarm.L'), 'orb': (WR_L, ORB, 'forearm.L')}
for s in 'RL':
    f = (lambda p: p) if s == 'R' else (lambda p: mirror(p, CX))
    B['thigh.' + s] = (f(HIP_R), f(KNEE_R), 'hips'); B['shin.' + s] = (f(KNEE_R), f(ANK_R), 'thigh.' + s); B['foot.' + s] = (f(ANK_R), f(TOE_R), 'shin.' + s)
AIM = ('upperarm.R', 'forearm.R', 'blade', 'upperarm.L', 'forearm.L', 'orb')
rig = Rig('BriarMetronome', B, AIM)

def pin(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0)
    if ctr[2] > 0.3 and (ctr[0] < -0.84 or (hi[0] < -0.7 and lo[2] < 1.0 and ctr[1] < -0.25)): return 'blade'   # not the toe roots
    if ctr[0] > 1.0 and ctr[2] < 1.55: return 'orb'
    if abs(ctr[0]) < 0.06 and -0.15 < ctr[1] < -0.05 and 1.9 < ctr[2] < 2.65: return 'pendulum'
    if ctr[2] > 2.6 and abs(ctr[0]) < 0.3 and ctr[1] < -0.05 and lo[2] > 2.5: return 'head'
    return None
rig.distance_weights(pin=pin, rigid=('blade', 'orb', 'pendulum'), bias={'chest': 0.12, 'hips': 0.05})   # heat weighting fails on the thin roots

REST = {n: tuple(rig.rest_dir[n]) for n in B}
def tick(t, per=0.555):   # the chest pendulum swings on its own 108 BPM beat; the loops below use whole beats
    return (0, 22 * math.sin(math.pi * t / per), 0)
def base(t, per=4.0, beats=None):
    p = 2 * math.pi * t / per; br = math.sin(p)
    beat = per / (beats or round(per / 0.555))
    return {'rot': {'chest': (-1.0 * br, 0, 0), 'head': (2 * math.sin(p + 0.7), 0, 5 * math.sin(p * 0.5)), 'pendulum': tick(t, beat)},
            'loc': {'hips': (0, 0, 0.012 * br)},
            'aim': {n: REST[n] for n in AIM}}
def crouch(ch, c):
    ch['loc']['hips'] = add(ch['loc'].get('hips', (0, 0, 0)), (0, 0, -0.2 * c))
    for s in 'RL':
        ch['rot']['thigh.' + s] = (-14 * c, 0, 0); ch['rot']['shin.' + s] = (24 * c, 0, 0); ch['rot']['foot.' + s] = (-10 * c, 0, 0)
    return ch
def idle(t): return base(t, 4.0, 8)   # 8 swings of the chest pendulum per loop (seamless)

WALK = 1.6
def walk(t):
    ph = 2 * math.pi * t / WALK; s = math.sin(ph); lift = lambda q: max(0.0, math.sin(q))
    ch = base(t, WALK, 2)
    ch['rot'].update({'thigh.R': (-18 * s, 0, 0), 'shin.R': (22 * lift(ph + math.pi), 0, 0), 'foot.R': (8 * s, 0, 0),
                      'thigh.L': (18 * s, 0, 0), 'shin.L': (22 * lift(ph), 0, 0), 'foot.L': (-8 * s, 0, 0),
                      'hips': (0, 2 * s, 5 * s), 'chest': (6, -2 * s, -6 * s)})
    ch['loc']['hips'] = (0, 0, -0.04 + 0.05 * abs(math.cos(ph)))
    ch['aim']['upperarm.R'] = add(REST['upperarm.R'], (0, 0.15 * s, 0)); ch['aim']['upperarm.L'] = add(REST['upperarm.L'], (0, -0.15 * s, 0))
    ch['aim']['orb'] = add(REST['orb'], (0, 0.25 * s, 0))
    return ch

# Pruning Cuts. Each: the tell pose by 0.55 (held to 0.7), the strike at 0.75, follow-through to 1.2.
T_TELL, T_HIT = 0.55, 0.75
def cut(t, tell, strike, twist_tell, twist_hit, crouch_hit=0.0):
    # tell / strike: (upperarm, forearm, blade) directions; twist: chest turn (deg, + toward its left)
    u = key(t, [(0, 0), (T_TELL, 1.0), (0.7, 1.0)]); h = key(t, [(0.7, 0), (T_HIT, 1.0), (1.0, 1.0), (1.2, 0.6)])
    ch = crouch(base(t), 0.3 * u + crouch_hit * h)
    for n, a, b in zip(('upperarm.R', 'forearm.R', 'blade'), tell, strike):
        ch['aim'][n] = nrm(add(scale(REST[n], (1 - u) * (1 - h)), scale(a, u * (1 - h)), scale(b, h)))
    ch['rot']['chest'] = (4 * u, 0, twist_tell * u * (1 - h) + twist_hit * h)
    ch['rot']['hips'] = (0, 0, 0.4 * (twist_tell * u * (1 - h) + twist_hit * h))
    return ch
IN_TELL = ((-0.2, -0.6, -0.75), (0.75, -0.6, -0.1), (0.95, -0.2, 0.2))        # blade pulled in across the chest
IN_HIT = ((-0.95, -0.25, -0.1), (-0.98, 0.1, 0.0), (-0.9, 0.4, -0.15))       # flung wide, reaching far
OUT_TELL = ((-0.6, 0.6, -0.5), (-0.6, 0.75, -0.2), (-0.4, 0.85, -0.35))       # swept back wide and low
OUT_HIT = ((-0.7, -0.6, -0.35), (0.2, -0.95, -0.2), (0.8, -0.5, -0.35))       # a close low sweep round the front
ACROSS_TELL = ((-0.35, 0.1, 0.93), (-0.1, 0.2, 0.97), (0.1, 0.5, 0.86))       # raised high
ACROSS_HIT = ((-0.2, -0.85, -0.5), (0.15, -0.9, -0.4), (0.35, -0.75, -0.55))  # brought down across the front
def cut_in(t): return cut(t, IN_TELL, IN_HIT, 35, -50)
def cut_out(t): return cut(t, OUT_TELL, OUT_HIT, -40, 60, 0.4)
def cut_across(t): return cut(t, ACROSS_TELL, ACROSS_HIT, -10, 25, 0.5)

# Overgrowth: the blade driven point-down into the floor in front of it, then leant on
PL = ((-0.25, -0.75, -0.6), (-0.05, -0.8, -0.6), (0.05, -0.25, -0.97))
def blade_plant(t):
    T = 0.6
    u = key(t, [(0, 0), (0.4, -0.5), (T, 1.0), (1.0, 1.0)]); up = max(0, -u); dn = max(0, u)
    ch = crouch(base(t), 0.8 * dn)
    for n, d, a in zip(('upperarm.R', 'forearm.R', 'blade'), PL, ACROSS_TELL):
        ch['aim'][n] = nrm(add(scale(REST[n], 1 - up - dn), scale(a, up), scale(d, dn)))
    ch['rot']['chest'] = (18 * dn + ring(t, T, 4, 3, 4), 0, 10 * dn)
    return ch
def plant_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = crouch(base(t, 2.0, 4), 0.8)
    for n, d in zip(('upperarm.R', 'forearm.R', 'blade'), PL): ch['aim'][n] = d
    ch['rot']['chest'] = (18 + 1.5 * math.sin(p), 0, 10); ch['rot']['head'] = (8, 0, 6 * math.sin(p))
    return ch

# Weight of Seasons: the orb whirled overhead on its chain, faster, then hurled forward
def whirl(t, rate):
    a = 2 * math.pi * rate * t
    return {'upperarm.L': (0.45, -0.05, 0.89), 'forearm.L': (0.15 + 0.12 * math.cos(a), 0.12 * math.sin(a), 0.98),
            'orb': (math.cos(a), math.sin(a), 0.15)}
def orb_whirl(t):
    u = key(t, [(0, 0), (0.6, 1.0), (0.8, 1.0)])
    ch = base(t); w = whirl(t, 1.5 * u)
    for n in ('upperarm.L', 'forearm.L', 'orb'): ch['aim'][n] = nrm(add(scale(REST[n], 1 - u), scale(w[n], u)))
    ch['rot']['chest'] = (-6 * u, 0, -8 * u); ch['rot']['head'] = (-10 * u, 0, 0)
    return ch
def whirl_loop(t):
    ch = base(t, 1.0, 2); w = whirl(t, 2.0)
    for n in ('upperarm.L', 'forearm.L', 'orb'): ch['aim'][n] = w[n]
    ch['rot']['chest'] = (-6, 0, -8 + 3 * math.sin(2 * math.pi * 2 * t)); ch['rot']['head'] = (-10, 0, 0)
    return ch
def orb_throw(t):
    T = 0.35
    ch = crouch(base(t), 0.4 * key(t, [(0, 0), (T, 1.0), (1.0, 0.3)]))
    w = whirl(0.0, 2.0)
    fling = {'upperarm.L': (0.3, -0.9, 0.3), 'forearm.L': (0.1, -0.98, 0.1), 'orb': (0.0, -0.98, -0.15)}
    for n in ('upperarm.L', 'forearm.L', 'orb'): ch['aim'][n] = dirs(t, [(0, w[n]), (T, fling[n]), (0.6, fling[n]), (1.0, REST[n])])
    ch['rot']['chest'] = (key(t, [(0, -6), (T, 14), (1.0, 4)]), 0, key(t, [(0, -8), (T, 22), (1.0, 0)]))
    return ch

# Lost Seasons: the mantle shaken, petals falling (a shiver from the shoulders down)
def mantle_shake(t):
    s = key(t, [(0, 0), (0.2, 1.0), (0.9, 1.0), (1.2, 0)])
    ch = base(t)
    ch['rot']['chest'] = (4 * s * math.sin(2 * math.pi * 7 * t), 3 * s * math.sin(2 * math.pi * 9 * t), 5 * s * math.sin(2 * math.pi * 6 * t))
    ch['rot']['head'] = (-10 * s, 0, 4 * s * math.sin(2 * math.pi * 5 * t))
    ch['aim']['upperarm.R'] = add(REST['upperarm.R'], (-0.3 * s, 0, 0.3 * s)); ch['aim']['upperarm.L'] = add(REST['upperarm.L'], (0.3 * s, 0, 0.3 * s))
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = base(t)
    ch['rot']['chest'] = (-14 * s + 2 * math.sin(40 * t) * s, 0, 0); ch['rot']['head'] = (-20 * s, 0, 0)
    ch['rot']['pendulum'] = (0, 45 * s * math.sin(2 * math.pi * 3 * t), 0)
    for n, d in (('upperarm.R', (-0.9, 0.0, 0.45)), ('forearm.R', (-0.8, -0.1, 0.6)), ('upperarm.L', (0.9, 0.0, 0.45)), ('forearm.L', (0.8, -0.1, 0.6))):
        ch['aim'][n] = dirs(t, [(0, REST[n]), (0.6, d), (1.5, d), (2.0, REST[n])])
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.3, 1.0), (2.5, 1.0)]); slump = key(t, [(0, 0), (0.8, 0), (2.1, 1.0), (2.5, 1.0)])
    ch = crouch(base(t), 1.7 * k)
    ch['rot']['chest'] = (28 * slump, 0, -8 * slump); ch['rot']['head'] = (24 * slump, 0, 0)
    ch['rot']['pendulum'] = (0, ring(t, 0.3, 25, 1.0, 1.2), 0)
    for n, d in (('upperarm.R', (-0.3, -0.3, -0.9)), ('forearm.R', (-0.2, -0.5, -0.85)), ('blade', (-0.55, -0.83, -0.05)),
                 ('upperarm.L', (0.3, -0.3, -0.9)), ('forearm.L', (0.2, -0.5, -0.85)), ('orb', (0.1, -0.3, -0.95))):
        ch['aim'][n] = dirs(t, [(0, REST[n]), (2.0, d)])
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('CutIn', 1.2, cut_in); rig.bake('CutOut', 1.2, cut_out); rig.bake('CutAcross', 1.2, cut_across)
rig.bake('BladePlant', 1.0, blade_plant); rig.bake('PlantLoop', 2.0, plant_loop)
rig.bake('OrbWhirl', 0.8, orb_whirl); rig.bake('WhirlLoop', 1.0, whirl_loop); rig.bake('OrbThrow', 1.0, orb_throw)
rig.bake('MantleShake', 1.2, mantle_shake); rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.4), ('CutIn', 0.6), ('CutIn', 0.8), ('CutOut', 0.6),
                       ('CutAcross', 0.6), ('PlantLoop', 0), ('WhirlLoop', 0.2), ('OrbThrow', 0.4), ('Defeat', 2.5)],
                cam=(-4.6, -7.6, 3.8), target=(0, -0.2, 1.7))
rig.export(work, 'briar_metronome', out_glb)
