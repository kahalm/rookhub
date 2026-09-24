import { Chess } from 'chess.js';
import { GameEvalPly, GameEvals, GameReview, ReviewedMove, reviewGame } from './game-review.util';
import { PlayedMove, collectMistakes, mistakeCount, sanOfUci, sideWithMoreMistakes } from './mistakes.util';

/** Kurze Partie nachspielen: liefert die FENs (Stellung VOR jedem Halbzug, plus die letzte) und die Zuege. */
function play(sans: string[]): { fens: string[]; moves: PlayedMove[] } {
  const chess = new Chess();
  const fens = [chess.fen()];
  const moves: PlayedMove[] = [];
  for (const san of sans) {
    const m = chess.move(san);
    moves.push({ san: m.san, from: m.from, to: m.to, promotion: m.promotion ?? null });
    fens.push(chess.fen());
  }
  return { fens, moves };
}

function evals(plies: GameEvalPly[], total: number): GameEvals {
  return { status: 'done', analyzed: plies.length, total, targetDepth: 20, plies };
}

describe('mistakes.util', () => {
  // 1.e4 e5 2.Nf3?? Nc6?? — beide „Fehlzuege" sind hier nur Rechenwerte, die Zuege selbst sind normal.
  const { fens, moves } = play(['e4', 'e5', 'Nf3', 'Nc6']);
  const rows: GameEvalPly[] = [
    { ply: 0, depth: 20, cp: 20, bestUci: 'e2e4', playedUci: 'e2e4' },
    { ply: 1, depth: 20, cp: 20, bestUci: 'e7e5', playedUci: 'e7e5' },
    { ply: 2, depth: 20, cp: 30, bestUci: 'b1c3', playedUci: 'g1f3' },
    { ply: 3, depth: 20, cp: -250, bestUci: 'g8f6', playedUci: 'b8c6' },
  ];
  const withFinal: GameEvals = { ...evals(rows, 4), final: { cp: 50 } };
  const review = reviewGame(withFinal, fens, moves.map(m => m.from + m.to + (m.promotion ?? '')));

  it('nimmt je Seite die Ungenauigkeiten/Fehler/groben Fehler in Partie-Reihenfolge', () => {
    const bySide = collectMistakes(review, withFinal, fens, moves);

    expect(bySide.white.map(m => m.ply)).toEqual([2]);
    expect(bySide.black.map(m => m.ply)).toEqual([3]);
    expect(mistakeCount(bySide)).toBe(2);
  });

  it('haelt zu jedem Fehler den gespielten Zug UND den besseren bereit — als SAN fuers Vorlesen', () => {
    const w = collectMistakes(review, withFinal, fens, moves).white[0];

    expect(w.cls).toBe('blunder');
    expect(w.playedSan).toBe('Nf3');
    expect(w.playedUci).toBe('g1f3');
    expect(w.bestUci).toBe('b1c3');
    expect(w.bestSan).toBe('Nc3');
    expect(w.fenBefore).toBe(fens[2]);
    // Weiss vor dem Zug 52,75 %, danach 28,49 % (winPercent von +30 bzw. −250) — rund 24 Punkte weg.
    expect(w.lostPercent).toBeCloseTo(24.26, 1);
  });

  it('die schwarze Aufgabe steht aus SCHWARZer Sicht da', () => {
    const b = collectMistakes(review, withFinal, fens, moves).black[0];

    expect(b.white).toBeFalse();
    expect(b.bestSan).toBe('Nf6');
    expect(b.lostPercent).toBeGreaterThan(20);
  });

  it('ein als Miss ETIKETTIERTER Zug bleibt eine Aufgabe — gezaehlt wird der Verlust', () => {
    // Schwarz patzt direkt nach dem weissen Patzer: unser Rueckblick nennt das „Verpasst",
    // Lichess kennt dieses Etikett nicht und traegt den Zug als groben Fehler.
    expect(review.moves[3]!.cls).toBe('miss');
    expect(review.moves[3]!.base).toBe('blunder');

    const b = collectMistakes(review, withFinal, fens, moves).black;

    expect(b.map(m => m.ply)).toEqual([3]);
    expect(b[0].cls).toBe('blunder');
  });

  it('gute Zuege werden nicht abgefragt', () => {
    const bySide = collectMistakes(review, withFinal, fens, moves);

    expect(bySide.white.some(m => m.ply === 0)).toBeFalse();
    expect(bySide.black.some(m => m.ply === 1)).toBeFalse();
  });

  it('ohne Analyse oder ohne Rueckblick: leer statt Wurf', () => {
    expect(collectMistakes(null, withFinal, fens, moves)).toEqual({ white: [], black: [] });
    expect(collectMistakes(review, null, fens, moves)).toEqual({ white: [], black: [] });
    expect(collectMistakes(review, withFinal, [], [])).toEqual({ white: [], black: [] });
  });

  // Die folgenden drei Faelle kann `reviewGame` gar nicht erzeugen — sie sind die Absicherung gegen
  // unvollstaendige Analyse-Daten, deshalb ein von Hand gebauter Rueckblick.
  function handReview(cls: ReviewedMove['cls'] = 'blunder'): GameReview {
    const m: ReviewedMove = {
      ply: 2, white: true, cls, base: cls, accuracy: 10, winBefore: 60, winAfter: 30,
      evalBefore: { cp: 30 }, evalAfter: { cp: -250 },
    };
    return { series: [], moves: [null, null, m], white: { accuracy: null, counts: {} as never }, black: { accuracy: null, counts: {} as never } };
  }

  it('ohne Bestzug gibt es keine Aufgabe', () => {
    const ohne = evals([{ ply: 2, depth: 20, cp: 30, playedUci: 'g1f3' }], 4);

    expect(collectMistakes(handReview(), ohne, fens, moves).white).toEqual([]);
  });

  it('ein Bestzug, der in der Stellung nicht geht, wird nicht vorgefuehrt', () => {
    const kaputt = evals([{ ply: 2, depth: 20, cp: 30, bestUci: 'a1a8', playedUci: 'g1f3' }], 4);

    expect(collectMistakes(handReview(), kaputt, fens, moves).white).toEqual([]);
  });

  it('Bestzug = gespielter Zug ist kein Lehrstueck', () => {
    const gleich = evals([{ ply: 2, depth: 20, cp: 30, bestUci: 'g1f3', playedUci: 'g1f3' }], 4);

    expect(collectMistakes(handReview(), gleich, fens, moves).white).toEqual([]);
  });

  it('sanOfUci kennt die Umwandlungsfigur und meldet Unspielbares mit null', () => {
    const vorUmwandlung = '8/4P3/8/8/8/8/8/K6k w - - 0 1';

    expect(sanOfUci(vorUmwandlung, 'e7e8q')).toBe('e8=Q');
    expect(sanOfUci(vorUmwandlung, 'e7e8n')).toBe('e8=N');
    expect(sanOfUci(vorUmwandlung, 'a1a8')).toBeNull();
    expect(sanOfUci('kaputt', 'e7e8q')).toBeNull();
  });

  it('ohne zuordenbare Seite wird die mit den meisten Fehlern trainiert, bei Gleichstand Weiss', () => {
    const w = { ply: 1 } as never;

    expect(sideWithMoreMistakes({ white: [w], black: [w, w] })).toBe('black');
    expect(sideWithMoreMistakes({ white: [w, w], black: [w] })).toBe('white');
    expect(sideWithMoreMistakes({ white: [], black: [] })).toBe('white');
  });
});
