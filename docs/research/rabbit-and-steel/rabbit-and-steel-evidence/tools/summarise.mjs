// Summarise boss-pattern-adds.json: telegraph window (spawnDelay - warningDelay) and sizes per pattern, per boss.
import fs from 'node:fs';
const A = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const lit = v => (v !== undefined && /^-?\d+(\.\d+)?$/.test(v)) ? +v : null;
const diff = [];
// A literal, or the Cute/Normal value of a d(normal/hard/lunar) switch; switched delays are also logged in diff.
const num = (v, key) => {
  if (lit(v) !== null) return lit(v);
  const m = /^d\(([^/]+)\/([^/]+)\/([^)]+)\)$/.exec(v ?? '');
  if (m && lit(m[1]) !== null) { if (key) diff.push([key, lit(m[1]), lit(m[2]), lit(m[3])]); return lit(m[1]); }
  return null;
};
const stat = xs => { xs = xs.filter(x => x !== null).sort((a, b) => a - b); if (!xs.length) return null; const q = p => xs[Math.min(xs.length - 1, Math.floor(p * xs.length))]; return { n: xs.length, min: xs[0], med: q(0.5), max: xs[xs.length - 1] }; };
const fam = {}, boss = {};
let unresolved = 0;
for (const [script, r] of Object.entries(A)) {
  const b = script.replace(/^bp_/, '').replace(/_pt\d+.*$/, '').replace(/_(h|sh|l|sl|enrage.*|painsplit|steel(_activate)?|magnus(_activate)?|holyshield|line|n|circle|pounce|flowershield|quickstart|perm|repeat|[bgpry])$/, '');
  for (const a of r.adds) {
    if (a.pattern === '?') { unresolved++; continue; }
    const w = num(a.vars.warningDelay, "warningDelay") ?? 0, s = num(a.vars.spawnDelay, "spawnDelay");
    const win = s === null || s - w <= 0 ? null : s - w; // non-positive = warning not used by this pattern or unresolved
    const f = fam[a.pattern] ??= { win: [], radius: [], width: [], fanAngle: [], kb: [], count: 0, bosses: new Set() };
    f.count++; f.bosses.add(b); f.win.push(win); f.radius.push(num(a.vars.radius)); f.width.push(num(a.vars.width)); f.fanAngle.push(num(a.vars.fanAngle)); f.kb.push(num(a.vars.kbAmount));
    const B = boss[b] ??= { patterns: {}, win: [] };
    B.patterns[a.pattern] = (B.patterns[a.pattern] ?? 0) + 1; B.win.push(win);
  }
}
const famOut = Object.fromEntries(Object.entries(fam).sort((a, b) => b[1].count - a[1].count).map(([k, f]) => [k, { count: f.count, bosses: f.bosses.size, telegraph_ms: stat(f.win), radius: stat(f.radius), width: stat(f.width), fanAngle: stat(f.fanAngle), kbAmount: stat(f.kb) }]));
const bossOut = Object.fromEntries(Object.entries(boss).sort().map(([k, b]) => [k, { telegraph_ms: stat(b.win), patterns: Object.fromEntries(Object.entries(b.patterns).sort((x, y) => y[1] - x[1])) }]));
fs.writeFileSync(process.argv[3], JSON.stringify({ note: 'telegraph_ms = spawnDelay - warningDelay (warningDelay missing = 0). Values are literals recovered from decompiled code; <expr> values (computed or difficulty-switched) are excluded from stats.', unresolved_adds: unresolved, difficulty_switched_delays: diff, families: famOut, bosses: bossOut }, null, 1));
console.log('unresolved', unresolved, 'difficulty-switched delays', diff.length); console.log(diff.slice(0,30).map(d=>d.join(" ")).join("; "));
for (const [k, f] of Object.entries(famOut).slice(0, 40)) console.log(k.padEnd(28), String(f.count).padStart(4), String(f.bosses).padStart(3), 'tele', JSON.stringify(f.telegraph_ms), 'r', JSON.stringify(f.radius), 'w', JSON.stringify(f.width), 'fan', JSON.stringify(f.fanAngle));
