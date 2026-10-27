# Hands for chimesinger_rig.py: measure each hand on the mesh, split it into four fingers and a thumb, weight them, and
# solve a power grip around the real staff shaft numerically.
# Hand frame (measured by PCA, as the rig always did): W wrist, fd finger direction, nrm, fw across the palm toward
# the thumb. Her palm is the -nrm face: the fingers droop that way and the thumb sits on it.
# All positions here are rest-pose armature coordinates (metres).
import math, numpy as np

KNUCKLE = 0.075                       # knuckle line of the longest finger, metres from the wrist along fd
FINGERS = ('index', 'middle', 'ring', 'pinky')
PIP_F, DIP_F = 0.373, 0.667           # middle / end joints as fractions knuckle -> tip (0.103 / 0.125 on a 0.15 finger)
THUMB_F = (0.45, 0.75)                # thumb MCP / IP joints as fractions base -> tip


def sm(a):
    a = np.clip(a, 0, 1); return a * a * (3 - 2 * a)


def rot(axis, deg):
    # Rodrigues: rotation matrices for one axis and an array of angles (degrees) -> (n, 3, 3)
    a = np.asarray(axis, float); a = a / np.linalg.norm(a); th = np.radians(np.atleast_1d(deg))
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    s, c = np.sin(th)[:, None, None], np.cos(th)[:, None, None]
    return np.eye(3)[None] + s * K[None] + (1 - c) * (K @ K)[None]


def rotvec(v):
    v = np.asarray(v, float); th = np.linalg.norm(v)
    return np.eye(3) if th < 1e-9 else rot(v / th, math.degrees(th))[0]


def shaft_profile(staff, z, n=24):
    # centre (x, y) and radius of the staff's shaft at height z (staff space), by casting rays at its surface from 6 cm
    # out (inside the banners). The shaft is a long decimated cylinder with almost no vertices along it, so vertex
    # statistics can't measure it.
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    me = staff.data; bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [p.vertices for p in me.polygons])
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n; d = Vector((math.cos(a), math.sin(a), 0))
        hit = bvh.ray_cast(Vector((0, 0, z)) + d * 0.06, -d, 0.06)
        if hit[0] is not None: pts.append(hit[0][:2])
    p = np.array(pts); A = np.c_[2 * p, np.ones(len(p))]; b = (p ** 2).sum(1)
    cx, cy, c = np.linalg.lstsq(A, b, rcond=None)[0]
    return float(cx), float(cy), float(math.sqrt(c + cx * cx + cy * cy))


def _seg_pt(X, a, b):
    d = b - a; t = np.clip(((X - a) @ d) / (d @ d), 0, 1); return np.linalg.norm(X - (a + np.outer(t, d)), axis=1)


def measure(co, s):
    """co: rest vertex positions (n, 3); s: +1 left hand, -1 right hand."""
    Wp = np.array([s * 0.41, -0.05, 1.03]); d = np.array([s * 0.53, -0.05, 0.935]) - Wp; Lh = np.linalg.norm(d); d /= Lh
    rel = co - Wp; t = rel @ d; perp = np.linalg.norm(rel - np.outer(t, d), axis=1)
    h = co[(t > -0.01) & (t < Lh + 0.03) & (perp < 0.07) & (co[:, 2] > 0.86)]
    _, _, vt = np.linalg.svd(h - h.mean(0), full_matrices=False)
    fd = vt[0] * np.sign(vt[0] @ d); nrm = vt[2] if vt[2][2] > 0 else -vt[2]
    fw = np.cross(nrm, fd); fw = fw if fw[1] < 0 else -fw
    T, Wv, N = rel @ fd, rel @ fw, rel @ nrm
    hand = (T > -0.015) & (T < 0.2) & (np.abs(Wv) < 0.07) & (np.abs(N) < 0.045)
    P = lambda t_, w_, n_: Wp + fd * t_ + fw * w_ + nrm * n_
    # ---- fingers: cluster the vertices past the web (separate geometry) along fw, then refine on 2D centre lines ----
    F = np.nonzero(hand & (T > 0.095) & (Wv < 0.026))[0]
    c = np.percentile(Wv[F], [88, 63, 37, 12])                   # index (toward the thumb) .. pinky
    for _ in range(30):
        lab = np.argmin(np.abs(Wv[F][:, None] - c[None]), 1); c = np.array([Wv[F][lab == k].mean() for k in range(4)])
    for _ in range(5):
        fits = []
        for k in range(4):
            i = F[lab == k]; A = np.c_[np.ones(len(i)), T[i]]
            fits.append((np.linalg.lstsq(A, Wv[i], rcond=None)[0], np.linalg.lstsq(A, N[i], rcond=None)[0]))
        dd = np.stack([np.hypot(Wv[F] - (cw[0] + cw[1] * T[F]), 0.5 * (N[F] - (cn[0] + cn[1] * T[F]))) for cw, cn in fits], 1)
        lab = np.argmin(dd, 1)
    tips = [np.percentile(T[F[lab == k]], 99.5) for k in range(4)]
    fingers = []
    for k, name in enumerate(FINGERS):
        i = F[lab == k]; cw, cn = fits[k]; tip = tips[k]
        K = KNUCKLE - 0.3 * (max(tips) - tip)
        Tj = [K, K + PIP_F * (tip - K), K + DIP_F * (tip - K)]
        def slab(t0, idx, wdt=0.004):
            m = idx[np.abs(T[idx] - t0) < wdt]
            return (Wv[m].mean(), N[m].mean()) if len(m) > 3 else (cw[0] + cw[1] * t0, cn[0] + cn[1] * t0)
        pal = np.nonzero(hand & (np.abs(Wv - (cw[0] + cw[1] * K)) < 0.007))[0]
        joints = [P(K, *slab(K, pal)), P(Tj[1], *slab(Tj[1], i)), P(Tj[2], *slab(Tj[2], i))]
        e = i[T[i] > tip - 0.004]; joints.append(P(T[e].mean(), Wv[e].mean(), N[e].mean()))
        hw = [np.ptp(Wv[m]) / 2 for t0 in np.arange(Tj[1], tip - 0.008, 0.004) for m in [i[np.abs(T[i] - t0) < 0.002]] if len(m) > 4]
        rf = float(np.clip(np.median(hw) if hw else 0.006, 0.005, 0.008))
        fingers.append({'name': name, 'J': joints, 'T': Tj + [tip], 'rf': rf, 'cw': cw, 'cn': cn, 'verts': i})
    # ---- thumb: everything well across the palm toward fw; axis by PCA, base at the wrist end of the thenar ----
    thr = 0.022 + np.clip((T - 0.06) * 0.5, 0, 0.012)
    th = sm((Wv - thr) / 0.005 + 0.5) * hand * (T > -0.012)
    TH = np.nonzero(hand & (Wv > 0.03) & (T > 0.02))[0]
    X = co[TH]; _, _, vt = np.linalg.svd(X - X.mean(0), full_matrices=False); tdir = vt[0] * np.sign(vt[0] @ fd)
    pr = (X - X.mean(0)) @ tdir; tipP = X[pr > np.percentile(pr, 99.5) - 0.004].mean(0)
    b = np.nonzero(hand & (Wv > 0.021) & (T > -0.002) & (T < 0.016))[0]; J0 = co[b].mean(0)
    L = np.linalg.norm(tipP - J0); ax = (tipP - J0) / L
    def tslab(f):
        q = J0 + ax * L * f; m = TH[np.abs((co[TH] - q) @ ax) < 0.004]
        if len(m) < 4: return q
        r = co[m] - q; return q + (r - np.outer(r @ ax, ax)).mean(0)
    tj = [J0, tslab(THUMB_F[0]), tslab(THUMB_F[1]), tipP]
    m = TH[((co[TH] - J0) @ ax > L * 0.5)]          # distal half: distance to the thumb's centre line
    rad = np.minimum(_seg_pt(co[m], tj[1], tj[2]), _seg_pt(co[m], tj[2], tj[3]))
    rt = float(np.clip(np.median(rad), 0.006, 0.009))
    return {'W': Wp, 'fd': fd, 'nrm': nrm, 'fw': fw, 'palm': -nrm, 'T': T, 'Wv': Wv, 'N': N, 'hand': hand,
            'fingers': fingers, 'thumb': {'J': tj, 'rt': rt, 'th': th}}


def weights(H):
    """Share of each hand bone in the hand's weight, per vertex: {'hand': w, 'index1': w, ..., 'thumb3': w}."""
    T, Wv, N, hand = H['T'], H['Wv'], H['N'], H['hand']; th = H['thumb']['th']
    out = {}
    # thumb: along its base -> tip line, joints blended over ~1 cm
    tj = H['thumb']['J']; L = np.linalg.norm(tj[3] - tj[0]); ax = (tj[3] - tj[0]) / L
    co = H['W'] + np.outer(T, H['fd']) + np.outer(Wv, H['fw']) + np.outer(N, H['nrm'])
    s_ = (co - tj[0]) @ ax; s1, s2 = (tj[1] - tj[0]) @ ax, (tj[2] - tj[0]) @ ax
    c1 = sm((s_ + 0.004) / 0.016); c2 = sm((s_ - (s1 - 0.006)) / 0.012); c3 = sm((s_ - (s2 - 0.005)) / 0.010)
    out['thumb1'], out['thumb2'], out['thumb3'] = th * c1 * (1 - c2), th * c1 * c2 * (1 - c3), th * c1 * c2 * c3
    # fingers: soft nearest-centre-line membership (sharp past the web, ~3 mm blends in the palm), then the chain
    sig = np.interp(T, [0.085, 0.1], [0.0035, 0.0008])
    d2 = np.stack([((Wv - (f['cw'][0] + f['cw'][1] * T)) ** 2 + (0.5 * (N - (f['cn'][0] + f['cn'][1] * T))) ** 2) for f in H['fingers']], 1)
    m = np.exp(-(d2 - d2.min(1, keepdims=True)) / (2 * sig[:, None] ** 2)); m /= m.sum(1, keepdims=True)
    for k, f in enumerate(H['fingers']):
        K, t1, t2, _ = f['T']
        a1 = sm((T - (K - 0.010)) / 0.020); a2 = sm((T - (t1 - 0.005)) / 0.010); a3 = sm((T - (t2 - 0.004)) / 0.008)
        g = (1 - th) * m[:, k] * hand
        out[f['name'] + '1'], out[f['name'] + '2'], out[f['name'] + '3'] = g * a1 * (1 - a2), g * a1 * a2 * (1 - a3), g * a1 * a2 * a3
    out['hand'] = np.clip(1 - sum(out.values()), 0, 1)
    return out


# ---------------- grip solve ----------------
def _seg_line(P, Q, C, u):
    # distance from segments P->Q (..., 3) to the infinite line C + u t
    p = P - C; q = Q - C; p = p - (p @ u)[..., None] * u; q = q - (q @ u)[..., None] * u
    d = q - p; t = np.clip(-(p * d).sum(-1) / np.maximum((d * d).sum(-1), 1e-12), 0, 1)
    return np.linalg.norm(p + d * t[..., None], axis=-1)


def _line_d(X, C, u):
    r = X - C; return np.linalg.norm(r - np.outer(r @ u, u), axis=1)


def _chain(J, axes, Rs0=None):
    # J: joints + tip (4, 3); axes: three rotation matrices (local, rest axes). -> per-segment (R, t), posed joints
    R, t = np.eye(3), np.zeros(3); out = []; Pj = [J[0]]
    if Rs0 is not None: R, t = Rs0
    for k in range(3):
        Rk = axes[k]; t = R @ (J[k] - Rk @ J[k]) + t; R = R @ Rk; out.append((R, t))
    for k in range(1, 4): Pj.append(out[k - 1][0] @ J[k] + out[k - 1][1])
    return out, Pj


def finger_axis(H, f):
    J = f['J']; fdir = J[3] - J[0]; fdir /= np.linalg.norm(fdir)
    pa = H['palm'] - fdir * (H['palm'] @ fdir); pa /= np.linalg.norm(pa)
    return np.cross(fdir, pa)          # +angle curls the finger toward the palm


def skin(V, Wb, xf):
    # V (n,3) rest, Wb {bone: (n,) weight}, xf {bone: (R, t)} -> posed (bones missing from xf stay put)
    out = V * Wb['hand'][:, None]
    for b, w in Wb.items():
        if b == 'hand': continue
        R, t = xf.get(b, (np.eye(3), np.zeros(3)))
        out = out + w[:, None] * (V @ R.T + t)
    return out


def solve(H, Wb, R_shaft, log=print):
    """Find the shaft line (C, u) seated in the palm under the proximal finger segments and the finger / thumb joint
    angles that wrap it. Wb: weights() output. Returns the solution dict."""
    T, Wv, N, hand = H['T'], H['Wv'], H['N'], H['hand']
    co = H['W'] + np.outer(T, H['fd']) + np.outer(Wv, H['fw']) + np.outer(N, H['nrm'])
    tot = sum(w for b, w in Wb.items() if b != 'hand')
    pal = np.nonzero(hand & (tot < 0.02) & (T > 0.03))[0]              # rigid palm, for seating the shaft
    sets = {}
    for f in H['fingers']:
        sets[f['name']] = np.nonzero(hand & ((Wb[f['name'] + '1'] + Wb[f['name'] + '2'] + Wb[f['name'] + '3']) > 0.01))[0]
    sets['thumb'] = np.nonzero(hand & ((Wb['thumb1'] + Wb['thumb2'] + Wb['thumb3']) > 0.01))[0]
    A = {f['name']: finger_axis(H, f) for f in H['fingers']}
    # tip vertices as chimesinger_qa.py measures them: the digit's last bone, within 1.5 mm of its far end
    hv = np.nonzero(hand)[0]; names = list(Wb); dom = np.array(names)[np.argmax(np.stack([Wb[b][hv] for b in names], 1), 1)]
    TIPV = {}
    for f in [dict(name=g['name'], J=g['J']) for g in H['fingers']] + [dict(name='thumb', J=H['thumb']['J'])]:
        m = dom == f['name'] + '3'; d_ = np.array(f['J'][3]) - np.array(f['J'][2]); d_ /= np.linalg.norm(d_)
        pr = co[hv] @ d_; TIPV[f['name']] = hv[m & (pr > pr[m].max() - 0.0015)]
    TIP_BAND = (0.0035, 0.008)   # tip vertices this far off the shaft surface: closed on it, never inside
    def band(d): return 50 * (max(0, TIP_BAND[0] - d.min()) ** 2 + max(0, d.max() - TIP_BAND[1]) ** 2)
    LO, HI = np.array([-15.0, 0, 0]), np.array([95.0, 110, 85])        # knuckle / middle / end joint flexion limits (deg)
    g1, g2, g3 = np.meshgrid(np.arange(LO[0], HI[0] + 1, 5), np.arange(LO[1], HI[1] + 1, 5), np.arange(LO[2], HI[2] + 1, 5), indexing='ij')
    G = np.stack([g1.ravel(), g2.ravel(), g3.ravel()], 1).astype(float)

    def finger_fit(f, C, u, refine=True):
        J = np.array(f['J']); ax = A[f['name']]; rho = R_shaft + f['rf']
        # coarse: centre lines only (rotations about one axis add up along the chain)
        c1, c2, c3 = rot(ax, G[:, 0]), rot(ax, G[:, 0] + G[:, 1]), rot(ax, G[:, 0] + G[:, 1] + G[:, 2])
        P1 = J[0] + (c1 @ (J[1] - J[0])); P2 = P1 + (c2 @ (J[2] - J[1])); P3 = P2 + (c3 @ (J[3] - J[2]))
        P0 = np.broadcast_to(J[0], P1.shape)
        dseg = np.stack([_seg_line(P0, P1, C, u), _seg_line(P1, P2, C, u), _seg_line(P2, P3, C, u)], 1)
        cost = (((dseg - rho) ** 2).sum(1) + 1e-7 * (G[:, 2] - 0.7 * G[:, 1]) ** 2 + 1e-6 * np.minimum(G[:, 0], 0) ** 2
                + 50 * np.clip(rho - 0.004 - dseg, 0, None).sum(1) ** 2)
        best = G[np.argsort(cost)[:6]]
        if not refine: return best[0], float(np.sort(cost)[0])
        idx = sets[f['name']]; V = co[idx]; Wf = {b: Wb[b][idx] for b in ('hand', f['name'] + '1', f['name'] + '2', f['name'] + '3')}
        ti = TIPV[f['name']]; Vt_ = co[ti]; Wt_ = {b: Wb[b][ti] for b in Wf}
        def mcost(th):
            xf, Pj = _chain(J, [rot(ax, a)[0] for a in th])
            ds = np.array([_seg_line(Pj[k], Pj[k + 1], C, u) for k in range(3)])
            X = skin(V, Wf, {f['name'] + str(k + 1): xf[k] for k in range(3)})
            pen = np.clip(R_shaft + 0.0005 - _line_d(X, C, u), 0, None)
            xd = {f['name'] + str(k + 1): xf[k] for k in range(3)}
            return (((ds - rho) ** 2).sum() + 2e3 * (pen ** 2).sum() + 1e-7 * (th[2] - 0.7 * th[1]) ** 2 + 1e-6 * min(th[0], 0) ** 2
                    + band(_line_d(skin(Vt_, Wt_, xd), C, u) - R_shaft))
        res = []
        for th in best:
            th = th.copy(); c = mcost(th); step = 2.0
            while step > 0.05:
                moved = False
                for j in range(3):
                    for sgn in (1, -1):
                        t2 = np.clip(th + sgn * step * np.eye(3)[j], LO, HI); c2_ = mcost(t2)
                        if c2_ < c: th, c, moved = t2, c2_, True
                if not moved: step /= 2
            res.append((c, th))
        c, th = min(res, key=lambda r: r[0])
        return th, c

    tj = np.array(H['thumb']['J']); rt = H['thumb']['rt']
    ti = sets['thumb']; Vt = co[ti]; Wt = {b: Wb[b][ti] for b in ('hand', 'thumb1', 'thumb2', 'thumb3')}

    TLO, THI = np.array([-75.0, -75, -75, 0, 0]), np.array([75.0, 75, 75, 80, 85])
    def thumb_pose(p, u):
        # p: thumb1 rotation vector (deg, 3) + MCP and IP flex (deg, same direction) about the shaft axis as the thumb sees it
        R1 = rotvec(np.radians(p[:3])); q = R1.T @ u * TSIGN[0]
        return _chain(tj, [R1, rot(q, p[3])[0], rot(q, p[4])[0]])

    TSIGN = [1.0]
    def thumb_fit(C, u, fingers_x):
        rho = R_shaft + rt
        # other posed hand vertices the thumb must not enter: the posed index / middle fingers and the palm
        others = np.vstack([fingers_x[k] for k in ('index', 'middle')] + [co[pal]])
        side = np.cross(u, H['palm']); side /= np.linalg.norm(side)
        def tcost(p):
            xf, Pj = thumb_pose(p, u)
            ds = np.array([_seg_line(Pj[k], Pj[k + 1], C, u) for k in (1, 2)])
            X = skin(Vt, Wt, {'thumb1': xf[0], 'thumb2': xf[1], 'thumb3': xf[2]})
            pen = np.clip(R_shaft + 0.0005 - _line_d(X, C, u), 0, None)
            # clearance against the fingers and palm: nearest distance from a sample of thumb vertices
            Xs = X[::3]; dmin = np.sqrt(((Xs[:, None] - others[None]) ** 2).sum(-1)).min(1)
            hit = np.clip(0.0015 - dmin, 0, None)
            # the thumb wraps around the shaft (its last two segments across it, not along it) and its tip closes over the
            # front of the shaft (the side away from the palm)
            tv = Pj[3] - C; tv = tv - u * (tv @ u); front = tv @ H['palm'] / max(np.linalg.norm(tv), 1e-9)
            Xt = skin(co[TIPV['thumb']], {b: Wb[b][TIPV['thumb']] for b in Wt}, {'thumb1': xf[0], 'thumb2': xf[1], 'thumb3': xf[2]})
            return (((ds - rho) ** 2).sum() + 2e3 * (pen ** 2).sum() + 5e2 * (hit ** 2).sum()
                    + 1e-3 * (1 - front) + 1e-8 * (p[:3] ** 2).sum() + 1e-3 * sum(((Pj[k + 1] - Pj[k]) @ u / np.linalg.norm(Pj[k + 1] - Pj[k])) ** 2 for k in (1, 2)) + band(_line_d(Xt, C, u) - R_shaft))
        rng = np.random.default_rng(7); seeds = []
        for sg in (1.0, -1.0):
            TSIGN[0] = sg
            for _ in range(600):
                p = rng.uniform(TLO, THI)
                xf, Pj = thumb_pose(p, u)
                ds = np.array([_seg_line(Pj[k], Pj[k + 1], C, u) for k in (1, 2)])
                c = ((ds - rho) ** 2).sum() + 50 * np.clip(rho - 0.004 - ds, 0, None).sum() ** 2
                seeds.append((c, sg, p))
        seeds.sort(key=lambda r: r[0])
        cands = []
        for _, sg, p in seeds[:6]:
            TSIGN[0] = sg
            c = tcost(p); step = 4.0
            while step > 0.1:
                moved = False
                for j in range(5):
                    for sgn in (1, -1):
                        p2 = np.clip(p + sgn * step * np.eye(5)[j], TLO, THI); c2_ = tcost(p2)
                        if c2_ < c: p, c, moved = p2, c2_, True
                if not moved: step /= 2
            cands.append((c, sg, p))
        c, sg, p = min(cands, key=lambda r: r[0]); TSIGN[0] = sg
        return p, c

    # ---- outer search: shaft tilt (fw turned toward nrm, the palm's cross slope), diagonal lie across the palm (fw
    # turned toward fd: nearer the fingertips on the index side, like a real grip) and distance from the wrist. The
    # coarse centre-line fit ranks the candidates; the best few get the full mesh-level solve. ----
    fwv, nr, fdv = H['fw'], H['nrm'], H['fd']
    cands = []
    for tilt in np.arange(-10, 41, 5):
        for diag in np.arange(-10, 26, 5):
            u = (fwv * math.cos(math.radians(tilt)) + nr * math.sin(math.radians(tilt))) * math.cos(math.radians(diag)) + fdv * math.sin(math.radians(diag))
            u /= np.linalg.norm(u)
            p = H['palm'] - u * (H['palm'] @ u); p /= np.linalg.norm(p)
            for tc in np.arange(0.045, 0.101, 0.005):
                C0 = H['W'] + fdv * tc + fwv * -0.012
                # seat: slide the shaft from inside the hand toward the palm side until it just touches the rigid palm
                lo_, hi_ = -0.02, 0.08
                for _ in range(30):
                    mid = (lo_ + hi_) / 2
                    if _line_d(co[pal], C0 + p * mid, u).min() < R_shaft + 0.0005: lo_ = mid
                    else: hi_ = mid
                C = C0 + p * hi_
                cost = sum(finger_fit(f, C, u, refine=False)[1] for f in H['fingers'])
                cands.append((cost, tilt, diag, tc, C, u))
    cands.sort(key=lambda r: r[0])
    best = None
    # QA-equivalent measurements of a solved grip on the skinned hand: the deepest hand vertex inside the shaft and,
    # per digit, the distance of its tip vertices (last bone, within 1.5 mm of the far end) to the shaft surface
    hv = np.nonzero(hand)[0]; Wall = {b: w[hv] for b, w in Wb.items()}
    names = list(Wb); dom = np.array(names)[np.argmax(np.stack([Wb[b][hv] for b in names], 1), 1)]
    tipsets = {}
    for f in [dict(name=g['name'], J=g['J']) for g in H['fingers']] + [dict(name='thumb', J=H['thumb']['J'])]:
        m = dom == f['name'] + '3'; d_ = np.array(f['J'][3]) - np.array(f['J'][2]); d_ /= np.linalg.norm(d_)
        pr = co[hv] @ d_; tipsets[f['name']] = m & (pr > pr[m].max() - 0.0015)
    def measure_grip(C, u, angles, tp):
        xf = {}
        for f in H['fingers']:
            ch, _ = _chain(np.array(f['J']), [rot(A[f['name']], a)[0] for a in angles[f['name']]])
            for k in range(3): xf[f['name'] + str(k + 1)] = ch[k]
        ch, _ = thumb_pose(tp, u)
        for k in range(3): xf['thumb' + str(k + 1)] = ch[k]
        X = skin(co[hv], Wall, xf); d = _line_d(X, C, u) - R_shaft
        return float(-min(0, d.min())), {k: (float(d[m].min()), float(d[m].max())) for k, m in tipsets.items()}

    def full(C, u):
        angles, fx, tot = {}, {}, 0.0
        for f in H['fingers']:
            th, c = finger_fit(f, C, u); tot += c
            angles[f['name']] = th
            xf, _ = _chain(np.array(f['J']), [rot(A[f['name']], a)[0] for a in th])
            idx = sets[f['name']]
            fx[f['name']] = skin(co[idx], {b: Wb[b][idx] for b in ('hand', f['name'] + '1', f['name'] + '2', f['name'] + '3')},
                                 {f['name'] + str(k + 1): xf[k] for k in range(3)})
        tp, c = thumb_fit(C, u, fx); tot += c
        return tot, angles, tp, TSIGN[0]

    for _, tilt, diag, tc, C, u in cands[:5]:
        p = H['palm'] - u * (H['palm'] @ u); p /= np.linalg.norm(p)
        for it in range(4):       # skin pressed under the knuckles can still dip in: back the shaft off and re-solve
            tot, angles, tp, sg = full(C, u); pen, tipd = measure_grip(C, u, angles, tp)
            if pen < 1.0e-3: break
            C = C + p * (pen - 0.0005)
        log('GRIP_CAND tilt %d diag %d at %.3f: cost %.2e, deepest %.1f mm, tips %s' % (
            tilt, diag, tc, tot, pen * 1000, {k: (round(a * 1000, 1), round(b * 1000, 1)) for k, (a, b) in tipd.items()}))
        bad = pen * 1000 + sum(max(0, TIP_BAND[0] - a) * 1000 + max(0, b - TIP_BAND[1]) * 1000 for a, b in tipd.values())
        if best is None or (bad, tot) < best[0]: best = ((bad, tot), tilt, diag, tc, C, u, angles, tp, sg)
    _, tilt, diag, tc, C, u, angles, tp, TSIGN[0] = best
    R1 = rotvec(np.radians(tp[:3])); q = R1.T @ u * TSIGN[0]
    log('GRIP_SOLVE tilt %d diag %d deg, shaft %.3f m from the wrist; fingers %s; thumb %s' % (
        tilt, diag, tc, {k: tuple(int(round(a)) for a in v) for k, v in angles.items()}, tuple(int(round(a)) for a in tp)))
    rots = {}
    for f in H['fingers']:
        for k, a in enumerate(angles[f['name']]): rots[f['name'] + str(k + 1)] = (A[f['name']], float(a))
    rots['thumb1'] = (tp[:3] / max(np.linalg.norm(tp[:3]), 1e-9), float(np.linalg.norm(tp[:3])))
    rots['thumb2'] = (q, float(tp[3])); rots['thumb3'] = (q, float(tp[4]))     # q already carries the wrap direction
    return {'C': C, 'u': u, 'tilt': tilt, 'diag': diag, 'rots': rots, 'R': R_shaft}
