// Bellkeeper Ultimate v2: mechanic cards (ULTIMATE-PROMPT-v2.md Part 2). Writes one SVG per mechanic plus the phase
// timeline into the folder given as argv[2]; render-cards.cmd turns them into PNGs with headless Chrome.
// Top-down Throne (13 m), north up. Colours: coral = live danger, ivory = memory / future, brass = plates and gears,
// cobalt = personal rule glyphs, red = fail states. Players: 1 blue V, 2 green C, 3 violet A, 4 cyan V.
const fs = require('fs'), path = require('path');
const out = process.argv[2] || '.';
fs.mkdirSync(out, { recursive: true });

const C = {
  bg: '#10162a', panel: '#182039', floor: '#212a47', dial: '#3a4466', brass: '#c99a4b', ivory: '#efe6cf', coral: '#ff6a4d',
  red: '#ff2f3c', cobalt: '#5d7ee8', text: '#e9e3d3', dim: '#8d93ad', ok: '#7bd88f',
  p: ['#4f8fff', '#47c98c', '#ad7dff', '#3fd0e0'], cls: ['V', 'C', 'A', 'V'],
};
const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;');

// ---------------- panel drawing (world metres -> px) ----------------
function Panel(x, y, size, title) {
  const s = (size / 2 - 14) / 13, cx = x + size / 2, cy = y + size / 2 + 8;
  const P = (wx, wy) => [cx + wx * s, cy - wy * s];
  const parts = [];
  const add = t => parts.push(t);
  const api = {
    P, s,
    arena() {
      add(`<rect x="${x}" y="${y}" width="${size}" height="${size + 16}" rx="10" fill="${C.panel}"/>`);
      add(`<text x="${x + 12}" y="${y + 22}" fill="${C.text}" font-size="17" font-weight="700">${esc(title)}</text>`);
      add(`<circle cx="${cx}" cy="${cy}" r="${13 * s}" fill="${C.floor}" stroke="#55608a" stroke-width="2"/>`);
      add(`<circle cx="${cx}" cy="${cy}" r="${9 * s}" fill="none" stroke="${C.dial}" stroke-width="1.5" stroke-dasharray="3 5"/>`);
      ['XII', 'III', 'VI', 'IX'].forEach((n, i) => { const a = i * Math.PI / 2; const [px, py] = P(Math.sin(a) * 10.6, Math.cos(a) * 10.6); add(`<text x="${px}" y="${py + 5}" fill="#6f7899" font-size="12" text-anchor="middle">${n}</text>`); });
      ['NE', 'SE', 'SW', 'NW'].forEach((n, i) => { const a = (45 + 90 * i) * Math.PI / 180; const [px, py] = P(Math.sin(a) * 12.8, Math.cos(a) * 12.8); add(`<circle cx="${px}" cy="${py}" r="5" fill="${C.brass}" opacity="0.7"/>`); });
      return api;
    },
    boss() { const [px, py] = P(0, 0); add(`<circle cx="${px}" cy="${py}" r="${1.4 * s}" fill="#6b5428" stroke="${C.brass}" stroke-width="2.5"/><text x="${px}" y="${py + 4}" fill="${C.ivory}" font-size="11" text-anchor="middle">BK</text>`); return api; },
    circle(wx, wy, r, kind = 'coral', label) {
      const [px, py] = P(wx, wy); add(`<circle cx="${px}" cy="${py}" r="${r * s}" ${style(kind)}/>`);
      if (label) add(`<text x="${px}" y="${py + 4}" fill="${kind === 'brass' ? '#2a1d08' : C.ivory}" font-size="12" font-weight="700" text-anchor="middle">${esc(label)}</text>`);
      return api;
    },
    ring(r1, r2, kind = 'coral', wx = 0, wy = 0) {
      const [px, py] = P(wx, wy); const a = r1 * s, b = r2 * s;
      add(`<path d="M ${px - b} ${py} a ${b} ${b} 0 1 0 ${2 * b} 0 a ${b} ${b} 0 1 0 ${-2 * b} 0 M ${px - a} ${py} a ${a} ${a} 0 1 1 ${2 * a} 0 a ${a} ${a} 0 1 1 ${-2 * a} 0" fill-rule="evenodd" ${style(kind)}/>`);
      return api;
    },
    cone(wx, wy, yawDeg, half, r, kind = 'coral') {
      const [px, py] = P(wx, wy); const a0 = (yawDeg - half) * Math.PI / 180, a1 = (yawDeg + half) * Math.PI / 180;
      const [x0, y0] = P(wx + Math.sin(a0) * r, wy + Math.cos(a0) * r), [x1, y1] = P(wx + Math.sin(a1) * r, wy + Math.cos(a1) * r);
      add(`<path d="M ${px} ${py} L ${x0} ${y0} A ${r * s} ${r * s} 0 ${2 * half > 180 ? 1 : 0} 1 ${x1} ${y1} Z" ${style(kind)}/>`);
      return api;
    },
    half(yawDeg, kind = 'coral') { return api.cone(0, 0, yawDeg, 90, 13, kind); },
    lane(ax, ay, bx, by, w, kind = 'coral') {
      const dx = bx - ax, dy = by - ay, l = Math.hypot(dx, dy) || 1, nx = -dy / l * w / 2, ny = dx / l * w / 2;
      const pts = [[ax + nx, ay + ny], [bx + nx, by + ny], [bx - nx, by - ny], [ax - nx, ay - ny]].map(p => P(...p).join(',')).join(' ');
      add(`<polygon points="${pts}" ${style(kind)}/>`); return api;
    },
    tether(ax, ay, bx, by, color = C.brass, label, dash) {
      const [x0, y0] = P(ax, ay), [x1, y1] = P(bx, by);
      add(`<line x1="${x0}" y1="${y0}" x2="${x1}" y2="${y1}" stroke="${color}" stroke-width="3.5" ${dash ? 'stroke-dasharray="6 5"' : ''}/>`);
      if (label) add(`<text x="${(x0 + x1) / 2 + 6}" y="${(y0 + y1) / 2 - 6}" fill="${color}" font-size="12" font-weight="700">${esc(label)}</text>`);
      return api;
    },
    arrow(ax, ay, bx, by, color = C.ivory) {
      const [x0, y0] = P(ax, ay), [x1, y1] = P(bx, by); const a = Math.atan2(y1 - y0, x1 - x0), h = 9;
      add(`<line x1="${x0}" y1="${y0}" x2="${x1 - Math.cos(a) * 4}" y2="${y1 - Math.sin(a) * 4}" stroke="${color}" stroke-width="2.5"/>`);
      add(`<polygon points="${x1},${y1} ${x1 - h * Math.cos(a - 0.45)},${y1 - h * Math.sin(a - 0.45)} ${x1 - h * Math.cos(a + 0.45)},${y1 - h * Math.sin(a + 0.45)}" fill="${color}"/>`);
      return api;
    },
    player(i, wx, wy, glyph, dead) {
      const [px, py] = P(wx, wy);
      add(`<circle cx="${px}" cy="${py}" r="10" fill="${C.p[i - 1]}" stroke="${dead ? C.red : '#0b0f1c'}" stroke-width="${dead ? 3 : 2}"/><text x="${px}" y="${py + 4}" fill="#0b0f1c" font-size="11" font-weight="800" text-anchor="middle">${i}</text>`);
      if (glyph) add(`<text x="${px}" y="${py - 15}" fill="${glyph.startsWith('!') ? C.cobalt : C.ivory}" font-size="12" font-weight="700" text-anchor="middle">${esc(glyph.replace(/^!/, ''))}</text>`);
      if (dead) add(`<text x="${px + 9}" y="${py + 17}" fill="${C.red}" font-size="16" font-weight="900">✕</text>`);
      return api;
    },
    ghost(i, wx, wy, label) {
      const [px, py] = P(wx, wy);
      add(`<circle cx="${px}" cy="${py}" r="10" fill="none" stroke="${C.ivory}" stroke-width="2" stroke-dasharray="3 3"/><text x="${px}" y="${py + 4}" fill="${C.ivory}" font-size="10" text-anchor="middle">${i}</text>`);
      if (label) add(`<text x="${px}" y="${py - 14}" fill="${C.ivory}" font-size="11" text-anchor="middle">${esc(label)}</text>`);
      return api;
    },
    mark(wx, wy, t, color = C.ivory, size = 13) { const [px, py] = P(wx, wy); add(`<text x="${px}" y="${py + 4}" fill="${color}" font-size="${size}" font-weight="800" text-anchor="middle">${esc(t)}</text>`); return api; },
    note(t, color = C.dim, line = 0) { add(`<text x="${x + 12}" y="${y + size + 6 - 4 - line * 16}" fill="${color}" font-size="13">${esc(t)}</text>`); return api; },
    fail(wx, wy, t) { const [px, py] = P(wx, wy); add(`<text x="${px}" y="${py}" fill="${C.red}" font-size="13" font-weight="800" text-anchor="middle">${esc(t)}</text>`); return api; },
    svg() { return parts.join('\n'); },
  };
  return api;
}
function style(kind) {
  switch (kind) {
    case 'coral': return `fill="${C.coral}" fill-opacity="0.38" stroke="${C.coral}" stroke-width="2.5"`;
    case 'memory': return `fill="${C.ivory}" fill-opacity="0.06" stroke="${C.ivory}" stroke-width="2" stroke-dasharray="7 5"`;
    case 'future2': return `fill="${C.brass}" fill-opacity="0.08" stroke="${C.brass}" stroke-width="2" stroke-dasharray="7 5"`;
    case 'brass': return `fill="${C.brass}" fill-opacity="0.85" stroke="#f0c878" stroke-width="2"`;
    case 'safe': return `fill="${C.ok}" fill-opacity="0.16" stroke="${C.ok}" stroke-width="2" stroke-dasharray="4 4"`;
    case 'fail': return `fill="${C.red}" fill-opacity="0.30" stroke="${C.red}" stroke-width="3"`;
    case 'cobalt': return `fill="${C.cobalt}" fill-opacity="0.25" stroke="${C.cobalt}" stroke-width="2"`;
    default: return kind;
  }
}

// ---------------- card ----------------
function card(file, m) {
  const W = 1840, H = 1140, ps = 430, gap = 16, top = 150;
  let svg = [`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Segoe UI, Arial, sans-serif">`,
    `<rect width="${W}" height="${H}" fill="${C.bg}"/>`,
    `<text x="28" y="52" fill="${C.ivory}" font-size="34" font-weight="800">${esc(m.title)}</text>`,
    `<text x="28" y="84" fill="${C.brass}" font-size="18">${esc(m.sub)}</text>`,
    `<text x="28" y="112" fill="${C.dim}" font-size="16">Look: ${esc(m.look)}</text>`,
    `<text x="${W - 28}" y="52" fill="${C.dim}" font-size="16" text-anchor="end">${esc(m.when)}</text>`,
    `<text x="${W - 28}" y="80" fill="${C.dim}" font-size="14" text-anchor="end">Bellkeeper Ultimate v2 · proposal · party of 4</text>`];
  m.panels.forEach((fn, i) => { const p = Panel(28 + i * (ps + gap), top, ps, `${i + 1}. ${fn.t}`).arena(); fn.d(p); p.boss(); if (fn.top) fn.top(p); (fn.n || []).forEach((t, k, a) => p.note(t, k === a.length - 1 ? C.text : C.dim, a.length - 1 - k)); svg.push(p.svg()); });
  const y0 = top + ps + 60, colW = (W - 56 - 2 * gap) / 3;
  const col = (cx, title, color, lines) => {
    svg.push(`<rect x="${cx}" y="${y0}" width="${colW}" height="${H - y0 - 24}" rx="10" fill="${C.panel}"/>`);
    svg.push(`<text x="${cx + 16}" y="${y0 + 30}" fill="${color}" font-size="19" font-weight="800">${esc(title)}</text>`);
    let yy = y0 + 58; lines.forEach(l => { wrap(l, 62).forEach((w, k) => { svg.push(`<text x="${cx + 16 + (k ? 14 : 0)}" y="${yy}" fill="${C.text}" font-size="15">${k ? '' : '• '}${esc(w)}</text>`); yy += 21; }); yy += 4; });
  };
  col(28, 'Assignments (what you are given)', C.ivory, m.assign);
  col(28 + colW + gap, 'Fail states (what kills / wipes)', C.red, m.fails);
  col(28 + 2 * (colW + gap), 'Ultimate checklist (Part 0 item 5)', C.brass, m.unique);
  svg.push('</svg>');
  fs.writeFileSync(path.join(out, file), svg.join('\n'));
}
function wrap(t, n) { const w = t.split(' '), lines = []; let cur = ''; w.forEach(x => { if ((cur + ' ' + x).trim().length > n) { lines.push(cur.trim()); cur = x; } else cur += ' ' + x; }); if (cur.trim()) lines.push(cur.trim()); return lines; }
const pol = (deg, r) => [Math.sin(deg * Math.PI / 180) * r, Math.cos(deg * Math.PI / 180) * r];

// ================= the eight mechanics =================
const plates = [[0, 7], [7, 0], [0, -7], [-7, 0]];
card('01-escapement-relay.svg', {
  title: '1 · Escapement Relay', sub: 'Four hours, four duties, one beat every 3.0 s: soak → stretch → balance → balance', when: 'P1 (all of it) · returns in P4, P5',
  look: 'the chest escapement wheel ticks; the brass hour hand on the dial points at the plate; the pendulum hangs from the boss on a chain',
  panels: [
    { t: 'Assign: your hour I–IV', d: p => { plates.forEach(([x, y]) => p.circle(x, y, 1.6, 'brass')); p.lane(0, 0, 0, 6, 0.6, `fill="${C.brass}" fill-opacity="0.9"`); p.player(1, 0, 7.6, 'I'); p.player(2, -3, 4, 'II'); p.player(3, 3, -3, 'III'); p.player(4, -5, -4, 'IV'); },
      n: ['Hour hand → the NORTH plate.', 'Each player reads ONE numeral over their head.'] },
    { t: 'Beat I: soak · stretch · balance', d: p => { plates.forEach(([x, y]) => p.circle(x, y, 1.6, 'brass')); p.lane(0, 0, 0, 6, 0.6, `fill="${C.brass}" fill-opacity="0.9"`);
      p.circle(0, -10.5, 1.2, 'safe'); p.tether(0, 0, 0, -10.2, C.brass, 'pendulum ≥ 9 m'); p.mark(4, 0, '✕', C.ivory, 18); p.mark(-4, 0, '✕', C.ivory, 18);
      p.player(1, 0, 7, 'I soaks'); p.player(2, 0, -10.2, 'II stretches'); p.player(3, 4, 0, 'III'); p.player(4, -4, 0, 'IV'); },
      n: ['I soaks the plate (alone).', 'II drags the pendulum to the far side (≥ 9 m).', 'III + IV hold the two counterweights (✕).'] },
    { t: 'Rotate: everyone moves every beat', d: p => { plates.forEach(([x, y]) => p.circle(x, y, 1.6, 'brass')); p.lane(0, 0, 6, 0, 0.6, `fill="${C.brass}" fill-opacity="0.9"`);
      p.arrow(0, -10, 6.2, -1.2, C.p[1]); p.arrow(4, 0, -9.5, 1.5, C.p[2]); p.arrow(0, 7, 0.4, 4.3, C.p[0]); p.arrow(-4, 0, -0.4, -4.3, C.p[3]);
      p.player(2, 7, 0, 'II soaks'); p.player(3, -10.2, 0, 'III stretches'); p.player(1, 0, 4, 'I ✕'); p.player(4, 0, -4, 'IV ✕'); },
      n: ['Hand turns 90° clockwise each beat.', 'Your duty = (your hour − beat) mod 4: soak, stretch, balance, balance.', '12 beats per round; no beat is ever empty.'] },
    { t: 'Fail states', d: p => { plates.forEach(([x, y]) => p.circle(x, y, 1.6, 'brass')); p.circle(0, -7, 1.6, 'fail'); p.fail(0, -9.6, 'EMPTY PLATE = WIPE');
      p.cone(0, 0, 90, 60, 13, 'fail'); p.fail(6.2, 6.4, 'no counterweight:'); p.fail(6.2, 5.2, 'side swept (999)');
      p.tether(0, 0, -3, 3, C.red, 'short (< 9 m)'); p.cone(0, 0, -45, 18, 13, 'fail'); p.player(2, -3, 3, null, true); p.player(1, -6.4, 6.4, 'struck', true); },
      n: ['Two on one plate: both CRACKED (60).', 'Short pendulum: it swings through the soaker (999).'] },
  ],
  assign: ['Hour numeral I, II, III or IV over your head (ivory), fixed for the round.', 'The brass hour hand shows the active plate; it turns 90° clockwise every 3.0 s.', 'Duty each beat from (hour − beat): SOAK the plate, STRETCH the pendulum chain ≥ 9 m on the far side, or hold a COUNTERWEIGHT ✕ (2 of them, perpendicular to the hand).', 'Solo: one pendulum, you soak every beat and the counterweights become two timed taps. 8 players: two hours per numeral, pairs split plate + chain, counterweight + counterweight.'],
  fails: ['Plate empty on its beat: the escapement skips, WIPE.', 'Two players on a plate: both Cracked (60, no damage dealt 20 s).', 'Pendulum chain shorter than 9 m: the pendulum swings through the soaker (999).', 'Counterweight ✕ empty: the pendulum swings a 120° cone on that side (999 to anyone in it).', 'Standing still = missing the next duty within 3 s.'],
  unique: ['✓ Personal assignment: hour numeral and the duty it gives each beat.', '✓ Interaction: the chain holder protects the soaker; the two counterweights balance each other.', '✓ Resolution order: duties rotate every beat; you must know your next one before the hand moves.', '✓ Strat: which counterweight (left/right of the hand) each player takes.', 'Twists: TOP Program Loop / Pantokrator (numbered beat rotation) + DSR Wrath (duties on one tick).', 'Zero unavoidable damage: a perfect beat costs nobody HP.'],
});

card('02-chime-debts.svg', {
  title: '2 · Chime Debts', sub: 'Your debt comes due on your own clock; where you pay it decides everyone else’s path', when: 'P1 overlay (every 36 s) · T2 fast version · P5',
  look: 'the clapper strikes once per debt; the debt ring is a porcelain hour circle; the chime line is a cracked bell-crack from the ring to the chest',
  panels: [
    { t: 'Assign: numeral + timer', d: p => { [['III', 90], ['VI', 180], ['IX', 270], ['XII', 0]].forEach(([n, a]) => { const [x, y] = pol(a, 9); p.circle(x, y, 1.1, 'memory', n); });
      p.player(1, 2, 3, 'III · 6 s'); p.player(2, -2, 3, 'VI · 9 s'); p.player(3, -2, -3, 'IX · 12 s'); p.player(4, 3, -2, 'XII · 15 s'); },
      n: ['Debuff = a numeral and a timer (seeded).', 'When it hits 0 you must stand ON your numeral.'] },
    { t: 't = 6 s: III pays, ring placed', d: p => { p.circle(9, 0, 2.5, 'memory', 'III ring'); p.player(1, 9, 0, 'pays'); p.arrow(-2, 3, -1, -7.5, C.p[1]); p.arrow(-2, -3, -8.2, -1.2, C.p[2]); p.arrow(3, -2, 1.5, 7.6, C.p[3]);
      p.player(2, -1, -7, 'VI'); p.player(3, -7.5, -1, 'IX'); p.player(4, 1.2, 7, 'XII'); },
      n: ['The ring is harmless now; it detonates 8 s later.', 'Everyone walks to their own numeral meanwhile.'] },
    { t: 't = 14 s: III detonates + chime line', d: p => { p.circle(9, 0, 2.5, 'coral'); p.lane(9, 0, 1.4, 0, 2.0, 'coral'); p.circle(0, -9, 2.5, 'memory', 'VI'); p.circle(-9, 0, 2.5, 'memory', 'IX');
      p.player(2, 2.5, -6.5, 'left VI ring'); p.player(3, -5.8, 3, 'left IX ring'); p.player(4, 0, 9, 'XII (due 15 s)'); p.player(1, 4, 5.5, 'out'); },
      n: ['Each ring explodes (r 2.5 m) and fires a 2 m line to the chest.', 'The lines cut the dial: route around them in time order.'] },
    { t: 'Fail states', d: p => { p.circle(0, 9, 2.5, 'fail'); p.fail(0, 12.2, 'NOT ON XII AT 0 = UNPAID: WIPE'); p.lane(-9, 0, -1.4, 0, 2.0, 'fail'); p.player(1, -5, 0.4, 'in a line', true); p.circle(0, -9, 2.5, 'fail'); p.player(3, 0.8, -8.6, 'own ring', true); },
      n: ['Paying anywhere else leaves the debt unpaid (wipe at the next beat).', 'Ring blast or chime line: 60. Two hits: dead.'] },
  ],
  assign: ['Each player: a numeral (III, VI, IX or XII) and a timer (6/9/12/15 s, seeded) shown as an ivory clock over the head.', 'At 0 you must stand on your numeral (r 9 m on the dial): your debt ring (r 2.5 m) is placed there.', '8 s after placing, the ring detonates and fires a 2 m chime line to the chest.', 'Solo: all four debts are yours (6/12/18/24 s). 8 players: two per numeral at the same time, standing apart (rings overlap = both hit).'],
  fails: ['Not on your numeral when your timer ends: debt unpaid, WIPE.', 'Inside any ring when it detonates: 60.', 'Inside any chime line when it fires: 60.', 'Two hits kill; potions are only for mistakes.'],
  unique: ['✓ Personal assignment: numeral + timer per player.', '✓ Resolution order: debts come due 6/9/12/15 s; the detonations follow 8 s later in the same order.', '✓ Interaction: each detonation line blocks the next payer’s route.', '✓ Memory: rings stay as ivory outlines for 8 s, then become live.', 'Twists: FRU Ultimate Relativity (timers leave something at your feet) + DSR Wroth Flames (route through time-ordered gaps).'],
});

card('03-gear-train.svg', {
  title: '3 · Gear Train', sub: 'Chained in pairs; keep the chain in its band, then swap partners when the gears mesh', when: 'P2 (main) · P4 overlay',
  look: 'two brass gears float between each pair, joined by chain; the escapement ring grinds around the boss',
  panels: [
    { t: 'Assign: gear pairs + Reverse', d: p => { p.ring(1.4, 3.5, 'coral'); p.tether(-2, 7, 6, 3, C.brass, '1–2 · 6–10 m'); p.tether(-6, -4, -1, -7, C.brass, '3–4 · < 4 m (Reverse)'); p.player(1, -2, 7); p.player(2, 6, 3); p.player(3, -6, -4, '!⟲ Reverse'); p.player(4, -1, -7); },
      n: ['Pairs: 1–2 and 3–4 (brass chain + gear).', 'One player carries REVERSE: their chain must stay SHORT.'] },
    { t: 'Hold the band (every 2 s check)', d: p => { p.ring(1.4, 3.5, 'coral'); p.circle(-2, 7, 10, `fill="none" stroke="${C.ok}" stroke-width="1" stroke-dasharray="3 4"`); p.circle(-2, 7, 6, `fill="none" stroke="${C.ok}" stroke-width="1" stroke-dasharray="3 4"`);
      p.tether(-2, 7, 6, 3, C.brass, '8.9 m ✓'); p.tether(-6, -4, -4, -6.5, C.brass, '3.2 m ✓'); p.player(1, -2, 7); p.player(2, 6, 3); p.player(3, -6, -4); p.player(4, -4, -6.5); },
      n: ['Normal chain: 6–10 m. Reverse chain: under 4 m.', 'The ring round the boss (1.4–3.5 m) grinds all the time.'] },
    { t: 'Mesh at 6 s: swap to 1–3, 2–4', d: p => { p.ring(1.4, 3.5, 'coral'); p.arrow(-2, 7, -6.5, 1, C.p[0]); p.arrow(-6, -4, -7, 0, C.p[2]); p.arrow(6, 3, 6.5, -2.5, C.p[1]); p.arrow(-4, -6.5, 4.5, -5.5, C.p[3]);
      p.tether(-6.5, 1.5, -7.5, -0.8, C.brass, '1–3 (Reverse)'); p.tether(6.5, -2.5, 4.5, -6.5, C.brass, '2–4'); p.player(1, -6.5, 1.5); p.player(3, -7.5, -0.8); p.player(2, 6.5, -2.5); p.player(4, 4.5, -6.5); },
      n: ['Gears mesh: chains jump to new partners.', 'Reverse moves with the gear: 1–3 is now the short one.', 'Agreed lanes: west pair west, east pair east, never through the ring.'] },
    { t: 'Fail states', d: p => { p.ring(1.4, 3.5, 'coral'); p.tether(-6, 2, 6, 2, C.red, 'SNAP > 10 m: WIPE'); p.tether(-2, -6, -1, -7, C.red, 'GRIND < 6 m: 40 each'); p.player(1, -6, 2); p.player(2, 6, 2); p.player(3, -2, -6); p.player(4, -1, -7);
      p.lane(-6, 2, 6, 2, 0.8, 'fail'); p.player(4, 1, 2.6, 'crossed a chain', true); },
      n: ['Crossing another pair’s chain: 40.', 'Entering the grinding ring: 60.'] },
  ],
  assign: ['Two chained pairs (brass gear between them), shown as tethers.', 'Normal chain length 6–10 m, checked every 2 s; one player has REVERSE (cobalt ⟲): their chain must stay under 4 m.', 'Every 6 s the gears mesh: partners swap (1–2/3–4 → 1–3/2–4 → 1–4/2–3 → …) and Reverse passes along its gear.', 'Solo: you are chained to the boss’s gear (6–10 m); its Reverse flips every mesh. 8 players: four pairs, two Reverses.'],
  fails: ['Chain longer than 10 m: it snaps, WIPE.', 'Chain shorter than 6 m (or Reverse longer than 4 m): grind, 40 to both.', 'Touching another pair’s chain: 40.', 'Inside the grinding escapement ring (1.4–3.5 m): 60.'],
  unique: ['✓ Personal assignment: partner + Reverse.', '✓ Interaction: your position is set by your partner’s.', '✓ Rule change: Reverse flips the band for its pair.', '✓ Resolution order: the mesh swaps partners in a fixed rotation.', '✓ Strat: the party agrees swap lanes (west pair / east pair).', 'Twists: TOP Hello World (hand-offs) + N’Zoth Paranoia (distance to a partner).'],
});

card('04-inverted-bell.svg', {
  title: '4 · The Inverted Bell', sub: 'Inverted players must stand IN the coral; inversion passes by touch', when: 'P2 overlay · P5',
  look: 'the shell flips upside down; inverted players carry a small cobalt upturned bell; rings toll from the chest',
  panels: [
    { t: 'Ring 1: east half', d: p => { p.half(90, 'coral'); p.player(2, 6, 3, '!INV'); p.player(4, 5, -4, '!INV'); p.player(1, -6, 3); p.player(3, -5, -4); },
      n: ['2 and 4 are INVERTED: they must be in the coral.', '1 and 3 avoid it as usual.'] },
    { t: 'Pass: touch to hand it on', d: p => { p.arrow(5, -4, 1.4, -6, C.p[3]); p.player(4, 1, -6.3, '!passes'); p.player(3, -0.2, -6.9, '!INV now'); p.player(2, 6, 3, '!INV'); p.player(1, -6, 3); },
      n: ['Contact (≤ 1 m) moves the cobalt bell to the other player.', 'Strat: by ring 3 the inverted must be the ODD hours.'] },
    { t: 'Ring 2: donut 5–13 m', d: p => { p.ring(5, 13, 'coral'); p.player(2, 8, 2, '!INV'); p.player(3, -7, -5, '!INV'); p.player(1, -2.5, 2.5); p.player(4, 2.5, -2.5); },
      n: ['Inverted go OUT into the donut; normal go IN.', 'Then ring 3 (west half) 2.5 s later: pass again first.'] },
    { t: 'Fail states', d: p => { p.half(270, 'coral'); p.player(3, 6, 2, '!INV outside', true); p.player(1, -6, 2, 'normal inside', true); p.player(2, -2, -6, '!INV'); p.player(4, 6, -4);
      p.fail(0, 11.5, 'WRONG HOLDERS AT RING 3 = WIPE'); },
      n: ['Inverted outside the coral / normal inside: 999.'] },
  ],
  assign: ['Two players get INVERTED (cobalt upturned bell over the head), seeded.', 'Three rings 2.5 s apart (half, donut, opposite half: seeded order). Inverted must be INSIDE the coral, normal outside.', 'Touch (≤ 1 m) passes your inversion to that player; a player can hold only one.', 'Rule: at ring 3 the inverted pair must be the two odd hours (I, III); the party plans the passes.', 'Solo: inversion toggles on each ring; you alternate in/out. 8 players: four inverted, odd hours of each half.'],
  fails: ['Inverted and outside the coral, or normal and inside: 999.', 'Wrong holders at ring 3: the bell cracks, WIPE.', 'Passing to someone already inverted: both cracked (60).'],
  unique: ['✓ Personal assignment: inverted or not.', '✓ Rule change: inversion flips safe and unsafe for its holder.', '✓ Interaction: passes by touch, and who passes to whom matters.', '✓ Strat: odd hours hold it at ring 3, so passes must be planned.', '✓ Resolution order: three rings in a fixed seeded order.', 'Twists: FRU Crystallize Time / DSR Death of the Heavens + TOP Hello World.'],
});

card('05-stopped-hour-wardens.svg', {
  title: '5 · The Stopped Hour: Wardens', sub: 'Two are frozen; the two free players fetch a ward gear and take each lash for them, while jumping the minute hand', when: 'T1 (×3) · P3 · P5',
  look: 'the boss catches his clapper and the pendulum hangs mid-swing; brass ward gears drop at two hour marks; chains lash from the rim stations',
  panels: [
    { t: 'Assign: hourglasses (5 s)', d: p => { p.player(1, -4, 4, 'HOURGLASS'); p.player(3, 4, -4, 'HOURGLASS'); p.player(2, 2, 6); p.player(4, -6, -2); p.circle(...pol(60, 9), 0.9, 'brass', 'W'); p.circle(...pol(240, 9), 0.9, 'brass', 'W'); },
      n: ['1 and 3 will be STILLED for 4 s.', 'Two ward gears (W) drop on the dial: free players fetch one each.'] },
    { t: 'Freeze: lanes from the stations', d: p => { const nw = pol(315, 13), se = pol(135, 13); p.lane(nw[0], nw[1], -4, 4, 1.6, 'memory'); p.lane(se[0], se[1], 4, -4, 1.6, 'memory');
      p.arrow(2, 6, -6.4, 6.6, C.p[1]); p.arrow(-6, -2, 6.6, -6.4, C.p[3]); p.player(1, -4, 4, '❄'); p.player(3, 4, -4, '❄'); p.player(2, -6.8, 6.8, 'W'); p.player(4, 6.8, -6.8, 'W'); },
      n: ['Each frozen player gets a lash lane from their station.', 'Each warden steps into a lane between station and frozen player.'] },
    { t: 'Meanwhile: jump the minute hand', d: p => { p.lane(0, 0, ...pol(200, 13), 1.4, `fill="#3fc9ff" fill-opacity="0.35" stroke="#3fc9ff" stroke-width="2"`); p.mark(-3.5, -8, 'JUMP', '#3fc9ff', 15);
      const nw = pol(315, 13), se = pol(135, 13); p.lane(nw[0], nw[1], -4, 4, 1.6, 'coral'); p.lane(se[0], se[1], 4, -4, 1.6, 'coral'); p.player(1, -4, 4, '❄'); p.player(3, 4, -4, '❄'); p.player(2, -6.8, 6.8, 'W'); p.player(4, 6.8, -6.8, 'W'); },
      n: ['The minute hand sweeps once round in 4 s (jump it).', 'Time is stopped for the frozen: the hand passes them.'] },
    { t: 'Fail states', d: p => { const nw = pol(315, 13); p.lane(nw[0], nw[1], -4, 4, 1.6, 'fail'); p.player(1, -4, 4, 'lash reached', true); p.player(2, 1, 6, 'no ward / out of lane');
      p.player(4, 6, -6, 'blocked w/o ward', true); p.fail(0, -11, 'unjumped hand: 60'); },
      n: ['Lash reaches a frozen player: 999.', 'Warden without a ward gear takes the lash: 60.'] },
  ],
  assign: ['Hourglass (ivory, 5 s warning) on two players: they will be Stilled for 4 s (no move, no act).', 'Two brass ward gears drop at hour marks: each free player must pick up one (walk over it).', 'At resume each frozen player is lashed by a chain lane from their station; a warden holding a gear in the lane absorbs it (0 damage).', 'The minute hand sweeps 360° in 4 s during the stop: free players jump it.', 'Solo: you are frozen, the boss’s body is your ward (hide behind him as before) and you jump nothing. 8 players: four frozen, four wardens.'],
  fails: ['Lash reaches a frozen player: 999.', 'Warden without a gear in a lash: 60.', 'Minute hand not jumped: 60 + knocked out of the lane.', 'A gear left lying: it ticks to coral and explodes (r 3 m, 60) at resume.'],
  unique: ['✓ Personal assignment: frozen or warden.', '✓ Interaction: the warden’s spot is set by the frozen player’s.', '✓ Resolution order: fetch the gear, then the lane, then the jump.', '✓ Strat: which warden takes which frozen player (NW/SE, agreed).', 'Twists: TEA Inception (locked + free players) + TOP intercepts (body-block).'],
});

card('06-echoes-of-you.svg', {
  title: '6 · Echoes of You', sub: 'The boss rewinds P1: your own past self returns, and only you may claim it', when: 'P4 (the replay phase)',
  look: 'the ledger pages turn backwards; porcelain after-images of each player rise where they stood; the halo spins in reverse',
  panels: [
    { t: 'P1 records where you stood', d: p => { p.ghost(1, 0, 7, 'beat 3'); p.ghost(2, 7, 0, 'beat 6'); p.ghost(3, 0, -7, 'beat 9'); p.ghost(4, -7, 0, 'beat 12'); p.player(1, 3, 4); p.player(2, 5, -3); p.player(3, -3, -5); p.player(4, -5, 4); },
      n: ['Your Echo numeral = one P1 beat.', 'Your echo is where YOU stood on that beat (remember it).'] },
    { t: 'P4: echoes return in reverse', d: p => { p.ghost(4, -7, 0, 'IV (first)'); p.ghost(3, 0, -7, 'III'); p.ghost(2, 7, 0, 'II'); p.ghost(1, 0, 7, 'I'); p.circle(-7, 0, 1.2, 'coral'); p.player(4, -7, 0.4, 'claims'); p.player(1, 2.5, 4); p.player(2, 4, -1.5); p.player(3, -2, -4.5); },
      n: ['Each echo turns coral 1.5 s before its tick, 2 s apart (IV → I).', 'Stand on YOUR echo when it resolves: it is absorbed (0).'] },
    { t: 'Others’ echoes are poison', d: p => { p.circle(0, -7, 1.2, 'coral'); p.circle(0, -7, 6, `fill="${C.coral}" fill-opacity="0.08" stroke="${C.coral}" stroke-width="1.5" stroke-dasharray="5 4"`); p.player(3, 0.3, -7, 'claims III'); p.player(1, -3, 5, 'away'); p.player(2, 7, 0.4, 'II next'); p.player(4, -8, 4, 'away'); },
      n: ['Touching someone else’s echo: 60.', 'An unclaimed echo bursts 6 m when it resolves.'] },
    { t: 'Fail states', d: p => { p.circle(7, 0, 6, 'fail'); p.fail(7, 7.2, 'UNCLAIMED: BURST 6 m (60)'); p.ghost(1, 0, 7); p.player(4, 0.5, 6.5, 'took 1’s echo', true); p.player(2, 2, 2, 'in the burst', true); },
      n: ['Memory matters: echoes show as dashed ivory only 2 s ahead.'] },
  ],
  assign: ['At P4 start each player gets an Echo numeral (I–IV) naming one P1 beat.', 'Your echo appears where you yourself stood on that beat (so P1 positioning is a memory).', 'Echoes resolve in reverse order, 2 s apart; each shows dashed ivory 2 s ahead, coral 1.5 s ahead.', 'Solo: all four echoes are yours, in reverse. 8 players: two echoes per numeral.'],
  fails: ['Not on your echo when it resolves: it bursts 6 m (60 to everyone in range).', 'Touching another player’s echo: 60.', 'Standing still: you will be on someone’s echo within 4 s.'],
  unique: ['✓ Personal assignment: your echo numeral.', '✓ Memory across mechanics: where you stood in P1.', '✓ Resolution order: reverse, 2 s apart.', '✓ Interaction: your echo is everyone else’s hazard.', 'Twists: TEA Fate Calibration (play your past from memory) + Jaina (your drop is someone else’s cover).'],
});

card('07-branching-hour.svg', {
  title: '7 · The Branching Hour', sub: 'Two futures are traced; the party votes on one with its feet; a split vote plays both', when: 'P3 (main) · P5',
  look: 'the conducting fingers trace Future I in porcelain and Future II in brass across the dial; two sigils light on the floor',
  panels: [
    { t: 'Trace both futures', d: p => { p.cone(0, 0, 45, 40, 13, 'memory'); p.cone(0, 0, 225, 40, 13, 'memory'); p.ring(6, 13, 'future2'); [[1, ...pol(135, 8)], [2, ...pol(315, 8)], [3, ...pol(180, 4)], [4, ...pol(0, 4)]].forEach(([i, x, y]) => p.ghost(i, x, y, 'I'));
      [[1, ...pol(90, 3)], [2, ...pol(270, 3)], [3, ...pol(0, 3)], [4, ...pol(180, 3)]].forEach(([i, x, y]) => p.mark(x + 1.3, y + 1.2, i, C.brass, 12)); },
      n: ['Future I (ivory): cones; each player’s spot numbered.', 'Future II (brass): donut; spots inside.'] },
    { t: 'Vote: stand on a sigil', d: p => { p.circle(-6, 6, 2, 'memory', 'I'); p.circle(6, -6, 2, 'future2', 'II'); p.player(1, -6.5, 6.5); p.player(2, -5.5, 5.5); p.player(3, -6.6, 5.3); p.player(4, 5.6, -6.2); },
      n: ['Future with 3+ players on its sigil plays.', 'Strat: odd round → I, even round → II (agreed).'] },
    { t: 'Resolve the chosen future', d: p => { p.cone(0, 0, 45, 40, 13, 'coral'); p.cone(0, 0, 225, 40, 13, 'coral'); p.player(1, ...pol(135, 8)); p.player(2, ...pol(315, 8)); p.player(3, ...pol(180, 4)); p.player(4, ...pol(0, 4)); p.arrow(-6, 6, ...pol(315, 7.6), C.p[1]); },
      n: ['3 s to reach YOUR numbered spot in that future.', 'Then the next pair of futures is traced (3 per phase loop).'] },
    { t: 'Fail states', d: p => { p.cone(0, 0, 45, 40, 13, 'fail'); p.ring(6, 13, 'fail'); p.fail(0, 11, '2–2 SPLIT: BOTH FUTURES PLAY = WIPE'); p.player(2, 7, 7, 'wrong spot', true); },
      n: ['Wrong spot in the chosen future: 60.'] },
  ],
  assign: ['Two futures are traced 4 s each (ivory I, brass II), each with a coral pattern and four numbered personal spots.', 'Vote: two sigils light; the future with 3+ players on its sigil at the verdict plays.', 'Then 3 s to reach your numbered spot in the chosen future.', 'Solo: you choose alone (no split possible) but both futures’ patterns resolve one after the other, in your chosen order. 8 players: 6+ on a sigil; spots per pair.'],
  fails: ['A 2–2 split: both futures resolve together, WIPE.', 'Not on your numbered spot: inside the pattern, 60.', 'Late to the vote (off both sigils): counts as a split.'],
  unique: ['✓ Choice the party must agree on (vote by position); a split wipes.', '✓ Memory: both futures fade after tracing.', '✓ Personal assignment: your numbered spot in each future.', '✓ Resolution order: trace I, trace II, vote, resolve.', 'Twists: TEA Fate Calibration + Kefka “says” (a call you must decide, not read).'],
});

card('08-thirteenth-toll.svg', {
  title: '8 · The Thirteenth Toll (P5)', sub: 'The great bell counts I…XIII; every count marks a player, flares the fissure and turns the escapement', when: 'P5 (78 s hard enrage)',
  look: 'the monumental cracked bell behind the north rim tolls; numerals light on the void sky; the gold fissure cracks open',
  panels: [
    { t: 'Count V: mark + fissure flare', d: p => { p.lane(-9.2, 9.2, 9.2, -9.2, 2.4, 'coral'); p.circle(-6, -2, 5, 'memory'); p.player(2, -6, -2, 'TOLL (3 s)'); p.player(1, 5, 4); p.player(3, 6, -1); p.player(4, 1, -7); p.mark(0, 11.6, 'V', C.ivory, 18); },
      n: ['Count V marks player 2 (hour order).', 'The fissure flares on every count.'] },
    { t: 'Spread away from all, off the fissure', d: p => { p.circle(-9, 2, 5, 'coral'); p.lane(-9.2, 9.2, 9.2, -9.2, 1.4, 'coral'); p.player(2, -9, 2, 'drops'); p.player(1, 6, 4); p.player(3, 6.5, -1); p.player(4, 2, -7.5); },
      n: ['The marked player drops a 5 m toll circle away from everyone.', 'Others keep the escapement relay (plates, chain, ✕) going.'] },
    { t: 'Every 6 s: next count, next player', d: p => { plates.forEach(([x, y]) => p.circle(x, y, 1.6, 'brass')); p.lane(-9.2, 9.2, 9.2, -9.2, 1.4, 'coral'); p.circle(4, -4, 5, 'memory'); p.player(3, 4, -4, 'TOLL'); p.player(4, 0, 7, 'soak'); p.player(1, -4, 0, '✕'); p.player(2, -10, -6, 'chain'); p.mark(0, 11.6, 'VI', C.ivory, 18); },
      n: ['Toll, plate, chain and counterweight on the same beats.', 'Burn the last 15 % before XIII.'] },
    { t: 'Fail states', d: p => { p.circle(0, 0, 13, 'fail'); p.fail(0, 1, 'XIII (78 s): ENRAGE, WIPE'); p.player(1, 3, 4, 'in a toll', true); },
      n: ['Toll circle on another player: 60 each.', 'Standing in the flared fissure: 60.'] },
  ],
  assign: ['The great bell counts I…XIII, one count every 6 s: no damage on its own.', 'Each count marks the next player in hour order with a TOLL: a 5 m circle at their feet 3 s later.', 'Each count also flares the gold fissure (2.4 m, 60) and advances the escapement relay (card 1).', 'XIII is the hard enrage.', 'Solo: every count marks you. 8 players: two marks per count, opposite sides.'],
  fails: ['Toll circle catching another player: 60 to each.', 'In the fissure on a flare: 60.', 'Relay duties missed (card 1): wipe / 999.', 'XIII: the bell completes the hour, WIPE.'],
  unique: ['✓ Personal assignment: the toll mark in hour order.', '✓ Interaction: the spread must avoid everyone else’s relay duty.', '✓ Resolution order: count, mark, flare, beat on a fixed 6 s rhythm.', '✓ Memory across mechanics: the hour order from P1.', 'Twists: Raszageth “tower + stack + spread” + UCoB Grand Octet (a long fixed sequence).'],
});

// ================= phase timeline =================
(function timeline() {
  const phases = [['P1 Last Guardian', 0, 150, ['Escapement Relay (beat 3 s)', 'Chime Debts ×4']], ['T1 The Hour Stops', 150, 175, ['Stopped Hour: Wardens ×3']], ['P2 Broken Hour', 175, 330, ['Gear Train (mesh 6 s)', 'Inverted Bell ×5']],
    ['P3 The Open Hour', 330, 480, ['Branching Hour ×4', 'Stopped Hour: Wardens ×2']], ['T2 Thirteen Chimes', 480, 510, ['Chime Debts (fast)']], ['P4 The Unfinished Hour', 510, 660, ['Echoes of You ×3', 'Gear Train']], ['P5 Final Toll', 660, 738, ['Thirteenth Toll', 'Relay', 'Fissure']]];
  const W = 2000, H = 760, x0 = 190, x1 = W - 40, T = 738, sx = (x1 - x0) / T, laneY = [250, 350, 450, 550];
  const svg = [`<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" font-family="Segoe UI, Arial, sans-serif"><rect width="${W}" height="${H}" fill="${C.bg}"/>`,
    `<text x="28" y="52" fill="${C.ivory}" font-size="32" font-weight="800">Bellkeeper Ultimate v2 · phase timeline (party of 4, proposal)</text>`,
    `<text x="28" y="84" fill="${C.dim}" font-size="17">Every player has a live job on every second (bars). A red mark would be a gap over 1.5 s: there are none. Total ≈ 12:18 at target DPS; P5 ends in the 78 s enrage.</text>`];
  phases.forEach(([n, a, b, jobs], i) => { svg.push(`<rect x="${x0 + a * sx}" y="120" width="${(b - a) * sx - 3}" height="80" fill="${i % 2 ? '#1b2442' : '#202a4c'}" rx="6"/><text x="${x0 + a * sx + 8}" y="146" fill="${C.ivory}" font-size="16" font-weight="700">${esc(n)}</text>`);
    jobs.forEach((j, k) => svg.push(`<text x="${x0 + a * sx + 8}" y="${168 + 16 * k}" fill="${C.brass}" font-size="13">${esc(j)}</text>`)); });
  for (let t = 0; t <= T; t += 60) svg.push(`<line x1="${x0 + t * sx}" y1="210" x2="${x0 + t * sx}" y2="610" stroke="#2c3556"/><text x="${x0 + t * sx}" y="630" fill="${C.dim}" font-size="13" text-anchor="middle">${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}</text>`);
  const job = { relay: ['#c99a4b', 'S/T/C'], debt: ['#efe6cf', 'debt'], debtfast: ['#efe6cf', 'debt'], ward: ['#5d7ee8', 'frozen/warden'], gear: ['#9b7a3c', 'chain'], inv: ['#3f63c8', 'inv'], branch: ['#e7d8b0', 'future'], echo: ['#d9d2c0', 'echo'], toll: ['#ff9a6b', 'toll'] };
  const bars = [];
  const seg = (p, a, b, k) => bars.push([p, a, b, k]);
  for (let p = 0; p < 4; p++) {
    for (let t = 0; t < 150; t += 3) seg(p, t, t + 3, 'relay');
    for (let r = 0; r < 4; r++) seg(p, 20 + r * 36 + p * 3, 20 + r * 36 + p * 3 + 14, 'debt');
    for (let t = 150; t < 175; t += 8) seg(p, t, t + 8, 'ward');
    for (let t = 175; t < 330; t += 2) seg(p, t, t + 2, 'gear');
    for (let r = 0; r < 5; r++) seg(p, 185 + r * 30, 193 + r * 30, 'inv');
    for (let t = 330; t < 480; t += 15) seg(p, t, t + 15, t % 60 < 30 ? 'branch' : 'ward');
    for (let t = 480; t < 510; t += 3) seg(p, t, t + 3, 'debtfast');
    for (let t = 510; t < 660; t += 2) seg(p, t, t + 2, Math.floor((t - 510) / 25) % 2 ? 'gear' : 'echo');
    for (let t = 660; t < 738; t += 3) seg(p, t, t + 3, ((t - 660) / 3 + p) % 4 === 0 ? 'toll' : 'relay');
  }
  bars.forEach(([p, a, b, k]) => { const y = laneY[p] + (k === 'debt' || k === 'inv' ? -16 : 0); svg.push(`<rect x="${x0 + a * sx}" y="${y - 14}" width="${Math.max(1.5, (b - a) * sx - 0.8)}" height="${k === 'debt' || k === 'inv' ? 10 : 28}" fill="${job[k][0]}" opacity="${k === 'debt' || k === 'inv' ? 1 : 0.85}"/>`); });
  // coverage check: largest gap per player
  for (let p = 0; p < 4; p++) {
    const iv = bars.filter(b => b[0] === p).map(b => [b[1], b[2]]).sort((a, b) => a[0] - b[0]); let end = 0, worst = 0;
    iv.forEach(([a, b]) => { worst = Math.max(worst, a - end); end = Math.max(end, b); }); worst = Math.max(worst, T - end);
    svg.push(`<text x="28" y="${laneY[p] + 5}" fill="${C.p[p]}" font-size="17" font-weight="800">Player ${p + 1} (${C.cls[p]})</text><text x="28" y="${laneY[p] + 24}" fill="${worst > 1.5 ? C.red : C.ok}" font-size="13">longest gap ${worst.toFixed(1)} s</text>`);
  }
  let lx = 190; Object.entries(job).filter(([k]) => k !== 'debtfast').forEach(([k, [c, l]]) => { svg.push(`<rect x="${lx}" y="668" width="22" height="16" fill="${c}"/><text x="${lx + 28}" y="681" fill="${C.text}" font-size="14">${esc({ relay: 'Escapement Relay', debt: 'Chime Debt (pay / dodge)', ward: 'Stopped Hour', gear: 'Gear Train', inv: 'Inverted Bell', branch: 'Branching Hour', echo: 'Echoes of You', toll: 'Thirteenth Toll' }[k])}</text>`); lx += 230; });
  svg.push(`<text x="190" y="725" fill="${C.dim}" font-size="14">Attacks happen during duties (counterweights, chain bands, spots are all within reach of the boss for at least half the party at any time).</text>`);
  svg.push('</svg>');
  fs.writeFileSync(path.join(out, '00-timeline.svg'), svg.join('\n'));
})();
console.log('cards written to', out);
