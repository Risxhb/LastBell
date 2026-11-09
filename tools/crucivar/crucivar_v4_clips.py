# blender -b -P crucivar_v4_clips.py -- <Crucivar.glb in> <Crucivar.glb out> [<render dir>]
# Adds the v4 fight's body clips to the shipped Crucivar model (the Meshy sculpt on its rigid rig). The Meshy source and
# crucivar_prep.blend are no longer on this PC, so this works on the GLB itself: it imports it, keeps every existing clip
# (Idle, Walk, Diebreaker, PhaseShift, Vulnerable, Defeat, ...), bakes the new ones with the pose helpers in
# crucivar_anim.py and exports the GLB again with the same node names, so CrucivarRig and the encounter work unchanged.
# Visual only: the encounter stretches each clip to its telegraph (CharacterPresenter.PlayOnce), and nothing here
# touches timing, damage or hit areas. Each clip is authored at that telegraph length (ForgeRite / FoundryUprising /
# MoltenCrown constants, 4 Nov):
#   Intro 2.0          rises from the gantry crouch, beats the hammer once (the run's 120-tick intro)
#   EmberBrand 3.5     crucible up, flicks embers while the brands follow (to 2.5), tips as they land
#   FurnaceSpinCW/CCW  1.5 s brace while the jets show, then 4 s turning the way the jets sweep (5.5)
#   Backdraft 3.0      draws in (arms wide, leaning back) through the pull, heaves forward on the blast
#   CastingOrder 4.0   crucible raised, then four tips sweeping across, one per pour (2.0 / 2.5 / 3.0 / 3.5 s)
#   TurretCall 2.5     hammer up, slams the floor as the Phase 2 turrets finish rising
#   PourFlood 2.0      both arms high, brought down as the flood lands (only when a turret still stands)
#   MouldPress 5.0     hammer and crucible held overhead, brought down as the Mould Towers land
#   CrownRise 3.0      chest up, arms pushing outward while the rim turns to crown
#   Eruption 5.0       sinks low and shakes harder, rears up as the floor erupts
import bpy, sys, os, math
from mathutils import Vector, Matrix, Euler
args = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = args[0], args[1]
RENDER = args[2] if len(args) > 2 else None
HERE = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
sc = bpy.context.scene
arm = next(o for o in sc.objects if o.type == 'ARMATURE'); ad = arm.data
old = [a.name for a in bpy.data.actions]
print("OLD CLIPS", old, "FPS", sc.render.fps)
FPS = sc.render.fps   # the import keeps the old clips at the scene rate (24): bake the new ones at it too, so nothing changes speed

BASE_ARMS = {}          # the sculpt's rest pose is the idle stance
HELPERS_ONLY = True
exec(open(os.path.join(HERE, 'crucivar_anim.py'), encoding='utf-8').read())

# the imported bones' tails are glTF guesses, so the leg IK takes each bone's direction from its child's head
CHILD = {'thigh': 'shin', 'shin': 'foot'}
def plant(feet):
    bpy.context.view_layer.update()
    for s, (h0, k0, a0) in LEG.items():
        th, sh, ft = pbs['thigh.' + s], pbs['shin.' + s], pbs['foot.' + s]
        dx, dy, dz, pitch = feet.get(s, (0, 0, 0, 0))
        H = th.head.copy(); A = a0 + Vector((dx, dy, dz))
        L1, L2 = (k0 - h0).length, (a0 - k0).length
        d = min((A - H).length, L1 + L2 - 1e-4); u = (A - H).normalized()
        pole = Vector((0, -1, 0.15)); v = (pole - pole.project(u)).normalized()
        ca = max(-1, min(1, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)))
        K = H + L1 * (ca * u + math.sqrt(max(0, 1 - ca * ca)) * v)
        for pb, a, b, rest in ((th, H, K, k0 - h0), (sh, K, A, a0 - k0)):
            r0 = pb.bone.matrix_local.to_3x3()
            pb.matrix = Matrix.Translation(a) @ (rest.normalized().rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ (R(x=pitch) @ ft.bone.matrix_local.to_3x3()).to_4x4()
    bpy.context.view_layer.update()

def bake(name, seconds, fn):
    frames = max(2, round(seconds * FPS))
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for fr in range(frames + 1):
        p = fn(fr / FPS); apply(p); plant(p.get('_feet', {}))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=fr + 1, group=pb.name)
            pb.keyframe_insert('location', frame=fr + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

def shake(t, t0, t1, amp, f=23.0):
    return 0 if t < t0 or t > t1 else amp * sm((t - t0) / max(1e-6, t1 - t0)) * math.sin(f * t)

# ---------------- v4 clips ----------------
def intro(t):
    up = key(t, [(0, 0), (0.9, 1), (2.0, 1)])
    arm_r = key(t, [(0, 20), (0.9, -40), (1.35, -130), (1.6, -40), (2.0, 0)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.38 * (1 - up) + jolt(t, 1.6, -0.06, 14, 8))},
        torso={'rot': (24 * (1 - up) + key(t, [(1.2, 0), (1.35, -8), (1.6, 10), (2.0, 0)]), 0, 0)},
        upperarm__R={'rot': (arm_r, -8 * up, 0)}, forearm__R={'rot': (key(t, [(0, 0), (1.35, -30), (1.6, 6), (2.0, 0)]), 0, 0)},
        upperarm__L={'rot': (18 * (1 - up), -10 * up, 0)})
    p['_feet'] = crouch(0.0, 0.08 * up)
    return p

def ember_brand(t):
    lift = key(t, [(0, 0), (0.8, 1), (3.3, 1), (3.5, 0.7)])
    tip = key(t, [(0, 0), (3.05, 0), (3.4, 1), (3.5, 0.9)])
    flick = jolt(t, 1.0, 16, 12, 5) + jolt(t, 1.8, 14, 12, 5) + jolt(t, 2.5, 18, 12, 6)
    p = base(t, False)
    mix(p, torso={'rot': (-5 * lift, 0, 12 * lift + 0.3 * flick)},
        upperarm__L={'rot': (-70 * lift, -18 * lift, 0)}, forearm__L={'rot': (12 * lift, 0, 0)},
        crucible__L={'rot': (-30 * lift + flick + 100 * tip, 0, 0)},
        upperarm__R={'rot': (10 * lift, 10 * lift, 0)})
    return p

def furnace_spin(sign):
    def f(t):
        brace = key(t, [(0, 0), (0.6, 1), (5.2, 1), (5.5, 0)])
        turn = key(t, [(1.5, 0), (5.5, 1)]) * 70 * sign
        p = base(t, False)
        mix(p, pelvis={'loc': (0, 0, -0.16 * brace)}, torso={'rot': (8 * brace + shake(t, 1.5, 5.5, 1.5), 0, turn)},
            upperarm__R={'rot': (-25 * brace, 70 * brace, 0)}, upperarm__L={'rot': (-25 * brace, -70 * brace, 0)},
            forearm__R={'rot': (-15 * brace, 0, 0)}, forearm__L={'rot': (-15 * brace, 0, 0)})
        p['_feet'] = {'R': (-0.14 * brace, 0, 0, 0), 'L': (0.14 * brace, 0, 0, 0)}
        return p
    return f

def backdraft(t):
    draw = key(t, [(0, 0), (1.6, 0.8), (2.0, 1), (2.75, 1), (3.0, 0)])
    heave = key(t, [(2.75, 0), (3.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.10 * draw - 0.12 * heave)},
        torso={'rot': (-16 * draw + 22 * heave + shake(t, 1.6, 2.75, 1.2), 0, 0)},
        upperarm__R={'rot': (-40 * draw - 60 * heave, 60 * draw - 50 * heave, 0)},
        upperarm__L={'rot': (-40 * draw - 60 * heave, -60 * draw + 50 * heave, 0)},
        forearm__R={'rot': (-30 * draw, 0, 0)}, forearm__L={'rot': (-30 * draw, 0, 0)})
    p['_feet'] = {'R': (-0.10 * draw, 0.12 * heave, 0, 0), 'L': (0.10 * draw, -0.18 * heave, 0, 0)}
    return p

POURS = (2.0, 2.5, 3.0, 3.5)
def casting_order(t):
    lift = key(t, [(0, 0), (1.2, 1), (3.7, 1), (4.0, 0.6)])
    tip = sum(key(t, [(t0 - 0.25, 0), (t0, 1), (t0 + 0.2, 0.35)]) if t0 - 0.25 <= t <= t0 + 0.45 else 0 for t0 in POURS)
    sweep = key(t, [(0, -18), (1.7, -18), (3.6, 18), (4.0, 0)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.12 * lift)}, torso={'rot': (6 * lift, 0, sweep * lift)},
        upperarm__L={'rot': (-55 * lift, -10 * lift, 0)}, forearm__L={'rot': (-10 * lift, 0, 0)},
        crucible__L={'rot': (-20 * lift + 70 * tip, 0, 0)},
        upperarm__R={'rot': (-60 * lift, 8 * lift, 0)}, forearm__R={'rot': (-25 * lift, 0, 0)})
    p['_feet'] = {'R': (-0.08 * lift, 0, 0, 0), 'L': (0.10 * lift, 0.12 * lift, 0, 0)}
    return p

def turret_call(t):
    arm_r = key(t, [(0, 0), (1.6, -150), (1.95, -150), (2.2, -45), (2.5, -40)])
    drop = key(t, [(0, 0), (1.6, 0.04), (1.95, 0.04), (2.2, -0.24), (2.5, -0.2)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, drop)}, torso={'rot': (key(t, [(0, 0), (1.6, -10), (1.95, -10), (2.2, 24), (2.5, 20)]) + jolt(t, 2.2, 3, 16, 6), 0, 0)},
        upperarm__R={'rot': (arm_r + 6, -6, 0)}, forearm__R={'rot': (key(t, [(0, 0), (1.6, -25), (2.2, 10)]), 0, 0)},
        upperarm__L={'rot': (key(t, [(0, 0), (1.6, 10), (2.2, -14)]), -16, 0)})
    p['_feet'] = {'R': (0, -0.1 * sm(t / 2.2), 0, 0), 'L': (0, 0.1 * sm(t / 2.2), 0, 0)}
    return p

def pour_flood(t):
    up = key(t, [(0, 0), (1.3, 1), (1.7, 1), (2.0, 0.2)])
    slam = key(t, [(1.7, 0), (2.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, 0.05 * up - 0.22 * slam)}, torso={'rot': (-12 * up + 20 * slam, 0, 0)},
        upperarm__R={'rot': (-160 * up, 20 * up, 0)}, upperarm__L={'rot': (-160 * up, -20 * up, 0)},
        forearm__R={'rot': (-10 * up, 0, 0)}, crucible__L={'rot': (90 * slam, 0, 0)})
    return p

def mould_press(t):
    up = key(t, [(0, 0), (1.5, 1), (4.6, 1), (5.0, 0.15)])
    down = key(t, [(4.6, 0), (5.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, 0.04 * up - 0.26 * down)},
        torso={'rot': (-10 * up + 26 * down + shake(t, 2.0, 4.6, 1.0, 9), 0, 0)},
        upperarm__R={'rot': (-155 * up, -16 * up, 0)}, forearm__R={'rot': (-20 * up, 0, 0)},
        upperarm__L={'rot': (-140 * up, 16 * up, 0)}, crucible__L={'rot': (-40 * up, 0, 0)})
    p['_feet'] = {'R': (-0.10 * up, 0, 0, 0), 'L': (0.10 * up, 0, 0, 0)}
    return p

def crown_rise(t):
    k = key(t, [(0, 0), (1.0, 0.7), (2.8, 1), (3.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, 0.06 * k)}, torso={'rot': (-18 * k + shake(t, 1.0, 3.0, 1.5), 0, 0)},
        upperarm__R={'rot': (-70 * k, 75 * k, 0)}, upperarm__L={'rot': (-70 * k, -75 * k, 0)},
        forearm__R={'rot': (-10 * k, 0, 0)}, forearm__L={'rot': (-10 * k, 0, 0)})
    p['_feet'] = {'R': (-0.12 * k, 0, 0, 0), 'L': (0.12 * k, 0, 0, 0)}
    return p

def eruption(t):
    sink = key(t, [(0, 0), (1.5, 1), (4.7, 1), (5.0, 0)])
    rear = key(t, [(4.7, 0), (5.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.34 * sink + 0.08 * rear)},
        torso={'rot': (22 * sink - 22 * rear + shake(t, 1.0, 4.7, 4.0, 27), 0, shake(t, 2.0, 4.7, 2.0, 19))},
        upperarm__R={'rot': (-10 * sink - 110 * rear, 30 * sink + 40 * rear, 0)},
        upperarm__L={'rot': (-10 * sink - 110 * rear, -30 * sink - 40 * rear, 0)},
        forearm__R={'rot': (20 * sink, 0, 0)}, forearm__L={'rot': (20 * sink, 0, 0)})
    p['_feet'] = crouch(0.0, 0.10 * sink)
    return p

NEW = [('Intro', 2.0, intro), ('EmberBrand', 3.5, ember_brand),
       ('FurnaceSpinCW', 5.5, furnace_spin(-1)), ('FurnaceSpinCCW', 5.5, furnace_spin(1)),
       ('Backdraft', 3.0, backdraft), ('CastingOrder', 4.0, casting_order), ('TurretCall', 2.5, turret_call),
       ('PourFlood', 2.0, pour_flood), ('MouldPress', 5.0, mould_press), ('CrownRise', 3.0, crown_rise),
       ('Eruption', 5.0, eruption)]
for name, _, _ in NEW:
    if name in bpy.data.actions: bpy.data.actions.remove(bpy.data.actions[name])   # re-runs replace their own clips
acts = [bake(n, s, f) for n, s, f in NEW]
print('NEW CLIPS', [a.name for a in acts])
arm.animation_data.action = bpy.data.actions['Idle'] if 'Idle' in bpy.data.actions else acts[0]
apply(base(0)); plant({})

# export before the preview lights and camera exist, so only the model goes in
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_apply=True, export_image_format='JPEG', export_image_quality=90)
print('EXPORTED', OUT, os.path.getsize(OUT))

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 520, 560
    world = bpy.data.worlds.new('W'); sc.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.93, 0.89, 0.80, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.9
    sun = bpy.data.objects.new('Key', bpy.data.lights.new('Key', 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = Euler((math.radians(50), math.radians(-25), math.radians(-35)))
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 40
    a = math.radians(-30); e = math.radians(14); target = Vector((0, 0, 1.9))
    cam.location = target + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * 12.5
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    for name, secs, _ in NEW:
        arm.animation_data.action = bpy.data.actions[name]
        for k, u in enumerate((0.0, 0.5, 0.85, 1.0)):
            sc.frame_set(int(round(u * secs * FPS)) + 1)
            sc.render.filepath = os.path.join(RENDER, f'{name}_{k}.png'); bpy.ops.render.render(write_still=True)
        print('RENDER', name)
    arm.animation_data.action = bpy.data.actions['Idle']; sc.frame_set(1)

