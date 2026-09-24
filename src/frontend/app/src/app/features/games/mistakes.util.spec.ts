import { Chess } from 'chess.js';
import { GameEvalPly, GameEvals, GameReview, ReviewedMove, reviewGame } from './game-review.util';
import { EQUIVALENT_LIMIT, PlayedMove, acceptedMoves, collectMistakes, mistakesOf, sanOfUci, sideWithMoreMistakes, trainingSide } from './mistakes.util';

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
    expect(mistakesOf(bySide, 'white').map(m => m.ply)).toEqual([2]);
    expect(mistakesOf(bySide, 'black').map(m => m.ply)).toEqual([3]);
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

  // Gemeldet 2026-09-24: „Eigene Fehler nachspielen (1)", der Dialog fand nichts — der Knopf zählte beide
  // Seiten, der Dialog öffnete auf der des Besitzers, und der einzige Fehler war der des Gegners.
  it('trainiert wird die Seite des Besitzers, auch wenn nur der Gegner Fehler hat; ohne Besitzer die mit den meisten', () => {
    const w = { ply: 1 } as never;
    const onlyOpponent = { white: [], black: [w] };

    expect(trainingSide(onlyOpponent, 'white')).toBe('white');
    expect(mistakesOf(onlyOpponent, trainingSide(onlyOpponent, 'white')).length).toBe(0);
    expect(trainingSide(onlyOpponent, null)).toBe('black');
    expect(trainingSide(onlyOpponent)).toBe('black');
  });
  // Gewuenscht 2026-09-24: nicht nur der Bestzug zaehlt, sondern jeder gleichwertige.
  describe('gleichwertige Zuege', () => {
    it('nimmt Kandidaten bis EQUIVALENT_LIMIT Punkte hinter dem Bestzug — Bestzug zuerst, Fehlzug nie', () => {
      expect(EQUIVALENT_LIMIT).toBe(2);
      // Weiss am Zug nach 1.e4 e5: +30 = 52,75 %, +25 = 52,29 % (0,5 dahinter), −10 = 49,08 % (3,7 dahinter)
      const row: GameEvalPly = {
        ply: 2, depth: 20, cp: 30, bestUci: 'b1c3', playedUci: 'g1f3',
        candidates: [
          { uci: 'b1c3', cp: 30 }, { uci: 'd2d4', cp: 25 }, { uci: 'f1c4', cp: -10 }, { uci: 'g1f3', cp: 29 },
        ],
      };

      const a = acceptedMoves(row, fens[2], true, 'b1c3', 'g1f3');

      expect(a.map(x => x.uci)).toEqual(['b1c3', 'd2d4']);
      expect(a.map(x => x.san)).toEqual(['Nc3', 'd4']);
    });

    it('rechnet fuer Schwarz aus SCHWARZer Sicht', () => {
      // Schwarz am Zug: −250 (Weiss-Sicht) ist fuer Schwarz 71,5 %, −230 sind 70,1 % (1,4 dahinter), 0 sind 50 %.
      const row: GameEvalPly = {
        ply: 3, depth: 20, cp: -250, bestUci: 'g8f6', playedUci: 'b8c6',
        candidates: [{ uci: 'g8f6', cp: -250 }, { uci: 'd7d6', cp: -230 }, { uci: 'f7f6', cp: 0 }],
      };

      expect(acceptedMoves(row, fens[3], false, 'g8f6', 'b8c6').map(x => x.san)).toEqual(['Nf6', 'd6']);
    });

    it('ohne Kandidatenliste (aelterer Server) bleibt es beim Bestzug; Unspielbares faellt heraus', () => {
      const ohne: GameEvalPly = { ply: 2, depth: 20, cp: 30, bestUci: 'b1c3', playedUci: 'g1f3' };
      expect(acceptedMoves(ohne, fens[2], true, 'b1c3', 'g1f3').map(x => x.uci)).toEqual(['b1c3']);

      const kaputt: GameEvalPly = { ...ohne, candidates: [{ uci: 'b1c3', cp: 30 }, { uci: 'a1a8', cp: 30 }] };
      expect(acceptedMoves(kaputt, fens[2], true, 'b1c3', 'g1f3').map(x => x.uci)).toEqual(['b1c3']);
    });

    it('die Aufgabe traegt die gleichwertigen Zuege mit', () => {
      const mitKandidaten = evals(rows.map(r => r.ply === 2
        ? { ...r, candidates: [{ uci: 'b1c3', cp: 30 }, { uci: 'd2d4', cp: 26 }] }
        : r), 4);
      const w = collectMistakes(handReview(), mitKandidaten, fens, moves).white[0];

      expect(w.acceptUci).toEqual(['b1c3', 'd2d4']);
      expect(w.acceptSan).toEqual(['Nc3', 'd4']);
    });
  });
});
