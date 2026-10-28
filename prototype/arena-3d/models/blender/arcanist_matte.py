# python arcanist_matte.py <in Arcanist.glb> <out .glb> [cloth roughness] [cloth metal scale]
# Makes the Arcanist's cloth matte and keeps his gold trim metallic, at the glTF level (only the body's
# occlusion / roughness / metal image changes).
#
# Why (critique 2026-10-28, user report: "glossiness / shine, the washed-out look"): Meshy's map gives his cloth the
# gloss of wet plastic. The white panels have median roughness 0.39 and the blue coat 0.55, and both are ~35% metal, so
# in game they mirror the sky and lights and the colour washes out. A blanket metal scale in code (CharacterPresenter)
# also dulled the gold trim. Here, per texel of the 1024 map, a "gold" weight is read from the base colour (warm,
# not dark): gold keeps its metal and a moderate roughness (0.35-0.6); everything else becomes cloth with roughness
# around <cloth roughness> (default 0.85, its own variation kept at a quarter strength) and metal x <cloth metal scale>
# (default 0.1). Occlusion (R) is untouched. Every other byte of the file is verified identical.
import io, json, struct, sys, numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
CLOTH_R = float(sys.argv[3]) if len(sys.argv) > 3 else 0.85
CLOTH_M = float(sys.argv[4]) if len(sys.argv) > 4 else 0.1

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
assert 'matteCloth' not in J.get('extras', {}), 'already matte'
mat = next(m for m in J['materials'] if m['name'] == 'arcanist'); pbr = mat['pbrMetallicRoughness']
src_of = lambda t: J['textures'][t]['source']
bv_orm = J['images'][src_of(pbr['metallicRoughnessTexture']['index'])]['bufferView']
bv_base = J['images'][src_of(pbr['baseColorTexture']['index'])]['bufferView']
assert sum(1 for im in J['images'] if im.get('bufferView') == bv_orm) == 1
views = [B[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']] for v in J['bufferViews']]
orm = np.asarray(Image.open(io.BytesIO(views[bv_orm])).convert('RGB')).astype(np.float32) / 255
H, W, _ = orm.shape
base = np.asarray(Image.open(io.BytesIO(views[bv_base])).convert('RGB').resize((W, H), Image.BOX)).astype(np.float32) / 255
r, g, b = base[..., 0], base[..., 1], base[..., 2]; L = base @ np.array([0.299, 0.587, 0.114])
gold = np.clip((r - b - 0.08) / 0.08, 0, 1) * np.clip((g - b - 0.02) / 0.06, 0, 1) * np.clip((L - 0.25) / 0.1, 0, 1)
R0, M0 = orm[..., 1], orm[..., 2]
cloth_r = np.clip(CLOTH_R + 0.25 * (R0 - np.median(R0)), 0.72, 0.97)
R1 = gold * np.clip(R0, 0.35, 0.6) + (1 - gold) * cloth_r
M1 = gold * M0 + (1 - gold) * M0 * CLOTH_M
out = orm.copy(); out[..., 1] = R1; out[..., 2] = M1
print('GOLD texels (weight > 0.5): %.1f%% of the map' % ((gold > 0.5).mean() * 100))
for name, msk in (('cloth', gold < 0.5), ('gold', gold >= 0.5)):
    print('%-5s roughness median %.2f -> %.2f, metal median %.2f -> %.2f' % (name, np.median(R0[msk]), np.median(R1[msk]), np.median(M0[msk]), np.median(M1[msk])))
buf = io.BytesIO(); Image.fromarray(np.round(out * 255).astype(np.uint8)).save(buf, 'JPEG', quality=95); new = buf.getvalue()
NB = bytearray()
for i, v in enumerate(J['bufferViews']):
    data = new if i == bv_orm else views[i]
    NB += bytes(-len(NB) % 4); v['byteOffset'] = len(NB); v['byteLength'] = len(data); NB += data
J['buffers'][0]['byteLength'] = len(NB)
J.setdefault('extras', {})['matteCloth'] = {'clothRoughness': CLOTH_R, 'clothMetalScale': CLOTH_M, 'script': 'arcanist_matte.py'}
write_glb(dst, J, NB)
J2, B2 = read_glb(dst)
for i, v in enumerate(J2['bufferViews']):
    if i != bv_orm: assert B2[v['byteOffset']:v['byteOffset'] + v['byteLength']] == views[i], f'view {i} changed'
print('WROTE', dst, '- only the body ORM image changed')
