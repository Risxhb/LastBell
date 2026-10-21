# Shared Crucivar body clips and preview renders. Loaded with exec() after the model is parented to its bones; needs
# the caller's globals arm, ad, sc, final and the MOLTEN / EMBER / AMBER materials.
# ---------------- animation ----------------
pbs = arm.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'
def R(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_matrix()
LEG = {}
for s in 'RL':
    LEG[s] = (ad.bones['thigh.' + s].head_local.copy(), ad.bones['shin.' + s].head_local.copy(), ad.bones['foot.' + s].head_local.copy())

def apply(pose):
    for pb in pbs: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    for n, v in pose.items():
        if n.startswith('_'): continue
        pb = pbs[n]; M = pb.bone.matrix_local.to_3x3()
        rot = v.get('rot', (0, 0, 0)); pb.rotation_quaternion = (M.inverted() @ R(*rot) @ M).to_quaternion()
        if 'loc' in v: pb.location = M.inverted() @ Vector(v['loc'])

def plant(feet):
    # two-bone IK in armature space: each ankle goes to its rest point plus an offset, the knee bends forward
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
        for pb, a, b in ((th, H, K), (sh, K, A)):
            r0 = pb.bone.matrix_local.to_3x3(); rest = (pb.bone.tail_local - pb.bone.head_local).normalized()
            pb.matrix = Matrix.Translation(a) @ (rest.rotation_difference(b - a).to_matrix() @ r0).to_4x4()
            bpy.context.view_layer.update()
        ft.matrix = Matrix.Translation(A) @ (R(x=pitch) @ ft.bone.matrix_local.to_3x3()).to_4x4()
    bpy.context.view_layer.update()

def sm(a): a = min(1, max(0, a)); return a * a * (3 - 2 * a)
def key(t, pts):
    # piecewise smooth interpolation through (time, value) pairs; values may be tuples
    if t <= pts[0][0]: return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            u = sm((t - t0) / max(1e-6, t1 - t0))
            if isinstance(v0, tuple): return tuple(a + (b - a) * u for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * u
    return pts[-1][1]
def jolt(t, t0, amp, f=9.0, damp=7.0):
    return 0 if t < t0 else amp * math.exp(-damp * (t - t0)) * math.sin(f * (t - t0))

# base stance: crucible held at the waist in front, the hammer hanging heavy
# arm offsets from the rest pose to the idle stance; a model whose rest pose already is the stance (the Meshy sculpt)
# sets BASE_ARMS = {} before loading this file
BASE_ARMS = globals().get('BASE_ARMS', {
    'upperarm.R': (-6, 6, 0), 'forearm.R': (-10, 0, 0), 'hammer.R': (0, 0, 0),
    'upperarm.L': (-12, -6, 0), 'forearm.L': (-38, 0, 0), 'crucible.L': (36, 0, 0),
})
def base(t, breathe=True):
    b = 0.5 + 0.5 * math.sin(2 * math.pi * t / 4.0) if breathe else 0
    p = {'pelvis': {'loc': (0, 0, -0.03 * b)}, 'torso': {'rot': (1.5 * b, 0, 0)}, '_feet': {}}
    for n in ('upperarm.R', 'forearm.R', 'hammer.R', 'upperarm.L', 'forearm.L', 'crucible.L'):
        p[n] = {'rot': BASE_ARMS.get(n, (0, 0, 0))}
    return p
def mix(p, **over):
    for k, v in over.items():
        k = k.replace('__', '.')
        if k == '_feet': p['_feet'] = v; continue
        cur = p.setdefault(k, {})
        for kk, vv in v.items():
            if kk == 'rot' and 'rot' in cur: cur['rot'] = tuple(a + b for a, b in zip(cur['rot'], vv))
            elif kk == 'loc' and 'loc' in cur: cur['loc'] = tuple(a + b for a, b in zip(cur['loc'], vv))
            else: cur[kk] = vv
    return p
def crouch(depth, wide=0.0):
    return {'R': (-wide, 0, depth, 0), 'L': (wide, 0, depth, 0)}

def idle(t): return base(t)

WALK = 1.6
def walk(t):
    p = base(t, False); ph = 2 * math.pi * t / WALK
    # compression walk: pistons compress first, the body drops onto each planted foot
    bob = -0.07 * abs(math.sin(ph))
    mix(p, pelvis={'loc': (0.04 * math.sin(ph), 0, bob)}, torso={'rot': (4, 3 * math.sin(ph), 2 * math.sin(ph))},
        upperarm__R={'rot': (-10 * math.sin(ph), 0, 0)}, upperarm__L={'rot': (10 * math.sin(ph), 0, 0)})
    def foot(q):
        lift = max(0, math.sin(q)) * 0.16
        return (0, -0.28 * math.cos(q), lift, -10 * max(0, math.sin(q)))
    p['_feet'] = {'R': foot(ph), 'L': foot(ph + math.pi)}
    # the planted foot holds; the pelvis drop is taken by the knees
    for s in 'RL':
        dx, dy, dz, pt = p['_feet'][s]; p['_feet'][s] = (dx, dy, dz - bob, pt)
    return p

def diebreaker(t):
    # three ratchet notches up (0.2 / 0.6 / 1.0 s), a held peak, then the slam lands exactly at 1.8 s
    notch = 0
    for t0, a in ((0.15, -55), (0.55, -105), (0.95, -150)):
        if t >= t0: notch = a if t >= t0 + 0.14 else notch + (a - notch) * sm((t - t0) / 0.14)
    arm = notch if t < 1.6 else key(t, [(1.6, -150), (1.8, -48)])
    lean = key(t, [(0, 0), (1.2, -8), (1.6, -10), (1.8, 20)])
    drop = key(t, [(0, 0), (1.2, 0.04), (1.6, 0.04), (1.8, -0.22)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, drop)}, torso={'rot': (lean + jolt(t, 0.15, 2) + jolt(t, 0.55, 2) + jolt(t, 0.95, 2), 0, key(t, [(0, 0), (1.2, -14), (1.8, 6)]))},
        upperarm__R={'rot': (arm + 6, key(t, [(0, 0), (1.2, -12), (1.8, -6)]), 0)}, forearm__R={'rot': (key(t, [(0, 0), (1.0, -20), (1.8, 10)]), 0, 0)},
        upperarm__L={'rot': (key(t, [(0, 0), (1.6, 10), (1.8, -10)]), key(t, [(0, 0), (1.6, -20), (1.8, -12)]), 0)})
    p['_feet'] = crouch(-max(0, -drop) * 0.0, 0.0)
    p['_feet'] = {'R': (0, -0.1 * sm(t / 1.8), 0, 0), 'L': (0, 0.1 * sm(t / 1.8), 0, 0)}
    return p

def hammer_lodged(t):
    # hammer stuck in the floor after the slam: two tugs, then the extraction and reset (2.4 s)
    tug = jolt(t, 0.3, -6, 8, 3) + jolt(t, 0.95, -8, 8, 3)
    ext = sm((t - 1.5) / 0.75)
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.22 * (1 - ext))}, torso={'rot': (20 * (1 - ext) + tug, 0, 6 * (1 - ext))},
        upperarm__R={'rot': ((-48 + 6) * (1 - ext) + tug * 0.5, -6 * (1 - ext), 0)}, forearm__R={'rot': (10 * (1 - ext), 0, 0)},
        upperarm__L={'rot': (-10 * (1 - ext), -12 * (1 - ext), 0)})
    p['_feet'] = {'R': (0, -0.1 * (1 - ext), 0, 0), 'L': (0, 0.1 * (1 - ext), 0, 0)}
    return p

def slag_cast(t):
    # the crucible rises to shoulder height, tracks, then tips and flings at 2.0 s
    lift = key(t, [(0, 0), (0.6, 1), (2.0, 1)])
    tip = key(t, [(0, 0), (1.3, 0), (1.85, 1), (2.0, 0.85)])
    p = base(t, False)
    mix(p, torso={'rot': (-4 * lift, 0, 10 * lift)},
        upperarm__L={'rot': (-60 * lift, -14 * lift, 0)}, forearm__L={'rot': (10 * lift, 0, 0)}, crucible__L={'rot': (-36 * lift + 95 * tip, 0, 0)},
        upperarm__R={'rot': (8 * lift, 6 * lift, 0)})
    return p

def rail_ram(t):
    # 1.8 s windup: crouch, hammer swung back as a counterweight, heel wheels showing; 0.6 s run; the braking lean
    wind = key(t, [(0, 0), (1.2, 1), (1.8, 1)]); run = key(t, [(1.8, 0), (1.95, 1), (2.4, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.22 * wind + 0.08 * run)}, torso={'rot': (16 * wind + 14 * run, 0, 0)},
        upperarm__R={'rot': (55 * wind - 115 * run, 8 * wind, 0)}, forearm__R={'rot': (-15 * run, 0, 0)},
        upperarm__L={'rot': (-30 * wind + 10 * run, -18 * wind, 0)}, forearm__L={'rot': (-20 * wind, 0, 0)})
    p['_feet'] = {'R': (0, 0.15 * wind - 0.3 * run, 0.08 * run, 18 * wind), 'L': (0, -0.25 * wind + 0.2 * run, 0, 0)}
    return p

def release(sides):
    def f(t):
        # brace, chest swells (lean back), the selected vents blow on the last frames; arms clear the vents
        b = key(t, [(0, 0), (0.5, 1), (1.7, 1)]); hit = jolt(t, 1.62, 5, 14, 6)
        p = base(t, False)
        if sides:
            mix(p, pelvis={'loc': (0, 0, -0.10 * b)}, torso={'rot': (4 * b + hit, 0, 0)},
                upperarm__R={'rot': (-55 * b, 10 * b, 0)}, upperarm__L={'rot': (-55 * b, -10 * b, 0)},
                forearm__R={'rot': (-20 * b, 0, 0)}, forearm__L={'rot': (-10 * b, 0, 0)})
        else:
            mix(p, pelvis={'loc': (0, 0, -0.12 * b)}, torso={'rot': (-7 * b - hit, 0, 0)},
                upperarm__R={'rot': (0, 38 * b, 0)}, upperarm__L={'rot': (0, -38 * b, 0)}, forearm__R={'rot': (-25 * b, 0, 0)})
        p['_feet'] = {'R': (-0.08 * b, 0, 0, 0), 'L': (0.08 * b, 0, 0, 0)}
        return p
    return f

CAST = 3.0
def casting(t):
    # braced pouring stance (the Phase 2 sheet pose): crucible tipped forward, hammer cocked high, a stamp at each strip
    b = key(t, [(0, 0), (0.5, 1), (CAST, 1)])
    stamp = sum(jolt(t, t0, 1, 16, 7) for t0 in (1.8, 2.4, 3.0))
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.24 * b + 0.03 * stamp)}, torso={'rot': (12 * b + 2 * stamp, 0, 8 * b)},
        upperarm__R={'rot': (-120 * b, -10 * b, 0)}, forearm__R={'rot': (-30 * b, 0, 0)},
        upperarm__L={'rot': (-40 * b, -16 * b, 0)}, crucible__L={'rot': (55 * b, 0, 0)})
    p['_feet'] = {'R': (-0.18 * b, -0.10 * b, 0, 0), 'L': (0.18 * b, 0.25 * b, 0, 0)}
    return p

def vulnerable(t):
    # recovery / braking: slumped, hammer braced on the floor, steam heave (loops)
    h = math.sin(2 * math.pi * t / 2.0)
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, -0.16 + 0.02 * h)}, torso={'rot': (14 + 2 * h, 0, -4)},
        upperarm__R={'rot': (-30, -4, 0)}, forearm__R={'rot': (8, 0, 0)}, upperarm__L={'rot': (8, -4, 0)}, forearm__L={'rot': (10, 0, 0)})
    return p

def phase_shift(t):
    # rears back and spreads its arms while the plates shed / the vents open, then settles (1.5 s)
    r = key(t, [(0, 0), (0.5, 1), (1.0, 1), (1.5, 0)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0, 0.05 * r)}, torso={'rot': (-14 * r, 0, 0)},
        upperarm__R={'rot': (-30 * r, 45 * r, 0)}, upperarm__L={'rot': (-30 * r, -45 * r, 0)}, forearm__R={'rot': (-30 * r, 0, 0)})
    return p

def defeat(t):
    # kneels, the crucible comes up under the belly for a harmless final pour, then it locks solid (3.0 s, holds)
    k = key(t, [(0, 0), (1.2, 1), (3.0, 1)]); c = key(t, [(1.0, 0), (2.0, 1), (3.0, 1)])
    p = base(t, False)
    mix(p, pelvis={'loc': (0, 0.12 * k, -0.42 * k)}, torso={'rot': (16 * k, 0, 0)},
        upperarm__R={'rot': (-25 * k, -6 * k, 0)}, forearm__R={'rot': (12 * k, 0, 0)},
        upperarm__L={'rot': (-40 * c, 20 * c, 0)}, forearm__L={'rot': (-40 * c, 0, 0)}, crucible__L={'rot': (-30 * c, 0, 0)})
    p['_feet'] = {'R': (0, 0.05 * k, 0, 0), 'L': (0, 0.05 * k, 0, 0)}
    return p

def bake(name, seconds, fn, loop=False):
    frames = max(2, round(seconds * 30))
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        t = f / 30 if not loop else (f / frames) * seconds
        p = fn(t); apply(p); plant(p.get('_feet', {}))
        for pb in pbs:
            pb.keyframe_insert('rotation_quaternion', frame=f + 1, group=pb.name)
            pb.keyframe_insert('location', frame=f + 1, group=pb.name)
    act.frame_range = (1, frames + 1)
    return act

acts = [bake('Idle', 4.0, idle, True), bake('Walk', WALK, walk, True),
        bake('Diebreaker', 1.8, diebreaker), bake('HammerLodged', 2.4, hammer_lodged),
        bake('SlagCast', 2.0, slag_cast), bake('RailRam', 2.4, rail_ram),
        bake('ReleaseFrontBack', 1.7, release(False)), bake('ReleaseSides', 1.7, release(True)),
        bake('CastingCycle', CAST, casting), bake('Vulnerable', 2.0, vulnerable, True),
        bake('PhaseShift', 1.5, phase_shift), bake('Defeat', 3.0, defeat)]
print('CLIPS', [a.name for a in acts])
arm.animation_data.action = acts[0]
apply(base(0)); plant({})

# ---------------- optional preview renders (turnaround + attack poses) ----------------
def render_set(outdir):
    os.makedirs(outdir, exist_ok=True)
    # previews only: brighter emission (glTFast drops emissive strength; the game boosts glow itself) and folded fins
    for m in (MOLTEN, EMBER, AMBER): m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 2.2
    fins = [o for o, _ in final if o.name.startswith("VentFin_")]
    def fold(k):
        for o in fins: o.scale = (1, 1, k)
    try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError: sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 900, 1000
    sc.render.film_transparent = False
    world = bpy.data.worlds.new('W'); sc.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.93, 0.89, 0.80, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.45
    sc.view_settings.view_transform = 'AgX'
    key_l = bpy.data.objects.new('Key', bpy.data.lights.new('Key', 'SUN')); sc.collection.objects.link(key_l)
    key_l.data.energy = 3.2; key_l.rotation_euler = Euler((math.radians(50), math.radians(-25), math.radians(-35)))
    rim = bpy.data.objects.new('Rim', bpy.data.lights.new('Rim', 'SUN')); sc.collection.objects.link(rim)
    rim.data.energy = 1.6; rim.data.color = (1.0, 0.75, 0.5); rim.rotation_euler = Euler((math.radians(60), 0, math.radians(160)))
    floor = bpy.data.objects.new('Floor', bpy.data.meshes.new('Floor')); sc.collection.objects.link(floor)
    bm = bmesh.new(); bmesh.ops.create_circle(bm, cap_ends=True, segments=48, radius=4.0); bm.to_mesh(floor.data); bm.free()
    floor.data.materials.append(mat('FloorM', (0.10, 0.07, 0.05), 0.6, 0.5))
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 50
    def shot(name, yaw_deg, action=None, t=0.0, elev=12, dist=11.5):
        if action:
            arm.animation_data.action = bpy.data.actions[action]
            sc.frame_set(int(round(t * 30)) + 1)
        else:
            arm.animation_data.action = acts[0]; sc.frame_set(1)
        a = math.radians(yaw_deg); e = math.radians(elev)
        target = Vector((0, 0, 1.9))
        cam.location = target + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(outdir, name + '.png'); bpy.ops.render.render(write_still=True)
        print('RENDER', name)
    fold(0.15)
    shot('01_front', 0); shot('02_side', 90); shot('03_back', 180); shot('04_threequarter', -35, elev=22)
    shot('05_diebreaker_peak', -30, 'Diebreaker', 1.55, 18); shot('06_diebreaker_slam', -30, 'Diebreaker', 1.8, 18)
    shot('07_slag', -40, 'SlagCast', 1.9, 18); shot('08_ram', 70, 'RailRam', 1.6, 15); shot('09_casting', -30, 'CastingCycle', 2.0, 18)
    shot('10_release', -20, 'ReleaseFrontBack', 1.6, 15); shot('11_defeat', -30, 'Defeat', 3.0, 18)
    fold(1.0); shot('12_open_pressure', -25, 'PhaseShift', 0.7, 18); shot('13_open_pressure_back', 150, None, 0, 18)

