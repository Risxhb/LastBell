# blender -b -P chimesinger_strandcheck.py -- <Chimesinger.glb> <report.txt>
# For every clip in the shipped Chimesinger model: do the bell strands stay attached? Edges of the body mesh that touch a
# strand bone (the strands and the cord link up to the sleeve) are measured on every frame against their rest length; a
# torn-off strand shows as an edge stretched far past rest. Also the gap from each strand's top to the sleeve cord.
# Bones: tassel.L/R and tassel2.L/R (chimesinger_rig.py). Stretch up to ~2x is cloth in the run clips; a tear is far past that.
import bpy, sys, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.find_armature() == arm]
bones = [b.name for b in arm.data.bones]
strand_bones = [b for b in bones if b.startswith(('tassel.', 'tassel2.'))]
lines = ['strand bones: ' + ', '.join(strand_bones)]
dg = bpy.context.evaluated_depsgraph_get()
checks = []
for o in meshes:
    gidx = {g.index: g.name for g in o.vertex_groups}
    sb = {i for i, n in gidx.items() if n in strand_bones}
    if not sb: continue
    w = np.zeros(len(o.data.vertices))
    for v in o.data.vertices:
        w[v.index] = sum(g.weight for g in v.groups if g.group in sb)
    edges = np.array([e.vertices[:] for e in o.data.edges])
    sel = edges[(w[edges[:, 0]] > 0.05) | (w[edges[:, 1]] > 0.05)]
    checks.append((o, sel))
    lines.append(f'{o.name}: {int((w > 0.05).sum())} strand verts, {len(sel)} edges checked')
if not checks: lines.append('NO STRAND WEIGHTS FOUND')
def coords(o):
    e = o.evaluated_get(dg); m = e.to_mesh()
    c = np.array([v.co[:] for v in m.vertices]); e.to_mesh_clear(); return c
arm.animation_data_create()
rest = {}
arm.animation_data.action = None
bpy.context.view_layer.update(); dg.update()
for o, sel in checks:
    c = coords(o); rest[o.name] = np.linalg.norm(c[sel[:, 0]] - c[sel[:, 1]], axis=1) + 1e-6
lines.append('clip                 frames  max stretch  edges >1.5x  (1.0 = rest length)')
for act in bpy.data.actions:
    arm.animation_data.action = act
    f0, f1 = (int(x) for x in act.frame_range)
    worst, bad, wf = 0.0, 0, f0
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f); dg.update()
        for o, sel in checks:
            c = coords(o); L = np.linalg.norm(c[sel[:, 0]] - c[sel[:, 1]], axis=1)
            r = L / rest[o.name]; m = float(r.max())
            if m > worst: worst, wf = m, f
            bad = max(bad, int((r > 1.5).sum()))
    lines.append(f'{act.name:20s} {f1 - f0 + 1:6d}  {worst:6.2f} (f{wf:3d})  {bad:6d}')
open(out, 'w').write('\n'.join(lines) + '\n'); print('\n'.join(lines))
