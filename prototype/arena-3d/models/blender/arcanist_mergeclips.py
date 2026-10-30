# python arcanist_mergeclips.py <original Arcanist.glb> <clips.json> <out.glb>
# Appends the clips baked by arcanist_addclips.py to the shipped Arcanist GLB without touching anything else: the JSON
# chunk is the original JSON (it round-trips byte-exactly through json.dumps(separators=(',', ':'))) with new
# accessors / bufferViews / animations appended, the BIN chunk is the original bytes with the new data appended
# (4-byte aligned). Channels target the original joint nodes by name, every joint gets translation / rotation / scale
# like the exporter's clips; rotations are LINEAR per frame, constant channels are 2-key STEP (as the exporter writes
# them). Always merge into the original (pre-merge) GLB: clips whose names already exist are refused.
# Keys start at t = 0 (the exporter's clips start at 1/30), so normalised clip time equals the authored t.
import json, struct, sys, numpy as np
ORIGINAL = {'AnchorReturn', 'ArcLance', 'Clockbolt', 'Clockbolt2', 'Convergence', 'Dodge', 'Idle', 'Jump', 'Run', 'Sprint',
            'TemporalAnchor', 'TimeSeal'}
def read_glb(p):
    d = open(p, 'rb').read()
    magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
    jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
    raw = d[20:20 + jl]; J = json.loads(raw)
    assert json.dumps(J, separators=(',', ':')).encode() == raw.rstrip(b' '), 'JSON does not round-trip'
    bl, bt = struct.unpack('<II', d[20 + jl:28 + jl]); assert bt == 0x004E4942
    return J, bytearray(d[28 + jl:28 + jl + bl])
def write_glb(p, J, B):
    js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    B = bytes(B) + b'\0' * (-len(B) % 4)
    out = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(B)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(B), 0x004E4942) + B
    open(p, 'wb').write(out)
def merge(src, clips_path, dst):
    J, B = read_glb(src); clips = json.load(open(clips_path))
    names = {a['name'] for a in J['animations']}
    if names != ORIGINAL: raise SystemExit(f'not the original clip set: {sorted(names - ORIGINAL)}')
    nid = {n['name']: i for i, n in enumerate(J['nodes'])}
    joints = [J['nodes'][j]['name'] for j in J['skins'][0]['joints']]
    def add(arr, typ, minmax=False):
        arr = np.ascontiguousarray(arr, np.float32)
        while len(B) % 4: B.append(0)
        off = len(B); B.extend(arr.tobytes())
        J['bufferViews'].append({'buffer': 0, 'byteLength': arr.nbytes, 'byteOffset': off})
        a = {'bufferView': len(J['bufferViews']) - 1, 'componentType': 5126, 'count': int(arr.shape[0]), 'type': typ}
        if minmax: a['min'] = [float(arr.min())]; a['max'] = [float(arr.max())]
        J['accessors'].append(a); return len(J['accessors']) - 1
    for name, c in clips.items():
        times = np.array(c['times'], np.float32); assert np.all(np.diff(times) > 0)
        t_all = add(times, 'SCALAR', True); t_two = add(times[[0, -1]], 'SCALAR', True)
        chans, samps = [], []
        def channel(node, path, vals):
            vals = np.array(vals, np.float64)
            if np.abs(vals - vals[0]).max() < 1e-7:
                inp, out, interp = t_two, add(np.array([vals[0], vals[0]]), 'VEC3' if vals.shape[1] == 3 else 'VEC4'), 'STEP'
            else:
                inp, out, interp = t_all, add(vals, 'VEC3' if vals.shape[1] == 3 else 'VEC4'), 'LINEAR'
            samps.append({'input': inp, 'interpolation': interp, 'output': out})
            chans.append({'sampler': len(samps) - 1, 'target': {'node': nid[node], 'path': path}})
        for n in joints:
            channel(n, 'translation', c['translation'][n])
            q = np.array(c['rotation'][n], np.float64); q /= np.linalg.norm(q, axis=1, keepdims=True)
            channel(n, 'rotation', q)
            channel(n, 'scale', np.ones((len(times), 3)))
        J['animations'].append({'channels': chans, 'name': name, 'samplers': samps})
    J['buffers'][0]['byteLength'] = len(B) + (-len(B) % 4)
    write_glb(dst, J, B)
if __name__ == '__main__':
    merge(*sys.argv[1:4])
    print('MERGED', sys.argv[3])
