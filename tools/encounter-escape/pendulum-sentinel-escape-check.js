// Pendulum Sentinel escape check (docs/design/pendulum-sentinel.md §5). Run: node tools/encounter-escape/pendulum-sentinel-escape-check.js [bossBound]
// Worst straight-line escape distance per shape in the key-art arena: player centres <= 11.5 m, boss centre <= bossBound
// (default 7.4 m), body 1.1 m (+0.3 player margin), hit margin 0.3 m. Same method as the Bellkeeper and Dial Warden checkers.
const PB = 11.5, BB = +(process.argv[2] || 7.4), BODY = 1.1 + 0.3, M = 0.3, G = 0.1, SG = 0.05;
const inArena = (x, y) => x * x + y * y <= PB * PB;

function segDist(px, py, ax, ay, bx, by) {
  const dx = bx - ax, dy = by - ay, t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  return Math.hypot(px - ax - t * dx, py - ay - t * dy);
}
// distance from p to a filled sector (apex o, facing yaw, half-angle h, radius R)
function sectorDist(px, py, ox, oy, yaw, h, R) {
  const r = Math.hypot(px - ox, py - oy);
  let d = Math.atan2(py - oy, px - ox) - yaw; d = Math.atan2(Math.sin(d), Math.cos(d));
  if (Math.abs(d) <= h && r <= R) return 0;
  const e1 = [ox + R * Math.cos(yaw - h), oy + R * Math.sin(yaw - h)], e2 = [ox + R * Math.cos(yaw + h), oy + R * Math.sin(yaw + h)];
  let best = Math.min(segDist(px, py, ox, oy, ...e1), segDist(px, py, ox, oy, ...e2));
  if (Math.abs(d) <= h) best = Math.min(best, r - R);
  return best;
}

function worst(bx, by, danger, cx, cy, R) {
  const ok = (x, y) => inArena(x, y) && (x - bx) ** 2 + (y - by) ** 2 >= BODY * BODY;
  const safe = [];
  for (let x = cx - R; x <= cx + R; x += SG) for (let y = cy - R; y <= cy + R; y += SG)
    if (ok(x, y) && !danger(x, y) && (danger(x + SG, y) || danger(x - SG, y) || danger(x, y + SG) || danger(x, y - SG))) safe.push([x, y]);
  let w = 0, at = null;
  for (let x = cx - R; x <= cx + R; x += G) for (let y = cy - R; y <= cy + R; y += G) {
    if (!ok(x, y) || !danger(x, y)) continue;
    let b = 1e9;
    for (const [sx, sy] of safe) { const d = (sx - x) ** 2 + (sy - y) ** 2; if (d < b) b = d; }
    b = Math.sqrt(b);
    if (b > w) { w = b; at = [+x.toFixed(1), +y.toFixed(1)]; }
  }
  return { d_max: +w.toFixed(2), at };
}

const out = { boss_bound: BB };
const keep = (best, res, extra) => (res.d_max > best.d_max ? { ...res, ...extra } : best);

// Anchor Drop: disc of radius 1.1 + 2.0 = 3.1 m around the boss centre
let best = { d_max: 0 };
for (let bx = 0; bx <= BB + 1e-9; bx += 0.5) {
  const r = 3.1 + M;
  best = keep(best, worst(bx, 0, (x, y) => (x - bx) ** 2 + y ** 2 <= r * r, bx, 0, r + 0.5), { bx });
}
out.anchor_drop = best;

// Low Pendulum / Counter-Swing beat: 90 deg sector, radius 1.1 + 4.0 = 5.1 m, apex at the boss centre
best = { d_max: 0 };
for (let bx = 0; bx <= BB + 1e-9; bx += 0.5) for (let a = 0; a <= 180; a += 10) {
  const yaw = a * Math.PI / 180;
  best = keep(best, worst(bx, 0, (x, y) => sectorDist(x, y, bx, 0, yaw, Math.PI / 4, 5.1) <= M, bx, 0, 5.1 + M + 0.5), { bx, facing_deg: a });
}
out.low_pendulum_sector = best;

// Counterweight Cast: 2.0 m circle, centre inside the player bound (worst case is at the wall or against the body)
best = { d_max: 0 };
for (const bx of [0, 3, 6, BB]) for (let a = 0; a <= 180; a += 30) for (const dist of [1.6, 2.5, 4, 6, 8.1]) {
  const cx = bx + dist * Math.cos(a * Math.PI / 180), cy = dist * Math.sin(a * Math.PI / 180);
  if (!inArena(cx, cy)) continue;
  const r = 2.0 + M;
  best = keep(best, worst(bx, 0, (x, y) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r, cx, cy, r + 0.5), { bx, bearing_deg: a, dist });
}
out.counterweight_cast = best;

// Pendulum Charge / Return Stroke: 2.2 m lane, sideways escape (wall never touches a lane: endpoints stay 3 m off it)
out.charge_lane = { d_max: +(1.1 + M).toFixed(2), note: "half-width + margin, any lane" };

const ticks = (d, v) => Math.ceil((d / v) * 60);
const tiers = { first_chime: 36, steady_ticking: 27, striking_hour: 21, midnight_toll: 18 };
for (const k of ["anchor_drop", "low_pendulum_sector", "counterweight_cast", "charge_lane"]) {
  out[k].min_window_ticks = {};
  for (const v of [6, 4.2]) for (const [t, react] of Object.entries(tiers)) out[k].min_window_ticks[`${t}@${v}`] = react + ticks(out[k].d_max, v);
}
console.log(JSON.stringify(out, null, 1));
