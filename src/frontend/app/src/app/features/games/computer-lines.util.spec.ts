import { COMPUTER_LINE_PLIES, bestMoveArrowAt, computerLinesAt } from './computer-lines.util';
import { GameEvals } from './game-review.util';

describe('computer-lines.util', () => {
  // 1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5 — Stellung 6 (nach 3…Bc5): Weiß am Zug, O-O möglich.
  const fens = [
    'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
    'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
    'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2',
    'rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2',
    'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3',
    'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3',
    'r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4',
  ];
  const evals: GameEvals = {
    status: 'done', analyzed: 6, total: 6, targetDepth: 30,
    plies: [
      { ply: 1, depth: 30, cp: 30, bestUci: 'e7e5', playedUci: 'e7e5', candidates: [
        { uci: 'e7e5', cp: 30, pv: ['e7e5', 'g1f3', 'b8c6'] },
        { uci: 'c7c5', cp: 35 },   // Altbestand ohne Variante
      ] },
      { ply: 6, depth: 30, cp: 25, bestUci: 'e1g1', playedUci: 'c2c3', candidates: [
        // Der Broker notiert die Rochade als König-schlägt-Turm — auch mitten in der Variante.
        { uci: 'e1g1', cp: 25, pv: ['e1h1', 'g8f6', 'd2d3'] },
        { uci: 'c2c3', cp: -20, pv: ['c2c3', 'g8f6'] },
        { uci: 'd2d3', mate: -4, pv: ['d2d3'] },
      ] },
    ],
  };

  it('die Linien der Stellung auf dem Brett: Bewertung in Weiß-Sicht, SAN mit Zugnummern, Rochade umgeschrieben', () => {
    const lines = computerLinesAt(evals, fens, 5);   // nach Halbzug 5 (3…Bc5) → Stellung 6
    expect(lines.map(l => l.san)).toEqual(['4. O-O Nf6 5. d3', '4. c3 Nf6', '4. d3']);
    expect(lines.map(l => l.evalText)).toEqual(['+0.25', '-0.20', '#-4']);
    expect(lines.map(l => l.whiteBetter)).toEqual([true, false, false]);
    expect(lines.map(l => l.played)).toEqual([false, true, false]);
  });

  it('Schwarz am Zug: Zugnummer mit drei Punkten; ohne Variante nur der Zug', () => {
    const lines = computerLinesAt(evals, fens, 0);   // nach 1.e4 → Stellung 1
    expect(lines.map(l => l.san)).toEqual(['1... e5 2. Nf3 Nc6', '1... c5']);
    expect(lines[0].played).toBeTrue();
  });

  it('nicht gerechnete Stellung und Endstellung: keine Linien, kein Pfeil', () => {
    expect(computerLinesAt(evals, fens, 1)).toEqual([]);
    expect(computerLinesAt(evals, fens, 6)).toEqual([]);
    expect(computerLinesAt(null, fens, 0)).toEqual([]);
    expect(bestMoveArrowAt(evals, 1)).toBeNull();
  });

  it('der Pfeil zeigt den besten Zug in Standardform', () => {
    expect(bestMoveArrowAt(evals, 5)).toEqual({ from: 'e1', to: 'g1' });
    expect(bestMoveArrowAt(evals, 0)).toEqual({ from: 'e7', to: 'e5' });
  });

  it('eine lange Variante wird gekürzt', () => {
    const long: GameEvals = { ...evals, plies: [{ ply: 0, depth: 30, cp: 20, bestUci: 'g1f3', playedUci: 'e2e4', candidates: [
      { uci: 'g1f3', cp: 20, pv: ['g1f3', 'g8f6', 'f3g1', 'f6g8', 'g1f3', 'g8f6', 'f3g1', 'f6g8', 'g1f3', 'g8f6', 'f3g1', 'f6g8', 'g1f3'] },
    ] }] };
    const san = computerLinesAt(long, fens, -1)[0].san;
    expect(san.split(' ').filter(t => !/^\d+\.$/.test(t)).length).toBe(COMPUTER_LINE_PLIES);
  });
});
