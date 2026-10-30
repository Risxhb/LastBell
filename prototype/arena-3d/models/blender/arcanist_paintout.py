# python arcanist_paintout.py <in Arcanist.glb> <out .glb> <hits.json> <region=mode> [...] [--preview dir]
# Paints details out of the Arcanist's base colour, at the glTF level (only the base colour image changes).
#
# hits.json maps a region name to [[screen x, screen y, u, v], ...]: the glTF UVs that a ray-cast of every pixel of that
# screen region landed on (made with a Blender ray-cast of a render). The texels under those hits (grown 2 px) form
# the region's mask. Modes:
#   gold  - gold / tan trim painted on the coat goes: texels that are not clearly blue become the region's own coat
#           blue (the median of its blue texels), keeping their light and shade.
#   blue  - blue patches painted on a white panel go: blue texels become the region's own white (the median of its pale
#           texels), keeping their light and shade.
# Critique 2026-10-28, user request: the V-shaped tab and the teardrop tab on his back (gold trim), the blue diamond on
# his right white panel and the blue shards in the corner of his left white panel. The surface under them is nearly
# flat (checked on an untextured render), so painting them out flattens them.
import io, json, struct, sys, os, numpy as np
from PIL import Image

src, dst, hits_p = sys.argv[1], sys.argv[2], sys.argv[3]
rest = sys.argv[4:]; preview = None
if '--preview' in rest: i = rest.index('--preview'); preview = rest[i + 1]; rest = rest[:i] + rest[i + 2:]
MODES = dict(r.split('=') for r in rest)
assert all(m in ('gold', 'blue') for m in MODES.values())

def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    J = json.loads(d[20:20 + jl]); bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, bytes(d[28 + jl:28 + jl + bl])
def write_glb(p, J, B):
    js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4); B = bytes(B) + bytes(-len(B) % 4)
    open(p, 'wb').write(struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(B)) + struct.pack('<II', len(js), 0x4E4F534A)
                        + js + struct.pack('<II', len(B), 0x004E4942) + B)
J, B = read_glb(src)
done = J.setdefault('extras', {}).setdefault('paintedOut', [])
assert not set(MODES) & set(done), f'regions {set(MODES) & set(done)} already painted out'
mat = next(m for m in J['materials'] if m['name'] == 'arcanist')
img_i = J['textures'][mat['pbrMetallicRoughness']['baseColorTexture']['index']]['source']; bv = J['images'][img_i]['bufferView']
assert sum(1 for im in J['images'] if im.get('bufferView') == bv) == 1
v = J['bufferViews'][bv]; o = v.get('byteOffset', 0)
tex = np.asarray(Image.open(io.BytesIO(B[o:o + v['byteLength']])).convert('RGB')).astype(np.float32) / 255
H, W, _ = tex.shape; out = tex.copy()
HITS = json.load(open(hits_p))
lum = lambda c: c[..., 0] * 0.299 + c[..., 1] * 0.587 + c[..., 2] * 0.114
for name, mode in MODES.items():
    h = np.array(HITS[name]); x = np.clip((h[:, 2] % 1 * (W - 1)).round().astype(int), 0, W - 1); y = np.clip((h[:, 3] % 1 * (H - 1)).round().astype(int), 0, H - 1)
    m = np.zeros((H, W), bool); m[y, x] = True
    for _ in range(2):
        g = m.copy(); g[1:] |= m[:-1]; g[:-1] |= m[1:]; g[:, 1:] |= m[:, :-1]; g[:, :-1] |= m[:, 1:]; m = g
    c = tex[m]; blu = c[:, 2] - c[:, :2].max(1); L = lum(c)        # blueness: blue above the larger of red / green
    if mode == 'gold':
        keep = blu > 0.18                                                     # the coat's blue
        tb = float(np.median(blu[keep])); tl = float(np.median(L[keep]))
        # gold (not blue) goes fully; the pale halo around it (blue, but far less than the coat, or much brighter) too
        strength = np.maximum(np.clip((tb - 0.06 - blu) / 0.08, 0, 1), np.clip((L - tl * 1.6) / (tl * 0.4), 0, 1))
    else:
        keep = (blu < 0.2) & (L > 0.35)                                       # the panel's own (white or lavender) colour
        tb = float(np.median(blu[keep]))
        strength = np.clip((blu - (tb + 0.05)) / 0.05, 0, 1)                  # 1 where clearly bluer than the panel
    assert keep.sum() > 50, f'{name}: too few {"blue" if mode == "gold" else "panel"} texels to sample'
    target = np.median(c[keep], 0)
    shade = np.clip(L / max(float(lum(target)), 1e-3), 0.85, 1.08)[:, None]   # keep folds, never brighter than the cloth
    new = c * (1 - strength[:, None]) + np.clip(target * shade, 0, 1) * strength[:, None]
    out[m] = new
    print('REGION %s (%s): %d texels in mask, %d recoloured (strength > 0.5) toward RGB %s' % (
        name, mode, m.sum(), int((strength > 0.5).sum()), (target * 255).round().astype(int).tolist()))
    if preview:
        os.makedirs(preview, exist_ok=True); ys, xs = np.nonzero(m)
        y0, y1, x0, x1 = max(ys.min() - 20, 0), min(ys.max() + 20, H), max(xs.min() - 20, 0), min(xs.max() + 20, W)
        if (y1 - y0) * (x1 - x0) < 4e6:
            a_ = Image.fromarray((tex[y0:y1, x0:x1] * 255).astype(np.uint8)); b_ = Image.fromarray((out[y0:y1, x0:x1] * 255).astype(np.uint8))
            s = Image.new('RGB', (a_.width * 2 + 8, a_.height)); s.paste(a_, (0, 0)); s.paste(b_, (a_.width + 8, 0)); s.save(f'{preview}/texture_{name}.png')
    done.append(name)
buf = io.BytesIO(); Image.fromarray(np.round(out * 255).astype(np.uint8)).save(buf, 'JPEG', quality=90); new_img = buf.getvalue()
# rebuild BIN: every view in order, the image view replaced, the rest copied verbatim
NB = bytearray(); orig = [B[vv.get('byteOffset', 0):vv.get('byteOffset', 0) + vv['byteLength']] for vv in J['bufferViews']]
for i, vv in enumerate(J['bufferViews']):
    data = new_img if i == bv else orig[i]
    NB += bytes(-len(NB) % 4); vv['byteOffset'] = len(NB); vv['byteLength'] = len(data); NB += data
J['buffers'][0]['byteLength'] = len(NB)
write_glb(dst, J, NB)
J2, B2 = read_glb(dst)
for i, vv in enumerate(J2['bufferViews']):
    if i != bv: assert B2[vv['byteOffset']:vv['byteOffset'] + vv['byteLength']] == orig[i], f'view {i} changed'
print('WROTE', dst, '- only the base colour image changed;', len(J2['bufferViews']) - 1, 'other views verified identical')
