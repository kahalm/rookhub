import { BookLastSolved, bookShareData } from './book-last-solved';
import { clearLastSolved, loadLastSolved } from './last-solved-store';

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('BookLastSolved (Zerlegung Z18)', () => {
  beforeEach(() => clearLastSolved('book'));
  afterEach(() => clearLastSolved('book'));
  const P: any = { id: 42, fen: FEN, moves: 'e2e4 e7e5', tags: 'fork', bookFileName: 'b' };

  it('merkt Buch-Puzzles mit Id und persistiert sie; Wochenpost ohne Id und ohne Persistenz', () => {
    const l = new BookLastSolved();
    l.remember(P, 'black', 1, false);
    expect([l.id, l.fen, l.moves, l.orientation, l.startPly, l.themes]).toEqual([42, FEN, 'e2e4 e7e5', 'black', 1, 'fork']);
    expect(loadLastSolved('book')?.id).toBe(42);
    const r = new BookLastSolved();
    expect(r.restore()).toBeTrue();
    expect(r.has).toBeTrue();
    clearLastSolved('book');
    const w = new BookLastSolved();
    w.remember(P, 'white', 0, true);
    expect(w.id).toBeNull();
    expect(w.has).toBeTrue();
    expect(loadLastSolved('book')).toBeNull();
  });

  it('Analyse-Query und Aufgabenblatt aus dem gemerkten Puzzle', () => {
    const l = new BookLastSolved();
    expect(l.analysisQuery('/x')).toBeNull();
    expect(l.worksheetItem(null)).toBeNull();
    l.remember(P, 'white', 0, false);
    expect(l.analysisQuery('/courses/7/sequential')).toEqual({ fen: FEN, moves: 'e2e4,e7e5', orientation: 'white', from: '/courses/7/sequential' });
    expect(l.worksheetItem(7)).not.toBeNull();
  });

  it('Teilen: Buch mit vorherigem Puzzle, Wochenpost teilt den Post ohne Challenge', () => {
    const d = bookShareData('https://o', 2, 1, null, false, true);
    expect(d.url).toBe('https://o/puzzles/book/2?single=1');
    expect(d.previousUrl).toBe('https://o/puzzles/book/1?single=1');
    expect(d.canChallenge).toBeTrue();
    const w = bookShareData('https://o', 0, 1, 12, true, true);
    expect(w.url).toBe('https://o/weekly/12');
    expect(w.previousUrl).toBeUndefined();
    expect(w.puzzleId).toBeUndefined();
    expect(w.canChallenge).toBeFalse();
  });
});
