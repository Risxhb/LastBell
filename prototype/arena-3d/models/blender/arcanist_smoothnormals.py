# python arcanist_smoothnormals.py <in Arcanist.glb> <out .glb> [radius m]
# Smooths the robe's shading normals across mesh fragments, at the glTF level (NORMAL only).
#
# Why: the Meshy mesh is a patchwork of ~1,860 loose fragments, and each fragment's normals were computed on its own.
# On the robe a fragment's normals sit 30-40 deg (up to 90 deg) off the surface around it, so in game lighting every
# fragment shades differently and its jagged outline reads as a point, notch or shard (critique 2026-10-28, user report
# on the back below the belt). Here each robe vertex gets the area- and distance-weighted average of the face normals of
# every robe face within the radius (default 3 cm) in the rest pose, ignoring fragment boundaries, so touching fragments
# shade as one surface. Faces turned away from the vertex's own side (the back of a thin panel) are left out, so the two
# sides of a panel don't cancel. Positions, skin weights, UVs and animations are untouched (verified byte for byte).
import json, struct, sys, numpy as np

src, dst = sys.argv[1], sys.argv[2]
RAD = float(sys.argv[3]) if len(sys.argv) > 3 else 0.03

def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    J = json.loads(d[20:20 + jl]); bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, bytearray(d[28 + jl:28 + jl + bl])
def write_glb(p, J, B):
    js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); B = bytes(B) + bytes(-len(B) % 4)
    open(p, 'wb').write(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(B)) + struct.pack('<II', len(js), 0x4E4F534A)
                        + js + struct.pack('<II', len(B), 0x004E4942) + B)
J, B = read_glb(src)
assert 'normalsSmoothed' not in J.get('extras', {}), 'already smoothed'
CT = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}; NC = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}
def view(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]; dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
    assert v.get('byteStride', dt.itemsize * n) == dt.itemsize * n
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return np.frombuffer(bytes(B), dt, count=a['count'] * n, offset=off).reshape(-1, n).copy(), off
prim = next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]
at = prim['attributes']
pos, _ = view(at['POSITION']); nrm, n_off = view(at['NORMAL']); jt, _ = view(at['JOINTS_0']); wt, _ = view(at['WEIGHTS_0'])
tri = view(prim['indices'])[0].reshape(-1, 3).astype(np.int64)
names = [J['nodes'][j]['name'] for j in J['skins'][0]['joints']]
W = np.zeros((len(pos), len(names))); np.add.at(W, (np.repeat(np.arange(len(pos)), 4), jt.ravel().astype(int)), wt.ravel())
robe = np.array([n.startswith('robe') for n in names]); hips = np.array([n == 'hips' for n in names])
z = pos[:, 1]
# the robe: vertices mostly on robe bones, plus the hips-bound band just under the belt that the robe hangs from
pool = (W[:, robe].sum(1) > 0.5) | ((W[:, robe | hips].sum(1) > 0.9) & (z < 0.93))
# faces of the robe: area-weighted normals and centroids
p0, p1, p2 = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
fn = np.cross(p1 - p0, p2 - p0); fa = np.linalg.norm(fn, axis=1); keep = fa > 1e-12
fpool = pool[tri].sum(1) >= 2
F = np.nonzero(keep & fpool)[0]; fc = (p0[F] + p1[F] + p2[F]) / 3; fu = fn[F] / fa[F, None]; fw = fa[F] / 2
idx = np.nonzero(pool)[0]; print('ROBE vertices', len(idx), 'faces', len(F))
cell = np.floor(fc / RAD).astype(np.int64); buckets = {}
for k, c in enumerate(map(tuple, cell)): buckets.setdefault(c, []).append(k)
sig2 = (RAD / 2) ** 2; out = nrm.copy(); tilt_before = []; tilt_after = []
for i in idx:
    cx, cy, cz = np.floor(pos[i] / RAD).astype(np.int64); nb = []
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1): nb += buckets.get((cx + dx, cy + dy, cz + dz), [])
    if not nb: continue
    nb = np.array(nb); d2 = ((fc[nb] - pos[i]) ** 2).sum(1); own = nrm[i] / (np.linalg.norm(nrm[i]) + 1e-9)
    m = (d2 < RAD * RAD) & (fu[nb] @ own > 0)
    if not m.any(): continue
    v = (fu[nb[m]] * (fw[nb[m]] * np.exp(-d2[m] / (2 * sig2)))[:, None]).sum(0)
    if np.linalg.norm(v) < 1e-12: continue
    v /= np.linalg.norm(v); out[i] = v
    tilt_before.append(np.degrees(np.arccos(np.clip(own @ v, -1, 1))))
print('NORMALS: %d robe vertices re-shaded; their old normals sat median %.0f deg, 90th pct %.0f deg off the smoothed surface' % (
    len(tilt_before), np.median(tilt_before), np.percentile(tilt_before, 90)))
B[n_off:n_off + out.nbytes] = out.astype(np.float32).tobytes()
J.setdefault('extras', {})['normalsSmoothed'] = {'radius': RAD, 'script': 'arcanist_smoothnormals.py'}
write_glb(dst, J, B)
J0, B0 = read_glb(src); J1, B1 = read_glb(dst); assert len(B0) == len(B1)
diff = np.nonzero(np.frombuffer(bytes(B0), np.uint8) != np.frombuffer(bytes(B1), np.uint8))[0]
assert ((diff >= n_off) & (diff < n_off + out.nbytes)).all(), 'bytes outside NORMAL changed'
assert {k: v for k, v in J0.items() if k != 'extras'} == {k: v for k, v in J1.items() if k != 'extras'}
print('WROTE', dst, '-', len(diff), 'bytes changed, all inside NORMAL')
