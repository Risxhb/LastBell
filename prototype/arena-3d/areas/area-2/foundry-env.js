// <foundry-env> Area 2, The Brass Foundry: the shared arena for its three minibosses and area boss, rebuilt from the
// Brass Foundry panel of Reference/LAST-BELL-ARENA-ART-HANDOFF/artwork/a_wide_cinematic_concept_art_style_layout_poster.png.
// A wheel-shaped bronze floor (eight brass spokes, eight glowing furnace-window grids, a sun medallion, gold scrollwork)
// inside a chunky raised rim, on an arched furnace drum. Four gate arms lead over truss bridges to gothic foundry towers;
// cranes hang chains and cage lanterns; molten falls pour into a glowing orange abyss. Classic script: call
//   const env = window.buildFoundryEnv(THREE, scene);
// then env.update(timeSeconds, dt, quiet) every frame (quiet = attack telegraphs are up: floor glow dims).
// Returns { R, root, sun, update }. Units are metres; the playable floor is the disc r <= R, the boss starts at 0,0.
// Molten metal stays below the floor or behind window grates: nothing on the playable disc reads as a hazard.
window.buildFoundryEnv = function buildFoundryEnv(THREE, scene, opts = {}) {
const R = 13.2;                  // playable floor radius (inner face of the rim)
const RIM = 15.2;                // outer edge of the raised rim, and the drum below it
const WIN = [6.0, 10.1];         // window grids, radially
const BAND = [[1.6, 1.9], [4.6, 4.95], [10.6, 10.95]]; // raised brass rings
const GATE_W = 4.4;

// ---- helpers ----
function mulberry32(a) { return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(11);
const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
const fbm = (x, y) => vnoise(x, y) * 0.55 + vnoise(x * 2.1, y * 2.1) * 0.3 + vnoise(x * 4.3, y * 4.3) * 0.15;
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const tex = (c, srgb = true) => { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t; };
const canvas2d = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };
const TAU = Math.PI * 2, SPOKE = Math.PI / 4;

// ---- floor painter: colour, height and glow in one pass, then gold inlay, scrollwork and rivets on top ----
function paintFloor(N) {
  const F = R, [map, mc] = canvas2d(N), [bump, bc] = canvas2d(N), [glow, gc] = canvas2d(N);
  const mi = mc.createImageData(N, N), bi = bc.createImageData(N, N), gi = gc.createImageData(N, N), md = mi.data, bd = bi.data, gd = gi.data;
  const seam = 0.03, bev = 0.12, LX = 0.55, LZ = -0.83;
  for (let py = 0; py < N; py++) for (let px = 0; px < N; px++) {
    const x = (px + 0.5) / N * 2 * F - F, z = (py + 0.5) / N * 2 * F - F, r = Math.hypot(x, z), o = (py * N + px) * 4;
    md[o + 3] = bd[o + 3] = gd[o + 3] = 255;
    if (r > F + 0.05) continue;
    let a = Math.atan2(z, x); if (a < 0) a += TAU;
    const k = Math.round(a / SPOKE) % 8, dSpoke = Math.abs(a - Math.round(a / SPOKE) * SPOKE) * r; // arc distance to the nearest spoke line
    const n = fbm(x * 0.45, z * 0.45), fine = vnoise(x * 9, z * 9), pit = vnoise(x * 2.6, z * 2.6 + 4);
    let cr, cg, cb, h = 0.62, gl = 0, edge = 9, nx = 0, nzz = 0, plate = 0;
    const radialSeams = (cnt, off = 0) => { const sg = TAU / cnt, da = ((a + off) % sg + sg) % sg, d = Math.min(da, sg - da) * r; plate = Math.floor((a + off) / sg); if (d < edge) { edge = d; const s = da < sg - da ? -1 : 1; nx = -Math.sin(a) * s; nzz = Math.cos(a) * s; } };
    const ringSeam = rad => { const d = Math.abs(r - rad); if (d < edge) { edge = d; const s = r > rad ? -1 : 1; nx = s * x / r; nzz = s * z / r; } };
    if (r < 1.9) { // sun medallion (aged brass), painted over below
      cr = 150; cg = 108; cb = 58; edge = 9;
    } else if (r < 4.6) { // inner ring: dark bronze plates
      cr = 76; cg = 58; cb = 46; radialSeams(12, SPOKE / 2); ringSeam(1.9); ringSeam(3.2); ringSeam(4.6); plate += r > 3.2 ? 40 : 0;
    } else if (r < 10.95) { // the window ring between the spokes
      cr = 88; cg = 66; cb = 50; ringSeam(4.95); ringSeam(10.6); plate = k + 80;
      if (dSpoke < 0.5) { cr = 54; cg = 40; cb = 32; }
      else { const d = dSpoke - 0.5; if (d < edge) { edge = d; nx = 0; nzz = 0; } }
      const sc = Math.round(a / SPOKE) * SPOKE + (a > Math.round(a / SPOKE) * SPOKE ? SPOKE / 2 : -SPOKE / 2); // sector centre angle
      const s = (a - sc) * r, hw = SPOKE / 2 * r - 1.3;
      if (r > WIN[0] - 0.28 && r < WIN[1] + 0.28 && dSpoke > 0.88) { // iron window frame
        cr = 46; cg = 34; cb = 28; h = 0.75;
        if (r > WIN[0] && r < WIN[1] && Math.abs(s) < hw) {
          const COLS = 5, ROWS = 3, fu = (s + hw) / (2 * hw) * COLS, fv = (r - WIN[0]) / (WIN[1] - WIN[0]) * ROWS, cu = fu - Math.floor(fu), cv = fv - Math.floor(fv);
          const du = Math.min(cu, 1 - cu) * 2 * hw / COLS, dv = Math.min(cv, 1 - cv) * (WIN[1] - WIN[0]) / ROWS, rc = 0.22;
          const ed = du < rc && dv < rc ? rc - Math.hypot(rc - du, rc - dv) : Math.min(du, dv); // rounded cells
          if (ed < 0.1) { cr = 40; cg = 30; cb = 24; h = 0.8; }                 // mullions
          else {
            const bar = Math.abs(cu - 0.5) * 2 * hw / COLS < 0.025;
            const heat = 0.72 + 0.28 * fbm(x * 0.7 + 5, z * 0.7), inner = smooth(0.1, 0.4, ed);
            cr = 70; cg = 24; cb = 6; h = 0.12; gl = heat * (0.45 + 0.55 * inner) * (bar ? 0.45 : 1);
            if (bar) { cr = 50; cg = 30; cb = 22; h = 0.5; }
          }
        }
      }
    } else { // outer band: 32 plates, a concentric seam
      cr = 92; cg = 70; cb = 52; radialSeams(32); ringSeam(10.95); ringSeam(12.1); ringSeam(F); plate += r > 12.1 ? 200 : 160;
    }
    const sid = hash(plate * 7.3, 3), v = (sid - 0.5) * 0.2 + (n - 0.5) * 0.36 + (fine - 0.5) * 0.08;
    cr *= 1 + v; cg *= 1 + v; cb *= 1 + v;
    if (gl === 0) {
      h += 0.3 * smooth(seam * 0.5, bev * 1.6, edge);
      if (edge < bev) { const t = 1 - smooth(seam, bev, edge), f = 1 + (nx * LX + nzz * LZ) * 0.35 * t; cr *= f; cg *= f; cb *= f; }
      if (pit < 0.14) { cr *= 0.8; cg *= 0.78; cb *= 0.8; h -= 0.08; }
      if (fine > 0.88) { cr *= 1.12; cg *= 1.08; cb *= 1.02; }
      const soot = fbm(x * 0.2 + 9, z * 0.2); if (soot > 0.62) { const s = 1 - 0.3 * smooth(0.62, 0.8, soot); cr *= s; cg *= s; cb *= s; }
      const warm = smooth(0.6, 0.25, Math.abs(r - 7.8) / 4); cr *= 1 + 0.12 * warm; cg *= 1 + 0.04 * warm;   // warmed by the windows
      if (edge < seam) { cr = 26; cg = 18; cb = 14; h = 0.16; }
    }
    md[o] = Math.min(255, cr); md[o + 1] = Math.min(255, cg); md[o + 2] = Math.min(255, cb);
    const hb = Math.max(0, Math.min(255, h * 255)); bd[o] = bd[o + 1] = bd[o + 2] = hb;
    gd[o] = 255 * gl; gd[o + 1] = 175 * gl * gl; gd[o + 2] = 80 * gl * gl * gl;
  }
  mc.putImageData(mi, 0, 0); bc.putImageData(bi, 0, 0); gc.putImageData(gi, 0, 0);
  const S = N / (2 * F), P = v => (v + F) * S, C = N / 2;
  const gold = 'rgba(214,160,82,0.92)', goldDim = 'rgba(190,136,66,0.6)', shade = 'rgba(16,10,6,0.7)';
  // engraved line: dark offset shadow, then gold; mirrored onto the height map
  const engrave = (draw, w, col = gold) => {
    mc.save(); mc.translate(1.5, 1.5); mc.strokeStyle = shade; mc.lineWidth = w * S + 1; draw(mc); mc.stroke(); mc.restore();
    mc.strokeStyle = col; mc.lineWidth = w * S; draw(mc); mc.stroke();
    bc.strokeStyle = '#fff'; bc.lineWidth = w * S; draw(bc); bc.stroke();
  };
  const arc = (rad, a0 = 0, a1 = TAU) => g => { g.beginPath(); g.arc(C, C, rad * S, a0, a1); };
  const line = pts => g => { g.beginPath(); pts.forEach(([x, z], i) => i ? g.lineTo(P(x), P(z)) : g.moveTo(P(x), P(z))); };
  mc.lineCap = bc.lineCap = 'round'; mc.lineJoin = bc.lineJoin = 'round';
  // window-grid frames: gold arcs and side lines around every grid
  for (let s = 0; s < 8; s++) {
    const c = s * SPOKE + SPOKE / 2, i0 = c - (SPOKE / 2 - 0.88 / (WIN[0] - 0.28)), i1 = c + (SPOKE / 2 - 0.88 / (WIN[0] - 0.28));
    const o0 = c - (SPOKE / 2 - 0.88 / (WIN[1] + 0.28)), o1 = c + (SPOKE / 2 - 0.88 / (WIN[1] + 0.28));
    engrave(arc(WIN[0] - 0.28, i0, i1), 0.06); engrave(arc(WIN[1] + 0.28, o0, o1), 0.06);
    for (const [a, b] of [[i0, o0], [i1, o1]]) engrave(line([[Math.cos(a) * (WIN[0] - 0.28), Math.sin(a) * (WIN[0] - 0.28)], [Math.cos(b) * (WIN[1] + 0.28), Math.sin(b) * (WIN[1] + 0.28)]]), 0.06);
  }
  // clock ticks around the inner ring and the outer band
  for (let i = 0; i < 60; i++) { const a = i / 60 * TAU, l = i % 5 ? 0.18 : 0.42; engrave(line([[Math.cos(a) * 4.42, Math.sin(a) * 4.42], [Math.cos(a) * (4.42 - l), Math.sin(a) * (4.42 - l)]]), i % 5 ? 0.035 : 0.06, i % 5 ? goldDim : gold); }
  for (let i = 0; i < 96; i++) { const a = i / 96 * TAU; engrave(line([[Math.cos(a) * 12.95, Math.sin(a) * 12.95], [Math.cos(a) * 12.75, Math.sin(a) * 12.75]]), 0.03, goldDim); }
  engrave(arc(4.42), 0.03, goldDim); engrave(arc(12.1), 0.05, goldDim); engrave(arc(3.2), 0.04, goldDim);
  for (const rr of [2.2, 2.35, 3.75, 5.45, 5.6, 10.4, 11.35, 12.6]) engrave(arc(rr), rr > 10 || rr < 3 ? 0.04 : 0.03, rr === 5.45 || rr === 11.35 ? gold : goldDim);
  // scrollwork: S-curls along the inner ring, at the spoke roots and between the window grids, cartouches on the outer band
  const curl = (cx, cz, rot, s, flip) => g => {
    g.beginPath();
    for (const side of [1, -1]) {
      for (let t = 0; t <= 2.6 * Math.PI; t += 0.12) {
        const rr = s * 0.42 * (1 - t / (3.1 * Math.PI)), ang = side * t * (flip ? -1 : 1) + (side > 0 ? 0 : Math.PI);
        const lx = side * s * 0.5 + Math.cos(ang) * rr, lz = Math.sin(ang) * rr * side;
        const wx = cx + Math.cos(rot) * lx - Math.sin(rot) * lz, wz = cz + Math.sin(rot) * lx + Math.cos(rot) * lz;
        t ? g.lineTo(P(wx), P(wz)) : g.moveTo(P(wx), P(wz));
      }
    }
  };
  for (let i = 0; i < 8; i++) {
    const a = i * SPOKE + SPOKE / 2; engrave(curl(Math.cos(a) * 2.6, Math.sin(a) * 2.6, a + Math.PI / 2, 1.3, i % 2), 0.05);
    const b = i * SPOKE; engrave(curl(Math.cos(b) * 3.9, Math.sin(b) * 3.9, b + Math.PI / 2, 0.8, 0), 0.045, goldDim);
    engrave(curl(Math.cos(a) * 5.15, Math.sin(a) * 5.15, a + Math.PI / 2, 1.6, 1), 0.04, goldDim);
    engrave(curl(Math.cos(a) * 10.45, Math.sin(a) * 10.45, a + Math.PI / 2, 2.2, 0), 0.045, goldDim);
  }
  for (let i = 0; i < 16; i++) { // outer cartouches: a ringed gear glyph between scrolls
    const a = i / 16 * TAU + TAU / 32, cx = Math.cos(a) * 11.55, cz = Math.sin(a) * 11.55;
    engrave(g => { g.beginPath(); g.arc(P(cx), P(cz), 0.28 * S, 0, TAU); }, 0.05);
    engrave(g => { g.beginPath(); for (let t = 0; t < 8; t++) { const q = t / 8 * TAU; g.moveTo(P(cx + Math.cos(q) * 0.3), P(cz + Math.sin(q) * 0.3)); g.lineTo(P(cx + Math.cos(q) * 0.42), P(cz + Math.sin(q) * 0.42)); } }, 0.05);
    engrave(curl(cx + Math.cos(a + Math.PI / 2) * 0.95, cz + Math.sin(a + Math.PI / 2) * 0.95, a + Math.PI / 2, 0.9, 0), 0.035, goldDim);
    engrave(curl(cx - Math.cos(a + Math.PI / 2) * 0.95, cz - Math.sin(a + Math.PI / 2) * 0.95, a + Math.PI / 2, 0.9, 1), 0.035, goldDim);
  }
  // gold veins: branching cracks filled with brass, like the in-game reference
  for (let i = 0; i < 26; i++) {
    let rad = i % 2 ? 2.2 + rnd() * 2.2 : 11.1 + rnd() * 1.7, a = rnd() * TAU, d = rnd() * TAU, pts = [[Math.cos(a) * rad, Math.sin(a) * rad]];
    for (let s = 0; s < 5 + rnd() * 6; s++) { d += (rnd() - 0.5) * 1.4; const [px, pz] = pts[pts.length - 1]; const nx2 = px + Math.cos(d) * 0.35, nz2 = pz + Math.sin(d) * 0.35, rr = Math.hypot(nx2, nz2); if ((i % 2 && (rr < 2 || rr > 4.5)) || (!(i % 2) && (rr < 11 || rr > 13))) break; pts.push([nx2, nz2]); }
    if (pts.length > 2) engrave(line(pts), 0.035, 'rgba(226,170,86,0.8)');
  }
  // sun medallion: sunburst rays, rings and ticks
  for (let i = 0; i < 32; i++) {
    const a = i / 32 * TAU, w = i % 2 ? 0.05 : 0.09, l = i % 2 ? 1.25 : 1.55;
    mc.fillStyle = i % 2 ? 'rgba(110,74,38,0.9)' : 'rgba(226,176,96,0.95)';
    mc.beginPath(); mc.moveTo(P(Math.cos(a - w) * 0.55), P(Math.sin(a - w) * 0.55)); mc.lineTo(P(Math.cos(a) * l), P(Math.sin(a) * l)); mc.lineTo(P(Math.cos(a + w) * 0.55), P(Math.sin(a + w) * 0.55)); mc.fill();
  }
  engrave(arc(0.55), 0.06); engrave(arc(1.0), 0.03, goldDim);
  { const gg = gc.createRadialGradient(C, C, 0, C, C, 0.55 * S); gg.addColorStop(0, 'rgba(255,200,110,1)'); gg.addColorStop(0.7, 'rgba(255,120,30,0.7)'); gg.addColorStop(1, 'rgba(0,0,0,0)'); gc.fillStyle = gg; gc.beginPath(); gc.arc(C, C, 0.55 * S, 0, TAU); gc.fill(); }
  // rivets along the seams
  for (const [rad, count] of [[2.05, 24], [4.45, 60], [5.1, 80], [10.45, 140], [11.1, 140], [12.95, 160]]) for (let i = 0; i < count; i++) {
    const a = i / count * TAU, x = P(Math.cos(a) * rad), y = P(Math.sin(a) * rad);
    mc.fillStyle = 'rgba(20,12,8,0.6)'; mc.beginPath(); mc.arc(x + 1.2, y + 1.2, 0.055 * S, 0, 7); mc.fill();
    mc.fillStyle = 'rgba(198,150,88,0.95)'; mc.beginPath(); mc.arc(x, y, 0.05 * S, 0, 7); mc.fill();
    bc.fillStyle = '#fff'; bc.beginPath(); bc.arc(x, y, 0.055 * S, 0, 7); bc.fill();
  }
  for (let i = 0; i < 90; i++) { // hairline cracks and scratches
    let rad = 2 + rnd() * (F - 2.5), a = rnd() * TAU, x = Math.cos(a) * rad, z = Math.sin(a) * rad, d = rnd() * TAU;
    mc.strokeStyle = 'rgba(20,12,8,0.55)'; mc.lineWidth = 1.4; bc.strokeStyle = 'rgba(30,30,30,0.7)'; bc.lineWidth = 1.4;
    mc.beginPath(); bc.beginPath(); mc.moveTo(P(x), P(z)); bc.moveTo(P(x), P(z));
    for (let s = 0; s < 3 + rnd() * 5; s++) { d += (rnd() - 0.5) * 1.3; x += Math.cos(d) * 0.2; z += Math.sin(d) * 0.2; mc.lineTo(P(x), P(z)); bc.lineTo(P(x), P(z)); }
    mc.stroke(); bc.stroke();
  }
  return { map, bump, glow };
}

// ---- surface textures: masonry, riveted iron, furnace glow, molten sea and falls ----
function masonryTexture() {
  const [c, g] = canvas2d(512); g.fillStyle = '#2a1e19'; g.fillRect(0, 0, 512, 512);
  for (let y = 0, row = 0; y < 512; y += 64, row++) for (let x = -(row % 2) * 48; x < 512; x += 96) {
    const v = rnd() * 26 - 13; g.fillStyle = `rgb(${84 + v},${66 + v},${56 + v})`; g.fillRect(x + 3, y + 3, 90, 58);
    g.fillStyle = 'rgba(255,190,130,0.08)'; g.fillRect(x + 3, y + 3, 90, 7);
    g.fillStyle = 'rgba(0,0,0,0.18)'; g.fillRect(x + 3, y + 52, 90, 9);
  }
  for (let i = 0; i < 1400; i++) { g.fillStyle = `rgba(${rnd() < 0.6 ? '16,10,8' : '140,100,70'},${rnd() * 0.18})`; g.fillRect(rnd() * 512, rnd() * 512, 2 + rnd() * 8, 2 + rnd() * 8); }
  const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
function plateTexture() {
  const [c, g] = canvas2d(512); g.fillStyle = '#2e221d'; g.fillRect(0, 0, 512, 512);
  for (let y = 0; y < 512; y += 128) for (let x = 0; x < 512; x += 128) {
    const v = rnd() * 22 - 11; g.fillStyle = `rgb(${78 + v},${60 + v},${48 + v})`; g.fillRect(x + 3, y + 3, 122, 122);
    g.fillStyle = 'rgba(255,200,150,0.07)'; g.fillRect(x + 3, y + 3, 122, 6); g.fillStyle = 'rgba(0,0,0,0.2)'; g.fillRect(x + 3, y + 118, 122, 7);
    g.fillStyle = 'rgba(205,155,92,0.85)'; for (let i = 0; i < 5; i++) for (const [px, py] of [[x + 10 + i * 27, y + 11], [x + 10 + i * 27, y + 117]]) { g.beginPath(); g.arc(px, py, 3, 0, 7); g.fill(); }
  }
  for (let i = 0; i < 1100; i++) { g.fillStyle = `rgba(${rnd() < 0.5 ? '18,10,6' : '150,100,60'},${rnd() * 0.2})`; g.fillRect(rnd() * 512, rnd() * 512, 2 + rnd() * 10, 2 + rnd() * 10); }
  const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
function furnaceTexture() { // inside of an arched furnace mouth: white-yellow at the bottom to deep orange at the top
  const [c, g] = canvas2d(64, 128), grd = g.createLinearGradient(0, 128, 0, 0);
  grd.addColorStop(0, '#fff2b0'); grd.addColorStop(0.35, '#ffb040'); grd.addColorStop(0.8, '#d0480e'); grd.addColorStop(1, '#5a1404');
  g.fillStyle = grd; g.fillRect(0, 0, 64, 128); return tex(c);
}
function paintLava(N) {
  const [c, g] = canvas2d(N), im = g.createImageData(N, N), d = im.data;
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
    const u = x / N * 8, v = y / N * 8, n = fbm(u, v), cr = Math.abs(fbm(u * 1.7 + 3, v * 1.7) - 0.5), o = (y * N + x) * 4;
    const hot = 0.35 + 0.35 * smooth(0.3, 0.8, n) + 0.3 * smooth(0.05, 0.0, cr);
    d[o] = 100 + 155 * hot; d[o + 1] = 30 + 150 * hot * hot; d[o + 2] = 8 + 60 * hot * hot * hot; d[o + 3] = 255;
  }
  g.putImageData(im, 0, 0); const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
function paintFall() {
  const [c, g] = canvas2d(64, 256);
  for (let x = 0; x < 64; x++) { const e = Math.min(x, 63 - x) / 32, k = 0.6 + 0.4 * Math.sin(x * 0.7) * Math.sin(x * 0.23 + 1); g.fillStyle = `rgba(255,${150 + 80 * k | 0},${50 + 70 * k | 0},${Math.min(1, e * 3)})`; g.fillRect(x, 0, 1, 256); }
  for (let i = 0; i < 70; i++) { g.fillStyle = `rgba(150,40,0,${0.2 + rnd() * 0.3})`; g.fillRect(4 + rnd() * 56, rnd() * 256, 1 + rnd() * 3, 10 + rnd() * 40); }
  for (let i = 0; i < 30; i++) { g.fillStyle = `rgba(255,250,210,${0.3 + rnd() * 0.4})`; g.fillRect(8 + rnd() * 48, rnd() * 256, 1 + rnd() * 2, 20 + rnd() * 50); }
  const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
function softDot(inner, outer) {
  const [c, g] = canvas2d(64), grd = g.createRadialGradient(32, 32, 0, 32, 32, 32); grd.addColorStop(0, inner); grd.addColorStop(1, outer);
  g.fillStyle = grd; g.fillRect(0, 0, 64, 64); return tex(c);
}

// ---- materials (textured ones cached by repeat) ----
const masonT = masonryTexture(), plateT = plateTexture(), furnT = furnaceTexture(), matCache = {};
const texM = (base, rx, ry, o = {}) => {
  const key = `${base.uuid}${rx.toFixed(1)}x${ry.toFixed(1)}`;
  if (!matCache[key]) { const t = base.clone(); t.needsUpdate = true; t.repeat.set(rx, ry); matCache[key] = new THREE.MeshStandardMaterial({ map: t, roughness: 0.82, metalness: 0.25, ...o }); }
  return matCache[key];
};
const stoneM = (rx = 1, ry = 1) => texM(masonT, rx, ry);
const plateM = (rx = 1, ry = 1) => texM(plateT, rx, ry, { metalness: 0.45, roughness: 0.7 });
const darkM = new THREE.MeshStandardMaterial({ color: 0x2a1f1a, roughness: 0.85, metalness: 0.3 });
const ironM = new THREE.MeshStandardMaterial({ color: 0x3a2c25, roughness: 0.55, metalness: 0.65 });
const brassM = new THREE.MeshStandardMaterial({ color: 0xc89048, roughness: 0.34, metalness: 0.9, emissive: 0x4a2008, emissiveIntensity: 0.3 });
const darkBrassM = new THREE.MeshStandardMaterial({ color: 0x8a5a2c, roughness: 0.45, metalness: 0.85, emissive: 0x2a1004, emissiveIntensity: 0.3 });
const roofM = new THREE.MeshStandardMaterial({ color: 0x3a2620, roughness: 0.7, metalness: 0.4, flatShading: true });
const glowM = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffa040).multiplyScalar(1.7), toneMapped: false });
const furnM = new THREE.MeshBasicMaterial({ map: furnT, color: new THREE.Color(1.6, 1.4, 1.3), toneMapped: false, side: THREE.DoubleSide });
const emberM = new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff8a2c).multiplyScalar(1.3), toneMapped: false });
function add(geo, mat, parent, pos, rot, shadow = true) { const m = new THREE.Mesh(geo, mat); m.castShadow = m.receiveShadow = shadow; if (pos) m.position.set(...pos); if (rot) m.rotation.set(...rot); parent.add(m); return m; }
const box = (w, h, d) => new THREE.BoxGeometry(w, h, d);
// shapes: an arch (rectangle under a half circle), an arch frame, an annular sector
function archShape(w, h) { const s = new THREE.Shape(), r = w / 2; s.moveTo(-r, 0); s.lineTo(-r, h - r); s.absarc(0, h - r, r, Math.PI, 0, true); s.lineTo(r, 0); s.lineTo(-r, 0); return s; }
function archFrame(w, h, t, depth) { const s = archShape(w + 2 * t, h + t); const hole = new THREE.Path(), r = w / 2; hole.moveTo(-r, 0); hole.lineTo(r, 0); hole.lineTo(r, h - r); hole.absarc(0, h - r, r, 0, Math.PI, false); hole.lineTo(-r, 0); s.holes.push(hole); return new THREE.ExtrudeGeometry(s, { depth, bevelEnabled: false }); }
function sectorGeo(r0, r1, a0, a1, h, seg = 6) { const s = new THREE.Shape(); s.absarc(0, 0, r1, a0, a1, false); s.absarc(0, 0, r0, a1, a0, true); const g = new THREE.ExtrudeGeometry(s, { depth: h, bevelEnabled: false, curveSegments: seg }); g.rotateX(Math.PI / 2); g.translate(0, h, 0); return g; }
function ringGeo(r0, r1, h, seg = 96) { const s = new THREE.Shape(); s.absarc(0, 0, r1, 0, TAU, false); const p = new THREE.Path(); p.absarc(0, 0, r0, 0, TAU, true); s.holes.push(p); const g = new THREE.ExtrudeGeometry(s, { depth: h, bevelEnabled: false, curveSegments: seg }); g.rotateX(Math.PI / 2); g.translate(0, h, 0); return g; }
const sector = sectorGeo; // lies in the x-z plane, angle measured like atan2(z, x)

// root holds everything solid (later exported to Unity as LB_ENV_BrassFoundry_Arena); sky, haze, embers and lights stay on the scene.
const root = new THREE.Group(); root.name = 'LB_ENV_BrassFoundry_Arena'; scene.add(root);
const arena = new THREE.Group(); arena.name = 'Arena'; root.add(arena);
const far = new THREE.Group(); far.name = 'Surroundings'; root.add(far);
const anim = { falls: [], lanterns: [], chimneys: [] };

// ---- sky, fog and the glowing abyss ----
{
  const [c, g] = canvas2d(16, 512), grd = g.createLinearGradient(0, 0, 0, 512);
  grd.addColorStop(0, '#160805'); grd.addColorStop(0.3, '#3a1408'); grd.addColorStop(0.46, '#8a3412'); grd.addColorStop(0.52, '#c0541a'); grd.addColorStop(0.7, '#e8742a'); grd.addColorStop(1, '#ff9a40');
  g.fillStyle = grd; g.fillRect(0, 0, 16, 512);
  const sky = new THREE.Mesh(new THREE.SphereGeometry(450, 32, 16), new THREE.MeshBasicMaterial({ map: tex(c), side: THREE.BackSide, fog: false, depthWrite: false }));
  sky.name = 'Sky'; scene.add(sky);
  scene.fog = new THREE.Fog(0x8e3414, 40, 210);
  // stacked haze sheets under the platform: the abyss glows up through smoke
  const hz = softDot('rgba(255,150,60,0.9)', 'rgba(255,90,20,0)');
  for (const [y, s, o] of [[-14, 120, 0.1], [-28, 220, 0.14], [-44, 320, 0.18], [-60, 420, 0.2]]) {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(s, s).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: hz, transparent: true, opacity: o, blending: THREE.AdditiveBlending, depthWrite: false, fog: false, toneMapped: false }));
    m.position.y = y; m.renderOrder = -1; scene.add(m);
  }
}

// ---- lights: warm key from behind-left, strong furnace under-glow, warm points over the window grids ----
scene.add(new THREE.HemisphereLight(0x7a5a5a, 0xff6a1a, 0.6));
const sun = new THREE.DirectionalLight(0xffc690, 2.0); sun.position.set(-18, 34, -6); sun.castShadow = true;
sun.shadow.mapSize.set(4096, 4096); Object.assign(sun.shadow.camera, { left: -26, right: 26, top: 26, bottom: -26, near: 1, far: 100 }); sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03;
scene.add(sun);
const fill = new THREE.DirectionalLight(0xff9a5a, 0.55); fill.position.set(14, 20, 24); scene.add(fill);
const under = new THREE.DirectionalLight(0xff5a14, 1.6); under.position.set(6, -20, -10); scene.add(under);
const back = new THREE.DirectionalLight(0xff7a30, 1.1); back.position.set(8, 10, -30); scene.add(back);
const winLights = [];
for (let s = 0; s < 8; s++) { const a = s * SPOKE + SPOKE / 2, l = new THREE.PointLight(0xff8a38, 7, 7.5, 2); l.position.set(Math.cos(a) * 7.8, 0.9, Math.sin(a) * 7.8); scene.add(l); winLights.push(l); }

// ---- the floor: painted disc, raised brass rings and spokes, the gear medallion ----
const fl = paintFloor(2048);
const floorMat = new THREE.MeshStandardMaterial({ map: tex(fl.map), bumpMap: tex(fl.bump, false), bumpScale: 3.5, emissiveMap: tex(fl.glow), emissive: 0xffffff, emissiveIntensity: 1, roughness: 0.6, metalness: 0.45 });
floorMat.name = 'FoundryFloor';
const floorGeo = new THREE.CircleGeometry(R, 160); floorGeo.rotateX(-Math.PI / 2);
{ const uv = floorGeo.attributes.uv, p = floorGeo.attributes.position; for (let i = 0; i < uv.count; i++) uv.setXY(i, (p.getX(i) + R) / (2 * R), (p.getZ(i) + R) / (2 * R)); }
floorMat.map.flipY = floorMat.bumpMap.flipY = floorMat.emissiveMap.flipY = false;
const floor = add(floorGeo, floorMat, arena); floor.name = 'Floor'; floor.castShadow = false;
for (const [r0, r1] of BAND) add(ringGeo(r0, r1, 0.05), brassM, arena, [0, 0, 0]);
const studs = [];
for (let s = 0; s < 8; s++) { // spokes: raised brass beams with a darker centre strip and studs
  const a = s * SPOKE, g = new THREE.Group(); g.rotation.y = -a; arena.add(g);
  add(box(10.6 - 4.95, 0.06, 0.8), brassM, g, [(4.95 + 10.6) / 2, 0.03, 0]);
  add(box(10.6 - 5.3, 0.075, 0.3), darkBrassM, g, [(4.95 + 10.6) / 2, 0.04, 0]);
  for (let i = 0; i < 9; i++) for (const sz of [-0.29, 0.29]) studs.push([Math.cos(a) * (5.3 + i * 0.62), Math.sin(a) * (5.3 + i * 0.62), sz, a]);
  add(new THREE.CylinderGeometry(0.34, 0.4, 0.09, 8), brassM, g, [10.78, 0.045, 0]); // bosses where the spokes meet the rings
  add(new THREE.CylinderGeometry(0.28, 0.34, 0.09, 8), brassM, g, [4.78, 0.045, 0]);
}
{ const sg = new THREE.SphereGeometry(0.055, 6, 4, 0, TAU, 0, Math.PI / 2), im = new THREE.InstancedMesh(sg, brassM, studs.length), m4 = new THREE.Matrix4();
  studs.forEach(([x, z, sz, a], i) => { m4.makeTranslation(x - Math.sin(a) * sz, 0.06, z + Math.cos(a) * sz); im.setMatrixAt(i, m4); }); arena.add(im); }
{ // gear medallion: a toothed brass ring around the painted sun, with a glass core
  const s = new THREE.Shape(), T = 28;
  for (let i = 0; i <= T * 4; i++) { const a = i / (T * 4) * TAU, rr = (i % 4 < 2) ? 1.62 : 1.48; i ? s.lineTo(Math.cos(a) * rr, Math.sin(a) * rr) : s.moveTo(Math.cos(a) * rr, Math.sin(a) * rr); }
  const hole = new THREE.Path(); hole.absarc(0, 0, 1.3, 0, TAU, true); s.holes.push(hole);
  const gg = new THREE.ExtrudeGeometry(s, { depth: 0.07, bevelEnabled: true, bevelThickness: 0.015, bevelSize: 0.015, bevelSegments: 1 }); gg.rotateX(Math.PI / 2); gg.translate(0, 0.07, 0);
  add(gg, darkBrassM, arena);
  add(ringGeo(0.5, 0.6, 0.05, 48), brassM, arena);
  const core = add(new THREE.CylinderGeometry(0.5, 0.5, 0.03, 32), new THREE.MeshBasicMaterial({ color: new THREE.Color(0xffb050).multiplyScalar(1.3), transparent: true, opacity: 0.55, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false }), arena, [0, 0.02, 0], null, false);
  anim.core = core;
}
// heat haze over each window grid (soft additive, breathes)
const heatMats = [];
for (let s = 0; s < 8; s++) {
  const c = s * SPOKE + SPOKE / 2, m = new THREE.MeshBasicMaterial({ color: 0xff7a24, transparent: true, opacity: 0.06, blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false });
  add(sector(WIN[0], WIN[1], c - 0.28, c + 0.28, 0.005, 12), m, arena, [0, 0.03, 0], null, false); heatMats.push(m);
}

// ---- rim: chunky masonry blocks with brass trim, merlons and small machinery, broken by four gates ----
const gateHalfIn = Math.asin(GATE_W / 2 / R), RIM_H = 0.6;
{
  const ped = [], merl = [];
  for (let q = 0; q < 4; q++) {
    const a0 = q * Math.PI / 2 + gateHalfIn, a1 = (q + 1) * Math.PI / 2 - gateHalfIn, n = 14;
    for (let i = 0; i < n; i++) {
      const b0 = a0 + i / n * (a1 - a0) + 0.006, b1 = a0 + (i + 1) / n * (a1 - a0) - 0.006, h = RIM_H + (hash(q, i) - 0.5) * 0.08;
      add(sector(R, RIM - 0.25, b0, b1, h, 4), stoneM(0.6, 0.15), arena);
      add(sector(R - 0.02, R + 0.22, b0, b1, 0.08, 4), brassM, arena, [0, h - 0.04, 0]);
      add(sector(RIM - 0.4, RIM, b0, b1, h + 0.22, 4), stoneM(0.5, 0.2), arena);                       // outer lip, a step higher
      const mid = (b0 + b1) / 2;
      if (i % 2 === 0) merl.push([mid, h + 0.22]);
      else ped.push([mid, h]);
    }
  }
  for (const [a, y] of merl) { // merlons with a brass cap and a tiny spike
    const x = Math.cos(a) * (RIM - 0.3), z = Math.sin(a) * (RIM - 0.3), g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = -a; arena.add(g);
    add(box(0.55, 0.65, 0.9), stoneM(0.2, 0.2), g, [0, 0.32, 0]); add(box(0.65, 0.08, 1.0), darkBrassM, g, [0, 0.68, 0]);
    add(new THREE.ConeGeometry(0.14, 0.4, 4), ironM, g, [0, 0.92, 0]);
  }
  ped.forEach(([a, y], i) => { // machinery bits on the in-between blocks: gear housings, valves, small braziers
    const x = Math.cos(a) * (R + 1.0), z = Math.sin(a) * (R + 1.0), g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = -a; arena.add(g);
    if (i % 3 === 0) { add(new THREE.CylinderGeometry(0.42, 0.42, 0.18, 12).rotateZ(Math.PI / 2), darkBrassM, g, [0, 0.42, 0]); add(box(0.5, 0.3, 0.7), ironM, g, [0, 0.15, 0]); add(new THREE.CylinderGeometry(0.12, 0.12, 0.3, 8).rotateZ(Math.PI / 2), brassM, g, [0, 0.42, 0]); }
    else if (i % 3 === 1) { add(new THREE.CylinderGeometry(0.34, 0.24, 0.32, 8), ironM, g, [0, 0.16, 0]); add(new THREE.SphereGeometry(0.26, 10, 6, 0, TAU, 0, Math.PI / 2), emberM, g, [0, 0.3, 0], null, false); }
    else { add(box(0.4, 0.5, 0.4), ironM, g, [0, 0.25, 0]); add(new THREE.TorusGeometry(0.18, 0.04, 6, 12), brassM, g, [0, 0.62, 0], [Math.PI / 2, 0, 0]); add(new THREE.CylinderGeometry(0.04, 0.04, 0.2, 6), brassM, g, [0, 0.55, 0]); }
  });
  for (const [a] of ped.filter((_, i) => i % 3 === 1).filter((_, i) => i % 2 === 0).slice(0, 4)) { const l = new THREE.PointLight(0xff9a40, 5, 6, 2); l.position.set(Math.cos(a) * (R + 1), 1.4, Math.sin(a) * (R + 1)); scene.add(l); }
}

// ---- gate arms (N, E, S, W): portcullis gateway, an arm platform with furnace houses, then a truss bridge ----
const ARM_END = 20, BRIDGE_END = 31;
function cageLantern(parent, pos, s = 1) { // iron cage with an arched glowing window on each side, pyramid cap and spike
  const g = new THREE.Group(); g.position.set(...pos); g.scale.setScalar(s); parent.add(g);
  const panel = (() => { const sh = new THREE.Shape(); sh.moveTo(-0.6, -0.8); sh.lineTo(0.6, -0.8); sh.lineTo(0.6, 0.8); sh.lineTo(-0.6, 0.8); sh.lineTo(-0.6, -0.8);
    const hole = new THREE.Path(); hole.moveTo(-0.28, -0.5); hole.lineTo(0.28, -0.5); hole.lineTo(0.28, 0.22); hole.absarc(0, 0.22, 0.28, 0, Math.PI, false); hole.lineTo(-0.28, -0.5); sh.holes.push(hole);
    return new THREE.ExtrudeGeometry(sh, { depth: 0.1, bevelEnabled: false }); })();
  for (let i = 0; i < 4; i++) { const p = add(panel, plateM(0.3, 0.3), g, null, null, false); p.rotation.y = i * Math.PI / 2; p.position.set(Math.sin(i * Math.PI / 2) * 0.6, 0, Math.cos(i * Math.PI / 2) * 0.6); p.translateZ(-0.05); }
  add(box(1.05, 1.5, 1.05), furnM, g, null, null, false);
  for (const y of [-0.85, 0.85]) add(box(1.45, 0.14, 1.45), darkBrassM, g, [0, y, 0], null, false);
  add(new THREE.ConeGeometry(1.0, 0.7, 4).rotateY(Math.PI / 4), roofM, g, [0, 1.27, 0], null, false);
  add(new THREE.ConeGeometry(0.5, 0.6, 4).rotateY(Math.PI / 4).rotateX(Math.PI), roofM, g, [0, -1.2, 0], null, false);
  add(new THREE.TorusGeometry(0.2, 0.06, 6, 12), ironM, g, [0, 1.75, 0], null, false);
  anim.lanterns.push(g); return g;
}
const linkGeo = new THREE.TorusGeometry(0.3, 0.085, 6, 12); linkGeo.scale(1, 1.5, 1);
const chainLinks = [];
function chain(A, B, sag = 0) { // links from A to B (arrays), optional sag
  const a = new THREE.Vector3(...A), b = new THREE.Vector3(...B), len = a.distanceTo(b), n = Math.ceil(len / 0.72);
  for (let i = 0; i < n; i++) { const t = (i + 0.5) / n, p = a.clone().lerp(b, t); p.y -= Math.sin(t * Math.PI) * sag; chainLinks.push([p, b.clone().sub(a).normalize(), i % 2]); }
}
for (let q = 0; q < 4; q++) {
  const a = q * Math.PI / 2, g = new THREE.Group(); g.rotation.y = -a; arena.add(g); // local +x points out of the arena
  // portcullis gateway in the rim gap
  for (const s of [-1, 1]) {
    const pz = s * (GATE_W / 2 + 0.5);
    add(box(1.6, 3.4, 1.0), stoneM(0.5, 1), g, [R + 0.9, 1.7, pz]);
    add(box(1.8, 0.22, 1.2), darkBrassM, g, [R + 0.9, 3.5, pz]);
    add(box(1.2, 0.9, 0.8), stoneM(0.4, 0.3), g, [R + 0.9, 4.05, pz]);
    add(new THREE.ConeGeometry(0.62, 1.4, 4).rotateY(Math.PI / 4), roofM, g, [R + 0.9, 5.2, pz]);
    add(new THREE.CylinderGeometry(0.05, 0.05, 0.6, 6), brassM, g, [R + 0.9, 6.1, pz]);
    add(box(0.1, 1.0, 0.3), glowM, g, [R + 0.1, 2.1, pz], null, false);
  }
  add(box(1.2, 0.8, GATE_W + 2), stoneM(1, 0.3), g, [R + 0.9, 3.1, 0]);
  add(box(1.3, 0.12, GATE_W + 2.1), brassM, g, [R + 0.9, 3.55, 0]);
  const emb = new THREE.Group(); emb.position.set(R + 0.25, 3.1, 0); emb.rotation.y = -Math.PI / 2; g.add(emb);
  add(new THREE.CylinderGeometry(0.55, 0.55, 0.12, 16).rotateX(Math.PI / 2), brassM, emb); add(new THREE.TorusGeometry(0.62, 0.06, 6, 20), darkBrassM, emb);
  for (let i = 0; i <= 9; i++) add(box(0.1, 2.7, 0.1), darkBrassM, g, [R + 1.1, 1.35, -GATE_W / 2 + i * GATE_W / 9]);
  for (let j = 0; j < 4; j++) add(box(0.08, 0.1, GATE_W), darkBrassM, g, [R + 1.1, 0.35 + j * 0.75, 0]);
  for (let i = 0; i <= 9; i++) add(new THREE.ConeGeometry(0.07, 0.2, 4), darkBrassM, g, [R + 1.1, 0.0, -GATE_W / 2 + i * GATE_W / 9], [Math.PI, 0, 0]);
  // arm platform out to ARM_END with two furnace houses flanking the lane
  const aw = GATE_W + 3.4, ax = (R - 0.1 + ARM_END) / 2, al = ARM_END - R + 0.1, hx = (RIM + ARM_END) / 2 + 0.6;
  add(box(al, 1.2, aw), stoneM(al / 3, 0.4), g, [ax, -0.63, 0]);
  add(box(al, 0.1, GATE_W), plateM(al / 3, 1), g, [ax, 0.03, 0]);
  add(box(al + 0.2, 0.18, aw + 0.2), darkBrassM, g, [ax, -1.1, 0]);
  for (const s of [-1, 1]) {
    const hz = s * (GATE_W / 2 + 0.85), hs = new THREE.Group(); hs.position.set(hx, 0, hz); g.add(hs);
    add(box(3.2, 2.2, 1.6), stoneM(1, 0.7), hs, [0, 1.1, 0]);
    add(box(3.4, 0.16, 1.8), darkBrassM, hs, [0, 2.25, 0]);
    add(new THREE.CylinderGeometry(0.3, 0.38, 2.2, 8), ironM, hs, [0.8, 3.3, 0]);
    add(new THREE.CylinderGeometry(0.26, 0.26, 0.05, 8), emberM, hs, [0.8, 4.42, 0], null, false);
    for (const wx of [-0.9, 0.1]) { const w = add(new THREE.ShapeGeometry(archShape(0.5, 1.0)), furnM, hs, [wx, 0.6, -s * 0.81], [0, s > 0 ? Math.PI : 0, 0], false); }
    add(box(0.9, 0.5, 0.9), ironM, hs, [-0.8, 2.55, 0]); add(new THREE.CylinderGeometry(0.3, 0.3, 0.12, 12).rotateZ(Math.PI / 2), brassM, hs, [-0.8, 2.6, s * -0.5]);
    anim.chimneys.push(hs);
  }
  for (let i = 0; i < 5; i++) add(box(0.5, 0.6, aw + 0.3), darkM, g, [RIM + 0.6 + i * 1.0, -1.45, 0]); // corbels under the arm
  // truss bridge to the pier tower
  const bl = BRIDGE_END - ARM_END, bx = (ARM_END + BRIDGE_END) / 2, bw = GATE_W - 0.4;
  add(box(bl, 0.3, bw), plateM(bl / 2.5, 1), g, [bx, -0.15, 0]);
  for (const s of [-1, 1]) {
    const sz = s * (bw / 2 + 0.05);
    add(box(bl, 0.22, 0.22), ironM, g, [bx, -0.35, sz]); add(box(bl, 0.18, 0.18), ironM, g, [bx, -1.9, sz]);
    for (let i = 0; i < 8; i++) { const x0 = ARM_END + i * bl / 8, d = add(box(Math.hypot(bl / 8, 1.55), 0.12, 0.12), ironM, g, [x0 + bl / 16, -1.12, sz]); d.rotation.z = (i % 2 ? 1 : -1) * Math.atan2(1.55, bl / 8); }
    for (let i = 0; i <= 8; i++) add(box(0.14, 1.75, 0.14), ironM, g, [ARM_END + i * bl / 8, -1.1, sz]);
    for (let i = 0; i <= 8; i++) add(box(0.12, 1.0, 0.12), ironM, g, [ARM_END + i * bl / 8, 0.5, sz]);
    add(box(bl, 0.1, 0.14), darkBrassM, g, [bx, 1.0, sz]); add(box(bl, 0.06, 0.08), ironM, g, [bx, 0.55, sz]);
    for (const lx of [ARM_END + bl * 0.3, ARM_END + bl * 0.75]) { add(box(0.12, 1.8, 0.12), ironM, g, [lx, 0.9, sz]); cageLantern(g, [lx, 2.0, sz], 0.28); }
  }
  // the bridge deck hangs from two chains on each side, rising to the pier tower
  const wp = new THREE.Vector3(), up = new THREE.Vector3();
  g.updateMatrixWorld(true);
  for (const s of [-1, 1]) for (const t of [0.35, 0.7]) {
    g.localToWorld(wp.set(ARM_END + bl * t, 1.0, s * (bw / 2 + 0.05))); g.localToWorld(up.set(BRIDGE_END + 1.5, 13, s * (bw / 2 + 0.3)));
    chain(wp.toArray(), up.toArray(), 0.4);
  }
}

// ---- furnace drum under the rim: cornice and corbels, arched furnace mouths between pilasters, stepped base, legs ----
{
  add(new THREE.CylinderGeometry(RIM + 0.25, RIM + 0.25, 0.5, 96, 1, true), darkBrassM, arena, [0, -0.3, 0]);
  add(new THREE.CylinderGeometry(RIM, RIM, 11, 96, 1, true), stoneM(24, 4), arena, [0, -6, 0]);
  const cg = box(0.5, 0.7, 0.55), cim = new THREE.InstancedMesh(cg, darkM, 120), m4 = new THREE.Matrix4(), q = new THREE.Quaternion();
  for (let i = 0; i < 120; i++) { const a = i / 120 * TAU; q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), -a); m4.compose(new THREE.Vector3(Math.cos(a) * (RIM + 0.2), -0.85, Math.sin(a) * (RIM + 0.2)), q, new THREE.Vector3(1, 1, 1)); cim.setMatrixAt(i, m4); }
  arena.add(cim);
  const AR = 20, frame = archFrame(1.5, 3.0, 0.28, 0.35), mouth = new THREE.ShapeGeometry(archShape(1.5, 3.0));
  for (let i = 0; i < AR; i++) {
    const a = (i + 0.5) / AR * TAU, g = new THREE.Group(); g.position.set(Math.cos(a) * RIM, 0, Math.sin(a) * RIM); g.rotation.y = -a + Math.PI / 2; arena.add(g);
    const y0 = -5.2;
    add(frame, darkBrassM, g, [0, y0, 0.0], null, false);
    add(mouth, i % 3 === 1 ? furnM : darkM, g, [0, y0, 0.03], null, false);
    if (i % 3 !== 1) for (let b = 0; b < 4; b++) add(box(0.06, 2.6, 0.06), ironM, g, [-0.5 + b * 0.33, y0 + 1.3, 0.15], null, false);
    add(box(0.5, 0.25, 0.4), darkBrassM, g, [0, y0 + 3.45, 0.15], null, false); // keystone
    const pa = (i / AR) * TAU, pg = new THREE.Group(); pg.position.set(Math.cos(pa) * (RIM + 0.15), 0, Math.sin(pa) * (RIM + 0.15)); pg.rotation.y = -pa + Math.PI / 2; arena.add(pg);
    add(box(0.9, 10, 0.6), stoneM(0.3, 3), pg, [0, -6, 0], null, false);
    add(box(1.1, 0.3, 0.8), darkBrassM, pg, [0, -1.3, 0], null, false); add(box(1.1, 0.3, 0.8), darkBrassM, pg, [0, -8, 0], null, false);
    if (i % 2 === 0) add(box(0.25, 0.9, 0.1), glowM, g, [0, -2.2, 0.08], null, false);
  }
  add(new THREE.CylinderGeometry(RIM + 0.5, RIM + 0.5, 0.6, 96), darkBrassM, arena, [0, -11.4, 0]);
  add(new THREE.CylinderGeometry(RIM + 0.4, RIM - 2.5, 3, 96, 1, true), stoneM(20, 1), arena, [0, -13.2, 0]);
  add(new THREE.CylinderGeometry(RIM - 2.5, 5, 14, 64, 1, true), darkM, arena, [0, -21.7, 0]);
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU + 0.2; add(box(1.6, 50, 1.6), stoneM(0.4, 10), arena, [Math.cos(a) * 9.5, -38, Math.sin(a) * 9.5], null, false); }
}

// ---- molten sea far below, and falls ----
const lavaT = paintLava(512); lavaT.repeat.set(10, 10);
const lava = new THREE.Mesh(new THREE.PlaneGeometry(900, 900).rotateX(-Math.PI / 2), new THREE.MeshBasicMaterial({ map: lavaT, color: new THREE.Color(1.2, 1.0, 0.9), toneMapped: false }));
lava.position.y = -70; lava.name = 'MoltenSea'; far.add(lava);
const fallT = paintFall();
function fall(x, z, top, bottom, w, face, glowSz = 0) {
  const t = fallT.clone(); t.needsUpdate = true; t.repeat.set(1, (top - bottom) / 10);
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, top - bottom), new THREE.MeshBasicMaterial({ map: t, color: new THREE.Color(1.7, 1.35, 1.1), toneMapped: false, transparent: true, side: THREE.DoubleSide, depthWrite: false }));
  m.position.set(x, (top + bottom) / 2, z); m.rotation.y = face; far.add(m); anim.falls.push({ t, speed: 0.7 + rnd() * 0.4 });
  if (glowSz) { const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: softDot('rgba(255,170,70,0.8)', 'rgba(255,90,20,0)'), blending: THREE.AdditiveBlending, depthWrite: false, toneMapped: false })); sp.position.set(x, top, z); sp.scale.setScalar(glowSz); far.add(sp); }
  return m;
}
// a crucible spout pours out of the drum toward the hero view, another on the far side
for (const a of [Math.PI * 0.62, Math.PI * 1.45]) {
  const g = new THREE.Group(); g.position.set(Math.cos(a) * RIM, -3.0, Math.sin(a) * RIM); g.rotation.y = -a + Math.PI / 2; arena.add(g);
  add(box(1.8, 0.5, 2.0), darkBrassM, g, [0, 0, 0.9]); add(box(0.25, 0.6, 2.0), darkBrassM, g, [-0.85, 0.3, 0.9]); add(box(0.25, 0.6, 2.0), darkBrassM, g, [0.85, 0.3, 0.9]);
  add(new THREE.ShapeGeometry(archShape(1.6, 2.4)), furnM, g, [0, -0.2, 0.02], null, false);
  fall(Math.cos(a) * (RIM + 1.9), Math.sin(a) * (RIM + 1.9), -2.8, -70, 1.3, -a + Math.PI / 2, 4);
}

// ---- gothic foundry towers: masonry shafts with cornices and merlons, arched glowing windows, buttresses,
//      a lantern stage and a spire; optional furnace mouth pouring a fall, crane arm with a hanging lantern ----
const windowGeo = new THREE.ShapeGeometry(archShape(0.7, 1.7)), windowGeoTall = new THREE.ShapeGeometry(archShape(0.9, 2.6));
function tower(x, z, top, w, seed, o = {}) {
  const r = mulberry32(seed), g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = o.face !== undefined ? o.face : r() * TAU; far.add(g);
  const base = -75, sh = o.shadow || false;
  let y = base, ww = w;
  const tiers = []; for (let t = top - 7; t > base + 10; t -= 9 + r() * 5) tiers.unshift(t);
  for (const t of tiers) { // one shaft section per tier, narrowing a little, cornice and merlons on top
    const h = t - y; add(box(ww, h, ww), stoneM(ww / 3, h / 3), g, [0, y + h / 2, 0], null, sh);
    add(box(ww + 0.8, 0.5, ww + 0.8), darkBrassM, g, [0, t + 0.25, 0], null, false);
    add(box(ww + 0.5, 0.35, ww + 0.5), darkM, g, [0, t - 0.25, 0], null, false);
    for (let i = 0; i < 4; i++) for (let j = -1; j <= 1; j++) { const ang = i * Math.PI / 2, lx = j * ww * 0.36, lz = ww / 2 + 0.2; add(box(0.5, 0.6, 0.35), stoneM(0.2, 0.2), g, [Math.cos(ang) * lx + Math.sin(ang) * lz, t + 0.8, -Math.sin(ang) * lx + Math.cos(ang) * lz], [0, ang, 0], false); }
    for (let i = 0; i < 4; i++) { // arched windows on each face, some glowing
      const ang = i * Math.PI / 2; if (t - y < 4) continue;
      for (let j = -1; j <= 1; j += 2) { if (r() < 0.35) continue; const wy = t - 2.4 - r() * Math.max(0, h - 5), lx = j * ww * 0.22;
        add(windowGeo, r() < 0.7 ? furnM : darkM, g, [Math.cos(ang) * lx + Math.sin(ang) * (ww / 2 + 0.02), wy, -Math.sin(ang) * lx + Math.cos(ang) * (ww / 2 + 0.02)], [0, ang, 0], false); }
    }
    for (const [sx, sz] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) add(box(0.9, h, 0.9), stoneM(0.2, h / 3), g, [sx * ww / 2, y + h / 2, sz * ww / 2], null, false); // corner buttresses
    if (r() < 0.6) { // balcony: a projecting ledge on a corbel band, with a railing
      const by = t - 3.5, bw = ww + 2.2; add(box(bw, 0.3, bw), darkBrassM, g, [0, by, 0], null, false); add(box(bw - 0.4, 0.6, bw - 0.4), darkM, g, [0, by - 0.45, 0], null, false);
      for (let i = 0; i < 4; i++) { const ang = i * Math.PI / 2;
        for (let j = 0; j <= 6; j++) { const lx = -bw / 2 + j * bw / 6; add(box(0.1, 0.8, 0.1), ironM, g, [Math.cos(ang) * lx + Math.sin(ang) * bw / 2, by + 0.55, -Math.sin(ang) * lx + Math.cos(ang) * bw / 2], null, false); }
        add(box(bw, 0.08, 0.1), ironM, g, [Math.sin(ang) * bw / 2, by + 0.95, Math.cos(ang) * bw / 2], [0, ang, 0], false); }
    }
    y = t + 0.5; ww *= 0.9;
  }
  // lantern stage, pinnacles and spire
  const lw = ww * 0.72, lh = 4.5;
  add(box(lw, lh, lw), stoneM(lw / 3, 1.5), g, [0, y + lh / 2, 0], null, false);
  for (let i = 0; i < 4; i++) { const ang = i * Math.PI / 2; add(windowGeoTall, furnM, g, [Math.sin(ang) * (lw / 2 + 0.02), y + 0.8, Math.cos(ang) * (lw / 2 + 0.02)], [0, ang, 0], false); }
  for (const [sx, sz] of [[1, 1], [1, -1], [-1, 1], [-1, -1]]) { add(box(0.5, lh + 1, 0.5), stoneM(0.2, 1), g, [sx * lw / 2, y + (lh + 1) / 2, sz * lw / 2], null, false); add(new THREE.ConeGeometry(0.35, 1.6, 4).rotateY(Math.PI / 4), roofM, g, [sx * lw / 2, y + lh + 1.8, sz * lw / 2], null, false); }
  add(box(lw + 0.6, 0.35, lw + 0.6), darkBrassM, g, [0, y + lh + 0.2, 0], null, false);
  const sp = o.spire ?? (lw * 1.6 + r() * 3);
  add(new THREE.ConeGeometry(lw * 0.68, sp, 8), roofM, g, [0, y + lh + 0.4 + sp / 2, 0], null, false);
  add(new THREE.CylinderGeometry(0.06, 0.06, 2, 6), brassM, g, [0, y + lh + 0.4 + sp + 0.8, 0], null, false);
  add(new THREE.SphereGeometry(0.18, 8, 6), brassM, g, [0, y + lh + 0.4 + sp + 0.2, 0], null, false);
  if (o.chimney) { const ch = add(new THREE.CylinderGeometry(0.55, 0.75, 7, 10), ironM, g, [ww * 0.3, y + 3.5, -ww * 0.3], null, false); add(new THREE.CylinderGeometry(0.5, 0.5, 0.08, 10), emberM, g, [ww * 0.3, y + 7.05, -ww * 0.3], null, false); g.updateMatrixWorld(true); anim.chimneys.push({ pos: ch.localToWorld(new THREE.Vector3(0, 3.6, 0)) }); }
  g.updateMatrixWorld(true);
  if (o.fall) { // furnace mouth facing the arena at fall height, pouring down into the abyss
    const fy = o.fall, fz = ww / 2 + 0.05, gg = new THREE.Group(); gg.position.set(0, fy, 0); g.add(gg);
    add(archFrame(2.2, 3.2, 0.4, 0.5), darkBrassM, gg, [0, 0, fz], null, false); add(new THREE.ShapeGeometry(archShape(2.2, 3.2)), furnM, gg, [0, 0, fz - 0.05], null, false);
    add(box(2.0, 0.4, 1.6), darkBrassM, gg, [0, 0, fz + 0.8], null, false);
    const p = g.localToWorld(new THREE.Vector3(0, fy, fz + 1.5)); fall(p.x, p.z, fy + 0.1, -70, 1.9, g.rotation.y, 6);
  }
  if (o.crane) { // gibbet arm toward the arena with a hanging cage lantern
    const len = o.crane, cy = y - 0.5, arm = new THREE.Group(); arm.position.set(0, cy, 0); g.add(arm);
    add(box(0.6, 0.6, len), ironM, arm, [0, 0, ww / 2 + len / 2 - 0.5], null, false);
    const brace = add(box(0.35, 0.35, Math.hypot(len * 0.6, 3)), ironM, arm, [0, -1.5, ww / 2 + len * 0.3 - 0.5], null, false); brace.rotation.x = Math.atan2(3, len * 0.6);
    add(box(0.9, 0.9, 0.9), darkBrassM, arm, [0, 0, ww / 2 + len - 0.6], null, false);
    g.updateMatrixWorld(true);
    const tip = g.localToWorld(new THREE.Vector3(0, cy - 0.5, ww / 2 + len - 0.6)), lanternY = o.hang ?? (tip.y - 10);
    chain(tip.toArray(), [tip.x, lanternY + 1.8, tip.z]); cageLantern(far, [tip.x, lanternY, tip.z], 1.1);
  }
  return g;
}
// close ring: tall towers behind and to the sides, low ones on the camera side (the game camera looks from +z)
const face = (x, z) => Math.atan2(-x, -z); // rotate a tower so its +z face looks at the arena centre
const T = [
  // [x, z, top, width, options]
  [-24, -12, 14, 8, { fall: 6, chimney: true }], [-9, -27, 16, 8.5, { crane: 9, hang: 5.5 }], [12, -26, 18, 8.5, { fall: 8 }], [27, -9, 13, 8, { crane: 8, hang: 4, chimney: true }],
  [-31, 3, 9, 6, {}], [32, 6, 8, 6, { fall: 2 }], [-26, 22, -4, 6, {}], [27, 21, -5, 6, {}], [-14, 33, -10, 6.5, {}], [16, 34, -11, 6.5, {}],
  [0, -36, 22, 8, { chimney: true }], [-36, -26, 24, 8, {}], [37, -24, 20, 8, { chimney: true }],
];
T.forEach(([x, z, top, w, o], i) => tower(x, z, top, w, 200 + i, { ...o, face: face(x, z) }));
// pier towers at the bridge ends (the bridge chains hang from these)
for (let q = 0; q < 4; q++) { const a = q * Math.PI / 2, d = BRIDGE_END + 3.2, x = Math.cos(a) * d, z = Math.sin(a) * d; tower(x, z, q === 1 ? -2 : 12, 6, 300 + q, { face: face(x, z), spire: q === 1 ? 3 : undefined }); }
// far silhouettes: towers, spires and chimney stacks fading into the orange haze
for (let i = 0; i < 34; i++) {
  const a = i / 34 * TAU + rnd() * 0.15, d = 62 + rnd() * 90, x = Math.cos(a) * d, z = Math.sin(a) * d; if (z > 30 && Math.abs(x) < 60) continue;
  tower(x, z, 10 + rnd() * 45, 7 + rnd() * 6, 400 + i, { face: face(x, z), chimney: rnd() < 0.4 });
}
fall(-48, -70, 30, -70, 4, 0.4, 14); fall(52, -64, 26, -70, 3.5, -0.5, 12); fall(-80, -20, 20, -70, 4, 1.2, 12);
// gantries between the back towers
for (const [A, B, y] of [[[-24, -12], [-9, -27], 9], [[12, -26], [27, -9], 7], [[-9, -27], [12, -26], 12]]) {
  const ax = A[0], az = A[1], bx = B[0], bz = B[1], len = Math.hypot(bx - ax, bz - az), rot = -Math.atan2(bz - az, bx - ax), g = new THREE.Group();
  g.position.set((ax + bx) / 2, y, (az + bz) / 2); g.rotation.y = rot; far.add(g);
  add(box(len, 0.6, 2.6), plateM(len / 3, 1), g, [0, 0, 0], null, false);
  for (const s of [-1, 1]) { add(box(len, 1.4, 0.15), ironM, g, [0, -1, s * 1.3], null, false); add(box(len, 0.1, 0.1), darkBrassM, g, [0, 0.95, s * 1.3], null, false); for (let i = 0; i <= 10; i++) add(box(0.1, 0.9, 0.1), ironM, g, [-len / 2 + i * len / 10, 0.5, s * 1.3], null, false); }
  for (let i = 1; i < 10; i++) { const d = add(box(len / 10 * 1.1, 0.1, 0.1), ironM, g, [-len / 2 + (i + 0.5) * len / 10, -1, 1.3], null, false); d.rotation.z = (i % 2 ? 1 : -1) * 0.5; }
}
// chains hanging from far above with cage counterweights, kept off the camera's line to the floor
for (const [x, z, y] of [[-19.5, -5, 3.5], [20, -3, 5], [-15, -18.5, 7], [16.5, -17, 2.5], [-21, 10, 0.5], [22, 11, -1], [-5, -21, 10]]) {
  chain([x, 70, z], [x, y + 1.8, z]); cageLantern(far, [x, y, z], 1.15);
}
{ // all chain links as one instanced mesh
  const im = new THREE.InstancedMesh(linkGeo, ironM, chainLinks.length), m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), q2 = new THREE.Quaternion(), Y = new THREE.Vector3(0, 1, 0);
  chainLinks.forEach(([p, dir, odd], i) => { q.setFromUnitVectors(Y, dir); q2.setFromAxisAngle(dir, odd ? Math.PI / 2 : 0); m4.compose(p, q2.multiply(q), new THREE.Vector3(1, 1, 1)); im.setMatrixAt(i, m4); });
  im.castShadow = true; far.add(im);
}

// ---- embers rising out of the depths, smoke from chimneys (scene, not root) ----
const EMB = 900, ep = new Float32Array(EMB * 3), es = new Float32Array(EMB);
for (let i = 0; i < EMB; i++) { const a = rnd() * TAU, r = 4 + rnd() * 60; ep[i * 3] = Math.cos(a) * r; ep[i * 3 + 1] = -60 + rnd() * 90; ep[i * 3 + 2] = Math.sin(a) * r; es[i] = 1 + rnd() * 3; }
const emGeo = new THREE.BufferGeometry(); emGeo.setAttribute('position', new THREE.BufferAttribute(ep, 3));
scene.add(new THREE.Points(emGeo, new THREE.PointsMaterial({ map: softDot('rgba(255,230,160,1)', 'rgba(255,80,0,0)'), size: 0.32, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, color: 0xffb060, toneMapped: false })));
const smokeT = softDot('rgba(60,34,26,0.55)', 'rgba(60,34,26,0)'), smoke = [];
scene.updateMatrixWorld(true);
const vents = anim.chimneys.map(c => c.pos || c.localToWorld(new THREE.Vector3(0.8, 4.5, 0)));
for (let i = 0; i < vents.length * 6; i++) { const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: smokeT, transparent: true, depthWrite: false })); s.userData = { v: vents[i % vents.length], age: (i / 6 % 1) * 6 + rnd() }; scene.add(s); smoke.push(s); }

// ---- per-frame: window glow breathes, molten flows, embers and smoke rise, lanterns sway; quiet dims the floor glow ----
let dim = 1;
function update(t, dt, quiet = false) {
  dim += ((quiet ? 0.4 : 1) - dim) * Math.min(1, dt * 4);
  const breathe = 0.85 + 0.1 * Math.sin(t * 0.9) + 0.05 * Math.sin(t * 2.3);
  floorMat.emissiveIntensity = 0.85 * dim * breathe;
  for (const m of heatMats) m.opacity = 0.06 * dim * breathe;
  for (const l of winLights) l.intensity = 7 * dim * breathe;
  anim.core.material.opacity = 0.55 * dim * breathe;
  lavaT.offset.set(t * 0.004, t * 0.002);
  for (const f of anim.falls) f.t.offset.y = t * f.speed;
  for (let i = 0; i < EMB; i++) { ep[i * 3 + 1] += es[i] * dt; ep[i * 3] += Math.sin(t * 0.7 + i) * dt * 0.3; if (ep[i * 3 + 1] > 32) ep[i * 3 + 1] = -60; }
  emGeo.attributes.position.needsUpdate = true;
  for (const s of smoke) { const u = s.userData; u.age = (u.age + dt) % 6; const k = u.age / 6; s.position.set(u.v.x + k * 3, u.v.y + k * 9, u.v.z - k * 1.5); s.scale.setScalar(1.5 + k * 6); s.material.opacity = Math.sin(k * Math.PI) * 0.7; }
  anim.lanterns.forEach((g, i) => { g.rotation.y = Math.sin(t * 0.3 + i) * 0.15; });
}
return { R, root, sun, update };
};
