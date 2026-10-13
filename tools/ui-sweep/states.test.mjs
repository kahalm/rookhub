// node --test tools/ui-sweep  — Katalog und Zustände passen zusammen, Brett-Koordinaten stimmen.
import test from 'node:test';
import assert from 'node:assert/strict';
import { ROUTES, routeId } from './routes.mjs';
import { STATES, withStates, squareCenter } from './states.mjs';

test('jeder Zustand hängt an einer Katalog-Route, Ids sind eindeutig', () => {
  const derived = withStates(ROUTES, routeId);
  assert.equal(derived.length, STATES.length);
  const ids = [...ROUTES, ...derived].map(routeId);
  assert.equal(new Set(ids).size, ids.length, 'doppelte Ids');
  for (const d of derived) {
    assert.match(routeId(d), new RegExp(`^${d.stateOf}--${d.state}$`));
    assert.ok(d.steps.length > 0, `${routeId(d)}: keine Schritte`);
  }
});

test('Zustände: nur Rollen, die die Route überhaupt hat', () => {
  for (const d of withStates(ROUTES, routeId)) {
    if (!d.stateAuth) continue;
    if (d.auth === 'anon') assert.deepEqual(d.stateAuth, ['anon'], routeId(d));
    if (d.auth === 'user') assert.ok(!d.stateAuth.includes('anon'), `${routeId(d)}: Route nur angemeldet`);
  }
});

test('Zustände sind von der Falz-Prüfung ausgenommen, außer sie nennen eine Hauptsache', () => {
  for (const d of withStates(ROUTES, routeId)) assert.equal(d.foldExempt, d.main == null);
});

test('unbekannte Route → Fehler mit Namen', () => {
  assert.throws(() => withStates([], routeId), STATES.length ? /unbekannter Route/ : /^$/);
});

test('Feldmitte: Weiß unten und Schwarz unten', () => {
  const box = { x: 100, y: 0, width: 800, height: 800 };
  assert.deepEqual(squareCenter(box, 'a1'), { x: 150, y: 750 });
  assert.deepEqual(squareCenter(box, 'h8'), { x: 850, y: 50 });
  assert.deepEqual(squareCenter(box, 'e2'), { x: 550, y: 650 });
  assert.deepEqual(squareCenter(box, 'a1', true), { x: 850, y: 50 });
  assert.deepEqual(squareCenter(box, 'e2', true), { x: 450, y: 150 });
  assert.throws(() => squareCenter(box, 'i9'), /kein Feld/);
});
