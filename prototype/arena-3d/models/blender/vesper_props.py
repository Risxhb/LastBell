# Vesper Seedkeeper props (miniboss art pass, 9 Nov 2026): cord stake, cord segment, seed of light.
# Prompts: build-evidence/miniboss-art-20261109/vesper-seedkeeper/prompts.md
#
#   blender -b -P vesper_props.py -- <out_dir> [--stage model|texture] [--body <VesperSeedkeeper.glb>] [--no-renders]
#
# Builds the three props from code (no source meshes), exports VesperStake.glb, VesperCord.glb, VesperSeed.glb into
# <out_dir>, renders turnarounds and Unity-camera mock frames (gameplay camera 13 m up / 9.7 m back / 44 deg vertical
# FOV, plus a close camera) with the rigged body, and saves vesper_props.blend. --stage texture adds UVs and bakes a
# 1k texture set per prop (base colour, ORM, emissive) instead of flat materials.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "vesper_props_out")
STAGE = argv[argv.index("--stage") + 1] if "--stage" in argv else "model"
BODY = argv[argv.index("--body") + 1] if "--body" in argv else None
RENDERS = "--no-renders" not in argv
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- tunables (refined per critic round)
P = dict(
    stake_height=1.12, stake_shaft_r=0.05, stake_bud_h=0.27, stake_bud_r=0.085, stake_eyelet_h=0.80, stake_eyelet_r=0.06,
    stake_petals=4, stake_petal_len=0.17, stake_petal_w=0.10, stake_petal_tilt=50,
    cord_core_r=0.031, cord_vine_r=0.009, cord_helix_r=0.034, cord_turns=2, cord_leaf=0.06, cord_vines=3,
    seed_h=0.32, seed_r=0.11, seed_sepals=3, seed_sepal_len=0.17,
)
# palette (linear-ish sRGB values in 0..1) matched to the Meshy body texture
def H(h):
    """sRGB hex -> linear tuple (Blender colour sockets are linear)."""
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)
IVORY = H("FFF2D8"); GOLD = H("E6BC62"); COBALT = H("2552A3"); LIGHT = (1.0, 0.91, 0.69)
# per-prop emissive multiplier: Unity multiplies emissiveFactor by these in VesperProps.cs (StakeGlow/CordGlow/SeedGlow)
GLOW = dict(VesperStake=2.0, VesperCord=2.0, VesperSeed=2.2)

def U(x, y, z): return Vector((-x, -z, y))   # Unity (glTFast) position -> Blender

# ---------------------------------------------------------------- scene helpers
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, color, metal=0.0, rough=0.5, emit=None, strength=0.0, alpha=1.0):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal; b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = strength
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: pass
    return m

def M_IVORY(): return mat("vs_ivory", IVORY, 0.0, 0.30)
def M_GOLD(): return mat("vs_gold", GOLD, 0.55, 0.38)
def M_BUD(): return mat("vs_bud", IVORY, 0.0, 0.30)
def M_PETAL(): return mat("vs_petal", IVORY, 0.0, 0.30)
def M_COBALT(): return mat("vs_cobalt", COBALT, 0.0, 0.18)
def M_LIGHT(s=6.0): return mat("vs_light", LIGHT, 0.0, 0.4, LIGHT, s)
CORD_LIGHT = (1.0, 0.76, 0.35); SEED_LIGHT = (1.0, 0.87, 0.58)
def M_CORDCORE(): return mat("vs_cordcore", CORD_LIGHT, 0.0, 0.4, CORD_LIGHT, 4.0)
def M_SEED(): return mat("vs_seed", SEED_LIGHT, 0.0, 0.35, SEED_LIGHT, 8.0)
def M_SHELL(): return mat("vs_seedshell", SEED_LIGHT, 0.0, 0.2, SEED_LIGHT, 3.0, alpha=0.22)

def link(obj, coll=None):
    (coll or bpy.context.scene.collection).objects.link(obj); return obj

def mesh_obj(name, verts, faces, m):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me); link(o); o.data.materials.append(m)
    return o

def apply_all(o):
    bpy.context.view_layer.objects.active = o
    for md in list(o.modifiers):
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
        bpy.ops.object.modifier_apply(modifier=md.name)

def lathe(name, profile, m, segs=16, z0=0.0):
    """profile: list of (radius, z). Closed at ends when radius 0."""
    verts, faces = [], []
    n = len(profile)
    for i in range(segs):
        a = 2 * math.pi * i / segs
        for r, z in profile: verts.append((r * math.cos(a), r * math.sin(a), z + z0))
    for i in range(segs):
        j = (i + 1) % segs
        for k in range(n - 1):
            faces.append((i * n + k, j * n + k, j * n + k + 1, i * n + k + 1))
    o = mesh_obj(name, verts, faces, m)
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = True
    return o

def bake_xform(o, loc=True):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=loc, rotation=True)

def leaf(name, length, width, m, bend=0.3, cup=0.4, thick=0.008, nu=7, nv=4, tip_pow=0.8, base_pinch=0.25, shape="point"):
    """A petal/leaf along +Z, width on X, bending toward +Y; solidified."""
    verts, faces = [], []
    for i in range(nu + 1):
        u = i / nu
        w = width * (math.sin(math.pi * min(1, base_pinch + (1 - base_pinch) * u)) ** tip_pow) * (1 if u < 1 else 0)
        if u < 1e-6: w = width * base_pinch * 0.5
        if shape == "round":   # spoon: narrow base, widest past the middle, round tip
            uu = 0.15 + 0.85 * u
            w = max(width * 0.12, width * math.sqrt(max(0.0, 1 - ((uu - 0.62) / 0.47) ** 2)))
        for j in range(nv + 1):
            v = -1 + 2 * j / nv
            x = v * w
            y = bend * length * u * u + cup * abs(v) ** 2 * w
            verts.append((x, y, u * length))
    for i in range(nu):
        for j in range(nv):
            a = i * (nv + 1) + j
            faces.append((a, a + 1, a + nv + 2, a + nv + 1))
    o = mesh_obj(name, verts, faces, m)
    for p in o.data.polygons: p.use_smooth = True
    s = o.modifiers.new("solid", 'SOLIDIFY'); s.thickness = thick; s.offset = 0
    apply_all(o)
    return o

def tube_curve(name, pts, radius, m, sides=1):
    cu = bpy.data.curves.new(name, 'CURVE'); cu.dimensions = '3D'
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p, co in zip(sp.points, pts): p.co = (*co, 1)
    cu.bevel_depth = radius; cu.bevel_resolution = sides; cu.use_fill_caps = False
    o = bpy.data.objects.new(name, cu); link(o); o.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH')
    o = bpy.context.view_layer.objects.active
    for p in o.data.polygons: p.use_smooth = True
    return o

def ico(name, r, m, sub=2, scale=(1, 1, 1), loc=(0, 0, 0)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub, radius=r, location=loc)
    o = bpy.context.active_object; o.name = name; o.scale = scale; o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth = True
    return o

def torus(name, R, r, m, loc=(0, 0, 0), rot=(0, 0, 0), maj=16, mnr=6):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=maj, minor_segments=mnr, location=loc, rotation=rot)
    o = bpy.context.active_object; o.name = name; o.data.materials.append(m)
    for p in o.data.polygons: p.use_smooth = True
    return o

def place(o, loc=(0, 0, 0), rot=(0, 0, 0)):
    o.location = loc; o.rotation_euler = rot; return o

def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)

# ---------------------------------------------------------------- A. cord stake (origin at the floor; Blender -Y = Unity +Z = toward the vessel)
def build_stake():
    H, sr = P["stake_height"], P["stake_shaft_r"]
    parts = []
    # low gold foot, sunk 2 cm into the moss
    parts.append(lathe("st_base", [(0, -0.02), (0.10, -0.02), (0.10, 0.012), (0.075, 0.04), (sr * 1.3, 0.10), (sr * 1.1, 0.16), (0, 0.16)], M_GOLD(), 16))
    # collar: cupped, round-tipped ivory petals tilted up ~40 deg (a lotus, not a caltrop)
    n = P["stake_petals"]
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        lf = leaf(f"st_petal{i}", P["stake_petal_len"], P["stake_petal_w"], M_IVORY(), bend=0.25, cup=-0.55, thick=0.014, nu=8, nv=6, shape="round")
        lf.rotation_euler = Euler((math.radians(-P["stake_petal_tilt"]), 0, 0)); lf.location = (0, 0.035, 0.03)
        bake_xform(lf)
        lf.rotation_euler = Euler((0, 0, a)); parts.append(lf)
    # tapered shaft: 0.12 m at the foot to 0.075 m under the bud
    top = H - P["stake_bud_h"] - 0.06
    parts.append(lathe("st_shaft", [(0, 0.15), (sr * 1.2, 0.15), (sr * 0.95, 0.42), (sr * 0.65, top), (0, top)], M_GOLD(), 14))
    # cobalt enamel band, gold rims, a large gold diamond pip facing the vessel (-Y)
    bz = 0.50
    parts.append(lathe("st_band", [(0, bz - 0.05), (sr * 1.30, bz - 0.05), (sr * 1.40, bz), (sr * 1.30, bz + 0.05), (0, bz + 0.05)], M_COBALT(), 14))
    parts.append(torus("st_rim_lo", sr * 1.33, 0.009, M_GOLD(), (0, 0, bz - 0.052), maj=16, mnr=4))
    parts.append(torus("st_rim_hi", sr * 1.33, 0.009, M_GOLD(), (0, 0, bz + 0.052), maj=16, mnr=4))
    parts.append(ico("st_pip", 0.025, M_GOLD(), 1, (0.8, 0.5, 1.0), (0, -(sr * 1.4 + 0.008), bz)))
    # eyelet: a gold ring standing on the vessel side (-Y), its hole along the cord, on a short arm
    ez = P["stake_eyelet_h"]
    parts.append(torus("st_collar", sr * 0.72, 0.014, M_GOLD(), (0, 0, ez - 0.06), maj=16, mnr=6))
    parts.append(torus("st_eyelet", P["stake_eyelet_r"], 0.016, M_GOLD(), (0, -(sr * 0.65 + P["stake_eyelet_r"] - 0.005), ez), (math.pi / 2, 0, 0), maj=16, mnr=6))
    parts.append(tube_curve("st_eyearm", [(0, 0, ez), (0, -(sr * 0.65), ez)], 0.014, M_GOLD(), 1))
    # gold cup under the bud, then a solid ivory ovoid bud with 2 slim gold leaves (>= 60% ivory showing) and a gold tip
    bh, br = P["stake_bud_h"], P["stake_bud_r"]; b0 = H - bh
    parts.append(lathe("st_cup", [(0, b0 - 0.07), (sr * 0.7, b0 - 0.07), (0.04, b0 - 0.04), (0.05, b0), (0, b0)], M_GOLD(), 18))
    parts.append(torus("st_cuplip", 0.05, 0.009, M_GOLD(), (0, 0, b0), maj=18, mnr=6))
    prof = [(0, b0 - 0.01)]
    for k in range(1, 13):
        t = k / 12
        r = br * math.sin(math.pi * (0.08 + 0.92 * t)) ** 0.7 * (1 - 0.25 * t)
        prof.append((r, b0 - 0.01 + bh * t))
    prof[-1] = (0, b0 - 0.01 + bh)
    parts.append(lathe("st_bud", prof, M_BUD(), 18))
    for i, a in enumerate((0.0, math.pi)):
        lf = leaf(f"st_budleaf{i}", bh * 0.58, 0.04, M_GOLD(), bend=-0.18, cup=-0.5, thick=0.01, nu=7, nv=3)
        lf.location = (0, br * 1.02, b0 - 0.005); lf.rotation_euler = (math.radians(4), 0, 0)
        bake_xform(lf)
        lf.rotation_euler = (0, 0, a + math.pi / 2); parts.append(lf)
    tz = b0 - 0.01 + bh
    parts.append(lathe("st_tip", [(0, tz - 0.03), (0.016, tz - 0.02), (0.005, tz + 0.02), (0, tz + 0.04)], M_GOLD(), 10))
    return join("VesperStake", parts)

# ---------------------------------------------------------------- B. cord segment (1 m along Blender -Y = Unity +Z, origin at the start)
def build_cord():
    turns, hr, vr = P["cord_turns"], P["cord_helix_r"], P["cord_vine_r"]
    parts = []
    parts.append(lathe("cd_core", [(P["cord_core_r"], 0), (P["cord_core_r"], 1.0)], M_CORDCORE(), 8))   # open tube: tiles butt cleanly
    for k in range(P["cord_vines"]):
        ph = 2 * math.pi * k / P["cord_vines"]
        pts = []
        steps = 8 * turns
        for s in range(steps + 1):
            t = s / steps; a = ph + 2 * math.pi * turns * t
            pts.append((hr * math.cos(a), hr * math.sin(a), t))
        parts.append(tube_curve(f"cd_vine{k}", pts, vr, M_GOLD(), 0))
    # 2 small ivory leaves laid along the cord (under 20 deg), never sticking out sideways
    for k, (z, a) in enumerate(((0.22, 0.5), (0.72, 0.5 + math.pi))):
        lf = leaf(f"cd_leaf{k}", P["cord_leaf"], 0.026, M_IVORY(), bend=0.15, cup=0.3, thick=0.006, nu=4, nv=2)
        lf.rotation_euler = (math.radians(-18), 0, 0)
        bake_xform(lf, loc=False)
        lf.location = (hr * math.cos(a), hr * math.sin(a), z); lf.rotation_euler = (0, 0, a - math.pi / 2)
        parts.append(lf)
    o = join("VesperCord", parts)
    o.rotation_euler = (math.radians(90), 0, 0)   # Blender +Z -> -Y (= Unity +Z)
    bpy.ops.object.transform_apply(rotation=True)
    return o

# ---------------------------------------------------------------- C. seed of light (origin at the seed's centre)
def build_seed():
    h, r = P["seed_h"], P["seed_r"]
    parts = []
    z0 = -h * 0.42
    prof = [(0, z0)]
    for k in range(1, 15):
        t = k / 14
        rr = r * math.sin(math.pi * t) ** 0.55 * (1 - 0.6 * t ** 1.6)
        prof.append((rr, z0 + h * t))
    prof[-1] = (0, z0 + h)
    parts.append(lathe("sd_body", prof, M_SEED(), 16))
    n = P["seed_sepals"]
    base = z0 + 0.055   # the seed's lower ~40% sits inside the cup
    for i in range(n):
        a = 2 * math.pi * i / n
        # upright gold sepal rising ~65 deg, cupped toward the seed, curling out at the tip
        sp = leaf(f"sd_sepal{i}", P["seed_sepal_len"], 0.05, M_GOLD(), bend=0.22, cup=-0.7, thick=0.01, nu=8, nv=4, shape="round")
        sp.rotation_euler = (math.radians(-22), 0, 0); sp.location = (0, 0.07, base - 0.02)
        bake_xform(sp)
        sp.rotation_euler = (0, 0, a); parts.append(sp)
        # spoon-shaped ivory petal between the sepals, tilted 50 deg up
        pt = leaf(f"sd_petal{i}", 0.12, 0.08, M_PETAL(), bend=0.15, cup=-0.55, thick=0.01, nu=7, nv=5, shape="round")
        pt.rotation_euler = (math.radians(-32), 0, 0); pt.location = (0, 0.06, base - 0.03)
        bake_xform(pt)
        pt.rotation_euler = (0, 0, a + math.pi / n); parts.append(pt)
    parts.append(lathe("sd_base", [(0, base - 0.075), (0.04, base - 0.06), (0.075, base - 0.02), (0, base - 0.01)], M_GOLD(), 14))
    pts = []
    for s in range(13):
        t = s / 12; a = 1.6 * math.pi * t
        pts.append((0.03 * math.sin(a) * t, 0.03 * (1 - math.cos(a)) * t, base - 0.07 - 0.06 * t))
    parts.append(tube_curve("sd_stem", pts, 0.012, M_GOLD(), 1))
    parts.append(ico("sd_bead", 0.018, M_GOLD(), 2, (1, 1, 1), (pts[-1][0], pts[-1][1], pts[-1][2] - 0.016)))
    return join("VesperSeed", parts)

# ---------------------------------------------------------------- texture stage (UV + 1k bake)
T = dict(
    ivory_lift=0.04, petal_lift=0.20, bud_glow=0.45, gold_brush=0.12, gold_cavity=0.8, gold_lift=0.08, pip_scale=34.0, pip_size=0.16,
    cord_emit=0.62, cord_pulse=0.10, seed_emit=0.75, ao_floor=0.80, size=1024,
)

def _nodes(m):
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    return nt, out

def _coord(nt):
    tc = nt.nodes.new("ShaderNodeTexCoord"); return tc.outputs["Object"]

def _math(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
    for i, v in enumerate((a, b)):
        if v is None: continue
        if isinstance(v, (int, float)): n.inputs[i].default_value = v
        else: nt.links.new(v, n.inputs[i])
    return n.outputs[0]

def _mix(nt, fac, c1, c2):
    n = nt.nodes.new("ShaderNodeMix"); n.data_type = 'RGBA'
    if isinstance(fac, (int, float)): n.inputs["Factor"].default_value = fac
    else: nt.links.new(fac, n.inputs["Factor"])
    for sock, c in ((n.inputs[6], c1), (n.inputs[7], c2)):
        if isinstance(c, tuple): sock.default_value = (*c, 1)
        else: nt.links.new(c, sock)
    return n.outputs[2]

def _noise(nt, co, scale, detail=4, stretch=None):
    if stretch:
        mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = stretch; nt.links.new(co, mp.inputs["Vector"]); co = mp.outputs[0]
    n = nt.nodes.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = detail
    nt.links.new(co, n.inputs["Vector"]); return n.outputs["Fac"]

def _sep_z(nt, co):
    s = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(co, s.inputs[0]); return s.outputs

CH = {}   # material name -> dict(color, rough, metal, emit) sockets

def _lin(nt, x, lo, hi):
    """clamp((x - lo) / (hi - lo), 0, 1)"""
    return _math(nt, 'MULTIPLY', _math(nt, 'SUBTRACT', x, lo), 1.0 / (hi - lo), True)

def _scale(nt, col, k):
    n = nt.nodes.new("ShaderNodeVectorMath"); n.operation = 'SCALE'
    if isinstance(col, tuple): n.inputs[0].default_value = col
    else: nt.links.new(col, n.inputs[0])
    if isinstance(k, (int, float)): n.inputs["Scale"].default_value = k
    else: nt.links.new(k, n.inputs["Scale"])
    return n.outputs[0]

def proceduralize():
    """Rebuild the flat materials as procedural ones (object space) and remember each channel's socket.
    Texture round 1: clean gradients (no tarnish noise or grain), a lighter less-metallic gold with a small emissive lift,
    deep cobalt, a glowing stake bud, a warm (not white) cord core, a seed that does not clip."""
    # ivory porcelain: cream glaze gradient, rough 0.30, a faint warm emissive lift
    for name in ("vs_ivory", "vs_bud", "vs_petal"):
        m = bpy.data.materials.get(name)
        if not m: continue
        nt, out = _nodes(m); co = _coord(nt); xyz = _sep_z(nt, co)
        grad = _math(nt, 'ADD', _math(nt, 'MULTIPLY', _math(nt, 'SINE', _math(nt, 'MULTIPLY', xyz["Z"], 7.0)), 0.5), 0.5)
        base = _mix(nt, grad, H("EADBC0"), H("FFF4DE"))
        emit = _scale(nt, base, T["petal_lift"] if name == "vs_petal" else T["ivory_lift"])
        if name == "vs_bud":
            # the bud glows over its top 60%: #FFE3A0, 0 at 0.93 m rising to T["bud_glow"] at 1.08 m
            f = _math(nt, 'MULTIPLY', _lin(nt, xyz["Z"], 0.93, 1.08), T["bud_glow"])
            emit = _scale(nt, H("FFE3A0"), _math(nt, 'MAXIMUM', f, T["ivory_lift"]))
        CH[name] = dict(color=base, rough=0.30, metal=0.0, emit=emit)
    # gilded gold: a soft light-to-deep gradient, deepest in cavities, metal 0.55 / rough 0.38, emissive lift
    m = bpy.data.materials.get("vs_gold")
    if m:
        nt, out = _nodes(m); co = _coord(nt); xyz = _sep_z(nt, co)
        grad = _math(nt, 'ADD', _math(nt, 'MULTIPLY', _math(nt, 'SINE', _math(nt, 'MULTIPLY', xyz["Z"], 5.0)), 0.5), 0.5)
        br = _noise(nt, co, 60, 3, stretch=(1, 1, 0.06))
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.04; ao.samples = 8
        cav = _math(nt, 'SUBTRACT', 1.0, ao.outputs["AO"])
        base = _mix(nt, grad, H("C9973F"), H("F2CF7A"))
        base = _mix(nt, _math(nt, 'MULTIPLY', br, T["gold_brush"]), base, H("F6DA92"))
        base = _mix(nt, _math(nt, 'MULTIPLY', cav, T["gold_cavity"], True), base, H("B88A3A"))
        rough = _math(nt, 'ADD', _math(nt, 'MULTIPLY', br, 0.06), 0.35)
        CH["vs_gold"] = dict(color=base, rough=rough, metal=0.55, emit=_scale(nt, base, T["gold_lift"]))
    # deep cobalt enamel with small gold diamond pips (manhattan voronoi), like the body's leaf inlays
    m = bpy.data.materials.get("vs_cobalt")
    if m:
        nt, out = _nodes(m); co = _coord(nt)
        vo = nt.nodes.new("ShaderNodeTexVoronoi"); vo.distance = 'MANHATTAN'; vo.inputs["Scale"].default_value = T["pip_scale"]; vo.inputs["Randomness"].default_value = 0.0
        nt.links.new(co, vo.inputs["Vector"])
        pip = _math(nt, 'LESS_THAN', vo.outputs["Distance"], T["pip_size"])
        n = _noise(nt, co, 12, 2)
        enamel = _mix(nt, _math(nt, 'MULTIPLY', n, 0.6), H("1A3F85"), H("2552A3"))
        base = _mix(nt, pip, enamel, H("E6BC62"))
        CH["vs_cobalt"] = dict(color=base, rough=_math(nt, 'ADD', _math(nt, 'MULTIPLY', pip, 0.2), 0.16), metal=_math(nt, 'MULTIPLY', pip, 0.55), emit=None)
    # cord core: warm gold light (#FFE6B0 base, #FFC86A emissive), soft pulses along its length (2 per metre: tiles seamlessly)
    m = bpy.data.materials.get("vs_cordcore")
    if m:
        nt, out = _nodes(m); co = _coord(nt); xyz = _sep_z(nt, co)
        ph = _math(nt, 'MULTIPLY', xyz["Y"], 2 * math.pi * 2)
        pulse = _math(nt, 'MULTIPLY', _math(nt, 'ADD', _math(nt, 'SINE', ph), 1.0), 0.5)
        emit = _scale(nt, H("FFC86A"), _math(nt, 'ADD', _math(nt, 'MULTIPLY', pulse, T["cord_pulse"]), T["cord_emit"]))
        CH["vs_cordcore"] = dict(color=H("FFE6B0"), rough=0.45, metal=0.0, emit=emit)
    # seed: #FFE7B0 body; emissive #FFCF73 fading from the tip (1.0) to the equator (~0.6) to inside the cup (~0.4)
    m = bpy.data.materials.get("vs_seed")
    if m:
        nt, out = _nodes(m); co = _coord(nt); xyz = _sep_z(nt, co)
        t = _lin(nt, xyz["Z"], -0.08, 0.18)
        k = _math(nt, 'MULTIPLY', _math(nt, 'ADD', _math(nt, 'MULTIPLY', t, 0.6), 0.4), T["seed_emit"])
        CH["vs_seed"] = dict(color=H("FFE7B0"), rough=0.35, metal=0.0, emit=_scale(nt, H("FFCF73"), k))

def _bake_channel(o, img, channel):
    """Bake one channel through an Emission shader (no lighting): color / rough / metal / emit."""
    for slot in o.material_slots:
        m = slot.material
        if not m or m.name not in CH: continue
        nt = m.node_tree; ch = CH[m.name]; out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        em = nt.nodes.new("ShaderNodeEmission"); em.name = "_bake_em"
        v = ch[channel]
        if v is None: em.inputs["Color"].default_value = (0, 0, 0, 1)
        elif isinstance(v, (int, float)): em.inputs["Color"].default_value = (v, v, v, 1)
        elif isinstance(v, tuple): em.inputs["Color"].default_value = (*v[:3], 1)
        else: nt.links.new(v, em.inputs["Color"])
        nt.links.new(em.outputs[0], out.inputs["Surface"])
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.name = "_bake_img"; tn.image = img; nt.nodes.active = tn
    bpy.ops.object.bake(type='EMIT', margin=6)
    for slot in o.material_slots:
        m = slot.material
        if not m or m.name not in CH: continue
        for nm in ("_bake_em", "_bake_img"):
            n = m.node_tree.nodes.get(nm)
            if n: m.node_tree.nodes.remove(n)

def _bake_ao(o, img):
    for slot in o.material_slots:
        m = slot.material
        if not m or m.name not in CH: continue
        nt = m.node_tree
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.name = "_bake_img"; tn.image = img; nt.nodes.active = tn
    bpy.context.scene.cycles.samples = 64
    bpy.ops.object.bake(type='AO', margin=6)
    bpy.context.scene.cycles.samples = 1
    for slot in o.material_slots:
        m = slot.material
        if m and m.name in CH:
            n = m.node_tree.nodes.get("_bake_img")
            if n: m.node_tree.nodes.remove(n)

def _img(name, size, noncolor):
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=False)
    if noncolor: img.colorspace_settings.name = 'Non-Color'
    return img

def texture(o, size=None):
    """Smart UV, then bake base colour / ORM / emissive into one 1k set per prop; keeps a non-baked shell material."""
    import numpy as np
    size = size or T["size"]
    if not CH: proceduralize()
    sc = bpy.context.scene; prev_engine = sc.render.engine
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 1
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    imgs = {k: _img(f"{o.name}_{k}", size, k != "basecolor" and k != "emissive") for k in ("basecolor", "rough", "metal", "emissive", "ao")}
    _bake_channel(o, imgs["basecolor"], "color")
    _bake_channel(o, imgs["rough"], "rough")
    _bake_channel(o, imgs["metal"], "metal")
    has_emit = any(s.material and s.material.name in CH and CH[s.material.name]["emit"] is not None for s in o.material_slots)
    if has_emit: _bake_channel(o, imgs["emissive"], "emit")
    _bake_ao(o, imgs["ao"])
    # ORM: R = AO (softened), G = roughness, B = metallic
    px = lambda im: np.array(im.pixels[:], dtype=np.float32).reshape(-1, 4)
    ao, ro, me = px(imgs["ao"]), px(imgs["rough"]), px(imgs["metal"])
    orm = np.ones_like(ao); orm[:, 0] = T["ao_floor"] + (1 - T["ao_floor"]) * ao[:, 0]; orm[:, 1] = ro[:, 0]; orm[:, 2] = me[:, 0]
    oi = _img(f"{o.name}_orm", size, True); oi.pixels.foreach_set(orm.ravel())
    keep = {"basecolor": imgs["basecolor"], "orm": oi}
    if has_emit: keep["emissive"] = imgs["emissive"]
    for k, im in keep.items():
        im.filepath_raw = os.path.join(OUT, f"{o.name}_{k}.png"); im.file_format = 'PNG'; im.save()
    # the baked material
    bm_ = bpy.data.materials.new(o.name + "_baked"); bm_.use_nodes = True; nt = bm_.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    t_b = nt.nodes.new("ShaderNodeTexImage"); t_b.image = keep["basecolor"]; nt.links.new(t_b.outputs["Color"], bsdf.inputs["Base Color"])
    t_o = nt.nodes.new("ShaderNodeTexImage"); t_o.image = oi
    sp = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(t_o.outputs["Color"], sp.inputs[0])
    nt.links.new(sp.outputs["Green"], bsdf.inputs["Roughness"]); nt.links.new(sp.outputs["Blue"], bsdf.inputs["Metallic"])
    grp = bpy.data.node_groups.get("glTF Material Output")
    if not grp:
        grp = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        grp.interface.new_socket("Occlusion", in_out='INPUT', socket_type='NodeSocketFloat')
    gn = nt.nodes.new("ShaderNodeGroup"); gn.node_tree = grp; nt.links.new(sp.outputs["Red"], gn.inputs["Occlusion"])
    if has_emit:
        t_e = nt.nodes.new("ShaderNodeTexImage"); t_e.image = keep["emissive"]
        nt.links.new(t_e.outputs["Color"], bsdf.inputs["Emission Color"]); bsdf.inputs["Emission Strength"].default_value = GLOW.get(o.name, 1.0)
    # swap every baked slot to the one baked material (non-baked slots, e.g. the seed's glow shell, stay)
    for slot in o.material_slots:
        if slot.material and slot.material.name in CH: slot.material = bm_
    # merge duplicate slots
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    try: bpy.ops.object.material_slot_remove_unused()
    except Exception: pass
    sc.render.engine = prev_engine

# ---------------------------------------------------------------- export
def export(o, path):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_apply=True,
                              export_yup=True, export_animations=False, export_materials='EXPORT')

# ---------------------------------------------------------------- renders
def setup_render(w=1600, h=900):
    sc = bpy.context.scene
    try: sc.render.engine = 'BLENDER_EEVEE'
    except Exception: sc.render.engine = 'BLENDER_EEVEE_NEXT'
    try: sc.eevee.taa_render_samples = 32
    except Exception: pass
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'AgX'
    return sc

def world(color, strength):
    w = bpy.data.worlds.new("w"); w.use_nodes = True
    bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (*color, 1); bg.inputs[1].default_value = strength
    bpy.context.scene.world = w

def cam_look(name, pos, target, fov_deg=44, lens_vertical=True):
    cd = bpy.data.cameras.new(name); cd.sensor_fit = 'VERTICAL' if lens_vertical else 'AUTO'; cd.angle = math.radians(fov_deg)
    c = bpy.data.objects.new(name, cd); link(c); c.location = pos
    c.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    return c

def render(cam, path):
    sc = bpy.context.scene; sc.camera = cam; sc.render.filepath = path
    bpy.ops.render.render(write_still=True)

def sun(name, rot, energy, color):
    ld = bpy.data.lights.new(name, 'SUN'); ld.energy = energy; ld.color = color
    o = bpy.data.objects.new(name, ld); link(o); o.rotation_euler = rot; return o

def turnarounds(objs):
    """Each prop alone on neutral grey: front / three-quarter / side / top, same scale for all three."""
    world((0.42, 0.42, 0.44), 0.8)
    sun("key", (math.radians(50), 0, math.radians(30)), 3.5, (1, 0.97, 0.92))
    sun("rim", (math.radians(60), 0, math.radians(200)), 1.5, (0.75, 0.8, 1.0))
    floor = bpy.data.meshes.new("gfloor"); bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6); bm.to_mesh(floor); bm.free()
    fo = bpy.data.objects.new("gfloor", floor); link(fo); fo.data.materials.append(mat("g_floor", (0.5, 0.5, 0.5), 0, 0.9))
    setup_render(900, 900)
    for o in objs:
        others = [x for x in objs if x is not o]
        for x in others: x.hide_render = True
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
        hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
        ctr = (lo + hi) / 2; size = max((hi - lo).length, 0.3)
        fo.location = (ctr.x, ctr.y, lo.z if o.name != "VesperSeed" else lo.z - 0.15)
        d = size * 2.0
        views = {"front": (0, -1, 0.25), "34": (0.75, -0.75, 0.4), "side": (1, 0, 0.2), "top": (0.02, -0.25, 1)}
        for k, (x, y, z) in views.items():
            dv = Vector((x, y, z)).normalized() * d
            c = cam_look(f"cam_{o.name}_{k}", ctr + dv, ctr, 30)
            render(c, os.path.join(OUT, f"turn_{o.name}_{k}.png"))
        for x in others: x.hide_render = False
    bpy.data.objects.remove(fo)

def arena_mock(stake, cord, seed):
    """Unity-camera mock of the Verdant Chimes floor at dusk with the rigged body and warning stand-ins."""
    sc = bpy.context.scene
    for o in list(sc.objects):
        if o.type in ('LIGHT', 'CAMERA'): bpy.data.objects.remove(o)
    world((0.30, 0.36, 0.36), 1.0)
    sun("dusk_key", (math.radians(62), 0, math.radians(-35)), 2.2, (0.78, 0.74, 1.0))
    sun("warm_fill", (math.radians(70), 0, math.radians(150)), 0.7, (1.0, 0.75, 0.5))
    # floor: dark moss disc (13.5 m bound) with a lighter stone ring
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=15, depth=0.1, location=(0, 0, -0.05))
    fl = bpy.context.active_object; fl.name = "moss"; fl.data.materials.append(mat("sage", H("8E9C86"), 0, 0.9))
    bpy.ops.mesh.primitive_torus_add(major_radius=13.9, minor_radius=0.25, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(mat("stone", (0.32, 0.30, 0.26), 0, 0.8))
    # body
    if BODY and os.path.exists(BODY):
        bpy.ops.import_scene.gltf(filepath=BODY)
        arm = next((o for o in bpy.context.selected_objects if o.type == 'ARMATURE'), None)
        if arm and arm.animation_data is not None:
            act = bpy.data.actions.get("TetherLoop")
            if act:
                arm.animation_data.action = act
                try: arm.animation_data.action_slot = act.slots[0]
                except Exception: pass
                sc.frame_set(1)

    def ring(name, at, r_in, r_out, color, alpha, emit=2.0):
        bpy.ops.mesh.primitive_circle_add(vertices=64, radius=r_out, fill_type='NGON', location=(at.x, at.y, 0.02))
        d = bpy.context.active_object; d.name = name + "_fill"; made.append(d)
        d.data.materials.append(mat(name + "_fillm", color, 0, 1, color, 0.6, alpha))
        bpy.ops.mesh.primitive_torus_add(major_radius=r_out - 0.04, minor_radius=0.04, major_segments=64, minor_segments=4, location=(at.x, at.y, 0.03))
        t = bpy.context.active_object; made.append(t); t.scale.z = 0.2; t.data.materials.append(mat(name + "_rim", color, 0, 1, color, emit))
    def lane(name, a, b, width, color):
        d = b - a; L = d.length; mid = (a + b) / 2
        bpy.ops.mesh.primitive_plane_add(size=1, location=(mid.x, mid.y, 0.025))
        p = bpy.context.active_object; made.append(p); p.name = name; p.scale = (width, L, 1)
        p.rotation_euler = (0, 0, math.atan2(d.y, d.x) - math.pi / 2)
        p.data.materials.append(mat(name + "_m", color, 0, 1, color, 0.8, 0.45))
    def numeral(text, at, cam_pos):
        # in game: an IMGUI label at the soak's origin + 0.2 m, 4% of the screen height, drawn over everything.
        # Here: a flat text card 60% of the way to the camera, sized to the same screen height, so it is on top.
        base = Vector((at.x, at.y, 0.2)); v = Vector(cam_pos) - base; D = v.length
        pos = base + v * 0.6; d2 = D * 0.4
        bpy.ops.object.text_add(location=pos); t = bpy.context.active_object; made.append(t); numerals.append(t)
        t.data.body = text; t.data.size = 0.04 * 2 * d2 * math.tan(math.radians(22)) / 0.72
        t.data.align_x = 'CENTER'; t.data.align_y = 'CENTER'
        t.data.materials.append(mat("numeral", (1, 0.96, 0.86), 0, 1, (1, 0.96, 0.86), 1.5))
        t.rotation_euler = v.to_track_quat('Z', 'Y').to_euler()
    def hero(name, at, color):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.35, depth=1.1, location=(at.x, at.y, 0.9))
        b = bpy.context.active_object; b.name = name; b.data.materials.append(mat(name + "_m", color, 0, 0.6)); made.append(b)
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.25, location=(at.x, at.y, 1.6)); bpy.context.active_object.data.materials.append(mat(name + "_m", color, 0, 0.6)); made.append(bpy.context.active_object)

    made = []
    STAKE_COL = (1.0, 0.6, 0.12); LANE_COL = (1.0, 0.32, 0.16)
    cords_vis = []; numerals = []
    # ---- Vesper Cords scene: 4 stakes at 10.5 m, 45/135/225/315 deg (Unity yaw), cords from the vessel
    vessel = U(0, 0, 0)
    stake_objs = []
    for i, yaw in enumerate((45, 135, 225, 315)):
        dx, dz = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        at = U(dx * 10.5, 0, dz * 10.5)
        s = stake.copy(); s.data = stake.data; link(s); s.location = at
        toward = (vessel - at); s.rotation_euler = (0, 0, math.atan2(toward.y, toward.x) + math.pi / 2)
        stake_objs.append(s)
        ring(f"stake{i}", at, 0, 1.6, STAKE_COL, 0.35)
        lane_end = vessel + (at - vessel).normalized() * ((at - vessel).length - 1.6 - 0.8)
        lane(f"lane{i}", vessel, lane_end, 1.4, LANE_COL)
        # cord: tiled 1 m segments, sagging from the vessel (2.0 m) to the eyelet (0.93 m)
        eye = at + (vessel - at).normalized() * (P["stake_shaft_r"] * 0.65 + P["stake_eyelet_r"] - 0.005) + Vector((0, 0, P["stake_eyelet_h"]))
        start = vessel + (at - vessel).normalized() * 0.9 + Vector((0, 0, 1.9))
        L = (eye - start).length; n = max(1, round(L))
        prev = start
        for k in range(1, n + 1):
            t = k / n
            p = start.lerp(eye, t) + Vector((0, 0, -0.45 * 4 * t * (1 - t)))
            seg = cord.copy(); seg.data = cord.data; link(seg)
            d = p - prev; seg.location = prev
            seg.rotation_euler = d.to_track_quat('-Y', 'Z').to_euler(); seg.scale = (1, d.length, 1)
            cords_vis.append(seg); prev = p
    player = U(dx * 10.5 * 0 + 7.42, 0, 7.42)   # standing on the 45 deg stake's edge
    player = U(math.sin(math.radians(45)) * 9.3, 0, math.cos(math.radians(45)) * 9.3)
    hero("hero_p1", player, (0.25, 0.45, 0.95))
    hero("hero_p2", U(-6.5, 0, 6.5), (0.85, 0.85, 0.85))
    seed.hide_render = True; stake.hide_render = True; cord.hide_render = True
    # gameplay camera on the player: + (0, 13, 9.7) Unity, looking along -offset + up
    pu = Vector((-player.x, player.z, -player.y))     # back to Unity coords
    cam_u = pu + Vector((0, 13, 9.7)); look_u = cam_u + (-Vector((0, 13, 9.7)) + Vector((0, 1, 0)))
    gc = cam_look("gameplay", U(*cam_u), U(*look_u), 44)
    numeral("1", stake_objs[0].location, gc.location)
    for so in stake_objs[1:]: numeral("1", so.location, gc.location)
    setup_render(1600, 900)
    render(gc, os.path.join(OUT, "game_cords_gameplay.png"))
    for t in numerals: t.hide_render = True
    s0 = stake_objs[0].location
    cc = cam_look("close_stake", s0 + Vector((-1.6, -2.6, 1.7)), s0 + Vector((0.3, 0.8, 0.7)), 40)
    render(cc, os.path.join(OUT, "game_cords_close.png"))
    # ---- Seedgift scene: carrier 9 m out, the seed 4.0 m over them, a 2.4 m soak circle; cords hidden
    for o in cords_vis + stake_objs + made: bpy.data.objects.remove(o)
    made.clear(); numerals.clear()
    seed.hide_render = False
    carrier = U(4.0, 0, 8.5)
    hero("carrier", carrier, (0.25, 0.45, 0.95)); hero("partner", carrier + Vector((1.2, 0.6, 0)), (0.85, 0.85, 0.85))
    seed.location = carrier + Vector((0, 0, 4.0))
    ring("seed", carrier, 0, 2.4, STAKE_COL, 0.3)
    cu = Vector((4.0, 0, 8.5)); cam_u = cu + Vector((0, 13, 9.7)); look_u = cam_u + (-Vector((0, 13, 9.7)) + Vector((0, 1, 0)))
    gc2 = cam_look("gameplay_seed", U(*cam_u), U(*look_u), 44)
    numeral("2", carrier, gc2.location)
    render(gc2, os.path.join(OUT, "game_seed_gameplay.png"))
    for t in numerals: t.hide_render = True
    cc2 = cam_look("close_seed", seed.location + Vector((-1.2, -2.4, 0.2)), seed.location + Vector((0, 0, -0.4)), 40)
    render(cc2, os.path.join(OUT, "game_seed_close.png"))

# ---------------------------------------------------------------- main
reset()
stake = build_stake(); cord = build_cord(); seed = build_seed()
stake.location = (-1.2, 0, 0); cord.location = (0, 0.5, 0.6); seed.location = (1.2, 0, 0.6)
if STAGE == "texture":
    for o in (stake, cord, seed): texture(o)
report = []
for o in (stake, cord, seed):
    loc = o.location.copy(); o.location = (0, 0, 0)
    path = os.path.join(OUT, o.name + ".glb"); export(o, path)
    report.append(f"{o.name}: {tris(o)} tris, {os.path.getsize(path) / 1024:.0f} KB, dims {tuple(round(v, 3) for v in o.dimensions)}")
    o.location = loc
open(os.path.join(OUT, "stats.txt"), "w").write("\n".join(report) + "\n")
print("\n".join(report))
if RENDERS:
    turnarounds([stake, cord, seed])
    for o in (stake, cord, seed): o.location = (0, 0, 0)
    arena_mock(stake, cord, seed)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "vesper_props.blend"))
