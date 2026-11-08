// P2 "Broken Hour" proposal solver: finds a perfect route for 4 players beat by beat, then checks it.
// Plan coords: x east, y north (screen up). Hours 0..3 = I..IV. Pairs (chains): I–III (0,2), II–IV (1,3).
const BEAT = 3, SPEED = 6, SPRINT = 9, BEATS = 18, PLATE_R = 7, BOSS_R = 2.0, BOUND = 11.2;
const CH_MIN = 6, CH_MAX = 10, TOUCH = 1.5, ON_PLATE = 1.6;
const RUNS = [6, 4, 5, 3];            // the hand reverses after these runs of beats (P1's rewind)
const dir = a => [Math.sin(a * Math.PI / 180), Math.cos(a * Math.PI / 180)];
const sub = (p, q) => [p[0] - q[0], p[1] - q[1]], len = v => Math.hypot(v[0], v[1]), dist = (p, q) => len(sub(p, q));
const dot = (p, q) => p[0] * q[0] + p[1] * q[1];
// dial position per beat (virtual, with rewinds)
const virt = []; { let v = 1, d = 1, run = 0, left = RUNS[0]; for (let b = 0; b < BEATS; b++) { virt.push(v); if (--left === 0) { d = -d; run = (run + 1) % RUNS.length; left = RUNS[run]; } v += d; } }
const handA = b => 90 * virt[b], plate = b => dir(handA(b)).map(x => x * PLATE_R);
const nearHalf = (p, b) => dot(p, dir(handA(b))) > 0.6;          // the plate's half (safe for normal players)
const farHalf = (p, b) => dot(p, dir(handA(b))) < -0.6;           // the slammed half (where INVERTED must be)
const soaker = b => b % 4, holder = b => (b + 2) % 4;             // INVERTED is always the soaker's partner
const partner = i => (i + 2) % 4;
function cross(a, b, c, d) { // do segments ab and cd cross
  const o = (p, q, r) => Math.sign((q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]));
  return o(a, b, c) !== o(a, b, d) && o(c, d, a) !== o(c, d, b);
}
const okSpot = p => len(p) >= BOSS_R + 0.4 && len(p) <= BOUND;
const grid = []; for (let x = -11; x <= 11; x += 1) for (let y = -11; y <= 11; y += 1) { const p = [x, y]; if (okSpot(p)) grid.push(p); }
const budget = (BEAT - 0.4) * SPEED;   // what a walking player covers in a beat after a 0.4 s read

function checkBeat(b, P) {   // the rules on beat b's tick; returns failures
  const f = [], pl = plate(b);
  if (dist(P[soaker(b)], pl) > ON_PLATE) f.push('plate empty: WIPE');
  for (let i = 0; i < 4; i++) if (i !== soaker(b) && dist(P[i], pl) <= ON_PLATE) f.push(`${'I,II,III,IV'.split(',')[i]} on the plate`);
  for (let i = 0; i < 4; i++) { const inv = i === holder(b); if (inv ? !farHalf(P[i], b) : !nearHalf(P[i], b)) f.push(`${'I,II,III,IV'.split(',')[i]} ${inv ? 'INVERTED outside the slam' : 'in the slam'}: 999`); }
  for (const [a, c] of [[0, 2], [1, 3]]) { const L = dist(P[a], P[c]); if (L > CH_MAX) f.push('chain snaps: WIPE'); else if (L < CH_MIN) f.push('chain grinds: 60 each'); }
  if (cross(P[0], P[2], P[1], P[3])) f.push('chains cross: 60 to all');
  return f;
}

// solve: beat 0 from a start spread; each later beat: the old holder x meets the new holder h at M mid-beat (the touch)
let cur = [[0, 6], [5, -1], [0, -5], [-5, 1]];
const plan = [];
for (let b = 0; b < BEATS; b++) {
  const s = soaker(b), h = holder(b), pl = plate(b), x = b > 0 ? holder(b - 1) : -1;   // x passes INVERTED to h
  const ys = [0, 1, 2, 3].filter(i => i !== s && i !== h);
  let best = null;
  const Hs = grid.filter(p => farHalf(p, b) && dist(p, pl) >= CH_MIN + 0.4 && dist(p, pl) <= CH_MAX - 0.4 && dist(p, pl) > ON_PLATE + 0.6);
  for (const ph of Hs) {
    if (x < 0 && dist(cur[h], ph) > budget) continue;
    for (const pa of grid) {
      const [ya, yb] = ys;
      if (!nearHalf(pa, b) || dist(pa, pl) <= ON_PLATE + 0.6) continue;
      if (dist(cur[ya], pa) > budget) continue;
      // the other of ys must be the partner of ya: ys are one pair (the other chain)
      const pbList = grid.filter(q => nearHalf(q, b) && dist(q, pl) > ON_PLATE + 0.6 && dist(q, pa) >= CH_MIN + 0.4 && dist(q, pa) <= CH_MAX - 0.4 && dist(cur[yb], q) <= budget);
      for (const pb of pbList) {
        const P = []; P[s] = pl; P[h] = ph; P[ya] = pa; P[yb] = pb;
        if (cross(P[0], P[2], P[1], P[3])) continue;
        // the touch: x and h meet at M, then both walk to their beat spots, all inside one beat (sprinting)
        let M = null, mcost = 0;
        if (x >= 0) {
          let mb = null;
          for (let f = 0.2; f <= 0.81; f += 0.15) {
            const m = [cur[x][0] + (cur[h][0] - cur[x][0]) * f, cur[h][1] !== undefined ? cur[x][1] + (cur[h][1] - cur[x][1]) * f : 0];
            if (!okSpot(m)) continue;
            const tx = dist(cur[x], m), th = dist(cur[h], m), tm = Math.max(tx, th) / SPRINT + 0.2;
            const rx = dist(m, P[x]) / SPRINT, rh = dist(m, P[h]) / SPRINT;
            if (tm + Math.max(rx, rh) > BEAT - 0.35) continue;
            const c = tx + th + dist(m, P[x]) + dist(m, P[h]);
            if (!mb || c < mb.c) mb = { m, c, tm };
          }
          if (!mb) continue; M = mb; mcost = mb.c;
        }
        let travel = 0; for (let i = 0; i < 4; i++) if (i !== x && i !== h) travel += dist(cur[i], P[i]);
        // uptime: a little pull toward the boss (melee), and margin from the half-line (readable spots)
        let score = travel + mcost + 0.15 * [0, 1, 2, 3].reduce((a, i) => a + len(P[i]), 0);
        if (!best || score < best.score) best = { score, P, M };
      }
    }
  }
  if (!best) { console.log('NO SOLUTION at beat', b); process.exit(1); }
  plan.push({ b, v: virt[b], hand: handA(b), soaker: s, holder: h, passFrom: x, P: best.P.map(p => p.map(v => +v.toFixed(2))), M: best.M ? { at: best.M.m.map(v => +v.toFixed(2)), t: +best.M.tm.toFixed(2) } : null });
  cur = best.P;
}
let fails = 0; for (const s of plan) { const f = checkBeat(s.b, s.P); fails += f.length; if (f.length) console.log('beat', s.b, f); }
console.log('beats', plan.length, 'fails', fails, 'rewinds at', plan.filter((s, i) => i > 0 && Math.sign(s.v - plan[i - 1].v) !== Math.sign(plan[Math.max(1, i - 1)].v - plan[Math.max(0, i - 2)].v)).map(s => s.b).join(','));
require('fs').writeFileSync(process.argv[2] || 'p2plan.json', JSON.stringify({ BEAT, SPEED, SPRINT, PLATE_R, CH_MIN, CH_MAX, TOUCH, ON_PLATE, plan }));
