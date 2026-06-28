import { saveBookOffline, getBookOffline, getBookOfflineByBookId, removeBookOffline, hasBookOffline, saveDailyOffline, getDailyOffline } from './book-offline.util';
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

  it('caches and reads a daily puzzle by date', () => {
    saveDailyOffline('20260628', puzzle(7, 'daily.pgn'));
    expect(getDailyOffline('20260628')?.id).toBe(7);
    expect(getDailyOffline('20260627')).toBeNull();
  });

  it('keeps only the most recent 14 daily puzzles', () => {
    // 16 aufeinanderfolgende Tage cachen → die 2 ältesten fallen raus.
    for (let d = 1; d <= 16; d++) {
      saveDailyOffline(`202606${String(d).padStart(2, '0')}`, puzzle(d, 'daily.pgn'));
    }
    expect(getDailyOffline('20260601')).toBeNull();   // ältester verdrängt
    expect(getDailyOffline('20260602')).toBeNull();
    expect(getDailyOffline('20260603')?.id).toBe(3);   // 14 jüngste bleiben
    expect(getDailyOffline('20260616')?.id).toBe(16);
  });
});
