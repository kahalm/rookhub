import { of } from 'rxjs';
import { BookPuzzleComponent } from './book-puzzle.component';

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
  const translate: any = { instant: (k: string) => k };
  const auth: any = { isLoggedIn: false };
  const snackbar: any = { info: () => {} };
  const offlineQueue: any = { enqueue: () => {} };
  const challengeService: any = { resolve: () => ({ subscribe: () => {} }) };
  // Default: keine Kappung — gibt die gemessene Zeit unverändert zurück (Tests überschreiben bei Bedarf).
  const longSolve: any = { resolve: (s: number) => of(s) };
  return new BookPuzzleComponent(
    puzzleService, stockfish, prefs, route, dialog, courseService, weeklyService,
    router, translate, auth, snackbar, offlineQueue, challengeService, longSolve
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
    expect(data.url.endsWith('/puzzles/book/200')).toBeTrue();
    expect(data.previousUrl.endsWith('/puzzles/book/100')).toBeTrue();
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
    expect(c.hasHints).toBeTrue();
    expect(c.availableHints).toEqual(HINTS.en);
  });

  it('fällt auf de zurück, wenn nur de vorhanden ist', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', hints: { de: HINTS.de } };
    expect(c.availableHints).toEqual(HINTS.de);
  });

  it('deckt mit jedem Tipp eine Stufe mehr auf und stoppt bei 3', () => {
    const c = makeComponent();
    c.puzzle = { id: 1, fen: FEN, moves: 'e2e4', bookFileName: 'b', hints: HINTS };
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
  it('setupPuzzle klassifiziert den ersten Löserzug, wenn keine vorberechneten Tipps da sind', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupSolver');   // echten Solver neutralisieren
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b' };  // moves[1]=e7e5 (ruhiger Bauernzug)
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;

    expect(c.hasPrecomputedHints).toBeFalse();
    expect(c.hasHints).toBeTrue();                    // on-the-fly Fallback greift
    expect(c.availableHints.length).toBe(3);
    expect(c.availableHints[0]).toBe('puzzles.hints.t1Quiet');   // translate-Mock gibt den Key zurück
  });

  it('vorberechnete Tipps haben Vorrang vor dem on-the-fly Fallback', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupSolver');
    const HINTS = { en: ['Motif', 'Piece', 'Move'] };
    const puzzle = { id: 1, fen: FEN, moves: 'e2e4 e7e5', bookFileName: 'b', hints: HINTS };
    (c as any).setupPuzzle(puzzle);
    c.puzzle = puzzle;

    expect(c.hasPrecomputedHints).toBeTrue();
    expect(c.availableHints).toEqual(HINTS.en);
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
