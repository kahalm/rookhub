// node --test scripts/eslint-ratchet.test.mjs — Vergleichslogik der ESLint-Ratsche (F8-004 Phase 2).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { applyUpdate, compare, PARSE_ERROR, serializeBaseline, summaryMarkdown } from './eslint-ratchet.mjs';

test('gleich: gruen, nichts gestiegen oder gesunken', () => {
  const r = compare({ a: 3, b: 1 }, { a: 3, b: 1 });
  assert.equal(r.ok, true);
  assert.deepEqual(r.increased, []);
  assert.deepEqual(r.decreased, []);
});

test('gesunken: gruen, Regel als senkbar gemeldet (auch auf 0 = Regel fehlt im Lauf)', () => {
  const r = compare({ a: 3, b: 2 }, { a: 1 });
  assert.equal(r.ok, true);
  assert.deepEqual(r.decreased.map((x) => [x.rule, x.baseline, x.current]), [['a', 3, 1], ['b', 2, 0]]);
});

test('gestiegen: rot, genau die gestiegene Regel', () => {
  const r = compare({ a: 3, b: 2 }, { a: 4, b: 1 });
  assert.equal(r.ok, false);
  assert.deepEqual(r.increased, [{ rule: 'a', baseline: 3, current: 4, delta: 1 }]);
});

test('neue Regel ohne Baseline-Eintrag zaehlt wie Baseline 0', () => {
  const r = compare({ a: 1 }, { a: 1, neu: 2, [PARSE_ERROR]: 1 });
  assert.equal(r.ok, false);
  assert.deepEqual(r.increased.map((x) => [x.rule, x.baseline]).sort(), [[PARSE_ERROR, 0], ['neu', 0]].sort());
});

test('--update senkt nur, hebt ohne force nichts an', () => {
  const { next, raised } = applyUpdate({ a: 5, b: 2, weg: 3 }, { a: 4, b: 3, neu: 1 });
  assert.deepEqual(next, { a: 4, b: 2, neu: 0, weg: 0 });
  assert.deepEqual(raised, ['b', 'neu']);
});

test('--update --force uebernimmt auch hoehere Zahlen', () => {
  const { next, raised } = applyUpdate({ a: 5, b: 2 }, { a: 4, b: 3 }, { force: true });
  assert.deepEqual(next, { a: 4, b: 3 });
  assert.deepEqual(raised, ['b']);
});

test('--update ohne Aenderung ist byte-gleich (sortiert, stabil)', () => {
  const text = serializeBaseline({ b: 2, a: 1 });
  assert.equal(text, '{\n  "a": 1,\n  "b": 2\n}\n');
  const { next } = applyUpdate(JSON.parse(text), { a: 1, b: 2 });
  assert.equal(serializeBaseline(next), text);
});

test('Summary: Tabelle mit Regel | Baseline | jetzt | Δ und Summenzeile', () => {
  const md = summaryMarkdown(compare({ a: 3, b: 1 }, { a: 4 }), 2);
  assert.match(md, /\| Regel \| Baseline \| jetzt \| Δ \|/);
  assert.match(md, /\| a \| 3 \| 4 \| \*\*\+1\*\* \|/);
  assert.match(md, /\| b \| 1 \| 0 \| -1 \|/);
  assert.match(md, /\| \*\*gesamt\*\* \| 4 \| 4 \| 0 \|/);
  assert.match(md, /ROT/);
});
