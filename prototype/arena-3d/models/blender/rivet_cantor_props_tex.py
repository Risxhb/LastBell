# Texture pass for rivet_cantor_props.py (mode "tex"): procedural Brass Foundry materials, UV unwrap, and a Cycles bake to
# 1k maps per object (base colour, metallic-roughness, tangent normal, emission), then one baked glTF material per object.
# Palette (prompts.md): foundry brass #C79A4C / worn #E8C27A / cavity #5A3E1C, blackened iron #2A2724, ember #FF7A2A,
# cobalt enamel #28366E; floor slate #2E3033. Never the pale coral / ivory of the telegraphs.
import bpy, math, os
import numpy as np

def srgb(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

BRASS, WORN, CAVITY = srgb('C79A4C'), srgb('E8C27A'), srgb('5A3E1C')
IRON, RUST = srgb('2A2724'), srgb('4A2E1E')
EMBER = srgb('FF6A1A'); COBALT = srgb('1F2A55'); SLATE = srgb('2E3033')
P = {}   # tunables, edited between texture rounds

def _n(nt, kind, **kw):
    n = nt.nodes.new(kind)
    for k, v in kw.items(): setattr(n, k, v)
    return n

def _rgb_mix(nt, fac, a, b):
    m = nt.nodes.new('ShaderNodeMix'); m.data_type = 'RGBA'; m.blend_type = 'MIX'
    nt.links.new(fac, m.inputs['Factor'])
    for sock, v in ((m.inputs[6], a), (m.inputs[7], b)):
        if isinstance(v, tuple): sock.default_value = (*v, 1)
        else: nt.links.new(v, sock)
    return m.outputs[2]

def _ramp(nt, src, stops):
    r = nt.nodes.new('ShaderNodeValToRGB'); nt.links.new(src, r.inputs[0])
    el = r.color_ramp.elements
    while len(el) > 2: el.remove(el[-1])
    for i, (pos, col) in enumerate(stops):
        e = el[i] if i < 2 else el.new(pos); e.position = pos; e.color = (*col, 1) if len(col) == 3 else col
    return r.outputs[0]

def _noise(nt, scale, detail=4.0, rough=0.55, coord=None):
    n = _n(nt, 'ShaderNodeTexNoise'); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    n.inputs['Roughness'].default_value = rough
    if coord is not None: nt.links.new(coord, n.inputs['Vector'])
    return n.outputs['Fac']

def _vmath(nt, op, a, b=None):
    n = nt.nodes.new('ShaderNodeVectorMath'); n.operation = op
    for i, v in enumerate((a, b)):
        if v is None: continue
        if isinstance(v, tuple): n.inputs[i].default_value = v
        else: nt.links.new(v, n.inputs[i])
    return n.outputs['Value'] if op in ('DOT_PRODUCT', 'LENGTH') else n.outputs['Vector']

def _math(nt, op, a, b):
    n = nt.nodes.new('ShaderNodeMath'); n.operation = op
    for i, v in enumerate((a, b)):
        if isinstance(v, (int, float)): n.inputs[i].default_value = v
        else: nt.links.new(v, n.inputs[i])
    return n.outputs[0]

def _metal(name, base, worn, cavity, rough=(0.30, 0.40), cav_rough=0.55, metal=1.0, bump=0.35, peen=True, grime=None,
           inner=None, rub=None, edge=None):
    """inner = (centre, mask, colour): faces whose normal points back toward the centre (masked axes) get the dark
    interior colour and roughness 0.7. rub = (half-length, colour): bright polished rub bands at the ends of a link."""
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
    b = nt.nodes['Principled BSDF']
    tc = _n(nt, 'ShaderNodeTexCoord').outputs['Object']
    ao = _n(nt, 'ShaderNodeAmbientOcclusion'); ao.inputs['Distance'].default_value = P.get('ao_dist', 0.08); ao.samples = 16
    geo = _n(nt, 'ShaderNodeNewGeometry')
    mott = _noise(nt, 6.0, 6, 0.6, tc)
    col = _rgb_mix(nt, _ramp(nt, mott, [(0.35, (0, 0, 0)), (0.75, (1, 1, 1))]), tuple(c * 0.8 for c in base), base)
    cav = _ramp(nt, ao.outputs['AO'], [(0.65, (1, 1, 1)), (0.985, (0, 0, 0))])
    col = _rgb_mix(nt, cav, col, cavity)
    # edge = (lo, hi) pointiness range for edge wear (small cylinders read as all-edge at the default: mallet texture R2)
    lo, hi = edge if edge else (P.get('edge_lo', 0.52), P.get('edge_hi', 0.6))
    edge = _ramp(nt, geo.outputs['Pointiness'], [(lo, (0, 0, 0)), (hi, (1, 1, 1))])
    col = _rgb_mix(nt, edge, col, worn)
    if grime is not None:
        g = _ramp(nt, _noise(nt, 2.5, 3, 0.5, tc), [(0.5, (0, 0, 0)), (0.7, (1, 1, 1))])
        col = _rgb_mix(nt, g, col, grime)
    r = _ramp(nt, _noise(nt, 11.0, 4, 0.6, tc), [(0.3, (rough[0],) * 3), (0.75, (rough[1],) * 3)])
    r = _rgb_mix(nt, cav, r, (cav_rough,) * 3)
    r = _rgb_mix(nt, edge, r, (rough[0] * 0.6,) * 3)   # worn edges are polished
    if inner is not None:
        c, mask, icol = inner
        d = _vmath(nt, 'NORMALIZE', _vmath(nt, 'MULTIPLY', _vmath(nt, 'SUBTRACT', tc, c), mask))
        dot = _vmath(nt, 'DOT_PRODUCT', geo.outputs['Normal'], d)
        fac = _ramp(nt, dot, [(-0.7, (1, 1, 1)), (-0.3, (0, 0, 0))])
        col = _rgb_mix(nt, fac, col, icol); r = _rgb_mix(nt, fac, r, (0.7,) * 3)
        if rub is not None:
            half, rcol = rub
            sep = _n(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(tc, sep.inputs[0])
            ay = _math(nt, 'ABSOLUTE', sep.outputs['Y'], 0)
            ends = _ramp(nt, ay, [(half - 0.07, (0, 0, 0)), (half - 0.03, (1, 1, 1))])
            outw = _ramp(nt, dot, [(0.05, (0, 0, 0)), (0.4, (1, 1, 1))])
            mul = _n(nt, 'ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1
            nt.links.new(ends, mul.inputs[6]); nt.links.new(outw, mul.inputs[7])
            col = _rgb_mix(nt, mul.outputs[2], col, rcol); r = _rgb_mix(nt, mul.outputs[2], r, (0.2,) * 3)
    nt.links.new(col, b.inputs['Base Color'])
    nt.links.new(r, b.inputs['Roughness'])
    b.inputs['Metallic'].default_value = metal
    bp = _n(nt, 'ShaderNodeBump'); bp.inputs['Strength'].default_value = bump; bp.inputs['Distance'].default_value = 0.02
    vor = _n(nt, 'ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 26.0 if peen else 60.0; nt.links.new(tc, vor.inputs['Vector'])
    scr = _n(nt, 'ShaderNodeTexWave'); scr.inputs['Scale'].default_value = 40.0; scr.inputs['Distortion'].default_value = 12.0
    nt.links.new(tc, scr.inputs['Vector'])
    h = _math(nt, 'ADD', vor.outputs['Distance'], _math(nt, 'MULTIPLY', scr.outputs['Fac'], 0.08))
    nt.links.new(h, bp.inputs['Height']); nt.links.new(bp.outputs['Normal'], b.inputs['Normal'])
    return m

def _enamel():
    m = bpy.data.materials.new('enamel'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = _n(nt, 'ShaderNodeTexCoord').outputs['Object']
    chips = _ramp(nt, _noise(nt, 30.0, 5, 0.7, tc), [(0.66, (0, 0, 0)), (0.7, (1, 1, 1))])
    col = _rgb_mix(nt, chips, COBALT, BRASS)
    nt.links.new(col, b.inputs['Base Color'])
    met = _ramp(nt, _noise(nt, 30.0, 5, 0.7, tc), [(0.66, (0, 0, 0)), (0.7, (1, 1, 1))]); nt.links.new(met, b.inputs['Metallic'])
    b.inputs['Roughness'].default_value = 0.35
    return m

def _slate():
    # torn floor plate (texture round 2): dark slate on top (roughness 0.75, not metal), dark brass #6B4A22 underneath;
    # the glow lives only on the torn lip faces (the "ember" material)
    m = bpy.data.materials.new('slate'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = _n(nt, 'ShaderNodeTexCoord').outputs['Object']
    geo = _n(nt, 'ShaderNodeNewGeometry')
    nrm = _n(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(geo.outputs['Normal'], nrm.inputs[0])
    under = _ramp(nt, nrm.outputs['Z'], [(-0.2, (1, 1, 1)), (0.2, (0, 0, 0))])
    top = _rgb_mix(nt, _ramp(nt, _noise(nt, 8.0, 5, 0.6, tc), [(0.3, (0, 0, 0)), (0.7, (1, 1, 1))]), tuple(c * 0.75 for c in SLATE), SLATE)
    edge = _ramp(nt, geo.outputs['Pointiness'], [(0.53, (0, 0, 0)), (0.62, (1, 1, 1))])
    top = _rgb_mix(nt, edge, top, srgb('1E1C1A'))   # scorched, chipped plate edges
    sx = _n(nt, 'ShaderNodeSeparateXYZ'); nt.links.new(tc, sx.inputs[0])
    inlay = _ramp(nt, _math(nt, 'ABSOLUTE', _math(nt, 'SUBTRACT', sx.outputs['X'], 0.12), 0), [(0.012, (1, 1, 1)), (0.02, (0, 0, 0))])
    top = _rgb_mix(nt, inlay, top, srgb('8A6A30'))   # a sliver of the floor's brass inlay line, torn with the plate
    grain = _n(nt, 'ShaderNodeTexWave'); grain.inputs['Scale'].default_value = 9.0; grain.inputs['Distortion'].default_value = 6.0; nt.links.new(tc, grain.inputs['Vector'])
    gb = _n(nt, 'ShaderNodeBump'); gb.inputs['Strength'].default_value = 0.25; gb.inputs['Distance'].default_value = 0.02
    nt.links.new(_math(nt, 'ADD', grain.outputs['Fac'], _math(nt, 'MULTIPLY', _noise(nt, 30.0, 4, 0.6, tc), 0.6)), gb.inputs['Height']); nt.links.new(gb.outputs['Normal'], b.inputs['Normal'])
    col = _rgb_mix(nt, under, top, srgb('6B4A22'))
    nt.links.new(col, b.inputs['Base Color'])
    nt.links.new(_rgb_mix(nt, under, (0.75,) * 3, (0.45,) * 3), b.inputs['Roughness'])
    nt.links.new(_rgb_mix(nt, under, (0.0,) * 3, (0.9,) * 3), b.inputs['Metallic'])
    return m

def _ember():
    # the torn lip: hot #FF7A2A grading to #C23A0A, broken up by noise (thin: only the 4.5 cm lip faces carry it)
    m = bpy.data.materials.new('ember'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    tc = _n(nt, 'ShaderNodeTexCoord').outputs['Object']
    g = _ramp(nt, _noise(nt, 18.0, 4, 0.55, tc), [(0.35, srgb('C23A0A')), (0.65, EMBER)])
    nt.links.new(g, b.inputs['Base Color']); nt.links.new(g, b.inputs['Emission Color'])
    b.inputs['Emission Strength'].default_value = 1.0; b.inputs['Roughness'].default_value = 0.6
    return m

def materials():
    return {
        'brass': _metal('brass', BRASS, WORN, CAVITY, rough=(0.30, 0.40), cav_rough=0.55, bump=P.get('brass_bump', 0.35)),
        'brass_link': _metal('brass_link', BRASS, WORN, CAVITY, rough=(0.30, 0.42), bump=0.07, peen=False,
                             inner=((0, 0, 0), (1, 1, 0), srgb('3E2C14')), rub=(0.21, srgb('E8C27A'))),
        # the shackle's bow interior faces back toward the bow's centre (x 0, z about -0.29 below the eye)
        'brass_shackle': _metal('brass_shackle', BRASS, WORN, CAVITY, rough=(0.30, 0.42), bump=0.2, peen=False,
                                inner=((0, 0, -0.29), (1, 0, 1), CAVITY)),
        'polish': _metal('polish', WORN, tuple(min(1, c * 1.15) for c in WORN), tuple(c * 0.92 for c in WORN), rough=(0.15, 0.2), cav_rough=0.2, bump=0.05, peen=False),
        'iron': _metal('iron', IRON, tuple(c * 2.2 for c in IRON), tuple(c * 0.5 for c in IRON), rough=(0.5, 0.6), cav_rough=0.7, metal=0.8, bump=0.2, peen=False, grime=RUST),
        'slate': _slate(),
        'ember': _ember(),
        'enamel': _enamel(),
    }

# ---------------------------------------------------------------- bake
def _unwrap(ob):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.012, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.006, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')

def _surface_from(mat, socket_name, value_fallback=None):
    """Temporarily route one Principled input to an Emission shader (the bake-emit trick). Returns an undo function."""
    nt = mat.node_tree; b = nt.nodes['Principled BSDF']; out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
    em = nt.nodes.new('ShaderNodeEmission'); src = b.inputs[socket_name]
    if src.is_linked: nt.links.new(src.links[0].from_socket, em.inputs['Color'])
    else:
        v = src.default_value
        if socket_name == "Emission Color": v = (0.0, 0.0, 0.0, 1.0)   # unlinked = no glow (the default colour is white)
        em.inputs['Color'].default_value = (v, v, v, 1) if isinstance(v, float) else tuple(v)
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    def undo():
        nt.links.new(b.outputs[0], out.inputs['Surface']); nt.nodes.remove(em)
    return undo

def _target(mats, img):
    nodes = []
    for m in mats:
        n = m.node_tree.nodes.new('ShaderNodeTexImage'); n.image = img
        m.node_tree.nodes.active = n; nodes.append((m, n))
    return lambda: [m.node_tree.nodes.remove(n) for m, n in nodes]

def _bake_pass(ob, mats, img, kind, socket=None):
    drop = _target(mats, img)
    undos = [_surface_from(m, socket) for m in mats] if kind == 'EMIT' else []
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    if kind == 'NORMAL': bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=6, use_clear=True)
    else: bpy.ops.object.bake(type='EMIT', margin=6, use_clear=True)
    for u in undos: u()
    drop()

def _img(name, res, data=False):
    im = bpy.data.images.new(name, res, res, alpha=False, float_buffer=False)
    if data: im.colorspace_settings.name = 'Non-Color'
    return im

def bake(assets, out, res):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 24
    sc.render.bake.margin = 6
    for name, objs in assets.items():
        for ob in objs:
            _unwrap(ob)
            mats = [s.material for s in ob.material_slots if s.material]
            mats = list(dict.fromkeys(mats))
            stem = f'{name}_{ob.name}' if len(objs) > 1 else name
            base = _img(stem + '_base', res); rough = _img(stem + '_rough', res, True); metal = _img(stem + '_metal', res, True)
            nrm = _img(stem + '_normal', res, True); emit = _img(stem + '_emit', res)
            _bake_pass(ob, mats, base, 'EMIT', 'Base Color')
            _bake_pass(ob, mats, rough, 'EMIT', 'Roughness')
            _bake_pass(ob, mats, metal, 'EMIT', 'Metallic')
            _bake_pass(ob, mats, nrm, 'NORMAL')
            has_emit = any(m.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].is_linked for m in mats)
            if has_emit: _bake_pass(ob, mats, emit, 'EMIT', 'Emission Color')
            # glTF metallicRoughness: G = roughness, B = metallic
            px = res * res
            r = np.empty(px * 4, np.float32); rough.pixels.foreach_get(r)
            mt = np.empty(px * 4, np.float32); metal.pixels.foreach_get(mt)
            mr = np.ones(px * 4, np.float32); mr[1::4] = r[0::4]; mr[2::4] = mt[0::4]; mr[0::4] = 1.0
            mrim = _img(stem + '_mr', res, True); mrim.pixels.foreach_set(mr)
            for im in [base, mrim, nrm] + ([emit] if has_emit else []):
                im.filepath_raw = os.path.join(out, im.name + '.png'); im.file_format = 'PNG'; im.save()
            _final_material(ob, stem, base, mrim, nrm, emit if has_emit else None)

def _final_material(ob, stem, base, mr, nrm, emit):
    m = bpy.data.materials.new(stem + '_mat'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']
    def tex(im):
        n = nt.nodes.new('ShaderNodeTexImage'); n.image = im; return n
    nt.links.new(tex(base).outputs['Color'], b.inputs['Base Color'])
    sep = nt.nodes.new('ShaderNodeSeparateColor'); nt.links.new(tex(mr).outputs['Color'], sep.inputs[0])
    nt.links.new(sep.outputs['Green'], b.inputs['Roughness']); nt.links.new(sep.outputs['Blue'], b.inputs['Metallic'])
    nm = nt.nodes.new('ShaderNodeNormalMap'); nt.links.new(tex(nrm).outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs[0], b.inputs['Normal'])
    if emit is not None:
        nt.links.new(tex(emit).outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = P.get('ember_strength', 2.5)
    ob.data.materials.clear(); ob.data.materials.append(m)
    for p in ob.data.polygons: p.material_index = 0
