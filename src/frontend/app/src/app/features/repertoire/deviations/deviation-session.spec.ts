import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Chess } from 'chess.js';
import { OpponentPositions, positionKey } from '../../../shared/player-card/opponent-positions';
import type { Deviation } from '../../../shared/player-card/training-lines';
import { DEV_GOOD_HOLD_MS, DEV_OPP_DELAY_MS, DeviationSession, linesOf } from './deviation-session';
import type { Verdict } from './deviation-judge';

const fenAfter = (sans: string[]) => { const c = new Chess(); sans.forEach(s => c.move(s)); return c.fen(); };

/** Nutzer Weiß, Repertoire 1.e4 e5 2.Nf3; er spielt 1…c5 (7×). Danach: 2.Nf3 d6 (3×) / Nc6 (1×); nach 2.Nf3 d6 3.d4 nichts mehr. */
const DEV: Deviation = { path: ['e4'], fen: fenAfter(['e4']), san: 'c5', games: 7, reached: 9, lastYear: 2024, repertoire: ['e5'] };
const POSITIONS: OpponentPositions = {
  [positionKey(fenAfter(['e4', 'c5', 'Nf3']))]: { n: 4, y: 2024, m: { d6: 3, Nc6: 1 } },
  [positionKey(fenAfter(['e4', 'c5', 'Nf3', 'd6', 'd4']))]: { n: 2, y: 2024, m: {} },
};

function setup(verdicts: Verdict[] = []) {
  const calls = { prepared: [] as string[], judged: [] as string[], stopped: 0 };
  const s = new DeviationSession({
    changed: () => {},
    prepare: fen => calls.prepared.push(fen),
    judge: (_fen, uci) => { calls.judged.push(uci); return Promise.resolve(verdicts.shift() ?? { good: true, lossCp: 0, best: [] }); },
    stopEngine: () => calls.stopped++,
  }, 'w');
  return { s, calls };
}

describe('DeviationSession (Abweichungen trainieren, 0.892.0)', () => {
  it('Pfad → Abweichung → gute Antwort → seine häufigste Fortsetzung, die andere als eigene Linie → Ende → nächste → fertig', fakeAsync(() => {
    const { s, calls } = setup();
    s.start(linesOf([DEV]), POSITIONS);
    expect(s.phase).toBe('PATH');                                // ich spiele 1.e4
    s.onMove('d2', 'd4');                                        // falsch: Hinweis, Brett bleibt
    expect(s.pathHint).toBe('e4');
    expect(s.played).toEqual([]);
    s.onMove('e2', 'e4');
    expect(s.phase).toBe('OPP');
    tick(DEV_OPP_DELAY_MS);
    expect(s.phase).toBe('ASK');                                 // er spielt 1…c5, die Frage
    expect(s.played).toEqual(['e4', 'c5']);
    expect(s.askGames).toBe(7);
    expect(calls.prepared).toEqual([fenAfter(['e4', 'c5'])]);
    s.onMove('g1', 'f3');
    expect(s.phase).toBe('JUDGING');
    flushMicrotasks();
    expect(s.phase).toBe('GOOD');
    expect(calls.judged).toEqual(['g1f3']);
    tick(DEV_GOOD_HOLD_MS);
    expect(s.phase).toBe('OPP');
    expect(s.queue.length).toBe(2);                              // 2…Nc6 als eigene Linie hinter die laufende
    expect(s.queue[1]).toEqual(jasmine.objectContaining({ path: ['e4', 'c5', 'Nf3'], oppSan: 'Nc6', games: 1, auto: 3 }));
    tick(DEV_OPP_DELAY_MS);
    expect(s.played).toEqual(['e4', 'c5', 'Nf3', 'd6']);         // die häufigste: 2…d6
    expect(s.phase).toBe('ASK');
    s.onMove('d2', 'd4');
    flushMicrotasks();
    s.tap();                                                     // gut → nicht abwarten
    expect(s.phase).toBe('END');                                 // nach 3.d4 hat er nichts mehr gespielt → Maia-Angebot
    expect(calls.stopped).toBeGreaterThan(0);
    s.next();                                                    // Verzweigung: ohne Abfrage aufgestellt, er spielt 2…Nc6
    expect(s.played).toEqual(['e4', 'c5', 'Nf3']);
    tick(DEV_OPP_DELAY_MS);
    expect(s.phase).toBe('ASK');
    expect(s.askSan).toBe('Nc6');
    s.onMove('d2', 'd4');
    flushMicrotasks();
    tick(DEV_GOOD_HOLD_MS);
    expect(s.phase).toBe('END');
    s.tap();
    expect(s.phase).toBe('DONE');
    expect(s.good).toBe(3);
  }));

  it('nicht optimal: Nochmal nimmt die Antwort zurück, Lösung zeigen spielt den besten Zug und macht weiter', fakeAsync(() => {
    const bad: Verdict = { good: false, lossCp: 120, best: [{ uci: 'g1f3', san: 'Nf3', evalText: '0.40' }] };
    const { s } = setup([bad, bad]);
    s.start(linesOf([DEV]), POSITIONS);
    s.onMove('e2', 'e4');
    tick(DEV_OPP_DELAY_MS);
    s.onMove('a2', 'a3');
    flushMicrotasks();
    expect(s.phase).toBe('BAD');
    expect(s.bad).toBe(1);
    s.retry();
    expect(s.phase).toBe('ASK');
    expect(s.played).toEqual(['e4', 'c5']);
    s.onMove('a2', 'a3');
    flushMicrotasks();
    s.showSolution();
    expect(s.played).toEqual(['e4', 'c5', 'Nf3']);
    expect(s.phase).toBe('OPP');                                 // seine Fortsetzung nach 2.Nf3
    tick(DEV_OPP_DELAY_MS);
    expect(s.askSan).toBe('d6');
  }));

  it('Abweichung als schwarzer Nutzer: sein erster Zug kommt von selbst; leer → EMPTY; Überspringen', fakeAsync(() => {
    const d: Deviation = { path: [], fen: new Chess().fen(), san: 'd4', games: 2, reached: 2, lastYear: null, repertoire: ['e4'] };
    const s = new DeviationSession({ changed: () => {}, prepare: () => {}, judge: () => Promise.resolve({ good: true, lossCp: 0, best: [] }),
      stopEngine: () => {} }, 'b');
    s.start(linesOf([d, DEV]), {});
    expect(s.phase).toBe('OPP');
    tick(DEV_OPP_DELAY_MS);
    expect(s.phase).toBe('ASK');
    expect(s.playable).toBeTrue();
    s.skip();
    expect(s.index).toBe(1);
    const empty = setup().s;
    empty.start([], {});
    expect(empty.phase).toBe('EMPTY');
  }));

  it('Hotfix 0.893.1: Farbe aus der Server-Antwort; Schwarz zieht nur schwarze Züge, Weiß kommt von selbst; SAN mit „+" zählt', fakeAsync(() => {
    // Französisch-Vorstoß aus dem Schwarz-Repertoire bis 5…Db6, Abweichung 6.Le2 statt 6.a3 — die Adresse sagte 'w', der Server 'b'
    const path = ['e4', 'e6', 'd4', 'd5', 'e5', 'c5', 'c3', 'Nc6', 'Nf3', 'Qb6'];
    const d: Deviation = { path, fen: fenAfter(path), san: 'Be2', games: 4, reached: 4, lastYear: 2026, repertoire: ['a3'] };
    const { s } = setup();
    s.start(linesOf([d]), {}, 'b');
    expect(s.color).toBe('b');
    expect(s.phase).toBe('OPP');                                 // 1.e4 kommt von selbst
    tick(DEV_OPP_DELAY_MS);
    expect(s.phase).toBe('PATH');
    expect(s.userToMove).toBeTrue();
    s.onMove('c7', 'c5');                                        // 1…c5 steht nicht im Repertoire: Hinweis 1…e6
    expect(s.pathHint).toBe('e6');
    const moves: [string, string][] = [['e7', 'e6'], ['d7', 'd5'], ['c7', 'c5'], ['b8', 'c6'], ['d8', 'b6']];
    for (const [from, to] of moves) {
      expect(s.phase).toBe('PATH');
      s.onMove(from, to);
      tick(DEV_OPP_DELAY_MS);                                    // Weiß antwortet von selbst
    }
    expect(s.phase).toBe('ASK');
    expect(s.askSan).toBe('Be2');
    expect(s.played).toEqual([...path, 'Be2']);

    // Server-SAN mit Schachzeichen: der Stellungsvergleich nimmt den Zug trotzdem an
    const check: Deviation = { path: ['e4', 'e5', 'Bc4', 'Nc6', 'Bxf7+'], fen: fenAfter(['e4', 'e5', 'Bc4', 'Nc6', 'Bxf7+']), san: 'Ke7',
      games: 1, reached: 1, lastYear: null, repertoire: ['Kxf7'] };
    const w = setup().s;
    w.start(linesOf([check]), {}, 'w');
    w.onMove('e2', 'e4'); tick(DEV_OPP_DELAY_MS);
    w.onMove('f1', 'c4'); tick(DEV_OPP_DELAY_MS);
    w.onMove('c4', 'f7');
    expect(w.pathHint).toBeNull();
    tick(DEV_OPP_DELAY_MS);
    expect(w.phase).toBe('ASK');
    expect(w.askSan).toBe('Ke7');
  }));
});
