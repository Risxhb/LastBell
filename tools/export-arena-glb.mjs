// Exports the key-art arena (prototype/arena-3d/arena-env.js) to Unity:
//   unity/Assets/LastBell/Arenas/KeyArt/BellkeepersDais.glb  (all solid geometry, materials, textures)
//   unity/Assets/LastBell/Arenas/KeyArt/FloorHeight.png      (floor height map; Unity makes it a normal map)
// Runs arena-look.html?export in headless Edge and reads window.__arenaExport over the DevTools protocol.
// Usage: node tools/export-arena-glb.mjs
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..');
const page = pathToFileURL(join(repo, 'prototype/arena-3d/arena-look.html')).href + '?export&clean&view=game';
const outDir = join(repo, 'unity/Assets/LastBell/Arenas/KeyArt');
const edge = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles].map(p => p && join(p, 'Microsoft/Edge/Application/msedge.exe')).find(p => p && existsSync(p));
if (!edge) throw new Error('Microsoft Edge not found');

const port = 9333, profile = join(tmpdir(), 'lb-export-profile');
const proc = spawn(edge, ['--headless=new', '--use-angle=d3d11', '--enable-unsafe-swiftshader', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--window-size=1280,720', page], { stdio: 'ignore' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
try {
  let target;
  for (let i = 0; i < 60 && !target; i++) { await sleep(500); try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find(t => t.type === 'page'); } catch {} }
  if (!target) throw new Error('DevTools target not found');
  const ws = new WebSocket(target.webSocketDebuggerUrl); await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  const evaluate = expr => new Promise(res => { const n = ++id; pending.set(n, res); ws.send(JSON.stringify({ id: n, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true } })); })
    .then(m => { if (m.result.exceptionDetails) throw new Error(m.result.exceptionDetails.text); return m.result.result.value; });
  let ready = false;
  for (let i = 0; i < 240 && !ready; i++) { await sleep(500); ready = await evaluate('!!window.__arenaExport'); }
  if (!ready) throw new Error('export did not finish (check the page console)');
  const read = async key => { const len = await evaluate(`window.__arenaExport.${key}.length`); let s = ''; for (let o = 0; o < len; o += 4e6) s += await evaluate(`window.__arenaExport.${key}.slice(${o}, ${o + 4e6})`); return Buffer.from(s, 'base64'); };
  mkdirSync(outDir, { recursive: true });
  const glb = await read('glb'), bump = await read('bump');
  writeFileSync(join(outDir, 'BellkeepersDais.glb'), glb); writeFileSync(join(outDir, 'FloorHeight.png'), bump);
  console.log(`BellkeepersDais.glb ${(glb.length / 1e6).toFixed(1)} MB, FloorHeight.png ${(bump.length / 1e6).toFixed(1)} MB -> ${outDir}`);
  ws.close();
} finally { proc.kill(); await sleep(500); try { rmSync(profile, { recursive: true, force: true }); } catch {} }
