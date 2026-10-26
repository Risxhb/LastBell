# Painterly texture bake for the Ultimate's props (ART-PLAN-v2.md: "high quality textures"). exec()'d by a kit script after
# it builds an object, before export:  paint_and_bake(obj, size)  ->  the object's materials now sample one baked colour
# map (and one normal map) through its UVs, so the GLB carries real textures.
# What gets painted, from gpt/02-visual-style-guide.md ("painterly stylized 3D, 2-3 broad tone bands, crafted bevels,
# hand-painted material breakup"; brass: warm diffuse base, broad bright edge highlights, slight umber shadow; enamel:
# smooth plates with painted chips; slate: broad planar breakup, stylized cracks):
#   broad breakup   large soft noise that shifts the value a little (never busy)
#   recesses        ambient occlusion darkens corners and the roots of teeth, toward umber for metals
#   worn edges      the bevel-normal vs face-normal difference picks the edges: metals brighten there, enamel chips to brass
#   tone bands      a fixed upper-left key quantised to three bands (the in-game light adds on top, so they stay gentle)
# The bake runs in Cycles on the CPU (works headless). Materials named *Gold*/*Coral* keep their emission.
import bpy, math

def _kind(m):
    n = m.name.lower()
    if 'enamel' in n: return 'enamel'
    if 'slate' in n or 'stone' in n: return 'slate'
    if 'ivory' in n: return 'ivory'
    if 'gold' in n or 'coral' in n: return 'glow'
    return 'metal'

def _principled(m):
    return next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')

def _paint_graph(m, img):
    # replace the material's output with an emission of the painted colour (EMIT bake), and an image node for the bake
    nt = m.node_tree; N = nt.nodes; L = nt.links
    b = _principled(m); base = tuple(b.inputs['Base Color'].default_value)
    kind = _kind(m)
    out = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
    def node(t, **kw):
        n = N.new(t)
        for k, v in kw.items(): setattr(n, k, v)
        return n
    def mix(a, bb, fac, op='MIX'):
        mx = node('ShaderNodeMix', data_type='RGBA', blend_type=op)
        if isinstance(fac, (int, float)): mx.inputs['Factor'].default_value = fac
        else: L.new(fac, mx.inputs['Factor'])
        for sock, val in ((mx.inputs[6], a), (mx.inputs[7], bb)):
            if isinstance(val, tuple): sock.default_value = val
            else: L.new(val, sock)
        return mx.outputs[2]
    col = (base[0], base[1], base[2], 1)
    # broad breakup
    tc = node('ShaderNodeTexCoord'); nz = node('ShaderNodeTexNoise'); nz.inputs['Scale'].default_value = 1.6 if kind != 'slate' else 0.9
    nz.inputs['Detail'].default_value = 2.0; L.new(tc.outputs['Object'], nz.inputs['Vector'])
    rmp = node('ShaderNodeValToRGB'); L.new(nz.outputs['Fac'], rmp.inputs['Fac'])
    rmp.color_ramp.elements[0].position = 0.35; rmp.color_ramp.elements[1].position = 0.65
    rmp.color_ramp.elements[0].color = (0.86, 0.86, 0.86, 1); rmp.color_ramp.elements[1].color = (1.12, 1.12, 1.12, 1)
    c = mix(col, rmp.outputs['Color'], 1.0, 'MULTIPLY')
    # slate: stylized cracks (thin dark veins from a voronoi edge)
    if kind == 'slate':
        vo = node('ShaderNodeTexVoronoi', feature='DISTANCE_TO_EDGE'); vo.inputs['Scale'].default_value = 1.3; L.new(tc.outputs['Object'], vo.inputs['Vector'])
        cr = node('ShaderNodeValToRGB'); L.new(vo.outputs['Distance'], cr.inputs['Fac'])
        cr.color_ramp.elements[0].position = 0.0; cr.color_ramp.elements[1].position = 0.035
        cr.color_ramp.elements[0].color = (0.55, 0.55, 0.62, 1); cr.color_ramp.elements[1].color = (1, 1, 1, 1)
        c = mix(c, cr.outputs['Color'], 1.0, 'MULTIPLY')
    # tone bands from a fixed upper-left key
    geo = node('ShaderNodeNewGeometry'); key = node('ShaderNodeVectorMath', operation='DOT_PRODUCT')
    key.inputs[1].default_value = (-0.45, 0.35, 0.82); L.new(geo.outputs['Normal'], key.inputs[0])
    band = node('ShaderNodeValToRGB'); band.color_ramp.interpolation = 'CONSTANT'; L.new(key.outputs['Value'], band.inputs['Fac'])
    e = band.color_ramp.elements; e[0].position = 0.0; e[0].color = (0.82, 0.80, 0.86, 1); e[1].position = 0.45; e[1].color = (1.0, 1.0, 1.0, 1)
    e3 = e.new(0.85); e3.color = (1.08, 1.06, 1.02, 1)
    c = mix(c, band.outputs['Color'], 1.0, 'MULTIPLY')
    # recesses
    ao = node('ShaderNodeAmbientOcclusion', samples=16); ao.inputs['Distance'].default_value = 0.35
    shade = (0.42, 0.30, 0.20, 1) if kind == 'metal' else (0.45, 0.47, 0.6, 1)
    dark = mix(c, shade, 1.0, 'MULTIPLY')
    c = mix(dark, c, ao.outputs['AO'])
    # worn edges: where the bevelled normal leaves the face normal
    bev = node('ShaderNodeBevel', samples=8); bev.inputs['Radius'].default_value = 0.035
    dp = node('ShaderNodeVectorMath', operation='DOT_PRODUCT'); L.new(bev.outputs['Normal'], dp.inputs[0]); L.new(geo.outputs['Normal'], dp.inputs[1])
    edge = node('ShaderNodeMapRange'); L.new(dp.outputs['Value'], edge.inputs['Value'])
    edge.inputs['From Min'].default_value = 0.9; edge.inputs['From Max'].default_value = 0.995; edge.inputs['To Min'].default_value = 1.0; edge.inputs['To Max'].default_value = 0.0
    if kind == 'metal':
        hi = mix(c, (1.0, 0.86, 0.6, 1), 0.55, 'MIX'); c = mix(c, hi, edge.outputs['Result'])
    elif kind == 'enamel':
        chip = (0.62, 0.42, 0.2, 1); c = mix(c, chip, edge.outputs['Result'])   # chipped to the brass beneath
    elif kind in ('ivory', 'slate'):
        hi = mix(c, (1, 1, 1, 1), 0.25, 'MIX'); c = mix(c, hi, edge.outputs['Result'])
    em = node('ShaderNodeEmission'); L.new(c, em.inputs['Color'])
    m['_orig_link'] = 1
    for lk in list(out.inputs['Surface'].links): L.remove(lk)
    L.new(em.outputs['Emission'], out.inputs['Surface'])
    it = node('ShaderNodeTexImage'); it.image = img; N.active = it; it.select = True
    return base, kind

def _final(m, img, nimg, base, kind):
    # a clean Principled for export: base colour from the bake, normal map from the bevel bake, the original metal/rough
    nt = m.node_tree; N = nt.nodes; L = nt.links
    keep = _principled(m); metal = keep.inputs['Metallic'].default_value; rough = keep.inputs['Roughness'].default_value
    emc = tuple(keep.inputs['Emission Color'].default_value); ems = keep.inputs['Emission Strength'].default_value
    for n in list(N): N.remove(n)
    out = N.new('ShaderNodeOutputMaterial'); b = N.new('ShaderNodeBsdfPrincipled')
    L.new(b.outputs['BSDF'], out.inputs['Surface'])
    t = N.new('ShaderNodeTexImage'); t.image = img; L.new(t.outputs['Color'], b.inputs['Base Color'])
    if nimg is not None:
        tn = N.new('ShaderNodeTexImage'); tn.image = nimg; tn.image.colorspace_settings.name = 'Non-Color'
        nm = N.new('ShaderNodeNormalMap'); L.new(tn.outputs['Color'], nm.inputs['Color']); L.new(nm.outputs['Normal'], b.inputs['Normal'])
    b.inputs['Metallic'].default_value = metal * 0.6; b.inputs['Roughness'].default_value = max(rough, 0.45)   # painterly: no mirror metals
    if kind == 'glow' and ems > 0:
        b.inputs['Emission Color'].default_value = emc; b.inputs['Emission Strength'].default_value = ems

def paint_and_bake(obj, size=1024):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 24; sc.cycles.bake_type = 'EMIT'
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    # apply modifiers (bevels) so the bake sees the real edges, then unwrap
    for md in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.012)
    bpy.ops.object.mode_set(mode='OBJECT')
    img = bpy.data.images.new(obj.name + '_paint', size, size, alpha=False)
    for s in obj.material_slots:   # this object's own copies (the kit shares materials between props)
        if s.material: s.material = s.material.copy()
    mats = [s.material for s in obj.material_slots if s.material]
    info = {}
    for m in mats: info[m.name] = _paint_graph(m, img)
    bpy.ops.object.bake(type='EMIT', margin=6)
    img.pack()
    # the normal map: bake the bevel normal in tangent space
    nimg = bpy.data.images.new(obj.name + '_normal', size // 2, size // 2, alpha=False); nimg.colorspace_settings.name = 'Non-Color'
    for m in mats:
        nt = m.node_tree; N = nt.nodes; L = nt.links
        out = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
        dif = N.new('ShaderNodeBsdfDiffuse'); bev = N.new('ShaderNodeBevel'); bev.inputs['Radius'].default_value = 0.035; bev.samples = 8
        L.new(bev.outputs['Normal'], dif.inputs['Normal'])
        for lk in list(out.inputs['Surface'].links): L.remove(lk)
        L.new(dif.outputs['BSDF'], out.inputs['Surface'])
        it = N.new('ShaderNodeTexImage'); it.image = nimg; N.active = it
    sc.cycles.samples = 8
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=6)
    nimg.pack()
    for m in mats:
        base, kind = info[m.name]; _final(m, img, nimg, base, kind)
    print('BAKED', obj.name, size)
