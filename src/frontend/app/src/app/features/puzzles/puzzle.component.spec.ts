import { fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { PuzzleComponent } from './puzzle.component';

/**
 * Fokussierter Test des Aufgeben-Verhaltens (giveUp) ohne TestBed/Template:
 * Aufgeben soll die Lösung ab der Anfangsstellung automatisch durchspielen
 * (NICHT bloß zurücksetzen wie resetPuzzle).
 */
/** Minimaler SolveModeService-Ersatz: merkt sich die Wahl je Bereich und zählt die Abfragen.
 *  `antwort` = was der (echte) Dialog liefern würde; `dialogCalls` zählt, wie oft tatsächlich
 *  gefragt worden wäre (der echte Service fragt nur beim ersten Mal je Bereich). */
function makeSolveMode(antwort: 'training' | 'easy' = 'training', prefsViz = 3): any {
  const gemerkt: Record<string, string> = {};
  const stub: any = { gemerkt, dialogCalls: 0 };
  Object.assign(stub, {
    ensure: jasmine.createSpy('ensure').and.callFake((scope: string) => {
      if (!gemerkt[scope]) { stub.dialogCalls++; gemerkt[scope] = antwort; }
      return of(gemerkt[scope]);
    }),
    get: (scope: string) => gemerkt[scope] ?? null,
    set: jasmine.createSpy('set').and.callFake((scope: string, mode: string) => { gemerkt[scope] = mode; }),
    levelFor: (mode: string) => (mode === 'easy' ? 0 : Math.max(1, prefsViz)),
    modeForLevel: (level: number) => (level > 0 ? 'training' : 'easy'),
  });
  return stub;
}

function makeComponent(params: Record<string, string> = {}, solveMode: any = makeSolveMode()): any {
  const prefs: any = {
    boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0,
    setVisualization(v: number) { this.visualization = v; },
  };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = {};
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { paramMap: { get: () => null }, queryParamMap: { get: (k: string) => params[k] ?? null } } };
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
  // Ohne Stats-Emission bleibt ngOnInit nach der Spielweisen-Abfrage stehen (kein Puzzle-Load).
  puzzleService.getStats = () => ({ subscribe: () => {} });
  puzzleService.getAnonymousStats = () => ({ subscribe: () => {} });
  puzzleService.getRatingRange = () => ({ subscribe: () => {} });
  const c: any = new PuzzleComponent(puzzleService, stockfish, auth, prefs, router, route, dialog, offline, offlineQueue, snackbar, challengeService, revengeService, translate, http, longSolve, favorites, solveMode);
  c.solveModeStub = solveMode;
  return c;
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


/**
 * Spielweise (Training/Einfach) im Bereich „Puzzles": einmalig erfragen, danach stumm anwenden —
 * aber NICHT fragen, wenn die Ansicht per Link vorgegeben ist oder ein Einzelkontext
 * (geteiltes Puzzle / Challenge / Revanche) geöffnet wurde.
 */
describe('PuzzleComponent Spielweise', () => {
  it('fragt beim ersten Einstieg mit dem Bereich „puzzles"', () => {
    const sm = makeSolveMode('training');
    const c = makeComponent({}, sm);

    c.ngOnInit();

    expect(sm.ensure).toHaveBeenCalled();
    expect(sm.ensure.calls.mostRecent().args[0]).toBe('puzzles');
    expect(sm.ensure.calls.mostRecent().args[1].scopeLabel).toBe('solveMode.scope.puzzles');
    expect(sm.dialogCalls).toBe(1);
    c.ngOnDestroy();
  });

  it('fragt beim zweiten Einstieg NICHT mehr (gemerkte Wahl)', () => {
    const sm = makeSolveMode('easy');
    const erste = makeComponent({}, sm);
    erste.ngOnInit();
    erste.ngOnDestroy();

    const zweite = makeComponent({}, sm);
    zweite.ngOnInit();

    expect(sm.dialogCalls).toBe(1);          // nur der erste Einstieg hat gefragt
    expect(zweite.solveModeChoice).toBe('easy');
    zweite.ngOnDestroy();
  });

  // Der Dialog blockiert: die Statistik wird mit der ALTEN Stufe geladen, bevor die Wahl da ist.
  // Verschiebt die Wahl die Stufe, muss die Elo-Statistik dazu nachgeladen werden.
  it('lädt die Elo-Statistik zur neuen Stufe nach, wenn die Wahl sie verschiebt', () => {
    let antworten: ((m: string) => void) | null = null;
    const sm: any = {
      ensure: () => ({ subscribe: (h: any) => { antworten = typeof h === 'function' ? h : h.next; return { unsubscribe() {} }; } }),
      set: () => {},
      levelFor: (mode: string) => (mode === 'easy' ? 0 : 3),
      modeForLevel: (level: number) => (level > 0 ? 'training' : 'easy'),
    };
    const c = makeComponent({}, sm);
    c.authService.isLoggedIn = true;
    const getStats = jasmine.createSpy('getStats').and.returnValue({ subscribe: () => {} });
    c.puzzleService.getStats = getStats;

    c.ngOnInit();
    expect(getStats).toHaveBeenCalledWith(0);   // erst die Stufe aus den Einstellungen

    antworten!('training');                      // jetzt entscheidet sich der Nutzer

    expect(c.visualizationMode).toBe(3);
    expect(getStats).toHaveBeenCalledWith(3);   // Statistik zur neuen Stufe nachgeladen
    c.ngOnDestroy();
  });

  it('wendet die Stufe der gewählten Spielweise an (einfach = 0, Training = eingestellte Stufe)', () => {
    const einfach = makeComponent({}, makeSolveMode('easy', 3));
    einfach.ngOnInit();
    expect(einfach.visualizationMode).toBe(0);
    einfach.ngOnDestroy();

    const training = makeComponent({}, makeSolveMode('training', 3));
    training.ngOnInit();
    expect(training.visualizationMode).toBe(3);
    training.ngOnDestroy();
  });

  it('setzt ein bereits stehendes Puzzle mit der gewählten Spielweise neu auf', () => {
    const c = makeComponent({}, makeSolveMode('easy'));
    c.puzzle = { ...PUZZLE };
    spyOn(c as any, 'setupPuzzle');

    c.ngOnInit();

    expect((c as any).setupPuzzle).toHaveBeenCalledWith(c.puzzle);
    c.ngOnDestroy();
  });

  it('fragt NICHT bei fester Ansicht aus dem Link (?visualmode=)', () => {
    const sm = makeSolveMode('easy');
    const c = makeComponent({ visualmode: '2' }, sm);

    c.ngOnInit();

    expect(sm.ensure).not.toHaveBeenCalled();
    expect(c.solveModeChoice).toBeNull();
    expect(c.visualizationMode).toBe(2);     // Stufe aus der URL bleibt
    c.ngOnDestroy();
  });

  it('fragt NICHT beim geteilten Einzel-Puzzle (?single=1), bei Challenge und bei Revanche', () => {
    const faelle: Record<string, string>[] = [{ single: '1' }, { challengeId: '5' }, { revengeUserId: '9' }];
    for (const params of faelle) {
      const sm = makeSolveMode('easy');
      const c = makeComponent(params, sm);
      (c as any).loadRevengeQueue = () => {};   // Revanche: Queue-Abruf im Test überspringen

      c.ngOnInit();

      expect(sm.ensure).not.toHaveBeenCalled();
      expect(c.solveModeChoice).toBeNull();
      c.ngOnDestroy();
    }
  });

  it('Umschalten merkt die neue Spielweise, wirkt aber erst beim nächsten Puzzle', () => {
    const sm = makeSolveMode('training', 3);
    const c = makeComponent({}, sm);
    c.ngOnInit();
    c.snackbar.info = jasmine.createSpy('info');
    c.puzzleService.getRandom = () => ({ subscribe: () => {} });
    expect(c.visualizationMode).toBe(3);

    c.toggleSolveMode();

    expect(c.solveModeChoice).toBe('easy');
    expect(sm.set).toHaveBeenCalledWith('puzzles', 'easy');
    expect(c.snackbar.info).toHaveBeenCalledWith('solveMode.switchedEasy', { duration: 3000 });
    expect(c.visualizationMode).toBe(3);      // laufender Versuch behält seine Regeln

    c.loadNext();
    expect(c.visualizationMode).toBe(0);      // erst das nächste Puzzle spielt einfach
    c.ngOnDestroy();
  });

  it('direkte Stufenwahl zieht die gemerkte Spielweise mit', () => {
    const sm = makeSolveMode('training', 3);
    const c = makeComponent({}, sm);

    c.setVisualizationLevel(0);
    expect(sm.set).toHaveBeenCalledWith('puzzles', 'easy');
    expect(c.solveModeChoice).toBe('easy');

    c.setVisualizationLevel(2);
    expect(sm.set).toHaveBeenCalledWith('puzzles', 'training');
    expect(c.solveModeChoice).toBe('training');
    c.ngOnDestroy();
  });
});
