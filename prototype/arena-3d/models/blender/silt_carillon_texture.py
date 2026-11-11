# Texture pass for the Silt Carillon art (miniboss art pass, 9 Nov 2026), imported by silt_carillon_build.py / _props.py
# with --textured. Smart-UV-projects the joined mesh into one atlas, swaps each flat material for a procedural one
# (object-space noise, so UV seams never show), bakes base colour (x baked AO), an ORM map (R occlusion, G roughness,
# B metallic, as glTF reads it) and emission with Cycles, then replaces every slot with one baked material.
# Sizes: body 2048 base / 1024 ORM / 512 emissive; props 1024 / 512 / 256. Tunables live in LOOK (round 2 edits them).
import bpy, os, math
import numpy as np

LOOK = {
    # name: (base colour A, base colour B, noise scale, roughness, metallic)
    'stone': ((0.76, 0.73, 0.60), (0.66, 0.63, 0.51), 1.4, 0.85, 0.0),
    'stone_dark': ((0.52, 0.49, 0.40), (0.40, 0.37, 0.30), 1.8, 0.9, 0.0),
    'moss': ((0.13, 0.25, 0.08), (0.06, 0.14, 0.04), 6.0, 0.95, 0.0),
    'moss_dark': ((0.07, 0.15, 0.05), (0.03, 0.08, 0.03), 6.0, 0.95, 0.0),
    'silt': ((0.15, 0.13, 0.095), (0.095, 0.08, 0.06), 2.5, 0.72, 0.0),
    'silt_wet': ((0.085, 0.074, 0.056), (0.05, 0.044, 0.034), 3.0, 0.45, 0.0),
    'verdigris': ((0.27, 0.50, 0.41), (0.17, 0.35, 0.28), 4.0, 0.5, 0.35),
    'bronze': ((0.34, 0.20, 0.07), (0.20, 0.12, 0.05), 8.0, 0.4, 0.9),
    'reed': ((0.20, 0.21, 0.08), (0.10, 0.11, 0.04), 9.0, 0.9, 0.0),
    'eye': ((0.05, 0.16, 0.55), (0.03, 0.10, 0.40), 5.0, 0.3, 0.0),
    'crown': ((0.89, 0.60, 0.17), (0.66, 0.42, 0.10), 6.0, 0.35, 0.7),
    'mouth': ((0.03, 0.08, 0.07), (0.02, 0.05, 0.04), 5.0, 0.6, 0.0),
    'fleck': ((0.78, 0.75, 0.66), (0.66, 0.63, 0.55), 9.0, 0.9, 0.0),
}
EMIT = {'eye': (0.035, 0.13, 0.48), 'crown': (0.45, 0.29, 0.07), 'mouth': (0.10, 0.65, 0.48)}
# weathering: silt streaks run down the stone and the lower stone is silted (z below SILT_LINE, metres)
SILT_LINE = 0.88; STREAK = 0.54

def _proc(mat_name):
    key = mat_name.replace('silt_', '', 1) if mat_name.startswith('silt_') else mat_name
    return key if key in LOOK else None

def _build_proc(m, key, zmax):
    a, b, sc, rough, metal = LOOK[key]
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear(); N = nt.nodes.new; L = nt.links.new
    out = N('ShaderNodeOutputMaterial'); bsdf = N('ShaderNodeBsdfPrincipled'); L(bsdf.outputs[0], out.inputs[0])
    tc = N('ShaderNodeTexCoord')
    sep = N('ShaderNodeSeparateXYZ'); L(tc.outputs['Object'], sep.inputs[0])
    # large mottling + fine grain
    n1 = N('ShaderNodeTexNoise'); n1.inputs['Scale'].default_value = sc; n1.inputs['Detail'].default_value = 6; L(tc.outputs['Object'], n1.inputs['Vector'])
    n2 = N('ShaderNodeTexNoise'); n2.inputs['Scale'].default_value = sc * 6; n2.inputs['Detail'].default_value = 3; L(tc.outputs['Object'], n2.inputs['Vector'])
    ramp = N('ShaderNodeValToRGB'); ramp.color_ramp.elements[0].color = (*b, 1); ramp.color_ramp.elements[1].color = (*a, 1)
    ramp.color_ramp.elements[0].position = 0.3; ramp.color_ramp.elements[1].position = 0.7
    mix_in = N('ShaderNodeMath'); mix_in.operation = 'MULTIPLY_ADD'; mix_in.inputs[1].default_value = 0.8
    g2 = N('ShaderNodeMath'); g2.operation = 'MULTIPLY'; g2.inputs[1].default_value = 0.08; L(n2.outputs['Fac'], g2.inputs[0])
    L(n1.outputs['Fac'], mix_in.inputs[0]); L(g2.outputs[0], mix_in.inputs[2]); L(mix_in.outputs[0], ramp.inputs[0])
    col = ramp.outputs[0]
    if key in ('stone', 'stone_dark'):
        # cracks / joints: voronoi edges darken; silt streaks (noise stretched along Z) and a silted lower band
        vor = N('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'; vor.inputs['Scale'].default_value = 1.4; L(tc.outputs['Object'], vor.inputs['Vector'])
        crack = N('ShaderNodeMapRange'); crack.inputs['From Min'].default_value = 0.0; crack.inputs['From Max'].default_value = 0.018
        crack.inputs['To Min'].default_value = 0.82; crack.inputs['To Max'].default_value = 1.0; L(vor.outputs['Distance'], crack.inputs['Value'])
        wv = N('ShaderNodeTexWave'); wv.wave_type = 'BANDS'; wv.bands_direction = 'Z'; wv.wave_profile = 'SAW'
        wv.inputs['Scale'].default_value = 0.5 / 0.32; wv.inputs['Distortion'].default_value = 0.6; wv.inputs['Detail'].default_value = 1.0
        L(tc.outputs['Object'], wv.inputs['Vector'])
        jt = N('ShaderNodeMapRange'); jt.inputs['From Min'].default_value = 0.0; jt.inputs['From Max'].default_value = 0.035
        jt.inputs['To Min'].default_value = 0.80; jt.inputs['To Max'].default_value = 1.0; L(wv.outputs['Fac'], jt.inputs['Value'])
        jm = N('ShaderNodeMath'); jm.operation = 'MINIMUM'; L(crack.outputs[0], jm.inputs[0]); L(jt.outputs[0], jm.inputs[1])
        mul = N('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1.0
        L(col, mul.inputs['A']); cc = N('ShaderNodeCombineColor'); [L(jm.outputs[0], cc.inputs[i]) for i in range(3)]; L(cc.outputs[0], mul.inputs['B']); col = mul.outputs['Result']
        mp = N('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (3.2, 3.2, 0.35); L(tc.outputs['Object'], mp.inputs['Vector'])
        sn = N('ShaderNodeTexNoise'); sn.inputs['Scale'].default_value = 2.2; sn.inputs['Detail'].default_value = 1; L(mp.outputs[0], sn.inputs['Vector'])
        st = N('ShaderNodeMapRange'); st.inputs['From Min'].default_value = STREAK; st.inputs['From Max'].default_value = STREAK + 0.12
        st.inputs['To Min'].default_value = 0.0; st.inputs['To Max'].default_value = 0.7; L(sn.outputs['Fac'], st.inputs['Value'])
        low = N('ShaderNodeMapRange'); low.inputs['From Min'].default_value = SILT_LINE + 0.01; low.inputs['From Max'].default_value = SILT_LINE - 0.06
        low.inputs['To Min'].default_value = 0.0; low.inputs['To Max'].default_value = 0.6; L(sep.outputs['Z'], low.inputs['Value'])
        mx = N('ShaderNodeMath'); mx.operation = 'MAXIMUM'; L(st.outputs[0], mx.inputs[0]); L(low.outputs[0], mx.inputs[1])
        mix = N('ShaderNodeMix'); mix.data_type = 'RGBA'; L(mx.outputs[0], mix.inputs['Factor']); L(col, mix.inputs['A'])
        mix.inputs['B'].default_value = (0.21, 0.19, 0.145, 1); col = mix.outputs['Result']
    if key in ('silt', 'silt_wet', 'moss', 'moss_dark'):
        # wetter (darker) toward the floor
        wet = N('ShaderNodeMapRange'); wet.inputs['From Min'].default_value = 0.0; wet.inputs['From Max'].default_value = 0.35
        wet.inputs['To Min'].default_value = 0.6; wet.inputs['To Max'].default_value = 1.0; L(sep.outputs['Z'], wet.inputs['Value'])
        cc = N('ShaderNodeCombineColor'); [L(wet.outputs[0], cc.inputs[i]) for i in range(3)]
        mul = N('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1.0
        L(col, mul.inputs['A']); L(cc.outputs[0], mul.inputs['B']); col = mul.outputs['Result']
    if key in ('verdigris', 'bronze'):
        # bronze shows through the patina on edges / high spots
        pn = N('ShaderNodeTexNoise'); pn.inputs['Scale'].default_value = 14; pn.inputs['Detail'].default_value = 5; L(tc.outputs['Object'], pn.inputs['Vector'])
        pr = N('ShaderNodeMapRange'); pr.inputs['From Min'].default_value = 0.66; pr.inputs['From Max'].default_value = 0.76; L(pn.outputs['Fac'], pr.inputs['Value'])
        mix = N('ShaderNodeMix'); mix.data_type = 'RGBA'; L(pr.outputs[0], mix.inputs['Factor']); L(col, mix.inputs['A'])
        mix.inputs['B'].default_value = (0.24, 0.20, 0.10, 1) if key == 'verdigris' else (0.16, 0.34, 0.27, 1); col = mix.outputs['Result']
    L(col, bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = rough; bsdf.inputs['Metallic'].default_value = metal
    if key in EMIT:
        bsdf.inputs['Emission Color'].default_value = (*EMIT[key], 1); bsdf.inputs['Emission Strength'].default_value = 1.0
    m['silt_key'] = key

def _bake(obj, img, kind, samples=8):
    sc = bpy.context.scene
    for m in obj.data.materials:
        nt = m.node_tree; n = nt.nodes.get('bake_target') or nt.nodes.new('ShaderNodeTexImage'); n.name = 'bake_target'; n.image = img
        nt.nodes.active = n
    sc.cycles.samples = samples
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    if kind == 'DIFFUSE':
        bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8)
    else:
        bpy.ops.object.bake(type=kind, margin=8)

def _swap_to(obj, key_fn):
    # route a per-material value into Emission (strength 1, no other light matters for an EMIT bake)
    saved = {}
    for m in obj.data.materials:
        nt = m.node_tree; b = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); k = m.get('silt_key')
        saved[m.name] = (tuple(b.inputs['Emission Color'].default_value), b.inputs['Emission Strength'].default_value)
        v = key_fn(k); b.inputs['Emission Color'].default_value = (v, v, v, 1); b.inputs['Emission Strength'].default_value = 1.0
    return saved

def _restore(obj, saved):
    for m in obj.data.materials:
        b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); c, s = saved[m.name]
        b.inputs['Emission Color'].default_value = c; b.inputs['Emission Strength'].default_value = s

def _px(img): return np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)

def _bake_all(obj, work, name, size, osize, esize):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'
    # UVs: one smart-projected atlas
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    if not obj.data.uv_layers: obj.data.uv_layers.new(name='UVMap')
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    zmax = max(v.co.z for v in obj.data.vertices)
    for m in obj.data.materials:
        k = _proc(m.name)
        if k: _build_proc(m, k, zmax)
    imgs = {}
    def new(n, s, non_color=False):
        im = bpy.data.images.new(f'{name}_{n}', s, s, alpha=False)
        if non_color: im.colorspace_settings.name = 'Non-Color'
        imgs[n] = im; return im
    # a DIFFUSE colour bake returns black for metal: bake the base colour with Metallic 0, then restore it
    bs = [next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED') for m in obj.data.materials]
    mv = [b.inputs['Metallic'].default_value for b in bs]
    for b in bs: b.inputs['Metallic'].default_value = 0.0
    base = new('base', size); _bake(obj, base, 'DIFFUSE', 4)
    for b, v in zip(bs, mv): b.inputs['Metallic'].default_value = v
    ao = new('ao', size, True); _bake(obj, ao, 'AO', 32)
    rough = new('rough', size, True); sv = _swap_to(obj, lambda k: LOOK[k][3] if k else 0.8); _bake(obj, rough, 'EMIT', 1); _restore(obj, sv)
    metal = new('metal', size, True); sv = _swap_to(obj, lambda k: LOOK[k][4] if k else 0.0); _bake(obj, metal, 'EMIT', 1); _restore(obj, sv)
    emit = new('emit', size); _bake(obj, emit, 'EMIT', 1)   # only eye / crown / mouth carry emission
    # compose: base x (0.45 + 0.55 AO); ORM = (AO, rough, metal); emission as baked
    B = _px(base); A = _px(ao)[..., 0:1]; Rg = _px(rough)[..., 0]; Mt = _px(metal)[..., 0]
    B[..., :3] *= 0.62 + 0.38 * A
    out = {}
    def save(n, arr, s, non_color):
        im = bpy.data.images.new(f'{name}_{n}_final', arr.shape[1], arr.shape[0], alpha=False)
        if non_color: im.colorspace_settings.name = 'Non-Color'
        im.pixels = arr.ravel(); im.scale(s, s)
        im.filepath_raw = os.path.join(work, f'{name}_{n}.png'); im.file_format = 'PNG'; im.save(); im.pack(); out[n] = im
    save('BaseColor', B, size, False)
    O = np.ones_like(B); O[..., 0] = 1.0; O[..., 1] = Rg; O[..., 2] = Mt   # AO lives in the base colour; R = 1 so it is not applied twice
    save('ORM', O, osize, True)
    E = _px(emit)
    save('Emissive', E, esize, False)
    for im in imgs.values(): bpy.data.images.remove(im)
    # one baked material on every face
    m = bpy.data.materials.new(name + '_baked'); m.use_nodes = True; nt = m.node_tree; b = nt.nodes['Principled BSDF']; L = nt.links.new
    tb = nt.nodes.new('ShaderNodeTexImage'); tb.image = out['BaseColor']; L(tb.outputs['Color'], b.inputs['Base Color'])
    to = nt.nodes.new('ShaderNodeTexImage'); to.image = out['ORM']; sp = nt.nodes.new('ShaderNodeSeparateColor'); L(to.outputs['Color'], sp.inputs[0])
    L(sp.outputs['Green'], b.inputs['Roughness']); L(sp.outputs['Blue'], b.inputs['Metallic'])
    te = nt.nodes.new('ShaderNodeTexImage'); te.image = out['Emissive']; L(te.outputs['Color'], b.inputs['Emission Color']); b.inputs['Emission Strength'].default_value = 1.0
    obj.data.materials.clear(); obj.data.materials.append(m)
    for p in obj.data.polygons: p.material_index = 0
    bpy.context.scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    print('TEXTURED', name, {k: v.size[:] for k, v in out.items()})

def apply(body, work): _bake_all(body, work, 'SiltCarillon', 2048, 1024, 512)
def apply_prop(obj, work, name): _bake_all(obj, work, name, 1024, 512, 256)
