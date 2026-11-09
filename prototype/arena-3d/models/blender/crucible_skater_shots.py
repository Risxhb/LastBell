# blender -b <crucible_skater_rig.blend> -P crucible_skater_shots.py -- <out dir> [ladle.glb]
# Critic frames for the Crucible Skater: a rest turnaround (front, 3/4, side, back) with the 0.9 m BodyRadius ring and a
# 1.8 m hero capsule for scale, then each clip's tell/strike frame from the gameplay camera (11 m up, 14 m back, 40 deg
# vertical FOV, 1280x720, as the autotests' RenderShot) and a closer camera. Contact sheets: crucible_skater_sheets.py.
import bpy, sys, os, math
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:]
out = os.path.abspath(args[0]); os.makedirs(out, exist_ok=True)
ladle = args[1] if len(args) > 1 else None
sc = bpy.context.scene
arm = next(o for o in sc.objects if o.type == 'ARMATURE')
eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
bg = sc.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.20, 0.17, 0.15, 1); bg.inputs[1].default_value = 0.9
sc.view_settings.view_transform = 'Standard'

def mat(n, col, metal=0.0, rough=0.6, emit=0.0):
    m = bpy.data.materials.new(n); m.use_nodes = True; p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*col, 1); p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if emit: p.inputs['Emission Color'].default_value = (*col, 1); p.inputs['Emission Strength'].default_value = emit
    return m
bpy.ops.mesh.primitive_plane_add(size=60); floor = bpy.context.object; floor.data.materials.append(mat('floor', (0.11, 0.085, 0.07), 0.35, 0.55))
bpy.ops.mesh.primitive_torus_add(major_radius=0.9, minor_radius=0.012, location=(0, 0, 0.01)); ringo = bpy.context.object
ringo.data.materials.append(mat('ring', (0.6, 0.6, 0.65), 0, 0.5, 0.6))
bpy.ops.mesh.primitive_cylinder_add(radius=0.28, depth=1.24, location=(1.7, 0.6, 0.9)); hero = bpy.context.object
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.28, location=(1.7, 0.6, 1.52)); hero2 = bpy.context.object
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.28, location=(1.7, 0.6, 0.28)); hero3 = bpy.context.object
hm = mat('hero', (0.45, 0.48, 0.55), 0, 0.7)
for h in (hero, hero2, hero3): h.data.materials.append(hm)
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), 0, math.radians(-35)); sun.data.energy = 3.2
fill = bpy.data.objects.new('fill', bpy.data.lights.new('fill', 'SUN')); sc.collection.objects.link(fill)
fill.rotation_euler = (math.radians(65), 0, math.radians(150)); fill.data.energy = 0.8; fill.data.color = (1.0, 0.75, 0.5)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.sensor_fit = 'VERTICAL'

def shoot(path, pos, look, fov, w, h):
    sc.render.resolution_x, sc.render.resolution_y = w, h
    cam.data.angle = math.radians(fov); cam.location = pos
    cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
def at(name, sec):
    arm.animation_data.action = bpy.data.actions[name]; sc.frame_set(1 + round(sec * 30))
def orbit(r, h, deg):
    a = math.radians(deg); return (r * math.sin(a), -r * math.cos(a), h)

# turnaround
at('Idle', 0)
for tag, deg in (('front', 0), ('front34', 35), ('side', 90), ('back', 180)):
    shoot(os.path.join(out, f'turn_{tag}.png'), orbit(4.2, 1.15, deg), (0, 0, 0.88), 32, 720, 900)
ringo.hide_render = True
for h in (hero, hero2, hero3): h.location.x = 1.9
# clip frames: (clip, second) at the tell / strike frame
SHOTS = [('Idle', 0.0), ('Walk', 0.25), ('CarveTrack', 0.3), ('CarveCrouch', 0.9), ('CarveLoop', 0.12), ('CarveTurn', 0.08),
         ('CheckWindup', 0.8), ('CheckLoop', 0.2), ('ChargeLoop', 0.1), ('Trip', 0.15), ('Trip', 0.6), ('StuckLoop', 0.5),
         ('TossRaise', 0.6), ('TossLoop', 0.25), ('TossFlick', 0.22), ('LapLoopL', 0.2), ('LapLoopR', 0.2), ('PirouetteLoop', 0.0),
         ('DizzyLoop', 0.3), ('Reheat', 1.0), ('Defeat', 1.2), ('Defeat', 2.5)]
only = os.environ.get('SKATER_SHOTS')
for name, sec in SHOTS:
    if only and name not in only.split(','): continue
    at(name, sec)
    shoot(os.path.join(out, f'game_{name}_{sec:.2f}.png'), orbit(14 / 1.0, 11, 25), (0, 0, 1.4), 40, 1280, 720)
    shoot(os.path.join(out, f'close_{name}_{sec:.2f}.png'), orbit(4.6, 1.7, 25), (0, 0, 0.85), 40, 800, 800)
if ladle:
    for o in list(sc.objects):
        if o.type in ('MESH', 'ARMATURE') and o not in (floor,): o.hide_render = True
    bpy.ops.import_scene.gltf(filepath=ladle)
    lo = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    for deg in (0, 90):
        for o in lo: o.rotation_mode = 'XYZ'; o.location = (0, 0, 0.45); o.rotation_euler = (0, 0, math.radians(deg))
        shoot(os.path.join(out, f'ladle_close_{deg}.png'), orbit(2.0, 1.3, 30), (0, 0, 0.4), 32, 700, 700)
    for o in lo: o.location = (0, 0, 4.0); o.rotation_euler = (math.radians(20), 0, 0)
    bpy.ops.mesh.primitive_torus_add(major_radius=2.4, minor_radius=0.03, location=(0, 0, 0.02)); pr = bpy.context.object
    pr.data.materials.append(mat('pour', (1.6, 1.35, 0.7), 0, 0.5, 1.0))
    shoot(os.path.join(out, 'ladle_game.png'), orbit(14, 11, 0), (0, 0, 1.4), 40, 1280, 720)
print('SHOTS_DONE', out)
