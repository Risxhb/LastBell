# python arcanist_dissolvechain.py <in Arcanist.glb> <out .glb> <chain> [<chain> ...]
# Hands the coat vertices of one robe chain over to its two neighbouring chains, at the glTF level (skin weights only).
#
# Why: arcanist_rig.py measured each of the 16 robe chains' rest joints on the coat's outer surface, but where a chain
# falls in one of the coat's splits its joints sit tucked in toward the legs. The cloth sim then pulls that chain out to
# the ring of its neighbours (spacing constraints and the 19 cm thigh capsules), and the coat skinned to it moves out by
# the whole difference while its neighbours move in: a bump. robe06 (his back-left, 146 deg from the front) sits
# 11.7 cm proud of its neighbours at hip height, averaged over all 23 clips (12.9 cm in Idle, the menu's clip).
# Its vertices are given to robe05/robe07 by their angle between those two chains, segment for segment, so that panel
# now follows the line of the coat on either side. Animations, skeleton and mesh are untouched (verified byte-identical).
import json, struct, sys, numpy as np

src, dst, chains = sys.argv[1], sys.argv[2], [int(c) for c in sys.argv[3:]]
assert chains, 'name at least one chain, e.g. 6'

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
done = J.setdefault('extras', {}).setdefault('dissolvedChains', [])
assert not set(chains) & set(done), f'chains {set(chains) & set(done)} already dissolved in this GLB'
CT = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}; NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}
def view(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]; dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
    assert v.get('byteStride', dt.itemsize * n) == dt.itemsize * n, 'interleaved data not supported'
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return np.frombuffer(B, dt, count=a['count'] * n, offset=off).reshape(-1, n).copy(), off, a
prim = next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]['attributes']
pos, _, _ = view(prim['POSITION'])
jt, jt_off, ja = view(prim['JOINTS_0']); wt, wt_off, wa = view(prim['WEIGHTS_0'])
assert wa['componentType'] == 5126, 'expects float weights'
names = [J['nodes'][j]['name'] for j in J['skins'][0]['joints']]; ji = {n: i for i, n in enumerate(names)}
# rest joint heads of each chain (the skin's inverse bind matrices hold the rest pose; take the bone head = bind inverse)
ibm, _, _ = view(J['skins'][0]['inverseBindMatrices']); ibm = ibm.reshape(-1, 4, 4).transpose(0, 2, 1)
head = lambda n: np.linalg.inv(ibm[ji[n]])[:3, 3]
P = np.c_[pos[:, 0], -pos[:, 2], pos[:, 1]]                    # critique frame: x = his left, y = -front, z = up
ang_v = np.degrees(np.arctan2(P[:, 0], -(P[:, 1] + 0.06)))     # 0 front, +90 his left (arcanist_rig.py's convention)
def chain_angle(i):
    h = head(f'robe{i:02d}_0'); return np.degrees(np.arctan2(h[0], -(-h[2] + 0.06)))
before = wt.copy()
for c in chains:
    lo, hi = (c - 1) % 16, (c + 1) % 16
    a_lo, a_c, a_hi = chain_angle(lo), chain_angle(c), chain_angle(hi)
    span = (a_hi - a_lo + 540) % 360 - 180                     # signed angular span lo -> hi (handles the +-180 seam)
    t = np.clip(((ang_v - a_lo + 540) % 360 - 180) / span, 0, 1)
    print(f'CHAIN robe{c:02d} at {a_c:.1f} deg -> robe{lo:02d} ({a_lo:.1f}) / robe{hi:02d} ({a_hi:.1f})')
    moved = 0
    for k in range(3):
        src_j = ji[f'robe{c:02d}_{k}']; to = (ji[f'robe{lo:02d}_{k}'], ji[f'robe{hi:02d}_{k}'])
        rows = np.nonzero((jt == src_j) & (wt > 0))[0]
        for r in np.unique(rows):
            # collect this vertex's influences as a dict, move the chain's weight, keep the 4 largest, renormalise
            d = {}
            for s in range(4):
                if wt[r, s] > 0: d[int(jt[r, s])] = d.get(int(jt[r, s]), 0) + float(wt[r, s])
            w = d.pop(src_j)
            d[to[0]] = d.get(to[0], 0) + w * (1 - t[r]); d[to[1]] = d.get(to[1], 0) + w * t[r]
            top = sorted(d.items(), key=lambda kv: -kv[1])[:4]; s_ = sum(x for _, x in top)
            jt[r] = 0; wt[r] = 0
            for s, (j, x) in enumerate(top): jt[r, s] = j; wt[r, s] = x / s_
            moved += 1
    print(f'  {moved} vertex influences moved')
    done.append(c)
assert not np.isin(jt[wt > 0], [ji[f'robe{c:02d}_{k}'] for c in chains for k in range(3)]).any()
assert np.allclose(wt.sum(1)[before.sum(1) > 0], 1, atol=1e-5)
B[jt_off:jt_off + jt.nbytes] = jt.astype(CT[ja['componentType']]).tobytes(); B[wt_off:wt_off + wt.nbytes] = wt.astype(np.float32).tobytes()
J['extras']['dissolvedChains'] = done
write_glb(dst, J, B)
# check: only the JOINTS_0 / WEIGHTS_0 bytes differ from the input
J0, B0 = read_glb(src); J1, B1 = read_glb(dst)
assert len(B0) == len(B1)
diff = np.nonzero(np.frombuffer(bytes(B0), np.uint8) != np.frombuffer(bytes(B1), np.uint8))[0]
spans = [(jt_off, jt_off + jt.nbytes), (wt_off, wt_off + wt.nbytes)]
inside = np.zeros(len(diff), bool)
for a, b in spans: inside |= (diff >= a) & (diff < b)
assert inside.all(), 'bytes outside the skin weights changed'
assert {k: v for k, v in J0.items() if k != 'extras'} == {k: v for k, v in J1.items() if k != 'extras'}, 'glTF JSON changed'
print('WROTE', dst, '-', len(diff), 'bytes changed, all inside JOINTS_0 / WEIGHTS_0')
