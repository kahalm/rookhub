import { startNumbering, prettyMoveLabel } from './repertoire-move-format.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
// Schwarz am Zug, Vollzug 3 (z. B. eine mitten begonnene Linie).
const BLACK_TO_MOVE = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 3';

describe('repertoire-move-format.util', () => {
  describe('startNumbering', () => {
    it('reads white-to-move + fullmove 1 from the initial FEN', () => {
      expect(startNumbering(START)).toEqual({ side: 'w', fullMove: 1 });
    });
    it('reads black-to-move + fullmove from a mid-line FEN', () => {
      expect(startNumbering(BLACK_TO_MOVE)).toEqual({ side: 'b', fullMove: 3 });
    });
    it('falls back to white / move 1 on an unparsable FEN', () => {
      expect(startNumbering('not-a-fen')).toEqual({ side: 'w', fullMove: 1 });
    });
  });

  describe('prettyMoveLabel', () => {
    it('labels a white move as "N. san"', () => {
      // ply 0 from initial position → white 1. e4
      expect(prettyMoveLabel(START, 'e4', 0)).toBe('1. e4');
      // ply 2 → white 2. Nf3
      expect(prettyMoveLabel(START, 'Nf3', 2)).toBe('2. Nf3');
    });
    it('labels a black move as "N… san"', () => {
      // ply 1 from initial position → black 1… e5
      expect(prettyMoveLabel(START, 'e5', 1)).toBe('1… e5');
      // ply 3 → black 2… d6
      expect(prettyMoveLabel(START, 'd6', 3)).toBe('2… d6');
    });
    it('respects a non-1 starting fullmove and black start side', () => {
      // start: black to move at move 3 → ply 0 is black 3… san
      expect(prettyMoveLabel(BLACK_TO_MOVE, 'Nf6', 0)).toBe('3… Nf6');
      // ply 1 → white 4. san
      expect(prettyMoveLabel(BLACK_TO_MOVE, 'c4', 1)).toBe('4. c4');
    });
  });
});
