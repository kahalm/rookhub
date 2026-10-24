import { sideToMove, whiteToMove } from './fen-side';

describe('fen-side (N11-006)', () => {
  it('liest w und b aus dem zweiten Feld', () => {
    expect(sideToMove('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1')).toBe('w');
    expect(sideToMove('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1')).toBe('b');
    expect(whiteToMove('8/8/8/8/8/8/8/K6k b - - 0 1')).toBeFalse();
  });

  it('fehlendes oder unlesbares Feld = Weiß (wie der Server)', () => {
    expect(sideToMove('8/8/8/8/8/8/8/K6k')).toBe('w');
    expect(sideToMove('8/8/8/8/8/8/8/K6k - - - 0 1')).toBe('w');
    expect(sideToMove('')).toBe('w');
    expect(sideToMove(null)).toBe('w');
    expect(whiteToMove(undefined)).toBeTrue();
  });

  it('Leerzeichen-Varianten und Großschreibung', () => {
    expect(sideToMove('8/8/8/8/8/8/8/K6k  b - - 0 1')).toBe('b');
    expect(sideToMove('  8/8/8/8/8/8/8/K6k b')).toBe('b');
    expect(sideToMove('8/8/8/8/8/8/8/K6k\tb - - 0 1')).toBe('b');
    expect(sideToMove('8/8/8/8/8/8/8/K6k B - - 0 1')).toBe('b');
  });
});
