import fs from 'node:fs';
const G = JSON.parse(fs.readFileSync(process.argv[2])).graph;
const bosses = {};
for (const [k, v] of Object.entries(G)) {
  const m = k.match(/^bp_((?:wolf|dragon|mouse|frog|bird|queens|rabbit|depths|geode|sanct|aurum|heart|toybox|spelllock)[a-z]*?_?[a-z_]*?)(\d)?(?:_.*)?$/);
  if (!m || k.startsWith('bp_dialog') || k.startsWith('bp_move_position')) continue;
  const boss = m[1].replace(/_$/, '') + (m[2] !== undefined ? m[2] : '');
  const b = bosses[boss] ??= { scripts: [], calls: {} };
  b.scripts.push(k);
  for (const [c, n] of Object.entries(v.calls)) b.calls[c] = (b.calls[c] || 0) + n;
}
const generic = {};
for (const [k, v] of Object.entries(G)) if (k.startsWith('bp_') && !Object.values(bosses).some(b => b.scripts.includes(k))) generic[k] = Object.keys(v.calls).sort();
const callers = {};
for (const [k, v] of Object.entries(G)) for (const c of Object.keys(v.calls)) if (c.startsWith('bp_')) (callers[c] ??= []).push(k);
fs.writeFileSync(process.argv[3], JSON.stringify({ note: 'Aggregated from exe-yyc-script-callgraph.json. bosses: union of direct calls made by every bp_<boss><variant>* script. generic: direct calls made by shared bp_ pattern scripts. bp_callers: who calls a bp_ script directly.', bosses, generic, bp_callers: callers }, null, 1));
for (const [k, b] of Object.entries(bosses)) console.log(k, b.scripts.length, Object.keys(b.calls).filter(c => !/^(scrbp_time|bpatt_|sbgv|math_|scrbp_animation|scrbp_end|scrbp_dialog|scrbp_sound)/.test(c)).sort().join(' '));
