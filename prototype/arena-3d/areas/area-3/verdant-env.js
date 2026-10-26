// <verdant-env> Area 3, The Verdant Chimes: overgrown clockwork ruins over a turquoise abyss, rebuilt from the Verdant
// Chimes panel of Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png.
// One shared arena for 3 minibosses + 1 area boss, so the floor is a broad, flat, unobstructed disc.
// Classic script, same contract as ../../arena-env.js:
//   const env = window.buildVerdantEnv(THREE, scene);   env.update(timeSeconds, dt, quiet) every frame.
// Returns { R, PLAY_R, root, sun, update }. Metres; the floor is a disc of radius R at y = 0, centred on 0,0.
window.buildVerdantEnv = function buildVerdantEnv(THREE, scene, opts = {}) {
const R = 14.6;        // floor disc radius (inside the curb)
const PLAY_R = 14;     // playable radius: same area as the Bellkeeper dais (±12.4 m square)
const DIAL_R = 3.6;    // turquoise resonance dial in the middle
const EXITS = [0, Math.PI / 2, Math.PI, Math.PI * 1.5]; // east, south (toward camera), west, north

// ---- helpers ----
function mulberry32(a) { return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(31);
const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
const fbm = (x, y) => vnoise(x, y) * 0.55 + vnoise(x * 2.1, y * 2.1) * 0.3 + vnoise(x * 4.3, y * 4.3) * 0.15;
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const tex = (c, srgb = true) => { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t; };
const canvas2d = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };
const nearExit = (a, tol) => EXITS.some(e => Math.abs(Math.atan2(Math.sin(a - e), Math.cos(a - e))) < tol);

const root = new THREE.Group(); root.name = 'LB_ENV_VerdantChimes_Arena'; scene.add(root);
const add = (m, parent = root) => { parent.add(m); return m; };
// ---- LB CustomLit look, from the Windblown teardown (research/windblown-artstyle.html §1–2, REA evidence) ----
// Windblown shades props with its own model instead of PBR: a remapped half-Lambert band with brush noise on the
// terminator, shadows tinted a chosen colour, a fake sky ambient from above, and a backlight rim. All tunables are
// shared globals held in one per-biome asset (their LightingDecoSettings, our LookSettings). LOOK is that asset for
// the Verdant Chimes: teal-green shadows, mossy top light, pale turquoise rim. Same shader chunk as ../../mockup.html.
const LOOK = {
  lbRemapMin: { value: 0.38 }, lbRemapMax: { value: 0.66 }, lbBrush: { value: 0.1 },
  lbTop: { value: new THREE.Color(0x5f9e86) }, lbTopStart: { value: 0.2 }, lbTopEnd: { value: 0.95 }, lbTopStr: { value: 0.6 },
  lbRimStr: { value: 0.42 }, lbRimTight: { value: 3.2 }, lbRim: { value: new THREE.Color(0xbff5e8) },
};
const LIT_PARS = `varying vec3 vWPos;
uniform float lbRemapMin, lbRemapMax, lbBrush, lbTopStart, lbTopEnd, lbTopStr, lbRimStr, lbRimTight, lbMetal; uniform vec3 lbTop, lbRim;
float lbH(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float lbN(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3. - 2. * f); return mix(mix(lbH(i), lbH(i + vec2(1, 0)), f.x), mix(lbH(i + vec2(0, 1)), lbH(i + vec2(1, 1)), f.x), f.y); }
float lbBrushN(vec3 p){ return (lbN(p.xz * 3.1 + p.y * 2.3) * 0.6 + lbN(p.xz * 9.7 - p.y * 5.1) * 0.4) * 2. - 1.; }
varying vec3 vViewPosition;
struct ToonMaterial { vec3 diffuseColor; };
void RE_Direct_Toon(const in IncidentLight directLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in ToonMaterial material, inout ReflectedLight reflectedLight) {
  float hl = dot(geometryNormal, directLight.direction) * 0.5 + 0.5 + lbBrushN(vWPos) * lbBrush;
  float band = smoothstep(lbRemapMin, lbRemapMax, hl);
  reflectedLight.directDiffuse += directLight.color * band * BRDF_Lambert(material.diffuseColor);
  if (lbMetal > 0.) { vec3 H = normalize(directLight.direction + geometryViewDir); float g = smoothstep(0.82, 0.9, dot(geometryNormal, H) + lbBrushN(vWPos * 1.7) * 0.03);
    reflectedLight.directSpecular += directLight.color * g * lbMetal * (material.diffuseColor * 1.6 + 0.25); }
}
void RE_IndirectDiffuse_Toon(const in vec3 irradiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in ToonMaterial material, inout ReflectedLight reflectedLight) {
  vec3 wN = normalize((vec4(geometryNormal, 0.) * viewMatrix).xyz);
  float top = smoothstep(lbTopStart, lbTopEnd, wN.y) * lbTopStr;
  reflectedLight.indirectDiffuse += (irradiance + lbTop * top) * BRDF_Lambert(material.diffuseColor);
}
#define RE_Direct RE_Direct_Toon
#define RE_IndirectDiffuse RE_IndirectDiffuse_Toon`;
const LIT_RIM = `{ vec3 lbV = normalize(vViewPosition); float lbR = pow(1. - saturate(dot(normal, lbV)), lbRimTight) * lbRimStr; outgoingLight += lbRim * lbR * (0.55 + 0.45 * diffuseColor.rgb); }`;
function L(color, o = {}) {
  const metal = o.metal || 0;
  const m = new THREE.MeshToonMaterial({ color, vertexColors: !!o.vc, side: o.side || THREE.FrontSide, ...(o.map ? { map: o.map } : {}), ...(o.bump ? { bumpMap: o.bump, bumpScale: o.bumpScale || 1 } : {}) });
  m.onBeforeCompile = sh => {
    Object.assign(sh.uniforms, LOOK, { lbMetal: { value: metal } });
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nvarying vec3 vWPos;').replace('#include <project_vertex>', '#include <project_vertex>\nvWPos = (modelMatrix * vec4(transformed, 1.0)).xyz;');
    sh.fragmentShader = sh.fragmentShader.replace('#include <lights_toon_pars_fragment>', LIT_PARS).replace('#include <opaque_fragment>', LIT_RIM + '\n#include <opaque_fragment>');
  };
  m.customProgramCacheKey = () => 'lbLit' + metal;
  return m;
}
const STONE = L(0x8c9182), STONE_DK = L(0x5d6658), MOSS = L(0x4a6e3a), MOSS_DK = L(0x2f5530);
const BRASS = L(0x9a7a44, { metal: 1 }), VERDI = L(0x4e8a78, { metal: 0.4 }), BARK = L(0x4a3a30);
const BLOSSOM = [0xc23a6e, 0xd95a8c, 0x9c2c5a, 0xe47aa2].map(c => L(c));
const TEAL_GLOW = new THREE.MeshBasicMaterial({ color: new THREE.Color(0x5ff2dc).multiplyScalar(1.6), toneMapped: false });
const WARM_GLOW = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffc477).multiplyScalar(1.5), toneMapped: false });

// smooth normals across UV seams: average the normals of vertices that share a position
function weldNormals(geo) {
  geo.computeVertexNormals(); const p = geo.attributes.position, n = geo.attributes.normal, acc = new Map();
  const key = i => `${p.getX(i).toFixed(3)},${p.getY(i).toFixed(3)},${p.getZ(i).toFixed(3)}`;
  for (let i = 0; i < p.count; i++) { const k = key(i), a = acc.get(k) || [0, 0, 0]; a[0] += n.getX(i); a[1] += n.getY(i); a[2] += n.getZ(i); acc.set(k, a); }
  for (let i = 0; i < p.count; i++) { const a = acc.get(key(i)), l = Math.hypot(a[0], a[1], a[2]) || 1; n.setXYZ(i, a[0] / l, a[1] / l, a[2] / l); }
  return geo;
}
const vnoise3 = (x, y, z) => (vnoise(x + z * 0.71, y - z * 0.37) + vnoise(y * 1.13 - x * 0.4, z + x * 0.29)) * 0.5;
// position-based rock displacement (seamless): broad lumps, horizontal strata and fine chisel detail, then
// Windblown-style peak/valley colour (§4): large-scale noise blends a warm and a cool stone, crevices darken, moss on top
function rockify(geo, amp, freq, mossFrom, seedOff = 0) {
  const p = geo.attributes.position, col = [], c = new THREE.Color(), peak = new THREE.Color(0x949a8e), valley = new THREE.Color(0x58625f), crev = new THREE.Color(0x2e3836), moss = new THREE.Color(0x4a6c38), moss2 = new THREE.Color(0x6a8a42);
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i), s = seedOff * 1.7;
    const big = vnoise3(x * freq + s, y * freq * 0.6, z * freq) - 0.5;
    const strata = Math.sin(y * 1.9 + vnoise(x * 0.3 + s, z * 0.3) * 4) * 0.5;
    const fine = vnoise3(x * freq * 4 + s, y * freq * 4, z * freq * 4) - 0.5;
    const d = big + strata * 0.25 + fine * 0.3, k = 1 + d * amp;
    p.setXYZ(i, x * k, y + big * amp * 1.5, z * k);
    const pv = vnoise(x * 0.06 + s, z * 0.06 + y * 0.04);
    c.copy(valley).lerp(peak, pv); c.lerp(crev, smooth(0.0, -0.35, d) * 0.7);
    if (y > mossFrom) { const m = Math.max(0, Math.min(0.9, (y - mossFrom) * 0.35 + big * 1.4 + 0.1 + strata * 0.3)); c.lerp(fine > 0 ? moss : moss2, m); }
    col.push(c.r, c.g, c.b);
  }
  geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3)); return weldNormals(geo);
}
const ROCK = L(0xffffff, { vc: true });
// rounded, lumpy blobs for moss, ivy and blossom canopies (a few cached variants, scaled per use)
const BLOBS = [0, 1, 2, 3, 4, 5].map(v => { const g = new THREE.IcosahedronGeometry(1, 4), p = g.attributes.position;
  for (let i = 0; i < p.count; i++) { const x = p.getX(i), y = p.getY(i), z = p.getZ(i), k = 1 + (vnoise3(x * 2.2 + v * 7, y * 2.2, z * 2.2) - 0.5) * 0.45 + (vnoise3(x * 6 + v, y * 6, z * 6) - 0.5) * 0.12; p.setXYZ(i, x * k, y * k, z * k); }
  return weldNormals(g); });
let blobN = 0;
const blobGeo = r => { const g = BLOBS[blobN++ % BLOBS.length]; return { g, r }; };
function blob(spec, mat) { const g = new THREE.Group(), m = new THREE.Mesh(spec.g, mat); m.scale.setScalar(spec.r); m.castShadow = true; g.add(m); return g; }
// bevelled stone block (unit size), so cut stone catches a highlight on its edges instead of reading as a raw box
const curbGeo = (() => { const s = new THREE.Shape(), r = 0.08, h = 0.5 - 0.05;
  s.moveTo(-h + r, -h); s.lineTo(h - r, -h); s.quadraticCurveTo(h, -h, h, -h + r); s.lineTo(h, h - r); s.quadraticCurveTo(h, h, h - r, h); s.lineTo(-h + r, h); s.quadraticCurveTo(-h, h, -h, h - r); s.lineTo(-h, -h + r); s.quadraticCurveTo(-h, -h, -h + r, -h);
  const g = new THREE.ExtrudeGeometry(s, { depth: 0.9, bevelEnabled: true, bevelThickness: 0.05, bevelSize: 0.05, bevelSegments: 3, curveSegments: 4 }); g.translate(0, 0, -0.45); g.computeVertexNormals(); return g; })();

// ---- floor painter: concentric rings of worn green-grey slabs, moss in the grout, aged brass inlay; colour + height ----
function paintFloor(N, H, seed) {
  const r = mulberry32(seed), rings = [];
  let rr = DIAL_R + 0.25;
  rings.push({ a: 0, b: rr, cuts: [0, 1], off: 0 });
  while (rr < H) { // each ring is cut at irregular angles; now and then a long slab, so it reads as hand-laid flagstone, not a grid
    const w = 0.85 + r() * 0.6, mid = rr + w / 2, circ = Math.PI * 2 * mid, cuts = [];
    let u = 0; while (u < 1 - 0.6 / circ) { cuts.push(u); u += (r() < 0.12 ? 2.0 + r() * 0.9 : 0.7 + r() * 0.9) / circ; }
    cuts.push(1); rings.push({ a: rr, b: rr + w, cuts, off: r() }); rr += w;
  }
  const [map, mc] = canvas2d(N), [bump, bc] = canvas2d(N), mi = mc.createImageData(N, N), bi = bc.createImageData(N, N), md = mi.data, bd = bi.data;
  const grout = 0.03, bev = 0.1;
  for (let py = 0; py < N; py++) {
    const z = -H + (py + 0.5) / N * 2 * H;
    for (let px = 0; px < N; px++) {
      const x = -H + (px + 0.5) / N * 2 * H, o = (py * N + px) * 4, d = Math.hypot(x, z), th = Math.atan2(z, x);
      let ri = 0; while (ri < rings.length - 1 && d > rings[ri].b) ri++;
      const g = rings[ri], cu = g.cuts; let u = (th + Math.PI) / (Math.PI * 2) + g.off; u -= Math.floor(u);
      let lo = 0, hi = cu.length - 1; while (hi - lo > 1) { const m = (lo + hi) >> 1; if (cu[m] <= u) lo = m; else hi = m; }
      const k = lo, fa = (u - cu[k]) * Math.PI * 2 * d, arcW = (cu[k + 1] - cu[k]) * Math.PI * 2 * d;
      const edge = ri === 0 ? (g.b - d) : Math.min(d - g.a, g.b - d, fa, arcW - fa), sid = hash(ri * 31 + k, seed);
      const n = fbm(x * 0.35, z * 0.35), fine = vnoise(x * 7, z * 7), chip = vnoise(x * 2.3 + sid * 9, z * 2.3);
      const v = (sid - 0.5) * 0.24 + (n - 0.5) * 0.3 + (fine - 0.5) * 0.08, tint = hash(k, ri * 7 + seed);
      let cr = (118 + tint * 12) * (1 + v), cg = (124 + tint * 8) * (1 + v), cb = (118 - tint * 8) * (1 + v), h = 0.6 + 0.4 * smooth(grout * 0.5, bev * 1.5, edge);
      if (ri === 0) { cr = 70 + n * 20; cg = 92 + n * 20; cb = 90 + n * 18; h = 0.55; }                    // dial plate: dark teal stone
      if (edge < bev && ri > 0) { const t = 1 - smooth(grout, bev, edge); cr *= 1 - t * 0.18; cg *= 1 - t * 0.16; cb *= 1 - t * 0.2; }
      h += (sid - 0.5) * 0.14;                                                                             // slabs settled unevenly
      if (chip < 0.15) { cr *= 0.84; cg *= 0.84; cb *= 0.82; h -= 0.08; }
      if (fine > 0.86) { cr *= 1.07; cg *= 1.07; cb *= 1.04; }
      // moss: patches that thicken toward the rim and creep along the joints; kept low-contrast so telegraphs read on top
      const mossAmt = smooth(0.56, 0.74, n + smooth(8, H, d) * 0.28 - smooth(0, 6, d) * 0.12 + (fine - 0.5) * 0.12);
      if (mossAmt > 0) { const mg = 0.75 + fine * 0.35; cr += (70 * mg - cr) * mossAmt * 0.7; cg += (96 * mg - cg) * mossAmt * 0.7; cb += (60 * mg - cb) * mossAmt * 0.7; }
      if (edge < grout && ri > 0) { const m = Math.min(1, mossAmt * 2 + 0.25); cr = 46 - m * 8; cg = 52 + m * 12; cb = 44 - m * 6; h = 0.2; }
      md[o] = Math.min(255, cr); md[o + 1] = Math.min(255, cg); md[o + 2] = Math.min(255, cb); md[o + 3] = 255;
      const hb = Math.max(0, Math.min(255, h * 255)); bd[o] = bd[o + 1] = bd[o + 2] = hb; bd[o + 3] = 255;
    }
  }
  mc.putImageData(mi, 0, 0); bc.putImageData(bi, 0, 0);
  const s = N / (2 * H), C = N / 2;
  // aged brass inlay rings, broken in places (decay), with verdigris
  const inlay = (rad, w, gaps, col) => {
    let a = r() * 6.28; const end = a + Math.PI * 2;
    while (a < end) { const len = 0.5 + r() * 1.4; mc.strokeStyle = r() < 0.3 ? '#5f9a84' : col; mc.lineWidth = w * s;
      mc.beginPath(); mc.arc(C, C, rad * s, a, Math.min(end, a + len)); mc.stroke(); bc.strokeStyle = '#ddd'; bc.lineWidth = w * s * 0.8; bc.beginPath(); bc.arc(C, C, rad * s, a, Math.min(end, a + len)); bc.stroke();
      a += len + (r() < gaps ? 0.15 + r() * 0.4 : 0.02); }
  };
  inlay(DIAL_R + 0.12, 0.12, 0.15, '#a8844a'); inlay(DIAL_R - 0.1, 0.05, 0.2, '#8f7444');
  inlay(9.4, 0.1, 0.35, '#977846'); inlay(9.75, 0.05, 0.45, '#86703f'); inlay(R - 0.25, 0.14, 0.25, '#977846');
  for (let i = 0; i < 12; i++) { // hour ticks between the two outer inlays
    if (r() < 0.25) continue; const a = i / 12 * Math.PI * 2;
    mc.strokeStyle = '#8f7444'; mc.lineWidth = 0.1 * s; mc.beginPath(); mc.moveTo(C + Math.cos(a) * 9.8 * s, C + Math.sin(a) * 9.8 * s); mc.lineTo(C + Math.cos(a) * 10.6 * s, C + Math.sin(a) * 10.6 * s); mc.stroke();
  }
  for (let i = 0; i < 70; i++) { // hairline cracks
    let x = (r() * 2 - 1) * H, z = (r() * 2 - 1) * H, a = r() * 6.28; if (Math.hypot(x, z) > H) continue;
    mc.strokeStyle = 'rgba(38,44,34,0.55)'; mc.lineWidth = 1.6; bc.strokeStyle = 'rgba(30,30,30,0.7)'; bc.lineWidth = 1.6;
    mc.beginPath(); bc.beginPath(); mc.moveTo(C + x * s, C + z * s); bc.moveTo(C + x * s, C + z * s);
    for (let k = 0; k < 3 + r() * 5; k++) { a += (r() - 0.5) * 1.3; x += Math.cos(a) * 0.2; z += Math.sin(a) * 0.2; mc.lineTo(C + x * s, C + z * s); bc.lineTo(C + x * s, C + z * s); }
    mc.stroke(); bc.stroke();
  }
  // roots creeping in from the rim between the exits, raised in the height map
  for (let i = 0; i < 22; i++) {
    let a = r() * Math.PI * 2; if (nearExit(a, 0.22)) continue;
    let d = H + 0.2, w = 0.16 + r() * 0.1; const stop = 11.2 + r() * 2;
    mc.lineCap = bc.lineCap = 'round';
    while (d > stop && w > 0.03) {
      const a2 = a + (r() - 0.5) * 0.06, d2 = d - 0.25 - r() * 0.2, X = C + Math.cos(a) * d * s, Y = C + Math.sin(a) * d * s, X2 = C + Math.cos(a2) * d2 * s, Y2 = C + Math.sin(a2) * d2 * s;
      mc.strokeStyle = '#3b3326'; mc.lineWidth = w * s; mc.beginPath(); mc.moveTo(X, Y); mc.lineTo(X2, Y2); mc.stroke();
      mc.strokeStyle = 'rgba(120,104,78,0.6)'; mc.lineWidth = w * s * 0.35; mc.beginPath(); mc.moveTo(X - 1, Y - 1); mc.lineTo(X2 - 1, Y2 - 1); mc.stroke();
      bc.strokeStyle = '#fff'; bc.lineWidth = w * s; bc.beginPath(); bc.moveTo(X, Y); bc.lineTo(X2, Y2); bc.stroke();
      if (r() < 0.12) { const b = a2 + (r() < 0.5 ? 0.05 : -0.05); mc.strokeStyle = '#3b3326'; mc.lineWidth = w * s * 0.5; mc.beginPath(); mc.moveTo(X2, Y2); mc.lineTo(C + Math.cos(b) * (d2 - 0.6) * s, C + Math.sin(b) * (d2 - 0.6) * s); mc.stroke(); }
      a = a2; d = d2; w *= 0.93;
    }
  }
  // fallen blossom petals and leaves, drifted toward the rim; small and low contrast so they never read as a hazard
  for (let i = 0; i < 900; i++) {
    const a = r() * Math.PI * 2, d = 6 + Math.sqrt(r()) * (H - 6.3), X = C + Math.cos(a) * d * s, Y = C + Math.sin(a) * d * s;
    if (r() > smooth(6, H, d) * 1.1) continue;
    const petal = r() < 0.6; mc.fillStyle = petal ? `rgba(${190 + r() * 40},${80 + r() * 40},${120 + r() * 30},0.7)` : `rgba(${80 + r() * 40},${86 + r() * 30},${40},0.75)`;
    mc.beginPath(); mc.ellipse(X, Y, (petal ? 0.06 : 0.09) * s, (petal ? 0.035 : 0.045) * s, r() * 3.14, 0, Math.PI * 2); mc.fill();
  }
  return { map, bump };
}

// ---- resonance dial glow: turquoise rings, runes and a star; a separate additive layer so it can dim (quiet) ----
function paintDial(N) {
  const [c, g] = canvas2d(N), C = N / 2, s = N / (2 * DIAL_R);
  g.strokeStyle = 'rgba(110,255,232,0.95)'; g.fillStyle = 'rgba(110,255,232,0.95)'; g.lineCap = 'round';
  const ring = (rad, w) => { g.lineWidth = w * s; g.beginPath(); g.arc(C, C, rad * s, 0, Math.PI * 2); g.stroke(); };
  ring(3.25, 0.07); ring(2.85, 0.04); ring(1.7, 0.05); ring(0.55, 0.06);
  for (let i = 0; i < 24; i++) { // rune marks between the two outer rings
    const a = i / 24 * Math.PI * 2, r0 = 2.92, r1 = 3.18; g.lineWidth = 0.035 * s; g.beginPath();
    if (i % 2) { g.moveTo(C + Math.cos(a) * r0 * s, C + Math.sin(a) * r0 * s); g.lineTo(C + Math.cos(a) * r1 * s, C + Math.sin(a) * r1 * s); }
    else { g.arc(C + Math.cos(a) * 3.05 * s, C + Math.sin(a) * 3.05 * s, 0.07 * s, 0, Math.PI * 2); }
    g.stroke();
  }
  g.lineWidth = 0.035 * s; g.beginPath(); // eight-point star
  for (let i = 0; i <= 16; i++) { const a = i / 16 * Math.PI * 2, rad = i % 2 ? 0.75 : 2.6; const X = C + Math.cos(a) * rad * s, Y = C + Math.sin(a) * rad * s; i ? g.lineTo(X, Y) : g.moveTo(X, Y); }
  g.stroke();
  const grd = g.createRadialGradient(C, C, 0, C, C, 0.9 * s); grd.addColorStop(0, 'rgba(170,255,240,0.9)'); grd.addColorStop(1, 'rgba(110,255,232,0)');
  g.fillStyle = grd; g.beginPath(); g.arc(C, C, 0.9 * s, 0, Math.PI * 2); g.fill();
  return tex(c);
}

// ---- floor ----
const floorPaint = paintFloor(opts.floorRes || 3072, R + 0.2, 11);
const floorMat = L(0xffffff, { map: tex(floorPaint.map), bump: tex(floorPaint.bump, false), bumpScale: 1.4 });
const floor = add(new THREE.Mesh(new THREE.CircleGeometry(R + 0.2, 160).rotateX(-Math.PI / 2), floorMat)); floor.receiveShadow = true; floor.name = 'Floor';
const dialMat = new THREE.MeshBasicMaterial({ map: paintDial(1024), transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false, opacity: 0.55 });
const dial = add(new THREE.Mesh(new THREE.CircleGeometry(DIAL_R, 96).rotateX(-Math.PI / 2), dialMat)); dial.position.y = 0.015; dial.name = 'ResonanceDial';
const dialLight = add(new THREE.PointLight(0x58f0d8, 6, 10, 2)); dialLight.position.set(0, 1.2, 0);
// aged brass gear ring around the dial, almost flush with the floor (5 cm) so it never trips movement or hides telegraphs
const gearRing = (() => {
  const sh = new THREE.Shape(), ro = DIAL_R + 0.5, ri = DIAL_R + 0.3, T = 40;
  for (let i = 0; i <= T * 4; i++) { const a = i / (T * 4) * Math.PI * 2, tooth = (i % 4 === 1 || i % 4 === 2) ? ro : ro - 0.14; i ? sh.lineTo(Math.cos(a) * tooth, Math.sin(a) * tooth) : sh.moveTo(Math.cos(a) * tooth, Math.sin(a) * tooth); }
  const hole = new THREE.Path(); hole.absarc(0, 0, ri, 0, Math.PI * 2, true); sh.holes.push(hole);
  const geo = new THREE.ExtrudeGeometry(sh, { depth: 0.05, bevelEnabled: false, curveSegments: 64 }).rotateX(-Math.PI / 2);
  const m = add(new THREE.Mesh(geo, L(0x8c7442, { metal: 1 }))); m.receiveShadow = true; m.name = 'DialGear';
  for (let i = 0; i < 8; i++) { // verdigris rivets on the gear
    const a = i / 8 * Math.PI * 2 + 0.2, rv = add(new THREE.Mesh(new THREE.CylinderGeometry(0.07, 0.07, 0.03, 8), VERDI), m); rv.position.set(Math.cos(a) * (ri + 0.1), 0.06, Math.sin(a) * (ri + 0.1)); }
  return m;
})();

// ---- platform body: brick drum under the curb, then a rocky root tapering into the abyss ----
function paintBricks(W, Hh) {
  const [c, g] = canvas2d(W, Hh), r = mulberry32(5), rowH = Hh / 5;
  g.fillStyle = '#3d453b'; g.fillRect(0, 0, W, Hh);
  for (let y = 0; y < Hh; y += rowH) { let x = -r() * 60; while (x < W) { const w = 50 + r() * 70, l = 92 + r() * 30 - y / Hh * 30; g.fillStyle = `rgb(${l},${l + 4},${l - 10})`; g.fillRect(x + 2, y + 2, w - 4, rowH - 4); x += w; } }
  for (let i = 0; i < 120; i++) { const x = r() * W, len = 10 + r() * Hh * 0.6; const grd = g.createLinearGradient(0, 0, 0, len); grd.addColorStop(0, 'rgba(70,110,50,0.85)'); grd.addColorStop(1, 'rgba(70,110,50,0)'); g.fillStyle = grd; g.fillRect(x, 0, 4 + r() * 14, len); }
  const t = tex(c); t.wrapS = THREE.RepeatWrapping; t.repeat.set(8, 1); return t;
}
const drum = add(new THREE.Mesh(new THREE.CylinderGeometry(R + 1.05, R + 0.85, 2.6, 96, 1, true), L(0xffffff, { map: paintBricks(2048, 512) })));
drum.position.y = -1.3; drum.receiveShadow = true;
const rootPts = [[R + 0.9, -2.5], [R + 0.4, -3.6], [R - 1.5, -6.5], [12, -9.5], [9.5, -14], [7, -20], [4.4, -27], [2.2, -34], [0.6, -40], [0, -42]].map(([x, y]) => new THREE.Vector2(x, y));
const rootRock = add(new THREE.Mesh(rockify(new THREE.LatheGeometry(new THREE.SplineCurve(rootPts).getPoints(48), 128), 0.22, 0.22, -6), ROCK)); rootRock.name = 'RockRoot';

// ---- curb and parapet: broken blocks around the rim, open at the four exits ----
for (let i = 0; i < 56; i++) {
  const a = (i + 0.5) / 56 * Math.PI * 2; if (nearExit(a, 0.13)) continue;
  const broken = rnd() < 0.18, h = broken ? 0.25 + rnd() * 0.2 : 0.55 + rnd() * 0.12, rad = R + 0.5;
  const b = add(new THREE.Mesh(curbGeo, rnd() < 0.5 ? STONE : STONE_DK)); b.scale.set(1.0, h, Math.PI * 2 * rad / 56 - 0.08);
  b.position.set(Math.cos(a) * rad, h / 2 - 0.02, Math.sin(a) * rad); b.rotation.y = -a; b.rotation.z = (rnd() - 0.5) * 0.06; b.castShadow = b.receiveShadow = true;
  if (!broken && rnd() < 0.55) { const m = add(blob(blobGeo(0.4 + rnd() * 0.25), rnd() < 0.5 ? MOSS : MOSS_DK)); m.scale.set(1.4, 0.45, 1.2); m.position.set(b.position.x, h + 0.05, b.position.z); m.rotation.y = rnd() * 6; }
  if (i % 4 === 0 && !broken) { // taller parapet post with a cap; low on the camera side so it never hides players
    const front = Math.sin(a) > 0.55, ph = front ? 0.9 : 1.6 + rnd() * 0.5;
    const p = add(new THREE.Mesh(curbGeo, STONE)); p.scale.set(0.75, ph, 0.75); p.position.set(Math.cos(a) * (R + 0.55), ph / 2, Math.sin(a) * (R + 0.55)); p.rotation.y = -a; p.castShadow = true;
    const cap = add(new THREE.Mesh(curbGeo, STONE_DK)); cap.scale.set(0.95, 0.18, 0.95); cap.position.set(p.position.x, ph + 0.09, p.position.z); cap.rotation.y = -a; cap.castShadow = true;
  }
}
// grass and fern tufts along the inside of the rim (outside the playable radius)
{
  const tuft = new THREE.ConeGeometry(0.07, 0.42, 6); tuft.translate(0, 0.21, 0);
  const im = add(new THREE.InstancedMesh(tuft, L(0x5f8d3e), 900)), M4 = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
  let n = 0;
  for (let c = 0; c < 70 && n < 900; c++) {
    const a = rnd() * Math.PI * 2; if (nearExit(a, 0.12)) continue; const cr = PLAY_R + 0.15 + rnd() * 0.4;
    for (let k = 0; k < 13 && n < 900; k++) { const aa = a + (rnd() - 0.5) * 0.09, rr = cr + (rnd() - 0.5) * 0.4, s = 0.6 + rnd() * 0.9;
      e.set((rnd() - 0.5) * 0.7, rnd() * 6, (rnd() - 0.5) * 0.7); q.setFromEuler(e); M4.compose(new THREE.Vector3(Math.cos(aa) * rr, 0, Math.sin(aa) * rr), q, new THREE.Vector3(s, s, s)); im.setMatrixAt(n++, M4); }
  }
  im.count = n;
}
// vines hanging off the drum
{
  const vg = new THREE.BoxGeometry(0.07, 1, 0.07); vg.translate(0, -0.5, 0);
  const im = add(new THREE.InstancedMesh(vg, MOSS_DK, 260)), M4 = new THREE.Matrix4(), q = new THREE.Quaternion();
  for (let i = 0; i < 260; i++) { const a = rnd() * Math.PI * 2, len = 0.8 + rnd() * rnd() * 7; q.setFromEuler(new THREE.Euler((rnd() - 0.5) * 0.1, 0, (rnd() - 0.5) * 0.1));
    M4.compose(new THREE.Vector3(Math.cos(a) * (R + 1.08), 0.1, Math.sin(a) * (R + 1.08)), q, new THREE.Vector3(1 + rnd(), len, 1 + rnd())); im.setMatrixAt(i, M4); }
}

// ---- lanterns: hanging bronze lanterns with a turquoise flame ----
const lanterns = [], halos = [];
const LANTERN_METAL = L(0x2c2a22, { metal: 0.6 });
const GLOW_SPRITE = (() => { const [c, g] = canvas2d(128), grd = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  grd.addColorStop(0, 'rgba(160,255,236,0.9)'); grd.addColorStop(0.25, 'rgba(95,242,220,0.35)'); grd.addColorStop(1, 'rgba(95,242,220,0)'); g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
  return new THREE.SpriteMaterial({ map: tex(c), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false }); })();
function lantern(parent, x, y, z, light) {
  const g = add(new THREE.Group(), parent); g.position.set(x, y, z);
  // bronze cage: six bars between two rings, a frosted teal glass core and a glow billboard (Windblown's "fake light", §9)
  for (let k = 0; k < 6; k++) { const a = k / 6 * Math.PI * 2; add(new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.018, 0.5, 6), LANTERN_METAL), g).position.set(Math.cos(a) * 0.18, 0, Math.sin(a) * 0.18); }
  for (const y of [-0.25, 0.25]) { const ring = add(new THREE.Mesh(new THREE.TorusGeometry(0.19, 0.025, 8, 24), LANTERN_METAL), g); ring.rotation.x = Math.PI / 2; ring.position.y = y; }
  add(new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.012, 0.5, 6), LANTERN_METAL), g).position.y = 0.6;
  const halo = add(new THREE.Sprite(GLOW_SPRITE), g); halo.scale.setScalar(1.6); halos.push(halo);
  add(new THREE.Mesh(new THREE.ConeGeometry(0.24, 0.22, 6), VERDI), g).position.y = 0.36;
  const core = add(new THREE.Mesh(new THREE.OctahedronGeometry(0.12, 0), TEAL_GLOW), g); core.scale.y = 1.4;
  if (light) { const pl = add(new THREE.PointLight(0x5ff2dc, 5, 9, 2), g); pl.position.y = -0.1; }
  lanterns.push(core); return g;
}

// ---- chimes: verdigris bells (the area's namesake) hung from towers and rim pylons; they sway gently ----
const chimes = [];
const BELL_GEO = weldNormals(new THREE.LatheGeometry(new THREE.SplineCurve([[0.001, 0.62], [0.12, 0.6], [0.2, 0.52], [0.23, 0.36], [0.27, 0.18], [0.36, 0.06], [0.43, 0.0], [0.4, -0.02], [0.33, 0.01]].map(([x, y]) => new THREE.Vector2(x, y - 0.62))).getPoints(40), 48));
function bell(s) {
  const g = new THREE.Group(); const b = add(new THREE.Mesh(BELL_GEO, VERDI), g); b.scale.setScalar(s); b.castShadow = true;
  const clap = add(new THREE.Mesh(new THREE.SphereGeometry(0.07 * s, 8, 6), TEAL_GLOW), g); clap.position.y = -0.52 * s;
  return g;
}
// rim pylons on the diagonals: tapered stone with a teal crystal seam, a brass arm and three hanging chimes.
// The two on the camera side are kept short so they never hide players.
function chimePylon(a) {
  const front = Math.sin(a) > 0.2, h = front ? 2.0 : 4.8, g = add(new THREE.Group()); g.position.set(Math.cos(a) * (R + 0.55), 0, Math.sin(a) * (R + 0.55)); g.rotation.y = -a;
  const p = add(new THREE.Mesh(new THREE.CylinderGeometry(0.42, 0.62, h, 4, 1).rotateY(Math.PI / 4), STONE), g); p.position.y = h / 2; p.castShadow = true;
  const seam = add(new THREE.Mesh(new THREE.BoxGeometry(0.06, h * 0.7, 0.16), TEAL_GLOW), g); seam.position.set(-0.47, h * 0.5, 0);
  const tip = add(new THREE.Mesh(new THREE.OctahedronGeometry(0.3, 0), TEAL_GLOW), g); tip.position.y = h + 0.45; tip.scale.y = 1.6; lanterns.push(tip);
  add(new THREE.Mesh(curbGeo, STONE_DK), g).position.set(0, h + 0.06, 0); g.children[g.children.length - 1].scale.set(0.95, 0.14, 0.95);
  const arm = add(new THREE.Mesh(new THREE.BoxGeometry(1.9, 0.09, 0.09), BRASS), g); arm.position.set(0.95, h - 0.4, 0);
  [0.5, 1.05, 1.6].forEach((x, k) => { const b = add(bell(0.42 + k * 0.08), g); b.position.set(x, h - 0.45, 0); chimes.push({ o: b, ph: rnd() * 6, amp: 0.18 }); });
  const m = add(blob(blobGeo(0.55), MOSS), g); m.scale.set(1.3, 0.5, 1.3); m.position.y = 0.1;
}
[Math.PI * 0.25, Math.PI * 0.75, Math.PI * 1.25, Math.PI * 1.75].forEach(chimePylon);

// ---- underside: hanging roots and floating rock debris around the platform ----
for (let i = 0; i < 34; i++) {
  const a = rnd() * Math.PI * 2, r0 = R + 0.3 + rnd() * 0.6, len = 3 + rnd() * 8, pts = [];
  for (let k = 0; k <= 5; k++) { const t = k / 5; pts.push(new THREE.Vector3(Math.cos(a) * (r0 - t * 1.5 + Math.sin(t * 5 + i) * 0.3), -2.4 - t * len, Math.sin(a) * (r0 - t * 1.5) + Math.cos(t * 4 + i) * 0.3)); }
  add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 32, 0.09 + rnd() * 0.12, 10), BARK));
}
const debris = [];
for (let i = 0; i < 26; i++) {
  const a = rnd() * Math.PI * 2, r = 17.5 + rnd() * 9, s = 0.4 + rnd() * 1.3; if (nearExit(a, 0.18)) continue;
  const y = Math.sin(a) > 0.3 ? -4 - rnd() * 10 : -1.5 - rnd() * 12;
  const m = add(new THREE.Mesh(rockify(new THREE.IcosahedronGeometry(s, 8), 0.55, 0.7 / s, s * 0.3, i), ROCK)); m.position.set(Math.cos(a) * r, y, Math.sin(a) * r); m.rotation.set(rnd() * 3, rnd() * 3, rnd() * 3);
  debris.push({ o: m, y, ph: rnd() * 6 });
}

// ---- gateways, bridges, stairs and towers at the four exits ----
function exitGate(a) {
  const g = add(new THREE.Group()); g.rotation.y = -a; const front = Math.abs(a - Math.PI / 2) < 0.1;
  const postH = front ? 1.1 : 3.6;
  for (const side of [-1, 1]) {
    const h = front ? postH : (side < 0 ? postH : postH * (0.75 + rnd() * 0.25));
    const p = add(new THREE.Mesh(curbGeo, STONE), g); p.scale.set(0.9, h, 0.9); p.position.set(R + 0.55, h / 2, side * 2.05); p.castShadow = true;
    add(new THREE.Mesh(curbGeo, STONE_DK), g).position.set(R + 0.55, h + 0.1, side * 2.05);
    g.children[g.children.length - 1].scale.set(1.15, 0.22, 1.15);
    if (!front && rnd() < 0.8) { const m = add(blob(blobGeo(0.5), MOSS), g); m.scale.set(1.2, 0.6, 1.2); m.position.set(R + 0.55, h + 0.3, side * 2.05); }
  }
  if (!front) {
    const lin = add(new THREE.Mesh(curbGeo, STONE), g); lin.scale.set(0.8, 0.45, 5.1); lin.position.set(R + 0.55, postH * 0.82 + 0.3, 0); lin.rotation.x = 0.06; lin.castShadow = true;
    lantern(g, R + 0.55, postH * 0.82 - 0.35, 0.9, true); lantern(g, R + 0.55, postH * 0.82 - 0.45, -1.1, false);
  }
  return g;
}
function bridge(a, stairs) {
  const g = add(new THREE.Group()); g.rotation.y = -a;
  const deckM = L(0x7f8676);
  const x0 = R + 1.0;
  if (!stairs) {
    const d = add(new THREE.Mesh(new THREE.BoxGeometry(9, 0.6, 3.6), deckM), g); d.position.set(x0 + 4.5, -0.32, 0); d.receiveShadow = d.castShadow = true;
    for (let s = -1; s <= 1; s += 2) for (let k = 0; k < 8; k++) { if (rnd() < 0.2) continue; const p = add(new THREE.Mesh(curbGeo, STONE), g); p.scale.set(0.3, 0.7, 0.3); p.position.set(x0 + 0.6 + k * 1.15, 0.35, s * 1.65); p.castShadow = true; }
    for (const px of [x0 + 2.5, x0 + 6.8]) { const col = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(0.9, 1.4, 22, 20, 16), 0.15, 0.4, 100, px), ROCK), g); col.position.set(px, -11.6, 0); }
  } else {
    const d = add(new THREE.Mesh(new THREE.BoxGeometry(2, 0.6, 3.6), deckM), g); d.position.set(x0 + 1, -0.32, 0); d.receiveShadow = true;
    for (let k = 0; k < 11; k++) { const st = add(new THREE.Mesh(new THREE.BoxGeometry(0.62, 0.36, 3.4), k % 3 ? deckM : STONE_DK), g); st.position.set(x0 + 2.3 + k * 0.6, -0.32 - (k + 1) * 0.34, 0); st.receiveShadow = st.castShadow = true; }
    const land = add(new THREE.Mesh(new THREE.BoxGeometry(3, 0.6, 3.8), deckM), g); land.position.set(x0 + 10.2, -4.4, 0);
    const col = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(1.4, 2.2, 22, 20, 16), 0.15, 0.4, 100, a), ROCK), g); col.position.set(x0 + 6, -15.5, 0);
  }
  // tower at the far end: octagonal stone, verdigris roof, lit windows, moss
  const tx = x0 + 12.5, ty = stairs ? -4.4 : 0;
  const t = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(2.4, 3.0, 40, 8, 24), 0.05, 0.3, 18.5, tx), ROCK), g); t.position.set(tx, ty - 13, 0); t.castShadow = true;
  for (const cy of [0.4, 6.5]) { const c = add(new THREE.Mesh(new THREE.CylinderGeometry(2.75, 2.75, 0.35, 8), STONE_DK), g); c.position.set(tx, ty + cy, 0); }
  // open belfry with a hanging chime bell, then the verdigris roof, a needle spire and four pinnacles
  for (let k = 0; k < 8; k++) { const wa = k / 8 * Math.PI * 2; const c = add(new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.2, 1.6, 12), STONE), g); c.position.set(tx + Math.cos(wa) * 2.35, ty + 7.5, Math.sin(wa) * 2.35); c.castShadow = true; }
  add(new THREE.Mesh(new THREE.CylinderGeometry(2.8, 2.8, 0.3, 8), STONE_DK), g).position.set(tx, ty + 8.4, 0);
  const tb = add(bell(1.0), g); tb.position.set(tx, ty + 8.2, 0); chimes.push({ o: tb, ph: rnd() * 6, amp: 0.06 });
  const roof = add(new THREE.Mesh(new THREE.ConeGeometry(3.0, 4.4, 8), VERDI), g); roof.position.set(tx, ty + 10.75, 0); roof.castShadow = true;
  const spire = add(new THREE.Mesh(new THREE.ConeGeometry(0.35, 3.2, 6), BRASS), g); spire.position.set(tx, ty + 14.4, 0);
  add(new THREE.Mesh(new THREE.OctahedronGeometry(0.28, 0), TEAL_GLOW), g).position.set(tx, ty + 13.0, 0);
  for (let k = 0; k < 4; k++) { const wa = k / 4 * Math.PI * 2 + Math.PI / 4; const pn = add(new THREE.Mesh(new THREE.ConeGeometry(0.3, 1.6, 4), VERDI), g); pn.position.set(tx + Math.cos(wa) * 2.7, ty + 9.3, Math.sin(wa) * 2.7);
    const bu = add(new THREE.Mesh(curbGeo, STONE_DK), g); bu.scale.set(0.6, 9, 0.9); bu.position.set(tx + Math.cos(wa) * 3.0, ty + 0.5, Math.sin(wa) * 3.0); bu.rotation.y = -wa; bu.rotation.z = 0; bu.castShadow = true; }
  for (let k = 0; k < 8; k++) { if (k === 4) continue; const wa = k / 8 * Math.PI * 2 + Math.PI / 8; const w = add(new THREE.Mesh(new THREE.PlaneGeometry(0.35, 0.7), TEAL_GLOW), g); w.position.set(tx + Math.cos(wa) * 2.66, ty - 2.2, Math.sin(wa) * 2.66); w.rotation.y = Math.PI / 2 - wa; }
  for (let k = 0; k < 8; k++) { if (k === 4) continue; const wa = k / 8 * Math.PI * 2 + Math.PI / 8; const w = add(new THREE.Mesh(new THREE.PlaneGeometry(0.45, 0.9), k % 3 ? TEAL_GLOW : WARM_GLOW), g); w.position.set(tx + Math.cos(wa) * 2.62, ty + 3.4, Math.sin(wa) * 2.62); w.rotation.y = Math.PI / 2 - wa; }
  const door = add(new THREE.Mesh(new THREE.BoxGeometry(0.4, 2.4, 1.6), new THREE.MeshBasicMaterial({ color: 0x0e1612 })), g); door.position.set(tx - 2.55, ty + 1.2, 0);
  for (let k = 0; k < 6; k++) { const m = add(blob(blobGeo(0.6 + rnd() * 0.5), rnd() < 0.5 ? MOSS : MOSS_DK), g); const wa = rnd() * 6.28; m.position.set(tx + Math.cos(wa) * 2.5, ty + 6.8 + rnd() * 0.6, Math.sin(wa) * 2.5); m.scale.y = 0.55; }
  lantern(g, tx - 2.9, ty + 4.4, 1.3, false);
  return g;
}
EXITS.forEach((a, i) => { exitGate(a); bridge(a, i === 1 || i === 2); });

// ---- blossom trees (the board's magenta accents) ----
function tree(x, y, z, s) {
  const g = add(new THREE.Group()); g.position.set(x, y, z); g.scale.setScalar(s); g.rotation.y = rnd() * 6;
  const curve = new THREE.CatmullRomCurve3([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0.3, 1.2, 0.1), new THREE.Vector3(-0.2, 2.4, 0.3), new THREE.Vector3(0.2, 3.2, 0)]);
  add(new THREE.Mesh(new THREE.TubeGeometry(curve, 32, 0.22, 12), BARK), g).castShadow = true;
  for (let k = 0; k < 20; k++) { const b = add(blob(blobGeo(0.6 + rnd() * 0.55), BLOSSOM[k % 4]), g); const a = rnd() * 6.28, r = rnd() * 1.4;
    b.position.set(Math.cos(a) * r, 3.0 + rnd() * 1.2 - r * 0.3, Math.sin(a) * r); b.castShadow = true; }
  return g;
}
// trees grow from mossy rock planters bulging out of the rim
for (const [x, z, s] of [[-13.6, -9.2, 1.0], [15.6, -6.5, 0.85], [-16.5, 6.2, 0.9]]) {
  const pl = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(1.9, 1.1, 3.4, 32, 10), 0.18, 0.6, 1.0, x), ROCK)); pl.position.set(x, -1.6, z);
  const top = add(blob(blobGeo(2.0), MOSS)); top.scale.set(1, 0.14, 1); top.position.set(x, 0.05, z);
  tree(x, 0.25, z, s);
}

// ---- floating ruin islands: arches, broken walls and shrines close around the arena ----
function ruinIsland(a, r, top, seed) {
  const x = Math.cos(a) * r, z = Math.sin(a) * r, rad = 3.6 + rnd() * 1.4;
  add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(rad, rad * 0.25, 14, 36, 24), 0.26, 0.3, 5.5, seed), ROCK)).position.set(x, top - 7, z);
  { const mt = add(blob(blobGeo(rad * 1.06), MOSS)); mt.scale.set(1, 0.12, 1); mt.position.set(x, top + 0.05, z); }
  const g = add(new THREE.Group()); g.position.set(x, top + 0.3, z); g.rotation.y = -a + Math.PI / 2 + (rnd() - 0.5) * 0.6;
  for (const sx of [-1.7, 1.7]) { const c = add(new THREE.Mesh(new THREE.CylinderGeometry(0.32, 0.38, 3.4, 16), STONE), g); c.position.set(sx, 1.7, 0); c.castShadow = true; add(new THREE.Mesh(curbGeo, STONE_DK), g).position.set(sx, 3.45, 0); }
  const arch = add(new THREE.Mesh(new THREE.TorusGeometry(1.7, 0.28, 12, 40, Math.PI * (0.55 + rnd() * 0.45)), STONE), g); arch.position.y = 3.5; arch.castShadow = true;
  const wall = add(new THREE.Mesh(curbGeo, STONE_DK), g); wall.scale.set(2.4, 1.2 + rnd() * 1.4, 0.45); wall.position.set((rnd() - 0.5) * 2, 0.8, -1.4); wall.rotation.z = (rnd() - 0.5) * 0.2;
  for (let k = 0; k < 4; k++) { const m = add(blob(blobGeo(0.4 + rnd() * 0.5), rnd() < 0.5 ? MOSS : MOSS_DK), g); m.position.set((rnd() - 0.5) * 4, rnd() * 3.6, (rnd() - 0.5) * 2); m.scale.y = 0.6; }
  if (rnd() < 0.5) tree(x + (rnd() - 0.5) * 2, top + 0.3, z + (rnd() - 0.5) * 2, 0.7 + rnd() * 0.3); else lantern(g, 0, 3.0, 0, false);
}
ruinIsland(Math.PI * 1.22, 27, -1.5, 3); ruinIsland(Math.PI * 1.78, 28, -0.5, 5); ruinIsland(Math.PI * 1.5 + 0.55, 36, 1.5, 9);
ruinIsland(Math.PI * 0.86, 28, -7, 7); ruinIsland(Math.PI * 0.14, 29, -8, 11);

// ---- rock pillars, ruins and waterfalls all around, down into the abyss ----
// waterfall shader: noise streaks scrolling down, a white foam lip at the top, soft side edges, fading out toward the bottom
const falls = [];
const FALL_VS = `varying vec2 vUv;
#include <fog_pars_vertex>
void main() { vUv = uv; vec4 mvPosition = modelViewMatrix * vec4(position, 1.0); gl_Position = projectionMatrix * mvPosition;
#include <fog_vertex>
}`;
const FALL_FS = `uniform float uTime, uRep, uW; varying vec2 vUv;
float h(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float n(vec2 p) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f); return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
#include <fog_pars_fragment>
void main() {
  float y = (1.0 - vUv.y) * uRep;
  vec2 p = vec2(vUv.x * uW, y * 1.4 - uTime * 1.5);
  float s = n(p) * 0.6 + n(p * vec2(2.3, 1.9) + 7.0) * 0.4;
  float streak = smoothstep(0.42, 0.85, s);
  float edge = smoothstep(0.0, 0.14, vUv.x) * smoothstep(1.0, 0.86, vUv.x);
  float foam = exp(-y * 5.0) * (0.6 + 0.4 * n(vec2(vUv.x * uW * 2.0, uTime * 2.0)));
  vec3 col = mix(vec3(0.30, 0.70, 0.70), vec3(0.88, 1.0, 0.97), clamp(streak * 0.85 + foam, 0.0, 1.0));
  float a = (0.38 + streak * 0.42 + foam * 0.6) * edge * smoothstep(uRep, uRep * 0.55, y);
  gl_FragColor = vec4(col, a);
#include <fog_fragment>
}`;
const fallMat = (rep, w = 12) => { const m = new THREE.ShaderMaterial({ uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uTime: { value: 0 }, uRep: { value: rep }, uW: { value: w } }]),
  vertexShader: FALL_VS, fragmentShader: FALL_FS, transparent: true, depthWrite: false, side: THREE.DoubleSide, fog: true }); falls.push(m); return m; };
function mistPuff(x, y, z, s) {
  const [c, g] = canvas2d(128); const grd = g.createRadialGradient(64, 64, 0, 64, 64, 64); grd.addColorStop(0, 'rgba(150,235,225,0.28)'); grd.addColorStop(1, 'rgba(150,235,225,0)'); g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
  const sp = add(new THREE.Sprite(new THREE.SpriteMaterial({ map: tex(c), transparent: true, depthWrite: false }))); sp.position.set(x, y, z); sp.scale.set(s, s * 0.6, 1); return sp;
}
// sheets pouring off the platform rim between the exits
for (const a of [Math.PI * 0.25, Math.PI * 0.75, Math.PI * 1.25, Math.PI * 1.75]) {
  const th = Math.PI / 2 - a, len = 0.22, f = add(new THREE.Mesh(new THREE.CylinderGeometry(R + 1.15, R + 2.4, 34, 10, 1, true, th - len / 2, len), fallMat(4))); f.position.y = -17.3; f.renderOrder = 2;
}
const pillarSpots = [];
for (let i = 0; i < 46; i++) {
  const a = rnd() * Math.PI * 2, r = 36 + rnd() * 80; if (nearExit(a, 0.12) && r < 46) continue;
  const front = Math.sin(a) > 0.2, top = front ? -18 + rnd() * 10 : -8 + rnd() * 14 + (r > 70 ? 8 : 0), rad = 2 + rnd() * 3.5 + r * 0.03, h = top + 70;
  // stacked strata: a few offset drums with mossy ledges between them, so the silhouette steps like the board's cliffs
  const p = { position: new THREE.Vector3(Math.cos(a) * r, 0, Math.sin(a) * r) };
  let yTop = top, rr = rad;
  for (let k = 0; k < 4; k++) {
    const sh = k === 3 ? 70 : 5 + rnd() * 9, rb = rr * (1.05 + rnd() * 0.15), ox = k ? (rnd() - 0.5) * rr * 0.25 : 0, oz = k ? (rnd() - 0.5) * rr * 0.25 : 0;
    const seg = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(rr, rb, sh, 28, Math.max(6, Math.round(sh))), 0.3, 0.16, sh / 2 - 1.8, i * 3.1 + k), ROCK)); seg.position.set(p.position.x + ox, yTop - sh / 2, p.position.z + oz); seg.rotation.y = rnd() * 6;
    if (k > 0) { const ledge = add(blob(blobGeo(rr * 1.1), rnd() < 0.5 ? MOSS : MOSS_DK)); ledge.scale.set(1, 0.1, 1); ledge.position.set(seg.position.x, yTop, seg.position.z); }
    yTop -= sh; rr = rb * 1.06;
  }
  const cap = add(blob(blobGeo(rad * 1.12), MOSS)); cap.scale.set(1, 0.16, 1); cap.position.set(p.position.x, top + 0.05, p.position.z); cap.rotation.y = rnd();
  pillarSpots.push({ x: p.position.x, z: p.position.z, top, rad, a, r });
  const roll = rnd();
  if (roll < 0.3) tree(p.position.x + (rnd() - 0.5) * rad, top + 0.4, p.position.z + (rnd() - 0.5) * rad, 0.9 + rnd() * 0.8);
  else if (roll < 0.55) { // ruined shrine: broken walls and an arch stub
    for (let k = 0; k < 3; k++) { const w = add(new THREE.Mesh(curbGeo, STONE)); const wh = 1.5 + rnd() * 3; w.scale.set(0.6, wh, rad * (0.6 + rnd() * 0.6)); w.position.set(p.position.x + (rnd() - 0.5) * rad, top + wh / 2, p.position.z + (rnd() - 0.5) * rad); w.rotation.y = rnd() * 3; }
  } else if (roll < 0.68) lantern(root, p.position.x, top + 2.6, p.position.z, false);
}
// big background waterfalls from the taller pillars, sheets turned to face the arena
pillarSpots.filter(p => p.top > 2).slice(0, 9).forEach(p => {
  const w = 2 + p.rad * 0.8, h = p.top + 60, f = add(new THREE.Mesh(new THREE.PlaneGeometry(w, h), fallMat(h / 18)));
  const dx = -p.x / p.r, dz = -p.z / p.r; f.position.set(p.x + dx * (p.rad * 1.15), p.top - h / 2 + 0.2, p.z + dz * (p.rad * 1.15)); f.lookAt(0, f.position.y, 0);
  mistPuff(f.position.x, -26, f.position.z, w * 4);
});
for (let i = 0; i < 10; i++) { const a = i / 10 * Math.PI * 2 + rnd() * 0.3; mistPuff(Math.cos(a) * 22, -28 - rnd() * 6, Math.sin(a) * 22, 26 + rnd() * 14); }

// ---- sea of mist below the islands (Windblown's "sea of clouds", §6): two scrolling noise layers lit teal from below ----
const mistMat = new THREE.ShaderMaterial({ transparent: true, depthWrite: false, fog: false, uniforms: { uTime: { value: 0 } },
  vertexShader: 'varying vec3 vW; void main(){ vec4 w = modelMatrix * vec4(position, 1.); vW = w.xyz; gl_Position = projectionMatrix * viewMatrix * w; }',
  fragmentShader: `uniform float uTime; varying vec3 vW;
    float h(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
    float n(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3. - 2. * f); return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
    float fbm(vec2 p){ float a = 0.5, s = 0.; for (int i = 0; i < 5; i++) { s += n(p) * a; p *= 2.03; a *= 0.5; } return s; }
    void main(){ vec2 p = vW.xz * 0.03; float d = length(vW.xz);
      float c = fbm(p + vec2(uTime * 0.012, uTime * 0.006)) * 0.6 + fbm(p * 2.1 - vec2(uTime * 0.02, 0.)) * 0.4;
      float dens = smoothstep(0.35, 0.75, c);
      vec3 col = mix(vec3(0.04, 0.15, 0.17), vec3(0.24, 0.52, 0.5), dens) + vec3(0.12, 0.42, 0.4) * exp(-d * 0.025) * 0.6;
      gl_FragColor = vec4(col, (0.3 + dens * 0.45) * smoothstep(260., 120., d)); }`,
});
for (const y of [-42, -32]) { const sea = add(new THREE.Mesh(new THREE.PlaneGeometry(520, 520).rotateX(-Math.PI / 2), mistMat)); sea.position.y = y; sea.renderOrder = -2; }

// ---- fireflies / resonance motes ----
const motes = (() => {
  const N = 180, pos = new Float32Array(N * 3), seed = [];
  for (let i = 0; i < N; i++) { const a = rnd() * 6.28, r = 6 + rnd() * 30; pos.set([Math.cos(a) * r, 0.5 + rnd() * 7, Math.sin(a) * r], i * 3); seed.push(rnd() * 100); }
  const [c, g] = canvas2d(64); const grd = g.createRadialGradient(32, 32, 0, 32, 32, 32); grd.addColorStop(0, 'rgba(200,255,240,1)'); grd.addColorStop(0.3, 'rgba(110,255,220,0.6)'); grd.addColorStop(1, 'rgba(110,255,220,0)'); g.fillStyle = grd; g.fillRect(0, 0, 64, 64);
  const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const pts = add(new THREE.Points(geo, new THREE.PointsMaterial({ map: tex(c), size: 0.35, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false, opacity: 0.8 })));
  return { pts, base: pos.slice(), seed };
})();

// ---- drifting blossom petals ----
const petals = (() => {
  const N = 140, pos = new Float32Array(N * 3);
  for (let i = 0; i < N; i++) pos.set([(rnd() - 0.5) * 56, rnd() * 15 - 3, (rnd() - 0.5) * 50 - 4], i * 3);
  const [c, g] = canvas2d(32); g.fillStyle = '#e57fa4'; g.beginPath(); g.ellipse(16, 16, 12, 6, 0.6, 0, Math.PI * 2); g.fill();
  const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  return { pts: add(new THREE.Points(geo, new THREE.PointsMaterial({ map: tex(c), size: 0.22, transparent: true, depthWrite: false, opacity: 0.85, alphaTest: 0.1 }))) };
})();
// ---- warm light shafts slanting in from the sun side, outside the arena floor ----
const rays = [];
{
  const [c, g] = canvas2d(64, 256); const grd = g.createLinearGradient(0, 0, 0, 256); grd.addColorStop(0, 'rgba(255,230,180,1)'); grd.addColorStop(1, 'rgba(255,230,180,0)'); g.fillStyle = grd; g.fillRect(0, 0, 64, 256);
  const m2 = document.createElement('canvas'); m2.width = 64; m2.height = 256; const mg = m2.getContext('2d'); const gx = mg.createLinearGradient(0, 0, 64, 0); gx.addColorStop(0, 'rgba(0,0,0,0)'); gx.addColorStop(0.5, '#000'); gx.addColorStop(1, 'rgba(0,0,0,0)'); mg.fillStyle = gx; mg.fillRect(0, 0, 64, 256);
  g.globalCompositeOperation = 'destination-in'; g.drawImage(m2, 0, 0); const t = tex(c);
  for (const [x, z, w] of [[-26, -14, 6], [-20, -24, 4], [-31, -4, 5], [-12, -32, 7], [6, -36, 4]]) {
    const m = new THREE.MeshBasicMaterial({ map: t, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0.055, side: THREE.DoubleSide, fog: false }); rays.push(m);
    const r = add(new THREE.Mesh(new THREE.PlaneGeometry(w, 60), m)); r.position.set(x, 4, z); r.rotation.set(0, Math.atan2(x, z), 0.38); r.renderOrder = 3;
  }
}

// ---- lights and atmosphere ----
let sun = null;
if (opts.lights !== false) {
  scene.background = new THREE.Color(0x0a2a30);
  scene.fog = new THREE.Fog(0x0f363b, 40, 160);
  add(new THREE.HemisphereLight(0x9cc8c4, 0x1b2624, 0.95));
  sun = add(new THREE.DirectionalLight(0xffe0b0, 2.2)); sun.position.set(-18, 34, -12); sun.castShadow = true;
  Object.assign(sun.shadow.camera, { left: -22, right: 22, top: 22, bottom: -22, near: 1, far: 90 }); sun.shadow.mapSize.set(2048, 2048); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03;
  const fill = add(new THREE.DirectionalLight(0x5fd8d0, 0.6)); fill.position.set(20, 12, 18);
}
root.traverse(o => { if (o.isMesh && !o.material.transparent && o.material !== TEAL_GLOW) { o.castShadow = o.castShadow || false; o.receiveShadow = true; } });

let glow = 1;
function update(t, dt, quiet) {
  glow += ((quiet ? 0.35 : 1) - glow) * Math.min(1, dt * 4);             // decoration dims while a telegraph is up
  dialMat.opacity = (0.48 + Math.sin(t * 1.3) * 0.07) * glow; dialLight.intensity = 6 * glow;
  dial.rotation.y = t * 0.03;
  for (const m of falls) m.uniforms.uTime.value = t;
  chimes.forEach(c => { c.o.rotation.z = Math.sin(t * 1.3 + c.ph) * c.amp; c.o.rotation.x = Math.sin(t * 0.9 + c.ph * 2) * c.amp * 0.5; });
  debris.forEach(d => { d.o.position.y = d.y + Math.sin(t * 0.5 + d.ph) * 0.35; d.o.rotation.y += dt * 0.05; });
  gearRing.rotation.y = -t * 0.015;
  const q = petals.pts.geometry.attributes.position;
  for (let i = 0; i < q.count; i++) { let y = q.getY(i) - dt * (0.45 + (i % 5) * 0.08); if (y < -3) y = 12; q.setXYZ(i, q.getX(i) + Math.sin(t * 0.8 + i) * dt * 0.5 + dt * 0.3, y, q.getZ(i) + Math.cos(t * 0.6 + i) * dt * 0.4); if (Math.abs(q.getX(i)) > 30) q.setX(i, -28); }
  q.needsUpdate = true; petals.pts.material.opacity = 0.85 * glow; rays.forEach(m => { m.opacity = 0.055 * (0.8 + 0.2 * Math.sin(t * 0.3)); });
  lanterns.forEach((l, i) => { l.scale.setScalar(1 + Math.sin(t * 5 + i) * 0.08); });
  halos.forEach((h, i) => { h.scale.setScalar((1.5 + Math.sin(t * 2.2 + i * 1.7) * 0.12) * (0.6 + 0.4 * glow)); });
  mistMat.uniforms.uTime.value = t;
  const p = motes.pts.geometry.attributes.position;
  for (let i = 0; i < p.count; i++) { const s = motes.seed[i]; p.setXYZ(i, motes.base[i * 3] + Math.sin(t * 0.4 + s) * 0.8, motes.base[i * 3 + 1] + Math.sin(t * 0.7 + s * 2) * 0.5, motes.base[i * 3 + 2] + Math.cos(t * 0.35 + s) * 0.8); }
  p.needsUpdate = true; motes.pts.material.opacity = 0.8 * glow;
}
return { R, PLAY_R, root, sun, update, paving: floorPaint };
};
