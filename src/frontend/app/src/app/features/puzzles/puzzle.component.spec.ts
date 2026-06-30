import { fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { PuzzleComponent } from './puzzle.component';

/**
 * Fokussierter Test des Aufgeben-Verhaltens (giveUp) ohne TestBed/Template:
 * Aufgeben soll die Lösung ab der Anfangsstellung automatisch durchspielen
 * (NICHT bloß zurücksetzen wie resetPuzzle).
 */
function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { paramMap: { get: () => null }, queryParamMap: { get: () => null } } };
  const dialog: any = {};
  const offline: any = { puzzleCount: 0, endlessRuns: 0 };
  const offlineQueue: any = { enqueue: jasmine.createSpy('enqueue') };
  const snackbar: any = { success: () => {}, info: () => {} };
  const challengeService: any = { send: () => ({ subscribe: () => {} }), resolve: () => ({ subscribe: () => {} }) };
  const revengeService: any = { recordResult: () => ({ subscribe: () => {} }) };
  const translate: any = { instant: (k: string) => k };
  const http: any = { get: () => ({ subscribe: () => {} }) };
  const longSolve: any = { resolve: (s: number) => of(s) };
  const favorites: any = { contains: () => of(false), add: () => of(true), remove: () => of(false), count: () => of(0), list: () => of([]) };
  return new PuzzleComponent(puzzleService, stockfish, auth, prefs, router, route, dialog, offline, offlineQueue, snackbar, challengeService, revengeService, translate, http, longSolve, favorites);
}

const PUZZLE = { id: 1, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5 g1f3', rating: 1500 };

describe('PuzzleComponent alternative Lösung (kein Auto-Advance)', () => {
  function solvedComponent() {
    const c = makeComponent();
    spyOn(c as any, 'enterSolutionReview');
    spyOn(c as any, 'updateBoard');
    spyOn(c as any, 'stopTimer');
    spyOn(c as any, 'startSolvedCountdown');
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;   // HTTP-Aufzeichnung überspringen
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

  it('singlePuzzle (?single=1): kein Auto-Weiter, bleibt aber gelöst stehen', () => {
    const c = solvedComponent();
    c.singlePuzzle = true;
    (c as any).handleSolved(false);
    expect((c as any).startSolvedCountdown).not.toHaveBeenCalled();
    expect(c.state).toBe('SOLVED');
  });
});

describe('PuzzleComponent give-up', () => {
  it('plays the solution from the start position move by move', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;   // HTTP-Aufzeichnung überspringen

    c.giveUp();

    expect(c.gaveUp).toBeTrue();
    expect(c.state).toBe('FAILED');         // Aufgeben = Fehlversuch (recordAttempt(false))
    expect(c.reviewMode).toBeTrue();
    expect(c.reviewIndex).toBe(0);          // startet an der Anfangsstellung

    tick(900); expect(c.reviewIndex).toBe(1);
    tick(900); expect(c.reviewIndex).toBe(2);
    tick(900); expect(c.reviewIndex).toBe(3); // = reviewTotal, fertig
    tick(900); expect(c.reviewIndex).toBe(3); // bleibt stehen (Timer beendet)

    c.ngOnDestroy();
  }));

  it('is NOT a plain reset (review-playthrough, not back to solving)', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;

    c.giveUp();
    // resetPuzzle würde wieder in einen Lös-Zustand gehen; Aufgeben bleibt im Review (FAILED).
    expect(c.state).toBe('FAILED');
    expect(c.reviewMode).toBeTrue();

    c.ngOnDestroy();
  }));

  it('reviewLastPuzzle navigates straight to the analysis board with the last solved puzzle', () => {
    const c = makeComponent();
    // Zustand wie nach einem gelösten Puzzle (handleSolved merkt sich id/fen/moves/orientation):
    c.puzzle = { ...PUZZLE, id: 123 };      // aktuelles Puzzle = das gelöste → from-Param '/puzzles/123'
    c.lastSolvedPuzzleId = 123;
    c.lastSolvedFen = PUZZLE.fen;
    c.lastSolvedMoves = PUZZLE.moves;       // 'e2e4 e7e5 g1f3'
    c.lastSolvedOrientation = 'black';

    c.reviewLastPuzzle();

    expect((c as any).router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'black', from: '/puzzles/123' },
    });
  });

  it('manual review navigation stops the auto-playback', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.attemptRecorded = true;

    c.giveUp();
    tick(900); expect(c.reviewIndex).toBe(1);

    c.reviewNext();              // manuell → Auto-Play stoppt
    expect(c.reviewIndex).toBe(2);
    tick(2000);
    expect(c.reviewIndex).toBe(2); // kein weiterer Auto-Schritt

    c.ngOnDestroy();
  }));

  it('showOriginalSolution plays the intended solution from the start (after an alternative solve)', fakeAsync(() => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    // Zustand nach alternativem (eigenem) Mattweg: gelöst, aber abweichend von der vorgesehenen Zugfolge.
    c.state = 'SOLVED';
    c.alternativeSolve = true;
    // Laufender Auto-Advance-Countdown soll durch das Anzeigen gestoppt werden.
    (c as any).startSolvedCountdown(() => {});

    c.showOriginalSolution();

    expect(c.solvedCountdown).toBe(0);   // Countdown gestoppt → kein Auto-Weiter beim Zuschauen
    expect(c.reviewMode).toBeTrue();
    expect(c.reviewIndex).toBe(0);       // startet an der Anfangsstellung der vorgesehenen Lösung

    tick(900); expect(c.reviewIndex).toBe(1);
    tick(900); expect(c.reviewIndex).toBe(2);
    tick(900); expect(c.reviewIndex).toBe(3); // = reviewTotal, fertig

    c.ngOnDestroy();
  }));
});

describe('PuzzleComponent offline pool exhaustion', () => {
  let originalDescriptor: PropertyDescriptor | undefined;

  beforeEach(() => {
    originalDescriptor = Object.getOwnPropertyDescriptor(navigator, 'onLine');
    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false });
  });

  afterEach(() => {
    if (originalDescriptor) Object.defineProperty(navigator, 'onLine', originalDescriptor);
    else Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => true });
  });

  it('signals exhausted (NOT no-cache) when pool empties after having shown a puzzle', () => {
    const c = makeComponent();
    // Erstaufruf-Cache leer; aber lastShownPuzzle gesetzt → Pool wurde durchgespielt.
    (c as any).offlinePuzzlePool = [];
    (c as any).lastShownPuzzle = { ...PUZZLE };

    c.loadNext();

    expect(c.state).toBe('ERROR');
    expect(c.offlinePoolExhausted).toBeTrue();
    expect(c.offlineNoCache).toBeFalse();
    c.ngOnDestroy();
  });

  it('signals no-cache when pool is empty AND nothing was ever shown', () => {
    const c = makeComponent();
    (c as any).offlinePuzzlePool = [];
    // lastShownPuzzle bleibt null → klassischer „nie online geöffnet"-Fall.

    c.loadNext();

    expect(c.state).toBe('ERROR');
    expect(c.offlineNoCache).toBeTrue();
    expect(c.offlinePoolExhausted).toBeFalse();
    c.ngOnDestroy();
  });

  it('replayLastPuzzle replays the last shown puzzle and clears the exhausted flag', () => {
    const c = makeComponent();
    const last = { ...PUZZLE };
    (c as any).lastShownPuzzle = last;
    c.offlinePoolExhausted = true;
    // setupPuzzle ruft setupSolver auf — den hier neutralisieren, der echte Solver hängt an Stockfish.
    spyOn(c as any, 'setupPuzzle');

    c.replayLastPuzzle();

    expect(c.puzzle).toBe(last);
    expect(c.offlinePoolExhausted).toBeFalse();
    expect((c as any).setupPuzzle).toHaveBeenCalledWith(last);
    c.ngOnDestroy();
  });
});

describe('PuzzleComponent load race (loadEpoch)', () => {
  it('a stale puzzle response does not overwrite a newer one', () => {
    const c = makeComponent();
    spyOn(c as any, 'setupPuzzle');
    spyOn(c as any, 'prefetchNext');
    spyOn(c as any, 'prefetchOfflinePool');
    c.stats = { puzzleElo: 1500 };
    (c as any).ratingRangeBounds = { min: 0, max: 4000 };

    // getRandom gibt steuerbare Observables zurück; wir lösen sie bewusst out-of-order auf.
    const emits: Array<(v: any) => void> = [];
    (c as any).puzzleService.getRandom = () => ({
      subscribe: (h: any) => { emits.push((v: any) => (typeof h === 'function' ? h : h.next)(v)); return { unsubscribe() {} }; }
    });

    c.loadNext();   // Epoch 1 → emits[0]
    c.loadNext();   // Epoch 2 → emits[1]

    emits[1]({ ...PUZZLE, id: 222 });   // neuere Anfrage löst zuerst auf
    expect(c.puzzle.id).toBe(222);
    emits[0]({ ...PUZZLE, id: 111 });   // ältere Anfrage löst danach auf → muss verworfen werden
    expect(c.puzzle.id).toBe(222);

    c.ngOnDestroy();
  });
});

describe('PuzzleComponent „dumme Tipps" markieren', () => {
  it('toggleHintsFlag setzt das Flag und ruft den Service', () => {
    const c = makeComponent();
    c.snackbar.success = jasmine.createSpy('success');
    const spy = jasmine.createSpy('flag').and.returnValue(of({ id: 9, hintsFlagged: true }));
    c.puzzleService.flagPuzzleHints = spy;
    c.puzzle = { id: 9, fen: 'x', moves: 'a', hintsFlagged: false };

    c.toggleHintsFlag();

    expect(spy).toHaveBeenCalledWith(9, true);
    expect(c.puzzle.hintsFlagged).toBeTrue();
    expect(c.flagSaving).toBeFalse();
  });
});
