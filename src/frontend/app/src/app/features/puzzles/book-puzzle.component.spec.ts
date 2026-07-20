import { of } from 'rxjs';
import { BookPuzzleComponent } from './book-puzzle.component';
import { saveBookOffline } from './book-offline.util';
import { saveDailyElapsed, loadDailyElapsed } from './daily-elapsed.util';
import { saveSolveElapsed, loadSolveElapsed } from './solve-elapsed.util';

/**
 * Fokussierter Test der Lade-Epoche (loadEpoch) ohne TestBed/Template: eine veraltete,
 * langsamer auflösende Puzzle-Antwort darf das inzwischen geladene Puzzle nicht überschreiben.
 */
function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const puzzleService: any = {};
  const route: any = { snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } }, paramMap: { subscribe: () => ({ unsubscribe() {} }) } };
  const dialog: any = {};
  const courseService: any = {};
  const weeklyService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate'), url: '/puzzles/book/1' };
  // gibt den Key zurück; Params werden angehängt (damit Tipp-Stufe 2/3 mit {piece}/{move} prüfbar sind).
  const translate: any = { instant: (k: string, p?: object) => (p ? k + ' ' + JSON.stringify(p) : k), currentLang: () => null, getFallbackLang: () => null };
  const auth: any = { isLoggedIn: false };
  const snackbar: any = { info: () => {} };
  const offlineQueue: any = { enqueue: () => {} };
  const challengeService: any = { resolve: () => ({ subscribe: () => {} }) };
  // Default: keine Kappung — gibt die gemessene Zeit unverändert zurück (Tests überschreiben bei Bedarf).
  const longSolve: any = { resolve: (s: number) => of(s) };
  const favorites: any = { contains: () => of(false), add: () => of(true), remove: () => of(false), count: () => of(0), list: () => of([]) };
  return new BookPuzzleComponent(
    puzzleService, stockfish, prefs, route, dialog, courseService, weeklyService,
    router, translate, auth, snackbar, offlineQueue, challengeService, longSolve, favorites
  );
}

const FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('BookPuzzleComponent load race (loadEpoch)', () => {
  it('a stale getBookPuzzleById response does not overwrite a newer one', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupPuzzle');

    const emits: Array<(v: any) => void> = [];
    c.puzzleService.getBookPuzzleById = () => ({
      subscribe: (h: any) => { emits.push((v: any) => (typeof h === 'function' ? h : h.next)(v)); return { unsubscribe() {} }; }
    });

    (c as any).loadPuzzle(1);   // Epoch 1 → emits[0]
    (c as any).loadPuzzle(2);   // Epoch 2 → emits[1]

    emits[1]({ id: 222, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' });   // neuere zuerst
    expect(c.puzzle.id).toBe(222);
    emits[0]({ id: 111, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' });   // ältere danach
    expect(c.puzzle.id).toBe(222);   // bleibt das neuere
  });
});

describe('BookPuzzleComponent Fehlversuch-Erfassung (Daily, v0.309.0)', () => {
  function makeDaily(): any {
    const c: any = makeComponent();
    c.puzzle = { id: 42, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    c.dailyDate = '20260714';           // isDaily = true; standalone-Getter: kein Kurs/Weekly-Kontext
    return c;
  }

  it('Solve nach vorherigem Fail wird trotzdem gemeldet (Spätlöser)', () => {
    const c = makeDaily();
    const calls: boolean[] = [];
    c.puzzleService.recordBookAttempt = (_id: number, solved: boolean) => { calls.push(solved); return { subscribe: () => {} }; };
    c.auth = { isLoggedIn: true };
    (c as any).recordBookAttempt(false);   // Fehlzug
    (c as any).recordBookAttempt(true);    // späterer Solve — darf nicht mehr blockiert sein
    (c as any).recordBookAttempt(true);    // zweiter Solve: einmalig
    expect(calls).toEqual([false, true]);
  });

  it('jeder Fehlversuch wird einzeln gemeldet, Reset nach FAILED aber nicht doppelt', () => {
    const c = makeDaily();
    const spy = spyOn(c as any, 'recordBookAttempt');
    // Reset mitten im Lauf (Zug gespielt, nicht FAILED) → Fehlversuch
    c.moveLog = [{ ok: true }];
    c.state = 'PLAYING';
    spyOn(c as any, 'setupPuzzle');
    (c as any).resetPuzzle();
    expect(spy).toHaveBeenCalledWith(false);
    spy.calls.reset();
    // Reset NACH FAILED (Fehlzug schon erfasst) → KEINE Doppelmeldung
    c.state = 'FAILED';
    (c as any).resetPuzzle();
    expect(spy).not.toHaveBeenCalled();
  });

  it('Off-Path-Mouseslip meldet Fehlversuch, aus FAILED heraus nicht', () => {
    const c = makeDaily();
    const spy = spyOn(c as any, 'recordBookAttempt');
    spyOn(Object.getPrototypeOf(Object.getPrototypeOf(c)), 'mouseslip');
    (c as any).onSolutionPath = false;
    c.state = 'PLAYING';
    c.mouseslip();
    expect(spy).toHaveBeenCalledWith(false);
    spy.calls.reset();
    c.state = 'FAILED';
    c.mouseslip();
    expect(spy).not.toHaveBeenCalled();
  });
});

describe('BookPuzzleComponent letztes Puzzle (analysieren/teilen)', () => {
  it('handleSolved merkt das gelöste Puzzle als lastSolved', () => {
    const c = makeComponent();
    spyOn(c as any, 'enterSolutionReview');
    spyOn(c as any, 'startSolvedCountdown');
    spyOn(c as any, 'updateBoard');
    spyOn(c as any, 'stopTimer');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    c.puzzle = { id: 42, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (c as any).orientation = 'black';

    (c as any).handleSolved();

    expect(c.lastSolvedPuzzleId).toBe(42);
    expect((c as any).lastSolvedFen).toBe(FEN);
    expect((c as any).lastSolvedMoves).toBe('e2e4 e7e5');
    expect((c as any).lastSolvedOrientation).toBe('black');
  });

  it('reviewLastPuzzle öffnet die Analyse mit dem zuletzt gelösten Puzzle', () => {
    const c = makeComponent();
    spyOn(c as any, 'stopCountdown');
    (c as any).lastSolvedFen = FEN;
    (c as any).lastSolvedMoves = 'e2e4 e7e5';
    (c as any).lastSolvedOrientation = 'white';

    c.reviewLastPuzzle();

    const [path, extras] = c.router.navigate.calls.mostRecent().args;
    expect(path).toEqual(['/analysis']);
    expect(extras.queryParams.fen).toBe(FEN);
    expect(extras.queryParams.moves).toBe('e2e4,e7e5');
    expect(extras.queryParams.orientation).toBe('white');
  });

  it('reviewLastPuzzle ohne gelöstes Puzzle navigiert nicht', () => {
    const c = makeComponent();
    spyOn(c as any, 'stopCountdown');
    c.reviewLastPuzzle();
    expect(c.router.navigate).not.toHaveBeenCalled();
  });

  it('sharePuzzle reicht das vorherige (zuletzt gelöste) Puzzle an den Dialog', () => {
    const c = makeComponent();
    (c as any).dialog = { open: jasmine.createSpy('open') };
    c.puzzle = { id: 200, fen: FEN, moves: 'e2e4', bookFileName: 'b' };
    c.lastSolvedPuzzleId = 100;

    c.sharePuzzle();

    const data = (c as any).dialog.open.calls.mostRecent().args[1].data;
    // Teilen-Link markiert das Einzel-Puzzle als „single" → Empfänger bleibt am Ende stehen.
    expect(data.url.endsWith('/puzzles/book/200?single=1')).toBeTrue();
    expect(data.previousUrl.endsWith('/puzzles/book/100?single=1')).toBeTrue();
    expect(data.previousPuzzleId).toBe(100);
  });

  it('sharePuzzle ohne abweichendes Vorgänger-Puzzle setzt kein previousUrl', () => {
    const c = makeComponent();
    (c as any).dialog = { open: jasmine.createSpy('open') };
    c.puzzle = { id: 200, fen: FEN, moves: 'e2e4', bookFileName: 'b' };
    c.lastSolvedPuzzleId = 200;   // = aktuelles Puzzle (z.B. Tagespuzzle, kein Auto-Advance)

    c.sharePuzzle();

    const data = (c as any).dialog.open.calls.mostRecent().args[1].data;
    expect(data.previousUrl).toBeUndefined();
    expect(data.previousPuzzleId).toBeUndefined();
  });
});

describe('BookPuzzleComponent direkt geteiltes Einzel-Puzzle (single)', () => {
  it('singlePuzzle: kein Auto-Advance-Countdown nach dem Lösen', () => {
    const c = makeComponent();
    spyOn(c as any, 'enterSolutionReview');
    spyOn(c as any, 'updateBoard');
    spyOn(c as any, 'stopTimer');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    const countdown = spyOn(c as any, 'startSolvedCountdown');
    c.singlePuzzle = true;
    c.puzzle = { id: 7, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };

    (c as any).handleSolved();   // ruft finalizeSolve (longSolve synchron)

    expect(countdown).not.toHaveBeenCalled();
  });

  it('singlePuzzle: solvedAutoNext springt nicht ins Buch', () => {
    const c = makeComponent();
    const next = spyOn(c as any, 'nextInBook');
    c.singlePuzzle = true;
    c.puzzle = { id: 7, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };

    (c as any).solvedAutoNext();

    expect(next).not.toHaveBeenCalled();
  });

  it('browseInBook ist false für ein single-Puzzle, sonst true im Standalone', () => {
    const c = makeComponent();
    c.singlePuzzle = true;
    expect(c.browseInBook).toBeFalse();
    c.singlePuzzle = false;
    expect(c.browseInBook).toBeTrue();   // standalone, kein Daily/Kurs/Weekly
  });
});

describe('BookPuzzleComponent track solves', () => {
  it('recordTrack meldet einmalig und übernimmt die Zähler', () => {
    const c = makeComponent();
    const spy = jasmine.createSpy('track').and.returnValue(of({ solved: 3, failed: 2 }));
    c.puzzleService.trackSharedAttempt = spy;
    c.trackSolves = true;
    c.puzzle = { id: 9, fen: FEN, moves: 'e2e4', bookFileName: 'b' };

    (c as any).recordTrack(true);
    (c as any).recordTrack(false);   // zweiter Aufruf wird vom Guard verschluckt

    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith(9, true, 0);   // 3. Arg = genutzte Tipp-Stufe (seit v0.202.0)
    expect(c.sharedCounts).toEqual({ solved: 3, failed: 2 });
  });

  it('recordTrack tut nichts ohne trackSolves', () => {
    const c = makeComponent();
    const spy = jasmine.createSpy('track').and.returnValue(of({ solved: 0, failed: 0 }));
    c.puzzleService.trackSharedAttempt = spy;
    c.trackSolves = false;
    c.puzzle = { id: 9, fen: FEN, moves: 'e2e4', bookFileName: 'b' };

    (c as any).recordTrack(false);

    expect(spy).not.toHaveBeenCalled();
  });

  it('resetPuzzle meldet einen failed-Track (Reset zählt als failed)', () => {
    const c = makeComponent();
    const spy = jasmine.createSpy('track').and.returnValue(of({ solved: 0, failed: 1 }));
    c.puzzleService.trackSharedAttempt = spy;
    spyOn(c as any, 'setupPuzzle');
    c.trackSolves = true;
    c.puzzle = { id: 9, fen: FEN, moves: 'e2e4', bookFileName: 'b' };

    c.resetPuzzle();

    expect(spy).toHaveBeenCalledWith(9, false, 0);   // 3. Arg = genutzte Tipp-Stufe (seit v0.202.0)
  });
});

describe('BookPuzzleComponent lange Lösezeit-Nachfrage', () => {
  function solvedComp(elapsed: number, resolvedSeconds: number) {
    const c = makeComponent();
    spyOn(c as any, 'enterSolutionReview');
    spyOn(c as any, 'updateBoard');
    spyOn(c as any, 'stopTimer');
    spyOn(c as any, 'startSolvedCountdown');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    c.puzzle = { id: 7, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    c.elapsedSeconds = elapsed;
    // LongSolveService kapselt Schwellwert/Nachfrage — hier nur das Ergebnis simulieren.
    const resolve = jasmine.createSpy('resolve').and.returnValue(of(resolvedSeconds));
    (c as any).longSolve = { resolve };
    return { c, resolve };
  }

  it('wertet die vom LongSolveService gelieferte (ggf. gekappte) Zeit + zeichnet auf', () => {
    const { c, resolve } = solvedComp(900, 300);   // „war weg" → Service kappt auf 300
    (c as any).handleSolved(false);
    expect(resolve).toHaveBeenCalledWith(900);
    expect((c as any).solveSeconds).toBe(300);
    expect((c as any).recordCourseAttempt).toHaveBeenCalled();
    expect((c as any).startSolvedCountdown).toHaveBeenCalled();
  });

  it('übernimmt die volle Zeit, wenn der Service sie unverändert zurückgibt', () => {
    const { c } = solvedComp(120, 120);
    (c as any).handleSolved(false);
    expect((c as any).solveSeconds).toBe(120);
  });
});

describe('BookPuzzleComponent Tipps (gestuft 1→3)', () => {
  const HINTS = { de: ['Motiv', 'Figur', 'Erster Zug'], en: ['Motif', 'Piece', 'First move'] };

  it('hat keine Tipps, wenn das Puzzle keine trägt', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b' };
    expect(c.hasHints).toBeFalse();
    expect(c.availableHints.length).toBe(0);
  });

  it('wählt die aktive Sprache (Fallback en, da Mock kein currentLang hat)', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', hints: HINTS };
    (c as any).startPly = -1;   // FEN ist Trainingsstellung → moveIndex 0 = erster Löserzug (LLM-Tipps greifen)
    expect(c.hasHints).toBeTrue();
    expect(c.availableHints).toEqual(HINTS.en);
  });

  it('fällt auf de zurück, wenn nur de vorhanden ist', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', hints: { de: HINTS.de } };
    (c as any).startPly = -1;
    expect(c.availableHints).toEqual(HINTS.de);
  });

  it('deckt mit jedem Tipp eine Stufe mehr auf und stoppt bei 3', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', hints: HINTS };
    (c as any).startPly = -1;
    expect(c.hintLevel).toBe(0);
    expect(c.shownHints).toEqual([]);

    c.showNextHint();
    expect(c.hintLevel).toBe(1);
    expect(c.shownHints).toEqual(['Motif']);

    c.showNextHint(); c.showNextHint();
    expect(c.hintLevel).toBe(3);
    expect(c.shownHints).toEqual(HINTS.en);
    expect(c.canShowMoreHints).toBeFalse();

    c.showNextHint();           // über das Maximum hinaus → bleibt 3
    expect(c.hintLevel).toBe(3);
  });
});

describe('BookPuzzleComponent on-the-fly Tipps (Fallback ohne vorberechnete, z. B. Wochenpost)', () => {
  // Solver-Zustand direkt setzen (Basis-`chess` ist die Grundstellung = FEN); der erste erwartete
  // Löserzug ist solutionMoves[moveIndex]. FEN ist die Trainingsstellung → startPly -1, moveIndex 0.
  function atMove(c: any, moves: string[], moveIndex: number): void {
    (c as any).startPly = -1;
    (c as any).solutionMoves = moves;
    (c as any).moveIndex = moveIndex;
  }

  it('klassifiziert den aktuell erwarteten Löserzug, wenn keine vorberechneten Tipps da sind', () => {
    const c = makeComponent();
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    c.puzzle = puzzle;
    atMove(c, ['e2e4', 'e7e5'], 0);   // erster Löserzug e2e4 (ruhiger Bauernzug)

    expect(c.hasPrecomputedHints).toBeFalse();
    expect(c.hasHints).toBeTrue();                    // on-the-fly Fallback greift
    expect(c.availableHints.length).toBe(3);
    expect(c.availableHints[0]).toBe('puzzles.hints.t1Quiet');   // translate-Mock gibt den Key zurück
  });

  it('liefert on-the-fly Tipps auch für einen SPÄTEREN Zug (nicht nur den ersten)', () => {
    const c = makeComponent();
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5 g1f3 b8c6', bookFileName: 'b' };
    c.puzzle = puzzle;
    atMove(c, ['e2e4', 'e7e5', 'g1f3', 'b8c6'], 2);   // dritter Zug g1f3 (Springer, ruhig)

    expect(c.availableHints.length).toBe(3);
    expect(c.availableHints[0]).toBe('puzzles.hints.t1Quiet');
    // Figur-Tipp (Stufe 2) nennt den Springer, nicht den Bauern vom ersten Zug
    expect(c.availableHints[1]).toContain('knight');
  });

  it('vorberechnete Tipps haben Vorrang vor dem on-the-fly Fallback — aber nur am ersten Löserzug', () => {
    const c = makeComponent();
    const HINTS = { en: ['Motif', 'Piece', 'Move'] };
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b', hints: HINTS };
    c.puzzle = puzzle;
    atMove(c, ['e2e4', 'e7e5'], 0);

    expect(c.hasPrecomputedHints).toBeTrue();
    expect(c.availableHints).toEqual(HINTS.en);

    // an einem späteren Zug greifen die (nur für den Schlüsselzug erzeugten) LLM-Tipps NICHT mehr
    (c as any).moveIndex = 1;
    expect(c.availableHints).not.toEqual(HINTS.en);
  });
});

describe('BookPuzzleComponent alternative Lösung (kein Auto-Advance)', () => {
  function solvedComponent() {
    const c = makeComponent();
    spyOn(c as any, 'enterSolutionReview');
    spyOn(c as any, 'updateBoard');
    spyOn(c as any, 'stopTimer');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    spyOn(c as any, 'startSolvedCountdown');
    c.puzzle = { id: 7, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    return c;
  }

  it('startet bei normaler Lösung den Auto-Advance-Countdown', () => {
    const c = solvedComponent();
    (c as any).handleSolved(false);
    expect((c as any).startSolvedCountdown).toHaveBeenCalled();
  });

  it('springt bei alternativer Lösung NICHT automatisch weiter', () => {
    const c = solvedComponent();
    (c as any).handleSolved(true);
    expect((c as any).startSolvedCountdown).not.toHaveBeenCalled();
    expect(c.state).toBe('SOLVED');
  });
});

describe('BookPuzzleComponent canFlagHints (Flag-Button-Sichtbarkeit)', () => {
  it('flaggbar im Buchmodus bei on-the-fly Tipps (echte BookPuzzle-Id, kein vorberechneter Tipp)', () => {
    const c = makeComponent();
    c.auth.isLoggedIn = true;
    spyOn(c as any, 'setupSolver');
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;
    // setupSolver ist gespiegelt → Solver-Zustand für die on-the-fly Tipps von Hand setzen
    (c as any).solutionMoves = ['e2e4', 'e7e5'];
    (c as any).moveIndex = 0;
    (c as any).startPly = -1;
    c.hintLevel = 1;                       // ein Tipp aufgedeckt

    expect(c.hasPrecomputedHints).toBeFalse();
    expect(c.canFlagHints).toBeTrue();
  });

  it('nicht flaggbar, solange kein Tipp aufgedeckt wurde', () => {
    const c = makeComponent();
    c.auth.isLoggedIn = true;
    spyOn(c as any, 'setupSolver');
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;
    c.hintLevel = 0;

    expect(c.canFlagHints).toBeFalse();
  });

  it('NICHT flaggbar im Wochenpost-Modus (puzzle.id = Index, keine echte BookPuzzle-Id)', () => {
    const c = makeComponent();
    c.auth.isLoggedIn = true;
    c.inWeekly = true;
    spyOn(c as any, 'setupSolver');
    const puzzle = { id: 0, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;
    (c as any).solutionMoves = ['e2e4', 'e7e5'];
    (c as any).moveIndex = 0;
    (c as any).startPly = -1;
    c.hintLevel = 1;

    expect(c.hasHints).toBeTrue();
    expect(c.canFlagHints).toBeFalse();
  });

  it('nicht flaggbar, wenn nicht eingeloggt', () => {
    const c = makeComponent();
    c.auth.isLoggedIn = false;
    spyOn(c as any, 'setupSolver');
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;
    c.hintLevel = 1;

    expect(c.canFlagHints).toBeFalse();
  });
});

describe('BookPuzzleComponent „dumme Tipps" markieren (Admin)', () => {
  it('toggleHintsFlag setzt das Flag und ruft den Service mit true', () => {
    const c = makeComponent();
    c.auth.isAdmin = true;
    c.snackbar.success = jasmine.createSpy('success');
    const spy = jasmine.createSpy('flag').and.returnValue(of({ id: 5, hintsFlagged: true }));
    c.puzzleService.flagBookPuzzleHints = spy;
    c.puzzle = { id: 5, fen: FEN, moves: 'e2e4', bookFileName: 'b', hintsFlagged: false };

    c.toggleHintsFlag();

    expect(spy).toHaveBeenCalledWith(5, true);
    expect(c.puzzle.hintsFlagged).toBeTrue();
    expect(c.flagSaving).toBeFalse();
  });

  it('toggleHintsFlag hebt eine bestehende Markierung wieder auf', () => {
    const c = makeComponent();
    c.auth.isAdmin = true;
    c.snackbar.success = jasmine.createSpy('success');
    const spy = jasmine.createSpy('flag').and.returnValue(of({ id: 5, hintsFlagged: false }));
    c.puzzleService.flagBookPuzzleHints = spy;
    c.puzzle = { id: 5, fen: FEN, moves: 'e2e4', bookFileName: 'b', hintsFlagged: true };

    c.toggleHintsFlag();

    expect(spy).toHaveBeenCalledWith(5, false);
    expect(c.puzzle.hintsFlagged).toBeFalse();
  });
});

describe('BookPuzzleComponent Offline-Kursmodus', () => {
  const FILE = 'course-book.pgn';
  const BOOK_ID = 77;
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  function offlineCourse(mode: 'sequential' | 'random'): any {
    // Buch offline ablegen (bookId-Index inklusive).
    saveBookOffline(FILE, [
      { id: 1, fen: FEN, moves: 'e2e4', bookFileName: FILE },
      { id: 2, fen: FEN, moves: 'd2d4', bookFileName: FILE },
      { id: 3, fen: FEN, moves: 'c2c4', bookFileName: FILE },
    ] as any, BOOK_ID);
    const c = makeComponent();
    spyOn(c as any, 'setupPuzzle');
    c.inCourse = true;
    c.courseBookId = BOOK_ID;
    c.courseModeKind = mode;
    return c;
  }

  it('serviert offline das erste Buch-Puzzle aus dem Cache (sequenziell)', () => {
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    try {
      const c = offlineCourse('sequential');
      (c as any).loadCourseNext();
      expect(c.puzzle?.id).toBe(1);
      expect(c.courseTotal).toBe(3);
      expect(c.loadError).toBeFalse();
    } finally { spy.and.callThrough(); }
  });

  it('rückt offline ans nächste ungelöste Puzzle vor', () => {
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    try {
      const c = offlineCourse('sequential');
      (c as any).offlineCourseSolvedIds.add(1);
      (c as any).loadCourseNext(1);   // nach Puzzle 1
      expect(c.puzzle?.id).toBe(2);
    } finally { spy.and.callThrough(); }
  });

  it('zeigt einen Fehler, wenn offline kein Buch gespeichert ist', () => {
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    try {
      const c = makeComponent();
      spyOn(c as any, 'setupPuzzle');
      c.inCourse = true;
      c.courseBookId = 999;   // nicht gecacht
      (c as any).loadCourseNext();
      expect(c.loadError).toBeTrue();
      expect(c.puzzle).toBeNull();
    } finally { spy.and.callThrough(); }
  });
});

describe('BookPuzzleComponent anonymer öffentlicher Kurs', () => {
  const FILE = 'public-course.pgn';
  const BOOK_ID = 55;
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  function anonCourse(mode: 'sequential' | 'random'): any {
    const c = makeComponent();
    spyOn(c as any, 'setupPuzzle');
    c.auth.isLoggedIn = false;   // anonym
    c.inCourse = true;
    c.courseBookId = BOOK_ID;
    c.courseModeKind = mode;
    c.courseService.getPublicCourse = jasmine.createSpy('getPublicCourse').and.returnValue(of([
      { id: 1, fen: FEN, moves: 'e2e4', bookFileName: FILE },
      { id: 2, fen: FEN, moves: 'd2d4', bookFileName: FILE },
    ]));
    return c;
  }

  it('lädt die öffentlichen Puzzles und serviert das erste (ohne Login)', () => {
    const c = anonCourse('sequential');
    (c as any).loadCourseNext();
    // Seitenweises Laden: erste Seite ab skip 0 mit einer Seitengröße.
    expect(c.courseService.getPublicCourse).toHaveBeenCalledWith(BOOK_ID, 0, jasmine.any(Number));
    expect(c.puzzle?.id).toBe(1);
    expect(c.courseTotal).toBe(2);
    expect(c.loadError).toBeFalse();
  });

  it('merkt gelöste Puzzles lokal (persistiert, kein Server-Call)', () => {
    const c = anonCourse('sequential');
    (c as any).loadCourseNext();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: FILE };
    (c as any).recordCourseAttempt(true);
    expect((c as any).offlineCourseSolvedIds.has(1)).toBeTrue();
    expect(c.courseSolved).toBe(1);
    // Frisch aufgesetzte Komponente übernimmt den lokal gemerkten Fortschritt.
    const c2 = anonCourse('sequential');
    c2.courseBookId = BOOK_ID;
    (c2 as any).offlineCourseSolvedIds = new Set<number>();
    // Re-Hydration wie im courseSub-Handler:
    const solved = JSON.parse(localStorage.getItem('rookhub_course_local_solved_' + BOOK_ID) || '[]');
    expect(solved).toContain(1);
  });

  it('zeigt „nicht verfügbar", wenn der Kurs nicht öffentlich ist (404)', () => {
    const c = anonCourse('sequential');
    c.courseService.getPublicCourse = jasmine.createSpy('getPublicCourse')
      .and.returnValue({ subscribe: (h: any) => { (h.error ?? (() => {}))(new Error('404')); return { unsubscribe() {} }; } });
    (c as any).loadCourseNext();
    expect(c.loadError).toBeTrue();
  });
});

describe('BookPuzzleComponent Info-/Erklärlinien (kein Quiz)', () => {
  it('eine IsInfoOnly-Linie geht in den INFO-Durchklick-Modus statt ins Quiz', () => {
    const c = makeComponent();
    const setup = spyOn(c as any, 'setupSolver');   // darf für Info-Linien NICHT laufen
    const puzzle = { id: 7, fen: FEN, moves: 'e2e4 e7e5 g1f3', bookFileName: 'b', isInfoOnly: true };
    c.puzzle = puzzle;
    (c as any).setupPuzzle(puzzle);
    expect(setup).not.toHaveBeenCalled();
    expect(c.state).toBe('INFO');
    expect(c.reviewMode).toBeTrue();
    expect(c.reviewIndex).toBe(0);
    expect(c.reviewTotal).toBe(3);   // ganze Linie durchklickbar
  });

  it('eine normale Linie startet den Solver und geht NICHT in INFO', () => {
    const c = makeComponent();
    const setup = spyOn(c as any, 'setupSolver');
    const puzzle = { id: 8, fen: FEN, moves: 'e2e4 e7e5 g1f3', bookFileName: 'b' };
    c.puzzle = puzzle;
    (c as any).setupPuzzle(puzzle);
    expect(setup).toHaveBeenCalled();
    expect(c.state).not.toBe('INFO');
  });

  it('courseNext auf einer Info-Linie merkt sie serverseitig + offline (überspringen beim Wiedereinstieg)', () => {
    const c = makeComponent();
    c.auth.isLoggedIn = true;   // serverseitiges Merken (markInfoSeen) ist ein eingeloggtes Verhalten
    spyOn(c as any, 'loadCourseNext');   // eigentliches Nachladen unterbinden
    const seen = jasmine.createSpy('markInfoSeen').and.returnValue(of(undefined));
    c.courseService.markInfoSeen = seen;
    c.inCourse = true;
    c.courseBookId = 5;
    c.courseModeKind = 'sequential';
    c.puzzle = { id: 7, fen: FEN, moves: 'e2e4', bookFileName: 'b', isInfoOnly: true };

    c.courseNext();

    expect(seen).toHaveBeenCalledWith(5, 7);
    expect((c as any).offlineCourseSolvedIds.has(7)).toBeTrue();
  });

  it('courseNext auf einer Quiz-Linie merkt NICHT als Info-View', () => {
    const c = makeComponent();
    spyOn(c as any, 'loadCourseNext');
    const seen = jasmine.createSpy('markInfoSeen').and.returnValue(of(undefined));
    c.courseService.markInfoSeen = seen;
    c.inCourse = true;
    c.courseBookId = 5;
    c.courseModeKind = 'sequential';
    c.puzzle = { id: 9, fen: FEN, moves: 'e2e4', bookFileName: 'b' };   // kein isInfoOnly

    c.courseNext();
    expect(seen).not.toHaveBeenCalled();
  });
});

describe('BookPuzzleComponent Kommentar-Anzeige (displayComment)', () => {
  it('außerhalb des Reviews zeigt sie den Puzzle-Kommentar', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', comment: 'Einleitung' };
    c.reviewMode = false;
    c.moveComment = 'sollte-ignoriert-werden';
    expect(c.displayComment).toBe('Einleitung');
  });

  it('im Review bevorzugt sie den Zug-Kommentar, fällt sonst auf den Puzzle-Kommentar zurück', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', comment: 'Einleitung' };
    c.reviewMode = true;
    c.moveComment = 'Zug-Kommentar';
    expect(c.displayComment).toBe('Zug-Kommentar');
    c.moveComment = null;
    expect(c.displayComment).toBe('Einleitung');
  });

  it('ohne Puzzle ist sie null', () => {
    const c = makeComponent();
    c.puzzle = null;
    expect(c.displayComment).toBeNull();
  });

  it('während des Lösens STAPELT commentLines die Zug-Kommentare (kein Intro-Rückfall)', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5 g1f3 b8c6', bookFileName: 'b', startPly: 0,
      comment: 'Einleitung', moveComments: { '0': 'Guter erster Zug', '2': 'Springer raus' } };
    c.reviewMode = false;
    (c as any).onSolutionPath = true;
    (c as any).startPly = 0;
    (c as any).state = 'AWAITING_USER_MOVE';

    (c as any).moveIndex = 0;                                  // vor dem ersten Zug → Einleitung
    expect(c.commentLines).toEqual(['Einleitung']);
    (c as any).moveIndex = 1;                                  // erster Löserzug noch ausstehend → Einleitung bleibt oben (linger), ply-0-Kommentar darunter
    expect(c.commentLines).toEqual(['Einleitung', 'Guter erster Zug']);
    (c as any).moveIndex = 2;                                  // 1. Löserzug gemacht → Einleitung weg, ply 1 KEIN Kommentar → bleibt beim einen
    expect(c.commentLines).toEqual(['Guter erster Zug']);
    (c as any).moveIndex = 3;                                  // ply 2 hat Kommentar → darunter gestapelt
    expect(c.commentLines).toEqual(['Guter erster Zug', 'Springer raus']);
    (c as any).moveIndex = 4;                                  // ply 3 KEIN Kommentar → KEIN Intro-Rückfall
    expect(c.commentLines).toEqual(['Guter erster Zug', 'Springer raus']);
    (c as any).onSolutionPath = false;                         // off-path (Fehlzug) → KEIN Intro-Rückfall
    expect(c.commentLines).toEqual([]);
  });

  it('commentLines: nach einem Fehlzug (off-path) kein Rückfall auf die Einleitung', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5 g1f3 b8c6', bookFileName: 'b', startPly: 0,
      comment: 'Einleitung', moveComments: { '0': 'Guter erster Zug' } } as any;
    c.reviewMode = false;
    (c as any).startPly = 0;
    (c as any).state = 'AWAITING_USER_MOVE';

    // Vor dem 1. Zug: Einleitung.
    (c as any).onSolutionPath = true;
    (c as any).moveIndex = 0;
    expect(c.commentLines).toEqual(['Einleitung']);

    // Erster Löserzug ausstehend: Einleitung bleibt einen Halbzug länger stehen, ply-0-Kommentar darunter.
    (c as any).moveIndex = 1;
    expect(c.commentLines).toEqual(['Einleitung', 'Guter erster Zug']);

    // 1. Löserzug gemacht → Einleitung weg, nur noch der Zug-Kommentar.
    (c as any).moveIndex = 2;
    expect(c.commentLines).toEqual(['Guter erster Zug']);

    // Danach FALSCH → off-path: die Einleitung darf NICHT wieder auftauchen (Bug-Report Buch 84572).
    (c as any).onSolutionPath = false;
    expect(c.commentLines).toEqual([]);
  });

  it('commentLines: Mid-Line-Puzzle (startPly ≥ 1) zeigt den Kommentar NICHT zu früh', () => {
    // Regression: `moveIndex` ist absolut (nach Setup = startPly+1). Die Stapel-Schleife addierte
    // `start` erneut dazu → bei startPly ≥ 1 erschienen Kommentare um startPly Halbzüge zu früh
    // (Daily 2026-07-05: Zug-31-Kommentar bei absolutem ply 59 tauchte schon nach De7/ply 58 auf).
    const c = makeComponent();
    // comment=null → keine Einleitung, damit dieser Test rein die Zug-Kommentar-Terminierung prüft
    // (der Einleitungs-Linger ist separat abgedeckt).
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5 g1f3 b8c6 f1c4', bookFileName: 'b', startPly: 2,
      comment: null, moveComments: { '3': 'Springer-Kommentar' } } as any;
    c.reviewMode = false;
    (c as any).onSolutionPath = true;
    (c as any).startPly = 2;
    (c as any).state = 'AWAITING_USER_MOVE';

    (c as any).moveIndex = 3;   // nach Setup: letzter gespielter Halbzug = ply 2 (ohne Kommentar)
    expect(c.commentLines).toEqual([]);        // KEIN verfrühter Kommentar
    (c as any).moveIndex = 4;   // ply 3 gespielt → dessen Kommentar erscheint
    expect(c.commentLines).toEqual(['Springer-Kommentar']);
  });

  it('commentLines: leer ohne jeden Kommentar während des Lösens (kein Intro)', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b', startPly: 0, comment: 'Einleitung' };
    c.reviewMode = false;
    (c as any).onSolutionPath = true;
    (c as any).startPly = 0;
    (c as any).state = 'AWAITING_USER_MOVE';
    (c as any).moveIndex = 2;                                  // gespielt, aber keine moveComments
    expect(c.commentLines).toEqual([]);                        // → Block ausgeblendet, KEIN Intro
  });
});

describe('BookPuzzleComponent Abschlusskommentar nach der Lösung', () => {
  it('enterSolutionReview zeigt den Kommentar NACH dem letzten Zug (statt der Einleitung)', () => {
    const c = makeComponent();
    c.puzzle = {
      id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', startPly: 0,
      comment: 'Only after...', moveComments: { '-1': 'Only after...', '0': 'Abschlusstext' },
    };
    (c as any).enterSolutionReview();
    expect(c.reviewMode).toBeTrue();
    expect(c.displayComment).toBe('Abschlusstext');   // Schlusstext, nicht die Einleitung
  });

  it('erkennt einen Abschlusskommentar nach dem letzten Zug', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', moveComments: { '0': 'Ende' } };
    expect((c as any).hasTrailingSolutionComment).toBeTrue();
  });

  it('ohne Kommentar nach dem letzten Zug ist es kein Trailing-Kommentar (nur Einleitung zählt nicht)', () => {
    const c = makeComponent();
    c.puzzle = { id: 2, fen: FEN, moves: 'e2e4', bookFileName: 'b', moveComments: { '-1': 'nur intro' } };
    expect((c as any).hasTrailingSolutionComment).toBeFalse();
  });

  it('finalizeSolve springt bei Abschlusstext NICHT automatisch weiter (kein Countdown)', () => {
    const c = makeComponent();
    const countdown = spyOn(c as any, 'startSolvedCountdown');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordTrack');
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', moveComments: { '0': 'Abschlusstext' } };
    (c as any).finalizeSolve();
    expect(countdown).not.toHaveBeenCalled();
  });

  it('finalizeSolve springt ohne Abschlusstext wie bisher automatisch weiter (Countdown)', () => {
    const c = makeComponent();
    const countdown = spyOn(c as any, 'startSolvedCountdown');
    spyOn(c as any, 'recordCourseAttempt');
    spyOn(c as any, 'recordWeeklyAttempt');
    spyOn(c as any, 'recordBookAttempt');
    spyOn(c as any, 'recordTrack');
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b' };   // keine moveComments
    (c as any).finalizeSolve();
    expect(countdown).toHaveBeenCalled();
  });
});

/**
 * Kumulierte Tagespuzzle-Lösezeit: Wiederbesuch führt die gemerkte Zeit fort, der Sekunden-Tick
 * persistiert den Zwischenstand, ein erfasster Versuch beendet das Kumulieren.
 */
describe('BookPuzzleComponent Daily kumulierte Lösezeit', () => {
  const DATE = '20260718';
  beforeEach(() => localStorage.removeItem('rookhub_daily_elapsed'));
  afterEach(() => localStorage.removeItem('rookhub_daily_elapsed'));

  function makeDaily(): any {
    const c: any = makeComponent();
    c.puzzle = { id: 42, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    c.dailyDate = DATE;
    return c;
  }

  it('resumes the stored time when solving begins again (revisit)', () => {
    saveDailyElapsed(DATE, 120);
    const c = makeDaily();
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(120);
    (c as any).stopTimer();
  });

  it('starts at 0 without a stored time and outside the daily mode', () => {
    const c = makeDaily();
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(0);
    (c as any).stopTimer();
    saveDailyElapsed(DATE, 99);
    const plain = makeComponent();
    (plain as any).puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (plain as any).onSolvingBegins();          // kein Daily → keine Fortführung
    expect((plain as any).elapsedSeconds).toBe(0);
    (plain as any).stopTimer();
  });

  it('the timer tick persists the current stand', () => {
    const c = makeDaily();
    c.elapsedSeconds = 42;
    (c as any).onTimerTick();
    expect(loadDailyElapsed(DATE)).toBe(42);
  });

  it('a recorded attempt (logged in) clears the stored time', () => {
    saveDailyElapsed(DATE, 99);
    const c = makeDaily();
    c.auth = { isLoggedIn: true };
    c.puzzleService.recordBookAttempt = () => ({ subscribe: () => {} });
    (c as any).recordBookAttempt(false);
    expect(loadDailyElapsed(DATE)).toBe(0);
  });

  it('an anonymous FAIL keeps accumulating (nothing recorded server-side), a solve clears', () => {
    saveDailyElapsed(DATE, 99);
    const c = makeDaily();                      // auth.isLoggedIn = false
    c.puzzleService.ensureSessionId = () => 's1';
    c.puzzleService.recordBookAttemptAnonymous = () => ({ subscribe: () => {} });
    (c as any).recordBookAttempt(false);        // anon-Fail wird nicht gemeldet
    expect(loadDailyElapsed(DATE)).toBe(99);
    (c as any).recordBookAttempt(true);         // anon-Solve wird gemeldet → Ende der Kumulation
    expect(loadDailyElapsed(DATE)).toBe(0);
  });

  it('leaving mid-run stores the final stand; after SOLVED it does not resurrect', () => {
    const c = makeDaily();
    c.state = 'AWAITING_USER_MOVE';
    (c as any).startTimer(33);      // laufende Stoppuhr mit fortgeführtem Stand (ngOnDestroy stoppt sie)
    c.ngOnDestroy();
    expect(loadDailyElapsed(DATE)).toBe(33);
    localStorage.removeItem('rookhub_daily_elapsed');
    const done = makeDaily();
    done.state = 'SOLVED';
    done.elapsedSeconds = 50;
    done.ngOnDestroy();
    expect(loadDailyElapsed(DATE)).toBe(0);     // erfasster Versuch bleibt gelöscht
  });
});

describe('BookPuzzleComponent Kurs kumulierte Lösezeit', () => {
  const KEY = 'course:77';
  beforeEach(() => localStorage.removeItem('rookhub_solve_elapsed'));
  afterEach(() => localStorage.removeItem('rookhub_solve_elapsed'));

  function makeCourse(): any {
    const c: any = makeComponent();
    c.puzzle = { id: 77, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    c.inCourse = true;
    c.courseBookId = 5;
    return c;
  }

  it('resumes the stored time when solving begins again (refresh)', () => {
    saveSolveElapsed(KEY, 60);
    const c = makeCourse();
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(60);
    (c as any).stopTimer();
  });

  it('starts at 0 without a stored time; a plain book puzzle never resumes', () => {
    const c = makeCourse();
    (c as any).onSolvingBegins();
    expect(c.elapsedSeconds).toBe(0);
    (c as any).stopTimer();
    saveSolveElapsed(KEY, 99);
    const plain = makeComponent();
    (plain as any).puzzle = { id: 77, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (plain as any).onSolvingBegins();          // kein Kursmodus → keine Fortführung
    expect((plain as any).elapsedSeconds).toBe(0);
    (plain as any).stopTimer();
  });

  it('the timer tick persists the current stand (course mode only)', () => {
    const c = makeCourse();
    c.elapsedSeconds = 42;
    (c as any).onTimerTick();
    expect(loadSolveElapsed(KEY)).toBe(42);
    const plain = makeComponent();
    (plain as any).puzzle = { id: 78, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };
    (plain as any).elapsedSeconds = 33;
    (plain as any).onTimerTick();              // außerhalb des Kurses wird nichts gemerkt
    expect(loadSolveElapsed('course:78')).toBe(0);
  });

  it('a recorded course attempt (logged in) clears the stored time', () => {
    saveSolveElapsed(KEY, 99);
    const c = makeCourse();
    c.auth = { isLoggedIn: true };
    c.courseService.recordResult = () => ({ subscribe: () => {} });
    (c as any).recordCourseAttempt(false);
    expect(loadSolveElapsed(KEY)).toBe(0);
  });

  it('an anonymous course attempt clears too (retry starts fresh)', () => {
    saveSolveElapsed(KEY, 99);
    const c = makeCourse();                    // auth.isLoggedIn = false → isAnonCourse
    (c as any).recordCourseAttempt(false);
    expect(loadSolveElapsed(KEY)).toBe(0);
  });

  it('leaving mid-run stores the final stand; after SOLVED it does not resurrect', () => {
    const c = makeCourse();
    c.state = 'AWAITING_USER_MOVE';
    (c as any).startTimer(33);      // laufende Stoppuhr mit fortgeführtem Stand (ngOnDestroy stoppt sie)
    c.ngOnDestroy();
    expect(loadSolveElapsed(KEY)).toBe(33);
    localStorage.removeItem('rookhub_solve_elapsed');
    const done = makeCourse();
    done.state = 'SOLVED';
    done.elapsedSeconds = 50;
    done.ngOnDestroy();
    expect(loadSolveElapsed(KEY)).toBe(0);     // erfasster Versuch bleibt gelöscht
  });
});
