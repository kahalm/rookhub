import {
  GameEvals, classify, formatEval, moveAccuracy, reviewGame, sideAccuracy, volatilityWeights, whiteToMove,
  windowSizeFor, winPercent,
} from './game-review.util';

// Alle erwarteten Zahlen sind LITERALE (unabhängig nachgerechnet), keine Aufrufe derselben Formel —
// ein Test, der die Formel ein zweites Mal ausrechnet, wandert mit jedem Fehler mit.

describe('game-review.util', () => {
  describe('winPercent (Lichess, Weiß-Sicht)', () => {
    it('0 cp = 50 %, ±100 cp = 59,10 / 40,90 %', () => {
      expect(winPercent({ cp: 0 })).toBe(50);
      expect(winPercent({ cp: 100 })!).toBeCloseTo(59.1026, 3);
      expect(winPercent({ cp: -100 })!).toBeCloseTo(40.8974, 3);
      expect(winPercent({ cp: 300 })!).toBeCloseTo(75.1126, 3);
      expect(winPercent({ cp: -250 })!).toBeCloseTo(28.4852, 3);
    });

    it('Matt ist der Rand, keine große Zahl: Weiß setzt matt = 100, wird matt = 0', () => {
      expect(winPercent({ mate: 3 })).toBe(100);
      expect(winPercent({ mate: -2 })).toBe(0);
    });

    it('mate 0 = die Seite am Zug IST matt — dafür entscheidet die FEN', () => {
      expect(winPercent({ mate: 0 }, true)).toBe(0);
      expect(winPercent({ mate: 0 }, false)).toBe(100);
    });

    it('ohne Bewertung: null statt 50 (eine Lücke ist kein Ausgleich)', () => {
      expect(winPercent(null)).toBeNull();
      expect(winPercent({})).toBeNull();
    });
  });

  it('whiteToMove liest das zweite FEN-Feld', () => {
    expect(whiteToMove('8/8/8/8/8/8/8/K6k w - - 0 1')).toBeTrue();
    expect(whiteToMove('8/8/8/8/8/8/8/K6k b - - 0 1')).toBeFalse();
  });

  describe('moveAccuracy (Sicht des Ziehenden)', () => {
    it('kein Verlust = 100 (die Formel allein gäbe 99,9999), Gewinn ebenso', () => {
      expect(moveAccuracy(60, 60)).toBe(100);
      expect(moveAccuracy(60, 70)).toBe(100);
    });

    it('Lichess-Werte', () => {
      expect(moveAccuracy(80, 79)).toBeCloseTo(95.6044, 3);
      expect(moveAccuracy(60, 50)).toBeCloseTo(63.5826, 3);
      expect(moveAccuracy(70, 40)).toBeCloseTo(24.7756, 3);
      expect(moveAccuracy(50, 0)).toBeCloseTo(8.5303, 3);
    });

    it('nie unter 0', () => {
      expect(moveAccuracy(100, 0)).toBe(0);
    });
  });

  describe('classify (chess.com-Bänder, in Prozentpunkten)', () => {
    it('Grenzen gehören zur BESSEREN Klasse', () => {
      expect(classify(60, 60, false)).toBe('best');
      expect(classify(60, 58, false)).toBe('excellent');
      expect(classify(60, 57.99, false)).toBe('good');
      expect(classify(60, 55, false)).toBe('good');
      expect(classify(60, 54.99, false)).toBe('inaccuracy');
      expect(classify(60, 50, false)).toBe('inaccuracy');
      expect(classify(60, 49.99, false)).toBe('mistake');
      expect(classify(60, 40, false)).toBe('mistake');
      expect(classify(60, 39.99, false)).toBe('blunder');
    });

    it('der Engine-Bestzug ist best, auch wenn die nächste Stellung tiefer weniger sieht', () => {
      expect(classify(60, 30, true)).toBe('best');
    });

    it('ein Zug, der gewinnt, ist best', () => {
      expect(classify(40, 55, false)).toBe('best');
    });
  });

  describe('Volatilität und Seiten-Genauigkeit (Lichess)', () => {
    it('Fensterbreite = Halbzüge / 10, ganzzahlig, 2..8', () => {
      expect(windowSizeFor(5)).toBe(2);
      expect(windowSizeFor(29)).toBe(2);
      expect(windowSizeFor(30)).toBe(3);
      expect(windowSizeFor(85)).toBe(8);
      expect(windowSizeFor(200)).toBe(8);
    });

    it('kurze Partie: gleitendes Zweierfenster, ein Gewicht je Zug', () => {
      expect(volatilityWeights([50, 60, 40, 55])).toEqual([5, 10, 7.5]);
    });

    it('Gewichte auf 0,5..12 begrenzt', () => {
      expect(volatilityWeights([50, 50, 0])).toEqual([0.5, 12]);
    });

    it('breiteres Fenster: das ERSTE wird für die ersten (Breite − 2) Züge wiederholt', () => {
      const series = [50, 56, 44, ...new Array(28).fill(50)];   // 31 Stellungen = 30 Züge → Fenster 3
      const w = volatilityWeights(series);
      expect(w.length).toBe(30);
      expect(w[0]).toBeCloseTo(4.8990, 3);   // std(50, 56, 44)
      expect(w[1]).toBeCloseTo(4.8990, 3);   // dasselbe erste Fenster
      expect(w[2]).toBeCloseTo(4.8990, 3);   // std(56, 44, 50)
      expect(w[3]).toBeCloseTo(2.8284, 3);   // std(44, 50, 50)
      expect(w[4]).toBe(0.5);                // ruhig → Untergrenze
    });

    it('eine Lücke fällt aus ihrem Fenster, statt als 0 % mitzuzählen', () => {
      expect(volatilityWeights([50, null, 70])).toEqual([0.5, 0.5]);
    });

    it('gewichtetes + harmonisches Mittel, halbiert', () => {
      expect(sideAccuracy([{ accuracy: 100, weight: 1 }, { accuracy: 50, weight: 1 }])!).toBeCloseTo(70.8333, 3);
      expect(sideAccuracy([
        { accuracy: 100, weight: 5 }, { accuracy: 50, weight: 10 }, { accuracy: 80, weight: 7.5 },
      ])!).toBeCloseTo(70.8497, 3);
    });

    it('ohne Zug: null, nicht 0 %', () => {
      expect(sideAccuracy([])).toBeNull();
    });
  });

  describe('reviewGame', () => {
    // 1.e4 c5 2.Nf3 d6 — Schwarz verdirbt mit d6 (+0,40 → +3,00 aus Weiß-Sicht).
    const fens = [
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
      'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2',
      'rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3',
    ];
    const evals = (): GameEvals => ({
      status: 'done', analyzed: 4, total: 4, targetDepth: 20, analysisId: 1,
      plies: [
        { ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30 },
        { ply: 1, cp: 25, depth: 20, bestUci: 'e7e5', playedUci: 'c7c5', playedCp: 35 },
        { ply: 2, cp: 35, depth: 20, bestUci: 'g1f3', playedUci: 'g1f3', playedCp: 35 },
        { ply: 3, cp: 40, depth: 20, bestUci: 'b8c6', playedUci: 'd7d6', playedCp: 300 },
      ],
      final: { cp: 300 },
    });

    it('Kurve in Weiß-Sicht, Start bis Endstellung', () => {
      const r = reviewGame(evals(), fens);
      expect(r.series.length).toBe(5);
      expect(r.series[0]!).toBeCloseTo(52.7588, 3);
      expect(r.series[1]!).toBeCloseTo(52.2997, 3);
      expect(r.series[4]!).toBeCloseTo(75.1126, 3);
    });

    it('Klassen aus Sicht des Ziehenden — Schwarz verliert, wenn Weiß steigt', () => {
      const r = reviewGame(evals(), fens);
      expect(r.moves.map(m => m?.cls)).toEqual(['best', 'excellent', 'best', 'blunder']);
      expect(r.moves[1]!.white).toBeFalse();
      expect(r.moves[3]!.winBefore).toBeCloseTo(46.3246, 3);   // 100 − 53,6754
      expect(r.moves[3]!.winAfter).toBeCloseTo(24.8874, 3);    // 100 − 75,1126
      expect(r.moves[3]!.accuracy).toBeCloseTo(37.4009, 3);
      expect(r.black.counts).toEqual({ best: 0, excellent: 1, good: 0, inaccuracy: 0, mistake: 0, blunder: 1 });
      expect(r.white.counts.best).toBe(2);
    });

    it('Genauigkeit je Seite, volatilitäts-gewichtet', () => {
      const r = reviewGame(evals(), fens);
      expect(r.white.accuracy!).toBeCloseTo(98.9739, 3);
      expect(r.black.accuracy!).toBeCloseTo(46.9172, 3);
    });

    it('fehlt die nächste Stellung, trägt der gespielte Kandidat; fehlt die eigene, ist der Zug nicht bewertbar', () => {
      const e = evals();
      e.plies = e.plies.filter(p => p.ply !== 2);
      const r = reviewGame(e, fens);
      expect(r.series[2]).toBeNull();                 // Lücke in der Kurve
      expect(r.moves[1]).not.toBeNull();              // „danach" = gespielter Kandidat (+0,35)
      expect(r.moves[1]!.evalAfter).toEqual({ cp: 35, mate: null });
      expect(r.moves[2]).toBeNull();                  // davor nicht gerechnet
    });

    it('ohne final und ohne gespielten Kandidaten ist der letzte Zug nicht bewertbar', () => {
      const e = evals();
      e.final = null;
      e.plies[3].playedCp = null;
      expect(reviewGame(e, fens).moves[3]).toBeNull();
    });

    it('die Seite kommt aus der FEN, nicht aus der Parität: Partie ab Stellung mit Schwarz am Zug', () => {
      const r = reviewGame({
        status: 'done', analyzed: 1, total: 1, targetDepth: 20,
        plies: [{ ply: 0, cp: -50, depth: 20, bestUci: 'a7a6', playedUci: 'h7h6', playedCp: 200 }],
        final: { cp: 200 },
      }, ['4k3/p6p/8/8/8/8/P6P/4K3 b - - 0 1', '4k3/p7/7p/8/8/8/P6P/4K3 w - - 0 2']);
      expect(r.moves[0]!.white).toBeFalse();
      expect(r.moves[0]!.winBefore).toBeCloseTo(54.5896, 3);   // Schwarz: 100 − Weiß(−50 cp)
      expect(r.moves[0]!.winAfter).toBeCloseTo(32.3788, 3);    // 100 − Weiß(+200 cp)
      expect(r.moves[0]!.cls).toBe('blunder');
      expect(r.black.counts.blunder).toBe(1);
      expect(r.white.accuracy).toBeNull();
    });

    it('ohne Analyse: nichts bewertbar, beide Seiten ohne Genauigkeit', () => {
      const r = reviewGame(null, fens);
      expect(r.moves.every(m => m === null)).toBeTrue();
      expect(r.series.every(s => s === null)).toBeTrue();
      expect(r.white.accuracy).toBeNull();
    });
  });

  it('formatEval wie auf dem Analysebrett', () => {
    expect(formatEval({ cp: 34 })).toBe('+0.34');
    expect(formatEval({ cp: -120 })).toBe('-1.20');
    expect(formatEval({ cp: 0 })).toBe('0.00');
    expect(formatEval({ mate: 3 })).toBe('#3');
    expect(formatEval({ mate: -2 })).toBe('#-2');
    expect(formatEval(null)).toBe('');
  });
});
