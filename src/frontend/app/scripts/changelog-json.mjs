#!/usr/bin/env node
// Changelog als DATEN (Codereview F8-008): schreibt das CHANGELOG-Array aus src/environments/changelog-data.ts als
// JSON nach generated/changelog/changelog.json. angular.json kopiert die Datei im Projekt `app` nach
// /assets/changelog.json; das Changelog-Overlay (shared/app-footer) laedt sie per HttpClient erst beim Oeffnen.
//
// Warum: als Code (dynamic import) lag das Changelog als ~2 MB-Lazy-Chunk unter `/*.js` in der prefetch-Gruppe des
// Service Workers — jeder Release aendert den Text, also den Hash, und jede installierte PWA lud den Chunk im
// Hintergrund neu, auch ohne das Changelog je zu oeffnen. Als Asset steht das JSON in einer eigenen lazy-Gruppe
// (ngsw-config.json, Gruppe `changelog`).
//
// Die PFLEGE bleibt unveraendert: neue Eintraege oben in changelog-data.ts, APP_VERSION in changelog.ts. Die
// generierte Datei ist NICHT eingecheckt (.gitignore) — der Docker-Build (src/frontend/Dockerfile) und die CI
// (test.yml, build-frontend) rufen dieses Skript vor `ng build app` auf; lokal `npm start`/`npm run build` ebenso.
//
// Aufruf (aus src/frontend/app):
//   node scripts/changelog-json.mjs           JSON schreiben (npm run changelog:json)
//   node scripts/changelog-json.mjs --check   nur lesen + pruefen (Exit 1 bei Syntax-/Formfehler), nichts schreiben
//
// parseChangelog/validateChangelog sind exportiert und in scripts/changelog-json.test.mjs per `node --test` geprueft.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const APP_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
export const SOURCE = join(APP_DIR, 'src', 'environments', 'changelog-data.ts');
export const OUT = join(APP_DIR, 'generated', 'changelog', 'changelog.json');

/**
 * changelog-data.ts ist ein reines Array-Literal hinter einem Typ-Import — ohne Import und Typangabe gueltiges
 * JavaScript (dasselbe Verfahren wie scripts/release-notes.mjs). Ein gerades `"` im Prosatext wirft hier einen
 * SyntaxError, genau wie vorher im `ng build`.
 */
export function parseChangelog(source) {
  const body = source
    .replace(/^import .*$/m, '')
    .replace(/export const CHANGELOG\s*:\s*ChangelogEntry\[\]\s*=/, 'return');
  if (body === source) throw new Error('changelog-data.ts: „export const CHANGELOG: ChangelogEntry[] =“ nicht gefunden');
  return new Function(body)();
}

/** Form wie ChangelogEntry (changelog.ts) — liefert die Fehler als Liste, leer = gut. */
export function validateChangelog(entries) {
  const errors = [];
  if (!Array.isArray(entries)) return ['CHANGELOG ist kein Array'];
  if (entries.length === 0) errors.push('CHANGELOG ist leer');
  entries.forEach((e, i) => {
    const where = `Eintrag ${i} (${e?.version ?? '?'})`;
    if (typeof e?.version !== 'string' || !/^\d+\.\d+\.\d+$/.test(e.version)) errors.push(`${where}: version ist nicht X.Y.Z`);
    if (typeof e?.date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(e.date)) errors.push(`${where}: date ist nicht JJJJ-MM-TT`);
    if (!Array.isArray(e?.changes) || e.changes.length === 0) { errors.push(`${where}: changes fehlt oder ist leer`); return; }
    e.changes.forEach((c, j) => {
      if (typeof c?.en !== 'string' || typeof c?.de !== 'string') errors.push(`${where}, Aenderung ${j}: en/de fehlt`);
    });
  });
  return errors;
}

/** Nur die Felder von ChangelogEntry, kompakt (eine Zeile je Version: lesbare Diffs beim Nachsehen, kaum groesser). */
export function serializeChangelog(entries) {
  const lines = entries.map((e) => JSON.stringify({
    version: e.version, date: e.date, changes: e.changes.map((c) => ({ en: c.en, de: c.de })),
  }));
  return '[\n' + lines.join(',\n') + '\n]\n';
}

function main() {
  const check = process.argv.includes('--check');
  const entries = parseChangelog(readFileSync(SOURCE, 'utf8'));
  const errors = validateChangelog(entries);
  if (errors.length) {
    for (const err of errors.slice(0, 20)) console.error(err);
    console.error(`changelog-data.ts: ${errors.length} Formfehler.`);
    process.exitCode = 1;
    return;
  }
  if (check) {
    console.log(`changelog-data.ts gueltig: ${entries.length} Versionen.`);
    return;
  }
  const text = serializeChangelog(entries);
  mkdirSync(dirname(OUT), { recursive: true });
  writeFileSync(OUT, text);
  console.log(`${relative(APP_DIR, OUT)}: ${entries.length} Versionen, ${Buffer.byteLength(text)} Byte.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main();
  } catch (err) {
    console.error(`changelog-data.ts nicht lesbar — meist ein gerades " im Prosatext (nur „…“ verwenden):\n${err}`);
    process.exitCode = 1;
  }
}
