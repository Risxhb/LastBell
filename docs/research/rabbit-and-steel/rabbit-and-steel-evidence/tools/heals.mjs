// List boss heals and HP-threshold checks per script. Usage: node heals.mjs (run from the evidence folder)
import fs from 'node:fs';
import { parse } from './parse.mjs';
const out = {};
for (const f of fs.readdirSync('ghidra-annotated')) {
  const r = parse(fs.readFileSync('ghidra-annotated/' + f, 'utf8'));
  for (const e of r.events) if (/^scrbp_(boss_heal|boss_heal_even|health_threshold)$/.test(e.fn))
    (out[f.replace(/\.c$/, '')] ??= []).push(e.fn.replace('scrbp_', '') + '(' + e.args.map(a => a.v ?? a.key).join(',') + ')');
}
for (const k of Object.keys(out)) out[k] = [...new Set(out[k])];
fs.writeFileSync('boss-heal-and-thresholds.json', JSON.stringify(out, null, 1));
for (const [k, v] of Object.entries(out)) console.log(k.padEnd(32), v.join(' '));
