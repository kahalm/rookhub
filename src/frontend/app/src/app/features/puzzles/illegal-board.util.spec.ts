import { replayIllegalFen } from './illegal-board.util';

describe('replayIllegalFen (dumb replay for illegal diagram FENs)', () => {
  // „📝65. The Lasker-Loman Tactic" — FEN ohne weißen König (illegal für chess.js).
  const FEN = '6k1/5pp1/6P1/8/8/8/8/7R w - - 0 1';
  const MOVES = ['h1h8', 'g8h8', 'g6f7']; // Rh8+ Kxh8 gxf7

  it('Index 0 = Ausgangsstellung, kein lastMove, Weiß am Zug', () => {
    const r = replayIllegalFen(FEN, MOVES, 0);
    expect(r.fen).toBe('6k1/5pp1/6P1/8/8/8/8/7R w - - 0 1');
    expect(r.lastMove).toBeUndefined();
    expect(r.whiteToMove).toBeTrue();
  });

  it('Index 1 = nach Rh8 (Turm h1→h8), Schwarz am Zug', () => {
    const r = replayIllegalFen(FEN, MOVES, 1);
    expect(r.fen).toBe('6kR/5pp1/6P1/8/8/8/8/8 b - - 0 1');
    expect(r.lastMove).toEqual(['h1', 'h8']);
    expect(r.whiteToMove).toBeFalse();
  });

  it('Index 3 = nach Rh8 Kxh8 gxf7 (Schlag + Königsschlag), Schwarz am Zug', () => {
    const r = replayIllegalFen(FEN, MOVES, 3);
    expect(r.fen).toBe('7k/5Pp1/8/8/8/8/8/8 b - - 0 1');
    expect(r.lastMove).toEqual(['g6', 'f7']);
    expect(r.whiteToMove).toBeFalse();
  });

  it('clamped: count über die Zugzahl hinaus = Endstellung', () => {
    expect(replayIllegalFen(FEN, MOVES, 99).fen).toBe(replayIllegalFen(FEN, MOVES, 3).fen);
  });

  it('Unterverwandlung: f7f8n setzt einen weißen Springer', () => {
    const r = replayIllegalFen('6k1/5P2/8/8/8/8/8/8 w - - 0 1', ['f7f8n'], 1);
    expect(r.fen).toBe('5Nk1/8/8/8/8/8/8/8 b - - 0 1');
  });
});
