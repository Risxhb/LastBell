# blender -b rivet_cantor_prep.blend -P rivet_cantor_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Rivet Cantor (miniboss_prep.py ... RivetCantor 3.2) and keys the clips in docs/bosses/rivet-cantor.md.
# Axes: -Y forward, +X = his left, Z up. He stands hunched forward: the mask sits low in front of a domed back.
# Right hand (-X): the round drum-mallet. Left hand (+X): the two-pronged tuning fork, held low and forward.
# Meshy fused the whole body, drum and fork into one shell (7,126 of the verts), so this rig uses heat weights on a
# voxel proxy (miniboss_rig_lib.Rig.heat_weights, as aurelion_rig.py). The hand bones run into the drum and the fork so
# both props follow the hands; the few small loose rivets are pinned to the nearest bone region.
#
# Clips (re-keyed 9 Nov 2026 for the 3 Nov design; prompts in build-evidence/miniboss-models-20261109/rivet/prompts.md):
#   Idle 4 s loop, Walk 1.4 s loop, ChorusCall 1.2 s (strike 0.35), SoloCall 1.2 s (stamp 0.35), VerseLoop 1.0 s loop
#   (rim taps at 0 and 0.5), HammerRaise 0.8 s (overhead at 0.8), HammerLoop 1.0 s loop (blows at 0.0 and 0.5),
#   ForkPlant 1.2 s (plant 0.8), ForkLoop 2 s loop, ForkStrike 1.0 s (from the planted crouch, wrench at 0.55),
#   DrumSlam 2.2 s (impact 1.4), DialLoop 2 s loop (Retuning), PhaseShift 2.0 s, Defeat 2.5 s (held)
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
FOOT_EDGE = lambda co: (co[:, 2] < 0.3) & (co[:, 0] > -1.2)   # 9 Nov: the right foot's outer edge stays with the foot
RIBBON = lambda co: (co[:, 2] < 0.85) & ((co[:, 0] < -0.97) | ((co[:, 0] < -0.86) & (co[:, 1] < -0.5))) & ~FOOT_EDGE(co)
rig.override(RIBBON, 'hand.R')
rig.cut_bridges(RIBBON, ('foot.R', 'shin.R', 'thigh.R'))
# 9 Nov: heat weights split the drum and the fork between forearm and hand (at rest they point the same way), so aiming
# the hand barely moved them: give each prop wholly to its hand bone
import numpy as np
def along(co, a, b, r):   # within r of the segment a-b, past its first 10 %
    a, b = np.array(a), np.array(b); d = b - a; u = (co - a) @ d / (d @ d)
    return (u > 0.1) & (u < 1.12) & (np.linalg.norm(co - (a + np.clip(u, 0, 1)[:, None] * d), axis=1) < r)
rig.override(lambda co: np.linalg.norm(co - np.array(DRUM), axis=1) < 0.5, 'hand.R')
rig.override(lambda co: along(co, WR_L, FORK, 0.3), 'hand.L')
# 9 Nov: the ribbon (drum to leg) reads as floating debris once the drum moves: remove it
import bmesh, numpy as np
TAIL = (np.linalg.norm(rig.co - np.array([-1.44, -0.58, 0.38]), axis=1) < 0.03) | (RIBBON(rig.co) & (rig.co[:, 2] < 0.5))   # the ribbon's hub vertex and everything of it below the drum
bm = bmesh.new(); bm.from_mesh(rig.body.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[int(i)] for i in np.nonzero(TAIL)[0]], context='VERTS'); bm.to_mesh(rig.body.data); bm.free()
print('RIBBON_TAIL_REMOVED', int(TAIL.sum()))

# 9 Nov round 2: also drop the small loose islands left of the ribbon (they float once the drum moves)
def drop_islands(rig, region, max_verts=60):
    me = rig.body.data; bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    seen = set(); kill = []
    for v in bm.verts:
        if v.index in seen: continue
        comp = []; stack = [v]; seen.add(v.index)
        while stack:
            a = stack.pop(); comp.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen: seen.add(b.index); stack.append(b)
        if len(comp) <= max_verts:
            c = np.mean([tuple(x.co) for x in comp], 0)
            if region(c): kill += comp
    bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(me); bm.free()
    print('ISLANDS_REMOVED', len(kill))
drop_islands(rig, lambda c: -1.5 < c[0] < -0.85 and -0.85 < c[1] < -0.2 and c[2] < 0.75)

REST = {n: tuple(rig.rest_dir[n]) for n in B}
POLE_R, POLE_L = (-1, 0.2, -0.4), (1, 0.2, -0.4)
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
def mixd(a, b, u): return nrm(add(scale(a, 1 - u), scale(b, u)))
def lerp(a, b, u): return add(scale(a, 1 - u), scale(b, u))
# Arms by targets (round 2): the wrist goes to a point (two-bone IK), then the hand bone (which runs into the drum / along
# the fork) is aimed. The drum centre is wrist + 0.70 * the hand direction; the fork tip is wrist + 1.14 * the hand direction.
# Aiming hand.R along its rest direction keeps the drum's face toward the front (-Y), as at rest.
D0 = REST['hand.R']; DRUM_LEN, FORK_LEN = 0.70, 1.14
def drum(ch, center, d=None, face=None, pole=POLE_R):
    d = nrm(d or D0); w = add(center, scale(d, -DRUM_LEN))
    ch.setdefault('ik', {})[('upperarm.R', 'forearm.R')] = (w, pole); ch['aim']['hand.R'] = (d, face_twist(d, face)) if face else d
    return ch
def fork(ch, tip, d, pole=POLE_L):
    d = nrm(d); w = add(tip, scale(d, -FORK_LEN))
    ch.setdefault('ik', {})[('upperarm.L', 'forearm.L')] = (w, pole); ch['aim']['hand.L'] = d
    return ch
def fork_w(ch, w, d, pole=POLE_L):   # by wrist position
    ch.setdefault('ik', {})[('upperarm.L', 'forearm.L')] = (w, pole); ch['aim']['hand.L'] = nrm(d)
    return ch
def drum_w(ch, w, d, face=None, pole=POLE_R):
    ch.setdefault('ik', {})[('upperarm.R', 'forearm.R')] = (w, pole); d = nrm(d)
    ch['aim']['hand.R'] = (d, face_twist(d, face)) if face else d
    return ch
PROBE = ['hand.R', 'hand.L', 'forearm.L', 'forearm.R']
REST_DRUM, REST_TIP = DRUM, FORK
REST_WR, REST_WL = WR_R, WR_L

def idle(t): return base(t)

WALK = 1.4
def walk(t):
    ph = 2 * math.pi * t / WALK; s = math.sin(ph); lift = lambda q: max(0.0, math.sin(q))
    ch = base(t, WALK)
    ch['rot'].update({'thigh.R': (-38 * s, 0, 0), 'shin.R': (48 * lift(ph + math.pi), 0, 0), 'foot.R': (12 * s, 0, 0),
                      'thigh.L': (38 * s, 0, 0), 'shin.L': (48 * lift(ph), 0, 0), 'foot.L': (-12 * s, 0, 0),
                      'hips': (0, 7 * s, 9 * s), 'chest': (8, -5 * s, -10 * s)})
    ch['loc']['hips'] = (0.05 * s, 0, -0.08 + 0.1 * abs(math.cos(ph)))
    ch['aim']['upperarm.R'] = add(REST['upperarm.R'], (0, 0.3 * s, 0)); ch['aim']['upperarm.L'] = add(REST['upperarm.L'], (0, -0.3 * s, 0))
    return ch
# ---- Work-Song Verse: the drum face-forward before him, the fork striking it.
CH_DRUM, CH_D = (-0.45, -0.88, 2.0), (0.55, -0.45, 0.2)    # Chorus: the drum raised to his face, face forward
CH_FACE = add(CH_DRUM, (0.0, -0.17, 0.0))
VL_DRUM = (-0.40, -0.95, 1.35)                               # VerseLoop: the drum at his belly, face tipped up
VL_RIM = (-0.40, -1.05, 1.70)                                # its front-top rim
F_STRIKE = (-0.85, -0.2, -0.48)                              # prongs laid across the drum face from his left
F_TAP = (-0.5, -0.3, -0.8)                                   # prongs down onto the rim
def verse_loop(t):   # 2 beats: the fork taps the drum's front-top rim on each, a bob
    ph = (t % 0.5) / 0.5; tap = snap(ph, 0.0, 0.0001, 0.35) + snap(ph, 1.0, 0.12, 0.01)
    ch = crouch(base(t, 1.0), 0.15 + 0.2 * tap)
    drum(ch, add(VL_DRUM, (0, 0, -0.04 * tap)), CH_D, (0, -0.75, 0.66))
    fork(ch, add(VL_RIM, (0.05, 0.0, 0.4 * (1 - tap))), F_TAP)
    ch['rot']['chest'] = (-6 + 6 * tap, 0, 0); ch['rot']['head'] = (-10, 0, 2 * math.sin(2 * math.pi * t))
    return ch
CALL_T = 0.35
def chorus_call(t):   # closed and up: the drum raised to his face, the fork laid across its face at 0.35, chest arched up
    u = key(t, [(0, 0), (0.22, 1.0), (0.8, 1.0), (1.2, 0.0)]); hit = snap(t, CALL_T, 0.06, 0.5)
    strike = key(t, [(0, 0), (0.2, 0.0), (CALL_T, 1.0), (0.75, 1.0), (1.2, 0.0)]); back = key(t, [(0.8, 0), (1.2, 1.0)])
    ch = crouch(base(t), 0.1 + 0.45 * hit)
    drum(ch, lerp(lerp(REST_DRUM, CH_DRUM, u), VL_DRUM, back), CH_D, (0, -1, 0.15))
    tip = lerp(add(CH_FACE, (0.55, -0.3, 0.5)), CH_FACE, strike)
    fork(ch, lerp(lerp(REST_TIP, tip, u), add(VL_RIM, (0.05, 0, 0.4)), back), mixd(REST['hand.L'], F_STRIKE, u))
    ch['rot']['chest'] = (-22 * u + 6 * hit + ring(t, CALL_T, 3, 6, 5), 0, 0)
    ch['rot']['head'] = (-20 * u, 0, 0)
    return ch
def solo_call(t):   # wide and down: arms flung apart, the right knee hauled up, stamped at 0.35 (spread, off his feet)
    u = key(t, [(0, 0), (0.2, 1.0), (0.8, 1.0), (1.2, 0.0)]); lift = key(t, [(0, 0), (0.2, 1.0), (0.27, 1.0), (CALL_T, 0.0)])
    stamp = snap(t, CALL_T, 0.02, 0.55); c = 0.15 + 1.0 * stamp
    ch = crouch(base(t), c)
    ch['rot']['thigh.R'] = (-16 * c - 88 * lift, 0, -10 * lift); ch['rot']['shin.R'] = (26 * c + 100 * lift, 0, 0)
    ch['rot']['thigh.L'] = (-16 * c + 6 * lift, 0, 0); ch['rot']['shin.L'] = (26 * c - 8 * lift, 0, 0)
    ch['loc']['hips'] = add(ch['loc']['hips'], (0.12 * lift, 0, 0.08 * lift))
    ch['rot']['hips'] = (0, -8 * lift, 0)
    low = 0.45 * stamp; back = key(t, [(0.8, 0.0), (1.2, 1.0)])
    wide_r = mixd(mixd((-0.97, -0.1, 0.25), (-0.85, -0.15, -0.5), low), REST['upperarm.R'], back)
    wide_l = mixd(mixd((0.97, -0.1, 0.25), (0.85, -0.15, -0.5), low), REST['upperarm.L'], back)
    hd = mixd(REST['hand.R'], (-0.45, -0.85, 0.25), u)
    ch['aim'].update({'upperarm.R': mixd(REST['upperarm.R'], wide_r, u), 'forearm.R': mixd(REST['forearm.R'], add(wide_r, (0, -0.15, 0.1)), u),
                      'hand.R': (hd, face_twist(hd, (-0.3, -0.95, 0.1))),
                      'upperarm.L': mixd(REST['upperarm.L'], wide_l, u), 'forearm.L': mixd(REST['forearm.L'], add(wide_l, (0, -0.15, 0.1)), u),
                      'hand.L': mixd(REST['hand.L'], (0.88, -0.35, 0.1), u)})
    ch['rot']['chest'] = (24 * stamp - 10 * lift + ring(t, CALL_T, 3, 5, 5), 0, 0); ch['rot']['head'] = (12 * stamp, 0, 0)
    return ch
# ---- Rivet Run: the drum swung overhead (face tipped toward the camera) and hammered down in front of his centre line.
OVER_W, OVER_D = (-0.85, 0.05, 3.20), (0.0, -0.6, 0.8)        # wrist overhead, the drum above and ahead of his head, face to the camera
IMPACT_DRUM, IMPACT_D = (-0.15, -1.35, 0.42), (0.1, -0.55, -0.83)
BRACE_W, BRACE_D = (1.05, -0.95, 1.25), (0.35, -0.75, -0.55)
def hammer_raise(t):
    u = key(t, [(0, 0), (0.7, 1.0)])
    ch = base(t)
    d = mixd(D0, OVER_D, u)
    drum_w(ch, lerp(REST_WR, OVER_W, u), d, OVER_FACE if u > 0.3 else None)
    fork_w(ch, lerp(REST_WL, BRACE_W, u), mixd(REST['hand.L'], BRACE_D, u))
    ch['rot']['chest'] = (-18 * u, 0, -6 * u); ch['rot']['head'] = (-12 * u, 0, 0)
    return ch
def hammer_loop(t):
    ph = (t % 0.5) / 0.5   # one beat: the impact on the beat, rebound, lift overhead, drop
    blow = key(ph, [(0, 1.0), (0.12, 0.85), (0.5, 0.0), (0.78, 0.0), (1.0, 1.0)])
    ch = crouch(base(t, 1.0), 0.2 + 0.9 * blow)
    impact_w = add(IMPACT_DRUM, scale(nrm(IMPACT_D), -DRUM_LEN))
    drum_w(ch, lerp(OVER_W, impact_w, blow), mixd(OVER_D, IMPACT_D, blow), lerp(OVER_FACE, (0, -0.9, 0.4), blow))
    fork_w(ch, BRACE_W, BRACE_D)
    ch['rot']['chest'] = (-18 + 48 * blow, 0, -6 + 6 * blow); ch['rot']['head'] = (-12 + 22 * blow, 0, 0)
    return ch
# ---- Rivet Chains: the fork driven into the floor in front of his centre line, then wrenched up and back.
PLANT_TIP, PLANT_D = (0.15, -1.45, 0.0), (-0.1, -0.25, -0.96)      # the fork planted in front, near his centre line
DRUM_SIDE_W, DRUM_SIDE_D = (-1.2, -0.5, 1.0), (-0.55, -0.6, -0.2)   # the drum held low out to his right, clear of the fork
UPBACK_W, UPBACK_D = (0.9, 0.35, 2.95), (0.2, 0.8, 0.55)            # the fork up and well back over his left shoulder
def fork_plant(t):
    T = 0.8
    u = key(t, [(0, 0), (0.5, -0.5), (T, 1.0), (1.2, 1.0)]); up = max(0, -u); dn = max(0, u)
    ch = crouch(base(t), 1.3 * dn)
    tip = lerp(lerp(REST_TIP, (0.5, -1.3, 1.9), up * 2), PLANT_TIP, dn)
    fork(ch, tip, mixd(mixd(REST['hand.L'], (-0.1, -0.3, -0.95), min(1, up * 2)), PLANT_D, dn))
    side = key(t, [(0, 0), (0.5, 1.0)])
    drum_w(ch, lerp(REST_WR, DRUM_SIDE_W, side), mixd(D0, DRUM_SIDE_D, side), SIDE_FACE if side > 0.3 else None)
    ch['rot']['chest'] = (40 * dn - 8 * up + ring(t, T, 4, 4, 4), 0, 6 * dn); ch['rot']['head'] = (18 * dn, 0, 0)
    return ch
def fork_loop(t):
    tr = math.sin(2 * math.pi * 12 * t)
    ch = crouch(base(t, 2.0), 1.3)
    fork(ch, PLANT_TIP, PLANT_D); drum_w(ch, DRUM_SIDE_W, DRUM_SIDE_D, SIDE_FACE)
    ch['rot']['chest'] = (40 + 1.0 * tr, 0, 6); ch['rot']['head'] = (18 + 1.5 * tr, 0, 0)
    return ch
def fork_strike(t):   # from the planted crouch: coil, then wrench the fork up and back over the left shoulder at 0.55
    T = 0.55
    c = key(t, [(0, 1.3), (0.4, 1.45), (T, 0.2), (1.0, 0.25)]); u = key(t, [(0, 0), (0.4, 0), (T, 1.0)])
    ch = crouch(base(t), c)
    plant_w = add(PLANT_TIP, scale(nrm(PLANT_D), -FORK_LEN))
    fork_w(ch, lerp(plant_w, UPBACK_W, u), mixd(PLANT_D, UPBACK_D, u))
    drum_w(ch, lerp(DRUM_SIDE_W, (-1.65, -0.25, 1.95), u), mixd(DRUM_SIDE_D, (-0.6, -0.6, 0.2), u), SIDE_FACE)   # the drum flung out for balance
    ch['rot']['chest'] = (40 * (1 - u) + 6 * key(t, [(0, 0), (0.4, 1), (T, 0)]) - 16 * u + ring(t, T, 3, 5, 5), 0, 6 * (1 - u) + 28 * u)
    ch['rot']['head'] = (18 * (1 - u) - 14 * u, 0, 12 * u)
    return ch
# ---- Anvil Hymn: the drum heaved high overhead with both hands, then slammed down centred in front.
SLAM_W, SLAM_D = (-0.55, 0.05, 3.30), (0.3, -0.45, 0.85)       # both arms straight up, the drum held high above his head
SLAM_LW, SLAM_LD = (0.45, 0.0, 3.25), (-0.55, 0.0, 0.83)       # the left hand up on the drum's rim
def drum_slam(t):
    T = 1.4
    blow = key(t, [(0, 0), (1.1, -1.0), (1.22, -1.05), (T, 1.0), (2.2, 0.9)])
    up = min(1, max(0, -blow)); dn = max(0, blow)
    ch = crouch(base(t), 1.4 * dn + 0.1 * up)
    impact_w = add(IMPACT_DRUM, scale(nrm(IMPACT_D), -DRUM_LEN))
    w = lerp(lerp(REST_WR, SLAM_W, up), impact_w, dn); d = mixd(mixd(D0, SLAM_D, up), IMPACT_D, dn)
    drum_w(ch, w, d, OVER_FACE if up > 0.3 else ((0, -0.9, 0.4) if dn > 0.3 else None))
    lw = lerp(lerp(REST_WL, SLAM_LW, up), (0.35, -1.0, 0.75), dn)
    fork_w(ch, lw, mixd(mixd(REST['hand.L'], SLAM_LD, up), (0.2, -0.6, -0.77), dn))
    ch['rot']['chest'] = (-24 * up + 48 * dn + ring(t, T, 3, 5, 5), 0, -4 * up)
    ch['rot']['head'] = (-14 * up + 22 * dn, 0, 0)
    return ch
# ---- Retuning: kneeling, compact and low, the drum set face-up before him, the fork tip turning its rim, head bowed.
RT_DRUM, RT_D = (-0.15, -1.1, 0.85), (0.25, -0.55, 0.8)       # Retuning: the drum tipped face-up before his knees
def dial_loop(t):
    p = 2 * math.pi * t / 2.0; tw = math.sin(p)
    ch = crouch(base(t, 2.0), 2.0)
    ch['rot']['thigh.R'] = (-75, 0, 0); ch['rot']['shin.R'] = (118, 0, 0)    # right knee down
    ch['loc']['hips'] = add(ch['loc']['hips'], (0, 0.05, -0.15))
    drum(ch, RT_DRUM, RT_D, (0, -0.3, 0.95))
    rim = add(RT_DRUM, (0.3 * math.cos(p), -0.08 * math.sin(p), 0.14))
    fork(ch, rim, (-0.3 + 0.1 * tw, -0.25, -0.92))
    ch['rot']['chest'] = (42 + 1.5 * math.sin(2 * p), 0, 3 * tw); ch['rot']['head'] = (26, 0, 4 * tw)
    return ch

def phase_shift(t):   # Key Change: both arms up in a symmetric Y, chest arched far back, trembling
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)]); tr = 2 * math.sin(40 * t) * s
    ch = base(t)
    d = mixd(D0, (-0.35, -0.5, 0.8), s)
    drum_w(ch, lerp(REST_WR, (-1.25, 0.1, 3.15), s), d, (0, -1, 0.2) if s > 0.3 else None)
    fork_w(ch, lerp(REST_WL, (1.25, 0.1, 3.15), s), mixd(REST['hand.L'], (0.35, -0.3, 0.88), s))
    ch['rot']['chest'] = (-28 * s + tr, 0, 0); ch['rot']['head'] = (-28 * s, 0, 0)
    return ch

def defeat(t):   # sinks onto his heels and topples to his right; the drum lands flat, the fork lies on the floor
    k = key(t, [(0, 0), (1.2, 1.0), (2.5, 1.0)]); slump = key(t, [(0, 0), (0.8, 0), (2.0, 1.0), (2.5, 1.0)])
    ch = crouch(base(t), 2.1 * k)
    ch['loc']['hips'] = add(ch['loc']['hips'], (-0.15 * slump, 0.15 * k, -0.2 * k))
    ch['rot']['chest'] = (16 * slump, -24 * slump, -10 * slump); ch['rot']['head'] = (30 * slump, -10 * slump, 0)
    d = mixd(D0, (-0.3, -0.2, -0.93), slump)   # the drum dropped face-down beside him
    drum_w(ch, lerp(REST_WR, (-1.3, -0.3, 0.85), slump), d)
    fork_w(ch, lerp(REST_WL, (1.15, -0.35, 0.4), slump), mixd(REST['hand.L'], (0.2, -0.98, -0.04), slump))
    return ch

N0 = nrm((-0.55, -0.83, 0.0))   # the drum's face normal at rest (out to his right and forward; measured from the side view)
# note: the face stays within ~33 degrees of the hand bone, so a face-up or face-to-camera drum needs the hand aimed that way
def face_twist(d, face):
    # the twist about d that turns the drum face (N0 carried by the shortest rotation D0 -> d) toward `face`
    d = Vector(nrm(d)); n1 = Vector(D0).rotation_difference(d) @ Vector(N0); f = Vector(nrm(face))
    a = n1 - d * n1.dot(d); b = f - d * f.dot(d)
    if a.length < 1e-4 or b.length < 1e-4: return 0.0
    a.normalize(); b.normalize(); return math.degrees(math.atan2(a.cross(b).dot(d), a.dot(b)))
OVER_FACE = (-0.3, -0.85, 0.4)                                   # its face turned to the camera
SIDE_FACE = (-0.8, -0.6, 0.0)

rig.bake('Idle', 4.0, idle); rig.bake('Walk', WALK, walk)
rig.bake('ChorusCall', 1.2, chorus_call); rig.bake('SoloCall', 1.2, solo_call); rig.bake('VerseLoop', 1.0, verse_loop)
rig.bake('ForkStrike', 1.0, fork_strike); rig.bake('DialLoop', 2.0, dial_loop)
rig.bake('HammerRaise', 0.8, hammer_raise); rig.bake('HammerLoop', 1.0, hammer_loop)
rig.bake('DrumSlam', 2.2, drum_slam)
rig.bake('ForkPlant', 1.2, fork_plant); rig.bake('ForkLoop', 2.0, fork_loop)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
PREVIEW_SHOTS = [('Idle', 0), ('ChorusCall', 0.35), ('SoloCall', 0.22), ('SoloCall', 0.35), ('VerseLoop', 0.0),
                 ('VerseLoop', 0.25), ('HammerRaise', 0.8), ('HammerLoop', 0.0), ('HammerLoop', 0.3), ('ForkPlant', 0.8),
                 ('ForkLoop', 0.3), ('ForkStrike', 0.55), ('DrumSlam', 1.1), ('DrumSlam', 1.4), ('DialLoop', 0.5),
                 ('PhaseShift', 1.0), ('Defeat', 2.5), ('Walk', 0.35), ('Walk', 1.05)]
if '--preview' in args:
    rig.preview(work, PREVIEW_SHOTS, cam=(-4.4, -7.2, 3.4), target=(0, -0.2, 1.5))
    if '--gameplay' in args:   # the gameplay camera: ~14 m back, ~11 m up, 40 degrees
        rig.preview(work, PREVIEW_SHOTS, cam=(-5.0, -13.5, 11.0), target=(0, 0, 1.2), sheet='poses_sheet_gameplay.png')
rig.export(work, 'rivet_cantor', out_glb)
