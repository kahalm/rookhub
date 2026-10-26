#!/usr/bin/env node
// Abdeckung der Sprachdateien (Codereview F8-009, Entscheidung 2026-10-25 „a: Abdeckung anzeigen“).
//
// Zaehlt je public/i18n/<lang>.json, wie viele Schluessel von en.json dort stehen, und schreibt das Ergebnis in Prozent
// (gerundet) nach src/app/core/i18n-coverage.ts. Das Sprachmenue zeigt die Teilsprachen damit als „Français (17 %)“ —
// ohne zur Laufzeit alle 25 Dateien zu laden. Die Datei ist eingecheckt; `src/app/core/language-menu.spec.ts` rechnet
// in der CI nach und wird rot, wenn sie mehr als zwei Prozentpunkte vom Stand abweicht.
//
// Aufruf (aus src/frontend/app):
//   node scripts/i18n-coverage.mjs           neu schreiben
//   node scripts/i18n-coverage.mjs --check   nur pruefen (Exit 1, wenn die Datei nicht exakt passt)
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const I18N = join(ROOT, 'public', 'i18n');
const OUT = join(ROOT, 'src', 'app', 'core', 'i18n-coverage.ts');

/** Blatt-Schluessel eines verschachtelten Objekts als „a.b.c“. */
export function flatKeys(obj, prefix = '', out = []) {
  for (const [k, v] of Object.entries(obj ?? {})) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object') flatKeys(v, key, out); else out.push(key);
  }
  return out;
}

/** Prozent (gerundet) der en-Schluessel, die `lang` traegt. */
export function coverage(enKeys, langKeys) {
  const have = new Set(langKeys);
  const n = enKeys.filter(k => have.has(k)).length;
  return enKeys.length ? Math.round((100 * n) / enKeys.length) : 0;
}

export function computeAll(dir = I18N) {
  const read = f => JSON.parse(readFileSync(join(dir, f), 'utf8'));
  const en = flatKeys(read('en.json'));
  const result = {};
  for (const f of readdirSync(dir).filter(f => f.endsWith('.json')).sort())
    result[f.slice(0, -5)] = coverage(en, flatKeys(read(f)));
  return result;
}

export function render(cov) {
  const body = Object.entries(cov).map(([l, p]) => `  ${l}: ${p},`).join('\n');
  return `// GENERIERT von scripts/i18n-coverage.mjs (Codereview F8-009) — nicht von Hand aendern, sondern neu erzeugen:
//   cd src/frontend/app && node scripts/i18n-coverage.mjs
/** Anteil der en-Schluessel je Sprachdatei in Prozent (gerundet). */
export const I18N_COVERAGE: Readonly<Record<string, number>> = {
${body}
};
`;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const text = render(computeAll());
  if (process.argv.includes('--check')) {
    let cur = '';
    try { cur = readFileSync(OUT, 'utf8'); } catch { /* fehlt */ }
    if (cur !== text) {
      console.error('i18n-coverage.ts passt nicht zum Stand der Sprachdateien — node scripts/i18n-coverage.mjs');
      process.exit(1);
    }
    console.log('i18n-coverage.ts aktuell.');
  } else {
    writeFileSync(OUT, text);
    console.log(`geschrieben: ${OUT}`);
  }
}
