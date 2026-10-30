# python arcanist_doublesided.py <in Arcanist.glb> <out .glb> [material ...]
# Marks the Arcanist's body material double-sided (glTF JSON only; the binary data is untouched).
#
# Why: the Meshy robe is single-sheet cloth in ~1,860 loose fragments, and many fragments face inward. Seen from behind,
# 168 triangles (about 4% of the visible back) face away from the camera; Unity culls them, so they show as dark holes
# and notches (critique 2026-10-28, user report: a dark notch on his right white panel). Blender draws both sides,
# which is why offline renders never showed them. glTFast renders a doubleSided material with culling off and flips
# the normal on back faces, so those fragments shade like the rest of the cloth.
import json, struct, sys
src, dst = sys.argv[1], sys.argv[2]; names = sys.argv[3:] or ['arcanist']
d = open(src, 'rb').read()
magic, ver, total = struct.unpack('<III', d[:12]); assert magic == 0x46546C67 and ver == 2 and total == len(d)
jl, jt = struct.unpack('<II', d[12:20]); assert jt == 0x4E4F534A
J = json.loads(d[20:20 + jl]); rest = d[20 + jl:]
for m in J['materials']:
    if m['name'] in names: m['doubleSided'] = True; print('DOUBLE-SIDED', m['name'])
js = json.dumps(J, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
open(dst, 'wb').write(struct.pack('<III', 0x46546C67, 2, 20 + len(js) + len(rest)) + struct.pack('<II', len(js), 0x4E4F534A) + js + rest)
print('WROTE', dst, '- binary chunk byte-identical')
