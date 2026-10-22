// Annotate Ghidra pseudo-C from RabbitSteel.exe: FUN_<addr> -> script name (from the .pdata/name-string scan),
// &DAT_<addr> -> its constant RValue (real, int, bool or string) when the address holds one. Read-only.
// Usage: node annotate.mjs <exe> <names.json> <in.c> <out.c>
import fs from 'node:fs';
const [exePath, namesPath, inPath, outPath] = process.argv.slice(2);
const b = fs.readFileSync(exePath);
const pe = b.readUInt32LE(0x3c), nsec = b.readUInt16LE(pe + 6), optSize = b.readUInt16LE(pe + 20), opt = pe + 24;
const base = Number(b.readBigUInt64LE(opt + 24));
const secs = [];
for (let i = 0; i < nsec; i++) {
  const o = opt + optSize + i * 40;
  secs.push({ va: b.readUInt32LE(o + 12), raw: b.readUInt32LE(o + 20), rsize: b.readUInt32LE(o + 16) });
}
const rva2off = r => { for (const s of secs) if (r >= s.va && r < s.va + s.rsize) return r - s.va + s.raw; return -1; };
const names = JSON.parse(fs.readFileSync(namesPath, 'utf8')); // { "0x<va hex>": "name" }

// YYC constant RValue: { value 8 bytes, flags 4, kind 4 }. kind 0 real, 1 string (ptr to RefString{char*}), 7 int32, 10 int64, 13 bool.
function rvalue(va) {
  const o = rva2off(va - base);
  if (o < 0 || o + 16 > b.length) return null;
  const kind = b.readUInt32LE(o + 12);
  if (kind === 0) { const d = b.readDoubleLE(o); return Number.isFinite(d) && Math.abs(d) < 1e9 && (d === 0 || Math.abs(d) > 1e-6) ? String(+d.toFixed(4)) : null; }
  if (kind === 7) return String(b.readInt32LE(o));
  if (kind === 10) return String(b.readBigInt64LE(o));
  if (kind === 13) return b.readDoubleLE(o) ? 'true' : 'false';
  if (kind === 1) {
    const ref = Number(b.readBigUInt64LE(o)) - base; const ro = rva2off(ref);
    if (ro < 0) return null;
    const sp = Number(b.readBigUInt64LE(ro)) - base; const so = rva2off(sp);
    if (so < 0) return null;
    const s = b.toString('latin1', so, b.indexOf(0, so));
    return /^[\x20-\x7e]{1,80}$/.test(s) ? JSON.stringify(s) : null;
  }
  return null;
}

// Optional maps: runtime string globals (keymap.mjs) and the data.win SCPT index (script id = 100000 + index).
const keys = process.argv[6] ? JSON.parse(fs.readFileSync(process.argv[6], 'utf8')) : {};
const scpt = new Map();
if (process.argv[7]) for (const l of fs.readFileSync(process.argv[7], 'utf8').split('\n')) { const [i, n] = l.split('\t'); if (n) scpt.set(100000 + +i, n.replace(/^gml_Script_/, '')); }
const scriptRef = h => { const o = rva2off(parseInt(h, 16) - base); if (o < 0) return null; return scpt.get(b.readInt32LE(o)) ?? null; };

let src = fs.readFileSync(inPath, 'utf8');
src = src.replace(/FUN_([0-9a-f]{9,})/g, (m, h) => names['0x' + h] ? `${names['0x' + h]}` : m);
src = src.replace(/DAT_([0-9a-f]{9,}) (\| 0x5000000|& 0xffffff)/g, (m, h) => { const n = scriptRef(h); return n ? `P(${n})` : m; });
src = src.replace(/&DAT_([0-9a-f]{9,})/g, (m, h) => {
  const k = keys['0x' + h]; if (k) return `S(${k})`;
  const v = rvalue(parseInt(h, 16)); return v === null ? m : `K(${v})`;
});
src = src.replace(/\bDAT_([0-9a-f]{9,})/g, (m, h) => keys['0x' + h] ? `V(${keys['0x' + h]})` : m);
fs.writeFileSync(outPath, src);
