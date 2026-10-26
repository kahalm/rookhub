// node --test scripts/changelog-json.test.mjs — Changelog als Daten (Codereview F8-008).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseChangelog, serializeChangelog, SOURCE, validateChangelog } from './changelog-json.mjs';

const SAMPLE = `// Kopf
import { ChangelogEntry } from './changelog';

export const CHANGELOG: ChangelogEntry[] = [
  { version: "0.2.0", date: "2026-10-26", changes: [
    { en: "New „thing“.", de: "Neues „Ding“." },
  ]},
  { version: "0.1.0", date: "2026-10-25", changes: [
    { en: "First", de: "Erstes" },
  ]},
];
`;

test('liest das Array-Literal hinter Typ-Import und Typangabe', () => {
  const entries = parseChangelog(SAMPLE);
  assert.deepEqual(entries.map((e) => e.version), ['0.2.0', '0.1.0']);
  assert.equal(entries[0].changes[0].de, 'Neues „Ding“.');
  assert.deepEqual(validateChangelog(entries), []);
});

test('ein gerades Anfuehrungszeichen im Prosatext ist ein Syntaxfehler (wie vorher im ng build)', () => {
  assert.throws(() => parseChangelog(SAMPLE.replace('Neues „Ding“.', 'Neues "Ding".')), SyntaxError);
});

test('ohne „export const CHANGELOG: ChangelogEntry[] =“ bricht es ab, statt etwas Falsches zu liefern', () => {
  assert.throws(() => parseChangelog('export const X = [];'), /nicht gefunden/);
});

test('Formfehler werden je Eintrag gemeldet', () => {
  const errors = validateChangelog([
    { version: '1.2', date: '2026-10-26', changes: [{ en: 'a', de: 'b' }] },
    { version: '1.2.3', date: '26.10.2026', changes: [{ en: 'a' }] },
    { version: '1.2.4', date: '2026-10-26', changes: [] },
  ]);
  assert.equal(errors.length, 4);
  assert.match(errors[0], /version/);
  assert.match(errors[1], /date/);
  assert.match(errors[2], /en\/de/);
  assert.match(errors[3], /changes/);
  assert.deepEqual(validateChangelog({}), ['CHANGELOG ist kein Array']);
});

test('Ausgabe ist gueltiges JSON mit genau den Feldern von ChangelogEntry, eine Version je Zeile', () => {
  const entries = parseChangelog(SAMPLE);
  entries[0].extra = 'weg';
  const text = serializeChangelog(entries);
  assert.equal(text.split('\n').length, 2 + entries.length + 1);
  const back = JSON.parse(text);
  assert.deepEqual(Object.keys(back[0]), ['version', 'date', 'changes']);
  assert.deepEqual(back[1], { version: '0.1.0', date: '2026-10-25', changes: [{ en: 'First', de: 'Erstes' }] });
});

test('der echte Bestand (changelog-data.ts) ist lesbar und formgerecht', () => {
  const entries = parseChangelog(readFileSync(SOURCE, 'utf8'));
  assert.ok(entries.length > 100);
  assert.deepEqual(validateChangelog(entries), []);
});
