import { saveBookOffline, getBookOffline, getBookOfflineByBookId, removeBookOffline, hasBookOffline } from './book-offline.util';
import { BookPuzzleDto } from './puzzle.service';

function puzzle(id: number, fileName: string): BookPuzzleDto {
  return { id, lineId: `l${id}`, bookFileName: fileName, round: '', fen: '8/8/8/8/8/8/8/8 w - - 0 1', moves: 'e2e4' } as BookPuzzleDto;
}

describe('book-offline.util', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('saves + reads a book by file name', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn'), puzzle(2, 'book-a.pgn')]);
    expect(hasBookOffline('book-a.pgn')).toBeTrue();
    expect(getBookOffline('book-a.pgn')?.length).toBe(2);
  });

  it('resolves a saved book via its course bookId', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn')], 42);
    const byId = getBookOfflineByBookId(42);
    expect(byId?.length).toBe(1);
    expect(byId![0].id).toBe(1);
  });

  it('returns null for a bookId that was never mapped', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn')]);   // ohne bookId
    expect(getBookOfflineByBookId(42)).toBeNull();
  });

  it('clears the bookId index when the book is removed', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn')], 42);
    removeBookOffline('book-a.pgn');
    expect(getBookOfflineByBookId(42)).toBeNull();
    expect(hasBookOffline('book-a.pgn')).toBeFalse();
  });
});
