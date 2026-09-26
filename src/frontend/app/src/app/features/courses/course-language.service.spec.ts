import { of, throwError } from 'rxjs';
import { COURSE_LANG_KEY, CourseLanguageService } from './course-language.service';

function make(ui: string | null = 'de', courses?: unknown): CourseLanguageService {
  return new CourseLanguageService({ currentLang: () => ui, getFallbackLang: () => 'en' } as never, courses as never);
}

describe('CourseLanguageService', () => {
  beforeEach(() => localStorage.removeItem(COURSE_LANG_KEY));
  afterEach(() => localStorage.removeItem(COURSE_LANG_KEY));

  it('Vorgabe ohne Wahl: die Oberflächensprache geht als lang raus', () => {
    const svc = make('de');
    expect(svc.choice({ bookId: 5 })).toBeNull();
    expect(svc.requestLang({ bookId: 5 })).toBe('de');
    expect(make(null).requestLang({ bookId: 5 })).toBe('en');   // ohne Oberflächensprache: Rückfall
  });

  it('Vorgabe = Oberflächensprache, wenn der Kurs sie hat, sonst das Original', () => {
    const svc = make('de');
    svc.noteLanguages({ bookId: 5 }, ['en', 'de']);
    expect(svc.effective({ bookId: 5 })).toBe('de');
    svc.noteLanguages({ bookId: 6 }, ['en', 'fr']);
    expect(svc.effective({ bookId: 6 })).toBe('en');   // Original
    expect(svc.source({ bookId: 6 })).toBe('en');
  });

  it('merkt die Wahl je Kurs auf dem Gerät', () => {
    const svc = make('de');
    svc.setChoice({ bookId: 5 }, 'EN');
    expect(svc.requestLang({ bookId: 5 })).toBe('en');
    expect(svc.requestLang({ bookId: 6 })).toBe('de');   // anderer Kurs unberührt
    // Neu gebaut (= neuer Seitenaufruf) liest sie aus dem localStorage.
    expect(make('de').requestLang({ bookId: 5 })).toBe('en');
  });

  it('Einzel-Puzzle ohne Kurs-Id: Wahl über den Dateinamen, wandert zur Kurs-Id', () => {
    const svc = make('de');
    svc.setChoice({ fileName: 'b.pgn' }, 'fr');
    svc.noteLanguages({ fileName: 'b.pgn' }, ['en', 'fr']);
    expect(svc.requestLang({ fileName: 'b.pgn' })).toBe('fr');
    svc.rememberFile(9, 'b.pgn');
    expect(svc.requestLang({ bookId: 9 })).toBe('fr');
    expect(svc.languages({ bookId: 9 })).toEqual(['en', 'fr']);
    // Danach findet auch der Dateiname die Wahl über die Kurs-Id.
    expect(svc.requestLang({ fileName: 'b.pgn' })).toBe('fr');
    const stored = JSON.parse(localStorage.getItem(COURSE_LANG_KEY) || '{}');
    expect(stored.c).toEqual({ b9: 'fr' });
    expect(stored.f).toEqual({ 'b.pgn': 9 });
  });

  it('eine schon getroffene Kurs-Wahl gewinnt gegen die am Dateinamen', () => {
    const svc = make('de');
    svc.setChoice({ bookId: 9 }, 'de');
    svc.setChoice({ fileName: 'b.pgn' }, 'fr');
    svc.rememberFile(9, 'b.pgn');
    expect(svc.requestLang({ bookId: 9 })).toBe('de');
  });

  it('übersteht einen gesperrten Speicher (Wahl gilt dann nur in der Sitzung)', () => {
    spyOn(localStorage, 'setItem').and.throwError('QuotaExceededError');
    const svc = make('de');
    expect(() => svc.setChoice({ bookId: 5 }, 'en')).not.toThrow();
    expect(svc.requestLang({ bookId: 5 })).toBe('en');
  });

  it('holt die Übersicht je Kurs höchstens einmal und merkt ihre Sprachen', () => {
    const load = jasmine.createSpy('load').and.returnValue(of({
      sourceLanguage: 'en', languages: [{ language: 'de', linesTranslated: 12, linesTotal: 40 }],
    }));
    const svc = make('de');
    svc.ensureLanguages(5, load);
    svc.ensureLanguages(5, load);
    expect(load).toHaveBeenCalledTimes(1);
    expect(svc.languages({ bookId: 5 })).toEqual(['en', 'de']);
  });

  it('nimmt ohne eigenen Abruf den Kurs-Dienst, und Fehler bleiben still', () => {
    const courses = { getTranslations: jasmine.createSpy('getTranslations').and.returnValue(throwError(() => new Error('404'))) };
    const svc = make('de', courses);
    expect(() => svc.ensureLanguages(7)).not.toThrow();
    expect(courses.getTranslations).toHaveBeenCalledWith(7);
    expect(svc.languages({ bookId: 7 })).toEqual([]);
    // Ein Abruf, der schon beim Aufbauen wirft (Attrappe ohne Methode), reißt nichts mit.
    expect(() => make('de').ensureLanguages(8, () => { throw new Error('boom'); })).not.toThrow();
  });
});
