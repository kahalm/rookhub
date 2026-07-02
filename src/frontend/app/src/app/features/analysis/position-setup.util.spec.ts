import { composeFen, deriveCastling, START_FEN } from './position-setup.component';

describe('position-setup FEN helpers', () => {
  it('derives full castling rights for the starting position', () => {
    expect(deriveCastling(START_FEN)).toBe('KQkq');
  });

  it('drops castling when a rook is missing', () => {
    // white king-side rook (h1) gone -> no 'K'
    const board = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN1';
    expect(deriveCastling(board)).toBe('Qkq');
  });

  it('returns "-" when kings are off their home squares', () => {
    const board = '8/8/8/4k3/4K3/8/8/8';
    expect(deriveCastling(board)).toBe('-');
  });

  it('composeFen appends side to move and derived castling', () => {
    expect(composeFen(START_FEN, 'w')).toBe(START_FEN);
    const board = '4k3/8/8/8/8/8/8/4K3';
    expect(composeFen(board, 'b')).toBe('4k3/8/8/8/8/8/8/4K3 b - - 0 1');
  });
});
