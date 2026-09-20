import { Chess } from 'chess.js';
import { isQuizLine, itemsFromLines, taskItemFromPuzzle } from './worksheet-items.util';

/** Kurs-Linie wie sie das Backend liefert (nur die Felder, die fürs Blatt zählen). */
const line = (over: Partial<any> = {}): any => ({
  id: 7,
  round: 1,
  fen: new Chess().fen(),
  moves: 'e2e4 e7e5 g1f3',
  startPly: 0,
  isInfoOnly: false,
  ...over,
});

describe('worksheet-items.util', () => {
  describe('isQuizLine', () => {
    it('nimmt abgefragte Linien', () => {
      expect(isQuizLine(line())).toBeTrue();
    });

    it('lässt Info-Seiten und zuglose Linien draußen — sie fragen nichts', () => {
      expect(isQuizLine(line({ isInfoOnly: true }))).toBeFalse();
      expect(isQuizLine(line({ moves: '   ' }))).toBeFalse();
    });
  });

  describe('itemsFromLines', () => {
    it('schickt die AUFGABEN-Stellung, nicht die Ausgangs-FEN (Vorspielzug eingespielt)', () => {
      const [item] = itemsFromLines(42, [line()]);
      const expected = new Chess();
      expected.move('e4');   // moves[0] ist Vorspiel (startPly 0)

      expect(item.fen).toBe(expected.fen());
      expect(item.orientation).toBe('black');
      expect(item.source).toBe('Book');
      expect(item.sourceId).toBe(7);
      expect(item.bookId).toBe(42);
    });

    it('lässt Überschrift und Begleittext leer — ein Linientitel verriete die Aufgabe', () => {
      const [item] = itemsFromLines(42, [line({ title: 'Matt in 3' })]);
      expect(item.heading).toBeUndefined();
      expect(item.text).toBeUndefined();
    });

    it('überspringt Info-Linien und kaputte Zuglisten statt falscher Diagramme', () => {
      const items = itemsFromLines(42, [
        line({ id: 1, isInfoOnly: true }),
        line({ id: 2, moves: 'e2e4 h8h1' }),   // zweiter Zug ist illegal
        line({ id: 3 }),
      ]);
      expect(items.map(i => i.sourceId)).toEqual([3]);
    });
  });

  describe('taskItemFromPuzzle', () => {
    it('Standard-Puzzle: der Gegnerzug moves[0] steht schon auf dem Brett', () => {
      const expected = new Chess();
      expected.move('e4');

      const item = taskItemFromPuzzle(
        { fen: new Chess().fen(), moves: 'e2e4 e7e5', orientation: 'black' }, 'Standard', { sourceId: 5 });

      expect(item!.fen).toBe(expected.fen());
      expect(item!.source).toBe('Standard');
      expect(item!.sourceId).toBe(5);
    });

    it('startPly −1: die FEN IST schon die Aufgabe', () => {
      const start = new Chess().fen();
      const item = taskItemFromPuzzle({ fen: start, moves: 'e2e4', orientation: 'white', startPly: -1 }, 'Book');
      expect(item!.fen).toBe(start);
    });

    it('Kurs-Linie mit Vorspiel: alle Züge bis einschließlich startPly kommen aufs Brett', () => {
      const expected = new Chess();
      expected.move('e4'); expected.move('e5'); expected.move('Nf3');

      const item = taskItemFromPuzzle(
        { fen: new Chess().fen(), moves: 'e2e4 e7e5 g1f3 b8c6', orientation: 'black', startPly: 2 },
        'Book', { sourceId: 9, bookId: 3 });

      expect(item!.fen).toBe(expected.fen());
      expect(item!.bookId).toBe(3);
    });

    it('unbrauchbare Stellung → null statt falscher Aufgabe', () => {
      expect(taskItemFromPuzzle({ fen: 'kaputt', moves: '', orientation: 'white' }, 'Standard')).toBeNull();
      expect(taskItemFromPuzzle(
        { fen: new Chess().fen(), moves: 'h8h1', orientation: 'white' }, 'Standard')).toBeNull();
    });
  });
});
