import {
  GameEvalPly, GameEvals, MOVE_CLASSES, classify, formatEval, graphHeight, moveAccuracy, reviewGame, sideAccuracy,
  volatilityWeights, whiteToMove, windowSizeFor, winPercent,
} from './game-review.util';
import { sacrificedPiece } from './move-tactics.util';

// Alle erwarteten Zahlen sind LITERALE (unabhängig nachgerechnet), keine Aufrufe derselben Formel —
// ein Test, der die Formel ein zweites Mal ausrechnet, wandert mit jedem Fehler mit.

describe('game-review.util', () => {
  // chess.com-Skala (Vergleich 2026-09-24): Bewertung linear bis ±10 Bauern, Matt am Rand.
  describe('graphHeight (Kurvenhöhe, linear bis ±10)', () => {
    it('0 = Mitte, ±6,51 = 82,55 / 17,45, ±10 und mehr am Rand', () => {
      expect(graphHeight({ cp: 0 })).toBe(50);
      expect(graphHeight({ cp: 651 })).toBeCloseTo(82.55, 6);
      expect(graphHeight({ cp: -651 })).toBeCloseTo(17.45, 6);
      expect(graphHeight({ cp: 1000 })).toBe(100);
      expect(graphHeight({ cp: 2500 })).toBe(100);
      expect(graphHeight({ cp: -2500 })).toBe(0);
    });

    it('Matt am Rand, ohne Bewertung keine Höhe', () => {
      expect(graphHeight({ mate: 3 })).toBe(100);
      expect(graphHeight({ mate: -2 })).toBe(0);
      expect(graphHeight(null)).toBeNull();
      expect(graphHeight({})).toBeNull();
    });
  });

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
      // Die gezeichnete Kurve ist die lineare Skala (+3,00 → 65), nicht die Gewinnchance.
      expect(r.curve.length).toBe(5);
      expect(r.curve[4]!).toBeCloseTo(65, 6);
    });

    it('Klassen aus Sicht des Ziehenden — Schwarz verliert, wenn Weiß steigt', () => {
      const r = reviewGame(evals(), fens);
      expect(r.moves.map(m => m?.cls)).toEqual(['best', 'excellent', 'best', 'blunder']);
      expect(r.moves[1]!.white).toBeFalse();
      expect(r.moves[3]!.winBefore).toBeCloseTo(46.3246, 3);   // 100 − 53,6754
      expect(r.moves[3]!.winAfter).toBeCloseTo(24.8874, 3);    // 100 − 75,1126
      expect(r.moves[3]!.accuracy).toBeCloseTo(37.4009, 3);
      expect(r.black.counts).toEqual({
        brilliant: 0, great: 0, best: 0, excellent: 1, good: 0, inaccuracy: 0, mistake: 0, miss: 0, blunder: 1,
      });
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

  describe('Sonderklassen Miss / Brilliant / Great (Bedingungen chess.com, Zahlen freechess)', () => {
    // 1.e4 c5 2.Sf3 d6 — dieselbe Partie wie oben, jetzt mit den Halbzügen als UCI.
    const SICILIAN = [
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
      'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2',
      'rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3',
    ];
    const UCIS = ['e2e4', 'c7c5', 'g1f3', 'd7d6'];
    // 6…0-0 7.Lxh7+ (Griechisches Geschenk, Zugfolge in move-tactics.util.spec.ts).
    const GREEK = [
      'rnbqk2r/pppnbppp/4p3/3pP3/3P4/2NB1N2/PPP2PPP/R1BQK2R b KQkq - 4 6',
      'rnbq1rk1/pppnbppp/4p3/3pP3/3P4/2NB1N2/PPP2PPP/R1BQK2R w KQ - 5 7',
      'rnbq1rk1/pppnbppB/4p3/3pP3/3P4/2N2N2/PPP2PPP/R1BQK2R b KQ - 0 7',
    ];
    // 5…Lxh2+ mit Schwarz am Zug.
    const GREEK_BLACK = [
      'rnbqk2r/ppp2ppp/3bpn2/3p4/3P4/3BP3/PPP1NPPP/RNBQ1RK1 b kq - 3 5',
      'rnbqk2r/ppp2ppp/4pn2/3p4/3P4/3BP3/PPP1NPPb/RNBQ1RK1 w kq - 0 6',
    ];

    const ply = (p: number, score: { cp?: number; mate?: number }, bestUci: string, playedUci: string,
                 second: { secondCp?: number; secondMate?: number } = {}): GameEvalPly =>
      ({ ply: p, depth: 20, ...score, bestUci, playedUci, ...second });
    const game = (plies: GameEvalPly[], final: { cp?: number; mate?: number }): GameEvals =>
      ({ status: 'done', analyzed: plies.length, total: plies.length, targetDepth: 20, plies, final });

    it('Reihenfolge der Anzeige', () => {
      expect(MOVE_CLASSES).toEqual(
        ['brilliant', 'great', 'best', 'excellent', 'good', 'inaccuracy', 'mistake', 'miss', 'blunder']);
    });

    describe('Miss', () => {
      const missGame = (afterCp: number, beforeCp = 300, blackCp = 25) => game([
        ply(0, { cp: 30 }, 'e2e4', 'e2e4'),
        ply(1, { cp: blackCp }, 'e7e5', 'c7c5'),
        ply(2, { cp: beforeCp }, 'd2d4', 'g1f3'),
        ply(3, { cp: afterCp }, 'b8c6', 'd7d6'),
      ], { cp: afterCp });

      it('ersetzt Blunder: Weiß bestraft den groben Fehler von Schwarz nicht (vorher 75,11 % ≥ 70, danach 53,68 % ≤ 60)', () => {
        // Schwarz 1…c5: 47,70 → 24,89 % (grober Fehler). Weiß 2.Sf3: 75,11 → 53,68 %.
        const r = reviewGame(missGame(40), SICILIAN, UCIS);
        expect(r.moves.map(m => m?.cls)).toEqual(['best', 'blunder', 'miss', 'best']);
        expect(r.moves[2]!.winBefore).toBeCloseTo(75.1126, 3);
        expect(r.moves[2]!.winAfter).toBeCloseTo(53.6754, 3);
        expect(r.moves[2]!.accuracy).toBeCloseTo(37.4009, 3);   // ein Etikett, keine andere Zahl
        expect(r.white.counts).toEqual({
          brilliant: 0, great: 0, best: 1, excellent: 0, good: 0, inaccuracy: 0, mistake: 0, miss: 1, blunder: 0,
        });
      });

      it('ersetzt auch einen Fehler: danach 59,10 % (Verlust 16,01 = mistake)', () => {
        expect(reviewGame(missGame(100), SICILIAN, UCIS).moves[2]!.cls).toBe('miss');
      });

      it('kein Miss, solange die Gewinnchance danach über 60 % bleibt (60,52 %)', () => {
        expect(reviewGame(missGame(116), SICILIAN, UCIS).moves[2]!.cls).toBe('mistake');
      });

      it('kein Miss, wenn danach weniger als 40 % bleiben (39,13 %) — das ist ein grober Fehler, kein Verpassen', () => {
        const r = reviewGame(missGame(-120), SICILIAN, UCIS);
        expect(r.moves[2]!.winAfter).toBeCloseTo(39.1301, 3);
        expect(r.moves[2]!.cls).toBe('blunder');
        expect(reviewGame(missGame(-100), SICILIAN, UCIS).moves[2]!.cls).toBe('miss');   // 40,90 % ≥ 40
      });

      it('kein Miss, wenn der Bestzug keine 70 % gebracht hätte (67,62 %)', () => {
        const r = reviewGame(missGame(40, 200), SICILIAN, UCIS);
        expect(r.moves[1]!.cls).toBe('mistake');   // der Gegner hat trotzdem einen Fehler gemacht
        expect(r.moves[2]!.cls).toBe('mistake');
      });

      it('kein Miss ohne Fehler des Gegners davor', () => {
        const r = reviewGame(missGame(40, 300, 300), SICILIAN, UCIS);   // Weiß stand schon vorher auf +3,00
        expect(r.moves[1]!.cls).toBe('best');
        expect(r.moves[2]!.cls).toBe('blunder');
      });

      it('eine Lücke davor: ohne bewerteten Zug des Gegners kein Miss', () => {
        const e = missGame(40);
        e.plies = e.plies.filter(p => p.ply !== 1);
        const r = reviewGame(e, SICILIAN, UCIS);
        expect(r.moves[1]).toBeNull();
        expect(r.moves[2]!.cls).toBe('blunder');
      });

      it('Schwarz am Zug: lässt den groben Fehler von Weiß liegen (Vorzeichen!)', () => {
        const r = reviewGame(game([
          ply(0, { cp: 30 }, 'd2d4', 'e2e4'),      // Weiß: 52,76 → 24,89 %, grober Fehler
          ply(1, { cp: -300 }, 'e7e5', 'c7c5'),    // Schwarz: 75,11 → 53,68 %
          ply(2, { cp: -40 }, 'g1f3', 'g1f3'),
          ply(3, { cp: -40 }, 'd7d6', 'd7d6'),
        ], { cp: -40 }), SICILIAN, UCIS);
        expect(r.moves.map(m => m?.cls)).toEqual(['blunder', 'miss', 'best', 'best']);
        expect(r.moves[1]!.winBefore).toBeCloseTo(75.1126, 3);
        expect(r.moves[1]!.winAfter).toBeCloseTo(53.6754, 3);
        expect(r.black.counts.miss).toBe(1);
      });
    });

    describe('Brilliant', () => {
      const greek = (score: { cp?: number; mate?: number }, final: { cp?: number; mate?: number },
                     second: { secondCp?: number; secondMate?: number } = {}, bestUci = 'd3h7') =>
        reviewGame(game([ply(0, score, bestUci, 'd3h7', second)], final), GREEK.slice(1), ['d3h7']);

      it('Lxh7+ opfert den Läufer, ist der Bestzug, der Zweitbeste liegt bei +0,50 → brilliant, mit geopferter Figur', () => {
        const r = greek({ cp: 150 }, { cp: 150 }, { secondCp: 50 });
        expect(r.moves[0]!.cls).toBe('brilliant');
        expect(r.moves[0]!.sacrifice).toEqual({ square: 'h7', piece: 'b' });
        expect(r.moves[0]!.accuracy).toBe(100);
        expect(r.white.counts.brilliant).toBe(1);
        expect(r.white.counts.best).toBe(0);
      });

      it('ohnehin gewonnen: Zweitbester +7,00 → best (Grenze), +6,99 → brilliant', () => {
        expect(greek({ cp: 900 }, { cp: 900 }, { secondCp: 700 }).moves[0]!.cls).toBe('best');
        expect(greek({ cp: 900 }, { cp: 900 }, { secondCp: 699 }).moves[0]!.cls).toBe('brilliant');
      });

      it('ohnehin gewonnen: der Zweitbeste setzt selbst matt → best', () => {
        expect(greek({ mate: 3 }, { mate: 2 }, { secondMate: 5 }).moves[0]!.cls).toBe('best');
      });

      it('ohne zweiten Kandidaten gilt der Zug nicht als ohnehin gewonnen → brilliant', () => {
        expect(greek({ cp: 150 }, { cp: 150 }).moves[0]!.cls).toBe('brilliant');
      });

      it('danach schlechter als −1,00 → best; genau −1,00 → brilliant', () => {
        expect(greek({ cp: -80 }, { cp: -101 }, { secondCp: -300 }).moves[0]!.cls).toBe('best');
        expect(greek({ cp: -80 }, { cp: -100 }, { secondCp: -300 }).moves[0]!.cls).toBe('brilliant');
      });

      it('fast der beste Zug (excellent, Verlust 0,86) kann brillant sein, ein nur guter (Verlust 4,36) nicht', () => {
        expect(greek({ cp: 150 }, { cp: 140 }, { secondCp: 140 }, 'e1g1').moves[0]!.cls).toBe('brilliant');
        expect(greek({ cp: 150 }, { cp: 100 }, { secondCp: 140 }, 'e1g1').moves[0]!.cls).toBe('good');
      });

      it('eine Umwandlung ist nie brillant — auch wenn die neue Dame hängt', () => {
        const fens = ['r7/4P2k/8/8/8/8/8/6K1 w - - 0 1', 'r3Q3/7k/8/8/8/8/8/6K1 b - - 0 1'];
        expect(sacrificedPiece(fens[0], fens[1], 'e7e8q')).toEqual({ square: 'e8', piece: 'q' });   // Opfer läge vor
        const r = reviewGame(game([ply(0, { cp: 0 }, 'e7e8q', 'e7e8q', { secondCp: -500 })], { cp: 0 }), fens, ['e7e8q']);
        expect(r.moves[0]!.cls).toBe('best');
      });

      it('wer vorher im Schach stand, zieht nicht brillant — auch wenn die Figur danach hängt', () => {
        // Lb4+ — Weiß stellt Sc3 dazwischen, der Bauer d4 schlägt ihn.
        const fens = ['4k3/8/8/8/1b1p4/8/8/1N2K3 w - - 0 1', '4k3/8/8/8/1b1p4/2N5/8/4K3 b - - 1 1'];
        expect(sacrificedPiece(fens[0], fens[1], 'b1c3')).toEqual({ square: 'c3', piece: 'n' });   // Opfer läge vor
        const r = reviewGame(game([ply(0, { cp: 0 }, 'b1c3', 'b1c3', { secondCp: -900 })], { cp: 0 }), fens, ['b1c3']);
        expect(r.moves[0]!.cls).toBe('best');
      });

      it('Schwarz am Zug: 5…Lxh2+ — die Weiß-Bewertungen werden für Schwarz gedreht', () => {
        const black = (after: number, secondCp: number) =>
          reviewGame(game([ply(0, { cp: -150 }, 'd6h2', 'd6h2', { secondCp })], { cp: after }), GREEK_BLACK, ['d6h2']);
        const r = black(-150, -50);                             // Schwarz +1,50, Zweitbester +0,50
        expect(r.moves[0]!.cls).toBe('brilliant');
        expect(r.moves[0]!.sacrifice).toEqual({ square: 'h2', piece: 'b' });
        expect(r.black.counts.brilliant).toBe(1);
        expect(black(-150, -700).moves[0]!.cls).toBe('best');   // Zweitbester Schwarz +7,00: ohnehin gewonnen
        expect(black(101, -50).moves[0]!.cls).toBe('best');     // danach Schwarz −1,01
        expect(black(100, -50).moves[0]!.cls).toBe('brilliant');   // danach Schwarz −1,00
      });
    });

    describe('Great', () => {
      const greatGame = (second: { secondCp?: number; secondMate?: number }, blackCp = 25,
                         best: { cp?: number; mate?: number } = { cp: 300 }, after: { cp?: number; mate?: number } = { cp: 280 }) => game([
        ply(0, { cp: 30 }, 'e2e4', 'e2e4'),
        ply(1, { cp: blackCp }, 'e7e5', 'c7c5'),
        ply(2, best, 'g1f3', 'g1f3', second),
        ply(3, after, 'd7d6', 'd7d6'),
      ], after);

      it('einziger guter Zug nach dem groben Fehler des Gegners: Abstand 1,50 (Grenze), danach 73,71 % → great', () => {
        const r = reviewGame(greatGame({ secondCp: 150 }), SICILIAN, UCIS);
        expect(r.moves.map(m => m?.cls)).toEqual(['best', 'blunder', 'great', 'best']);
        expect(r.moves[2]!.gapPawns).toBe(1.5);
        expect(r.moves[2]!.accuracy).toBeCloseTo(93.8910, 3);   // 75,11 → 73,71 %: dieselbe Zahl wie als „best"
        expect(r.white.counts.great).toBe(1);
        expect(r.white.counts.best).toBe(1);
      });

      it('Abstand 1,49 → best', () => {
        const r = reviewGame(greatGame({ secondCp: 151 }), SICILIAN, UCIS);
        expect(r.moves[2]!.cls).toBe('best');
        expect(r.moves[2]!.gapPawns).toBe(1.49);
      });

      it('ohne Fehler des Gegners davor → best', () => {
        const r = reviewGame(greatGame({ secondCp: 100 }, 300), SICILIAN, UCIS);
        expect(r.moves[1]!.cls).toBe('best');
        expect(r.moves[2]!.cls).toBe('best');
      });

      it('danach unter 45 % → best (40,90 %)', () => {
        const r = reviewGame(game([
          ply(0, { cp: -600 }, 'e2e4', 'e2e4'),
          ply(1, { cp: -600 }, 'e7e5', 'c7c5'),                        // Schwarz: 90,11 → 59,10 %, grober Fehler
          ply(2, { cp: -100 }, 'g1f3', 'g1f3', { secondCp: -300 }),    // Abstand 2,00, aber danach nur 40,90 %
          ply(3, { cp: -100 }, 'd7d6', 'd7d6'),
        ], { cp: -100 }), SICILIAN, UCIS);
        expect(r.moves[1]!.cls).toBe('blunder');
        expect(r.moves[2]!.cls).toBe('best');
      });

      it('Matt gegen Nicht-Matt ist ein großer Abstand → great', () => {
        const r = reviewGame(greatGame({ secondCp: 500 }, 25, { mate: 3 }, { mate: 2 }), SICILIAN, UCIS);
        expect(r.moves[2]!.cls).toBe('great');
        expect(r.moves[2]!.gapPawns).toBe(992);   // (1000 − 3) − 5
      });

      it('beide Matt: der Zweitbeste setzt selbst matt, also war es nicht der einzige gute Zug → best (auch #2 gegen #4)', () => {
        expect(reviewGame(greatGame({ secondMate: 3 }, 25, { mate: 2 }, { mate: 1 }), SICILIAN, UCIS).moves[2]!.cls)
          .toBe('best');
        const r = reviewGame(greatGame({ secondMate: 4 }, 25, { mate: 2 }, { mate: 1 }), SICILIAN, UCIS);
        expect(r.moves[2]!.cls).toBe('best');
        expect(r.moves[2]!.gapPawns).toBe(2);   // der Abstand wird trotzdem ausgewiesen
      });

      it('der Zweitbeste gewinnt ohnehin (+7,00): kein Great — dieselbe Grenze wie bei Brilliant; +6,99 → great', () => {
        expect(reviewGame(greatGame({ secondCp: 700 }, 25, { cp: 900 }, { cp: 850 }), SICILIAN, UCIS).moves[2]!.cls)
          .toBe('best');
        expect(reviewGame(greatGame({ secondCp: 699 }, 25, { cp: 900 }, { cp: 850 }), SICILIAN, UCIS).moves[2]!.cls)
          .toBe('great');
      });

      it('der erste Zug der Partie hat keinen vorigen Zug → kein Great', () => {
        const r = reviewGame(game([
          ply(0, { cp: 30 }, 'e2e4', 'e2e4', { secondCp: -200 }),
          ply(1, { cp: 30 }, 'c7c5', 'c7c5'),
        ], { cp: 30 }), SICILIAN, UCIS);
        expect(r.moves[0]!.cls).toBe('best');
        expect(r.moves[0]!.gapPawns).toBe(2.3);
      });

      it('Schwarz am Zug: einziger guter Zug nach dem groben Fehler von Weiß (Vorzeichen!)', () => {
        const r = reviewGame(game([
          ply(0, { cp: 30 }, 'e2e4', 'e2e4'),
          ply(1, { cp: 25 }, 'c7c5', 'c7c5'),
          ply(2, { cp: 35 }, 'd2d4', 'g1f3'),                          // Weiß: 53,22 → 28,49 %, grober Fehler
          ply(3, { cp: -250 }, 'd7d6', 'd7d6', { secondCp: -50 }),     // Schwarz: +2,50 gegen +0,50
        ], { cp: -250 }), SICILIAN, UCIS);
        expect(r.moves.map(m => m?.cls)).toEqual(['best', 'best', 'blunder', 'great']);
        expect(r.moves[3]!.gapPawns).toBe(2);
        expect(r.black.counts.great).toBe(1);
      });

      it('hängt danach eine eigene Figur, ist es kein Great — mit Zweitbestem unter +7 wird es brilliant (Brilliant geht vor)', () => {
        const greekGame = (secondCp: number) => reviewGame(game([
          ply(0, { cp: 30 }, 'c7c5', 'e8g8'),                          // Schwarz rochiert in den Angriff: 47,24 → 1,19 %
          ply(1, { cp: 1200 }, 'd3h7', 'd3h7', { secondCp }),
        ], { cp: 1200 }), GREEK, ['e8g8', 'd3h7']);
        const winning = greekGame(800);                                // Abstand 4,00, aber ohnehin gewonnen
        expect(winning.moves[0]!.cls).toBe('blunder');
        expect(winning.moves[1]!.cls).toBe('best');
        expect(greekGame(300).moves[1]!.cls).toBe('brilliant');
      });
    });

    it('ohne UCIs keine Sonderklassen — Miss, Great und Brilliant bleiben bei ihrer Grundklasse', () => {
      const miss = game([
        ply(0, { cp: 30 }, 'e2e4', 'e2e4'), ply(1, { cp: 25 }, 'e7e5', 'c7c5'),
        ply(2, { cp: 300 }, 'd2d4', 'g1f3'), ply(3, { cp: 40 }, 'b8c6', 'd7d6'),
      ], { cp: 40 });
      expect(reviewGame(miss, SICILIAN).moves.map(m => m?.cls)).toEqual(['best', 'blunder', 'blunder', 'best']);
      const great = game([
        ply(0, { cp: 30 }, 'e2e4', 'e2e4'), ply(1, { cp: 25 }, 'e7e5', 'c7c5'),
        ply(2, { cp: 300 }, 'g1f3', 'g1f3', { secondCp: 100 }), ply(3, { cp: 280 }, 'd7d6', 'd7d6'),
      ], { cp: 280 });
      expect(reviewGame(great, SICILIAN).moves[2]!.cls).toBe('best');
      const brilliant = game([ply(0, { cp: 150 }, 'd3h7', 'd3h7', { secondCp: 50 })], { cp: 150 });
      expect(reviewGame(brilliant, GREEK.slice(1)).moves[0]!.cls).toBe('best');
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
