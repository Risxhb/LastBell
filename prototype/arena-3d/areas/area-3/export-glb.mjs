// Exports the Verdant Chimes arena (verdant-arena.js) to Unity, the same way tools/export-arena-glb.mjs does for the dais:
//   unity/Assets/LastBell/Areas/Area3/Source/VerdantChimes.glb   (all solid geometry, named materials, markers)
//   unity/Assets/LastBell/Areas/Area3/Source/FloorAlbedo.png     (4K floor colour)
//   unity/Assets/LastBell/Areas/Area3/Source/FloorHeight.png     (4K floor height; Unity turns it into a normal map)
//   unity/Assets/LastBell/Areas/Area3/Source/FloorMask.png       (4K: R moss, G brass, B emissive mosaic grout)
// Runs verdant-arena.html?export in headless Edge and reads window.__arenaExport over the DevTools protocol.
// Usage: node prototype/arena-3d/areas/area-3/export-glb.mjs [outDir]
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url)), repo = join(here, '../../../..');
const page = pathToFileURL(join(here, 'verdant-arena.html')).href + '?export';
const outDir = process.argv[2] || join(repo, 'unity/Assets/LastBell/Areas/Area3/Source');
const edge = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles].map(p => p && join(p, 'Microsoft/Edge/Application/msedge.exe')).find(p => p && existsSync(p));
if (!edge) throw new Error('Microsoft Edge not found');

const port = 9343, profile = join(tmpdir(), 'lb-export-area3');
const proc = spawn(edge, ['--headless=new', '--use-angle=d3d11', '--enable-unsafe-swiftshader', '--allow-file-access-from-files', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--window-size=1280,720', page], { stdio: 'ignore' });
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
  for (let i = 0; i < 360 && !ready; i++) { await sleep(500); ready = await evaluate('!!window.__arenaExport'); }
  if (!ready) throw new Error('export did not finish (check the page console)');
  const err = await evaluate('window.__arenaExport.error || null'); if (err) throw new Error('page: ' + err);
  const read = async key => { const len = await evaluate(`window.__arenaExport.${key}.length`); let s = ''; for (let o = 0; o < len; o += 4e6) s += await evaluate(`window.__arenaExport.${key}.slice(${o}, ${o + 4e6})`); return Buffer.from(s, 'base64'); };
  mkdirSync(outDir, { recursive: true });
  for (const [key, file] of [['glb', 'VerdantChimes.glb'], ['floorMap', 'FloorAlbedo.png'], ['floorHeight', 'FloorHeight.png'], ['floorMask', 'FloorMask.png']]) {
    const buf = await read(key); writeFileSync(join(outDir, file), buf); console.log(`${file} ${(buf.length / 1e6).toFixed(1)} MB`);
  }
  console.log(await evaluate('window.__arenaExport.stats'));
  console.log('-> ' + outDir);
  ws.close();
} finally { // Edge detaches its browser processes from the launcher, so stop every process using the export profile
  spawn('powershell', ['-NoProfile', '-Command', `Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -match 'lb-export-area3' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`], { stdio: 'ignore' }).on('exit', () => {});
  await sleep(2500); try { rmSync(profile, { recursive: true, force: true }); } catch {} }
