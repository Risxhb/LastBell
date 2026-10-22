// Turn annotated Ghidra pseudo-C of a YYC bp_ script into a flat event list. Read-only.
// YYC passes script arguments as an array of RValue pointers on the stack: each `puStack_N = auStack_X`
// fills array slot N, and a call `fn(param_1,param_2,&ret,argc,&puStack_N)` reads argc pointers from N
// downward in 8-byte steps. We track what literal went into each auStack slot and follow those pointers.
// bpsw_* calls (difficulty / player-count switches) become values like d(normal/hard/lunar).
// Usage: node parse.mjs <annotated.c>  -> prints JSON {events, adds}
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const hexDouble = h => { const b = Buffer.alloc(8); b.writeBigUInt64BE(BigInt(h)); return +b.readDoubleBE(0).toFixed(4); };
const slotName = s => s.replace(/^&/, '').trim();

export function parse(src) {
  const slot = new Map();   // auStack_X / uStack_X -> {v} or {key}
  const ptr = new Map();    // N (offset of puStack_N) -> slot name
  const vars = new Map();   // uVarN -> value from a switch call
  let lastPattern = null;
  const events = [];
  const argsAt = (n, argc) => { const out = []; for (let i = 0; i < argc; i++) { const s = ptr.get(n - 8 * i); out.push(s && slot.has(s) ? slot.get(s) : { v: '<expr>' }); } return out; };
  const show = a => a.key ?? a.v;

  for (const raw of src.split('\n')) {
    const line = raw.trim();
    let m;
    if ((m = line.match(/P\(([\w]+)\)/))) lastPattern = m[1];
    if ((m = line.match(/^(uVar\d+) = /)) && !/= bpsw_/.test(line)) vars.delete(m[1]); // register reused
    if ((m = line.match(/FUN_1401435d0\(([&\w]+),S\((\w+)\)\)/))) { slot.set(slotName(m[1]), { key: m[2] }); continue; }
    if ((m = line.match(/FUN_140143660\(([&\w]+),K\(([^)]*)\)\)/))) { slot.set(slotName(m[1]), { v: m[2] }); continue; }
    if ((m = line.match(/FUN_140143660\(([&\w]+),&DAT_143e01118\)/))) { slot.set(slotName(m[1]), { v: '0' }); continue; } // shared zero constant (inferred)
    if ((m = line.match(/^(\w+) = FUN_1427a7de0\((0x[0-9a-f]+)\)/))) { slot.set(m[1], { v: `random(${hexDouble(m[2])})` }); continue; }
    if ((m = line.match(/FUN_14014(?:35d0|34f0)\(([&\w]+),(uVar\d+)\)/))) { slot.set(slotName(m[1]), { v: vars.get(m[2]) ?? '<expr>' }); continue; }
    if ((m = line.match(/^puStack_([0-9a-f]+) = &?(\w+);/))) { ptr.set(parseInt(m[1], 16), m[2]); continue; }
    const call = line.match(/\b((?:scrbp|bpatt|bpsw|ttrg|bp|math)_\w+)\([^,()]+,[^,()]+,[^,()]+,(0x[0-9a-f]+|\d+),(?:&puStack_([0-9a-f]+)|0)\)/);
    if (call) {
      const argc = parseInt(call[2]);
      const args = call[3] ? argsAt(parseInt(call[3], 16), argc) : [];
      const assign = line.match(/^(\w+) = /);
      if (/^bpsw_/.test(call[1]) && assign) vars.set(assign[1], `${call[1].replace('bpsw_', '')}(${args.map(show).join('/')})`);
      events.push({ fn: call[1], argc, args, pattern: call[1] === 'bpatt_add' ? lastPattern : undefined });
    }
  }

  const adds = [];
  let pv = {}, time = null;
  for (const e of events) {
    if (/^scrbp_time/.test(e.fn)) { time = `${e.fn.replace('scrbp_', '')}(${e.args.map(show).join(',')})`; pv = {}; } // pattVars last until the time block ends
    else if (e.fn === 'bpatt_var_reset') pv = {};
    else if (e.fn === 'bpatt_var') { for (let i = 0; i + 1 < e.args.length; i += 2) if (e.args[i].key) pv[e.args[i].key] = show(e.args[i + 1]); }
    else if (e.fn === 'bpatt_add') adds.push({ time, pattern: e.pattern ?? '?', vars: { ...pv } });
  }
  return { events, adds };
}

if (process.argv[1] === fileURLToPath(import.meta.url) && process.argv[2]) console.log(JSON.stringify(parse(fs.readFileSync(process.argv[2], 'utf8')), null, 1));
