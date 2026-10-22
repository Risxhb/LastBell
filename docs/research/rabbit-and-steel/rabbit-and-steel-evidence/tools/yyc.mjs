// Static read of a YYC exe: name each gml_Script function from the name string it loads
// (lea reg,[rip+name]), bound it with .pdata, and list direct calls between named scripts.
// Nothing is executed. Usage: node yyc.mjs <exe> <outDir>
import fs from 'node:fs';
import path from 'node:path';
const [exePath, out] = process.argv.slice(2);
const b = fs.readFileSync(exePath);
const pe = b.readUInt32LE(0x3c), nsec = b.readUInt16LE(pe + 6), optSize = b.readUInt16LE(pe + 20), opt = pe + 24;
const secs = [];
for (let i = 0; i < nsec; i++) {
  const o = opt + optSize + i * 40;
  secs.push({ name: b.toString('latin1', o, o + 8).replace(/\0+$/, ''), va: b.readUInt32LE(o + 12), vsize: b.readUInt32LE(o + 8), raw: b.readUInt32LE(o + 20), rsize: b.readUInt32LE(o + 16) });
}
const sec = n => secs.find(s => s.name === n);
const text = sec('.text'), rdata = sec('.rdata'), pdata = sec('.pdata');
const t2r = off => off - text.raw + text.va, r2t = rva => rva - text.va + text.raw;

// 1. .pdata RUNTIME_FUNCTION entries (begin, end, unwind)
const funcs = [];
for (let o = pdata.raw; o < pdata.raw + pdata.vsize; o += 12) {
  const s = b.readUInt32LE(o), e = b.readUInt32LE(o + 4);
  if (s && e > s) funcs.push([s, e]);
}
funcs.sort((x, y) => x[0] - y[0]);
const findFunc = rva => { let lo = 0, hi = funcs.length - 1; while (lo <= hi) { const m = (lo + hi) >> 1; if (funcs[m][0] <= rva) { if (rva < funcs[m][1]) return m; lo = m + 1; } else hi = m - 1; } return -1; };

// 2. gml_Script_ name strings in .rdata
const names = new Map();
const pfx = Buffer.from('gml_Script_', 'latin1');
for (let i = b.indexOf(pfx, rdata.raw); i >= 0 && i < rdata.raw + rdata.rsize; i = b.indexOf(pfx, i + 1)) {
  if (b[i - 1] !== 0) continue;
  names.set(i - rdata.raw + rdata.va, b.toString('latin1', i, b.indexOf(0, i)));
}

// 3. lea r64,[rip+disp32] referencing a name -> owning function
const owner = new Map(); // func index -> {name, dist}
for (let p = text.raw; p < text.raw + text.rsize - 7; p++) {
  if ((b[p] & 0xf0) !== 0x40 || b[p + 1] !== 0x8d || (b[p + 2] & 0xc7) !== 0x05) continue;
  const tgt = t2r(p + 7) + b.readInt32LE(p + 3);
  const nm = names.get(tgt); if (!nm) continue;
  const fi = findFunc(t2r(p)); if (fi < 0) continue;
  const dist = t2r(p) - funcs[fi][0];
  const cur = owner.get(fi);
  if (!cur || dist < cur.dist) owner.set(fi, { name: nm, dist });
}
const startToName = new Map();
for (const [fi, v] of owner) startToName.set(funcs[fi][0], v.name.replace(/^gml_Script_/, ''));

// 3b. every named function start, for annotate.mjs
const allNames = {};
for (const [st, n] of startToName) allNames["0x" + (st + 0x140000000).toString(16)] = n;
fs.writeFileSync(path.join(out, "exe-yyc-function-names.json"), JSON.stringify(allNames));

// 4. direct calls (E8 rel32) between named functions
const graph = {};
for (const [fi, v] of owner) {
  const name = v.name.replace(/^gml_Script_/, '');
  if (!/^(bp_|scrbp_)/.test(name)) continue;
  const [s, e] = funcs[fi];
  const calls = {};
  for (let p = r2t(s); p < r2t(e) - 5; p++) {
    if (b[p] !== 0xe8) continue;
    const n = startToName.get(t2r(p + 5) + b.readInt32LE(p + 1));
    if (n && n !== name) calls[n] = (calls[n] || 0) + 1;
  }
  graph[name] = { rva: '0x' + s.toString(16), bytes: e - s, calls };
}
fs.writeFileSync(path.join(out, 'exe-yyc-script-callgraph.json'), JSON.stringify({
  note: 'Static only. Each function is named by the gml_Script_ string it loads nearest its start (YYC stack-trace entry), bounded by .pdata. Calls are direct E8 rel32 to other named scripts, with counts. Indirect calls (script variables, method calls) are not seen, so absence of a call is not proof.',
  pdata_functions: funcs.length, name_strings: names.size, named_functions: owner.size, graph }, null, 1));
console.log('pdata', funcs.length, 'names', names.size, 'named', owner.size, 'graph', Object.keys(graph).length);
