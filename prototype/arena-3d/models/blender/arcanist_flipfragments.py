# python arcanist_flipfragments.py <in Arcanist.glb> <out .glb> <fragments.json>
# Turns inside-out mesh fragments the right way round, at the glTF level (index winding and NORMAL only).
#
# fragments.json: {name: {"pos": [[x, y, z], ...]}} rest positions (glTF frame) of each fragment's vertices, e.g.
# exported from Blender by mesh island. Every glTF vertex at one of those positions belongs to the fragment; its
# triangles get their winding reversed and its vertices their normals negated, so the fragment faces outward.
# Critique 2026-10-28, user report: a dark notch on his right white panel is two tiny inside-out fragments (3 triangles
# each) facing into his body, which Unity lights from behind (dark) even with the material double-sided.
import json, struct, sys, numpy as np

src, dst, fp = sys.argv[1], sys.argv[2], sys.argv[3]
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
prim = next(m for m in J['meshes'] if m['name'] == 'Arcanist')['primitives'][0]
CT = {5123: np.uint16, 5125: np.uint32, 5126: np.float32}
def view(i, n):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]; dt = CT[a['componentType']]
    off = v.get('byteOffset', 0) + a.get('byteOffset', 0); assert v.get('byteStride', np.dtype(dt).itemsize * n) == np.dtype(dt).itemsize * n
    return np.frombuffer(bytes(B), dt, count=a['count'] * n, offset=off).reshape(-1, n).copy(), off
pos, _ = view(prim['attributes']['POSITION'], 3); nrm, n_off = view(prim['attributes']['NORMAL'], 3)
idx, i_off = view(prim['indices'], 1); tri = idx.reshape(-1, 3)
key = lambda a: [tuple(k) for k in np.round(np.asarray(a) / 2e-5).astype(np.int64)]
vk = key(pos); spans = []
for name, f in json.load(open(fp)).items():
    want = set(key(f['pos'])); sel = np.array([k in want for k in vk])
    # its triangles: all corners at fragment positions and facing into the body (overlapping neighbours that share
    # positions but face outward are left alone)
    p0, p1, p2 = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]; fn = np.cross(p1 - p0, p2 - p0); c = (p0 + p1 + p2) / 3
    radial = np.c_[c[:, 0], np.zeros(len(c)), c[:, 2]]                  # away from the body axis (glTF Y up)
    t = sel[tri].all(1) & (np.sum(fn * radial, 1) < 0)
    assert t.any(), f'fragment {name} not found'
    tri[t] = tri[t][:, [0, 2, 1]]
    own = np.zeros(len(pos), bool); own[tri[t].ravel()] = True; own[tri[~t].ravel()] = False   # normals only this fragment uses
    nrm[own] = -nrm[own]
    print('FLIPPED fragment', name, '-', int(t.sum()), 'triangles,', int(own.sum()), 'vertex normals')
B[i_off:i_off + idx.nbytes] = tri.reshape(-1, 1).astype(idx.dtype).tobytes(); B[n_off:n_off + nrm.nbytes] = nrm.astype(np.float32).tobytes()
J.setdefault('extras', {}).setdefault('flippedFragments', []).extend(json.load(open(fp)).keys())
write_glb(dst, J, B)
J0, B0 = read_glb(src); J1, B1 = read_glb(dst); assert len(B0) == len(B1)
diff = np.nonzero(np.frombuffer(bytes(B0), np.uint8) != np.frombuffer(bytes(B1), np.uint8))[0]
ok = ((diff >= i_off) & (diff < i_off + idx.nbytes)) | ((diff >= n_off) & (diff < n_off + nrm.nbytes))
assert ok.all(), 'bytes outside the indices / normals changed'
print('WROTE', dst, '-', len(diff), 'bytes changed, all inside the index list and NORMAL')
