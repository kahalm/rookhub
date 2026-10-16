// Deckt der Routen-Katalog jede Seite der fünf Oberflächen ab? Liest die `path:`-Einträge aus den app.routes.ts und
// sucht zu jedem eine Katalogzeile (Parameter `:x` ↔ `{x}` oder ein fester Wert wie `/courses/{bookId}/random` für
// `:mode`). Eine neue Route ohne Katalogzeile lässt den Test scheitern — so fällt sie vor dem nächsten Volllauf auf.
// Lauf: `node --test tools/ui-sweep/*.test.mjs`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ROUTES } from './routes.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND = path.resolve(here, '../../src/frontend/app');
const SOURCES = { rookhub: 'src', turnier: 'src-turnier', kidhub: 'src-kidhub', leaguehub: 'src-leaguehub', clubhub: 'src-clubhub' };

/** Pfade aus app.routes.ts — ohne `**` und ohne reine Weiterleitungen (`redirectTo: '…'` auf derselben Zeile). */
export function angularPaths(source) {
  const out = new Set();
  for (const m of source.matchAll(/path:\s*'([^']*)'([^\n]*)/g)) {
    if (m[1].includes('**') || /redirectTo:\s*'/.test(m[2])) continue;
    out.add(m[1]);
  }
  return [...out];
}

/** Passt ein Angular-Pfad (`courses/:bookId/:mode`) auf eine Katalog-Adresse (`/courses/{bookId}/random?x=1`)? */
export function covers(angularPath, catalogUrl) {
  const a = angularPath.split('/').filter(Boolean);
  const c = catalogUrl.split('?')[0].split('/').filter(Boolean);
  if (a.length !== c.length) return false;
  return a.every((seg, i) => seg.startsWith(':') || seg === c[i]);
}

test('angularPaths: reine Weiterleitungen und ** zählen nicht', () => {
  const src = "{ path: '', pathMatch: 'full', redirectTo: 'x' },\n{ path: 'a/:id', loadComponent: f },\n{ path: '**', redirectTo: 'x' },";
  assert.deepEqual(angularPaths(src), ['a/:id']);
});

test('covers: Parameter passen auf {x} und auf feste Werte, Länge muss stimmen', () => {
  assert.ok(covers('courses/:bookId/:mode', '/courses/{bookId}/random'));
  assert.ok(covers('', '/'));
  assert.ok(!covers('courses/:bookId', '/courses/{bookId}/browse'));
  assert.ok(!covers('games', '/repertoires'));
});

for (const [app, dir] of Object.entries(SOURCES)) {
  test(`Katalog deckt jede Route von ${app} ab`, () => {
    const file = path.join(FRONTEND, dir, 'app', 'app.routes.ts');
    const urls = ROUTES.filter(r => r.app === app).map(r => r.url);
    const missing = angularPaths(fs.readFileSync(file, 'utf8')).filter(p => !urls.some(u => covers(p, u)));
    assert.deepEqual(missing, [], `Neue Route(n) in ${app} ohne Zeile in routes.mjs: ${missing.join(', ')}`);
  });
}
