# python arcanist_robefit.py <in Arcanist.glb> <out .glb> [thigh radius m] [shin radius m]
# python arcanist_robefit.py <in Arcanist.glb> <out .glb> --gather [L1 L2 L3 keep]
# Re-fits the Arcanist's robe to his legs in all 23 clips, at the glTF level (robe bones' rotation keys only).
#
# Why: the robe cloth (16 chains x 3 bones, arcanist_rig.py / arcanist_addclips.py RobeSim) keeps its joints out of
# capsules around the legs, 19 cm around the thighs and 14 cm around the shins. The trousers are only ~12-14 cm from the
# thigh bone, and the sim also pushes each segment's midpoint clear (which doubles the push), so in Idle every chain
# hangs 22-27 cm from the nearest leg, 9-14 cm outside the trousers: the robe stands out like a hoop.
#
# How: for every clip this replays RobeSim twice on the clip's own body motion (read from the GLB: hips and legs),
# once with the original capsules and once with leg-sized ones (blending back to the originals as a knee lifts
# past 20-50 deg, so the robe still tents over raised knees), with the same pre-roll, loop drift removal and 1-2-1
# smoothing as the original bake. The difference between the two runs (the extra push alone) is taken off the
# baked robe joints, segment lengths are restored, and the robe bones get the minimal swing that points them at the new
# joints. Everything else in the clip (its swing, gusts, flare and lift, which this sim doesn't know) stays as baked.
# Only the robe rotation outputs change; the script verifies every other byte of the file is identical.
#
# --gather: the robe's side panels are modelled flared (A-line): at rest the side chains hang 35-47 cm from his centre,
# 15-20 cm clear of the trousers, so they stand out from the legs in every clip. Gathering pulls every robe joint that is
# further than L1 / L2 / L3 (joints 1-3: hip, thigh and hem height; default 18 / 19 / 20 cm) from the nearest leg axis
# toward the hips axis, removing (1 - keep) of the excess (default keep 0.3), so the panels keep their swing but in a
# narrower band. Same per-frame write path, smoothing and byte-for-byte check as the fit.
import json, math, struct, sys, numpy as np

src, dst = sys.argv[1], sys.argv[2]
GATHER = '--gather' in sys.argv
_num = [float(a) for a in sys.argv[3:] if a != '--gather']
if GATHER:
    LIMIT = np.array(_num[:3]) if len(_num) >= 3 else np.array([0.18, 0.19, 0.20]); KEEP = _num[3] if len(_num) > 3 else 0.3
else:
    R_THIGH = _num[0] if len(_num) > 0 else 0.145
    R_SHIN = _num[1] if len(_num) > 1 else 0.13
OLD = (0.19, 0.14)
CAP = 0.10                                               # the most any robe joint is moved (m)
LOOPS = {'Idle', 'Run', 'Sprint', 'DisintegrateHold'}
VEL = {'Run': (0, -4.2, 0), 'Sprint': (0, -6.5, 0)}       # arcanist_rig.py: in-place locomotion, the air he runs into
NCH = 16; CH_STEP = 360 / NCH
CH_A = [((CH_STEP / 2 + CH_STEP * i + 180) % 360) - 180 for i in range(NCH)]
HEM_Z = 0.34

def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    J = json.loads(d[20:20 + jl]); bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, bytearray(d[28 + jl:28 + jl + bl])
def write_glb(p, J, B):
    js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); B = bytes(B) + b'\0' * (-len(B) % 4)
    open(p, 'wb').write(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(B)) + struct.pack('<II', len(js), 0x4E4F534A)
                        + js + struct.pack('<II', len(B), 0x004E4942) + B)
J, B = read_glb(src)
KEY = 'robeGather' if GATHER else 'robeFit'
assert KEY not in J.get('extras', {}), f'this GLB already has {KEY}'
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
def acc(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]; assert a['componentType'] == 5126
    n = NC[a['type']]; assert v.get('byteStride', 4 * n) == 4 * n
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return np.frombuffer(B, np.float32, count=a['count'] * n, offset=off).reshape(-1, n).astype(np.float64), off

# ---------------- quaternions (x, y, z, w) and node transforms ----------------
def q2m(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
def m2q(m):
    t = np.trace(m)
    if t > 0:
        s = math.sqrt(t + 1) * 2; return np.array([(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, s / 4])
    i = int(np.argmax(np.diag(m))); j, k = (i + 1) % 3, (i + 2) % 3
    s = math.sqrt(1 + m[i, i] - m[j, j] - m[k, k]) * 2; q = np.zeros(4)
    q[i] = s / 4; q[j] = (m[j, i] + m[i, j]) / s; q[k] = (m[k, i] + m[i, k]) / s; q[3] = (m[k, j] - m[j, k]) / s
    return q
def swing(a, b):   # smallest rotation taking direction a to direction b
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b); v = np.cross(a, b); c = float(a @ b)
    if c < -0.999999: raise ValueError('opposite directions')
    vx = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + vx + vx @ vx / (1 + c)
NODES = J['nodes']; NAME = {n['name']: i for i, n in enumerate(NODES)}
PARENT = {c: i for i, n in enumerate(NODES) for c in n.get('children', [])}
def rest_trs(i):
    n = NODES[i]; return (np.array(n.get('translation', [0, 0, 0]), float), np.array(n.get('rotation', [0, 0, 0, 1]), float),
                          np.array(n.get('scale', [1, 1, 1]), float))
def mat(t, r, s):
    M = np.eye(4); M[:3, :3] = q2m(r) * s; M[:3, 3] = t; return M
CHAIN = [[NAME[f'robe{i:02d}_{k}'] for k in range(3)] for i in range(NCH)]
ROBE_NODES = {j for c in CHAIN for j in c}
LEGS = {n: [NAME[f'{b}.{n}'] for b in ('thigh', 'shin', 'foot')] for n in 'LR'}
HIPS = NAME['hips']
# glTF (Y up, +Z = his front) -> the sims' frame (Z up, -Y = his front): (x, y, z) -> (x, -z, y)
C = np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]], float)

def globals_(local):   # local: node -> 4x4 (animated or rest); returns node -> global 4x4 for the nodes we need
    G = {}
    def g(i):
        if i in G: return G[i]
        M = local[i]; G[i] = M if i not in PARENT else g(PARENT[i]) @ M; return G[i]
    for i in list(ROBE_NODES) + [HIPS] + LEGS['L'] + LEGS['R']: g(i)
    return G
REST_LOCAL = {i: mat(*rest_trs(i)) for i in range(len(NODES))}
GR = globals_(REST_LOCAL)
for i in ROBE_NODES | {HIPS}: assert np.allclose(np.linalg.norm(GR[i][:3, :3], axis=0), 1, atol=1e-4), 'scaled bones not supported'
P_ = lambda M: C @ M[:3, 3]             # node position in the sim frame
Y_ = lambda M: C @ M[:3, 1]             # node's +Y (bone) axis in the sim frame
# rest chain joints: heads of robeNN_0..2, and the hem (robeNN_2's axis at hem height), as arcanist_addclips.py builds them
REST = np.zeros((NCH, 4, 3)); HEML = np.zeros(NCH)
for i, c in enumerate(CHAIN):
    for k in range(3): REST[i, k] = P_(GR[c[k]])
    y2 = Y_(GR[c[2]]); HEML[i] = (HEM_Z - REST[i, 2, 2]) / y2[2]; REST[i, 3] = REST[i, 2] + y2 * HEML[i]
    for k in range(2):   # bones point at their child
        d = REST[i, k + 1] - REST[i, k]; assert np.dot(d / np.linalg.norm(d), Y_(GR[c[k]])) > 0.9999, f'robe{i:02d}_{k} axis'
SEG = np.linalg.norm(REST[:, 1:] - REST[:, :-1], axis=2); SPACE = np.linalg.norm(np.roll(REST, -1, 0) - REST, axis=2)

class Sim:   # arcanist_addclips.py RobeSim.step, on arrays (same constants, constraints and capsule rule)
    G = np.array([0, 0, -9.8]); K = np.array([0, 40.0, 12.0, 5.0]); DRAG = 1.5; DAMP = 0.04; SUB = 4; LIM = (75, 45, 40)
    def __init__(s, r_thigh, r_shin): s.rt, s.rs = r_thigh, r_shin; s.P = None
    def step(s, dt, M, legs, vel):
        T = REST @ M[:3, :3].T + M[:3, 3]
        if s.P is None: s.P, s.Q = T.copy(), T.copy()
        air = -np.array(vel, float)
        OUT = np.array([(math.sin(math.radians(a_)), -math.cos(math.radians(a_)), 0) for a_ in CH_A]) @ M[:3, :3].T
        caps = []
        for n in 'LR':
            # leg-sized capsules while the thigh hangs near vertical; the original ones as the knee comes up (tucks,
            # kneels, high strides), where the robe has to tent over the knee: blend over 20-50 deg of thigh lift
            th = legs[n][1] - legs[n][0]; down = -M[:3, 2]
            lift = math.degrees(math.acos(np.clip(th @ down / np.linalg.norm(th), -1, 1)))
            w = min(1.0, max(0.0, (lift - 20) / 30)); w = w * w * (3 - 2 * w)
            caps += [(legs[n][0], legs[n][1], s.rt + (OLD[0] - s.rt) * w), (legs[n][1], legs[n][2], s.rs + (OLD[1] - s.rs) * w)]
        h = dt / s.SUB
        for _ in range(s.SUB):
            v = (s.P - s.Q) / h
            a = s.G + s.K[None, :, None] * (T - s.P) + s.DRAG * (air - v)
            mv = (s.P - s.Q) * (1 - s.DAMP) + a * h * h
            n_ = np.linalg.norm(mv, axis=2, keepdims=True); mv *= np.minimum(1, 0.04 / np.maximum(n_, 1e-9))
            s.P, s.Q = s.P + mv, s.P
            for _ in range(3):
                s.P[:, 0] = T[:, 0]
                for k in range(3):
                    d = s.P[:, k + 1] - s.P[:, k]; d = d / np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-6)
                    ref = (T[:, 1] - T[:, 0]) if k == 0 else (s.P[:, k] - s.P[:, k - 1])
                    ref = ref / np.maximum(np.linalg.norm(ref, axis=1, keepdims=True), 1e-6)
                    cosl = math.cos(math.radians(s.LIM[k])); c = np.sum(d * ref, 1); bad = c < cosl
                    if bad.any():
                        perp = d[bad] - ref[bad] * c[bad, None]; perp /= np.maximum(np.linalg.norm(perp, axis=1, keepdims=True), 1e-6)
                        d[bad] = ref[bad] * cosl + perp * math.sqrt(1 - cosl * cosl)
                    s.P[:, k + 1] = s.P[:, k] + d * SEG[:, k:k + 1]
                d = np.roll(s.P, -1, 0) - s.P; l = np.linalg.norm(d, axis=2, keepdims=True)
                tgt = np.clip(l, SPACE[..., None] * 0.8, SPACE[..., None] * 1.3)
                corr = d * (1 - tgt / np.maximum(l, 1e-6)) * 0.5; corr[:, 0] = 0
                s.P += corr; s.P -= np.roll(corr, 1, 0)
                for a0, b0, rad in caps:
                    ab = b0 - a0; ab2 = ab @ ab
                    side = OUT + np.array([0, 0, 0.6]); side = side - np.outer(side @ ab, ab) / ab2
                    side /= np.maximum(np.linalg.norm(side, axis=1, keepdims=True), 1e-6)
                    for k in range(1, 4):
                        for f_ in (1.0, 0.5):
                            q = s.P[:, k - 1] + (s.P[:, k] - s.P[:, k - 1]) * f_
                            u = np.clip(((q - a0) @ ab) / ab2, 0, 1); c = a0 + u[:, None] * ab; d = q - c
                            l = np.linalg.norm(d, axis=1); hit = l < rad
                            if not hit.any(): continue
                            wrong = (np.sum(d * side, 1) < 0) | (l < rad * 0.5)
                            nrm = np.where(wrong[:, None], side, d / np.maximum(l, 1e-6)[:, None])
                            dist = np.where(wrong, rad + np.maximum(0, -np.sum(d * side, 1)), rad - l)
                            s.P[hit, k] += nrm[hit] * (dist[hit] / f_)[:, None]
        s.P[:, 0] = T[:, 0]
def run_sim(frames, loop, r_thigh, r_shin, vel):
    # arcanist_rig.py bake(): pre-roll (two cycles for loops, 25 steps of the first frame otherwise), the keyed frames,
    # then loop drift removal and two 1-2-1 passes (cyclic for loops). frames: list of (M, legs) per key.
    s = Sim(r_thigh, r_shin); n = len(frames); body = n - 1 if loop else n
    for f in (list(range(body)) * 2 if loop else [0] * 25): s.step(1 / 30, *frames[f], vel)
    Ps = []
    for f in range(n): s.step(1 / 30, *frames[f], vel); Ps.append(s.P.copy())
    Ps = np.array(Ps)
    if loop:
        Ps -= (Ps[-1] - Ps[0])[None] * (np.arange(n) / (n - 1))[:, None, None, None]
        Cc = Ps[:-1]
        for _ in range(2): Cc = (np.roll(Cc, 1, 0) + 2 * Cc + np.roll(Cc, -1, 0)) / 4
        Ps = np.concatenate([Cc, Cc[:1]])
    else:
        for _ in range(2): Ps[1:-1] = (Ps[:-2] + 2 * Ps[1:-1] + Ps[2:]) / 4
    return Ps

# ---------------- per clip ----------------
def sample(times, vals, interp, t, is_rot):
    if len(times) == 1 or t <= times[0]: return vals[0]
    if t >= times[-1]: return vals[-1]
    i = int(np.searchsorted(times, t) - 1); u = (t - times[i]) / (times[i + 1] - times[i])
    if interp == 'STEP' or u < 1e-9: return vals[i]
    a, b = vals[i], vals[i + 1]
    if is_rot:
        if a @ b < 0: b = -b
        q = a * (1 - u) + b * u; return q / np.linalg.norm(q)
    return a * (1 - u) + b * u
B0 = bytes(B); J0 = json.loads(json.dumps(J)); written = []; report = []; converted = {}
for an in J['animations']:
    name = an['name']; ch = {}
    for c in an['channels']:
        smp = an['samplers'][c['sampler']]; times, _ = acc(smp['input']); vals, off = acc(smp['output'])
        ch[(c['target']['node'], c['target']['path'])] = (times[:, 0], vals, smp.get('interpolation', 'LINEAR'), smp, off, c['sampler'])
    # the clip's key times: those of its robe rotation tracks
    key_ch = max((v for (n, p), v in ch.items() if n in ROBE_NODES and p == 'rotation'), key=lambda v: len(v[0]))
    keyt, key_input = key_ch[0], key_ch[3]['input']
    loop = name in LOOPS; nkeys = len(keyt)
    G_base, frames, Pb = [], [], np.zeros((nkeys, NCH, 4, 3))
    for f, t in enumerate(keyt):
        local = {}
        for i in range(len(NODES)):
            tr, r, s_ = rest_trs(i)
            if (i, 'translation') in ch: tr = sample(*ch[(i, 'translation')][:3], t, False)
            if (i, 'rotation') in ch: r = sample(*ch[(i, 'rotation')][:3], t, True)
            if (i, 'scale') in ch: s_ = sample(*ch[(i, 'scale')][:3], t, False)
            local[i] = mat(tr, r, s_)
        G = globals_(local); G_base.append(G)
        Mh = np.eye(4); D = G[HIPS] @ np.linalg.inv(GR[HIPS]); Mh[:3, :3] = C @ D[:3, :3] @ C.T; Mh[:3, 3] = C @ D[:3, 3]
        legs = {n: [P_(G[j]) for j in LEGS[n]] for n in 'LR'}
        frames.append((Mh, legs))
        for i, c in enumerate(CHAIN):
            for k in range(3): Pb[f, i, k] = P_(G[c[k]])
            Pb[f, i, 3] = Pb[f, i, 2] + Y_(G[c[2]]) * HEML[i]
    vel = VEL.get(name, (0, 0, 0))
    if GATHER:
        D = np.zeros_like(Pb); replay = 0.0
        for f in range(nkeys):
            legs = frames[f][1]; segs = [(legs[n][0], legs[n][1]) for n in 'LR'] + [(legs[n][1], legs[n][2]) for n in 'LR']
            q = Pb[f, :, 1:].reshape(-1, 3); dl = np.full(len(q), 9.0)
            for a0, b0 in segs:
                ab = b0 - a0; u = np.clip(((q - a0) @ ab) / (ab @ ab), 0, 1); dl = np.minimum(dl, np.linalg.norm(q - (a0 + u[:, None] * ab), axis=1))
            e = np.maximum(dl.reshape(NCH, 3) - LIMIT[None], 0) * (1 - KEEP)
            r = Pb[f, :, 1:, :2] - P_(G_base[f][HIPS])[:2]; r = r / np.maximum(np.linalg.norm(r, axis=-1, keepdims=True), 1e-6)
            D[f, :, 1:, :2] = -r * e[..., None]
    else:
        So = run_sim(frames, loop, *OLD, vel); D = run_sim(frames, loop, R_THIGH, R_SHIN, vel) - So
    if not GATHER: replay = np.linalg.norm((So - Pb)[:, :, 1:], axis=-1).mean() * 100   # how closely the replay matches the bake
    # keep only what this fix is for: pulling the robe in. Clips with wind or fast motion replay less exactly, and their
    # leftover differences would show as stray outward swings, so the outward (away from the hips axis) part of the
    # difference is dropped, each joint's move is capped at CAP, and the result is smoothed in time like the bake.
    hip = np.array([P_(G[HIPS]) for G in G_base])                      # the posed hips joint per key
    rad = Pb[..., :2] - hip[:, None, None, :2]; rad = np.concatenate([rad, np.zeros(rad.shape[:-1] + (1,))], -1)
    rad /= np.maximum(np.linalg.norm(rad, axis=-1, keepdims=True), 1e-6)
    if not GATHER:
        out = np.sum(D * rad, -1, keepdims=True); D = D - np.maximum(out, 0) * rad
        n_ = np.linalg.norm(D, axis=-1, keepdims=True); D *= np.minimum(1, CAP / np.maximum(n_, 1e-9))
    for _ in range(2):
        if loop: Dc = D[:-1]; Dc = (np.roll(Dc, 1, 0) + 2 * Dc + np.roll(Dc, -1, 0)) / 4; D = np.concatenate([Dc, Dc[:1]])
        else: D[1:-1] = (D[:-2] + 2 * D[1:-1] + D[2:]) / 4
    Pn = Pb + D; Pn[:, :, 0] = Pb[:, :, 0]
    for k in range(3):   # restore the bone lengths, joint by joint down each chain
        d = Pn[:, :, k + 1] - Pn[:, :, k]; Pn[:, :, k + 1] = Pn[:, :, k] + d / np.linalg.norm(d, axis=-1, keepdims=True) * SEG[None, :, k, None]
    # new robe rotations: each bone swings from its baked direction to the new one (glTF frame), parents first
    newq = {j: np.zeros((nkeys, 4)) for j in ROBE_NODES}
    for f in range(nkeys):
        G = G_base[f]
        for i, c in enumerate(CHAIN):
            parent_rot = G[PARENT[c[0]]][:3, :3]
            for k in range(3):
                Rg = C.T @ swing(Pb[f, i, k + 1] - Pb[f, i, k], Pn[f, i, k + 1] - Pn[f, i, k]) @ C @ G[c[k]][:3, :3]
                q = m2q(parent_rot.T @ Rg)
                prev = newq[c[k]][f - 1] if f else ch[(c[k], 'rotation')][1][0]
                newq[c[k]][f] = -q if q @ prev < 0 else q
                parent_rot = Rg
    moved = np.linalg.norm((Pn - Pb)[:, :, 1:], axis=-1)
    fi, ci, ki = np.unravel_index(np.argmax(moved), moved.shape)
    report.append((name, nkeys, loop, replay, moved.mean() * 100, moved.max() * 100, fi, ci, ki + 1))
    for j, q in newq.items():
        times, vals, interp, smp, off, si = ch[(j, 'rotation')]
        if len(times) != nkeys:
            # a constant (2-key STEP) track: keep it when the new values stay constant; otherwise it becomes a per-key
            # LINEAR track on the clip's own key times, its keys appended at the end of the buffer
            if np.allclose(np.abs(q @ q[0]), 1, atol=1e-6): continue
            converted[(J['animations'].index(an), si)] = dict(smp)
            data = q.astype(np.float32).tobytes(); B += b'\0' * (-len(B) % 4); bo = len(B); B += data
            J['bufferViews'].append({'buffer': 0, 'byteOffset': bo, 'byteLength': len(data)})
            J['accessors'].append({'bufferView': len(J['bufferViews']) - 1, 'componentType': 5126, 'count': nkeys, 'type': 'VEC4'})
            smp['input'] = key_input; smp['output'] = len(J['accessors']) - 1; smp['interpolation'] = 'LINEAR'
            continue
        B[off:off + q.size * 4] = q.astype(np.float32).tobytes(); written.append((off, off + q.size * 4))
for r in report: print('CLIP %-18s keys %3d loop %-5s replay vs bake %.1f cm | robe joints moved: mean %.1f cm, max %.1f cm (frame %d, robe%02d joint %d)' % r)
J.setdefault('extras', {})[KEY] = ({'limits': LIMIT.tolist(), 'keep': KEEP} if GATHER else {'thigh': R_THIGH, 'shin': R_SHIN, 'from': list(OLD)}) | {'script': 'arcanist_robefit.py'}
J['buffers'][0]['byteLength'] = len(B) + (-len(B) % 4)
write_glb(dst, J, B)
# check: only the robe rotation outputs changed
J1, B1 = read_glb(dst); assert len(B1) >= len(B0)
diff = np.nonzero(np.frombuffer(B0, np.uint8) != np.frombuffer(bytes(B1[:len(B0)]), np.uint8))[0]
ok = np.zeros(len(diff), bool)
for a, b in written: ok |= (diff >= a) & (diff < b)
assert ok.all(), 'bytes outside the robe rotation keys changed'
# the JSON matches the input once the converted samplers, the appended views / accessors and the buffer length are undone
for (ai, si), orig in converted.items(): J1['animations'][ai]['samplers'][si] = orig
J1['bufferViews'] = J1['bufferViews'][:len(J0['bufferViews'])]; J1['accessors'] = J1['accessors'][:len(J0['accessors'])]
J1['buffers'][0]['byteLength'] = J0['buffers'][0]['byteLength']
assert {k: v for k, v in J1.items() if k != 'extras'} == {k: v for k, v in J0.items() if k != 'extras'}, 'glTF JSON changed elsewhere'
print('WROTE', dst, f'- {len(written)} robe rotation tracks rewritten in place, {len(converted)} constant tracks made per-key, {len(diff)} bytes changed, nothing else touched')
