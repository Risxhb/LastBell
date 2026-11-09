# blender -b escapement_twins_prep.blend -P escapement_twins_rig.py -- <work dir> <out dir or .../EscapementTwins.glb> --part tick|tock|wheel [--preview] [--gameplay]
# Rigs one piece of the prepared Escapement Twins (miniboss_prep.py ... EscapementTwins 3.8) for the 3 Nov design
# (docs/bosses/escapement-twins.md), where the twins are two separate towers about 12 m apart plus an escape-wheel prop.
# The 26 Oct Meshy model is one tracked plinth carrying both towers with the wheel between them, so it is split (9 Nov):
#   tick  = the sun tower (-X, brass) on its half of the plinth          -> EscapementTick.glb
#   tock  = the moon tower (+X, cobalt) on its half of the plinth        -> EscapementTock.glb
#   wheel = the gold escape wheel, its rocker bridge and the centre panel -> EscapementWheel.glb
# Each tower is re-centred on its own axis (x = 0) facing -Y. All Meshy parts are rigid and pinned whole to bones.
# Axes: -Y forward (the clock face), +X = the tower's left, Z up.
#
# Tower clips (prompts in build-evidence/miniboss-models-20261109/twins/prompts.md; Tick and Tock share names and keys,
# except PolarityLoop's bow, which falls on beat 0 for Tick and beat 1 for Tock):
#   Idle 4 s loop, Walk 1.2 s loop, PolarityLoop 1.2 s loop (2 beats at 100 BPM), FacesDown 0.8 s (stomp at 0.5),
#   AgreeAim 1.2 s (aimed by 1.1), AgreeFire 0.6 s (fire at 0.1), VesicaLoop 2 s loop, FacesOut 0.8 s (spring at 0.15),
#   Stagger 3.5 s, PhaseShift 2.0 s, Defeat 2.5 s (held)
# Wheel clips: Idle 2.4 s loop (four slow ratchet steps), WheelSpin 0.8 s, WheelLoop 1.2 s loop (four fast steps),
#   WheelReverse 0.6 s (kicks back at 0.05)
import bpy, bmesh, sys, os, math, numpy as np
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
work = os.path.abspath(args[0]); out = os.path.abspath(args[1])
PART = args[args.index('--part') + 1]
NAME = {'tick': 'EscapementTick', 'tock': 'EscapementTock', 'wheel': 'EscapementWheel'}[PART]
out_dir = out if os.path.isdir(out) or not out.lower().endswith('.glb') else os.path.dirname(out)
out_glb = os.path.join(out_dir, NAME + '.glb')

TX = 1.47; GEAR = (0.0, 0.0, 1.64); SPLIT = 0.62   # tower axes at x = -TX (tick) and +TX (tock); |x| < SPLIT is the wheel

# ---- split: keep only this piece's loose parts, re-centre it ----
body = bpy.data.objects['EscapementTwins']; me = body.data
co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
ed = np.empty(len(me.edges) * 2, np.int64); me.edges.foreach_get('vertices', ed); ed = ed.reshape(-1, 2)
par = np.arange(len(co))
def find(a):
    while par[a] != a: par[a] = par[par[a]]; a = par[a]
    return a
for a, b in ed:
    ra, rb = find(a), find(b)
    if ra != rb: par[ra] = rb
roots = np.array([find(i) for i in range(len(co))]); _, part = np.unique(roots, return_inverse=True)
keep = np.zeros(len(co), bool); straddle = 0
for p in range(part.max() + 1):
    m = part == p; c = co[m].mean(0); lo, hi = co[m].min(0), co[m].max(0)
    side = 'wheel' if abs(c[0]) < SPLIT else ('tick' if c[0] < 0 else 'tock')
    if side == 'wheel' and hi[0] - lo[0] > 1.5: side = None   # the long deck plates under both towers belong to no piece
    if side == 'wheel' and c[2] < 0.75 and c[1] > -0.95: side = None   # loose tread bits under the gear (keep only the front panel)
    if lo[0] < -SPLIT < hi[0] or lo[0] < SPLIT < hi[0]: straddle += 1
    if side == PART: keep[m] = True
print('SPLIT', PART, 'verts', int(keep.sum()), 'of', len(co), 'parts straddling the cut', straddle)
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in np.nonzero(~keep)[0]], context='VERTS'); bm.to_mesh(me); bm.free()
shift = {'tick': TX, 'tock': -TX, 'wheel': 0.0}[PART]
me.transform(Matrix.Translation((shift, 0, 0))); me.update()
body.name = NAME; me.name = NAME
print('TRIS', sum(len(f.vertices) - 2 for f in me.polygons))

def smooth01(u): return smooth(u)

if PART in ('tick', 'tock'):
    B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
         'base': ((0, 0, 0.3), (0, 0, 1.0), 'root'),
         'tower': ((0, 0, 1.08), (0, 0, 3.7), 'base'),
         'halo': ((0, 0, 3.29), (0, -0.5, 3.29), 'tower')}
    rig = Rig(NAME, B)
    def classify(c):
        lo, hi, ctr = c.min(0), c.max(0), c.mean(0)
        if ctr[2] < 1.05: return 'base'
        if abs(ctr[0]) < 0.5 and 2.85 < ctr[2] < 3.75 and hi[1] - lo[1] < 0.15: return 'halo'
        return 'tower'
    rig.assign(classify)
    BEAT = 0.6; MY_BEAT = 0.0 if PART == 'tick' else BEAT   # Tick bows on beat 0, Tock on beat 1
    def base_pose(t, per=4.0, halo_turns=0.25):
        p = 2 * math.pi * t / per
        return {'rot': {'tower': (0.8 * math.sin(p), 0.6 * math.sin(p * 0.5), 0), 'halo': (0, 360 * halo_turns * t / per, 0)},
                'loc': {'base': (0, 0, 0.01 * math.sin(p))}}
    # round 3: big tilts go on the plinth ('base', which carries the tower) so the tower never pivots off its plinth and
    # shows the cut; the halo tilt is the visible signal (flared up = firing / stomping, drooped = stunned or heavy)
    def idle(t):
        ch = base_pose(t); ch['rot']['halo'] = (10, ch['rot']['halo'][1], 0); return ch
    def walk(t):   # the treads roll: the plinth bobs down and rocks, the whole tower leans against the travel
        p = 2 * math.pi * t / 1.2
        ch = base_pose(t, 1.2, 0.5)
        ch['loc']['base'] = (0, 0, -0.1 * abs(math.sin(p)))
        ch['rot']['base'] = (-6 + 4 * math.sin(2 * p), 3 * math.sin(p), 0)
        ch['rot']['tower'] = (2 * math.sin(p), 2 * math.sin(p), 0); ch['rot']['halo'] = (12, 180 * t / 1.2, 0)
        return ch
    def polarity_loop(t):   # Pallet Lock warning: a fast, hard bow on its own beat, springing fully upright between beats
        ch = base_pose(t, 1.2, 0.5)
        b0 = MY_BEAT + 0.06
        b = key(t, [(b0 - 0.06, 0), (b0, 1), (b0 + 0.1, 1), (b0 + 0.3, 0)]) + (key(t, [(1.2 + b0 - 0.06, 0), (1.2 + b0, 1)]) if MY_BEAT == 0 else 0)
        ch['rot']['base'] = (16 * b, 0, 0); ch['rot']['tower'] = (6 * b, 0, 0); ch['loc']['base'] = (0, 0, -0.04 * b)
        ch['rot']['halo'] = (-15 * b, 90 * math.floor(t / 0.3) + 90 * smooth01(((t % 0.3) / 0.3) / 0.25), 0)
        return ch
    def faces_down(t):   # the lock / slam stomp: rises on its treads, then slams down at 0.5 with a forward jolt
        T = 0.5
        up = key(t, [(0, 0), (0.35, 1.0), (T, 0.0)]); hit = snap(t, T, 0.02, 0.3)
        ch = base_pose(t)
        ch['loc']['base'] = (0, 0, 0.25 * up - 0.08 * hit)
        ch['rot']['base'] = (-6 * up + 9 * hit + ring(t, T, 3, 3, 4), 0, 0)
        ch['rot']['halo'] = (-30 * up + 20 * hit, 360 * smooth01(t / 0.8), 0)
        return ch
    def agree_aim(t):   # Escape Wheel tell: the whole tower rears right back, face to the sky, rising, halo flared up
        u = key(t, [(0, 0), (1.1, 1.0), (1.2, 1.0)])
        ch = base_pose(t)
        ch['rot']['base'] = (-26 * u, 0, 0); ch['rot']['tower'] = (-6 * u, 0, 0); ch['loc']['base'] = (0, 0.1 * u, 0.15 * u)
        ch['rot']['halo'] = (-35 * u, 720 * smooth01(t / 1.2) ** 2, 0)
        return ch
    def agree_fire(t):   # the fan fires at 0.1: from reared back it snaps well past upright, the plinth kicked back
        T = 0.1
        k = snap(t, T, 0.06, 0.45); back = 1 - smooth01(t / 0.1)
        ch = base_pose(t)
        ch['rot']['base'] = (-26 * back + 14 * k + ring(t, T, 3, 3, 4), 0, 0); ch['rot']['tower'] = (-6 * back + 4 * k, 0, 0)
        ch['loc']['base'] = (0, 0.1 * back + 0.15 * k, 0.15 * back); ch['rot']['halo'] = (-35 * back + 25 * k, 720 + 180 * smooth01(t / 0.6), 0)
        return ch
    def vesica_loop(t):   # Counterpoise: bowed lowest of all and sunk, as if pressed down by a weight; halo drooped
        p = 2 * math.pi * t / 2.0
        ch = base_pose(t, 2.0)
        ch['rot']['base'] = (22 + 2 * math.sin(p), 0, 0); ch['rot']['tower'] = (12 + 2 * math.sin(p), 0, 0)
        ch['loc']['base'] = (0, 0, -0.14 + 0.02 * math.sin(p)); ch['rot']['halo'] = (35, 180 * t / 2.0, 0)
        return ch
    def faces_out(t):   # Counterpoise lands / the leap lands: springs up out of the bow and past upright at 0.15, plinth off the floor
        T = 0.15
        ch = base_pose(t)
        ch['rot']['base'] = (key(t, [(0, 22), (T, -10), (0.45, 3), (0.8, 0)]), 0, 0)
        ch['rot']['tower'] = (key(t, [(0, 12), (T, -2), (0.45, 0), (0.8, 0)]), 0, 0)
        ch['loc']['base'] = (0, 0, key(t, [(0, -0.14), (T, 0.22), (0.4, 0.0), (0.8, 0.0)]))
        ch['rot']['halo'] = (-30 * snap(t, T, 0.05, 0.4), 360 * smooth01(t / 0.8), 0)
        return ch
    def stagger(t):   # Lock and Release punish window (3.5 s): rolled hard over sideways, sunk, halo drooped, wobbling; upright at the end
        u = key(t, [(0, 0), (0.25, 1.0), (2.9, 1.0), (3.5, 0.0)])
        w = math.exp(-1.2 * t) * math.sin(2 * math.pi * 1.6 * t)
        sg = 1 if PART == 'tick' else -1   # the twins reel opposite ways; the roll is about the face normal, so it shows face-on
        ch = base_pose(t, 3.5, 0.1)
        ch['rot']['base'] = (8 * u + 3 * w, (22 * u + 7 * w) * sg, 0); ch['rot']['tower'] = (0, 6 * u * sg, 3 * w)
        ch['loc']['base'] = (0, 0, -0.12 * u); ch['rot']['halo'] = (45 * u, 20 * t, 15 * u * sg)
        return ch
    def phase_shift(t):   # the tempo changes: lifted high and trembling, halo tipped and spinning
        s = key(t, [(0, 0), (0.6, 1.0), (1.5, 1.0), (2.0, 0.0)])
        ch = base_pose(t)
        ch['rot']['base'] = (5 * math.sin(41 * t) * s, 5 * math.sin(37 * t) * s, 0); ch['loc']['base'] = (0, 0, 0.18 * s)
        ch['rot']['halo'] = (30 * s, 1080 * smooth01(t / 2.0), 0)
        return ch
    def defeat(t):   # toppled over to its side on the plinth edge, halo tipped right over
        k = key(t, [(0, 0), (1.6, 1.0), (2.5, 1.0)])
        sg = -1 if PART == 'tick' else 1
        return {'rot': {'base': (10 * k, 30 * k * sg, 0), 'tower': (6 * k, 8 * k * sg, 0), 'halo': (60 * k, 0, 0)},
                'loc': {'base': (0, 0, -0.1 * k)}}
    rig.bake('Idle', 4.0, idle); rig.bake('Walk', 1.2, walk); rig.bake('PolarityLoop', 1.2, polarity_loop)
    rig.bake('FacesDown', 0.8, faces_down); rig.bake('AgreeAim', 1.2, agree_aim); rig.bake('AgreeFire', 0.6, agree_fire)
    rig.bake('VesicaLoop', 2.0, vesica_loop); rig.bake('FacesOut', 0.8, faces_out); rig.bake('Stagger', 3.5, stagger)
    rig.bake('PhaseShift', 2.0, phase_shift); rig.bake('Defeat', 2.5, defeat)
    SHOTS = [('Idle', 0), ('PolarityLoop', MY_BEAT + 0.06), ('FacesDown', 0.35), ('FacesDown', 0.5), ('AgreeAim', 1.1), ('AgreeFire', 0.15),
             ('VesicaLoop', 0.5), ('FacesOut', 0.15), ('Stagger', 1.0), ('PhaseShift', 1.0), ('Defeat', 2.5), ('Walk', 0.3)]
else:
    B = {'root': ((0, 0, 0), (0, 0.6, 0), None),
         'plinth': ((0, 0, 0.3), (0, 0, 1.0), 'root'),
         'wheel': (GEAR, (0, -0.6, 1.64), 'plinth'),
         'rocker': ((0, 0, 2.5), (0, 0, 2.9), 'plinth')}
    rig = Rig(NAME, B)
    def classify(c):
        lo, hi, ctr = c.min(0), c.max(0), c.mean(0)
        if ctr[2] < 1.05: return 'plinth'
        if ctr[1] < 0.25 and math.hypot(ctr[0], ctr[2] - GEAR[2]) < 0.6 and hi[2] < 2.25: return 'wheel'
        if ctr[2] > 1.85: return 'rocker'
        return 'plinth'
    rig.assign(classify)
    def ratchet(t, per, steps, step_deg):
        slot = per / steps; n = math.floor(t / slot); u = ((t - n * slot) / slot) / 0.15
        return step_deg * (n + smooth(u))
    def rock(t, per):
        beat = math.floor(t / (per / 2)); u = (t - beat * per / 2) / (per / 2); side = 1 if beat % 2 == 0 else -1
        return side * 12 * (1 - smooth(u / 0.5)) - side * 12 * smooth((u - 0.5) / 0.5)
    def w_idle(t): return {'rot': {'wheel': (0, ratchet(t, 2.4, 4, 45), 0), 'rocker': (0, rock(t, 2.4) * 0.6, 0)}}
    def w_loop(t): return {'rot': {'wheel': (0, ratchet(t, 1.2, 4, 90), 0), 'rocker': (0, rock(t, 1.2) * 1.6, 0)}, 'loc': {'rocker': (0, 0, -0.06)}}
    def w_spin(t):
        u = key(t, [(0, 0), (0.6, 1.0), (0.8, 1.0)])
        rise = key(t, [(0, 0), (0.8, 1.0)])
        return {'rot': {'wheel': (0, 180 * smooth(t / 0.8) ** 2, 0), 'rocker': (0, 18 * math.sin(2 * math.pi * 2 * t) * (1 - u), 0)},
                'loc': {'rocker': (0, 0, -0.08 * u), 'plinth': (0, 0, -0.6 * (1 - rise))}}   # rises up out of the floor
    def w_reverse(t):
        return {'rot': {'wheel': (0, -45 * snap(t, 0.05, 0.03, 0.4) + ring(t, 0.05, -20, 2.5, 4), 0), 'rocker': (0, ring(t, 0.02, 30, 2.0, 3.5), 0),
                        'plinth': (-10 * snap(t, 0.05, 0.03, 0.4), 0, 0)}, 'loc': {'rocker': (0, 0, -0.06), 'plinth': (0, 0.1 * snap(t, 0.05, 0.03, 0.4), 0)}}
    rig.bake('Idle', 2.4, w_idle); rig.bake('WheelSpin', 0.8, w_spin); rig.bake('WheelLoop', 1.2, w_loop); rig.bake('WheelReverse', 0.6, w_reverse)
    SHOTS = [('Idle', 0), ('WheelSpin', 0.3), ('WheelLoop', 0.1), ('WheelReverse', 0.1)]

if '--preview' in args:
    rig.preview(work, SHOTS, cam=(-3.5, -7.5, 3.6), target=(0, 0, 1.7), sheet=f'poses_sheet_{PART}.png')
    if '--gameplay' in args:
        rig.preview(work, SHOTS, cam=(-5.0, -13.5, 11.0), target=(0, 0, 1.5), sheet=f'poses_sheet_{PART}_gameplay.png')
rig.export(work, NAME.lower(), out_glb)
