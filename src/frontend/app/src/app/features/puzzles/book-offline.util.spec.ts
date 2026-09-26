import { saveBookOffline, getBookOffline, getBookOfflineByBookId, removeBookOffline, hasBookOffline, saveDailyOffline, getDailyOffline, isBookCacheComplete, markBookCacheComplete, getBookOfflineLanguage, getBookOfflineLanguageByBookId } from './book-offline.util';
import { offlineLanguageStale } from '../courses/course-language.util';
import { BookPuzzleDto } from './puzzle.service';

function puzzle(id: number, fileName: string): BookPuzzleDto {
  return { id, lineId: `l${id}`, bookFileName: fileName, round: '', fen: '8/8/8/8/8/8/8/8 w - - 0 1', moves: 'e2e4' } as BookPuzzleDto;
}

describe('book-offline.util', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('merkt sich die Sprache der Kopie (lang + Sprachen der Linien) — Grundlage für „neu herunterladen"', () => {
    const lines = [
      { ...puzzle(1, 'book-a.pgn'), commentLanguages: ['en', 'de'] },
      { ...puzzle(2, 'book-a.pgn'), commentLanguages: ['en', 'de', 'fr'] },
    ];
    saveBookOffline('book-a.pgn', lines, 42, 'DE');
    const meta = getBookOfflineLanguage('book-a.pgn');
    expect(meta).toEqual({ lang: 'de', langs: ['en', 'de', 'fr'] });
    expect(getBookOfflineLanguageByBookId(42)).toEqual(meta);
    // Wahl wie beim Herunterladen → passt; auf das Original gewechselt → Hinweis.
    expect(offlineLanguageStale(meta, 'de', ['en', 'de', 'fr'])).toBeFalse();
    expect(offlineLanguageStale(meta, 'en', ['en', 'de', 'fr'])).toBeTrue();
  });

  it('eine alte Kopie ohne Vermerk gilt als Original; ohne Kopie gibt es keinen Vermerk', () => {
    localStorage.setItem('rookhub_book_offline_' + encodeURIComponent('old.pgn'), JSON.stringify([puzzle(1, 'old.pgn')]));
    expect(getBookOfflineLanguage('old.pgn')).toEqual({ lang: null, langs: [] });
    expect(getBookOfflineLanguage('missing.pgn')).toBeNull();
  });

  it('Entfernen räumt den Sprach-Vermerk mit weg', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn')], 42, 'de');
    removeBookOffline('book-a.pgn');
    expect(getBookOfflineLanguage('book-a.pgn')).toBeNull();
    expect(localStorage.getItem('rookhub_book_lang_' + encodeURIComponent('book-a.pgn'))).toBeNull();
  });

  it('saves + reads a book by file name', () => {
    saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn'), puzzle(2, 'book-a.pgn')]);
    expect(hasBookOffline('book-a.pgn')).toBeTrue();
    expect(getBookOffline('book-a.pgn')?.length).toBe(2);
  });

  it('meldet Erfolg (true) beim Speichern und Fehlschlag (false) bei vollem Speicher', () => {
    // Gleiche Linie wie writeCalcLocal*: ein Quota-Wurf darf nicht als Erfolg durchgehen.
    expect(saveBookOffline('book-a.pgn', [puzzle(1, 'book-a.pgn')], 42)).toBeTrue();
    spyOn(localStorage, 'setItem').and.throwError('QuotaExceededError');
    expect(saveBookOffline('book-b.pgn', [puzzle(2, 'book-b.pgn')], 43)).toBeFalse();
    // Der gescheiterte Eintrag landet auch nicht im bookId-Index.
    expect(getBookOfflineByBookId(43)).toBeNull();
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

  it('Vollständigkeits-Marker: unbekannt ⇒ unvollständig, gesetzt ⇒ vollständig, löschbar', () => {
    // Ohne diesen Marker war ein TORSO (erste Seite geladen, dann Netz weg) nicht von einem
    // komplett gecachten Kurs zu unterscheiden: der anonyme Modus nahm die 300 gecachten Linien als
    // Gesamtzahl und meldete nach 300 Aufgaben „Kurs abgeschlossen" — die restlichen 2700 blieben
    // für diesen Browser dauerhaft unerreichbar, weil nie wieder nachgeladen wurde.
    expect(isBookCacheComplete(4711)).toBeFalse();
    markBookCacheComplete(4711, true);
    expect(isBookCacheComplete(4711)).toBeTrue();
    markBookCacheComplete(4711, false);
    expect(isBookCacheComplete(4711)).toBeFalse();
  });

  it('Vollständigkeits-Marker gilt bei gesperrtem Speicher als NICHT vollständig', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    expect(isBookCacheComplete(4711)).toBeFalse();   // lieber erneut laden als still Linien fehlen
  });

});
