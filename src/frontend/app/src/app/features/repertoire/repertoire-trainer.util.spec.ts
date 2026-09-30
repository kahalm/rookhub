import { ParsedGame, parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { destsAt, lineChapter, linesInChapter, userMoveCount } from './repertoire-trainer.util';

function game(black: string | undefined, fen: string, moves: number): ParsedGame {
  return {
    headers: black === undefined ? {} : { Black: black },
    moves: Array.from({ length: moves }, () => ({}) as never),
    fens: [fen],
    comments: {},
  };
}

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

describe('repertoire-trainer.util', () => {
  describe('userMoveCount', () => {
    it('zählt die Halbzüge der Farbe, beginnend mit der Seite am Zug aus FEN[0]', () => {
      expect(userMoveCount(game('A', START, 5), 'w')).toBe(3);      // w b w b w
      expect(userMoveCount(game('A', START, 5), 'b')).toBe(2);
      expect(userMoveCount(game('A', AFTER_E4, 5), 'w')).toBe(2);   // b w b w b
      expect(userMoveCount(game('A', AFTER_E4, 5), 'b')).toBe(3);
    });

    it('ein einziger Gegnerzug ergibt 0 — die Linie hat nichts zu üben', () => {
      expect(userMoveCount(game('A', START, 1), 'b')).toBe(0);
      expect(userMoveCount(game('A', AFTER_E4, 1), 'w')).toBe(0);
    });

    it('ohne Züge 0, ohne FEN[0] überhaupt zu lesen', () => {
      expect(userMoveCount(game('A', 'keine FEN', 0), 'w')).toBe(0);
    });

    it('stimmt mit einer echt geparsten Linie überein', () => {
      const [g] = parsePgnText('[Event "x"]\n[Black "Italienisch"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bc4 *');
      expect(g.moves.length).toBe(5);
      expect(userMoveCount(g, 'w')).toBe(3);
      expect(userMoveCount(g, 'b')).toBe(2);
    });
  });

  describe('Kapitel', () => {
    const lines = [game(' Sizilianisch ', START, 2), game('Französisch', START, 2), game(undefined, START, 2)];

    it('lineChapter ist der getrimmte Black-Header, fehlend = leer', () => {
      expect(lines.map(lineChapter)).toEqual(['Sizilianisch', 'Französisch', '']);
    });

    it('linesInChapter filtert exakt (getrimmt), ohne Filter kommt dieselbe Liste zurück', () => {
      expect(linesInChapter(lines, 'Sizilianisch ')).toEqual([lines[0]]);
      expect(linesInChapter(lines, 'sizilianisch')).toEqual([]);
      expect(linesInChapter(lines, null)).toBe(lines);
    });
  });

  it('destsAt: Zielfelder der Stellung, unlesbare FEN = leer', () => {
    const d = destsAt(START);
    expect(d.get('e2')).toEqual(['e3', 'e4']);
    expect(d.get('g1')?.slice().sort()).toEqual(['f3', 'h3']);
    expect(d.size).toBe(10);
    expect(destsAt('kaputt').size).toBe(0);
  });
});
