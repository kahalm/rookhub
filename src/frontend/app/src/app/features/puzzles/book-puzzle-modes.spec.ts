import { of, throwError } from 'rxjs';
import { BookPuzzleComponent } from './book-puzzle.component';
import { saveBookOffline, getDailyOffline, saveDailyOffline } from './book-offline.util';
import { CourseLanguageService } from '../courses/course-language.service';
import { formatUtcDate, shiftDailyDate } from './book-nav.util';

/**
 * Charakterisierung der vier Modi des Buch-Lösers (Kurs, Wochenpost, Tagespuzzle, einzelnes Buch-Puzzle):
 * Laden der nächsten Aufgabe, Ergebnis melden, Navigation je Modus. Geschrieben gegen die ungeteilte Komponente
 * VOR der Zerlegung (Z18), damit der Schnitt in Modus-Strategien dasselbe Verhalten nachweist.
 */
const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const P = (id: number, extra: Record<string, unknown> = {}): any => ({ id, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b.pgn', ...extra });

function makeComponent(opts: { loggedIn?: boolean; params?: Record<string, string> } = {}): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const params = opts.params ?? {};
  let paramHandler: ((pm: any) => void) | null = null;
  const pm = { get: (k: string) => params[k] ?? null, has: (k: string) => k in params };
  const route: any = {
    snapshot: { paramMap: pm, queryParamMap: { get: () => null } },
    paramMap: { subscribe: (fn: (pm: any) => void) => { paramHandler = fn; fn(pm); return { unsubscribe() {} }; } },
    emit: () => paramHandler?.(pm),
  };
  const puzzleService: any = {};
  const courseService: any = {
    getTranslations: () => of({ languages: [] }),
    getLink: jasmine.createSpy('getLink').and.returnValue(of({ linkedBookId: null, linkedDisplayName: null })),
    getEbookRefs: jasmine.createSpy('getEbookRefs').and.returnValue(of([])),
    getBookPuzzles: jasmine.createSpy('getBookPuzzles').and.returnValue(of([])),
  };
  const weeklyService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate'), url: '/puzzles/book/1' };
  const translate: any = { instant: (k: string) => k, currentLang: () => null, getFallbackLang: () => null };
  const auth: any = { isLoggedIn: opts.loggedIn ?? true };
  const snackbar: any = { info: jasmine.createSpy('info'), warn: jasmine.createSpy('warn'), success: jasmine.createSpy('success') };
  const offlineQueue: any = { enqueue: jasmine.createSpy('enqueue') };
  const challengeService: any = { resolve: () => of(null) };
  const longSolve: any = { resolve: (s: number) => of(s) };
  const favorites: any = { contains: () => of(false), add: () => of(true), remove: () => of(false), count: () => of(0), list: () => of([]) };
  // Spielweise schon gemerkt → kein Dialog.
  const solveMode: any = {
    ensure: () => of('training'), get: () => 'training', set: () => {}, clear: () => {},
    levelFor: () => 0, modeForLevel: (l: number) => (l > 0 ? 'training' : 'easy'), openDialog: () => of({ mode: 'training' }),
  };
  const worksheets: any = { sendAndNotify: () => {} };
  const c: any = new BookPuzzleComponent(
    puzzleService, stockfish, prefs, route, {} as any, courseService, weeklyService,
    router, translate, auth, snackbar, offlineQueue, challengeService, longSolve, favorites,
    solveMode, worksheets, new CourseLanguageService(translate),
  );
  c.__route = route;
  return c;
}

function offline<T>(fn: () => T): T {
  const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
  try { return fn(); } finally { spy.and.callThrough(); }
}

describe('BookPuzzle Modus Kurs (Charakterisierung Z18)', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  function courseRes(puzzle: any, extra: Record<string, unknown> = {}): any {
    return { solvedCount: 3, total: 10, puzzle, completed: false, book: null, chapter: null, chapterName: null, ...extra };
  }

  it('ngOnInit /courses/:bookId/:mode lädt das nächste Puzzle, Partner-Kurs und E-Book-Verweise', () => {
    const c = makeComponent({ params: { bookId: '7', mode: 'random' } });
    c.courseService.getNext = jasmine.createSpy('getNext').and.returnValue(of(courseRes(P(11))));
    c.courseService.getLink.and.returnValue(of({ linkedBookId: 9, linkedDisplayName: 'Workbook' }));
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.inCourse).toBeTrue();
    expect(c.courseBookId).toBe(7);
    expect(c.courseModeKind).toBe('random');
    expect(c.courseService.getNext).toHaveBeenCalledWith(7, 'random', undefined, undefined, undefined, 'en');
    expect(c.puzzle.id).toBe(11);
    expect(c.courseSolved).toBe(3);
    expect(c.courseTotal).toBe(10);
    expect(c.setupPuzzle).toHaveBeenCalledWith(c.puzzle);
    expect(c.courseService.getEbookRefs).toHaveBeenCalledWith(7);
    expect(c.linkedCourseBookId).toBe(9);
    expect(c.linkedCourseName).toBe('Workbook');
    c.switchToLinkedCourse();
    expect(c.router.navigate).toHaveBeenCalledWith(['/courses', 9, 'random']);
  });

  it('Kapitel-Route: getNext bekommt den Kapitel-Index', () => {
    const c = makeComponent({ params: { bookId: '7', mode: 'sequential', chapterIndex: '2' } });
    c.courseService.getNext = jasmine.createSpy('getNext').and.returnValue(of(courseRes(P(11))));
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.courseChapterIndex).toBe(2);
    expect(c.courseService.getNext).toHaveBeenCalledWith(7, 'sequential', undefined, undefined, 2, 'en');
  });

  it('abgeschlossen ohne vorheriges Puzzle → COURSE_DONE-Panel', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7;
    c.courseService.getNext = () => of(courseRes(null, { completed: true }));
    c.loadCourseNext();
    expect(c.courseCompleted).toBeTrue();
    expect(c.puzzle).toBeNull();
    expect(c.state).toBe('COURSE_DONE');
  });

  it('abgeschlossen nach gelöstem Puzzle → Brett bleibt stehen', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7;
    c.puzzle = P(5); c.state = 'SOLVED';
    c.courseService.getNext = () => of(courseRes(null, { completed: true }));
    c.loadCourseNext(5);
    expect(c.courseCompleted).toBeTrue();
    expect(c.puzzle.id).toBe(5);
    expect(c.state).toBe('SOLVED');
  });

  it('Netzfehler ohne Offline-Kopie → Ladefehler; „Erneut versuchen" lädt wieder', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7;
    c.courseService.getNext = jasmine.createSpy('getNext').and.returnValue(throwError(() => new Error('x')));
    c.loadCourseNext();
    expect(c.loadError).toBeTrue();
    expect(c.state).toBe('LOADING');
    c.retryLoad();
    expect(c.courseService.getNext).toHaveBeenCalledTimes(2);
  });

  it('Netzfehler mit Offline-Kopie → aus dem Cache bedient', () => {
    saveBookOffline('b.pgn', [P(1), P(2)], 7);
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7;
    spyOn(c, 'setupPuzzle');
    c.courseService.getNext = () => throwError(() => new Error('x'));
    c.loadCourseNext();
    expect(c.loadError).toBeFalse();
    expect(c.puzzle.id).toBe(1);
  });

  it('courseNext: sequenziell after=aktuelles, zufällig exclude=aktuelles', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7; c.puzzle = P(4);
    spyOn(c, 'loadCourseNext');
    c.courseNext();
    expect(c.loadCourseNext).toHaveBeenCalledWith(4, undefined);
    c.courseModeKind = 'random';
    c.courseNext();
    expect(c.loadCourseNext).toHaveBeenCalledWith(undefined, 4);
  });

  it('retryFailedCourse: Server-Topf auffüllen, dann weiter; Fehler → Warnung', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7; c.courseCompleted = true;
    spyOn(c, 'loadCourseNext');
    c.courseService.retryFailed = jasmine.createSpy('retryFailed').and.returnValue(of(undefined));
    c.retryFailedCourse();
    expect(c.courseService.retryFailed).toHaveBeenCalledWith(7);
    expect(c.courseCompleted).toBeFalse();
    expect(c.loadCourseNext).toHaveBeenCalledTimes(1);
    c.courseService.retryFailed = () => throwError(() => new Error('x'));
    c.retryFailedCourse();
    expect(c.snackbar.warn).toHaveBeenCalled();
  });

  it('retryFailedCourse anonym: kein Server, einfach weiterladen', () => {
    const c = makeComponent({ loggedIn: false });
    c.inCourse = true; c.courseBookId = 7; c.courseCompleted = true;
    spyOn(c, 'loadCourseNext');
    c.courseService.retryFailed = jasmine.createSpy('retryFailed');
    c.retryFailedCourse();
    expect(c.courseService.retryFailed).not.toHaveBeenCalled();
    expect(c.courseCompleted).toBeFalse();
    expect(c.loadCourseNext).toHaveBeenCalled();
  });

  it('restartCourse angemeldet: Server-Reset, Erfolgsmeldung, neu laden', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7; c.courseCompleted = true;
    spyOn(c, 'loadCourseNext');
    c.courseService.reset = jasmine.createSpy('reset').and.returnValue(of({}));
    c.restartCourse();
    expect(c.courseService.reset).toHaveBeenCalledWith(7);
    expect(c.snackbar.success).toHaveBeenCalled();
    expect(c.courseCompleted).toBeFalse();
    expect(c.loadCourseNext).toHaveBeenCalled();
  });

  it('restartCourse anonym: nur lokaler Fortschritt wird verworfen', () => {
    const c = makeComponent({ loggedIn: false });
    c.inCourse = true; c.courseBookId = 7;
    c.offlineCourseSolvedIds.add(3);
    spyOn(c, 'loadCourseNext');
    c.courseService.reset = jasmine.createSpy('reset');
    c.restartCourse();
    expect(c.courseService.reset).not.toHaveBeenCalled();
    expect(c.offlineCourseSolvedIds.size).toBe(0);
    expect(c.loadCourseNext).toHaveBeenCalled();
  });

  it('recordCourseAttempt online: meldet Ergebnis und übernimmt den Serverstand (einmal je Durchgang)', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7; c.puzzle = P(4); c.solveSeconds = 12;
    c.courseService.recordResult = jasmine.createSpy('recordResult').and.returnValue(of({ solvedCount: 5, total: 9 }));
    c.recordCourseAttempt(true);
    c.recordCourseAttempt(true);
    expect(c.courseService.recordResult).toHaveBeenCalledTimes(1);
    expect(c.courseService.recordResult).toHaveBeenCalledWith(7, 4, true, 'sequential', 12, undefined, 0, 'easy');
    expect(c.courseSolved).toBe(5);
    expect(c.courseTotal).toBe(9);
  });

  it('recordCourseAttempt offline: Warteschlange + lokaler Zähler', () => {
    const c = makeComponent();
    c.inCourse = true; c.courseBookId = 7; c.puzzle = P(4); c.courseSolved = 1; c.courseTotal = 3;
    offline(() => c.recordCourseAttempt(true));
    expect(c.offlineQueue.enqueue).toHaveBeenCalledWith('POST', '/api/courses/7/results', jasmine.objectContaining({ bookPuzzleId: 4, solved: true }));
    expect(c.courseSolved).toBe(2);
    expect(c.offlineCourseSolvedIds.has(4)).toBeTrue();
  });
});

describe('BookPuzzle Modus Wochenpost (Charakterisierung Z18)', () => {
  beforeEach(() => sessionStorage.setItem('rookhub_weekly_mode_5', 'training'));
  afterEach(() => sessionStorage.removeItem('rookhub_weekly_mode_5'));

  it('ngOnInit /weekly/:id: Post laden, Fortschritt holen, beim ersten neuen Puzzle einsteigen', () => {
    const c = makeComponent({ params: { weeklyId: '5' } });
    c.weeklyService.getPlay = jasmine.createSpy('getPlay').and.returnValue(of({ title: 'W', puzzles: [P(0), P(1), P(2)] }));
    c.weeklyService.getProgress = () => of({ playedCount: 1, solvedCount: 1, totalSeconds: 30, trainingCount: 1, easyCount: 0, playedIndices: [0], completed: false });
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.inWeekly).toBeTrue();
    expect(c.weeklyService.getPlay).toHaveBeenCalledWith(5);
    expect(c.weeklyIndex).toBe(1);
    expect(c.puzzle.id).toBe(1);
    expect(c.weeklyPlayed).toBe(1);
    expect(c.weeklySeconds).toBe(30);
    expect(c.setupPuzzle).toHaveBeenCalledWith(c.puzzle);
  });

  it('leerer Post → COURSE_DONE ohne Puzzle', () => {
    const c = makeComponent({ params: { weeklyId: '5' } });
    c.weeklyService.getPlay = () => of({ title: 'W', puzzles: [] });
    c.ngOnInit();
    expect(c.state).toBe('COURSE_DONE');
    expect(c.puzzle).toBeNull();
    expect(c.weeklyCompleted).toBeFalse();
  });

  it('weeklyNext über das Ende → abgeschlossen, letztes Puzzle bleibt', () => {
    const c = makeComponent();
    c.inWeekly = true; c.weeklyId = 5;
    c.weeklyPuzzles = [P(0), P(1)];
    spyOn(c, 'setupPuzzle');
    c.loadWeeklyAt(1);
    expect(c.puzzle.id).toBe(1);
    c.weeklyNext();
    expect(c.weeklyCompleted).toBeTrue();
    expect(c.puzzle.id).toBe(1);
    expect(c.weeklyPercent).toBe(100);
  });

  it('recordWeeklyAttempt online: meldet einmal je Puzzle mit Index und Spielweise', () => {
    const c = makeComponent();
    c.inWeekly = true; c.weeklyId = 5; c.puzzle = P(2); c.solveSeconds = 8;
    c.weeklyService.recordAttempt = jasmine.createSpy('recordAttempt').and.returnValue(of({ playedCount: 3, solvedCount: 2, totalSeconds: 40 }));
    c.recordWeeklyAttempt(true);
    c.recordWeeklyAttempt(true);
    expect(c.weeklyService.recordAttempt).toHaveBeenCalledTimes(1);
    expect(c.weeklyService.recordAttempt).toHaveBeenCalledWith(5, 2, true, 8, 0, 0, 0, 'training');
    expect(c.weeklyPlayed).toBe(3);
    expect(c.weeklySeconds).toBe(40);
  });
});

describe('BookPuzzle Modus Tagespuzzle (Charakterisierung Z18)', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('online: lädt das Puzzle des Datums und legt es offline ab', () => {
    const c = makeComponent({ params: { date: '20260714' } });
    c.puzzleService.getDailyPuzzle = jasmine.createSpy('getDailyPuzzle').and.returnValue(of(P(42)));
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.dailyDate).toBe('20260714');
    expect(c.puzzleService.getDailyPuzzle).toHaveBeenCalledWith('20260714');
    expect(c.puzzle.id).toBe(42);
    expect(getDailyOffline('20260714')?.id).toBe(42);
  });

  it('„today" wird zum heutigen UTC-Datum', () => {
    const c = makeComponent({ params: { date: 'today' } });
    c.puzzleService.getDailyPuzzle = jasmine.createSpy('getDailyPuzzle').and.returnValue(of(P(42)));
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.dailyDate).toBe(formatUtcDate(new Date()));
  });

  it('Fehler mit Offline-Kopie → aus dem Cache; ohne → Ladefehler', () => {
    saveDailyOffline('20260714', P(42));
    const c = makeComponent();
    spyOn(c, 'setupPuzzle');
    c.puzzleService.getDailyPuzzle = () => throwError(() => new Error('x'));
    c.loadDaily('20260714');
    expect(c.puzzle.id).toBe(42);
    expect(c.loadError).toBeFalse();
    c.loadDaily('20260715');
    expect(c.puzzle).toBeNull();
    expect(c.loadError).toBeTrue();
  });

  it('offline: direkt aus dem Cache, ohne Netz', () => {
    saveDailyOffline('20260714', P(42));
    const c = makeComponent();
    spyOn(c, 'setupPuzzle');
    c.puzzleService.getDailyPuzzle = jasmine.createSpy('getDailyPuzzle');
    offline(() => c.loadDaily('20260714'));
    expect(c.puzzleService.getDailyPuzzle).not.toHaveBeenCalled();
    expect(c.puzzle.id).toBe(42);
  });

  it('Datums-Navigation: zurück immer, vor nur bis heute', () => {
    const c = makeComponent();
    c.dailyDate = '20260714';
    c.prevDaily();
    expect(c.router.navigate).toHaveBeenCalledWith(['/puzzles/daily', shiftDailyDate('20260714', -1)]);
    c.nextDaily();
    expect(c.router.navigate).toHaveBeenCalledWith(['/puzzles/daily', shiftDailyDate('20260714', 1)]);
    c.router.navigate.calls.reset();
    c.dailyDate = formatUtcDate(new Date());
    expect(c.canGoForwardDaily).toBeFalse();
    c.nextDaily();
    expect(c.router.navigate).not.toHaveBeenCalled();
  });
});

describe('BookPuzzle Modus einzelnes Buch-Puzzle (Charakterisierung Z18)', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('ngOnInit /puzzles/book/:id lädt das Puzzle', () => {
    const c = makeComponent({ params: { id: '42' } });
    c.puzzleService.getBookPuzzleById = jasmine.createSpy('getBookPuzzleById').and.returnValue(of(P(42)));
    spyOn(c, 'setupPuzzle');
    c.ngOnInit();
    expect(c.puzzleService.getBookPuzzleById).toHaveBeenCalledWith(42, 'en');
    expect(c.puzzle.id).toBe(42);
    expect(c.browseInBook).toBeTrue();
  });

  it('nächstes/zufälliges im Buch online: Server fragen, URL nachziehen', () => {
    const c = makeComponent();
    c.puzzle = P(42);
    spyOn(c, 'setupPuzzle');
    c.puzzleService.getNextBookPuzzle = jasmine.createSpy('next').and.returnValue(of(P(43)));
    c.puzzleService.getRandomBookPuzzle = jasmine.createSpy('random').and.returnValue(of(P(50)));
    c.nextInBook();
    expect(c.puzzleService.getNextBookPuzzle).toHaveBeenCalledWith(42, 'en');
    expect(c.router.navigate).toHaveBeenCalledWith(['/puzzles/book', 43]);
    expect(c.puzzle.id).toBe(43);
    expect(c.bookNavLoading).toBeFalse();
    c.randomInBook();
    expect(c.puzzleService.getRandomBookPuzzle).toHaveBeenCalledWith(43, 'en');
    expect(c.puzzle.id).toBe(50);
  });

  it('Fehler beim Blättern gibt den Knopf wieder frei', () => {
    const c = makeComponent();
    c.puzzle = P(42);
    c.puzzleService.getNextBookPuzzle = () => throwError(() => new Error('x'));
    c.nextInBook();
    expect(c.bookNavLoading).toBeFalse();
    expect(c.puzzle.id).toBe(42);
  });

  it('offline: nächstes aus dem gespeicherten Buch (mit Umlauf am Ende)', () => {
    saveBookOffline('b.pgn', [P(1), P(2), P(3)]);
    const c = makeComponent();
    c.puzzle = P(3);
    spyOn(c, 'setupPuzzle');
    offline(() => {
      c.nextInBook();
      expect(c.puzzle.id).toBe(1);
      c.randomInBook();
      expect(c.puzzle.id).not.toBe(1);
    });
  });

  it('offline ohne gespeichertes Buch: Hinweis, Puzzle bleibt', () => {
    const c = makeComponent();
    c.puzzle = P(3, { bookFileName: 'nicht-da.pgn' });
    offline(() => c.nextInBook());
    expect(c.snackbar.info).toHaveBeenCalled();
    expect(c.puzzle.id).toBe(3);
  });
});

describe('BookPuzzle Weiter-Ziel je Modus (Charakterisierung Z18)', () => {
  function spied(): any {
    const c = makeComponent();
    spyOn(c, 'courseNext');
    spyOn(c, 'weeklyNext');
    spyOn(c, 'nextInBook');
    return c;
  }
  const calls = (c: any) => [c.courseNext.calls.count(), c.weeklyNext.calls.count(), c.nextInBook.calls.count()];

  it('failedNext: Kurs → courseNext, Wochenpost → weeklyNext, Buch → nextInBook, Tagespuzzle → nichts', () => {
    let c = spied(); c.inCourse = true; c.failedNext(); expect(calls(c)).toEqual([1, 0, 0]);
    c = spied(); c.inWeekly = true; c.failedNext(); expect(calls(c)).toEqual([0, 1, 0]);
    c = spied(); c.failedNext(); expect(calls(c)).toEqual([0, 0, 1]);
    c = spied(); c.dailyDate = '20260714'; c.failedNext(); expect(calls(c)).toEqual([0, 0, 0]);
  });

  it('solvedAutoNextPublic: wie failedNext, geteiltes Einzel-Puzzle bleibt stehen', () => {
    let c = spied(); c.inCourse = true; c.solvedAutoNextPublic(); expect(calls(c)).toEqual([1, 0, 0]);
    c = spied(); c.inWeekly = true; c.solvedAutoNextPublic(); expect(calls(c)).toEqual([0, 1, 0]);
    c = spied(); c.solvedAutoNextPublic(); expect(calls(c)).toEqual([0, 0, 1]);
    c = spied(); c.singlePuzzle = true; c.solvedAutoNextPublic(); expect(calls(c)).toEqual([0, 0, 0]);
    c = spied(); c.dailyDate = '20260714'; c.solvedAutoNextPublic(); expect(calls(c)).toEqual([0, 0, 0]);
  });

  it('Enter nach dem Lösen: Wochenpost/Kurs/Buch-Blättern; Tagespuzzle tut nichts', () => {
    const press = (c: any) => {
      const e = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
      Object.defineProperty(e, 'target', { value: document.body });
      c.onKeyDown(e);
      return e.defaultPrevented;
    };
    let c = spied(); c.state = 'SOLVED'; c.inWeekly = true; expect(press(c)).toBeTrue(); expect(calls(c)).toEqual([0, 1, 0]);
    c = spied(); c.state = 'SOLVED'; c.inCourse = true; expect(press(c)).toBeTrue(); expect(calls(c)).toEqual([1, 0, 0]);
    c = spied(); c.state = 'SOLVED'; expect(press(c)).toBeTrue(); expect(calls(c)).toEqual([0, 0, 1]);
    c = spied(); c.state = 'SOLVED'; c.bookNavLoading = true; expect(press(c)).toBeTrue(); expect(calls(c)).toEqual([0, 0, 0]);
    c = spied(); c.state = 'SOLVED'; c.dailyDate = '20260714'; expect(press(c)).toBeFalse(); expect(calls(c)).toEqual([0, 0, 0]);
  });

});
