# Materials for the new astrolabe (remodel step 2). Run from arcanist_astrolabe_build.py (it execs this file with its
# globals: PARTS, the parts objects, the M_* placeholder materials, OUTER/MID ring numbers and OUT).
#   Brass: one 1024 atlas shared by every brass face. Cycles bakes three masks (ambient occlusion, a bevel-based edge
#     mask, engraved degree ticks on the outer and mid rings' flat faces) and a tangent normal map of the ticks; numpy
#     composes them into the base colour (clean yellow brass about hue 40, dark recesses, bright worn edges, dark tick
#     lines, a faint patina drift) and an ORM map (occlusion, roughness 0.3 polished to 0.6 in recesses, metallic 1).
#   Enamel: a painted 512 RGBA texture on the disc (planar), deep navy with gold etching and six cut-out star windows
#     (alpha mask), so the globe shows through from the front.
#   Glass: pale cyan-lilac, see-through (alpha blend). Its bright rim is a runtime shell in the game (glTF has no fresnel).
#   Gem: lilac with emission. Core: white-violet emission.
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector

TEX = os.path.join(OUT, 'textures'); os.makedirs(TEX, exist_ok=True)
ATLAS = 1024
BRASS = np.array([0.86, 0.68, 0.31])      # #DBAD4F display values: yellow brass near the arena gold and his trim (hue 38-43)
WEAR = np.array([1.0, 0.88, 0.56])        # polished edges (warm, not cream)
WEAR_STRENGTH = 0.75
RECESS = 0.60                             # base colour multiplier in full occlusion
TICK_DARK = 0.55                          # engraved lines
INNER_DARK = 0.60                         # the rings' inner faces (two-toned bands, so neighbouring rings separate)
METAL = 0.8                               # metallic in the ORM (the game may scale it)
ENAMEL = np.array([0.043, 0.063, 0.149])  # #0B1026: deep navy toward his robe, value about 0.15
ETCH = np.array([0.80, 0.62, 0.30])

sc = bpy.context.scene
brass_parts = [o for o in PARTS if any(m and m.name == 'astrolabe_brass' for m in o.data.materials)]

# ---------------------------------------------------------------- UVs: one shared atlas for every brass face
for o in bpy.data.objects: o.select_set(False)
for o in brass_parts: o.select_set(True)
bpy.context.view_layer.objects.active = brass_parts[0]
bpy.ops.object.mode_set(mode='EDIT')
for o in brass_parts:
    bm = bmesh.from_edit_mesh(o.data)
    bi = [i for i, m in enumerate(o.data.materials) if m and m.name == 'astrolabe_brass']
    for f in bm.faces: f.select = f.material_index in bi
    bmesh.update_edit_mesh(o.data)
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, correct_aspect=True, scale_to_bounds=False)
try: bpy.ops.uv.pack_islands(margin=0.003, rotate=True, shape_method='CONCAVE')
except TypeError:
    try: bpy.ops.uv.pack_islands(margin=0.003, rotate=True)
    except TypeError: bpy.ops.uv.pack_islands(margin=0.003)
bpy.ops.object.mode_set(mode='OBJECT')
# enamel faces: planar UVs over the disc (x, z), so the painted windows land where they should
disc_o = bpy.data.objects['Astro_Disc']; me = disc_o.data
ei = [i for i, m in enumerate(me.materials) if m and m.name == 'astrolabe_enamel']
uvl = me.uv_layers.active.data; SPAN = 0.13
for p in me.polygons:
    if p.material_index in ei:
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co; uvl[li].uv = (v.x / SPAN + 0.5, v.z / SPAN + 0.5)

# ---------------------------------------------------------------- bake masks with Cycles
sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.device = 'CPU'
sc.render.bake.margin = 8; sc.render.bake.use_clear = True
if not sc.world: sc.world = bpy.data.worlds.new('BakeWorld')
sc.world.light_settings.distance = 0.02
for o in PARTS:
    o['ticks'] = 0.0; o['tick_rout'] = 0.0; o['tick_w'] = 0.0
for name in ('Astro_RingOuter', 'Astro_RingMid', 'Astro_RingInner'): bpy.data.objects[name]['ring'] = 1.0
for o in PARTS:
    if 'ring' not in o: o['ring'] = 0.0
for name, R in (('Astro_RingOuter', OUTER), ('Astro_RingMid', MID)):
    o = bpy.data.objects[name]; o['ticks'] = 1.0; o['tick_rout'] = R['r'] + R['w'] / 2; o['tick_w'] = R['w']

def new_img(name, size, data=True, alpha=False):
    im = bpy.data.images.new(name, size, size, alpha=alpha, float_buffer=False)
    if data: im.colorspace_settings.name = 'Non-Color'
    return im
dummy = new_img('bake_dummy', 16)

def node(nt, kind, **kw):
    n = nt.nodes.new(kind)
    for k, v in kw.items():
        if k == 'op': n.operation = v
        elif k == 'inputs':
            for i, val in v.items(): n.inputs[i].default_value = val
        else: setattr(n, k, v)
    return n
def math_(nt, op, a, b=None, clamp=False):
    n = node(nt, 'ShaderNodeMath', op=op); n.use_clamp = clamp
    for i, x in enumerate((a, b)):
        if x is None: continue
        if isinstance(x, (int, float)): n.inputs[i].default_value = x
        else: nt.links.new(x, n.inputs[i])
    return n.outputs[0]

def bake_material(kind):
    # a temporary material for the brass faces whose emission is one mask, or (kind 'normal') whose bump is the ticks
    m = bpy.data.materials.new('bake_' + kind); m.use_nodes = True; nt = m.node_tree
    out = nt.nodes['Material Output']; bsdf = nt.nodes['Principled BSDF']
    tc = node(nt, 'ShaderNodeTexCoord'); sep = node(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Object'], sep.inputs[0])
    x, y, z = sep.outputs['X'], sep.outputs['Y'], sep.outputs['Z']
    # ticks on the rings' flat faces: every 5 deg a short line, every 30 deg a long one, from the outer edge inward
    attr = lambda nm: node(nt, 'ShaderNodeAttribute', attribute_type='OBJECT', attribute_name=nm).outputs['Fac']
    on, rout, w = attr('ticks'), attr('tick_rout'), attr('tick_w')
    rho = math_(nt, 'SQRT', math_(nt, 'ADD', math_(nt, 'MULTIPLY', x, x), math_(nt, 'MULTIPLY', z, z)))
    ang = math_(nt, 'MULTIPLY', math_(nt, 'ARCTAN2', z, x), 180 / math.pi)
    def near(step):
        f = math_(nt, 'SUBTRACT', math_(nt, 'FRACT', math_(nt, 'ADD', math_(nt, 'DIVIDE', ang, step), 0.5)), 0.5)
        dist = math_(nt, 'MULTIPLY', math_(nt, 'MULTIPLY', math_(nt, 'ABSOLUTE', f), step * math.pi / 180), rho)   # arc distance (m)
        return math_(nt, 'LESS_THAN', dist, 0.0006)
    din = math_(nt, 'SUBTRACT', rout, rho)
    short = math_(nt, 'MULTIPLY', near(5.0), math_(nt, 'LESS_THAN', din, math_(nt, 'MULTIPLY', w, 0.38)))
    long_ = math_(nt, 'MULTIPLY', near(30.0), math_(nt, 'LESS_THAN', din, math_(nt, 'MULTIPLY', w, 0.78)))
    flat = math_(nt, 'GREATER_THAN', math_(nt, 'ABSOLUTE', y), 0.0045)   # only near the flat faces (|y| ~ d/2)
    face_ok = math_(nt, 'MULTIPLY', flat, math_(nt, 'GREATER_THAN', din, 0.0015))
    tick = math_(nt, 'MULTIPLY', math_(nt, 'MULTIPLY', math_(nt, 'MAXIMUM', short, long_), face_ok), on)
    if kind == 'normal':
        bump = node(nt, 'ShaderNodeBump', inputs={'Strength': 1.0, 'Distance': 0.0006}); bump.invert = True
        nt.links.new(tick, bump.inputs['Height']); nt.links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])
        return m
    em = node(nt, 'ShaderNodeEmission'); nt.links.new(em.outputs[0], out.inputs['Surface'])
    if kind == 'ticks': nt.links.new(tick, em.inputs['Color'])
    elif kind == 'inner':
        # object-space normal facing the ring's centre (the band's inner face): clamp((dot(N, -radial) - 0.5) * 4)
        sn = node(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Normal'], sn.inputs[0])
        nx, nz = sn.outputs['X'], sn.outputs['Z']
        rr = math_(nt, 'MAXIMUM', rho, 1e-5)
        dotv = math_(nt, 'ADD', math_(nt, 'MULTIPLY', nx, math_(nt, 'DIVIDE', x, rr)), math_(nt, 'MULTIPLY', nz, math_(nt, 'DIVIDE', z, rr)))
        inner_s = math_(nt, 'MULTIPLY', math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', math_(nt, 'MULTIPLY', dotv, -1.0), 0.5), 4.0, clamp=True), attr('ring'))
        nt.links.new(inner_s, em.inputs['Color'])
    elif kind == 'edge':
        bev = node(nt, 'ShaderNodeBevel', samples=64); bev.inputs['Radius'].default_value = 0.0025
        geo = node(nt, 'ShaderNodeNewGeometry')
        dp = node(nt, 'ShaderNodeVectorMath', op='DOT_PRODUCT'); nt.links.new(bev.outputs[0], dp.inputs[0]); nt.links.new(geo.outputs['Normal'], dp.inputs[1])
        e = math_(nt, 'MULTIPLY', math_(nt, 'SUBTRACT', 1.0, dp.outputs['Value']), 9.0, clamp=True)
        nt.links.new(e, em.inputs['Color'])
    return m

def bake(kind, img, btype):
    # every brass face renders into `img` through a temporary material; other faces go to a dummy image
    saved = {}
    bm_ = bake_material(kind) if kind else None
    for o in brass_parts:
        saved[o] = list(o.data.materials)
        for i, m in enumerate(o.data.materials):
            if m and m.name == 'astrolabe_brass':
                tgt = bm_ if bm_ else m
                o.data.materials[i] = tgt
            else:
                tgt = m
            nt = tgt.node_tree; t = nt.nodes.get('BAKE_TARGET') or nt.nodes.new('ShaderNodeTexImage'); t.name = 'BAKE_TARGET'
            t.image = img if (m and m.name == 'astrolabe_brass') else dummy; nt.nodes.active = t
    for o in bpy.data.objects: o.select_set(o in brass_parts)   # only parts with brass faces bake (the rest still occlude)
    bpy.context.view_layer.objects.active = brass_parts[0]
    kw = dict(type=btype, margin=8, use_clear=True)
    if btype == 'NORMAL': kw.update(normal_space='TANGENT')
    bpy.ops.object.bake(**kw)
    for o, ms in saved.items():
        for i, m in enumerate(ms): o.data.materials[i] = m
    for m in bpy.data.materials:
        if m.node_tree and m.node_tree.nodes.get('BAKE_TARGET'): m.node_tree.nodes.remove(m.node_tree.nodes['BAKE_TARGET'])
    return np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)

# the brass faces need a real material for the AO and normal bakes: use the placeholder brass
ao = bake(None, new_img('bake_ao', ATLAS), 'AO')[..., 0]
edge = bake('edge', new_img('bake_edge', ATLAS), 'EMIT')[..., 0]
ticks = bake('ticks', new_img('bake_ticks', ATLAS), 'EMIT')[..., 0]
inner_m = bake('inner', new_img('bake_inner', ATLAS), 'EMIT')[..., 0]
print('BAKED ao', round(float(ao.mean()), 3), 'edge', round(float(edge.mean()), 3), 'ticks', round(float(ticks.mean()), 4), 'inner', round(float(inner_m.mean()), 3))

# ---------------------------------------------------------------- compose
def save(name, arr, fmt='JPEG', data=False):
    h, w = arr.shape[:2]; im = bpy.data.images.new(name, w, h, alpha=(arr.shape[2] == 4 and fmt == 'PNG'))
    if data: im.colorspace_settings.name = 'Non-Color'
    rgba = np.ones((h, w, 4), np.float32); rgba[..., :arr.shape[2]] = arr
    im.pixels.foreach_set(rgba.ravel()); ext = '.jpg' if fmt == 'JPEG' else '.png'
    im.filepath_raw = os.path.join(TEX, name + ext); im.file_format = fmt
    if fmt == 'JPEG': sc.render.image_settings.quality = 92
    im.save(); im.reload(); return im
rng = np.random.default_rng(7)
yy, xx = np.mgrid[0:ATLAS, 0:ATLAS] / ATLAS
patina = 1 + 0.05 * np.sin(xx * 9.1 + np.sin(yy * 7.3) * 2) * np.cos(yy * 6.7 + xx * 3.1)
aoc = np.clip((ao - 0.35) / 0.65, 0, 1)
base = BRASS[None, None, :] * (RECESS + (1 - RECESS) * aoc[..., None]) * patina[..., None]
base *= (1 - TICK_DARK * ticks[..., None]) * (1 - INNER_DARK * inner_m[..., None])
def blur1(a):   # a 3x3 box blur (about sigma 1 px)
    return sum(np.roll(np.roll(a, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9
def dilate(a, r):
    out = a.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1): out = np.maximum(out, np.roll(np.roll(a, dy, 0), dx, 1))
    return out
eb = np.clip(blur1(edge), 0, 1); wear = WEAR_STRENGTH * eb * eb * (3 - 2 * eb)   # smoothstep-ish ramp
wear = np.clip((eb - 0.30) / 0.50, 0, 1); wear = WEAR_STRENGTH * wear * wear * (3 - 2 * wear) * (1 - dilate(ticks, 2))   # no wear on the engraving
base = base + (WEAR[None, None, :] - base) * wear[..., None]
base_img = save('astrolabe_brass_basecolor', np.clip(base, 0, 1))
rough = np.clip(0.30 + 0.30 * (1 - aoc) + 0.12 * ticks + 0.15 * inner_m - 0.15 * wear, 0.15, 0.8)
orm = np.stack([np.clip(0.4 + 0.6 * aoc, 0, 1), rough, np.full_like(rough, METAL)], -1)
orm_img = save('astrolabe_brass_orm', orm, fmt='PNG', data=True)   # PNG: the occlusion channel survives
# normal map from the tick mask (engraved: the ticks are a 0.4 mm-deep groove), Sobel in UV space
hgt = -ticks.astype(np.float32)
gx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5; gy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * 0.5
K = 2.0; n = np.dstack([-gx * K, -gy * K, np.ones_like(gx)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
nrm_img = save('astrolabe_brass_normal', n * 0.5 + 0.5, fmt='PNG', data=True)   # PNG: no JPEG blocks in the grooves

# enamel: deep navy, gold etching (a circle and 12 radial ticks), six cut-out four-point star windows
E = 512; ys, xs = np.mgrid[0:E, 0:E]; px = (xs + 0.5) / E - 0.5; pz = (ys + 0.5) / E - 0.5
mx, mz = px * SPAN, pz * SPAN; r = np.hypot(mx, mz); a = np.arctan2(mz, mx)
col = np.ones((E, E, 3)) * ENAMEL; alpha = np.ones((E, E))
etch = (np.abs(r - 0.054) < 0.0007) | ((np.abs(((a / (2 * np.pi / 12)) + 0.5) % 1 - 0.5) * r * 2 * np.pi / 12 < 0.0005) & (r > 0.021) & (r < 0.027))
col[etch] = ETCH
for k in range(6):
    ang = 2 * np.pi * k / 6 + np.pi / 6; cx, cz = 0.039 * np.cos(ang), 0.039 * np.sin(ang)
    dx, dz = np.abs(mx - cx), np.abs(mz - cz); S = 0.0105
    star = (np.sqrt(dx / S) + np.sqrt(dz / S)) < 1.0
    rim = ((np.sqrt(dx / (S * 1.25)) + np.sqrt(dz / (S * 1.25))) < 1.0) & ~star
    alpha[star] = 0.0; col[rim] = ETCH
enamel_img = save('astrolabe_enamel', np.dstack([col, alpha]), fmt='PNG')

# ---------------------------------------------------------------- final materials (glTF exporter friendly)
def principled(m):
    m.node_tree.nodes.clear(); nt = m.node_tree
    out = nt.nodes.new('ShaderNodeOutputMaterial'); b = nt.nodes.new('ShaderNodeBsdfPrincipled'); nt.links.new(b.outputs[0], out.inputs[0])
    return nt, b
nt, b = principled(M_BRASS)
tb = nt.nodes.new('ShaderNodeTexImage'); tb.image = base_img; nt.links.new(tb.outputs['Color'], b.inputs['Base Color'])
to = nt.nodes.new('ShaderNodeTexImage'); to.image = orm_img; sp = nt.nodes.new('ShaderNodeSeparateColor')
nt.links.new(to.outputs['Color'], sp.inputs[0]); nt.links.new(sp.outputs['Green'], b.inputs['Roughness']); nt.links.new(sp.outputs['Blue'], b.inputs['Metallic'])
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = nrm_img; nm = nt.nodes.new('ShaderNodeNormalMap')
nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], b.inputs['Normal'])
nt, b = principled(M_ENAMEL)
te = nt.nodes.new('ShaderNodeTexImage'); te.image = enamel_img; nt.links.new(te.outputs['Color'], b.inputs['Base Color'])
rnd = nt.nodes.new('ShaderNodeMath'); rnd.operation = 'ROUND'; nt.links.new(te.outputs['Alpha'], rnd.inputs[0]); nt.links.new(rnd.outputs[0], b.inputs['Alpha'])   # alpha mask (cutoff 0.5)
b.inputs['Roughness'].default_value = 0.15
try: M_ENAMEL.surface_render_method = 'DITHERED'
except Exception: M_ENAMEL.blend_method = 'CLIP'
nt, b = principled(M_GLASS)
b.inputs['Base Color'].default_value = (0.30, 0.52, 1.0, 1); b.inputs['Roughness'].default_value = 0.05; b.inputs['Alpha'].default_value = 0.40
b.inputs['Emission Color'].default_value = (0.12, 0.25, 0.85, 1); b.inputs['Emission Strength'].default_value = 0.5
M_GLASS.use_backface_culling = True   # single-sided: an alpha-blended double-sided sphere sorts wrongly in URP
try: M_GLASS.surface_render_method = 'BLENDED'
except Exception: M_GLASS.blend_method = 'BLEND'
nt, b = principled(M_GEM)
b.inputs['Base Color'].default_value = (0.28, 0.12, 0.85, 1); b.inputs['Roughness'].default_value = 0.30
b.inputs['Emission Color'].default_value = (0.42, 0.16, 1.0, 1); b.inputs['Emission Strength'].default_value = 0.7
nt, b = principled(M_CORE)
b.inputs['Base Color'].default_value = (1, 1, 1, 1); b.inputs['Emission Color'].default_value = (1.0, 0.93, 1.0, 1); b.inputs['Emission Strength'].default_value = 9.0
print('MATERIALS done')
