#!/usr/bin/env node
// Release-Text fuer einen Versions-Tag aus dem Changelog (docker.yml, Job `github-release`).
//
//   node scripts/release-notes.mjs <tag> [<voriger-tag>] [<changelog-data.ts>]
//
// Gibt Markdown aus: alle Changelog-Eintraege NACH dem vorigen Tag bis einschliesslich <tag>,
// deutsch, darunter eingeklappt die englische Fassung. Ohne vorigen Tag nur der Eintrag von
// <tag>. Bricht ab, wenn <tag> keinen Changelog-Eintrag hat — ein Tag ohne Eintrag ist
// fast immer ein Tippfehler im Tag-Namen, und ein leerer Release faellt niemandem auf.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const [tagArg, prevArg, fileArg] = process.argv.slice(2);
if (!tagArg) {
  console.error('Aufruf: release-notes.mjs <tag> [<voriger-tag>] [<changelog-data.ts>]');
  process.exit(2);
}

const parse = (v) => {
  const m = /^v?(\d+)\.(\d+)\.(\d+)$/.exec(v ?? '');
  if (!m) throw new Error(`keine Version vX.Y.Z: ${v}`);
  return m.slice(1).map(Number);
};
const cmp = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];

// changelog-data.ts ist ein reines Array-Literal hinter einem Typ-Import — ohne Import und
// Typangabe ist es gueltiges JavaScript. Stammt aus dem getaggten Commit selbst.
const file = fileArg ?? join(here, '../src/frontend/app/src/environments/changelog-data.ts');
const source = readFileSync(file, 'utf8')
  .replace(/^import .*$/m, '')
  .replace(/export const CHANGELOG\s*:\s*ChangelogEntry\[\]\s*=/, 'return');
const entries = new Function(source)();

const tag = parse(tagArg);
const prev = prevArg ? parse(prevArg) : null;
const picked = entries.filter((e) => {
  const v = parse(e.version);
  return cmp(v, tag) <= 0 && (prev ? cmp(v, prev) > 0 : cmp(v, tag) === 0);
});
if (!picked.some((e) => cmp(parse(e.version), tag) === 0)) {
  console.error(`Kein Changelog-Eintrag fuer ${tagArg} in ${file}`);
  process.exit(1);
}

const lines = [];
for (const e of picked) {
  lines.push(`### ${e.version} (${e.date})`, '');
  for (const c of e.changes) lines.push(`- ${c.de ?? c.en}`);
  lines.push('');
}
lines.push('<details><summary>English</summary>', '');
for (const e of picked) {
  lines.push(`#### ${e.version}`, '');
  for (const c of e.changes) lines.push(`- ${c.en}`);
  lines.push('');
}
lines.push('</details>');
// GitHub nimmt hoechstens 125 000 Zeichen; nach langer Tag-Pause (dutzende Versionen) wird gekuerzt
// statt dass der Release-Schritt scheitert.
const LIMIT = 120_000;
let text = lines.join('\n') + '\n';
if (text.length > LIMIT) text = text.slice(0, LIMIT) + '\n\n… (gekürzt — vollständig im Changelog der App)\n';
process.stdout.write(text);
