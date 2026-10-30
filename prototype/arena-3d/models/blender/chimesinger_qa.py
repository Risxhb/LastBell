# blender -b chimesinger_rig.blend -P chimesinger_qa.py -- <report.json> [clip ...]
# Mesh-level QA for the rigged Chimesinger (run on the .blend that chimesinger_rig.py saves; chimesinger_rig.py also
# imports Checker to test poses while authoring):
#  - staff vs body: BVHTree.overlap of the evaluated staff and body meshes on every frame of every clip. Polygons
#    touching the right hand (and the left hand while it holds the staff) are ignored; the rest are reported by the
#    dominant bone of the overlapping body vertices and by the staff part (shaft / banner / head).
#  - grip: tip vertices of each finger and the thumb vs the shaft surface, and the deepest hand vertex inside the shaft.
#  - rigidity: the staff's transform relative to the right hand bone, frame to frame; the left fist's shaft line to the
#    shaft axis while it holds the staff.
#  - bell tip: world position and speed of the staff's bell end per frame (Bellstrike timing).
import bpy, sys, os, json, math, numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chimesinger_hands as hands

DIGITS = ('fingers', 'thumb', 'index', 'middle', 'ring', 'pinky')
def is_hand(n, side): return n.endswith('.' + side) and (n.startswith('hand.') or n.startswith(DIGITS))
REGION = lambda n: n.split('.')[0].rstrip('0123456789') + ('.' + n.split('.')[1] if '.' in n else '')


class Checker:
    def __init__(self, arm, body, staff):
        self.arm, self.body, self.staff = arm, body, staff; self.pbs = arm.pose.bones
        gname = {g.index: g.name for g in body.vertex_groups}; nv = len(body.data.vertices)
        self.dom = dom = np.empty(nv, object)
        for v in body.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            dom[v.index] = gname[best.group] if best else 'none'
        self.HAND = {s: np.array([is_hand(n, s) for n in dom]) for s in 'LR'}
        self.polys = [tuple(p.vertices) for p in body.data.polygons]
        pv = np.zeros((len(self.polys), 4), np.int64) - 1
        for i, p in enumerate(self.polys): pv[i, :len(p[:4])] = p[:4]
        self.pv = pv
        def poly_hand(side):
            h = self.HAND[side]; m = np.zeros(len(self.polys), bool)
            for k in range(4): m |= (pv[:, k] >= 0) & h[np.maximum(pv[:, k], 0)]
            return m
        self.PH = {s: poly_hand(s) for s in 'LR'}
        self.rest = np.array([v.co[:] for v in body.data.vertices])
        self.TIPS = {s: self._tips(s) for s in 'LR'}
        self.sco = np.array([v.co[:] for v in staff.data.vertices])
        self.cx, self.cy, self.R = hands.shaft_profile(staff, 0.58)
        # the shaft's real centre and radius along its length (it thickens at the blue grip wrap above the right fist)
        self.prof = np.array([(z,) + hands.shaft_profile(staff, z) for z in np.arange(0.40, 0.951, 0.01)])
        self.TOPZ = float(self.sco[:, 2].max())
        self.spolys = [tuple(p.vertices) for p in staff.data.polygons]
        # staff parts by polygon centre (staff space): head = ring + bell (z > 1.0), shaft = within 4 cm of the axis
        c = np.array([self.sco[list(p)].mean(0) for p in self.spolys])
        r = np.hypot(c[:, 0] - self.cx, c[:, 1] - self.cy)
        self.spart = np.where(c[:, 2] > 1.0, 'head', np.where(r < 0.04, 'shaft', 'banner'))
        # the banners swing on their own bones (separate objects): checked with the staff, as 'banner'
        self.extra = [o for o in bpy.data.objects if o.name.startswith('ChimeBanner') and o.type == 'MESH']
        self.eco = [np.array([v.co[:] for v in o.data.vertices]) for o in self.extra]
        off = len(self.sco)
        for o, ec in zip(self.extra, self.eco):
            self.spolys += [tuple(k + off for k in p.vertices) for p in o.data.polygons]; off += len(ec)
            self.spart = np.r_[self.spart, np.full(len(o.data.polygons), 'banner')]
        gl = arm.get('qa_grip_left'); self.gripL = Vector(json.loads(gl)) if gl else None

    def _tips(self, side):
        # per digit: the tip vertices = those of its last bone within 1.5 mm of its farthest point along the bone (rest)
        out_, chains = {}, {}
        for n in sorted({n for n in set(self.dom) if is_hand(n, side) and not n.startswith('hand.')}):
            chains.setdefault(n.split('.')[0].rstrip('0123456789'), []).append(n)
        for base, ns in chains.items():
            last = sorted(ns, key=lambda n: (len(n), n))[-1]
            b = self.arm.data.bones[last]; h, t = Vector(b.head_local), Vector(b.tail_local); d = (t - h).normalized()
            idx = np.nonzero(self.dom == last)[0]
            if not len(idx): continue
            proj = (self.rest[idx] - np.array(h)) @ np.array(d)
            out_[base] = idx[proj > proj.max() - 0.0015]
        return out_

    def staff_world(self):
        out = []
        for o, c in [(self.staff, self.sco)] + list(zip(self.extra, self.eco)):
            m = np.array(o.matrix_world); out.append(c @ m[:3, :3].T + m[:3, 3])
        return np.concatenate(out)
    def axis(self):
        m = self.staff.matrix_world; a = m @ Vector((self.cx, self.cy, 0)); b = m @ Vector((self.cx, self.cy, 1))
        return a, (b - a).normalized()
    def shaft_dist(self, P):
        # distance from points to the shaft surface (negative = inside), and their height along the staff
        m = np.array(self.staff.matrix_world.inverted()); L = P @ m[:3, :3].T + m[:3, 3]; z = L[:, 2]
        pr = self.prof; cx, cy, r = (np.interp(z, pr[:, 0], pr[:, k]) for k in (1, 2, 3))
        return np.hypot(L[:, 0] - cx, L[:, 1] - cy) - r, z
    def body_world(self):
        dg = bpy.context.evaluated_depsgraph_get(); ev = self.body.evaluated_get(dg); me = ev.to_mesh()
        c = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', c); ev.to_mesh_clear()
        m = np.array(self.body.matrix_world); return c.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
    def hand_m(self, n): return self.arm.matrix_world @ self.pbs[n].matrix

    def overlap(self, left_on, P=None):
        """Staff/body overlap now: (pair count, {body region: n}, {staff part: n}) outside the gripping hands."""
        P = self.body_world() if P is None else P; S = self.staff_world()
        bt = BVHTree.FromPolygons([Vector(p) for p in P], self.polys, all_triangles=False)
        st = BVHTree.FromPolygons([Vector(p) for p in S], self.spolys, all_triangles=False)
        hits = [(i, j) for i, j in bt.overlap(st) if not self.PH['R'][i] and not (left_on and self.PH['L'][i])]
        regs, parts = {}, {}
        for i, j in hits:
            parts[self.spart[j]] = parts.get(self.spart[j], 0) + 1
            for k in self.pv[i]:
                if k >= 0: g = REGION(self.dom[k]); regs[g] = regs.get(g, 0) + 1
        return len(hits), regs, parts

    def clearance(self, left_on, P=None, skip=0.12):
        """Smallest gap between the staff and the body outside the gripping hands (near-misses read as clipping in
        motion): (metres, body region). Staff vertices within `skip` of a gripping fist are ignored (the wrist and
        sleeve sit right next to the shaft there)."""
        P = self.body_world() if P is None else P; S = self.staff_world()
        keep = ~self.PH['R'] & ~(self.PH['L'] if left_on else False)
        bt = BVHTree.FromPolygons([Vector(p) for p in P], [p for p, k in zip(self.polys, keep) if k], all_triangles=False)
        kidx = np.nonzero(keep)[0]
        fists = [np.array((self.hand_m('hand.R') @ Vector((0, 0.08, 0)))[:])]
        if left_on: fists.append(np.array((self.hand_m('hand.L') @ Vector((0, 0.08, 0)))[:]))
        far = np.ones(len(S), bool)
        for q in fists: far &= np.linalg.norm(S - q, axis=1) > skip
        best = (9.0, '')
        for v in S[far][::2]:
            loc, _, i, d = bt.find_nearest(Vector(v))
            if d is not None and d < best[0]:
                k = self.pv[kidx[i]][0]; best = (d, REGION(self.dom[k]))
        return best

    def left_line_mm(self):
        # the left fist's solved shaft line (stored on the rig) to the staff axis
        if self.gripL is None: return None
        q = np.array([(self.hand_m('hand.L') @ self.gripL)[:]]); d, z = self.shaft_dist(q)
        return float(d[0] + np.interp(z[0], self.prof[:, 0], self.prof[:, 3])) * 1000

    def grip(self, side, P=None):
        P = self.body_world() if P is None else P
        d, _ = self.shaft_dist(P[self.HAND[side]]); hi = np.nonzero(self.HAND[side])[0]
        g = {'inside_mm': round(float(-min(0.0, d.min())) * 1000, 1), 'deepest': self.dom[hi[np.argmin(d)]]}
        for dig, idx in self.TIPS[side].items():
            dt, _ = self.shaft_dist(P[idx]); g[dig] = (round(float(dt.min()) * 1000, 1), round(float(dt.max()) * 1000, 1))
        return g

    def tip_point(self): return self.staff.matrix_world @ Vector((self.cx, self.cy, self.TOPZ))


def use(arm, act):
    arm.animation_data.action = act
    if act.slots: arm.animation_data.action_slot = act.slots[0]


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = args[0] if args else None; only = set(args[1:])
    sc = bpy.context.scene
    arm = bpy.data.objects['ChimesingerRig']
    Q = Checker(arm, bpy.data.objects['Chimesinger'], bpy.data.objects['ChimeStaff'])
    GRIP_FRAMES = json.loads(arm.get('qa_grip_frames', '{}')) or {'Idle': [1], 'Run': [1], 'ResonantNote': [6], 'Bellstrike': [11]}
    LG = json.loads(arm.get('qa_lg', '{}'))             # per clip, per frame: left hand on the staff (0..1)
    report = {'shaft_radius': Q.R, 'clips': {}}
    tot_before = 0
    for act in [a for a in bpy.data.actions if a.use_fake_user and (not only or a.name in only)]:
        use(arm, act); f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        lg = LG.get(act.name)
        r = {'frames': f1 - f0 + 1, 'overlap_frames': [], 'regions': {}, 'parts': {}, 'staff_hand_drift_mm': 0.0,
             'staff_hand_drift_deg': 0.0, 'left_line_max_mm': None, 'tip': [], 'staff_min_z': [], 'grip': {}, 'clearance': [9.0, '', 0]}
        M0 = None
        for f in range(f0, f1 + 1):
            sc.frame_set(f); P = Q.body_world()
            if lg is not None: lon = lg[f - f0] > 0
            else:   # older builds: the left hand counts as holding when its palm centre is on the shaft
                lon = Q.shaft_dist(np.array([(Q.hand_m('hand.L') @ Vector((0, 0.09, 0)))[:]]))[0][0] < 0.03
            n, regs, parts = Q.overlap(lon, P)
            cl, cr = Q.clearance(lon, P)
            if cl < r['clearance'][0]: r['clearance'] = [cl, cr, f]
            if n:
                r['overlap_frames'].append(f)
                for k, v in regs.items(): r['regions'][k] = r['regions'].get(k, 0) + v
                for k, v in parts.items(): r['parts'][k] = r['parts'].get(k, 0) + v
            M = Q.hand_m('hand.R').inverted() @ Q.staff.matrix_world
            if M0 is None: M0 = M
            D = M0.inverted() @ M
            r['staff_hand_drift_mm'] = max(r['staff_hand_drift_mm'], D.to_translation().length * 1000)
            r['staff_hand_drift_deg'] = max(r['staff_hand_drift_deg'], math.degrees(D.to_quaternion().angle))
            if lon and (lg is None or lg[f - f0] >= 0.999) and Q.gripL is not None:
                r['left_line_max_mm'] = max(r['left_line_max_mm'] or 0.0, Q.left_line_mm())
            r['tip'].append([round(v, 4) for v in Q.tip_point()])
            r['staff_min_z'].append(round(float(Q.staff_world()[:, 2].min()), 4))
            if f in GRIP_FRAMES.get(act.name, []): r['grip'][f'R{f}'] = Q.grip('R', P)
            if lon and f in GRIP_FRAMES.get(act.name + '.L', []): r['grip'][f'L{f}'] = Q.grip('L', P)
        tp = np.array(r['tip']); r['tip_speed'] = np.r_[0, np.linalg.norm(np.diff(tp, axis=0), axis=1) * sc.render.fps].round(2).tolist()
        report['clips'][act.name] = r
        regs = ', '.join(f'{k} {v}' for k, v in sorted(r['regions'].items(), key=lambda kv: -kv[1])[:6])
        print('QA %-16s overlap %2d/%2d frames  closest %3.0f mm (%s, f%d)  staff/hand drift %.1f mm %.1f deg  left fist line %s  %s  %s' % (
            act.name, len(r['overlap_frames']), r['frames'], r['clearance'][0] * 1000, r['clearance'][1], r['clearance'][2], r['staff_hand_drift_mm'], r['staff_hand_drift_deg'],
            '-' if r['left_line_max_mm'] is None else '%.1f mm' % r['left_line_max_mm'], r['parts'] or '', regs))
        for k, g in r['grip'].items(): print('QA_GRIP %-16s %-5s %s' % (act.name, k, g))
    if out:
        with open(out, 'w') as fh: json.dump(report, fh)
        print('QA_REPORT', out)
