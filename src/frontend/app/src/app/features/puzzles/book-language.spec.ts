import { of } from 'rxjs';
import { BookLanguageController, BookLanguageHost } from './book-language';
import { CourseLanguageService } from '../courses/course-language.service';

describe('BookLanguageController (Zerlegung Z18)', () => {
  function host(over: Record<string, unknown> = {}): BookLanguageHost & { redraws: number } {
    const translate: any = { instant: (k: string) => k, currentLang: () => null, getFallbackLang: () => null };
    const h: any = {
      puzzle: { id: 5, fen: 'x', moves: '', bookFileName: 'b.pgn', comment: 'alt', commentLanguages: ['en', 'de'] },
      inCourse: false, inWeekly: false, isDaily: false, standalone: true,
      course: { bookId: null, isAnon: false, refreshAnonCache: jasmine.createSpy('refreshAnonCache'), autoCache: jasmine.createSpy('autoCache') },
      courseLang: new CourseLanguageService(translate),
      puzzleService: { getBookPuzzleById: jasmine.createSpy('get').and.returnValue(of({ id: 5, comment: 'neu', titleLabel: 'T' })) },
      redraws: 0, redrawReview() { h.redraws++; },
      ...over,
    };
    return h;
  }

  it('Sprachwahl nur im Kurs und beim Buch-Puzzle, nicht im Tagespuzzle/Wochenpost', () => {
    expect(new BookLanguageController(host()).enabled).toBeTrue();
    expect(new BookLanguageController(host({ isDaily: true })).enabled).toBeFalse();
    expect(new BookLanguageController(host({ inWeekly: true, standalone: false })).enabled).toBeFalse();
    expect(new BookLanguageController(host({ isDaily: true })).requestLang).toBeNull();
    expect(new BookLanguageController(host()).ref).toEqual({ bookId: null, fileName: 'b.pgn' });
  });

  it('Sprache wählen tauscht nur die Texte der laufenden Linie und zieht im Kurs die Offline-Kopie nach', () => {
    const h = host({ inCourse: true, standalone: false });
    (h.course as any).bookId = 7;
    const l = new BookLanguageController(h);
    l.pick('de');
    expect(h.puzzleService.getBookPuzzleById).toHaveBeenCalledWith(5, 'de');
    expect(h.puzzle!.comment).toBe('neu');
    expect(h.puzzle!.titleLabel).toBe('T');
    expect(h.redraws).toBe(1);
    expect(h.course.autoCache).toHaveBeenCalled();
    expect(h.course.refreshAnonCache).not.toHaveBeenCalled();
  });
});
