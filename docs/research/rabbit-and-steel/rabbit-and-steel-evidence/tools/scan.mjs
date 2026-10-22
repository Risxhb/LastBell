// Read-only scans of Rabbit and Steel files. Usage: node scan.mjs <gameDir> <outDir>
import fs from 'node:fs';
import path from 'node:path';
const [game, out] = process.argv.slice(2);

// 1. data.win chunk list (FORM container, 4-byte tag + u32 size)
const dw = fs.readFileSync(path.join(game, 'data.win'));
const chunks = [];
let off = 8;
while (off + 8 <= dw.length) {
  const tag = dw.toString('latin1', off, off + 4);
  const size = dw.readUInt32LE(off + 4);
  chunks.push({ tag, offset: off, size });
  off += 8 + size;
}
fs.writeFileSync(path.join(out, 'datawin-chunks.json'), JSON.stringify({ form: dw.toString('latin1', 0, 4), total: dw.length, chunks }, null, 1));

// 2. STRG strings (u32 count, u32 ptrs -> u32 len + bytes)
const strg = chunks.find(c => c.tag === 'STRG');
const strs = [];
if (strg) {
  const b = strg.offset + 8, n = dw.readUInt32LE(b);
  for (let i = 0; i < n; i++) {
    const p = dw.readUInt32LE(b + 4 + i * 4);
    const len = dw.readUInt32LE(p);
    strs.push(dw.toString('utf8', p + 4, p + 4 + len));
  }
}
fs.writeFileSync(path.join(out, 'datawin-strg-all.txt'), strs.join('\n'));

// 3. ASCII strings (len>=4) from exe
const exe = fs.readFileSync(path.join(game, 'RabbitSteel.exe'));
const ex = [];
let s = -1;
for (let i = 0; i <= exe.length; i++) {
  const c = i < exe.length ? exe[i] : 0;
  const ok = c >= 0x20 && c < 0x7f;
  if (ok && s < 0) s = i;
  if (!ok && s >= 0) { if (i - s >= 4) ex.push(exe.toString('latin1', s, i)); s = -1; }
}
fs.writeFileSync(path.join(out, 'exe-ascii-strings-raw.txt'), ex.join('\n'));
console.log('chunks', chunks.map(c => c.tag).join(','), 'strg', strs.length, 'exe strings', ex.length);
