// <vanguard-model> — source of truth; copied into index.html and mockup.html by sync-model.ps1.
// Vanguard baseline from reference/LAST-BELL-VANGUARD-MOCKUP.png: blue hood with cyan eyes, faceted
// gold-over-navy armour, layered pauldrons, cyan diamond runes, blue tabards and cape, crystal cyan
// sword with gold crossguard, gold-rimmed heater shield with a glowing spiral rune.
// Needs THREE plus the page's M(color, opts) lit-material factory and G(color, k) glow factory.
// Faces +Z, feet at y = 0, about 1.5 units tall. The sword arm is the character's right (-X).
function facetGeo(geo) { const g = geo.index ? geo.toNonIndexed() : geo; g.computeVertexNormals(); return g; }
const VANGUARD_RUNE_TEX = (() => {
  const c = document.createElement('canvas'); c.width = c.height = 256; const g = c.getContext('2d');
  g.translate(128, 128); g.lineCap = 'square'; g.lineJoin = 'miter';
  const spiral = [[0, 0], [22, 0], [22, 22], [-30, 22], [-30, -34], [46, -34], [46, 50], [-62, 50], [-62, -66], [70, -66]];
  for (const [w, a] of [[30, 0.25], [18, 0.6], [9, 1]]) { g.strokeStyle = `rgba(190,248,255,${a})`; g.lineWidth = w; g.beginPath(); spiral.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y)); g.stroke(); }
  g.fillStyle = '#e8fcff'; g.beginPath(); g.moveTo(58, 74); g.lineTo(84, 74); g.lineTo(71, 50); g.closePath(); g.fill();
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
})();
function buildVanguardModel(M, G, palette = {}) {
  const P = { cloth: 0x2050cc, clothDark: 0x163891, navy: 0x1b2343, navy2: 0x283258, gold: 0xd9a64c, goldDark: 0xa9772f, glow: 0x5fe3ff, face: 0x04060d, ...palette };
  const DS = THREE.DoubleSide;
  const cloth = M(P.cloth, { side: DS }), clothD = M(P.clothDark, { side: DS }), navy = M(P.navy), navy2 = M(P.navy2);
  const gold = M(P.gold, { metal: 1 }), goldD = M(P.goldDark, { metal: 1 }), glow = G(P.glow, 2.6), glowSoft = G(P.glow, 1.6);
  const mk = (geo, mat, parent, pos, rot, scale) => { const m = new THREE.Mesh(geo, mat); m.castShadow = true; m.receiveShadow = true; if (pos) m.position.set(...pos); if (rot) m.rotation.set(...rot); if (scale) m.scale.set(...scale); parent.add(m); return m; };
  const grp = (parent, x = 0, y = 0, z = 0) => { const g = new THREE.Group(); g.position.set(x, y, z); parent.add(g); return g; };
  const F = facetGeo;
  const root = new THREE.Group(), body = grp(root);

  // ---- legs: navy thigh, gold knee, faceted gold greave, navy boot with gold toe ----
  const leg = side => {
    const hip = grp(body, side * 0.13, 0.62, 0);
    mk(F(new THREE.CylinderGeometry(0.11, 0.094, 0.3, 6)), navy, hip, [0, -0.16, 0]);
    mk(F(new THREE.BoxGeometry(0.14, 0.17, 0.05)), gold, hip, [side * 0.01, -0.1, 0.1], [-0.12, 0, side * 0.08]);
    mk(F(new THREE.OctahedronGeometry(0.095)), gold, hip, [0, -0.33, 0.06], null, [1.05, 0.9, 0.85]);
    mk(F(new THREE.CylinderGeometry(0.105, 0.078, 0.26, 5)), gold, hip, [0, -0.47, 0.012]);
    mk(F(new THREE.BoxGeometry(0.04, 0.2, 0.03)), goldD, hip, [0, -0.46, 0.105]);
    mk(F(new THREE.BoxGeometry(0.16, 0.09, 0.27)), navy, hip, [0, -0.595, 0.04]);
    mk(F(new THREE.ConeGeometry(0.08, 0.11, 4)), gold, hip, [0, -0.595, 0.185], [Math.PI / 2, Math.PI / 4, 0]);
    return hip;
  };
  const legL = leg(1), legR = leg(-1);

  // ---- hips: belt with glowing buckle, gold faulds, blue tabards front and back ----
  mk(F(new THREE.CylinderGeometry(0.205, 0.2, 0.075, 8)), navy2, body, [0, 0.665, 0]);
  mk(F(new THREE.OctahedronGeometry(0.055)), gold, body, [0, 0.665, 0.2], null, [1, 1, 0.5]);
  mk(F(new THREE.OctahedronGeometry(0.028)), glow, body, [0, 0.665, 0.225], null, [1, 1, 0.5]);
  for (const s of [-1, 1]) mk(F(new THREE.BoxGeometry(0.14, 0.17, 0.2)), gold, body, [s * 0.2, 0.56, 0], [0, 0, s * 0.35]);
  const tabardShape = (w, h) => { const t = new THREE.Shape(); t.moveTo(-w, 0); t.lineTo(w, 0); t.lineTo(w * 0.8, -h * 0.85); t.lineTo(0, -h); t.lineTo(-w * 0.8, -h * 0.85); t.closePath(); return t; };
  for (const [z, ry] of [[0.17, 0], [-0.16, Math.PI]]) {
    const tg = grp(body, 0, 0.64, z); tg.rotation.set(0.08 * (z > 0 ? 1 : -1), ry, 0);
    mk(F(new THREE.ExtrudeGeometry(tabardShape(0.13, 0.36), { depth: 0.012, bevelEnabled: false })), gold, tg, [0, 0.005, -0.004]);
    mk(F(new THREE.ExtrudeGeometry(tabardShape(0.11, 0.32), { depth: 0.012, bevelEnabled: false })), cloth, tg, [0, -0.005, 0.006]);
  }

  // ---- torso: navy core, faceted gold breastplate, cyan diamond emblem ----
  const torso = grp(body, 0, 0.68, 0);
  mk(F(new THREE.CylinderGeometry(0.235, 0.19, 0.42, 6)), navy, torso, [0, 0.2, 0], null, [1, 1, 0.78]);
  mk(F(new THREE.CylinderGeometry(0.275, 0.205, 0.35, 6, 1, false, -Math.PI / 2, Math.PI)), gold, torso, [0, 0.24, 0.01], null, [1, 1, 0.9]);
  mk(F(new THREE.BoxGeometry(0.2, 0.06, 0.06)), goldD, torso, [0, 0.03, 0.14]);
  mk(F(new THREE.OctahedronGeometry(0.075)), goldD, torso, [0, 0.27, 0.235], null, [1, 1.15, 0.45]);
  mk(F(new THREE.OctahedronGeometry(0.048)), glow, torso, [0, 0.27, 0.258], null, [1, 1.15, 0.4]);
  // high cowl / scarf over the shoulders
  mk(F(new THREE.CylinderGeometry(0.12, 0.25, 0.15, 7)), clothD, torso, [0, 0.44, -0.01]);

  // ---- pauldrons: three stacked faceted gold plates per side ----
  for (const s of [-1, 1]) {
    const pg = grp(torso, s * 0.3, 0.39, 0); pg.rotation.z = s * -0.45; pg.scale.setScalar(1.12);
    [[0.19, 0, 0], [0.165, 0.05, -0.07], [0.14, 0.09, -0.13]].forEach(([r, dx, dy], i) => {
      mk(F(new THREE.SphereGeometry(r, 6, 3, 0, Math.PI * 2, 0, Math.PI / 2)), i === 1 ? goldD : gold, pg, [s * dx, dy, 0], null, [1.15, 0.75, 1.1]);
    });
    mk(F(new THREE.ConeGeometry(0.035, 0.1, 4)), gold, pg, [s * 0.05, 0.12, 0], [0, 0, s * -0.5]);
  }

  // ---- arms: navy sleeve, gold elbow, flared faceted gauntlet ----
  const arm = side => {
    const sh = grp(torso, side * 0.335, 0.31, 0);
    mk(F(new THREE.CylinderGeometry(0.09, 0.078, 0.22, 6)), navy, sh, [0, -0.12, 0]);
    mk(F(new THREE.OctahedronGeometry(0.078)), gold, sh, [0, -0.25, 0.01]);
    mk(F(new THREE.CylinderGeometry(0.118, 0.082, 0.18, 6)), gold, sh, [0, -0.34, 0]);
    mk(F(new THREE.BoxGeometry(0.105, 0.09, 0.115)), navy2, sh, [0, -0.45, 0.01]);
    return sh;
  };
  const armR = arm(-1), armL = arm(1);

  // ---- hood: faceted blue hood open at the front, void face, slanted glowing eyes ----
  const head = grp(torso, 0, 0.6, 0.01);
  mk(F(new THREE.SphereGeometry(0.19, 8, 6, Math.PI / 2 + 0.6, Math.PI * 2 - 1.2, 0, Math.PI * 0.84)), cloth, head, [0, 0, 0], null, [1.05, 1.08, 1.12]);
  mk(F(new THREE.ConeGeometry(0.075, 0.16, 6)), cloth, head, [0, 0.07, -0.18], [-2.1, 0, 0]);
  mk(new THREE.SphereGeometry(0.135, 10, 8), M(P.face), head, [0, -0.01, 0.035], null, [1, 1.05, 0.8]);
  for (const s of [-1, 1]) mk(new THREE.BoxGeometry(0.055, 0.017, 0.01), glow, head, [s * 0.048, 0.0, 0.142], [0, s * 0.25, s * 0.28]);

  // ---- cape: long, faceted, angular hem ----
  const capeGeo = new THREE.PlaneGeometry(0.7, 1.0, 8, 6);
  const cp = capeGeo.attributes.position;
  for (let i = 0; i < cp.count; i++) {
    const x = cp.getX(i), y = cp.getY(i), t = (0.5 - y);           // 0 top .. 1 bottom
    const hem = Math.abs(y + 0.5) < 1e-3 ? (Math.round((x + 0.35) / 0.0875) % 2 ? -0.08 : 0.02) : 0;
    cp.setXYZ(i, x * (0.72 + t * 0.6), y + hem, -Math.pow(Math.max(0, t), 1.5) * 0.28 - x * x * 0.5 + Math.sin(x * 17) * 0.065 * Math.min(1, t * 1.6));
  }
  capeGeo.translate(0, -0.5, 0);
  const cape = mk(F(capeGeo), M(P.capeTone ?? 0x14306f, { side: DS }), torso, [0, 0.43, -0.19]);

  // ---- sword: crystal cyan blade with rune strips, angular gold crossguard ----
  const sword = grp(armR, 0, -0.47, 0.03);
  const bs = new THREE.Shape(); bs.moveTo(0, 0); bs.lineTo(0.055, 0.07); bs.lineTo(0.048, 0.82); bs.lineTo(0, 0.96); bs.lineTo(-0.048, 0.82); bs.lineTo(-0.055, 0.07); bs.closePath();
  const bladeGeo = F(new THREE.ExtrudeGeometry(bs, { depth: 0.016, bevelEnabled: true, bevelThickness: 0.012, bevelSize: 0.012, bevelSegments: 1 })); bladeGeo.translate(0, 0, -0.008);
  const blade = mk(bladeGeo, M(0x3aa8ff, { emissive: 0x1a78d6, ei: 1.15 }), sword, [0, 0, 0.12], [Math.PI / 2, 0, 0]);
  const runes = [];
  for (let i = 0; i < 4; i++) runes.push(mk(new THREE.BoxGeometry(0.018, 0.018, 0.09), glow, sword, [0, 0.022, 0.26 + i * 0.17]));
  mk(F(new THREE.BoxGeometry(0.008, 0.03, 0.8)), glowSoft, sword, [0, 0, 0.55]);
  for (const s of [-1, 1]) mk(F(new THREE.BoxGeometry(0.16, 0.05, 0.05)), gold, sword, [s * 0.09, 0, 0.11], [0, 0, s * 0.25]);
  mk(F(new THREE.OctahedronGeometry(0.045)), gold, sword, [0, 0, 0.11]);
  mk(F(new THREE.OctahedronGeometry(0.022)), glow, sword, [0, 0.03, 0.11]);
  mk(F(new THREE.CylinderGeometry(0.022, 0.022, 0.14, 6)), navy2, sword, [0, 0, 0.03], [Math.PI / 2, 0, 0]);
  mk(F(new THREE.OctahedronGeometry(0.03)), gold, sword, [0, 0, -0.05]);

  // ---- shield: gold-rimmed heater shield, navy face, glowing spiral rune ----
  const shield = grp(armL, 0.08, -0.4, 0.1); shield.rotation.set(0, 0.55, 0);
  const heater = k => { const s = new THREE.Shape(); s.moveTo(-0.27 * k, 0.32 * k); s.lineTo(0.27 * k, 0.32 * k); s.lineTo(0.29 * k, 0.06 * k); s.quadraticCurveTo(0.26 * k, -0.26 * k, 0, -0.44 * k); s.quadraticCurveTo(-0.26 * k, -0.26 * k, -0.29 * k, 0.06 * k); s.closePath(); return s; };
  mk(F(new THREE.ExtrudeGeometry(heater(1), { depth: 0.04, bevelEnabled: true, bevelThickness: 0.02, bevelSize: 0.02, bevelSegments: 1, curveSegments: 4 })), gold, shield, [0, 0, -0.02]);
  mk(F(new THREE.ExtrudeGeometry(heater(0.84), { depth: 0.02, bevelEnabled: true, bevelThickness: 0.01, bevelSize: 0.01, bevelSegments: 1, curveSegments: 4 })), navy, shield, [0, 0.005, 0.035]);
  const rune = mk(new THREE.PlaneGeometry(0.36, 0.36), new THREE.MeshBasicMaterial({ map: VANGUARD_RUNE_TEX, color: new THREE.Color(P.glow).multiplyScalar(2.2), transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }), shield, [0, -0.02, 0.07]);
  rune.castShadow = false;
  for (const [x, y] of [[-0.2, 0.24], [0.2, 0.24], [0, -0.33]]) mk(F(new THREE.OctahedronGeometry(0.025)), goldD, shield, [x, y, 0.06]);

  return { root, body, torso, head, legL, legR, armL, armR, cape, weapon: sword, shield, glow: [blade, rune, ...runes], kind: 'vanguard', height: 1.5 };
}
// </vanguard-model>
