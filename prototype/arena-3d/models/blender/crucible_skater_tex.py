# Texture pass for the Crucible Skater and its ladle (imported by crucible_skater_rig.py / crucible_ladle_build.py with
# --textured <dir>). UV-unwraps the mesh (smart project, mask/boot islands enlarged, then packed), gives each flat
# material a procedural look, bakes it with Cycles into one texture set and replaces the materials with one baked material
# that the glTF exporter writes as textures:
#   base colour (x a short-range baked AO), glTF metallic-roughness, a tangent normal map computed from a baked height map
#   (hammer dimples, ivory crazing, raised crust on the molten brass), emissive.
# Palette (prompts.md), as LINEAR values: brass #C79A4D, seam brass #7A5A2A, ivory #F0E6CC, cobalt #293D8C,
# molten #FF8C26, steel #7C838C. Images go to <dir>.
import bpy, bmesh, os, math
import numpy as np

def lin(hexs):
    c = [int(hexs[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
def scl(c, k): return tuple(min(1.0, x * k) for x in c)

STYLE = dict(ao=0.30, ao_dist=0.12, tarnish=0.5, normal=2.0, crust=0.32)

def _nodes(m):
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear(); return nt, nt.nodes, nt.links
def _n(N, t, x=0, y=0, **kw):
    n = N.new(t); n.location = (x, y)
    for k, v in kw.items(): setattr(n, k, v)
    return n
def _ramp(N, L, src, stops, x=0, y=0):
    r = _n(N, 'ShaderNodeValToRGB', x, y); e = r.color_ramp.elements
    e[0].position, e[0].color = stops[0][0], (*stops[0][1], 1); e[1].position, e[1].color = stops[-1][0], (*stops[-1][1], 1)
    for p, c in stops[1:-1]: el = e.new(p); el.color = (*c, 1)
    L.new(src, r.inputs[0]); return r
def _math(N, L, op, a, b, x=0, y=0, clamp=False):
    m = _n(N, 'ShaderNodeMath', x, y); m.operation = op; m.use_clamp = clamp
    for i, v in enumerate((a, b)):
        if isinstance(v, (int, float)): m.inputs[i].default_value = v
        else: L.new(v, m.inputs[i])
    return m.outputs[0]
def _mixc(N, L, fac, a, b, blend='MIX', x=0, y=0):
    m = _n(N, 'ShaderNodeMix', x, y); m.data_type = 'RGBA'; m.blend_type = blend
    for sock, v in ((m.inputs[0], fac), (m.inputs[6], a), (m.inputs[7], b)):
        if isinstance(v, (int, float)): sock.default_value = v
        elif isinstance(v, tuple): sock.default_value = (*v, 1) if len(v) == 3 else v
        else: L.new(v, sock)
    return m.outputs[2]

def procedural(m, kind, s):
    """Builds the material; returns (principled, height socket or None)."""
    nt, N, L = _nodes(m)
    out = _n(N, 'ShaderNodeOutputMaterial', 900, 0); bs = _n(N, 'ShaderNodeBsdfPrincipled', 600, 0); L.new(bs.outputs[0], out.inputs[0])
    co = _n(N, 'ShaderNodeTexCoord', -1100, 0).outputs['Object']
    geo = _n(N, 'ShaderNodeNewGeometry', -1100, -300)
    def noise(scale, detail=4, rough=0.5, y=0):
        n = _n(N, 'ShaderNodeTexNoise', -800, y); n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
        n.inputs['Roughness'].default_value = rough; L.new(co, n.inputs['Vector']); return n
    def voronoi(scale, feature='F1', y=0, rand=1.0):
        v = _n(N, 'ShaderNodeTexVoronoi', -800, y); v.feature = feature; v.inputs['Scale'].default_value = scale
        v.inputs['Randomness'].default_value = rand; L.new(co, v.inputs['Vector']); return v
    # convex-edge factor from Cycles pointiness (bakes fine; coarse on low-poly but enough for polished rims)
    edge = _n(N, 'ShaderNodeValToRGB', -800, -300); L.new(geo.outputs['Pointiness'], edge.inputs[0])
    edge.color_ramp.elements[0].position = 0.52; edge.color_ramp.elements[1].position = 0.57
    height = None
    bs.inputs['Metallic'].default_value = 0.0; bs.inputs['Roughness'].default_value = 0.5
    if kind in ('Brass', 'BrassDark'):
        dark = kind == 'BrassDark'
        base = lin('#7A5A2A') if dark else lin('#D3A85A')
        tarn = scl(base, 0.62); tarn = (tarn[0], tarn[1] * 0.97, tarn[2] * 0.9)
        polish = scl(base, 1.1)
        n1 = noise(3.5, 5, 0.55, 300)
        tf = _n(N, 'ShaderNodeMapRange', -550, 300); tf.inputs['From Min'].default_value = 0.5; tf.inputs['From Max'].default_value = 0.72
        tf.inputs['To Max'].default_value = s['tarnish'] * (0.3 if dark else 1.0); L.new(n1.outputs['Fac'], tf.inputs['Value'])
        c = _mixc(N, L, tf.outputs[0], base, tarn, x=-300, y=300)
        c = _mixc(N, L, edge.outputs[0], c, polish, x=-100, y=300)
        L.new(c, bs.inputs['Base Color'])
        n2 = noise(9.0, 3, 0.5, 100)
        r = _math(N, L, 'ADD', 0.42 if not dark else 0.52, _math(N, L, 'MULTIPLY', n2.outputs['Fac'], 0.16, -300, 0), -100, 0)
        r = _math(N, L, 'ADD', r, _math(N, L, 'MULTIPLY', tf.outputs[0], 0.2, -300, -80), -50, 0)
        L.new(r, bs.inputs['Roughness'])
        bs.inputs['Metallic'].default_value = 0.85 if not dark else 0.75
        v = voronoi(95.0, 'F1', -150, 0.8)   # hammer dimples, ~1 cm
        height = _math(N, L, 'MULTIPLY', v.outputs['Distance'], 0.35 if not dark else 0.2, -500, -150)
    elif kind == 'Ivory':
        v = voronoi(16.0, 'DISTANCE_TO_EDGE', -200)
        crack = _n(N, 'ShaderNodeMapRange', -550, -200); crack.inputs['From Min'].default_value = 0.0; crack.inputs['From Max'].default_value = 0.006
        L.new(v.outputs['Distance'], crack.inputs['Value'])
        n1 = noise(6.0, 3, 0.5, 200)
        c = _mixc(N, L, _math(N, L, 'MULTIPLY', n1.outputs['Fac'], 0.25, -500, 200), lin('#F0E6CC'), lin('#E4D3AE'), x=-300, y=200)
        c = _mixc(N, L, _math(N, L, 'MULTIPLY', _math(N, L, 'SUBTRACT', 1.0, crack.outputs[0], -600, 0, True), 0.35, -500, 0), c, lin('#C9B48C'), x=-100, y=200)
        L.new(c, bs.inputs['Base Color']); bs.inputs['Roughness'].default_value = 0.38
        height = _math(N, L, 'MULTIPLY', crack.outputs[0], 0.06, -300, -200)
    elif kind == 'Cobalt':
        n1 = noise(10.0, 3, 0.5, 200)
        c = _mixc(N, L, _math(N, L, 'MULTIPLY', n1.outputs['Fac'], 0.25, -500, 200), lin('#30479E'), lin('#3F5CBC'), x=-300, y=200)
        c = _mixc(N, L, edge.outputs[0], c, lin('#6F86D6'), x=-100, y=200)
        L.new(c, bs.inputs['Base Color']); bs.inputs['Roughness'].default_value = 0.2; bs.inputs['Metallic'].default_value = 0.2
    elif kind == 'Molten':
        # glowing brass with dark crust islands (about a third of the cells, toward the rim) and hotter seams
        hot = noise(5.0, 3, 0.45, 200); crustn = noise(14.0, 4, 0.55, 0)
        hf = _n(N, 'ShaderNodeMapRange', -550, 200); hf.inputs['From Min'].default_value = 0.45; hf.inputs['From Max'].default_value = 0.7
        L.new(hot.outputs['Fac'], hf.inputs['Value'])
        glow = _mixc(N, L, hf.outputs[0], lin('#FF8C26'), lin('#FFB850'), x=-200, y=-150)
        cf = _n(N, 'ShaderNodeMapRange', -550, 0); cf.inputs['From Min'].default_value = 0.66; cf.inputs['From Max'].default_value = 0.74
        cf.inputs['To Max'].default_value = 0.85; L.new(crustn.outputs['Fac'], cf.inputs['Value'])
        cm = cf.outputs[0]
        col = _mixc(N, L, cm, glow, (0.16, 0.04, 0.008), x=0, y=0)
        L.new(col, bs.inputs['Base Color']); L.new(col, bs.inputs['Emission Color']); bs.inputs['Emission Strength'].default_value = 1.0
        L.new(_math(N, L, 'ADD', 0.3, _math(N, L, 'MULTIPLY', cm, 0.6, -200, -300), 0, -300), bs.inputs['Roughness'])
        height = _math(N, L, 'MULTIPLY', cm, 0.25, 0, -450)
    elif kind == 'Steel':
        n1 = noise(40.0, 3, 0.5, 200)
        c = _mixc(N, L, _math(N, L, 'MULTIPLY', n1.outputs['Fac'], 0.3, -500, 200), lin('#7C838C'), lin('#69707A'), x=-300, y=200)
        c = _mixc(N, L, edge.outputs[0], c, lin('#D8DCE2'), x=-100, y=200)
        L.new(c, bs.inputs['Base Color']); bs.inputs['Roughness'].default_value = 0.25; bs.inputs['Metallic'].default_value = 0.9
    else:  # Dark
        bs.inputs['Base Color'].default_value = (0.012, 0.012, 0.02, 1); bs.inputs['Roughness'].default_value = 0.6
    return bs, height

def unwrap(ob, grow=('Ivory', 'Cobalt'), k=1.8, margin=0.004):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    # more texels for the mask and the cobalt panels: scale their islands, then repack everything
    me = ob.data; bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv.active
    names = [m.name.split('.')[0] for m in me.materials]
    for f in bm.faces:
        if names[f.material_index] in grow:
            for l in f.loops: l[uv].uv *= k
    bm.to_mesh(me); bm.free()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    try: bpy.ops.uv.pack_islands(rotate=True, margin=margin, shape_method='CONCAVE')
    except TypeError: bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')

def _img(name, size, noncolor=False, flt=False):
    im = bpy.data.images.new(name, size, size, alpha=False, float_buffer=flt)
    if noncolor: im.colorspace_settings.name = 'Non-Color'
    return im
def _bake(ob, img, typ, **kw):
    for m in ob.data.materials:
        nt = m.node_tree; n = nt.nodes.get('bake_target') or nt.nodes.new('ShaderNodeTexImage'); n.name = 'bake_target'
        n.image = img; nt.nodes.active = n
    bpy.ops.object.bake(type=typ, margin=12, use_clear=True, **kw)
def _px(img): return np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
def _emit_bake(ob, img, sockets):
    # route a value per material into an emission shader and bake EMIT (metallic, height)
    for m, sock in zip(ob.data.materials, sockets):
        nt = m.node_tree; outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        em = nt.nodes.get('emit_probe') or nt.nodes.new('ShaderNodeEmission'); em.name = 'emit_probe'
        for l in list(em.inputs['Color'].links): nt.links.remove(l)
        if isinstance(sock, float): em.inputs['Color'].default_value = (sock, sock, sock, 1)
        else: nt.links.new(sock, em.inputs['Color'])
        nt.links.new(em.outputs[0], outn.inputs[0])
    _bake(ob, img, 'EMIT')
    for m in ob.data.materials:
        nt = m.node_tree; outn = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); nt.links.new(bs.outputs[0], outn.inputs[0])

def bake_set(ob, tex_dir, slug, size, normal_size, style=None):
    s = dict(STYLE); s.update(style or {})
    os.makedirs(tex_dir, exist_ok=True)
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 16; sc.cycles.device = 'CPU'
    if sc.world is None: sc.world = bpy.data.worlds.new('bake')
    sc.world.light_settings.distance = s['ao_dist']
    unwrap(ob, margin=s.get('uvmargin', 0.004))
    built = [procedural(m, m.name.split('.')[0], s) for m in ob.data.materials]
    bsdfs = [b for b, _ in built]; heights = [h if h is not None else 0.0 for _, h in built]
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    metals = [b.inputs['Metallic'].default_value for b in bsdfs]
    # the diffuse colour pass drops the base colour of metallic surfaces: bake it with metallic at 0, then restore
    for b in bsdfs: b.inputs['Metallic'].default_value = 0.0
    alb = _img(slug + '_albedo', size); _bake(ob, alb, 'DIFFUSE', pass_filter={'COLOR'})
    for b, v in zip(bsdfs, metals): b.inputs['Metallic'].default_value = v
    ao = _img(slug + '_ao', size, True); sc.cycles.samples = 64; _bake(ob, ao, 'AO'); sc.cycles.samples = 16
    rough = _img(slug + '_rough', size, True); _bake(ob, rough, 'ROUGHNESS')
    emit = _img(slug + '_emit', size); _bake(ob, emit, 'EMIT')
    met = _img(slug + '_metal', size, True); _emit_bake(ob, met, [float(v) for v in metals])
    hgt = _img(slug + '_height', normal_size, True, True); _emit_bake(ob, hgt, heights)
    # tangent normal from the height map's UV gradient (glTF tangents follow +U / +V)
    H = _px(hgt)[..., 0].astype(np.float64)
    gy, gx = np.gradient(H)
    k = s['normal'] * normal_size / 1024
    nx, ny, nz = -gx * k, -gy * k, np.ones_like(H)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz); Nrm = np.ones((H.shape[0], H.shape[1], 4), np.float32)
    Nrm[..., 0] = nx / ln * 0.5 + 0.5; Nrm[..., 1] = ny / ln * 0.5 + 0.5; Nrm[..., 2] = nz / ln * 0.5 + 0.5
    nrmi = _img(slug + '_normal', normal_size, True); nrmi.pixels = Nrm.ravel()
    A = _px(alb); O = _px(ao)[..., :1]
    A[..., :3] *= (1 - s['ao']) + s['ao'] * O
    alb.pixels = A.ravel()
    MR = np.ones_like(A); MR[..., 0] = 0; MR[..., 1] = _px(rough)[..., 0]; MR[..., 2] = _px(met)[..., 0]
    mr = _img(slug + '_mr', size, True); mr.pixels = MR.ravel()
    files = {}
    for im, key in ((alb, 'albedo'), (mr, 'mr'), (nrmi, 'normal'), (emit, 'emit')):
        f = os.path.join(tex_dir, f'{slug}_{key}.png'); im.filepath_raw = f; im.file_format = 'PNG'; im.save(); files[key] = im
    # one baked material
    m = bpy.data.materials.new(slug + '_baked'); nt, N, L = _nodes(m)
    out = _n(N, 'ShaderNodeOutputMaterial', 600, 0); bs = _n(N, 'ShaderNodeBsdfPrincipled', 300, 0); L.new(bs.outputs[0], out.inputs[0])
    ta = _n(N, 'ShaderNodeTexImage', -300, 300, image=files['albedo']); L.new(ta.outputs['Color'], bs.inputs['Base Color'])
    tm = _n(N, 'ShaderNodeTexImage', -300, 0, image=files['mr']); sp = _n(N, 'ShaderNodeSeparateColor', 0, 0)
    L.new(tm.outputs['Color'], sp.inputs[0]); L.new(sp.outputs['Green'], bs.inputs['Roughness']); L.new(sp.outputs['Blue'], bs.inputs['Metallic'])
    tn = _n(N, 'ShaderNodeTexImage', -300, -300, image=files['normal']); nm = _n(N, 'ShaderNodeNormalMap', 0, -300)
    L.new(tn.outputs['Color'], nm.inputs['Color']); L.new(nm.outputs[0], bs.inputs['Normal'])
    te = _n(N, 'ShaderNodeTexImage', -300, -600, image=files['emit']); L.new(te.outputs['Color'], bs.inputs['Emission Color'])
    bs.inputs['Emission Strength'].default_value = 1.0
    ob.data.materials.clear(); ob.data.materials.append(m)
    for p in ob.data.polygons: p.material_index = 0
    eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in eng else 'BLENDER_EEVEE'
    cov = float((_px(alb)[..., :3].sum(-1) > 0.002).mean())
    print('BAKED', slug, size, normal_size, 'uv coverage %.2f' % cov, tex_dir)

def apply_textures(body, tex_dir, style=None): bake_set(body, tex_dir, 'crucible_skater', 2048, 2048, style)
def apply_ladle(ob, tex_dir, style=None): bake_set(ob, tex_dir, 'crucible_ladle', 1024, 1024, dict(style or {}, uvmargin=0.015))
