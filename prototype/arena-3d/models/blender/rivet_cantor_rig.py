# blender -b rivet_cantor_prep.blend -P rivet_cantor_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Rivet Cantor (miniboss_prep.py ... RivetCantor 3.2) and keys the clips in docs/bosses/rivet-cantor.md.
# Axes: -Y forward, +X = his left, Z up. He stands hunched forward: the mask sits low in front of a domed back.
# Right hand (-X): the round drum-mallet. Left hand (+X): the two-pronged tuning fork, held low and forward.
# Meshy fused the whole body, drum and fork into one shell (7,126 of the verts), so this rig uses heat weights on a
# voxel proxy (miniboss_rig_lib.Rig.heat_weights, as aurelion_rig.py). The hand bones run into the drum and the fork so
# both props follow the hands; the few small loose rivets are pinned to the nearest bone region.
#
# Clips: Idle 4 s loop, Walk 1.4 s loop, ForkStrike 1.0 s (strike 0.55), DialLoop 2 s loop, HammerRaise 0.8 s,
#        HammerLoop 1.0 s loop (two blows, at 0.25 and 0.75), DrumSlam 1.4 s (impact 0.8), ForkPlant 1.2 s (plant 0.8),
#        ForkLoop 2 s loop, PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

SH_R, EL_R, WR_R, DRUM = (-1.10, 0.16, 2.15), (-1.32, -0.14, 1.66), (-1.42, -0.58, 1.30), (-1.62, -1.15, 0.95)
SH_L, EL_L, WR_L, FORK = (1.10, 0.16, 2.15), (1.22, -0.14, 1.70), (1.30, -0.61, 1.23), (1.62, -1.40, 0.48)
HIP_R, KNEE_R, ANK_R, TOE_R = (-0.68, 0.15, 1.30), (-0.72, 0.02, 0.58), (-0.74, -0.08, 0.20), (-0.76, -0.45, 0.02)
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'hips': ((0, 0.1, 1.15), (0, 0.1, 1.55), 'root'),
     'spine': ((0, 0.1, 1.55), (0, 0.05, 2.05), 'hips'),
     'chest': ((0, 0.05, 2.05), (0, 0.0, 2.75), 'spine'),
     'head': ((0, -0.22, 2.38), (0, -0.32, 2.95), 'chest'),
     'upperarm.R': (SH_R, EL_R, 'chest'), 'forearm.R': (EL_R, WR_R, 'upperarm.R'), 'hand.R': (WR_R, DRUM, 'forearm.R'),
     'upperarm.L': (SH_L, EL_L, 'chest'), 'forearm.L': (EL_L, WR_L, 'upperarm.L'), 'hand.L': (WR_L, FORK, 'forearm.L')}
for s, sg in (('R', 1), ('L', -1)):
    f = (lambda p: p) if s == 'R' else (lambda p: mirror(p, -0.04))
    B['thigh.' + s] = (f(HIP_R), f(KNEE_R), 'hips'); B['shin.' + s] = (f(KNEE_R), f(ANK_R), 'thigh.' + s)
    B['foot.' + s] = (f(ANK_R), f(TOE_R), 'shin.' + s)
AIM = ('upperarm.R', 'forearm.R', 'hand.R', 'upperarm.L', 'forearm.L', 'hand.L')
rig = Rig('RivetCantor', B, AIM)

def pin(c):
    # small loose rivets and bolts: follow whatever bone region they sit in
    ctr = c.mean(0)
    if ctr[2] > 2.3 and abs(ctr[0]) < 0.4: return 'head'
    if ctr[0] > 1.15 and ctr[2] < 1.9: return 'hand.L' if ctr[2] < 1.25 else 'forearm.L'
    if ctr[0] < -1.15 and ctr[2] < 1.9: return 'hand.R' if ctr[2] < 1.3 else 'forearm.R'
    if ctr[2] < 1.15 and abs(ctr[0]) > 0.35: s = 'L' if ctr[0] > 0 else 'R'; return ('shin.' if ctr[2] > 0.25 else 'foot.') + s
    return None
rig.heat_weights(pin=pin, voxel=0.03, fallback='chest')
# the thin ribbon trailing from the drum to the floor belongs to the drum, not the legs it touches
RIBBON = lambda co: (co[:, 2] < 0.85) & ((co[:, 0] < -0.97) | ((co[:, 0] < -0.86) & (co[:, 1] < -0.5)))
rig.override(RIBBON, 'hand.R')
rig.cut_bridges(RIBBON, ('foot.R', 'shin.R', 'thigh.R'))

REST = {n: tuple(rig.rest_dir[n]) for n in B}
def base(t, per=4.0):
    p = 2 * math.pi * t / per; br = math.sin(p)
    return {'rot': {'chest': (-1.2 * br, 0, 0), 'head': (2 * math.sin(p + 0.8), 0, 4 * math.sin(p * 0.5))},
            'loc': {'hips': (0, 0, 0.012 * br)},
            'aim': {'upperarm.R': add(REST['upperarm.R'], (0, 0.03 * br, 0)), 'forearm.R': REST['forearm.R'], 'hand.R': REST['hand.R'],
                    'upperarm.L': add(REST['upperarm.L'], (0, 0.03 * br, 0)), 'forearm.L': REST['forearm.L'], 'hand.L': REST['hand.L']}}
def crouch(ch, c):
    ch['loc']['hips'] = add(ch['loc'].get('hips', (0, 0, 0)), (0, 0, -0.16 * c))
    for s in 'RL':
        ch['rot']['thigh.' + s] = (-16 * c, 0, 0); ch['rot']['shin.' + s] = (26 * c, 0, 0); ch['rot']['foot.' + s] = (-10 * c, 0, 0)
    return ch

def idle(t): return base(t)

WALK = 1.4
def walk(t):
    ph = 2 * math.pi * t / WALK; s = math.sin(ph); lift = lambda q: max(0.0, math.sin(q))
    ch = base(t, WALK)
    ch['rot'].update({'thigh.R': (-20 * s, 0, 0), 'shin.R': (24 * lift(ph + math.pi), 0, 0), 'foot.R': (8 * s, 0, 0),
                      'thigh.L': (20 * s, 0, 0), 'shin.L': (24 * lift(ph), 0, 0), 'foot.L': (-8 * s, 0, 0),
                      'hips': (0, 3 * s, 4 * s), 'chest': (5, -3 * s, -5 * s)})
    ch['loc']['hips'] = (0, 0, -0.05 + 0.07 * abs(math.cos(ph)))
    ch['aim']['upperarm.R'] = add(REST['upperarm.R'], (0, 0.18 * s, 0)); ch['aim']['upperarm.L'] = add(REST['upperarm.L'], (0, -0.18 * s, 0))
    return ch

# Pitch Match: the fork is swung in and struck on the drum held before his chest, then raised beside his head
FS_UR, FS_FR, FS_HR = (-0.3, -0.85, -0.42), (0.55, -0.83, 0.05), (0.35, -0.9, 0.25)
FS_UL, FS_FL, FS_HL = (0.25, -0.9, -0.35), (-0.55, -0.82, 0.12), (-0.25, -0.45, 0.86)
UP_UL, UP_FL, UP_HL = (0.65, -0.15, 0.75), (0.12, -0.3, 0.95), (0.02, -0.12, 1.0)
def fork_strike(t):
    T = 0.55
    ch = base(t)
    ch['aim'].update({'upperarm.R': dirs(t, [(0, REST['upperarm.R']), (0.45, FS_UR)]), 'forearm.R': dirs(t, [(0, REST['forearm.R']), (0.45, FS_FR)]),
                      'hand.R': dirs(t, [(0, REST['hand.R']), (0.45, FS_HR)]),
                      'upperarm.L': dirs(t, [(0, REST['upperarm.L']), (0.35, (0.7, 0.1, 0.4)), (T, FS_UL), (1.0, UP_UL)]),
                      'forearm.L': dirs(t, [(0, REST['forearm.L']), (0.35, (0.5, 0.2, 0.8)), (T, FS_FL), (1.0, UP_FL)]),
                      'hand.L': dirs(t, [(0, REST['hand.L']), (0.35, (0.2, 0.3, 0.93)), (T, FS_HL), (1.0, UP_HL)])})
    ch['rot']['chest'] = (-6 * key(t, [(0, 0), (T, 1.0), (1.0, 0.6)]) + ring(t, T, 3, 6, 5), 0, 8 * key(t, [(0, 0), (T, -1), (1.0, 0)]))
    ch['rot']['head'] = (-8 * key(t, [(0, 0), (1.0, 1.0)]), 0, 0)
    return ch
def dial_loop(t):   # fork held up beside his head, chest out, the whole body faintly trembling with the note
    p = 2 * math.pi * t / 2.0; tr = 0.6 * math.sin(2 * math.pi * 9 * t)
    ch = base(t, 2.0)
    ch['rot']['chest'] = (-6 + tr, 0, 0); ch['rot']['head'] = (-8, 0, 3 * math.sin(p))
    ch['aim'].update({'upperarm.L': UP_UL, 'forearm.L': add(UP_FL, (0.01 * math.sin(2 * math.pi * 9 * t), 0, 0)), 'hand.L': UP_HL,
                      'upperarm.R': add(REST['upperarm.R'], (0, -0.2, 0.1)), 'forearm.R': add(REST['forearm.R'], (0.1, -0.3, 0.2)), 'hand.R': add(REST['hand.R'], (0.2, -0.1, 0.4))})
    return ch

# Riveting: the drum-mallet raised overhead, then hammered down in front, twice per loop
OVER_U, OVER_F, OVER_H = (-0.35, 0.05, 0.94), (-0.05, 0.25, 0.97), (0.05, 0.45, 0.89)
DOWN_U, DOWN_F, DOWN_H = (-0.3, -0.8, -0.5), (-0.05, -0.92, -0.38), (0.05, -0.55, -0.83)
def hammer_raise(t):
    ch = base(t)
    ch['aim'].update({'upperarm.R': dirs(t, [(0, REST['upperarm.R']), (0.7, OVER_U)]), 'forearm.R': dirs(t, [(0, REST['forearm.R']), (0.7, OVER_F)]),
                      'hand.R': dirs(t, [(0, REST['hand.R']), (0.7, OVER_H)])})
    ch['rot']['chest'] = (-8 * key(t, [(0, 0), (0.7, 1.0)]), 0, -6 * key(t, [(0, 0), (0.7, 1.0)]))
    return ch
def hammer_loop(t):
    ph = (t % 0.5) / 0.5; T = 0.35   # each half: up, then down hard at 35%, rebound
    blow = key(ph, [(0, 0), (T, 1.0), (0.6, 0.85), (1.0, 0)])
    ch = crouch(base(t, 1.0), 0.4)
    ch['aim'].update({'upperarm.R': nrm(add(scale(OVER_U, 1 - blow), scale(DOWN_U, blow))), 'forearm.R': nrm(add(scale(OVER_F, 1 - blow), scale(DOWN_F, blow))),
                      'hand.R': nrm(add(scale(OVER_H, 1 - blow), scale(DOWN_H, blow)))})
    ch['rot']['chest'] = (-8 + 18 * blow, 0, -6 + 4 * blow); ch['rot']['head'] = (6 * snap(ph, T, 0.03, 0.2), 0, 0)
    return ch

# The Great Cast: drum hauled overhead, then slammed onto the floor in front; he drops into a crouch
def drum_slam(t):
    T = 0.8
    blow = key(t, [(0, 0), (0.55, -1.0), (T, 1.0), (1.4, 0.8)])
    up = max(0, -blow); dn = max(0, blow)
    ch = crouch(base(t), 0.9 * dn + 0.15 * up)
    ch['aim'].update({'upperarm.R': nrm(add(scale(REST['upperarm.R'], 1 - up - dn), scale(OVER_U, up), scale((-0.25, -0.75, -0.6), dn))),
                      'forearm.R': nrm(add(scale(REST['forearm.R'], 1 - up - dn), scale(OVER_F, up), scale((0.0, -0.85, -0.52), dn))),
                      'hand.R': nrm(add(scale(REST['hand.R'], 1 - up - dn), scale(OVER_H, up), scale((0.1, -0.35, -0.93), dn))),
                      'upperarm.L': add(REST['upperarm.L'], (0.25 * up, 0, 0.2 * up)), 'forearm.L': add(REST['forearm.L'], (0.25 * up, 0.2 * up, 0))})
    ch['rot']['chest'] = (-12 * up + 22 * dn + ring(t, T, 3, 5, 5), 0, -8 * up)
    ch['rot']['head'] = (8 * dn, 0, 0)
    return ch

# Standing Wave: the fork driven prongs-down into the floor in front of him and held there while he hums
PL_U, PL_F, PL_H = (0.2, -0.85, -0.5), (-0.12, -0.75, -0.65), (-0.02, -0.25, -0.97)
def fork_plant(t):
    T = 0.8
    u = key(t, [(0, 0), (0.5, -0.4), (T, 1.0), (1.2, 1.0)]); up = max(0, -u); dn = max(0, u)
    ch = crouch(base(t), 0.75 * dn)
    ch['aim'].update({'upperarm.L': nrm(add(scale(REST['upperarm.L'], 1 - dn - up), scale(UP_UL, up), scale(PL_U, dn))),
                      'forearm.L': nrm(add(scale(REST['forearm.L'], 1 - dn - up), scale(UP_FL, up), scale(PL_F, dn))),
                      'hand.L': nrm(add(scale(REST['hand.L'], 1 - dn - up), scale(UP_HL, up), scale(PL_H, dn))),
                      'upperarm.R': add(REST['upperarm.R'], (0.1 * dn, -0.2 * dn, 0.1 * dn))})
    ch['rot']['chest'] = (18 * dn - 6 * up + ring(t, T, 4, 4, 4), 0, 6 * dn)
    return ch
def fork_loop(t):
    tr = math.sin(2 * math.pi * 12 * t)
    ch = crouch(base(t, 2.0), 0.75)
    ch['aim'].update({'upperarm.L': PL_U, 'forearm.L': PL_F, 'hand.L': PL_H, 'upperarm.R': add(REST['upperarm.R'], (0.1, -0.2, 0.1))})
    ch['rot']['chest'] = (18 + 0.8 * tr, 0, 6); ch['rot']['head'] = (4 + 1.2 * tr, 0, 0)
    return ch

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = base(t)
    ch['rot']['chest'] = (-16 * s + 2 * math.sin(40 * t) * s, 0, 0); ch['rot']['head'] = (-22 * s, 0, 0)
    ch['aim'].update({'upperarm.R': dirs(t, [(0, REST['upperarm.R']), (0.6, (-0.9, 0.1, 0.4)), (1.5, (-0.9, 0.1, 0.4)), (2.0, REST['upperarm.R'])]),
                      'forearm.R': dirs(t, [(0, REST['forearm.R']), (0.6, (-0.8, -0.2, 0.55)), (1.5, (-0.8, -0.2, 0.55)), (2.0, REST['forearm.R'])]),
                      'upperarm.L': dirs(t, [(0, REST['upperarm.L']), (0.6, (0.9, 0.1, 0.4)), (1.5, (0.9, 0.1, 0.4)), (2.0, REST['upperarm.L'])]),
                      'forearm.L': dirs(t, [(0, REST['forearm.L']), (0.6, (0.8, -0.2, 0.55)), (1.5, (0.8, -0.2, 0.55)), (2.0, REST['forearm.L'])])})
    return ch

def defeat(t):
    k = key(t, [(0, 0), (1.2, 1.0), (2.5, 1.0)]); slump = key(t, [(0, 0), (0.8, 0), (2.0, 1.0), (2.5, 1.0)])
    ch = crouch(base(t), 1.6 * k)
    ch['rot']['chest'] = (26 * slump, 0, 6 * slump); ch['rot']['head'] = (24 * slump, 0, 0)
    ch['aim'].update({'upperarm.R': dirs(t, [(0, REST['upperarm.R']), (2.0, (-0.35, -0.3, -0.9))]), 'forearm.R': dirs(t, [(0, REST['forearm.R']), (2.0, (-0.1, -0.5, -0.86))]),
                      'upperarm.L': dirs(t, [(0, REST['upperarm.L']), (2.0, (0.35, -0.3, -0.9))]), 'forearm.L': dirs(t, [(0, REST['forearm.L']), (2.0, (0.1, -0.5, -0.86))])})
    return ch

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('ForkStrike', 1.0, fork_strike); rig.bake('DialLoop', 2.0, dial_loop)
rig.bake('HammerRaise', 0.8, hammer_raise); rig.bake('HammerLoop', 1.0, hammer_loop)
rig.bake('DrumSlam', 1.4, drum_slam)
rig.bake('ForkPlant', 1.2, fork_plant); rig.bake('ForkLoop', 2.0, fork_loop)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.35), ('ForkStrike', 0.55), ('DialLoop', 0.5), ('HammerRaise', 0.8),
                       ('HammerLoop', 0.18), ('DrumSlam', 0.85), ('ForkLoop', 0.3), ('PhaseShift', 1.0), ('Defeat', 2.5)],
                cam=(-4.4, -7.2, 3.4), target=(0, -0.2, 1.5))
rig.export(work, 'rivet_cantor', out_glb)
