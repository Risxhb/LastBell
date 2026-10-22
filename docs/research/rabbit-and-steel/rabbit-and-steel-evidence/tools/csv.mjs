// Extract key/level/English columns from R&S CSVs. Usage: node csv.mjs <dataDir> <outDir>
import fs from 'node:fs';
import path from 'node:path';
const [dir, out] = process.argv.slice(2);

function parse(text) {
  const rows = []; let row = [], f = '', q = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) { if (c === '"') { if (text[i + 1] === '"') { f += '"'; i++; } else q = false; } else f += c; }
    else if (c === '"') q = true;
    else if (c === ',') { row.push(f); f = ''; }
    else if (c === '\n') { row.push(f.replace(/\r$/, '')); rows.push(row); row = []; f = ''; }
    else f += c;
  }
  if (f || row.length) { row.push(f); rows.push(row); }
  return rows;
}

const files = ['Titles_Enemy', 'Names_HBS', 'Descriptions_HBS', 'Strings_Effect', 'Strings_Menu', 'Names_Move', 'Descriptions_Move', 'Strings', 'Strings2', 'Strings_Unlock'];
for (const name of files) {
  const rows = parse(fs.readFileSync(path.join(dir, name + '.csv'), 'utf8').replace(/^﻿/, ''));
  const head = rows[0].map(h => h.trim());
  const ki = 0, li = head.indexOf('level'), ei = head.indexOf('English');
  const lines = rows.slice(1).filter(r => r[ki]).map(r => [r[ki], li >= 0 ? r[li] : '', r[ei]].join('\t'));
  fs.writeFileSync(path.join(out, `csv-${name}-english.tsv`), 'key\tlevel\tEnglish\n' + lines.join('\n'));
  console.log(name, lines.length);
}
