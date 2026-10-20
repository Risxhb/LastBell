// <arena-env> The Bellkeeper's Dais and the sky city around it, rebuilt from Reference/LAST-BELL-KEY-ART-ARENA.png.
// Shared by index.html (the game) and arena-look.html (the look study). Classic script: call
//   const env = window.buildArenaEnv(THREE, scene, { lights: true, noClouds: false });
// then env.update(timeSeconds, dt, quiet) every frame (quiet = hazards are up: floor decoration dims, arena §8.1).
// Returns { E, sun, update }. Units are metres; the platform floor spans [-E, E] on x and z, the boss stands at 0,0.
window.buildArenaEnv = function buildArenaEnv(THREE, scene, opts = {}) {
const E = 13;           // half-size of the platform floor
const DIAL_R = 7.2;     // main clock dial under the boss; its rim sits just outside the Hollow Toll's 6.8 m edge

// ---- helpers ----
function mulberry32(a) { return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(7);
const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
const fbm = (x, y) => vnoise(x, y) * 0.55 + vnoise(x * 2.1, y * 2.1) * 0.3 + vnoise(x * 4.3, y * 4.3) * 0.15;
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const tex = (c, srgb = true) => { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t; };
const canvas2d = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };

// ---- flagstone painter: running-bond rows of worn lavender-grey slabs, colour + height in one pass ----
function paintPaving(N, H, rowH, seed) {
  const r = mulberry32(seed), rows = [];
  for (let z = -H; z < H + rowH; z += rowH) { const b = []; let x = -H - r() * 1.4; while (x < H + 2) { b.push(x); x += 0.75 + r() * 1.1; } rows.push(b); }
  const [map, mc] = canvas2d(N), [bump, bc] = canvas2d(N), mi = mc.createImageData(N, N), bi = bc.createImageData(N, N), md = mi.data, bd = bi.data;
  const LX = 0.55, LZ = -0.83, grout = 0.022, bev = 0.09;
  for (let py = 0; py < N; py++) {
    const z = -H + (py + 0.5) / N * 2 * H, row = Math.floor((z + H) / rowH), fz = (z + H) - row * rowH, b = rows[Math.min(row, rows.length - 1)];
    let k = 0;
    for (let px = 0; px < N; px++) {
      const x = -H + (px + 0.5) / N * 2 * H, o = (py * N + px) * 4;
      while (k < b.length - 2 && x > b[k + 1]) k++;
      const dl = x - b[k], dr = b[k + 1] - x, dt = fz, db = rowH - fz;
      const edge = Math.min(dl, dr, dt, db), sid = hash(row * 31 + k, seed);
      let nx = 0, nz = 0; if (edge === dl) nx = -1; else if (edge === dr) nx = 1; else if (edge === dt) nz = -1; else nz = 1;
      const n = fbm(x * 0.4, z * 0.4), fine = vnoise(x * 7, z * 7), chip = vnoise(x * 2.3 + sid * 9, z * 2.3);
      const v = (sid - 0.5) * 0.26 + (n - 0.5) * 0.3 + (fine - 0.5) * 0.08, tint = hash(k, row * 7 + seed);
      let cr = (124 + tint * 22) * (1 + v), cg = (114 + tint * 6) * (1 + v), cb = (150 - tint * 12) * (1 + v);
      let h = 0.6 + 0.4 * smooth(grout * 0.5, bev * 1.5, edge);
      if (edge < bev) { const t = 1 - smooth(grout, bev, edge), l = nx * LX + nz * LZ, f = 1 + l * 0.34 * t; cr *= f; cg *= f; cb *= f; }
      if (chip < 0.16) { cr *= 0.82; cg *= 0.8; cb *= 0.84; h -= 0.08; }               // shallow worn pits
      if (fine > 0.86) { cr *= 1.08; cg *= 1.07; cb *= 1.05; }                            // pale speckle
      if (edge < grout) { cr = 52; cg = 44; cb = 70; h = 0.2; }
      md[o] = Math.min(255, cr); md[o + 1] = Math.min(255, cg); md[o + 2] = Math.min(255, cb); md[o + 3] = 255;
      const hb = Math.max(0, Math.min(255, h * 255)); bd[o] = bd[o + 1] = bd[o + 2] = hb; bd[o + 3] = 255;
    }
  }
  mc.putImageData(mi, 0, 0); bc.putImageData(bi, 0, 0);
  const toPx = v => (v + H) / (2 * H) * N;
  for (let i = 0; i < 60; i++) { // hairline cracks
    let x = (r() * 2 - 1) * H, z = (r() * 2 - 1) * H, a = r() * 6.28;
    mc.strokeStyle = 'rgba(40,32,58,0.55)'; mc.lineWidth = 1.6; bc.strokeStyle = 'rgba(30,30,30,0.7)'; bc.lineWidth = 1.6;
    mc.beginPath(); bc.beginPath(); mc.moveTo(toPx(x), toPx(z)); bc.moveTo(toPx(x), toPx(z));
    for (let s = 0; s < 3 + r() * 5; s++) { a += (r() - 0.5) * 1.3; x += Math.cos(a) * 0.2; z += Math.sin(a) * 0.2; mc.lineTo(toPx(x), toPx(z)); bc.lineTo(toPx(x), toPx(z)); }
    mc.stroke(); bc.stroke();
  }
  return { map, bump };
}

// ---- clock dial: indigo disc, gold rings, roman numerals, sub-dials, sunburst; plus a gold-only glow mask ----
function paintDial(N, style = 'full') {
  const [c, g] = canvas2d(N), [e, ge] = canvas2d(N), R = N / 2 * 0.985;
  ge.fillStyle = '#000'; ge.fillRect(0, 0, N, N);
  for (const ctx of [g, ge]) ctx.translate(N / 2, N / 2);
  if (style === 'full') {
    const grd = g.createRadialGradient(0, 0, R * 0.1, 0, 0, R); grd.addColorStop(0, '#3a3d8a'); grd.addColorStop(0.7, '#2b2e70'); grd.addColorStop(1, '#22255c');
    g.fillStyle = grd; g.beginPath(); g.arc(0, 0, R, 0, 7); g.fill();
    for (let i = 0; i < 2600; i++) { const a = Math.random() * 6.28, rr = Math.sqrt(Math.random()) * R; g.fillStyle = `rgba(${Math.random() < 0.5 ? '150,150,220' : '10,10,40'},${0.05 + Math.random() * 0.08})`; g.fillRect(Math.cos(a) * rr, Math.sin(a) * rr, 3 + Math.random() * 8, 2 + Math.random() * 5); }
  }
  const both = f => { f(g, '#e4b25c'); f(ge, '#ffffff'); };
  const ring = (rr, w) => both((ctx, col) => { ctx.strokeStyle = col; ctx.lineWidth = w * N / 2048; ctx.beginPath(); ctx.arc(0, 0, R * rr, 0, 7); ctx.stroke(); });
  for (const [rr, w] of [[0.985, 22], [0.95, 5], [0.81, 12], [0.785, 4], [0.56, 9], [0.535, 3], [0.2, 6]]) ring(rr, w);
  both((ctx, col) => { // minute ticks
    ctx.strokeStyle = col;
    for (let i = 0; i < 120; i++) { const a = i / 120 * Math.PI * 2, big = i % 10 === 0; ctx.lineWidth = (big ? 10 : 3) * N / 2048; ctx.beginPath(); ctx.moveTo(Math.cos(a) * R * 0.95, Math.sin(a) * R * 0.95); ctx.lineTo(Math.cos(a) * R * (big ? 0.89 : 0.925), Math.sin(a) * R * (big ? 0.89 : 0.925)); ctx.stroke(); }
  });
  if (style === 'full') both((ctx, col) => { // numerals between the 0.81 and 0.89 rings
    const num = ['XII', 'I', 'II', 'III', 'IIII', 'V', 'VI', 'VII', 'VIII', 'IX', 'X', 'XI'];
    ctx.fillStyle = col; ctx.font = `700 ${Math.round(N * 0.052)}px Georgia, 'Times New Roman', serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    for (let i = 0; i < 12; i++) { const a = i / 12 * Math.PI * 2 - Math.PI / 2; ctx.save(); ctx.translate(Math.cos(a) * R * 0.85, Math.sin(a) * R * 0.85); ctx.rotate(a + Math.PI / 2); ctx.fillText(num[i], 0, 0); ctx.restore(); }
  });
  both((ctx, col) => { // filigree circles and studs, three sub-dials, a sunburst at the centre
    ctx.strokeStyle = col; ctx.fillStyle = col; ctx.lineWidth = 4 * N / 2048;
    for (let i = 0; i < 12; i++) { const a = i / 12 * Math.PI * 2;
      ctx.beginPath(); ctx.arc(Math.cos(a) * R * 0.67, Math.sin(a) * R * 0.67, R * 0.075, 0, 7); ctx.stroke();
      ctx.beginPath(); ctx.arc(Math.cos(a) * R * 0.67, Math.sin(a) * R * 0.67, R * 0.018, 0, 7); ctx.fill();
      const b = a + Math.PI / 12; ctx.beginPath(); ctx.arc(Math.cos(b) * R * 0.67, Math.sin(b) * R * 0.67, R * 0.012, 0, 7); ctx.fill(); }
    for (let i = 0; i < 3; i++) { const a = i / 3 * Math.PI * 2 + Math.PI / 6, cx = Math.cos(a) * R * 0.37, cy = Math.sin(a) * R * 0.37;
      for (const [rr, w] of [[0.12, 6], [0.1, 2]]) { ctx.lineWidth = w * N / 2048; ctx.beginPath(); ctx.arc(cx, cy, R * rr, 0, 7); ctx.stroke(); }
      ctx.lineWidth = 3 * N / 2048; for (let j = 0; j < 12; j++) { const t = j / 12 * Math.PI * 2; ctx.beginPath(); ctx.moveTo(cx + Math.cos(t) * R * 0.1, cy + Math.sin(t) * R * 0.1); ctx.lineTo(cx + Math.cos(t) * R * 0.08, cy + Math.sin(t) * R * 0.08); ctx.stroke(); }
      ctx.lineWidth = 5 * N / 2048; ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(cx + Math.cos(a * 3) * R * 0.08, cy + Math.sin(a * 3) * R * 0.08); ctx.stroke(); }
    ctx.beginPath(); for (let i = 0; i < 16; i++) { const a = i / 16 * Math.PI * 2, rr = i % 2 ? R * 0.07 : R * (i % 4 ? 0.13 : 0.18); ctx.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); } ctx.closePath(); ctx.fill();
  });
  return { map: tex(c), glow: tex(e) };
}


// sunset sky dome: violet zenith, lavender band, pink-gold glow toward the sun (back right of the key art)
const SUN = new THREE.Vector3(0.75, 0.16, -0.64).normalize();
scene.add(new THREE.Mesh(new THREE.SphereGeometry(400, 48, 24), new THREE.ShaderMaterial({
  side: THREE.BackSide, depthWrite: false, fog: false, uniforms: { sun: { value: SUN } },
  vertexShader: 'varying vec3 vD; void main(){ vD = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.); }',
  fragmentShader: `varying vec3 vD; uniform vec3 sun;
    void main(){ float h = vD.y, s = max(dot(vD, sun), 0.);
      vec3 zen = vec3(0.16,0.12,0.36), mid = vec3(0.45,0.33,0.68), hor = vec3(0.98,0.62,0.55), low = vec3(0.30,0.22,0.50);
      vec3 c = h > 0. ? mix(mix(hor, mid, smoothstep(0.,0.25,h)), zen, smoothstep(0.25,0.9,h)) : mix(hor, low, smoothstep(0.,-0.35,h));
      c += vec3(1.0,0.72,0.42) * pow(s, 6.) * 0.55 + vec3(1.,0.85,0.6) * pow(s, 60.) * 0.6;
      gl_FragColor = vec4(pow(c, vec3(2.2)), 1.); }`, // colours above are sRGB; the output pass converts back
})));
scene.fog = new THREE.Fog(0x6a4f98, 90, 360);

// light: warm low sun from the back right, lavender sky fill, cool bounce from the front left
let sun = null;
if (opts.lights !== false) {
  scene.add(new THREE.HemisphereLight(0xc2a8ff, 0x4a3460, 0.95));
  sun = new THREE.DirectionalLight(0xffc49a, opts.sunIntensity ?? 2.6); sun.position.copy(SUN).multiplyScalar(40).setY(26); sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096); Object.assign(sun.shadow.camera, { left: -22, right: 22, top: 22, bottom: -22, near: 1, far: 100 });
  sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03; scene.add(sun);
  const bounce = new THREE.DirectionalLight(0x8f88ff, 0.45); bounce.position.set(-14, 8, 16); scene.add(bounce);
}

const stoneM = (c, o = {}) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.92, metalness: 0, ...o });
const brassM = new THREE.MeshStandardMaterial({ color: 0xdcaa55, roughness: 0.35, metalness: 0.8, emissive: 0x3a2208, emissiveIntensity: 0.2 });
const ironM = new THREE.MeshStandardMaterial({ color: 0x2a2440, roughness: 0.6, metalness: 0.5 });
function add(geo, mat, parent, pos, rot, shadow = true) { const m = new THREE.Mesh(geo, mat); m.castShadow = m.receiveShadow = shadow; if (pos) m.position.set(...pos); if (rot) m.rotation.set(...rot); parent.add(m); return m; }
const world = new THREE.Group(); scene.add(world);

// ---- platform: flagstone floor, brick sides, rocky underside ----
const paving = paintPaving(3072, E + 1, 0.95, 3);
const floor = add(new THREE.PlaneGeometry(2 * E + 2, 2 * E + 2), new THREE.MeshStandardMaterial({ map: tex(paving.map), bumpMap: tex(paving.bump, false), bumpScale: 4, roughness: 0.9 }), world, [0, 0, 0], [-Math.PI / 2, 0, 0]);
floor.castShadow = false;
function brickTexture() {
  const [c, g] = canvas2d(1024, 512); g.fillStyle = '#3a3158'; g.fillRect(0, 0, 1024, 512);
  for (let y = 0, row = 0; y < 512; y += 64, row++) for (let x = -(row % 2) * 64; x < 1024; x += 128) {
    const v = Math.random() * 30 - 15; g.fillStyle = `rgb(${92 + v},${80 + v},${116 + v})`; g.fillRect(x + 4, y + 4, 120, 56);
    g.fillStyle = 'rgba(255,220,200,0.07)'; g.fillRect(x + 4, y + 4, 120, 8);
  }
  const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
const brick = brickTexture(); brick.repeat.set(6, 1);
const brickM = stoneM(0xffffff, { map: brick });
add(new THREE.BoxGeometry(2 * E + 2.2, 3.4, 2 * E + 2.2), [brickM, brickM, stoneM(0x4a3f68), stoneM(0x2a2340), brickM, brickM], world, [0, -1.71, 0]);
add(new THREE.BoxGeometry(2 * E + 3.4, 0.7, 2 * E + 3.4), stoneM(0x40365e), world, [0, -3.6, 0]);
const rock = add(new THREE.ConeGeometry(17, 16, 7, 3), stoneM(0x3b3058, { flatShading: true }), world, [0, -11.9, 0], [Math.PI, 0.3, 0]);
{ const p = rock.geometry.attributes.position; for (let i = 0; i < p.count; i++) { const y = p.getY(i); if (y > -7.9 && y < 7.9) { p.setX(i, p.getX(i) * (0.8 + rnd() * 0.35)); p.setZ(i, p.getZ(i) * (0.8 + rnd() * 0.35)); } } rock.geometry.computeVertexNormals(); }

// ---- clock dial under the boss, raised brass rims, two faint engraved circles, meshing gears ----
const dial = paintDial(2048, 'full');
const dialFace = add(new THREE.CircleGeometry(DIAL_R, 128), new THREE.MeshStandardMaterial({ map: dial.map, emissiveMap: dial.glow, emissive: 0xffb860, emissiveIntensity: 0.28, roughness: 0.55, metalness: 0.15 }), world, [0, 0.012, 0], [-Math.PI / 2, 0, 0], false); dialFace.receiveShadow = true;
add(new THREE.TorusGeometry(DIAL_R + 0.05, 0.09, 8, 160), brassM, world, [0, 0.03, 0], [Math.PI / 2, 0, 0]);
add(new THREE.TorusGeometry(DIAL_R + 0.32, 0.05, 8, 160), brassM, world, [0, 0.02, 0], [Math.PI / 2, 0, 0]);
const engraved = paintDial(1024, 'lines');
const engravedFaces = [[-8.6, -8.4, 3.4], [8.9, 8.6, 3.0]].map(([x, z, r]) => add(new THREE.CircleGeometry(r, 96), new THREE.MeshStandardMaterial({ map: engraved.map, emissiveMap: engraved.glow, emissive: 0xffb860, emissiveIntensity: 0.18, transparent: true, opacity: 0.75, roughness: 0.5, depthWrite: false }), world, [x, 0.01, z], [-Math.PI / 2, 0, hash(x, z) * 6], false));
const gears = [];
for (const [x, z, r, n, dir] of [[3.6, 5.6, 0.8, 14, 1], [4.75, 5.15, 0.48, 9, -1]]) {
  const gr = new THREE.Group(); gr.position.set(x, 0.05, z); world.add(gr); gears.push([gr, dir / r]);
  add(new THREE.TorusGeometry(r, 0.09, 8, 48), brassM, gr, [0, 0, 0], [Math.PI / 2, 0, 0]);
  for (let i = 0; i < n; i++) { const a = i / n * Math.PI * 2; add(new THREE.BoxGeometry(0.16, 0.1, 0.18), brassM, gr, [Math.cos(a) * (r + 0.1), 0, Math.sin(a) * (r + 0.1)], [0, -a, 0]); }
  add(new THREE.CylinderGeometry(r * 0.3, r * 0.3, 0.12, 16), brassM, gr);
  for (let i = 0; i < 4; i++) add(new THREE.BoxGeometry(0.07, 0.06, r * 1.8), brassM, gr, [0, 0, 0], [0, i * Math.PI / 4, 0]);
}

// ---- parapet: posts with caps, low block walls, broken gaps, rubble, an open front stair ----
const wallM = stoneM(0x8a7ca6), wallDarkM = stoneM(0x6c5f8c), capM = stoneM(0x9c8eb6);
function blockAt(parent, x, y, z, w, h, d, mat, ry = 0) { return add(new THREE.BoxGeometry(w, h, d), mat, parent, [x, y + h / 2, z], [0, ry, 0]); }
const WL = E + 0.55; // wall line
for (let side = 0; side < 4; side++) {
  const g = new THREE.Group(); g.rotation.y = side * Math.PI / 2; world.add(g);
  for (let i = 0; i <= 8; i++) {
    const t = -WL + i * (2 * WL / 8), ph = 1.25 + hash(i, side) * 0.25;
    if (!(side === 0 && i === 4)) { blockAt(g, t, 0, WL, 0.95, ph, 0.95, wallDarkM); blockAt(g, t, ph, WL, 1.1, 0.22, 1.1, capM); }
    if (i === 8) break;
    if (side === 0 && (i === 3 || i === 4)) continue; // stair opening
    const seg = 2 * WL / 8 - 0.95, cx = t + 2 * WL / 16;
    if (hash(side * 9 + i, 4) < 0.18) { blockAt(g, cx - seg * 0.3, 0, WL, seg * 0.35, 0.45, 0.6, wallM, 0.1); blockAt(g, cx + 0.4, 0, WL + 0.75, 0.6, 0.45, 0.55, wallM, 0.7); continue; }
    for (let k = 0; k < 3; k++) blockAt(g, cx - seg / 3 + k * seg / 3, 0, WL, seg / 3 - 0.04, 0.62 + hash(k, i + side) * 0.06, 0.6, wallM, (hash(k, i) - 0.5) * 0.03);
    blockAt(g, cx, 0.66, WL, seg, 0.14, 0.72, capM);
  }
  if (side === 0) for (let s = 0; s < 4; s++) blockAt(g, 0, -0.45 * (s + 1), WL + 0.6 + s * 0.75, 5.2, 0.45, 0.75, wallM);
}

// ---- lanterns: iron post, brass cage, glowing glass, a real warm light ----
const glassM = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffc56a).multiplyScalar(2.4), toneMapped: false });
function glowSprite(size, color, opacity) {
  const [c, g] = canvas2d(128); const grd = g.createRadialGradient(64, 64, 0, 64, 64, 64); grd.addColorStop(0, 'rgba(255,255,255,1)'); grd.addColorStop(0.3, 'rgba(255,255,255,0.35)'); grd.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grd; g.fillRect(0, 0, 128, 128);
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex(c), color, transparent: true, opacity, blending: THREE.AdditiveBlending, depthWrite: false, fog: false })); s.scale.setScalar(size); return s;
}
function lantern(x, z, h = 3.2) {
  const g = new THREE.Group(); g.position.set(x, 0, z); world.add(g);
  add(new THREE.CylinderGeometry(0.3, 0.38, 0.3, 8), ironM, g, [0, 0.15, 0]);
  add(new THREE.CylinderGeometry(0.07, 0.09, h, 8), ironM, g, [0, h / 2, 0]);
  add(new THREE.BoxGeometry(0.42, 0.06, 0.42), brassM, g, [0, h + 0.03, 0]);
  add(new THREE.BoxGeometry(0.3, 0.46, 0.3), glassM, g, [0, h + 0.29, 0], null, false);
  for (const [sx, sz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) add(new THREE.BoxGeometry(0.04, 0.5, 0.04), brassM, g, [sx * 0.17, h + 0.29, sz * 0.17]);
  add(new THREE.ConeGeometry(0.32, 0.32, 4), brassM, g, [0, h + 0.68, 0], [0, Math.PI / 4, 0]);
  add(new THREE.SphereGeometry(0.06, 8, 6), brassM, g, [0, h + 0.88, 0]);
  const s = glowSprite(2.6, 0xffb35a, 0.75); s.position.set(0, h + 0.29, 0); g.add(s);
  const l = new THREE.PointLight(0xffa850, 6, 8, 2); l.position.set(0, h + 0.2, 0); g.add(l);
}
for (const [x, z] of [[-E + 0.6, -E + 0.6], [E - 0.6, -E + 0.6], [-E + 0.6, E - 0.6], [E - 0.6, E - 0.6], [-2.8, E - 0.3], [2.8, E - 0.3], [-E + 0.3, 0], [E - 0.3, 0]]) lantern(x, z);

// ---- banners: navy cloth with gold border and clock-compass emblem, on poles with a crossbar ----
function bannerTexture() {
  const [c, g] = canvas2d(512, 1280);
  g.beginPath(); g.moveTo(0, 0); g.lineTo(512, 0); g.lineTo(512, 1100); g.lineTo(256, 1270); g.lineTo(0, 1100); g.closePath();
  const grd = g.createLinearGradient(0, 0, 512, 0); grd.addColorStop(0, '#1a2050'); grd.addColorStop(0.5, '#2c3778'); grd.addColorStop(1, '#1a2050');
  g.fillStyle = grd; g.fill(); g.save(); g.clip();
  for (let i = 0; i < 40; i++) { g.fillStyle = `rgba(0,0,0,${0.03 + Math.random() * 0.05})`; g.fillRect(Math.random() * 512, 0, 4 + Math.random() * 16, 1280); }
  g.restore();
  g.strokeStyle = '#d9a64c'; g.lineWidth = 12; g.beginPath(); g.moveTo(26, 30); g.lineTo(486, 30); g.lineTo(486, 1088); g.lineTo(256, 1236); g.lineTo(26, 1088); g.closePath(); g.stroke();
  g.save(); g.translate(256, 560); g.strokeStyle = '#efbd5e'; g.fillStyle = '#e6b058'; g.lineWidth = 12;
  g.beginPath(); g.arc(0, 0, 150, 0, 7); g.stroke(); g.lineWidth = 5; g.beginPath(); g.arc(0, 0, 110, 0, 7); g.stroke();
  for (let i = 0; i < 4; i++) { g.save(); g.rotate(i * Math.PI / 2); g.beginPath(); g.moveTo(0, -230); g.lineTo(16, -40); g.lineTo(-16, -40); g.closePath(); g.fill(); g.restore(); }
  for (let i = 0; i < 4; i++) { g.save(); g.rotate(i * Math.PI / 2 + Math.PI / 4); g.beginPath(); g.moveTo(0, -120); g.lineTo(9, -40); g.lineTo(-9, -40); g.closePath(); g.fill(); g.restore(); }
  for (let i = 0; i < 12; i++) { g.save(); g.rotate(i * Math.PI / 6); g.fillRect(-4, -146, 8, 22); g.restore(); }
  g.beginPath(); g.arc(0, 0, 26, 0, 7); g.fill(); g.restore();
  g.fillStyle = '#d9a64c'; for (const y of [140, 970]) { g.beginPath(); g.moveTo(256, y - 30); g.lineTo(286, y); g.lineTo(256, y + 30); g.lineTo(226, y); g.closePath(); g.fill(); }
  return tex(c);
}
const bannerMat = new THREE.MeshStandardMaterial({ map: bannerTexture(), roughness: 0.85, side: THREE.DoubleSide, alphaTest: 0.5 });
const banners = [];
function banner(x, z, ry, h = 5.4) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; world.add(g);
  add(new THREE.CylinderGeometry(0.08, 0.1, h, 8), ironM, g, [0, h / 2, 0]);
  add(new THREE.CylinderGeometry(0.05, 0.05, 1.9, 8), brassM, g, [0, h - 0.25, 0.12], [0, 0, Math.PI / 2]);
  for (const s of [-1, 1]) add(new THREE.SphereGeometry(0.08, 8, 6), brassM, g, [s * 0.97, h - 0.25, 0.12]);
  add(new THREE.ConeGeometry(0.12, 0.4, 6), brassM, g, [0, h + 0.2, 0]);
  const geo = new THREE.PlaneGeometry(1.6, 4.0, 8, 20), b = add(geo, bannerMat, g, [0, h - 0.3 - 2.0, 0.14]);
  b.userData.base = geo.attributes.position.array.slice(); banners.push(b);
}
banner(-E - 0.2, -5.5, Math.PI / 2); banner(-E - 0.2, 5.5, Math.PI / 2); banner(-4.6, E + 0.2, 0); banner(4.6, E + 0.2, 0); banner(E + 0.2, -6, -Math.PI / 2);

// ---- greenery tucked against the parapet ----
const leafM = [stoneM(0x2f5a4c, { flatShading: true }), stoneM(0x3d6f52, { flatShading: true }), stoneM(0x284a48, { flatShading: true })];
for (let i = 0; i < 26; i++) { const side = Math.floor(rnd() * 4), t = (rnd() * 2 - 1) * (E - 1), p = [[t, E - 0.2], [t, -E + 0.2], [E - 0.2, t], [-E + 0.2, t]][side];
  for (let k = 0; k < 3; k++) { const s = 0.3 + rnd() * 0.35; add(new THREE.IcosahedronGeometry(s, 0), leafM[k % 3], world, [p[0] + (rnd() - 0.5) * 0.9, s * 0.6, p[1] + (rnd() - 0.5) * 0.5]); } }

// ---- the world around: sea of clouds, distant sky islands with domes and a clock cathedral, floating debris ----
const cloudTex = (() => { const [c, g] = canvas2d(256); for (let i = 0; i < 14; i++) { const x = 60 + Math.random() * 136, y = 80 + Math.random() * 96, r = 30 + Math.random() * 50;
  const grd = g.createRadialGradient(x, y, 0, x, y, r); grd.addColorStop(0, 'rgba(255,255,255,0.55)'); grd.addColorStop(1, 'rgba(255,255,255,0)'); g.fillStyle = grd; g.beginPath(); g.arc(x, y, r, 0, 7); g.fill(); } return tex(c); })();
function cloud(x, y, z, s, color) { if (opts.noClouds) return; const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: cloudTex, color, transparent: true, depthWrite: false, opacity: 0.85 })); sp.position.set(x, y, z); sp.scale.set(s, s * 0.55, 1); scene.add(sp); return sp; }
const cloudCols = [0x5a4590, 0x7a5aa8, 0xc0849e, 0xe89a88, 0x463a7c];
for (let i = 0; i < 110; i++) { const a = rnd() * Math.PI * 2, r = 40 + rnd() * 150; cloud(Math.cos(a) * r, -16 - rnd() * 14 + (r > 70 ? rnd() * 18 : 0), Math.sin(a) * r, 18 + rnd() * 30, cloudCols[i % 5]); }
for (let i = 0; i < 24; i++) { const a = rnd() * Math.PI * 2, r = 34 + rnd() * 25; cloud(Math.cos(a) * r, -12 - rnd() * 8, Math.sin(a) * r, 14 + rnd() * 16, cloudCols[(i + 2) % 5]); }

// ---- the sky city, built from the key art's pieces: floating rock islands, blue-domed rotundas, a gothic
// cathedral with a glowing clock window, slim spires, arched bridges and hanging rocks. Every island faces the
// platform; distance fog does the painterly fade. Sizes in metres before each island's scale.
const cityStone = stoneM(0x8576ad, { flatShading: true }), cityStoneDark = stoneM(0x54487e, { flatShading: true }), cityTrim = stoneM(0xa898c4);
const domeM = new THREE.MeshStandardMaterial({ color: 0x31408f, roughness: 0.35, metalness: 0.35 });
const roofM = stoneM(0x3b3a7c, { flatShading: true }), rockM = stoneM(0x4a3d6e, { flatShading: true });
const warmM = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffb35e).multiplyScalar(1.5), toneMapped: false });
const roseM = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffc879).multiplyScalar(1.9), toneMapped: false, side: THREE.DoubleSide });
const S0 = (geo, mat, parent, pos, rot) => add(geo, mat, parent, pos, rot, false);
const archShape = (w, h) => { const s = new THREE.Shape(); s.moveTo(-w / 2, 0); s.lineTo(w / 2, 0); s.lineTo(w / 2, h - w * 0.6); s.quadraticCurveTo(w / 2, h - w * 0.1, 0, h); s.quadraticCurveTo(-w / 2, h - w * 0.1, -w / 2, h - w * 0.6); s.closePath(); return new THREE.ShapeGeometry(s); };
function windowAt(parent, x, y, z, w, h, ry = 0, lit = true) { const m = S0(archShape(w, h), lit ? warmM : stoneM(0x231c40), parent, [x, y, z], [0, ry, 0]); return m; }

function rockIsland(parent, r, depth, seed) {
  const r2 = mulberry32(seed), g = new THREE.Group(); parent.add(g);
  const geo = new THREE.ConeGeometry(r, depth, 9, 5); const p = geo.attributes.position;
  for (let i = 0; i < p.count; i++) { const y = p.getY(i); if (y < depth / 2 - 0.01) { const k = 0.75 + r2() * 0.45; p.setX(i, p.getX(i) * k); p.setZ(i, p.getZ(i) * k); p.setY(i, y + (r2() - 0.5) * depth * 0.06); } }
  geo.computeVertexNormals(); S0(geo, rockM, g, [0, -depth / 2, 0], [Math.PI, 0, 0]);
  S0(new THREE.CylinderGeometry(r * 1.02, r * 0.95, 1.2, 10), cityStoneDark, g, [0, -0.6, 0]);
  for (let i = 0; i < 7; i++) { const a = r2() * 6.28, rr = r * (0.7 + r2() * 0.3), s = 0.6 + r2() * 1.1; S0(new THREE.IcosahedronGeometry(s, 0), leafM[i % 3], g, [Math.cos(a) * rr, s * 0.3, Math.sin(a) * rr]); }
  for (let i = 0; i < 3; i++) { const a = r2() * 6.28, s = 0.8 + r2() * 1.5; S0(new THREE.ConeGeometry(s, s * 3, 5), rockM, g, [Math.cos(a) * r * 1.3, -depth * (0.3 + r2() * 0.4), Math.sin(a) * r * 1.3], [Math.PI, r2(), 0]); }
  return g;
}
function rotunda(parent, x, z, s = 1) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.scale.setScalar(s); parent.add(g);
  S0(new THREE.BoxGeometry(6.4, 2.4, 6.4), cityStoneDark, g, [0, 1.2, 0]);
  for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2; windowAt(g, Math.sin(a) * 3.21, 0.2, Math.cos(a) * 3.21, 1.4, 1.9, a, false); }
  S0(new THREE.CylinderGeometry(3.0, 3.1, 0.4, 24), cityTrim, g, [0, 2.6, 0]);
  S0(new THREE.CylinderGeometry(2.6, 2.6, 3.4, 20), cityStone, g, [0, 4.5, 0]);
  for (let i = 0; i < 10; i++) { const a = i / 10 * Math.PI * 2;
    S0(new THREE.BoxGeometry(0.35, 3.4, 0.35), cityTrim, g, [Math.sin(a) * 2.65, 4.5, Math.cos(a) * 2.65], [0, a, 0]);
    windowAt(g, Math.sin(a + 0.31) * 2.62, 3.4, Math.cos(a + 0.31) * 2.62, 0.75, 1.8, a + 0.31); }
  S0(new THREE.CylinderGeometry(2.85, 2.7, 0.45, 24), cityTrim, g, [0, 6.4, 0]);
  S0(new THREE.TorusGeometry(2.75, 0.12, 6, 32), brassM, g, [0, 6.65, 0], [Math.PI / 2, 0, 0]);
  S0(new THREE.SphereGeometry(2.6, 24, 12, 0, Math.PI * 2, 0, Math.PI / 2), domeM, g, [0, 6.6, 0]);
  for (let i = 0; i < 4; i++) S0(new THREE.TorusGeometry(2.62, 0.07, 4, 24, Math.PI), brassM, g, [0, 6.6, 0], [0, i * Math.PI / 4, 0]);
  S0(new THREE.CylinderGeometry(0.6, 0.7, 1.1, 8), cityTrim, g, [0, 9.6, 0]);
  for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2 + 0.4; windowAt(g, Math.sin(a) * 0.66, 9.2, Math.cos(a) * 0.66, 0.3, 0.7, a); }
  S0(new THREE.SphereGeometry(0.7, 12, 6, 0, Math.PI * 2, 0, Math.PI / 2), domeM, g, [0, 10.15, 0]);
  S0(new THREE.ConeGeometry(0.16, 2.2, 6), brassM, g, [0, 11.8, 0]);
  S0(new THREE.SphereGeometry(0.2, 8, 6), brassM, g, [0, 10.9, 0]);
  return g;
}
function spire(parent, x, z, h, w = 1.6, s = 1) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.scale.setScalar(s); parent.add(g);
  S0(new THREE.BoxGeometry(w, h, w), cityStone, g, [0, h / 2, 0]);
  S0(new THREE.BoxGeometry(w * 1.15, 0.3, w * 1.15), cityTrim, g, [0, h * 0.62, 0]);
  S0(new THREE.BoxGeometry(w * 1.2, 0.4, w * 1.2), cityTrim, g, [0, h, 0]);
  for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2; windowAt(g, Math.sin(a) * (w / 2 + 0.01), h * 0.7, Math.cos(a) * (w / 2 + 0.01), w * 0.38, w * 0.9, a); }
  S0(new THREE.ConeGeometry(w * 0.72, w * 3.2, 4), roofM, g, [0, h + 0.2 + w * 1.6, 0], [0, Math.PI / 4, 0]);
  for (const [sx, sz] of [[-1, -1], [1, -1], [-1, 1], [1, 1]]) S0(new THREE.ConeGeometry(w * 0.13, w * 0.9, 4), roofM, g, [sx * w * 0.55, h + 0.65, sz * w * 0.55]);
  S0(new THREE.ConeGeometry(0.07, 1.2, 4), brassM, g, [0, h + 0.2 + w * 3.4, 0]);
  return g;
}
function cathedral(parent, s = 1) {
  const g = new THREE.Group(); g.scale.setScalar(s); parent.add(g);
  S0(new THREE.BoxGeometry(11, 14, 8), cityStone, g, [0, 7, -2]);                                 // nave block
  const gable = new THREE.Shape(); gable.moveTo(-5.5, 0); gable.lineTo(5.5, 0); gable.lineTo(0, 5); gable.closePath();
  S0(new THREE.ExtrudeGeometry(gable, { depth: 8, bevelEnabled: false }), cityStone, g, [0, 14, -6]);
  S0(new THREE.CylinderGeometry(3.6, 3.6, 0.6, 40), cityTrim, g, [0, 8.5, 2.05], [Math.PI / 2, 0, 0]); // clock / rose window
  S0(new THREE.CircleGeometry(3.2, 40), roseM, g, [0, 8.5, 2.4]);
  S0(new THREE.TorusGeometry(3.25, 0.18, 6, 40), brassM, g, [0, 8.5, 2.45]);
  S0(new THREE.TorusGeometry(1.3, 0.14, 6, 32), brassM, g, [0, 8.5, 2.47]);
  for (let i = 0; i < 12; i++) S0(new THREE.BoxGeometry(0.14, 3.2, 0.1), cityStoneDark, g, [Math.sin(i / 12 * Math.PI * 2) * 1.6, 8.5 + Math.cos(i / 12 * Math.PI * 2) * 1.6, 2.5], [0, 0, -i / 12 * Math.PI * 2]);
  S0(new THREE.SphereGeometry(0.45, 12, 8), brassM, g, [0, 8.5, 2.55]);
  windowAt(g, 0, 0, 2.02, 3, 5, 0);                                                               // great door, lit
  for (const sx of [-1, 1]) {                                                                     // twin towers
    spire(g, sx * 7, 0, 22, 3.2);
    for (let k = 0; k < 3; k++) windowAt(g, sx * 7, 4 + k * 5, 1.62, 1.1, 2.6, 0);
    for (let k = 0; k < 3; k++) S0(new THREE.BoxGeometry(0.8, 9 - k * 2, 1.2), cityStoneDark, g, [sx * 5.4, (9 - k * 2) / 2, -5 + k * 3]); // buttresses
    for (let k = 0; k < 2; k++) windowAt(g, sx * 3.2, 10 + k * 0.01, 2.02, 1.1, 2.8, 0);
  }
  for (const sx of [-1, 1]) { const w = new THREE.Group(); w.position.set(sx * 13, 0, -1.5); g.add(w);        // side wings
    S0(new THREE.BoxGeometry(6, 8, 6), cityStone, w, [0, 4, 0]);
    for (let k = 0; k < 3; k++) windowAt(w, -2 + k * 2, 3, 3.02, 0.9, 2.2, 0);
    spire(w, sx * 1.8, 1.8, 11, 1.6); spire(w, -sx * 2.2, -1.5, 8, 1.2); }
  for (let i = 0; i < 6; i++) S0(new THREE.ConeGeometry(0.25, 2, 4), roofM, g, [-4.6 + i * 1.84, 14.5 + (i % 2) * 0.4, 1.8]); // pinnacles
  S0(new THREE.ConeGeometry(0.9, 9, 6), roofM, g, [0, 22.5, -3]);                                // central fleche
  return g;
}
function bridge(parent, a, b, y, w = 2.6) {
  const dx = b[0] - a[0], dz = b[1] - a[1], len = Math.hypot(dx, dz), g = new THREE.Group();
  g.position.set(a[0] + dx / 2, y, a[1] + dz / 2); g.rotation.y = Math.atan2(dx, dz); parent.add(g);
  S0(new THREE.BoxGeometry(w, 0.9, len), cityStone, g, [0, -0.45, 0]);
  for (const sx of [-1, 1]) { S0(new THREE.BoxGeometry(0.18, 0.12, len), cityTrim, g, [sx * (w / 2 - 0.1), 0.85, 0]);
    for (let t = -len / 2; t <= len / 2; t += 1.2) S0(new THREE.BoxGeometry(0.14, 0.8, 0.14), cityTrim, g, [sx * (w / 2 - 0.1), 0.4, t]); }
  const spans = Math.max(1, Math.round(len / 9)), sl = len / spans;
  for (let i = 0; i < spans; i++) { const zc = -len / 2 + sl * (i + 0.5);
    S0(new THREE.TorusGeometry(sl / 2 - 0.6, 0.55, 6, 20, Math.PI), cityStone, g, [0, -0.9, zc], [Math.PI, Math.PI / 2, 0]);
    if (i > 0) S0(new THREE.BoxGeometry(w * 0.9, 14, 1.4), cityStoneDark, g, [0, -7.9, -len / 2 + sl * i]);
    if (i % 2 === 0) { S0(new THREE.CylinderGeometry(0.05, 0.05, 1.6, 6), ironM, g, [w / 2 - 0.1, 1.2, zc]); S0(new THREE.BoxGeometry(0.25, 0.35, 0.25), warmM, g, [w / 2 - 0.1, 2.1, zc]); }
  }
  return g;
}
function hangingRock(x, y, z, s, seed) { const g = new THREE.Group(); g.position.set(x, y, z); g.scale.setScalar(s); scene.add(g);
  const r2 = mulberry32(seed); for (let i = 0; i < 4; i++) { const h = 6 + r2() * 10, w = 1 + r2() * 1.6;
    S0(new THREE.CylinderGeometry(w, w * 0.4, h, 6), rockM, g, [(r2() - 0.5) * 6, -h / 2, (r2() - 0.5) * 4], [0, r2() * 3, 0]); }
  S0(new THREE.BoxGeometry(8, 2, 6), rockM, g, [0, 0.5, 0]); return g; }

// islands: [x, y, z, scale, builder]
function islandAt(x, y, z, s, build) { const g = new THREE.Group(); g.position.set(x, y, z); g.scale.setScalar(s); g.lookAt(0, y, 0); scene.add(g); build(g); return g; }
const CITY = [
  [72, -26, -92, 2.1, g => { rockIsland(g, 22, 34, 1); cathedral(g, 1); spire(g, -17, -10, 14, 2); spire(g, 18, -9, 16, 2.2); }],      // the clock cathedral, back right
  [-48, -12, -42, 1.3, g => { rockIsland(g, 7, 18, 2); rotunda(g, 0, 0, 1); }],                                                     // blue dome, back left
  [44, -14, 26, 1.25, g => { rockIsland(g, 7, 16, 3); rotunda(g, 0, 0, 1.05); spire(g, 4.6, -3, 8, 1.2); }],                         // blue dome, right
  [-12, -24, -98, 1.6, g => { rockIsland(g, 9, 20, 4); spire(g, 0, 0, 16, 2.4); spire(g, -4, 2, 10, 1.6); spire(g, 4, 2.5, 12, 1.8); }],
  [-90, -22, 8, 1.5, g => { rockIsland(g, 8, 18, 5); rotunda(g, 0, -1, 0.9); spire(g, -5, 2, 11, 1.5); }],
  [-52, -30, 52, 1.3, g => { rockIsland(g, 7, 15, 6); spire(g, 0, 0, 12, 2); spire(g, 3.5, 1, 7, 1.3); }],
  [100, -30, -20, 1.6, g => { rockIsland(g, 9, 18, 7); rotunda(g, 0, 0, 1); spire(g, -6, 1, 13, 1.8); }],
  [20, -34, 92, 1.5, g => { rockIsland(g, 8, 16, 8); rotunda(g, 0, 0, 0.9); }],
  [130, -20, -110, 1.8, g => { rockIsland(g, 9, 18, 9); spire(g, 0, 0, 20, 2.6); spire(g, 5, 2, 12, 1.6); }],
  [-120, -26, -80, 1.8, g => { rockIsland(g, 10, 20, 10); rotunda(g, 0, 0, 1.2); spire(g, 6, 1, 14, 1.8); spire(g, -6, 1, 10, 1.5); }],
  // a low, near ring so the steep gameplay view sees city past every edge
  [-50, -46, -14, 1, g => { rockIsland(g, 6, 12, 11); rotunda(g, 0, 0, 0.8); }],
  [52, -48, -34, 1, g => { rockIsland(g, 6, 12, 12); spire(g, 0, 0, 12, 1.8); spire(g, 3, 2, 8, 1.2); }],
  [0, -52, -62, 1, g => { rockIsland(g, 7, 13, 13); spire(g, -2.5, 0, 10, 1.6); spire(g, 2.5, 0, 13, 1.8); }],
  [-52, -50, 40, 1, g => { rockIsland(g, 6, 12, 14); spire(g, 0, 0, 9, 1.5); }],
  [50, -50, 52, 1, g => { rockIsland(g, 6, 12, 15); rotunda(g, 0, 0, 0.75); }],
];
const islands = CITY.map(c => islandAt(...c));
// bridges between neighbouring islands, at deck height
bridge(scene, [-48 + 7, -42 + 4], [-12 - 5, -98 + 9], -12.4, 2.6);
bridge(scene, [44 - 3, 26 - 6], [72 - 18, -92 + 20], -14.4, 2.6);
bridge(scene, [-90 + 9, 8 - 3], [-48 - 7, -42 + 3], -12.8, 2.4);
hangingRock(-60, 26, -90, 1.4, 21); hangingRock(40, 30, -120, 1.6, 22); hangingRock(-110, 22, -30, 1.3, 23); hangingRock(110, 24, -60, 1.2, 24);

const debris = [];
for (let i = 0; i < 16; i++) { const a = rnd() * Math.PI * 2, r = E + 4 + rnd() * 10, s = 0.3 + rnd() * 0.9;
  const m = add(new THREE.BoxGeometry(s, s * (0.7 + rnd() * 0.6), s), wallDarkM, scene, [Math.cos(a) * r, -5 + rnd() * 6, Math.sin(a) * r], [rnd(), rnd() * 3, rnd()]); debris.push([m, m.position.y, rnd() * 6]); }

function update(t, dt = 0, quiet = false) {
  for (const b of banners) { const p = b.geometry.attributes.position, base = b.userData.base;
    for (let i = 0; i < p.count; i++) { const y = base[i * 3 + 1], drop = (2 - y) / 4; p.setZ(i, Math.sin(t * 1.3 + y * 1.4 + b.id) * 0.07 * drop + Math.sin(base[i * 3] * 2.5) * 0.04); }
    p.needsUpdate = true; b.geometry.computeVertexNormals(); }
  for (const [g, k] of gears) g.rotation.y = t * 0.25 * k;
  for (const [m, y0, ph] of debris) { m.position.y = y0 + Math.sin(t * 0.5 + ph) * 0.25; m.rotation.y += 0.0015; }
  const k = Math.min(1, dt * 4); // floor decoration dims while a warning is up
  dialFace.material.emissiveIntensity += ((quiet ? 0.08 : 0.28) - dialFace.material.emissiveIntensity) * k;
  for (const m of engravedFaces) m.material.opacity += ((quiet ? 0.3 : 0.75) - m.material.opacity) * k;
}
return { E, sun, update };
};
// </arena-env>
