// Map runtime string and variable-ID globals (anywhere in .data) to the literal each is filled from at start-up:
// the nearest preceding `lea r64,[rip+string]` within 40 bytes of an instruction that references the global.
// The most common string per global wins. Read-only. Usage: node keymap.mjs <exe> <out.json>
import fs from 'node:fs';
const [exePath, outPath] = process.argv.slice(2);
const b = fs.readFileSync(exePath);
const base = 0x140000000;
const T = { va: 0x1000, raw: 0x400, rsize: 51152384 }, R = { va: 51159040, raw: 51153408, rsize: 8348160 };
const D = { va: 59510784, rawSize: 1931776, vsize: 6135148 };
const t2r = o => o - T.raw + T.va;
const strAt = r => {
  const o = r - R.va + R.raw; if (o < R.raw || o >= R.raw + R.rsize) return null;
  const s = b.toString('latin1', o, Math.min(o + 64, b.length)).split('\0')[0];
  return /^[A-Za-z_][\w]{0,60}$/.test(s) ? s : null;
};
const lastStr = []; // ring of recent string leas: [pos, string]
const votes = new Map();
for (let p = T.raw; p < T.raw + T.rsize - 7; p++) {
  if ((b[p] & 0xf0) === 0x40 && b[p + 1] === 0x8d && (b[p + 2] & 0xc7) === 0x05) {
    const s = strAt(t2r(p + 7) + b.readInt32LE(p + 3));
    if (s) { lastStr.push([p, s]); if (lastStr.length > 4) lastStr.shift(); continue; }
  }
  const tg = t2r(p + 4) + b.readInt32LE(p);
  if (tg < D.va || tg >= D.va + D.vsize) continue;
  const prev = lastStr.length ? lastStr[lastStr.length - 1] : null;
  if (!prev || p - prev[0] > 40 || p - prev[0] < 7) continue;
  const key = (tg + base).toString(16);
  const m = votes.get(key) ?? new Map(); m.set(prev[1], (m.get(prev[1]) ?? 0) + 1); votes.set(key, m);
}
const out = {};
for (const [k, m] of votes) out['0x' + k] = [...m.entries()].sort((a, c) => c[1] - a[1])[0][0];
fs.writeFileSync(outPath, JSON.stringify(out));
console.log('globals mapped', Object.keys(out).length, 'sample', out['0x143e3e0e8'], out['0x143e3e178'], out['0x143e3e1d8']);
