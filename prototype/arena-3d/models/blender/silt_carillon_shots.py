# blender -b <rig .blend> -P silt_carillon_shots.py -- <out dir> <turn|clips> [Clip:t,t ...]
# Review renders for the Silt Carillon art pass (body or prop rig blends). EEVEE, a dusk Verdant-Chimes-like stage
# (dark moss floor, violet-blue sky, warm key). Writes single PNGs; silt_sheet.py labels and tiles them outside Blender.
#   turn   rest pose turnaround: front, front-left 3/4, left, back, back-right 3/4, high 3/4 (gameplay-like)
#   clips  for each Clip:t: a close 3/4 frame and a gameplay-camera frame (13 m up, 9.7 m behind a player 7 m out, 44 deg)
# Env: SHOT_SCALE (default 1; props use ~0.35 to frame a 0.9 m object), SHOT_SIZE (default 640).
import bpy, sys, os, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
out, mode, specs = os.path.abspath(args[0]), args[1], args[2:]
os.makedirs(out, exist_ok=True)
K = float(os.environ.get('SHOT_SCALE', '1')); SIZE = int(os.environ.get('SHOT_SIZE', '640'))
sc = bpy.context.scene
eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
sc.view_settings.view_transform = 'AgX' if 'AgX' in [v.identifier for v in bpy.types.ColorManagedViewSettings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
w = bpy.data.worlds.new('dusk'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.10, 0.11, 0.20, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
fm = bpy.data.materials.new('floor'); fm.use_nodes = True
fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.035, 0.07, 0.035, 1)
fm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
bpy.ops.mesh.primitive_circle_add(vertices=64, radius=14, fill_type='NGON'); floor = bpy.context.object; floor.data.materials.append(fm)
def light(name, kind, rot, energy, col):
    l = bpy.data.objects.new(name, bpy.data.lights.new(name, kind)); sc.collection.objects.link(l)
    l.rotation_euler = [math.radians(a) for a in rot]; l.data.energy = energy; l.data.color = col; return l
light('key', 'SUN', (50, 0, -35), 3.2, (1.0, 0.88, 0.72))
light('sky', 'SUN', (60, 0, 150), 1.4, (0.55, 0.6, 1.0))
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
arm = next((o for o in sc.objects if o.type == 'ARMATURE'), None)

def shoot(name, loc, target, fov=None, lens=None, w=SIZE, h=SIZE):
    cam.location = Vector(loc); cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cam.data.sensor_fit = 'VERTICAL'
    if fov: cam.data.angle_y = math.radians(fov)
    if lens: cam.data.lens = lens
    sc.render.resolution_x, sc.render.resolution_y = w, h
    f = os.path.join(out, name + '.png'); sc.render.filepath = f; bpy.ops.render.render(write_still=True); print('SHOT', f)

if mode == 'turn':
    if arm and arm.animation_data and 'Idle' in bpy.data.actions: arm.animation_data.action = bpy.data.actions['Idle']; sc.frame_set(1)
    T = (0, 0, 1.8 * K); d = 9.5 * K; z = 2.4 * K
    for i, (n, a) in enumerate([('front', 0), ('front_left34', 45), ('left', 90), ('back', 180), ('back_right34', 225)]):
        th = math.radians(a); shoot(f'turn{i}_{n}', (d * math.sin(th), -d * math.cos(th), z), T, lens=50)
    shoot('turn5_high34', (6.5 * K, -9 * K, 8 * K), (0, 0, 1.2 * K), lens=40)
else:
    for spec in specs:
        clip, ts = spec.split(':')
        if arm: arm.animation_data.action = bpy.data.actions[clip]
        for t in ts.split(','):
            sc.frame_set(1 + round(float(t) * 30))
            shoot(f'clip_{clip}_{float(t):.2f}_close', (5.6 * K, -7.6 * K, 3.4 * K), (0, 0, 1.9 * K), lens=42)
            # gameplay camera: player 7 m in front of the boss, camera 9.7 m behind and 13 m above the player, 44 deg
            shoot(f'clip_{clip}_{float(t):.2f}_game', (0, -16.7, 13), (0, -7, 0), fov=44, w=SIZE * 3 // 2, h=SIZE)
