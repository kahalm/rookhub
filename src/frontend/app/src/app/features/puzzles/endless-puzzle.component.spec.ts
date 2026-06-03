import { EndlessPuzzleComponent } from './endless-puzzle.component';

/**
 * Fokussierter Test der Analyse-Navigation im Endless-Modus (ohne TestBed/Template):
 * - „Analysieren" beim Aufgeben öffnet das AKTUELLE Puzzle im Analysemodus.
 * - „Letztes Puzzle analysieren" öffnet das zuletzt GELÖSTE Puzzle (bleibt nach dem
 *   Auto-Advance verfügbar, da die lastSolved*-Felder dort gemerkt werden).
 * Rücksprungziel ist jeweils der Endless-Modus.
 */
function makeComponent(): any {
  const prefs: any = { boardTheme: 'green', pieceSet: 'cburnett', themeMode: 'fixed', stockfishDepth: 12, visualization: 0 };
  const stockfish: any = { init: () => Promise.resolve(), getEval: () => Promise.resolve('') };
  const auth: any = { isLoggedIn: false };
  const puzzleService: any = { getRatingRange: () => ({ subscribe: () => {} }) };
  const storage: any = {
    loadConfig: (c: any) => c,
    loadHighscore: () => 0,
    loadSessionHistory: () => [],
    loadOfflinePool: () => [],
    loadActiveGameLocal: () => null,
    saveActiveGameLocal: () => {},
    loadFromServer: () => ({ subscribe: () => {} }),   // async Merge: im Test no-op
  };
  const router: any = { navigate: jasmine.createSpy('navigate') };
  const route: any = { snapshot: { queryParamMap: { get: () => null } } };
  const dialog: any = {};
  const translate: any = {};
  const offline: any = { puzzleCount: 0, endlessRuns: 0 };
  const snackBar: any = {};
  const offlineQueue: any = { enqueue: jasmine.createSpy('enqueue') };
  return new EndlessPuzzleComponent(
    puzzleService, stockfish, storage, auth, prefs, router, route, dialog, translate, offline, snackBar, offlineQueue
  );
}

const PUZZLE = { id: 7, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: 'e2e4 e7e5 g1f3', rating: 1500 };

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
});
