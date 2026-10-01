import { fakeAsync, tick } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
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
  const worksheets: any = { sendAndNotify: jasmine.createSpy('sendAndNotify') };
  const c: any = new PuzzleComponent(puzzleService, stockfish, auth, prefs, router, route, dialog, offline, offlineQueue, snackbar, challengeService, revengeService, translate, http, longSolve, favorites, solveMode, worksheets);
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

// Codereview N9-001 (Client-Teil): /resolve und /revenge/result gingen gleichzeitig mit dem Versuchs-POST hinaus, der
// Server prüfte, bevor der Versuch stand — echte Lösungen wurden als „nicht gelöst" gebucht, die Revanche-Glocke fiel
// aus. Jetzt: erst im next-Zweig des Versuchs; bei Fehler oder offline nur der Versuch (mit revengeUserId) in die Schlange.
describe('PuzzleComponent Challenge/Revanche erst nach dem gespeicherten Versuch', () => {
  function challengeComponent() {
    const c = makeComponent();
    (c as any).authService = { isLoggedIn: true };
    c.puzzle = { ...PUZZLE };
    (c as any).challengeId = 7;
    (c as any).revengeUserId = 9;
    const attempt$ = new Subject<any>();
    c.puzzleService.recordAttempt = jasmine.createSpy('recordAttempt').and.returnValue(attempt$);
    c.puzzleService.getStats = () => of({});
    const resolve = jasmine.createSpy('resolve').and.returnValue(of({}));
    const recordResult = jasmine.createSpy('recordResult').and.returnValue(of({}));
    (c as any).challengeService = { resolve };
    (c as any).revengeService = { recordResult };
    return { c, attempt$, resolve, recordResult };
  }

  it('meldet Challenge und Revanche erst, wenn der Versuchs-POST geantwortet hat', () => {
    const { c, attempt$, resolve, recordResult } = challengeComponent();
    (c as any).recordAttempt(true, 12);

    expect(c.puzzleService.recordAttempt).toHaveBeenCalled();
    expect(c.puzzleService.recordAttempt.calls.mostRecent().args[8]).toBe(9);   // revengeUserId im Versuch
    expect(resolve).not.toHaveBeenCalled();
    expect(recordResult).not.toHaveBeenCalled();
    expect(c.revengeSolvedCount).toBe(1);   // Abschlusskarte zählt sofort, nicht erst mit der Antwort

    attempt$.next({ eloChange: 4 });
    expect(resolve).toHaveBeenCalledOnceWith(7, true, 12);
    expect(recordResult).toHaveBeenCalledOnceWith(9, PUZZLE.id, true);
  });

  it('scheitert der Versuch, geht nur er (mit revengeUserId) in die Schlange — kein /resolve, keine Revanche-Meldung', () => {
    const { c, attempt$, resolve, recordResult } = challengeComponent();
    (c as any).recordAttempt(false, 30);
    attempt$.error(new Error('500'));

    expect(resolve).not.toHaveBeenCalled();
    expect(recordResult).not.toHaveBeenCalled();
    expect(c.offlineQueue.enqueue).toHaveBeenCalledTimes(1);
    const [method, url, body] = c.offlineQueue.enqueue.calls.mostRecent().args;
    expect([method, url]).toEqual(['POST', `/api/puzzles/${PUZZLE.id}/attempt`]);
    expect(body.revengeUserId).toBe(9);   // beim Nachsenden legt der Server die Glocke an
  });

  it('ein späteres Puzzle derselben Seite meldet die Challenge nicht mit seinem Ergebnis', () => {
    const { c, attempt$, resolve } = challengeComponent();
    (c as any).recordAttempt(true, 12);
    attempt$.error(new Error('offline'));   // Challenge-Puzzle: Versuch in der Schlange

    const next$ = new Subject<any>();
    c.puzzleService.recordAttempt.and.returnValue(next$);
    c.puzzle = { ...PUZZLE, id: 2 };
    c.attemptRecorded = false;              // wie loadNext()
    (c as any).revengeNotified = false;
    (c as any).recordAttempt(false, 5);
    next$.next({});

    expect(resolve).not.toHaveBeenCalled();
  });

  it('offline: nur der Versuch samt revengeUserId in die Schlange, keine Meldung', () => {
    const spy = spyOnProperty(navigator, 'onLine', 'get').and.returnValue(false);
    try {
      const { c, resolve, recordResult } = challengeComponent();
      (c as any).recordAttempt(true, 12);
      expect(c.puzzleService.recordAttempt).not.toHaveBeenCalled();
      expect(resolve).not.toHaveBeenCalled();
      expect(recordResult).not.toHaveBeenCalled();
      expect(c.offlineQueue.enqueue.calls.mostRecent().args[2].revengeUserId).toBe(9);
    } finally { spy.and.callThrough(); }
  });
});

/** Einstellungsdialog-Doppel: `open()` liefert sofort das Ergebnis (Brett/Figuren geändert, Rest wie vorher). */
function stubSettingsDialog(c: any, overrides: Record<string, unknown> = {}): void {
  const p: any = c.prefs;
  Object.assign(p, {
    offPathWarnMoves: 3, enPassantForced: true,
    setBoardTheme: (t: string) => { p.boardTheme = t; }, setPieceSet: (s: string) => { p.pieceSet = s; },
    setThemeMode: (m: string) => { p.themeMode = m; }, setVizArrow: () => {}, setStockfishDepth: () => {},
    setPuzzleDifficulty: () => {}, setOffPathWarnMoves: () => {}, setEnPassantForced: (v: boolean) => { p.enPassantForced = v; },
  });
  const result = {
    boardTheme: 'blue', pieceSet: 'alpha', themeMode: c.themeMode, visualizationMode: c.visualizationMode,
    vizArrowEnabled: c.vizArrowEnabled, enPassantForced: true, ...overrides,
  };
  c.dialog.open = () => ({ afterClosed: () => of(result) });
}

// Codereview F2-009: Speichern im Einstellungsdialog setzte das laufende Puzzle IMMER neu auf — auch wenn nur
// Brett/Figuren wechselten (Uhr wieder bei 0, Zugliste weg; nach Gelöst zurück ins Setup). Und die e.p.-Zeile
// überschrieb den Link-Zwang (?anarchy=max).
describe('PuzzleComponent Einstellungen speichern (F2-009)', () => {
  it('nur Brett/Figuren geändert: der laufende Versuch bleibt stehen, die Uhr läuft weiter', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.state = 'AWAITING_USER_MOVE';
    c.elapsedSeconds = 40;
    spyOn(c as any, 'setupPuzzle');
    stubSettingsDialog(c);

    c.openSettingsDialog();

    expect((c as any).setupPuzzle).not.toHaveBeenCalled();
    expect(c.elapsedSeconds).toBe(40);
    expect(c.boardTheme).toBe('blue');
    c.ngOnDestroy();
  });

  it('geänderte Stufe während des Lösens setzt das Puzzle neu auf', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.state = 'AWAITING_USER_MOVE';
    spyOn(c as any, 'setupPuzzle');
    stubSettingsDialog(c, { visualizationMode: 2 });

    c.openSettingsDialog();

    expect(c.visualizationMode).toBe(2);
    expect((c as any).setupPuzzle).toHaveBeenCalledWith(c.puzzle);
    c.ngOnDestroy();
  });

  it('geänderte Stufe nach Gelöst springt NICHT zurück ins Setup (gilt ab dem nächsten Puzzle)', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.state = 'SOLVED';
    spyOn(c as any, 'setupPuzzle');
    stubSettingsDialog(c, { visualizationMode: 2 });

    c.openSettingsDialog();

    expect(c.visualizationMode).toBe(2);
    expect(c.state).toBe('SOLVED');
    expect((c as any).setupPuzzle).not.toHaveBeenCalled();
    c.ngOnDestroy();
  });

  it('?anarchy=max erzwingt e.p. weiter, auch wenn es in den Einstellungen aus ist', () => {
    const c = makeComponent();
    (c as any).anarchyForcedByUrl = true;      // wie nach ngOnInit mit ?anarchy=max
    c.enPassantForced = true;                  // wie nach onSetupStart
    stubSettingsDialog(c, { enPassantForced: false });

    c.openSettingsDialog();

    expect(c.enPassantForced).toBeTrue();
    c.ngOnDestroy();
  });
});
