# Fast pose check for the miniboss rig scripts (9 Nov 2026): reuses a saved <slug>_rig.blend (already weighted), so pose
# functions can be tried in seconds instead of re-running the whole rig (heat weights take minutes).
#   blender -b <work>/<slug>_rig.blend -P rig_poke.py -- <rig_script.py> <out dir> <Clip>@<sec> [<Clip>@<sec> ...] [--far]
# Renders pose_poke_<Clip>_<sec>.png (close camera; --far: the gameplay camera too) and prints, per shot, the world
# position of every bone tail named in PROBE (e.g. the hand bones that run into props).
import bpy, sys, os, math, re
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from miniboss_rig_lib import *
args = sys.argv[sys.argv.index('--') + 1:]
script, out = args[0], os.path.abspath(args[1]); far = '--far' in args
shots = [(a.split('@')[0], float(a.split('@')[1])) for a in args[2:] if '@' in a]
src = open(script, encoding='utf-8').read()
top = src[:src.index('rig = Rig(')]; top = top[top.index('args = sys.argv'):]
top = top.replace("work, out_glb = os.path.abspath(args[0]), os.path.abspath(args[1])", "")
body = src[src.index('REST = {n: tuple(rig.rest_dir[n]) for n in B}'):src.index("rig.bake('Idle'")]
pre = src[src.index('rig = Rig('):src.index('REST = {n: tuple(rig.rest_dir[n]) for n in B}')]
helpers = '\n'.join(re.findall(r'(?ms)^def \w+\(.*?(?=^\S)', pre))   # helper defs between the rig and the poses (e.g. drop_islands)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
class Shim:
    pass
rig = Shim()
ns = {'__name__': 'poke', 'args': []}
exec('import bpy, sys, os, math\nfrom miniboss_rig_lib import *\nimport bmesh, numpy as np\n' + top, ns)
rig.B = ns['B']; rig.aim_order = tuple(ns.get('AIM', ())); rig.pbs = arm.pose.bones; rig.arm = arm
rig.rest_dir = {n: (Vector(rig.B[n][1]) - Vector(rig.B[n][0])).normalized() for n in rig.B}
rig.sc = bpy.context.scene
for m in ('aim', 'apply', 'ik2'): setattr(rig, m, getattr(Rig, m).__get__(rig))
ns['rig'] = rig
ns['drop_islands'] = lambda *a, **k: None
body = re.sub(r'(?m)^(drop_islands\(|bm = bmesh|bmesh\.ops|print\(.RIBBON|TAIL =)', r'pass  # \1', body)
exec(helpers.replace('def drop_islands', 'def _unused_drop_islands'), ns)
exec(body, ns)
fns = {n.lower(): f for n, f in ns.items() if callable(f)}
def fn_for(clip):
    snake = re.sub(r'(?<!^)(?=[A-Z])', '_', clip).lower()
    return ns.get(snake) or ns.get(clip.lower())
sc = bpy.context.scene; arm.animation_data.action = None
eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 520
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.75, 0.78, 1)
bpy.ops.mesh.primitive_plane_add(size=30)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(35), 0, math.radians(-30)); sun.data.energy = 3.5
cams = [('close', ns.get('POKE_CAM', (-4.4, -7.2, 3.4)), ns.get('POKE_TARGET', (0, -0.2, 1.5)))]
if far: cams.append(('far', (-5.0, -13.5, 11.0), (0, 0, 1.2)))
probe = ns.get('PROBE', [])
for clip, t in shots:
    f = fn_for(clip)
    if f is None: print('NO_FN', clip); continue
    rig.apply(f(t)); bpy.context.view_layer.update()
    for b in probe:
        pb = arm.pose.bones[b]; print('PROBE', clip, t, b, 'head', tuple(round(c, 2) for c in arm.matrix_world @ pb.head), 'tail', tuple(round(c, 2) for c in arm.matrix_world @ pb.tail))
    for tag, pos, tgt in cams:
        cam.location = pos; cam.rotation_euler = (Vector(tgt) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f'poke_{tag}_{clip}_{t:.2f}.png'); bpy.ops.render.render(write_still=True)
print('POKE_DONE')
