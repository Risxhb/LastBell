// <skyward-spire-env> Area 1 — The Skyward Spire, rebuilt from reference-topdown.webp (this folder).
// Classic script shared by skyward-spire.html (look study + Unity export). Call
//   const env = window.buildSkywardSpire(THREE, scene, { mergeGeometries, lights: true, clouds: true });
// then env.update(timeSeconds, dt, quiet) every frame (quiet = hazards are up: floor gold dims).
// Units are metres. North is -z, east is +x (the gameplay camera sits on the south side, +z, like index.html).
// env.root holds every solid thing (exported to Unity as SkywardSpire.glb); sky, clouds and lights stay on the scene.
// Static meshes are merged per material inside each top-level group; animated parts keep their own nodes:
//   BannerCloth (sways), ArmillaryRing (turns), and anything under an "Occluder" group (hidden in the gameplay view).
window.buildSkywardSpire = function buildSkywardSpire(THREE, scene, opts = {}) {
const R = 14;              // walkable plaza radius
const DISC = 9.0;          // lapis disc radius (0.62 of the balustrade radius, as in the reference)
const RAIL = 14.65;        // balustrade centre line
const LEDGE = 15.6;        // outer edge of the rim walk
const BW = 4.2;            // bridge deck width
const TAU = Math.PI * 2;

// ---- helpers ----
function mulberry32(a) { return () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const rnd = mulberry32(21);
const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
function vnoise(x, y) { const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash(xi, yi), b = hash(xi + 1, yi), c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1); return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v; }
const fbm = (x, y) => vnoise(x, y) * 0.5 + vnoise(x * 2.1, y * 2.1) * 0.27 + vnoise(x * 4.3, y * 4.3) * 0.15 + vnoise(x * 9.1, y * 9.1) * 0.08;
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
const canvas2d = (w, h = w) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')]; };
const tex = (c, srgb = true) => { const t = new THREE.CanvasTexture(c); if (srgb) t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 8; return t; };
const nearBridge = (a, half, r) => { for (let k = 0; k < 4; k++) { const d = Math.abs(((a - k * Math.PI / 2) % TAU + TAU + Math.PI) % TAU - Math.PI); if (d * r < half) return true; } return false; };

// ================================================================================================
// Floor painter: colour, height (turned into a normal map), ORM (G roughness, B metal) and emissive.
// Ivory courses of curved slabs around a lapis disc; gold inlay drawn as vector strokes on all layers.
// ================================================================================================
function paintFloor(N) {
  const S = N / (2 * R);
  const [c, g] = canvas2d(N), [hc, hg] = canvas2d(N), [oc, og] = canvas2d(N), [ec, eg] = canvas2d(N);
  const img = g.createImageData(N, N), d = img.data, himg = hg.createImageData(N, N), hd = himg.data, oimg = og.createImageData(N, N), od = oimg.data;
  // ivory courses: [inner, outer, slabs] — staggered so seams don't line up
  const COURSES = [[DISC, 10.5, 28], [10.5, 12.35, 34], [12.35, R, 40]];
  // lapis tile rings and sectors (faint)
  const LAPIS_R = [0, 1.15, 2.85, 5.5, 7.4, DISC], LAPIS_SEC = [0, 8, 12, 16, 24];
  for (let py = 0; py < N; py++) for (let px = 0; px < N; px++) {
    const x = (px + 0.5) / S - R, z = (py + 0.5) / S - R, r = Math.hypot(x, z), a = Math.atan2(z, x), o = (py * N + px) * 4;
    const n = fbm(x * 0.35 + 3, z * 0.35), fine = vnoise(x * 6, z * 6);
    let cr, cg, cb, h = 0.75, rough = 200, metal = 0;
    if (r < DISC) {                                       // lapis: deep blue, cloudy, pale veins, rare pyrite flecks
      let ring = 0; while (ring < LAPIS_R.length - 2 && r >= LAPIS_R[ring + 1]) ring++;
      const sec = LAPIS_SEC[Math.min(ring, LAPIS_SEC.length - 1)];
      let edge = Math.min(r - LAPIS_R[ring], LAPIS_R[ring + 1] - r);
      if (sec) { const sa = (a / TAU + 1) * sec + (ring % 2) * 0.5, fs = sa - Math.floor(sa); edge = Math.min(edge, Math.min(fs, 1 - fs) * r * TAU / sec); }
      const cloud = fbm(x * 0.55 + 11, z * 0.55 - 4), vein = Math.abs(Math.sin(x * 0.7 - z * 0.45 + fbm(x * 0.5, z * 0.5) * 6));
      const tid = hash(ring * 31 + Math.floor((a / TAU + 1) * (sec || 1)), 5), v = (cloud - 0.5) * 0.24 + (tid - 0.5) * 0.06 + (fine - 0.5) * 0.06;
      cr = 26 * (1 + v); cg = 38 * (1 + v); cb = 98 * (1 + v * 0.8);
      if (vein > 0.975) { cr += 10; cg += 12; cb += 16; }
      const fl = hash(px * 0.37, py * 0.53);
      if (fl > 0.9991) { cr = 214; cg = 176; cb = 96; } else if (fl > 0.9984) { cr = 196; cg = 206; cb = 236; }   // gold and pale star-speckle
      if (edge < 0.04) { const t = 1 - edge / 0.04; cr *= 1 - t * 0.1; cg *= 1 - t * 0.1; cb *= 1 - t * 0.08; h = 0.75 - t * 0.05; }   // faint tile seams
      rough = 165;                                       // honed, not polished: a glossy disc mirrors the blue sky and oversaturates
    } else {                                              // ivory slabs
      const ci = COURSES.findIndex(q => r >= q[0] && r < q[1]), q = COURSES[Math.max(0, ci)];
      const sa = (a / TAU + 1) * q[2] + ci * 0.37, si = Math.floor(sa), fs = sa - si, arc = r * TAU / q[2];
      const edge = Math.min(fs * arc, (1 - fs) * arc, r - q[0], q[1] - r), sid = hash(si * 7 + ci * 101, 3.1);
      const warm = hash(sid * 13, 1.7), v = (sid - 0.5) * 0.06 + (n - 0.5) * 0.1 + (fine - 0.5) * 0.035;
      cr = (226 + warm * 5) * (1 + v); cg = (215 + warm * 3) * (1 + v); cb = (192 - warm * 8) * (1 + v);
      if (Math.abs(Math.sin(x * 0.8 + z * 0.3 + fbm(x * 0.4, z * 0.4) * 5)) > 0.992) { cr *= 0.96; cg *= 0.955; cb *= 0.95; }
      if (edge < 0.12) { const t = 1 - smooth(0.02, 0.12, edge); cr *= 1 - t * 0.05; cg *= 1 - t * 0.055; cb *= 1 - t * 0.06; h = 0.75 - t * 0.18; }
      if (edge < 0.022) { cr = 196; cg = 182; cb = 156; h = 0.42; }
      rough = 190;
    }
    if (r > R - 0.01) { cr = 224; cg = 212; cb = 186; }
    d[o] = Math.min(255, cr); d[o + 1] = Math.min(255, cg); d[o + 2] = Math.min(255, cb); d[o + 3] = 255;
    const hv = Math.max(0, Math.min(255, h * 255)); hd[o] = hd[o + 1] = hd[o + 2] = hv; hd[o + 3] = 255;
    od[o] = 255; od[o + 1] = rough; od[o + 2] = metal; od[o + 3] = 255;
  }
  g.putImageData(img, 0, 0); hg.putImageData(himg, 0, 0); og.putImageData(oimg, 0, 0);
  eg.fillStyle = '#000'; eg.fillRect(0, 0, N, N);
  const L = [g, hg, og, eg];
  L.forEach(ctx => { ctx.save(); ctx.translate(N / 2, N / 2); ctx.scale(S, S); ctx.lineCap = 'round'; ctx.lineJoin = 'round'; });
  const GOLD = '#c9a04a', GOLD_DK = '#7f5d26', GOLD_LT = '#ead190', GOLD_SH = '#9c7632';
  function gold(fn, w, fill = false, col = GOLD) {
    const st = (ctx, cc, ww) => { ctx.strokeStyle = ctx.fillStyle = cc; ctx.lineWidth = ww; fn(ctx); if (fill) ctx.fill(); if (!fill || ww) ctx.stroke(); };
    st(g, GOLD_DK, w + 0.02); st(g, col, w); if (!fill) st(g, GOLD_LT, w * 0.28);
    st(hg, '#fff', w + 0.015); st(og, 'rgb(255,80,170)', w + 0.015); st(eg, '#4a361a', w);
  }
  function enamel(fn, col = '#26428f') { g.fillStyle = col; fn(g); g.fill(); hg.fillStyle = '#d6d6d6'; fn(hg); hg.fill(); og.fillStyle = 'rgb(255,70,20)'; fn(og); og.fill(); }
  const P = (r, a) => [Math.cos(a) * r, Math.sin(a) * r];
  const circ = (x, y, r) => ctx => { ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); };
  const seg = (p0, p1) => ctx => { ctx.beginPath(); ctx.moveTo(...p0); ctx.lineTo(...p1); };
  const poly = pts => ctx => { ctx.beginPath(); pts.forEach((p, i) => i ? ctx.lineTo(...p) : ctx.moveTo(...p)); ctx.closePath(); };
  const N_ = -Math.PI / 2; // north (up on the canvas = -z)

  // --- lapis disc inlay ---
  gold(circ(0, 0, DISC - 0.07), 0.16);
  for (const [r, w] of [[1.9, 0.035], [2.85, 0.05], [3.7, 0.035], [5.5, 0.055]]) gold(circ(0, 0, r), w);
  for (let i = 0; i < 8; i++) { const a = N_ + i / 8 * TAU; gold(seg(P(i % 2 ? 1.9 : 3.35, a), P(DISC - 0.12, a)), 0.035); }
  for (let i = 0; i < 4; i++) {                                     // ring studs on the diagonals and cardinals
    const a = N_ + Math.PI / 4 + i * Math.PI / 2, b = N_ + i * Math.PI / 2;
    gold(circ(...P(5.5, a), 0.24), 0.045); gold(circ(...P(5.5, a), 0.07), 0.02, true);
    gold(circ(...P(7.4, b), 0.2), 0.04); gold(circ(...P(7.4, a), 0.17), 0.035);
  }
  // eight-point compass star, every point split into a lit and a shaded half
  const starPt = (len, wid, a, lit) => poly([[0, 0], P(wid, a + (lit ? -Math.PI / 4 : Math.PI / 4)), P(len, a)]);
  for (let i = 0; i < 8; i++) {
    const a = N_ + i / 8 * TAU, long = i % 2 === 0, len = long ? 3.35 : 1.9, wid = long ? 0.62 : 0.42;
    gold(starPt(len, wid, a, true), 0.012, true, GOLD_LT); gold(starPt(len, wid, a, false), 0.012, true, GOLD_SH);
  }
  gold(circ(0, 0, 0.3), 0.03, true); gold(circ(0, 0, 0.42), 0.03);

  // --- ivory ring inlay ---
  gold(circ(0, 0, 12.95), 0.05); gold(circ(0, 0, R - 0.08), 0.09);
  for (let i = 0; i < 4; i++) {                                     // cardinal cobalt diamonds at the bridge mouths
    const a = N_ + i * Math.PI / 2, [cx, cy] = P(12.4, a), ux = Math.cos(a), uy = Math.sin(a);
    const dia = (l, w) => poly([[cx + ux * l, cy + uy * l], [cx - uy * w, cy + ux * w], [cx - ux * l, cy - uy * l], [cx + uy * w, cy - ux * w]]);
    gold(dia(0.82, 0.5), 0.06, true); enamel(dia(0.68, 0.38)); gold(dia(0.3, 0.17), 0.015, true);
    gold(dia(0.98, 0.62), 0.03);
  }
  for (let i = 0; i < 4; i++) {                                     // diagonal square markers with flanking chevrons
    const a = N_ + Math.PI / 4 + i * Math.PI / 2, [cx, cy] = P(12.45, a), ux = Math.cos(a), uy = Math.sin(a);
    const sq = s => poly([[cx + ux * s, cy + uy * s], [cx - uy * s, cy + ux * s], [cx - ux * s, cy - uy * s], [cx + uy * s, cy - ux * s]]);
    gold(sq(0.42), 0.05); gold(sq(0.24), 0.03); gold(circ(cx, cy, 0.07), 0.01, true);
    for (const s of [-1, 1]) gold(seg(P(12.45, a + s * 0.05), P(12.45, a + s * 0.16)), 0.03);
  }
  L.forEach(ctx => ctx.restore());

  // height -> tangent-space normal map (Sobel), so the export carries relief without a bump map
  const [nc, ng] = canvas2d(N), nimg = ng.createImageData(N, N), nd = nimg.data, H = hg.getImageData(0, 0, N, N).data, k = 2.2;
  const hAt = (x, y) => H[(Math.min(N - 1, Math.max(0, y)) * N + Math.min(N - 1, Math.max(0, x))) * 4] / 255;
  for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
    const dx = (hAt(x + 1, y) - hAt(x - 1, y)) * k, dy = (hAt(x, y + 1) - hAt(x, y - 1)) * k, inv = 1 / Math.hypot(dx, dy, 1), o = (y * N + x) * 4;
    nd[o] = (-dx * inv * 0.5 + 0.5) * 255; nd[o + 1] = (dy * inv * 0.5 + 0.5) * 255; nd[o + 2] = (inv * 0.5 + 0.5) * 255; nd[o + 3] = 255;
  }
  ng.putImageData(nimg, 0, 0);
  const map = tex(c), glow = tex(ec); map.userData.mimeType = glow.userData.mimeType = 'image/jpeg';   // smaller glTF; normal and ORM stay PNG
  return { map, normal: tex(nc, false), orm: tex(oc, false), glow };
}

// ---- small textures: ashlar blocks, tracery windows, banner, cloud ----
// Ivory ashlar: running-bond blocks with per-block tint, soft grout shading, worn edges and the odd chip, fine grain.
function paintBlocks(w, h, rows, base, seed = 1) {
  const [c, g] = canvas2d(w, h), r2 = mulberry32(seed * 977); g.fillStyle = `rgb(${base.map(v => v - 34)})`; g.fillRect(0, 0, w, h);
  const rh = h / rows, gap = Math.max(2, w / 340);
  for (let r = 0; r < rows; r++) {
    let x = (r % 2) * -w / 10;
    while (x < w) {
      const bw = w / 8 + hash(x + seed, r) * w / 7, v = (hash(r, x + seed) - 0.5) * 16, warm = (hash(x * 3, r + seed) - 0.5) * 6;
      const x0 = x + gap, y0 = r * rh + gap, bw2 = bw - gap * 2, bh = rh - gap * 2;
      g.fillStyle = `rgb(${base[0] + v + warm},${base[1] + v},${base[2] + v - warm})`; g.fillRect(x0, y0, bw2, bh);
      const sh = g.createLinearGradient(0, y0, 0, y0 + bh);                         // lit top edge, worn darker base
      sh.addColorStop(0, 'rgba(255,250,240,0.18)'); sh.addColorStop(0.15, 'rgba(255,250,240,0)'); sh.addColorStop(0.8, 'rgba(90,70,50,0)'); sh.addColorStop(1, 'rgba(90,70,50,0.16)');
      g.fillStyle = sh; g.fillRect(x0, y0, bw2, bh);
      for (let k = 0; k < 3; k++) if (r2() < 0.35) {                                  // chipped corners and edges
        const ex = x0 + (r2() < 0.5 ? r2() * bw2 * 0.15 : bw2 - r2() * bw2 * 0.15), ey = y0 + (r2() < 0.5 ? 0 : bh);
        g.fillStyle = 'rgba(110,90,70,0.28)'; g.beginPath(); g.ellipse(ex, ey, 2 + r2() * gap * 2.5, 1 + r2() * gap * 1.5, r2() * 3, 0, TAU); g.fill();
      }
      x += bw;
    }
  }
  const img = g.getImageData(0, 0, w, h), d = img.data;                                // fine stone grain
  for (let i = 0; i < d.length; i += 4) { const p = i / 4, n = (hash(p * 0.37, seed) - 0.5) * 10 + (vnoise((p % w) * 0.05, Math.floor(p / w) * 0.05) - 0.5) * 8; d[i] += n; d[i + 1] += n; d[i + 2] += n * 0.9; }
  g.putImageData(img, 0, 0);
  const t = tex(c); t.wrapS = t.wrapT = THREE.RepeatWrapping; return t;
}
// Tall cobalt lancets with gold leading. Variant 0: two lights under a trefoil; 1: one tall light under a small rose;
// 2: three stepped lights under a quatrefoil. Every variant carries a small gold star.
function paintWindow(variant = 0, w = 256, h = 512) {
  const [c, g] = canvas2d(w, h);
  const lancet = (x0, x1, top, bot) => { const ww = x1 - x0, sh = top + ww * 0.8; g.beginPath(); g.moveTo(x0, bot); g.lineTo(x0, sh); g.quadraticCurveTo(x0, top + ww * 0.12, (x0 + x1) / 2, top); g.quadraticCurveTo(x1, top + ww * 0.12, x1, sh); g.lineTo(x1, bot); g.closePath(); };
  const glass = g.createLinearGradient(0, 0, 0, h); glass.addColorStop(0, '#3a4c96'); glass.addColorStop(0.6, '#2a3a7c'); glass.addColorStop(1, '#1f2c66');
  lancet(0, w, 0, h); g.fillStyle = glass; g.fill();
  g.save(); lancet(0, w, 0, h); g.clip();                                             // diamond quarries
  g.strokeStyle = 'rgba(232,189,104,0.35)'; g.lineWidth = 1.5;
  for (let k = -h; k < w + h; k += 22) { g.beginPath(); g.moveTo(k, 0); g.lineTo(k + h * 0.5, h); g.stroke(); g.beginPath(); g.moveTo(k, h); g.lineTo(k + h * 0.5, 0); g.stroke(); }
  g.restore();
  g.strokeStyle = '#e3b964'; g.lineWidth = 7;
  const circle = (x, y, r) => { g.beginPath(); g.arc(x, y, r, 0, TAU); g.stroke(); };
  if (variant === 0) { lancet(w * 0.12, w * 0.48, h * 0.32, h * 0.97); g.stroke(); lancet(w * 0.52, w * 0.88, h * 0.32, h * 0.97); g.stroke(); for (const [x, y] of [[0.5, 0.13], [0.39, 0.21], [0.61, 0.21]]) circle(w * x, h * y, w * 0.09); }
  else if (variant === 1) {
    lancet(w * 0.18, w * 0.82, h * 0.3, h * 0.97); g.stroke(); circle(w * 0.5, h * 0.16, w * 0.16); g.lineWidth = 3;
    for (let i = 0; i < 8; i++) { const a = i / 8 * TAU; g.beginPath(); g.moveTo(w * 0.5, h * 0.16); g.lineTo(w * 0.5 + Math.cos(a) * w * 0.16, h * 0.16 + Math.sin(a) * w * 0.16); g.stroke(); }
  } else { lancet(w * 0.08, w * 0.34, h * 0.42, h * 0.97); g.stroke(); lancet(w * 0.37, w * 0.63, h * 0.34, h * 0.97); g.stroke(); lancet(w * 0.66, w * 0.92, h * 0.42, h * 0.97); g.stroke(); for (const [x, y] of [[0.5, 0.11], [0.5, 0.23], [0.41, 0.17], [0.59, 0.17]]) circle(w * x, h * y, w * 0.06); }
  g.lineWidth = 12; lancet(0, w, 0, h); g.stroke();                                    // outer gold frame
  g.fillStyle = '#efc872'; g.save(); g.translate(w / 2, h * (variant === 1 ? 0.55 : 0.62)); g.beginPath();
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU - Math.PI / 2, rr = i % 2 ? w * 0.035 : w * 0.13; g.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); } g.closePath(); g.fill(); g.restore();
  return tex(c);
}
function paintBanner(w = 256, h = 1024) {
  const [c, g] = canvas2d(w, h);
  const grd = g.createLinearGradient(0, 0, w, 0); grd.addColorStop(0, '#1a3389'); grd.addColorStop(0.5, '#2547ad'); grd.addColorStop(1, '#1a3389');
  g.fillStyle = grd; g.fillRect(0, 0, w, h);
  for (let i = 0; i < 8; i++) { g.fillStyle = `rgba(0,0,30,${0.04 + (i % 2) * 0.06})`; g.fillRect(i * w / 8, 0, w / 16, h); }   // cloth folds
  g.strokeStyle = '#e8bd68'; g.lineWidth = 16; g.strokeRect(8, 8, w - 16, h - 16); g.lineWidth = 3; g.strokeRect(28, 28, w - 56, h - 56);
  g.fillStyle = '#e8bd68'; for (let y = 60; y < h - 40; y += 36) { g.beginPath(); g.arc(18, y, 3, 0, TAU); g.arc(w - 18, y, 3, 0, TAU); g.fill(); }
  g.fillStyle = '#f0c672'; g.save(); g.translate(w / 2, h * 0.36); g.beginPath();
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU - Math.PI / 2, rr = i % 2 ? 18 : 80; g.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); } g.closePath(); g.fill();
  g.lineWidth = 5; g.strokeStyle = '#f0c672'; g.beginPath(); g.arc(0, 0, 60, 0, TAU); g.stroke(); g.lineWidth = 2; g.beginPath(); g.arc(0, 0, 70, 0, TAU); g.stroke(); g.restore();
  g.fillStyle = '#f0c672'; for (let i = 0; i < 3; i++) { g.beginPath(); g.arc(w * (0.3 + i * 0.2), h * 0.66, 8, 0, TAU); g.fill(); }
  return tex(c);
}
function paintCloud(N = 256, seed = 1) {
  const r2 = mulberry32(seed), [c, g] = canvas2d(N);
  for (let i = 0; i < 36; i++) {
    const x = N * (0.18 + r2() * 0.64), y = N * (0.3 + r2() * 0.36), rr = N * (0.08 + r2() * 0.16);
    const grd = g.createRadialGradient(x, y - rr * 0.35, 0, x, y, rr); grd.addColorStop(0, 'rgba(255,255,255,0.75)'); grd.addColorStop(0.6, 'rgba(238,244,255,0.45)'); grd.addColorStop(1, 'rgba(210,224,250,0)');
    g.fillStyle = grd; g.fillRect(0, 0, N, N);
  }
  return tex(c);
}

// ---- materials (named: Unity's builder keys off these names) ----
const named = (m, name) => { m.name = name; return m; };
const ivoryBlocks = paintBlocks(1024, 1024, 8, [244, 230, 200]), ivoryBlocks2 = paintBlocks(1024, 1024, 6, [230, 213, 184], 5);
for (const t of [ivoryBlocks, ivoryBlocks2]) t.userData.mimeType = 'image/jpeg';   // opaque colour maps go to the glTF as JPEG (smaller file)
const windowMat = (v, name) => { const map = paintWindow(v); return named(new THREE.MeshStandardMaterial({ map, emissiveMap: map, roughness: 0.3, metalness: 0.15, emissive: 0x2a3c7e, emissiveIntensity: 0.15, alphaTest: 0.5 }), name); };
const MAT = {
  ivory: named(new THREE.MeshStandardMaterial({ color: 0xffffff, map: ivoryBlocks, roughness: 0.7 }), 'IvoryBlocks'),
  ivoryShade: named(new THREE.MeshStandardMaterial({ color: 0xffffff, map: ivoryBlocks2, roughness: 0.78 }), 'IvoryShade'),
  ivoryPlain: named(new THREE.MeshStandardMaterial({ color: 0xede3cc, roughness: 0.6 }), 'Ivory'),
  gold: named(new THREE.MeshStandardMaterial({ color: 0xd6bc82, roughness: 0.36, metalness: 0.55, emissive: 0x3e2a0c, emissiveIntensity: 0.25 }), 'Gold'),
  goldDark: named(new THREE.MeshStandardMaterial({ color: 0x9c7a3a, roughness: 0.45, metalness: 0.55, emissive: 0x2a1a06, emissiveIntensity: 0.25 }), 'GoldDark'),
  cobalt: named(new THREE.MeshStandardMaterial({ color: 0x2b3f8f, roughness: 0.3, metalness: 0.2 }), 'Cobalt'),
  windows: [windowMat(0, 'StarWindow'), windowMat(1, 'StarWindow2'), windowMat(2, 'StarWindow3')],
  banner: named(new THREE.MeshStandardMaterial({ map: paintBanner(), side: THREE.DoubleSide, roughness: 0.75 }), 'Banner'),
  shadow: named(new THREE.MeshStandardMaterial({ color: 0x8a90a8, roughness: 0.9 }), 'ArchShadow'),
};
MAT.window = MAT.windows[0];
let winPick = 0;
const nextWindow = () => MAT.windows[(winPick++ >> 2) % 3];   // runs of four calls share a design, so a storey stays consistent

const root = new THREE.Group(); root.name = 'LB_ENV_SkywardSpire_Arena'; scene.add(root);
const G = name => { const g = new THREE.Group(); g.name = name; root.add(g); return g; };
const gFloor = G('Floor'), gRim = G('Rim'), gBridges = G('Bridges'), gBelfries = G('Belfries'), gSpires = G('Spires'), gAbyss = G('Abyss');
function add(geo, mat, parent, pos, rot, scl) { const m = new THREE.Mesh(geo, mat); m.castShadow = m.receiveShadow = true; if (pos) m.position.set(...pos); if (rot) m.rotation.set(...rot); if (scl) m.scale.set(...scl); parent.add(m); return m; }
const grp = (parent, pos, rotY = 0, name) => { const g = new THREE.Group(); if (pos) g.position.set(...pos); g.rotation.y = rotY; if (name) g.name = name; parent.add(g); return g; };
// Cloud anchors: empty nodes (position = structure centre at floor level, scale = footprint radius). Unity's builder
// gathers a waist-height cloud cluster round each one, where the SkyArchitecture shader dissolves the structure.
const gAnchors = G('CloudAnchors');
function anchor(parent, x, z, r) {
  parent.updateMatrixWorld(true);
  const p = new THREE.Vector3(x, 0, z).applyMatrix4(parent.matrixWorld), a = new THREE.Object3D();
  a.name = 'CloudAnchor'; a.position.set(p.x, 0, p.z); a.scale.setScalar(r); gAnchors.add(a);
}

// ---- reusable shapes ----
const archCache = {};
function archGeo(w, h) {    // pointed arch facing +z, base at y = 0, UVs normalised to the arch's box
  const k = w + '|' + h; if (archCache[k]) return archCache[k];
  const s = new THREE.Shape(), hw = w / 2, sh = Math.max(0, h - w * 0.8);
  s.moveTo(-hw, 0); s.lineTo(-hw, sh); s.quadraticCurveTo(-hw, h - w * 0.15, 0, h); s.quadraticCurveTo(hw, h - w * 0.15, hw, sh); s.lineTo(hw, 0); s.closePath();
  const geo = new THREE.ShapeGeometry(s, 8), p = geo.attributes.position, uv = geo.attributes.uv;
  for (let i = 0; i < p.count; i++) uv.setXY(i, (p.getX(i) + hw) / w, p.getY(i) / h);
  return archCache[k] = geo;
}
function ribbedDome(parent, r, mat, ribMat, pos, ribs = 8) {
  const d = grp(parent, pos);
  add(new THREE.SphereGeometry(r, 20, 10, 0, TAU, 0, Math.PI / 2), mat, d);
  for (let i = 0; i < ribs / 2; i++) add(new THREE.TorusGeometry(r * 1.01, r * 0.045, 5, 18, Math.PI), ribMat, d, null, [0, i / (ribs / 2) * Math.PI, 0]);
  add(new THREE.TorusGeometry(r * 1.02, r * 0.06, 6, 28), ribMat, d, null, [Math.PI / 2, 0, 0]);
  return d;
}
function finial(parent, pos, s = 1) {
  const f = grp(parent, pos);
  add(new THREE.CylinderGeometry(0.06 * s, 0.1 * s, 0.25 * s, 8), MAT.gold, f, [0, 0.12 * s, 0]);
  add(new THREE.SphereGeometry(0.13 * s, 10, 8), MAT.gold, f, [0, 0.36 * s, 0]);
  add(new THREE.ConeGeometry(0.05 * s, 0.5 * s, 6), MAT.gold, f, [0, 0.7 * s, 0]);
  return f;
}
// small round pavilion: plinth, six columns, entablature, ribbed gold dome, finial
function pavilion(parent, x, z, s = 1, domeMat = MAT.gold) {
  const p = grp(parent, [x, 0, z]); p.scale.setScalar(s);
  add(new THREE.CylinderGeometry(0.62, 0.68, 0.55, 16), MAT.ivoryPlain, p, [0, 0.27, 0]);
  add(new THREE.CylinderGeometry(0.66, 0.66, 0.06, 16), MAT.gold, p, [0, 0.56, 0]);
  for (let i = 0; i < 6; i++) { const a = i / 6 * TAU; add(new THREE.CylinderGeometry(0.06, 0.07, 0.95, 6), MAT.ivoryPlain, p, [Math.cos(a) * 0.48, 1.05, Math.sin(a) * 0.48]); }
  add(new THREE.CylinderGeometry(0.4, 0.4, 0.9, 12), MAT.shadow, p, [0, 1.05, 0]);
  add(new THREE.CylinderGeometry(0.7, 0.6, 0.24, 16), MAT.ivoryPlain, p, [0, 1.62, 0]);
  add(new THREE.CylinderGeometry(0.64, 0.64, 0.06, 16), MAT.gold, p, [0, 1.74, 0]);
  ribbedDome(p, 0.42, domeMat, MAT.goldDark, [0, 1.76, 0]);
  finial(p, [0, 2.16, 0], 0.8);
  return p;
}
// gold armillary sphere: base drum, meridians, equator, tilted ecliptic, globe; the inner rings turn
function armillary(parent, pos, r = 1.6) {
  const a = grp(parent, pos, 0, 'Armillary');
  add(new THREE.CylinderGeometry(r * 0.35, r * 0.5, r * 0.35, 12), MAT.gold, a, [0, r * 0.18, 0]);
  add(new THREE.CylinderGeometry(r * 0.08, r * 0.12, r * 0.5, 8), MAT.gold, a, [0, r * 0.55, 0]);
  const c = grp(a, [0, r * 1.75, 0]);
  add(new THREE.TorusGeometry(r, r * 0.045, 6, 48), MAT.gold, c, null, [Math.PI / 2, 0, 0]);
  add(new THREE.TorusGeometry(r, r * 0.045, 6, 48), MAT.gold, c);
  add(new THREE.TorusGeometry(r * 1.06, r * 0.035, 6, 48), MAT.goldDark, c, null, [0, Math.PI / 2, 0]);
  for (const [tilt, rr, sp] of [[0.41, 0.86, 1], [-0.7, 0.72, -1.4]]) {
    const ring = grp(c, null, 0, 'ArmillaryRing'); ring.rotation.z = tilt; ring.userData.spin = sp;
    add(new THREE.TorusGeometry(r * rr, r * 0.05, 6, 40), MAT.gold, ring, null, [Math.PI / 2, 0, 0]);
    add(new THREE.SphereGeometry(r * 0.08, 8, 6), MAT.gold, ring, [r * rr, 0, 0]);
  }
  add(new THREE.SphereGeometry(r * 0.3, 16, 12), MAT.cobalt, c);
  add(new THREE.CylinderGeometry(r * 0.025, r * 0.025, r * 2.5, 6), MAT.gold, c, null, [0, 0, 0.41]);
  finial(c, [0, r * 1.05, 0], r * 0.7);
  return a;
}
function pinnacle(parent, x, y, z, h, w, mat = MAT.ivoryPlain) {
  add(new THREE.BoxGeometry(w, h, w), mat, parent, [x, y + h / 2, z]);
  add(new THREE.BoxGeometry(w * 1.2, 0.07, w * 1.2), MAT.gold, parent, [x, y + h, z]);
  add(new THREE.ConeGeometry(w * 0.7, h * 0.9, 4), mat, parent, [x, y + h * 1.45, z], [0, Math.PI / 4, 0]);
  for (let k = 0; k < 4; k++) {                    // crockets: little leaf knobs climbing the spirelet's edges
    const a = k * Math.PI / 2, f = 0.35 + (k % 2) * 0.2;
    add(new THREE.SphereGeometry(w * 0.11, 6, 4), mat, parent, [x + Math.cos(a) * w * 0.5 * (1 - f), y + h + h * 0.9 * f, z + Math.sin(a) * w * 0.5 * (1 - f)]);
  }
  add(new THREE.SphereGeometry(w * 0.16, 8, 6), MAT.gold, parent, [x, y + h * 1.95, z]);
}
// windows on the four faces of a w x d box centred at (0, y, 0) in `parent`; faces listed as 'nesw'
function windowsOnBox(parent, w, d, y, wh, faces = 'nesw', cols = 0) {
  const ww = wh * 0.34;   // tall lancets, about 2.2 : 1 with the frame
  for (const f of faces) {
    const along = f === 'n' || f === 's' ? w : d, n = cols || Math.max(1, Math.floor(along / (ww * 2.5)));
    const win = nextWindow();
    for (let i = 0; i < n; i++) {
      const t = (i + 0.5) / n - 0.5, off = t * along;
      const [px, pz, ry] = f === 's' ? [off, d / 2 + 0.03, 0] : f === 'n' ? [-off, -d / 2 - 0.03, Math.PI] : f === 'e' ? [w / 2 + 0.03, -off, Math.PI / 2] : [-w / 2 - 0.03, off, -Math.PI / 2];
      add(archGeo(ww, wh), win, parent, [px, y, pz], [0, ry, 0]);
      const fr = add(archGeo(ww * 1.22, wh * 1.07), MAT.gold, parent, [px * 0.999, y - wh * 0.035, pz * 0.999], [0, ry, 0]);
      fr.position.x -= Math.sin(ry) * 0.012; fr.position.z -= Math.cos(ry) * 0.012;
      if (i < n - 1) {                             // blind arcade between this window and the next
        const t2 = (i + 1) / n - 0.5, mid = (t + t2) / 2 * along;
        const [bx, bz] = f === 's' ? [mid, d / 2 + 0.02] : f === 'n' ? [-mid, -d / 2 - 0.02] : f === 'e' ? [w / 2 + 0.02, -mid] : [-w / 2 - 0.02, mid];
        add(archGeo(ww * 0.42, wh * 0.5), MAT.shadow, parent, [bx, y + wh * 0.12, bz], [0, ry, 0]);
        const nx = Math.sin(ry), nz = Math.cos(ry);   // pilaster dividing the bays, capped in gold
        add(new THREE.BoxGeometry(0.34, wh * 1.15, 0.3), MAT.ivoryPlain, parent, [bx + nx * 0.1, y + wh * 0.55, bz + nz * 0.1], [0, ry, 0]);
        add(new THREE.BoxGeometry(0.42, 0.12, 0.36), MAT.gold, parent, [bx + nx * 0.1, y + wh * 1.13, bz + nz * 0.1], [0, ry, 0]);
      }
    }
  }
}
// round tower drum with arched star windows all round
function roundTier(parent, r, h, y, nWin, mat = MAT.ivory) {
  add(new THREE.CylinderGeometry(r, r * 1.03, h, 24), mat, parent, [0, y + h / 2, 0]);
  add(new THREE.CylinderGeometry(r * 1.08, r * 1.08, 0.22, 24), MAT.gold, parent, [0, y + h, 0]);
  add(new THREE.CylinderGeometry(r * 1.05, r * 1.05, 0.14, 24), MAT.goldDark, parent, [0, y + 0.07, 0]);
  const wh = h * 0.72, ww = Math.min(wh * 0.32, r * TAU / nWin * 0.5), win = nextWindow();
  for (let i = 0; i < nWin; i++) {
    const a = i / nWin * TAU;
    add(archGeo(ww, wh), win, parent, [Math.sin(a) * (r + 0.02), y + h * 0.16, Math.cos(a) * (r + 0.02)], [0, a, 0]);
    add(archGeo(ww * 1.25, wh * 1.07), MAT.gold, parent, [Math.sin(a) * (r + 0.005), y + h * 0.16 - wh * 0.035, Math.cos(a) * (r + 0.005)], [0, a, 0]);
    add(new THREE.BoxGeometry(r * 0.16, h, r * 0.16), MAT.ivoryPlain, parent, [Math.sin(a + Math.PI / nWin) * r, y + h / 2, Math.cos(a + Math.PI / nWin) * r]);
  }
}

// ================================================================================================
// 1. Floor
// ================================================================================================
const floorTex = paintFloor(opts.floorRes || 3072);
const floorMat = named(new THREE.MeshStandardMaterial({ map: floorTex.map, normalMap: floorTex.normal, normalScale: new THREE.Vector2(1, 1),
  roughnessMap: floorTex.orm, metalnessMap: floorTex.orm, roughness: 1, metalness: 1, emissive: 0xffd9a0, emissiveMap: floorTex.glow, emissiveIntensity: 0.12 }), 'Floor');
const floor = add(new THREE.CircleGeometry(R, 192), floorMat, gFloor, [0, 0, 0], [-Math.PI / 2, 0, 0]);
floor.name = 'FloorSurface'; floor.castShadow = false;
floor.userData.keep = true;

// ================================================================================================
// 2. Rim: curb, balustrade, pavilion posts, gates, outer drum with blind arches, tapering underside
// ================================================================================================
add(new THREE.RingGeometry(R, LEDGE, 192, 1), MAT.ivoryPlain, gRim, [0, 0.02, 0], [-Math.PI / 2, 0, 0]);
add(new THREE.TorusGeometry(R, 0.05, 6, 192), MAT.gold, gRim, [0, 0.03, 0], [Math.PI / 2, 0, 0]);
add(new THREE.TorusGeometry(LEDGE, 0.08, 6, 192), MAT.gold, gRim, [0, 0.0, 0], [Math.PI / 2, 0, 0]);
const GAP = BW / 2 + 0.35;
{ // balustrade: plinth, balusters, rail and gold coping, segment by segment
  const n = 260;
  for (let i = 0; i < n; i++) {
    const a = (i + 0.5) / n * TAU; if (nearBridge(a, GAP + 0.4, RAIL)) continue;
    const x = Math.cos(a) * RAIL, z = Math.sin(a) * RAIL, ry = Math.PI / 2 - a, len = RAIL * TAU / n;
    add(new THREE.BoxGeometry(len * 1.02, 0.22, 0.42), MAT.ivoryPlain, gRim, [x, 0.11, z], [0, ry, 0]);
    add(new THREE.CylinderGeometry(0.06, 0.08, 0.5, 6), MAT.ivoryPlain, gRim, [x, 0.47, z]);
    add(new THREE.BoxGeometry(len * 1.03, 0.12, 0.34), MAT.ivoryPlain, gRim, [x, 0.78, z], [0, ry, 0]);
    add(new THREE.BoxGeometry(len * 1.04, 0.05, 0.4), MAT.gold, gRim, [x, 0.865, z], [0, ry, 0]);
  }
}
// 28 pavilion posts: 20 on the arcs between the bridges plus a larger pair at each bridge mouth
for (let k = 0; k < 4; k++) {
  const a0 = k * Math.PI / 2 + (GAP + 2.6) / RAIL, a1 = (k + 1) * Math.PI / 2 - (GAP + 2.6) / RAIL;
  for (let i = 0; i <= 6; i++) { const a = a0 + (a1 - a0) * i / 6; pavilion(gRim, Math.cos(a) * RAIL, Math.sin(a) * RAIL, 0.55); }
}
for (let k = 0; k < 4; k++) {                    // bridge-mouth thresholds and gate pavilions
  const a = k * Math.PI / 2, b = grp(gRim, null, -a);
  add(new THREE.BoxGeometry(1.6, 0.06, BW + 1.6), MAT.ivoryPlain, b, [R + 0.8, 0.03, 0]);
  for (const s of [-1, 1]) add(new THREE.BoxGeometry(1.6, 0.07, 0.07), MAT.gold, b, [R + 0.8, 0.06, s * (BW / 2 + 0.8)]);
  add(new THREE.BoxGeometry(0.07, 0.07, BW + 1.6), MAT.gold, b, [LEDGE, 0.06, 0]);
  for (const s of [-1, 1]) pavilion(b, RAIL + 0.2, s * (GAP + 0.55), 0.95);
}
// long star banners hanging over the outer wall either side of each bridge mouth, tilted out so they show from above
const rimBanners = [];
for (let k = 0; k < 4; k++) for (const s of [-1, 1]) for (const off of [1.5, 3.4]) {
  const a = k * Math.PI / 2 + s * (GAP + off) / LEDGE, bn = new THREE.Mesh(new THREE.PlaneGeometry(1.15, 4.2, 1, 8), MAT.banner);
  bn.name = 'BannerCloth'; bn.geometry.translate(0, -2.1, 0); bn.userData.dynamic = true; bn.castShadow = true;
  bn.position.set(Math.cos(a) * (LEDGE + 0.12), -0.05, Math.sin(a) * (LEDGE + 0.12)); bn.rotation.order = 'YXZ'; bn.rotation.set(-0.5, Math.PI / 2 - a, 0); bn.userData.ry = Math.PI / 2 - a;
  gRim.add(bn); rimBanners.push(bn);
  add(new THREE.CylinderGeometry(0.05, 0.05, 1.5, 6), MAT.gold, gRim, [Math.cos(a) * (LEDGE + 0.12), -0.02, Math.sin(a) * (LEDGE + 0.12)], [0, Math.PI / 2 - a, Math.PI / 2]);
}
// outer drum wall: blind gothic arches with shadowed recesses, gold bands; then the tiered underside
add(new THREE.CylinderGeometry(LEDGE, LEDGE, 6, 192, 1, true), MAT.ivory, gRim, [0, -3, 0]);
for (const y of [-0.15, -0.9, -5.8]) add(new THREE.TorusGeometry(LEDGE + 0.04, y > -1 ? 0.1 : 0.14, 6, 192), MAT.gold, gRim, [0, y, 0], [Math.PI / 2, 0, 0]);
for (let i = 0; i < 48; i++) {
  const a = (i + 0.5) / 48 * TAU, x = Math.cos(a) * (LEDGE + 0.02), z = Math.sin(a) * (LEDGE + 0.02), ry = Math.PI / 2 - a;
  add(archGeo(1.0, 3.4), MAT.shadow, gRim, [x, -4.9, z], [0, ry, 0]);
  add(archGeo(1.3, 3.6), MAT.gold, gRim, [x * 0.9995, -5.0, z * 0.9995], [0, ry, 0]);
  add(new THREE.BoxGeometry(0.3, 6, 0.3), MAT.ivoryPlain, gRim, [Math.cos(a + TAU / 96) * (LEDGE + 0.1), -3, Math.sin(a + TAU / 96) * (LEDGE + 0.1)], [0, Math.PI / 2 - a, 0]);
}
for (const [rt, rb, h, y, m] of [[LEDGE + 0.3, LEDGE - 0.4, 1, -6.5, MAT.ivoryShade], [LEDGE - 0.4, 11, 6, -10, MAT.ivory], [11, 6.5, 8, -17, MAT.ivoryShade], [6.5, 2, 10, -26, MAT.ivory], [2, 0.2, 6, -34, MAT.gold]])
  add(new THREE.CylinderGeometry(rt, rb, h, 64), m, gRim, [0, y, 0]);
for (const [y, r] of [[-7.1, LEDGE - 0.35], [-13.1, 11], [-21.1, 6.5]]) add(new THREE.TorusGeometry(r, 0.14, 6, 96), MAT.gold, gRim, [0, y, 0], [Math.PI / 2, 0, 0]);
for (let i = 0; i < 16; i++) {                   // hanging pinnacles under the rim
  const a = (i + 0.25) / 16 * TAU; if (nearBridge(a, 3, LEDGE)) continue;
  const rr = LEDGE - 1.2, len = 3 + (i * 5 % 4) * 1.5, x = Math.cos(a) * rr, z = Math.sin(a) * rr;
  add(new THREE.CylinderGeometry(0.4, 0.35, len, 8), MAT.ivory, gRim, [x, -7 - len / 2, z]);
  add(new THREE.CylinderGeometry(0.46, 0.46, 0.14, 8), MAT.gold, gRim, [x, -7 - len, z]);
  add(new THREE.ConeGeometry(0.38, 2.2, 8), MAT.ivoryPlain, gRim, [x, -8.1 - len, z], [Math.PI, 0, 0]);
}
for (let i = 0; i < 12; i++) { const a = (i + 0.5) / 12 * TAU; anchor(gRim, Math.cos(a) * 10.5, Math.sin(a) * 10.5, 4.2); }   // the dais itself sinks into cloud

// ================================================================================================
// 3. Bridges: N, E, S (entry), W. Deck, side walls, banner posts both sides, cobalt plaques, arches below
// ================================================================================================
const BRIDGE_DIRS = [['Bridge_E', 0], ['Bridge_S', Math.PI / 2], ['Bridge_W', Math.PI], ['Bridge_N', -Math.PI / 2]];
const deckTex = paintBlocks(1024, 256, 3, [238, 228, 204], 9); deckTex.repeat.set(8, 1);
const deckMat = named(new THREE.MeshStandardMaterial({ map: deckTex, roughness: 0.7 }), 'BridgeDeck');
const banners = [];
for (const [name, ang] of BRIDGE_DIRS) {
  const br = grp(gBridges, null, -ang, name);       // local +x points outward along the bridge
  const x0 = LEDGE - 0.1, x1 = 46, len = x1 - x0, mid = (x0 + x1) / 2, hw = BW / 2;
  add(new THREE.BoxGeometry(len, 0.5, BW), deckMat, br, [mid, -0.25, 0]);
  add(new THREE.BoxGeometry(len, 1.2, BW + 0.5), MAT.ivoryShade, br, [mid, -1.1, 0]);
  add(new THREE.BoxGeometry(len, 0.12, BW + 0.62), MAT.gold, br, [mid, -0.5, 0]);
  add(new THREE.BoxGeometry(len, 0.1, BW + 0.56), MAT.goldDark, br, [mid, -1.7, 0]);
  for (const px of name === 'Bridge_E' || name === 'Bridge_W' ? [19.2, 28.5, 38] : []) {   // cobalt plaques on the side bridges' centreline
    add(new THREE.BoxGeometry(1.5, 0.03, 0.62), MAT.gold, br, [px, 0.01, 0]);
    add(new THREE.BoxGeometry(1.36, 0.04, 0.48), MAT.cobalt, br, [px, 0.015, 0]);
  }
  for (const s of [-1, 1]) {
    const sz = s * (hw - 0.15);
    add(new THREE.BoxGeometry(len, 0.6, 0.3), MAT.ivoryPlain, br, [mid, 0.3, sz]);
    add(new THREE.BoxGeometry(len, 0.06, 0.38), MAT.gold, br, [mid, 0.63, sz]);
    for (let px = 17.2; px < x1 - 1; px += 3.2) {   // balustrade posts: ivory shaft, gold dome cap
      const pz = s * (hw - 0.15);
      add(new THREE.BoxGeometry(0.4, 1.3, 0.4), MAT.ivoryPlain, br, [px, 0.65, pz]);
      add(new THREE.BoxGeometry(0.48, 0.07, 0.48), MAT.gold, br, [px, 1.3, pz]);
      ribbedDome(br, 0.22, MAT.gold, MAT.goldDark, [px, 1.33, pz], 6);
      add(new THREE.SphereGeometry(0.06, 8, 6), MAT.gold, br, [px, 1.6, pz]);
      // a long star banner draped over the balustrade between posts, long side along the bridge (as the reference shows them)
      const bn = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 2.5, 1, 8), MAT.banner); bn.name = 'BannerCloth';
      bn.geometry.rotateZ(Math.PI / 2); bn.geometry.translate(0, -0.5, 0);
      bn.position.set(px + 1.6, 0.66, s * (hw + 0.02)); bn.rotation.x = -s * 0.65; bn.castShadow = true;
      bn.userData.dynamic = true; br.add(bn); banners.push(bn);
    }
  }
  for (let i = 0; i < 5; i++) {                      // gothic arches and piers under the span
    const ax = x0 + (i + 0.5) / 5 * len;
    const arch = add(new THREE.TorusGeometry(1.8, 0.35, 6, 20, Math.PI), MAT.ivoryShade, br, [ax, -3.6, 0], [0, 0, Math.PI], [1, 1.25, 4.4]);
    add(new THREE.BoxGeometry(0.9, 12, 0.9), MAT.ivory, br, [ax - len / 10, -7.6, 0]);
    add(new THREE.ConeGeometry(0.65, 3, 4), MAT.ivoryPlain, br, [ax - len / 10, -15.1, 0], [Math.PI, Math.PI / 4, 0]);
    for (const y of [-2.2, -6.5]) add(new THREE.BoxGeometry(1.05, 0.16, 1.05), MAT.gold, br, [ax - len / 10, y, 0]);
    anchor(br, ax - len / 10, 0, 1.5);
  }
}

// ================================================================================================
// 4. Belfries and landmark buildings (positions read off reference-topdown.webp, north up)
// ================================================================================================
// Gothic cathedral block: stacked tiers, windows on every face, corner pinnacles, flat roof with balustrade.
function cathedral(parent, x, z, rotY, { w = 9, d = 6, tiers = [8, 6, 5], name = 'Cathedral' } = {}) {
  const c = grp(parent, [x, 0, z], rotY, name);
  let y = -30, cw = w, cd = d;
  // below the floor line: one undercroft storey, then the mass splits into separate tower shafts falling into the
  // cloud (the tilted top-down view shows these, so they must read as a cluster, not one windowed wall)
  add(new THREE.BoxGeometry(cw * 0.97, 4, cd * 0.97), MAT.ivory, c, [0, -2, 0]);
  add(new THREE.BoxGeometry(cw, 0.22, cd), MAT.gold, c, [0, -0.1, 0]);
  windowsOnBox(c, cw * 0.97, cd * 0.97, -3.6, 3, 'nesw');
  const shafts = w >= 14 ? [[-0.38, 0.17], [-0.13, 0.2], [0.13, 0.2], [0.38, 0.17]] : w >= 9 ? [[-0.33, 0.24], [0, 0.3], [0.33, 0.24]] : [[-0.22, 0.26], [0.22, 0.26]];
  for (const [fx, fw] of shafts) {
    const sw = cw * fw, sx = fx * cw;
    for (let i = 0; i < 3; i++) {
      const y0 = -4 - 8 * (i + 1), tw = sw * (1 - i * 0.07);
      add(new THREE.BoxGeometry(tw, 8, tw), i ? MAT.ivoryShade : MAT.ivory, c, [sx, y0 + 4, 0]);
      add(new THREE.BoxGeometry(tw * 1.08, 0.18, tw * 1.08), MAT.gold, c, [sx, y0 + 7.9, 0]);
      const g = grp(c, [sx, 0, 0]); windowsOnBox(g, tw, tw, y0 + 1.2, 5.6, 'nesw', 1);
      for (const px of [-1, 1]) for (const pz of [-1, 1]) add(new THREE.BoxGeometry(0.45, 8, 0.45), MAT.ivoryPlain, c, [sx + px * tw / 2, y0 + 4, pz * tw / 2]);
    }
    add(new THREE.ConeGeometry(sw * 0.6, 5, 4), MAT.ivoryShade, c, [sx, -30.5, 0], [Math.PI, Math.PI / 4, 0]);   // pointed foot
  }
  // the hall: one windowed storey across the full footprint, a gold cornice and a pitched ridge roof
  const h0 = tiers[0];
  add(new THREE.BoxGeometry(cw, h0, cd), MAT.ivory, c, [0, h0 / 2, 0]);
  add(new THREE.BoxGeometry(cw * 1.06, 0.25, cd * 1.06), MAT.gold, c, [0, h0, 0]);
  add(new THREE.BoxGeometry(cw * 1.04, 0.14, cd * 1.04), MAT.goldDark, c, [0, 0.07, 0]);
  add(new THREE.BoxGeometry(cw * 1.03, 0.12, cd * 1.03), MAT.gold, c, [0, h0 * 0.55, 0]);
  windowsOnBox(c, cw, cd, h0 * 0.12, h0 * 0.78, 'nesw');
  for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
    add(new THREE.BoxGeometry(0.6, h0, 0.6), MAT.ivoryPlain, c, [sx * cw / 2, h0 / 2, sz * cd / 2]);
    pinnacle(c, sx * cw / 2, h0, sz * cd / 2, 2.2, 0.48);
  }
  for (let i = 1; i < Math.floor(cw / 2.2); i++) for (const sz of [-1, 1]) pinnacle(c, -cw / 2 + i * cw / Math.floor(cw / 2.2), h0, sz * cd / 2, 0.9, 0.22);
  add(new THREE.CylinderGeometry(cd * 0.42, cd * 0.42, cw * 0.96, 3), MAT.ivoryShade, c, [0, h0 + cd * 0.21, 0], [0, 0, Math.PI / 2]);
  add(new THREE.BoxGeometry(cw * 0.96, 0.12, 0.16), MAT.gold, c, [0, h0 + cd * 0.42, 0]);   // gilded ridge line

  // above the roof: a cluster of slender square belfries with tall pointed spires (no more stacked boxes)
  const n = w >= 14 ? 4 : w >= 9 ? 3 : 2, sw = Math.min(cw * (n === 4 ? 0.17 : 0.22), cd * 0.5), up = tiers.slice(1).reduce((s, v) => s + v, 0) || 8;
  for (let k = 0; k < n; k++) {
    const tx = n === 4 ? (k - 1.5) * cw * 0.25 : n === 3 ? (k - 1) * cw * 0.33 : (k - 0.5) * cw * 0.42, mid = (n === 3 && k === 1) || (n === 4 && (k === 1 || k === 2));
    const th = up * (mid ? 1.15 : 0.75 + hash(tx + x, z) * 0.2), base = h0 + 0.25;
    let ty = base, tw = sw;
    for (const f of [0.55, 0.45]) {                                     // two storeys, the upper slightly narrower
      const sh = th * f;
      add(new THREE.BoxGeometry(tw, sh, tw), MAT.ivory, c, [tx, ty + sh / 2, 0]);
      add(new THREE.BoxGeometry(tw * 1.1, 0.2, tw * 1.1), MAT.gold, c, [tx, ty + sh, 0]);
      const g = grp(c, [tx, 0, 0]); windowsOnBox(g, tw, tw, ty + sh * 0.12, sh * 0.76, 'nesw', 1);
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) pinnacle(c, tx + sx * tw / 2, ty + sh, sz * tw / 2, Math.min(1.8, sh * 0.25), tw * 0.16);
      ty += sh + 0.2; tw *= 0.86;
    }
    const sp = tw * (mid ? 4.2 : 3.4);                                   // tall four-sided spire with gold ribs and a finial
    add(new THREE.ConeGeometry(tw * 0.72, sp, 4), MAT.ivoryPlain, c, [tx, ty + sp / 2, 0], [0, Math.PI / 4, 0]);
    add(new THREE.CylinderGeometry(tw * 0.55, tw * 0.55, 0.16, 4), MAT.gold, c, [tx, ty + sp * 0.3, 0], [0, Math.PI / 4, 0]);
    finial(c, [tx, ty + sp - 0.2, 0], mid ? 1.4 : 1.0);
    for (const sz of [-1, 1]) {                                         // flying buttress from the hall's eaves up to the tower
      const ox = tx, oz = sz * (cd / 2 + 0.2), top = h0 + 1.2, iz = sz * sw / 2, iy = base + th * 0.45;
      const dz = iz - oz, dy = iy - top, len = Math.hypot(dz, dy);
      add(new THREE.BoxGeometry(0.4, 0.34, len), MAT.ivoryPlain, c, [ox, (iy + top) / 2, (iz + oz) / 2], [Math.atan2(-dy, dz), 0, 0]);   // box z-axis along (dy, dz)
      pinnacle(c, ox, top - 0.6, oz, 1.0, 0.34);
    }
  }
  anchor(c, 0, 0, Math.max(w, d) * 0.6);
  return c;
}
// Round belfry with a ribbed cobalt dome, gold lantern and needle spire.
function domedBelfry(parent, x, z, { r = 3, tiers = [9, 6], domeMat = MAT.cobalt, spire = 5, name = 'Belfry' } = {}) {
  const b = grp(parent, [x, 0, z], 0, name);
  for (let i = 0; i < 4; i++) roundTier(b, r * (0.96 - i * 0.06), 8, -8 * (i + 1), 5, i ? MAT.ivoryShade : MAT.ivory);   // storeys below the floor line
  add(new THREE.TorusGeometry(r * 1.16, 0.18, 6, 32), MAT.gold, b, [0, 0, 0], [Math.PI / 2, 0, 0]);
  let y = 0, cr = r;
  for (const h of tiers) {
    roundTier(b, cr, h, y, 6);
    for (let i = 0; i < 10; i++) { const a = (i + 0.5) / 10 * TAU; pinnacle(b, Math.cos(a) * cr * 1.06, y + h, Math.sin(a) * cr * 1.06, 0.7, 0.22); }   // pinnacle ring on every tier
    y += h + 0.22; cr *= 0.86;
  }
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU; pinnacle(b, Math.cos(a) * cr * 1.05, y - 0.2, Math.sin(a) * cr * 1.05, 1.0, 0.28); }
  ribbedDome(b, cr * 0.95, domeMat, MAT.gold, [0, y, 0], 12);
  const ly = y + cr * 0.92;
  add(new THREE.CylinderGeometry(cr * 0.25, cr * 0.3, cr * 0.5, 8), MAT.gold, b, [0, ly + cr * 0.25, 0]);
  ribbedDome(b, cr * 0.26, MAT.gold, MAT.goldDark, [0, ly + cr * 0.5, 0], 8);
  add(new THREE.ConeGeometry(cr * 0.08, spire, 8), MAT.gold, b, [0, ly + cr * 0.75 + spire / 2, 0]);
  add(new THREE.SphereGeometry(cr * 0.1, 10, 8), MAT.gold, b, [0, ly + cr * 0.78, 0]);
  anchor(b, 0, 0, r * 1.2);
  return b;
}
// Round tower topped with an open gold-columned bell lantern and an armillary sphere.
function armillaryTower(parent, x, z, { r = 2.2, h = 10, sphere = 1.6, name = 'ArmillaryTower' } = {}) {
  const t = grp(parent, [x, 0, z], 0, name);
  for (let i = 0; i < 4; i++) roundTier(t, r * (0.95 - i * 0.06), 7.5, -7.5 * (i + 1), 6, MAT.ivoryShade);
  roundTier(t, r, h, 0, 6);
  const y = h + 0.22;
  add(new THREE.CylinderGeometry(r * 0.95, r * 0.95, 0.3, 20), MAT.ivoryPlain, t, [0, y + 0.15, 0]);
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU; add(new THREE.CylinderGeometry(0.1, 0.12, 2.2, 8), MAT.gold, t, [Math.cos(a) * r * 0.78, y + 1.4, Math.sin(a) * r * 0.78]); }
  add(new THREE.SphereGeometry(r * 0.38, 14, 10, 0, TAU, 0, Math.PI * 0.6), MAT.goldDark, t, [0, y + 2.3, 0], [Math.PI, 0, 0]); // the bell
  add(new THREE.CylinderGeometry(r * 0.95, r * 0.9, 0.35, 20), MAT.gold, t, [0, y + 2.65, 0]);
  for (let i = 0; i < 8; i++) { const a = i / 8 * TAU + 0.2; pinnacle(t, Math.cos(a) * r * 0.9, y + 2.8, Math.sin(a) * r * 0.9, 0.6, 0.18, MAT.ivoryPlain); }
  armillary(t, [0, y + 2.8, 0], sphere);
  anchor(t, 0, 0, r * 1.1);
  return t;
}
// Rotunda: a colonnaded drum, ribbed dome and a ring of small domed turrets.
function rotunda(parent, x, z, { r = 4, domeMat = MAT.gold, centre = MAT.cobalt, turrets = 6, name = 'Rotunda' } = {}) {
  const t = grp(parent, [x, 0, z], 0, name);
  for (let i = 0; i < 3; i++) roundTier(t, r * (1.0 - i * 0.08), 7, -7 * (i + 1), 12, MAT.ivoryShade);
  add(new THREE.CylinderGeometry(r * 1.12, r * 1.12, 0.4, 32), MAT.ivoryPlain, t, [0, 0.2, 0]);
  add(new THREE.TorusGeometry(r * 1.12, 0.1, 6, 40), MAT.gold, t, [0, 0.4, 0], [Math.PI / 2, 0, 0]);
  add(new THREE.CylinderGeometry(r * 0.78, r * 0.78, 3, 24), MAT.shadow, t, [0, 1.9, 0]);
  for (let i = 0; i < 16; i++) { const a = i / 16 * TAU; add(new THREE.CylinderGeometry(0.13, 0.15, 3, 8), MAT.ivoryPlain, t, [Math.cos(a) * r * 0.95, 1.9, Math.sin(a) * r * 0.95]); }
  add(new THREE.CylinderGeometry(r * 1.02, r * 1.0, 0.5, 32), MAT.ivoryPlain, t, [0, 3.65, 0]);
  add(new THREE.CylinderGeometry(r * 1.05, r * 1.05, 0.12, 32), MAT.gold, t, [0, 3.95, 0]);
  ribbedDome(t, r * 0.8, domeMat, MAT.gold, [0, 4.0, 0], 16);
  ribbedDome(t, r * 0.25, centre, MAT.gold, [0, 4.0 + r * 0.78, 0], 8);
  finial(t, [0, 4.0 + r * 1.02, 0], 1.2);
  for (let i = 0; i < turrets; i++) { const a = i / turrets * TAU + 0.3; pavilion(t, Math.cos(a) * r * 1.02, Math.sin(a) * r * 1.02, 0.9).position.y = 0.4; }
  anchor(t, 0, 0, r * 1.15);
  return t;
}
// Slender ivory skybridge with an arch beneath, from p0 to p1 (x, y, z)
function skybridge(parent, p0, p1, w = 1.6) {
  const a = new THREE.Vector3(...p0), b = new THREE.Vector3(...p1), mid = a.clone().add(b).multiplyScalar(0.5), len = a.distanceTo(b);
  const s = grp(parent, mid.toArray(), 0, 'Skybridge'); s.lookAt(b.x, mid.y, b.z);
  add(new THREE.BoxGeometry(w, 0.45, len), MAT.ivoryPlain, s);
  for (const sx of [-1, 1]) { add(new THREE.BoxGeometry(0.18, 0.5, len), MAT.ivoryPlain, s, [sx * (w / 2 - 0.09), 0.45, 0]); add(new THREE.BoxGeometry(0.22, 0.05, len), MAT.gold, s, [sx * (w / 2 - 0.09), 0.72, 0]); }
  const arch = add(new THREE.TorusGeometry(len / 2, 0.3, 6, 24, Math.PI), MAT.ivoryShade, s, [0, -0.2, 0], [0, Math.PI / 2, Math.PI], [1, 0.35, 1]);
  return s;
}

// Landmarks — positions in metres (x east, z south), read from the reference with 1 m = 16.3 px about (437, 325).
const B = gBelfries;
// NW: cathedral mass with windows facing the arena, crowned by an armillary sphere on the drum nearest the rim
cathedral(B, -27, -24, 0.62, { w: 16, d: 8, tiers: [5, 18, 10], name: 'Cathedral_NW' });
cathedral(B, -33, -9, 0.3, { w: 8, d: 5, tiers: [5, 12], name: 'Cathedral_NW2' });
armillaryTower(B, -16.2, -15.2, { r: 2.4, h: 6, sphere: 1.9, name: 'Armillary_NW' });
// NE: mirror with variation
cathedral(B, 28, -25, -0.6, { w: 16, d: 8, tiers: [5, 18, 9], name: 'Cathedral_NE' });
cathedral(B, 34, -11, -0.25, { w: 8, d: 5, tiers: [5, 12], name: 'Cathedral_NE2' });
armillaryTower(B, 16.4, -15.0, { r: 2.4, h: 6, sphere: 1.9, name: 'Armillary_NE' });
// W and E: tall round belfries with cobalt domes, just south of the side bridges
domedBelfry(B, -23, 13.5, { r: 2.8, tiers: [10, 7], name: 'Belfry_W' });
domedBelfry(B, 23, 13.5, { r: 2.8, tiers: [10, 7], name: 'Belfry_E' });
// small gold-domed kiosks just outside the rim on the W and E arcs (north of the side bridges)
for (const [x, z, s] of [[-17.2, -5.6, 1.6], [-19.6, -8.4, 1.3], [17.4, -5.6, 1.6], [19.8, -8.4, 1.3], [-17.6, 3.8, 1.3], [17.6, 3.8, 1.3]]) {
  add(new THREE.CylinderGeometry(0.9 * s, 0.7 * s, 20, 16), MAT.ivoryShade, B, [x, -10, z]);
  for (const y of [-3, -9, -15]) add(new THREE.CylinderGeometry(0.95 * s, 0.95 * s, 0.14, 16), MAT.gold, B, [x, y, z]);
  pavilion(B, x, z, s);
  anchor(B, x, z, 1.2 * s);
}
// SW: armillary tower with its open bell lantern
armillaryTower(B, -15.8, 15.2, { r: 2.3, h: 5, sphere: 1.7, name: 'Occluder_Armillary_SW' });
// bottom corners: rotundas (SW gold dome, SE blue-centred with gold turrets)
rotunda(B, -21.5, 24, { r: 3.2, domeMat: MAT.gold, centre: MAT.cobalt, name: 'Occluder_Rotunda_SW' });
rotunda(B, 19.5, 24.5, { r: 3.4, domeMat: MAT.ivoryPlain, centre: MAT.cobalt, turrets: 8, name: 'Occluder_Rotunda_SE' });
domedBelfry(B, 28, 16, { r: 2.2, tiers: [7, 5], domeMat: MAT.cobalt, spire: 4, name: 'Occluder_Belfry_SE' });
domedBelfry(B, -29, 16, { r: 2.0, tiers: [6, 4], domeMat: MAT.gold, spire: 3, name: 'Occluder_Belfry_SW' });
// corner masses: extra cathedral wings so the top corners read as solid ivory architecture, as in the reference
cathedral(B, -12.5, -33, 0.15, { w: 9, d: 6, tiers: [12, 7], name: 'Cathedral_NW3' });
for (const [x, z, h] of [[-38, -31, 26], [-35, -36, 19], [-42, -27, 15]]) needle(B, x, z, h, 1.1);   // slender spire cluster in the far NW corner
cathedral(B, 12.5, -33, -0.15, { w: 9, d: 6, tiers: [12, 7], name: 'Cathedral_NE3' });
for (const [x, z, h] of [[38, -31, 26], [35, -36, 19], [42, -27, 15]]) needle(B, x, z, h, 1.1);     // and in the far NE corner
// needle spires: thin ivory shafts with gold bands and tall gold tips, clustered in every corner
function needle(parent, x, z, h, w = 0.9) {
  const n = grp(parent, [x, 0, z], 0, 'Needle');
  for (let i = 0; i < 3; i++) roundTier(n, w * (0.92 - i * 0.07), 8, -8 * (i + 1), 6, MAT.ivoryShade);
  roundTier(n, w, h * 0.55, 0, 6);
  roundTier(n, w * 0.78, h * 0.25, h * 0.55 + 0.22, 6);
  const y = h * 0.8 + 0.44;
  for (let i = 0; i < 4; i++) { const a = i / 4 * TAU + Math.PI / 4; pinnacle(n, Math.cos(a) * w * 0.8, y, Math.sin(a) * w * 0.8, 0.8, 0.18); }
  add(new THREE.ConeGeometry(w * 0.7, h * 0.35, 8), MAT.ivoryPlain, n, [0, y + h * 0.175, 0]);
  add(new THREE.CylinderGeometry(w * 0.72, w * 0.72, 0.12, 8), MAT.gold, n, [0, y + h * 0.06, 0]);
  add(new THREE.ConeGeometry(w * 0.12, h * 0.22, 6), MAT.gold, n, [0, y + h * 0.35 + h * 0.11, 0]);
  add(new THREE.SphereGeometry(w * 0.16, 8, 6), MAT.gold, n, [0, y + h * 0.35, 0]);
  anchor(n, 0, 0, w * 1.4);
}
for (const [x, z, h] of [[-20, -28, 24], [-30, -18, 20], [-8.5, -25, 15], [-35, -26, 18], [-27, -34, 22], [-42, -14, 16],
  [20, -28, 24], [30, -18, 20], [8.5, -25, 15], [35, -26, 18], [27, -34, 22], [42, -14, 16], [-31, 0, 14], [31, 0, 14]]) needle(B, x, z, h);
const southNeedles = grp(B, null, 0, 'Occluder_Needles_S');
for (const [x, z, h] of [[-27, 11, 15], [-34, 22, 13], [-12, 21, 9], [-30, 31, 11], [27, 11, 15], [34, 22, 13], [12, 21, 9], [30, 31, 11], [-40, 6, 12], [40, 6, 12]]) needle(southNeedles, x, z, h);
// short arched skybridges inside the corner clusters (top-left and right), as in the reference
skybridge(B, [-36, 9, -28], [-28, 9, -21], 1.4);
skybridge(B, [33, 7, -9], [31, 7, -1.5], 1.3);
// mark camera-side groups so the gameplay view can hide them
B.children.forEach(c => { if (c.name.startsWith('Occluder')) c.userData.occluder = true; });

// ================================================================================================
// 5. Filler spires receding into the clouds, and the far skyline
// ================================================================================================
function spire(parent, x, z, top, w, kind) {
  // No two alike: width, height, window count and banding vary; tops are a cone (0), gold dome (1), cobalt dome (2),
  // a slender gold-tipped needle (3) or a broken stump with fallen-away blocks (4, sparingly, for rhythm).
  const s = grp(parent, [x, 0, z], rnd() * TAU, 'Spire');
  w *= 0.85 + rnd() * 0.3; top += (rnd() - 0.5) * 3;
  if (kind < 3 && rnd() < 0.22) kind = rnd() < 0.7 ? 3 : 4;
  const seg = 12, base = top - 14 - rnd() * 18, h = top - base - w * 3, y = base + h;
  add(new THREE.CylinderGeometry(w * 0.7, w * 0.8, h, seg), MAT.ivory, s, [0, base + h / 2, 0]);
  const bands = 2 + Math.floor(rnd() * 3);
  for (let i = 1; i <= bands; i++) add(new THREE.CylinderGeometry(w * 0.78, w * 0.78, 0.15, seg), MAT.gold, s, [0, base + h * (i / bands) * 0.98, 0]);
  const nw = 3 + Math.floor(rnd() * 4), win = nextWindow();
  for (let i = 0; i < nw; i++) { const a = i / nw * TAU; add(archGeo(w * 0.42, w * 1.9), win, s, [Math.sin(a) * w * 0.71, y - w * 2.5, Math.cos(a) * w * 0.71], [0, a, 0]); }
  if (kind === 0) { add(new THREE.ConeGeometry(w * 0.72, w * 3, seg), MAT.ivoryPlain, s, [0, y + w * 1.5, 0]); add(new THREE.SphereGeometry(w * 0.12, 8, 6), MAT.gold, s, [0, y + w * 3.05, 0]); }
  else if (kind === 1) { ribbedDome(s, w * 0.72, MAT.gold, MAT.goldDark, [0, y, 0], 8); finial(s, [0, y + w * 0.7, 0], w * 0.6); }
  else if (kind === 2) { ribbedDome(s, w * 0.72, MAT.cobalt, MAT.gold, [0, y, 0], 8); add(new THREE.ConeGeometry(w * 0.08, w * 2, 6), MAT.gold, s, [0, y + w * 1.6, 0]); }
  else if (kind === 3) { add(new THREE.ConeGeometry(w * 0.45, w * 6, seg), MAT.ivoryPlain, s, [0, y + w * 3, 0]); add(new THREE.CylinderGeometry(w * 0.5, w * 0.5, 0.12, seg), MAT.gold, s, [0, y + w * 1.2, 0]); add(new THREE.ConeGeometry(w * 0.07, w * 1.4, 6), MAT.gold, s, [0, y + w * 6.6, 0]); }
  else for (let i = 0; i < 5; i++) { const a = rnd() * TAU, bs = w * (0.25 + rnd() * 0.3); add(new THREE.BoxGeometry(bs, bs * (1 + rnd()), bs), MAT.ivoryShade, s, [Math.cos(a) * w * 0.45, y + bs * 0.4, Math.sin(a) * w * 0.45], [rnd(), rnd(), rnd()]); }
  anchor(s, 0, 0, w * 1.2);
}
// clusters between the landmarks (x, z, top height, width, kind 0 cone / 1 gold dome / 2 cobalt dome)
const FILLER = [[-30, -30, 14, 1.6, 0], [-37, -20, 10, 1.4, 1], [-13, -28, 6, 1.2, 1], [-21, -31, 16, 1.5, 0], [13, -28, 6, 1.2, 1], [21, -31, 16, 1.5, 0], [30, -30, 14, 1.6, 0], [37, -20, 10, 1.4, 1],
  [-38, -3, 8, 1.6, 0], [-36, 4, 14, 1.4, 1], [38, -3, 8, 1.6, 0], [37, 4, 14, 1.4, 1], [-33, 11, 6, 1.4, 1], [33, 11, 6, 1.4, 1],
  [-9, 23, -6, 1.3, 1], [8, 22, -4, 1.3, 1], [12, 26, -7, 1.2, 0], [-12, 28, -9, 1.2, 0], [16, 33, -5, 1.4, 1], [-16, 33, -4, 1.4, 2], [31, 27, 3, 1.6, 0], [-31, 27, 2, 1.6, 0],
  [8, 32, -10, 1.2, 2], [-6, 34, -12, 1.1, 1], [-26, 34, -3, 1.4, 0], [26, 35, -2, 1.4, 0], [-10, -21, -6, 1.0, 1], [10, -21, -6, 1.0, 1], [-40, 18, 4, 1.5, 0], [40, 18, 4, 1.5, 0]];
for (const [x, z, top, w, kind] of FILLER) if (top > -3) spire(gSpires, x, z, top, w, kind === 1 && top < 8 ? 3 : kind);   // low ones rise as pointed tips; ones buried in the fade are left out
for (let i = 0; i < 40; i++) {                     // far ring, deep in the abyss
  const a = rnd() * TAU, d = 48 + rnd() * 60;
  spire(gAbyss, Math.cos(a) * d, Math.sin(a) * d, -6 - rnd() * 22 + (d > 80 ? 10 : 0), 1.4 + rnd() * 1.6, Math.floor(rnd() * 3));
}
// Distant skyline (110–200 m): floating cathedral islands, simple but many, left to the shader's aerial perspective
// (pale and blue) so they add depth past the near ring without competing with it.
function skylineIsland(x, z, y, s) {
  const isl = grp(gAbyss, [x, y, z], rnd() * TAU, 'Island');
  add(new THREE.CylinderGeometry(7 * s, 6 * s, 2 * s, 12), MAT.ivoryShade, isl, [0, -s, 0]);
  add(new THREE.ConeGeometry(6 * s, 14 * s, 10), MAT.ivoryShade, isl, [0, -9 * s, 0], [Math.PI, 0, 0]);
  add(new THREE.TorusGeometry(7 * s, 0.25 * s, 6, 24), MAT.gold, isl, [0, 0, 0], [Math.PI / 2, 0, 0]);
  const n = 3 + Math.floor(rnd() * 3);
  for (let i = 0; i < n; i++) {
    const b = rnd() * TAU, rr = i ? 2 + rnd() * 3.5 * s : 0, ht = (i ? 8 + rnd() * 10 : 18 + rnd() * 12) * s, ww = (i ? 0.9 : 1.8) * s;
    add(new THREE.CylinderGeometry(ww, ww * 1.1, ht, 10), MAT.ivory, isl, [Math.cos(b) * rr, ht / 2, Math.sin(b) * rr]);
    add(new THREE.CylinderGeometry(ww * 1.1, ww * 1.1, 0.25 * s, 10), MAT.gold, isl, [Math.cos(b) * rr, ht * 0.7, Math.sin(b) * rr]);
    if (rnd() < 0.6) add(new THREE.ConeGeometry(ww, ww * 4, 10), MAT.ivoryPlain, isl, [Math.cos(b) * rr, ht + ww * 2, Math.sin(b) * rr]);
    else add(new THREE.SphereGeometry(ww, 12, 6, 0, TAU, 0, Math.PI / 2), rnd() < 0.5 ? MAT.cobalt : MAT.gold, isl, [Math.cos(b) * rr, ht, Math.sin(b) * rr]);
  }
}
for (let i = 0; i < 18; i++) { const a = (i + rnd() * 0.6) / 18 * TAU, d = 110 + rnd() * 90; skylineIsland(Math.cos(a) * d, Math.sin(a) * d, -10 + rnd() * 30, 0.8 + rnd() * 0.7); }

// ================================================================================================
// Sky, clouds and lights (scene-only, not exported)
// ================================================================================================
const SUN = new THREE.Vector3(-0.34, 0.92, -0.18).normalize();  // high in the north-west: short shadows falling to the lower right
const skyMat = new THREE.ShaderMaterial({ side: THREE.BackSide, depthWrite: false, fog: false, uniforms: { sun: { value: SUN } },
  vertexShader: 'varying vec3 vD; void main(){ vD = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.); }',
  fragmentShader: `varying vec3 vD; uniform vec3 sun;
    void main(){ float h = vD.y, s = max(dot(vD, sun), 0.);
      vec3 zen = vec3(0.20,0.42,0.84), mid = vec3(0.42,0.63,0.93), hor = vec3(0.81,0.89,0.98), low = vec3(0.48,0.66,0.93);
      vec3 c = h > 0. ? mix(mix(hor, mid, smoothstep(0.,0.28,h)), zen, smoothstep(0.28,0.95,h)) : mix(hor, low, smoothstep(0.,-0.35,h));
      c += vec3(1.0,0.95,0.85) * pow(s, 10.) * 0.25;
      gl_FragColor = vec4(pow(c, vec3(2.2)), 1.); }` });
scene.add(new THREE.Mesh(new THREE.SphereGeometry(800, 48, 24), skyMat));
scene.fog = new THREE.Fog(0xbcd3f3, 70, 420);
let sun = null;
if (opts.lights !== false) {
  scene.add(new THREE.HemisphereLight(0xf2ecdf, 0xb9ac92, 0.75));
  sun = new THREE.DirectionalLight(0xfff2de, 2.0); sun.position.copy(SUN).multiplyScalar(80); sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096); Object.assign(sun.shadow.camera, { left: -45, right: 45, top: 45, bottom: -45, near: 1, far: 220 });
  sun.shadow.bias = -0.0004; sun.shadow.normalBias = 0.03; scene.add(sun);
  const fill = new THREE.DirectionalLight(0xa9c6ff, 0.4); fill.position.set(20, 10, 25); scene.add(fill);
  const pm = new THREE.PMREMGenerator(opts.renderer); const es = new THREE.Scene(); es.add(new THREE.Mesh(new THREE.SphereGeometry(50, 32, 16), skyMat));
  const warm = new THREE.Mesh(new THREE.PlaneGeometry(60, 60), new THREE.MeshBasicMaterial({ color: 0xfff1d0, side: THREE.DoubleSide })); warm.position.y = -20; warm.rotation.x = Math.PI / 2; es.add(warm);
  if (opts.renderer) scene.environment = pm.fromScene(es, 0.02).texture;
}
const clouds = [];
if (opts.clouds !== false) {
  const ct = [paintCloud(256, 3), paintCloud(256, 8), paintCloud(256, 13)];
  const puff = (a, d, y, s, op) => {
    const sp = new THREE.Sprite(new THREE.SpriteMaterial({ map: ct[Math.floor(rnd() * 3)], transparent: true, opacity: op, depthWrite: false, fog: true }));
    sp.position.set(Math.cos(a) * d, y, Math.sin(a) * d); sp.scale.set(s, s * 0.6, 1); scene.add(sp); clouds.push([sp, a, d, 0.002 + rnd() * 0.004]);
  };
  for (let i = 0; i < 120; i++) puff(rnd() * TAU, 18 + rnd() * 200, -28 - rnd() * 30, 18 + rnd() * 34, 0.8 + rnd() * 0.2);   // the cloud sea: dense banks, sky showing between
  for (let i = 0; i < 70; i++) puff(rnd() * TAU, 20 + rnd() * 34, -14 - rnd() * 16, 9 + rnd() * 12, 0.9);                 // puffs round the tower bases
  for (let i = 0; i < 30; i++) puff(rnd() * TAU, 200 + rnd() * 200, 20 + rnd() * 80, 60 + rnd() * 80, 0.6);
  const [sc, sg] = canvas2d(512), grd = sg.createRadialGradient(256, 256, 0, 256, 256, 256);
  grd.addColorStop(0, '#5f92e2'); grd.addColorStop(0.6, '#86b0ec'); grd.addColorStop(1, '#cfe1f8'); sg.fillStyle = grd; sg.fillRect(0, 0, 512, 512);
  const sea = new THREE.Mesh(new THREE.CircleGeometry(900, 48), new THREE.MeshBasicMaterial({ map: tex(sc), fog: false })); sea.position.y = -75; sea.rotation.x = -Math.PI / 2; scene.add(sea);
}

// ================================================================================================
// Merge static meshes per material inside each top-level group (fewer draw calls; smaller glTF)
// ================================================================================================
const armRings = [], occluders = [];
function mergeGroup(group) {
  if (!opts.mergeGeometries) return;
  group.updateMatrixWorld(true);
  const inv = new THREE.Matrix4().copy(group.matrixWorld).invert(), buckets = new Map(), drop = [];
  group.traverse(o => {
    if (!o.isMesh || o.userData.keep || o.userData.dynamic) return;
    for (let p = o.parent; p && p !== group; p = p.parent) if (p.name === 'ArmillaryRing' || p.userData.occluder) return;
    let geo = o.geometry.index ? o.geometry.toNonIndexed() : o.geometry.clone();
    for (const k of Object.keys(geo.attributes)) if (!['position', 'normal', 'uv'].includes(k)) geo.deleteAttribute(k);
    if (!geo.attributes.uv) geo.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(geo.attributes.position.count * 2), 2));
    geo.applyMatrix4(new THREE.Matrix4().multiplyMatrices(inv, o.matrixWorld));
    if (!buckets.has(o.material)) buckets.set(o.material, []); buckets.get(o.material).push(geo); drop.push(o);
  });
  drop.forEach(o => o.parent.remove(o));
  for (const [mat, geos] of buckets) {
    const m = new THREE.Mesh(opts.mergeGeometries(geos, false), mat); m.name = `${group.name}_${mat.name}`; m.castShadow = m.receiveShadow = true; group.add(m);
  }
  // prune empty groups left behind
  const empty = []; group.traverse(o => { if (o !== group && !o.isMesh && o.children.length === 0 && o.name !== 'ArmillaryRing') empty.push(o); }); empty.forEach(o => o.parent && o.parent.remove(o));
}
// occluder groups merge on their own, so they stay separate nodes that can be hidden
gBelfries.children.filter(c => c.userData.occluder).forEach(c => { mergeGroup(c); c.userData.occluder = true; occluders.push(c); });
[gRim, gBridges, gBelfries, gSpires, gAbyss].forEach(mergeGroup);
root.traverse(o => { if (o.name === 'ArmillaryRing') armRings.push(o); });

function update(t, dt, quiet = false) {
  floorMat.emissiveIntensity += ((quiet ? 0 : 0.12) - floorMat.emissiveIntensity) * Math.min(1, dt * 4);
  for (const b of banners) b.rotation.y = Math.sin(t * 1.2 + b.position.x * 0.7) * 0.06;
  for (const b of rimBanners) b.rotation.y = b.userData.ry + Math.sin(t * 1.1 + b.position.z * 0.5) * 0.05;
  for (const r of armRings) r.rotation.y = t * 0.25 * (r.userData.spin || 1);
  for (const c of clouds) { c[1] += c[3] * dt; c[0].position.x = Math.cos(c[1]) * c[2]; c[0].position.z = Math.sin(c[1]) * c[2]; }
}
function setOccludersVisible(v) { occluders.forEach(o => o.visible = v); }
return { root, update, floorMat, floorTex, sun, setOccludersVisible, R };
};
