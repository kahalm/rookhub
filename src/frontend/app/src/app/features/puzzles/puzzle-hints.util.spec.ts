import { classifyStandardFirstMove, classifyFirstSolverMove, classifyMoveAt } from './puzzle-hints.util';

describe('classifyStandardFirstMove', () => {
  it('erkennt einen ruhigen Zug', () => {
    // Startstellung; moves[0]=e2e4 (Setup), moves[1]=e7e5 (Löserzug, ruhig)
    const h = classifyStandardFirstMove(
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4 e7e5');
    expect(h).toEqual({ type: 'quiet', pieceType: 'p', san: 'e5' });
  });

  it('erkennt einen Schlagzug', () => {
    // weiße Bauer auf e4, schwarzer auf d5; Setup Nb1-c3, dann d5xe4
    const h = classifyStandardFirstMove(
      'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', 'b1c3 d5e4');
    expect(h?.type).toBe('capture');
    expect(h?.pieceType).toBe('p');
    expect(h?.san).toContain('x');
  });

  it('erkennt ein Schach (Vorrang vor Schlag)', () => {
    // Setup Ke8-d8 (schwarz), dann Rf1-f8+ (weiß) — Schach
    const h = classifyStandardFirstMove('4k3/8/8/8/8/8/8/4KR2 b - - 0 1', 'e8d8 f1f8');
    expect(h?.type).toBe('check');
    expect(h?.pieceType).toBe('r');
    expect(h?.san).toContain('+');
  });

  it('liefert null bei zu kurzer Zugliste', () => {
    expect(classifyStandardFirstMove('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4')).toBeNull();
  });
});

describe('classifyFirstSolverMove (startPly-bewusst, Buch/Daily)', () => {
  it('startPly=-1: FEN ist Trainingsstellung, Löserzug = moves[0] (Dame-Schach, NICHT moves[1])', () => {
    // Tagespuzzle 2026-06-23 (BookPuzzle 29343): Weiß am Zug, Lösung Qc2+ (g6c2), dann Kxc2 (b2c2).
    const h = classifyFirstSolverMove('8/8/6Q1/7p/2p4P/5q2/1k6/4K3 w - - 0 1', 'g6c2 b2c2', -1);
    expect(h?.type).toBe('check');
    expect(h?.pieceType).toBe('q');
    expect(h?.san).toBe('Qc2+');     // früher fälschlich „Kxc2" (moves[1], der Gegnerzug)
  });

  it('startPly=-1: ruhiger Bauernzug als Löserzug (moves[0])', () => {
    // BookPuzzle 23721: Schwarz am Zug, Lösung ...c3 (c4c3), dann Kxc3 (d4c3).
    const h = classifyFirstSolverMove('8/p7/4k3/B7/2pKP2p/Pp6/1P3P2/5b2 b - - 0 1', 'c4c3 d4c3 e6d7', -1);
    expect(h).toEqual({ type: 'quiet', pieceType: 'p', san: 'c3' });
  });

  it('startPly=0 verhält sich wie die Lichess-Konvention (moves[1])', () => {
    const h = classifyFirstSolverMove(
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', 'e2e4 e7e5', 0);
    expect(h).toEqual({ type: 'quiet', pieceType: 'p', san: 'e5' });
  });

  it('startPly=-1: liefert null bei leerer Zugliste', () => {
    expect(classifyFirstSolverMove('8/8/6Q1/7p/2p4P/5q2/1k6/4K3 w - - 0 1', '', -1)).toBeNull();
  });
});

describe('classifyMoveAt (Tipps zu JEDEM Zug, nicht nur dem ersten)', () => {
  const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
  const moves = ['e2e4', 'e7e5', 'g1f3', 'b8c6'];

  it('klassifiziert den ersten Zug (Index 0) aus der Grundstellung', () => {
    expect(classifyMoveAt(START, moves, 0)).toEqual({ type: 'quiet', pieceType: 'p', san: 'e4' });
  });

  it('klassifiziert einen SPÄTEREN Zug (Index 2) nach Nachspielen der Vorgänger', () => {
    // nach 1.e4 e5 zieht Weiß Sf3 → ruhiger Springerzug
    expect(classifyMoveAt(START, moves, 2)).toEqual({ type: 'quiet', pieceType: 'n', san: 'Nf3' });
  });

  it('liefert null bei Index außerhalb der Zugliste', () => {
    expect(classifyMoveAt(START, moves, 4)).toBeNull();
    expect(classifyMoveAt(START, moves, -1)).toBeNull();
  });
});
