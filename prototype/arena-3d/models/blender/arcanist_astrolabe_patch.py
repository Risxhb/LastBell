# python arcanist_astrolabe_patch.py <in Arcanist.glb> <astrolabe_new.glb> <out .glb> [backup dir]
# Installs the rebuilt astrolabe (arcanist_astrolabe_build.py) into the shipped Arcanist GLB at the glTF level, and
# removes the old one (remodel plan step 3, build-evidence/arcanist-orb-20261028/remodel-plan.md):
#   - the old astrolabe's mesh, material, textures, images, accessors and bufferViews are dropped (compaction); every
#     remaining bufferView is copied byte for byte, in its original order, and all indices are remapped;
#   - the new parts are appended: their nodes become children of node 'Astrolabe' (which keeps its name and transform
#     and loses its mesh, so the game still finds it), with their meshes, materials, textures, images and data;
#   - node indices never change (only appended), so the skin, the joints and all 23 clips' targets are untouched.
# Checks before writing: every kept node (all but 'Astrolabe') is identical; the skin, every animation channel and the
# body's mesh, material and images are identical after following the remapped indices down to their bytes. Refuses to
# run on a file that already has the new astrolabe.
import json, struct, sys, os, shutil, hashlib

src, new, dst = sys.argv[1], sys.argv[2], sys.argv[3]
backup = sys.argv[4] if len(sys.argv) > 4 else None

def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d), p
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    J = json.loads(d[20:20 + jl])
    bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, d[28 + jl:28 + jl + bl]
def view_bytes(J, B, i):
    v = J['bufferViews'][i]; o = v.get('byteOffset', 0); return B[o:o + v['byteLength']]
def acc_bytes(J, B, i):
    a = J['accessors'][i]; return (json.dumps({k: a[k] for k in a if k != 'bufferView'}, sort_keys=True).encode(), view_bytes(J, B, a['bufferView']) if 'bufferView' in a else b'')

J, B = read_glb(src); N, NB = read_glb(new)
names = [n.get('name') for n in J['nodes']]
if any(str(x).startswith('Astro_') for x in names): sys.exit('refusing: the input already has the new astrolabe')
A = names.index('Astrolabe'); old_mesh = J['nodes'][A]['mesh']

# ---------------------------------------------------------------- what belongs only to the old astrolabe
def mesh_refs(m):
    acc, mats = set(), set()
    for p in m['primitives']:
        acc |= set(p['attributes'].values())
        if 'indices' in p: acc.add(p['indices'])
        for t in p.get('targets', []): acc |= set(t.values())
        if 'material' in p: mats.add(p['material'])
    return acc, mats
def mat_textures(m):
    t = set(); pbr = m.get('pbrMetallicRoughness', {})
    for k in ('baseColorTexture', 'metallicRoughnessTexture'):
        if k in pbr: t.add(pbr[k]['index'])
    for k in ('normalTexture', 'occlusionTexture', 'emissiveTexture'):
        if k in m: t.add(m[k]['index'])
    return t
old_acc, old_mats = mesh_refs(J['meshes'][old_mesh])
keep_meshes = [i for i in range(len(J['meshes'])) if i != old_mesh]
keep_acc_used, keep_mats_used = set(), set()
for i in keep_meshes:
    a, m = mesh_refs(J['meshes'][i]); keep_acc_used |= a; keep_mats_used |= m
for s in J.get('skins', []):
    if 'inverseBindMatrices' in s: keep_acc_used.add(s['inverseBindMatrices'])
for an in J.get('animations', []):
    for sm in an['samplers']: keep_acc_used |= {sm['input'], sm['output']}
assert not (old_acc & keep_acc_used), 'old astrolabe shares accessors with the body'
assert not (old_mats & keep_mats_used), 'old astrolabe shares a material with the body'
old_tex = set().union(*[mat_textures(J['materials'][m]) for m in old_mats])
keep_tex = set().union(*[mat_textures(J['materials'][m]) for m in keep_mats_used]) if keep_mats_used else set()
assert not (old_tex & keep_tex)
old_img = {J['textures'][t]['source'] for t in old_tex}
keep_img = {J['textures'][t]['source'] for t in keep_tex}
assert not (old_img & keep_img)
old_views = {J['accessors'][a]['bufferView'] for a in old_acc if 'bufferView' in J['accessors'][a]} | {J['images'][i]['bufferView'] for i in old_img}
keep_views_used = {J['accessors'][a]['bufferView'] for a in keep_acc_used if 'bufferView' in J['accessors'][a]} | {J['images'][i]['bufferView'] for i in keep_img}
assert not (old_views & keep_views_used)

# ---------------------------------------------------------------- compact: drop the old data, remap indices
def remap(n, drop):
    m, k = {}, 0
    for i in range(n):
        if i not in drop: m[i] = k; k += 1
    return m
mM = remap(len(J['meshes']), {old_mesh}); mMat = remap(len(J['materials']), old_mats); mTex = remap(len(J['textures']), old_tex)
mImg = remap(len(J['images']), old_img); mAcc = remap(len(J['accessors']), old_acc); mView = remap(len(J['bufferViews']), old_views)
out = json.loads(json.dumps(J))
# (filter the deep copy, never J: J stays untouched for the checks below)
out['meshes'] = [m for i, m in enumerate(out['meshes']) if i in mM]; out['materials'] = [m for i, m in enumerate(out['materials']) if i in mMat]
out['textures'] = [t for i, t in enumerate(out['textures']) if i in mTex]; out['images'] = [m for i, m in enumerate(out['images']) if i in mImg]
out['accessors'] = [a for i, a in enumerate(out['accessors']) if i in mAcc]
# rebuild the binary chunk: kept views in their original order, 4-byte aligned
blob = bytearray(); views = []
for i, v in enumerate(J['bufferViews']):
    if i not in mView: continue
    while len(blob) % 4: blob += b'\0'
    nv = dict(v); nv['byteOffset'] = len(blob); nv['buffer'] = 0; blob += view_bytes(J, B, i); views.append(nv)
out['bufferViews'] = views
del out['nodes'][A]['mesh']   # the old astrolabe's mesh goes; the node stays as the pivot
for n in out['nodes']:
    if 'mesh' in n: n['mesh'] = mM[n['mesh']]
def fix_prim(p, mA, mMt):
    p['attributes'] = {k: mA[v] for k, v in p['attributes'].items()}
    if 'indices' in p: p['indices'] = mA[p['indices']]
    if 'targets' in p: p['targets'] = [{k: mA[v] for k, v in t.items()} for t in p['targets']]
    if 'material' in p: p['material'] = mMt[p['material']]
for m in out['meshes']:
    for p in m['primitives']: fix_prim(p, mAcc, mMat)
def fix_mat(m, mT):
    pbr = m.get('pbrMetallicRoughness', {})
    for k in ('baseColorTexture', 'metallicRoughnessTexture'):
        if k in pbr: pbr[k]['index'] = mT[pbr[k]['index']]
    for k in ('normalTexture', 'occlusionTexture', 'emissiveTexture'):
        if k in m: m[k]['index'] = mT[m[k]['index']]
for m in out['materials']: fix_mat(m, mTex)
for t in out['textures']: t['source'] = mImg[t['source']]
for im in out['images']: im['bufferView'] = mView[im['bufferView']]
for a in out['accessors']:
    if 'bufferView' in a: a['bufferView'] = mView[a['bufferView']]
for s in out.get('skins', []):
    if 'inverseBindMatrices' in s: s['inverseBindMatrices'] = mAcc[s['inverseBindMatrices']]
for an in out.get('animations', []):
    for sm in an['samplers']: sm['input'] = mAcc[sm['input']]; sm['output'] = mAcc[sm['output']]

# ---------------------------------------------------------------- append the new astrolabe
root = N['scenes'][N.get('scene', 0)]['nodes'][0]; assert N['nodes'][root].get('name') == 'Astrolabe_new'
off = {k: len(out.get(k, [])) for k in ('nodes', 'meshes', 'materials', 'textures', 'images', 'accessors', 'bufferViews', 'samplers')}
new_nodes = [i for i in range(len(N['nodes'])) if i != root]
nN = {i: off['nodes'] + k for k, i in enumerate(new_nodes)}
samp = {}
for i, s in enumerate(N.get('samplers', [])):
    same = [j for j, t in enumerate(out.get('samplers', [])) if t == s]
    if same: samp[i] = same[0]
    else: out.setdefault('samplers', []).append(s); samp[i] = len(out['samplers']) - 1
for i in new_nodes:
    n = json.loads(json.dumps(N['nodes'][i]))
    if 'children' in n: n['children'] = [nN[c] for c in n['children']]
    if 'mesh' in n: n['mesh'] += off['meshes']
    out['nodes'].append(n)
out['nodes'][A]['children'] = [nN[c] for c in N['nodes'][root].get('children', [])]
for v_i, v in enumerate(N['bufferViews']):
    while len(blob) % 4: blob += b'\0'
    nv = dict(v); nv['byteOffset'] = len(blob); nv['buffer'] = 0; blob += view_bytes(N, NB, v_i); out['bufferViews'].append(nv)
for a in N['accessors']:
    a = dict(a)
    if 'bufferView' in a: a['bufferView'] += off['bufferViews']
    out['accessors'].append(a)
for im in N['images']:
    im = dict(im); im['bufferView'] += off['bufferViews']; out['images'].append(im)
for t in N['textures']:
    t = dict(t); t['source'] += off['images']
    if 'sampler' in t: t['sampler'] = samp[t['sampler']]
    out['textures'].append(t)
for m in N['materials']:
    m = json.loads(json.dumps(m)); fix_mat(m, {i: i + off['textures'] for i in range(len(N['textures']))}); out['materials'].append(m)
for m in N['meshes']:
    m = json.loads(json.dumps(m))
    for p in m['primitives']: fix_prim(p, {i: i + off['accessors'] for i in range(len(N['accessors']))}, {i: i + off['materials'] for i in range(len(N['materials']))})
    out['meshes'].append(m)
for e in N.get('extensionsUsed', []):
    if e not in out.setdefault('extensionsUsed', []): out['extensionsUsed'].append(e)
while len(blob) % 4: blob += b'\0'
out['buffers'] = [{'byteLength': len(blob)}]

# ---------------------------------------------------------------- checks
O, OB = out, bytes(blob)
for i in range(len(J['nodes'])):
    if i == A: continue
    a, b = dict(J['nodes'][i]), dict(O['nodes'][i])
    if 'mesh' in a: a['mesh'] = mM[a['mesh']]
    assert a == b, f'node {i} ({names[i]}) changed'
a, b = dict(J['nodes'][A]), dict(O['nodes'][A])
for k in ('name', 'translation', 'rotation', 'scale'): assert a.get(k) == b.get(k), f'Astrolabe {k} changed'
for si, s in enumerate(J.get('skins', [])):
    t = O['skins'][si]; assert s['joints'] == t['joints'] and s.get('skeleton') == t.get('skeleton')
    if 'inverseBindMatrices' in s: assert acc_bytes(J, B, s['inverseBindMatrices']) == acc_bytes(O, OB, t['inverseBindMatrices'])
nch = 0
for ai, an in enumerate(J.get('animations', [])):
    bn = O['animations'][ai]; assert an['channels'] == bn['channels'] and an.get('name') == bn.get('name')
    for si, sm in enumerate(an['samplers']):
        tm = bn['samplers'][si]; assert sm.get('interpolation') == tm.get('interpolation')
        assert acc_bytes(J, B, sm['input']) == acc_bytes(O, OB, tm['input']) and acc_bytes(J, B, sm['output']) == acc_bytes(O, OB, tm['output'])
    nch += len(an['channels'])
for mi in keep_meshes:
    ma, mb = J['meshes'][mi], O['meshes'][mM[mi]]
    for pa, pb in zip(ma['primitives'], mb['primitives']):
        for k in pa['attributes']: assert acc_bytes(J, B, pa['attributes'][k]) == acc_bytes(O, OB, pb['attributes'][k]), f'mesh {mi} {k}'
        if 'indices' in pa: assert acc_bytes(J, B, pa['indices']) == acc_bytes(O, OB, pb['indices'])
        ma_, mb_ = J['materials'][pa['material']], O['materials'][pb['material']]
        assert ma_['name'] == mb_['name']
        ta, tb = sorted(mat_textures(ma_)), sorted(mat_textures(mb_))
        for x, y in zip(ta, tb):
            assert view_bytes(J, B, J['images'][J['textures'][x]['source']]['bufferView']) == view_bytes(O, OB, O['images'][O['textures'][y]['source']]['bufferView'])
print(f'CHECK ok: {len(J["nodes"]) - 1} nodes identical, skin and {nch} animation channels identical, body mesh/material/images identical')
print(f'REMOVED old astrolabe: mesh {old_mesh}, {len(old_mats)} material, {len(old_tex)} textures, {len(old_img)} images, {len(old_acc)} accessors, {len(old_views)} bufferViews')
print(f'ADDED {len(new_nodes)} nodes, {len(N["meshes"])} meshes, {len(N["materials"])} materials, {len(N["images"])} images')

# ---------------------------------------------------------------- write
js = json.dumps(out, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
glb = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(OB)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(OB), 0x004E4942) + OB
if backup:
    os.makedirs(backup, exist_ok=True); shutil.copy2(src, os.path.join(backup, 'Arcanist.before-astrolabe.glb'))
open(dst, 'wb').write(glb)
print('WROTE', dst, len(glb), 'bytes (was', os.path.getsize(src), ') sha1', hashlib.sha1(glb).hexdigest()[:12])
