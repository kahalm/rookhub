// Deckt der Routen-Katalog jede Seite der fünf Oberflächen ab? Liest die `path:`-Einträge aus den app.routes.ts und
// sucht zu jedem eine Katalogzeile (Parameter `:x` ↔ `{x}` oder ein fester Wert wie `/courses/{bookId}/random` für
// `:mode`). Eine neue Route ohne Katalogzeile lässt den Test scheitern — so fällt sie vor dem nächsten Volllauf auf.
// Lauf: `node --test tools/ui-sweep/*.test.mjs`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ROUTES, routeId } from './routes.mjs';
import { STATES } from './states.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const FRONTEND = path.resolve(here, '../../src/frontend/app');
const SOURCES = { rookhub: 'src', turnier: 'src-turnier', kidhub: 'src-kidhub', leaguehub: 'src-leaguehub', clubhub: 'src-clubhub' };

/** Pfade aus app.routes.ts — ohne `**` und ohne reine Weiterleitungen (`redirectTo: '…'` als Text; eine Funktion als
 *  Ziel entscheidet zur Laufzeit und braucht deshalb eine Zeile). Eine Route reicht bis zum nächsten `path:` — so zählt
 *  auch ein `redirectTo` in der Folgezeile derselben Route. */
export function angularPaths(source) {
  const out = new Set();
  const hits = [...source.matchAll(/path:\s*'([^']*)'/g)];
  hits.forEach((m, i) => {
    const body = source.slice(m.index + m[0].length, hits[i + 1]?.index ?? source.length);
    if (m[1].includes('**') || /redirectTo:\s*'/.test(body)) return;
    out.add(m[1]);
  });
  return [...out];
}

/** Ist die Route-Datei flach? Kind-Routen (`children`, `loadChildren`) setzen Pfade aus Teilen zusammen — das kann
 *  `angularPaths` nicht; wer sie einführt, muss den Abgleich hier erweitern statt still Seiten zu verlieren. */
export function nestedRouteMarkers(source) {
  return [...source.matchAll(/\b(children|loadChildren)\s*:/g)].map(m => m[1]);
}

/** Werte einer Reiter-Liste aus dem Quelltext: die Zeichenketten im ersten Treffer von `re` (Gruppe 1). */
export function tabKeys(source, re) {
  const m = re.exec(source);
  return m ? [...m[1].matchAll(/'([^']+)'/g)].map(x => x[1]) : [];
}

/** Abfrage-Wert `name` einer Katalog-Adresse (`/admin?tab=ci` → 'ci'), sonst null. */
export function queryValue(url, name) {
  const q = url.split('?')[1];
  return q ? new URLSearchParams(q).get(name) : null;
}

/** Passt ein Angular-Pfad (`courses/:bookId/:mode`) auf eine Katalog-Adresse (`/courses/{bookId}/random?x=1`)? */
export function covers(angularPath, catalogUrl) {
  const a = angularPath.split('/').filter(Boolean);
  const c = catalogUrl.split('?')[0].split('/').filter(Boolean);
  if (a.length !== c.length) return false;
  return a.every((seg, i) => seg.startsWith(':') || seg === c[i]);
}

test('angularPaths: reine Weiterleitungen und ** zählen nicht, auch über Zeilen; Funktionsziele schon', () => {
  const src = "{ path: '', pathMatch: 'full', redirectTo: 'x' },\n{ path: 'a/:id', loadComponent: f },\n{ path: '**', redirectTo: 'x' },"
    + "\n{ path: 'alt',\n  redirectTo: 'neu' },\n{ path: 'f', redirectTo: () => 'x' },";
  assert.deepEqual(angularPaths(src), ['a/:id', 'f']);
});

test('tabKeys/queryValue: Reiter-Schlüssel aus dem Quelltext, Abfrage-Wert aus der Adresse', () => {
  assert.deepEqual(tabKeys("const TABS = ['a', 'b'] as const;", /TABS = \[([^\]]*)\]/), ['a', 'b']);
  assert.equal(queryValue('/admin?tab=ci&x=1', 'tab'), 'ci');
  assert.equal(queryValue('/admin', 'tab'), null);
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

// Keine Kind-Routen und keine weiteren Route-Dateien: der Abgleich oben liest je Oberfläche genau app.routes.ts flach.
test('Routen liegen flach in je einer app.routes.ts (sonst Abgleich erweitern)', () => {
  for (const [app, dir] of Object.entries(SOURCES)) {
    const src = fs.readFileSync(path.join(FRONTEND, dir, 'app', 'app.routes.ts'), 'utf8');
    assert.deepEqual(nestedRouteMarkers(src), [], `${app}: Kind-Routen — routes-coverage.test.mjs setzt ihre Pfade nicht zusammen`);
  }
  const extra = [];
  const walk = d => { for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const f = path.join(d, e.name);
    if (e.isDirectory()) { if (e.name !== 'node_modules') walk(f); }
    else if (/\.routes\.ts$/.test(e.name) && e.name !== 'app.routes.ts') extra.push(path.relative(FRONTEND, f));
  } };
  for (const dir of Object.values(SOURCES)) walk(path.join(FRONTEND, dir));
  assert.deepEqual(extra, [], 'weitere Route-Dateien — im Abgleich ergänzen');
});

// Reiter, die eine Seite über `?tab=` öffnet: jeder Schlüssel außer dem ersten (= die Seite ohne Abfrage) braucht eine
// Katalogzeile mit genau diesem `tab` ODER einen Zustand (states.mjs), der den Reiter anklickt. Die Schlüssel kommen aus dem
// Quelltext — ein neuer Reiter ohne Aufnahme fällt hier auf.
const TAB_PAGES = [
  { app: 'rookhub', path: '/admin', file: 'src/app/features/admin/admin-tabs.ts', list: /ADMIN_TAB_KEYS = \[([\s\S]*?)\]/ },
  { app: 'rookhub', path: '/training-goals', file: 'src/app/features/training-goals/training-goals.component.ts', list: /TABS = \[([^\]]*)\]/ },
  // Freunde: Reihenfolge Liste, Anfragen, Herausforderungen; der Schlüssel der Liste steht nicht im Code („friends“).
  { app: 'rookhub', path: '/friends', file: 'src/app/features/friends/friends.component.ts', list: /(tab === '[^']+'[\s\S]*?tab === '[^']+')/,
    first: 'friends', states: { requests: 'rookhub__friends--anfragen' } },
  { app: 'turnier', path: '/tournaments/{tournamentId}', file: 'src-turnier/app/features/tournaments/tournament-detail.component.ts',
    list: /TAB_NAMES = \[([^\]]*)\]/, states: { teams: 'turnier__tournaments-tournamentId--teams', pairings: 'turnier__tournaments-tournamentId--paarungen' } },
];

for (const t of TAB_PAGES) {
  test(`Katalog deckt jeden ?tab= von ${t.app}${t.path} ab`, () => {
    const keys = tabKeys(fs.readFileSync(path.join(FRONTEND, t.file), 'utf8'), t.list);
    assert.ok(keys.length >= 2, `${t.file}: Reiter-Liste nicht gefunden (Muster in TAB_PAGES nachziehen)`);
    const rows = ROUTES.filter(r => r.app === t.app && r.url.split('?')[0] === t.path);
    assert.ok(rows.some(r => queryValue(r.url, 'tab') == null), `${t.path}: Zeile ohne ?tab= fehlt`);
    const stateIds = new Set(STATES.map(s => `${s.of}--${s.name}`));
    const todo = (t.first ? keys : keys.slice(1)).filter(k => !rows.some(r => queryValue(r.url, 'tab') === k)
      && !(t.states?.[k] && stateIds.has(t.states[k])));
    assert.deepEqual(todo, [], `${t.path}: Reiter ohne Katalogzeile/Zustand: ${todo.join(', ')}`);
    // Jede ?tab=-Zeile nennt einen Reiter, den es gibt (sonst fotografiert sie still den ersten).
    const unknown = rows.map(r => queryValue(r.url, 'tab')).filter(k => k != null && !keys.includes(k));
    assert.deepEqual(unknown, [], `${t.path}: ?tab= ohne Reiter im Code: ${unknown.join(', ')}`);
  });
}

test('routeId bleibt eindeutig (auch mit den ?tab=-Zeilen)', () => {
  const ids = ROUTES.map(routeId);
  assert.equal(new Set(ids).size, ids.length, ids.filter((x, i) => ids.indexOf(x) !== i).join(', '));
});
