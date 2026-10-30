# python arcanist_flattenpieces.py <in Arcanist.glb> <out .glb> <verts.json> [allowance m]
# Presses raised mesh pieces flat onto the coat around them, at the glTF level (POSITION only, bind pose).
#
# verts.json: rest positions (glTF frame) of the pieces' vertices, e.g. exported from Blender by mesh island. Every glTF
# vertex at one of those positions is a piece vertex. For each, the coat surface is estimated from the 24 nearest
# robe vertices that are not part of the pieces (a least-squares plane, its normal pointed away from the body axis),
# and anything standing out more than the allowance (default 3 mm) is pushed back down along that normal. Skin weights,
# UVs, normals and animations are untouched (verified byte for byte), so all 23 clips deform the flattened shape.
# Critique 2026-10-28, user request: the V-shaped tab and the teardrop tab on his back (their gold is painted out by
# arcanist_paintout.py; this removes the bump they leave).
import json, struct, sys, numpy as np

src, dst, vp = sys.argv[1], sys.argv[2], sys.argv[3]
ALLOW = float(sys.argv[4]) if len(sys.argv) > 4 else 0.003

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
prim = next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]['attributes']
def view(i, dt, n):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]; off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    assert v.get('byteStride', np.dtype(dt).itemsize * n) == np.dtype(dt).itemsize * n
    return np.frombuffer(bytes(B), dt, count=a['count'] * n, offset=off).reshape(-1, n).copy(), off, a
pos, p_off, pa = view(prim['POSITION'], np.float32, 3)
jt, _, _ = view(prim['JOINTS_0'], np.uint8, 4); wt, _, _ = view(prim['WEIGHTS_0'], np.float32, 4)
names = [J['nodes'][j]['name'] for j in J['skins'][0]['joints']]
robe_ids = [i for i, n in enumerate(names) if n.startswith('robe') or n == 'hips']
robeish = np.where(np.isin(jt, robe_ids), wt, 0).sum(1) > 0.9
target = np.array(json.load(open(vp)))
# piece vertices: every glTF vertex at a listed position
key = lambda a: np.round(a / 2e-5).astype(np.int64)
tk = {tuple(k) for k in key(target)}
sel = np.array([tuple(k) in tk for k in key(pos)])
print('PIECE vertices', int(sel.sum()), 'from', len(target), 'listed positions')
assert sel.sum() >= len(target) * 0.9, 'positions did not match the GLB'
ref = np.nonzero(robeish & ~sel)[0]; R = pos[ref].astype(np.float64)
new = pos.astype(np.float64).copy()
# one push vector per piece vertex (toward a wide, stable fit of the coat: 80 neighbours), then smoothed over the
# piece's own triangles so connected vertices move together (no tears), capped at CAP
CAP = 0.02
P0 = pos.astype(np.float64); idx = np.nonzero(sel)[0]; push = np.zeros((len(P0), 3))
for i in idx:
    p = P0[i]; d = np.linalg.norm(R - p, axis=1); Q = R[np.argsort(d)[:80]]
    c = Q.mean(0); _, _, vt = np.linalg.svd(Q - c); n = vt[2]
    out = np.array([p[0], 0, p[2]]); out /= np.linalg.norm(out) + 1e-9
    if n @ out < 0: n = -n
    h = (p - c) @ n
    if h > ALLOW: push[i] = -n * min(h - ALLOW, CAP)
tri = view(next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]['indices'], np.uint32 if J['accessors'][next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]['indices']]['componentType'] == 5125 else np.uint16, 1)[0].reshape(-1, 3)
tri = tri[sel[tri].all(1)]
# vertices sharing a position (UV seams) must move together: group them
grp = {}
for i in idx: grp.setdefault(tuple(np.round(P0[i] / 2e-5).astype(np.int64)), []).append(i)
for _ in range(12):
    acc = push.copy(); cnt = np.ones(len(P0))
    for a_, b_ in ((0, 1), (1, 2), (2, 0)):
        np.add.at(acc, tri[:, a_], push[tri[:, b_]]); np.add.at(cnt, tri[:, a_], 1)
        np.add.at(acc, tri[:, b_], push[tri[:, a_]]); np.add.at(cnt, tri[:, b_], 1)
    push = acc / cnt[:, None]
    for g in grp.values(): push[g] = push[g].mean(0)
new[idx] = P0[idx] + push[idx]
moved = list(np.linalg.norm(push[idx], axis=1))
moved = np.array(moved) * 100
print('FLATTENED %d vertices: mean push %.2f cm, max %.2f cm' % (len(moved), moved.mean() if len(moved) else 0, moved.max() if len(moved) else 0))
pos2 = new.astype(np.float32); B[p_off:p_off + pos2.nbytes] = pos2.tobytes()
pa['min'] = pos2.min(0).tolist(); pa['max'] = pos2.max(0).tolist()
J.setdefault('extras', {}).setdefault('flattenedPieces', []).append({'verts': int(sel.sum()), 'allowance': ALLOW, 'script': 'arcanist_flattenpieces.py'})
write_glb(dst, J, B)
J0, B0 = read_glb(src); J1, B1 = read_glb(dst); assert len(B0) == len(B1)
diff = np.nonzero(np.frombuffer(bytes(B0), np.uint8) != np.frombuffer(bytes(B1), np.uint8))[0]
assert ((diff >= p_off) & (diff < p_off + pos2.nbytes)).all(), 'bytes outside POSITION changed'
print('WROTE', dst, '-', len(diff), 'bytes changed, all inside POSITION')
