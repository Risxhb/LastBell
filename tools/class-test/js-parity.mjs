// Runs the RULES section of prototype/arena-3d/index.html under Node and checks it against the same
// Vanguard 2.0 / dash numbers the C# checks use (tools/class-test/ClassTestChecks.cs).
// Usage: node tools/class-test/js-parity.mjs
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const html = readFileSync(join(here, '..', '..', 'prototype', 'arena-3d', 'index.html'), 'utf8');
const start = html.indexOf('RULES —'), end = html.indexOf('PRESENTATION');
if (start < 0 || end < 0) throw new Error('RULES / PRESENTATION markers not found');
const code = html.slice(html.indexOf('*/', start) + 2, html.lastIndexOf('/*', end));
const R = new Function(code + '\nreturn { Sim, KITS, VG, DASH, BOSS, ORIGIN, SPAWN, ARENA, contains, V1, TICK, SPEED };')();

let assertions = 0; const failures = [];
const check = (ok, what) => { assertions++; if (!ok) failures.push(what); };
const idle = () => ({ mx: 0, my: 0, held: [false, false, false, false], pressed: [false, false, false, false], g: false, gp: false, target: 0, ax: 0, ay: 0, dp: false });
const cmd = o => Object.assign(idle(), o);
const press = i => cmd({ held: [0, 1, 2, 3].map(k => k === i), pressed: [0, 1, 2, 3].map(k => k === i), g: i === 3, gp: i === 3 });
const run = (n, s, c) => { for (let i = 0; i < n; i++) s.step(typeof c === 'function' ? c(i) : c); };
const sim = (cls, distance, extra = {}) => { const s = new R.Sim(cls, { hp: 'infinite', phase: 0, inv: true, ko: 'respawn', ally: false, seed: 7, strikes: false, encounter: 'v01', ...extra }); if (distance != null) { s.p.x = R.BOSS.x; s.p.y = R.BOSS.y + distance; } return s; };
const d = s => Math.hypot(s.p.x - R.BOSS.x, s.p.y - R.BOSS.y);

// combo
{ const s = sim('vanguard', 120); run(41, s, cmd({ held: [true, false, false, false] }));
  check(s.stats.dmg === 8 && s.p.res === 3, `held Basic chains 2+2+4 with +3 Resonance (dmg ${s.stats.dmg}, res ${s.p.res})`);
  check(s.tick < s.p.reversalUntil, 'full chain primes Reversal'); }
// rush
{ const s = sim('vanguard', 250); s.step(press(1)); check(s.p.res === 0 && !s.p.vAction, 'rush rejected below 3 Resonance');
  s.p.res = 4; s.step(press(1)); run(R.VG.rushStartup + R.VG.rushTravel, s, idle());
  check(Math.abs(d(s) - (250 - R.VG.rushSpeed * R.VG.rushTravel)) < 0.5, `rush travel (dist ${d(s).toFixed(1)})`);
  check(s.stats.dmg === R.VG.rushDmg && s.p.res === 1, 'rush deals 9, spends 3'); }
// reversal + final hour mark
{ const s = sim('vanguard', 120); run(41, s, cmd({ held: [true, false, false, false] })); run(30, s, idle());
  const b = s.stats.dmg; s.step(press(1)); run(R.VG.rushTotal, s, idle());
  check(s.stats.dmg - b === R.VG.reversalDmg && s.tick < s.markUntil, 'Reversal Rush 15 + mark');
  const b2 = s.stats.dmg; s.step(press(2)); run(R.VG.finalStartup, s, idle());
  check(s.stats.dmg - b2 === 27 && s.markUntil === 0, `Final Hour vs mark 27 (got ${s.stats.dmg - b2})`); }
// dash
{ const s = sim('vanguard', null); const x0 = s.p.x; s.step(cmd({ dp: true, mx: 1 })); run(R.DASH.duration - 1, s, idle());
  check(Math.abs(s.p.x - x0 - R.DASH.distance) < 0.01 && s.p.dashCharges === 1, 'dash 150 units, one charge');
  s.step(cmd({ dp: true, mx: -1 })); run(R.DASH.duration - 1, s, idle()); s.step(cmd({ dp: true, mx: 1 }));
  check(s.p.dashCharges === 0 && s.tick >= s.p.dashUntil, 'no third dash without charges');
  run(R.DASH.recharge - s.tick, s, idle()); check(s.p.dashCharges === 0, 'not back before 72'); s.step(idle()); check(s.p.dashCharges === 1, 'back at 72'); }
// dash cancel after hit tick
{ const s = sim('vanguard', 120); const ticks = [];
  s.step(press(0)); s.step(idle()); s.step(cmd({ dp: true, mx: 1 })); run(6, s, idle());
  for (const e of s.fx) if (e.type === 'dash') ticks.push(e.tick);
  check(s.stats.dmg === 2 && ticks[0] === 7, `buffered dash after the hit tick (dash at ${ticks[0]})`); }
// counter on the Pendulum Strike (scheduled at 1290 when strikes are on)
{ const s = sim('vanguard', null, { strikes: true }); s.p.x = 700; s.p.y = 650;
  run(1343, s, idle()); s.p.ready[3] = 0; s.step(press(3)); run(1350 - s.tick, s, idle());
  const counters = s.fx.filter(e => e.type === 'counter').length;
  check(counters === 1 && s.stats.countered === 1 && s.p.res === 1, `strike countered (counters ${counters}, res ${s.p.res})`); }
// dash for other classes
{ const s = sim('arcanist', 400); s.step(cmd({ held: [false, false, true, false], pressed: [false, false, true, false] })); run(10, s, idle());
  s.step(cmd({ dp: true, mx: 1 })); check(!s.p.castUntil && s.fx.some(e => e.type === 'cancel'), 'dash cancels Arcanist cast'); }

// ---------- Bellkeeper v1.0 encounter (reference/LAST-BELL-BELLKEEPER-BOSS-DESIGN-v1.0.md) ----------
const v1 = (extra = {}) => new R.Sim('vanguard', { hp: 'infinite', phase: 0, inv: true, ko: 'respawn', ally: false, seed: 7, strikes: false, encounter: 'v1', ...extra });
const record = (s, n, cmdFn = () => idle()) => { const log = []; for (let i = 0; i < n; i++) { s.step(cmdFn(i, s)); for (const e of s.fx) log.push({ ...e, tick: e.tick }); s.fx.length = 0; } return log; };
const warns = log => log.filter(e => e.type === 'warn').map(e => e.attack);
// Phase 1 fixed teaching order, then a recovery window, looping
{ const s = v1(); const log = record(s, 1300);
  const w = warns(log);
  check(w.slice(0, 6).join() === 'sweep,toll,charge,chimeMarker,chimeMarker,chimeMarker', `phase 1 order (got ${w.slice(0, 6).join()})`);
  check(w.slice(6, 9).join() === 'sweep,toll,charge', 'phase 1 loops in the same order');
  check(log.some(e => e.type === 'vulnerable' && e.ticks === 54), 'phase 1 ends with a recovery window');
  check(s.p.hp === 10 && R.TICK === 60, 'v1.0 uses a 10-pip health bar'); }
// Bell Sweep tracks for 0.30 s, then the direction is frozen
{ const s = v1(); let h0 = null, dirAtLock = null, dirAtEnd = null;
  record(s, 160, (i, sim) => { const h = sim.hazards.find(x => x.attack === 'sweep'); if (h) { h0 = h; if (sim.tick === h.L) dirAtLock = h.dir; if (sim.tick === h.R - 1) dirAtEnd = h.dir; } return cmd({ mx: 1 }); });
  check(h0 && h0.L - h0.W === 18 && h0.R - h0.W === 57, 'sweep warns 0.95 s and locks after 0.30 s');
  check(dirAtLock !== null && dirAtLock === dirAtEnd, 'sweep direction does not change after lock'); }
// Pendulum Charge: lane locks at warning, boss lands at the lane end, next Hollow Toll centres on the new position
{ const s = v1({ phase: 1 }); let lane = null, toll = null;
  const log = record(s, 600, (i, sim) => { for (const h of sim.hazards) { if (h.attack === 'charge' && !lane) lane = { ...h }; if (h.attack === 'toll' && lane && !toll) toll = { ...h }; } return idle(); });
  check(lane && lane.locked && lane.R - lane.W === 48, 'charge lane is locked from its 0.80 s warning');
  check(lane && toll && Math.abs(toll.ox - lane.end.x) < 0.01 && Math.abs(toll.oy - lane.end.y) < 0.01, 'Pursuit: Hollow Toll follows the boss to its landing point');
  check(log.some(e => e.type === 'chargeGo'), 'boss physically charges');
  const inLane = { x: lane.ox + Math.cos(lane.dir) * lane.len * 0.5, y: lane.oy + Math.sin(lane.dir) * lane.len * 0.5 };
  const beside = { x: inLane.x - Math.sin(lane.dir) * (lane.inner + 5), y: inLane.y + Math.cos(lane.dir) * (lane.inner + 5) };
  check(R.contains(lane, inLane) && !R.contains(lane, beside), 'lane geometry: inside the 2.2 m lane hits, a step to the side is safe');
  const sides = new Set(); const s2 = v1({ phase: 1, seed: 3 }); record(s2, 3000, (i, sim) => { for (const h of sim.hazards) if (h.attack === 'charge') sides.add(h.side); return idle(); });
  check(sides.has(1) && sides.has(-1), 'Phase 2 charges use both seeded left/right approaches'); }
// Shattered Chime markers follow briefly, then lock
{ const s = v1(); let m = null, posAtLock = null, posAtEnd = null;
  record(s, 520, (i, sim) => { const h = sim.hazards.find(x => x.attack === 'chimeMarker'); if (h && !m) m = h; if (m && sim.tick === m.L) posAtLock = [m.ox, m.oy]; if (m && sim.tick === m.R - 1) posAtEnd = [m.ox, m.oy]; return cmd({ mx: Math.sin(i * 0.05), my: Math.cos(i * 0.05) }); });
  check(m && posAtLock && posAtEnd && posAtLock[0] === posAtEnd[0] && posAtLock[1] === posAtEnd[1], 'chime marker cannot move after lock'); }
// Damage tiers and post-hit protection
{ const s = v1({ inv: false }); const log = record(s, 160);
  const hit = log.find(e => e.type === 'outcome' && e.outcome === 'hit');
  check(hit && hit.h.attack === 'sweep' && s.p.hp === 5, `standing in the sweep costs ~50 % (hp ${s.p.hp})`); }
// Phase transitions wait for the active pattern
{ const s = new R.Sim('vanguard', { hp: 'standard', phase: 0, inv: true, ko: 'respawn', ally: false, seed: 7, strikes: false, encounter: 'v1' });
  record(s, 200); s.bossHp = Math.round(s.bossMax * 0.69);
  check(s.phase === 0, 'crossing 70 % mid-pattern does not interrupt');
  const log = record(s, 900); const ph = log.find(e => e.type === 'phase');
  const lastP1 = log.filter(e => e.type === 'warn' && e.tick < (ph ? ph.tick : 1e9)).pop();
  check(ph && ph.phase === 1 && s.hazards.every(h => h.W >= ph.tick), 'transition happens only after the pattern resolved');
  check(!!lastP1, 'phase 1 material finished before the transition'); }
// Phase 3: The Final Toll order and its decisive strike
{ const s = v1({ phase: 2 }); const log = record(s, 700);
  const w = warns(log).slice(0, 6);
  check(w.join() === 'charge,toll,chimeMarker,chimeMarker,chimeMarker,sweep', `Final Toll order (got ${w.join()})`);
  const sweep = log.find(e => e.type === 'warn' && e.attack === 'sweep');
  check(sweep && sweep.h.decisive && sweep.h.dmg === 8, 'Final Toll last strike targets ~80 % max HP');
  check(log.some(e => e.type === 'vulnerable' && e.extended && e.ticks === 120), 'Final Toll ends with ~2 s of vulnerability'); }
// Seeded variation is reproducible
{ const trace = seed => { const s = v1({ phase: 1, seed }); return record(s, 2500).filter(e => e.type === 'warn').map(e => e.attack + ':' + e.tick + ':' + (e.h.side || 0) + ':' + e.h.dir.toFixed(4)).join('|'); };
  check(trace(5) === trace(5), 'same seed reproduces the same variations'); }
// Fairness gate (§11): from the worst point inside each attack, walking out must fit in the warning after lock
{ const react = 18, v = R.SPEED / R.TICK; const worst = {};
  const escape = h => { // straight-line distance from p to the nearest safe point (walls ignored except as the sample limit)
    let wd = 0;
    for (let x = R.ARENA.x0 + 14; x <= R.ARENA.x1 - 14; x += 10) for (let y = R.ARENA.y0 + 14; y <= R.ARENA.y1 - 14; y += 10) {
      const p = { x, y }; if (!R.contains(h, p)) continue;
      const dx = x - h.ox, dy = y - h.oy, d = Math.hypot(dx, dy); let e;
      if (h.kind === 'circle') e = h.r - d;
      else if (h.kind === 'donut') e = Math.min(d - h.inner, h.r - d);
      else if (h.kind === 'cone') { const a = Math.abs(Math.atan2(Math.sin(Math.atan2(dy, dx) - h.dir), Math.cos(Math.atan2(dy, dx) - h.dir))); e = Math.min(h.r - d, d * Math.sin(Math.max(0, h.half - a))); }
      else { const c = Math.cos(h.dir), s = Math.sin(h.dir), al = dx * c + dy * s, pe = -dx * s + dy * c; e = Math.min(h.inner - Math.abs(pe), h.len - al); }
      wd = Math.max(wd, e);
    }
    return wd;
  };
  for (const phase of [0, 1, 2]) { const s = v1({ phase, seed: 9 }); record(s, 1500, (i, sim) => { for (const h of sim.hazards) if (h.W === sim.tick) { const window = h.R - Math.max(h.W, h.L), e = escape(h) + 2; const walk = react + Math.ceil(e / v), dash = react + Math.min(Math.ceil(e / v), R.DASH.duration + Math.ceil(Math.max(0, e - R.DASH.distance) / v)); const k = h.attack; if (!worst[k] || dash - window > worst[k].dash - worst[k].window) worst[k] = { walk, dash, window }; } return cmd({ mx: Math.sin(i * 0.03) * 0.6, my: Math.cos(i * 0.02) * 0.6 }); }); }
  for (const [k, w] of Object.entries(worst)) check(w.dash <= w.window, `${k}: escape with the universal dash needs ${w.dash} ticks, window ${w.window}`);
  const walkOnly = Object.entries(worst).filter(([, w]) => w.walk > w.window).map(([k, w]) => `${k} (walk needs ${w.walk}, window ${w.window})`);
  console.error('fairness margins with dash (ticks):', Object.fromEntries(Object.entries(worst).map(([k, w]) => [k, w.window - w.dash])));
  if (walkOnly.length) console.error('DESIGN WARNING, not escapable by walking alone from the worst point:', walkOnly.join('; ')); }

console.log(JSON.stringify({ evidenceType: 'js-rules-parity', assertions, failures }, null, 2));
process.exit(failures.length ? 1 : 0);
