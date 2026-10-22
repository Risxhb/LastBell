// List enrage timer values: bpsw_enrage_time arguments and the time blocks of *_enrage scripts. Run from evidence folder.
import fs from 'node:fs';
import { parse } from './parse.mjs';
const out = {};
for (const f of fs.readdirSync('ghidra-annotated')) {
  const r = parse(fs.readFileSync('ghidra-annotated/' + f, 'utf8'));
  const name = f.replace(/\.c$/, '');
  for (const e of r.events) {
    if (e.fn === 'bpsw_enrage_time') (out[name] ??= []).push('enrage_time(' + e.args.map(a => a.v ?? a.key).join('/') + ')');
    if (/_enrage$/.test(name) && /^scrbp_time/.test(e.fn)) (out[name] ??= []).push(e.fn.replace('scrbp_', '') + '(' + e.args.map(a => a.v ?? a.key).join(',') + ')');
  }
}
for (const k of Object.keys(out)) out[k] = [...new Set(out[k])];
fs.writeFileSync('boss-enrage-times.json', JSON.stringify(out, null, 1));
for (const [k, v] of Object.entries(out)) console.log(k.padEnd(32), v.join(' '));
