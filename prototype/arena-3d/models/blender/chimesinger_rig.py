# blender -b chimesinger_prep.blend -P chimesinger_rig.py -- <work dir> <out.glb> [mocap run glb] [unity out.glb]
# Rigs the prepared Chimesinger, mounts the staff in her right hand, keys locomotion and the #36 kit, exports a GLB.
# Adapted from the Vanguard's rig.py. Axes: -Y forward, +X = character's left, Z up.
# Body rotations are degrees in rest-armature axes (X: negative swings a leg forward / positive bends a knee).
# Arms are posed by directions in chest space (see arm_R / arm_L), not angles: the mesh is in an A-pose and the staff
# needs exact aiming, so 'staff points here, forearm points there' is easier to author and to read back.
import bpy, sys, os, math, collections, numpy as np
from mathutils import Vector, Matrix, Euler, Quaternion
args = sys.argv[sys.argv.index('--') + 1:]
work, out_glb = args[0], args[1]
sc = bpy.context.scene; sc.render.fps = 30
body = bpy.data.objects['Chimesinger']
staff = bpy.data.objects.get('ChimeStaff')

# ---------------- armature (joint positions measured from grid renders of the prepared mesh) ----------------
B = {  # name: (head, tail, parent, deform)
    'root': ((0, 0, 0), (0, 0.25, 0), None, False),
    'hips': ((0, -0.04, 0.86), (0, -0.04, 0.98), 'root', True),
    'spine': ((0, -0.04, 0.98), (0, -0.05, 1.13), 'hips', True),
    'chest': ((0, -0.05, 1.13), (0, -0.06, 1.36), 'spine', True),
    'neck': ((0, -0.06, 1.36), (0, -0.07, 1.44), 'chest', True),
    'head': ((0, -0.07, 1.44), (0, -0.07, 1.66), 'neck', True),
}
for s, n in ((1, 'L'), (-1, 'R')):
    B.update({
        f'upperarm.{n}': ((s * 0.13, -0.06, 1.28), (s * 0.28, -0.06, 1.14), 'chest', True),
        f'forearm.{n}': ((s * 0.28, -0.06, 1.14), (s * 0.41, -0.05, 1.03), f'upperarm.{n}', True),
        f'hand.{n}': ((s * 0.41, -0.05, 1.03), (s * 0.51, -0.05, 0.95), f'forearm.{n}', True),
        f'thigh.{n}': ((s * 0.075, -0.04, 0.86), (s * 0.05, -0.04, 0.47), 'hips', True),
        f'shin.{n}': ((s * 0.05, -0.04, 0.47), (s * 0.035, -0.02, 0.10), f'thigh.{n}', True),
        f'foot.{n}': ((s * 0.035, -0.02, 0.10), (s * 0.04, -0.16, 0.02), f'shin.{n}', True),
        # coat: a front and a back panel per side, two segments each, driven from the thighs (see secondary())
        f'skirtF.{n}': ((s * 0.07, -0.17, 0.86), (s * 0.10, -0.21, 0.48), 'hips', True),
        f'skirtF2.{n}': ((s * 0.10, -0.21, 0.48), (s * 0.13, -0.23, 0.12), f'skirtF.{n}', True),
        f'skirtB.{n}': ((s * 0.07, 0.08, 0.86), (s * 0.10, 0.12, 0.48), 'hips', True),
        f'skirtB2.{n}': ((s * 0.10, 0.12, 0.48), (s * 0.13, 0.15, 0.12), f'skirtB.{n}', True),
        # the wide sleeve's hanging drape: gravity-hung from the elbow so it keeps falling straight down
        f'sleeve.{n}': ((s * 0.28, -0.06, 1.14), (s * 0.30, -0.06, 0.94), f'forearm.{n}', True),
        # bell strands: a cord down the back of each sleeve that hangs on below the hem with its bells (reference back
        # view), so the strand hangs from the sleeve drape, gravity-hung below it
        f'tassel.{n}': ((s * 0.27, 0.10, 0.92), (s * 0.31, 0.13, 0.66), f'sleeve.{n}', True),
        f'tassel2.{n}': ((s * 0.31, 0.13, 0.66), (s * 0.37, 0.17, 0.36), f'tassel.{n}', True),
    })
# Hands (prep keeps them high-poly): chimesinger_hands.py measures each hand (finger direction fd, nrm, fw across the
# palm toward the thumb; the palm is the -nrm face), finds the four fingers by clustering the vertices past the web
# along fw, and gives each finger three joints (knuckle / middle / end) and the thumb three. The grip around the staff
# is solved numerically against the real shaft (see hands.solve): the shaft sits in the palm under the first finger
# segments, and each joint angle is searched so every segment's centre line sits one finger radius off the shaft.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chimesinger_hands as hands
_co = np.empty(len(body.data.vertices) * 3); body.data.vertices.foreach_get('co', _co); _co = _co.reshape(-1, 3)
HANDAX, HANDS, HW = {}, {}, {}
DIGITS = [f + str(k) for f in hands.FINGERS + ('thumb',) for k in (1, 2, 3)]
for s, n in ((1, 'L'), (-1, 'R')):
    H = hands.measure(_co, s); HANDS[n] = H; HW[n] = hands.weights(H)
    HANDAX[n] = tuple(Vector(v) for v in (H['W'], H['fd'], H['nrm'], H['fw']))
    for f in H['fingers'] + [dict(name='thumb', J=H['thumb']['J'])]:
        for k in range(3):
            B[f"{f['name']}{k + 1}.{n}"] = (tuple(f['J'][k]), tuple(f['J'][k + 1]), f'hand.{n}' if k == 0 else f"{f['name']}{k}.{n}", True)
    print('HAND', n, {f['name']: (round(f['J'][3] @ H['fd'] - H['W'] @ H['fd'], 3), round(f['rf'] * 1000, 1)) for f in H['fingers']},
          'thumb r %.1f mm' % (H['thumb']['rt'] * 1000))
# the shaft at the grip height, measured on the staff mesh (staff space: shaft along +Z, butt at 0)
GRIP_AT = 0.58                                # metres from the butt of the 1.5 m staff, fixed: the staff never slides
LEFT_AT = 0.776                               # where the left fist takes the staff (two-handed clips): on the blue grip wrap
if staff: *SHAFT_XY, SHAFT_R = hands.shaft_profile(staff, GRIP_AT); SHAFT_RL = hands.shaft_profile(staff, LEFT_AT)[2]
else: SHAFT_XY, SHAFT_R, SHAFT_RL = (0.0, 0.0), 0.02, 0.02
print('SHAFT radius %.1f mm at the right fist, %.1f mm at the left' % (SHAFT_R * 1000, SHAFT_RL * 1000))
# the solve takes a few minutes: cache it in the work dir, keyed on the solver source, the hand vertices and the shaft
import hashlib, json
_key = hashlib.md5(open(hands.__file__, 'rb').read() + np.round(_co[HANDS['R']['hand'] | HANDS['L']['hand']], 5).tobytes()
                   + repr((round(SHAFT_R, 5), round(SHAFT_RL, 5))).encode()).hexdigest()
_cp = os.path.join(work, 'chimesinger_grip_solve.json')
_cache = json.load(open(_cp)) if os.path.exists(_cp) else {}
if _cache.get('key') == _key:
    GRIPSOL = {n: {'C': np.array(v['C']), 'u': np.array(v['u']), 'tilt': v['tilt'], 'diag': v['diag'],
                   'rots': {b: (np.array(ax), a) for b, (ax, a) in v['rots'].items()}} for n, v in _cache['sol'].items()}
    print('GRIP_SOLVE cached', {n: (v['tilt'], v['diag']) for n, v in GRIPSOL.items()})
else:
    GRIPSOL = {n: hands.solve(HANDS[n], HW[n], SHAFT_R if n == 'R' else SHAFT_RL) for n in ('R', 'L')}
    json.dump({'key': _key, 'sol': {n: {'C': list(v['C']), 'u': list(v['u']), 'tilt': float(v['tilt']), 'diag': float(v['diag']),
               'rots': {b: (list(map(float, ax)), float(a)) for b, (ax, a) in v['rots'].items()}} for n, v in GRIPSOL.items()}},
              open(_cp, 'w'))
def _grip_point(n): return Vector(GRIPSOL[n]['C'])     # a point on the shaft axis inside the closed fist
# the staff rides its own (non-deforming) bone under the right hand, along the shaft axis through the fist
B['staff'] = (tuple(_grip_point('R')), tuple(_grip_point('R') + Vector(GRIPSOL['R']['u']) * 0.2), 'hand.R', False)
CLOTH = [n for n in B if n.startswith(('skirt', 'tassel', 'sleeve'))]
ad = bpy.data.armatures.new('ChimesingerRig'); arm = bpy.data.objects.new('ChimesingerRig', ad); sc.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
for n, (h, t, p, d) in B.items():
    e = ad.edit_bones.new(n); e.head, e.tail = h, t; e.use_deform = d
    if p: e.parent = ad.edit_bones[p]; e.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------- weights: explicit by region (heat weighting fails on this mesh: the AI mesh is ~hundreds of loose
# parts and even a voxel-remeshed proxy returns no solution). Torso by height, then arms / legs / cloth / head. ----------------
for n in B:
    if B[n][3] and n not in body.vertex_groups: body.vertex_groups.new(name=n)
names = [g.name for g in body.vertex_groups]; gi = {n: i for i, n in enumerate(names)}
nv = len(body.data.vertices); W = np.zeros((nv, len(names)), np.float32)
co = np.empty(nv * 3); body.data.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
x, y, z = co[:, 0], co[:, 1], co[:, 2]; ax = np.abs(x)
# loose pieces of the mesh (islands): the bell strands and the small bells / charms are separate parts
E0 = np.array([e.vertices[:] for e in body.data.edges]); _par = np.arange(nv)
def _find(i):
    while _par[i] != i: _par[i] = _par[_par[i]]; i = _par[i]
    return i
for _a, _b in E0:
    _ra, _rb = _find(_a), _find(_b)
    if _ra != _rb: _par[_ra] = _rb
ISLAND = np.array([_find(i) for i in range(nv)]); _ids, _cnt = np.unique(ISLAND, return_counts=True)
SMALL = set(_ids[_cnt <= 800].tolist())
cl = lambda a: np.clip(a, 0, 1)
def blend_rows(mask, rows):
    W[:] = W * (1 - mask[:, None]) + rows * mask[:, None]
def zeros(): return np.zeros((nv, len(names)), np.float32)
def sm(a): a = cl(a); return a * a * (3 - 2 * a)
_sp, _ch, _nk = sm((z - 0.95) / 0.07), sm((z - 1.1) / 0.08), sm((z - 1.35) / 0.05)
W[:, gi['hips']] = 1 - _sp; W[:, gi['spine']] = _sp * (1 - _ch); W[:, gi['chest']] = _ch * (1 - _nk); W[:, gi['neck']] = _nk
for s, n in ((1, 'L'), (-1, 'R')):
    side = (x * s) > 0
    # bell strands: the loose pieces hanging below each sleeve (outboard of the coat, behind the body line); they hang
    # from the tassel bones, which hang from the sleeve drape
    strand = side & (z < 0.91) & (z > 0.33) & (((ax > 0.265) & (y > 0.03)) | (ax > 0.3))   # (fused to the coat in places: cut below)
    r = zeros(); u = sm((z - 0.6) / 0.12)
    r[:, gi[f'tassel.{n}']] = u; r[:, gi[f'tassel2.{n}']] = 1 - u
    blend_rows(strand.astype(np.float32), r)
    STRAND = strand if n == 'L' else (STRAND | strand)
    # arm: everything outboard of the shoulder above the hands' lowest point, split along the shoulder -> fingertip line
    S0, T0 = np.array([0.13, 1.28]), np.array([0.53, 0.94]); dvec = T0 - S0; L = np.linalg.norm(dvec); dvec /= L
    t = ((np.stack([ax, z], 1) - S0) @ dvec) / L
    # torso half-width by height (waist 0.11 -> bust 0.16 -> shoulder); the sleeves start ~3 cm outboard of it
    half = np.interp(z, [0.86, 0.92, 0.96, 1.1, 1.22, 1.3, 1.36], [0.19, 0.19, 0.12, 0.12, 0.16, 0.14, 0.12])
    # (front of the bust and the belt pouches sit at y < -0.14; the sleeves never reach that far forward)
    a = cl((ax - half - 0.01) / 0.04) * cl((y + 0.15) / 0.03) * ((z > 0.86) & (z < 1.36) & side & ~strand)
    up, fo = 1 - sm((t - 0.33) / 0.12), sm((t - 0.33) / 0.12) * (1 - sm((t - 0.68) / 0.08))
    r = zeros(); r[:, gi[f'upperarm.{n}']] = up; r[:, gi[f'forearm.{n}']] = fo; r[:, gi[f'hand.{n}']] = 1 - up - fo
    sh = sm((0.16 - t) / 0.1)            # shoulder cap: share with the chest
    r[:, gi['chest']] += sh * up; r[:, gi[f'upperarm.{n}']] *= 1 - sh
    # drape: sleeve cloth well below the arm line, along the forearm half
    zl = S0[1] + t * L * dvec[1]; below = zl - z
    h = sm((below - 0.05) / 0.08) * sm((t - 0.3) / 0.1)
    r[:] = r * (1 - h[:, None]); r[:, gi[f'sleeve.{n}']] += h
    blend_rows(a.astype(np.float32), r)
    # legs: the stockinged core under the coat, and everything at boot height
    # distance from the rest leg axis (thigh + shin), against the stocking's radius at that height
    def seg_d(h, t_):
        h, t_ = np.array(h), np.array(t_); d = t_ - h; u = np.clip(((co - h) @ d) / (d @ d), 0, 1)
        return np.linalg.norm(co - (h + u[:, None] * d), axis=1)
    dl = np.minimum(seg_d(B[f'thigh.{n}'][0], B[f'thigh.{n}'][1]), seg_d(B[f'shin.{n}'][0], B[f'shin.{n}'][1]))
    rad = np.interp(z, [0.1, 0.3, 0.47, 0.7, 0.86], [0.06, 0.05, 0.055, 0.07, 0.09])
    core = side & (((z < 0.86) & (dl < rad + 0.012)) | ((z < 0.2) & (ax < 0.11) & (y > -0.21) & (y < 0.08)))
    r = zeros(); hp = sm((z - 0.78) / 0.1); kn = sm((z - 0.43) / 0.08); an = sm((z - 0.08) / 0.06)
    r[:, gi['hips']] = hp; r[:, gi[f'thigh.{n}']] = (1 - hp) * kn
    r[:, gi[f'shin.{n}']] = (1 - kn) * an; r[:, gi[f'foot.{n}']] = 1 - an
    blend_rows(core.astype(np.float32), r)
    if n == 'L': leg_core = core
    else: leg_core = leg_core | core
# coat / skirt: everything else below the waist. Angle around the pelvis picks the panels, height picks the segment.
cloth = (z < 0.9) & ~leg_core & ~STRAND
ang = np.degrees(np.arctan2(x, -(y + 0.04)))       # 0 front, +90 her left, +-180 back
PANELS = [('skirtF.L', 35), ('skirtB.L', 145), ('skirtB.R', -145), ('skirtF.R', -35)]
wa = np.zeros((nv, 4), np.float32)
for i, (_, a0) in enumerate(PANELS):
    d = np.abs((ang - a0 + 180) % 360 - 180); wa[:, i] = cl(1 - d / 75)     # (only the two nearest panels share a vertex)
wa /= np.maximum(wa.sum(1, keepdims=True), 1e-6)
top = sm((z - 0.8) / 0.08); low = sm((0.5 - z) / 0.25)
r = zeros(); r[:, gi['hips']] = top
for i, (pn, _) in enumerate(PANELS):
    r[:, gi[pn]] = (1 - top) * wa[:, i] * (1 - low); r[:, gi[pn.replace('F.', 'F2.').replace('B.', 'B2.')]] = (1 - top) * wa[:, i] * low
blend_rows(cloth.astype(np.float32), r)
# fingers and thumb take their share of each hand's weight (chimesinger_hands.weights: per finger, blended smoothly at
# the knuckles and between neighbouring fingers in the palm)
for n in ('L', 'R'):
    hw = W[:, gi[f'hand.{n}']].copy()
    for b, w in HW[n].items(): W[:, gi[f'{b}.{n}']] = hw * w
# head, hair and halo move as one
r = zeros(); r[:, gi['head']] = 1
blend_rows(sm((z - 1.41) / 0.05).astype(np.float32), r)
# every other small loose piece (bells, charms) moves rigidly with the exact point it touches: it takes that vertex's
# weights, so it can never drift off what it hangs from
from mathutils.kdtree import KDTree
_kd = KDTree(nv)
for _i in range(nv): _kd.insert(co[_i], _i)
_kd.balance(); ATTACH = {}
for _r in SMALL:
    _idx = np.nonzero(ISLAND == _r)[0]
    if STRAND[_idx].any(): continue
    _best = None
    for _i in _idx[:: max(1, len(_idx) // 80)]:
        for (_p, _j, _d) in _kd.find_n(co[_i], 60):
            if ISLAND[_j] != _r and not STRAND[_j]:
                if _best is None or _d < _best[1]: _best = (_j, _d)
                break
    if _best: W[_idx] = W[_best[0]]; ATTACH[int(_r)] = int(_best[0])
print('LOOSE_PIECES strands', int(STRAND.sum()), 'verts; rigid pieces', len(ATTACH))
# the cord link from the sleeve's cord down to each strand's top bead: a short run of the cord just above the strand
# that the regions above leave on the hips. It blends from exactly the sleeve cord's end weights (top) to the strand
# bone (bottom), so the cord runs on unbroken from the sleeve into the strand.
_armdom = np.array([names[int(np.argmax(W[i]))].startswith(('sleeve', 'forearm', 'upperarm')) for i in range(nv)])
LINK = np.zeros(nv, bool)
for s, n in ((1, 'L'), (-1, 'R')):
    _st = np.nonzero(STRAND & ((x * s) > 0))[0]
    if not len(_st): continue
    T = co[_st[np.argsort(-z[_st])[:8]]].mean(0)
    _c = np.nonzero(_armdom & ((x * s) > 0) & (z > T[2]))[0]
    C = _c[np.argmin(np.linalg.norm(co[_c] - T, axis=1))]
    # only the cord itself: close to the line from the sleeve cord's end down to the strand top (not the coat behind it)
    _d = co[C] - T; _u = cl(((co - T) @ _d) / (_d @ _d)); hz = np.linalg.norm(co - (T + _u[:, None] * _d), axis=1)
    link = ((x * s) > 0) & (hz < 0.012) & (z > T[2] - 0.02) & (z < co[C][2]) & ~_armdom
    b = sm((co[C][2] - z[link]) / max(1e-6, co[C][2] - (T[2] - 0.02)))
    tas = np.zeros(len(names), np.float32); tas[gi[f'tassel.{n}']] = 1
    W[link] = (1 - b)[:, None] * W[C][None] + b[:, None] * tas[None]; LINK |= link
    print('CORD_LINK', n, 'verts', int(link.sum()), 'from z %.3f (sleeve cord) to %.3f (strand top)' % (co[C][2], T[2]))
# shoulders: the chest -> upper arm hand-over is spread along the surface (a few rings of neighbour averaging), so the
# back of the shoulder stretches smoothly when the arm goes overhead instead of tearing into a sheet at one edge row
_sz = (ax > 0.05) & (ax < 0.27) & (z > 1.0) & (z < 1.34)
_nb = np.zeros(nv); np.add.at(_nb, E0[:, 0], 1); np.add.at(_nb, E0[:, 1], 1)
for _it in range(10):
    _acc = W.copy(); np.add.at(_acc, E0[:, 0], W[E0[:, 1]]); np.add.at(_acc, E0[:, 1], W[E0[:, 0]])
    W[_sz] = (_acc / (1 + _nb)[:, None])[_sz]
order = np.argsort(-W, 1); keep = np.zeros_like(W, bool); np.put_along_axis(keep, order[:, :4], True, 1)
W = np.where(keep, W, 0); s_ = W.sum(1, keepdims=True); W = np.where(s_ > 0, W / np.maximum(s_, 1e-6), 0)
W[s_[:, 0] == 0, gi['hips']] = 1
print('UNWEIGHTED_FIXED', int((s_[:, 0] == 0).sum()), 'CLOTH', int(cloth.sum()), 'LEGS', int(leg_core.sum()))
uw = co[s_[:, 0] == 0]
if len(uw): print('UNWEIGHTED_AT', np.percentile(uw, [5, 50, 95], axis=0).round(2).tolist())
# Families that really separate: left vs right leg, an arm vs the hips / coat, the bell strands vs everything.
FAM = {'legL': ['thigh.L', 'shin.L', 'foot.L'], 'legR': ['thigh.R', 'shin.R', 'foot.R'],
       'tasL': ['tassel.L', 'tassel2.L'], 'tasR': ['tassel.R', 'tassel2.R'],
       'armL': ['upperarm.L', 'forearm.L', 'hand.L', 'sleeve.L'] + [d + '.L' for d in DIGITS],
       'armR': ['upperarm.R', 'forearm.R', 'hand.R', 'sleeve.R'] + [d + '.R' for d in DIGITS],
       'low': ['hips'] + [b for b in names if b.startswith('skirt')], 'torso': ['spine', 'chest']}
def families():
    f = np.full(nv, '', object)
    for k, bs in FAM.items(): f[W[:, [gi[b] for b in bs]].sum(1) > 0.6] = k
    return f
# Below the chest the hanging sleeves touch the coat and belt: a vertex there belongs to one or the other, never half
# (a half-and-half vertex tears into a sheet once the arm lifts away), so it goes wholly to its larger share.
_ab = [gi[b] for b in FAM['armL'] + FAM['armR']]; _nab = [j for j in range(len(names)) if j not in _ab]
_am = W[:, _ab].sum(1); _mix = (z < 1.02) & (_am > 0.05) & (_am < 0.95) & ~LINK & ~STRAND
for _sel, _zero in ((_mix & (_am >= 0.5), _nab), (_mix & (_am < 0.5), _ab)):
    _w = W[_sel]; _w[:, _zero] = 0; _w /= np.maximum(_w.sum(1, keepdims=True), 1e-6); W[_sel] = _w
print('ARM_COAT_SNAPPED', int(_mix.sum()))
# Stray patches: a few vertices of one family inside another's region (Meshy fuses small bits of coat onto a sleeve or a
# leg) would be cut loose below and float, or stretch into a spike. A small patch takes the weights of what surrounds it.
E = np.array([e.vertices[:] for e in body.data.edges])
for _pass in range(3):
    fam = families(); fam[LINK] = 'link'
    _par = np.arange(nv)
    same = E[(fam[E[:, 0]] == fam[E[:, 1]]) & (fam[E[:, 0]] != '')]
    for _a, _b in same:
        _ra, _rb = _find(_a), _find(_b)
        if _ra != _rb: _par[_ra] = _rb
    comp = np.array([_find(i) for i in range(nv)]); _cid, _ccnt = np.unique(comp, return_counts=True); csize = dict(zip(_cid, _ccnt))
    nbr = collections.defaultdict(list)
    for _a, _b in E:
        if comp[_a] != comp[_b]: nbr[comp[_a]].append(_b); nbr[comp[_b]].append(_a)
    fixed = 0
    for _r, _ns in nbr.items():
        _f = fam[_r]
        if _f in ('', 'link') or csize[_r] > 60: continue
        _nf = [fam[j] for j in _ns if fam[j] not in ('', 'link', _f)]
        if len(_nf) < 0.6 * len(_ns): continue
        _maj = max(set(_nf), key=_nf.count); _idx = np.nonzero(comp == _r)[0]
        _src = np.array([j for j in _ns if fam[j] == _maj])
        for i in _idx: W[i] = W[_src[np.argmin(np.linalg.norm(co[_src] - co[i], axis=1))]]
        fixed += 1
    print('STRAY_PATCHES pass', _pass, 'fixed', fixed)
    if not fixed: break
# Meshy fuses touching parts (legs, sleeve hems on the belt, strands on the coat). An edge whose two ends are driven by
# entirely different bones stretches into a sheet once they move apart, so it is cut (below, once the groups are written).
fam = families(); fam[LINK] = 'link'
fa, fb = fam[E[:, 0]], fam[E[:, 1]]
pair = lambda p, q: ((fa == p) & (fb == q)) | ((fa == q) & (fb == p))
tas = lambda f: (f == 'tasL') | (f == 'tasR') | (f == 'link')
# the cord link stays joined to the sleeve cord above it; the coat panels hanging past the legs below the crotch are
# fused to the trousers where they touch, so they are cut apart there
skirtv = W[:, [gi[b] for b in names if b.startswith('skirt')]].sum(1) > 0.5
legv = W[:, [gi[b] for b in FAM['legL'] + FAM['legR']]].sum(1) > 0.5; lowz = (z[E[:, 0]] < 0.84) & (z[E[:, 1]] < 0.84)
leg_skirt = lowz & ((legv[E[:, 0]] & skirtv[E[:, 1]]) | (legv[E[:, 1]] & skirtv[E[:, 0]]))
arm_link = pair('link', 'armL') | pair('link', 'armR')
# below the armpit a sleeve only rests against the waist and belt
belt = (z[E[:, 0]] < 1.02) & (z[E[:, 1]] < 1.02) & (pair('armL', 'torso') | pair('armR', 'torso'))
far = np.nonzero(belt | pair('legL', 'legR') | ((tas(fa) != tas(fb)) & ~arm_link) | pair('armL', 'low') | pair('armR', 'low') | leg_skirt)[0]
# a cut must never leave a scrap loose: a small piece it would split off takes the weights of the side it touches most
# and stays joined to that side
_par = np.arange(nv); _keep = np.ones(len(E), bool); _keep[far] = False
for _a, _b in E[_keep]:
    _ra, _rb = _find(_a), _find(_b)
    if _ra != _rb: _par[_ra] = _rb
_cr = np.array([_find(i) for i in range(nv)]); _cid, _ccnt = np.unique(_cr, return_counts=True); _csz = dict(zip(_cid, _ccnt))
_isz = dict(zip(*np.unique(ISLAND, return_counts=True))); _uncut = set(); _scraps = 0
for _r in _cid[_ccnt < 40]:
    _idx = np.nonzero(_cr == _r)[0]
    if _isz[ISLAND[_idx[0]]] == len(_idx): continue            # a real loose part of the model, not a scrap
    _ein = far[np.isin(E[far, 0], _idx) | np.isin(E[far, 1], _idx)]
    _oth = np.where(np.isin(E[_ein, 0], _idx), E[_ein, 1], E[_ein, 0]); _of = [fam[j] for j in _oth]
    _maj = max(set(_of), key=_of.count); _src = _oth[np.array(_of) == _maj]
    for i in _idx: W[i] = W[_src[np.argmin(np.linalg.norm(co[_src] - co[i], axis=1))]]
    fam[_idx] = _maj; _uncut |= set(_ein[np.array(_of) == _maj].tolist()); _scraps += 1
far = np.array(sorted(set(far.tolist()) - _uncut), int)
print('SCRAPS_REJOINED', _scraps)
for g in body.vertex_groups: g.remove(list(range(nv)))
for j, n in enumerate(names):
    vg = body.vertex_groups[n]; idx = np.nonzero(W[:, j] > 0)[0]
    for i in idx: vg.add([int(i)], float(W[i, j]), 'REPLACE')
import bmesh
bm = bmesh.new(); bm.from_mesh(body.data); bm.edges.ensure_lookup_table()
# (runs after the groups are written: bmesh keeps them per vertex, while deleting renumbers the vertices)
bmesh.ops.delete(bm, geom=[bm.edges[int(i)] for i in far], context='EDGES'); bm.to_mesh(body.data); bm.free(); body.data.update()
print('SEAMS_CUT', len(far))
body.parent = arm; body.matrix_parent_inverse = arm.matrix_world.inverted()
for m in list(body.modifiers): body.modifiers.remove(m)
am = body.modifiers.new('Armature', 'ARMATURE'); am.object = arm

# ---------------- staff: gripped in the right fist ----------------
# Rest mount: the shaft axis on the solved grip line through the fist; the ring faces back along the forearm (toward the
# wrist). The banners hang in the ring's plane 17-20 cm either side of the shaft, level with the fist, so this is the
# only mount where the forearm can't pass through one. The staff never moves relative to the hand.
GRIP_R = _grip_point('R')                     # on the shaft axis inside the closed right fist
STAFF0 = Vector(GRIPSOL['R']['u'])            # through the fist, thumb side = bell end
FD_R = HANDAX['R'][1]; FDP = (FD_R - STAFF0 * STAFF0.dot(FD_R)).normalized()   # finger direction across the shaft
RING0 = -FDP
if staff:
    Zs, Ys = STAFF0, RING0; Xs = Ys.cross(Zs)
    staff.matrix_world = (Matrix.Translation(GRIP_R) @ Matrix((Xs, Ys, Zs)).transposed().to_4x4()
                          @ Matrix.Translation((-SHAFT_XY[0], -SHAFT_XY[1], -GRIP_AT)))
    staff.parent = arm; staff.parent_type = 'BONE'; staff.parent_bone = 'staff'
    staff.matrix_parent_inverse = (arm.matrix_world @ ad.bones['staff'].matrix_local @ Matrix.Translation((0, ad.bones['staff'].length, 0))).inverted()
# The two framed banners under the ring swing on their own hinges: split off the staff mesh into their own objects, each
# riding a bone under the staff bone (hinge at the banner's top, tail at its bottom), driven by the follow-through pass.
BANNERS = []
if staff:
    import bmesh
    Ms = staff.matrix_world.copy()
    def _banner_faces(bm, sgn):     # staff space: below the ring, well off the shaft on one side
        return [f for f in bm.faces if f.calc_center_median().z < 0.86 and (f.calc_center_median().x - SHAFT_XY[0]) * sgn > 0.075]
    for nm, sgn in (('bannerA', 1), ('bannerB', -1)):
        me_ = staff.data.copy(); bm = bmesh.new(); bm.from_mesh(me_); keep = set(_banner_faces(bm, sgn))
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS'); bm.to_mesh(me_); bm.free()
        ob = bpy.data.objects.new('ChimeBanner' + nm[-1], me_); sc.collection.objects.link(ob); ob.matrix_world = Ms
        bc = np.array([v.co[:] for v in me_.vertices]); zt = bc[:, 2].max(); zb = bc[:, 2].min()
        hinge = Vector(bc[bc[:, 2] > zt - 0.02].mean(0)); foot = Vector(bc[bc[:, 2] < zb + 0.1].mean(0))
        B[nm] = (tuple(Ms @ hinge), tuple(Ms @ foot), 'staff', False); BANNERS.append((nm, ob))
        print('BANNER', nm, len(me_.vertices), 'verts, hinge %.2f m up the staff, %.2f m long' % (hinge.z, (hinge - foot).length))
    bm = bmesh.new(); bm.from_mesh(staff.data)
    bmesh.ops.delete(bm, geom=_banner_faces(bm, 1) + _banner_faces(bm, -1), context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS'); bm.to_mesh(staff.data); bm.free()
    bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
    for nm, ob in BANNERS:
        e = ad.edit_bones.new(nm); e.head, e.tail = B[nm][0], B[nm][1]; e.use_deform = False; e.parent = ad.edit_bones['staff']
    bpy.ops.object.mode_set(mode='OBJECT')
    for nm, ob in BANNERS:
        ob.parent = arm; ob.parent_type = 'BONE'; ob.parent_bone = nm
        ob.matrix_parent_inverse = (arm.matrix_world @ ad.bones[nm].matrix_local @ Matrix.Translation((0, ad.bones[nm].length, 0))).inverted()

# ---------------- animation helpers ----------------
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
def Rzyx(x=0, y=0, z=0): return R(z=z) @ R(y=y) @ R(x=x)
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
V = lambda *a: Vector(a)
HEAD = {n: Vector(B[n][0]) for n in B}; TAIL = {n: Vector(B[n][1]) for n in B}
SOLE = {n: [Vector((B[n][0][0], 0.0, 0.0)), Vector((B[n][1][0], -0.17, 0.0))] for n in ('foot.L', 'foot.R')}   # heel tip, toe
def delta(n):   # posed rotation of a bone relative to its rest, in armature axes
    return pbs[n].matrix.to_3x3() @ pbs[n].bone.matrix_local.to_3x3().inverted()
def _frame(a, b):
    a = Vector(a).normalized(); b = Vector(b); b = (b - a * a.dot(b)).normalized()
    return Matrix((a, b, a.cross(b))).transposed()
UP0 = {n: (TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).normalized() for n in 'LR'}
FO0 = {n: (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).normalized() for n in 'LR'}
LEN = {n: ((TAIL[f'upperarm.{n}'] - HEAD[f'upperarm.{n}']).length, (TAIL[f'forearm.{n}'] - HEAD[f'forearm.{n}']).length) for n in 'LR'}
def arm_dirs(n, up, fo, hand=None):
    # chest-space rotations for an arm whose upper arm points along up, forearm along fo, hand frame Dh (or none)
    Ru = UP0[n].rotation_difference(Vector(up).normalized()).to_matrix()
    Df = (Ru @ FO0[n]).rotation_difference(Vector(fo).normalized()).to_matrix() @ Ru
    out = {f'upperarm.{n}': {'rot': Ru}, f'forearm.{n}': {'rot': Ru.inverted() @ Df}}
    if hand is not None: out[f'hand.{n}'] = {'rot': Df.inverted() @ hand}
    wrist = HEAD[f'upperarm.{n}'] + Ru @ (HEAD[f'forearm.{n}'] - HEAD[f'upperarm.{n}']) + Df @ (HEAD[f'hand.{n}'] - HEAD[f'forearm.{n}'])
    return out, wrist, (hand if hand is not None else Df)
def hand_R(fo_dir, staff_dir, roll=0.0):
    # The right hand's rotation for a staff pointing along staff_dir, rolled about the shaft so the hand lines up with the
    # forearm (a neutral wrist; roll adds pronation, degrees). Its roll is a consequence of the arm, not authored.
    s = Vector(staff_dir).normalized(); f = Vector(fo_dir); e = (f - s * s.dot(f))
    e = e.normalized() if e.length > 1e-6 else _frame(s, (0, 0, 1)).col[1]
    e = Matrix.Rotation(math.radians(roll), 3, s) @ e
    return _frame(s, e) @ _frame(STAFF0, FDP).transposed()
def arm_R(up, fo, staff_dir, roll=0.0):
    # right arm holding the staff: staff_dir is where the bell end points
    _, _, Df = arm_dirs('R', up, fo)
    Dh = hand_R(Df @ FO0['R'], staff_dir, roll)
    out, wrist, Dh = arm_dirs('R', up, fo, Dh)
    grip = wrist + Dh @ (GRIP_R - HEAD['hand.R'])
    return out, grip, Vector(staff_dir).normalized()
def two_bone(n, target, pole):
    S = HEAD[f'upperarm.{n}']; L1, L2 = LEN[n]; d = target - S; dist = min(d.length, L1 + L2 - 1e-3); dh = d.normalized()
    a = math.acos(max(-1, min(1, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist))))
    pp = Vector(pole) - dh * dh.dot(Vector(pole)); pp = pp.normalized() if pp.length > 1e-6 else Vector((0, 0, -1))
    up = dh * math.cos(a) + pp * math.sin(a); elbow = S + up * L1
    return up, (S + dh * dist - elbow).normalized()
def slerpv(a, b, u):
    a, b = Vector(a).normalized(), Vector(b).normalized()
    return a.slerp(b, u, Vector((0, 0, 1))) if a.dot(b) > -0.999 else (a + (b - a) * u).normalized()
def smooth(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):  # piecewise smooth interpolation through (time, value); vector values (3-tuples) are slerped
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple): return slerpv(v0, v1, u)
            return v0 + (v1 - v0) * u
    v = pts[-1][1]
    return Vector(v).normalized() if isinstance(v, tuple) else v
def keyp(t, pts):  # like key() but tuples are points: smooth linear blend, no normalising
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = smooth((t - t0) / max(1e-6, t1 - t0)); return Vector(v0).lerp(Vector(v1), u)
    return Vector(pts[-1][1])
POINTS = {'rh', 'rg'}
def leg(side, thigh, knee, foot=0.0, hip_pitch=0.0, flat=1.0, spread=0.0):
    # thigh: - = forward; knee: + = bend; flat 1 keeps the sole as it was at rest; spread: + = out to the side
    n = 'L' if side > 0 else 'R'
    f = -(hip_pitch + thigh + knee) * flat + foot
    return {f'thigh.{n}': {'rot': R(x=thigh) @ R(y=-side * spread)}, f'shin.{n}': {'rot': R(x=knee)}, f'foot.{n}': {'rot': R(x=f)}}
THIGH_LEN = (TAIL['thigh.L'] - HEAD['thigh.L']).length; SHIN_LEN = (TAIL['shin.L'] - HEAD['shin.L']).length
HIP_Z, ANKLE_Z = B['thigh.L'][0][2], B['shin.L'][1][2]
def ik_leg(side, ankle_y, hips_y, hips_z, hip_pitch=0.0, spread=2.0, foot=0.0):
    # 2-bone IK in the sagittal plane: ankle at its floor height, ankle_y metres along Y (-Y forward)
    fwd = -(ankle_y - hips_y); down = HIP_Z + hips_z - ANKLE_Z
    d = min(math.hypot(fwd, down), THIGH_LEN + SHIN_LEN - 1e-3); a, b = THIGH_LEN, SHIN_LEN
    knee = 180 - math.degrees(math.acos((a * a + b * b - d * d) / (2 * a * b)))
    thigh = -(math.degrees(math.atan2(fwd, down) + math.acos((a * a + d * d - b * b) / (2 * a * d)))) - hip_pitch
    return leg(side, thigh, knee, foot=foot, hip_pitch=hip_pitch, spread=spread)

def ik_leg3(side, ankle, R_h, loc_h, toe=0.0):
    # 3D two-bone IK with the sole planted: the ankle goes to a fixed floor point (armature space) whatever the hips do,
    # the knee bends toward her front, and the foot keeps its rest heading flat on the floor (toe > 0 lifts the heel,
    # pivoting on the toes). Rotations come back in set_pose's convention (relative to the parent's posed frame).
    n = 'L' if side > 0 else 'R'
    A = Vector(ankle) + Vector((0, 0.0, 0.15 * math.sin(math.radians(max(toe, 0.0)))))
    H = HEAD['hips'] + Vector(loc_h) + R_h @ (HEAD[f'thigh.{n}'] - HEAD['hips'])
    d = A - H; dist = min(d.length, THIGH_LEN + SHIN_LEN - 1e-4); dh = d.normalized()
    pole = Vector((side * 0.12, -1, 0)); pp = (pole - dh * dh.dot(pole)).normalized()
    a = math.acos(max(-1, min(1, (THIGH_LEN ** 2 + dist ** 2 - SHIN_LEN ** 2) / (2 * THIGH_LEN * dist))))
    knee = H + (dh * math.cos(a) + pp * math.sin(a)) * THIGH_LEN
    t0 = (TAIL[f'thigh.{n}'] - HEAD[f'thigh.{n}']).normalized(); s0 = (TAIL[f'shin.{n}'] - HEAD[f'shin.{n}']).normalized()
    Dt = t0.rotation_difference(knee - H).to_matrix()
    Ds = (Dt @ s0).rotation_difference(H + dh * dist - knee).to_matrix() @ Dt
    Df = R(x=toe)
    return {f'thigh.{n}': {'rot': R_h.inverted() @ Dt}, f'shin.{n}': {'rot': Dt.inverted() @ Ds}, f'foot.{n}': {'rot': Ds.inverted() @ Df}}

def cap(a, m): return m * math.tanh(a / m)
def secondary(pose):
    # Coat panels follow the thighs (front panel pushed by a forward leg, back panel by a backward one) with a lagging
    # lower segment; '_flow' lifts the coat back (speed), '_flare' opens it outward (spins, landings).
    # Bell strands hang under gravity from the sleeve hems, with '_swing' (deg) to trail them.
    flow, flare, swing = pose.get('_flow', 0.0), pose.get('_flare', 0.0), pose.get('_swing', 0.0)
    bpy.context.view_layer.update()
    Dh = delta('hips')
    for s, n in ((1, 'L'), (-1, 'R')):
        if f'skirtF.{n}' not in pose:
            d = Dh.inverted() @ (pbs[f'thigh.{n}'].tail - pbs[f'thigh.{n}'].head).normalized()
            d0 = (TAIL[f'thigh.{n}'] - HEAD[f'thigh.{n}']).normalized()
            fwd = math.degrees(math.atan2(-d.y, -d.z) - math.atan2(-d0.y, -d0.z))   # + = thigh forward
            out = math.degrees(math.atan2(d.x * s, -d.z) - math.atan2(d0.x * s, -d0.z))
            # a leg pushes its panel only so far (soft cap): past that the cloth drapes over the thigh instead of
            # swinging up level with it like a board
            # (and leg + speed together never lift a panel past ~65 deg, or its lower segment past ~25 deg more)
            front = cap(-cap(max(fwd, 0) * 0.95 + min(fwd, 0) * 0.35, 45) + flow * 0.4, 65)
            back = cap(-cap(max(fwd, 0) * 0.35 + min(fwd, 0) * 0.95, 45) + flow, 65)
            sp = -s * cap(max(out, 0) * 0.7 + flare, 40)
            pose[f'skirtF.{n}'] = {'rot': R(x=front) @ R(y=sp)}; pose[f'skirtB.{n}'] = {'rot': R(x=back) @ R(y=sp)}
            pose[f'skirtF2.{n}'] = {'rot': R(x=cap(front * 0.25 + flow * 0.3, 25))}; pose[f'skirtB2.{n}'] = {'rot': R(x=cap(back * 0.3 + flow * 0.5, 25))}
    for n in ('L', 'R'):
        if f'sleeve.{n}' not in pose:
            pose[f'sleeve.{n}'] = {'rot': delta(f'forearm.{n}').inverted() @ R(x=swing * 0.4)}
        if f'tassel.{n}' in pose: continue
        D = delta(f'forearm.{n}') @ pose[f'sleeve.{n}']['rot']        # the sleeve drape's world rotation (its parent)
        pose[f'tassel.{n}'] = {'rot': D.inverted() @ R(x=swing)}; pose[f'tassel2.{n}'] = {'rot': R(x=swing * 0.5)}
    for n in CLOTH:
        v = pose[n]; pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
def set_pose(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        if n.startswith('_') or n in CLOTH: continue
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        if 'rot' in v: pb.rotation_quaternion = (M.inverted() @ v['rot'] @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])
    if '_ground' in pose:
        bpy.context.view_layer.update()
        lowest = min((pbs[n].matrix @ pbs[n].bone.matrix_local.inverted() @ p).z for n, ps in SOLE.items() for p in ps)
        hb = pbs['hips']; M = hb.bone.matrix_local.to_3x3()
        hb.location = hb.location + M.inverted() @ Vector((0, 0, pose['_ground'] - lowest))
    if pose: secondary(pose)
# ---------------- follow-through: everything loose lags, swings and settles ----------------
# The cloth rules give each loose part a target every frame (coat panels pushed by the legs, sleeves and bell strands
# hung under gravity, banners as carried). Each loose bone's tail is then run as a damped spring around that target in
# world space (8 substeps a frame): it trails when what it hangs from moves, overshoots, and settles. Loops are run
# until they repeat and the small remaining drift is spread over the cycle, so they stay seamless; one-shot clips start
# at rest and ease back onto their targets over the last frames.
# bone: (natural frequency Hz, damping ratio, max degrees off its target, share of straight-down pull on the target)
DYN = {}
for n in 'LR':
    DYN.update({f'skirtF.{n}': (2.2, 0.35, 15, 0), f'skirtF2.{n}': (1.7, 0.25, 22, 0),
                f'skirtB.{n}': (2.2, 0.35, 15, 0), f'skirtB2.{n}': (1.7, 0.25, 22, 0),
                f'sleeve.{n}': (1.6, 0.25, 15, 0), f'tassel.{n}': (1.2, 0.2, 30, 0), f'tassel2.{n}': (1.1, 0.18, 40, 0)})
for nm, _ in BANNERS: DYN[nm] = (2.0, 0.3, 20, 0)
# a banner swings out, away from the shaft, freely; toward the shaft (the fist and forearm) and across the ring plane
# (her head and arm in the two-handed swings) only a few degrees: (outward unit in the bone's rest frame, deg in, deg across)
BAN_LIM = {}
for nm, ob in BANNERS:
    _h = Vector(B[nm][0]); _ax = Ms @ Vector((SHAFT_XY[0], SHAFT_XY[1], (Ms.inverted() @ _h).z))
    BAN_LIM[nm] = (ad.bones[nm].matrix_local.to_3x3().inverted() @ (_h - _ax).normalized(), 2.0, 5.0)
DYN_ORDER = [n for n in B if n in DYN]                     # parents before children
DYN_PAR = {n: B[n][2] for n in DYN_ORDER}
REL = {n: ad.bones[DYN_PAR[n]].matrix_local.inverted() @ ad.bones[n].matrix_local for n in DYN_ORDER}
DOWN = Vector((0, 0, -1))
# a light breeze so nothing loose ever hangs dead still (Idle above all): a slow drift of each target, in whole cycles of
# the clip for loops (seamless), ~0.5 Hz otherwise. Radians of drift by part:
BREEZE = {'skirt': 0.035, 'sleeve': 0.03, 'tassel': 0.045, 'banner': 0.07}
def _breeze(n, ph):
    k = BREEZE[next(b for b in BREEZE if n.startswith(b))]; s = sum(map(ord, n)) * 0.37     # each part its own phase
    return Vector((math.sin(ph + s) + 0.4 * math.sin(2 * ph + 1.3 * s), 0.6 * math.sin(ph + 2.1 + s) + 0.3 * math.sin(3 * ph + s), 0)) * k
def _follow(frames, loop, rec, base):
    """rec[f][bone] = posed matrix (armature space) of each loose bone and its parent at frame f; base[f][bone] = the
    keyed target rotation. Keys the springs' rotations over the targets."""
    SUB = 8; h = 1.0 / sc.render.fps / SUB
    par = {n: (DYN[n], ad.bones[n].length) for n in DYN_ORDER}
    def tgt(Ct, g, L):
        hd = Ct.to_translation(); d = (Ct.to_3x3() @ Vector((0, 1, 0))).normalized()
        if g: d = (d * (1 - g) + DOWN * g).normalized()
        return hd, d, hd + d * L
    st = {}
    for n in DYN_ORDER:
        (_, _, _, g), L = par[n]; _, _, tt = tgt(rec[0][n], g, L); st[n] = [tt.copy(), tt.copy()]
    def step(fa, fb):
        out = {}
        for s in range(1, SUB + 1):
            a = s / SUB; cur = {}
            ph = 2 * math.pi * ((fa + a) / frames if loop else (fa + a) / sc.render.fps * 0.5)
            for n in DYN_ORDER:
                (fq, zeta, mx, g), L = par[n]; pn = DYN_PAR[n]
                Pt = rec[fa][pn].lerp(rec[fb][pn], a); Ct = rec[fa][n].lerp(rec[fb][n], a)
                if pn in cur: Ct = cur[pn] @ Pt.inverted() @ Ct; Pnew = cur[pn]
                else: Pnew = Pt
                hd, d, tt = tgt(Ct, g, L); tt = hd + (tt - hd + _breeze(n, ph) * L).normalized() * L
                w = 2 * math.pi * fq; p, pp = st[n]
                q = p + (p - pp) * (1 - 2 * zeta * w * h) + (tt - p) * (w * w * h * h)
                dn = (q - hd).normalized(); ang = d.angle(dn, 0.0)
                if ang > math.radians(mx): dn = d.slerp(dn, math.radians(mx) / ang).normalized()
                if n in BAN_LIM:
                    ol, din, dac = BAN_LIM[n]; o = Ct.to_3x3() @ ol; o = (o - d * d.dot(o)).normalized(); e2 = d.cross(o)
                    a_ = max(-math.sin(math.radians(din)), dn.dot(o)); b_ = max(-math.sin(math.radians(dac)), min(math.sin(math.radians(dac)), dn.dot(e2)))
                    dn = (d * math.sqrt(max(0.0, 1 - a_ * a_ - b_ * b_)) + o * a_ + e2 * b_).normalized()
                q = hd + dn * L; st[n] = [q, p]
                d0 = (Ct.to_3x3() @ Vector((0, 1, 0))).normalized()
                Cn = Matrix.Translation(hd) @ d0.rotation_difference(dn).to_matrix().to_4x4() @ Ct.to_3x3().to_4x4()
                cur[n] = Cn
                if s == SUB: out[n] = ((Pnew @ REL[n]).inverted() @ Cn).to_quaternion()
        return out
    if loop:
        cyc = max(3, math.ceil(6.0 * sc.render.fps / frames)); keys = None
        for c in range(cyc):
            got = [step((f - 1) % frames, f) if (c or f) else step(0, 0) for f in range(frames)]
            if c == cyc - 1: keys = got
        nxt = step(frames - 1, 0)
        for n in DYN_ORDER:
            dq = nxt[n] @ keys[0][n].inverted()
            for f in range(frames): keys[f][n] = Quaternion().slerp(dq.inverted(), f / frames) @ keys[f][n]
        keys.append({n: q.copy() for n, q in keys[0].items()})
    else:
        F = len(rec); keys = [step(0, 0)] + [step(f - 1, f) for f in range(1, F)]
        for f in range(max(0, F - 6), F):            # ease back onto the targets at the end (hands over to the next clip)
            for n in DYN_ORDER: keys[f][n] = keys[f][n].slerp(base[f][n], sm((f - (F - 7)) / 6))
    for f, kq in enumerate(keys):
        for n in DYN_ORDER:
            q = kq[n]
            if f and q.dot(keys[f - 1][n]) < 0: q = -q; kq[n] = q
            pbs[n].rotation_quaternion = q; pbs[n].keyframe_insert('rotation_quaternion', frame=f + 1, group=n)
LG_TRACE, LG_BY_CLIP = [], {}      # left hand on the staff (0..1) per baked frame, for chimesinger_qa.py
BASE_BY_CLIP = {}                  # the loose bones' target rotations per baked frame (the contact pass falls back to them)
def bake(name, frames, fn, loop):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    LG_TRACE.clear(); rec, base = [], []
    for f in range(frames + (1 if loop else 0)):
        LG_TRACE.append(0.0); set_pose(fn(f, f / frames))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
        bpy.context.view_layer.update()
        rec.append({n: pbs[n].matrix.copy() for n in set(DYN_ORDER) | set(DYN_PAR.values())})
        base.append({n: pbs[n].rotation_quaternion.copy() for n in DYN_ORDER})
    _follow(frames, loop, rec, base); BASE_BY_CLIP[name] = base
    act.frame_range = (1, frames + (1 if loop else 0)); LG_BY_CLIP[name] = [round(v, 3) for v in LG_TRACE]
    return act

# ---------------- stance and the channel-table poser ----------------
# Arm channels are chest-space directions: ru/rf right upper/forearm, rs staff (bell end), rn ring face;
# lu/lf left upper/forearm; lg 0..1 puts the left hand on the staff at la metres from the grip (+ = toward the bell).
# Body: tw twist (+ toward her left), lean, drop (m), hy hips fwd/back (m, - fwd), front right-foot step (m, + fwd),
# back left-foot step (m, + back), spread, flow/flare/swing cloth, hz extra hip height (m, jumps).
STANCE = {
    # relaxed: upper arm down, forearm hanging forward-down, staff upright at her right side, rolled 20 deg in the fist
    # (searched: 5 cm from the coat through the whole idle sway)
    'ru': (-0.462, -0.266, -0.673), 'rf': (-0.304, -0.725, -0.399), 'rs': (0.0, -0.097, 1.0), 'roll_r': 20.7,
    'lu': (0.28, -0.05, -0.96), 'lf': (0.3, -0.4, -0.87),
}
def arms_from(g):
    ru, rf, rh, rg, roll = g('ru'), g('rf'), g('rh', False), g('rg', False), g('roll_r', 0.0)
    pole = g('rpole', (-0.7, 0.2, -0.7))
    if rh is not False: ru, rf = two_bone('R', Vector(rh), pole)
    if rg is not False:
        # grip point target: the wrist sits where the fist puts the shaft on rg, with the hand following the forearm
        for _ in range(5):
            _, _, Df = arm_dirs('R', ru, rf); Dh = hand_R(Df @ FO0['R'], g('rs'), roll)
            ru, rf = two_bone('R', Vector(rg) - Dh @ (GRIP_R - HEAD['hand.R']), pole)
    ar, grip, sdir = arm_R(ru, rf, g('rs'), roll)
    out = dict(ar)
    out['staff'] = staff_pose()
    out.update(fist('R', 1.0))
    lu, lf, lg = Vector(g('lu')), Vector(g('lf')), g('lg', 0.0)
    if LG_TRACE: LG_TRACE[-1] = lg
    hand_rel = None
    if lg > 0:
        P = grip + sdir * g('la', -0.3); pole = g('lpole', (0.6, 0.3, -0.7))
        iu, ifo = two_bone('L', P, pole); _, _, Df0 = arm_dirs('L', iu, ifo)
        W_, fd_, nr_, fw_ = HANDAX['L']; uL = Vector(GRIPSOL['L']['u'])
        best = None
        for sg in (1, -1):   # either grip direction; take the one closer to how the forearm already points
            Dh = _frame(sdir * sg, Df0 @ fd_) @ _frame(uL, fd_).transposed()
            ang = (Df0.inverted() @ Dh).to_quaternion().angle
            if best is None or ang < best[0]: best = (ang, Dh)
        Dh = best[1]
        iu, ifo = two_bone('L', P - Dh @ (_grip_point('L') - W_), pole)   # put the fist's shaft line, not the wrist, on the staff
        lu, lf = slerpv(lu, iu, lg), slerpv(lf, ifo, lg)
        _, _, Df = arm_dirs('L', lu, lf)
        hand_rel = Matrix.Identity(3).to_quaternion().slerp((Df.inverted() @ Dh).to_quaternion(), lg).to_matrix()
    al, _, Df = arm_dirs('L', lu, lf, None if hand_rel is None else arm_dirs('L', lu, lf)[2] @ hand_rel); out.update(al)
    out.update(fist('L', 0.2 + 0.8 * lg))
    return out
# A hand-posed right grip (chimesinger_grip.json, written by chimesinger_grip_extract.py from the grip edit file)
# replaces the computed fist and adds its offset to the staff mount.
import json
_gp = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'chimesinger_grip.json')
GRIP = json.load(open(_gp)) if os.path.exists(_gp) else {}
GRIP = {k: v for k, v in GRIP.items() if k in B}          # a grip posed on an older rig keeps only the bones still here
if not any(k != 'staff' for k in GRIP): GRIP = {}
print('GRIP_POSE', 'hand-posed' if GRIP else 'computed')
def _local(n, k=1.0):
    # the hand-posed bone-local transform of n as a set_pose entry (rest-armature axes), its rotation scaled by k
    M = ad.bones[n].matrix_local.to_3x3(); q = Quaternion(GRIP[n]['rot'])
    q = Quaternion().slerp(q, k)
    return {'rot': M @ q.to_matrix() @ M.inverted(), 'loc': tuple(M @ Vector(GRIP[n]['loc']))}
def staff_pose():
    # the staff is fixed in the fist (a hand-posed grip may carry its own offset); it never slides along the shaft
    return {'rot': _local('staff')['rot'], 'loc': _local('staff')['loc']} if 'staff' in GRIP else {}
def fist(n, k):
    # the solved grip (GRIPSOL rots: rest-axis rotations per finger / thumb joint), scaled: k = 0 open .. 1 closed
    if n == 'R' and GRIP:
        return {b: {'rot': _local(b, k)['rot']} for b in GRIP if b != 'staff'}
    return {f'{b}.{n}': {'rot': Matrix.Rotation(math.radians(a * k), 3, Vector(ax))} for b, (ax, a) in GRIPSOL[n]['rots'].items()}
_, _w, _ = arm_dirs('R', STANCE['ru'], STANCE['rf']); STANCE['rh'] = tuple(round(v, 3) for v in _w)   # stance right wrist
# the IK pole that puts the elbow where the stance has it, so IK-driven clips start and end on the idle pose
_e = HEAD['upperarm.R'] + Vector(STANCE['ru']).normalized() * LEN['R'][0]; _d = (_w - HEAD['upperarm.R']).normalized()
STANCE_POLE = tuple(((_e - HEAD['upperarm.R']) - _d * _d.dot(_e - HEAD['upperarm.R'])).normalized())
STANCE_HZ = 0.0
def base_pose(t, K):
    def g(n, d=None):
        if n in K: return keyp(t, K[n]) if n in POINTS else key(t, K[n])
        if d is not None: return d
        v = STANCE.get(n)
        if isinstance(v, tuple): return Vector(v) if n in POINTS else Vector(v).normalized()
        return v if v is not None else 0.0
    tw, lean, drop, hy = g('tw', 0.0), g('lean', 0.0), g('drop', 0.0), g('hy', 0.0)
    hz = STANCE_HZ - drop + g('hz', 0.0); hp = lean * 0.3
    s = {
        'hips': {'loc': (0, hy, hz), 'rot': Rzyx(x=hp, y=g('roll', 0.0), z=tw * 0.4)},
        'spine': {'rot': Rzyx(x=2 + lean * 0.7, z=tw * 0.3)}, 'chest': {'rot': Rzyx(x=g('chest', 0.0), z=tw * 0.4)},
        'head': {'rot': Rzyx(x=-lean * 0.6 + g('nod', 0.0), z=-tw * 0.6)},
        '_flow': g('flow', 0.0), '_flare': g('flare', 0.0), '_swing': g('swing', 0.0),
    }
    s.update(arms_from(g))
    sp = g('spread', 0.0)
    if STANCE_ANKLE:
        # feet planted where the stance put them (plus any step), whatever the hips do: no sliding soles
        R_h = s['hips']['rot']; L_h = s['hips']['loc']; out_ = math.sin(math.radians(sp)) * 0.75
        def lift(ch):   # a moving foot comes off the floor (highest mid-step), so steps don't skate
            if ch not in K: return 0.0
            return min(0.08, 0.8 * abs(key(min(1, t + 0.02), K[ch]) - key(max(0, t - 0.02), K[ch])))
        # widening the stance ('spread') is a step out with the left foot
        s.update(ik_leg3(-1, STANCE_ANKLE['R'] + Vector((0, -g('front', 0.0), lift('front'))), R_h, L_h, toe=g('toeR', 0.0)))
        s.update(ik_leg3(1, STANCE_ANKLE['L'] + Vector((2 * out_, g('back', 0.0), max(lift('back'), 2 * lift('spread') * 0.013))), R_h, L_h, toe=g('toeL', 0.0)))
    return s
STANCE_ANKLE_Y = STANCE_ANKLE = None
# stance hip height: soft knees, both soles on the floor
_s = {'_ground': 0.0}; _s.update(leg(-1, -4, 10, spread=2)); _s.update(leg(1, -2, 8, spread=2)); set_pose(_s)
bpy.context.view_layer.update(); _hb = pbs['hips']; STANCE_HZ = (_hb.bone.matrix_local.to_3x3() @ _hb.location).z
STANCE_ANKLE_Y = {s: pbs[f'foot.{s}'].head.y for s in ('L', 'R')}
STANCE_ANKLE = {s: pbs[f'foot.{s}'].head.copy() for s in ('L', 'R')}
print('STANCE', round(STANCE_HZ, 3), {k: round(v, 3) for k, v in STANCE_ANKLE_Y.items()})
def kit_pose(K): return lambda f, t: base_pose(t, K)

IDLE_AMP = 1.0
def idle(f, t):
    # calm: breathing (a small chest rise and dip), a tiny weight shift; feet stay planted (ik_leg3)
    p = 2 * math.pi * t; b = math.sin(p); a = IDLE_AMP; s0 = Vector(STANCE['rs'])
    rs = s0 + Vector((0.012 * math.sin(p + 0.4), 0.01 * math.sin(2 * p), 0)) * a
    K = {'chest': [(0, 1.2 * a * math.sin(2 * p))] * 2, 'tw': [(0, 1.0 * a * math.sin(p + 0.5))] * 2, 'roll': [(0, 0.8 * a * b)] * 2,
         'rs': [(0, tuple(rs))] * 2,
         'lf': [(0, (0.3, -0.4 + 0.02 * a * math.sin(p + 1), -0.87))] * 2,
         'lu': [(0, (0.28, -0.05 + 0.015 * a * math.sin(p + 0.6), -0.96))] * 2,
         'swing': [(0, 1.5 * a * math.sin(p - 0.8))] * 2,
         'drop': [(0, 0.005 * a * (1 - math.cos(2 * p)))] * 2, 'nod': [(0, -0.8 * a * math.sin(2 * p + 0.6))] * 2,
         'hy': [(0, 0.004 * a * math.sin(p + 2))] * 2}
    return base_pose(0, K)

# ---------------- world-space staff aiming (strikes / plants where the staff has to hit an exact spot) ----------------
GRIP_L_LOCAL = ad.bones['hand.L'].matrix_local.inverted() @ _grip_point('L')     # the left fist's shaft line, hand space
def world_aim(t, K, G, S, roll=0.0):
    # Pose the body from K, then aim the staff by world targets: G the grip point (on the shaft axis in the fist), S the
    # bell direction. They are carried into chest space for the arm IK (rg / rs).
    # With both hands on (lg = 1): the staff's roll (the banners' side) is set so the ring faces between the two forearms,
    # keeping both forearms clear of the banners, and roll becomes an offset from that. If the left arm is at full stretch
    # the staff is moved onto the left fist's shaft line (the right arm follows), so both fists stay locked on the shaft.
    set_pose(base_pose(t, K)); bpy.context.view_layer.update()
    Mi = (pbs['chest'].matrix @ pbs['chest'].bone.matrix_local.inverted()).inverted()
    G, S = Vector(G), Vector(S).normalized(); lgv = key(t, K['lg']) if 'lg' in K else 0.0; both = lgv > 0
    for it in range(6 if both else 1):
        K2 = dict(K, rg=[(0, tuple(Mi @ G))] * 2, rs=[(0, tuple((Mi.to_3x3() @ S).normalized()))] * 2, roll_r=[(0, roll)] * 2)
        pose = base_pose(t, K2)
        if not both: break
        set_pose(pose); bpy.context.view_layer.update()
        q = (arm.matrix_world @ pbs['hand.L'].matrix) @ GRIP_L_LOCAL; a = staff.matrix_world @ Vector((SHAFT_XY[0], SHAFT_XY[1], 0))
        off = (q - a) - S * S.dot(q - a)
        fo = [(pbs[f'forearm.{n}'].head - pbs[f'forearm.{n}'].tail) for n in 'RL']          # wrist -> elbow
        fo = [(v - S * S.dot(v)).normalized() for v in fo]
        ring = (staff.matrix_world.to_3x3() @ Vector((0, 1, 0))); ring = (ring - S * S.dot(ring)).normalized()
        bis = (fo[0] + fo[1]).normalized()
        d = math.degrees(math.atan2(ring.cross(bis).dot(S), ring.dot(bis)))   # turn the ring toward the bisector
        if (off.length < 0.002 or lgv < 0.999) and abs(d * lgv) < 1: break
        roll = roll + d * lgv                   # blended in as the left hand closes, so the staff never pops
        if lgv >= 0.999: G = G + off
    return pose
def seg(t, t0, t1): return min(1.0, max(0.0, (t - t0) / (t1 - t0)))
def bez(a, b, c, u): a, b, c = Vector(a), Vector(b), Vector(c); return a * (1 - u) ** 2 + b * (2 * u * (1 - u)) + c * (u * u)
_STANCE_GS = []
def stance_gs():
    # world grip point and bell direction of the stance pose
    if not _STANCE_GS:
        set_pose(base_pose(0, {})); bpy.context.view_layer.update(); m = staff.matrix_world
        _STANCE_GS.extend([m @ Vector((SHAFT_XY[0], SHAFT_XY[1], GRIP_AT)), (m.to_3x3() @ Vector((0, 0, 1))).normalized()])
    return _STANCE_GS

# Bellstrike: one-handed overhead strike, bell end into the floor ahead of her centre line at .5 (30 frames).
# One hand, by reach: at the top of the wind-up the staff has to be out to her right (its banners hang 20 cm either side
# of the fist, and an overhead-centred staff sweeps them through her head and halo), and there the shaft is ~0.55 m
# from her left shoulder against 0.46 m of reach.
# Physics: the staff never moves in the fist. Wind-up (0-.3): weight back, the hand rises out and back over her right
# shoulder while the heavy bell end trails (the staff angle finishes .04 after the hand). Downswing (.3-.5, 6 frames):
# step, then hips (settle .44), twist (.46), chest (.48), hand (.5) and last the staff, whose angle accelerates all the
# way in (ease-in t^2.6) so the bell end is fastest at contact. Contact .5: the bell end ~1.1 m ahead on her centre line,
# as far as the 0.92 m bell end and her short arms reach from a lunge with the wrist under 30 deg (searched against the
# mesh overlap check). Then a one-frame bounce, a two-frame hold and recovery to idle.
# Key poses (searched): G grip point, phi staff angle in the swing plane (0 up, + forward), roll staff roll at the wrist.
BS = {'G1': (-0.821, -0.495, 1.341), 'phi1': -64.0, 'roll1': 8.1,
      'G2': (-0.318, -0.741, 0.785), 'phi2': None, 'roll2': 38.5, 'lat': 0.827,
      'Gw': (-0.585, -0.406, 1.378), 'Gd': (-1.385, -1.063, 1.244), 'Gr': (-0.623, -1.144, 1.793), 'bounce': 3.0, 'whip': 1.6, 'gpow': 1.0, 'rolld': 15.0, 'rollr': 5.3, 'Sr': (-0.618, 0.236, 0.326)}
_bs_up = Vector((0, 0, 1))
def _bs_sphi(a):
    fw = Vector((BS['lat'], -1, 0)).normalized(); return (_bs_up * math.cos(math.radians(a)) + fw * math.sin(math.radians(a))).normalized()
def bellstrike(f, t):
    G0, S0 = stance_gs()
    p1 = BS['phi1']; p2 = BS['phi2'] or 90 + math.degrees(math.asin(min(0.99, (BS['G2'][2] - 0.01) / 0.92)))
    # staff angle in the swing plane: trails back (.04-.34), whips through (.34-.50, ease-in), bounce, hold, recover
    if t < 0.27: phi = p1 * smooth(seg(t, 0.03, 0.27))
    elif t <= 0.5: phi = p1 + (p2 - p1) * seg(t, 0.27, 0.5) ** BS['whip']
    elif t < 0.534: phi = p2 - BS['bounce'] * smooth(seg(t, 0.5, 0.534))
    else: phi = p2 - BS['bounce'] * (1 - smooth(seg(t, 0.534, 0.567)))
    S = _bs_sphi(phi)
    if t < 0.2: S = S0.slerp(S, smooth(seg(t, 0.0, 0.2)))           # leave the stance angle (off the swing plane)
    if t > 0.633:      # recovery: the bell swings up past her right side (out of the strike line), then back to the stance
        u = smooth(seg(t, 0.633, 1.0)); Sr = Vector(BS['Sr']).normalized()
        S = _bs_sphi(p2).slerp(Sr, min(1, u * 2)) if u < 0.5 else Sr.slerp(S0, (u - 0.5) * 2)
    G2 = Vector(BS['G2'])
    if t < 0.25: G = bez(G0, BS['Gw'], BS['G1'], smooth(seg(t, 0.0, 0.25)))
    elif t <= 0.5: G = bez(BS['G1'], BS['Gd'], G2, (1 - math.cos(math.pi * seg(t, 0.25, 0.5) ** BS['gpow'])) / 2)   # hand peaks before contact, brakes into it
    elif t < 0.534: G = G2 + Vector((0, 0, 0.015)) * smooth(seg(t, 0.5, 0.534))
    elif t < 0.633: G = G2 + Vector((0, 0, 0.015)) * (1 - smooth(seg(t, 0.534, 0.567)))
    else: G = bez(G2, BS['Gr'], G0, smooth(seg(t, 0.633, 1.0)))
    roll = key(t, [(0, 0.0), (0.25, BS['roll1']), (0.38, BS['rolld']), (0.5, BS['roll2']), (0.633, BS['roll2']), (0.8, BS['rollr']), (1, 0.0)])
    K = {'hy': [(0, 0), (0.25, 0.1), (0.42, -0.25), (0.633, -0.24), (1, 0)],
         'tw': [(0, 0), (0.25, 18), (0.45, -11), (0.633, -10), (1, 0)],
         'lean': [(0, 0), (0.25, -14), (0.48, 23), (0.633, 22), (1, 0)], 'chest': [(0, 0), (0.25, -6), (0.48, 6), (0.633, 5), (1, 0)],
         'drop': [(0, 0), (0.25, -0.01), (0.45, 0.195), (0.633, 0.19), (1, 0)], 'nod': [(0, 0), (0.25, -8), (0.5, -3.6), (1, 0)],
         'front': [(0, 0), (0.27, 0), (0.42, 0.447), (0.8, 0.447), (1, 0)], 'back': [(0, 0), (0.25, 0.05), (0.42, 0.266), (0.8, 0.266), (1, 0)],
         'roll': [(0, 0), (0.25, -3), (0.45, 3), (1, 0)], 'toeL': [(0, 0), (0.3, 0), (0.45, 18), (0.8, 18), (1, 0)],
         'lu': [(0, STANCE['lu']), (0.25, (0.35, -0.75, -0.55)), (0.5, (0.55, 0.6, -0.58)), (0.633, (0.55, 0.6, -0.58)), (1, STANCE['lu'])],
         'lf': [(0, STANCE['lf']), (0.25, (0.2, -0.95, 0.1)), (0.5, (0.5, 0.7, -0.5)), (0.633, (0.5, 0.7, -0.5)), (1, STANCE['lf'])],
         'rpole': [(0, STANCE_POLE), (0.15, (-0.7, 0.2, -0.7)), (0.8, (-0.7, 0.2, -0.7)), (1, STANCE_POLE)],
         'swing': [(0, 0), (0.25, -10), (0.52, 15), (1, 0)], 'flow': [(0, 0), (0.5, 10), (1, 0)], 'flare': [(0, 0), (0.5, 6), (1, 0)]}
    return world_aim(t, K, G, S, roll)


# Crescendo: the left hand takes the staff (.1-.25), both hands raise it high in front (.36), a small lift (.46), then
# drive the butt into the floor in front with a deep lunge (.533 = frame 17, bell up), hold, rise and let go (.8-.9).
# The grip never changes: the right fist stays at 0.58 m from the butt, so planting the butt on the floor puts that fist
# 0.58 m up and she has to sink into a deep lunge; the left fist stays 0.196 m above it on the shaft (held there by IK)
# the whole time both hands are on. Key poses searched against the mesh overlap check, wrist and both-fists reach.
CR = {'G1': (-0.053, -0.532, 1.465), 'S1': (0.052, 0.33, 1.0), 'S1h': (-0.219, 0.688, 1.269), 'roll1': 16.7,
      'butt': (-0.144, -0.836), 'S2': (-0.055, 0.027, 1.0), 'roll2': 0.0, 'la': LEFT_AT - GRIP_AT, 'Gc': (-0.052, -0.856, 1.447), 'Gr': (-0.35, -0.55, 0.9),
      'lift': (0.171, -0.19, 0.004), 'Gd': (0.235, -0.508, 1.396), 'lu0': (0.753, 0.331, -0.507), 'lf0': (-0.631, -0.958, 0.499),
      'lg0': (0.051, 0.34), 'mid': (14.3, 0.061), 'lpole2': (0.7, 0.2, -0.7)}
def crescendo(f, t):
    G0, S0 = stance_gs()
    S1, S2 = Vector(CR['S1']).normalized(), Vector(CR['S2']).normalized()
    G1 = Vector(CR['G1']); G1h = G1 + Vector(CR['lift']); S1h = Vector(CR['S1h']).normalized()
    G2 = Vector((CR['butt'][0], CR['butt'][1], 0.0)) + S2 * GRIP_AT + Vector((0, 0, 0.012))
    if t < 0.36: u = smooth(seg(t, 0.0, 0.36)); G = bez(G0, CR['Gc'], G1, u); S = S0.slerp(S1, u)
    elif t < 0.46: u = smooth(seg(t, 0.36, 0.46)); G = G1.lerp(G1h, u); S = S1.slerp(S1h, u)
    elif t <= 0.533: u = seg(t, 0.46, 0.533) ** 2; G = bez(G1h, CR['Gd'], G2, u); S = S1h.slerp(S2, u)   # driven down, accelerating
    elif t < 0.7: G, S = G2, S2
    else: u = smooth(seg(t, 0.7, 1.0)); G = bez(G2, CR['Gr'], G0, u); S = S2.slerp(S0, u)
    roll = key(t, [(0, 0.0), (0.36, CR['roll1']), (0.46, CR['roll1']), (0.533, CR['roll2']), (0.7, CR['roll2']), (1, 0.0)])
    K = {'lg': [(0, 0), (CR['lg0'][0], 0), (CR['lg0'][1], 1), (0.72, 1), (0.86, 0), (1, 0)], 'la': [(0, CR['la'])] * 2,
         'lpole': [(0, (0.9, 0.1, -0.4)), (0.46, (0.9, 0.1, -0.4)), (0.56, CR['lpole2']), (1, CR['lpole2'])],
         # free left arm swings out in front before it takes the staff (and after it lets go), so the elbow never cuts the waist
         'lu': [(0, STANCE['lu']), (CR['lg0'][0], CR['lu0']), (0.86, CR['lu0']), (1, STANCE['lu'])],
         'lf': [(0, STANCE['lf']), (CR['lg0'][0], CR['lf0']), (0.86, CR['lf0']), (1, STANCE['lf'])],
         'lean': [(0, 0), (0.36, 1.3), (0.46, 0), (0.5, CR['mid'][0]), (0.533, 19.8), (0.7, 19.8), (1, 0)], 'tw': [(0, 0), (0.36, -17.5), (0.46, -14), (0.533, 14.6), (0.7, 14.6), (1, 0)],
         'nod': [(0, 0), (0.36, -12), (0.46, -14), (0.533, -8), (0.7, -8), (1, 0)], 'chest': [(0, 0), (0.46, 0), (0.533, -4), (0.7, -4), (1, 0)],
         'drop': [(0, 0), (0.25, 0.03), (0.36, -0.03), (0.46, -0.06), (0.5, CR['mid'][1]), (0.533, 0.414), (0.7, 0.414), (1, 0)],
         'hy': [(0, 0), (0.46, 0), (0.533, -0.19), (0.7, -0.19), (1, 0)], 'front': [(0, 0), (0.46, 0), (0.533, 0.214), (0.75, 0.214), (1, 0)],
         'back': [(0, 0), (0.46, 0.02), (0.533, 0.484), (0.75, 0.484), (1, 0)], 'spread': [(0, 0), (0.533, 8), (0.75, 8), (1, 0)],
         'toeL': [(0, 0), (0.46, 0), (0.533, 30), (0.75, 30), (1, 0)],
         'swing': [(0, 0), (0.45, -8), (0.6, 18), (0.8, 6), (1, 0)], 'flare': [(0, 0), (0.55, 3), (0.6, 12), (0.85, 4), (1, 0)],
         'rpole': [(0, STANCE_POLE), (0.15, (-0.7, 0.2, -0.7)), (0.85, (-0.7, 0.2, -0.7)), (1, STANCE_POLE)]}
    return world_aim(t, K, G, S, roll)

# Mending Chorus: instant heal. The staff goes out to her right side first (.12), then straight up (.28, the peak, shaft
# 30 cm clear of her head and halo), free arm swept wide, coat blooming; held to .6, then back to idle. Routed around
# her body, not through it: key poses and path searched against the mesh overlap check.
MC = {'Gs': (-0.62, -0.12, 1.2), 'Ss': (-50.0, 72.0), 'rolls': 0.0, 'G1': (-0.457, -0.151, 1.524), 'S1': (22.2, 87.1), 'roll1': 9.6,
      'Gc': (-0.6, -0.15, 1.0), 'Gr': (-0.6, -0.2, 1.3)}
def dir_yp(yaw, pitch):
    # unit direction: yaw 0 = her front (-Y), + toward her left (+X); pitch + up
    y, p = math.radians(yaw), math.radians(pitch); return Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
def mending_chorus(f, t):
    G0, S0 = stance_gs()
    Ss, S1 = dir_yp(*MC['Ss']), dir_yp(*MC['S1'])
    if t < 0.12: u = smooth(seg(t, 0, 0.12)); G = bez(G0, MC['Gc'], MC['Gs'], u); S = S0.slerp(Ss, u)
    elif t < 0.28: u = smooth(seg(t, 0.12, 0.28)); G = Vector(MC['Gs']).lerp(Vector(MC['G1']), u); S = Ss.slerp(S1, u)
    elif t < 0.6:      # held: the staff keeps rising a little and sways as the heal rings out
        h = seg(t, 0.28, 0.6); G = Vector(MC['G1']) + Vector((0.012 * math.sin(h * math.pi * 2), 0, 0.03 * smooth(h) - 0.01 * h))
        S = dir_yp(MC['S1'][0] + 4 * math.sin(h * math.pi * 2), MC['S1'][1] - 3 * math.sin(h * math.pi))
    else: u = smooth(seg(t, 0.6, 1.0)); G = bez(Vector(MC['G1']) + Vector((0, 0, 0.02)), MC['Gr'], G0, u); S = S1.slerp(S0, u)
    roll = key(t, [(0, 0.0), (0.12, MC['rolls']), (0.28, MC['roll1']), (0.6, MC['roll1']), (1, 0.0)])
    K = {'tw': [(0, 0), (0.12, 4), (0.28, -5.2), (0.6, -4), (1, 0)], 'lean': [(0, 0), (0.12, 6), (0.28, -12.6), (0.6, -10), (1, 0)],
         'chest': [(0, 0), (0.12, 2), (0.28, -12), (0.44, -13), (0.6, -9), (1, 0)], 'drop': [(0, 0), (0.12, 0.06), (0.28, -0.035), (0.6, -0.03), (0.8, 0.02), (1, 0)],
         'nod': [(0, 0), (0.12, 4), (0.28, -14), (0.6, -12), (1, 0)], 'spread': [(0, 0), (0.12, 3), (0.28, 8), (0.6, 8), (1, 0)],
         'roll': [(0, 0), (0.12, 3), (0.28, -4), (0.6, -3), (1, 0)], 'toeL': [(0, 0), (0.15, 0), (0.28, 12), (0.6, 10), (0.8, 0)], 'toeR': [(0, 0), (0.15, 0), (0.28, 12), (0.6, 10), (0.8, 0)],
         'lu': [(0, STANCE['lu']), (0.12, (0.4, -0.7, -0.5)), (0.28, (0.9, -0.15, 0.4)), (0.44, (0.85, -0.12, 0.5)), (0.6, (0.9, -0.15, 0.35)), (1, STANCE['lu'])],
         'lf': [(0, STANCE['lf']), (0.12, (-0.3, -0.9, 0.2)), (0.28, (0.8, -0.3, 0.55)), (0.6, (0.85, -0.3, 0.45)), (1, STANCE['lf'])],
         'rpole': [(0, STANCE_POLE), (0.12, (-0.8, 0.3, -0.5)), (0.8, (-0.8, 0.3, -0.5)), (1, STANCE_POLE)],
         'flare': [(0, 0), (0.12, 4), (0.3, 20), (0.6, 10), (1, 0)], 'flow': [(0, 0), (0.12, 6), (0.3, -10), (0.6, -4), (1, 0)],
         'swing': [(0, 0), (0.12, -6), (0.28, 12), (0.6, -5), (1, 0)]}
    return world_aim(t, K, G, S, roll)

# Resonant Note: 0.4 s basic. Cock-back (.2): weight back, torso wound 30 deg to the right, the bell drawn back over her
# right shoulder. Flick (.2-.45): a step in, the torso unwinds and the staff whips over the top until the bell end points
# level at the target (accelerating into the release); held to .6, then back to idle with the foot.
RN = {'G1': (-0.471, -0.037, 1.572), 'phi1': 153.4, 'roll1': 5.0, 'G2': (-0.52, -0.297, 1.368), 'phi2': -4.1, 'roll2': 35.7,
      'Gc': (-0.55, -0.05, 1.1), 'Gd': (-1.279, -0.147, 0.84), 'Gr': (-0.515, -0.371, 1.0), 'snap': 1.0}
def resonant_note(f, t):
    G0, S0 = stance_gs()
    fwd, up = Vector((0, -1, 0)), Vector((0, 0, 1))
    Sp = lambda a: (fwd * math.cos(math.radians(a)) + up * math.sin(math.radians(a))).normalized()   # 0 = level ahead, 90 = up
    S1, S2 = Sp(RN['phi1']), Sp(RN['phi2'])
    if t < 0.2: u = smooth(seg(t, 0, 0.2)); G = bez(G0, RN['Gc'], RN['G1'], u); S = S0.slerp(S1, u)
    elif t <= 0.45:
        u = seg(t, 0.2, 0.45); G = bez(RN['G1'], RN['Gd'], RN['G2'], smooth(u)); S = Sp(RN['phi1'] + (RN['phi2'] - RN['phi1']) * u ** RN['snap'])
    elif t < 0.6: G, S = Vector(RN['G2']), S2
    else: u = smooth(seg(t, 0.6, 1.0)); G = bez(RN['G2'], RN['Gr'], G0, u); S = S2.slerp(S0, u)
    roll = key(t, [(0, 0.0), (0.2, RN['roll1']), (0.45, RN['roll2']), (0.6, RN['roll2']), (1, 0.0)])
    K = {'tw': [(0, 0), (0.2, 30), (0.45, -19.3), (0.6, -18), (1, 0)], 'lean': [(0, 0), (0.2, -7.2), (0.45, 6.1), (0.6, 6), (1, 0)],
         'hy': [(0, 0), (0.2, 0.04), (0.45, -0.07), (0.65, -0.07), (1, 0)], 'drop': [(0, 0), (0.2, 0), (0.45, 0.04), (0.65, 0.04), (1, 0)],
         'front': [(0, 0), (0.25, 0), (0.42, 0.2), (0.7, 0.2), (1, 0)], 'back': [(0, 0), (0.2, 0.05), (0.45, 0.0), (1, 0)],
         'roll': [(0, 0), (0.2, -3), (0.45, 3), (1, 0)], 'nod': [(0, 0), (0.2, -4), (0.45, 3), (1, 0)],
         'lu': [(0, STANCE['lu']), (0.2, (0.45, -0.55, -0.7)), (0.45, (0.55, 0.4, -0.73)), (0.6, (0.55, 0.4, -0.73)), (1, STANCE['lu'])],
         'lf': [(0, STANCE['lf']), (0.2, (0.3, -0.85, -0.2)), (0.45, (0.5, 0.35, -0.8)), (0.6, (0.5, 0.35, -0.8)), (1, STANCE['lf'])],
         'rpole': [(0, STANCE_POLE), (0.15, (-0.7, 0.2, -0.7)), (0.85, (-0.7, 0.2, -0.7)), (1, STANCE_POLE)],
         'swing': [(0, 0), (0.2, -6), (0.45, 10), (1, 0)], 'flare': [(0, 0), (0.45, 6), (1, 0)]}
    return world_aim(t, K, G, S, roll)

# Harmonic Refrain: flat sweep. Side-load low on her right (.25, bell pointing out to her right), one continuous sweep
# across her front (bell straight ahead at the release, .5, where it moves fastest) to finish on her left (.7), held to
# .8, back to idle. The twist comes from the body (-36 -> -5 -> +38 deg), the staff stays level. Key poses searched
# against the mesh overlap check; the fist is out to her right at the release so the butt end passes beside her hip.
HR = {'G': [(-0.558, -0.726, 1.338), (-0.5, -0.35, 1.05), (-0.048, -0.996, 1.264)], 'yaw': (-110.0, 0.0, 85.0), 'pitch': (-0.3, 0.0, -8.0),
      'roll': (-6.0, 0.0, -1.0), 'tw': (-39.9, -5.0, 40.1), 'lean': (8.0, 10.0, 3.1), 'Gc': (-0.661, -0.566, 1.357), 'Gr': (0.053, -0.806, 1.354),
      'bump1': (-0.18, -0.4, 0.948), 'bump2': (-0.433, -0.131, -0.166), 'pb': (-35.0, 5.0), 'rb': (22.1, -30.8), 'pole': (-3.221, 1.524, -2.796), 'lag': 1.14,
      'lu0': (0.531, -0.155, -0.403), 'lf0': (0.268, -0.797, 0.029)}
def _q3(a, b, c, u):   # quadratic through a (u=0), b (u=.5), c (u=1)
    return a * (1 - u) * (1 - 2 * u) + b * 4 * u * (1 - u) + c * u * (2 * u - 1)
def harmonic_refrain(f, t):
    G0, S0 = stance_gs()
    Ga, Gb, Gc_ = (Vector(v) for v in HR['G'])
    yaw, pitch, rl, tw, ln = (lambda k: HR[k])('yaw'), HR['pitch'], HR['roll'], HR['tw'], HR['lean']
    if t < 0.25:
        u = smooth(seg(t, 0, 0.25)); G = bez(G0, HR['Gc'], Ga, u); S = S0.slerp(dir_yp(yaw[0], pitch[0]), u)
        r, w, l = rl[0] * u, tw[0] * u, ln[0] * u
    elif t <= 0.7:
        u = smooth(seg(t, 0.25, 0.7))        # one move: fastest through the middle (the release)
        b1, b2 = max(0.0, math.sin(2 * math.pi * u)), max(0.0, -math.sin(2 * math.pi * u))   # mid-sweep offsets (first / second half)
        G = _q3(Ga, Gb, Gc_, u) + Vector(HR['bump1']) * b1 + Vector(HR['bump2']) * b2
        ul = u ** HR['lag']                    # the heavy bell end trails the fist early in the sweep
        S = dir_yp(_q3(*yaw, ul), _q3(*pitch, ul) + HR['pb'][0] * b1 + HR['pb'][1] * b2)
        r, w, l = _q3(*rl, u) + HR['rb'][0] * b1 + HR['rb'][1] * b2, _q3(*tw, u), _q3(*ln, u)
    elif t < 0.8: G, S, r, w, l = Gc_, dir_yp(yaw[2], pitch[2]), rl[2], tw[2], ln[2]
    else:
        u = smooth(seg(t, 0.8, 1.0)); G = bez(Gc_, HR['Gr'], G0, u); S = dir_yp(yaw[2], pitch[2]).slerp(S0, u)
        r, w, l = rl[2] * (1 - u), tw[2] * (1 - u), ln[2] * (1 - u)
    K = {'tw': [(0, w)] * 2, 'lean': [(0, l)] * 2, 'drop': [(0, 0), (0.25, 0.08), (0.5, 0.1), (0.8, 0.08), (1, 0)], 'spread': [(0, 0), (0.25, 6), (0.8, 6), (1, 0)],
         'front': [(0, 0), (0.3, 0), (0.48, 0.22), (0.82, 0.22), (1, 0)], 'back': [(0, 0), (0.25, 0.2), (0.82, 0.22), (1, 0)],
         'hy': [(0, 0), (0.25, 0.04), (0.5, -0.05), (0.75, -0.06), (1, 0)], 'roll': [(0, 0), (0.25, -3), (0.5, 0), (0.75, 3), (1, 0)],
         'lu': [(0, STANCE['lu']), (0.25, HR['lu0']), (0.5, (0.6, 0.45, -0.65)), (0.8, (0.6, 0.5, -0.6)), (1, STANCE['lu'])],
         'lf': [(0, STANCE['lf']), (0.25, HR['lf0']), (0.5, (0.75, 0.45, -0.45)), (0.8, (0.8, 0.45, -0.4)), (1, STANCE['lf'])],
         'rpole': [(0, STANCE_POLE), (0.15, (-0.7, 0.2, -0.7)), (0.4, HR['pole']), (0.6, (-0.7, 0.2, -0.7)), (0.85, (-0.7, 0.2, -0.7)), (1, STANCE_POLE)],
         'swing': [(0, 0), (0.25, -10), (0.6, 15), (1, 0)], 'flare': [(0, 0), (0.25, 4), (0.6, 10), (1, 0)]}
    return world_aim(t, K, G, S, r)

# ---------------- the Chimesinger kit (board #36). Distinct top-down silhouettes:
#   Resonant Note: one-arm flick, bell end pointed at the target.  Bellstrike: one-handed overhead strike into the floor.
#   Harmonic Refrain: wide flat sweep.  Crescendo: staff raised high in both hands, then planted in a deep lunge.
#   Mending Chorus: out to the side, then straight up, free arm wide.  Harmonic Step (dash): low glide, staff trailing.
# The ability clips are functions above (world-space staff targets + world_aim); their key poses and paths were searched
# against the mesh-level staff/body overlap check, the wrist angle and both-fist reach (chimesinger_qa.Checker).
# Clip lengths are prototype values at 30 fps; contact frames are listed in CLIP_EVENTS for integration.
KIT = {
    'ResonantNote': (12, resonant_note),
    'Bellstrike': (30, bellstrike),
    'HarmonicRefrain': (22, harmonic_refrain),
    'Crescendo': (30, crescendo),
    'MendingChorus': (18, mending_chorus),
}
CLIP_EVENTS = {  # proposed integration markers (normalised clip time): anticipation end / contact or release / cancel-before / recovery start
    'ResonantNote': {'release': 0.45, 'cancel_before': 0.4, 'recover': 0.7},
    'Bellstrike': {'contact': 0.5, 'cancel_before': 0.42, 'recover': 0.7},
    'HarmonicRefrain': {'release': 0.5, 'cancel_before': 0.4, 'recover': 0.75},
    'Crescendo': {'detonate': 0.533, 'cancel_before': 0.5, 'recover': 0.7},
    'MendingChorus': {'heal': 0.0, 'pose_peak': 0.28, 'recover': 0.6},
    'Dodge': {'move_start': 0.1, 'move_end': 0.7},
}
DODGE_R = ((-0.689, -0.201, -0.757), (-0.737, -0.252, -0.326), (-0.095, 0.381, -0.272))   # right arm and staff (trailing level behind her) in the glide
def dodge(f, t):
    # Harmonic Step: drop into a low, long, lean-forward glide (legs split, back heel up) with the staff trailing behind,
    # coat streaming; recover by .85
    d = key(t, [(0, 0), (0.1, 0.6), (0.25, 1), (0.7, 1), (0.85, 0.2), (1, 0)])
    K = {
        'lean': [(0, 34 * d + 4 * d * math.sin(seg(t, 0.25, 0.7) * math.pi))] * 2, 'drop': [(0, 0.2 * d + 0.04 * d * math.sin(seg(t, 0.2, 0.75) * math.pi))] * 2, 'hy': [(0, -0.16 * d)] * 2, 'tw': [(0, -10 * d)] * 2, 'nod': [(0, -14 * d)] * 2,
        'front': [(0, 0.38 * d)] * 2, 'back': [(0, 0.4 * d)] * 2, 'toeL': [(0, 25 * d)] * 2, 'spread': [(0, 4 * d)] * 2,
        'ru': [(0, tuple(slerpv(STANCE['ru'], DODGE_R[0], d)))] * 2, 'rf': [(0, tuple(slerpv(STANCE['rf'], DODGE_R[1], d)))] * 2,
        'rs': [(0, tuple(slerpv(STANCE['rs'], Vector(DODGE_R[2]) + Vector((0, 0.15, -0.15)) * math.sin(seg(t, 0.2, 0.8) * math.pi), d)))] * 2,
        'lu': [(0, tuple(slerpv(STANCE['lu'], (0.4, 0.55, -0.73), d)))] * 2, 'lf': [(0, tuple(slerpv(STANCE['lf'], (0.3, 0.75, -0.6), d)))] * 2,
        'flow': [(0, 40 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2, 'flare': [(0, 10 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2,
        'swing': [(0, 20 * key(t, [(0, 0), (0.4, 1), (1, 0)]))] * 2,
    }
    s = base_pose(0, K)
    s['_ground'] = 0.03 * key(t, [(0, 0), (0.2, 0), (0.4, 1), (0.6, 0), (1, 0)])   # a skim, not a hop
    return s
JUMP_C = ((-0.834, -0.306, -0.652), (-1.0, -0.577, -0.566), (-0.135, 0.13, 0.7))   # right arm / staff in the crouches
JUMP_A = ((-0.737, -0.333, -0.564), (-0.48, -0.435, 0.263), (-0.734, 0.574, 1.0))   # and in the air (both searched: 6 cm from the coat)
def jump(f, t):
    crouch = key(t, [(0, 0), (0.15, 1), (0.2, 0.35), (0.25, 0), (0.7, 0), (0.76, 0.85), (0.86, 0.5), (1, 0)])
    ext = key(t, [(0, 0), (0.15, 0), (0.21, 1), (0.28, 0), (1, 0)])
    air = key(t, [(0, 0), (0.2, 0), (0.34, 1), (0.6, 1), (0.72, 0), (1, 0)])
    h = 0.0
    if 0.2 < t < 0.73: u = (t - 0.2) / 0.53; h = 0.5 * 4 * u * (1 - u)
    pitch = 12 * crouch + 15 * crouch * (t > 0.7)
    # in the air: staff out to her right, free arm up and out for balance; the landing absorbs without folding
    ra = lambda i: tuple(slerpv(slerpv(STANCE[('ru', 'rf', 'rs')[i]], JUMP_C[i], crouch), JUMP_A[i], air))
    drag = (0.3 * math.cos(math.pi * (t - 0.2) / 0.53) if 0.2 < t < 0.73 else 0.0) * air   # + rising: the heavy bell end lags
    K = {'ru': [(0, ra(0))] * 2, 'rf': [(0, ra(1))] * 2, 'rs': [(0, tuple(Vector(ra(2)) + Vector((0, drag, 0))))] * 2,
         'lu': [(0, tuple(slerpv(STANCE['lu'], (0.75, -0.25, 0.1), air)))] * 2, 'lf': [(0, tuple(slerpv(STANCE['lf'], (0.45, -0.45, 0.75), air)))] * 2,
         'flow': [(0, -20 * air + 15 * crouch)] * 2, 'flare': [(0, 10 * air)] * 2, 'swing': [(0, -15 * air + 8 * crouch)] * 2}
    s = base_pose(0, K)
    s.update({'hips': {'loc': (0, 0, 0), 'rot': Rzyx(x=pitch)},
              'spine': {'rot': Rzyx(x=2 + 14 * crouch - 6 * air)}, 'head': {'rot': Rzyx(x=-18 * crouch + 4 * air)}})
    s.update(leg(-1, -4 - 60 * crouch - 45 * air + 6 * ext, 10 + 80 * crouch + 60 * air - 10 * ext, foot=25 * ext + 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    s.update(leg(1, -2 - 50 * crouch - 5 * air + 2 * ext, 8 + 76 * crouch + 20 * air - 8 * ext, foot=25 * ext + 10 * air,
                 hip_pitch=pitch, flat=max(0.0, 1 - air - ext), spread=2))
    land = key(t, [(0, 0), (0.72, 0), (0.77, 1), (0.88, 0), (1, 0)])
    s['_ground'] = h - 0.03 * land
    return s

# ---------------- mocap run (Mixamo-skeleton clip, e.g. three.js Soldier.glb): legs by bone direction, torso by world delta ----------------
def load_mocap(path, clip='Run'):
    objs0, acts0, meshes0 = set(bpy.data.objects), set(bpy.data.actions), set(bpy.data.meshes)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in objs0]
    src = [o for o in new if o.type == 'ARMATURE'][0]
    act = [a for a in bpy.data.actions if a not in acts0 and a.name.startswith(clip)][0]
    src.animation_data_create()
    for tr in src.animation_data.nla_tracks: tr.mute = True
    src.animation_data.action = act
    if act.slots: src.animation_data.action_slot = act.slots[0]
    names = {'hips': 'Hips', 'spine': 'Spine', 'spine2': 'Spine2', 'neck': 'Neck', 'head': 'Head'}
    for n, m in (('L', 'Left'), ('R', 'Right')):
        names.update({f'thigh.{n}': f'{m}UpLeg', f'shin.{n}': f'{m}Leg', f'foot.{n}': f'{m}Foot', f'toe.{n}': f'{m}ToeBase'})
    flip = Matrix.Rotation(math.pi, 3, 'Z')   # the clip faces +Y in Blender; ours faces -Y
    def grab():
        bpy.context.view_layer.update(); out = {}
        for k, n in names.items():
            mw = src.matrix_world @ src.pose.bones['mixamorig:' + n].matrix
            out[k] = (flip @ mw.to_3x3().normalized(), flip @ mw.translation)
        return out
    src.data.pose_position = 'REST'; rest = grab(); src.data.pose_position = 'POSE'
    f0, f1 = act.frame_range; n = round((f1 - f0) / sc.render.fps * 30)
    samples = []
    for i in range(n):
        fr = f0 + (f1 - f0) * i / n; sc.frame_set(int(fr), subframe=fr - int(fr)); samples.append(grab())
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    for a in [a for a in bpy.data.actions if a not in acts0]: bpy.data.actions.remove(a)
    for me in [me for me in bpy.data.meshes if me not in meshes0]: bpy.data.meshes.remove(me)
    ph = [(s['foot.R'][1].y - s['foot.L'][1].y) for s in samples]; k = max(abs(v) for v in ph) or 1
    print('MOCAP', clip, n, 'frames')
    return rest, samples, [v / k for v in ph]
def mocap_pose(rest, smp, stride=1.0, lean=0.0):
    D = lambda k: smp[k][0] @ rest[k][0].inverted()
    dh = D('hips'); ds = D('spine'); dc = D('spine2'); dn = D('neck'); dhd = D('head')
    dh = Matrix.Identity(3).to_quaternion().slerp(dh.to_quaternion(), 0.5).to_matrix()   # calm the pelvis twist
    sway = (smp['hips'][1].x - rest['hips'][1].x) * 0.7
    pose = {'hips': {'rot': dh, 'loc': (sway, 0, 0)}, 'spine': {'rot': R(x=lean) @ dh.inverted() @ ds},
            'chest': {'rot': ds.inverted() @ dc}, 'neck': {'rot': dc.inverted() @ dn}, 'head': {'rot': R(x=-lean * 0.6) @ dn.inverted() @ dhd}}
    def scale_pitch(d):
        a = math.atan2(-d.y, -d.z) * stride; r = math.hypot(d.y, d.z)
        return Vector((d.x * 0.6, -math.sin(a) * r, -math.cos(a) * r)).normalized()   # narrower track: her legs sit close together
    sole = []
    for side, n in ((1, 'L'), (-1, 'R')):
        hip, knee, ank, toe = (smp[f'{b}.{n}'][1] for b in ('thigh', 'shin', 'foot', 'toe'))
        fd = toe - ank; fd.x = 0
        dirs = [scale_pitch((knee - hip).normalized()), scale_pitch((ank - knee).normalized()), fd.normalized()]
        parent = dh
        for b, d in zip(('thigh', 'shin', 'foot'), dirs):
            name = f'{b}.{n}'; h, t = B[name][0], B[name][1]
            cur = parent @ (Vector(t) - Vector(h)).normalized()
            Db = cur.rotation_difference(d).to_matrix() @ parent
            pose[name] = {'rot': parent.inverted() @ Db}; parent = Db
        sole.append(min(toe.z - rest[f'toe.{n}'][1].z, ank.z - rest[f'foot.{n}'][1].z))
    pose['_ground'] = max(0.0, min(sole)) * 0.9
    return pose
MOCAP = load_mocap(args[2]) if len(args) > 2 and os.path.exists(args[2]) else None
# right upper arm, forearm (relaxed), staff: carried level, straight ahead, bell end forward; clears the coat and legs
# by 5 cm on every frame of the mocap stride (searched against the mesh-level overlap / clearance check)
RUN_R = {False: ((-1.0, -0.489, -0.747), (-0.733, -0.893, -0.433), (0.332, -0.913, 0.132)),
         True: ((-0.961, -0.258, -0.655), (-0.756, -0.907, -0.386), (0.553, -0.967, 0.166))}
def run_arms(s, sprinting, Dc):
    # Staff carried level at her right side, bell end forward, steady (a few degrees of bob).
    # Free left arm: a runner's swing in the sagittal plane, opposite the legs (s < 0 = right foot forward = left arm
    # forward), elbow bent ~80 deg and folding more on the forward swing. Directions are in the hips' frame: Dc (the
    # chest's posed rotation relative to the hips) is mostly cancelled so the clip's chest twist doesn't wave the staff.
    C = Matrix.Identity(3).to_quaternion().slerp(Dc.to_quaternion(), 0.85).to_matrix().inverted()
    w = lambda v: tuple(C @ Vector(v).normalized())
    ru, rf, sd = RUN_R[sprinting]
    # the staff rocks with the stride (counter to the right leg) and bobs on each footfall
    sw, bob = (0.16, 0.06) if sprinting else (0.12, 0.05)
    out, _, _ = arm_R(w((ru[0], ru[1] + 0.08 * s, ru[2])), w((rf[0], rf[1] + sw * 0.6 * s, rf[2] + bob * abs(s))), w((sd[0], sd[1] + sw * s, sd[2])))
    amp = 38 if sprinting else 30
    ph = math.radians(-amp * s)                                   # upper arm pitch, + = forward
    bend = math.radians((85 if sprinting else 75) + 15 * max(0.0, -s))
    lu = (0.14, -math.sin(ph), -math.cos(ph))
    lf = (0.14, -math.sin(ph + bend), -math.cos(ph + bend))
    al, _, _ = arm_dirs('L', w(lu), w(lf))
    out.update(al); out.update(fist('R', 1.0)); out.update(fist('L', 0.45)); out['staff'] = staff_pose(); return out
if MOCAP:
    MREST, MSMP, MPH = MOCAP; MN = len(MSMP)
    def loco(t, sprinting):
        i = int(round(t * MN)) % MN; p = 2 * math.pi * t
        pose = mocap_pose(MREST, MSMP[i], stride=1.3 if sprinting else 0.9, lean=24 if sprinting else 8)
        set_pose(dict(pose)); Dc = delta('hips').inverted() @ delta('chest')   # (a copy: secondary() fills in cloth for the arms-less pose)
        pose.update(run_arms(MPH[i], sprinting, Dc))
        pose.update({'_flow': (32 if sprinting else 20) + 6 * math.sin(2 * p), '_swing': (30 if sprinting else 18) + 8 * math.sin(2 * p + 1.2)})
        return pose
    run = lambda f, t: loco(t, False); sprint = lambda f, t: loco(t, True)
else:
    raise SystemExit('mocap run clip required (pass the Soldier.glb path): hand-keyed runs were rejected')
RUN_FRAMES, SPRINT_FRAMES = MN, round(MN / 1.3)
if os.environ.get('CHIME_EXPLORE'):     # authoring: run a pose experiment with the rig set up, then stop
    exec(open(os.environ['CHIME_EXPLORE']).read()); raise SystemExit(0)
acts = [bake('Idle', 60, idle, True), bake('Run', RUN_FRAMES, run, True), bake('Sprint', SPRINT_FRAMES, sprint, True),
        bake('Dodge', 14, dodge, False), bake('Jump', 36, jump, False)]
acts += [bake(name, frames, K if callable(K) else kit_pose(K), False) for name, (frames, K) in KIT.items()]
# ---------------- contact pass: the follow-through never swings a loose part into the staff or its banners ----------
# Every frame is checked mesh against mesh (chimesinger_qa.Checker: overlap, and a 12 mm gap). Where a swing touches,
# the banners and the touching chain are eased back toward their targets (which clear) only as far as needed.
from chimesinger_qa import Checker
_Q = Checker(arm, body, staff); CHAIN = {}
for n in 'LR':
    for b in ('skirtF', 'skirtB'): CHAIN[f'{b}.{n}'] = CHAIN[f'{b}2.{n}'] = [f'{b}.{n}', f'{b}2.{n}']
    CHAIN[f'sleeve.{n}'] = CHAIN[f'tassel.{n}'] = [f'sleeve.{n}', f'tassel.{n}', f'tassel2.{n}']     # (a strand hangs from its sleeve)
def _contact(act):
    arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]
    base, lg = BASE_BY_CLIP[act.name], LG_BY_CLIP[act.name]; f0, f1 = int(act.frame_range[0]), int(act.frame_range[1]); eased = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f); lon = lg[f - f0] > 0
        def hit():
            n_, regs, _ = _Q.overlap(lon); c_, cr = _Q.clearance(lon)
            return (n_ > 0 or c_ < 0.012), set(regs) | {cr}
        bad, regs = hit()
        if not bad: continue
        bones = {b for b, _ in BANNERS} | {c for r in regs for c in CHAIN.get(r, [])}
        sim = {b: pbs[b].rotation_quaternion.copy() for b in bones}; ws = (0.3, 0.55, 0.8, 1.0); i = 0
        while i < len(ws):
            w = ws[i]
            for b in bones:
                pbs[b].rotation_quaternion = sim[b].slerp(base[f - f0][b], w); pbs[b].keyframe_insert('rotation_quaternion', frame=f, group=b)
            sc.frame_set(f); bad, regs = hit()   # (re-evaluate the frame from the keys just written)
            if not bad: break
            new = {c for r in regs for c in CHAIN.get(r, [])} - bones      # easing one part can uncover the next
            if new: sim.update({b: pbs[b].rotation_quaternion.copy() for b in new}); bones |= new
            else: i += 1
        eased.append((f, w))
    if eased: print('CONTACT', act.name, 'eased', eased)
for a in acts: _contact(a)
# ---------------- QA hand-off: chimesinger_qa.py (run on the saved .blend) reads which frames to check the grip on and
# when the left hand holds the staff (its vertices are then allowed to touch the shaft) ----------------
def _ev_frame(name, key_):
    a = bpy.data.actions[name]; return int(round(CLIP_EVENTS[name][key_] * a.frame_range[1])) + 1   # baked frame f+1 is t = f / frames
arm['qa_lg'] = json.dumps(LG_BY_CLIP)
arm['qa_grip_left'] = json.dumps(list(ad.bones['hand.L'].matrix_local.inverted() @ _grip_point('L')))
arm['qa_grip_frames'] = json.dumps({'Idle': [1, 31], 'Run': [1, 8], 'ResonantNote': [_ev_frame('ResonantNote', 'release')],
                                    'Bellstrike': [_ev_frame('Bellstrike', 'contact')], 'Crescendo.L': [_ev_frame('Crescendo', 'detonate')]})
arm.animation_data.action = acts[0]
set_pose({})
with open(os.path.join(work, 'chimesinger_clips.json'), 'w') as fh:
    json.dump({a.name: {'frames': int(a.frame_range[1] - a.frame_range[0]), 'events': CLIP_EVENTS.get(a.name, {})} for a in acts}, fh, indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(work, 'chimesinger_rig.blend'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_image_format='WEBP', export_image_quality=90)
print('EXPORTED', out_glb, os.path.getsize(out_glb))
if len(args) > 3 and args[3]:
    bpy.ops.export_scene.gltf(filepath=args[3], export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                              export_force_sampling=True, export_image_format='JPEG', export_image_quality=92)
    print('EXPORTED', args[3], os.path.getsize(args[3]))
