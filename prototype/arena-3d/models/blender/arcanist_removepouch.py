# python arcanist_removepouch.py <in Arcanist.glb> <out .glb> [preview dir]
# Removes the orange pouch on the Arcanist's right hip (critique 2026-10-28, problem 11: "an unidentifiable blob")
# as if it was never there, by patching the shipped GLB at the glTF level (the Meshy sources are gone):
#   - geometry: the pouch is 4 loose mesh pieces lying on the coat. Their triangles are dropped from the index buffer.
#     The vertices stay (unused), so vertex order, skin weights, the skeleton and all 23 clips are untouched.
#   - texture: where the pouch touched the coat, its orange was projected onto the coat. Those texels are filled
#     from the surrounding coat colour (harmonic fill) and only the base colour JPEG is re-encoded.
# Every other bufferView is copied byte for byte; the script checks that and refuses to run twice.
import io, json, struct, sys, os, numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
preview = sys.argv[3] if len(sys.argv) > 3 else None

def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    J = json.loads(d[20:20 + jl])
    bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, bytes(d[28 + jl:28 + jl + bl])

def write_glb(p, J, B):
    js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    B = bytes(B) + b'\0' * (-len(B) % 4)
    out = struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(B)) + struct.pack('<II', len(js), 0x4E4F534A) + js
    out += struct.pack('<II', len(B), 0x004E4942) + B
    open(p, 'wb').write(out)

J, B = read_glb(src)
assert not J.get('extras', {}).get('pouchRemoved'), 'this GLB already has the pouch removed'
CT = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}

def view_bytes(i):
    v = J['bufferViews'][i]; o = v.get('byteOffset', 0); return B[o:o + v['byteLength']]

def accessor(i):
    a = J['accessors'][i]; v = J['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
    stride = v.get('byteStride', dt.itemsize * n); base = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    if stride == dt.itemsize * n:
        return np.frombuffer(B, dt, count=a['count'] * n, offset=base).reshape(-1, n).copy()
    return np.array([np.frombuffer(B, dt, count=n, offset=base + k * stride) for k in range(a['count'])])

mesh = next(m for m in J['meshes'] if m['name'] == 'Arcanist'); prim = mesh['primitives'][0]
pos = accessor(prim['attributes']['POSITION']); uv = accessor(prim['attributes']['TEXCOORD_0'])
ia = prim['indices']; tri = accessor(ia).reshape(-1, 3).astype(np.int64)
mat = J['materials'][prim['material']]; img_i = J['textures'][mat['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
img_bv = J['images'][img_i]['bufferView']
tex = np.asarray(Image.open(io.BytesIO(view_bytes(img_bv))).convert('RGB')).astype(np.float32) / 255
H, W, _ = tex.shape
print('MESH', len(pos), 'verts', len(tri), 'tris; base colour', W, 'x', H)

# glTF is Y-up, -Z = his front... convert to the critique's frame: x = his left, y = -gltf z (front is -y), z = up
P = np.c_[pos[:, 0], -pos[:, 2], pos[:, 1]]

def sample(uvs):
    x = np.clip((uvs[..., 0] % 1) * (W - 1), 0, W - 1).astype(int); y = np.clip((uvs[..., 1] % 1) * (H - 1), 0, H - 1).astype(int)
    return tex[y, x]

def pouch_colour(c):   # the pouch's saturated orange-brown; the coat's gold trim is paler (lower saturation)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    return (r > 0.25) & (r > g * 1.15) & (g > b * 1.15) & ((r - b) / np.maximum(r, 1e-3) > 0.4)

# islands: connectivity through shared triangle indices only. (Welding by position would glue the pouch to the coat:
# Meshy left its rim touching the coat at shared points.)
weld = np.arange(len(pos))
parent = np.arange(len(pos))
def find(x):
    while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
    return x
for a, b, c in weld[tri]:
    ra, rb, rc = find(a), find(b), find(c); parent[rb] = ra; parent[find(rc)] = ra
root = np.array([find(x) for x in range(len(parent))])[weld]
tri_isl = root[tri[:, 0]]
centre = P[tri].mean(1); tri_orange = pouch_colour(sample(uv[tri].mean(1)))
# the pouch: islands lying wholly inside its box on the right hip that are mostly that orange
LO, HI = np.array([-0.30, -0.175, 0.74]), np.array([-0.20, -0.015, 0.96])
remove = np.zeros(len(tri), bool); chosen = []
for isl in np.unique(tri_isl[np.all((centre > LO) & (centre < HI), 1)]):
    t = tri_isl == isl; v = np.unique(tri[t])
    inside = np.all((P[v] > LO - 0.01) & (P[v] < HI + 0.01))
    frac = tri_orange[t].mean()
    if inside and frac > 0.5 and len(v) < 600:
        remove |= t; chosen.append((int(t.sum()), round(float(frac), 2), P[v].min(0).round(3).tolist(), P[v].max(0).round(3).tolist()))
print('POUCH PIECES', len(chosen)); [print('  piece', c) for c in chosen]
print('REMOVED', int(remove.sum()), 'triangles')
assert 3 <= len(chosen) <= 6 and 300 < remove.sum() < 1500, 'pouch selection looks wrong; refusing'

# texture: Meshy projected the pouch's colour onto the coat where it touched. On coat triangles within RADIUS of the
# pouch (not the hand's), every warm texel (red above blue: the coat is blue or white, so warmth there is pouch spill)
# is blended toward the coat blue sampled from those triangles, keeping its light and shade.
RADIUS = float(os.environ.get('POUCH_RADIUS', '0.025'))
joints = accessor(prim['attributes']['JOINTS_0']); weights = accessor(prim['attributes']['WEIGHTS_0'])
names = [J['nodes'][j]['name'] for j in J['skins'][0]['joints']]
dom = np.array([names[joints[i][np.argmax(weights[i])]] for i in range(len(pos))])
pv = P[np.unique(tri[remove])]
dist = np.sqrt(((centre[:, None, :] - pv[None, :, :]) ** 2).sum(-1)).min(1)
handish = np.isin(dom[tri], ['hand.R', 'forearm.R', 'hand.L', 'forearm.L']).any(1)
near = ~remove & (dist < RADIUS) & ~handish
cover = np.zeros((H, W), bool)
yy, xx = np.mgrid[0:H, 0:W]
for t in np.where(near)[0]:
    q = uv[tri[t]] * [W - 1, H - 1]
    x0, y0 = np.floor(q.min(0)).astype(int) - 1; x1, y1 = np.ceil(q.max(0)).astype(int) + 1
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, H - 1)
    if x1 < x0 or y1 < y0: continue
    px, py = xx[y0:y1 + 1, x0:x1 + 1] + 0.5, yy[y0:y1 + 1, x0:x1 + 1] + 0.5
    (ax, ay), (bx, by), (cx, cy) = q
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-9: continue
    l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d; l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d
    e = -1.5 / max(np.sqrt(abs(d)), 1)   # include a ~1.5 px rim so the JPEG fringe on the triangle edges goes too
    cover[y0:y1 + 1, x0:x1 + 1] |= (l1 >= e) & (l2 >= e) & (1 - l1 - l2 >= e)
r, g, b = tex[..., 0], tex[..., 1], tex[..., 2]
warmth = np.clip((r - b - 0.02) / 0.10, 0, 1) * cover
# directly under the pouch Meshy never saw the coat and filled it grey-white: there, anything not clearly blue goes too
UNDER = float(os.environ.get('POUCH_UNDER', '0.025'))
under = np.zeros((H, W), bool)
for t in np.where(near & (dist < UNDER))[0]:
    q = uv[tri[t]] * [W - 1, H - 1]
    x0, y0 = np.floor(q.min(0)).astype(int) - 1; x1, y1 = np.ceil(q.max(0)).astype(int) + 1
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, H - 1)
    if x1 < x0 or y1 < y0: continue
    px, py = xx[y0:y1 + 1, x0:x1 + 1] + 0.5, yy[y0:y1 + 1, x0:x1 + 1] + 0.5
    (ax, ay), (bx, by), (cx, cy) = q
    d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
    if abs(d) < 1e-9: continue
    l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d; l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d
    e = -1.5 / max(np.sqrt(abs(d)), 1)
    under[y0:y1 + 1, x0:x1 + 1] |= (l1 >= e) & (l2 >= e) & (1 - l1 - l2 >= e)
# 1 where blue does not dominate by 0.12; only mid tones, so the real white panels (brightness ~0.8) stay white
greyness = np.clip(((r + g) / 2 + 0.12 - b) / 0.10, 0, 1) * np.clip((0.66 - (0.299 * r + 0.587 * g + 0.114 * b)) / 0.06, 0, 1) * under
warmth = np.maximum(warmth, greyness)
cool = cover & (b > r + 0.08) & (b > 0.2)            # the coat blue inside the same triangles
target = np.median(tex[cool], 0) if cool.sum() > 50 else np.array([0.16, 0.17, 0.42])
lum = lambda c: c[..., 0] * 0.299 + c[..., 1] * 0.587 + c[..., 2] * 0.114
shade = np.clip(lum(tex) / max(lum(target), 1e-3), 0.6, 1.4)[..., None]
out = tex * (1 - warmth[..., None]) + np.clip(target * shade, 0, 1) * warmth[..., None]
print('NEAR', int(near.sum()), 'coat triangles; TEXELS', int((warmth > 0).sum()), 'recoloured toward', (target * 255).round().astype(int).tolist())
mask = warmth > 0
buf = io.BytesIO(); Image.fromarray(np.round(out * 255).astype(np.uint8)).save(buf, 'JPEG', quality=90)
new_img = buf.getvalue()

# new index data, same component type
idx_acc = J['accessors'][ia]; keep = tri[~remove].ravel().astype(CT[idx_acc['componentType']])
assert sum(1 for a in J['accessors'] if a.get('bufferView') == idx_acc['bufferView']) == 1, 'index view is shared'
assert sum(1 for im in J['images'] if im.get('bufferView') == img_bv) == 1, 'image view is shared'
idx_acc['count'] = int(keep.size); idx_acc.pop('byteOffset', None)
if 'min' in idx_acc: idx_acc['min'] = [int(keep.min())]; idx_acc['max'] = [int(keep.max())]
replace = {idx_acc['bufferView']: keep.tobytes(), img_bv: new_img}

# rebuild BIN: every view in its original order, 4-byte aligned, untouched views copied verbatim
ORIG = [view_bytes(i) for i in range(len(J['bufferViews']))]
view_bytes = lambda i: ORIG[i]
NB = bytearray()
for i, v in enumerate(J['bufferViews']):
    data = replace.get(i, view_bytes(i))
    NB += b'\0' * (-len(NB) % 4); v['byteOffset'] = len(NB); v['byteLength'] = len(data); NB += data
J['buffers'][0]['byteLength'] = len(NB)
J.setdefault('extras', {})['pouchRemoved'] = 'arcanist_removepouch.py 2026-10-28'
write_glb(dst, J, NB)

# check: everything except the two replaced views is byte-identical
J2, B2 = read_glb(dst)
for i, v in enumerate(J2['bufferViews']):
    if i in replace: continue
    o = v['byteOffset']; assert B2[o:o + v['byteLength']] == view_bytes(i), f'view {i} changed'
print('WROTE', dst, len(open(dst, 'rb').read()), 'bytes; untouched views verified:', len(J2['bufferViews']) - len(replace))
