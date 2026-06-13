import { EndlessPuzzleComponent } from './endless-puzzle.component';

/**
 * Fokussierter Test der Analyse-Navigation im Endless-Modus (ohne TestBed/Template):
 * - „Analysieren" beim Aufgeben öffnet das AKTUELLE Puzzle im Analysemodus.
 * - „Letztes Puzzle analysieren" öffnet das zuletzt GELÖSTE Puzzle (bleibt nach dem
 *   Auto-Advance verfügbar, da die lastSolved*-Felder dort gemerkt werden).
 * Rücksprungziel ist jeweils der Endless-Modus.
 */
/** Synchroner Observable-Stub: ruft next sofort auf (für getRandomBatch/recordSessionToServer). */
function sub(value: any): any {
  return {
    subscribe: (h: any) => {
      const next = typeof h === 'function' ? h : h?.next;
      if (next) next(value);
      return { unsubscribe() {} };
    },
  };
}

const PUZZLE = { id: 7, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5 g1f3', rating: 1500 };

/** Vordefinierte Kette, die getRandomBatch im Test zurückgibt. */
const CHAIN = [
  { id: 100, lichessId: 'a', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 700 },
  { id: 101, lichessId: 'b', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 900 },
  { id: 102, lichessId: 'c', fen: PUZZLE.fen, moves: PUZZLE.moves, rating: 1100 },
];

function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = {
    getRatingRange: () => ({ subscribe: () => {} }),
    getRandomBatch: jasmine.createSpy('getRandomBatch').and.callFake(() => sub(CHAIN.map(p => ({ ...p })))),
    recordAttempt: () => sub(null),
    recordAnonymousAttempt: () => sub(null),
    ensureSessionId: () => 'sess',
    getAllThemes: () => sub(['advancedPawn', 'backRankMate', 'endgame', 'fork', 'pin']),
  };
  const storage: any = {
    loadConfig: (c: any) => c,
    loadHighscore: () => 0,
    loadSessionHistory: () => [],
    loadOfflinePool: () => [],
    loadActiveGameLocal: () => null,
    saveActiveGameLocal: () => {},
    saveOfflinePool: () => {},
    saveChainSeed: () => {},
    loadChainSeed: () => '',
    saveConfig: () => {},
    saveProgressToServer: () => {},
    saveProgressImmediate: () => {},
    checkHighscore: (max: number, hs: number) => ({ highscore: Math.max(max, hs), isNew: max > hs }),
    recordSession: (hist: any[]) => hist,
    recordSessionToServer: () => sub(null),
    loadFromServer: () => ({ subscribe: () => {} }),   // async Merge: im Test no-op
  };
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { queryParamMap: { get: () => null } } };
  const dialog: any = {};
  const translate: any = { instant: (k: string) => k };
  const offline: any = { puzzleCount: 0, endlessRuns: 0 };
  const snackBar: any = { info: jasmine.createSpy('info') };
  const offlineQueue: any = { enqueue: jasmine.createSpy('enqueue') };
  return new EndlessPuzzleComponent(
    puzzleService, stockfish, storage, auth, prefs, router, route, dialog, translate, offline, snackBar, offlineQueue
  );
}

describe('EndlessPuzzleComponent analyse', () => {
  it('analyzeCurrentPuzzle opens the current puzzle in the analysis board (give-up case)', () => {
    const c = makeComponent();
    c.puzzle = { ...PUZZLE };
    c.orientation = 'black';

    c.analyzeCurrentPuzzle();

    expect(c.router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'black', from: '/puzzles/endless?resume=1' },
    });
  });

  it('reviewLastPuzzle opens the last solved puzzle (survives auto-advance)', () => {
    const c = makeComponent();
    // Zustand wie nach einem gelösten Puzzle (puzzleSolved merkt sich id/fen/moves/orientation):
    c.lastSolvedPuzzleId = 7;
    c.lastSolvedFen = PUZZLE.fen;
    c.lastSolvedMoves = PUZZLE.moves;
    c.lastSolvedOrientation = 'white';

    c.reviewLastPuzzle();

    expect(c.router.navigate).toHaveBeenCalledWith(['/analysis'], {
      queryParams: { fen: PUZZLE.fen, moves: 'e2e4,e7e5,g1f3', orientation: 'white', from: '/puzzles/endless?resume=1' },
    });
  });

  it('reviewLastPuzzle does nothing when no puzzle has been solved yet', () => {
    const c = makeComponent();
    c.lastSolvedPuzzleId = null;
    c.lastSolvedFen = null;

    c.reviewLastPuzzle();

    expect(c.router.navigate).not.toHaveBeenCalled();
  });

  // Bug: nach Game-Over + „Nochmal spielen" konnte der beendete Run erneut fortgesetzt
  // werden, weil playAgain den in-memory activeGameState nicht löschte (nur der Storage
  // war genullt). Der Config-Screen zeigte dann wieder den Resume-Banner.
  it('playAgain clears the finished run so it cannot be resumed again', () => {
    const c = makeComponent();
    c.activeGameState = { lives: 1, solved: 5, level: 3, currentMinRating: 1800, maxRatingReached: 1800 };
    c.state = 'GAME_OVER';

    c.playAgain();

    expect(c.state).toBe('CONFIG');
    expect(c.activeGameState).toBeNull();
  });
});

describe('EndlessPuzzleComponent Themen-Multiselect', () => {
  it('selectedThemes liest die leerzeichengetrennten Themen aus der Config', () => {
    const c = makeComponent();
    c.config.themes = 'fork pin';
    expect(c.selectedThemes).toEqual(['fork', 'pin']);
  });

  it('addThemeValue hängt ein Thema an und schreibt es als String zurück (keine Duplikate)', () => {
    const c = makeComponent();
    c.config.themes = 'fork';
    (c as any).addThemeValue('pin');
    expect(c.config.themes).toBe('fork pin');
    (c as any).addThemeValue('pin');   // Duplikat ignoriert
    expect(c.config.themes).toBe('fork pin');
  });

  it('removeTheme entfernt das Thema aus dem String', () => {
    const c = makeComponent();
    c.config.themes = 'fork pin endgame';
    c.removeTheme('pin');
    expect(c.config.themes).toBe('fork endgame');
  });

  it('filteredThemes blendet bereits gewählte aus und filtert nach dem Suchtext', () => {
    const c = makeComponent();
    c.allThemes = ['advancedPawn', 'backRankMate', 'endgame', 'fork', 'pin'];
    c.config.themes = 'fork';        // fork ist gewählt → raus aus Vorschlägen
    c.themeInput = 'ba';             // Suchtext
    expect(c.filteredThemes).toEqual(['backRankMate']);
  });

  it('onThemeInputTokenEnd übernimmt frei getippte Themen und leert das Eingabefeld', () => {
    const c = makeComponent();
    c.config.themes = '';
    const clear = jasmine.createSpy('clear');
    (c as any).onThemeInputTokenEnd({ value: 'zwischenzug', chipInput: { clear } });
    expect(c.config.themes).toBe('zwischenzug');
    expect(clear).toHaveBeenCalled();
    expect(c.themeInput).toBe('');
  });
});

/** Tijdelijk navigator.onLine = false innerhalb von fn(). */
function withOffline(fn: () => void): void {
  Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false });
  try { fn(); } finally { delete (navigator as any).onLine; }
}

describe('EndlessPuzzleComponent gauntlet (Kette)', () => {
  it('startGame generiert die Kette und lädt das erste Ketten-Puzzle', () => {
    const c = makeComponent();
    c.startGame();
    expect(c['puzzleService'].getRandomBatch).toHaveBeenCalled();
    expect(c['chain'].length).toBe(3);
    expect(c.chainIndex).toBe(0);
    expect(c.puzzle.id).toBe(100);   // chain[0]
    c.ngOnDestroy();
  });

  it('Lösen rückt eine Stelle in der Kette weiter', () => {
    const c = makeComponent();
    c.startGame();
    c.continueAfterSolve();
    expect(c.chainIndex).toBe(1);
    expect(c.puzzle.id).toBe(101);   // chain[1]
    c.ngOnDestroy();
  });

  it('Ein Fehler kostet ein Leben UND rückt weiter (Gauntlet)', () => {
    const c = makeComponent();
    c.startGame();
    expect(c.lives).toBe(3);
    c['loseLife']();                 // Fehler: Leben -1, Status FAILED
    expect(c.lives).toBe(2);
    expect(c.state).toBe('FAILED');
    c.continueAfterWrong();          // weiter zum nächsten (höheren) Puzzle
    expect(c.chainIndex).toBe(1);
    expect(c.puzzle.id).toBe(101);
    c.ngOnDestroy();
  });

  it('continueAfterWrong bei 0 Leben beendet das Spiel (Game Over)', () => {
    const c = makeComponent();
    c.startGame();
    c.lives = 0;
    c.continueAfterWrong();
    expect(c.state).toBe('GAME_OVER');
    expect(c.chainIndex).toBe(0);    // kein Weiterrücken mehr
    c.ngOnDestroy();
  });

  it('retry erlaubt auch beim letzten verlorenen Leben einen Neuversuch desselben Puzzles', () => {
    const c = makeComponent();
    c.startGame();
    c.lives = 1;
    c['loseLife']();                 // letztes Leben weg → lives 0, Status FAILED
    expect(c.lives).toBe(0);
    expect(c.state).toBe('FAILED');

    c.retry();                       // früher: no-op bei 0 Leben; jetzt: Puzzle erneut aufsetzen
    expect(c.state).not.toBe('FAILED');   // wieder spielbar (SETUP/AWAITING)
    expect(c.chainIndex).toBe(0);    // kein Weiterrücken
    expect(c.puzzle.id).toBe(100);   // dasselbe Puzzle, an dem der Run scheiterte
    expect(c.lives).toBe(0);         // Retry kostet kein weiteres Leben
    c.ngOnDestroy();
  });

  it('Kette offline durchgespielt → „You win"', () => {
    const c = makeComponent();
    c['chain'] = [{ ...CHAIN[0] }];
    c.chainIndex = 1;                // hinter dem Kettenende
    withOffline(() => c['loadCurrent']());
    expect(c.state).toBe('WON');
    c.ngOnDestroy();
  });

  it('Fortsetzen stellt bei passendem Seed das aktuelle Ketten-Puzzle wieder her', () => {
    const c = makeComponent();
    c['storage'].loadChainSeed = () => 'seed-xyz';
    c['offlinePool'] = CHAIN.map(p => ({ ...p }));
    c.activeGameState = { lives: 2, solved: 2, chainIndex: 2, seed: 'seed-xyz', maxRatingReached: 1100 };
    c.resumeGame();
    expect(c.chainIndex).toBe(2);
    expect(c.puzzle.id).toBe(102);   // exakt dasselbe Puzzle wie vor dem Refresh
    c.ngOnDestroy();
  });

  it('startGame vergibt einen eindeutigen Seed und schreibt Seed + Ketten-IDs in den Session-Record', () => {
    const c = makeComponent();
    const sessions: any[] = [];
    c['storage'].recordSessionToServer = (s: any) => { sessions.push(s); return sub(null); };
    c.startGame();
    expect(c['seed']).toBeTruthy();
    const seed = c['seed'];
    // Lauf beenden (Game Over) → Session wird mit Seed + Ketten-IDs aufgezeichnet.
    c.lives = 0;
    c.continueAfterWrong();
    expect(c.state).toBe('GAME_OVER');
    expect(sessions.length).toBe(1);
    expect(sessions[0].seed).toBe(seed);
    expect(sessions[0].chainPuzzleIds).toBe('100,101,102');   // geordnete Ketten-IDs
    c.ngOnDestroy();
  });
});
