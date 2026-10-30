# blender -b vesper_seedkeeper_prep.blend -P vesper_seedkeeper_rig.py -- <work dir> <out.glb> [--preview]
# Rigs the prepared Vesper Seedkeeper (miniboss_prep.py ... VesperSeedkeeper 3.6) and keys the clips in
# docs/bosses/vesper-seedkeeper.md. Axes: -Y forward (the sleeping face), +X = its left, Z up.
# The model brief said 2.5 m, but at that size the vessel is only about 1 m across (smaller than a hero), so it is
# prepped at 3.6 m (vessel about 1.5 m) and floats. A lotus vessel under a halo ring and spire, with three
# seed-lanterns on vine cords: one under its right side (-X), one under its left (+X) and one straight below.
# The vessel, halo and lanterns are rigid; the vine cords are skinned by distance to the cord bones so they bend as
# the lanterns swing. (Survey numbers were measured on a 2.5 m prep; S scales them.)
#
# Clips: Idle 4 s loop, Walk 2 s loop (a drift), LanternCast 1.0 s, TetherLoop 2 s loop, BloomOpen 1.2 s,
#        BloomLoop 2 s loop, SeedTip 1.0 s (seeds drop at 0.45), HaloTilt 0.8 s, SweepLoop 2 s loop (the halo turns once),
#        PhaseShift 2.0 s, Defeat 2.5 s (held)
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])

S = 3.6 / 2.5
def P(x, y, z): return (x * S, y * S, z * S)
HOVER = 0.5
VESSEL = P(0.07, 0.04, 1.66)
LANTERN = {'R': (P(-0.45, 0.0, 1.85), P(-0.80, 0.04, 1.25), P(-0.80, 0.04, 0.45), P(-0.80, 0.04, 0.83)),
           'L': (P(0.60, 0.0, 1.80), P(1.00, 0.04, 1.14), P(1.00, 0.04, 0.40), P(1.00, 0.04, 0.75)),
           'M': (P(0.06, 0.04, 1.15), P(0.06, 0.04, 0.78), P(0.06, 0.04, 0.10), P(0.07, 0.04, 0.49))}
B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
     'float': (VESSEL, add(VESSEL, (0, 0, 0.6)), 'root'),
     'body': (VESSEL, add(VESSEL, (0, -0.8, 0)), 'float'),
     'halo': (P(0.08, 0.04, 2.14), P(0.08, 0.04, 2.5), 'body')}
for s, (top, hook, tip, ctr) in LANTERN.items():
    B['cord.' + s] = (top, hook, 'body'); B['lantern.' + s] = (hook, tip, 'cord.' + s)
rig = Rig('VesperSeedkeeper', B, no_deform=('root', 'float'))

def pin(c):
    lo, hi, ctr = c.min(0), c.max(0), c.mean(0)
    if ctr[2] > 2.08 * S and (hi[0] - lo[0] > 0.8 * S or lo[2] > 2.15 * S): return 'halo'   # the ring and the spire, not the crown
    for s, (top, hook, tip, cc) in LANTERN.items():
        if math.hypot(ctr[0] - cc[0], ctr[2] - cc[2]) < 0.34 * S and ctr[2] < 1.05 * S and hi[2] - lo[2] < 0.8 * S: return 'lantern.' + s
    if ctr[2] > 1.05 * S and abs(ctr[0] - 0.07 * S) < 0.62 * S and len(c) > 60 and hi[0] - lo[0] < 1.3 * S: return 'body'
    return None
rig.distance_weights(pin=pin, rigid=('halo', 'lantern.R', 'lantern.L', 'lantern.M'), bias={'body': 0.25})

def base(t, per=4.0):
    p = 2 * math.pi * t / per
    ch = {'rot': {'float': (1.5 * math.sin(p + 0.4), 0, 0), 'halo': (0, 0, 90 * t / per)},
          'loc': {'float': (0, 0, HOVER + 0.08 * math.sin(p))}}
    for k, s in enumerate('RML'): ch['rot']['cord.' + s] = (4 * math.sin(p + k * 1.6), 3 * math.sin(p + k * 0.9), 0)
    return ch
def hang(ch):   # lanterns counter-turn their cords' tilt so they keep hanging, plus a little lag
    for s in 'RML':
        cx, cy, cz = ch['rot'].get('cord.' + s, (0, 0, 0)); fx, fy, fz = ch['rot'].get('float', (0, 0, 0))
        ch['rot']['lantern.' + s] = add((-0.7 * (cx + fx), -0.7 * (cy + fy), 0), ch['rot'].get('lantern.' + s, (0, 0, 0)))
    return ch
def idle(t): return hang(base(t))
def walk(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0); ch['rot']['float'] = (9 + 1.5 * math.sin(p), 0, 2 * math.sin(p))
    for k, s in enumerate('RML'): ch['rot']['cord.' + s] = (-10 + 3 * math.sin(p + k), 2 * math.sin(p + k), 0)
    return hang(ch)

# Lantern Tethers: the face wakes and the lanterns are swung out on their cords and held
OUT = {'R': (0, 50, 0), 'L': (0, -50, 0), 'M': (-40, 0, 0)}   # outward and forward (the cords point down)
def lantern_cast(t):
    u = key(t, [(0, 0), (0.7, 1.0), (1.0, 1.0)])
    ch = base(t); ch['loc']['float'] = (0, 0, HOVER + 0.25 * u)
    for s in 'RML': ch['rot']['cord.' + s] = add(scale(OUT[s], u), (ring(t, 0.7, 6, 1.5, 3), 0, 0))
    ch = hang(ch)
    for s in 'RML': ch['rot']['lantern.' + s] = add(ch['rot']['lantern.' + s], (ring(t, 0.7, 15, 1.2, 2.5), 0, 0))
    return ch
def tether_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0); ch['loc']['float'] = (0, 0, HOVER + 0.25 + 0.05 * math.sin(p))
    for k, s in enumerate('RML'): ch['rot']['cord.' + s] = add(OUT[s], (5 * math.sin(p + k), 4 * math.sin(p + 2 * k), 0))
    return hang(ch)

# Vesper Bloom: rises, the halo lifts and blazes round, the lanterns fall wide and low
def bloom_open(t):
    u = key(t, [(0, 0), (1.0, 1.0), (1.2, 1.0)])
    ch = base(t); ch['loc']['float'] = (0, 0, HOVER + 0.7 * u); ch['rot']['float'] = (-8 * u, 0, 0)
    ch['loc']['halo'] = (0, 0, 0.3 * u); ch['rot']['halo'] = (0, 0, 360 * smooth(t / 1.2))
    for s in 'RML': ch['rot']['cord.' + s] = scale(OUT[s], 0.6 * u)
    return hang(ch)
def bloom_loop(t):
    p = 2 * math.pi * t / 2.0
    ch = base(t, 2.0); ch['loc']['float'] = (0, 0, HOVER + 0.7 + 0.05 * math.sin(p)); ch['rot']['float'] = (-8, 0, 0)
    ch['loc']['halo'] = (0, 0, 0.3); ch['rot']['halo'] = (0, 0, 360 * t / 2.0)
    for k, s in enumerate('RML'): ch['rot']['cord.' + s] = add(scale(OUT[s], 0.6), (3 * math.sin(p + k), 0, 0))
    return hang(ch)

# Seedfall: tips forward and shakes the seeds out
def seed_tip(t):
    T = 0.45
    u = key(t, [(0, 0), (0.35, 1.0), (0.75, 1.0), (1.0, 0)])
    ch = base(t); ch['rot']['float'] = (28 * u + 3 * snap(t, T, 0.05, 0.2) * math.sin(2 * math.pi * 9 * t), 0, 0)
    ch['loc']['float'] = (0, -0.15 * u, HOVER - 0.1 * u)
    for k, s in enumerate('RML'): ch['rot']['cord.' + s] = (ring(t, T, 10, 2, 3) + 3 * math.sin(4 * t + k), 0, 0)
    return hang(ch)

# Halo Sweep: the halo tilts to stand like a lighthouse lamp and turns once per loop
TILT = 35
def halo_tilt(t):
    u = key(t, [(0, 0), (0.7, 1.0), (0.8, 1.0)])
    ch = base(t); ch['rot']['halo'] = (TILT * u, 0, 0); ch['loc']['halo'] = (0, 0, 0.12 * u)
    ch['rot']['float'] = (-5 * u, 0, 0)
    return hang(ch)
def sweep_loop(t):
    ch = base(t, 2.0)
    a = 2 * math.pi * t / 2.0
    # tilted ring turning about the vertical: tip it toward the turning direction
    ch['rot']['halo'] = (TILT * math.cos(a), TILT * math.sin(a), 360 * t / 2.0); ch['loc']['halo'] = (0, 0, 0.12)
    ch['rot']['float'] = (-5, 0, 0)
    return hang(ch)

def phase_shift(t):
    s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0)])
    ch = base(t); ch['loc']['float'] = (0, 0, HOVER + 0.35 * s); ch['rot']['float'] = (2 * math.sin(37 * t) * s, 2 * math.sin(29 * t) * s, 0)
    ch['rot']['halo'] = (0, 0, 720 * smooth(t / 2.0)); ch['loc']['halo'] = (0, 0, 0.25 * s)
    for k, l in enumerate('RML'): ch['rot']['cord.' + l] = scale(OUT[l], 0.9 * s * (0.8 + 0.2 * math.sin(9 * t + k)))
    return hang(ch)

def defeat(t):
    k = key(t, [(0, 0), (1.8, 1.0), (2.5, 1.0)])
    ch = {'rot': {'float': (14 * k, -10 * k, 0), 'halo': (25 * k, 10 * k, 30 * (1 - k) * t)},
          'loc': {'float': (0, 0, HOVER - (HOVER + 0.05) * k), 'halo': (0, 0, -0.05 * k)}}
    for j, s in enumerate('RML'): ch['rot']['cord.' + s] = add(scale(OUT[s], (1.5 if s == 'M' else 1.3) * k), (ring(t, 1.8, 8, 1.2, 2), 0, 0))
    return hang(ch)

rig.bake('Idle', 4.0, idle); rig.bake('Walk', 2.0, walk)
rig.bake('LanternCast', 1.0, lantern_cast); rig.bake('TetherLoop', 2.0, tether_loop)
rig.bake('BloomOpen', 1.2, bloom_open); rig.bake('BloomLoop', 2.0, bloom_loop)
rig.bake('SeedTip', 1.0, seed_tip); rig.bake('HaloTilt', 0.8, halo_tilt); rig.bake('SweepLoop', 2.0, sweep_loop)
rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
if '--preview' in args:
    rig.preview(work, [('Idle', 0), ('Walk', 0.5), ('TetherLoop', 0), ('BloomLoop', 0.3), ('SeedTip', 0.5),
                       ('HaloTilt', 0.8), ('SweepLoop', 0.5), ('PhaseShift', 1.0), ('Defeat', 1.5), ('Defeat', 2.5)],
                cam=(-5.0, -8.0, 4.4), target=(0, 0, 2.0))
rig.export(work, 'vesper_seedkeeper', out_glb)
