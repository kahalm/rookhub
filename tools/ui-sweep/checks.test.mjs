// node --test tools/ui-sweep  — Warten vor dem Foto (waitShellReady): bereit, Ablauf mit „noch geladen“, echte Schlüssel.
import test from 'node:test';
import assert from 'node:assert/strict';
import { waitShellReady } from './checks.mjs';

/** Gestellte Uhr + Probe: liefert der Reihe nach die Zustände aus `seq` (der letzte bleibt stehen). */
function fake(seq) {
  let t = 0; let i = 0;
  return {
    probe: async () => seq[Math.min(i++, seq.length - 1)],
    opts: { maxMs: 1000, intervalMs: 200, sleep: async ms => { t += ms; }, now: () => t },
  };
}
const EMPTY = { keys: ['nav.help', 'legal.privacy.link'], outlet: true, routed: false };
const DONE = { keys: [], outlet: true, routed: true };

test('wartet, bis Hülle übersetzt und Seite da ist', async () => {
  const f = fake([EMPTY, EMPTY, { keys: [], outlet: true, routed: false }, DONE]);
  const r = await waitShellReady(f.probe, f.opts);
  assert.equal(r.ready, true);
  assert.equal(r.waitedMs, 600);
});

test('sofort bereit ohne Warten; Seite ohne router-outlet gilt als bereit', async () => {
  assert.equal((await waitShellReady(fake([DONE]).probe, fake([DONE]).opts)).waitedMs, 0);
  const f = fake([{ keys: [], outlet: false, routed: false }]);
  assert.equal((await waitShellReady(f.probe, f.opts)).ready, true);
});

test('Obergrenze: leere Seite → loading („noch geladen“), mit Grund', async () => {
  const f = fake([EMPTY]);
  const r = await waitShellReady(f.probe, f.opts);
  assert.equal(r.ready, false);
  assert.equal(r.loading, true);
  assert.equal(r.waitedMs, 1000);
  assert.match(r.detail, /nav\.help/);
  assert.match(r.detail, /router-outlet/);
});

test('Obergrenze: Seite steht, nur Schlüssel in der Hülle → kein loading (echter i18n-Befund)', async () => {
  const f = fake([{ keys: ['nav.gibtsnicht'], outlet: true, routed: true }]);
  const r = await waitShellReady(f.probe, f.opts);
  assert.equal(r.ready, false);
  assert.equal(r.loading, false);
});

test('Probe wirft (Navigation) → weiter versuchen, am Ende loading', async () => {
  let t = 0;
  const r = await waitShellReady(async () => { throw new Error('Execution context was destroyed'); },
    { maxMs: 400, intervalMs: 200, sleep: async ms => { t += ms; }, now: () => t });
  assert.equal(r.loading, true);
  assert.match(r.detail, /nicht prüfbar/);
});
