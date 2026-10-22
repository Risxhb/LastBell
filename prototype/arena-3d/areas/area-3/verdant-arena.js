// <verdant-arena> Area 3, The Verdant Chimes: the Unity-bound build of the arena, laid out from the user's top-down
// concept (reference/verdant-chimes-topdown.webp) per VERDANT-CHIMES-UNITY-PROMPT.md. Rings-and-cross brass floor with a
// turquoise mosaic centre, four bridges (the long one to the south is the entry bridge), overgrown belfries with brass
// bells on the diagonals, ruined arcades under the rim with waterfalls on every edge, aqueducts and mist all around.
// Classic script, same contract as ../../arena-env.js:
//   const env = window.buildVerdantArena(THREE, scene, { floorRes });   env.update(timeSeconds, dt, quiet) every frame.
// three.js coordinates: x east, -z north, +z south (toward the camera, where the entry bridge runs). Unity rotates the
// imported root 180 degrees about y, so there north is +Z and the entry bridge points to -Z (see the build prompt §3).
// Returns { R, PLAY_R, root, sun, update, floor: { map, bump, mask } }. Materials carry names the Unity builder remaps.
window.buildVerdantArena = function buildVerdantArena(THREE, scene, opts = {}) {
const R = 14.6, PLAY_R = 14.0;                          // floor radius inside the curb; playable radius (boundary)
const MOSAIC_R = 5.8, INNER_R = 3.1, MED_R = 1.9, MID_R = 7.3, OUTER_R = 14.2;
const BRIDGE_W = 4.0, EXITS = [0, Math.PI / 2, Math.PI, Math.PI * 1.5]; // east, south (entry), west, north
const ENTRY = Math.PI / 2, GAP = Math.asin((BRIDGE_W / 2 + 0.2) / (R + 0.5));
const DIAG = [Math.PI * 0.25, Math.PI * 0.75, Math.PI * 1.25, Math.PI * 1.75];
const front = a => Math.sin(a) > 0.2;                    // camera side (south half)

// ---- helpers ----
function mulberry32(a) { return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(33);
const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
const fbm = (x, y) => vnoise(x, y) * 0.55 + vnoise(x * 2.1, y * 2.1) * 0.3 + vnoise(x * 4.3, y * 4.3) * 0.15;
const vnoise3 = (x, y, z) => (vnoise(x + z * 0.71, y - z * 0.37) + vnoise(y * 1.13 - x * 0.4, z + x * 0.29)) * 0.5;
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const tex = (c, srgb = true) => { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t; };
const canvas2d = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };
const angDist = (a, b) => Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
const nearExit = (a, tol) => EXITS.some(e => angDist(a, e) < tol);
const nearDiag = (a, tol) => DIAG.some(e => angDist(a, e) < tol);

const root = new THREE.Group(); root.name = 'LB_ENV_VerdantChimes_Arena'; scene.add(root);
const add = (m, parent = root) => { parent.add(m); return m; };
const node = (name, x, y, z, parent = root) => { const o = new THREE.Object3D(); o.name = name; o.position.set(x, y, z); parent.add(o); return o; };

// ---- LB CustomLit look (Windblown teardown §1–2), the three.js twin of the Unity shader; LOOK = the LookSettings asset ----
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
function L(name, color, o = {}) {
  const metal = o.metal || 0;
  const m = new THREE.MeshToonMaterial({ name, color, vertexColors: !!o.vc, side: o.side || THREE.FrontSide, ...(o.map ? { map: o.map } : {}), ...(o.bump ? { bumpMap: o.bump, bumpScale: o.bumpScale || 1 } : {}) });
  m.userData.metal = metal;
  m.onBeforeCompile = sh => {
    Object.assign(sh.uniforms, LOOK, { lbMetal: { value: metal } });
    sh.vertexShader = sh.vertexShader.replace('#include <common>', '#include <common>\nvarying vec3 vWPos;').replace('#include <project_vertex>', '#include <project_vertex>\nvWPos = (modelMatrix * vec4(transformed, 1.0)).xyz;');
    sh.fragmentShader = sh.fragmentShader.replace('#include <lights_toon_pars_fragment>', LIT_PARS).replace('#include <opaque_fragment>', LIT_RIM + '\n#include <opaque_fragment>');
  };
  m.customProgramCacheKey = () => 'lbLit' + metal;
  return m;
}
const STONE = L('Stone', 0xb3ad9f), STONE_DK = L('StoneDark', 0x7d796e), MOSS = L('Moss', 0x4f7139), MOSS_DK = L('MossDark', 0x2f4f2c);
const IVY = L('Ivy', 0x3c6232), CANOPY = L('Canopy', 0x34552f), CANOPY_DK = L('CanopyDark', 0x1d3420), BARK = L('Bark', 0x4a3a2c), TIMBER = L('Timber', 0x5a4330);
const BRASS = L('Brass', 0xb0893f, { metal: 1 }), BELL = L('Bell', 0xd2a24c, { metal: 1 }), BRASS_DK = L('BrassDark', 0x7a5e2c, { metal: 0.7 });
const ROCK = L('Rock', 0xffffff, { vc: true }), FLOWER = L('Flower', 0xffffff, { vc: true });
const GLOW_AMBER = new THREE.MeshBasicMaterial({ name: 'GlowAmber', color: new THREE.Color(0xffc477).multiplyScalar(2.2), toneMapped: false });
const POOL_CHANNEL = new THREE.MeshBasicMaterial({ name: 'Pool', color: 0x2a8a88 });
const GLOW_TEAL = new THREE.MeshBasicMaterial({ name: 'GlowTeal', color: new THREE.Color(0x6ff5df).multiplyScalar(1.8), toneMapped: false });

// smooth normals across seams: average the normals of vertices that share a position
function weldNormals(geo) {
  geo.computeVertexNormals(); const p = geo.attributes.position, n = geo.attributes.normal, acc = new Map();
  const key = i => `${p.getX(i).toFixed(3)},${p.getY(i).toFixed(3)},${p.getZ(i).toFixed(3)}`;
  for (let i = 0; i < p.count; i++) { const k = key(i), a = acc.get(k) || [0, 0, 0]; a[0] += n.getX(i); a[1] += n.getY(i); a[2] += n.getZ(i); acc.set(k, a); }
  for (let i = 0; i < p.count; i++) { const a = acc.get(key(i)), l = Math.hypot(a[0], a[1], a[2]) || 1; n.setXYZ(i, a[0] / l, a[1] / l, a[2] / l); }
  return geo;
}
// rock displacement in local space (lumps, strata, chisel detail) + Windblown peak/valley colour (§4), crevices, moss on top
function rockify(geo, amp, freq, mossFrom, seedOff = 0) {
  const p = geo.attributes.position, col = [], c = new THREE.Color(), peak = new THREE.Color(0x9d988b), valley = new THREE.Color(0x5e655f), crev = new THREE.Color(0x2f3733), moss = new THREE.Color(0x4c6e37), moss2 = new THREE.Color(0x6a8a42);
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i), s = seedOff * 1.7;
    const big = vnoise3(x * freq + s, y * freq * 0.6, z * freq) - 0.5, strata = Math.sin(y * 1.9 + vnoise(x * 0.3 + s, z * 0.3) * 4) * 0.5, fine = vnoise3(x * freq * 4 + s, y * freq * 4, z * freq * 4) - 0.5;
    const d = big + strata * 0.1 + fine * 0.3, k = 1 + d * amp; p.setXYZ(i, x * k, y + big * amp * 1.5, z * k);
    c.copy(valley).lerp(peak, vnoise(x * 0.06 + s, z * 0.06 + y * 0.04)); c.lerp(crev, smooth(0.0, -0.35, d) * 0.7);
    if (y > mossFrom) c.lerp(fine > 0 ? moss : moss2, Math.max(0, Math.min(0.9, (y - mossFrom) * 0.35 + big * 1.4 + 0.1 + strata * 0.3)));
    col.push(c.r, c.g, c.b);
  }
  geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3)); return weldNormals(geo);
}
// lumpy unit blob variants for moss, ivy and canopies
const BLOBS = [0, 1, 2, 3, 4, 5].map(v => { const g = new THREE.IcosahedronGeometry(1, 3), p = g.attributes.position;
  for (let i = 0; i < p.count; i++) { const x = p.getX(i), y = p.getY(i), z = p.getZ(i), k = 1 + (vnoise3(x * 2.2 + v * 7, y * 2.2, z * 2.2) - 0.5) * 0.45 + (vnoise3(x * 6 + v, y * 6, z * 6) - 0.5) * 0.12; p.setXYZ(i, x * k, y * k, z * k); }
  return weldNormals(g); });
let blobN = 0;
function blob(r, mat, x, y, z, sy = 1, parent = root) { const m = add(new THREE.Mesh(BLOBS[blobN++ % 6], mat), parent); m.scale.set(r, r * sy, r); m.position.set(x, y, z); m.rotation.y = rnd() * 6; m.castShadow = true; return m; }
// bevelled unit stone block
const BLOCK = (() => { const s = new THREE.Shape(), r = 0.08, h = 0.45;
  s.moveTo(-h + r, -h); s.lineTo(h - r, -h); s.quadraticCurveTo(h, -h, h, -h + r); s.lineTo(h, h - r); s.quadraticCurveTo(h, h, h - r, h); s.lineTo(-h + r, h); s.quadraticCurveTo(-h, h, -h, h - r); s.lineTo(-h, -h + r); s.quadraticCurveTo(-h, -h, -h + r, -h);
  const g = new THREE.ExtrudeGeometry(s, { depth: 0.9, bevelEnabled: true, bevelThickness: 0.05, bevelSize: 0.05, bevelSegments: 3, curveSegments: 4 }); g.translate(0, 0, -0.45); g.computeVertexNormals(); return g; })();
function block(mat, sx, sy, sz, x, y, z, ry = 0, parent = root) { const b = add(new THREE.Mesh(BLOCK, mat), parent); b.scale.set(sx, sy, sz); b.position.set(x, y, z); b.rotation.y = ry; b.castShadow = b.receiveShadow = true; return b; }
// concatenate non-indexed geometries (position, normal, optional color) into one
function merge(geos) {
  const parts = geos.map(g => g.index ? g.toNonIndexed() : g), n = parts.reduce((s, g) => s + g.attributes.position.count, 0);
  const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3); let o = 0;
  for (const g of parts) { pos.set(g.attributes.position.array, o * 3); nor.set(g.attributes.normal.array, o * 3); if (g.attributes.color) col.set(g.attributes.color.array, o * 3); else col.fill(1, o * 3, (o + g.attributes.position.count) * 3); o += g.attributes.position.count; }
  const m = new THREE.BufferGeometry(); m.setAttribute('position', new THREE.BufferAttribute(pos, 3)); m.setAttribute('normal', new THREE.BufferAttribute(nor, 3)); m.setAttribute('color', new THREE.BufferAttribute(col, 3)); return m;
}
const tint = (g, hex) => { const c = new THREE.Color(hex), n = g.attributes.position.count, a = new Float32Array(n * 3); for (let i = 0; i < n; i++) a.set([c.r, c.g, c.b], i * 3); g.setAttribute('color', new THREE.BufferAttribute(a, 3)); return g; };
// arcade outline: a wall of length len and height h (bottom at y = 0) with n open arches springing at s, width w
function arcadeShape(len, h, n, w, s, pointed = false) {
  const sh = new THREE.Shape(); sh.moveTo(0, 0);
  for (let i = 0; i < n; i++) { const cx = (i + 0.5) * len / n, l = cx - w / 2, r = cx + w / 2;
    sh.lineTo(l, 0); sh.lineTo(l, s);
    if (pointed) { sh.quadraticCurveTo(l, s + w * 0.62, cx, s + w * 0.8); sh.quadraticCurveTo(r, s + w * 0.62, r, s); }
    else sh.absarc(cx, s, w / 2, Math.PI, 0, true);
    sh.lineTo(r, 0); }
  sh.lineTo(len, 0); sh.lineTo(len, h); sh.lineTo(0, h); sh.lineTo(0, 0); return sh;
}
const extrude = (shape, depth, bevel = 0.04) => { const g = new THREE.ExtrudeGeometry(shape, { depth, bevelEnabled: bevel > 0, bevelThickness: bevel, bevelSize: bevel, bevelSegments: 2, curveSegments: 16 }); g.translate(0, 0, -depth / 2); g.computeVertexNormals(); return g; };

// ---- instanced foliage, collected as we go and built at the end (Unity gets them baked into single meshes) ----
const ivyM = [], flowerM = [], tuftM = [], fernM = [], M4 = new THREE.Matrix4(), Q = new THREE.Quaternion(), E3 = new THREE.Euler(), V = new THREE.Vector3(), S = new THREE.Vector3();
const pushM = (list, x, y, z, s, sy = s, ry = rnd() * 6, rx = 0) => { E3.set(rx, ry, 0); Q.setFromEuler(E3); M4.compose(V.set(x, y, z), Q, S.set(s, sy, s)); list.push(M4.clone()); };
// a curtain of ivy hanging from (x, y, z) down `len` metres, pushed `out` along the face normal (nx, nz); white flowers dotted in
// Leaves are flattened against the wall (thin along the face normal) and jittered, strands wobble and thin out as they
// fall, and a heavier clump overflows at the top, so a curtain reads as a mass of foliage rather than a string of beads.
const pushLeaf = (x, y, z, s, nx, nz) => { E3.set((rnd() - 0.5) * 0.8, Math.atan2(nx, nz) + (rnd() - 0.5) * 0.6, (rnd() - 0.5) * 0.8); Q.setFromEuler(E3); M4.compose(V.set(x, y, z), Q, S.set(s * 1.15, s, s * 0.45)); ivyM.push(M4.clone()); };
function ivyCurtain(x, y, z, len, nx = 0, nz = 0, width = 0.8, flowers = 0.12) {
  const tx = -nz, tz = nx, strands = Math.max(2, Math.round(width / 0.22));
  for (let k = 0; k < 4 + width * 3; k++) { const o = (rnd() - 0.5) * width * 1.1, s = 0.24 + rnd() * 0.2; pushLeaf(x + tx * o + nx * 0.15, y + rnd() * 0.15, z + tz * o + nz * 0.15, s, nx, nz); }
  for (let c = 0; c < strands; c++) {
    const off = (c / (strands - 1) - 0.5) * width + (rnd() - 0.5) * 0.12, L2 = len * (0.35 + rnd() * 0.75), ph = rnd() * 6;
    for (let d = 0.1; d < L2; d += 0.16) {
      const fall = d / L2, s = (0.2 - fall * 0.1) * (0.75 + rnd() * 0.5), wob = Math.sin(d * 1.7 + ph) * 0.1, lo = off + wob + (rnd() - 0.5) * 0.1, out = 0.06 + rnd() * 0.08;
      pushLeaf(x + tx * lo + nx * out, y - d, z + tz * lo + nz * out, s, nx, nz);
      if (rnd() < 0.3) pushLeaf(x + tx * (lo + (rnd() - 0.5) * 0.18) + nx * out, y - d - 0.08, z + tz * (lo + (rnd() - 0.5) * 0.18) + nz * out, s * 0.8, nx, nz);
      if (rnd() < flowers) pushM(flowerM, x + tx * lo + nx * (out + 0.12), y - d, z + tz * lo + nz * (out + 0.12), 0.9 + rnd() * 0.5, undefined, rnd() * 6, -1.2);
    }
  }
}
// a mound of moss with flowers on top of a ledge
function mossMound(x, y, z, r, flowers = 4) { for (let k = 0; k < 3; k++) { const a = rnd() * 6.28, d = rnd() * r * 0.6; blob(r * (0.5 + rnd() * 0.4), rnd() < 0.5 ? MOSS : MOSS_DK, x + Math.cos(a) * d, y, z + Math.sin(a) * d, 0.55); }
  for (let k = 0; k < 4; k++) { const a = rnd() * 6.28; pushLeaf(x + Math.cos(a) * r * 0.8, y + 0.05, z + Math.sin(a) * r * 0.8, 0.16 + rnd() * 0.1, Math.cos(a), Math.sin(a)); } for (let i = 0; i < flowers; i++) { const a = rnd() * 6.28, d = rnd() * r * 0.8; pushM(flowerM, x + Math.cos(a) * d, y + r * 0.35, z + Math.sin(a) * d, 0.9 + rnd() * 0.6); } }

// ---- floor painter: warm grey slab rings, turquoise mosaic, brass rings + cross + roundels + medallion. Writes three
// canvases: albedo, height (Unity makes a normal map) and mask (R moss, G brass, B emissive mosaic grout) ----
function paintFloor(N, H) {
  const r = mulberry32(11), rings = [];
  let rr = MOSAIC_R + 0.18;
  while (rr < H) { const w = 0.9 + r() * 0.7, circ = Math.PI * 2 * (rr + w / 2), cuts = []; let u = 0;
    while (u < 1 - 0.6 / circ) { cuts.push(u); u += (r() < 0.25 ? 1.5 + r() * 0.7 : 0.7 + r() * 0.6) / circ; }
    cuts.push(1); rings.push({ a: rr, b: rr + w, cuts, off: r() }); rr += w; }
  const [map, mc] = canvas2d(N), [bump, bc] = canvas2d(N), [mask, kc] = canvas2d(N);
  const mi = mc.createImageData(N, N), bi = bc.createImageData(N, N), ki = kc.createImageData(N, N), md = mi.data, bd = bi.data, kd = ki.data;
  const TEAL = [[40, 92, 90], [50, 106, 102], [66, 118, 112], [32, 72, 74], [46, 100, 96]]; // around #2E6F6C // mid-value, desaturated: telegraphs must win
  for (let py = 0; py < N; py++) {
    const z = -H + (py + 0.5) / N * 2 * H;
    for (let px = 0; px < N; px++) {
      const x = -H + (px + 0.5) / N * 2 * H, o = (py * N + px) * 4, d = Math.hypot(x, z), th = Math.atan2(z, x);
      const n = fbm(x * 0.35, z * 0.35), fine = vnoise(x * 7, z * 7);
      let cr, cg, cb, h, moss = 0, glow = 0;
      if (d < MOSAIC_R + 0.02) {                                       // mosaic: 11 cm tiles set in rings
        const T = 0.11, ti = Math.floor(d / T), nt = Math.max(6, Math.round(Math.PI * 2 * (ti + 0.5) * T / T)), u = ((th + Math.PI) / (Math.PI * 2) + hash(ti, 3)) * nt, k = Math.floor(u);
        const ed = Math.min(d - ti * T, (ti + 1) * T - d, (u - k) * Math.PI * 2 * d / nt, (k + 1 - u) * Math.PI * 2 * d / nt), sid = hash(ti * 131 + k, 7);
        const pal = TEAL[Math.floor(sid * TEAL.length)], v = (hash(k, ti) - 0.5) * 0.16 + (n - 0.5) * 0.18;
        cr = pal[0] * (1 + v); cg = pal[1] * (1 + v); cb = pal[2] * (1 + v); h = 0.62;
        if (sid > 0.975) { cr = 150; cg = 146; cb = 134; }                 // a few replaced stone tiles
        if (ed < 0.012) { cr = 52; cg = 72; cb = 70; h = 0.45; glow = 1 - smooth(0, MOSAIC_R, d) * 0.3; }
      } else {                                                         // slab rings
        let ri = 0; while (ri < rings.length - 1 && d > rings[ri].b) ri++;
        const g = rings[ri], cu = g.cuts; let u = (th + Math.PI) / (Math.PI * 2) + g.off; u -= Math.floor(u);
        let lo = 0, hi = cu.length - 1; while (hi - lo > 1) { const m = (lo + hi) >> 1; if (cu[m] <= u) lo = m; else hi = m; }
        const fa = (u - cu[lo]) * Math.PI * 2 * d, arcW = (cu[lo + 1] - cu[lo]) * Math.PI * 2 * d, edge = Math.min(d - g.a, g.b - d, fa, arcW - fa), sid = hash(ri * 31 + lo, 5);
        const v = (sid - 0.5) * 0.18 + (n - 0.5) * 0.22 + (fine - 0.5) * 0.06, tn = hash(lo, ri * 7);
        cr = (158 + tn * 6) * (1 + v); cg = (156 + tn * 5) * (1 + v); cb = (150 - tn * 3) * (1 + v);
        h = 0.62 + 0.36 * smooth(0.012, 0.12, edge) + (sid - 0.5) * 0.12;
        if (edge < 0.1) { const t = 1 - smooth(0.02, 0.1, edge); cr *= 1 - t * 0.14; cg *= 1 - t * 0.14; cb *= 1 - t * 0.16; }
        moss = smooth(0.62, 0.8, n + (fine - 0.5) * 0.1) * smooth(11.2, R, d) * 0.7;
        if (moss > 0) { const mg = 0.78 + fine * 0.3; cr += (78 * mg - cr) * moss; cg += (104 * mg - cg) * moss; cb += (62 * mg - cb) * moss; }
        if (edge < 0.016) { const m = Math.min(1, moss * 2 + smooth(11.5, H, d) * (0.6 + n * 0.6)); cr = 94 - m * 30; cg = 90 - m * 8; cb = 80 - m * 26; h = 0.22; moss = Math.max(moss, m * 0.8); }
      }
      md[o] = Math.min(255, cr); md[o + 1] = Math.min(255, cg); md[o + 2] = Math.min(255, cb); md[o + 3] = 255;
      const hb = Math.max(0, Math.min(255, h * 255)); bd[o] = bd[o + 1] = bd[o + 2] = hb; bd[o + 3] = 255;
      kd[o] = moss * 255; kd[o + 1] = 0; kd[o + 2] = glow * 255; kd[o + 3] = d < MOSAIC_R + 0.02 ? 160 : 255;
    }
  }
  mc.putImageData(mi, 0, 0); bc.putImageData(bi, 0, 0); kc.putImageData(ki, 0, 0);
  const s = N / (2 * H), C = N / 2, P = (rad, a) => [C + Math.cos(a) * rad * s, C + Math.sin(a) * rad * s];
  // brass: drawn three times (albedo / height / mask) from the same layout
  function brass(g, mode) {
    const col = (light, dark) => mode === 'map' ? (dark ? '#5d4520' : light) : mode === 'bump' ? (dark ? '#9a9a9a' : '#ffffff') : (dark ? 'rgb(0,90,0)' : 'rgb(0,255,0)');
    if (mode === 'mask') g.globalCompositeOperation = 'lighter';
    g.lineCap = 'butt';
    const ring = (rad, w) => { g.strokeStyle = col(0, true); g.lineWidth = (w + 0.05) * s; g.beginPath(); g.arc(C, C, rad * s, 0, Math.PI * 2); g.stroke(); g.strokeStyle = col('#a8894a'); g.lineWidth = w * s; g.beginPath(); g.arc(C, C, rad * s, 0, Math.PI * 2); g.stroke();
      if (mode === 'map') { g.strokeStyle = 'rgba(200,170,105,0.45)'; g.lineWidth = w * 0.25 * s; g.beginPath(); g.arc(C, C, (rad - w * 0.2) * s, 0, Math.PI * 2); g.stroke(); } };
    const line = (r0, r1, a, w) => { const [x0, y0] = P(r0, a), [x1, y1] = P(r1, a); g.strokeStyle = col(0, true); g.lineWidth = (w + 0.05) * s; g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke(); g.strokeStyle = col('#a8894a'); g.lineWidth = w * s; g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke(); };
    const disc = (cx, cy, rad, fill) => { g.fillStyle = col(0, true); g.beginPath(); g.arc(cx, cy, (rad + 0.04) * s, 0, Math.PI * 2); g.fill(); g.fillStyle = col(fill); g.beginPath(); g.arc(cx, cy, rad * s, 0, Math.PI * 2); g.fill(); };
    const star = (cx, cy, r0, r1, pts, w) => { g.strokeStyle = col(0, true); g.lineWidth = w * s; g.beginPath(); for (let i = 0; i <= pts * 2; i++) { const a = i / (pts * 2) * Math.PI * 2 - Math.PI / 2, rd = i % 2 ? r0 : r1, X = cx + Math.cos(a) * rd * s, Y = cy + Math.sin(a) * rd * s; i ? g.lineTo(X, Y) : g.moveTo(X, Y); } g.stroke(); };
    const engr = (cx, cy, rad, w) => { g.strokeStyle = col(0, true); g.lineWidth = w * s; g.beginPath(); g.arc(cx, cy, rad * s, 0, Math.PI * 2); g.stroke(); };
    ring(OUTER_R, 0.16); ring(MID_R, 0.13); ring(MOSAIC_R + 0.05, 0.2); ring(INNER_R, 0.11);
    for (const a of EXITS) { line(MED_R, R + 0.25, a, 0.12); }
    for (const a of [Math.PI / 4, Math.PI * 0.75, Math.PI * 1.25, Math.PI * 1.75]) line(MED_R, INNER_R, a, 0.07); // spokes quartering the mosaic
    for (const a of EXITS) for (const [rad, rr2] of [[OUTER_R, 0.72], [MID_R, 0.6], [INNER_R, 0.42], [MOSAIC_R + 0.05, 0.34]]) {
      const [cx, cy] = P(rad, a); disc(cx, cy, rr2, '#86703f'); engr(cx, cy, rr2 * 0.78, 0.03); star(cx, cy, rr2 * 0.22, rr2 * 0.66, 4, 0.025);
      if (mode === 'map') { g.fillStyle = '#b39257'; g.beginPath(); g.arc(cx, cy, rr2 * 0.12 * s, 0, Math.PI * 2); g.fill(); }
    }
    // medallion: brass compass rose with a gear-tooth edge (the 3D gear ring sits just outside it)
    disc(C, C, MED_R, '#94783f'); engr(C, C, MED_R * 0.88, 0.04); engr(C, C, MED_R * 0.62, 0.03); engr(C, C, MED_R * 0.3, 0.03);
    star(C, C, MED_R * 0.18, MED_R * 0.84, 8, 0.035); star(C, C, MED_R * 0.12, MED_R * 0.5, 4, 0.03);
    for (let i = 0; i < 32; i++) { const a = i / 32 * Math.PI * 2, [x0, y0] = P(MED_R * 0.88, a), [x1, y1] = P(MED_R * (i % 4 ? 0.82 : 0.74), a); g.strokeStyle = col(0, true); g.lineWidth = 0.025 * s; g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke(); }
    if (mode === 'map') { g.fillStyle = '#d9b866'; g.beginPath(); g.arc(C, C, 0.16 * s, 0, Math.PI * 2); g.fill(); }
    g.globalCompositeOperation = 'source-over';
  }
  brass(mc, 'map'); brass(bc, 'bump'); brass(kc, 'mask');
  // wear on the brass: soft dark patina blotches (albedo only)
  for (let i = 0; i < 260; i++) { const a = r() * 6.28, d = r() * H, [X, Y] = P(d, a); mc.fillStyle = `rgba(60,48,26,${0.05 + r() * 0.08})`; mc.beginPath(); mc.arc(X, Y, (0.1 + r() * 0.3) * s, 0, Math.PI * 2); mc.fill(); }
  // cracks
  for (let i = 0; i < 80; i++) { let x = (r() * 2 - 1) * H, z = (r() * 2 - 1) * H, a = r() * 6.28; const d0 = Math.hypot(x, z); if (d0 > H || d0 < MOSAIC_R + 0.4) continue;
    mc.strokeStyle = 'rgba(60,54,44,0.5)'; mc.lineWidth = 1.6; bc.strokeStyle = 'rgba(40,40,40,0.7)'; bc.lineWidth = 1.6; mc.beginPath(); bc.beginPath(); mc.moveTo(C + x * s, C + z * s); bc.moveTo(C + x * s, C + z * s);
    for (let k = 0; k < 3 + r() * 5; k++) { a += (r() - 0.5) * 1.3; x += Math.cos(a) * 0.2; z += Math.sin(a) * 0.2; mc.lineTo(C + x * s, C + z * s); bc.lineTo(C + x * s, C + z * s); } mc.stroke(); bc.stroke(); }
  return { map, bump, mask };
}
const floorPaint = paintFloor(opts.floorRes || 2048, R + 0.2);
const floorMat = L('Floor', 0xffffff, { map: tex(floorPaint.map), bump: tex(floorPaint.bump, false), bumpScale: 1.2 });
const floor = add(new THREE.Mesh(new THREE.CircleGeometry(R + 0.2, 192).rotateX(-Math.PI / 2), floorMat)); floor.name = 'Floor'; floor.receiveShadow = true;
// mosaic grout glow: an additive layer in the preview; Unity reads the B channel of the floor mask instead
const glowMat = new THREE.MeshBasicMaterial({ map: (() => { const t = tex(floorPaint.mask, false); return t; })(), color: 0x000000, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false });
glowMat.onBeforeCompile = sh => { sh.fragmentShader = sh.fragmentShader.replace('#include <map_fragment>', '#include <map_fragment>\n diffuseColor = vec4(vec3(0.25, 0.95, 0.85) * texture2D(map, vMapUv).b * 0.55, 1.0);'); };
const glowLayer = add(new THREE.Mesh(new THREE.CircleGeometry(MOSAIC_R + 0.1, 96).rotateX(-Math.PI / 2), glowMat)); glowLayer.position.y = 0.01; glowLayer.userData.previewOnly = true;
glowMat.map.repeat.set((MOSAIC_R + 0.1) / (R + 0.2), (MOSAIC_R + 0.1) / (R + 0.2)); glowMat.map.offset.set(0.5 - glowMat.map.repeat.x / 2, 0.5 - glowMat.map.repeat.y / 2);
// flush brass gear ring around the medallion (turns slowly at runtime)
const gear = (() => { const sh = new THREE.Shape(), ro = MED_R + 0.2, ri = MED_R + 0.02, T = 36;
  for (let i = 0; i <= T * 4; i++) { const a = i / (T * 4) * Math.PI * 2, t = (i % 4 === 1 || i % 4 === 2) ? ro : ro - 0.1; i ? sh.lineTo(Math.cos(a) * t, Math.sin(a) * t) : sh.moveTo(Math.cos(a) * t, Math.sin(a) * t); }
  const hole = new THREE.Path(); hole.absarc(0, 0, ri, 0, Math.PI * 2, true); sh.holes.push(hole);
  const m = add(new THREE.Mesh(new THREE.ExtrudeGeometry(sh, { depth: 0.02, bevelEnabled: false, curveSegments: 72 }).rotateX(-Math.PI / 2), BRASS_DK)); m.name = 'MedallionGear'; m.receiveShadow = true; return m; })();

// ---- platform body: drum under the curb, ruined arcades around it, rock root into the abyss ----
function paintBricks(W, Hh) {
  const [c, g] = canvas2d(W, Hh), r = mulberry32(5), rowH = Hh / 6;
  g.fillStyle = '#4a4a42'; g.fillRect(0, 0, W, Hh);
  for (let y = 0; y < Hh; y += rowH) { let x = -r() * 60; while (x < W) { const w = 70 + r() * 90, l = 128 + r() * 34 - y / Hh * 40; g.fillStyle = `rgb(${l},${l - 3},${l - 14})`; g.fillRect(x + 3, y + 3, w - 6, rowH - 6); x += w; } }
  for (let i = 0; i < 160; i++) { const x = r() * W, len = 10 + r() * Hh * 0.7, grd = g.createLinearGradient(0, 0, 0, len); grd.addColorStop(0, 'rgba(66,100,48,0.85)'); grd.addColorStop(1, 'rgba(66,100,48,0)'); g.fillStyle = grd; g.fillRect(x, 0, 4 + r() * 16, len); }
  const t = tex(c); t.wrapS = THREE.RepeatWrapping; t.repeat.set(10, 1); return t;
}
const BRICK = L('Brick', 0xffffff, { map: paintBricks(2048, 512) });
const drum = add(new THREE.Mesh(new THREE.CylinderGeometry(R + 0.85, R + 0.7, 3.2, 128, 1, true), BRICK)); drum.position.y = -1.6; drum.name = 'Drum';
const rootPts = [[R + 0.75, -3.1], [R + 0.2, -4.2], [R - 1.8, -7.5], [11.5, -11], [9, -16], [6.5, -22], [4, -29], [2, -35], [0.5, -40], [0, -42]].map(([x, y]) => new THREE.Vector2(x, y));
add(new THREE.Mesh(rockify(new THREE.LatheGeometry(new THREE.SplineCurve(rootPts).getPoints(48), 128), 0.2, 0.2, -6), ROCK)).name = 'RockRoot';
// ruined arcade ring under the rim: straight segments with one round arch each, some collapsed; water pours through
const fallSpots = [];
{ const segs = 24, rad = R + 1.15;
  for (let i = 0; i < segs; i++) {
    const a = (i + 0.5) / segs * Math.PI * 2; if (nearExit(a, GAP + 0.08) || nearDiag(a, 0.14)) continue;
    if (rnd() < 0.15) continue;                                                      // collapsed bay
    const len = Math.PI * 2 * rad / segs + 0.05, h = 6.2, g = add(new THREE.Group()); g.position.set(Math.cos(a) * rad, -6.6, Math.sin(a) * rad); g.rotation.y = -a + Math.PI / 2;
    const wall = add(new THREE.Mesh(extrude(arcadeShape(len, h, 1, len * 0.58, 2.6), 0.55), STONE_DK), g); wall.position.x = -len / 2; wall.castShadow = wall.receiveShadow = true;
    if (rnd() < 0.85) fallSpots.push({ a, rad: rad + 0.35, w: 4 + rnd() * 3 });
    ivyCurtain(Math.cos(a) * (rad + 0.3), -0.3, Math.sin(a) * (rad + 0.3), 2 + rnd() * 4, Math.cos(a), Math.sin(a), 1.4, 0.18);
  }
}

// ---- curb: bevelled blocks with a brass band, open at the bridges; moss, ivy and white flowers spilling over ----
{ const n = 64, rad = R + 0.4;
  for (let i = 0; i < n; i++) {
    const a = (i + 0.5) / n * Math.PI * 2; if (nearExit(a, GAP)) continue;
    const sunk = rnd() < 0.12, h = sunk ? 0.32 : 0.5 + rnd() * 0.08, w = Math.PI * 2 * rad / n - 0.06;
    const b = block(rnd() < 0.6 ? STONE : STONE_DK, 0.8, h, w, Math.cos(a) * rad, h / 2 - 0.05, Math.sin(a) * rad, -a); b.rotation.z = (rnd() - 0.5) * 0.04;
    if (rnd() < 0.6) mossMound(Math.cos(a) * rad, h, Math.sin(a) * rad, 0.3 + rnd() * 0.25, rnd() < 0.75 ? 9 : 0); // lily clusters
    if (rnd() < 0.9) ivyCurtain(Math.cos(a) * (R + 0.85), h - 0.1, Math.sin(a) * (R + 0.85), 1 + rnd() * 5, Math.cos(a), Math.sin(a), 0.6, 0.15);
  }
  for (let i = 0; i < 40; i++) { const a = (i + rnd() * 0.6) / 40 * Math.PI * 2; if (nearExit(a, GAP + 0.04)) continue;
    const cx = Math.cos(a) * (R + 1.1), cz = Math.sin(a) * (R + 1.1);
    for (let k = 0; k < 4; k++) blob(0.35 + rnd() * 0.35, k % 2 ? IVY : MOSS_DK, cx + (rnd() - 0.5) * 1.2, 0.1 - rnd() * 0.8, cz + (rnd() - 0.5) * 1.2, 0.8);
    for (let k = 0; k < 4; k++) pushM(flowerM, cx + (rnd() - 0.5) * 1.2, 0.35, cz + (rnd() - 0.5) * 1.2, 1 + rnd() * 0.5);
    ivyCurtain(Math.cos(a) * (R + 1.0), 0.2, Math.sin(a) * (R + 1.0), 2 + rnd() * 4.5, Math.cos(a), Math.sin(a), 1.0, 0.18); }
  // brass band along the curb's top outer edge, one arc per quadrant between the bridge gaps
  for (const e of EXITS) { const t0 = e + GAP, len = Math.PI / 2 - GAP * 2, band = add(new THREE.Mesh(new THREE.CylinderGeometry(R + 0.82, R + 0.82, 0.09, 96, 1, true, Math.PI / 2 - t0 - len, len), BRASS)); band.position.y = 0.38; }
  for (let i = 0; i < 220; i++) { const a = rnd() * 6.28; if (nearExit(a, GAP)) continue; const d = rnd() < 0.5 ? PLAY_R + 0.45 + rnd() * 0.15 : R + 0.85 + rnd() * 0.3; E3.set(0, rnd() * 6, 0); Q.setFromEuler(E3); const sc = d < R ? 0.5 + rnd() * 0.3 : 0.7 + rnd() * 0.7; M4.compose(V.set(Math.cos(a) * d, d > R ? 0.45 : 0, Math.sin(a) * d), Q, S.set(sc, sc, sc)); fernM.push(M4.clone()); }
  // tufts of grass between the curb blocks, outside the playable radius
  for (let i = 0; i < 900; i++) { const a = rnd() * 6.28; if (nearExit(a, GAP)) continue; const d = PLAY_R + 0.12 + rnd() * 0.4; pushM(tuftM, Math.cos(a) * d, 0, Math.sin(a) * d, 0.6 + rnd() * 0.9, undefined, rnd() * 6, (rnd() - 0.5) * 0.6); }
}

// ---- lanterns: bronze cage, amber core; Unity adds a glow billboard at every node named "Lantern" ----
const LANTERN_METAL = L('LanternMetal', 0x2c2a22, { metal: 0.6 }), lanterns = [];
function lantern(parent, x, y, z) {
  const g = add(new THREE.Group(), parent); g.position.set(x, y, z); g.name = 'Lantern';
  for (let k = 0; k < 6; k++) { const a = k / 6 * Math.PI * 2; add(new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.016, 0.42, 6), LANTERN_METAL), g).position.set(Math.cos(a) * 0.15, 0, Math.sin(a) * 0.15); }
  for (const y2 of [-0.21, 0.21]) { const ring = add(new THREE.Mesh(new THREE.TorusGeometry(0.16, 0.022, 8, 24), LANTERN_METAL), g); ring.rotation.x = Math.PI / 2; ring.position.y = y2; }
  add(new THREE.Mesh(new THREE.ConeGeometry(0.2, 0.18, 12), BRASS_DK), g).position.y = 0.3;
  const core = add(new THREE.Mesh(new THREE.SphereGeometry(0.1, 12, 8), GLOW_AMBER), g); core.scale.y = 1.5; lanterns.push(core);
  return g;
}

// ---- crag detail: jutting ledges with moss, hanging roots and vines down the faces, optionally a ruined crown + tree ----
function detailCrag(parent, x, top, z, rad, seed, noCrown = false) {
  for (let k = 0; k < 2; k++) { const y = top - 3 - k * (3 + rnd() * 3), lg = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(rad * 1.2, rad * 0.85, 1.1, 18, 2), 0.35, 0.3, 0.2, seed + k), ROCK), parent); lg.position.set(x, y, z);
    for (let m = 0; m < 3; m++) { const la = rnd() * 6.28; blob(rad * 0.3, rnd() < 0.5 ? MOSS : MOSS_DK, x + Math.cos(la) * rad, y + 0.5, z + Math.sin(la) * rad, 0.45, parent); } }
  parent.updateMatrixWorld(true); const w = new THREE.Vector3(), w2 = new THREE.Vector3();
  for (let m = 0; m < 3; m++) { const la = rnd() * 6.28, L = 3 + rnd() * 5, pts = []; for (let q = 0; q <= 5; q++) { const tq = q / 5; pts.push(new THREE.Vector3(x + Math.cos(la) * (rad * 0.95 + tq * 0.5), top - 0.3 - tq * L, z + Math.sin(la) * (rad * 0.95 + tq * 0.5) + Math.sin(tq * 5 + m) * 0.15)); }
    add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 14, 0.1 + rnd() * 0.08, 6), BARK), parent);
    parent.localToWorld(w.set(x + Math.cos(la + 0.6) * rad, top - 0.2, z + Math.sin(la + 0.6) * rad)); parent.localToWorld(w2.set(x + Math.cos(la + 0.6) * (rad + 1), top - 0.2, z + Math.sin(la + 0.6) * (rad + 1))); w2.sub(w);
    ivyCurtain(w.x, w.y, w.z, 2 + rnd() * 4, w2.x, w2.z, 0.6, 0.1); }
  if (!noCrown) { for (let m = 0; m < 3; m++) { const fb = block(STONE_DK, 0.8 + rnd() * 0.5, 0.7 + rnd() * 0.9, 0.7, x + (rnd() - 0.5) * rad, top + 0.4, z + (rnd() - 0.5) * rad, rnd() * 3, parent); fb.rotation.z = (rnd() - 0.5) * 0.5; }
    tree(x + (rnd() - 0.5) * rad * 0.6, top + 0.2, z + (rnd() - 0.5) * rad * 0.6, 0.8 + rnd() * 0.3, parent); }
}

// ---- bridges: decks on arched piers, brass edge strips + centre line, parapets; the entry bridge (south) is long and formal ----
const DECK = L('Deck', 0xa9a395);
const fallsBridge = [];
function bridge(a, entry) {
  const g = add(new THREE.Group()); g.rotation.y = -a; g.name = entry ? 'EntryBridge' : 'Bridge';
  const x0 = R + 0.3, len = entry ? 21 : 11.5, x1 = x0 + len, hw = BRIDGE_W / 2;
  const deck = add(new THREE.Mesh(new THREE.BoxGeometry(len, 0.7, BRIDGE_W), DECK), g); deck.position.set(x0 + len / 2, -0.36, 0); deck.castShadow = deck.receiveShadow = true;
  for (let k = 0; k < len / 1.1; k++) { const sx = x0 + 0.55 + k * 1.1; add(new THREE.Mesh(new THREE.BoxGeometry(0.03, 0.012, BRIDGE_W - 0.4), STONE_DK), g).position.set(sx, 0.0, 0); } // slab joints
  for (const z of [-hw + 0.18, hw - 0.18]) add(new THREE.Mesh(new THREE.BoxGeometry(len, 0.03, 0.12), BRASS), g).position.set(x0 + len / 2, 0.005, z);
  add(new THREE.Mesh(new THREE.BoxGeometry(len, 0.025, 0.12), BRASS), g).position.set(x0 + len / 2, 0.005, 0);
  // arched supports below: an arcade wall under each deck edge, piers continuing down as rock
  const bays = Math.round(len / 5.2), wallH = 7;
  for (const z of [-hw + 0.3, hw - 0.3]) { const w = add(new THREE.Mesh(extrude(arcadeShape(len, wallH, bays, len / bays * 0.62, 3.2), 0.6), STONE_DK), g); w.position.set(x0, -0.7 - wallH, z); w.castShadow = w.receiveShadow = true; }
  for (let k = 0; k <= bays; k++) { const px = x0 + k * len / bays; block(STONE_DK, 1.7, 26, 1.7, px, -7.7 - 13, 0, 0, g); block(STONE, 2.1, 0.5, 2.1, px, -7.9, 0, 0, g); }
  // parapets (low, broken) or, on the entry bridge, brass-capped posts every 3 m with lanterns on alternate posts
  for (const z of [-hw + 0.17, hw - 0.17]) {
    if (!entry) { for (let x = x0 + 0.4; x < x1 - 0.5; x += 1.3) { if (rnd() < 0.18) continue; block(STONE, 1.2, 0.45 + rnd() * 0.08, 0.34, x + 0.6, 0.22, z, 0, g); if (rnd() < 0.45) mossMound(...new THREE.Vector3(x + 0.6, 0.45, z).applyAxisAngle(new THREE.Vector3(0, 1, 0), -a).toArray(), 0.3, 3); } }
    else { let k = 0; for (let x = x0 + 1.2; x < x1 - 0.5; x += 3, k++) {
      block(STONE, 0.42, 1.15, 0.42, x, 0.57, z, 0, g); const cap = add(new THREE.Mesh(new THREE.ConeGeometry(0.3, 0.32, 4), BRASS), g); cap.position.set(x, 1.32, z); cap.rotation.y = Math.PI / 4;
      if (x + 1.5 < x1) block(STONE_DK, 2.5, 0.42, 0.3, x + 1.5, 0.2, z, 0, g);
      if (k % 2 === 0) { const arm = add(new THREE.Mesh(new THREE.BoxGeometry(0.05, 0.05, 0.42), BRASS_DK), g); arm.position.set(x, 1.12, z - Math.sign(z) * 0.22); lantern(g, x, 0.85, z - Math.sign(z) * 0.42); }
    } }
  }
  // ivy hanging from the deck edges
  for (let x = x0 + 0.5; x < x1; x += 1.6) for (const z of [-hw, hw]) if (rnd() < 0.55) { const w = new THREE.Vector3(x, -0.2, z + Math.sign(z) * 0.05).applyAxisAngle(new THREE.Vector3(0, 1, 0), -a), nz = new THREE.Vector3(0, 0, Math.sign(z)).applyAxisAngle(new THREE.Vector3(0, 1, 0), -a); ivyCurtain(w.x, w.y, w.z, 1 + rnd() * 3.5, nz.x, nz.z, 0.7, 0.14); }
  // the far end: a ruined gothic gate arch (north/east/west) or the arrival threshold of the entry bridge
  if (!entry) {
    const gate = add(new THREE.Group(), g); gate.position.set(x1 - 0.4, 0, 0);
    const shape = arcadeShape(BRIDGE_W + 1.8, 6.2, 1, BRIDGE_W - 0.4, 3.4, true);
    const ga = add(new THREE.Mesh(extrude(shape, 0.9), STONE), gate); ga.rotation.y = Math.PI / 2; ga.position.set(0, 0, (BRIDGE_W + 1.8) / 2); ga.castShadow = true;
    const brk = add(new THREE.Mesh(new THREE.BoxGeometry(1.1, 1.6, 1.4), STONE_DK), gate); brk.position.set(0, 6.4, -1.5); brk.rotation.z = 0.25; // broken crown
    for (const z of [-BRIDGE_W / 2 - 0.5, BRIDGE_W / 2 + 0.5]) { ivyCurtain(...new THREE.Vector3(x1 - 0.4 - 0.5, 6.0, z).applyAxisAngle(new THREE.Vector3(0, 1, 0), -a).toArray(), 4.5, ...(() => { const v = new THREE.Vector3(-1, 0, 0).applyAxisAngle(new THREE.Vector3(0, 1, 0), -a); return [v.x, v.z]; })(), 0.9, 0.2); }
    lantern(gate, -0.2, 3.1, 0);
    // a tower stub behind the gate, rising from the abyss
    const t = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(2.6, 3.4, 34, 24, 34), 0.3, 0.2, 14, a), ROCK), g); t.position.set(x1 + 3.2, -14, 0);
    detailCrag(g, x1 + 3.2, 3.0, 0, 2.6, a * 7);
    // waterfalls off both sides of the bridge, from the arcade below the deck
    for (const z of [-hw - 0.4, hw + 0.4]) fallsBridge.push({ g, x: x0 + len * (0.35 + rnd() * 0.3), z, w: 2.6 + rnd() * 1.4 });
  } else {
    node('EntryPoint', 0, 0, R - 1.5);                                         // inner end of the entry bridge
    for (const z of [-hw - 0.2, hw + 0.2]) { block(STONE, 0.6, 1.4, 0.6, x0 + 0.3, 0.7, z, 0, g); add(new THREE.Mesh(new THREE.ConeGeometry(0.42, 0.4, 4), BRASS), g).position.set(x0 + 0.3, 1.6, z); }
    const far = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(4.5, 3, 30, 24, 20), 0.12, 0.25, 13.5, 4), ROCK), g); far.position.set(x1 + 3.5, -15.1, 0);
  }
  return g;
}
EXITS.forEach(a => bridge(a, a === ENTRY));

// ---- overgrown belfries on the diagonals: bastion, solid base, open bell chamber with pointed arches, brass bell ----
const bells = [];
const BELL_GEO = (() => { const pts = []; for (let i = 0; i <= 24; i++) { const t = i / 24, rr = 0.42 + 0.58 * Math.pow(t, 2.4) + (t > 0.88 ? 0.14 * (t - 0.88) / 0.12 : 0); pts.push(new THREE.Vector2(t === 0 ? 0.001 : rr * 0.5, 1 - t)); }
  pts.push(new THREE.Vector2(0.5, -0.02), new THREE.Vector2(0.42, 0.0)); pts.reverse(); // bottom-to-top so the faces point outward
  const g = new THREE.LatheGeometry(pts, 48); g.translate(0, -1, 0); return weldNormals(g); })();
function belfry(a, idx) {
  const isFront = front(a), rad = 17.2, baseY = isFront ? -1.2 : 0, chamberH = isFront ? 3.6 : 4.4, solidH = isFront ? 1.4 : 2.2;
  const g = add(new THREE.Group()); g.position.set(Math.cos(a) * rad, baseY, Math.sin(a) * rad); g.rotation.y = Math.atan2(-Math.cos(a), -Math.sin(a)); g.name = 'Belfry';
  // bastion: a rocky outcrop the tower stands on, bonded to the rim
  const bas = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(3.4, 1.6, 18, 28, 18), 0.3, 0.25, 7.6, idx * 5), ROCK), g); bas.position.y = -9.05;
  detailCrag(g, 0, -0.2, 0, 3.2, idx * 3, true);
  const W = 3.2, wallT = 0.42;
  block(STONE_DK, W, solidH, W, 0, solidH / 2, 0, 0, g);
  // bell chamber: four pointed-arch faces
  for (let f = 0; f < 4; f++) {
    const face = add(new THREE.Group(), g); face.rotation.y = f * Math.PI / 2;
    const m = add(new THREE.Mesh(extrude(arcadeShape(W, chamberH, 1, W * 0.56, chamberH * 0.45, true), wallT), STONE), face); m.position.set(-W / 2, solidH, W / 2 - wallT / 2); m.castShadow = m.receiveShadow = true;
  }
  const corn = block(STONE_DK, W + 0.5, 0.35, W + 0.5, 0, solidH + chamberH + 0.17, 0, 0, g);
  // open top (the roof fell in long ago): broken merlons on the cornice and corner piers of uneven height, so the high
  // camera looks straight down onto the bell, as in the top-down concept
  for (let k = 0; k < 12; k++) { if (rnd() < 0.4) continue; const f = Math.floor(k / 3), o = (k % 3 - 1) * 1.05, fa = f * Math.PI / 2, h = 0.3 + rnd() * 0.5;
    block(STONE, 0.6, h, 0.45, Math.sin(fa) * 1.65 + Math.cos(fa) * o, solidH + chamberH + 0.35 + h / 2, Math.cos(fa) * 1.65 - Math.sin(fa) * o, fa, g); }
  for (let k = 0; k < 4; k++) { const ca = k * Math.PI / 2 + Math.PI / 4, h = (isFront ? 0.4 : 0.9) + rnd() * (isFront ? 0.3 : 1.4); block(STONE_DK, 0.7, h, 0.7, Math.cos(ca) * 1.75, solidH + chamberH + 0.35 + h / 2, Math.sin(ca) * 1.75, 0, g); }
  // bell on a timber beam
  const beam = add(new THREE.Mesh(new THREE.BoxGeometry(W - 0.2, 0.22, 0.26), TIMBER), g); beam.position.y = solidH + chamberH - 0.35;
  const bellY = solidH + chamberH * 0.45 + (W * 0.56) * 0.62; // just under the arch crown
  for (const sx of [-1, 1]) { const br = add(new THREE.Mesh(new THREE.BoxGeometry(0.16, 1.3, 0.16), TIMBER), g); br.position.set(sx * (W / 2 - 0.65), solidH + chamberH - 0.8, 0); br.rotation.z = sx * 0.65; }
  for (let y = bellY + 0.05, k = 0; y < solidH + chamberH - 0.4; y += 0.11, k++) { const link = add(new THREE.Mesh(new THREE.TorusGeometry(0.05, 0.014, 5, 10), LANTERN_METAL), g); link.position.y = y; link.rotation.set(Math.PI / 2, k % 2 ? Math.PI / 2 : 0, 0); }
  const bell = add(new THREE.Group(), g); bell.position.y = bellY; bell.name = 'Bell';
  const bm = add(new THREE.Mesh(BELL_GEO, BELL), bell); const bs = isFront ? 1.45 : 1.7; bm.scale.set(bs, bs, bs); bm.castShadow = true;
  add(new THREE.Mesh(new THREE.SphereGeometry(0.1, 10, 8), BRASS_DK), bell).position.y = -bs * 0.95;
  bells.push({ o: bell, ph: rnd() * 6 });
  // ivy curtains and flowers: heavy on the faces, spilling from the cornice
  for (let f = 0; f < 4; f++) { const fa = f * Math.PI / 2, nx = Math.sin(fa), nz = Math.cos(fa);
    for (let k = 0; k < 3; k += 2) { const off = (k - 1) * 1.25, lx = nx * (W / 2 + 0.05) + nz * off, lz = nz * (W / 2 + 0.05) - nx * off;
      const wv = new THREE.Vector3(lx, solidH + chamberH + 0.2, lz).applyEuler(g.rotation).add(g.position), wn = new THREE.Vector3(nx, 0, nz).applyEuler(g.rotation);
      if (rnd() < 0.75) ivyCurtain(wv.x, wv.y, wv.z, 1.5 + rnd() * (chamberH + solidH + 3), wn.x, wn.z, 0.8, 0.22); } }
  for (let k = 0; k < 7; k++) { const ca = rnd() * 6.28; mossMound(...new THREE.Vector3(Math.cos(ca) * 1.9, solidH + chamberH + 0.4, Math.sin(ca) * 1.9).applyEuler(g.rotation).add(g.position).toArray(), 0.5 + rnd() * 0.3, 5); }
  // broken arch fragment linking the belfry to the rim
  // a broken arcade fragment flanking the tower along the rim
  const side = idx % 2 ? 1 : -1, fm = add(new THREE.Mesh(extrude(arcadeShape(2.6, isFront ? 2.4 : 3.6, 1, 1.5, isFront ? 0.9 : 1.6, true), 0.5), STONE), g);
  fm.position.set(side > 0 ? 1.6 : -4.2, 0, 0.9); fm.castShadow = true;
  return g;
}
DIAG.forEach(belfry);

// ---- waterfalls: scrolling sheets off the rim arcades and the bridges, foam lips (Unity spray) and pools below ----
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
  float y = (1.0 - vUv.y) * uRep; vec2 p = vec2(vUv.x * uW, y * 1.4 - uTime * 1.5);
  float s = n(p) * 0.6 + n(p * vec2(2.3, 1.9) + 7.0) * 0.4, streak = smoothstep(0.42, 0.85, s);
  float edge = smoothstep(0.0, 0.14, vUv.x) * smoothstep(1.0, 0.86, vUv.x), foam = exp(-y * 5.0) * (0.6 + 0.4 * n(vec2(vUv.x * uW * 2.0, uTime * 2.0)));
  vec3 col = mix(vec3(0.32, 0.72, 0.72), vec3(0.9, 1.0, 0.98), clamp(streak * 0.85 + foam, 0.0, 1.0));
  gl_FragColor = vec4(col, (0.42 + streak * 0.42 + foam * 0.6) * edge * smoothstep(uRep, uRep * 0.55, y));
#include <fog_fragment>
}`;
const fallMat = (rep, w) => { const m = new THREE.ShaderMaterial({ name: 'Waterfall', uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uTime: { value: 0 }, uRep: { value: rep }, uW: { value: w } }]), vertexShader: FALL_VS, fragmentShader: FALL_FS, transparent: true, depthWrite: false, side: THREE.DoubleSide, fog: true }); falls.push(m); return m; };
function sheet(x, yTop, z, w, h, faceX, faceZ, curve = 0) {
  const geo = new THREE.PlaneGeometry(w, h, 6, 12), p = geo.attributes.position;
  for (let i = 0; i < p.count; i++) { const t = (h / 2 - p.getY(i)) / h; p.setZ(i, p.getZ(i) + Math.pow(t, 0.6) * curve + Math.sin(p.getX(i) * 1.3) * 0.15); } // arc out as it falls
  geo.computeVertexNormals();
  const m = add(new THREE.Mesh(geo, fallMat(h / 9, w * 2.2))); m.position.set(x, yTop - h / 2, z); m.lookAt(x + faceX, yTop - h / 2, z + faceZ); m.name = 'Waterfall'; m.renderOrder = 2;
  node('FallLip', x, yTop, z).userData = { w };
  return m;
}
for (const f of fallSpots) sheet(Math.cos(f.a) * (R + 1.5), -0.15, Math.sin(f.a) * (R + 1.5), f.w + 1.5, 36, Math.cos(f.a), Math.sin(f.a), 5);
// broad falls pouring off each belfry bastion
for (const a of DIAG) { const o = a + (rnd() < 0.5 ? 0.2 : -0.2); sheet(Math.cos(o) * (R + 2.6), front(a) ? -2.6 : -0.4, Math.sin(o) * (R + 2.6), 6 + rnd() * 3, 36, Math.cos(o), Math.sin(o), 3.5); }
for (const f of fallsBridge) { const w = new THREE.Vector3(f.x, -0.9, f.z).applyEuler(f.g.rotation), nz = new THREE.Vector3(0, 0, Math.sign(f.z)).applyEuler(f.g.rotation); sheet(w.x, -0.9, w.z, f.w, 33, nz.x, nz.z, 1.6); }

// ---- surroundings: aqueduct arcades on rock pillars, some pouring water; mossy spires with dark tree canopies ----
function tree(x, y, z, s, parent = root) {
  const g = add(new THREE.Group(), parent); g.position.set(x, y, z); g.scale.setScalar(s);
  const curve = new THREE.CatmullRomCurve3([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0.3, 1.4, 0.1), new THREE.Vector3(-0.2, 2.6, 0.3), new THREE.Vector3(0.1, 3.4, 0)]);
  add(new THREE.Mesh(new THREE.TubeGeometry(curve, 24, 0.36, 10), BARK), g).castShadow = true;
  for (let k = 0; k < 20; k++) { const a = rnd() * 6.28, r = Math.sqrt(rnd()) * 2.3, layer = k / 20, y = 2.6 + layer * 1.9 - r * 0.3;
    blob(0.75 + rnd() * 0.75 - layer * 0.25, layer < 0.4 ? CANOPY_DK : layer < 0.85 ? CANOPY : MOSS, Math.cos(a) * r * (1.1 - layer * 0.4), y, Math.sin(a) * r * (1.1 - layer * 0.4), 0.7, g); }
  // leaf fringe and flowers in world space: trees can sit inside rotated, offset groups (aqueducts, islands)
  g.updateMatrixWorld(true); const wv = new THREE.Vector3(), wq = new THREE.Vector3();
  for (let k = 0; k < 36; k++) { const a = rnd() * 6.28, rr = 1.6 + rnd() * 1.2; g.localToWorld(wv.set(Math.cos(a) * rr, 2.6 + rnd() * 2, Math.sin(a) * rr)); g.localToWorld(wq.set(Math.cos(a) * (rr + 1), 2.6, Math.sin(a) * (rr + 1))); wq.sub(wv); pushLeaf(wv.x, wv.y, wv.z, (0.3 + rnd() * 0.25) * s, wq.x, wq.z); }
  if (rnd() < 0.5) for (let k = 0; k < 5; k++) { g.localToWorld(wv.set((rnd() - 0.5) * 3, 3.6 + rnd(), (rnd() - 0.5) * 3)); pushM(flowerM, wv.x, wv.y, wv.z, 1.2 + rnd()); }
  return g;
}
function aqueduct(cx, cz, rotY, len, topY, bays, pour) {
  const g = add(new THREE.Group()); g.position.set(cx, 0, cz); g.rotation.y = rotY; g.name = 'Aqueduct';
  const h = 11, bay = len / bays, axis = new THREE.Vector3(0, 1, 0), nrm = new THREE.Vector3(0, 0, 1).applyAxisAngle(axis, rotY);
  const fallen = new Set(); for (let k = 1; k < bays - 1; k++) if (rnd() < 0.28) fallen.add(k);   // collapsed spans (never the ends)
  for (let k = 0; k < bays; k++) {
    const px = -len / 2 + k * bay, top = topY - (rnd() < 0.3 ? 1 + rnd() * 2.5 : 0);
    if (!fallen.has(k)) { const w = add(new THREE.Mesh(extrude(arcadeShape(bay + 0.02, h, 1, bay * 0.64, 6.2), 2.4), STONE_DK), g); w.position.set(px, top - h, 0); w.castShadow = w.receiveShadow = true;
      const cap = add(new THREE.Mesh(new THREE.BoxGeometry(bay + 0.3, 0.5, 2.9), STONE), g); cap.position.set(px + bay / 2, top + 0.25, 0);
      if (rnd() < 0.6) blob(1.1 + rnd() * 0.6, MOSS, px + bay / 2, top + 0.6, 0, 0.35, g);
      const wv = new THREE.Vector3(px + bay / 2, top, 1.3).applyAxisAngle(axis, rotY).add(g.position);
      if (rnd() < 0.7) ivyCurtain(wv.x, wv.y, wv.z, 3 + rnd() * 6, nrm.x, nrm.z, 1.4, 0.1);
      // keystones on both faces, a cornice under the cap, and a strip of water in the channel on top
      const crown = top - h + 6.2 + bay * 0.32;
      for (const fz of [-1.25, 1.25]) block(STONE, 0.55, 0.9, 0.3, px + bay / 2, crown, fz, 0, g);
      block(STONE, bay + 0.25, 0.32, 3.1, px + bay / 2, top - 0.2, 0, 0, g);
      const ch = add(new THREE.Mesh(new THREE.BoxGeometry(bay, 0.06, 1.1), POOL_CHANNEL), g); ch.position.set(px + bay / 2, top + 0.52, 0); ch.name = 'Channel';
      if (pour && rnd() < 0.55) sheet(wv.x + nrm.x * 0.3, top + 0.3, wv.z + nrm.z * 0.3, 4 + rnd() * 3, top + 40, nrm.x, nrm.z, 3); }
    else { const stub = add(new THREE.Mesh(extrude(arcadeShape(bay * 0.35, h * 0.5, 1, bay * 0.2, 2.5), 2.4), STONE_DK), g); stub.position.set(px, topY - h, 0);
      for (let m = 0; m < 3; m++) { const fb = block(STONE_DK, 0.8 + rnd() * 0.6, 0.6 + rnd() * 0.4, 1.2 + rnd(), px + bay * (0.3 + rnd() * 0.5), topY - h + h * 0.5 - rnd() * 6, (rnd() - 0.5) * 1.5, rnd() * 3, g); fb.rotation.z = (rnd() - 0.5) * 1.2; fb.rotation.x = (rnd() - 0.5) * 0.8; } } // fallen blocks
  }
  for (let k = 0; k <= bays; k++) { const px = -len / 2 + k * bay; block(STONE_DK, 2.6, 50, 2.6, px, topY - h - 25, 0, 0, g); block(STONE, 3.2, 0.7, 3.2, px, topY - h + 0.15, 0, 0, g); block(STONE, 3.0, 0.5, 3.0, px, topY - h - 4, 0, 0, g); }
  if (rnd() < 0.6) tree(-len / 2 + rnd() * len * 0.8, topY + 0.5, 0, 0.9 + rnd() * 0.4, g);
  return g;
}
// placed around the arena facing it; the camera side (south) stays low so nothing rises into the play view
for (const [deg, r, len, top, bays] of [[-90, 60, 36, -9, 6], [-50, 62, 30, -12, 5], [-130, 62, 30, -12, 5], [-12, 64, 30, -9, 5], [-168, 64, 30, -9, 5], [30, 66, 26, -14, 4], [150, 66, 26, -14, 4], [-90, 88, 54, 2, 8], [-40, 88, 44, 5, 6], [-140, 86, 46, 6, 6]]) {
  const a = deg * Math.PI / 180, cx = Math.cos(a) * r, cz = Math.sin(a) * r; aqueduct(cx, cz, -a + Math.PI / 2, len, top, bays, r < 70);
}
for (let i = 0; i < 26; i++) {
  const a = rnd() * Math.PI * 2, r = 36 + rnd() * 70; if (nearExit(a, 0.2) && r < 48) continue;
  const top = front(a) ? -16 + rnd() * 8 : -9 + rnd() * 10, rad = 2.5 + rnd() * 3, h = top + 70;
  const p = { position: new THREE.Vector3(Math.cos(a) * r, 0, Math.sin(a) * r) }; let yT = top, rr = rad * 0.85;
  for (let k = 0; k < 3; k++) { const sh = k === 2 ? 60 : 4 + rnd() * 7, rb = k === 0 ? rr * (0.8 + rnd() * 0.1) : rr * (1.15 + rnd() * 0.2), /* the crown overhangs the drum below */ ox = k ? (rnd() - 0.5) * rr * 0.4 : 0, oz = k ? (rnd() - 0.5) * rr * 0.4 : 0;
    const seg = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(rr, rb, sh, 24, Math.max(6, Math.round(sh))), 0.46, 0.14, sh / 2 - 1.5, i * 3.3 + k), ROCK)); seg.position.set(p.position.x + ox, yT - sh / 2, p.position.z + oz);
    if (k > 0) { for (let m = 0; m < 3; m++) { const la = rnd() * 6.28; blob(rr * 0.35, rnd() < 0.5 ? MOSS : MOSS_DK, seg.position.x + Math.cos(la) * rr * 0.9, yT, seg.position.z + Math.sin(la) * rr * 0.9, 0.45); }
      const lg = add(new THREE.Mesh(rockify(new THREE.CylinderGeometry(rr * 1.25, rr * 0.9, 1.2, 18, 2), 0.35, 0.3, 0.2, i + k * 7), ROCK)); lg.position.set(seg.position.x, yT - 0.4, seg.position.z); } // jutting ledge
    yT -= sh; rr = rb * 1.05; }
  for (let m = 0; m < 4; m++) { const la = rnd() * 6.28, d2 = rnd() * rad * 0.4; blob(rad * (0.35 + rnd() * 0.2), MOSS, p.position.x + Math.cos(la) * d2, top + 0.1, p.position.z + Math.sin(la) * d2, 0.45); }
  for (let m = 0; m < 2 + Math.floor(rnd() * 2); m++) { const la = rnd() * 6.28, rr0 = rad * 0.72; ivyCurtain(p.position.x + Math.cos(la) * rr0, top - 0.2, p.position.z + Math.sin(la) * rr0, 4 + rnd() * 8, Math.cos(la), Math.sin(la), 0.55, 0.05); }
  for (let m = 0; m < 3; m++) { const la = rnd() * 6.28, r0 = rad * 0.75, L = 3 + rnd() * 6, pts = []; for (let q = 0; q <= 5; q++) { const tq = q / 5; pts.push(new THREE.Vector3(p.position.x + Math.cos(la) * (r0 + tq * 0.6 + Math.sin(tq * 6 + m) * 0.2), top - tq * L, p.position.z + Math.sin(la) * (r0 + tq * 0.6))); }
    add(new THREE.Mesh(new THREE.TubeGeometry(new THREE.CatmullRomCurve3(pts), 16, 0.12 + rnd() * 0.1, 6), BARK)); } // hanging roots
  const crownRoll = rnd();
  if (crownRoll < 0.45) { const rg = add(new THREE.Group()); rg.position.set(p.position.x, top + 0.2, p.position.z); rg.rotation.y = rnd() * 6;   // a small ruin on top
    block(STONE, 2.2, 1.6 + rnd() * 1.4, 0.45, 0, 1, -0.6, 0, rg); const ar = add(new THREE.Mesh(extrude(arcadeShape(2.2, 2.6, 1, 1.2, 1.3, true), 0.4), STONE), rg); ar.position.set(-1.1, 0, 0.6);
    if (rnd() < 0.4) lantern(rg, 0, 2.2, 0.6); }
  else if (crownRoll < 0.7) for (let m = 0; m < 4; m++) { const fb = block(STONE_DK, 0.7 + rnd() * 0.6, 0.6 + rnd() * 0.8, 0.7 + rnd() * 0.5, p.position.x + (rnd() - 0.5) * rad, top + 0.4, p.position.z + (rnd() - 0.5) * rad, rnd() * 3); fb.rotation.z = (rnd() - 0.5) * 0.6; } // broken crown
  if (rnd() < 0.7) tree(p.position.x, top + 0.3, p.position.z, 0.9 + rnd() * 0.7);
}

// ---- water far below, sea of mist (Windblown §6) ----
const POOL = new THREE.MeshBasicMaterial({ name: 'Pool', color: 0x1d6f70 });
const pool = add(new THREE.Mesh(new THREE.CircleGeometry(900, 64).rotateX(-Math.PI / 2), POOL)); pool.position.y = -36; pool.name = 'Pool';
const mistMat = new THREE.ShaderMaterial({ name: 'Mist', transparent: true, depthWrite: false, fog: false, uniforms: { uTime: { value: 0 } },
  vertexShader: 'varying vec3 vW; void main(){ vec4 w = modelMatrix * vec4(position, 1.); vW = w.xyz; gl_Position = projectionMatrix * viewMatrix * w; }',
  fragmentShader: `uniform float uTime; varying vec3 vW;
    float h(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
    float n(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3. - 2. * f); return mix(mix(h(i), h(i + vec2(1, 0)), f.x), mix(h(i + vec2(0, 1)), h(i + vec2(1, 1)), f.x), f.y); }
    float fbm(vec2 p){ float a = 0.5, s = 0.; for (int i = 0; i < 5; i++) { s += n(p) * a; p *= 2.03; a *= 0.5; } return s; }
    void main(){ vec2 p = vW.xz * 0.03; float d = length(vW.xz);
      float c = fbm(p + vec2(uTime * 0.012, uTime * 0.006)) * 0.6 + fbm(p * 2.1 - vec2(uTime * 0.02, 0.)) * 0.4, dens = smoothstep(0.38, 0.78, c);
      vec3 col = mix(vec3(0.10, 0.32, 0.33), vec3(0.62, 0.88, 0.85), dens);
      gl_FragColor = vec4(col, dens * 0.75 * smoothstep(260., 120., d)); }`,
});
for (const y of [-33, -27]) { const sea = add(new THREE.Mesh(new THREE.PlaneGeometry(520, 520).rotateX(-Math.PI / 2), mistMat)); sea.position.y = y; sea.renderOrder = -1; sea.name = 'Mist'; }

// ---- build the instanced foliage ----
const IVY_GEO = (() => { const g = new THREE.IcosahedronGeometry(1, 0), q = g.attributes.position; for (let i = 0; i < q.count; i++) { const x = q.getX(i), y = q.getY(i), z = q.getZ(i), k = 1 + (vnoise3(x * 2.5, y * 2.5, z * 2.5) - 0.5) * 0.5; q.setXYZ(i, x * k, y * k * 0.7, z * k); } return weldNormals(g); })(), TUFT_GEO = (() => { const g = new THREE.ConeGeometry(0.06, 0.4, 5); g.translate(0, 0.2, 0); return g; })();
const FLOWER_GEO = (() => { const parts = []; for (let k = 0; k < 5; k++) { const a = k / 5 * Math.PI * 2, p = new THREE.CircleGeometry(0.05, 5); p.rotateX(-Math.PI / 2 + 0.35); p.scale(1, 1, 0.55); p.translate(0.055, 0.005, 0); p.rotateY(a); parts.push(tint(p, 0xf4f1e6)); }
  const c = new THREE.CircleGeometry(0.024, 6); c.rotateX(-Math.PI / 2); c.translate(0, 0.015, 0); parts.push(tint(c, 0xe8c54a)); return merge(parts); })();
const TUFT = L('Grass', 0x5d8a3c), FERN = L('Fern', 0x4f7f3a);
const FERN_GEO = (() => { const parts = []; for (let k = 0; k < 7; k++) { const f = new THREE.CircleGeometry(0.5, 6); f.scale(0.13, 1, 1); f.translate(0, 0.5, 0); f.rotateX(-0.9 - (k % 2) * 0.25); f.rotateY(k / 7 * Math.PI * 2); parts.push(f); } return merge(parts); })();
const instanced = [];
for (const [geo, mat, list, name] of [[IVY_GEO, IVY, ivyM, 'IvyLeaves'], [FLOWER_GEO, FLOWER, flowerM, 'Flowers'], [TUFT_GEO, TUFT, tuftM, 'GrassTufts'], [FERN_GEO, FERN, fernM, 'Ferns']]) {
  const im = add(new THREE.InstancedMesh(geo, mat, list.length)); im.name = name; list.forEach((m, i) => im.setMatrixAt(i, m)); im.castShadow = name !== 'GrassTufts'; im.receiveShadow = true; instanced.push(im);
}

// ---- markers for gameplay (empty transforms; boss designers use them later) ----
node('BossAnchor', 0, 0, 0);
const spawns = node('PlayerSpawns', 0, 0, 0);
for (let i = 0; i < 8; i++) { const a = Math.PI * 0.18 + i / 7 * Math.PI * 0.64; node('Spawn' + (i + 1), Math.cos(a) * 10, 0, Math.sin(a) * 10, spawns); }

// ---- preview-only life: motes and petals (Unity uses particle systems instead) ----
const motes = (() => {
  const N = 160, pos = new Float32Array(N * 3), seed = [];
  for (let i = 0; i < N; i++) { const a = rnd() * 6.28, r = 11 + rnd() * 22; pos.set([Math.cos(a) * r, 0.5 + rnd() * 6, Math.sin(a) * r], i * 3); seed.push(rnd() * 100); }
  const [c, g] = canvas2d(64), grd = g.createRadialGradient(32, 32, 0, 32, 32, 32); grd.addColorStop(0, 'rgba(200,255,240,1)'); grd.addColorStop(0.3, 'rgba(110,255,220,0.6)'); grd.addColorStop(1, 'rgba(110,255,220,0)'); g.fillStyle = grd; g.fillRect(0, 0, 64, 64);
  const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const pts = add(new THREE.Points(geo, new THREE.PointsMaterial({ map: tex(c), size: 0.32, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false, opacity: 0.8 }))); pts.userData.previewOnly = true;
  return { pts, base: pos.slice(), seed };
})();
const halos = [];
{ const [c, g] = canvas2d(128), grd = g.createRadialGradient(64, 64, 0, 64, 64, 64); grd.addColorStop(0, 'rgba(255,214,150,0.9)'); grd.addColorStop(0.25, 'rgba(255,180,100,0.35)'); grd.addColorStop(1, 'rgba(255,180,100,0)'); g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
  const sm = new THREE.SpriteMaterial({ map: tex(c), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false });
  for (const l of lanterns) { const sp = add(new THREE.Sprite(sm), l); sp.scale.setScalar(1.8); sp.userData.previewOnly = true; halos.push(sp); } }

// ---- lights and atmosphere (Unity's builder sets the same values) ----
let sun = null;
if (opts.lights !== false) {
  scene.background = new THREE.Color(0x0e3338);
  scene.fog = new THREE.Fog(0x123c40, 45, 170);
  add(new THREE.HemisphereLight(0x9cc8c4, 0x1b2624, 1.0)).userData.previewOnly = true;
  sun = add(new THREE.DirectionalLight(0xffe0b0, 2.3)); sun.position.set(-20, 34, -16); sun.castShadow = true; sun.userData.previewOnly = true;
  Object.assign(sun.shadow.camera, { left: -26, right: 26, top: 26, bottom: -26, near: 1, far: 100 }); sun.shadow.mapSize.set(4096, 4096); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03;
  const fill = add(new THREE.DirectionalLight(0x5fd8d0, 0.6)); fill.position.set(14, 12, 24); fill.userData.previewOnly = true;
}

let glow = 1;
function update(t, dt, quiet) {
  glow += ((quiet ? 0.35 : 1) - glow) * Math.min(1, dt * 4);
  glowMat.opacity = glow; glowLayer.visible = glow > 0.02;
  for (const m of falls) m.uniforms.uTime.value = t;
  mistMat.uniforms.uTime.value = t;
  gear.rotation.y = -t * 0.01;
  bells.forEach(b => { b.o.rotation.z = Math.sin(t * 0.9 + b.ph) * 0.05; b.o.rotation.x = Math.sin(t * 0.6 + b.ph * 2) * 0.025; });
  halos.forEach((h, i) => h.scale.setScalar((1.7 + Math.sin(t * 2.1 + i * 1.7) * 0.12) * (0.6 + 0.4 * glow)));
  const p = motes.pts.geometry.attributes.position;
  for (let i = 0; i < p.count; i++) { const s = motes.seed[i]; p.setXYZ(i, motes.base[i * 3] + Math.sin(t * 0.4 + s) * 0.8, motes.base[i * 3 + 1] + Math.sin(t * 0.7 + s * 2) * 0.5, motes.base[i * 3 + 2] + Math.cos(t * 0.35 + s) * 0.8); }
  p.needsUpdate = true; motes.pts.material.opacity = 0.8 * glow;
}
return { R, PLAY_R, root, sun, update, floor: floorPaint, instanced };
};
