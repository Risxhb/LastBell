// Annotate every decompiled file, parse it, and write boss-pattern-adds.json. Usage: node annotate-all.mjs <exe>
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { parse } from './parse.mjs';
const exe = process.argv[2];
const here = path.dirname(fileURLToPath(import.meta.url));
const ev = path.join(here, '..');
const out = {};
for (const f of fs.readdirSync(path.join(ev, 'ghidra-decompiled')).sort()) {
  const inp = path.join(ev, 'ghidra-decompiled', f), ann = path.join(ev, 'ghidra-annotated', f);
  execFileSync('node', [path.join(here, 'annotate.mjs'), exe, path.join(ev, 'exe-yyc-function-names.json'), inp, ann, path.join(ev, 'exe-yyc-string-globals.json'), path.join(ev, 'datawin-scpt-index.tsv')]);
  const r = parse(fs.readFileSync(ann, 'utf8'));
  out[f.replace(/\.c$/, '')] = { calls: [...new Set(r.events.map(e => e.fn))], adds: r.adds, times: r.events.filter(e => /^scrbp_time/.test(e.fn)).map(e => `${e.fn.replace('scrbp_', '')}(${e.args.slice(-e.argc).map(a => a.v ?? a.key).join(',')})`) };
}
fs.writeFileSync(path.join(ev, 'boss-pattern-adds.json'), JSON.stringify(out, null, 1));
console.log('scripts', Object.keys(out).length, 'adds', Object.values(out).reduce((s, x) => s + x.adds.length, 0));
