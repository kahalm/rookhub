import { CommentBlockCache, lineStepAt } from './line-step.util';

/**
 * Vektor-Spec der geteilten Durchklick-Regel (Codereview F3-014): Brett-Stand nach `index` Halbzügen
 * für legale FENs (chess.js) und illegale Chessable-Diagramm-FENs (per Koordinaten), Index-Klemmung,
 * und der Kommentar-Segment-Cache. Die Komponenten-Specs (course-browse, book-puzzle, base solver)
 * prüfen dieselben Fälle über ihre Felder.
 */
describe('lineStepAt', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  it('legale FEN, Index 0: Ausgangsstellung, kein letzter Zug', () => {
    const s = lineStepAt(START, ['e2e4', 'e7e5'], 0);
    expect(s.index).toBe(0);
    expect(s.fen).toBe(START);
    expect(s.lastMove).toBeUndefined();
    expect(s.turnColor).toBe('white');
    expect(s.isCheck).toBeFalse();
    expect(s.chess).not.toBeNull();
  });

  it('legale FEN: spielt die Züge, letzter Zug + Seite am Zug + Schach', () => {
    const s = lineStepAt(START, ['e2e4', 'f7f6', 'd1h5'], 3);
    expect(s.fen).toBe('rnbqkbnr/ppppp1pp/5p2/7Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2');
    expect(s.lastMove).toEqual(['d1', 'h5']);
    expect(s.turnColor).toBe('black');
    expect(s.isCheck).toBeTrue();
    expect(s.chess!.fen()).toBe(s.fen);
  });

  it('klemmt den Index auf [0, Zuganzahl]', () => {
    expect(lineStepAt(START, ['e2e4'], 5).index).toBe(1);
    expect(lineStepAt(START, ['e2e4'], 5).lastMove).toEqual(['e2', 'e4']);
    expect(lineStepAt(START, ['e2e4'], -3).index).toBe(0);
    expect(lineStepAt(START, [], 2).index).toBe(0);
  });

  it('illegale Diagramm-FEN (ohne Könige): per Koordinaten, ohne chess.js, nie Schach', () => {
    const bad = '8/5pp1/6P1/8/8/8/8/7R w - - 0 1';
    const s0 = lineStepAt(bad, ['h1h8', 'g6f7'], 0);
    expect(s0.chess).toBeNull();
    expect(s0.fen).toBe(bad);
    expect(s0.turnColor).toBe('white');

    const s1 = lineStepAt(bad, ['h1h8', 'g6f7'], 1);
    expect(s1.chess).toBeNull();
    expect(s1.fen).toBe('7R/5pp1/6P1/8/8/8/8/8 b - - 0 1');
    expect(s1.lastMove).toEqual(['h1', 'h8']);
    expect(s1.turnColor).toBe('black');
    expect(s1.isCheck).toBeFalse();
    expect(lineStepAt(bad, ['h1h8'], 9).index).toBe(1);
  });
});

describe('CommentBlockCache', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  it('löst je Linie + Kommentar einmal auf und liefert danach dieselben Blöcke', () => {
    const cache = new CommentBlockCache();
    const ucis = jasmine.createSpy('ucis').and.returnValue(['e2e4']);
    const a = cache.get(1, ['Instead 1.d4 is also good.'], START, ucis);
    expect(a.flat().some(s => s.move?.includes('d4'))).toBeTrue();
    expect(cache.get(1, ['Instead 1.d4 is also good.'], START, ucis)).toBe(a);
    expect(ucis).toHaveBeenCalledTimes(1);

    const b = cache.get(2, ['Instead 1.d4 is also good.'], START, ucis);   // andere Linie → neu
    expect(b).not.toBe(a);
    expect(ucis).toHaveBeenCalledTimes(2);
  });

  it('ohne FEN bleibt jeder Absatz Text; Absatzgrenzen gehören zum Schlüssel', () => {
    const cache = new CommentBlockCache();
    expect(cache.get(1, ['ab', 'c'], '', () => [])).toEqual([[{ text: 'ab' }], [{ text: 'c' }]]);
    expect(cache.get(1, ['a', 'bc'], '', () => [])).toEqual([[{ text: 'a' }], [{ text: 'bc' }]]);
  });
});
